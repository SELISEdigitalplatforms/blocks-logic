using Blocks.Secrets;
using FluentAssertions;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Mails.Office365;
using Mail.DomainService.Shared.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Moq;

namespace XUnitTest.Mail
{
    /// <summary>
    /// Office 365 over Microsoft Graph: the token, the message, both send paths, and every failure
    /// path failing closed without a resend.
    /// </summary>
    public class Office365GraphMailSenderTests
    {
        private const string Token = "an-access-token";
        private const int ThreeMegabytes = 3 * 1024 * 1024;

        private readonly Mock<IOffice365TokenProvider> _tokens = new();
        private readonly RecordingGraphSession _session = new();
        private readonly CapturingLogger _logger = new();

        public Office365GraphMailSenderTests()
        {
            _tokens
                .Setup(t => t.GetTokenAsync(It.IsAny<Office365TokenRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Token);
        }

        private TestableGraphSender Sender() => new(_tokens.Object, _session, _logger);

        private static MailServerConfiguration Config() => new()
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
            SenderName = "Contoso Notifications",
            SenderAddress = "notifications@contoso.com",
            IsEnableSnsConfiguration = false
        };

        private static MailToBeSent AMail() => new()
        {
            ItemId = "mail-1",
            CorrelationId = "corr-1",
            To = ["recipient@example.com"],
            MailServerConfiguration = Config()
        };

        private static MailBody ABody(params MailAttachment[] attachments) => new()
        {
            Subject = "Invoice",
            Body = "<p>Invoice</p>",
            Attachments = attachments
        };

        private static MailAttachment Attachment(string name, int bytes) => new()
        {
            FileName = name,
            ContentType = "application/pdf",
            Content = new byte[bytes]
        };

        private Task<bool> Send(MailToBeSent? mail = null, MailBody? body = null) =>
            Sender().SendAsync(mail ?? AMail(), body ?? ABody(), "blocks-tenant");

        // ---------- token ----------

        [Fact]
        public async Task RequestsAGraphToken_ForTheConfigurationsOwnTenantAndApplication()
        {
            await Send();

            _tokens.Verify(
                t => t.GetTokenAsync(
                    It.Is<Office365TokenRequest>(r =>
                        r.BlocksTenantId == "blocks-tenant"
                        && r.EntraTenantId == "contoso-tenant"
                        && r.ClientId == "mailer-app"
                        && r.ClientSecretReference == "secret-1"
                        && r.Scope == Office365TokenScopes.Graph),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _session.Token.Should().Be(Token);
        }

        [Fact]
        public async Task SecretResolutionFailure_MakesNoGraphRequest()
        {
            _tokens
                .Setup(t => t.GetTokenAsync(It.IsAny<Office365TokenRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new SecretNotFoundException("secret-1"));

            (await Send()).Should().BeFalse();
            _session.Created.Should().BeFalse();
            _logger.Codes.Should().Equal(Office365FailureCode.SecretResolutionFailed);
        }

        [Fact]
        public async Task VaultOutage_MakesNoGraphRequest()
        {
            _tokens
                .Setup(t => t.GetTokenAsync(It.IsAny<Office365TokenRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new SecretVaultException("down", "Get", "secret-1"));

            (await Send()).Should().BeFalse();
            _session.Created.Should().BeFalse();
            _logger.Codes.Should().Equal(Office365FailureCode.VaultUnavailable);
        }

        [Fact]
        public async Task TokenAcquisitionFailure_MakesNoGraphRequest()
        {
            _tokens
                .Setup(t => t.GetTokenAsync(It.IsAny<Office365TokenRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("AADSTS7000215"));

            (await Send()).Should().BeFalse();
            _session.Created.Should().BeFalse();
            _logger.Codes.Should().Equal(Office365FailureCode.TokenAcquisitionFailed);
        }

        // ---------- the message ----------

        [Fact]
        public async Task SendsOnce_ThroughSendMail_AsTheConfiguredMailbox()
        {
            var result = await Send();

            result.Should().BeTrue();
            _session.Calls.Should().Equal("sendMail mailer@contoso.com");
        }

        [Fact]
        public async Task TheMessage_CarriesTheSameIdentitiesTheSmtpComposerWould()
        {
            var mail = AMail();
            mail.To = ["Ada Lovelace <ada@example.com>", "bob@example.com"];
            mail.Cc = ["cc@example.com"];
            mail.Bcc = ["bcc@example.com"];
            mail.ReplyTo = ["replies@contoso.com"];

            await Send(mail);

            var message = _session.Sent!;
            message.Subject.Should().Be("Invoice");
            message.Body!.ContentType.Should().Be(BodyType.Html);
            message.Body.Content.Should().Be("<p>Invoice</p>");
            message.From!.EmailAddress!.Address.Should().Be("notifications@contoso.com");
            message.From.EmailAddress.Name.Should().Be("Contoso Notifications");
            message.ToRecipients!.Select(r => (r.EmailAddress!.Name, r.EmailAddress.Address)).Should().Equal(
                ("Ada Lovelace", "ada@example.com"),
                (null, "bob@example.com"));
            message.CcRecipients!.Single().EmailAddress!.Address.Should().Be("cc@example.com");
            message.BccRecipients!.Single().EmailAddress!.Address.Should().Be("bcc@example.com");
            message.ReplyTo!.Single().EmailAddress!.Address.Should().Be("replies@contoso.com");
        }

        [Fact]
        public async Task ABlankSenderAddress_LeavesFromUnset_SoGraphSendsAsTheMailbox()
        {
            var mail = AMail();
            mail.MailServerConfiguration.SenderAddress = "";

            (await Send(mail)).Should().BeTrue();
            _session.Sent!.From.Should().BeNull();
        }

        [Fact]
        public async Task AnUnparsableAddress_FailsBeforeAnyGraphRequest()
        {
            var mail = AMail();
            mail.To = ["not an address"];

            (await Send(mail)).Should().BeFalse();
            _session.Calls.Should().BeEmpty();
            _logger.Codes.Should().Equal(Office365FailureCode.RecipientRejected);
        }

        [Fact]
        public async Task SmallAttachments_GoInlineInTheOneRequest()
        {
            await Send(body: ABody(Attachment("a.pdf", 1024), Attachment("b.pdf", 2048)));

            _session.Calls.Should().Equal("sendMail mailer@contoso.com");
            var attachments = _session.Sent!.Attachments!.Cast<FileAttachment>().ToList();
            attachments.Select(a => a.Name).Should().Equal("a.pdf", "b.pdf");
            attachments[0].ContentType.Should().Be("application/pdf");
            attachments[0].ContentBytes.Should().HaveCount(1024);
        }

        // ---------- the draft path ----------

        [Fact]
        public async Task ALargeAttachment_GoesThroughADraftAndAnUploadSession()
        {
            var result = await Send(body: ABody(Attachment("small.pdf", 1024), Attachment("big.pdf", ThreeMegabytes)));

            result.Should().BeTrue();
            _session.Calls.Should().Equal(
                "createDraft mailer@contoso.com",
                "addAttachment draft-1 small.pdf",
                "uploadAttachment draft-1 big.pdf",
                "sendDraft draft-1");
            _session.Sent!.Attachments.Should().BeNull("the draft is created bare and filled one attachment at a time");
        }

        [Fact]
        public async Task SmallAttachmentsThatTogetherExceedOneRequest_GoThroughADraftWithoutAnUploadSession()
        {
            var result = await Send(body: ABody(Attachment("a.pdf", ThreeMegabytes / 2), Attachment("b.pdf", ThreeMegabytes / 2)));

            result.Should().BeTrue();
            _session.Calls.Should().Equal(
                "createDraft mailer@contoso.com",
                "addAttachment draft-1 a.pdf",
                "addAttachment draft-1 b.pdf",
                "sendDraft draft-1");
        }

        [Fact]
        public async Task AFailedUpload_DeletesTheDraftAndNeverSendsIt()
        {
            _session.Throw["uploadAttachment"] = new ODataError { ResponseStatusCode = 403, Error = new MainError { Code = "ErrorAccessDenied" } };

            var result = await Send(body: ABody(Attachment("big.pdf", ThreeMegabytes)));

            result.Should().BeFalse();
            _session.Calls.Should().Equal(
                "createDraft mailer@contoso.com",
                "uploadAttachment draft-1 big.pdf",
                "deleteDraft draft-1");
            _logger.Codes.Should().Equal(Office365FailureCode.PermissionDenied);
        }

        [Fact]
        public async Task AFailedDraftSend_IsNotRetried_AndTheDraftIsCleanedUp()
        {
            _session.Throw["sendDraft"] = new HttpRequestException("reset");

            var result = await Send(body: ABody(Attachment("big.pdf", ThreeMegabytes)));

            result.Should().BeFalse();
            _session.Calls.Count(c => c.StartsWith("sendDraft", StringComparison.Ordinal)).Should().Be(1);
            _session.Calls.Last().Should().Be("deleteDraft draft-1");
        }

        [Fact]
        public async Task AFailedCleanup_IsNotASecondFailure()
        {
            _session.Throw["sendDraft"] = new HttpRequestException("reset");
            _session.Throw["deleteDraft"] = new HttpRequestException("reset again");

            var result = await Send(body: ABody(Attachment("big.pdf", ThreeMegabytes)));

            result.Should().BeFalse();
            _logger.Codes.Should().ContainSingle("the send failure is reported once; the cleanup is only a warning");
        }

        [Fact]
        public async Task AFailedDraftCreation_LeavesNothingToDelete()
        {
            _session.Throw["createDraft"] = new ODataError { ResponseStatusCode = 403, Error = new MainError { Code = "ErrorAccessDenied" } };

            (await Send(body: ABody(Attachment("big.pdf", ThreeMegabytes)))).Should().BeFalse();
            _session.Calls.Should().Equal("createDraft mailer@contoso.com");
        }

        // ---------- failure ----------

        [Fact]
        public async Task AFailedSendMail_ReturnsFalse_AndIsNotRetried()
        {
            _session.Throw["sendMail"] = new ODataError { ResponseStatusCode = 503, Error = new MainError { Code = "ErrorServerBusy" } };

            var result = await Send();

            result.Should().BeFalse();
            _session.Calls.Should().Equal("sendMail mailer@contoso.com");
            _logger.Codes.Should().Equal(Office365FailureCode.Throttled);
        }

        [Fact]
        public async Task TheFailureLog_NamesTheGraphStatusAndCode_ButNoRecipientOrToken()
        {
            _session.Throw["sendMail"] = new ODataError { ResponseStatusCode = 403, Error = new MainError { Code = "ErrorAccessDenied", Message = "Access is denied for recipient@example.com" } };

            await Send();

            var line = _logger.Messages.Single();
            line.Should().Contain("FailureCode=O365_PERMISSION_DENIED");
            line.Should().Contain("Host=graph.microsoft.com");
            line.Should().Contain("Reason=graph status 403 code ErrorAccessDenied");
            line.Should().NotContain("recipient@example.com");
            line.Should().NotContain(Token);
        }

        [Theory]
        [InlineData(401, null, Office365FailureCode.AuthenticationFailed)]
        [InlineData(403, "ErrorAccessDenied", Office365FailureCode.PermissionDenied)]
        [InlineData(403, null, Office365FailureCode.PermissionDenied)]
        [InlineData(403, "ErrorSendAsDenied", Office365FailureCode.SendAsDenied)]
        [InlineData(404, "ErrorInvalidUser", Office365FailureCode.MailboxNotFound)]
        [InlineData(404, "MailboxNotEnabledForRESTAPI", Office365FailureCode.MailboxNotFound)]
        [InlineData(400, "ErrorInvalidRecipients", Office365FailureCode.RecipientRejected)]
        [InlineData(413, null, Office365FailureCode.MessageTooLarge)]
        [InlineData(400, "ErrorMessageSizeExceeded", Office365FailureCode.MessageTooLarge)]
        [InlineData(429, "ApplicationThrottled", Office365FailureCode.Throttled)]
        [InlineData(503, null, Office365FailureCode.Throttled)]
        [InlineData(400, "ErrorInvalidRequest", Office365FailureCode.GraphFailed)]
        [InlineData(500, null, Office365FailureCode.GraphFailed)]
        public void Classify_UsesTheGraphCodeFirst_ThenTheStatus(int status, string? code, string expected)
        {
            var error = new ODataError { ResponseStatusCode = status, Error = code is null ? null : new MainError { Code = code } };

            Office365GraphMailSender.Classify(error).Should().Be(expected);
        }

        [Fact]
        public void Classify_TransportFailures()
        {
            Office365GraphMailSender.Classify(
                new HttpRequestException("tls", new System.Security.Authentication.AuthenticationException("bad cert")))
                .Should().Be(Office365FailureCode.TlsFailed);

            Office365GraphMailSender.Classify(new TaskCanceledException("timeout"))
                .Should().Be(Office365FailureCode.GraphFailed);
        }

        [Fact]
        public void Describe_KeepsOnlyIdentifierCharactersOfTheCode()
        {
            var error = new ODataError { ResponseStatusCode = 400, Error = new MainError { Code = "Bad\r\nInjected=1 Code" } };

            Office365GraphMailSender.Describe(error).Should().Be("graph status 400 code BadInjected1Code");
        }

        [Fact]
        public void FitsInOneRequest_EstimatesTheRequestOnTheWire_NotTheContent()
        {
            Office365GraphMailSender.FitsInOneRequest(ABody()).Should().BeTrue();
            Office365GraphMailSender.FitsInOneRequest(ABody(Attachment("a", 2_000_000))).Should().BeTrue();

            // Under the upload-session threshold as content, but a third larger as base64: about
            // 4.1 MB on the wire, over Graph's request limit.
            Office365GraphMailSender.FitsInOneRequest(ABody(Attachment("a", 3_100_000))).Should().BeFalse();
            Office365GraphMailSender.FitsInOneRequest(ABody(Attachment("a", ThreeMegabytes))).Should().BeFalse("it needs an upload session");
        }

        [Fact]
        public void FitsInOneRequest_CountsTheBodyAsTheSerializerEscapesIt()
        {
            // 700 KB of markup is 4.2 MB once every '<' is written as <.
            var markup = ABody();
            markup.Body = new string('<', 700_000);
            Office365GraphMailSender.FitsInOneRequest(markup).Should().BeFalse();

            var text = ABody();
            text.Body = new string('a', 700_000);
            Office365GraphMailSender.FitsInOneRequest(text).Should().BeTrue();
        }

        /// <summary>A sender whose Graph session is a recorder.</summary>
        private sealed class TestableGraphSender : Office365GraphMailSender
        {
            private readonly RecordingGraphSession _session;

            public TestableGraphSender(IOffice365TokenProvider tokens, RecordingGraphSession session, ILogger<Office365GraphMailSender> logger)
                : base(tokens, Mock.Of<IHttpClientFactory>(), logger)
            {
                _session = session;
            }

            protected override IOffice365GraphMailSession CreateSession(string accessToken)
            {
                _session.Created = true;
                _session.Token = accessToken;
                return _session;
            }
        }

        private sealed class RecordingGraphSession : IOffice365GraphMailSession
        {
            public bool Created { get; set; }
            public string? Token { get; set; }
            public Message? Sent { get; private set; }
            public List<string> Calls { get; } = [];
            public Dictionary<string, Exception> Throw { get; } = [];

            private void Record(string operation, string detail)
            {
                Calls.Add($"{operation} {detail}");
                if (Throw.TryGetValue(operation, out var exception)) throw exception;
            }

            public Task SendMailAsync(string mailbox, Message message, CancellationToken cancellationToken = default)
            {
                Sent = message;
                Record("sendMail", mailbox);
                return Task.CompletedTask;
            }

            public Task<string> CreateDraftAsync(string mailbox, Message message, CancellationToken cancellationToken = default)
            {
                Sent = message;
                Record("createDraft", mailbox);
                return Task.FromResult("draft-1");
            }

            public Task AddAttachmentAsync(string mailbox, string draftId, MailAttachment attachment, CancellationToken cancellationToken = default)
            {
                Record("addAttachment", $"{draftId} {attachment.FileName}");
                return Task.CompletedTask;
            }

            public Task UploadAttachmentAsync(string mailbox, string draftId, MailAttachment attachment, CancellationToken cancellationToken = default)
            {
                Record("uploadAttachment", $"{draftId} {attachment.FileName}");
                return Task.CompletedTask;
            }

            public Task SendDraftAsync(string mailbox, string draftId, CancellationToken cancellationToken = default)
            {
                Record("sendDraft", draftId);
                return Task.CompletedTask;
            }

            public Task DeleteDraftAsync(string mailbox, string draftId, CancellationToken cancellationToken = default)
            {
                Record("deleteDraft", draftId);
                return Task.CompletedTask;
            }
        }

        /// <summary>Keeps every error line, so a test can read the one classified failure.</summary>
        private sealed class CapturingLogger : ILogger<Office365GraphMailSender>
        {
            public List<string> Messages { get; } = [];

            public IEnumerable<string> Codes => Messages
                .Select(m => m.Split(' ').First(part => part.StartsWith("FailureCode=", StringComparison.Ordinal)))
                .Select(part => part["FailureCode=".Length..]);

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel == LogLevel.Error)
                {
                    Messages.Add(formatter(state, exception));
                }
            }
        }
    }
}
