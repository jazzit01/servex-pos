namespace ServeX.Models
{
    public class Table
    {
        public int Id { get; set; }

        public string TableNumber { get; set; }

        public TableStatus Status { get; set; } = TableStatus.Available;
    }
}
