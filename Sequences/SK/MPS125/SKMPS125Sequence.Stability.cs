using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TestPlatform.TestSequences
{
    public sealed partial class SKMPS125Sequence
    {
        private static string[] GetHeaterStabilityResultIds(string stepId) => GetMpsHeaterStabilityResultIds(stepId);

        private async Task<StepResult> ExecuteStabilityDelayAsync(
            TestStepItem step, IList<TestStepItem> steps, TestSequenceContext context,
            SK441Device device, Mps125ActionDefinition action,
            IReadOnlyList<Mps125ActionDefinition> catalog,
            Dictionary<string, StepResult> earlyResults, CancellationToken token)
        {
            if (device.SkipComInit && string.Equals(Environment.GetEnvironmentVariable("TESTPLATFORM_MPS125_FAULT_STEP"),
                action.StepId, StringComparison.OrdinalIgnoreCase))
                return BasicResult(false, "Injected fault", action);
            string[] resultIds = GetHeaterStabilityResultIds(step.StepId);
            // Never cross a write/relay/delay boundary or measure a deselected row.
            StabilityMeasurement[] candidates = catalog.SkipWhile(x => x.StepId != step.StepId).Skip(1)
                .TakeWhile(x => x.IsResult).Where(x => resultIds.Contains(x.ResultId)).Select(x =>
                {
                    TestStepItem resultStep = steps.Single(s => s.StepId == x.StepId);
                    return new StabilityMeasurement
                    {
                        Step = resultStep,
                        IsCurrent = x.Kind == Mps125ActionKind.DaqShuntCurrent,
                        TransitionVoltageChannel = x.Kind == Mps125ActionKind.DaqAverageVoltage && x.DaqRangeVolts < 10
                            ? (int?)x.Channel : null,
                        MeasureAsync = probeToken => ExecuteResultAsync(resultStep, context, device, x, probeToken)
                    };
                }).ToArray();
            return await RunHeaterStabilityWaitAsync(step, context, device, candidates, earlyResults, token);
        }
    }
}
