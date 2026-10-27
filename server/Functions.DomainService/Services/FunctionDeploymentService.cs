using FluentValidation;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Dtos.Responses;
using Blocks.Genesis;
using Functions.DomainService.Queue;
using Functions.DomainService.Repositories;
using Functions.DomainService.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using StackExchange.Redis;
using Blocks.Genesis.Entities.Logic;

namespace Functions.DomainService.Services
{
    /// <summary>
    /// A deploy's outcome: the new version, or — when its build outlasted the deploy's wait — the
    /// build still running, to watch and then deploy with <see cref="DeployFunctionRequestDto.BuildId"/>.
    /// </summary>
    public sealed record DeployOutcome(FunctionVersionSummaryDto? Version, string? BuildId = null, string? BuildStatus = null)
    {
        public bool IsPending => Version is null;
    }

    public interface IFunctionDeploymentService
    {
        Task<DeployOutcome> DeployAsync(
            string tenantId, DeployFunctionRequestDto request, string? actorId, string? actorEmail,
            CancellationToken cancellationToken = default);

        /// <summary>The build a deploy of the function's current code still waits for; null fields when there is none.</summary>
        Task<PendingDeployDto> GetPendingAsync(
            string tenantId, string functionId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Deploy creates an immutable snapshot of the function and points it at that snapshot;
    /// Rollback only ever moves the pointer. Neither ever mutates a version already created —
    /// that immutability is what makes a version a safe rollback target in the first place, and
    /// what lets a run keep behaving the way it did even after the function is edited further.
    /// </summary>
    public class FunctionDeploymentService : IFunctionDeploymentService
    {
        // A concurrent Deploy of the same function is the only case that can race the unique
        // (FunctionId, Number) index; one retry is enough because losing the race means someone
        // else's version now holds the number this one was about to use.
        private const int MaxVersionNumberRetries = 3;

        private readonly IFunctionRepository _functionRepository;
        private readonly IFunctionVersionRepository _versionRepository;
        private readonly IFunctionBuildService _buildService;
        private readonly IFunctionVersionRetentionService _retentionService;
        private readonly IFunctionImagePinService _imagePins;
        private readonly IFunctionAuditService _auditService;
        private readonly IValidator<DeployFunctionRequestDto> _deployValidator;
        private readonly ICacheClient _cache;
        private readonly IConfiguration _configuration;
        private readonly ILogger<FunctionDeploymentService> _logger;
        private readonly IFunctionEventSettingsRepository? _eventSettings;

        /// <summary>
        /// Upper bound on <see cref="FunctionQueueKeys.WarmStream"/>'s length, applied on every
        /// write (approximate MAXLEN). This trim is the <b>only</b> one the stream gets: each runner
        /// reads it through its own consumer group and only acknowledges — never deletes — so that
        /// every host sees every drain. Without it the stream would grow for ever. An entry is
        /// advisory and useful for seconds, so the last ~1000 deploys are far more than any runner
        /// that is up needs; one that was down misses old drains, and its clean-up removes those idle sandboxes anyway.
        /// </summary>
        internal const int WarmStreamMaxLength = 1000;

        public FunctionDeploymentService(
            IFunctionRepository functionRepository,
            IFunctionVersionRepository versionRepository,
            IFunctionBuildService buildService,
            IFunctionVersionRetentionService retentionService,
            IFunctionImagePinService imagePins,
            IFunctionAuditService auditService,
            IValidator<DeployFunctionRequestDto> deployValidator,
            ICacheClient cache,
            IConfiguration configuration,
            ILogger<FunctionDeploymentService> logger,
            IFunctionEventSettingsRepository? eventSettings = null)
        {
            _eventSettings = eventSettings;
            _functionRepository = functionRepository;
            _versionRepository = versionRepository;
            _buildService = buildService;
            _retentionService = retentionService;
            _imagePins = imagePins;
            _auditService = auditService;
            _deployValidator = deployValidator;
            _cache = cache;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<DeployOutcome> DeployAsync(
            string tenantId, DeployFunctionRequestDto request, string? actorId, string? actorEmail,
            CancellationToken cancellationToken = default)
        {
            await ValidateAsync(_deployValidator, request);

            var function = await _functionRepository.GetByIdAsync(tenantId, request.FunctionId, cancellationToken)
                ?? throw new FunctionNotFoundException($"function '{request.FunctionId}' was not found");

            // The request waits for the build only a short while (F-4): a proxy in front of the Api
            // cuts requests at ~60 s, and a build can take minutes. A cached image or a fast build
            // still answers with the version; a longer one answers 202 with the build to watch.
            FunctionBuildEntity build;
            if (!string.IsNullOrEmpty(request.BuildId))
            {
                build = await _buildService.GetAsync(tenantId, request.BuildId, cancellationToken);
                if (build.FunctionId != function.ItemId || build.Ephemeral)
                {
                    throw new FunctionValidationException("that build is not a deployable build of this function");
                }
                if (build.SourceHash != function.SourceHash)
                {
                    throw new FunctionValidationException(
                        "the code changed after this build started; deploy again to build the current code");
                }
            }
            else
            {
                build = await _buildService.EnsureImageAsync(
                    tenantId, function, cancellationToken,
                    waitSecondsOverride: _configuration.GetValue("Functions:DeployWaitSeconds", 20), request.Rebuild);
            }

            if (build.Status is BuildStatus.Queued or BuildStatus.Building)
            {
                return new DeployOutcome(null, build.ItemId, build.Status.ToString());
            }

            // A deploy that names its build can be sent twice: the page that clicked Deploy and a page reloaded meanwhile
            // both watch the build. The second one gets the version the first one made instead of a copy of it.
            if (!string.IsNullOrEmpty(request.BuildId)
                && await GetActiveVersionFromAsync(tenantId, function, build, cancellationToken) is { } deployed)
            {
                return new DeployOutcome(FunctionVersionSummaryDto.From(deployed));
            }

            // A build is usable if it produced either an artifact or a registry image. Both are
            // accepted so a runner on the old path and one on the new path can deploy side by side
            // during the cutover; neither being present means the build gave us nothing to run.
            var hasArtifact = !string.IsNullOrEmpty(build.ArtifactSha256);
            if (build.Status != BuildStatus.Succeeded || (string.IsNullOrEmpty(build.ImageDigest) && !hasArtifact))
            {
                throw new FunctionBuildFailedException($"the build failed: {build.ErrorMessage ?? "unknown error"}", build.ItemId);
            }

            FunctionVersionEntity? created = null;
            for (var attempt = 0; attempt < MaxVersionNumberRetries && created is null; attempt++)
            {
                var latest = await _versionRepository.GetLatestAsync(tenantId, function.ItemId, cancellationToken);
                var nextNumber = (latest?.Number ?? 0) + 1;

                var version = new FunctionVersionEntity
                {
                    ItemId = Guid.NewGuid().ToString(),
                    CreatedDate = DateTime.UtcNow,
                    LastUpdatedDate = DateTime.UtcNow,
                    CreatedBy = actorId ?? string.Empty,
                    LastUpdatedBy = actorId ?? string.Empty,
                    FunctionId = function.ItemId,
                    Number = nextNumber,
                    ImageDigest = build.ImageDigest ?? string.Empty,

                    // The artifact is addressed by the build that made it, so the version keeps the
                    // build id rather than a separate name. Null on a version built the old way.
                    ArtifactId = hasArtifact ? build.ItemId : null,
                    ArtifactSha256 = build.ArtifactSha256,

                    CodeHash = FunctionHashing.CodeHash(function.Source),
                    ManifestHash = FunctionHashing.ManifestHash(function.Source),
                    Source = function.Source,
                    Limits = function.Limits.ClampKeepingConcurrency(),
                    Retry = function.Retry,
                    Trigger = function.Trigger,
                    OutputActions = function.OutputActions,
                    Variables = function.Variables,
                    Packages = build.Packages,
                    Note = request.Note,
                };

                try
                {
                    await _versionRepository.CreateAsync(tenantId, version, cancellationToken);
                    created = version;
                }
                catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
                {
                    _logger.LogInformation(
                        "Version number {Number} for function {FunctionId} was taken by a concurrent deploy; retrying",
                        nextNumber, function.ItemId);
                }
            }

            if (created is null)
            {
                throw new FunctionValidationException("could not deploy: too many concurrent deployments of this function");
            }

            // The events this version may send are a copy of the draft's, taken now, so the version's list never changes
            // at run time. Before the pointer moves: a copy that fails fails the deploy, and no run uses the version.
            if (_eventSettings is not null)
            {
                await _eventSettings.CopyDraftToVersionAsync(tenantId, function.ItemId, created.ItemId, cancellationToken);
            }

            // Pinned here rather than when the image was built: a build is not a promise that
            // anything will use its image, but a version is. Until this point the image is kept
            // alive only by Image GC's grace window, and a version whose image was reclaimed
            // before it got here would be rebuilt rather than lost.
            await _imagePins.PinAsync(created.ImageDigest, cancellationToken);

            // Read before the pointer moves: the version this deploy replaces, whose idle warm
            // sandboxes the runners can now let go of.
            var previousVersionId = function.ActiveVersionId;

            function.ActiveVersionId = created.ItemId;
            function.LastVersionNumber = created.Number;
            function.Status = FunctionStatus.Live;
            function.LastDeployedAt = DateTime.UtcNow;
            function.LastUpdatedDate = DateTime.UtcNow;
            function.LastUpdatedBy = actorId ?? string.Empty;
            function.IsDirty = false;
            await _functionRepository.SetActiveVersionAsync(
                tenantId, function.ItemId, created.ItemId, created.Number, function.LastDeployedAt.Value,
                actorId, cancellationToken);

            await _auditService.RecordAsync(
                tenantId, function.ItemId, FunctionsConstants.AuditActions.Deployed, actorId, actorEmail,
                new { Version = created.Number, created.ImageDigest }, cancellationToken);

            // History is bounded here rather than by a sweep: a deploy is the only thing that
            // adds a version, so it is the only moment the cap can be exceeded.
            await _retentionService.PruneAsync(tenantId, function.ItemId, created.ItemId, cancellationToken);

            await TryPublishDrainAsync(tenantId, created, previousVersionId);

            return new DeployOutcome(FunctionVersionSummaryDto.From(created));
        }

        public async Task<PendingDeployDto> GetPendingAsync(
            string tenantId, string functionId, CancellationToken cancellationToken = default)
        {
            var function = await _functionRepository.GetByIdAsync(tenantId, functionId, cancellationToken)
                ?? throw new FunctionNotFoundException($"function '{functionId}' was not found");
            var build = await _buildService.GetPendingAsync(tenantId, function, cancellationToken);
            return new PendingDeployDto { BuildId = build?.ItemId, Status = build?.Status.ToString() };
        }

        /// <summary>The function's active version when it was made from <paramref name="build"/>; else null.</summary>
        private async Task<FunctionVersionEntity?> GetActiveVersionFromAsync(
            string tenantId, FunctionEntity function, FunctionBuildEntity build, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(function.ActiveVersionId)) return null;
            var active = await _versionRepository.GetByIdAsync(tenantId, function.ActiveVersionId, cancellationToken);
            if (active is null) return null;
            var sameArtifact = string.Equals(active.ArtifactId, build.ItemId, StringComparison.Ordinal);
            var sameImage = !string.IsNullOrEmpty(build.ImageDigest)
                && string.Equals(active.ImageDigest, build.ImageDigest, StringComparison.Ordinal);
            return sameArtifact || sameImage ? active : null;
        }

        /// <summary>
        /// Tells the runners that a version has just replaced another (RUNNER-FLOW step 2.2): one entry
        /// on the warm stream asking every runner to drain the idle warm sandboxes of the previous
        /// version. Nothing is pre-warmed (step 2.1): the first call of the new version warms it.
        /// <para>
        /// <b>Best effort, never part of the deploy's outcome.</b> The version is already Live when
        /// this runs; a missed drain only leaves idle sandboxes until the runners' clean-up removes
        /// them (60 s / 600 s idle). Failing the deploy for it — a Redis blip after the pointer moved —
        /// would report a deploy as failed that in fact succeeded, so a failure is logged and
        /// swallowed. Not cancellable for the same reason.
        /// </para>
        /// </summary>
        private async Task TryPublishDrainAsync(string tenantId, FunctionVersionEntity version, string? previousVersionId)
        {
            if (string.IsNullOrEmpty(previousVersionId)
                || string.Equals(previousVersionId, version.ItemId, StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                await _cache.CacheDatabase().StreamAddAsync(
                    FunctionQueueKeys.WarmStream,
                    [
                        new("tenantId", tenantId),
                        new("functionId", version.FunctionId),
                        new("versionId", version.ItemId),
                        new("drainVersionId", previousVersionId),
                    ],
                    maxLength: WarmStreamMaxLength,
                    useApproximateMaxLength: true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not publish the drain request for function {FunctionId} version {VersionId}; "
                    + "the deploy stands and the old version's idle sandboxes go on their own",
                    version.FunctionId, version.ItemId);
            }
        }

        private static async Task ValidateAsync<T>(IValidator<T> validator, T instance)
        {
            var result = await validator.ValidateAsync(instance);
            if (!result.IsValid)
            {
                throw new FunctionValidationException(string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
            }
        }
    }
}
