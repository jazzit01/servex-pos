using System.Collections.Generic;

namespace ServeX.Models
{
    public class MenuItem
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public decimal Price { get; set; }

        public string Category { get; set; }

        public bool IsAvailable { get; set; }

        public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    }
}
