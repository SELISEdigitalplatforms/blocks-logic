using Blocks.Genesis;
using DomainService.Dtos;
using DomainService.Entities;
using DomainService.Migration;
using DomainService.Migration.Services;
using DomainService.Shared;
using DomainService.Subscription.RequestModel;
using DomainService.Subscription.Services;
using FluentAssertions;
using Moq;

namespace XUnitTest.Identifier
{
    public class EncryptionHelperTests
    {
        [Fact]
        public void EncryptThenDecrypt_RoundTripsThePlainText()
        {
            const string plainText = "Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=policy";

            var cipherText = EncryptionHelper.Encrypt(plainText, "tenant-salt");

            cipherText.Should().NotBe(plainText);
            EncryptionHelper.Decrypt(cipherText, "tenant-salt").Should().Be(plainText);
        }

        [Fact]
        public void Encrypt_ProducesADifferentCipherTextEachTime()
        {
            var first = EncryptionHelper.Encrypt("same value", "tenant-salt");
            var second = EncryptionHelper.Encrypt("same value", "tenant-salt");

            first.Should().NotBe(second, "a fresh IV is generated for every call");
            EncryptionHelper.Decrypt(first, "tenant-salt").Should().Be(EncryptionHelper.Decrypt(second, "tenant-salt"));
        }

        [Fact]
        public void Encrypt_KeyLongerThanThirtyTwoBytes_IsTruncatedConsistently()
        {
            var longKey = new string('k', 64);

            var cipherText = EncryptionHelper.Encrypt("payload", longKey);

            EncryptionHelper.Decrypt(cipherText, longKey).Should().Be("payload");
            EncryptionHelper.Decrypt(cipherText, new string('k', 32)).Should().Be("payload");
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void Encrypt_EmptyOrNullPlainText_ReturnsInput(string? plainText)
        {
            EncryptionHelper.Encrypt(plainText!, "tenant-salt").Should().Be(plainText);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void Decrypt_EmptyOrNullCipherText_ReturnsInput(string? cipherText)
        {
            EncryptionHelper.Decrypt(cipherText!, "tenant-salt").Should().Be(cipherText);
        }
    }

    public class IdentifierConstantsTests
    {
        [Theory]
        [InlineData("amqp://<username>:<password>@localhost:5672")]
        [InlineData("AMQPS://guest:guest@rabbit.test:5671")]
        public void GetMessageConfiguration_AmqpConnectionString_BuildsRabbitMqConfiguration(string connectionString)
        {
            var configuration = IdentifierConstants.GetMessageConfiguration(connectionString);

            configuration.RabbitMqConfiguration.Should().NotBeNull();
            configuration.RabbitMqConfiguration!.ConsumerSubscriptions.Should().HaveCount(3);
        }

        [Theory]
        [InlineData("Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=policy")]
        [InlineData("not-a-uri")]
        [InlineData("https://example.test")]
        public void GetMessageConfiguration_NonAmqpConnectionString_BuildsAzureServiceBusConfiguration(string connectionString)
        {
            var configuration = IdentifierConstants.GetMessageConfiguration(connectionString);

            configuration.AzureServiceBusConfiguration.Should().NotBeNull();
            configuration.AzureServiceBusConfiguration!.Queues.Should().BeEquivalentTo(
            [
                IdentifierConstants.IdentifierQueueName,
                IdentifierConstants.GenericMigrationQueue,
                IdentifierConstants.DataCleanupQueue
            ]);
            configuration.AzureServiceBusConfiguration.Topics.Should().BeEquivalentTo([IdentifierConstants.MigrationCompletionTopic]);
        }
    }

    public class MigrationNotificationServiceTests
    {
        private readonly Mock<IMessageClient> _messageClient = new();

        [Fact]
        public async Task NotifyMigrationCompletionAsync_Success_PublishesToTheCompletionTopic()
        {
            MigrationCompletionEvent? published = null;
            _messageClient.Setup(m => m.SendToMassConsumerAsync(It.IsAny<ConsumerMessage<MigrationCompletionEvent>>()))
                .Callback<ConsumerMessage<MigrationCompletionEvent>>(c => published = c.Payload)
                .Returns(Task.CompletedTask);

            await new MigrationNotificationService(_messageClient.Object)
                .NotifyMigrationCompletionAsync("tracker-1", MigrationServiceNames.DataGateway, true);

            published.Should().NotBeNull();
            published!.TrackerId.Should().Be("tracker-1");
            published.ServiceName.Should().Be("DataGateway");
            published.IsSuccess.Should().BeTrue();
            published.ErrorMessage.Should().BeNull();
        }

        [Fact]
        public async Task NotifyMigrationCompletionAsync_Failure_CarriesTheErrorMessage()
        {
            MigrationCompletionEvent? published = null;
            _messageClient.Setup(m => m.SendToMassConsumerAsync(It.IsAny<ConsumerMessage<MigrationCompletionEvent>>()))
                .Callback<ConsumerMessage<MigrationCompletionEvent>>(c => published = c.Payload)
                .Returns(Task.CompletedTask);

            await new MigrationNotificationService(_messageClient.Object)
                .NotifyMigrationCompletionAsync("tracker-1", MigrationServiceNames.Email, false, "copy failed");

            published!.IsSuccess.Should().BeFalse();
            published.ErrorMessage.Should().Be("copy failed");
            published.ServiceName.Should().Be("Email");
        }
    }

    public class SubscriptionServiceTests
    {
        private readonly Mock<ISubscriptionRepository> _repository = new();

        [Fact]
        public async Task GetSubscriptionsAsync_ReturnsRepositoryResults()
        {
            var limits = new List<ResourceLimit> { new() { Resource = "people::invite", Limit = 5 } };
            _repository.Setup(r => r.GetSubscriptionsAsync()).ReturnsAsync(limits);

            var result = await new SubscriptionService(_repository.Object)
                .GetSubscriptionsAsync(new GetSubscriptionsRequest { ProjectKey = "TENANT-1" });

            result.IsSuccess.Should().BeTrue();
            result.Subscriptions.Should().BeSameAs(limits);
        }

        [Fact]
        public async Task GetSubscriptionsAsync_NoLimits_ReturnsEmptyList()
        {
            _repository.Setup(r => r.GetSubscriptionsAsync()).ReturnsAsync([]);

            var result = await new SubscriptionService(_repository.Object)
                .GetSubscriptionsAsync(new GetSubscriptionsRequest());

            result.IsSuccess.Should().BeTrue();
            result.Subscriptions.Should().BeEmpty();
        }
    }
}
