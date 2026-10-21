namespace Workflow.DomainService.Nodes.TransformMergeV2
{
    public class TransformMergeV2Parameters
    {
        /// <summary>Only "append" is supported.</summary>
        public string Mode { get; set; } = "append";

        /// <summary>
        /// Number of inputs. A double so that a fraction binds instead of failing the parameter shape:
        /// missing or null means 2, below 1 means 1, anything else is floored. No upper limit.
        /// </summary>
        public double? NumberInputs { get; set; }
    }
}
