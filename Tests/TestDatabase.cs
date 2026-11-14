using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServeX.Application;
using ServeX.Data;
using ServeX.Infrastructure.Persistence;
using ServeX.Models;
using ServeX.Services;

namespace ServeX.Tests;

public sealed class TestDatabase : IDisposable
{
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "servex-tests-" + Guid.NewGuid().ToString("N"));
    public DatabaseLocation Location { get; }
    public ServiceProvider Services { get; }
    public TestDatabase()
    {
        Directory.CreateDirectory(DirectoryPath);
        Location = new DatabaseLocation(Path.Combine(DirectoryPath, "test.db"));
        Services = new ServiceCollection().AddServeXCore(Location)
            .AddSingleton(new LogService(Path.Combine(DirectoryPath, "test.log")))
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
    public ServeXDbContext Context() => Services.GetRequiredService<IDbContextFactory<ServeXDbContext>>().CreateDbContext();
    public Task Initialize() => Services.GetRequiredService<DatabaseInitializationService>().InitializeAsync();
    public async Task<(int TableId, int MenuId)> SeedCustomerAsync()
    {
        await Initialize();
        using var context = Context();
        context.Settings.Add(new AppSetting { RestaurantName = "Customer Restaurant", Address = "Address", Phone = "123", TaxPercentage = 5m });
        context.Users.Add(new User { Username = "existing-admin", Password = "test-only-password" });
        var table = new Table { TableNumber = "Customer T1", Area = TableArea.Outdoor, Status = TableStatus.Available };
        var menu = new MenuItem { Name = "Customer Item", Category = "Food", Price = 19.99m, IsAvailable = true };
        context.Tables.Add(table);
        context.MenuItems.Add(menu);
        context.Rooms.Add(new Room { RoomNumber = "Customer Room", Status = RoomStatus.Reserved });
        await context.SaveChangesAsync();
        return (table.Id, menu.Id);
    }
    public void Dispose()
    {
        Services.Dispose();
        SqliteConnection.ClearAllPools();
        Directory.Delete(DirectoryPath, true);
    }
}
