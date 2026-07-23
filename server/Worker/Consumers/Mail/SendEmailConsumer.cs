using Blocks.Genesis;
using Mail.DomainService.Dtos;
using Mail.DomainService.Mails;
using System;
using System.Collections.Generic;
using System.Text;

namespace Worker.Consumers.Mail
{
    public class SendEmailConsumer : IConsumer<SendEmailEvent>
    {
        private readonly ISendMailService _sendMailService;

        public SendEmailConsumer ( ISendMailService sendMailService )
        {
            _sendMailService = sendMailService;
        }

        public async Task Consume ( SendEmailEvent sendEmailEvent )
        {
            await _sendMailService.ProcessSendMailAsync(sendEmailEvent);
        }
    }
}
