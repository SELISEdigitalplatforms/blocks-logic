using Blocks.Genesis;
using FluentAssertions;
using Mail.DomainService.Dtos;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Services;
using Mail.DomainService.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using Moq;

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

        private SendMailService CreateService()
        {
            var services = new ServiceCollection();
            services.AddSingleton<MailKitSmtpClient>(_smtp);

            var provider = new SmtpClientProvider(
                services.BuildServiceProvider(),
                NullLogger<SmtpClientProvider>.Instance);

            return new SendMailService(
                NullLogger<SendMailService>.Instance,
                _repository.Object,
                provider,
                _resolver.Object,
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

        private void GivenMail(MailToBeSent? mail) =>
            _repository.Setup(r => r.GetMailToBeSent(MailId)).ReturnsAsync(mail!);

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

            var result = await CreateService().ProcessSendMailAsync(new SendEmailEvent { ItemId = MailId });

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

            var result = await CreateService().ProcessSendMailAsync(new SendEmailEvent { ItemId = MailId });

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

            var result = await CreateService().ProcessSendMailAsync(new SendEmailEvent { ItemId = MailId });

            // Delivering the mail without its attachment is the defect being fixed, so nothing is sent.
            result.Should().BeFalse();
            _smtp.Sent.Should().BeNull();
            PublishedEvent()!.Error.Should().Contain("no Download grant");
        }

        [Fact]
        public async Task ProcessSendMailAsync_ReturnsFalseWhenTheMailRecordIsMissing()
        {
            GivenMail(null);

            var result = await CreateService().ProcessSendMailAsync(new SendEmailEvent { ItemId = MailId });

            result.Should().BeFalse();
            PublishedEvent()!.IsSuccess.Should().BeFalse();
        }

        [Fact]
        public async Task ProcessSendMailAsync_EchoesTheCorrelationIdOnTheStatusEvent()
        {
            var mail = AMail();
            mail.CorrelationId = "caller-abc";
            GivenMail(mail);
            GivenAttachments();

            await CreateService().ProcessSendMailAsync(new SendEmailEvent { ItemId = MailId });

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

            var result = await CreateService().ProcessSendMailAsync(new SendEmailEvent { ItemId = MailId });

            // The mail was accepted by the SMTP server; a broken bus must not rewrite that outcome.
            result.Should().BeTrue();
        }

        [Fact]
        public async Task ProcessSendMailAsync_PublishesToTheMailStatusQueue()
        {
            GivenMail(AMail());
            GivenAttachments();

            await CreateService().ProcessSendMailAsync(new SendEmailEvent { ItemId = MailId });

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

            var result = await CreateService().ProcessSendMailAsync(new SendEmailEvent { ItemId = MailId });

            result.Should().BeTrue();
            _messageClient.Invocations.Should().BeEmpty();
        }

        [Fact]
        public async Task ProcessSendMailAsync_PublishesToTheConfiguredQueueName()
        {
            GivenMail(AMail());
            GivenAttachments();
            _statusOptions.QueueName = "some_other_listener";

            await CreateService().ProcessSendMailAsync(new SendEmailEvent { ItemId = MailId });

            _messageClient.Verify(
                m => m.SendToConsumerAsync(It.Is<ConsumerMessage<MailSentEvent>>(
                    c => c.ConsumerName == "some_other_listener")),
                Times.Once);
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

            protected override IMailKitSmtpClient CreateSmtpClient() => new Recorder(this);

            private sealed class Recorder : IMailKitSmtpClient
            {
                private readonly RecordingSmtpClient _owner;

                public Recorder(RecordingSmtpClient owner) => _owner = owner;

                public Task ConnectAsync(string host, int port, bool useSsl) => Task.CompletedTask;
                public Task AuthenticateAsync(string userName, string password) => Task.CompletedTask;
                public Task DisconnectAsync(bool quit) => Task.CompletedTask;
                public void Dispose() { }

                public Task SendAsync(MimeMessage message)
                {
                    _owner.Sent = message;
                    return Task.CompletedTask;
                }
            }
        }
    }
}
