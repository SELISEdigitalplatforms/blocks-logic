using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Workflow.DomainService.Nodes.TransformLimitV2
{
    /// <summary>Which end of the list Limit keeps. Saved as camelCase text ("first", "last").</summary>
    [JsonConverter(typeof(StringEnumConverter), typeof(CamelCaseNamingStrategy))]
    public enum LimitKeep
    {
        First = 0,
        Last = 1,
    }

    public class TransformLimitV2Parameters
    {
        /// <summary>The most items to keep, 1 or more.</summary>
        public int MaxItems { get; set; } = 1;

        public LimitKeep Keep { get; set; } = LimitKeep.First;
    }
}
