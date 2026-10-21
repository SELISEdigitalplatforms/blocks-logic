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
        /// <summary>The output's stable handle key; item Branch and edge sourceHandle carry it.</summary>
        public string? HandleKey { get; set; }
        /// <summary>Display label of the output only; never used for routing.</summary>
        public string? HandleLabel { get; set; }
        /// <summary>Same values as If: "or" = any condition, anything else = all.</summary>
        public string? ConditionType { get; set; } = "all";
        public List<Condition> Conditions { get; set; } = new();
    }

    public class SwitchOptions
    {
        /// <summary>"none" (drop), "extra" (the fallback handle) or the handle key of a rule.</summary>
        public string? FallbackOutput { get; set; } = "none";
        public string? RenameFallbackOutput { get; set; } = string.Empty;
        public bool IgnoreCase { get; set; }
        public bool AllMatchingOutputs { get; set; }
    }
}
