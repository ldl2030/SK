using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TestPlatform.TestSequences
{
    public class SKMPS250Sequence : SKBaseSequence
    {
        public override string SequenceKey => "SK_MPS250_Sequence";

        public override string ExpectedBoardType => "MPS-250";

        public SKMPS250Sequence(
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
