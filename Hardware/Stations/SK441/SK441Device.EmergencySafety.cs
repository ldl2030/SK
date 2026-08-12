using System;
using System.Threading.Tasks;

namespace TestPlatform
{
    public partial class SK441Device
    {
        private readonly object _emergencyStopSync = new object();
        private readonly object _emergencyShutdownSync = new object();
        private Task<bool> _emergencyShutdownTask;
        private bool _emergencyStopLatched;
        private string _emergencyStopReason;

        public bool IsEmergencyStopLatched
        {
            get
            {
                lock (_emergencyStopSync)
                    return _emergencyStopLatched;
            }
        }

        public string EmergencyStopReason
        {
            get
            {
                lock (_emergencyStopSync)
                    return _emergencyStopReason;
            }
        }

        public int GetSafetyPollMilliseconds()
        {
            return Math.Max(50, GetIntAppSetting("SKSafetyPollMs", 100));
        }

        public void LatchEmergencyStop(string reason)
        {
            lock (_emergencyStopSync)
            {
                if (_emergencyStopLatched) return;
                _emergencyStopLatched = true;
                _emergencyStopReason = string.IsNullOrWhiteSpace(reason)
                    ? "急停或安全输入失联"
                    : reason;
            }
            lock (_powerSafetySync)
                _powerSafetyTrace.Add("EMERGENCY LATCHED " + EmergencyStopReason);
            LogError?.Invoke("急停已锁存：" + EmergencyStopReason);
        }

        private bool ValidateEmergencyAllowsEnergizing(string operation, out string error)
        {
            lock (_emergencyStopSync)
            {
                if (!_emergencyStopLatched)
                {
                    error = null;
                    return true;
                }

                error = "急停已锁存，禁止" + operation + "。原因：" + _emergencyStopReason;
            }
            lock (_powerSafetySync)
                _powerSafetyTrace.Add("BLOCK EMERGENCY " + operation);
            return false;
        }

        public Task<bool> EmergencyStopOutputsAsync(string boardType, string reason)
        {
            LatchEmergencyStop(reason);
            lock (_emergencyShutdownSync)
            {
                if (_emergencyShutdownTask == null)
                    _emergencyShutdownTask = ExecuteEmergencyStopOutputsAsync(boardType);
                return _emergencyShutdownTask;
            }
        }

        private async Task<bool> ExecuteEmergencyStopOutputsAsync(string boardType)
        {
            LogError?.Invoke("正在执行急停输出关断；治具立即失压上升由硬件安全回路负责。");

            bool instrumentsOff = await TurnOffAllInstrumentsAsync();
            bool dischargeSafe = instrumentsOff &&
                await VerifyPowerDomainsOffAsync("ANS", "VBUS", "SECONDARY", "LOAD");

            bool powerRelaysOff = false;
            bool daqRelaysOff = false;
            if (dischargeSafe)
            {
                powerRelaysOff = await OpenAllRelaysAsync();
                daqRelaysOff = await SetDaqRelaysAsync(
                    "@301:320",
                    false,
                    "急停断开全部DAQ测试路径");
            }
            else
            {
                LogError?.Invoke("急停掉电确认失败：禁止自动切断可能仍带电的测试功率路径，请检查硬件安全回路。");
            }

            // 硬件已经负责治具立即上升；这里清除软件下压命令，防止急停复位后再次下压。
            bool fixtureCommandOff = await ResetFixturePressDownCommandAsync(boardType);
            return instrumentsOff && dischargeSafe && powerRelaysOff && daqRelaysOff && fixtureCommandOff;
        }
    }
}
