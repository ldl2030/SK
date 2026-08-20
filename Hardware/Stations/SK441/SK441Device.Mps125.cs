using System;
using System.Globalization;
using System.Threading.Tasks;

namespace TestPlatform
{
    public partial class SK441Device
    {
        public async Task<bool> CheckMpsRequiredInstrumentsAsync()
        {
            if (SkipComInit)
            {
                await Task.Delay(1);
                LogInfo?.Invoke("[Simulation] MPS-125 VBUS/VBATT/DAQ instrument check passed.");
                return true;
            }

            bool passed = HengHuiPower1 != null && HengHuiPower1.IsConnected;
            passed = passed && HengHuiPower2 != null && HengHuiPower2.IsConnected;
            passed = passed && Daq != null && Daq.IsConnected;
            if (!passed)
                LogError?.Invoke("MPS-125 requires voltage source A (VBUS), source B (VBATT), and DAQ960.");
            return passed;
        }

        public async Task<bool> SetVbattGeneratorAsync(float voltage, float current, float ovp, bool outputOn)
        {
            try
            {
                string safetyError;
                if (outputOn && !ValidatePowerSourceEnable("SECONDARY", voltage, out safetyError))
                {
                    LogError?.Invoke(safetyError);
                    return false;
                }
                if (SkipComInit)
                {
                    await Task.Delay(1);
                    RecordPowerOutput("SECONDARY", outputOn);
                    _mockSecondaryVoltage = voltage;
                    _mockSecondaryCurrentLimit = current;
                    LogInfo?.Invoke($"[Simulation] VBATT source B: {voltage}V/{current}A OVP={ovp}V, {(outputOn ? "ON" : "OFF")}");
                    return true;
                }

                HengHuiPower2.SetVoltageCurrent(voltage, current);
                HengHuiPower2.SetOvp(ovp);
                HengHuiPower2.SetPower(outputOn);
                RecordPowerOutput("SECONDARY", outputOn);
                LogInfo?.Invoke($"VBATT source B: {voltage}V/{current}A OVP={ovp}V, {(outputOn ? "ON" : "OFF")}");
                return true;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"VBATT source B setting failed: {ex.Message}");
                return false;
            }
        }

        public async Task<double> MeasureDaqChannelResistanceAsync(int channel)
        {
            try
            {
                if (SkipComInit)
                {
                    await Task.Delay(1);
                    return 1.0;
                }

                string channelList = "@" + channel.ToString(CultureInfo.InvariantCulture);
                Daq.ConfigResistanceAuto(channelList);
                double value = Daq.ReadSingleValue();
                LogInfo?.Invoke($"DAQ channel {channel} resistance: {value:0.###} Ohm");
                return value;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"DAQ channel {channel} resistance failed: {ex.Message}");
                return 0.0;
            }
        }
    }
}
