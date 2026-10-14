using System;

namespace TestPlatform
{
    /// <summary>
    /// Common control surface shared by the installed station power sources.
    /// Implementations may reject commands that the connected
    /// instrument does not expose instead of silently pretending they succeeded.
    /// </summary>
    public interface IProgrammablePowerSupply : IDisposable
    {
        float Vmax { get; set; }
        float Imax { get; set; }
        string Identity { get; }
        bool IsConnected { get; }

        void Connect(string portName, int baudRate, byte address);
        void Disconnect();
        void SetPower(bool turnOn);
        void SetVoltageCurrent(float voltage, float current);
        void SetVoltage(float voltage);
        void SetCurrent(float current);
        void ReadVoltageCurrent(out float voltage, out float current);
        void ReadOutputTelemetry(
            int readTimeoutMs,
            out bool outputOn,
            out float voltage,
            out float current);
        void SetOvp(float ovp);
        void SetOcp(float ocp);
        ushort ReadStatus();
        void SetWorkingMode(int mode);
        void SetPowerLimit(float powerWatt, bool isMaxPowerGreaterThan100);
        float ReadPower(bool isMaxPowerGreaterThan100);
        void ReadStatistics(
            out float maxV,
            out float maxI,
            out float minV,
            out float minI,
            out float avgV,
            out float avgI);
        void ClearLimits();
    }
}
