using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Blocks.Genesis;
using DomainService.Workflow.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;
using StackExchange.Redis;
using static DomainService.Workflow.Services.WorkflowAuthService;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// Unit tests for <see cref="WorkflowAuthService"/>: the claim readers and authorization
    /// evaluation are pure and covered directly; <see cref="WorkflowAuthService.IsAuthorized"/>
    /// is covered end-to-end with a self-signed certificate standing in for the tenant's public cert.
    /// </summary>
    public class WorkflowAuthServiceTests : IDisposable
    {
        private const string TenantId = "tenant-auth";

        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<ICacheClient> _cacheClient = new();
        private readonly Mock<IDatabase> _cacheDatabase = new();
        private readonly Mock<IDelegatedTokenProvider> _delegatedTokenProvider = new();
        private readonly WorkflowAuthService _service;

        public WorkflowAuthServiceTests()
        {
            BlocksContext.IsTestMode = true;

            _cacheClient.Setup(c => c.CacheDatabase()).Returns(_cacheDatabase.Object);
            _service = new WorkflowAuthService(
                _tenants.Object,
                _cacheClient.Object,
                Mock.Of<ILogger<WorkflowAuthService>>(),
                _delegatedTokenProvider.Object);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        // ---------- EvaluateAuthorization ----------

        [Fact]
        public void EvaluateAuthorization_OrganizationMismatch_ReturnsFalse()
        {
            var principal = PrincipalWith(orgId: "org-a");
            var config = new AuthorizationConfig("org-b", null, null, AuthorizationMode.RolesOnly);

            EvaluateAuthorization(principal, config).Should().BeFalse();
        }

        [Theory]
        [InlineData("or", new[] { "editor" }, true)]
        [InlineData("or", new[] { "viewer" }, false)]
        [InlineData("and", new[] { "editor", "admin" }, true)]
        [InlineData("and", new[] { "editor", "owner" }, false)]
        public void EvaluateAuthorization_RolesOnly_RespectsRuleMode(string mode, string[] requiredRoles, bool expected)
        {
            var principal = PrincipalWith(orgId: "org-a", roles: ["editor", "admin"]);
            var config = new AuthorizationConfig("org-a", new Rule { Mode = mode, Values = requiredRoles.ToList() }, null, AuthorizationMode.RolesOnly);

            EvaluateAuthorization(principal, config).Should().Be(expected);
        }

        [Fact]
        public void EvaluateAuthorization_PermissionsOnly_ChecksPermissionsNotRoles()
        {
            var principal = PrincipalWith(orgId: "org-a", roles: ["editor"], permissions: ["workflow:read"]);
            var config = new AuthorizationConfig("org-a", null, new Rule { Mode = "or", Values = ["workflow:read"] }, AuthorizationMode.PermissionsOnly);

            EvaluateAuthorization(principal, config).Should().BeTrue();
        }

        [Fact]
        public void EvaluateAuthorization_RolesAndPermissions_RequiresBoth()
        {
            var principal = PrincipalWith(orgId: "org-a", roles: ["editor"], permissions: ["workflow:read"]);
            var passingConfig = new AuthorizationConfig(
                "org-a",
                new Rule { Mode = "or", Values = ["editor"] },
                new Rule { Mode = "or", Values = ["workflow:read"] },
                AuthorizationMode.RolesAndPermissions);
            var failingConfig = new AuthorizationConfig(
                "org-a",
                new Rule { Mode = "or", Values = ["editor"] },
                new Rule { Mode = "or", Values = ["workflow:write"] },
                AuthorizationMode.RolesAndPermissions);

            EvaluateAuthorization(principal, passingConfig).Should().BeTrue();
            EvaluateAuthorization(principal, failingConfig).Should().BeFalse();
        }

        [Fact]
        public void EvaluateAuthorization_EmptyRule_IsTreatedAsPass()
        {
            var principal = PrincipalWith(orgId: "org-a");
            var config = new AuthorizationConfig("org-a", new Rule { Mode = "or", Values = [] }, null, AuthorizationMode.RolesOnly);

            EvaluateAuthorization(principal, config).Should().BeTrue();
        }

        // ---------- claim readers ----------

        [Fact]
        public void ClaimReaders_ReadExpectedValues()
        {
            var principal = PrincipalWith(
                orgId: "org-a",
                roles: ["editor", "admin"],
                permissions: ["workflow:read"],
                userId: "user-42");

            GetUserId(principal).Should().Be("user-42");
            GetOrganization(principal).Should().Be("org-a");
            GetRoles(principal).Should().BeEquivalentTo("editor", "admin");
            GetPermissions(principal).Should().BeEquivalentTo("workflow:read");
        }

        [Fact]
        public void ClaimReaders_ReturnEmpty_WhenClaimsAreAbsent()
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity());

            GetUserId(principal).Should().BeEmpty();
            GetOrganization(principal).Should().BeEmpty();
            GetRoles(principal).Should().BeEmpty();
            GetPermissions(principal).Should().BeEmpty();
        }

        // ---------- AuthorizationConfig.TryParseAuthorizationMode ----------

        [Theory]
        [InlineData("RolesOnly", AuthorizationMode.RolesOnly)]
        [InlineData("PermissionsOnly", AuthorizationMode.PermissionsOnly)]
        [InlineData("RolesAndPermissions", AuthorizationMode.RolesAndPermissions)]
        public void TryParseAuthorizationMode_ParsesKnownValues(string wireValue, AuthorizationMode expected)
        {
            AuthorizationConfig.TryParseAuthorizationMode(wireValue).Should().Be(expected);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("unknown")]
        public void TryParseAuthorizationMode_ReturnsNull_ForUnrecognisedValues(string? wireValue)
        {
            AuthorizationConfig.TryParseAuthorizationMode(wireValue).Should().BeNull();
        }

        // ---------- GetAudience ----------

        [Fact]
        public void GetAudience_ReturnsConfiguredAudience_WhenPresent()
        {
            var tenant = TenantWithCert(out _, out _);
            tenant.JwtTokenParameters.Audiences = ["api://custom"];

            GetAudience(tenant).Should().Be("api://custom");
        }

        [Fact]
        public void GetAudience_FallsBackToDefault_WhenNotConfigured()
        {
            GetAudience(null).Should().Be("api://blocks-protected-api");
        }

        // ---------- IsAuthenticated / IsAuthorized ----------

        [Fact]
        public async Task IsAuthenticated_UnknownTenant_ReturnsFalse()
        {
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns((Tenant?)null);

            var result = await _service.IsAuthenticated(new DefaultHttpContext().Request, TenantId);

            result.Should().BeFalse();
        }

        [Fact]
        public async Task IsAuthenticated_MissingBearerToken_ReturnsFalse()
        {
            var tenant = TenantWithCert(out _, out _);
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(tenant);

            var result = await _service.IsAuthenticated(new DefaultHttpContext().Request, TenantId);

            result.Should().BeFalse();
        }

        [Fact]
        public async Task IsAuthenticated_ValidToken_AssignsHttpContextUser()
        {
            var tenant = TenantWithCert(out var cert, out var certBytes);
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(tenant);
            _cacheDatabase.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisValue)certBytes);

            var request = RequestWithBearerToken(cert, orgId: "org-a", userId: "user-1");

            var result = await _service.IsAuthenticated(request, TenantId);

            result.Should().BeTrue();
            request.HttpContext.User.Identity!.IsAuthenticated.Should().BeTrue();
            request.HttpContext.User.FindFirst(BlocksContext.USER_ID_CLAIM)!.Value.Should().Be("user-1");
            request.HttpContext.User.FindFirst(DelegationGrantFactory.TokenVersionClaim)!.Value.Should().Be("1");
            request.HttpContext.User.FindFirst(DelegationGrantFactory.SecurityStampClaim)!.Value.Should().Be("stamp-1");
        }

        [Fact]
        public async Task IsAuthorized_UnauthenticatedCaller_ReturnsFalse()
        {
            var tenant = TenantWithCert(out _, out _);
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(tenant);
            var config = new AuthorizationConfig("org-a", null, null, AuthorizationMode.RolesOnly);
            var httpContext = new DefaultHttpContext();
            var originalUser = httpContext.User;

            var result = await _service.IsAuthorized(httpContext.Request, TenantId, config);

            result.Should().BeFalse();
            httpContext.User.Should().BeSameAs(originalUser);
        }

        [Fact]
        public async Task IsAuthorized_ValidTokenButOrganizationMismatch_ReturnsFalse()
        {
            var tenant = TenantWithCert(out var cert, out var certBytes);
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(tenant);
            _cacheDatabase.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisValue)certBytes);

            var request = RequestWithBearerToken(cert, orgId: "org-a", userId: "user-1");
            var originalUser = request.HttpContext.User;
            var config = new AuthorizationConfig("org-b", null, null, AuthorizationMode.RolesOnly);

            var result = await _service.IsAuthorized(request, TenantId, config);

            result.Should().BeFalse();
            request.HttpContext.User.Should().BeSameAs(originalUser);
        }

        [Fact]
        public async Task IsAuthorized_ValidTokenAndMatchingRules_AssignsHttpContextUser()
        {
            var tenant = TenantWithCert(out var cert, out var certBytes);
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(tenant);
            _cacheDatabase.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisValue)certBytes);

            var request = RequestWithBearerToken(
                cert,
                orgId: "org-a",
                userId: "user-1",
                roles: ["editor"],
                email: "user1@example.com",
                userName: "user1",
                displayName: "User One");
            var config = new AuthorizationConfig("org-a", new Rule { Mode = "or", Values = ["editor"] }, null, AuthorizationMode.RolesOnly);

            var result = await _service.IsAuthorized(request, TenantId, config);

            result.Should().BeTrue();
            request.HttpContext.User.Identity!.IsAuthenticated.Should().BeTrue();
            request.HttpContext.User.FindFirst(BlocksContext.USER_ID_CLAIM)!.Value.Should().Be("user-1");
            request.HttpContext.User.FindFirst(DelegationGrantFactory.TokenVersionClaim)!.Value.Should().Be("1");
            request.HttpContext.User.FindFirst(DelegationGrantFactory.SecurityStampClaim)!.Value.Should().Be("stamp-1");
        }

        // ---------- CreateBlocksAuthorizationTokenAsync ----------

        [Fact]
        public async Task CreateBlocksAuthorizationTokenAsync_ReturnsProviderToken_WhenAvailable()
        {
            _delegatedTokenProvider.Setup(p => p.GetTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("delegated-token");

            var token = await _service.CreateBlocksAuthorizationTokenAsync();

            token.Should().Be("delegated-token");
        }

        [Fact]
        public async Task CreateBlocksAuthorizationTokenAsync_ReturnsNull_WhenNoGrantIsAvailable()
        {
            _delegatedTokenProvider.Setup(p => p.GetTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

            var token = await _service.CreateBlocksAuthorizationTokenAsync();

            token.Should().BeNull();
        }

        // ---------- helpers ----------

        private static ClaimsPrincipal PrincipalWith(
            string orgId = "",
            IEnumerable<string>? roles = null,
            IEnumerable<string>? permissions = null,
            string userId = "user-1")
        {
            var identity = new ClaimsIdentity(
            [
                new Claim(BlocksContext.USER_ID_CLAIM, userId),
                new Claim(BlocksContext.ORGANIZATION_ID_CLAIM, orgId),
                .. (roles ?? []).Select(r => new Claim(BlocksContext.ROLES_CLAIM, r)),
                .. (permissions ?? []).Select(p => new Claim(BlocksContext.PERMISSION_CLAIM, p)),
            ], authenticationType: "test");

            return new ClaimsPrincipal(identity);
        }

        private static Tenant TenantWithCert(out X509Certificate2 cert, out byte[] certBytes)
        {
            cert = CreateSelfSignedCertificate($"CN={TenantId}");
            certBytes = cert.Export(X509ContentType.Pfx);

            return new Tenant
            {
                TenantId = TenantId,
                DbConnectionString = "mongodb://localhost:27017",
                JwtTokenParameters = new JwtTokenParameters
                {
                    PublicCertificatePassword = string.Empty,
                    PrivateCertificatePassword = string.Empty,
                    IssueDate = DateTime.UtcNow,
                }
            };
        }

        private static HttpRequest RequestWithBearerToken(
            X509Certificate2 cert,
            string orgId,
            string userId,
            IEnumerable<string>? roles = null,
            string? email = null,
            string? userName = null,
            string? displayName = null)
        {
            var claims = new List<Claim>
            {
                new(BlocksContext.USER_ID_CLAIM, userId),
                new(BlocksContext.ORGANIZATION_ID_CLAIM, orgId),
                new(BlocksContext.TENANT_ID_CLAIM, TenantId),
                new(DelegationGrantFactory.TokenVersionClaim, "1"),
                new(DelegationGrantFactory.SecurityStampClaim, "stamp-1"),
            };
            claims.AddRange((roles ?? []).Select(r => new Claim(BlocksContext.ROLES_CLAIM, r)));
            if (email is not null) claims.Add(new Claim(BlocksContext.EMAIL_CLAIM, email));
            if (userName is not null) claims.Add(new Claim(BlocksContext.USER_NAME_CLAIM, userName));
            if (displayName is not null) claims.Add(new Claim(BlocksContext.DISPLAY_NAME_CLAIM, displayName));

            var signingCredentials = new X509SigningCredentials(cert, SecurityAlgorithms.RsaSha256);
            var token = new JwtSecurityTokenHandler().CreateEncodedJwt(new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddHours(1),
                SigningCredentials = signingCredentials,
            });

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers["Authorization"] = $"Bearer {token}";
            return httpContext.Request;
        }

        private static X509Certificate2 CreateSelfSignedCertificate(string subject)
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

            // Re-import so the returned certificate carries an exportable private key
            // (CreateSelfSigned's key is ephemeral on some platforms).
            return new X509Certificate2(cert.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.Exportable);
        }
    }
}
