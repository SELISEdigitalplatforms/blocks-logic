using Common.InternalService.Language;

namespace Blocks.Driver.Language;

/// <summary>
/// Service for retrieving the languages supported by the platform.
/// </summary>
public interface ILanguageDriverService
{
    Task<List<Common.InternalService.Language.Language>> GetLanguagesAsync();
}
