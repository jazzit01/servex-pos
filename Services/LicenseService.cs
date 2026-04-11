using System;
using System.Text.RegularExpressions;

namespace ServeX.Services
{
    public class LicenseService
    {
        private static readonly Regex LicensePattern = new Regex(
            "^SERVEX-[A-Z0-9]{4,}(-[A-Z0-9]{4,}){1,3}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public string Normalize(string licenseKey)
        {
            return (licenseKey ?? string.Empty).Trim().ToUpperInvariant();
        }

        public bool IsValid(string licenseKey)
        {
            var normalized = Normalize(licenseKey);
            return LicensePattern.IsMatch(normalized);
        }

        public string GetValidationMessage()
        {
            return "Enter a valid license key in the format SERVEX-DEMO-2026.";
        }
    }
}
