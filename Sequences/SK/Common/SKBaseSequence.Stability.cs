using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TestPlatform.TestSequences
{
    public abstract partial class SKBaseSequence
    {
        protected sealed class StabilityMeasurement
        {
            public TestStepItem Step { get; set; }
            public bool IsCurrent { get; set; }
            public int? TransitionVoltageChannel { get; set; }
            public Func<CancellationToken, Task<StepResult>> MeasureAsync { get; set; }
        }

        protected static string[] GetMpsHeaterStabilityResultIds(string stepId)
        {
            if (string.IsNullOrEmpty(stepId) ||
                !(stepId.StartsWith("MPS125.", StringComparison.Ordinal) || stepId.StartsWith("MPS250.", StringComparison.Ordinal)))
                return Array.Empty<string>();
            switch (stepId.Substring(6))
            {
                case ".CH17.S016.ACTION": return new[] { "L06", "L07" };
                case ".CH17.S021.ACTION": return new[] { "L08" };
                case ".CH17.S025.ACTION": return new[] { "L10", "L11" };
                case ".CH17.S030.ACTION": return new[] { "L12" };
                case ".CH17.S036.ACTION": return new[] { "L14", "L15", "L16" };
                default: return Array.Empty<string>();
            }
        }

        protected async Task<StepResult> RunHeaterStabilityWaitAsync(
            TestStepItem step, TestSequenceContext context, SK441Device device,
            StabilityMeasurement[] candidates, Dictionary<string, StepResult> earlyResults,
            CancellationToken token)
        {
            int maximumWaitMs = step.PlanDefinition.DelayMs.Value;
            string[] resultIds = GetMpsHeaterStabilityResultIds(step.StepId);
            string configPrefix = "SK" + ExpectedBoardType.Replace("-", string.Empty);
            bool configured;
            bool enabled = !bool.TryParse(ConfigurationManager.AppSettings[configPrefix + "HeaterEarlyMeasurementEnabled"], out configured) || configured;
            bool eligible = resultIds.Length > 0 && candidates.Length == resultIds.Length && candidates.All(c =>
                c.Step.PlanDefinition != null && c.Step.PlanDefinition.LowerLimit.HasValue && c.Step.PlanDefinition.UpperLimit.HasValue &&
                (context.TestPlan == null || context.TestPlan.ShouldRun(c.Step.StepId)));
            if (device.SkipComInit || !enabled || !eligible || maximumWaitMs == 0)
            {
                await Task.Delay(device.SkipComInit ? Math.Min(maximumWaitMs, 1) : maximumWaitMs, token);
                return new StepResult { IsPass = true, Value = maximumWaitMs + " ms" };
            }
            int minimumSettleMs = (int)ReadPositiveStabilitySetting(configPrefix + "HeaterMinimumSettleMs", 1000);
            double voltageDeltaMv = ReadPositiveStabilitySetting(configPrefix + "HeaterStableVoltageDeltaMv", 20);
            double currentDeltaMa = ReadPositiveStabilitySetting(configPrefix + "HeaterStableCurrentDeltaMa", 2);
            double[] deltas = candidates.Select(c =>
            {
                TestPlanStepDefinition plan = c.Step.PlanDefinition;
                double delta = c.IsCurrent ? currentDeltaMa : voltageDeltaMv;
                if (!c.IsCurrent && plan.Unit == "V") delta /= 1000;
                return Math.Min(delta, Math.Max(0, plan.UpperLimit.Value - plan.LowerLimit.Value) * 0.02);
            }).ToArray();
            StepResult[] latestResults = null;
            bool transitionReady = false;
            var timer = Stopwatch.StartNew();
            OnLogInfo(ExpectedBoardType + " " + step.StepId + " 稳定等待内提前测量：" + string.Join(",", resultIds) +
                "；上限=" + maximumWaitMs + "ms，连续两轮完整测量反馈合格、电流读数有效且变化不超过[" +
                string.Join(",", deltas.Select(x => x.ToString("0.###", CultureInfo.InvariantCulture))) + "]（各项XML单位）。");
            bool stable = await WaitForStableMeasurementsAsync(maximumWaitMs, minimumSettleMs, 200,
                async probeToken =>
                {
                    // The ON feedback starts near 8-9V. A wide-range single reading
                    // gates the original 1V full averages; gate readings are never cached.
                    if (!transitionReady)
                    {
                        foreach (StabilityMeasurement candidate in candidates.Where(c => c.TransitionVoltageChannel.HasValue))
                        {
                            double volts = await device.MeasureDaqChannelVoltageAverageAsync(
                                candidate.TransitionVoltageChannel.Value, 1, 0, probeToken, false, 10);
                            double value = candidate.Step.PlanDefinition.Unit == "V" ? volts : volts * 1000;
                            TestPlanStepDefinition plan = candidate.Step.PlanDefinition;
                            bool ready = value >= plan.LowerLimit.Value && value <= plan.UpperLimit.Value;
                            OnLogInfo(ExpectedBoardType + " " + step.StepId + " 开启过渡探测：DAQ" +
                                candidate.TransitionVoltageChannel.Value + "，量程=10V，反馈=" +
                                volts.ToString("0.######", CultureInfo.InvariantCulture) + "V，耗时=" +
                                timer.ElapsedMilliseconds + "ms，" + (ready ? "进入目标范围，开始完整测量。" : "尚未进入目标范围，继续探测。"));
                            if (!ready) return null;
                        }
                        transitionReady = true;
                    }
                    var results = new StepResult[candidates.Length];
                    try
                    {
                        for (int i = 0; i < candidates.Length; i++)
                        {
                            probeToken.ThrowIfCancellationRequested();
                            results[i] = await candidates[i].MeasureAsync(probeToken);
                            probeToken.ThrowIfCancellationRequested();
                        }
                    }
                    catch (Exception ex) when (IsTransientDaqMeasurementFailure(ex))
                    {
                        transitionReady = false;
                        throw;
                    }
                    latestResults = results;
                    if (results.Where((result, index) => !candidates[index].IsCurrent).Any(result => !result.IsPass))
                        transitionReady = false;
                    // A settled current outside the XML limits remains NG on its
                    // original row, but its range offset need not consume the wait.
                    return results.Select((result, index) => result.NumericValue.HasValue &&
                        (result.IsPass || candidates[index].IsCurrent)).All(valid => valid)
                        ? results.Select(x => x.NumericValue.Value).ToArray() : null;
                },
                (previous, current) => current.Select((value, i) => Math.Abs(value - previous[i]) <= deltas[i]).All(x => x),
                ex => OnLogWarning(ExpectedBoardType + " " + step.StepId +
                    (IsTransientDaqMeasurementFailure(ex) ? " 提前测量遇到无效读数，重置连续轮次并在原窗口内继续探测：" :
                        " 提前测量异常，保留原等待和正式测量：") + ex.Message), token);
            token.ThrowIfCancellationRequested();
            if (stable)
            {
                for (int i = 0; i < candidates.Length; i++) earlyResults[candidates[i].Step.StepId] = latestResults[i];
                OnLogInfo(ExpectedBoardType + " " + step.StepId + " 连续两轮测量稳定，提前结束等待：耗时=" + timer.ElapsedMilliseconds +
                    "ms，原上限=" + maximumWaitMs + "ms；结果将在原测试行记录。");
            }
            else
            {
                earlyResults.Clear();
                OnLogInfo(ExpectedBoardType + " " + step.StepId + " 未提前确认稳定，已完成原等待；丢弃探测值，后续按原计划重新测量。");
            }
            return new StepResult { IsPass = true, Value = timer.ElapsedMilliseconds + " ms / " + maximumWaitMs + " ms" +
                (stable ? "（提前稳定）" : "（原计划测量）") };
        }

        private static double ReadPositiveStabilitySetting(string key, double fallback)
        {
            double value;
            return double.TryParse(ConfigurationManager.AppSettings[key], NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                value > 0 && value <= int.MaxValue && !double.IsInfinity(value) ? value : fallback;
        }
        // Await every probe to completion, including DAQ cleanup. No abandoned I/O
        // may overlap the formal measurement after the original wait expires.
        protected static async Task<bool> WaitForStableMeasurementsAsync(
            int maximumWaitMs, int minimumSettleMs, int pollIntervalMs,
            Func<CancellationToken, Task<double[]>> measureAsync,
            Func<double[], double[], bool> isStable, Action<Exception> onProbeFailure,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var timer = Stopwatch.StartNew();
            using (var probeCts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                probeCts.CancelAfter(maximumWaitMs);
                try
                {
                    await Task.Delay(Math.Min(minimumSettleMs, maximumWaitMs), probeCts.Token);
                    double[] previous = null;
                    while (timer.ElapsedMilliseconds < maximumWaitMs)
                    {
                        double[] current;
                        try
                        {
                            current = await measureAsync(probeCts.Token);
                        }
                        catch (Exception ex) when (IsTransientDaqMeasurementFailure(ex))
                        {
                            probeCts.Token.ThrowIfCancellationRequested();
                            previous = null;
                            onProbeFailure?.Invoke(ex);
                            await Task.Delay(pollIntervalMs, probeCts.Token);
                            continue;
                        }
                        probeCts.Token.ThrowIfCancellationRequested();
                        bool valid = current != null && current.Length > 0 &&
                            current.All(x => !double.IsNaN(x) && !double.IsInfinity(x) && Math.Abs(x) < 9E36);
                        if (valid && previous != null && previous.Length == current.Length &&
                            isStable(previous, current) && timer.ElapsedMilliseconds < maximumWaitMs)
                            return true;
                        previous = valid ? current : null;
                        await Task.Delay(pollIntervalMs, probeCts.Token);
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested && probeCts.IsCancellationRequested)
                {
                    // Only the probe budget expired; the wait row still passes.
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    token.ThrowIfCancellationRequested();
                    onProbeFailure?.Invoke(ex);
                }
            }
            token.ThrowIfCancellationRequested();
            int remainingMs = (int)Math.Max(0, maximumWaitMs - timer.ElapsedMilliseconds);
            await Task.Delay(remainingMs, token);
            return false;
        }

        protected async Task<double> ReadDaqVoltageAverageWithRangeRecoveryAsync(
            SK441Device device, int channel, int count, int intervalMs, CancellationToken token, double? rangeVolts)
        {
            try
            {
                return await device.MeasureDaqChannelVoltageAverageAsync(
                    channel, Math.Max(1, count), intervalMs, token, rangeVolts: rangeVolts);
            }
            catch (Exception ex) when (rangeVolts == 1 && (channel == 107 || channel == 108) &&
                IsTransientDaqMeasurementFailure(ex))
            {
                token.ThrowIfCancellationRequested();
                OnLogWarning(ExpectedBoardType + " 加热器反馈 DAQ" + channel +
                    " 原1V量程读数无效，改用10V量程重新完整测量以显示真实反馈；仍按原XML范围判定。");
                return await device.MeasureDaqChannelVoltageAverageAsync(
                    channel, Math.Max(1, count), intervalMs, token, rangeVolts: 10);
            }
        }

        private static bool IsTransientDaqMeasurementFailure(Exception exception)
        {
            for (Exception current = exception; current != null; current = current.InnerException)
                if (current is DaqInvalidMeasurementException) return true;
            return false;
        }
    }
}
