using System.Text;

namespace Proxy.DomainService.Utils
{
    /// <summary>
    /// Turns a response body into text for the in-memory result (the workflow step and Test read it). Never
    /// persisted: Blocks stores no request or response body (user decision 2026-10-07).
    /// </summary>
    public static class ExecutionBodyStore
    {
        /// <summary>
        /// Decodes <paramref name="body"/> as UTF-8 for the in-memory result. Returns <c>null</c> when there was
        /// no body. Never persisted.
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
