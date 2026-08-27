using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TestPlatform.TestSequences
{
    public partial class SKBCM125Sequence : SKBcmSequenceBase
    {
        private readonly bool _skipFirmwareProgramming;
        private Bcm125MbdTerminalState _mbdTerminalState;

        private enum Bcm125MbdTerminalState
        {
            Unknown,
            Mbd,
            Bcm1,
            Bcm2
        }

        public override string SequenceKey => "SK_BCM125_Sequence";

        public override string ExpectedBoardType => "BCM-125";

        protected override bool SkipFirmwareProgramming => _skipFirmwareProgramming;

        public SKBCM125Sequence(
            ITestGridService grid = null,
            Func<string, Task<bool>> confirmAsync = null,
            bool skipFirmwareProgramming = false)
            : base(grid, confirmAsync)
        {
            _skipFirmwareProgramming = skipFirmwareProgramming;
            _mbdTerminalState = Bcm125MbdTerminalState.Unknown;
        }

        protected override void AddElectricalTestSteps(
            List<TestStepItem> steps,
            TestSequenceContext context,
            SK441Device deviceManager)
        {
            AssignPlanGroupToRange(steps, context, Bcm125GroupIds.Precheck, 0, steps.Count);
            AddPlanChapter(steps, context, Bcm125GroupIds.Chapter(7),
                () => AddInitializingToolsStep(steps, deviceManager, ExpectedBoardType));
            AddPlanChapter(steps, context, Bcm125GroupIds.Chapter(8),
                () => AddBcm125FirstStartupSteps(steps, deviceManager));
            AddPlanChapter(steps, context, Bcm125GroupIds.Chapter(9),
                () => AddBcm125ProgrammingSteps(steps, deviceManager));
            AddPlanChapter(steps, context, Bcm125GroupIds.Chapter(10),
                () => AddBcm125CanBusSteps(steps, deviceManager));
            AddPlanChapter(steps, context, Bcm125GroupIds.Chapter(11),
                () => AddBcm125FirmwareVersionSteps(steps, deviceManager));
            AddPlanChapter(steps, context, Bcm125GroupIds.Chapter(12),
                () => AddBcm125ResetFlatCableSteps(steps, context, deviceManager));
            AddPlanChapter(steps, context, Bcm125GroupIds.Chapter(13),
                () => AddBcm125VrefVoltageSteps(steps, deviceManager));
            AddPlanChapter(steps, context, Bcm125GroupIds.Chapter(14),
                () => AddBcm125PowerSupplyVoltageSteps(steps, deviceManager));
            AddPlanChapter(steps, context, Bcm125GroupIds.Chapter(15),
                () => AddBcm125HeatSinkTemperatureSteps(steps, deviceManager));
            AddPlanChapter(steps, context, Bcm125GroupIds.Chapter(16),
                () => AddBcm125VbattScalCalibrationSteps(steps, context, deviceManager));
            AddPlanChapter(steps, context, Bcm125GroupIds.Chapter(17),
                () => AddBcm125WestinghouseSteps(steps, deviceManager));
            AddPlanChapter(steps, context, Bcm125GroupIds.Chapter(18),
                () => AddBcm125PrechargeRelaySteps(steps, deviceManager));
            AddPlanChapter(steps, context, Bcm125GroupIds.Chapter(19),
                () => AddBcm125MidpointCalibrationSteps(steps, context, deviceManager));
            AddPlanChapter(steps, context, Bcm125GroupIds.Chapter(20),
                () => AddBcm125DischargeCurrentSteps(steps, context, deviceManager));
            AddPlanChapter(steps, context, Bcm125GroupIds.Chapter(21),
                () => AddBcm125DischargeMosfetSteps(steps, deviceManager));
            AddPlanChapter(steps, context, Bcm125GroupIds.Chapter(22),
                () => AddBcm125ShortProtectionSteps(steps, deviceManager));
            AddPlanChapter(steps, context, Bcm125GroupIds.Chapter(23),
                () => AddBcm125ChargingCurrentCalibrationSteps(steps, context, deviceManager));
            AddPlanChapter(steps, context, Bcm125GroupIds.Chapter(24),
                () => AddBcm125CurrentAndVstrNegVerificationSteps(steps, deviceManager));
            AddPlanChapter(steps, context, Bcm125GroupIds.Chapter(25),
                () => AddBcm125CcbFunctionalCheckSteps(steps, deviceManager));
            AddPlanChapter(steps, context, Bcm125GroupIds.Chapter(26),
                () => AddBcm125StringTestSteps(steps, deviceManager));
            AddPlanChapter(steps, context, Bcm125GroupIds.Chapter(27),
                () => AddBcm125WritingInfoFieldsSteps(steps, context, deviceManager));
            AddPlanChapter(steps, context, Bcm125GroupIds.Cleanup,
                () => AddBcm125FixtureReleaseSteps(steps, deviceManager));
        }

    }
}
