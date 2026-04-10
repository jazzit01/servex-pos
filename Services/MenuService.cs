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
        public async Task<List<MenuItem>> GetMenuItemsAsync()
        {
            using (var context = new ServeXDbContext())
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
            using (var context = new ServeXDbContext())
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
                return entity;
            }
        }

        public async Task UpdateMenuItemAsync(MenuItem menuItem)
        {
            using (var context = new ServeXDbContext())
            {
                var entity = await context.MenuItems.FirstOrDefaultAsync(item => item.Id == menuItem.Id);
                if (entity == null)
                {
                    return;
                }

                entity.Name = menuItem.Name;
                entity.Category = menuItem.Category;
                entity.Price = menuItem.Price;
                entity.IsAvailable = menuItem.IsAvailable;

                await context.SaveChangesAsync();
            }
        }

        public async Task DeleteMenuItemAsync(int id)
        {
            using (var context = new ServeXDbContext())
            {
                var entity = await context.MenuItems.FirstOrDefaultAsync(item => item.Id == id);
                if (entity == null)
                {
                    return;
                }

                context.MenuItems.Remove(entity);
                await context.SaveChangesAsync();
            }
        }
    }
}
