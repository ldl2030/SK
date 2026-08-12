using System;
using System.Collections.Generic;
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
                    if ((relayId == 1 || relayId == 5) && _vbusOutputOn) blocked = true;
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
                int requiredRelay = domain == "ANS" && !isMps ? 7 : domain == "VBUS" && isMps ? 1 : 0;
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

                float highVoltageThreshold = GetFloatAppSetting("SKSafeOpenVoltageThreshold", 5.0f);
                if (Math.Abs(voltage) > highVoltageThreshold)
                {
                    bool pathClosed = true;
                    if (isMps && domain == "SECONDARY")
                        pathClosed = IsAnyPowerRelayClosedUnsafe(5, 6);

                    if (!pathClosed)
                    {
                        error = "禁止开启 " + domain + " 高压输出：尚未确认对应功率路径继电器闭合。";
                        _powerSafetyTrace.Add("BLOCK OUTPUT " + domain + " HIGH VOLTAGE ON");
                        return false;
                    }
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

            float voltageThreshold = GetFloatAppSetting("SKSafeOpenVoltageThreshold", 5.0f);
            float currentThreshold = GetFloatAppSetting("SKSafeOpenCurrentThreshold", 0.05f);
            int timeoutMs = GetIntAppSetting("SKSafeDischargeTimeoutMs", 10000);
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

        private Task<bool> VerifyRelayPowerIsOffAsync(int relayId)
        {
            bool isMps = (SimulationBoardType ?? string.Empty).StartsWith("MPS", StringComparison.OrdinalIgnoreCase);
            if (isMps)
            {
                if (relayId == 1 || relayId == 5)
                    return VerifyPowerDomainsOffAsync("VBUS");
                if (relayId == 6)
                    return VerifyPowerDomainsOffAsync("SECONDARY");
                return Task.FromResult(true);
            }

            if (relayId == 7)
                return VerifyPowerDomainsOffAsync("ANS");
            if (relayId >= 1 && relayId <= 16)
                return VerifyPowerDomainsOffAsync("VBUS", "SECONDARY", "LOAD");
            return Task.FromResult(true);
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

        public async Task<bool> SafeSwitchMpsVbusRelayAsync(int relayId, bool close, float voltage, float current, float ovp, bool restoreOutput)
        {
            if (!await SetVbusGeneratorAsync(0, 0, ovp, false)) return false;
            if (!await VerifyPowerDomainsOffAsync("VBUS")) return false;
            if (!await ControlRelayAsync(relayId, close)) return false;
            return !restoreOutput || await SetVbusGeneratorAsync(voltage, current, ovp, true);
        }

        public async Task<bool> SafeConnectMpsVbattRelayAsync(int relayId, float ovp)
        {
            if (!await SetVbattGeneratorAsync(0, 0.1f, ovp, false)) return false;
            if (!await VerifyPowerDomainsOffAsync("SECONDARY")) return false;
            if (!await ControlRelayAsync(relayId, true)) return false;
            return await SetVbattGeneratorAsync(0, 0.1f, ovp, true);
        }

        public async Task<bool> SafeDisconnectMpsVbattRelayAsync(int relayId, float ovp)
        {
            if (!await SetVbattGeneratorAsync(0, 0, ovp, false)) return false;
            if (!await VerifyPowerDomainsOffAsync("SECONDARY")) return false;
            return await ControlRelayAsync(relayId, false);
        }
    }
}
