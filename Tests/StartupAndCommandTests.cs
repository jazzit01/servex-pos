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
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task NumericTableNamesUseAreaPrefixAndCheckTheFinalNameForDuplicates()
    {
        using var db = new TestDatabase();
        await db.Initialize();
        var service = db.Services.GetRequiredService<TableService>();
        var indoor = await service.AddTableAsync(new Table { TableNumber = " 006 ", Area = TableArea.Indoor });
        var outdoor = await service.AddTableAsync(new Table { TableNumber = "6", Area = TableArea.Outdoor });
        var named = await service.AddTableAsync(new Table { TableNumber = "T6", Area = TableArea.Indoor });
        Assert.Equal("I-6", indoor.TableNumber);
        Assert.Equal("O-6", outdoor.TableNumber);
        Assert.Equal("T6", named.TableNumber);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddTableAsync(new Table { TableNumber = "i-6", Area = TableArea.Indoor }));
    }

    [Fact]
    public async Task FloorMovesKeepGapsSwapTablesAndSurviveReload()
    {
        using var db = new TestDatabase();
        await db.Initialize();
        var service = db.Services.GetRequiredService<TableService>();
        var first = await service.AddTableAsync(new Table { TableNumber = "T1" });
        var second = await service.AddTableAsync(new Table { TableNumber = "T2", Status = TableStatus.Reserved });
        var third = await service.AddTableAsync(new Table { TableNumber = "T3" });
        var outdoor = await service.AddTableAsync(new Table { TableNumber = "T4", Area = TableArea.Outdoor });

        Assert.Null(first.FloorSlot);
        var moved = await service.MoveOnFloorAsync(first.Id, TableArea.Indoor, TableFloorLayout.Columns + 2);
        await service.MoveOnFloorAsync(second.Id, TableArea.Indoor, moved[first.Id]);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.MoveOnFloorAsync(outdoor.Id, TableArea.Indoor, 0));

        var saved = (await service.GetTablesAsync()).ToDictionary(table => table.Id);
        Assert.Equal(1, saved[first.Id].FloorSlot);
        Assert.Equal(TableFloorLayout.Columns + 2, saved[second.Id].FloorSlot);
        Assert.Equal(TableStatus.Reserved, saved[second.Id].Status);
        Assert.Equal(2, saved[third.Id].FloorSlot);
        Assert.Null(saved[outdoor.Id].FloorSlot);

        var fourth = await service.AddTableAsync(new Table { TableNumber = "T5" });
        var positions = TableFloorLayout.GetPositions((await service.GetTablesAsync())
            .Where(table => table.Area == TableArea.Indoor));
        Assert.Equal(0, positions[fourth.Id]);

        saved[second.Id].Area = TableArea.Outdoor;
        await service.UpdateTableAsync(saved[second.Id]);
        Assert.Null((await service.GetTablesAsync()).Single(table => table.Id == second.Id).FloorSlot);
    }

    [Fact]
    public void RememberedUsernameCanBeSavedAndCleared()
    {
        using var db = new TestDatabase();
        var path = Path.Combine(db.DirectoryPath, "remembered-username.txt");
        var preference = new RememberedUsername(db.Services.GetRequiredService<LogService>(), path);
        Assert.Empty(preference.Read());
        preference.Save("operator");
        Assert.Equal("operator", new RememberedUsername(db.Services.GetRequiredService<LogService>(), path).Read());
        preference.Save(null);
        Assert.Empty(preference.Read());
    }

    [Fact]
    public async Task FreshSetupAddsStarterAreasAndDoesNotCreateSampleSalesOrMenuItems()
    {
        using var db = new TestDatabase();
        await db.Initialize();
        var access = db.Services.GetRequiredService<ApplicationAccessService>();
        await access.SaveInitialSetupAsync(new InitialSetupRequest
        {
            RestaurantName = "Test Restaurant", Address = "Address", Phone = "123",
            AdminUsername = "admin", AdminPassword = "test-password"
        });
        using var context = db.Context();
        Assert.Equal(5, await context.Tables.CountAsync(table => table.Area == TableArea.Indoor));
        Assert.Equal(5, await context.Tables.CountAsync(table => table.Area == TableArea.Outdoor));
        Assert.Equal(3, await context.Rooms.CountAsync());
        Assert.Empty(await context.MenuItems.ToListAsync());
        Assert.Empty(await context.Orders.ToListAsync());

    }

    [Fact]
    public async Task ExistingLayoutFillsOnlyEmptyAreasAndKeepsExistingNumbers()
    {
        using var db = new TestDatabase();
        await db.Initialize();
        var tables = db.Services.GetRequiredService<TableService>();
        await tables.AddTableAsync(new Table { TableNumber = "O-1", Area = TableArea.Indoor });
        await db.Services.GetRequiredService<ApplicationAccessService>().SaveInitialSetupAsync(new InitialSetupRequest
        {
            RestaurantName = "Test Restaurant", Address = "Address", Phone = "123",
            AdminUsername = "admin", AdminPassword = "test-password"
        });
        var all = await tables.GetTablesAsync();
        Assert.Equal("O-1", Assert.Single(all, table => table.Area == TableArea.Indoor).TableNumber);
        Assert.Equal(5, all.Count(table => table.Area == TableArea.Outdoor));
        Assert.Contains(all, table => table.TableNumber == "O-6");
    }

    [Fact]
    public async Task CashierCanLogInButCannotManageStaffMenuOrBackups()
    {
        using var db = new TestDatabase();
        await db.Initialize();
        var access = db.Services.GetRequiredService<ApplicationAccessService>();
        var session = db.Services.GetRequiredService<OperatorSession>();
        session.ActiveUser = new User { Role = UserRole.Admin };
        await access.SaveInitialSetupAsync(new InitialSetupRequest
        {
            RestaurantName = "Role test", Address = "Address", Phone = "123",
            AdminUsername = "admin", AdminPassword = "test-password"
        });
        await access.CreateStaffAsync("cashier", "cashier-password", UserRole.Cashier);
        session.ActiveUser = await access.ValidateLoginAsync("cashier", "cashier-password");
        Assert.Equal(UserRole.Cashier, session.ActiveUser.Role);
        await Assert.ThrowsAsync<InvalidOperationException>(() => access.CreateStaffAsync("new", "new-password", UserRole.Admin));
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Services.GetRequiredService<MenuService>()
            .AddMenuItemAsync(new MenuItem { Name = "Test", Price = 1, Category = "Food" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Services.GetRequiredService<DatabaseBackupService>()
            .BackupAsync(Path.Combine(db.DirectoryPath, "denied.db")));
    }

    [Fact]
    public async Task RepeatedFailuresTemporarilyLockLoginAndPasswordChangeRequiresTheCurrentPassword()
    {
        using var db = new TestDatabase();
        await db.Initialize();
        var clock = new TestClock();
        var contexts = db.Services.GetRequiredService<IDbContextFactory<ServeXDbContext>>();
        var access = new ApplicationAccessService(contexts, db.Services.GetRequiredService<LogService>(), clock);
        await access.SaveInitialSetupAsync(new InitialSetupRequest
        {
            RestaurantName = "Test Restaurant", Address = "Test address", Phone = "123",
            AdminUsername = "Admin", AdminPassword = "existing-password"
        });

        for (var attempt = 0; attempt < 5; attempt++)
            Assert.Null(await access.ValidateLoginAsync("admin", "wrong-password"));
        Assert.Null(await access.ValidateLoginAsync("ADMIN", "existing-password"));
        clock.Now = clock.Now.AddSeconds(16);
        var user = await access.ValidateLoginAsync("ADMIN", "existing-password");
        Assert.NotNull(user);
        Assert.False(await access.ChangePasswordAsync(user.Id, "incorrect", "updated-password"));
        Assert.True(await access.ChangePasswordAsync(user.Id, "existing-password", "updated-password"));
        Assert.Null(await access.ValidateLoginAsync("admin", "existing-password"));
        Assert.NotNull(await access.ValidateLoginAsync("admin", "updated-password"));
    }

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
