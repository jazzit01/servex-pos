using Microsoft.Data.Sqlite;
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
    public async Task ExportAndSupportBundleContainTheExpectedFiles()
    {
        using var db = new TestDatabase();
        await db.SeedCustomerAsync();
        var archive = db.Services.GetRequiredService<BusinessArchiveService>();
        var csv = Path.Combine(db.DirectoryPath, "export.zip");
        await archive.ExportCsvAsync(csv);
        using (var file = System.IO.Compression.ZipFile.OpenRead(csv))
        {
            Assert.NotNull(file.GetEntry("orders.csv"));
            Assert.NotNull(file.GetEntry("bills.csv"));
            using var reader = new StreamReader(file.GetEntry("menu.csv").Open());
            Assert.Contains("Customer Item", await reader.ReadToEndAsync());
        }
        var bundle = Path.Combine(db.DirectoryPath, "support.zip");
        await archive.CreateSupportBundleAsync(bundle);
        using var support = System.IO.Compression.ZipFile.OpenRead(bundle);
        Assert.NotNull(support.GetEntry("servex.db"));
        Assert.NotNull(support.GetEntry("servex.log"));
    }

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
        Assert.Throws<DatabaseSelectionException>(() => DatabaseLocation.Resolve(app, local));
        Assert.False(File.Exists(location.FilePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyDatabaseRelocatesAndPinsTheVerifiedCopy(bool oldPinExists)
    {
        using var db = new TestDatabase();
        await db.SeedCustomerAsync();
        var app = Path.Combine(db.DirectoryPath, "app");
        var local = Path.Combine(db.DirectoryPath, "local");
        var legacy = Path.Combine(app, "Data", "servex.db");
        await db.Services.GetRequiredService<DatabaseBackupService>().BackupAsync(legacy);
        if (oldPinExists)
        {
            Directory.CreateDirectory(Path.Combine(local, "ServeX"));
            File.WriteAllText(Path.Combine(local, "ServeX", "database-location.txt"), legacy);
        }

        var migrated = DatabaseLocation.Resolve(app, local);
        Assert.StartsWith(Path.Combine(local, "ServeX", "Data") + Path.DirectorySeparatorChar, migrated.FilePath);
        Assert.True(File.Exists(legacy));
        using (var check = new RuntimeDbContextFactory(migrated).CreateDbContext())
            Assert.Equal("Customer Item", (await check.MenuItems.SingleAsync()).Name);
        Assert.Equal(migrated.FilePath, DatabaseLocation.Resolve(app, local).FilePath);
        Assert.Equal(migrated.FilePath, DatabaseLocation.Resolve(Path.Combine(db.DirectoryPath, "moved-app"), local).FilePath);
    }

    [Fact]
    public void CorruptLegacyDatabaseIsNeverReplacedOrPinned()
    {
        using var db = new TestDatabase();
        var app = Path.Combine(db.DirectoryPath, "app");
        var local = Path.Combine(db.DirectoryPath, "local");
        var legacy = Path.Combine(app, "Data", "servex.db");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy));
        File.WriteAllText(legacy, "damaged legacy database");

        Assert.ThrowsAny<Exception>(() => DatabaseLocation.Resolve(app, local));
        Assert.False(File.Exists(Path.Combine(local, "ServeX", "database-location.txt")));
        Assert.Equal("damaged legacy database", File.ReadAllText(legacy));
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
        Assert.Throws<DatabaseSelectionException>(() => DatabaseLocation.Resolve(app, local));
        Assert.Equal(legacy, DatabaseLocation.Resolve(app, local, legacy).FilePath);
        Assert.Throws<DatabaseSelectionException>(() => DatabaseLocation.Resolve(app, local, Path.Combine(db.DirectoryPath, "missing.db")));
        Assert.Throws<DatabaseSelectionException>(() => DatabaseLocation.Resolve(app, local, "relative.db"));
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
    public async Task StagedRestoreLeavesLiveDataUntilStartupAndPreservesThePreviousDatabase()
    {
        using var db = new TestDatabase();
        await db.SeedCustomerAsync();
        var service = db.Services.GetRequiredService<DatabaseBackupService>();
        var backup = await service.BackupAsync(Path.Combine(db.DirectoryPath, "original.db"));
        using (var context = db.Context())
        {
            await context.Database.OpenConnectionAsync();
            await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
            await context.Database.ExecuteSqlRawAsync("UPDATE MenuItems SET Name='Most recent sale catalog';");
        }

        await service.StageRestoreAsync(backup);
        using (var before = db.Context())
            Assert.Equal("Most recent sale catalog", (await before.MenuItems.SingleAsync()).Name);

        var safetyPath = await service.ApplyPendingRestoreAsync();
        Assert.NotNull(safetyPath);
        using (var after = db.Context())
            Assert.Equal("Customer Item", (await after.MenuItems.SingleAsync()).Name);

        using var saved = new RuntimeDbContextFactory(new DatabaseLocation(safetyPath)).CreateDbContext();
        Assert.Equal("Most recent sale catalog", (await saved.MenuItems.SingleAsync()).Name);
        Assert.Null(await service.ApplyPendingRestoreAsync());
    }

    [Fact]
    public async Task DailyBackupsKeepSevenDatesAndPreserveUnmanagedFiles()
    {
        using var db = new TestDatabase();
        await db.SeedCustomerAsync();
        var service = db.Services.GetRequiredService<DatabaseBackupService>();
        var root = Path.Combine(db.DirectoryPath, "automatic");
        var firstDay = new DateOnly(2026, 11, 1);

        await service.CreateDailyBackupAsync(root, firstDay);
        var directory = Assert.Single(Directory.GetDirectories(root));
        var manual = Path.Combine(directory, "manual.db");
        await File.WriteAllTextAsync(manual, "unmanaged");
        await service.CreateDailyBackupAsync(root, firstDay);
        Assert.Single(Directory.GetFiles(directory, "daily-*.db"));

        for (var day = 1; day <= 8; day++)
            await service.CreateDailyBackupAsync(root, firstDay.AddDays(day));

        Assert.Equal(7, Directory.GetFiles(directory, "daily-*.db").Length);
        Assert.False(File.Exists(Path.Combine(directory, "daily-2026-11-02.db")));
        Assert.True(File.Exists(Path.Combine(directory, "daily-2026-11-09.db")));
        Assert.Equal("unmanaged", await File.ReadAllTextAsync(manual));
    }

    [Fact]
    public async Task InvalidRestoreCannotReplaceOrQueueAgainstActiveDatabase()
    {
        using var db = new TestDatabase();
        await db.SeedCustomerAsync();
        var service = db.Services.GetRequiredService<DatabaseBackupService>();
        var invalid = Path.Combine(db.DirectoryPath, "foreign.db");
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = invalid }.ToString()))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE Unrelated(Id INTEGER);";
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => service.StageRestoreAsync(invalid));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StageRestoreAsync(db.Location.FilePath));
        Assert.Null(await service.ApplyPendingRestoreAsync());
        using var check = db.Context();
        Assert.Equal("Customer Item", (await check.MenuItems.SingleAsync()).Name);
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
