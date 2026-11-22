using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ServeX.Data;
using ServeX.Models;

namespace ServeX.Services
{
    public class TableService
    {
        private readonly IDbContextFactory<ServeXDbContext> _contexts;
        private readonly OperatorSession _session;
        public TableService(IDbContextFactory<ServeXDbContext> contexts, LogService logService, OperatorSession session)
        {
            _contexts = contexts;
            _session = session;
            _logService = logService;
        }

        private readonly LogService _logService;

        public async Task<List<Table>> GetTablesAsync()
        {
            try
            {
                using (var context = _contexts.CreateDbContext())
                {
                    var items = await context.Tables
                        .AsNoTracking()
                        .OrderBy(table => table.TableNumber)
                        .ToListAsync();

                    return items
                        .Where(table => table != null)
                        .Select(NormalizeTable)
                        .ToList();
                }
            }
            catch (System.Exception exception)
            {
                _logService.LogError("Table load failed", exception);
                throw;
            }
        }

        public async Task<Dictionary<int, string>> GetActiveGuestNamesByTableAsync()
        {
            try
            {
                using (var context = _contexts.CreateDbContext())
                {
                    var activeOrders = await context.Orders
                        .AsNoTracking()
                        .Where(order =>
                            order.Type == OrderType.Table &&
                            order.Status != OrderStatus.Billed &&
                            order.Status != OrderStatus.Paid &&
                            order.Status != OrderStatus.Cancelled &&
                            order.Status != OrderStatus.Merged)
                        .OrderByDescending(order => order.CreatedAt)
                        .Select(order => new
                        {
                            order.ReferenceId,
                            order.CustomerName
                        })
                        .ToListAsync();

                    return activeOrders
                        .GroupBy(order => order.ReferenceId)
                        .ToDictionary(
                            group => group.Key,
                            group =>
                            {
                                var customerName = group
                                    .Select(order => order.CustomerName)
                                    .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));

                                return string.IsNullOrWhiteSpace(customerName)
                                    ? "Atithi"
                                    : customerName.Trim();
                            });
                }
            }
            catch (System.Exception exception)
            {
                _logService.LogError("Active guest lookup failed for tables", exception);
                throw;
            }
        }

        public async Task<Table> AddTableAsync(Table table)
        {
            try
            {
                using (var context = _contexts.CreateDbContext())
                {
                    var normalizedTableNumber = NormalizeTableNumber(table.TableNumber);
                    await EnsureUniqueTableNumberAsync(context, normalizedTableNumber);

                    var entity = new Table
                    {
                        TableNumber = normalizedTableNumber,
                        Area = NormalizeArea(table?.Area ?? TableArea.Indoor),
                        Status = NormalizeStatus(table?.Status ?? TableStatus.Available)
                    };

                    context.Tables.Add(entity);
                    await context.SaveChangesAsync();
                    _logService.LogInfo(string.Format("Table added: {0}", entity.TableNumber));
                    return entity;
                }
            }
            catch (System.Exception exception)
            {
                _logService.LogError("Table save failed", exception);
                throw;
            }
        }

        public async Task UpdateTableAsync(Table table)
        {
            try
            {
                using (var context = _contexts.CreateDbContext())
                {
                    if (table == null)
                    {
                        throw new System.InvalidOperationException("The selected table could not be loaded.");
                    }

                    var normalizedTableNumber = NormalizeTableNumber(table.TableNumber);
                    await EnsureUniqueTableNumberAsync(context, normalizedTableNumber, table.Id);

                    var entity = await context.Tables.FirstOrDefaultAsync(item => item.Id == table.Id);
                    if (entity == null)
                    {
                        throw new System.InvalidOperationException("The selected table could not be found.");
                    }

                    entity.TableNumber = normalizedTableNumber;
                    entity.Area = NormalizeArea(table.Area);
                    if (entity.Status != TableStatus.Cleaning) entity.Status = NormalizeStatus(table.Status);

                    await context.SaveChangesAsync();
                    _logService.LogInfo(string.Format("Table updated: {0}", entity.TableNumber));
                }
            }
            catch (System.Exception exception)
            {
                _logService.LogError("Table update failed", exception);
                throw;
            }
        }

        public async Task ChangeFloorStatusAsync(int tableId, TableStatus expected, TableStatus next)
        {
            if ((expected, next) is not ((TableStatus.Available, TableStatus.Reserved) or
                (TableStatus.Reserved, TableStatus.Available) or (TableStatus.Cleaning, TableStatus.Available)))
                throw new System.InvalidOperationException("That table status change is not allowed.");

            using var context = _contexts.CreateDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();
            var table = await context.Tables.FindAsync(tableId);
            if (table == null || table.Status != expected || await context.Orders.AnyAsync(order =>
                order.Type == OrderType.Table && order.ReferenceId == tableId &&
                (order.Status == OrderStatus.Open || order.Status == OrderStatus.Preparing || order.Status == OrderStatus.Served)))
                throw new System.InvalidOperationException("The floor has changed. Refresh the table before trying again.");

            table.Status = next;
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        public async Task DeleteTableAsync(int id)
        {
            _session.RequireManager();
            try
            {
                using (var context = _contexts.CreateDbContext())
                {
                    var entity = await context.Tables.FirstOrDefaultAsync(item => item.Id == id);
                    if (entity == null)
                    {
                        return;
                    }

                    if (await context.Orders.AnyAsync(order => order.Type == OrderType.Table && order.ReferenceId == id))
                        throw new System.InvalidOperationException("Tables linked to order history cannot be deleted.");

                    context.Tables.Remove(entity);
                    await context.SaveChangesAsync();
                    _logService.LogInfo(string.Format("Table deleted: {0}", entity.TableNumber));
                }
            }
            catch (System.Exception exception)
            {
                _logService.LogError(string.Format("Table delete failed for id {0}", id), exception);
                throw;
            }
        }

        private static Table NormalizeTable(Table table)
        {
            table.Area = NormalizeArea(table.Area);
            table.Status = NormalizeStatus(table.Status);
            return table;
        }

        private static string NormalizeTableNumber(string tableNumber)
        {
            return string.IsNullOrWhiteSpace(tableNumber)
                ? string.Empty
                : tableNumber.Trim();
        }

        private static async Task EnsureUniqueTableNumberAsync(ServeXDbContext context, string tableNumber, int? currentTableId = null)
        {
            if (string.IsNullOrWhiteSpace(tableNumber))
            {
                throw new System.InvalidOperationException("Table number is required.");
            }

            var normalizedTableNumber = tableNumber.Trim().ToLower();
            var duplicateExists = await context.Tables
                .AnyAsync(table =>
                    table.Id != currentTableId.GetValueOrDefault() &&
                    (table.TableNumber ?? string.Empty).ToLower() == normalizedTableNumber);

            if (duplicateExists)
            {
                throw new System.InvalidOperationException("Table number must be unique.");
            }
        }

        private static TableArea NormalizeArea(TableArea area)
        {
            return System.Enum.IsDefined(typeof(TableArea), area)
                ? area
                : TableArea.Indoor;
        }

        private static TableStatus NormalizeStatus(TableStatus status)
        {
            if (!System.Enum.IsDefined(typeof(TableStatus), status))
            {
                return TableStatus.Available;
            }

            return status;
        }
    }
}
