using System;
using System.Collections.Generic;
using System.Linq;

namespace TestPlatform.TestSequences
{
    public static class Mps125GroupIds
    {
        public const string Prefix = "MPS125";
        public const string Precheck = Prefix + ".PRECHECK";
        public const string Cleanup = Prefix + ".CLEANUP";
        public static string Chapter(int chapter) => Prefix + ".CH" + chapter.ToString("00");
    }

    public static class Mps125ResultKeys
    {
        public static string For(string resultId) => "MPS125.RESULT." + resultId;
    }

    public enum Mps125ActionKind
    {
        CheckInstruments,
        AllOutputsOff,
        DaqRelays,
        CheckMbdCommunication,
        CloseMbdCommunication,
        MbdCommand,
        Delay,
        SetVbus,
        SetVbatt,
        SafeVbusRelay,
        SafeVbattConnect,
        SafeVbattDisconnect,
        DaqVoltage,
        DaqAverageVoltage,
        DaqShuntCurrent,
        DaqResistance,
        MbdNumeric,
        FirmwareVersion,
        IgnitionRamp,
        ComputedGain,
        DocumentedBoundary,
        Cleanup
    }

    public sealed class Mps125ActionDefinition
    {
        public int Chapter { get; set; }
        public string StepId { get; set; }
        public string Name { get; set; }
        public Mps125ActionKind Kind { get; set; }
        public string Argument { get; set; }
        public string ResultId { get; set; }
        public int Channel { get; set; }
        public double Scale { get; set; }
        public int SampleCount { get; set; } = 1;
        public int SampleIntervalMs { get; set; }
        public bool State { get; set; }
        public bool AlwaysRun { get; set; }
        public string[] RequiresResultIds { get; set; } = Array.Empty<string>();
        public bool IsResult => !string.IsNullOrWhiteSpace(ResultId);
    }

    public static partial class Mps125ActionCatalog
    {
        private static void AssignStableIds(List<Mps125ActionDefinition> items)
        {
            var chapterRows = new Dictionary<int, int>();
            int cleanupRow = 0;
            foreach (Mps125ActionDefinition item in items)
            {
                if (item.AlwaysRun)
                {
                    item.StepId = "MPS125.CLEANUP.S" + (++cleanupRow).ToString("000") + ".ACTION";
                    continue;
                }

                int row;
                chapterRows.TryGetValue(item.Chapter, out row);
                row++;
                chapterRows[item.Chapter] = row;
                item.StepId = "MPS125.CH" + item.Chapter.ToString("00") + ".S" +
                    row.ToString("000") + "." + (item.ResultId ?? "ACTION");
            }
        }

        private static void Require(List<Mps125ActionDefinition> items, string resultId, params string[] dependencies)
        {
            items.Find(x => string.Equals(x.ResultId, resultId, StringComparison.OrdinalIgnoreCase))
                .RequiresResultIds = dependencies;
        }
    }
}
