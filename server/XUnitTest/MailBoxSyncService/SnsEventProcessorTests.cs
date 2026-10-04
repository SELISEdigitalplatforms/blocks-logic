using FluentAssertions;
using MailBoxSyncService.Controllers;
using MailBoxSyncService.Entities;
using MailBoxSyncService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text;

namespace XUnitTest.MailBoxSyncService
{
    public class SnsEventProcessorTests
    {
        private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
        private readonly Mock<IMailBoxSyncService> _mockMailBoxSyncService;
        private readonly Mock<ILogger<SnsEventProcessor>> _mockLogger;
        private readonly SnsEventProcessor _processor;

        public SnsEventProcessorTests()
        {
            _mockHttpClientFactory = new Mock<IHttpClientFactory>();
            _mockMailBoxSyncService = new Mock<IMailBoxSyncService>();
            _mockLogger = new Mock<ILogger<SnsEventProcessor>>();
            _processor = new SnsEventProcessor(
                _mockHttpClientFactory.Object,
                _mockMailBoxSyncService.Object,
                _mockLogger.Object);
        }

        [Fact]
        public async Task ProcessAsync_ShouldLogWarning_WhenBodyIsEmpty()
        {
            var context = new DefaultHttpContext();
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(""));

            await _processor.ProcessAsync(context.Request);

            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("empty body")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task ProcessAsync_ShouldConfirmSubscription_WhenTypeIsSubscriptionConfirmation()
        {
            var snsMessage = """
                {
                    "Type": "SubscriptionConfirmation",
                    "SubscribeURL": "https://example.com/subscribe"
                }
                """;
            var context = new DefaultHttpContext();
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(snsMessage));
            _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

            await _processor.ProcessAsync(context.Request);

            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Confirming SNS subscription")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task ProcessAsync_ShouldProcessNotification_WhenTypeIsNotification()
        {
            var sesEventJson = """
                {
                    "mail": {
                        "headers": [
                            { "name": "X-Tenant-Id", "value": "tenant-123" }
                        ]
                    }
                }
                """;
            var snsMessage = $$"""
                {
                    "Type": "Notification",
                    "Message": {{System.Text.Json.JsonSerializer.Serialize(sesEventJson)}}
                }
                """;
            var context = new DefaultHttpContext();
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(snsMessage));

            await _processor.ProcessAsync(context.Request);

            _mockMailBoxSyncService.Verify(
                s => s.SyncOutgoingAsync(It.IsAny<SesEventNotification>(), "tenant-123"),
                Times.Once);
        }

        [Fact]
        public async Task ProcessAsync_ShouldLogWarning_WhenUnknownMessageType()
        {
            var context = new DefaultHttpContext();
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("""{ "Type": "UnknownType" }"""));

            await _processor.ProcessAsync(context.Request);

            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Unknown SNS message type")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task ProcessAsync_ShouldReturn_WhenTypeIsMissing()
        {
            var context = new DefaultHttpContext();
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{}"));

            await _processor.ProcessAsync(context.Request);

            _mockMailBoxSyncService.Verify(s => s.SyncOutgoingAsync(It.IsAny<SesEventNotification>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ProcessAsync_ShouldLogWarning_WhenNotificationMessageEmpty()
        {
            var context = new DefaultHttpContext();
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("""{ "Type": "Notification", "Message": "" }"""));

            await _processor.ProcessAsync(context.Request);

            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("SNS Notification message is empty")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task ProcessAsync_ShouldNotCallService_WhenNotificationJsonInvalid()
        {
            var snsMessage = "{ \"Type\": \"Notification\", \"Message\": " + System.Text.Json.JsonSerializer.Serialize("{ invalid }") + " }";
            var context = new DefaultHttpContext();
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(snsMessage));

            await _processor.ProcessAsync(context.Request);

            _mockMailBoxSyncService.Verify(s => s.SyncOutgoingAsync(It.IsAny<SesEventNotification>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ProcessAsync_ShouldNotCallService_WhenTenantIdMissing()
        {
            var sesEventJson = """{ "mail": { "headers": [] } }""";
            var snsMessage = $$"""{ "Type": "Notification", "Message": {{System.Text.Json.JsonSerializer.Serialize(sesEventJson)}} }""";
            var context = new DefaultHttpContext();
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(snsMessage));

            await _processor.ProcessAsync(context.Request);

            _mockMailBoxSyncService.Verify(s => s.SyncOutgoingAsync(It.IsAny<SesEventNotification>(), It.IsAny<string>()), Times.Never);
        }
    }

    public class SesEventsControllerTests
    {
        [Fact]
        public async Task Receive_ShouldReturnOk_WhenProcessingSucceeds()
        {
            var processor = new Mock<ISnsEventProcessor>();
            var controller = new SesEventsController(processor.Object, Mock.Of<ILogger<SesEventsController>>());
            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            processor.Setup(p => p.ProcessAsync(It.IsAny<HttpRequest>())).Returns(Task.CompletedTask);

            var result = await controller.Receive();

            result.Should().BeOfType<OkResult>();
            processor.Verify(p => p.ProcessAsync(It.IsAny<HttpRequest>()), Times.Once);
        }
    }
}
