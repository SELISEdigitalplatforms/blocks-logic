using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Workflow.DomainService.Nodes.LogicIFV1;

namespace Workflow.DomainService.Nodes.LogicFilterV2
{
    /// <summary>
    /// Whether every condition (And) or any one (Or) must match. Saved as camelCase text ("and", "or"); the
    /// numbers 0 and 1 also bind. A number outside the enum binds too, so the executor checks it before use.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter), typeof(CamelCaseNamingStrategy))]
    public enum FilterConditionType
    {
        And = 0,
        Or = 1,
    }

    public class LogicFilterV2Parameters
    {
        /// <summary>Missing or null means And, the editor's default.</summary>
        public FilterConditionType ConditionType { get; set; } = FilterConditionType.And;

        /// <summary>Same shape and evaluator as If and Switch conditions.</summary>
        public List<Condition> Conditions { get; set; } = new();
    }
}
