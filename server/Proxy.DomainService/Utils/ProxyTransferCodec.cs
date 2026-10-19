using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Proxy.DomainService.Dtos;
using Proxy.DomainService.Entities;
using Blocks.Genesis.Entities.Logic;

namespace Proxy.DomainService.Utils
{
    /// <summary>
    /// The proxy file format (export / import, 2026-10-18): stored config &rarr; <see cref="ProxyExportConfig"/>, the
    /// one canonical JSON its hash is taken over, and the <c>***</c> handling for literal credentials. Export,
    /// preview, apply and the tests all go through here, so "same" means the same thing everywhere.
    /// </summary>
    public static partial class ProxyTransferCodec
    {
        public const string Format = "blocks.proxy.export";
        public const int SchemaVersion = 1;

        /// <summary>The largest proxy config a file may carry, measured on its canonical JSON.</summary>
        public const int MaxConfigBytes = 256 * 1024;

        /// <summary>The wire shape of the file: camelCase, nulls left out (a null route list means "inherit").</summary>
        public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        [GeneratedRegex("^[0-9a-f]{32}$")]
        private static partial Regex ItemIdPattern();

        /// <summary>A proxy id as Create makes it: 32 lower-case hex characters (a GUID without dashes).</summary>
        public static bool IsValidItemId(string? itemId) => itemId is not null && ItemIdPattern().IsMatch(itemId);

        /// <summary>A stored slug is already in its derived form; anything else did not come from Blocks.</summary>
        public static bool IsValidSlug(string? slug) =>
            !string.IsNullOrEmpty(slug) && string.Equals(ProxySlug.From(slug), slug, StringComparison.Ordinal);

        /// <summary>
        /// The whole file for <paramref name="proxy"/>. Literal credentials become <c>***</c>; <c>{{$VAR.name}}</c>
        /// references stay; no tenant, audit, version, stats or secret id goes in.
        /// </summary>
        public static ProxyExportFile BuildFile(ProxyDetailEntity proxy, DateTime exportedAtUtc)
        {
            var masked = new List<string>();
            var config = ToExportConfig(proxy, masked);
            var node = ToJson(config);
            return new ProxyExportFile
            {
                Format = Format,
                SchemaVersion = SchemaVersion,
                ExportedAt = exportedAtUtc,
                Proxy = node,
                SecretNames = ProxyVarRef.NamesIn(proxy).ToList(),
                MaskedValues = masked,
                ContentHash = Hash(node),
            };
        }

        /// <summary>The config part of the file. <paramref name="maskedAddresses"/> collects every value written as <c>***</c>.</summary>
        /// <remarks>
        /// <paramref name="mask"/> false keeps every value as stored: only for feeding a merged config back through
        /// the validator, never for anything that leaves the server.
        /// </remarks>
        public static ProxyExportConfig ToExportConfig(
            ProxyDetailEntity proxy, List<string>? maskedAddresses = null, bool mask = true)
        {
            var masked = maskedAddresses ?? new List<string>();
            List<ProxyKeyValueInputDto>? Pairs(List<ProxyKeyValue>? pairs, string prefix, List<string> into) =>
                mask ? MaskedPairs(pairs, prefix, into) : pairs?.Select(kv => new ProxyKeyValueInputDto { Key = kv.Key, Value = kv.Value }).ToList();
            string MaskUrl(string url, string address, List<string> into) => mask ? MaskedUrl(url, address, into) : url;

            return new ProxyExportConfig
            {
                ItemId = proxy.ItemId,
                Slug = proxy.Slug,
                Name = proxy.Name,
                Upstream = MaskUrl(proxy.Upstream, "upstream", masked),
                Methods = proxy.Methods.Select(m => m.Wire()).ToList(),
                Enabled = proxy.Enabled,
                Access = new ProxyAccessInputDto
                {
                    Kind = proxy.Access.Kind.ToString(),
                    OrganizationId = proxy.Access.OrganizationId,
                    Roles = Rule(proxy.Access.Roles),
                    Permissions = Rule(proxy.Access.Permissions),
                    Combine = proxy.Access.Combine.ToString(),
                },
                Headers = Pairs(proxy.Headers, "header:", masked),
                Query = Pairs(proxy.Query, "query:", masked),
                BodyMerge = Pairs(proxy.BodyMerge, "body:", masked),
                MethodConfigs = proxy.MethodConfigs.Select(m => new ProxyMethodConfigInputDto
                {
                    Method = m.Method.Wire(),
                    Headers = Pairs(m.Headers, $"method:{m.Method.Wire()}:header:", masked),
                    Query = Pairs(m.Query, $"method:{m.Method.Wire()}:query:", masked),
                    Upstream = m.Upstream is null ? null : MaskUrl(m.Upstream, $"method:{m.Method.Wire()}:upstream", masked),
                }).ToList(),
                Routes = proxy.Routes.Select(r =>
                {
                    var at = $"route:{ProxyRouteCodec.AddressOf(r)}:";
                    return new ProxyRouteConfigInputDto
                    {
                        Method = r.Method.Wire(),
                        Path = r.Path,
                        UpstreamPath = r.UpstreamPath,
                        Headers = Pairs(r.Headers, at + "header:", masked),
                        Query = Pairs(r.Query, at + "query:", masked),
                        BodyMerge = Pairs(r.BodyMerge, at + "body:", masked),
                        ResponseMode = r.ResponseMode?.ToString(),
                        ResponseInclude = r.ResponseInclude is null ? null : new List<string>(r.ResponseInclude),
                        Resilience = Resilience(r.Resilience),
                    };
                }).ToList(),
                ResponseMode = proxy.ResponseMode.ToString(),
                ResponseInclude = new List<string>(proxy.ResponseInclude),
                Resilience = Resilience(proxy.Resilience),
                RequestsPerMinute = proxy.RequestsPerMinute,
            };
        }

        public static JsonObject ToJson(ProxyExportConfig config) =>
            JsonSerializer.SerializeToNode(config, Json)!.AsObject();

        /// <summary>
        /// The one canonical form: object keys sorted (ordinal) at every depth, array order kept, no whitespace
        /// unless <paramref name="indented"/>. Two configs are the same exactly when this text is.
        /// </summary>
        public static string Canonicalize(JsonNode? node, bool indented = false)
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = indented }))
            {
                Write(writer, node);
            }

            return Encoding.UTF8.GetString(buffer.ToArray());
        }

        /// <summary>sha256 of <see cref="Canonicalize"/>, lower-case hex.</summary>
        public static string Hash(JsonNode? node) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonicalize(node)))).ToLowerInvariant();

        private static void Write(Utf8JsonWriter writer, JsonNode? node)
        {
            switch (node)
            {
                case null:
                    writer.WriteNullValue();
                    return;
                case JsonObject obj:
                    writer.WriteStartObject();
                    foreach (var (key, value) in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                    {
                        writer.WritePropertyName(key);
                        Write(writer, value);
                    }

                    writer.WriteEndObject();
                    return;
                case JsonArray array:
                    writer.WriteStartArray();
                    foreach (var item in array)
                    {
                        Write(writer, item);
                    }

                    writer.WriteEndArray();
                    return;
                default:
                    node.WriteTo(writer);
                    return;
            }
        }

        /// <summary>
        /// The config as a version snapshot would hold it, with every literal credential replaced by <c>***</c> the
        /// way the file has it. Comparing this to an incoming file never shows (or diffs on) a stored secret.
        /// </summary>
        public static ProxyConfigSnapshot MaskSnapshot(ProxyConfigSnapshot snapshot)
        {
            var ignored = new List<string>();
            snapshot.Upstream = MaskedUrl(snapshot.Upstream, "upstream", ignored);
            MaskInPlace(snapshot.Headers);
            MaskInPlace(snapshot.Query);
            MaskInPlace(snapshot.BodyMerge);
            foreach (var method in snapshot.MethodConfigs)
            {
                MaskInPlace(method.Headers);
                MaskInPlace(method.Query);
                if (method.Upstream is not null) method.Upstream = MaskedUrl(method.Upstream, "upstream", ignored);
            }

            foreach (var route in snapshot.Routes)
            {
                MaskInPlace(route.Headers);
                MaskInPlace(route.Query);
                MaskInPlace(route.BodyMerge);
            }

            return snapshot;

            static void MaskInPlace(List<ProxyKeyValue>? pairs)
            {
                if (pairs is null) return;
                foreach (var pair in pairs) pair.Value = ProxySecretRedactor.MaskConfiguredValue(pair.Key, pair.Value) ?? string.Empty;
            }
        }

        /// <summary>
        /// Every place <paramref name="proxy"/> holds a <c>***</c> under a credential-looking key (or in an upstream
        /// query), as file addresses. Recomputed on import: the file's own list is never trusted.
        /// </summary>
        public static List<string> MaskedAddresses(ProxyDetailEntity proxy)
        {
            var found = new List<string>();
            if (IsMaskedUrl(proxy.Upstream)) found.Add("upstream");
            Collect(proxy.Headers, "header:");
            Collect(proxy.Query, "query:");
            Collect(proxy.BodyMerge, "body:");
            foreach (var m in proxy.MethodConfigs)
            {
                Collect(m.Headers, $"method:{m.Method.Wire()}:header:");
                Collect(m.Query, $"method:{m.Method.Wire()}:query:");
                if (IsMaskedUrl(m.Upstream)) found.Add($"method:{m.Method.Wire()}:upstream");
            }

            foreach (var r in proxy.Routes)
            {
                var at = $"route:{ProxyRouteCodec.AddressOf(r)}:";
                Collect(r.Headers, at + "header:");
                Collect(r.Query, at + "query:");
                Collect(r.BodyMerge, at + "body:");
            }

            return found;

            void Collect(List<ProxyKeyValue>? pairs, string prefix)
            {
                if (pairs is null) return;
                found.AddRange(pairs.Where(IsMaskedPair).Select(kv => prefix + kv.Key));
            }
        }

        /// <summary>
        /// Puts this environment's value back wherever <paramref name="target"/> carries a <c>***</c> from the file and
        /// <paramref name="current"/> has a value at the same place. A <c>***</c> with nothing to restore stays, so the
        /// owner sees it needs a value. Matching is by place: proxy-wide lists, per-method lists by method, route
        /// lists by route address, each by key.
        /// </summary>
        public static void RestoreMasked(ProxyDetailEntity target, ProxyConfigSnapshot? current)
        {
            if (current is null) return;

            target.Upstream = RestoreUrl(target.Upstream, current.Upstream) ?? string.Empty;
            Restore(target.Headers, current.Headers);
            Restore(target.Query, current.Query);
            Restore(target.BodyMerge, current.BodyMerge);

            foreach (var method in target.MethodConfigs)
            {
                var mine = current.MethodConfigs.FirstOrDefault(m => m.Method == method.Method);
                Restore(method.Headers, mine?.Headers);
                Restore(method.Query, mine?.Query);
                method.Upstream = RestoreUrl(method.Upstream, mine?.Upstream);
            }

            foreach (var route in target.Routes)
            {
                var address = ProxyRouteCodec.AddressOf(route);
                var mine = current.Routes.FirstOrDefault(
                    r => string.Equals(ProxyRouteCodec.AddressOf(r), address, StringComparison.Ordinal));
                Restore(route.Headers, mine?.Headers);
                Restore(route.Query, mine?.Query);
                Restore(route.BodyMerge, mine?.BodyMerge);
            }

            static void Restore(List<ProxyKeyValue>? incoming, List<ProxyKeyValue>? mine)
            {
                if (incoming is null || mine is null) return;
                foreach (var pair in incoming.Where(IsMaskedPair))
                {
                    var stored = mine.FirstOrDefault(kv => string.Equals(kv.Key, pair.Key, StringComparison.Ordinal));
                    if (stored is not null && !string.IsNullOrEmpty(stored.Value)) pair.Value = stored.Value;
                }
            }

            // An upstream is restored whole, and only when it is this environment's upstream with the same values
            // masked: anything else is a real change and stays as the file has it.
            static string? RestoreUrl(string? incoming, string? mine) =>
                incoming is not null && mine is not null && IsMaskedUrl(incoming)
                && string.Equals(MaskedUrl(mine, "upstream", new List<string>()), incoming, StringComparison.Ordinal)
                    ? mine
                    : incoming;
        }

        private static bool IsMaskedPair(ProxyKeyValue kv) =>
            string.Equals(kv.Value, ProxySecretRedactor.Mask, StringComparison.Ordinal) && ProxySecretRedactor.IsSensitiveKey(kv.Key);

        private static bool IsMaskedUrl(string? url) =>
            url is not null && url.Contains("=" + ProxySecretRedactor.Mask, StringComparison.Ordinal);

        private static List<ProxyKeyValueInputDto>? MaskedPairs(List<ProxyKeyValue>? pairs, string prefix, List<string> masked)
        {
            if (pairs is null) return null;

            return pairs.Select(kv =>
            {
                var shown = ProxySecretRedactor.MaskConfiguredValue(kv.Key, kv.Value);
                if (!string.Equals(shown, kv.Value, StringComparison.Ordinal)) masked.Add(prefix + kv.Key);
                return new ProxyKeyValueInputDto { Key = kv.Key, Value = shown };
            }).ToList();
        }

        /// <summary>
        /// A URL with the value of every credential-looking query key replaced by <c>***</c> (a <c>{{$VAR}}</c> value
        /// stays). Everything else, encoding included, is kept byte for byte.
        /// </summary>
        private static string MaskedUrl(string url, string address, List<string> masked)
        {
            var queryStart = url.IndexOf('?');
            if (queryStart < 0) return url;

            var fragmentStart = url.IndexOf('#', queryStart);
            var query = fragmentStart < 0 ? url[(queryStart + 1)..] : url[(queryStart + 1)..fragmentStart];
            var changed = false;
            var parts = query.Split('&').Select(part =>
            {
                var eq = part.IndexOf('=');
                if (eq <= 0) return part;
                var key = Uri.UnescapeDataString(part[..eq]);
                var value = part[(eq + 1)..];
                var shown = ProxySecretRedactor.MaskConfiguredValue(key, Uri.UnescapeDataString(value));
                if (shown != ProxySecretRedactor.Mask || value == ProxySecretRedactor.Mask) return part;
                changed = true;
                return part[..(eq + 1)] + ProxySecretRedactor.Mask;
            }).ToList();

            if (!changed) return url;

            masked.Add(address);
            return url[..(queryStart + 1)] + string.Join("&", parts) + (fragmentStart < 0 ? string.Empty : url[fragmentStart..]);
        }

        private static ProxyAccessRuleDto Rule(EndpointAccessRule rule) => new()
        {
            Mode = rule.RequiresAll ? EndpointAccessRule.ModeAll : EndpointAccessRule.ModeAny,
            Values = new List<string>(rule.Values),
        };

        private static ProxyResilienceInputDto? Resilience(ProxyResilienceConfig? source) =>
            source is null ? null : new ProxyResilienceInputDto
            {
                TimeoutSeconds = source.TimeoutSeconds,
                Retry = source.Retry is null ? null : new ProxyRetryInputDto
                {
                    Attempts = source.Retry.Attempts,
                    Backoff = source.Retry.Backoff.ToString(),
                    InitialDelaySeconds = source.Retry.InitialDelaySeconds,
                    Idempotent = source.Retry.Idempotent,
                },
                Breaker = source.Breaker is null ? null : new ProxyBreakerInputDto
                {
                    FailureThreshold = source.Breaker.FailureThreshold,
                    OpenSeconds = source.Breaker.OpenSeconds,
                },
            };
    }
}
