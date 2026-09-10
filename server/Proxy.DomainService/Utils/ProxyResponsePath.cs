using System.Text.RegularExpressions;

namespace Proxy.DomainService.Utils
{
    /// <summary>
    /// Grammar and parser for a proxy response-field path expression
    /// (<c>segment ( "." segment )*</c> where <c>segment = KEY [ "[]" ]</c> and
    /// <c>KEY = [^.\[\]]+</c>). The <c>[]</c> suffix marks "this key holds an array": it is preserved in
    /// storage and shown in the UI, but <see cref="ProxyResponseProjector"/>'s matcher ignores it (matching is
    /// by key, and any array encountered is auto-iterated). Array indices, slices, wildcards, recursive
    /// descent, and keys that literally contain <c>.</c> <c>[</c> <c>]</c> are not supported.
    /// <para>Pure, synchronous, offline &mdash; it never touches a response.</para>
    /// </summary>
    public static partial class ProxyResponsePath
    {
        [GeneratedRegex(@"^(?:[^.\[\]]+(?:\[\])?)(?:\.[^.\[\]]+(?:\[\])?)*$")]
        private static partial Regex Pattern();

        /// <summary>Max number of stored path expressions per proxy. Enforced at save and mirrored in the FE schema.</summary>
        public const int MaxPaths = 200;

        /// <summary>Max length of one path expression, in characters. Enforced at save and mirrored in the FE.</summary>
        public const int MaxPathLength = 512;

        /// <summary>
        /// Max number of segments in one path expression &mdash; also the deepest individually-selectable node
        /// in the console's field-tree builder.
        /// </summary>
        public const int MaxSegments = 25;

        /// <summary>One parsed path segment: the key, and whether the stored form carried the <c>[]</c> marker.</summary>
        public readonly record struct Segment(string Key, bool ArrayEach);

        /// <summary>
        /// Grammar + length + segment-count check. Returns <c>false</c> for <c>null</c>, whitespace-only,
        /// over-length, over-segment, or ungrammatical input. Does not touch a response.
        /// </summary>
        public static bool IsValid(string? path)
        {
            if (string.IsNullOrEmpty(path) || path.Length > MaxPathLength)
            {
                return false;
            }

            if (!Pattern().IsMatch(path))
            {
                return false;
            }

            return CountSegments(path) <= MaxSegments;
        }

        /// <summary>
        /// <c>"items[].id"</c> &rarr; <c>[Segment("items", true), Segment("id", false)]</c>. Assumes
        /// <see cref="IsValid"/> holds for <paramref name="path"/>.
        /// </summary>
        public static Segment[] Parse(string path)
        {
            var raw = path.Split('.');
            var segments = new Segment[raw.Length];
            for (var i = 0; i < raw.Length; i++)
            {
                var token = raw[i];
                var isArray = token.EndsWith("[]", StringComparison.Ordinal);
                var key = isArray ? token[..^2] : token;
                segments[i] = new Segment(key, isArray);
            }

            return segments;
        }

        private static int CountSegments(string path)
        {
            var count = 1;
            foreach (var c in path)
            {
                if (c == '.')
                {
                    count++;
                }
            }

            return count;
        }
    }
}
