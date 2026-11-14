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
        public DashboardService(IDbContextFactory<ServeXDbContext> contexts, LogService logService)
        {
            _contexts = contexts;
        }

        public async Task<DashboardSnapshot> GetSnapshotAsync()
        {
            var startLocal = DateTime.Today;
            var endLocal = startLocal.AddDays(1);
            var startUtc = startLocal.ToUniversalTime();
            var endUtc = endLocal.ToUniversalTime();

            using (var context = _contexts.CreateDbContext())
            {
                var todayBillTotals = await context.Bills
                    .AsNoTracking()
                    .Where(bill => bill.PaidAt.HasValue && bill.PaidAt.Value >= startUtc && bill.PaidAt.Value < endUtc)
                    .Select(bill => bill.Total)
                    .ToListAsync();

                var snapshot = new DashboardSnapshot
                {
                    TodayRevenue = todayBillTotals.DefaultIfEmpty(0m).Sum(),

                    ActiveTables = await context.Tables
                        .AsNoTracking()
                        .CountAsync(table => table.Status != TableStatus.Available),

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

                snapshot.RecentOrders = recentOrders.Select(order => new DashboardRecentOrder
                {
                    OrderId = order.Id,
                    Type = order.Type,
                    Status = order.Status,
                    CreatedAt = order.CreatedAt,
                    Total = order.Bill?.Total ?? (order.Items ?? Array.Empty<OrderItem>()).Select(item => item.Price * item.Quantity).DefaultIfEmpty(0m).Sum(),
                    ReferenceLabel = GetReferenceLabel(order, tableMap)
                }).ToList();

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

        private static string GetReferenceLabel(Order order, System.Collections.Generic.IDictionary<int, string> tableMap)
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
                    return string.Format("Room #{0}", order.ReferenceId);

                case OrderType.Parcel:
                    return string.IsNullOrWhiteSpace(order.CustomerName)
                        ? "Parcel - Atithi"
                        : "Parcel - " + order.CustomerName;

                default:
                    return string.Format("#{0}", order.ReferenceId);
            }
        }
    }
}
