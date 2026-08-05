namespace Common.InternalService.Language
{
    public interface ILanguageRepository
    {
        Task<List<Language>> GetAllLanguagesAsync();
    }
}
