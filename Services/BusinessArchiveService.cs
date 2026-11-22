using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ServeX.Data;

namespace ServeX.Services
{
    public sealed class BusinessArchiveService
    {
        private readonly IDbContextFactory<ServeXDbContext> _contexts;
        private readonly DatabaseBackupService _backup;
        private readonly LogService _log;
        private readonly OperatorSession _session;

        public BusinessArchiveService(IDbContextFactory<ServeXDbContext> contexts, DatabaseBackupService backup, LogService log, OperatorSession session)
        {
            _contexts = contexts;
            _backup = backup;
            _log = log;
            _session = session;
        }

        public Task ExportCsvAsync(string destination) => WriteArchiveAsync(destination, async archive =>
        {
            _session.RequireManager();
            using var context = _contexts.CreateDbContext();
            using var snapshot = await context.Database.BeginTransactionAsync();
            var orders = await context.Orders.AsNoTracking().OrderBy(order => order.Id).ToListAsync();
            var bills = await context.Bills.AsNoTracking().OrderBy(bill => bill.OrderId).ToListAsync();
            var menu = await context.MenuItems.AsNoTracking().OrderBy(item => item.Id).ToListAsync();
            await snapshot.CommitAsync();

            using (var entry = CsvWriter(archive, "orders.csv"))
            {
                entry.WriteLine("OrderId,Type,ReferenceId,Customer,Status,CreatedUtc");
                foreach (var order in orders)
                    entry.WriteLine(FormattableString.Invariant($"{order.Id},{order.Type},{order.ReferenceId},{Csv(order.CustomerName)},{order.Status},{AsUtc(order.CreatedAt)}"));
            }
            using (var entry = CsvWriter(archive, "bills.csv"))
            {
                entry.WriteLine("OrderId,Subtotal,Tax,Discount,Total,PaymentMethod,PaidUtc");
                foreach (var bill in bills)
                    entry.WriteLine(FormattableString.Invariant($"{bill.OrderId},{bill.Subtotal},{bill.Tax},{bill.Discount},{bill.Total},{bill.PaymentMethod},{AsUtc(bill.PaidAt)}"));
            }
            using (var entry = CsvWriter(archive, "payments.csv"))
            {
                entry.WriteLine("OrderId,Method,Amount,Reference,PaidUtc,CurrencyLocale");
                foreach (var bill in bills.Where(bill => bill.PaidAt.HasValue))
                foreach (var payment in BillingService.ReadPayments(bill))
                    entry.WriteLine(FormattableString.Invariant($"{bill.OrderId},{payment.Method},{payment.Amount},{Csv(payment.Reference)},{AsUtc(bill.PaidAt)},{Csv(bill.RecordedLocale)}"));
            }
            using (var entry = CsvWriter(archive, "menu.csv"))
            {
                entry.WriteLine("ItemId,Name,Category,Price,Available,Archived");
                foreach (var item in menu)
                    entry.WriteLine(FormattableString.Invariant($"{item.Id},{Csv(item.Name)},{Csv(item.Category)},{item.Price},{item.IsAvailable},{item.IsArchived}"));
            }
        });

        public async Task CreateSupportBundleAsync(string destination)
        {
            var working = Directory.CreateTempSubdirectory("servex-support-");
            try
            {
                var database = await _backup.BackupAsync(Path.Combine(working.FullName, "servex.db"));
                await WriteArchiveAsync(destination, archive =>
                {
                    archive.CreateEntryFromFile(database, "servex.db");
                    AddLogIfPresent(archive, _log.FilePath, "servex.log");
                    AddLogIfPresent(archive, _log.FilePath + ".1", "servex.log.1");
                    return Task.CompletedTask;
                });
            }
            finally
            {
                working.Delete(recursive: true);
            }
        }

        private static async Task WriteArchiveAsync(string destination, Func<ZipArchive, Task> write)
        {
            var staging = Path.GetFullPath(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var archive = ZipFile.Open(staging, ZipArchiveMode.Create)) await write(archive);
                File.Move(staging, destination, overwrite: false);
            }
            finally
            {
                if (File.Exists(staging)) File.Delete(staging);
            }
        }

        private static void AddLogIfPresent(ZipArchive archive, string path, string entryName)
        {
            if (!File.Exists(path)) return;
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var target = archive.CreateEntry(entryName).Open();
            source.CopyTo(target);
        }

        private static StreamWriter CsvWriter(ZipArchive archive, string name) =>
            new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        private static string AsUtc(DateTime? value) =>
            value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture) : string.Empty;

        private static string Csv(string value)
        {
            var text = value ?? string.Empty;
            var leading = text.TrimStart();
            if (leading.Length > 0 && "=+-@".Contains(leading[0])) text = "'" + text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
    }
}
