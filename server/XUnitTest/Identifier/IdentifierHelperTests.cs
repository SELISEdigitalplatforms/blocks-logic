using DomainService.Shared;
using FluentAssertions;

namespace XUnitTest.Identifier
{
    public class IdentifierHelperTests
    {
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
        [InlineData("amqp://user:pass@localhost:5672", true)]
        [InlineData("amqps://user:pass@localhost:5671", true)]
        [InlineData("AMQP://user:pass@localhost:5672", true)]
        [InlineData("Endpoint=sb://ns.servicebus.windows.net/;SharedAccessKeyName=x", false)]
        [InlineData("not-a-uri", false)]
        [InlineData("https://example.com", false)]
        public void IsRabbitMq_DetectsAmqpSchemes(string connectionString, bool expected)
        {
            IdentifierHelper.IsRabbitMq(connectionString).Should().Be(expected);
        }
    }
}
