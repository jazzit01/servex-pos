using System;
using System.Collections.Generic;
using System.Linq;

namespace ServeX.Models
{
    public static class TableFloorLayout
    {
        public const int Columns = 6;

        public static Dictionary<int, int> GetPositions(IEnumerable<Table> tables)
        {
            var ordered = tables.OrderBy(table => table.Id).ToList();
            var positions = new Dictionary<int, int>();
            var occupied = new HashSet<int>();

            foreach (var table in ordered)
            {
                if (table.FloorSlot is int slot && slot >= 0 && occupied.Add(slot))
                    positions.Add(table.Id, slot);
            }

            var next = 0;
            foreach (var table in ordered.Where(table => !positions.ContainsKey(table.Id)))
            {
                while (!occupied.Add(next))
                    next++;

                positions.Add(table.Id, next);
            }

            return positions;
        }

        public static int GetRowCount(IEnumerable<int> positions)
        {
            var last = positions.DefaultIfEmpty(-1).Max();
            return last < 0 ? 1 : last / Columns + 2;
        }
    }
}
