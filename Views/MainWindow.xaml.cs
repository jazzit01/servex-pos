using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;

namespace ServeX.Views
{
    public partial class MainWindow : Window
    {
        public static readonly DependencyProperty IsSidebarCollapsedProperty = DependencyProperty.Register(
            nameof(IsSidebarCollapsed), typeof(bool), typeof(MainWindow));

        public bool IsSidebarCollapsed
        {
            get => (bool)GetValue(IsSidebarCollapsedProperty);
            private set => SetValue(IsSidebarCollapsedProperty, value);
        }

        private void ToggleSidebar(object sender, RoutedEventArgs args)
        {
            IsSidebarCollapsed = !IsSidebarCollapsed;
            SidebarColumn.Width = new GridLength(IsSidebarCollapsed ? 64 : 208);
            SidebarBrand.Visibility = SidebarAccount.Visibility = IsSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
            var label = IsSidebarCollapsed ? "Expand sidebar" : "Collapse sidebar";
            ToggleSidebarButton.ToolTip = label;
            AutomationProperties.SetName(ToggleSidebarButton, label);
        }

        private readonly DispatcherTimer _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };

        public MainWindow()
        {
            InitializeComponent();
            _clock.Tick += (_, _) => UpdateClock();
            Loaded += (_, _) => { UpdateClock(); _clock.Start(); };
            Closed += (_, _) => _clock.Stop();
        }
        private void UpdateClock() =>
            HeaderClock.Text = DateTime.Now.ToString("MMM'/'dd HH:mm:ss", CultureInfo.InvariantCulture);
    }
}
