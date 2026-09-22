using Blocks.Genesis;
using FluentAssertions;
using Mail.DomainService.Dtos;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Mails.Strategies;
using Mail.DomainService.Services;
using Mail.DomainService.Shared.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace XUnitTest.Mail
{
    /// <summary>
    /// C6: an unregistered provider is a reported send failure, not an exception escaping the
    /// orchestrator.
    /// </summary>
    /// <remarks>
    /// This is the regression that matters most here. The resolver used to hand back null for an
    /// unknown provider and the very next line dereferenced it, so a single bad record took down
    /// the queue consumer instead of failing one send.
    /// </remarks>
    public class UnregisteredProviderSendTests
    {
        private readonly Mock<IMailRepository> _repository = new();
        private readonly Mock<IMailAttachmentResolver> _resolver = new();
        private readonly Mock<IMessageClient> _messageClient = new();

        private SendMailService CreateService(params IOutboundMailSender[] senders) =>
            new(NullLogger<SendMailService>.Instance,
                _repository.Object,
                new OutboundMailSenderRegistry(senders),
                _resolver.Object,
                _messageClient.Object,
                Options.Create(new MailStatusEventOptions()));

        private static MailToBeSent AMail(MailServiceProvider provider) => new()
        {
            ItemId = "mail-1",
            Name = "purpose",
            Language = "en-US",
            To = ["someone@example.com"],
            Attachments = [],
            MailServerConfiguration = new MailServerConfiguration { ItemId = "cfg-1", Provider = provider },
            EmailTemplate = new EmailTemplate { Name = "t", TemplateSubject = "s", TemplateBody = "b" }
        };

        [Fact]
        public async Task Send_ProviderWithNoRegisteredSender_ReportsAFailureWithoutThrowing()
        {
            _repository.Setup(r => r.GetMailToBeSent("mail-1")).ReturnsAsync(AMail(MailServiceProvider.Office365Smtp));

            MailSentEvent? published = null;
            _messageClient
                .Setup(c => c.SendToConsumerAsync(It.IsAny<ConsumerMessage<MailSentEvent>>()))
                .Callback<ConsumerMessage<MailSentEvent>>(m => published = m.Payload)
                .Returns(Task.CompletedTask);

            // Only the legacy senders are registered.
            var service = CreateService(
                new AmazonSesMailSender(new SmtpClientProvider(new Mock<IServiceProvider>().Object, NullLogger<SmtpClientProvider>.Instance)));

            var result = await service.ProcessSendMailAsync(new SendEmailEvent { ItemId = "mail-1" });

            result.Should().BeFalse();

            // The public contract is unchanged: the same generic error every other failure gives.
            published.Should().NotBeNull();
            published!.IsSuccess.Should().BeFalse();
            published.Error.Should().Be("The SMTP server did not accept the message.");

            // And nothing was resolved or dialled on the way there.
            _resolver.Verify(r => r.ResolveAsync(It.IsAny<IEnumerable<string>>()), Times.Never);
        }
    }
}
