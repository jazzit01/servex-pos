using System;
using System.Collections.Generic;
using ServeX.Models;

namespace ServeX.Services
{
    public class ReceiptData
    {
        public int OrderId { get; set; }

        public string RestaurantName { get; set; }
        public string RestaurantAddress { get; set; }
        public string RestaurantPhone { get; set; }

        public string TableNumber { get; set; }

        public PaymentMethod PaymentMethod { get; set; }

        public DateTime PaidAt { get; set; }

        public decimal Subtotal { get; set; }

        public decimal Tax { get; set; }

        public decimal Discount { get; set; }

        public decimal Total { get; set; }

        public IList<ReceiptLineItem> Items { get; set; }
    }
}
