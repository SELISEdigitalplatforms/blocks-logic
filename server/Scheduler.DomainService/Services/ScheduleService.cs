using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Scheduler.DomainService.Dtos.Requests;
using Scheduler.DomainService.Dtos.Responses;
using Scheduler.DomainService.Entities;
using Scheduler.DomainService.Enums;
using Scheduler.DomainService.Events;
using Scheduler.DomainService.Models;
using Scheduler.DomainService.Repositories;
using Scheduler.DomainService.Utils;

namespace Scheduler.DomainService.Services
{
    public class ScheduleService : IScheduleService
    {
        private readonly IScheduleRepository _scheduleRepository;
        private readonly IMessageClient _messageClient;
        private readonly ILogger<ScheduleService> _logger;

        public ScheduleService(IScheduleRepository scheduleRepository,
                               IMessageClient messageClient,
                               ILogger<ScheduleService> logger)
        {
            _scheduleRepository = scheduleRepository;
            _messageClient = messageClient;
            _logger = logger;
        }

        public async Task<BaseMutationResponse> CreateScheduleAsync(CreateScheduleRequestDto request)
        {
            var isValidCronExp = Helper.IsValidCronExpression(request.CronExpression);
            if (!isValidCronExp)
            {
                return CreateMutationErrorResponse("validation_failed", "Cron expression is not valid");
            }
            var schedule = new Schedule
            {
                ItemId = Guid.NewGuid().ToString(),
                Name = request.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                Payload = request.Payload,
                CronExpression = request.CronExpression,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                IsActive = true,
                Kind = ScheduleKind.Application,
                TriggerType = ScheduleTriggerType.Webhook,
                Webhook = request.Webhook,
                Queue = null,
                CreatedBy = BlocksContext.GetContext()?.UserId ?? "",
                CreatedDate = DateTime.UtcNow,
            };

            try
            {
                await _scheduleRepository.CreateAsync(schedule);

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create schedule in the repository.");
                return CreateMutationErrorResponse("create_failed", "Failed to create schedule");
            }
            try
            {
                await EnqueueScheduleJobUpsertedAsync(schedule.ItemId);

                return CreateMutationSuccessResponse(schedule.ItemId);
            }
            catch (Exception ex)
            {
                // Todo
                // remove schedule from repository if enqueue fails
                _logger.LogError(ex, "Failed to enqueue schedule {ItemId} for registration.", schedule.ItemId);
                return CreateMutationErrorResponse("enqueue_failed", "Failed to register schedule");
            }

        }

        public async Task<BaseMutationResponse> UpdateScheduleAsync(UpdateScheduleRequestDto request)
        {
            var isValidCronExp = Helper.IsValidCronExpression(request.CronExpression);
            if (!isValidCronExp)
            {
                return CreateMutationErrorResponse("validation_failed", "Cron expression is not valid");
            }
            var schedule = await _scheduleRepository.GetByIdAsync(request.ItemId);
            if (schedule is null)
            {
                return CreateMutationErrorResponse("schedule_not_found", $"Schedule {request.ItemId} was not found");
            }

            if (schedule.Kind == ScheduleKind.Internal)
            {
                return CreateMutationErrorResponse("internal_schedule", "Internal schedules cannot be modified via API");
            }

            schedule.Name = request.Name.Trim();
            schedule.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            schedule.Payload = request.Payload;
            schedule.CronExpression = request.CronExpression;
            schedule.StartDate = request.StartDate;
            schedule.EndDate = request.EndDate;
            schedule.IsActive = request.IsActive;
            schedule.TriggerType = ScheduleTriggerType.Webhook;
            schedule.Webhook = request.Webhook;
            schedule.Queue = null;
            schedule.LastUpdatedDate = DateTime.UtcNow;
            schedule.LastUpdatedBy = BlocksContext.GetContext()?.UserId ?? "";

            try
            {
                await _scheduleRepository.UpdateAsync(schedule);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update schedule {ItemId} in the repository.", schedule.ItemId);
                return CreateMutationErrorResponse("update_failed", "Failed to update schedule");
            }
            try
            {
                await EnqueueScheduleJobUpsertedAsync(schedule.ItemId);
            }
            catch (Exception ex)
            {
                // Todo
                // rollback repository update if enqueue fails
                _logger.LogError(ex, "Failed to enqueue schedule {ItemId} for registration.", schedule.ItemId);
                return CreateMutationErrorResponse("enqueue_failed", "Failed to register schedule");
            }

            return CreateMutationSuccessResponse(schedule.ItemId);
        }

        public async Task<BaseResponse> DeleteScheduleAsync(DeleteScheduleRequestDto request)
        {
            var schedule = await _scheduleRepository.GetByIdAsync(request.ItemId);
            if (schedule is null)
            {
                return CreateErrorResponse("schedule_not_found", $"Schedule was not found");
            }

            if (schedule.Kind == ScheduleKind.Internal)
            {
                return CreateErrorResponse("internal_schedule", "Internal schedules cannot be deleted via API");
            }

            try
            {
                var deleted = await _scheduleRepository.DeleteAsync(request.ItemId);
                if (!deleted)
                {
                    return CreateErrorResponse("delete_failed", "Failed to delete schedule");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete schedule {ItemId} in the repository.", request.ItemId);
                return CreateErrorResponse("delete_failed", "Failed to delete schedule");
            }

            try
            {
                await EnqueueScheduleJobDeletedAsync(request.ItemId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enqueue schedule {ItemId} for unregistration.", request.ItemId);
                return CreateErrorResponse("enqueue_failed", "Failed to unregister schedule");
            }

            return CreateSuccessResponse();
        }

        public async Task<BaseMutationResponse> CreateWorkflowScheduleAsync(CreateWorkflowScheduleRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.WorkflowId) || string.IsNullOrWhiteSpace(request.NodeId))
            {
                return CreateMutationErrorResponse("validation_failed", "WorkflowId and NodeId are required");
            }

            var tenantId = string.IsNullOrWhiteSpace(request.TenantId)
                ? BlocksContext.GetContext()?.TenantId ?? string.Empty
                : request.TenantId;

            if (!Helper.IsValidCronExpression(request.CronExpression))
            {
                return CreateMutationErrorResponse("validation_failed", "Cron expression is not valid");
            }

            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                workflowId = request.WorkflowId,
                triggerId = request.NodeId,
                tenantId,
                cronExpression = request.CronExpression
            });

            var schedule = new Schedule
            {
                ItemId = Guid.NewGuid().ToString(),
                Name = $"wf-{tenantId}-{request.WorkflowId}-{request.NodeId}",
                Description = "Workflow schedule trigger (managed by workflow publish)",
                Payload = payload,
                CronExpression = request.CronExpression,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                IsActive = true,
                Kind = ScheduleKind.Internal,
                TriggerType = ScheduleTriggerType.Queue,
                Webhook = null,
                Queue = new QueueConfiguration { QueueName = SchedulerConstants.WorkflowSchedulerTriggerQueue },
                CreatedBy = BlocksContext.GetContext()?.UserId ?? "",
                CreatedDate = DateTime.UtcNow,
            };

            try
            {
                await _scheduleRepository.CreateAsync(schedule);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create workflow schedule in the repository.");
                return CreateMutationErrorResponse("create_failed", "Failed to create workflow schedule");
            }

            try
            {
                await EnqueueScheduleJobUpsertedWithTenantAsync(schedule.ItemId, tenantId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enqueue workflow schedule {ItemId} for registration.", schedule.ItemId);
                return CreateMutationErrorResponse("enqueue_failed", "Failed to register workflow schedule");
            }

            return CreateMutationSuccessResponse(schedule.ItemId);
        }

        public async Task<BaseResponse> DeleteWorkflowSchedulesAsync(IEnumerable<string> itemIds)
        {
            foreach (var itemId in itemIds)
            {
                if (string.IsNullOrWhiteSpace(itemId))
                {
                    continue;
                }

                try
                {
                    var schedule = await _scheduleRepository.GetByIdAsync(itemId);
                    if (schedule is null)
                    {
                        // Tolerate missing ids — republish/unpublish retries must not fail.
                        continue;
                    }

                    var tenantId = BlocksContext.GetContext()?.TenantId ?? string.Empty;

                    var deleted = await _scheduleRepository.DeleteAsync(itemId);
                    if (!deleted)
                    {
                        return CreateErrorResponse("delete_failed", $"Failed to delete schedule {itemId}");
                    }

                    await EnqueueScheduleJobDeletedWithTenantAsync(itemId, tenantId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete workflow schedule {ItemId}.", itemId);
                    return CreateErrorResponse("delete_failed", $"Failed to delete schedule {itemId}");
                }
            }

            return CreateSuccessResponse();
        }

        /// <summary>
        /// Sends the minimal delete notification to the Worker for Hangfire removal.
        /// Plain send — no inner try/catch so callers' error handling stays reachable.
        /// </summary>
        private Task EnqueueScheduleJobDeletedAsync(string itemId)
        {
            var tenantId = BlocksContext.GetContext()?.TenantId ?? string.Empty;
            if (string.IsNullOrEmpty(tenantId))
            {
                _logger.LogWarning("Schedule {ItemId} delete event enqueued with an empty TenantId; recurring-job id will not match reseed ids.", itemId);
            }

            return _messageClient.SendToConsumerAsync(new ConsumerMessage<ScheduleJobDeletedEvent>
            {
                ConsumerName = SchedulerConstants.ScheduleJobRegistryQueueName,
                Payload = new ScheduleJobDeletedEvent { ItemId = itemId, TenantId = tenantId }
            });
        }

        /// <summary>
        /// Sends the minimal upsert notification to the Worker for Hangfire registration.
        /// Plain send — no inner try/catch so callers' error handling stays reachable.
        /// </summary>
        private Task EnqueueScheduleJobUpsertedAsync(string itemId)
        {
            var tenantId = BlocksContext.GetContext()?.TenantId ?? string.Empty;
            if (string.IsNullOrEmpty(tenantId))
            {
                _logger.LogWarning("Schedule {ItemId} enqueued with an empty TenantId; recurring-job id will not match reseed ids.", itemId);
            }

            return _messageClient.SendToConsumerAsync(new ConsumerMessage<ScheduleJobUpsertedEvent>
            {
                ConsumerName = SchedulerConstants.ScheduleJobRegistryQueueName,
                Payload = new ScheduleJobUpsertedEvent { ItemId = itemId, TenantId = tenantId }
            });
        }

        private static BaseResponse CreateErrorResponse(string errorKey, string errorMessage)
        {
            return new BaseResponse
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string> { { errorKey, errorMessage } }
            };
        }

        private static BaseResponse CreateSuccessResponse()
        {
            return new BaseResponse { IsSuccess = true };
        }

        private static BaseMutationResponse CreateMutationSuccessResponse(string itemId)
        {
            return new BaseMutationResponse { IsSuccess = true, ItemId = itemId };
        }

        private static BaseMutationResponse CreateMutationErrorResponse(string errorKey, string errorMessage)
        {
            return new BaseMutationResponse
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string> { { errorKey, errorMessage } }
            };
        }

        private Task EnqueueScheduleJobUpsertedWithTenantAsync(string itemId, string tenantId)
        {
            if (string.IsNullOrEmpty(tenantId))
            {
                _logger.LogWarning("Schedule {ItemId} enqueued with an empty TenantId; recurring-job id will not match reseed ids.", itemId);
            }

            return _messageClient.SendToConsumerAsync(new ConsumerMessage<ScheduleJobUpsertedEvent>
            {
                ConsumerName = SchedulerConstants.ScheduleJobRegistryQueueName,
                Payload = new ScheduleJobUpsertedEvent { ItemId = itemId, TenantId = tenantId }
            });
        }

        private Task EnqueueScheduleJobDeletedWithTenantAsync(string itemId, string tenantId)
        {
            if (string.IsNullOrEmpty(tenantId))
            {
                _logger.LogWarning("Schedule {ItemId} delete event enqueued with an empty TenantId; recurring-job id will not match reseed ids.", itemId);
            }

            return _messageClient.SendToConsumerAsync(new ConsumerMessage<ScheduleJobDeletedEvent>
            {
                ConsumerName = SchedulerConstants.ScheduleJobRegistryQueueName,
                Payload = new ScheduleJobDeletedEvent { ItemId = itemId, TenantId = tenantId }
            });
        }

        public async Task<GetSchedulesResponseDto> GetSchedulesAsync(GetSchedulesRequestDto request)
        {
            try
            {
                var result = await _scheduleRepository.GetAllAsync(request.SearchKey, request.PageNumber, request.PageSize, ScheduleKind.Application);
                _logger.LogInformation($"Schedules count {result.TotalCount}");
                return new GetSchedulesResponseDto
                {
                    Data = result.Items?.Select(item => new ScheduleDto
                    {
                        ItemId = item.ItemId,
                        Name = item.Name,
                        Queue = item.Queue,
                        Description = item.Description,
                        CronExpression = item.CronExpression,
                        IsActive = item.IsActive,
                        Kind = item.Kind,
                        Webhook = item.Webhook,
                        StartDate = item.StartDate,
                        EndDate = item.EndDate,
                        Payload = item.Payload

                    }).ToList(),
                    TotalCount = result.TotalCount
                };
            }
            catch (System.Exception)
            {

                return new GetSchedulesResponseDto
                {
                    Errors = new Dictionary<string, string> { { "Message", "Something went wrong" } }
                };
            }


        }
    }
}
