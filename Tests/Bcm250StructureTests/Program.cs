using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TestPlatform;
using TestPlatform.TestSequences;

namespace Bcm250StructureTests
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                string root = args.Length > 0
                    ? Path.GetFullPath(args[0])
                    : FindRepositoryRoot(AppDomain.CurrentDomain.BaseDirectory);
                string xmlPath = Path.Combine(
                    root,
                    "ProjectConfig",
                    "SK",
                    "BCM-250",
                    "BCM-250_autoTest.xml");

                ResolvedTestPlan plan = TestPlanService.Resolve(xmlPath, TestProfileIds.Normal);
                var context = new TestSequenceContext
                {
                    TestPlan = plan,
                    SN = "BCM250-SIM-0001"
                };
                var device = new SK441Device
                {
                    SkipComInit = true,
                    SimulationBoardType = "BCM-250"
                };
                IList steps = BuildSteps(context, device);
                ValidateMbdSerialIdentification();
                ValidateMbdResponseCompletion();
                ValidateMbdResponseParsing();
                ValidateDeferredMbdDiscovery();
                ValidateRuntimePlanResolutionGate();
                ValidateSingleChapterProfile(xmlPath);
                ValidateDigitalInputChannelPolarity();
                ValidateStructure(plan, steps);
                ValidateVariableDependencies();
                ValidatePowerSafety();
                RunSimulation(context, steps);
                RunFaultInjection(steps, "Q16");
                RunEndToEndSimulation(xmlPath);

                Console.WriteLine(
                    "PASS BCM250 structure/simulation: 594 steps, 91 results, " +
                    "chapters 7-27, detailed PDF actions, explicit StepIds, XML authority, 6 cleanup steps, " +
                    "full simulation and Q16 fault injection.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL BCM250 structure: " + Unwrap(ex).Message);
                return 1;
            }
        }

        private static void ValidateMbdSerialIdentification()
        {
            Require(
                SKSerialDeviceDiscovery.IsMbdIdentificationResponse(
                    "000054 chars answered. Ready.\rCOMMAND ERROR\r"),
                "MBD identification must accept buffered Ready/error ordering.");
            Require(
                SKSerialDeviceDiscovery.IsMbdIdentificationResponse(
                    "COMMAND ERROR\r000014 chars answered. Ready.\r"),
                "MBD identification must accept the normal CR probe response.");
            Require(
                !SKSerialDeviceDiscovery.IsMbdIdentificationResponse(
                    "000054 chars answered. Ready.\r"),
                "Ready alone must not identify a port as MBD.");
            Require(
                !SKSerialDeviceDiscovery.IsMbdIdentificationResponse("COMMAND ERROR\r"),
                "COMMAND ERROR alone must not identify a port as MBD.");
        }

        private static void ValidateMbdResponseParsing()
        {
            double scalar = MbdResponseParser.ParseScalar(
                "R55",
                "055 = 1549      \r000017 chars answered. Ready.\r");
            Require(Math.Abs(scalar - 1549.0) < 0.000001,
                "MBD scalar parser returned the register number instead of the payload.");

            string textPayload = MbdResponseParser.ParsePayloadText(
                "R40",
                "040 = 20260806 ;calibration date\r\nReady.\r\n");
            Require(textPayload == "20260806",
                "MBD text parser did not isolate the register payload.");

            int[] vector = MbdResponseParser.ParseIntegerVector(
                "R15",
                "015 = 99,99,99,99,99,99,99,99,99,99,99,99,99,99,99,00\r" +
                "000054 chars answered. Ready.\r",
                16);
            Require(vector.Length == 16 && vector[15] == 0,
                "MBD R15 vector parser did not preserve byte16.");

            bool rangeOk = MbdResponseParser.IsSuccessfulCommandResponse(
                "RANGE ERROR: 018 = 406000\rReady.\r",
                out string rangeError);
            Require(!rangeOk && rangeError == "RANGE ERROR",
                "MBD RANGE ERROR was incorrectly accepted as command success.");

            bool registerMismatchRejected = false;
            try
            {
                MbdResponseParser.ParseScalar("R55", "051 = 2753\rReady.\r");
            }
            catch (InvalidOperationException)
            {
                registerMismatchRejected = true;
            }
            Require(registerMismatchRejected,
                "MBD parser did not reject a mismatched response register.");
        }

        private static void ValidateMbdResponseCompletion()
        {
            Require(
                TargetBoardComm.HasTerminalPrompt(
                    "055 = 1549\r000017 chars answered. Ready.\r"),
                "MBD response reader must stop as soon as the terminal prompt arrives.");
            Require(
                TargetBoardComm.HasTerminalPrompt("COMMAND ERROR\rReady.\r"),
                "MBD error responses with a terminal prompt must complete immediately.");
            Require(
                !TargetBoardComm.HasTerminalPrompt("055 = 1549\r"),
                "A register payload without a terminal prompt must use quiet-time completion.");
        }

        private static void ValidateDeferredMbdDiscovery()
        {
            var result = new SKSerialDeviceDiscoveryResult();
            Type type = typeof(SKSerialDeviceDiscoveryResult);
            foreach (string propertyName in new[]
            {
                "AnsPowerPort",
                "HengHuiPowerAPort",
                "HengHuiPowerBPort",
                "ElectronicLoadPort",
                "DaqPort",
                "DigitalInputPort",
                "FixtureRelayPort",
                "Relay16Port"
            })
            {
                type.GetProperty(propertyName).SetValue(result, "COM_TEST", null);
            }
            type.GetProperty("TtlRequired").SetValue(result, false, null);

            Require(result.IsComplete,
                "Startup discovery must complete before the powered MBD probe.");
            Require(!result.GetMissingDeviceNames().Contains("MBD UART/目标板USB-B"),
                "Startup missing-device list must not require unpowered MBD UART.");

            type.GetProperty("HengHuiPowerAPort").SetValue(result, null, null);
            type.GetProperty("HengHuiPowerBPort").SetValue(result, null, null);
            type.GetProperty("ElectronicLoadPort").SetValue(result, null, null);
            var bcm250DebugPorts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ansPowerComName",
                "daqComName",
                "digitalInputComName",
                "fixtureRelayComName",
                "relay16ComName"
            };
            Require(result.IsCompleteFor(bcm250DebugPorts),
                "BCM250 chapter debug must not require unlisted HengHui/load ports.");
        }

        private static void ValidateRuntimePlanResolutionGate()
        {
            MethodInfo method = typeof(MainWindow).GetMethod(
                "RequiresResolvedTestPlan",
                BindingFlags.NonPublic | BindingFlags.Static);
            Require(method != null, "MainWindow plan-resolution gate is missing.");
            Require((bool)method.Invoke(null, new object[] { new SKBCM250Sequence() }),
                "BCM250 runtime path must resolve and bind its XML test plan.");
            Require((bool)method.Invoke(null, new object[] { new SKBCM125Sequence() }),
                "BCM125 runtime path must resolve and bind its XML test plan.");
            Require((bool)method.Invoke(null, new object[] { new SKMPS125Sequence() }),
                "MPS125 runtime path must resolve and bind its XML test plan.");
            Require((bool)method.Invoke(null, new object[] { new SKMPS250Sequence() }),
                "MPS250 runtime path must resolve and bind its XML test plan.");
        }

        private static void ValidateSingleChapterProfile(string xmlPath)
        {
            TestPlanDefinition definition = TestPlanService.LoadDefinition(xmlPath);
            const string selectedGroup = "BCM250.CH08";
            var overrides = definition.Steps.ToDictionary(
                step => step.StepId,
                step => step.GroupId == selectedGroup ||
                        step.GroupId == "BCM250.PRECHECK" ||
                        step.GroupId == "BCM250.CLEANUP",
                StringComparer.OrdinalIgnoreCase);
            ResolvedTestPlan plan = TestPlanService.Resolve(
                xmlPath,
                TestProfileIds.SingleChapter,
                overrides);

            Require(definition.Steps
                    .Where(step => step.GroupId == selectedGroup)
                    .All(step => plan.ShouldRun(step.StepId)),
                "Single-chapter profile did not enable the selected BCM250 chapter.");
            Require(definition.Steps
                    .Where(step => step.GroupId == "BCM250.CH07")
                    .All(step => !plan.ShouldRun(step.StepId)),
                "Single-chapter profile incorrectly forced BCM250 CH7 to run.");
            Require(definition.Steps
                    .Where(step => step.GroupId == "BCM250.PRECHECK" ||
                                   step.GroupId == "BCM250.CLEANUP")
                    .All(step => plan.ShouldRun(step.StepId)),
                "Single-chapter profile must retain PRECHECK and CLEANUP.");
        }

        private static void ValidateDigitalInputChannelPolarity()
        {
            // Actual station response 02 04 02 00 97 ... => DI word 0x0097.
            const int rawWord = 0x0097;
            bool[] rawStates = Enumerable.Range(0, 16)
                .Select(index => (rawWord & (1 << index)) != 0)
                .ToArray();
            bool[] states = SK441Device.NormalizeDigitalInputStates(rawStates, 0x03F7);

            Require(!states[0] && !states[1] && !states[2],
                "DI1-DI3 must normalize to interfaces A/B/C connected.");
            Require(!states[3], "DI4 raw low must mean P2 is installed correctly.");
            Require(!states[4] && states[5],
                "DI5-DI6 must normalize to BCM-250 for word 0x0097.");
            Require(states[6], "DI7 must normalize to travel switch not closed.");
            Require(!states[7], "DI8 must normalize to emergency stop normal.");
            Require(states[8] && states[9],
                "DI9-DI10 must normalize to both main switches not closed.");

            const int fixtureDownRawWord = 0x00D7;
            bool[] fixtureDownRawStates = Enumerable.Range(0, 16)
                .Select(index => (fixtureDownRawWord & (1 << index)) != 0)
                .ToArray();
            bool[] fixtureDownStates = SK441Device.NormalizeDigitalInputStates(
                fixtureDownRawStates,
                0x03F7);
            Require(!fixtureDownStates[6],
                "DI7 raw high must normalize to travel switch closed after fixture press-down.");
        }

        private static IList BuildSteps(TestSequenceContext context, SK441Device device)
        {
            var sequence = new SKBCM250Sequence();
            Type type = sequence.GetType();
            object steps = FindMethod(type, "BuildCommonSteps")
                .Invoke(sequence, new object[] { context, device });
            FindMethod(type, "AddElectricalTestSteps")
                .Invoke(sequence, new object[] { steps, context, device });
            FindMethod(type, "BindStableStepIds")
                .Invoke(null, new[] { context, steps });
            return (IList)steps;
        }

        private static void ValidateStructure(ResolvedTestPlan plan, IList steps)
        {
            List<TestPlanStepDefinition> definitions = plan.Definition.Steps
                .Where(x => !string.Equals(x.RunCondition, "OnChapterFailure", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.SequenceOrder)
                .ToList();
            Require(steps.Count == 594, "Expected 594 executable steps, got " + steps.Count + ".");
            Require(steps.Count == definitions.Count, "Code/XML step counts differ.");

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var resultIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int cleanupCount = 0;
            int numericCount = 0;
            int firmwareCount = 0;
            foreach (object step in steps)
            {
                Type type = step.GetType();
                string stepId = Read<string>(type, step, "StepId");
                string groupId = Read<string>(type, step, "ExpectedGroupId");
                bool alwaysRun = Read<bool>(type, step, "AlwaysRun");
                TestPlanStepDefinition definition =
                    Read<TestPlanStepDefinition>(type, step, "PlanDefinition");

                Require(!string.IsNullOrWhiteSpace(stepId), "A code step has no StepId.");
                Require(ids.Add(stepId), "Duplicate StepId: " + stepId);
                Require(definition != null, "No XML definition: " + stepId);
                Require(string.Equals(groupId, definition.GroupId, StringComparison.OrdinalIgnoreCase),
                    "Group mismatch: " + stepId);

                string tail = stepId.Split('.').Last();
                if (tail.Length == 3 && char.IsLetter(tail[0]))
                    Require(resultIds.Add(tail), "Duplicate result ID: " + tail);

                if (definition.LowerLimit.HasValue || definition.UpperLimit.HasValue)
                {
                    Require(definition.LowerLimit.HasValue && definition.UpperLimit.HasValue,
                        "Incomplete limit: " + stepId);
                    Require(!string.IsNullOrWhiteSpace(definition.Unit), "Missing unit: " + stepId);
                    numericCount++;
                }
                if (!string.IsNullOrWhiteSpace(definition.ExpectedValue))
                    firmwareCount++;
                if (definition.DelayMs.HasValue)
                    Require(definition.DelayMs.Value >= 0, "Invalid delay: " + stepId);
                if (alwaysRun)
                {
                    cleanupCount++;
                    Require(definition.GroupId == Bcm250GroupIds.Cleanup, "Cleanup group mismatch.");
                }
            }

            Require(numericCount == 90, "Expected 90 numeric/logic results, got " + numericCount + ".");
            Require(firmwareCount == 1, "Expected one XML firmware expected value.");
            Require(resultIds.Count == 91, "Expected 91 unique result IDs, got " + resultIds.Count + ".");
            Require(cleanupCount == 6, "Expected six cleanup steps.");
            Require(definitions.Count(x => x.GroupId == Bcm250GroupIds.Chapter(23)) == 61,
                "Chapter 23 must expose all 61 PDF operations.");
            for (int chapter = 7; chapter <= 27; chapter++)
                Require(definitions.Any(x => x.GroupId == Bcm250GroupIds.Chapter(chapter)),
                    "Missing chapter " + chapter + ".");

            ValidateLimit(plan, "O01", 200, 1100, "mA");
            ValidateLimit(plan, "O02", 1, 1, "Logic (0/1)");
            ValidateLimit(plan, "Q16", -6600, -5400, "mA");
            ValidateLimit(plan, "S02", 259000, 263000, "mV");
            ValidateLimit(plan, "T02", 300, 750, "mA");
            ValidateLimit(plan, "T03", -20, 20, "mA");

            Require(Bcm250StationMap.VbusReferenceDaqChannel == 105, "VBUS reference mapping changed.");
            Require(Bcm250StationMap.VmidReferenceDaqChannel == 106, "VMID mapping changed.");
            Require(Bcm250StationMap.StringShuntDaqChannel == 113, "Shunt mapping changed.");
            Require(Bcm250StationMap.MidpointDaqRelay == "301", "Midpoint relay must use DAQ 301.");
            Require(Bcm250StationMap.U12ConnectionDaqRelay == "302", "U1.2 relay must use DAQ 302.");
            Require(Bcm250StationMap.VoltageSelectorDaqRelay == "304", "Voltage selector must use DAQ 304.");
            Require(Bcm250StationMap.StringGeneratorPowerRelays == "1,3,6",
                "BCM250 String supply must use power relays Y1/Y3/Y6.");
            Require(Bcm250StationMap.ProgrammingBootDaqRelay == "305", "BOOT0 relay must use DAQ 305.");
            Require(Bcm250StationMap.PlacementDetectionDaqRelays == "318,319", "Placement relays must use DAQ 318/319.");
            Require(Math.Abs(Bcm250StationMap.StringShuntOhms - 0.0203111) < 1e-9,
                "String shunt value changed.");

            IReadOnlyList<Bcm250ActionDefinition> catalogue = Bcm250ActionCatalog.Create();
            int[] measuredShuntChannels = catalogue
                .Where(x => x.Kind == Bcm250ActionKind.DaqShuntResistance)
                .Select(x => x.Channel)
                .OrderBy(x => x)
                .ToArray();
            Require(measuredShuntChannels.SequenceEqual(new[] { 113, 117, 119 }),
                "BCM250 must measure and store all three DAQ shunt resistances while de-energized.");
            Require(catalogue
                    .Where(x => x.Kind == Bcm250ActionKind.DaqShuntCurrent)
                    .All(x => measuredShuntChannels.Contains(x.Channel)),
                "A BCM250 shunt-current action has no preceding measured-resistance channel.");
        }

        private static void ValidateLimit(
            ResolvedTestPlan plan,
            string resultId,
            double lower,
            double upper,
            string unit)
        {
            TestPlanStepDefinition item = plan.Definition.Steps.Single(
                x => x.StepId.EndsWith("." + resultId, StringComparison.OrdinalIgnoreCase));
            Require(item.LowerLimit == lower && item.UpperLimit == upper && item.Unit == unit,
                "Errata/limit mismatch for " + resultId + ".");
        }

        private static void ValidateVariableDependencies()
        {
            IReadOnlyList<Bcm250ActionDefinition> catalog = Bcm250ActionCatalog.Create();
            Require(catalog.Any(x =>
                    x.Chapter == 8 &&
                    x.Kind == Bcm250ActionKind.SetMpsPower),
                "Chapter 8 must use ANS source C for the MPS board supply.");
            Require(catalog.Any(x => x.Kind == Bcm250ActionKind.PowerRelays),
                "BCM250 power paths must use the 16-channel power relay bank.");
            Require(catalog
                    .Where(x => x.Kind == Bcm250ActionKind.FixtureRelays)
                    .All(x => string.Equals(
                        x.Argument,
                        Bcm250StationMap.ProgrammingFixtureRelays,
                        StringComparison.Ordinal)),
                "Only TTL programming routes may use the fixture relay bank.");
            Require(catalog
                    .Where(x => x.Kind == Bcm250ActionKind.MbdCommand)
                    .All(x => (x.Argument ?? string.Empty).IndexOf(
                        ";",
                        StringComparison.Ordinal) < 0),
                "Each MBD command must be a separate visible step.");

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Bcm250ActionDefinition action in catalog)
            {
                foreach (string dependency in action.RequiresResultIds)
                    Require(seen.Contains(dependency),
                        action.ResultId + " depends on missing/later result " + dependency + ".");
                if (action.IsResult)
                    seen.Add(action.ResultId);
            }
        }

        private static void RunSimulation(TestSequenceContext context, IList steps)
        {
            foreach (object step in steps)
            {
                object result = InvokeAction(step);
                bool passed = Read<bool>(result.GetType(), result, "IsPass");
                string stepId = Read<string>(step.GetType(), step, "StepId");
                Require(passed, "Simulation failed at " + stepId + ".");
                string storeKey = Read<string>(result.GetType(), result, "StoreKey");
                if (!string.IsNullOrWhiteSpace(storeKey))
                {
                    double? numeric = Read<double?>(result.GetType(), result, "NumericValue");
                    string value = Read<string>(result.GetType(), result, "Value");
                    string unit = Read<string>(result.GetType(), result, "Unit");
                    context.StoreValue(storeKey, numeric ?? 0.0, value, unit, passed);
                }
            }
        }

        private static void RunFaultInjection(IList steps, string resultId)
        {
            object target = steps.Cast<object>().Single(
                x => Read<string>(x.GetType(), x, "StepId")
                    .EndsWith("." + resultId, StringComparison.OrdinalIgnoreCase));
            string stepId = Read<string>(target.GetType(), target, "StepId");
            Environment.SetEnvironmentVariable("TESTPLATFORM_BCM250_FAULT_STEP", stepId);
            try
            {
                object result = InvokeAction(target);
                Require(!Read<bool>(result.GetType(), result, "IsPass"),
                    "Fault injection did not fail " + stepId + ".");
            }
            finally
            {
                Environment.SetEnvironmentVariable("TESTPLATFORM_BCM250_FAULT_STEP", null);
            }
        }

        private static void RunEndToEndSimulation(string xmlPath)
        {
            string oldMode = Environment.GetEnvironmentVariable("TESTPLATFORM_SK_MODE");
            string oldFault = Environment.GetEnvironmentVariable("TESTPLATFORM_BCM250_FAULT_STEP");
            Environment.SetEnvironmentVariable("TESTPLATFORM_SK_MODE", "Simulation");
            try
            {
                var passGrid = new RecordingGrid();
                var passContext = new TestSequenceContext
                {
                    SN = "BCM250-E2E-PASS",
                    TestPlan = TestPlanService.Resolve(xmlPath, TestProfileIds.Normal),
                    StopOnFail = true
                };
                bool pass = new SKBCM250Sequence(passGrid)
                    .RunAsync(passContext)
                    .GetAwaiter()
                    .GetResult();
                Require(pass, "End-to-end simulation did not pass.");
                Require(passGrid.CleanupCompleted == 6,
                    "Passing run did not execute all cleanup steps.");

                ResolvedTestPlan faultPlan = TestPlanService.Resolve(xmlPath, TestProfileIds.Normal);
                string faultStep = faultPlan.Definition.Steps.Single(
                    x => x.StepId.EndsWith(".Q16", StringComparison.OrdinalIgnoreCase)).StepId;
                Environment.SetEnvironmentVariable("TESTPLATFORM_BCM250_FAULT_STEP", faultStep);
                var faultGrid = new RecordingGrid();
                var faultContext = new TestSequenceContext
                {
                    SN = "BCM250-E2E-FAULT",
                    TestPlan = faultPlan,
                    StopOnFail = true
                };
                bool faultPass = new SKBCM250Sequence(faultGrid)
                    .RunAsync(faultContext)
                    .GetAwaiter()
                    .GetResult();
                Require(!faultPass, "End-to-end injected-fault run unexpectedly passed.");
                Require(faultGrid.FailedStepIds.Contains(faultStep),
                    "Injected Q16 failure was not reported.");
                Require(faultGrid.CleanupCompleted == 6,
                    "Injected-fault run did not execute all cleanup steps.");

                int continuePrompts = 0;
                var continueGrid = new RecordingGrid();
                var continueContext = new TestSequenceContext
                {
                    SN = "BCM250-E2E-CONTINUE",
                    TestPlan = TestPlanService.Resolve(xmlPath, TestProfileIds.Normal),
                    StopOnFail = false,
                    PromptOnFail = true
                };
                bool continuePass = new SKBCM250Sequence(
                    continueGrid,
                    message =>
                    {
                        if (message != null &&
                            message.IndexOf("测试项失败", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            continuePrompts++;
                        }
                        return Task.FromResult(true);
                    })
                    .RunAsync(continueContext)
                    .GetAwaiter()
                    .GetResult();
                Require(!continuePass,
                    "Continue-after-failure run lost the recorded FAIL result.");
                Require(continuePrompts == 1,
                    "Continue-after-failure prompt was not shown exactly once.");
                Require(continueGrid.FailedStepIds.Contains(faultStep),
                    "Continue-after-failure run did not report injected Q16 failure.");
                Require(continueGrid.PassedStepIds.Any(
                        x => x.StartsWith(Bcm250GroupIds.Chapter(24) + ".", StringComparison.OrdinalIgnoreCase)),
                    "Continue-after-failure run did not reach chapter 24 after Q16 failed.");
                Require(continueGrid.CleanupCompleted == 6,
                    "Continue-after-failure run did not execute all cleanup steps.");
            }
            finally
            {
                Environment.SetEnvironmentVariable("TESTPLATFORM_SK_MODE", oldMode);
                Environment.SetEnvironmentVariable("TESTPLATFORM_BCM250_FAULT_STEP", oldFault);
            }
        }

        private static object InvokeAction(object step)
        {
            Delegate action = Read<Delegate>(step.GetType(), step, "Action");
            var task = (Task)action.DynamicInvoke(CancellationToken.None);
            task.GetAwaiter().GetResult();
            return task.GetType().GetProperty("Result").GetValue(task);
        }

        private static MethodInfo FindMethod(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                MethodInfo method = current.GetMethod(
                    name,
                    BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (method != null)
                    return method;
            }
            throw new MissingMethodException(type.FullName, name);
        }

        private static T Read<T>(Type type, object instance, string property)
        {
            return (T)type.GetProperty(property).GetValue(instance);
        }

        private static string FindRepositoryRoot(string start)
        {
            var directory = new DirectoryInfo(start);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "TestPlatform.csproj")))
                    return directory.FullName;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("Repository root not found.");
        }

        private static Exception Unwrap(Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null)
                ex = ex.InnerException;
            return ex;
        }

        private static void ValidatePowerSafety()
        {
            var device = new SK441Device { SkipComInit = true, SimulationBoardType = "BCM-250" };
            Require(!device.SetAnsVoltageCurrentOutputAsync(275, 1, true).GetAwaiter().GetResult(), "ANS energized before RL4 closed.");
            Require(device.TurnOffAllInstrumentsAsync().GetAwaiter().GetResult(), "Cannot reset outputs.");
            Require(device.ConfigureAnsOutputOffAsync(275, 1).GetAwaiter().GetResult(), "Cannot preconfigure ANS output off.");
            Require(device.IsAnsSetpointConfigured(275, 1), "ANS configured setpoint was not retained.");
            Require(device.CloseRl4ThenEnableAnsAsync(7, 275, 1).GetAwaiter().GetResult(), "Safe RL4/ANS power-on failed.");
            Require(!device.StopFixturePressDownAsync("BCM-250").GetAwaiter().GetResult(), "Fixture released while ANS was energized.");
            Require(device.ControlRelayAsync(7, false).GetAwaiter().GetResult(), "Safe RL4 opening was blocked while ANS was energized.");
            Require(device.DisableAnsThenOpenRl4Async(7).GetAwaiter().GetResult(), "Safe ANS/RL4 power-off failed.");
            Require(device.StopFixturePressDownAsync("BCM-250").GetAwaiter().GetResult(), "Fixture remained locked after safe power-off.");
            Require(device.ControlRelayAsync(8, true).GetAwaiter().GetResult(), "Cannot close BCM generator path before String power-on.");
            Require(!device.SetStringGeneratorAsync(12, 1, 16, true).GetAwaiter().GetResult(),
                "String output was allowed without Y1/Y3/Y6.");
            Require(device.ControlRelayAsync(1, true).GetAwaiter().GetResult(), "Cannot close String relay Y1.");
            Require(device.ControlRelayAsync(3, true).GetAwaiter().GetResult(), "Cannot close String relay Y3.");
            Require(device.ControlRelayAsync(6, true).GetAwaiter().GetResult(), "Cannot close String relay Y6.");
            Require(device.SetStringGeneratorAsync(12, 1, 16, true).GetAwaiter().GetResult(), "Cannot simulate String ON with Y1/Y3/Y6 closed.");
            Require(!device.ControlRelayAsync(9, true).GetAwaiter().GetResult(), "Power path closed while String was energized.");
            Require(device.ControlRelayAsync(8, false).GetAwaiter().GetResult(), "Safe relay opening was blocked while String was energized.");
            Require(device.SetStringGeneratorAsync(0, 0, 16, false).GetAwaiter().GetResult(), "Cannot turn String generator off after safe relay opening.");
            Require(device.GetPowerSafetyTrace().Any(x => x.StartsWith("BLOCK", StringComparison.Ordinal)), "No unsafe power action was recorded.");

            var emergency = new SK441Device { SkipComInit = true, SimulationBoardType = "BCM-250" };
            Require(emergency.EnableFixturePressDownAsync("BCM-250").GetAwaiter().GetResult(), "BCM-250 fixture Y1 did not close.");
            Require(emergency.EmergencyStopOutputsAsync("BCM-250", "test emergency").GetAwaiter().GetResult(), "BCM-250 emergency output shutdown failed.");
            string[] fixtureTrace = emergency.GetFixtureActuationTrace();
            Require(fixtureTrace.Contains("FIXTURE STATION 1 RELAY 1 CLOSED") && fixtureTrace.Contains("FIXTURE STATION 1 RELAY 1 OPEN"), "BCM-250 fixture Y1 emergency mapping failed.");
            Require(!fixtureTrace.Any(x => x.Contains("RELAY 2")), "BCM-250 incorrectly used fixture Y2 for press-down.");
            Require(!emergency.SetVbusGeneratorAsync(275, 1, 300, true).GetAwaiter().GetResult(), "Emergency latch allowed VBUS output ON.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private sealed class RecordingGrid : ITestGridService
        {
            public int RowCount => 1000;
            public int CleanupCompleted { get; private set; }
            public HashSet<string> FailedStepIds { get; } =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> PassedStepIds { get; } =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

            public Task SetValueAndStatusByStepIdAsync(
                int channelIndex,
                string stepId,
                string value,
                StepExecutionStatus status)
            {
                if (status == StepExecutionStatus.Failed)
                    FailedStepIds.Add(stepId);
                if (status == StepExecutionStatus.Passed ||
                    status == StepExecutionStatus.PassedAfterRetry)
                {
                    PassedStepIds.Add(stepId);
                }
                if (stepId.StartsWith(Bcm250GroupIds.Cleanup + ".", StringComparison.OrdinalIgnoreCase) &&
                    (status == StepExecutionStatus.Passed ||
                     status == StepExecutionStatus.PassedAfterRetry))
                {
                    CleanupCompleted++;
                }
                return Task.CompletedTask;
            }

            public Task<(bool IsValid, double Lower, double Upper, string ErrorMessage)> GetLimitsAsync(int rowIndex)
            {
                return Task.FromResult((true, 0.0, 0.0, string.Empty));
            }
        }
    }
}
