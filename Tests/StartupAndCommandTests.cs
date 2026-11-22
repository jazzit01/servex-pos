using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServeX.Data;
using ServeX.Models;
using ServeX.Presentation.Navigation;
using ServeX.Services;
using ServeX.ViewModels;
using Xunit;

namespace ServeX.Tests;

public class StartupAndCommandTests
{
    [Fact]
    public async Task StartupUsesSetupOnlyWithoutSettingsAndLoginAfterSetup()
    {
        using var db = new TestDatabase();
        await db.Initialize();
        var access = db.Services.GetRequiredService<ApplicationAccessService>();
        Assert.Equal(StartupFlow.Setup, await access.GetStartupFlowAsync());
        Assert.Null(await db.Services.GetRequiredService<ApplicationSettingsService>().GetSettingsAsync());
        await access.SaveInitialSetupAsync(new InitialSetupRequest
        {
            RestaurantName = "Restaurant", Address = "Address", Phone = "123", TaxPercentage = 5,
            AdminUsername = "admin", AdminPassword = "test-password"
        });
        Assert.Equal(StartupFlow.Login, await access.GetStartupFlowAsync());
        Assert.NotNull(await access.ValidateLoginAsync(" ADMIN ", "test-password"));
        Assert.Null(await access.ValidateLoginAsync("admin", "incorrect"));
        Assert.Null(await access.ValidateLoginAsync("", ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => access.SaveInitialSetupAsync(new InitialSetupRequest { AdminUsername = "other" }));
        using var context = db.Context();
        Assert.Single(await context.Settings.ToListAsync());
        Assert.Single(await context.Users.ToListAsync());
    }

    [Fact]
    public async Task SettingsAndStartupFailuresPropagateRatherThanDefaulting()
    {
        using var db = new TestDatabase();
        // An unmigrated database must be an error, not a new setup decision or default tax/settings.
        await Assert.ThrowsAnyAsync<Exception>(() => db.Services.GetRequiredService<ApplicationAccessService>().GetStartupFlowAsync());
        await Assert.ThrowsAnyAsync<Exception>(() => db.Services.GetRequiredService<ApplicationSettingsService>().GetSettingsAsync());
    }

    [Fact]
    public void CoreRegistrationsResolveAndFactoryCreatesIndependentContexts()
    {
        using var db = new TestDatabase();
        Assert.NotNull(db.Services.GetRequiredService<DatabaseInitializationService>());
        Assert.NotNull(db.Services.GetRequiredService<MenuService>());
        Assert.NotNull(db.Services.GetRequiredService<TableService>());
        Assert.NotNull(db.Services.GetRequiredService<BillingService>());
        Assert.NotNull(db.Services.GetRequiredService<DashboardService>());
        Assert.NotNull(db.Services.GetRequiredService<WorkspaceSummaryService>());
        var factory = db.Services.GetRequiredService<IDbContextFactory<ServeXDbContext>>();
        using var one = factory.CreateDbContext();
        using var two = factory.CreateDbContext();
        Assert.NotSame(one, two);
        Assert.Equal(db.Location.FilePath, one.Database.GetDbConnection().DataSource);
    }

    [Theory]
    [InlineData(WorkspaceDestination.Dashboard)]
    [InlineData(WorkspaceDestination.Menu)]
    [InlineData(WorkspaceDestination.Tables)]
    [InlineData(WorkspaceDestination.Parcel)]
    [InlineData(WorkspaceDestination.Settings)]
    public void NavigationHasTypedDestinationsAndDashboardBackBehavior(WorkspaceDestination destination)
    {
        var navigation = new WorkspaceNavigation();
        var notifications = new List<string>();
        navigation.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        navigation.Navigate(destination);
        Assert.Equal(destination, navigation.Current);
        Assert.Equal(destination != WorkspaceDestination.Dashboard, navigation.CanGoBack);
        var count = notifications.Count;
        Assert.False(navigation.Navigate(destination));
        Assert.Equal(count, notifications.Count);
        navigation.GoBack();
        Assert.Equal(WorkspaceDestination.Dashboard, navigation.Current);
        Assert.False(navigation.CanGoBack);
        Assert.Throws<ArgumentOutOfRangeException>(() => navigation.Navigate((WorkspaceDestination)999));
    }

    [Fact]
    public void LogRotatesAndKeepsOnlyThePreviousFile()
    {
        using var db = new TestDatabase();
        var path = Path.Combine(db.DirectoryPath, "rotation.log");
        var log = new LogService(path);
        var maxBytes = 2L * 1024 * 1024;

        using (var file = File.Create(path)) file.SetLength(maxBytes);
        log.LogInfo("first after rotation");
        Assert.Equal(maxBytes, new FileInfo(path + ".1").Length);
        Assert.Contains("first after rotation", File.ReadAllText(path));

        using (var file = File.OpenWrite(path)) file.SetLength(maxBytes);
        log.LogInfo("second after rotation");
        Assert.Equal(maxBytes, new FileInfo(path + ".1").Length);
        using var previous = File.OpenText(path + ".1");
        Assert.Contains("first after rotation", previous.ReadLine());
        Assert.Contains("second after rotation", File.ReadAllText(path));
    }

    private sealed class DetailPage : ViewModelBase { }

    [Fact]
    public async Task ClosingBillingReturnsToTheSameOrderAndCompletesOnlyTheChild()
    {
        var navigation = new WorkspaceNavigation();
        navigation.Navigate(WorkspaceDestination.Tables);
        var order = new DetailPage();
        var billing = new DetailPage();
        var orderClosed = navigation.OpenDetailAsync(order);
        var billingClosed = navigation.OpenDetailAsync(billing);

        Assert.Same(billing, navigation.Detail);
        navigation.CloseDetail();
        await billingClosed;
        Assert.Same(order, navigation.Detail);
        Assert.False(orderClosed.IsCompleted);

        navigation.CloseDetail();
        await orderClosed;
        Assert.Null(navigation.Detail);
        Assert.Equal(WorkspaceDestination.Tables, navigation.Current);
    }

    [Fact]
    public async Task LeavingTheWorkspaceCompletesBothOpenDetailPages()
    {
        var navigation = new WorkspaceNavigation();
        var orderClosed = navigation.OpenDetailAsync(new DetailPage());
        var billingClosed = navigation.OpenDetailAsync(new DetailPage());

        navigation.CloseDetails();

        await Task.WhenAll(orderClosed, billingClosed);
        Assert.Null(navigation.Detail);
    }

    [Fact]
    public async Task AsyncCommandRejectsReentryAndReenablesAfterCompletion()
    {
        var gate = new TaskCompletionSource();
        int calls = 0;
        var command = new AsyncRelayCommand(async _ => { calls++; await gate.Task; });
        var running = command.ExecuteAsync();
        Assert.True(command.IsRunning);
        Assert.False(command.CanExecute(null));
        await command.ExecuteAsync();
        Assert.Equal(1, calls);
        gate.SetResult();
        await running;
        Assert.False(command.IsRunning);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task AsyncCommandPropagatesFailureAndRestoresExecutionState()
    {
        var command = new AsyncRelayCommand(_ => throw new InvalidOperationException("expected"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteAsync());
        Assert.False(command.IsRunning);
        Assert.True(command.CanExecute(null));
        var disabled = new AsyncRelayCommand(_ => throw new Exception("must not run"), _ => false);
        await disabled.ExecuteAsync();
    }
}
