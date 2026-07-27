using Blocks.Genesis;
using BlocksTemplate.Api.Controllers;
using CloudConfiguration.DomainService.Mail.Entities;
using CloudConfiguration.DomainService.Mail.RequestModel;
using CloudConfiguration.DomainService.Shared.Services;
using FluentAssertions;
using Mail.DomainService.Mails;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace XUnitTest.Controllers
{
    public class MailControllerTests
    {
        private readonly Mock<IConfigurationService> _config = new();
        private readonly Mock<IMailService> _mail = new();
        private readonly MailController _controller;

        public MailControllerTests()
        {
            _controller = new MailController(_config.Object, _mail.Object);
        }

        [Fact]
        public async Task Save_Success_ReturnsOk_AndAssignsIdWhenMissing()
        {
            _config.Setup(s => s.SaveMailConfigurationAsync(It.IsAny<MailConfiguration>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true });

            var request = new MailConfiguration { ConfigurationId = "" };
            var result = await _controller.Save(request);

            result.Should().BeOfType<OkObjectResult>();
            request.ConfigurationId.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public async Task Save_Failure_ReturnsBadRequest()
        {
            _config.Setup(s => s.SaveMailConfigurationAsync(It.IsAny<MailConfiguration>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = false });

            var result = await _controller.Save(new MailConfiguration { ConfigurationId = "id" });

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Get_ReturnsConfiguration()
        {
            var config = new MailConfiguration { ConfigurationId = "id" };
            _config.Setup(s => s.GetMailConfigurationAsync(It.IsAny<GetMailConfigurationRequest>()))
                .ReturnsAsync(config);

            var result = await _controller.Get(new GetMailConfigurationRequest());

            result.Should().BeSameAs(config);
        }

        [Fact]
        public async Task Gets_ReturnsList()
        {
            var list = new List<MailServerConfiguration> { new() };
            _config.Setup(s => s.GetAllMailConfigurationsAsync()).ReturnsAsync(list);

            var result = await _controller.Gets(new GetAllMailConfigurationsRequest());

            result.Should().BeSameAs(list);
        }

        [Fact]
        public async Task Delete_EmptyId_ReturnsBadRequest()
        {
            var result = await _controller.Delete(new DeleteMailConfigurationRequest { ConfigurationId = "" });

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Delete_Success_ReturnsOk()
        {
            _config.Setup(s => s.DeleteMailConfigurationAsync(It.IsAny<DeleteMailConfigurationRequest>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true });

            var result = await _controller.Delete(new DeleteMailConfigurationRequest { ConfigurationId = "id" });

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task Duplicate_EmptyId_ReturnsBadRequest()
        {
            var result = await _controller.Duplicate(new DuplicateMailConfigurationRequest { ConfigurationId = "" });

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Duplicate_Success_ReturnsOk()
        {
            _config.Setup(s => s.DuplicateMailConfigurationAsync(It.IsAny<DuplicateMailConfigurationRequest>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true });

            var result = await _controller.Duplicate(new DuplicateMailConfigurationRequest { ConfigurationId = "id" });

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task SendToAny_Success_ReturnsOk()
        {
            _mail.Setup(s => s.ProcessMailToAnyAsync(It.IsAny<SendMailToAny>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true });

            var result = await _controller.SendToAny(new SendMailToAny());

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task Send_Failure_ReturnsBadRequest()
        {
            _mail.Setup(s => s.ProcessMailAsync(It.IsAny<SendMail>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = false });

            var result = await _controller.Send(new SendMail());

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task GetMailBoxMails_Success_ReturnsOk()
        {
            _mail.Setup(s => s.GetMailBoxMailsAsync(It.IsAny<GetMailBoxMails>()))
                .ReturnsAsync(new GetMailBoxMailsResponse { IsSuccess = true });

            var result = await _controller.GetMailBoxMails(new GetMailBoxMails());

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task GetMailBoxMail_Success_ReturnsOk()
        {
            _mail.Setup(s => s.GetMailBoxMailAsync(It.IsAny<GetMailBoxMail>()))
                .ReturnsAsync(new GetMailBoxMailResponse { IsSuccess = true });

            var result = await _controller.GetMailBoxMail(new GetMailBoxMail());

            result.Should().BeOfType<OkObjectResult>();
        }
    }
}
