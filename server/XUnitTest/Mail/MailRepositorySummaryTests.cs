using Blocks.Genesis;
using FluentAssertions;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Services;
using Mail.DomainService.Shared.Enums;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestHelpers;

namespace XUnitTest.Mail
{
    public class MailRepositorySummaryTests : IDisposable
    {
        private readonly Mock<IDbContextProvider> _dbContextProvider = new();
        private readonly Mock<IMongoCollection<MailServerConfiguration>> _configs = new();
        private readonly MailRepository _repository;

        public MailRepositorySummaryTests()
        {
            TestBlocksContext.Set();
            _dbContextProvider
                .Setup(p => p.GetCollection<MailServerConfiguration>("MailServerConfigurations"))
                .Returns(_configs.Object);
            _repository = new MailRepository(_dbContextProvider.Object);
        }

        public void Dispose() => TestBlocksContext.Clear();

        [Fact]
        public async Task GetMailServerConfigurationSummariesAsync_ReturnsProjectedSummaries()
        {
            var summaries = new List<MailServerConfigurationSummary>
            {
                new()
                {
                    ItemId = "m1",
                    Name = "Inbox",
                    IsDefault = true,
                    IsInbound = true,
                    Provider = MailServiceProvider.Zoho
                }
            };

            _configs.Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<MailServerConfiguration>>(),
                    It.IsAny<FindOptions<MailServerConfiguration, MailServerConfigurationSummary>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Cursor(summaries));

            var result = await _repository.GetMailServerConfigurationSummariesAsync();

            result.Should().BeEquivalentTo(summaries);
            result[0].Should().NotBeOfType<MailServerConfiguration>();
        }

        [Fact]
        public void MailServerConfigurationSummary_ExposesOnlyPickerFields()
        {
            typeof(MailServerConfigurationSummary)
                .GetProperties()
                .Select(p => p.Name)
                .Should()
                .BeEquivalentTo("ItemId", "Name", "IsDefault", "IsInbound", "Provider");
        }

        private static IAsyncCursor<T> Cursor<T>(List<T> items)
        {
            var cursor = new Mock<IAsyncCursor<T>>();
            cursor.Setup(c => c.Current).Returns(items);
            cursor.SetupSequence(c => c.MoveNext(It.IsAny<CancellationToken>())).Returns(items.Count > 0).Returns(false);
            cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(items.Count > 0).ReturnsAsync(false);
            return cursor.Object;
        }
    }
}
