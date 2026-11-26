using Microsoft.Extensions.DependencyInjection;
using ServeX.Services;
using Xunit;

namespace ServeX.Tests;
public class DayCloseTests
{
    [Fact]
    public async Task DayCloseSummarisesTodaysPaidBillsAndFlagsUnpaidOrders()
    {
        using var db = new TestDatabase();
        var ids = await db.SeedCustomerAsync();
        var close = db.Services.GetRequiredService<DayCloseService>();

        var empty = await close.GetSummaryAsync(DateTime.Today);
        Assert.Equal(0, empty.PaidBills);
        Assert.Empty(empty.Sections);
        Assert.Null(empty.FirstBillLocal);

        var order = await db.Services.GetRequiredService<OrderService>().SaveTableOrderAsync(ids.TableId,
            new[] { new OrderSaveItem { MenuItemId = ids.MenuId, Price = 10m, Quantity = 2 } });
        var unpaid = await close.GetSummaryAsync(DateTime.Today);
        Assert.Single(unpaid.UnpaidOrders);

        var bill = await db.Services.GetRequiredService<BillingService>()
            .ConfirmPaymentAsync(order.Id, 0, ServeX.Models.PaymentMethod.Cash);
        var paid = await close.GetSummaryAsync(DateTime.Today);
        Assert.Equal(1, paid.PaidBills);
        Assert.Equal(bill.Total, Assert.Single(paid.Sections).Total);
        Assert.Empty(paid.UnpaidOrders);
        Assert.Equal(2, Assert.Single(paid.TopItems).Units);
        Assert.NotNull(paid.FirstBillLocal);

        var yesterday = await close.GetSummaryAsync(DateTime.Today.AddDays(-1));
        Assert.Equal(0, yesterday.PaidBills);
    }
}
