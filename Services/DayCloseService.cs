using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ServeX.Data;
using ServeX.Models;

namespace ServeX.Services
{
    /// <summary>One currency's totals for the day.</summary>
    public sealed record DayCloseSection(string Locale, int Bills, decimal Subtotal, decimal Discount, decimal Tax, decimal Total)
    {
        public string Currency => ReceiptProfile.CurrencyCode(Locale);
        public string SubtotalDisplay => ReceiptProfile.FormatAmount(Subtotal, Locale);
        public string DiscountDisplay => ReceiptProfile.FormatAmount(Discount, Locale);
        public string TaxDisplay => ReceiptProfile.FormatAmount(Tax, Locale);
        public string TotalDisplay => ReceiptProfile.FormatAmount(Total, Locale);
        public string AverageDisplay => Bills == 0 ? "—" : ReceiptProfile.FormatAmount(decimal.Round(Total / Bills, 2), Locale);
        public string BillsDisplay => (Bills == 1 ? "1 paid bill" : Bills + " paid bills") + " · average " + AverageDisplay;
    }

    public sealed class DayCloseSummary
    {
        public DateTime Day { get; init; }
        public IReadOnlyList<DayCloseSection> Sections { get; init; } = Array.Empty<DayCloseSection>();
        public IReadOnlyList<DashboardPaymentRow> Payments { get; init; } = Array.Empty<DashboardPaymentRow>();
        public IReadOnlyList<DashboardTopItem> TopItems { get; init; } = Array.Empty<DashboardTopItem>();
        public int CancelledOrders { get; init; }

        /// <summary>Orders still unpaid right now. Only filled when the report is for today.</summary>
        public IReadOnlyList<HistoryRow> UnpaidOrders { get; init; } = Array.Empty<HistoryRow>();

        public DateTime? FirstBillLocal { get; init; }
        public DateTime? LastBillLocal { get; init; }
        public int PaidBills => Sections.Sum(section => section.Bills);
    }

    public sealed class DayCloseService
    {
        private readonly IDbContextFactory<ServeXDbContext> _contexts;
        private readonly OrderHistoryService _history;

        public DayCloseService(IDbContextFactory<ServeXDbContext> contexts, OrderHistoryService history)
        {
            _contexts = contexts;
            _history = history;
        }

        public async Task<DayCloseSummary> GetSummaryAsync(DateTime day)
        {
            var startUtc = DateTime.SpecifyKind(day.Date, DateTimeKind.Local).ToUniversalTime();
            var endUtc = DateTime.SpecifyKind(day.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime();

            using var context = _contexts.CreateDbContext();
            var locale = await context.Settings.AsNoTracking().OrderBy(setting => setting.Id)
                .Select(setting => setting.Locale).FirstOrDefaultAsync();

            var paid = await context.Bills.AsNoTracking()
                .Where(bill => bill.PaidAt.HasValue && bill.PaidAt.Value >= startUtc && bill.PaidAt.Value < endUtc)
                .Include(bill => bill.Order).ThenInclude(order => order.Items).ThenInclude(line => line.MenuItem)
                .ToListAsync();

            var cancelled = await context.Orders.AsNoTracking().CountAsync(order =>
                order.Status == OrderStatus.Cancelled &&
                (order.CancelledAt ?? order.CreatedAt) >= startUtc && (order.CancelledAt ?? order.CreatedAt) < endUtc);

            var sections = paid
                .GroupBy(bill => ReceiptProfile.CurrencyCode(bill.RecordedLocale))
                .OrderBy(group => group.Key)
                .Select(group => new DayCloseSection(
                    group.First().RecordedLocale ?? locale ?? "en-IN",
                    group.Count(),
                    group.Sum(bill => bill.Subtotal),
                    group.Sum(bill => bill.Discount),
                    group.Sum(bill => bill.Tax),
                    group.Sum(bill => bill.Total)))
                .ToList();

            var paidTimes = paid.Where(bill => bill.PaidAt.HasValue).Select(bill => bill.PaidAt.Value).ToList();
            var unpaid = day.Date == DateTime.Today
                ? await _history.GetOpenOrdersAsync()
                : new List<HistoryRow>();

            return new DayCloseSummary
            {
                Day = day.Date,
                Sections = sections,
                Payments = DashboardService.BuildPayments(paid, locale).ToList(),
                TopItems = DashboardService.BuildTopItems(paid).ToList(),
                CancelledOrders = cancelled,
                UnpaidOrders = unpaid,
                FirstBillLocal = paidTimes.Count == 0 ? null : BusinessTime.ToLocalTime(paidTimes.Min()),
                LastBillLocal = paidTimes.Count == 0 ? null : BusinessTime.ToLocalTime(paidTimes.Max())
            };
        }
    }
}
