using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TestPlatform
{
    public sealed class SKSerialDeviceDiscoveryResult
    {
        public string AnsPowerPort { get; internal set; }
        public string AinuoPowerAPort { get; internal set; }
        public string HengHuiPowerBPort { get; internal set; }
        public string ElectronicLoadPort { get; internal set; }
        public string DaqPort { get; internal set; }
        public string DigitalInputPort { get; internal set; }
        public string FixtureRelayPort { get; internal set; }
        public string Relay16Port { get; internal set; }
        public string TargetBoardPort { get; internal set; }
        public string TtlPort { get; internal set; }
        public bool TtlRequired { get; internal set; } = true;
        public List<string> Errors { get; } = new List<string>();

        public bool IsComplete =>
            !string.IsNullOrWhiteSpace(AnsPowerPort) &&
            !string.IsNullOrWhiteSpace(AinuoPowerAPort) &&
            !string.IsNullOrWhiteSpace(HengHuiPowerBPort) &&
            !string.IsNullOrWhiteSpace(ElectronicLoadPort) &&
            !string.IsNullOrWhiteSpace(DaqPort) &&
            !string.IsNullOrWhiteSpace(DigitalInputPort) &&
            !string.IsNullOrWhiteSpace(FixtureRelayPort) &&
            !string.IsNullOrWhiteSpace(Relay16Port) &&
            (!TtlRequired || !string.IsNullOrWhiteSpace(TtlPort));

        public bool IsCompleteFor(ISet<string> requiredPorts)
        {
            return !GetMissingDeviceNames(requiredPorts).Any();
        }

        public IEnumerable<string> GetMissingDeviceNames(ISet<string> requiredPorts)
        {
            if (requiredPorts == null || requiredPorts.Count == 0)
                return GetMissingDeviceNames();

            var missing = new List<string>();
            if (requiredPorts.Contains("ansPowerComName") && string.IsNullOrWhiteSpace(AnsPowerPort)) missing.Add("ANS电源");
            if ((requiredPorts.Contains("ainuoPowerAComName") ||
                 requiredPorts.Contains("hengHuiPowerAComName")) &&
                string.IsNullOrWhiteSpace(AinuoPowerAPort)) missing.Add("艾诺AN50300-10电源A");
            if (requiredPorts.Contains("hengHuiPowerBComName") && string.IsNullOrWhiteSpace(HengHuiPowerBPort)) missing.Add("恒辉电源B");
            if (requiredPorts.Contains("electronicLoadComName") && string.IsNullOrWhiteSpace(ElectronicLoadPort)) missing.Add("KUNKIN 7303电子负载");
            if (requiredPorts.Contains("daqComName") && string.IsNullOrWhiteSpace(DaqPort)) missing.Add("GWInstek DAQ-9600");
            if (requiredPorts.Contains("digitalInputComName") && string.IsNullOrWhiteSpace(DigitalInputPort)) missing.Add("16路数字量输入模块");
            if (requiredPorts.Contains("fixtureRelayComName") && string.IsNullOrWhiteSpace(FixtureRelayPort)) missing.Add("工装继电器模块");
            if (requiredPorts.Contains("relay16ComName") && string.IsNullOrWhiteSpace(Relay16Port)) missing.Add("16路继电器模块");
            if (TtlRequired && requiredPorts.Contains("uartComName") && string.IsNullOrWhiteSpace(TtlPort)) missing.Add("独立TTL烧录口");
            return missing;
        }

        public IEnumerable<string> GetMissingDeviceNames()
        {
            if (string.IsNullOrWhiteSpace(AnsPowerPort)) yield return "ANS电源";
            if (string.IsNullOrWhiteSpace(AinuoPowerAPort)) yield return "艾诺AN50300-10电源A";
            if (string.IsNullOrWhiteSpace(HengHuiPowerBPort)) yield return "恒辉电源B";
            if (string.IsNullOrWhiteSpace(ElectronicLoadPort)) yield return "KUNKIN 7303电子负载";
            if (string.IsNullOrWhiteSpace(DaqPort)) yield return "GWInstek DAQ-9600";
            if (string.IsNullOrWhiteSpace(DigitalInputPort)) yield return "16路数字量输入模块(站号02)";
            if (string.IsNullOrWhiteSpace(FixtureRelayPort)) yield return "工装继电器模块(站号01)";
            if (string.IsNullOrWhiteSpace(Relay16Port)) yield return "16路继电器模块(站号03)";
            if (TtlRequired && string.IsNullOrWhiteSpace(TtlPort)) yield return "独立TTL烧录口";
        }
    }

    /// <summary>
    /// Discovers the fixed SK station instruments without relying on Windows COM numbers.
    /// Probes are read-only: SCPI identity/setpoint queries or Modbus register reads.
    /// </summary>
    public static class SKSerialDeviceDiscovery
    {
        private static readonly Regex MbdCommandErrorRegex = new Regex(
            @"(?:\b(?:COMMAND|CMD)\b\s*[:\-]?\s*\b(?:ERROR|ERR)\b)|" +
            @"(?:\b(?:INVALID|UNKNOWN|UNRECOGNIZED)\b\s+\b(?:COMMAND|CMD)\b)|" +
            @"(?:\bSYNTAX\b\s*[:\-]?\s*\b(?:ERROR|ERR)\b)",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant |
            RegexOptions.IgnoreCase);

        private sealed class PortProbe
        {
            public string PortName { get; set; }
            public string Kind { get; set; }
            public string Identity { get; set; }
            public string UsbSerial { get; set; }
            public bool IsFixtureRelay { get; set; }
            public bool IsRelay16 { get; set; }
        }

        /// <summary>
        /// Resolves stable USB identities to this PC's current COM ports, then verifies
        /// instrument protocol responses. A full-port scan is used only when needed.
        /// </summary>
        public static async Task<SKSerialDeviceDiscoveryResult> ResolveConfiguredOrDiscoverAsync(
            Action<string> logAction = null,
            ISet<string> requiredPorts = null)
        {
            bool requireTtl = GetBoolSetting("SKRequireTtl", true);
            SKSerialDeviceDiscoveryResult configured = CreateConfiguredResult(requireTtl);
            PreferAinuoPortByUsbSerial(configured, logAction);
            PreferStationPortsByUsbSerial(configured, logAction);
            configured.TtlPort = ResolveTtlPortByHardwareId(configured.TtlPort, logAction);
            RemoveUnavailableConfiguredPorts(configured, logAction);
            LogConfiguredPorts(configured, logAction);

            string[] availablePorts = SerialPort.GetPortNames();
            string[] configuredActivePorts = new[]
            {
                configured.AnsPowerPort,
                configured.AinuoPowerAPort,
                configured.HengHuiPowerBPort,
                configured.ElectronicLoadPort,
                configured.DaqPort,
                configured.DigitalInputPort,
                configured.FixtureRelayPort,
                configured.Relay16Port
            }
                .Where(port => !string.IsNullOrWhiteSpace(port))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            logAction?.Invoke(
                $"先校验USB身份定位的设备端口：{(configuredActivePorts.Length == 0 ? "无" : string.Join(", ", configuredActivePorts))}");
            PortProbe[] configuredProbes = await Task.WhenAll(
                configuredActivePorts.Select(port =>
                    Task.Run(() => ProbePort(port, logAction))));
            SKSerialDeviceDiscoveryResult verifiedConfigured = BuildResultFromProbes(
                configuredProbes,
                availablePorts,
                configured.TtlPort,
                requireTtl,
                logAction);
            if (verifiedConfigured.IsCompleteFor(requiredPorts))
            {
                logAction?.Invoke("USB身份与仪器协议校验全部通过，直接使用，不执行全端口扫描。");
                ApplyToCommunicationState(verifiedConfigured);
                return verifiedConfigured;
            }

            logAction?.Invoke(
                "USB身份定位端口未通过全部必需设备校验，开始全端口扫描；" +
                "扫描识别结果将覆盖对应默认值。");
            SKSerialDeviceDiscoveryResult discovered = await DiscoverAsync(logAction);
            ApplyConfiguredFallbackPorts(configured, discovered);
            VerifyStationUsbSerialBindings(discovered, logAction);
            ApplyToCommunicationState(discovered);
            return discovered;
        }

        private static void RemoveUnavailableConfiguredPorts(
            SKSerialDeviceDiscoveryResult result,
            Action<string> logAction)
        {
            var available = new HashSet<string>(
                SerialPort.GetPortNames(),
                StringComparer.OrdinalIgnoreCase);
            Action<string, Func<string>, Action> validate = (name, read, clear) =>
            {
                string port = read();
                if (!string.IsNullOrWhiteSpace(port) && !available.Contains(port))
                {
                    logAction?.Invoke($"预设串口不可用：{name}={port}，改为自动扫描。");
                    clear();
                }
            };
            validate("ANS电源", () => result.AnsPowerPort, () => result.AnsPowerPort = null);
            validate("艾诺AN50300-10电源A", () => result.AinuoPowerAPort, () => result.AinuoPowerAPort = null);
            validate("恒辉电源B", () => result.HengHuiPowerBPort, () => result.HengHuiPowerBPort = null);
            validate("电子负载", () => result.ElectronicLoadPort, () => result.ElectronicLoadPort = null);
            validate("DAQ", () => result.DaqPort, () => result.DaqPort = null);
            validate("数字输入", () => result.DigitalInputPort, () => result.DigitalInputPort = null);
            validate("工装继电器", () => result.FixtureRelayPort, () => result.FixtureRelayPort = null);
            validate("16路继电器", () => result.Relay16Port, () => result.Relay16Port = null);
            validate("MBD UART", () => result.TargetBoardPort, () => result.TargetBoardPort = null);
            validate("TTL", () => result.TtlPort, () => result.TtlPort = null);
        }

        private static SKSerialDeviceDiscoveryResult CreateConfiguredResult(bool requireTtl)
        {
            return new SKSerialDeviceDiscoveryResult
            {
                AnsPowerPort = NormalizePort(GetSetting("AnsPort", null)),
                AinuoPowerAPort = NormalizePort(GetSetting("AinuoPowerAPort", null)),
                HengHuiPowerBPort = NormalizePort(GetSetting("HengHui2Port", null)),
                ElectronicLoadPort = NormalizePort(GetSetting("LoadPort", null)),
                DaqPort = NormalizePort(GetSetting("DaqPort", null)),
                DigitalInputPort = NormalizePort(GetSetting("SKDigitalInputPort", null)),
                FixtureRelayPort = NormalizePort(GetSetting("SKFixtureRelayComPort", null)),
                Relay16Port = NormalizePort(GetSetting("SKRelayComPort", null)),
                TargetBoardPort = NormalizePort(GetSetting("TargetBoardPort", null)),
                TtlPort = NormalizePort(GetSetting("TtlPort", null)),
                TtlRequired = requireTtl
            };
        }

        private static string NormalizePort(string portName)
        {
            return string.IsNullOrWhiteSpace(portName) ? null : portName.Trim();
        }

        private static void PreferAinuoPortByUsbSerial(
            SKSerialDeviceDiscoveryResult result,
            Action<string> logAction)
        {
            string expectedSerial = GetSetting("AinuoPowerAUsbSerial", null);
            if (string.IsNullOrWhiteSpace(expectedSerial))
                return;

            string matchedPort = SerialPort.GetPortNames()
                .FirstOrDefault(portName => string.Equals(
                    TryGetFtdiSerial(portName),
                    expectedSerial,
                    StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(matchedPort))
            {
                logAction?.Invoke(
                    $"未找到电源A绑定的FTDI序列号 {expectedSerial}，" +
                    $"先校验配置端口 {result.AinuoPowerAPort ?? "未配置"}。");
                return;
            }

            if (!string.Equals(
                result.AinuoPowerAPort,
                matchedPort,
                StringComparison.OrdinalIgnoreCase))
            {
                logAction?.Invoke(
                    $"电源A按FTDI序列号 {expectedSerial} 映射到 {matchedPort}，" +
                    $"替代配置端口 {result.AinuoPowerAPort ?? "未配置"}。");
            }
            else
            {
                logAction?.Invoke(
                    $"电源A FTDI序列号绑定确认：{expectedSerial} = {matchedPort}。");
            }
            result.AinuoPowerAPort = matchedPort;
        }

        private static void PreferStationPortsByUsbSerial(
            SKSerialDeviceDiscoveryResult result,
            Action<string> logAction)
        {
            PreferPortByUsbSerial("AnsPowerUsbSerial", "ANS电源", value => result.AnsPowerPort = value, logAction);
            PreferPortByUsbSerial("HengHuiPowerBUsbSerial", "恒辉电源B", value => result.HengHuiPowerBPort = value, logAction);
            PreferPortByUsbSerial("ElectronicLoadUsbSerial", "电子负载", value => result.ElectronicLoadPort = value, logAction);
            PreferPortByUsbSerial("DaqUsbSerial", "DAQ", value => result.DaqPort = value, logAction);
            PreferPortByUsbSerial("SKRelayBusUsbSerial", "数字输入/继电器总线", value =>
            {
                result.DigitalInputPort = value;
                result.FixtureRelayPort = value;
                result.Relay16Port = value;
            }, logAction);
        }

        private static void PreferPortByUsbSerial(
            string settingKey,
            string displayName,
            Action<string> assign,
            Action<string> logAction)
        {
            string expected = GetSetting(settingKey, null);
            if (string.IsNullOrWhiteSpace(expected))
                return;

            string[] matches = SerialPort.GetPortNames()
                .Where(port => string.Equals(TryGetUsbSerial(port), expected, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length != 1)
            {
                assign(null);
                logAction?.Invoke($"{displayName}的USB序列号 {expected} 未唯一匹配串口，稍后按协议扫描确认。");
                return;
            }
            assign(matches[0]);
            logAction?.Invoke($"{displayName}按USB序列号 {expected} 定位到 {matches[0]}。");
        }

        private static void VerifyStationUsbSerialBindings(
            SKSerialDeviceDiscoveryResult result,
            Action<string> logAction)
        {
            VerifyUsbSerialBinding(result, "AnsPowerUsbSerial", "ANS电源", () => result.AnsPowerPort,
                value => result.AnsPowerPort = value, logAction);
            VerifyUsbSerialBinding(result, "AinuoPowerAUsbSerial", "艾诺电源A", () => result.AinuoPowerAPort,
                value => result.AinuoPowerAPort = value, logAction);
            VerifyUsbSerialBinding(result, "HengHuiPowerBUsbSerial", "恒辉电源B", () => result.HengHuiPowerBPort,
                value => result.HengHuiPowerBPort = value, logAction);
            VerifyUsbSerialBinding(result, "ElectronicLoadUsbSerial", "电子负载", () => result.ElectronicLoadPort,
                value => result.ElectronicLoadPort = value, logAction);
            VerifyUsbSerialBinding(result, "DaqUsbSerial", "DAQ", () => result.DaqPort,
                value => result.DaqPort = value, logAction);
            VerifyUsbSerialBinding(result, "SKRelayBusUsbSerial", "数字输入", () => result.DigitalInputPort,
                value => result.DigitalInputPort = value, logAction);
            VerifyUsbSerialBinding(result, "SKRelayBusUsbSerial", "工装继电器", () => result.FixtureRelayPort,
                value => result.FixtureRelayPort = value, logAction);
            VerifyUsbSerialBinding(result, "SKRelayBusUsbSerial", "16路继电器", () => result.Relay16Port,
                value => result.Relay16Port = value, logAction);
        }

        private static void VerifyUsbSerialBinding(
            SKSerialDeviceDiscoveryResult result,
            string settingKey,
            string displayName,
            Func<string> selectedPort,
            Action<string> clearPort,
            Action<string> logAction)
        {
            string expected = GetSetting(settingKey, null);
            if (string.IsNullOrWhiteSpace(expected))
                return;

            string actual = selectedPort();
            if (!string.IsNullOrWhiteSpace(actual) &&
                string.Equals(TryGetUsbSerial(actual), expected, StringComparison.OrdinalIgnoreCase))
            {
                logAction?.Invoke($"{displayName}协议及USB序列号双重确认：{actual} / {expected}。");
                return;
            }

            clearPort(null);
            result.Errors.Add($"{displayName}未通过USB序列号 {expected} 与协议的双重确认" +
                (string.IsNullOrWhiteSpace(actual) ? "。" : $"；协议识别端口为 {actual}。"));
        }

        private static string ResolveTtlPortByHardwareId(
            string configuredPort,
            Action<string> logAction)
        {
            string expectedSerial = GetSetting("TtlUsbSerial", null);
            if (!string.IsNullOrWhiteSpace(expectedSerial))
            {
                string[] serialMatches = SerialPort.GetPortNames()
                    .Where(port => string.Equals(
                        TryGetUsbSerial(port),
                        expectedSerial,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (serialMatches.Length != 1)
                {
                    logAction?.Invoke($"TTL USB序列号 {expectedSerial} 匹配到 {serialMatches.Length} 个串口；禁止猜测烧录口。");
                    return null;
                }
                logAction?.Invoke($"TTL烧录口按USB序列号 {expectedSerial} 定位到 {serialMatches[0]}。");
                return serialMatches[0];
            }

            string expected = GetSetting("TtlUsbHardwareId", null);
            if (string.IsNullOrWhiteSpace(expected))
                return configuredPort;

            string[] matches = SerialPort.GetPortNames()
                .Where(port => string.Equals(
                    TryGetUsbHardwareId(port),
                    expected,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length != 1)
            {
                logAction?.Invoke($"TTL USB硬件ID {expected} 匹配到 {matches.Length} 个串口；禁止猜测烧录口。");
                return null;
            }
            logAction?.Invoke($"TTL烧录口按唯一USB硬件ID {expected} 定位到 {matches[0]}。");
            return matches[0];
        }

        private static void ApplyConfiguredFallbackPorts(
            SKSerialDeviceDiscoveryResult configured,
            SKSerialDeviceDiscoveryResult result)
        {
            // A positive identity probe is stronger evidence than a COM number
            // copied from another PC. Only fill gaps in the scan from configuration.
            if (string.IsNullOrWhiteSpace(GetSetting("AnsPowerUsbSerial", null)) &&
                string.IsNullOrWhiteSpace(result.AnsPowerPort) &&
                !string.IsNullOrWhiteSpace(configured.AnsPowerPort))
                result.AnsPowerPort = configured.AnsPowerPort;
            // Source A must pass its SCPI identity probe. Do not turn a configured
            // COM number into a successful discovery result when the supply did
            // not answer.
            if (string.IsNullOrWhiteSpace(GetSetting("HengHuiPowerBUsbSerial", null)) &&
                string.IsNullOrWhiteSpace(result.HengHuiPowerBPort) &&
                !string.IsNullOrWhiteSpace(configured.HengHuiPowerBPort))
                result.HengHuiPowerBPort = configured.HengHuiPowerBPort;
            if (string.IsNullOrWhiteSpace(GetSetting("ElectronicLoadUsbSerial", null)) &&
                string.IsNullOrWhiteSpace(result.ElectronicLoadPort) &&
                !string.IsNullOrWhiteSpace(configured.ElectronicLoadPort))
                result.ElectronicLoadPort = configured.ElectronicLoadPort;
            if (string.IsNullOrWhiteSpace(GetSetting("DaqUsbSerial", null)) &&
                string.IsNullOrWhiteSpace(result.DaqPort) &&
                !string.IsNullOrWhiteSpace(configured.DaqPort))
                result.DaqPort = configured.DaqPort;
            if (string.IsNullOrWhiteSpace(GetSetting("SKRelayBusUsbSerial", null)) &&
                string.IsNullOrWhiteSpace(result.DigitalInputPort) &&
                !string.IsNullOrWhiteSpace(configured.DigitalInputPort))
                result.DigitalInputPort = configured.DigitalInputPort;
            if (string.IsNullOrWhiteSpace(GetSetting("SKRelayBusUsbSerial", null)) &&
                string.IsNullOrWhiteSpace(result.FixtureRelayPort) &&
                !string.IsNullOrWhiteSpace(configured.FixtureRelayPort))
                result.FixtureRelayPort = configured.FixtureRelayPort;
            if (string.IsNullOrWhiteSpace(GetSetting("SKRelayBusUsbSerial", null)) &&
                string.IsNullOrWhiteSpace(result.Relay16Port) &&
                !string.IsNullOrWhiteSpace(configured.Relay16Port))
                result.Relay16Port = configured.Relay16Port;
            if (string.IsNullOrWhiteSpace(result.TargetBoardPort) &&
                !string.IsNullOrWhiteSpace(configured.TargetBoardPort))
                result.TargetBoardPort = configured.TargetBoardPort;
            if (string.IsNullOrWhiteSpace(result.TtlPort) &&
                !string.IsNullOrWhiteSpace(configured.TtlPort))
                result.TtlPort = configured.TtlPort;
        }

        private static void LogConfiguredPorts(
            SKSerialDeviceDiscoveryResult result,
            Action<string> logAction)
        {
            var configured = new[]
            {
                new { Name = "ANS电源", Port = result.AnsPowerPort },
                new { Name = "艾诺AN50300-10电源A", Port = result.AinuoPowerAPort },
                new { Name = "恒辉电源B", Port = result.HengHuiPowerBPort },
                new { Name = "电子负载", Port = result.ElectronicLoadPort },
                new { Name = "DAQ", Port = result.DaqPort },
                new { Name = "数字输入", Port = result.DigitalInputPort },
                new { Name = "工装继电器", Port = result.FixtureRelayPort },
                new { Name = "16路继电器", Port = result.Relay16Port },
                new { Name = "MBD UART", Port = result.TargetBoardPort },
                new { Name = "TTL", Port = result.TtlPort }
            };

            foreach (var item in configured.Where(item => !string.IsNullOrWhiteSpace(item.Port)))
                logAction?.Invoke($"待校验串口：{item.Name}={item.Port}");
        }

        private static SKSerialDeviceDiscoveryResult BuildResultFromProbes(
            IEnumerable<PortProbe> probes,
            IEnumerable<string> availablePorts,
            string configuredTtlPort,
            bool requireTtl,
            Action<string> logAction)
        {
            PortProbe[] probeArray = probes?.ToArray() ?? new PortProbe[0];
            var result = new SKSerialDeviceDiscoveryResult
            {
                TtlRequired = requireTtl
            };
            AssignSingle(probeArray, "DAQ", "GWInstek DAQ-9600", value => result.DaqPort = value, result);
            AssignSingle(probeArray, "LOAD", "KUNKIN 7303电子负载", value => result.ElectronicLoadPort = value, result);
            AssignSingle(probeArray, "ANS", "ANS电源", value => result.AnsPowerPort = value, result);
            AssignSingle(probeArray, "DI", "16路数字量输入模块(站号02)", value => result.DigitalInputPort = value, result);
            AssignFixtureRelay(probeArray, result);
            AssignRelay16(probeArray, result);
            AssignHengHui(probeArray, result, logAction);
            AssignAinuoAn50(probeArray, result, logAction);
            result.TtlPort = ValidateConfiguredPassivePort(
                availablePorts,
                configuredTtlPort,
                "独立TTL烧录口",
                requireTtl,
                logAction,
                result);
            VerifyStationUsbSerialBindings(result, logAction);
            return result;
        }

        public static async Task<SKSerialDeviceDiscoveryResult> DiscoverAsync(
            Action<string> logAction = null)
        {
            string[] portNames = SerialPort.GetPortNames()
                .Where(name => GetComNumber(name) != int.MaxValue)
                .OrderBy(GetComNumber)
                .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            logAction?.Invoke(
                $"开始扫描 {portNames.Length} 个串口：{string.Join(", ", portNames)}");

            string configuredTtlPort = ResolveTtlPortByHardwareId(
                GetSetting("TtlPort", null), logAction);
            bool requireTtl = GetBoolSetting("SKRequireTtl", true);
            var passivePorts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(configuredTtlPort))
                passivePorts.Add(configuredTtlPort.Trim());

            // Never probe the independent programming TTL port.  MBD is intentionally
            // not identified here because it only responds after ANS/RL4 power-up.
            Task<PortProbe>[] tasks = portNames
                .Where(portName => !passivePorts.Contains(portName))
                .Select(portName => Task.Run(() => ProbePort(portName, logAction)))
                .ToArray();
            PortProbe[] probes = await Task.WhenAll(tasks);

            var result = new SKSerialDeviceDiscoveryResult();
            result.TtlRequired = requireTtl;
            AssignSingle(probes, "DAQ", "GWInstek DAQ-9600", value => result.DaqPort = value, result);
            AssignSingle(probes, "LOAD", "KUNKIN 7303电子负载", value => result.ElectronicLoadPort = value, result);
            AssignSingle(probes, "ANS", "ANS电源", value => result.AnsPowerPort = value, result);
            AssignSingle(probes, "DI", "16路数字量输入模块(站号02)", value => result.DigitalInputPort = value, result);
            AssignFixtureRelay(probes, result);
            AssignRelay16(probes, result);
            AssignHengHui(probes, result, logAction);
            AssignAinuoAn50(probes, result, logAction);
            result.TargetBoardPort = null;
            logAction?.Invoke("MBD UART识别已延后：将在DUT上电并完成启动等待后执行0D探测。");
            result.TtlPort = ValidateConfiguredPassivePort(
                portNames,
                configuredTtlPort,
                "独立TTL烧录口",
                requireTtl,
                logAction,
                result);

            VerifyStationUsbSerialBindings(result, logAction);

            ApplyToCommunicationState(result);

            foreach (string missing in result.GetMissingDeviceNames())
                logAction?.Invoke($"未识别到必需设备：{missing}");

            return result;
        }

        /// <summary>
        /// Identifies MBD only after the test sequence has powered the DUT and waited
        /// for boot.  Ports already assigned to station instruments are excluded.
        /// </summary>
        public static async Task<string> DiscoverMbdPortAsync(
            Action<string> logAction = null,
            string preferredPort = null,
            bool ignoreConfiguredPort = false,
            bool allowUnverifiedFallback = true,
            int? baudRate = null)
        {
            string[] portNames = SerialPort.GetPortNames()
                .Where(name => GetComNumber(name) != int.MaxValue)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(GetComNumber)
                .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            string configuredTargetBoardPort = ignoreConfiguredPort
                ? null
                : string.IsNullOrWhiteSpace(preferredPort)
                    ? GetSetting("TargetBoardPort", null)
                    : preferredPort.Trim();
            var excludedPorts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ComName.ansPowerComName,
                ComName.ainuoPowerAComName,
                ComName.hengHuiPowerBComName,
                ComName.electronicLoadComName,
                ComName.daqComName,
                ComName.digitalInputComName,
                ComName.fixtureRelayComName,
                ComName.relay16ComName,
                ComName.uartComName
            };
            excludedPorts.RemoveWhere(string.IsNullOrWhiteSpace);

            string[] candidates = portNames
                .Where(portName => !excludedPorts.Contains(portName))
                .OrderBy(portName => string.Equals(
                    portName,
                    configuredTargetBoardPort,
                    StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(GetComNumber)
                .ToArray();

            logAction?.Invoke(
                $"DUT已上电，开始识别MBD UART；候选端口：" +
                $"{(candidates.Length == 0 ? "无" : string.Join(", ", candidates))}");

            // Probe serially and stop at the first verified response. Opening every
            // candidate in parallel can race a USB CDC device that is still
            // enumerating, and also makes a known-good MBD wait for unrelated ports.
            var probeResults = new List<PortProbe>();
            foreach (string portName in candidates)
            {
                PortProbe probe = await Task.Run(() => ProbeMbdPort(
                    portName,
                    logAction,
                    baudRate ?? GetIntSetting("SKTargetBoardBaudRate", 9600)));
                probeResults.Add(probe);
                if (probe.Kind == "MBD")
                    break;
            }
            PortProbe[] matches = probeResults.Where(probe => probe.Kind == "MBD").ToArray();
            if (matches.Length != 1)
            {
                if (matches.Length == 0 && allowUnverifiedFallback)
                {
                    string configuredCandidate = candidates.FirstOrDefault(portName =>
                        !string.IsNullOrWhiteSpace(configuredTargetBoardPort) &&
                        string.Equals(
                            portName,
                            configuredTargetBoardPort,
                            StringComparison.OrdinalIgnoreCase));
                    string fallbackPort = configuredCandidate ??
                        (candidates.Length == 1 ? candidates[0] : null);
                    if (!string.IsNullOrWhiteSpace(fallbackPort))
                    {
                        ComName.testComName = fallbackPort;
                        logAction?.Invoke(
                            $"MBD 0D严格探测无回包，但候选端口唯一/已配置：{fallbackPort}；" +
                            "先按MBD UART接入，后续EXIT/R14/R15真实命令负责确认通信。");
                        return fallbackPort;
                    }
                }

                if (matches.Length == 0 && !allowUnverifiedFallback)
                {
                    logAction?.Invoke(
                        "MBD探测未收到COMMAND ERROR + Ready.；端口存在不等于设备可通信，" +
                        "不会使用无回包的唯一候选口或固定COM口兜底。");
                }

                logAction?.Invoke(matches.Length == 0
                    ? "DUT上电后仍未识别到MBD UART。"
                    : $"识别到多个MBD UART端口：{string.Join(", ", matches.Select(item => item.PortName))}");
                return null;
            }

            string detectedPort = matches[0].PortName;
            ComName.testComName = detectedPort;
            logAction?.Invoke($"MBD UART识别完成：{detectedPort}");
            return detectedPort;
        }

        public static void ApplyToCommunicationState(SKSerialDeviceDiscoveryResult result)
        {
            if (result == null) return;
            ComName.ansPowerComName = result.AnsPowerPort;
            ComName.ainuoPowerAComName = result.AinuoPowerAPort;
            ComName.hengHuiPowerBComName = result.HengHuiPowerBPort;
            ComName.electronicLoadComName = result.ElectronicLoadPort;
            ComName.daqComName = result.DaqPort;
            ComName.digitalInputComName = result.DigitalInputPort;
            ComName.fixtureRelayComName = result.FixtureRelayPort;
            ComName.relay16ComName = result.Relay16Port;
            ComName.rs485ComName = result.FixtureRelayPort;
            ComName.testComName = result.TargetBoardPort;
            ComName.uartComName = result.TtlPort;
        }

        private static string ValidateConfiguredPassivePort(
            IEnumerable<string> availablePorts,
            string configuredPort,
            string displayName,
            bool required,
            Action<string> logAction,
            SKSerialDeviceDiscoveryResult result)
        {
            if (string.IsNullOrWhiteSpace(configuredPort))
            {
                if (required)
                    result.Errors.Add($"{displayName}未配置独立串口。");
                else
                    logAction?.Invoke($"{displayName}未连接：按SKRequireTtl=false跳过烧录。 ");
                return null;
            }

            string portName = configuredPort.Trim();
            if (!availablePorts.Contains(portName, StringComparer.OrdinalIgnoreCase))
            {
                result.Errors.Add($"{displayName}配置为{portName}，但Windows未枚举到该端口。");
                return null;
            }

            try
            {
                using (var port = new SerialPort(portName))
                {
                    port.Open();
                }
                logAction?.Invoke($"{displayName}已按配置确认：{portName}");
                return portName;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{displayName}端口{portName}不可用：{ex.Message}");
                return null;
            }
        }

        private static PortProbe ProbePort(string portName, Action<string> logAction)
        {
            string usbSerial = TryGetUsbSerial(portName);
            if (MatchesConfiguredUsbSerial(usbSerial, "SKRelayBusUsbSerial"))
                return ProbeFixtureBusPort(portName, logAction) ?? NewProbe(portName, "UNKNOWN", null);
            if (MatchesConfiguredUsbSerial(usbSerial, "AnsPowerUsbSerial"))
                return ProbeAnsPort(portName, logAction) ?? NewProbe(portName, "UNKNOWN", null);
            if (MatchesConfiguredUsbSerial(usbSerial, "AinuoPowerAUsbSerial"))
            {
                string identity;
                string error;
                int baudRate = GetIntSetting("AinuoPowerABaudRate", 38400);
                if (ProbeAinuoAn50(portName, baudRate, out identity, out error))
                {
                    logAction?.Invoke($"识别成功：{portName} = 艾诺{AinuoAn50PowerSupply.Model}电源A " +
                        $"({identity}, {baudRate} baud)");
                    return NewProbe(portName, "AINUO_AN50", identity);
                }
                logAction?.Invoke($"艾诺电源A已按USB序列号定位到 {portName}，但只读SCPI探测失败：{error}");
                return NewProbe(portName, "UNKNOWN", null);
            }
            if (MatchesConfiguredUsbSerial(usbSerial, "ElectronicLoadUsbSerial"))
            {
                string error;
                int baudRate = GetIntSetting("SKElectronicLoadBaudRate", 38400);
                string identity = QueryScpiIdn(portName, baudRate, 2000, out error);
                if (IsElectronicLoadIdentity(identity))
                {
                    logAction?.Invoke($"识别成功：{portName} = KUNKIN 7303电子负载 " +
                        $"({identity}, {baudRate} baud)");
                    return NewProbe(portName, "LOAD", identity);
                }
                logAction?.Invoke($"电子负载已按USB序列号定位到 {portName}，但IDN探测失败：{error ?? identity}");
                return NewProbe(portName, "UNKNOWN", identity);
            }
            if (MatchesConfiguredUsbSerial(usbSerial, "HengHuiPowerBUsbSerial"))
            {
                string lastIdentity = null;
                string lastError = null;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    lastIdentity = QueryScpiIdn(portName, 9600, attempt == 0 ? 800 : 1500, out lastError);
                    if (!string.IsNullOrWhiteSpace(lastIdentity) &&
                        lastIdentity.IndexOf("DC POWER", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        lastIdentity.IndexOf("8602", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        logAction?.Invoke($"识别成功：{portName} = 恒辉8602，USB序列号={usbSerial}" +
                            (attempt > 0 ? "（IDN重试后确认）" : string.Empty));
                        return NewProbe(portName, "HENGHUI", lastIdentity);
                    }
                }
                logAction?.Invoke($"恒辉电源B已按USB序列号定位到 {portName}，但两次IDN校验失败：{lastError ?? lastIdentity}");
                return NewProbe(portName, "UNKNOWN", lastIdentity);
            }
            if (MatchesConfiguredUsbSerial(usbSerial, "DaqUsbSerial"))
            {
                string error;
                string identity = QueryScpiIdn(portName, 9600, 1500, out error);
                if (!string.IsNullOrWhiteSpace(identity) &&
                    identity.IndexOf("GWInstek", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    identity.IndexOf("DAQ-9600", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    logAction?.Invoke($"识别成功：{portName} = GWInstek DAQ-9600 ({identity})");
                    return NewProbe(portName, "DAQ", identity);
                }
                logAction?.Invoke($"DAQ已按USB序列号定位到 {portName}，但IDN校验失败：{error ?? identity}");
                return NewProbe(portName, "UNKNOWN", identity);
            }

            string scpiError;
            string idn = QueryScpiIdn(portName, 9600, 2000, out scpiError);
            if (!string.IsNullOrWhiteSpace(idn))
            {
                if (idn.IndexOf("GWInstek", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    idn.IndexOf("DAQ-9600", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    logAction?.Invoke($"识别成功：{portName} = GWInstek DAQ-9600 ({idn})");
                    return NewProbe(portName, "DAQ", idn);
                }

                if (idn.IndexOf("KUNKIN", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    idn.IndexOf("7303", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    logAction?.Invoke($"识别成功：{portName} = KUNKIN 7303电子负载 ({idn})");
                    return NewProbe(portName, "LOAD", idn);
                }

                if (idn.IndexOf("DC POWER", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    idn.IndexOf("8602", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    PortProbe probe = NewProbe(portName, "HENGHUI", idn);
                    probe.UsbSerial = TryGetFtdiSerial(portName);
                    logAction?.Invoke(
                        $"识别成功：{portName} = 恒辉8602，USB序列号={probe.UsbSerial ?? "未知"}");
                    return probe;
                }

            }

            // KUNKIN 7303 is configured for 38400 baud at this SK station;
            // DAQ-9600 and HengHui supplies continue to use 9600 baud.
            int loadBaudRate = GetIntSetting("SKElectronicLoadBaudRate", 38400);
            string loadScpiError = null;
            if (loadBaudRate != 9600)
            {
                string loadIdn = QueryScpiIdn(
                    portName,
                    loadBaudRate,
                    2000,
                    out loadScpiError);
                if (IsElectronicLoadIdentity(loadIdn))
                {
                    logAction?.Invoke(
                        $"识别成功：{portName} = KUNKIN 7303电子负载 " +
                        $"({loadIdn}, {loadBaudRate} baud)");
                    return NewProbe(portName, "LOAD", loadIdn);
                }
            }

            int an50BaudRate = GetIntSetting("AinuoPowerABaudRate", 38400);
            string an50Identity;
            string an50Error;
            if (ProbeAinuoAn50(
                portName,
                an50BaudRate,
                out an50Identity,
                out an50Error))
            {
                logAction?.Invoke(
                    $"识别成功：{portName} = 艾诺{AinuoAn50PowerSupply.Model}电源A " +
                    $"({an50Identity}, {an50BaudRate} baud)");
                return NewProbe(portName, "AINUO_AN50", an50Identity);
            }

            if (!string.IsNullOrWhiteSpace(idn))
                logAction?.Invoke($"串口 {portName} 返回未登记的IDN：{idn}");
            else if (!string.IsNullOrWhiteSpace(scpiError) &&
                     !string.IsNullOrWhiteSpace(loadScpiError) &&
                     !string.IsNullOrWhiteSpace(an50Error))
            {
                logAction?.Invoke(
                    $"串口 {portName} SCPI探测未完成：" +
                    $"9600={scpiError}；{loadBaudRate}={loadScpiError}；" +
                    $"AN50/{an50BaudRate}={an50Error}");
            }

            PortProbe fixtureProbe = ProbeFixtureBusPort(portName, logAction);
            if (fixtureProbe != null)
                return fixtureProbe;

            PortProbe ansProbe = ProbeAnsPort(portName, logAction);
            if (ansProbe != null)
                return ansProbe;

            logAction?.Invoke($"未能识别串口：{portName}");
            return NewProbe(portName, "UNKNOWN", null);
        }

        private static bool MatchesConfiguredUsbSerial(string actualSerial, string settingKey)
        {
            string expected = GetSetting(settingKey, null);
            return !string.IsNullOrWhiteSpace(expected) &&
                   string.Equals(actualSerial, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static PortProbe ProbeFixtureBusPort(string portName, Action<string> logAction)
        {
            bool isDigitalInput = ProbeModbus(
                portName,
                38400,
                new byte[] { 0x02, 0x04, 0x00, 0x32, 0x00, 0x01 },
                7,
                response => response[0] == 0x02 && response[1] == 0x04 && response[2] == 0x02);
            bool isFixtureRelay = ProbeModbus(
                portName,
                38400,
                new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x08 },
                21,
                response => response[0] == 0x01 && response[1] == 0x03 && response[2] == 0x10);
            bool isRelay16 = ProbeModbus(
                portName,
                38400,
                new byte[] { 0x03, 0x03, 0x00, 0x00, 0x00, 0x08 },
                21,
                response => response[0] == 0x03 && response[1] == 0x03 && response[2] == 0x10);

            if (isDigitalInput || isFixtureRelay || isRelay16)
            {
                var fixtureProbe = NewProbe(
                    portName,
                    isDigitalInput ? "DI" : isFixtureRelay ? "FIXTURE_RELAY" : "RELAY16",
                    isDigitalInput
                        ? "Modbus 02/04/0032"
                        : isFixtureRelay
                            ? "Modbus 01/03/0000/0008"
                            : "Modbus 03/03/0000/0008");
                fixtureProbe.IsFixtureRelay = isFixtureRelay;
                fixtureProbe.IsRelay16 = isRelay16;
                if (isDigitalInput)
                    logAction?.Invoke($"识别成功：{portName} = 16路数字量输入模块(站号02/38400)");
                if (isFixtureRelay)
                    logAction?.Invoke(
                        $"识别成功：{portName} = 工装继电器模块(站号01/38400，读取0000H起8个寄存器)");
                if (isRelay16)
                    logAction?.Invoke(
                        $"识别成功：{portName} = 16路继电器模块(站号03/38400，读取0000H起8个寄存器)");
                return fixtureProbe;
            }

            return null;
        }

        private static PortProbe ProbeAnsPort(string portName, Action<string> logAction)
        {
            if (ProbeModbus(
                portName,
                9600,
                new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x02 },
                9,
                response => response[0] == 0x01 && response[1] == 0x03 && response[2] == 0x04))
            {
                logAction?.Invoke($"识别成功：{portName} = ANS电源(Modbus 01/9600)");
                return NewProbe(portName, "ANS", "Modbus 01/03/0000");
            }

            return null;
        }

        private static PortProbe ProbeMbdPort(
            string portName,
            Action<string> logAction,
            int baudRate)
        {
            string response;
            string error;
            if (ProbeMbd(portName, baudRate, out response, out error))
            {
                logAction?.Invoke(
                    $"识别成功：{portName} = MBD UART/目标板USB-B " +
                    $"({baudRate} 8N1, 发送0D，收到COMMAND ERROR + Ready.)");
                return NewProbe(portName, "MBD", response);
            }

            if (!string.IsNullOrWhiteSpace(response))
                logAction?.Invoke($"串口 {portName} 的0D回复不符合MBD特征：{response}");
            else if (!string.IsNullOrWhiteSpace(error))
                logAction?.Invoke($"串口 {portName} MBD 0D探测未通过：{error}");
            return NewProbe(portName, "UNKNOWN", response);
        }

        private static bool ProbeAinuoAn50(
            string portName,
            int baudRate,
            out string identity,
            out string error)
        {
            identity = null;
            error = null;
            try
            {
                using (var supply = new AinuoAn50PowerSupply())
                {
                    supply.ReadTimeoutMs = Math.Max(
                        100,
                        GetIntSetting("AinuoPowerAReadTimeoutMs", 1500));
                    supply.WriteTimeoutMs = Math.Max(
                        100,
                        GetIntSetting("AinuoPowerAWriteTimeoutMs", 1000));
                    supply.OpenSettleMs = Math.Max(
                        0,
                        GetIntSetting("AinuoPowerAOpenSettleMs", 200));
                    supply.ResponseIdleMs = Math.Max(
                        20,
                        GetIntSetting("AinuoPowerAResponseIdleMs", 120));
                    supply.CommandDelayMs = Math.Max(
                        0,
                        GetIntSetting("AinuoPowerACommandDelayMs", 180));
                    supply.Connect(portName, baudRate);
                    identity = supply.Identity;
                    return !string.IsNullOrWhiteSpace(identity);
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// A standalone CR is an invalid command at the MBD terminal.  Its stable
        /// identification signature is the error text plus the Ready trailer.
        /// </summary>
        public static bool IsMbdIdentificationResponse(string response)
        {
            return !string.IsNullOrWhiteSpace(response) &&
                   MbdCommandErrorRegex.IsMatch(response) &&
                   TargetBoardComm.ContainsReadyPrompt(response);
        }

        private static bool ProbeMbd(
            string portName,
            int baudRate,
            out string response,
            out string error)
        {
            response = null;
            error = null;
            int settleMs = GetIntSetting("SKMbdOpenSettleMs", 500);
            int timeoutMs = GetIntSetting("SKMbdProbeTimeoutMs", 2000);

            try
            {
                using (var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One))
                {
                    port.ReadTimeout = timeoutMs;
                    port.WriteTimeout = 500;
                    port.Handshake = Handshake.None;
                    port.DtrEnable = false;
                    port.RtsEnable = false;
                    port.Encoding = Encoding.ASCII;
                    port.Open();
                    if (settleMs > 0)
                        Thread.Sleep(settleMs);
                    port.DiscardInBuffer();
                    port.DiscardOutBuffer();

                    // Send exactly one CR byte.  Do not send the ASCII text "0D".
                    port.Write(new[] { (byte)'\r' }, 0, 1);

                    var buffer = new StringBuilder();
                    var timer = Stopwatch.StartNew();
                    long lastDataAtMs = -1;
                    while (timer.ElapsedMilliseconds < timeoutMs)
                    {
                        string chunk = port.ReadExisting();
                        if (!string.IsNullOrEmpty(chunk))
                        {
                            buffer.Append(chunk);
                            lastDataAtMs = timer.ElapsedMilliseconds;
                        }

                        if (IsMbdIdentificationResponse(buffer.ToString()) &&
                            lastDataAtMs >= 0 &&
                            timer.ElapsedMilliseconds - lastDataAtMs >= 100)
                        {
                            break;
                        }
                        Thread.Sleep(10);
                    }

                    response = NormalizeMbdResponse(buffer.ToString());
                    if (string.IsNullOrWhiteSpace(response))
                        error = $"等待0D回复超时({timeoutMs}ms)";
                    return IsMbdIdentificationResponse(response);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                error = $"端口被占用或无访问权限：{ex.Message}";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string NormalizeMbdResponse(string rawResponse)
        {
            return string.Join(
                " | ",
                (rawResponse ?? string.Empty)
                    .Split(new[] { '\r', '\n', '\0' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0));
        }

        private static PortProbe NewProbe(string portName, string kind, string identity)
        {
            return new PortProbe
            {
                PortName = portName,
                Kind = kind,
                Identity = identity,
                UsbSerial = TryGetUsbSerial(portName)
            };
        }

        private static void AssignSingle(
            IEnumerable<PortProbe> probes,
            string kind,
            string displayName,
            Action<string> assign,
            SKSerialDeviceDiscoveryResult result)
        {
            PortProbe[] matches = probes.Where(item => item.Kind == kind).ToArray();
            if (matches.Length == 1)
            {
                assign(matches[0].PortName);
                return;
            }

            if (matches.Length > 1)
            {
                result.Errors.Add(
                    $"{displayName}识别到多个端口：{string.Join(", ", matches.Select(item => item.PortName))}");
            }
        }

        private static void AssignAinuoAn50(
            IEnumerable<PortProbe> probes,
            SKSerialDeviceDiscoveryResult result,
            Action<string> logAction)
        {
            PortProbe[] allProbes = probes.ToArray();
            PortProbe[] matches = allProbes
                .Where(item => item.Kind == "AINUO_AN50")
                .ToArray();
            string expectedSerial = GetSetting("AinuoPowerAUsbSerial", null);

            if (!string.IsNullOrWhiteSpace(expectedSerial))
            {
                PortProbe serialProbe = allProbes.FirstOrDefault(item => string.Equals(
                    item.UsbSerial,
                    expectedSerial,
                    StringComparison.OrdinalIgnoreCase));
                if (serialProbe != null && serialProbe.Kind == "AINUO_AN50")
                {
                    result.AinuoPowerAPort = serialProbe.PortName;
                    logAction?.Invoke(
                        $"艾诺{AinuoAn50PowerSupply.Model}电源A按SCPI应答和FTDI序列号双重确认：" +
                        $"{serialProbe.PortName} / {expectedSerial}。");
                    return;
                }

                if (serialProbe != null)
                {
                    result.Errors.Add(
                        $"电源A FTDI序列号 {expectedSerial} 位于 {serialProbe.PortName}，" +
                        "但AN50300-10只读SCPI探测未通过。");
                    return;
                }

                if (matches.Length > 0)
                {
                    result.Errors.Add(
                        $"识别到AN50300-10应答端口 {string.Join(", ", matches.Select(item => item.PortName))}，" +
                        $"但其FTDI序列号与配置值 {expectedSerial} 不匹配。");
                    return;
                }
            }

            AssignSingle(
                matches,
                "AINUO_AN50",
                $"艾诺{AinuoAn50PowerSupply.Model}电源A",
                value => result.AinuoPowerAPort = value,
                result);
        }

        private static void AssignHengHui(
            IEnumerable<PortProbe> probes,
            SKSerialDeviceDiscoveryResult result,
            Action<string> logAction)
        {
            PortProbe[] allProbes = probes.ToArray();
            PortProbe[] matches = allProbes.Where(item => item.Kind == "HENGHUI").ToArray();
            string serialB = GetSetting("HengHuiPowerBUsbSerial", null);
            PortProbe matchB = string.IsNullOrWhiteSpace(serialB)
                ? (matches.Length == 1 ? matches[0] : null)
                : matches.FirstOrDefault(item => string.Equals(
                    item.UsbSerial, serialB, StringComparison.OrdinalIgnoreCase));

            result.HengHuiPowerBPort = matchB?.PortName;
            if (!string.IsNullOrWhiteSpace(serialB) && matchB == null)
            {
                PortProbe serialProbe = allProbes.FirstOrDefault(item => string.Equals(
                    item.UsbSerial, serialB, StringComparison.OrdinalIgnoreCase));
                result.Errors.Add(serialProbe == null
                    ? $"未找到恒辉电源B绑定的USB序列号 {serialB}。"
                    : $"恒辉电源B的USB序列号 {serialB} 位于 {serialProbe.PortName}，但8602 IDN未通过。");
            }

            foreach (PortProbe unmatched in matches.Where(item => item != matchB))
            {
                result.Errors.Add(
                    $"恒辉8602 {unmatched.PortName} 的USB序列号 {unmatched.UsbSerial ?? "未知"} 未绑定电源B");
            }
        }

        private static void AssignFixtureRelay(
            IEnumerable<PortProbe> probes,
            SKSerialDeviceDiscoveryResult result)
        {
            PortProbe[] matches = probes.Where(item => item.IsFixtureRelay).ToArray();
            if (matches.Length == 1)
            {
                result.FixtureRelayPort = matches[0].PortName;
                return;
            }

            if (matches.Length > 1)
            {
                result.Errors.Add(
                    $"工装继电器模块识别到多个端口：{string.Join(", ", matches.Select(item => item.PortName))}");
            }
        }

        private static void AssignRelay16(
            IEnumerable<PortProbe> probes,
            SKSerialDeviceDiscoveryResult result)
        {
            PortProbe[] matches = probes.Where(item => item.IsRelay16).ToArray();
            if (matches.Length == 1)
            {
                result.Relay16Port = matches[0].PortName;
                return;
            }

            if (matches.Length > 1)
            {
                result.Errors.Add(
                    $"16路继电器模块识别到多个端口：{string.Join(", ", matches.Select(item => item.PortName))}");
            }
        }

        private static string QueryScpiIdn(
            string portName,
            int baudRate,
            int timeoutMs,
            out string error)
        {
            error = null;
            try
            {
                using (var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One))
                {
                    port.ReadTimeout = timeoutMs;
                    port.WriteTimeout = 500;
                    port.Open();
                    // Some USB-to-serial instruments need a short settling period after Open().
                    Thread.Sleep(100);
                    port.DiscardInBuffer();
                    port.DiscardOutBuffer();
                    port.Write("*IDN?\r\n");

                    var response = new StringBuilder();
                    var timer = Stopwatch.StartNew();
                    while (timer.ElapsedMilliseconds < timeoutMs)
                    {
                        string chunk = port.ReadExisting();
                        if (!string.IsNullOrEmpty(chunk))
                        {
                            response.Append(chunk);
                            string normalized = NormalizeScpiIdentity(response.ToString());
                            // Do not stop on the echoed "*IDN?" line.  KUNKIN 7303 can
                            // echo the command before returning its identity on a later line.
                            if (LooksLikeRegisteredScpiIdentity(normalized))
                                break;
                        }
                        Thread.Sleep(20);
                    }

                    string identity = NormalizeScpiIdentity(response.ToString());
                    if (string.IsNullOrWhiteSpace(identity))
                        error = $"等待 *IDN? 回复超时({timeoutMs}ms)";
                    else if (!LooksLikeRegisteredScpiIdentity(identity))
                        error = $"只收到回显或不完整回复：{identity}";
                    return identity;
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                error = $"端口被占用或无访问权限：{ex.Message}";
                return null;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        private static string NormalizeScpiIdentity(string rawResponse)
        {
            string[] lines = (rawResponse ?? string.Empty)
                .Split(new[] { '\r', '\n', '\0' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .Where(line => !string.Equals(line, "*IDN?", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return string.Join(" ", lines).Trim();
        }

        private static bool LooksLikeRegisteredScpiIdentity(string identity)
        {
            if (string.IsNullOrWhiteSpace(identity))
                return false;

            return
                (identity.IndexOf("KUNKIN", StringComparison.OrdinalIgnoreCase) >= 0 &&
                 identity.IndexOf("7303", StringComparison.OrdinalIgnoreCase) >= 0) ||
                (identity.IndexOf("GWInstek", StringComparison.OrdinalIgnoreCase) >= 0 &&
                 identity.IndexOf("DAQ-9600", StringComparison.OrdinalIgnoreCase) >= 0) ||
                (identity.IndexOf("DC POWER", StringComparison.OrdinalIgnoreCase) >= 0 &&
                 identity.IndexOf("8602", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsElectronicLoadIdentity(string identity)
        {
            return !string.IsNullOrWhiteSpace(identity) &&
                   identity.IndexOf("KUNKIN", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   identity.IndexOf("7303", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int GetIntSetting(string key, int defaultValue)
        {
            int value;
            return int.TryParse(ConfigurationManager.AppSettings[key], out value)
                ? value
                : defaultValue;
        }

        private static bool GetBoolSetting(string key, bool defaultValue)
        {
            bool value;
            return bool.TryParse(ConfigurationManager.AppSettings[key], out value)
                ? value
                : defaultValue;
        }

        private static bool ProbeModbus(
            string portName,
            int baudRate,
            byte[] payload,
            int expectedLength,
            Func<byte[], bool> responseValidator)
        {
            try
            {
                using (var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One))
                {
                    port.ReadTimeout = 600;
                    port.WriteTimeout = 500;
                    port.Open();
                    port.DiscardInBuffer();
                    port.DiscardOutBuffer();

                    byte[] crc = CalculateCrc(payload);
                    byte[] command = payload.Concat(crc).ToArray();
                    port.Write(command, 0, command.Length);

                    var response = new List<byte>();
                    var timer = Stopwatch.StartNew();
                    while (timer.ElapsedMilliseconds < 700 && response.Count < expectedLength)
                    {
                        int available = port.BytesToRead;
                        if (available > 0)
                        {
                            byte[] buffer = new byte[available];
                            int count = port.Read(buffer, 0, buffer.Length);
                            response.AddRange(buffer.Take(count));
                        }
                        else
                        {
                            Thread.Sleep(15);
                        }
                    }

                    byte[] bytes = response.Take(expectedLength).ToArray();
                    return bytes.Length == expectedLength &&
                           HasValidCrc(bytes) &&
                           responseValidator(bytes);
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool HasValidCrc(byte[] response)
        {
            if (response == null || response.Length < 4) return false;
            byte[] expected = CalculateCrc(response.Take(response.Length - 2).ToArray());
            return response[response.Length - 2] == expected[0] &&
                   response[response.Length - 1] == expected[1];
        }

        private static byte[] CalculateCrc(byte[] data)
        {
            ushort crc = 0xFFFF;
            foreach (byte value in data)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++)
                    crc = (ushort)(((crc & 1) != 0) ? ((crc >> 1) ^ 0xA001) : (crc >> 1));
            }
            return new[] { (byte)(crc & 0xFF), (byte)(crc >> 8) };
        }

        private static string TryGetFtdiSerial(string portName)
        {
            try
            {
                using (RegistryKey root = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Enum\FTDIBUS"))
                {
                    if (root == null) return null;
                    foreach (string deviceName in root.GetSubKeyNames())
                    {
                        using (RegistryKey device = root.OpenSubKey(deviceName))
                        {
                            if (device == null) continue;
                            foreach (string instanceName in device.GetSubKeyNames())
                            {
                                using (RegistryKey parameters = device.OpenSubKey(
                                    instanceName + @"\Device Parameters"))
                                {
                                    string candidatePort = parameters?.GetValue("PortName") as string;
                                    if (!string.Equals(candidatePort, portName, StringComparison.OrdinalIgnoreCase))
                                        continue;
                                }

                                int marker = deviceName.LastIndexOf('+');
                                return marker >= 0 ? deviceName.Substring(marker + 1) : deviceName;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Registry information is optional; an unbound device will be reported.
            }
            return null;
        }

        private static string TryGetUsbSerial(string portName)
        {
            string ftdiSerial = TryGetFtdiSerial(portName);
            if (!string.IsNullOrWhiteSpace(ftdiSerial))
                return ftdiSerial;

            try
            {
                using (RegistryKey root = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Enum\USB"))
                {
                    if (root == null) return null;
                    foreach (string deviceName in root.GetSubKeyNames())
                    {
                        using (RegistryKey device = root.OpenSubKey(deviceName))
                        {
                            if (device == null) continue;
                            foreach (string instanceName in device.GetSubKeyNames())
                            {
                                // An instance path with ampersands is a USB location,
                                // not a device-owned serial number (e.g. CH340).
                                if (instanceName.IndexOf('&') >= 0)
                                    continue;
                                using (RegistryKey parameters = device.OpenSubKey(
                                    instanceName + @"\Device Parameters"))
                                {
                                    if (string.Equals(
                                        parameters?.GetValue("PortName") as string,
                                        portName,
                                        StringComparison.OrdinalIgnoreCase))
                                        return instanceName;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // A USB serial is optional unless a configured binding requires it.
            }
            return null;
        }

        private static string TryGetUsbHardwareId(string portName)
        {
            try
            {
                using (RegistryKey root = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Enum\USB"))
                {
                    if (root == null) return null;
                    foreach (string deviceName in root.GetSubKeyNames())
                    {
                        using (RegistryKey device = root.OpenSubKey(deviceName))
                        {
                            if (device == null) continue;
                            foreach (string instanceName in device.GetSubKeyNames())
                            {
                                using (RegistryKey parameters = device.OpenSubKey(
                                    instanceName + @"\Device Parameters"))
                                {
                                    if (string.Equals(
                                        parameters?.GetValue("PortName") as string,
                                        portName,
                                        StringComparison.OrdinalIgnoreCase))
                                        return deviceName;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // A missing hardware ID must not select a programmer by COM number.
            }
            return null;
        }

        private static string GetSetting(string key, string defaultValue)
        {
            string configured = ConfigurationManager.AppSettings[key];
            return string.IsNullOrWhiteSpace(configured) ? defaultValue : configured.Trim();
        }

        private static int GetComNumber(string portName)
        {
            int number;
            return portName != null &&
                   portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
                   int.TryParse(portName.Substring(3), out number)
                ? number
                : int.MaxValue;
        }
    }
}
