using ServeX.Models;

namespace ServeX.Services
{
    public sealed record PaymentSplit(decimal FirstAmount, PaymentMethod SecondMethod, string SecondReference);
    public sealed record PaymentAllocation(decimal Amount, PaymentMethod Method, string Reference);
    public sealed record DiscountOptions(bool IsPercentage, string Reason, string ManagerPassword);
}
