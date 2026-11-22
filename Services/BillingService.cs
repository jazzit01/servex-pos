using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ServeX.Data;
using ServeX.Models;

namespace ServeX.Services
{
    public class BillingService
    {
        private readonly IDbContextFactory<ServeXDbContext> _contexts;
        private readonly OperatorSession _session;
        public BillingService(IDbContextFactory<ServeXDbContext> contexts, LogService logService, OperatorSession session)
        {
            _contexts = contexts;
            _session = session;
            _logService = logService;
        }

        private readonly LogService _logService;

        public async Task<BillingOrderData> GetBillingOrderAsync(int orderId)
        {
            using (var context = _contexts.CreateDbContext())
            {
                var order = await context.Orders
                    .AsNoTracking()
                    .Include(item => item.Items)
                    .ThenInclude(item => item.MenuItem)
                    .Include(item => item.Bill)
                    .FirstOrDefaultAsync(item => item.Id == orderId);

                if (order == null)
                {
                    return null;
                }

                Table table = null;
                if (order.Type == OrderType.Table)
                {
                    table = await context.Tables
                        .AsNoTracking()
                        .FirstOrDefaultAsync(item => item.Id == order.ReferenceId);
                }

                return new BillingOrderData
                {
                    Order = order,
                    Table = table,
                    RoomNumber = order.Type == OrderType.Room
                        ? (await context.Rooms.FindAsync(order.ReferenceId))?.RoomNumber : null,
                    Restaurant = await context.Settings.AsNoTracking().OrderBy(setting => setting.Id).FirstOrDefaultAsync()
                };
            }
        }

        public async Task<Bill> ConfirmPaymentAsync(int orderId, decimal discount, PaymentMethod paymentMethod, string reference = null,
            PaymentSplit split = null, DiscountOptions discountOptions = null, int? expectedRevision = null)
        {
            if (discount != 0m) _session.RequireManager();
            reference = NormalizeReference(paymentMethod, reference);
            try
            {
                using (var context = _contexts.CreateDbContext())
                {
                    using var transaction = await context.Database.BeginTransactionAsync();
                    var order = await context.Orders
                        .Include(item => item.Items)
                        .ThenInclude(item => item.MenuItem)
                        .Include(item => item.Bill)
                        .FirstOrDefaultAsync(item => item.Id == orderId);

                if (order == null || (expectedRevision.HasValue && order.Revision != expectedRevision))
                {
                    throw new InvalidOperationException("The order changed. Reopen the bill before confirming payment.");
                }

                    var orderItems = order.Items ?? new List<OrderItem>();

                    if (!orderItems.Any())
                    {
                        throw new InvalidOperationException("The order has no items to bill.");
                    }

                    if (order.Status == OrderStatus.Billed || order.Status == OrderStatus.Paid || order.Bill?.PaidAt != null)
                    {
                        throw new InvalidOperationException("This order has already been billed.");
                    }

                    if (order.Status is OrderStatus.Cancelled or OrderStatus.Merged)
                        throw new InvalidOperationException("A cancelled or merged order cannot be billed.");
                    if (!Enum.IsDefined(paymentMethod))
                        throw new InvalidOperationException("Select a valid payment method.");

                    var settings = await context.Settings.AsNoTracking().OrderBy(setting => setting.Id).FirstOrDefaultAsync();
                    var taxPercentage = settings?.TaxPercentage ?? 5m;
                    var subtotal = OrderPricing.Round(orderItems.Sum(item => item.Price * item.Quantity));
                    var tax = OrderPricing.Tax(orderItems.Where(item => !item.TaxExempt).Sum(item => item.Price * item.Quantity), taxPercentage);
                    var grossTotal = subtotal + tax;

                    if (discount < 0 || (discountOptions?.IsPercentage == true && discount > 100))
                        throw new InvalidOperationException("Enter a valid discount.");
                    if (discountOptions?.IsPercentage == true)
                        discount = OrderPricing.Round(grossTotal * discount / 100m);
                    if (discount > grossTotal)
                        throw new InvalidOperationException("Discount cannot exceed the bill total.");

                    var reason = discountOptions?.Reason?.Trim();
                    if (reason?.Length > 120 || (discount > 0 && _session.ActiveUser != null && string.IsNullOrWhiteSpace(reason)))
                        throw new InvalidOperationException("Enter a discount reason up to 120 characters.");
                    if (grossTotal > 0 && discount > grossTotal * 0.20m && _session.ActiveUser != null)
                    {
                        var manager = await context.Users.FindAsync(_session.ActiveUser.Id);
                        if (manager?.Role != UserRole.Admin || !manager.IsPasswordHashed ||
                            !PasswordHash.Verify(discountOptions?.ManagerPassword ?? string.Empty, manager.Password))
                            throw new InvalidOperationException("Discounts above 20% require the signed-in administrator’s password.");
                    }

                    var finalAmount = OrderPricing.Round(grossTotal - discount);
                    var payments = CreatePayments(finalAmount, paymentMethod, reference, split);

                    if (order.Bill == null)
                    {
                        order.Bill = new Bill();
                    }

                    order.Bill.Subtotal = subtotal;
                    order.Bill.Tax = tax;
                    order.Bill.RecordedTaxRate = taxPercentage;
                    order.Bill.RecordedTaxName = settings?.TaxName ?? "Tax";
                    order.Bill.RecordedTaxNumber = settings?.TaxNumber;
                    order.Bill.RecordedLocale = settings?.Locale ?? "en-IN";
                    order.Bill.Discount = OrderPricing.Round(discount);
                    order.Bill.Total = finalAmount;
                    order.Bill.DiscountReason = reason;
                    order.Bill.PaymentBreakdown = split == null ? null : JsonSerializer.Serialize(payments);
                    order.Bill.PaymentMethod = paymentMethod;
                    order.Bill.PaymentReference = string.IsNullOrEmpty(reference) ? null : reference;
                    order.Bill.PaidAt = DateTime.UtcNow;

                    order.Status = OrderStatus.Paid;

                    if (order.Type == OrderType.Table)
                    {
                        var table = await context.Tables.FirstOrDefaultAsync(item => item.Id == order.ReferenceId);
                        if (table != null)
                        {
                            table.Status = TableStatus.Cleaning;
                        }
                    }

                    if (order.Type == OrderType.Room)
                    {
                        var room = await context.Rooms.FindAsync(order.ReferenceId);
                        if (room?.Status == RoomStatus.Occupied) room.Status = RoomStatus.Available;
                    }

                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    _logService.LogInfo(string.Format("Payment confirmed for order #{0} via {1}", order.Id, paymentMethod));
                    return order.Bill;
                }
            }
            catch (Exception exception)
            {
                _logService.LogError(string.Format("Billing failed for order #{0}", orderId), exception);
                throw;
            }
        }

        public ReceiptData CreateReceiptData(BillingOrderData orderData)
        {
            if (orderData?.Order?.Bill?.PaidAt == null)
            {
                return null;
            }

            return new ReceiptData
            {
                OrderId = orderData.Order.Id,
                RestaurantName = orderData.Restaurant?.RestaurantName ?? "ServeX",
                RestaurantAddress = orderData.Restaurant?.Address,
                RestaurantPhone = orderData.Restaurant?.Phone,
                TaxRate = orderData.Order.Bill.RecordedTaxRate,
                TaxName = orderData.Order.Bill.RecordedTaxName ?? "Tax",
                TaxNumber = orderData.Order.Bill.RecordedTaxNumber,
                Locale = orderData.Order.Bill.RecordedLocale ?? orderData.Restaurant?.Locale ?? "en-IN",
                PaperWidth = orderData.Restaurant?.ReceiptWidth ?? 80,
                TableNumber = orderData.Order.Type == OrderType.Parcel
                    ? GetParcelLabel(orderData.Order.CustomerName)
                    : orderData.Table?.TableNumber ?? orderData.RoomNumber ?? $"Room #{orderData.Order.ReferenceId}",
                PaymentMethod = orderData.Order.Bill.PaymentMethod ?? PaymentMethod.Cash,
                PaidAt = orderData.Order.Bill.PaidAt.Value,
                PaymentReference = orderData.Order.Bill.PaymentReference,
                Payments = ReadPayments(orderData.Order.Bill),
                DiscountReason = orderData.Order.Bill.DiscountReason,
                Subtotal = orderData.Order.Bill.Subtotal,
                Tax = orderData.Order.Bill.Tax,
                Discount = orderData.Order.Bill.Discount,
                Total = orderData.Order.Bill.Total,
                Items = (orderData.Order.Items ?? new List<OrderItem>()).Select(item => new ReceiptLineItem
                {
                    Name = item.MenuItem?.Name ?? "Item",
                    Quantity = item.Quantity,
                    UnitPrice = item.Price,
                    LineTotal = item.Price * item.Quantity
                }).ToList()
            };
        }

        public static IReadOnlyList<PaymentAllocation> ReadPayments(Bill bill)
        {
            if (!string.IsNullOrEmpty(bill.PaymentBreakdown))
                return JsonSerializer.Deserialize<List<PaymentAllocation>>(bill.PaymentBreakdown);
            return new[] { new PaymentAllocation(bill.Total, bill.PaymentMethod ?? PaymentMethod.Cash, bill.PaymentReference) };
        }

        private static IReadOnlyList<PaymentAllocation> CreatePayments(decimal total, PaymentMethod first,
            string reference, PaymentSplit split)
        {
            if (split == null) return new[] { new PaymentAllocation(total, first, reference) };
            var firstAmount = OrderPricing.Round(split.FirstAmount);
            if (!Enum.IsDefined(split.SecondMethod) || firstAmount <= 0 || firstAmount >= total)
                throw new InvalidOperationException("The first split amount must be greater than zero and less than the bill total.");

            return new[]
            {
                new PaymentAllocation(firstAmount, first, reference),
                new PaymentAllocation(total - firstAmount, split.SecondMethod, NormalizeReference(split.SecondMethod, split.SecondReference))
            };
        }

        private static string NormalizeReference(PaymentMethod method, string value)
        {
            var reference = method == PaymentMethod.Cash ? null : value?.Trim();
            if (reference?.Length > 80) throw new ArgumentException("Keep the payment reference to 80 characters.");
            return string.IsNullOrEmpty(reference) ? null : reference;
        }

        public static string GetParcelLabel(string customerName)
        {
            return string.IsNullOrWhiteSpace(customerName)
                ? "Parcel - Atithi"
                : "Parcel - " + customerName.Trim();
        }

        public async Task<decimal> GetTaxPercentageAsync()
        {
            using (var context = _contexts.CreateDbContext())
            {
                return await OrderPricing.GetTaxPercentageAsync(context);
            }
        }

    }
}
