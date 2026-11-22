using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ServeX.Data;
using ServeX.Models;

namespace ServeX.Services
{
    // Called only by the explicit DEBUG --demo startup action against its separate database.
    public sealed class DemoDataService
    {
        private readonly IDbContextFactory<ServeXDbContext> _contexts;
        public DemoDataService(IDbContextFactory<ServeXDbContext> contexts) { _contexts = contexts; }
        public async Task SeedEmptyDemoAsync()
        {
            using var context = _contexts.CreateDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();
            if (await context.MenuItems.AnyAsync() || await context.Tables.AnyAsync() ||
                await context.Rooms.AnyAsync() || await context.Orders.AnyAsync() ||
                await context.Bills.AnyAsync() || await context.Users.AnyAsync() || await context.Settings.AnyAsync()) return;
            context.MenuItems.AddRange(
                new MenuItem { Name = "Classic Burger", Price = 249m, Category = "Main Course", IsAvailable = true },
                new MenuItem { Name = "Caesar Salad", Price = 179m, Category = "Salad", IsAvailable = true });
            await StarterLayout.AddToEmptyAreasAsync(context);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
    }
}
