using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Workflow.DomainService.Nodes.TransformMergeV2
{
    /// <summary>Saved as camelCase text ("append"). Any other value fails the step with a parameter-shape message.</summary>
    [JsonConverter(typeof(StringEnumConverter), typeof(CamelCaseNamingStrategy))]
    public enum MergeMode
    {
        Append,
    }

    public class TransformMergeV2Parameters
    {
        public MergeMode Mode { get; set; } = MergeMode.Append;

        /// <summary>
        /// Number of inputs, 2 to 10 (the editor offers a dropdown). A double so that a fraction binds instead of
        /// failing the parameter shape: missing means 2, anything else is floored and kept within 2 to 10.
        /// </summary>
        public double? NumberInputs { get; set; }
    }
}
