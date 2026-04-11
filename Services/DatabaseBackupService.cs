using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using ServeX.Data;

namespace ServeX.Services
{
    public class DatabaseBackupService
    {
        public async Task<string> BackupAsync(string destinationFilePath)
        {
            if (string.IsNullOrWhiteSpace(destinationFilePath))
            {
                throw new ArgumentException("A backup file path is required.", nameof(destinationFilePath));
            }

            var sourcePath = Path.GetFullPath(ServeXDbContext.GetDatabaseFilePath());
            var targetPath = Path.GetFullPath(destinationFilePath);

            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("The ServeX database file could not be found.", sourcePath);
            }

            if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Choose a different file name for the backup.");
            }

            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrWhiteSpace(targetDirectory))
            {
                throw new InvalidOperationException("The selected backup location is invalid.");
            }

            Directory.CreateDirectory(targetDirectory);

            await Task.Run(() =>
            {
                using (var sourceConnection = new SqliteConnection("Data Source=" + sourcePath + ";Mode=ReadOnly"))
                using (var targetConnection = new SqliteConnection("Data Source=" + targetPath))
                {
                    sourceConnection.Open();
                    targetConnection.Open();
                    sourceConnection.BackupDatabase(targetConnection);
                }
            });

            return targetPath;
        }
    }
}
