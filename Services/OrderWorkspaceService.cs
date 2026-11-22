using System.Threading.Tasks;
using System.Windows;
using System.Runtime.Versioning;
using ServeX.Models;
using ServeX.ViewModels;
using ServeX.Presentation.Navigation;

namespace ServeX.Services
{
    [SupportedOSPlatform("windows")]
    public class OrderWorkspaceService
    {
        private readonly System.Func<int, OrderViewModel> _createViewModel;
        private readonly WorkspaceNavigation _navigation;
        private readonly LogService _logService;

        public OrderWorkspaceService(System.Func<int, OrderViewModel> createViewModel, WorkspaceNavigation navigation, LogService logService)
        {
            _createViewModel = createViewModel;
            _navigation = navigation;
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

                await _navigation.OpenDetailAsync(viewModel);
            }
            catch (System.Exception exception)
            {
                _logService.LogError(string.Format("Order workspace failed for table id {0}", table.Id), exception);
                MessageBox.Show(
                    "The order screen could not be opened right now.",
                    "Order Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }
}
