using System;
using System.Drawing;
using System.Drawing.Printing;
using System.Runtime.Versioning;
using System.Windows.Controls;

namespace ServeX.Services
{
    internal sealed class ReceiptPrinterException : InvalidOperationException
    {
        public ReceiptPrinterException(string message) : base(message) { }
    }

    [SupportedOSPlatform("windows")]
    public class ReceiptPrintService
    {
        private readonly LogService _logService;
        public ReceiptPrintService(LogService logService) { _logService = logService; }

        public bool PrintReceipt(ReceiptData receipt)
        {
            try
            {
                if (receipt == null)
                {
                    throw new InvalidOperationException("There is no receipt data to print.");
                }

                if (PrinterSettings.InstalledPrinters.Count == 0)
                    throw new ReceiptPrinterException("No printers are installed. Add a printer in Windows and try again.");

                var dialog = new PrintDialog { UserPageRangeEnabled = false };
                if (dialog.ShowDialog() != true) return false;

                using var queue = dialog.PrintQueue;
                if (queue == null)
                    throw new ReceiptPrinterException("Choose a printer and try again.");
                queue.Refresh();
                if (queue.IsOffline)
                    throw new ReceiptPrinterException("The selected printer is offline. Reconnect it or select another printer.");

                using var printDocument = new PrintDocument();
                printDocument.PrinterSettings.PrinterName = queue.FullName;
                if (!printDocument.PrinterSettings.IsValid)
                    printDocument.PrinterSettings.PrinterName = queue.Name;
                if (!printDocument.PrinterSettings.IsValid)
                    throw new ReceiptPrinterException("Windows cannot access the selected printer. Select another printer.");
                printDocument.DocumentName = string.Format("ServeX Bill #{0}", receipt.OrderId);
                printDocument.DefaultPageSettings.Margins = new Margins(12, 12, 12, 12);
                var widthMm = receipt.PaperWidth == 58 ? 58 : 80;
                var width = (int)Math.Round(widthMm / 25.4 * 100);
                printDocument.DefaultPageSettings.PaperSize = new PaperSize($"Receipt{widthMm}mm", width, 1200);
                var culture = ReceiptProfile.Culture(receipt.Locale);
                string Money(decimal amount) => amount.ToString("C2", culture);

                using var headerFont = new Font("Segoe UI", 12, FontStyle.Bold);
                using var bodyFont = new Font("Segoe UI", 9);
                using var footerFont = new Font("Segoe UI", 9, FontStyle.Bold);
                var lines = new System.Collections.Generic.List<(string Text, Font Font, StringAlignment Alignment)>();
                void Line(string text, Font font = null, StringAlignment alignment = StringAlignment.Near)
                {
                    if (!string.IsNullOrWhiteSpace(text)) lines.Add((text, font ?? bodyFont, alignment));
                }
                void Rule() => lines.Add((null, bodyFont, StringAlignment.Near));

                Line(receipt.RestaurantName ?? "ServeX", headerFont, StringAlignment.Center);
                Line(receipt.RestaurantAddress, alignment: StringAlignment.Center);
                Line(receipt.RestaurantPhone, alignment: StringAlignment.Center);
                if (!string.IsNullOrWhiteSpace(receipt.TaxNumber))
                    Line($"{receipt.TaxName} number: {receipt.TaxNumber}", alignment: StringAlignment.Center);
                Line(BusinessTime.ToLocalTime(receipt.PaidAt).ToString("g", culture), alignment: StringAlignment.Center);
                Rule();
                Line($"Order #{receipt.OrderId} · {receipt.TableNumber}");
                foreach (var payment in receipt.Payments)
                {
                    Line($"{payment.Method}: {Money(payment.Amount)}");
                    if (!string.IsNullOrWhiteSpace(payment.Reference)) Line("Reference: " + payment.Reference);
                }
                Rule();
                foreach (var item in receipt.Items)
                {
                    Line(item.Name);
                    Line($"{item.Quantity} × {Money(item.UnitPrice)}   {Money(item.LineTotal)}");
                }
                Rule();
                Line($"Subtotal   {Money(receipt.Subtotal)}");
                var taxLabel = receipt.TaxRate.HasValue ? $"{receipt.TaxName} ({receipt.TaxRate:0.##}%)" : receipt.TaxName;
                Line($"{taxLabel}   {Money(receipt.Tax)}");
                Line($"Discount   {Money(receipt.Discount)}");
                Line(receipt.DiscountReason);
                Rule();
                Line($"TOTAL   {Money(receipt.Total)}", footerFont);
                Rule();
                Line("Thank you for dining with us", alignment: StringAlignment.Center);

                var nextLine = 0;
                printDocument.PrintPage += (_, args) =>
                {
                    var y = 0f;
                    var contentWidth = Math.Max(1, Math.Min(width - 24, args.MarginBounds.Width));
                    args.Graphics.TranslateTransform(args.MarginBounds.Left, args.MarginBounds.Top);
                    if (nextLine > 0) DrawWrapped(args.Graphics, $"Order #{receipt.OrderId} (continued)", bodyFont, ref y, contentWidth, StringAlignment.Near);

                    var firstLine = nextLine;
                    while (nextLine < lines.Count)
                    {
                        var line = lines[nextLine];
                        var height = line.Text == null ? 10f : args.Graphics.MeasureString(line.Text, line.Font, contentWidth).Height;
                        if (nextLine > firstLine && y + height > args.MarginBounds.Height) break;
                        if (line.Text == null) DrawLine(args.Graphics, ref y, contentWidth);
                        else DrawWrapped(args.Graphics, line.Text, line.Font, ref y, contentWidth, line.Alignment);
                        nextLine++;
                    }
                    args.HasMorePages = nextLine < lines.Count;
                };

                printDocument.Print();
                _logService.LogInfo(string.Format("Receipt printed for order #{0}", receipt.OrderId));
                return true;
            }
            catch (Exception exception)
            {
                _logService.LogError(
                    receipt == null
                        ? "Receipt printing failed"
                        : string.Format("Receipt printing failed for order #{0}", receipt.OrderId),
                    exception);
                throw;
            }
        }

        private static void DrawWrapped(Graphics graphics, string text, Font font, ref float y, int width, StringAlignment alignment)
        {
            using var format = new StringFormat { Alignment = alignment };
            var size = graphics.MeasureString(text, font, width, format);
            graphics.DrawString(text, font, Brushes.Black, new RectangleF(0, y, width, size.Height + 1), format);
            y += size.Height;
        }

        private static void DrawLine(Graphics graphics, ref float y, int width)
        {
            graphics.DrawLine(Pens.Black, 0, y + 4, width, y + 4);
            y += 10;
        }
    }
}
