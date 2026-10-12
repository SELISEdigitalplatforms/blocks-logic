using System.Security.Claims;
using Blocks.Genesis;
using FluentAssertions;
using Functions.DomainService.Enums;
using Functions.DomainService.Queue;
using Functions.DomainService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Functions
{
    /// <summary>
    /// Who gets a delegation grant for <c>ctx.blocks.accessToken</c>, and where its version
    /// material comes from: the validated token on the current request, or the grant a worker flow
    /// already holds — and only when either names exactly the run's caller. Anything else is "no
    /// token", and nothing here can fail the invoke.
    /// </summary>
    public class FunctionDelegationServiceTests : IDisposable
    {
        private const string Tenant = "tenant-dlg";
        private static readonly string Grant = "dg_" + new string('d', 64);
        private static readonly string Held = "dg_" + new string('e', 64);

        private readonly Mock<IDelegationGrantStore> _store = new();
        private readonly HttpContextAccessor _http = new();

        public FunctionDelegationServiceTests()
        {
            DelegatedTokenContext.Clear();
            _store
                .Setup(s => s.CreateAsync(It.IsAny<BlocksContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
                .ReturnsAsync(Grant);
        }

        public void Dispose()
        {
            DelegatedTokenContext.Clear();
            GC.SuppressFinalize(this);
        }

        private FunctionDelegationService Service(ILogger<FunctionDelegationService>? logger = null) =>
            new(_store.Object, _http, logger ?? NullLogger<FunctionDelegationService>.Instance);

        private static BlocksContext Caller(
            string tenantId = Tenant, string userId = "user-1", bool authenticated = true, bool impersonated = false,
            string organizationId = "org-1") =>
            BlocksContext.Create(
                tenantId: tenantId, roles: ["dev"], userId: userId, isAuthenticated: authenticated, requestUri: string.Empty,
                organizationId: organizationId, expireOn: DateTime.UtcNow.AddMinutes(5), email: string.Empty, permissions: [],
                userName: string.Empty, phoneNumber: string.Empty, displayName: string.Empty, oauthToken: string.Empty,
                originalTenantId: tenantId, applicationDomain: string.Empty, impersonated: impersonated,
                impersonationSessionId: string.Empty);

        /// <summary>The validated caller on the request, as the authorizer or the bearer handler leaves it.</summary>
        private void RequestBy(string userId, string? tokenVersion = "7", string? securityStamp = "stamp-1")
        {
            var claims = new List<Claim> { new(BlocksContext.USER_ID_CLAIM, userId) };
            if (tokenVersion is not null) claims.Add(new Claim(DelegationGrantFactory.TokenVersionClaim, tokenVersion));
            if (securityStamp is not null) claims.Add(new Claim(DelegationGrantFactory.SecurityStampClaim, securityStamp));
            _http.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")) };
        }

        private void Holding(DelegationGrantRecord? record)
        {
            DelegatedTokenContext.Set(Held);
            _store.Setup(s => s.GetAsync(Held)).ReturnsAsync(record);
        }

        private static DelegationGrantRecord HeldRecord(
            string tenantId = Tenant, string userId = "user-1", string organizationId = "org-1") => new()
            {
                TenantId = tenantId, UserId = userId, OrganizationId = organizationId,
                TokenVersion = "9", SecurityStamp = "stamp-held",
            };

        // ---------- a grant for the caller ----------

        [Fact]
        public async Task An_authenticated_caller_on_a_token_trigger_gets_a_grant_from_the_requests_claims()
        {
            RequestBy("user-1");
            var caller = Caller();

            var grant = await Service().CreateGrantAsync(Tenant, caller, AuthMode.Token);

            grant.Should().Be(Grant);
            _store.Verify(s => s.CreateAsync(caller, "7", "stamp-1", FunctionQueueKeys.DelegationGrantTtl), Times.Once);
        }

        [Fact]
        public void The_grant_outlives_the_longest_retry_chain_and_no_more()
        {
            // 5 attempts x 90 s + 4 delays x 900 s, the validators' ceilings.
            FunctionQueueKeys.DelegationGrantTtl.Should().BeGreaterThan(TimeSpan.FromSeconds(5 * 90 + 4 * 900));
            FunctionQueueKeys.DelegationGrantTtl.Should().BeLessThanOrEqualTo(FunctionQueueKeys.RunTtl);
        }

        [Fact]
        public async Task A_worker_flow_carries_its_held_grant_forward_for_the_same_caller()
        {
            Holding(HeldRecord());

            var grant = await Service().CreateGrantAsync(Tenant, Caller(), AuthMode.Token);

            grant.Should().Be(Grant);
            _store.Verify(s => s.CreateAsync(It.IsAny<BlocksContext>(), "9", "stamp-held", FunctionQueueKeys.DelegationGrantTtl), Times.Once);
        }

        [Fact]
        public async Task A_request_by_someone_else_is_not_used_and_falls_back_to_the_held_grant()
        {
            RequestBy("someone-else", tokenVersion: "1", securityStamp: "other");
            Holding(HeldRecord());

            await Service().CreateGrantAsync(Tenant, Caller(), AuthMode.Token);

            _store.Verify(s => s.CreateAsync(It.IsAny<BlocksContext>(), "9", "stamp-held", It.IsAny<TimeSpan?>()), Times.Once);
            _store.Verify(s => s.CreateAsync(It.IsAny<BlocksContext>(), "1", It.IsAny<string>(), It.IsAny<TimeSpan?>()), Times.Never);
        }

        // ---------- no grant ----------

        [Fact]
        public async Task A_public_trigger_never_gets_a_grant()
        {
            RequestBy("user-1");

            (await Service().CreateGrantAsync(Tenant, Caller(), AuthMode.Public)).Should().BeNull();

            NoGrantWritten();
        }

        [Fact]
        public async Task No_caller_gets_no_grant()
        {
            (await Service().CreateGrantAsync(Tenant, null, AuthMode.Token)).Should().BeNull();
            NoGrantWritten();
        }

        [Theory]
        [InlineData(false, "user-1", Tenant)]
        [InlineData(true, "", Tenant)]
        [InlineData(true, "user-1", "another-tenant")]
        public async Task An_unauthenticated_userless_or_foreign_caller_gets_no_grant(
            bool authenticated, string userId, string callerTenant)
        {
            RequestBy(userId);

            var grant = await Service().CreateGrantAsync(
                Tenant, Caller(tenantId: callerTenant, userId: userId, authenticated: authenticated),
                AuthMode.Token);

            grant.Should().BeNull();
            NoGrantWritten();
        }

        /// <summary>
        /// The Functions pages sit under the console's impersonate route, so this is the ordinary
        /// case, not an edge one: refusing it was why Test could not call Blocks at all. What keeps
        /// it safe is downstream — Genesis records the session on the grant and IAM refuses to
        /// redeem once the session ends — not a refusal here.
        /// </summary>
        [Fact]
        public async Task An_impersonated_caller_gets_a_grant()
        {
            RequestBy("user-1");

            var grant = await Service().CreateGrantAsync(
                Tenant, Caller(userId: "user-1", impersonated: true), AuthMode.Token);

            grant.Should().Be(Grant);
            _store.Verify(
                s => s.CreateAsync(
                    It.Is<BlocksContext>(c => c.Impersonated && c.UserId == "user-1"),
                    "7", "stamp-1", It.IsAny<TimeSpan?>()),
                Times.Once);
        }

        [Fact]
        public async Task A_blank_run_tenant_gets_no_grant()
        {
            RequestBy("user-1");
            (await Service().CreateGrantAsync(" ", Caller(), AuthMode.Token)).Should().BeNull();
            NoGrantWritten();
        }

        [Theory]
        [InlineData(null, "stamp-1")]
        [InlineData("7", null)]
        [InlineData(" ", "stamp-1")]
        public async Task Missing_version_material_means_no_grant(string? tokenVersion, string? securityStamp)
        {
            // IAM compares both on redemption; a grant without them could never be redeemed.
            RequestBy("user-1", tokenVersion, securityStamp);

            (await Service().CreateGrantAsync(Tenant, Caller(), AuthMode.Token)).Should().BeNull();
            NoGrantWritten();
        }

        [Theory]
        [InlineData("another-tenant", "user-1", "org-1")]
        [InlineData(Tenant, "user-2", "org-1")]
        [InlineData(Tenant, "user-1", "org-2")]
        public async Task A_held_grant_for_anyone_else_is_never_carried_forward(string tenantId, string userId, string organizationId)
        {
            Holding(HeldRecord(tenantId, userId, organizationId));

            (await Service().CreateGrantAsync(Tenant, Caller(), AuthMode.Token)).Should().BeNull();
            NoGrantWritten();
        }

        [Fact]
        public async Task A_held_grant_that_has_expired_means_no_grant()
        {
            Holding(null);

            (await Service().CreateGrantAsync(Tenant, Caller(), AuthMode.Token)).Should().BeNull();
            NoGrantWritten();
        }

        [Fact]
        public async Task A_store_failure_means_no_grant_never_a_failed_invoke_and_logs_no_detail()
        {
            RequestBy("user-1");
            _store
                .Setup(s => s.CreateAsync(It.IsAny<BlocksContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
                .ThrowsAsync(new InvalidOperationException($"redis said {Grant}"));
            var logger = new Mock<ILogger<FunctionDelegationService>>();

            (await Service(logger.Object).CreateGrantAsync(Tenant, Caller(), AuthMode.Token)).Should().BeNull();

            logger.Verify(l => l.Log(
                LogLevel.Warning, It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(nameof(InvalidOperationException)) && !v.ToString()!.Contains(Grant)),
                null, It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }

        // ---------- cleanup ----------

        [Fact]
        public async Task Deleting_a_grant_removes_it_and_tolerates_nothing_to_delete_or_a_failing_store()
        {
            await Service().DeleteGrantAsync(Grant);
            _store.Verify(s => s.DeleteAsync(Grant), Times.Once);

            await Service().DeleteGrantAsync(null);
            await Service().DeleteGrantAsync(" ");
            _store.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Once);

            _store.Setup(s => s.DeleteAsync(Grant)).ThrowsAsync(new TimeoutException("redis"));
            var act = () => Service().DeleteGrantAsync(Grant);
            await act.Should().NotThrowAsync();
        }

        private void NoGrantWritten() =>
            _store.Verify(s => s.CreateAsync(It.IsAny<BlocksContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()),
                Times.Never);
    }
}
