using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
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
                ValidateMps250BaudRate(root);
                ValidateCatalog();
                ValidateVbusStableWaits(root);
                ValidatePowerTelemetry(root);
                ValidateFixtureCleanup(steps, device);
                ValidateMpsFailurePolicy(context, steps);
                ValidatePowerSafety();
                ValidateCustomerReport(root, plan);
                RunEndToEnd(xml, false);
                RunEndToEnd(xml, true);

                Console.WriteLine("PASS MPS250: 193 rows, PDF Rev.3 thresholds, 115200 USB-B, verified fixture release, MPS-only failure cleanup, report generation, full simulation and J06 fault injection.");
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
            Require(definitions.Any(x => x.StepId == "MPS250.PRECHECK.S009.ACTION"), "MPS-250 USB-B insertion row is missing.");
            Require(definitions.Count(x => x.AlwaysRun) == 6, "Expected six cleanup rows.");
            Require(definitions.Count(x => x.GroupId == Mps250GroupIds.Chapter(17)) == 42, "Chapter 17 must expose 42 detailed rows after removing nonexistent Relay_1.");

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
            ValidateLimit(plan, "B07", 137000, 138000, "mV");
            ValidateLimit(plan, "G02", 1000000, null, "Ohm");
            ValidateLimit(plan, "K01", 1700, 2200, "mV");
            ValidateLimit(plan, "K02", 3000, 3300, "mV");
            ValidateLimit(plan, "K03", 400, 800, "mV");
            ValidateLimit(plan, "J06", 200000, 205000, "Count");
            ValidateLimit(plan, "L07", 100, 200, "mA");
            ValidateLimit(plan, "L11", 50, 100, "mA");
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
            Require(catalog.Where(x => x.Kind == Mps250ActionKind.DaqShuntCurrent).All(x => x.Channel == 117 && Math.Abs(x.Scale - 1.0) < 1e-9), "Current must use DAQ117/1 ohm shunt.");
            Require(catalog.Any(x => x.Kind == Mps250ActionKind.DaqResistance && x.Channel == 118), "Alarm resistance mapping missing.");
            Require(catalog.Where(x => x.Kind == Mps250ActionKind.MbdCommand).All(x => x.AlwaysRun || (x.Argument ?? "").IndexOf(';') < 0), "Normal commands must be one UI row each.");
            Require(catalog.Count(x => x.Kind == Mps250ActionKind.MbdCleanupIfConnected) == 1, "MPS-250 cleanup must use one connected-only MBD shutdown row.");
            Require(Mps250StationMap.FiveVoltIsolated == 119, "+5VI mapping must be DAQ119.");
            Require(Mps250StationMap.Vpwm == 114, "VPWM mapping must be DAQ114.");
            Require(Mps250StationMap.VbusShunt == 117, "VBUS shunt mapping must be DAQ117.");
            Require(Mps250StationMap.PPlus == 113 && Mps250StationMap.VbusReference == 113, "P+/VBUS reference mapping must be DAQ113.");
            Require(Mps250StationMap.U24Pin7 == 109, "U24.7 mapping must be DAQ109.");
            Require(Mps250StationMap.McuIsoBatt == 105, "MCU_ISOBATT mapping must be DAQ105.");
            Require(Mps250StationMap.ExternalPowerDaqRelays == "@305,306", "Relay_2/2BIS must use DAQ305/306.");
            Require(Mps250StationMap.AllControlledRelays == "2,3,4,5,6", "MPS-250 must not declare logical Relay_1.");
            Require(!catalog.Where(x => x.Kind == Mps250ActionKind.DaqRelays)
                .SelectMany(x => (x.Argument ?? "").Split(','))
                .Any(x => x.Trim() == "1"), "MPS-250 catalog must not operate Relay_1/high-power Y1.");

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Mps250ActionDefinition item in catalog)
            {
                foreach (string dependency in item.RequiresResultIds)
                    Require(seen.Contains(dependency), item.ResultId + " has missing/later dependency " + dependency + ".");
                if (item.IsResult)
                    seen.Add(item.ResultId);
            }
        }

        private static void ValidateMps250BaudRate(string root)
        {
            XDocument config = XDocument.Load(Path.Combine(root, "App.config"));
            XElement setting = config.Descendants("add").SingleOrDefault(element =>
                string.Equals(
                    (string)element.Attribute("key"),
                    "SKMPS250TargetBoardBaudRate",
                    StringComparison.OrdinalIgnoreCase));
            Require(
                setting != null && (string)setting.Attribute("value") == "115200",
                "MPS-250 USB-B must follow PDF Rev.3 at 115200 baud.");
        }

        private static void ValidatePowerTelemetry(string root)
        {
            XDocument config = XDocument.Load(Path.Combine(root, "App.config"));
            Dictionary<string, string> settings = config.Descendants("add")
                .Where(element => element.Attribute("key") != null)
                .ToDictionary(
                    element => (string)element.Attribute("key"),
                    element => (string)element.Attribute("value"),
                    StringComparer.OrdinalIgnoreCase);
            Require(
                settings.ContainsKey("SKMPS250PowerTelemetryEnabled") &&
                string.Equals(settings["SKMPS250PowerTelemetryEnabled"], "true", StringComparison.OrdinalIgnoreCase),
                "MPS-250 power telemetry must be enabled by default.");
            Require(
                settings.ContainsKey("SKMPS250PowerTelemetryIntervalMs") &&
                settings["SKMPS250PowerTelemetryIntervalMs"] == "1000",
                "MPS-250 power telemetry interval must default to 1000 ms.");

            var device = new SK441Device
            {
                SkipComInit = true,
                SimulationBoardType = "MPS-250"
            };
            Require(
                device.SetVbusGeneratorAsync(275, 2, 300, true).GetAwaiter().GetResult(),
                "Cannot prepare simulated VBUS telemetry.");
            Require(
                device.SetVbattGeneratorAsync(250, 0.1f, 300, true).GetAwaiter().GetResult(),
                "Cannot prepare simulated VBATT telemetry.");
            string telemetry = device.ReadMps250PowerTelemetryLine();
            Require(
                telemetry.Contains("VBUS/A 输出=ON") && telemetry.Contains("设定=275V/2A"),
                "VBUS telemetry is missing output state or setpoint.");
            Require(
                telemetry.Contains("VBATT/B 输出=ON") && telemetry.Contains("设定=250V/0.1A"),
                "VBATT telemetry is missing output state or setpoint.");
        }

        private static void ValidateVbusStableWaits(string root)
        {
            Mps250ActionDefinition[] stableActions = Mps250ActionCatalog.Create()
                .Where(action => action.Kind == Mps250ActionKind.SetVbusAndWaitStable)
                .ToArray();
            Require(
                stableActions.Length == 2,
                "B01 and D01 must each have a VBUS measured-voltage stability gate.");
            Require(
                stableActions.Any(action => action.Chapter == 7) &&
                stableActions.Any(action => action.Chapter == 9),
                "VBUS stability gates must be in chapters 7 and 9.");

            XDocument config = XDocument.Load(Path.Combine(root, "App.config"));
            Dictionary<string, string> settings = config.Descendants("add")
                .Where(element => element.Attribute("key") != null)
                .ToDictionary(
                    element => (string)element.Attribute("key"),
                    element => (string)element.Attribute("value"),
                    StringComparer.OrdinalIgnoreCase);
            Require(
                settings.ContainsKey("SKMPS250VbusMinimumSettleMs") &&
                settings["SKMPS250VbusMinimumSettleMs"] == "500",
                "MPS-250 VBUS minimum settle time must default to 500 ms.");
            Require(
                settings.ContainsKey("SKMPS250VbusStableTimeoutMs") &&
                settings["SKMPS250VbusStableTimeoutMs"] == "5000",
                "MPS-250 VBUS stability timeout must default to 5000 ms.");
        }

        private static void ValidateCustomerReport(string root, ResolvedTestPlan plan)
        {
            string sourceConfigDirectory = Path.Combine(
                root,
                "ProjectConfig",
                "SK",
                "MPS-250");
            string definitionPath = Path.Combine(sourceConfigDirectory, "MPS-250_report.psv");
            string autoTestPath = Path.Combine(sourceConfigDirectory, "MPS-250_autoTest.xml");
            string[] definitionLines = File.ReadAllLines(definitionPath)
                .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
                .ToArray();

            Require(
                definitionLines.Length == Bcm125LegacyReportExporter.Mps250ExpectedReportItemCount,
                "MPS250 customer report definition must contain exactly 60 output rows.");
            Require(
                definitionLines.Select(line => line.Split('|')[0])
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() == definitionLines.Length,
                "MPS250 customer report codes must be unique.");
            Require(
                definitionLines.Select(line => line.Split('|')[1])
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() == definitionLines.Length,
                "MPS250 customer report StepIds must be unique.");

            foreach (string line in definitionLines)
            {
                string[] fields = line.Split('|');
                Require(fields.Length == 9, "MPS250 customer report row must contain 9 fields: " + line);
                TestPlanStepDefinition step = plan.GetStep(fields[1]);
                double scale = double.Parse(fields[8], System.Globalization.CultureInfo.InvariantCulture);
                ValidateScaledReportLimit(fields[0], "lower", step.LowerLimit, fields[6], scale, false);
                ValidateScaledReportLimit(
                    fields[0],
                    "upper",
                    step.UpperLimit,
                    fields[7],
                    scale,
                    !step.UpperLimit.HasValue && step.LowerLimit.HasValue);
            }

            var table = new DataTable();
            table.Columns.Add("StepId", typeof(string));
            table.Columns.Add("Channel1Value", typeof(string));
            table.Columns.Add("Channel1Result", typeof(string));
            foreach (string line in definitionLines)
            {
                string[] fields = line.Split('|');
                string value = fields[0] == "B01" ? "5000" : "1";
                table.Rows.Add(fields[1], value, "PASS");
            }

            string requestedQaRoot = Environment.GetEnvironmentVariable("TESTPLATFORM_MPS250_REPORT_QA_ROOT");
            bool keepQaReport = !string.IsNullOrWhiteSpace(requestedQaRoot);
            string temporaryRoot = keepQaReport
                ? Path.GetFullPath(requestedQaRoot)
                : Path.Combine(
                    Path.GetTempPath(),
                    "TestPlatform_MPS250_Report_" + Guid.NewGuid().ToString("N"));
            string temporaryConfigDirectory = Path.Combine(
                temporaryRoot,
                "ProjectConfig",
                "SK",
                "MPS-250");
            Directory.CreateDirectory(temporaryConfigDirectory);
            File.Copy(
                definitionPath,
                Path.Combine(temporaryConfigDirectory, "MPS-250_report.psv"),
                true);
            File.Copy(
                autoTestPath,
                Path.Combine(temporaryConfigDirectory, "MPS-250_autoTest.xml"),
                true);

            try
            {
                Bcm125LegacyReportExporter.ExportResult result =
                    Bcm125LegacyReportExporter.SaveAsync(
                        table,
                        0,
                        "2295",
                        "MPS250-REPORT-TEST",
                        true,
                        "MPS-250",
                        new DateTime(2025, 12, 18, 13, 10, 0),
                        temporaryRoot)
                    .GetAwaiter()
                    .GetResult();

                Require(result.Success, "MPS250 TXT/PDF report generation failed: " + result.ErrorMessage);
                Require(result.DutPassed, "MPS250 all-pass report was not marked PASSED.");
                Require(File.Exists(result.TextFilePath), "MPS250 TXT report was not created.");
                Require(File.Exists(result.PdfFilePath), "MPS250 PDF report was not created.");

                string reportText = File.ReadAllText(result.TextFilePath);
                Require(
                    reportText.StartsWith("MPS_UP250, Codice operatore: 2295, Seriale: MPS250-REPORT-TEST"),
                    "MPS250 report header does not match the customer format.");
                Require(
                    reportText.Contains("B01") && reportText.Contains("+5V = 5 [4,8; 5,4] V"),
                    "MPS250 report did not scale or format the B01 measurement correctly.");
                Require(
                    new FileInfo(result.PdfFilePath).Length > 1000,
                    "MPS250 PDF report is unexpectedly empty.");
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
            double scale,
            bool expectAny)
        {
            if (expectAny)
            {
                Require(
                    string.Equals(reportLimit, "any", StringComparison.OrdinalIgnoreCase),
                    $"MPS250 report {code} must use 'any' for its open {limitName} limit.");
                return;
            }

            if (!xmlLimit.HasValue)
            {
                Require(
                    string.IsNullOrWhiteSpace(reportLimit),
                    $"MPS250 report {code} unexpectedly declares a {limitName} limit.");
                return;
            }

            Require(
                !string.IsNullOrWhiteSpace(reportLimit),
                $"MPS250 report {code} is missing its {limitName} limit.");
            double parsed = double.Parse(
                reportLimit.Replace(',', '.'),
                System.Globalization.CultureInfo.InvariantCulture);
            Require(
                Math.Abs(parsed - xmlLimit.Value * scale) < 0.0000001,
                $"MPS250 report {code} {limitName} limit does not match the XML plan after scaling.");
        }

        private static void ValidateLimit(ResolvedTestPlan plan, string resultId, double? lower, double? upper, string unit)
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
            Require(device.SetVbusGeneratorAsync(275, 2, 300, true).GetAwaiter().GetResult(), "MPS VBUS must enable directly without a high-power relay.");
            Require(device.SetVbattGeneratorAsync(250, 0.1f, 300, true).GetAwaiter().GetResult(), "MPS VBATT must enable directly without a high-power relay.");
            Require(device.SetVbattGeneratorAsync(0, 0, 300, false).GetAwaiter().GetResult(), "Cannot turn off direct VBATT output.");
            Require(!device.StopFixturePressDownAsync("MPS-250").GetAwaiter().GetResult(), "Fixture released while VBUS was energized.");
            Require(!device.ControlMpsRelayAsync(5, Mps250StationMap.SensPrecDaqRelay, true).GetAwaiter().GetResult(), "Relay_5/DAQ303 closed while VBUS was energized.");
            Require(device.ControlMpsRelayAsync(3, Mps250StationMap.WithdrawalDaqRelay, true).GetAwaiter().GetResult(), "Relay_3 DAQ switching was incorrectly blocked.");
            Require(device.SafeSwitchMpsVbusRelayAsync(5, Mps250StationMap.SensPrecDaqRelay, true, 275, 2, 300, true).GetAwaiter().GetResult(), "Relay_5/DAQ303 safe switching failed.");
            Require(device.SetVbusGeneratorAsync(0, 0, 300, false).GetAwaiter().GetResult(), "Cannot turn off VBUS.");
            Require(device.SafeConnectMpsVbattRelayAsync(6, Mps250StationMap.IsoBattDaqRelay, 300).GetAwaiter().GetResult(), "Relay_6/DAQ304 safe connection failed.");
            Require(device.SafeDisconnectMpsVbattRelayAsync(6, Mps250StationMap.IsoBattDaqRelay, 300).GetAwaiter().GetResult(), "Safe MPS VBATT disconnection failed.");
            Require(device.TurnOffAllInstrumentsAsync().GetAwaiter().GetResult(), "Cannot power off before fixture release.");
            Require(device.StopFixturePressDownAsync("MPS-250").GetAwaiter().GetResult(), "Fixture remained locked after safe power-off.");
            Require(!device.GetPowerSafetyTrace().Any(x => x.Contains("RELAY 1 ")), "MPS-250 touched nonexistent high-power Relay_1.");
            Require(device.GetPowerSafetyTrace().Any(x => x.StartsWith("BLOCK", StringComparison.Ordinal)), "No unsafe VBUS action was recorded.");

            var emergency = new SK441Device { SkipComInit = true, SimulationBoardType = "MPS-250" };
            Require(emergency.EnableFixturePressDownAsync("MPS-250").GetAwaiter().GetResult(), "MPS-250 fixture Y1 did not close.");
            Require(emergency.EmergencyStopOutputsAsync("MPS-250", "test emergency").GetAwaiter().GetResult(), "MPS-250 emergency output shutdown failed.");
            string[] fixtureTrace = emergency.GetFixtureActuationTrace();
            Require(fixtureTrace.Contains("FIXTURE STATION 1 RELAY 1 CLOSED") && fixtureTrace.Contains("FIXTURE STATION 1 RELAY 1 OPEN"), "MPS-250 fixture Y1 emergency mapping failed.");
            Require(!fixtureTrace.Any(x => x.Contains("RELAY 2")), "MPS-250 incorrectly used fixture Y2 for press-down.");
            Require(!emergency.SetVbusGeneratorAsync(275, 2, 300, true).GetAwaiter().GetResult(), "Emergency latch allowed VBUS output ON.");
        }

        private static void ValidateFixtureCleanup(IList steps, SK441Device device)
        {
            object cleanupStep = steps.Cast<object>().Single(step =>
                Read<bool>(step.GetType(), step, "IsFixtureReleaseStep"));
            Require(
                device.EnableFixturePressDownAsync("MPS-250").GetAwaiter().GetResult(),
                "Cannot prepare fixture-release cleanup test.");

            Delegate action = Read<Delegate>(cleanupStep.GetType(), cleanupStep, "Action");
            Task task = (Task)action.DynamicInvoke(CancellationToken.None);
            task.GetAwaiter().GetResult();
            object result = task.GetType().GetProperty("Result").GetValue(task);
            Require(
                Read<bool>(result.GetType(), result, "IsPass"),
                "Final MPS-250 cleanup row did not release and verify the fixture.");

            string[] trace = device.GetFixtureActuationTrace();
            Require(
                trace.Contains("FIXTURE STATION 1 RELAY 1 CLOSED") &&
                trace.Contains("FIXTURE STATION 1 RELAY 1 OPEN"),
                "Final MPS-250 cleanup did not open fixture station 1 relay Y1.");
        }

        private static void ValidateMpsFailurePolicy(TestSequenceContext context, IList steps)
        {
            var sequence = new SKMPS250Sequence();
            var device = new SK441Device
            {
                SkipComInit = true,
                SimulationBoardType = "MPS-250"
            };
            object failedStep = steps.Cast<object>().Single(step =>
                string.Equals(
                    Read<string>(step.GetType(), step, "StepId"),
                    "MPS250.CH15.S016.J06",
                    StringComparison.OrdinalIgnoreCase));

            Task task = (Task)FindMethod(
                sequence.GetType(),
                "ExecuteChapterFailurePolicyAsync").Invoke(
                    sequence,
                    new[] { context, device, failedStep });
            task.GetAwaiter().GetResult();

            Require(
                !device.GetPowerSafetyTrace().Any(x => x.Contains("RELAY 1 ")),
                "MPS-250 failure policy touched station-3 Relay_1.");
            Require(
                !device.GetFixtureActuationTrace().Any(x =>
                    x.Contains("RELAY 3") || x.Contains("RELAY 4") ||
                    x.Contains("RELAY 5") || x.Contains("RELAY 6") ||
                    x.Contains("RELAY 7") || x.Contains("RELAY 8")),
                "MPS-250 failure policy executed BCM fixture-programming relays.");
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
                Require(passed != injectFault, injectFault
                    ? "Injected-fault run passed."
                    : "Full simulation failed. Failed steps: " + string.Join(", ", grid.FailedStepIds));
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

