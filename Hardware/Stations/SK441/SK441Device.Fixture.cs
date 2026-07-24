using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TestPlatform
{
    public partial class SK441Device
    {
        public async Task<bool> StopFixturePressDownAsync(string boardType)
        {
            try
            {
                int[] relays = GetFixtureReleaseRelays(boardType);
                if (SkipComInit)
                {
                    await Task.Delay(50);
                    _mockFixtureDown = false;
                    LogInfo?.Invoke(
                        $"[调试模式] 模拟释放/上升工装继电器: {string.Join(",", relays)}");
                    return true;
                }

                bool allSuccess = true;
                foreach (int relayId in relays)
                {
                    bool ok = await ControlFixtureRelayAsync(relayId, false);
                    allSuccess = allSuccess && ok;
                }

                return allSuccess;
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
                int[] relays = GetFixtureReleaseRelays(boardType);
                if (SkipComInit)
                {
                    await Task.Delay(50);
                    _mockFixtureDown = true;
                    LogInfo?.Invoke(
                        $"[调试模式] 模拟闭合工装下压许可继电器: {string.Join(",", relays)}");
                    return true;
                }

                bool allSuccess = true;
                foreach (int relayId in relays)
                {
                    bool ok = await ControlFixtureRelayAsync(relayId, true);
                    allSuccess = allSuccess && ok;
                }

                return allSuccess;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"闭合工装下压许可继电器失败: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SetFixtureRelaysAsync(
            int[] relayIds,
            bool close,
            string description)
        {
            try
            {
                if (relayIds == null || relayIds.Length == 0)
                {
                    LogInfo?.Invoke(
                        $"{description}: no fixture relay configured, skipped.");
                    return true;
                }

                if (SkipComInit)
                {
                    await Task.Delay(50);
                    LogInfo?.Invoke(
                        $"[调试模式] 模拟{(close ? "闭合" : "断开")}工装继电器 " +
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

            string[] parts = configured.Split(
                new[] { ',' },
                StringSplitOptions.RemoveEmptyEntries);
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

        private int[] GetFixtureReleaseRelays(string boardType)
        {
            string defaultRelays =
                string.Equals(
                    boardType,
                    "BCM-125",
                    StringComparison.OrdinalIgnoreCase)
                    ? "1,2"
                    : "1";
            string configured = GetAppSetting(
                "SKFixtureReleaseRelays",
                GetAppSetting("SKFixtureStopPressDownRelays", defaultRelays));
            string[] parts = configured.Split(
                new[] { ',' },
                StringSplitOptions.RemoveEmptyEntries);
            var relays = new List<int>();

            foreach (string part in parts)
            {
                int relayId;
                if (int.TryParse(part.Trim(), out relayId) && relayId > 0)
                    relays.Add(relayId);
            }

            return relays.Count > 0 ? relays.ToArray() : new[] { 1 };
        }

        private async Task<bool> ControlFixtureRelayAsync(
            int relayId,
            bool isOpen)
        {
            int address = GetIntAppSetting("SKFixtureRelayAddress", 1);
            int baudRate = GetIntAppSetting("SKFixtureRelayBaudRate", 38400);
            string comPort = GetAppSetting(
                "SKFixtureRelayComPort",
                GetAppSetting("SKRelayComPort", ComName.rs485ComName));
            string response = await RelayController.SendCommandAsync(
                address,
                relayId,
                isOpen,
                1,
                baudRate,
                comPort,
                message => LogInfo?.Invoke(message));
            string error = GetRelayResponseError(response);
            if (error != null)
            {
                LogError?.Invoke($"工装继电器 {relayId} 控制失败: {error}");
                return false;
            }

            LogInfo?.Invoke(
                $"Fixture relay {(isOpen ? "closed" : "opened")}: {relayId}");
            return true;
        }
    }
}
