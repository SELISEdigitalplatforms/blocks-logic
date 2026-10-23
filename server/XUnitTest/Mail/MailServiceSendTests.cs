using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Blocks.Genesis;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Services;
using Moq;
using XUnitTest.TestHelpers;

namespace XUnitTest.Mail
{
    /// <summary>
    /// SPEC1 Step A: the mail is passed to the send service in memory. It is never stored or read back.
    /// </summary>
    public class MailServiceSendTests
    {
        private readonly Mock<IValidator<MailToBeSent>> _validator = new();
        private readonly Mock<IMailRepository> _repository = new(MockBehavior.Strict);
        private readonly Mock<ISendMailService> _sendMailService = new();
        private readonly CapturingLogger<MailService> _logger = new();

        private MailService CreateService() => new(
            _validator.Object,
            Mock.Of<IMessageClient>(),
            _repository.Object,
            _sendMailService.Object,
            _logger);

        private static MailToBeSent AMail() => new()
        {
            ItemId = "mail-1",
            Name = "purpose",
            Language = "en-US",
            To = ["someone@example.com"],
            Attachments = ["f1", "f2"],
            CorrelationId = "corr-1"
        };

        [Fact]
        public async Task ValidMail_IsPassedInMemoryToTheSendService_AndNeverStored()
        {
            var mail = AMail();
            _validator.Setup(v => v.ValidateAsync(mail, default)).ReturnsAsync(new ValidationResult());
            _sendMailService.Setup(s => s.ProcessSendMailAsync(It.IsAny<MailToBeSent>())).ReturnsAsync(true);

            var response = await CreateService().ProcessMailSent(mail);

            response.IsSuccess.Should().BeTrue();
            _sendMailService.Verify(s => s.ProcessSendMailAsync(It.Is<MailToBeSent>(m => ReferenceEquals(m, mail))), Times.Once);
            // Strict repository mock: any call (insert or read-back) would have thrown.
            _repository.Invocations.Should().BeEmpty();
        }

        [Fact]
        public async Task ValidMail_LogsMailPersistedWithIdPurposeRecipientsAttachmentsAndCorrelationId()
        {
            var mail = AMail();
            _validator.Setup(v => v.ValidateAsync(mail, default)).ReturnsAsync(new ValidationResult());
            _sendMailService.Setup(s => s.ProcessSendMailAsync(It.IsAny<MailToBeSent>())).ReturnsAsync(true);

            await CreateService().ProcessMailSent(mail);

            var line = _logger.Lines.Should().ContainSingle(l => l.StartsWith("MAIL PERSISTED:")).Subject;
            line.Should().Contain("itemId=mail-1").And.Contain("purpose=purpose")
                .And.Contain("attachments=2").And.Contain("correlationId=corr-1");
            line.Should().NotContain("someone@example.com");
        }

        [Fact]
        public async Task SendFalse_ReturnsTheSameErrorResponseAsBefore()
        {
            var mail = AMail();
            _validator.Setup(v => v.ValidateAsync(mail, default)).ReturnsAsync(new ValidationResult());
            _sendMailService.Setup(s => s.ProcessSendMailAsync(It.IsAny<MailToBeSent>())).ReturnsAsync(false);

            var response = await CreateService().ProcessMailSent(mail);

            response.IsSuccess.Should().BeFalse();
            response.Errors.Should().ContainKey("Send").WhoseValue.Should().Be("The mail was not sent. Mail id: mail-1");
        }

        [Fact]
        public async Task InvalidMail_IsRejectedAndNeverSent()
        {
            var mail = AMail();
            _validator.Setup(v => v.ValidateAsync(mail, default))
                .ReturnsAsync(new ValidationResult([new ValidationFailure("To", "bad")]));

            var response = await CreateService().ProcessMailSent(mail);

            response.IsSuccess.Should().BeFalse();
            response.Errors.Should().ContainKey("To");
            _sendMailService.Verify(s => s.ProcessSendMailAsync(It.IsAny<MailToBeSent>()), Times.Never);
            _repository.Invocations.Should().BeEmpty();
        }

    }
}
