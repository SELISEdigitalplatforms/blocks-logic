using System.Text.Json;
using FluentValidation;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Dtos.Responses;
using Functions.DomainService.Events;
using Functions.DomainService.Repositories;
using Functions.DomainService.Utils;

namespace Functions.DomainService.Services
{
    public interface IFunctionEventSettingsService
    {
        FunctionEventCatalogDto GetCatalog();

        /// <summary>The draft's events; empty when none were saved. A function of another tenant is not found.</summary>
        Task<FunctionEventSettingsDto> GetAsync(string tenantId, string functionId, CancellationToken cancellationToken = default);

        /// <summary>Saves the draft's events. Deploy copies them to the new version.</summary>
        Task SaveAsync(
            string tenantId, SaveEventSettingsRequestDto request, string? actorId, string? actorEmail,
            CancellationToken cancellationToken = default);

        /// <summary>Runs of the function still waiting to start, HTTP and event runs apart. A function of another tenant is not found.</summary>
        Task<QueueStatusDto> GetQueueStatusAsync(string tenantId, string functionId, CancellationToken cancellationToken = default);

        /// <summary>What happened to a run's events (its newest attempt that has any). A run of another tenant is not found.</summary>
        Task<RunEventsDto> GetRunEventsAsync(string tenantId, string runId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The events a function may send (see <see cref="FunctionEventCatalog"/>): the editor's side — catalog, the draft's
    /// settings, and what happened to a run's events. The Worker reads the same data through the repositories.
    /// </summary>
    public class FunctionEventSettingsService : IFunctionEventSettingsService
    {
        private readonly IFunctionRepository _functionRepository;
        private readonly IFunctionRunRepository _runRepository;
        private readonly IFunctionEventSettingsRepository _settings;
        private readonly IFunctionRunEventsRepository _runEvents;
        private readonly IFunctionAuditService _auditService;
        private readonly IValidator<SaveEventSettingsRequestDto> _validator;

        public FunctionEventSettingsService(
            IFunctionRepository functionRepository,
            IFunctionRunRepository runRepository,
            IFunctionEventSettingsRepository settings,
            IFunctionRunEventsRepository runEvents,
            IFunctionAuditService auditService,
            IValidator<SaveEventSettingsRequestDto> validator)
        {
            _functionRepository = functionRepository;
            _runRepository = runRepository;
            _settings = settings;
            _runEvents = runEvents;
            _auditService = auditService;
            _validator = validator;
        }

        public FunctionEventCatalogDto GetCatalog() => new()
        {
            Items =
            [
                .. FunctionEventCatalog.All.Select(d => new FunctionEventCatalogItemDto
                {
                    Type = d.Type,
                    Title = d.Title,
                    Description = d.Description,
                    PublicAllowed = d.PublicAllowed,
                    PayloadTemplate = ParseObject(d.PayloadTemplateJson),
                }),
            ],
        };

        public async Task<FunctionEventSettingsDto> GetAsync(
            string tenantId, string functionId, CancellationToken cancellationToken = default)
        {
            await RequireFunctionAsync(tenantId, functionId, cancellationToken);

            var row = await _settings.GetAsync(tenantId, functionId, null, cancellationToken);
            return new FunctionEventSettingsDto
            {
                FunctionId = functionId,
                Events =
                [
                    .. (row?.Events ?? []).Select(e => new FunctionEventSettingDto
                    {
                        Type = e.Type,
                        Payload = ParseObject(e.PayloadTemplateJson),
                    }),
                ],
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

            var events = request.Events
                .Select(e => new FunctionEventSetting { Type = e.Type, PayloadTemplateJson = e.Payload.GetRawText() })
                .ToList();
            await _settings.SaveDraftAsync(
                tenantId, request.FunctionId, events, [.. request.TriggerEvents], actorId, cancellationToken);

            await _auditService.RecordAsync(
                tenantId, request.FunctionId, FunctionsConstants.AuditActions.EventsUpdated, actorId, actorEmail,
                new { Events = events.Select(e => e.Type).ToList(), TriggerEvents = request.TriggerEvents.Count },
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

        public async Task<RunEventsDto> GetRunEventsAsync(
            string tenantId, string runId, CancellationToken cancellationToken = default)
        {
            var run = await _runRepository.GetByIdAsync(tenantId, runId, cancellationToken)
                ?? throw new FunctionNotFoundException($"run '{runId}' was not found");

            var document = await _runEvents.GetLatestAsync(tenantId, run.ItemId, cancellationToken);
            return new RunEventsDto
            {
                Rejected = document?.Rejected,
                Items =
                [
                    .. (document?.Events ?? []).Select(e => new RunEventDto
                    {
                        Id = e.Id,
                        Type = e.Type,
                        Name = e.Name,
                        Status = e.Status.ToString(),
                        Error = e.Error,
                        Attempts = e.Attempts,
                        SentAt = e.SentAt,
                    }),
                ],
            };
        }

        private async Task RequireFunctionAsync(string tenantId, string functionId, CancellationToken cancellationToken)
        {
            var function = string.IsNullOrWhiteSpace(functionId)
                ? null
                : await _functionRepository.GetByIdAsync(tenantId, functionId, cancellationToken);
            if (function is null) throw new FunctionNotFoundException($"function '{functionId}' was not found");
        }

        /// <summary>A stored template as a JSON value; a broken one (never written by this service) is an empty object.</summary>
        private static JsonElement ParseObject(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<JsonElement>(json);
            }
            catch (JsonException)
            {
                return JsonSerializer.Deserialize<JsonElement>("{}");
            }
        }
    }
}
