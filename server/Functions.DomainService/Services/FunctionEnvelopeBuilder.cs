using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    /// </summary>
    public static class FunctionEnvelopeBuilder
    {
        /// <summary>
        /// Property-name fragments that must never appear anywhere in an envelope, at any
        /// depth. Matched case-insensitively. Mirrors the runner's own screen.
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
                ["env"] = BuildEnv(variables),
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
        /// <c>ctx.env</c> from the version's variable snapshot. Non-secret configuration only —
        /// secret values are resolved in the Worker's output processor and never come near here.
        /// </summary>
        private static JsonObject BuildEnv(IEnumerable<VariableBinding>? variables)
        {
            var env = new JsonObject();
            if (variables is null) return env;

            foreach (var variable in variables)
            {
                if (string.IsNullOrWhiteSpace(variable.Key)) continue;
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
                Walk(document.RootElement, string.Empty);
            }
        }

        private static void Walk(JsonElement element, string path)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
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
                        Walk(property.Value, $"{path}{property.Name}.");
                    }
                    break;

                case JsonValueKind.Array:
                    var index = 0;
                    foreach (var item in element.EnumerateArray())
                    {
                        Walk(item, $"{path}[{index++}].");
                    }
                    break;

                default:
                    break;
            }
        }
    }
}
