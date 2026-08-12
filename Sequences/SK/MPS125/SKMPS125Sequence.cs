using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TestPlatform.TestSequences
{
    public sealed class SKMPS125Sequence : SKBcmSequenceBase
    {
        public override string SequenceKey => "SK_MPS125_Sequence";
        public override string ExpectedBoardType => "MPS-125";

        public SKMPS125Sequence(ITestGridService grid = null, Func<string, Task<bool>> confirmAsync = null)
            : base(grid, confirmAsync)
        {
        }

        protected override void AddElectricalTestSteps(List<TestStepItem> steps, TestSequenceContext context, SK441Device device)
        {
            AssignPlanGroupToRange(steps, context, Mps125GroupIds.Precheck, 0, steps.Count);
            IReadOnlyList<Mps125ActionDefinition> catalog = Mps125ActionCatalog.Create();

            foreach (IGrouping<int, Mps125ActionDefinition> chapter in catalog
                .Where(x => !x.AlwaysRun)
                .GroupBy(x => x.Chapter)
                .OrderBy(x => x.Key))
            {
                string groupId = Mps125GroupIds.Chapter(chapter.Key);
                AddPlanChapter(steps, context, groupId, () =>
                {
                    foreach (Mps125ActionDefinition action in chapter)
                        AddCatalogStep(steps, context, device, action, groupId);
                });
            }

            AddPlanChapter(steps, context, Mps125GroupIds.Cleanup, () =>
            {
                foreach (Mps125ActionDefinition action in catalog.Where(x => x.AlwaysRun))
                    AddCatalogStep(steps, context, device, action, Mps125GroupIds.Cleanup);
            });
        }

        private void AddCatalogStep(IList<TestStepItem> steps, TestSequenceContext context, SK441Device device, Mps125ActionDefinition action, string groupId)
        {
            var step = new TestStepItem
            {
                Name = action.Name,
                StepId = action.StepId,
                ExpectedGroupId = groupId,
                RowIndex = steps.Count,
                AlwaysRun = action.AlwaysRun,
                PowerCritical = IsPowerCritical(action),
                IsFixtureReleaseStep = action.AlwaysRun && action.Kind == Mps125ActionKind.Cleanup
            };
            step.Action = token => ExecuteActionAsync(step, context, device, action, token);
            steps.Add(step);
        }

        private async Task<StepResult> ExecuteActionAsync(TestStepItem step, TestSequenceContext context, SK441Device device, Mps125ActionDefinition action, CancellationToken token)
        {
            string faultStep = Environment.GetEnvironmentVariable("TESTPLATFORM_MPS125_FAULT_STEP");
            if (device.SkipComInit && !string.IsNullOrWhiteSpace(faultStep) &&
                string.Equals(faultStep, action.StepId, StringComparison.OrdinalIgnoreCase))
                return BasicResult(false, "Injected fault", action);

            if (action.Kind == Mps125ActionKind.Delay)
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
                case Mps125ActionKind.CheckInstruments:
                    passed = await device.CheckMpsRequiredInstrumentsAsync();
                    break;
                case Mps125ActionKind.AllOutputsOff:
                    passed = await device.TurnOffAllInstrumentsAsync();
                    break;
                case Mps125ActionKind.PowerRelays:
                    passed = await SetPowerRelaysAsync(device, ParseRelayList(action.Argument), action.State);
                    break;
                case Mps125ActionKind.CheckMbdCommunication:
                    passed = await device.CheckMbdCommunicationAsync();
                    break;
                case Mps125ActionKind.CloseMbdCommunication:
                    passed = action.AlwaysRun ? await device.CloseTargetBoardCommunicationAsync() : true;
                    break;
                case Mps125ActionKind.MbdCommand:
                    passed = await SendCommandsAsync(device, action.Argument);
                    break;
                case Mps125ActionKind.SetVbus:
                    passed = await SetGeneratorAsync(device, action.Argument, true, action.State);
                    break;
                case Mps125ActionKind.SetVbatt:
                    passed = await SetGeneratorAsync(device, action.Argument, false, action.State);
                    break;
                case Mps125ActionKind.SafeVbusRelay:
                    {
                        double[] values = ParseNumbers(action.Argument);
                        passed = values.Length >= 5 && await device.SafeSwitchMpsVbusRelayAsync(
                            (int)values[0], action.State, (float)values[1], (float)values[2], (float)values[3], values[4] > 0);
                        break;
                    }
                case Mps125ActionKind.SafeVbattConnect:
                    passed = await device.SafeConnectMpsVbattRelayAsync(ParseInt(action.Argument), 150);
                    break;
                case Mps125ActionKind.SafeVbattDisconnect:
                    passed = await device.SafeDisconnectMpsVbattRelayAsync(ParseInt(action.Argument), 150);
                    break;
                case Mps125ActionKind.DaqVoltage:
                    await device.MeasureDaqChannelVoltageAsync(ParseInt(action.Argument));
                    passed = true;
                    break;
                case Mps125ActionKind.DocumentedBoundary:
                    passed = true;
                    break;
                case Mps125ActionKind.Cleanup:
                    passed = await device.CloseTargetBoardCommunicationAsync();
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

            double value = device.SkipComInit
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
                Value = value.ToString("0.###", CultureInfo.InvariantCulture),
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
                    return ScaleVoltage(await ReadDaqAverageAsync(device, action.Channel, action.SampleCount, action.SampleIntervalMs, token), unit);
                case Mps125ActionKind.DaqShuntCurrent:
                    return await ReadDaqAverageAsync(device, action.Channel, action.SampleCount, action.SampleIntervalMs, token) / action.Scale * 1000.0;
                case Mps125ActionKind.DaqResistance:
                    return await device.MeasureDaqChannelResistanceAsync(action.Channel);
                case Mps125ActionKind.IgnitionRamp:
                    return await RunIgnitionRampAsync(device, action, token);
                case Mps125ActionKind.ComputedGain:
                    return await ComputeGainAsync(device, context, action);
                default:
                    return await ReadMbdAverageAsync(device, action.Argument, action.SampleCount, action.SampleIntervalMs, token);
            }
        }

        private static bool IsPowerCritical(Mps125ActionDefinition action)
        {
            if (action.AlwaysRun)
                return false;
            switch (action.Kind)
            {
                case Mps125ActionKind.SetVbus:
                case Mps125ActionKind.SetVbatt:
                case Mps125ActionKind.SafeVbusRelay:
                case Mps125ActionKind.SafeVbattConnect:
                case Mps125ActionKind.SafeVbattDisconnect:
                case Mps125ActionKind.PowerRelays:
                case Mps125ActionKind.DaqVoltage:
                case Mps125ActionKind.DaqAverageVoltage:
                case Mps125ActionKind.DaqShuntCurrent:
                case Mps125ActionKind.DaqResistance:
                case Mps125ActionKind.IgnitionRamp:
                case Mps125ActionKind.ComputedGain:
                    return true;
                default:
                    return false;
            }
        }

        private static async Task<bool> SetPowerRelaysAsync(SK441Device device, int[] ids, bool state)
        {
            bool passed = true;
            foreach (int id in ids)
                passed = await device.ControlRelayAsync(id, state) && passed;
            return passed;
        }

        private static async Task<double> ReadDaqAverageAsync(SK441Device device, int channel, int count, int intervalMs, CancellationToken token)
        {
            double sum = 0;
            int samples = Math.Max(1, count);
            for (int i = 0; i < samples; i++)
            {
                token.ThrowIfCancellationRequested();
                sum += await device.MeasureDaqChannelVoltageAsync(channel);
                if (intervalMs > 0 && i + 1 < samples)
                    await Task.Delay(intervalMs, token);
            }
            return sum / samples;
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

        private static async Task<double> RunIgnitionRampAsync(SK441Device device, Mps125ActionDefinition action, CancellationToken token)
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

        private static async Task<double> ComputeGainAsync(SK441Device device, TestSequenceContext context, Mps125ActionDefinition action)
        {
            double adc;
            if (!TryGetStored(context, action.Argument, out adc) || Math.Abs(adc) < 0.000001)
                throw new InvalidOperationException("Missing/zero MPS-125 gain dependency " + action.Argument);
            double vbusMv = await device.MeasureDaqChannelVoltageAsync(action.Channel) * 1000.0;
            return 4096.0 * vbusMv / adc;
        }

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

        private static async Task<bool> SetGeneratorAsync(SK441Device device, string argument, bool vbus, bool state)
        {
            double[] values = ParseNumbers(argument);
            if (values.Length < 3)
                return false;
            return vbus
                ? await device.SetVbusGeneratorAsync((float)values[0], (float)values[1], (float)values[2], state)
                : await device.SetVbattGeneratorAsync((float)values[0], (float)values[1], (float)values[2], state);
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
