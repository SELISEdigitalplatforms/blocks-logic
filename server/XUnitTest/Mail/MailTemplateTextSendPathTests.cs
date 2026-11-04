using Blocks.Genesis;
using BlocksTemplate.Api.Controllers;
using EmailTemplate = Blocks.Genesis.Entities.Os.EmailTemplate;
using FluentAssertions;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using Worker.Consumers.Mail;
using XUnitTest.TestHelpers;

namespace XUnitTest.Mail
{
    /// <summary>
    /// #402 N1 on every entry point: <c>Mail/Send</c>, <c>Mail/SendToAny</c> (test mail) and the queue consumer, through the
    /// real <see cref="MailService"/> and <see cref="EmailValidator"/>. A template without a subject or body is a 400 (or a
    /// logged rejection on the queue) and nothing is sent; a complete template is sent as before.
    /// </summary>
    public sealed class MailTemplateTextSendPathTests : IDisposable
    {
        private const string Caller = "caller@app.test";
        private const string NoSubject = "The email template for this purpose and language has no subject.";
        private const string NoBody = "The email template for this purpose and language has no body.";

        private readonly Mock<IMailRepository> _repository = new();
        private readonly Mock<ISendMailService> _sender = new();
        private readonly CapturingLogger<MailService> _logger = new();
        private EmailTemplate _template = Template("Welcome {{name}}", "<p>Hello</p>");

        public MailTemplateTextSendPathTests()
        {
            BlocksContext.SetContext(BlocksContext.Create("t1", [], "caller-user", true, "/api/mail", "default", DateTime.UtcNow.AddMinutes(5),
                Caller, [], "caller", "", "Caller", "token", "t1"));
            _repository.Setup(r => r.GetEmailAdressOfUsers(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync((IEnumerable<string> users) => users?.ToList() ?? []);
            ServeTemplateAndConfiguration();
            _sender.Setup(s => s.ProcessSendMailAsync(It.IsAny<MailToBeSent>())).ReturnsAsync(true);
        }

        public void Dispose() => BlocksContext.ClearContext();

        /// <summary>
        /// The only coupling to how MailService looks the template and configuration up. #406 (open in parallel)
        /// replaces these two repository methods; whichever PR merges second retargets this method only.
        /// </summary>
        private void ServeTemplateAndConfiguration()
        {
            _repository.Setup(r => r.GetEmailTemplateByPurpose("welcome", "en", It.IsAny<string>())).ReturnsAsync(() => _template);
            _repository.Setup(r => r.GetMailServerConfigurationByPurpose("welcome", "en", It.IsAny<string>()))
                .ReturnsAsync(new MailServerConfiguration { ItemId = "c1" });
        }

        private static EmailTemplate Template(string? subject, string? body) =>
            new() { ItemId = "t1", Name = "welcome", Language = "en", TemplateSubject = subject!, TemplateBody = body! };

        private MailService Service() => new(
            new EmailValidator(new CommonEmailValidator(_repository.Object), Options.Create(new MailAttachmentOptions { ValidateAttachmentsExist = false })),
            Mock.Of<IMessageClient>(),
            _repository.Object,
            _sender.Object,
            _logger);

        private static SendMail Send() => new()
        {
            Purpose = "welcome",
            Language = "en",
            To = ["user-1"],
            SubjectDataContext = new() { ["name"] = "A" },
        };

        private void NothingWasSent() => _sender.Verify(s => s.ProcessSendMailAsync(It.IsAny<MailToBeSent>()), Times.Never);

        [Fact]
        public async Task H9_H10_Send_CompleteTemplate_IsSentAsBefore()
        {
            var result = await new MailController(Service()).Send(Send());

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            ok.Value.Should().BeOfType<BaseMutationResponse>().Which.IsSuccess.Should().BeTrue();
            _sender.Verify(s => s.ProcessSendMailAsync(It.Is<MailToBeSent>(m =>
                m.EmailTemplate.TemplateSubject == "Welcome {{name}}" && m.SubjectDataContext["name"] == "A")), Times.Once);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public async Task C6_C8_Send_TemplateWithoutSubject_Is400WithOnlyTheSubjectError(string? subject)
        {
            _template = Template(subject, "<p>Hello</p>");

            var result = await new MailController(Service()).Send(Send());

            var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            var body = badRequest.Value.Should().BeOfType<BaseMutationResponse>().Subject;
            body.IsSuccess.Should().BeFalse();
            body.Errors.Should().BeEquivalentTo(new Dictionary<string, string> { ["TemplateSubject"] = NoSubject });
            NothingWasSent();
        }

        [Fact]
        public async Task C7_C9_Send_TemplateWithoutBody_Is400WithOnlyTheBodyError()
        {
            _template = Template("Welcome", null);
            var request = Send();
            request.BodyDataContext = new() { ["name"] = "A" };

            var result = await new MailController(Service()).Send(request);

            result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should().BeOfType<BaseMutationResponse>()
                .Which.Errors.Should().BeEquivalentTo(new Dictionary<string, string> { ["TemplateBody"] = NoBody });
            NothingWasSent();
        }

        [Fact]
        public async Task C10_SendToAny_TestMailToTheCaller_WithoutSubject_Is400()
        {
            _template = Template(null, "<p>Hello</p>");

            var result = await new MailController(Service()).SendToAny(
                new SendMailToAny { IsTestMail = true, Purpose = "welcome", Language = "en", To = [Caller] });

            result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should().BeOfType<BaseMutationResponse>()
                .Which.Errors.Should().BeEquivalentTo(new Dictionary<string, string> { ["TemplateSubject"] = NoSubject });
            NothingWasSent();
        }

        [Fact]
        public async Task H11_SendToAny_TestMailToTheCaller_CompleteTemplate_IsSent()
        {
            var result = await new MailController(Service()).SendToAny(
                new SendMailToAny { IsTestMail = true, Purpose = "welcome", Language = "en", To = [Caller] });

            result.Should().BeOfType<OkObjectResult>();
            _sender.Verify(s => s.ProcessSendMailAsync(It.Is<MailToBeSent>(m => m.IsTestMail)), Times.Once);
        }

        [Theory]
        [InlineData(null, "<p>Hello</p>", "TemplateSubject: " + NoSubject)]
        [InlineData("Welcome", null, "TemplateBody: " + NoBody)]
        public async Task C11_QueueConsumer_TemplateWithoutSubjectOrBody_LogsTheRejection_CompletesAndSendsNothing(
            string? subject, string? body, string reason)
        {
            _template = Template(subject, body);

            var consume = () => new SendConsumer(Service()).Consume(Send());

            await consume.Should().NotThrowAsync();
            _logger.Lines.Should().ContainSingle(l => l.StartsWith("MAIL REJECTED (validation):"))
                .Which.Should().Contain(reason);
            NothingWasSent();
        }

        [Fact]
        public async Task H12_QueueConsumer_CompleteTemplate_IsSent()
        {
            await new SendConsumer(Service()).Consume(Send());

            _sender.Verify(s => s.ProcessSendMailAsync(It.IsAny<MailToBeSent>()), Times.Once);
            _logger.Lines.Should().NotContain(l => l.StartsWith("MAIL REJECTED"));
        }
    }
}
