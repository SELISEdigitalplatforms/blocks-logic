using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Blocks.FunctionRunner.Runs
{
    /// <summary>
    /// Finds and substitutes the <c>{{secret.&lt;id&gt;}}</c> references in an envelope's
    /// <c>env</c> — the runner's half of "secrets are resolved by the runner, not the queue".
    /// <para>
    /// The control plane leaves a secret-bound variable as its reference (whole, or embedded as
    /// in <c>Bearer {{secret.abc}}</c>), so the run record in Redis and its retry copy never hold
    /// plaintext. The runner resolves the ids for the run's tenant immediately before the sandbox
    /// starts and substitutes them here; the resolved envelope exists only in this process's
    /// memory and in the per-run 0440 envelope file, which is deleted when the run ends. It is
    /// never written back to Redis.
    /// </para>
    /// <para>
    /// Pure and synchronous on purpose: the lookup is I/O and lives behind
    /// <see cref="SecretStore.IRunSecretResolver"/>, so everything that decides what reaches the
    /// sandbox can be tested without a secret store.
    /// </para>
    /// </summary>
    public static class EnvSecretReferences
    {
        /// <summary>
        /// The reference shape. Ids, never names — the same pattern as the control plane's
        /// <c>FunctionEnvelopeBuilder</c> and <c>OutputActionProcessor</c>; change all three together.
        /// </summary>
        private static readonly Regex Placeholder = new(@"\{\{secret\.([\w-]+)\}\}", RegexOptions.Compiled);

        /// <summary>
        /// An imported reference not yet bound to an id (2026-10-18): <c>{{secretName.&lt;name&gt;}}</c>, a Blocks OS secret
        /// name (<c>^[A-Za-z0-9][A-Za-z0-9._-]*$</c>, at most 100). The runner looks the name up exactly at run start and
        /// reads its value in the same batch as the ids. Same pattern as the control plane's <c>FunctionSecretRefs</c>.
        /// </summary>
        private static readonly Regex NamePlaceholder = new(@"\{\{secretName\.([A-Za-z0-9][A-Za-z0-9._-]{0,99})\}\}", RegexOptions.Compiled);

        /// <summary>Both shapes in one pattern, so substitution is a single pass and a value is never expanded again.</summary>
        private static readonly Regex Combined = new(
            @"\{\{secret\.(?<id>[\w-]+)\}\}|\{\{secretName\.(?<name>[A-Za-z0-9][A-Za-z0-9._-]{0,99})\}\}", RegexOptions.Compiled);

        /// <summary>The caller identity the control plane recorded in the envelope's <c>context</c>.</summary>
        public sealed record Caller(string? TenantId, string? UserId, string? OrganizationId, IReadOnlyList<string> Roles);

        /// <summary>What an envelope asks the runner to resolve.</summary>
        public sealed class Plan
        {
            internal Plan(
                IReadOnlyDictionary<string, IReadOnlyList<string>> idsByKey,
                Caller caller,
                IReadOnlyDictionary<string, IReadOnlyList<string>>? namesByKey = null)
            {
                IdsByKey = idsByKey;
                NamesByKey = namesByKey ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
                Caller = caller;
                Ids = idsByKey.Values.SelectMany(v => v).Distinct(StringComparer.Ordinal).ToArray();
                Names = NamesByKey.Values.SelectMany(v => v).Distinct(StringComparer.Ordinal).ToArray();
            }

            /// <summary>Env key &rarr; the secret <b>names</b> its value references (<c>{{secretName.x}}</c>), in order.</summary>
            public IReadOnlyDictionary<string, IReadOnlyList<string>> NamesByKey { get; }

            /// <summary>Every referenced name, once — looked up exactly, then read with <see cref="Ids"/> in one batch.</summary>
            public IReadOnlyCollection<string> Names { get; }

            /// <summary>Env key &rarr; the secret ids its value references, in order of appearance.</summary>
            public IReadOnlyDictionary<string, IReadOnlyList<string>> IdsByKey { get; }

            /// <summary>Every referenced id, once — the single batch the resolver is asked for.</summary>
            public IReadOnlyCollection<string> Ids { get; }

            public Caller Caller { get; }

            public bool IsEmpty => Ids.Count == 0 && Names.Count == 0;
        }

        /// <summary>
        /// Reads which env values carry references. Only the envelope's own top-level <c>env</c>
        /// is looked at — a reference in <c>input</c> is caller data and is never resolved, or a
        /// caller could send <c>{{secret.x}}</c> in a request body and read the value back.
        /// </summary>
        /// <exception cref="ExecutionEnvelope.ForbiddenContentException">The envelope is not a JSON object.</exception>
        public static Plan Collect(string envelopeJson)
        {
            var root = ParseObject(envelopeJson);

            var idsByKey = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            var namesByKey = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            if (root["env"] is JsonObject env)
            {
                foreach (var (key, node) in env)
                {
                    if (node is not JsonValue value || !value.TryGetValue<string>(out var text)) continue;
                    var ids = Placeholder.Matches(text).Select(m => m.Groups[1].Value).ToList();
                    if (ids.Count > 0) idsByKey[key] = ids;
                    var names = NamePlaceholder.Matches(text).Select(m => m.Groups[1].Value).ToList();
                    if (names.Count > 0) namesByKey[key] = names;
                }
            }

            return new Plan(idsByKey, ReadCaller(root["context"] as JsonObject), namesByKey);
        }

        /// <summary>
        /// The envelope with every reference replaced by its value, <c>maskedEnv</c> widened to
        /// every key that carried one, and <c>maskedValues</c> set to the individual values.
        /// <para>
        /// <c>maskedValues</c> is what closes the embedded-reference gap: masking the whole of
        /// <c>Bearer sk_live_x</c> does nothing for a log line holding only <c>sk_live_x</c>, so
        /// the bootstrap is handed each secret value as well (it masks longest first). It is set
        /// here and nowhere else — whatever the control plane sent in that field is replaced.
        /// </para>
        /// <para>
        /// Substitution is a single pass: a value that itself contains <c>{{secret.y}}</c> is
        /// delivered as that text, never expanded again.
        /// </para>
        /// </summary>
        /// <exception cref="KeyNotFoundException">
        /// A referenced id is missing from <paramref name="values"/>. The caller checks first and
        /// fails the run instead; the message names the id only.
        /// </exception>
        public static string Apply(string envelopeJson, Plan plan, IReadOnlyDictionary<string, string> values)
            => Apply(envelopeJson, plan, values, idsByName: null);

        /// <summary>
        /// <see cref="Apply(string, Plan, IReadOnlyDictionary{string, string})"/> for a plan that also carries names:
        /// <paramref name="idsByName"/> is the one id each name was bound to for this run. Every token — id or name —
        /// is replaced in the same single pass.
        /// </summary>
        public static string Apply(
            string envelopeJson, Plan plan, IReadOnlyDictionary<string, string> values,
            IReadOnlyDictionary<string, string>? idsByName)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(values);

            var root = ParseObject(envelopeJson);
            if (plan.IsEmpty) return envelopeJson;

            var env = root["env"] as JsonObject
                ?? throw new InvalidOperationException("the plan names env keys but the envelope has no env");

            foreach (var key in plan.IdsByKey.Keys.Union(plan.NamesByKey.Keys, StringComparer.Ordinal))
            {
                var text = env[key]!.GetValue<string>();
                env[key] = Combined.Replace(text, match =>
                {
                    if (match.Groups["id"].Success)
                    {
                        var id = match.Groups["id"].Value;
                        return values.TryGetValue(id, out var secret)
                            ? secret
                            : throw new KeyNotFoundException($"secret '{id}' referenced by '{key}' was not resolved");
                    }

                    var name = match.Groups["name"].Value;
                    return idsByName is not null && idsByName.TryGetValue(name, out var boundId)
                           && values.TryGetValue(boundId, out var named)
                        ? named
                        : throw new KeyNotFoundException($"secret named '{name}' referenced by '{key}' was not resolved");
                });
            }

            var masked = new List<string>();
            if (root["maskedEnv"] is JsonArray existing)
            {
                foreach (var item in existing)
                {
                    if (item is JsonValue v && v.TryGetValue<string>(out var k)) masked.Add(k);
                }
            }
            masked.AddRange(plan.IdsByKey.Keys);
            masked.AddRange(plan.NamesByKey.Keys);
            root["maskedEnv"] = new JsonArray(masked.Distinct(StringComparer.Ordinal)
                .Select(k => (JsonNode?)JsonValue.Create(k)).ToArray());

            var boundIds = plan.Names
                .Select(n => idsByName is not null && idsByName.TryGetValue(n, out var id) ? id : null)
                .Where(id => id is not null)
                .Select(id => id!);
            root["maskedValues"] = new JsonArray(plan.Ids.Concat(boundIds)
                .Distinct(StringComparer.Ordinal)
                .Select(id => values[id])
                .Where(v => !string.IsNullOrEmpty(v))
                .Distinct(StringComparer.Ordinal)
                .Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());

            return root.ToJsonString();
        }

        private static JsonObject ParseObject(string envelopeJson)
        {
            try
            {
                return JsonNode.Parse(envelopeJson) as JsonObject
                    ?? throw new ExecutionEnvelope.ForbiddenContentException("the execution envelope is not a JSON object");
            }
            catch (JsonException ex)
            {
                throw new ExecutionEnvelope.ForbiddenContentException($"the execution envelope is not valid JSON: {ex.Message}");
            }
        }

        private static Caller ReadCaller(JsonObject? context)
        {
            static string? Text(JsonNode? node) =>
                node is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;

            var roles = new List<string>();
            if (context?["roles"] is JsonArray array)
            {
                foreach (var item in array)
                {
                    if (Text(item) is { } role) roles.Add(role);
                }
            }

            return new Caller(
                Text(context?["tenantId"]), Text(context?["userId"]), Text(context?["organizationId"]), roles);
        }
    }
}
