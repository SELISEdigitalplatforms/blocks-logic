using System.Text.Json;
using System.Text.RegularExpressions;

namespace Workflow.DomainService.Services
{
    /// <summary>
    /// What a failed token call may log: the OAuth <c>error</c> code (RFC 6749 §5.2, e.g. <c>invalid_client</c>)
    /// and the body length — never the body itself, which a token endpoint may fill with the submitted form
    /// (client secret, refresh token). User rule 2026-10-08: never show a secret in a log.
    /// </summary>
    public static partial class TokenErrorBody
    {
        [GeneratedRegex(@"^[A-Za-z0-9_.\-]{1,64}$")]
        private static partial Regex SafeCode();

        /// <summary>The <c>error</c> code when the body is JSON with a short code-like value; otherwise <c>"unknown"</c>.</summary>
        public static string Code(string? body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "none";
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("error", out var error)
                    && error.ValueKind == JsonValueKind.String
                    && error.GetString() is { } code
                    && SafeCode().IsMatch(code))
                {
                    return code;
                }
            }
            catch (JsonException)
            {
                // not JSON: say nothing about it
            }
            return "unknown";
        }
    }
}
