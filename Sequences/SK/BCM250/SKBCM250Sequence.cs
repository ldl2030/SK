using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TestPlatform.TestSequences
{
    public class SKBCM250Sequence : SKBaseSequence
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
            AddInitializingToolsStep(steps, deviceManager, ExpectedBoardType);
        }
    }
}
