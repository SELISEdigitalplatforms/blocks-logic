using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Services;
using Microsoft.Extensions.Logging;
using Moq;
using EmailTemplate = Blocks.Genesis.Entities.Os.EmailTemplate;

namespace XUnitTest.Mail
{
    /// <summary>
    /// The optional <see cref="BaseMailRequest.OrganizationId"/>. On HTTP a token of the Default organization (or of
    /// none) may name any organization and any other token only its own. On the queue a message without a delegation
    /// grant is a service send and may name any organization; with a grant, the grant's organization decides. A request
    /// without the field, or one that may not name the organization, is mapped exactly as before the field existed.
    /// </summary>
    public sealed class MailRequestOrganizationTests : IDisposable
    {
        private const string Tenant = "t1";
        private const string Caller = "caller@app.test";

        private readonly Mock<IMailRepository> _repo = new();
        private readonly Mock<IValidator<MailToBeSent>> _validator = new();
        private readonly Mock<IDelegationGrantStore> _grants = new();
        private readonly MailSendSelectionMongoTests.RecordingLogger _logger = new();
        private string? _templateOrganization;
        private string? _configurationOrganization;

        public MailRequestOrganizationTests()
        {
            _repo.Setup(r => r.GetEmailAdressOfUsers(It.IsAny<IEnumerable<string>>())).ReturnsAsync([Caller]);
            _repo.Setup(r => r.GetEmailTemplateForOrganizationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string?, string?, string>((_, _, organization) => _templateOrganization = organization)
                .ReturnsAsync(EmailTemplateChoice.None);
            _repo.Setup(r => r.GetMailServerConfigurationForSendAsync(It.IsAny<string?>(), It.IsAny<string>()))
                .Callback<string?, string>((_, organization) => _configurationOrganization = organization)
                .ReturnsAsync(MailServerConfigurationChoice.None);
            // Stop right after mapping: these tests need only the organization the lookups were asked for.
            _validator.Setup(v => v.ValidateAsync(It.IsAny<MailToBeSent>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult([new ValidationFailure("x", "stop")]));
        }

        public void Dispose()
        {
            BlocksContext.ClearContext();
            DelegatedTokenContext.Clear();
        }

        private MailService Service(bool withGrantStore = true) => new(_validator.Object, Mock.Of<IMessageClient>(), _repo.Object,
            Mock.Of<ISendMailService>(), _logger, withGrantStore ? _grants.Object : null);

        // An HTTP token, or on the queue the context a producer wrote into the message.
        private static void SignIn(string? organizationId) =>
            BlocksContext.SetContext(BlocksContext.Create(Tenant, [], "caller", true, "/api/mail", organizationId, DateTime.UtcNow.AddMinutes(5),
                Caller, [], "caller", "", "Caller", "token", Tenant));

        private static SendMail Mail(string? organizationId) =>
            new() { Purpose = "Welcome", Language = "en-US", To = [Caller], OrganizationId = organizationId, CorrelationId = "corr-1" };

        private static SendMailToAny MailToAny(string? organizationId) =>
            new() { Purpose = "Welcome", Language = "en-US", To = [Caller], OrganizationId = organizationId, CorrelationId = "corr-1" };

        /// <summary>Puts a grant behind the current flow, as the worker does for a message sent by a person or client.</summary>
        private string HoldGrant(string? organizationId, string tenantId = Tenant)
        {
            var id = DelegationGrantStore.NewGrantId();
            _grants.Setup(g => g.GetAsync(id))
                .ReturnsAsync(new DelegationGrantRecord { TenantId = tenantId, UserId = "u1", OrganizationId = organizationId ?? string.Empty });
            DelegatedTokenContext.Set(id);
            return id;
        }

        // token organization, requested organization, organization used
        public static TheoryData<string?, string?, string> HttpCases => new()
        {
            // No field: the token's organization, as before.
            { "acme", null, "acme" },
            { "default", null, "default" },
            { null, null, "default" },
            { "acme", "  ", "acme" },
            // A token of the Default organization, or of none (the workflow node), may name any organization.
            { "default", "acme", "acme" },
            { "", "acme", "acme" },
            { null, " acme ", "acme" },
            // Any other token its own; another organization is ignored, which is what happened before the field.
            { "acme", "acme", "acme" },
            { "acme", "globex", "acme" },
            { "acme", "default", "acme" },
        };

        [Theory]
        [MemberData(nameof(HttpCases))]
        public async Task Send_UsesTheOrganizationTheTokenMayName(string? token, string? requested, string expected)
        {
            SignIn(token);

            await Service().ProcessMailAsync(Mail(requested));

            _templateOrganization.Should().Be(expected);
        }

        [Theory]
        [MemberData(nameof(HttpCases))]
        public async Task SendToAny_UsesTheOrganizationTheTokenMayName(string? token, string? requested, string expected)
        {
            SignIn(token);

            await Service().ProcessMailToAnyAsync(MailToAny(requested));

            _templateOrganization.Should().Be(expected);
        }

        [Fact]
        public async Task Send_PhoneAsEmail_UsesTheRequestedOrganizationToo()
        {
            SignIn("default");
            _repo.Setup(r => r.GetPhoneEmailAddressesOfUsers(It.IsAny<IEnumerable<string>>())).ReturnsAsync([Caller]);
            var mail = Mail("acme");
            mail.SendPhoneNumberAsEmail = true;

            await Service().ProcessMailAsync(mail);

            _templateOrganization.Should().Be("acme");
        }

        [Fact]
        public async Task Send_ForAnotherOrganization_IsNotRefused_AndLogsAWarning()
        {
            SignIn("acme");

            var response = await Service().ProcessMailAsync(Mail("globex"));

            response.Errors.Should().NotContainKey(nameof(BaseMailRequest.OrganizationId));
            _logger.Lines.Should().Contain(l => l.Level == LogLevel.Warning
                && l.Message.Contains("a token of organization acme may not send for organization globex")
                && l.Message.Contains("correlationId=corr-1"));
        }

        [Fact]
        public async Task Send_ForAnOrganizationTheTokenMayName_LogsNoWarningAboutIt()
        {
            SignIn("default");

            await Service().ProcessMailAsync(Mail("acme"));

            _logger.Lines.Should().NotContain(l => l.Message.Contains("may not send for organization"));
        }

        [Fact]
        public async Task TestMailByTemplateId_UsesTheTemplatesOrganization_NotTheRequestedOne()
        {
            SignIn("default");
            _repo.Setup(r => r.GetEmailTemplateByIdAsync("T-acme")).ReturnsAsync(new EmailTemplate
            {
                ItemId = "T-acme", Name = "Welcome", Language = "en-US", OrganizationId = "acme", MailConfigurationId = "C1"
            });
            var mail = MailToAny("globex");
            mail.IsTestMail = true;
            mail.TemplateId = "T-acme";

            await Service().ProcessMailToAnyAsync(mail);

            _configurationOrganization.Should().Be("acme");
            _templateOrganization.Should().BeNull("a test mail by templateId never looks a template up by organization");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("acme")]
        public async Task Queued_WithoutAnOrganization_UsesTheDefault_AndReadsNoGrant(string? contextOrganization)
        {
            SignIn(contextOrganization);
            HoldGrant("acme");

            await Service().ProcessQueuedMailAsync(Mail(null));

            _templateOrganization.Should().Be("default");
            _grants.Verify(g => g.GetAsync(It.IsAny<string>()), Times.Never);
        }

        [Theory]
        [InlineData("default")]
        [InlineData("globex")]
        [InlineData(null)]
        public async Task Queued_WithAnOrganizationAndNoGrant_IsAServiceSend_AndUsesIt(string? contextOrganization)
        {
            // IAM's forgot password, OTP and activation mails, and background jobs, carry no grant. The message
            // context plays no part: any producer can write it.
            SignIn(contextOrganization);

            await Service().ProcessQueuedMailAsync(Mail(" acme "));

            _templateOrganization.Should().Be("acme");
            _grants.Verify(g => g.GetAsync(It.IsAny<string>()), Times.Never);
        }

        [Theory]
        // message context, grant organization, organization used (the request names acme)
        [InlineData("default", "default", "acme")]
        [InlineData("default", "", "acme")]
        [InlineData("globex", "acme", "acme")]
        [InlineData("acme", "acme", "acme")]
        [InlineData("acme", "globex", "default")]
        [InlineData("default", "globex", "default")]
        public async Task Queued_WithAGrant_TheGrantsOrganizationDecides_NotTheMessageContext(
            string contextOrganization, string grantOrganization, string expected)
        {
            SignIn(contextOrganization);
            HoldGrant(grantOrganization);

            await Service().ProcessQueuedMailAsync(Mail("acme"));

            _templateOrganization.Should().Be(expected);
        }

        [Fact]
        public async Task Queued_GrantOfAnotherOrganization_LogsAWarning_AndTheMailIsStillMapped()
        {
            SignIn("default");
            HoldGrant("globex");

            await Service().ProcessQueuedMailAsync(Mail("acme"));

            _templateOrganization.Should().Be("default");
            _logger.Lines.Should().Contain(l => l.Level == LogLevel.Warning
                && l.Message.Contains("is in organization globex and may not send for organization acme")
                && l.Message.Contains("correlationId=corr-1"));
        }

        [Fact]
        public async Task Queued_GrantThatIsGone_UsesTheDefault()
        {
            SignIn("default");
            var id = HoldGrant("default");
            _grants.Setup(g => g.GetAsync(id)).ReturnsAsync((DelegationGrantRecord?)null);

            await Service().ProcessQueuedMailAsync(Mail("acme"));

            _templateOrganization.Should().Be("default");
            _logger.Lines.Should().Contain(l => l.Level == LogLevel.Warning && l.Message.Contains("could not be read"));
        }

        [Fact]
        public async Task Queued_GrantOfAnotherTenant_UsesTheDefault()
        {
            SignIn("default");
            HoldGrant("default", tenantId: "other-tenant");

            await Service().ProcessQueuedMailAsync(Mail("acme"));

            _templateOrganization.Should().Be("default");
        }

        [Fact]
        public async Task Queued_GrantStoreFails_UsesTheDefault_AndNeverLogsTheGrantId()
        {
            SignIn("default");
            var id = HoldGrant("default");
            _grants.Setup(g => g.GetAsync(id)).ThrowsAsync(new TimeoutException($"Timeout performing GET delegation:{id}"));

            await Service().ProcessQueuedMailAsync(Mail("acme"));

            _templateOrganization.Should().Be("default");
            _logger.Lines.Should().Contain(l => l.Message.Contains("reading the delegation grant failed: TimeoutException"));
            _logger.Lines.Should().NotContain(l => l.Message.Contains(id));
        }

        [Fact]
        public async Task Queued_HostWithoutAGrantStore_UsesTheDefaultWhenAGrantIsHeld()
        {
            SignIn("default");
            HoldGrant("default");

            await Service(withGrantStore: false).ProcessQueuedMailAsync(Mail("acme"));

            _templateOrganization.Should().Be("default");
        }

        [Fact]
        public void MayActFor_DefaultOrNoSenderMayNameAny_AnyOtherOnlyItself()
        {
            MailOrganization.MayActFor(null, "acme").Should().BeTrue();
            MailOrganization.MayActFor(" ", "acme").Should().BeTrue();
            MailOrganization.MayActFor("default", "acme").Should().BeTrue();
            MailOrganization.MayActFor("acme", "acme").Should().BeTrue();
            MailOrganization.MayActFor("acme", "globex").Should().BeFalse();
            MailOrganization.MayActFor("acme", "default").Should().BeFalse();
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData("", null)]
        [InlineData("  ", null)]
        [InlineData(" acme ", "acme")]
        public void Requested_IsTrimmed_AndBlankIsNone(string? value, string? expected) =>
            MailOrganization.Requested(value).Should().Be(expected);
    }
}
