using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Services;
using Mail.DomainService.Shared.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Mail
{
    public class MailServiceSummaryTests
    {
        [Fact]
        public async Task GetMailServerConfigurationSummariesAsync_ReturnsRepositoryResult()
        {
            var expected = new List<MailServerConfigurationSummary>
            {
                new()
                {
                    ItemId = "m1",
                    Name = "Inbox",
                    IsDefault = false,
                    IsInbound = true,
                    Provider = MailServiceProvider.AmazonSes
                }
            };

            var repository = new Mock<IMailRepository>();
            repository.Setup(r => r.GetMailServerConfigurationSummariesAsync()).ReturnsAsync(expected);

            var service = new MailService(
                Mock.Of<IValidator<MailToBeSent>>(),
                Mock.Of<IMessageClient>(),
                repository.Object,
                Mock.Of<ISendMailService>(),
                NullLogger<MailService>.Instance);

            var result = await service.GetMailServerConfigurationSummariesAsync();

            result.Should().BeSameAs(expected);
        }
    }
}
