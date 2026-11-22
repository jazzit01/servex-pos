using System;
using Microsoft.Extensions.DependencyInjection;
using ServeX.Infrastructure.Persistence;
using ServeX.Presentation.Navigation;
using ServeX.Services;
using ServeX.Models;
using ServeX.ViewModels;
using ServeX.Views;

namespace ServeX.Application
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static class CompositionRoot
    {
        public static ServiceProvider Build(DatabaseLocation location)
        {
            var services = new ServiceCollection();
            services.AddServeXCore(location);
            services.AddSingleton<PresentationFactory>();
            services.AddSingleton<WorkspaceNavigation>();
            services.AddTransient<DashboardViewModel>();
            services.AddTransient<HistoryViewModel>();
            services.AddTransient<RoomsViewModel>();
            services.AddTransient<MenuViewModel>();
            services.AddTransient<TablesViewModel>();
            services.AddTransient<ParcelViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddSingleton<SaveFileDialogService>();
            services.AddSingleton<DialogService>();
            services.AddSingleton<ReceiptPrintService>();
            services.AddSingleton<OrderWorkspaceService>();
            services.AddSingleton<KitchenWorkspaceService>();
            services.AddSingleton<BillingWorkspaceService>();
            services.AddSingleton<Func<int, OrderType, OrderViewModel>>(provider => (id, type) =>
                ActivatorUtilities.CreateInstance<OrderViewModel>(provider, id, type));
            services.AddSingleton<Func<int, OrderViewModel>>(provider => id =>
                ActivatorUtilities.CreateInstance<OrderViewModel>(provider, id));
            services.AddSingleton<Func<int, BillingViewModel>>(provider => id =>
                ActivatorUtilities.CreateInstance<BillingViewModel>(provider, id));
            services.AddTransient<MainWindow>();
            services.AddTransient<AuthWindow>();
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        }
    }
}
