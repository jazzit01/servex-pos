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
                var todayBillTotals = await context.Bills
                    .AsNoTracking()
                    .Where(bill => bill.PaidAt.HasValue && bill.PaidAt.Value >= startUtc && bill.PaidAt.Value < endUtc)
                    .Select(bill => new { bill.Total, bill.RecordedLocale })
                    .ToListAsync();

                var todaySales = todayBillTotals.GroupBy(bill => ReceiptProfile.CurrencyCode(bill.RecordedLocale))
                    .OrderBy(group => group.Key)
                    .Select(group => new DashboardSalesTotal(group.First().RecordedLocale, group.Sum(bill => bill.Total)))
                    .ToList();
                if (todaySales.Count == 0) todaySales.Add(new DashboardSalesTotal(locale, 0m));

                var snapshot = new DashboardSnapshot
                {
                    TodaySales = todaySales,
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
