namespace DomainService.Workflow.Nodes.TransformSetFieldV1
{
    public class TransformSetFieldV1Parameters
    {
        public string Mode { get; set; } = "manual_mapping";
        public List<ManualMappingField> ManualMappingFields { get; set; } = new List<ManualMappingField>();
        public string JsonCode { get; set; } = "{}";
        public bool IncludeOtherFields { get; set; } = false;
        public string OtherFieldsMode { get; set; } = "all";
        public string IncludedFields { get; set; } = string.Empty;
        public string ExcludeFields { get; set; } = string.Empty;
    }

    public class ManualMappingField
    {
        public string key { get; set; } = string.Empty;
        public string value { get; set; } = string.Empty;
        public string type { get; set; } = string.Empty;
    }

}
