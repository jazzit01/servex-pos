using System;

namespace ServeX.Services
{
    internal static class BusinessTime
    {
        public static DateTime ToLocalTime(DateTime storedUtc) =>
            DateTime.SpecifyKind(storedUtc, DateTimeKind.Utc).ToLocalTime();
    }
}
