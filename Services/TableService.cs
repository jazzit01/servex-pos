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

        public async Task<Table> AddTableAsync(Table table)
        {
            try
            {
                using (var context = new ServeXDbContext())
                {
                    var entity = new Table
                    {
                        TableNumber = table.TableNumber,
                        Area = NormalizeArea(table.Area),
                        Status = NormalizeStatus(table.Status)
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
                    var entity = await context.Tables.FirstOrDefaultAsync(item => item.Id == table.Id);
                    if (entity == null)
                    {
                        return;
                    }

                    entity.TableNumber = table.TableNumber;
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

        private static TableArea NormalizeArea(TableArea area)
        {
            return System.Enum.IsDefined(typeof(TableArea), area)
                ? area
                : TableArea.Indoor;
        }

        private static TableStatus NormalizeStatus(TableStatus status)
        {
            return System.Enum.IsDefined(typeof(TableStatus), status)
                ? status
                : TableStatus.Available;
        }
    }
}
