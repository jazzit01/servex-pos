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
            services.AddSingleton<OperatorSession>();
            services.AddSingleton<IDbContextFactory<ServeXDbContext>, RuntimeDbContextFactory>();
            services.AddSingleton(new LogService(Path.Combine(Path.GetDirectoryName(location.FilePath), "servex.log")));
            services.AddSingleton<DatabaseBackupService>();
            services.AddSingleton<BusinessArchiveService>();
            services.AddSingleton<DatabaseInitializationService>();
            services.AddTransient<DemoDataService>();
            services.AddSingleton<ApplicationAccessService>();
            services.AddSingleton<ApplicationSettingsService>();
            services.AddSingleton<MenuService>();
            services.AddSingleton<TableService>();
            services.AddSingleton<RoomService>();
            services.AddSingleton<OrderService>();
            services.AddSingleton<BillingService>();
            services.AddSingleton<DashboardService>();
            services.AddSingleton<OrderHistoryService>();
            services.AddSingleton<KitchenService>();
            services.AddSingleton<WorkspaceSummaryService>();
            return services;
        }
    }
}
