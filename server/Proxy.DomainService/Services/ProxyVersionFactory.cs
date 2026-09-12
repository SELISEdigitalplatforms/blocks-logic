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
            BodyMerge = proxy.BodyMerge.Select(CloneKeyValue).ToList(),
            MethodConfigs = proxy.MethodConfigs.Select(CloneMethodConfig).ToList(),
            ResponseMode = proxy.ResponseMode,
            ResponseInclude = new List<string>(proxy.ResponseInclude),
        };

        public static ProxyVersionEntity Build(
            ProxyDetailEntity proxy,
            int versionNumber,
            ProxyVersionKind kind,
            string changeSummary,
            List<ProxyFieldChange> changes,
            ProxyConfigSnapshot snapshot,
            string userId,
            string? userName = null)
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
                CreatedByName = userName,
            };
        }

        public static string CurrentUserId() => BlocksContext.GetContext()?.UserId ?? "system";

        /// <summary>
        /// Display name of the current user, or <c>null</c> when the ambient context carries none. Kept nullable
        /// (no <c>"system"</c> sentinel) so the console can fall back to resolving <c>CreatedBy</c> against IAM.
        /// <para>
        /// <see cref="BlocksContext.DisplayName"/> first, <see cref="BlocksContext.UserName"/> only as a
        /// fallback: the stored value is rendered verbatim in the console's <em>Change history</em> column, and
        /// <c>UserName</c> is the login handle ("mjones") where <c>DisplayName</c> is the human name
        /// ("Mary Jones"). Change history rows are written once and never recomputed, so picking the wrong one
        /// here is permanent for every row it writes.
        /// </para>
        /// </summary>
        public static string? CurrentUserName()
        {
            var context = BlocksContext.GetContext();
            var name = context?.DisplayName;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = context?.UserName;
            }

            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
    }
}
