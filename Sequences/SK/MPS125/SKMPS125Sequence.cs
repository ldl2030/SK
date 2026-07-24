using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TestPlatform.TestSequences
{
    public class SKMPS125Sequence : SKBaseSequence
    {
        public override string SequenceKey => "SK_MPS125_Sequence";

        public override string ExpectedBoardType => "MPS-125";

        public SKMPS125Sequence(
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
