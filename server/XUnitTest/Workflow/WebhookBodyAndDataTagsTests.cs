using System.Text.Json;
using FluentAssertions;
using MongoDB.Bson;
using Workflow.DomainService.Services;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// Webhook body shape (a primitive used to throw inside BsonDocument.Parse → 500; now a 400 message)
    /// and the data-trigger "mock-data" Tags check (a non-array Tags used to throw and drop the event).
    /// </summary>
    public class WebhookBodyAndDataTagsTests
    {
        private static JsonElement Json(string raw)
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.Clone();
        }

        // ---------- webhook body ----------

        [Fact]
        public void Object_body_is_one_item()
        {
            var items = WebhookBodyNormalizer.Normalize(Json("""{"a":1,"b":{"c":"x"}}"""));

            items.Should().ContainSingle();
            items[0]["a"].ToInt32().Should().Be(1);
            items[0]["b"]["c"].AsString.Should().Be("x");
        }

        [Fact]
        public void Array_of_objects_is_one_item_each()
        {
            var items = WebhookBodyNormalizer.Normalize(Json("""[{"a":1},{"a":2}]"""));

            items.Should().HaveCount(2);
            items[1]["a"].ToInt32().Should().Be(2);
        }

        [Fact]
        public void Empty_array_is_no_items()
        {
            WebhookBodyNormalizer.Normalize(Json("[]")).Should().BeEmpty();
        }

        [Theory]
        [InlineData("42", "a number")]
        [InlineData("\"text\"", "a string")]
        [InlineData("true", "a boolean")]
        [InlineData("null", "null")]
        public void Primitive_body_is_refused(string raw, string kind)
        {
            var act = () => WebhookBodyNormalizer.Normalize(Json(raw));

            act.Should().Throw<InvalidWebhookBodyException>()
                .Which.Message.Should().StartWith(WebhookBodyNormalizer.ShapeMessage).And.Contain(kind);
        }

        [Theory]
        [InlineData("""[{"a":1}, 5]""", "element 1 is a number")]
        [InlineData("""["x"]""", "element 0 is a string")]
        [InlineData("""[null]""", "element 0 is null")]
        [InlineData("""[[{"a":1}]]""", "element 0 is an array")]
        public void Array_with_non_object_entry_is_refused(string raw, string detail)
        {
            var act = () => WebhookBodyNormalizer.Normalize(Json(raw));

            act.Should().Throw<InvalidWebhookBodyException>()
                .Which.Message.Should().Contain(detail);
        }

        [Fact]
        public void Undefined_element_is_refused()
        {
            var act = () => WebhookBodyNormalizer.Normalize(default);

            act.Should().Throw<InvalidWebhookBodyException>();
        }

        [Fact]
        public void Object_that_Mongo_cannot_read_is_refused_not_500()
        {
            // Extended-JSON key with a bad value: BsonDocument.Parse throws on it.
            var act = () => WebhookBodyNormalizer.Normalize(Json("""{"$date":"not-a-date"}"""));

            act.Should().Throw<InvalidWebhookBodyException>()
                .Which.Message.Should().Contain("could not be read as an object");
        }

        // ---------- data trigger Tags ----------

        [Fact]
        public void Mock_data_when_every_item_has_tags_array_with_mock_data()
        {
            var data = new BsonArray
            {
                new BsonDocument { { "Tags", new BsonArray { "mock-data" } } },
                new BsonDocument { { "Tags", new BsonArray { "x", "mock-data" } } },
            };

            WorkflowExecutionService.IsMockedData(data).Should().BeTrue();
        }

        [Fact]
        public void Not_mock_data_when_one_item_lacks_the_tag()
        {
            var data = new BsonArray
            {
                new BsonDocument { { "Tags", new BsonArray { "mock-data" } } },
                new BsonDocument { { "Tags", new BsonArray { "other" } } },
            };

            WorkflowExecutionService.IsMockedData(data).Should().BeFalse();
        }

        public static TheoryData<BsonValue> NonArrayTags => new()
        {
            "mock-data",
            42,
            BsonNull.Value,
            new BsonDocument { { "0", "mock-data" } },
            true,
        };

        [Theory]
        [MemberData(nameof(NonArrayTags))]
        public void Non_array_tags_is_not_mock_data_and_does_not_throw(BsonValue tags)
        {
            var data = new BsonArray { new BsonDocument { { "Tags", tags } } };

            var act = () => WorkflowExecutionService.IsMockedData(data);

            act.Should().NotThrow();
            WorkflowExecutionService.IsMockedData(data).Should().BeFalse();
        }

        [Fact]
        public void Missing_tags_or_no_items_is_not_mock_data()
        {
            WorkflowExecutionService.IsMockedData(new BsonArray { new BsonDocument { { "a", 1 } } }).Should().BeFalse();
            WorkflowExecutionService.IsMockedData(new BsonArray()).Should().BeFalse();
        }
    }
}
