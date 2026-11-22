using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ServeX.Views
{
    public partial class DashboardView : UserControl
    {
        private readonly DispatcherTimer _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };

        public DashboardView()
        {
            InitializeComponent();
            _clock.Tick += (_, _) => UpdateClock();
            Loaded += (_, _) => { UpdateClock(); _clock.Start(); };
            Unloaded += (_, _) => _clock.Stop();
        }

        private void UpdateClock() =>
            DashboardClock.Text = DateTime.Now.ToString("MMM'/'dd HH:mm:ss", CultureInfo.InvariantCulture);
    }
}
