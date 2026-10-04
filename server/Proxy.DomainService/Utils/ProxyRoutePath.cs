namespace Proxy.DomainService.Utils
{
    /// <summary>
    /// Parsing, validation and matching for the path templates on <c>ProxyRouteConfig</c>.
    /// <para>
    /// A template is a <c>/</c>-separated list of segments with no leading or trailing slash. A segment is
    /// either a literal or a parameter written <c>{name}</c>, which matches exactly one segment and captures
    /// it for substitution into the upstream template. The empty template is the base path itself.
    /// </para>
    /// A dot segment (<c>.</c> / <c>..</c>) is rejected in a template and never matches in an incoming path:
    /// the whole point of the allowlist is that the caller cannot walk out of the endpoints the tenant
    /// declared, and <c>Uri</c> normalizes <c>..</c> away after the URL is built.
    /// </summary>
    public static class ProxyRoutePath
    {
        /// <summary>Most routes one proxy may declare.</summary>
        public const int MaxRoutes = 50;

        /// <summary>Longest a single path template may be, before or after rewrite.</summary>
        public const int MaxPathLength = 512;

        /// <summary>Most segments one template may have.</summary>
        public const int MaxSegments = 20;

        private static readonly string[] NoSegments = Array.Empty<string>();

        /// <summary>Trims surrounding whitespace and slashes. <c>null</c> / blank becomes <c>""</c>, the base path.</summary>
        public static string Normalize(string? path) => (path ?? string.Empty).Trim().Trim('/');

        /// <summary>Splits a normalized path into segments. The base path yields an empty array, not <c>[""]</c>.</summary>
        public static string[] Split(string normalized) =>
            normalized.Length == 0 ? NoSegments : normalized.Split('/');

        /// <summary><c>true</c> when the segment is a <c>{name}</c> parameter with a usable name.</summary>
        public static bool IsParameter(string segment, out string name)
        {
            name = string.Empty;
            if (segment.Length < 3 || segment[0] != '{' || segment[^1] != '}')
            {
                return false;
            }

            var inner = segment[1..^1];
            if (!char.IsLetter(inner[0]) && inner[0] != '_')
            {
                return false;
            }

            foreach (var c in inner)
            {
                if (!char.IsLetterOrDigit(c) && c != '_')
                {
                    return false;
                }
            }

            name = inner;
            return true;
        }

        /// <summary>
        /// Validates a normalized template and collects its parameter names in order. <paramref name="reason"/>
        /// carries a human message when the result is <c>false</c>.
        /// </summary>
        public static bool TryParseTemplate(string normalized, out List<string> parameters, out string reason)
        {
            parameters = new List<string>();
            reason = string.Empty;

            if (normalized.Length > MaxPathLength)
            {
                reason = $"A path must be {MaxPathLength} characters or fewer.";
                return false;
            }

            var segments = Split(normalized);
            if (segments.Length > MaxSegments)
            {
                reason = $"A path must have {MaxSegments} segments or fewer.";
                return false;
            }

            foreach (var segment in segments)
            {
                if (segment.Length == 0)
                {
                    reason = "A path must not contain an empty segment.";
                    return false;
                }

                if (segment is "." or "..")
                {
                    reason = "A path must not contain a '.' or '..' segment.";
                    return false;
                }

                if (!segment.Contains('{') && !segment.Contains('}'))
                {
                    continue;
                }

                if (IsParameter(segment, out var name))
                {
                    if (parameters.Contains(name, StringComparer.Ordinal))
                    {
                        reason = $"Parameter '{name}' is declared more than once.";
                        return false;
                    }

                    parameters.Add(name);
                    continue;
                }

                // Two distinct mistakes, and the message has to tell them apart: a malformed name inside
                // otherwise correct braces, versus braces glued to literal text (which would silently become
                // a literal segment and send a call somewhere nobody intended).
                reason = segment[0] == '{' && segment[^1] == '}'
                    ? $"'{segment}' is not a valid parameter; use a name of letters, digits or underscore."
                    : $"'{segment}' mixes literal text with a parameter; a parameter must be a whole segment.";
                return false;
            }

            return true;
        }

        /// <summary><c>true</c> when any segment of an incoming path is <c>.</c> or <c>..</c>.</summary>
        public static bool HasDotSegment(string path)
        {
            foreach (var segment in Split(Normalize(path)))
            {
                if (segment is "." or "..")
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Matches an incoming path's segments against a template. Segment counts must be equal; literals
        /// compare ordinally; a parameter captures one non-empty segment. <paramref name="captures"/> is
        /// <c>null</c> when the template declares no parameters, so the common case allocates nothing.
        /// </summary>
        public static bool TryMatch(
            string template, IReadOnlyList<string> segments, out Dictionary<string, string>? captures,
            out int literalSegments)
        {
            captures = null;
            literalSegments = 0;

            var templateSegments = Split(template);
            if (templateSegments.Length != segments.Count)
            {
                return false;
            }

            for (var i = 0; i < templateSegments.Length; i++)
            {
                var templateSegment = templateSegments[i];
                var candidate = segments[i];

                if (IsParameter(templateSegment, out var name))
                {
                    // An empty capture would let "a//b" satisfy "a/{id}/b" and forward a malformed URL.
                    if (candidate.Length == 0)
                    {
                        captures = null;
                        return false;
                    }

                    captures ??= new Dictionary<string, string>(StringComparer.Ordinal);
                    captures[name] = candidate;
                    continue;
                }

                if (!string.Equals(templateSegment, candidate, StringComparison.Ordinal))
                {
                    captures = null;
                    literalSegments = 0;
                    return false;
                }

                literalSegments++;
            }

            return true;
        }

        /// <summary>
        /// Replaces each <c>{name}</c> in <paramref name="template"/> with its captured segment. Values are
        /// substituted raw; the forwarder percent-encodes per segment when it builds the outbound URL.
        /// </summary>
        public static string Substitute(string template, IReadOnlyDictionary<string, string>? captures)
        {
            var segments = Split(template);
            if (segments.Length == 0)
            {
                return string.Empty;
            }

            var parts = new string[segments.Length];
            for (var i = 0; i < segments.Length; i++)
            {
                parts[i] = IsParameter(segments[i], out var name)
                    && captures is not null
                    && captures.TryGetValue(name, out var value)
                        ? value
                        : segments[i];
            }

            return string.Join('/', parts);
        }
    }
}
