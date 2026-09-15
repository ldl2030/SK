using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
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
                ValidateIndependentPowerChapters(xmlPath);
                ValidateDigitalInputChannelPolarity();
                ValidateStructure(plan, steps);
                ValidateVariableDependencies();
                ValidateProgrammingSkipOption();
                ValidateProgrammingGuards(root, steps);
                ValidatePowerSafety();
                ValidateCustomerReport(root, plan);
                RunSimulation(context, steps);
                RunFaultInjection(steps, "Q16");
                RunEndToEndSimulation(xmlPath);

                Console.WriteLine(
                    "PASS BCM250 structure/simulation: 598 steps, 91 results, " +
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

        private static void ValidateCustomerReport(string root, ResolvedTestPlan plan)
        {
            string definitionPath = Path.Combine(
                root,
                "ProjectConfig",
                "SK",
                "BCM-250",
                "BCM-250_report.psv");
            string[] definitionLines = File.ReadAllLines(definitionPath)
                .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
                .ToArray();
            Require(
                definitionLines.Length == Bcm125LegacyReportExporter.Bcm250ExpectedReportItemCount,
                "BCM250 customer report definition must contain exactly 91 output rows.");
            Require(
                definitionLines.Select(line => line.Split('|')[0]).Distinct(StringComparer.OrdinalIgnoreCase).Count() ==
                definitionLines.Length,
                "BCM250 customer report codes must be unique.");
            Require(
                definitionLines.Select(line => line.Split('|')[1]).Distinct(StringComparer.OrdinalIgnoreCase).Count() ==
                definitionLines.Length,
                "BCM250 customer report StepIds must be unique.");

            foreach (string line in definitionLines)
            {
                string[] fields = line.Split('|');
                Require(fields.Length == 9, "BCM250 customer report row must contain 9 fields: " + line);
                TestPlanStepDefinition step = plan.GetStep(fields[1]);
                double scale = double.Parse(fields[8], System.Globalization.CultureInfo.InvariantCulture);
                ValidateScaledReportLimit(fields[0], "lower", step.LowerLimit, fields[6], scale);
                ValidateScaledReportLimit(fields[0], "upper", step.UpperLimit, fields[7], scale);
            }

            var table = new DataTable();
            table.Columns.Add("StepId", typeof(string));
            table.Columns.Add("Channel1Value", typeof(string));
            table.Columns.Add("Channel1Result", typeof(string));
            foreach (string line in definitionLines)
            {
                string[] fields = line.Split('|');
                string value = fields[0] == "M02" ? "125000" : "1";
                table.Rows.Add(fields[1], value, "PASS");
            }

            string requestedQaRoot = Environment.GetEnvironmentVariable("TESTPLATFORM_BCM250_REPORT_QA_ROOT");
            bool keepQaReport = !string.IsNullOrWhiteSpace(requestedQaRoot);
            string temporaryRoot = keepQaReport
                ? Path.GetFullPath(requestedQaRoot)
                : Path.Combine(
                    Path.GetTempPath(),
                    "TestPlatform_BCM250_Report_" + Guid.NewGuid().ToString("N"));
            string temporaryConfigDirectory = Path.Combine(
                temporaryRoot,
                "ProjectConfig",
                "SK",
                "BCM-250");
            Directory.CreateDirectory(temporaryConfigDirectory);
            File.Copy(
                definitionPath,
                Path.Combine(temporaryConfigDirectory, "BCM-250_report.psv"));
            File.Copy(
                Path.Combine(
                    root,
                    "ProjectConfig",
                    "SK",
                    "BCM-250",
                    "BCM-250_autoTest.xml"),
                Path.Combine(temporaryConfigDirectory, "BCM-250_autoTest.xml"));

            try
            {
                Bcm125LegacyReportExporter.ExportResult result =
                    Bcm125LegacyReportExporter.SaveAsync(
                        table,
                        0,
                        "2295",
                        "C1110400103",
                        true,
                        "BCM-250",
                        new DateTime(2025, 12, 18, 13, 10, 0),
                        temporaryRoot)
                    .GetAwaiter()
                    .GetResult();

                Require(result.Success, "BCM250 TXT/PDF report generation failed: " + result.ErrorMessage);
                Require(result.DutPassed, "BCM250 all-pass report was not marked PASSED.");
                Require(File.Exists(result.TextFilePath), "BCM250 TXT report was not created.");
                Require(File.Exists(result.PdfFilePath), "BCM250 PDF report was not created.");
                string reportText = File.ReadAllText(result.TextFilePath);
                Require(
                    reportText.StartsWith("BCM_UP250, Codice operatore: 2295, Seriale: C1110400103"),
                    "BCM250 report header does not match the customer format.");
                Require(
                    reportText.Contains("M02") && reportText.Contains("vmid_125V = 125 V"),
                    "BCM250 report did not convert the M02 measurement from mV to V.");
                Require(
                    new FileInfo(result.PdfFilePath).Length > 1000,
                    "BCM250 PDF report is unexpectedly empty.");
            }
            finally
            {
                if (!keepQaReport && Directory.Exists(temporaryRoot))
                    Directory.Delete(temporaryRoot, true);
            }
        }

        private static void ValidateScaledReportLimit(
            string code,
            string limitName,
            double? xmlLimit,
            string reportLimit,
            double scale)
        {
            if (!xmlLimit.HasValue)
            {
                Require(string.IsNullOrWhiteSpace(reportLimit),
                    $"BCM250 report {code} unexpectedly declares a {limitName} limit.");
                return;
            }

            Require(!string.IsNullOrWhiteSpace(reportLimit),
                $"BCM250 report {code} is missing its {limitName} limit.");
            double parsed = double.Parse(
                reportLimit.Replace(',', '.'),
                System.Globalization.CultureInfo.InvariantCulture);
            Require(Math.Abs(parsed - xmlLimit.Value * scale) < 0.0000001,
                $"BCM250 report {code} {limitName} limit does not match the XML plan after scaling.");
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
                SKSerialDeviceDiscovery.IsMbdIdentificationResponse(
                    "cmd-error\rReady\r"),
                "MBD identification must accept a compact command-error variant.");
            Require(
                SKSerialDeviceDiscovery.IsMbdIdentificationResponse(
                    "Invalid command\r000014 chars answered. Ready.\r"),
                "MBD identification must accept an invalid-command variant.");
            Require(
                SKSerialDeviceDiscovery.IsMbdIdentificationResponse(
                    "SYNTAX ERR\rReady.\r"),
                "MBD identification must accept a syntax-error variant.");
            Require(
                !SKSerialDeviceDiscovery.IsMbdIdentificationResponse(
                    "000054 chars answered. Ready.\r"),
                "Ready alone must not identify a port as MBD.");
            Require(
                !SKSerialDeviceDiscovery.IsMbdIdentificationResponse("COMMAND ERROR\r"),
                "COMMAND ERROR alone must not identify a port as MBD.");
            Require(
                !SKSerialDeviceDiscovery.IsMbdIdentificationResponse(
                    "RANGE ERROR\rReady.\r"),
                "A generic device error plus Ready must not identify a port as MBD.");
        }

        private static void ValidateMbdResponseParsing()
        {
            double scalar = MbdResponseParser.ParseScalar(
                "R55",
                "055 = 1549      \r000017 chars answered. Ready.\r");
            Require(Math.Abs(scalar - 1549.0) < 0.000001,
                "MBD scalar parser returned the register number instead of the payload.");

            double readyBeforeRegister = MbdResponseParser.ParseScalar(
                "R68",
                "000014 chars answered. Ready.\r068 = 2797\r");
            Require(Math.Abs(readyBeforeRegister - 2797.0) < 0.000001,
                "MBD parser must accept a CR-delimited register after Ready.");

            double optionalPrefixAndExponent = MbdResponseParser.ParseScalar(
                "R70",
                "Ready.\nR70 = +5.55E+02\0");
            Require(Math.Abs(optionalPrefixAndExponent - 555.0) < 0.000001,
                "MBD parser must accept LF/NUL delimiters, an R prefix and exponent values.");

            double selectedRegister = MbdResponseParser.ParseScalar(
                "R55",
                "051 = 12\r055 = .625\rReady\r");
            Require(Math.Abs(selectedRegister - 0.625) < 0.000001,
                "MBD parser must select the requested register from a multi-register response.");

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

            const string exitAtRootResponse =
                "COMMAND ERROR\r000014 chars answered. Ready.\r";
            Require(
                MbdResponseParser.IsSuccessfulCommandResponse(
                    "EXIT ",
                    exitAtRootResponse,
                    out string exitError) &&
                string.IsNullOrEmpty(exitError),
                "EXIT at the MBD root terminal must accept COMMAND ERROR + Ready as an idempotent success.");
            Require(
                !MbdResponseParser.IsSuccessfulCommandResponse(
                    "DIAG ON",
                    exitAtRootResponse,
                    out string diagError) &&
                diagError == "COMMAND ERROR",
                "COMMAND ERROR must remain a failure for non-EXIT commands.");
            Require(
                MbdResponseParser.IsSuccessfulCommandResponse(
                    "EXIT",
                    "cmd-error\rReady\r",
                    out string compactExitError) &&
                string.IsNullOrEmpty(compactExitError),
                "EXIT must accept the compact command-error terminal variant.");
            Require(
                !MbdResponseParser.IsSuccessfulCommandResponse(
                    "DIAG ON",
                    "unknown cmd\rReady\r",
                    out string unknownCommandError) &&
                unknownCommandError == "UNKNOWN COMMAND",
                "Unknown-command variants must remain command failures.");
            Require(
                !MbdResponseParser.IsSuccessfulCommandResponse(
                    "EXIT",
                    "COMMAND ERROR\r",
                    out string incompleteExitError) &&
                incompleteExitError == "COMMAND ERROR",
                "EXIT COMMAND ERROR without the Ready terminal prompt must remain a failure.");
            Require(
                !MbdResponseParser.IsSuccessfulCommandResponse(
                    "EXIT",
                    "RANGE ERROR\rReady.\r",
                    out string exitRangeError) &&
                exitRangeError == "RANGE ERROR",
                "EXIT must not hide non-idempotent MBD errors.");
            Require(
                !MbdResponseParser.IsSuccessfulCommandResponse(
                    "EXIT",
                    "COMMAND ERROR\rWRITE ERROR\rReady.\r",
                    out string mixedExitError),
                "EXIT must not accept COMMAND ERROR when another MBD error is present.");

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

        private static void ValidateIndependentPowerChapters(string xmlPath)
        {
            TestPlanDefinition definition = TestPlanService.LoadDefinition(xmlPath);
            string[] independentGroups =
            {
                "BCM250.CH23",
                "BCM250.CH24",
                "BCM250.CH25",
                "BCM250.CH26"
            };

            Require(definition.Groups
                    .Where(group => independentGroups.Contains(
                        group.GroupId,
                        StringComparer.OrdinalIgnoreCase))
                    .All(group => group.DependsOn.Count == 0),
                "BCM250 CH23-CH26 must not declare forced group prerequisites.");
            Require(definition.Groups.All(group => !group.DependsOn.Any(dependency =>
                    independentGroups.Contains(dependency, StringComparer.OrdinalIgnoreCase))),
                "No BCM250 chapter may force-enable CH23-CH26 as a group prerequisite.");

            foreach (string selectedGroup in independentGroups)
            {
                var overrides = definition.Steps.ToDictionary(
                    step => step.StepId,
                    step => string.Equals(step.GroupId, selectedGroup, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(step.GroupId, "BCM250.PRECHECK", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(step.GroupId, "BCM250.CLEANUP", StringComparison.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase);
                ResolvedTestPlan plan = TestPlanService.Resolve(
                    xmlPath,
                    TestProfileIds.SingleChapter,
                    overrides);

                Require(definition.Steps
                        .Where(step => independentGroups.Contains(
                            step.GroupId,
                            StringComparer.OrdinalIgnoreCase))
                        .All(step => plan.ShouldRun(step.StepId) == string.Equals(
                            step.GroupId,
                            selectedGroup,
                            StringComparison.OrdinalIgnoreCase)),
                    selectedGroup + " single-chapter selection forced another CH23-CH26 chapter.");
            }
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
            Require(steps.Count == 598, "Expected 598 executable steps, got " + steps.Count + ".");
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
            Require(
                plan.GetStep("BCM250.CH08.S002.ACTION").DelayMs == 2000,
                "BCM250 B01 must be preceded by a 2000 ms power stabilization delay.");
            Require(definitions.Count(x => x.GroupId == Bcm250GroupIds.Chapter(23)) == 61,
                "Chapter 23 must expose all 61 PDF operations.");
            for (int chapter = 7; chapter <= 27; chapter++)
                Require(definitions.Any(x => x.GroupId == Bcm250GroupIds.Chapter(chapter)),
                    "Missing chapter " + chapter + ".");

            ValidateLimit(plan, "O01", 200, 1100, "mA");
            ValidateLimit(plan, "O02", 1, 1, "Logic (0/1)");
            ValidateLimit(plan, "Q16", -6600, -5400, "mA");
            ValidateLimit(plan, "S01", -6300, -5700, "mA");
            ValidateLimit(plan, "S02", 259000, 263000, "mV");
            ValidateLimit(plan, "T02", 300, 750, "mA");
            ValidateLimit(plan, "T03", -20, 20, "mA");

            MethodInfo sampleCountMethod = typeof(SKBCM250Sequence).GetMethod(
                "SampleCountForResult",
                BindingFlags.NonPublic | BindingFlags.Static);
            Require(sampleCountMethod != null, "BCM250 sample-count resolver is missing.");
            Require((int)sampleCountMethod.Invoke(null, new object[] { "T01" }) == 20,
                "BCM250 T01 must average 20 R44 samples.");
            Require((int)sampleCountMethod.Invoke(null, new object[] { "T02" }) == 20,
                "BCM250 T02 must average 20 DAQ113 samples.");

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
            Require(Math.Abs(Bcm250StationMap.VccShuntOhms - 2.0) < 1e-12,
                "BCM250 +VCC shunt must use the station-confirmed 2.0 ohm value.");
            Require(Math.Abs(Bcm250StationMap.FiveVoltShuntOhms - 0.4) < 1e-12,
                "BCM250 +5V shunt must use the station-confirmed 0.4 ohm value.");

            IReadOnlyList<Bcm250ActionDefinition> catalogue = Bcm250ActionCatalog.Create();
            int w101OnIndex = catalogue.ToList().FindIndex(x =>
                x.Chapter == 17 &&
                x.Kind == Bcm250ActionKind.MbdCommand &&
                string.Equals(x.Argument, "W101=1", StringComparison.OrdinalIgnoreCase));
            int w95OnIndex = catalogue.ToList().FindIndex(x =>
                x.Chapter == 17 &&
                x.Kind == Bcm250ActionKind.MbdCommand &&
                string.Equals(x.Argument, "W95=1", StringComparison.OrdinalIgnoreCase));
            Require(
                w101OnIndex >= 1 &&
                w95OnIndex == w101OnIndex + 1 &&
                string.Equals(catalogue[w101OnIndex - 1].Argument, "DIAG ON", StringComparison.OrdinalIgnoreCase),
                "BCM250 CH17 must issue W101=1 (drv_main_sw PE14) between DIAG ON and W95=1.");
            int w95OffIndex = catalogue.ToList().FindIndex(x =>
                x.Chapter == 17 &&
                x.Kind == Bcm250ActionKind.MbdCommand &&
                string.Equals(x.Argument, "W95=0", StringComparison.OrdinalIgnoreCase));
            Require(w95OffIndex >= 1 && w95OffIndex + 3 < catalogue.Count,
                "BCM250 CH17 W95=0 command sequence is incomplete.");
            Require(
                string.Equals(catalogue[w95OffIndex - 1].Argument, "DIAG ON", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(catalogue[w95OffIndex + 1].Argument, "REMOTE2", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(catalogue[w95OffIndex + 2].Argument, "DIAG ON", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(catalogue[w95OffIndex + 3].ResultId, "K02", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(catalogue[w95OffIndex + 3].Argument, "R61", StringComparison.OrdinalIgnoreCase),
                "BCM250 CH17 must execute DIAG ON before W95=0 and again after REMOTE2 before K02/R61.");
            Bcm250ActionDefinition[] chapter16Rl2Commands = catalogue
                .Where(x => x.Chapter == 16 &&
                    x.Kind == Bcm250ActionKind.MbdCommand &&
                    (string.Equals(x.Argument, "W91=1", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(x.Argument, "W91=0", StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            Require(
                chapter16Rl2Commands.Length == 2 &&
                chapter16Rl2Commands.Any(x => string.Equals(x.Argument, "W91=1", StringComparison.OrdinalIgnoreCase)) &&
                chapter16Rl2Commands.Any(x => string.Equals(x.Argument, "W91=0", StringComparison.OrdinalIgnoreCase)) &&
                !catalogue.Any(x => x.Chapter == 16 &&
                    (x.Argument ?? string.Empty).StartsWith("W94=", StringComparison.OrdinalIgnoreCase)),
                "BCM250 CH16 MPS RL2 commands must use W91=1/0 and must not use W94.");
            Require(
                catalogue.Any(x => x.Chapter == 23 &&
                    string.Equals(x.Argument, "W69=0", StringComparison.OrdinalIgnoreCase) &&
                    x.Name.IndexOf("34.b", StringComparison.Ordinal) >= 0),
                "BCM250 CH23 must enable CCB at step 34.");
            Require(
                catalogue.Any(x => x.Chapter == 24 &&
                    string.Equals(x.Argument, "W69=0", StringComparison.OrdinalIgnoreCase) &&
                    x.Name.IndexOf("26.b", StringComparison.Ordinal) >= 0),
                "BCM250 CH24 must enable CCB at step 26.");
            Bcm250ActionDefinition ccbVoltageTarget = catalogue.Single(x =>
                x.Chapter == 25 &&
                x.Kind == Bcm250ActionKind.MbdCommand &&
                x.Name.IndexOf("30 设置CCB电压目标", StringComparison.Ordinal) >= 0);
            Require(
                string.Equals(ccbVoltageTarget.Argument, "W13=267000", StringComparison.OrdinalIgnoreCase),
                "BCM250 CH25 step 30 must use W13=267000.");
            Bcm250ActionDefinition s01Target = catalogue.Single(x =>
                string.Equals(x.StepId, "BCM250.CH25.S033.ACTION", StringComparison.OrdinalIgnoreCase));
            Require(
                s01Target.Kind == Bcm250ActionKind.MbdCommand &&
                string.Equals(s01Target.Argument, "W12=6000", StringComparison.OrdinalIgnoreCase),
                "BCM250 S01 target must remain W12=6000.");
            Bcm250ActionDefinition ccbReadyWait = catalogue.Single(x =>
                string.Equals(x.StepId, "BCM250.CH25.S035.ACTION", StringComparison.OrdinalIgnoreCase));
            Require(
                ccbReadyWait.Kind == Bcm250ActionKind.WaitForCcbCurrentStable,
                "BCM250 CH25 step 34 must wait conditionally for stable R44 current.");
            Require(
                plan.GetStep(ccbReadyWait.StepId).DelayMs == 20000,
                "BCM250 CCB current readiness timeout must be 20000 ms in XML.");
            int[] measuredShuntChannels = catalogue
                .Where(x => x.Kind == Bcm250ActionKind.DaqShuntResistance)
                .Select(x => x.Channel)
                .OrderBy(x => x)
                .ToArray();
            Require(measuredShuntChannels.SequenceEqual(new[] { 113, 117, 119 }),
                "BCM250 must retain all three visible startup shunt-resistance rows.");
            Require(catalogue
                    .Where(x => x.Kind == Bcm250ActionKind.DaqShuntCurrent)
                    .All(x => measuredShuntChannels.Contains(x.Channel)),
                "A BCM250 shunt-current action has no preceding measured-resistance channel.");
            Require(catalogue.Any(x =>
                    x.Kind == Bcm250ActionKind.DaqShuntCurrent &&
                    x.Channel == Bcm250StationMap.VccShuntDaqChannel &&
                    Math.Abs(x.Scale - 2.0) < 1e-12),
                "BCM250 +VCC current action did not retain the 2.0 ohm single-chapter fallback.");
            Require(catalogue.Any(x =>
                    x.Kind == Bcm250ActionKind.DaqShuntCurrent &&
                    x.Channel == Bcm250StationMap.FiveVoltShuntDaqChannel &&
                    Math.Abs(x.Scale - 0.4) < 1e-12),
                "BCM250 +5V current action did not retain the 0.4 ohm single-chapter fallback.");

            Bcm250ActionDefinition[] chapter24 = catalogue
                .Where(x => x.Chapter == 24)
                .ToArray();
            int ccbOnIndex = Array.FindIndex(chapter24, x =>
                x.Kind == Bcm250ActionKind.MbdCommand &&
                string.Equals(x.Argument, "W69=0", StringComparison.OrdinalIgnoreCase));
            int ccbOffBefore15VIndex = Array.FindIndex(chapter24, x =>
                x.Kind == Bcm250ActionKind.MbdCommand &&
                string.Equals(x.Argument, "W69=1", StringComparison.OrdinalIgnoreCase) &&
                x.Name.IndexOf("33 关闭CCB后", StringComparison.Ordinal) >= 0);
            int fifteenVoltIndex = Array.FindIndex(chapter24, x =>
                x.Kind == Bcm250ActionKind.SetVbus &&
                x.Name.IndexOf("34 设置VSTR_NEG 15V", StringComparison.Ordinal) >= 0);
            Require(
                ccbOnIndex >= 0 &&
                ccbOffBefore15VIndex > ccbOnIndex &&
                fifteenVoltIndex > ccbOffBefore15VIndex,
                "BCM250 CH24 must close CCB before entering the 15V/0A verification point.");

            int stringSixAmpIndex = Array.FindIndex(chapter24, x =>
                x.Kind == Bcm250ActionKind.SetString &&
                x.Name.IndexOf("40 设置String发生器10V/6A", StringComparison.Ordinal) >= 0 &&
                string.Equals(x.Argument, "10,6,300", StringComparison.Ordinal));
            int r11Index = Array.FindIndex(chapter24, x =>
                string.Equals(x.ResultId, "R11", StringComparison.OrdinalIgnoreCase));
            Require(
                stringSixAmpIndex >= 0 &&
                r11Index == stringSixAmpIndex + 1 &&
                !chapter24.Skip(stringSixAmpIndex + 1).Take(r11Index - stringSixAmpIndex - 1).Any(x =>
                    x.Kind == Bcm250ActionKind.MbdCommand &&
                    string.Equals(x.Argument, "W69=0", StringComparison.OrdinalIgnoreCase)),
                "BCM250 CH24 must set String to 10V/6A and proceed directly to R11 without reopening CCB.");
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
                string[] startupResistanceSteps =
                {
                    "BCM250.CH07.S004.ACTION",
                    "BCM250.CH07.S005.ACTION",
                    "BCM250.CH07.S006.ACTION"
                };
                Require(startupResistanceSteps.All(stepId =>
                        passGrid.ValuesByStepId.ContainsKey(stepId) &&
                        passGrid.ValuesByStepId[stepId].StartsWith("SKIPPED (nominal ", StringComparison.Ordinal)),
                    "BCM250 CH7 startup DAQ resistance rows were not retained as visible SKIPPED rows.");
                Require(passGrid.ScrollCount < 200,
                    "BCM250 UI scroll throttling regressed; too many ScrollIntoView requests were issued.");

                TestPlanDefinition singleDefinition = TestPlanService.LoadDefinition(xmlPath);
                var chapter8Overrides = singleDefinition.Steps.ToDictionary(
                    step => step.StepId,
                    step => step.GroupId == Bcm250GroupIds.Chapter(8) ||
                            step.GroupId == Bcm250GroupIds.Precheck ||
                            step.GroupId == Bcm250GroupIds.Cleanup,
                    StringComparer.OrdinalIgnoreCase);
                var chapter8Grid = new RecordingGrid();
                var chapter8Context = new TestSequenceContext
                {
                    SN = "BCM250-CH08-SHUNT-FALLBACK",
                    TestPlan = TestPlanService.Resolve(
                        xmlPath,
                        TestProfileIds.SingleChapter,
                        chapter8Overrides),
                    StopOnFail = true
                };
                bool chapter8Pass = new SKBCM250Sequence(chapter8Grid)
                    .RunAsync(chapter8Context)
                    .GetAwaiter()
                    .GetResult();
                Require(chapter8Pass,
                    "BCM250 CH8 single-chapter run did not use nominal shunt fallback values.");
                Require(chapter8Grid.PassedStepIds.Any(x =>
                            x.StartsWith(Bcm250GroupIds.Chapter(8) + ".", StringComparison.OrdinalIgnoreCase) &&
                            x.EndsWith(".B06", StringComparison.OrdinalIgnoreCase)) &&
                        chapter8Grid.PassedStepIds.Any(x =>
                            x.StartsWith(Bcm250GroupIds.Chapter(8) + ".", StringComparison.OrdinalIgnoreCase) &&
                            x.EndsWith(".B07", StringComparison.OrdinalIgnoreCase)),
                    "BCM250 CH8 single-chapter run did not execute the shunt-current rows.");
                Require(chapter8Grid.CleanupCompleted == 6,
                    "BCM250 CH8 single-chapter run did not execute all cleanup steps.");

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

        private static void ValidateProgrammingSkipOption()
        {
            var sequence = new SKBCM250Sequence(skipFirmwareProgramming: true);
            PropertyInfo skipProperty = typeof(SKBCM250Sequence).GetProperty(
                "SkipFirmwareProgramming",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Require(skipProperty != null && (bool)skipProperty.GetValue(sequence),
                "BCM250 sequence did not retain the firmware-programming skip option.");

            var device = new SK441Device
            {
                SkipComInit = true,
                SimulationBoardType = "BCM-250",
                SkipFirmwareProgramming = true
            };
            Require(!device.IsTtlProgrammingRequired,
                "BCM250 programming skip option did not remove the TTL requirement.");
        }

        private static void ValidateProgrammingGuards(string root, IList steps)
        {
            object programmingStep = steps.Cast<object>().First(step => string.Equals(
                Read<string>(step.GetType(), step, "StepId"),
                "BCM250.CH09.S020.C01",
                StringComparison.OrdinalIgnoreCase));
            Require(Read<bool>(programmingStep.GetType(), programmingStep, "PowerCritical"),
                "Real BCM250 firmware programming must be power critical.");

            string malformedHex = Path.Combine(Path.GetTempPath(), "bcm250_bad_extended_address.hex");
            File.WriteAllText(malformedHex, ":00000004FC\r\n");
            try
            {
                bool rejected = false;
                try
                {
                    Stm32IntelHexParser.Parse(malformedHex, 0x08000000, 0x0801FFFF);
                }
                catch (InvalidDataException ex)
                {
                    rejected = ex.Message.Contains("扩展线性地址记录长度无效");
                }
                Require(rejected,
                    "Malformed HEX extended-address records must fail explicitly instead of indexing past the record.");
            }
            finally
            {
                File.Delete(malformedHex);
            }
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
            public Dictionary<string, string> ValuesByStepId { get; } =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public int ScrollCount { get; private set; }

            public Task<bool> IsRowSelectedAsync(int rowIndex) => Task.FromResult(true);
            public Task<bool> IsStepSelectedAsync(string stepId) => Task.FromResult(true);
            public Task SetValueAsync(int channelIndex, int rowIndex, string value) => Task.CompletedTask;
            public Task SetResultAsync(int channelIndex, int rowIndex, string result) => Task.CompletedTask;
            public Task SetValueAndResultAsync(int channelIndex, int rowIndex, string value, bool pass) => Task.CompletedTask;
            public Task SetValueAndResultByStepIdAsync(int channelIndex, string stepId, string value, bool pass) => Task.CompletedTask;
            public Task SetExecTimeAsync(int rowIndex, long elapsedMs) => Task.CompletedTask;
            public Task SetExecTimeByStepIdAsync(string stepId, long elapsedMs) => Task.CompletedTask;
            public Task ScrollToRowAsync(int rowIndex, int durationMs = 150) => Task.CompletedTask;
            public Task ScrollToStepAsync(string stepId, int durationMs = 150)
            {
                ScrollCount++;
                return Task.CompletedTask;
            }

            public Task SetValueAndStatusByStepIdAsync(
                int channelIndex,
                string stepId,
                string value,
                StepExecutionStatus status)
            {
                ValuesByStepId[stepId] = value ?? string.Empty;
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
