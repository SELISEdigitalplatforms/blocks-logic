using Proxy.DomainService.Entities;

namespace Proxy.DomainService.Utils
{
    /// <summary>
    /// Computes the structured per-field change set between two <see cref="ProxyConfigSnapshot"/>s and applies
    /// / reads a single field address on a live <see cref="ProxyDetailEntity"/>. Header and query diffing is
    /// key-based and order-insensitive: reordering rows is not a change; a value edit, add, or remove is.
    /// </summary>
    public static class ProxyChangeSet
    {
        private const string EnabledWord = "enabled";
        private const string DisabledWord = "disabled";
        private const string HeaderPrefix = "header:";
        private const string QueryPrefix = "query:";
        private const string BodyPrefix = "body:";
        private const string MethodPrefix = "method:";

        private static readonly IReadOnlyList<ProxyKeyValue> NoPairs = Array.Empty<ProxyKeyValue>();

        /// <summary>Field-level diff, newest state in <paramref name="after"/>. Empty when the two are identical.</summary>
        public static List<ProxyFieldChange> Diff(ProxyConfigSnapshot before, ProxyConfigSnapshot after)
        {
            var changes = new List<ProxyFieldChange>();

            if (!string.Equals(before.Name, after.Name, StringComparison.Ordinal))
            {
                changes.Add(new ProxyFieldChange { Field = "name", Label = "name", Before = before.Name, After = after.Name });
            }

            if (!string.Equals(before.Upstream, after.Upstream, StringComparison.Ordinal))
            {
                changes.Add(new ProxyFieldChange
                {
                    Field = "upstream", Label = "upstream", Before = before.Upstream, After = after.Upstream,
                });
            }

            if (before.Enabled != after.Enabled)
            {
                changes.Add(new ProxyFieldChange
                {
                    Field = "enabled",
                    Label = "status",
                    Before = before.Enabled ? EnabledWord : DisabledWord,
                    After = after.Enabled ? EnabledWord : DisabledWord,
                });
            }

            if (!MethodSetsEqual(before.Methods, after.Methods))
            {
                changes.Add(new ProxyFieldChange
                {
                    Field = "methods",
                    Label = "methods",
                    Before = MethodsValue(before.Methods),
                    After = MethodsValue(after.Methods),
                });
            }

            DiffPairs(before.Headers, after.Headers, HeaderPrefix, "header", changes);
            DiffPairs(before.Query, after.Query, QueryPrefix, "query", changes);
            DiffPairs(before.BodyMerge, after.BodyMerge, BodyPrefix, "body field", changes);
            DiffMethodConfigs(before.MethodConfigs, after.MethodConfigs, changes);

            return changes;
        }

        /// <summary>
        /// Per-method override diff. Each method is compared field-by-field; an absent <see cref="ProxyMethodConfig"/>
        /// or a <c>null</c> member reads as "inherit", so the addresses behave exactly like the shared
        /// <c>upstream</c> / <c>header:&lt;key&gt;</c> / <c>query:&lt;key&gt;</c> ones (add / remove / change).
        /// </summary>
        private static void DiffMethodConfigs(
            IReadOnlyList<ProxyMethodConfig> before,
            IReadOnlyList<ProxyMethodConfig> after,
            List<ProxyFieldChange> changes)
        {
            foreach (var method in HttpMethodTypeExtensions.All)
            {
                var b = before.FirstOrDefault(c => c.Method == method);
                var a = after.FirstOrDefault(c => c.Method == method);
                if (b is null && a is null)
                {
                    continue;
                }

                var wire = method.Wire();

                if (!string.Equals(b?.Upstream, a?.Upstream, StringComparison.Ordinal))
                {
                    changes.Add(new ProxyFieldChange
                    {
                        Field = $"{MethodPrefix}{wire}:upstream",
                        Label = $"{wire} upstream",
                        Before = b?.Upstream,
                        After = a?.Upstream,
                    });
                }

                DiffPairs(b?.Headers ?? NoPairs, a?.Headers ?? NoPairs, $"{MethodPrefix}{wire}:header:", $"{wire} header", changes);
                DiffPairs(b?.Query ?? NoPairs, a?.Query ?? NoPairs, $"{MethodPrefix}{wire}:query:", $"{wire} query", changes);
            }
        }

        /// <summary>
        /// The headline sentence for a version row, derived from its change set (SPEC &sect;8.4). Falls back to
        /// <c>"Configuration updated"</c> whenever more than one field moved or no special-case fits.
        /// </summary>
        public static string Summarize(IReadOnlyList<ProxyFieldChange> changes)
        {
            if (changes.Count == 1)
            {
                var change = changes[0];

                if (change.Field == "enabled")
                {
                    return string.Equals(change.After, EnabledWord, StringComparison.Ordinal)
                        ? "Proxy enabled"
                        : "Proxy disabled";
                }

                if (change.Field == "methods")
                {
                    var before = SplitMethods(change.Before);
                    var after = SplitMethods(change.After);
                    var added = after.Except(before).ToList();
                    var removed = before.Except(after).ToList();
                    if (added.Count == 1 && removed.Count == 0)
                    {
                        return $"Method {added[0]} allowed";
                    }

                    if (removed.Count == 1 && added.Count == 0)
                    {
                        return $"Method {removed[0]} removed";
                    }
                }

                if ((change.Field.StartsWith(HeaderPrefix, StringComparison.Ordinal)
                        || change.Field.StartsWith(QueryPrefix, StringComparison.Ordinal))
                    && change.Before is { } literalBefore
                    && !ProxyVarRef.ContainsRef(literalBefore)
                    && change.After is { } refAfter
                    && ProxyVarRef.ContainsRef(refAfter))
                {
                    TrySplitPair(change.Field, out _, out var key);
                    return $"{key} credential switched to a configuration variable";
                }

                if (TrySplitBodyField(change.Field, out var bodyKey))
                {
                    if (change.Before is null)
                    {
                        return $"body field {bodyKey} added";
                    }

                    return change.After is null
                        ? $"body field {bodyKey} removed"
                        : $"body field {bodyKey} changed";
                }

                if (TryParseMethodField(change.Field, out var overrideMethod, out var overrideTail))
                {
                    var wire = overrideMethod.Wire();
                    if (overrideTail == "upstream")
                    {
                        return change.After is null
                            ? $"{wire} upstream override removed"
                            : $"{wire} upstream overridden";
                    }

                    if (TrySplitPair(overrideTail, out var isHeader, out var overrideKey))
                    {
                        var kind = isHeader ? "header" : "query";
                        return change.After is null
                            ? $"{wire} {kind} {overrideKey} override removed"
                            : $"{wire} {kind} {overrideKey} overridden";
                    }
                }
            }

            return "Configuration updated";
        }

        private static List<string> SplitMethods(string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? new List<string>()
                : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        /// <summary>The mirror image of <paramref name="change"/> &mdash; what an undo of it would record.</summary>
        public static ProxyFieldChange Invert(ProxyFieldChange change) => new()
        {
            Field = change.Field,
            Label = change.Label,
            Before = change.After,
            After = change.Before,
        };

        /// <summary>
        /// Reads the raw current value of one field address on <paramref name="proxy"/>. Returns <c>null</c>
        /// for a <c>header:</c> / <c>query:</c> address whose key is not present.
        /// </summary>
        public static string? ReadField(ProxyDetailEntity proxy, string field)
        {
            switch (field)
            {
                case "name": return proxy.Name;
                case "upstream": return proxy.Upstream;
                case "enabled": return proxy.Enabled ? EnabledWord : DisabledWord;
                case "methods": return MethodsValue(proxy.Methods);
            }

            if (TryParseMethodField(field, out var method, out var tail))
            {
                var entry = proxy.MethodConfigs.FirstOrDefault(c => c.Method == method);
                if (entry is null)
                {
                    return null;
                }

                if (tail == "upstream")
                {
                    return entry.Upstream;
                }

                if (TrySplitPair(tail, out var isOverrideHeader, out var overrideKey))
                {
                    var overrideList = isOverrideHeader ? entry.Headers : entry.Query;
                    return overrideList?.FirstOrDefault(kv => string.Equals(kv.Key, overrideKey, StringComparison.Ordinal))?.Value;
                }

                return null;
            }

            if (TrySplitBodyField(field, out var bodyKey))
            {
                return proxy.BodyMerge
                    .FirstOrDefault(kv => string.Equals(kv.Key, bodyKey, StringComparison.Ordinal))?.Value;
            }

            if (TrySplitPair(field, out var isHeader, out var key))
            {
                var list = isHeader ? proxy.Headers : proxy.Query;
                return list.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.Ordinal))?.Value;
            }

            return null;
        }

        /// <summary>
        /// Writes <paramref name="rawValue"/> onto one field address of <paramref name="proxy"/>. A
        /// <c>null</c> value on a <c>header:</c> / <c>query:</c> address removes the row; a non-null value adds
        /// or updates it. The value (any <c>{{$VAR.name}}</c> token included) is stored verbatim.
        /// </summary>
        public static void ApplyField(ProxyDetailEntity proxy, string field, string? rawValue)
        {
            switch (field)
            {
                case "name":
                    proxy.Name = rawValue ?? string.Empty;
                    return;
                case "upstream":
                    proxy.Upstream = rawValue ?? string.Empty;
                    return;
                case "enabled":
                    proxy.Enabled = string.Equals(rawValue, EnabledWord, StringComparison.Ordinal);
                    return;
                case "methods":
                    proxy.Methods = ParseMethods(rawValue);
                    return;
            }

            if (TryParseMethodField(field, out var method, out var tail))
            {
                ApplyMethodField(proxy, method, tail, rawValue);
                return;
            }

            if (TrySplitBodyField(field, out var bodyKey))
            {
                proxy.BodyMerge = ApplyPair(proxy.BodyMerge, bodyKey, rawValue) ?? new List<ProxyKeyValue>();
                return;
            }

            if (!TrySplitPair(field, out var isHeader, out var key))
            {
                return;
            }

            var list = isHeader ? proxy.Headers : proxy.Query;
            var existingIndex = list.FindIndex(kv => string.Equals(kv.Key, key, StringComparison.Ordinal));

            if (rawValue is null)
            {
                if (existingIndex >= 0)
                {
                    list.RemoveAt(existingIndex);
                }

                return;
            }

            var updated = new ProxyKeyValue
            {
                Key = key,
                Value = rawValue,
            };

            if (existingIndex >= 0)
            {
                list[existingIndex] = updated;
            }
            else
            {
                list.Add(updated);
            }
        }

        /// <summary>First-occurrence-ordered wire names joined with <c>", "</c> (e.g. <c>"GET, POST"</c>).</summary>
        public static string MethodsValue(IEnumerable<HttpMethodType> methods) =>
            string.Join(", ", methods.Select(m => m.Wire()));

        /// <summary>
        /// Splits a <c>method:&lt;M&gt;:&lt;tail&gt;</c> address. <paramref name="tail"/> is the remainder after the
        /// method segment, e.g. <c>"upstream"</c>, <c>"header:Authorization"</c>, <c>"query:tag"</c>.
        /// </summary>
        private static bool TryParseMethodField(string field, out HttpMethodType method, out string tail)
        {
            method = default;
            tail = string.Empty;

            if (!field.StartsWith(MethodPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var rest = field[MethodPrefix.Length..];
            var separator = rest.IndexOf(':');
            if (separator <= 0 || separator == rest.Length - 1)
            {
                return false;
            }

            if (!HttpMethodTypeExtensions.TryParse(rest[..separator], out method))
            {
                return false;
            }

            tail = rest[(separator + 1)..];
            return true;
        }

        private static void ApplyMethodField(ProxyDetailEntity proxy, HttpMethodType method, string tail, string? rawValue)
        {
            var entry = proxy.MethodConfigs.FirstOrDefault(c => c.Method == method);
            if (entry is null)
            {
                entry = new ProxyMethodConfig { Method = method };
                proxy.MethodConfigs.Add(entry);
            }

            if (tail == "upstream")
            {
                entry.Upstream = rawValue;
            }
            else if (TrySplitPair(tail, out var isHeader, out var key))
            {
                if (isHeader)
                {
                    entry.Headers = ApplyPair(entry.Headers, key, rawValue);
                }
                else
                {
                    entry.Query = ApplyPair(entry.Query, key, rawValue);
                }
            }

            // Prune an override that no longer overrides anything (SPEC D-feature §2.5).
            if (entry.Upstream is null
                && (entry.Headers is null || entry.Headers.Count == 0)
                && (entry.Query is null || entry.Query.Count == 0))
            {
                proxy.MethodConfigs.Remove(entry);
            }
        }

        /// <summary>
        /// Add / update / remove one key in a nullable override list. A <c>null</c> <paramref name="rawValue"/>
        /// removes the key; a non-null value adds or replaces it (stored verbatim).
        /// Returns the list to store back (a <c>null</c> input list stays <c>null</c> on a remove).
        /// </summary>
        private static List<ProxyKeyValue>? ApplyPair(List<ProxyKeyValue>? list, string key, string? rawValue)
        {
            if (rawValue is null)
            {
                var removeIndex = list?.FindIndex(kv => string.Equals(kv.Key, key, StringComparison.Ordinal)) ?? -1;
                if (removeIndex >= 0)
                {
                    list!.RemoveAt(removeIndex);
                }

                return list;
            }

            list ??= new List<ProxyKeyValue>();
            var updated = new ProxyKeyValue
            {
                Key = key,
                Value = rawValue,
            };

            var existingIndex = list.FindIndex(kv => string.Equals(kv.Key, key, StringComparison.Ordinal));
            if (existingIndex >= 0)
            {
                list[existingIndex] = updated;
            }
            else
            {
                list.Add(updated);
            }

            return list;
        }

        private static void DiffPairs(
            IReadOnlyList<ProxyKeyValue> before,
            IReadOnlyList<ProxyKeyValue> after,
            string fieldPrefix,
            string labelWord,
            List<ProxyFieldChange> changes)
        {
            var beforeByKey = ToMap(before);
            var afterByKey = ToMap(after);

            // Removed / value-changed, walked in the "before" order for a stable diff.
            foreach (var kv in before)
            {
                if (beforeByKey.TryGetValue(kv.Key, out var wasFirst) && !ReferenceEquals(wasFirst, kv))
                {
                    continue; // only act on the first occurrence of a key
                }

                afterByKey.TryGetValue(kv.Key, out var afterValue);
                if (afterValue is null)
                {
                    changes.Add(Pair(fieldPrefix, labelWord, kv.Key, kv.Value, null));
                }
                else if (!string.Equals(kv.Value, afterValue.Value, StringComparison.Ordinal))
                {
                    changes.Add(Pair(fieldPrefix, labelWord, kv.Key, kv.Value, afterValue.Value));
                }
            }

            // Added.
            foreach (var kv in after)
            {
                if (afterByKey.TryGetValue(kv.Key, out var wasFirst) && !ReferenceEquals(wasFirst, kv))
                {
                    continue;
                }

                if (!beforeByKey.ContainsKey(kv.Key))
                {
                    changes.Add(Pair(fieldPrefix, labelWord, kv.Key, null, kv.Value));
                }
            }
        }

        private static Dictionary<string, ProxyKeyValue> ToMap(IReadOnlyList<ProxyKeyValue> pairs)
        {
            var map = new Dictionary<string, ProxyKeyValue>(StringComparer.Ordinal);
            foreach (var kv in pairs)
            {
                map.TryAdd(kv.Key, kv);
            }

            return map;
        }

        private static ProxyFieldChange Pair(string fieldPrefix, string labelWord, string key, string? before, string? after) => new()
        {
            Field = fieldPrefix + key,
            Label = $"{labelWord} {key}",
            Before = before,
            After = after,
        };

        private static bool TrySplitPair(string field, out bool isHeader, out string key)
        {
            if (field.StartsWith(HeaderPrefix, StringComparison.Ordinal))
            {
                isHeader = true;
                key = field[HeaderPrefix.Length..];
                return true;
            }

            if (field.StartsWith(QueryPrefix, StringComparison.Ordinal))
            {
                isHeader = false;
                key = field[QueryPrefix.Length..];
                return true;
            }

            isHeader = false;
            key = string.Empty;
            return false;
        }

        /// <summary>Splits a <c>body:&lt;key&gt;</c> address into its (ordinal, case-sensitive) JSON key.</summary>
        private static bool TrySplitBodyField(string field, out string key)
        {
            if (field.StartsWith(BodyPrefix, StringComparison.Ordinal))
            {
                key = field[BodyPrefix.Length..];
                return true;
            }

            key = string.Empty;
            return false;
        }

        private static bool MethodSetsEqual(IEnumerable<HttpMethodType> a, IEnumerable<HttpMethodType> b) =>
            new HashSet<HttpMethodType>(a).SetEquals(b);

        private static List<HttpMethodType> ParseMethods(string? rawValue)
        {
            var result = new List<HttpMethodType>();
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return result;
            }

            foreach (var token in rawValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (HttpMethodTypeExtensions.TryParse(token, out var parsed) && !result.Contains(parsed))
                {
                    result.Add(parsed);
                }
            }

            return result;
        }
    }
}
