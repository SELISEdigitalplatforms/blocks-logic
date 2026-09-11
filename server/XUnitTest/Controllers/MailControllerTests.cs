using BlocksTemplate.Api.Controllers;
using FluentAssertions;
using Mail.DomainService.Mails;
using Mail.DomainService.Shared.Enums;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace XUnitTest.Controllers
{
    public class MailControllerTests
    {
        private readonly Mock<IMailService> _mailService = new();
        private readonly MailController _controller;

        public MailControllerTests()
        {
            _controller = new MailController(_mailService.Object);
        }

        [Fact]
        public async Task Gets_ReturnsOk_WithSummariesFromService()
        {
            var summaries = new List<MailServerConfigurationSummary>
            {
                new()
                {
                    ItemId = "m1",
                    Name = "Inbox",
                    IsDefault = true,
                    IsInbound = true,
                    Provider = MailServiceProvider.AmazonSes
                }
            };
            _mailService.Setup(s => s.GetMailServerConfigurationSummariesAsync()).ReturnsAsync(summaries);

            var result = await _controller.Gets(new GetMailConfigurationsRequest
            {
                PageNumber = 1,
                PageSize = 200
            });

            result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(summaries);
            _mailService.Verify(s => s.ProcessMailAsync(It.IsAny<SendMail>()), Times.Never);
            _mailService.Verify(s => s.ProcessMailToAnyAsync(It.IsAny<SendMailToAny>()), Times.Never);
        }

        [Fact]
        public async Task Gets_ReturnsOk_WithEmptyList()
        {
            var summaries = new List<MailServerConfigurationSummary>();
            _mailService.Setup(s => s.GetMailServerConfigurationSummariesAsync()).ReturnsAsync(summaries);

            var result = await _controller.Gets(new GetMailConfigurationsRequest());

            result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(summaries);
        }
    }
}
