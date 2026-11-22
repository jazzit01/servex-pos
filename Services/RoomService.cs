using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ServeX.Data;
using ServeX.Models;

namespace ServeX.Services
{
    public sealed class RoomService
    {
        private readonly IDbContextFactory<ServeXDbContext> _contexts;
        public RoomService(IDbContextFactory<ServeXDbContext> contexts) { _contexts = contexts; }

        public async Task<List<Room>> ListAsync()
        {
            using var context = _contexts.CreateDbContext();
            return await context.Rooms.AsNoTracking().OrderBy(room => room.RoomNumber).ToListAsync();
        }

        public async Task SaveAsync(int? id, string number, RoomStatus status)
        {
            number = number?.Trim() ?? string.Empty;
            if (number.Length is < 1 or > 20 || !Enum.IsDefined(status))
                throw new InvalidOperationException("Enter a valid room number and status.");
            using var context = _contexts.CreateDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();
            if (await context.Rooms.AnyAsync(room => room.Id != id && EF.Functions.Collate(room.RoomNumber, "NOCASE") == number))
                throw new InvalidOperationException("That room number is already in use.");
            var room = id.HasValue ? await context.Rooms.FindAsync(id.Value) : new Room();
            if (room == null) throw new InvalidOperationException("That room has changed.");
            if (id.HasValue && status != RoomStatus.Occupied && await context.Orders.AnyAsync(order =>
                order.Type == OrderType.Room && order.ReferenceId == id &&
                (order.Status == OrderStatus.Open || order.Status == OrderStatus.Preparing || order.Status == OrderStatus.Served)))
                throw new InvalidOperationException("Close the room's active order before changing its status.");
            room.RoomNumber = number;
            room.Status = status;
            if (!id.HasValue) context.Rooms.Add(room);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
    }
}
