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
    /// The request half of a delete: check nothing depends on the function, tombstone it, audit
    /// it, queue the background purge — and nothing else. The purge itself belongs to
    /// <c>FunctionDeletionWorker</c> (see <see cref="FunctionDeletionWorkerTests"/>), off the
    /// request, because it has to outlast work that was already in flight when the delete came.
    /// </summary>
    public class FunctionServiceDeleteTests
    {
        private const string Tenant = "t1";
        private const string FunctionId = "fn_1";

        private readonly Mock<IFunctionRepository> _functions = new(MockBehavior.Loose);
        private readonly Mock<IFunctionDeletionQueue> _queue = new(MockBehavior.Loose);
        private readonly Mock<IFunctionAuditService> _audit = new(MockBehavior.Loose);
        private readonly Mock<IFunctionUsageService> _usage = new(MockBehavior.Loose);
        private readonly List<string> _order = [];
        private FunctionDeletion? _tombstone;

        private FunctionService Service(
            FunctionEntity? existing = null,
            bool markSucceeds = true,
            IReadOnlyList<FunctionWorkflowReference>? references = null)
        {
            _usage.Setup(u => u.GetWorkflowReferencesAsync(Tenant, FunctionId, It.IsAny<CancellationToken>()))
                .Callback(() => _order.Add("usage-check"))
                .ReturnsAsync(references ?? []);

            _functions.Setup(f => f.GetByIdAsync(Tenant, FunctionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existing);
            _functions.Setup(f => f.MarkDeletedAsync(Tenant, FunctionId, It.IsAny<FunctionDeletion>(), It.IsAny<CancellationToken>()))
                .Callback((string _, string __, FunctionDeletion d, CancellationToken ___) =>
                {
                    _tombstone = d;
                    _order.Add("tombstone");
                })
                .ReturnsAsync(markSucceeds);

            _queue.Setup(q => q.EnqueueAsync(Tenant, FunctionId, It.IsAny<CancellationToken>()))
                .Callback(() => _order.Add("enqueue"))
                .Returns(Task.CompletedTask);

            _audit.Setup(a => a.RecordAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
                .Callback(() => _order.Add("audit"))
                .Returns(Task.CompletedTask);

            return new FunctionService(
                _functions.Object,
                Mock.Of<IFunctionVersionRepository>(),
                Mock.Of<IFunctionRunRepository>(),
                _audit.Object,
                _queue.Object,
                _usage.Object,
                Mock.Of<IValidator<CreateFunctionRequestDto>>(),
                Mock.Of<IValidator<UpdateFunctionRequestDto>>(),
                Mock.Of<IValidator<SaveFunctionRequestDto>>(),
                NullLogger<FunctionService>.Instance,
                new ConfigurationBuilder().AddInMemoryCollection([]).Build());
        }

        private static FunctionEntity Function() => new() { ItemId = FunctionId, Name = "converter" };

        [Fact]
        public async Task A_delete_tombstones_audits_and_queues_the_purge_and_nothing_more()
        {
            var deleted = await Service(Function()).DeleteAsync(Tenant, FunctionId, "u1", "u@example.com");

            deleted.Should().BeTrue();
            _order.Should().Equal("usage-check", "tombstone", "audit", "enqueue");
            // Nothing is removed on the request: the document stays as the tombstone.
            _functions.Verify(f => f.DeleteTombstoneAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task The_tombstone_says_who_asked_when_and_whether_it_was_forced()
        {
            var before = DateTime.UtcNow;
            await Service(Function()).DeleteAsync(Tenant, FunctionId, "u1", "u@example.com", force: true);

            _tombstone.Should().NotBeNull();
            _tombstone!.RequestedBy.Should().Be("u1");
            _tombstone.RequestedByEmail.Should().Be("u@example.com");
            _tombstone.Forced.Should().BeTrue();
            _tombstone.RequestedAt.Should().BeOnOrAfter(before);
            _tombstone.Passes.Should().Be(0);
        }

        [Fact]
        public async Task A_function_that_is_not_there_is_not_tombstoned()
        {
            var deleted = await Service(existing: null).DeleteAsync(Tenant, FunctionId, "u1", "u@example.com");

            deleted.Should().BeFalse();
            _order.Should().BeEmpty();
        }

        [Fact]
        public async Task Losing_the_race_to_another_delete_is_not_reported_as_a_delete()
        {
            // Two deletes at once: one tombstone, one audit record, one "deleted".
            var deleted = await Service(Function(), markSucceeds: false)
                .DeleteAsync(Tenant, FunctionId, "u1", "u@example.com");

            deleted.Should().BeFalse();
            _order.Should().NotContain("audit").And.NotContain("enqueue");
        }

        [Fact]
        public async Task The_audit_record_is_the_delete_itself()
        {
            var service = Service(Function());
            string? action = null;
            _audit.Setup(a => a.RecordAsync(
                    Tenant, FunctionId, It.IsAny<string>(), "u1", "u@example.com",
                    It.IsAny<object?>(), It.IsAny<CancellationToken>()))
                .Callback((string _, string __, string a, string? ___, string? ____, object? _____, CancellationToken ______) => action = a)
                .Returns(Task.CompletedTask);

            await service.DeleteAsync(Tenant, FunctionId, "u1", "u@example.com");

            action.Should().Be(FunctionsConstants.AuditActions.Deleted);
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
            _order.Should().NotContain("tombstone").And.NotContain("enqueue");
        }

        [Fact]
        public async Task Force_deletes_it_anyway_and_does_not_even_ask()
        {
            var service = Service(Function(), references: [new FunctionWorkflowReference("wf_1", "Nightly payouts", true)]);

            var deleted = await service.DeleteAsync(Tenant, FunctionId, "u1", "u@example.com", force: true);

            deleted.Should().BeTrue();
            _order.Should().Equal("tombstone", "audit", "enqueue");
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
            _order.Should().NotContain("tombstone");
        }

        [Fact]
        public async Task A_tombstone_write_that_fails_leaves_the_function_live_and_unqueued()
        {
            var service = Service(Function());
            _functions.Setup(f => f.MarkDeletedAsync(Tenant, FunctionId, It.IsAny<FunctionDeletion>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException("mongo"));

            var act = async () => await service.DeleteAsync(Tenant, FunctionId, "u1", "u@example.com");

            await act.Should().ThrowAsync<TimeoutException>();
            _order.Should().NotContain("enqueue").And.NotContain("audit");
        }
    }
}
