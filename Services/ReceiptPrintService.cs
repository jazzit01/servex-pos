using System;
using System.Drawing;
using System.Drawing.Printing;
using System.Runtime.Versioning;

namespace ServeX.Services
{
    [SupportedOSPlatform("windows")]
    public class ReceiptPrintService
    {
        private readonly LogService _logService;
        public ReceiptPrintService(LogService logService) { _logService = logService; }

        public void PrintReceipt(ReceiptData receipt)
        {
            try
            {
                if (receipt == null)
                {
                    throw new InvalidOperationException("There is no receipt data to print.");
                }

                using var printDocument = new PrintDocument();
                printDocument.DocumentName = string.Format("ServeX Bill #{0}", receipt.OrderId);
                printDocument.DefaultPageSettings.Margins = new Margins(12, 12, 12, 12);
                printDocument.DefaultPageSettings.PaperSize = new PaperSize("Receipt80mm", 314, 1200);

                printDocument.PrintPage += (sender, args) =>
                {
                    using (var headerFont = new Font("Consolas", 12, FontStyle.Bold))
                    using (var bodyFont = new Font("Consolas", 9, FontStyle.Regular))
                    using (var footerFont = new Font("Consolas", 9, FontStyle.Bold))
                    {
                        var y = 10f;
                        DrawCentered(args.Graphics, "SERVEX", headerFont, ref y);
                        DrawCentered(args.Graphics, "Simple Receipt", bodyFont, ref y);
                        DrawCentered(args.Graphics, receipt.PaidAt.ToLocalTime().ToString("dd MMM yyyy hh:mm tt"), bodyFont, ref y);
                        y += 6f;

                        DrawLine(args.Graphics, ref y);
                        DrawText(args.Graphics, string.Format("Order: #{0}", receipt.OrderId), bodyFont, ref y);
                        DrawText(args.Graphics, string.Format("Table: {0}", receipt.TableNumber), bodyFont, ref y);
                        DrawText(args.Graphics, string.Format("Payment: {0}", receipt.PaymentMethod), bodyFont, ref y);
                        DrawLine(args.Graphics, ref y);

                        foreach (var item in receipt.Items)
                        {
                            DrawText(args.Graphics, item.Name, bodyFont, ref y);
                            DrawText(args.Graphics, string.Format("{0} x {1:N2}   {2:N2}", item.Quantity, item.UnitPrice, item.LineTotal), bodyFont, ref y);
                            y += 3f;
                        }

                        DrawLine(args.Graphics, ref y);
                        DrawText(args.Graphics, string.Format("Subtotal   {0:N2}", receipt.Subtotal), bodyFont, ref y);
                        DrawText(args.Graphics, string.Format("Tax        {0:N2}", receipt.Tax), bodyFont, ref y);
                        DrawText(args.Graphics, string.Format("Discount   {0:N2}", receipt.Discount), bodyFont, ref y);
                        DrawLine(args.Graphics, ref y);
                        DrawText(args.Graphics, string.Format("TOTAL      {0:N2}", receipt.Total), footerFont, ref y);
                        DrawLine(args.Graphics, ref y);
                        DrawCentered(args.Graphics, "Thank you for dining with us", bodyFont, ref y);
                        DrawCentered(args.Graphics, "Powered by ServeX", bodyFont, ref y);
                    }
                    args.HasMorePages = false;
                };

                printDocument.Print();
                _logService.LogInfo(string.Format("Receipt printed for order #{0}", receipt.OrderId));
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

        private static void DrawText(Graphics graphics, string text, Font font, ref float y)
        {
            graphics.DrawString(text, font, Brushes.Black, 0, y);
            y += graphics.MeasureString(text, font).Height;
        }

        private static void DrawCentered(Graphics graphics, string text, Font font, ref float y)
        {
            var size = graphics.MeasureString(text, font);
            var x = Math.Max(0, (290f - size.Width) / 2f);
            graphics.DrawString(text, font, Brushes.Black, x, y);
            y += size.Height;
        }

        private static void DrawLine(Graphics graphics, ref float y)
        {
            const string separator = "--------------------------------";
            graphics.DrawString(separator, SystemFonts.DefaultFont, Brushes.Black, 0, y);
            y += graphics.MeasureString(separator, SystemFonts.DefaultFont).Height;
        }
    }
}
