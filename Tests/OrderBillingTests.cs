using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServeX.Models;
using ServeX.Services;
using Xunit;

namespace ServeX.Tests;

public class OrderBillingTests
{
    [Theory]
    [InlineData(19.99, 2, 5, 39.98, 2.00, 41.98)]
    [InlineData(10.10, 1, 5, 10.10, 0.50, 10.60)]
    [InlineData(99.95, 3, 18, 299.85, 53.97, 353.82)]
    [InlineData(12.50, 4, 0, 50, 0, 50)]
    public async Task OrderTotalsKeepExistingDecimalRounding(double price, int quantity, double taxPercent, double subtotal, double tax, double total)
    {
        using var db = new TestDatabase();
        var ids = await db.SeedCustomerAsync();
        await db.Services.GetRequiredService<ApplicationSettingsService>().UpdateSettingsAsync("Restaurant", "123", (decimal)taxPercent);
        var order = await db.Services.GetRequiredService<OrderService>().SaveTableOrderAsync(ids.TableId,
            new[] { new OrderSaveItem { MenuItemId = ids.MenuId, Quantity = quantity, Price = (decimal)price } });
        Assert.Equal((decimal)subtotal, order.Bill.Subtotal);
        Assert.Equal((decimal)tax, order.Bill.Tax);
        Assert.Equal((decimal)total, order.Bill.Total);
        using var context = db.Context();
        Assert.Equal(TableStatus.Occupied, (await context.Tables.SingleAsync()).Status);
        Assert.Equal(OrderStatus.Open, (await context.Orders.SingleAsync()).Status);
    }

    [Theory]
    [InlineData(PaymentMethod.Cash)]
    [InlineData(PaymentMethod.Card)]
    [InlineData(PaymentMethod.Upi)]
    public async Task PaymentPersistsDiscountAndReleasesTableAndCannotRepeat(PaymentMethod method)
    {
        using var db = new TestDatabase();
        var ids = await db.SeedCustomerAsync();
        var orders = db.Services.GetRequiredService<OrderService>();
        var billing = db.Services.GetRequiredService<BillingService>();
        var saved = await orders.SaveTableOrderAsync(ids.TableId,
            new[] { new OrderSaveItem { MenuItemId = ids.MenuId, Quantity = 2, Price = 19.99m } });
        var bill = await billing.ConfirmPaymentAsync(saved.Id, 1.98m, method);
        Assert.Equal(40m, bill.Total);
        Assert.Equal(1.98m, bill.Discount);
        Assert.NotNull(bill.PaidAt);
        Assert.Equal(method, bill.PaymentMethod);
        using var context = db.Context();
        Assert.Equal(OrderStatus.Billed, (await context.Orders.SingleAsync()).Status);
        Assert.Equal(TableStatus.Available, (await context.Tables.SingleAsync()).Status);
        Assert.Equal(40m, (await context.Bills.SingleAsync()).Total);
        await Assert.ThrowsAsync<InvalidOperationException>(() => billing.ConfirmPaymentAsync(saved.Id, 0, method));
        Assert.Single(await context.Bills.ToListAsync());
        var receipt = billing.CreateReceiptData(await billing.GetBillingOrderAsync(saved.Id));
        Assert.Equal(bill.Total, receipt.Total);
        Assert.Equal(39.98m, Assert.Single(receipt.Items).LineTotal);
        Assert.Equal("Customer T1", receipt.TableNumber);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    public async Task InvalidDiscountDoesNotPersistPayment(double discount)
    {
        using var db = new TestDatabase();
        var ids = await db.SeedCustomerAsync();
        var saved = await db.Services.GetRequiredService<OrderService>().SaveTableOrderAsync(ids.TableId,
            new[] { new OrderSaveItem { MenuItemId = ids.MenuId, Quantity = 1, Price = 19.99m } });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Services.GetRequiredService<BillingService>()
            .ConfirmPaymentAsync(saved.Id, (decimal)discount, PaymentMethod.Cash));
        using var context = db.Context();
        Assert.Null((await context.Bills.SingleAsync()).PaidAt);
        Assert.Equal(OrderStatus.Open, (await context.Orders.SingleAsync()).Status);
        Assert.Equal(TableStatus.Occupied, (await context.Tables.SingleAsync()).Status);
    }

    [Fact]
    public async Task EditActiveOrderKeepsIdentityAndReplacesItems()
    {
        using var db = new TestDatabase();
        var ids = await db.SeedCustomerAsync();
        var orders = db.Services.GetRequiredService<OrderService>();
        var first = await orders.SaveTableOrderAsync(ids.TableId,
            new[] { new OrderSaveItem { MenuItemId = ids.MenuId, Quantity = 1, Price = 19.99m } });
        var edited = await orders.SaveTableOrderAsync(ids.TableId,
            new[] { new OrderSaveItem { MenuItemId = ids.MenuId, Quantity = 3, Price = 19.99m } });
        Assert.Equal(first.Id, edited.Id);
        using var context = db.Context();
        Assert.Single(await context.Orders.ToListAsync());
        Assert.Equal(3, (await context.OrderItems.SingleAsync()).Quantity);
    }

    [Fact]
    public async Task ParcelPersistsAndBillsWithoutChangingDiningTable()
    {
        using var db = new TestDatabase();
        var ids = await db.SeedCustomerAsync();
        var orders = db.Services.GetRequiredService<OrderService>();
        var saved = await orders.SaveParcelOrderAsync(null, "  Guest  ",
            new[] { new OrderSaveItem { MenuItemId = ids.MenuId, Quantity = 1, Price = 19.99m } });
        var edited = await orders.SaveParcelOrderAsync(saved.Id, "Guest",
            new[] { new OrderSaveItem { MenuItemId = ids.MenuId, Quantity = 2, Price = 19.99m } });
        Assert.Equal(saved.Id, edited.Id);
        Assert.Equal("Guest", edited.CustomerName);
        var billing = db.Services.GetRequiredService<BillingService>();
        await billing.ConfirmPaymentAsync(saved.Id, 0, PaymentMethod.Cash);
        var receipt = billing.CreateReceiptData(await billing.GetBillingOrderAsync(saved.Id));
        Assert.Equal("Parcel - Guest", receipt.TableNumber);
        using var context = db.Context();
        Assert.Equal(TableStatus.Available, (await context.Tables.SingleAsync()).Status);
    }

    [Fact]
    public async Task MissingMenuForeignKeyRollsBackOrderAndTableStatus()
    {
        using var db = new TestDatabase();
        var ids = await db.SeedCustomerAsync();
        await Assert.ThrowsAsync<DbUpdateException>(() => db.Services.GetRequiredService<OrderService>().SaveTableOrderAsync(ids.TableId,
            new[] { new OrderSaveItem { MenuItemId = int.MaxValue, Quantity = 1, Price = 10 } }));
        using var context = db.Context();
        Assert.Empty(await context.Orders.ToListAsync());
        Assert.Empty(await context.Bills.ToListAsync());
        Assert.Equal(TableStatus.Available, (await context.Tables.SingleAsync()).Status);
    }

    [Fact]
    public async Task ReferencedMenuAndHistoricalTableCannotBeDeleted()
    {
        using var db = new TestDatabase();
        var ids = await db.SeedCustomerAsync();
        await db.Services.GetRequiredService<OrderService>().SaveTableOrderAsync(ids.TableId,
            new[] { new OrderSaveItem { MenuItemId = ids.MenuId, Quantity = 1, Price = 19.99m } });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.Services.GetRequiredService<MenuService>().DeleteMenuItemAsync(ids.MenuId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Services.GetRequiredService<TableService>().DeleteTableAsync(ids.TableId));
    }
    [Theory]
    [InlineData(OrderStatus.Paid)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task TerminalOrderCannotBeChargedOrRecreatedByStaleParcelSave(OrderStatus status)
    {
        using var db = new TestDatabase();
        var ids = await db.SeedCustomerAsync();
        var orders = db.Services.GetRequiredService<OrderService>();
        var items = new[] { new OrderSaveItem { MenuItemId = ids.MenuId, Quantity = 1, Price = 19.99m } };
        var saved = await orders.SaveParcelOrderAsync(null, "Guest", items);
        using (var context = db.Context())
        {
            var order = await context.Orders.SingleAsync();
            order.Status = status;
            await context.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Services.GetRequiredService<BillingService>()
            .ConfirmPaymentAsync(saved.Id, 0, PaymentMethod.Cash));
        await Assert.ThrowsAsync<InvalidOperationException>(() => orders.SaveParcelOrderAsync(saved.Id, "Guest", items));
        using var check = db.Context();
        Assert.Equal(status, (await check.Orders.SingleAsync()).Status);
        Assert.Null((await check.Bills.SingleAsync()).PaidAt);
    }

    [Fact]
    public async Task InvalidPaymentMethodCannotBePersisted()
    {
        using var db = new TestDatabase();
        var ids = await db.SeedCustomerAsync();
        var saved = await db.Services.GetRequiredService<OrderService>().SaveParcelOrderAsync(null, "Guest",
            new[] { new OrderSaveItem { MenuItemId = ids.MenuId, Quantity = 1, Price = 19.99m } });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Services.GetRequiredService<BillingService>()
            .ConfirmPaymentAsync(saved.Id, 0, (PaymentMethod)999));
        using var check = db.Context();
        Assert.Null((await check.Bills.SingleAsync()).PaidAt);
    }

}
