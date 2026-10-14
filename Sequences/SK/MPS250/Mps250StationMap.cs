namespace TestPlatform.TestSequences
{
    public static class Mps250StationMap
    {
        // VBATT/source B uses station-3 high-power relays Y1 and Y9. Logical
        // Relay_2-Relay_6 remain on the DAQ960 34903A functional relay bank.
        public const int VbattSourcePowerRelay = SK441Device.MpsVbattSourcePowerRelay;
        public const int VbattDutPowerRelay = SK441Device.MpsVbattDutPowerRelay;
        public const string VbattPowerRelays = "1,9";
        public const int ExternalPowerRelay = MpsFunctionalRelayMap.ExternalPowerRelay;
        public const int WithdrawalRelay = MpsFunctionalRelayMap.WithdrawalRelay;
        public const int Contact80VRelay = MpsFunctionalRelayMap.Contact80VRelay;
        public const int SensPrecRelay = MpsFunctionalRelayMap.SensPrecRelay;
        public const int IsoBattRelay = MpsFunctionalRelayMap.IsoBattRelay;
        public const string AllControlledRelays = MpsFunctionalRelayMap.AllControlledRelays;
        public const string AllDaqControlRelays = MpsFunctionalRelayMap.AllDaqControlRelays;
        public const string WithdrawalDaqRelay = MpsFunctionalRelayMap.WithdrawalDaqRelay;
        public const string Contact80VDaqRelay = MpsFunctionalRelayMap.Contact80VDaqRelay;
        public const string SensPrecDaqRelay = MpsFunctionalRelayMap.SensPrecDaqRelay;
        public const string IsoBattDaqRelay = MpsFunctionalRelayMap.IsoBattDaqRelay;
        public const string ExternalPowerDaqRelays = MpsFunctionalRelayMap.ExternalPowerDaqRelays;

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
            return MpsFunctionalRelayMap.GetDaqRelayChannels(logicalRelay);
        }
    }
}
