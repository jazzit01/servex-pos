using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Documents;
using ServeX.Models;
using ServeX.Services;

namespace ServeX.ViewModels
{
    [SupportedOSPlatform("windows")]
    public sealed class HistoryViewModel : ViewModelBase
    {
        private readonly OrderHistoryService _history;
        private readonly BillingWorkspaceService _billing;
        private readonly LogService _log;
        private List<HistoryRow> _orders = new();
        private IReadOnlyList<ReportRow> _report = Array.Empty<ReportRow>();
        private string _message = string.Empty;
        private HistoryRow _selected;
        private DateTime _reportStart;
        private DateTime _reportEnd;
        private string _reportGrouping;
        private readonly ApplicationSettingsService _settings;

        public HistoryViewModel(OrderHistoryService history, BillingWorkspaceService billing, LogService log, ApplicationSettingsService settings)
        {
            _history = history;
            _billing = billing;
            _log = log;
            _settings = settings;
            RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
            ResumeParcelCommand = new AsyncRelayCommand(_ => ResumeSelectedParcelAsync(), _ => CanResumeParcel);
            OpenOrderCommand = new AsyncRelayCommand(_ => OpenOrderAsync(), _ => Selected != null);
            PrintReportCommand = new AsyncRelayCommand(_ => PrintReportAsync(), _ => Report.Count > 0);
        }

        public DateTime? Start { get; set; } = DateTime.Today;
        public DateTime? End { get; set; } = DateTime.Today;
        public string Search { get; set; }
        public string Type { get; set; } = "All";
        public string Status { get; set; } = "All";
        public string Method { get; set; } = "All";
        public string Grouping { get; set; } = "Summary";
        public string[] Types => new[] { "All" }.Concat(Enum.GetNames<OrderType>()).ToArray();
        public string[] Statuses => new[] { "All" }.Concat(Enum.GetNames<OrderStatus>()).ToArray();
        public string[] Methods => new[] { "All" }.Concat(Enum.GetNames<PaymentMethod>()).ToArray();
        public string[] Groupings { get; } = new[] { "Summary", "Day", "Week", "Month", "Items", "Categories" };
        public AsyncRelayCommand RefreshCommand { get; }
        public Func<int, Task<bool>> ResumeParcel { private get; set; } = _ => Task.FromResult(false);
        public bool CanResumeParcel => Selected?.Type == OrderType.Parcel &&
            Selected.Status is OrderStatus.Open or OrderStatus.Preparing or OrderStatus.Served;
        public AsyncRelayCommand ResumeParcelCommand { get; }
        public AsyncRelayCommand OpenOrderCommand { get; }
        public AsyncRelayCommand PrintReportCommand { get; }
        public IReadOnlyList<HistoryRow> Orders => _orders;
        public IReadOnlyList<ReportRow> Report => _report;
        public string Message { get => _message; private set => SetProperty(ref _message, value); }
        public HistoryRow Selected
        {
            get => _selected;
            set
            {
                if (!SetProperty(ref _selected, value)) return;
                OpenOrderCommand.RaiseCanExecuteChanged();
                ResumeParcelCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(CanResumeParcel));
            }
        }

        public async Task RefreshAsync()
        {
            if (Start is not DateTime start || End is not DateTime end || end.Date < start.Date)
            {
                Message = "Select a valid date range.";
                return;
            }

            try
            {
                var search = Search?.Trim() ?? string.Empty;
                var orders = await _history.GetOrdersAsync(start, end);
                var grouping = Grouping;
                var report = await _history.GetReportAsync(start, end, grouping);
                _orders = orders.Where(row => (Type == "All" || row.Type.ToString() == Type) &&
                    (Status == "All" || row.Status.ToString() == Status) &&
                    (Method == "All" || row.Methods.Any(method => method.ToString() == Method)) &&
                    (search.Length == 0 || row.Id.ToString() == search || row.Reference.Contains(search, StringComparison.CurrentCultureIgnoreCase))).ToList();
                _report = report;
                _reportStart = start;
                _reportEnd = end;
                _reportGrouping = grouping;
                OnPropertyChanged(nameof(Orders));
                OnPropertyChanged(nameof(Report));
                PrintReportCommand.RaiseCanExecuteChanged();
                Message = $"{_orders.Count} orders in the selected range.";
            }
            catch (Exception exception)
            {
                _log.LogError("Could not load order history", exception);
                Message = "Could not load order history. Try again.";
            }
        }

        private async Task ResumeSelectedParcelAsync()
        {
            var selected = Selected;
            if (selected == null || !CanResumeParcel) return;
            if (!await ResumeParcel(selected.Id))
                Message = "Parcel was not reopened. Refresh history if the order status changed.";
        }

        private async Task OpenOrderAsync()
        {
            try { if (Selected != null) await _billing.ShowAsync(Selected.Id, readOnly: true); }
            catch (Exception exception)
            {
                _log.LogError("Could not open historical bill", exception);
                Message = "Could not open that order.";
            }
        }

        private async Task PrintReportAsync()
        {
            try
            {
                var settings = await _settings.GetSettingsAsync();
                var dialog = new PrintDialog();
                if (dialog.ShowDialog() != true) return;
                var document = new FlowDocument { PagePadding = new System.Windows.Thickness(28), ColumnWidth = double.PositiveInfinity };
                var title = _reportGrouping == "Summary" && _reportStart.Date == _reportEnd.Date ? "End-of-day report" : "Sales report";
                document.Blocks.Add(new Paragraph(new Run($"{settings?.RestaurantName ?? "ServeX"} · {title} | {_reportStart:d} to {_reportEnd:d}")));
                foreach (var row in Report)
                    document.Blocks.Add(new Paragraph(new Run($"{row.Label}   {row.Units:N0}   {row.Currency} {row.DisplayAmount}")));
                dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, "ServeX report");
            }
            catch (Exception exception)
            {
                _log.LogError("Report printing failed", exception);
                Message = "Could not print the report. Check your printer.";
            }
        }
    }
}
