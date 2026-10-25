using System.Text;

namespace Functions.DomainService.Utils
{
    /// <summary>
    /// The registry name a function build is tagged and pushed under.
    /// <para>
    /// <b>New form (2026-10-18):</b> <c>{registry}/fn/{tenant}/{functionId}:{tag}</c>. The tenant is in the name
    /// because an imported function keeps its id in every environment (export/import), so two tenants sharing one
    /// registry can now hold the same function id — under the old name their images would share a repository, and
    /// Image GC or a pin release for one could remove the other's manifest.
    /// </para>
    /// <para>
    /// <b>Old form:</b> <c>{registry}/fn/{functionId}:{tag}</c>. Never rewritten: a version keeps the digest it was
    /// deployed with (<c>FunctionVersionEntity.ImageDigest</c>) and keeps running from it. Every consumer of these
    /// names — the runner's push / digest selection / Image GC (<c>RegistryClient.TryParse</c>,
    /// <c>ImageGc.RepositoryOf</c>, <c>BuildProcessor.SelectPushedDigest</c>) and the keep set — treats the whole
    /// repository path as opaque, so both forms work side by side; <see cref="TryParse"/> reads either.
    /// </para>
    /// </summary>
    public static class FunctionImageRef
    {
        /// <summary>A Docker path component may be long, but the full name must stay readable; tenant ids are short.</summary>
        private const int MaxTenantSegment = 64;

        /// <summary>
        /// The repository for a new build. Falls back to the old form only when the tenant id leaves nothing usable
        /// after it is made registry-safe (it never does for a real tenant).
        /// </summary>
        public static string Repository(string registry, string tenantId, string functionId)
        {
            var tenant = TenantSegment(tenantId);
            var function = functionId.Trim().ToLowerInvariant();
            return tenant.Length == 0
                ? $"{registry}/fn/{function}"
                : $"{registry}/fn/{tenant}/{function}";
        }

        /// <summary>The full reference for a new build: <see cref="Repository"/> plus the tag.</summary>
        public static string For(string registry, string tenantId, string functionId, string tag) =>
            $"{Repository(registry, tenantId, functionId)}:{tag}";

        /// <summary>
        /// The tenant id as a Docker path component: lower case, only <c>[a-z0-9]</c> with single <c>.</c>, <c>_</c> or
        /// <c>-</c> between them (the OCI distribution grammar). Anything else becomes <c>-</c>, runs collapse, and
        /// separators are trimmed from both ends.
        /// </summary>
        public static string TenantSegment(string? tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId)) return string.Empty;
            var buffer = new StringBuilder(tenantId.Length);
            foreach (var raw in tenantId.Trim().ToLowerInvariant())
            {
                var ch = raw is (>= 'a' and <= 'z') or (>= '0' and <= '9') ? raw : '-';
                if (ch == '-' && (buffer.Length == 0 || buffer[^1] == '-')) continue;
                buffer.Append(ch);
            }
            var segment = buffer.ToString().Trim('-');
            if (segment.Length > MaxTenantSegment) segment = segment[..MaxTenantSegment].TrimEnd('-');
            return segment;
        }

        /// <summary>
        /// Reads a function image reference in either form — with a tag, a digest, or both. <paramref name="tenant"/> is
        /// null for the old form. False for anything that is not a function image under <paramref name="registry"/>.
        /// </summary>
        public static bool TryParse(string? reference, string registry, out string? tenant, out string functionId)
        {
            tenant = null;
            functionId = string.Empty;
            if (string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(registry)) return false;

            var prefix = registry.TrimEnd('/') + "/fn/";
            if (!reference.StartsWith(prefix, StringComparison.Ordinal)) return false;

            var path = reference[prefix.Length..];
            var at = path.IndexOf('@', StringComparison.Ordinal);
            if (at >= 0) path = path[..at];
            var colon = path.LastIndexOf(':');
            if (colon >= 0) path = path[..colon];

            var parts = path.Split('/');
            if (parts.Any(p => p.Length == 0)) return false;
            switch (parts.Length)
            {
                case 1:
                    functionId = parts[0];
                    return true;
                case 2:
                    tenant = parts[0];
                    functionId = parts[1];
                    return true;
                default:
                    return false;
            }
        }
    }
}
