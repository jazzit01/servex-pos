using System;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace ServeX.Services
{
    [SupportedOSPlatform("windows")]
    public sealed class KitchenWorkspaceService
    {
        private readonly KitchenService _kitchen;
        public KitchenWorkspaceService(KitchenService kitchen) { _kitchen = kitchen; }

        public async Task<(int Revision, string Message)> SendAsync(int orderId, int revision)
        {
            var ticket = await _kitchen.GetTicketAsync(orderId, revision);
            if (ticket.Lines.Count == 0) return (revision, "There are no new items to send.");
            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true) return (revision, "Kitchen ticket printing cancelled.");
            var page = new FlowDocument { PagePadding = new Thickness(16), ColumnWidth = double.PositiveInfinity };
            page.Blocks.Add(new Paragraph(new Run($"Kitchen ticket #{ticket.OrderId}")));
            page.Blocks.Add(new Paragraph(new Run($"{ticket.Reference} | {ticket.CreatedLocal:g}")));
            foreach (var line in ticket.Lines)
                page.Blocks.Add(new Paragraph(new Run($"{line.Quantity} × {line.Name}")));
            dialog.PrintDocument(((IDocumentPaginatorSource)page).DocumentPaginator, "ServeX kitchen ticket");

            try { return (await _kitchen.MarkTicketSentAsync(ticket), "Kitchen ticket sent. Order is preparing."); }
            catch (Exception exception)
            {
                throw new InvalidOperationException("The ticket was submitted, but the order changed. Check with the kitchen before printing again.", exception);
            }
        }

        public Task<int> MarkServedAsync(int orderId, int revision) => _kitchen.MarkServedAsync(orderId, revision);
    }
}
