using System.Collections.Generic;

namespace ServeX.Services
{
    public class DashboardSnapshot
    {
        public decimal TodayRevenue { get; set; }

        public int ActiveTables { get; set; }

        public int TotalOrdersToday { get; set; }

        public int ParcelOrdersCount { get; set; }

        public IList<DashboardRecentOrder> RecentOrders { get; set; } = new List<DashboardRecentOrder>();
    }
}
