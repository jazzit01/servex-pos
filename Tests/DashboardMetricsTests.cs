using Microsoft.Extensions.DependencyInjection;
using ServeX.Services;
using Xunit;

namespace ServeX.Tests;
public class DashboardMetricsTests
{
    [Fact]
    public async Task OperationalMetricsReflectRealOrdersAndPaymentTransitions()
    {
        using var db = new TestDatabase();
        var ids = await db.SeedCustomerAsync();
        using (var context = db.Context())
        {
            context.Tables.Add(new ServeX.Models.Table { TableNumber = "Reserved", Status = ServeX.Models.TableStatus.Reserved });
            await context.SaveChangesAsync();
        }
        var dashboard = db.Services.GetRequiredService<DashboardService>();
        var initial = await dashboard.GetSnapshotAsync();
        Assert.Equal(1, initial.AvailableTables);
        Assert.Equal(0, initial.OccupiedTables);
        Assert.Equal(1, initial.ReservedTables);
        Assert.Equal(0, initial.ActiveOrders);
        Assert.Equal(0m, Assert.Single(initial.TodaySales).Amount);
        var order = await db.Services.GetRequiredService<OrderService>().SaveTableOrderAsync(ids.TableId,
            new[] { new OrderSaveItem { MenuItemId = ids.MenuId, Price = 19.99m, Quantity = 1 } });
        var active = await dashboard.GetSnapshotAsync();
        Assert.Equal(0, active.AvailableTables);
        Assert.Equal(1, active.OccupiedTables);
        Assert.Equal(1, active.ReservedTables);
        Assert.Equal(1, active.ActiveOrders);
        Assert.Equal(0m, Assert.Single(active.TodaySales).Amount);
        var bill = await db.Services.GetRequiredService<BillingService>().ConfirmPaymentAsync(order.Id, 0, ServeX.Models.PaymentMethod.Cash);
        var paid = await dashboard.GetSnapshotAsync();
        Assert.Equal(0, paid.AvailableTables);
        Assert.Equal(0, paid.OccupiedTables);
        Assert.Equal(0, paid.ActiveOrders);
        Assert.Equal(bill.Total, Assert.Single(paid.TodaySales).Amount);
        Assert.Equal(ServeX.Models.OrderStatus.Paid, Assert.Single(paid.RecentOrders).Status);
    }
}
