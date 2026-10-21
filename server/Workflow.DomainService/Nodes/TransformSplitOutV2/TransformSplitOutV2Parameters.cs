using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Workflow.DomainService.Nodes.TransformSplitOutV2
{
    /// <summary>Which other fields of the input item each new item keeps. Saved as "noOtherFields" etc.</summary>
    [JsonConverter(typeof(StringEnumConverter), typeof(CamelCaseNamingStrategy))]
    public enum SplitOutInclude
    {
        NoOtherFields,
        AllOtherFields,
        SelectedOtherFields,
    }

    public class TransformSplitOutV2Parameters
    {
        /// <summary>The fields to split out. Several are combined by position, as in n8n.</summary>
        public List<SplitOutField?> Fields { get; set; } = new();

        public SplitOutInclude Include { get; set; } = SplitOutInclude.NoOtherFields;

        /// <summary>Paths copied into each item when Include is SelectedOtherFields.</summary>
        public List<string?> FieldsToInclude { get; set; } = new();

        /// <summary>True: every path is one literal top-level key.</summary>
        public bool DisableDotNotation { get; set; }
    }

    public class SplitOutField
    {
        /// <summary>Path of the input field to split, for example lines or order.lines.</summary>
        public string? FieldName { get; set; }

        /// <summary>
        /// Optional. The field in the output under which to put each element; empty keeps FieldName.
        /// With dot notation it is a path (order.line is written inside order).
        /// </summary>
        public string? OutputFieldName { get; set; }
    }
}
