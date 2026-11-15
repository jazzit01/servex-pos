using Microsoft.Extensions.DependencyInjection;
using ServeX.Application;
using ServeX.Models;
using ServeX.Presentation.Navigation;
using ServeX.Services;
using ServeX.ViewModels;
using Xunit;

namespace ServeX.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class WindowsCompositionTests
{
    [Fact]
    public async Task FullCompositionResolvesRuntimeViewModelsAndPreservesWorkspaceInstances()
    {
        using var db = new TestDatabase();
        var ids = await db.SeedCustomerAsync();
        using var services = CompositionRoot.Build(db.Location);
        var factory = services.GetRequiredService<PresentationFactory>();
        var settings = await services.GetRequiredService<ApplicationSettingsService>().GetSettingsAsync();
        using var main = factory.CreateMain(settings, new User { Username = "test-user" });
        await main.LoadAsync();
        var destinations = new (WorkspaceDestination, object)[]
        {
            (WorkspaceDestination.Dashboard, main.Dashboard), (WorkspaceDestination.Menu, main.Menu),
            (WorkspaceDestination.Tables, main.Tables), (WorkspaceDestination.Parcel, main.Parcel),
            (WorkspaceDestination.Settings, main.Settings)
        };
        foreach (var (destination, expected) in destinations)
        {
            await main.NavigateCommand.ExecuteAsync(destination);
            Assert.Same(expected, main.CurrentWorkspace);
            Assert.Equal(destination, main.SelectedDestination);
        }
        main.GoBackCommand.Execute(null);
        Assert.Same(main.Dashboard, main.CurrentWorkspace);
        Assert.False(main.CanGoBack);
        Assert.NotNull(services.GetRequiredService<Func<int, OrderViewModel>>()(ids.TableId));
        Assert.NotNull(services.GetRequiredService<Func<int, BillingViewModel>>()(1));
        var auth = factory.CreateAuth(_ => Task.CompletedTask);
        await auth.InitializeAsync(StartupFlow.Setup);
        Assert.IsType<SetupViewModel>(auth.CurrentViewModel);
        await auth.InitializeAsync(StartupFlow.Login);
        Assert.IsType<LoginViewModel>(auth.CurrentViewModel);
        await Assert.ThrowsAsync<InvalidOperationException>(() => auth.InitializeAsync(StartupFlow.Blocked));
    }
    [Fact]
    public async Task OrderAndParcelSearchPreserveDraftItemsAndTotals()
    {
        using var db = new TestDatabase();
        var ids = await db.SeedCustomerAsync();
        using var services = CompositionRoot.Build(db.Location);
        var order = services.GetRequiredService<Func<int, OrderViewModel>>()(ids.TableId);
        await order.LoadAsync();
        order.AddItemCommand.Execute(order.MenuItems.Single());
        var total = order.Total;
        order.SearchText = "no-match";
        Assert.True(order.IsMenuEmpty);
        Assert.Single(order.OrderItems);
        Assert.Equal(total, order.Total);
        order.SearchText = "customer";
        Assert.Single(order.FilteredMenuItems);
        order.SearchText = "food";
        Assert.Single(order.FilteredMenuItems);
        var parcel = services.GetRequiredService<ParcelViewModel>();
        await parcel.LoadAsync();
        parcel.AddItemCommand.Execute(parcel.MenuItems.Single());
        var parcelTotal = parcel.Total;
        parcel.SearchText = "no-match";
        Assert.True(parcel.IsMenuEmpty);
        Assert.Single(parcel.OrderItems);
        Assert.Equal(parcelTotal, parcel.Total);
        parcel.SearchText = "CUSTOMER";
        Assert.Single(parcel.FilteredMenuItems);
    }

    [Fact]
    public async Task RedesignedViewsLoadResourcesAndTemplatesOnStaThread()
    {
        using var db = new TestDatabase();
        await db.SeedCustomerAsync();
        using var services = CompositionRoot.Build(db.Location);
        var factory = services.GetRequiredService<PresentationFactory>();
        var settings = await services.GetRequiredService<ApplicationSettingsService>().GetSettingsAsync();
        using var main = factory.CreateMain(settings, new User { Username = "ui-test" });
        await main.LoadAsync();
        var order = services.GetRequiredService<Func<int, OrderViewModel>>()(1);
        await order.LoadAsync();
        var auth = factory.CreateAuth(_ => Task.CompletedTask);
        await auth.InitializeAsync(StartupFlow.Login);
        Exception failure = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                var app = new ServeX.App();
                app.InitializeComponent(); // Resources only; never Run/OnStartup or production initialization.
                app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
                var views = new (System.Windows.FrameworkElement, object)[]
                {
                    (new ServeX.Views.DashboardView(), main.Dashboard),
                    (new ServeX.Views.MenuView(), main.Menu),
                    (new ServeX.Views.TablesView(), main.Tables),
                    (new ServeX.Views.ParcelView(), main.Parcel),
                    (new ServeX.Views.SettingsView(), main.Settings),
                    (new ServeX.Views.OrderView(), order),
                    (new ServeX.Views.BillingView(), services.GetRequiredService<Func<int, BillingViewModel>>()(1)),
                    (new ServeX.Views.LoginView(), auth.CurrentViewModel),
                    (new ServeX.Views.SetupView(), factory.CreateSetup(_ => Task.CompletedTask))
                };
                foreach (var (view, context) in views)
                {
                    view.DataContext = context;
                    view.Measure(new System.Windows.Size(850, 520));
                    view.Arrange(new System.Windows.Rect(0, 0, 850, 520));
                    view.UpdateLayout();
                    Assert.True(double.IsFinite(view.ActualWidth));
                }
                // Constructors parse shell/dialog resources without opening UI or printing.
                new ServeX.Views.MainWindow { DataContext = main }.Close();
                new ServeX.Views.AuthWindow { DataContext = auth }.Close();
                new ServeX.Views.OrderWindow { DataContext = order }.Close();
                new ServeX.Views.BillingWindow().Close();
                app.Shutdown();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF resource/layout smoke test timed out.");
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

}
