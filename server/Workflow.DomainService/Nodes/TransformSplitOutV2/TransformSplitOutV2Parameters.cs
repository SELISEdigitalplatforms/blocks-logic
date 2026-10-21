namespace Workflow.DomainService.Nodes.TransformSplitOutV2
{
    public class TransformSplitOutV2Parameters
    {
        /// <summary>Comma-separated paths of the fields to split out.</summary>
        public string FieldToSplitOut { get; set; } = string.Empty;

        /// <summary>"noOtherFields" (default), "allOtherFields" or "selectedOtherFields".</summary>
        public string Include { get; set; } = "noOtherFields";

        /// <summary>Comma-separated paths copied into each item when Include is "selectedOtherFields".</summary>
        public string FieldsToInclude { get; set; } = string.Empty;

        /// <summary>Optional comma-separated names, one per field to split out.</summary>
        public string DestinationFieldName { get; set; } = string.Empty;

        /// <summary>True: every path is one literal top-level key.</summary>
        public bool DisableDotNotation { get; set; }
    }
}
