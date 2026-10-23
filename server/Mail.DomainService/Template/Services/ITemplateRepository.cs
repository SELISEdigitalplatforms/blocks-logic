using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Blocks.Genesis.Entities.Os;

namespace Mail.DomainService.Template
{
    public interface ITemplateRepository
    {
        public Task SaveAsync(EmailTemplate template);
        public Task<EmailTemplate> GetByIdAsync(string itemId);
        public Task<GetAllTemplatesResponse> GetsAsync(GetAllTemplates request);
        public Task<EmailTemplate> GetByNameAndLanguageAsync(string name, string language);
        public Task DeleteAsync(string itemId);
    }
}
