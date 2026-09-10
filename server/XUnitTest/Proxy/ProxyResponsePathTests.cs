using FluentAssertions;
using Proxy.DomainService.Utils;

namespace XUnitTest.Proxy
{
    /// <summary>
    /// Covers <see cref="ProxyResponsePath"/>: the grammar accept / reject table, segment splitting incl. the
    /// <c>[]</c> capture, and the length / segment-count caps.
    /// </summary>
    public class ProxyResponsePathTests
    {
        [Theory]
        [InlineData("data")]
        [InlineData("data.user.email")]
        [InlineData("items[].id")]
        [InlineData("items.id")]
        [InlineData("a[].b[].c")]
        [InlineData("field name with spaces")]
        [InlineData("nested.field name.value")]
        public void IsValid_AcceptsGrammar(string path) => ProxyResponsePath.IsValid(path).Should().BeTrue();

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(".")]
        [InlineData("a.")]
        [InlineData(".a")]
        [InlineData("a..b")]
        [InlineData("items[0]")]
        [InlineData("items[*]")]
        [InlineData("a..b.c")]
        [InlineData("a[].[].b")]
        [InlineData("a[]extra")]
        [InlineData("a.b[")]
        [InlineData("a].b")]
        public void IsValid_RejectsGrammar(string? path) => ProxyResponsePath.IsValid(path).Should().BeFalse();

        [Fact]
        public void IsValid_RejectsOverLength()
        {
            var path = new string('a', ProxyResponsePath.MaxPathLength + 1);
            ProxyResponsePath.IsValid(path).Should().BeFalse();
        }

        [Fact]
        public void IsValid_AcceptsExactlyMaxSegments()
        {
            var path = string.Join('.', Enumerable.Repeat("a", ProxyResponsePath.MaxSegments));
            ProxyResponsePath.IsValid(path).Should().BeTrue();
        }

        [Fact]
        public void IsValid_RejectsOverMaxSegments()
        {
            var path = string.Join('.', Enumerable.Repeat("a", ProxyResponsePath.MaxSegments + 1));
            ProxyResponsePath.IsValid(path).Should().BeFalse();
        }

        [Fact]
        public void Parse_SplitsSegmentsAndCapturesArrayMarker()
        {
            var segments = ProxyResponsePath.Parse("items[].user.tags[]");

            segments.Should().HaveCount(3);
            segments[0].Should().Be(new ProxyResponsePath.Segment("items", true));
            segments[1].Should().Be(new ProxyResponsePath.Segment("user", false));
            segments[2].Should().Be(new ProxyResponsePath.Segment("tags", true));
        }

        [Fact]
        public void Parse_SingleSegment()
        {
            ProxyResponsePath.Parse("data").Should().ContainSingle()
                .Which.Should().Be(new ProxyResponsePath.Segment("data", false));
        }
    }
}
