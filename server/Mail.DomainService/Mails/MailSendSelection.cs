using Mail.DomainService.Entities;
using EmailTemplate = Blocks.Genesis.Entities.Os.EmailTemplate;

namespace Mail.DomainService.Mails
{
    /// <summary>
    /// The template chosen for a send. <see cref="TiedTemplateIds"/> lists every template that shared the winning
    /// rank (same Name and Language in the same organization; nothing enforces uniqueness), lowest ItemId first;
    /// it is empty when the choice was unambiguous. The lookup reads at most 3 templates, so it lists at most 3.
    /// </summary>
    public sealed record EmailTemplateChoice(EmailTemplate? Template, IReadOnlyList<string> TiedTemplateIds)
    {
        public static readonly EmailTemplateChoice None = new(null, []);
    }

    /// <summary>Which step of the configuration chain chose the configuration.</summary>
    public enum MailConfigurationSource
    {
        None,
        Template,
        OrganizationDefault,
        ProjectDefault
    }

    /// <summary>The outbound configuration chosen for a send, and the step that chose it.</summary>
    public sealed record MailServerConfigurationChoice(MailServerConfiguration? Configuration, MailConfigurationSource Source)
    {
        public static readonly MailServerConfigurationChoice None = new(null, MailConfigurationSource.None);

        /// <summary>The value logged as <c>configurationSource</c>.</summary>
        public string SourceName => Source switch
        {
            MailConfigurationSource.Template => "template",
            MailConfigurationSource.OrganizationDefault => "organization-default",
            MailConfigurationSource.ProjectDefault => "project-default",
            _ => "none"
        };
    }
}
