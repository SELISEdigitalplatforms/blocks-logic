namespace Workflow.DomainService.Dtos
{
    public class NotificationData
    {
        public required string Title { get; set; }
        public string? Description { get; set; } = "";
        public required string ResponseKey { get; set; }
        public required string ResponseValue { get; set; }
        public required Dictionary<string, object> Information { get; set; }

    }
}

