using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TestPlatform.TestSequences
{
    public sealed class SKMPS250Sequence : SKBcmSequenceBase
    {
        public override string SequenceKey => "SK_MPS250_Sequence";
        public override string ExpectedBoardType => "MPS-250";

        public SKMPS250Sequence(ITestGridService grid = null, Func<string, Task<bool>> confirmAsync = null)
            : base(grid, confirmAsync)
        {
        }

        protected override async Task RunBoardBackgroundMonitorAsync(
            TestSequenceContext context,
            SK441Device device,
            CancellationToken token)
        {
            if (!device.IsMps250PowerTelemetryEnabled())
            {
                OnLogInfo("MPS-250电源遥测已通过App.config关闭。");
                return;
            }

            int intervalMs = device.GetMps250PowerTelemetryIntervalMilliseconds();
            string identity = string.Format(
                CultureInfo.InvariantCulture,
                "通道{0},SN={1}",
                context.ChannelIndex + 1,
                string.IsNullOrWhiteSpace(context.SN) ? "EMPTY" : context.SN);
            OnLogInfo(
                $"MPS-250电源遥测已启动：{identity}，周期={intervalMs}ms；" +
                "记录恒辉8602 VBUS/A与VBATT/B的输出状态、实测电压和实测电流。");

            while (!token.IsCancellationRequested)
            {
                OnLogInfo(
                    $"[MPS250电源遥测][{identity}] " +
                    device.ReadMps250PowerTelemetryLine());
                await Task.Delay(intervalMs, token);
            }
        }

        protected override void AddElectricalTestSteps(List<TestStepItem> steps, TestSequenceContext context, SK441Device device)
        {
            steps.Add(new TestStepItem
            {
                Name = "人工插入USB-B通讯线并确认",
                StepId = "MPS250.PRECHECK.S009.ACTION",
                ExpectedGroupId = Mps250GroupIds.Precheck,
                RowIndex = steps.Count,
                SafetyCritical = true,
                Action = async token =>
                {
                    device.PrepareMpsUsbBManualConnection(ExpectedBoardType);
                    bool confirmed = await ConfirmOperatorActionAsync(
                        "治具已下压到位。\n\n" +
                        "请现在插入 MPS-250 的 USB-B 通讯线。\n" +
                        "确认插接到位后点击“已插入”，也可以直接按 Enter。\n\n" +
                        "按 Esc 或点击“取消”将终止本次测试。",
                        "已插入");
                    if (!confirmed)
                        return new StepResult { IsPass = false, Value = "操作员取消插线" };

                    token.ThrowIfCancellationRequested();
                    return new StepResult { IsPass = true, Value = "USB-B已插入" };
                }
            });
            AssignPlanGroupToRange(steps, context, Mps250GroupIds.Precheck, 0, steps.Count);
            IReadOnlyList<Mps250ActionDefinition> catalog = Mps250ActionCatalog.Create();

            foreach (IGrouping<int, Mps250ActionDefinition> chapter in catalog
                .Where(x => !x.AlwaysRun)
                .GroupBy(x => x.Chapter)
                .OrderBy(x => x.Key))
            {
                string groupId = Mps250GroupIds.Chapter(chapter.Key);
                AddPlanChapter(steps, context, groupId, () =>
                {
                    foreach (Mps250ActionDefinition action in chapter)
                        AddCatalogStep(steps, context, device, action, groupId);
                });
            }

            AddPlanChapter(steps, context, Mps250GroupIds.Cleanup, () =>
            {
                foreach (Mps250ActionDefinition action in catalog.Where(x => x.AlwaysRun))
                    AddCatalogStep(steps, context, device, action, Mps250GroupIds.Cleanup);
            });
        }

        private void AddCatalogStep(IList<TestStepItem> steps, TestSequenceContext context, SK441Device device, Mps250ActionDefinition action, string groupId)
        {
            var step = new TestStepItem
            {
                Name = action.Name,
                StepId = action.StepId,
                ExpectedGroupId = groupId,
                RowIndex = steps.Count,
                AlwaysRun = action.AlwaysRun,
                SafetyCritical = IsSafetyCritical(action) && !IsFinalChapterResult(action),
                PowerCritical = IsPowerCritical(action) && !IsFinalChapterResult(action),
                IsFixtureReleaseStep = action.AlwaysRun && action.Kind == Mps250ActionKind.Cleanup
            };
            step.Action = token => ExecuteActionAsync(step, context, device, action, token);
            steps.Add(step);
        }

        private async Task<StepResult> ExecuteActionAsync(TestStepItem step, TestSequenceContext context, SK441Device device, Mps250ActionDefinition action, CancellationToken token)
        {
            string faultStep = Environment.GetEnvironmentVariable("TESTPLATFORM_MPS250_FAULT_STEP");
            if (device.SkipComInit && !string.IsNullOrWhiteSpace(faultStep) &&
                string.Equals(faultStep, action.StepId, StringComparison.OrdinalIgnoreCase))
                return BasicResult(false, "Injected fault", action);

            if (action.Kind == Mps250ActionKind.Delay)
            {
                int delayMs = GetPlanDelayMs(new[] { step }, 0, 0);
                await Task.Delay(device.SkipComInit ? Math.Min(delayMs, 1) : delayMs, token);
                return BasicResult(true, delayMs + " ms", action);
            }

            if (action.IsResult)
                return await ExecuteResultAsync(step, context, device, action, token);

            bool passed;
            switch (action.Kind)
            {
                case Mps250ActionKind.CheckInstruments:
                    passed = await device.CheckMpsRequiredInstrumentsAsync();
                    break;
                case Mps250ActionKind.AllOutputsOff:
                    passed = await device.TurnOffAllInstrumentsAsync();
                    break;
                case Mps250ActionKind.DaqRelays:
                    passed = await SetMpsRelaysAsync(device, ParseRelayList(action.Argument), action.State);
                    break;
                case Mps250ActionKind.CheckMbdCommunication:
                    passed = await device.CheckMbdCommunicationAsync();
                    break;
                case Mps250ActionKind.CloseMbdCommunication:
                    passed = await device.CloseTargetBoardCommunicationAsync();
                    break;
                case Mps250ActionKind.MbdCommand:
                    passed = await SendCommandsAsync(device, action.Argument);
                    break;
                case Mps250ActionKind.MbdCleanupIfConnected:
                    if (HasFixtureReleaseStarted || device.IsEmergencyStopLatched)
                    {
                        await device.CloseTargetBoardCommunicationAsync();
                        OnLogWarning(
                            "MPS-250治具已开始释放或急停已触发，USB-B会随下压解除而断开；" +
                            "跳过MBD安全关闭命令，不再重新枚举串口。");
                        return BasicResult(true, "治具已释放，跳过MBD命令", action);
                    }
                    if (!device.IsTargetBoardCommunicationConnected)
                    {
                        OnLogWarning(
                            "MPS-250目标板通信当前未连接；跳过MBD安全关闭命令，" +
                            "不重新枚举串口。硬件输出与继电器收尾继续执行。");
                        return BasicResult(true, "USB已断开，跳过MBD命令", action);
                    }
                    passed = await device.SendMbdCleanupCommandsOnCurrentConnectionAsync(
                        action.Argument);
                    break;
                case Mps250ActionKind.SetVbus:
                    passed = await SetGeneratorAsync(device, action.Argument, true, action.State);
                    break;
                case Mps250ActionKind.SetVbusAndWaitStable:
                    {
                        double[] values = ParseNumbers(action.Argument);
                        passed = values.Length >= 1 &&
                            await SetGeneratorAsync(device, action.Argument, true, true);
                        if (passed)
                        {
                            passed = await device.WaitForMps250VbusStableAsync(
                                (float)values[0],
                                token);
                        }
                        break;
                    }
                case Mps250ActionKind.SetVbatt:
                    passed = await SetGeneratorAsync(device, action.Argument, false, action.State);
                    break;
                case Mps250ActionKind.SetVbattAndWaitStable:
                    {
                        double[] values = ParseNumbers(action.Argument);
                        passed = values.Length >= 1 &&
                            await SetGeneratorAsync(device, action.Argument, false, action.State);
                        if (passed)
                        {
                            passed = await device.WaitForMps250VbattVoltageAsync(
                                (float)values[0],
                                token);
                        }
                        break;
                    }
                case Mps250ActionKind.SafeVbusRelay:
                    {
                        double[] values = ParseNumbers(action.Argument);
                        passed = values.Length >= 5 && await device.SafeSwitchMpsVbusRelayAsync(
                            (int)values[0],
                            Mps250StationMap.GetDaqRelayChannels((int)values[0]),
                            action.State,
                            (float)values[1],
                            (float)values[2],
                            (float)values[3],
                            values[4] > 0,
                            token);
                        break;
                    }
                case Mps250ActionKind.SafeVbattConnect:
                    passed = await device.SafeConnectMpsVbattRelayAsync(
                        ParseInt(action.Argument),
                        Mps250StationMap.GetDaqRelayChannels(ParseInt(action.Argument)),
                        300,
                        token);
                    break;
                case Mps250ActionKind.SafeVbattDisconnect:
                    passed = await device.SafeDisconnectMpsVbattRelayAsync(
                        ParseInt(action.Argument),
                        Mps250StationMap.GetDaqRelayChannels(ParseInt(action.Argument)),
                        300);
                    break;
                case Mps250ActionKind.DaqVoltage:
                    await device.MeasureDaqChannelVoltageAsync(ParseInt(action.Argument));
                    passed = true;
                    break;
                case Mps250ActionKind.DocumentedBoundary:
                    passed = true;
                    break;
                case Mps250ActionKind.Cleanup:
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

        private async Task<StepResult> ExecuteResultAsync(TestStepItem step, TestSequenceContext context, SK441Device device, Mps250ActionDefinition action, CancellationToken token)
        {
            TestPlanStepDefinition plan = step.PlanDefinition;
            if (plan == null)
                throw new TestPlanConfigurationException("No XML definition for " + action.StepId);

            foreach (string dependency in action.RequiresResultIds)
            {
                double ignored;
                if (!TryGetStoredNumericValue(context, Mps250ResultKeys.For(dependency), out ignored))
                    return BasicResult(false, "Missing dependency " + dependency, action);
            }

            if (action.Kind == Mps250ActionKind.FirmwareVersion)
            {
                string expected = string.IsNullOrWhiteSpace(plan.ExpectedValue) ? "mcu_app U.1.1.4" : plan.ExpectedValue;
                string response = device.SkipComInit ? expected : await device.QueryMbdCommandAsync(action.Argument, 0);
                string actual = device.SkipComInit
                    ? response
                    : MbdResponseParser.ParsePayloadText(action.Argument, response);
                return new StepResult
                {
                    IsPass = string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                    Value = actual ?? string.Empty,
                    StoreKey = Mps250ResultKeys.For(action.ResultId),
                    Unit = plan.Unit
                };
            }

            if (action.Kind == Mps250ActionKind.DaqAverageVoltageAfterVbattBaseline ||
                action.Kind == Mps250ActionKind.DaqTimedWindowAverageVoltageAfterVbattBaseline)
            {
                int relayId = ParseInt(action.Argument);
                bool baselineReady = await device.PrepareMpsVbattBaselineAsync(
                    relayId,
                    Mps250StationMap.GetDaqRelayChannels(relayId),
                    300,
                    token);
                if (!baselineReady)
                {
                    return new StepResult
                    {
                        IsPass = false,
                        Value = "VBATT基线条件不成立",
                        StoreKey = Mps250ResultKeys.For(action.ResultId),
                        Unit = plan.Unit
                    };
                }
            }

            double value = device.SkipComInit
                ? SimulationValue(action.ResultId, plan.LowerLimit, plan.UpperLimit)
                : await ReadHardwareValueAsync(device, context, action, plan.Unit, token);
            bool passed = (!plan.LowerLimit.HasValue || value >= plan.LowerLimit.Value) &&
                          (!plan.UpperLimit.HasValue || value <= plan.UpperLimit.Value);

            if (string.Equals(action.ResultId, "H06", StringComparison.OrdinalIgnoreCase))
            {
                double baseline;
                passed = passed && TryGetStoredNumericValue(context, Mps250ResultKeys.For("H02"), out baseline) && value > baseline;
            }

            return new StepResult
            {
                IsPass = passed,
                Value = value.ToString("0.###", CultureInfo.InvariantCulture),
                NumericValue = value,
                StoreKey = Mps250ResultKeys.For(action.ResultId),
                Unit = plan.Unit
            };
        }

        private async Task<double> ReadHardwareValueAsync(SK441Device device, TestSequenceContext context, Mps250ActionDefinition action, string unit, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            switch (action.Kind)
            {
                case Mps250ActionKind.DaqVoltage:
                case Mps250ActionKind.DaqAverageVoltage:
                case Mps250ActionKind.DaqAverageVoltageAfterVbattBaseline:
                    return ScaleVoltage(await ReadDaqAverageAsync(device, action.Channel, ResolveAverageSampleCount(action), action.SampleIntervalMs, token), unit);
                case Mps250ActionKind.DaqTimedWindowAverageVoltage:
                case Mps250ActionKind.DaqTimedWindowAverageVoltageAfterVbattBaseline:
                    return ScaleVoltage(
                        await device.MeasureDaqChannelVoltageWindowAverageAsync(
                            action.Channel,
                            (Math.Max(2, action.SampleCount) - 1) * action.SampleIntervalMs,
                            action.SampleIntervalMs,
                            token),
                        unit);
                case Mps250ActionKind.DaqShuntCurrent:
                    return await ReadDaqAverageAsync(device, action.Channel, ResolveAverageSampleCount(action), action.SampleIntervalMs, token) / action.Scale * 1000.0;
                case Mps250ActionKind.DaqResistance:
                    return await device.MeasureDaqChannelResistanceAsync(action.Channel);
                case Mps250ActionKind.IgnitionRamp:
                    return await RunIgnitionRampAsync(device, action, token);
                case Mps250ActionKind.ComputedGain:
                    return await ComputeGainAsync(device, context, action);
                default:
                    return await ReadMbdAverageAsync(device, action.Argument, ResolveAverageSampleCount(action), action.SampleIntervalMs, token);
            }
        }

        private static bool IsPowerCritical(Mps250ActionDefinition action)
        {
            if (action.AlwaysRun)
                return false;
            switch (action.Kind)
            {
                case Mps250ActionKind.SetVbus:
                case Mps250ActionKind.SetVbusAndWaitStable:
                case Mps250ActionKind.SetVbatt:
                case Mps250ActionKind.SetVbattAndWaitStable:
                case Mps250ActionKind.SafeVbusRelay:
                case Mps250ActionKind.SafeVbattConnect:
                case Mps250ActionKind.SafeVbattDisconnect:
                case Mps250ActionKind.DaqRelays:
                case Mps250ActionKind.DaqVoltage:
                case Mps250ActionKind.DaqAverageVoltage:
                case Mps250ActionKind.DaqAverageVoltageAfterVbattBaseline:
                case Mps250ActionKind.DaqTimedWindowAverageVoltage:
                case Mps250ActionKind.DaqTimedWindowAverageVoltageAfterVbattBaseline:
                case Mps250ActionKind.DaqShuntCurrent:
                case Mps250ActionKind.DaqResistance:
                case Mps250ActionKind.IgnitionRamp:
                case Mps250ActionKind.ComputedGain:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsSafetyCritical(Mps250ActionDefinition action)
        {
            if (action.AlwaysRun)
                return false;

            if (action.Kind == Mps250ActionKind.SafeVbattConnect ||
                action.Kind == Mps250ActionKind.SetVbattAndWaitStable ||
                action.Kind == Mps250ActionKind.DaqAverageVoltageAfterVbattBaseline)
            {
                return true;
            }

            return false;
        }

        private static bool IsFinalChapterResult(Mps250ActionDefinition action)
        {
            return action.IsResult && (action.Chapter == 16 || action.Chapter == 17);
        }

        private static int ResolveAverageSampleCount(Mps250ActionDefinition action)
        {
            if (action.SampleCount != 16 || IsPwmSamplingChapter(action.Chapter))
                return Math.Max(1, action.SampleCount);

            int configured;
            return int.TryParse(
                       System.Configuration.ConfigurationManager.AppSettings["SKMPSNonPwmAverageSampleCount"],
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out configured) && configured > 0
                ? configured
                : 16;
        }

        private static bool IsPwmSamplingChapter(int chapter)
        {
            return chapter == 11 || chapter == 13 || chapter == 16 || chapter == 17;
        }

        private static async Task<bool> SetMpsRelaysAsync(SK441Device device, int[] ids, bool state)
        {
            bool passed = true;
            foreach (int id in ids)
                passed = await device.ControlMpsRelayAsync(
                    id,
                    Mps250StationMap.GetDaqRelayChannels(id),
                    state) && passed;
            return passed;
        }

        private static async Task<double> ReadDaqAverageAsync(SK441Device device, int channel, int count, int intervalMs, CancellationToken token)
        {
            return await device.MeasureDaqChannelVoltageAverageAsync(
                channel,
                Math.Max(1, count),
                intervalMs,
                token);
        }

        private static async Task<double> ReadMbdAverageAsync(SK441Device device, string command, int count, int intervalMs, CancellationToken token)
        {
            double sum = 0;
            int samples = Math.Max(1, count);
            for (int i = 0; i < samples; i++)
            {
                token.ThrowIfCancellationRequested();
                sum += MbdResponseParser.ParseScalar(
                    command,
                    await device.QueryMbdCommandAsync(command, 0));
                if (intervalMs > 0 && i + 1 < samples)
                    await Task.Delay(intervalMs, token);
            }
            return sum / samples;
        }

        private static async Task<double> RunIgnitionRampAsync(SK441Device device, Mps250ActionDefinition action, CancellationToken token)
        {
            double[] values = ParseNumbers(action.Argument);
            double start = values.Length > 0 ? values[0] : 5;
            double stop = values.Length > 1 ? values[1] : 80;
            double increment = values.Length > 2 ? values[2] : 1;
            for (double voltage = start; voltage <= stop; voltage += increment)
            {
                token.ThrowIfCancellationRequested();
                if (!await device.SetVbusGeneratorAsync((float)voltage, 0.5f, 300, true))
                    throw new InvalidOperationException("VBUS ramp setting failed.");
                double fiveVolt = await device.MeasureDaqChannelVoltageAsync(action.Channel);
                if (fiveVolt > 4.8)
                    return voltage * 1000.0;
                await Task.Delay(100, token);
            }
            return stop * 1000.0;
        }

        private static async Task<double> ComputeGainAsync(SK441Device device, TestSequenceContext context, Mps250ActionDefinition action)
        {
            double adc;
            if (!TryGetStored(context, action.Argument, out adc) || Math.Abs(adc) < 0.000001)
                throw new InvalidOperationException("Missing/zero MPS-250 gain dependency " + action.Argument);
            double vbusMv = await device.MeasureDaqChannelVoltageAsync(action.Channel) * 1000.0;
            return 4096.0 * vbusMv / adc;
        }

        private static bool TryGetStored(TestSequenceContext context, string resultId, out double value)
        {
            value = 0;
            return context != null &&
                context.TryGetNumericValue(Mps250ResultKeys.For(resultId), out value);
        }

        private static double ScaleVoltage(double volts, string unit) => unit != null && unit.IndexOf("mV", StringComparison.OrdinalIgnoreCase) >= 0 ? volts * 1000.0 : volts;

        private static double SimulationValue(string resultId, double? lower, double? upper)
        {
            if (string.Equals(resultId, "H02", StringComparison.OrdinalIgnoreCase))
                return lower.HasValue && upper.HasValue
                    ? lower.Value + ((upper.Value - lower.Value) * 0.25)
                    : 25.0;
            if (lower.HasValue && upper.HasValue)
                return lower.Value == upper.Value ? lower.Value : lower.Value + ((upper.Value - lower.Value) * 0.5);
            if (lower.HasValue)
                return lower.Value;
            if (upper.HasValue)
                return upper.Value;
            return 0.0;
        }

        private static StepResult BasicResult(bool passed, string value, Mps250ActionDefinition action) => new StepResult
        {
            IsPass = passed,
            Value = value,
            StoreKey = action.IsResult ? Mps250ResultKeys.For(action.ResultId) : null
        };

        private static async Task<bool> SendCommandsAsync(SK441Device device, string commands)
        {
            foreach (string command in (commands ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                if (!await device.SendMbdCommandAsync(command.Trim(), 0))
                    return false;
            return true;
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

            // Never keep a SerialPort handle across a DUT power edge. A COM name can
            // remain enumerated even though the previous USB endpoint is no longer usable.
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
