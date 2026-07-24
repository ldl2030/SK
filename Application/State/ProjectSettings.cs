using System.Collections.Generic;
using System.Data;

namespace TestPlatform
{
    public static class ProjectSettings
    {
        public static DataTable testDataTable;

        public static string TestFikePath { get; set; }

        public static string CurrentProjectName { get; set; }

        public static bool TestResult { get; set; }

        public static bool IndexRows { get; set; }

        public static string MesSN { get; set; }

        public static string PrintRefSN { get; set; }

        public static List<ChannelContext> Channels { get; set; } = new List<ChannelContext>();

        private static readonly HashSet<string> UsedSerialNumbers = new HashSet<string>();
        private static readonly object SerialNumberLock = new object();

        public static bool IsSNUsed(string sn)
        {
            lock (SerialNumberLock)
            {
                return UsedSerialNumbers.Contains(sn);
            }
        }

        public static void AddSN(string sn)
        {
            lock (SerialNumberLock)
            {
                UsedSerialNumbers.Add(sn);
            }
        }

        public static void RemoveSN(string sn)
        {
            lock (SerialNumberLock)
            {
                UsedSerialNumbers.Remove(sn);
            }
        }

        public static string LastSavedLogHash = string.Empty;
    }
}
