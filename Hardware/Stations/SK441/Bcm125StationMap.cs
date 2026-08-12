namespace TestPlatform
{
    /// <summary>
    /// Authoritative BCM-125 physical station mapping.
    /// Keep this aligned with the curated SK/BMS instrument mapping reference.
    /// </summary>
    public static class Bcm125StationMap
    {
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
        public const string ShortProtectionSelectorDaqRelay = "@304";
        public const string String1SimulatorDaqRelay = "@302";
        public const string String2SimulatorDaqRelay = "@303";
        public const string StringSimulatorDaqRelays = "@302,@303";
        public const string PlacementRelayA = "@318";
        public const string PlacementRelayB = "@319";
    }
}
