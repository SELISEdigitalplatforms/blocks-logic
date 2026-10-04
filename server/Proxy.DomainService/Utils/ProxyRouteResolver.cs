using Proxy.DomainService.Entities;

namespace Proxy.DomainService.Utils
{
    /// <summary>Why a path was accepted or refused by the route allowlist.</summary>
    public enum ProxyRouteStatus
    {
        /// <summary>A route accepted the path for this method (or the proxy has no routes and this is its base path).</summary>
        Allowed,

        /// <summary>No declared route has this path shape, or the path carries a <c>.</c> / <c>..</c> segment.</summary>
        NotAllowed,

        /// <summary>A route has this path shape, but not for the requested method.</summary>
        MethodMismatch,
    }

    /// <summary>The outcome of matching one incoming path against a proxy's routes.</summary>
    public sealed class ProxyRouteResolution
    {
        public required ProxyRouteStatus Status { get; init; }

        /// <summary>
        /// The matched route, or <c>null</c> when the proxy declares no routes and the caller asked for its
        /// base path — in which case every shared value applies unchanged.
        /// </summary>
        public ProxyRouteConfig? Route { get; init; }

        /// <summary>
        /// The path to append to the upstream, with parameters substituted and no leading slash. <c>""</c>
        /// means call the upstream exactly as configured.
        /// </summary>
        public string UpstreamPathSuffix { get; init; } = string.Empty;

        /// <summary>Wire names of the methods declared for this path, for the <c>Allow</c> header on a 405.</summary>
        public IReadOnlyList<string> AllowedMethods { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// Matches an incoming path against a proxy's route allowlist and rewrites it to the upstream path.
    /// <para>
    /// An empty route list is deliberately strict: only the proxy's base path is callable. A proxy is then
    /// one-to-one with a single endpoint, which is the safe default for a component whose whole job is to
    /// hold a credential the caller never sees — widening it to a family of endpoints is something the
    /// tenant opts into by declaring routes.
    /// </para>
    /// Among competing routes the most literal one wins (<c>charges/summary</c> over <c>charges/{id}</c>),
    /// so behaviour does not depend on the order the console happened to save them in.
    /// </summary>
    public static class ProxyRouteResolver
    {
        private static readonly ProxyRouteResolution Refused = new() { Status = ProxyRouteStatus.NotAllowed };

        public static ProxyRouteResolution Resolve(
            IReadOnlyList<ProxyRouteConfig> routes, HttpMethodType method, string? pathSuffix)
        {
            var normalized = ProxyRoutePath.Normalize(pathSuffix);

            // Checked before matching: Uri collapses "a/../b" after the URL is built, so a dot segment that
            // slipped through would escape whichever route accepted it.
            if (ProxyRoutePath.HasDotSegment(normalized))
            {
                return Refused;
            }

            if (routes.Count == 0)
            {
                return normalized.Length == 0
                    ? new ProxyRouteResolution { Status = ProxyRouteStatus.Allowed }
                    : Refused;
            }

            var segments = ProxyRoutePath.Split(normalized);

            ProxyRouteConfig? best = null;
            Dictionary<string, string>? bestCaptures = null;
            var bestSpecificity = -1;

            // Allocated only when a path matches under some OTHER method, which is the one case its value is
            // read. Every forward that resolves normally leaves this null.
            List<string>? otherMethods = null;

            foreach (var route in routes)
            {
                var template = ProxyRoutePath.Normalize(route.Path);
                if (!ProxyRoutePath.TryMatch(template, segments, out var captures, out var literalSegments))
                {
                    continue;
                }

                if (route.Method != method)
                {
                    var wire = route.Method.Wire();
                    otherMethods ??= new List<string>(2);
                    if (!otherMethods.Contains(wire, StringComparer.Ordinal))
                    {
                        otherMethods.Add(wire);
                    }

                    continue;
                }

                if (literalSegments > bestSpecificity)
                {
                    best = route;
                    bestCaptures = captures;
                    bestSpecificity = literalSegments;
                }
            }

            if (best is null)
            {
                return otherMethods is not null
                    ? new ProxyRouteResolution { Status = ProxyRouteStatus.MethodMismatch, AllowedMethods = otherMethods }
                    : Refused;
            }

            // A null UpstreamPath means "no rewrite": the client-facing template is also the upstream one.
            var upstreamTemplate = best.UpstreamPath is null
                ? ProxyRoutePath.Normalize(best.Path)
                : ProxyRoutePath.Normalize(best.UpstreamPath);

            var suffix = ProxyRoutePath.Substitute(upstreamTemplate, bestCaptures);

            // Belt and braces. The incoming path was already checked and stored templates are validated on
            // the way in, so this can only fire on data that reached the database another way. It is one
            // string scan on the happy path, against a class of bug whose blast radius is the credential.
            return ProxyRoutePath.HasDotSegment(suffix)
                ? Refused
                : new ProxyRouteResolution
                {
                    Status = ProxyRouteStatus.Allowed,
                    Route = best,
                    UpstreamPathSuffix = suffix,
                };
        }
    }
}
