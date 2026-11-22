using ServeX.Models;

namespace ServeX.Services
{
    public class BillingOrderData
    {
        public Order Order { get; set; }

        public AppSetting Restaurant { get; set; }

        public Table Table { get; set; }
    }
}
