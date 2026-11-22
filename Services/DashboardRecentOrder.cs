using System;
using ServeX.Models;

namespace ServeX.Services
{
    public class DashboardRecentOrder
    {
        public int OrderId { get; set; }

        public OrderType Type { get; set; }

        public string ReferenceLabel { get; set; }

        public OrderStatus Status { get; set; }

        public decimal Total { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime CreatedAtLocal => BusinessTime.ToLocalTime(CreatedAt);
    }
}
