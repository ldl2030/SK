using System;
using System.Collections.Generic;
using System.Linq;

namespace TestPlatform.TestSequences
{
    /// <summary>
    /// Shared BCM sequence infrastructure. It binds code steps to the
    /// authoritative XML plan without containing board-specific hardware data.
    /// </summary>
    public abstract class SKBcmSequenceBase : SKBaseSequence
    {
        protected SKBcmSequenceBase(
            ITestGridService grid = null,
            Func<string, System.Threading.Tasks.Task<bool>> confirmAsync = null)
            : base(grid, confirmAsync)
        {
        }

        protected static void AddPlanChapter(
            List<TestStepItem> steps,
            TestSequenceContext context,
            string groupId,
            Action addSteps)
        {
            int startIndex = steps.Count;
            addSteps();
            AssignPlanGroupToRange(steps, context, groupId, startIndex, steps.Count - startIndex);
        }

        protected static void AssignPlanGroupToRange(
            IList<TestStepItem> steps,
            TestSequenceContext context,
            string groupId,
            int startIndex,
            int count)
        {
            if (context?.TestPlan == null)
                return;

            var definitions = context.TestPlan.Definition.Steps
                .Where(x =>
                    string.Equals(x.GroupId, groupId, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(x.RunCondition, "OnChapterFailure", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.SequenceOrder)
                .ToList();

            if (definitions.Count != count)
            {
                throw new TestPlanConfigurationException(
                    $"Group {groupId} step count mismatch: code={count}, XML={definitions.Count}.");
            }

            for (int offset = 0; offset < count; offset++)
            {
                TestStepItem step = steps[startIndex + offset];
                TestPlanStepDefinition definition = definitions[offset];
                ValidateStepSemantics(step, definition, groupId);
                if (!string.IsNullOrWhiteSpace(step.StepId) &&
                    !string.Equals(step.StepId, definition.StepId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new TestPlanConfigurationException(
                        $"Explicit StepId mismatch in {groupId}: code={step.StepId}, XML={definition.StepId}.");
                }
                step.StepId = definition.StepId;
                step.ExpectedGroupId = groupId;
                step.PlanDefinition = definition;
                // BCM-125 creates its cleanup rows directly rather than from an
                // action catalogue. Preserve the XML AlwaysRun contract so those
                // rows still run after a failed or cancelled test step.
                step.AlwaysRun = step.AlwaysRun || definition.AlwaysRun;
            }
        }

        protected static void ValidateStepSemantics(
            TestStepItem step,
            TestPlanStepDefinition definition,
            string groupId)
        {
            var groupMatch = System.Text.RegularExpressions.Regex.Match(
                groupId ?? string.Empty,
                @"\.CH(\d+)$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var nameChapterMatch = System.Text.RegularExpressions.Regex.Match(
                step.Name ?? string.Empty,
                @"^(\d+)\.");
            if (groupMatch.Success &&
                nameChapterMatch.Success &&
                !string.Equals(
                    groupMatch.Groups[1].Value.TrimStart('0'),
                    nameChapterMatch.Groups[1].Value.TrimStart('0'),
                    StringComparison.Ordinal))
            {
                throw new TestPlanConfigurationException(
                    $"Step semantic chapter mismatch: code '{step.Name}', XML StepId {definition.StepId}.");
            }

            var resultIdMatch = System.Text.RegularExpressions.Regex.Match(
                definition.StepId ?? string.Empty,
                @"\.([A-Z]\d{2})$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (resultIdMatch.Success &&
                definition.LowerLimit.HasValue &&
                definition.UpperLimit.HasValue &&
                (step.Name ?? string.Empty).IndexOf(
                    resultIdMatch.Groups[1].Value,
                    StringComparison.OrdinalIgnoreCase) < 0)
            {
                throw new TestPlanConfigurationException(
                    $"Step semantic result-id mismatch: code '{step.Name}', XML StepId {definition.StepId}.");
            }
        }

        protected static void GetPlanRange(
            IList<TestStepItem> steps,
            int rowIndex,
            double legacyLower,
            double legacyUpper,
            out double lower,
            out double upper)
        {
            TestPlanStepDefinition definition = RequirePlanDefinition(steps, rowIndex, "numeric");
            if (!definition.LowerLimit.HasValue || !definition.UpperLimit.HasValue)
            {
                throw new TestPlanConfigurationException(
                    $"Numeric StepId {definition.StepId} must define LowerLimit and UpperLimit in XML.");
            }
            lower = definition.LowerLimit.Value;
            upper = definition.UpperLimit.Value;
        }

        protected static string GetPlanUnit(
            IList<TestStepItem> steps,
            int rowIndex,
            string legacyUnit)
        {
            return RequirePlanDefinition(steps, rowIndex, "numeric").Unit;
        }

        protected static int GetPlanDelayMs(
            IList<TestStepItem> steps,
            int rowIndex,
            int legacyDelayMs)
        {
            TestPlanStepDefinition definition = RequirePlanDefinition(steps, rowIndex, "delay");
            if (!definition.DelayMs.HasValue || definition.DelayMs.Value < 0)
            {
                throw new TestPlanConfigurationException(
                    $"Delay StepId {definition.StepId} must define DelayMs in XML.");
            }
            return definition.DelayMs.Value;
        }

        private static TestPlanStepDefinition RequirePlanDefinition(
            IList<TestStepItem> steps,
            int rowIndex,
            string stepKind)
        {
            TestPlanStepDefinition definition = steps[rowIndex].PlanDefinition;
            if (definition == null)
            {
                throw new TestPlanConfigurationException(
                    $"BCM {stepKind} step at index {rowIndex} has no XML definition.");
            }
            return definition;
        }
    }
}
