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

            var file = context.Database.GetDbConnection().DataSource;
            bool backedUp = false;
            if (pending.Length > 0 && File.Exists(file) && new FileInfo(file).Length > 0)
            {
                await BackupBeforeChangeAsync(file);
                backedUp = true;
            }
            await context.Database.MigrateAsync();
            if (pending.Length > 0)
                _log.LogInfo("Applied forward database migrations: " + string.Join(", ", pending));

            // Earlier versions stored plaintext in Password. Convert all legacy accounts
            // before login, and save hashes and markers together so a failed write can retry.
            await context.Database.OpenConnectionAsync();
            var legacyUsers = await context.Users.Where(user => !user.IsPasswordHashed).ToListAsync();
            foreach (var user in legacyUsers)
            {
                user.Password = PasswordHash.Create(user.Password);
                user.IsPasswordHashed = true;
            }
            if (legacyUsers.Count > 0)
            {
                await context.Database.ExecuteSqlRawAsync("PRAGMA secure_delete = ON;");
                await context.SaveChangesAsync();
            }

            // The historic AddTableAreaSupport migration used an empty string default.
            // EF cannot materialize that value as TableArea. Repair only that legacy default,
            // preserving every table ID, status and relationship, with a backup first.
            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Tables WHERE Area = '';";
            if (Convert.ToInt64(await command.ExecuteScalarAsync()) > 0)
            {
                if (!backedUp) await BackupBeforeChangeAsync(file);
                await context.Database.ExecuteSqlRawAsync("UPDATE Tables SET Area = 'Indoor' WHERE Area = '';");
                _log.LogInfo("Repaired empty legacy table areas to Indoor.");
            }
        }
        private async Task BackupBeforeChangeAsync(string file)
        {
            var destination = Path.Combine(Path.GetDirectoryName(file), "Backups",
                "before-migration-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".db");
            await _backup.BackupAsync(destination);
            _log.LogInfo("Pre-initialization backup created at " + destination);
        }
    }
}
