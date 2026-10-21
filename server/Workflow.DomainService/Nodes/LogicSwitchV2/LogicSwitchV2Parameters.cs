using Workflow.DomainService.Nodes.LogicIFV1;

namespace Workflow.DomainService.Nodes.LogicSwitchV2
{
    public class LogicSwitchV2Parameters
    {
        /// <summary>"rules" or "expression".</summary>
        public string Mode { get; set; } = "rules";

        /// <summary>Rules mode: evaluated in this order.</summary>
        public List<SwitchRule?> Rules { get; set; } = new();

        /// <summary>Expression mode: the output for index i is Outputs[i].</summary>
        public List<SwitchOutput?> Outputs { get; set; } = new();

        /// <summary>Expression mode: resolves to the 0-based output index.</summary>
        public string Output { get; set; } = string.Empty;

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

    public class SwitchOutput
    {
        public string? Key { get; set; }
        public string? OutputName { get; set; }
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
