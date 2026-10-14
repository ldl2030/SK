using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
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
                ValidateCustomerReport(root, plan);
                if (args.Contains("--report-only"))
                {
                    Console.WriteLine("PASS MPS125 customer report: 70 mapped rows, current XML limits, TXT/PDF, failure and missing-result handling.");
                    return 0;
                }
                var context = new TestSequenceContext { TestPlan = plan, SN = "MPS125-SIM-0001" };
                var device = new SK441Device { SkipComInit = true, SimulationBoardType = "MPS-125" };
                IList steps = BuildSteps(context, device);
                SkCommonBehaviorTests.Validate("MPS-125", steps);

                ValidateStructure(plan, steps);
                ValidateGainReferenceReuse(plan);
                ValidateCatalog();
                ValidateChapterCommunicationClose(context, device);
                ValidateSafetyGates(steps);
                ValidateConfiguredAverageSampleCounts();
                ValidateFastMeasurementProfile(plan);
                ValidateDaqMeasurementTransitions();
                ValidateStabilityWaitPolicy();
                ValidateHeaterEarlyMeasurements(plan);
                ValidatePowerSafety();
                ValidateK01IsolatedBaseline();
                ValidateKPreparationWait(plan, true);
                ValidateKPreparationWait(plan, false);
                RunEndToEnd(xml, false);
                RunEndToEnd(xml, true);
                RunSafetyGateFault(xml, "MPS125.CH15.P011.ACTION", "MPS125.CH15.P012.ACTION");
                RunSafetyGateFault(xml, "MPS125.CH16.S005.ACTION", "MPS125.CH16.S006.K02");
                RunGlobalResultFailureContinue(xml, "MPS125.CH16.S003.K01", "MPS125.CH16.S004.ACTION");
                RunGlobalResultFailureContinue(xml, "MPS125.CH16.S006.K02", "MPS125.CH16.S007.ACTION");
                RunGlobalResultFailureContinue(xml, "MPS125.CH17.S014.L04", "MPS125.CH17.S015.ACTION");

                Console.WriteLine("PASS MPS125: 231 rows, both Chapter 15 VBUS ramps at 100 ms, K01/K02 zero waits with warning continuation, 70 PDF results, full simulation and fault cleanup.");
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
            ValidateLimit(plan, "F10", 1, 1, "Count");
            ValidateLimit(plan, "F09", 0, 0, "Count");
            ValidateOpenUpperLimit(plan, "G02", 1000000, "Ohm");
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
            }, 100);
        }

        private static void ValidateGainReferenceReuse(ResolvedTestPlan plan)
        {
            var context = new TestSequenceContext { TestPlan = plan };
            // Replay the successful reference and ADC readings from the log,
            // with hardware mode enabled and no connected instrument.
            var device = new SK441Device { SkipComInit = false, SimulationBoardType = "MPS-125" };
            IList steps = BuildSteps(context, device);
            string[] adcIds = { "J01", "J02", "J03", "J04" };
            double[] adcValues = { 2782, 2783, 2783, 2783 };
            for (int index = 0; index < adcIds.Length; index++)
                context.StoreValue(Mps125ResultKeys.For(adcIds[index]), adcValues[index], "ADC", "Count", true);
            context.StoreValue(Mps125ResultKeys.VbusCalibrationReference, 137.314536, "137.314536", "V", true);

            int gainIndex = 0;
            foreach (string id in new[] { "J05", "J06", "J07", "J08" })
            {
                TestPlanStepDefinition definition = plan.Definition.Steps.Single(x => x.StepId.EndsWith("." + id, StringComparison.Ordinal));
                Require(definition.DependsOn.Contains("MPS125.CH15.S016.ACTION"), id + " XML must depend on the reference measurement.");
                object step = steps.Cast<object>().Single(x => Read<string>(x.GetType(), x, "StepId") == definition.StepId);
                Delegate action = Read<Delegate>(step.GetType(), step, "Action");
                Task task = (Task)action.DynamicInvoke(CancellationToken.None);
                task.GetAwaiter().GetResult();
                object result = task.GetType().GetProperty("Result").GetValue(task);
                Require(Read<bool>(result.GetType(), result, "IsPass"), id + " failed with the successful logged reference.");
                double expected = 4096.0 * 137314.536 / adcValues[gainIndex++];
                Require(Math.Abs(Read<double?>(result.GetType(), result, "NumericValue").Value - expected) < 0.000001,
                    id + " did not reuse the logged reference with V-to-mV conversion.");
            }

            Mps125ActionDefinition gain = Mps125ActionCatalog.Create().Single(x => x.ResultId == "J05");
            Action requireInvalidReference = () =>
            {
                try
                {
                    ((Task<double>)FindMethod(typeof(SKMPS125Sequence), "ComputeGainAsync").Invoke(null, new object[] { context, gain })).GetAwaiter().GetResult();
                    throw new Exception("Missing/invalid reference accepted.");
                }
                catch (TargetInvocationException ex)
                {
                    Require(Unwrap(ex).Message.Contains("15.15"), "Gain failed for an unrelated reason.");
                }
            };
            context.Values.Remove(Mps125ResultKeys.VbusCalibrationReference);
            requireInvalidReference();
            context.StoreValue(Mps125ResultKeys.VbusCalibrationReference, 137.314536, "failed", "V", false);
            requireInvalidReference();
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, 0.0, 9.9E37 })
            {
                context.StoreValue(Mps125ResultKeys.VbusCalibrationReference, invalid, "invalid", "V", true);
                requireInvalidReference();
            }

            device.SkipComInit = true;
            context.StoreValue(Mps125ResultKeys.VbusCalibrationReference, 137.314536, "old", "V", true);
            object referenceStep = steps.Cast<object>().Single(x => Read<string>(x.GetType(), x, "StepId") == "MPS125.CH15.S016.ACTION");
            Delegate referenceAction = Read<Delegate>(referenceStep.GetType(), referenceStep, "Action");
            string oldFault = Environment.GetEnvironmentVariable("TESTPLATFORM_MPS125_FAULT_STEP");
            Environment.SetEnvironmentVariable("TESTPLATFORM_MPS125_FAULT_STEP", "MPS125.CH15.S016.ACTION");
            try
            {
                Task task = (Task)referenceAction.DynamicInvoke(CancellationToken.None);
                task.GetAwaiter().GetResult();
                object result = task.GetType().GetProperty("Result").GetValue(task);
                Require(!Read<bool>(result.GetType(), result, "IsPass"), "Reference fault was not injected.");
                Require(!context.Values.ContainsKey(Mps125ResultKeys.VbusCalibrationReference), "Failed reference retained a stale success.");
                requireInvalidReference();
            }
            finally { Environment.SetEnvironmentVariable("TESTPLATFORM_MPS125_FAULT_STEP", oldFault); }
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
                "K01 must safely isolate its VBATT baseline before sampling.");
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
            Require(Mps125StationMap.WithdrawalDaqRelay == "@308", "Relay_3 must use DAQ308 / CH_8.");
            Require(Mps125StationMap.GetDaqRelayChannels(3) == "@308", "Logical Relay_3 must resolve to DAQ308.");
            Require(Mps125StationMap.AllDaqControlRelays == "@302:306,308", "MPS-125 functional relay range must exclude DAQ301/307 and include DAQ308.");
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
            Require((int)resolver.Invoke(null, new object[] { mainSwitchPwm }) == 8, "Main-switch PWM sampling must use the configured eight samples.");
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
            Require((int)resolver.Invoke(null, new object[] { heaterPwm }) == 8, "Heater PWM sampling must use the configured eight samples.");
        }

        private static void ValidateFastMeasurementProfile(ResolvedTestPlan plan)
        {
            ValidateExpectedValue(plan, "MPS125.CH08.S001.ACTION", "28,0.5,300");
            ValidateExpectedValue(plan, "MPS125.CH08.S002.ACTION", "28,0.5,300");
            Require(Mps125ActionCatalog.Create().Single(x => x.Kind == Mps125ActionKind.IgnitionRamp).Argument == "28,80,1",
                "Ignition must start at the user-authorized 28 V.");
            foreach (var action in Mps125ActionCatalog.Create().Where(x => x.Kind == Mps125ActionKind.DaqAverageVoltage))
            {
                var item = plan.Definition.Steps.Single(x => x.StepId == action.StepId);
                double requiredRange = Math.Max(Math.Abs(item.LowerLimit ?? 0), Math.Abs(item.UpperLimit ?? 0)) / 1000;
                Require(action.DaqRangeVolts.HasValue && action.DaqRangeVolts.Value >= requiredRange,
                    action.StepId + " fixed range cannot cover its limits.");
                if (requiredRange <= 0.2)
                    Require(action.DaqRangeVolts.Value <= 1, action.StepId + " low-voltage range loses sensitivity.");
            }
            var device = new SK441Device { SkipComInit = true, SimulationBoardType = "MPS-125" };
            Require(device.SetVbusGeneratorAsync(28, 0.5f, 300, true).GetAwaiter().GetResult(), "Ignition startup failed.");
            double ignition = device.MeasureMps125IgnitionVoltageAsync(101, 28, 80, 1, System.Threading.CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(ignition == 31, "Ignition scan failed to stop at the first +5 V threshold crossing.");
            var trace = device.GetPowerSafetyTrace();
            Require(trace.Where(x => x.StartsWith("MPS125 IGNITION ")).SequenceEqual(
                new[] { "MPS125 IGNITION 28", "MPS125 IGNITION 29", "MPS125 IGNITION 30", "MPS125 IGNITION 31" }),
                "Ignition scan skipped or repeated voltage points.");
            Require(trace.Count(x => x == "OUTPUT VBUS ON") == 1, "Ignition must not re-enable the source at each point.");
            Require(!device.TargetBoard.ShouldLogSuccessfulTransmit(), "Compact MPS logs must omit duplicate successful TX.");
            device.SimulationBoardType = "BCM-125";
            Require(!device.TargetBoard.ShouldLogSuccessfulTransmit(), "BCM and MPS must share the compact logging policy.");

            foreach (bool cancel in new[] { false, true })
            {
                var failing = new SK441Device { SkipComInit = true, SimulationBoardType = "MPS-125" };
                Require(failing.SetVbusGeneratorAsync(28, 0.5f, 300, true).GetAwaiter().GetResult(), "Fault scan startup failed.");
                bool rejected = false;
                try
                {
                    failing.MeasureMps125IgnitionVoltageAsync(101, 28, 30, 1,
                        new System.Threading.CancellationToken(cancel)).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) { rejected = cancel; }
                catch (InvalidOperationException) { rejected = !cancel; }
                Require(rejected, "Canceled or never-ignited scan must fail instead of returning the upper limit.");
                Require(failing.GetPowerSafetyTrace().Last(x => x.StartsWith("OUTPUT VBUS ")) == "OUTPUT VBUS OFF",
                    "Failed ignition scan must disable VBUS.");
            }
        }

        private static void ValidateCustomerReport(string root, ResolvedTestPlan plan)
        {
            string sourceDirectory = Path.Combine(root, "ProjectConfig", "SK", "MPS-125");
            string definitionPath = Path.Combine(sourceDirectory, "MPS-125_report.psv");
            string[][] definitions = File.ReadAllLines(definitionPath)
                .Where(x => !string.IsNullOrWhiteSpace(x) && !x.StartsWith("#"))
                .Select(x => x.Split('|')).ToArray();
            Require(definitions.Length == Bcm125LegacyReportExporter.Mps125ExpectedReportItemCount && definitions.Length == 70,
                "MPS125 report must contain all 70 results.");
            Require(definitions.All(x => x.Length == 9) && definitions.Select(x => x[0]).Distinct().Count() == 70 &&
                definitions.Select(x => x[1]).Distinct().Count() == 70, "Duplicate or malformed MPS125 report mapping.");
            Require(definitions.Select(x => x[1]).SequenceEqual(Mps125ActionCatalog.Create().Where(x => x.IsResult).Select(x => x.StepId)),
                "MPS125 customer report order/mapping differs from the test results.");

            var table = new DataTable();
            table.Columns.Add("StepId", typeof(string));
            table.Columns.Add("Channel1Value", typeof(string));
            table.Columns.Add("Channel1Result", typeof(string));
            foreach (string[] fields in definitions)
            {
                TestPlanStepDefinition step = plan.GetStep(fields[1]);
                double scale = double.Parse(fields[8], System.Globalization.CultureInfo.InvariantCulture);
                string lower = step.LowerLimit.HasValue ? ReportNumber(step.LowerLimit.Value * scale) : "";
                string upper = step.UpperLimit.HasValue ? ReportNumber(step.UpperLimit.Value * scale) : step.LowerLimit.HasValue ? "any" : "";
                Require(fields[6] == lower && fields[7] == upper, fields[0] + " PSV limit differs from current XML.");
                double value = step.LowerLimit.HasValue && step.UpperLimit.HasValue
                    ? (step.LowerLimit.Value + step.UpperLimit.Value) / 2 : step.LowerLimit ?? 1;
                if (fields[5] == "Count" && fields[0].StartsWith("L")) value = 4095;
                string measured = fields[0] == "D03" ? "mcu_app U.2.2.2" : value.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture);
                table.Rows.Add(step.StepId, measured, "PASS");
            }
            table.Rows[0]["Channel1Value"] = "5000";

            string qaRoot = Environment.GetEnvironmentVariable("TESTPLATFORM_MPS125_REPORT_QA_ROOT");
            bool keepQa = !string.IsNullOrWhiteSpace(qaRoot);
            string outputRoot = keepQa ? Path.GetFullPath(qaRoot) : Path.Combine(Path.GetTempPath(), "Mps125Report_" + Guid.NewGuid().ToString("N"));
            string configDirectory = Path.Combine(outputRoot, "ProjectConfig", "SK", "MPS-125");
            Directory.CreateDirectory(configDirectory);
            string copiedDefinition = Path.Combine(configDirectory, "MPS-125_report.psv");
            string copiedXml = Path.Combine(configDirectory, "MPS-125_autoTest.xml");
            File.Copy(definitionPath, copiedDefinition, true);
            File.Copy(Path.Combine(sourceDirectory, "MPS-125_autoTest.xml"), copiedXml, true);
            Func<DataTable, bool, string, Bcm125LegacyReportExporter.ExportResult> save = (snapshot, passed, sn) =>
                Bcm125LegacyReportExporter.SaveAsync(snapshot, 0, "1131", sn, passed, "SK MPS-125 自动测试",
                    new DateTime(2026, 10, 10, 17, 25, 0), outputRoot).GetAwaiter().GetResult();
            try
            {
                var result = save(table, true, "MPS125-REPORT-SIM-PASS");
                Require(result.Success && result.DutPassed && File.Exists(result.TextFilePath) && new FileInfo(result.PdfFilePath).Length > 1000,
                    "MPS125 TXT/PDF generation failed: " + result.ErrorMessage);
                string text = File.ReadAllText(result.TextFilePath);
                Require(text.StartsWith("MPS_UP125, Codice operatore: 1131, Seriale: MPS125-REPORT-SIM-PASS"), "Wrong MPS125 report header.");
                Require(text.Contains("+5V = 5 V") && text.Contains("[4,8; 5,4]"), "MPS125 voltage conversion or unit order differs from the reference.");
                foreach (string[] fields in definitions)
                {
                    string line = text.Split('\n').Single(x => x.StartsWith(fields[0] + " "));
                    if (!string.IsNullOrEmpty(fields[6]) || !string.IsNullOrEmpty(fields[7]))
                        Require(line.Contains("[" + fields[6] + "; " + fields[7] + "]"), fields[0] + " report did not use XML limits.");
                }
                Require(text.Contains("string_negative = 4095 Count") && !text.Contains("string_negative = 4,095"),
                    "MPS125 R71 register count was incorrectly converted from the legacy mV label.");
                Require(text.Split('\n').Count(x => x.StartsWith("Test ")) == 11, "MPS125 report must summarize all 11 reference groups.");

                var failing = table.Copy();
                DataRow l11 = failing.AsEnumerable().Single(x => x["StepId"].ToString().EndsWith(".L11"));
                l11["Channel1Value"] = "102.834";
                l11["Channel1Result"] = "FAIL";
                result = save(failing, false, "MPS125-REPORT-SIM-L11-FAIL");
                text = File.ReadAllText(result.TextFilePath);
                Require(result.Success && !result.DutPassed && result.TextFilePath.Contains("NG") &&
                    text.Split('\n').Single(x => x.StartsWith("L11 ")).Contains("0,102834 A") && text.Contains("[0,05; 0,1]") &&
                    text.Contains("Test CHECK HEATER SIGNALS: NOT PASSED"), "MPS125 current failure or range was lost in the customer report.");

                var missing = table.Copy();
                missing.Rows[0]["Channel1Value"] = "未执行";
                result = save(missing, true, "MPS125-REPORT-SIM-MISSING");
                Require(result.Success && !result.DutPassed && File.ReadAllText(result.TextFilePath).Contains("NOT EXECUTED"),
                    "An unexecuted required result must not produce a passing report.");
                result = save(table, false, "MPS125-REPORT-SIM-AUX-FAIL");
                Require(result.Success && !result.DutPassed, "An auxiliary-step DUT failure must be preserved even when all exported results pass.");

                // Definitions carry only fallback metadata: stale reference limits cannot override runtime XML.
                string[] changedDefinitions = File.ReadAllLines(copiedDefinition);
                int b05Index = Array.FindIndex(changedDefinitions, x => x.StartsWith("B05|"));
                string[] b05Fields = changedDefinitions[b05Index].Split('|');
                b05Fields[7] = "14,5";
                changedDefinitions[b05Index] = string.Join("|", b05Fields);
                File.WriteAllLines(copiedDefinition, changedDefinitions);
                result = save(table, true, "MPS125-REPORT-SIM-STALE-PSV");
                string b05Line = File.ReadAllText(result.TextFilePath).Split('\n').Single(x => x.StartsWith("B05 "));
                Require(b05Line.Contains("[12; 14]") && !b05Line.Contains("14,5"), "Stale report-definition limits overrode current XML.");

                XDocument runtimeXml = XDocument.Load(copiedXml);
                runtimeXml.Descendants("TestItem").Single(x => (string)x.Element("StepId") == b05Fields[1]).Element("UpperLimit").Value = "13900";
                runtimeXml.Save(copiedXml);
                result = save(table, true, "MPS125-REPORT-SIM-XML-UPDATE");
                Require(File.ReadAllText(result.TextFilePath).Split('\n').Single(x => x.StartsWith("B05 ")).Contains("[12; 13,9]"),
                    "Report failed to refresh limits after runtime XML changed.");
                missing.Rows.RemoveAt(0);
                result = save(missing, true, "MPS125-REPORT-SIM-MISSING-ROW");
                Require(!result.Success, "A missing mapped StepId must reject report generation.");
            }
            finally
            {
                if (keepQa)
                {
                    File.Copy(definitionPath, copiedDefinition, true);
                    File.Copy(Path.Combine(sourceDirectory, "MPS-125_autoTest.xml"), copiedXml, true);
                }
                else if (Directory.Exists(outputRoot)) Directory.Delete(outputRoot, true);
            }
        }

        private static string ReportNumber(double value) => value.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');

        private static void ValidateStabilityWaitPolicy()
        {
            MethodInfo wait = FindMethod(typeof(SKMPS125Sequence), "WaitForStableMeasurementsAsync");
            Func<int, Func<CancellationToken, Task<double[]>>, CancellationToken, Action<Exception>, bool> run =
                (timeout, read, token, failure) => ((Task<bool>)wait.Invoke(null, new object[]
                {
                    timeout, 0, 1, read,
                    new Func<double[], double[], bool>((a, b) => Math.Abs(a[0] - b[0]) <= 1), failure, token
                })).GetAwaiter().GetResult();

            int reads = 0;
            Require(run(1000, _ => { reads++; return Task.FromResult(new[] { 10.0 }); }, CancellationToken.None, null) && reads == 2,
                "Early exit requires two complete stable rounds.");
            var samples = new Queue<double[]>(new[] { new[] { 10.0 }, null, new[] { 10.0 }, new[] { 10.0 } });
            reads = 0;
            Require(run(1000, _ => { reads++; return Task.FromResult(samples.Dequeue()); }, CancellationToken.None, null) && reads == 4,
                "A failed round must reset stability instead of accepting isolated passes.");
            samples = new Queue<double[]>(new[] { new[] { 10.0 }, new[] { 15.0 }, new[] { 15.5 } });
            reads = 0;
            Require(run(1000, _ => { reads++; return Task.FromResult(samples.Dequeue()); }, CancellationToken.None, null) && reads == 3,
                "Passing but drifting readings must not terminate the wait.");
            reads = 0;
            int transientFailures = 0;
            Require(run(1000, _ =>
            {
                reads++;
                if (reads == 2) throw new InvalidOperationException("Wrapped DAQ failure", new DaqInvalidMeasurementException("+9.9E37"));
                return Task.FromResult(new[] { 10.0 });
            }, CancellationToken.None, _ => transientFailures++) && reads == 4 && transientFailures == 1,
                "Transient overrange must reset consecutive rounds and resume probing before the deadline.");
            reads = 0;
            Require(!run(40, _ => { reads++; throw new DaqInvalidMeasurementException("+9.9E37"); }, CancellationToken.None, null) && reads > 1,
                "Persistent invalid readings must exhaust the original budget without returning a stable result.");
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, 9.9E37 })
                Require(!run(35, _ => Task.FromResult(new[] { invalid }), CancellationToken.None, null),
                    "Invalid DAQ readings must not become stable results.");

            var timer = System.Diagnostics.Stopwatch.StartNew();
            Require(!run(80, _ => Task.FromResult<double[]>(null), CancellationToken.None, null) && timer.ElapsedMilliseconds >= 70,
                "Never-stable readings must complete the original wait.");
            bool reported = false;
            timer.Restart();
            Require(!run(80, _ => throw new TimeoutException("DAQ probe timeout"), CancellationToken.None, _ => reported = true) &&
                reported && timer.ElapsedMilliseconds >= 70, "Probe exceptions must preserve the original wait.");
            bool finished = false;
            Require(!run(35, async _ => { await Task.Delay(60); finished = true; return new[] { 10.0 }; }, CancellationToken.None, null) && finished,
                "A late in-flight probe must finish and be rejected before formal I/O starts.");
            using (var cancel = new CancellationTokenSource())
            {
                bool canceled = false;
                cancel.CancelAfter(20);
                try { run(1000, async token => { await Task.Delay(500, token); return new[] { 10.0 }; }, cancel.Token, null); }
                catch (OperationCanceledException) { canceled = true; }
                Require(canceled, "User/safety cancellation must propagate instead of falling back to a normal wait.");
            }
        }

        private static void ValidateHeaterEarlyMeasurements(ResolvedTestPlan plan)
        {
            var context = new TestSequenceContext { TestPlan = plan };
            var device = new SK441Device { SkipComInit = false, SimulationBoardType = "MPS-125" };
            var daq = new HeaterDaq();
            typeof(SK441Device).GetProperty("Daq").GetSetMethod(true).Invoke(device, new object[] { daq });
            IList steps = BuildSteps(context, device);
            Func<string, object> execute = id =>
            {
                object step = steps.Cast<object>().Single(x => Read<string>(x.GetType(), x, "StepId") == id);
                Task task = (Task)Read<Delegate>(step.GetType(), step, "Action").DynamicInvoke(CancellationToken.None);
                task.GetAwaiter().GetResult();
                return task.GetType().GetProperty("Result").GetValue(task);
            };
            object waitResult = execute("MPS125.CH17.S016.ACTION");
            Require(Read<string>(waitResult.GetType(), waitResult, "Value").Contains("提前稳定"), "Stable heater did not exit early.");
            Require(daq.FetchCount == 3 && daq.LastSampleCount == 8 && daq.SingleReadCount == 2 && !daq.Initiated,
                "Early wait must acquire two full configured feedback averages and two current readings, then restore DAQ state.");
            Require(daq.TransitionReadCount == 3 && daq.FullAverageRanges.SequenceEqual(new[] { 1.0, 10.0, 1.0 }),
                "Invalid narrow readings must trigger a complete 10V average; gate readings must never replace complete results.");
            int queries = daq.QueryCount;
            object feedback = execute("MPS125.CH17.S018.L06");
            object current = execute("MPS125.CH17.S019.L07");
            Require(daq.QueryCount == queries && Read<bool>(feedback.GetType(), feedback, "IsPass") &&
                Read<double?>(current.GetType(), current, "NumericValue") == 167,
                "Formal result rows must consume the stable measurements without repeating I/O.");
            execute("MPS125.CH17.S018.L06");
            Require(daq.FetchCount == 4, "A cached result must be consumed only once; retries must remeasure.");

            // Stable overcurrent can finish the wait, but must retain its NG result.
            TestPlanStepDefinition definition = plan.GetStep("MPS125.CH17.S025.ACTION");
            int? oldDelay = definition.DelayMs;
            try
            {
                definition.DelayMs = 4000;
                waitResult = execute(definition.StepId);
                Require(Read<string>(waitResult.GetType(), waitResult, "Value").Contains("提前稳定"),
                    "A stable current range offset unnecessarily consumed the full wait.");
                queries = daq.QueryCount;
                current = execute("MPS125.CH17.S028.L11");
                Require(daq.QueryCount == queries && !Read<bool>(current.GetType(), current, "IsPass") &&
                    Read<double?>(current.GetType(), current, "NumericValue") == 103,
                    "Early exit must preserve the original current range and record the cached NG, never turn it into PASS.");
            }
            finally { definition.DelayMs = oldDelay; }

            definition = plan.GetStep("MPS125.CH17.S016.ACTION");
            oldDelay = definition.DelayMs;
            try
            {
                definition.DelayMs = 1500;
                daq.HoldTransition = true;
                int fetches = daq.FetchCount;
                waitResult = execute(definition.StepId);
                Require(Read<string>(waitResult.GetType(), waitResult, "Value").Contains("原计划测量") && daq.FetchCount == fetches,
                    "A feedback that stays at 8.9V must preserve the deadline without starting or caching narrow-range averages.");
                execute("MPS125.CH17.S018.L06");
                Require(daq.FetchCount == fetches + 1, "Unready feedback must leave the formal row to acquire a fresh complete average.");
            }
            finally { definition.DelayMs = oldDelay; daq.HoldTransition = false; }

            daq.FeedbackStuckHigh = true;
            feedback = execute("MPS125.CH17.S018.L06");
            Require(!Read<bool>(feedback.GetType(), feedback, "IsPass") &&
                Math.Abs(Read<double?>(feedback.GetType(), feedback, "NumericValue").Value - 8634) < 0.001 &&
                daq.FullAverageRanges.Last() == 10 && daq.LastSampleCount == 8 && !daq.Initiated,
                "L06 overrange must display the real full 10V average as NG, rather than an exception, zero, or an old gate value.");
            daq.FeedbackStuckHigh = false;

            MethodInfo ids = FindMethod(typeof(SKMPS125Sequence), "GetHeaterStabilityResultIds");
            string[] bounded = ((string[])ids.Invoke(null, new object[] { "MPS125.CH17.S036.ACTION" }));
            Require(bounded.SequenceEqual(new[] { "L14", "L15", "L16" }), "Final wait must cover both feedbacks and current.");
            foreach (Mps125ActionDefinition item in Mps125ActionCatalog.Create().Where(x => x.Kind == Mps125ActionKind.Delay && x.Chapter != 17))
                Require(((string[])ids.Invoke(null, new object[] { item.StepId })).Length == 0,
                    "Early heater measurement escaped its authorized wait group.");
        }

        private static void ValidateDaqMeasurementTransitions()
        {
            var daq = new StatefulDaq();
            int[] channels = { 101, 102, 103, 119, 114, 117, 113, 111, 120 };
            foreach (int channel in channels)
            {
                double value = daq.MeasureVoltageDc("@" + channel, out _, true);
                Require(value == channel / 100.0 && !daq.Initiated,
                    "Consecutive voltage measurement used a stale channel or left the DAQ initiated.");
            }
            daq.StartBufferedVoltageDcScan("@109", 100, 1, 0.1, 8, true);
            Require(daq.MeasureVoltageDc("@102", out _, true) == 1.02,
                "Single reading inherited buffered trigger count/format.");
            daq.StartTimedVoltageDcScan("@105", 100, 1, 0.1, 81, true);
            Require(daq.MeasureVoltageDc("@120", out _, true) == 1.2,
                "Single reading inherited timed trigger count/format.");
            daq.FailNextRead = true;
            bool timedOut = false;
            try { daq.MeasureVoltageDc("@101", out _, true); }
            catch (TimeoutException) { timedOut = true; }
            Require(timedOut && !daq.Initiated, "Timeout must propagate and restore idle state.");
            Require(daq.MeasureVoltageDc("@102", out _, true) == 1.02,
                "Timeout poisoned the next channel measurement.");
            daq.ResponseOverride = "NaN";
            bool invalid = false;
            try { daq.MeasureVoltageDc("@101", out _, true); }
            catch (FormatException) { invalid = true; }
            Require(invalid && !daq.Initiated, "Invalid readings must fail and return to idle.");
            daq.ResponseOverride = "+9.9E37";
            Require(daq.MeasureResistanceAuto("@118", out _) == 9.9E37 && !daq.Initiated,
                "Open-contact resistance overload was lost during recovery.");
            daq.ResponseOverride = null;
            Require(daq.MeasureVoltageDc("@101", out _, true) == 1.01,
                "Resistance-to-voltage transition used the old function/channel.");
            Require(!daq.Commands.Any(x => x.StartsWith("MEAS:") || x == "*RST"),
                "Measurement must not reintroduce MEAS? or reset functional relays.");
            Require(daq.MeasureVoltageDc("@109", out _, rangeVolts: 100) == 1.09 &&
                    daq.Commands.Contains("CONF:VOLT:DC 100,MIN,(@109)"),
                "Unified single measurement lost its fixed range.");
            daq.ResponseOverride = "NaN";
            bool rejectedByDefault = false;
            try { daq.MeasureVoltageDc("@101", out _); }
            catch (FormatException) { rejectedByDefault = true; }
            Require(rejectedByDefault, "Strict single-reading validation must apply to BCM as well as MPS.");
            MethodInfo initialWait = FindMethod(typeof(SK441Device), "GetBufferedAverageInitialWaitMs");
            Require((int)initialWait.Invoke(null, new object[] { 8, 100, 1.0 }) == 800 &&
                    (int)initialWait.Invoke(null, new object[] { 3, 100, 1.0 }) == 300 &&
                    (int)initialWait.Invoke(null, new object[] { 8, 100, 10.0 }) >= 900,
                "Batch readiness polling must retain all intervals and an integration-dependent margin.");
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

        private static void ValidateRampRows(ResolvedTestPlan plan, string[] stepIds, int delayMs = 100)
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
                Require(item.DelayMs == delayMs,
                    "Ramp delay must be " + delayMs + " ms for " + stepIds[index] + ".");
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
            Require(device.ControlMpsRelayAsync(3, Mps125StationMap.WithdrawalDaqRelay, true).GetAwaiter().GetResult(), "Relay_3/DAQ308 live functional switching was incorrectly blocked.");
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

        private static void ValidateK01IsolatedBaseline()
        {
            // Simulate shutdown/physical power relays, then use instrument doubles
            // for the real residual-voltage gate and Relay_6 command. No COM port opens.
            Action<float, float, bool, bool, bool> check = (voltage, current, readFails, relayFails, expected) =>
            {
                var device = new SK441Device { SkipComInit = true, SimulationBoardType = "MPS-125" };
                Require(device.ControlMpsRelayAsync(6, Mps125StationMap.IsoBattDaqRelay, true).GetAwaiter().GetResult(),
                    "Cannot prepare connected Relay_6 for the K01 isolation test.");
                var source = new ResidualVbattSupply(voltage, current, readFails);
                var daq = new BaselineRelayDaq { FailOpen = relayFails };
                typeof(SK441Device).GetProperty("HengHuiPower2").GetSetMethod(true).Invoke(device, new object[] { source });
                typeof(SK441Device).GetProperty("Daq").GetSetMethod(true).Invoke(device, new object[] { daq });
                device.LogInfo += message =>
                {
                    if (message.StartsWith("MPS VBATT电源路径已断开", StringComparison.Ordinal))
                        device.SkipComInit = false;
                };

                bool ready = device.PrepareMps125VbattBaselineAsync(
                    6, Mps125StationMap.IsoBattDaqRelay, 150, CancellationToken.None).GetAwaiter().GetResult();
                Require(ready == expected, "K01 isolation accepted an unsafe/unconfirmed path.");
                if (expected)
                {
                    Require(source.ReadCount == 1 && daq.OpenCount == 1,
                        "The isolation helper must verify safe discharge and open Relay_6; the sequence performs the separate zero wait.");
                    string[] trace = device.GetPowerSafetyTrace();
                    int off = Array.LastIndexOf(trace, "OUTPUT SECONDARY OFF");
                    int y9 = Array.LastIndexOf(trace, "RELAY 9 OPEN");
                    int y1 = Array.LastIndexOf(trace, "RELAY 1 OPEN");
                    int relay6 = Array.LastIndexOf(trace, "RELAY 6 OPEN");
                    Require(off >= 0 && off < y9 && y9 < y1 && y1 < relay6,
                        "K01 must turn off VBATT and open Y9/Y1 before opening Relay_6.");
                }
                else if (!relayFails)
                    Require(daq.OpenCount == 0, "K01 must not advance to Relay_6 or sampling after failed discharge confirmation.");
                else
                    Require(!device.GetPowerSafetyTrace().Contains("RELAY 6 OPEN"),
                        "Failed Relay_6 isolation must not be recorded as success.");
            };

            check(15.4f, 0.001f, false, false, true);
            check(21.0f, 0.001f, false, false, false);
            check(15.4f, 0.06f, false, false, false);
            check(15.4f, 0.001f, true, false, false);
            check(15.4f, 0.001f, false, true, false);

            var canceledDevice = new SK441Device { SkipComInit = true, SimulationBoardType = "MPS-125" };
            bool canceled = false;
            try
            {
                canceledDevice.PrepareMps125VbattBaselineAsync(
                    6, Mps125StationMap.IsoBattDaqRelay, 150, new CancellationToken(true)).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { canceled = true; }
            Require(canceled && canceledDevice.GetPowerSafetyTrace().Length == 0,
                "Canceled K01 preparation must not start hardware actions.");
        }

        private static void ValidateKPreparationWait(ResolvedTestPlan plan, bool k01)
        {
            var settings = ConfigurationManager.AppSettings;
            PropertyInfo readOnlyProperty = typeof(System.Collections.Specialized.NameObjectCollectionBase)
                .GetProperty("IsReadOnly", BindingFlags.Instance | BindingFlags.NonPublic);
            bool wasReadOnly = (bool)readOnlyProperty.GetValue(settings);
            string oldTimeout = settings["SKMPS125VbattZeroTimeoutMs"];
            string oldStableCount = settings["SKStableSampleCount"];
            readOnlyProperty.SetValue(settings, false);
            settings["SKMPS125VbattZeroTimeoutMs"] = "1000";
            settings["SKStableSampleCount"] = "1";
            try
            {
                Action<VbattSettlingSupply, bool> check = (source, expectWarning) =>
                {
                    var device = new SK441Device { SkipComInit = true, SimulationBoardType = "MPS-125" };
                    var daq = new BaselineRelayDaq();
                    if (k01)
                    {
                        Require(device.ControlMpsRelayAsync(6, Mps125StationMap.IsoBattDaqRelay, true)
                            .GetAwaiter().GetResult(), "Cannot prepare K01 relay isolation.");
                        source.IncludePowerOffReading = true;
                        typeof(SK441Device).GetProperty("Daq").GetSetMethod(true).Invoke(device, new object[] { daq });
                        source.BeforeRead = () => Require(daq.OpenCount == 1,
                            "K01 zero-voltage polling must occur after Relay_6 has opened.");
                    }
                    typeof(SK441Device).GetProperty("HengHuiPower2").GetSetMethod(true).Invoke(device, new object[] { source });
                    // No COM ports: simulate switching, then use actual wait logic
                    // with the instrument double after the zero output is enabled.
                    device.LogInfo += message =>
                    {
                        if (k01 && message.StartsWith("MPS VBATT电源路径已断开", StringComparison.Ordinal))
                            device.SkipComInit = false;
                        if (!k01 && message.StartsWith("[Simulation] VBATT source B:", StringComparison.Ordinal) &&
                            message.EndsWith(", ON", StringComparison.Ordinal))
                            device.SkipComInit = false;
                        // Restore simulated sampling only after the real preparation
                        // wait finishes; its voltage/current queries remain real doubles.
                        if (k01 && ((message.StartsWith("等待MPS-125 VBATT实测条件：目标=0V", StringComparison.Ordinal) &&
                                     Math.Abs(source.LastVoltage) <= 0.5f && Math.Abs(source.LastCurrent) <= 0.05f) ||
                                    message.StartsWith("MPS-125 VBATT实测条件在", StringComparison.Ordinal) ||
                                    message.StartsWith("MPS-125 VBATT实测条件读取失败", StringComparison.Ordinal)))
                            device.SkipComInit = true;
                    };
                    IList steps = BuildSteps(new TestSequenceContext { TestPlan = plan }, device);
                    object connectStep = steps.Cast<object>().Single(x =>
                        Read<string>(x.GetType(), x, "StepId") ==
                        (k01 ? "MPS125.CH16.S003.K01" : "MPS125.CH16.S005.ACTION"));
                    Task connectTask = (Task)Read<Delegate>(connectStep.GetType(), connectStep, "Action")
                        .DynamicInvoke(CancellationToken.None);
                    connectTask.GetAwaiter().GetResult();
                    object result = connectTask.GetType().GetProperty("Result").GetValue(connectTask);
                    Require(source.ReadCount > (k01 ? 1 : 0),
                        "K01/K02 must really poll the zero-voltage preparation condition.");
                    Require(Read<bool>(result.GetType(), result, "IsPass") &&
                            Read<bool>(result.GetType(), result, "IsWarning") == expectWarning,
                        "Preparation timeout/read failure must warn and continue; settling must succeed normally.");
                    if (!expectWarning)
                        Require(source.ReadCount == (k01 ? 4 : 3),
                            "K01/K02 preparation must wait for both low voltage and low current.");
                    if (k01)
                        Require(Read<double?>(result.GetType(), result, "NumericValue").HasValue &&
                                Read<string>(result.GetType(), result, "StoreKey") == Mps125ResultKeys.For("K01"),
                            "K01 must retain its measured numeric result even after a preparation warning.");
                    string[] trace = device.GetPowerSafetyTrace();
                    if (k01)
                        Require(Array.LastIndexOf(trace, "OUTPUT SECONDARY OFF") < Array.LastIndexOf(trace, "RELAY 6 OPEN") &&
                                !trace.Contains("OUTPUT SECONDARY ON"),
                            "K01 must remain isolated with VBATT off throughout waiting and sampling.");
                    else
                    {
                        int off = Array.IndexOf(trace, "OUTPUT SECONDARY OFF");
                        int relay6 = Array.IndexOf(trace, "RELAY 6 CLOSED");
                        int on = Array.IndexOf(trace, "OUTPUT SECONDARY ON");
                        Require(off >= 0 && off < relay6 && relay6 < on &&
                                !trace.Skip(on + 1).Any(x => x == "OUTPUT SECONDARY OFF" || x == "RELAY 6 OPEN"),
                            "16.5 must switch safely and retain the K02 path after a preparation warning.");
                    }

                    device.SkipComInit = true;
                    object k02Step = steps.Cast<object>().Single(x =>
                        Read<string>(x.GetType(), x, "StepId") == "MPS125.CH16.S006.K02");
                    Task k02Task = (Task)Read<Delegate>(k02Step.GetType(), k02Step, "Action")
                        .DynamicInvoke(CancellationToken.None);
                    k02Task.GetAwaiter().GetResult();
                    object k02Result = k02Task.GetType().GetProperty("Result").GetValue(k02Task);
                    Require(Read<bool>(k02Result.GetType(), k02Result, "IsPass") &&
                            Read<double?>(k02Result.GetType(), k02Result, "NumericValue").HasValue,
                        "K02 must still judge its measurement after successful or timed-out preparation.");
                };
                check(new VbattSettlingSupply(new[] { 3.286f, 0.4f, 0.4f }, new[] { 0.024f, 0.06f, 0.024f }), false);
                var timeoutSource = new VbattSettlingSupply(new[] { 3.286f }, new[] { 0.024f });
                check(timeoutSource, true);
                Require(timeoutSource.ReadCount >= 3, "Timeout must result from repeated polling, not a read failure.");
                check(new VbattSettlingSupply(new[] { 0.4f }, new[] { 0.024f }) { ReadFails = true }, true);

                var nominalDevice = new SK441Device { SkipComInit = false, SimulationBoardType = "MPS-125" };
                typeof(SK441Device).GetProperty("HengHuiPower2").GetSetMethod(true).Invoke(nominalDevice,
                    new object[] { new VbattSettlingSupply(new[] { 124.339f }, new[] { 0.026f }) });
                Require(nominalDevice.WaitForMps125VbattVoltageAsync(125, CancellationToken.None).GetAwaiter().GetResult(),
                    "K03 nominal-voltage stability gate must remain functional.");
                using (var cancel = new CancellationTokenSource())
                {
                    var canceledSource = new VbattSettlingSupply(new[] { 3.286f }, new[] { 0.024f })
                    {
                        ReadFails = true,
                        BeforeRead = () => cancel.Cancel()
                    };
                    typeof(SK441Device).GetProperty("HengHuiPower2").GetSetMethod(true).Invoke(nominalDevice,
                        new object[] { canceledSource });
                    bool waitCanceled = false;
                    try
                    {
                        nominalDevice.WaitForMps125VbattVoltageAsync(0, cancel.Token, preparationOnly: true)
                            .GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException) { waitCanceled = true; }
                    Require(waitCanceled, "Cancellation during preparation readback must not become a continuation warning.");
                }
            }
            finally
            {
                if (oldTimeout == null) settings.Remove("SKMPS125VbattZeroTimeoutMs");
                else settings["SKMPS125VbattZeroTimeoutMs"] = oldTimeout;
                if (oldStableCount == null) settings.Remove("SKStableSampleCount");
                else settings["SKStableSampleCount"] = oldStableCount;
                readOnlyProperty.SetValue(settings, wasReadOnly);
            }

            var canceledDevice = new SK441Device { SkipComInit = true, SimulationBoardType = "MPS-125" };
            bool canceled = false;
            try
            {
                canceledDevice.SafeConnectMps125VbattRelayAsync(
                    6, Mps125StationMap.IsoBattDaqRelay, 150, new CancellationToken(true)).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { canceled = true; }
            Require(canceled && canceledDevice.GetPowerSafetyTrace().Length == 0,
                "Canceled 16.5 must not start hardware actions.");
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
                var logs = new List<string>();
                var sequence = new SKMPS125Sequence(grid);
                sequence.LogInfo += logs.Add;
                sequence.LogSuccess += logs.Add;
                sequence.LogError += logs.Add;
                bool passed = sequence.RunAsync(context).GetAwaiter().GetResult();
                Require(!logs.Any(x => x.Contains("已保存测试变量")), "Compact logs still duplicate result storage.");
                Require(logs.Any(x => x.Contains("StepId=MPS125.CH08") && x.Contains("值=") && x.Contains("耗时")),
                    "Compact completion logs lost the step ID, value or timing.");
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

        private sealed class VbattSettlingSupply : HengHuiPowerSupply
        {
            private readonly float[] voltages;
            private readonly float[] currents;
            public int ReadCount;
            public bool ReadFails;
            public Action BeforeRead;
            public bool IncludePowerOffReading;
            public float LastVoltage;
            public float LastCurrent;

            public VbattSettlingSupply(float[] voltages, float[] currents)
            {
                this.voltages = voltages;
                this.currents = currents;
            }

            public override void ReadVoltageCurrent(out float voltage, out float current)
            {
                int readIndex = ReadCount++;
                if (IncludePowerOffReading && readIndex == 0)
                {
                    voltage = LastVoltage = 15.4f;
                    current = LastCurrent = 0.001f;
                    return;
                }
                int index = Math.Min(readIndex - (IncludePowerOffReading ? 1 : 0), voltages.Length - 1);
                BeforeRead?.Invoke();
                if (ReadFails) throw new TimeoutException("Injected preparation read failure.");
                voltage = LastVoltage = voltages[index];
                current = LastCurrent = currents[index];
            }
        }

        private sealed class ResidualVbattSupply : HengHuiPowerSupply
        {
            private readonly float voltage;
            private readonly float current;
            private readonly bool readFails;
            public int ReadCount;

            public ResidualVbattSupply(float voltage, float current, bool readFails)
            {
                this.voltage = voltage;
                this.current = current;
                this.readFails = readFails;
            }

            public override void ReadVoltageCurrent(out float voltage, out float current)
            {
                ReadCount++;
                if (readFails || ReadCount > 1)
                    throw new TimeoutException("Injected VBATT read failure/redundant zero polling.");
                voltage = this.voltage;
                current = this.current;
            }
        }

        private sealed class BaselineRelayDaq : DaqMultimeter
        {
            public bool FailOpen;
            public int OpenCount;

            protected override void SendCommand(string command)
            {
                Require(command == "ROUT:OPEN (@304)", "Unexpected K01 relay command " + command);
                OpenCount++;
                if (FailOpen) throw new TimeoutException("Injected Relay_6 open failure.");
            }
        }

        private sealed class HeaterDaq : DaqMultimeter
        {
            public int QueryCount;
            public int FetchCount;
            public int SingleReadCount;
            public int LastSampleCount;
            public bool Initiated;
            public int TransitionReadCount;
            public bool HoldTransition;
            public bool FeedbackStuckHigh;
            private bool failFirstAverage = true;
            private bool invalidNarrowReading;
            public readonly List<double> FullAverageRanges = new List<double>();
            private readonly Queue<string> transition = new Queue<string>(new[] { "8.9", "+9.90000000E+37", "0.012" });
            private double range;
            private int channel;
            private int count;

            protected override void SendCommand(string command)
            {
                if (command == "ABOR") Initiated = false;
                if (command.StartsWith("CONF:") && Initiated) throw new InvalidOperationException("Configure while initiated.");
                if (command.StartsWith("CONF:VOLT:DC ")) double.TryParse(command.Substring(13).Split(',')[0],
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out range);
                if (command.StartsWith("ROUT:SCAN ")) channel = int.Parse(command.Substring(command.IndexOf('@') + 1).TrimEnd(')'));
                if (command.StartsWith("TRIG:COUN ")) count = int.Parse(command.Substring(10));
                if (command == "INIT") Initiated = true;
            }

            protected override string Query(string command, int readTimeoutMs = 0)
            {
                QueryCount++;
                if (command == "DATA:POIN?") return count.ToString();
                if (command == "FETCH?")
                {
                    FetchCount++;
                    FullAverageRanges.Add(range);
                    LastSampleCount = count;
                    if (FeedbackStuckHigh)
                    {
                        invalidNarrowReading = range == 1;
                        return string.Join(",", Enumerable.Repeat(range == 1 ? "+9.90000000E+37" : "8.634", count));
                    }
                    if (failFirstAverage)
                    {
                        failFirstAverage = false;
                        invalidNarrowReading = true;
                        return string.Join(",", Enumerable.Repeat("+9.90000000E+37", count));
                    }
                    // L06=12 mV, L10=150 mV, L07=167 mA, L11=103 mA.
                    double value = channel == 107 ? 0.012 : 0.150;
                    return string.Join(",", Enumerable.Repeat(value.ToString(System.Globalization.CultureInfo.InvariantCulture), count));
                }
                if (command == "READ?" && (channel == 107 || channel == 108))
                {
                    if (range == 1 && invalidNarrowReading)
                    {
                        invalidNarrowReading = false;
                        return "+9.90000000E+37";
                    }
                    Require(range == 10, "Transition probes must cover the pre-switch feedback voltage.");
                    TransitionReadCount++;
                    if (HoldTransition) return "8.9";
                    return transition.Count > 0 ? transition.Dequeue() : channel == 107 ? "0.012" : "0.150";
                }
                Require(command == "READ?" && channel == 117, "Unexpected heater query " + command);
                SingleReadCount++;
                return SingleReadCount <= 2 ? "0.167" : "0.103";
            }
        }

        private sealed class StatefulDaq : DaqMultimeter
        {
            public readonly List<string> Commands = new List<string>();
            public bool Initiated;
            public bool FailNextRead;
            public string ResponseOverride;
            private string source = "EXT";
            private int count = 81;
            private int channel;
            private bool includesTime = true;

            protected override void SendCommand(string command)
            {
                Commands.Add(command);
                if (command.StartsWith("CONF:") && command.Contains("AUTO,MIN"))
                    throw new InvalidOperationException("DAQ-9600 AUTO range requires DEF resolution.");
                if (command == "ABOR") Initiated = false;
                if (command.StartsWith("CONF:") && Initiated)
                    throw new InvalidOperationException("+261: configuration while initiated.");
                if (command.StartsWith("ROUT:SCAN "))
                    channel = int.Parse(command.Substring(command.IndexOf('@') + 1).TrimEnd(')'));
                if (command.StartsWith("TRIG:SOUR ")) source = command.Substring(10);
                if (command.StartsWith("TRIG:COUN ")) count = int.Parse(command.Substring(10));
                if (command == "FORM:READ:TIME ON") includesTime = true;
                if (command == "FORM:READ:TIME OFF") includesTime = false;
                if (command == "INIT") Initiated = true;
            }

            protected override string Query(string command, int readTimeoutMs = 0)
            {
                Commands.Add(command);
                Require(command == "READ?", "Single measurement must use the prepared channel's READ?.");
                Require(source == "IMM" && count == 1 && !includesTime,
                    "READ? inherited stale scan trigger/format state.");
                Initiated = true;
                if (FailNextRead)
                {
                    FailNextRead = false;
                    throw new TimeoutException("Injected DAQ timeout.");
                }
                return ResponseOverride ?? (channel / 100.0).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
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
            public Task SetOutcomeByStepIdAsync(
                int channelIndex,
                string stepId,
                string value,
                StepExecutionStatus status,
                long elapsedMs) => SetValueAndStatusByStepIdAsync(channelIndex, stepId, value, status);
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
