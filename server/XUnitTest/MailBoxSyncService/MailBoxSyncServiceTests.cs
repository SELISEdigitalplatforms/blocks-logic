using Blocks.Genesis;
using FluentAssertions;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Shared.Enums;
using MailBoxSyncService.Entities;
using MailBoxSyncService.Services;
using MailKit.Security;
using MimeKit;
using Moq;

namespace XUnitTest.MailBoxSyncService
{
    public class MailBoxSyncServiceTests
    {
        private sealed class FakeImapFolder : IImapFolderWrapper
        {
            private readonly List<MimeMessage> _messages;

            public FakeImapFolder(List<MimeMessage> messages)
            {
                _messages = messages;
            }

            public int Count => _messages.Count;

            public Task OpenAsync(MailKit.FolderAccess access) => Task.CompletedTask;

            public Task<MimeMessage> GetMessageAsync(int index) => Task.FromResult(_messages[index]);
        }

        private sealed class FakeImapClient : IImapClientWrapper
        {
            private readonly FakeImapFolder _folder;

            public FakeImapClient(List<MimeMessage> messages)
            {
                _folder = new FakeImapFolder(messages);
            }

            public bool IsConnected { get; private set; }
            public bool IsAuthenticated { get; private set; }
            public int ConnectCalls { get; private set; }
            public int AuthenticateCalls { get; private set; }
            public int DisposeCalls { get; private set; }
            public bool ThrowOnDispose { get; set; }

            public IImapFolderWrapper Inbox => _folder;

            public Task ConnectAsync(string host, int port, SecureSocketOptions options)
            {
                ConnectCalls++;
                IsConnected = true;
                return Task.CompletedTask;
            }

            public Task AuthenticateAsync(string userName, string password)
            {
                AuthenticateCalls++;
                IsAuthenticated = true;
                return Task.CompletedTask;
            }

            public void Dispose()
            {
                DisposeCalls++;
                if (ThrowOnDispose)
                    throw new InvalidOperationException("dispose failed");
            }

            public void SetDisconnected()
            {
                IsConnected = false;
                IsAuthenticated = false;
            }
        }

        private sealed class FakeImapClientFactory : IImapClientFactory
        {
            private readonly Queue<FakeImapClient> _clients;

            public FakeImapClientFactory(IEnumerable<FakeImapClient> clients)
            {
                _clients = new Queue<FakeImapClient>(clients);
            }

            public int CreateCalls { get; private set; }

            public IImapClientWrapper Create()
            {
                CreateCalls++;
                return _clients.Dequeue();
            }
        }

        private readonly Mock<IMailRepository> _mockRepository;
        private readonly Mock<IMessageClient> _mockMessageClient;
        private readonly FakeImapClientFactory _imapClientFactory;
        private readonly global::MailBoxSyncService.Services.MailBoxSyncService _service;

        public MailBoxSyncServiceTests()
        {
            _mockRepository = new Mock<IMailRepository>();
            _mockMessageClient = new Mock<IMessageClient>();
            _imapClientFactory = new FakeImapClientFactory(new[]
            {
                new FakeImapClient(new List<MimeMessage>())
            });
            _service = new global::MailBoxSyncService.Services.MailBoxSyncService(
                _mockRepository.Object,
                _mockMessageClient.Object,
                _imapClientFactory);
        }

        [Fact]
        public async Task SyncOutgoingAsync_ShouldInsertMailEntity_WhenSesEventIsValid()
        {
            var tenantId = "tenant-123";
            var sesEvent = new SesEventNotification
            {
                EventType = "Delivery",
                Mail = new SesMail
                {
                    MessageId = "message-123",
                    Source = "sender@example.com",
                    Destination = new List<string> { "recipient@example.com" },
                    Timestamp = DateTime.UtcNow,
                    Headers = new List<SesHeader>
                    {
                        new() { Name = "Subject", Value = "Test Subject" },
                        new() { Name = "X-Mail-Body", Value = "Test Body" }
                    }
                }
            };

            await _service.SyncOutgoingAsync(sesEvent, tenantId);

            _mockRepository.Verify(r => r.InsertAsync(
                It.Is<MailBoxEntity>(m =>
                    m.MessageId == "message-123" &&
                    m.From == "sender@example.com" &&
                    m.To == "recipient@example.com" &&
                    m.Status == MailStatus.Delivered &&
                    m.Subject == "Test Subject" &&
                    m.Body == "Test Body" &&
                    m.IsInbound == false
                ),
                tenantId), Times.Once);
        }

        [Fact]
        public async Task SyncOutgoingAsync_ShouldMapStatusCorrectly_ForSendEvent()
        {
            var sesEvent = CreateSesEvent("Send");
            await _service.SyncOutgoingAsync(sesEvent, "tenant-123");
            _mockRepository.Verify(r => r.InsertAsync(
                It.Is<MailBoxEntity>(m => m.Status == MailStatus.Sent),
                "tenant-123"), Times.Once);
        }

        [Fact]
        public async Task SyncOutgoingAsync_ShouldMapStatusCorrectly_ForBounceEvent()
        {
            var sesEvent = CreateSesEvent("Bounce");
            sesEvent.Bounce = new SesBounce
            {
                BouncedRecipients = new List<BouncedRecipient>
                {
                    new() { DiagnosticCode = "550 User unknown" }
                }
            };

            await _service.SyncOutgoingAsync(sesEvent, "tenant-123");

            _mockRepository.Verify(r => r.InsertAsync(
                It.Is<MailBoxEntity>(m =>
                    m.Status == MailStatus.Bounced &&
                    m.Error == "550 User unknown"
                ),
                "tenant-123"), Times.Once);
        }

        [Fact]
        public async Task SyncOutgoingAsync_ShouldMapStatusCorrectly_ForComplaintEvent()
        {
            await _service.SyncOutgoingAsync(CreateSesEvent("Complaint"), "tenant-123");
            _mockRepository.Verify(r => r.InsertAsync(
                It.Is<MailBoxEntity>(m => m.Status == MailStatus.Complained),
                "tenant-123"), Times.Once);
        }

        [Fact]
        public async Task SyncOutgoingAsync_ShouldMapStatusCorrectly_ForRejectEvent()
        {
            await _service.SyncOutgoingAsync(CreateSesEvent("Reject"), "tenant-123");
            _mockRepository.Verify(r => r.InsertAsync(
                It.Is<MailBoxEntity>(m => m.Status == MailStatus.Rejected),
                "tenant-123"), Times.Once);
        }

        [Fact]
        public async Task SyncOutgoingAsync_ShouldHandleMultipleRecipients()
        {
            var sesEvent = CreateSesEvent("Delivery");
            sesEvent.Mail.Destination = new List<string> { "recipient1@example.com", "recipient2@example.com" };

            await _service.SyncOutgoingAsync(sesEvent, "tenant-123");

            _mockRepository.Verify(r => r.InsertAsync(It.IsAny<MailBoxEntity>(), "tenant-123"), Times.Exactly(2));
        }

        [Fact]
        public async Task SyncOutgoingAsync_ShouldNotProcess_WhenSesEventIsNull()
        {
            await _service.SyncOutgoingAsync(null, "tenant-123");
            _mockRepository.Verify(r => r.InsertAsync(It.IsAny<MailBoxEntity>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task SyncOutgoingAsync_ShouldNotProcess_WhenMessageIdIsNull()
        {
            var sesEvent = CreateSesEvent("Delivery");
            sesEvent.Mail.MessageId = null!;
            await _service.SyncOutgoingAsync(sesEvent, "tenant-123");
            _mockRepository.Verify(r => r.InsertAsync(It.IsAny<MailBoxEntity>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task SyncOutgoingAsync_ShouldMapStatusUnknown_WhenEventTypeIsUnknown()
        {
            await _service.SyncOutgoingAsync(CreateSesEvent("Unknown"), "tenant-123");
            _mockRepository.Verify(r => r.InsertAsync(
                It.Is<MailBoxEntity>(m => m.Status == MailStatus.Unknown && m.Error == null),
                "tenant-123"), Times.Once);
        }

        [Fact]
        public async Task SyncInboxAsync_ShouldInsertNewMessages_AndEnqueue()
        {
            var tenantId = "tenant-123";
            var config = new MailServerConfiguration
            {
                ItemId = "config-1",
                SenderUserName = "user",
                AccountPassword = "pass",
                Host = "localhost",
                Port = 993,
                EnableSSL = true
            };

            var message1 = new MimeMessage();
            message1.MessageId = "msg-1";
            message1.Subject = "subject-1";
            message1.From.Add(MailboxAddress.Parse("from@example.com"));
            message1.To.Add(MailboxAddress.Parse("to@example.com"));
            message1.Body = new TextPart("plain") { Text = "body-1" };
            message1.Date = DateTimeOffset.UtcNow;

            var message2 = new MimeMessage();
            message2.MessageId = "msg-2";
            message2.Subject = "subject-2";
            message2.From.Add(MailboxAddress.Parse("from2@example.com"));
            message2.To.Add(MailboxAddress.Parse("to2@example.com"));
            message2.Body = new TextPart("plain") { Text = "body-2" };
            message2.Date = DateTimeOffset.UtcNow;

            var fakeClient = new FakeImapClient(new List<MimeMessage> { message1, message2 });
            var factory = new FakeImapClientFactory(new[] { fakeClient });
            var service = new global::MailBoxSyncService.Services.MailBoxSyncService(_mockRepository.Object, _mockMessageClient.Object, factory);

            _mockRepository.Setup(r => r.ExistsAsync("msg-1", tenantId)).ReturnsAsync(false);
            _mockRepository.Setup(r => r.ExistsAsync("msg-2", tenantId)).ReturnsAsync(true);

            await service.SyncInboxAsync(config, tenantId);

            _mockRepository.Verify(r => r.InsertAsync(It.IsAny<MailBoxEntity>(), tenantId), Times.Once);
            _mockMessageClient.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<EmailTriggerEvent>>()), Times.Once);
        }

        [Fact]
        public async Task SyncInboxAsync_ShouldSkip_WhenMessageIdMissing()
        {
            var config = new MailServerConfiguration
            {
                ItemId = "config-1",
                SenderUserName = "user",
                AccountPassword = "pass",
                Host = "localhost",
                Port = 993,
                EnableSSL = true
            };

            var message = new MimeMessage();
            message.Subject = "subject";
            message.Body = new TextPart("plain") { Text = "body" };
            message.Headers.Remove(HeaderId.MessageId);

            var fakeClient = new FakeImapClient(new List<MimeMessage> { message });
            var factory = new FakeImapClientFactory(new[] { fakeClient });
            var service = new global::MailBoxSyncService.Services.MailBoxSyncService(_mockRepository.Object, _mockMessageClient.Object, factory);

            await service.SyncInboxAsync(config, "tenant-123");

            _mockRepository.Verify(r => r.InsertAsync(It.IsAny<MailBoxEntity>(), It.IsAny<string>()), Times.Never);
            _mockMessageClient.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<EmailTriggerEvent>>()), Times.Never);
        }

        [Fact]
        public async Task SyncInboxAsync_ShouldReconnect_WhenClientDisconnected()
        {
            var config = new MailServerConfiguration
            {
                ItemId = "config-1",
                SenderUserName = "user",
                AccountPassword = "pass",
                Host = "localhost",
                Port = 993,
                EnableSSL = true
            };

            var first = new FakeImapClient(new List<MimeMessage>());
            first.SetDisconnected();
            var second = new FakeImapClient(new List<MimeMessage>());
            var factory = new FakeImapClientFactory(new[] { first, second });
            var service = new global::MailBoxSyncService.Services.MailBoxSyncService(_mockRepository.Object, _mockMessageClient.Object, factory);

            await service.SyncInboxAsync(config, "tenant-123");
            first.SetDisconnected();
            await service.SyncInboxAsync(config, "tenant-123");

            factory.CreateCalls.Should().Be(2);
        }

        private static SesEventNotification CreateSesEvent(string eventType)
        {
            return new SesEventNotification
            {
                EventType = eventType,
                Mail = new SesMail
                {
                    MessageId = "message-123",
                    Source = "sender@example.com",
                    Destination = new List<string> { "recipient@example.com" },
                    Timestamp = DateTime.UtcNow
                }
            };
        }
    }
}
