namespace TestPlatform
{
    public static class ComName
    {
        private static string _ainuoPowerAComName;

        public static string powerSupplyComName { get; set; }

        public static string rs485ComName { get; set; }

        public static string testComName { get; set; }

        public static string uartComName { get; set; }

        public static string ledComName { get; set; }

        public static string ansPowerComName { get; set; }

        public static string ainuoPowerAComName
        {
            get => _ainuoPowerAComName;
            set => _ainuoPowerAComName = value;
        }

        // Compatibility alias for older project files. New SK projects use
        // ainuoPowerAComName as the logical source-A role.
        public static string hengHuiPowerAComName
        {
            get => _ainuoPowerAComName;
            set => _ainuoPowerAComName = value;
        }

        public static string hengHuiPowerBComName { get; set; }

        public static string electronicLoadComName { get; set; }

        public static string daqComName { get; set; }

        public static string digitalInputComName { get; set; }

        public static string fixtureRelayComName { get; set; }

        public static string relay16ComName { get; set; }
    }

    public static class CommandList
    {
        public static byte[] CloseAllRelay_01 =
            { 0x01, 0x06, 0x00, 0x34, 0x00, 0x00, 0xC8, 0x04 };

        public static byte[] CloseAllRelay_02 =
            { 0x02, 0x06, 0x00, 0x34, 0x00, 0x00 };

        public static byte[] OpenAllRelay_01 =
            { 0x01, 0x06, 0x00, 0x34, 0x00, 0x01 };

        public static byte[] ReadOhmValue_03 =
            { 0x03, 0x03, 0x00, 0x00, 0x00, 0x10, 0x45, 0xE4 };

        public static byte[] ReadAddress =
            { 0x01, 0x03, 0x00, 0x66, 0x00, 0x01, 0x64, 0x15 };

        public static byte[] Get01Volt_16 =
            { 0x01, 0x04, 0x00, 0x00, 0x00, 0x10 };

        public static byte[] Read16_02Volt =
            { 0x02, 0x04, 0x00, 0x00, 0x00, 0x10 };

        public static byte[] Get03Volt_16 =
            { 0x03, 0x04, 0x00, 0x00, 0x00, 0x10 };
    }
}
