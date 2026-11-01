using System.Text.Json;
using System.Text.Json.Serialization;
using Mail.DomainService.Shared.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Workflow.DomainService.Repositories;

namespace Workflow.DomainService.Services
{
    /// <summary>
    /// Response from OAuth token endpoint
    /// </summary>
    public class TokenResponse
    {
        public string? AccessToken { get; set; }
        public string? TokenType { get; set; }
        public int ExpiresIn { get; set; }
        public string? RefreshToken { get; set; }
        public string? IdToken { get; set; }
    }

    /// <summary>
    /// Service interface for obtaining authentication tokens using client credentials
    /// </summary>
    public interface IClientCredentialTokenService
    {
        /// <summary>
        /// Gets an access token using client credentials
        /// </summary>
        /// <param name="clientCredentials">The client credentials entity</param>
        /// <param name="tenantId">The project key (used as X-Blocks-Key header)</param>
        /// <returns>The access token or null if failed</returns>
        Task<string?> GetTokenAsync(ClientCredential clientCredentials, string tenantId);

        /// <summary>
        /// Gets an access token for the client with this id, reading its secret from the tenant's IAM data.
        /// Workflow nodes store only the client id.
        /// </summary>
        /// <param name="clientId">The client credential id selected on the node</param>
        /// <param name="tenantId">The project key: picks the tenant database and is sent as X-Blocks-Key</param>
        /// <returns>The access token, or null when there is no active client with this id or the token call failed</returns>
        Task<string?> GetTokenAsync(string clientId, string tenantId);
    }

    /// <summary>
    /// Service implementation for obtaining authentication tokens using client credentials
    /// </summary>
    public class ClientCredentialTokenService : IClientCredentialTokenService
    {
        private readonly ILogger<ClientCredentialTokenService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly IClientCredentialRepository _clientCredentialRepository;
        private readonly IServiceScopeFactory _scopeFactory;

        public ClientCredentialTokenService(
            ILogger<ClientCredentialTokenService> logger,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            IClientCredentialRepository clientCredentialRepository,
            IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _clientCredentialRepository = clientCredentialRepository;
            _scopeFactory = scopeFactory;
        }

        public async Task<string?> GetTokenAsync(string clientId, string tenantId)
        {
            var credential = await _clientCredentialRepository.GetClientCredentialAsync(tenantId, clientId);
            if (credential == null || !credential.IsActive)
            {
                // Null like a refused token call: the node then sends without auth, as it always did.
                _logger.LogWarning("No active client credential for ClientId: {ClientId}", clientId);
                return null;
            }

            return await GetTokenAsync(credential, tenantId);
        }

        public async Task<string?> GetTokenAsync(ClientCredential clientCredentials, string tenantId)
        {
            try
            {
                _logger.LogInformation("Getting token for ClientId: {ClientId}", clientCredentials.ItemId);

                var clientSecret = await ResolveSecretAsync(clientCredentials, tenantId);
                if (string.IsNullOrWhiteSpace(clientSecret))
                {
                    // Null like a refused token call: the node then sends without auth, as it always did.
                    _logger.LogWarning("No client secret available for ClientId: {ClientId}", clientCredentials.ItemId);
                    return null;
                }

                // Get the authentication endpoint from configuration
                var authEndpoint = _configuration["ClienCredentialsTokenEndpoint"]
                    ?? "https://iam.seliseblocks.com/api/oidc/token";

                using var client = _httpClientFactory.CreateClient();

                // Set headers
                client.DefaultRequestHeaders.Add("X-Blocks-Key", tenantId);

                // Prepare form data
                var formData = new Dictionary<string, string>
                {
                    { "grant_type", "client_credentials" },
                    { "client_id", clientCredentials.ItemId },
                    { "client_secret", clientSecret },
                    {"org_id","default"}
                };

                var content = new FormUrlEncodedContent(formData);

                // Make the request
                var response = await client.PostAsync(authEndpoint, content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to get token for ClientId: {ClientId}. Status: {StatusCode}, Error: {Error}, body {BodyLength} chars",
                        clientCredentials.ItemId, response.StatusCode, TokenErrorBody.Code(errorContent), errorContent.Length);
                    return null;
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (string.IsNullOrEmpty(tokenResponse?.AccessToken))
                {
                    _logger.LogError("Token response is empty or invalid for ClientId: {ClientId}", clientCredentials.ItemId);
                    return null;
                }

                _logger.LogInformation("Successfully obtained token for ClientId: {ClientId}, ExpiresIn: {ExpiresIn}s",
                    clientCredentials.ItemId, tokenResponse.ExpiresIn);
                return tokenResponse.AccessToken;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting token for ClientId: {ClientId}", clientCredentials.ItemId);
                return null;
            }
        }

        /// <summary>
        /// The client secret: read from the tenant's Blocks Secrets when the credential holds a reference
        /// (blocks-iam keeps it there), else the plaintext field of a credential saved before. A reference
        /// always wins: a failed read answers null and never falls back to the plaintext.
        /// </summary>
        /// <remarks>
        /// One store read per token request, nothing cached, so a rotation in the console is picked up by
        /// the next run. <see cref="TenantSecretReader"/> reads in an authenticated context for the
        /// credential's own tenant and restores the caller's afterwards.
        /// </remarks>
        private async Task<string?> ResolveSecretAsync(ClientCredential credential, string tenantId)
        {
            var reference = credential.ClientSecretReference;
            if (string.IsNullOrEmpty(reference))
            {
                return credential.ClientSecret;
            }

            // Blocks Secrets refuses a blank tenant, and no other tenant may stand in for it.
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                _logger.LogWarning("The client secret of ClientId: {ClientId} cannot be read without a tenant", credential.ItemId);
                return null;
            }

            string failureClass;
            try
            {
                return await TenantSecretReader.ReadAsync(_scopeFactory, tenantId, reference);
            }
            catch (Exception ex)
            {
                // Caught here, not by the caller's handler: Secret* messages carry the reference id, so
                // only the type is kept and the exception itself is never logged.
                failureClass = ex.GetType().Name;
            }

            _logger.LogWarning("The client secret of ClientId: {ClientId} is unavailable: {FailureClass}",
                credential.ItemId, failureClass);
            return null;
        }
    }
}
