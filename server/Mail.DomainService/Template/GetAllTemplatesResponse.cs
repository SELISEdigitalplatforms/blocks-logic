using Mail.DomainService.Entities;
using Blocks.Genesis.Entities.Os;

namespace Mail.DomainService.Template
{
    public class GetAllTemplatesResponse
    {
        public int TotalCount { get; set; }
        public List<EmailTemplate> Templates { get; set; }
    }
}
