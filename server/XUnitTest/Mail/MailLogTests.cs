using FluentAssertions;
using Mail.DomainService.Utilities;

namespace XUnitTest.Mail
{
    /// <summary>
    /// Mail log lines are searched routinely and shipped to observability, so no address may
    /// appear in one intact. The domain is deliberately kept: it is what you need to diagnose a
    /// delivery problem, and it is not identifying on its own.
    /// </summary>
    public class MailLogTests
    {
        [Theory]
        [InlineData("john.doe@yopmail.com", "*****@yopmail.com")]
        [InlineData("a@b.co", "*****@b.co")]
        [InlineData("first.last+tag@sub.example.org", "*****@sub.example.org")]
        public void Mask_KeepsTheDomainAndNothingElse(string address, string expected)
        {
            MailLog.Mask(address).Should().Be(expected);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Mask_HandlesAMissingAddress(string? address)
        {
            MailLog.Mask(address).Should().Be("(blank)");
        }

        [Fact]
        public void Mask_RevealsNothingWhenThereIsNoDomain()
        {
            MailLog.Mask("not-an-address").Should().Be("*****");
        }

        [Fact]
        public void Recipients_MasksEveryEntry()
        {
            var line = MailLog.Recipients(["john.doe@yopmail.com", "jane@example.com"]);

            line.Should().Be("*****@yopmail.com, *****@example.com");
            line.Should().NotContain("john").And.NotContain("jane");
        }

        [Fact]
        public void Recipients_SaysSoWhenThereAreNone()
        {
            MailLog.Recipients(null).Should().Be("(none)");
            MailLog.Recipients([]).Should().Be("(none)");
        }

        [Fact]
        public void Count_TreatsNullAsZero()
        {
            MailLog.Count(null).Should().Be(0);
            MailLog.Count(["a", "b"]).Should().Be(2);
        }
    }
}
