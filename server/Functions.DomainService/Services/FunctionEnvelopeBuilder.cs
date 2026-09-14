using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Blocks.Genesis;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Models;

namespace Functions.DomainService.Services
{
    /// <summary>
    /// Builds the execution envelope — the only channel into a sandbox.
    /// <para>
    /// This is the narrowest and most consequential piece of the control plane. Spec §17 lists
    /// what must never travel into a sandbox: access and refresh tokens, client secrets,
    /// service account keys, Redis and Mongo credentials, vault and registry tokens. A function
    /// receives <i>identity metadata</i>, not credentials.
    /// </para>
    /// <para>
    /// Two habits keep that true. The envelope is <b>constructed from named fields</b>, never
    /// by serialising a context object — so a new property added to <c>BlocksContext</c>
    /// upstream cannot silently start appearing inside tenant sandboxes. And the result is
    /// screened before it is handed over, so a mistake anywhere in this file fails the run
    /// instead of leaking. The runner screens it again on arrival; this is the first of two
    /// independent checks, not the only one.
    /// </para>
    /// <para>
    /// <b>One deliberate exception</b>: a variable's value may be a <c>{{secret.&lt;id&gt;}}</c>
    /// reference, resolved here so the function reads the real value as <c>ctx.env.NAME</c>.
    /// Those are the tenant's <i>own</i> secrets, chosen explicitly in the editor — spec §17 is
    /// about the platform's credentials, which still never appear. Because the value genuinely
    /// is a credential, it is resolved as late as possible (at invoke, not at deploy) and is
    /// never written to the version snapshot: the snapshot keeps the reference.
    /// </para>
    /// </summary>
    public static class FunctionEnvelopeBuilder
    {
        /// <summary>
        /// Property-name fragments that must never appear in an envelope outside <c>env</c>, at
        /// any depth. Matched case-insensitively. Mirrors the runner's own screen.
        /// </summary>
        private static readonly string[] ForbiddenKeyFragments =
        [
            "accesstoken", "access_token", "refreshtoken", "refresh_token",
            "clientsecret", "client_secret", "servicekey", "service_account",
            "connectionstring", "connection_string", "password", "passwd",
            "vaulttoken", "vault_token", "apikey", "api_key", "privatekey", "private_key",
            "secret", "credential", "bearer", "authorization", "oauthtoken", "oauth_token",
        ];

        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

        /// <summary>
        /// <c>{{secret.&lt;id&gt;}}</c> in a variable value. Ids, never names — the same shape
        /// and the same reasoning as <see cref="OutputActionProcessor"/>'s: renaming a secret in
        /// the catalog must not break a function that already references it.
        /// </summary>
        private static readonly Regex SecretPlaceholder = new(@"\{\{secret\.([\w-]+)\}\}", RegexOptions.Compiled);

        /// <summary>Thrown when an envelope fails screening. The run must not be enqueued.</summary>
        public sealed class ForbiddenContentException(string message) : Exception(message);

        /// <summary>
        /// Thrown when a variable references a secret this tenant cannot resolve — deleted,
        /// renamed away, or never readable. The run fails instead of starting, because the
        /// alternative is handing the sandbox the literal <c>{{secret.id}}</c> text and letting
        /// the function present it to a payment provider as if it were a key.
        /// </summary>
        public sealed class UnresolvedSecretException(string message) : Exception(message);

        /// <summary>
        /// Builds the envelope for one run.
        /// </summary>
        /// <param name="run">The run being started.</param>
        /// <param name="version">
        /// The deployed version, whose snapshots supply <c>ctx.env</c> and the limits. Null for
        /// a Test run, which uses the function's editor configuration instead.
        /// </param>
        /// <param name="function">The function, used only when <paramref name="version"/> is null.</param>
        /// <param name="context">
        /// The trusted caller context. Only the identity fields named below are read from it;
        /// notably <c>OAuthToken</c> is not, and must never be.
        /// </param>
        /// <param name="inputJson">The caller's input as raw JSON, or null.</param>
        /// <param name="secrets">
        /// Secret id &rarr; plaintext, for the ids <see cref="CollectSecretIds"/> reported. Every
        /// referenced id must be present: a missing one fails the run rather than reaching the
        /// sandbox as literal placeholder text.
        /// </param>
        public static string Build(
            FunctionRunEntity run,
            FunctionVersionEntity? version,
            FunctionEntity function,
            BlocksContext? context,
            string? inputJson,
            IReadOnlyDictionary<string, string>? secrets = null)
        {
            ArgumentNullException.ThrowIfNull(run);
            ArgumentNullException.ThrowIfNull(function);

            var limits = (version?.Limits ?? function.Limits).Clamp();
            var variables = version?.Variables ?? function.Variables;
            var authMode = (version?.Trigger ?? function.Trigger).AuthMode;

            var envelope = new JsonObject
            {
                ["run"] = new JsonObject
                {
                    ["id"] = run.ItemId,
                    ["functionId"] = run.FunctionId,
                    ["version"] = run.VersionNumber,
                    ["attempt"] = run.Attempt,
                    ["invokedBy"] = new JsonObject
                    {
                        ["type"] = InvokedByWire(run.InvokedBy),
                        ["id"] = run.InvokedById,
                    },
                },
                ["context"] = BuildIdentity(context, authMode),
                ["env"] = BuildEnv(variables, secrets, out var maskedEnvKeys),
                // The keys whose values came from a secret, so the sandbox can mask those values
                // out of every log line it writes. Keys only — the values are already in `env`,
                // and naming them twice would be one more place a secret can be read from.
                //
                // Not "envSecrets": the envelope screen on both sides rejects any property whose
                // name contains "secret", and it is right to, so the marker is named for what it
                // does instead.
                ["maskedEnv"] = new JsonArray(maskedEnvKeys.Select(k => (JsonNode?)JsonValue.Create(k)).ToArray()),
                ["input"] = ParseInput(inputJson),
                ["limits"] = new JsonObject
                {
                    ["timeoutMs"] = limits.TimeoutSeconds * 1000,
                },
            };

            var json = envelope.ToJsonString(SerializerOptions);

            var bytes = Encoding.UTF8.GetByteCount(json);
            if (bytes > FunctionLimits.Ceiling.InputBytes)
            {
                throw new ForbiddenContentException(
                    $"the execution envelope is {bytes} bytes, over the " +
                    $"{FunctionLimits.Ceiling.InputBytes} byte input ceiling");
            }

            Screen(json);
            return json;
        }

        /// <summary>
        /// The secret ids referenced by the variables this run will use, deduplicated. The
        /// caller resolves them and hands the values back to <see cref="Build"/> — the lookup
        /// is I/O and this type stays synchronous and pure, which is what makes the screening
        /// guarantees here testable without a secret store.
        /// </summary>
        public static IReadOnlyCollection<string> CollectSecretIds(
            FunctionVersionEntity? version, FunctionEntity function)
        {
            ArgumentNullException.ThrowIfNull(function);

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var variable in version?.Variables ?? function.Variables)
            {
                if (string.IsNullOrEmpty(variable.Value)) continue;
                foreach (Match match in SecretPlaceholder.Matches(variable.Value))
                {
                    ids.Add(match.Groups[1].Value);
                }
            }
            return ids;
        }

        /// <summary>
        /// The public identity surface (spec §16). Built field by field; absent values become
        /// explicit nulls and empties so <c>ctx.context.userId === null</c> is always a
        /// meaningful test rather than an accident of serialisation.
        /// </summary>
        private static JsonObject BuildIdentity(BlocksContext? context, AuthMode authMode)
        {
            // A public function must not inherit a privileged service identity (spec §18),
            // even if the request happened to arrive with a usable token attached.
            if (authMode == AuthMode.Public || context is null)
            {
                return new JsonObject
                {
                    ["tenantId"] = context?.TenantId,
                    ["userId"] = null,
                    ["organizationId"] = null,
                    ["roles"] = new JsonArray(),
                    ["permissions"] = new JsonArray(),
                    ["email"] = null,
                    ["isAuthenticated"] = false,
                    ["impersonated"] = false,
                    ["applicationDomain"] = context?.ApplicationDomain,
                };
            }

            return new JsonObject
            {
                ["tenantId"] = context.TenantId,
                ["userId"] = context.UserId,
                ["organizationId"] = context.OrganizationId,
                ["roles"] = ToArray(context.Roles),
                ["permissions"] = ToArray(context.Permissions),
                ["email"] = context.Email,
                ["isAuthenticated"] = context.IsAuthenticated,
                ["impersonated"] = context.Impersonated,
                ["applicationDomain"] = context.ApplicationDomain,
            };
        }

        /// <summary>
        /// <c>ctx.env</c> from the version's variable snapshot, with every
        /// <c>{{secret.&lt;id&gt;}}</c> reference replaced by its value.
        /// <para>
        /// A reference can be embedded rather than whole (<c>Bearer {{secret.abc}}</c>), so this
        /// substitutes in place rather than swapping the value wholesale. An id with no resolved
        /// value throws: the function would otherwise receive the placeholder text and send it
        /// upstream as though it were a key, which fails far away from the cause and can look
        /// like a provider outage rather than a deleted secret.
        /// </para>
        /// </summary>
        private static JsonObject BuildEnv(
            IEnumerable<VariableBinding>? variables,
            IReadOnlyDictionary<string, string>? secrets,
            out List<string> maskedEnvKeys)
        {
            var env = new JsonObject();
            maskedEnvKeys = [];
            if (variables is null) return env;

            foreach (var variable in variables)
            {
                if (string.IsNullOrWhiteSpace(variable.Key)) continue;

                // A binding is secret-backed when its stored value carries a reference, whether
                // the whole value is one or it is spliced into a larger string. Either way the
                // resolved value is a credential and must not appear in the run's logs.
                if (!string.IsNullOrEmpty(variable.Value) && SecretPlaceholder.IsMatch(variable.Value))
                {
                    maskedEnvKeys.Add(variable.Key);
                }

                env[variable.Key] = Substitute(variable.Key, variable.Value, secrets);
            }
            return env;
        }

        private static string? Substitute(
            string key, string? value, IReadOnlyDictionary<string, string>? secrets)
        {
            if (string.IsNullOrEmpty(value)) return value;

            var unresolved = new List<string>();
            var substituted = SecretPlaceholder.Replace(value, match =>
            {
                var id = match.Groups[1].Value;
                if (secrets is not null && secrets.TryGetValue(id, out var secret)) return secret;
                unresolved.Add(id);
                return match.Value;
            });

            if (unresolved.Count > 0)
            {
                // Ids only. The whole point of the failure is that there was no value to leak.
                throw new UnresolvedSecretException(
                    $"variable '{key}' references {(unresolved.Count == 1 ? "a secret" : "secrets")} " +
                    $"that could not be resolved: {string.Join(", ", unresolved)}");
            }

            return substituted;
        }

        private static JsonNode? ParseInput(string? inputJson)
        {
            if (string.IsNullOrWhiteSpace(inputJson)) return null;
            try
            {
                return JsonNode.Parse(inputJson);
            }
            catch (JsonException)
            {
                // A caller's malformed input is their problem to see, not a reason to hand the
                // sandbox a broken envelope: it arrives as a JSON string it can inspect.
                return JsonValue.Create(inputJson);
            }
        }

        private static JsonArray ToArray(IEnumerable<string>? values)
        {
            var array = new JsonArray();
            if (values is null) return array;
            foreach (var value in values) array.Add(value);
            return array;
        }

        private static string InvokedByWire(InvokedByType type) => type switch
        {
            InvokedByType.Http => "http",
            InvokedByType.Workflow => "workflow",
            InvokedByType.Test => "test",
            InvokedByType.Replay => "replay",
            InvokedByType.Schedule => "schedule",
            InvokedByType.Event => "event",
            _ => "http",
        };

        /// <summary>
        /// Rejects an envelope carrying anything credential-shaped. Keys only: screening values
        /// would reject legitimate input such as a note that happens to mention a password.
        /// <para>
        /// <c>env</c> is exempt, and only <c>env</c>. Its keys are variable names the tenant
        /// authored, already constrained to an identifier by <c>VariableBindingValidator</c>,
        /// and <see cref="BuildEnv"/> copies nothing else into it — so no platform credential
        /// can ever surface as an <c>env</c> key, and screening there blocked only the honest
        /// names (<c>STRIPE_API_KEY</c>, <c>DB_PASSWORD</c>) that a bound secret is for. The
        /// screen still covers <c>run</c>, <c>context</c>, <c>input</c> and <c>limits</c>,
        /// which is where a control-plane mistake would actually put a token.
        /// </para>
        /// </summary>
        public static void Screen(string envelopeJson)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(envelopeJson);
            }
            catch (JsonException ex)
            {
                throw new ForbiddenContentException($"the execution envelope is not valid JSON: {ex.Message}");
            }

            using (document)
            {
                Walk(document.RootElement, string.Empty, screenKeys: true);
            }
        }

        private static void Walk(JsonElement element, string path, bool screenKeys)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        // Only the envelope's own top-level `env` — a nested object that merely
                        // happens to be called "env" inside `input` is still screened.
                        var childScreens = screenKeys
                            && !(path.Length == 0 && property.Name == "env");

                        if (screenKeys)
                        {
                            foreach (var fragment in ForbiddenKeyFragments)
                            {
                                if (property.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                                {
                                    throw new ForbiddenContentException(
                                        $"the execution envelope contains a forbidden key at " +
                                        $"'{path}{property.Name}'; credentials must never reach a sandbox");
                                }
                            }
                        }
                        Walk(property.Value, $"{path}{property.Name}.", childScreens);
                    }
                    break;

                case JsonValueKind.Array:
                    var index = 0;
                    foreach (var item in element.EnumerateArray())
                    {
                        Walk(item, $"{path}[{index++}].", screenKeys);
                    }
                    break;

                default:
                    break;
            }
        }
    }
}
