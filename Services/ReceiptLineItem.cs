namespace ServeX.Services
{
    public class ReceiptLineItem
    {
        public string Name { get; set; }

        public int Quantity { get; set; }

        public decimal UnitPrice { get; set; }

        public decimal LineTotal { get; set; }
    }
}
