using Common.InternalService.Language;

namespace Blocks.Driver.Language;

public class LanguageDriverService : ILanguageDriverService
{
    private readonly ILanguageManagementService _languageManagementService;

    public LanguageDriverService(ILanguageManagementService languageManagementService)
    {
        _languageManagementService = languageManagementService;
    }

    public Task<List<Common.InternalService.Language.Language>> GetLanguagesAsync()
        => _languageManagementService.GetLanguagesAsync();
}
