using System;
using System.Configuration;

namespace TestPlatform
{
    /// <summary>Shared presentation policy for all four SK test sequences.</summary>
    internal static class SkTestPolicy
    {
        public static bool UseCompactLogging(string boardType)
        {
            bool configured;
            if (bool.TryParse(ConfigurationManager.AppSettings["SKCompactLoggingEnabled"], out configured))
                return configured;

            // Preserve the old MPS setting for installations without the shared key.
            bool isMps = (boardType ?? string.Empty).StartsWith("MPS-", StringComparison.OrdinalIgnoreCase);
            return !isMps ||
                !bool.TryParse(ConfigurationManager.AppSettings["SKMPSCompactLoggingEnabled"], out configured) || configured;
        }
    }
}
