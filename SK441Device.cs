using System;
using System.Threading.Tasks;

namespace TestPlatform
{
    /// <summary>
    /// SK441 娴嬭瘯宸ヤ綅鐨勨€滆澶囩瀹垛€?(Hardware Abstraction Layer)
    /// 缁熶竴绠＄悊璇ュ伐浣嶄笅鎵€鏈?8 鍙拌澶囩殑鍒濆鍖栥€佽繛鎺ュ拰閲婃斁銆?    /// Sequence (娴嬭瘯娴佺▼) 鍙渶瑕佽皟鐢ㄨ繖涓被鐨勫疄渚嬶紝鑰屼笉闇€瑕佸叧蹇冨簳灞傜殑鍏蜂綋涓插彛鍒涘缓銆?    /// </summary>
    public class SK441Device : IDisposable
    {
        // 1. 瀹夎€愭柉鐢垫簮
        public AnsPowerSupply AnsPower { get; private set; }

        // 2 & 3. 鎭掓儬鐢垫簮 (2鍙?
        public HengHuiPowerSupply HengHuiPower1 { get; private set; }
        public HengHuiPowerSupply HengHuiPower2 { get; private set; }

        // 4. 鎭掓儬鐢靛瓙璐熻浇
        public HengHuiElectronicLoad ElectronicLoad { get; private set; }

        // 5. DAQ digital multimeter
        public DaqMultimeter Daq { get; private set; }

        // 6. 鐩爣娴嬭瘯鏉块€氳 (USB-B)
        public TargetBoardComm TargetBoard { get; private set; }

        // 7. 鍥轰欢鐑у綍鍣?(TTL)
        public FirmwareFlasher Flasher { get; private set; }

        // 娉? 涓洓鐨?RelayController 鍜?DigitalInputController 鏄潤鎬佸伐鍏风被锛?        // 瀹冧滑涓嶉渶瑕佸疄渚嬪寲锛岀洿鎺ラ€氳繃浼犲叆涓插彛鍙峰拰绔欏彿浣跨敤鍗冲彲銆?
        // 璋冭瘯涓撶敤鏍囪锛氳烦杩囩湡瀹炰覆鍙ｈ繛鎺ワ紝鏂逛究鑴辨満璋冭瘯娴佺▼
        public bool SkipComInit { get; set; } = GetBoolAppSetting("SKSkipComInit", false);

        public Action<string> LogInfo { get; set; }
        public Action<string> LogError { get; set; }
        private int _mockR10ReadCount;
        private int _mockR68ReadCount;
        private bool _mockMbdWestCmdActive;
        private bool _mockDutWestCmdActive;
        private double _mockVbusVoltage;
        private double _mockStringCurrent;
        private double _mockCcbCurrentSetpointMa;
        private bool _mockCcbEnabled;
        private bool _mockMosTestOn;
        private volatile bool _mockFixtureDown;
        private string _mockCalibrationDate = string.Empty;
        private string _mockBcmSerial = string.Empty;
        private double _mockGainVmid = 4096;
        private double _mockOffsetVmid;
        private double _mockGainIdch = 90000;
        private double _mockOffsetIdch;
        private double _mockShortProtectionVoltage = 2.9;

        public SK441Device()
        {
            // 瀹炰緥鍖栧悇涓华鍣ㄥ璞?            AnsPower = new AnsPowerSupply();
            HengHuiPower1 = new HengHuiPowerSupply();
            HengHuiPower2 = new HengHuiPowerSupply();
            ElectronicLoad = new HengHuiElectronicLoad();
            Daq = new DaqMultimeter();
            TargetBoard = new TargetBoardComm();
            Flasher = new FirmwareFlasher();

            // 缁戝畾鏃ュ織浜嬩欢锛屽皢搴曞眰浠櫒鐨勬棩蹇楀叏閮ㄥ線涓婃姏
            BindLogs(AnsPower);
            BindLogs(HengHuiPower1);
            BindLogs(HengHuiPower2);
            BindLogs(ElectronicLoad);
            BindLogs(Daq);
            BindLogs(TargetBoard);
            BindLogs(Flasher);
        }

        private void BindLogs(dynamic device)
        {
            try
            {
                device.LogInfo += new Action<string>(msg => LogInfo?.Invoke(msg));
                device.LogError += new Action<string>(msg => LogError?.Invoke(msg));
            }
            catch { /* 蹇界暐涓嶆敮鎸佷簨浠剁殑缁戝畾 */ }
        }

        private static bool GetBoolAppSetting(string key, bool defaultValue)
        {
            string value = System.Configuration.ConfigurationManager.AppSettings[key];
            bool parsed;
            return bool.TryParse(value, out parsed) ? parsed : defaultValue;
        }

        private static int GetIntAppSetting(string key, int defaultValue)
        {
            string value = System.Configuration.ConfigurationManager.AppSettings[key];
            int parsed;
            return int.TryParse(value, out parsed) ? parsed : defaultValue;
        }

        private static float GetFloatAppSetting(string key, float defaultValue)
        {
            string value = System.Configuration.ConfigurationManager.AppSettings[key];
            float parsed;
            return float.TryParse(value, out parsed) ? parsed : defaultValue;
        }

        private static string GetAppSetting(string key, string defaultValue = null)
        {
            string value = System.Configuration.ConfigurationManager.AppSettings[key];
            return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
        }

        private static string GetRelayResponseError(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return "empty response";

            if (response.IndexOf("TIMEOUT", StringComparison.OrdinalIgnoreCase) >= 0)
                return response;

            if (response.IndexOf("閿欒", StringComparison.OrdinalIgnoreCase) >= 0)
                return response;

            return null;
        }

        /// <summary>
        /// 涓€閿垵濮嬪寲鎵€鏈夎澶?        /// </summary>
        public Task<bool> InitializeAllDevicesAsync(
            string ansPort,
            string henghui1Port,
            string henghui2Port,
            string loadPort,
            string daqPort,
            string targetBoardPort,
            string ttlPort)
        {
            LogInfo?.Invoke("=========================================");
            LogInfo?.Invoke("寮€濮嬪垵濮嬪寲 SK441 娴嬭瘯宸ヤ綅鐨勬墍鏈夊簳灞傜‖浠?..");
            LogInfo?.Invoke("=========================================");

            if (SkipComInit)
            {
                LogInfo?.Invoke("[Debug] SkipComInit enabled, skip real serial connection.");
                return Task.FromResult(true);
            }

            bool allSuccess = true;

            try
            {
                // 1. 鍒濆鍖栫洰鏍囨澘涓插彛
                TargetBoard.Connect(targetBoardPort);

                // 2. 鍒濆鍖栫儳褰曚覆鍙?                Flasher.Connect(ttlPort);

                // 3. 渚濇鎵撳紑鍚勪华鍣ㄧ鍙?                LogInfo?.Invoke("姝ｅ湪杩炴帴鍚勭鐢垫簮銆佽礋杞藉強 DAQ 璁惧...");
                AnsPower.Connect(ansPort);
                HengHuiPower1.Connect(henghui1Port, address: 1);
                HengHuiPower2.Connect(henghui2Port, address: 2); // 鍋囪绗簩鍙扮珯鍙蜂负2
                ElectronicLoad.Connect(loadPort);
                Daq.Connect(daqPort);

                LogInfo?.Invoke(">>> All device ports opened.");

                // 4. Query DAQ to confirm device is online.
                string daqIdn = Daq.GetIdn();
                if (string.IsNullOrEmpty(daqIdn))
                {
                    LogError?.Invoke("DAQ 璁惧鏃犲搷搴旓紝璇锋鏌ユ帴绾匡紒");
                    allSuccess = false;
                }
                else
                {
                    LogInfo?.Invoke($"DAQ 鍦ㄧ嚎锛屽瀷鍙? {daqIdn}");
                }
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"璁惧鍒濆鍖栬繃绋嬩腑鍙戠敓寮傚父: {ex.Message}");
                allSuccess = false;
            }

            return Task.FromResult(allSuccess);
        }

        #region 楂樼骇纭欢鎺у埗灏佽 (渚?Sequence 璋冪敤)

        /// <summary>
        /// 璁剧疆瀹夎€愭柉鐢垫簮鐢靛帇
        /// </summary>
        public async Task<bool> SetAnsVoltageAsync(float voltage)
        {
            try
            {
                if (SkipComInit)
                {
                    await Task.Delay(50);
                    LogInfo?.Invoke($"[调试模式] 模拟设置AnsPower电源电压为 {voltage}V");
                    return true;
                }

                float currentLimit = GetFloatAppSetting("SKAnsCurrentLimit", 1.0f);
                AnsPower.SetVoltageCurrent(voltage, currentLimit);
                LogInfo?.Invoke($"已设置AnsPower电源电压为 {voltage}V");
                return true;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"设置AnsPower电源电压失败: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SetAnsVoltageCurrentOutputAsync(float voltage, float current, bool outputOn)
        {
            try
            {
                if (SkipComInit)
                {
                    await Task.Delay(50);
                    LogInfo?.Invoke($"[Debug] Mock AnsPower: {voltage}V/{current}A, output {(outputOn ? "ON" : "OFF")}");
                    return true;
                }

                AnsPower.SetVoltageCurrent(voltage, current);
                AnsPower.SetPower(outputOn);
                LogInfo?.Invoke($"AnsPower set: {voltage}V/{current}A, output {(outputOn ? "ON" : "OFF")}");
                return true;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"设置AnsPower电源输出失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 浠?DAQ 涓囩敤琛ㄨ鍙栫數鍘?        /// </summary>
        public async Task<float> MeasureDaqVoltageAsync()
        {
            try
            {
                if (SkipComInit)
                {
                    await Task.Delay(50);
                    float mockVal = 12.05f;
                    LogInfo?.Invoke($"[调试模式] 模拟 DAQ 测量电压结果: {mockVal}V");
                    return mockVal;
                }

                string channelList = GetAppSetting("SKDaqVoltageChannel", "@101");
                float range = GetFloatAppSetting("SKDaqVoltageRange", 10.0f);
                Daq.ConfigVoltageDc(channelList, range);
                float val = (float)Daq.ReadSingleValue();
                LogInfo?.Invoke($"DAQ 测量电压结果: {val}V");
                return val;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"DAQ 测量电压失败: {ex.Message}");
                return 0.0f;
            }
        }

        public async Task<double> MeasureDaqChannelVoltageAsync(int channel)
        {
            try
            {
                if (SkipComInit)
                {
                    await Task.Delay(50);
                    double mockVal = (channel == 113 || channel == 115) && _mockCcbEnabled && _mockCcbCurrentSetpointMa > 0.001
                        ? (_mockCcbCurrentSetpointMa / 1000.0) * (channel == 115 ? 0.0215914 : 0.0215909)
                        : (channel == 113 || channel == 115) && _mockMosTestOn
                        ? 0.65 * (channel == 115 ? 0.0215914 : 0.0215909)
                        : (channel == 114 || channel == 116) && _mockStringCurrent > 0.001
                        ? 134.36
                        : (channel == 109 || channel == 110) && _mockStringCurrent >= 5.5 && _mockCcbEnabled && _mockVbusVoltage <= 2.0
                        ? 0.7
                        : (channel == 109 || channel == 110) && _mockVbusVoltage > 0.001
                        ? _mockVbusVoltage
                        : (channel == 105 || channel == 106) && _mockVbusVoltage > 0.001
                        ? _mockVbusVoltage
                        : (channel == 113 || channel == 115 || channel == 118) && _mockStringCurrent > 0.001
                            ? _mockStringCurrent * (channel == 115 || channel == 118 ? 0.0215914 : 0.0215909)
                        : GetMockBcm125FirstStartupVoltage(channel);
                    LogInfo?.Invoke($"[调试模式] 模拟 DAQ 通道 {channel} 测量电压: {mockVal:0.###}V");
                    return mockVal;
                }

                string channelList = "@" + channel.ToString(System.Globalization.CultureInfo.InvariantCulture);
                double range = Math.Max(10.0, Math.Abs(GetExpectedDaqRangeHint(channel)));
                Daq.ConfigVoltageDc(channelList, (float)range);
                double val = Daq.ReadSingleValue();
                LogInfo?.Invoke($"DAQ 通道 {channel} 测量电压: {val:0.###}V");
                return val;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"DAQ 通道 {channel} 测量失败: {ex.Message}");
                return 0.0;
            }
        }

        private static double GetExpectedDaqRangeHint(int channel)
        {
            switch (channel)
            {
                case 102: return 20.0;
                case 103:
                case 104: return 20.0;
                default: return 10.0;
            }
        }

        private static double GetMockBcm125FirstStartupVoltage(int channel)
        {
            switch (channel)
            {
                case 101: return 5.02;
                case 102: return 15.10;
                case 103: return -13.75;
                case 104: return -13.76;
                case 105: return 140.0;
                case 107: return 3.31;
                case 108: return 3.30;
                case 111: return 3.29;
                case 112: return 3.30;
                case 117: return 0.0141; // 30 mA across 0.47R
                case 118: return 0.0141;
                case 119: return 0.132;  // 60 mA across 2.2R
                case 120: return 0.132;
                default: return 0.0;
            }
        }

        /// <summary>
        /// 鎺у埗缁х數鍣ㄥ紑鍏?(鍋囪杩欓噷鏈夌粺涓€鐨勭户鐢靛櫒鎺у埗绫?
        /// </summary>
        public async Task<bool> ControlRelayAsync(int relayId, bool isOpen)
        {
            try
            {
                if (SkipComInit)
                {
                    await Task.Delay(50);
                    LogInfo?.Invoke($"[调试模式] 模拟{(isOpen ? "闭合" : "断开")}继电器: {relayId}");
                    return true;
                }

                int address = GetIntAppSetting("SKRelayAddress", 1);
                int baudRate = GetIntAppSetting("SKRelayBaudRate", 38400);
                string comPort = GetAppSetting("SKRelayComPort", ComName.rs485ComName);
                string response = await RelayController.SendCommandAsync(
                    address, relayId, isOpen, 1, baudRate, comPort, msg => LogInfo?.Invoke(msg));
                bool ok = !string.IsNullOrWhiteSpace(response) &&
                          response.IndexOf("TIMEOUT", StringComparison.OrdinalIgnoreCase) < 0 &&
                          response.IndexOf("閿欒", StringComparison.OrdinalIgnoreCase) < 0;
                if (!ok)
                {
                    LogError?.Invoke($"继电器 {relayId} 控制失败: {response}");
                    return false;
                }
                LogInfo?.Invoke($"Relay {(isOpen ? "closed" : "opened")}: {relayId}");
                return true;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"继电器控制失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 璇诲彇 16 璺暟瀛楅噺杈撳叆妯″潡鐘舵€?(涓昏鐢ㄤ簬鎺ュ彛D-鎺у埗鎺ュ彛)
        /// </summary>
        public async Task<bool[]> ReadDigitalInputsAsync(int address = 1, string comPort = null)
        {
            try
            {
                if (SkipComInit)
                {
                    await Task.Delay(50);
                    // 调试模式仍模拟真实下压状态变化：
                    // 数字量4=0 表示板子已安装；数字量7=1 表示未下压，0 表示下压到位。
                    bool[] mockStates = new bool[16];
                    for (int i = 0; i < 16; i++) mockStates[i] = false;
                    mockStates[6] = !_mockFixtureDown;

                    return mockStates;
                }

                // 鐪熷疄璋冪敤
                bool[] states = await DigitalInputController.ReadDigitalInputAsync(address, 38400, comPort, msg => LogError?.Invoke(msg));
                if (states == null)
                {
                    LogError?.Invoke("读取数字输入模块失败，返回 null");
                    return new bool[16]; // 杩斿洖榛樿
                }
                return states;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"读取数字输入模块发生异常: {ex.Message}");
                return new bool[16];
            }
        }

        /// <summary>
        /// 鍏抽棴鎵€鏈夌墿鐞嗕华鍣ㄧ殑杈撳嚭 (鐢垫簮銆佽礋杞界瓑)
        /// </summary>
        public async Task<bool> TurnOffAllInstrumentsAsync()
        {
            try
            {
                if (SkipComInit)
                {
                    await Task.Delay(50);
                    LogInfo?.Invoke("[调试模式] 模拟关闭所有电源/负载输出。");
                    return true;
                }

                bool allSuccess = true;

                try { AnsPower.SetPower(false); }
                catch (Exception ex) { allSuccess = false; LogError?.Invoke($"ANS 电源关闭失败: {ex.Message}"); }

                try { HengHuiPower1.SetPower(false); }
                catch (Exception ex) { allSuccess = false; LogError?.Invoke($"HengHui1 电源关闭失败: {ex.Message}"); }

                try { HengHuiPower2.SetPower(false); }
                catch (Exception ex) { allSuccess = false; LogError?.Invoke($"HengHui2 电源关闭失败: {ex.Message}"); }

                try { ElectronicLoad.SetInputState(false); }
                catch (Exception ex) { allSuccess = false; LogError?.Invoke($"电子负载关闭失败: {ex.Message}"); }

                LogInfo?.Invoke("已发送关闭所有仪器输出的指令");
                return allSuccess;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"鍏抽棴浠櫒杈撳嚭鍙戠敓寮傚父: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> CloseTargetBoardCommunicationAsync()
        {
            try
            {
                if (SkipComInit)
                {
                    await Task.Delay(20);
                    LogInfo?.Invoke("[调试模式] 模拟关闭MBD/目标板串口通信。");
                    return true;
                }

                TargetBoard?.Disconnect();
                LogInfo?.Invoke("已关闭MBD/目标板串口通信。");
                return true;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"关闭MBD/目标板串口通信失败: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> CheckRequiredInstrumentsAsync()
        {
            try
            {
                if (SkipComInit)
                {
                    await Task.Delay(50);
                    LogInfo?.Invoke("[Debug] Mock instrument presence check.");
                    return true;
                }

                bool allSuccess = true;

                allSuccess = allSuccess && CheckConnected(AnsPower.IsConnected, "ANS 鐢垫簮C");
                allSuccess = allSuccess && CheckConnected(HengHuiPower1.IsConnected, "PS 9200 VBUS 鐢垫簮A");
                allSuccess = allSuccess && CheckConnected(HengHuiPower2.IsConnected, "PS 9200 String 鐢垫簮B");
                allSuccess = allSuccess && CheckConnected(ElectronicLoad.IsConnected, "EL 9200 鐢靛瓙璐熻浇");
                allSuccess = allSuccess && CheckConnected(Daq.IsConnected, "DAQ960/34970A");

                try
                {
                    string daqIdn = Daq.GetIdn();
                    bool daqOk = !string.IsNullOrWhiteSpace(daqIdn);
                    allSuccess = allSuccess && daqOk;
                    LogInfo?.Invoke(daqOk ? $"DAQ online: {daqIdn}" : "DAQ no response");
                }
                catch (Exception ex)
                {
                    allSuccess = false;
                    LogError?.Invoke($"DAQ 鍦ㄧ嚎妫€鏌ュけ璐? {ex.Message}");
                }

                return allSuccess;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"浠櫒鍦ㄧ嚎妫€鏌ュ彂鐢熷紓甯? {ex.Message}");
                return false;
            }
        }

        public async Task<bool> CheckProgrammingInstrumentsAsync()
        {
            try
            {
                if (SkipComInit)
                {
                    await Task.Delay(50);
                    LogInfo?.Invoke("[调试模式] 模拟检查 String generator、BUS generator、电子负载和 USB-B/MBD 通信在线");
                    return true;
                }

                bool allSuccess = true;
                allSuccess = allSuccess && CheckConnected(HengHuiPower1.IsConnected, "BUS generator / 鐢垫簮A");
                allSuccess = allSuccess && CheckConnected(HengHuiPower2.IsConnected, "String generator / 鐢垫簮B");
                allSuccess = allSuccess && CheckConnected(ElectronicLoad.IsConnected, "鐢靛瓙璐熻浇");
                allSuccess = allSuccess && CheckConnected(TargetBoard.IsConnected, "USB-B/MBD 閫氫俊涓插彛");
                return allSuccess;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"缂栫▼娴嬭瘯浠櫒鍦ㄧ嚎妫€鏌ュけ璐? {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SendMbdCommandAsync(string command, int waitAfterMs)
        {
            if (SkipComInit)
                ApplyMockMbdCommandState(command);

            try
            {
                if (SkipComInit)
                {
                    await Task.Delay(Math.Max(0, waitAfterMs));
                    LogInfo?.Invoke($"[调试模式] 模拟 USB-B/MBD 发送命令: {command}");
                    return true;
                }

                string response = await TargetBoard.SendCommandAsync(command, 2000);
                await Task.Delay(Math.Max(0, waitAfterMs));

                // MBD 鍛戒护涓嶄竴瀹氭瘡鏉￠兘鏈夋湁鏁堣浇鑽峰洖澶嶏紱鍙涓插彛鍐欒鏈紓甯稿嵆鍙户缁€?                if (response == null)
                    LogInfo?.Invoke($"USB-B/MBD 命令已发送但未读到回复: {command}");

                return true;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"USB-B/MBD 鍛戒护鍙戦€佸け璐?({command}): {ex.Message}");
                return false;
            }
        }

        public async Task<string> QueryMbdCommandAsync(string command, int waitAfterMs)
        {
            try
            {
                if (SkipComInit)
                {
                    await Task.Delay(Math.Max(0, waitAfterMs));
                    string mock = GetMockMbdResponseInstance(command);
                    LogInfo?.Invoke($"[调试模式] 模拟 USB-B/MBD 查询: {command} -> {mock}");
                    return mock;
                }

                string response = await TargetBoard.SendCommandAsync(command, 2000);
                await Task.Delay(Math.Max(0, waitAfterMs));
                LogInfo?.Invoke($"USB-B/MBD 鏌ヨ {command} 杩斿洖: {response}");
                return response;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"USB-B/MBD 查询失败 ({command}): {ex.Message}");
                return null;
            }
        }

        public Task<bool> CheckMbdCommunicationAsync()
        {
            if (SkipComInit)
            {
                LogInfo?.Invoke("[调试模式] 模拟 USB-B/MBD 串口通信已打开");
                return Task.FromResult(true);
            }

            bool ok = TargetBoard != null && TargetBoard.IsConnected;
            LogInfo?.Invoke(ok ? "USB-B/MBD 串口通信已打开" : "USB-B/MBD 串口未打开");
            return Task.FromResult(ok);
        }

        public string GetBcm125ExpectedFirmwareVersion()
        {
            return GetAppSetting("SKBCM125ExpectedFirmwareVersion", "BCM_125_V8.HEX");
        }

        public async Task<bool> SetVbusGeneratorAsync(float voltage, float current, float ovp, bool outputOn)
        {
            try
            {
                if (SkipComInit)
                {
                    await Task.Delay(50);
                    _mockVbusVoltage = voltage;
                    if (Math.Abs(voltage) < 0.001 && !outputOn)
                        _mockR68ReadCount = 0;
                    LogInfo?.Invoke($"[调试模式] 模拟设置 VBUS 发生器: {voltage}V/{current}A, OVP={ovp}V, 输出{(outputOn ? "ON" : "OFF")}");
                    return true;
                }

                HengHuiPower1.SetVoltageCurrent(voltage, current);
                HengHuiPower1.SetOvp(ovp);
                HengHuiPower1.SetPower(outputOn);
                LogInfo?.Invoke($"已设置 VBUS 发生器: {voltage}V/{current}A, OVP={ovp}V, 输出{(outputOn ? "ON" : "OFF")}");
                return true;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"设置 VBUS 发生器失败: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SetStringGeneratorAsync(float voltage, float current, float ovp, bool outputOn)
        {
            try
            {
                if (SkipComInit)
                {
                    await Task.Delay(50);
                    _mockStringCurrent = outputOn ? current : 0;
                    LogInfo?.Invoke($"[调试模式] 模拟设置 String 发生器: {voltage}V/{current}A, OVP={ovp}V, 输出{(outputOn ? "ON" : "OFF")}");
                    return true;
                }

                HengHuiPower2.SetVoltageCurrent(voltage, current);
                HengHuiPower2.SetOvp(ovp);
                HengHuiPower2.SetPower(outputOn);
                LogInfo?.Invoke($"已设置 String 发生器: {voltage}V/{current}A, OVP={ovp}V, 输出{(outputOn ? "ON" : "OFF")}");
                return true;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"设置 String 发生器失败: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ResetElectronicLoadAsync()
        {
            try
            {
                if (SkipComInit)
                {
                    await Task.Delay(50);
                    LogInfo?.Invoke("[调试模式] 模拟复位电子负载 CV 0V/0A/0W");
                    return true;
                }

                ElectronicLoad.SetInputState(false);
                ElectronicLoad.SetMode("CV");
                ElectronicLoad.SetVoltage(0);
                ElectronicLoad.SetCurrent(0);
                ElectronicLoad.SetPower(0);
                return true;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"复位电子负载失败: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SetElectronicLoadCvAsync(float voltage, float current, float power, bool inputOn)
        {
            try
            {
                if (SkipComInit)
                {
                    await Task.Delay(50);
                    LogInfo?.Invoke($"[Debug] Mock electronic load CV {voltage}V/{current}A/{power}W, input {(inputOn ? "ON" : "OFF")}");
                    return true;
                }

                ElectronicLoad.SetMode("CV");
                ElectronicLoad.SetVoltage(voltage);
                ElectronicLoad.SetCurrent(current);
                ElectronicLoad.SetPower(power);
                ElectronicLoad.SetInputState(inputOn);
                return true;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"Set electronic load CV failed: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SetShortProtectionSimulatorAsync(string pointName, double voltage)
        {
            try
            {
                bool useNoContact;
                string expectedContact;
                if (Math.Abs(voltage - 3.3) < 0.05)
                {
                    useNoContact = true;
                    expectedContact = "NO=3.3V";
                }
                else if (Math.Abs(voltage - 2.9) < 0.05)
                {
                    useNoContact = false;
                    expectedContact = "NC=2.9V";
                }
                else
                {
                    LogError?.Invoke($"短路保护信号 {pointName} 只能通过RL CORTO切换2.9V/3.3V，当前请求 {voltage:0.###}V");
                    return false;
                }

                _mockShortProtectionVoltage = useNoContact ? 3.3 : 2.9;
                string relay = GetConfiguredDaqRelayList("SKBCM125RlCortoDaqRelay", "@204");
                bool ok = await SetDaqRelaysAsync(relay, useNoContact, $"RL CORTO CH4 {pointName} {expectedContact}");
                if (ok)
                    LogInfo?.Invoke($"短路保护信号 {pointName} 已切换到 {expectedContact}，万用表继电器 {relay}");
                return ok;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"设置短路保护信号失败: {ex.Message}");
                return false;
            }
        }
        private static string GetMockMbdResponse(string command)
        {
            if (string.Equals(command, "R14", StringComparison.OrdinalIgnoreCase))
                return "O14 = 99,00,00,99,99,99,99,99,99,99,99,99,99,99,99,99;slaves timeout1";

            if (string.Equals(command, "R15", StringComparison.OrdinalIgnoreCase))
                return "O15 = 99,99,99,99,99,99,99,99,99,99,99,99,99,99,99,00;slaves timeout2";

            if (string.Equals(command, "R0", StringComparison.OrdinalIgnoreCase))
                return "fw_version = BCM_125_V8.HEX";

            if (string.Equals(command, "R55", StringComparison.OrdinalIgnoreCase))
                return "adc13 vref PC3 = 1550";

            if (string.Equals(command, "R53", StringComparison.OrdinalIgnoreCase))
                return "adc11 vcc PC1 = 3900";

            if (string.Equals(command, "R54", StringComparison.OrdinalIgnoreCase))
                return "adc12 vee PC2 = 1350";

            if (string.Equals(command, "R52", StringComparison.OrdinalIgnoreCase))
                return "adc10 hs_temp PC0 = 2200";

            if (string.Equals(command, "R51", StringComparison.OrdinalIgnoreCase))
                return "adc1 vstr_pos PA1 = 4096";

            if (string.Equals(command, "R18", StringComparison.OrdinalIgnoreCase))
                return "gain_vstr_pos = 140000";

            if (string.Equals(command, "R19", StringComparison.OrdinalIgnoreCase))
                return "offset_vstr_pos = 0";

            if (string.Equals(command, "R43", StringComparison.OrdinalIgnoreCase))
                return "vstr_pos = 140000";

            return "OK";
        }

        private string GetMockMbdResponseInstance(string command)
        {
            if (string.Equals(command, "R10", StringComparison.OrdinalIgnoreCase))
            {
                _mockR10ReadCount++;
                return _mockR10ReadCount % 2 == 1 ? "running_time = 5.0" : "running_time = 1.0";
            }

            if (string.Equals(command, "R61", StringComparison.OrdinalIgnoreCase))
                return "st_emerg PC8 = " + ((_mockShortProtectionVoltage >= 3.2 || _mockMbdWestCmdActive || _mockDutWestCmdActive) ? "0" : "1");

            if (string.Equals(command, "R60", StringComparison.OrdinalIgnoreCase))
                return "st_prot_short PB15 = " + (_mockShortProtectionVoltage >= 3.2 ? "0" : "1");

            if (string.Equals(command, "R81", StringComparison.OrdinalIgnoreCase))
                return "st_westngh PD3 = " + (_mockDutWestCmdActive ? "0" : "1");

            if (string.Equals(command, "R68", StringComparison.OrdinalIgnoreCase))
            {
                _mockR68ReadCount++;
                return _mockR68ReadCount == 1 ? "adc14 mcu_vs_prec PC1 = 0" : "adc14 mcu_vs_prec PC1 = 2750";
            }

            if (string.Equals(command, "R69", StringComparison.OrdinalIgnoreCase))
                return "adc3 mcu_vbus PA3 = 2750";

            if (string.Equals(command, "R93", StringComparison.OrdinalIgnoreCase))
            {
                double mockVmidAdc = _mockVbusVoltage >= 60.0 ? 1800.0 : 159.0;
                return "adc2 Vmid1 PA2 = " + mockVmidAdc.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            }

            if (string.Equals(command, "R92", StringComparison.OrdinalIgnoreCase))
                return "vstr_mid1 = " + Math.Round(_mockVbusVoltage * 1000).ToString("0", System.Globalization.CultureInfo.InvariantCulture);

            if (string.Equals(command, "R20", StringComparison.OrdinalIgnoreCase))
                return "gain_vstr_mid = " + Math.Round(_mockGainVmid).ToString("0", System.Globalization.CultureInfo.InvariantCulture);

            if (string.Equals(command, "R21", StringComparison.OrdinalIgnoreCase))
                return "offset_vstr_mid = " + Math.Round(_mockOffsetVmid).ToString("0", System.Globalization.CultureInfo.InvariantCulture);

            if (string.Equals(command, "R57", StringComparison.OrdinalIgnoreCase))
                return "adc15 idch PC5 = " + Math.Round(_mockStringCurrent <= 1.5 ? 45.0 : 270.0).ToString("0", System.Globalization.CultureInfo.InvariantCulture);

            if (string.Equals(command, "R45", StringComparison.OrdinalIgnoreCase))
                return "idch = " + Math.Round(_mockStringCurrent <= 0.001 ? 0.0 : _mockStringCurrent * 1000).ToString("0", System.Globalization.CultureInfo.InvariantCulture);

            if (string.Equals(command, "R59", StringComparison.OrdinalIgnoreCase))
                return "st_gm_sc PB13 = " + (_mockStringCurrent >= 0.8 ? "1" : "0");

            if (string.Equals(command, "R50", StringComparison.OrdinalIgnoreCase))
            {
                double adc = _mockStringCurrent >= 5.5 && _mockCcbEnabled ? 32.0 : (_mockVbusVoltage >= 10 ? 675.0 : 45.0);
                return "adc0 vstr_neg PA0 = " + Math.Round(adc).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            }

            if (string.Equals(command, "R56", StringComparison.OrdinalIgnoreCase))
            {
                double adc = _mockStringCurrent >= 5.5 && _mockCcbEnabled ? 2800.0 : 1425.0;
                return "adc14iccb PC4 = " + Math.Round(adc).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            }

            if (string.Equals(command, "R42", StringComparison.OrdinalIgnoreCase))
            {
                double mv = _mockVbusVoltage * 1000.0;
                if (_mockStringCurrent >= 5.5 && _mockCcbEnabled && _mockVbusVoltage <= 2.0)
                    mv = 700.0;
                else if (_mockStringCurrent >= 5.5 && _mockCcbEnabled && _mockVbusVoltage >= 10.0)
                    mv = 14500.0;
                return "vstr_neg = " + Math.Round(mv).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            }

            if (string.Equals(command, "R44", StringComparison.OrdinalIgnoreCase))
            {
                double ma = _mockMosTestOn ? -650.0 : (_mockStringCurrent >= 5.5 && _mockCcbEnabled ? 6000.0 : 0.0);
                return "iccb = " + Math.Round(ma).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            }

            if (string.Equals(command, "ACT->FLASH", StringComparison.OrdinalIgnoreCase))
                return "ACT values copied to FLASH\r\n000027 chars answered. Ready.";

            if (string.Equals(command, "R40", StringComparison.OrdinalIgnoreCase))
                return "data_calibrazione = " + _mockCalibrationDate;

            if (string.Equals(command, "R41", StringComparison.OrdinalIgnoreCase))
                return "bcm_serial = " + _mockBcmSerial;

            return GetMockMbdResponse(command);
        }

        private void ApplyMockMbdCommandState(string command)
        {
            string normalized = (command ?? string.Empty).Trim().ToUpperInvariant();
            if (normalized == "W95=1")
                _mockMbdWestCmdActive = true;
            else if (normalized == "W95=0")
                _mockMbdWestCmdActive = false;
            else if (normalized == "W67=1")
                _mockDutWestCmdActive = true;
            else if (normalized == "W67=0")
                _mockDutWestCmdActive = false;
            else if (normalized == "W69=1")
                _mockCcbEnabled = false;
            else if (normalized == "W69=0")
                _mockCcbEnabled = true;
            else if (normalized == "W71=1")
                _mockMosTestOn = true;
            else if (normalized == "W71=0")
                _mockMosTestOn = false;
            else if (normalized.StartsWith("W12="))
                double.TryParse(normalized.Substring(4), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _mockCcbCurrentSetpointMa);
            else if (normalized.StartsWith("W40="))
                _mockCalibrationDate = (command ?? string.Empty).Trim().Substring(4).Trim();
            else if (normalized.StartsWith("W41="))
                _mockBcmSerial = (command ?? string.Empty).Trim().Substring(4).Trim();
            else if (normalized.StartsWith("W20="))
                double.TryParse(normalized.Substring(4), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _mockGainVmid);
            else if (normalized.StartsWith("W21="))
                double.TryParse(normalized.Substring(4), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _mockOffsetVmid);
            else if (normalized.StartsWith("W26="))
                double.TryParse(normalized.Substring(4), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _mockGainIdch);
            else if (normalized.StartsWith("W27="))
                double.TryParse(normalized.Substring(4), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _mockOffsetIdch);
        }

        private bool CheckConnected(bool isConnected, string name)
        {
            if (isConnected)
            {
                LogInfo?.Invoke($"{name}: connected");
                return true;
            }

            LogError?.Invoke($"{name}: not connected");
            return false;
        }

        /// <summary>
        /// 鏂紑娴嬭瘯/鍔熺巼缁х數鍣ㄣ€備笉瑕佸湪杩欓噷閲婃斁宸ヨ涓嬪帇鍏佽缁х數鍣?Y1/Y2銆?        /// </summary>
        public async Task<bool> OpenAllRelaysAsync()
        {
            try
            {
                if (SkipComInit)
                {
                    await Task.Delay(50);
                    LogInfo?.Invoke("[调试模式] 模拟断开测试继电器(保留工装 Y1/Y2 状态)");
                    return true;
                }

                int relayCount = GetIntAppSetting("SKRelayCount", 64);
                bool allSuccess = true;
                for (int relayId = 1; relayId <= relayCount; relayId++)
                {
                    bool ok = await ControlRelayAsync(relayId, false);
                    allSuccess = allSuccess && ok;
                }
                LogInfo?.Invoke("已断开测试继电器；工装下压许可继电器 Y1/Y2 保持不动");
                return allSuccess;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"断开所有继电器失败: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ClosePlacementDetectionRelaysAsync()
        {
            return await SetDaqRelayPairAsync(true, "闂悎");
        }

        public async Task<bool> OpenPlacementDetectionRelaysAsync()
        {
            return await SetDaqRelayPairAsync(false, "断开");
        }

        public async Task<bool> SetDaqRelaysAsync(string channelList, bool close, string description)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(channelList))
                {
                    LogInfo?.Invoke($"{description}: no DAQ relay configured, skipped.");
                    return true;
                }

                if (SkipComInit)
                {
                    await Task.Delay(50);
                    LogInfo?.Invoke($"[调试模式] 模拟{(close ? "闭合" : "断开")}万用表继电器 {description}: {channelList}");
                    return true;
                }

                if (close)
                    Daq.CloseRelay(channelList);
                else
                    Daq.OpenRelay(channelList);

                LogInfo?.Invoke($"DAQ relay {(close ? "closed" : "opened")} {description}: {channelList}");
                return true;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"{description} 万用表继电器控制失败: {ex.Message}");
                return false;
            }
        }

        public string GetConfiguredDaqRelayList(string key, string defaultRelays)
        {
            string configured = GetAppSetting(key, defaultRelays);
            if (string.IsNullOrWhiteSpace(configured))
                return string.Empty;

            configured = configured.Trim();
            return configured.StartsWith("@", StringComparison.Ordinal) ? configured : "@" + configured;
        }

        private async Task<bool> SetDaqRelayPairAsync(bool close, string actionName)
        {
            try
            {
                string channelA = GetAppSetting("SKPlacementRelayA", "@218");
                string channelB = GetAppSetting("SKPlacementRelayB", "@219");

                if (SkipComInit)
                {
                    await Task.Delay(50);
                    LogInfo?.Invoke($"[调试模式] 模拟{actionName}万用表继电器 {channelA}, {channelB}");
                    return true;
                }

                if (close)
                {
                    Daq.CloseRelay(channelA);
                    Daq.CloseRelay(channelB);
                }
                else
                {
                    Daq.OpenRelay(channelA);
                    Daq.OpenRelay(channelB);
                }

                LogInfo?.Invoke($"DAQ placement relays {actionName}: {channelA}, {channelB}");
                return true;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"{actionName}万用表继电器失败: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> StopFixturePressDownAsync(string boardType)
        {
            try
            {
                int[] relays = GetFixtureReleaseRelays(boardType);

                if (SkipComInit)
                {
                    await Task.Delay(50);
                    _mockFixtureDown = false;
                    LogInfo?.Invoke($"[调试模式] 模拟释放/上升工装继电器: {string.Join(",", relays)}");
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
                    LogInfo?.Invoke($"[调试模式] 模拟闭合工装下压许可继电器: {string.Join(",", relays)}");
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

        public async Task<bool> SetFixtureRelaysAsync(int[] relayIds, bool close, string description)
        {
            try
            {
                if (relayIds == null || relayIds.Length == 0)
                {
                    LogInfo?.Invoke($"{description}: no fixture relay configured, skipped.");
                    return true;
                }

                if (SkipComInit)
                {
                    await Task.Delay(50);
                    LogInfo?.Invoke($"[调试模式] 模拟{(close ? "闭合" : "断开")}工装继电器 {description}: {string.Join(",", relayIds)}");
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
            var relays = new System.Collections.Generic.List<int>();
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
            string defaultRelays = string.Equals(boardType, "BCM-125", StringComparison.OrdinalIgnoreCase) ? "1,2" : "1";
            string configured = GetAppSetting("SKFixtureReleaseRelays", GetAppSetting("SKFixtureStopPressDownRelays", defaultRelays));
            string[] parts = configured.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var relays = new System.Collections.Generic.List<int>();

            foreach (string part in parts)
            {
                int relayId;
                if (int.TryParse(part.Trim(), out relayId) && relayId > 0)
                    relays.Add(relayId);
            }

            return relays.Count > 0 ? relays.ToArray() : new[] { 1 };
        }

        private async Task<bool> ControlFixtureRelayAsync(int relayId, bool isOpen)
        {
            int address = GetIntAppSetting("SKFixtureRelayAddress", 1);
            int baudRate = GetIntAppSetting("SKFixtureRelayBaudRate", 38400);
            string comPort = GetAppSetting("SKFixtureRelayComPort", GetAppSetting("SKRelayComPort", ComName.rs485ComName));
            string response = await RelayController.SendCommandAsync(
                address, relayId, isOpen, 1, baudRate, comPort, msg => LogInfo?.Invoke(msg));
            string error = GetRelayResponseError(response);
            if (error != null)
            {
                LogError?.Invoke($"工装继电器 {relayId} 控制失败: {error}");
                return false;
            }

            LogInfo?.Invoke($"Fixture relay {(isOpen ? "closed" : "opened")}: {relayId}");
            return true;
        }

        #endregion

        /// <summary>
        /// 涓€閿噴鏀炬墍鏈夎澶囪祫婧愶紝鍏抽棴涓插彛
        /// </summary>
        public void Dispose()
        {
            LogInfo?.Invoke("正在关闭 SK441 工位的所有设备连接...");
            AnsPower?.Disconnect();
            HengHuiPower1?.Disconnect();
            HengHuiPower2?.Disconnect();
            ElectronicLoad?.Disconnect();
            Daq?.Disconnect();
            TargetBoard?.Disconnect();
            Flasher?.Disconnect();
        }
    }
}

