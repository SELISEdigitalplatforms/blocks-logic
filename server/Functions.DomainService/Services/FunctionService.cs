using FluentValidation;
using Microsoft.Extensions.Configuration;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Dtos.Responses;
using Functions.DomainService.Entities;
using Functions.DomainService.Models;
using Functions.DomainService.Repositories;
using Functions.DomainService.Utils;
using Microsoft.Extensions.Logging;

namespace Functions.DomainService.Services
{
    public interface IFunctionService
    {
        Task<FunctionDetailDto> CreateAsync(
            string tenantId, CreateFunctionRequestDto request, string? actorId, string? actorEmail,
            CancellationToken cancellationToken = default);

        Task<FunctionDetailDto> UpdateAsync(
            string tenantId, UpdateFunctionRequestDto request, string? actorId, string? actorEmail,
            CancellationToken cancellationToken = default);

        Task<FunctionDetailDto> SaveAsync(
            string tenantId, SaveFunctionRequestDto request, string? actorId, string? actorEmail,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a function. Refuses while a workflow still has a step pointing at it, unless
        /// <paramref name="force"/> says to delete it anyway. Returns once the delete is accepted:
        /// the function is gone to every reader from that moment, and everything it owned —
        /// versions, builds, runs, logs, stats, in-flight work, pinned images — is purged in the
        /// background by <c>FunctionDeletionWorker</c>.
        /// </summary>
        Task<bool> DeleteAsync(
            string tenantId, string functionId, string? actorId, string? actorEmail,
            bool force = false,
            CancellationToken cancellationToken = default);

        Task<(IReadOnlyList<FunctionSummaryDto> Items, long TotalCount)> GetAllAsync(
            string tenantId, GetFunctionsRequestDto request, CancellationToken cancellationToken = default);

        Task<FunctionDetailDto> GetAsync(string tenantId, string functionId, CancellationToken cancellationToken = default);

        /// <summary>The raw entity, for other services in this module — never returned across the API boundary.</summary>
        Task<FunctionEntity> GetEntityAsync(string tenantId, string functionId, CancellationToken cancellationToken = default);

        Task<FunctionLimitsOptionsDto> GetLimitsOptionsAsync();

        Task<(IReadOnlyList<FunctionVersionSummaryDto> Items, long TotalCount)> GetVersionsAsync(
            string tenantId, string functionId, int pageNumber, int pageSize, CancellationToken cancellationToken = default);

        /// <summary>The source a specific version was built from — for the versions tab's "view source".</summary>
        Task<FunctionSource> GetVersionSourceAsync(
            string tenantId, string functionId, string versionId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// CRUD and the editor's Save, plus dirty detection.
    /// <para>
    /// "Dirty" means the editor's current source differs from what the active version was
    /// built from — checked by comparing the code and manifest hashes independently rather
    /// than a single combined hash, because those are exactly the two identities
    /// <see cref="FunctionBuildService"/> already needs for its own cache key (DECISIONS D3);
    /// keeping one derivation avoids a second hash field that could drift from it.
    /// </para>
    /// </summary>
    public class FunctionService : IFunctionService
    {
        private readonly IFunctionRepository _repository;
        private readonly IFunctionVersionRepository _versionRepository;
        private readonly IFunctionRunRepository _runRepository;
        private readonly IFunctionAuditService _auditService;
        private readonly IFunctionDeletionQueue _deletionQueue;
        private readonly IFunctionUsageService _usageService;
        private readonly IValidator<CreateFunctionRequestDto> _createValidator;
        private readonly IValidator<UpdateFunctionRequestDto> _updateValidator;
        private readonly IValidator<SaveFunctionRequestDto> _saveValidator;
        private readonly ILogger<FunctionService> _logger;
        private readonly IConfiguration _configuration;

        public FunctionService(
            IFunctionRepository repository,
            IFunctionVersionRepository versionRepository,
            IFunctionRunRepository runRepository,
            IFunctionAuditService auditService,
            IFunctionDeletionQueue deletionQueue,
            IFunctionUsageService usageService,
            IValidator<CreateFunctionRequestDto> createValidator,
            IValidator<UpdateFunctionRequestDto> updateValidator,
            IValidator<SaveFunctionRequestDto> saveValidator,
            ILogger<FunctionService> logger,
            IConfiguration configuration)
        {
            _configuration = configuration;
            _repository = repository;
            _versionRepository = versionRepository;
            _runRepository = runRepository;
            _auditService = auditService;
            _deletionQueue = deletionQueue;
            _usageService = usageService;
            _createValidator = createValidator;
            _updateValidator = updateValidator;
            _saveValidator = saveValidator;
            _logger = logger;
        }

        public async Task<FunctionDetailDto> CreateAsync(
            string tenantId, CreateFunctionRequestDto request, string? actorId, string? actorEmail,
            CancellationToken cancellationToken = default)
        {
            await ValidateAsync(_createValidator, request);

            var function = new FunctionEntity
            {
                ItemId = Guid.NewGuid().ToString(),
                CreatedDate = DateTime.UtcNow,
                LastUpdatedDate = DateTime.UtcNow,
                CreatedBy = actorId ?? string.Empty,
                LastUpdatedBy = actorId ?? string.Empty,
                Name = request.Name,
                Description = request.Description,
                Source = FunctionStarterTemplates.For(request.Template),
            };
            function.SourceHash = FunctionHashing.SourceHash(function.Source);
            function.IsDirty = true;

            await _repository.CreateAsync(tenantId, function, cancellationToken);
            await _auditService.RecordAsync(
                tenantId, function.ItemId, FunctionsConstants.AuditActions.Created, actorId, actorEmail,
                new { function.Name }, cancellationToken);

            return FunctionDetailDto.From(function);
        }

        public async Task<FunctionDetailDto> UpdateAsync(
            string tenantId, UpdateFunctionRequestDto request, string? actorId, string? actorEmail,
            CancellationToken cancellationToken = default)
        {
            await ValidateAsync(_updateValidator, request);

            var function = await GetEntityAsync(tenantId, request.FunctionId, cancellationToken);
            function.Name = request.Name;
            function.Description = request.Description;
            function.LastUpdatedDate = DateTime.UtcNow;
            function.LastUpdatedBy = actorId ?? string.Empty;

            await _repository.UpdateDetailsAsync(
                tenantId, function.ItemId, request.Name, request.Description, actorId, cancellationToken);
            await _auditService.RecordAsync(
                tenantId, function.ItemId, FunctionsConstants.AuditActions.Updated, actorId, actorEmail,
                cancellationToken: cancellationToken);

            return await BuildDetailAsync(tenantId, function, cancellationToken);
        }

        public async Task<FunctionDetailDto> SaveAsync(
            string tenantId, SaveFunctionRequestDto request, string? actorId, string? actorEmail,
            CancellationToken cancellationToken = default)
        {
            await ValidateAsync(_saveValidator, request);

            var function = await GetEntityAsync(tenantId, request.FunctionId, cancellationToken);

            function.Source = new FunctionSource
            {
                IndexJs = request.IndexJs,
                PackageJson = request.PackageJson,
                LockJson = request.LockJson,
                AllowInstallScripts = request.AllowInstallScripts,
            };
            function.SourceHash = FunctionHashing.SourceHash(function.Source);
            // Stored as the platform profile, not as sent. The values are fixed, so keeping a
            // caller's numbers would make the saved document disagree with what actually runs.
            function.Limits = request.Limits.Clamp();
            function.Retry = RetryPolicy.Fixed;
            function.Trigger = NormalizeForStorage(request.Trigger);
            function.OutputActions = request.OutputActions;
            function.Variables = request.Variables;
            function.LastUpdatedDate = DateTime.UtcNow;
            function.LastUpdatedBy = actorId ?? string.Empty;

            await _repository.UpdateSourceAndConfigAsync(
                tenantId, function.ItemId, function.Source, function.SourceHash,
                function.Limits, function.Retry, function.Trigger, function.OutputActions, function.Variables,
                actorId, cancellationToken);
            await _auditService.RecordAsync(
                tenantId, function.ItemId, FunctionsConstants.AuditActions.Saved, actorId, actorEmail,
                cancellationToken: cancellationToken);

            return await BuildDetailAsync(tenantId, function, cancellationToken);
        }

        public async Task<bool> DeleteAsync(
            string tenantId, string functionId, string? actorId, string? actorEmail,
            bool force = false,
            CancellationToken cancellationToken = default)
        {
            var function = await _repository.GetByIdAsync(tenantId, functionId, cancellationToken);
            if (function is null) return false;

            var references = force
                ? []
                : await _usageService.GetWorkflowReferencesAsync(tenantId, functionId, cancellationToken);
            if (references.Count > 0)
            {
                // Refused rather than warned: the step keeps its function id either way, so the
                // only difference is whether the person deleting finds out now or a workflow does
                // at its next run.
                var named = string.Join(", ", references.Take(5).Select(r =>
                    r.IsPublished ? $"'{r.Name}' (published)" : $"'{r.Name}'"));
                var more = references.Count > 5 ? $" and {references.Count - 5} more" : string.Empty;

                throw new FunctionValidationException(
                    $"this function is used by {references.Count} workflow(s): {named}{more}. " +
                    "Remove those steps first, or delete it anyway with force.");
            }

            // The tombstone is the delete. From here the function is invisible to every read and
            // write in the repository, so no new run, build, deploy or save can start against it,
            // and the purge — which can take a while for a function with a long history, and must
            // outlast anything already in flight — happens in the Worker, off this request.
            var accepted = await _repository.MarkDeletedAsync(tenantId, functionId, new FunctionDeletion
            {
                RequestedAt = DateTime.UtcNow,
                RequestedBy = actorId,
                RequestedByEmail = actorEmail,
                Forced = force,
            }, cancellationToken);
            // Someone else's delete got there first: theirs is the one in the trail.
            if (!accepted) return false;

            // The audit trail is deliberately never purged: it is the only remaining record that
            // this function existed at all, and it expires on its own after AuditRetention. The
            // Worker adds a second record, with what was removed, once the purge is finished.
            await _auditService.RecordAsync(
                tenantId, functionId, FunctionsConstants.AuditActions.Deleted, actorId, actorEmail,
                new { Forced = force }, cancellationToken);

            // Only the fast path. Should it fail, the Worker's backstop sweep finds the tombstone.
            await _deletionQueue.EnqueueAsync(tenantId, functionId, cancellationToken);
            return true;
        }

        public async Task<(IReadOnlyList<FunctionSummaryDto> Items, long TotalCount)> GetAllAsync(
            string tenantId, GetFunctionsRequestDto request, CancellationToken cancellationToken = default)
        {
            var (items, totalCount) = await _repository.GetAllAsync(
                tenantId, request.SearchKey, request.Status, request.SortBy,
                request.PageNumber, request.PageSize, cancellationToken);

            var functionIds = items.Select(f => f.ItemId).ToList();

            // One query for the whole page's 24 h counts rather than one per row; the all-time
            // counters are fields of the function itself.
            var runs24h = await _runRepository.CountSinceByFunctionAsync(
                tenantId, functionIds, DateTime.UtcNow.AddHours(-24), cancellationToken);

            var summaries = new List<FunctionSummaryDto>(items.Count);
            foreach (var function in items)
            {
                var activeVersion = await GetActiveVersionAsync(tenantId, function, cancellationToken);
                // Derived, exactly as the detail view does it. Without this the list read the
                // unset default and never showed "unpublished changes" for anything.
                function.IsDirty = IsDirty(function, activeVersion);
                runs24h.TryGetValue(function.ItemId, out var recentRuns);
                summaries.Add(FunctionSummaryDto.From(
                    function, activeVersion?.Number, recentRuns));
            }

            return (summaries, totalCount);
        }

        public async Task<FunctionDetailDto> GetAsync(
            string tenantId, string functionId, CancellationToken cancellationToken = default)
        {
            var function = await GetEntityAsync(tenantId, functionId, cancellationToken);
            return await BuildDetailAsync(tenantId, function, cancellationToken);
        }

        public async Task<FunctionEntity> GetEntityAsync(
            string tenantId, string functionId, CancellationToken cancellationToken = default)
        {
            var function = await _repository.GetByIdAsync(tenantId, functionId, cancellationToken);
            return function ?? throw new FunctionNotFoundException($"function '{functionId}' was not found");
        }

        public Task<FunctionLimitsOptionsDto> GetLimitsOptionsAsync() => Task.FromResult(new FunctionLimitsOptionsDto
        {
            ShowRateLimits = _configuration.GetValue("Functions:RateLimits:ShowInUi", false),
        });

        public async Task<(IReadOnlyList<FunctionVersionSummaryDto> Items, long TotalCount)> GetVersionsAsync(
            string tenantId, string functionId, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            var (items, totalCount) = await _versionRepository.GetAllAsync(
                tenantId, functionId, pageNumber, pageSize, cancellationToken);

            var runCounts = await _runRepository.CountByVersionAsync(
                tenantId, functionId, items.Select(v => v.Number).ToList(), cancellationToken);

            var summaries = items
                .Select(version =>
                {
                    runCounts.TryGetValue(version.Number, out var runCount);
                    return FunctionVersionSummaryDto.From(version, runCount);
                })
                .ToList();

            return (summaries, totalCount);
        }

        public async Task<FunctionSource> GetVersionSourceAsync(
            string tenantId, string functionId, string versionId, CancellationToken cancellationToken = default)
        {
            var version = await _versionRepository.GetByIdAsync(tenantId, versionId, cancellationToken);
            if (version is null || version.FunctionId != functionId)
            {
                throw new FunctionNotFoundException($"function '{functionId}' has no version '{versionId}'");
            }
            return version.Source;
        }

        private async Task<FunctionDetailDto> BuildDetailAsync(
            string tenantId, FunctionEntity function, CancellationToken cancellationToken)
        {
            var activeVersion = await GetActiveVersionAsync(tenantId, function, cancellationToken);
            function.IsDirty = IsDirty(function, activeVersion);

            var dto = FunctionDetailDto.From(function);
            dto.ActiveVersionNumber = activeVersion?.Number;
            return dto;
        }

        private async Task<FunctionVersionEntity?> GetActiveVersionAsync(
            string tenantId, FunctionEntity function, CancellationToken cancellationToken)
            => string.IsNullOrEmpty(function.ActiveVersionId)
                ? null
                : await _versionRepository.GetByIdAsync(tenantId, function.ActiveVersionId, cancellationToken);

        /// <summary>
        /// True when the editor's source is not what the active version was built from. A
        /// function with no active version is always dirty: nothing is deployed yet.
        /// </summary>
        internal static bool IsDirty(FunctionEntity function, FunctionVersionEntity? activeVersion)
        {
            if (activeVersion is null) return true;

            return FunctionHashing.CodeHash(function.Source) != activeVersion.CodeHash
                || FunctionHashing.ManifestHash(function.Source) != activeVersion.ManifestHash;
        }

        /// <summary>
        /// Stores the newer trigger settings only when they say something: an empty verb list
        /// becomes null (the legacy single method) and "async" becomes null (the default). Both
        /// fields are skipped by the BSON map while null, so a function whose owner never touched
        /// them saves exactly the document an older Api or Worker pod can read — which keeps a
        /// rolling deploy or a rollback from failing workflow steps on an unknown element.
        /// </summary>
        internal static TriggerConfig NormalizeForStorage(TriggerConfig? trigger)
        {
            trigger ??= new TriggerConfig();

            var verbs = (trigger.HttpMethods ?? [])
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Select(m => m.Trim().ToUpperInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            trigger.HttpMethods = verbs.Count == 0 ? null : verbs;
            trigger.ResponseMode = TriggerConfig.IsSync(trigger.ResponseMode) ? TriggerConfig.ResponseModes.Sync : null;
            return trigger;
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
