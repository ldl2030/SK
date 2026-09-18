using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
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
                ValidateDualFirmwareProgramming(executableDefinitions, repositoryRoot);
                ValidateCustomerReport(repositoryRoot);
                ValidateDependencies(plan.Definition);
                ValidateInstrumentMapping();
                ValidateMbdResponseCleaning();
                ValidateBcm125TerminalStateMachine();
                ValidateAllBcm125MockQueries();
                ValidateExplicitRemoteTransitions(steps);
                ValidateXmlParameterAuthority(steps);
                ValidateResetFloatingPointAndSt2Wait(steps);
                ValidatePowerResultFailureDoesNotBypassPolicy(steps);
                ValidatePowerSafety();
                ValidateStringGeneratorPowerPathSteps(steps);
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
                new object[] { device, command, waitAfterMs });
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
