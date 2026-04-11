using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ServeX.Data;
using ServeX.Models;

namespace ServeX.Services
{
    public class ApplicationSettingsService
    {
        private readonly LogService _logService = new LogService();

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

        public async Task<AppSetting> UpdateSettingsAsync(string restaurantName, string phone, decimal taxPercentage)
        {
            if (string.IsNullOrWhiteSpace(restaurantName))
            {
                throw new ArgumentException("Restaurant name is required.", nameof(restaurantName));
            }

            if (string.IsNullOrWhiteSpace(phone))
            {
                throw new ArgumentException("Phone number is required.", nameof(phone));
            }

            if (taxPercentage < 0m || taxPercentage > 100m)
            {
                throw new ArgumentOutOfRangeException(nameof(taxPercentage), "Tax percentage must be between 0 and 100.");
            }

            try
            {
                using (var context = new ServeXDbContext())
                {
                    var settings = await context.Settings
                        .OrderBy(setting => setting.Id)
                        .FirstOrDefaultAsync();

                    if (settings == null)
                    {
                        throw new InvalidOperationException("Restaurant settings have not been created yet.");
                    }

                    settings.RestaurantName = restaurantName.Trim();
                    settings.Phone = phone.Trim();
                    settings.TaxPercentage = taxPercentage;

                    await context.SaveChangesAsync();

                    _logService.LogInfo("Application settings updated");

                    return settings;
                }
            }
            catch (Exception exception)
            {
                _logService.LogError("Updating application settings failed", exception);
                throw;
            }
        }
    }
}
