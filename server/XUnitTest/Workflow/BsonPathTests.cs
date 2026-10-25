using FluentAssertions;
using MongoDB.Bson;
using Workflow.DomainService.Utils;

namespace XUnitTest.Workflow
{
    public class BsonPathTests
    {
        private static BsonDocument Doc() => BsonDocument.Parse("{ \"a\": { \"b\": { \"c\": 1 } }, \"a.b\": 2, \"list\": [1] }");

        [Theory]
        [InlineData("a.b.c", true, "1")]
        [InlineData("a.b", false, "2")]
        [InlineData("list", true, "[1]")]
        public void TryGet_FindsValues(string path, bool dot, string expected)
        {
            BsonPath.TryGet(Doc(), path, dot, out var value).Should().BeTrue();
            value.ToJson().Should().Be(expected);
        }

        [Theory]
        [InlineData("a.x", true)]
        [InlineData("a.b.c.d", true)]
        [InlineData("list.0", true)]
        [InlineData("a.b.c", false)]
        public void TryGet_MissingPaths(string path, bool dot)
        {
            BsonPath.TryGet(Doc(), path, dot, out _).Should().BeFalse();
        }

        [Fact]
        public void Set_CreatesTheDocumentsAlongThePath_AndKeepsSiblings()
        {
            var doc = Doc();
            BsonPath.Set(doc, "a.b.d", dotNotation: true, 5);
            BsonPath.Set(doc, "x.y", dotNotation: true, "new");
            BsonPath.Set(doc, "list.k", dotNotation: true, 1);

            doc["a"]["b"].ToJson().Should().Be("{ \"c\" : 1, \"d\" : 5 }");
            doc["x"].ToJson().Should().Be("{ \"y\" : \"new\" }");
            doc["list"].ToJson().Should().Be("{ \"k\" : 1 }");
        }

        [Fact]
        public void Set_WithoutDotNotation_WritesOneLiteralKey()
        {
            var doc = new BsonDocument();
            BsonPath.Set(doc, "a.b", dotNotation: false, 1);

            doc.ToJson().Should().Be("{ \"a.b\" : 1 }");
        }

        [Fact]
        public void Remove_WithAndWithoutDotNotation()
        {
            var doc = Doc();
            BsonPath.Remove(doc, "a.b.c", dotNotation: true).Should().BeTrue();
            doc["a"]["b"].AsBsonDocument.ElementCount.Should().Be(0);
            BsonPath.Remove(doc, "a.b", dotNotation: false).Should().BeTrue();
            doc.Contains("a.b").Should().BeFalse();
            BsonPath.Remove(doc, "zz.y", dotNotation: true).Should().BeFalse();
            BsonPath.Remove(doc, "zz", dotNotation: false).Should().BeFalse();
        }
    }
}
