using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ServeX.Data;
using ServeX.Models;

namespace ServeX.Services
{
    public class DashboardService
    {
        private readonly IDbContextFactory<ServeXDbContext> _contexts;
        private readonly OrderHistoryService _history;
        public DashboardService(IDbContextFactory<ServeXDbContext> contexts, OrderHistoryService history)
        {
            _contexts = contexts;
            _history = history;
        }

        public async Task<DashboardSnapshot> GetSnapshotAsync()
        {
            var startLocal = DateTime.Today;
            var endLocal = startLocal.AddDays(1);
            var startUtc = startLocal.ToUniversalTime();
            var endUtc = endLocal.ToUniversalTime();

            using (var context = _contexts.CreateDbContext())
            {
                var locale = await context.Settings.AsNoTracking().OrderBy(setting => setting.Id)
                    .Select(setting => setting.Locale).FirstOrDefaultAsync();
                var paidToday = await context.Bills
                    .AsNoTracking()
                    .Where(bill => bill.PaidAt.HasValue && bill.PaidAt.Value >= startUtc && bill.PaidAt.Value < endUtc)
                    .Include(bill => bill.Order).ThenInclude(order => order.Items).ThenInclude(line => line.MenuItem)
                    .ToListAsync();

                var todaySales = paidToday.GroupBy(bill => ReceiptProfile.CurrencyCode(bill.RecordedLocale))
                    .OrderBy(group => group.Key)
                    .Select(group => new DashboardSalesTotal(group.First().RecordedLocale, group.Sum(bill => bill.Total), group.Count()))
                    .ToList();
                if (todaySales.Count == 0) todaySales.Add(new DashboardSalesTotal(locale, 0m));

                var snapshot = new DashboardSnapshot
                {
                    TodaySales = todaySales,
                    TopItems = BuildTopItems(paidToday),
                    Payments = BuildPayments(paidToday, locale),
                    TotalTables = await context.Tables.CountAsync(),
                    OccupiedTables = await context.Tables.CountAsync(table => table.Status == TableStatus.Occupied),
                    AvailableTables = await context.Tables.CountAsync(table => table.Status == TableStatus.Available),
                    ReservedTables = await context.Tables.CountAsync(table => table.Status == TableStatus.Reserved),

                    TotalOrdersToday = await context.Orders
                        .AsNoTracking()
                        .CountAsync(order => order.CreatedAt >= startUtc && order.CreatedAt < endUtc),

                    ParcelOrdersCount = await context.Orders
                        .AsNoTracking()
                        .CountAsync(order =>
                            order.CreatedAt >= startUtc &&
                            order.CreatedAt < endUtc &&
                            order.Type == OrderType.Parcel)
                };

                var recentOrders = await context.Orders
                    .AsNoTracking()
                    .Include(order => order.Items)
                    .Include(order => order.Bill)
                    .OrderByDescending(order => order.CreatedAt)
                    .Take(5)
                    .ToListAsync();

                var tableIds = recentOrders
                    .Where(order => order.Type == OrderType.Table)
                    .Select(order => order.ReferenceId)
                    .Distinct()
                    .ToList();

                var tableMap = await context.Tables
                    .AsNoTracking()
                    .Where(table => tableIds.Contains(table.Id))
                    .ToDictionaryAsync(table => table.Id, table => table.TableNumber);

                var roomIds = recentOrders.Where(order => order.Type == OrderType.Room)
                    .Select(order => order.ReferenceId).Distinct().ToList();
                var roomMap = await context.Rooms.AsNoTracking()
                    .Where(room => roomIds.Contains(room.Id))
                    .ToDictionaryAsync(room => room.Id, room => room.RoomNumber);

                snapshot.RecentOrders = recentOrders.Select(order => new DashboardRecentOrder
                {
                    OrderId = order.Id,
                    Type = order.Type,
                    Status = order.Status == OrderStatus.Billed && order.Bill?.PaidAt != null ? OrderStatus.Paid : order.Status,
                    CreatedAt = order.CreatedAt,
                    Locale = order.Bill?.PaidAt != null ? order.Bill.RecordedLocale ?? "en-IN" : locale,
                    Total = order.Bill?.Total ?? (order.Items ?? Array.Empty<OrderItem>()).Select(item => item.Price * item.Quantity).DefaultIfEmpty(0m).Sum(),
                    ReferenceLabel = GetReferenceLabel(order, tableMap, roomMap)
                }).ToList();

                snapshot.OpenOrders = await _history.GetOrdersAsync(startLocal, startLocal, unpaidOnly: true);
                snapshot.ActiveOrders = snapshot.OpenOrders.Count;
                return snapshot;
            }
        }

        public async Task<Table> GetPreferredTableForNewOrderAsync()
        {
            using (var context = _contexts.CreateDbContext())
            {
                return await context.Tables
                    .AsNoTracking()
                    .Where(table => table.Status == TableStatus.Available)
                    .OrderBy(table => table.TableNumber)
                    .FirstOrDefaultAsync();
            }
        }

        internal static System.Collections.Generic.IList<DashboardTopItem> BuildTopItems(
            System.Collections.Generic.IEnumerable<Bill> paidToday)
        {
            return paidToday
                .Where(bill => bill.Order != null && bill.Order.Status != OrderStatus.Cancelled)
                .SelectMany(bill => bill.Order.Items)
                .GroupBy(line => line.MenuItemId)
                .Select(group => new
                {
                    Name = group.Select(line => line.MenuItem?.Name).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "Item",
                    Units = group.Sum(line => line.Quantity)
                })
                .OrderByDescending(item => item.Units)
                .ThenBy(item => item.Name)
                .Take(5)
                .Select((item, index) => new DashboardTopItem(index + 1, item.Name, item.Units))
                .ToList();
        }

        internal static System.Collections.Generic.IList<DashboardPaymentRow> BuildPayments(
            System.Collections.Generic.IEnumerable<Bill> paidToday, string fallbackLocale)
        {
            var allocations = paidToday
                .SelectMany(bill =>
                {
                    var billLocale = bill.RecordedLocale ?? fallbackLocale;
                    return ReadPaymentsSafely(bill).Select(payment => new
                    {
                        Locale = billLocale,
                        Currency = ReceiptProfile.CurrencyCode(billLocale),
                        payment.Method,
                        payment.Amount
                    });
                })
                .ToList();

            var currencyCount = allocations.Select(item => item.Currency).Distinct().Count();
            var totalsByCurrency = allocations
                .GroupBy(item => item.Currency)
                .ToDictionary(group => group.Key, group => group.Sum(item => item.Amount));

            return allocations
                .GroupBy(item => new { item.Currency, item.Method })
                .Select(group =>
                {
                    var amount = group.Sum(item => item.Amount);
                    var currencyTotal = totalsByCurrency[group.Key.Currency];
                    var methodName = group.Key.Method == PaymentMethod.Upi ? "UPI" : group.Key.Method.ToString();
                    return new DashboardPaymentRow(
                        currencyCount > 1 ? methodName + " (" + group.Key.Currency + ")" : methodName,
                        group.Count(),
                        amount,
                        group.First().Locale,
                        currencyTotal > 0m ? (double)(amount / currencyTotal * 100m) : 0d);
                })
                .OrderByDescending(row => row.Amount)
                .ToList();
        }

        private static System.Collections.Generic.IReadOnlyList<PaymentAllocation> ReadPaymentsSafely(Bill bill)
        {
            try
            {
                return BillingService.ReadPayments(bill);
            }
            catch (System.Exception)
            {
                // A bill with an unreadable breakdown still counts: fall back to its recorded method and total.
                return new[] { new PaymentAllocation(bill.Total, bill.PaymentMethod ?? PaymentMethod.Cash, bill.PaymentReference) };
            }
        }

        private static string GetReferenceLabel(Order order, System.Collections.Generic.IDictionary<int, string> tableMap,
            System.Collections.Generic.IDictionary<int, string> roomMap)
        {
            switch (order.Type)
            {
                case OrderType.Table:
                    if (tableMap.TryGetValue(order.ReferenceId, out var tableNumber))
                    {
                        return tableNumber;
                    }

                    return string.Format("Table #{0}", order.ReferenceId);

                case OrderType.Room:
                    return roomMap.TryGetValue(order.ReferenceId, out var roomNumber) ? roomNumber : $"Room #{order.ReferenceId}";

                case OrderType.Parcel:
                    return BillingService.GetParcelLabel(order.CustomerName);

                default:
                    return string.Format("#{0}", order.ReferenceId);
            }
        }
    }
}
