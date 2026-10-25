using Blocks.Genesis;
using Mail.DomainService.Entities;
using Blocks.Genesis.Entities.Logic;

namespace Mail.DomainService.Mails
{
    public class GetMailBoxMailResponse : BaseResponse
    {
        public MailBoxEntity Mail { get; set; }
    }
}