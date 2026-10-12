namespace Functions.DomainService.Nodes
{
    /// <summary>Node-editor parameters for the "function" action step.</summary>
    public class ActionFunctionParameters
    {
        public string FunctionId { get; set; } = string.Empty;

        /// <summary><c>"item"</c> (default) passes the whole input item through as-is; <c>"expression"</c> evaluates <see cref="InputExpression"/>.</summary>
        public string InputMode { get; set; } = "item";

        public string InputExpression { get; set; } = string.Empty;

        /// <summary>
        /// Ignored. The step waits for the function's own timeout plus grace, and nothing else.
        /// <para>
        /// It was editable, and the property stays so a workflow saved while it was still keeps
        /// loading. A step may not wait a different length of time from the run it is waiting for:
        /// shorter and the step fails while the run goes on to execute and fire its output actions
        /// anyway — a side effect with no result; longer and it simply idles past a run that can no
        /// longer be in flight.
        /// </para>
        /// </summary>
        public int? WaitTimeoutSec { get; set; }
    }
}
