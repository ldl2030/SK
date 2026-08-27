using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TestPlatform
{
    public sealed class DigitalInputReadResult
    {
        public bool CommunicationOk { get; set; }
        public bool[] States { get; set; }
        public string Error { get; set; }
    }

    /// <summary>
    /// SK441 娴嬭瘯宸ヤ綅鐨勨€滆澶囩瀹垛€?(Hardware Abstraction Layer)
    /// 缁熶竴绠＄悊璇ュ伐浣嶄笅鎵€鏈?8 鍙拌澶囩殑鍒濆鍖栥€佽繛鎺ュ拰閲婃斁銆?    /// Sequence (娴嬭瘯娴佺▼) 鍙渶瑕佽皟鐢ㄨ繖涓被鐨勫疄渚嬶紝鑰屼笉闇€瑕佸叧蹇冨簳灞傜殑鍏蜂綋涓插彛鍒涘缓銆?    /// </summary>
    public partial class SK441Device : IDisposable
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
        // 单点模式开关：
        // App.config: SKExecutionMode=Simulation|Hardware
        // 临时覆盖: TESTPLATFORM_SK_MODE=Simulation|Hardware
        public bool SkipComInit { get; set; } = ResolveSimulationMode();
        public bool SkipFirmwareProgramming { get; set; }

        public string ExecutionModeName => SkipComInit ? "Simulation" : "Hardware";
        public bool IsTtlProgrammingRequired =>
            !SkipFirmwareProgramming && GetBoolAppSetting("SKRequireTtl", true);

        public bool TryResolveBcm250FirmwarePath(
            string firmwareFileName,
            out string firmwarePath,
            out string error)
        {
            return TryResolveBcmFirmwarePath(
                "BCM250",
                "SKBCM250FirmwarePath",
                null,
                firmwareFileName,
                out firmwarePath,
                out error);
        }

        public bool TryResolveBcm125FirmwarePath(
            string firmwareFileName,
            out string firmwarePath,
            out string error)
        {
            return TryResolveBcmFirmwarePath(
                "BCM125",
                "SKBCM125FirmwarePath",
                Path.Combine("ProjectConfig", "SK", "BCM-125", "Firmware"),
                firmwareFileName,
                out firmwarePath,
                out error);
        }

        private static bool TryResolveBcmFirmwarePath(
            string boardName,
            string settingKey,
            string defaultRelativeDirectory,
            string firmwareFileName,
            out string firmwarePath,
            out string error)
        {
            string configuredPath = GetAppSetting(settingKey);
            firmwarePath = string.IsNullOrWhiteSpace(configuredPath)
                ? Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    defaultRelativeDirectory ?? string.Empty,
                    firmwareFileName ?? string.Empty)
                : Environment.ExpandEnvironmentVariables(configuredPath.Trim());
            if (!Path.IsPathRooted(firmwarePath))
            {
                firmwarePath = Path.GetFullPath(Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    firmwarePath));
            }

            if (!string.Equals(
                    Path.GetExtension(firmwarePath),
                    ".hex",
                    StringComparison.OrdinalIgnoreCase))
            {
                error = boardName + "固件必须是HEX文件：" + firmwarePath;
                return false;
            }
            if (!File.Exists(firmwarePath))
            {
                error = boardName + "固件文件不存在：" + firmwarePath;
                return false;
            }

            error = string.Empty;
            return true;
        }

        public async Task<bool> FlashBcm250FirmwareAsync(
            string firmwareFileName,
            CancellationToken cancellationToken)
        {
            string port = string.IsNullOrWhiteSpace(ComName.uartComName)
                ? GetAppSetting("TtlPort")
                : ComName.uartComName;
            if (string.IsNullOrWhiteSpace(port))
            {
                LogError?.Invoke("BCM250烧录失败：未配置独立TTL串口 TtlPort。");
                return false;
            }

            string firmwarePath;
            string firmwareError;
            if (!TryResolveBcm250FirmwarePath(
                    firmwareFileName,
                    out firmwarePath,
                    out firmwareError))
            {
                LogError?.Invoke("BCM250烧录失败：" + firmwareError);
                return false;
            }

            return await Flasher.FlashFirmwareAsync(
                firmwarePath,
                port,
                cancellationToken);
        }

        public async Task<bool> FlashBcm125FirmwareAsync(
            string firmwareFileName,
            CancellationToken cancellationToken)
        {
            string port = string.IsNullOrWhiteSpace(ComName.uartComName)
                ? GetAppSetting("TtlPort")
                : ComName.uartComName;
            if (string.IsNullOrWhiteSpace(port))
            {
                LogError?.Invoke("BCM125烧录失败：未配置独立TTL串口 TtlPort。");
                return false;
            }

            string firmwarePath;
            string firmwareError;
            if (!TryResolveBcm125FirmwarePath(
                    firmwareFileName,
                    out firmwarePath,
                    out firmwareError))
            {
                LogError?.Invoke("BCM125烧录失败：" + firmwareError);
                return false;
            }

            return await Flasher.FlashFirmwareAsync(
                firmwarePath,
                port,
                cancellationToken);
        }
        public bool IsEmergencyStopTriggered(bool logicalState)
        {
            return GetBoolAppSetting("SKEmergencyStopActiveHigh", true)
                ? logicalState
                : !logicalState;
        }
        public string SimulationBoardType { get; set; } = "BCM-125";
        public bool SimulateDigitalInputCommunicationFailure { get; set; }

        public Action<string> LogInfo { get; set; }
        public Action<string> LogError { get; set; }
        private int _mockR10ReadCount;
        private int _mockR68ReadCount;
        private bool _mockMbdWestCmdActive;
        private bool _mockDutWestCmdActive;
        private double _mockVbusVoltage;
        private double _mockVbusCurrentLimit;
        private double _mockSecondaryVoltage;
        private double _mockSecondaryCurrentLimit;
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
        private float _ansConfiguredVoltage = float.NaN;
        private float _ansConfiguredCurrent = float.NaN;
        private string _digitalInputPort;
        private string _targetBoardPortCandidate;
        private string _lastSuccessfulTargetBoardPort;
        private string _lastLoggedDigitalInputBits;

        public SK441Device()
        {
            // Instantiate every device even before connecting physical ports.
            AnsPower = new AnsPowerSupply();
            HengHuiPower1 = new HengHuiPowerSupply();
            HengHuiPower2 = new HengHuiPowerSupply();
            ElectronicLoad = new HengHuiElectronicLoad();
            Daq = new DaqMultimeter();
            TargetBoard = new TargetBoardComm();
            Flasher = new FirmwareFlasher();

            // 缁戝畾鏃ュ織浜嬩欢锛屽皢搴曞眰浠櫒鐨勬棩蹇楀叏閮ㄥ線涓婃姏
            BindLogs(TargetBoard);
            BindLogs(Flasher);
        }

        private void BindLogs(IDeviceLogSource device)
        {
            if (device == null)
                throw new ArgumentNullException(nameof(device));

            device.LogInfo += msg => LogInfo?.Invoke(msg);
            device.LogError += msg => LogError?.Invoke(msg);
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

        private static bool ResolveSimulationMode()
        {
            string mode = Environment.GetEnvironmentVariable("TESTPLATFORM_SK_MODE");
            if (string.IsNullOrWhiteSpace(mode))
                mode = GetAppSetting("SKExecutionMode");

            if (string.Equals(mode, "Simulation", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mode, "Sim", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(mode, "Hardware", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mode, "Real", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // 兼容旧配置；新部署只需要维护 SKExecutionMode。
            return GetBoolAppSetting("SKSkipComInit", false);
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
            string digitalInputPort,
            string fixtureRelayPort,
            string relay16Port,
            string targetBoardPort,
            string ttlPort,
            ISet<string> requiredComPorts = null)
        {
            LogInfo?.Invoke("=========================================");
            LogInfo?.Invoke("开始初始化 SK441 测试工位的所有底层硬件...");
            LogInfo?.Invoke("=========================================");

            if (SkipComInit)
            {
                LogInfo?.Invoke("[Debug] SkipComInit enabled, skip real serial connection.");
                return Task.FromResult(true);
            }

            bool requireTtl = IsTtlProgrammingRequired;
            bool useProjectRequirements = requiredComPorts != null && requiredComPorts.Count > 0;
            Func<string, bool> isRequired = key =>
                !useProjectRequirements || requiredComPorts.Contains(key);
            var requiredPorts = new[]
            {
                new { Key = "ansPowerComName", Name = "ANS电源", Port = ansPort },
                new { Key = "hengHuiPowerAComName", Name = "恒辉电源A", Port = henghui1Port },
                new { Key = "hengHuiPowerBComName", Name = "恒辉电源B", Port = henghui2Port },
                new { Key = "electronicLoadComName", Name = "KUNKIN 7303电子负载", Port = loadPort },
                new { Key = "daqComName", Name = "GWInstek DAQ-9600", Port = daqPort },
                new { Key = "digitalInputComName", Name = "16路数字量输入模块", Port = digitalInputPort },
                new { Key = "fixtureRelayComName", Name = "工装继电器模块", Port = fixtureRelayPort },
                new { Key = "relay16ComName", Name = "16路继电器模块", Port = relay16Port },
                new { Key = "uartComName", Name = "固件烧录TTL", Port = ttlPort }
            }.Where(item => isRequired(item.Key) &&
                (requireTtl || item.Name != "固件烧录TTL")).ToArray();
            string[] missingDevices = requiredPorts
                .Where(item => string.IsNullOrWhiteSpace(item.Port))
                .Select(item => item.Name)
                .ToArray();
            if (missingDevices.Length > 0)
            {
                LogError?.Invoke($"缺少必需设备：{string.Join("、", missingDevices)}");
                return Task.FromResult(false);
            }

            string[] duplicatePorts = requiredPorts
                .GroupBy(item => item.Port, StringComparer.OrdinalIgnoreCase)
                .Where(group =>
                {
                    string[] names = group.Select(item => item.Name).ToArray();
                    string[] sharedBusDevices =
                    {
                        "16路数字量输入模块",
                        "工装继电器模块",
                        "16路继电器模块"
                    };
                    bool sharedFixtureBus =
                        names.Length > 1 &&
                        names.All(name => sharedBusDevices.Contains(name));
                    return names.Length > 1 && !sharedFixtureBus;
                })
                .Select(group =>
                    $"{group.Key} 被同时分配给 {string.Join("、", group.Select(item => item.Name))}")
                .ToArray();
            if (duplicatePorts.Length > 0)
            {
                foreach (string duplicate in duplicatePorts)
                    LogError?.Invoke($"串口配置冲突：{duplicate}");
                return Task.FromResult(false);
            }

            bool allSuccess = true;
            Action<string, bool, Action> connect = (deviceName, required, action) =>
            {
                try
                {
                    action();
                    LogInfo?.Invoke(required
                        ? $"{deviceName}连接成功"
                        : $"可选设备 {deviceName} 已提前连接成功");
                }
                catch (Exception ex)
                {
                    if (required)
                    {
                        allSuccess = false;
                        LogError?.Invoke($"{deviceName}连接失败：{ex.Message}");
                    }
                    else
                    {
                        LogInfo?.Invoke(
                            $"警告：可选设备 {deviceName} 提前连接失败；当前初始化继续，" +
                            $"后续使用前仍会检查在线状态：{ex.Message}");
                    }
                }
            };

            int ansAddress = GetIntAppSetting("SKAnsAddress", 1);
            int electronicLoadBaudRate = GetIntAppSetting("SKElectronicLoadBaudRate", 38400);
            _digitalInputPort = digitalInputPort;
            _targetBoardPortCandidate = targetBoardPort;
            if (!string.IsNullOrWhiteSpace(targetBoardPort))
                _lastSuccessfulTargetBoardPort = targetBoardPort;
            ComName.fixtureRelayComName = fixtureRelayPort;
            ComName.relay16ComName = relay16Port;
            ComName.rs485ComName = fixtureRelayPort;

            LogInfo?.Invoke(
                "MBD USB-B暂不连接：将在DUT上电并等待启动完成后的通信步骤中自动识别。");
            bool ttlRequired = requireTtl && isRequired("uartComName");
            if ((ttlRequired || !string.IsNullOrWhiteSpace(ttlPort)) &&
                !string.IsNullOrWhiteSpace(ttlPort))
                connect("固件烧录TTL", ttlRequired, () => Flasher.Connect(ttlPort));
            else if (isRequired("uartComName"))
                LogInfo?.Invoke("独立TTL未连接：已配置跳过烧录，继续实机测试。");
            if (isRequired("ansPowerComName") || !string.IsNullOrWhiteSpace(ansPort))
                connect("ANS电源", isRequired("ansPowerComName"),
                    () => AnsPower.Connect(ansPort, address: (byte)ansAddress));
            if (isRequired("hengHuiPowerAComName") || !string.IsNullOrWhiteSpace(henghui1Port))
                connect("恒辉8602电源A (SCPI 9600 8N1)", isRequired("hengHuiPowerAComName"),
                    () => HengHuiPower1.Connect(henghui1Port));
            if (isRequired("hengHuiPowerBComName") || !string.IsNullOrWhiteSpace(henghui2Port))
                connect("恒辉8602电源B (SCPI 9600 8N1)", isRequired("hengHuiPowerBComName"),
                    () => HengHuiPower2.Connect(henghui2Port));
            if (isRequired("electronicLoadComName") || !string.IsNullOrWhiteSpace(loadPort))
                connect(
                    "KUNKIN 7303电子负载",
                    isRequired("electronicLoadComName"),
                    () => ElectronicLoad.Connect(loadPort, electronicLoadBaudRate));
            if (isRequired("daqComName") || !string.IsNullOrWhiteSpace(daqPort))
                connect("GWInstek DAQ-9600", isRequired("daqComName"), () =>
            {
                Daq.Connect(
                    daqPort,
                    9600,
                    GetIntAppSetting("SKDaqReadTimeoutMs", 5000));
                string daqIdn = Daq.GetIdn();
                if (string.IsNullOrWhiteSpace(daqIdn) ||
                    daqIdn.IndexOf("DAQ-9600", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    throw new InvalidOperationException($"IDN不匹配：{daqIdn ?? "无响应"}");
                }
                });

            if (allSuccess)
                LogInfo?.Invoke("全部必需串口设备已连接。");

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
            string safetyError;
            if (outputOn && !ValidatePowerSourceEnable("ANS", voltage, out safetyError))
            {
                LogError?.Invoke(safetyError);
                return false;
            }
            if (SkipComInit)
            {
                await Task.Delay(50);
                _ansConfiguredVoltage = voltage;
                _ansConfiguredCurrent = current;
                RecordPowerOutput("ANS", outputOn);
                LogInfo?.Invoke($"[Debug] Mock AnsPower: {voltage}V/{current}A, output {(outputOn ? "ON" : "OFF")}");
                return true;
            }

            try
            {
                AnsPower.SetVoltageCurrent(voltage, current);
                _ansConfiguredVoltage = voltage;
                _ansConfiguredCurrent = current;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"设置ANS电压/电流 {voltage}V/{current}A 失败: {ex.Message}");
                return false;
            }

            try
            {
                AnsPower.SetPower(outputOn);
                RecordPowerOutput("ANS", outputOn);
                LogInfo?.Invoke($"AnsPower set: {voltage}V/{current}A, output {(outputOn ? "ON" : "OFF")}");
                return true;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"设置ANS输出{(outputOn ? "开启" : "关闭")}失败: {ex.Message}");
                return false;
            }
        }

        public bool IsAnsSetpointConfigured(float voltage, float current)
        {
            return !float.IsNaN(_ansConfiguredVoltage) &&
                   !float.IsNaN(_ansConfiguredCurrent) &&
                   Math.Abs(_ansConfiguredVoltage - voltage) < 0.001f &&
                   Math.Abs(_ansConfiguredCurrent - current) < 0.001f;
        }

        public async Task<bool> SetAnsPowerOnlyAsync(float configuredVoltage, bool outputOn)
        {
            string safetyError;
            if (outputOn && !ValidatePowerSourceEnable("ANS", configuredVoltage, out safetyError))
            {
                LogError?.Invoke(safetyError);
                return false;
            }

            if (SkipComInit)
            {
                await Task.Delay(50);
                RecordPowerOutput("ANS", outputOn);
                LogInfo?.Invoke($"[Debug] Mock AnsPower output {(outputOn ? "ON" : "OFF")}");
                return true;
            }

            try
            {
                AnsPower.SetPower(outputOn);
                if (outputOn)
                {
                    await Task.Delay(100);
                    ushort status1;
                    ushort status2;
                    AnsPower.ReadStatus(out status1, out status2);
                    if ((status1 & 0x0010) == 0)
                    {
                        LogError?.Invoke($"ANS输出开启命令已应答，但状态未确认输出允许：status1=0x{status1:X4}");
                        return false;
                    }
                    LogInfo?.Invoke($"ANS输出状态确认成功：status1=0x{status1:X4}");
                }

                RecordPowerOutput("ANS", outputOn);
                LogInfo?.Invoke($"ANS output {(outputOn ? "ON" : "OFF")}");
                return true;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"设置ANS输出{(outputOn ? "开启" : "关闭")}失败: {ex.Message}");
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
                    double mockVal = (channel == Bcm125StationMap.String1ShuntDaqChannel || channel == Bcm125StationMap.String2ShuntDaqChannel) && _mockCcbEnabled && _mockCcbCurrentSetpointMa > 0.001
                        ? (_mockCcbCurrentSetpointMa / 1000.0) * (channel == Bcm125StationMap.String2ShuntDaqChannel ? Bcm125StationMap.String2ShuntOhms : Bcm125StationMap.String1ShuntOhms)
                        : (channel == Bcm125StationMap.String1ShuntDaqChannel || channel == Bcm125StationMap.String2ShuntDaqChannel) && _mockMosTestOn
                        ? 0.65 * (channel == Bcm125StationMap.String2ShuntDaqChannel ? Bcm125StationMap.String2ShuntOhms : Bcm125StationMap.String1ShuntOhms)
                        : (channel == 114 || channel == 116) && _mockStringCurrent > 0.001
                        ? 134.36
                        : (channel == 109 || channel == 110) && _mockStringCurrent >= 5.5 && _mockCcbEnabled && _mockVbusVoltage <= 2.0
                        ? 0.7
                        : (channel == 109 || channel == 110) && _mockVbusVoltage > 0.001
                        ? _mockVbusVoltage
                        : (channel == 105 || channel == Bcm125StationMap.VmidReferenceDaqChannel) && _mockVbusVoltage > 0.001
                        ? _mockVbusVoltage
                        : (channel == Bcm125StationMap.String1ShuntDaqChannel ||
                           channel == Bcm125StationMap.String2ShuntDaqChannel ||
                           channel == 118) && _mockStringCurrent > 0.001
                            ? _mockStringCurrent *
                              (channel == Bcm125StationMap.String2ShuntDaqChannel || channel == 118
                                  ? Bcm125StationMap.String2ShuntOhms
                                  : Bcm125StationMap.String1ShuntOhms)
                        : GetMockBcm125FirstStartupVoltage(channel);
                    LogInfo?.Invoke($"[调试模式] 模拟 DAQ 通道 {channel} 测量电压: {mockVal:0.###}V");
                    return mockVal;
                }

                string channelList = "@" + channel.ToString(System.Globalization.CultureInfo.InvariantCulture);
                double val = Daq.MeasureVoltageDc(channelList, out string rawResponse);
                LogInfo?.Invoke($"DAQ 通道 {channel} 测量电压: {val:0.######}V (原始响应: {rawResponse})");
                return val;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"DAQ 通道 {channel} 测量失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Measure a required number of DAQ samples and return their average.
        /// The DAQ state/error queue is prepared once for the whole series.
        /// Any failed sample invalidates the series; a failed read must never
        /// be substituted with 0 and included in the average.
        /// </summary>
        public async Task<double> MeasureDaqChannelVoltageAverageAsync(
            int channel,
            int sampleCount,
            int intervalMs,
            CancellationToken token)
        {
            if (sampleCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(sampleCount), "DAQ sample count must be greater than zero.");

            if (SkipComInit)
            {
                double mockTotal = 0.0;
                var mockSchedule = System.Diagnostics.Stopwatch.StartNew();
                for (int index = 0; index < sampleCount; index++)
                {
                    token.ThrowIfCancellationRequested();
                    await WaitForDaqSampleStartAsync(
                        mockSchedule,
                        index,
                        intervalMs,
                        token);
                    mockTotal += await MeasureDaqChannelVoltageAsync(channel);
                }
                return mockTotal / sampleCount;
            }

            string channelList = "@" + channel.ToString(System.Globalization.CultureInfo.InvariantCulture);
            double total = 0.0;
            int currentSample = 0;
            try
            {
                Daq.PrepareVoltageDcMeasurementSeries(channelList);
                var schedule = System.Diagnostics.Stopwatch.StartNew();
                bool overrunLogged = false;
                for (int index = 0; index < sampleCount; index++)
                {
                    token.ThrowIfCancellationRequested();
                    bool onSchedule = await WaitForDaqSampleStartAsync(
                        schedule,
                        index,
                        intervalMs,
                        token);
                    if (!onSchedule && !overrunLogged && index > 0)
                    {
                        overrunLogged = true;
                        LogInfo?.Invoke(
                            $"DAQ 通道 {channel} 单次通信耗时超过 {intervalMs}ms；" +
                            "已取消额外等待并按最快可用速度连续采样。");
                    }
                    currentSample = index + 1;
                    double value;
                    string rawResponse;
                    try
                    {
                        value = Daq.ReadVoltageDcMeasurementSeriesSample(
                            out rawResponse);
                    }
                    catch (TimeoutException firstTimeout)
                    {
                        LogInfo?.Invoke(
                            $"DAQ 通道 {channel} 第 {currentSample}/{sampleCount} 次读取超时；" +
                            "执行ABOR/*CLS、重新配置通道后重读一次。原因: " +
                            firstTimeout.Message);
                        Daq.RecoverVoltageDcMeasurement();
                        Daq.PrepareVoltageDcMeasurementSeries(channelList);
                        await Task.Delay(100, token);
                        value = Daq.ReadVoltageDcMeasurementSeriesSample(
                            out rawResponse);
                    }
                    total += value;
                    LogInfo?.Invoke(
                        $"DAQ 通道 {channel} 第 {currentSample}/{sampleCount} 次测量电压: " +
                        $"{value:0.######}V (原始响应: {rawResponse})");

                }

                return total / sampleCount;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                try
                {
                    Daq.RecoverVoltageDcMeasurement();
                }
                catch (Exception recoveryEx)
                {
                    LogError?.Invoke($"DAQ 通道 {channel} 测量失败后恢复失败: {recoveryEx.Message}");
                }

                string message =
                    $"DAQ 通道 {channel} 第 {currentSample}/{sampleCount} 次测量失败，整批平均值无效: {ex.Message}";
                LogError?.Invoke(message);
                throw new InvalidOperationException(message, ex);
            }
        }

        /// <summary>
        /// Measure a PWM-related voltage over a real elapsed-time window using
        /// the DAQ-9600 internal timer and reading memory. Both endpoints are
        /// sampled, so an 8000 ms window at 100 ms uses 81 readings (0..8000 ms).
        /// </summary>
        public async Task<double> MeasureDaqChannelVoltageWindowAverageAsync(
            int channel,
            int windowMs,
            int intervalMs,
            CancellationToken token)
        {
            if (windowMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(windowMs));
            if (intervalMs <= 0 || intervalMs > windowMs)
                throw new ArgumentOutOfRangeException(nameof(intervalMs));
            if (windowMs % intervalMs != 0)
            {
                throw new ArgumentException(
                    "DAQ window must be an exact multiple of the sample interval.",
                    nameof(windowMs));
            }

            if (SkipComInit)
                return await MeasureDaqChannelVoltageAsync(channel);

            int sampleCount = (windowMs / intervalMs) + 1;
            string channelList = "@" + channel.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            bool scanConfigurationAttempted = false;
            try
            {
                // MCU_ISOBATT ranges from approximately 0 V to 3.3 V. A fixed
                // 10 V range avoids autorange switching on the PWM edges.
                scanConfigurationAttempted = true;
                Daq.StartTimedVoltageDcScan(
                    channelList,
                    10.0,
                    0.15,
                    intervalMs / 1000.0,
                    sampleCount);
                // Depending on firmware, the first TIM trigger can be immediate
                // or one interval after INIT. FETCH? waits for a final reading,
                // so leave one interval plus a small margin before bulk readback.
                await Task.Delay(windowMs + intervalMs + 250, token);
                int bufferedCount = Daq.GetPointsCount();
                if (bufferedCount != sampleCount)
                {
                    throw new InvalidOperationException(
                        $"DAQ internal timer produced {bufferedCount}/{sampleCount} readings " +
                        $"within the {windowMs}ms window; the requested {intervalMs}ms " +
                        "sample interval was not maintained.");
                }
                double[] values = Daq.FetchTimedVoltageDcScan(
                    sampleCount,
                    out string rawResponse);
                double average = values.Average();
                double minimum = values.Min();
                double maximum = values.Max();
                LogInfo?.Invoke(
                    $"DAQ 通道 {channel} 内部定时窗口采样完成：" +
                    $"窗口={windowMs / 1000.0:0.000}s，间隔={intervalMs}ms，" +
                    $"点数={values.Length}，缓存校验={bufferedCount}，最小={minimum:0.######}V，" +
                    $"最大={maximum:0.######}V，平均={average:0.######}V，" +
                    $"含批量回读总耗时={elapsed.ElapsedMilliseconds}ms，" +
                    $"原始响应长度={rawResponse.Length}。");
                return average;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                string message =
                    $"DAQ 通道 {channel} {windowMs}ms内部定时窗口采样失败: {ex.Message}";
                LogError?.Invoke(message);
                throw new InvalidOperationException(message, ex);
            }
            finally
            {
                if (scanConfigurationAttempted)
                {
                    try
                    {
                        Daq.RestoreAfterTimedVoltageDcScan();
                    }
                    catch (Exception recoveryEx)
                    {
                        LogError?.Invoke(
                            $"DAQ 通道 {channel} 定时窗口采样后恢复失败: {recoveryEx.Message}");
                    }
                }
            }
        }

        private static async Task<bool> WaitForDaqSampleStartAsync(
            System.Diagnostics.Stopwatch schedule,
            int sampleIndex,
            int intervalMs,
            CancellationToken token)
        {
            if (sampleIndex <= 0 || intervalMs <= 0)
                return true;

            long dueMilliseconds = (long)sampleIndex * intervalMs;
            long remainingMilliseconds = dueMilliseconds - schedule.ElapsedMilliseconds;
            if (remainingMilliseconds <= 0)
                return false;

            await Task.Delay(
                remainingMilliseconds > int.MaxValue
                    ? int.MaxValue
                    : (int)remainingMilliseconds,
                token);
            return true;
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
                string safetyError;
                if (!ValidatePowerRelaySwitch(relayId, isOpen, out safetyError))
                {
                    LogError?.Invoke(safetyError);
                    return false;
                }
                if (IsPowerRelayAlreadyInState(relayId, isOpen))
                {
                    return true;
                }

                // isOpen=true means close/energize in this legacy API. Only closing
                // requires a strict power-off measurement; opening is always allowed.
                if (isOpen && !await VerifyRelayPowerIsOffAsync(relayId))
                {
                    LogError?.Invoke($"继电器 {relayId} 闭合前掉电确认失败。");
                    return false;
                }

                if (SkipComInit)
                {
                    await Task.Delay(50);
                    RecordPowerRelay(relayId, isOpen);
                    LogInfo?.Invoke($"[调试模式] 模拟{(isOpen ? "闭合" : "断开")}继电器: {relayId}");
                    return true;
                }

                int address = GetIntAppSetting("SKRelayAddress", 3);
                if (address != 3)
                {
                    LogError?.Invoke($"功率继电器站位配置错误：期望地址3，当前地址{address}。");
                    return false;
                }
                int baudRate = GetIntAppSetting("SKRelayBaudRate", 38400);
                string comPort = GetAppSetting("SKRelayComPort", ComName.rs485ComName);
                string response = await RelayController.SendCommandAsync(
                    address, relayId, isOpen, 1, baudRate, comPort, null);
                bool ok = !string.IsNullOrWhiteSpace(response) &&
                          response.IndexOf("TIMEOUT", StringComparison.OrdinalIgnoreCase) < 0 &&
                          response.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) < 0 &&
                          response.IndexOf("错误", StringComparison.OrdinalIgnoreCase) < 0 &&
                          response.IndexOf("超时", StringComparison.OrdinalIgnoreCase) < 0;
                if (!ok)
                {
                    LogError?.Invoke($"继电器 {relayId} 控制失败: {response}");
                    return false;
                }
                LogInfo?.Invoke($"功率继电器站3 Y{relayId} -> {(isOpen ? "闭合" : "断开")}");
                RecordPowerRelay(relayId, isOpen);
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
        public async Task<DigitalInputReadResult> ReadDigitalInputsSafeAsync(int address = 0, string comPort = null)
        {
            try
            {
                if (SkipComInit)
                {
                    await Task.Delay(50);
                    if (SimulateDigitalInputCommunicationFailure)
                    {
                        return new DigitalInputReadResult
                        {
                            CommunicationOk = false,
                            States = null,
                            Error = "模拟数字输入通信失联"
                        };
                    }
                    // Simulation returns normalized logical states, not raw module levels.
                    // DI4=0 installed; DI7=1 released; DI8=0 emergency normal.
                    bool[] mockStates = new bool[16];
                    for (int i = 0; i < 16; i++) mockStates[i] = false;
                    mockStates[4] = SimulationBoardType.StartsWith(
                        "MPS",
                        StringComparison.OrdinalIgnoreCase);
                    mockStates[5] = SimulationBoardType.EndsWith(
                        "250",
                        StringComparison.OrdinalIgnoreCase);
                    mockStates[6] = !_mockFixtureDown;
                    mockStates[7] = false;

                    return new DigitalInputReadResult
                    {
                        CommunicationOk = true,
                        States = mockStates
                    };
                }

                // 鐪熷疄璋冪敤
                if (address <= 0)
                    address = GetIntAppSetting("SKDigitalInputAddress", 2);
                if (address != 2)
                {
                    string stationError = $"数字输入模块站位配置错误：期望地址2，当前地址{address}。";
                    LogError?.Invoke(stationError);
                    return new DigitalInputReadResult
                    {
                        CommunicationOk = false,
                        States = null,
                        Error = stationError
                    };
                }
                if (string.IsNullOrWhiteSpace(comPort))
                    comPort = _digitalInputPort ??
                              GetAppSetting("SKDigitalInputPort", ComName.digitalInputComName);
                int baudRate = GetIntAppSetting("SKDigitalInputBaudRate", 38400);
                bool[] rawStates = await DigitalInputController.ReadDigitalInputAsync(
                    address,
                    baudRate,
                    comPort,
                    null);
                if (rawStates == null)
                {
                    LogError?.Invoke("读取数字输入模块失败，返回 null");
                    return new DigitalInputReadResult
                    {
                        CommunicationOk = false,
                        States = null,
                        Error = "数字输入模块无响应或数据无效"
                    };
                }
                int invertMask = GetIntAppSetting("SKDigitalInputInvertMask", 0x03F7);
                bool[] states = NormalizeDigitalInputStates(rawStates, invertMask);
                string stateBits = FormatDigitalInputBits(states);
                if (!string.Equals(
                    stateBits,
                    _lastLoggedDigitalInputBits,
                    StringComparison.Ordinal))
                {
                    _lastLoggedDigitalInputBits = stateBits;
                    LogInfo?.Invoke($"数字输入状态变化 DI16→DI1: {stateBits}");
                }
                return new DigitalInputReadResult
                {
                    CommunicationOk = true,
                    States = states
                };
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"读取数字输入模块发生异常: {ex.Message}");
                return new DigitalInputReadResult
                {
                    CommunicationOk = false,
                    States = null,
                    Error = ex.Message
                };
            }
        }

        public async Task<bool[]> ReadDigitalInputsAsync(int address = 0, string comPort = null)
        {
            DigitalInputReadResult result = await ReadDigitalInputsSafeAsync(address, comPort);
            if (result.CommunicationOk) return result.States;
            bool[] failSafeStates = new bool[16];
            failSafeStates[7] = GetBoolAppSetting("SKEmergencyStopActiveHigh", true);
            return failSafeStates;
        }

        public static bool[] NormalizeDigitalInputStates(bool[] rawStates, int invertMask)
        {
            if (rawStates == null)
                return null;

            bool[] normalized = new bool[rawStates.Length];
            for (int index = 0; index < rawStates.Length; index++)
            {
                bool invert = index < 16 && (invertMask & (1 << index)) != 0;
                normalized[index] = invert ? !rawStates[index] : rawStates[index];
            }
            return normalized;
        }

        private static string FormatDigitalInputBits(bool[] states)
        {
            if (states == null)
                return "<null>";
            return string.Concat(states.Reverse().Select(state => state ? "1" : "0"));
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
                    RecordPowerOutput("ANS", false);
                    RecordPowerOutput("VBUS", false);
                    RecordPowerOutput("SECONDARY", false);
                    RecordPowerOutput("LOAD", false);
                    LogInfo?.Invoke("[调试模式] 模拟关闭所有电源/负载输出。");
                    return true;
                }

                bool allSuccess = true;

                if (AnsPower.IsConnected)
                {
                    try { AnsPower.SetPower(false); RecordPowerOutput("ANS", false); }
                    catch (Exception ex) { allSuccess = false; LogError?.Invoke($"ANS 电源关闭失败: {ex.Message}"); }
                }

                if (HengHuiPower1.IsConnected)
                {
                    try { HengHuiPower1.SetPower(false); RecordPowerOutput("VBUS", false); }
                    catch (Exception ex) { allSuccess = false; LogError?.Invoke($"HengHui1 电源关闭失败: {ex.Message}"); }
                }

                if (HengHuiPower2.IsConnected)
                {
                    try { HengHuiPower2.SetPower(false); RecordPowerOutput("SECONDARY", false); }
                    catch (Exception ex) { allSuccess = false; LogError?.Invoke($"HengHui2 电源关闭失败: {ex.Message}"); }
                }

                if (ElectronicLoad.IsConnected)
                {
                    try { ElectronicLoad.SetInputState(false); RecordPowerOutput("LOAD", false); }
                    catch (Exception ex) { allSuccess = false; LogError?.Invoke($"电子负载关闭失败: {ex.Message}"); }
                }

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

                int timeoutMs = Math.Max(
                    100,
                    GetIntAppSetting("SKMbdCommandTimeoutMs", 700));
                string response = await SendMbdCommandWithRecoveryAsync(command, timeoutMs);
                await Task.Delay(Math.Max(0, waitAfterMs));

                string responseError;
                bool acceptedExitNoOp = MbdResponseParser.IsAcceptedExitNoOpResponse(
                    command,
                    response);
                if (!MbdResponseParser.IsSuccessfulCommandResponse(
                    command,
                    response,
                    out responseError))
                {
                    LogError?.Invoke(
                        $"USB-B/MBD 命令失败: {command}，设备返回={responseError}，原始响应={response}");
                    return false;
                }

                if (acceptedExitNoOp)
                {
                    LogInfo?.Invoke(
                        "USB-B/MBD EXIT 返回 COMMAND ERROR + Ready：设备已处于MBD终端，按成功处理。");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"USB-B/MBD 鍛戒护鍙戦€佸け璐?({command}): {ex.Message}");
                return false;
            }
        }

        public async Task<bool> CheckBcm125ProgrammingInstrumentsAsync()
        {
            try
            {
                if (SkipComInit)
                {
                    await Task.Delay(50);
                    LogInfo?.Invoke(
                        "[调试模式] 模拟检查BCM-125编程所需电源、电子负载和TTL接口；" +
                        "USB-B/MBD将在DUT上电后识别。");
                    return true;
                }

                bool allSuccess = true;
                allSuccess = allSuccess && CheckConnected(
                    HengHuiPower1.IsConnected,
                    "BUS generator / 电源A");
                allSuccess = allSuccess && CheckConnected(
                    HengHuiPower2.IsConnected,
                    "String generator / 电源B");
                allSuccess = allSuccess && CheckConnected(
                    ElectronicLoad.IsConnected,
                    "电子负载");
                if (IsTtlProgrammingRequired)
                {
                    allSuccess = allSuccess && CheckConnected(
                        Flasher.IsConnected,
                        "BCM-125 TTL烧录接口");
                }
                else
                {
                    LogInfo?.Invoke(
                        "BCM-125已配置跳过TTL烧录；本步骤不要求TTL串口在线。");
                }

                LogInfo?.Invoke(
                    "USB-B/MBD不在编程预检中判定；将在RL4上电和6000ms启动等待后自动识别。");
                return allSuccess;
            }
            catch (Exception ex)
            {
                LogError?.Invoke("BCM-125编程仪器检查发生异常: " + ex.Message);
                return false;
            }
        }

        public bool IsTargetBoardCommunicationConnected =>
            SkipComInit || (TargetBoard != null && TargetBoard.IsConnected);

        public async Task<bool> SendMbdCommandOnCurrentConnectionAsync(
            string command,
            int waitAfterMs)
        {
            if (SkipComInit)
                return await SendMbdCommandAsync(command, waitAfterMs);

            if (TargetBoard == null || !TargetBoard.IsConnected)
            {
                LogError?.Invoke(
                    $"MBD有状态命令禁止重新枚举，当前串口已断开：{command}");
                return false;
            }

            int timeoutMs = Math.Max(
                500,
                GetIntAppSetting("SKMbdStatefulCommandTimeoutMs", 2000));
            int retryCount = Math.Max(
                1,
                GetIntAppSetting("SKMbdStatefulRetryCount", 3));
            int retryDelayMs = Math.Max(
                0,
                GetIntAppSetting("SKMbdStatefulRetryDelayMs", 250));
            for (int attempt = 1; attempt <= retryCount; attempt++)
            {
                string response = await TargetBoard.SendCommandAsync(command, timeoutMs);
                string responseError;
                if (MbdResponseParser.IsSuccessfulCommandResponse(
                    command,
                    response,
                    out responseError))
                {
                    await Task.Delay(Math.Max(0, waitAfterMs));
                    if (attempt > 1)
                    {
                        LogInfo?.Invoke(
                            $"MBD有状态命令已在当前连接恢复：{command}，尝试次数={attempt}。");
                    }
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(response))
                {
                    LogError?.Invoke(
                        $"MBD有状态命令失败：{command}，设备返回={responseError}，" +
                        $"原始响应={response}；为保持终端状态，不重新枚举串口。");
                    return false;
                }

                if (attempt < retryCount)
                {
                    LogInfo?.Invoke(
                        $"MBD有状态命令无响应：{command}；保持当前串口和终端上下文，" +
                        $"等待{retryDelayMs}ms后重试（{attempt}/{retryCount}）。");
                    await Task.Delay(retryDelayMs);
                }
            }

            LogError?.Invoke(
                $"MBD有状态命令通信失败：{command}；已在当前连接重试{retryCount}次，" +
                "为保持终端状态，未重新枚举串口。");
            return false;
        }

        public async Task<string> QueryMbdCommandOnCurrentConnectionAsync(
            string command,
            int waitAfterMs)
        {
            if (SkipComInit)
                return await QueryMbdCommandAsync(command, waitAfterMs);

            if (TargetBoard == null || !TargetBoard.IsConnected)
            {
                throw new InvalidOperationException(
                    "MBD有状态查询禁止重新枚举，当前串口已断开：" + command);
            }

            int timeoutMs = Math.Max(
                500,
                GetIntAppSetting("SKMbdStatefulCommandTimeoutMs", 2000));
            int retryCount = Math.Max(
                1,
                GetIntAppSetting("SKMbdStatefulRetryCount", 3));
            int retryDelayMs = Math.Max(
                0,
                GetIntAppSetting("SKMbdStatefulRetryDelayMs", 250));
            for (int attempt = 1; attempt <= retryCount; attempt++)
            {
                string response = await TargetBoard.SendCommandAsync(command, timeoutMs);
                if (!string.IsNullOrWhiteSpace(response))
                {
                    await Task.Delay(Math.Max(0, waitAfterMs));
                    if (attempt > 1)
                    {
                        LogInfo?.Invoke(
                            $"MBD有状态查询已在当前连接恢复：{command}，尝试次数={attempt}。");
                    }
                    return response;
                }

                if (attempt < retryCount)
                {
                    LogInfo?.Invoke(
                        $"MBD有状态查询无响应：{command}；保持当前串口和终端上下文，" +
                        $"等待{retryDelayMs}ms后重试（{attempt}/{retryCount}）。");
                    await Task.Delay(retryDelayMs);
                }
            }

            throw new InvalidOperationException(
                "MBD有状态查询通信失败：" + command +
                $"；已在当前连接重试{retryCount}次，为保持终端状态，未重新枚举串口。");
        }

        public async Task<bool> SendMbdCleanupCommandsOnCurrentConnectionAsync(string commands)
        {
            if (SkipComInit)
            {
                foreach (string command in (commands ?? string.Empty)
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    ApplyMockMbdCommandState(command.Trim());
                }
                LogInfo?.Invoke("[调试模式] 模拟在当前MBD连接上发送安全收尾命令。");
                return true;
            }

            if (TargetBoard == null || !TargetBoard.IsConnected)
            {
                LogInfo?.Invoke("MBD当前未连接，安全收尾不重新枚举串口。");
                return true;
            }

            int timeoutMs = Math.Max(
                100,
                GetIntAppSetting("SKMbdCommandTimeoutMs", 700));
            foreach (string item in (commands ?? string.Empty)
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string command = item.Trim();
                string response = await TargetBoard.SendCommandAsync(command, timeoutMs);
                string responseError;
                if (!MbdResponseParser.IsSuccessfulCommandResponse(
                    command,
                    response,
                    out responseError))
                {
                    LogError?.Invoke(
                        $"MBD安全收尾命令未确认：{command}，设备返回={responseError}；" +
                        "不重新枚举串口，继续硬件断电收尾。");
                    TargetBoard.Disconnect();
                    return false;
                }
            }

            LogInfo?.Invoke("已通过当前MBD连接发送主开关/加热器安全关闭命令。");
            return true;
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

                int timeoutMs = Math.Max(
                    100,
                    GetIntAppSetting("SKMbdCommandTimeoutMs", 700));
                string response = await SendMbdCommandWithRecoveryAsync(command, timeoutMs);
                await Task.Delay(Math.Max(0, waitAfterMs));
                return response;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"USB-B/MBD 查询失败 ({command}): {ex.Message}");
                return null;
            }
        }

        public async Task<bool> CheckMbdCommunicationAsync()
        {
            if (SkipComInit)
            {
                LogInfo?.Invoke("[调试模式] 模拟 USB-B/MBD 串口通信已打开");
                return true;
            }

            int probeTimeoutMs = Math.Max(
                500,
                GetIntAppSetting("SKMbdProbeTimeoutMs", 2000));
            string response = await SendMbdCommandWithRecoveryAsync(string.Empty, probeTimeoutMs);
            bool ok = !string.IsNullOrWhiteSpace(response);
            LogInfo?.Invoke(ok
                ? $"USB-B/MBD 串口通信正常：{ComName.testComName}"
                : "USB-B/MBD 串口识别或连接失败");
            return ok;
        }

        public void PrepareMpsUsbBManualConnection(string boardType)
        {
            if (SkipComInit)
                return;

            TargetBoard?.Disconnect();
            _targetBoardPortCandidate = null;
            ComName.testComName = null;
            LogInfo?.Invoke(
                $"{boardType}等待人工插入USB-B；已清除固定/历史MBD串口候选。" +
                "DUT上电启动后将从现有未占用COM口中自动识别MBD。");
        }

        public void PrepareMps250UsbBManualConnection()
        {
            PrepareMpsUsbBManualConnection("MPS-250");
        }

        public bool ResetMbdConnectionBeforeDutPowerOn()
        {
            if (SkipComInit)
                return true;

            try
            {
                TargetBoard?.Disconnect();
                _targetBoardPortCandidate = null;
                ComName.testComName = null;
                LogInfo?.Invoke(
                    "DUT重新上电前已释放旧MBD串口句柄；上电后将重新枚举并严格验证终端响应。");
                return true;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"DUT重新上电前释放旧MBD串口失败：{ex.Message}");
                return false;
            }
        }

        public async Task<bool> WaitForMbdReadyAfterPowerCycleAsync(
            int timeoutMs,
            CancellationToken token)
        {
            if (SkipComInit)
                return true;

            int effectiveTimeoutMs = Math.Max(1000, timeoutMs);
            int retryIntervalMs = Math.Max(
                100,
                GetIntAppSetting("SKMbdRecoveryRetryMs", 750));
            int commandTimeoutMs = Math.Max(
                500,
                GetIntAppSetting("SKMbdRecoveryCommandTimeoutMs", 2000));
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(effectiveTimeoutMs);
            int attempt = 0;

            while (DateTime.UtcNow <= deadline)
            {
                token.ThrowIfCancellationRequested();
                attempt++;
                if (attempt > 1)
                    await Task.Delay(retryIntervalMs, token);

                await CloseMbdPortAndWaitForReleaseAsync(token);
                LogInfo?.Invoke(
                    $"DUT电源循环后MBD就绪检查 {attempt}：重新枚举并验证终端响应。");
                if (!await EnsureMbdConnectedAsync())
                    continue;

                string response = await TargetBoard.SendCommandAsync(
                    string.Empty,
                    commandTimeoutMs);
                if (SKSerialDeviceDiscovery.IsMbdIdentificationResponse(response))
                {
                    LogInfo?.Invoke(
                        $"DUT电源循环后MBD已就绪：{ComName.testComName}，尝试次数={attempt}。");
                    return true;
                }

                LogInfo?.Invoke(
                    "DUT电源循环后MBD端口已打开，但终端响应尚未完整，继续等待。");
            }

            TargetBoard?.Disconnect();
            LogError?.Invoke(
                $"DUT电源循环后MBD在 {effectiveTimeoutMs} ms 内未恢复就绪。");
            return false;
        }

        private async Task<bool> EnsureMbdConnectedAsync()
        {
            if (TargetBoard != null && TargetBoard.IsConnected)
                return true;

            bool isManualMpsUsbB =
                string.Equals(SimulationBoardType, "MPS-125", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(SimulationBoardType, "MPS-250", StringComparison.OrdinalIgnoreCase);
            int targetBoardBaudRate = string.Equals(
                SimulationBoardType,
                "MPS-250",
                StringComparison.OrdinalIgnoreCase)
                ? GetIntAppSetting("SKMPS250TargetBoardBaudRate", 115200)
                : GetIntAppSetting("SKTargetBoardBaudRate", 9600);
            string detectedPort = await SKSerialDeviceDiscovery.DiscoverMbdPortAsync(
                message => LogInfo?.Invoke(message),
                isManualMpsUsbB
                    ? null
                    : _lastSuccessfulTargetBoardPort ?? _targetBoardPortCandidate,
                ignoreConfiguredPort: isManualMpsUsbB,
                allowUnverifiedFallback: false,
                baudRate: targetBoardBaudRate);
            if (string.IsNullOrWhiteSpace(detectedPort))
            {
                LogError?.Invoke(
                    "DUT上电后未识别到MBD UART；请检查ANS输出、RL4、USB-B和启动等待时间。");
                return false;
            }

            try
            {
                int settleMs = GetIntAppSetting("SKMbdOpenSettleMs", 500);
                TargetBoard.Connect(detectedPort, targetBoardBaudRate, settleMs);
                _targetBoardPortCandidate = detectedPort;
                _lastSuccessfulTargetBoardPort = detectedPort;
                ComName.testComName = detectedPort;
                return true;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"连接MBD UART失败 ({detectedPort})：{ex.Message}");
                return false;
            }
        }

        private async Task<string> SendMbdCommandWithRecoveryAsync(
            string command,
            int normalTimeoutMs)
        {
            if (await EnsureMbdConnectedAsync())
            {
                string response = await TargetBoard.SendCommandAsync(command, normalTimeoutMs);
                if (!string.IsNullOrWhiteSpace(response))
                    return response;

                LogInfo?.Invoke(
                    $"USB-B/MBD 首次命令无响应，准备释放旧句柄并重新枚举：{command}");
            }

            await CloseMbdPortAndWaitForReleaseAsync(CancellationToken.None);
            int recoveryTimeoutMs = Math.Max(
                1000,
                GetIntAppSetting("SKMbdRecoveryTimeoutMs", 15000));
            int retryIntervalMs = Math.Max(
                100,
                GetIntAppSetting("SKMbdRecoveryRetryMs", 750));
            int recoveryCommandTimeoutMs = Math.Max(
                normalTimeoutMs,
                GetIntAppSetting("SKMbdRecoveryCommandTimeoutMs", 2000));
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(recoveryTimeoutMs);
            int attempt = 0;

            while (DateTime.UtcNow <= deadline)
            {
                attempt++;
                if (attempt > 1)
                    await Task.Delay(retryIntervalMs);

                await WaitForKnownMbdPortToReenumerateAsync(deadline);

                LogInfo?.Invoke(
                    $"USB-B/MBD 恢复尝试 {attempt}：重新枚举COM并发送 {command}");
                if (!await EnsureMbdConnectedAsync())
                    continue;

                string response = await TargetBoard.SendCommandAsync(
                    command,
                    recoveryCommandTimeoutMs);
                if (!string.IsNullOrWhiteSpace(response))
                {
                    LogInfo?.Invoke(
                        $"USB-B/MBD 已恢复：{ComName.testComName}，尝试次数={attempt}");
                    return response;
                }

                await CloseMbdPortAndWaitForReleaseAsync(CancellationToken.None);
            }

            LogError?.Invoke(
                $"USB-B/MBD 在 {recoveryTimeoutMs} ms 内未恢复：{command}");
            return null;
        }

        private async Task CloseMbdPortAndWaitForReleaseAsync(CancellationToken token)
        {
            TargetBoard?.Disconnect();
            int releaseWaitMs = Math.Max(
                0,
                GetIntAppSetting("SKMbdPortReleaseWaitMs", 300));
            if (releaseWaitMs <= 0)
                return;

            LogInfo?.Invoke(
                $"已确认旧MBD串口关闭，等待 {releaseWaitMs} ms 让Windows释放端口句柄。");
            await Task.Delay(releaseWaitMs, token);
        }

        private async Task WaitForKnownMbdPortToReenumerateAsync(DateTime recoveryDeadline)
        {
            string preferredPort = _lastSuccessfulTargetBoardPort;
            if (string.IsNullOrWhiteSpace(preferredPort))
                return;

            string[] available = SerialPort.GetPortNames();
            if (available.Contains(preferredPort, StringComparer.OrdinalIgnoreCase))
                return;

            int maximumWaitMs = Math.Max(
                500,
                GetIntAppSetting("SKMbdEnumerationWaitMs", 4000));
            DateTime waitDeadline = DateTime.UtcNow.AddMilliseconds(maximumWaitMs);
            if (waitDeadline > recoveryDeadline)
                waitDeadline = recoveryDeadline;

            LogInfo?.Invoke(
                $"历史MBD端口 {preferredPort} 已从Windows枚举中消失，等待USB设备稳定重新出现。");
            int consecutiveSamples = 0;
            while (DateTime.UtcNow <= waitDeadline)
            {
                await Task.Delay(250);
                available = SerialPort.GetPortNames();
                if (available.Contains(preferredPort, StringComparer.OrdinalIgnoreCase))
                {
                    consecutiveSamples++;
                    if (consecutiveSamples >= 2)
                    {
                        LogInfo?.Invoke($"历史MBD端口 {preferredPort} 已稳定重新枚举。");
                        return;
                    }
                }
                else
                {
                    consecutiveSamples = 0;
                }
            }

            LogInfo?.Invoke(
                $"等待历史MBD端口 {preferredPort} 重新枚举超时，继续扫描其他候选端口。");
        }

        public string GetBcm125ExpectedFirmwareVersion()
        {
            return GetAppSetting("SKBCM125ExpectedFirmwareVersion", "BCM_125_V8.HEX");
        }

        public async Task<bool> SetVbusGeneratorAsync(float voltage, float current, float ovp, bool outputOn)
        {
            try
            {
                string safetyError;
                if (outputOn && !ValidatePowerSourceEnable("VBUS", voltage, out safetyError))
                {
                    LogError?.Invoke(safetyError);
                    return false;
                }
                if (SkipComInit)
                {
                    await Task.Delay(50);
                    RecordPowerOutput("VBUS", outputOn);
                    _mockVbusVoltage = voltage;
                    _mockVbusCurrentLimit = current;
                    if (Math.Abs(voltage) < 0.001 && !outputOn)
                        _mockR68ReadCount = 0;
                    LogInfo?.Invoke($"[调试模式] 模拟设置 VBUS 发生器: {voltage}V/{current}A, OVP={ovp}V, 输出{(outputOn ? "ON" : "OFF")}");
                    return true;
                }

                HengHuiPower1.SetVoltageCurrent(voltage, current);
                HengHuiPower1.SetOvp(ovp);
                HengHuiPower1.SetPower(outputOn);
                RecordPowerOutput("VBUS", outputOn);
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
                string safetyError;
                if (outputOn && !ValidatePowerSourceEnable("SECONDARY", voltage, out safetyError))
                {
                    LogError?.Invoke(safetyError);
                    return false;
                }
                if (SkipComInit)
                {
                    await Task.Delay(50);
                    RecordPowerOutput("SECONDARY", outputOn);
                    _mockSecondaryVoltage = voltage;
                    _mockSecondaryCurrentLimit = current;
                    _mockStringCurrent = outputOn ? current : 0;
                    LogInfo?.Invoke($"[调试模式] 模拟设置 String 发生器: {voltage}V/{current}A, OVP={ovp}V, 输出{(outputOn ? "ON" : "OFF")}");
                    return true;
                }

                HengHuiPower2.SetVoltageCurrent(voltage, current);
                HengHuiPower2.SetOvp(ovp);
                HengHuiPower2.SetPower(outputOn);
                RecordPowerOutput("SECONDARY", outputOn);
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
                    RecordPowerOutput("LOAD", false);
                    LogInfo?.Invoke("[调试模式] 模拟复位电子负载 CV 0V/0A/0W");
                    return true;
                }

                ElectronicLoad.SetInputState(false);
                ElectronicLoad.ClearInputProtection();
                ElectronicLoad.SetCurrent(0);
                ElectronicLoad.SetMode("CV");
                ElectronicLoad.SetVoltage(0);
                RecordPowerOutput("LOAD", false);
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
                string safetyError;
                if (inputOn && !ValidateEmergencyAllowsEnergizing("开启电子负载输入", out safetyError))
                {
                    LogError?.Invoke(safetyError);
                    return false;
                }
                if (SkipComInit)
                {
                    await Task.Delay(50);
                    RecordPowerOutput("LOAD", inputOn);
                    LogInfo?.Invoke($"[Debug] Mock electronic load CV {voltage}V/{current}A/{power}W, input {(inputOn ? "ON" : "OFF")}");
                    return true;
                }

                bool loadWasAlreadyOn;
                lock (_powerSafetySync)
                    loadWasAlreadyOn = _electronicLoadOn;

                // POW is a constant-power operating setpoint on this load and
                // changes the active mode to CW. Configure limits first, then
                // select CV last so the requested voltage remains authoritative.
                float effectiveCurrent = current;
                if (voltage > 0 && current > 0 && power > 0 && voltage * current > power)
                {
                    double derated = Math.Floor((power / voltage) * 0.95 * 10.0) / 10.0;
                    effectiveCurrent = (float)Math.Max(0.1, Math.Min(current, derated));
                    LogInfo?.Invoke(
                        $"电子负载CV电流设定由{current:0.###}A降额为{effectiveCurrent:0.###}A，" +
                        $"避免{voltage:0.###}V×{current:0.###}A超过{power:0.###}W功率上限。");
                }

                if (inputOn && loadWasAlreadyOn)
                {
                    // Keep the load conducting while moving the CV operating point.
                    // Toggling INP and rewriting protection values here can latch the
                    // KUNKIN 7303 over-power alarm during the 250 V -> 255 V transition.
                    ElectronicLoad.SetVoltage(voltage);
                    LogInfo?.Invoke(
                        $"电子负载保持输入ON，仅更新CV电压为{voltage:0.###}V；" +
                        "不重新写入电流/功率保护，也不在运行中切断负载。");
                }
                else
                {
                    ElectronicLoad.SetInputState(false);
                    RecordPowerOutput("LOAD", false);
                    ElectronicLoad.ClearInputProtection();
                    ElectronicLoad.SetCurrent(effectiveCurrent);
                    if (effectiveCurrent > 0)
                        ElectronicLoad.SetCurrentProtection(effectiveCurrent);
                    if (power > 0)
                        ElectronicLoad.SetPowerProtection(power);
                    ElectronicLoad.SetMode("CV");
                    ElectronicLoad.SetVoltage(voltage);
                    if (inputOn)
                        ElectronicLoad.ClearInputProtection();
                    ElectronicLoad.SetInputState(inputOn);
                    RecordPowerOutput("LOAD", inputOn);
                }

                int readbackDelayMs = Math.Max(
                    0,
                    GetIntAppSetting("SKElectronicLoadReadbackDelayMs", 300));
                if (readbackDelayMs > 0)
                    await Task.Delay(readbackDelayMs);

                string actualMode = ElectronicLoad.GetMode();
                bool actualInputOn = ElectronicLoad.GetInputState();
                RecordPowerOutput("LOAD", actualInputOn);
                float measuredVoltage = ElectronicLoad.MeasureVoltage();
                float measuredCurrent = ElectronicLoad.MeasureCurrent();
                float measuredPower = ElectronicLoad.MeasurePower();
                if (float.IsNaN(measuredVoltage) || float.IsInfinity(measuredVoltage) ||
                    float.IsNaN(measuredCurrent) || float.IsInfinity(measuredCurrent) ||
                    float.IsNaN(measuredPower) || float.IsInfinity(measuredPower))
                {
                    LogError?.Invoke("电子负载回读包含非有限值，设置结果无效。");
                    return false;
                }

                LogInfo?.Invoke(
                    $"电子负载真实状态回读：模式={actualMode}，输入={(actualInputOn ? "ON" : "OFF")}，" +
                    $"设定={voltage:0.###}V/{effectiveCurrent:0.###}A，保护={power:0.###}W，" +
                    $"实测={measuredVoltage:0.###}V/{measuredCurrent:0.###}A/{measuredPower:0.###}W");

                if (!string.Equals(actualMode, "CV", StringComparison.OrdinalIgnoreCase))
                {
                    LogError?.Invoke($"电子负载模式回读失败：要求CV，设备实际为{actualMode}。");
                    return false;
                }
                if (actualInputOn != inputOn)
                {
                    LogError?.Invoke(
                        $"电子负载输入状态回读失败：要求{(inputOn ? "ON" : "OFF")}，" +
                        $"设备实际为{(actualInputOn ? "ON" : "OFF")}。");
                    return false;
                }

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
                string relay = GetConfiguredDaqRelayList(
                    "SKBCM125RlCortoDaqRelay",
                    Bcm125StationMap.ShortProtectionSelectorDaqRelay);
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
                return _mockR10ReadCount % 2 == 1
                    ? "010 = 5.264\r\nReady.\r\n"
                    : "010 = 0.953\r\nReady.\r\n";
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
                    int simulatedRelayCount = GetIntAppSetting("SKRelayCount", 16);
                    for (int relayId = 1; relayId <= simulatedRelayCount; relayId++)
                        RecordPowerRelay(relayId, false);
                    LogInfo?.Invoke("[调试模式] 模拟断开测试继电器(保留工装 Y1/Y2 状态)");
                    return true;
                }

                int relayCount = GetIntAppSetting("SKRelayCount", 16);
                bool powerConfirmedOff = await VerifyPowerDomainsOffAsync(
                    "ANS", "VBUS", "SECONDARY", "LOAD");
                if (!powerConfirmedOff)
                {
                    LogInfo?.Invoke(
                        "警告：集中掉电确认未通过；仍继续断开全部测试继电器以建立安全隔离，" +
                        "但在重新确认掉电前禁止任何继电器闭合或仪器输出开启。");
                }

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
                string safetyError;
                if (close && !ValidateEmergencyAllowsEnergizing("闭合DAQ测试继电器 " + channelList, out safetyError))
                {
                    LogError?.Invoke(safetyError);
                    return false;
                }
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
                string channelA = GetAppSetting("SKPlacementRelayA", Bcm125StationMap.PlacementRelayA);
                string channelB = GetAppSetting("SKPlacementRelayB", Bcm125StationMap.PlacementRelayB);

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

