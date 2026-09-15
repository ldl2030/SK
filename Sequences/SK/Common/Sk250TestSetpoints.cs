using System.Globalization;

namespace TestPlatform.TestSequences
{
    /// <summary>
    /// Immutable source/load setting used by the 250-series action catalogues.
    /// The third value is OVP for a power source and maximum power for a load.
    /// </summary>
    public sealed class Sk250SourceSetpoint
    {
        public Sk250SourceSetpoint(double voltage, double current, double protection)
        {
            Voltage = voltage;
            Current = current;
            Protection = protection;
        }

        public double Voltage { get; }
        public double Current { get; }
        public double Protection { get; }

        public string ToArgument()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1},{2}",
                Voltage,
                Current,
                Protection);
        }
    }

    /// <summary>
    /// Central edit point for BCM-250 and MPS-250 programmed voltage/current values.
    /// Keep board-specific groups separate because equal voltages can have different
    /// current limits and safety requirements on the two fixtures.
    /// </summary>
    public static class Sk250TestSetpoints
    {
        public static class Common
        {
            public static readonly Sk250SourceSetpoint OutputOffOvp300 =
                new Sk250SourceSetpoint(0, 0, 300);

            public static readonly Sk250SourceSetpoint OutputOffOvp10 =
                new Sk250SourceSetpoint(0, 0, 10);

            public static readonly Sk250SourceSetpoint LoadOff =
                new Sk250SourceSetpoint(0, 0, 0);
        }

        public static class Bcm250
        {
            public const double MpsBoardVoltage = 275;
            public const double MpsBoardCurrent = 1;

            public static readonly Sk250SourceSetpoint VbusCalibration =
                new Sk250SourceSetpoint(275, 0.1, 300);
            public static readonly Sk250SourceSetpoint VmidHigh =
                new Sk250SourceSetpoint(125, 0.1, 300);
            public static readonly Sk250SourceSetpoint VmidLow =
                new Sk250SourceSetpoint(5, 0.1, 300);
            public static readonly Sk250SourceSetpoint DischargeOneAmp =
                new Sk250SourceSetpoint(12, 1, 16);
            public static readonly Sk250SourceSetpoint DischargeSixAmp =
                new Sk250SourceSetpoint(12, 6, 16);
            public static readonly Sk250SourceSetpoint MosfetRampStart =
                new Sk250SourceSetpoint(5, 0.3, 10);
            public static readonly Sk250SourceSetpoint MosfetRampEnd =
                new Sk250SourceSetpoint(5, 1.3, 10);
            public static readonly Sk250SourceSetpoint ShortProtection =
                new Sk250SourceSetpoint(275, 1, 300);
            public static readonly Sk250SourceSetpoint VstrNegLow =
                new Sk250SourceSetpoint(1, 1, 300);
            public static readonly Sk250SourceSetpoint VstrNegHigh =
                new Sk250SourceSetpoint(15, 1, 300);
            public static readonly Sk250SourceSetpoint ChargeCalibration =
                new Sk250SourceSetpoint(25, 6, 300);
            public static readonly Sk250SourceSetpoint ChargeVerificationZero =
                new Sk250SourceSetpoint(0, 0, 300);
            public static readonly Sk250SourceSetpoint ChargeVerificationSixAmp =
                new Sk250SourceSetpoint(15, 6, 300);
            public static readonly Sk250SourceSetpoint ChargeVerificationTenVolt =
                new Sk250SourceSetpoint(10, 6, 300);
            public static readonly Sk250SourceSetpoint CcbVbus =
                new Sk250SourceSetpoint(275, 10, 300);
            public static readonly Sk250SourceSetpoint CcbString =
                new Sk250SourceSetpoint(225, 0.2, 300);
            public static readonly Sk250SourceSetpoint CcbLoad =
                new Sk250SourceSetpoint(250, 7, 2000);
            public static readonly Sk250SourceSetpoint CcbLoadHold =
                new Sk250SourceSetpoint(255, 7, 2000);
            public static readonly Sk250SourceSetpoint MosTestVbus =
                new Sk250SourceSetpoint(100, 0.5, 300);
            public static readonly Sk250SourceSetpoint MosTestString =
                new Sk250SourceSetpoint(90, 3, 300);

            // MBD register test targets. Change these values here, not in the catalogue.
            public const int CcbCalibrationCurrentMa = 6000;
            public const int CcbHardwareTestCurrentMa = 6000;
            public const int CcbVoltageTargetMv = 267000;
        }

        public static class Mps250
        {
            public static readonly Sk250SourceSetpoint VbusNominal =
                new Sk250SourceSetpoint(275, 2, 300);
            public static readonly Sk250SourceSetpoint VbusIgnitionStart =
                new Sk250SourceSetpoint(5, 0.5, 300);
            public static readonly Sk250SourceSetpoint VbattCalibration =
                new Sk250SourceSetpoint(275, 0.1, 300);
            public static readonly Sk250SourceSetpoint VbattZero =
                new Sk250SourceSetpoint(0, 0.1, 300);
            public static readonly Sk250SourceSetpoint VbattIsolation =
                new Sk250SourceSetpoint(250, 0.1, 300);

            public const double IgnitionRampStartVoltage = 30;
            public const double IgnitionRampEndVoltage = 85;
            public const double IgnitionRampStepVoltage = 1;
        }
    }
}
