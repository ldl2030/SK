namespace TestPlatform
{
    /// <summary>
    /// Authoritative BCM-125 physical station mapping.
    /// Keep this aligned with the curated SK/BMS instrument mapping reference.
    /// </summary>
    public static class Bcm125StationMap
    {
        public const int DutPowerRelay = 7;
        public const string ElectronicLoadPowerRelays = "2,5";

        public const int StringGeneratorSourcePowerRelay = 1;
        public const int StringGeneratorRoutePowerRelay = 4;
        public const int StringGeneratorReturnPowerRelay = 6;
        public const string StringGeneratorPowerRelays = "1,4,6";

        public const int Rl2BPowerRelay = 8;
        public const int Rl1aBPowerRelay = 9;
        public const int Rl2APowerRelay = 10;
        public const int Rl1bAPowerRelay = 11;
        public const int Rl9APowerRelay = 12;
        public const int Rl1bBPowerRelay = 13;
        public const int Rl9BPowerRelay = 14;
        public const int Rl1aAPowerRelay = 15;
        public const int Rl1aCPowerRelay = 16;

        public const string AllSwitchingPowerRelays = "8,9,10,11,12,13,14,15,16";
        public const string AllStringPowerPathRelays = "1,4,6,8,9,10,11,12,13,14,15,16";
        public const string PrechargePowerRelays = "9,8";
        public const string DischargeString1PowerRelays = "1,4,6,9,13,10";
        public const string DischargeString2PowerRelays = "1,4,6,9,14,10";
        public const string ChargingString1PowerRelays = "1,4,6,15,11,8";
        public const string ChargingString2PowerRelays = "1,4,6,16,12,8";
        public const string CcbString1PowerRelays = "1,4,6,9,13,8";
        public const string CcbString2PowerRelays = "1,4,6,9,14,8";

        public static int[] GetStringGeneratorPowerRelayIds()
        {
            return new[]
            {
                StringGeneratorSourcePowerRelay,
                StringGeneratorRoutePowerRelay,
                StringGeneratorReturnPowerRelay
            };
        }

        public const int VmidReferenceDaqChannel = 106;
        public const int String1VstrNegativeDaqChannel = 109;
        public const int String2VstrNegativeDaqChannel = 110;
        public const int String1ShuntDaqChannel = 113;
        public const int String2ShuntDaqChannel = 115;
        public const int String1VoltageDaqChannel = 114;
        public const int String2VoltageDaqChannel = 116;
        public const double String1ShuntOhms = 0.0215909;
        public const double String2ShuntOhms = 0.0215914;

        public const string ProgrammingBcm1DaqRelay = "@305";
        public const string ProgrammingBcm2DaqRelay = "@306";
        public const string MidpointDaqRelay = "@301";
        public const string ShortProtectionSelectorDaqRelay = "@304";
        public const string String1SimulatorDaqRelay = "@302";
        public const string String2SimulatorDaqRelay = "@303";
        public const string StringSimulatorDaqRelays = "@302,@303";
        public const string PlacementRelayA = "@318";
        public const string PlacementRelayB = "@319";

        public const string ProgrammingBcm1FixtureRelays = "3,4,5";
        public const string ProgrammingBcm2FixtureRelays = "6,7,8";
    }
}
