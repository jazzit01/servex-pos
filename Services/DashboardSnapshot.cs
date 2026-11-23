using System.Collections.Generic;

namespace ServeX.Services
{
    public sealed record DashboardSalesTotal(string Locale, decimal Amount)
    {
        public string Currency => ReceiptProfile.CurrencyCode(Locale);
        public string DisplayAmount => ReceiptProfile.FormatAmount(Amount, Locale);
    }

    public class DashboardSnapshot
    {
        public IList<DashboardSalesTotal> TodaySales { get; set; } = new List<DashboardSalesTotal>();

        public int ReservedTables { get; set; }
        public int OccupiedTables { get; set; }
        public int AvailableTables { get; set; }
        public int ActiveOrders { get; set; }

        public int TotalOrdersToday { get; set; }

        public int ParcelOrdersCount { get; set; }

        public IList<HistoryRow> OpenOrders { get; set; } = new List<HistoryRow>();

        public IList<DashboardRecentOrder> RecentOrders { get; set; } = new List<DashboardRecentOrder>();
    }
}
