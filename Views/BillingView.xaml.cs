using System.Windows;
using System.Windows.Controls;
using ServeX.ViewModels;

namespace ServeX.Views
{
    public partial class BillingView : UserControl
    {
        public BillingView()
        {
            InitializeComponent();
        }

        private void PrepareConfirmation(object sender, RoutedEventArgs args)
        {
            if (DataContext is BillingViewModel billing) billing.ManagerPassword = ManagerPasswordBox.Password;
            ManagerPasswordBox.Clear();
        }
    }
}
