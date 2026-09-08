using System.Text.Json;
using System.Text.Json.Nodes;
using Proxy.DomainService.Entities;

namespace Proxy.DomainService.Utils
{
    /// <summary>
    /// Merges a proxy's configured <c>BodyMerge</c> fields into the top level of the client's JSON request
    /// body before it is forwarded upstream (SPEC "request body merge" &sect;3.8). Empty <c>mergeFields</c>
    /// never reaches here &mdash; the gateway forwards the body byte-for-byte in that case.
    /// </summary>
    public static class ProxyBodyMerger
    {
        /// <summary>
        /// The outcome of a merge attempt. <see cref="Body"/> is the minified UTF-8 JSON to forward when
        /// <see cref="NotMergeable"/> is <c>false</c>; when <see cref="NotMergeable"/> is <c>true</c> the client
        /// body was not a JSON object (malformed, a top-level array, or a scalar) and the caller must reject
        /// the call with <c>422</c> without forwarding.
        /// </summary>
        public readonly record struct Result(byte[]? Body, bool NotMergeable);

        /// <summary>
        /// Merges <paramref name="mergeFields"/> into the top level of <paramref name="clientBody"/> (UTF-8 JSON).
        /// A null / empty <paramref name="clientBody"/> starts from <c>{}</c>. A body that is not a JSON object
        /// (array, scalar, or malformed) returns <c>Result(null, NotMergeable: true)</c>. Each field value is
        /// resolved through <paramref name="resolver"/> and written as a JSON string, overriding any client key
        /// of the same name.
        /// </summary>
        public static Result Merge(
            byte[]? clientBody,
            IReadOnlyList<ProxyKeyValue> mergeFields,
            IProxySecretResolver resolver)
        {
            JsonObject obj;
            if (clientBody is null || clientBody.Length == 0)
            {
                obj = new JsonObject();
            }
            else
            {
                JsonNode? node;
                try
                {
                    node = JsonNode.Parse(clientBody);
                }
                catch (JsonException)
                {
                    return new Result(null, true);
                }

                if (node is not JsonObject parsed)
                {
                    return new Result(null, true);
                }

                obj = parsed;
            }

            foreach (var field in mergeFields)
            {
                obj[field.Key] = JsonValue.Create(resolver.Resolve(field.Value));
            }

            return new Result(JsonSerializer.SerializeToUtf8Bytes(obj), false);
        }
    }
}
