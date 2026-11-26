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

        public string StatusLabel => DashboardStatus.Label(Status);

        public string StatusTone => DashboardStatus.Tone(Status);

        public decimal Total { get; set; }

        public string Locale { get; set; }

        public string DisplayTotal => ReceiptProfile.FormatAmount(Total, Locale);

        public DateTime CreatedAt { get; set; }

        public DateTime CreatedAtLocal => BusinessTime.ToLocalTime(CreatedAt);
    }
}
