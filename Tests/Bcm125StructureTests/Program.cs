using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Globalization;
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
        private static SKBCM125Sequence _simulationSequence;
        private static TestSequenceContext _simulationContext;

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
                SkCommonBehaviorTests.Validate("BCM-125", steps);
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
                ValidateDualFirmwareProgramming(executableDefinitions, repositoryRoot);
                ValidateCustomerReport(repositoryRoot);
                ValidateDependencies(plan.Definition);
                ValidateInstrumentMapping();
                ValidateMbdResponseCleaning();
                ValidateMbdRootRecoveryGuards();
                ValidateConditionalFirmwareProgramming(plan);
                ValidateElectronicLoadShutdown();
                ValidateBcm125TerminalStateMachine();
                ValidateAllBcm125MockQueries();
                ValidateExplicitRemoteTransitions(steps);
                ValidateXmlParameterAuthority(steps);
                ValidateMeasuredReadinessBindings(steps);
                ValidateAdaptiveWaitBindings(steps);
                ValidateAuxiliaryWarnings(steps);
                ValidateCalibrationDateReadback(steps);
                ValidateMbdAverageCadence(steps);
                ValidateChargingMeasurementBatches(plan);
                ValidateResetFloatingPointAndSt2Wait(steps);
                ValidateMidpointToDischargePower(steps);
                ValidatePowerResultFailureDoesNotBypassPolicy(steps);
                ValidateSignedShuntPolarity(steps);
                ValidateStringTestTimedSampling(steps, repositoryRoot);
                ValidatePowerSafety();
                ValidateStringGeneratorPowerPathSteps(steps);
                ValidateAllActionSafety(steps, executableDefinitions);
                ValidateCalibrationWriteGuards(steps);
                ValidateFailedNumericCalibrationWrites(steps);
                ValidateChapter23MbdCommands(steps);
                ValidateEndToEndSimulation(xmlPath);

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
            var sequence = new SKBCM125Sequence(null, null, forceFirmwareProgramming: true);
            _simulationSequence = sequence;
            var context = new TestSequenceContext { TestPlan = plan };
            _simulationContext = context;
            _simulationDevice = new SK441Device { SkipComInit = true };
            Type sequenceType = sequence.GetType();
            object steps = FindMethod(sequenceType, "BuildCommonSteps")
                .Invoke(sequence, new object[] { context, _simulationDevice });
            FindMethod(sequenceType, "AddElectricalTestSteps")
                .Invoke(sequence, new object[] { steps, context, _simulationDevice });
            FindMethod(sequenceType, "BindStableStepIds")
                .Invoke(null, new[] { context, steps });
            ((Task<bool>)FindMethod(sequenceType, "EvaluateBcm125FirmwareAsync")
                .Invoke(sequence, new object[] { context, _simulationDevice, CancellationToken.None })).GetAwaiter().GetResult();
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

            Require(delayCount == 140, $"Expected 140 XML delay rows, got {delayCount}.");
            Require(numericCount == 182, $"Expected 182 numeric XML rows, got {numericCount}.");
        }

        private static void ValidateDualFirmwareProgramming(
            IReadOnlyCollection<TestPlanStepDefinition> definitions,
            string repositoryRoot)
        {
            TestPlanStepDefinition bcm1Flash = definitions.SingleOrDefault(x => string.Equals(
                x.StepId,
                "BCM125.CH09.S013.C01",
                StringComparison.OrdinalIgnoreCase));
            TestPlanStepDefinition bcm2Flash = definitions.SingleOrDefault(x => string.Equals(
                x.StepId,
                "BCM125.CH09.S024.C02",
                StringComparison.OrdinalIgnoreCase));

            Require(bcm1Flash != null && bcm2Flash != null,
                "BCM125 programming must contain independent BCM1 C01 and BCM2 C02 flash rows.");
            Require(bcm1Flash.SequenceOrder < bcm2Flash.SequenceOrder,
                "BCM1 firmware must be programmed before BCM2 firmware.");
            Require((bcm1Flash.Name ?? string.Empty).IndexOf(Bcm125Commands.FirmwareFile,
                        StringComparison.OrdinalIgnoreCase) >= 0 &&
                    (bcm2Flash.Name ?? string.Empty).IndexOf(Bcm125Commands.FirmwareFile,
                        StringComparison.OrdinalIgnoreCase) >= 0,
                "Both BCM125 programming rows must identify the configured V8 HEX file.");

            string firmwarePath = Path.Combine(
                repositoryRoot,
                "ProjectConfig",
                "SK",
                "BCM-125",
                "Firmware",
                Bcm125Commands.FirmwareFile);
            Require(File.Exists(firmwarePath),
                "BCM125 V8 HEX firmware is missing from the source project configuration.");

            string expectedVersion = "stcu_UP125_V8";
            Require(string.Equals(
                    _simulationDevice.GetBcm125ExpectedFirmwareVersion(),
                    expectedVersion,
                    StringComparison.Ordinal),
                "BCM125 configured R0 firmware version differs from the programmed V8 image.");
            foreach (string stepId in new[] { "BCM125.CH11.S004.E01", "BCM125.CH11.S007.E02" })
            {
                TestPlanStepDefinition versionRow = definitions.Single(x =>
                    string.Equals(x.StepId, stepId, StringComparison.OrdinalIgnoreCase));
                Require(versionRow.ExpectedValue == expectedVersion,
                    "BCM125 XML firmware version limit differs from R0 for " + stepId + ".");
            }
        }

        private static void ValidateCustomerReport(string repositoryRoot)
        {
            string sourceDirectory = Path.Combine(
                repositoryRoot,
                "ProjectConfig",
                "SK",
                "BCM-125");
            string requestedQaRoot = Environment.GetEnvironmentVariable(
                "TESTPLATFORM_BCM125_REPORT_QA_ROOT");
            bool keepQaReport = !string.IsNullOrWhiteSpace(requestedQaRoot);
            string temporaryRoot = keepQaReport
                ? Path.GetFullPath(requestedQaRoot)
                : Path.Combine(
                    Path.GetTempPath(),
                    "TestPlatform_BCM125_Report_" + Guid.NewGuid().ToString("N"));
            string targetDirectory = Path.Combine(
                temporaryRoot,
                "ProjectConfig",
                "SK",
                "BCM-125");
            Directory.CreateDirectory(targetDirectory);
            File.Copy(
                Path.Combine(sourceDirectory, "BCM-125_report.psv"),
                Path.Combine(targetDirectory, "BCM-125_report.psv"),
                true);
            File.Copy(
                Path.Combine(sourceDirectory, "BCM-125_autoTest.xml"),
                Path.Combine(targetDirectory, "BCM-125_autoTest.xml"),
                true);

            try
            {
                var table = new DataTable();
                table.Columns.Add("StepId", typeof(string));
                table.Columns.Add("Channel1Value", typeof(string));
                table.Columns.Add("Channel1Result", typeof(string));
                foreach (string line in File.ReadAllLines(
                             Path.Combine(sourceDirectory, "BCM-125_report.psv"))
                         .Where(value => !string.IsNullOrWhiteSpace(value) && !value.StartsWith("#")))
                {
                    string[] fields = line.Split('|');
                    string value = fields[0].StartsWith("E", StringComparison.OrdinalIgnoreCase)
                        ? "stcu_UP125_V8"
                        : "1";
                    table.Rows.Add(fields[1], value, "PASS");
                }

                Bcm125LegacyReportExporter.ExportResult result =
                    Bcm125LegacyReportExporter.SaveAsync(
                            table,
                            0,
                            "1105",
                            "B1114700117",
                            true,
                            "BCM-125",
                            new DateTime(2025, 5, 19, 17, 34, 0),
                            temporaryRoot)
                        .GetAwaiter()
                        .GetResult();

                Require(result.Success, "BCM125 TXT/PDF report generation failed: " + result.ErrorMessage);
                Require(result.DutPassed, "BCM125 all-pass report was not marked PASSED.");
                string reportText = File.ReadAllText(result.TextFilePath);
                Require(reportText.Contains("C01") && reportText.Contains("Micro Programming BCM 1") &&
                        reportText.Contains("E01") && reportText.Contains("stcu_UP125_V8"),
                    "BCM125 report omits programming or firmware rows.");
                Require(!reportText.Contains("BCM_125_V8.HEX"),
                    "BCM125 report must not render the HEX filename as a numeric limit.");
                string programmingLine = reportText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                    .First(line => line.Contains("Micro Programming BCM 1"));
                Require(!programmingLine.Contains("["),
                    "BCM125 programming rows must not render XML logic criteria as report limits.");
                Require(File.Exists(result.PdfFilePath) && new FileInfo(result.PdfFilePath).Length > 1000,
                    "BCM125 PDF report is unexpectedly empty.");
            }
            finally
            {
                if (!keepQaReport && Directory.Exists(temporaryRoot))
                    Directory.Delete(temporaryRoot, true);
            }
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
            Require(Math.Abs(Bcm125StationMap.String1ShuntOhms - ExpectedShuntOhms(
                "SKBCM125String1ShuntOhms", 0.0205)) < 1e-10,
                "ST1 shunt resistance mismatch.");
            Require(Math.Abs(Bcm125StationMap.String2ShuntOhms - ExpectedShuntOhms(
                "SKBCM125String2ShuntOhms", 0.0205)) < 1e-10,
                "ST2 shunt resistance mismatch.");
            Require(Bcm125StationMap.ProgrammingBcm1DaqRelay == "@305",
                "RLP4 must use DAQ relay 305.");
            Require(Bcm125StationMap.ProgrammingBcm2DaqRelay == "@306",
                "RLP8 must use DAQ relay 306.");
            Require(Bcm125StationMap.ShortProtectionSelectorDaqRelay == "@304",
                "Short-protection selector must use DAQ relay 304.");
            Require(Bcm125StationMap.MidpointDaqRelay == "@301",
                "RL6 midpoint path must use DAQ relay 301.");
            Require(Bcm125StationMap.StringSimulatorDaqRelays == "@302,@303",
                "String simulator relays must use DAQ relays 302/303.");
            Require(Bcm125StationMap.StringGeneratorPowerRelays == "1,4,6",
                "BCM125 String supply must use station-3 power relays Y1/Y4/Y6.");
            Require(Bcm125StationMap.ElectronicLoadPowerRelays == "2,5",
                "BCM125 electronic-load supply must use station-3 power relays Y2/Y5.");
            Require(Bcm125StationMap.DutPowerRelay == 7 &&
                    Bcm125StationMap.Rl2BPowerRelay == 8 &&
                    Bcm125StationMap.Rl1aBPowerRelay == 9 &&
                    Bcm125StationMap.Rl2APowerRelay == 10 &&
                    Bcm125StationMap.Rl1bAPowerRelay == 11 &&
                    Bcm125StationMap.Rl9APowerRelay == 12 &&
                    Bcm125StationMap.Rl1bBPowerRelay == 13 &&
                    Bcm125StationMap.Rl9BPowerRelay == 14 &&
                    Bcm125StationMap.Rl1aAPowerRelay == 15 &&
                    Bcm125StationMap.Rl1aCPowerRelay == 16,
                "BCM125 named high-power relay mapping changed.");
            Require(Bcm125StationMap.DischargeString1PowerRelays == "1,4,6,9,13,10",
                "BCM125 ST1 discharge path must use RL1a_B/RL1b_B/RL2A.");
            Require(Bcm125StationMap.DischargeString2PowerRelays == "1,4,6,9,14,10",
                "BCM125 ST2 discharge path must use RL1a_B/RL9_B/RL2A.");
            Require(Bcm125ChannelProfiles.DischargeString1.RelayDefaults ==
                    Bcm125StationMap.DischargeString1PowerRelays &&
                    Bcm125ChannelProfiles.DischargeString2.RelayDefaults ==
                    Bcm125StationMap.DischargeString2PowerRelays,
                "BCM125 discharge profiles are not bound to the station map.");
            Require(Bcm125StationMap.PlacementRelayA == "@318" &&
                    Bcm125StationMap.PlacementRelayB == "@319",
                "Placement detection must use DAQ relays 318/319.");

            BcmStringChannelConfig[] string1Profiles =
            {
                Bcm125ChannelProfiles.DischargeString1,
                Bcm125ChannelProfiles.ChargingString1,
                Bcm125ChannelProfiles.VerificationString1,
                Bcm125ChannelProfiles.CcbString1,
                Bcm125ChannelProfiles.StringTest1
            };
            BcmStringChannelConfig[] string2Profiles =
            {
                Bcm125ChannelProfiles.DischargeString2,
                Bcm125ChannelProfiles.ChargingString2,
                Bcm125ChannelProfiles.VerificationString2,
                Bcm125ChannelProfiles.CcbString2,
                Bcm125ChannelProfiles.StringTest2
            };
            Require(string1Profiles.All(profile =>
                    string.Equals(profile.Suffix, "ST1", StringComparison.Ordinal) &&
                    string.Equals(profile.RemoteCommand, "REMOTE1", StringComparison.Ordinal) &&
                    string.Equals(profile.MbdRelayCommandPrefix, "W94=", StringComparison.Ordinal)),
                "A BCM125 String-1 profile is not bound to REMOTE1/W94.");
            Require(string2Profiles.All(profile =>
                    string.Equals(profile.Suffix, "ST2", StringComparison.Ordinal) &&
                    string.Equals(profile.RemoteCommand, "REMOTE2", StringComparison.Ordinal) &&
                    string.Equals(profile.MbdRelayCommandPrefix, "W91=", StringComparison.Ordinal)),
                "A BCM125 String-2 profile is not bound to REMOTE2/W91.");

            string[] resultIds = Bcm125ChannelProfiles.ChargingString1.ResultIds
                .Concat(Bcm125ChannelProfiles.ChargingString2.ResultIds)
                .ToArray();
            Require(resultIds.Length == 32 &&
                    resultIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 32,
                "Strongly typed charging result identifiers are incomplete or duplicated.");
        }

        private static double ExpectedShuntOhms(string settingName, double legacyValue)
        {
            string configured = ConfigurationManager.AppSettings[settingName];
            return string.IsNullOrWhiteSpace(configured)
                ? legacyValue
                : double.Parse(configured, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static void ValidateMbdResponseCleaning()
        {
            string canResponse =
                "015 = 99,99,99,99,99,99,99,99,99,99,99,99,99,99,99,00 | Ready.\r\n" +
                "000054 chars answered.\r\nReady.\r\n";
            int[] canValues = MbdResponseParser.ParseIntegerVector("R15", canResponse, 16);
            Require(canValues.Length == 16 && canValues[15] == 0,
                "MBD CAN vector cleanup did not preserve the final 00 value.");

            string version = MbdResponseParser.ParsePayloadText(
                "R0",
                "000 = stcu_UP125_V8 | Ready.\r\n000020 chars answered.\r\nReady.\r\n");
            Require(string.Equals(version, "stcu_UP125_V8", StringComparison.Ordinal),
                "MBD text payload cleanup retained terminal metadata.");

            double runningTime = MbdResponseParser.ParseScalar(
                "R10",
                "010 = 0.953 | Ready.\r\nReady.\r\n");
            Require(Math.Abs(runningTime - 0.953) < 0.000001,
                "MBD scalar cleanup changed the floating-point running time.");

            string responseError;
            Require(
                !MbdResponseParser.IsSuccessfulCommandResponse(
                    "W76=1",
                    "READ ONLY ERROR: 076 = 0 | Ready.\r\n",
                    out responseError) &&
                string.Equals(responseError, "READ ONLY ERROR", StringComparison.Ordinal),
                "MBD READ ONLY ERROR was incorrectly accepted as a successful write.");

            Require(
                MbdResponseParser.IsSuccessfulCommandResponse(
                    "REMOTE1",
                    "REMOTE SLAVE 1.\r\nReady.\r\n",
                    out responseError),
                "REMOTE1 did not accept the confirmed SLAVE 1 terminal response.");
            Require(
                MbdResponseParser.IsSuccessfulCommandResponse(
                    "REMOTE2",
                    "SLAVE   2\r\nReady.\r\n",
                    out responseError),
                "REMOTE2 did not accept the whitespace-tolerant SLAVE 2 response.");
            Require(
                !MbdResponseParser.IsSuccessfulCommandResponse(
                    "REMOTE2",
                    "000 = stcu_UP125_V8 | Ready.\r\n",
                    out responseError) &&
                responseError.IndexOf("SLAVE 2", StringComparison.OrdinalIgnoreCase) >= 0,
                "REMOTE2 incorrectly accepted a BCM register response without SLAVE 2 confirmation.");
            Require(
                !MbdResponseParser.IsSuccessfulCommandResponse(
                    "REMOTE2",
                    "REMOTE SLAVE 1.\r\nReady.\r\n",
                    out responseError),
                "REMOTE2 incorrectly accepted the SLAVE 1 terminal response.");

            Require(
                !TargetBoardComm.HasTerminalPrompt(
                    "Ready.\r\nREMOTE SLAVE 2.\r\ntrailing data\r\n"),
                "Ready text before trailing data was incorrectly accepted as a terminal prompt.");
            Require(
                !MbdResponseParser.IsSuccessfulCommandResponse(
                    "W69=1",
                    "OK\r\n",
                    out responseError),
                "A write response without the terminal Ready prompt was incorrectly accepted.");
            Require(
                MbdResponseParser.IsSuccessfulCommandResponse(
                    "W69=1",
                    "OK\r\nReady.\r\n",
                    out responseError),
                "A complete write response ending in Ready was rejected.");
            Require(
                !MbdResponseParser.IsSuccessfulQueryResponse(
                    "R45",
                    "000 = stcu_UP125_V8\r\nReady.\r\n",
                    out responseError) &&
                responseError.IndexOf("mismatch", StringComparison.OrdinalIgnoreCase) >= 0,
                "A wrong-terminal R45 response was not rejected by register identity.");
            Require(
                !MbdResponseParser.IsSuccessfulQueryResponse(
                    "R45",
                    "045 = 852\r\n",
                    out responseError),
                "An R45 response without the terminal Ready prompt was incorrectly accepted.");
            Require(
                MbdResponseParser.IsSuccessfulQueryResponse(
                    "R45",
                    "045 = 852\r\nReady.\r\n",
                    out responseError),
                "A complete R45 response was rejected.");
        }

        private static void ValidateConditionalFirmwareProgramming(ResolvedTestPlan plan)
        {
            var cases = new[]
            {
                new { Force = false, Skip = false, BadSecond = false, ExpectedFirst = false, ExpectedSecond = false, Accepted = true },
                new { Force = false, Skip = false, BadSecond = true, ExpectedFirst = false, ExpectedSecond = true, Accepted = true },
                new { Force = true, Skip = false, BadSecond = false, ExpectedFirst = true, ExpectedSecond = true, Accepted = true },
                new { Force = true, Skip = true, BadSecond = false, ExpectedFirst = true, ExpectedSecond = true, Accepted = true },
                new { Force = false, Skip = true, BadSecond = true, ExpectedFirst = false, ExpectedSecond = true, Accepted = false }
            };
            foreach (var testCase in cases)
            {
                var log = new List<string>();
                int remote = 0;
                var device = new SK441Device { SkipComInit = true, ForceFirmwareProgramming = testCase.Force };
                device.LogInfo = message =>
                {
                    log.Add(message ?? string.Empty);
                    if (message.EndsWith(": REMOTE1", StringComparison.Ordinal)) remote = 1;
                    if (message.EndsWith(": REMOTE2", StringComparison.Ordinal)) remote = 2;
                };
                device.SimulationMbdResponseOverride = command =>
                    command == "R0" && remote == 2 && testCase.BadSecond
                        ? "000 = wrong_firmware\r\nReady.\r\n" : null;
                var sequence = new SKBCM125Sequence(skipFirmwareProgramming: testCase.Skip,
                    forceFirmwareProgramming: testCase.Force, skipFirmwareVerification: true);
                var context = new TestSequenceContext { TestPlan = plan };
                var steps = (IList)FindMethod(sequence.GetType(), "BuildCommonSteps")
                    .Invoke(sequence, new object[] { context, device });
                FindMethod(sequence.GetType(), "AddElectricalTestSteps").Invoke(sequence, new object[] { steps, context, device });
                FindMethod(sequence.GetType(), "BindStableStepIds").Invoke(null, new object[] { context, steps });
                bool accepted = ((Task<bool>)FindMethod(sequence.GetType(), "EvaluateBcm125FirmwareAsync")
                    .Invoke(sequence, new object[] { context, device, CancellationToken.None })).GetAwaiter().GetResult();
                Require(accepted == testCase.Accepted, "Firmware precheck accepted a prohibited write or rejected valid firmware.");
                bool[] program = (bool[])sequence.GetType().GetField("_programController", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sequence);
                Require(program[0] == testCase.ExpectedFirst && program[1] == testCase.ExpectedSecond,
                    "Per-controller firmware decision/force priority is incorrect.");
                foreach (string flashId in new[] { "BCM125.CH09.S013.C01", "BCM125.CH09.S024.C02" })
                {
                    object step = FindStep(steps, flashId);
                    object result = InvokeStep(step.GetType(), step);
                    Require(Read<bool>(result.GetType(), result, "IsPass") == testCase.Accepted,
                        "Firmware result bypassed a failed precheck.");
                }
                if (accepted && !testCase.Force && !testCase.BadSecond)
                {
                    log.Clear();
                    foreach (object step in steps)
                    {
                        string id = Read<string>(step.GetType(), step, "StepId");
                        if (!id.StartsWith("BCM125.CH09.", StringComparison.Ordinal) || id == "BCM125.CH09.S001.ACTION") continue;
                        object skippedResult = InvokeStep(step.GetType(), step);
                        Require(Read<bool>(skippedResult.GetType(), skippedResult, "IsPass"),
                            "Verified firmware did not skip a programming action.");
                    }
                    Require(!log.Any(message => message.IndexOf("W86=", StringComparison.Ordinal) >= 0 ||
                        message.IndexOf("烧录；", StringComparison.Ordinal) >= 0),
                        "Verified existing firmware still issued RESET/TTL write operations.");
                    object cycle = FindStep(steps, "BCM125.CH10.S002.ACTION");
                    object cycleResult = InvokeStep(cycle.GetType(), cycle);
                    Require(Read<string>(cycleResult.GetType(), cycleResult, "Value").Contains("SKIPPED"),
                        "A no-write run still performed the post-flash power cycle.");
                }
            }
            var forced250 = new SKBCM250Sequence(skipFirmwareProgramming: true, forceFirmwareProgramming: true);
            Require(!(bool)forced250.GetType().GetProperty("SkipFirmwareProgramming", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(forced250),
                "Explicit force must override skip for BCM250 too.");
            var verifyParameter = typeof(FirmwareFlasher).GetMethods().Single(method => method.Name == "FlashFirmwareAsync" &&
                method.GetParameters().Length == 4).GetParameters()[3];
            Require(verifyParameter.HasDefaultValue && (bool)verifyParameter.DefaultValue,
                "Firmware write verification must remain enabled by default.");
        }

        private sealed class ShutdownLoad : HengHuiElectronicLoad
        {
            public readonly Queue<bool> States = new Queue<bool>();
            public bool AlwaysOn;
            public bool ThrowOnRead;
            public int OffCommands;
            public int ZeroSetpoints;
            public override void SetInputState(bool on) { Require(!on, "Shutdown enabled the load."); OffCommands++; }
            public override bool GetInputState()
            {
                if (ThrowOnRead) throw new TimeoutException("Injected unknown input state");
                return States.Count > 0 ? States.Dequeue() : AlwaysOn;
            }
            public override void SetCurrent(float value) { Require(value == 0, "Shutdown did not zero current."); ZeroSetpoints++; }
            public override void SetVoltage(float value) { Require(value == 0, "Shutdown did not zero voltage."); ZeroSetpoints++; }
        }

        private static void ValidateElectronicLoadShutdown()
        {
            foreach (string testCase in new[] { "off", "delayed", "on", "unknown" })
            {
                var load = new ShutdownLoad { AlwaysOn = testCase == "on", ThrowOnRead = testCase == "unknown" };
                if (testCase == "delayed") { load.States.Enqueue(true); load.States.Enqueue(false); load.States.Enqueue(false); }
                var device = new SK441Device { SkipComInit = false };
                typeof(SK441Device).GetProperty("ElectronicLoad").SetValue(device, load);
                bool result = device.SetElectronicLoadCvAsync(0, 0, 0, false).GetAwaiter().GetResult();
                bool expected = testCase == "off" || testCase == "delayed";
                Require(result == expected, "Load shutdown must require actual OFF and ignore the operating mode: " + testCase);
                if (expected)
                {
                    Require(load.ZeroSetpoints == 2, "Load setpoints were not reset after confirmed OFF.");
                    int before = load.OffCommands;
                    Require(device.SetElectronicLoadCvAsync(0, 0, 0, false).GetAwaiter().GetResult() && load.OffCommands == before,
                        "Adjacent confirmed OFF/reset commands were not merged.");
                }
                else Require(load.ZeroSetpoints == 0, "Unknown/ON load was reconfigured as if safely OFF.");
            }
        }

        private static void ValidateMbdRootRecoveryGuards()
        {
            var device = new SK441Device { SkipComInit = true, SimulationBoardType = "BCM-125" };
            MethodInfo recoveryPort = FindMethod(typeof(SK441Device), "GetMbdRootRecoveryPort");
            FieldInfo verifiedPort = typeof(SK441Device).GetField(
                "_verifiedTargetBoardPort", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo configuredPort = typeof(SK441Device).GetField(
                "_targetBoardPortCandidate", BindingFlags.Instance | BindingFlags.NonPublic);
            configuredPort.SetValue(device, "COM987");
            Require(recoveryPort.Invoke(device, new object[] { "EXIT" }) == null,
                "A configured port without protocol verification must not receive recovery EXIT.");

            verifiedPort.SetValue(device, "COM987");
            Require((string)recoveryPort.Invoke(device, new object[] { "EXIT " }) == "COM987" &&
                    (string)recoveryPort.Invoke(device, new object[] { string.Empty }) == "COM987",
                "Root synchronization and communication checks must allow the verified port.");
            foreach (string command in new[] { "R14", "R0", "REMOTE1", "REMOTE2", "DIAG ON", "W86=1", "ACT->FLASH" })
            {
                Require(recoveryPort.Invoke(device, new object[] { command }) == null,
                    "Root recovery must not alter terminal context or replay a stateful command: " + command);
            }

            string oldTtlPort = ComName.uartComName;
            try
            {
                ComName.uartComName = "com987";
                Require(recoveryPort.Invoke(device, new object[] { "EXIT" }) == null,
                    "Recovery must exclude an instrument/TTL port even if it was previously verified.");
            }
            finally
            {
                ComName.uartComName = oldTtlPort;
            }
            foreach (string board in new[] { "BCM-250", "MPS-125", "MPS-250" })
            {
                device.SimulationBoardType = board;
                Require(recoveryPort.Invoke(device, new object[] { "EXIT" }) == null,
                    "BCM125 root recovery must not change other boards' terminal protocols.");
            }
            device.SimulationBoardType = "BCM-125";
            device.Dispose();
            Require(recoveryPort.Invoke(device, new object[] { "EXIT" }) == null,
                "Disposal must invalidate protocol verification from the completed run.");

            string responseError;
            Require(MbdResponseParser.IsSuccessfulCommandResponse("EXIT ",
                    "COMMAND ERROR\r000014 chars answered. Ready.\r", out responseError),
                "EXIT at the root must accept a complete idempotent reply.");
            Require(!MbdResponseParser.IsSuccessfulCommandResponse("EXIT",
                    "000014 chars answered. Ready.\rCOMMAND ERROR\r", out responseError),
                "The logged out-of-order EXIT reply must require a fresh synchronization.");
        }

        private static void ValidateBcm125TerminalStateMachine()
        {
            var sequence = new SKBCM125Sequence(null, null);
            var device = new SK441Device
            {
                SkipComInit = true,
                SimulationBoardType = "BCM-125"
            };
            var log = new List<string>();
            device.LogInfo = message => log.Add(message ?? string.Empty);
            MethodInfo send = FindMethod(
                sequence.GetType(),
                "SendBcm125MbdCommandAsync");
            MethodInfo query = FindMethod(
                sequence.GetType(),
                "QueryBcm125MbdCommandAsync");

            Require(
                InvokePrivateBoolTask(send, sequence, device, "REMOTE2", 0),
                "REMOTE2 failed from an unknown BCM125 terminal state.");
            RequireCommandOrder(log, "EXIT", "REMOTE2",
                "Unknown terminal state did not synchronize through EXIT before REMOTE2.");

            log.Clear();
            Require(
                InvokePrivateBoolTask(send, sequence, device, "W94=1", 0),
                "MBD-root W94 failed while returning from REMOTE2.");
            RequireCommandOrder(log, "EXIT", "W94=1",
                "MBD-root W94 was not protected by EXIT from REMOTE2.");

            log.Clear();
            Require(
                !InvokePrivateBoolTask(send, sequence, device, "W69=1", 0),
                "SLAVE-only W69 was accepted at the MBD root terminal.");
            Require(!ContainsSimulatedCommand(log, "W69=1"),
                "Rejected SLAVE-only W69 was still transmitted at the MBD root terminal.");

            Require(
                InvokePrivateBoolTask(send, sequence, device, "REMOTE1", 0),
                "REMOTE1 failed from the synchronized MBD root terminal.");
            Require(
                InvokePrivateBoolTask(send, sequence, device, "W69=1", 0),
                "SLAVE-only W69 failed after REMOTE1 confirmation.");

            log.Clear();
            Require(
                InvokePrivateBoolTask(send, sequence, device, "REMOTE2", 0),
                "REMOTE1 to REMOTE2 transition failed.");
            RequireCommandOrder(log, "EXIT", "REMOTE2",
                "REMOTE1 to REMOTE2 transition did not return through MBD root.");

            log.Clear();
            string rootResponse = InvokePrivateStringTask(
                query,
                sequence,
                device,
                "R81",
                0);
            Require(
                MbdResponseParser.ParseScalar("R81", rootResponse) == 1,
                "MBD-root R81 query did not return a valid response.");
            RequireCommandOrder(log, "EXIT", "R81",
                "MBD-root R81 query was not protected by EXIT from REMOTE2.");

            bool rejectedRemoteQuery = false;
            try
            {
                InvokePrivateStringTask(query, sequence, device, "R45", 0);
            }
            catch (InvalidOperationException)
            {
                rejectedRemoteQuery = true;
            }
            Require(rejectedRemoteQuery,
                "SLAVE-only R45 query was accepted without REMOTE1/2 confirmation.");

            Require(
                InvokePrivateBoolTask(send, sequence, device, "REMOTE1", 0),
                "Cannot enter REMOTE1 before the discharge power-change check.");
            log.Clear();
            MethodInfo communicationCheck = FindMethod(
                sequence.GetType(), "CheckBcm125MbdCommunicationAsync");
            var checkTask = (Task<bool>)communicationCheck.Invoke(
                sequence, new object[] { device, true });
            Require(checkTask.GetAwaiter().GetResult() &&
                    ContainsSimulatedCommand(log, "EXIT"),
                "Discharge communication check accepted a stale open port without EXIT response.");
        }

        private static void ValidateAllBcm125MockQueries()
        {
            var device = new SK441Device
            {
                SkipComInit = true,
                SimulationBoardType = "BCM-125"
            };
            string[] commands =
            {
                "R0", "R10", "R14", "R15", "R18", "R19", "R20", "R21",
                "R40", "R41", "R42", "R43", "R44", "R45", "R50", "R51",
                "R52", "R53", "R54", "R55", "R56", "R57", "R59", "R60",
                "R61", "R68", "R69", "R81", "R92", "R93", "ACT->FLASH"
            };

            foreach (string command in commands)
            {
                string response = device.QueryMbdCommandAsync(command, 0)
                    .GetAwaiter()
                    .GetResult();
                string error;
                Require(
                    MbdResponseParser.IsSuccessfulQueryResponse(
                        command,
                        response,
                        out error),
                    "BCM125 mock query failed protocol validation for " + command +
                    ": " + error);
                if (command == "R0")
                    Require(
                        MbdResponseParser.ParsePayloadText("R0", response) ==
                        device.GetBcm125ExpectedFirmwareVersion(),
                        "BCM125 simulated R0 version differs from the expected firmware version.");
            }
        }

        private static bool InvokePrivateBoolTask(
            MethodInfo method,
            object instance,
            SK441Device device,
            string command,
            int waitAfterMs)
        {
            var task = (Task<bool>)method.Invoke(
                instance,
                new object[] { device, command, waitAfterMs });
            return task.GetAwaiter().GetResult();
        }

        private static string InvokePrivateStringTask(
            MethodInfo method,
            object instance,
            SK441Device device,
            string command,
            int waitAfterMs)
        {
            var task = (Task<string>)method.Invoke(
                instance,
                new object[] { device, command, waitAfterMs, true });
            return task.GetAwaiter().GetResult();
        }

        private static void RequireCommandOrder(
            IList<string> log,
            string first,
            string second,
            string message)
        {
            int firstIndex = Enumerable.Range(0, log.Count)
                .FirstOrDefault(index => ContainsSimulatedCommand(log[index], first));
            int secondIndex = Enumerable.Range(0, log.Count)
                .FirstOrDefault(index => ContainsSimulatedCommand(log[index], second));
            bool firstFound = log.Any(item => ContainsSimulatedCommand(item, first));
            bool secondFound = log.Any(item => ContainsSimulatedCommand(item, second));
            Require(firstFound && secondFound && secondIndex > firstIndex, message);
        }

        private static bool ContainsSimulatedCommand(string logLine, string command)
        {
            string value = logLine ?? string.Empty;
            return value.IndexOf(
                       "USB-B/MBD 发送命令: " + command,
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf(
                       "USB-B/MBD 查询: " + command,
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsSimulatedCommand(
            IEnumerable<string> log,
            string command)
        {
            return log.Any(item => ContainsSimulatedCommand(item, command));
        }

        private static void ValidateResetFloatingPointAndSt2Wait(IList steps)
        {
            object runningTimeStep = FindStep(steps, "BCM125.CH12.S007.R10");
            object runningTimeResult = InvokeStep(runningTimeStep.GetType(), runningTimeStep);
            double? numericValue = Read<double?>(
                runningTimeResult.GetType(),
                runningTimeResult,
                "NumericValue");
            Require(numericValue.HasValue &&
                    Math.Abs(numericValue.Value - Math.Round(numericValue.Value)) > 0.001,
                "BCM125 reset running time was truncated instead of retaining decimals.");

            object waitStep = FindStep(steps, "BCM125.CH12.S015.ACTION");
            TestPlanStepDefinition waitDefinition = Read<TestPlanStepDefinition>(
                waitStep.GetType(),
                waitStep,
                "PlanDefinition");
            Require(waitDefinition.DelayMs == 3000,
                "BCM125 ST2 pre-reset wait is not controlled by XML DelayMs=3000.");

            // Replay the real log: sampling x=2.606 too early made a valid
            // reset (y=0.925) fail the unchanged x-y >= 2 requirement.
            var readings = new Queue<double>(new[] { 2.606, 3.6, 0.925 });
            int readCount = 0;
            _simulationDevice.SimulationMbdResponseOverride = command =>
            {
                if (!string.Equals(command, "R10", StringComparison.OrdinalIgnoreCase)) return null;
                readCount++;
                return "R10=" + readings.Dequeue().ToString(CultureInfo.InvariantCulture) + "\r\nReady.\r\n";
            };
            try
            {
                object baseline = InvokeStep(runningTimeStep.GetType(), runningTimeStep);
                double x = Read<double?>(baseline.GetType(), baseline, "NumericValue").Value;
                Require(Math.Abs(x - 3.6) < 0.001 && readCount == 2,
                    "F01 did not establish its baseline before RESET.");
                object afterResetStep = FindStep(steps, "BCM125.CH12.S013.R10");
                object afterReset = InvokeStep(afterResetStep.GetType(), afterResetStep);
                double y = Read<double?>(afterReset.GetType(), afterReset, "NumericValue").Value;
                Require(Math.Abs(y - 0.925) < 0.001 && readCount == 3,
                    "F01 retried/changed the post-reset measurement.");
                const string xKey = "BCM125.RESET_FLAT.ST1_X";
                const string yKey = "BCM125.RESET_FLAT.ST1_Y";
                _simulationContext.StoreValue(xKey, x, x.ToString(), "s", true);
                _simulationContext.StoreValue(yKey, y, y.ToString(), "s", true);
                object f01 = FindStep(steps, "BCM125.CH12.S014.F01");
                object validReset = InvokeStep(f01.GetType(), f01);
                Require(Read<bool>(validReset.GetType(), validReset, "IsPass"),
                    "Valid reset still failed F01.");
                _simulationContext.StoreValue(xKey, 2.606, "2.606", "s", true);
                object invalidDifference = InvokeStep(f01.GetType(), f01);
                Require(!Read<bool>(invalidDifference.GetType(), invalidDifference, "IsPass"),
                    "F01 weakened the required x-y difference.");
                _simulationContext.StoreValue(xKey, 5, "5", "s", true);
                _simulationContext.StoreValue(yKey, 1.6, "1.6", "s", true);
                object invalidReset = InvokeStep(f01.GetType(), f01);
                Require(!Read<bool>(invalidReset.GetType(), invalidReset, "IsPass"),
                    "F01 weakened the allowed post-reset running time.");
            }
            finally { _simulationDevice.SimulationMbdResponseOverride = null; }
        }

        private static void ValidateExplicitRemoteTransitions(IList steps)
        {
            var transitions = new[]
            {
                Tuple.Create("BCM125.CH18.S052.ACTION", "REMOTE2"),
                Tuple.Create("BCM125.CH20.S013.ACTION", "REMOTE1"),
                Tuple.Create("BCM125.CH20.S024.ACTION", "REMOTE1"),
                Tuple.Create("BCM125.CH20.S044.ACTION", "REMOTE2"),
                Tuple.Create("BCM125.CH20.S055.ACTION", "REMOTE2"),
                Tuple.Create("BCM125.CH21.S013.ACTION", "REMOTE1"),
                Tuple.Create("BCM125.CH21.S027.ACTION", "REMOTE2"),
                Tuple.Create("BCM125.CH22.S015.ACTION", "REMOTE1"),
                Tuple.Create("BCM125.CH22.S041.ACTION", "REMOTE2"),
                Tuple.Create("BCM125.CH27.S009.ACTION", "REMOTE1"),
                Tuple.Create("BCM125.CH27.S023.ACTION", "REMOTE2")
            };

            foreach (Tuple<string, string> transition in transitions)
            {
                var log = new List<string>();
                _simulationDevice.LogInfo = message => log.Add(message ?? string.Empty);

                object step = FindStep(steps, transition.Item1);
                object result = InvokeStep(step.GetType(), step);
                Require(Read<bool>(result.GetType(), result, "IsPass"),
                    transition.Item1 + " failed in simulation.");

                int exitIndex = log.FindIndex(message =>
                    message.IndexOf("USB-B/MBD", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    message.EndsWith(": EXIT", StringComparison.OrdinalIgnoreCase));
                int remoteIndex = log.FindIndex(message =>
                    message.IndexOf("USB-B/MBD", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    message.EndsWith(": " + transition.Item2, StringComparison.OrdinalIgnoreCase));
                Require(exitIndex >= 0 && remoteIndex > exitIndex,
                    transition.Item1 + " did not execute EXIT before " + transition.Item2 + ".");
            }

            _simulationDevice.LogInfo = null;
        }

        private static void ValidateChapter23MbdCommands(IList steps)
        {
            var cases = new[]
            {
                Tuple.Create("BCM125.CH23.S015.ACTION", "BCM125.CH23.S016.ACTION", "DIAG ON"),
                Tuple.Create("BCM125.CH23.S017.ACTION", "BCM125.CH23.S061.ACTION", "EXIT"),
                Tuple.Create("BCM125.CH23.S069.ACTION", "BCM125.CH23.S070.ACTION", "DIAG ON"),
                Tuple.Create("BCM125.CH23.S071.ACTION", "BCM125.CH23.S115.ACTION", "EXIT")
            };

            Action<string> previousLogInfo = _simulationDevice.LogInfo;
            var log = new List<string>();
            _simulationDevice.LogInfo = message => log.Add(message ?? string.Empty);
            try
            {
                foreach (Tuple<string, string, string> testCase in cases)
                {
                    object setup = FindStep(steps, testCase.Item1);
                    object setupResult = InvokeStep(setup.GetType(), setup);
                    Require(Read<bool>(setupResult.GetType(), setupResult, "IsPass"),
                        testCase.Item1 + " failed in simulation.");

                    log.Clear();
                    object step = FindStep(steps, testCase.Item2);
                    object result = InvokeStep(step.GetType(), step);
                    Require(Read<bool>(result.GetType(), result, "IsPass"),
                        testCase.Item2 + " failed in simulation.");

                    string[] commands = log.Where(message => message.IndexOf(
                        "USB-B/MBD 发送命令: ", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                    Require(commands.Length == 1 && commands[0].EndsWith(
                            ": " + testCase.Item3, StringComparison.OrdinalIgnoreCase),
                        testCase.Item2 + " sent unexpected MBD commands: " +
                        string.Join(" | ", commands) + ".");
                }
            }
            finally
            {
                _simulationDevice.LogInfo = previousLogInfo;
            }
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
                    return definition != null &&
                           definition.DelayMs.HasValue &&
                           Regex.IsMatch(
                               definition.Name ?? string.Empty,
                               @"(?:等待|wait)\s*\d+\s*ms",
                               RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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

        private static void ValidateMeasuredReadinessBindings(IList steps)
        {
            string[] readinessStepIds =
            {
                "BCM125.CH18.S022.ACTION",
                "BCM125.CH18.S038.ACTION",
                "BCM125.CH18.S049.ACTION",
                "BCM125.CH19.S009.ACTION",
                "BCM125.CH19.S019.ACTION",
                "BCM125.CH19.S023.ACTION",
                "BCM125.CH19.S032.ACTION",
                "BCM125.CH19.S041.ACTION",
                "BCM125.CH19.S051.ACTION",
                "BCM125.CH19.S055.ACTION",
                "BCM125.CH19.S064.ACTION",
                "BCM125.CH25.S035.ACTION",
                "BCM125.CH25.S038.ACTION",
                "BCM125.CH25.S081.ACTION",
                "BCM125.CH25.S084.ACTION"
            };

            foreach (string stepId in readinessStepIds)
            {
                object step = FindStep(steps, stepId);
                Type type = step.GetType();
                TestPlanStepDefinition definition =
                    Read<TestPlanStepDefinition>(type, step, "PlanDefinition");
                int? originalDelay = definition.DelayMs;
                try
                {
                    definition.DelayMs = 0;
                    object result = InvokeStep(type, step);
                    Require(Read<bool>(result.GetType(), result, "IsPass") &&
                            Read<string>(result.GetType(), result, "Value") == "0 ms",
                        "BCM125 measured readiness binding failed for " + stepId + ".");
                }
                finally
                {
                    definition.DelayMs = originalDelay;
                }
            }
        }

        private static void ValidateAdaptiveWaitBindings(IList steps)
        {
            foreach (string stepId in new[]
            {
                "BCM125.CH09.S006.ACTION", "BCM125.CH12.S002.ACTION",
                "BCM125.CH23.S034.ACTION", "BCM125.CH23.S088.ACTION",
                "BCM125.CH25.S016.ACTION", "BCM125.CH25.S062.ACTION",
                "BCM125.CH26.S012.ACTION", "BCM125.CH26.S046.ACTION"
            })
            {
                object step = FindStep(steps, stepId);
                Type type = step.GetType();
                TestPlanStepDefinition definition =
                    Read<TestPlanStepDefinition>(type, step, "PlanDefinition");
                int? originalDelay = definition.DelayMs;
                try
                {
                    definition.DelayMs = 0;
                    object result = InvokeStep(type, step);
                    Require(Read<bool>(result.GetType(), result, "IsPass") &&
                            Read<string>(result.GetType(), result, "Value") == "0 ms",
                        "BCM125 adaptive wait binding failed for " + stepId + ".");
                }
                finally
                {
                    definition.DelayMs = originalDelay;
                }
            }
        }

        private sealed class ReadbackPower : HengHuiPowerSupply
        {
            public float Voltage;
            public int ReadCount;
            public override void ReadVoltageCurrent(out float voltage, out float current)
            {
                ReadCount++;
                voltage = Voltage;
                current = 6;
            }
        }

        private static void ValidateAuxiliaryWarnings(IList steps)
        {
            var supply = new ReadbackPower { Voltage = 10 };
            var originalSupply = _simulationDevice.HengHuiPower2;
            var oldError = _simulationDevice.LogError;
            var errors = new List<string>();
            _simulationDevice.SkipComInit = false;
            _simulationDevice.LogError = errors.Add;
            typeof(SK441Device).GetProperty("HengHuiPower2").SetValue(_simulationDevice, supply);
            try
            {
                foreach (string id in new[] { "BCM125.CH23.S034.ACTION", "BCM125.CH23.S088.ACTION",
                    "BCM125.CH24.S027.ACTION", "BCM125.CH24.S077.ACTION" })
                {
                    object step = FindStep(steps, id);
                    var definition = Read<TestPlanStepDefinition>(step.GetType(), step, "PlanDefinition");
                    int? oldDelay = definition.DelayMs;
                    definition.DelayMs = id.StartsWith("BCM125.CH23.", StringComparison.Ordinal) ? 800 : 300;
                    try
                    {
                        supply.Voltage = 10;
                        int oldReads = supply.ReadCount;
                        object ready = InvokeStep(step.GetType(), step);
                        Require(Read<bool>(ready.GetType(), ready, "IsPass") &&
                            !Read<bool>(ready.GetType(), ready, "IsWarning") && supply.ReadCount > oldReads,
                            id + " did not use the actual String/SECONDARY supply readback.");
                        supply.Voltage = 0;
                        object timeout = InvokeStep(step.GetType(), step);
                        Require(Read<bool>(timeout.GetType(), timeout, "IsPass") &&
                            Read<bool>(timeout.GetType(), timeout, "IsWarning") &&
                            Read<string>(timeout.GetType(), timeout, "Value").Contains("TIMEOUT"),
                            id + " timeout incorrectly failed the product.");
                        Require(errors.Count == 0, "Advisory wait logged a hard error.");
                    }
                    finally { definition.DelayMs = oldDelay; }
                }
                Require(!_simulationDevice.WaitForPowerVoltageStableAsync("SECONDARY", 10, 0, 1,
                        CancellationToken.None).GetAwaiter().GetResult() && errors.Count > 0,
                    "A mandatory power stability check was weakened.");
            }
            finally
            {
                _simulationDevice.SkipComInit = true;
                _simulationDevice.LogError = oldError;
                typeof(SK441Device).GetProperty("HengHuiPower2").SetValue(_simulationDevice, originalSupply);
            }
            _simulationDevice.SimulationMbdResponseOverride = command => command == "R68"
                ? "068 = 57\r\nReady.\r\n" : command == "R69" ? "069 = 0\r\nReady.\r\n" : null;
            try
            {
                object observe = FindStep(steps, "BCM125.CH18.S008.R68");
                object warning = InvokeStep(observe.GetType(), observe);
                Require(Read<bool>(warning.GetType(), warning, "IsPass") &&
                    Read<bool>(warning.GetType(), warning, "IsWarning") &&
                    Read<double?>(warning.GetType(), warning, "NumericValue") == 57,
                    "18.8 did not retain its out-of-range observation as a warning.");
                object formal = FindStep(steps, "BCM125.CH18.S023.L01");
                object fail = InvokeStep(formal.GetType(), formal);
                Require(!Read<bool>(fail.GetType(), fail, "IsPass") &&
                    !Read<bool>(fail.GetType(), fail, "IsWarning"), "A formal measurement was downgraded to warning.");
            }
            finally { _simulationDevice.SimulationMbdResponseOverride = null; }
            Type gridType = typeof(SK441Device).Assembly.GetType("TestPlatform.TestSequences.DataTableTestGridService");
            Require((string)FindMethod(gridType, "ToDisplayText").Invoke(null, new object[] { StepExecutionStatus.Warning }) == "警告",
                "Warning UI status was not rendered.");
            Type windowType = typeof(SK441Device).Assembly.GetType("TestPlatform.MainWindow");
            Require((bool)FindMethod(windowType, "IsTerminalRuntimeStatus").Invoke(null, new object[] { "警告" }),
                "Warning left the chapter progress pending.");
        }

        private static void ValidateCalibrationDateReadback(IList steps)
        {
            foreach (string id in new[] { "BCM125.CH27.S023.ACTION", "BCM125.CH27.S024.ACTION", "BCM125.CH27.S029.ACTION" })
            {
                object setup = FindStep(steps, id);
                object setupResult = InvokeStep(setup.GetType(), setup);
                Require(Read<bool>(setupResult.GetType(), setupResult, "IsPass"),
                    "Cannot prepare simulated calibration-date readback.");
            }
            var originalLog = _simulationDevice.LogInfo;
            var log = new List<string>();
            _simulationDevice.LogInfo = log.Add;
            try
            {
                foreach (string testCase in new[] { "transient", "persistent", "wrong-date" })
                {
                    int reads = 0;
                    log.Clear();
                    _simulationDevice.SimulationMbdResponseOverride = command =>
                    {
                        if (command != "R40") return null;
                        reads++;
                        if (testCase == "transient" && reads == 2) return null;
                        return "040 = " + (testCase == "wrong-date" ? "01/01/2000 00:00" : "09/10/2026 1W40=09/") + "\r\nReady.\r\n";
                    };
                    object step = FindStep(steps, "BCM125.CH27.S034.U05");
                    object result = InvokeStep(step.GetType(), step);
                    Require(Read<bool>(result.GetType(), result, "IsPass") == (testCase == "transient") &&
                        reads == (testCase == "wrong-date" ? 1 : 2), "U05 date recovery/strict comparison incorrect: " + testCase);
                    Require(!ContainsSimulatedCommand(log, "W40=") && !ContainsSimulatedCommand(log, "ACT->FLASH"),
                        "U05 verification silently rewrote the DUT.");
                }
            }
            finally
            {
                _simulationDevice.SimulationMbdResponseOverride = null;
                _simulationDevice.LogInfo = originalLog;
            }
        }

        private static void ValidateSignedShuntPolarity(IList steps)
        {
            FieldInfo currentField = typeof(SK441Device).GetField(
                "_mockStringCurrent", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo ccbField = typeof(SK441Device).GetField(
                "_mockCcbEnabled", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo setpointField = typeof(SK441Device).GetField(
                "_mockCcbCurrentSetpointMa", BindingFlags.Instance | BindingFlags.NonPublic);
            object originalCurrent = currentField.GetValue(_simulationDevice);
            object originalCcb = ccbField.GetValue(_simulationDevice);
            object originalSetpoint = setpointField.GetValue(_simulationDevice);
            try
            {
                currentField.SetValue(_simulationDevice, 6.0);
                ccbField.SetValue(_simulationDevice, true);
                setpointField.SetValue(_simulationDevice, 0.0);
                foreach (string stepId in new[]
                {
                    "BCM125.CH23.S038.Q10", "BCM125.CH23.S092.Q26"
                })
                {
                    object step = FindStep(steps, stepId);
                    object result = InvokeStep(step.GetType(), step);
                    Require(Read<bool>(result.GetType(), result, "IsPass") &&
                            Read<double?>(result.GetType(), result, "NumericValue") < 0,
                        "BCM125 negative DAQ shunt current lost its polarity at " + stepId + ".");
                }
            }
            finally
            {
                currentField.SetValue(_simulationDevice, originalCurrent);
                ccbField.SetValue(_simulationDevice, originalCcb);
                setpointField.SetValue(_simulationDevice, originalSetpoint);
            }
        }

        private static void ValidateMbdAverageCadence(IList steps)
        {
            object step = steps.Cast<object>().Single(value =>
                Read<string>(value.GetType(), value, "StepId").EndsWith(".J04", StringComparison.Ordinal));
            var previousOverride = _simulationDevice.SimulationMbdResponseOverride;
            try
            {
                foreach (int responseMs in new[] { 120, 320 })
                {
                    var timer = Stopwatch.StartNew();
                    var starts = new List<double>();
                    _simulationDevice.SimulationMbdResponseOverride = command =>
                    {
                        if (command != "R43") return null;
                        starts.Add(timer.Elapsed.TotalMilliseconds);
                        Thread.Sleep(responseMs);
                        return "043 = " + (137500 + (starts.Count - 1) * 1000) + "\r\nReady.\r\n";
                    };
                    object result = InvokeStep(step.GetType(), step);
                    Require(starts.Count == 5 && Read<bool>(result.GetType(), result, "IsPass") &&
                        Read<double?>(result.GetType(), result, "NumericValue") == 139500,
                        "Average cadence changed the sample count, values or verdict.");
                    double[] intervals = starts.Skip(1).Select((start, index) => start - starts[index]).ToArray();
                    Require(intervals.All(interval => interval >= 199),
                        "Average cadence sent requests before the required 200ms interval.");
                    Require(intervals.Average() < (responseMs < 200 ? 280 : 460),
                        "Average cadence still adds response time to the full sampling delay.");
                }

                using (var cancellation = new CancellationTokenSource())
                {
                    int queries = 0;
                    _simulationDevice.SimulationMbdResponseOverride = command =>
                    {
                        if (command != "R43") return null;
                        queries++;
                        cancellation.Cancel();
                        return "043 = 139500\r\nReady.\r\n";
                    };
                    Delegate action = Read<Delegate>(step.GetType(), step, "Action");
                    bool canceled = false;
                    try { ((Task)action.DynamicInvoke(cancellation.Token)).GetAwaiter().GetResult(); }
                    catch (OperationCanceledException) { canceled = true; }
                    Require(canceled && queries == 1,
                        "Canceled average acquisition continued sending commands.");
                }
            }
            finally { _simulationDevice.SimulationMbdResponseOverride = previousOverride; }
        }

        private static void ValidateChargingMeasurementBatches(ResolvedTestPlan plan)
        {
            var previousDevice = _simulationDevice;
            var previousSequence = _simulationSequence;
            var previousContext = _simulationContext;
            try
            {
                IList steps = BuildSteps(plan);
                var device = _simulationDevice;
                var context = _simulationContext;
                device.SetVbusGeneratorAsync(1, 1, 150, true).GetAwaiter().GetResult();
                device.SetStringGeneratorAsync(0, 0, 150, false).GetAwaiter().GetResult();
                device.SendMbdCommandAsync("W69=1", 0).GetAwaiter().GetResult();
                object remote = FindStep(steps, "BCM125.CH23.S017.ACTION");
                InvokeStep(remote.GetType(), remote);
                object[] batch = new[] { "BCM125.CH23.S023.Q01", "BCM125.CH23.S024.Q02",
                    "BCM125.CH23.S025.Q03", "BCM125.CH23.S026.Q04" }
                    .Select(id => FindStep(steps, id)).ToArray();
                int adc0Reads = 0, adc14Reads = 0, daqReads = 0;
                double adc0 = 45, adc14 = 1425;
                bool corruptAdc14 = false;
                var messages = new ConcurrentQueue<string>();
                using (var daqStarted = new ManualResetEventSlim(false))
                {
                    device.LogInfo = message =>
                    {
                        messages.Enqueue(message ?? string.Empty);
                        if ((message ?? string.Empty).Contains("模拟 DAQ 通道"))
                        {
                            Interlocked.Increment(ref daqReads);
                            daqStarted.Set();
                        }
                    };
                    device.SimulationMbdResponseOverride = command =>
                    {
                        if (command == "R50")
                        {
                            if (Interlocked.Increment(ref adc0Reads) == 1)
                                Require(daqStarted.Wait(2000), "DAQ did not run alongside the blocked MBD branch.");
                            return "050 = " + adc0.ToString(CultureInfo.InvariantCulture) + "\r\nReady.\r\n";
                        }
                        if (command == "R56")
                        {
                            Interlocked.Increment(ref adc14Reads);
                            if (corruptAdc14) return "056 = ERROR\r\nReady.\r\n";
                            return "056 = " + adc14.ToString(CultureInfo.InvariantCulture) + "\r\nReady.\r\n";
                        }
                        return null;
                    };
                    foreach (object member in batch)
                    {
                        object result = InvokeStep(member.GetType(), member);
                        Require(Read<bool>(result.GetType(), result, "IsPass"), "Parallel Q measurement failed.");
                    }
                    Require(adc0Reads == 4 && adc14Reads == 4 && daqReads == 16,
                        "Parallel Q measurements were dropped or sampled twice.");

                    context.RequiresSynchronousUiResults = () => true;
                    int beforeAdc0 = adc0Reads, beforeAdc14 = adc14Reads, beforeDaq = daqReads;
                    InvokeStep(batch[0].GetType(), batch[0]);
                    Require(adc0Reads == beforeAdc0 + 4 && adc14Reads == beforeAdc14 && daqReads == beforeDaq,
                        "Engineering single-step prefetched future rows.");
                    InvokeStep(batch[1].GetType(), batch[1]);
                    InvokeStep(batch[2].GetType(), batch[2]);

                    context.RequiresSynchronousUiResults = () => false;
                    InvokeStep(batch[0].GetType(), batch[0]);
                    adc14 = 1600;
                    context.RequiresSynchronousUiResults = () => true;
                    beforeAdc14 = adc14Reads;
                    object changed = InvokeStep(batch[1].GetType(), batch[1]);
                    Require(adc14Reads == beforeAdc14 + 4 &&
                        !Read<bool>(changed.GetType(), changed, "IsPass") &&
                        Read<double?>(changed.GetType(), changed, "NumericValue") == 1600,
                        "Engineering pause reused stale passing data.");

                    context.RequiresSynchronousUiResults = () => false;
                    adc0 = 0;
                    adc14 = 1425;
                    object failed = InvokeStep(batch[0].GetType(), batch[0]);
                    Require(!Read<bool>(failed.GetType(), failed, "IsPass"), "Parallel sampling hid an out-of-range Q01.");
                    adc0 = 45;
                    beforeAdc14 = adc14Reads;
                    InvokeStep(batch[1].GetType(), batch[1]);
                    Require(adc14Reads == beforeAdc14 + 4, "A failed row left later cached results active.");

                    corruptAdc14 = true;
                    object validFirst = InvokeStep(batch[0].GetType(), batch[0]);
                    Require(Read<bool>(validFirst.GetType(), validFirst, "IsPass"),
                        "An R56 error incorrectly failed the independent Q01 reading.");
                    bool rejected = false;
                    try { InvokeStep(batch[1].GetType(), batch[1]); }
                    catch (Exception) { rejected = true; }
                    Require(rejected, "Parallel sampling accepted a malformed R56 response.");
                    corruptAdc14 = false;
                    beforeDaq = daqReads;
                    InvokeStep(batch[2].GetType(), batch[2]);
                    Require(daqReads == beforeDaq + 8, "A failed parallel branch left a later DAQ cache active.");

                    using (var cancellation = new CancellationTokenSource())
                    {
                        var originalOverride = device.SimulationMbdResponseOverride;
                        int queries = 0;
                        device.SimulationMbdResponseOverride = command =>
                        {
                            if (command == "R50")
                            {
                                queries++;
                                cancellation.Cancel();
                                return "050 = 45\r\nReady.\r\n";
                            }
                            return originalOverride(command);
                        };
                        bool canceled = false;
                        try
                        {
                            Delegate action = Read<Delegate>(batch[0].GetType(), batch[0], "Action");
                            ((Task)action.DynamicInvoke(cancellation.Token)).GetAwaiter().GetResult();
                        }
                        catch (OperationCanceledException) { canceled = true; }
                        finally { device.SimulationMbdResponseOverride = originalOverride; }
                        Require(canceled && queries == 1, "Canceled parallel sampling continued MBD requests.");
                        beforeAdc14 = adc14Reads;
                        InvokeStep(batch[1].GetType(), batch[1]);
                        Require(adc14Reads == beforeAdc14 + 4, "Canceled parallel sampling left stale results.");
                    }

                    var enabled = (HashSet<string>)typeof(ResolvedTestPlan).GetField("_enabledStepIds",
                        BindingFlags.Instance | BindingFlags.NonPublic).GetValue(plan);
                    string skippedId = Read<string>(batch[1].GetType(), batch[1], "StepId");
                    enabled.Remove(skippedId);
                    try
                    {
                        beforeAdc14 = adc14Reads;
                        beforeDaq = daqReads;
                        InvokeStep(batch[0].GetType(), batch[0]);
                        Require(adc14Reads == beforeAdc14 && daqReads == beforeDaq,
                            "Partial test plan measured a skipped or future Q row.");
                    }
                    finally { enabled.Add(skippedId); }
                }
            }
            finally
            {
                _simulationDevice = previousDevice;
                _simulationSequence = previousSequence;
                _simulationContext = previousContext;
            }
        }

        private static void ValidateStringTestTimedSampling(IList steps, string repositoryRoot)
        {
            MethodInfo parser = typeof(DaqMultimeter).GetMethod("ParseTimedVoltageDcSampleSeries",
                BindingFlags.Static | BindingFlags.NonPublic);
            var voltages = Enumerable.Repeat(0.0117, 100).ToArray();
            for (int index = 20; index < 25; index++) voltages[index] = 0.0035;
            string response = string.Join(",", Enumerable.Range(0, 100).SelectMany(index => new[]
            {
                voltages[index].ToString("R", CultureInfo.InvariantCulture),
                (index * 0.05).ToString("R", CultureInfo.InvariantCulture)
            }));
            var series = (DaqVoltageSampleSeries)parser.Invoke(null, new object[] { response, 100 });
            series.ValidateInterval(50);
            Require(series.Voltages.Length == 100 && series.Voltages.Count(value => value < 0.00615) == 5,
                "T02/T05 parsing discarded short low-current readings.");
            Require(Math.Abs(series.Voltages.Average() / 0.0205 * 1000 - 550.731707317) < 0.000001,
                "T02/T05 average must include all five 3.5mV dips.");

            var sequence = new SKBCM125Sequence(null, null);
            var messages = new List<string>();
            sequence.LogInfo += messages.Add;
            FindMethod(sequence.GetType(), "LogStringTestShuntSamples").Invoke(sequence,
                new object[] { "T02", series, 0.0205, 300.0, 1000.0 });
            Require(messages.Count == 1 && messages[0].Contains("低于300mA=5点") &&
                    messages.All(message => !message.Contains("序号:t_ms")),
                "Passing shunt results must retain the summary and omit per-point debug logs.");
            messages.Clear();
            FindMethod(sequence.GetType(), "LogStringTestShuntSamples").Invoke(sequence,
                new object[] { "T02", series, 0.0205, 300.0, 500.0 });
            Require(messages.Any(message => message.Contains("低于300mA=5点/5%") &&
                        message.Contains("最长连续低值=5点") && message.Contains("跨度=200ms")),
                "T02/T05 low-value diagnostics must retain dip count and timestamp span.");
            Require(messages.Any(message => message.Contains("21:1000/3.5/170.732")),
                "T02/T05 raw sample log omitted the low voltage or timestamp.");

            var diagnosticMessages = new List<string>();
            Action<string> originalLog = _simulationDevice.LogInfo;
            _simulationDevice.LogInfo = diagnosticMessages.Add;
            try
            {
                string partialRaw = string.Join(",", response.Split(',').Take(76 * 2));
                MethodInfo diagnosticLog = typeof(SK441Device).GetMethod("LogInvalidDaqTimedScanData",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                diagnosticLog.Invoke(_simulationDevice, new object[] { 113, 100, partialRaw });
                Require(diagnosticMessages.Any(message => message.Contains("RAW字段121-152")) &&
                        diagnosticMessages.Any(message => message.Contains("0.0035")) &&
                        diagnosticMessages.Any(message => message.Contains("不参与PASS判定")),
                    "Partial 76/100 scan diagnostics must preserve all raw fields and the low dip without passing.");
                diagnosticLog.Invoke(_simulationDevice, new object[] { 115, 100, "bad,0,9.9E37,0.1" });
                Require(diagnosticMessages.Any(message => message.Contains("bad,0,9.9E37,0.1")),
                    "Malformed/overrange raw samples must remain visible in failed-window diagnostics.");
            }
            finally { _simulationDevice.LogInfo = originalLog; }

            RequireInvalidTimedSeries(() => parser.Invoke(null, new object[] { "0.0117,0", 2 }),
                "Missing sample must invalidate a scan.");
            RequireInvalidTimedSeries(() => parser.Invoke(null, new object[] { "bad,0,0.0117,0.05", 2 }),
                "Malformed voltage must invalidate a scan.");
            RequireInvalidTimedSeries(() => parser.Invoke(null, new object[] { "9.9E37,0,0.0117,0.05", 2 }),
                "SCPI overrange must invalidate a scan.");
            RequireInvalidTimedSeries(() => parser.Invoke(null, new object[] { "NaN,0,0.0117,0.05", 2 }),
                "Non-finite voltage must invalidate a scan.");
            RequireInvalidTimedSeries(() => parser.Invoke(null, new object[] { "0.0117,0,0.0117,0", 2 }),
                "Duplicate timestamp must invalidate a scan.");
            RequireInvalidTimedSeries(() => new DaqVoltageSampleSeries(
                new[] { 0.0117, 0.0117 }, new[] { 0.0, 0.1 }).ValidateInterval(50),
                "DAQ timer overrun must invalidate a fixed 50ms window.");

            // The observed first completion gap may be short. Retain its voltage,
            // while keeping later gaps and the whole-window span bounded.
            var startup = new DaqVoltageSampleSeries(new[] { 0.0176249506, 0.0176248893, 0.0176261343 },
                new[] { 0.086, 0.169, 0.269 });
            startup.ValidateInterval(100, true);
            RequireInvalidTimedSeries(() => startup.ValidateInterval(100),
                "Startup relaxation must be explicitly requested.");
            RequireInvalidTimedSeries(() => new DaqVoltageSampleSeries(new[] { 0.01, 0.01, 0.01 },
                new[] { 0.0, 0.1, 0.183 }).ValidateInterval(100, true),
                "An 83ms gap after startup must still be rejected.");
            RequireInvalidTimedSeries(() => new DaqVoltageSampleSeries(new[] { 0.01, 0.01 },
                new[] { 0.0, 0.05 }).ValidateInterval(100, true),
                "Startup validation must reject gaps outside its fixed bounds.");
            RequireInvalidTimedSeries(() => new DaqVoltageSampleSeries(Enumerable.Repeat(0.01, 120).ToArray(),
                Enumerable.Range(0, 120).Select(i => i * 0.11).ToArray()).ValidateInterval(100, true),
                "Small per-sample drift must not extend the whole window.");
            foreach (string test in new[] { "T02", "T05" })
            {
                string raw = File.ReadAllText(Path.Combine(repositoryRoot, "Tests", "Bcm125StructureTests",
                    "Fixtures", test + "_144351_timed.csv"));
                var replay = (DaqVoltageSampleSeries)parser.Invoke(null, new object[] { raw.Trim(), 100 });
                replay.ValidateInterval(100, true);
                double expected = test == "T02" ? 696.151087 : 686.698310;
                Require(Math.Abs(replay.Voltages.Average() / 0.0205 * 1000 - expected) < 0.001,
                    "Log replay changed the signed whole-window current for " + test + ".");
                Require(replay.Voltages.Count(v => v / 0.0205 * 1000 < 300) == (test == "T02" ? 19 : 20),
                    "Log replay discarded a periodic low-current interval for " + test + ".");
                string latestRaw = File.ReadAllText(Path.Combine(repositoryRoot, "Tests", "Bcm125StructureTests",
                    "Fixtures", test + "_151026_timed.csv"));
                var latest = (DaqVoltageSampleSeries)parser.Invoke(null, new object[] { latestRaw.Trim(), 120 });
                var windowMeans = new List<double>();
                for (int start = 0; start <= 60; start++)
                {
                    var shorter = new DaqVoltageSampleSeries(latest.Voltages.Skip(start).Take(60).ToArray(),
                        latest.RelativeTimesSeconds.Skip(start).Take(60).ToArray());
                    shorter.ValidateInterval(100, true);
                    Require(shorter.Voltages.Count(v => v / 0.0205 * 1000 < 300) == 10,
                        "Every replayed 60-point window must retain all ten low-current readings.");
                    windowMeans.Add(shorter.Voltages.Average() / 0.0205 * 1000);
                }
                Require(windowMeans.All(mean => mean >= 300 && mean <= 1000) &&
                        windowMeans.Max() - windowMeans.Min() < 0.4,
                    "Shorter actual windows changed the original PASS result or introduced phase-sensitive means.");
            }

            MethodInfo offResult = FindMethod(sequence.GetType(), "CreateStringTestMosOffResult");
            object offsetResult = offResult.Invoke(null, new object[] {
                Enumerable.Repeat(50.0, 20).ToArray(), 20, "T06", -20.0, 20.0, "mA" });
            Require(!Read<bool>(offsetResult.GetType(), offsetResult, "IsPass") &&
                    Read<double?>(offsetResult.GetType(), offsetResult, "NumericValue") == 50.0,
                "Persistent +50mA must remain NG, with no offset subtraction.");
            var offValues = Enumerable.Repeat(0.0, 20).ToArray();
            offValues[0] = 50;
            object transientResult = offResult.Invoke(null, new object[] {
                offValues, 20, "T06", -20.0, 20.0, "mA" });
            Require(Read<double?>(transientResult.GetType(), transientResult, "NumericValue") == 2.5,
                "All MOS-off values, including a transient, must contribute to the mean.");
            RequireInvalidTimedSeries(() => offResult.Invoke(null, new object[] {
                Enumerable.Repeat(0.0, 19).ToArray(), 20, "T06", -20.0, 20.0, "mA" }),
                "A partial R44 window must never produce a passing mean.");
            RequireInvalidTimedSeries(() => offResult.Invoke(null, new object[] {
                new[] { 0.0, double.NaN }, 2, "T06", -20.0, 20.0, "mA" }),
                "A non-finite R44 value must invalidate the window.");
            var disconnected = new SK441Device { SkipComInit = false };
            RequireInvalidTimedSeries(() => disconnected.QueryMbdCommandOnceOnCurrentConnectionAsync(
                "R44", 200, CancellationToken.None).GetAwaiter().GetResult(),
                "A fixed-window query must fail on a disconnected port instead of enumerating/reconnecting.");
            RequireInvalidTimedSeries(() => _simulationDevice.QueryMbdCommandOnceOnCurrentConnectionAsync(
                "R44", 0, CancellationToken.None).GetAwaiter().GetResult(),
                "A fixed-window query must reject a nonpositive timeout.");

            // One fixed 6s cycle retains its near-zero portion at every phase.
            for (int phase = 0; phase < 60; phase++)
            {
                double[] periodic = Enumerable.Range(0, 60)
                    .Select(i => (i + phase) % 60 < 50 ? 858.0 : 0.0).ToArray();
                Require(Math.Abs(periodic.Average() - 715.0) < 0.000001,
                    "A one-cycle fixed window changed its average with starting phase.");
            }

            var negative = (DaqVoltageSampleSeries)parser.Invoke(null,
                new object[] { "-0.0117,0,-0.0117,0.05", 2 });
            Require(negative.Voltages.All(value => value < 0), "Timed shunt capture must preserve polarity.");
            FieldInfo mos = typeof(SK441Device).GetField("_mockMosTestOn", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo ccb = typeof(SK441Device).GetField("_mockCcbEnabled", BindingFlags.Instance | BindingFlags.NonPublic);
            object originalMos = mos.GetValue(_simulationDevice);
            object originalCcb = ccb.GetValue(_simulationDevice);
            FieldInfo stringCurrent = typeof(SK441Device).GetField("_mockStringCurrent", BindingFlags.Instance | BindingFlags.NonPublic);
            object originalStringCurrent = stringCurrent.GetValue(_simulationDevice);
            var resultLogs = new List<string>();
            Action<string> originalDeviceLog = _simulationDevice.LogInfo;
            _simulationSequence.LogInfo += resultLogs.Add;
            _simulationSequence.LogWarning += resultLogs.Add;
            _simulationDevice.LogInfo = resultLogs.Add;
            try
            {
                mos.SetValue(_simulationDevice, true);
                ccb.SetValue(_simulationDevice, false);
                DaqVoltageSampleSeries configuredSeries = _simulationDevice
                    .MeasureBcm125StringTestShuntSamplesAsync(113, CancellationToken.None).GetAwaiter().GetResult();
                configuredSeries.ValidateInterval(100);
                Require(configuredSeries.Voltages.Length == 60 &&
                        Math.Abs(configuredSeries.RelativeTimesSeconds.Last() - 5.9) < 0.000001,
                    "Updated T02/T05 configuration must capture 60 points at 100ms, spanning 5.9s.");
                Require(_simulationDevice.Bcm125MosOffSampleCount == 5 &&
                        _simulationDevice.Bcm125MosOffIntervalMs == 200,
                    "Updated T03/T06 configuration must use five readings at 200ms.");
                ValidateStringTestMosOnWindowPairs(steps, resultLogs);
                mos.SetValue(_simulationDevice, false);
                int offRowCount = 0;
                foreach (object step in steps)
                {
                    string stepId = Read<string>(step.GetType(), step, "StepId");
                    if (!stepId.StartsWith("BCM125.CH26.") ||
                        !(stepId.EndsWith(".T03") || stepId.EndsWith(".T06"))) continue;
                    offRowCount++;
                    Require(Read<int>(step.GetType(), step, "MaxRetries") == 0,
                        "MOS-off measurements must not retry until a window passes.");
                    resultLogs.Clear();
                    object result = InvokeStep(step.GetType(), step);
                    Require(Read<bool>(result.GetType(), result, "IsPass") &&
                            Read<double?>(result.GetType(), result, "NumericValue") == 0.0,
                        "Simulated MOS-off result must use R44's raw fixed-window mean.");
                    Require(resultLogs.Any(log => log.Contains("R44窗口：5点")) &&
                            !resultLogs.Any(log => log.Contains("诊断") || log.Contains("R44原始全样本") || log.Contains("DAQ")),
                        "Passing OFF rows must not acquire/log additional DAQ or register diagnostics.");

                    ccb.SetValue(_simulationDevice, true);
                    stringCurrent.SetValue(_simulationDevice, 6.0);
                    resultLogs.Clear();
                    object failedResult = InvokeStep(step.GetType(), step);
                    Require(!Read<bool>(failedResult.GetType(), failedResult, "IsPass") &&
                            Read<double?>(failedResult.GetType(), failedResult, "NumericValue") == 6000.0,
                        "OFF diagnostics must not replace the original 6000mA NG result.");
                    Require(resultLogs.Count(log => log.Contains("关断NG追加一次诊断")) == 1,
                        "OFF NG must emit exactly one diagnostic window.");
                    Require(resultLogs.Any(log => log.Contains("R44原始全样本")),
                        "OFF NG must retain every original R44 reading.");
                    Require(resultLogs.Any(log => log.Contains("外部分流原始诊断")),
                        "OFF NG must retain its additional diagnostic DAQ samples.");
                    Require(resultLogs.Any(log => log.Contains("诊断 R24=") || log.Contains("诊断R24读取失败")) &&
                            resultLogs.Any(log => log.Contains("诊断 R56=")),
                        "OFF NG must include the cross-compensation register diagnostic.");
                    ccb.SetValue(_simulationDevice, false);
                    stringCurrent.SetValue(_simulationDevice, originalStringCurrent);
                    var canceled = new CancellationToken(true);
                    Delegate action = Read<Delegate>(step.GetType(), step, "Action");
                    bool cancellationObserved = false;
                    try { ((Task)action.DynamicInvoke(canceled)).GetAwaiter().GetResult(); }
                    catch (Exception ex) { if (Unwrap(ex) is OperationCanceledException) cancellationObserved = true; else throw; }
                    Require(cancellationObserved, "Canceled MOS-off test must not produce a successful result.");
                }
                Require(offRowCount == 2, "Both T03 and T06 must be exercised by MOS-off regression checks.");
            }
            finally
            {
                mos.SetValue(_simulationDevice, originalMos);
                ccb.SetValue(_simulationDevice, originalCcb);
                stringCurrent.SetValue(_simulationDevice, originalStringCurrent);
                _simulationSequence.LogInfo -= resultLogs.Add;
                _simulationSequence.LogWarning -= resultLogs.Add;
                _simulationDevice.LogInfo = originalDeviceLog;
            }
            Console.WriteLine("PASS String sampling: ON 30 R44/60 DAQ points in one window, independent results and single-use cache; five OFF readings, retained dips/polarity, concise PASS/associated NG logs, incomplete/canceled windows rejected.");
        }

        private static void ValidateStringTestMosOnWindowPairs(IList steps, List<string> logs)
        {
            Require(_simulationDevice.Bcm125MosOnSampleCount == 30 && _simulationDevice.Bcm125MosOnIntervalMs == 200,
                "Internal MOS-on current must use 30 readings at 200ms.");
            MethodInfo settings = FindMethod(typeof(SKBCM125Sequence), "ValidateStringTestMosOnWindowSettings");
            settings.Invoke(null, new object[] { 30, 200, 60, 100 });
            RequireInvalidTimedSeries(() => settings.Invoke(null, new object[] { 30, 200, 120, 100 }),
                "Internal and external windows must reject mismatched durations before hardware actions.");
            MethodInfo resultMethod = FindMethod(typeof(SKBCM125Sequence), "CreateStringTestMosOnResult");
            for (int phase = 0; phase < 30; phase++)
            {
                double[] values = Enumerable.Range(0, 30)
                    .Select(i => (i + phase) % 30 < 25 ? -804.0 : 49.0).ToArray();
                object result = resultMethod.Invoke(null, new object[] { values, 30, "T04", -1000.0, -300.0, "mA" });
                Require(Read<bool>(result.GetType(), result, "IsPass") &&
                        Math.Abs(Read<double?>(result.GetType(), result, "NumericValue").Value + 661.833333333) < 0.000001,
                    "Internal whole-window average must retain every low-current portion at every phase.");
            }
            object off = resultMethod.Invoke(null, new object[] { Enumerable.Repeat(49.0, 30).ToArray(),
                30, "T04", -1000.0, -300.0, "mA" });
            Require(!Read<bool>(off.GetType(), off, "IsPass"), "A MOS that remains off must still fail the ON result.");
            RequireInvalidTimedSeries(() => resultMethod.Invoke(null, new object[] {
                Enumerable.Repeat(-650.0, 29).ToArray(), 30, "T04", -1000.0, -300.0, "mA" }),
                "Incomplete internal windows must not pass.");
            RequireInvalidTimedSeries(() => resultMethod.Invoke(null, new object[] {
                new[] { -650.0, double.NaN }, 2, "T04", -1000.0, -300.0, "mA" }),
                "Non-finite internal readings must not pass.");

            FieldInfo terminal = typeof(SKBCM125Sequence).GetField("_mbdTerminalState", BindingFlags.Instance | BindingFlags.NonPublic);
            object originalTerminal = terminal.GetValue(_simulationSequence);
            try
            {
                foreach (string[] pair in new[] {
                    new[] { "BCM125.CH26.S025.T01", "BCM125.CH26.S026.T02", "REMOTE1", "113" },
                    new[] { "BCM125.CH26.S059.T04", "BCM125.CH26.S060.T05", "REMOTE2", "115" } })
                {
                    object internalStep = FindStep(steps, pair[0]);
                    object externalStep = FindStep(steps, pair[1]);
                    Require(Read<int>(internalStep.GetType(), internalStep, "MaxRetries") == 0 &&
                            Read<int>(externalStep.GetType(), externalStep, "MaxRetries") == 0,
                        "Both shared-window results must prohibit retries that select passing windows.");
                    RequireInvalidTimedSeries(() => InvokeStep(externalStep.GetType(), externalStep),
                        "External rows must not acquire a new window when the internal row was never executed.");
                    Require(InvokePrivateBoolTask(FindMethod(typeof(SKBCM125Sequence), "SendBcm125MbdCommandAsync"),
                        _simulationSequence, _simulationDevice, pair[2], 0), "Cannot select the shared-window test terminal.");
                    logs.Clear();
                    object internalResult = InvokeStep(internalStep.GetType(), internalStep);
                    Require(Read<bool>(internalResult.GetType(), internalResult, "IsPass") &&
                            Read<double?>(internalResult.GetType(), internalResult, "NumericValue") == -650.0 &&
                            logs.Any(log => log.Contains("R44=30点")),
                        "Shared internal rows must report all 30 signed R44 readings.");
                    int acquisitionCount = logs.Count(log => log.Contains("模拟 DAQ 通道 " + pair[3]));
                    Require(acquisitionCount == 1, "The paired window must start exactly one DAQ acquisition.");
                    object externalResult = InvokeStep(externalStep.GetType(), externalStep);
                    Require(Read<bool>(externalResult.GetType(), externalResult, "IsPass") &&
                            Math.Abs(Read<double?>(externalResult.GetType(), externalResult, "NumericValue").Value - 650.0) < 0.001 &&
                            logs.Count(log => log.Contains("模拟 DAQ 通道 " + pair[3])) == acquisitionCount &&
                            logs.Any(log => log.Contains("无新增采集等待")) &&
                            !logs.Any(log => log.Contains("原始全样本") || log.Contains("序号:t_ms") || log.Contains("双向分流回读")),
                        "External rows must reuse the same window once and retain concise passing logs.");
                    RequireInvalidTimedSeries(() => InvokeStep(externalStep.GetType(), externalStep),
                        "A second external-row execution must not reuse consumed data.");

                    // A failed internal query must not discard an independently valid DAQ series.
                    terminal.SetValue(_simulationSequence, Enum.Parse(terminal.FieldType, "Unknown"));
                    logs.Clear();
                    RequireInvalidTimedSeries(() => InvokeStep(internalStep.GetType(), internalStep),
                        "An unconfirmed terminal must invalidate the internal result.");
                    object independentExternal = InvokeStep(externalStep.GetType(), externalStep);
                    Require(Read<bool>(independentExternal.GetType(), independentExternal, "IsPass"),
                        "Internal communication failure must not overwrite a valid external window.");
                    Require(logs.Any(log => log.Contains("内部NG关联外部分流原始")),
                        "Internal NG diagnostics must include the captured external timeline without a new scan.");
                    terminal.SetValue(_simulationSequence, Enum.Parse(terminal.FieldType,
                        pair[2] == "REMOTE1" ? "Bcm1" : "Bcm2"));

                    // Cancellation while the simulated DAQ read is pending must finish
                    // that task, invalidate the cache, and leave no result for the next row.
                    Delegate action = Read<Delegate>(internalStep.GetType(), internalStep, "Action");
                    using (var cancellation = new CancellationTokenSource())
                    {
                        Task pending = (Task)action.DynamicInvoke(cancellation.Token);
                        cancellation.Cancel();
                        bool observed = false;
                        try { pending.GetAwaiter().GetResult(); }
                        catch (Exception ex) { if (Unwrap(ex) is OperationCanceledException) observed = true; else throw; }
                        Require(observed && pending.IsCompleted, "Canceled joint acquisition must drain before returning.");
                    }
                    RequireInvalidTimedSeries(() => InvokeStep(externalStep.GetType(), externalStep),
                        "Canceled internal capture must not leave reusable DAQ cache.");
                    InvokeStep(internalStep.GetType(), internalStep);
                    Delegate externalAction = Read<Delegate>(externalStep.GetType(), externalStep, "Action");
                    bool externalCanceled = false;
                    try { ((Task)externalAction.DynamicInvoke(new CancellationToken(true))).GetAwaiter().GetResult(); }
                    catch (Exception ex) { if (Unwrap(ex) is OperationCanceledException) externalCanceled = true; else throw; }
                    Require(externalCanceled, "Canceled external result must not pass.");
                    RequireInvalidTimedSeries(() => InvokeStep(externalStep.GetType(), externalStep),
                        "Canceled external result must consume/invalidate its cache.");
                }
            }
            finally { terminal.SetValue(_simulationSequence, originalTerminal); }

            Type windowType = typeof(SKBCM125Sequence).GetNestedType("StringTestOnWindow", BindingFlags.NonPublic);
            object failedWindow = Activator.CreateInstance(windowType, true);
            windowType.GetField("ExternalError").SetValue(failedWindow, new InvalidOperationException("Injected DAQ failure"));
            MethodInfo consume = FindMethod(typeof(SKBCM125Sequence), "ConsumeStringTestOnExternalSamples");
            RequireInvalidTimedSeries(() => consume.Invoke(_simulationSequence, new[] {
                failedWindow, (object)"T05", CancellationToken.None }),
                "Failed external windows must not fall back to a new acquisition.");
        }

        private static void RequireInvalidTimedSeries(Action action, string message)
        {
            try { action(); }
            catch (Exception ex)
            {
                Exception error = Unwrap(ex);
                if (error is InvalidOperationException || error is FormatException || error is ArgumentException)
                    return;
                throw;
            }
            throw new InvalidOperationException(message);
        }

        private static void ValidateCalibrationWriteGuards(IList steps)
        {
            const string gainKey = "BCM125.MIDPOINT.ST1.M05_GAIN_VMID";
            _simulationContext.StoreValue(
                "BCM125.MIDPOINT.ST1.M02_VMID_62V5_MV", 62000, "62000", "mV", true);
            _simulationContext.StoreValue(
                "BCM125.MIDPOINT.ST1.M04_VMID_5V_MV", 5000, "5000", "mV", true);
            _simulationContext.StoreValue(
                "BCM125.MIDPOINT.ST1.M01_ADC2_VMID_62V5", 1800, "1800", "count", false);
            _simulationContext.StoreValue(
                "BCM125.MIDPOINT.ST1.M03_ADC2_VMID_5V", 160, "160", "count", true);
            object gain = FindStep(steps, "BCM125.CH19.S026.M05");
            object gainResult = InvokeStep(gain.GetType(), gain);
            Require(!Read<bool>(gainResult.GetType(), gainResult, "IsPass") &&
                    Read<double?>(gainResult.GetType(), gainResult, "NumericValue") >= 140000,
                "BCM125 W20 was accepted despite a failed source measurement.");

            _simulationContext.StoreValue(gainKey, 131079, "131079", "count", false);
            object offset = FindStep(steps, "BCM125.CH19.S027.M06");
            object offsetResult = InvokeStep(offset.GetType(), offset);
            Require(!Read<bool>(offsetResult.GetType(), offsetResult, "IsPass"),
                "BCM125 W21 was accepted after failed VMID gain calibration.");

            _simulationContext.StoreValue(
                "BCM125.MIDPOINT.ST1.M06_OFFSET_VMID", -61, "-61", "mV", false);
            object midpointFlash = FindStep(steps, "BCM125.CH19.S028.ACTION");
            object flashResult = InvokeStep(midpointFlash.GetType(), midpointFlash);
            Require(!Read<bool>(flashResult.GetType(), flashResult, "IsPass") &&
                    Read<string>(flashResult.GetType(), flashResult, "Value") == "Source Failed",
                "BCM125 midpoint FLASH ran after failed calibration.");

            _simulationContext.StoreValue(
                "BCM125.DISCHARGE.ST1.N05_GAIN_IDCH.WRITE_OK", 0, "Source Failed", null, false);
            _simulationContext.StoreValue(
                "BCM125.DISCHARGE.ST1.N06_OFFSET_IDCH.WRITE_OK", 0, "Source Failed", null, false);
            object dischargeFlash = FindStep(steps, "BCM125.CH20.S027.ACTION");
            object dischargeResult = InvokeStep(dischargeFlash.GetType(), dischargeFlash);
            Require(!Read<bool>(dischargeResult.GetType(), dischargeResult, "IsPass") &&
                    Read<string>(dischargeResult.GetType(), dischargeResult, "Value") == "Source Failed",
                "BCM125 discharge FLASH ran after failed calibration writes.");
        }

        private static void ValidateFailedNumericCalibrationWrites(IList steps)
        {
            const string dischargeGain = "BCM125.DISCHARGE.ST1.N05_GAIN_IDCH";
            const string dischargeOffset = "BCM125.DISCHARGE.ST1.N06_OFFSET_IDCH";
            object selectRemote = FindStep(steps, "BCM125.CH20.S024.ACTION");
            object selectResult = InvokeStep(selectRemote.GetType(), selectRemote);
            Require(Read<bool>(selectResult.GetType(), selectResult, "IsPass"),
                "Cannot select simulated BCM1 terminal for calibration write validation.");

            _simulationContext.StoreValue(dischargeGain, 0, "Missing Input", "count", false, false);
            object writeGain = FindStep(steps, "BCM125.CH20.S025.ACTION");
            object missingWrite = InvokeStep(writeGain.GetType(), writeGain);
            Require(!Read<bool>(missingWrite.GetType(), missingWrite, "IsPass") &&
                    Read<string>(missingWrite.GetType(), missingWrite, "Value") == "Source Missing",
                "BCM125 wrote a calibration whose calculation had no numeric result.");

            _simulationContext.StoreValue(dischargeGain, 84729, "G=84729;O=1", "count", false);
            _simulationContext.StoreValue(dischargeOffset, 1, "1", "mA", true);
            object failedNumericWrite = InvokeStep(writeGain.GetType(), writeGain);
            Require(Read<bool>(failedNumericWrite.GetType(), failedNumericWrite, "IsPass") &&
                    Read<string>(failedNumericWrite.GetType(), failedNumericWrite, "Value") == "84729",
                "BCM125 rejected an out-of-limit but valid W26 calibration value.");
            _simulationContext.StoreValue(dischargeGain + ".WRITE_OK", 1, "84729", null, true);
            object writeOffset = FindStep(steps, "BCM125.CH20.S026.ACTION");
            object offsetWriteResult = InvokeStep(writeOffset.GetType(), writeOffset);
            Require(Read<bool>(offsetWriteResult.GetType(), offsetWriteResult, "IsPass"),
                "BCM125 failed to write W27 after a valid out-of-limit W26 value.");
            _simulationContext.StoreValue(dischargeOffset + ".WRITE_OK", 1, "1", null, true);
            object dischargeFlash = FindStep(steps, "BCM125.CH20.S027.ACTION");
            object dischargeFlashResult = InvokeStep(dischargeFlash.GetType(), dischargeFlash);
            Require(Read<bool>(dischargeFlashResult.GetType(), dischargeFlashResult, "IsPass"),
                "BCM125 skipped discharge FLASH although both values were written.");
            TestSequenceValue preservedGain;
            Require(_simulationContext.TryGetValue(dischargeGain, out preservedGain) && !preservedGain.Pass,
                "Writing an out-of-limit discharge calibration erased its NG result.");

            const string chargingPrefix = "BCM125.CHARGING.ST1.";
            const string chargingGain = chargingPrefix + "Q14_GAIN_ICCB";
            const string chargingCrossGain = chargingPrefix + "Q15_GAIN_ICCB_VSTR_NEG";
            const string chargingOffset = chargingPrefix + "Q16_OFFSET_ICCB";
            _simulationContext.StoreValue(chargingPrefix + "Q01_ADC0_0V_0A", 45, "45", "count", true);
            _simulationContext.StoreValue(chargingPrefix + "Q02_ADC14_0V_0A", 1430, "1430", "count", true);
            _simulationContext.StoreValue(chargingPrefix + "Q05_ADC0_15V_0A", 675, "675", "count", true);
            _simulationContext.StoreValue(chargingPrefix + "Q06_ADC14_15V_0A", 1440, "1440", "count", true);
            object calculateCrossGain = FindStep(steps, "BCM125.CH23.S047.Q15");
            _simulationContext.StoreValue(chargingGain, 0, "Missing Input", "count", false, false);
            object missingCrossGain = InvokeStep(calculateCrossGain.GetType(), calculateCrossGain);
            Require(!Read<double?>(missingCrossGain.GetType(), missingCrossGain, "NumericValue").HasValue,
                "BCM125 synthesized a Q15 number from a missing Q14 calculation.");

            _simulationContext.StoreValue(chargingGain, 16715, "16715", "count", false);
            object crossGainResult = InvokeStep(calculateCrossGain.GetType(), calculateCrossGain);
            double? crossGainValue = Read<double?>(crossGainResult.GetType(), crossGainResult, "NumericValue");
            Require(crossGainValue.HasValue &&
                    Read<string>(crossGainResult.GetType(), crossGainResult, "Value") != "Missing Input",
                "BCM125 Q15 lost an available but out-of-limit Q14 input.");
            _simulationContext.StoreValue(chargingCrossGain, crossGainValue.Value,
                crossGainValue.Value.ToString(CultureInfo.InvariantCulture), "count",
                Read<bool>(crossGainResult.GetType(), crossGainResult, "IsPass"));
            object calculateOffset = FindStep(steps, "BCM125.CH23.S048.Q16");
            object chargingOffsetResult = InvokeStep(calculateOffset.GetType(), calculateOffset);
            double? chargingOffsetValue = Read<double?>(chargingOffsetResult.GetType(), chargingOffsetResult, "NumericValue");
            Require(chargingOffsetValue.HasValue &&
                    Read<string>(chargingOffsetResult.GetType(), chargingOffsetResult, "Value") != "Missing Input",
                "BCM125 Q16 lost an available but out-of-limit Q14 input.");
            _simulationContext.StoreValue(chargingOffset, chargingOffsetValue.Value,
                chargingOffsetValue.Value.ToString(CultureInfo.InvariantCulture), "mA",
                Read<bool>(chargingOffsetResult.GetType(), chargingOffsetResult, "IsPass"));
            _simulationContext.StoreValue(chargingPrefix + "Q12_GAIN_VSTR_NEG", 91098, "91098", "count", true);
            _simulationContext.StoreValue(chargingPrefix + "Q13_OFFSET_VSTR_NEG", 10, "10", "mV", true);

            foreach (string stepId in new[]
            {
                "BCM125.CH23.S049.Q12", "BCM125.CH23.S050.Q13",
                "BCM125.CH23.S051.Q14", "BCM125.CH23.S052.Q15",
                "BCM125.CH23.S053.Q16"
            })
            {
                object write = FindStep(steps, stepId);
                object result = InvokeStep(write.GetType(), write);
                Require(Read<bool>(result.GetType(), result, "IsPass"),
                    "BCM125 rejected an available charging calibration write at " + stepId + ".");
                string storeKey = Read<string>(result.GetType(), result, "StoreKey");
                _simulationContext.StoreValue(storeKey,
                    Read<double?>(result.GetType(), result, "NumericValue").Value,
                    Read<string>(result.GetType(), result, "Value"), null, true);
            }
            object chargingFlash = FindStep(steps, "BCM125.CH23.S054.ACTION");
            object chargingFlashResult = InvokeStep(chargingFlash.GetType(), chargingFlash);
            Require(Read<bool>(chargingFlashResult.GetType(), chargingFlashResult, "IsPass"),
                "BCM125 skipped charging FLASH after all finite values were written.");
            TestSequenceValue preservedChargingGain;
            Require(_simulationContext.TryGetValue(chargingGain, out preservedChargingGain) &&
                    !preservedChargingGain.Pass,
                "Writing an out-of-limit charging calibration erased its NG result.");
        }

        private static void ValidateMidpointToDischargePower(IList steps)
        {
            Require(_simulationDevice.TurnOffAllInstrumentsAsync().GetAwaiter().GetResult(),
                "Cannot reset outputs before BCM125 midpoint boundary validation.");
            Require(_simulationDevice.CloseRl4ThenEnableAnsAsync(7, 140, 1).GetAwaiter().GetResult(),
                "Cannot power DUT before BCM125 midpoint boundary validation.");

            int traceStart = _simulationDevice.GetPowerSafetyTrace().Length;
            object resetLoad = FindStep(steps, "BCM125.CH19.S074.ACTION");
            object resetResult = InvokeStep(resetLoad.GetType(), resetLoad);
            Require(Read<bool>(resetResult.GetType(), resetResult, "IsPass") &&
                    !_simulationDevice.GetPowerSafetyTrace().Skip(traceStart)
                        .Contains("OUTPUT ANS OFF"),
                "BCM125 midpoint load reset unexpectedly turned off DUT power.");

            Require(_simulationDevice.TurnOffAllInstrumentsAsync().GetAwaiter().GetResult(),
                "Cannot simulate lost DUT power before discharge.");
            traceStart = _simulationDevice.GetPowerSafetyTrace().Length;
            object dischargePower = FindStep(steps, "BCM125.CH20.S001.ACTION");
            object powerResult = InvokeStep(dischargePower.GetType(), dischargePower);
            Require(Read<bool>(powerResult.GetType(), powerResult, "IsPass") &&
                    _simulationDevice.GetPowerSafetyTrace().Skip(traceStart)
                        .Contains("OUTPUT ANS ON"),
                "BCM125 discharge setup did not restore ANS power.");
        }

        private static void ValidatePowerResultFailureDoesNotBypassPolicy(IList steps)
        {
            Require(
                _simulationDevice.TurnOffAllInstrumentsAsync().GetAwaiter().GetResult(),
                "Cannot reset BCM125 simulation power before failure-policy validation.");

            object powerOnStep = FindStep(steps, "BCM125.CH08.S002.ACTION");
            object powerOnResult = InvokeStep(powerOnStep.GetType(), powerOnStep);
            Require(
                Read<bool>(powerOnResult.GetType(), powerOnResult, "IsPass"),
                "BCM125 CH8 power-on/stability step failed in simulation.");

            object resultStep = FindStep(steps, "BCM125.CH08.S003.B01");
            Type resultType = resultStep.GetType();
            TestPlanStepDefinition definition =
                Read<TestPlanStepDefinition>(resultType, resultStep, "PlanDefinition");
            double? originalLower = definition.LowerLimit;
            double? originalUpper = definition.UpperLimit;
            definition.LowerLimit = 100000;
            definition.UpperLimit = 100001;
            int traceCountBefore = _simulationDevice.GetPowerSafetyTrace().Length;

            object failedResult = InvokeStep(resultType, resultStep);
            Require(
                !Read<bool>(failedResult.GetType(), failedResult, "IsPass"),
                "BCM125 CH8 forced result failure unexpectedly passed.");
            string[] traceAfter = _simulationDevice.GetPowerSafetyTrace();
            Require(
                !traceAfter.Skip(traceCountBefore).Any(x =>
                    string.Equals(x, "RELAY 7 OPEN", StringComparison.Ordinal)),
                "BCM125 result step bypassed the common failure policy and opened RL4.");

            definition.LowerLimit = originalLower;
            definition.UpperLimit = originalUpper;
            Require(
                _simulationDevice.DisableAnsThenOpenRl4Async(7).GetAwaiter().GetResult(),
                "Cannot restore BCM125 simulation power after failure-policy validation.");
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

        private static void ValidateEndToEndSimulation(string xmlPath)
        {
            string oldMode = Environment.GetEnvironmentVariable("TESTPLATFORM_SK_MODE");
            Environment.SetEnvironmentVariable("TESTPLATFORM_SK_MODE", "Simulation");
            try
            {
                foreach (bool injectFailure in new[] { false, true })
                {
                    var plan = TestPlanService.Resolve(xmlPath, TestProfileIds.Normal);
                    // Accelerate idle waits in this in-memory test plan only.
                    foreach (var definition in plan.Definition.Steps)
                        if (definition.DelayMs.HasValue) definition.DelayMs = 0;
                    const string failureId = "BCM125.CH08.S003.B01";
                    if (injectFailure)
                    {
                        var failedDefinition = plan.Definition.Steps.Single(step => step.StepId == failureId);
                        failedDefinition.LowerLimit = 100000;
                        failedDefinition.UpperLimit = 100001;
                    }
                    else
                    {
                        var observation = plan.Definition.Steps.Single(step => step.StepId == "BCM125.CH18.S008.R68");
                        observation.LowerLimit = 100000;
                        observation.UpperLimit = 100001;
                    }
                    var grid = new SimulationGrid();
                    var context = new TestSequenceContext
                    {
                        SN = injectFailure ? "BCM125-E2E-FAULT" : "BCM125-E2E-AUTO",
                        TestPlan = plan,
                        StopOnFail = true
                    };
                    var errors = new List<string>();
                    var sequence = new SKBCM125Sequence(grid);
                    sequence.LogError += message => errors.Add(message);
                    bool passed = sequence.RunAsync(context).GetAwaiter().GetResult();
                    Require(passed == !injectFailure,
                        "BCM125 full simulation/failure propagation incorrect: " + string.Join(" | ", errors));
                    Require(grid.CleanupCompleted == 5,
                        "BCM125 full simulation did not complete every cleanup step.");
                    if (injectFailure)
                        Require(grid.FailedStepIds.Contains(failureId), "Injected B01 failure was not reported.");
                    else
                    {
                        Require(grid.Values["BCM125.CH09.S013.C01"].StartsWith("EXISTING", StringComparison.Ordinal) &&
                            grid.Values["BCM125.CH09.S024.C02"].StartsWith("EXISTING", StringComparison.Ordinal),
                            "Full auto run still programmed known-good firmware.");
                        Require(grid.Values.ContainsKey("BCM125.CH27.S023.ACTION"),
                            "BCM125 full simulation did not reach its final functional chapter.");
                        Require(grid.WarningStepIds.Contains("BCM125.CH18.S008.R68"),
                            "Auxiliary warning did not propagate to UI while preserving overall PASS.");
                    }
                }
                Console.WriteLine("PASS BCM125 full auto simulation and B01 fault propagation with all five cleanup steps.");
            }
            finally { Environment.SetEnvironmentVariable("TESTPLATFORM_SK_MODE", oldMode); }
        }

        private sealed class SimulationGrid : ITestGridService
        {
            public int RowCount => 1000;
            public int CleanupCompleted { get; private set; }
            public Dictionary<string, string> Values { get; } = new Dictionary<string, string>();
            public HashSet<string> FailedStepIds { get; } = new HashSet<string>();
            public HashSet<string> WarningStepIds { get; } = new HashSet<string>();
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
            public Task SetOutcomeByStepIdAsync(int channelIndex, string stepId, string value,
                StepExecutionStatus status, long elapsedMs) => SetValueAndStatusByStepIdAsync(channelIndex, stepId, value, status);
            public Task SetValueAndStatusByStepIdAsync(int channelIndex, string stepId, string value, StepExecutionStatus status)
            {
                Values[stepId] = value ?? string.Empty;
                if (status == StepExecutionStatus.Failed) FailedStepIds.Add(stepId);
                if (status == StepExecutionStatus.Warning) WarningStepIds.Add(stepId);
                if (stepId.StartsWith(Bcm125GroupIds.Cleanup + ".", StringComparison.Ordinal) &&
                    (status == StepExecutionStatus.Passed || status == StepExecutionStatus.PassedAfterRetry))
                    CleanupCompleted++;
                return Task.CompletedTask;
            }
            public Task<(bool IsValid, double Lower, double Upper, string ErrorMessage)> GetLimitsAsync(int rowIndex)
                => Task.FromResult((true, 0.0, 0.0, string.Empty));
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

            var stringSupply = new SK441Device { SkipComInit = true, SimulationBoardType = "BCM-125" };
            Require(!stringSupply.SetStringGeneratorAsync(5, 0.3f, 10, true).GetAwaiter().GetResult(),
                "BCM125 String supply energized before Y1/Y4/Y6 were closed.");
            Require(stringSupply.SetBcm125PowerPathRelaysAsync(
                    Bcm125StationMap.GetStringGeneratorPowerRelayIds(),
                    true,
                    "String supply safety path").GetAwaiter().GetResult(),
                "Cannot close BCM125 String supply relays Y1/Y4/Y6.");
            Require(stringSupply.SetStringGeneratorAsync(5, 0.3f, 10, true).GetAwaiter().GetResult(),
                "BCM125 String supply did not energize after Y1/Y4/Y6 were closed.");
            Require(stringSupply.SetStringGeneratorAsync(0, 0, 10, false).GetAwaiter().GetResult(),
                "Cannot turn BCM125 String supply off.");
            Require(stringSupply.SetBcm125PowerPathRelaysAsync(
                    Bcm125StationMap.GetStringGeneratorPowerRelayIds(),
                    false,
                    "String supply safety path").GetAwaiter().GetResult(),
                "Cannot open BCM125 String supply relays Y1/Y4/Y6.");

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

        private static void ValidateStringGeneratorPowerPathSteps(IList steps)
        {
            Require(
                _simulationDevice.TurnOffAllInstrumentsAsync().GetAwaiter().GetResult(),
                "Cannot reset BCM125 outputs before String path validation.");
            int traceStart = _simulationDevice.GetPowerSafetyTrace().Length;

            object closeStep = FindStep(steps, "BCM125.CH21.S008.ACTION");
            object closeResult = InvokeStep(closeStep.GetType(), closeStep);
            Require(Read<bool>(closeResult.GetType(), closeResult, "IsPass"),
                "BCM125 O01 setup failed to close its String path.");

            string[] closeTrace = _simulationDevice.GetPowerSafetyTrace().Skip(traceStart).ToArray();
            Require(closeTrace.Contains("RELAY 1 CLOSED") &&
                    closeTrace.Contains("RELAY 4 CLOSED") &&
                    closeTrace.Contains("RELAY 6 CLOSED") &&
                    closeTrace.Contains("RELAY 9 CLOSED") &&
                    closeTrace.Contains("RELAY 13 CLOSED") &&
                    closeTrace.Contains("RELAY 10 CLOSED"),
                "BCM125 O01 setup did not close Y1/Y4/Y6 + RL1a_B/RL1b_B/RL2A.");
            Require(!closeTrace.Contains("RELAY 8 CLOSED") &&
                    !closeTrace.Contains("RELAY 11 CLOSED") &&
                    !closeTrace.Contains("RELAY 16 CLOSED"),
                "BCM125 O01 setup closed a relay from the old incorrect mapping.");

            object openStep = FindStep(steps, "BCM125.CH21.S020.ACTION");
            object openResult = InvokeStep(openStep.GetType(), openStep);
            Require(Read<bool>(openResult.GetType(), openResult, "IsPass"),
                "BCM125 O01 teardown failed to open its String path.");

            traceStart = _simulationDevice.GetPowerSafetyTrace().Length;
            object closeSt2Step = FindStep(steps, "BCM125.CH21.S022.ACTION");
            object closeSt2Result = InvokeStep(closeSt2Step.GetType(), closeSt2Step);
            Require(Read<bool>(closeSt2Result.GetType(), closeSt2Result, "IsPass"),
                "BCM125 O02 setup failed to close its String path.");

            string[] closeSt2Trace = _simulationDevice.GetPowerSafetyTrace().Skip(traceStart).ToArray();
            Require(closeSt2Trace.Contains("RELAY 1 CLOSED") &&
                    closeSt2Trace.Contains("RELAY 4 CLOSED") &&
                    closeSt2Trace.Contains("RELAY 6 CLOSED") &&
                    closeSt2Trace.Contains("RELAY 9 CLOSED") &&
                    closeSt2Trace.Contains("RELAY 14 CLOSED") &&
                    closeSt2Trace.Contains("RELAY 10 CLOSED"),
                "BCM125 O02 setup did not close Y1/Y4/Y6 + RL1a_B/RL9_B/RL2A.");
            Require(!closeSt2Trace.Contains("RELAY 8 CLOSED") &&
                    !closeSt2Trace.Contains("RELAY 16 CLOSED"),
                "BCM125 O02 setup closed a relay from the old incorrect mapping.");

            object openSt2Step = FindStep(steps, "BCM125.CH21.S034.ACTION");
            object openSt2Result = InvokeStep(openSt2Step.GetType(), openSt2Step);
            Require(Read<bool>(openSt2Result.GetType(), openSt2Result, "IsPass"),
                "BCM125 O02 teardown failed to open its String path.");
        }

        private static void ValidateAllActionSafety(IList steps, IEnumerable<TestPlanStepDefinition> definitions)
        {
            foreach (TestPlanStepDefinition definition in definitions)
                if (definition.DelayMs.HasValue) definition.DelayMs = 0;

            string lastDeviceError = null;
            Action<string> previousLogError = _simulationDevice.LogError;
            Action<string> previousLogInfo = _simulationDevice.LogInfo;
            var actionMessages = new List<string>();
            _simulationDevice.LogError = message => lastDeviceError = message;
            _simulationDevice.LogInfo = message => actionMessages.Add(message);
            try
            {
                foreach (object step in steps)
                {
                    Type type = step.GetType();
                    string stepId = Read<string>(type, step, "StepId");
                    string name = Read<string>(type, step, "Name");
                    if (stepId.StartsWith("BCM125.PRECHECK", StringComparison.OrdinalIgnoreCase) ||
                        !stepId.EndsWith(".ACTION", StringComparison.OrdinalIgnoreCase) ||
                        !Regex.IsMatch(name, "RL4|relay|relays|继电器|generator|power supplies|electronic load|电源|电子负载|outputs|输出|Reset", RegexOptions.IgnoreCase)) continue;

                    if (stepId == "BCM125.CH24.S033.ACTION" || stepId == "BCM125.CH24.S083.ACTION")
                    {
                        string remoteStepId = stepId == "BCM125.CH24.S033.ACTION"
                            ? "BCM125.CH24.S016.ACTION"
                            : "BCM125.CH24.S066.ACTION";
                        object remoteStep = FindStep(steps, remoteStepId);
                        object remoteResult = InvokeStep(remoteStep.GetType(), remoteStep);
                        Require(Read<bool>(remoteResult.GetType(), remoteResult, "IsPass"),
                            "Cannot select BCM125 remote terminal for " + stepId + ".");
                    }

                    int messageStart = actionMessages.Count;
                    int powerTraceStart = _simulationDevice.GetPowerSafetyTrace().Length;
                    object result = InvokeStep(type, step);
                    Require(Read<bool>(result.GetType(), result, "IsPass"),
                        "Unsafe/failed BCM125 action trace at " + stepId + ": " + lastDeviceError +
                        "; recent trace=" + string.Join(" | ", _simulationDevice.GetPowerSafetyTrace()
                            .Skip(Math.Max(0, _simulationDevice.GetPowerSafetyTrace().Length - 12))) + ".");

                    if (stepId == "BCM125.CH07.S003.ACTION")
                    {
                        string[] resetTrace = _simulationDevice.GetPowerSafetyTrace()
                            .Skip(powerTraceStart).ToArray();
                        Require(resetTrace.Contains("RELAY 2 CLOSED") &&
                                resetTrace.Contains("RELAY 5 CLOSED"),
                            "BCM125 CH7 reset left the electronic load path Y2/Y5 open.");
                    }
                    if (stepId == "BCM125.CLEANUP.S003.ACTION")
                    {
                        string[] cleanupTrace = _simulationDevice.GetPowerSafetyTrace()
                            .Skip(powerTraceStart).ToArray();
                        Require(cleanupTrace.Contains("RELAY 2 OPEN") &&
                                cleanupTrace.Contains("RELAY 5 OPEN"),
                            "BCM125 cleanup left the electronic load path Y2/Y5 closed.");
                    }

                    if (stepId == "BCM125.CH24.S033.ACTION" || stepId == "BCM125.CH24.S083.ACTION" ||
                        stepId == "BCM125.CH24.S040.ACTION" || stepId == "BCM125.CH24.S090.ACTION")
                    {
                        List<string> messages = actionMessages.Skip(messageStart).ToList();
                        int stringSet = messages.FindIndex(message => message.Contains("模拟设置 String 发生器"));
                        bool zeroCurrent = stepId == "BCM125.CH24.S033.ACTION" ||
                            stepId == "BCM125.CH24.S083.ACTION";
                        int ccbCommand = messages.FindIndex(message => message.Contains(
                            zeroCurrent ? "发送命令: W69=1" : "发送命令: W69=0"));
                        Require(stringSet >= 0 && ccbCommand >= 0 &&
                                (zeroCurrent ? ccbCommand < stringSet : stringSet < ccbCommand),
                            "BCM125 CCB/String transition order is unsafe at " + stepId + ".");
                    }
                }
                Require(!_simulationDevice.GetPowerSafetyTrace().Any(x => x.StartsWith("BLOCK RELAY", StringComparison.Ordinal)), "BCM125 full action trace attempted energized relay switching.");
            }
            finally
            {
                _simulationDevice.LogError = previousLogError;
                _simulationDevice.LogInfo = previousLogInfo;
            }
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
