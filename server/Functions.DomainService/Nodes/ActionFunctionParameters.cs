namespace Functions.DomainService.Nodes
{
    /// <summary>Node-editor parameters for the "function" action step.</summary>
    public class ActionFunctionParameters
    {
        public string FunctionId { get; set; } = string.Empty;

        /// <summary><c>"item"</c> (default) passes the whole input item through as-is; <c>"expression"</c> evaluates <see cref="InputExpression"/>.</summary>
        public string InputMode { get; set; } = "item";

        public string InputExpression { get; set; } = string.Empty;

        /// <summary>Overrides how long this step waits for a terminal result; null uses the function's own timeout plus grace.</summary>
        public int? WaitTimeoutSec { get; set; }
    }
}
