using FluentValidation;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Dtos.Responses;
using Functions.DomainService.Repositories;
using Functions.DomainService.Utils;
using Workflow.DomainService.Actions;

namespace Functions.DomainService.Services
{
    public interface IFunctionEventSettingsService
    {
        /// <summary>The draft's trigger events; empty when none were saved. A function of another tenant is not found.</summary>
        Task<FunctionEventSettingsDto> GetAsync(string tenantId, string functionId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Saves the draft's trigger actions. Deploy copies them to the new version. Every name must be in the tenant's
        /// <c>actions</c> list (<see cref="ActionNotFoundCode"/> otherwise); its Function switch may be off.
        /// </summary>
        Task SaveAsync(
            string tenantId, SaveEventSettingsRequestDto request, string? actorId, string? actorEmail,
            CancellationToken cancellationToken = default);

        /// <summary>Runs of the function still waiting to start, HTTP and event runs apart. A function of another tenant is not found.</summary>
        Task<QueueStatusDto> GetQueueStatusAsync(string tenantId, string functionId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Function events, the editor's side: the draft's trigger events and the queue status. The Worker reads the same
    /// data through the repositories.
    /// </summary>
    public class FunctionEventSettingsService : IFunctionEventSettingsService
    {
        /// <summary>The error code of a trigger name that is not in the tenant's actions.</summary>
        public const string ActionNotFoundCode = "ACTION_NOT_FOUND";

        private readonly IFunctionRepository _functionRepository;
        private readonly IFunctionRunRepository _runRepository;
        private readonly IFunctionEventSettingsRepository _settings;
        private readonly IFunctionAuditService _auditService;
        private readonly IValidator<SaveEventSettingsRequestDto> _validator;
        private readonly IActionsRepository _actions;

        public FunctionEventSettingsService(
            IFunctionRepository functionRepository,
            IFunctionRunRepository runRepository,
            IFunctionEventSettingsRepository settings,
            IFunctionAuditService auditService,
            IValidator<SaveEventSettingsRequestDto> validator,
            IActionsRepository actions)
        {
            _actions = actions;
            _functionRepository = functionRepository;
            _runRepository = runRepository;
            _settings = settings;
            _auditService = auditService;
            _validator = validator;
        }

        public async Task<FunctionEventSettingsDto> GetAsync(
            string tenantId, string functionId, CancellationToken cancellationToken = default)
        {
            await RequireFunctionAsync(tenantId, functionId, cancellationToken);

            var row = await _settings.GetAsync(tenantId, functionId, null, cancellationToken);
            return new FunctionEventSettingsDto
            {
                FunctionId = functionId,
                TriggerEvents = [.. row?.TriggerEvents ?? []],
            };
        }

        public async Task SaveAsync(
            string tenantId, SaveEventSettingsRequestDto request, string? actorId, string? actorEmail,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var result = await _validator.ValidateAsync(request, cancellationToken);
            if (!result.IsValid)
            {
                throw new FunctionValidationException(string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
            }

            await RequireFunctionAsync(tenantId, request.FunctionId, cancellationToken);
            await RequireKnownActionsAsync(request.TriggerEvents, cancellationToken);

            await _settings.SaveDraftAsync(
                tenantId, request.FunctionId, [.. request.TriggerEvents], actorId, cancellationToken);

            await _auditService.RecordAsync(
                tenantId, request.FunctionId, FunctionsConstants.AuditActions.EventsUpdated, actorId, actorEmail,
                new { TriggerEvents = request.TriggerEvents.Count },
                cancellationToken);
        }

        public async Task<QueueStatusDto> GetQueueStatusAsync(
            string tenantId, string functionId, CancellationToken cancellationToken = default)
        {
            await RequireFunctionAsync(tenantId, functionId, cancellationToken);

            var counts = await _runRepository.GetQueueCountsAsync(tenantId, functionId, cancellationToken);
            return new QueueStatusDto
            {
                HttpQueued = counts.HttpQueued,
                EventQueued = counts.EventQueued,
                OldestEventQueuedAt = counts.OldestEventQueuedAt is { } oldest
                    ? DateTime.SpecifyKind(oldest, DateTimeKind.Utc).ToString("O", System.Globalization.CultureInfo.InvariantCulture)
                    : null,
            };
        }

        private async Task RequireKnownActionsAsync(List<string> names, CancellationToken cancellationToken)
        {
            if (names.Count == 0) return;

            var known = (await _actions.GetAllAsync(cancellationToken)).Select(a => a.Name).ToHashSet(StringComparer.Ordinal);
            var problems = names
                .Select((name, index) => (name, index))
                .Where(entry => !known.Contains(entry.name))
                .Select(entry => $"triggerEvents[{entry.index}]: {ActionNotFoundCode}: '{entry.name}' is not an action of this project")
                .ToList();
            if (problems.Count > 0)
            {
                throw new FunctionValidationException(string.Join("; ", problems));
            }
        }

        private async Task RequireFunctionAsync(string tenantId, string functionId, CancellationToken cancellationToken)
        {
            var function = string.IsNullOrWhiteSpace(functionId)
                ? null
                : await _functionRepository.GetByIdAsync(tenantId, functionId, cancellationToken);
            if (function is null) throw new FunctionNotFoundException($"function '{functionId}' was not found");
        }
    }
}
