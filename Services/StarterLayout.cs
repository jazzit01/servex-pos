using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ServeX.Data;
using ServeX.Models;

namespace ServeX.Services
{
    internal static class StarterLayout
    {
        public static async Task<int> AddToEmptyAreasAsync(ServeXDbContext context)
        {
            var tables = await context.Tables.AsNoTracking().ToListAsync();
            var usedNumbers = new HashSet<string>(tables.Select(table => table.TableNumber), StringComparer.OrdinalIgnoreCase);
            var added = 0;
            foreach (var (area, prefix) in new[] { (TableArea.Indoor, "IN-"), (TableArea.Outdoor, "OUT-") })
            {
                if (tables.Any(table => table.Area == area)) continue;
                for (int number = 1, count = 0; count < 5; number++)
                {
                    var name = $"{prefix}{number}";
                    if (!usedNumbers.Add(name)) continue;
                    context.Tables.Add(new Table { TableNumber = name, Area = area, Status = TableStatus.Available });
                    count++;
                    added++;
                }
            }

            if (!await context.Rooms.AnyAsync())
            {
                for (int number = 101; number <= 103; number++)
                {
                    context.Rooms.Add(new Room { RoomNumber = $"R{number}", Status = RoomStatus.Available });
                    added++;
                }
            }

            return added;
        }
    }
}
