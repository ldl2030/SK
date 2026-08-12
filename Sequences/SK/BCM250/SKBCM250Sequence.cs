using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TestPlatform.TestSequences
{
    public sealed class SKBCM250Sequence : SKBcmSequenceBase
    {
        public override string SequenceKey => "SK_BCM250_Sequence";
        public override string ExpectedBoardType => "BCM-250";

        public SKBCM250Sequence(
            ITestGridService grid = null,
            Func<string, Task<bool>> confirmAsync = null)
            : base(grid, confirmAsync)
        {
        }

        protected override void AddElectricalTestSteps(
            List<TestStepItem> steps,
            TestSequenceContext context,
            SK441Device deviceManager)
        {
            AssignPlanGroupToRange(steps, context, Bcm250GroupIds.Precheck, 0, steps.Count);

            IReadOnlyList<Bcm250ActionDefinition> catalog = Bcm250ActionCatalog.Create();
            foreach (IGrouping<int, Bcm250ActionDefinition> chapter in catalog
                .Where(x => !x.AlwaysRun)
                .GroupBy(x => x.Chapter)
                .OrderBy(x => x.Key))
            {
                string groupId = Bcm250GroupIds.Chapter(chapter.Key);
                AddPlanChapter(
                    steps,
                    context,
                    groupId,
                    () =>
                    {
                        foreach (Bcm250ActionDefinition action in chapter)
                            AddCatalogStep(steps, context, deviceManager, action, groupId);
                    });
            }

            AddPlanChapter(
                steps,
                context,
                Bcm250GroupIds.Cleanup,
                () =>
                {
                    foreach (Bcm250ActionDefinition action in catalog.Where(x => x.AlwaysRun))
                        AddCatalogStep(steps, context, deviceManager, action, Bcm250GroupIds.Cleanup);
                });
        }

        private void AddCatalogStep(
            IList<TestStepItem> steps,
            TestSequenceContext context,
            SK441Device device,
            Bcm250ActionDefinition action,
            string groupId)
        {
            var step = new TestStepItem
            {
                Name = action.Name,
                StepId = action.StepId,
                ExpectedGroupId = groupId,
                RowIndex = steps.Count,
                AlwaysRun = action.AlwaysRun,
                PowerCritical = IsPowerCritical(action),
                IsFixtureReleaseStep = action.AlwaysRun &&
                    action.Kind == Bcm250ActionKind.Cleanup
            };
            step.Action = token => ExecuteActionAsync(step, context, device, action, token);
            steps.Add(step);
        }

        private async Task<StepResult> ExecuteActionAsync(
            TestStepItem step,
            TestSequenceContext context,
            SK441Device device,
            Bcm250ActionDefinition action,
            CancellationToken token)
        {
            string injectedStep = Environment.GetEnvironmentVariable("TESTPLATFORM_BCM250_FAULT_STEP");
            if (device.SkipComInit &&
                !string.IsNullOrWhiteSpace(injectedStep) &&
                string.Equals(injectedStep, action.StepId, StringComparison.OrdinalIgnoreCase))
            {
                return Result(false, "Injected fault", action);
            }

            if (action.Kind == Bcm250ActionKind.Delay)
            {
                int delayMs = GetPlanDelayMs(
                    new[] { step },
                    0,
                    0);
                await Task.Delay(device.SkipComInit ? Math.Min(delayMs, 1) : delayMs, token);
                return Result(true, delayMs + " ms", action);
            }

            if (action.IsResult)
                return await ExecuteResultAsync(step, context, device, action, token);

            if (action.Kind == Bcm250ActionKind.DaqShuntResistance)
            {
                token.ThrowIfCancellationRequested();
                double resistance = device.SkipComInit
                    ? action.Scale
                    : await device.MeasureDaqChannelResistanceAsync(action.Channel);
                bool resistanceValid = !double.IsNaN(resistance) &&
                                       !double.IsInfinity(resistance) &&
                                       resistance > 0.0;
                return new StepResult
                {
                    IsPass = resistanceValid,
                    Value = resistanceValid
                        ? resistance.ToString("0.######", CultureInfo.InvariantCulture)
                        : "Invalid resistance",
                    NumericValue = resistance,
                    StoreKey = Bcm250ResultKeys.ShuntResistanceForChannel(action.Channel),
                    Unit = "Ohm"
                };
            }

            bool passed;
            switch (action.Kind)
            {
                case Bcm250ActionKind.CheckInstruments:
                    passed = await device.CheckRequiredInstrumentsAsync();
                    break;
                case Bcm250ActionKind.AllOutputsOff:
                    passed = await device.TurnOffAllInstrumentsAsync();
                    break;
                case Bcm250ActionKind.PowerRelay:
                    passed = await device.ControlRelayAsync(ParseInt(action.Argument), action.State);
                    break;
                case Bcm250ActionKind.SafeAnsPowerOn:
                    {
                        double[] values = ParseNumbers(action.Argument);
                        passed = values.Length >= 3 && await device.CloseRl4ThenEnableAnsAsync(
                            (int)values[0], (float)values[1], (float)values[2]);
                        break;
                    }
                case Bcm250ActionKind.SafeAnsPowerOff:
                    passed = await device.DisableAnsThenOpenRl4Async(ParseInt(action.Argument));
                    break;
                case Bcm250ActionKind.PowerRelays:
                    passed = await SetPowerRelaysAsync(
                        device,
                        ParseRelayList(action.Argument),
                        action.State);
                    break;
                case Bcm250ActionKind.FixtureRelays:
                    passed = await device.SetFixtureRelaysAsync(
                        ParseRelayList(action.Argument),
                        action.State,
                        action.Name);
                    break;
                case Bcm250ActionKind.CheckMbdCommunication:
                    passed = await device.CheckMbdCommunicationAsync();
                    break;
                case Bcm250ActionKind.DocumentedBoundary:
                    passed = true;
                    break;
                case Bcm250ActionKind.DaqRelays:
                    passed = await device.SetDaqRelaysAsync(
                        NormalizeDaqRelays(action.Argument),
                        action.State,
                        action.Name);
                    break;
                case Bcm250ActionKind.MbdCommand:
                    passed = await SendCommandsAsync(device, action.Argument);
                    break;
                case Bcm250ActionKind.SetVbus:
                    passed = await SetGeneratorAsync(device, action.Argument, true, action.State);
                    break;
                case Bcm250ActionKind.SetMpsPower:
                    {
                        double[] values = ParseNumbers(action.Argument);
                        passed = values.Length >= 2 &&
                            await device.SetAnsVoltageCurrentOutputAsync(
                                (float)values[0],
                                (float)values[1],
                                action.State);
                        break;
                    }
                case Bcm250ActionKind.SetString:
                    passed = action.Argument.IndexOf(";", StringComparison.Ordinal) >= 0 ||
                             action.Argument.IndexOf("points", StringComparison.OrdinalIgnoreCase) >= 0
                        ? true
                        : await SetGeneratorAsync(device, action.Argument, false, action.State);
                    break;
                case Bcm250ActionKind.SetLoad:
                    passed = await SetLoadAsync(device, action.Argument, action.State);
                    break;
                case Bcm250ActionKind.ShortSimulator:
                    double simulatorVoltage = ParseFirstNumber(action.Argument);
                    passed = await device.SetShortProtectionSimulatorAsync(
                        action.Argument ?? "3V3",
                        action.State ? simulatorVoltage : 0.0);
                    break;
                case Bcm250ActionKind.DaqVoltage:
                    await device.MeasureDaqChannelVoltageAsync(
                        action.Channel > 0 ? action.Channel : ParseInt(action.Argument));
                    passed = true;
                    break;
                case Bcm250ActionKind.WriteIdentity:
                    passed = await WriteAndVerifyIdentityAsync(device, context, action.Argument);
                    break;
                case Bcm250ActionKind.StoreToFlash:
                    passed = await device.SendMbdCommandAsync(Bcm250Commands.StoreToFlash, 0);
                    break;
                case Bcm250ActionKind.Cleanup:
                    passed = await device.CloseTargetBoardCommunicationAsync();
                    break;
                default:
                    passed = true;
                    break;
            }

            return Result(passed, passed ? "OK" : "FAIL", action);
        }

        private async Task<StepResult> ExecuteResultAsync(
            TestStepItem step,
            TestSequenceContext context,
            SK441Device device,
            Bcm250ActionDefinition action,
            CancellationToken token)
        {
            TestPlanStepDefinition plan = step.PlanDefinition;
            if (plan == null)
                throw new TestPlanConfigurationException("No XML definition for " + action.StepId);

            foreach (string requiredResultId in action.RequiresResultIds)
            {
                double ignored;
                if (!TryGetStoredNumericValue(
                    context,
                    Bcm250ResultKeys.For(requiredResultId),
                    out ignored))
                {
                    return Result(
                        false,
                        "Missing dependency " + requiredResultId,
                        action);
                }
            }

            if (action.Kind == Bcm250ActionKind.FirmwareVersion)
            {
                string expectedFirmware = string.IsNullOrWhiteSpace(plan.ExpectedValue)
                    ? Bcm250Commands.FirmwareFile
                    : plan.ExpectedValue;
                string response = device.SkipComInit
                    ? expectedFirmware
                    : await device.QueryMbdCommandAsync(action.Argument, 0);
                string actualFirmware = device.SkipComInit
                    ? response
                    : MbdResponseParser.ParsePayloadText(action.Argument, response);
                bool pass = string.Equals(
                    actualFirmware,
                    expectedFirmware,
                    StringComparison.OrdinalIgnoreCase);
                return new StepResult
                {
                    IsPass = pass,
                    Value = actualFirmware ?? string.Empty,
                    StoreKey = Bcm250ResultKeys.For(action.ResultId),
                    Unit = plan.Unit
                };
            }

            double lower;
            double upper;
            GetPlanRange(new[] { step }, 0, 0, 0, out lower, out upper);
            string unit = GetPlanUnit(new[] { step }, 0, string.Empty);
            double value;

            if (device.SkipComInit)
            {
                value = SimulationValue(action.ResultId, lower, upper);
            }
            else
            {
                value = await ReadHardwareValueAsync(device, action, unit, context, token);
            }

            bool passed = value >= lower && value <= upper;
            if (!device.SkipComInit &&
                passed &&
                action.Kind == Bcm250ActionKind.ComputedCalibration)
            {
                passed = await WriteComputedCalibrationAsync(
                    device,
                    action.ResultId,
                    value);
            }
            return new StepResult
            {
                IsPass = passed,
                Value = value.ToString("0.###", CultureInfo.InvariantCulture),
                NumericValue = value,
                StoreKey = Bcm250ResultKeys.For(action.ResultId),
                Unit = unit
            };
        }

        private async Task<double> ReadHardwareValueAsync(
            SK441Device device,
            Bcm250ActionDefinition action,
            string unit,
            TestSequenceContext context,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            switch (action.Kind)
            {
                case Bcm250ActionKind.DaqVoltage:
                    {
                        double volts = await ReadDaqAverageAsync(
                            device,
                            action.Channel,
                            SampleCountForResult(action.ResultId),
                            token);
                        return unit.IndexOf("mV", StringComparison.OrdinalIgnoreCase) >= 0
                            ? volts * 1000.0
                            : volts;
                    }
                case Bcm250ActionKind.DaqShuntCurrent:
                    {
                        double shuntOhms;
                        string resistanceKey =
                            Bcm250ResultKeys.ShuntResistanceForChannel(action.Channel);
                        if (context == null ||
                            !context.TryGetNumericValue(resistanceKey, out shuntOhms) ||
                            double.IsNaN(shuntOhms) ||
                            double.IsInfinity(shuntOhms) ||
                            shuntOhms <= 0.0)
                        {
                            throw new InvalidOperationException(
                                "Missing/invalid measured shunt resistance: " + resistanceKey);
                        }

                        double volts = await ReadDaqAverageAsync(
                            device,
                            action.Channel,
                            SampleCountForResult(action.ResultId),
                            token);
                        double currentMa = volts / shuntOhms * 1000.0 * ShuntSign(action.ResultId);
                        OnLogInfo(string.Format(
                            CultureInfo.InvariantCulture,
                            "DAQ{0}分流电流计算：V={1:0.########}V，R={2:0.########}Ohm，I={3:0.###}mA",
                            action.Channel,
                            volts,
                            shuntOhms,
                            currentMa));
                        return currentMa;
                    }
                case Bcm250ActionKind.ProgramFirmware:
                    // Hardware programming remains a deliberate validation gate:
                    // the deployed firmware path/TTL COM must be confirmed at station.
                    if (!device.IsTtlProgrammingRequired)
                    {
                        OnLogWarning("SKRequireTtl=false：BCM250 TTL烧录记录为SKIPPED，未执行也未模拟烧录。");
                        return 1.0;
                    }
                    return 0.0;
                case Bcm250ActionKind.StoreToFlash:
                    return await device.SendMbdCommandAsync(Bcm250Commands.StoreToFlash, 0) ? 1.0 : 0.0;
                case Bcm250ActionKind.WriteIdentity:
                    return await WriteAndVerifyIdentityAsync(device, context, action.Argument) ? 1.0 : 0.0;
                case Bcm250ActionKind.LogicResult:
                    return 1.0;
                case Bcm250ActionKind.ComputedCalibration:
                    return await ComputeCalibrationAsync(device, context, action);
                case Bcm250ActionKind.MosfetRamp:
                    return await RunMosfetRampAsync(device, token);
                default:
                    {
                        string command = action.Argument;
                        if (string.Equals(action.ResultId, "D01", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(action.ResultId, "D02", StringComparison.OrdinalIgnoreCase))
                        {
                            return await ReadCanStatusAsync(
                                device,
                                action.ResultId,
                                command,
                                token);
                        }
                        if (command != null && command.StartsWith("DAQ", StringComparison.OrdinalIgnoreCase))
                        {
                            int channel = ParseInt(command.Substring(3));
                            double volts = await device.MeasureDaqChannelVoltageAsync(channel);
                            return unit.IndexOf("mV", StringComparison.OrdinalIgnoreCase) >= 0
                                ? volts * 1000.0
                                : volts;
                        }
                        return await ReadMbdAverageAsync(
                            device,
                            command,
                            SampleCountForResult(action.ResultId),
                            SampleIntervalForResult(action.ResultId),
                            token);
                    }
            }
        }

        private static bool IsPowerCritical(Bcm250ActionDefinition action)
        {
            if (action.AlwaysRun)
                return false;

            switch (action.Kind)
            {
                case Bcm250ActionKind.PowerRelay:
                case Bcm250ActionKind.SafeAnsPowerOn:
                case Bcm250ActionKind.SafeAnsPowerOff:
                case Bcm250ActionKind.PowerRelays:
                case Bcm250ActionKind.SetVbus:
                case Bcm250ActionKind.SetMpsPower:
                case Bcm250ActionKind.SetString:
                case Bcm250ActionKind.SetLoad:
                case Bcm250ActionKind.DaqRelays:
                case Bcm250ActionKind.DaqVoltage:
                case Bcm250ActionKind.DaqShuntResistance:
                case Bcm250ActionKind.DaqShuntCurrent:
                case Bcm250ActionKind.ProgramFirmware:
                case Bcm250ActionKind.ShortSimulator:
                    return true;
                default:
                    return false;
            }
        }

        private static async Task<bool> SetPowerRelaysAsync(
            SK441Device device,
            int[] relayIds,
            bool close)
        {
            bool passed = true;
            foreach (int relayId in relayIds)
                passed = await device.ControlRelayAsync(relayId, close) && passed;

            // A partially closed power path is not a valid state. In particular,
            // BCM250 String output requires Y1/Y3/Y6 as one indivisible route.
            if (close && !passed)
            {
                foreach (int relayId in relayIds)
                    await device.ControlRelayAsync(relayId, false);
            }
            return passed;
        }

        private static async Task<double> ReadDaqAverageAsync(
            SK441Device device,
            int channel,
            int sampleCount,
            CancellationToken token)
        {
            double total = 0.0;
            for (int index = 0; index < sampleCount; index++)
            {
                token.ThrowIfCancellationRequested();
                total += await device.MeasureDaqChannelVoltageAsync(channel);
            }
            return total / sampleCount;
        }

        private async Task<double> ReadCanStatusAsync(
            SK441Device device,
            string resultId,
            string command,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string response = await device.QueryMbdCommandAsync(command, 0);
            int[] buffer = MbdResponseParser.ParseIntegerVector(command, response, 16);
            bool passed;

            if (string.Equals(resultId, "D01", StringComparison.OrdinalIgnoreCase))
            {
                // Rev.5 describes the BCM status in byte 2, while the customer firmware
                // stcu_UP250_V5 reports the same zero status in byte 3. Accept either
                // documented layout so that this step tests CAN state instead of firmware layout.
                passed = buffer[1] == 0 || buffer[2] == 0;
                OnLogInfo(string.Format(
                    CultureInfo.InvariantCulture,
                    "D01/R14 CAN判定：byte2={0:D2}, byte3={1:D2}，兼容文档与客户固件位置，结果={2}",
                    buffer[1],
                    buffer[2],
                    passed ? 0 : 1));
            }
            else
            {
                passed = buffer[15] == 0;
                OnLogInfo(string.Format(
                    CultureInfo.InvariantCulture,
                    "D02/R15 CAN判定：byte16={0:D2}，结果={1}",
                    buffer[15],
                    passed ? 0 : 1));
            }

            return passed ? 0.0 : 1.0;
        }

        private static async Task<double> ReadMbdAverageAsync(
            SK441Device device,
            string command,
            int sampleCount,
            int intervalMs,
            CancellationToken token)
        {
            double total = 0.0;
            for (int index = 0; index < sampleCount; index++)
            {
                token.ThrowIfCancellationRequested();
                string response = await device.QueryMbdCommandAsync(command, 0);
                total += MbdResponseParser.ParseScalar(command, response);
                if (intervalMs > 0 && index + 1 < sampleCount)
                    await Task.Delay(intervalMs, token);
            }
            return total / sampleCount;
        }

        private static int SampleCountForResult(string resultId)
        {
            switch ((resultId ?? string.Empty).ToUpperInvariant())
            {
                case "J04":
                    return 5;
                case "M01":
                case "M02":
                case "M03":
                case "M04":
                case "N02":
                case "N04":
                    return 8;
                case "M09":
                    return 20;
                case "N01":
                case "N03":
                    return 16;
                case "N07":
                case "N08":
                    return 50;
                case "Q01":
                case "Q02":
                case "Q05":
                case "Q06":
                case "Q08":
                case "Q09":
                    return 4;
                default:
                    return 1;
            }
        }

        private static int SampleIntervalForResult(string resultId)
        {
            switch ((resultId ?? string.Empty).ToUpperInvariant())
            {
                case "J04":
                case "M09":
                    return 200;
                case "N01":
                case "N03":
                    return 20;
                case "N07":
                case "N08":
                    return 50;
                default:
                    return 0;
            }
        }

        private static double ShuntSign(string resultId)
        {
            switch ((resultId ?? string.Empty).ToUpperInvariant())
            {
                case "Q10":
                case "R06":
                case "R14":
                case "S01":
                    return -1.0;
                default:
                    return 1.0;
            }
        }

        private async Task<double> ComputeCalibrationAsync(
            SK441Device device,
            TestSequenceContext context,
            Bcm250ActionDefinition action)
        {
            string id = (action.ResultId ?? string.Empty).ToUpperInvariant();
            double value;

            switch (id)
            {
                case "J01":
                    {
                        double adc = MbdResponseParser.ParseScalar(
                            "R51",
                            await device.QueryMbdCommandAsync("R51", 0));
                        double vbusMv =
                            await device.MeasureDaqChannelVoltageAsync(
                                Bcm250StationMap.VbusReferenceDaqChannel) * 1000.0;
                        value = 4096.0 * vbusMv / adc;
                        return value;
                    }
                case "M05":
                    value = 4096.0 *
                        (Stored(context, "M02") - Stored(context, "M04")) /
                        (Stored(context, "M01") - Stored(context, "M03"));
                    return value;
                case "M06":
                    value = Stored(context, "M04") -
                        (Stored(context, "M05") * Stored(context, "M03") / 4096.0);
                    return value;
                case "N05":
                    value = (Stored(context, "N04") - Stored(context, "N02")) *
                        4096.0 /
                        (Stored(context, "N03") - Stored(context, "N01"));
                    return value;
                case "N06":
                    value = Stored(context, "N02") -
                        (Stored(context, "N05") * Stored(context, "N01") / 4096.0);
                    return value;
                case "Q12":
                    value = (Stored(context, "Q07") - Stored(context, "Q03")) *
                        4096.0 /
                        (Stored(context, "Q05") - Stored(context, "Q01"));
                    return value;
                case "Q13":
                    value = Stored(context, "Q03") -
                        (Stored(context, "Q12") * Stored(context, "Q01") / 4096.0);
                    return value;
                case "Q14":
                    value = Math.Abs(Stored(context, "Q10")) * 4096.0 /
                        (Stored(context, "Q09") - Stored(context, "Q02"));
                    return value;
                case "Q15":
                    value = -Stored(context, "Q14") *
                        (Stored(context, "Q06") - Stored(context, "Q02")) /
                        (Stored(context, "Q05") - Stored(context, "Q01"));
                    return value;
                case "Q16":
                    value = -(
                        Stored(context, "Q14") * Stored(context, "Q02") +
                        Stored(context, "Q15") * Stored(context, "Q01")) /
                        4096.0;
                    return value;
                default:
                    throw new InvalidOperationException(
                        "Unsupported BCM250 computed calibration result: " + id);
            }
        }

        private double Stored(TestSequenceContext context, string resultId)
        {
            double value;
            if (!TryGetStoredNumericValue(
                context,
                Bcm250ResultKeys.For(resultId),
                out value))
            {
                throw new InvalidOperationException(
                    "Missing BCM250 calibration dependency: " + resultId);
            }
            return value;
        }

        private static Task<bool> WriteComputedCalibrationAsync(
            SK441Device device,
            string resultId,
            double value)
        {
            string commandPrefix;
            switch ((resultId ?? string.Empty).ToUpperInvariant())
            {
                case "J01": commandPrefix = "W18="; break;
                case "M05": commandPrefix = "W20="; break;
                case "M06": commandPrefix = "W21="; break;
                case "N05": commandPrefix = "W26="; break;
                case "N06": commandPrefix = "W27="; break;
                case "Q12": commandPrefix = "W16="; break;
                case "Q13": commandPrefix = "W17="; value = 0.0; break;
                case "Q14": commandPrefix = "W22="; break;
                case "Q15": commandPrefix = "W24="; break;
                case "Q16": commandPrefix = "W23="; break;
                default:
                    throw new InvalidOperationException(
                        "Unsupported BCM250 calibration write result: " + resultId);
            }

            return device.SendMbdCommandAsync(
                commandPrefix + Math.Round(value).ToString(
                    "0",
                    CultureInfo.InvariantCulture),
                0);
        }

        private static async Task<double> RunMosfetRampAsync(
            SK441Device device,
            CancellationToken token)
        {
            double lastCurrent = 0.0;
            for (int step = 0; step <= 10; step++)
            {
                token.ThrowIfCancellationRequested();
                double current = 0.3 + (step * 0.1);
                if (!await device.SetStringGeneratorAsync(5, (float)current, 10, true))
                    throw new InvalidOperationException("String current ramp set failed.");

                string currentResponse = await device.QueryMbdCommandAsync("R45", 0);
                string stateResponse = await device.QueryMbdCommandAsync("R59", 0);
                lastCurrent = MbdResponseParser.ParseScalar("R45", currentResponse);
                if (MbdResponseParser.ParseScalar("R59", stateResponse) >= 1.0)
                    return lastCurrent;
                await Task.Delay(200, token);
            }
            return lastCurrent;
        }

        private static async Task<bool> WriteAndVerifyIdentityAsync(
            SK441Device device,
            TestSequenceContext context,
            string readCommand)
        {
            string value = string.Equals(readCommand, "R40", StringComparison.OrdinalIgnoreCase)
                ? DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                : context?.SN ?? string.Empty;
            string writeCommand = string.Equals(readCommand, "R40", StringComparison.OrdinalIgnoreCase)
                ? "W40=" + value
                : "W41=" + value;
            if (!await device.SendMbdCommandAsync(writeCommand, 0))
                return false;
            string response = await device.QueryMbdCommandAsync(readCommand, 0);
            return response != null &&
                response.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static double SimulationValue(string resultId, double lower, double upper)
        {
            if (string.Equals(resultId, "T02", StringComparison.OrdinalIgnoreCase))
                return (lower + upper) / 2.0;
            return lower == upper ? lower : lower + ((upper - lower) * 0.5);
        }

        private static StepResult Result(bool passed, string value, Bcm250ActionDefinition action)
        {
            return new StepResult
            {
                IsPass = passed,
                Value = value,
                StoreKey = action.IsResult ? Bcm250ResultKeys.For(action.ResultId) : null
            };
        }

        private static async Task<bool> SendCommandsAsync(SK441Device device, string commands)
        {
            foreach (string command in (commands ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!await device.SendMbdCommandAsync(command.Trim(), 0))
                    return false;
            }
            return true;
        }

        private static async Task<bool> SetGeneratorAsync(SK441Device device, string argument, bool vbus, bool state)
        {
            double[] values = ParseNumbers(argument);
            if (values.Length < 3)
                return false;
            return vbus
                ? await device.SetVbusGeneratorAsync((float)values[0], (float)values[1], (float)values[2], state)
                : await device.SetStringGeneratorAsync((float)values[0], (float)values[1], (float)values[2], state);
        }

        private static async Task<bool> SetLoadAsync(SK441Device device, string argument, bool state)
        {
            double[] values = ParseNumbers(argument);
            if (values.Length < 3)
                return false;
            return await device.SetElectronicLoadCvAsync((float)values[0], (float)values[1], (float)values[2], state);
        }

        private static int[] ParseRelayList(string value)
        {
            return (value ?? string.Empty)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(ParseInt)
                .ToArray();
        }

        private static string NormalizeDaqRelays(string value)
        {
            string normalized = (value ?? string.Empty).Trim();
            return normalized.StartsWith("@", StringComparison.Ordinal)
                ? normalized
                : "@" + normalized;
        }

        private static int ParseInt(string value)
        {
            return int.Parse((value ?? string.Empty).Trim(), CultureInfo.InvariantCulture);
        }

        private static double[] ParseNumbers(string value)
        {
            return (value ?? string.Empty)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => double.Parse(x, CultureInfo.InvariantCulture))
                .ToArray();
        }

        private static double ParseFirstNumber(string response)
        {
            string numericPayload = response ?? string.Empty;
            int equalsIndex = numericPayload.IndexOf('=');
            if (equalsIndex >= 0)
                numericPayload = numericPayload.Substring(equalsIndex + 1);

            Match match = Regex.Match(
                numericPayload,
                @"[-+]?\d+(?:\.\d+)?",
                RegexOptions.CultureInvariant);
            if (!match.Success)
                throw new InvalidOperationException("MBD response contains no numeric value: " + response);
            return double.Parse(match.Value, CultureInfo.InvariantCulture);
        }

    }
}
