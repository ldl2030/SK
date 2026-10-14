using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TestPlatform
{
    public partial class SK441Device
    {
        private readonly List<string> _fixtureActuationTrace = new List<string>();

        public string[] GetFixtureActuationTrace()
        {
            lock (_fixtureActuationTrace)
                return _fixtureActuationTrace.ToArray();
        }

        public async Task<bool> StopFixturePressDownAsync(string boardType)
        {
            try
            {
                if (!await VerifyFixturePowerIsOffAsync())
                {
                    LogError?.Invoke("治具释放被安全联锁阻止：请先关闭全部电源和负载并确认母线完成放电。");
                    return false;
                }
                return await ResetFixturePressDownCommandAsync(boardType);
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"释放/上升工装失败: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> EnableFixturePressDownAsync(string boardType)
        {
            try
            {
                string safetyError;
                if (!ValidateEmergencyAllowsEnergizing("闭合工装下压继电器", out safetyError))
                {
                    LogError?.Invoke(safetyError);
                    return false;
                }

                int[] relays = GetFixtureActuationRelays(boardType);
                if (SkipComInit)
                {
                    await Task.Delay(1);
                    foreach (int relayId in relays)
                        RecordFixtureActuation(relayId, true);
                    _mockFixtureDown = true;
                    LogInfo?.Invoke(
                        $"[调试模式] 模拟闭合工装站1下压继电器: {string.Join(",", relays)}");
                    return true;
                }

                bool allSuccess = true;
                foreach (int relayId in relays)
                {
                    bool ok = await ControlFixtureRelayAsync(relayId, true);
                    if (ok) RecordFixtureActuation(relayId, true);
                    allSuccess = allSuccess && ok;
                }

                // BCM-125 的Y1/Y2是一个逻辑执行器；任一路失败都必须复位全部下压通道。
                if (!allSuccess)
                {
                    LogError?.Invoke("工装下压继电器未全部闭合，立即复位全部下压通道。");
                    await ResetFixturePressDownCommandAsync(boardType);
                }
                return allSuccess;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"闭合工装下压许可继电器失败: {ex.Message}");
                return false;
            }
        }

        // 硬件急停回路负责治具立即上升；此方法只清除软件下压输出，防止急停复位后再次动作。
        public async Task<bool> ResetFixturePressDownCommandAsync(string boardType)
        {
            int[] relays = GetFixtureActuationRelays(boardType);
            if (SkipComInit)
            {
                await Task.Delay(1);
                foreach (int relayId in relays)
                    RecordFixtureActuation(relayId, false);
                _mockFixtureDown = false;
                return true;
            }

            bool allSuccess = true;
            foreach (int relayId in relays)
            {
                bool ok = await ControlFixtureRelayAsync(relayId, false);
                if (ok) RecordFixtureActuation(relayId, false);
                allSuccess = allSuccess && ok;
            }
            return allSuccess;
        }

        public async Task<bool> SetFixtureRelaysAsync(
            int[] relayIds,
            bool close,
            string description)
        {
            try
            {
                string safetyError;
                if (close && !ValidateEmergencyAllowsEnergizing("闭合工装辅助继电器", out safetyError))
                {
                    LogError?.Invoke(safetyError);
                    return false;
                }
                if (relayIds == null || relayIds.Length == 0)
                {
                    LogInfo?.Invoke($"{description}: no fixture relay configured, skipped.");
                    return true;
                }

                if (SkipComInit)
                {
                    await Task.Delay(1);
                    LogInfo?.Invoke(
                        $"[调试模式] 模拟{(close ? "闭合" : "断开")}工装辅助继电器 " +
                        $"{description}: {string.Join(",", relayIds)}");
                    return true;
                }

                bool allSuccess = true;
                foreach (int relayId in relayIds)
                {
                    bool ok = await ControlFixtureRelayAsync(relayId, close);
                    allSuccess = allSuccess && ok;
                }
                return allSuccess;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"{description} 工装继电器控制失败: {ex.Message}");
                return false;
            }
        }

        public int[] GetConfiguredRelayList(string key, string defaultRelays)
        {
            string configured = GetAppSetting(key, defaultRelays);
            if (string.IsNullOrWhiteSpace(configured))
                return new int[0];

            string[] parts = configured.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var relays = new List<int>();
            foreach (string part in parts)
            {
                int relayId;
                if (int.TryParse(part.Trim(), out relayId) && relayId > 0)
                    relays.Add(relayId);
            }
            return relays.ToArray();
        }

        public bool ShouldBypassFixtureDownCheck()
        {
            return GetBoolAppSetting("SKBypassFixtureDownCheck", false);
        }

        public double GetFixtureNoticeMinimumSeconds()
        {
            return GetFloatAppSetting("SKFixtureNoticeMinimumSeconds", 3.0f);
        }

        public int GetFixtureDownTimeoutMilliseconds()
        {
            return Math.Max(5000, GetIntAppSetting("SKFixtureDownTimeoutMs", 30000));
        }

        private int[] GetFixtureActuationRelays(string boardType)
        {
            int[] expected;
            string configKey;
            if (string.Equals(boardType, "BCM-125", StringComparison.OrdinalIgnoreCase))
            {
                expected = new[] { 1, 2 };
                configKey = "SKBCM125FixtureRelays";
            }
            else if (string.Equals(boardType, "BCM-250", StringComparison.OrdinalIgnoreCase))
            {
                expected = new[] { 1 };
                configKey = "SKBCM250FixtureRelays";
            }
            else if (string.Equals(boardType, "MPS-125", StringComparison.OrdinalIgnoreCase))
            {
                expected = new[] { 1 };
                configKey = "SKMPS125FixtureRelays";
            }
            else if (string.Equals(boardType, "MPS-250", StringComparison.OrdinalIgnoreCase))
            {
                expected = new[] { 1 };
                configKey = "SKMPS250FixtureRelays";
            }
            else
            {
                throw new ArgumentException(
                    "未知板型，不能确定工装站1下压继电器映射: " + boardType,
                    nameof(boardType));
            }

            int[] configured = GetConfiguredRelayList(configKey, string.Join(",", expected));
            bool matches = configured.Length == expected.Length;
            for (int i = 0; matches && i < expected.Length; i++)
                matches = configured[i] == expected[i];
            if (!matches)
            {
                throw new InvalidOperationException(
                    $"{configKey}与工装映射不一致：期望{string.Join(",", expected)}，" +
                    $"当前{string.Join(",", configured)}。");
            }
            return expected;
        }

        private void RecordFixtureActuation(int relayId, bool close)
        {
            lock (_fixtureActuationTrace)
                _fixtureActuationTrace.Add(
                    "FIXTURE STATION 1 RELAY " + relayId + " " +
                    (close ? "CLOSED" : "OPEN"));
        }

        private async Task<bool> ControlFixtureRelayAsync(int relayId, bool close)
        {
            int address = GetIntAppSetting("SKFixtureRelayAddress", 1);
            if (address != 1)
            {
                LogError?.Invoke($"工装继电器站位配置错误：期望地址1，当前地址{address}。");
                return false;
            }

            int baudRate = GetIntAppSetting("SKFixtureRelayBaudRate", 38400);
            string comPort = !string.IsNullOrWhiteSpace(ComName.fixtureRelayComName)
                ? ComName.fixtureRelayComName
                : GetAppSetting(
                    "SKFixtureRelayComPort",
                    GetAppSetting("SKRelayComPort", ComName.rs485ComName));
            string response = await RelayController.SendCommandAsync(
                address,
                relayId,
                close,
                1,
                baudRate,
                comPort,
                null);
            string error = GetRelayResponseError(response);
            if (error != null)
            {
                LogError?.Invoke($"工装站1继电器{relayId}控制失败: {error}");
                return false;
            }

            LogInfo?.Invoke($"工装继电器站1 Y{relayId} -> {(close ? "闭合" : "断开")}");
            return true;
        }
    }
}
