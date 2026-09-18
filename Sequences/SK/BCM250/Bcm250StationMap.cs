namespace TestPlatform.TestSequences
{
    /// <summary>
    /// BCM-250 fixture wiring. Values follow the curated SK441 station map,
    /// which takes precedence over the legacy 34901/34903 labels in the PDF.
    /// </summary>
    public static class Bcm250StationMap
    {
        public const int FiveVoltDaqChannel = 101;
        public const int VccDaqChannel = 102;
        public const int VeeDaqChannel = 103;
        public const int VbusReferenceDaqChannel = 105;
        public const int VmidReferenceDaqChannel = 106;
        public const int ThreeVoltThreeAnalogDaqChannel = 107;
        public const int V2DaqChannel = 109;
        public const int ThreeVoltThreeDaqChannel = 111;
        public const int StringShuntDaqChannel = 113;
        public const int StringVoltageDaqChannel = 114;
        public const int FiveVoltShuntDaqChannel = 117;
        public const int VccShuntDaqChannel = 119;

        public const double VccShuntOhms = 2.0;
        public const double FiveVoltShuntOhms = 0.4;
        public const double StringShuntOhms = 0.0203111;
        public const double InternalStringShuntOhms = 1.0;
        public const double StringTestShuntOhms = 125.0;

        public const string PlacementDetectionDaqRelays = "318,319";
        public const string MidpointDaqRelay = "301";
        public const string U12ConnectionDaqRelay = "302";
        public const string VoltageSelectorDaqRelay = "304";
        public const string ProgrammingBootDaqRelay = "305";

        public const int InternalOneOhmRelay = 3;
        public const int DutPowerRelay = 7;
        public const string ElectronicLoadPowerRelays = "2,5";
        public const int Rl2B = 8;
        public const int Rl1aB = 9;
        public const int Rl2A = 10;
        public const int Rl1bA = 11;
        public const int Rl1bB = 13;
        public const int Rl1aA = 15;

        public const string ProgrammingFixtureRelays = "2,3,4";
        public const string PrechargePowerPathRelays = "9,8";
        public const string StringGeneratorPowerRelays = "1,3,6";
        public const string DischargeStringPowerPathRelays = "1,3,6,9,13,10";
        public const string ChargeStringPowerPathRelays = "1,3,6,15,11,8";
        public const string CcbStringPowerPathRelays = "1,3,6,9,13,8";
        public const string PowerPathRelays = "1,3,6,8,9,10,11,13,15";
        public const string AllPowerRelays = "1,2,3,5,6,7,8,9,10,11,13,15";
        // Y2/Y5 supply the electronic load and must remain closed during CH7-CH27.
        public const string AllBcm250Relays = "1,3,4,6,7,8,9,10,11,13,15";
    }
}
