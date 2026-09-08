using Microsoft.Extensions.Logging;
using Proxy.DomainService.Dtos;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Repositories;
using Proxy.DomainService.Utils;

namespace Proxy.DomainService.Services
{
    /// <summary>
    /// Serves the console's <em>Change history</em> tab and its <em>Revert</em> button. History reads keep
    /// working after a proxy is deleted; a revert requires the proxy to still exist.
    /// </summary>
    public sealed class ProxyVersionService : IProxyVersionService
    {
        private readonly IProxyRepository _proxyRepository;
        private readonly IProxyVersionRepository _proxyVersionRepository;
        private readonly ILogger<ProxyVersionService> _logger;

        public ProxyVersionService(
            IProxyRepository proxyRepository,
            IProxyVersionRepository proxyVersionRepository,
            ILogger<ProxyVersionService> logger)
        {
            _proxyRepository = proxyRepository;
            _proxyVersionRepository = proxyVersionRepository;
            _logger = logger;
        }

        public async Task<ProxyGetVersionsResponseDto> GetVersionsAsync(string tenantId, ProxyGetVersionsRequestDto request)
        {
            var proxyId = request.ProxyId ?? string.Empty;
            var pageSize = Math.Clamp(request.PageSize <= 0 ? 50 : request.PageSize, 1, 200);
            var pageNumber = Math.Max(0, request.PageNumber);

            _logger.LogInformation(
                "Fetching change history for proxy {ProxyId}, tenant {TenantId}. Page: {Page}, PageSize: {PageSize}",
                proxyId, tenantId, pageNumber, pageSize);

            var totalCount = await _proxyVersionRepository.CountForProxyAsync(tenantId, proxyId);
            if (totalCount == 0)
            {
                var proxy = await _proxyRepository.GetAsync(tenantId, proxyId);
                if (proxy == null)
                {
                    _logger.LogWarning(
                        "Change history rejected: proxy {ProxyId} not found for tenant {TenantId}.", proxyId, tenantId);
                    return new ProxyGetVersionsResponseDto
                    {
                        Data = null,
                        TotalCount = 0,
                        Errors = null,
                        Code = ProxyErrorCodes.NotFound,
                        Message = $"Proxy '{proxyId}' was not found.",
                        HttpStatus = 404,
                    };
                }
            }

            var versions = await _proxyVersionRepository.GetForProxyAsync(tenantId, proxyId, pageSize, pageNumber);
            var data = versions.Select(ToDto).ToList();

            _logger.LogInformation(
                "Returned {Returned} of {TotalCount} history rows for proxy {ProxyId}, tenant {TenantId}.",
                data.Count, totalCount, proxyId, tenantId);

            return new ProxyGetVersionsResponseDto { Data = data, TotalCount = totalCount, Errors = null };
        }

        public async Task<ProxyMutationResponse> RevertAsync(string tenantId, ProxyRevertRequestDto request)
        {
            var proxyId = request.ProxyId ?? string.Empty;
            var versionId = request.VersionId ?? string.Empty;
            _logger.LogInformation(
                "Reverting proxy {ProxyId} to version {VersionId} for tenant {TenantId}.", proxyId, versionId, tenantId);

            var sourceVersion = await _proxyVersionRepository.GetAsync(tenantId, versionId);
            if (sourceVersion == null || !string.Equals(sourceVersion.ProxyId, proxyId, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Revert rejected: version {VersionId} not found for proxy {ProxyId}, tenant {TenantId}.",
                    versionId, proxyId, tenantId);
                return ProxyMutationResponse.Failure(
                    404, ProxyErrorCodes.VersionNotFound, $"Version '{versionId}' was not found for this proxy.");
            }

            var proxy = await _proxyRepository.GetAsync(tenantId, proxyId);
            if (proxy == null)
            {
                _logger.LogWarning(
                    "Revert rejected: proxy {ProxyId} has been deleted (tenant {TenantId}).", proxyId, tenantId);
                return ProxyMutationResponse.Failure(
                    409, ProxyErrorCodes.Deleted, $"Proxy '{proxyId}' has been deleted.");
            }

            // "Revert this change" only: undo exactly the fields this version moved. Create / Delete / no-op
            // rows carry no change set and cannot be reverted.
            if (sourceVersion.Kind is ProxyVersionKind.Create or ProxyVersionKind.Delete
                || sourceVersion.Changes.Count == 0)
            {
                _logger.LogWarning(
                    "Revert rejected: version {VersionId} (kind {Kind}) has nothing to revert.",
                    versionId, sourceVersion.Kind);
                return ProxyMutationResponse.Failure(
                    400, ProxyErrorCodes.VersionNotRevertable, "This version has no change to revert.");
            }

            // Conflict guard: every field the source version changed must still hold that version's
            // resulting value. If a later version touched any of them, reject the whole revert.
            var conflicts = sourceVersion.Changes
                .Where(c => !ValueEquals(c.Field, ProxyChangeSet.ReadField(proxy, c.Field), c.After))
                .ToList();
            if (conflicts.Count > 0)
            {
                var fields = conflicts.ToDictionary(
                    c => c.Field, _ => "This field was changed again by a later version.", StringComparer.Ordinal);
                _logger.LogWarning(
                    "Revert rejected: version {VersionId} conflicts on [{Fields}] for proxy {ProxyId}, tenant {TenantId}.",
                    versionId, string.Join(", ", conflicts.Select(c => c.Label)), proxyId, tenantId);
                return ProxyMutationResponse.Failure(
                    409,
                    ProxyErrorCodes.RevertConflict,
                    $"Cannot revert: {string.Join(", ", conflicts.Select(c => c.Label))} changed again in a later version.",
                    fields);
            }

            // Revert restores stored field values directly (no ProxyConfigValidator run), so re-check the
            // SSRF guard on any upstream this revert would put back in place — before mutating the entity.
            foreach (var change in sourceVersion.Changes)
            {
                if ((change.Field == "upstream" || change.Field.EndsWith(":upstream", StringComparison.Ordinal))
                    && !string.IsNullOrWhiteSpace(change.Before)
                    && ProxyUpstreamGuard.IsDisallowedTarget(change.Before, out var guardReason))
                {
                    _logger.LogWarning(
                        "Revert rejected: version {VersionId} would restore a disallowed upstream '{Upstream}' for proxy {ProxyId}, tenant {TenantId}.",
                        versionId, ProxyUpstreamMasker.Mask(change.Before!), proxyId, tenantId);
                    return ProxyMutationResponse.Failure(
                        400, ProxyErrorCodes.Validation, guardReason,
                        new Dictionary<string, string>(StringComparer.Ordinal) { ["upstream"] = guardReason });
                }
            }

            var beforeSnapshot = ProxyVersionFactory.SnapshotOf(proxy);

            foreach (var change in sourceVersion.Changes)
            {
                ProxyChangeSet.ApplyField(proxy, change.Field, change.Before);
            }

            proxy.LastUpdatedDate = DateTime.UtcNow;
            proxy.LastUpdatedBy = ProxyVersionFactory.CurrentUserId();
            proxy.CurrentVersion += 1;

            await _proxyRepository.ReplaceAsync(proxy);

            var afterSnapshot = ProxyVersionFactory.SnapshotOf(proxy);
            var changes = ProxyChangeSet.Diff(beforeSnapshot, afterSnapshot);
            var newVersion = ProxyVersionFactory.Build(
                proxy, proxy.CurrentVersion, ProxyVersionKind.Revert,
                $"Reverted the change from v{sourceVersion.VersionNumber}",
                changes, afterSnapshot, proxy.LastUpdatedBy ?? "system");
            await _proxyVersionRepository.InsertAsync(newVersion);

            _logger.LogInformation(
                "Reverted the change from v{SourceVersion} on proxy {ProxyId} for tenant {TenantId}; wrote version {Version}.",
                sourceVersion.VersionNumber, proxyId, tenantId, proxy.CurrentVersion);
            return ProxyMutationResponse.Success(proxy.ItemId, 200);
        }

        /// <summary>
        /// Field-aware equality for the revert conflict guard: the <c>methods</c> address compares as an
        /// order-insensitive set; every other address is an ordinal string compare (<c>null</c> == <c>null</c>).
        /// </summary>
        private static bool ValueEquals(string field, string? a, string? b)
        {
            if (a is null || b is null)
            {
                return a is null && b is null;
            }

            if (field == "methods")
            {
                var setA = a.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var setB = b.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return new HashSet<string>(setA, StringComparer.Ordinal).SetEquals(setB);
            }

            return string.Equals(a, b, StringComparison.Ordinal);
        }

        private static ProxyVersionDto ToDto(ProxyVersionEntity version) => new()
        {
            ItemId = version.ItemId,
            VersionNumber = version.VersionNumber,
            Kind = version.Kind.ToString(),
            ChangeSummary = version.ChangeSummary,
            Changes = version.Changes.Select(c => new ProxyFieldChangeDto
            {
                Field = c.Field,
                Label = c.Label,
                Before = c.Before,
                After = c.After,
            }).ToList(),
            Who = version.CreatedBy,
            WhenUtc = version.CreatedDate,
            VersionLabel = $"v{version.VersionNumber}",
        };
    }
}
