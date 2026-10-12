using System.Text.Json;
using System.Text.Json.Nodes;

namespace Blocks.FunctionRunner.Runs
{
    /// <summary>
    /// Puts the caller's delegated Blocks access token into an envelope as
    /// <c>blocks.accessToken</c> — the runner's half of "the queue carries a grant id, never a
    /// token".
    /// <para>
    /// The control plane writes only a delegation grant id, and writes it beside the envelope in
    /// the run hash (<see cref="Contracts.RedisKeys.RunDelegationField"/>). The runner redeems it
    /// with IAM immediately before the sandbox starts and adds the token here, <b>after</b>
    /// <see cref="ExecutionEnvelope.Screen"/> has run: the screen refuses any
    /// <c>accesstoken</c> key, so a token the control plane put in the envelope by mistake still
    /// fails the run, while this one — added deliberately, by the runner, from a grant — reaches
    /// the function. Like a resolved secret, it exists only in this process's memory and in the
    /// per-run 0440 envelope file, and is never written back to Redis.
    /// </para>
    /// <para>
    /// Pure and synchronous, like <see cref="EnvSecretReferences"/>: the redemption is I/O and
    /// lives behind <see cref="Delegation.IRunAccessTokenResolver"/>.
    /// </para>
    /// </summary>
    public static class RunDelegation
    {
        /// <summary>The caller identity the control plane recorded, as far as delegation needs it.</summary>
        public sealed record Caller(string? TenantId, string? UserId, bool IsAuthenticated);

        /// <summary>Reads <c>context</c> from the envelope.</summary>
        /// <exception cref="ExecutionEnvelope.ForbiddenContentException">The envelope is not a JSON object.</exception>
        public static Caller ReadCaller(string envelopeJson)
        {
            var context = ParseObject(envelopeJson)["context"] as JsonObject;
            return new Caller(
                Text(context?["tenantId"]),
                Text(context?["userId"]),
                context?["isAuthenticated"] is JsonValue v && v.TryGetValue<bool>(out var authed) && authed);
        }

        /// <summary>
        /// The envelope with <c>blocks.accessToken</c> set and the token appended to
        /// <c>maskedValues</c>, so the bootstrap masks it in every line it writes. Whatever
        /// <c>blocks</c> the envelope already carried is replaced.
        /// </summary>
        public static string Apply(string envelopeJson, string accessToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

            var root = ParseObject(envelopeJson);
            root["blocks"] = new JsonObject { ["accessToken"] = accessToken };

            var masked = new List<string>();
            if (root["maskedValues"] is JsonArray existing)
            {
                foreach (var item in existing)
                {
                    if (item is JsonValue v && v.TryGetValue<string>(out var s)) masked.Add(s);
                }
            }
            masked.Add(accessToken);
            root["maskedValues"] = new JsonArray(masked.Distinct(StringComparer.Ordinal)
                .Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());

            return root.ToJsonString();
        }

        private static string? Text(JsonNode? node) =>
            node is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;

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
    }
}
