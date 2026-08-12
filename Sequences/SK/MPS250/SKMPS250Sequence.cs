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

        protected override void AddElectricalTestSteps(List<TestStepItem> steps, TestSequenceContext context, SK441Device device)
        {
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
                PowerCritical = IsPowerCritical(action),
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
                case Mps250ActionKind.PowerRelays:
                    passed = await SetPowerRelaysAsync(device, ParseRelayList(action.Argument), action.State);
                    break;
                case Mps250ActionKind.CheckMbdCommunication:
                    passed = await device.CheckMbdCommunicationAsync();
                    break;
                case Mps250ActionKind.CloseMbdCommunication:
                    passed = action.AlwaysRun ? await device.CloseTargetBoardCommunicationAsync() : true;
                    break;
                case Mps250ActionKind.MbdCommand:
                    passed = await SendCommandsAsync(device, action.Argument);
                    break;
                case Mps250ActionKind.SetVbus:
                    passed = await SetGeneratorAsync(device, action.Argument, true, action.State);
                    break;
                case Mps250ActionKind.SetVbatt:
                    passed = await SetGeneratorAsync(device, action.Argument, false, action.State);
                    break;
                case Mps250ActionKind.SafeVbusRelay:
                    {
                        double[] values = ParseNumbers(action.Argument);
                        passed = values.Length >= 5 && await device.SafeSwitchMpsVbusRelayAsync(
                            (int)values[0], action.State, (float)values[1], (float)values[2], (float)values[3], values[4] > 0);
                        break;
                    }
                case Mps250ActionKind.SafeVbattConnect:
                    passed = await device.SafeConnectMpsVbattRelayAsync(ParseInt(action.Argument), 300);
                    break;
                case Mps250ActionKind.SafeVbattDisconnect:
                    passed = await device.SafeDisconnectMpsVbattRelayAsync(ParseInt(action.Argument), 300);
                    break;
                case Mps250ActionKind.DaqVoltage:
                    await device.MeasureDaqChannelVoltageAsync(ParseInt(action.Argument));
                    passed = true;
                    break;
                case Mps250ActionKind.DocumentedBoundary:
                    passed = true;
                    break;
                case Mps250ActionKind.Cleanup:
                    passed = await device.CloseTargetBoardCommunicationAsync();
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
                    return ScaleVoltage(await ReadDaqAverageAsync(device, action.Channel, action.SampleCount, action.SampleIntervalMs, token), unit);
                case Mps250ActionKind.DaqShuntCurrent:
                    return await ReadDaqAverageAsync(device, action.Channel, action.SampleCount, action.SampleIntervalMs, token) / action.Scale * 1000.0;
                case Mps250ActionKind.DaqResistance:
                    return await device.MeasureDaqChannelResistanceAsync(action.Channel);
                case Mps250ActionKind.IgnitionRamp:
                    return await RunIgnitionRampAsync(device, action, token);
                case Mps250ActionKind.ComputedGain:
                    return await ComputeGainAsync(device, context, action);
                default:
                    return await ReadMbdAverageAsync(device, action.Argument, action.SampleCount, action.SampleIntervalMs, token);
            }
        }

        private static bool IsPowerCritical(Mps250ActionDefinition action)
        {
            if (action.AlwaysRun)
                return false;
            switch (action.Kind)
            {
                case Mps250ActionKind.SetVbus:
                case Mps250ActionKind.SetVbatt:
                case Mps250ActionKind.SafeVbusRelay:
                case Mps250ActionKind.SafeVbattConnect:
                case Mps250ActionKind.SafeVbattDisconnect:
                case Mps250ActionKind.PowerRelays:
                case Mps250ActionKind.DaqVoltage:
                case Mps250ActionKind.DaqAverageVoltage:
                case Mps250ActionKind.DaqShuntCurrent:
                case Mps250ActionKind.DaqResistance:
                case Mps250ActionKind.IgnitionRamp:
                case Mps250ActionKind.ComputedGain:
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
                return 30.0;
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
