using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TestPlatform.TestSequences
{
    public class SKBCM125Sequence : SKBaseSequence
    {
        public override string SequenceKey => "SK_BCM125_Sequence";

        public override string ExpectedBoardType => "BCM-125";

        public SKBCM125Sequence(
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
            AddBcm125FirstStartupSteps(steps, deviceManager);
            AddBcm125ProgrammingSteps(steps, deviceManager);
            AddBcm125CanBusSteps(steps, deviceManager);
            AddBcm125FirmwareVersionSteps(steps, deviceManager);
            AddBcm125ResetFlatCableSteps(steps, context, deviceManager);
            AddBcm125VrefVoltageSteps(steps, deviceManager);
            AddBcm125PowerSupplyVoltageSteps(steps, deviceManager);
            AddBcm125HeatSinkTemperatureSteps(steps, deviceManager);
            AddBcm125VbattScalCalibrationSteps(steps, context, deviceManager);
            AddBcm125WestinghouseSteps(steps, deviceManager);
            AddBcm125PrechargeRelaySteps(steps, deviceManager);
            AddBcm125MidpointCalibrationSteps(steps, context, deviceManager);
            AddBcm125DischargeCurrentSteps(steps, context, deviceManager);
            AddBcm125DischargeMosfetSteps(steps, deviceManager);
            AddBcm125ShortProtectionSteps(steps, deviceManager);
            AddBcm125ChargingCurrentCalibrationSteps(steps, context, deviceManager);
            AddBcm125CurrentAndVstrNegVerificationSteps(steps, deviceManager);
            AddBcm125CcbFunctionalCheckSteps(steps, deviceManager);
            AddBcm125StringTestSteps(steps, deviceManager);
            AddBcm125WritingInfoFieldsSteps(steps, context, deviceManager);
            AddBcm125FixtureReleaseSteps(steps, deviceManager);
        }
    }
}
