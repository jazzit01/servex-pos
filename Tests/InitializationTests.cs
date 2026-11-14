using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using ServeX.Models;
using ServeX.Services;
using Xunit;

namespace ServeX.Tests;

public class InitializationTests
{
    [Fact]
    public async Task FirstAndRepeatLaunchApplyMigrationsWithoutBusinessData()
    {
        using var db = new TestDatabase();
        await db.Initialize();
        await db.Initialize();
        using var context = db.Context();
        Assert.Equal(5, (await context.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Empty(await context.MenuItems.ToListAsync());
        Assert.Empty(await context.Tables.ToListAsync());
        Assert.Empty(await context.Rooms.ToListAsync());
        Assert.Empty(await context.Orders.ToListAsync());
        Assert.Empty(await context.Bills.ToListAsync());
        Assert.Empty(await context.Users.ToListAsync());
        Assert.Empty(await context.Settings.ToListAsync());
    }

    [Fact]
    public async Task RepeatLaunchPreservesCustomerOrdersBillsSettingsAndStatuses()
    {
        using var db = new TestDatabase();
        var ids = await db.SeedCustomerAsync();
        var order = await db.Services.GetRequiredService<OrderService>().SaveTableOrderAsync(ids.TableId,
            new[] { new OrderSaveItem { MenuItemId = ids.MenuId, Quantity = 2, Price = 19.99m } });
        await db.Initialize();
        await db.Initialize();
        using var context = db.Context();
        Assert.Single(await context.MenuItems.ToListAsync());
        Assert.Equal("Customer Restaurant", (await context.Settings.SingleAsync()).RestaurantName);
        Assert.Equal("test-only-password", (await context.Users.SingleAsync()).Password);
        Assert.Equal(RoomStatus.Reserved, (await context.Rooms.SingleAsync()).Status);
        Assert.Equal(TableStatus.Occupied, (await context.Tables.SingleAsync()).Status);
        Assert.Equal(order.Id, (await context.Orders.SingleAsync()).Id);
        Assert.Equal(41.98m, (await context.Bills.SingleAsync()).Total);
        Assert.Equal(2, (await context.OrderItems.SingleAsync()).Quantity);
    }

    [Fact]
    public async Task ExistingDatabaseWithEmptyCatalogIsNotRepopulated()
    {
        using var db = new TestDatabase();
        await db.Initialize();
        using (var context = db.Context())
        {
            context.Settings.Add(new AppSetting { RestaurantName = "Existing" });
            await context.SaveChangesAsync();
        }
        await db.Initialize();
        using var check = db.Context();
        Assert.Empty(await check.MenuItems.ToListAsync());
        Assert.Empty(await check.Tables.ToListAsync());
        Assert.Empty(await check.Rooms.ToListAsync());
        Assert.Empty(await check.Orders.ToListAsync());
    }

    [Fact]
    public async Task UpgradeBacksUpExistingSchemaBeforeMigrationAndPreservesRows()
    {
        using var db = new TestDatabase();
        using (var context = db.Context())
        {
            await context.GetService<IMigrator>().MigrateAsync("20260410061929_ExpandServeXModels");
            await context.Database.ExecuteSqlRawAsync("INSERT INTO MenuItems (Name, Price, Category, IsAvailable) VALUES ('Original', '12.50', 'Food', 1)");
            await context.Database.ExecuteSqlRawAsync("INSERT INTO Tables (TableNumber, Status) VALUES ('Original Table', 'Occupied')");
        }
        await db.Initialize();
        var backup = Assert.Single(Directory.GetFiles(Path.Combine(db.DirectoryPath, "Backups"), "*.db"));
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backup, Mode = SqliteOpenMode.ReadOnly }.ToString());
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM __EFMigrationsHistory";
        Assert.Equal(1L, await command.ExecuteScalarAsync());
        using var check = db.Context();
        Assert.Equal("Original", (await check.MenuItems.SingleAsync()).Name);
        var table = await check.Tables.SingleAsync();
        Assert.Equal(TableArea.Indoor, table.Area);
        Assert.Equal(TableStatus.Occupied, table.Status);
        Assert.Equal("Original Table", table.TableNumber);
        Assert.Equal(5, (await check.Database.GetAppliedMigrationsAsync()).Count());
    }

    [Fact]
    public async Task ExplicitDemoIsRepeatableAndDoesNotCreateOrdersOrOccupancy()
    {
        using var db = new TestDatabase();
        await db.Initialize();
        var demo = new DemoDataService(db.Services.GetRequiredService<IDbContextFactory<ServeX.Data.ServeXDbContext>>());
        await demo.SeedEmptyDemoAsync();
        await demo.SeedEmptyDemoAsync();
        using var context = db.Context();
        Assert.Equal(2, await context.MenuItems.CountAsync());
        Assert.Equal(2, await context.Rooms.CountAsync());
        Assert.All(await context.Tables.ToListAsync(), t => Assert.Equal(TableStatus.Available, t.Status));
        Assert.Empty(await context.Orders.ToListAsync());
        Assert.Empty(await context.Bills.ToListAsync());
    }

    [Fact]
    public async Task DemoRefusesToPopulateCustomerDatabase()
    {
        using var db = new TestDatabase();
        await db.SeedCustomerAsync();
        await new DemoDataService(db.Services.GetRequiredService<IDbContextFactory<ServeX.Data.ServeXDbContext>>()).SeedEmptyDemoAsync();
        using var context = db.Context();
        Assert.Equal("Customer Item", (await context.MenuItems.SingleAsync()).Name);
        Assert.Empty(await context.Orders.ToListAsync());
    }

    [Fact]
    public async Task CorruptDatabaseFailsWithoutReplacement()
    {
        using var db = new TestDatabase();
        var original = System.Text.Encoding.UTF8.GetBytes("This is not a SQLite database");
        await File.WriteAllBytesAsync(db.Location.FilePath, original);
        await Assert.ThrowsAnyAsync<Exception>(db.Initialize);
        Assert.Equal(original, await File.ReadAllBytesAsync(db.Location.FilePath));
    }
    [Fact]
    public async Task FailedPreMigrationBackupStopsUpgradeAndPreservesOldDatabase()
    {
        using var db = new TestDatabase();
        using (var context = db.Context())
        {
            await context.GetService<IMigrator>().MigrateAsync("20260410061929_ExpandServeXModels");
            await context.Database.ExecuteSqlRawAsync("INSERT INTO MenuItems (Name, Price, Category, IsAvailable) VALUES ('Original', '12.50', 'Food', 1)");
        }
        await File.WriteAllTextAsync(Path.Combine(db.DirectoryPath, "Backups"), "blocked directory");
        await Assert.ThrowsAnyAsync<IOException>(db.Initialize);
        using var check = db.Context();
        Assert.Single(await check.Database.GetAppliedMigrationsAsync());
        Assert.Equal("Original", (await check.MenuItems.SingleAsync()).Name);
    }

    [Fact]
    public async Task FullyMigratedLegacyEmptyAreaIsBackedUpAndRepairedOnce()
    {
        using var db = new TestDatabase();
        await db.Initialize();
        using (var context = db.Context())
            await context.Database.ExecuteSqlRawAsync("INSERT INTO Tables (TableNumber, Status, Area) VALUES ('Legacy', 'Reserved', '')");
        await db.Initialize();
        await db.Initialize();
        Assert.Single(Directory.GetFiles(Path.Combine(db.DirectoryPath, "Backups"), "*.db"));
        using var check = db.Context();
        var table = await check.Tables.SingleAsync();
        Assert.Equal(TableArea.Indoor, table.Area);
        Assert.Equal(TableStatus.Reserved, table.Status);
    }

}
