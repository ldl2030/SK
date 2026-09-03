namespace TestPlatform.TestSequences
{
    public static class Mps250StationMap
    {
        // VBATT/source B uses station-3 high-power relays Y1 and Y9. Logical
        // Relay_2-Relay_6 remain on the DAQ960 34903A functional relay bank.
        public const int VbattSourcePowerRelay = SK441Device.MpsVbattSourcePowerRelay;
        public const int VbattDutPowerRelay = SK441Device.MpsVbattDutPowerRelay;
        public const string VbattPowerRelays = "1,9";
        public const int ExternalPowerRelay = 2;
        public const int WithdrawalRelay = 3;
        public const int Contact80VRelay = 4;
        public const int SensPrecRelay = 5;
        public const int IsoBattRelay = 6;
        public const string AllControlledRelays = "2,3,4,5,6";
        public const string AllDaqControlRelays = "@301:306";
        public const string WithdrawalDaqRelay = "@301";
        public const string Contact80VDaqRelay = "@302";
        public const string SensPrecDaqRelay = "@303";
        public const string IsoBattDaqRelay = "@304";
        public const string ExternalPowerDaqRelays = "@305,306";

        public const int FiveVolt = 101;
        public const int Vcc = 102;
        public const int Vee = 103;
        public const int McuIsoBatt = 105;
        public const int OpenPrec = 106;
        public const int HeaterFeedback1 = 107;
        public const int HeaterFeedback2 = 108;
        public const int U24Pin7 = 109;
        public const int ComUsb = 111;
        public const int PPlus = 113;
        public const int VbusReference = PPlus;
        public const int Vpwm = 114;
        public const int VbusShunt = 117;
        public const int AlarmContact = 118;
        public const int FiveVoltIsolated = 119;
        public const int ThreeVoltThree = 120;
        public const double VbusShuntOhms = 1.0;

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
