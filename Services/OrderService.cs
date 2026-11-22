using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ServeX.Data;
using ServeX.Models;

namespace ServeX.Services
{
    public class OrderService
    {
        private readonly IDbContextFactory<ServeXDbContext> _contexts;
        public OrderService(IDbContextFactory<ServeXDbContext> contexts, LogService logService)
        {
            _contexts = contexts;
            _logService = logService;
        }

        private readonly LogService _logService;

        public async Task<Table> GetTableAsync(int tableId)
        {
            try
            {
                using (var context = _contexts.CreateDbContext())
                {
                    return await context.Tables
                        .AsNoTracking()
                        .FirstOrDefaultAsync(table => table.Id == tableId);
                }
            }
            catch (Exception exception)
            {
                _logService.LogError(string.Format("Table lookup failed for table id {0}", tableId), exception);
                throw;
            }
        }

        public async Task<List<MenuItem>> GetMenuItemsAsync()
        {
            try
            {
                using (var context = _contexts.CreateDbContext())
                {
                    return await context.MenuItems
                        .AsNoTracking()
                        .OrderBy(menuItem => menuItem.Category)
                        .ThenBy(menuItem => menuItem.Name)
                        .ToListAsync();
                }
            }
            catch (Exception exception)
            {
                _logService.LogError("Menu item load failed for order screen", exception);
                throw;
            }
        }

        public async Task<Order> GetActiveOrderForTableAsync(int tableId)
        {
            try
            {
                using (var context = _contexts.CreateDbContext())
                {
                    return await context.Orders
                        .AsNoTracking()
                        .Include(order => order.Items)
                        .ThenInclude(orderItem => orderItem.MenuItem)
                        .Include(order => order.Bill)
                        .Where(order =>
                            order.Type == OrderType.Table &&
                            order.ReferenceId == tableId &&
                            order.Status != OrderStatus.Billed &&
                            order.Status != OrderStatus.Paid &&
                            order.Status != OrderStatus.Cancelled)
                        .OrderByDescending(order => order.CreatedAt)
                        .FirstOrDefaultAsync();
                }
            }
            catch (Exception exception)
            {
                _logService.LogError(string.Format("Active order lookup failed for table id {0}", tableId), exception);
                throw;
            }
        }

        public async Task<Order> GetOrderAsync(int orderId)
        {
            using (var context = _contexts.CreateDbContext())
            {
                return await context.Orders
                    .AsNoTracking()
                    .Include(order => order.Items)
                    .ThenInclude(orderItem => orderItem.MenuItem)
                    .Include(order => order.Bill)
                    .FirstOrDefaultAsync(order => order.Id == orderId);
            }
        }

        public async Task<decimal> GetTaxPercentageAsync()
        {
            using (var context = _contexts.CreateDbContext())
            {
                return await OrderPricing.GetTaxPercentageAsync(context);
            }
        }

        public async Task CancelOrderAsync(int orderId)
        {
            using var context = _contexts.CreateDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();
            var order = await context.Orders.Include(item => item.Bill).SingleOrDefaultAsync(item => item.Id == orderId);
            if (order == null || order.Status is not (OrderStatus.Open or OrderStatus.Preparing or OrderStatus.Served) ||
                order.Bill?.PaidAt != null)
                throw new InvalidOperationException("Only an open, unpaid order can be cancelled.");

            order.Status = OrderStatus.Cancelled;
            if (order.Type == OrderType.Table)
            {
                var otherOpenOrder = await context.Orders.AnyAsync(item => item.Id != orderId &&
                    item.Type == OrderType.Table && item.ReferenceId == order.ReferenceId &&
                    (item.Status == OrderStatus.Open || item.Status == OrderStatus.Preparing || item.Status == OrderStatus.Served));
                if (!otherOpenOrder)
                {
                    var table = await context.Tables.FindAsync(order.ReferenceId);
                    if (table?.Status == TableStatus.Occupied) table.Status = TableStatus.Available;
                }
            }

            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            _logService.LogInfo($"Order #{orderId} cancelled");
        }

        public async Task<Order> SaveTableOrderAsync(int tableId, IEnumerable<OrderSaveItem> items, int? expectedOrderId = null)
        {
            var orderItems = items?
                .Where(item => item.Quantity > 0)
                .ToList() ?? new List<OrderSaveItem>();

            if (!orderItems.Any())
            {
                throw new InvalidOperationException("Add at least one item before saving the order.");
            }

            try
            {
                using (var context = _contexts.CreateDbContext())
                {
                    using var transaction = await context.Database.BeginTransactionAsync();
                    var table = await context.Tables.FirstOrDefaultAsync(item => item.Id == tableId);
                    if (table == null)
                    {
                        throw new InvalidOperationException("The selected table could not be found.");
                    }

                    var order = await context.Orders
                        .Include(item => item.Items)
                        .Include(item => item.Bill)
                        .Where(item =>
                            item.Type == OrderType.Table &&
                            item.ReferenceId == tableId &&
                            item.Status != OrderStatus.Billed &&
                            item.Status != OrderStatus.Paid &&
                            item.Status != OrderStatus.Cancelled)
                        .OrderByDescending(item => item.CreatedAt)
                        .FirstOrDefaultAsync();

                    if (expectedOrderId.HasValue && order?.Id != expectedOrderId)
                        throw new InvalidOperationException("The table order changed. Reopen it before saving.");

                    if (order == null)
                    {
                        order = new Order
                        {
                            Type = OrderType.Table,
                            ReferenceId = tableId,
                            Status = OrderStatus.Open,
                            CreatedAt = DateTime.UtcNow
                        };

                        context.Orders.Add(order);
                    }

                    if (order.Items.Any())
                    {
                        context.OrderItems.RemoveRange(order.Items);
                    }

                    order.Items = new List<OrderItem>();
                    foreach (var item in orderItems)
                    {
                        order.Items.Add(new OrderItem
                        {
                            MenuItemId = item.MenuItemId,
                            Quantity = item.Quantity,
                            Price = OrderPricing.Round(item.Price)
                        });
                    }

                    var taxPercentage = await OrderPricing.GetTaxPercentageAsync(context);
                    var subtotal = OrderPricing.Round(orderItems.Sum(item => item.Price * item.Quantity));
                    var tax = OrderPricing.Tax(subtotal, taxPercentage);
                    var total = subtotal + tax;

                    if (order.Bill == null)
                    {
                        order.Bill = new Bill();
                    }

                    order.Bill.Subtotal = subtotal;
                    order.Bill.Tax = tax;
                    order.Bill.Discount = 0m;
                    order.Bill.Total = total;

                    table.Status = TableStatus.Occupied;

                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    _logService.LogInfo(string.Format("Order saved for table {0} with {1} items", table.TableNumber, orderItems.Count));
                    return order;
                }
            }
            catch (Exception exception)
            {
                _logService.LogError(string.Format("Order save failed for table id {0}", tableId), exception);
                throw;
            }
        }

        public async Task<Order> SaveParcelOrderAsync(int? orderId, string customerName, IEnumerable<OrderSaveItem> items)
        {
            var orderItems = items?
                .Where(item => item.Quantity > 0)
                .ToList() ?? new List<OrderSaveItem>();

            if (!orderItems.Any())
            {
                throw new InvalidOperationException("Add at least one item before saving the parcel order.");
            }

            var normalizedCustomerName = string.IsNullOrWhiteSpace(customerName)
                ? "Atithi"
                : customerName.Trim();

            try
            {
                using (var context = _contexts.CreateDbContext())
                {
                    using var transaction = await context.Database.BeginTransactionAsync();
                    Order order = null;
                    if (orderId.HasValue)
                    {
                        order = await context.Orders
                            .Include(item => item.Items)
                            .Include(item => item.Bill)
                            .FirstOrDefaultAsync(item =>
                                item.Id == orderId.Value &&
                                item.Type == OrderType.Parcel &&
                                item.Status != OrderStatus.Billed &&
                                item.Status != OrderStatus.Paid &&
                                item.Status != OrderStatus.Cancelled);
                        if (order == null)
                            throw new InvalidOperationException("The parcel order is no longer editable. Refresh before starting a new order.");
                    }

                    if (order == null)
                    {
                        order = new Order
                        {
                            Type = OrderType.Parcel,
                            ReferenceId = 0,
                            CustomerName = normalizedCustomerName,
                            Status = OrderStatus.Open,
                            CreatedAt = DateTime.UtcNow
                        };

                        context.Orders.Add(order);
                    }
                    else
                    {
                        order.CustomerName = normalizedCustomerName;
                    }

                    if (order.Items.Any())
                    {
                        context.OrderItems.RemoveRange(order.Items);
                    }

                    order.Items = new List<OrderItem>();
                    foreach (var item in orderItems)
                    {
                        order.Items.Add(new OrderItem
                        {
                            MenuItemId = item.MenuItemId,
                            Quantity = item.Quantity,
                            Price = OrderPricing.Round(item.Price)
                        });
                    }

                    var taxPercentage = await OrderPricing.GetTaxPercentageAsync(context);
                    var subtotal = OrderPricing.Round(orderItems.Sum(item => item.Price * item.Quantity));
                    var tax = OrderPricing.Tax(subtotal, taxPercentage);
                    var total = subtotal + tax;

                    if (order.Bill == null)
                    {
                        order.Bill = new Bill();
                    }

                    order.Bill.Subtotal = subtotal;
                    order.Bill.Tax = tax;
                    order.Bill.Discount = 0m;
                    order.Bill.Total = total;

                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    _logService.LogInfo(string.Format("Parcel order saved for customer {0} with {1} items", normalizedCustomerName, orderItems.Count));
                    return order;
                }
            }
            catch (Exception exception)
            {
                _logService.LogError("Parcel order save failed", exception);
                throw;
            }
        }

    }
}
