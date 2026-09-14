using Common.InternalService.Access;
using FluentAssertions;
using Functions.DomainService.Enums;
using Functions.DomainService.Models;
using Functions.DomainService.Services;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The one translation from a function's stored trigger to the shared access policy the public
    /// route enforces. Field by field, because a slip here is silently the wrong callers getting in.
    /// </summary>
    public class TriggerAccessPolicyTests
    {
        [Fact]
        public void Public_is_public_and_drops_any_leftover_rules()
        {
            var policy = TriggerAccessPolicy.From(new TriggerConfig
            {
                AuthMode = AuthMode.Public,
                Roles = ["Admin"],
                Permissions = ["orders.write"],
            });

            policy.IsPublic.Should().BeTrue();
            policy.HasRestrictions.Should().BeFalse();
        }

        [Fact]
        public void Token_maps_each_list_with_its_own_mode_and_the_combine()
        {
            var policy = TriggerAccessPolicy.From(new TriggerConfig
            {
                AuthMode = AuthMode.Token,
                Roles = ["Admin", "Finance"],
                RoleMatch = MatchMode.All,
                Permissions = ["orders.write"],
                PermissionMatch = MatchMode.Any,
                Combine = AccessCombine.And,
            });

            policy.Kind.Should().Be(EndpointAccessKind.BlocksToken);
            policy.Roles.Values.Should().Equal("Admin", "Finance");
            policy.Roles.RequiresAll.Should().BeTrue();
            policy.Permissions.Values.Should().Equal("orders.write");
            policy.Permissions.RequiresAll.Should().BeFalse();
            policy.Combine.Should().Be(EndpointAccessCombine.And);
        }

        [Fact]
        public void Values_are_trimmed_deduplicated_and_never_empty()
        {
            // An empty entry cannot be held by anyone: kept, it would turn "any of" into "none of".
            var policy = TriggerAccessPolicy.From(new TriggerConfig
            {
                AuthMode = AuthMode.Token,
                Roles = [" Admin ", "Admin", "", "   "],
            });

            policy.Roles.Values.Should().Equal("Admin");
        }

        [Fact]
        public void A_token_trigger_with_no_rules_requires_a_token_and_nothing_more()
        {
            var policy = TriggerAccessPolicy.From(new TriggerConfig { AuthMode = AuthMode.Token });

            policy.IsPublic.Should().BeFalse();
            policy.HasRestrictions.Should().BeFalse();
        }
    }
}
