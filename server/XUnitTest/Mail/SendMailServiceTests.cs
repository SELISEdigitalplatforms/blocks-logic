using Blocks.Genesis;
using FluentAssertions;
using Mail.DomainService.Dtos;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Mails.Office365;
using Mail.DomainService.Mails.Strategies;
using Mail.DomainService.Shared.Enums;
using DataAccess.InternalService.Notifications;
using Mail.DomainService.Services;
using MailKit.Security;
using Mail.DomainService.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using Moq;
using XUnitTest.TestHelpers;
using EmailTemplate = Blocks.Genesis.Entities.Os.EmailTemplate;

namespace XUnitTest.Mail
{
    /// <summary>
    /// Covers the send pipeline end to end: a real MailKit message is built, so these assert what
    /// actually reaches the wire rather than what the code intended to put there.
    /// </summary>
    public class SendMailServiceTests
    {
        private const string MailId = "mail-1";

        private readonly Mock<IMailRepository> _repository = new();
        private readonly Mock<IMailAttachmentResolver> _resolver = new();
        private readonly Mock<IMessageClient> _messageClient = new();
        private readonly RecordingSmtpClient _smtp = new();
        private readonly MailStatusEventOptions _statusOptions = new();
        private Mock<Blocks.Secrets.ISecretService>? _secrets;

        /// <param name="senders">
        /// Overrides the registered outbound senders. The default keeps the legacy providers
        /// wired to the recording client, so the existing assertions still exercise a real
        /// MailKit message build.
        /// </param>
        private SendMailService CreateService(params IOutboundMailSender[] senders)
        {
            var services = new ServiceCollection();
            services.AddSingleton<MailKitSmtpClient>(_smtp);

            var provider = new SmtpClientProvider(
                services.BuildServiceProvider(),
                NullLogger<SmtpClientProvider>.Instance);

            var registry = new OutboundMailSenderRegistry(
                senders.Length > 0
                    ? senders
                    : [new AmazonSesMailSender(provider), new ZohoMailSender(provider)]);

            return new SendMailService(
                NullLogger<SendMailService>.Instance,
                _repository.Object,
                registry,
                new MailSendResolvers(_resolver.Object, MailSecretsTestDoubles.PasswordResolver(_secrets)),
                _messageClient.Object,
                Options.Create(_statusOptions));
        }

        private static MailToBeSent AMail(params string[] attachmentIds) => new()
        {
            ItemId = MailId,
            Name = "subscription_invoice",
            Language = "en-US",
            To = ["someone@example.com"],
            Attachments = attachmentIds,
            BodyDataContext = new Dictionary<string, string> { { "DocumentNumber", "INV-2026-000043" } },
            SubjectDataContext = new Dictionary<string, string> { { "DocumentNumber", "INV-2026-000043" } },
            EmailTemplate = new EmailTemplate
            {
                Name = "subscription_invoice",
                TemplateSubject = "Invoice {{DocumentNumber}}",
                TemplateBody = "<p>Invoice {{DocumentNumber}}</p>"
            },
            MailServerConfiguration = new MailServerConfiguration
            {
                Host = "smtp.example.com",
                Port = 587,
                SenderName = "Blocks",
                SenderAddress = "no-reply@example.com",
                SenderUserName = "user",
                AccountPassword = "pass",
                IsEnableSnsConfiguration = false
            }
        };

        private MailToBeSent? _given;

        private void GivenMail(MailToBeSent? mail)
        {
            _given = mail;
        }

        private void GivenAttachments(params MailAttachment[] attachments) =>
            _resolver.Setup(r => r.ResolveAsync(It.IsAny<IEnumerable<string>?>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(attachments);

        private MailSentEvent? PublishedEvent()
        {
            var published = _messageClient.Invocations
                .Select(i => i.Arguments.FirstOrDefault())
                .OfType<ConsumerMessage<MailSentEvent>>()
                .LastOrDefault();

            return published?.Payload;
        }

        [Fact]
        public async Task ProcessSendMailAsync_BuildsAPlainMessageWhenThereAreNoAttachments()
        {
            GivenMail(AMail());
            GivenAttachments();

            var result = await CreateService().ProcessSendMailAsync(_given!);

            result.Should().BeTrue();
            _smtp.Sent.Should().NotBeNull();
            _smtp.Sent!.Subject.Should().Be("Invoice INV-2026-000043");
            _smtp.Sent.Attachments.Should().BeEmpty();
            _smtp.Sent.HtmlBody.Should().Contain("INV-2026-000043");
        }

        [Fact]
        public async Task ProcessSendMailAsync_PutsResolvedAttachmentsOnTheWire()
        {
            GivenMail(AMail("file-1"));
            GivenAttachments(new MailAttachment
            {
                FileName = "INV-2026-000043.pdf",
                ContentType = "application/pdf",
                Content = [1, 2, 3, 4]
            });

            var result = await CreateService().ProcessSendMailAsync(_given!);

            result.Should().BeTrue();

            var attachment = _smtp.Sent!.Attachments.Single();
            attachment.ContentDisposition.FileName.Should().Be("INV-2026-000043.pdf");
            attachment.ContentType.MimeType.Should().Be("application/pdf");
        }

        [Fact]
        public async Task ProcessSendMailAsync_FailsTheSendWhenAnAttachmentCannotBeResolved()
        {
            GivenMail(AMail("file-1"));
            _resolver.Setup(r => r.ResolveAsync(It.IsAny<IEnumerable<string>?>(), It.IsAny<CancellationToken>()))
                     .ThrowsAsync(new MailAttachmentException("no Download grant"));

            var result = await CreateService().ProcessSendMailAsync(_given!);

            // Delivering the mail without its attachment is the defect being fixed, so nothing is sent.
            result.Should().BeFalse();
            _smtp.Sent.Should().BeNull();
            PublishedEvent()!.Error.Should().Contain("no Download grant");
        }

        [Fact]
        public async Task ProcessSendMailAsync_InMemory_NeverReadsTheRepository()
        {
            GivenAttachments();

            var result = await CreateService().ProcessSendMailAsync(AMail());

            result.Should().BeTrue();
            _repository.Invocations.Should().BeEmpty();
        }

        [Fact]
        public async Task ProcessSendMailAsync_InMemory_MailWithoutTemplateOrServerConfigFailsAsIncomplete()
        {
            var mail = AMail();
            mail.EmailTemplate = null!;

            var result = await CreateService().ProcessSendMailAsync(mail);

            result.Should().BeFalse();
            _smtp.Sent.Should().BeNull();
            PublishedEvent()!.IsSuccess.Should().BeFalse();
            PublishedEvent()!.Error.Should().Be("The mail is missing its template or server configuration.");
        }

        [Fact]
        public async Task ProcessSendMailAsync_InMemory_SmtpFailureReturnsFalseWithTheGenericError()
        {
            GivenAttachments();
            _smtp.FailSend = true;

            var result = await CreateService().ProcessSendMailAsync(AMail());

            result.Should().BeFalse();
            PublishedEvent()!.Error.Should().Be("The SMTP server did not accept the message.");
        }

        [Fact]
        public async Task ProcessSendMailAsync_EchoesTheCorrelationIdOnTheStatusEvent()
        {
            var mail = AMail();
            mail.CorrelationId = "caller-abc";
            GivenMail(mail);
            GivenAttachments();

            await CreateService().ProcessSendMailAsync(_given!);

            var published = PublishedEvent()!;
            published.CorrelationId.Should().Be("caller-abc");
            published.IsSuccess.Should().BeTrue();
            published.ItemId.Should().Be(MailId);
            published.Purpose.Should().Be("subscription_invoice");
        }

        [Fact]
        public async Task ProcessSendMailAsync_StillReportsSuccessWhenTheStatusEventCannotBePublished()
        {
            GivenMail(AMail());
            GivenAttachments();
            _messageClient
                .Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<MailSentEvent>>()))
                .ThrowsAsync(new InvalidOperationException("bus down"));

            var result = await CreateService().ProcessSendMailAsync(_given!);

            // The mail was accepted by the SMTP server; a broken bus must not rewrite that outcome.
            result.Should().BeTrue();
        }

        [Fact]
        public async Task ProcessSendMailAsync_PublishesToTheMailStatusQueue()
        {
            GivenMail(AMail());
            GivenAttachments();

            await CreateService().ProcessSendMailAsync(_given!);

            _messageClient.Verify(
                m => m.SendToConsumerAsync(It.Is<ConsumerMessage<MailSentEvent>>(
                    c => c.ConsumerName == CommunicationConstants.MailStatusQueueName)),
                Times.Once);
        }

        [Fact]
        public async Task ProcessSendMailAsync_PublishesNothingWhenTheStatusEventIsDisabled()
        {
            GivenMail(AMail());
            GivenAttachments();
            _statusOptions.Enabled = false;

            var result = await CreateService().ProcessSendMailAsync(_given!);

            result.Should().BeTrue();
            _messageClient.Invocations.Should().BeEmpty();
        }

        [Fact]
        public async Task ProcessSendMailAsync_PublishesToTheConfiguredQueueName()
        {
            GivenMail(AMail());
            GivenAttachments();
            _statusOptions.QueueName = "some_other_listener";

            await CreateService().ProcessSendMailAsync(_given!);

            _messageClient.Verify(
                m => m.SendToConsumerAsync(It.Is<ConsumerMessage<MailSentEvent>>(
                    c => c.ConsumerName == "some_other_listener")),
                Times.Once);
        }

        // ======================= #371: AccountPasswordReference through the send =======================

        private const string Reference = "s-123";
        private const string SecretPassword = "abcdefghijklmnop";
        private const string TenantId = "tenant-a";

        private readonly CapturingLogger<SendMailService> _log = new();
        private readonly Mock<IBuiltInNotificationSender> _builtIn = new();
        private readonly RecordingNetSmtpClient _netSmtp = new();
        private readonly RecordingOffice365SmtpClient _o365Smtp = new();

        /// <summary>
        /// Every provider registered as in production, each on a recording transport, with the real
        /// resolver over <see cref="_secrets"/> (a strict mock: any unexpected read fails the send).
        /// </summary>
        private SendMailService CreateSecretAwareService(MailKitSmtpClient? mailKit = null)
        {
            _secrets ??= new Mock<Blocks.Secrets.ISecretService>(MockBehavior.Strict);

            var services = new ServiceCollection();
            services.AddSingleton<MailKitSmtpClient>(mailKit ?? _smtp);
            services.AddSingleton<MicrosoftSmtpClient>(_netSmtp);
            var provider = new SmtpClientProvider(services.BuildServiceProvider(), NullLogger<SmtpClientProvider>.Instance);
            var graph = new Mock<Office365GraphMailSender>(
                Mock.Of<IOffice365TokenProvider>(), Mock.Of<IHttpClientFactory>(), NullLogger<Office365GraphMailSender>.Instance);

            var registry = new OutboundMailSenderRegistry(
            [
                new AmazonSesMailSender(provider),
                new ZohoMailSender(provider),
                new GmailMailSender(provider),
                new Office365MailSender(graph.Object, _o365Smtp, NullLogger<Office365MailSender>.Instance)
            ]);

            return new SendMailService(
                _log,
                _repository.Object,
                registry,
                new MailSendResolvers(_resolver.Object, MailSecretsTestDoubles.PasswordResolver(_secrets)),
                _messageClient.Object,
                Options.Create(_statusOptions),
                _builtIn.Object);
        }

        private static MailToBeSent ReferencedMail(MailServiceProvider provider, string accountPassword = "", string? reference = Reference)
        {
            var mail = AMail();
            var config = mail.MailServerConfiguration;
            config.ItemId = "cfg-1";
            config.Provider = provider;
            config.AccountPassword = accountPassword;
            config.AccountPasswordReference = reference;
            if (provider == MailServiceProvider.Office365Smtp)
            {
                config.Host = "smtp.office365.com";
                config.Port = 587;
                config.SecurityMode = MailSecurityMode.StartTls;
                config.SenderUserName = "support@contoso.com";
            }

            return mail;
        }

        private static IDisposable InTenant(string tenantId = TenantId)
        {
            var previous = BlocksContext.GetContext();
            BlocksContext.SetContext(
                BlocksContext.Create(tenantId, [], "user", true, "", "", DateTime.MaxValue, "", [], "", "", "", "", "", tenantId),
                true);
            return new RestoreContext(previous);
        }

        private sealed class RestoreContext(BlocksContext? previous) : IDisposable
        {
            public void Dispose() => BlocksContext.SetContext(previous!, previous is not null);
        }

        private void GivenSecret(string value = SecretPassword, List<BlocksContext?>? seen = null) =>
            _secrets = MailSecretsTestDoubles.Answering(Reference, value, seen);

        private void GivenSecretFails(string failure) =>
            _secrets = MailSecretsTestDoubles.Throwing(Reference, MailSecretsTestDoubles.SecretFailure(failure, Reference));

        private int SmtpConnections => _smtp.Connects + _netSmtp.Created + _o365Smtp.Connects;

        private void AssertNoSecretMaterialLogged(params string[] secrets)
        {
            foreach (var secret in secrets.Append(Reference))
            {
                _log.All.Should().NotContain(secret);
            }
        }

        [Theory]
        [InlineData(MailServiceProvider.AmazonSes)]
        [InlineData(MailServiceProvider.Zoho)]
        [InlineData(MailServiceProvider.Gmail)]
        public async Task ReferencedRecord_MailKitTransport_AuthenticatesOnTheWireWithTheSecretValue(MailServiceProvider provider)
        {
            // H3 (E2) through the real MailKit client against a local SMTP server: the AUTH that
            // reaches the wire carries the secret value, read once in the send's tenant.
            using var _ = InTenant();
            using var server = new FakeSmtpServer();
            var seen = new List<BlocksContext?>();
            GivenSecret(seen: seen);
            GivenAttachments();
            var mail = ReferencedMail(provider);
            mail.MailServerConfiguration.Host = server.Host;
            mail.MailServerConfiguration.Port = server.Port;
            mail.MailServerConfiguration.EnableSSL = false;

            var result = await CreateSecretAwareService(new MailKitSmtpClient(NullLogger<MailKitSmtpClient>.Instance, new ConfigurationBuilder().Build()))
                .ProcessSendMailAsync(mail);

            result.Should().BeTrue(server.Describe());
            server.Credentials.Should().Equal(("user", SecretPassword));
            _secrets!.Reads().Should().Be(1);
            seen.Single()!.TenantId.Should().Be(TenantId);
            seen.Single()!.IsAuthenticated.Should().BeTrue();
            PublishedEvent()!.IsSuccess.Should().BeTrue();
        }

        [Theory]
        [InlineData(MailServiceProvider.AmazonSes)]
        [InlineData(MailServiceProvider.Zoho)]
        [InlineData(MailServiceProvider.Gmail)]
        public async Task ReferencedRecord_SystemNetTransport_BuildsItsCredentialFromTheSecretValue(MailServiceProvider provider)
        {
            // H3: the System.Net.Mail transport (SmtpClient = MsMailKit) takes the same value.
            using var _ = InTenant();
            GivenSecret();
            GivenAttachments();
            var mail = ReferencedMail(provider);
            mail.MailServerConfiguration.SmtpClient = global::Mail.DomainService.Entities.SmtpClient.MsMailKit;

            var result = await CreateSecretAwareService().ProcessSendMailAsync(mail);

            result.Should().BeTrue();
            _netSmtp.Passwords.Should().Equal(SecretPassword);
            _smtp.Connects.Should().Be(0);
            _secrets!.Reads().Should().Be(1);
        }

        [Fact]
        public async Task ReferencedOffice365PasswordRecord_PassesTheContract_AndAuthenticatesWithTheSecretValue()
        {
            // H4: the stored record has no password, only the reference.
            using var _ = InTenant();
            GivenSecret();
            GivenAttachments();

            var result = await CreateSecretAwareService().ProcessSendMailAsync(ReferencedMail(MailServiceProvider.Office365Smtp));

            result.Should().BeTrue();
            _o365Smtp.Credentials.Should().Equal(("support@contoso.com", SecretPassword));
            _secrets!.Reads().Should().Be(1);
        }

        [Theory]
        [InlineData(MailServiceProvider.AmazonSes)]
        [InlineData(MailServiceProvider.Zoho)]
        [InlineData(MailServiceProvider.Gmail)]
        [InlineData(MailServiceProvider.Office365Smtp)]
        public async Task LegacyRecord_AuthenticatesWithTheStoredPassword_AndMakesNoSecretCall(MailServiceProvider provider)
        {
            // H5, C10 (E1): no reference, 0 Blocks Secrets calls (the strict mock fails any).
            using var _ = InTenant();
            _secrets = new Mock<Blocks.Secrets.ISecretService>(MockBehavior.Strict);
            GivenAttachments();

            var result = await CreateSecretAwareService().ProcessSendMailAsync(ReferencedMail(provider, accountPassword: "app-pass-1", reference: null));

            result.Should().BeTrue();
            _smtp.Credentials.Concat(_o365Smtp.Credentials).Should().Equal(
                (provider == MailServiceProvider.Office365Smtp ? "support@contoso.com" : "user", "app-pass-1"));
            _secrets.Invocations.Should().BeEmpty();
        }

        [Fact]
        public async Task LegacyRecord_WithAnEmptyReference_IsTreatedAsLegacy()
        {
            using var _ = InTenant();
            _secrets = new Mock<Blocks.Secrets.ISecretService>(MockBehavior.Strict);
            GivenAttachments();

            var result = await CreateSecretAwareService().ProcessSendMailAsync(ReferencedMail(MailServiceProvider.Zoho, "app-pass-1", reference: ""));

            result.Should().BeTrue();
            _smtp.Credentials.Should().Equal(("user", "app-pass-1"));
            _secrets.Invocations.Should().BeEmpty();
        }

        [Fact]
        public async Task TestMail_ThroughAReferencedRecord_ResolvesLikeANormalSend()
        {
            // H11
            using var _ = InTenant();
            GivenSecret();
            GivenAttachments();
            var mail = ReferencedMail(MailServiceProvider.Zoho);
            mail.IsTestMail = true;

            var result = await CreateSecretAwareService().ProcessSendMailAsync(mail);

            result.Should().BeTrue();
            _smtp.Credentials.Should().Equal(("user", SecretPassword));
            _secrets!.Reads().Should().Be(1);
        }

        [Fact]
        public async Task BothReferenceAndPassword_UsesOnlyTheReference()
        {
            // C3 (E2 with a stale plaintext): the reference wins.
            using var _ = InTenant();
            GivenSecret();
            GivenAttachments();

            var result = await CreateSecretAwareService().ProcessSendMailAsync(ReferencedMail(MailServiceProvider.Gmail, accountPassword: "old-pass"));

            result.Should().BeTrue();
            _smtp.Credentials.Should().Equal(("user", SecretPassword));
        }

        [Fact]
        public async Task OneReadPerSendAttempt_AndThePlaintextDoesNotOutliveTheSend()
        {
            // C10, C4: two attempts with the same record read twice (no cross-send cache), and the
            // record is left as it was loaded.
            using var _ = InTenant();
            GivenSecret();
            GivenAttachments();
            var service = CreateSecretAwareService();
            var mail = ReferencedMail(MailServiceProvider.Zoho, accountPassword: "");

            (await service.ProcessSendMailAsync(mail)).Should().BeTrue();
            mail.MailServerConfiguration.AccountPassword.Should().BeEmpty();
            (await service.ProcessSendMailAsync(mail)).Should().BeTrue();

            _secrets!.Reads().Should().Be(2);
            _smtp.Credentials.Should().Equal(("user", SecretPassword), ("user", SecretPassword));
            mail.MailServerConfiguration.AccountPassword.Should().BeEmpty();
            mail.MailServerConfiguration.AccountPasswordReference.Should().Be(Reference);
        }

        [Fact]
        public async Task ThePlaintextIsPutBack_EvenWhenTheTransportThrows()
        {
            // C4: the record never keeps the plaintext past the send attempt.
            using var _ = InTenant();
            GivenSecret();
            GivenAttachments();
            var sender = new Mock<IOutboundMailSender>();
            sender.SetupGet(s => s.Provider).Returns(MailServiceProvider.Zoho);
            sender.Setup(s => s.SendAsync(It.IsAny<MailToBeSent>(), It.IsAny<MailBody>()))
                  .Callback<MailToBeSent, MailBody>((m, _) => m.MailServerConfiguration.AccountPassword.Should().Be(SecretPassword))
                  .ThrowsAsync(new InvalidOperationException("transport bug"));
            var service = new SendMailService(_log, _repository.Object, new OutboundMailSenderRegistry([sender.Object]), new MailSendResolvers(_resolver.Object, MailSecretsTestDoubles.PasswordResolver(_secrets)),
                _messageClient.Object, Options.Create(_statusOptions));
            var mail = ReferencedMail(MailServiceProvider.Zoho, accountPassword: "old-pass");

            var act = () => service.ProcessSendMailAsync(mail);

            await act.Should().ThrowAsync<InvalidOperationException>();
            mail.MailServerConfiguration.AccountPassword.Should().Be("old-pass");
        }

        [Theory]
        [InlineData(nameof(Blocks.Secrets.SecretNotFoundException), "credential")]
        [InlineData(nameof(Blocks.Secrets.SecretStateException), "credential")]
        [InlineData(nameof(Blocks.Secrets.SecretAccessDeniedException), "credential")]
        [InlineData(nameof(Blocks.Secrets.SecretVaultException), "vault_unavailable")]
        [InlineData(nameof(Blocks.Secrets.SecretValidationException), "unexpected")]
        [InlineData(nameof(InvalidOperationException), "unexpected")]
        public async Task SecretReadFailure_FailsTheSendBeforeAnySmtpConnection_WithoutFallingBack(string failure, string failureClass)
        {
            // C1 (E3), C2 (E4), C3, C4, Q4: false, FailedToAcceptError, no SMTP connect, admins
            // notified, one MAIL FAILED (credential) line free of the password and the reference.
            using var _ = InTenant();
            GivenSecretFails(failure);
            GivenAttachments();

            foreach (var provider in new[] { MailServiceProvider.Zoho, MailServiceProvider.Office365Smtp })
            {
                var result = await CreateSecretAwareService().ProcessSendMailAsync(ReferencedMail(provider, accountPassword: "old-pass"));

                result.Should().BeFalse();
                PublishedEvent()!.IsSuccess.Should().BeFalse();
                PublishedEvent()!.Error.Should().Be("The SMTP server did not accept the message.");
            }

            SmtpConnections.Should().Be(0);
            _smtp.Credentials.Concat(_o365Smtp.Credentials).Should().BeEmpty();
            // C10: exactly one read per send attempt (two sends), so a retry on failure is caught.
            _secrets!.Reads().Should().Be(2);
            _builtIn.Verify(b => b.SendToTenantAdminsAsync(TenantId, It.IsAny<string>(), It.IsAny<BuiltInNotificationPayload>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
            var failures = _log.Lines.Where(l => l.Contains("MAIL FAILED (credential)", StringComparison.Ordinal)).ToList();
            failures.Should().HaveCount(2);
            failures.Should().OnlyContain(l => l.Contains($"failureClass={failureClass}", StringComparison.Ordinal)
                                               && l.Contains($"exceptionType={failure}", StringComparison.Ordinal)
                                               && l.Contains("itemId=mail-1", StringComparison.Ordinal)
                                               && l.Contains("configId=cfg-1", StringComparison.Ordinal));
            AssertNoSecretMaterialLogged("old-pass");
            PublishedEvent()!.Error.Should().NotContain(Reference);
        }

        [Fact]
        public async Task SecretReadFailure_OfATestMail_NotifiesNoAdmin()
        {
            // C1: admin notification "as for any failed non-test send".
            using var _ = InTenant();
            GivenSecretFails(nameof(Blocks.Secrets.SecretNotFoundException));
            GivenAttachments();
            var mail = ReferencedMail(MailServiceProvider.Zoho);
            mail.IsTestMail = true;

            (await CreateSecretAwareService().ProcessSendMailAsync(mail)).Should().BeFalse();

            _builtIn.Invocations.Should().BeEmpty();
        }

        [Fact]
        public async Task VaultOutage_LeavesOtherQueuedMailsUnaffected()
        {
            // C2: the next mail, through a legacy record, still sends.
            using var _ = InTenant();
            GivenSecretFails(nameof(Blocks.Secrets.SecretVaultException));
            GivenAttachments();
            var service = CreateSecretAwareService();

            (await service.ProcessSendMailAsync(ReferencedMail(MailServiceProvider.Zoho))).Should().BeFalse();
            (await service.ProcessSendMailAsync(ReferencedMail(MailServiceProvider.Zoho, "app-pass-1", reference: null))).Should().BeTrue();

            _smtp.Credentials.Should().Equal(("user", "app-pass-1"));
        }

        [Fact]
        public async Task SecretReadFailure_WithNoAmbientTenant_FailsWithoutARead()
        {
            // C5: no tenant may stand in for the send's own.
            _secrets = new Mock<Blocks.Secrets.ISecretService>(MockBehavior.Strict);
            GivenAttachments();

            (await CreateSecretAwareService().ProcessSendMailAsync(ReferencedMail(MailServiceProvider.Zoho))).Should().BeFalse();

            _secrets.Invocations.Should().BeEmpty();
            SmtpConnections.Should().Be(0);
            _log.All.Should().Contain("failureClass=unexpected");
        }

        [Fact]
        public async Task TwoTenantsSendingConcurrently_EachReadsInItsOwnTenant()
        {
            // C5 through the orchestrator: the read tenant is the ambient tenant of each send. Neither
            // read completes until both are in flight, so the two sends really overlap.
            var tenants = new System.Collections.Concurrent.ConcurrentBag<(string Reference, string Tenant)>();
            var bothReading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var reading = 0;
            _secrets = new Mock<Blocks.Secrets.ISecretService>(MockBehavior.Strict);
            foreach (var reference in new[] { "ref-a", "ref-b" })
            {
                _secrets.Setup(s => s.GetValueAsync(reference, It.IsAny<CancellationToken>()))
                        .Returns(async () =>
                        {
                            tenants.Add((reference, BlocksContext.GetContext()!.TenantId));
                            if (Interlocked.Increment(ref reading) == 2)
                            {
                                bothReading.SetResult();
                            }

                            await bothReading.Task.WaitAsync(TimeSpan.FromSeconds(10));
                            return "pass-" + reference;
                        });
            }
            GivenAttachments();
            var service = CreateSecretAwareService();

            Task<bool> Send(string tenant, string reference) => Task.Run(async () =>
            {
                using var _ = InTenant(tenant);
                return await service.ProcessSendMailAsync(ReferencedMail(MailServiceProvider.Zoho, reference: reference));
            });

            var results = await Task.WhenAll(Send("tenant-a", "ref-a"), Send("tenant-b", "ref-b"));

            results.Should().OnlyContain(r => r);
            tenants.Should().BeEquivalentTo(new[] { ("ref-a", "tenant-a"), ("ref-b", "tenant-b") });
        }

        [Fact]
        public async Task MissingTemplate_MakesNoSecretRead()
        {
            // C9
            using var _ = InTenant();
            _secrets = new Mock<Blocks.Secrets.ISecretService>(MockBehavior.Strict);
            var mail = ReferencedMail(MailServiceProvider.Zoho);
            mail.EmailTemplate = null!;

            (await CreateSecretAwareService().ProcessSendMailAsync(mail)).Should().BeFalse();

            _secrets.Invocations.Should().BeEmpty();
            PublishedEvent()!.Error.Should().Be("The mail is missing its template or server configuration.");
        }

        [Fact]
        public async Task UnregisteredProvider_MakesNoSecretRead()
        {
            // C9
            using var _ = InTenant();
            _secrets = new Mock<Blocks.Secrets.ISecretService>(MockBehavior.Strict);

            var result = await CreateSecretAwareService().ProcessSendMailAsync(ReferencedMail((MailServiceProvider)99));

            result.Should().BeFalse();
            _secrets.Invocations.Should().BeEmpty();
            PublishedEvent()!.Error.Should().Be("The SMTP server did not accept the message.");
        }

        [Fact]
        public async Task UnresolvableAttachment_MakesNoSecretRead()
        {
            // C9, Q5: resolution runs after the attachment check.
            using var _ = InTenant();
            _secrets = new Mock<Blocks.Secrets.ISecretService>(MockBehavior.Strict);
            _resolver.Setup(r => r.ResolveAsync(It.IsAny<IEnumerable<string>?>(), It.IsAny<CancellationToken>()))
                     .ThrowsAsync(new MailAttachmentException("no Download grant"));

            (await CreateSecretAwareService().ProcessSendMailAsync(ReferencedMail(MailServiceProvider.Zoho))).Should().BeFalse();

            _secrets.Invocations.Should().BeEmpty();
        }

        [Fact]
        public async Task OAuthOffice365Record_WithAStrayReference_MakesNoAccountPasswordRead()
        {
            // H10, C10: Graph records are not touched by the password resolver.
            using var _ = InTenant();
            _secrets = new Mock<Blocks.Secrets.ISecretService>(MockBehavior.Strict);
            GivenAttachments();
            var mail = ReferencedMail(MailServiceProvider.Office365Smtp);
            mail.MailServerConfiguration.AuthenticationType = MailAuthenticationType.OAuthClientCredentials;
            var sender = new Mock<IOutboundMailSender>();
            sender.SetupGet(s => s.Provider).Returns(MailServiceProvider.Office365Smtp);
            sender.Setup(s => s.SendAsync(It.IsAny<MailToBeSent>(), It.IsAny<MailBody>())).ReturnsAsync(true);
            var service = new SendMailService(_log, _repository.Object, new OutboundMailSenderRegistry([sender.Object]), new MailSendResolvers(_resolver.Object, MailSecretsTestDoubles.PasswordResolver(_secrets)),
                _messageClient.Object, Options.Create(_statusOptions));

            (await service.ProcessSendMailAsync(mail)).Should().BeTrue();

            _secrets.Invocations.Should().BeEmpty();
        }

        /// <summary>
        /// A real <see cref="MicrosoftSmtpClient"/> whose System.Net client is replaced at the point
        /// the credential is built: records the password it would authenticate with.
        /// </summary>
        private sealed class RecordingNetSmtpClient : MicrosoftSmtpClient
        {
            public RecordingNetSmtpClient()
                : base(NullLogger<MicrosoftSmtpClient>.Instance, new ConfigurationBuilder().Build())
            {
            }

            public List<string> Passwords { get; } = [];
            public int Created => Passwords.Count;

            protected override INetSmtpClient CreateSmtpClient(MailServerConfiguration config)
            {
                Passwords.Add(config.AccountPassword);
                return new Sink();
            }

            private sealed class Sink : INetSmtpClient
            {
                public Task SendMailAsync(System.Net.Mail.MailMessage message) => Task.CompletedTask;
                public void Dispose() { }
            }
        }

        /// <summary>A real <see cref="Office365SmtpClient"/> with only the socket swapped out.</summary>
        private sealed class RecordingOffice365SmtpClient : Office365SmtpClient
        {
            public RecordingOffice365SmtpClient() : base(NullLogger<Office365SmtpClient>.Instance)
            {
            }

            public int Connects { get; private set; }
            public List<(string User, string Password)> Credentials { get; } = [];

            protected override IMailKitSmtpClient CreateSmtpClient() => new Session(this);

            private sealed class Session(RecordingOffice365SmtpClient owner) : IMailKitSmtpClient
            {
                public Task ConnectAsync(string host, int port, bool useSsl) { owner.Connects++; return Task.CompletedTask; }
                public Task ConnectAsync(string host, int port, SecureSocketOptions socketOptions) { owner.Connects++; return Task.CompletedTask; }
                public Task AuthenticateAsync(string userName, string password) { owner.Credentials.Add((userName, password)); return Task.CompletedTask; }
                public Task AuthenticateAsync(SaslMechanism mechanism) => Task.CompletedTask;
                public Task SendAsync(MimeMessage message) => Task.CompletedTask;
                public Task DisconnectAsync(bool quit) => Task.CompletedTask;
                public void Dispose() { }
            }
        }

        /// <summary>
        /// A real <see cref="MailKitSmtpClient"/> with only the socket swapped out, so the message
        /// under assertion is the one MimeKit actually built.
        /// </summary>
        private sealed class RecordingSmtpClient : MailKitSmtpClient
        {
            public RecordingSmtpClient()
                : base(NullLogger<MailKitSmtpClient>.Instance, new ConfigurationBuilder().Build())
            {
            }

            public MimeMessage? Sent { get; private set; }
            public bool FailSend { get; set; }
            public int Connects { get; private set; }
            public List<(string User, string Password)> Credentials { get; } = [];

            protected override IMailKitSmtpClient CreateSmtpClient() => new Recorder(this);

            private sealed class Recorder : IMailKitSmtpClient
            {
                private readonly RecordingSmtpClient _owner;

                public Recorder(RecordingSmtpClient owner) => _owner = owner;

                public Task ConnectAsync(string host, int port, bool useSsl)
                {
                    _owner.Connects++;
                    return Task.CompletedTask;
                }

                public Task AuthenticateAsync(string userName, string password)
                {
                    _owner.Credentials.Add((userName, password));
                    return Task.CompletedTask;
                }

                public Task ConnectAsync(string host, int port, SecureSocketOptions socketOptions) => Task.CompletedTask;
                public Task AuthenticateAsync(SaslMechanism mechanism) => Task.CompletedTask;
                public Task DisconnectAsync(bool quit) => Task.CompletedTask;
                public void Dispose() { }

                public Task SendAsync(MimeMessage message)
                {
                    if (_owner.FailSend)
                    {
                        throw new InvalidOperationException("smtp down");
                    }

                    _owner.Sent = message;
                    return Task.CompletedTask;
                }
            }
        }
    }
}
