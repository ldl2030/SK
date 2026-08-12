namespace TestPlatform.TestSequences
{
    public static class Bcm125Commands
    {
        public const string Exit = "EXIT";
        public const string DiagnosticOn = "DIAG ON";
        public const string Remote1 = "REMOTE1";
        public const string Remote2 = "REMOTE2";
        public const string String1RelayPrefix = "W94=";
        public const string String2RelayPrefix = "W91=";
    }

    public static class Bcm125ChannelProfiles
    {
        private static BcmStringChannelConfig Create(
            bool string1,
            string relayConfigKey,
            string relayDefaults,
            string[] resultIds,
            int stringVoltageChannel = 0)
        {
            return new BcmStringChannelConfig
            {
                Suffix = string1 ? "ST1" : "ST2",
                RemoteCommand = string1 ? Bcm125Commands.Remote1 : Bcm125Commands.Remote2,
                MbdRelayCommandPrefix = string1
                    ? Bcm125Commands.String1RelayPrefix
                    : Bcm125Commands.String2RelayPrefix,
                RelayConfigKey = relayConfigKey,
                RelayDefaults = relayDefaults,
                VoltageMeasureChannel = string1
                    ? Bcm125StationMap.String1VstrNegativeDaqChannel
                    : Bcm125StationMap.String2VstrNegativeDaqChannel,
                ShuntChannel = string1
                    ? Bcm125StationMap.String1ShuntDaqChannel
                    : Bcm125StationMap.String2ShuntDaqChannel,
                StringVoltageChannel = stringVoltageChannel,
                ShuntOhms = string1
                    ? Bcm125StationMap.String1ShuntOhms
                    : Bcm125StationMap.String2ShuntOhms,
                ResultIds = resultIds
            };
        }

        public static BcmStringChannelConfig DischargeString1 => Create(
            true, "SKBCM125DischargeSt1CloseRelays", "8,11,10",
            Bcm125ResultIds.Range("N", 1, 8));

        public static BcmStringChannelConfig DischargeString2 => Create(
            false, "SKBCM125DischargeSt2CloseRelays", "8,16,10",
            Bcm125ResultIds.Range("N", 9, 8));

        public static BcmStringChannelConfig ChargingString1 =>
            WithWrites(Create(
                true, "SKBCM125ChargingSt1CloseRelays", "15,11,8",
                Bcm125ResultIds.Range("Q", 1, 16)));

        public static BcmStringChannelConfig ChargingString2 =>
            WithWrites(Create(
                false, "SKBCM125ChargingSt2CloseRelays", "16,12,8",
                Bcm125ResultIds.Range("Q", 17, 16)));

        public static BcmStringChannelConfig VerificationString1 => Create(
            true, "SKBCM125VerificationSt1CloseRelays", "15,11,8",
            Bcm125ResultIds.Range("R", 1, 14));

        public static BcmStringChannelConfig VerificationString2 => Create(
            false, "SKBCM125VerificationSt2CloseRelays", "16,12,8",
            Bcm125ResultIds.Range("R", 15, 14));

        public static BcmStringChannelConfig CcbString1 => Create(
            true, "SKBCM125CcbSt1CloseRelays", "9,13,8",
            Bcm125ResultIds.Range("S", 1, 2),
            Bcm125StationMap.String1VoltageDaqChannel);

        public static BcmStringChannelConfig CcbString2 => Create(
            false, "SKBCM125CcbSt2CloseRelays", "9,14,8",
            Bcm125ResultIds.Range("S", 3, 2),
            Bcm125StationMap.String2VoltageDaqChannel);

        public static BcmStringChannelConfig StringTest1 => Create(
            true, "SKBCM125StringTestSt1CloseRelays", "9,13,8",
            Bcm125ResultIds.Range("T", 1, 3));

        public static BcmStringChannelConfig StringTest2 => Create(
            false, "SKBCM125StringTestSt2CloseRelays", "9,14,8",
            Bcm125ResultIds.Range("T", 4, 3));

        private static BcmStringChannelConfig WithWrites(BcmStringChannelConfig config)
        {
            config.CalibrationWriteCommands =
                new[] { "W16=", "W17=", "W22=", "W24=", "W23=" };
            return config;
        }
    }

    public static class Bcm125ResultIds
    {
        public static string[] Range(string prefix, int start, int count)
        {
            var values = new string[count];
            for (int index = 0; index < count; index++)
                values[index] = prefix + (start + index).ToString("00");
            return values;
        }

        public static string Key(string chapter, string suffix, string resultId)
        {
            return $"BCM125.{chapter}.{suffix}.{resultId}";
        }
    }
}
