using DomainService.Dtos;
using DomainService.Migration;
using DomainService.Migration.Entities;
using DomainService.Migration.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MigrationCompletionConsumer = global::Worker.Consumers.MigrationCompletionConsumer;

namespace XUnitTest.Migrations
{
    /// <summary>
    /// Unit tests for MigrationCompletionConsumer.
    ///
    /// This consumer sits on a queue, so its two interesting behaviours are what it does with a
    /// message it cannot make sense of and whether it lets the broker retry. Swallowing an
    /// exception here would drop a migration completion silently, and re-throwing on a message
    /// that can never succeed would have the broker redeliver it forever, so both paths are
    /// pinned deliberately.
    ///
    /// The type is aliased with global:: because the test namespace would otherwise resolve
    /// "Worker" against XUnitTest first.
    /// </summary>
    public class MigrationCompletionConsumerTests
    {
        private readonly Mock<IMigrationRepository> _repo = new();
        private readonly Mock<IMigrationService> _service = new();
        private readonly MigrationCompletionConsumer _sut;

        public MigrationCompletionConsumerTests()
        {
            _sut = new MigrationCompletionConsumer(
                _repo.Object,
                NullLogger<MigrationCompletionConsumer>.Instance,
                _service.Object);
        }

        private static MigrationCompletionEvent Event(
            string serviceName = "IAM",
            bool isSuccess = true,
            string? errorMessage = null) => new()
            {
                TrackerId = "tracker-1",
                ServiceName = serviceName,
                IsSuccess = isSuccess,
                ErrorMessage = errorMessage,
            };

        private static MigrationTracker Tracker() => new()
        {
            ProjectKey = "project-source",
            TargetedProjectKey = "project-target",
            TenantGroupId = "group-1",
        };

        private void TrackerIs(MigrationTracker? tracker) =>
            _repo.Setup(r => r.GetMigrationTrackerAsync(It.IsAny<string>())).ReturnsAsync(tracker);

        private void UpdateReturns(bool result) =>
            _repo.Setup(r => r.UpdateServiceStatusAsync(
                    It.IsAny<string>(), It.IsAny<MigrationServiceNames>(), It.IsAny<bool>(), It.IsAny<string?>()))
                 .ReturnsAsync(result);

        [Fact]
        public async Task An_unrecognised_service_name_is_dropped_without_touching_the_tracker()
        {
            // Re-throwing here would have the broker redeliver a message that can never parse.
            await _sut.Consume(Event(serviceName: "NotAService"));

            _repo.Verify(r => r.UpdateServiceStatusAsync(
                It.IsAny<string>(), It.IsAny<MigrationServiceNames>(), It.IsAny<bool>(), It.IsAny<string?>()),
                Times.Never);
            _repo.Verify(r => r.GetMigrationTrackerAsync(It.IsAny<string>()), Times.Never);
        }

        [Theory]
        [InlineData("iam")]
        [InlineData("IAM")]
        [InlineData("Iam")]
        public async Task The_service_name_is_parsed_case_insensitively(string serviceName)
        {
            UpdateReturns(true);
            TrackerIs(Tracker());

            await _sut.Consume(Event(serviceName: serviceName));

            _repo.Verify(r => r.UpdateServiceStatusAsync(
                "tracker-1", MigrationServiceNames.IAM, true, null), Times.Once);
        }

        [Fact]
        public async Task A_missing_tracker_stops_processing_before_any_notification()
        {
            UpdateReturns(true);
            TrackerIs(null);

            await _sut.Consume(Event());

            _service.Verify(s => s.NotifyServiceDataMigrationProgress(
                It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _service.Verify(s => s.NotifyDataMigrationEvent(
                It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task The_failure_reason_is_forwarded_to_the_tracker()
        {
            UpdateReturns(true);
            TrackerIs(Tracker());

            await _sut.Consume(Event(isSuccess: false, errorMessage: "index build failed"));

            _repo.Verify(r => r.UpdateServiceStatusAsync(
                "tracker-1", MigrationServiceNames.IAM, false, "index build failed"), Times.Once);
        }

        [Fact]
        public async Task Per_service_progress_is_reported_even_when_the_status_update_was_rejected()
        {
            // The progress notification sits above the updateResult branch, so an update that
            // returned false still notifies. This is deliberate ordering, not a fallthrough.
            UpdateReturns(false);
            TrackerIs(Tracker());

            await _sut.Consume(Event());

            _service.Verify(s => s.NotifyServiceDataMigrationProgress(
                true, "project-source", "project-target"), Times.Once);
        }

        [Fact]
        public async Task A_rejected_status_update_does_not_raise_the_final_completion_event()
        {
            UpdateReturns(false);
            TrackerIs(Tracker());
            _service.Setup(s => s.AreAllServicesCompleted(It.IsAny<MigrationTracker>())).Returns(true);

            await _sut.Consume(Event());

            _service.Verify(s => s.NotifyDataMigrationEvent(
                It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task The_final_completion_event_is_raised_once_every_service_is_done()
        {
            UpdateReturns(true);
            TrackerIs(Tracker());
            _service.Setup(s => s.AreAllServicesCompleted(It.IsAny<MigrationTracker>())).Returns(true);

            await _sut.Consume(Event());

            _service.Verify(s => s.NotifyDataMigrationEvent(
                true, "project-source", "project-target"), Times.Once);
        }

        [Fact]
        public async Task No_final_event_is_raised_while_services_are_still_outstanding()
        {
            UpdateReturns(true);
            TrackerIs(Tracker());
            _service.Setup(s => s.AreAllServicesCompleted(It.IsAny<MigrationTracker>())).Returns(false);

            await _sut.Consume(Event());

            _service.Verify(s => s.NotifyDataMigrationEvent(
                It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task A_repository_failure_is_re_thrown_so_the_broker_can_redeliver()
        {
            // The opposite of the unparseable-name case: this one may well succeed on retry,
            // so swallowing it would lose a completion permanently.
            _repo.Setup(r => r.UpdateServiceStatusAsync(
                    It.IsAny<string>(), It.IsAny<MigrationServiceNames>(), It.IsAny<bool>(), It.IsAny<string?>()))
                 .ThrowsAsync(new InvalidOperationException("mongo unreachable"));

            var act = async () => await _sut.Consume(Event());

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("mongo unreachable");
        }
    }
}
