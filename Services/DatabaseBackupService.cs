using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
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

        // Offline operator recovery only. Never overwrites a database or targets the live path.
        public static async Task RestoreToNewFileAsync(string backupPath, string newDatabasePath)
        {
            await Task.Run(() =>
            {
                Verify(backupPath);
                if (File.Exists(newDatabasePath)) throw new IOException("Restore requires a new, unused destination file.");
                var staging = newDatabasePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(newDatabasePath)));
                    using (var source = Open(backupPath, SqliteOpenMode.ReadOnly))
                    using (var target = Open(staging, SqliteOpenMode.ReadWriteCreate)) source.BackupDatabase(target);
                    Verify(staging);
                    File.Move(staging, newDatabasePath, false);
                }
                finally { if (File.Exists(staging)) File.Delete(staging); }
            });
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
