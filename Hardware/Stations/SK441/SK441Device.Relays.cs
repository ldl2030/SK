using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestPlatform.TestSequences;

namespace TestPlatform
{
    public partial class SK441Device
    {
        private readonly SemaphoreSlim _daqRelaySemaphore = new SemaphoreSlim(1, 1);
        private readonly Dictionary<int, bool> _daqRelayStates = new Dictionary<int, bool>();
        // Logical MPS Relay_2 is DAQ305/306; it is never station-3 Y2.
        private readonly Dictionary<int, bool> _mpsRelayStates = new Dictionary<int, bool>();

        private static int[] ParseDaqRelayChannels(string channelList)
        {
            var channels = new List<int>();
            foreach (string item in channelList.Split(','))
            {
                string[] bounds = item.Trim().TrimStart('@').Split(':');
                int first;
                int last;
                if (bounds.Length > 2 || !int.TryParse(bounds[0], NumberStyles.None, CultureInfo.InvariantCulture, out first))
                    throw new ArgumentException("Invalid DAQ relay list: " + channelList);
                last = first;
                if (bounds.Length == 2 && !int.TryParse(bounds[1], NumberStyles.None, CultureInfo.InvariantCulture, out last))
                    throw new ArgumentException("Invalid DAQ relay range: " + channelList);
                if (first < 301 || last > 320 || last < first)
                    throw new ArgumentOutOfRangeException(nameof(channelList), "DAQ functional relays must be within 301-320.");
                channels.AddRange(Enumerable.Range(first, last - first + 1));
            }
            return channels.Distinct().ToArray();
        }

        private void UpdateDaqRelayStates(int[] channels, bool? close)
        {
            lock (_powerSafetySync)
            {
                foreach (int channel in channels)
                {
                    if (close.HasValue) _daqRelayStates[channel] = close.Value;
                    else _daqRelayStates.Remove(channel);
                }
                if (!IsMpsBoard()) return;
                for (int id = 2; id <= 6; id++)
                {
                    int[] mapped = ParseDaqRelayChannels(MpsFunctionalRelayMap.GetDaqRelayChannels(id));
                    if (!mapped.Intersect(channels).Any()) continue;
                    bool state;
                    if (mapped.All(ch => _daqRelayStates.TryGetValue(ch, out state) && state))
                        _mpsRelayStates[id] = true;
                    else if (mapped.All(ch => _daqRelayStates.TryGetValue(ch, out state) && !state))
                        _mpsRelayStates[id] = false;
                    else
                        _mpsRelayStates.Remove(id);
                }
            }
        }

        public async Task<bool> ControlMpsRelaysAsync(IEnumerable<int> logicalRelayIds, bool close)
        {
            int[] ids = (logicalRelayIds ?? Enumerable.Empty<int>()).Distinct().ToArray();
            bool passed = true;
            foreach (int id in ids)
            {
                bool success = await ControlMpsRelayAsync(id, MpsFunctionalRelayMap.GetDaqRelayChannels(id), close);
                passed = success && passed;
                if (!success && close)
                {
                    // On a failed close, stop energizing and isolate the whole group.
                    foreach (int rollbackId in ids.Reverse())
                        await ControlMpsRelayAsync(rollbackId, MpsFunctionalRelayMap.GetDaqRelayChannels(rollbackId), false);
                    return false;
                }
            }
            // Opening continues through failures so all other channels are isolated.
            return passed;
        }

        private void InvalidatePowerRelayStates(IEnumerable<int> relayIds)
        {
            lock (_powerSafetySync)
                foreach (int id in relayIds) _powerRelayStates.Remove(id);
        }

        private void ResetRelayCommandStates()
        {
            lock (_powerSafetySync)
            {
                _daqRelayStates.Clear();
                _mpsRelayStates.Clear();
                _powerRelayStates.Clear();
            }
        }

        public async Task<bool> SetDaqRelaysAsync(string channelList, bool close, string description)
        {
            int[] channels = null;
            await _daqRelaySemaphore.WaitAsync();
            try
            {
                string safetyError;
                if (close && !ValidateEmergencyAllowsEnergizing("闭合DAQ测试继电器 " + channelList, out safetyError))
                {
                    LogError?.Invoke(safetyError);
                    return false;
                }
                if (string.IsNullOrWhiteSpace(channelList))
                {
                    LogInfo?.Invoke(description + ": 未配置DAQ继电器，跳过。");
                    return true;
                }
                channels = ParseDaqRelayChannels(channelList);
                int[] changed;
                lock (_powerSafetySync)
                    changed = channels.Where(ch => !_daqRelayStates.ContainsKey(ch) || _daqRelayStates[ch] != close).ToArray();
                if (changed.Length == 0) return true;
                if (close && IsMpsBoard())
                {
                    foreach (int id in Enumerable.Range(2, 5))
                    {
                        int[] mapped = ParseDaqRelayChannels(MpsFunctionalRelayMap.GetDaqRelayChannels(id));
                        if (!mapped.Intersect(changed).Any()) continue;
                        if (!ValidatePowerRelaySwitch(id, true, out safetyError, true) ||
                            !await VerifyRelayPowerIsOffAsync(id))
                        {
                            LogError?.Invoke(safetyError ?? ("MPS逻辑继电器 Relay_" + id + "闭合前掉电确认失败。"));
                            return false;
                        }
                    }
                }
                string commandChannels = "@" + string.Join(",", changed);
                if (SkipComInit) await Task.Delay(50);
                else if (close) Daq.CloseRelay(commandChannels);
                else Daq.OpenRelay(commandChannels);
                UpdateDaqRelayStates(changed, close);
                LogInfo?.Invoke($"{(SkipComInit ? "[Simulation] " : "")}DAQ继电器 {commandChannels} -> {(close ? "闭合" : "断开")}，{description}");
                return true;
            }
            catch (Exception ex)
            {
                // A partial write/timeout leaves every requested channel unknown.
                if (channels != null) UpdateDaqRelayStates(channels, null);
                LogError?.Invoke(description + " DAQ继电器控制失败: " + ex.Message);
                return false;
            }
            finally { _daqRelaySemaphore.Release(); }
        }
    }
}
