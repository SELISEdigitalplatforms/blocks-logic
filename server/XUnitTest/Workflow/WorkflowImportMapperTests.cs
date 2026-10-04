using System.Text.Json;
using FluentAssertions;
using MongoDB.Bson;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Import;

namespace XUnitTest.Workflow
{
    public class WorkflowImportMapperTests
    {
        private static JsonElement Root(object graph)
        {
            var json = JsonSerializer.Serialize(graph);
            return WorkflowImportMapper.Preflight(json, json.Length).Root;
        }

        [Fact]
        public void Preflight_RejectsFilesOver5Mb()
        {
            var result = WorkflowImportMapper.Preflight("{}", WorkflowImportMapper.MaxImportBytes + 1);
            result.Ok.Should().BeFalse();
            result.Code.Should().Be(WorkflowImportMapper.ImportTooLarge);
        }

        [Fact]
        public void Preflight_RejectsInvalidJson()
        {
            var result = WorkflowImportMapper.Preflight("not json", 8);
            result.Ok.Should().BeFalse();
            result.Code.Should().Be(WorkflowImportMapper.ImportNotJson);
        }

        [Fact]
        public void Preflight_RejectsMissingName()
        {
            var json = JsonSerializer.Serialize(new { settings = new { }, nodes = Array.Empty<object>(), edges = Array.Empty<object>() });
            var result = WorkflowImportMapper.Preflight(json, json.Length);
            result.Ok.Should().BeFalse();
            result.Code.Should().Be(WorkflowImportMapper.ImportBadShape);
        }

        [Fact]
        public void ReplaceIdsInString_RewritesWholeTokensAndNodeRefs()
        {
            var map = new Dictionary<string, string> { ["8d75404dc42646619b91a7d85278687b"] = "NEWID" };
            WorkflowImportMapper.ReplaceIdsInString("8d75404dc42646619b91a7d85278687b", map).Should().Be("NEWID");
            WorkflowImportMapper.ReplaceIdsInString("node_8d75404dc42646619b91a7d85278687b_source", map)
                .Should().Be("node_NEWID_source");
            WorkflowImportMapper.ReplaceIdsInString("zz8d75404dc42646619b91a7d85278687bzz", map)
                .Should().Be("zz8d75404dc42646619b91a7d85278687bzz");
        }

        [Fact]
        public void RemapAndSanitise_RegeneratesNodeIdsAndWebhookPath()
        {
            var root = Root(new
            {
                name = "wf",
                settings = new { },
                nodes = new object[]
                {
                    new
                    {
                        id = "8d75404dc42646619b91a7d85278687b",
                        name = "Webhook",
                        type = "webhook",
                        category = "trigger",
                        version = "v1",
                        position = new { x = 0.0, y = 0.0 },
                        parameters = new { path = "8d75404dc42646619b91a7d85278687b" },
                        settings = new { },
                    },
                    new
                    {
                        id = "bcc3fabc012345678901234567890abc",
                        name = "HTTP",
                        type = "httpRequest",
                        category = "action",
                        version = "v1",
                        position = new { x = 200.0, y = 0.0 },
                        parameters = new { url = "={{ node_8d75404dc42646619b91a7d85278687b_source }}" },
                        settings = new { },
                    },
                },
                edges = new object[]
                {
                    new
                    {
                        id = "xy-edge__8d75404dc42646619b91a7d85278687b-bcc3fabc012345678901234567890abc",
                        source = "8d75404dc42646619b91a7d85278687b",
                        target = "bcc3fabc012345678901234567890abc",
                        sourceHandle = "source",
                        targetHandle = "target",
                    },
                },
            });

            var outGraph = WorkflowImportMapper.RemapAndSanitise(root);

            outGraph.Issues.Should().Be(0);
            outGraph.Nodes.Should().HaveCount(2);
            outGraph.Nodes[0].Id.Should().MatchRegex("^[0-9a-f]{32}$");
            outGraph.Nodes[0].Id.Should().NotBe("8d75404dc42646619b91a7d85278687b");
            outGraph.Nodes[0].Parameters["path"].AsString.Should().Be(outGraph.Nodes[0].Id);
            outGraph.Nodes[1].Parameters["url"].AsString.Should().Be($"={{{{ node_{outGraph.Nodes[0].Id}_source }}}}");
            outGraph.Edges.Should().ContainSingle();
            outGraph.Edges[0].Source.Should().Be(outGraph.Nodes[0].Id);
            outGraph.Edges[0].Target.Should().Be(outGraph.Nodes[1].Id);
            outGraph.Edges[0].Id.Should().Be($"xy-edge__{outGraph.Nodes[0].Id}-{outGraph.Nodes[1].Id}");
        }

        [Fact]
        public void RemapAndSanitise_DropsMalformedNodesAndDanglingEdges()
        {
            var json = """
                {"name":"wf","settings":{},"nodes":[
                  {"id":"11111111111111111111111111111111","name":"A","type":"webhook","category":"trigger","version":"v1","position":{"x":1,"y":2}},
                  {"id":"x","name":"bad"},
                  {"id":"11111111111111111111111111111111","name":"dup","type":"webhook","category":"trigger","version":"v1","position":{"x":1,"y":2}}
                ],"edges":[
                  {"source":"11111111111111111111111111111111","target":"missing","sourceHandle":"s","targetHandle":"t"}
                ]}
                """;
            var preflight = WorkflowImportMapper.Preflight(json, json.Length);
            preflight.Ok.Should().BeTrue();
            var outGraph = WorkflowImportMapper.RemapAndSanitise(preflight.Root);
            outGraph.Nodes.Should().ContainSingle();
            outGraph.Edges.Should().BeEmpty();
            outGraph.Issues.Should().Be(3);
        }

        [Fact]
        public void RewriteProjectIdentity_SetsProjectKeyAndSendMailTemplate()
        {
            var dataAction = new NodeEntity
            {
                Id = "n1",
                Name = "Data",
                Category = "action",
                Type = "dataAction",
                Version = "v1",
                Position = new Position { X = 0, Y = 0 },
                Parameters = new BsonDocument { { "projectShortKey", "old-slug" } },
            };
            var sendMail = new NodeEntity
            {
                Id = "n2",
                Name = "Mail",
                Category = "action",
                Type = "sendMail",
                Version = "v1",
                Position = new Position { X = 0, Y = 0 },
                Parameters = new BsonDocument { { "Template", "Welcome" }, { "EmailTemplate", "Welcome_oldTenant" } },
            };

            WorkflowImportMapper.RewriteProjectIdentity([dataAction, sendMail], "dest-tenant", "dslug");

            dataAction.Parameters["projectKey"].AsString.Should().Be("dest-tenant");
            dataAction.Parameters["projectShortKey"].AsString.Should().Be("dslug");
            sendMail.Parameters["projectKey"].AsString.Should().Be("dest-tenant");
            sendMail.Parameters["EmailTemplate"].AsString.Should().Be("Welcome_dest-tenant");
        }
    }
}
