using System;
using ServeX.Models;

namespace ServeX.Services
{
    /// <summary>Display text and colour tone for an order status, shared by the dashboard grids.</summary>
    public static class DashboardStatus
    {
        public static string Label(OrderStatus status)
        {
            switch (status)
            {
                case OrderStatus.Open: return "Open";
                case OrderStatus.Preparing: return "Preparing";
                case OrderStatus.Served: return "Served";
                case OrderStatus.Billed: return "Billed";
                case OrderStatus.Paid: return "Paid";
                case OrderStatus.Cancelled: return "Cancelled";
                case OrderStatus.Merged: return "Merged";
                default: return status.ToString();
            }
        }

        public static string Tone(OrderStatus status)
        {
            switch (status)
            {
                case OrderStatus.Paid: return "Success";
                case OrderStatus.Open: return "Info";
                case OrderStatus.Preparing: return "Warning";
                case OrderStatus.Served: return "Accent";
                case OrderStatus.Billed: return "Accent";
                case OrderStatus.Cancelled: return "Danger";
                default: return "Neutral";
            }
        }

        public static string FormatAge(TimeSpan age)
        {
            if (age < TimeSpan.FromMinutes(1)) return "Just now";
            if (age < TimeSpan.FromHours(1)) return string.Format("{0} min", (int)age.TotalMinutes);
            if (age < TimeSpan.FromDays(1)) return string.Format("{0} h {1} min", (int)age.TotalHours, age.Minutes);
            return string.Format("{0} d {1} h", (int)age.TotalDays, age.Hours);
        }
    }

    public sealed record DashboardTopItem(int Rank, string Name, int Units)
    {
        public string UnitsDisplay => Units == 1 ? "1 sold" : Units + " sold";
    }

    public sealed record DashboardPaymentRow(string Label, int Count, decimal Amount, string Locale, double SharePercent)
    {
        public string DisplayAmount => ReceiptProfile.FormatAmount(Amount, Locale);
        public string CountDisplay => Count == 1 ? "1 payment" : Count + " payments";
    }

    /// <summary>An unpaid order shown on the dashboard, with how long it has been open.</summary>
    public sealed class DashboardOpenOrder
    {
        public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(60);

        public DashboardOpenOrder(HistoryRow row, DateTime nowLocal)
        {
            Row = row ?? throw new ArgumentNullException(nameof(row));
            var age = nowLocal - row.CreatedLocal;
            Age = age < TimeSpan.Zero ? TimeSpan.Zero : age;
        }

        public HistoryRow Row { get; }
        public int Id => Row.Id;
        public string OrderNumber => "#" + Row.Id;
        public string Reference => Row.Reference;
        public string ServiceLabel => Row.ServiceLabel;
        public OrderStatus Status => Row.Status;
        public string StatusLabel => DashboardStatus.Label(Row.Status);
        public string StatusTone => DashboardStatus.Tone(Row.Status);
        public string DisplayTotal => Row.DisplayTotal;
        public TimeSpan Age { get; }
        public string AgeDisplay => DashboardStatus.FormatAge(Age);
        public bool IsStale => Age >= StaleAfter;
    }
}
