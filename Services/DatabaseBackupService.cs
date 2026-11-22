using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ServeX.Infrastructure.Persistence;

namespace ServeX.Services
{
    public class DatabaseBackupService
    {
        private readonly DatabaseLocation _location;
        public DatabaseBackupService(DatabaseLocation location) { _location = location; }

        public async Task<string> BackupAsync(string destinationFilePath)
        {
            if (string.IsNullOrWhiteSpace(destinationFilePath))
                throw new ArgumentException("A backup file path is required.", nameof(destinationFilePath));
            var sourcePath = _location.FilePath;
            var targetPath = Path.GetFullPath(destinationFilePath);
            if (!File.Exists(sourcePath)) throw new FileNotFoundException("The ServeX database file could not be found.", sourcePath);
            if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choose a different file name for the backup.");
            if (File.Exists(targetPath)) throw new IOException("A file already exists at the backup destination. Choose a new file name.");
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
            var stagingPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await Task.Run(() =>
                {
                    using (var source = Open(sourcePath, SqliteOpenMode.ReadOnly))
                    using (var target = Open(stagingPath, SqliteOpenMode.ReadWriteCreate))
                        source.BackupDatabase(target);
                    Verify(stagingPath);
                    File.Move(stagingPath, targetPath, false);
                });
                return targetPath;
            }
            finally
            {
                if (File.Exists(stagingPath)) File.Delete(stagingPath);
            }
        }

        public async Task CreateDailyBackupAsync(string backupRoot, DateOnly date)
        {
            const int retainedCopies = 7;
            var databasePath = OperatingSystem.IsWindows() ? _location.FilePath.ToUpperInvariant() : _location.FilePath;
            var databaseId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(databasePath)));
            var directory = Path.Combine(backupRoot, databaseId);
            var destination = Path.Combine(directory, $"daily-{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.db");
            if (!File.Exists(destination)) await BackupAsync(destination);

            // Only prune completed backups with the naming convention owned by this routine.
            var expired = Directory.EnumerateFiles(directory, "daily-*.db")
                .Where(path => DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(path).Substring(6),
                    "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                .Skip(retainedCopies);
            foreach (var path in expired) File.Delete(path);
        }

        private string PendingRestorePath => _location.FilePath + ".restore-pending.db";

        public async Task StageRestoreAsync(string backupPath)
        {
            if (string.Equals(Path.GetFullPath(backupPath), _location.FilePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choose a backup file instead of the active database.");

            await RestoreToNewFileAsync(backupPath, PendingRestorePath, VerifyRestoreSource, PrepareRestoreSource);
        }

        // Called under the app's single-instance mutex, before any workspace opens.
        public async Task<string> ApplyPendingRestoreAsync()
        {
            if (!File.Exists(PendingRestorePath)) return null;
            VerifyRestoreSource(PendingRestorePath, requireCurrentSchema: true);
            var directory = Path.GetDirectoryName(_location.FilePath);
            var safetyPath = Path.Combine(directory, "Backups",
                $"before-restore-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");
            await BackupAsync(safetyPath);

            // SQLite applies the copy transactionally, including when the live file uses WAL.
            // If startup is interrupted before the staging file is removed, the next launch
            // repeats recovery before the database is made available to staff.
            using (var source = Open(PendingRestorePath, SqliteOpenMode.ReadOnly))
            using (var target = Open(_location.FilePath, SqliteOpenMode.ReadWrite))
                source.BackupDatabase(target);

            VerifyRestoreSource(_location.FilePath, requireCurrentSchema: true);
            File.Delete(PendingRestorePath);
            return safetyPath;
        }

        private static void VerifyRestoreSource(string path) => VerifyRestoreSource(path, requireCurrentSchema: false);

        private static void PrepareRestoreSource(string path)
        {
            using (var connection = Open(path, SqliteOpenMode.ReadWrite))
            {
                var options = new DbContextOptionsBuilder<ServeX.Data.ServeXDbContext>().UseSqlite(connection).Options;
                using var context = new ServeX.Data.ServeXDbContext(options);
                context.Database.Migrate();
            }

            VerifyRestoreSource(path, requireCurrentSchema: true);
        }

        private static void VerifyRestoreSource(string path, bool requireCurrentSchema)
        {
            Verify(path);
            using var connection = Open(path, SqliteOpenMode.ReadOnly);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('OrderItems','Rooms','Settings','Tables','Users');";
            if (Convert.ToInt32(command.ExecuteScalar()) != 5)
                throw new InvalidDataException("The backup is missing required ServeX tables.");

            var options = new DbContextOptionsBuilder<ServeX.Data.ServeXDbContext>().UseSqlite(connection).Options;
            using var context = new ServeX.Data.ServeXDbContext(options);
            var supported = context.Database.GetMigrations().ToArray();
            var applied = context.Database.GetAppliedMigrations().ToArray();
            if (applied.Length < 3 || !applied.SequenceEqual(supported.Take(applied.Length)))
                throw new InvalidDataException("The backup uses an unsupported ServeX database version.");
            if (!requireCurrentSchema) return;

            if (applied.Length != supported.Length)
                throw new InvalidDataException("The staged backup is missing ServeX migrations.");
            foreach (var table in context.Model.GetRelationalModel().Tables)
            {
                // The names come from the compiled ServeX model, not the backup.
                command.CommandText = $"SELECT {string.Join(", ", table.Columns.Select(column => "[" + column.Name + "]"))} FROM [{table.Name}] LIMIT 0;";
                using var reader = command.ExecuteReader();
            }
        }

        // Offline operator recovery only. Never overwrites a database or targets the live path.
        public static Task RestoreToNewFileAsync(string backupPath, string newDatabasePath) =>
            Task.Run(() => CopyVerifiedDatabase(backupPath, newDatabasePath));

        internal static void CopyVerifiedDatabase(string backupPath, string newDatabasePath) =>
            CopyVerifiedDatabase(backupPath, newDatabasePath, Verify);

        private static Task RestoreToNewFileAsync(string backupPath, string newDatabasePath, Action<string> verify, Action<string> prepare) =>
            Task.Run(() => CopyVerifiedDatabase(backupPath, newDatabasePath, verify, prepare));

        private static void CopyVerifiedDatabase(string backupPath, string newDatabasePath, Action<string> verify, Action<string> prepare = null)
        {
            verify(backupPath);
            if (File.Exists(newDatabasePath)) throw new IOException("Restore requires a new, unused destination file.");
            var staging = newDatabasePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(newDatabasePath)));
                using (var source = Open(backupPath, SqliteOpenMode.ReadOnly))
                using (var target = Open(staging, SqliteOpenMode.ReadWriteCreate)) source.BackupDatabase(target);
                verify(staging);
                prepare?.Invoke(staging);
                File.Move(staging, newDatabasePath, false);
            }
            finally { if (File.Exists(staging)) File.Delete(staging); }
        }

        private static SqliteConnection Open(string path, SqliteOpenMode mode)
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = path, Mode = mode, Pooling = false }.ToString());
            try { connection.Open(); return connection; }
            catch { connection.Dispose(); throw; }
        }

        private static void Verify(string path)
        {
            using var connection = Open(path, SqliteOpenMode.ReadOnly);
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            if (!string.Equals(command.ExecuteScalar() as string, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SQLite integrity verification failed.");
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('Orders','Bills','MenuItems','__EFMigrationsHistory');";
            if (Convert.ToInt32(command.ExecuteScalar()) != 4)
                throw new InvalidDataException("The file is not a migrated ServeX database.");
        }
    }
}
