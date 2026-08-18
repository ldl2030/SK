using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
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
                ValidatePowerSafety();
                RunEndToEnd(xml, false);
                RunEndToEnd(xml, true);

                Console.WriteLine("PASS MPS125: 213 rows, 70 PDF results, chapters 6-17, DAQ-only functional relays, USB-B precheck, full simulation and J08 fault cleanup.");
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
            Require(steps.Count == 213, "Expected 213 rows, got " + steps.Count + ".");
            Require(definitions.Count == steps.Count, "Code/XML row count mismatch.");
            Require(definitions.Select(x => x.StepId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 213, "StepIds are not unique.");
            Require(definitions.Any(x => x.StepId == "MPS125.PRECHECK.S009.ACTION"), "MPS-125 USB-B insertion row is missing.");
            Require(definitions.Count(x => x.AlwaysRun) == 6, "Expected six cleanup rows.");
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
            ValidateLimit(plan, "J08", 200000, 205000, "Count");
            Require(plan.Definition.Steps.Single(x => x.StepId.EndsWith(".D03", StringComparison.OrdinalIgnoreCase)).ExpectedValue == "mcu_app U.2.2.2", "Firmware expectation mismatch.");
        }

        private static void ValidateCatalog()
        {
            IReadOnlyList<Mps125ActionDefinition> catalog = Mps125ActionCatalog.Create();
            Require(catalog.Any(x => x.Kind == Mps125ActionKind.SetVbatt), "VBATT source B actions missing.");
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
            Require(Mps125StationMap.AllControlledRelays == "2,3,4,5,6", "MPS-125 must not declare logical Relay_1.");
            Require(Mps125StationMap.WithdrawalDaqRelay == "@301", "Relay_3 must use DAQ301.");
            Require(Mps125StationMap.ExternalPowerDaqRelays == "@305,306", "Relay_2/2BIS must use DAQ305/306.");
            Require(!catalog.Where(x => x.Kind == Mps125ActionKind.DaqRelays)
                .SelectMany(x => (x.Argument ?? "").Split(','))
                .Any(x => x.Trim() == "1"), "MPS-125 catalog must not operate Relay_1/high-power Y1.");

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Mps125ActionDefinition item in catalog)
            {
                foreach (string dependency in item.RequiresResultIds)
                    Require(seen.Contains(dependency), item.ResultId + " has missing/later dependency " + dependency + ".");
                if (item.IsResult)
                    seen.Add(item.ResultId);
            }
        }

        private static void ValidateLimit(ResolvedTestPlan plan, string resultId, double lower, double upper, string unit)
        {
            TestPlanStepDefinition item = plan.Definition.Steps.Single(x => x.StepId.EndsWith("." + resultId, StringComparison.OrdinalIgnoreCase));
            Require(item.LowerLimit == lower && item.UpperLimit == upper && item.Unit == unit, "Limit mismatch " + resultId + ".");
        }

        private static void ValidatePowerSafety()
        {
            var device = new SK441Device { SkipComInit = true, SimulationBoardType = "MPS-125" };
            Require(device.SetVbusGeneratorAsync(137.5f, 2, 300, true).GetAwaiter().GetResult(), "MPS VBUS must enable directly without a high-power relay.");
            Require(device.SetVbattGeneratorAsync(125, 0.1f, 150, true).GetAwaiter().GetResult(), "MPS VBATT must enable directly without a high-power relay.");
            Require(device.SetVbattGeneratorAsync(0, 0, 150, false).GetAwaiter().GetResult(), "Cannot turn off direct VBATT output.");
            Require(!device.StopFixturePressDownAsync("MPS-125").GetAwaiter().GetResult(), "Fixture released while VBUS was energized.");
            Require(!device.ControlMpsRelayAsync(5, Mps125StationMap.SensPrecDaqRelay, true).GetAwaiter().GetResult(), "Relay_5/DAQ303 closed while VBUS was energized.");
            Require(device.ControlMpsRelayAsync(3, Mps125StationMap.WithdrawalDaqRelay, true).GetAwaiter().GetResult(), "Relay_3/DAQ301 live functional switching was incorrectly blocked.");
            Require(device.SafeSwitchMpsVbusRelayAsync(5, Mps125StationMap.SensPrecDaqRelay, true, 137.5f, 2, 300, true).GetAwaiter().GetResult(), "Relay_5/DAQ303 safe switching failed.");
            Require(device.SetVbusGeneratorAsync(0, 0, 300, false).GetAwaiter().GetResult(), "Cannot turn off VBUS.");
            Require(device.SafeConnectMpsVbattRelayAsync(6, Mps125StationMap.IsoBattDaqRelay, 150).GetAwaiter().GetResult(), "Relay_6/DAQ304 safe connection failed.");
            Require(device.SafeDisconnectMpsVbattRelayAsync(6, Mps125StationMap.IsoBattDaqRelay, 150).GetAwaiter().GetResult(), "Safe MPS VBATT disconnection failed.");
            Require(device.TurnOffAllInstrumentsAsync().GetAwaiter().GetResult(), "Cannot power off before fixture release.");
            Require(device.StopFixturePressDownAsync("MPS-125").GetAwaiter().GetResult(), "Fixture remained locked after safe power-off.");
            Require(!device.GetPowerSafetyTrace().Any(x => x.Contains("RELAY 1 ")), "MPS-125 touched nonexistent high-power Relay_1.");
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
                if (status == StepExecutionStatus.Failed) FailedStepIds.Add(stepId);
                if (stepId.StartsWith(Mps125GroupIds.Cleanup + ".", StringComparison.OrdinalIgnoreCase) &&
                    (status == StepExecutionStatus.Passed || status == StepExecutionStatus.PassedAfterRetry)) CleanupCompleted++;
                return Task.CompletedTask;
            }
            public Task<(bool IsValid, double Lower, double Upper, string ErrorMessage)> GetLimitsAsync(int rowIndex) => Task.FromResult((true, 0.0, 0.0, string.Empty));
        }
    }
}
