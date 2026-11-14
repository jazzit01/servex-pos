using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ServeX.Data;
using ServeX.Models;

namespace ServeX.Services
{
    public class MenuService
    {
        private readonly IDbContextFactory<ServeXDbContext> _contexts;
        public MenuService(IDbContextFactory<ServeXDbContext> contexts, LogService logService)
        {
            _contexts = contexts;
            _logService = logService;
        }

        private readonly LogService _logService;

        public async Task<List<MenuItem>> GetMenuItemsAsync()
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

        public async Task<MenuItem> AddMenuItemAsync(MenuItem menuItem)
        {
            try
            {
                using (var context = _contexts.CreateDbContext())
                {
                    var entity = new MenuItem
                    {
                        Name = menuItem.Name,
                        Category = menuItem.Category,
                        Price = menuItem.Price,
                        IsAvailable = menuItem.IsAvailable
                    };

                    context.MenuItems.Add(entity);
                    await context.SaveChangesAsync();
                    _logService.LogInfo(string.Format("Menu item added: {0}", entity.Name));
                    return entity;
                }
            }
            catch (System.Exception exception)
            {
                _logService.LogError("Menu item save failed", exception);
                throw;
            }
        }

        public async Task UpdateMenuItemAsync(MenuItem menuItem)
        {
            try
            {
                using (var context = _contexts.CreateDbContext())
                {
                    var entity = await context.MenuItems.FirstOrDefaultAsync(item => item.Id == menuItem.Id);
                    if (entity == null)
                    {
                        throw new System.InvalidOperationException("The menu item no longer exists. Refresh and try again.");
                    }

                    entity.Name = menuItem.Name;
                    entity.Category = menuItem.Category;
                    entity.Price = menuItem.Price;
                    entity.IsAvailable = menuItem.IsAvailable;

                    await context.SaveChangesAsync();
                    _logService.LogInfo(string.Format("Menu item updated: {0}", entity.Name));
                }
            }
            catch (System.Exception exception)
            {
                _logService.LogError("Menu item update failed", exception);
                throw;
            }
        }

        public async Task DeleteMenuItemAsync(int id)
        {
            try
            {
                using (var context = _contexts.CreateDbContext())
                {
                    var entity = await context.MenuItems.FirstOrDefaultAsync(item => item.Id == id);
                    if (entity == null)
                    {
                        throw new System.InvalidOperationException("The menu item no longer exists. Refresh and try again.");
                    }

                    context.MenuItems.Remove(entity);
                    await context.SaveChangesAsync();
                    _logService.LogInfo(string.Format("Menu item deleted: {0}", entity.Name));
                }
            }
            catch (System.Exception exception)
            {
                _logService.LogError(string.Format("Menu item delete failed for id {0}", id), exception);
                throw;
            }
        }
    }
}
