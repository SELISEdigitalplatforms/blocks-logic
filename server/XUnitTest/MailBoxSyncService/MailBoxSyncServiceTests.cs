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
using XUnitTest.TestHelpers;
using Blocks.Genesis.Entities.Logic;

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

            public SecureSocketOptions? SocketOptions { get; private set; }
            public SaslMechanism? Mechanism { get; private set; }

            public Task ConnectAsync(string host, int port, SecureSocketOptions options)
            {
                ConnectCalls++;
                SocketOptions = options;
                IsConnected = true;
                return Task.CompletedTask;
            }

            public Task AuthenticateAsync(SaslMechanism mechanism)
            {
                Mechanism = mechanism;
                IsAuthenticated = true;
                return Task.CompletedTask;
            }

            public List<string> Passwords { get; } = [];

            public Task AuthenticateAsync(string userName, string password)
            {
                AuthenticateCalls++;
                Passwords.Add(password);
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
                _imapClientFactory,
                MailSecretsTestDoubles.PasswordResolver());
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
            var service = new global::MailBoxSyncService.Services.MailBoxSyncService(_mockRepository.Object, _mockMessageClient.Object, factory, MailSecretsTestDoubles.PasswordResolver());

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
            var service = new global::MailBoxSyncService.Services.MailBoxSyncService(_mockRepository.Object, _mockMessageClient.Object, factory, MailSecretsTestDoubles.PasswordResolver());

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
            var service = new global::MailBoxSyncService.Services.MailBoxSyncService(_mockRepository.Object, _mockMessageClient.Object, factory, MailSecretsTestDoubles.PasswordResolver());

            await service.SyncInboxAsync(config, "tenant-123");
            first.SetDisconnected();
            await service.SyncInboxAsync(config, "tenant-123");

            factory.CreateCalls.Should().Be(2);
        }

        [Fact]
        public async Task StoreInboundAsync_StoresANewMessage_AndPublishesItsTrigger()
        {
            var stored = await _service.StoreInboundAsync(PasswordConfig(), "tenant-123", MessageWithId("new@example.com"));

            stored.Should().BeTrue();
            _mockRepository.Verify(r => r.InsertAsync(
                It.Is<MailBoxEntity>(m =>
                    m.MessageId == "new@example.com"
                    && m.MailServerConfigurationId == "config-1"
                    && m.Status == MailStatus.Received
                    && m.IsInbound),
                "tenant-123"), Times.Once);
            _mockMessageClient.Verify(c => c.SendToConsumerAsync(It.IsAny<ConsumerMessage<EmailTriggerEvent>>()), Times.Once);
        }

        [Fact]
        public async Task StoreInboundAsync_AnAlreadyStoredMessage_WritesNothing()
        {
            _mockRepository.Setup(r => r.ExistsAsync("seen@example.com", "tenant-123")).ReturnsAsync(true);

            var stored = await _service.StoreInboundAsync(PasswordConfig(), "tenant-123", MessageWithId("seen@example.com"));

            stored.Should().BeFalse();
            _mockRepository.Verify(r => r.InsertAsync(It.IsAny<MailBoxEntity>(), It.IsAny<string>()), Times.Never);
        }

        private static MimeMessage MessageWithId(string id, string body = "body")
        {
            var message = new MimeMessage();
            message.MessageId = id;
            message.Subject = id;
            message.From.Add(MailboxAddress.Parse("from@example.com"));
            message.To.Add(MailboxAddress.Parse("to@example.com"));
            message.Body = new TextPart("plain") { Text = body };
            return message;
        }

        private static MailServerConfiguration PasswordConfig() => new()
        {
            ItemId = "config-1",
            SenderUserName = "user",
            AccountPassword = "pass",
            Host = "localhost",
            Port = 993,
            EnableSSL = true
        };

        [Fact]
        public async Task SyncInboxAsync_TriggerEvent_CarriesNoRawMimeAndACappedBody()
        {
            var longBody = new string('x', global::MailBoxSyncService.Services.MailBoxSyncService.MaxTriggerBodyLength + 500);
            var fakeClient = new FakeImapClient(new List<MimeMessage> { MessageWithId("msg-big", longBody) });
            var service = new global::MailBoxSyncService.Services.MailBoxSyncService(
                _mockRepository.Object, _mockMessageClient.Object, new FakeImapClientFactory(new[] { fakeClient }), MailSecretsTestDoubles.PasswordResolver());

            ConsumerMessage<EmailTriggerEvent>? sent = null;
            _mockMessageClient
                .Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<EmailTriggerEvent>>()))
                .Callback<ConsumerMessage<EmailTriggerEvent>>(m => sent = m)
                .Returns(Task.CompletedTask);

            await service.SyncInboxAsync(PasswordConfig(), "tenant-123");

            // The stored mail keeps everything; only the event is slimmed.
            _mockRepository.Verify(r => r.InsertAsync(
                It.Is<MailBoxEntity>(m => !string.IsNullOrEmpty(m.RawMime)), "tenant-123"), Times.Once);
            sent!.Payload.Mail.RawMime.Should().BeNull();
            sent.Payload.Mail.Body!.Length.Should().Be(global::MailBoxSyncService.Services.MailBoxSyncService.MaxTriggerBodyLength);
            sent.Payload.Mail.MessageId.Should().Be("msg-big");
            sent.Payload.Mail.MailServerConfigurationId.Should().Be("config-1");
        }

        [Fact]
        public async Task SyncInboxAsync_WhenATriggerCannotBePublished_KeepsSyncingTheRestOfTheInbox()
        {
            var fakeClient = new FakeImapClient(new List<MimeMessage> { MessageWithId("msg-a"), MessageWithId("msg-b") });
            var service = new global::MailBoxSyncService.Services.MailBoxSyncService(
                _mockRepository.Object, _mockMessageClient.Object, new FakeImapClientFactory(new[] { fakeClient }), MailSecretsTestDoubles.PasswordResolver());

            _mockMessageClient
                .SetupSequence(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<EmailTriggerEvent>>()))
                .ThrowsAsync(new InvalidOperationException("message too large"))
                .Returns(Task.CompletedTask);

            await service.SyncInboxAsync(PasswordConfig(), "tenant-123");

            _mockRepository.Verify(r => r.InsertAsync(It.IsAny<MailBoxEntity>(), "tenant-123"), Times.Exactly(2));
            _mockMessageClient.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<EmailTriggerEvent>>()), Times.Exactly(2));
        }

        [Fact]
        public async Task SyncInboxAsync_WithASaslMechanism_AuthenticatesWithItInsteadOfThePassword()
        {
            var config = new MailServerConfiguration
            {
                ItemId = "config-o365",
                Provider = MailServiceProvider.Office365Smtp,
                IsInbound = true,
                Host = "outlook.office365.com",
                Port = 993,
                SecurityMode = MailSecurityMode.SslOnConnect,
                MailboxAddress = "support@contoso.com"
            };

            var fakeClient = new FakeImapClient(new List<MimeMessage>());
            var factory = new FakeImapClientFactory(new[] { fakeClient });
            var service = new global::MailBoxSyncService.Services.MailBoxSyncService(_mockRepository.Object, _mockMessageClient.Object, factory, MailSecretsTestDoubles.PasswordResolver());

            await service.SyncInboxAsync(config, "tenant-123", new SaslMechanismOAuth2("support@contoso.com", "token"));

            fakeClient.Mechanism.Should().BeOfType<SaslMechanismOAuth2>();
            fakeClient.AuthenticateCalls.Should().Be(0);
            fakeClient.SocketOptions.Should().Be(SecureSocketOptions.SslOnConnect);
        }

        // ======================= #371: AccountPasswordReference on IMAP connect =======================

        private const string Reference = "s-inbound-1";
        private const string SecretPassword = "imap-secret-9";

        private static MailServerConfiguration ReferencedConfig(string itemId = "config-1", string accountPassword = "") => new()
        {
            ItemId = itemId,
            Provider = MailServiceProvider.Zoho,
            IsInbound = true,
            SenderUserName = "user",
            AccountPassword = accountPassword,
            AccountPasswordReference = Reference,
            Host = "imap.zoho.com",
            Port = 993,
            EnableSSL = true
        };

        private global::MailBoxSyncService.Services.MailBoxSyncService SyncService(
            FakeImapClientFactory factory,
            Mock<Blocks.Secrets.ISecretService> secrets,
            CapturingLogger<global::MailBoxSyncService.Services.MailBoxSyncService>? logger = null) =>
            new(_mockRepository.Object, _mockMessageClient.Object, factory, MailSecretsTestDoubles.PasswordResolver(secrets), logger);

        [Fact]
        public async Task ReferencedConfig_ReadsOncePerConnect_AndNotWhileTheCachedClientStaysAuthenticated()
        {
            // H7, C10 (E7): first cycle 1 read and a connect; second cycle 0 reads, cached client reused.
            var seen = new List<BlocksContext?>();
            var secrets = MailSecretsTestDoubles.Answering(Reference, SecretPassword, seen);
            var client = new FakeImapClient(new List<MimeMessage>());
            var factory = new FakeImapClientFactory(new[] { client });
            var service = SyncService(factory, secrets);

            await service.SyncInboxAsync(ReferencedConfig(), "tenant-123");
            await service.SyncInboxAsync(ReferencedConfig(), "tenant-123");

            secrets.Reads().Should().Be(1);
            factory.CreateCalls.Should().Be(1);
            client.Passwords.Should().Equal(SecretPassword);
            seen.Single()!.TenantId.Should().Be("tenant-123");
            seen.Single()!.IsAuthenticated.Should().BeTrue();
        }

        [Fact]
        public async Task ReferencedConfig_ReadsAgainOnReconnect()
        {
            // C10: 1 read per IMAP (re)connect.
            var secrets = MailSecretsTestDoubles.Answering(Reference, SecretPassword);
            var first = new FakeImapClient(new List<MimeMessage>());
            var second = new FakeImapClient(new List<MimeMessage>());
            var service = SyncService(new FakeImapClientFactory(new[] { first, second }), secrets);

            await service.SyncInboxAsync(ReferencedConfig(), "tenant-123");
            first.SetDisconnected();
            await service.SyncInboxAsync(ReferencedConfig(), "tenant-123");

            secrets.Reads().Should().Be(2);
            second.Passwords.Should().Equal(SecretPassword);
        }

        [Fact]
        public async Task ReferencedConfig_ReadsInTheSyncedTenant_AndRestoresTheAmbientContext()
        {
            // H6, C5 inbound: the read tenant is the tenant being synced, never the ambient one.
            var seen = new List<BlocksContext?>();
            var service = SyncService(
                new FakeImapClientFactory(new[] { new FakeImapClient(new List<MimeMessage>()) }),
                MailSecretsTestDoubles.Answering(Reference, SecretPassword, seen));
            TestBlocksContext.Set("some-other-tenant");
            try
            {
                var ambient = BlocksContext.GetContext();

                await service.SyncInboxAsync(ReferencedConfig(), "tenant-123");

                seen.Single()!.TenantId.Should().Be("tenant-123");
                BlocksContext.GetContext().Should().BeSameAs(ambient);
            }
            finally
            {
                TestBlocksContext.Clear();
            }
        }

        [Fact]
        public async Task ReferenceAndStalePassword_UsesOnlyTheReference()
        {
            // C3, and the record is not written: the password lives in a local for the connect.
            var client = new FakeImapClient(new List<MimeMessage>());
            var service = SyncService(new FakeImapClientFactory(new[] { client }), MailSecretsTestDoubles.Answering(Reference, SecretPassword));
            var config = ReferencedConfig(accountPassword: "old-pass");

            await service.SyncInboxAsync(config, "tenant-123");

            client.Passwords.Should().Equal(SecretPassword);
            config.AccountPassword.Should().Be("old-pass");
        }

        [Fact]
        public async Task LegacyConfig_AuthenticatesWithTheStoredPassword_AndMakesNoSecretCall()
        {
            // H5, C10 inbound.
            var secrets = new Mock<Blocks.Secrets.ISecretService>(MockBehavior.Strict);
            var client = new FakeImapClient(new List<MimeMessage>());
            var service = SyncService(new FakeImapClientFactory(new[] { client }), secrets);

            await service.SyncInboxAsync(PasswordConfig(), "tenant-123");

            client.Passwords.Should().Equal("pass");
            secrets.Invocations.Should().BeEmpty();
        }

        [Fact]
        public async Task SaslConnect_WithAStrayReference_MakesNoSecretRead()
        {
            // H10 inbound: only password auth resolves AccountPasswordReference.
            var secrets = new Mock<Blocks.Secrets.ISecretService>(MockBehavior.Strict);
            var client = new FakeImapClient(new List<MimeMessage>());
            var service = SyncService(new FakeImapClientFactory(new[] { client }), secrets);

            await service.SyncInboxAsync(ReferencedConfig(), "tenant-123", new SaslMechanismOAuth2("support@contoso.com", "token"));

            secrets.Invocations.Should().BeEmpty();
            client.AuthenticateCalls.Should().Be(0);
        }

        [Theory]
        [InlineData(nameof(Blocks.Secrets.SecretNotFoundException), "credential")]
        [InlineData(nameof(Blocks.Secrets.SecretStateException), "credential")]
        [InlineData(nameof(Blocks.Secrets.SecretAccessDeniedException), "credential")]
        [InlineData(nameof(Blocks.Secrets.SecretVaultException), "vault_unavailable")]
        [InlineData(nameof(Blocks.Secrets.SecretValidationException), "unexpected")]
        [InlineData(nameof(InvalidOperationException), "unexpected")]
        public async Task ReadFailure_FailsTheCycleWithACleanException_AndCachesNoClient(string failure, string failureClass)
        {
            // C7, C4, Q3: no connection opened, no client cached, and what reaches the worker carries
            // only the failure class and the type — never the reference id or a password.
            var logger = new CapturingLogger<global::MailBoxSyncService.Services.MailBoxSyncService>();
            var secrets = MailSecretsTestDoubles.Throwing(Reference, MailSecretsTestDoubles.SecretFailure(failure, Reference));
            var factory = new FakeImapClientFactory(Array.Empty<FakeImapClient>());
            var service = SyncService(factory, secrets, logger);

            var act = () => service.SyncInboxAsync(ReferencedConfig(accountPassword: "old-pass"), "tenant-123");

            var thrown = (await act.Should().ThrowExactlyAsync<MailCredentialException>()).Which;
            thrown.FailureClass.Should().Be(failureClass);
            thrown.ExceptionType.Should().Be(failure);
            thrown.InnerException.Should().BeNull();
            thrown.ToString().Should().NotContain(Reference).And.NotContain("old-pass");
            factory.CreateCalls.Should().Be(0);
            logger.Lines.Should().ContainSingle(l => l.Contains("MAIL FAILED (credential)", StringComparison.Ordinal)
                                                     && l.Contains($"failureClass={failureClass}", StringComparison.Ordinal)
                                                     && l.Contains("configId=config-1", StringComparison.Ordinal));
            logger.All.Should().NotContain(Reference).And.NotContain("old-pass");
        }

        [Fact]
        public async Task ReadFailure_LeavesNothingCached_AndOtherConfigurationsStillSync()
        {
            // C7: the failed configuration retries its read next cycle; another configuration of
            // the same tenant connects meanwhile.
            var secrets = new Mock<Blocks.Secrets.ISecretService>(MockBehavior.Strict);
            secrets.SetupSequence(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>()))
                   .ThrowsAsync(new Blocks.Secrets.SecretVaultException("down", "get", Reference))
                   .ReturnsAsync(SecretPassword);
            var legacyClient = new FakeImapClient(new List<MimeMessage>());
            var recoveredClient = new FakeImapClient(new List<MimeMessage>());
            var factory = new FakeImapClientFactory(new[] { legacyClient, recoveredClient });
            var service = SyncService(factory, secrets);

            var failed = () => service.SyncInboxAsync(ReferencedConfig(), "tenant-123");
            await failed.Should().ThrowAsync<MailCredentialException>();
            var legacy = PasswordConfig();
            legacy.ItemId = "config-2";
            await service.SyncInboxAsync(legacy, "tenant-123");
            await service.SyncInboxAsync(ReferencedConfig(), "tenant-123");

            legacyClient.Passwords.Should().Equal("pass");
            recoveredClient.Passwords.Should().Equal(SecretPassword);
            secrets.Reads().Should().Be(2);
        }

        [Theory]
        [InlineData(MailSecurityMode.Legacy, true, SecureSocketOptions.SslOnConnect)]
        [InlineData(MailSecurityMode.Legacy, false, SecureSocketOptions.StartTls)]
        [InlineData(MailSecurityMode.SslOnConnect, false, SecureSocketOptions.SslOnConnect)]
        [InlineData(MailSecurityMode.StartTls, true, SecureSocketOptions.StartTls)]
        public void SocketOptionsFor_PrefersTheExplicitModeAndFallsBackToEnableSsl(
            MailSecurityMode mode, bool enableSsl, SecureSocketOptions expected)
        {
            var config = new MailServerConfiguration { SecurityMode = mode, EnableSSL = enableSsl };

            global::MailBoxSyncService.Services.MailBoxSyncService.SocketOptionsFor(config).Should().Be(expected);
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
