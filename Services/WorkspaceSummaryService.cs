using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ServeX.Data;

namespace ServeX.Services
{
    public sealed record WorkspaceSummary(int MenuItems, int Tables, int Rooms, int Orders);
    public sealed class WorkspaceSummaryService
    {
        private readonly IDbContextFactory<ServeXDbContext> _contexts;
        public WorkspaceSummaryService(IDbContextFactory<ServeXDbContext> contexts) { _contexts = contexts; }
        public async Task<WorkspaceSummary> LoadAsync()
        {
            using var context = _contexts.CreateDbContext();
            return new WorkspaceSummary(await context.MenuItems.CountAsync(), await context.Tables.CountAsync(),
                await context.Rooms.CountAsync(), await context.Orders.CountAsync());
        }
    }
}
