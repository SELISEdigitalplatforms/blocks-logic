using Common.InternalService.Access;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Proxy.DomainService.Dtos;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Repositories;
using Proxy.DomainService.Utils;

namespace Proxy.DomainService.Services
{
    /// <summary>
    /// Control-plane implementation for named reverse proxies. Correctness and auditability of the stored
    /// config take priority over breadth of validation (Phase 1). Every effective change appends exactly one
    /// <see cref="ProxyVersionEntity"/> and bumps <see cref="ProxyDetailEntity.CurrentVersion"/>; no-op
    /// mutations write nothing and still return 200.
    /// </summary>
    public sealed class ProxyService : IProxyService
    {
        private readonly IProxyRepository _proxyRepository;
        private readonly IProxyVersionRepository _proxyVersionRepository;
        private readonly IProxyExecutionRepository _proxyExecutionRepository;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<ProxyService> _logger;
        private readonly IProxyVariableResolver? _variableResolver;

        public ProxyService(
            IProxyRepository proxyRepository,
            IProxyVersionRepository proxyVersionRepository,
            IProxyExecutionRepository proxyExecutionRepository,
            TimeProvider timeProvider,
            ILogger<ProxyService> logger,
            IProxyVariableResolver? variableResolver = null)
        {
            _variableResolver = variableResolver;
            _proxyRepository = proxyRepository;
            _proxyVersionRepository = proxyVersionRepository;
            _proxyExecutionRepository = proxyExecutionRepository;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        public async Task<ProxyGetAllResponseDto> GetAllAsync(string tenantId, ProxyGetAllRequestDto request)
        {
            var pageSize = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, 200);
            var pageNumber = Math.Max(0, request.PageNumber);
            var search = request.GetSearchTerm();

            _logger.LogInformation(
                "Listing proxies for tenant {TenantId}. Search: {Search}, IsActive: {IsActive}, Page: {Page}, PageSize: {PageSize}",
                tenantId, search, request.IsActive, pageNumber, pageSize);

            var proxies = await _proxyRepository.GetAllAsync(tenantId, search, request.IsActive, pageSize, pageNumber);
            var totalCount = await _proxyRepository.CountAsync(tenantId, search, request.IsActive);

            // Read from each proxy document's own counters instead of aggregating ProxyExecutions for the
            // whole page: the documents are already loaded, so the card count now costs no query at all.
            var asOf = _timeProvider.GetUtcNow().UtcDateTime;

            var data = proxies.Select(p => new ProxyListItemDto
            {
                ItemId = p.ItemId,
                Name = p.Name,
                Slug = p.Slug,
                Upstream = p.Upstream,
                UpstreamMasked = ProxyUpstreamMasker.Mask(p.Upstream),
                Methods = p.Methods.Select(m => m.Wire()).ToList(),
                Enabled = p.Enabled,
                InjectedCredential = p.Headers.Concat(p.Query).Concat(p.BodyMerge)
                    .Any(kv => ProxyVarRef.ContainsRef(kv.Value)),
                HeaderCount = p.Headers.Count,
                QueryCount = p.Query.Count,
                Calls24h = ProxyStatsWindow.Rollup(p.Stats, asOf).Calls,
                CreatedDate = p.CreatedDate,
                LastUpdatedDate = p.LastUpdatedDate,
            }).ToList();

            _logger.LogInformation(
                "Listed {Returned} of {TotalCount} proxies for tenant {TenantId}.", data.Count, totalCount, tenantId);

            return new ProxyGetAllResponseDto { Data = data, TotalCount = totalCount, Errors = null };
        }

        public async Task<ProxyGetResponseDto> GetAsync(string tenantId, ProxyGetRequestDto request)
        {
            var itemId = request.ItemId ?? string.Empty;
            _logger.LogInformation("Fetching proxy {ItemId} for tenant {TenantId}.", itemId, tenantId);

            var proxy = await _proxyRepository.GetAsync(tenantId, itemId);
            if (proxy == null)
            {
                _logger.LogInformation("Proxy {ItemId} not found for tenant {TenantId}; returning null.", itemId, tenantId);
                return new ProxyGetResponseDto { Data = null, Errors = null };
            }

            var dto = new ProxyDetailDto
            {
                ItemId = proxy.ItemId,
                Name = proxy.Name,
                Slug = proxy.Slug,
                Path = $"/api/proxy/gateway/{proxy.Slug}/*",
                Upstream = proxy.Upstream,
                UpstreamMasked = ProxyUpstreamMasker.Mask(proxy.Upstream),
                Methods = proxy.Methods.Select(m => m.Wire()).ToList(),
                Enabled = proxy.Enabled,
                Headers = proxy.Headers.Select(ToKeyValueDto).ToList(),
                Query = proxy.Query.Select(ToKeyValueDto).ToList(),
                BodyMerge = proxy.BodyMerge.Select(ToKeyValueDto).ToList(),
                MethodConfigs = proxy.MethodConfigs.Select(ToMethodConfigDto).ToList(),
                Routes = proxy.Routes.Select(ToRouteDto).ToList(),
                ResponseMode = proxy.ResponseMode.ToString(),
                ResponseInclude = proxy.ResponseInclude.ToList(),
                Access = ToAccessDto(proxy.Access),
                Resilience = ToResilienceDto(proxy.Resilience),
                RequestsPerMinute = proxy.RequestsPerMinute,
                CurrentVersion = proxy.CurrentVersion,
                CreatedDate = proxy.CreatedDate,
                CreatedBy = proxy.CreatedBy,
                LastUpdatedDate = proxy.LastUpdatedDate,
                LastUpdatedBy = proxy.LastUpdatedBy,
            };

            _logger.LogInformation("Fetched proxy {ItemId} (slug {Slug}) for tenant {TenantId}.", proxy.ItemId, proxy.Slug, tenantId);
            return new ProxyGetResponseDto { Data = dto, Errors = null };
        }

        public async Task<ProxyMutationResponse> CreateAsync(string tenantId, ProxyCreateRequestDto request)
        {
            _logger.LogInformation("Creating proxy '{Name}' for tenant {TenantId}.", request.Name, tenantId);

            var validation = ProxyConfigValidator.Validate(
                request.Name, request.Upstream, request.Methods, request.Headers, request.Query, request.MethodConfigs,
                request.BodyMerge, request.ResponseMode, request.ResponseInclude, request.Routes, request.Access,
                request.Resilience, request.RequestsPerMinute);
            if (!validation.IsValid)
            {
                _logger.LogWarning(
                    "Proxy create for tenant {TenantId} rejected: {Errors}", tenantId, string.Join("; ", validation.Errors));
                return ProxyMutationResponse.Failure(
                    400, ProxyErrorCodes.Validation, "The proxy configuration is invalid.", validation.Errors);
            }

            // Derived by the validator, which has already rejected any name that yields an empty slug.
            var slug = validation.Slug;
            var existing = await _proxyRepository.GetBySlugAsync(tenantId, slug);
            if (existing != null)
            {
                _logger.LogWarning(
                    "Proxy create for tenant {TenantId} rejected: slug '{Slug}' already exists.", tenantId, slug);
                return ProxyMutationResponse.Failure(
                    409, ProxyErrorCodes.SlugConflict, $"A proxy named '{request.Name}' already exists.");
            }

            var userId = ProxyVersionFactory.CurrentUserId();
            var userName = ProxyVersionFactory.CurrentUserName();
            var now = DateTime.UtcNow;
            var proxy = new ProxyDetailEntity
            {
                ItemId = Guid.NewGuid().ToString("N"),
                TenantId = tenantId,
                Name = validation.Name,
                Slug = slug,
                Upstream = validation.Upstream,
                Methods = validation.Methods,
                Enabled = request.Enabled,
                Headers = validation.Headers,
                Query = validation.Query,
                BodyMerge = validation.BodyMerge,
                MethodConfigs = validation.MethodConfigs,
                Routes = validation.Routes,
                ResponseMode = validation.ResponseMode,
                ResponseInclude = validation.ResponseInclude,
                Access = validation.Access,
                Resilience = validation.Resilience,
                RequestsPerMinute = validation.RequestsPerMinute,
                CurrentVersion = 1,
                CreatedDate = now,
                LastUpdatedDate = now,
                CreatedBy = userId,
                LastUpdatedBy = userId,
            };

            // PX-9: one exact lookup now, so every call reads secrets by id. An unknown name is refused here
            // with its name, instead of failing every call later with a 502.
            var (secretIds, missingVars) = await ProxySecretIdBinder.BindAsync(_variableResolver, tenantId, proxy, _logger);
            if (missingVars.Count > 0)
            {
                _logger.LogWarning(
                    "Proxy create for tenant {TenantId} rejected: unknown variable(s) [{Names}].",
                    tenantId, string.Join(", ", missingVars));
                return ProxyMutationResponse.Failure(
                    400, ProxyErrorCodes.Validation, "The proxy uses a secret that does not exist.",
                    ProxySecretIdBinder.UnknownVariablesError(missingVars));
            }

            proxy.SecretIds = secretIds;

            try
            {
                await _proxyRepository.InsertAsync(proxy);
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                // Lost the race against a concurrent Create for the same slug (C7): surface it as a conflict,
                // never as a raw Mongo duplicate-key error.
                _logger.LogWarning(
                    "Proxy create for tenant {TenantId} lost a slug race on '{Slug}'.", tenantId, slug);
                return ProxyMutationResponse.Failure(
                    409, ProxyErrorCodes.SlugConflict, $"A proxy named '{request.Name}' already exists.");
            }

            var snapshot = ProxyVersionFactory.SnapshotOf(proxy);
            var version = ProxyVersionFactory.Build(
                proxy, 1, ProxyVersionKind.Create, "Proxy created",
                new List<ProxyFieldChange>(), snapshot, userId, userName);
            await _proxyVersionRepository.InsertAsync(version);

            _logger.LogInformation(
                "Created proxy {ItemId} (slug {Slug}) for tenant {TenantId}; wrote version 1.", proxy.ItemId, slug, tenantId);
            return ProxyMutationResponse.Success(proxy.ItemId, 201);
        }

        public async Task<ProxyMutationResponse> UpdateAsync(string tenantId, ProxyUpdateRequestDto request)
        {
            var itemId = request.ItemId ?? string.Empty;
            _logger.LogInformation("Updating proxy {ItemId} for tenant {TenantId}.", itemId, tenantId);

            var validation = ProxyConfigValidator.Validate(
                request.Name, request.Upstream, request.Methods, request.Headers, request.Query, request.MethodConfigs,
                request.BodyMerge, request.ResponseMode, request.ResponseInclude, request.Routes, request.Access,
                request.Resilience, request.RequestsPerMinute);
            if (!validation.IsValid)
            {
                _logger.LogWarning(
                    "Proxy update {ItemId} for tenant {TenantId} rejected: {Errors}",
                    itemId, tenantId, string.Join("; ", validation.Errors));
                return ProxyMutationResponse.Failure(
                    400, ProxyErrorCodes.Validation, "The proxy configuration is invalid.", validation.Errors);
            }

            var proxy = await _proxyRepository.GetAsync(tenantId, itemId);
            if (proxy == null)
            {
                _logger.LogWarning("Proxy update rejected: {ItemId} not found for tenant {TenantId}.", itemId, tenantId);
                return ProxyMutationResponse.Failure(
                    404, ProxyErrorCodes.NotFound, $"Proxy '{itemId}' was not found.");
            }

            if (IsStale(request.ExpectedVersion, proxy))
            {
                return VersionConflict(itemId, tenantId, request.ExpectedVersion, proxy.CurrentVersion);
            }

            var readVersion = proxy.CurrentVersion;

            // The slug itself is immutable — the gateway path is a contract third-party clients hard-code, so
            // a rename never moves it. But a rename INTO another proxy's slug would leave two rows whose names
            // collapse to the same identity while the URL belongs to the other one, which is only confusing.
            // Reject that the way Create does. A rename to a slug nobody owns is fine: name and slug are then
            // allowed to drift, which is the price of immutability.
            if (!string.Equals(validation.Slug, proxy.Slug, StringComparison.Ordinal))
            {
                var slugOwner = await _proxyRepository.GetBySlugAsync(tenantId, validation.Slug);
                if (slugOwner != null && !string.Equals(slugOwner.ItemId, proxy.ItemId, StringComparison.Ordinal))
                {
                    _logger.LogWarning(
                        "Proxy update {ItemId} for tenant {TenantId} rejected: name derives to slug '{Slug}', owned by {OwnerItemId}.",
                        itemId, tenantId, validation.Slug, slugOwner.ItemId);
                    return ProxyMutationResponse.Failure(
                        409, ProxyErrorCodes.SlugConflict, $"A proxy named '{request.Name}' already exists.");
                }
            }

            var beforeSnapshot = ProxyVersionFactory.SnapshotOf(proxy);

            // Decide from the structured diff, not a string fingerprint: a Save that only reorders methods /
            // headers / query produces no field change and therefore no version row (SPEC §2.8 / §8.5).
            var candidate = new ProxyConfigSnapshot
            {
                Name = validation.Name,
                Slug = proxy.Slug,
                Upstream = validation.Upstream,
                Methods = new List<HttpMethodType>(validation.Methods),
                Enabled = proxy.Enabled,
                Headers = validation.Headers.Select(ProxyVersionFactory.CloneKeyValue).ToList(),
                Query = validation.Query.Select(ProxyVersionFactory.CloneKeyValue).ToList(),
                BodyMerge = validation.BodyMerge.Select(ProxyVersionFactory.CloneKeyValue).ToList(),
                MethodConfigs = validation.MethodConfigs.Select(ProxyVersionFactory.CloneMethodConfig).ToList(),
                Routes = validation.Routes.Select(ProxyVersionFactory.CloneRoute).ToList(),
                ResponseMode = validation.ResponseMode,
                ResponseInclude = new List<string>(validation.ResponseInclude),
                Access = validation.Access.Clone(),
                Resilience = ProxyVersionFactory.CloneResilience(validation.Resilience),
                RequestsPerMinute = validation.RequestsPerMinute,
            };
            var changes = ProxyChangeSet.Diff(beforeSnapshot, candidate);

            // PX-9: bind the ids of the config being saved (the candidate), before deciding no-op vs change.
            var bindTarget = new ProxyDetailEntity
            {
                ItemId = proxy.ItemId,
                TenantId = proxy.TenantId,
                Name = validation.Name,
                Slug = proxy.Slug,
                Upstream = validation.Upstream,
                Headers = validation.Headers,
                Query = validation.Query,
                BodyMerge = validation.BodyMerge,
                MethodConfigs = validation.MethodConfigs,
                Routes = validation.Routes,
                SecretIds = proxy.SecretIds,
            };
            var (secretIds, missingVars) = await ProxySecretIdBinder.BindAsync(_variableResolver, tenantId, bindTarget, _logger);
            if (missingVars.Count > 0)
            {
                _logger.LogWarning(
                    "Proxy update {ItemId} for tenant {TenantId} rejected: unknown variable(s) [{Names}].",
                    itemId, tenantId, string.Join(", ", missingVars));
                return ProxyMutationResponse.Failure(
                    400, ProxyErrorCodes.Validation, "The proxy uses a secret that does not exist.",
                    ProxySecretIdBinder.UnknownVariablesError(missingVars));
            }

            if (changes.Count == 0)
            {
                // A save with no config change still refreshes the ids (e.g. a proxy saved before PX-9, or a secret
                // deleted and created again), without a version row.
                if (!ProxySecretIdBinder.SameIds(proxy.SecretIds, secretIds))
                {
                    proxy.SecretIds = secretIds;
                    // No version bump. If someone saved meanwhile, their save bound the ids already: nothing lost.
                    if (!await _proxyRepository.SaveConfigAsync(proxy, readVersion))
                    {
                        _logger.LogInformation(
                            "Proxy {ItemId} (tenant {TenantId}) changed during a no-op save; ids left to that save.",
                            itemId, tenantId);
                    }
                }

                _logger.LogInformation(
                    "Proxy update {ItemId} for tenant {TenantId} is a no-op; no version written.", itemId, tenantId);
                return ProxyMutationResponse.Success(proxy.ItemId, 200);
            }

            proxy.SecretIds = secretIds;
            proxy.Name = validation.Name;
            proxy.Upstream = validation.Upstream;
            proxy.Methods = validation.Methods;
            proxy.Headers = validation.Headers;
            proxy.Query = validation.Query;
            proxy.BodyMerge = validation.BodyMerge;
            proxy.MethodConfigs = validation.MethodConfigs;
            proxy.Routes = validation.Routes;
            proxy.ResponseMode = validation.ResponseMode;
            proxy.ResponseInclude = validation.ResponseInclude;
            proxy.Access = validation.Access;
            proxy.Resilience = validation.Resilience;
            proxy.RequestsPerMinute = validation.RequestsPerMinute;
            proxy.LastUpdatedDate = DateTime.UtcNow;
            proxy.LastUpdatedBy = ProxyVersionFactory.CurrentUserId();
            proxy.CurrentVersion += 1;

            if (!await _proxyRepository.SaveConfigAsync(proxy, readVersion))
            {
                return VersionConflict(itemId, tenantId, readVersion, null);
            }

            var afterSnapshot = ProxyVersionFactory.SnapshotOf(proxy);
            var version = ProxyVersionFactory.Build(
                proxy, proxy.CurrentVersion, ProxyVersionKind.ConfigUpdate, ProxyChangeSet.Summarize(changes),
                changes, afterSnapshot, proxy.LastUpdatedBy ?? "system", ProxyVersionFactory.CurrentUserName());
            await _proxyVersionRepository.InsertAsync(version);

            _logger.LogInformation(
                "Updated proxy {ItemId} for tenant {TenantId}; wrote version {Version}.",
                proxy.ItemId, tenantId, proxy.CurrentVersion);
            return ProxyMutationResponse.Success(proxy.ItemId, 200);
        }

        public async Task<ProxyMutationResponse> ToggleAsync(string tenantId, ProxyToggleRequestDto request)
        {
            var itemId = request.ItemId ?? string.Empty;
            _logger.LogInformation(
                "Toggling proxy {ItemId} to Enabled={Enabled} for tenant {TenantId}.", itemId, request.Enabled, tenantId);

            var proxy = await _proxyRepository.GetAsync(tenantId, itemId);
            if (proxy == null)
            {
                _logger.LogWarning("Proxy toggle rejected: {ItemId} not found for tenant {TenantId}.", itemId, tenantId);
                return ProxyMutationResponse.Failure(
                    404, ProxyErrorCodes.NotFound, $"Proxy '{itemId}' was not found.");
            }

            if (proxy.Enabled == request.Enabled)
            {
                _logger.LogInformation(
                    "Proxy toggle {ItemId} for tenant {TenantId} already at Enabled={Enabled}; no version written.",
                    itemId, tenantId, request.Enabled);
                return ProxyMutationResponse.Success(proxy.ItemId, 200);
            }

            if (IsStale(request.ExpectedVersion, proxy))
            {
                return VersionConflict(itemId, tenantId, request.ExpectedVersion, proxy.CurrentVersion);
            }

            var wasEnabled = proxy.Enabled;
            var readVersion = proxy.CurrentVersion;

            proxy.Enabled = request.Enabled;
            proxy.LastUpdatedDate = DateTime.UtcNow;
            proxy.LastUpdatedBy = ProxyVersionFactory.CurrentUserId();
            proxy.CurrentVersion += 1;

            if (!await _proxyRepository.SaveConfigAsync(proxy, readVersion))
            {
                return VersionConflict(itemId, tenantId, readVersion, null);
            }

            var snapshot = ProxyVersionFactory.SnapshotOf(proxy);
            var summary = request.Enabled ? "Proxy enabled" : "Proxy disabled";
            var changes = new List<ProxyFieldChange>
            {
                new()
                {
                    Field = "enabled",
                    Label = "status",
                    Before = wasEnabled ? "enabled" : "disabled",
                    After = request.Enabled ? "enabled" : "disabled",
                },
            };
            var version = ProxyVersionFactory.Build(
                proxy, proxy.CurrentVersion, ProxyVersionKind.Toggle, summary,
                changes, snapshot, proxy.LastUpdatedBy ?? "system", ProxyVersionFactory.CurrentUserName());
            await _proxyVersionRepository.InsertAsync(version);

            _logger.LogInformation(
                "Toggled proxy {ItemId} to Enabled={Enabled} for tenant {TenantId}; wrote version {Version}.",
                proxy.ItemId, request.Enabled, tenantId, proxy.CurrentVersion);
            return ProxyMutationResponse.Success(proxy.ItemId, 200);
        }

        public async Task<ProxyMutationResponse> DeleteAsync(string tenantId, ProxyDeleteRequestDto request)
        {
            var itemId = request.ItemId ?? string.Empty;
            _logger.LogInformation("Deleting proxy {ItemId} for tenant {TenantId}.", itemId, tenantId);

            var proxy = await _proxyRepository.GetAsync(tenantId, itemId);
            if (proxy == null)
            {
                _logger.LogWarning("Proxy delete rejected: {ItemId} not found for tenant {TenantId}.", itemId, tenantId);
                return ProxyMutationResponse.Failure(
                    404, ProxyErrorCodes.NotFound, $"Proxy '{itemId}' was not found.");
            }

            var snapshot = ProxyVersionFactory.SnapshotOf(proxy);
            var version = ProxyVersionFactory.Build(
                proxy, proxy.CurrentVersion + 1, ProxyVersionKind.Delete, "Proxy deleted",
                new List<ProxyFieldChange>(), snapshot, ProxyVersionFactory.CurrentUserId(),
                ProxyVersionFactory.CurrentUserName());

            // Delete only the version that was read (PX-16), THEN write the final history row: a save that won the
            // race keeps its own version number, and a refused delete leaves no "deleted" row behind. Versions are
            // retained.
            if (!await _proxyRepository.DeleteAsync(tenantId, proxy.ItemId, proxy.CurrentVersion))
            {
                return VersionConflict(itemId, tenantId, proxy.CurrentVersion, null);
            }

            await _proxyVersionRepository.InsertAsync(version);

            _logger.LogInformation(
                "Deleted proxy {ItemId} for tenant {TenantId}; wrote final version {Version}.",
                proxy.ItemId, tenantId, version.VersionNumber);
            return ProxyMutationResponse.Success(proxy.ItemId, 200);
        }

        private static bool IsStale(int? expectedVersion, ProxyDetailEntity proxy) =>
            expectedVersion is { } expected && expected != proxy.CurrentVersion;

        /// <summary>409 for a write that lost to another one (PX-16). Nothing was written.</summary>
        private ProxyMutationResponse VersionConflict(string itemId, string tenantId, int? expected, int? current)
        {
            _logger.LogWarning(
                "Proxy {ItemId} (tenant {TenantId}) write refused: expected version {Expected}, stored {Current}.",
                itemId, tenantId, expected, current?.ToString() ?? "changed meanwhile");
            return ProxyMutationResponse.Failure(
                409, ProxyErrorCodes.VersionConflict,
                "Someone else changed this proxy. Reload to see their changes, then try again.");
        }

        private static ProxyKeyValueDto ToKeyValueDto(ProxyKeyValue source) => new()
        {
            Key = source.Key,
            Value = source.Value,
        };

        private static ProxyRouteConfigDto ToRouteDto(ProxyRouteConfig source) => new()
        {
            Method = source.Method.Wire(),
            Path = source.Path,
            UpstreamPath = source.UpstreamPath,
            Headers = source.Headers?.Select(ToKeyValueDto).ToList(),
            Query = source.Query?.Select(ToKeyValueDto).ToList(),
            BodyMerge = source.BodyMerge?.Select(ToKeyValueDto).ToList(),
            ResponseMode = source.ResponseMode?.ToString(),
            ResponseInclude = source.ResponseInclude is null ? null : new List<string>(source.ResponseInclude),
            Resilience = ToResilienceDto(source.Resilience),
        };

        /// <summary>
        /// Absent stays absent all the way out: a caller must be able to tell "not configured" from
        /// "configured with the usual numbers", because only the first one inherits.
        /// </summary>
        private static ProxyResilienceDto? ToResilienceDto(ProxyResilienceConfig? source) =>
            source is null ? null : new ProxyResilienceDto
            {
                TimeoutSeconds = source.TimeoutSeconds,
                Retry = source.Retry is null ? null : new ProxyRetryDto
                {
                    Attempts = source.Retry.Attempts,
                    Backoff = source.Retry.Backoff.ToString(),
                    InitialDelaySeconds = source.Retry.InitialDelaySeconds,
                    Idempotent = source.Retry.Idempotent,
                },
                Breaker = source.Breaker is null ? null : new ProxyBreakerDto
                {
                    FailureThreshold = source.Breaker.FailureThreshold,
                    OpenSeconds = source.Breaker.OpenSeconds,
                },
            };

        private static ProxyAccessDto ToAccessDto(EndpointAccessPolicy source) => new()
        {
            Kind = source.Kind.ToString(),
            OrganizationId = source.OrganizationId,
            Roles = ToAccessRuleDto(source.Roles),
            Permissions = ToAccessRuleDto(source.Permissions),
            Combine = source.Combine.ToString(),
        };

        private static ProxyAccessRuleDto ToAccessRuleDto(EndpointAccessRule source) => new()
        {
            Mode = source.RequiresAll ? EndpointAccessRule.ModeAll : EndpointAccessRule.ModeAny,
            Values = new List<string>(source.Values),
        };

        private static ProxyMethodConfigDto ToMethodConfigDto(ProxyMethodConfig source) => new()
        {
            Method = source.Method.Wire(),
            Headers = source.Headers?.Select(ToKeyValueDto).ToList(),
            Query = source.Query?.Select(ToKeyValueDto).ToList(),
            Upstream = source.Upstream,
        };
    }
}
