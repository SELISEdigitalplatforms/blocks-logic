using DomainService.Entities;
using DomainService.Notification;
using DomainService.Shared;
using FluentAssertions;
using FirebaseConfiguration = DomainService.Configuration.FirebaseConfiguration;
using GetConfigurationsRequest = DomainService.Configuration.GetConfigurationsRequest;
using GetConfigurationsResponse = DomainService.Configuration.GetConfigurationsResponse;
using SaveConfigurationRequest = DomainService.Configuration.SaveConfigurationRequest;

namespace XUnitTest.Notification
{
    /// <summary>
    /// Tests for the notification contracts: the entities that are persisted and the request and
    /// response shapes that cross the wire. They pin the defaults other components rely on.
    /// </summary>
    public class NotificationContractsTests
    {
        [Fact]
        public void NotificationConnection_StampsItsCreationTime()
        {
            var before = DateTime.Now.AddSeconds(-1);

            var connection = new NotificationConnection { Id = "id-1", ConnectionId = "conn-1" };

            connection.CreatedTime.Should().BeOnOrAfter(before);
            connection.UserId.Should().BeNull("a connection may belong to an anonymous client");
        }

        [Fact]
        public void Subscription_AlwaysCarriesAPayload()
        {
            var subscription = new Subscription();

            subscription.Payload.Should().NotBeNull();
            subscription.Payload.SubscriptionFilters.Should().BeNull();
        }

        [Fact]
        public void SubscriptionFilter_DefaultsToAnEmptyFilter()
        {
            var filter = new SubscriptionFilter();

            filter.Context.Should().BeEmpty();
            filter.ActionName.Should().BeEmpty();
            filter.Value.Should().BeEmpty();
        }

        [Fact]
        public void NotificationSubscription_IsASubscriptionFilterWithAnOwner()
        {
            var subscription = new NotificationSubscription
            {
                Id = "sub-1",
                ConnectionId = "conn-1",
                UserId = "user-1",
                Context = "orders",
                ActionName = "created",
                Value = "1",
                CreatedTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            };

            subscription.Should().BeAssignableTo<SubscriptionFilter>();
            subscription.Context.Should().Be("orders");
            subscription.UserId.Should().Be("user-1");
        }

        [Fact]
        public void NotifyRequest_IsANotifierPayloadWithAConfigurationAndAResponse()
        {
            var request = new NotifyRequest
            {
                ConnectionId = "conn-1",
                UserIds = ["user-1"],
                Roles = ["admin"],
                SubscriptionFilters = [new SubscriptionFilter { Context = "orders" }],
                DenormalizedPayload = "{}",
                SaveDenormalizedPayloadAsAnObject = true,
                ResponseKey = "order",
                ResponseValue = "42",
                ContentAvailable = true,
                ConfigurationName = "cfg",
            };

            request.Should().BeAssignableTo<NotifierPayload>();
            request.ConfigurationName.Should().Be("cfg");
            request.ResponseKey.Should().Be("order");
            request.ResponseValue.Should().Be("42");
            request.ContentAvailable.Should().BeTrue();
            request.Roles.Should().Equal("admin");
            request.SaveDenormalizedPayloadAsAnObject.Should().BeTrue();
        }

        [Fact]
        public void OfflineNotification_KeepsItsReadStateOutsideThePayload()
        {
            var notification = new OfflineNotification
            {
                Id = "n-1",
                CorrelationId = "c-1",
                CreatedTime = DateTime.UtcNow,
                Payload = new PayloadData
                {
                    UserId = "user-1",
                    NotificationType = NotificationReceiverTypes.UserSpecificReceiverType.ToString(),
                    ResponseKey = "order",
                    ResponseValue = "42",
                    SubscriptionFilters = [new SubscriptionFilter { Context = "orders" }],
                },
                DenormalizedPayload = "{}",
                ReadByUserIds = ["user-1"],
                ReadByRoles = ["admin"],
                IsRead = true,
            };

            notification.Payload.UserId.Should().Be("user-1");
            notification.Payload.SubscriptionFilters.Should().ContainSingle();
            notification.ReadByUserIds.Should().Equal("user-1");
            notification.ReadByRoles.Should().Equal("admin");
            notification.IsRead.Should().BeTrue();
        }

        [Fact]
        public void GetNotificationsRequest_CanAskForUnreadItemsOnly()
        {
            var request = new GetNotificationsRequest { IsUnreadOnly = true, Page = 2, PageSize = 25 };

            request.IsUnreadOnly.Should().BeTrue();
            request.Page.Should().Be(2);
            request.PageSize.Should().Be(25);
        }

        [Fact]
        public void GetNotificationsResponse_CarriesBothCounters()
        {
            var response = new GetNotificationsResponse
            {
                Notifications = [new OfflineNotification { Id = "n-1" }],
                UnReadNotificationsCount = 1,
                TotalNotificationsCount = 3,
            };

            response.Notifications.Should().ContainSingle();
            response.UnReadNotificationsCount.Should().Be(1);
            response.TotalNotificationsCount.Should().Be(3);
        }

        [Fact]
        public void GetUnreadNotificationsRequestBySubscriptionFilter_CarriesTheOrdering()
        {
            var request = new GetUnreadNotificationsRequestBySubscriptionFilter
            {
                UserId = "user-1",
                SubscriptionFilterData = new SubscriptionFilter { Context = "orders" },
                OrderBy = OfflineNotificationOrder.ReadStatus,
            };

            request.UserId.Should().Be("user-1");
            request.SubscriptionFilterData.Context.Should().Be("orders");
            request.OrderBy.Should().Be(OfflineNotificationOrder.ReadStatus);
        }

        [Fact]
        public void MarkNotificationAsReadRequest_IdentifiesASingleNotification()
        {
            var request = new MarkNotificationAsReadRequest { Id = "n-1" };

            request.Id.Should().Be("n-1");
        }

        [Fact]
        public void NotificationConfiguration_DescribesAChannelAndAReceiver()
        {
            var configuration = new NotificationConfiguration
            {
                ItemId = "cfg-1",
                Name = "welcome",
                ChannelToNotify = NotifierTypes.Firebase,
                NotificationType = NotificationReceiverTypes.FilterSpecificReceiverType,
                NotifyMethod = "ReceiveNotification",
                EnablePersistence = true,
            };

            configuration.Name.Should().Be("welcome");
            configuration.ChannelToNotify.Should().Be(NotifierTypes.Firebase);
            configuration.NotificationType.Should().Be(NotificationReceiverTypes.FilterSpecificReceiverType);
            configuration.NotifyMethod.Should().Be("ReceiveNotification");
            configuration.EnablePersistence.Should().BeTrue();
        }

        [Fact]
        public void SaveConfigurationRequest_MarksWhetherItUpdatesAnExistingConfiguration()
        {
            var request = new SaveConfigurationRequest
            {
                Name = "welcome",
                ChannelToNotify = NotifierTypes.SignalR,
                NotificationType = NotificationReceiverTypes.BroadcastReceiverType,
                NotifyMethod = "ReceiveNotification",
                EnablePersistence = false,
                ProjectKey = "project-1",
                IsUpdateRequest = true,
            };

            request.IsUpdateRequest.Should().BeTrue();
            request.ProjectKey.Should().Be("project-1");
            request.EnablePersistence.Should().BeFalse();
        }

        [Fact]
        public void GetConfigurationsRequest_CarriesTheProjectItBelongsTo()
        {
            var request = new GetConfigurationsRequest { ProjectKey = "project-1", Page = 1, PageSize = 10 };

            request.ProjectKey.Should().Be("project-1");
            request.Page.Should().Be(1);
            request.PageSize.Should().Be(10);
        }

        [Fact]
        public void GetConfigurationsResponse_CarriesThePageAndTheTotal()
        {
            var response = new GetConfigurationsResponse
            {
                Configurations = [new NotificationConfiguration { ItemId = "cfg-1" }],
                TotalCount = 5,
                IsSuccess = true,
            };

            response.Configurations.Should().ContainSingle();
            response.TotalCount.Should().Be(5);
            response.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public void FirebaseConfiguration_CarriesTheAuthorizationKey()
        {
            var configuration = new FirebaseConfiguration { ItemId = "fb-1", AuthorizationKey = "auth-key" };

            configuration.AuthorizationKey.Should().Be("auth-key");
            configuration.ItemId.Should().Be("fb-1");
        }

        [Fact]
        public void NotificationReceiverTypes_KeepsTheOrderTheStoredConfigurationsRelyOn()
        {
            ((int)NotificationReceiverTypes.NoReceiverType).Should().Be(0);
            ((int)NotificationReceiverTypes.BroadcastReceiverType).Should().Be(1);
            ((int)NotificationReceiverTypes.UserSpecificReceiverType).Should().Be(2);
            ((int)NotificationReceiverTypes.FilterSpecificReceiverType).Should().Be(3);
        }

        [Fact]
        public void NotifierTypes_KeepsTheOrderTheStoredConfigurationsRelyOn()
        {
            ((int)NotifierTypes.SignalR).Should().Be(0);
            ((int)NotifierTypes.Firebase).Should().Be(1);
        }

        [Fact]
        public void OfflineNotificationOrder_IsOneBased()
        {
            ((int)OfflineNotificationOrder.CreatedTime).Should().Be(1);
            ((int)OfflineNotificationOrder.ReadStatus).Should().Be(2);
        }
    }
}
