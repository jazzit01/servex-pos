using System.Threading.Tasks;
using System.Windows;
using System.Runtime.Versioning;
using ServeX.Models;
using ServeX.ViewModels;
using ServeX.Views;

namespace ServeX.Services
{
    [SupportedOSPlatform("windows")]
    public class OrderWindowService
    {
        private readonly OrderService _orderService;
        private readonly BillingWindowService _billingWindowService;

        public OrderWindowService(OrderService orderService, BillingWindowService billingWindowService)
        {
            _orderService = orderService;
            _billingWindowService = billingWindowService;
        }

        public async Task ShowForTableAsync(Table table)
        {
            if (table == null)
            {
                return;
            }

            var viewModel = new OrderViewModel(_orderService, _billingWindowService, table.Id);
            await viewModel.LoadAsync();

            var window = new OrderWindow
            {
                Owner = Application.Current?.MainWindow,
                DataContext = viewModel
            };

            window.ShowDialog();
        }
    }
}
