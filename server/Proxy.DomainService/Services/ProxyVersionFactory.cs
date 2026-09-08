using Blocks.Genesis;
using Proxy.DomainService.Entities;

namespace Proxy.DomainService.Services
{
    /// <summary>
    /// Builds <see cref="ProxyVersionEntity"/> rows and deep-copies config so a snapshot never aliases the
    /// live <see cref="ProxyDetailEntity"/>. Shared by <see cref="ProxyService"/> and <see cref="ProxyVersionService"/>.
    /// </summary>
    internal static class ProxyVersionFactory
    {
        public static ProxyKeyValue CloneKeyValue(ProxyKeyValue source) => new()
        {
            Key = source.Key,
            Value = source.Value,
            IsSecretRef = source.IsSecretRef,
        };

        /// <summary>Deep-copies a per-method override so a snapshot never aliases the live entity's list.</summary>
        public static ProxyMethodConfig CloneMethodConfig(ProxyMethodConfig source) => new()
        {
            Method = source.Method,
            Headers = source.Headers?.Select(CloneKeyValue).ToList(),
            Query = source.Query?.Select(CloneKeyValue).ToList(),
            Upstream = source.Upstream,
        };

        /// <summary>Captures the full effective configuration of <paramref name="proxy"/> as an independent snapshot.</summary>
        public static ProxyConfigSnapshot SnapshotOf(ProxyDetailEntity proxy) => new()
        {
            Name = proxy.Name,
            Slug = proxy.Slug,
            Upstream = proxy.Upstream,
            Methods = new List<HttpMethodType>(proxy.Methods),
            Enabled = proxy.Enabled,
            Headers = proxy.Headers.Select(CloneKeyValue).ToList(),
            Query = proxy.Query.Select(CloneKeyValue).ToList(),
            MethodConfigs = proxy.MethodConfigs.Select(CloneMethodConfig).ToList(),
        };

        public static ProxyVersionEntity Build(
            ProxyDetailEntity proxy,
            int versionNumber,
            ProxyVersionKind kind,
            string changeSummary,
            List<ProxyFieldChange> changes,
            ProxyConfigSnapshot snapshot,
            string userId)
        {
            var now = DateTime.UtcNow;
            return new ProxyVersionEntity
            {
                ItemId = Guid.NewGuid().ToString("N"),
                TenantId = proxy.TenantId,
                ProxyId = proxy.ItemId,
                VersionNumber = versionNumber,
                Kind = kind,
                ChangeSummary = changeSummary,
                Changes = changes,
                Snapshot = snapshot,
                CreatedDate = now,
                LastUpdatedDate = now,
                CreatedBy = userId,
                LastUpdatedBy = userId,
            };
        }

        public static string CurrentUserId() => BlocksContext.GetContext()?.UserId ?? "system";
    }
}
