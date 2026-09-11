using Blocks.Genesis;
using System;
using System.Collections.Generic;
using System.Text;

namespace Cloud.LmtService.Models.Logs
{
    public class LiveLogRequest
    {
        public required string Name { get; set; }
        public DateTime LastDate { get; set; }
    }
}
