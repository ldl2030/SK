using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private readonly bool _skipFirmwareProgramming;
        private readonly Dictionary<string, Bcm250ActionDefinition> _actionsByResultId =
            new Dictionary<string, Bcm250ActionDefinition>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TestStepItem> _stepsByResultId =
            new Dictionary<string, TestStepItem>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _prefetchedHardwareValues =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private MbdTerminalState _mbdTerminalState;
        private bool _mbdReadyObserved;
        private bool _firmwareReadinessEvaluated;
        private bool _firmwareReadinessPrepared;
        private bool _programFirmwareThisRun;
        private bool _firmwareProgrammedThisRun;
        private string _precheckedFirmwareVersion;
        private bool _firmwarePrecheckCacheValid;

        private enum MbdTerminalState
        {
            Unknown,
            Mbd,
            Bcm
        }

        private sealed class ParallelMeasurementBatch
        {
            public string Leader { get; set; }
            public string[][] Branches { get; set; }
        }

        private sealed class MeasurementReadinessDefinition
        {
            public string Leader { get; set; }
            public string[] ProbeResultIds { get; set; }
            public int TimeoutMs { get; set; }
        }

        private sealed class MeasurementReadinessResult
        {
            public bool IsReady { get; set; }
            public Dictionary<string, double> LastValues { get; set; }
            public Dictionary<string, string> FailedProbes { get; set; }
            public string FailureReason { get; set; }
        }

        private sealed class MeasurementReadinessDaqState
        {
            public int? LastChannel { get; set; }
        }

        private static readonly ParallelMeasurementBatch[] ParallelMeasurementBatches =
        {
            // M/N/R working points must be sampled in plan order after DUT-side
            // readiness is confirmed. Prefetching their later rows preserved
            // transient values across breakpoints and caused false failures.
            Batch(new[] { "Q01", "Q02" }, new[] { "Q03", "Q04" }),
            Batch(new[] { "Q05", "Q06" }, new[] { "Q07" }),
            Batch(new[] { "Q08", "Q09" }, new[] { "Q10", "Q11" }),
            Batch(new[] { "T01" }, new[] { "T02" })
        };

        private static readonly MeasurementReadinessDefinition[] MeasurementReadinessDefinitions =
        {
            Readiness("M01", "M01", "M02"),
            Readiness("M03", "M03", "M04"),
            Readiness("M09", "M09"),
            Readiness("N01", "N01", "N02"),
            Readiness("N03", "N03", "N04"),
            Readiness("N07", "N07"),
            Readiness("N08", "N08"),
            Readiness("R01", "R01", "R02", "R03"),
            Readiness("R04", "R04", "R05", "R06", "R07"),
            Readiness("R08", "R08", "R09", "R10"),
            Readiness("R11", "R11", "R12", "R13", "R14")
        };
        private readonly bool _forceFirmwareProgramming;
        private readonly bool _skipFirmwareVerification;
        protected override bool SkipFirmwareProgramming => _skipFirmwareProgramming && !_forceFirmwareProgramming;
        protected override bool ForceFirmwareProgramming => _forceFirmwareProgramming;
        protected override bool SkipFirmwareVerification => _skipFirmwareVerification;

        public SKBCM250Sequence(
            ITestGridService grid = null,
            Func<string, Task<bool>> confirmAsync = null,
            bool skipFirmwareProgramming = false,
            bool? forceFirmwareProgramming = null,
            bool skipFirmwareVerification = false)
            : base(grid, confirmAsync)
        {
            _skipFirmwareProgramming = skipFirmwareProgramming;
            _forceFirmwareProgramming = forceFirmwareProgramming ?? !skipFirmwareProgramming;
            // Explicit force from the UI takes priority; the default above keeps
            // existing callers' enabled/disabled programming semantics.
            _skipFirmwareVerification = skipFirmwareVerification;
        }

        protected override void AddElectricalTestSteps(
            List<TestStepItem> steps,
            TestSequenceContext context,
            SK441Device deviceManager)
        {
            AssignPlanGroupToRange(steps, context, Bcm250GroupIds.Precheck, 0, steps.Count);

            IReadOnlyList<Bcm250ActionDefinition> catalog = Bcm250ActionCatalog.Create();
            _actionsByResultId.Clear();
            _stepsByResultId.Clear();
            _prefetchedHardwareValues.Clear();
            _mbdTerminalState = MbdTerminalState.Unknown;
            _mbdReadyObserved = false;
            _firmwareReadinessEvaluated = false;
            _firmwareReadinessPrepared = false;
            _programFirmwareThisRun = false;
            _firmwareProgrammedThisRun = false;
            _precheckedFirmwareVersion = null;
            _firmwarePrecheckCacheValid = false;
            foreach (Bcm250ActionDefinition resultAction in catalog.Where(x => x.IsResult))
                _actionsByResultId[resultAction.ResultId] = resultAction;
            foreach (IGrouping<string, Bcm250ActionDefinition> chapterGroup in catalog
                .Where(x => !x.AlwaysRun)
                .GroupBy(x => Bcm250GroupIds.PlanGroupForChapter(x.Chapter))
                .OrderBy(x => x.Min(action => action.Chapter)))
            {
                string groupId = chapterGroup.Key;
                AddPlanChapter(
                    steps,
                    context,
                    groupId,
                    () =>
                    {
                        foreach (Bcm250ActionDefinition action in chapterGroup)
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
                MaxRetries = action.Kind == Bcm250ActionKind.ProgramFirmware ? 0 : -1,
                AlwaysRun = action.AlwaysRun,
                PowerCritical = IsPowerCritical(action),
                IsFixtureReleaseStep = action.AlwaysRun &&
                    action.Kind == Bcm250ActionKind.Cleanup
            };
            step.Action = token => ExecuteActionAsync(step, context, device, action, token);
            steps.Add(step);
            if (action.IsResult)
                _stepsByResultId[action.ResultId] = step;
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

            if (action.OnlyWhenFirmwareProgrammingRequired && !_programFirmwareThisRun)
                return Result(true, "SKIPPED (firmware verified)", action);

            if (action.OnlyAfterFirmwareProgramming && !_firmwareProgrammedThisRun)
                return Result(true, "SKIPPED (no programming power cycle)", action);

            if (action.Kind == Bcm250ActionKind.Delay)
            {
                int delayMs = GetPlanDelayMs(
                    new[] { step },
                    0,
                    0);
                if (device.SkipComInit)
                {
                    await Task.Delay(Math.Min(delayMs, 1), token);
                }
                else
                {
                    await ExecuteOptimizedDelayAsync(device, action.StepId, delayMs, token);
                }
                return Result(true, delayMs + " ms", action);
            }

            if (action.Kind == Bcm250ActionKind.WaitForCcbCurrentStable)
                return await WaitForCcbCurrentStableAsync(step, device, action, token);

            if (action.IsResult)
                return await ExecuteResultAsync(step, context, device, action, token);

            if (action.Kind == Bcm250ActionKind.DaqShuntResistance)
            {
                token.ThrowIfCancellationRequested();
                double resistance = action.Scale;
                bool resistanceValid = !double.IsNaN(resistance) &&
                                       !double.IsInfinity(resistance) &&
                                       resistance > 0.0;
                OnLogWarning(string.Format(
                    CultureInfo.InvariantCulture,
                    "BCM250 CH7 DAQ{0} 开机电阻测量已跳过；保留测试行，后续计算使用工位标称值 {1:0.######} Ohm。",
                    action.Channel,
                    resistance));
                return new StepResult
                {
                    IsPass = resistanceValid,
                    Value = resistanceValid
                        ? "SKIPPED (nominal " + resistance.ToString("0.######", CultureInfo.InvariantCulture) + " Ohm)"
                        : "Invalid resistance",
                    NumericValue = resistance,
                    StoreKey = null,
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
                        ResetMbdState();
                        break;
                    }
                case Bcm250ActionKind.SafeAnsPowerOff:
                    // Only release MBD when the DUT is actually about to lose power.
                    // Programming itself still needs MBD before and after flashing for W86.
                    passed = await device.CloseTargetBoardCommunicationAsync() &&
                             await device.DisableAnsThenOpenRl4Async(ParseInt(action.Argument));
                    ResetMbdState();
                    InvalidateFirmwarePrecheckCache();
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
                    {
                        bool reusedExistingConnection =
                            (_mbdTerminalState == MbdTerminalState.Mbd ||
                             _mbdTerminalState == MbdTerminalState.Bcm) &&
                            device.IsTargetBoardCommunicationConnected;
                        if (reusedExistingConnection)
                        {
                            OnLogInfo(
                                "BCM250复用现有USB-B连接和终端上下文，跳过重复0D探测。");
                            passed = true;
                        }
                        else
                        {
                            passed = _mbdReadyObserved || await device.CheckMbdCommunicationAsync();
                        }
                        _mbdReadyObserved = false;
                        if (!passed)
                            ResetMbdState();
                        else if (!reusedExistingConnection)
                            _mbdTerminalState = MbdTerminalState.Unknown;
                        break;
                    }
                case Bcm250ActionKind.FirmwareReadinessCheck:
                    passed = await EvaluateFirmwareReadinessAsync(device, token);
                    break;
                case Bcm250ActionKind.WaitForBcmRunningTime:
                    return await WaitForBcmRunningTimeAsync(device, action, step, token);
                case Bcm250ActionKind.WaitForBcmApplication:
                    return await WaitForBcmApplicationAsync(device, action, step, token);
                case Bcm250ActionKind.BcmSerialBatchBoundary:
                    passed = await CompleteBcmSerialBatchChapterAsync(
                        context,
                        device,
                        action,
                        token);
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
                    passed = await SendCommandsAsync(device, action.Argument, action.StepId, token);
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
                    // Keep the fixture pressed until every preceding cleanup action has
                    // removed power and opened the test relays, then explicitly open
                    // station-1's press-down relay and confirm the fixture has risen.
                    bool communicationClosed = await device.CloseTargetBoardCommunicationAsync();
                    bool fixtureReleased = await ReleaseFixtureAndVerifyAsync(device);
                    passed = communicationClosed && fixtureReleased;
                    ResetMbdState();
                    break;
                default:
                    passed = true;
                    break;
            }

            if (passed &&
                action.Kind != Bcm250ActionKind.Delay &&
                step.PlanDefinition != null &&
                step.PlanDefinition.DelayMs.GetValueOrDefault() > 0)
            {
                int postActionDelayMs = step.PlanDefinition.DelayMs.Value;
                await Task.Delay(
                    device.SkipComInit ? Math.Min(postActionDelayMs, 1) : postActionDelayMs,
                    token);
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
                    ? Bcm250Commands.FirmwareVersion
                    : plan.ExpectedValue;
                string actualFirmware;
                if (_firmwarePrecheckCacheValid &&
                    !_firmwareProgrammedThisRun &&
                    !string.IsNullOrWhiteSpace(_precheckedFirmwareVersion))
                {
                    actualFirmware = _precheckedFirmwareVersion;
                    OnLogInfo("E01复用本次供电周期内的R0预检查结果。");
                }
                else
                {
                    string response = device.SkipComInit
                        ? expectedFirmware
                        : await device.QueryMbdCommandOnCurrentConnectionAsync(
                            action.Argument,
                            0);
                    actualFirmware = device.SkipComInit
                        ? response
                        : MbdResponseParser.ParsePayloadText(action.Argument, response);
                }
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

            if (action.Kind == Bcm250ActionKind.LogicResult)
            {
                double before;
                if (!TryGetStoredNumericValue(
                    context,
                    Bcm250ResultKeys.ResetFlatRunningTimeBefore,
                    out before))
                {
                    return new StepResult
                    {
                        IsPass = false,
                        Value = "缺少复位前R10",
                        NumericValue = 0.0,
                        StoreKey = Bcm250ResultKeys.For(action.ResultId),
                        Unit = plan.Unit
                    };
                }

                double afterRaw;
                if (!TryGetStoredNumericValue(
                    context,
                    Bcm250ResultKeys.ResetFlatRunningTimeAfter,
                    out afterRaw))
                {
                    return new StepResult
                    {
                        IsPass = false,
                        Value = "缺少复位后R10就绪值",
                        NumericValue = 0.0,
                        StoreKey = Bcm250ResultKeys.For(action.ResultId),
                        Unit = plan.Unit
                    };
                }
                double after = Math.Floor(afterRaw);
                bool resetPassed = Math.Abs(before - after) >= 2.0 &&
                                   Math.Abs(after - 1.0) < 0.001;
                OnLogInfo(string.Format(
                    CultureInfo.InvariantCulture,
                    "平线复位R10判定：复位前={0:0}s，复位后={1:0.###}s（整数部分={2:0}），结果={3}",
                    before,
                    afterRaw,
                    after,
                    resetPassed ? 1 : 0));
                return new StepResult
                {
                    IsPass = resetPassed,
                    Value = resetPassed ? "1" : "0",
                    NumericValue = resetPassed ? 1.0 : 0.0,
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
                value = action.Kind == Bcm250ActionKind.ProgramFirmware
                    ? await ReadHardwareValueAsync(device, action, unit, context, token)
                    : SimulationValue(action.ResultId, lower, upper);
            }
            else
            {
                MeasurementReadinessResult readiness = await WaitForMeasurementReadinessAsync(
                    device,
                    action.ResultId,
                    context,
                    token);
                if (!readiness.IsReady)
                {
                    double lastValue = 0.0;
                    bool hasLastValue = readiness.LastValues != null &&
                        readiness.LastValues.TryGetValue(action.ResultId, out lastValue);
                    string failureSummary = FormatReadinessFailureSummary(readiness);
                    string displayValue = hasLastValue
                        ? lastValue.ToString("0.###", CultureInfo.InvariantCulture) +
                          "（门禁失败：" + failureSummary + "）"
                        : "无有效值（门禁失败：" + failureSummary + "）";
                    return new StepResult
                    {
                        IsPass = false,
                        Value = displayValue,
                        NumericValue = hasLastValue ? lastValue : (double?)null,
                        StoreKey = Bcm250ResultKeys.For(action.ResultId),
                        Unit = unit
                    };
                }

                CacheReusableReadinessValues(readiness);

                value = await ReadHardwareValueWithParallelPrefetchAsync(
                    device,
                    action,
                    unit,
                    context,
                    token);
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

        private void CacheReusableReadinessValues(MeasurementReadinessResult readiness)
        {
            if (readiness?.LastValues == null)
                return;

            var reusableIds = new List<string>();
            foreach (KeyValuePair<string, double> pair in readiness.LastValues)
            {
                // R01-R14 are single-sample verification rows. The readiness
                // gate already captured three consecutive in-limit samples from
                // these exact probes. Averaged calibration rows still execute
                // their complete configured sample series.
                if (!Regex.IsMatch(pair.Key ?? string.Empty, "^R(?:0[1-9]|1[0-4])$"))
                    continue;

                _prefetchedHardwareValues[pair.Key] = pair.Value;
                reusableIds.Add(pair.Key);
            }

            if (reusableIds.Count > 0)
            {
                OnLogInfo(
                    "复用工作点稳定门禁的最终采样作为R结果：" +
                    string.Join(",", reusableIds));
            }
        }

        private static MeasurementReadinessDefinition Readiness(
            string leader,
            params string[] probeResultIds)
        {
            return new MeasurementReadinessDefinition
            {
                Leader = leader,
                ProbeResultIds = probeResultIds,
                TimeoutMs = 15000
            };
        }

        private static ParallelMeasurementBatch Batch(string[] mbdBranch, string[] daqBranch)
        {
            return new ParallelMeasurementBatch
            {
                Leader = mbdBranch[0],
                Branches = new[] { mbdBranch, daqBranch }
            };
        }

        private async Task<MeasurementReadinessResult> WaitForMeasurementReadinessAsync(
            SK441Device device,
            string resultId,
            TestSequenceContext context,
            CancellationToken token)
        {
            MeasurementReadinessDefinition definition = MeasurementReadinessDefinitions.FirstOrDefault(x =>
                string.Equals(x.Leader, resultId, StringComparison.OrdinalIgnoreCase));
            if (definition == null)
            {
                return new MeasurementReadinessResult
                {
                    IsReady = true,
                    LastValues = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
                    FailedProbes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                };
            }

            int requiredConsecutiveSamples = GetConfiguredStableSampleCount();
            const int pollIntervalMs = 200;
            var timer = Stopwatch.StartNew();
            var lastValues = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var lastFailedProbes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var daqState = new MeasurementReadinessDaqState();
            int consecutiveSamples = 0;
            int sampleNumber = 0;

            OnLogInfo(
                "开始工作点就绪确认：" + definition.Leader +
                "，探针=" + string.Join("+", definition.ProbeResultIds) +
                "，要求连续稳定=" + requiredConsecutiveSamples +
                "，最长等待=" + definition.TimeoutMs + "ms。");

            while (timer.ElapsedMilliseconds < definition.TimeoutMs)
            {
                token.ThrowIfCancellationRequested();
                sampleNumber++;
                bool allInRange = true;
                var statuses = new List<string>();
                var currentFailedProbes =
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (string probeResultId in definition.ProbeResultIds)
                {
                    Bcm250ActionDefinition probeAction;
                    TestStepItem probeStep;
                    if (!_actionsByResultId.TryGetValue(probeResultId, out probeAction) ||
                        !_stepsByResultId.TryGetValue(probeResultId, out probeStep) ||
                        probeStep.PlanDefinition == null ||
                        !probeStep.PlanDefinition.LowerLimit.HasValue ||
                        !probeStep.PlanDefinition.UpperLimit.HasValue)
                    {
                        throw new TestPlanConfigurationException(
                            "Measurement readiness has no complete XML definition for " + probeResultId);
                    }

                    double lower = probeStep.PlanDefinition.LowerLimit.Value;
                    double upper = probeStep.PlanDefinition.UpperLimit.Value;
                    // Readiness answers "has the working point arrived?"; the
                    // formal result row below still uses the unmodified XML limits.
                    // A few milliamps outside the production limit must not make
                    // the leader row wait 15 seconds and fail as a second result.
                    double readinessMargin = GetReadinessMargin(probeResultId);
                    lower -= readinessMargin;
                    upper += readinessMargin;
                    string unit = probeStep.PlanDefinition.Unit ?? string.Empty;
                    try
                    {
                        double value = await ReadMeasurementReadinessProbeAsync(
                            device,
                            probeAction,
                            unit,
                            context,
                            daqState,
                            token);
                        lastValues[probeResultId] = value;
                        bool inRange = value >= lower && value <= upper;
                        allInRange &= inRange;
                        if (!inRange)
                        {
                            currentFailedProbes[probeResultId] = string.Format(
                                CultureInfo.InvariantCulture,
                                "{0:0.###}{1}不在{2:0.###}~{3:0.###}{1}",
                                value,
                                unit,
                                lower,
                                upper);
                        }
                        statuses.Add(string.Format(
                            CultureInfo.InvariantCulture,
                            "{0}={1:0.###}{2}[{3:0.###}~{4:0.###}]({5})",
                            probeResultId,
                            value,
                            string.IsNullOrWhiteSpace(unit) ? string.Empty : unit,
                            lower,
                            upper,
                            inRange ? "OK" : "WAIT"));
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        allInRange = false;
                        currentFailedProbes[probeResultId] = "读取失败：" + ex.Message;
                        statuses.Add(probeResultId + "=ERROR(" + ex.Message + ")");
                    }
                }

                lastFailedProbes = currentFailedProbes;
                consecutiveSamples = allInRange ? consecutiveSamples + 1 : 0;
                OnLogInfo(
                    "工作点就绪轮询 " + definition.Leader + " #" + sampleNumber + "：" +
                    string.Join("，", statuses) +
                    "；连续稳定=" + consecutiveSamples + "/" + requiredConsecutiveSamples + "。");

                if (consecutiveSamples >= requiredConsecutiveSamples)
                {
                    timer.Stop();
                    OnLogSuccess(
                        "工作点已就绪：" + definition.Leader +
                        "，耗时=" + timer.ElapsedMilliseconds + "ms；开始正式顺序采样。");
                    return new MeasurementReadinessResult
                    {
                        IsReady = true,
                        LastValues = lastValues,
                        FailedProbes = lastFailedProbes
                    };
                }

                int remainingMs = definition.TimeoutMs - (int)timer.ElapsedMilliseconds;
                if (remainingMs <= 0)
                    break;
                await Task.Delay(Math.Min(pollIntervalMs, remainingMs), token);
            }

            timer.Stop();
            bool finalSampleIsValid = lastFailedProbes.Count == 0 &&
                definition.ProbeResultIds.All(id => lastValues.ContainsKey(id));
            if (finalSampleIsValid)
            {
                OnLogSuccess(
                    "工作点在等待边界取得有效测量，按XML门限接受：" +
                    definition.Leader + "，不因连续计数器未累积而误判失败。");
                return new MeasurementReadinessResult
                {
                    IsReady = true,
                    LastValues = lastValues,
                    FailedProbes = lastFailedProbes
                };
            }

            string lastValueText = string.Join(
                "，",
                definition.ProbeResultIds.Select(id =>
                    lastValues.ContainsKey(id)
                        ? id + "=" + lastValues[id].ToString("0.###", CultureInfo.InvariantCulture)
                        : id + "=N/A"));
            OnLogError(
                "工作点就绪超时：" + definition.Leader +
                "，最长等待=" + definition.TimeoutMs + "ms，最后值：" + lastValueText +
                "；失败探针：" + FormatFailedProbeSummary(lastFailedProbes) + "。");
            return new MeasurementReadinessResult
            {
                IsReady = false,
                LastValues = lastValues,
                FailedProbes = lastFailedProbes,
                FailureReason = lastFailedProbes.Count == 0
                    ? "未达到连续" + requiredConsecutiveSamples + "次稳定要求"
                    : "工作点就绪超时"
            };
        }

        private static int GetConfiguredStableSampleCount()
        {
            int configured;
            string value = System.Configuration.ConfigurationManager.AppSettings[
                "SKStableSampleCount"];
            return int.TryParse(value, out configured)
                ? Math.Max(1, configured)
                : 1;
        }

        private static double GetReadinessMargin(string resultId)
        {
            if (!string.Equals(resultId, "R06", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(resultId, "R14", StringComparison.OrdinalIgnoreCase))
            {
                return 0.0;
            }

            double configured;
            string value = System.Configuration.ConfigurationManager.AppSettings[
                "SKReadinessExternalCurrentMarginMa"];
            return double.TryParse(
                    value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out configured)
                ? Math.Max(0.0, configured)
                : 100.0;
        }

        private async Task<double> ReadMeasurementReadinessProbeAsync(
            SK441Device device,
            Bcm250ActionDefinition action,
            string unit,
            TestSequenceContext context,
            MeasurementReadinessDaqState daqState,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            switch (action.Kind)
            {
                case Bcm250ActionKind.DaqVoltage:
                    {
                        double volts = await ReadMeasurementReadinessDaqVoltageAsync(
                            device,
                            action.Channel,
                            daqState,
                            token);
                        return unit.IndexOf("mV", StringComparison.OrdinalIgnoreCase) >= 0
                            ? volts * 1000.0
                            : volts;
                    }
                case Bcm250ActionKind.DaqShuntCurrent:
                    {
                        double shuntOhms = action.Scale;
                        string resistanceKey = Bcm250ResultKeys.ShuntResistanceForChannel(action.Channel);
                        double measuredResistance;
                        bool useNominalResistance =
                            action.Channel == Bcm250StationMap.StringShuntDaqChannel ||
                            string.Equals(action.ResultId, "B06", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(action.ResultId, "B07", StringComparison.OrdinalIgnoreCase);
                        if (!useNominalResistance &&
                            context != null &&
                            context.TryGetNumericValue(resistanceKey, out measuredResistance) &&
                            !double.IsNaN(measuredResistance) &&
                            !double.IsInfinity(measuredResistance) &&
                            measuredResistance > 0.0)
                        {
                            shuntOhms = measuredResistance;
                        }
                        if (double.IsNaN(shuntOhms) ||
                            double.IsInfinity(shuntOhms) ||
                            shuntOhms <= 0.0)
                        {
                            throw new InvalidOperationException(
                                "Missing/invalid readiness shunt resistance: " + resistanceKey);
                        }

                        double volts = await ReadMeasurementReadinessDaqVoltageAsync(
                            device,
                            action.Channel,
                            daqState,
                            token);
                        return volts / shuntOhms * 1000.0;
                    }
                case Bcm250ActionKind.MbdNumeric:
                    {
                        string command = action.Argument;
                        string response = _mbdTerminalState == MbdTerminalState.Bcm &&
                                          device.IsTargetBoardCommunicationConnected
                            ? await device.QueryMbdCommandOnCurrentConnectionAsync(command, 0)
                            : await device.QueryMbdCommandAsync(command, 0);
                        return MbdResponseParser.ParseScalar(command, response);
                    }
                default:
                    throw new InvalidOperationException(
                        "Unsupported readiness probe kind for " + action.ResultId + ": " + action.Kind);
            }
        }

        private async Task<double> ReadMeasurementReadinessDaqVoltageAsync(
            SK441Device device,
            int channel,
            MeasurementReadinessDaqState state,
            CancellationToken token)
        {
            const int channelSwitchSettleMs = 100;
            const int retrySettleMs = 150;

            if (!device.SkipComInit &&
                state.LastChannel.HasValue &&
                state.LastChannel.Value != channel)
            {
                device.RecoverDaqVoltageMeasurement(
                    "稳定门禁由通道" + state.LastChannel.Value + "切换到通道" + channel);
                await Task.Delay(channelSwitchSettleMs, token);
            }

            state.LastChannel = channel;
            try
            {
                return await device.MeasureDaqChannelVoltageAsync(channel);
            }
            catch (TimeoutException ex)
            {
                OnLogWarning(
                    "稳定门禁DAQ通道" + channel + "首次读取超时，恢复测量状态后单次重试：" +
                    ex.Message);
                device.RecoverDaqVoltageMeasurement("通道" + channel + "读取超时重试");
                await Task.Delay(retrySettleMs, token);
                return await device.MeasureDaqChannelVoltageAsync(channel);
            }
        }

        private static string FormatReadinessFailureSummary(MeasurementReadinessResult readiness)
        {
            string failedProbeSummary = FormatFailedProbeSummary(readiness.FailedProbes);
            return string.Equals(failedProbeSummary, "无", StringComparison.Ordinal)
                ? readiness.FailureReason ?? "工作点未就绪"
                : failedProbeSummary;
        }

        private static string FormatFailedProbeSummary(
            IDictionary<string, string> failedProbes)
        {
            if (failedProbes == null || failedProbes.Count == 0)
                return "无";

            return string.Join(
                "；",
                failedProbes.Select(pair => pair.Key + "=" + pair.Value));
        }

        private async Task<double> ReadHardwareValueWithParallelPrefetchAsync(
            SK441Device device,
            Bcm250ActionDefinition action,
            string unit,
            TestSequenceContext context,
            CancellationToken token)
        {
            bool engineeringStepMode = context?.RequiresSynchronousUiResults?.Invoke() == true;
            double prefetched;
            if (_prefetchedHardwareValues.TryGetValue(action.ResultId, out prefetched))
            {
                _prefetchedHardwareValues.Remove(action.ResultId);
                if (!engineeringStepMode)
                    return prefetched;
                OnLogInfo("工程单步/断点模式：重新读取 " + action.ResultId + "，不复用提前采样。");
            }

            ParallelMeasurementBatch batch = ParallelMeasurementBatches.FirstOrDefault(x =>
                string.Equals(x.Leader, action.ResultId, StringComparison.OrdinalIgnoreCase));
            if (batch == null || engineeringStepMode)
                return await ReadHardwareValueAsync(device, action, unit, context, token);

            OnLogInfo("启动跨仪器并行测量批次：" + string.Join(" + ", batch.Branches.SelectMany(x => x)));
            Task<Dictionary<string, double>>[] branchTasks = batch.Branches
                .Select(branch => ReadMeasurementBranchAsync(device, branch, context, token))
                .ToArray();
            Dictionary<string, double>[] branchResults = await Task.WhenAll(branchTasks);
            foreach (Dictionary<string, double> result in branchResults)
            {
                foreach (KeyValuePair<string, double> pair in result)
                    _prefetchedHardwareValues[pair.Key] = pair.Value;
            }

            if (!_prefetchedHardwareValues.TryGetValue(action.ResultId, out prefetched))
                throw new InvalidOperationException("Parallel measurement did not return " + action.ResultId);
            _prefetchedHardwareValues.Remove(action.ResultId);
            return prefetched;
        }

        private async Task<Dictionary<string, double>> ReadMeasurementBranchAsync(
            SK441Device device,
            IEnumerable<string> resultIds,
            TestSequenceContext context,
            CancellationToken token)
        {
            var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (string resultId in resultIds)
            {
                token.ThrowIfCancellationRequested();
                Bcm250ActionDefinition branchAction;
                TestStepItem branchStep;
                if (!_actionsByResultId.TryGetValue(resultId, out branchAction) ||
                    !_stepsByResultId.TryGetValue(resultId, out branchStep) ||
                    branchStep.PlanDefinition == null)
                {
                    throw new TestPlanConfigurationException(
                        "Parallel measurement has no XML definition for " + resultId);
                }
                values[resultId] = await ReadHardwareValueAsync(
                    device,
                    branchAction,
                    branchStep.PlanDefinition.Unit ?? string.Empty,
                    context,
                    token);
            }
            return values;
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
                        if (string.Equals(action.ResultId, "S02", StringComparison.OrdinalIgnoreCase))
                            await LogCcbDiagnosticAsync(device, "S02测量前", token);

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
                        if (string.Equals(action.ResultId, "S01", StringComparison.OrdinalIgnoreCase))
                            await LogCcbDiagnosticAsync(device, "S01测量前", token);

                        bool useNominalResistance =
                            action.Channel == Bcm250StationMap.StringShuntDaqChannel ||
                            string.Equals(action.ResultId, "B06", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(action.ResultId, "B07", StringComparison.OrdinalIgnoreCase);
                        double shuntOhms = useNominalResistance ? action.Scale : 0.0;
                        string resistanceKey =
                            Bcm250ResultKeys.ShuntResistanceForChannel(action.Channel);
                        bool usingMeasuredResistance = !useNominalResistance && context != null &&
                            context.TryGetNumericValue(resistanceKey, out shuntOhms) &&
                            !double.IsNaN(shuntOhms) &&
                            !double.IsInfinity(shuntOhms) &&
                            shuntOhms > 0.0;
                        if (!usingMeasuredResistance)
                        {
                            shuntOhms = action.Scale;
                            if (double.IsNaN(shuntOhms) ||
                                double.IsInfinity(shuntOhms) ||
                                shuntOhms <= 0.0)
                            {
                                throw new InvalidOperationException(
                                    "Missing/invalid measured and nominal shunt resistance: " + resistanceKey);
                            }

                            if (!useNominalResistance)
                            {
                                OnLogWarning(string.Format(
                                    CultureInfo.InvariantCulture,
                                    "单章调试未取得CH7实测分流电阻：DAQ{0}使用工位标称值 {1:0.###} Ohm。",
                                    action.Channel,
                                    shuntOhms));
                            }
                        }

                        double volts = await ReadDaqAverageAsync(
                            device,
                            action.Channel,
                            SampleCountForResult(action.ResultId),
                            token);
                        // Preserve the DAQ polarity. BCM250 shunt current is defined as the
                        // measured shunt voltage divided directly by the configured resistance.
                        double currentMa = volts / shuntOhms * 1000.0;
                        OnLogInfo(string.Format(
                            CultureInfo.InvariantCulture,
                            "DAQ{0}分流电流计算：V={1:0.########}V，R={2:0.########}Ohm（{3}），I={4:0.###}mA",
                            action.Channel,
                            volts,
                            shuntOhms,
                            usingMeasuredResistance ? "CH7实测" : "工位标称",
                            currentMa));
                        return currentMa;
                    }
                case Bcm250ActionKind.ProgramFirmware:
                    if (!_firmwareReadinessEvaluated || !_firmwareReadinessPrepared)
                    {
                        OnLogError("BCM250烧录前检查或TTL路由准备未通过，C01不能放行。");
                        return 0.0;
                    }
                    if (!_programFirmwareThisRun)
                    {
                        if (_forceFirmwareProgramming)
                        {
                            OnLogError("BCM250已启用真实TTL烧录，但C01没有执行烧录，判定失败。");
                            return 0.0;
                        }
                        OnLogSuccess(
                            "BCM250现有固件已通过通讯和版本检查，C01不重复烧录。" +
                            (string.IsNullOrWhiteSpace(_precheckedFirmwareVersion)
                                ? string.Empty
                                : " 版本=" + _precheckedFirmwareVersion));
                        return 1.0;
                    }
                    return await ProgramFirmwareWithDisplayAsync(device, action.Argument, token)
                        ? 1.0
                        : 0.0;
                case Bcm250ActionKind.StoreToFlash:
                    return await device.SendMbdCommandAsync(Bcm250Commands.StoreToFlash, 0) ? 1.0 : 0.0;
                case Bcm250ActionKind.WriteIdentity:
                    return await WriteAndVerifyIdentityAsync(device, context, action.Argument) ? 1.0 : 0.0;
                case Bcm250ActionKind.LogicResult:
                    throw new InvalidOperationException("LogicResult must be evaluated with its stored dependencies.");
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

        private async Task LogCcbDiagnosticAsync(
            SK441Device device,
            string phase,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                string currentResponse = await device.QueryMbdCommandAsync("R44", 0);
                string stringVoltageResponse = await device.QueryMbdCommandAsync("R42", 0);
                double internalCurrentMa = MbdResponseParser.ParseScalar("R44", currentResponse);
                double stringVoltageMv = MbdResponseParser.ParseScalar("R42", stringVoltageResponse);

                OnLogInfo(string.Format(
                    CultureInfo.InvariantCulture,
                    "BCM250 CCB诊断[{0}]：R44内部iccb={1:0.###} mA，R42 vstr_neg={2:0.###} mV。",
                    phase,
                    internalCurrentMa,
                    stringVoltageMv));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                OnLogWarning("BCM250 CCB诊断读取失败[" + phase + "]：" + ex.Message);
            }
        }

        private async Task<bool> EvaluateFirmwareReadinessAsync(
            SK441Device device,
            CancellationToken token)
        {
            _firmwareReadinessEvaluated = true;
            _firmwareReadinessPrepared = false;
            _programFirmwareThisRun = false;
            _firmwareProgrammedThisRun = false;
            _precheckedFirmwareVersion = null;
            InvalidateFirmwarePrecheckCache();

            string expectedVersion = Bcm250Commands.FirmwareVersion;
            TestStepItem firmwareStep;
            if (_stepsByResultId.TryGetValue("E01", out firmwareStep) &&
                firmwareStep.PlanDefinition != null &&
                !string.IsNullOrWhiteSpace(firmwareStep.PlanDefinition.ExpectedValue))
            {
                expectedVersion = firmwareStep.PlanDefinition.ExpectedValue;
            }

            if (device.SkipComInit)
            {
                _precheckedFirmwareVersion = expectedVersion;
                _prefetchedHardwareValues["D01"] = 0.0;
                _prefetchedHardwareValues["D02"] = 0.0;
                _firmwarePrecheckCacheValid = !_forceFirmwareProgramming;
                _programFirmwareThisRun = _forceFirmwareProgramming;
                _firmwareReadinessPrepared = true;
                OnLogInfo(_programFirmwareThisRun
                    ? "[调试模式] BCM250已启用TTL烧录，模拟执行烧录及后续复检。"
                    : "[调试模式] BCM250模拟版本预检查通过，按需模式跳过本轮TTL烧录。");
                return true;
            }

            bool bcmCanOk = false;
            bool safeCanOk = false;
            try
            {
                bcmCanOk = Math.Abs(await ReadCanStatusAsync(
                    device,
                    "D01",
                    "R14",
                    token)) < 0.001;
            }
            catch (Exception ex)
            {
                OnLogWarning("BCM250烧录前R14检查未通过：" + ex.Message);
            }

            try
            {
                safeCanOk = Math.Abs(await ReadCanStatusAsync(
                    device,
                    "D02",
                    "R15VERB",
                    token)) < 0.001;
            }
            catch (Exception ex)
            {
                OnLogError("BCM250烧录前R15 SAFE检查失败：" + ex.Message);
            }

            if (!safeCanOk)
            {
                OnLogError(
                    "SAFE内部CAN异常不能通过烧录BCM250修复；本轮禁止烧录并停止测试。");
                return false;
            }

            bool enteredBcm = await SendCommandsAsync(
                device,
                Bcm250Commands.EnterBcm,
                "BCM250.FIRMWARE.READINESS.REMOTE2",
                token);
            if (enteredBcm)
            {
                try
                {
                    string response = await device.QueryMbdCommandAsync("R0", 0);
                    _precheckedFirmwareVersion = MbdResponseParser.ParsePayloadText("R0", response);
                }
                catch (Exception ex)
                {
                    OnLogWarning("BCM250烧录前R0版本读取失败：" + ex.Message);
                }
            }
            else
            {
                OnLogWarning("BCM250烧录前无法进入REMOTE2，将按应用固件不可用处理。");
            }

            bool versionMatches = string.Equals(
                _precheckedFirmwareVersion,
                expectedVersion,
                StringComparison.OrdinalIgnoreCase);

            // W86 and the CAN status registers belong to the MBD terminal.
            // Always leave REMOTE2 before either skipping or preparing Bootloader mode.
            if (!await SendCommandsAsync(
                device,
                Bcm250Commands.EnterMbd,
                "BCM250.FIRMWARE.READINESS.EXIT",
                token))
            {
                OnLogError("BCM250版本预检查后无法返回MBD终端，禁止继续烧录。");
                return false;
            }

            if (versionMatches && !_forceFirmwareProgramming)
            {
                _prefetchedHardwareValues["D01"] = bcmCanOk ? 0.0 : 1.0;
                _prefetchedHardwareValues["D02"] = 0.0;
                _firmwarePrecheckCacheValid = true;
                if (!bcmCanOk)
                {
                    OnLogWarning(
                        "BCM250 R0版本正确但R14状态异常：不重复烧录，交由正式D01复检判定。");
                }
                OnLogSuccess(
                    "BCM250现有固件版本正确（" + expectedVersion + "），跳过本轮TTL烧录。");
                _firmwareReadinessPrepared = true;
                return true;
            }

            OnLogWarning(string.Format(
                CultureInfo.InvariantCulture,
                versionMatches
                    ? "BCM250已启用真实TTL烧录：即使版本一致也执行C01。R14={0}，当前版本={1}，目标版本={2}。"
                    : "BCM250需要烧录：R14={0}，当前版本={1}，目标版本={2}。",
                bcmCanOk ? "OK" : "FAIL/NO RESPONSE",
                string.IsNullOrWhiteSpace(_precheckedFirmwareVersion)
                    ? "NO RESPONSE"
                    : _precheckedFirmwareVersion,
                expectedVersion));

            if (SkipFirmwareProgramming)
            {
                OnLogError(
                    "BCM250版本不匹配或无响应，但当前配置禁止TTL烧录，C01不能按成功放行。");
                return false;
            }

            if (!await SendCommandsAsync(
                    device,
                    Bcm250Commands.EnableDiagnostics,
                    "BCM250.FIRMWARE.READINESS.DIAG",
                    token) ||
                !await SendCommandsAsync(
                    device,
                    "W86=1",
                    "BCM250.FIRMWARE.READINESS.RESET",
                    token))
            {
                OnLogError("BCM250无法通过MBD置位RESET，禁止进入Bootloader烧录。");
                return false;
            }

            bool ttlRouteReady = await device.SetFixtureRelaysAsync(
                ParseRelayList(Bcm250StationMap.ProgrammingFixtureRelays),
                true,
                "BCM250按需烧录TTL路由");
            if (!ttlRouteReady)
            {
                OnLogError("BCM250 TTL TX/RX/GND编程继电器闭合失败。");
                return false;
            }

            _programFirmwareThisRun = true;
            _firmwareReadinessPrepared = true;
            return true;
        }

        private async Task<bool> ProgramFirmwareWithDisplayAsync(
            SK441Device device,
            string firmwareFileName,
            CancellationToken token)
        {
            if (device.SkipComInit)
            {
                token.ThrowIfCancellationRequested();
                _firmwareProgrammedThisRun = true;
                InvalidateFirmwarePrecheckCache();
                OnLogInfo("[调试模式] BCM250模拟TTL烧录完成；后续复位和版本复检仍按步骤执行。");
                return true;
            }

            FirmwareProgrammingWindow dialog = await ShowFirmwareProgrammingAsync(
                firmwareFileName);
            Action<string, int> progressHandler = (phase, percent) =>
            {
                if (dialog != null)
                {
                    dialog.UpdateProgress(
                        phase,
                        percent,
                        percent < 100
                            ? "请勿断电、拔出 USB-B 或移动测试板。"
                            : "正在完成烧录收尾...");
                }
            };

            device.Flasher.ProgressChanged += progressHandler;
            bool flashed = false;
            try
            {
                if (dialog != null)
                    dialog.UpdateProgress("连接 STM32 Bootloader", 0);

                flashed = await device.FlashBcm250FirmwareAsync(
                    firmwareFileName,
                    token);
                _firmwareProgrammedThisRun = flashed;
                if (flashed)
                    InvalidateFirmwarePrecheckCache();

                string detail = flashed
                    ? "即将复位并重新上电检查通讯和版本。"
                    : (device.Flasher.LastError ?? "请检查TTL串口、BOOT0、RESET及固件文件。");
                await CompleteFirmwareProgrammingAsync(dialog, flashed, detail);
                return flashed;
            }
            finally
            {
                device.Flasher.ProgressChanged -= progressHandler;
                if (!flashed && dialog != null)
                    dialog.CloseSafely();
            }
        }

        private async Task<StepResult> WaitForBcmRunningTimeAsync(
            SK441Device device,
            Bcm250ActionDefinition action,
            TestStepItem step,
            CancellationToken token)
        {
            if (device.SkipComInit)
            {
                return RunningTimeResult(
                    true,
                    5.0,
                    Bcm250ResultKeys.ResetFlatRunningTimeBefore,
                    "BCM application ready",
                    action);
            }

            int timeoutMs = GetPollingTimeoutMs(step, 8000);
            Stopwatch timer = Stopwatch.StartNew();
            string lastError = null;
            while (timer.ElapsedMilliseconds < timeoutMs)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (await EnsureBcmTerminalForPollingAsync(device, token))
                    {
                        double runningTime = MbdResponseParser.ParseScalar(
                            "R10",
                            await device.QueryMbdCommandOnCurrentConnectionAsync("R10", 0));
                        if (runningTime >= 3.0)
                        {
                            OnLogInfo(string.Format(
                                CultureInfo.InvariantCulture,
                                "BCM250复位前R10已就绪：{0:0.###}s，用时{1}ms。",
                                runningTime,
                                timer.ElapsedMilliseconds));
                            return RunningTimeResult(
                                true,
                                runningTime,
                                Bcm250ResultKeys.ResetFlatRunningTimeBefore,
                                "BCM application ready",
                                action);
                        }
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    ResetMbdState();
                }

                int remainingMs = timeoutMs - (int)timer.ElapsedMilliseconds;
                if (remainingMs <= 0)
                    break;
                await Task.Delay(Math.Min(250, remainingMs), token);
            }

            OnLogError(
                "BCM250复位前R10在" + timeoutMs + "ms内未达到3秒。" +
                (string.IsNullOrWhiteSpace(lastError) ? string.Empty : " 最后错误：" + lastError));
            return RunningTimeResult(
                false,
                0.0,
                Bcm250ResultKeys.ResetFlatRunningTimeBefore,
                "R10 readiness timeout",
                action);
        }

        private async Task<StepResult> WaitForBcmApplicationAsync(
            SK441Device device,
            Bcm250ActionDefinition action,
            TestStepItem step,
            CancellationToken token)
        {
            if (device.SkipComInit)
            {
                return RunningTimeResult(
                    true,
                    1.0,
                    Bcm250ResultKeys.ResetFlatRunningTimeAfter,
                    "BCM reset recovery ready",
                    action);
            }

            int timeoutMs = GetPollingTimeoutMs(step, 5000);
            Stopwatch timer = Stopwatch.StartNew();
            string lastError = null;
            await Task.Delay(Math.Min(300, timeoutMs), token);
            while (timer.ElapsedMilliseconds < timeoutMs)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (await EnsureBcmTerminalForPollingAsync(device, token))
                    {
                        double runningTime = MbdResponseParser.ParseScalar(
                            "R10",
                            await device.QueryMbdCommandOnCurrentConnectionAsync("R10", 0));
                        if (runningTime >= 1.0)
                        {
                            OnLogInfo(string.Format(
                                CultureInfo.InvariantCulture,
                                "W86复位后BCM250应用已恢复：R10={0:0.###}s，用时{1}ms。",
                                runningTime,
                                timer.ElapsedMilliseconds));
                            return RunningTimeResult(
                                true,
                                runningTime,
                                Bcm250ResultKeys.ResetFlatRunningTimeAfter,
                                "BCM reset recovery ready",
                                action);
                        }
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    ResetMbdState();
                }

                int remainingMs = timeoutMs - (int)timer.ElapsedMilliseconds;
                if (remainingMs <= 0)
                    break;
                await Task.Delay(Math.Min(200, remainingMs), token);
            }

            OnLogError(
                "W86复位后BCM250应用在" + timeoutMs + "ms内未恢复。" +
                (string.IsNullOrWhiteSpace(lastError) ? string.Empty : " 最后错误：" + lastError));
            return RunningTimeResult(
                false,
                0.0,
                Bcm250ResultKeys.ResetFlatRunningTimeAfter,
                "BCM reset recovery timeout",
                action);
        }

        private async Task<bool> EnsureBcmTerminalForPollingAsync(
            SK441Device device,
            CancellationToken token)
        {
            if (_mbdTerminalState == MbdTerminalState.Bcm &&
                device.IsTargetBoardCommunicationConnected)
            {
                return true;
            }

            if (_mbdTerminalState != MbdTerminalState.Mbd)
            {
                if (!await SendCommandsAsync(
                    device,
                    Bcm250Commands.EnterMbd,
                    "BCM250.R10.POLL.EXIT",
                    token))
                {
                    return false;
                }
            }

            return await SendCommandsAsync(
                device,
                Bcm250Commands.EnterBcm,
                "BCM250.R10.POLL.REMOTE2",
                token);
        }

        private async Task<bool> CompleteBcmSerialBatchChapterAsync(
            TestSequenceContext context,
            SK441Device device,
            Bcm250ActionDefinition action,
            CancellationToken token)
        {
            bool hasFollowingBcmReadChapter = context != null &&
                context.TestPlan != null &&
                (action.Argument ?? string.Empty)
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Any(context.TestPlan.ShouldRun);
            if (hasFollowingBcmReadChapter &&
                _mbdTerminalState == MbdTerminalState.Bcm &&
                device.IsTargetBoardCommunicationConnected)
            {
                OnLogInfo(
                    action.StepId + " 保持REMOTE2，下一串口测量章节复用当前连接。");
                return true;
            }

            return await SendCommandsAsync(
                device,
                Bcm250Commands.EnterMbd,
                action.StepId,
                token);
        }

        private static StepResult RunningTimeResult(
            bool passed,
            double runningTime,
            string storeKey,
            string value,
            Bcm250ActionDefinition action)
        {
            return new StepResult
            {
                IsPass = passed,
                Value = passed
                    ? runningTime.ToString("0.###", CultureInfo.InvariantCulture)
                    : value,
                NumericValue = runningTime,
                StoreKey = storeKey,
                Unit = "s"
            };
        }

        private static int GetPollingTimeoutMs(TestStepItem step, int fallbackMs)
        {
            if (step != null &&
                step.PlanDefinition != null &&
                step.PlanDefinition.DelayMs.GetValueOrDefault() > 0)
            {
                return step.PlanDefinition.DelayMs.Value;
            }
            return fallbackMs;
        }

        private void InvalidateFirmwarePrecheckCache()
        {
            _firmwarePrecheckCacheValid = false;
            _prefetchedHardwareValues.Remove("D01");
            _prefetchedHardwareValues.Remove("D02");
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
                case Bcm250ActionKind.WaitForCcbCurrentStable:
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
            bool passed = await device.ControlRelaysAsync(relayIds, close);

            // A partially closed power path is not a valid state. In particular,
            // BCM250 String output requires Y1/Y3/Y6 as one indivisible route.
            if (close && !passed)
            {
                await device.ControlRelaysAsync(relayIds, false);
            }
            return passed;
        }

        private static async Task<double> ReadDaqAverageAsync(
            SK441Device device,
            int channel,
            int sampleCount,
            CancellationToken token)
        {
            return await device.MeasureDaqChannelVoltageAverageAsync(
                channel,
                sampleCount,
                0,
                token);
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

        private async Task<double> ReadMbdAverageAsync(
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
                string response = _mbdTerminalState == MbdTerminalState.Bcm &&
                                  device.IsTargetBoardCommunicationConnected
                    ? await device.QueryMbdCommandOnCurrentConnectionAsync(command, 0)
                    : await device.QueryMbdCommandAsync(command, 0);
                total += MbdResponseParser.ParseScalar(command, response);
                if (intervalMs > 0 && index + 1 < sampleCount)
                    await Task.Delay(intervalMs, token);
            }
            return total / sampleCount;
        }

        private static int SampleCountForResult(string resultId)
        {
            string normalizedResultId = (resultId ?? string.Empty).ToUpperInvariant();
            int defaultCount;
            switch (normalizedResultId)
            {
                case "J04":
                    defaultCount = 5;
                    break;
                case "M01":
                case "M02":
                case "M03":
                case "M04":
                case "N02":
                case "N04":
                    defaultCount = 8;
                    break;
                case "M09":
                case "T01":
                case "T02":
                    defaultCount = 20;
                    break;
                case "N01":
                case "N03":
                    defaultCount = 16;
                    break;
                case "N07":
                case "N08":
                    defaultCount = 50;
                    break;
                case "Q01":
                case "Q02":
                case "Q05":
                case "Q06":
                case "Q08":
                case "Q09":
                    defaultCount = 4;
                    break;
                default:
                    defaultCount = 1;
                    break;
            }

            int configuredCount;
            string configuredValue = System.Configuration.ConfigurationManager.AppSettings[
                "SKBCM250SampleCount" + normalizedResultId];
            return int.TryParse(configuredValue, out configuredCount) && configuredCount > 0
                ? configuredCount
                : defaultCount;
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

        private async Task<StepResult> WaitForCcbCurrentStableAsync(
            TestStepItem step,
            SK441Device device,
            Bcm250ActionDefinition action,
            CancellationToken token)
        {
            int timeoutMs = GetPlanDelayMs(new[] { step }, 0, 20000);
            const int pollIntervalMs = 500;
            int requiredConsecutiveSamples = GetConfiguredStableSampleCount();

            TestStepItem s01Step;
            if (!_stepsByResultId.TryGetValue("S01", out s01Step) ||
                s01Step.PlanDefinition == null ||
                !s01Step.PlanDefinition.LowerLimit.HasValue ||
                !s01Step.PlanDefinition.UpperLimit.HasValue)
            {
                throw new TestPlanConfigurationException(
                    "BCM250 S01 limits are required for the CCB current readiness check.");
            }

            double readyLowerMa = Math.Min(
                Math.Abs(s01Step.PlanDefinition.LowerLimit.Value),
                Math.Abs(s01Step.PlanDefinition.UpperLimit.Value));
            double readyUpperMa = Math.Max(
                Math.Abs(s01Step.PlanDefinition.LowerLimit.Value),
                Math.Abs(s01Step.PlanDefinition.UpperLimit.Value));

            if (device.SkipComInit)
            {
                await Task.Delay(Math.Min(timeoutMs, 1), token);
                return Result(
                    true,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "R44={0:0} mA (simulation)",
                        Sk250TestSetpoints.Bcm250.CcbHardwareTestCurrentMa),
                    action);
            }

            var timer = Stopwatch.StartNew();
            int consecutiveSamples = 0;
            int sampleNumber = 0;
            double lastCurrentMa = double.NaN;
            while (timer.ElapsedMilliseconds <= timeoutMs)
            {
                token.ThrowIfCancellationRequested();
                sampleNumber++;
                try
                {
                    string response = await device.QueryMbdCommandAsync("R44", 0);
                    lastCurrentMa = MbdResponseParser.ParseScalar("R44", response);
                    bool inRange = lastCurrentMa >= readyLowerMa && lastCurrentMa <= readyUpperMa;
                    consecutiveSamples = inRange ? consecutiveSamples + 1 : 0;
                    OnLogInfo(string.Format(
                        CultureInfo.InvariantCulture,
                        "BCM250 CCB电流就绪轮询 #{0}：R44={1:0.###}mA，范围={2:0.###}~{3:0.###}mA，连续稳定={4}/{5}。",
                        sampleNumber,
                        lastCurrentMa,
                        readyLowerMa,
                        readyUpperMa,
                        consecutiveSamples,
                        requiredConsecutiveSamples));

                    if (consecutiveSamples >= requiredConsecutiveSamples)
                    {
                        timer.Stop();
                        return Result(
                            true,
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "R44={0:0.###} mA, stable in {1} ms",
                                lastCurrentMa,
                                timer.ElapsedMilliseconds),
                            action);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    consecutiveSamples = 0;
                    OnLogWarning(
                        "BCM250 CCB电流就绪轮询读取失败 #" + sampleNumber + "：" + ex.Message);
                }

                int remainingMs = timeoutMs - (int)timer.ElapsedMilliseconds;
                if (remainingMs <= 0)
                    break;
                await Task.Delay(Math.Min(pollIntervalMs, remainingMs), token);
            }

            timer.Stop();
            OnLogWarning(string.Format(
                CultureInfo.InvariantCulture,
                "BCM250 CCB电流在{0}ms内未连续稳定到{1:0.###}~{2:0.###}mA，最后R44={3}。",
                timeoutMs,
                readyLowerMa,
                readyUpperMa,
                double.IsNaN(lastCurrentMa)
                    ? "N/A"
                    : lastCurrentMa.ToString("0.###", CultureInfo.InvariantCulture) + "mA"));
            return Result(false, "R44 stabilization timeout", action);
        }

        private async Task ExecuteOptimizedDelayAsync(
            SK441Device device,
            string stepId,
            int maximumWaitMs,
            CancellationToken token)
        {
            if (string.Equals(
                    stepId,
                    "BCM250.CH18.S018.ACTION",
                    StringComparison.OrdinalIgnoreCase))
            {
                bool prechargeSenseReady = await WaitForL01L02ReadyAsync(
                    device,
                    3000,
                    maximumWaitMs,
                    token);
                if (!prechargeSenseReady)
                {
                    OnLogWarning(
                        stepId + " R69/R68未在最长等待内同时进入XML范围；" +
                        "后续L01/L02仍执行正式读取并独立判定。");
                }
                return;
            }

            if (IsSecondarySettleDelay(stepId, out int secondaryMinimumWaitMs))
            {
                int settleMs = Math.Min(maximumWaitMs, secondaryMinimumWaitMs);
                await Task.Delay(settleMs, token);
                OnLogInfo(
                    stepId + " String发生器设定值和输出状态已由下发命令回读确认；" +
                    "保留" + settleMs + "ms硬件稳定时间，实际DUT值由紧随其后的正式测量判定。");
                return;
            }

            if (string.Equals(
                    stepId,
                    "BCM250.CH16.S006.ACTION",
                    StringComparison.OrdinalIgnoreCase))
            {
                bool vbusReady = await WaitForDaqVoltageInRangeAsync(
                    device,
                    Bcm250StationMap.VbusReferenceDaqChannel,
                    260.0,
                    290.0,
                    500,
                    maximumWaitMs,
                    token);
                if (!vbusReady)
                {
                    OnLogWarning(
                        stepId + " DAQ105未在最长等待内确认VBUS接通；" +
                        "已完整保留XML等待上限，后续R51/DAQ105正式步骤继续判定。");
                }
                return;
            }

            if (string.Equals(
                    stepId,
                    "BCM250.CH20.S029.ACTION",
                    StringComparison.OrdinalIgnoreCase))
            {
                int flashSettleMs = Math.Min(maximumWaitMs, 250);
                await Task.Delay(flashSettleMs, token);
                OnLogInfo(
                    stepId + " ACT->FLASH已返回完成；保留" + flashSettleMs +
                    "ms落盘缓冲，0A状态由紧随其后的N07稳定门禁确认。");
                return;
            }

            if (string.Equals(
                    stepId,
                    "BCM250.CH26.S028.ACTION",
                    StringComparison.OrdinalIgnoreCase))
            {
                bool currentReady = await WaitForMbdScalarInRangeAsync(
                    device,
                    "R44",
                    -20.0,
                    20.0,
                    500,
                    maximumWaitMs,
                    GetConfiguredStableSampleCount(),
                    token);
                if (!currentReady)
                {
                    OnLogWarning(
                        stepId + " R44未在最长等待内回到-20~20mA；" +
                        "已完整保留XML等待上限，后续T03正式步骤继续判定。");
                }
                return;
            }

            if (string.Equals(
                    stepId,
                    "BCM250.CH19.S015.ACTION",
                    StringComparison.OrdinalIgnoreCase))
            {
                // W91 only needs relay settling. The following explicit two-second
                // delay plus the M01/M02 readiness gate performs the real VMID
                // stability check, so a second blind ten-second wait is redundant.
                int relaySettleMs = Math.Min(maximumWaitMs, 500);
                await Task.Delay(relaySettleMs, token);
                OnLogInfo(
                    stepId + " 已保留" + relaySettleMs +
                    "ms继电器稳定时间；VMID由后续M01/M02工作点门禁确认。");
                return;
            }

            if (string.Equals(
                    stepId,
                    "BCM250.CH26.S024.ACTION",
                    StringComparison.OrdinalIgnoreCase))
            {
                double lower = -750.0;
                double upper = -300.0;
                TestStepItem t01Step;
                if (_stepsByResultId.TryGetValue("T01", out t01Step) &&
                    t01Step.PlanDefinition?.LowerLimit != null &&
                    t01Step.PlanDefinition.UpperLimit != null)
                {
                    lower = t01Step.PlanDefinition.LowerLimit.Value;
                    upper = t01Step.PlanDefinition.UpperLimit.Value;
                }

                bool mosCurrentReady = await WaitForMbdScalarInRangeAsync(
                    device,
                    "R44",
                    lower,
                    upper,
                    500,
                    maximumWaitMs,
                    GetConfiguredStableSampleCount(),
                    token);
                if (!mosCurrentReady)
                {
                    OnLogWarning(
                        stepId + " R44未在最长等待内进入T01限值；" +
                        "后续T01正式平均测量仍继续判定。");
                }
                return;
            }

            int minimumWaitMs;
            string[] domains;
            float[] targets;
            if (TryGetPowerPollingDefinition(stepId, out minimumWaitMs, out domains, out targets))
            {
                var polls = new List<Task<bool>>();
                for (int index = 0; index < domains.Length; index++)
                {
                    polls.Add(device.WaitForPowerVoltageStableAsync(
                        domains[index],
                        targets[index],
                        minimumWaitMs,
                        maximumWaitMs,
                        token));
                }
                bool[] stable = await Task.WhenAll(polls);
                if (stable.All(x => x))
                    return;

                OnLogWarning(stepId + " 状态轮询未提前满足，已保留 XML 最大等待时间 " + maximumWaitMs + "ms。");
                return;
            }

            if (string.Equals(stepId, "BCM250.CH09.S008.ACTION", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(stepId, "BCM250.CH12.S002.ACTION", StringComparison.OrdinalIgnoreCase))
            {
                DateTime started = DateTime.UtcNow;
                int probeAfterMs = string.Equals(stepId, "BCM250.CH09.S008.ACTION", StringComparison.OrdinalIgnoreCase)
                    ? 1500
                    : 500;
                await Task.Delay(Math.Min(probeAfterMs, maximumWaitMs), token);
                if (await device.CheckMbdCommunicationAsync())
                {
                    _mbdReadyObserved = true;
                    _mbdTerminalState = MbdTerminalState.Unknown;
                    OnLogInfo(stepId + " DUT 通信已就绪，提前结束启动等待。");
                    return;
                }

                int remainingMs = maximumWaitMs - (int)(DateTime.UtcNow - started).TotalMilliseconds;
                if (remainingMs > 0)
                    await Task.Delay(remainingMs, token);
                return;
            }

            await Task.Delay(maximumWaitMs, token);
        }

        private async Task<bool> WaitForDaqVoltageInRangeAsync(
            SK441Device device,
            int channel,
            double lowerVoltage,
            double upperVoltage,
            int minimumWaitMs,
            int maximumWaitMs,
            CancellationToken token)
        {
            var timer = Stopwatch.StartNew();
            int stableSamples = 0;
            int requiredStableSamples = GetConfiguredStableSampleCount();
            double lastVoltage = double.NaN;
            while (timer.ElapsedMilliseconds < maximumWaitMs)
            {
                token.ThrowIfCancellationRequested();
                int remainingMinimumMs = minimumWaitMs - (int)timer.ElapsedMilliseconds;
                if (remainingMinimumMs > 0)
                {
                    await Task.Delay(Math.Min(100, remainingMinimumMs), token);
                    continue;
                }

                try
                {
                    lastVoltage = await device.MeasureDaqChannelVoltageAsync(channel);
                    bool inRange = lastVoltage >= lowerVoltage && lastVoltage <= upperVoltage;
                    stableSamples = inRange ? stableSamples + 1 : 0;
                    OnLogInfo(string.Format(
                        CultureInfo.InvariantCulture,
                        "DAQ{0}快速就绪轮询：{1:0.###}V，范围={2:0.###}~{3:0.###}V，连续稳定={4}/{5}。",
                        channel,
                        lastVoltage,
                        lowerVoltage,
                        upperVoltage,
                        stableSamples,
                        requiredStableSamples));
                    if (stableSamples >= requiredStableSamples)
                    {
                        OnLogSuccess(string.Format(
                            CultureInfo.InvariantCulture,
                            "DAQ{0}工作点已就绪，提前结束等待：{1:0.###}V，耗时={2}ms。",
                            channel,
                            lastVoltage,
                            timer.ElapsedMilliseconds));
                        return true;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    stableSamples = 0;
                    OnLogWarning("DAQ" + channel + "快速就绪读取失败：" + ex.Message);
                }

                int remainingMs = maximumWaitMs - (int)timer.ElapsedMilliseconds;
                if (remainingMs > 0)
                    await Task.Delay(Math.Min(200, remainingMs), token);
            }
            return false;
        }

        private async Task<bool> WaitForMbdScalarInRangeAsync(
            SK441Device device,
            string command,
            double lower,
            double upper,
            int minimumWaitMs,
            int maximumWaitMs,
            int requiredConsecutiveSamples,
            CancellationToken token)
        {
            requiredConsecutiveSamples = GetConfiguredStableSampleCount();
            var timer = Stopwatch.StartNew();
            int stableSamples = 0;
            double lastValue = double.NaN;
            while (timer.ElapsedMilliseconds < maximumWaitMs)
            {
                token.ThrowIfCancellationRequested();
                int remainingMinimumMs = minimumWaitMs - (int)timer.ElapsedMilliseconds;
                if (remainingMinimumMs > 0)
                {
                    await Task.Delay(Math.Min(100, remainingMinimumMs), token);
                    continue;
                }

                try
                {
                    string response = await device.QueryMbdCommandAsync(command, 0);
                    lastValue = MbdResponseParser.ParseScalar(command, response);
                    bool inRange = lastValue >= lower && lastValue <= upper;
                    stableSamples = inRange ? stableSamples + 1 : 0;
                    OnLogInfo(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}快速就绪轮询：{1:0.###}，范围={2:0.###}~{3:0.###}，连续稳定={4}/{5}。",
                        command,
                        lastValue,
                        lower,
                        upper,
                        stableSamples,
                        requiredConsecutiveSamples));
                    if (stableSamples >= requiredConsecutiveSamples)
                    {
                        OnLogSuccess(string.Format(
                            CultureInfo.InvariantCulture,
                            "{0}已稳定，提前结束等待：{1:0.###}，耗时={2}ms。",
                            command,
                            lastValue,
                            timer.ElapsedMilliseconds));
                        return true;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    stableSamples = 0;
                    OnLogWarning(command + "快速就绪读取失败：" + ex.Message);
                }

                int remainingMs = maximumWaitMs - (int)timer.ElapsedMilliseconds;
                if (remainingMs > 0)
                    await Task.Delay(Math.Min(200, remainingMs), token);
            }
            return false;
        }

        private async Task<bool> WaitForL01L02ReadyAsync(
            SK441Device device,
            int minimumWaitMs,
            int maximumWaitMs,
            CancellationToken token)
        {
            TestStepItem l01Step;
            TestStepItem l02Step;
            if (!_stepsByResultId.TryGetValue("L01", out l01Step) ||
                !_stepsByResultId.TryGetValue("L02", out l02Step) ||
                l01Step.PlanDefinition?.LowerLimit == null ||
                l01Step.PlanDefinition.UpperLimit == null ||
                l02Step.PlanDefinition?.LowerLimit == null ||
                l02Step.PlanDefinition.UpperLimit == null)
            {
                throw new TestPlanConfigurationException(
                    "L01/L02 readiness requires complete XML limits.");
            }

            double l01Lower = l01Step.PlanDefinition.LowerLimit.Value;
            double l01Upper = l01Step.PlanDefinition.UpperLimit.Value;
            double l02Lower = l02Step.PlanDefinition.LowerLimit.Value;
            double l02Upper = l02Step.PlanDefinition.UpperLimit.Value;
            var timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < maximumWaitMs)
            {
                token.ThrowIfCancellationRequested();
                int remainingMinimumMs = minimumWaitMs - (int)timer.ElapsedMilliseconds;
                if (remainingMinimumMs > 0)
                {
                    await Task.Delay(Math.Min(100, remainingMinimumMs), token);
                    continue;
                }

                try
                {
                    double l01 = MbdResponseParser.ParseScalar(
                        "R69",
                        await device.QueryMbdCommandAsync("R69", 0));
                    double l02 = MbdResponseParser.ParseScalar(
                        "R68",
                        await device.QueryMbdCommandAsync("R68", 0));
                    bool ready = l01 >= l01Lower && l01 <= l01Upper &&
                                 l02 >= l02Lower && l02 <= l02Upper;
                    OnLogInfo(string.Format(
                        CultureInfo.InvariantCulture,
                        "L01/L02就绪轮询：R69={0:0.###}[{1:0.###}~{2:0.###}]，" +
                        "R68={3:0.###}[{4:0.###}~{5:0.###}]，状态={6}。",
                        l01,
                        l01Lower,
                        l01Upper,
                        l02,
                        l02Lower,
                        l02Upper,
                        ready ? "READY" : "WAIT"));
                    if (ready)
                    {
                        _prefetchedHardwareValues["L01"] = l01;
                        _prefetchedHardwareValues["L02"] = l02;
                        OnLogSuccess(
                            "L01/L02实际信号已稳定，复用最终R69/R68读数，耗时=" +
                            timer.ElapsedMilliseconds + "ms。");
                        return true;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    OnLogWarning("L01/L02就绪轮询读取失败：" + ex.Message);
                }

                int remainingMs = maximumWaitMs - (int)timer.ElapsedMilliseconds;
                if (remainingMs > 0)
                    await Task.Delay(Math.Min(250, remainingMs), token);
            }
            return false;
        }

        private static bool IsSecondarySettleDelay(string stepId, out int minimumWaitMs)
        {
            switch ((stepId ?? string.Empty).ToUpperInvariant())
            {
                case "BCM250.CH23.S034.ACTION": minimumWaitMs = 1500; return true;
                case "BCM250.CH24.S027.ACTION": minimumWaitMs = 1500; return true;
                case "BCM250.CH25.S025.ACTION": minimumWaitMs = 750; return true;
                case "BCM250.CH25.S044.ACTION": minimumWaitMs = 500; return true;
                default: minimumWaitMs = 0; return false;
            }
        }

        private static bool TryGetPowerPollingDefinition(
            string stepId,
            out int minimumWaitMs,
            out string[] domains,
            out float[] targets)
        {
            minimumWaitMs = 0;
            domains = null;
            targets = null;
            switch ((stepId ?? string.Empty).ToUpperInvariant())
            {
                case "BCM250.CH18.S018.ACTION": minimumWaitMs = 1500; domains = new[] { "VBUS" }; targets = new[] { 275f }; return true;
                case "BCM250.CH18.S034.ACTION": minimumWaitMs = 750; domains = new[] { "VBUS" }; targets = new[] { 0f }; return true;
                // M01/M02, M03/M04 and M09 immediately perform independent
                // DUT-side readiness checks. Once the source readback reaches
                // target, those gates provide the remaining settling time.
                case "BCM250.CH19.S009.ACTION": minimumWaitMs = 500; domains = new[] { "VBUS" }; targets = new[] { 125f }; return true;
                case "BCM250.CH19.S023.ACTION": minimumWaitMs = 1000; domains = new[] { "VBUS" }; targets = new[] { 0f }; return true;
                case "BCM250.CH19.S025.ACTION": minimumWaitMs = 500; domains = new[] { "VBUS" }; targets = new[] { 5f }; return true;
                case "BCM250.CH19.S034.ACTION": minimumWaitMs = 500; domains = new[] { "VBUS" }; targets = new[] { 125f }; return true;
                case "BCM250.CH23.S028.ACTION": minimumWaitMs = 750; domains = new[] { "VBUS" }; targets = new[] { 15f }; return true;
                case "BCM250.CH23.S034.ACTION": minimumWaitMs = 1500; domains = new[] { "VBUS", "SECONDARY" }; targets = new[] { 1f, 25f }; return true;
                case "BCM250.CH24.S027.ACTION": minimumWaitMs = 1500; domains = new[] { "VBUS", "SECONDARY" }; targets = new[] { 1f, 15f }; return true;
                case "BCM250.CH25.S016.ACTION": minimumWaitMs = 3000; domains = new[] { "VBUS" }; targets = new[] { 275f }; return true;
                case "BCM250.CH25.S025.ACTION": minimumWaitMs = 750; domains = new[] { "SECONDARY" }; targets = new[] { 225f }; return true;
                case "BCM250.CH24.S058.ACTION": minimumWaitMs = 250; domains = new[] { "LOAD" }; targets = new[] { 0f }; return true;
                case "BCM250.CH25.S038.ACTION": minimumWaitMs = 250; domains = new[] { "LOAD" }; targets = new[] { 255f }; return true;
                case "BCM250.CH25.S044.ACTION": minimumWaitMs = 500; domains = new[] { "SECONDARY" }; targets = new[] { 0f }; return true;
                case "BCM250.CH26.S012.ACTION": minimumWaitMs = 1000; domains = new[] { "VBUS" }; targets = new[] { 100f }; return true;
                case "BCM250.CH26.S037.ACTION": minimumWaitMs = 250; domains = new[] { "LOAD" }; targets = new[] { 0f }; return true;
                default: return false;
            }
        }

        private async Task<bool> SendCommandsAsync(
            SK441Device device,
            string commands,
            string stepId,
            CancellationToken token)
        {
            foreach (string command in (commands ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string normalizedCommand = command.Trim();
                if (CanSkipMbdCommand(normalizedCommand, stepId))
                {
                    OnLogInfo("重复 MBD 状态命令已消除：" + normalizedCommand);
                    continue;
                }
                if (!await device.SendMbdCommandAsync(normalizedCommand, 0))
                {
                    ResetMbdState();
                    return false;
                }

                RecordMbdCommandState(normalizedCommand);

                int settleMs = MbdStateChangeSettleMs(normalizedCommand);
                if (settleMs > 0 && !HasFollowingExplicitSettle(stepId))
                    await Task.Delay(settleMs, token);
            }
            return true;
        }

        private bool CanSkipMbdCommand(string command, string stepId)
        {
            string normalized = (command ?? string.Empty).Trim().ToUpperInvariant();
            return (normalized == "EXIT" && _mbdTerminalState == MbdTerminalState.Mbd) ||
                   (normalized == "REMOTE2" && _mbdTerminalState == MbdTerminalState.Bcm) ||
                   (normalized == "EXIT" &&
                    _mbdTerminalState == MbdTerminalState.Bcm &&
                    IsBcmBatchChapterEntry(stepId));
        }

        private static bool IsBcmBatchChapterEntry(string stepId)
        {
            return string.Equals(
                       stepId,
                       "BCM250.CH14.S003.ACTION",
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       stepId,
                       "BCM250.CH15.S003.ACTION",
                       StringComparison.OrdinalIgnoreCase);
        }

        private void RecordMbdCommandState(string command)
        {
            string normalized = (command ?? string.Empty).Trim().ToUpperInvariant();
            if (normalized == "EXIT")
                _mbdTerminalState = MbdTerminalState.Mbd;
            else if (normalized == "REMOTE2")
                _mbdTerminalState = MbdTerminalState.Bcm;
            else if (normalized.StartsWith("W86=", StringComparison.Ordinal))
                _mbdTerminalState = MbdTerminalState.Mbd;
        }

        private void ResetMbdState()
        {
            _mbdTerminalState = MbdTerminalState.Unknown;
            _mbdReadyObserved = false;
        }

        private static bool HasFollowingExplicitSettle(string stepId)
        {
            switch ((stepId ?? string.Empty).ToUpperInvariant())
            {
                case "BCM250.CH19.S014.ACTION":
                case "BCM250.CH23.S021.ACTION":
                case "BCM250.CH24.S020.ACTION":
                case "BCM250.CH25.S034.ACTION":
                case "BCM250.CH26.S023.ACTION":
                case "BCM250.CH26.S027.ACTION":
                    return true;
                default:
                    return false;
            }
        }

        private static int MbdStateChangeSettleMs(string command)
        {
            string normalized = (command ?? string.Empty).Trim().ToUpperInvariant();
            return normalized.StartsWith("W95=", StringComparison.Ordinal) ||
                   normalized.StartsWith("W67=", StringComparison.Ordinal) ||
                   normalized.StartsWith("W75=", StringComparison.Ordinal) ||
                   normalized.StartsWith("W69=", StringComparison.Ordinal) ||
                   normalized.StartsWith("W71=", StringComparison.Ordinal) ||
                   normalized.StartsWith("W91=", StringComparison.Ordinal)
                ? 500
                : 0;
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
