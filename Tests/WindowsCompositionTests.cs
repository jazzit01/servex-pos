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
}
