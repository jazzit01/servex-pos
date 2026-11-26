using System;
using System.Windows.Controls;
using System.Windows.Threading;
using ServeX.ViewModels;

namespace ServeX.Views
{
    public partial class DashboardView : UserControl
    {
        private readonly DispatcherTimer _refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };

        public DashboardView()
        {
            InitializeComponent();
            _refresh.Tick += async (_, _) =>
            {
                // Only reload while the dashboard is actually on screen.
                if (IsVisible && DataContext is DashboardViewModel dashboard)
                {
                    await dashboard.AutoRefreshAsync();
                }
            };
            Loaded += (_, _) => _refresh.Start();
            Unloaded += (_, _) => _refresh.Stop();
        }
    }
}
