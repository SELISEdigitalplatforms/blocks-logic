using Blocks.Genesis;
using System;
using System.Collections.Generic;
using System.Text;

namespace Cloud.LmtService.Models.Logs
{
    public class LogsByDateRequest : BaseGetsRequest<LogsByLastDateRequestFilter>
    {
        public string? Search { get; set; }
        public required string ServiceName { get; set; }
    }

    public class LogsByLastDateRequestFilter
    {
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? Level { get; set; }
        public string? TraceId { get; set; }
        public string? SpanId { get; set; }
    }
}
