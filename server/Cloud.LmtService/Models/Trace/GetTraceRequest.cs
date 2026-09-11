using Blocks.Genesis;
using System;
using System.Collections.Generic;
using System.Text;

namespace Cloud.LmtService.Models.Trace
{
    public class GetTraceRequest
    {
        public required string TraceId { get; set; }
    }
    public class GetRestoredTraceRequest
    {
        public required string RequestId { get; set; }
        public required string TraceId { get; set; }
    }
}
