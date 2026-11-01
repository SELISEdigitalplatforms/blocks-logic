using Blocks.Genesis;
using DomainService.Notification;
using DomainService.Shared;
using DomainService.Configuration.Services;
using DomainService.Entities;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Linq.Expressions;
using Blocks.Genesis.Entities.Logic;
using Blocks.Genesis.Entities.Os;

namespace XUnitTest.Notifications
{
    /// <summary>
    /// Unit tests for <see cref="NotificationService"/>. Both validators, the repository, the
    /// notifier factory and the configuration repository are mocked, so the subscription, connection,
    /// notify and read-state paths are exercised without Mongo or SignalR.
    /// </summary>
    public class NotificationServiceTests : IDisposable
    {
        private readonly Mock<INotificationRepository> _repo = new();
        private readonly Mock<IValidator<Subscription>> _subscriptionValidator = new();
        private readonly Mock<INotificationUserRepository> _users = new();
        private readonly NotificationService _sut;

        public NotificationServiceTests()
        {
            _subscriptionValidator
                .Setup(v => v.ValidateAsync(It.IsAny<Subscription>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _users.Setup(u => u.GetOrganizationIdsAsync(It.IsAny<string>())).ReturnsAsync(["org-1"]);

            BlocksContext.IsTestMode = true;

            _sut = new NotificationService(
                _repo.Object,
                _subscriptionValidator.Object,
                _users.Object,
                NullLogger<NotificationService>.Instance);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        /// <summary>
        /// Positional call to match the overload this repo's other tests use; the third argument is
        /// the user id and the last is the original tenant id.
        /// </summary>
        private static void SetUser(string userId) =>
            BlocksContext.SetContext(BlocksContext.Create(
                "t1", null, userId, true, null, null,
                DateTime.UtcNow.AddHours(1), null, null, null, null, null, null, "", "t1"));

        private static ValidationResult Invalid(string property, string message) =>
            new([new ValidationFailure(property, message)]);

        private static Subscription Sub(params (string Context, string Action, string Value)[] filters) => new()
        {
            Payload = new NotifierPayload
            {
                ConnectionId = "conn-1",
                UserIds = ["user-1"],
                SubscriptionFilters = filters
                    .Select(f => new SubscriptionFilter { Context = f.Context, ActionName = f.Action, Value = f.Value })
                    .ToList(),
            },
        };

        [Fact]
        public async Task AddSubscriptionAsync_StoresOneRowPerFilter()
        {
            List<NotificationSubscription>? saved = null;
            _repo.Setup(r => r.SaveAsync(It.IsAny<List<NotificationSubscription>>()))
                 .Callback<List<NotificationSubscription>>(l => saved = l)
                 .Returns(Task.CompletedTask);

            var result = await _sut.AddSubscriptionAsync(Sub(("orders", "created", "1"), ("orders", "updated", "2")));

            result.IsSuccess.Should().BeTrue();
            saved.Should().HaveCount(2);
            saved!.Should().OnlyContain(s => s.ConnectionId == "conn-1" && s.UserId == "user-1");
            saved!.Select(s => s.Id).Should().OnlyHaveUniqueItems();
            saved!.Should().Contain(s => s.ActionName == "created" && s.Context == "orders" && s.Value == "1");
        }

        [Fact]
        public async Task AddSubscriptionAsync_ReturnsTheValidationErrorsAndStoresNothing()
        {
            _subscriptionValidator
                .Setup(v => v.ValidateAsync(It.IsAny<Subscription>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Invalid("Payload.ConnectionId", "ConnectionId is required"));

            var result = await _sut.AddSubscriptionAsync(Sub(("orders", "created", "1")));

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("Payload.ConnectionId");
            _repo.Verify(r => r.SaveAsync(It.IsAny<List<NotificationSubscription>>()), Times.Never);
        }

        [Fact]
        public async Task CreateConnectionAsync_StoresTheConnectionWithTheContextUser()
        {
            SetUser("user-9");

            NotificationConnection? saved = null;
            _repo.Setup(r => r.SaveAsync(It.IsAny<NotificationConnection>(), It.IsAny<string>()))
                 .Callback<NotificationConnection, string>((c, _) => saved = c)
                 .Returns(Task.CompletedTask);

            await _sut.CreateConnectionAsync("conn-abc");

            saved.Should().NotBeNull();
            saved!.ConnectionId.Should().Be("conn-abc");
            saved.UserId.Should().Be("user-9");
            saved.Id.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public async Task CreateConnectionAsync_LeavesTheUserNullForAnAnonymousConnection()
        {
            NotificationConnection? saved = null;
            _repo.Setup(r => r.SaveAsync(It.IsAny<NotificationConnection>(), It.IsAny<string>()))
                 .Callback<NotificationConnection, string>((c, _) => saved = c)
                 .Returns(Task.CompletedTask);

            await _sut.CreateConnectionAsync("conn-anon");

            saved!.UserId.Should().BeNull("an unauthenticated connection has no user to attribute it to");
        }

        [Fact]
        public async Task RemoveCollectionAsync_DropsBothTheConnectionAndItsSubscriptions()
        {
            await _sut.RemoveCollectionAsync("conn-1");

            _repo.Verify(r => r.DeleteAsync<NotificationConnection>(
                It.IsAny<Expression<Func<NotificationConnection, bool>>>()), Times.Once);
            _repo.Verify(r => r.DeleteAsync<NotificationSubscription>(
                It.IsAny<Expression<Func<NotificationSubscription, bool>>>()), Times.Once);
        }

        [Fact]
        public async Task RemoveSubscriptionAsync_DeletesOnePerFilter()
        {
            var result = await _sut.RemoveSubscriptionAsync(Sub(("orders", "created", "1"), ("orders", "updated", "2")));

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.DeleteAsync<NotificationSubscription>(
                It.IsAny<Expression<Func<NotificationSubscription, bool>>>()), Times.Exactly(2));
        }

        [Fact]
        public async Task RemoveSubscriptionAsync_ReturnsTheValidationErrorsAndDeletesNothing()
        {
            _subscriptionValidator
                .Setup(v => v.ValidateAsync(It.IsAny<Subscription>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Invalid("Payload", "Payload is required"));

            var result = await _sut.RemoveSubscriptionAsync(Sub(("orders", "created", "1")));

            result.IsSuccess.Should().BeFalse();
            _repo.Verify(r => r.DeleteAsync<NotificationSubscription>(
                It.IsAny<Expression<Func<NotificationSubscription, bool>>>()), Times.Never);
        }

        [Fact]
        public async Task RemoveSubscriptionAsync_HandlesASubscriptionWithNoFilters()
        {
            var result = await _sut.RemoveSubscriptionAsync(new Subscription
            {
                Payload = new NotifierPayload { ConnectionId = "conn-1", SubscriptionFilters = null },
            });

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.DeleteAsync<NotificationSubscription>(
                It.IsAny<Expression<Func<NotificationSubscription, bool>>>()), Times.Never);
        }

        [Fact]
        public async Task MarkAllNotificationAsRead_UpdatesEveryNotificationForTheContextUser()
        {
            SetUser("user-9");

            var result = await _sut.MarkAllNotificationAsRead();

            result.Should().NotBeNull();
            _repo.Verify(r => r.MarkAllAsReadAsync(It.Is<InboxViewer>(v => v.UserId == "user-9" && v.OrganizationIds.Contains("org-1"))), Times.Once);
        }

        [Fact]
        public async Task MarkNotificationAsRead_UpdatesTheSingleNotification()
        {
            SetUser("user-9");

            var result = await _sut.MarkNotificationAsRead(new MarkNotificationAsReadRequest { Id = "n-1" });

            result.Should().NotBeNull();
            _repo.Verify(r => r.MarkAsReadAsync(It.Is<InboxViewer>(v => v.UserId == "user-9"), "n-1"), Times.Once);
        }

        [Fact]
        public async Task GetNotificationsAsync_ReadsAsTheTokenUserWithTheirOrganizations()
        {
            SetUser("user-9");
            var request = new GetNotificationsRequest();
            _repo.Setup(r => r.GetNotificationsAsync(request, It.IsAny<InboxViewer>()))
                 .ReturnsAsync(new GetNotificationsResponse());

            var result = await _sut.GetNotificationsAsync(request);

            result.Should().NotBeNull();
            _repo.Verify(r => r.GetNotificationsAsync(request,
                It.Is<InboxViewer>(v => v.UserId == "user-9" && v.OrganizationIds.Contains("org-1") && v.ProjectTenantId == null)), Times.Once);
        }

        [Fact]
        public async Task GetNotificationsAsync_WithoutAUser_IsRefused_NeverEveryRowWithoutAUser()
        {
            var act = () => _sut.GetNotificationsAsync(new GetNotificationsRequest());

            await act.Should().ThrowAsync<InvalidOperationException>();
            _repo.Verify(r => r.GetNotificationsAsync(It.IsAny<GetNotificationsRequest>(), It.IsAny<InboxViewer>()), Times.Never);
        }

        [Fact]
        public async Task GetNotificationsAsync_ImpersonatedRootUser_ReadsOnlyTheProjectTheyWorkIn()
        {
            BlocksContext.SetContext(BlocksContext.Create(
                "project-1", null, "root-user", true, null, null,
                DateTime.UtcNow.AddHours(1), null, null, null, null, null, null, "", "root", true));
            _repo.Setup(r => r.GetNotificationsAsync(It.IsAny<GetNotificationsRequest>(), It.IsAny<InboxViewer>()))
                 .ReturnsAsync(new GetNotificationsResponse());

            await _sut.GetNotificationsAsync(new GetNotificationsRequest());

            _repo.Verify(r => r.GetNotificationsAsync(It.IsAny<GetNotificationsRequest>(),
                It.Is<InboxViewer>(v => v.UserId == "root-user" && v.ProjectTenantId == "project-1")), Times.Once);
        }
    }
}
