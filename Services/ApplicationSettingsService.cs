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
        private readonly IDbContextFactory<ServeXDbContext> _contexts;
        private readonly OperatorSession _session;
        public ApplicationSettingsService(IDbContextFactory<ServeXDbContext> contexts, LogService logService, OperatorSession session)
        {
            _contexts = contexts;
            _session = session;
            _logService = logService;
        }

        private readonly LogService _logService;

        public async Task<AppSetting> GetSettingsAsync()
        {
            try
            {
                using (var context = _contexts.CreateDbContext())
                {
                    var settings = await context.Settings
                        .AsNoTracking()
                        .OrderBy(setting => setting.Id)
                        .FirstOrDefaultAsync();

                    return settings;
                }
            }
            catch (Exception exception)
            {
                _logService.LogError("Loading application settings failed", exception);
                throw;
            }
        }

        public async Task<decimal> GetTaxPercentageAsync()
        {
            using var context = _contexts.CreateDbContext();
            return await OrderPricing.GetTaxPercentageAsync(context);
        }

        public async Task<decimal> GetTaxRateAsync()
        {
            var taxPercentage = await GetTaxPercentageAsync();
            return taxPercentage / 100m;
        }

        public async Task<AppSetting> UpdateSettingsAsync(string restaurantName, string phone, decimal taxPercentage, string address = null, ReceiptProfile receipt = null)
        {
            _session.RequireManager();
            if (string.IsNullOrWhiteSpace(restaurantName))
            {
                throw new ArgumentException("Restaurant name is required.", nameof(restaurantName));
            }

            if (string.IsNullOrWhiteSpace(phone))
            {
                throw new ArgumentException("Phone number is required.", nameof(phone));
            }

            if (address != null && (string.IsNullOrWhiteSpace(address) || address.Trim().Length > 250))
                throw new ArgumentException("Enter a restaurant address up to 250 characters.", nameof(address));

            if (receipt != null && (!ReceiptProfile.Locales.Contains(receipt.Locale) ||
                string.IsNullOrWhiteSpace(receipt.TaxName) || receipt.TaxName.Trim().Length > 24 ||
                receipt.TaxNumber?.Trim().Length > 48 || receipt.PaperWidth is not (58 or 80)))
                throw new ArgumentException("Check the currency locale, tax details and receipt width.", nameof(receipt));

            if (taxPercentage < 0m || taxPercentage > 100m)
            {
                throw new ArgumentOutOfRangeException(nameof(taxPercentage), "Tax percentage must be between 0 and 100.");
            }

            try
            {
                using (var context = _contexts.CreateDbContext())
                {
                    var settings = await context.Settings
                        .OrderBy(setting => setting.Id)
                        .FirstOrDefaultAsync();

                    if (settings == null)
                    {
                        throw new InvalidOperationException("ServeX setup is incomplete. Settings were not saved.");
                    }

                    settings.RestaurantName = restaurantName.Trim();
                    settings.Phone = phone.Trim();
                    if (address != null) settings.Address = address.Trim();
                    settings.TaxPercentage = taxPercentage;
                    if (receipt != null)
                    {
                        settings.Locale = receipt.Locale;
                        settings.TaxName = receipt.TaxName.Trim();
                        settings.TaxNumber = receipt.TaxNumber?.Trim();
                        settings.ReceiptWidth = receipt.PaperWidth;
                    }

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
