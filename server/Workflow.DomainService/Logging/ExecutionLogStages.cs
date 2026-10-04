namespace Workflow.DomainService.Logging
{
    /// <summary>
    /// Every stage code an execution log line can carry. Stage codes are always taken from here, never built
    /// at runtime; the frontend mirrors a few of them for icons and renders unknown ones without one.
    /// </summary>
    public static class ExecutionLogStages
    {
        // Trigger side (WorkflowExecutionService)
        public const string ExecutionCreated = "execution.created";
        public const string ExecutionQueued = "execution.queued";
        public const string ExecutionInProcess = "execution.inProcess";
        public const string ExecutionStep = "execution.step";
        public const string ExecutionStartFailed = "execution.startFailed";

        // Engine (WorkflowEngineService)
        public const string NodeSkipped = "node.skipped";
        public const string NodeWaiting = "node.waiting";
        public const string NodeStarted = "node.started";
        public const string NodeInput = "node.input";
        public const string NodeNoInput = "node.noInput";
        public const string NodeAncestors = "node.ancestors";
        public const string NodePinned = "node.pinned";
        public const string NodeCached = "node.cached";
        public const string NodeOutputSaved = "node.outputSaved";
        public const string NodeCompleted = "node.completed";
        public const string NodeDispatched = "node.dispatched";
        public const string NodeDispatchedInProcess = "node.dispatchedInProcess";
        public const string NodeTargetReached = "node.targetReached";
        public const string NodeFailed = "node.failed";
        public const string ExecutionCompleted = "execution.completed";
        public const string ExecutionFailed = "execution.failed";

        // Base class (NodeExecutorBase.RunAsync)
        public const string NodeVariables = "node.variables";
        public const string NodeVariablesResolved = "node.variablesResolved";
        public const string NodeVariablesFailed = "node.variablesFailed";
        public const string NodeParameters = "node.parameters";
        public const string NodeParametersFailed = "node.parametersFailed";
        public const string NodeExecuting = "node.executing";
        public const string NodeExecuted = "node.executed";

        // Per node type (inside each executor's ExecuteAsync)
        public const string TriggerRead = "node.trigger.read";
        public const string IfEvaluated = "node.if.evaluated";
        public const string SetFieldApplied = "node.setfield.applied";
        public const string CodeStarted = "node.code.started";
        public const string CodeFinished = "node.code.finished";
        public const string CodeTimeout = "node.code.timeout";
        public const string CodeLimit = "node.code.limit";
        public const string HttpSending = "node.http.sending";
        public const string HttpResponse = "node.http.response";
        public const string HttpError = "node.http.error";
        public const string HttpNotLogged = "node.http.notLogged";
        public const string ProxySending = "node.proxy.sending";
        public const string ProxyResponse = "node.proxy.response";
        public const string ProxyError = "node.proxy.error";
        public const string ProxyNotLogged = "node.proxy.notLogged";
        public const string DataToken = "node.data.token";
        public const string DataTokenFailed = "node.data.tokenFailed";
        public const string DataFinished = "node.data.finished";
        public const string MailFinished = "node.mail.finished";
        public const string AgentCall = "node.agent.call";
        public const string AgentResponse = "node.agent.response";
        public const string AgentNotLogged = "node.agent.notLogged";

        /// <summary>Per-item lines written by network-bound nodes before switching to the summary only.</summary>
        public const int PerItemLineCap = 50;
    }
}
