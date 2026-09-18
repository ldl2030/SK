using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using TestPlatform;
using TestPlatform.TestSequences;

namespace Mps125StructureTests
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                string root = args.Length > 0 ? Path.GetFullPath(args[0]) : FindRoot(AppDomain.CurrentDomain.BaseDirectory);
                string xml = Path.Combine(root, "ProjectConfig", "SK", "MPS-125", "MPS-125_autoTest.xml");
                ResolvedTestPlan plan = TestPlanService.Resolve(xml, TestProfileIds.Normal);
                var context = new TestSequenceContext { TestPlan = plan, SN = "MPS125-SIM-0001" };
                var device = new SK441Device { SkipComInit = true, SimulationBoardType = "MPS-125" };
                IList steps = BuildSteps(context, device);

                ValidateStructure(plan, steps);
                ValidateCatalog();
                ValidateChapterCommunicationClose(context, device);
                ValidateSafetyGates(steps);
                ValidateConfiguredAverageSampleCounts();
                ValidatePowerSafety();
                RunEndToEnd(xml, false);
                RunEndToEnd(xml, true);
                RunSafetyGateFault(xml, "MPS125.CH15.P011.ACTION", "MPS125.CH15.P012.ACTION");
                RunSafetyGateFault(xml, "MPS125.CH16.S005.ACTION", "MPS125.CH16.S006.K02");
                RunGlobalResultFailureContinue(xml, "MPS125.CH16.S003.K01", "MPS125.CH16.S004.ACTION");
                RunGlobalResultFailureContinue(xml, "MPS125.CH17.S014.L04", "MPS125.CH17.S015.ACTION");

                Console.WriteLine("PASS MPS125: 231 rows, two Chapter 15 25-137.5 V/200 ms VBUS ramps, direct nominal startup in Chapters 7/9/10, 70 PDF results, USB-B recovery, full simulation and fault cleanup.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL MPS125: " + Unwrap(ex).Message);
                return 1;
            }
        }

        private static IList BuildSteps(TestSequenceContext context, SK441Device device)
        {
            var sequence = new SKMPS125Sequence();
            Type type = sequence.GetType();
            object steps = FindMethod(type, "BuildCommonSteps").Invoke(sequence, new object[] { context, device });
            FindMethod(type, "AddElectricalTestSteps").Invoke(sequence, new object[] { steps, context, device });
            FindMethod(type, "BindStableStepIds").Invoke(null, new[] { context, steps });
            return (IList)steps;
        }

        private static void ValidateStructure(ResolvedTestPlan plan, IList steps)
        {
            List<TestPlanStepDefinition> definitions = plan.Definition.Steps.OrderBy(x => x.SequenceOrder).ToList();
            Require(steps.Count == 231, "Expected 231 rows, got " + steps.Count + ".");
            Require(definitions.Count == steps.Count, "Code/XML row count mismatch.");
            Require(definitions.Select(x => x.StepId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 231, "StepIds are not unique.");
            Require(definitions.Any(x => x.StepId == "MPS125.PRECHECK.S009.ACTION"), "MPS-125 USB-B insertion row is missing.");
            Require(definitions.Count(x => x.AlwaysRun) == 6, "Expected six cleanup rows.");
            Require(definitions.Count(x => x.GroupId == Mps125GroupIds.Chapter(7)) == 12, "Chapter 7 must use direct nominal VBUS startup.");
            Require(definitions.Count(x => x.GroupId == Mps125GroupIds.Chapter(9)) == 10, "Chapter 9 must use direct nominal VBUS startup.");
            Require(definitions.Count(x => x.GroupId == Mps125GroupIds.Chapter(10)) == 10, "Chapter 10 must use direct nominal VBUS startup.");
            Require(definitions.Count(x => x.GroupId == Mps125GroupIds.Chapter(15)) == 42, "Chapter 15 must expose both relay-separated VBUS ramps.");
            Require(definitions.Count(x => x.GroupId == Mps125GroupIds.Chapter(17)) == 42, "Chapter 17 must expose 42 detailed rows after removing nonexistent Relay_1.");

            var resultIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Mps125ActionDefinition item in Mps125ActionCatalog.Create().Where(x => x.IsResult))
                Require(resultIds.Add(item.ResultId), "Duplicate result ID " + item.ResultId + ".");
            Require(resultIds.Count == 70, "Expected 70 PDF result IDs.");

            foreach (object step in steps)
            {
                Type type = step.GetType();
                string stepId = Read<string>(type, step, "StepId");
                TestPlanStepDefinition definition = Read<TestPlanStepDefinition>(type, step, "PlanDefinition");
                Require(definition != null, "Missing XML definition " + stepId + ".");
                Require(string.Equals(Read<string>(type, step, "ExpectedGroupId"), definition.GroupId, StringComparison.OrdinalIgnoreCase), "Group mismatch " + stepId + ".");
                Require(string.Equals(Read<string>(type, step, "Name"), definition.Name, StringComparison.Ordinal), "Name mismatch " + stepId + ".");
            }

            ValidateLimit(plan, "B06", 19, 60, "mA");
            ValidateLimit(plan, "K01", 1700, 2200, "mV");
            ValidateLimit(plan, "K02", 3000, 3300, "mV");
            ValidateLimit(plan, "K03", 400, 800, "mV");
            ValidateOpenUpperLimit(plan, "L01", 0, "mV");
            ValidateOpenUpperLimit(plan, "L05", 0, "mV");
            ValidateOpenUpperLimit(plan, "L09", 0, "mV");
            ValidateOpenUpperLimit(plan, "L13", 0, "mV");
            Require(
                !MbdResponseParser.IsSuccessfulCommandResponse(
                    "W99=0",
                    "DIAGNOSTIC MODE is OFF.\rReady.\r",
                    out string diagnosticModeError) &&
                diagnosticModeError == "DIAGNOSTIC MODE IS OFF",
                "MBD diagnostic-mode-off response must fail a write command.");
            ValidateLimit(plan, "J08", 200000, 205000, "Count");
            Require(plan.Definition.Steps.Single(x => x.StepId.EndsWith(".D03", StringComparison.OrdinalIgnoreCase)).ExpectedValue == "mcu_app U.2.2.2", "Firmware expectation mismatch.");
            ValidateExpectedValue(plan, "MPS125.CH15.P001.ACTION", "0,0,300");
            ValidateExpectedValue(plan, "MPS125.CH15.P003.ACTION", "0,0,300");
            ValidateExpectedValue(plan, "MPS125.CH07.S001.ACTION", "137.5,2,300");
            ValidateExpectedValue(plan, "MPS125.CH07.S003.ACTION", "137.5,2,300");
            ValidateExpectedValue(plan, "MPS125.CH09.S001.ACTION", "137.5,2,300");
            ValidateExpectedValue(plan, "MPS125.CH09.S002.ACTION", "137.5,2,300");
            ValidateExpectedValue(plan, "MPS125.CH10.S002.ACTION", "137.5,2,300");
            ValidateExpectedValue(plan, "MPS125.CH10.S003.ACTION", "137.5,2,300");
            ValidateRampRows(plan, new[]
            {
                "MPS125.CH15.P002.ACTION", "MPS125.CH15.P009.ACTION", "MPS125.CH15.P010.ACTION",
                "MPS125.CH15.P011.ACTION", "MPS125.CH15.P012.ACTION", "MPS125.CH15.P013.ACTION"
            });
            ValidateRampRows(plan, new[]
            {
                "MPS125.CH15.P004.ACTION", "MPS125.CH15.P014.ACTION", "MPS125.CH15.P015.ACTION",
                "MPS125.CH15.P016.ACTION", "MPS125.CH15.P017.ACTION", "MPS125.CH15.P018.ACTION"
            });
        }

        private static void ValidateCatalog()
        {
            IReadOnlyList<Mps125ActionDefinition> catalog = Mps125ActionCatalog.Create();
            Require(catalog.Any(x => x.Kind == Mps125ActionKind.SetVbatt), "VBATT source B actions missing.");
            Require(catalog.Count(x => x.Kind == Mps125ActionKind.SetVbusRampStep) == 12,
                "MPS-125 Chapter 15 must expose two independent six-level VBUS ramps.");
            Require(catalog.Count(x => x.Kind == Mps125ActionKind.SetVbusRampStep && x.State) == 2,
                "Each Chapter 15 VBUS ramp must have exactly one 25 V output-enable row.");
            Require(catalog.Where(x => x.Kind == Mps125ActionKind.SetVbusRampStep).All(x => x.Chapter == 15),
                "MPS-125 VBUS ramps must be limited to Chapter 15.");
            Require(catalog.Count(x => x.Kind == Mps125ActionKind.WaitForMbdReadyAfterPowerCycle) == 2,
                "Chapter 15 must expose MBD recovery after both VBUS ramps.");
            Require(catalog.Count(x => x.Kind == Mps125ActionKind.SetVbusAndWaitStable) == 2,
                "MPS-125 Chapters 7 and 9 must directly start nominal VBUS and wait for measured stability.");
            Require(catalog.Any(x => x.Chapter == 10 && x.Kind == Mps125ActionKind.SetVbus && x.State &&
                string.Equals(x.Argument, "137.5,2,300", StringComparison.Ordinal)),
                "MPS-125 Chapter 10 direct 137.5 V output-enable action is missing.");
            Require(!catalog.Any(x => x.Chapter == 15 && x.Kind == Mps125ActionKind.SafeVbusRelay),
                "Chapter 15 must not combine VBUS and Relay_5 into one row.");
            Require(catalog.Count(x => x.Chapter == 15 && x.Kind == Mps125ActionKind.DaqRelays && x.Argument == "5") == 2,
                "Chapter 15 must have separate Relay_5 close/open rows.");
            Require(catalog.Single(x => x.StepId == "MPS125.CH15.P007.ACTION").Argument == "EXIT",
                "Chapter 15 must re-enter the MCU terminal after restoring VBUS.");
            Require(catalog.Single(x => x.StepId == "MPS125.CH15.P008.ACTION").Argument == "DIAG ON",
                "Chapter 15 must restore diagnostic mode after restoring VBUS.");
            Require(catalog.Count(x => x.Kind == Mps125ActionKind.DaqTimedWindowAverageVoltageAfterVbattBaseline) == 1,
                "K01 must prepare an isolated/discharged VBATT baseline before sampling.");
            Require(catalog.Count(x => x.Kind == Mps125ActionKind.SetVbattAndWaitStable) == 1,
                "K03 must wait for measured 125 V VBATT stability.");
            Require(catalog.Count(x => x.Kind == Mps125ActionKind.MbdCleanupIfConnected) == 1,
                "Cleanup must not rediscover MBD after fixture release.");
            Require(catalog.Where(x => x.Kind == Mps125ActionKind.DaqShuntCurrent).All(x => x.Channel == 117 && Math.Abs(x.Scale - 1.0) < 1e-9), "Current must use DAQ117/1 ohm shunt.");
            Require(catalog.Any(x => x.Kind == Mps125ActionKind.DaqResistance && x.Channel == 118), "Alarm resistance mapping missing.");
            Require(catalog.Where(x => x.Kind == Mps125ActionKind.MbdCommand).All(x => x.AlwaysRun || (x.Argument ?? "").IndexOf(';') < 0), "Normal commands must be one UI row each.");
            Require(Mps125StationMap.VpPlus == 104, "VP+ mapping must be DAQ104.");
            Require(Mps125StationMap.McuIsoBatt == 105, "MCU_ISOBATT mapping must be DAQ105.");
            Require(Mps125StationMap.HeaterFeedback1 == 107 && Mps125StationMap.HeaterFeedback2 == 108, "FBK mapping must be DAQ107/108.");
            Require(Mps125StationMap.U20Pin7 == 109 && Mps125StationMap.U20Pin5 == 110, "U20.7/U20.5 mapping must be DAQ109/110.");
            Require(Mps125StationMap.ComUsb == 111, "COM_USB mapping must be DAQ111.");
            Require(Mps125StationMap.PPlus == 113 && Mps125StationMap.VbusReference == 113, "P+/VBUS reference mapping must be DAQ113.");
            Require(Mps125StationMap.Vpwm == 114, "VPWM mapping must be DAQ114.");
            Require(Mps125StationMap.VbusShunt == 117, "VBUS shunt mapping must be DAQ117.");
            Require(Mps125StationMap.FiveVoltIsolated == 119 && Mps125StationMap.ThreeVoltThree == 120, "+5VI/3V3 mapping must be DAQ119/120.");
            Require(Mps125StationMap.VbattSourcePowerRelay == 1 &&
                Mps125StationMap.VbattDutPowerRelay == 9 &&
                Mps125StationMap.VbattPowerRelays == "1,9",
                "MPS-125 VBATT must use station-3 Y1/Y9.");
            Require(Mps125StationMap.AllControlledRelays == "2,3,4,5,6", "MPS-125 must not declare logical Relay_1.");
            Require(Mps125StationMap.WithdrawalDaqRelay == "@301", "Relay_3 must use DAQ301.");
            Require(Mps125StationMap.ExternalPowerDaqRelays == "@305,306", "Relay_2/2BIS must use DAQ305/306.");
            Require(!catalog.Where(x => x.Kind == Mps125ActionKind.DaqRelays)
                .SelectMany(x => (x.Argument ?? "").Split(','))
                .Any(x => x.Trim() == "1"), "MPS-125 DAQ functional-relay catalog must not treat power-path Y1 as a logical relay.");

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Mps125ActionDefinition item in catalog)
            {
                foreach (string dependency in item.RequiresResultIds)
                    Require(seen.Contains(dependency), item.ResultId + " has missing/later dependency " + dependency + ".");
                if (item.IsResult)
                    seen.Add(item.ResultId);
            }
        }

        private static void ValidateChapterCommunicationClose(
            TestSequenceContext context,
            SK441Device device)
        {
            IList steps = BuildSteps(context, device);
            object closeStep = steps.Cast<object>().Single(x => string.Equals(
                Read<string>(x.GetType(), x, "StepId"),
                "MPS125.CH08.S006.ACTION",
                StringComparison.OrdinalIgnoreCase));

            bool closeLogged = false;
            device.LogInfo += message =>
            {
                if ((message ?? string.Empty).IndexOf(
                    "模拟关闭MBD/目标板串口通信",
                    StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    closeLogged = true;
                }
            };

            var action = (Delegate)closeStep.GetType().GetProperty("Action").GetValue(closeStep);
            var task = (Task)action.DynamicInvoke(CancellationToken.None);
            task.GetAwaiter().GetResult();
            object result = task.GetType().GetProperty("Result").GetValue(task);
            bool isPass = (bool)result.GetType().GetProperty("IsPass").GetValue(result);
            Require(isPass, "Chapter 8.10 MBD communication close failed.");
            Require(closeLogged, "Chapter 8.10 did not execute the MBD communication close action.");
        }

        private static void ValidateSafetyGates(IList steps)
        {
            string[] safetyStepIds =
            {
                "MPS125.CH16.S005.ACTION",
                "MPS125.CH16.S007.ACTION"
            };
            foreach (string stepId in safetyStepIds)
            {
                object step = steps.Cast<object>().Single(x => string.Equals(
                    Read<string>(x.GetType(), x, "StepId"),
                    stepId,
                    StringComparison.OrdinalIgnoreCase));
                Require(Read<bool>(step.GetType(), step, "SafetyCritical"),
                    stepId + " must be safety-critical.");
            }

            foreach (object step in steps.Cast<object>().Where(x =>
                Read<string>(x.GetType(), x, "StepId").StartsWith(
                    "MPS125.CH",
                    StringComparison.OrdinalIgnoreCase) &&
                !Read<string>(x.GetType(), x, "StepId").EndsWith(
                    ".ACTION",
                    StringComparison.OrdinalIgnoreCase)))
            {
                Require(!Read<bool>(step.GetType(), step, "SafetyCritical"),
                    Read<string>(step.GetType(), step, "StepId") +
                    " numeric result must follow the global stop-on-fail policy.");
                Require(!Read<bool>(step.GetType(), step, "PowerCritical"),
                    Read<string>(step.GetType(), step, "StepId") +
                    " numeric result must not override the global stop-on-fail policy.");
            }
        }

        private static void ValidateConfiguredAverageSampleCounts()
        {
            MethodInfo resolver = FindMethod(typeof(SKMPS125Sequence), "ResolveAverageSampleCount");
            Mps125ActionDefinition nonPwm = Mps125ActionCatalog.Create().Single(x => x.StepId == "MPS125.CH14.S010.I01");
            Mps125ActionDefinition mainSwitchPwm = Mps125ActionCatalog.Create().Single(x => x.StepId == "MPS125.CH13.S003.H01");
            Mps125ActionDefinition isobattPwm = Mps125ActionCatalog.Create().Single(x => x.StepId == "MPS125.CH16.S003.K01");
            Mps125ActionDefinition heaterPwm = Mps125ActionCatalog.Create().Single(x => x.StepId == "MPS125.CH17.S012.L02");
            Require((int)resolver.Invoke(null, new object[] { nonPwm }) == 3, "Configured non-PWM average count was not applied.");
            Require((int)resolver.Invoke(null, new object[] { mainSwitchPwm }) == 16, "Main-switch PWM sampling must remain at 16.");
            Require((int)resolver.Invoke(null, new object[] { isobattPwm }) == 81, "ISOBATT timed-window sampling must use 81 endpoint-inclusive samples.");
            Require(isobattPwm.Kind == Mps125ActionKind.DaqTimedWindowAverageVoltageAfterVbattBaseline,
                "ISOBATT K01 must use the DAQ internal timed-window action.");
            Require((isobattPwm.SampleCount - 1) * isobattPwm.SampleIntervalMs == 8000,
                "ISOBATT K01 must span a real 8-second sampling window.");
            Mps125ActionDefinition isobattAtZero = Mps125ActionCatalog.Create().Single(x => x.StepId == "MPS125.CH16.S006.K02");
            Mps125ActionDefinition isobattAtVoltage = Mps125ActionCatalog.Create().Single(x => x.StepId == "MPS125.CH16.S008.K03");
            Require(isobattAtZero.Kind == Mps125ActionKind.DaqTimedWindowAverageVoltage &&
                    isobattAtVoltage.Kind == Mps125ActionKind.DaqTimedWindowAverageVoltage,
                "ISOBATT K02/K03 must use the DAQ internal timed-window action.");
            Require((isobattAtZero.SampleCount - 1) * isobattAtZero.SampleIntervalMs == 8000 &&
                    (isobattAtVoltage.SampleCount - 1) * isobattAtVoltage.SampleIntervalMs == 8000,
                "ISOBATT K02/K03 must span real 8-second sampling windows.");
            Require((int)resolver.Invoke(null, new object[] { heaterPwm }) == 16, "Heater PWM sampling must remain at 16.");
        }

        private static void ValidateLimit(ResolvedTestPlan plan, string resultId, double lower, double upper, string unit)
        {
            TestPlanStepDefinition item = plan.Definition.Steps.Single(x => x.StepId.EndsWith("." + resultId, StringComparison.OrdinalIgnoreCase));
            Require(item.LowerLimit == lower && item.UpperLimit == upper && item.Unit == unit, "Limit mismatch " + resultId + ".");
        }

        private static void ValidateOpenUpperLimit(
            ResolvedTestPlan plan,
            string resultId,
            double lower,
            string unit)
        {
            TestPlanStepDefinition item = plan.Definition.Steps.Single(
                x => x.StepId.EndsWith("." + resultId, StringComparison.OrdinalIgnoreCase));
            Require(
                item.LowerLimit == lower &&
                !item.UpperLimit.HasValue &&
                item.Unit == unit,
                "Open-upper limit mismatch " + resultId + ".");
        }

        private static void ValidateExpectedValue(
            ResolvedTestPlan plan,
            string stepId,
            string expectedValue)
        {
            TestPlanStepDefinition item = plan.Definition.Steps.Single(x =>
                string.Equals(x.StepId, stepId, StringComparison.OrdinalIgnoreCase));
            Require(item.ExpectedValue == expectedValue,
                "ExpectedValue mismatch " + stepId + ".");
        }

        private static void ValidateRampRows(ResolvedTestPlan plan, string[] stepIds)
        {
            double[] voltages = { 25, 50, 75, 100, 125, 137.5 };
            Require(stepIds.Length == voltages.Length,
                "Ramp validation requires six step IDs.");
            for (int index = 0; index < stepIds.Length; index++)
            {
                TestPlanStepDefinition item = plan.Definition.Steps.Single(x =>
                    string.Equals(x.StepId, stepIds[index], StringComparison.OrdinalIgnoreCase));
                string expected = voltages[index].ToString(
                    System.Globalization.CultureInfo.InvariantCulture) + ",2,300";
                Require(item.ExpectedValue == expected,
                    "Ramp setpoint mismatch " + stepIds[index] + ".");
                Require(item.DelayMs == 200,
                    "Ramp delay must be 200 ms for " + stepIds[index] + ".");
            }
        }

        private static bool RunMps125VbusRamp(SK441Device device)
        {
            float[] voltages = { 25, 50, 75, 100, 125, 137.5f };
            for (int index = 0; index < voltages.Length; index++)
            {
                if (!device.SetMps125VbusRampStepAsync(
                    voltages[index],
                    2,
                    300,
                    index == 0).GetAwaiter().GetResult())
                {
                    return false;
                }
            }

            return true;
        }

        private static void ValidatePowerSafety()
        {
            var device = new SK441Device { SkipComInit = true, SimulationBoardType = "MPS-125" };
            Require(device.SetVbusGeneratorAsync(137.5f, 2, 300, true).GetAwaiter().GetResult(),
                "MPS-125 direct 137.5 V start failed.");
            Require(device.SetVbattGeneratorAsync(125, 0.1f, 150, true).GetAwaiter().GetResult(), "MPS VBATT failed to close Y1/Y9 before output enable.");
            string[] vbattOnTrace = device.GetPowerSafetyTrace();
            int y1Closed = Array.IndexOf(vbattOnTrace, "RELAY 1 CLOSED");
            int y9Closed = Array.IndexOf(vbattOnTrace, "RELAY 9 CLOSED");
            int vbattOn = Array.IndexOf(vbattOnTrace, "OUTPUT SECONDARY ON");
            Require(y1Closed >= 0 && y1Closed < y9Closed && y9Closed < vbattOn,
                "MPS-125 VBATT order must be Y1 close, Y9 close, then output ON.");
            Require(device.SetVbattGeneratorAsync(0, 0, 150, false).GetAwaiter().GetResult(), "Cannot turn off VBATT and open Y9/Y1.");
            string[] vbattOffTrace = device.GetPowerSafetyTrace();
            int vbattOff = Array.LastIndexOf(vbattOffTrace, "OUTPUT SECONDARY OFF");
            int y9Opened = Array.LastIndexOf(vbattOffTrace, "RELAY 9 OPEN");
            int y1Opened = Array.LastIndexOf(vbattOffTrace, "RELAY 1 OPEN");
            Require(vbattOff >= 0 && vbattOff < y9Opened && y9Opened < y1Opened,
                "MPS-125 VBATT order must be output OFF, Y9 open, then Y1 open.");
            Require(!device.StopFixturePressDownAsync("MPS-125").GetAwaiter().GetResult(), "Fixture released while VBUS was energized.");
            Require(!device.ControlMpsRelayAsync(5, Mps125StationMap.SensPrecDaqRelay, true).GetAwaiter().GetResult(), "Relay_5/DAQ303 closed while VBUS was energized.");
            Require(device.ControlMpsRelayAsync(3, Mps125StationMap.WithdrawalDaqRelay, true).GetAwaiter().GetResult(), "Relay_3/DAQ301 live functional switching was incorrectly blocked.");
            Require(device.SafeSwitchMpsVbusRelayAsync(5, Mps125StationMap.SensPrecDaqRelay, true, 137.5f, 2, 300, false).GetAwaiter().GetResult(), "Relay_5/DAQ303 safe switching failed.");
            Require(RunMps125VbusRamp(device), "MPS-125 VBUS ramp after Relay_5 switching failed.");
            Require(device.SetVbusGeneratorAsync(0, 0, 300, false).GetAwaiter().GetResult(), "Cannot turn off VBUS.");
            Require(device.SafeConnectMpsVbattRelayAsync(6, Mps125StationMap.IsoBattDaqRelay, 150).GetAwaiter().GetResult(), "Relay_6/DAQ304 safe connection failed.");
            Require(device.SafeDisconnectMpsVbattRelayAsync(6, Mps125StationMap.IsoBattDaqRelay, 150).GetAwaiter().GetResult(), "Safe MPS VBATT disconnection failed.");
            Require(device.TurnOffAllInstrumentsAsync().GetAwaiter().GetResult(), "Cannot power off before fixture release.");
            Require(device.StopFixturePressDownAsync("MPS-125").GetAwaiter().GetResult(), "Fixture remained locked after safe power-off.");
            Require(device.GetPowerSafetyTrace().Any(x => x == "RELAY 1 CLOSED") &&
                device.GetPowerSafetyTrace().Any(x => x == "RELAY 9 CLOSED"),
                "MPS-125 never operated required VBATT power relays Y1/Y9.");
            Require(device.GetPowerSafetyTrace().Any(x => x.StartsWith("BLOCK", StringComparison.Ordinal)), "No unsafe VBUS action was recorded.");

            var emergency = new SK441Device { SkipComInit = true, SimulationBoardType = "MPS-125" };
            Require(emergency.EnableFixturePressDownAsync("MPS-125").GetAwaiter().GetResult(), "MPS-125 fixture Y1 did not close.");
            Require(emergency.EmergencyStopOutputsAsync("MPS-125", "test emergency").GetAwaiter().GetResult(), "MPS-125 emergency output shutdown failed.");
            string[] fixtureTrace = emergency.GetFixtureActuationTrace();
            Require(fixtureTrace.Contains("FIXTURE STATION 1 RELAY 1 CLOSED") && fixtureTrace.Contains("FIXTURE STATION 1 RELAY 1 OPEN"), "MPS-125 fixture Y1 emergency mapping failed.");
            Require(!fixtureTrace.Any(x => x.Contains("RELAY 2")), "MPS-125 incorrectly used fixture Y2 for press-down.");
            Require(!emergency.SetVbusGeneratorAsync(137.5f, 2, 300, true).GetAwaiter().GetResult(), "Emergency latch allowed VBUS output ON.");
        }

        private static void RunEndToEnd(string xmlPath, bool injectFault)
        {
            string oldMode = Environment.GetEnvironmentVariable("TESTPLATFORM_SK_MODE");
            string oldFault = Environment.GetEnvironmentVariable("TESTPLATFORM_MPS125_FAULT_STEP");
            Environment.SetEnvironmentVariable("TESTPLATFORM_SK_MODE", "Simulation");
            try
            {
                ResolvedTestPlan plan = TestPlanService.Resolve(xmlPath, TestProfileIds.Normal);
                string faultStep = plan.Definition.Steps.Single(x => x.StepId.EndsWith(".J08", StringComparison.OrdinalIgnoreCase)).StepId;
                Environment.SetEnvironmentVariable("TESTPLATFORM_MPS125_FAULT_STEP", injectFault ? faultStep : null);
                var grid = new RecordingGrid();
                var context = new TestSequenceContext { SN = injectFault ? "MPS125-FAULT" : "MPS125-PASS", TestPlan = plan, StopOnFail = true };
                bool passed = new SKMPS125Sequence(grid).RunAsync(context).GetAwaiter().GetResult();
                Require(passed != injectFault, injectFault
                    ? "Injected-fault run passed."
                    : "Full simulation failed. Failed steps: " + string.Join(", ", grid.FailedStepIds));
                Require(grid.CleanupCompleted == 6, "Not all cleanup rows executed.");
                if (injectFault)
                    Require(grid.FailedStepIds.Contains(faultStep), "Injected J08 failure was not reported.");
            }
            finally
            {
                Environment.SetEnvironmentVariable("TESTPLATFORM_SK_MODE", oldMode);
                Environment.SetEnvironmentVariable("TESTPLATFORM_MPS125_FAULT_STEP", oldFault);
            }
        }

        private static void RunSafetyGateFault(
            string xmlPath,
            string faultStepId,
            string forbiddenLaterStepId)
        {
            string oldMode = Environment.GetEnvironmentVariable("TESTPLATFORM_SK_MODE");
            string oldFault = Environment.GetEnvironmentVariable("TESTPLATFORM_MPS125_FAULT_STEP");
            Environment.SetEnvironmentVariable("TESTPLATFORM_SK_MODE", "Simulation");
            Environment.SetEnvironmentVariable("TESTPLATFORM_MPS125_FAULT_STEP", faultStepId);
            try
            {
                ResolvedTestPlan plan = TestPlanService.Resolve(xmlPath, TestProfileIds.Normal);
                var grid = new RecordingGrid();
                var context = new TestSequenceContext
                {
                    SN = "MPS125-SAFETY-FAULT",
                    TestPlan = plan,
                    StopOnFail = false,
                    ContinueAfterPowerCriticalFailure = true
                };
                bool passed = new SKMPS125Sequence(grid).RunAsync(context)
                    .GetAwaiter().GetResult();
                Require(!passed, faultStepId + " safety fault unexpectedly passed.");
                Require(grid.FailedStepIds.Contains(faultStepId),
                    faultStepId + " safety fault was not reported.");
                Require(!grid.ObservedStepIds.Contains(forbiddenLaterStepId),
                    faultStepId + " was bypassed by engineering continuation.");
                Require(grid.CleanupCompleted == 6,
                    faultStepId + " did not complete all cleanup rows.");
            }
            finally
            {
                Environment.SetEnvironmentVariable("TESTPLATFORM_SK_MODE", oldMode);
                Environment.SetEnvironmentVariable("TESTPLATFORM_MPS125_FAULT_STEP", oldFault);
            }
        }

        private static void RunGlobalResultFailureContinue(
            string xmlPath,
            string faultStepId,
            string expectedLaterStepId)
        {
            string oldMode = Environment.GetEnvironmentVariable("TESTPLATFORM_SK_MODE");
            string oldFault = Environment.GetEnvironmentVariable("TESTPLATFORM_MPS125_FAULT_STEP");
            Environment.SetEnvironmentVariable("TESTPLATFORM_SK_MODE", "Simulation");
            Environment.SetEnvironmentVariable("TESTPLATFORM_MPS125_FAULT_STEP", faultStepId);
            try
            {
                ResolvedTestPlan plan = TestPlanService.Resolve(xmlPath, TestProfileIds.Normal);
                var grid = new RecordingGrid();
                var context = new TestSequenceContext
                {
                    SN = "MPS125-GLOBAL-CONTINUE",
                    TestPlan = plan,
                    StopOnFail = false,
                    ContinueAfterPowerCriticalFailure = false
                };
                bool passed = new SKMPS125Sequence(grid).RunAsync(context)
                    .GetAwaiter().GetResult();
                Require(!passed, faultStepId + " injected result failure unexpectedly passed.");
                Require(grid.FailedStepIds.Contains(faultStepId),
                    faultStepId + " result failure was not reported.");
                Require(grid.ObservedStepIds.Contains(expectedLaterStepId),
                    faultStepId + " did not follow the global continue-after-failure setting.");
                Require(grid.CleanupCompleted == 6,
                    faultStepId + " did not complete all cleanup rows.");
            }
            finally
            {
                Environment.SetEnvironmentVariable("TESTPLATFORM_SK_MODE", oldMode);
                Environment.SetEnvironmentVariable("TESTPLATFORM_MPS125_FAULT_STEP", oldFault);
            }
        }

        private static MethodInfo FindMethod(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                MethodInfo method = current.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (method != null) return method;
            }
            throw new MissingMethodException(type.FullName, name);
        }

        private static T Read<T>(Type type, object instance, string property) => (T)type.GetProperty(property).GetValue(instance);

        private static string FindRoot(string start)
        {
            var directory = new DirectoryInfo(start);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "TestPlatform.csproj"))) return directory.FullName;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("Repository root not found.");
        }

        private static Exception Unwrap(Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
            return ex;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class RecordingGrid : ITestGridService
        {
            public int RowCount => 1000;
            public int CleanupCompleted { get; private set; }
            public HashSet<string> FailedStepIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> ObservedStepIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public Task<bool> IsRowSelectedAsync(int rowIndex) => Task.FromResult(true);
            public Task<bool> IsStepSelectedAsync(string stepId) => Task.FromResult(true);
            public Task SetValueAsync(int channelIndex, int rowIndex, string value) => Task.CompletedTask;
            public Task SetResultAsync(int channelIndex, int rowIndex, string result) => Task.CompletedTask;
            public Task SetValueAndResultAsync(int channelIndex, int rowIndex, string value, bool pass) => Task.CompletedTask;
            public Task SetValueAndResultByStepIdAsync(int channelIndex, string stepId, string value, bool pass) => Task.CompletedTask;
            public Task SetExecTimeAsync(int rowIndex, long elapsedMs) => Task.CompletedTask;
            public Task SetExecTimeByStepIdAsync(string stepId, long elapsedMs) => Task.CompletedTask;
            public Task ScrollToRowAsync(int rowIndex, int durationMs = 150) => Task.CompletedTask;
            public Task ScrollToStepAsync(string stepId, int durationMs = 150) => Task.CompletedTask;
            public Task SetValueAndStatusByStepIdAsync(int channelIndex, string stepId, string value, StepExecutionStatus status)
            {
                ObservedStepIds.Add(stepId);
                if (status == StepExecutionStatus.Failed) FailedStepIds.Add(stepId);
                if (stepId.StartsWith(Mps125GroupIds.Cleanup + ".", StringComparison.OrdinalIgnoreCase) &&
                    (status == StepExecutionStatus.Passed || status == StepExecutionStatus.PassedAfterRetry)) CleanupCompleted++;
                return Task.CompletedTask;
            }
            public Task<(bool IsValid, double Lower, double Upper, string ErrorMessage)> GetLimitsAsync(int rowIndex) => Task.FromResult((true, 0.0, 0.0, string.Empty));
        }
    }
}
