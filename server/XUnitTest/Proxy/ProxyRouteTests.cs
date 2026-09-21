using FluentAssertions;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Utils;

namespace XUnitTest.Proxy
{
    /// <summary>
    /// The route allowlist in isolation: template grammar, matching, rewrite, and the decisions the gateway
    /// turns into 403 / 405. These are the rules that decide which upstream endpoints a caller who never
    /// sees the credential can reach, so they are tested away from the forwarder's plumbing.
    /// </summary>
    public class ProxyRouteTests
    {
        private static ProxyRouteConfig Route(
            HttpMethodType method, string path, string? upstreamPath = null) => new()
        {
            Method = method,
            Path = path,
            UpstreamPath = upstreamPath,
        };

        // ---------- template grammar ----------

        [Theory]
        [InlineData("")]
        [InlineData("charges")]
        [InlineData("charges/{id}")]
        [InlineData("charges/{id}/refunds/{refundId}")]
        [InlineData("v1/_private/{a_1}")]
        public void TryParseTemplate_AcceptsWellFormedPaths(string path)
        {
            ProxyRoutePath.TryParseTemplate(path, out _, out var reason).Should().BeTrue(reason);
        }

        [Theory]
        [InlineData("a//b", "empty segment")]
        [InlineData("../admin", "'.' or '..'")]
        [InlineData("a/./b", "'.' or '..'")]
        [InlineData("charges/{}", "not a valid parameter")]
        [InlineData("charges/{1id}", "not a valid parameter")]
        [InlineData("charges/x{id}", "whole segment")]
        [InlineData("charges/{id}/{id}", "more than once")]
        public void TryParseTemplate_RejectsMalformedPaths(string path, string expectedReasonFragment)
        {
            ProxyRoutePath.TryParseTemplate(path, out _, out var reason).Should().BeFalse();
            reason.Should().Contain(expectedReasonFragment);
        }

        // ---------- matching ----------

        [Fact]
        public void Resolve_NoRoutes_AllowsOnlyTheBasePath()
        {
            var none = Array.Empty<ProxyRouteConfig>();

            ProxyRouteResolver.Resolve(none, HttpMethodType.Get, "").Status
                .Should().Be(ProxyRouteStatus.Allowed);
            ProxyRouteResolver.Resolve(none, HttpMethodType.Get, "ch_123").Status
                .Should().Be(ProxyRouteStatus.NotAllowed);
        }

        [Fact]
        public void Resolve_UndeclaredPath_IsRefused()
        {
            var routes = new[] { Route(HttpMethodType.Get, "charges/{id}") };

            ProxyRouteResolver.Resolve(routes, HttpMethodType.Get, "customers/cus_1").Status
                .Should().Be(ProxyRouteStatus.NotAllowed);
        }

        [Theory]
        [InlineData("../customers")]
        [InlineData("charges/../../admin")]
        [InlineData("charges/./ch_1")]
        public void Resolve_DotSegments_AreRefusedEvenWhenSegmentCountsLineUp(string path)
        {
            // Without this the URL is built literally and Uri collapses the dot segments afterwards, landing
            // the credential on an endpoint no route ever declared.
            var routes = new[]
            {
                Route(HttpMethodType.Get, "{a}"),
                Route(HttpMethodType.Get, "{a}/{b}"),
                Route(HttpMethodType.Get, "{a}/{b}/{c}"),
            };

            ProxyRouteResolver.Resolve(routes, HttpMethodType.Get, path).Status
                .Should().Be(ProxyRouteStatus.NotAllowed);
        }

        [Fact]
        public void Resolve_PathDeclaredForAnotherMethod_ReportsMethodMismatchWithTheAllowedSet()
        {
            var routes = new[]
            {
                Route(HttpMethodType.Get, "charges/{id}"),
                Route(HttpMethodType.Delete, "charges/{id}"),
            };

            var resolution = ProxyRouteResolver.Resolve(routes, HttpMethodType.Post, "charges/ch_1");

            resolution.Status.Should().Be(ProxyRouteStatus.MethodMismatch);
            resolution.AllowedMethods.Should().BeEquivalentTo("GET", "DELETE");
        }

        [Fact]
        public void Resolve_SegmentCountMustMatchExactly()
        {
            var routes = new[] { Route(HttpMethodType.Get, "charges/{id}") };

            ProxyRouteResolver.Resolve(routes, HttpMethodType.Get, "charges/ch_1/refunds").Status
                .Should().Be(ProxyRouteStatus.NotAllowed);
            ProxyRouteResolver.Resolve(routes, HttpMethodType.Get, "charges").Status
                .Should().Be(ProxyRouteStatus.NotAllowed);
        }

        [Fact]
        public void Resolve_LiteralRouteWinsOverParameterRoute_RegardlessOfDeclarationOrder()
        {
            var parameterFirst = new[]
            {
                Route(HttpMethodType.Get, "charges/{id}", "v1/charges/{id}"),
                Route(HttpMethodType.Get, "charges/summary", "v1/charges/summary"),
            };
            var literalFirst = parameterFirst.Reverse().ToArray();

            foreach (var routes in new[] { parameterFirst, literalFirst })
            {
                ProxyRouteResolver.Resolve(routes, HttpMethodType.Get, "charges/summary")
                    .UpstreamPathSuffix.Should().Be("v1/charges/summary");
            }
        }

        // ---------- rewrite ----------

        [Fact]
        public void Resolve_RewritesClientPathToTheUpstreamTemplate_SubstitutingParameters()
        {
            var routes = new[] { Route(HttpMethodType.Get, "orders/{id}/refunds", "v1/charges/{id}/refunds") };

            var resolution = ProxyRouteResolver.Resolve(routes, HttpMethodType.Get, "orders/ch_123/refunds");

            resolution.Status.Should().Be(ProxyRouteStatus.Allowed);
            resolution.UpstreamPathSuffix.Should().Be("v1/charges/ch_123/refunds");
        }

        [Fact]
        public void Resolve_NoUpstreamPath_UsesTheClientPathVerbatim()
        {
            var routes = new[] { Route(HttpMethodType.Get, "charges/{id}") };

            ProxyRouteResolver.Resolve(routes, HttpMethodType.Get, "charges/ch_9")
                .UpstreamPathSuffix.Should().Be("charges/ch_9");
        }

        [Fact]
        public void Resolve_BasePathRoute_ProducesNoSuffix()
        {
            var routes = new[] { Route(HttpMethodType.Get, "") };

            var resolution = ProxyRouteResolver.Resolve(routes, HttpMethodType.Get, "");

            resolution.Status.Should().Be(ProxyRouteStatus.Allowed);
            resolution.UpstreamPathSuffix.Should().BeEmpty();
        }

        [Fact]
        public void Resolve_CapturedSegmentIsNotInterpreted_SoAnIdCannotCarryAPath()
        {
            // "{id}" matches exactly one segment, so a value containing a slash simply fails to match
            // rather than silently extending the upstream path.
            var routes = new[] { Route(HttpMethodType.Get, "charges/{id}", "v1/charges/{id}") };

            ProxyRouteResolver.Resolve(routes, HttpMethodType.Get, "charges/ch_1/../../admin").Status
                .Should().Be(ProxyRouteStatus.NotAllowed);
        }

        // ---------- round-trip through the version codec ----------

        [Fact]
        public void RouteCodec_RoundTripsEveryOverride()
        {
            var route = new ProxyRouteConfig
            {
                Method = HttpMethodType.Post,
                Path = "orders/{id}",
                UpstreamPath = "v1/charges/{id}",
                Headers = new List<ProxyKeyValue> { new() { Key = "X-Scope", Value = "orders" } },
                Query = new List<ProxyKeyValue> { new() { Key = "expand", Value = "customer" } },
                BodyMerge = new List<ProxyKeyValue> { new() { Key = "source", Value = "blocks" } },
                ResponseMode = ProxyResponseMode.Select,
                ResponseInclude = new List<string> { "id", "amount" },
            };

            var decoded = ProxyRouteCodec.Decode(ProxyRouteCodec.AddressOf(route), ProxyRouteCodec.Encode(route));

            decoded.Should().NotBeNull();
            decoded!.Method.Should().Be(HttpMethodType.Post);
            decoded.Path.Should().Be("orders/{id}");
            ProxyRouteCodec.OverridesEqual(route, decoded).Should().BeTrue();
        }

        [Fact]
        public void RouteCodec_DistinguishesInheritFromExplicitlyEmpty()
        {
            // An empty BodyMerge is a route opting out of the proxy-wide merge; a null one inherits it.
            // If the codec flattened the two, a Revert would quietly re-enable fields a route had removed.
            var inherits = new ProxyRouteConfig { Method = HttpMethodType.Post, Path = "a", BodyMerge = null };
            var optsOut = new ProxyRouteConfig
            {
                Method = HttpMethodType.Post,
                Path = "a",
                BodyMerge = new List<ProxyKeyValue>(),
            };

            ProxyRouteCodec.OverridesEqual(inherits, optsOut).Should().BeFalse();
            ProxyRouteCodec.Decode("POST a", ProxyRouteCodec.Encode(optsOut))!.BodyMerge.Should().NotBeNull();
            ProxyRouteCodec.Decode("POST a", ProxyRouteCodec.Encode(inherits))!.BodyMerge.Should().BeNull();
        }

        [Fact]
        public void RouteCodec_RejectsTemplatesTheValidatorWouldHaveRefused()
        {
            // Revert applies stored values without re-running the validator, so a version row that reached
            // the database another way must not be able to install a route that walks out of the upstream.
            ProxyRouteCodec.Decode("GET ../admin", "{}").Should().BeNull();
            ProxyRouteCodec.Decode("GET charges/{id}", "{\"upstreamPath\":\"../../admin\"}").Should().BeNull();

            // An upstream template may only use parameters the client-facing path declares, or the rewrite
            // would send a literal "{other}" segment to the third party.
            ProxyRouteCodec.Decode("GET charges/{id}", "{\"upstreamPath\":\"v1/{other}\"}").Should().BeNull();
        }

        [Fact]
        public void Resolve_StoredRouteWithADotSegment_IsStillRefusedAtMatchTime()
        {
            // Constructed directly, bypassing validation, to stand in for a route that reached the database
            // by some route other than the API. The resolver is the last line before the credential is used.
            var routes = new[] { Route(HttpMethodType.Get, "orders/{id}", "../../admin/{id}") };

            ProxyRouteResolver.Resolve(routes, HttpMethodType.Get, "orders/ch_1").Status
                .Should().Be(ProxyRouteStatus.NotAllowed);
        }

        [Fact]
        public void RouteCodec_UnreadableValue_DecodesToNull_RatherThanThrowing()
        {
            ProxyRouteCodec.Decode("GET charges", "{ not json").Should().BeNull();
            ProxyRouteCodec.Decode("NOTAMETHOD charges", "{}").Should().BeNull();
        }
    }
}
