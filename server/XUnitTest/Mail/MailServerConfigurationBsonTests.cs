using FluentAssertions;
using Mail.DomainService.Entities;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace XUnitTest.Mail
{
    /// <summary>
    /// H2, H12: documents written by blocks-os before and after SPEC31 both deserialize into
    /// blocks-logic's own entity copy, under the element name blocks-os writes.
    /// </summary>
    public class MailServerConfigurationBsonTests
    {
        private static BsonDocument Document() => new()
        {
            { "_id", "cfg-1" },
            { "Name", "Zoho outbound" },
            { "Host", "smtp.zoho.com" },
            { "Port", 587 },
            { "SenderUserName", "mailer" },
            { "AccountPassword", "app-pass-1" },
            { "SmtpClient", 0 },
            { "Provider", 1 },
        };

        [Fact]
        public void Deserialize_WithoutReference_LeavesTheReferenceNull()
        {
            var config = BsonSerializer.Deserialize<MailServerConfiguration>(Document());

            config.AccountPassword.Should().Be("app-pass-1");
            config.AccountPasswordReference.Should().BeNull();
        }

        [Fact]
        public void Deserialize_WithReference_ReadsTheExactElementName()
        {
            var document = Document();
            document["AccountPassword"] = "";
            document.Add("AccountPasswordReference", "s-123");

            var config = BsonSerializer.Deserialize<MailServerConfiguration>(document);

            config.AccountPassword.Should().BeEmpty();
            config.AccountPasswordReference.Should().Be("s-123");
        }

        [Fact]
        public void ClassMap_UsesTheExactElementName()
        {
            BsonClassMap.LookupClassMap(typeof(MailServerConfiguration))
                .GetMemberMap(nameof(MailServerConfiguration.AccountPasswordReference))
                .ElementName.Should().Be("AccountPasswordReference");
        }

        [Fact]
        public void Deserialize_WithoutIsOrganizationDefault_ReadsFalse()
        {
            // SPEC10 C11: configurations written before organizations existed.
            BsonSerializer.Deserialize<MailServerConfiguration>(Document()).IsOrganizationDefault.Should().BeFalse();
        }

        [Fact]
        public void Deserialize_WithIsOrganizationDefault_ReadsTheExactElementName()
        {
            // SPEC10 6.4: blocks-os writes the element under this name.
            var document = Document();
            document.Add("IsOrganizationDefault", true);

            BsonSerializer.Deserialize<MailServerConfiguration>(document).IsOrganizationDefault.Should().BeTrue();
            BsonClassMap.LookupClassMap(typeof(MailServerConfiguration))
                .GetMemberMap(nameof(MailServerConfiguration.IsOrganizationDefault))
                .ElementName.Should().Be("IsOrganizationDefault");
        }

        [Fact]
        public void TheGenesisEntity_CarriesTheSameField()
        {
            // H1, H12: the Genesis release in use is the one with blocks-genesis-net 1cb776f, whose
            // entity names the field exactly as this copy does.
            typeof(Blocks.Genesis.Entities.Os.MailServerConfiguration)
                .GetProperty(nameof(MailServerConfiguration.AccountPasswordReference))
                .Should().NotBeNull();
        }
    }
}
