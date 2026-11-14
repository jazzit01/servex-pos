using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ServeX.Data;
using ServeX.Models;

namespace ServeX.Services
{
    public class BillingService
    {
        private readonly IDbContextFactory<ServeXDbContext> _contexts;
        public BillingService(IDbContextFactory<ServeXDbContext> contexts, LogService logService)
        {
            _contexts = contexts;
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
                    Table = table
                };
            }
        }

        public async Task<Bill> ConfirmPaymentAsync(int orderId, decimal discount, PaymentMethod paymentMethod)
        {
            try
            {
                using (var context = _contexts.CreateDbContext())
                {
                    var order = await context.Orders
                        .Include(item => item.Items)
                        .ThenInclude(item => item.MenuItem)
                        .Include(item => item.Bill)
                        .FirstOrDefaultAsync(item => item.Id == orderId);

                if (order == null)
                {
                    throw new InvalidOperationException("The order could not be found.");
                }

                    var orderItems = order.Items ?? new List<OrderItem>();

                    if (!orderItems.Any())
                    {
                        throw new InvalidOperationException("The order has no items to bill.");
                    }

                    if (order.Status == OrderStatus.Billed || order.Bill?.PaidAt != null)
                    {
                        throw new InvalidOperationException("This order has already been billed.");
                    }

                    var taxRate = await GetTaxRateAsync(context);
                    var subtotal = decimal.Round(orderItems.Select(item => item.Price * item.Quantity).DefaultIfEmpty(0m).Sum(), 2);
                    var tax = decimal.Round(subtotal * taxRate, 2);
                    var grossTotal = subtotal + tax;

                    if (discount < 0)
                    {
                        throw new InvalidOperationException("Discount cannot be negative.");
                    }

                    if (discount > grossTotal)
                    {
                        throw new InvalidOperationException("Discount cannot exceed the bill total.");
                    }

                    if (order.Bill == null)
                    {
                        order.Bill = new Bill();
                    }

                    order.Bill.Subtotal = subtotal;
                    order.Bill.Tax = tax;
                    order.Bill.Discount = decimal.Round(discount, 2);
                    order.Bill.Total = decimal.Round(grossTotal - discount, 2);
                    order.Bill.PaymentMethod = paymentMethod;
                    order.Bill.PaidAt = DateTime.UtcNow;

                    order.Status = OrderStatus.Billed;

                    if (order.Type == OrderType.Table)
                    {
                        var table = await context.Tables.FirstOrDefaultAsync(item => item.Id == order.ReferenceId);
                        if (table != null)
                        {
                            table.Status = TableStatus.Available;
                        }
                    }

                    await context.SaveChangesAsync();
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
            if (orderData?.Order?.Bill == null)
            {
                return null;
            }

            return new ReceiptData
            {
                OrderId = orderData.Order.Id,
                TableNumber = orderData.Order.Type == OrderType.Parcel
                    ? GetParcelLabel(orderData.Order.CustomerName)
                    : orderData.Table?.TableNumber ?? "Parcel",
                PaymentMethod = orderData.Order.Bill.PaymentMethod ?? PaymentMethod.Cash,
                PaidAt = orderData.Order.Bill.PaidAt ?? DateTime.UtcNow,
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
                return await GetTaxPercentageAsync(context);
            }
        }

        private static async Task<decimal> GetTaxPercentageAsync(ServeXDbContext context)
        {
            var configuredTax = await context.Settings
                .AsNoTracking()
                .Select(setting => (decimal?)setting.TaxPercentage)
                .FirstOrDefaultAsync();

            return configuredTax ?? 5m;
        }

        private static async Task<decimal> GetTaxRateAsync(ServeXDbContext context)
        {
            return await GetTaxPercentageAsync(context) / 100m;
        }
    }
}
