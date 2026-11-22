using System.Threading.Tasks;
using System.Runtime.Versioning;
using ServeX.ViewModels;
using ServeX.Presentation.Navigation;

namespace ServeX.Services
{
    [SupportedOSPlatform("windows")]
    public class BillingWorkspaceService
    {
        private readonly System.Func<int, BillingViewModel> _createViewModel;
        private readonly WorkspaceNavigation _navigation;

        public BillingWorkspaceService(System.Func<int, BillingViewModel> createViewModel, WorkspaceNavigation navigation)
        {
            _createViewModel = createViewModel;
            _navigation = navigation;
        }

        public async Task ShowAsync(int orderId)
        {
            var viewModel = _createViewModel(orderId);
            await viewModel.LoadAsync();

            await _navigation.OpenDetailAsync(viewModel);
        }
    }
}
