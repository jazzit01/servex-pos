using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServeX.Models;
using ServeX.Services;
using Xunit;

namespace ServeX.Tests;

public class OrderBillingTests
{
    [Fact]
    public async Task UnnamedParcelRemainsUnnamedInStorageAndReceipts()
    {
        using var db = new TestDatabase();
        var ids = await db.SeedCustomerAsync();
        var orders = db.Services.GetRequiredService<OrderService>();
        var order = await orders.SaveParcelOrderAsync(null, "  ",
            new[] { new OrderSaveItem { MenuItemId = ids.MenuId, Quantity = 1 } });
        Assert.Null((await orders.GetOrderAsync(order.Id)).CustomerName);
        var billing = db.Services.GetRequiredService<BillingService>();
        await billing.ConfirmPaymentAsync(order.Id, 0, PaymentMethod.Cash);
        Assert.Equal("Parcel", billing.CreateReceiptData(await billing.GetBillingOrderAsync(order.Id)).TableNumber);
    }

    [Fact]
    public async Task MenuRejectsDuplicateNamesInTheSameCategoryAndPricesThatRoundToZero()
    {
        using var db = new TestDatabase();
        await db.Initialize();
        var menu = db.Services.GetRequiredService<MenuService>();
        var first = await menu.AddMenuItemAsync(new MenuItem { Name = " Café ", Category = " Drinks ", Price = 15 });
        var second = await menu.AddMenuItemAsync(new MenuItem { Name = "CAFÉ", Category = "Desserts", Price = 20 });

        await Assert.ThrowsAsync<MenuValidationException>(() => menu.AddMenuItemAsync(
            new MenuItem { Name = "café", Category = "DRINKS", Price = 15 }));
        second.Category = "DRINKS";
        await Assert.ThrowsAsync<MenuValidationException>(() => menu.UpdateMenuItemAsync(second));
        first.Price = 0.001m;
        await Assert.ThrowsAsync<MenuValidationException>(() => menu.UpdateMenuItemAsync(first));

        first.Price = 16;
        await menu.UpdateMenuItemAsync(first);
        var saved = await menu.GetMenuItemsAsync();
        Assert.Equal(16m, saved.Single(item => item.Id == first.Id).Price);
        Assert.Equal("Desserts", saved.Single(item => item.Id == second.Id).Category);
    }

    [Fact]
    public async Task CancellationPreservesHistoryReleasesTheTableAndBlocksPayment()
    {
        using var db = new TestDatabase();
        var ids = await db.SeedCustomerAsync();
        var orders = db.Services.GetRequiredService<OrderService>();
        var items = new[] { new OrderSaveItem { MenuItemId = ids.MenuId, Quantity = 1, Price = 19.99m } };
        var order = await orders.SaveTableOrderAsync(ids.TableId, items);
        await orders.CancelOrderAsync(order.Id);
        var cancelled = await orders.GetOrderAsync(order.Id);

        Assert.Equal(OrderStatus.Cancelled, cancelled.Status);
        Assert.Single(cancelled.Items);
        Assert.Equal(TableStatus.Available, (await orders.GetTableAsync(ids.TableId)).Status);
        Assert.Null(await orders.GetActiveOrderForTableAsync(ids.TableId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => orders.SaveTableOrderAsync(ids.TableId, items, order.Id));
        var billing = db.Services.GetRequiredService<BillingService>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => billing.ConfirmPaymentAsync(order.Id, 0, PaymentMethod.Cash));

        var parcel = await orders.SaveParcelOrderAsync(null, "Customer", items);
        await billing.ConfirmPaymentAsync(parcel.Id, 0, PaymentMethod.Cash);
        await Assert.ThrowsAsync<InvalidOperationException>(() => orders.CancelOrderAsync(parcel.Id));
    }

    [Fact]
    public async Task StaleTableAndParcelEditsCannotOverwriteTheLatestRevision()
    {
        using var db = new TestDatabase();
        var ids = await db.SeedCustomerAsync();
        var orders = db.Services.GetRequiredService<OrderService>();
        var items = new[] { new OrderSaveItem { MenuItemId = ids.MenuId, Quantity = 1 } };
        var table = await orders.SaveTableOrderAsync(ids.TableId, items, null, 0);
        var parcel = await orders.SaveParcelOrderAsync(null, "Test", items, 0);
        var tableLineId = Assert.Single(table.Items).Id;
        items[0].Quantity = 2;
        var edited = await orders.SaveTableOrderAsync(ids.TableId, items, table.Id, table.Revision);
        await orders.SaveParcelOrderAsync(parcel.Id, "Test", items, parcel.Revision);

        Assert.Equal(tableLineId, Assert.Single(edited.Items).Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => orders.SaveTableOrderAsync(ids.TableId, items, table.Id, table.Revision));
        await Assert.ThrowsAsync<InvalidOperationException>(() => orders.SaveParcelOrderAsync(parcel.Id, "Test", items, parcel.Revision));
    }

    [Fact]
    public async Task ReservationsRequireConfirmationAndPaidTablesRequireCleaning()
    {
        using var db = new TestDatabase();
        var ids = await db.SeedCustomerAsync();
        var tables = db.Services.GetRequiredService<TableService>();
        var orders = db.Services.GetRequiredService<OrderService>();
        var billing = db.Services.GetRequiredService<BillingService>();
        var items = new[] { new OrderSaveItem { MenuItemId = ids.MenuId, Quantity = 1 } };
        await tables.ChangeFloorStatusAsync(ids.TableId, TableStatus.Available, TableStatus.Reserved);
        await Assert.ThrowsAsync<InvalidOperationException>(() => orders.SaveTableOrderAsync(ids.TableId, items));
        var order = await orders.SaveTableOrderAsync(ids.TableId, items, allowReserved: true);
        await billing.ConfirmPaymentAsync(order.Id, 0, PaymentMethod.Cash);
        await Assert.ThrowsAsync<InvalidOperationException>(() => orders.SaveTableOrderAsync(ids.TableId, items));
        await tables.ChangeFloorStatusAsync(ids.TableId, TableStatus.Cleaning, TableStatus.Available);
        Assert.Equal(TableStatus.Available, (await orders.GetTableAsync(ids.TableId)).Status);
    }

    [Fact]
    public async Task KitchenTicketsIncludeOnlyNewQuantitiesAndHistoryReportsRecordedPayments()
    {
        using var db = new TestDatabase();
        var ids = await db.SeedCustomerAsync();
        var orders = db.Services.GetRequiredService<OrderService>();
        var kitchen = db.Services.GetRequiredService<KitchenService>();
        var items = new[] { new OrderSaveItem { MenuItemId = ids.MenuId, Quantity = 2 } };
        var order = await orders.SaveTableOrderAsync(ids.TableId, items, null, 0);
        var first = await kitchen.GetTicketAsync(order.Id, order.Revision);
        Assert.Equal(2, Assert.Single(first.Lines).Quantity);
        var revision = await kitchen.MarkTicketSentAsync(first);
        Assert.Empty((await kitchen.GetTicketAsync(order.Id, revision)).Lines);

        items[0].Quantity = 3;
        var saved = await orders.SaveTableOrderAsync(ids.TableId, items, order.Id, revision);
        var second = await kitchen.GetTicketAsync(saved.Id, saved.Revision);
        Assert.Equal(1, Assert.Single(second.Lines).Quantity);
        revision = await kitchen.MarkTicketSentAsync(second);
        await kitchen.MarkServedAsync(order.Id, revision);
        Assert.Equal(OrderStatus.Served, (await orders.GetOrderAsync(order.Id)).Status);

        var history = db.Services.GetRequiredService<OrderHistoryService>();
        Assert.Single(await history.GetOrdersAsync(DateTime.Today, DateTime.Today, unpaidOnly: true));
        var billing = db.Services.GetRequiredService<BillingService>();
        var paid = await billing.ConfirmPaymentAsync(order.Id, 1m, PaymentMethod.Upi, " abc-123 ");
        Assert.Equal("abc-123", billing.CreateReceiptData(await billing.GetBillingOrderAsync(order.Id)).PaymentReference);
        Assert.Empty(await history.GetOrdersAsync(DateTime.Today, DateTime.Today, unpaidOnly: true));
        var report = await history.GetReportAsync(DateTime.Today, DateTime.Today, "Summary");
        Assert.Equal(paid.Total, report.Single(row => row.Label == "Upi").Amount);
        Assert.Equal(paid.Tax, report.Single(row => row.Label == "Tax collected").Amount);
    }

    [Fact]
    public async Task RoomServiceBillsToRoomAndReturnsItToAvailable()
    {
        using var db = new TestDatabase();
        var ids = await db.SeedCustomerAsync();
        var rooms = db.Services.GetRequiredService<RoomService>();
        await rooms.SaveAsync(null, "203", RoomStatus.Available);
        var room = (await rooms.ListAsync()).Single(item => item.RoomNumber == "203");
        var orders = db.Services.GetRequiredService<OrderService>();
        var order = await orders.SaveTableOrderAsync(room.Id,
            new[] { new OrderSaveItem { MenuItemId = ids.MenuId, Quantity = 1 } }, locationType: OrderType.Room);
        Assert.Equal(OrderType.Room, order.Type);
        Assert.Equal(RoomStatus.Occupied, (await rooms.ListAsync()).Single(item => item.Id == room.Id).Status);
        var billing = db.Services.GetRequiredService<BillingService>();
        await billing.ConfirmPaymentAsync(order.Id, 0m, PaymentMethod.Cash);
        Assert.Equal("203", billing.CreateReceiptData(await billing.GetBillingOrderAsync(order.Id)).TableNumber);
        Assert.Equal(RoomStatus.Available, (await rooms.ListAsync()).Single(item => item.Id == room.Id).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rooms.DeleteAsync(room.Id));
        var unused = (await rooms.ListAsync()).Single(item => item.Id != room.Id);
        await rooms.DeleteAsync(unused.Id);
        Assert.DoesNotContain(await rooms.ListAsync(), item => item.Id == unused.Id);
    }

    [Fact]
    public async Task TableOrdersCanMoveThenMergeWithoutDuplicatingPaidSales()
    {
        using var db = new TestDatabase();
        var ids = await db.SeedCustomerAsync();
        var tables = db.Services.GetRequiredService<TableService>();
        var second = await tables.AddTableAsync(new Table { TableNumber = "Second" });
        var third = await tables.AddTableAsync(new Table { TableNumber = "Third" });
        var orders = db.Services.GetRequiredService<OrderService>();
        var items = new[] { new OrderSaveItem { MenuItemId = ids.MenuId, Quantity = 1 } };
        var original = await orders.SaveTableOrderAsync(ids.TableId, items);
        await orders.TransferTableOrderAsync(original.Id, original.Revision, second.Id, merge: false);
        var moved = await orders.GetActiveOrderForTableAsync(second.Id);
        Assert.Equal(original.Id, moved.Id);
        var target = await orders.SaveTableOrderAsync(third.Id, items);
        await orders.TransferTableOrderAsync(moved.Id, moved.Revision, third.Id, merge: true);
        var merged = await orders.GetActiveOrderForTableAsync(third.Id);
        Assert.Equal(target.Id, merged.Id);
        Assert.Equal(2, Assert.Single(merged.Items).Quantity);
        Assert.Equal(OrderStatus.Merged, (await orders.GetOrderAsync(moved.Id)).Status);
        Assert.Equal(TableStatus.Available, (await orders.GetTableAsync(second.Id)).Status);
    }

    [Fact]
    public async Task CheckoutKeepsTaxExemptionReceiptProfileManagerDiscountAndSplitPayments()
    {
        using var db = new TestDatabase();
        var ids = await db.SeedCustomerAsync();
        await db.Initialize();
        var access = db.Services.GetRequiredService<ApplicationAccessService>();
        db.Services.GetRequiredService<OperatorSession>().ActiveUser =
            await access.ValidateLoginAsync("existing-admin", "test-only-password");
        var settings = db.Services.GetRequiredService<ApplicationSettingsService>();
        await settings.UpdateSettingsAsync("Test", "123", 10m, "Address", new ReceiptProfile("en-GB", "VAT", "TX-9", 58));
        var menu = db.Services.GetRequiredService<MenuService>();
        var exempt = await menu.AddMenuItemAsync(new MenuItem { Name = "Exempt", Category = "Food", Price = 20m, IsAvailable = true, TaxExempt = true });
        var orders = db.Services.GetRequiredService<OrderService>();
        var order = await orders.SaveParcelOrderAsync(null, "Test", new[]
        {
            new OrderSaveItem { MenuItemId = ids.MenuId, Quantity = 1 },
            new OrderSaveItem { MenuItemId = exempt.Id, Quantity = 1 }
        });
        var billing = db.Services.GetRequiredService<BillingService>();
        var split = new PaymentSplit(10m, PaymentMethod.Upi, "upi-ref");
        await Assert.ThrowsAsync<InvalidOperationException>(() => billing.ConfirmPaymentAsync(order.Id, 25m, PaymentMethod.Cash,
            split: split, discountOptions: new DiscountOptions(true, "Promotion", "wrong")));
        var bill = await billing.ConfirmPaymentAsync(order.Id, 25m, PaymentMethod.Cash, split: split,
            discountOptions: new DiscountOptions(true, "Promotion", "test-only-password"));
        var receipt = billing.CreateReceiptData(await billing.GetBillingOrderAsync(order.Id));
        Assert.Equal(2m, bill.Tax);
        Assert.Equal(10.50m, bill.Discount);
        Assert.Equal(31.49m, bill.Total);
        Assert.Equal("VAT", receipt.TaxName);
        Assert.Equal("en-GB", receipt.Locale);
        Assert.Equal("TX-9", receipt.TaxNumber);
        Assert.Equal(58, receipt.PaperWidth);
        Assert.Equal(new[] { 10m, 21.49m }, receipt.Payments.Select(payment => payment.Amount));
    }

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
        var menu = db.Services.GetRequiredService<MenuService>();
        var catalog = Assert.Single(await menu.GetMenuItemsAsync());
        catalog.Price = (decimal)price;
        await menu.UpdateMenuItemAsync(catalog);
        var order = await db.Services.GetRequiredService<OrderService>().SaveTableOrderAsync(ids.TableId,
            new[] { new OrderSaveItem { MenuItemId = ids.MenuId, Quantity = quantity, Price = 999m } });
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
        Assert.Equal(OrderStatus.Paid, (await context.Orders.SingleAsync()).Status);
        Assert.Equal(TableStatus.Cleaning, (await context.Tables.SingleAsync()).Status);
        Assert.Equal(40m, (await context.Bills.SingleAsync()).Total);
        await Assert.ThrowsAsync<InvalidOperationException>(() => billing.ConfirmPaymentAsync(saved.Id, 0, method));
        Assert.Single(await context.Bills.ToListAsync());
        await db.Services.GetRequiredService<ApplicationSettingsService>()
            .UpdateSettingsAsync("Updated Restaurant", "555", 5m, " 12 Main Road ");
        var receipt = billing.CreateReceiptData(await billing.GetBillingOrderAsync(saved.Id));
        Assert.Equal("Updated Restaurant", receipt.RestaurantName);
        Assert.Equal("12 Main Road", receipt.RestaurantAddress);
        Assert.Equal("555", receipt.RestaurantPhone);
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
    public async Task MissingCatalogItemRollsBackOrderAndTableStatus()
    {
        using var db = new TestDatabase();
        var ids = await db.SeedCustomerAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Services.GetRequiredService<OrderService>().SaveTableOrderAsync(ids.TableId,
            new[] { new OrderSaveItem { MenuItemId = int.MaxValue, Quantity = 1, Price = 10 } }));
        using var context = db.Context();
        Assert.Empty(await context.Orders.ToListAsync());
        Assert.Empty(await context.Bills.ToListAsync());
        Assert.Equal(TableStatus.Available, (await context.Tables.SingleAsync()).Status);
    }

    [Fact]
    public async Task ArchivedMenuStaysInHistoryAndHistoricalTableCannotBeDeleted()
    {
        using var db = new TestDatabase();
        var ids = await db.SeedCustomerAsync();
        await db.Services.GetRequiredService<OrderService>().SaveTableOrderAsync(ids.TableId,
            new[] { new OrderSaveItem { MenuItemId = ids.MenuId, Quantity = 1, Price = 19.99m } });
        var menu = db.Services.GetRequiredService<MenuService>();
        var orders = db.Services.GetRequiredService<OrderService>();
        await menu.ArchiveMenuItemAsync(ids.MenuId);
        Assert.Empty(await menu.GetMenuItemsAsync());
        Assert.Empty(await orders.GetMenuItemsAsync());
        Assert.Equal("Customer Item", Assert.Single((await orders.GetActiveOrderForTableAsync(ids.TableId)).Items).MenuItem.Name);
        await Assert.ThrowsAsync<InvalidOperationException>(() => orders.SaveTableOrderAsync(ids.TableId,
            new[] { new OrderSaveItem { MenuItemId = ids.MenuId, Quantity = 2 } }));
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
