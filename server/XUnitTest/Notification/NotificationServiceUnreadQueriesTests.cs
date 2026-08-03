using System.Linq.Expressions;
using DomainService.Notification;
using DomainService.Shared;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using Moq;
using XUnitTest.TestHelpers;
using IConfigurationRepository = DomainService.Configuration.Services.IConfigurationRepository;

namespace XUnitTest.Notification
{
    /// <summary>
    /// Unit tests for the read state queries of <see cref="NotificationService"/>: the ordering
    /// of the unread notifications of a subscription filter and the failure handling of the
    /// mark as read operations. The repository is mocked, so the tests assert the selection and
    /// the ordering the service applies on top of it.
    /// </summary>
    public class NotificationServiceUnreadQueriesTests : IDisposable
    {
        private const string UserId = "user-unread";

        private readonly Mock<INotificationRepository> _repository = new();
        private readonly NotificationService _sut;

        public NotificationServiceUnreadQueriesTests()
        {
            var subscriptionValidator = new Mock<IValidator<Subscription>>();
            subscriptionValidator
                .Setup(v => v.ValidateAsync(It.IsAny<Subscription>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            var notifyValidator = new Mock<IValidator<NotifyRequest>>();
            notifyValidator
                .Setup(v => v.ValidateAsync(It.IsAny<NotifyRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());

            TestBlocksContext.Set("tenant-unread", UserId);

            _sut = new NotificationService(
                _repository.Object,
                subscriptionValidator.Object,
                notifyValidator.Object,
                Mock.Of<ILogger<NotificationService>>(),
                Mock.Of<INotifierServiceFactory>(),
                Mock.Of<IConfigurationRepository>());
        }

        public void Dispose()
        {
            TestBlocksContext.Clear();
            GC.SuppressFinalize(this);
        }

        private void SetupNotifications(params OfflineNotification[] notifications) =>
            _repository.Setup(r => r.GetItemsAsync(
                    It.IsAny<Expression<Func<OfflineNotification, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(notifications.ToList());

        private static OfflineNotification Notification(
            string id, DateTime createdTime, List<string>? readBy = null) => new()
            {
                Id = id,
                CorrelationId = "corr-1",
                CreatedTime = createdTime,
                ReadByUserIds = readBy,
                DenormalizedPayload = "{}",
                Payload = new PayloadData
                {
                    UserId = UserId,
                    NotificationType = NotificationReceiverTypes.UserSpecificReceiverType.ToString(),
                    SubscriptionFilters = [new SubscriptionFilter { Context = "orders" }],
                },
            };

        private static GetUnreadNotificationsRequestBySubscriptionFilter Request(
            OfflineNotificationOrder order, string? context) => new()
            {
                UserId = UserId,
                OrderBy = order,
                SubscriptionFilterData = context is null
                    ? null!
                    : new SubscriptionFilter { Context = context, ActionName = "created", Value = "1" },
            };

        [Fact]
        public async Task GetUnreadNotificationsBySubscriptionFilter_OrdersASingleContextNewestFirst()
        {
            var now = DateTime.UtcNow;
            SetupNotifications(
                Notification("n-old", now.AddMinutes(-10)),
                Notification("n-new", now),
                Notification("n-middle", now.AddMinutes(-5)));

            var result = await _sut.GetUnreadNotificationsBySubscriptionFilter(
                Request(OfflineNotificationOrder.CreatedTime, "orders"));

            result.Select(n => n.Id).Should().Equal("n-new", "n-middle", "n-old");
            _repository.Verify(r => r.GetItemsAsync(
                It.IsAny<Expression<Func<OfflineNotification, bool>>>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task GetUnreadNotificationsBySubscriptionFilter_OrdersSeveralContextsNewestFirst()
        {
            var now = DateTime.UtcNow;
            SetupNotifications(
                Notification("n-old", now.AddMinutes(-10)),
                Notification("n-new", now));

            var result = await _sut.GetUnreadNotificationsBySubscriptionFilter(
                Request(OfflineNotificationOrder.CreatedTime, "orders,invoices"));

            result.Select(n => n.Id).Should().Equal("n-new", "n-old");
        }

        [Fact]
        public async Task GetUnreadNotificationsBySubscriptionFilter_MarksWhatTheUserHasAlreadyRead()
        {
            var now = DateTime.UtcNow;
            SetupNotifications(
                Notification("n-read", now, [UserId]),
                Notification("n-unread", now.AddMinutes(-1)),
                Notification("n-read-by-others", now.AddMinutes(-2), ["someone-else"]));

            var result = await _sut.GetUnreadNotificationsBySubscriptionFilter(
                Request(OfflineNotificationOrder.CreatedTime, "orders"));

            result.Single(n => n.Id == "n-read").IsRead.Should().BeTrue();
            result.Single(n => n.Id == "n-unread").IsRead.Should().BeFalse();
            result.Single(n => n.Id == "n-read-by-others").IsRead.Should().BeFalse();
        }

        [Fact]
        public async Task GetUnreadNotificationsBySubscriptionFilter_ReturnsNothingWithoutAFilter()
        {
            SetupNotifications(Notification("n-1", DateTime.UtcNow));

            var result = await _sut.GetUnreadNotificationsBySubscriptionFilter(
                Request(OfflineNotificationOrder.CreatedTime, null));

            result.Should().BeEmpty("without a filter there is nothing to select on");
            _repository.Verify(r => r.GetItemsAsync(
                It.IsAny<Expression<Func<OfflineNotification, bool>>>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task GetUnreadNotificationsBySubscriptionFilter_PutsTheUnreadFirstWhenOrderedByReadState()
        {
            var now = DateTime.UtcNow;
            SetupNotifications(
                Notification("n-read", now, [UserId]),
                Notification("n-unread", now.AddMinutes(-5)));

            var result = await _sut.GetUnreadNotificationsBySubscriptionFilter(
                Request(OfflineNotificationOrder.ReadStatus, "orders"));

            result.Select(n => n.Id).Should().Equal("n-unread", "n-read");
            result.Single(n => n.Id == "n-read").IsRead.Should().BeTrue();
        }

        [Fact]
        public async Task GetUnreadNotificationsBySubscriptionFilter_ReturnsAnEmptyListWhenNothingMatchesTheReadOrder()
        {
            SetupNotifications();

            var result = await _sut.GetUnreadNotificationsBySubscriptionFilter(
                Request(OfflineNotificationOrder.ReadStatus, "orders"));

            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetUnreadNotificationsBySubscriptionFilter_ReturnsNothingForAnUnknownOrder()
        {
            SetupNotifications(Notification("n-1", DateTime.UtcNow));

            var result = await _sut.GetUnreadNotificationsBySubscriptionFilter(new()
            {
                UserId = UserId,
                SubscriptionFilterData = new SubscriptionFilter { Context = "orders" },
                OrderBy = 0,
            });

            result.Should().BeEmpty();
            _repository.Verify(r => r.GetItemsAsync(
                It.IsAny<Expression<Func<OfflineNotification, bool>>>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task MarkAllNotificationAsRead_ReportsTheFailureInsteadOfThrowing()
        {
            _repository.Setup(r => r.UpdateNotificationAsReadByUserIdAsync(It.IsAny<string>()))
                       .ThrowsAsync(new InvalidOperationException("mongo down"));

            var result = await _sut.MarkAllNotificationAsRead();

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("Exception");
            result.Errors["Exception"].Should().Contain("mongo down");
        }

        [Fact]
        public async Task MarkNotificationAsRead_ReportsTheFailureInsteadOfThrowing()
        {
            _repository.Setup(r => r.UpdateNotificationAsReadByUserIdAsync(It.IsAny<string>(), It.IsAny<string>()))
                       .ThrowsAsync(new InvalidOperationException("mongo down"));

            var result = await _sut.MarkNotificationAsRead(new MarkNotificationAsReadRequest { Id = "n-1" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("Exception");
            result.Errors["Exception"].Should().Contain("mongo down");
        }
    }
}
