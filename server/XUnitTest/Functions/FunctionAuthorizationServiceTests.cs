using Blocks.Genesis;
using FluentAssertions;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Models;
using Functions.DomainService.Services;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The authorization matrix: public versus token, roles versus permissions, any versus all.
    /// <para>
    /// Two boundary cases carry most of the risk and are tested from both directions. An empty
    /// requirement list must mean "authentication and nothing more", not "deny everything" —
    /// getting that wrong locks tenants out of their own functions. And a public function must
    /// be reachable without a token, but must not thereby become a way to run code with
    /// whatever identity happened to be attached.
    /// </para>
    /// </summary>
    public class FunctionAuthorizationServiceTests
    {
        private readonly FunctionAuthorizationService _service = new();

        private static BlocksContext Context(
            bool authenticated = true,
            string[]? roles = null,
            string[]? permissions = null) => BlocksContext.Create(
                tenantId: "tenant_1",
                roles: roles ?? [],
                userId: "user_1",
                isAuthenticated: authenticated,
                requestUri: "/api/fn/p/s",
                organizationId: "org_1",
                expireOn: DateTime.UtcNow.AddHours(1),
                email: "u@example.com",
                permissions: permissions ?? [],
                userName: "u",
                phoneNumber: null,
                displayName: "U",
                oauthToken: null,
                originalTenantId: "tenant_1",
                applicationDomain: "app.example.com",
                impersonated: false,
                impersonationSessionId: null);

        private static FunctionEntity Function(TriggerConfig trigger) => new()
        {
            ItemId = "fn_1",
            Trigger = trigger,
        };

        // ---------------------------------------------------------------- public ----

        [Fact]
        public void A_public_function_is_reachable_without_any_context()
        {
            var function = Function(new TriggerConfig { AuthMode = AuthMode.Public });

            _service.Authorize(function, null, null).Allowed.Should().BeTrue();
        }

        [Fact]
        public void A_public_function_ignores_role_requirements()
        {
            // Roles left over from a previous configuration must not resurrect when the tenant
            // switches the trigger to public.
            var function = Function(new TriggerConfig
            {
                AuthMode = AuthMode.Public,
                Roles = ["Admin"],
                Permissions = ["orders.write"],
            });

            _service.Authorize(function, null, Context(authenticated: false)).Allowed.Should().BeTrue();
        }

        // ----------------------------------------------------------------- token ----

        [Fact]
        public void A_token_function_refuses_an_anonymous_caller()
        {
            var function = Function(new TriggerConfig { AuthMode = AuthMode.Token });

            var result = _service.Authorize(function, null, null);

            result.Allowed.Should().BeFalse();
            result.Reason.Should().Contain("authentication");
        }

        [Fact]
        public void A_token_function_refuses_an_unauthenticated_context()
        {
            var function = Function(new TriggerConfig { AuthMode = AuthMode.Token });

            _service.Authorize(function, null, Context(authenticated: false)).Allowed.Should().BeFalse();
        }

        [Fact]
        public void A_token_function_with_no_requirements_needs_only_authentication()
        {
            // The boundary that matters: empty means no extra requirement, not deny-all.
            var function = Function(new TriggerConfig { AuthMode = AuthMode.Token });

            _service.Authorize(function, null, Context()).Allowed.Should().BeTrue();
        }

        // ----------------------------------------------------------------- roles ----

        [Fact]
        public void Any_role_match_needs_one_of_them()
        {
            var function = Function(new TriggerConfig
            {
                AuthMode = AuthMode.Token,
                Roles = ["Admin", "Editor"],
                RoleMatch = MatchMode.Any,
            });

            _service.Authorize(function, null, Context(roles: ["Editor"])).Allowed.Should().BeTrue();
            _service.Authorize(function, null, Context(roles: ["Viewer"])).Allowed.Should().BeFalse();
        }

        [Fact]
        public void All_role_match_needs_every_one_of_them()
        {
            var function = Function(new TriggerConfig
            {
                AuthMode = AuthMode.Token,
                Roles = ["Admin", "Editor"],
                RoleMatch = MatchMode.All,
            });

            _service.Authorize(function, null, Context(roles: ["Admin", "Editor"])).Allowed.Should().BeTrue();
            _service.Authorize(function, null, Context(roles: ["Admin"])).Allowed.Should().BeFalse();
        }

        [Fact]
        public void A_missing_role_is_named_in_the_reason()
        {
            var function = Function(new TriggerConfig
            {
                AuthMode = AuthMode.Token,
                Roles = ["Admin", "Auditor"],
                RoleMatch = MatchMode.All,
            });

            var result = _service.Authorize(function, null, Context(roles: ["Admin"]));

            result.Reason.Should().Contain("Auditor");
        }

        [Fact]
        public void Role_comparison_is_case_insensitive()
        {
            var function = Function(new TriggerConfig { AuthMode = AuthMode.Token, Roles = ["Admin"] });

            _service.Authorize(function, null, Context(roles: ["admin"])).Allowed.Should().BeTrue();
        }

        // ----------------------------------------------------------- permissions ----

        [Fact]
        public void Permissions_are_checked_the_same_way_as_roles()
        {
            var function = Function(new TriggerConfig
            {
                AuthMode = AuthMode.Token,
                Permissions = ["orders.read", "orders.write"],
                PermissionMatch = MatchMode.All,
            });

            _service.Authorize(function, null, Context(permissions: ["orders.read", "orders.write"]))
                .Allowed.Should().BeTrue();
            _service.Authorize(function, null, Context(permissions: ["orders.read"]))
                .Allowed.Should().BeFalse();
        }

        [Fact]
        public void Roles_and_permissions_must_both_be_satisfied()
        {
            // They are separate gates, not alternatives.
            var function = Function(new TriggerConfig
            {
                AuthMode = AuthMode.Token,
                Roles = ["Admin"],
                Permissions = ["orders.write"],
            });

            _service.Authorize(function, null, Context(roles: ["Admin"], permissions: ["orders.write"]))
                .Allowed.Should().BeTrue();
            _service.Authorize(function, null, Context(roles: ["Admin"], permissions: ["orders.read"]))
                .Allowed.Should().BeFalse();
            _service.Authorize(function, null, Context(roles: ["Viewer"], permissions: ["orders.write"]))
                .Allowed.Should().BeFalse();
        }

        // ---------------------------------------------------------------- trigger ----

        [Fact]
        public void A_function_with_http_disabled_refuses_everyone()
        {
            var function = Function(new TriggerConfig { HttpEnabled = false, AuthMode = AuthMode.Public });

            var result = _service.Authorize(function, null, Context());

            result.Allowed.Should().BeFalse();
            result.Reason.Should().Contain("HTTP");
        }

        [Fact]
        public void The_deployed_versions_trigger_wins_over_the_editable_one()
        {
            // Editing the trigger must not change how the running version authorizes until it
            // is deployed — otherwise a half-finished edit changes production immediately.
            var function = Function(new TriggerConfig { AuthMode = AuthMode.Public });
            var version = new FunctionVersionEntity
            {
                Trigger = new TriggerConfig { AuthMode = AuthMode.Token, Roles = ["Admin"] },
            };

            _service.Authorize(function, version, null).Allowed.Should().BeFalse();
            _service.Authorize(function, version, Context(roles: ["Admin"])).Allowed.Should().BeTrue();
        }

        [Fact]
        public void A_function_with_no_trigger_configuration_still_authorizes_safely()
        {
            var function = new FunctionEntity { ItemId = "fn_1", Trigger = null! };

            // Falls back to the defaults: HTTP on, token required.
            _service.Authorize(function, null, null).Allowed.Should().BeFalse();
            _service.Authorize(function, null, Context()).Allowed.Should().BeTrue();
        }

        // ------------------------------------------------------- AuthorizeForWorkflow ----

        [Fact]
        public void Workflow_invocation_is_gated_on_WorkflowEnabled_not_HttpEnabled()
        {
            // The two triggers are independent settings: HTTP can be off while a workflow can
            // still call the function, and vice versa.
            var function = Function(new TriggerConfig
            {
                HttpEnabled = false,
                WorkflowEnabled = true,
                AuthMode = AuthMode.Public,
            });

            _service.AuthorizeForWorkflow(function, null, null).Allowed.Should().BeTrue();
            _service.Authorize(function, null, null).Allowed.Should().BeFalse("HTTP is off for this function");
        }

        [Fact]
        public void Workflow_invocation_is_refused_when_WorkflowEnabled_is_false()
        {
            var function = Function(new TriggerConfig { WorkflowEnabled = false, AuthMode = AuthMode.Public });

            var result = _service.AuthorizeForWorkflow(function, null, null);

            result.Allowed.Should().BeFalse();
            result.Reason.Should().Contain("workflow");
        }

        [Fact]
        public void Workflow_invocation_still_enforces_roles_and_permissions()
        {
            var function = Function(new TriggerConfig
            {
                WorkflowEnabled = true,
                AuthMode = AuthMode.Token,
                Roles = ["Admin"],
            });

            _service.AuthorizeForWorkflow(function, null, Context(roles: ["Admin"])).Allowed.Should().BeTrue();
            _service.AuthorizeForWorkflow(function, null, Context(roles: ["Viewer"])).Allowed.Should().BeFalse();
            _service.AuthorizeForWorkflow(function, null, null).Allowed.Should().BeFalse("no context means no authentication");
        }

        [Fact]
        public void Workflow_invocation_also_prefers_the_deployed_versions_trigger()
        {
            var function = Function(new TriggerConfig { WorkflowEnabled = false });
            var version = new FunctionVersionEntity { Trigger = new TriggerConfig { WorkflowEnabled = true, AuthMode = AuthMode.Public } };

            _service.AuthorizeForWorkflow(function, version, null).Allowed.Should().BeTrue();
        }
    }
}
