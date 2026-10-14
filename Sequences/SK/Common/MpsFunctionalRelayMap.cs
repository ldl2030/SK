namespace TestPlatform.TestSequences
{
    /// <summary>Shared DAQ960 functional relay wiring of the two MPS fixtures.</summary>
    public static class MpsFunctionalRelayMap
    {
        public const int ExternalPowerRelay = 2;
        public const int WithdrawalRelay = 3;
        public const int Contact80VRelay = 4;
        public const int SensPrecRelay = 5;
        public const int IsoBattRelay = 6;
        public const string AllControlledRelays = "2,3,4,5,6";
        public const string AllDaqControlRelays = "@302:306,308";
        public const string WithdrawalDaqRelay = "@308";
        public const string Contact80VDaqRelay = "@302";
        public const string SensPrecDaqRelay = "@303";
        public const string IsoBattDaqRelay = "@304";
        public const string ExternalPowerDaqRelays = "@305,306";

        public static string GetDaqRelayChannels(int logicalRelay)
        {
            switch (logicalRelay)
            {
                case ExternalPowerRelay: return ExternalPowerDaqRelays;
                case WithdrawalRelay: return WithdrawalDaqRelay;
                case Contact80VRelay: return Contact80VDaqRelay;
                case SensPrecRelay: return SensPrecDaqRelay;
                case IsoBattRelay: return IsoBattDaqRelay;
                default: return null;
            }
        }
    }
}
