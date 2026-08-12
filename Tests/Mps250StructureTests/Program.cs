using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TestPlatform;
using TestPlatform.TestSequences;

namespace Mps250StructureTests
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                string root = args.Length > 0 ? Path.GetFullPath(args[0]) : FindRoot(AppDomain.CurrentDomain.BaseDirectory);
                string xml = Path.Combine(root, "ProjectConfig", "SK", "MPS-250", "MPS-250_autoTest.xml");
                ResolvedTestPlan plan = TestPlanService.Resolve(xml, TestProfileIds.Normal);
                var context = new TestSequenceContext { TestPlan = plan, SN = "MPS250-SIM-0001" };
                var device = new SK441Device { SkipComInit = true, SimulationBoardType = "MPS-250" };
                IList steps = BuildSteps(context, device);

                ValidateStructure(plan, steps);
                ValidateCatalog();
                ValidatePowerSafety();
                RunEndToEnd(xml, false);
                RunEndToEnd(xml, true);

                Console.WriteLine("PASS MPS250: 193 rows, 60 PDF results, chapters 6-17, full simulation and J06 fault cleanup.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL MPS250: " + Unwrap(ex).Message);
                return 1;
            }
        }

        private static IList BuildSteps(TestSequenceContext context, SK441Device device)
        {
            var sequence = new SKMPS250Sequence();
            Type type = sequence.GetType();
            object steps = FindMethod(type, "BuildCommonSteps").Invoke(sequence, new object[] { context, device });
            FindMethod(type, "AddElectricalTestSteps").Invoke(sequence, new object[] { steps, context, device });
            FindMethod(type, "BindStableStepIds").Invoke(null, new[] { context, steps });
            return (IList)steps;
        }

        private static void ValidateStructure(ResolvedTestPlan plan, IList steps)
        {
            List<TestPlanStepDefinition> definitions = plan.Definition.Steps.OrderBy(x => x.SequenceOrder).ToList();
            Require(steps.Count == 193, "Expected 193 rows, got " + steps.Count + ".");
            Require(definitions.Count == steps.Count, "Code/XML row count mismatch.");
            Require(definitions.Select(x => x.StepId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 193, "StepIds are not unique.");
            Require(definitions.Count(x => x.AlwaysRun) == 6, "Expected six cleanup rows.");
            Require(definitions.Count(x => x.GroupId == Mps250GroupIds.Chapter(17)) == 43, "Chapter 17 must expose 43 detailed rows.");

            var resultIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Mps250ActionDefinition item in Mps250ActionCatalog.Create().Where(x => x.IsResult))
                Require(resultIds.Add(item.ResultId), "Duplicate result ID " + item.ResultId + ".");
            Require(resultIds.Count == 60, "Expected 60 PDF result IDs.");

            foreach (object step in steps)
            {
                Type type = step.GetType();
                string stepId = Read<string>(type, step, "StepId");
                TestPlanStepDefinition definition = Read<TestPlanStepDefinition>(type, step, "PlanDefinition");
                Require(definition != null, "Missing XML definition " + stepId + ".");
                Require(string.Equals(Read<string>(type, step, "ExpectedGroupId"), definition.GroupId, StringComparison.OrdinalIgnoreCase), "Group mismatch " + stepId + ".");
                Require(string.Equals(Read<string>(type, step, "Name"), definition.Name, StringComparison.Ordinal), "Name mismatch " + stepId + ".");
            }

            ValidateLimit(plan, "B06", 10, 60, "mA");
            ValidateLimit(plan, "K01", 1700, 2200, "mV");
            ValidateLimit(plan, "K02", 3000, 3300, "mV");
            ValidateLimit(plan, "J06", 200000, 205000, "Count");
            Require(plan.Definition.Steps.Single(x => x.StepId.EndsWith(".D03", StringComparison.OrdinalIgnoreCase)).ExpectedValue == "mcu_app U.1.1.4", "Firmware expectation mismatch.");
            ValidateDependency(plan, "H06", "H02");
            ValidateDependency(plan, "J04", "J01");
            ValidateDependency(plan, "J05", "J02");
            ValidateDependency(plan, "J06", "J03");
        }

        private static void ValidateCatalog()
        {
            IReadOnlyList<Mps250ActionDefinition> catalog = Mps250ActionCatalog.Create();
            Require(catalog.Any(x => x.Kind == Mps250ActionKind.SetVbatt), "VBATT source B actions missing.");
            Require(catalog.Where(x => x.Kind == Mps250ActionKind.DaqShuntCurrent).All(x => x.Channel == 111 && Math.Abs(x.Scale - 1.0) < 1e-9), "Current must use DAQ111/1 ohm shunt.");
            Require(catalog.Any(x => x.Kind == Mps250ActionKind.DaqResistance && x.Channel == 107), "Alarm resistance mapping missing.");
            Require(catalog.Where(x => x.Kind == Mps250ActionKind.MbdCommand).All(x => x.AlwaysRun || (x.Argument ?? "").IndexOf(';') < 0), "Normal commands must be one UI row each.");
            Require(Mps250StationMap.VbusReference == 116, "VBUS reference mapping changed.");
            Require(Mps250StationMap.U24Pin7 == 108, "U24.7 mapping must be DAQ108.");
            Require(Mps250StationMap.McuIsoBatt == 117, "MCU_ISOBATT mapping must be DAQ117.");

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Mps250ActionDefinition item in catalog)
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

        private static void ValidateDependency(ResolvedTestPlan plan, string resultId, string dependencyResultId)
        {
            TestPlanStepDefinition item = plan.Definition.Steps.Single(x => x.StepId.EndsWith("." + resultId, StringComparison.OrdinalIgnoreCase));
            string dependencyStepId = plan.Definition.Steps.Single(x => x.StepId.EndsWith("." + dependencyResultId, StringComparison.OrdinalIgnoreCase)).StepId;
            Require(item.DependsOn.SequenceEqual(new[] { dependencyStepId }, StringComparer.OrdinalIgnoreCase), "Dependency mismatch " + resultId + " -> " + dependencyResultId + ".");
        }

        private static void ValidatePowerSafety()
        {
            var device = new SK441Device { SkipComInit = true, SimulationBoardType = "MPS-250" };
            Require(!device.SetVbusGeneratorAsync(275, 2, 300, true).GetAwaiter().GetResult(), "VBUS energized before Relay_1 closed.");
            Require(!device.SetVbattGeneratorAsync(250, 0.1f, 300, true).GetAwaiter().GetResult(), "High-voltage VBATT energized before Relay_5/Relay_6 closed.");
            Require(device.TurnOffAllInstrumentsAsync().GetAwaiter().GetResult(), "Cannot reset outputs.");
            Require(device.SafeSwitchMpsVbusRelayAsync(1, true, 275, 2, 300, true).GetAwaiter().GetResult(), "Safe MPS VBUS connection failed.");
            Require(!device.StopFixturePressDownAsync("MPS-250").GetAwaiter().GetResult(), "Fixture released while VBUS was energized.");
            Require(device.ControlRelayAsync(1, false).GetAwaiter().GetResult(), "Safe Relay_1 opening was blocked while VBUS was energized.");
            Require(device.ControlRelayAsync(3, true).GetAwaiter().GetResult(), "Relay_3 live functional switching was incorrectly blocked.");
            Require(device.SafeDisconnectMpsVbattRelayAsync(6, 300).GetAwaiter().GetResult(), "Safe MPS VBATT disconnection failed.");
            Require(device.TurnOffAllInstrumentsAsync().GetAwaiter().GetResult(), "Cannot power off before fixture release.");
            Require(device.ControlRelayAsync(1, false).GetAwaiter().GetResult(), "Cannot open Relay_1 after VBUS power-off.");
            Require(device.StopFixturePressDownAsync("MPS-250").GetAwaiter().GetResult(), "Fixture remained locked after safe power-off.");
            Require(device.GetPowerSafetyTrace().Any(x => x.StartsWith("BLOCK", StringComparison.Ordinal)), "No unsafe VBUS action was recorded.");

            var emergency = new SK441Device { SkipComInit = true, SimulationBoardType = "MPS-250" };
            Require(emergency.EnableFixturePressDownAsync("MPS-250").GetAwaiter().GetResult(), "MPS-250 fixture Y1 did not close.");
            Require(emergency.EmergencyStopOutputsAsync("MPS-250", "test emergency").GetAwaiter().GetResult(), "MPS-250 emergency output shutdown failed.");
            string[] fixtureTrace = emergency.GetFixtureActuationTrace();
            Require(fixtureTrace.Contains("FIXTURE STATION 1 RELAY 1 CLOSED") && fixtureTrace.Contains("FIXTURE STATION 1 RELAY 1 OPEN"), "MPS-250 fixture Y1 emergency mapping failed.");
            Require(!fixtureTrace.Any(x => x.Contains("RELAY 2")), "MPS-250 incorrectly used fixture Y2 for press-down.");
            Require(!emergency.SetVbusGeneratorAsync(275, 2, 300, true).GetAwaiter().GetResult(), "Emergency latch allowed VBUS output ON.");
        }

        private static void RunEndToEnd(string xmlPath, bool injectFault)
        {
            string oldMode = Environment.GetEnvironmentVariable("TESTPLATFORM_SK_MODE");
            string oldFault = Environment.GetEnvironmentVariable("TESTPLATFORM_MPS250_FAULT_STEP");
            Environment.SetEnvironmentVariable("TESTPLATFORM_SK_MODE", "Simulation");
            try
            {
                ResolvedTestPlan plan = TestPlanService.Resolve(xmlPath, TestProfileIds.Normal);
                string faultStep = plan.Definition.Steps.Single(x => x.StepId.EndsWith(".J06", StringComparison.OrdinalIgnoreCase)).StepId;
                Environment.SetEnvironmentVariable("TESTPLATFORM_MPS250_FAULT_STEP", injectFault ? faultStep : null);
                var grid = new RecordingGrid();
                var context = new TestSequenceContext { SN = injectFault ? "MPS250-FAULT" : "MPS250-PASS", TestPlan = plan, StopOnFail = true };
                bool passed = new SKMPS250Sequence(grid).RunAsync(context).GetAwaiter().GetResult();
                Require(passed != injectFault, injectFault ? "Injected-fault run passed." : "Full simulation failed.");
                Require(grid.CleanupCompleted == 6, "Not all cleanup rows executed.");
                if (injectFault)
                    Require(grid.FailedStepIds.Contains(faultStep), "Injected J06 failure was not reported.");
            }
            finally
            {
                Environment.SetEnvironmentVariable("TESTPLATFORM_SK_MODE", oldMode);
                Environment.SetEnvironmentVariable("TESTPLATFORM_MPS250_FAULT_STEP", oldFault);
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
                if (stepId.StartsWith(Mps250GroupIds.Cleanup + ".", StringComparison.OrdinalIgnoreCase) &&
                    (status == StepExecutionStatus.Passed || status == StepExecutionStatus.PassedAfterRetry)) CleanupCompleted++;
                return Task.CompletedTask;
            }
            public Task<(bool IsValid, double Lower, double Upper, string ErrorMessage)> GetLimitsAsync(int rowIndex) => Task.FromResult((true, 0.0, 0.0, string.Empty));
        }
    }
}

