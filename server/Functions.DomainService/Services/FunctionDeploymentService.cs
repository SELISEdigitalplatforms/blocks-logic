using FluentValidation;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Dtos.Responses;
using Functions.DomainService.Entities;
using Blocks.Genesis;
using Functions.DomainService.Enums;
using Functions.DomainService.Queue;
using Functions.DomainService.Repositories;
using Functions.DomainService.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using StackExchange.Redis;

namespace Functions.DomainService.Services
{
    public interface IFunctionDeploymentService
    {
        Task<FunctionVersionSummaryDto> DeployAsync(
            string tenantId, DeployFunctionRequestDto request, string? actorId, string? actorEmail,
            CancellationToken cancellationToken = default);

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
        private readonly Storage.IFunctionArtifactStore? _artifacts;

        /// <summary>Warm sandboxes asked for per deploy when <c>Functions:PrewarmCount</c> is not set.</summary>
        internal const int DefaultPrewarmCount = 1;

        /// <summary>
        /// Upper bound on <see cref="FunctionQueueKeys.WarmStream"/>'s length, applied on every
        /// write (approximate MAXLEN). This trim is the <b>only</b> one the stream gets: each runner
        /// reads it through its own consumer group and only acknowledges — never deletes — so that
        /// every host sees every drain. Without it the stream would grow for ever. An entry is
        /// advisory and useful for seconds, so the last ~1000 deploys are far more than any runner
        /// that is up needs; one that was down misses old pre-warms, which only costs a cold start.
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
            Storage.IFunctionArtifactStore? artifacts = null)
        {
            _artifacts = artifacts;
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

        public async Task<FunctionVersionSummaryDto> DeployAsync(
            string tenantId, DeployFunctionRequestDto request, string? actorId, string? actorEmail,
            CancellationToken cancellationToken = default)
        {
            await ValidateAsync(_deployValidator, request);

            var function = await _functionRepository.GetByIdAsync(tenantId, request.FunctionId, cancellationToken)
                ?? throw new FunctionNotFoundException($"function '{request.FunctionId}' was not found");

            var build = await _buildService.EnsureImageAsync(
                tenantId, function, cancellationToken, waitSecondsOverride: null, request.Rebuild);
            // A build is usable if it produced either an artifact or a registry image. Both are
            // accepted so a runner on the old path and one on the new path can deploy side by side
            // during the cutover; neither being present means the build gave us nothing to run.
            var hasArtifact = !string.IsNullOrEmpty(build.ArtifactSha256);
            if (build.Status != BuildStatus.Succeeded || (string.IsNullOrEmpty(build.ImageDigest) && !hasArtifact))
            {
                throw new FunctionValidationException(
                    build.Status is BuildStatus.Queued or BuildStatus.Building
                        ? "the build has not finished yet; try deploying again shortly"
                        : $"the build failed: {build.ErrorMessage ?? "unknown error"}");
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
                    Limits = function.Limits.Clamp(),
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

            await TryPublishPrewarmAsync(tenantId, created, previousVersionId);

            return FunctionVersionSummaryDto.From(created);
        }

        /// <summary>
        /// Tells the runners about a version that has just gone Live (sandbox/REUSE.md, "Pre-warm"):
        /// start warm sandboxes for it when it opted into <c>ReuseSandbox</c>, and drain the idle
        /// warm sandboxes of the version it replaced.
        /// <list type="bullet">
        /// <item>New version reuses → one entry, <c>count</c> = <c>Functions:PrewarmCount</c>
        /// (default 1; ≤ 0 starts none but still drains), <c>drainVersionId</c> = the previous
        /// version.</item>
        /// <item>New version does not reuse, the previous one did → one entry with <c>count=0</c>
        /// and <c>drainVersionId</c> = the previous version, so turning the switch off does not
        /// leave its warm sandboxes idling until they age out.</item>
        /// <item>Neither reuses → nothing at all: a function that never touches the switch deploys
        /// exactly as before, without even the lookup of its previous version.</item>
        /// </list>
        /// <para>
        /// <b>Best effort, never part of the deploy's outcome.</b> The version is already Live when
        /// this runs; a pre-warm only saves the first caller a cold start, and a missed drain only
        /// leaves idle sandboxes until their idle timeout. Failing the deploy for either — a Redis or
        /// Mongo blip after the pointer moved — would report a deploy as failed that in fact
        /// succeeded, so a failure is logged and swallowed. Not cancellable for the same reason.
        /// </para>
        /// </summary>
        private async Task TryPublishPrewarmAsync(string tenantId, FunctionVersionEntity version, string? previousVersionId)
        {
            var drain = string.IsNullOrEmpty(previousVersionId)
                || string.Equals(previousVersionId, version.ItemId, StringComparison.Ordinal)
                ? string.Empty
                : previousVersionId;

            try
            {
                // Reuse is always on for deployed functions (2026-10-06), so every deploy pre-warms the
                // new version and drains the one it replaced.
                var count = Math.Max(0, _configuration.GetValue("Functions:PrewarmCount", DefaultPrewarmCount));
                if (count == 0 && drain.Length == 0) return;

                var entry = new List<NameValueEntry>
                {
                    new("tenantId", tenantId),
                    new("functionId", version.FunctionId),
                    new("versionId", version.ItemId),
                    new("image", FunctionRunImage.For(version)),
                    new("count", count),
                    new("drainVersionId", drain),
                };

                // An artifact-built version exists on no registry: without the artifact's address a
                // runner cannot make its image ahead of the first call, and the pre-warm did nothing
                // (seen 2026-10-06 — the first call after every deploy was cold). Same signed,
                // read-only URL and hash a run carries. Best effort: no URL → no pre-warm, as before.
                if (count > 0 && !string.IsNullOrEmpty(version.ArtifactId) && _artifacts is not null)
                {
                    try
                    {
                        var url = await _artifacts.CreateDownloadUrlAsync(
                            tenantId, version.ArtifactId!, FunctionInvocationService.ArtifactDownloadWindow,
                            CancellationToken.None).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(url))
                        {
                            entry.Add(new NameValueEntry(FunctionQueueKeys.RunArtifactUrlField, url));
                            entry.Add(new NameValueEntry(
                                FunctionQueueKeys.RunArtifactSha256Field, version.ArtifactSha256 ?? string.Empty));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("No artifact URL for pre-warming version {VersionId} ({Type})",
                            version.ItemId, ex.GetType().Name);
                    }
                }

                await _cache.CacheDatabase().StreamAddAsync(
                    FunctionQueueKeys.WarmStream,
                    [.. entry],
                    maxLength: WarmStreamMaxLength,
                    useApproximateMaxLength: true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not publish the pre-warm/drain request for function {FunctionId} version {VersionId}; "
                    + "the deploy stands and the first call will start a sandbox cold",
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
