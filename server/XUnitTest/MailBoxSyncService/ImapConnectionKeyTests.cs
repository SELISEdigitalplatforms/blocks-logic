using FluentAssertions;
using Mail.DomainService.Entities;
using Mail.DomainService.Shared.Enums;

namespace XUnitTest.MailBoxSyncService
{
    /// <summary>
    /// C6: cached IMAP sessions are isolated per tenant and per configuration.
    /// </summary>
    /// <remarks>
    /// The map used to be keyed on the username alone, in a singleton service. Two tenants that
    /// configured the same mailbox username shared one authenticated session and each read the
    /// other's inbox — a username is a field two unrelated records can hold the same value in, not
    /// an identity.
    /// </remarks>
    public class ImapConnectionKeyTests
    {
        private static MailServerConfiguration Config(string itemId, string userName, MailServiceProvider provider = MailServiceProvider.Zoho) =>
            new() { ItemId = itemId, SenderUserName = userName, Provider = provider };

        [Fact]
        public void SameUsernameInDifferentTenants_DoesNotShareAKey()
        {
            var config = Config("cfg-1", "shared@example.com");

            var a = global::MailBoxSyncService.Services.MailBoxSyncService.ConnectionKey(config, "tenant-a");
            var b = global::MailBoxSyncService.Services.MailBoxSyncService.ConnectionKey(config, "tenant-b");

            a.Should().NotBe(b);
        }

        [Fact]
        public void TwoConfigurationsInOneTenant_DoNotShareAKey()
        {
            var tenant = "tenant-a";

            var a = global::MailBoxSyncService.Services.MailBoxSyncService.ConnectionKey(Config("cfg-1", "shared@example.com"), tenant);
            var b = global::MailBoxSyncService.Services.MailBoxSyncService.ConnectionKey(Config("cfg-2", "shared@example.com"), tenant);

            a.Should().NotBe(b);
        }

        [Fact]
        public void TheSameConfigurationInTheSameTenant_ReusesItsKey()
        {
            var a = global::MailBoxSyncService.Services.MailBoxSyncService.ConnectionKey(Config("cfg-1", "a@example.com"), "tenant-a");
            var b = global::MailBoxSyncService.Services.MailBoxSyncService.ConnectionKey(Config("cfg-1", "a@example.com"), "tenant-a");

            a.Should().Be(b, "connection reuse is the point; only the sharing across identities was wrong");
        }

        [Fact]
        public void KeyPartsCannotBeConfusedWithOneAnother()
        {
            // A plain concatenation would make ("a", "bc") and ("ab", "c") the same key.
            var a = global::MailBoxSyncService.Services.MailBoxSyncService.ConnectionKey(Config("bc", "u"), "a");
            var b = global::MailBoxSyncService.Services.MailBoxSyncService.ConnectionKey(Config("c", "u"), "ab");

            a.Should().NotBe(b);
        }
    }
}
