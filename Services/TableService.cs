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
        private readonly LogService _logService = new LogService();

        public async Task<List<Table>> GetTablesAsync()
        {
            try
            {
                using (var context = new ServeXDbContext())
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
                using (var context = new ServeXDbContext())
                {
                    var activeOrders = await context.Orders
                        .AsNoTracking()
                        .Where(order =>
                            order.Type == OrderType.Table &&
                            order.Status != OrderStatus.Billed &&
                            order.Status != OrderStatus.Paid &&
                            order.Status != OrderStatus.Cancelled)
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
                using (var context = new ServeXDbContext())
                {
                    var normalizedTableNumber = NormalizeTableNumber(table?.TableNumber);
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
                using (var context = new ServeXDbContext())
                {
                    if (table == null)
                    {
                        throw new System.InvalidOperationException("The selected table could not be loaded.");
                    }

                    var normalizedTableNumber = NormalizeTableNumber(table?.TableNumber);
                    await EnsureUniqueTableNumberAsync(context, normalizedTableNumber, table?.Id);

                    var entity = await context.Tables.FirstOrDefaultAsync(item => item.Id == table.Id);
                    if (entity == null)
                    {
                        throw new System.InvalidOperationException("The selected table could not be found.");
                    }

                    if (table.Status == TableStatus.Cleaning)
                    {
                        table.Status = TableStatus.Available;
                    }

                    if (table.Area == 0)
                    {
                        table.Area = TableArea.Indoor;
                    }

                    if (string.IsNullOrWhiteSpace(normalizedTableNumber))
                    {
                        throw new System.InvalidOperationException("Table number is required.");
                    }

                    if (!System.Enum.IsDefined(typeof(TableArea), table.Area))
                    {
                        table.Area = TableArea.Indoor;
                    }

                    if (!System.Enum.IsDefined(typeof(TableStatus), table.Status) || table.Status == TableStatus.Cleaning)
                    {
                        table.Status = TableStatus.Available;
                    }

                    if (!System.Enum.IsDefined(typeof(TableArea), entity.Area))
                    {
                        entity.Area = TableArea.Indoor;
                    }

                    if (!System.Enum.IsDefined(typeof(TableStatus), entity.Status) || entity.Status == TableStatus.Cleaning)
                    {
                        entity.Status = TableStatus.Available;
                    }

                    if (entity.Id <= 0)
                    {
                        throw new System.InvalidOperationException("The selected table is invalid.");
                    }

                    if (entity.TableNumber == null)
                    {
                        return;
                    }

                    entity.TableNumber = normalizedTableNumber;
                    entity.Area = NormalizeArea(table.Area);
                    entity.Status = NormalizeStatus(table.Status);

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

        public async Task DeleteTableAsync(int id)
        {
            try
            {
                using (var context = new ServeXDbContext())
                {
                    var entity = await context.Tables.FirstOrDefaultAsync(item => item.Id == id);
                    if (entity == null)
                    {
                        return;
                    }

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
            if (!System.Enum.IsDefined(typeof(TableStatus), status) || status == TableStatus.Cleaning)
            {
                return TableStatus.Available;
            }

            return status;
        }
    }
}
