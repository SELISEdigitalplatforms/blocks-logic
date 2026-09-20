using System.Text.RegularExpressions;
using Proxy.DomainService.Entities;

namespace Proxy.DomainService.Utils
{
    /// <summary>
    /// Detects and rewrites <c>{{$VAR.name}}</c> configuration-variable tokens in a stored header / query /
    /// body-merge value. The token is resolved on the fly by the forwarder (name &rarr; id &rarr; value in
    /// Blocks Secrets) and never persisted; everything here operates on the verbatim stored string.
    /// <para>
    /// Syntax: the literal <c>{{$VAR.</c> prefix, a name in <c>[A-Za-z0-9._:-]</c>, then <c>}}</c>. The prefix
    /// and name are case-sensitive; a token whose name falls outside that charset is not recognised and is
    /// treated as literal text.
    /// </para>
    /// </summary>
    public static partial class ProxyVarRef
    {
        [GeneratedRegex(@"\{\{\$VAR\.([A-Za-z0-9._:-]+)\}\}")]
        private static partial Regex Pattern();

        /// <summary><c>true</c> when <paramref name="value"/> contains at least one <c>{{$VAR.name}}</c> token.</summary>
        public static bool ContainsRef(string? value) =>
            !string.IsNullOrEmpty(value) && Pattern().IsMatch(value);

        /// <summary>
        /// Every <c>{{$VAR.name}}</c> literal in <paramref name="value"/>, in the order it appears, with
        /// duplicates. Used by the Overview tile, which de-duplicates across a proxy's fields itself.
        /// </summary>
        public static IEnumerable<string> Tokens(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                yield break;
            }

            foreach (Match match in Pattern().Matches(value))
            {
                yield return match.Value;
            }
        }

        /// <summary>
        /// Every distinct variable NAME referenced across the given pair lists (ordinal, case-sensitive), in
        /// first-seen order. A <c>null</c> list is skipped. Feeds the single batched resolve per forward.
        /// </summary>
        public static IEnumerable<string> Names(params IEnumerable<ProxyKeyValue>?[] lists)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var list in lists)
            {
                if (list is null)
                {
                    continue;
                }

                foreach (var pair in list)
                {
                    foreach (Match match in Pattern().Matches(pair.Value ?? string.Empty))
                    {
                        var name = match.Groups[1].Value;
                        if (seen.Add(name))
                        {
                            yield return name;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Replaces each <c>{{$VAR.name}}</c> in <paramref name="value"/> with <c>map[name]</c>. A name that is
        /// not in <paramref name="map"/> is a caller bug (the resolve step must have supplied every name
        /// <see cref="Names"/> reported) and throws <see cref="KeyNotFoundException"/>.
        /// </summary>
        public static string Substitute(string? value, IReadOnlyDictionary<string, string> map)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value ?? string.Empty;
            }

            return Pattern().Replace(value, m =>
            {
                var name = m.Groups[1].Value;
                if (!map.TryGetValue(name, out var resolved))
                {
                    throw new KeyNotFoundException(
                        $"Configuration variable '{name}' was not resolved before substitution.");
                }

                return resolved;
            });
        }
    }
}
