using Blocks.Genesis.DataAccess;
using Microsoft.Extensions.DependencyInjection;

namespace DataAccess.InternalService;

public static class LogicDataAccessExtensions
{
    /// <summary>
    /// Genesis data access resolver (row filter + projection) and the one logic door to it.
    /// Call in every host that runs wrapped repositories (Api, Worker, MailBoxSyncService).
    /// </summary>
    public static IServiceCollection AddLogicDataAccess(this IServiceCollection services)
    {
        services.AddBlocksDataAccess();
        services.AddSingleton<ILogicDataAccess, LogicDataAccess>();
        return services;
    }
}
