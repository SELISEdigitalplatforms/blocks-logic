using EmailTemplate = Blocks.Genesis.Entities.Os.EmailTemplate;
using FluentAssertions;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Services;
using Microsoft.Extensions.Options;
using Moq;

namespace XUnitTest.Mail
{
    /// <summary>
    /// #402 N1: <see cref="EmailValidator"/> rejects a resolved template without a subject or a body. MimeKit used to
    /// throw on the null subject (a 500), and the data-context rules threw in <c>Regex.Matches</c> on a null text.
    /// </summary>
    public class EmailTemplateTextValidatorTests
    {
        private const string NoSubject = "The email template for this purpose and language has no subject.";
        private const string NoBody = "The email template for this purpose and language has no body.";

        private static EmailValidator Validator() => new(
            new CommonEmailValidator(Mock.Of<IMailRepository>()),
            Options.Create(new MailAttachmentOptions { ValidateAttachmentsExist = false }));

        private static MailToBeSent Mail(string? subject, string? body, bool isTestMail = false,
            Dictionary<string, string>? subjectContext = null, Dictionary<string, string>? bodyContext = null) => new()
        {
            ItemId = "mail-1",
            Name = "welcome",
            Language = "en",
            To = ["user-1@app.test"],
            Cc = [],
            Bcc = [],
            ReplyTo = [],
            Attachments = [],
            SubjectDataContext = subjectContext ?? [],
            BodyDataContext = bodyContext ?? [],
            EmailTemplate = new EmailTemplate { ItemId = "t1", Name = "welcome", Language = "en", TemplateSubject = subject!, TemplateBody = body! },
            MailServerConfiguration = new MailServerConfiguration { ItemId = "c1" },
            IsTestMail = isTestMail,
        };

        private static async Task<Dictionary<string, string>> ErrorsOf(MailToBeSent mail)
        {
            var result = await Validator().ValidateAsync(mail).ConfigureAwait(false);
            // The same conversion MailService.ProcessMailSent makes: it throws on a duplicate key.
            return result.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage);
        }

        [Fact]
        public async Task H9_CompleteTemplate_IsValid()
        {
            (await ErrorsOf(Mail("Welcome", "<p>Hello</p>"))).Should().BeEmpty();
        }

        [Fact]
        public async Task H10_SubjectPlaceholders_StillNeedTheirValues()
        {
            (await ErrorsOf(Mail("Hi {{name}}", "<p>Hello</p>", subjectContext: new() { ["name"] = "A" }))).Should().BeEmpty();
            (await ErrorsOf(Mail("Hi {{name}}", "<p>Hello</p>", subjectContext: new() { ["other"] = "A" })))
                .Should().BeEquivalentTo(new Dictionary<string, string> { ["SubjectDataContext"] = "Subject datacontext is invalid" });
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task C6_TemplateWithoutSubject_IsRejected(string? subject)
        {
            (await ErrorsOf(Mail(subject, "<p>Hello</p>")))
                .Should().BeEquivalentTo(new Dictionary<string, string> { ["TemplateSubject"] = NoSubject });
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" \t ")]
        public async Task C7_TemplateWithoutBody_IsRejected(string? body)
        {
            (await ErrorsOf(Mail("Welcome", body)))
                .Should().BeEquivalentTo(new Dictionary<string, string> { ["TemplateBody"] = NoBody });
        }

        [Theory]
        [InlineData(null)]
        [InlineData("  ")]
        public async Task C8_NoSubject_WithASubjectDataContext_IsOnlyTheSubjectError_WithoutAnException(string? subject)
        {
            var errors = await ErrorsOf(Mail(subject, "<p>Hello</p>", subjectContext: new() { ["name"] = "A" }));

            errors.Should().BeEquivalentTo(new Dictionary<string, string> { ["TemplateSubject"] = NoSubject });
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task C9_NoBody_WithABodyDataContext_IsOnlyTheBodyError_WithoutAnException(string? body)
        {
            var errors = await ErrorsOf(Mail("Welcome", body, bodyContext: new() { ["name"] = "A" }));

            errors.Should().BeEquivalentTo(new Dictionary<string, string> { ["TemplateBody"] = NoBody });
        }

        [Fact]
        public async Task C10_TestMail_WithoutSubject_IsRejected()
        {
            (await ErrorsOf(Mail(null, "<p>Hello</p>", isTestMail: true)))
                .Should().BeEquivalentTo(new Dictionary<string, string> { ["TemplateSubject"] = NoSubject });
        }

        [Fact]
        public async Task TestMail_WithoutBody_IsRejected()
        {
            (await ErrorsOf(Mail("Welcome", null, isTestMail: true)))
                .Should().BeEquivalentTo(new Dictionary<string, string> { ["TemplateBody"] = NoBody });
        }

        [Fact]
        public async Task NeitherSubjectNorBody_ReportsBoth()
        {
            (await ErrorsOf(Mail(null, null, subjectContext: new() { ["a"] = "1" }, bodyContext: new() { ["b"] = "2" })))
                .Should().BeEquivalentTo(new Dictionary<string, string> { ["TemplateSubject"] = NoSubject, ["TemplateBody"] = NoBody });
        }

        [Fact]
        public async Task NoTemplate_KeepsTheExistingErrors_AndAddsNoTemplateTextErrors()
        {
            var mail = Mail("Welcome", "<p>Hello</p>");
            mail.EmailTemplate = null!;

            var errors = await ErrorsOf(mail);

            errors.Should().ContainKey("Name").WhoseValue.Should().Be(
                "The purpose and language combination does not exist. Please check the EmailTemplates collection");
            errors.Should().NotContainKeys("TemplateSubject", "TemplateBody");
        }

        [Fact]
        public void DataContextRules_AcceptAMissingTemplateText_SoTheTemplateRulesReportIt()
        {
            var common = new CommonEmailValidator(Mock.Of<IMailRepository>());
            var mail = Mail(null, null);

            common.BeValidSubjectDataContext(mail, new() { ["name"] = "A" }).Should().BeTrue();
            common.BeAVaildDataContext(mail, new() { ["name"] = "A" }).Should().BeTrue();
            common.BeAVaildDataContext(Mail("s", "  "), new()).Should().BeTrue();
        }
    }
}
