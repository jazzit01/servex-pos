using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ServeX.Data;
using ServeX.Models;

namespace ServeX.Services
{
    public sealed class MenuValidationException : InvalidOperationException
    {
        public MenuValidationException(string message) : base(message) { }
    }

    public class MenuService
    {
        private readonly IDbContextFactory<ServeXDbContext> _contexts;
        private readonly OperatorSession _session;
        public MenuService(IDbContextFactory<ServeXDbContext> contexts, LogService logService, OperatorSession session)
        {
            _contexts = contexts;
            _session = session;
            _logService = logService;
        }

        private readonly LogService _logService;

        public async Task<List<MenuItem>> GetMenuItemsAsync()
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

        public async Task<MenuItem> AddMenuItemAsync(MenuItem menuItem)
        {
            _session.RequireManager();
            try
            {
                using (var context = _contexts.CreateDbContext())
                {
                    using var transaction = await context.Database.BeginTransactionAsync();
                    var (name, category, price) = await ValidateAsync(context, menuItem, null);
                    var entity = new MenuItem
                    {
                        Name = name,
                        Category = category,
                        Price = price,
                        TaxExempt = menuItem.TaxExempt,
                        IsAvailable = menuItem.IsAvailable
                    };

                    context.MenuItems.Add(entity);
                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();
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
            _session.RequireManager();
            try
            {
                using (var context = _contexts.CreateDbContext())
                {
                    using var transaction = await context.Database.BeginTransactionAsync();
                    var entity = await context.MenuItems.FirstOrDefaultAsync(item => item.Id == menuItem.Id && !item.IsArchived);
                    if (entity == null)
                    {
                        throw new System.InvalidOperationException("The menu item no longer exists. Refresh and try again.");
                    }

                    var (name, category, price) = await ValidateAsync(context, menuItem, entity.Id);
                    entity.Name = name;
                    entity.Category = category;
                    entity.Price = price;
                    entity.TaxExempt = menuItem.TaxExempt;
                    entity.IsAvailable = menuItem.IsAvailable;

                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    _logService.LogInfo(string.Format("Menu item updated: {0}", entity.Name));
                }
            }
            catch (System.Exception exception)
            {
                _logService.LogError("Menu item update failed", exception);
                throw;
            }
        }

        private static async Task<(string Name, string Category, decimal Price)> ValidateAsync(
            ServeXDbContext context, MenuItem item, int? currentId)
        {
            ArgumentNullException.ThrowIfNull(item);
            var name = item.Name?.Trim() ?? string.Empty;
            var category = item.Category?.Trim() ?? string.Empty;
            var price = decimal.Round(item.Price, 2);

            if (name.Length == 0 || name.Length > 100)
                throw new MenuValidationException("Enter a menu item name from 1 to 100 characters.");
            if (category.Length == 0 || category.Length > 50)
                throw new MenuValidationException("Enter a category from 1 to 50 characters.");
            if (price <= 0)
                throw new MenuValidationException("Price must be greater than zero after rounding to two decimal places.");

            var otherNames = await context.MenuItems.Where(other => other.Id != currentId && !other.IsArchived)
                .Select(other => new { other.Name, other.Category }).ToListAsync();
            if (otherNames.Any(other =>
                string.Equals(other.Name.Trim(), name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(other.Category.Trim(), category, StringComparison.OrdinalIgnoreCase)))
                throw new MenuValidationException("An item with this name already exists in this category.");

            category = otherNames.Select(other => other.Category)
                .FirstOrDefault(existing => string.Equals(existing.Trim(), category, StringComparison.OrdinalIgnoreCase)) ?? category;
            return (name, category, price);
        }

        public async Task ArchiveMenuItemAsync(int id)
        {
            _session.RequireManager();
            try
            {
                using (var context = _contexts.CreateDbContext())
                {
                    var entity = await context.MenuItems.FirstOrDefaultAsync(item => item.Id == id);
                    if (entity == null)
                    {
                        throw new System.InvalidOperationException("The menu item no longer exists. Refresh and try again.");
                    }

                    entity.IsArchived = true;
                    entity.IsAvailable = false;
                    await context.SaveChangesAsync();
                    _logService.LogInfo(string.Format("Menu item archived: {0}", entity.Name));
                }
            }
            catch (System.Exception exception)
            {
                _logService.LogError(string.Format("Menu item archive failed for id {0}", id), exception);
                throw;
            }
        }
    }
}
