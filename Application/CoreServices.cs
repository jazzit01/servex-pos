using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServeX.Data;
using ServeX.Infrastructure.Persistence;
using ServeX.Services;

namespace ServeX.Application
{
    public static class CoreServices
    {
        public static IServiceCollection AddServeXCore(this IServiceCollection services, DatabaseLocation location)
        {
            services.AddSingleton(location);
            services.AddSingleton<IDbContextFactory<ServeXDbContext>, RuntimeDbContextFactory>();
            services.AddSingleton(new LogService(Path.Combine(Path.GetDirectoryName(location.FilePath), "servex.log")));
            services.AddSingleton<DatabaseBackupService>();
            services.AddSingleton<DatabaseInitializationService>();
            services.AddTransient<DemoDataService>();
            services.AddSingleton<ApplicationAccessService>();
            services.AddSingleton<ApplicationSettingsService>();
            services.AddSingleton<MenuService>();
            services.AddSingleton<TableService>();
            services.AddSingleton<OrderService>();
            services.AddSingleton<BillingService>();
            services.AddSingleton<DashboardService>();
            services.AddSingleton<WorkspaceSummaryService>();
            return services;
        }
    }
}
