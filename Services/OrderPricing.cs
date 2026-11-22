using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ServeX.Data;

namespace ServeX.Services
{
    internal static class OrderPricing
    {
        public static async Task<decimal> GetTaxPercentageAsync(ServeXDbContext context) =>
            await context.Settings.AsNoTracking().OrderBy(setting => setting.Id)
                .Select(setting => (decimal?)setting.TaxPercentage).FirstOrDefaultAsync() ?? 5m;

        public static decimal Round(decimal amount) => decimal.Round(amount, 2);

        public static decimal Tax(decimal subtotal, decimal percentage) =>
            Round(subtotal * (percentage / 100m));
    }
}
