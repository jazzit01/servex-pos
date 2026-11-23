using System;
using System.Globalization;
using System.Linq;

namespace ServeX.Services
{
    public sealed record ReceiptProfile(string Locale, string TaxName, string TaxNumber, int PaperWidth)
    {
        public static readonly string[] Locales = { "en-IN", "en-US", "en-GB", "en-CA", "en-AU", "en-AE", "de-DE", "fr-FR" };
        public static string CurrencyCode(string locale) => new RegionInfo(Culture(locale).Name).ISOCurrencySymbol;

        public static string FormatAmount(decimal amount, string locale) => amount.ToString("C2", Culture(locale));

        public static CultureInfo Culture(string locale) =>
            CultureInfo.GetCultureInfo(Locales.Contains(locale, StringComparer.OrdinalIgnoreCase) ? locale : "en-IN");
    }
}
