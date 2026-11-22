using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ServeX.Data;
using ServeX.Models;

namespace ServeX.Services
{
    public sealed record KitchenLine(int ItemId, string Name, int Quantity);
    public sealed record KitchenTicket(int OrderId, int Revision, string Reference, DateTime CreatedLocal, IReadOnlyList<KitchenLine> Lines);

    public sealed class KitchenService
    {
        private readonly IDbContextFactory<ServeXDbContext> _contexts;
        public KitchenService(IDbContextFactory<ServeXDbContext> contexts) { _contexts = contexts; }

        public async Task<KitchenTicket> GetTicketAsync(int orderId, int revision)
        {
            using var context = _contexts.CreateDbContext();
            var order = await ReadOpenAsync(context, orderId, revision);
            var reference = order.Type == OrderType.Table
                ? (await context.Tables.FindAsync(order.ReferenceId))?.TableNumber ?? $"Table #{order.ReferenceId}"
                : order.Type == OrderType.Room
                    ? (await context.Rooms.FindAsync(order.ReferenceId))?.RoomNumber ?? $"Room #{order.ReferenceId}"
                    : BillingService.GetParcelLabel(order.CustomerName);
            var lines = order.Items.Where(line => line.Quantity > line.KitchenSentQuantity)
                .Select(line => new KitchenLine(line.Id, line.MenuItem.Name, line.Quantity - line.KitchenSentQuantity)).ToArray();
            return new KitchenTicket(orderId, revision, reference, DateTime.Now, lines);
        }

        public async Task<int> MarkTicketSentAsync(KitchenTicket ticket)
        {
            using var context = _contexts.CreateDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();
            var order = await ReadOpenAsync(context, ticket.OrderId, ticket.Revision);
            if (ticket.Lines.Count == 0) return order.Revision;
            foreach (var line in order.Items) line.KitchenSentQuantity = line.Quantity;
            order.Status = OrderStatus.Preparing;
            order.Revision++;
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            return order.Revision;
        }

        public async Task<int> MarkServedAsync(int orderId, int revision)
        {
            using var context = _contexts.CreateDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();
            var order = await ReadOpenAsync(context, orderId, revision);
            if (order.Status != OrderStatus.Preparing || order.Items.Any(line => line.Quantity > line.KitchenSentQuantity))
                throw new InvalidOperationException("Send outstanding items to the kitchen before marking this order served.");

            order.Status = OrderStatus.Served;
            order.Revision++;
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            return order.Revision;
        }

        private static async Task<Order> ReadOpenAsync(ServeXDbContext context, int orderId, int revision)
        {
            var order = await context.Orders.Include(order => order.Items).ThenInclude(line => line.MenuItem)
                .SingleOrDefaultAsync(order => order.Id == orderId);
            if (order == null || order.Revision != revision ||
                order.Status is not (OrderStatus.Open or OrderStatus.Preparing or OrderStatus.Served))
                throw new InvalidOperationException("The order changed. Reopen it and try again.");
            return order;
        }
    }
}
