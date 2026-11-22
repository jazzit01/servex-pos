using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServeX.Data;
using ServeX.Infrastructure.Persistence;
using ServeX.Services;
using Xunit;

namespace ServeX.Tests;

public class StorageBackupTests
{
    [Fact]
    public void FreshStorageUsesWritableLocalDirectoryAndPinsSelection()
    {
        using var db = new TestDatabase();
        var app = Path.Combine(db.DirectoryPath, "app");
        var local = Path.Combine(db.DirectoryPath, "local");
        var location = DatabaseLocation.Resolve(app, local);
        Assert.Equal(Path.Combine(local, "ServeX", "Data", "servex.db"), location.FilePath);
        Assert.Equal(location.FilePath, DatabaseLocation.Resolve(app, local).FilePath);
        File.Delete(location.FilePath);
        Assert.Throws<InvalidOperationException>(() => DatabaseLocation.Resolve(app, local));
        Assert.False(File.Exists(location.FilePath));
    }

    [Fact]
    public void LegacyDatabaseRemainsInPlaceAndPinPreventsSilentSwitchAfterMove()
    {
        using var db = new TestDatabase();
        var app = Path.Combine(db.DirectoryPath, "app");
        var local = Path.Combine(db.DirectoryPath, "local");
        var legacy = Path.Combine(app, "Data", "servex.db");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy));
        var content = new byte[] { 1, 2, 3 };
        File.WriteAllBytes(legacy, content);
        Assert.Equal(legacy, DatabaseLocation.Resolve(app, local).FilePath);
        Assert.Equal(content, File.ReadAllBytes(legacy));
        Assert.Equal(legacy, DatabaseLocation.Resolve(Path.Combine(db.DirectoryPath, "moved-app"), local).FilePath);
        File.Move(legacy, legacy + ".moved");
        Assert.Throws<InvalidOperationException>(() => DatabaseLocation.Resolve(app, local));
        Assert.False(File.Exists(Path.Combine(local, "ServeX", "Data", "servex.db")));
    }

    [Fact]
    public void MultipleDatabasesRequireExplicitExistingSelection()
    {
        using var db = new TestDatabase();
        var app = Path.Combine(db.DirectoryPath, "app");
        var local = Path.Combine(db.DirectoryPath, "local");
        var legacy = Path.Combine(app, "Data", "servex.db");
        var fresh = DatabaseLocation.Resolve(app, local).FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(legacy));
        File.WriteAllBytes(legacy, new byte[] { 1 });
        Assert.Throws<InvalidOperationException>(() => DatabaseLocation.Resolve(app, local));
        Assert.Equal(legacy, DatabaseLocation.Resolve(app, local, legacy).FilePath);
        Assert.Throws<InvalidOperationException>(() => DatabaseLocation.Resolve(app, local, Path.Combine(db.DirectoryPath, "missing.db")));
        Assert.Throws<InvalidOperationException>(() => DatabaseLocation.Resolve(app, local, "relative.db"));
        Assert.True(File.Exists(fresh));
        Assert.True(File.Exists(legacy));
    }

    [Fact]
    public async Task OnlineBackupIncludesWalAndRestoreCreatesSeparateVerifiedDatabase()
    {
        using var db = new TestDatabase();
        await db.SeedCustomerAsync();
        using var context = db.Context();
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        await context.Database.ExecuteSqlRawAsync("UPDATE MenuItems SET Name='Committed WAL item';");
        var backup = Path.Combine(db.DirectoryPath, "backups", "backup.db");
        await db.Services.GetRequiredService<DatabaseBackupService>().BackupAsync(backup);
        var restored = Path.Combine(db.DirectoryPath, "restored.db");
        await DatabaseBackupService.RestoreToNewFileAsync(backup, restored);
        using var check = new RuntimeDbContextFactory(new DatabaseLocation(restored)).CreateDbContext();
        Assert.Equal("Committed WAL item", (await check.MenuItems.SingleAsync()).Name);
        Assert.Equal("Customer Restaurant", (await check.Settings.SingleAsync()).RestaurantName);
        Assert.Single(await check.Users.ToListAsync());
        Assert.Equal(await context.Database.GetAppliedMigrationsAsync(), await check.Database.GetAppliedMigrationsAsync());
        Assert.True(File.Exists(db.Location.FilePath));
    }

    [Fact]
    public async Task BackupAndRestoreRefuseOverwriteAndLiveDestination()
    {
        using var db = new TestDatabase();
        await db.Initialize();
        var backupService = db.Services.GetRequiredService<DatabaseBackupService>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => backupService.BackupAsync(db.Location.FilePath));
        var target = Path.Combine(db.DirectoryPath, "existing.db");
        var bytes = new byte[] { 1, 2, 3, 4 };
        await File.WriteAllBytesAsync(target, bytes);
        await Assert.ThrowsAsync<IOException>(() => backupService.BackupAsync(target));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(target));
        var good = Path.Combine(db.DirectoryPath, "good.db");
        await backupService.BackupAsync(good);
        await Assert.ThrowsAsync<IOException>(() => DatabaseBackupService.RestoreToNewFileAsync(good, db.Location.FilePath));
        await Assert.ThrowsAnyAsync<Exception>(() => DatabaseBackupService.RestoreToNewFileAsync(target, Path.Combine(db.DirectoryPath, "must-not-exist.db")));
        Assert.False(File.Exists(Path.Combine(db.DirectoryPath, "must-not-exist.db")));
        Assert.Empty(Directory.GetFiles(db.DirectoryPath, "*.tmp"));
    }

    [Fact]
    public async Task MissingSourceBackupFailsWithoutCreatingDestination()
    {
        using var db = new TestDatabase();
        var target = Path.Combine(db.DirectoryPath, "backup.db");
        await Assert.ThrowsAsync<FileNotFoundException>(() => db.Services.GetRequiredService<DatabaseBackupService>().BackupAsync(target));
        Assert.False(File.Exists(target));
    }
}
