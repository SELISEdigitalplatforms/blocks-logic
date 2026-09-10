using FluentValidation;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Dtos.Responses;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Repositories;
using Functions.DomainService.Utils;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Functions.DomainService.Services
{
    public interface IFunctionDeploymentService
    {
        Task<FunctionVersionSummaryDto> DeployAsync(
            string tenantId, DeployFunctionRequestDto request, string? actorId, string? actorEmail,
            CancellationToken cancellationToken = default);

        Task<FunctionVersionSummaryDto> RollbackAsync(
            string tenantId, RollbackFunctionRequestDto request, string? actorId, string? actorEmail,
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
        private readonly IFunctionAuditService _auditService;
        private readonly IValidator<DeployFunctionRequestDto> _deployValidator;
        private readonly IValidator<RollbackFunctionRequestDto> _rollbackValidator;
        private readonly ILogger<FunctionDeploymentService> _logger;

        public FunctionDeploymentService(
            IFunctionRepository functionRepository,
            IFunctionVersionRepository versionRepository,
            IFunctionBuildService buildService,
            IFunctionVersionRetentionService retentionService,
            IFunctionAuditService auditService,
            IValidator<DeployFunctionRequestDto> deployValidator,
            IValidator<RollbackFunctionRequestDto> rollbackValidator,
            ILogger<FunctionDeploymentService> logger)
        {
            _functionRepository = functionRepository;
            _versionRepository = versionRepository;
            _buildService = buildService;
            _retentionService = retentionService;
            _auditService = auditService;
            _deployValidator = deployValidator;
            _rollbackValidator = rollbackValidator;
            _logger = logger;
        }

        public async Task<FunctionVersionSummaryDto> DeployAsync(
            string tenantId, DeployFunctionRequestDto request, string? actorId, string? actorEmail,
            CancellationToken cancellationToken = default)
        {
            await ValidateAsync(_deployValidator, request);

            var function = await _functionRepository.GetByIdAsync(tenantId, request.FunctionId, cancellationToken)
                ?? throw new FunctionNotFoundException($"function '{request.FunctionId}' was not found");

            var build = await _buildService.EnsureImageAsync(tenantId, function, cancellationToken);
            if (build.Status != BuildStatus.Succeeded || string.IsNullOrEmpty(build.ImageDigest))
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
                    ImageDigest = build.ImageDigest,
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

            return FunctionVersionSummaryDto.From(created);
        }

        public async Task<FunctionVersionSummaryDto> RollbackAsync(
            string tenantId, RollbackFunctionRequestDto request, string? actorId, string? actorEmail,
            CancellationToken cancellationToken = default)
        {
            await ValidateAsync(_rollbackValidator, request);

            var function = await _functionRepository.GetByIdAsync(tenantId, request.FunctionId, cancellationToken)
                ?? throw new FunctionNotFoundException($"function '{request.FunctionId}' was not found");

            var target = await _versionRepository.GetByNumberAsync(tenantId, function.ItemId, request.VersionNumber, cancellationToken)
                ?? throw new FunctionNotFoundException(
                    $"function '{request.FunctionId}' has no version {request.VersionNumber}");

            // Only the pointer moves. In-flight runs already carry their own copy of the
            // version they started under, so they are unaffected either way.
            function.ActiveVersionId = target.ItemId;
            function.Status = FunctionStatus.Live;
            function.LastUpdatedDate = DateTime.UtcNow;
            function.LastUpdatedBy = actorId ?? string.Empty;
            await _functionRepository.MoveActiveVersionAsync(
                tenantId, function.ItemId, target.ItemId, actorId, cancellationToken);

            await _auditService.RecordAsync(
                tenantId, function.ItemId, FunctionsConstants.AuditActions.RolledBack, actorId, actorEmail,
                new { ToVersion = target.Number }, cancellationToken);

            return FunctionVersionSummaryDto.From(target);
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
