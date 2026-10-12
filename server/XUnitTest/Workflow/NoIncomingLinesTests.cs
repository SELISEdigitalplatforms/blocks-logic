using Blocks.Genesis;
using FluentAssertions;
using Moq;
using Workflow.DomainService.Repositories;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// PKG-25: a step with no incoming lines asks for its parents' items with an empty list. That used
    /// to become `$or: []`, which Mongo rejects, so the step failed before its own checks could run.
    /// </summary>
    public class NoIncomingLinesTests
    {
        [Fact]
        public async Task No_parents_means_no_items_and_no_query()
        {
            var provider = new Mock<IDbContextProvider>(MockBehavior.Strict);
            var repository = new WorkflowExecutionRepository(provider.Object);

            var items = await repository.GetItemsByNodeIdsAsync("exec-1", new List<Dictionary<string, string>>(), "t1");

            items.Should().BeEmpty();
            provider.VerifyNoOtherCalls();
        }
    }
}
