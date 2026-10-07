using System.Text.Json;
using Workflow.DomainService.Dtos;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Utils;
using FluentAssertions;
using MongoDB.Bson;

namespace XUnitTest.Workflow
{
    public class NodeMapperTests
    {
        private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

        private static NodeDto Node(JsonElement parameters = default, JsonElement settings = default, JsonElement? pinData = null) => new()
        {
            Id = "node-1",
            Name = "Node",
            Category = "action",
            Type = "transformCode",
            Version = "1",
            Position = new Position { X = 1, Y = 2 },
            Handle = new Handle(),
            Parameters = parameters,
            Settings = settings,
            PinData = pinData,
        };

        [Fact]
        public void ToEntity_CopiesScalarFields()
        {
            var dto = Node(Json("{}"), Json("{}"));

            var entity = NodeMapper.ToEntity(dto);

            entity.Id.Should().Be("node-1");
            entity.Name.Should().Be("Node");
            entity.Category.Should().Be("action");
            entity.Type.Should().Be("transformCode");
            entity.Version.Should().Be("1");
            entity.Position.Should().BeSameAs(dto.Position);
            entity.Handle.Should().BeSameAs(dto.Handle);
        }

        [Fact]
        public void ToEntity_MapsNestedObjectParameters()
        {
            var json = "{\"s\":\"v\",\"i\":42,\"d\":1.5,\"b\":true,\"z\":null,\"list\":[1,\"two\"],\"nested\":{\"a\":{\"b\":\"c\"}}}";

            var entity = NodeMapper.ToEntity(Node(Json(json), Json("{\"retry\":3}")));

            entity.Parameters.Equals(BsonDocument.Parse(json)).Should().BeTrue();
            entity.Parameters["nested"]["a"]["b"].AsString.Should().Be("c");
            entity.Parameters["z"].IsBsonNull.Should().BeTrue();
            entity.Settings["retry"].AsInt32.Should().Be(3);
        }

        [Fact]
        public void ToEntity_MissingParametersAndSettings_BecomeEmptyDocuments()
        {
            var entity = NodeMapper.ToEntity(Node());

            entity.Parameters.Should().BeEmpty();
            entity.Settings.Should().BeEmpty();
        }

        [Fact]
        public void ToEntity_NullParametersAndSettings_BecomeEmptyDocuments()
        {
            var entity = NodeMapper.ToEntity(Node(Json("null"), Json("null")));

            entity.Parameters.Should().BeEmpty();
            entity.Settings.Should().BeEmpty();
        }

        [Theory]
        [InlineData("[1,2]")]
        [InlineData("\"text\"")]
        [InlineData("5")]
        public void ToEntity_NonObjectParameters_Throws(string json)
        {
            var act = () => NodeMapper.ToEntity(Node(Json(json)));

            act.Should().Throw<NodeMappingException>()
                .Which.Message.Should().Contain("node-1").And.Contain("parameters");
        }

        [Fact]
        public void ToEntity_NonObjectSettings_Throws()
        {
            var act = () => NodeMapper.ToEntity(Node(Json("{}"), Json("[]")));

            act.Should().Throw<NodeMappingException>()
                .Which.Message.Should().Contain("settings");
        }

        [Fact]
        public void ToEntity_DuplicateKeys_ThrowsMappingException()
        {
            var act = () => NodeMapper.ToEntity(Node(Json("{\"a\":1,\"a\":2}")));

            act.Should().Throw<NodeMappingException>()
                .Which.InnerException.Should().NotBeNull();
        }

        [Fact]
        public void ToEntity_ArrayPinData_BecomesBsonArray()
        {
            var entity = NodeMapper.ToEntity(Node(pinData: Json("[{\"x\":1},{\"x\":2}]")));

            entity.PinData.Should().NotBeNull();
            entity.PinData!.Count.Should().Be(2);
            entity.PinData[1]["x"].AsInt32.Should().Be(2);
        }

        [Fact]
        public void ToEntity_MissingOrNullPinData_StaysNull()
        {
            NodeMapper.ToEntity(Node()).PinData.Should().BeNull();
            NodeMapper.ToEntity(Node(pinData: Json("null"))).PinData.Should().BeNull();
        }

        [Fact]
        public void ToEntities_Null_ReturnsEmptyList()
        {
            NodeMapper.ToEntities(null).Should().BeEmpty();
        }
    }
}
