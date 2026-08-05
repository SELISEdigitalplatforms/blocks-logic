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
    }
}
