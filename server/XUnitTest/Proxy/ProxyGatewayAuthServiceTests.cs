using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Proxy.DomainService.Services;
using StackExchange.Redis;

namespace XUnitTest.Proxy
{
    /// <summary>
    /// Covers <see cref="ProxyGatewayAuthService"/> against a self-signed certificate standing in for the
    /// tenant's public cert: missing <c>X-Blocks-Key</c>, unknown tenant, missing / expired / wrong-tenant
    /// bearer, and the happy path (C1).
    /// </summary>
    public class ProxyGatewayAuthServiceTests
    {
        private const string TenantId = "T1";
        private const string OtherTenantId = "T2";

        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<ICacheClient> _cacheClient = new();
        private readonly Mock<IDatabase> _cacheDatabase = new();
        private readonly ProxyGatewayAuthService _service;

        public ProxyGatewayAuthServiceTests()
        {
            _cacheClient.Setup(c => c.CacheDatabase()).Returns(_cacheDatabase.Object);
            _service = new ProxyGatewayAuthService(
                _tenants.Object, _cacheClient.Object, Mock.Of<ILogger<ProxyGatewayAuthService>>());
        }

        [Fact]
        public async Task Authenticate_MissingTenantKeyHeader_Fails()
        {
            var request = RequestWith(bearer: "whatever", tenantKey: null);

            var result = await _service.AuthenticateAsync(request);

            result.IsAuthenticated.Should().BeFalse();
        }

        [Fact]
        public async Task Authenticate_UnknownTenant_Fails()
        {
            _tenants.Setup(t => t.GetTenantByID("nope")).Returns((Tenant?)null);
            var request = RequestWith(bearer: "whatever", tenantKey: "nope");

            var result = await _service.AuthenticateAsync(request);

            result.IsAuthenticated.Should().BeFalse();
        }

        [Fact]
        public async Task Authenticate_MissingBearer_Fails()
        {
            var tenant = TenantWithCert(TenantId, out _, out var certBytes);
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(tenant);
            GivenCachedCert(certBytes);

            var request = new DefaultHttpContext().Request;
            request.Headers[ProxyGatewayAuthService.TenantKeyHeader] = TenantId;

            var result = await _service.AuthenticateAsync(request);

            result.IsAuthenticated.Should().BeFalse();
        }

        [Fact]
        public async Task Authenticate_WrongTenantBearer_Fails()
        {
            // X-Blocks-Key names T1, but the bearer is signed by T2's certificate.
            var t1 = TenantWithCert(TenantId, out _, out var t1CertBytes);
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(t1);
            GivenCachedCert(t1CertBytes);

            using var t2Cert = CreateCert($"CN={OtherTenantId}");
            var request = RequestWith(bearer: Jwt(t2Cert, TimeSpan.FromHours(1)), tenantKey: TenantId);

            var result = await _service.AuthenticateAsync(request);

            result.IsAuthenticated.Should().BeFalse();
        }

        [Fact]
        public async Task Authenticate_ExpiredBearer_Fails()
        {
            var tenant = TenantWithCert(TenantId, out var cert, out var certBytes);
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(tenant);
            GivenCachedCert(certBytes);

            using (cert)
            {
                var request = RequestWith(bearer: Jwt(cert, TimeSpan.FromMinutes(-5)), tenantKey: TenantId);

                var result = await _service.AuthenticateAsync(request);

                result.IsAuthenticated.Should().BeFalse();
            }
        }

        [Fact]
        public async Task Authenticate_ValidKeyAndBearer_Succeeds_AndResolvesUserId()
        {
            var tenant = TenantWithCert(TenantId, out var cert, out var certBytes);
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(tenant);
            GivenCachedCert(certBytes);

            using (cert)
            {
                var request = RequestWith(bearer: Jwt(cert, TimeSpan.FromHours(1), userId: "user-9"), tenantKey: TenantId);

                var result = await _service.AuthenticateAsync(request);

                result.IsAuthenticated.Should().BeTrue();
                result.TenantId.Should().Be(TenantId);
                result.UserId.Should().Be("user-9");
            }
        }

        // ---------- helpers ----------

        private void GivenCachedCert(byte[] certBytes) =>
            _cacheDatabase.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisValue)certBytes);

        private static HttpRequest RequestWith(string bearer, string? tenantKey)
        {
            var request = new DefaultHttpContext().Request;
            if (tenantKey is not null)
            {
                request.Headers[ProxyGatewayAuthService.TenantKeyHeader] = tenantKey;
            }

            request.Headers["Authorization"] = $"Bearer {bearer}";
            return request;
        }

        private static Tenant TenantWithCert(string tenantId, out X509Certificate2 cert, out byte[] certBytes)
        {
            cert = CreateCert($"CN={tenantId}");
            certBytes = cert.Export(X509ContentType.Pfx);
            return new Tenant
            {
                TenantId = tenantId,
                DbConnectionString = "mongodb://localhost:27017",
                JwtTokenParameters = new JwtTokenParameters
                {
                    PublicCertificatePassword = string.Empty,
                    PrivateCertificatePassword = string.Empty,
                    IssueDate = DateTime.UtcNow,
                },
            };
        }

        private static string Jwt(X509Certificate2 cert, TimeSpan lifetime, string userId = "user-1")
        {
            var credentials = new X509SigningCredentials(cert, SecurityAlgorithms.RsaSha256);
            var expires = DateTime.UtcNow.Add(lifetime);
            var notBefore = expires <= DateTime.UtcNow ? expires.AddMinutes(-5) : DateTime.UtcNow;
            return new JwtSecurityTokenHandler().CreateEncodedJwt(new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[] { new Claim(BlocksContext.USER_ID_CLAIM, userId) }),
                NotBefore = notBefore,
                IssuedAt = notBefore,
                Expires = expires,
                SigningCredentials = credentials,
            });
        }

        private static X509Certificate2 CreateCert(string subject)
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
            return new X509Certificate2(cert.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.Exportable);
        }
    }
}
