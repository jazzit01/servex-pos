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
                        .Where(item => !item.IsArchived)
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

        public async Task<Table> GetOrderLocationAsync(int id, OrderType locationType)
        {
            if (locationType == OrderType.Table) return await GetTableAsync(id);
            using var context = _contexts.CreateDbContext();
            var room = await context.Rooms.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id);
            return room == null ? null : new Table
            {
                Id = room.Id, TableNumber = room.RoomNumber,
                Status = room.Status switch
                {
                    RoomStatus.Reserved => TableStatus.Reserved,
                    RoomStatus.Occupied => TableStatus.Occupied,
                    RoomStatus.Maintenance => TableStatus.Cleaning,
                    _ => TableStatus.Available
                }
            };
        }

        public async Task<Order> GetActiveOrderForTableAsync(int tableId, OrderType locationType = OrderType.Table)
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
                            order.Type == locationType &&
                            order.ReferenceId == tableId &&
                            order.Status != OrderStatus.Billed &&
                            order.Status != OrderStatus.Paid &&
                            order.Status != OrderStatus.Cancelled &&
                            order.Status != OrderStatus.Merged)
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

        public async Task<Order> GetOpenParcelAsync(int orderId)
        {
            var order = await GetOrderAsync(orderId);
            return order?.Type == OrderType.Parcel && order.Bill?.PaidAt == null &&
                order.Status is OrderStatus.Open or OrderStatus.Preparing or OrderStatus.Served ? order : null;
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
            order.CancelledAt = DateTime.UtcNow;
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

            if (order.Type == OrderType.Room)
            {
                var room = await context.Rooms.FindAsync(order.ReferenceId);
                if (room?.Status == RoomStatus.Occupied) room.Status = RoomStatus.Available;
            }

            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            _logService.LogInfo($"Order #{orderId} cancelled");
        }

        public async Task TransferTableOrderAsync(int orderId, int revision, int destinationId, bool merge)
        {
            using var context = _contexts.CreateDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();
            var open = context.Orders.Where(order => order.Type == OrderType.Table &&
                (order.Status == OrderStatus.Open || order.Status == OrderStatus.Preparing || order.Status == OrderStatus.Served));
            var source = await open.Include(order => order.Items).SingleOrDefaultAsync(order => order.Id == orderId);
            if (source == null || source.Revision != revision || source.ReferenceId == destinationId)
                throw new InvalidOperationException("The source order changed. Reopen it first.");

            var from = await context.Tables.FindAsync(source.ReferenceId);
            var destination = await context.Tables.FindAsync(destinationId);
            if (from == null || destination == null || destination.Status is TableStatus.Reserved or TableStatus.Cleaning)
                throw new InvalidOperationException("Choose an available table or one with an active order to merge.");

            var target = await open.Include(order => order.Items).Include(order => order.Bill)
                .SingleOrDefaultAsync(order => order.ReferenceId == destinationId);
            if ((target != null) != merge)
                throw new InvalidOperationException("The destination changed. Refresh the dining floor and try again.");

            if (target == null)
            {
                source.ReferenceId = destinationId;
                source.Revision++;
            }
            else
            {
                foreach (var item in source.Items)
                {
                    var matching = target.Items.FirstOrDefault(line => line.MenuItemId == item.MenuItemId &&
                        line.Price == item.Price && line.TaxExempt == item.TaxExempt);
                    if (matching == null)
                    {
                        matching = new OrderItem { MenuItemId = item.MenuItemId, Price = item.Price, TaxExempt = item.TaxExempt };
                        target.Items.Add(matching);
                    }
                    matching.Quantity += item.Quantity;
                    matching.KitchenSentQuantity += item.KitchenSentQuantity;
                }

                var rate = await OrderPricing.GetTaxPercentageAsync(context);
                target.Bill ??= new Bill();
                target.Bill.Subtotal = OrderPricing.Round(target.Items.Sum(line => line.Price * line.Quantity));
                target.Bill.Tax = OrderPricing.Tax(target.Items.Where(line => !line.TaxExempt).Sum(line => line.Price * line.Quantity), rate);
                target.Bill.Total = target.Bill.Subtotal + target.Bill.Tax;
                target.Bill.Discount = 0m;
                target.Revision++;
                source.Status = OrderStatus.Merged;
            }

            from.Status = TableStatus.Available;
            destination.Status = TableStatus.Occupied;
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            _logService.LogInfo($"Order #{orderId} {(merge ? "merged into" : "moved to")} table {destination.TableNumber}.");
        }

        public async Task<Order> SaveTableOrderAsync(int tableId, IEnumerable<OrderSaveItem> items, int? expectedOrderId = null, int? expectedRevision = null, bool allowReserved = false, OrderType locationType = OrderType.Table)
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
                    if (locationType is not (OrderType.Table or OrderType.Room))
                        throw new InvalidOperationException("Choose a table or room.");
                    var table = locationType == OrderType.Table ? await context.Tables.FindAsync(tableId) : null;
                    var room = locationType == OrderType.Room ? await context.Rooms.FindAsync(tableId) : null;
                    if (table == null && room == null)
                        throw new InvalidOperationException("The selected location could not be found.");

                    var order = await context.Orders
                        .Include(item => item.Items)
                        .Include(item => item.Bill)
                        .Where(item =>
                            item.Type == locationType &&
                            item.ReferenceId == tableId &&
                            item.Status != OrderStatus.Billed &&
                            item.Status != OrderStatus.Paid &&
                            item.Status != OrderStatus.Cancelled &&
                            item.Status != OrderStatus.Merged)
                        .OrderByDescending(item => item.CreatedAt)
                        .FirstOrDefaultAsync();

                    if ((expectedOrderId.HasValue && order?.Id != expectedOrderId) ||
                        (expectedRevision.HasValue && (order?.Id != expectedOrderId || (order?.Revision ?? 0) != expectedRevision)))
                        throw new InvalidOperationException("The table order changed. Reopen it before saving.");

                    if (order == null)
                    {
                        if (table?.Status == TableStatus.Cleaning || room?.Status == RoomStatus.Maintenance)
                            throw new InvalidOperationException("Mark the table clean before starting a new order.");
                        if ((table?.Status == TableStatus.Reserved || room?.Status == RoomStatus.Reserved) && !allowReserved)
                            throw new InvalidOperationException("Confirm using the reserved table before starting a new order.");

                        order = new Order
                        {
                            Type = locationType,
                            ReferenceId = tableId,
                            Status = OrderStatus.Open,
                            CreatedAt = DateTime.UtcNow
                        };

                        context.Orders.Add(order);
                    }

                    await ApplyOrderItemsAsync(context, order, orderItems);
                    order.Revision++;

                    var taxPercentage = await OrderPricing.GetTaxPercentageAsync(context);
                    var subtotal = OrderPricing.Round(order.Items.Sum(item => item.Price * item.Quantity));
                    var tax = OrderPricing.Tax(order.Items.Where(item => !item.TaxExempt).Sum(item => item.Price * item.Quantity), taxPercentage);
                    var total = subtotal + tax;

                    if (order.Bill == null)
                    {
                        order.Bill = new Bill();
                    }

                    order.Bill.Subtotal = subtotal;
                    order.Bill.Tax = tax;
                    order.Bill.Discount = 0m;
                    order.Bill.Total = total;

                    if (table != null) table.Status = TableStatus.Occupied;
                    if (room != null) room.Status = RoomStatus.Occupied;

                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    _logService.LogInfo(string.Format("Order saved for table {0} with {1} items", table?.TableNumber ?? room.RoomNumber, orderItems.Count));
                    return order;
                }
            }
            catch (Exception exception)
            {
                _logService.LogError(string.Format("Order save failed for table id {0}", tableId), exception);
                throw;
            }
        }

        public async Task<Order> SaveParcelOrderAsync(int? orderId, string customerName, IEnumerable<OrderSaveItem> items, int? expectedRevision = null)
        {
            var orderItems = items?
                .Where(item => item.Quantity > 0)
                .ToList() ?? new List<OrderSaveItem>();

            if (!orderItems.Any())
            {
                throw new InvalidOperationException("Add at least one item before saving the parcel order.");
            }

            var normalizedCustomerName = string.IsNullOrWhiteSpace(customerName)
                ? null
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
                                item.Status != OrderStatus.Cancelled &&
                            item.Status != OrderStatus.Merged);
                        if (order == null || (expectedRevision.HasValue && order.Revision != expectedRevision))
                            throw new InvalidOperationException("The parcel order changed. Reopen it before saving.");
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

                    await ApplyOrderItemsAsync(context, order, orderItems);
                    order.Revision++;

                    var taxPercentage = await OrderPricing.GetTaxPercentageAsync(context);
                    var subtotal = OrderPricing.Round(order.Items.Sum(item => item.Price * item.Quantity));
                    var tax = OrderPricing.Tax(order.Items.Where(item => !item.TaxExempt).Sum(item => item.Price * item.Quantity), taxPercentage);
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

        private static async Task ApplyOrderItemsAsync(ServeXDbContext context, Order order, IEnumerable<OrderSaveItem> requested)
        {
            var quantities = requested.GroupBy(item => item.MenuItemId)
                .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity));
            var ids = quantities.Keys.ToArray();
            var menu = await context.MenuItems.Where(item => ids.Contains(item.Id)).ToDictionaryAsync(item => item.Id);
            var oldItems = order.Items.ToLookup(item => item.MenuItemId);

            foreach (var (menuId, quantity) in quantities)
            {
                if (!menu.TryGetValue(menuId, out var catalog))
                    throw new InvalidOperationException("A menu item no longer exists. Refresh the order.");
                if ((!catalog.IsAvailable || catalog.IsArchived) && quantity > oldItems[menuId].Sum(item => item.Quantity))
                    throw new InvalidOperationException("An unavailable menu item cannot be added or increased.");
            }

            var removed = order.Items.Where(item => !quantities.ContainsKey(item.MenuItemId)).ToArray();
            foreach (var item in removed)
            {
                order.Items.Remove(item);
                context.OrderItems.Remove(item);
            }

            foreach (var (menuId, quantity) in quantities)
            {
                var catalog = menu[menuId];
                var existing = oldItems[menuId].ToArray();
                var line = existing.FirstOrDefault();
                if (line == null)
                {
                    line = new OrderItem { MenuItemId = menuId };
                    order.Items.Add(line);
                }
                else
                {
                    foreach (var duplicate in existing.Skip(1))
                    {
                        order.Items.Remove(duplicate);
                        context.OrderItems.Remove(duplicate);
                    }
                }

                line.Quantity = quantity;
                line.KitchenSentQuantity = Math.Min(quantity, line.KitchenSentQuantity);
                if (catalog.IsAvailable && !catalog.IsArchived)
                {
                    line.Price = OrderPricing.Round(catalog.Price);
                    line.TaxExempt = catalog.TaxExempt;
                }
            }
        }

    }
}
