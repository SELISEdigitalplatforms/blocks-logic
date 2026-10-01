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
    /// <b>Secret-bound variables stay references here.</b> A variable's value may be a
    /// <c>{{secret.&lt;id&gt;}}</c> reference (whole, or embedded as in <c>Bearer {{secret.x}}</c>),
    /// and this builder leaves it exactly as written: the envelope goes into the Redis run record
    /// and its retry copy, and plaintext must never be in either. The <b>runner</b> resolves the
    /// references for the run's tenant right before it starts the sandbox
    /// (<c>sandbox/src/Blocks.FunctionRunner/Runs/EnvSecretReferences.cs</c>), so the function
    /// still reads the real value as <c>ctx.env.NAME</c>. Those are the tenant's <i>own</i>
    /// secrets, chosen explicitly in the editor — spec §17 is about the platform's credentials,
    /// which never appear at all. The run entry carries
    /// <see cref="Queue.FunctionQueueKeys.RunProtocolVersion"/>, so a runner that predates this
    /// refuses the run rather than handing a function the reference text.
    /// </para>
    /// </summary>
    public static class FunctionEnvelopeBuilder
    {
        /// <summary>
        /// Property-name fragments that must never appear, at any depth, in the parts of an
        /// envelope the control plane builds — everything but <c>env</c> and the caller's
        /// <c>input</c> (whose <c>headers</c> are still screened); see <see cref="Screen"/>.
        /// Matched case-insensitively. Mirrors the runner's own screen.
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
        public static string Build(
            FunctionRunEntity run,
            FunctionVersionEntity? version,
            FunctionEntity function,
            BlocksContext? context,
            string? inputJson)
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
                ["env"] = BuildEnv(variables, out var maskedEnvKeys),
                // The keys whose values carry a secret reference: the runner resolves exactly
                // these, and the sandbox masks the resolved values out of every log line it
                // writes. Keys only — never ids alongside, never values.
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
        /// The secret ids referenced by the variables this run will use, deduplicated. Nothing in
        /// the invoke path resolves them — the runner does, from the references left in
        /// <c>env</c> — but the set is what an author's variables depend on, for tests and
        /// diagnostics.
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
        /// <c>ctx.env</c> from the version's variable snapshot, <b>unresolved</b>: a
        /// <c>{{secret.&lt;id&gt;}}</c> reference — whole or embedded — is copied through as the
        /// reference text, and its key is reported in <paramref name="maskedEnvKeys"/> so the
        /// runner knows what to resolve and the sandbox what to mask.
        /// <para>
        /// A reference whose secret was deleted or can no longer be read is not caught here: the
        /// runner fails that run as <c>SECRET_UNRESOLVED</c> before the sandbox starts, naming
        /// the variable. Checking here as well would mean reading the secret's value into this
        /// process just to throw it away — the SDK's only lookups either return the value or
        /// return metadata under a different authorisation rule — and would still not settle it,
        /// because a secret can be deleted between the invoke and the run.
        /// </para>
        /// </summary>
        private static JsonObject BuildEnv(IEnumerable<VariableBinding>? variables, out List<string> maskedEnvKeys)
        {
            var env = new JsonObject();
            maskedEnvKeys = [];
            if (variables is null) return env;

            foreach (var variable in variables)
            {
                if (string.IsNullOrWhiteSpace(variable.Key)) continue;

                if (!string.IsNullOrEmpty(variable.Value) && SecretPlaceholder.IsMatch(variable.Value))
                {
                    maskedEnvKeys.Add(variable.Key);
                }

                env[variable.Key] = variable.Value;
            }
            return env;
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
        /// The screen exists to catch <b>control-plane</b> mistakes — a token copied out of
        /// <c>BlocksContext</c>, a credential added to <c>run</c> or <c>limits</c> — so it covers
        /// everything this file builds: <c>run</c>, <c>context</c>, <c>maskedEnv</c>, <c>limits</c>
        /// and any top-level key added later.
        /// </para>
        /// <para>
        /// Two subtrees are exempt, because their keys are not the control plane's:
        /// <list type="bullet">
        /// <item><c>env</c> — variable names the tenant authored, already constrained to an
        /// identifier by <c>VariableBindingValidator</c>. Screening there blocked only the honest
        /// names (<c>STRIPE_API_KEY</c>, <c>DB_PASSWORD</c>) that a bound secret is for.</item>
        /// <item><c>input</c> — the caller's own payload (for HTTP: its query and body). A signup
        /// form has a <c>password</c> field and a GitHub webhook a <c>secret</c>; refusing them
        /// broke real callers and protected nothing, since the caller chose to send that data to
        /// this function. <b>Except <c>input.headers</c></b>, which the control plane shapes from
        /// an allow-list (<see cref="FunctionHttpInputBuilder.ForwardedHeaders"/>): an
        /// <c>authorization</c> or cookie key there means the allow-list regressed, and that
        /// stays a refusal.</item>
        /// </list>
        /// Both exemptions are the envelope's own top-level keys only; an object called
        /// <c>env</c> or <c>input</c> nested inside <c>run</c> or <c>context</c> is still screened.
        /// </para>
        /// <para>
        /// The runner's <c>ExecutionEnvelope.Screen</c> must apply the same exemptions, or a
        /// payload this side admits is refused on arrival.
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
                        // Only the envelope's own top-level `env` and `input` are exempt (see
                        // Screen) — and within `input`, its `headers` are screened again, since
                        // that object is built by the control plane from an allow-list.
                        var isRoot = path.Length == 0;
                        var childScreens = screenKeys switch
                        {
                            true => !(isRoot && (property.Name == "env" || property.Name == "input")),
                            false => path == "input." && property.Name == "headers",
                        };

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
