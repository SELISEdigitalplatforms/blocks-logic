using DomainService.Notification.Push;
using FluentAssertions;

namespace XUnitTest.Notification.Push
{
    /// <summary>Push text rules: top-level fields only, values 100, title 65, body 240, missing → empty, language fallback.</summary>
    public class PushTextRendererTests
    {
        [Fact]
        public void Fields_AreTopLevelScalarsOnly_AndCutTo100()
        {
            var fields = PushTextRenderer.FieldsOf($$"""{"name":"{{new string('a', 150)}}","count":3,"ok":true,"nested":{"x":"y"},"list":[1],"nothing":null}""");

            fields.Keys.Should().BeEquivalentTo("name", "count", "ok");
            fields["name"].Should().HaveLength(PushTextRenderer.MaxValueLength);
            fields["count"].Should().Be("3");
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("[1,2]")]
        [InlineData(null)]
        public void Fields_OfAPayloadThatIsNotAnObject_AreEmpty(string? payload)
        {
            PushTextRenderer.FieldsOf(payload).Should().BeEmpty();
        }

        [Fact]
        public void Fill_ReplacesKnownFields_MissingOnesBecomeEmpty_AndUnclosedTextStays()
        {
            var fields = new Dictionary<string, string> { ["name"] = "Ada" };

            PushTextRenderer.Fill("Hi {{ name }}, {{missing}}done {{open", fields, 100)
                .Should().Be("Hi Ada, done {{open");
        }

        [Fact]
        public void Fill_CutsTitleAndBodyToTheirLimits()
        {
            var fields = new Dictionary<string, string> { ["v"] = new string('x', 100) };

            PushTextRenderer.Fill("{{v}}{{v}}{{v}}", fields, PushTextRenderer.MaxTitleLength).Should().HaveLength(65);
            PushTextRenderer.Fill("{{v}}{{v}}{{v}}", fields, PushTextRenderer.MaxBodyLength).Should().HaveLength(240);
        }

        [Fact]
        public void Fill_ForALink_EscapesValues_SoAFieldCannotChangeTheUrl()
        {
            var fields = new Dictionary<string, string> { ["id"] = "a/../b?x=1" };

            PushTextRenderer.Fill("app://orders/{{id}}", fields, PushTextRenderer.MaxLinkLength, escapeValues: true)
                .Should().Be("app://orders/a%2F..%2Fb%3Fx%3D1");
        }

        [Fact]
        public void Pick_UsesTheUsersLanguage_ThenTheSameLanguage_ThenTheDefault_ThenTheKey()
        {
            var cultures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["de-DE"] = "Hallo",
                ["en-US"] = "Hello",
            };

            PushTextRenderer.Pick(cultures, "de-DE", "en-US", "key").Should().Be("Hallo");
            PushTextRenderer.Pick(cultures, "de-AT", "en-US", "key").Should().Be("Hallo");
            PushTextRenderer.Pick(cultures, "fr-FR", "en-US", "key").Should().Be("Hello");
            PushTextRenderer.Pick(cultures, "fr-FR", "it-IT", "key").Should().Be("key");
            PushTextRenderer.Pick(null, "de-DE", "en-US", "key").Should().Be("key");
        }
    }
}
