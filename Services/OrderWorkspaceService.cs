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
        private readonly System.Func<int, OrderType, OrderViewModel> _createViewModel;
        private readonly WorkspaceNavigation _navigation;
        private readonly LogService _logService;
        private readonly DialogService _dialogs;

        public OrderWorkspaceService(System.Func<int, OrderType, OrderViewModel> createViewModel, WorkspaceNavigation navigation, LogService logService, DialogService dialogs)
        {
            _createViewModel = createViewModel;
            _navigation = navigation;
            _logService = logService;
            _dialogs = dialogs;
        }

        public Task ShowForRoomAsync(Room room) =>
            ShowAsync(room.Id, OrderType.Room);

        public Task ShowForTableAsync(Table table) =>
            table == null ? Task.CompletedTask : ShowAsync(table.Id, OrderType.Table);

        private async Task ShowAsync(int id, OrderType type)
        {
            try
            {
                if (id <= 0)
                {
                    throw new System.InvalidOperationException("The selected table is invalid.");
                }

                var viewModel = _createViewModel(id, type);
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

                if (viewModel.TableStatus == TableStatus.Cleaning)
                {
                    _dialogs.ShowInformation("Location unavailable", type == OrderType.Room
                        ? "A room under maintenance cannot take orders." : "Mark this table clean before starting an order.");
                    return;
                }
                if (viewModel.TableStatus == TableStatus.Reserved)
                {
                    if (!_dialogs.Confirm("Use reserved location?", "Start an order for this reservation?")) return;
                    viewModel.AllowReservedTable = true;
                }

                await _navigation.OpenDetailAsync(viewModel);
            }
            catch (System.Exception exception)
            {
                _logService.LogError(string.Format("Order workspace failed for table id {0}", id), exception);
                MessageBox.Show(
                    "The order screen could not be opened right now.",
                    "Order Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }
}
