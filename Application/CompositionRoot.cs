using System;
using Microsoft.Extensions.DependencyInjection;
using ServeX.Infrastructure.Persistence;
using ServeX.Presentation.Navigation;
using ServeX.Services;
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
            services.AddTransient<MenuViewModel>();
            services.AddTransient<TablesViewModel>();
            services.AddTransient<ParcelViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddSingleton<SaveFileDialogService>();
            services.AddSingleton<DialogService>();
            services.AddSingleton<ReceiptPrintService>();
            services.AddSingleton<OrderWindowService>();
            services.AddSingleton<BillingWindowService>();
            services.AddSingleton<Func<int, OrderViewModel>>(provider => id =>
                ActivatorUtilities.CreateInstance<OrderViewModel>(provider, id));
            services.AddSingleton<Func<int, BillingViewModel>>(provider => id =>
                ActivatorUtilities.CreateInstance<BillingViewModel>(provider, id));
            services.AddTransient<MainWindow>();
            services.AddTransient<AuthWindow>();
            services.AddTransient<OrderWindow>();
            services.AddTransient<BillingWindow>();
            services.AddSingleton<Func<OrderWindow>>(provider => () => provider.GetRequiredService<OrderWindow>());
            services.AddSingleton<Func<BillingWindow>>(provider => () => provider.GetRequiredService<BillingWindow>());
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        }
    }
}
