namespace ServeX.Models
{
    public enum OrderStatus
    {
        Open = 1,
        Preparing = 2,
        Served = 3,
        Billed = 4,
        Paid = 5,
        Cancelled = 6,
        Merged = 7
    }
}
