using System;
using System.Collections.Generic;

namespace TestPlatform.TestSequences
{
    public static class Bcm250GroupIds
    {
        public const string Prefix = "BCM250";
        public const string Precheck = Prefix + ".PRECHECK";
        public const string Cleanup = Prefix + ".CLEANUP";
        public const string StartupAndCommunication = Prefix + ".CH10-15";
        public static string Chapter(int chapter) => Prefix + ".CH" + chapter.ToString("00");

        public static string PlanGroupForChapter(int chapter)
        {
            return chapter >= 10 && chapter <= 15
                ? StartupAndCommunication
                : Chapter(chapter);
        }
    }

    public static class Bcm250Commands
    {
        public const string EnterMbd = "EXIT";
        public const string EnableDiagnostics = "DIAG ON";
        public const string EnterBcm = "REMOTE2";
        public const string StoreToFlash = "ACT->FLASH";
        public const string FirmwareFile = "BCM_250_V5.HEX";
        public const string FirmwareVersion = "stcu_UP250_V5";
    }

    public static class Bcm250ResultKeys
    {
        public static string For(string resultId) => "BCM250.RESULT." + resultId;
        public const string VbusReferenceMv = "BCM250.CAL.VBUS_REFERENCE_MV";
        public const string VmidHighMv = "BCM250.CAL.VMID_HIGH_MV";
        public const string VmidLowMv = "BCM250.CAL.VMID_LOW_MV";
        public const string DischargeOneAmpMa = "BCM250.CAL.IDCH_1A_MA";
        public const string DischargeSixAmpMa = "BCM250.CAL.IDCH_6A_MA";
        public const string ResetFlatRunningTimeBefore = "BCM250.RESET_FLAT.R10_BEFORE";
        public const string ResetFlatRunningTimeAfter = "BCM250.RESET_FLAT.R10_AFTER";

        public static string ShuntResistanceForChannel(int channel)
        {
            return "BCM250.SHUNT.DAQ" + channel + "_OHM";
        }
    }

    public enum Bcm250ActionKind
    {
        CheckInstruments,
        AllOutputsOff,
        PowerRelay,
        SafeAnsPowerOn,
        SafeAnsPowerOff,
        PowerRelays,
        FixtureRelays,
        DaqRelays,
        CheckMbdCommunication,
        FirmwareReadinessCheck,
        WaitForBcmRunningTime,
        WaitForBcmApplication,
        BcmSerialBatchBoundary,
        DocumentedBoundary,
        MbdCommand,
        Delay,
        WaitForCcbCurrentStable,
        ProgramFirmware,
        SetMpsPower,
        SetVbus,
        SetString,
        SetLoad,
        ShortSimulator,
        DaqVoltage,
        DaqShuntResistance,
        DaqShuntCurrent,
        MbdNumeric,
        FirmwareVersion,
        LogicResult,
        ComputedCalibration,
        MosfetRamp,
        StoreToFlash,
        WriteIdentity,
        Cleanup
    }

    public sealed class Bcm250ActionDefinition
    {
        public int Chapter { get; set; }
        public string StepId { get; set; }
        public string Name { get; set; }
        public Bcm250ActionKind Kind { get; set; }
        public string Argument { get; set; }
        public string ResultId { get; set; }
        public int Channel { get; set; }
        public double Scale { get; set; }
        public bool State { get; set; }
        public bool AlwaysRun { get; set; }
        public bool OnlyWhenFirmwareProgrammingRequired { get; set; }
        public bool OnlyAfterFirmwareProgramming { get; set; }
        public string[] RequiresResultIds { get; set; } = Array.Empty<string>();

        public bool IsResult => !string.IsNullOrWhiteSpace(ResultId);
    }

    /// <summary>
    /// Code-side action catalogue. It deliberately contains no limits, units,
    /// or wait durations; those are resolved from BCM-250_autoTest.xml.
    /// </summary>
    public static partial class Bcm250ActionCatalog
    {
        internal static IReadOnlyList<Bcm250ActionDefinition> CreateLegacy()
        {
            var items = new List<Bcm250ActionDefinition>();

            AddAction(items, 7, "检查全部仪器在线", Bcm250ActionKind.CheckInstruments);
            AddAction(items, 7, "关闭全部仪器输出并复位", Bcm250ActionKind.AllOutputsOff);
            AddAction(items, 7, "断开BCM250测试继电器", Bcm250ActionKind.FixtureRelays, Bcm250StationMap.AllBcm250Relays, false);

            AddAction(items, 8, "设置MPS电源275V/1A并输出", Bcm250ActionKind.SetVbus, "275,1,300", true);
            AddAction(items, 8, "闭合RL4给DUT供电", Bcm250ActionKind.PowerRelay, Bcm250StationMap.DutPowerRelay.ToString(), true);
            AddDaq(items, 8, "B01", "+5V", Bcm250StationMap.FiveVoltDaqChannel);
            AddDaq(items, 8, "B02", "+VCC", Bcm250StationMap.VccDaqChannel);
            AddDaq(items, 8, "B03", "-VEE", Bcm250StationMap.VeeDaqChannel);
            AddDaq(items, 8, "B04", "+3V3A", Bcm250StationMap.ThreeVoltThreeAnalogDaqChannel);
            AddDaq(items, 8, "B05", "+3V3-1", Bcm250StationMap.ThreeVoltThreeDaqChannel);
            AddShunt(items, 8, "B06", "+VCC电流", Bcm250StationMap.VccShuntDaqChannel, Bcm250StationMap.VccShuntOhms);
            AddShunt(items, 8, "B07", "+5V电流", Bcm250StationMap.FiveVoltShuntDaqChannel, Bcm250StationMap.FiveVoltShuntOhms);

            AddAction(items, 9, "检查编程接口", Bcm250ActionKind.CheckInstruments);
            AddAction(items, 9, "闭合TTL TX/RX/GND夹具继电器", Bcm250ActionKind.FixtureRelays, Bcm250StationMap.ProgrammingFixtureRelays, true);
            AddAction(items, 9, "RESET置位 W86=1", Bcm250ActionKind.MbdCommand, "W86=1");
            AddAction(items, 9, "闭合BOOT0 DAQ继电器", Bcm250ActionKind.DaqRelays, Bcm250StationMap.ProgrammingBootDaqRelay, true);
            AddAction(items, 9, "等待BOOT0稳定", Bcm250ActionKind.Delay);
            AddAction(items, 9, "RESET释放 W86=0", Bcm250ActionKind.MbdCommand, "W86=0");
            AddResult(items, 9, "C01", "烧录BCM_250_V5.HEX", Bcm250ActionKind.ProgramFirmware, Bcm250Commands.FirmwareFile);
            AddAction(items, 9, "断开BOOT0和TTL继电器", Bcm250ActionKind.DaqRelays, Bcm250StationMap.ProgrammingBootDaqRelay, false);

            AddTerminalHeader(items, 10);
            AddMbd(items, 10, "D01", "slave BCM CAN状态", "R14");
            AddMbd(items, 10, "D02", "slave SAFE CAN状态", "R15");
            AddTerminalHeader(items, 11);
            AddResult(items, 11, "E01", "BCM固件版本", Bcm250ActionKind.FirmwareVersion, "R0");

            AddTerminalHeader(items, 12);
            AddAction(items, 12, "读取复位前running_time", Bcm250ActionKind.MbdCommand, "R10");
            AddAction(items, 12, "通过W86执行平线复位", Bcm250ActionKind.MbdCommand, "W86=1;W86=0");
            AddAction(items, 12, "等待复位完成", Bcm250ActionKind.Delay);
            AddResult(items, 12, "F01", "Reset from flat cable", Bcm250ActionKind.LogicResult, "R10");

            AddTerminalHeader(items, 13);
            AddMbd(items, 13, "G01", "adc13 vref PC3", "R55");
            AddTerminalHeader(items, 14);
            AddMbd(items, 14, "H01", "adc11 vcc PC1", "R53");
            AddMbd(items, 14, "H02", "adc12 vee PC2", "R54");
            AddTerminalHeader(items, 15);
            AddMbd(items, 15, "I01", "adc10 hs_temp PC0", "R52");

            AddTerminalHeader(items, 16);
            AddAction(items, 16, "闭合VBUS校准功率路径", Bcm250ActionKind.FixtureRelays, Bcm250StationMap.PowerPathRelays, true);
            AddAction(items, 16, "设置VBUS 275V并读取R51/DAQ106", Bcm250ActionKind.SetVbus, "275,1,300", true);
            AddMbd(items, 16, "J01", "计算并写入Gain_VBUS W18", "R18");
            AddMbd(items, 16, "J02", "回读gain_vbus R18", "R18");
            AddMbd(items, 16, "J03", "回读offset_vstr_pos R19", "R19");
            AddMbd(items, 16, "J04", "回读vstr_pos R43", "R43");
            AddAction(items, 16, "保存VBUS校准参数", Bcm250ActionKind.StoreToFlash, Bcm250Commands.StoreToFlash);

            AddTerminalHeader(items, 17);
            AddMbdRange(items, 17, "K", 1, 6, new[] { "R61", "R61", "R61", "R81", "R61", "R81" }, "Westinghouse状态");
            AddTerminalHeader(items, 18);
            AddMbdRange(items, 18, "L", 1, 4, new[] { "R69", "R68", "R69", "R68" }, "预充继电器状态");

            AddTerminalHeader(items, 19);
            AddAction(items, 19, "闭合中点测量DAQ301", Bcm250ActionKind.DaqRelays, Bcm250StationMap.MidpointDaqRelay, true);
            AddAction(items, 19, "设置VMID高点125V", Bcm250ActionKind.SetVbus, "125,1,150", true);
            AddMbd(items, 19, "M01", "adc2 Vmid1 PA2 @125V", "R93");
            AddDaq(items, 19, "M02", "VMID_125V", Bcm250StationMap.VmidReferenceDaqChannel);
            AddAction(items, 19, "设置VMID低点5V", Bcm250ActionKind.SetVbus, "5,1,20", true);
            AddMbd(items, 19, "M03", "adc2 Vmid1 PA2 @5V", "R93");
            AddDaq(items, 19, "M04", "VMID_5V", Bcm250StationMap.VmidReferenceDaqChannel);
            AddMbd(items, 19, "M05", "计算Gain_VMID并写W20", "R20");
            AddMbd(items, 19, "M06", "计算offset_vstr_mid并写W21", "R21");
            AddMbd(items, 19, "M07", "回读Gain_VMID", "R20");
            AddMbd(items, 19, "M08", "回读offset_vstr_mid", "R21");
            AddMbd(items, 19, "M09", "回读vstr_mid1", "R92");

            AddTerminalHeader(items, 20);
            AddAction(items, 20, "闭合放电校准功率路径", Bcm250ActionKind.FixtureRelays, Bcm250StationMap.PowerPathRelays, true);
            AddAction(items, 20, "设置String 1A工作点", Bcm250ActionKind.SetString, "14,1,20", true);
            AddMbd(items, 20, "N01", "adc15 @1A", "R57");
            AddMbd(items, 20, "N02", "idch @1A", "R45");
            AddAction(items, 20, "设置String 6A工作点", Bcm250ActionKind.SetString, "14,6,20", true);
            AddMbd(items, 20, "N03", "adc15 @6A", "R57");
            AddMbd(items, 20, "N04", "idch @6A", "R45");
            AddMbd(items, 20, "N05", "计算GAINidch并写W22", "R22");
            AddMbd(items, 20, "N06", "计算OFFSETidch并写W23", "R23");
            AddMbd(items, 20, "N07", "idch @0A", "R45");
            AddMbd(items, 20, "N08", "idch @6A复核", "R45");

            AddTerminalHeader(items, 21);
            AddAction(items, 21, "缓升String电流观察自动MOS动作", Bcm250ActionKind.SetString, "14,1.2,20", true);
            AddMbd(items, 21, "O01", "自动放电MOS动作电流", "R45");
            AddMbd(items, 21, "O02", "st_gm_sc PB13翻转", "R59");

            AddTerminalHeader(items, 22);
            AddAction(items, 22, "短路模拟器输出3.3V", Bcm250ActionKind.ShortSimulator, "3.3V", true);
            AddMbdRange(items, 22, "P", 1, 4, new[] { "R61", "R60", "R61", "R60" }, "短路保护状态");

            AddTerminalHeader(items, 23);
            AddAction(items, 23, "闭合充电/VSTR_NEG校准路径", Bcm250ActionKind.FixtureRelays, Bcm250StationMap.PowerPathRelays, true);
            AddAction(items, 23, "建立0V/0A、15V/0A、0V/6A校准点", Bcm250ActionKind.SetString, "calibration-points", true);
            AddMbdRange(items, 23, "Q", 1, 16,
                new[] { "R50", "R56", "DAQ109", "R44", "R50", "R56", "DAQ109", "R50", "R56", "R44", "DAQ109", "R24", "R25", "R26", "R27", "R28" },
                "充电电流及VSTR_NEG校准");

            AddTerminalHeader(items, 24);
            AddAction(items, 24, "依次建立四组验证工作点", Bcm250ActionKind.SetString, "0V0A;0V6A;15V0A;15V6A", true);
            AddMbdRange(items, 24, "R", 1, 14,
                new[] { "R42", "R44", "DAQ109", "R42", "R44", "R45", "DAQ109", "R42", "R44", "DAQ109", "R42", "R44", "DAQ109", "R45" },
                "充电电流及VSTR_NEG验证");

            AddTerminalHeader(items, 25);
            AddAction(items, 25, "启用CCB并建立6A/261V工作点", Bcm250ActionKind.SetString, "261,6,280", true);
            AddMbd(items, 25, "S01", "CCB 6A电流", "R44");
            AddDaq(items, 25, "S02", "V2_261000mV（按详细步骤修正）", Bcm250StationMap.V2DaqChannel);

            AddTerminalHeader(items, 26);
            AddAction(items, 26, "打开mos_test_uc PA3", Bcm250ActionKind.MbdCommand, "W71=1");
            AddMbd(items, 26, "T01", "内部iccb（mos_test_uc=1）", "R44");
            AddShunt(items, 26, "T02", "外部分流电流【待实机确认极性】", Bcm250StationMap.StringShuntDaqChannel, Bcm250StationMap.StringShuntOhms);
            AddAction(items, 26, "关闭mos_test_uc PA3", Bcm250ActionKind.MbdCommand, "W71=0");
            AddMbd(items, 26, "T03", "内部iccb（mos_test_uc=0）", "R44");

            AddTerminalHeader(items, 27);
            AddResult(items, 27, "U01", "ACT->FLASH响应", Bcm250ActionKind.StoreToFlash, Bcm250Commands.StoreToFlash);
            AddResult(items, 27, "U02", "写入并回读data_calibration", Bcm250ActionKind.WriteIdentity, "R40");
            AddResult(items, 27, "U03", "写入并回读bcm_serial", Bcm250ActionKind.WriteIdentity, "R41");

            AddCleanup(items, "关闭RL4 DUT电源", Bcm250ActionKind.PowerRelay, Bcm250StationMap.DutPowerRelay.ToString());
            AddCleanup(items, "关闭全部电源和电子负载输出", Bcm250ActionKind.AllOutputsOff);
            AddCleanup(items, "断开全部BCM250夹具继电器", Bcm250ActionKind.FixtureRelays, Bcm250StationMap.AllBcm250Relays);
            AddCleanup(items, "断开DAQ测试继电器", Bcm250ActionKind.DaqRelays, "301,302,304,305");
            AddCleanup(items, "关闭目标板通信并释放治具", Bcm250ActionKind.Cleanup);

            Require(items, "J02", "J01");
            Require(items, "M05", "M01", "M02", "M03", "M04");
            Require(items, "M06", "M05");
            Require(items, "M07", "M05");
            Require(items, "M08", "M06");
            Require(items, "M09", "M07", "M08");
            Require(items, "N05", "N01", "N02", "N03", "N04");
            Require(items, "N06", "N01", "N02", "N03", "N04");
            Require(items, "N07", "N05", "N06");
            Require(items, "N08", "N05", "N06");
            Require(items, "Q12", "Q01", "Q03", "Q05", "Q07", "Q08", "Q10", "Q11");
            Require(items, "Q13", "Q12");
            Require(items, "Q14", "Q02", "Q06", "Q09", "Q10");
            Require(items, "Q15", "Q12", "Q14");
            Require(items, "Q16", "Q14", "Q15");
            AssignStableIds(items);
            return items;
        }

        private static void Require(List<Bcm250ActionDefinition> items, string resultId, params string[] dependencies)
        {
            items.Find(x => string.Equals(x.ResultId, resultId, StringComparison.OrdinalIgnoreCase))
                .RequiresResultIds = dependencies;
        }

        private static void AddTerminalHeader(List<Bcm250ActionDefinition> items, int chapter)
        {
            AddAction(items, chapter, "进入MBD终端", Bcm250ActionKind.MbdCommand, Bcm250Commands.EnterMbd);
            AddAction(items, chapter, "开启诊断模式", Bcm250ActionKind.MbdCommand, Bcm250Commands.EnableDiagnostics);
            AddAction(items, chapter, "通过REMOTE2进入BCM250终端", Bcm250ActionKind.MbdCommand, Bcm250Commands.EnterBcm);
        }

        private static void AddMbdRange(List<Bcm250ActionDefinition> items, int chapter, string prefix, int first, int last, string[] commands, string description)
        {
            for (int number = first; number <= last; number++)
                AddMbd(items, chapter, prefix + number.ToString("00"), description, commands[number - first]);
        }

        private static void AddMbd(List<Bcm250ActionDefinition> items, int chapter, string resultId, string name, string command)
        {
            AddResult(items, chapter, resultId, name, Bcm250ActionKind.MbdNumeric, command);
        }

        private static void AddDaq(List<Bcm250ActionDefinition> items, int chapter, string resultId, string name, int channel)
        {
            AddResult(items, chapter, resultId, name, Bcm250ActionKind.DaqVoltage, channel: channel);
        }

        private static void AddShunt(List<Bcm250ActionDefinition> items, int chapter, string resultId, string name, int channel, double shuntOhms)
        {
            AddResult(items, chapter, resultId, name, Bcm250ActionKind.DaqShuntCurrent, channel: channel, scale: shuntOhms);
        }

        private static void AddResult(List<Bcm250ActionDefinition> items, int chapter, string resultId, string name, Bcm250ActionKind kind, string argument = null, int channel = 0, double scale = 0)
        {
            items.Add(new Bcm250ActionDefinition
            {
                Chapter = chapter,
                Name = resultId + " " + name,
                Kind = kind,
                Argument = argument,
                ResultId = resultId,
                Channel = channel,
                Scale = scale,
                State = true
            });
        }

        private static void AddAction(List<Bcm250ActionDefinition> items, int chapter, string name, Bcm250ActionKind kind, string argument = null, bool state = true)
        {
            items.Add(new Bcm250ActionDefinition { Chapter = chapter, Name = chapter + ". " + name, Kind = kind, Argument = argument, State = state });
        }

        private static void AddCleanup(List<Bcm250ActionDefinition> items, string name, Bcm250ActionKind kind, string argument = null)
        {
            items.Add(new Bcm250ActionDefinition { Chapter = 0, Name = "CLEANUP " + name, Kind = kind, Argument = argument, State = false, AlwaysRun = true });
        }

        private static void AssignStableIds(List<Bcm250ActionDefinition> items)
        {
            var chapterRows = new Dictionary<int, int>();
            int cleanupRow = 0;
            foreach (Bcm250ActionDefinition item in items)
            {
                if (!string.IsNullOrWhiteSpace(item.StepId))
                    continue;

                if (item.AlwaysRun)
                {
                    item.StepId = "BCM250.CLEANUP.S" + (++cleanupRow).ToString("000") + ".ACTION";
                    continue;
                }

                int row;
                chapterRows.TryGetValue(item.Chapter, out row);
                row++;
                chapterRows[item.Chapter] = row;
                item.StepId = "BCM250.CH" + item.Chapter.ToString("00") + ".S" + row.ToString("000") + "." + (item.ResultId ?? "ACTION");
            }
        }
    }
}
