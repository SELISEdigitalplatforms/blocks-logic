using Blocks.Genesis;
using FluentAssertions;
using Mail.DomainService.Mails;
using Moq;
using Worker.Consumers.Mail;

namespace XUnitTest.Worker.Consumers
{
    /// <summary>SPEC10 H3: a queued SendMail takes the queue entry, which always uses the Default organization.</summary>
    public class SendConsumerTests
    {
        [Fact]
        public async Task Consume_UsesTheQueueEntry_NotTheHttpSend()
        {
            var mailService = new Mock<IMailService>();
            mailService.Setup(s => s.ProcessQueuedMailAsync(It.IsAny<SendMail>())).ReturnsAsync(new BaseMutationResponse { IsSuccess = true });
            var message = new SendMail { Purpose = "UserInvite", Language = "en-US", To = ["user@app.test"] };

            await new SendConsumer(mailService.Object).Consume(message);

            mailService.Verify(s => s.ProcessQueuedMailAsync(message), Times.Once);
            mailService.Verify(s => s.ProcessMailAsync(It.IsAny<SendMail>()), Times.Never);
        }
    }
}
