using Blocks.Genesis;
using Mail.DomainService.Mails;

namespace Worker.Consumers.Mail
{
    public class SendConsumer : IConsumer<SendMail>
    {
        private readonly IMailService _mailService;

        public SendConsumer ( IMailService mailService )
        {
            _mailService = mailService;
        }

        public async Task Consume ( SendMail context )
        {
            await _mailService.ProcessMailAsync(context);
        }
    }
}
