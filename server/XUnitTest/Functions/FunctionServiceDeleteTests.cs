using FluentAssertions;
using FluentValidation;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Entities;
using Functions.DomainService.Repositories;
using Functions.DomainService.Services;
using Functions.DomainService.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Functions
{
    /// <summary>
    /// Deleting a function is the one operation with nothing left to retry against once it is
    /// half done: after the document goes, nothing names the versions, builds, runs and images it
    /// owned. So the order matters as much as the steps, and it is pinned here.
    /// </summary>
    public class FunctionServiceDeleteTests
    {
        private const string Tenant = "t1";
        private const string FunctionId = "fn_1";

        private readonly Mock<IFunctionRepository> _functions = new(MockBehavior.Loose);
        private readonly Mock<IFunctionPurgeService> _purge = new(MockBehavior.Loose);
        private readonly Mock<IFunctionAuditService> _audit = new(MockBehavior.Loose);
        private readonly Mock<IFunctionUsageService> _usage = new(MockBehavior.Loose);
        private readonly List<string> _order = [];

        private FunctionService Service(
            FunctionEntity? existing = null,
            bool deleteSucceeds = true,
            IReadOnlyList<FunctionWorkflowReference>? references = null)
        {
            _usage.Setup(u => u.GetWorkflowReferencesAsync(Tenant, FunctionId, It.IsAny<CancellationToken>()))
                .Callback(() => _order.Add("usage-check"))
                .ReturnsAsync(references ?? []);

            _functions.Setup(f => f.GetByIdAsync(Tenant, FunctionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existing);
            _functions.Setup(f => f.DeleteAsync(Tenant, FunctionId, It.IsAny<CancellationToken>()))
                .Callback(() => _order.Add("delete-document"))
                .ReturnsAsync(deleteSucceeds);

            _purge.Setup(p => p.PurgeAsync(Tenant, FunctionId, It.IsAny<CancellationToken>()))
                .Callback(() => _order.Add("purge"))
                .ReturnsAsync(new FunctionPurgeReport(2, 3, 4, 5, 6, 1));

            _audit.Setup(a => a.RecordAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
                .Callback(() => _order.Add("audit"))
                .Returns(Task.CompletedTask);

            return new FunctionService(
                _functions.Object,
                Mock.Of<IFunctionVersionRepository>(),
                Mock.Of<IFunctionRunStatsRepository>(),
                Mock.Of<IFunctionRunRepository>(),
                _audit.Object,
                _purge.Object,
                _usage.Object,
                Mock.Of<IValidator<CreateFunctionRequestDto>>(),
                Mock.Of<IValidator<UpdateFunctionRequestDto>>(),
                Mock.Of<IValidator<SaveFunctionRequestDto>>(),
                NullLogger<FunctionService>.Instance,
                new ConfigurationBuilder().AddInMemoryCollection([]).Build());
        }

        private static FunctionEntity Function() => new() { ItemId = FunctionId, Name = "converter" };

        [Fact]
        public async Task Everything_the_function_owns_goes_before_the_function_itself()
        {
            // The other way round strands versions, builds, runs and pinned images with nothing
            // left to look them up by.
            var deleted = await Service(Function()).DeleteAsync(Tenant, FunctionId, "u1", "u@example.com");

            deleted.Should().BeTrue();
            _order.Should().Equal("usage-check", "purge", "delete-document", "audit");
        }

        [Fact]
        public async Task A_function_that_is_not_there_is_not_purged()
        {
            var deleted = await Service(existing: null).DeleteAsync(Tenant, FunctionId, "u1", "u@example.com");

            deleted.Should().BeFalse();
            _purge.Verify(p => p.PurgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _order.Should().BeEmpty();
        }

        [Fact]
        public async Task A_document_delete_that_does_nothing_is_not_reported_as_a_delete()
        {
            // Someone else deleted it first. Saying "deleted" and writing an audit record for work
            // this call did not do would put two deletions in the trail for one function.
            var deleted = await Service(Function(), deleteSucceeds: false)
                .DeleteAsync(Tenant, FunctionId, "u1", "u@example.com");

            deleted.Should().BeFalse();
            _order.Should().NotContain("audit");
        }

        [Fact]
        public async Task The_audit_record_says_what_was_removed()
        {
            var service = Service(Function());

            object? details = null;
            _audit.Setup(a => a.RecordAsync(
                    Tenant, FunctionId, FunctionsConstants.AuditActions.Deleted, It.IsAny<string?>(),
                    It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
                .Callback((string _, string __, string ___, string? ____, string? _____, object? d, CancellationToken ______) => details = d)
                .Returns(Task.CompletedTask);

            await service.DeleteAsync(Tenant, FunctionId, "u1", "u@example.com");

            // The audit trail outlives the function and is the only record it ever existed, so the
            // counts belong in it rather than only in a log line on one host.
            details.Should().NotBeNull();
            details!.ToString().Should().Contain("2").And.Contain("6");
        }

        [Fact]
        public async Task A_function_a_workflow_still_calls_is_not_deleted()
        {
            // The step keeps its function id either way; the only question is whether the person
            // deleting finds out now, or a workflow does at its next run.
            var service = Service(Function(), references: [new FunctionWorkflowReference("wf_1", "Nightly payouts", true)]);

            var act = async () => await service.DeleteAsync(Tenant, FunctionId, "u1", "u@example.com");

            (await act.Should().ThrowAsync<FunctionValidationException>())
                .Which.Message.Should().Contain("Nightly payouts").And.Contain("published");
            _order.Should().NotContain("purge").And.NotContain("delete-document");
        }

        [Fact]
        public async Task Force_deletes_it_anyway_and_does_not_even_ask()
        {
            var service = Service(Function(), references: [new FunctionWorkflowReference("wf_1", "Nightly payouts", true)]);

            var deleted = await service.DeleteAsync(Tenant, FunctionId, "u1", "u@example.com", force: true);

            deleted.Should().BeTrue();
            _order.Should().Equal("purge", "delete-document", "audit");
            _usage.Verify(u => u.GetWorkflowReferencesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task The_refusal_names_a_handful_and_counts_the_rest()
        {
            var many = Enumerable.Range(1, 8)
                .Select(i => new FunctionWorkflowReference($"wf_{i}", $"Workflow {i}", false))
                .ToList();
            var service = Service(Function(), references: many);

            var act = async () => await service.DeleteAsync(Tenant, FunctionId, "u1", "u@example.com");

            (await act.Should().ThrowAsync<FunctionValidationException>())
                .Which.Message.Should().Contain("8 workflow(s)").And.Contain("and 3 more");
        }

        [Fact]
        public async Task A_usage_check_that_fails_stops_the_delete()
        {
            // An empty answer must mean "nothing uses it", never "the question could not be asked".
            var service = Service(Function());
            _usage.Setup(u => u.GetWorkflowReferencesAsync(Tenant, FunctionId, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException("mongo"));

            var act = async () => await service.DeleteAsync(Tenant, FunctionId, "u1", "u@example.com");

            await act.Should().ThrowAsync<TimeoutException>();
            _order.Should().NotContain("delete-document");
        }

        [Fact]
        public async Task A_purge_that_throws_leaves_the_function_in_place()
        {
            // Visible and deletable again beats invisible with its footprint stranded.
            var service = Service(Function());
            _purge.Setup(p => p.PurgeAsync(Tenant, FunctionId, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException("mongo"));

            var act = async () => await service.DeleteAsync(Tenant, FunctionId, "u1", "u@example.com");

            await act.Should().ThrowAsync<TimeoutException>();
            _order.Should().NotContain("delete-document");
        }
    }
}
