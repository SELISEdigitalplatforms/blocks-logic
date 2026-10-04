using System.Security.Claims;
using Blocks.Genesis;
using Common.InternalService.Access;
using FluentAssertions;

namespace XUnitTest.Common
{
    /// <summary>
    /// Covers the shared "Who can call it" evaluator: public always passes; a token policy with no rules
    /// passes any signed-in caller; a single configured rule decides alone; two configured rules combine by
    /// OR / AND; an unconfigured rule never participates (so OR cannot be satisfied by an empty list).
    /// </summary>
    public class EndpointAccessEvaluatorTests
    {
        private static ClaimsPrincipal Caller(IEnumerable<string>? roles = null, IEnumerable<string>? permissions = null, string orgId = "")
        {
            var identity = new ClaimsIdentity(
            [
                new Claim(BlocksContext.USER_ID_CLAIM, "user-1"),
                new Claim(BlocksContext.ORGANIZATION_ID_CLAIM, orgId),
                .. (roles ?? []).Select(r => new Claim(BlocksContext.ROLES_CLAIM, r)),
                .. (permissions ?? []).Select(p => new Claim(BlocksContext.PERMISSION_CLAIM, p)),
            ], authenticationType: "test");
            return new ClaimsPrincipal(identity);
        }

        private static EndpointAccessRule Rule(string mode, params string[] values) =>
            new() { Mode = mode, Values = values.ToList() };

        [Fact]
        public void Public_AlwaysPasses_EvenWithRules()
        {
            var policy = EndpointAccessPolicy.AllowPublic();
            policy.Roles = Rule("any", "admin");

            EndpointAccessEvaluator.Evaluate(Caller(), policy).Should().BeTrue();
        }

        [Fact]
        public void Token_NoRules_PassesAnySignedInCaller()
        {
            EndpointAccessEvaluator.Evaluate(Caller(), EndpointAccessPolicy.RequireToken()).Should().BeTrue();
        }

        [Theory]
        [InlineData("any", new[] { "editor" }, true)]
        [InlineData("any", new[] { "viewer" }, false)]
        [InlineData("all", new[] { "editor", "admin" }, true)]
        [InlineData("all", new[] { "editor", "owner" }, false)]
        public void RolesOnly_RespectsRuleMode(string mode, string[] required, bool expected)
        {
            var policy = EndpointAccessPolicy.RequireToken();
            policy.Roles = Rule(mode, required);

            EndpointAccessEvaluator.Evaluate(Caller(roles: ["editor", "admin"]), policy).Should().Be(expected);
        }

        [Fact]
        public void PermissionsOnly_IgnoresRoles()
        {
            var policy = EndpointAccessPolicy.RequireToken();
            policy.Permissions = Rule("any", "proxy:call");

            EndpointAccessEvaluator.Evaluate(Caller(roles: ["admin"], permissions: ["proxy:call"]), policy).Should().BeTrue();
            EndpointAccessEvaluator.Evaluate(Caller(roles: ["admin"], permissions: []), policy).Should().BeFalse();
        }

        [Fact]
        public void BothRules_Or_PassesWhenEitherPasses()
        {
            var policy = EndpointAccessPolicy.RequireToken();
            policy.Roles = Rule("any", "admin");
            policy.Permissions = Rule("any", "proxy:call");
            policy.Combine = EndpointAccessCombine.Or;

            EndpointAccessEvaluator.Evaluate(Caller(roles: ["admin"]), policy).Should().BeTrue();
            EndpointAccessEvaluator.Evaluate(Caller(permissions: ["proxy:call"]), policy).Should().BeTrue();
            EndpointAccessEvaluator.Evaluate(Caller(roles: ["viewer"], permissions: ["other"]), policy).Should().BeFalse();
        }

        [Fact]
        public void BothRules_And_RequiresBoth()
        {
            var policy = EndpointAccessPolicy.RequireToken();
            policy.Roles = Rule("any", "admin");
            policy.Permissions = Rule("any", "proxy:call");
            policy.Combine = EndpointAccessCombine.And;

            EndpointAccessEvaluator.Evaluate(Caller(roles: ["admin"], permissions: ["proxy:call"]), policy).Should().BeTrue();
            EndpointAccessEvaluator.Evaluate(Caller(roles: ["admin"]), policy).Should().BeFalse();
            EndpointAccessEvaluator.Evaluate(Caller(permissions: ["proxy:call"]), policy).Should().BeFalse();
        }

        [Fact]
        public void Or_WithOnlyOneRuleConfigured_IsDecidedByThatRuleAlone()
        {
            // The empty permissions rule must not count as "passed" under OR, or every caller would get in.
            var policy = EndpointAccessPolicy.RequireToken();
            policy.Roles = Rule("any", "admin");
            policy.Combine = EndpointAccessCombine.Or;

            EndpointAccessEvaluator.Evaluate(Caller(roles: ["viewer"]), policy).Should().BeFalse();
            EndpointAccessEvaluator.Evaluate(Caller(roles: ["admin"]), policy).Should().BeTrue();
        }

        [Fact]
        public void Organization_MustMatch_WhenConfigured()
        {
            var policy = EndpointAccessPolicy.RequireToken();
            policy.OrganizationId = "org-a";

            EndpointAccessEvaluator.Evaluate(Caller(orgId: "org-a"), policy).Should().BeTrue();
            EndpointAccessEvaluator.Evaluate(Caller(orgId: "org-b"), policy).Should().BeFalse();

            // An empty org claim means the tenant's default organization.
            policy.OrganizationId = "default";
            EndpointAccessEvaluator.Evaluate(Caller(orgId: ""), policy).Should().BeTrue();
        }

        [Fact]
        public void Roles_AreReadFromRoleClaimTypeAndLiteralClaim()
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimsIdentity.DefaultRoleClaimType, "mapped"), new Claim(BlocksContext.ROLES_CLAIM, "literal")],
                authenticationType: "test");

            EndpointAccessEvaluator.GetRoles(new ClaimsPrincipal(identity)).Should().BeEquivalentTo(["mapped", "literal"]);
        }

        [Fact]
        public void Rule_NormalizeMode_MapsAndAllToAll_EverythingElseToAny()
        {
            EndpointAccessRule.NormalizeMode("all").Should().Be("all");
            EndpointAccessRule.NormalizeMode("AND").Should().Be("all");
            EndpointAccessRule.NormalizeMode("any").Should().Be("any");
            EndpointAccessRule.NormalizeMode("or").Should().Be("any");
            EndpointAccessRule.NormalizeMode(null).Should().Be("any");
        }

        [Fact]
        public void Clone_IsDeep()
        {
            var policy = EndpointAccessPolicy.RequireToken();
            policy.Roles = Rule("all", "a");

            var clone = policy.Clone();
            clone.Roles.Values.Add("b");

            policy.Roles.Values.Should().Equal("a");
        }
    }
}
