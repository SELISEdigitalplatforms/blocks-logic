using System.Net;
using System.Text;
using Blocks.Genesis;
using Blocks.Secrets;
using Workflow.DomainService.Repositories;
using Workflow.DomainService.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using XUnitTest.TestHelpers;

namespace XUnitTest.Links
{
    /// <summary>
    /// Unit tests for ClientCredentialTokenService.
    ///
    /// This is the one place magic-link actions exchange a stored client secret for a bearer
    /// token, so the tests cover both what goes out on the wire and the fact that every failure
    /// path returns null rather than throwing. A caller that treated an exception as "no token"
    /// would behave the same way, but only by accident, so the null contract is pinned.
    /// </summary>
    public class ClientCredentialTokenServiceTests
    {
        /// <summary>
        /// Captures the outgoing request and replays a canned response, so no socket is opened.
        /// </summary>
        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly string _body;
            private readonly Exception? _throw;

            public HttpRequestMessage? Request { get; private set; }
            public string? RequestBody { get; private set; }

            public StubHandler(HttpStatusCode status, string body, Exception? shouldThrow = null)
            {
                _status = status;
                _body = body;
                _throw = shouldThrow;
            }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Request = request;
                RequestBody = request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken);

                if (_throw is not null)
                {
                    throw _throw;
                }

                return new HttpResponseMessage(_status)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json"),
                };
            }
        }

        private static ClientCredential Credential() => new()
        {
            ItemId = "client-1",
            ClientSecret = "s3cr3t",
        };

        /// <summary>A scope factory whose scopes serve <paramref name="secrets"/> as the scoped ISecretService.</summary>
        internal static IServiceScopeFactory SecretScopes(Mock<ISecretService>? secrets = null)
        {
            var secretService = (secrets ?? new Mock<ISecretService>(MockBehavior.Strict)).Object;
            var services = new ServiceCollection();
            services.AddScoped(_ => secretService);
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true })
                .GetRequiredService<IServiceScopeFactory>();
        }

        private static (ClientCredentialTokenService sut, StubHandler handler) Build(
            HttpStatusCode status = HttpStatusCode.OK,
            string body = """{"accessToken":"token-abc","expiresIn":3600}""",
            Exception? shouldThrow = null,
            string? endpoint = "https://iam.example.com/api/oidc/token",
            ClientCredential? stored = null,
            Mock<ISecretService>? secrets = null,
            ILogger<ClientCredentialTokenService>? logger = null)
        {
            var handler = new StubHandler(status, body, shouldThrow);
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>()))
                   .Returns(() => new HttpClient(handler, disposeHandler: false));

            var settings = new Dictionary<string, string?>();
            if (endpoint is not null)
            {
                settings["ClienCredentialsTokenEndpoint"] = endpoint;
            }

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

            var repository = new Mock<IClientCredentialRepository>();
            repository.Setup(r => r.GetClientCredentialAsync("project-1", "client-1")).ReturnsAsync(stored);

            return (new ClientCredentialTokenService(
                logger ?? NullLogger<ClientCredentialTokenService>.Instance,
                factory.Object,
                configuration,
                repository.Object,
                SecretScopes(secrets)), handler);
        }

        // ---------- secret in Blocks Secrets (blocks-iam#651) ----------

        private const string Reference = "cs-ref-1";

        private static ClientCredential Referenced(string plaintext = "") =>
            new() { ItemId = "client-1", ClientSecret = plaintext, ClientSecretReference = Reference, IsActive = true };

        [Fact]
        public async Task By_client_id_a_referenced_secret_is_read_from_the_store_in_the_credentials_tenant()
        {
            var secrets = new Mock<ISecretService>(MockBehavior.Strict);
            BlocksContext? seen = null;
            secrets.Setup(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>()))
                .Callback(() => seen = BlocksContext.GetContext())
                .ReturnsAsync("vault-s3cr3t");
            var (sut, handler) = Build(stored: Referenced(), secrets: secrets);

            var token = await sut.GetTokenAsync("client-1", "project-1");

            token.Should().Be("token-abc");
            handler.RequestBody.Should().Contain("client_secret=vault-s3cr3t");
            seen!.TenantId.Should().Be("project-1");
            seen.IsAuthenticated.Should().BeTrue();
        }

        [Fact]
        public async Task A_referenced_secret_wins_over_a_plaintext_left_in_the_document()
        {
            var secrets = new Mock<ISecretService>(MockBehavior.Strict);
            secrets.Setup(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>())).ReturnsAsync("vault-s3cr3t");
            var (sut, handler) = Build(stored: Referenced(plaintext: "stale-plaintext"), secrets: secrets);

            await sut.GetTokenAsync("client-1", "project-1");

            handler.RequestBody.Should().Contain("client_secret=vault-s3cr3t").And.NotContain("stale-plaintext");
        }

        public static TheoryData<Exception> StoreFailures() => new()
        {
            new SecretVaultException("vault down for " + Reference, "get", Reference),
            new SecretNotFoundException(Reference),
            new SecretStateException("locked", "read"),
        };

        [Theory]
        [MemberData(nameof(StoreFailures))]
        public async Task A_failed_store_read_yields_null_calls_nothing_and_never_uses_the_plaintext(Exception failure)
        {
            var secrets = new Mock<ISecretService>(MockBehavior.Strict);
            secrets.Setup(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>())).ThrowsAsync(failure);
            var logger = new CapturingLogger<ClientCredentialTokenService>();
            var (sut, handler) = Build(stored: Referenced(plaintext: "stale-plaintext"), secrets: secrets, logger: logger);

            var token = await sut.GetTokenAsync("client-1", "project-1");

            token.Should().BeNull();
            handler.Request.Should().BeNull();
            logger.All.Should().Contain(failure.GetType().Name).And.NotContain(Reference).And.NotContain("stale-plaintext");
        }

        [Fact]
        public async Task A_legacy_plaintext_credential_makes_no_store_call()
        {
            var secrets = new Mock<ISecretService>(MockBehavior.Strict);
            var (sut, handler) = Build(stored: new ClientCredential { ItemId = "client-1", ClientSecret = "s3cr3t", IsActive = true }, secrets: secrets);

            await sut.GetTokenAsync("client-1", "project-1");

            handler.RequestBody.Should().Contain("client_secret=s3cr3t");
            secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task A_referenced_secret_without_a_tenant_yields_null_and_reads_nothing()
        {
            var secrets = new Mock<ISecretService>(MockBehavior.Strict);
            var (sut, handler) = Build(secrets: secrets);

            var token = await sut.GetTokenAsync(Referenced(), " ");

            token.Should().BeNull();
            handler.Request.Should().BeNull();
            secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task By_client_id_the_secret_is_read_from_the_repository_and_exchanged()
        {
            var (sut, handler) = Build(stored: new ClientCredential { ItemId = "client-1", ClientSecret = "s3cr3t", IsActive = true });

            var token = await sut.GetTokenAsync("client-1", "project-1");

            token.Should().Be("token-abc");
            handler.RequestBody.Should().Contain("client_id=client-1");
            handler.RequestBody.Should().Contain("client_secret=s3cr3t");
            handler.Request!.Headers.GetValues("X-Blocks-Key").Should().ContainSingle().Which.Should().Be("project-1");
        }

        public static TheoryData<ClientCredential?> UnusableCredentials() => new()
        {
            null,
            new ClientCredential { ItemId = "client-1", ClientSecret = "s3cr3t", IsActive = false },
            new ClientCredential { ItemId = "client-1", ClientSecret = "", IsActive = true },
        };

        [Theory]
        [MemberData(nameof(UnusableCredentials))]
        public async Task By_client_id_a_missing_inactive_or_secretless_client_yields_null_and_calls_nothing(ClientCredential? stored)
        {
            var (sut, handler) = Build(stored: stored);

            var token = await sut.GetTokenAsync("client-1", "project-1");

            token.Should().BeNull();
            handler.Request.Should().BeNull();
        }

        [Fact]
        public async Task A_successful_exchange_returns_the_access_token()
        {
            var (sut, _) = Build();

            var token = await sut.GetTokenAsync(Credential(), "project-1");

            token.Should().Be("token-abc");
        }

        [Fact]
        public async Task A_camel_case_token_response_is_matched_case_insensitively()
        {
            var (sut, _) = Build(body: """{"AccessToken":"token-xyz","ExpiresIn":60}""");

            var token = await sut.GetTokenAsync(Credential(), "project-1");

            token.Should().Be("token-xyz");
        }

        [Fact]
        public async Task A_spec_compliant_snake_case_token_response_is_NOT_parsed_today()
        {
            // Documents a defect rather than endorsing it, see blocks-logic#182.
            // RFC 6749 names the field access_token, and blocks-iam emits exactly that, but
            // TokenResponse carries no [JsonPropertyName] and PropertyNameCaseInsensitive
            // does not bridge an underscore. The token is dropped and the caller sees null,
            // which is indistinguishable from a rejected credential.
            var (sut, _) = Build(body: """{"access_token":"token-abc","expires_in":3600}""");

            var token = await sut.GetTokenAsync(Credential(), "project-1");

            token.Should().BeNull("this is the bug; flip this assertion when #182 is fixed");
        }

        [Fact]
        public async Task The_tenant_id_is_sent_as_the_blocks_key_header()
        {
            var (sut, handler) = Build();

            await sut.GetTokenAsync(Credential(), "project-1");

            handler.Request!.Headers.GetValues("X-Blocks-Key").Should().ContainSingle()
                .Which.Should().Be("project-1");
        }

        [Fact]
        public async Task The_request_is_a_client_credentials_grant_carrying_the_stored_secret()
        {
            var (sut, handler) = Build();

            await sut.GetTokenAsync(Credential(), "project-1");

            handler.Request!.Method.Should().Be(HttpMethod.Post);
            handler.RequestBody.Should().Contain("grant_type=client_credentials");
            handler.RequestBody.Should().Contain("client_id=client-1");
            handler.RequestBody.Should().Contain("client_secret=s3cr3t");
            handler.RequestBody.Should().Contain("org_id=default");
        }

        [Fact]
        public async Task The_configured_endpoint_is_used_when_one_is_present()
        {
            var (sut, handler) = Build(endpoint: "https://iam.test.example.com/token");

            await sut.GetTokenAsync(Credential(), "project-1");

            handler.Request!.RequestUri!.ToString().Should().Be("https://iam.test.example.com/token");
        }

        [Fact]
        public async Task An_absent_endpoint_falls_back_to_a_hard_coded_production_url()
        {
            // Documenting rather than endorsing: with no configuration this posts a client
            // secret to a fixed production host. Pinned so the fallback cannot change unnoticed.
            var (sut, handler) = Build(endpoint: null);

            await sut.GetTokenAsync(Credential(), "project-1");

            handler.Request!.RequestUri!.ToString()
                .Should().Be("https://iam.seliseblocks.com/api/oidc/token");
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.BadRequest)]
        [InlineData(HttpStatusCode.InternalServerError)]
        public async Task A_non_success_status_yields_null_rather_than_throwing(HttpStatusCode status)
        {
            var (sut, _) = Build(status: status, body: """{"error":"invalid_client"}""");

            var token = await sut.GetTokenAsync(Credential(), "project-1");

            token.Should().BeNull();
        }

        [Theory]
        [InlineData("""{"access_token":""}""")]
        [InlineData("""{"expires_in":3600}""")]
        [InlineData("{}")]
        public async Task A_response_without_a_usable_token_yields_null(string body)
        {
            var (sut, _) = Build(body: body);

            var token = await sut.GetTokenAsync(Credential(), "project-1");

            token.Should().BeNull();
        }

        [Fact]
        public async Task Malformed_json_yields_null_rather_than_propagating_a_parse_error()
        {
            var (sut, _) = Build(body: "not json at all");

            var token = await sut.GetTokenAsync(Credential(), "project-1");

            token.Should().BeNull();
        }

        [Fact]
        public async Task A_transport_failure_yields_null()
        {
            var (sut, _) = Build(shouldThrow: new HttpRequestException("connection refused"));

            var token = await sut.GetTokenAsync(Credential(), "project-1");

            token.Should().BeNull();
        }
    }
}
