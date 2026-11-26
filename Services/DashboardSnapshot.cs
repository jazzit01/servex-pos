using System.Collections.Generic;
using System.Linq;

namespace ServeX.Services
{
    public sealed record DashboardSalesTotal(string Locale, decimal Amount, int Bills = 0)
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
        public int TotalTables { get; set; }
        public int ActiveOrders { get; set; }

        public int TotalOrdersToday { get; set; }

        public int ParcelOrdersCount { get; set; }

        public IList<HistoryRow> OpenOrders { get; set; } = new List<HistoryRow>();

        public IList<DashboardRecentOrder> RecentOrders { get; set; } = new List<DashboardRecentOrder>();

        public IList<DashboardTopItem> TopItems { get; set; } = new List<DashboardTopItem>();

        public IList<DashboardPaymentRow> Payments { get; set; } = new List<DashboardPaymentRow>();

        public int PaidBillsToday => TodaySales.Sum(total => total.Bills);

        /// <summary>Average paid bill today; shown only when every bill is in one currency.</summary>
        public string AverageOrderDisplay
        {
            get
            {
                var withBills = TodaySales.Where(total => total.Bills > 0).ToList();
                if (withBills.Count == 0) return "—";
                if (withBills.Count > 1) return "Mixed currencies";
                var only = withBills[0];
                return ReceiptProfile.FormatAmount(decimal.Round(only.Amount / only.Bills, 2), only.Locale);
            }
        }
    }
}
