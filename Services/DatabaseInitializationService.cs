using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ServeX.Data;
using ServeX.Models;

namespace ServeX.Services
{
    public class DatabaseInitializationService
    {
        public async Task InitializeAsync()
        {
            using (var context = new ServeXDbContext())
            {
                await context.Database.MigrateAsync();

                if (!await context.MenuItems.AnyAsync())
                {
                    context.MenuItems.AddRange(
                        new MenuItem
                        {
                            Name = "Classic Burger",
                            Price = 249.00m,
                            Category = "Main Course",
                            IsAvailable = true
                        },
                        new MenuItem
                        {
                            Name = "Caesar Salad",
                            Price = 179.00m,
                            Category = "Salad",
                            IsAvailable = true
                        });
                }

                if (!await context.Tables.AnyAsync())
                {
                    context.Tables.AddRange(
                        new Table
                        {
                            TableNumber = "T1",
                            Area = TableArea.Indoor,
                            Status = TableStatus.Available
                        },
                        new Table
                        {
                            TableNumber = "T2",
                            Area = TableArea.Indoor,
                            Status = TableStatus.Available
                        });
                }

                if (!await context.Rooms.AnyAsync())
                {
                    context.Rooms.AddRange(
                        new Room
                        {
                            RoomNumber = "R101",
                            Status = RoomStatus.Available
                        },
                        new Room
                        {
                            RoomNumber = "R102",
                            Status = RoomStatus.Reserved
                        });
                }

                await context.SaveChangesAsync();

                if (!await context.Orders.AnyAsync())
                {
                    var firstTable = await context.Tables.OrderBy(table => table.Id).FirstAsync();
                    var firstMenuItem = await context.MenuItems.OrderBy(menuItem => menuItem.Id).FirstAsync();
                    var quantity = 2;
                    var subtotal = firstMenuItem.Price * quantity;
                    var configuredTax = await context.Settings
                        .AsNoTracking()
                        .Select(setting => (decimal?)setting.TaxPercentage)
                        .FirstOrDefaultAsync();
                    var taxRate = (configuredTax ?? 5m) / 100m;
                    var tax = Math.Round(subtotal * taxRate, 2);
                    var discount = 0m;
                    var total = subtotal + tax - discount;

                    firstTable.Status = TableStatus.Occupied;

                    context.Orders.Add(new Order
                    {
                        Type = OrderType.Table,
                        ReferenceId = firstTable.Id,
                        Status = OrderStatus.Open,
                        CreatedAt = DateTime.UtcNow,
                        Items =
                        {
                            new OrderItem
                            {
                                MenuItemId = firstMenuItem.Id,
                                Quantity = quantity,
                                Price = firstMenuItem.Price
                            }
                        },
                        Bill = new Bill
                        {
                            Subtotal = subtotal,
                            Tax = tax,
                            Discount = discount,
                            Total = total
                        }
                    });

                    await context.SaveChangesAsync();
                }
            }
        }
    }
}
