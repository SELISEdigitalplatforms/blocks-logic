using Cloud.LmtService.Repositories.Logs;
using Cloud.LmtService.Services.Logs;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace Cloud.LmtService.Utilities
{
    public static class CloudLmtServiceCollectionExtensions
    {
        public static void AddCloudLmtServices(this IServiceCollection services)
        {
            services.AddSingleton<ILogService, LogService>();
            services.AddSingleton<ILogRepository, LogRepository>();
        }
    }
}
