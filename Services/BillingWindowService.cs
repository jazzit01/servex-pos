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
        private readonly BillingService _billingService;
        private readonly ReceiptPrintService _receiptPrintService;

        public BillingWindowService(BillingService billingService, ReceiptPrintService receiptPrintService)
        {
            _billingService = billingService;
            _receiptPrintService = receiptPrintService;
        }

        public async Task ShowAsync(int orderId)
        {
            var viewModel = new BillingViewModel(_billingService, _receiptPrintService, orderId);
            await viewModel.LoadAsync();

            var window = new BillingWindow
            {
                Owner = Application.Current?.MainWindow,
                DataContext = viewModel
            };

            window.ShowDialog();
        }
    }
}
