using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Proxy.DomainService.Entities;

namespace Proxy.DomainService.Utils
{
    /// <summary>
    /// The one place that decides what a secret looks like and hides it (P-5 / PS-2 / PS-7, user 2026-10-07:
    /// "never ever show any secret"). Used on the response relayed to the caller, on everything an execution row
    /// stores, and on the version history.
    /// <para>
    /// Two kinds of secret are known: the values the forward itself injected (resolved <c>{{$VAR}}</c> values and
    /// literal credential values), which are hidden wherever they appear; and caller-sent query values whose key
    /// looks like a credential (<c>api_key</c>, <c>token</c>, <c>signature</c>, …), which are hidden in what is stored.
    /// </para>
    /// </summary>
    public static partial class ProxySecretRedactor
    {
        public const string Mask = "***";

        /// <summary>Shorter values are not masked: "true", "1", "json" would otherwise be wiped from every body.</summary>
        public const int MinSecretLength = 8;

        // A key is a credential when any of these words appears in it, ignoring case and separators:
        // api_key, x-api-key, access_token, client_secret, password, signature, sig, auth, session, …
        [GeneratedRegex(
            @"(key|token|secret|passw|pwd|signature|^sig$|auth|session|credential|cookie|bearer|jwt)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex SensitiveKeyPattern();

        public static bool IsSensitiveKey(string? key) =>
            !string.IsNullOrEmpty(key) && SensitiveKeyPattern().IsMatch(key);

        /// <summary>
        /// The secret strings a forward knows it sent: every resolved <c>{{$VAR}}</c> value, plus every literal
        /// header / query / body-merge value under a credential-looking key. A <c>"Bearer xyz"</c> value also
        /// yields <c>"xyz"</c>, because a vendor that echoes the token rarely echoes the scheme with it.
        /// Values shorter than <see cref="MinSecretLength"/> are left out. Longest first, so a longer secret is
        /// masked before a shorter one it contains.
        /// </summary>
        public static IReadOnlyList<string> CollectSecrets(
            IReadOnlyDictionary<string, string> resolvedVariables,
            params IEnumerable<ProxyKeyValue>?[] configuredPairs)
        {
            var secrets = new HashSet<string>(StringComparer.Ordinal);

            foreach (var value in resolvedVariables.Values)
            {
                Add(value);
            }

            foreach (var pairs in configuredPairs)
            {
                if (pairs is null) continue;
                foreach (var pair in pairs)
                {
                    if (ProxyVarRef.ContainsRef(pair.Value)) continue; // its resolved value is already above
                    if (IsSensitiveKey(pair.Key)) Add(pair.Value);
                }
            }

            return secrets.OrderByDescending(s => s.Length).ToList();

            void Add(string? value)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                var trimmed = value.Trim();
                if (trimmed.Length >= MinSecretLength) secrets.Add(trimmed);

                // "Bearer xyz" / "Basic xyz" / "Token xyz": the credential is the last word.
                var space = trimmed.LastIndexOf(' ');
                if (space > 0 && trimmed.Length - space - 1 >= MinSecretLength)
                {
                    secrets.Add(trimmed[(space + 1)..]);
                }
            }
        }

        /// <summary>Replaces every secret in <paramref name="text"/> with <see cref="Mask"/>. <c>null</c> stays <c>null</c>.</summary>
        public static string? MaskText(string? text, IReadOnlyList<string> secrets)
        {
            if (string.IsNullOrEmpty(text) || secrets.Count == 0) return text;

            foreach (var secret in secrets)
            {
                if (text.Contains(secret, StringComparison.Ordinal))
                {
                    text = text.Replace(secret, Mask, StringComparison.Ordinal);
                }
            }

            return text;
        }

        /// <summary>
        /// Masks secrets in a response body before it reaches the caller, for text content types only. A binary
        /// body is returned untouched: rewriting bytes inside an image or a PDF would corrupt it, and a vendor does
        /// not echo a key inside one. Returns the same array when nothing matched, so the common path copies nothing.
        /// </summary>
        public static byte[]? MaskBody(byte[]? body, string? contentType, IReadOnlyList<string> secrets)
        {
            if (body is null || body.Length == 0 || secrets.Count == 0 || !IsTextContentType(contentType))
            {
                return body;
            }

            var text = Encoding.UTF8.GetString(body);
            var masked = MaskText(text, secrets);
            return ReferenceEquals(masked, text) || masked == text ? body : Encoding.UTF8.GetBytes(masked!);
        }

        /// <summary>
        /// A query string fit to store: values under credential-looking keys and any known secret become
        /// <see cref="Mask"/>. Keys and their order are kept, so the log still shows what was sent.
        /// </summary>
        public static string RedactQuery(string? query, IReadOnlyList<string>? secrets = null)
        {
            if (string.IsNullOrEmpty(query)) return string.Empty;

            var parts = new List<string>();
            foreach (var pair in QueryHelpers.ParseQuery(query.TrimStart('?')))
            {
                foreach (var value in (StringValues)pair.Value)
                {
                    var shown = IsSensitiveKey(pair.Key)
                        ? Mask
                        : MaskText(value ?? string.Empty, secrets ?? Array.Empty<string>())!;
                    parts.Add($"{Uri.EscapeDataString(pair.Key)}={(shown == Mask ? Mask : Uri.EscapeDataString(shown))}");
                }
            }

            return string.Join("&", parts);
        }

        /// <summary>An absolute URL fit to store: user info dropped, query redacted as <see cref="RedactQuery"/>.</summary>
        public static string RedactUrl(string? url, IReadOnlyList<string>? secrets = null)
        {
            if (string.IsNullOrEmpty(url)) return string.Empty;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return MaskText(url, secrets ?? Array.Empty<string>()) ?? string.Empty;
            }

            var path = MaskText(uri.GetLeftPart(UriPartial.Path), secrets ?? Array.Empty<string>())!;
            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                path = path.Replace(uri.UserInfo + "@", string.Empty, StringComparison.Ordinal);
            }

            var query = RedactQuery(uri.Query, secrets);
            return query.Length == 0 ? path : path + "?" + query;
        }

        /// <summary>
        /// A stored header / query / body-merge value as the version history may show it: a <c>{{$VAR}}</c>
        /// reference is not a secret and is shown; any other value under a credential-looking key is masked.
        /// </summary>
        public static string? MaskConfiguredValue(string? key, string? value) =>
            value is null || ProxyVarRef.ContainsRef(value) || !IsSensitiveKey(key) ? value : Mask;

        /// <summary>Text types are the ones worth storing or scanning: text/*, JSON, XML, form, JavaScript, GraphQL.</summary>
        public static bool IsTextContentType(string? contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType)) return false;
            var mediaType = contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
            return mediaType.StartsWith("text/", StringComparison.Ordinal)
                || mediaType.EndsWith("/json", StringComparison.Ordinal)
                || mediaType.EndsWith("+json", StringComparison.Ordinal)
                || mediaType.EndsWith("/xml", StringComparison.Ordinal)
                || mediaType.EndsWith("+xml", StringComparison.Ordinal)
                || mediaType is "application/x-www-form-urlencoded" or "application/javascript"
                    or "application/graphql" or "application/x-ndjson";
        }
    }
}
