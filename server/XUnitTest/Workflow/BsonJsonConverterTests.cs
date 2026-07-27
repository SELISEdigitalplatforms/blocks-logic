using System.Text.Json;
using DomainService.Workflow.Utils;
using FluentAssertions;
using MongoDB.Bson;

namespace XUnitTest.Workflow
{
    public class BsonJsonConverterTests
    {
        [Fact]
        public void ToJsonElement_ConvertsDocument()
        {
            var doc = new BsonDocument { { "name", "abc" }, { "count", 3 } };

            var element = BsonJsonConverter.ToJsonElement(doc);

            element.GetProperty("name").GetString().Should().Be("abc");
            element.GetProperty("count").GetInt32().Should().Be(3);
        }

        [Fact]
        public void ToJsonElementOrNull_WithBsonNull_ReturnsNull()
        {
            BsonJsonConverter.ToJsonElementOrNull(BsonNull.Value).Should().BeNull();
            BsonJsonConverter.ToJsonElementOrNull(null).Should().BeNull();
        }

        [Fact]
        public void ToJsonElementOrNull_WithValue_ReturnsElement()
        {
            var doc = new BsonDocument { { "x", 1 } };

            var element = BsonJsonConverter.ToJsonElementOrNull(doc);

            element.Should().NotBeNull();
            element!.Value.GetProperty("x").GetInt32().Should().Be(1);
        }

        [Fact]
        public void ToBsonValue_RoundTripsObject()
        {
            using var doc = JsonDocument.Parse("{\"a\":\"b\",\"n\":5}");

            var bson = BsonJsonConverter.ToBsonValue(doc.RootElement);

            bson.AsBsonDocument["a"].AsString.Should().Be("b");
            bson.AsBsonDocument["n"].AsInt32.Should().Be(5);
        }

        [Fact]
        public void ToBsonArrayOrNull_WithArray_ReturnsArray()
        {
            using var doc = JsonDocument.Parse("[1,2,3]");

            var arr = BsonJsonConverter.ToBsonArrayOrNull(doc.RootElement);

            arr.Should().NotBeNull();
            arr!.Count.Should().Be(3);
        }

        [Fact]
        public void ToBsonArrayOrNull_WithNullOrNonArray_ReturnsNull()
        {
            BsonJsonConverter.ToBsonArrayOrNull(null).Should().BeNull();

            using var nullDoc = JsonDocument.Parse("null");
            BsonJsonConverter.ToBsonArrayOrNull(nullDoc.RootElement).Should().BeNull();

            using var objDoc = JsonDocument.Parse("{\"a\":1}");
            BsonJsonConverter.ToBsonArrayOrNull(objDoc.RootElement).Should().BeNull();
        }
    }
}
