using System.Security.Claims;
using Blocks.Genesis;
using Common.InternalService.Access;
using FluentAssertions;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Models;
using Functions.DomainService.Services;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The OR / AND toggle between the roles rule and the permissions rule, and the guarantee that
    /// the two evaluators of a trigger agree on it.
    /// <para>
    /// A function's "Who can call it" is enforced twice, by two different pieces of code: the
    /// workflow path runs <see cref="FunctionAuthorizationService"/> over a <see cref="BlocksContext"/>,
    /// the public HTTP route hands <see cref="TriggerAccessPolicy.From"/>'s mapping to the shared
    /// <see cref="EndpointAccessEvaluator"/> over a <see cref="ClaimsPrincipal"/>. The same trigger
    /// must mean the same thing on both, or a tenant who tested from a workflow gets a different
    /// answer from the URL. Every case here runs through both.
    /// </para>
    /// </summary>
    public class FunctionAccessCombineTests
    {
        private readonly FunctionAuthorizationService _service = new();

        private static TriggerConfig Trigger(
            string[]? roles = null, string[]? permissions = null,
            MatchMode roleMatch = MatchMode.Any, MatchMode permissionMatch = MatchMode.Any,
            AccessCombine combine = AccessCombine.Or) => new()
        {
            AuthMode = AuthMode.Token,
            Roles = [.. roles ?? []],
            Permissions = [.. permissions ?? []],
            RoleMatch = roleMatch,
            PermissionMatch = permissionMatch,
            Combine = combine,
        };

        private static BlocksContext Context(string[] roles, string[] permissions) => BlocksContext.Create(
            tenantId: "tenant_1", roles: roles, userId: "user_1", isAuthenticated: true, requestUri: "/api/fn/fn_1",
            organizationId: "org_1", expireOn: DateTime.UtcNow.AddHours(1), email: "u@example.com",
            permissions: permissions, userName: "u", phoneNumber: null, displayName: "U", oauthToken: null,
            originalTenantId: "tenant_1", applicationDomain: "app.example.com", impersonated: false,
            impersonationSessionId: null);

        private static ClaimsPrincipal Principal(string[] roles, string[] permissions)
        {
            var claims = new List<Claim> { new(BlocksContext.USER_ID_CLAIM, "user_1") };
            claims.AddRange(roles.Select(r => new Claim(BlocksContext.ROLES_CLAIM, r)));
            claims.AddRange(permissions.Select(p => new Claim(BlocksContext.PERMISSION_CLAIM, p)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        }

        /// <summary>Both evaluators, asserted to agree, returning the shared verdict.</summary>
        private bool BothSay(TriggerConfig trigger, string[] roles, string[] permissions)
        {
            var function = new FunctionEntity { ItemId = "fn_1", Trigger = trigger };
            var workflow = _service.AuthorizeForWorkflow(function, null, Context(roles, permissions)).Allowed;
            var http = EndpointAccessEvaluator.Evaluate(Principal(roles, permissions), TriggerAccessPolicy.From(trigger));

            http.Should().Be(workflow,
                "the HTTP route and the workflow path must read the same trigger the same way " +
                "(roles {0}, permissions {1})", string.Join(",", roles), string.Join(",", permissions));
            return workflow;
        }

        // ---------------------------------------------------------------------- OR ----

        [Fact]
        public void With_OR_either_configured_list_is_enough()
        {
            var trigger = Trigger(roles: ["Admin"], permissions: ["orders.write"], combine: AccessCombine.Or);

            BothSay(trigger, ["Admin"], []).Should().BeTrue();
            BothSay(trigger, [], ["orders.write"]).Should().BeTrue();
            BothSay(trigger, ["Admin"], ["orders.write"]).Should().BeTrue();
        }

        [Fact]
        public void With_OR_a_caller_holding_neither_is_refused()
        {
            var trigger = Trigger(roles: ["Admin"], permissions: ["orders.write"], combine: AccessCombine.Or);

            BothSay(trigger, ["Viewer"], ["orders.read"]).Should().BeFalse();
        }

        [Fact]
        public void OR_never_treats_an_empty_list_as_a_pass()
        {
            // The trap: "roles OR permissions" with no permissions configured must still require
            // the role. An unconfigured list is not a rule that passes; it is no rule at all.
            var trigger = Trigger(roles: ["Admin"], permissions: [], combine: AccessCombine.Or);

            BothSay(trigger, ["Viewer"], ["anything"]).Should().BeFalse();
            BothSay(trigger, ["Admin"], []).Should().BeTrue();
        }

        // --------------------------------------------------------------------- AND ----

        [Fact]
        public void With_AND_both_configured_lists_must_hold()
        {
            var trigger = Trigger(roles: ["Admin"], permissions: ["orders.write"], combine: AccessCombine.And);

            BothSay(trigger, ["Admin"], ["orders.write"]).Should().BeTrue();
            BothSay(trigger, ["Admin"], ["orders.read"]).Should().BeFalse();
            BothSay(trigger, ["Viewer"], ["orders.write"]).Should().BeFalse();
        }

        [Fact]
        public void Per_list_any_versus_all_still_governs_inside_each_list()
        {
            var trigger = Trigger(
                roles: ["Admin", "Finance"], roleMatch: MatchMode.All,
                permissions: ["a", "b"], permissionMatch: MatchMode.Any,
                combine: AccessCombine.And);

            BothSay(trigger, ["Admin", "Finance"], ["a"]).Should().BeTrue();
            BothSay(trigger, ["Admin"], ["a", "b"]).Should().BeFalse("roles need all, only one is held");
            BothSay(trigger, ["Admin", "Finance"], []).Should().BeFalse("permissions need any, none is held");
        }

        [Fact]
        public void No_rules_at_all_means_authentication_and_nothing_more()
        {
            BothSay(Trigger(), [], []).Should().BeTrue();
            BothSay(Trigger(combine: AccessCombine.And), [], []).Should().BeTrue();
        }

        [Fact]
        public void The_default_combine_is_OR_which_is_what_the_interface_has_always_shown()
        {
            new TriggerConfig().Combine.Should().Be(AccessCombine.Or);
        }

        [Fact]
        public void The_refusal_names_both_halves_under_OR()
        {
            var trigger = Trigger(roles: ["Admin"], permissions: ["orders.write"], combine: AccessCombine.Or);
            var function = new FunctionEntity { ItemId = "fn_1", Trigger = trigger };

            var result = _service.AuthorizeForWorkflow(function, null, Context(["Viewer"], []));

            result.Allowed.Should().BeFalse();
            result.Reason.Should().Contain("role").And.Contain("permission");
        }
    }
}
