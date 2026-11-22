#nullable enable

using System;
using System.Collections.Generic;

namespace ServeX.Models
{
    public class Order
    {
        public int Id { get; set; }

        public OrderType Type { get; set; }

        public int Revision { get; set; }

        public int ReferenceId { get; set; }

        public string? CustomerName { get; set; }

        public OrderStatus Status { get; set; } = OrderStatus.Open;

        public DateTime? CancelledAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();

        public Bill? Bill { get; set; }
    }
}
