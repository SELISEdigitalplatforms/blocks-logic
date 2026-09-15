using FluentAssertions;
using MongoDB.Bson;
using Workflow.DomainService.Utils;

namespace XUnitTest.Workflow
{
    public class WorkflowVariableRefTests
    {
        [Fact]
        public void Names_UsesWorkflowExpressionTrimmingRules()
        {
            var parameters = new BsonDocument
            {
                { "url", "https://example.test?key={{ $VAR.api-token }}" },
                { "header", "Bearer {{$VAR.api-token}}" },
                { "other", "{{$VAR.other_9:x-y}}" }
            };

            WorkflowVariableRef.Names(parameters).Should().Equal("api-token", "other_9:x-y");
        }
    }
}
