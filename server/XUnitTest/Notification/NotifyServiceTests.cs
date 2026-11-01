using Blocks.Genesis.Entities.Logic;
using Blocks.Genesis.Notifications;
using DomainService.Notification;
using DomainService.Shared;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Notification
{
    /// <summary>HTTP Notify: validated as before, then handed to the genesis publisher and answered at once.</summary>
    public class NotifyServiceTests
    {
        private readonly Mock<IValidator<NotifyRequest>> _validator = new();
        private readonly List<NotificationRequest> _published = [];
        private readonly NotifyService _sut;

        public NotifyServiceTests()
        {
            _validator.Setup(v => v.ValidateAsync(It.IsAny<NotifyRequest>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new ValidationResult());
            var publisher = new Mock<INotificationPublisher>();
            publisher.Setup(p => p.Publish(It.IsAny<NotificationRequest>())).Callback<NotificationRequest>(_published.Add);
            _sut = new NotifyService(_validator.Object, publisher.Object, NullLogger<NotifyService>.Instance);
        }

        [Fact]
        public async Task AValidRequest_IsPublishedWithEverythingAPageMatchesOn()
        {
            var filters = new List<SubscriptionFilter> { new() { Context = "orders", ActionName = "created", Value = "1" } };

            var result = await _sut.NotifyAsync(new NotifyRequest
            {
                ConfigurationName = " order-created ",
                UserIds = ["u1"],
                OrganizationIds = ["", "org-1"],
                DenormalizedPayload = "{\"orderId\":\"o-1\"}",
                SubscriptionFilters = filters,
                ResponseKey = "job-7",
                ResponseValue = "done",
            });

            result.IsSuccess.Should().BeTrue();
            var request = _published.Single();
            request.Name.Should().Be("order-created");
            request.NamedUserIds.Should().Equal("u1");
            request.OrganizationId.Should().Be("org-1");
            request.Payload.Should().Be("{\"orderId\":\"o-1\"}");
            request.SubscriptionFilters.Should().BeEquivalentTo(filters);
            request.ResponseKey.Should().Be("job-7");
            request.ResponseValue.Should().Be("done");
        }

        [Fact]
        public async Task AnInvalidRequest_ReturnsTheErrors_AndPublishesNothing()
        {
            _validator.Setup(v => v.ValidateAsync(It.IsAny<NotifyRequest>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new ValidationResult([
                          new ValidationFailure("ConfigurationName", "no_configuration_exist"),
                          new ValidationFailure("ConfigurationName", "second message for the same field"),
                      ]));

            var result = await _sut.NotifyAsync(new NotifyRequest { ConfigurationName = "missing" });

            result.IsSuccess.Should().BeFalse();
            result.Errors!["ConfigurationName"].Should().Be("no_configuration_exist");
            _published.Should().BeEmpty();
        }
    }
}
