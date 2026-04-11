using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ServeX.Data;
using ServeX.Models;

namespace ServeX.Services
{
    public class ApplicationSettingsService
    {
        public async Task<AppSetting> GetSettingsAsync()
        {
            using (var context = new ServeXDbContext())
            {
                return await context.Settings
                    .AsNoTracking()
                    .OrderBy(setting => setting.Id)
                    .FirstOrDefaultAsync();
            }
        }

        public async Task<decimal> GetTaxPercentageAsync()
        {
            using (var context = new ServeXDbContext())
            {
                var taxPercentage = await context.Settings
                    .AsNoTracking()
                    .Select(setting => (decimal?)setting.TaxPercentage)
                    .FirstOrDefaultAsync();

                return taxPercentage ?? 5m;
            }
        }

        public async Task<decimal> GetTaxRateAsync()
        {
            var taxPercentage = await GetTaxPercentageAsync();
            return taxPercentage / 100m;
        }
    }
}
