using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ServeX.Data;

namespace ServeX.Services
{
    public class DatabaseInitializationService
    {
        private readonly IDbContextFactory<ServeXDbContext> _contexts;
        private readonly DatabaseBackupService _backup;
        private readonly LogService _log;
        public DatabaseInitializationService(IDbContextFactory<ServeXDbContext> contexts, DatabaseBackupService backup, LogService log)
        { _contexts = contexts; _backup = backup; _log = log; }

        public async Task InitializeAsync()
        {
            using var context = _contexts.CreateDbContext();
            var pending = (await context.Database.GetPendingMigrationsAsync()).ToArray();
            if (pending.Length == 0) return;
            var file = context.Database.GetDbConnection().DataSource;
            // Back up any nonempty existing SQLite database before forward migrations.
            if (File.Exists(file) && new FileInfo(file).Length > 0)
            {
                var destination = Path.Combine(Path.GetDirectoryName(file), "Backups",
                    "before-migration-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".db");
                await _backup.BackupAsync(destination);
                _log.LogInfo("Pre-migration backup created at " + destination);
            }
            await context.Database.MigrateAsync();
            _log.LogInfo("Applied forward database migrations: " + string.Join(", ", pending));
        }
    }
}
