using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace DomainService.Notification.Push
{
    /// <summary>OAuth2 access tokens for FCM, from a service-account key (JWT bearer grant).</summary>
    public interface IFcmAccessTokenProvider
    {
        /// <summary>A token for <paramref name="tenantId"/>; cached until 5 minutes before it expires. Null when Google refused.</summary>
        Task<string?> GetTokenAsync(string tenantId, ServiceAccountKey key, CancellationToken cancellationToken);

        /// <summary>Drops the cached token of <paramref name="tenantId"/> (FCM answered 401).</summary>
        void Forget(string tenantId);
    }

    /// <inheritdoc cref="IFcmAccessTokenProvider"/>
    /// <remarks>
    /// The token URL is fixed: the key's own <c>token_uri</c> is never used, so an uploaded key cannot point the
    /// worker at another host. The cache entry carries a fingerprint of the key, so a newly uploaded key is used at once.
    /// </remarks>
    public sealed class FcmAccessTokenProvider : IFcmAccessTokenProvider
    {
        public const string HttpClientName = "blocks-fcm";
        internal const string TokenUrl = "https://oauth2.googleapis.com/token"; // NOSONAR S1075 — fixed on purpose: never the key's own token_uri
        internal const string Scope = "https://www.googleapis.com/auth/firebase.messaging";
        internal static readonly TimeSpan RefreshBefore = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan AssertionLifetime = TimeSpan.FromHours(1);
        private const int MaxCachedTenants = 10_000;

        private readonly ConcurrentDictionary<string, CachedToken> _cache = new(StringComparer.Ordinal);
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<FcmAccessTokenProvider> _logger;

        public FcmAccessTokenProvider(IHttpClientFactory httpClientFactory, TimeProvider timeProvider, ILogger<FcmAccessTokenProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        public Task<string?> GetTokenAsync(string tenantId, ServiceAccountKey key, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(key);
            var fingerprint = Fingerprint(key);
            var now = _timeProvider.GetUtcNow();
            if (_cache.TryGetValue(tenantId, out var cached) && cached.Fingerprint == fingerprint && cached.RefreshAt > now)
            {
                return Task.FromResult<string?>(cached.Value);
            }

            return RequestAsync(tenantId, key, fingerprint, cancellationToken);
        }

        public void Forget(string tenantId) => _cache.TryRemove(tenantId, out _);

        private async Task<string?> RequestAsync(string tenantId, ServiceAccountKey key, string fingerprint, CancellationToken cancellationToken)
        {
            var now = _timeProvider.GetUtcNow();
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = Assertion(key, now),
            });

            using var response = await _httpClientFactory.CreateClient(HttpClientName).PostAsync(TokenUrl, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // Status only: the body can echo the assertion.
                _logger.LogWarning("Push: access token refused (HTTP {Status})", (int)response.StatusCode);
                return null;
            }

            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
            if (string.IsNullOrWhiteSpace(token?.Value) || token.ExpiresIn <= 0)
            {
                _logger.LogWarning("Push: access token response had no token");
                return null;
            }

            if (_cache.Count >= MaxCachedTenants)
            {
                _cache.Clear();
            }

            _cache[tenantId] = new CachedToken(fingerprint, token.Value, now + TimeSpan.FromSeconds(token.ExpiresIn) - RefreshBefore);
            return token.Value;
        }

        /// <summary>A signed RS256 JWT: issuer the service account, audience the token URL, scope FCM, one hour.</summary>
        internal static string Assertion(ServiceAccountKey key, DateTimeOffset now)
        {
            var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string> { ["alg"] = "RS256", ["typ"] = "JWT" }));
            var claims = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
            {
                ["iss"] = key.ClientEmail,
                ["scope"] = Scope,
                ["aud"] = TokenUrl,
                ["iat"] = now.ToUnixTimeSeconds(),
                ["exp"] = (now + AssertionLifetime).ToUnixTimeSeconds(),
            }));

            var unsigned = header + "." + claims;
            using var rsa = RSA.Create();
            rsa.ImportFromPem(key.PrivateKeyPem);
            // RS256 is defined as RSASSA-PKCS1-v1_5 with SHA-256 (RFC 7518); Google accepts only this.
            var signature = rsa.SignData(Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1); // NOSONAR S5542 — required by RS256
            return unsigned + "." + Base64Url(signature);
        }

        private static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static string Fingerprint(ServiceAccountKey key) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.ClientEmail + "\n" + key.PrivateKeyPem)));

        private sealed record CachedToken(string Fingerprint, string Value, DateTimeOffset RefreshAt);

        private sealed class TokenResponse
        {
            [JsonPropertyName("access_token")]
            public string? Value { get; set; }

            [JsonPropertyName("expires_in")]
            public int ExpiresIn { get; set; }
        }
    }
}
