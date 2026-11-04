using Blocks.Genesis;

namespace Mail.DomainService.Mails
{
    public class SendMailToAny : BaseMailRequest
    {
        public bool? IsTestMail { get; set; }

        /// <summary>
        /// A test mail of this exact template (the console's "Send test Email"). Used only when
        /// <see cref="IsTestMail"/> is true, and then Purpose and Language come from the template.
        /// </summary>
        public string? TemplateId { get; set; }
    }
}
