using System.Runtime.CompilerServices;
using FluentAssertions;
using Functions.DomainService.Nodes;
using Workflow.DomainService.Nodes;
using Workflow.DomainService.Nodes.LogicIFV1;

namespace XUnitTest.Workflow
{
    /// <summary>SPEC4: executors are picked by (type, version); an unregistered version falls back to v1.</summary>
    public class NodeExecutorRegistryTests
    {
        private sealed class FakeExecutor(string type, string version, params string[] versions) : INodeExecutor
        {
            public string NodeType { get; } = type;
            public string Version { get; } = version;
            public IReadOnlyCollection<string> Versions { get; } = versions.Length == 0 ? new[] { "v1", "v2" } : versions;
            public Task<NodeExecutionResult> RunAsync(NodeExecutionContext context) => Task.FromResult(NodeExecutionResult.Empty());
        }

        /// <summary>Types added after SPEC6: they start at v2 and have no v1.</summary>
        private static bool IsV2Only(INodeExecutor e) =>
            e.NodeType is "switch" or "splitOut" or "merge" or "filter" or "blocksEvent" or "limit" or "notification";

        /// <summary>The 13 node types that existed before SPEC6; they must keep resolving exactly as before.</summary>
        private static List<INodeExecutor> ProductionExecutors() =>
            AllProductionExecutors().Where(e => !IsV2Only(e)).ToList();

        /// <summary>Every production executor class, read without running its constructor (NodeType/Versions need no state).</summary>
        private static List<INodeExecutor> AllProductionExecutors() =>
            new[] { typeof(LogicIfV1Node).Assembly, typeof(ActionFunctionNode).Assembly }
                .SelectMany(a => a.GetTypes())
                .Where(t => typeof(INodeExecutor).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false })
                .Select(t => (INodeExecutor)RuntimeHelpers.GetUninitializedObject(t))
                .ToList();

        [Fact]
        public void ProductionExecutors_RegisterV1AndV2_ForAll13Types()
        {
            var executors = ProductionExecutors();
            var registry = new NodeExecutorRegistry(executors);

            executors.Select(e => e.NodeType).Should().BeEquivalentTo(
                "webhook", "email", "dataGateway", "schedule", "if", "setfield", "code",
                "agent", "sendMail", "httpRequest", "dataAction", "proxy", "function");
            foreach (var executor in executors)
            {
                registry.Resolve(executor.NodeType, "v1").Should().Be(new NodeExecutorResolution(executor, false));
                registry.Resolve(executor.NodeType, "v2").Should().Be(new NodeExecutorResolution(executor, false));
            }
        }

        /// <summary>
        /// Behaviour preservation: for every production type and any stored version string, the registry picks
        /// exactly the executor the old lookup picked (<c>executors.First(e => e.NodeType == node.Type)</c>).
        /// </summary>
        [Fact]
        public void Resolve_PicksTheSameExecutorAsTheOldLookup_ForEveryTypeAndVersion()
        {
            var executors = ProductionExecutors();
            var registry = new NodeExecutorRegistry(executors);
            string?[] storedVersions = ["v1", "v2", "1", "1.0", "V1", "v3", "", " ", null, "anything"];

            foreach (var type in executors.Select(e => e.NodeType))
            {
                var old = executors.First(e => e.NodeType == type);
                foreach (var version in storedVersions)
                {
                    registry.Resolve(type, version).Executor.Should().BeSameAs(old, $"type {type}, version '{version}'");
                }
            }
        }

        [Fact]
        public void Resolve_UnknownType_ThrowsTheSameExceptionTypeAsTheOldLookup()
        {
            var executors = ProductionExecutors();
            var oldLookup = () => executors.First(e => e.NodeType == "manual");
            var newLookup = () => new NodeExecutorRegistry(executors).Resolve("manual", "v1");

            // Both are InvalidOperationException, so the engine records the same errorKind and the user sees
            // the same generic UnexpectedErrorMessage (UserFacingError); only the server-side log text differs.
            oldLookup.Should().Throw<InvalidOperationException>();
            newLookup.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void Switch_ResolvesOnlyAsV2_AndNeverFallsBack()
        {
            var all = AllProductionExecutors();
            var registry = new NodeExecutorRegistry(all);
            var @switch = all.Single(e => e.NodeType == "switch");

            registry.Resolve("switch", "v2").Should().Be(new NodeExecutorResolution(@switch, false));
            foreach (var version in new[] { "v1", "v3", "", null })
            {
                var act = () => registry.Resolve("switch", version);
                act.Should().Throw<InvalidOperationException>($"version '{version}'");
            }
            foreach (var executor in all.Where(e => !IsV2Only(e)))
            {
                registry.Resolve(executor.NodeType, "v1").Executor.Should().BeSameAs(executor);
            }
        }

        /// <summary>SPEC9 C10: a server built before Filter (no Filter executor) fails the step instead of running another node.</summary>
        [Fact]
        public void Filter_OnAServerWithoutTheFilterExecutor_HasNoExecutor()
        {
            var registry = new NodeExecutorRegistry(AllProductionExecutors().Where(e => e.NodeType != "filter"));

            var act = () => registry.Resolve("filter", "v2");

            act.Should().Throw<InvalidOperationException>().WithMessage("No executor for node type.");
        }

        [Fact]
        public void Notification_ResolvesOnlyAsV2_AndNeverFallsBack()
        {
            var all = AllProductionExecutors();
            var registry = new NodeExecutorRegistry(all);
            var notification = all.Single(e => e.NodeType == "notification");

            registry.Resolve("notification", "v2").Should().Be(new NodeExecutorResolution(notification, false));
            var act = () => registry.Resolve("notification", "v1");
            act.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void ProductionExecutors_DeclareVersionV1()
        {
            ProductionExecutors().Should().OnlyContain(e => e.Version == "v1");
        }

        [Fact]
        public void Resolve_V1AndV2_ReturnTheSameExecutor()
        {
            var ifNode = new FakeExecutor("if", "v1");
            var registry = new NodeExecutorRegistry([ifNode, new FakeExecutor("code", "v1")]);

            registry.Resolve("if", "v1").Executor.Should().BeSameAs(ifNode);
            registry.Resolve("if", "v2").Executor.Should().BeSameAs(ifNode);
        }

        [Theory]
        [InlineData("9.9")]
        [InlineData("1.0")]
        [InlineData("v3")]
        [InlineData("")]
        [InlineData(null)]
        public void Resolve_UnregisteredVersion_FallsBackToV1(string? version)
        {
            var code = new FakeExecutor("code", "v1");
            var registry = new NodeExecutorRegistry([code]);

            var resolution = registry.Resolve("code", version);

            resolution.Executor.Should().BeSameAs(code);
            resolution.UsedFallback.Should().BeTrue();
        }

        [Fact]
        public void Resolve_PrefersAnExactVersionOverTheFallback()
        {
            var v1 = new FakeExecutor("switch", "v1", "v1");
            var v2 = new FakeExecutor("switch", "v2", "v2");
            var registry = new NodeExecutorRegistry([v1, v2]);

            registry.Resolve("switch", "v2").Should().Be(new NodeExecutorResolution(v2, false));
            registry.Resolve("switch", "v7").Should().Be(new NodeExecutorResolution(v1, true));
        }

        [Fact]
        public void Resolve_UnknownType_Throws()
        {
            var registry = new NodeExecutorRegistry([new FakeExecutor("code", "v1")]);

            var act = () => registry.Resolve("unknownType", "v1");

            act.Should().Throw<InvalidOperationException>().WithMessage("No executor for node type.");
        }

        [Fact]
        public void Resolve_TypeWithoutV1_DoesNotFallBack()
        {
            var registry = new NodeExecutorRegistry([new FakeExecutor("switch", "v2", "v2")]);

            var act = () => registry.Resolve("switch", "v1");

            act.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void DuplicateTypeAndVersion_FailsWithTheDuplicateMessage()
        {
            var act = () => new NodeExecutorRegistry([new FakeExecutor("if", "v1"), new FakeExecutor("if", "v1", "v2")]);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("Duplicate node executor for type 'if' version 'v2'.");
        }

        [Fact]
        public void FallbackLogMessage_CarriesNoValues()
        {
            NodeExecutorRegistry.FallbackLogMessage.Should().NotContain("{");
        }
    }
}
