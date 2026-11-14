using System.Threading.Tasks;
using System.Windows;
using System.Runtime.Versioning;
using ServeX.ViewModels;
using ServeX.Views;

namespace ServeX.Services
{
    [SupportedOSPlatform("windows")]
    public class BillingWindowService
    {
        private readonly System.Func<int, BillingViewModel> _createViewModel;
        private readonly System.Func<BillingWindow> _createWindow;

        public BillingWindowService(System.Func<int, BillingViewModel> createViewModel, System.Func<BillingWindow> createWindow)
        {
            _createViewModel = createViewModel;
            _createWindow = createWindow;
        }

        public async Task ShowAsync(int orderId)
        {
            var viewModel = _createViewModel(orderId);
            await viewModel.LoadAsync();

            var window = _createWindow();
            window.Owner = System.Windows.Application.Current?.MainWindow;
            window.DataContext = viewModel;

            window.ShowDialog();
        }
    }
}
