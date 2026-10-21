using MongoDB.Bson;

namespace Workflow.DomainService.Services
{
    public enum InputRuleDecision
    {
        Run,
        Skip,
        Fail,
    }

    /// <summary>
    /// The node's input rule, from its settings: whether it runs when some inputs sent no items.
    /// Missing or unknown values take the defaults (run when any input has data), which is how every
    /// node behaved before this rule existed.
    /// </summary>
    public sealed record NodeInputRule(bool RequireAllInputs, bool FailWhenMissing)
    {
        public const string RunWhenKey = "runWhen";
        public const string IfInputMissingKey = "ifInputMissing";

        public static readonly NodeInputRule Default = new(RequireAllInputs: false, FailWhenMissing: false);

        public static NodeInputRule Parse(BsonDocument? settings)
        {
            if (settings is null || settings.ElementCount == 0) return Default;
            var requireAll = Text(settings, RunWhenKey) == "allInputsHaveData";
            var fail = Text(settings, IfInputMissingKey) == "fail";
            return requireAll || fail ? new NodeInputRule(requireAll, fail) : Default;
        }

        private static string? Text(BsonDocument settings, string key)
            => settings.TryGetValue(key, out var value) && value.IsString ? value.AsString : null;

        /// <summary>Run unless all inputs are required and some of the node's incoming edges sent no items.</summary>
        public static InputRuleDecision Evaluate(NodeInputRule rule, int total, int withoutData)
        {
            if (!rule.RequireAllInputs || total == 0 || withoutData == 0) return InputRuleDecision.Run;
            return rule.FailWhenMissing ? InputRuleDecision.Fail : InputRuleDecision.Skip;
        }
    }
}
