using Workflow.DomainService.Entities;
using Workflow.DomainService.Nodes;
using Workflow.DomainService.Utils;
using FluentAssertions;

namespace XUnitTest.Workflow
{
    public class AncestorMapMergerTests
    {
        [Fact]
        public void Merge_KeepsAgreedAncestors_AndDropsNamesThatDisagree()
        {
            var parents = new[]
            {
                Parent("sca-1", "Get SCA Report", new Dictionary<string, string>
                {
                    ["Webhook"] = "wh-1",
                    ["Load Repositories"] = "repos-1",
                    ["Run For Each Repositories"] = "each-1",
                }),
                Parent("sca-2", "Get SCA Report", new Dictionary<string, string>
                {
                    ["Webhook"] = "wh-1",
                    ["Load Repositories"] = "repos-1",
                    ["Run For Each Repositories"] = "each-2",
                }),
                Parent("sca-3", "Get SCA Report", new Dictionary<string, string>
                {
                    ["Webhook"] = "wh-1",
                    ["Load Repositories"] = "repos-1",
                    ["Run For Each Repositories"] = "each-3",
                }),
            };

            var merged = AncestorMapMerger.Merge(
                parents.Select(p => p.Id),
                parents,
                "prepare-1",
                "Prepare SCA Data");

            merged["Webhook"].Should().Be("wh-1");
            merged["Load Repositories"].Should().Be("repos-1");
            merged.Should().NotContainKey("Run For Each Repositories");
            merged.Should().NotContainKey("Get SCA Report");
            merged["Prepare SCA Data"].Should().Be("prepare-1");
        }

        [Fact]
        public void Merge_OmitsWebhook_WhenEachParentPointsAtADifferentItem()
        {
            var parents = new[]
            {
                Parent("mail-1", "Send Mail", new Dictionary<string, string> { ["Webhook"] = "wh-1" }),
                Parent("mail-2", "Send Mail", new Dictionary<string, string> { ["Webhook"] = "wh-2" }),
                Parent("mail-3", "Send Mail", new Dictionary<string, string> { ["Webhook"] = "wh-3" }),
            };

            var merged = AncestorMapMerger.Merge(
                parents.Select(p => p.Id),
                parents,
                "code-1",
                "Code");

            merged.Should().NotContainKey("Webhook");
            merged.Should().NotContainKey("Send Mail");
            merged["Code"].Should().Be("code-1");
        }

        [Fact]
        public void Merge_DoesNotMutateSourceMaps()
        {
            var source = new Dictionary<string, string> { ["Webhook"] = "wh-1" };
            var parent = Parent("mail-1", "Send Mail", source);

            AncestorMapMerger.Merge(new[] { parent.Id }, new[] { parent }, "code-1", "Code");

            parent.AncestorMap.Should().BeSameAs(source);
            source.Should().Equal(new Dictionary<string, string> { ["Webhook"] = "wh-1" });
            source.Should().NotContainKey("Send Mail");
        }

        [Fact]
        public void Merge_UsesAncestorCandidate_WhenParentIsNotADirectInput()
        {
            var direct = Parent("alert-1", "Get SCA Report", new Dictionary<string, string>());
            var ancestor = Parent("each-1", "Run For Each Repositories", new Dictionary<string, string>
            {
                ["Webhook"] = "wh-1",
                ["Get GitHub Token"] = "token-1",
            });

            var merged = AncestorMapMerger.Merge(
                new[] { "each-1" },
                new[] { direct, ancestor },
                "prepare-1",
                "Prepare SCA Data");

            merged["Webhook"].Should().Be("wh-1");
            merged["Get GitHub Token"].Should().Be("token-1");
            merged["Run For Each Repositories"].Should().Be("each-1");
            merged["Prepare SCA Data"].Should().Be("prepare-1");
            merged.Should().NotContainKey("Get SCA Report");
        }

        [Fact]
        public void Merge_IgnoresUnknownParents_AndEmptyParentsYieldOnlySelf()
        {
            var candidate = Parent("wh-1", "Webhook", new Dictionary<string, string>());

            var unknown = AncestorMapMerger.Merge(new[] { "missing" }, new[] { candidate }, "code-1", "Code");
            unknown.Should().Equal(new Dictionary<string, string> { ["Code"] = "code-1" });

            var none = AncestorMapMerger.Merge(null, new[] { candidate }, "code-1", "Code");
            none.Should().Equal(new Dictionary<string, string> { ["Code"] = "code-1" });

            var empty = AncestorMapMerger.Merge(Array.Empty<string>(), new[] { candidate }, "code-1", "Code");
            empty.Should().Equal(new Dictionary<string, string> { ["Code"] = "code-1" });
        }

        private static WorkflowItemExecutionEntity Parent(
            string id,
            string nodeName,
            Dictionary<string, string> ancestorMap)
            => new()
            {
                Id = id,
                WorkflowExecutionId = "exec-1",
                TenantId = "tenant-1",
                NodeId = "node-" + id,
                NodeExecutionId = "ne-" + id,
                NodeName = nodeName,
                Branch = "source",
                AncestorMap = ancestorMap,
                Data = new NodeOutputItemData(),
            };
    }
}
