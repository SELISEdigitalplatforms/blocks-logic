using Blocks.Genesis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Functions.DomainService.Services
{
    /// <summary>
    /// Resolves secrets by calling blocks-os directly: <c>GET /api/Secrets/value?secretId=</c>
    /// per id, with a delegated bearer token. Selected by
    /// <c>Functions:SecretResolver = "blocksos"</c> — the fallback DECISIONS.md describes for
    /// environments where the in-process <see cref="NugetSecretResolver"/> cannot reach Azure
    /// Key Vault.
    /// <para>
    /// <b>Known limitation, not yet resolved</b>: <c>IDelegatedTokenProvider.GetTokenAsync</c>
    /// redeems an existing delegation grant tied to the current HTTP request's authenticated
    /// user (see <c>WorkflowAuthService.CreateBlocksAuthorizationTokenAsync</c>). This resolver
    /// runs from the Worker's result consumer, which has no HTTP request and no request-scoped
    /// grant, so <c>GetTokenAsync</c> is expected to return <c>null</c> here — its own contract
    /// says treat that as "omit the Authorization header", not as an error, so a call proceeds
    /// unauthenticated and blocks-os will most likely answer 401. The failure is not silent: it
    /// surfaces as that one output action failing (see <see cref="OutputActionProcessor"/>),
    /// which retries per the function's policy and eventually reports <c>OUTPUT_FAILED</c> —
    /// the same path any other output-action failure takes. Until a service-to-service
    /// credential exists for the Worker, this path is expected to degrade this way rather than
    /// to work end-to-end; DECISIONS.md's open question #3 is the place that is tracked.
    /// </para>
    /// </summary>
    public class BlocksOsHttpSecretResolver : ISecretResolver
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IDelegatedTokenProvider _delegatedTokenProvider;
        private readonly IConfiguration _configuration;
        private readonly ILogger<BlocksOsHttpSecretResolver> _logger;

        public BlocksOsHttpSecretResolver(
            IHttpClientFactory httpClientFactory,
            IDelegatedTokenProvider delegatedTokenProvider,
            IConfiguration configuration,
            ILogger<BlocksOsHttpSecretResolver> logger)
        {
            _httpClientFactory = httpClientFactory;
            _delegatedTokenProvider = delegatedTokenProvider;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<IReadOnlyDictionary<string, string>> ResolveAsync(
            IReadOnlyCollection<string> secretIds, string tenantId, CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<string, string>();
            if (secretIds.Count == 0) return result;

            var baseUrl = _configuration["Functions:BlocksOsBaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                _logger.LogWarning("Functions:BlocksOsBaseUrl is not configured; cannot resolve secrets via blocks-os");
                return result;
            }

            var token = await _delegatedTokenProvider.GetTokenAsync(cancellationToken);
            var client = _httpClientFactory.CreateClient(nameof(BlocksOsHttpSecretResolver));
            client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
            if (!string.IsNullOrEmpty(token))
            {
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }

            // One request per id, bounded so a large template cannot open unbounded
            // connections to blocks-os. Independent failures: one bad reference must not
            // block the ones that resolve cleanly.
            const int maxConcurrency = 4;
            using var gate = new SemaphoreSlim(maxConcurrency);
            var locker = new Lock();

            var tasks = secretIds.Select(async id =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    using var request = new HttpRequestMessage(
                        HttpMethod.Get, $"/api/Secrets/value?secretId={Uri.EscapeDataString(id)}");
                    using var response = await client.SendAsync(request, cancellationToken);

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning(
                            "blocks-os returned {StatusCode} resolving secret {SecretId}", response.StatusCode, id);
                        return;
                    }

                    var value = await response.Content.ReadAsStringAsync(cancellationToken);
                    if (string.IsNullOrEmpty(value)) return;

                    lock (locker) { result[id] = value; }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to resolve secret {SecretId} via blocks-os", id);
                }
                finally
                {
                    gate.Release();
                }
            });

            await Task.WhenAll(tasks);
            return result;
        }
    }
}
