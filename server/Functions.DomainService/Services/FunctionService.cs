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

        Task<bool> DeleteAsync(
            string tenantId, string functionId, string? actorId, string? actorEmail,
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
        private readonly IFunctionRunStatsRepository _runStatsRepository;
        private readonly IFunctionRunRepository _runRepository;
        private readonly IFunctionAuditService _auditService;
        private readonly IValidator<CreateFunctionRequestDto> _createValidator;
        private readonly IValidator<UpdateFunctionRequestDto> _updateValidator;
        private readonly IValidator<SaveFunctionRequestDto> _saveValidator;
        private readonly ILogger<FunctionService> _logger;
        private readonly IConfiguration _configuration;

        public FunctionService(
            IFunctionRepository repository,
            IFunctionVersionRepository versionRepository,
            IFunctionRunStatsRepository runStatsRepository,
            IFunctionRunRepository runRepository,
            IFunctionAuditService auditService,
            IValidator<CreateFunctionRequestDto> createValidator,
            IValidator<UpdateFunctionRequestDto> updateValidator,
            IValidator<SaveFunctionRequestDto> saveValidator,
            ILogger<FunctionService> logger,
            IConfiguration configuration)
        {
            _configuration = configuration;
            _repository = repository;
            _versionRepository = versionRepository;
            _runStatsRepository = runStatsRepository;
            _runRepository = runRepository;
            _auditService = auditService;
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
            };
            function.SourceHash = FunctionHashing.SourceHash(function.Source);
            function.Limits = request.Limits;
            function.Retry = request.Retry;
            function.Trigger = request.Trigger;
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
            CancellationToken cancellationToken = default)
        {
            var deleted = await _repository.DeleteAsync(tenantId, functionId, cancellationToken);
            if (deleted) await _runStatsRepository.DeleteAsync(tenantId, functionId, cancellationToken);
            if (!deleted) return false;

            // Versions are immutable history for a function that no longer exists; nothing
            // references them (runs keep their own copy of what they need), so they are
            // removed alongside it rather than left as orphans.
            await _versionRepository.DeleteAllForFunctionAsync(tenantId, functionId, cancellationToken);

            await _auditService.RecordAsync(
                tenantId, functionId, FunctionsConstants.AuditActions.Deleted, actorId, actorEmail,
                cancellationToken: cancellationToken);
            return true;
        }

        public async Task<(IReadOnlyList<FunctionSummaryDto> Items, long TotalCount)> GetAllAsync(
            string tenantId, GetFunctionsRequestDto request, CancellationToken cancellationToken = default)
        {
            var (items, totalCount) = await _repository.GetAllAsync(
                tenantId, request.SearchKey, request.Status, request.SortBy,
                request.PageNumber, request.PageSize, cancellationToken);

            var functionIds = items.Select(f => f.ItemId).ToList();

            // Two queries for the whole page's counters rather than two per row.
            var stats = await _runStatsRepository.GetManyAsync(tenantId, functionIds, cancellationToken);
            var runs24h = await _runRepository.CountSinceByFunctionAsync(
                tenantId, functionIds, DateTime.UtcNow.AddHours(-24), cancellationToken);

            var summaries = new List<FunctionSummaryDto>(items.Count);
            foreach (var function in items)
            {
                var activeVersion = await GetActiveVersionAsync(tenantId, function, cancellationToken);
                // Derived, exactly as the detail view does it. Without this the list read the
                // unset default and never showed "unpublished changes" for anything.
                function.IsDirty = IsDirty(function, activeVersion);
                stats.TryGetValue(function.ItemId, out var functionStats);
                runs24h.TryGetValue(function.ItemId, out var recentRuns);
                summaries.Add(FunctionSummaryDto.From(
                    function, activeVersion?.Number, functionStats, recentRuns));
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
