using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using ServeX.Services;

namespace ServeX.ViewModels
{
    /// <summary>The end-of-day report: totals, payments, best sellers and anything still unpaid.</summary>
    [SupportedOSPlatform("windows")]
    public sealed class DayCloseViewModel : ViewModelBase
    {
        private readonly DayCloseService _service;
        private readonly ApplicationSettingsService _settings;
        private readonly LogService _log;
        private DayCloseSummary _summary = new DayCloseSummary { Day = DateTime.Today };
        private DateTime? _day = DateTime.Today;
        private string _message = string.Empty;
        private bool _isLoading;

        public DayCloseViewModel(DayCloseService service, ApplicationSettingsService settings, LogService log)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync(), _ => !_isLoading);
            PrintCommand = new AsyncRelayCommand(_ => PrintAsync(), _ => !_isLoading && _summary.PaidBills > 0);
        }

        public AsyncRelayCommand RefreshCommand { get; }

        public AsyncRelayCommand PrintCommand { get; }

        /// <summary>Bound to the date picker. Picking a new date reloads the report.</summary>
        public DateTime? Day
        {
            get => _day;
            set
            {
                if (value is null || !SetProperty(ref _day, value.Value.Date)) return;
                OnPropertyChanged(nameof(TitleDisplay));
                RefreshCommand.Execute(null);
            }
        }

        public string TitleDisplay => (_day ?? DateTime.Today).ToString("dddd, d MMMM yyyy");

        public IReadOnlyList<DayCloseSection> Sections => _summary.Sections;

        public IReadOnlyList<DashboardPaymentRow> Payments => _summary.Payments;

        public IReadOnlyList<DashboardTopItem> TopItems => _summary.TopItems;

        public IReadOnlyList<HistoryRow> UnpaidOrders => _summary.UnpaidOrders;

        public bool HasSales => _summary.PaidBills > 0;

        public bool HasNoSales => !HasSales;

        public bool HasPayments => _summary.Payments.Count > 0;

        public bool HasTopItems => _summary.TopItems.Count > 0;

        public bool HasUnpaid => _summary.UnpaidOrders.Count > 0;

        public string UnpaidMessage => _summary.UnpaidOrders.Count == 1
            ? "1 order is still unpaid. Settle or cancel it before closing the day."
            : string.Format("{0} orders are still unpaid. Settle or cancel them before closing the day.", _summary.UnpaidOrders.Count);

        public string CancelledDisplay => _summary.CancelledOrders == 1 ? "1 cancelled order" : _summary.CancelledOrders + " cancelled orders";

        public string TimingDisplay => _summary.FirstBillLocal is DateTime first && _summary.LastBillLocal is DateTime last
            ? string.Format("First bill {0:HH:mm} · last bill {1:HH:mm}", first, last)
            : "No bills were paid on this day.";

        public string Message
        {
            get => _message;
            private set
            {
                if (SetProperty(ref _message, value)) OnPropertyChanged(nameof(HasMessage));
            }
        }

        public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

        /// <summary>Opens the report on the given day (the dashboard opens it on today).</summary>
        public async Task LoadAsync(DateTime day)
        {
            _day = day.Date;
            OnPropertyChanged(nameof(Day));
            OnPropertyChanged(nameof(TitleDisplay));
            await RefreshAsync();
        }

        public async Task RefreshAsync()
        {
            if (_isLoading) return;
            SetLoading(true);
            try
            {
                _summary = await _service.GetSummaryAsync(_day ?? DateTime.Today);
                Message = string.Empty;
            }
            catch (Exception exception)
            {
                _log.LogError("The end-of-day report could not be loaded.", exception);
                Message = "The report could not be loaded. Try Refresh.";
            }
            finally
            {
                SetLoading(false);
                RaiseSummaryChanged();
            }
        }

        private void SetLoading(bool loading)
        {
            _isLoading = loading;
            RefreshCommand.RaiseCanExecuteChanged();
            PrintCommand.RaiseCanExecuteChanged();
        }

        private void RaiseSummaryChanged()
        {
            foreach (var name in new[]
            {
                nameof(Sections), nameof(Payments), nameof(TopItems), nameof(UnpaidOrders), nameof(HasSales), nameof(HasNoSales),
                nameof(HasPayments), nameof(HasTopItems), nameof(HasUnpaid), nameof(UnpaidMessage), nameof(CancelledDisplay), nameof(TimingDisplay)
            })
            {
                OnPropertyChanged(name);
            }

            PrintCommand.RaiseCanExecuteChanged();
        }

        private async Task PrintAsync()
        {
            try
            {
                var settings = await _settings.GetSettingsAsync();
                var dialog = new PrintDialog();
                if (dialog.ShowDialog() != true) return;

                var document = BuildDocument(settings?.RestaurantName ?? "ServeX", _summary);
                document.PageWidth = dialog.PrintableAreaWidth;
                document.PageHeight = dialog.PrintableAreaHeight;
                document.PagePadding = new Thickness(48);
                document.ColumnWidth = double.PositiveInfinity;
                dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, "ServeX end-of-day report");
                Message = "Report sent to the printer.";
            }
            catch (Exception exception)
            {
                _log.LogError("The end-of-day report could not be printed.", exception);
                Message = "Could not print the report. Check your printer and try again.";
            }
        }

        private static FlowDocument BuildDocument(string restaurant, DayCloseSummary summary)
        {
            var document = new FlowDocument { FontFamily = new System.Windows.Media.FontFamily("Segoe UI"), FontSize = 13 };
            document.Blocks.Add(new Paragraph(new Run(restaurant)) { FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0) });
            document.Blocks.Add(new Paragraph(new Run("End-of-day report · " + summary.Day.ToString("dddd, d MMMM yyyy")))
                { FontSize = 14, Margin = new Thickness(0, 2, 0, 14) });

            foreach (var section in summary.Sections)
            {
                var heading = summary.Sections.Count > 1 ? "Sales (" + section.Currency + ")" : "Sales";
                document.Blocks.Add(Heading(heading));
                document.Blocks.Add(Table(new[]
                {
                    ("Paid bills", section.Bills.ToString(CultureInfo.CurrentCulture), false),
                    ("Gross sales", section.SubtotalDisplay, false),
                    ("Discounts", "-" + section.DiscountDisplay, false),
                    ("Tax collected", section.TaxDisplay, false),
                    ("Net total", section.TotalDisplay, true),
                    ("Average bill", section.AverageDisplay, false)
                }));
            }

            if (summary.Payments.Count > 0)
            {
                document.Blocks.Add(Heading("Payments by method"));
                document.Blocks.Add(Table(summary.Payments.Select(payment =>
                    (payment.Label + " (" + payment.CountDisplay + ")", payment.DisplayAmount, false)).ToArray()));
            }

            if (summary.TopItems.Count > 0)
            {
                document.Blocks.Add(Heading("Best sellers"));
                document.Blocks.Add(Table(summary.TopItems.Select(item =>
                    (item.Rank + ". " + item.Name, item.UnitsDisplay, false)).ToArray()));
            }

            document.Blocks.Add(Heading("Day notes"));
            var notes = new List<(string, string, bool)>
            {
                ("Cancelled orders", summary.CancelledOrders.ToString(CultureInfo.CurrentCulture), false)
            };
            if (summary.FirstBillLocal is DateTime first && summary.LastBillLocal is DateTime last)
            {
                notes.Add(("First bill", first.ToString("HH:mm"), false));
                notes.Add(("Last bill", last.ToString("HH:mm"), false));
            }
            if (summary.UnpaidOrders.Count > 0)
                notes.Add(("Unpaid orders still open", summary.UnpaidOrders.Count.ToString(CultureInfo.CurrentCulture), true));
            document.Blocks.Add(Table(notes.ToArray()));

            document.Blocks.Add(new Paragraph(new Run("Counted by: ______________________     Signed: ______________________"))
                { Margin = new Thickness(0, 36, 0, 0), Foreground = System.Windows.Media.Brushes.DimGray });
            return document;
        }

        private static Paragraph Heading(string text) => new Paragraph(new Run(text))
        {
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 14, 0, 4)
        };

        private static Table Table(IEnumerable<(string Label, string Value, bool Bold)> rows)
        {
            var table = new Table { CellSpacing = 0 };
            table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
            table.Columns.Add(new TableColumn { Width = new GridLength(170) });
            var group = new TableRowGroup();
            foreach (var (label, value, bold) in rows)
            {
                var row = new TableRow();
                row.Cells.Add(new TableCell(new Paragraph(new Run(label)) { Margin = new Thickness(0, 2, 0, 2) }));
                row.Cells.Add(new TableCell(new Paragraph(new Run(value)) { TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 2, 0, 2) }));
                if (bold) row.FontWeight = FontWeights.Bold;
                group.Rows.Add(row);
            }

            table.RowGroups.Add(group);
            return table;
        }
    }
}
