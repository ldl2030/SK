using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TestPlatform;
using TestPlatform.TestSequences;

namespace Bcm125StructureTests
{
    internal static class Program
    {
        private static SK441Device _simulationDevice;

        private static int Main(string[] args)
        {
            try
            {
                string repositoryRoot = args.Length > 0
                    ? Path.GetFullPath(args[0])
                    : FindRepositoryRoot(AppDomain.CurrentDomain.BaseDirectory);
                string xmlPath = Path.Combine(
                    repositoryRoot,
                    "ProjectConfig",
                    "SK",
                    "BCM-125",
                    "BCM-125_autoTest.xml");

                ResolvedTestPlan plan = TestPlanService.Resolve(
                    xmlPath,
                    TestProfileIds.Normal);
                IList steps = BuildSteps(plan);
                List<TestPlanStepDefinition> executableDefinitions = plan.Definition.Steps
                    .Where(x => !string.Equals(
                        x.RunCondition,
                        "OnChapterFailure",
                        StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.SequenceOrder)
                    .ToList();

                Require(steps.Count == executableDefinitions.Count,
                    $"Step count mismatch: code={steps.Count}, XML={executableDefinitions.Count}.");
                Require(steps.Count == 956, $"Expected 956 executable steps, got {steps.Count}.");

                var codeStepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int cleanupCount = 0;
                foreach (object step in steps)
                {
                    Type type = step.GetType();
                    string stepId = Read<string>(type, step, "StepId");
                    string expectedGroup = Read<string>(type, step, "ExpectedGroupId");
                    TestPlanStepDefinition definition =
                        Read<TestPlanStepDefinition>(type, step, "PlanDefinition");
                    bool alwaysRun = Read<bool>(type, step, "AlwaysRun");

                    Require(!string.IsNullOrWhiteSpace(stepId), "Code step has no StepId.");
                    Require(codeStepIds.Add(stepId), $"Duplicate code StepId: {stepId}.");
                    Require(definition != null, $"Step {stepId} has no XML definition.");
                    Require(string.Equals(
                            expectedGroup,
                            definition.GroupId,
                            StringComparison.OrdinalIgnoreCase),
                        $"Step {stepId} group mismatch.");

                    if (alwaysRun)
                    {
                        cleanupCount++;
                        Require(string.Equals(
                                definition.GroupId,
                                Bcm125GroupIds.Cleanup,
                                StringComparison.OrdinalIgnoreCase),
                            $"AlwaysRun step {stepId} is outside cleanup.");
                    }
                }

                Require(cleanupCount == 5, $"Expected 5 cleanup steps, got {cleanupCount}.");
                Require(codeStepIds.SetEquals(executableDefinitions.Select(x => x.StepId)),
                    "Code/XML StepId sets differ.");

                ValidateXmlParameters(executableDefinitions);
                ValidateDependencies(plan.Definition);
                ValidateInstrumentMapping();
                ValidateXmlParameterAuthority(steps);
                ValidatePowerSafety();
                ValidateAllActionSafety(steps, executableDefinitions);

                Console.WriteLine(
                    "PASS BCM125 structure: 956 steps, unique StepIds, chapter binding, " +
                    "XML limits/units/delays, dependencies, mapping, and 5 cleanup steps.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL BCM125 structure: " + Unwrap(ex).Message);
                return 1;
            }
        }

        private static IList BuildSteps(ResolvedTestPlan plan)
        {
            var sequence = new SKBCM125Sequence(null, null);
            var context = new TestSequenceContext { TestPlan = plan };
            _simulationDevice = new SK441Device { SkipComInit = true };
            Type sequenceType = sequence.GetType();
            object steps = FindMethod(sequenceType, "BuildCommonSteps")
                .Invoke(sequence, new object[] { context, _simulationDevice });
            FindMethod(sequenceType, "AddElectricalTestSteps")
                .Invoke(sequence, new object[] { steps, context, _simulationDevice });
            FindMethod(sequenceType, "BindStableStepIds")
                .Invoke(null, new[] { context, steps });
            return (IList)steps;
        }

        private static MethodInfo FindMethod(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                MethodInfo method = current.GetMethod(
                    name,
                    BindingFlags.Instance |
                    BindingFlags.Static |
                    BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
                if (method != null)
                    return method;
            }

            throw new MissingMethodException(type.FullName, name);
        }

        private static void ValidateXmlParameters(
            IReadOnlyCollection<TestPlanStepDefinition> definitions)
        {
            int delayCount = 0;
            int numericCount = 0;
            var waitPattern = new Regex(
                @"(?:等待|wait)\s*\d+\s*ms",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            foreach (TestPlanStepDefinition definition in definitions)
            {
                bool hasLower = definition.LowerLimit.HasValue;
                bool hasUpper = definition.UpperLimit.HasValue;
                Require(hasLower == hasUpper,
                    $"Step {definition.StepId} defines only one numeric limit.");

                if (hasLower)
                {
                    numericCount++;
                    Require(definition.LowerLimit.Value <= definition.UpperLimit.Value,
                        $"Step {definition.StepId} has reversed limits.");
                    Require(definition.Unit != null,
                        $"Step {definition.StepId} has no XML unit value.");
                }

                if (waitPattern.IsMatch(definition.Name ?? string.Empty))
                {
                    delayCount++;
                    Require(definition.DelayMs.HasValue && definition.DelayMs.Value >= 0,
                        $"Wait step {definition.StepId} has no DelayMs in XML.");
                }
            }

            Require(delayCount == 139, $"Expected 139 XML delay rows, got {delayCount}.");
            Require(numericCount == 182, $"Expected 182 numeric XML rows, got {numericCount}.");
        }

        private static void ValidateDependencies(TestPlanDefinition definition)
        {
            var steps = definition.Steps.ToDictionary(
                x => x.StepId,
                StringComparer.OrdinalIgnoreCase);
            var groups = new HashSet<string>(
                definition.Groups.Select(x => x.GroupId),
                StringComparer.OrdinalIgnoreCase);

            foreach (TestPlanStepDefinition step in definition.Steps)
            {
                foreach (string dependency in step.DependsOn)
                {
                    Require(steps.ContainsKey(dependency) || groups.Contains(dependency),
                        $"Step {step.StepId} has missing dependency {dependency}.");
                    if (steps.TryGetValue(dependency, out TestPlanStepDefinition required))
                        Require(required.SequenceOrder < step.SequenceOrder,
                            $"Step {step.StepId} depends on non-prior step {dependency}.");
                }
            }
        }

        private static void ValidateInstrumentMapping()
        {
            Require(Bcm125StationMap.VmidReferenceDaqChannel == 106,
                "VMID reference must use DAQ 106.");
            Require(Bcm125StationMap.String1ShuntDaqChannel == 113,
                "ST1 shunt must use DAQ 113.");
            Require(Bcm125StationMap.String2ShuntDaqChannel == 115,
                "ST2 shunt must use DAQ 115.");
            Require(Math.Abs(Bcm125StationMap.String1ShuntOhms - 0.0215909) < 1e-10,
                "ST1 shunt resistance mismatch.");
            Require(Math.Abs(Bcm125StationMap.String2ShuntOhms - 0.0215914) < 1e-10,
                "ST2 shunt resistance mismatch.");
            Require(Bcm125StationMap.ProgrammingBcm1DaqRelay == "@305",
                "RLP4 must use DAQ relay 305.");
            Require(Bcm125StationMap.ProgrammingBcm2DaqRelay == "@306",
                "RLP8 must use DAQ relay 306.");
            Require(Bcm125StationMap.ShortProtectionSelectorDaqRelay == "@304",
                "Short-protection selector must use DAQ relay 304.");
            Require(Bcm125StationMap.StringSimulatorDaqRelays == "@302,@303",
                "String simulator relays must use DAQ relays 302/303.");
            Require(Bcm125StationMap.PlacementRelayA == "@318" &&
                    Bcm125StationMap.PlacementRelayB == "@319",
                "Placement detection must use DAQ relays 318/319.");

            string[] resultIds = Bcm125ChannelProfiles.ChargingString1.ResultIds
                .Concat(Bcm125ChannelProfiles.ChargingString2.ResultIds)
                .ToArray();
            Require(resultIds.Length == 32 &&
                    resultIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 32,
                "Strongly typed charging result identifiers are incomplete or duplicated.");
        }

        private static void ValidateXmlParameterAuthority(IList steps)
        {
            object numericStep = FindStep(steps, "BCM125.CH08.S003.B01");
            Type numericType = numericStep.GetType();
            TestPlanStepDefinition numericDefinition =
                Read<TestPlanStepDefinition>(numericType, numericStep, "PlanDefinition");
            double? originalLower = numericDefinition.LowerLimit;
            double? originalUpper = numericDefinition.UpperLimit;
            numericDefinition.LowerLimit = 100;
            numericDefinition.UpperLimit = 101;
            object numericResult = InvokeStep(numericType, numericStep);
            Require(!Read<bool>(numericResult.GetType(), numericResult, "IsPass"),
                "Changing XML limits did not change the simulated B01 verdict.");
            numericDefinition.LowerLimit = originalLower;
            numericDefinition.UpperLimit = originalUpper;

            object delayStep = steps.Cast<object>()
                .First(step =>
                {
                    Type type = step.GetType();
                    TestPlanStepDefinition definition =
                        Read<TestPlanStepDefinition>(type, step, "PlanDefinition");
                    return definition != null && definition.DelayMs.HasValue;
                });
            Type delayType = delayStep.GetType();
            TestPlanStepDefinition delayDefinition =
                Read<TestPlanStepDefinition>(delayType, delayStep, "PlanDefinition");
            int? originalDelay = delayDefinition.DelayMs;
            delayDefinition.DelayMs = 0;
            object delayResult = InvokeStep(delayType, delayStep);
            Require(
                string.Equals(
                    Read<string>(delayResult.GetType(), delayResult, "Value"),
                    "0 ms",
                    StringComparison.Ordinal),
                "Changing XML DelayMs did not change the delay action result.");
            delayDefinition.DelayMs = originalDelay;
        }

        private static object FindStep(IList steps, string stepId)
        {
            foreach (object step in steps)
            {
                Type type = step.GetType();
                if (string.Equals(
                    Read<string>(type, step, "StepId"),
                    stepId,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return step;
                }
            }
            throw new InvalidOperationException("Step not found: " + stepId);
        }

        private static object InvokeStep(Type type, object step)
        {
            Delegate action = (Delegate)type.GetProperty("Action").GetValue(step, null);
            var task = (Task)action.DynamicInvoke(CancellationToken.None);
            task.GetAwaiter().GetResult();
            return task.GetType().GetProperty("Result").GetValue(task, null);
        }

        private static T Read<T>(Type type, object instance, string propertyName)
        {
            return (T)type.GetProperty(propertyName).GetValue(instance, null);
        }

        private static Exception Unwrap(Exception exception)
        {
            while (exception is TargetInvocationException &&
                   exception.InnerException != null)
            {
                exception = exception.InnerException;
            }
            return exception;
        }

        private static void ValidatePowerSafety()
        {
            var device = new SK441Device { SkipComInit = true, SimulationBoardType = "BCM-125" };
            Require(!device.SetAnsVoltageCurrentOutputAsync(140, 1, true).GetAwaiter().GetResult(), "ANS energized before RL4 closed.");
            Require(device.TurnOffAllInstrumentsAsync().GetAwaiter().GetResult(), "Cannot reset outputs.");
            Require(device.CloseRl4ThenEnableAnsAsync(7, 140, 1).GetAwaiter().GetResult(), "Safe RL4/ANS power-on failed.");
            Require(!device.StopFixturePressDownAsync("BCM-125").GetAwaiter().GetResult(), "Fixture released while ANS was energized.");
            Require(device.ControlRelayAsync(7, false).GetAwaiter().GetResult(), "Safe RL4 opening was blocked while ANS was energized.");
            Require(device.DisableAnsThenOpenRl4Async(7).GetAwaiter().GetResult(), "Safe ANS/RL4 power-off failed.");
            Require(device.StopFixturePressDownAsync("BCM-125").GetAwaiter().GetResult(), "Fixture remained locked after safe power-off.");
            Require(device.SetBcm125PowerPathRelaysAsync(new[] { 8 }, true, "safety path").GetAwaiter().GetResult(), "Cannot close BCM generator path before VBUS power-on.");
            Require(device.SetVbusGeneratorAsync(50, 1, 150, true).GetAwaiter().GetResult(), "Cannot simulate VBUS ON.");
            Require(!device.SetBcm125PowerPathRelaysAsync(new[] { 9 }, true, "safety test").GetAwaiter().GetResult(), "Power path closed while VBUS was energized.");
            Require(device.ControlRelayAsync(8, false).GetAwaiter().GetResult(), "Safe relay opening was blocked while VBUS was energized.");
            Require(device.SetVbusGeneratorAsync(0, 0, 150, false).GetAwaiter().GetResult(), "Cannot turn VBUS off after safe relay opening.");
            Require(device.GetPowerSafetyTrace().Any(x => x.StartsWith("BLOCK", StringComparison.Ordinal)), "No unsafe power action was recorded.");

            var emergency = new SK441Device { SkipComInit = true, SimulationBoardType = "BCM-125" };
            Require(emergency.EnableFixturePressDownAsync("BCM-125").GetAwaiter().GetResult(), "BCM-125 fixture Y1/Y2 did not close.");
            Require(emergency.EmergencyStopOutputsAsync("BCM-125", "test emergency").GetAwaiter().GetResult(), "BCM-125 emergency output shutdown failed.");
            string[] fixtureTrace = emergency.GetFixtureActuationTrace();
            Require(fixtureTrace.Contains("FIXTURE STATION 1 RELAY 1 CLOSED"), "BCM-125 fixture Y1 was not closed.");
            Require(fixtureTrace.Contains("FIXTURE STATION 1 RELAY 2 CLOSED"), "BCM-125 fixture Y2 was not closed.");
            Require(fixtureTrace.Contains("FIXTURE STATION 1 RELAY 1 OPEN") && fixtureTrace.Contains("FIXTURE STATION 1 RELAY 2 OPEN"), "BCM-125 emergency did not clear Y1/Y2 commands.");
            Require(!emergency.SetAnsVoltageCurrentOutputAsync(140, 1, true).GetAwaiter().GetResult(), "Emergency latch allowed ANS output ON.");
            Require(!emergency.EnableFixturePressDownAsync("BCM-125").GetAwaiter().GetResult(), "Emergency latch allowed BCM-125 fixture press-down.");
            var inputFailure = new SK441Device { SkipComInit = true, SimulateDigitalInputCommunicationFailure = true };
            DigitalInputReadResult failedInput = inputFailure.ReadDigitalInputsSafeAsync().GetAwaiter().GetResult();
            Require(!failedInput.CommunicationOk, "Digital-input communication failure was reported healthy.");
            bool failSafeRawState = inputFailure.ReadDigitalInputsAsync().GetAwaiter().GetResult()[7];
            Require(
                inputFailure.IsEmergencyStopTriggered(failSafeRawState),
                "Legacy digital-input API did not fail safe as emergency pressed.");
        }

        private static void ValidateAllActionSafety(IList steps, IEnumerable<TestPlanStepDefinition> definitions)
        {
            foreach (TestPlanStepDefinition definition in definitions)
                if (definition.DelayMs.HasValue) definition.DelayMs = 0;

            foreach (object step in steps)
            {
                Type type = step.GetType();
                string stepId = Read<string>(type, step, "StepId");
                string name = Read<string>(type, step, "Name");
                if (stepId.StartsWith("BCM125.PRECHECK", StringComparison.OrdinalIgnoreCase) ||
                    !stepId.EndsWith(".ACTION", StringComparison.OrdinalIgnoreCase) ||
                    !Regex.IsMatch(name, "RL4|relay|relays|继电器|generator|电源|电子负载|outputs|输出|Reset", RegexOptions.IgnoreCase)) continue;
                object result = InvokeStep(type, step);
                Require(Read<bool>(result.GetType(), result, "IsPass"), "Unsafe/failed BCM125 action trace at " + stepId + ".");
            }
            Require(!_simulationDevice.GetPowerSafetyTrace().Any(x => x.StartsWith("BLOCK RELAY", StringComparison.Ordinal)), "BCM125 full action trace attempted energized relay switching.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
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
            throw new DirectoryNotFoundException("Cannot locate TestPlatform repository root.");
        }
    }
}
