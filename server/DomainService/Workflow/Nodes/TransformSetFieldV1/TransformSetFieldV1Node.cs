using System.Diagnostics.CodeAnalysis;

namespace DomainService.Workflow.Nodes.TransformSetFieldV1
{
    [ExcludeFromCodeCoverage]
    public class TransformSetFieldV1Node : NodeExecutorBase<TransformSetFieldV1Parameters>
    {
        public override string NodeType => "setfield";
        public override string Version => "v1";

        protected override async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, TransformSetFieldV1Parameters? nodeparameters)
        {
            try
            {
                var parameters = nodeparameters ?? new TransformSetFieldV1Parameters();
                var outputItems = new List<NodeOutputItem>();

                for (int i = 0; i < context.IterationCount; i++)
                {
                    var mode = parameters.mode;
                    if (mode == "manual_mapping") { }


                }

                return NodeExecutionResult.Successful(outputItems);
            }
            catch (Exception ex)
            {
                return NodeExecutionResult.Failed(ex.Message);
            }
        }



    }
}
