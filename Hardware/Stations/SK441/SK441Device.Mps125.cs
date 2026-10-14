using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace TestPlatform
{
    public partial class SK441Device
    {
        public bool IsCompactLoggingEnabled() => SkTestPolicy.UseCompactLogging(SimulationBoardType);

        public bool IsMpsCompactLoggingEnabled() => IsCompactLoggingEnabled();

        public async Task<double> MeasureMps125IgnitionVoltageAsync(
            int channel, double startVoltage, double endVoltage, double increment,
            CancellationToken token)
        {
            if (!string.Equals(SimulationBoardType, "MPS-125", StringComparison.OrdinalIgnoreCase) ||
                channel != TestSequences.Mps125StationMap.FiveVolt ||
                !IsFiniteIgnitionValue(startVoltage) || !IsFiniteIgnitionValue(endVoltage) ||
                !IsFiniteIgnitionValue(increment) || startVoltage <= 0 ||
                endVoltage < startVoltage || endVoltage > 80 || increment <= 0 || increment > 1)
                throw new ArgumentOutOfRangeException(nameof(startVoltage), "Invalid MPS-125 ignition scan profile.");

            lock (_powerSafetySync)
            {
                if (!_vbusCommandStateKnown || !_vbusOutputOn ||
                    Math.Abs(_vbusCommandedVoltage - startVoltage) > 0.001 ||
                    Math.Abs(_vbusCommandedCurrent - 0.5f) > 0.001)
                    throw new InvalidOperationException($"MPS-125 ignition scan requires the existing {startVoltage:0.###}V/0.5A output-on state.");
            }

            string channelList = "@" + channel.ToString(CultureInfo.InvariantCulture);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            LogInfo?.Invoke($"MPS-125点火扫描开始：{startVoltage:0.###}–{endVoltage:0.###}V，步进={increment:0.###}V，DAQ{channel}固定10V量程，保持0.5A限流及输出ON。");
            try
            {
                if (!SkipComInit)
                {
                    if (!AinuoPowerA.HasVoltageCurrentSetpoint((float)startVoltage, 0.5f))
                        throw new InvalidOperationException("MPS-125 ignition source setpoint readback mismatch.");
                    Daq.PrepareVoltageDcMeasurementSeries(channelList, 10);
                }

                for (double voltage = startVoltage; voltage <= endVoltage + 0.000001; voltage += increment)
                {
                    token.ThrowIfCancellationRequested();
                    string safetyError;
                    if (!ValidatePowerSourceEnable("VBUS", (float)voltage, out safetyError))
                        throw new InvalidOperationException(safetyError);

                    if (voltage > startVoltage)
                    {
                        if (SkipComInit) _mockVbusVoltage = (float)voltage;
                        else AinuoPowerA.SetVoltage((float)voltage); // SetVoltage also verifies its readback.
                        RecordVbusSetpoint((float)voltage);
                        RecordVbusCommandState((float)voltage, 0.5f, true);
                        await Task.Delay(SkipComInit ? 1 : 100, token);
                    }
                    lock (_powerSafetySync)
                        _powerSafetyTrace.Add("MPS125 IGNITION " + voltage.ToString(CultureInfo.InvariantCulture));

                    double fiveVolt;
                    if (SkipComInit)
                        fiveVolt = voltage >= 31 ? 5.0 : 0.0;
                    else
                    {
                        try { fiveVolt = Daq.ReadVoltageDcMeasurementSeriesSample(out _); }
                        catch (TimeoutException)
                        {
                            LogInfo?.Invoke($"MPS-125点火扫描DAQ读取超时：VBUS={voltage:0.###}V，恢复同一通道并重读一次。");
                            Daq.PrepareVoltageDcMeasurementSeries(channelList, 10);
                            await Task.Delay(100, token);
                            fiveVolt = Daq.ReadVoltageDcMeasurementSeriesSample(out _);
                        }
                    }
                    token.ThrowIfCancellationRequested();
                    if (!IsFiniteIgnitionValue(fiveVolt) || Math.Abs(fiveVolt) >= 9E36)
                        throw new InvalidOperationException("Invalid ignition DAQ voltage.");
                    if (!IsMpsCompactLoggingEnabled())
                        LogInfo?.Invoke($"MPS-125点火扫描：VBUS={voltage:0.###}V，+5V={fiveVolt:0.######}V。");
                    if (fiveVolt > 4.8)
                    {
                        LogInfo?.Invoke($"MPS-125点火扫描完成：VBUS={voltage:0.###}V，+5V={fiveVolt:0.######}V，耗时={timer.ElapsedMilliseconds}ms。");
                        return voltage;
                    }
                }
                throw new InvalidOperationException($"MPS-125在VBUS≤{endVoltage:0.###}V内未检测到+5V>4.8V，不能以扫描上限作为点火值。");
            }
            catch
            {
                await SetVbusGeneratorAsync(0, 0, 300, false);
                throw;
            }
            finally
            {
                if (!SkipComInit)
                {
                    try { Daq.RecoverVoltageDcMeasurement(); }
                    catch (Exception ex) { LogError?.Invoke("MPS-125点火扫描后DAQ恢复失败：" + ex.Message); }
                }
            }
        }

        private static bool IsFiniteIgnitionValue(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        public async Task<bool> CheckMpsRequiredInstrumentsAsync()
        {
            if (SkipComInit)
            {
                await Task.Delay(1);
                LogInfo?.Invoke("[Simulation] MPS-125 VBUS/VBATT/DAQ instrument check passed.");
                return true;
            }

            bool passed = AinuoPowerA != null && AinuoPowerA.IsConnected;
            passed = passed && HengHuiPower2 != null && HengHuiPower2.IsConnected;
            passed = passed && Daq != null && Daq.IsConnected;
            if (!passed)
                LogError?.Invoke("MPS-125 requires voltage source A (VBUS), source B (VBATT), and DAQ960.");
            return passed;
        }

        public async Task<bool> SetVbattGeneratorAsync(float voltage, float current, float ovp, bool outputOn)
        {
            bool outputCommandSucceeded = false;
            try
            {
                if (outputOn && !await EnsureMpsVbattPowerPathClosedAsync())
                    return false;

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
                    outputCommandSucceeded = true;
                    return outputOn || await OpenMpsVbattPowerPathAsync(true);
                }

                HengHuiPower2.SetVoltageCurrent(voltage, current);
                HengHuiPower2.SetOvp(ovp);
                HengHuiPower2.SetPower(outputOn);
                RecordPowerOutput("SECONDARY", outputOn);
                LogInfo?.Invoke($"VBATT source B: {voltage}V/{current}A OVP={ovp}V, {(outputOn ? "ON" : "OFF")}");
                outputCommandSucceeded = true;
                return outputOn || await OpenMpsVbattPowerPathAsync(true);
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"VBATT source B setting failed: {ex.Message}");
                return false;
            }
            finally
            {
                if (!outputCommandSucceeded)
                {
                    if (outputOn)
                    {
                        try
                        {
                            if (!SkipComInit && HengHuiPower2 != null && HengHuiPower2.IsConnected)
                                HengHuiPower2.SetPower(false);
                            RecordPowerOutput("SECONDARY", false);
                        }
                        catch (Exception rollbackException)
                        {
                            LogError?.Invoke($"VBATT source B rollback failed: {rollbackException.Message}");
                        }
                    }

                    await OpenMpsVbattPowerPathAsync(false);
                }
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
                double value = Daq.MeasureResistanceAuto(channelList, out string rawResponse);
                LogInfo?.Invoke($"DAQ channel {channel} resistance: {value:0.###} Ohm (原始响应: {rawResponse})");
                return value;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"DAQ channel {channel} resistance failed: {ex.Message}");
                throw;
            }
        }
    }
}
