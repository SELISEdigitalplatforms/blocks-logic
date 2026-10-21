using Workflow.DomainService.Nodes.LogicIFV1;

namespace Workflow.DomainService.Nodes.LogicSwitchV2
{
    public class LogicSwitchV2Parameters
    {
        /// <summary>Only "rules" for now; kept so other modes can be added later.</summary>
        public string Mode { get; set; } = "rules";

        /// <summary>Evaluated in this order.</summary>
        public List<SwitchRule?> Rules { get; set; } = new();

        public SwitchOptions Options { get; set; } = new();
    }

    public class SwitchRule
    {
        public string? Key { get; set; }
        public string? OutputName { get; set; }
        /// <summary>Same values as If: "or" = any condition, anything else = all.</summary>
        public string? ConditionType { get; set; } = "all";
        public List<Condition> Conditions { get; set; } = new();
    }

    public class SwitchOptions
    {
        /// <summary>"none" (drop), "extra" (the fallback handle) or the key of a rule.</summary>
        public string? FallbackOutput { get; set; } = "none";
        public string? RenameFallbackOutput { get; set; } = string.Empty;
        public bool IgnoreCase { get; set; }
        public bool AllMatchingOutputs { get; set; }
    }
}
