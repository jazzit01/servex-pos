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
        private readonly System.Func<int, OrderViewModel> _createViewModel;
        private readonly System.Func<OrderWindow> _createWindow;
        private readonly LogService _logService;

        public OrderWindowService(System.Func<int, OrderViewModel> createViewModel, System.Func<OrderWindow> createWindow, LogService logService)
        {
            _createViewModel = createViewModel;
            _createWindow = createWindow;
            _logService = logService;
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

                var viewModel = _createViewModel(table.Id);
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

                var window = _createWindow();
                window.Owner = System.Windows.Application.Current?.MainWindow;
                window.DataContext = viewModel;

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
