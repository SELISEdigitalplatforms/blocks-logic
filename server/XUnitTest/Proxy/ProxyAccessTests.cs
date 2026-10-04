using Common.InternalService.Access;
using FluentAssertions;
using Proxy.DomainService.Dtos;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Services;
using Proxy.DomainService.Utils;

namespace XUnitTest.Proxy
{
    /// <summary>
    /// "Who can call it" on a proxy: how the validator normalizes the input block, how the change history
    /// encodes / diffs / reverts it, and the headline the history shows.
    /// </summary>
    public class ProxyAccessTests
    {
        private static ProxyConfigValidationResult Validate(ProxyAccessInputDto? access) =>
            ProxyConfigValidator.Validate("Stripe", "https://api.stripe.com", ["GET"], null, null, access: access);

        // ---------- validator ----------

        [Fact]
        public void Validate_OmittedAccess_DefaultsToTokenRequired_NoRestrictions()
        {
            var result = Validate(null);

            result.IsValid.Should().BeTrue();
            result.Access.Kind.Should().Be(EndpointAccessKind.BlocksToken);
            result.Access.HasRestrictions.Should().BeFalse();
            result.Access.Combine.Should().Be(EndpointAccessCombine.Or);
        }

        [Fact]
        public void Validate_NormalizesKindCombineAndRules()
        {
            var result = Validate(new ProxyAccessInputDto
            {
                Kind = "blocksToken",
                Combine = "and",
                Roles = new ProxyAccessRuleDto { Mode = "ALL", Values = [" admin ", "admin", "", "editor"] },
                Permissions = new ProxyAccessRuleDto { Mode = null, Values = ["proxy:call"] },
            });

            result.IsValid.Should().BeTrue();
            result.Access.Kind.Should().Be(EndpointAccessKind.BlocksToken);
            result.Access.Combine.Should().Be(EndpointAccessCombine.And);
            result.Access.Roles.Mode.Should().Be("all");
            result.Access.Roles.Values.Should().Equal("admin", "editor");
            result.Access.Permissions.Mode.Should().Be("any");
            result.Access.Permissions.Values.Should().Equal("proxy:call");
        }

        [Fact]
        public void Validate_Public_WithRules_IsRejected()
        {
            var result = Validate(new ProxyAccessInputDto
            {
                Kind = "Public",
                Roles = new ProxyAccessRuleDto { Values = ["admin"] },
            });

            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainKey("access");
        }

        [Fact]
        public void Validate_Public_WithoutRules_IsAccepted()
        {
            var result = Validate(new ProxyAccessInputDto { Kind = "public", Roles = new ProxyAccessRuleDto { Values = [] } });

            result.IsValid.Should().BeTrue();
            result.Access.IsPublic.Should().BeTrue();
        }

        [Theory]
        [InlineData("Anonymous", null)]
        [InlineData(null, "Xor")]
        public void Validate_UnknownKindOrCombine_IsRejected(string? kind, string? combine)
        {
            var result = Validate(new ProxyAccessInputDto { Kind = kind, Combine = combine });

            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainKey("access");
        }

        [Fact]
        public void Validate_RuleValueWithComma_IsRejected()
        {
            var result = Validate(new ProxyAccessInputDto
            {
                Permissions = new ProxyAccessRuleDto { Values = ["a,b"] },
            });

            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainKey("access.permissions");
        }

        // ---------- change history ----------

        private static ProxyConfigSnapshot Snapshot(Action<EndpointAccessPolicy>? access = null)
        {
            var snapshot = new ProxyConfigSnapshot
            {
                Name = "Stripe",
                Slug = "stripe",
                Upstream = "https://api.stripe.com",
                Methods = [HttpMethodType.Get],
                Enabled = true,
            };
            access?.Invoke(snapshot.Access);
            return snapshot;
        }

        [Fact]
        public void Diff_NoAccessChange_IsEmpty_EvenWhenChipsAreReordered()
        {
            var before = Snapshot(a => a.Roles = new EndpointAccessRule { Values = ["admin", "editor"] });
            var after = Snapshot(a => a.Roles = new EndpointAccessRule { Values = ["editor", "admin"] });

            ProxyChangeSet.Diff(before, after).Should().BeEmpty();
        }

        [Fact]
        public void Diff_MakingPublic_ListsKindAndRemovedRules()
        {
            var before = Snapshot(a => a.Roles = new EndpointAccessRule { Values = ["admin"] });
            var after = Snapshot(a => a.Kind = EndpointAccessKind.Public);

            var changes = ProxyChangeSet.Diff(before, after);

            changes.Select(c => c.Field).Should().Equal("access", "access:roles");
            changes[0].Before.Should().Be("BlocksToken");
            changes[0].After.Should().Be("Public");
            changes[1].Before.Should().Be("any of admin");
            changes[1].After.Should().BeNull();
        }

        [Fact]
        public void Diff_CombineChange_IsItsOwnField()
        {
            var before = Snapshot();
            var after = Snapshot(a => a.Combine = EndpointAccessCombine.And);

            var change = ProxyChangeSet.Diff(before, after).Should().ContainSingle().Subject;
            change.Field.Should().Be("access:combine");
            change.Before.Should().Be("Or");
            change.After.Should().Be("And");
        }

        [Fact]
        public void ReadAndApply_RoundTripEveryAccessField()
        {
            var proxy = new ProxyDetailEntity
            {
                TenantId = "t", Name = "Stripe", Slug = "stripe", Upstream = "https://api.stripe.com",
            };

            ProxyChangeSet.ApplyField(proxy, "access", "Public");
            ProxyChangeSet.ReadField(proxy, "access").Should().Be("Public");

            ProxyChangeSet.ApplyField(proxy, "access", "BlocksToken");
            ProxyChangeSet.ApplyField(proxy, "access:roles", "all of admin, editor");
            proxy.Access.Roles.RequiresAll.Should().BeTrue();
            proxy.Access.Roles.Values.Should().Equal("admin", "editor");
            ProxyChangeSet.ReadField(proxy, "access:roles").Should().Be("all of admin, editor");

            ProxyChangeSet.ApplyField(proxy, "access:permissions", "any of proxy:call");
            ProxyChangeSet.ReadField(proxy, "access:permissions").Should().Be("any of proxy:call");

            ProxyChangeSet.ApplyField(proxy, "access:permissions", null);
            proxy.Access.Permissions.IsConfigured.Should().BeFalse();
            ProxyChangeSet.ReadField(proxy, "access:permissions").Should().BeNull();

            ProxyChangeSet.ApplyField(proxy, "access:combine", "And");
            ProxyChangeSet.ReadField(proxy, "access:combine").Should().Be("And");

            ProxyChangeSet.ApplyField(proxy, "access:organization", "org-a");
            ProxyChangeSet.ReadField(proxy, "access:organization").Should().Be("org-a");
            ProxyChangeSet.ApplyField(proxy, "access:organization", null);
            ProxyChangeSet.ReadField(proxy, "access:organization").Should().BeNull();
        }

        [Fact]
        public void Summarize_NamesAccessChangesOutright()
        {
            ProxyChangeSet.Summarize([new ProxyFieldChange { Field = "access", Before = "BlocksToken", After = "Public" }])
                .Should().Be("Endpoint made public");
            ProxyChangeSet.Summarize([new ProxyFieldChange { Field = "access", Before = "Public", After = "BlocksToken" }])
                .Should().Be("Endpoint now requires a Blocks token");
            ProxyChangeSet.Summarize([new ProxyFieldChange { Field = "access:roles", Before = null, After = "any of admin" }])
                .Should().Be("Role restriction added");
            ProxyChangeSet.Summarize([new ProxyFieldChange { Field = "access:permissions", Before = "any of x", After = null }])
                .Should().Be("Permission restriction removed");
            ProxyChangeSet.Summarize([new ProxyFieldChange { Field = "access:combine", Before = "Or", After = "And" }])
                .Should().Be("Roles and permissions now both required");
        }

        [Fact]
        public void Snapshot_ClonesAccess_SoLaterEditsDoNotLeakIntoHistory()
        {
            var proxy = new ProxyDetailEntity
            {
                TenantId = "t", Name = "Stripe", Slug = "stripe", Upstream = "https://api.stripe.com",
            };
            proxy.Access.Roles.Values.Add("admin");

            var snapshot = ProxyVersionFactory.SnapshotOf(proxy);
            proxy.Access.Roles.Values.Add("editor");

            snapshot.Access.Roles.Values.Should().Equal("admin");
        }
    }
}
