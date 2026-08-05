using Common.InternalService.Language;
using Microsoft.Extensions.DependencyInjection;

namespace Common.InternalService.Shared.Utilities
{
    public static class ApplicationServiceCollectionExtensions
    {
        public static void RegisterCommonInternalServices(this IServiceCollection serviceCollection)
        {
            #region Language
            serviceCollection.AddSingleton<ILanguageManagementService, LanguageManagementService>();
            serviceCollection.AddSingleton<ILanguageRepository, LanguageRepository>();
            #endregion
        }
    }
}
