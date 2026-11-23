#nullable enable

namespace ServeX.Models
{
    public class Table
    {
        public int Id { get; set; }

        public string TableNumber { get; set; } = string.Empty;

        public TableArea Area { get; set; } = TableArea.Indoor;

        public int? FloorSlot { get; set; }

        public TableStatus Status { get; set; } = TableStatus.Available;
    }
}
