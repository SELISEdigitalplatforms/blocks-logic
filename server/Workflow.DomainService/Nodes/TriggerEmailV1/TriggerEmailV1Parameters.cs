namespace Workflow.DomainService.Nodes.TriggerEmailV1
{
    public class TriggerEmailV1Parameters
    {
        public string MailServerConfigurationId { get; set; } = string.Empty;
        public string? TestSubject { get; set; }
    }
}
