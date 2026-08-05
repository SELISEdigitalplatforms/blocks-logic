namespace Common.InternalService.Language
{
    public class LanguageManagementService : ILanguageManagementService
    {
        private readonly ILanguageRepository _languageRepository;

        public LanguageManagementService(ILanguageRepository languageRepository)
        {
            _languageRepository = languageRepository;
        }

        public async Task<List<Language>> GetLanguagesAsync()
        {
            return await _languageRepository.GetAllLanguagesAsync();
        }
    }
}
