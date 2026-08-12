using System;

namespace TestPlatform.TestSequences
{
    public sealed class BcmStringChannelConfig
    {
        public string Suffix { get; set; }
        public string RemoteCommand { get; set; }
        public string MbdRelayCommandPrefix { get; set; }
        public string RelayConfigKey { get; set; }
        public string RelayDefaults { get; set; }
        public int VoltageMeasureChannel { get; set; }
        public int ShuntChannel { get; set; }
        public int StringVoltageChannel { get; set; }
        public double ShuntOhms { get; set; }
        public string[] ResultIds { get; set; } = Array.Empty<string>();
        public string[] CalibrationWriteCommands { get; set; } = Array.Empty<string>();
    }
}
