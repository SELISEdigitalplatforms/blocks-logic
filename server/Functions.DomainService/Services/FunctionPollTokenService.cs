using System.Security.Cryptography;
using System.Text;
using Blocks.Genesis;
using Functions.DomainService.Queue;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Functions.DomainService.Services
{
    /// <summary>
    /// Per-run bearer capability for the anonymous poll route, so a caller of a <c>Public</c>
    /// function that took a 202 can fetch its own result without being a Blocks user.
    /// <para>
    /// The token is 32 random bytes (base64url), handed out once in the 202 body. Only its SHA-256
    /// is stored, in Redis next to the tenant it belongs to, for <see cref="FunctionQueueKeys.RunTtl"/>
    /// — the same lifetime as the run's own queue record. A Redis dump therefore holds nothing that
    /// can be replayed, and a token outlives neither its run record nor its tenant: presenting it
    /// under another tenant's <c>x-blocks-key</c> fails exactly as a wrong token does.
    /// </para>
    /// </summary>
    public interface IFunctionPollTokenService
    {
        /// <summary>Mints a token for <paramref name="runId"/> and stores its hash. Returns the plaintext, once.</summary>
        Task<string> IssueAsync(string tenantId, string runId, CancellationToken cancellationToken = default);

        /// <summary>
        /// <c>true</c> only when <paramref name="token"/> is the one issued for this run under this
        /// tenant and has not expired. Every other case — no token, wrong token, wrong tenant,
        /// unknown or expired run, malformed input — is the same <c>false</c>, so the answer is no
        /// oracle for which runs exist.
        /// </summary>
        Task<bool> VerifyAsync(string tenantId, string runId, string? token, CancellationToken cancellationToken = default);
    }

    /// <inheritdoc cref="IFunctionPollTokenService"/>
    public class FunctionPollTokenService : IFunctionPollTokenService
    {
        /// <summary>Base64url of 32 bytes is 43 characters; anything far from that is not a token.</summary>
        internal const int MaxTokenLength = 64;

        private const string TenantField = "tenant";
        private const string HashField = "hash";

        private readonly ICacheClient _cache;
        private readonly ILogger<FunctionPollTokenService> _logger;

        public FunctionPollTokenService(ICacheClient cache, ILogger<FunctionPollTokenService> logger)
        {
            _cache = cache;
            _logger = logger;
        }

        public async Task<string> IssueAsync(string tenantId, string runId, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
            ArgumentException.ThrowIfNullOrWhiteSpace(runId);

            var token = Base64Url(RandomNumberGenerator.GetBytes(32));
            var key = FunctionQueueKeys.PollToken(runId);
            var database = _cache.CacheDatabase();

            await database.HashSetAsync(key,
            [
                new HashEntry(TenantField, tenantId),
                new HashEntry(HashField, Hash(token)),
            ]);
            await database.KeyExpireAsync(key, FunctionQueueKeys.RunTtl);

            return token;
        }

        public async Task<bool> VerifyAsync(
            string tenantId, string runId, string? token, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(runId)
                || string.IsNullOrEmpty(token) || token.Length > MaxTokenLength)
            {
                return false;
            }

            var values = await _cache.CacheDatabase().HashGetAsync(
                FunctionQueueKeys.PollToken(runId), [TenantField, HashField]);

            if (values.Length != 2 || values[0].IsNullOrEmpty || values[1].IsNullOrEmpty)
            {
                return false;
            }

            // Both compared in constant time and both always compared, so neither a wrong tenant
            // nor a near-miss token answers measurably faster than the other.
            var tenantMatches = FixedTimeEquals(values[0].ToString(), tenantId);
            var hashMatches = FixedTimeEquals(values[1].ToString(), Hash(token));

            if (!(tenantMatches & hashMatches))
            {
                _logger.LogInformation("Poll token rejected for run {RunId}", runId);
                return false;
            }

            return true;
        }

        internal static string Hash(string token) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        private static bool FixedTimeEquals(string a, string b) =>
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

        private static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
