using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TestPlatform
{
    public partial class SK441Device
    {
        private readonly object _powerSafetySync = new object();
        private readonly Dictionary<int, bool> _powerRelayStates = new Dictionary<int, bool>();
        private readonly List<string> _powerSafetyTrace = new List<string>();
        private bool _ansOutputOn;
        private bool _vbusOutputOn;
        private bool _secondaryOutputOn;
        private bool _electronicLoadOn;

        public string[] GetPowerSafetyTrace()
        {
            lock (_powerSafetySync)
                return _powerSafetyTrace.ToArray();
        }

        public bool IsMps250PowerTelemetryEnabled()
        {
            return GetBoolAppSetting("SKMPS250PowerTelemetryEnabled", true);
        }

        public int GetMps250PowerTelemetryIntervalMilliseconds()
        {
            return Math.Max(
                250,
                GetIntAppSetting("SKMPS250PowerTelemetryIntervalMs", 1000));
        }

        public string ReadMps250PowerTelemetryLine()
        {
            if (SkipComInit)
            {
                bool vbusOn;
                bool vbattOn;
                lock (_powerSafetySync)
                {
                    vbusOn = _vbusOutputOn;
                    vbattOn = _secondaryOutputOn;
                }
                return string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "模式=Simulation | VBUS/A 输出={0}, 设定={1:0.###}V/{2:0.###}A | " +
                    "VBATT/B 输出={3}, 设定={4:0.###}V/{5:0.###}A",
                    vbusOn ? "ON" : "OFF",
                    _mockVbusVoltage,
                    _mockVbusCurrentLimit,
                    vbattOn ? "ON" : "OFF",
                    _mockSecondaryVoltage,
                    _mockSecondaryCurrentLimit);
            }

            int readTimeoutMs = Math.Max(
                50,
                GetIntAppSetting("SKMPS250PowerTelemetryReadTimeoutMs", 250));
            return ReadHengHuiTelemetry("VBUS/A", HengHuiPower1, readTimeoutMs) +
                " | " +
                ReadHengHuiTelemetry("VBATT/B", HengHuiPower2, readTimeoutMs);
        }

        private static string ReadHengHuiTelemetry(
            string name,
            HengHuiPowerSupply supply,
            int readTimeoutMs)
        {
            if (supply == null || !supply.IsConnected)
                return name + " 状态=DISCONNECTED";

            try
            {
                bool outputOn;
                float voltage;
                float current;
                supply.ReadOutputTelemetry(
                    readTimeoutMs,
                    out outputOn,
                    out voltage,
                    out current);
                return string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0} 输出={1}, 实测={2:0.###}V/{3:0.###}A",
                    name,
                    outputOn ? "ON" : "OFF",
                    voltage,
                    current);
            }
            catch (Exception ex)
            {
                return name + " 状态=READ_ERROR(" + ex.Message + ")";
            }
        }

        private void RecordPowerOutput(string domain, bool outputOn)
        {
            lock (_powerSafetySync)
            {
                if (domain == "ANS") _ansOutputOn = outputOn;
                else if (domain == "VBUS") _vbusOutputOn = outputOn;
                else if (domain == "SECONDARY") _secondaryOutputOn = outputOn;
                else if (domain == "LOAD") _electronicLoadOn = outputOn;
                _powerSafetyTrace.Add("OUTPUT " + domain + " " + (outputOn ? "ON" : "OFF"));
            }
        }

        private bool ValidatePowerRelaySwitch(int relayId, bool close, out string error)
        {
            if (close && !ValidateEmergencyAllowsEnergizing("闭合功率继电器 " + relayId, out error))
                return false;

            lock (_powerSafetySync)
            {
                bool current;
                if (_powerRelayStates.TryGetValue(relayId, out current) && current == close)
                {
                    error = null;
                    return true;
                }

                // Opening a relay removes energy from the DUT path and must never be
                // blocked by stale software state or a failed measurement query.
                bool isMps = (SimulationBoardType ?? string.Empty).StartsWith("MPS", StringComparison.OrdinalIgnoreCase);
                bool blocked = false;
                if (close && isMps)
                {
                    if (relayId == 5 && _vbusOutputOn) blocked = true;
                    if (relayId == 6 && _secondaryOutputOn) blocked = true;
                }
                else if (close)
                {
                    if (relayId == 7 && _ansOutputOn) blocked = true;
                    if (relayId >= 1 && relayId <= 16 && relayId != 7 &&
                        (_vbusOutputOn || _secondaryOutputOn || _electronicLoadOn)) blocked = true;
                }

                if (blocked)
                {
                    error = "禁止带电" + (close ? "闭合" : "断开") + "功率继电器 " + relayId;
                    _powerSafetyTrace.Add("BLOCK RELAY " + relayId + " " + (close ? "CLOSE" : "OPEN"));
                    return false;
                }

                error = null;
                return true;
            }
        }

        private void RecordPowerRelay(int relayId, bool close)
        {
            lock (_powerSafetySync)
            {
                _powerRelayStates[relayId] = close;
                _powerSafetyTrace.Add("RELAY " + relayId + " " + (close ? "CLOSED" : "OPEN"));
            }
        }

        private bool ValidatePowerSourceEnable(string domain, float voltage, out string error)
        {
            if (!ValidateEmergencyAllowsEnergizing("开启 " + domain + " 输出", out error))
                return false;

            lock (_powerSafetySync)
            {
                bool isMps = (SimulationBoardType ?? string.Empty).StartsWith("MPS", StringComparison.OrdinalIgnoreCase);
                bool isBcm250 = string.Equals(
                    SimulationBoardType,
                    "BCM-250",
                    StringComparison.OrdinalIgnoreCase);
                int requiredRelay = domain == "ANS" && !isMps ? 7 : 0;
                bool closed;
                if (requiredRelay > 0 && (!_powerRelayStates.TryGetValue(requiredRelay, out closed) || !closed))
                {
                    error = "禁止开启" + domain + "：功率继电器 " + requiredRelay + " 尚未确认闭合";
                    _powerSafetyTrace.Add("BLOCK OUTPUT " + domain + " ON");
                    return false;
                }

                if (isBcm250 && domain == "SECONDARY" &&
                    !AreAllPowerRelaysClosedUnsafe(1, 3, 6))
                {
                    error = "禁止开启String输出：16路大功率继电器 Y1、Y3、Y6 尚未全部确认闭合";
                    _powerSafetyTrace.Add("BLOCK OUTPUT SECONDARY ON: RELAYS 1,3,6 REQUIRED");
                    return false;
                }

                error = null;
                return true;
            }
        }

        private bool IsAnyPowerRelayClosedUnsafe(params int[] relayIds)
        {
            foreach (int relayId in relayIds)
            {
                bool closed;
                if (_powerRelayStates.TryGetValue(relayId, out closed) && closed)
                    return true;
            }
            return false;
        }

        public Task<bool> WaitForMps250VbusStableAsync(
            float targetVoltage,
            CancellationToken token)
        {
            int minimumWaitMs = Math.Max(
                0,
                GetIntAppSetting("SKMPS250VbusMinimumSettleMs", 500));
            int maximumWaitMs = Math.Max(
                minimumWaitMs + 1000,
                GetIntAppSetting("SKMPS250VbusStableTimeoutMs", 5000));
            return WaitForPowerVoltageStableAsync(
                "VBUS",
                targetVoltage,
                minimumWaitMs,
                maximumWaitMs,
                token);
        }

        public Task<bool> WaitForMps125VbusStableAsync(
            float targetVoltage,
            CancellationToken token)
        {
            int minimumWaitMs = Math.Max(
                0,
                GetIntAppSetting("SKMPS125VbusMinimumSettleMs", 500));
            int maximumWaitMs = Math.Max(
                minimumWaitMs + 1000,
                GetIntAppSetting("SKMPS125VbusStableTimeoutMs", 5000));
            return WaitForPowerVoltageStableAsync(
                "VBUS",
                targetVoltage,
                minimumWaitMs,
                maximumWaitMs,
                token);
        }

        private bool AreAllPowerRelaysClosedUnsafe(params int[] relayIds)
        {
            foreach (int relayId in relayIds)
            {
                bool closed;
                if (!_powerRelayStates.TryGetValue(relayId, out closed) || !closed)
                    return false;
            }
            return true;
        }

        private bool IsPowerRelayAlreadyInState(int relayId, bool close)
        {
            lock (_powerSafetySync)
            {
                bool current;
                return _powerRelayStates.TryGetValue(relayId, out current) && current == close;
            }
        }

        private bool CanReleaseFixture(out string error)
        {
            lock (_powerSafetySync)
            {
                if (_ansOutputOn || _vbusOutputOn || _secondaryOutputOn || _electronicLoadOn)
                {
                    error = "禁止释放治具：仍有电源或电子负载输出处于开启状态。";
                    _powerSafetyTrace.Add("BLOCK FIXTURE RELEASE");
                    return false;
                }

                error = null;
                return true;
            }
        }

        private async Task<bool> VerifyPowerDomainsOffAsync(params string[] domains)
        {
            if (SkipComInit)
            {
                await Task.Delay(1);
                string stateError;
                if (!CanReleaseFixtureForDomains(domains, out stateError))
                {
                    LogError?.Invoke(stateError);
                    return false;
                }
                return true;
            }

            float voltageThreshold = GetFloatAppSetting("SKSafeOpenVoltageThreshold", 20.0f);
            float currentThreshold = GetFloatAppSetting("SKSafeOpenCurrentThreshold", 0.05f);
            int timeoutMs = GetIntAppSetting("SKSafeDischargeTimeoutMs", 30000);
            int pollMs = Math.Max(50, GetIntAppSetting("SKSafeDischargePollMs", 200));
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(pollMs, timeoutMs));

            while (DateTime.UtcNow <= deadline)
            {
                bool allSafe = true;
                foreach (string domain in domains)
                {
                    float voltage;
                    float current;
                    try
                    {
                        ReadPowerDomain(domain, out voltage, out current);
                    }
                    catch (Exception ex)
                    {
                        LogError?.Invoke($"读取 {domain} 掉电状态失败: {ex.Message}");
                        return false;
                    }

                    if (Math.Abs(voltage) > voltageThreshold || Math.Abs(current) > currentThreshold)
                    {
                        allSafe = false;
                        LogInfo?.Invoke(
                            $"等待 {domain} 放电：{voltage:F3} V, {current:F3} A；" +
                            $"安全阈值 {voltageThreshold:F3} V / {currentThreshold:F3} A。");
                    }
                }

                if (allSafe) return true;
                await Task.Delay(pollMs);
            }

            LogError?.Invoke($"掉电确认超时，禁止切换继电器或释放治具：{string.Join(",", domains)}");
            return false;
        }

        private bool CanReleaseFixtureForDomains(string[] domains, out string error)
        {
            lock (_powerSafetySync)
            {
                foreach (string domain in domains)
                {
                    bool on = domain == "ANS" ? _ansOutputOn :
                              domain == "VBUS" ? _vbusOutputOn :
                              domain == "SECONDARY" ? _secondaryOutputOn :
                              domain == "LOAD" && _electronicLoadOn;
                    if (on)
                    {
                        error = "禁止安全动作：" + domain + " 输出尚未关闭。";
                        return false;
                    }
                }
                error = null;
                return true;
            }
        }

        private void ReadPowerDomain(string domain, out float voltage, out float current)
        {
            if (domain == "ANS")
            {
                AnsPower.ReadVoltageCurrent(out voltage, out current);
                return;
            }
            if (domain == "VBUS")
            {
                HengHuiPower1.ReadVoltageCurrent(out voltage, out current);
                return;
            }
            if (domain == "SECONDARY")
            {
                HengHuiPower2.ReadVoltageCurrent(out voltage, out current);
                return;
            }
            if (domain == "LOAD")
            {
                voltage = ElectronicLoad.MeasureVoltage();
                current = ElectronicLoad.MeasureCurrent();
                return;
            }
            throw new ArgumentException("Unknown power domain: " + domain, nameof(domain));
        }

        public async Task<bool> WaitForPowerVoltageStableAsync(
            string domain,
            float targetVoltage,
            int minimumWaitMs,
            int maximumWaitMs,
            CancellationToken token)
        {
            if (SkipComInit)
                return true;

            DateTime started = DateTime.UtcNow;
            int stableSamples = 0;
            float tolerance = Math.Max(0.5f, Math.Abs(targetVoltage) * 0.02f);
            while ((DateTime.UtcNow - started).TotalMilliseconds < maximumWaitMs)
            {
                token.ThrowIfCancellationRequested();
                int elapsedMs = (int)(DateTime.UtcNow - started).TotalMilliseconds;
                if (elapsedMs < minimumWaitMs)
                {
                    await Task.Delay(Math.Min(250, minimumWaitMs - elapsedMs), token);
                    continue;
                }

                try
                {
                    float voltage;
                    float current;
                    ReadPowerDomain(domain, out voltage, out current);
                    stableSamples = Math.Abs(voltage - targetVoltage) <= tolerance
                        ? stableSamples + 1
                        : 0;
                    if (stableSamples >= 3)
                    {
                        LogInfo?.Invoke(
                            $"{domain} 电压连续稳定，提前结束等待：目标={targetVoltage:0.###}V，" +
                            $"实测={voltage:0.###}V，耗时={(DateTime.UtcNow - started).TotalMilliseconds:0}ms");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    stableSamples = 0;
                    LogInfo?.Invoke($"{domain} 稳定轮询暂未就绪：{ex.Message}");
                }

                await Task.Delay(250, token);
            }
            LogError?.Invoke(
                $"{domain} 电压稳定等待超时：目标={targetVoltage:0.###}V，" +
                $"最短等待={minimumWaitMs}ms，最长等待={maximumWaitMs}ms。");
            return false;
        }

        private Task<bool> VerifyRelayPowerIsOffAsync(int relayId)
        {
            bool isMps = (SimulationBoardType ?? string.Empty).StartsWith("MPS", StringComparison.OrdinalIgnoreCase);
            if (isMps)
            {
                if (relayId == 5)
                    return VerifyPowerDomainsOffAsync("VBUS");
                if (relayId == 6)
                    return VerifyPowerDomainsOffAsync("SECONDARY");
                return Task.FromResult(true);
            }

            if (relayId == 7)
                return VerifyPowerDomainsOffAsync("ANS");
            // BCM250 station-3 Y2/Y5 power the electronic load itself. Requiring
            // a LOAD measurement before closing them creates an impossible loop
            // while the load is still unpowered. Confirm every source feeding the
            // fixture is off, then power the load and verify it separately.
            if (relayId == 2 || relayId == 5)
                return VerifyPowerDomainsOffAsync("ANS", "VBUS", "SECONDARY");
            if (relayId >= 1 && relayId <= 16)
                return VerifyPowerDomainsOffAsync("VBUS", "SECONDARY", "LOAD");
            return Task.FromResult(true);
        }

        public async Task<bool> EnableBcm250ElectronicLoadSupplyAsync()
        {
            const int loadPowerRelay1 = 2;
            const int loadPowerRelay2 = 5;

            if (!await ControlRelayAsync(loadPowerRelay1, true))
                return false;
            if (!await ControlRelayAsync(loadPowerRelay2, true))
            {
                await ControlRelayAsync(loadPowerRelay1, false);
                return false;
            }

            if (SkipComInit)
            {
                LogInfo?.Invoke(
                    "[调试模式] 模拟闭合电子负载供电继电器 Y2/Y5 并确认负载通信。");
                return true;
            }

            int startupDelayMs = Math.Max(
                0,
                GetIntAppSetting("SKElectronicLoadPowerOnDelayMs", 3000));
            await Task.Delay(startupDelayMs);

            try
            {
                float voltage = ElectronicLoad.MeasureVoltage();
                float current = ElectronicLoad.MeasureCurrent();
                LogInfo?.Invoke(
                    $"电子负载供电继电器 Y2/Y5 已闭合，通信确认成功：{voltage:F3} V, {current:F3} A");
                return true;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"电子负载 Y2/Y5 上电后通信确认失败：{ex.Message}");
                await ControlRelayAsync(loadPowerRelay2, false);
                await ControlRelayAsync(loadPowerRelay1, false);
                return false;
            }
        }

        private async Task<bool> VerifyFixturePowerIsOffAsync()
        {
            string error;
            if (!CanReleaseFixture(out error))
            {
                LogError?.Invoke(error);
                return false;
            }
            return await VerifyPowerDomainsOffAsync("ANS", "VBUS", "SECONDARY", "LOAD");
        }

        public async Task<bool> SetBcm125PowerPathRelaysAsync(int[] relayIds, bool close, string description)
        {
            if (relayIds == null || relayIds.Length == 0) return true;
            foreach (int relayId in relayIds)
            {
                string error;
                if (!ValidatePowerRelaySwitch(relayId, close, out error))
                {
                    LogError?.Invoke(error + "：" + description);
                    return false;
                }
            }

            bool passed = true;
            foreach (int relayId in relayIds)
            {
                if (IsPowerRelayAlreadyInState(relayId, close)) continue;
                // BCM功率路径属于16路功率继电器站3，不属于工装继电器站1。
                bool ok = await ControlRelayAsync(relayId, close);
                passed = ok && passed;
            }
            return passed;
        }

        public async Task<bool> ConfigureAnsOutputOffAsync(float voltage, float current)
        {
            return await SetAnsVoltageCurrentOutputAsync(voltage, current, false);
        }

        public async Task<bool> CloseRl4ThenEnableAnsAsync(int relayId, float voltage, float current)
        {
            if (!IsAnsSetpointConfigured(voltage, current) &&
                !await SetAnsVoltageCurrentOutputAsync(voltage, current, false))
            {
                return false;
            }

            if (!await ControlRelayAsync(relayId, true)) return false;
            if (await SetAnsPowerOnlyAsync(voltage, true)) return true;

            await SetAnsPowerOnlyAsync(voltage, false);
            await ControlRelayAsync(relayId, false);
            return false;
        }

        public async Task<bool> DisableAnsThenOpenRl4Async(int relayId)
        {
            if (!await SetAnsVoltageCurrentOutputAsync(0, 0, false)) return false;
            if (!await VerifyPowerDomainsOffAsync("ANS")) return false;
            return await ControlRelayAsync(relayId, false);
        }

        public async Task<bool> ControlMpsRelayAsync(int logicalRelayId, string daqChannelList, bool close)
        {
            if (string.IsNullOrWhiteSpace(daqChannelList))
                return await ControlRelayAsync(logicalRelayId, close);

            string error;
            if (!ValidatePowerRelaySwitch(logicalRelayId, close, out error))
            {
                LogError?.Invoke(error);
                return false;
            }
            if (IsPowerRelayAlreadyInState(logicalRelayId, close))
                return true;
            if (close && !await VerifyRelayPowerIsOffAsync(logicalRelayId))
            {
                LogError?.Invoke($"MPS逻辑继电器 Relay_{logicalRelayId} 闭合前掉电确认失败。");
                return false;
            }

            bool passed = await SetDaqRelaysAsync(
                daqChannelList,
                close,
                $"MPS逻辑继电器 Relay_{logicalRelayId}");
            if (passed)
                RecordPowerRelay(logicalRelayId, close);
            return passed;
        }

        public async Task<bool> SafeSwitchMpsVbusRelayAsync(
            int relayId,
            string daqChannelList,
            bool close,
            float voltage,
            float current,
            float ovp,
            bool restoreOutput,
            CancellationToken token)
        {
            if (!await CloseTargetBoardCommunicationAsync()) return false;
            if (!await SetVbusGeneratorAsync(0, 0, ovp, false)) return false;
            if (!await VerifyPowerDomainsOffAsync("VBUS")) return false;
            if (!await ControlMpsRelayAsync(relayId, daqChannelList, close)) return false;
            if (!restoreOutput) return true;

            if (!ResetMbdConnectionBeforeDutPowerOn()) return false;
            if (!await SetVbusGeneratorAsync(voltage, current, ovp, true)) return false;

            bool voltageStable = string.Equals(
                SimulationBoardType,
                "MPS-250",
                StringComparison.OrdinalIgnoreCase)
                ? await WaitForMps250VbusStableAsync(voltage, token)
                : await WaitForPowerVoltageStableAsync(
                    "VBUS",
                    voltage,
                    500,
                    5000,
                    token);
            if (!voltageStable) return false;

            int mbdRecoveryTimeoutMs = Math.Max(
                5000,
                GetIntAppSetting("SKMbdPowerCycleRecoveryTimeoutMs", 25000));
            return await WaitForMbdReadyAfterPowerCycleAsync(
                mbdRecoveryTimeoutMs,
                token);
        }

        public Task<bool> SafeSwitchMpsVbusRelayAsync(
            int relayId,
            string daqChannelList,
            bool close,
            float voltage,
            float current,
            float ovp,
            bool restoreOutput)
        {
            return SafeSwitchMpsVbusRelayAsync(
                relayId,
                daqChannelList,
                close,
                voltage,
                current,
                ovp,
                restoreOutput,
                CancellationToken.None);
        }

        public Task<bool> SafeSwitchMpsVbusRelayAsync(int relayId, bool close, float voltage, float current, float ovp, bool restoreOutput)
        {
            return SafeSwitchMpsVbusRelayAsync(relayId, null, close, voltage, current, ovp, restoreOutput);
        }

        public async Task<bool> PrepareMpsVbattBaselineAsync(
            int relayId,
            string daqChannelList,
            float ovp,
            CancellationToken token)
        {
            if (!await SetVbattGeneratorAsync(0, 0, ovp, false)) return false;
            if (!await VerifyPowerDomainsOffAsync("SECONDARY")) return false;
            if (!await ControlMpsRelayAsync(relayId, daqChannelList, false)) return false;
            return await WaitForMps250VbattVoltageAsync(0, token);
        }

        public async Task<bool> PrepareMps125VbattBaselineAsync(
            int relayId,
            string daqChannelList,
            float ovp,
            CancellationToken token)
        {
            if (!await SetVbattGeneratorAsync(0, 0, ovp, false)) return false;
            if (!await VerifyPowerDomainsOffAsync("SECONDARY")) return false;
            if (!await ControlMpsRelayAsync(relayId, daqChannelList, false)) return false;
            return await WaitForMps125VbattVoltageAsync(0, token);
        }

        public async Task<bool> WaitForMps250VbattVoltageAsync(
            float targetVoltage,
            CancellationToken token)
        {
            if (SkipComInit)
                return true;

            float voltageTolerance = Math.Abs(targetVoltage) < 0.001f
                ? GetFloatAppSetting("SKMPS250VbattZeroToleranceV", 0.5f)
                : Math.Max(1.0f, Math.Abs(targetVoltage) * 0.02f);
            float zeroCurrentTolerance = GetFloatAppSetting(
                "SKMPS250VbattZeroCurrentToleranceA",
                0.05f);
            int timeoutMs = Math.Max(
                1000,
                GetIntAppSetting(
                    Math.Abs(targetVoltage) < 0.001f
                        ? "SKMPS250VbattZeroTimeoutMs"
                        : "SKMPS250VbattStableTimeoutMs",
                    Math.Abs(targetVoltage) < 0.001f ? 30000 : 10000));
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            int stableSamples = 0;

            while (DateTime.UtcNow <= deadline)
            {
                token.ThrowIfCancellationRequested();
                float voltage;
                float current;
                try
                {
                    ReadPowerDomain("SECONDARY", out voltage, out current);
                }
                catch (Exception ex)
                {
                    LogError?.Invoke("VBATT实测条件读取失败：" + ex.Message);
                    return false;
                }

                bool voltageReady = Math.Abs(voltage - targetVoltage) <= voltageTolerance;
                bool currentReady = Math.Abs(targetVoltage) >= 0.001f ||
                    Math.Abs(current) <= zeroCurrentTolerance;
                stableSamples = voltageReady && currentReady ? stableSamples + 1 : 0;
                LogInfo?.Invoke(
                    $"等待VBATT实测条件：目标={targetVoltage:0.###}V，" +
                    $"实测={voltage:0.###}V/{current:0.###}A，" +
                    $"电压容差=±{voltageTolerance:0.###}V。");
                if (stableSamples >= 3)
                    return true;

                await Task.Delay(250, token);
            }

            LogError?.Invoke(
                $"VBATT实测条件在 {timeoutMs}ms 内未达成：" +
                $"目标={targetVoltage:0.###}V，容差=±{voltageTolerance:0.###}V。");
            return false;
        }

        public async Task<bool> WaitForMps125VbattVoltageAsync(
            float targetVoltage,
            CancellationToken token)
        {
            if (SkipComInit)
                return true;

            float voltageTolerance = Math.Abs(targetVoltage) < 0.001f
                ? GetFloatAppSetting("SKMPS125VbattZeroToleranceV", 0.5f)
                : Math.Max(1.0f, Math.Abs(targetVoltage) * 0.02f);
            float zeroCurrentTolerance = GetFloatAppSetting(
                "SKMPS125VbattZeroCurrentToleranceA",
                0.05f);
            int timeoutMs = Math.Max(
                1000,
                GetIntAppSetting(
                    Math.Abs(targetVoltage) < 0.001f
                        ? "SKMPS125VbattZeroTimeoutMs"
                        : "SKMPS125VbattStableTimeoutMs",
                    Math.Abs(targetVoltage) < 0.001f ? 30000 : 10000));
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            int stableSamples = 0;

            while (DateTime.UtcNow <= deadline)
            {
                token.ThrowIfCancellationRequested();
                float voltage;
                float current;
                try
                {
                    ReadPowerDomain("SECONDARY", out voltage, out current);
                }
                catch (Exception ex)
                {
                    LogError?.Invoke("MPS-125 VBATT实测条件读取失败：" + ex.Message);
                    return false;
                }

                bool voltageReady = Math.Abs(voltage - targetVoltage) <= voltageTolerance;
                bool currentReady = Math.Abs(targetVoltage) >= 0.001f ||
                    Math.Abs(current) <= zeroCurrentTolerance;
                stableSamples = voltageReady && currentReady ? stableSamples + 1 : 0;
                LogInfo?.Invoke(
                    $"等待MPS-125 VBATT实测条件：目标={targetVoltage:0.###}V，" +
                    $"实测={voltage:0.###}V/{current:0.###}A，" +
                    $"电压容差=±{voltageTolerance:0.###}V。");
                if (stableSamples >= 3)
                    return true;

                await Task.Delay(250, token);
            }

            LogError?.Invoke(
                $"MPS-125 VBATT实测条件在 {timeoutMs}ms 内未达成：" +
                $"目标={targetVoltage:0.###}V，容差=±{voltageTolerance:0.###}V。");
            return false;
        }

        public async Task<bool> SafeConnectMps125VbattRelayAsync(
            int relayId,
            string daqChannelList,
            float ovp,
            CancellationToken token)
        {
            if (!await SetVbattGeneratorAsync(0, 0.1f, ovp, false)) return false;
            if (!await VerifyPowerDomainsOffAsync("SECONDARY")) return false;
            if (!await ControlMpsRelayAsync(relayId, daqChannelList, true)) return false;
            if (!await SetVbattGeneratorAsync(0, 0.1f, ovp, true)) return false;
            if (await WaitForMps125VbattVoltageAsync(0, token)) return true;

            LogError?.Invoke(
                "MPS-125 Relay_6闭合后VBATT实测未达到0V条件，立即关闭VBATT并断开Relay_6。");
            await SetVbattGeneratorAsync(0, 0, ovp, false);
            await ControlMpsRelayAsync(relayId, daqChannelList, false);
            return false;
        }

        public async Task<bool> SafeConnectMpsVbattRelayAsync(
            int relayId,
            string daqChannelList,
            float ovp,
            CancellationToken token)
        {
            if (!await SetVbattGeneratorAsync(0, 0.1f, ovp, false)) return false;
            if (!await VerifyPowerDomainsOffAsync("SECONDARY")) return false;
            if (!await ControlMpsRelayAsync(relayId, daqChannelList, true)) return false;
            if (!await SetVbattGeneratorAsync(0, 0.1f, ovp, true)) return false;
            if (await WaitForMps250VbattVoltageAsync(0, token)) return true;

            LogError?.Invoke(
                "Relay_6闭合后VBATT实测未达到0V条件，立即关闭VBATT并断开Relay_6。");
            await SetVbattGeneratorAsync(0, 0, ovp, false);
            await ControlMpsRelayAsync(relayId, daqChannelList, false);
            return false;
        }

        public Task<bool> SafeConnectMpsVbattRelayAsync(
            int relayId,
            string daqChannelList,
            float ovp)
        {
            return SafeConnectMpsVbattRelayAsync(
                relayId,
                daqChannelList,
                ovp,
                CancellationToken.None);
        }

        public Task<bool> SafeConnectMpsVbattRelayAsync(int relayId, float ovp)
        {
            return SafeConnectMpsVbattRelayAsync(relayId, null, ovp);
        }

        public async Task<bool> SafeDisconnectMpsVbattRelayAsync(int relayId, string daqChannelList, float ovp)
        {
            if (!await SetVbattGeneratorAsync(0, 0, ovp, false)) return false;
            if (!await VerifyPowerDomainsOffAsync("SECONDARY")) return false;
            return await ControlMpsRelayAsync(relayId, daqChannelList, false);
        }

        public Task<bool> SafeDisconnectMpsVbattRelayAsync(int relayId, float ovp)
        {
            return SafeDisconnectMpsVbattRelayAsync(relayId, null, ovp);
        }
    }
}
