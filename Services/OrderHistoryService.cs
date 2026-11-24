using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ServeX.Data;
using ServeX.Models;

namespace ServeX.Services
{
    public sealed record HistoryRow(int Id, OrderType Type, OrderStatus Status, DateTime CreatedLocal, string Reference,
        IReadOnlyList<PaymentMethod> Methods, decimal Total, string Locale)
    {
        public int ReferenceId { get; init; }
        public bool IsUnfinished => Status is OrderStatus.Open or OrderStatus.Preparing or OrderStatus.Served;
        public string ServiceLabel => Type == OrderType.Parcel ? "Takeaway" : Type == OrderType.Room ? "Room service" : "Dine-in";
        public string DisplayTotal => ReceiptProfile.FormatAmount(Total, Locale);
        public string PaymentDisplay => string.Join(" / ", Methods);
    }
    public sealed record ReportRow(string Label, int Units, decimal Amount, string Locale = null)
    {
        public string Currency => Locale == null ? string.Empty : ReceiptProfile.CurrencyCode(Locale);
        public string DisplayAmount => Locale == null ? string.Empty : ReceiptProfile.FormatAmount(Amount, Locale);
    }

    public sealed class OrderHistoryService
    {
        private readonly IDbContextFactory<ServeXDbContext> _contexts;
        public OrderHistoryService(IDbContextFactory<ServeXDbContext> contexts) { _contexts = contexts; }

        public Task<List<HistoryRow>> GetOpenOrdersAsync() => GetOrdersAsync(default, default, unpaidOnly: true);

        public async Task<List<HistoryRow>> GetOrdersAsync(DateTime start, DateTime end, bool unpaidOnly = false)
        {
            using var context = _contexts.CreateDbContext();
            var query = context.Orders.AsNoTracking().Include(order => order.Bill).AsQueryable();
            if (unpaidOnly)
                query = query.Where(order => order.Status == OrderStatus.Open || order.Status == OrderStatus.Preparing ||
                    order.Status == OrderStatus.Served).Where(order => order.Bill == null || order.Bill.PaidAt == null);
            else
            {
                var (from, until) = UtcRange(start, end);
                query = query.Where(order => order.CreatedAt >= from && order.CreatedAt < until);
            }
            var orders = await query.OrderByDescending(order => order.CreatedAt).ThenByDescending(order => order.Id).ToListAsync();
            var tables = await context.Tables.AsNoTracking().ToDictionaryAsync(table => table.Id, table => table.TableNumber);
            var rooms = await context.Rooms.AsNoTracking().ToDictionaryAsync(room => room.Id, room => room.RoomNumber);
            var locale = await context.Settings.AsNoTracking().OrderBy(setting => setting.Id)
                .Select(setting => setting.Locale).FirstOrDefaultAsync();
            return orders.Select(order => new HistoryRow(order.Id, order.Type,
                order.Status == OrderStatus.Billed && order.Bill?.PaidAt != null ? OrderStatus.Paid : order.Status,
                BusinessTime.ToLocalTime(order.CreatedAt),
                order.Type switch
                {
                    OrderType.Table => tables.TryGetValue(order.ReferenceId, out var number) ? number : $"Table #{order.ReferenceId}",
                    OrderType.Parcel => BillingService.GetParcelLabel(order.CustomerName),
                    _ => rooms.TryGetValue(order.ReferenceId, out var room) ? room : $"Room #{order.ReferenceId}"
                },
                order.Bill?.PaidAt != null ? BillingService.ReadPayments(order.Bill).Select(payment => payment.Method).Distinct().ToArray() :
                    Array.Empty<PaymentMethod>(), order.Bill?.Total ?? 0m,
                order.Bill?.PaidAt != null ? order.Bill.RecordedLocale ?? "en-IN" : locale) { ReferenceId = order.ReferenceId }).ToList();
        }

        public async Task<IReadOnlyList<ReportRow>> GetReportAsync(DateTime start, DateTime end, string grouping)
        {
            var (from, until) = UtcRange(start, end);
            using var context = _contexts.CreateDbContext();
            var paid = await context.Bills.AsNoTracking()
                .Where(bill => bill.PaidAt >= from && bill.PaidAt < until)
                .Include(bill => bill.Order).ThenInclude(order => order.Items).ThenInclude(line => line.MenuItem)
                .ToListAsync();

            var rows = new List<ReportRow>();
            foreach (var currency in paid.GroupBy(bill => ReceiptProfile.CurrencyCode(bill.RecordedLocale)).OrderBy(group => group.Key))
            {
                var locale = currency.First().RecordedLocale ?? "en-IN";
                if (grouping == "Summary")
                {
                    rows.AddRange(currency.SelectMany(BillingService.ReadPayments).GroupBy(payment => payment.Method)
                        .Select(method => new ReportRow(method.Key.ToString(), method.Count(), method.Sum(payment => payment.Amount), locale)));
                    rows.Add(new ReportRow("Total revenue", currency.Count(), currency.Sum(bill => bill.Total), locale));
                    rows.Add(new ReportRow("Tax collected", currency.Count(), currency.Sum(bill => bill.Tax), locale));
                    rows.Add(new ReportRow("Discounts", currency.Count(bill => bill.Discount > 0), currency.Sum(bill => bill.Discount), locale));
                }
                else if (grouping is "Items" or "Categories")
                {
                    rows.AddRange(currency.SelectMany(bill => bill.Order.Items)
                        .GroupBy(line => grouping == "Items" ? line.MenuItem.Name : line.MenuItem.Category)
                        .Select(group => new ReportRow(group.Key, group.Sum(line => line.Quantity),
                            group.Sum(line => line.Price * line.Quantity), locale))
                        .OrderByDescending(row => row.Units));
                }
                else
                {
                    rows.AddRange(currency.GroupBy(bill => Period(BusinessTime.ToLocalTime(bill.PaidAt.Value), grouping))
                        .OrderBy(group => group.Key)
                        .Select(group => new ReportRow(group.Key, group.Count(), group.Sum(bill => bill.Total), locale)));
                }
            }

            if (grouping == "Summary")
            {
                var cancelled = await context.Orders.CountAsync(order =>
                    order.Status == OrderStatus.Cancelled && (order.CancelledAt ?? order.CreatedAt) >= from &&
                    (order.CancelledAt ?? order.CreatedAt) < until);
                rows.Add(new ReportRow("Cancelled orders", cancelled, 0m));
            }

            return rows;
        }

        private static string Period(DateTime day, string grouping) => grouping switch
        {
            "Week" => FormattableString.Invariant($"{ISOWeek.GetYear(day)}-W{ISOWeek.GetWeekOfYear(day):00}"),
            "Month" => day.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            _ => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };

        private static (DateTime StartUtc, DateTime EndUtc) UtcRange(DateTime start, DateTime end) =>
            (DateTime.SpecifyKind(start.Date, DateTimeKind.Local).ToUniversalTime(),
             DateTime.SpecifyKind(end.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime());
    }
}
