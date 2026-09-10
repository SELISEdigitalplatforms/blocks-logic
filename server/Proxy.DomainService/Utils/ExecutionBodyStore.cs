using System.Text;

namespace Proxy.DomainService.Utils
{
    /// <summary>
    /// The single seam that turns a captured upstream response body into the string persisted on
    /// <see cref="Entities.ProxyExecutionEntity.ResponseBody"/> (and returned by the <c>Test</c> endpoint).
    /// Phase 2 stores the body in full; a later truncation / redaction policy is a one-method change here.
    /// </summary>
    public static class ExecutionBodyStore
    {
        /// <summary>
        /// Decodes <paramref name="body"/> as UTF-8 text for persistence. Returns <c>null</c> when there was
        /// no body. <paramref name="contentType"/> is accepted now so a future policy can branch on it
        /// (e.g. skip binary payloads) without a signature change.
        /// </summary>
        public static string? Capture(byte[]? body, string? contentType)
        {
            _ = contentType;
            if (body is null || body.Length == 0)
            {
                return null;
            }

            return Encoding.UTF8.GetString(body);
        }
    }
}
