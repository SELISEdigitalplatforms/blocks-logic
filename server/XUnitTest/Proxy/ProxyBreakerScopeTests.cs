using Common.InternalService.Access;
using FluentAssertions;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Services;
using Proxy.DomainService.Utils;

namespace XUnitTest.Proxy
{
    /// <summary>
    /// PS-8 (2026-10-07): an anonymous flood of slow calls on one public proxy opens only that proxy's
    /// "anonymous" circuit. Signed-in callers, workflow steps, Test and the tenant's other proxies to the same
    /// vendor keep working.
    /// </summary>
    public class ProxyBreakerScopeTests
    {
        private static readonly ProxyBreakerConfig Breaker = new() { FailureThreshold = 3, OpenSeconds = 60 };
        private const string Host = "api.openai.com";

        private static ProxyResolvedConfig Config(string proxyId) => new()
        {
            ProxyId = proxyId, Slug = "ai-" + proxyId, Upstream = "https://api.openai.com",
            Access = EndpointAccessPolicy.AllowPublic(),
        };

        private static ProxyForwardRequest Call(
            string? userId = null, string kind = ProxyCallerKind.Client, bool isTest = false, string tenant = "t1") => new()
        {
            TenantId = tenant, UserId = userId, CallerKind = kind, IsTest = isTest,
            Slug = "ai", Method = "POST", PathSuffix = "", RequestPath = "/api/proxy/gateway/ai",
        };

        [Fact]
        public void Each_caller_type_and_proxy_gets_its_own_circuit()
        {
            var scopes = new[]
            {
                ProxyGatewayService.BreakerScope(Call(), Config("p1")),
                ProxyGatewayService.BreakerScope(Call(userId: "u1"), Config("p1")),
                ProxyGatewayService.BreakerScope(Call(kind: ProxyCallerKind.Workflow), Config("p1")),
                ProxyGatewayService.BreakerScope(Call(isTest: true, userId: "u1"), Config("p1")),
                ProxyGatewayService.BreakerScope(Call(), Config("p2")),
                ProxyGatewayService.BreakerScope(Call(tenant: "t2"), Config("p1")),
            };

            scopes.Should().OnlyHaveUniqueItems();
            scopes.Should().AllSatisfy(s => s.Should().Contain("|"));
            ProxyGatewayService.BreakerScope(Call(userId: "u1"), Config("p1"))
                .Should().Be(ProxyGatewayService.BreakerScope(Call(userId: "u2"), Config("p1")),
                    "all signed-in callers of one proxy share a circuit, so a real outage is still found fast");
        }

        [Fact]
        public void An_anonymous_flood_blocks_only_anonymous_callers_of_that_proxy()
        {
            var breaker = new ProxyCircuitBreaker();
            var anonymous = ProxyGatewayService.BreakerScope(Call(), Config("p1"));

            for (var i = 0; i < 3; i++) breaker.RecordFailure(anonymous, Host, Breaker);

            breaker.IsOpen(anonymous, Host, Breaker).Should().BeTrue();
            breaker.IsOpen(ProxyGatewayService.BreakerScope(Call(userId: "u1"), Config("p1")), Host, Breaker).Should().BeFalse();
            breaker.IsOpen(ProxyGatewayService.BreakerScope(Call(kind: ProxyCallerKind.Workflow), Config("p1")), Host, Breaker).Should().BeFalse();
            breaker.IsOpen(ProxyGatewayService.BreakerScope(Call(userId: "u1"), Config("p2")), Host, Breaker).Should().BeFalse();
        }

        [Fact]
        public void An_unsaved_draft_under_test_has_a_scope_too()
        {
            ProxyGatewayService.BreakerScope(Call(isTest: true), Config(""))
                .Should().Be("t1|draft:ai-|test");
        }
    }
}
