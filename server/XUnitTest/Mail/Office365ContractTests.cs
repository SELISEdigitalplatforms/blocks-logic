using FluentAssertions;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails.Office365;
using Mail.DomainService.Shared.Enums;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace XUnitTest.Mail
{
    /// <summary>
    /// H1 and C1: the configuration contract shared with blocks-os, and the runtime guard that
    /// refuses a record before anything is read or connected.
    /// </summary>
    public class Office365ContractTests
    {
        [Fact]
        public void ProviderValues_MatchBlocksOs()
        {
            // Persisted, and mirrored by blocks-os and blocks-cli. Appending is safe; reordering
            // silently repoints every stored record at a different integration.
            ((int)MailServiceProvider.AmazonSes).Should().Be(0);
            ((int)MailServiceProvider.Zoho).Should().Be(1);
            ((int)MailServiceProvider.Office365Smtp).Should().Be(2);
        }

        [Fact]
        public void AuthenticationAndSecurityDefaults_AreZero()
        {
            // So a document written before these fields existed lands on the legacy path.
            ((int)MailAuthenticationType.Password).Should().Be(0);
            ((int)MailSecurityMode.Legacy).Should().Be(0);
            ((int)MailAuthenticationType.OAuthClientCredentials).Should().Be(1);
            ((int)MailSecurityMode.StartTls).Should().Be(2);
        }

        [Fact]
        public void PreChangeDocument_DeserializesOntoTheLegacyPath()
        {
            var document = BsonDocument.Parse("""
                {
                  "_id": "legacy-zoho-1",
                  "Name": "Zoho Primary",
                  "Host": "smtp.zoho.com",
                  "Port": 465,
                  "EnableSSL": true,
                  "SenderUserName": "zohouser",
                  "AccountPassword": "password1",
                  "Provider": 1,
                  "IsInbound": false
                }
                """);

            var entity = BsonSerializer.Deserialize<MailServerConfiguration>(document);

            entity.AuthenticationType.Should().Be(MailAuthenticationType.Password);
            entity.SecurityMode.Should().Be(MailSecurityMode.Legacy);
            entity.TenantId.Should().BeNull();
            entity.ClientSecretReference.Should().BeNull();
            entity.AccountPassword.Should().Be("password1");
        }

        [Fact]
        public void SnsFlag_AbsentMeansYesForLegacyAndNoForAProviderThatForbidsIt()
        {
            // The one field whose default differs from blocks-os, which writes false. Absent has to
            // stay distinguishable from false: read as a plain bool it would default to true here
            // and fail every Office 365 record whose document happens to omit it.
            var absent = BsonSerializer.Deserialize<MailServerConfiguration>(
                BsonDocument.Parse("""{ "_id": "x", "Provider": 1 }"""));

            absent.IsEnableSnsConfiguration.Should().BeNull();
            absent.SendsSnsHeaders().Should().BeTrue("a record written before the field existed kept getting SES headers");
            absent.RequestsSnsHeaders().Should().BeFalse("absence is not a request");

            var explicitlyOff = new MailServerConfiguration { IsEnableSnsConfiguration = false };
            explicitlyOff.SendsSnsHeaders().Should().BeFalse();
            explicitlyOff.RequestsSnsHeaders().Should().BeFalse();

            var explicitlyOn = new MailServerConfiguration { IsEnableSnsConfiguration = true };
            explicitlyOn.SendsSnsHeaders().Should().BeTrue();
            explicitlyOn.RequestsSnsHeaders().Should().BeTrue();
        }

        private static MailServerConfiguration Valid() => new()
        {
            ItemId = "cfg-1",
            Provider = MailServiceProvider.Office365Smtp,
            IsInbound = false,
            AuthenticationType = MailAuthenticationType.OAuthClientCredentials,
            SecurityMode = MailSecurityMode.StartTls,
            Host = "smtp.office365.com",
            Port = 587,
            TenantId = "contoso-tenant",
            ClientId = "mailer-app",
            ClientSecretReference = "secret-1",
            MailboxAddress = "mailer@contoso.com",
            IsEnableSnsConfiguration = false
        };

        [Fact]
        public void Validate_AWellFormedRecord_Passes()
        {
            Office365ConfigurationContract.Validate(Valid(), "blocks-tenant").Should().BeNull();
        }

        [Fact]
        public void Validate_HostComparison_IgnoresCase()
        {
            var config = Valid();
            config.Host = "SMTP.Office365.COM";

            Office365ConfigurationContract.Validate(config, "blocks-tenant").Should().BeNull();
        }

        [Fact]
        public void Validate_ARecordMissingTheSnsField_Passes()
        {
            // The regression this guards: fail-closed must react to a deliberate request, not to a
            // document that predates the field.
            var config = Valid();
            config.IsEnableSnsConfiguration = null;

            Office365ConfigurationContract.Validate(config, "blocks-tenant").Should().BeNull();
        }

        [Theory]
        [InlineData("port")]
        [InlineData("host")]
        [InlineData("inbound")]
        [InlineData("sns")]
        [InlineData("auth")]
        [InlineData("security")]
        [InlineData("tenant")]
        [InlineData("client")]
        [InlineData("reference")]
        [InlineData("mailbox")]
        [InlineData("provider")]
        public void Validate_RejectsEachViolation(string violation)
        {
            var config = Valid();

            switch (violation)
            {
                case "port": config.Port = 25; break;
                case "host": config.Host = "smtp.contoso.example"; break;
                case "inbound": config.IsInbound = true; break;
                case "sns": config.IsEnableSnsConfiguration = true; break;
                case "auth": config.AuthenticationType = MailAuthenticationType.Password; break;
                case "security": config.SecurityMode = MailSecurityMode.SslOnConnect; break;
                case "tenant": config.TenantId = "  "; break;
                case "client": config.ClientId = null; break;
                case "reference": config.ClientSecretReference = ""; break;
                case "mailbox": config.MailboxAddress = null; break;
                case "provider": config.Provider = MailServiceProvider.Zoho; break;
            }

            Office365ConfigurationContract.Validate(config, "blocks-tenant").Should().NotBeNull();
        }

        [Fact]
        public void Validate_RequiresAnAmbientTenant()
        {
            // Without one the secret read cannot be scoped to anybody, so it must not be attempted.
            Office365ConfigurationContract.Validate(Valid(), blocksTenantId: null).Should().NotBeNull();
            Office365ConfigurationContract.Validate(Valid(), blocksTenantId: "   ").Should().NotBeNull();
        }

        [Fact]
        public void TheExchangeScope_IsExact()
        {
            // A token for Graph or a resource-specific scope is issued happily and then refused by
            // SMTP, surfacing as an authentication failure with nothing pointing at the scope.
            AzureOffice365TokenAcquirer.ExchangeOnlineScope.Should().Be("https://outlook.office365.com/.default");
        }
    }
}
