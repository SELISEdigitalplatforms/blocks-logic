using System.Text.Json;
using Blocks.Genesis.Entities.Logic;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Workflow.DomainService.Dtos;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// SPEC4 rollout safety: this phase writes only null or empty handles, which every reader accepts,
    /// and it already reads the {key, label} entries that Phase 2 (SPEC5) writes.
    /// </summary>
    public class HandleShapeCompatibilityTests
    {
        /// <summary>The node and handle shape before Genesis 4.3.7 (stg code): List&lt;string&gt; fields, no [BsonIgnoreExtraElements].</summary>
        private sealed class OldNode
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string Category { get; set; } = "";
            public string Type { get; set; } = "";
            public string Version { get; set; } = "";
            public OldPosition Position { get; set; } = new();
            public OldHandle? Handle { get; set; }
            public BsonDocument Parameters { get; set; } = new();
            public BsonDocument Settings { get; set; } = new();
            public BsonArray? PinData { get; set; }
        }

        private sealed class OldPosition
        {
            public double X { get; set; }
            public double Y { get; set; }
        }

        private sealed class OldHandle
        {
            public List<string> Sources = new();
            public List<string> Targets = new();
        }

        private static NodeEntity Node(Handle? handle) => new()
        {
            Id = "n1",
            Name = "If",
            Category = "logic",
            Type = "if",
            Version = "v1",
            Position = new Position { X = 1, Y = 2 },
            Handle = handle,
        };

        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

        [Fact]
        public void OldReader_ReadsANullHandleWrittenByThisPhase()
        {
            var bytes = Node(null).ToBson();

            var old = BsonSerializer.Deserialize<OldNode>(bytes);

            old.Handle.Should().BeNull();
            old.Type.Should().Be("if");
        }

        [Fact]
        public void OldReader_ReadsAnEmptyHandle()
        {
            var bytes = Node(new Handle()).ToBson();

            var old = BsonSerializer.Deserialize<OldNode>(bytes);

            old.Handle!.Sources.Should().BeEmpty();
            old.Handle.Targets.Should().BeEmpty();
        }

        [Fact]
        public void ThisPhase_ReadsThePhase2Shape()
        {
            var phase2 = Node(new Handle
            {
                Sources = [new() { ["key"] = "if-true", ["label"] = "True" }, new() { ["key"] = "if-false", ["label"] = "False" }],
                Targets = [new() { ["key"] = "target", ["label"] = "" }],
            });

            var read = BsonSerializer.Deserialize<NodeEntity>(phase2.ToBson());

            read.Handle!.Sources.Select(e => e["key"]).Should().Equal("if-true", "if-false");
            read.Handle.Sources[0]["label"].Should().Be("True");
            read.Handle.Targets.Single()["key"].Should().Be("target");
        }

        [Fact]
        public void ApiBinding_MapShapedHandle_Binds()
        {
            var dto = JsonSerializer.Deserialize<NodeDto>(
                """{"id":"n1","name":"If","category":"logic","type":"if","version":"v2","position":{"x":0,"y":0},"handle":{"sources":[{"key":"if-true","label":"True"}],"targets":[]}}""",
                Web)!;

            dto.Handle!.Sources.Single()["key"].Should().Be("if-true");
        }

        [Fact]
        public void ApiBinding_StringEntries_AreRejected()
        {
            var act = () => JsonSerializer.Deserialize<NodeDto>(
                """{"id":"n1","name":"If","category":"logic","type":"if","version":"v1","position":{"x":0,"y":0},"handle":{"sources":["x"],"targets":[]}}""",
                Web);

            // ASP.NET model binding turns this JsonException into HTTP 400 before the action runs.
            act.Should().Throw<JsonException>();
        }
    }
}
