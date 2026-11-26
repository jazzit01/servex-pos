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

    [Fact]
    public async Task DashboardReportsBestSellersPaymentMixAndAverageBill()
    {
        using var db = new TestDatabase();
        var ids = await db.SeedCustomerAsync();
        var dashboard = db.Services.GetRequiredService<DashboardService>();

        var empty = await dashboard.GetSnapshotAsync();
        Assert.Equal(1, empty.TotalTables);
        Assert.Equal(0, empty.PaidBillsToday);
        Assert.Equal("—", empty.AverageOrderDisplay);
        Assert.Empty(empty.TopItems);
        Assert.Empty(empty.Payments);

        var order = await db.Services.GetRequiredService<OrderService>().SaveTableOrderAsync(ids.TableId,
            new[] { new OrderSaveItem { MenuItemId = ids.MenuId, Price = 19.99m, Quantity = 3 } });
        var bill = await db.Services.GetRequiredService<BillingService>()
            .ConfirmPaymentAsync(order.Id, 0, ServeX.Models.PaymentMethod.Cash);

        var snapshot = await dashboard.GetSnapshotAsync();
        Assert.Equal(1, snapshot.PaidBillsToday);
        Assert.Equal(ReceiptProfile.FormatAmount(bill.Total, "en-IN"), snapshot.AverageOrderDisplay);

        var top = Assert.Single(snapshot.TopItems);
        Assert.Equal(1, top.Rank);
        Assert.Equal("Customer Item", top.Name);
        Assert.Equal(3, top.Units);

        var payment = Assert.Single(snapshot.Payments);
        Assert.Equal("Cash", payment.Label);
        Assert.Equal(bill.Total, payment.Amount);
        Assert.Equal(1, payment.Count);
        Assert.Equal(100d, payment.SharePercent, 1);
    }

    [Theory]
    [InlineData(0, "Just now")]
    [InlineData(5, "5 min")]
    [InlineData(59, "59 min")]
    [InlineData(61, "1 h 1 min")]
    [InlineData(1500, "1 d 1 h")]
    public void OpenOrderAgeIsReadable(int minutes, string expected)
    {
        Assert.Equal(expected, DashboardStatus.FormatAge(TimeSpan.FromMinutes(minutes)));
    }

    [Fact]
    public void OrdersOpenForAnHourAreFlaggedAsStale()
    {
        var created = new DateTime(2026, 11, 25, 9, 0, 0);
        var row = new HistoryRow(7, ServeX.Models.OrderType.Table, ServeX.Models.OrderStatus.Open, created, "T1",
            Array.Empty<ServeX.Models.PaymentMethod>(), 100m, "en-IN");

        Assert.False(new DashboardOpenOrder(row, created.AddMinutes(59)).IsStale);
        Assert.True(new DashboardOpenOrder(row, created.AddMinutes(60)).IsStale);
        Assert.Equal(TimeSpan.Zero, new DashboardOpenOrder(row, created.AddMinutes(-5)).Age);
    }
}
