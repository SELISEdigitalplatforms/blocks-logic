namespace Common.InternalService.Language
{
    public interface ILanguageManagementService
    {
        Task<List<Language>> GetLanguagesAsync();
    }
}
