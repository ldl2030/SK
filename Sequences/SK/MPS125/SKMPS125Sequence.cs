using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TestPlatform.TestSequences
{
    public sealed partial class SKMPS125Sequence : SKBcmSequenceBase
    {
        public override string SequenceKey => "SK_MPS125_Sequence";
        public override string ExpectedBoardType => "MPS-125";

        public SKMPS125Sequence(ITestGridService grid = null, Func<string, Task<bool>> confirmAsync = null)
            : base(grid, confirmAsync)
        {
        }

        protected override void AddElectricalTestSteps(List<TestStepItem> steps, TestSequenceContext context, SK441Device device)
        {
            steps.Add(new TestStepItem
            {
                Name = "人工插入USB-B通讯线并确认",
                StepId = "MPS125.PRECHECK.S009.ACTION",
                ExpectedGroupId = Mps125GroupIds.Precheck,
                RowIndex = steps.Count,
                SafetyCritical = true,
                Action = async token =>
                {
                    device.PrepareMpsUsbBManualConnection(ExpectedBoardType);
                    bool confirmed = await ConfirmOperatorActionAsync(
                        "治具已下压到位。\n\n" +
                        "请现在插入 MPS-125 的 USB-B 通讯线。\n" +
                        "确认插接到位后点击“已插入”，也可以直接按 Enter。\n\n" +
                        "按 Esc 或点击“取消”将终止本次测试。",
                        "已插入");
                    if (!confirmed)
                        return new StepResult { IsPass = false, Value = "操作员取消插线" };

                    token.ThrowIfCancellationRequested();
                    return new StepResult { IsPass = true, Value = "USB-B已插入" };
                }
            });
            AssignPlanGroupToRange(steps, context, Mps125GroupIds.Precheck, 0, steps.Count);
            IReadOnlyList<Mps125ActionDefinition> catalog = Mps125ActionCatalog.Create();
            // A fresh cache belongs only to this run and the current heater state.
            var earlyResults = new Dictionary<string, StepResult>(StringComparer.OrdinalIgnoreCase);

            foreach (IGrouping<int, Mps125ActionDefinition> chapter in catalog
                .Where(x => !x.AlwaysRun)
                .GroupBy(x => x.Chapter)
                .OrderBy(x => x.Key))
            {
                string groupId = Mps125GroupIds.Chapter(chapter.Key);
                AddPlanChapter(steps, context, groupId, () =>
                {
                    foreach (Mps125ActionDefinition action in chapter)
                        AddCatalogStep(steps, context, device, action, groupId, catalog, earlyResults);
                });
            }

            AddPlanChapter(steps, context, Mps125GroupIds.Cleanup, () =>
            {
                foreach (Mps125ActionDefinition action in catalog.Where(x => x.AlwaysRun))
                    AddCatalogStep(steps, context, device, action, Mps125GroupIds.Cleanup, catalog, earlyResults);
            });
        }

        private void AddCatalogStep(IList<TestStepItem> steps, TestSequenceContext context, SK441Device device, Mps125ActionDefinition action, string groupId,
            IReadOnlyList<Mps125ActionDefinition> catalog, Dictionary<string, StepResult> earlyResults)
        {
            var step = new TestStepItem
            {
                Name = action.Name,
                StepId = action.StepId,
                ExpectedGroupId = groupId,
                RowIndex = steps.Count,
                AlwaysRun = action.AlwaysRun,
                MaxRetries = action.Kind == Mps125ActionKind.SetVbusRampStep ? 0 : -1,
                SafetyCritical = IsSafetyCritical(action) && !IsFinalChapterResult(action),
                PowerCritical = IsPowerCritical(action) && !IsFinalChapterResult(action),
                IsFixtureReleaseStep = action.AlwaysRun && action.Kind == Mps125ActionKind.Cleanup
            };
            step.Action = async token =>
            {
                token.ThrowIfCancellationRequested();
                if (!action.IsResult)
                    earlyResults.Clear();

                if (action.Kind == Mps125ActionKind.Delay)
                    return await ExecuteStabilityDelayAsync(step, steps, context, device, action, catalog, earlyResults, token);

                StepResult measured;
                if (action.IsResult && earlyResults.TryGetValue(action.StepId, out measured))
                {
                    earlyResults.Remove(action.StepId);
                    OnLogInfo("MPS-125 " + action.ResultId + " 复用稳定等待内的完整测量结果：" + measured.Value + " " + measured.Unit);
                    return measured;
                }

                return await ExecuteActionAsync(step, context, device, action, token);
            };
            steps.Add(step);
        }

        private async Task<StepResult> ExecuteActionAsync(TestStepItem step, TestSequenceContext context, SK441Device device, Mps125ActionDefinition action, CancellationToken token)
        {
            if (action.Chapter == 15 &&
                (action.Kind == Mps125ActionKind.DaqVbusCalibrationReference ||
                 action.Kind == Mps125ActionKind.SetVbus ||
                 action.Kind == Mps125ActionKind.SetVbusRampStep))
            {
                context.Values.Remove(Mps125ResultKeys.VbusCalibrationReference);
            }

            string faultStep = Environment.GetEnvironmentVariable("TESTPLATFORM_MPS125_FAULT_STEP");
            if (device.SkipComInit && !string.IsNullOrWhiteSpace(faultStep) &&
                string.Equals(faultStep, action.StepId, StringComparison.OrdinalIgnoreCase))
                return BasicResult(false, "Injected fault", action);

            if (action.IsResult)
                return await ExecuteResultAsync(step, context, device, action, token);

            string actionArgument = ResolveActionArgument(step, action);
            bool passed;
            switch (action.Kind)
            {
                case Mps125ActionKind.CheckInstruments:
                    passed = await device.CheckMpsRequiredInstrumentsAsync();
                    break;
                case Mps125ActionKind.AllOutputsOff:
                    passed = await device.TurnOffAllInstrumentsAsync();
                    break;
                case Mps125ActionKind.DaqRelays:
                    passed = await SetMpsRelaysAsync(device, ParseRelayList(action.Argument), action.State);
                    break;
                case Mps125ActionKind.CheckMbdCommunication:
                    passed = await device.CheckMbdCommunicationAsync();
                    break;
                case Mps125ActionKind.CloseMbdCommunication:
                    passed = await device.CloseTargetBoardCommunicationAsync();
                    break;
                case Mps125ActionKind.MbdCommand:
                    passed = IsChapter17StatefulCommand(action)
                        ? await SendCommandsOnCurrentConnectionAsync(device, action.Argument)
                        : await SendCommandsAsync(device, action.Argument);
                    break;
                case Mps125ActionKind.MbdCleanupIfConnected:
                    if (HasFixtureReleaseStarted || device.IsEmergencyStopLatched)
                    {
                        await device.CloseTargetBoardCommunicationAsync();
                        OnLogWarning(
                            "MPS-125治具已开始释放或急停已触发，USB-B会随下压解除而断开；" +
                            "跳过MBD安全关闭命令，不再重新枚举串口。");
                        return BasicResult(true, "治具已释放，跳过MBD命令", action);
                    }
                    if (!device.IsTargetBoardCommunicationConnected)
                    {
                        OnLogWarning(
                            "MPS-125目标板通信当前未连接；跳过MBD安全关闭命令，" +
                            "不重新枚举串口。硬件输出与继电器收尾继续执行。");
                        return BasicResult(true, "USB已断开，跳过MBD命令", action);
                    }
                    passed = await device.SendMbdCleanupCommandsOnCurrentConnectionAsync(
                        action.Argument);
                    break;
                case Mps125ActionKind.SetVbus:
                    passed = await SetGeneratorAsync(device, actionArgument, true, action.State);
                    break;
                case Mps125ActionKind.SetVbusAndWaitStable:
                    {
                        double[] values = ParseNumbers(actionArgument);
                        passed = values.Length >= 1 &&
                            await SetGeneratorAsync(device, actionArgument, true, true);
                        if (passed)
                        {
                            passed = await device.WaitForMps125VbusStableAsync(
                                (float)values[0],
                                token);
                        }
                        break;
                    }
                case Mps125ActionKind.SetVbusRampStep:
                    {
                        double[] values = ParseNumbers(actionArgument);
                        passed = values.Length >= 3 &&
                            await device.SetMps125VbusRampStepAsync(
                                (float)values[0],
                                (float)values[1],
                                (float)values[2],
                                action.State);
                        if (passed)
                        {
                            int delayMs = GetPlanDelayMs(new[] { step }, 0, 100);
                            await Task.Delay(
                                device.SkipComInit ? Math.Min(delayMs, 1) : delayMs,
                                token);
                        }
                        break;
                    }
                case Mps125ActionKind.WaitForMbdReadyAfterPowerCycle:
                    passed = await device.WaitForMbdReadyAfterPowerCycleAsync(25000, token);
                    break;
                case Mps125ActionKind.SetVbatt:
                    passed = await SetGeneratorAsync(device, action.Argument, false, action.State);
                    break;
                case Mps125ActionKind.SetVbattAndWaitStable:
                    {
                        double[] values = ParseNumbers(action.Argument);
                        passed = values.Length >= 1 &&
                            await SetGeneratorAsync(device, action.Argument, false, action.State);
                        if (passed)
                        {
                            passed = await device.WaitForMps125VbattVoltageAsync(
                                (float)values[0],
                                token);
                        }
                        break;
                    }
                case Mps125ActionKind.SafeVbusRelay:
                    {
                        double[] values = ParseNumbers(action.Argument);
                        passed = values.Length >= 5 && await device.SafeSwitchMpsVbusRelayAsync(
                            (int)values[0],
                            Mps125StationMap.GetDaqRelayChannels((int)values[0]),
                            action.State,
                            (float)values[1],
                            (float)values[2],
                            (float)values[3],
                            values[4] > 0,
                            token);
                        break;
                    }
                case Mps125ActionKind.SafeVbattConnect:
                    passed = await device.SafeConnectMps125VbattRelayAsync(
                        ParseInt(action.Argument),
                        Mps125StationMap.GetDaqRelayChannels(ParseInt(action.Argument)),
                        150,
                        token);
                    if (passed && !await device.WaitForMps125VbattVoltageAsync(0, token, preparationOnly: true))
                    {
                        OnLogWarning("K02前的VBATT零电压条件未确认；记录告警并继续8秒实测，不触发安全停机。K02仍按原范围判定。");
                        return new StepResult
                        {
                            IsPass = true,
                            IsWarning = true,
                            Value = "零电压条件未确认，告警续测"
                        };
                    }
                    break;
                case Mps125ActionKind.SafeVbattDisconnect:
                    passed = await device.SafeDisconnectMpsVbattRelayAsync(
                        ParseInt(action.Argument),
                        Mps125StationMap.GetDaqRelayChannels(ParseInt(action.Argument)),
                        150);
                    break;
                case Mps125ActionKind.DaqVoltage:
                    await device.MeasureDaqChannelVoltageAsync(ParseInt(action.Argument));
                    passed = true;
                    break;
                case Mps125ActionKind.DaqVbusCalibrationReference:
                    double referenceVolts = device.SkipComInit
                        ? 137.5
                        : await device.MeasureDaqChannelVoltageAsync(ParseInt(action.Argument));
                    token.ThrowIfCancellationRequested();
                    bool referenceValid = IsPositiveFinite(referenceVolts) && referenceVolts < 9E36;
                    return new StepResult
                    {
                        IsPass = referenceValid,
                        Value = referenceValid
                            ? referenceVolts.ToString("0.######", CultureInfo.InvariantCulture)
                            : "Invalid VBUS reference",
                        NumericValue = referenceValid ? (double?)referenceVolts : null,
                        StoreKey = Mps125ResultKeys.VbusCalibrationReference,
                        Unit = "V"
                    };
                case Mps125ActionKind.DocumentedBoundary:
                    passed = true;
                    break;
                case Mps125ActionKind.Cleanup:
                    // This is the final cleanup row.  The preceding rows have already
                    // disabled VBUS/VBATT and opened the functional relays, so release
                    // the fixture even if the target-board serial close fails.
                    bool communicationClosed = await device.CloseTargetBoardCommunicationAsync();
                    bool fixtureReleased = await ReleaseFixtureAndVerifyAsync(device);
                    passed = communicationClosed && fixtureReleased;
                    break;
                default:
                    passed = true;
                    break;
            }
            return BasicResult(passed, passed ? "OK" : "FAIL", action);
        }

        private async Task<StepResult> ExecuteResultAsync(TestStepItem step, TestSequenceContext context, SK441Device device, Mps125ActionDefinition action, CancellationToken token)
        {
            TestPlanStepDefinition plan = step.PlanDefinition;
            if (plan == null)
                throw new TestPlanConfigurationException("No XML definition for " + action.StepId);

            foreach (string dependency in action.RequiresResultIds)
            {
                double ignored;
                if (!TryGetStoredNumericValue(context, Mps125ResultKeys.For(dependency), out ignored))
                    return BasicResult(false, "Missing dependency " + dependency, action);
            }

            if (action.Kind == Mps125ActionKind.FirmwareVersion)
            {
                string expected = string.IsNullOrWhiteSpace(plan.ExpectedValue) ? "mcu_app U.2.2.2" : plan.ExpectedValue;
                string response = device.SkipComInit ? expected : await device.QueryMbdCommandAsync(action.Argument, 0);
                string actual = device.SkipComInit
                    ? response
                    : MbdResponseParser.ParsePayloadText(action.Argument, response);
                return new StepResult
                {
                    IsPass = string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                    Value = actual ?? string.Empty,
                    StoreKey = Mps125ResultKeys.For(action.ResultId),
                    Unit = plan.Unit
                };
            }

            bool preparationWarning = false;
            if (action.Kind == Mps125ActionKind.DaqAverageVoltageAfterVbattBaseline ||
                action.Kind == Mps125ActionKind.DaqTimedWindowAverageVoltageAfterVbattBaseline)
            {
                int relayId = ParseInt(action.Argument);
                bool baselineReady = await device.PrepareMps125VbattBaselineAsync(
                    relayId,
                    Mps125StationMap.GetDaqRelayChannels(relayId),
                    150,
                    token);
                if (!baselineReady)
                {
                    return new StepResult
                    {
                        IsPass = false,
                        Value = "VBATT基线条件不成立",
                        StoreKey = Mps125ResultKeys.For(action.ResultId),
                        Unit = plan.Unit
                    };
                }
                preparationWarning = !await device.WaitForMps125VbattVoltageAsync(0, token, preparationOnly: true);
                if (preparationWarning)
                    OnLogWarning("K01前的VBATT零电压条件未确认；记录告警并继续8秒实测，不触发安全停机。K01仍按原范围判定。");
            }

            double value = device.SkipComInit && action.Kind != Mps125ActionKind.ComputedGain
                ? SimulationValue(action.ResultId, plan.LowerLimit, plan.UpperLimit)
                : await ReadHardwareValueAsync(device, context, action, plan.Unit, token);
            bool passed = (!plan.LowerLimit.HasValue || value >= plan.LowerLimit.Value) &&
                          (!plan.UpperLimit.HasValue || value <= plan.UpperLimit.Value);

            if (string.Equals(action.ResultId, "H09", StringComparison.OrdinalIgnoreCase))
            {
                double baseline;
                passed = passed && TryGetStoredNumericValue(context, Mps125ResultKeys.For("H03"), out baseline) && value > baseline;
            }

            return new StepResult
            {
                IsPass = passed,
                IsWarning = passed && preparationWarning,
                Value = value.ToString("0.###", CultureInfo.InvariantCulture) +
                    (preparationWarning ? "（零电压条件未确认，告警续测）" : string.Empty),
                NumericValue = value,
                StoreKey = Mps125ResultKeys.For(action.ResultId),
                Unit = plan.Unit
            };
        }

        private async Task<double> ReadHardwareValueAsync(SK441Device device, TestSequenceContext context, Mps125ActionDefinition action, string unit, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            switch (action.Kind)
            {
                case Mps125ActionKind.DaqVoltage:
                case Mps125ActionKind.DaqAverageVoltage:
                case Mps125ActionKind.DaqAverageVoltageAfterVbattBaseline:
                    return ScaleVoltage(await ReadDaqVoltageAverageWithRangeRecoveryAsync(device, action.Channel, ResolveAverageSampleCount(action), action.SampleIntervalMs, token, action.DaqRangeVolts), unit);
                case Mps125ActionKind.DaqTimedWindowAverageVoltage:
                case Mps125ActionKind.DaqTimedWindowAverageVoltageAfterVbattBaseline:
                    return ScaleVoltage(
                        await device.MeasureDaqChannelVoltageWindowAverageAsync(
                            action.Channel,
                            (Math.Max(2, action.SampleCount) - 1) * action.SampleIntervalMs,
                            action.SampleIntervalMs,
                            token),
                        unit);
                case Mps125ActionKind.DaqShuntCurrent:
                    return await ReadDaqAverageAsync(device, action.Channel, ResolveAverageSampleCount(action), action.SampleIntervalMs, token) / action.Scale * 1000.0;
                case Mps125ActionKind.DaqResistance:
                    return await device.MeasureDaqChannelResistanceAsync(action.Channel);
                case Mps125ActionKind.IgnitionRamp:
                    return await RunIgnitionRampAsync(device, action, token);
                case Mps125ActionKind.ComputedGain:
                    return await ComputeGainAsync(context, action);
                default:
                    return await ReadMbdAverageAsync(
                        device,
                        action.Argument,
                        ResolveAverageSampleCount(action),
                        action.SampleIntervalMs,
                        action.Chapter == 17,
                        token);
            }
        }

        private static bool IsPowerCritical(Mps125ActionDefinition action)
        {
            if (action.AlwaysRun)
                return false;
            switch (action.Kind)
            {
                case Mps125ActionKind.SetVbus:
                case Mps125ActionKind.SetVbusAndWaitStable:
                case Mps125ActionKind.SetVbusRampStep:
                case Mps125ActionKind.WaitForMbdReadyAfterPowerCycle:
                case Mps125ActionKind.SetVbatt:
                case Mps125ActionKind.SetVbattAndWaitStable:
                case Mps125ActionKind.SafeVbusRelay:
                case Mps125ActionKind.SafeVbattConnect:
                case Mps125ActionKind.SafeVbattDisconnect:
                case Mps125ActionKind.DaqRelays:
                case Mps125ActionKind.DaqVbusCalibrationReference:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsSafetyCritical(Mps125ActionDefinition action)
        {
            if (action.AlwaysRun)
                return false;

            if (action.Kind == Mps125ActionKind.SetVbusRampStep ||
                action.Kind == Mps125ActionKind.SafeVbattConnect ||
                action.Kind == Mps125ActionKind.SetVbattAndWaitStable)
            {
                return true;
            }

            return false;
        }

        private static bool IsFinalChapterResult(Mps125ActionDefinition action)
        {
            return action.IsResult && (action.Chapter == 16 || action.Chapter == 17);
        }

        private static int ResolveAverageSampleCount(Mps125ActionDefinition action)
        {
            if (action.SampleCount != 16)
                return Math.Max(1, action.SampleCount);

            bool pwm = IsPwmSamplingChapter(action.Chapter);
            int configured;
            return int.TryParse(
                       System.Configuration.ConfigurationManager.AppSettings[
                           pwm ? "SKMPS125PwmAverageSampleCount" : "SKMPSNonPwmAverageSampleCount"],
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out configured) && configured > 0 && (!pwm || configured >= 8)
                ? configured
                : (pwm ? 8 : 16);
        }

        private static bool IsPwmSamplingChapter(int chapter)
        {
            return chapter == 11 || chapter == 13 || chapter == 16 || chapter == 17;
        }

        private static Task<bool> SetMpsRelaysAsync(SK441Device device, int[] ids, bool state)
        {
            return device.ControlMpsRelaysAsync(ids, state);
        }
        private static async Task<double> ReadDaqAverageAsync(SK441Device device, int channel, int count, int intervalMs, CancellationToken token, double? rangeVolts = null)
        {
            if (count <= 1 && !rangeVolts.HasValue)
                return await device.MeasureDaqChannelVoltageAsync(channel);

            return await device.MeasureDaqChannelVoltageAverageAsync(
                channel,
                Math.Max(1, count),
                intervalMs,
                token,
                rangeVolts: rangeVolts);
        }

        private static async Task<double> ReadMbdAverageAsync(
            SK441Device device,
            string command,
            int count,
            int intervalMs,
            bool requireCurrentConnection,
            CancellationToken token)
        {
            double sum = 0;
            int samples = Math.Max(1, count);
            for (int i = 0; i < samples; i++)
            {
                token.ThrowIfCancellationRequested();
                string response = requireCurrentConnection
                    ? await device.QueryMbdCommandOnCurrentConnectionAsync(command, 0)
                    : await device.QueryMbdCommandAsync(command, 0);
                sum += MbdResponseParser.ParseScalar(command, response);
                if (intervalMs > 0 && i + 1 < samples)
                    await Task.Delay(intervalMs, token);
            }
            return sum / samples;
        }

        private static async Task<double> RunIgnitionRampAsync(SK441Device device, Mps125ActionDefinition action, CancellationToken token)
        {
            double[] values = ParseNumbers(action.Argument);
            double start = values.Length > 0 ? values[0] : 28;
            double stop = values.Length > 1 ? values[1] : 80;
            double increment = values.Length > 2 ? values[2] : 1;
            return await device.MeasureMps125IgnitionVoltageAsync(
                action.Channel, start, stop, increment, token) * 1000.0;
        }

        private static Task<double> ComputeGainAsync(TestSequenceContext context, Mps125ActionDefinition action)
        {
            double adc;
            if (!TryGetStored(context, action.Argument, out adc) || !IsPositiveFinite(adc))
                throw new InvalidOperationException("Missing/invalid MPS-125 gain dependency " + action.Argument);

            TestSequenceValue reference;
            if (!context.TryGetValue(Mps125ResultKeys.VbusCalibrationReference, out reference) ||
                !reference.Pass || !reference.HasNumericValue ||
                !string.Equals(reference.Unit, "V", StringComparison.OrdinalIgnoreCase) ||
                !IsPositiveFinite(reference.NumericValue) || reference.NumericValue >= 9E36)
            {
                throw new InvalidOperationException("MPS-125 gain requires a successful DAQ113 VBUS reference from step 15.15.");
            }

            return Task.FromResult(4096.0 * reference.NumericValue * 1000.0 / adc);
        }

        private static bool IsPositiveFinite(double value) => value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);

        private static bool TryGetStored(TestSequenceContext context, string resultId, out double value)
        {
            value = 0;
            return context != null &&
                context.TryGetNumericValue(Mps125ResultKeys.For(resultId), out value);
        }

        private static double ScaleVoltage(double volts, string unit) => unit != null && unit.IndexOf("mV", StringComparison.OrdinalIgnoreCase) >= 0 ? volts * 1000.0 : volts;

        private static double SimulationValue(string resultId, double? lower, double? upper)
        {
            if (string.Equals(resultId, "H03", StringComparison.OrdinalIgnoreCase))
                return 30.0;
            if (lower.HasValue && upper.HasValue)
                return lower.Value == upper.Value ? lower.Value : lower.Value + ((upper.Value - lower.Value) * 0.5);
            if (lower.HasValue)
                return lower.Value;
            if (upper.HasValue)
                return upper.Value;
            return 0.0;
        }

        private static StepResult BasicResult(bool passed, string value, Mps125ActionDefinition action) => new StepResult
        {
            IsPass = passed,
            Value = value,
            StoreKey = action.IsResult ? Mps125ResultKeys.For(action.ResultId) : null
        };

        private static async Task<bool> SendCommandsAsync(SK441Device device, string commands)
        {
            foreach (string command in (commands ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                if (!await device.SendMbdCommandAsync(command.Trim(), 0))
                    return false;
            return true;
        }

        private static async Task<bool> SendCommandsOnCurrentConnectionAsync(
            SK441Device device,
            string commands)
        {
            foreach (string command in (commands ?? string.Empty)
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!await device.SendMbdCommandOnCurrentConnectionAsync(
                    command.Trim(),
                    0))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsChapter17StatefulCommand(Mps125ActionDefinition action)
        {
            return action.Chapter == 17 &&
                !string.Equals(
                    action.StepId,
                    "MPS125.CH17.S001.ACTION",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<bool> SetGeneratorAsync(SK441Device device, string argument, bool vbus, bool state)
        {
            double[] values = ParseNumbers(argument);
            if (values.Length < 3)
                return false;
            if (!vbus)
                return await device.SetVbattGeneratorAsync(
                    (float)values[0],
                    (float)values[1],
                    (float)values[2],
                    state);

            if (state)
            {
                if (!device.ResetMbdConnectionBeforeDutPowerOn())
                    return false;
            }
            else if (!await device.CloseTargetBoardCommunicationAsync())
            {
                return false;
            }

            return await device.SetVbusGeneratorAsync(
                (float)values[0],
                (float)values[1],
                (float)values[2],
                state);
        }

        private static string ResolveActionArgument(TestStepItem step, Mps125ActionDefinition action)
        {
            if (step.PlanDefinition != null &&
                !string.IsNullOrWhiteSpace(step.PlanDefinition.ExpectedValue) &&
                (action.Kind == Mps125ActionKind.SetVbusRampStep ||
                 action.Kind == Mps125ActionKind.SetVbus))
            {
                return step.PlanDefinition.ExpectedValue;
            }

            return action.Argument;
        }

        private static int[] ParseRelayList(string value) => (value ?? string.Empty)
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseInt)
            .ToArray();

        private static int ParseInt(string value) => int.Parse((value ?? string.Empty).Trim(), CultureInfo.InvariantCulture);
        private static double[] ParseNumbers(string value) => (value ?? string.Empty)
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => double.Parse(x.Trim(), CultureInfo.InvariantCulture))
            .ToArray();

    }
}
