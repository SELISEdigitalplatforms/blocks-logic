namespace Workflow.DomainService.Logging
{
    /// <summary>
    /// Per-item request lines for network-bound nodes (HTTP Request, Proxy): "sending", "response" and "error",
    /// numbered 1..Total. After <see cref="ExecutionLogStages.PerItemLineCap"/> items it writes one
    /// "… N more request(s) not logged individually." line and stays silent, so a large run stays well under the
    /// read limit.
    /// </summary>
    public sealed class PerItemRequestLog
    {
        private readonly NodeExecutionLog _log;
        private readonly int _total;
        private readonly string _sendingStage;
        private readonly string _responseStage;
        private readonly string _errorStage;
        private readonly string _notLoggedStage;
        private bool _capAnnounced;

        public PerItemRequestLog(NodeExecutionLog log, int total, string sendingStage, string responseStage, string errorStage, string notLoggedStage)
        {
            _log = log;
            _total = total;
            _sendingStage = sendingStage;
            _responseStage = responseStage;
            _errorStage = errorStage;
            _notLoggedStage = notLoggedStage;
        }

        public static PerItemRequestLog ForHttp(NodeExecutionLog log, int total) => new(
            log, total, ExecutionLogStages.HttpSending, ExecutionLogStages.HttpResponse, ExecutionLogStages.HttpError, ExecutionLogStages.HttpNotLogged);

        public static PerItemRequestLog ForProxy(NodeExecutionLog log, int total) => new(
            log, total, ExecutionLogStages.ProxySending, ExecutionLogStages.ProxyResponse, ExecutionLogStages.ProxyError, ExecutionLogStages.ProxyNotLogged);

        /// <param name="index">Zero-based item index.</param>
        public void Sending(int index)
        {
            if (!ShouldLog(index)) return;
            _log.Info(_sendingStage, "Sending request {Index} of {Total}.", index + 1, _total);
        }

        public void Response(int index, int status, long durationMs)
        {
            if (!ShouldLog(index)) return;
            _log.Info(_responseStage, "Request {Index} of {Total}: HTTP {Status} in {DurationMs} ms.", index + 1, _total, status, durationMs);
        }

        public void Failed(int index, string errorKind)
        {
            if (!ShouldLog(index)) return;
            _log.Error(_errorStage, "Request {Index} of {Total} failed ({ErrorKind:l}).", index + 1, _total, errorKind);
        }

        private bool ShouldLog(int index)
        {
            if (index < ExecutionLogStages.PerItemLineCap) return true;
            if (!_capAnnounced)
            {
                _capAnnounced = true;
                _log.Info(_notLoggedStage, "… {Remaining} more request(s) not logged individually.", _total - ExecutionLogStages.PerItemLineCap);
            }
            return false;
        }
    }
}
