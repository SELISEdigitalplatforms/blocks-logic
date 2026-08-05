using System.Net;
using Blocks.Genesis;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Common.InternalService.Monitor
{
    public class MonitorConfigurationService : IMonitorConfigurationService
    {
        private readonly ILogger<MonitorConfigurationService> _logger;
        private readonly IMessageClient _messageClient;
        private readonly IMonitorConfigurationRepoService _monitorConfigurationRepoService;
        private readonly IMonitorIncidentRepoService _monitorIncidentRepoService;
        private readonly IValidator<SaveMonitorConfigurationRequest> _saveValidator;
        private readonly IValidator<UpdateMonitorConfigurationRequest> _updateValidator;

        public MonitorConfigurationService(
                                            ILogger<MonitorConfigurationService> logger,
                                            IMessageClient messageClient,
                                            IMonitorConfigurationRepoService monitorConfigurationRepoService,
                                            IValidator<SaveMonitorConfigurationRequest> saveValidator,
                                            IMonitorIncidentRepoService monitorIncidentRepoService,
                                            IValidator<UpdateMonitorConfigurationRequest> updateValidator)
        {
            _logger = logger;
            _messageClient = messageClient;
            _monitorConfigurationRepoService = monitorConfigurationRepoService;
            _saveValidator = saveValidator;
            _updateValidator = updateValidator;
            _monitorIncidentRepoService = monitorIncidentRepoService;
        }

        public async Task<BaseApiResponse> GetConfigurationByIdAsync(string monitorId)
        {
            var data = new MonitorConfiguration();
            try
            {
                data = await _monitorConfigurationRepoService.GetConfigurationAsync(monitorId);

                if (data != null && IsCrossTenantAccess(data))
                {
                    _logger.LogWarning("MonitorConfiguration with ItemId {MonitorId} not found.", monitorId);
                    data = new MonitorConfiguration();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving MonitorConfiguration with ItemId {MonitorId}", monitorId);
            }
            return new BaseApiResponse()
            {
                IsSuccess = true,
                Message = "Success",
                Data = data
            };
        }

        public async Task<PaginatedResponse> GetConfigurationListAsync(string tenantId, string? monitorSourceType, int pageNumber, int pageSize, string? sortProperty = null, bool sortIsDescending = false)
        {
            var data = new List<MonitorConfiguration>();
            int totalCount = 0;
            try
            {
                (data, totalCount) = await _monitorConfigurationRepoService.GetConfigurationListAsync(tenantId, monitorSourceType, pageNumber, pageSize, sortProperty, sortIsDescending);
                var monitorIds = data.Select(x => x.ItemId).ToList();
                var start = DateTime.UtcNow.AddHours(-24);
                var end = DateTime.UtcNow;

                var incidents = await _monitorIncidentRepoService
                    .GetIncidentsListByMonitorIdsAndDateRangeAsync(monitorIds, start, end);

                var incidentGroups = incidents
                    .GroupBy(i => i.MonitorId)
                    .ToDictionary(g => g.Key, g => g.ToList());
                foreach (var config in data)
                {
                    if (incidentGroups.TryGetValue(config.ItemId, out var monitorIncidents))
                    {
                        config.IncidentSummaries = monitorIncidents.Select(i => new IncidentListSummary
                        {
                            StartTime = i.StartTime,
                            EndTime = i.EndTime,
                            DowntimeDurationSeconds = i.DowntimeDurationSeconds
                        }).ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving MonitorConfiguration list for TenantId {TenantId}", tenantId);
            }
            return new PaginatedResponse()
            {
                StatusCode = HttpStatusCode.OK,
                IsSuccess = true,
                Message = "Success",
                Data = data,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalCount = totalCount
            };
        }

        public async Task<BaseApiResponse> SaveConfigurationAsync(SaveMonitorConfigurationRequest request)
        {
            try
            {
                var result = await _saveValidator.ValidateAsync(request);

                if (!result.IsValid)
                {
                    return new BaseApiResponse
                    {
                        IsSuccess = false,
                        Message = string.Join(", ", result.Errors.Select(e => e.ErrorMessage))
                    };
                }

                MonitorSourceTypes monitorSourceTypeEnum = MonitorSourceTypes.DeployedServices;
                if (!string.IsNullOrEmpty(request.MonitorSourceType))
                {
                    if (!Enum.TryParse<MonitorSourceTypes>(request.MonitorSourceType, true, out var parsedEnum))
                    {
                        return new BaseApiResponse
                        {
                            IsSuccess = false,
                            Message = $"Invalid MonitorSourceType: {request.MonitorSourceType}"
                        };
                    }

                    if (parsedEnum != MonitorSourceTypes.ExternalServices &&
                        parsedEnum != MonitorSourceTypes.BlocksServices &&
                        parsedEnum != MonitorSourceTypes.DeployedServices &&
                        parsedEnum != MonitorSourceTypes.OtherServices
)
                    {
                        return new BaseApiResponse
                        {
                            IsSuccess = false,
                            Message = $"Invalid MonitorSourceType"
                        };
                    }

                    monitorSourceTypeEnum = parsedEnum;
                }

                MonitorConfiguration monitorConfiguration = new MonitorConfiguration
                {
                    ItemId = Guid.NewGuid().ToString(),
                    IsActive = request.IsActive,
                    CreatedDate = DateTime.UtcNow,
                    MonitorConfigurationType = MonitorConfigurationTypes.OutboundPing,
                    CreatedBy = GetCurrentUserId(),
                    MonitorSourceType = monitorSourceTypeEnum,
                    ExternalServiceId = monitorSourceTypeEnum == MonitorSourceTypes.ExternalServices
                               ? request.ExternalServiceId
                               : null
                };

                UpdateMonitorConfiguration(request, monitorConfiguration);

                await _monitorConfigurationRepoService.SaveConfigurationAsync(monitorConfiguration);
                try
                {
                    var payload = new MonitorConfigurationUpdateQueue
                    {
                        MonitorId = monitorConfiguration.ItemId
                    };

                    await _messageClient.SendToConsumerAsync(new ConsumerMessage<MonitorConfigurationUpdateQueue>
                    {
                        ConsumerName = MonitorConstants.MonitorConfigurationUpdateQueue,
                        Payload = payload
                    });

                    _logger.LogInformation("Queued monitor configuration update for {Monitor}", monitorConfiguration.ItemId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to enqueue monitor configuration update for {Monitor}", monitorConfiguration.ItemId);
                }
                return new BaseApiResponse()
                {
                    Data = monitorConfiguration,
                    IsSuccess = true,
                    Message = $"MonitorConfiguration with ItemId {monitorConfiguration.ItemId} created successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving MonitorConfiguration with url {Url}", request.Url);
                return new BaseApiResponse()
                {
                    IsSuccess = false,
                    Message = $"Error saving MonitorConfiguration with ItemId {request.Name}: {ex.Message}"
                };
            }
        }

        public async Task<BaseApiResponse> UpdateConfigurationAsync(UpdateMonitorConfigurationRequest request)
        {
            try
            {
                var result = await _updateValidator.ValidateAsync(request);

                if (!result.IsValid)
                {
                    return new BaseApiResponse
                    {
                        IsSuccess = false,
                        Message = string.Join(", ", result.Errors.Select(e => e.ErrorMessage))
                    };
                }

                MonitorConfiguration monitorConfiguration;

                monitorConfiguration = await _monitorConfigurationRepoService.GetConfigurationAsync(request.ItemId);
                if (monitorConfiguration == null)
                {
                    _logger.LogError("MonitorConfiguration with ItemId {ItemId} not found.", request.ItemId);
                    return new BaseApiResponse()
                    {
                        IsSuccess = false,
                        StatusCode = HttpStatusCode.OK,
                        Message = $"MonitorConfiguration with ItemId {request.ItemId} not found."
                    };
                }

                var effectiveSourceType = monitorConfiguration.EffectiveMonitorSourceType;
                if (effectiveSourceType == MonitorSourceTypes.Infrastructure ||
                    effectiveSourceType == MonitorSourceTypes.BlocksServices)
                {
                    _logger.LogError("MonitorConfiguration with ItemId {ItemId} not found.", request.ItemId);
                    return new BaseApiResponse()
                    {
                        IsSuccess = false,
                        StatusCode = HttpStatusCode.OK,
                        Message = $"MonitorConfiguration with ItemId {request.ItemId} not found."
                    };
                }

                // Reject cross-tenant mutation: a caller may only update a monitor owned by their own tenant.
                if (IsCrossTenantAccess(monitorConfiguration))
                {
                    _logger.LogWarning("MonitorConfiguration with ItemId {ItemId} not found.", request.ItemId);
                    return new BaseApiResponse()
                    {
                        IsSuccess = false,
                        StatusCode = HttpStatusCode.OK,
                        Message = $"MonitorConfiguration with ItemId {request.ItemId} not found."
                    };
                }

                UpdateMonitorConfiguration(request, monitorConfiguration);

                await _monitorConfigurationRepoService.SaveConfigurationAsync(monitorConfiguration);
                try
                {
                    var payload = new MonitorConfigurationUpdateQueue
                    {
                        MonitorId = monitorConfiguration.ItemId
                    };

                    await _messageClient.SendToConsumerAsync(new ConsumerMessage<MonitorConfigurationUpdateQueue>
                    {
                        ConsumerName = MonitorConstants.MonitorConfigurationUpdateQueue,
                        Payload = payload
                    });

                    _logger.LogInformation("Queued monitor configuration update for {Monitor}", monitorConfiguration.ItemId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to enqueue monitor configuration update for {Monitor}", monitorConfiguration.ItemId);
                }
                return new BaseApiResponse()
                {
                    Data = monitorConfiguration,
                    IsSuccess = true,
                    Message = $"MonitorConfiguration with ItemId {request.ItemId} updated successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving MonitorConfiguration with ItemId {ItemId}", request.ItemId);
                return new BaseApiResponse()
                {
                    IsSuccess = false,
                    Message = $"Error saving MonitorConfiguration with ItemId {request.Name}: {ex.Message}"
                };
            }
        }

        private static void UpdateMonitorConfiguration(SaveMonitorConfigurationRequest request, MonitorConfiguration monitorConfiguration)
        {
            monitorConfiguration.TenantId = monitorConfiguration.TenantId ?? request.ProjectKey;
            monitorConfiguration.LastUpdatedDate = DateTime.UtcNow;
            monitorConfiguration.LastUpdatedBy = GetCurrentUserId();

            if (!string.IsNullOrEmpty(request.Name)) monitorConfiguration.Name = request.Name;
            if (!string.IsNullOrEmpty(request.Url)) monitorConfiguration.Url = request.Url;
            if (!string.IsNullOrEmpty(request.RepoId)) monitorConfiguration.RepoId = request.RepoId;
            if (!string.IsNullOrEmpty(request.RepoName)) monitorConfiguration.RepoName = request.RepoName;
            if (!string.IsNullOrEmpty(request.ExternalServiceName)) monitorConfiguration.ExternalServiceName = request.ExternalServiceName;

            if (!string.IsNullOrEmpty(request.MonitorType))
                monitorConfiguration.MonitorType = Enum.Parse<MonitorTypes>(request.MonitorType, true);

            if (!string.IsNullOrEmpty(request.ProtocolType))
                monitorConfiguration.ProtocolType = Enum.Parse<ProtocolTypes>(request.ProtocolType, true);

            if (!string.IsNullOrEmpty(request.HttpMethodType))
                monitorConfiguration.HttpMethodType = Enum.Parse<HttpMethodTypes>(request.HttpMethodType, true);

            if (!string.IsNullOrEmpty(request.AuthorizationType))
                monitorConfiguration.AuthorizationType = Enum.Parse<AuthorizationTypes>(request.AuthorizationType, true);

            if (request.IntervalInSeconds.HasValue)
                monitorConfiguration.IntervalInSeconds = request.IntervalInSeconds.Value;

            if (request.TimeoutInSeconds.HasValue)
                monitorConfiguration.TimeoutInSeconds = request.TimeoutInSeconds.Value;

            monitorConfiguration.IsActive = request.IsActive;

            if (!string.IsNullOrEmpty(request.ExpectedContent)) monitorConfiguration.ExpectedContent = request.ExpectedContent;
            if (!string.IsNullOrEmpty(request.CustomHttpHeaders)) monitorConfiguration.CustomHttpHeaders = request.CustomHttpHeaders;
            if (!string.IsNullOrEmpty(request.CustomPayload)) monitorConfiguration.CustomPayload = request.CustomPayload;

            if (request.SuccessHttpResponseCodes != null && request.SuccessHttpResponseCodes.Count > 0)
                monitorConfiguration.SuccessHttpResponseCodes = request.SuccessHttpResponseCodes;

            if (request.Regions != null && request.Regions.Count > 0)
                monitorConfiguration.Regions = request.Regions;

            if (request.Emails != null && request.Emails.Count > 0)
                monitorConfiguration.Emails = request.Emails;
        }

        private static string GetCurrentUserId()
        {
            try
            {
                return BlocksContext.GetContext()?.UserId ?? "system";
            }
            catch
            {
                return "system";
            }
        }

        private static string? GetCurrentTenantId()
        {
            try
            {
                return BlocksContext.GetContext()?.TenantId;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Returns true when the acting caller belongs to a different tenant than the one that owns the
        /// monitor. Used to keep by-id operations tenant-isolated: a signed-in user of one tenant must not
        /// be able to read, update, or delete another tenant's monitor. When either the caller's tenant or
        /// the monitor's tenant is unknown (e.g. system/background contexts), no cross-tenant access is
        /// asserted so legitimate internal flows keep working.
        /// </summary>
        private static bool IsCrossTenantAccess(MonitorConfiguration monitorConfiguration)
        {
            var callerTenantId = GetCurrentTenantId();
            return !string.IsNullOrEmpty(callerTenantId)
                && !string.IsNullOrEmpty(monitorConfiguration.TenantId)
                && !string.Equals(callerTenantId, monitorConfiguration.TenantId, StringComparison.Ordinal);
        }

        public async Task<BaseApiResponse> DeleteConfigurationAsync(string itemId)
        {
            try
            {
                var monitorConfiguration = await _monitorConfigurationRepoService.GetConfigurationAsync(itemId);
                if (monitorConfiguration == null)
                {
                    _logger.LogError("MonitorConfiguration with ItemId {ItemId} not found.", itemId);
                    return new BaseApiResponse()
                    {
                        IsSuccess = false,
                        StatusCode = HttpStatusCode.OK,
                        Message = $"MonitorConfiguration with ItemId {itemId} not found."
                    };
                }

                var effectiveSourceType = monitorConfiguration.EffectiveMonitorSourceType;
                if (effectiveSourceType == MonitorSourceTypes.Infrastructure ||
                    effectiveSourceType == MonitorSourceTypes.BlocksServices)
                {
                    _logger.LogError("MonitorConfiguration with ItemId {ItemId} not found.", itemId);
                    return new BaseApiResponse()
                    {
                        IsSuccess = false,
                        StatusCode = HttpStatusCode.OK,
                        Message = $"MonitorConfiguration with ItemId {itemId} not found."
                    };
                }

                // Reject cross-tenant deletes: a caller may only delete a monitor owned by their own tenant.
                if (IsCrossTenantAccess(monitorConfiguration))
                {
                    _logger.LogWarning("MonitorConfiguration with ItemId {ItemId} not found.", itemId);
                    return new BaseApiResponse()
                    {
                        IsSuccess = false,
                        StatusCode = HttpStatusCode.OK,
                        Message = $"MonitorConfiguration with ItemId {itemId} not found."
                    };
                }

                var deleted = await _monitorConfigurationRepoService.DeleteConfigurationAsync(itemId);

                if (!deleted)
                {
                    _logger.LogWarning("MonitorConfiguration with ItemId {ItemId} does not exist. Cannot delete.", itemId);
                }
                return new BaseApiResponse()
                {
                    IsSuccess = true,
                    Message = $"MonitorConfiguration with ItemId {itemId} deleted successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting MonitorConfiguration with ItemId {ItemId}", itemId);
                return new BaseApiResponse()
                {
                    IsSuccess = false,
                    Message = $"Error deleting MonitorConfiguration with ItemId {itemId}: {ex.Message}"
                };
            }
        }

        public async Task<BaseApiResponse> GetConfigurationListWithDowntimeByRepoIdAsync(string tenantId, string repoId)
        {
            try
            {
                var configurations = await _monitorConfigurationRepoService.GetConfigurationListByRepoIdAsync(tenantId, repoId);
                if (configurations == null || configurations.Count == 0)
                {
                    return new BaseApiResponse
                    {
                        IsSuccess = true,
                        Message = "No monitors found for the given repo.",
                        Data = new List<MonitorConfiguration>()
                    };
                }

                var monitorIds = configurations.Select(x => x.ItemId).ToList();
                var start = DateTime.UtcNow.AddHours(-24);
                var end = DateTime.UtcNow;

                var incidents = await _monitorIncidentRepoService
                    .GetIncidentsListByMonitorIdsAndDateRangeAsync(monitorIds, start, end);

                var incidentGroups = incidents
                    .GroupBy(i => i.MonitorId)
                    .ToDictionary(g => g.Key, g => g.ToList());
                foreach (var config in configurations)
                {
                    if (incidentGroups.TryGetValue(config.ItemId, out var monitorIncidents))
                    {
                        config.IncidentSummaries = monitorIncidents.Select(i => new IncidentListSummary
                        {
                            StartTime = i.StartTime,
                            EndTime = i.EndTime,
                            DowntimeDurationSeconds = i.DowntimeDurationSeconds
                        }).ToList();
                    }
                }

                return new BaseApiResponse
                {
                    IsSuccess = true,
                    Message = "Successfully fetched monitors with downtime (last 24h).",
                    Data = configurations
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching monitor list with downtime for RepoId {RepoId}", repoId);
                return new BaseApiResponse
                {
                    IsSuccess = false,
                    Message = $"An error occurred: {ex.Message}"
                };
            }
        }
    }
}
