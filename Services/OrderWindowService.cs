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
        private readonly LogService _logService = new LogService();

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

            try
            {
                if (table.Id <= 0)
                {
                    throw new System.InvalidOperationException("The selected table is invalid.");
                }

                var viewModel = new OrderViewModel(_orderService, _billingWindowService, table.Id);
                await viewModel.LoadAsync();

                if (viewModel.HasBlockingError)
                {
                    MessageBox.Show(
                        string.IsNullOrWhiteSpace(viewModel.StatusMessage)
                            ? "The order screen could not be opened right now."
                            : viewModel.StatusMessage,
                        "Order Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var window = new OrderWindow
                {
                    Owner = Application.Current?.MainWindow,
                    DataContext = viewModel
                };

                window.ShowDialog();
            }
            catch (System.Exception exception)
            {
                _logService.LogError(string.Format("Order window failed for table id {0}", table.Id), exception);
                MessageBox.Show(
                    "The order screen could not be opened right now.",
                    "Order Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }
}
