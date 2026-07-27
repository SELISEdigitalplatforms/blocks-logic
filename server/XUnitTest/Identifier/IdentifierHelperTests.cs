using DomainService.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace XUnitTest.Identifier
{
    public class IdentifierHelperTests
    {
        [Theory]
        [InlineData("https://example.com", true)]
        [InlineData("http://example.com", true)]
        [InlineData("ftp://example.com", false)]
        [InlineData("example.com", false)]
        [InlineData("not a url", false)]
        [InlineData("", false)]
        public void BeAValidUrl_ReturnsExpected(string url, bool expected)
        {
            IdentifierHelper.BeAValidUrl(url).Should().Be(expected);
        }

        [Theory]
        [InlineData("https://dev.example.com", "example.com")]
        [InlineData("http://example.com", "example.com")]
        [InlineData("a.b.c.example.com", "example.com")]
        [InlineData("localhost", "localhost")]
        [InlineData("", "")]
        [InlineData("   ", "")]
        public void ExtractMainDomain_ReturnsExpected(string domain, string expected)
        {
            IdentifierHelper.ExtractMainDomain(domain).Should().Be(expected);
        }

        [Theory]
        [InlineData("dev", "d")]
        [InlineData("test", "t")]
        [InlineData("stg", "s")]
        [InlineData("iat", "i")]
        [InlineData("uat", "u")]
        [InlineData("prod-shadow", "h")]
        [InlineData("pre-prod", "r")]
        [InlineData("prod", "p")]
        [InlineData("unknown", "n")]
        public void EnvironmentMapper_ReturnsExpected(string env, string expected)
        {
            IdentifierHelper.EnvironmentMapper(env).Should().Be(expected);
        }

        [Theory]
        [InlineData("dev", true)]
        [InlineData("prod", true)]
        [InlineData("pre-prod", true)]
        [InlineData("staging", false)]
        [InlineData("", false)]
        public void IsSupportedEnvironment_ReturnsExpected(string env, bool expected)
        {
            IdentifierHelper.IsSupportedEnvironment(env).Should().Be(expected);
        }

        [Theory]
        [InlineData("amqp://guest:guest@localhost:5672", true)]
        [InlineData("amqps://host:5671", true)]
        [InlineData("mongodb://localhost:27017", false)]
        [InlineData("not-a-uri", false)]
        public void IsRabbitMq_ReturnsExpected(string cs, bool expected)
        {
            IdentifierHelper.IsRabbitMq(cs).Should().Be(expected);
        }

        [Fact]
        public void GetControllerAction_WithNoEndpoint_ReturnsNulls()
        {
            var context = new DefaultHttpContext();

            var (controller, action) = IdentifierHelper.GetControllerAction(context);

            controller.Should().BeNull();
            action.Should().BeNull();
        }
    }
}
