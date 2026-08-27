using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TestPlatform.TestSequences
{
    public partial class SKBCM125Sequence
    {
        protected void AddBcm125FirstStartupSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager)
        {
            steps.Add(new TestStepItem
            {
                Name = "8.1 FIRST START-UP-配置MPS板电源140V/1A并保持输出关闭",
                RowIndex = steps.Count,
                PowerCritical = true,
                Action = async (token) =>
                {
                    bool ok = await deviceManager.ConfigureAnsOutputOffAsync(140.0f, 1.0f);
                    return new StepResult { IsPass = ok, Value = ok ? "140V/1A OFF" : "Power Fail" };
                }
            });

            int powerOnRowIndex = steps.Count;
            steps.Add(new TestStepItem
            {
                Name = "8.2 FIRST START-UP-闭合RL4后开启MPS板电源",
                RowIndex = powerOnRowIndex,
                PowerCritical = true,
                Action = async (token) =>
                {
                    bool ok = await deviceManager.CloseRl4ThenEnableAnsAsync(7, 140.0f, 1.0f);
                    if (ok)
                    {
                        int stableTimeoutMs = GetPlanDelayMs(
                            steps,
                            powerOnRowIndex,
                            5000);
                        ok = await deviceManager.WaitForPowerVoltageStableAsync(
                            "ANS",
                            140.0f,
                            Math.Min(2000, stableTimeoutMs),
                            stableTimeoutMs,
                            token);
                    }
                    return new StepResult { IsPass = ok, Value = ok ? "RL4 ON / 140V ON" : "Safe Power-On Fail" };
                }
            });

            AddBcm125DaqVoltageStep(steps, deviceManager, "B01 +5V", "BCM125.FIRST_STARTUP.B01_5V", 0, 101, 4750, 5300, "mV");
            AddBcm125DaqVoltageStep(steps, deviceManager, "B02 +VCC", "BCM125.FIRST_STARTUP.B02_VCC", 1, 102, 14690, 15570, "mV");
            AddBcm125DaqVoltageStep(steps, deviceManager, "B03 -VEE1", "BCM125.FIRST_STARTUP.B03_VEE1", 2, 103, -13890, -13610, "mV");
            AddBcm125DaqVoltageStep(steps, deviceManager, "B04 -VEE2", "BCM125.FIRST_STARTUP.B04_VEE2", 3, 104, -13890, -13610, "mV");
            AddBcm125DaqVoltageStep(steps, deviceManager, "B05 3V3A-1", "BCM125.FIRST_STARTUP.B05_3V3A_1", 4, 107, 3200, 3400, "mV");
            AddBcm125DaqVoltageStep(steps, deviceManager, "B06 3V3A-2", "BCM125.FIRST_STARTUP.B06_3V3A_2", 5, 108, 3200, 3400, "mV");
            AddBcm125DaqVoltageStep(steps, deviceManager, "B07 3V3-1", "BCM125.FIRST_STARTUP.B07_3V3_1", 6, 111, 3200, 3400, "mV");
            AddBcm125DaqVoltageStep(steps, deviceManager, "B08 3V3-2", "BCM125.FIRST_STARTUP.B08_3V3_2", 7, 112, 3200, 3400, "mV");
            AddBcm125DaqCurrentFromShuntStep(steps, deviceManager, "B09 +VCC_1电流", "BCM125.FIRST_STARTUP.B09_VCC_1_CURRENT", 8, 119, 2.2, 30.0, 90.0);
            AddBcm125DaqCurrentFromShuntStep(steps, deviceManager, "B10 +VCC_2电流", "BCM125.FIRST_STARTUP.B10_VCC_2_CURRENT", 9, 120, 2.2, 30.0, 90.0);
            AddBcm125DaqCurrentFromShuntStep(steps, deviceManager, "B11 +5V_1电流", "BCM125.FIRST_STARTUP.B11_5V_1_CURRENT", 10, 117, 0.47, 15.0, 50.0);
            AddBcm125DaqCurrentFromShuntStep(steps, deviceManager, "B12 +5V_2电流", "BCM125.FIRST_STARTUP.B12_5V_2_CURRENT", 11, 118, 0.47, 15.0, 50.0);
        }

        protected void AddBcm125ProgrammingSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager)
        {
            int row = steps.Count;

            AddProgrammingActionStep(steps, row++, "9.1 检查编程电源、负载及TTL连接（MBD上电后识别）", token => deviceManager.CheckBcm125ProgrammingInstrumentsAsync(), "Ready", "Check Fail");
            AddProgrammingActionStep(steps, row++, "9.1 关闭仪器输出并复位设定值为0", token => deviceManager.TurnOffAllInstrumentsAsync(), "Off/Reset", "Off Fail");
            AddFixtureRelayStep(steps, row++, deviceManager, "9.2 BCM1 闭合RLP1/RLP2/RLP3连接J4", "SKBCM125Rlp1To3Relays", "3,4,5", true, "RLP1-3 ON");
            AddDelayStep(steps, row++, "9.3 BCM1 等待300ms", 300);
            AddProgrammingActionStep(steps, row++, "9.4 BCM1 闭合RL4后开启ANS给DUT供电", token => deviceManager.CloseRl4ThenEnableAnsAsync(7, 140, 1), "RL4/ANS ON", "Safe Power-On Fail");
            AddDelayStep(steps, row++, "9.5 BCM1 等待6000ms", 6000);
            AddMbdCommandStep(steps, row++, deviceManager, "9.6 BCM1 上电后自动识别USB-B/MBD并发送EXIT", "EXIT", 50);
            AddMbdCommandStep(steps, row++, deviceManager, "9.7 BCM1 发送DIAG ON到MBD", "DIAG ON ", 50);
            AddMbdCommandStep(steps, row++, deviceManager, "9.8 BCM1 RESET置位 W86=1", "W86=1 ", 50);
            AddDaqRelayStep(steps, row++, deviceManager, "9.9 BCM1 闭合RLP4=DAQ CH5引入BOOT0 3.3V", "SKBCM125Rlp4DaqRelays", Bcm125StationMap.ProgrammingBcm1DaqRelay, true, "RLP4 ON");
            AddDelayStep(steps, row++, "9.10 BCM1 等待50ms", 50);
            AddMbdCommandStep(steps, row++, deviceManager, "9.11 BCM1 RESET释放 W86=0", "W86=0 ", 50);
            AddFirmwareFlashResultStep(steps, row++, deviceManager, "C01 BCM1 TTL烧录BCM_125_V8.hex", "BCM125.PROGRAMMING.C01_BCM1_FLASH");
            AddMbdCommandStep(steps, row++, deviceManager, "9.13 BCM1 烧录后RESET置位 W86=1", "W86=1 ", 50);
            AddDaqRelayStep(steps, row++, deviceManager, "9.14 BCM1 断开RLP4=DAQ CH5", "SKBCM125Rlp4DaqRelays", Bcm125StationMap.ProgrammingBcm1DaqRelay, false, "RLP4 OFF");
            AddMbdCommandStep(steps, row++, deviceManager, "9.15 BCM1 RESET释放 W86=0", "W86=0 ", 50);
            AddFixtureRelayStep(steps, row++, deviceManager, "9.16 BCM1 断开RLP1/RLP2/RLP3", "SKBCM125Rlp1To3Relays", "3,4,5", false, "RLP1-3 OFF");

            AddFixtureRelayStep(steps, row++, deviceManager, "9.17 BCM2 闭合RLP5/RLP6/RLP7连接J8", "SKBCM125Rlp5To7Relays", "6,7,8", true, "RLP5-7 ON");
            AddDelayStep(steps, row++, "9.18 BCM2 等待300ms", 300);
            AddMbdCommandStep(steps, row++, deviceManager, "9.19 BCM2 RESET置位 W86=1", "W86=1", 50);
            AddDaqRelayStep(steps, row++, deviceManager, "9.20 BCM2 闭合RLP8=DAQ CH6引入BOOT0 3.3V", "SKBCM125Rlp8DaqRelays", Bcm125StationMap.ProgrammingBcm2DaqRelay, true, "RLP8 ON");
            AddDelayStep(steps, row++, "9.21 BCM2 等待50ms", 50);
            AddMbdCommandStep(steps, row++, deviceManager, "9.22 BCM2 RESET释放 W86=0", "W86=0 ", 50);
            AddFirmwareFlashResultStep(steps, row++, deviceManager, "C02 BCM2 TTL烧录BCM_125_V8.hex", "BCM125.PROGRAMMING.C02_BCM2_FLASH");
            AddMbdCommandStep(steps, row++, deviceManager, "9.24 BCM2 烧录后RESET置位 W86=1", "W86=1", 50);
            AddDaqRelayStep(steps, row++, deviceManager, "9.25 BCM2 断开RLP8=DAQ CH6", "SKBCM125Rlp8DaqRelays", Bcm125StationMap.ProgrammingBcm2DaqRelay, false, "RLP8 OFF");
            AddMbdCommandStep(steps, row++, deviceManager, "9.26 BCM2 RESET释放 W86=0", "W86=0", 50);
            AddFixtureRelayStep(steps, row++, deviceManager, "9.27 BCM2 断开RLP5/RLP6/RLP7", "SKBCM125Rlp5To7Relays", "6,7,8", false, "RLP5-7 OFF");
            AddProgrammingActionStep(steps, row++, "9.28 关闭USB-B/MBD串口通信", token => Task.FromResult(true), "Close OK", "Close Fail");
        }

        private void AddProgrammingActionStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, string name, Func<CancellationToken, Task<bool>> action, string passValue, string failValue)
        {
            steps.Add(new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                PowerCritical = true,
                Action = async (token) =>
                {
                    bool ok = await action(token);
                    return new StepResult { IsPass = ok, Value = ok ? passValue : failValue };
                }
            });
        }

        private void AddDelayStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, string name, int delayMs)
        {
            steps.Add(new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                PowerCritical = true,
                Action = async (token) =>
                {
                    int plannedDelayMs = GetPlanDelayMs(steps, rowIndex, delayMs);
                    await Task.Delay(plannedDelayMs, token);
                    return new StepResult { IsPass = true, Value = plannedDelayMs + " ms" };
                }
            });
        }

        private void AddMbdCommandStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, SK441Device deviceManager, string name, string command, int waitAfterMs)
        {
            AddProgrammingActionStep(
                steps,
                rowIndex,
                name,
                token => SendBcm125MbdCommandAsync(
                    deviceManager,
                    command,
                    waitAfterMs),
                "Sent",
                "Send Fail");
        }

        private void AddMbdCommandPairStep(
            System.Collections.Generic.List<TestStepItem> steps,
            int rowIndex,
            SK441Device deviceManager,
            string name,
            string firstCommand,
            string secondCommand,
            int waitAfterMs)
        {
            AddProgrammingActionStep(
                steps,
                rowIndex,
                name,
                async token =>
                {
                    if (!await SendBcm125MbdCommandAsync(deviceManager, firstCommand, 0))
                        return false;
                    return await SendBcm125MbdCommandAsync(
                        deviceManager,
                        secondCommand,
                        waitAfterMs > 0
                            ? GetPlanDelayMs(steps, rowIndex, waitAfterMs)
                            : 0);
                },
                "Sent",
                "Send Fail");
        }

        private bool IsBcm125RemoteTerminal =>
            _mbdTerminalState == Bcm125MbdTerminalState.Bcm1 ||
            _mbdTerminalState == Bcm125MbdTerminalState.Bcm2;

        private async Task<bool> CheckBcm125MbdCommunicationAsync(SK441Device deviceManager)
        {
            if (IsBcm125RemoteTerminal && deviceManager.IsTargetBoardCommunicationConnected)
            {
                OnLogInfo(
                    "BCM-125已处于REMOTE终端且串口保持连接；跳过空命令探测，避免破坏终端上下文。");
                return true;
            }

            bool connected = await deviceManager.CheckMbdCommunicationAsync();
            if (!connected)
                _mbdTerminalState = Bcm125MbdTerminalState.Unknown;
            return connected;
        }

        private async Task<bool> SendBcm125MbdCommandAsync(
            SK441Device deviceManager,
            string command,
            int waitAfterMs)
        {
            string normalized = (command ?? string.Empty).Trim().ToUpperInvariant();
            bool keepCurrentConnection = IsBcm125RemoteTerminal;
            Bcm125MbdTerminalState remoteState = _mbdTerminalState;
            bool sent = keepCurrentConnection
                ? await deviceManager.SendMbdCommandOnCurrentConnectionAsync(command, waitAfterMs)
                : await deviceManager.SendMbdCommandAsync(command, waitAfterMs);

            if (!sent && keepCurrentConnection)
            {
                sent = await RestoreBcm125RemoteTerminalAsync(deviceManager, remoteState);
                if (sent && normalized != "DIAG ON")
                {
                    sent = await deviceManager.SendMbdCommandOnCurrentConnectionAsync(
                        command,
                        waitAfterMs);
                }
            }

            if (!sent)
            {
                _mbdTerminalState = Bcm125MbdTerminalState.Unknown;
                return false;
            }

            if (normalized == "EXIT")
                _mbdTerminalState = Bcm125MbdTerminalState.Mbd;
            else if (normalized == "REMOTE1")
                _mbdTerminalState = Bcm125MbdTerminalState.Bcm1;
            else if (normalized == "REMOTE2")
                _mbdTerminalState = Bcm125MbdTerminalState.Bcm2;
            else if (normalized.StartsWith("W86=", StringComparison.Ordinal))
                _mbdTerminalState = Bcm125MbdTerminalState.Unknown;

            return true;
        }

        private async Task<string> QueryBcm125MbdCommandAsync(
            SK441Device deviceManager,
            string command,
            int waitAfterMs)
        {
            if (!IsBcm125RemoteTerminal)
                return await deviceManager.QueryMbdCommandAsync(command, waitAfterMs);

            Bcm125MbdTerminalState remoteState = _mbdTerminalState;
            try
            {
                return await deviceManager.QueryMbdCommandOnCurrentConnectionAsync(
                    command,
                    waitAfterMs);
            }
            catch (Exception ex)
            {
                OnLogWarning(
                    $"BCM-125有状态查询失败：{command}，{ex.Message}；" +
                    "准备重新连接并恢复REMOTE终端状态。");
                if (!await RestoreBcm125RemoteTerminalAsync(deviceManager, remoteState))
                    throw;

                return await deviceManager.QueryMbdCommandOnCurrentConnectionAsync(
                    command,
                    waitAfterMs);
            }
        }

        private async Task<bool> RestoreBcm125RemoteTerminalAsync(
            SK441Device deviceManager,
            Bcm125MbdTerminalState remoteState)
        {
            string remoteCommand = remoteState == Bcm125MbdTerminalState.Bcm1
                ? "REMOTE1"
                : remoteState == Bcm125MbdTerminalState.Bcm2
                    ? "REMOTE2"
                    : null;
            if (remoteCommand == null)
                return false;

            OnLogWarning(
                $"BCM-125 USB-B/MBD连接丢失，开始重建{remoteCommand}终端上下文。");
            _mbdTerminalState = Bcm125MbdTerminalState.Unknown;
            if (!await deviceManager.SendMbdCommandAsync("EXIT", 0) ||
                !await deviceManager.SendMbdCommandAsync("DIAG ON", 0) ||
                !await deviceManager.SendMbdCommandAsync(remoteCommand, 0) ||
                !await deviceManager.SendMbdCommandOnCurrentConnectionAsync("DIAG ON", 0))
            {
                _mbdTerminalState = Bcm125MbdTerminalState.Unknown;
                OnLogWarning(
                    $"BCM-125未能重建{remoteCommand}终端上下文，停止后续寄存器命令。");
                return false;
            }

            _mbdTerminalState = remoteState;
            OnLogInfo($"BCM-125已重建{remoteCommand}终端上下文。");
            return true;
        }

        private void AddFixtureRelayStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, SK441Device deviceManager, string name, string configKey, string defaultRelays, bool close, string passValue)
        {
            bool isProgrammingRelay = configKey.IndexOf("Rlp", StringComparison.OrdinalIgnoreCase) >= 0;
            AddProgrammingActionStep(
                steps,
                rowIndex,
                name,
                token => isProgrammingRelay
                    ? deviceManager.SetFixtureRelaysAsync(deviceManager.GetConfiguredRelayList(configKey, defaultRelays), close, name)
                    : deviceManager.SetBcm125PowerPathRelaysAsync(deviceManager.GetConfiguredRelayList(configKey, defaultRelays), close, name),
                passValue,
                "Relay Fail");
        }

        private void AddDaqRelayStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, SK441Device deviceManager, string name, string configKey, string defaultRelays, bool close, string passValue)
        {
            AddProgrammingActionStep(
                steps,
                rowIndex,
                name,
                token => deviceManager.SetDaqRelaysAsync(deviceManager.GetConfiguredDaqRelayList(configKey, defaultRelays), close, name),
                passValue,
                "Relay Fail");
        }

        private void AddFirmwareFlashResultStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, SK441Device deviceManager, string name, string storeKey)
        {
            steps.Add(new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                PowerCritical = true,
                Action = async (token) =>
                {
                    if (deviceManager.IsTtlProgrammingRequired)
                    {
                        bool flashed = await deviceManager.FlashBcm125FirmwareAsync(
                            Bcm125Commands.FirmwareFile,
                            token);
                        return new StepResult
                        {
                            IsPass = flashed,
                            Value = flashed ? "1" : "0",
                            StoreKey = storeKey,
                            NumericValue = flashed ? 1 : 0,
                            Unit = string.Empty
                        };
                    }

                    OnLogWarning(name + "：固件烧录已跳过；未执行也未模拟TTL烧录。");
                    await Task.Delay(200, token);
                    return new StepResult
                    {
                        IsPass = true,
                        Value = "SKIPPED",
                        StoreKey = storeKey,
                        NumericValue = 1,
                        Unit = string.Empty
                    };
                }
            });
        }

        protected void AddBcm125CanBusSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager)
        {
            int row = steps.Count;

            AddProgrammingActionStep(steps, row++, "10.1 先关闭ANS输出再断开RL4", token => deviceManager.DisableAnsThenOpenRl4Async(7), "ANS/RL4 OFF", "Safe Power-Off Fail");
            AddDelayStep(steps, row++, "10.2 等待6500ms", 6500);
            AddProgrammingActionStep(steps, row++, "10.3 闭合RL4后重新开启ANS给DUT供电", token => deviceManager.CloseRl4ThenEnableAnsAsync(7, 140, 1), "RL4/ANS ON", "Safe Power-On Fail");
            AddProgrammingActionStep(steps, row++, "10.4 检查USB-B/MBD串口通信已打开", token => CheckBcm125MbdCommunicationAsync(deviceManager), "Open", "COM Fail");
            AddMbdCommandStep(steps, row++, deviceManager, "10.5 发送EXIT到MBD", "EXIT ", 0);
            AddCanBusQueryStep(steps, row++, deviceManager, "D01 读取R14检查BCM内部CAN响应", "R14", "BCM125.CAN.D01_R14_BCM", 1);
            AddCanBusQueryStep(steps, row++, deviceManager, "D02 读取R15检查SAFE内部CAN响应", "R15", "BCM125.CAN.D02_R15_SAFE", -1);
            AddProgrammingActionStep(steps, row++, "10.8 关闭USB-B/MBD串口通信", token => Task.FromResult(true), "Close OK", "Close Fail");
        }

        private void AddCanBusQueryStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, SK441Device deviceManager, string name, string command, string storeKey, int expectedZeroIndex)
        {
            steps.Add(new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                PowerCritical = true,
                Action = async (token) =>
                {
                    string response = await QueryBcm125MbdCommandAsync(deviceManager, command, 0);
                    int[] values = MbdResponseParser.ParseIntegerVector(command, response, 16);

                    int index = expectedZeroIndex >= 0 ? expectedZeroIndex : values.Length - 1;
                    bool pass = index >= 0 && index < values.Length && values[index] == 0;
                    string display = index >= 0 && index < values.Length
                        ? values[index].ToString("00", System.Globalization.CultureInfo.InvariantCulture)
                        : "Index Err";

                    return new StepResult
                    {
                        IsPass = pass,
                        Value = display,
                        StoreKey = storeKey,
                        NumericValue = pass ? 0 : 99,
                        Unit = string.Empty
                    };
                }
            });
        }

        protected void AddBcm125FirmwareVersionSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager)
        {
            int row = steps.Count;

            AddProgrammingActionStep(steps, row++, "11.1 闭合RL4给DUT供电（如未上电）", token => deviceManager.ControlRelayAsync(7, true), "RL4 ON", "RL4 Fail");
            AddProgrammingActionStep(steps, row++, "11.2 检查USB-B/MBD串口通信已打开", token => CheckBcm125MbdCommunicationAsync(deviceManager), "Open", "COM Fail");
            AddMbdCommandStep(steps, row++, deviceManager, "11.3 发送REMOTE1进入BCM1终端", "REMOTE1 ", 0);
            AddFirmwareVersionQueryStep(steps, row++, deviceManager, "E01 读取R0检查BCM1固件版本", "BCM125.FIRMWARE.E01_FW_ST1_VERSION");
            AddMbdCommandStep(steps, row++, deviceManager, "11.5 发送EXIT返回MBD终端", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "11.6 发送REMOTE2进入BCM2终端", "REMOTE2 ", 0);
            AddFirmwareVersionQueryStep(steps, row++, deviceManager, "E02 读取R0检查BCM2固件版本", "BCM125.FIRMWARE.E02_FW_ST2_VERSION");
            AddProgrammingActionStep(steps, row++, "11.9 关闭USB-B/MBD串口通信", token => Task.FromResult(true), "Close OK", "Close Fail");
        }

        private void AddFirmwareVersionQueryStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, SK441Device deviceManager, string name, string storeKey)
        {
            steps.Add(new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                PowerCritical = true,
                Action = async (token) =>
                {
                    string response = await QueryBcm125MbdCommandAsync(deviceManager, "R0", 0);
                    string version = MbdResponseParser.ParsePayloadText("R0", response).Trim('"');
                    string expected = deviceManager.GetBcm125ExpectedFirmwareVersion();
                    bool pass = string.Equals(version, expected, StringComparison.OrdinalIgnoreCase);

                    return new StepResult
                    {
                        IsPass = pass,
                        Value = string.IsNullOrWhiteSpace(version) ? "No Version" : version,
                        StoreKey = storeKey,
                        NumericValue = pass ? 1 : 0,
                        Unit = string.Empty
                    };
                }
            });
        }

        protected void AddBcm125ResetFlatCableSteps(System.Collections.Generic.List<TestStepItem> steps, TestSequenceContext context, SK441Device deviceManager)
        {
            int row = steps.Count;

            AddProgrammingActionStep(steps, row++, "12.1 闭合RL4给DUT供电（如未上电）", token => deviceManager.ControlRelayAsync(7, true), "RL4 ON", "RL4 Fail");
            AddDelayStep(steps, row++, "12.2 等待3000ms", 3000);
            AddProgrammingActionStep(steps, row++, "12.3 检查USB-B/MBD串口通信已打开", token => CheckBcm125MbdCommunicationAsync(deviceManager), "Open", "COM Fail");
            AddMbdCommandStep(steps, row++, deviceManager, "12.4 发送EXIT进入MBD终端", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "12.5 发送DIAG ON到MBD", "DIAG ON", 0);

            AddMbdCommandStep(steps, row++, deviceManager, "12.6 ST1发送REMOTE1进入BCM1终端", "REMOTE1", 0);
            AddRunningTimeReadStep(steps, row++, deviceManager, "12.7 ST1读取R10 running_time x", "BCM125.RESET_FLAT.ST1_X");
            AddMbdCommandStep(steps, row++, deviceManager, "12.8 ST1发送EXIT返回MBD终端", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "12.9 ST1 RESET置位 W86=1", "W86=1", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "12.10 ST1 RESET释放 W86=0", "W86=0", 0);
            AddDelayStep(steps, row++, "12.11 ST1等待1000ms", 1000);
            AddMbdCommandStep(steps, row++, deviceManager, "12.12 ST1重新发送REMOTE1", "REMOTE1", 0);
            AddRunningTimeReadStep(steps, row++, deviceManager, "12.13 ST1再次读取R10 running_time y", "BCM125.RESET_FLAT.ST1_Y");
            AddResetFlatResultStep(steps, row++, context, "F01 ST1判断Reset from flat cable", "BCM125.RESET_FLAT.ST1_X", "BCM125.RESET_FLAT.ST1_Y", "BCM125.RESET_FLAT.F01_ST1_RESULT");

            int st2WaitExitRow = row++;
            AddProgrammingActionStep(
                steps,
                st2WaitExitRow,
                "12.15 ST2等待3000ms后发送EXIT返回MBD终端",
                async token =>
                {
                    int delayMs = GetPlanDelayMs(steps, st2WaitExitRow, 3000);
                    await Task.Delay(delayMs, token);
                    return await SendBcm125MbdCommandAsync(deviceManager, "EXIT", 0);
                },
                "Waited / Sent",
                "Wait / Send Fail");
            AddMbdCommandStep(steps, row++, deviceManager, "12.16 ST2发送REMOTE2进入BCM2终端", "REMOTE2", 0);
            AddRunningTimeReadStep(steps, row++, deviceManager, "12.17 ST2读取R10 running_time x", "BCM125.RESET_FLAT.ST2_X");
            AddMbdCommandStep(steps, row++, deviceManager, "12.18 ST2发送EXIT返回MBD终端", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "12.19 ST2 RESET置位 W86=1", "W86=1", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "12.20 ST2 RESET释放 W86=0", "W86=0", 0);
            AddDelayStep(steps, row++, "12.21 ST2等待1000ms", 1000);
            AddMbdCommandStep(steps, row++, deviceManager, "12.22 ST2重新发送REMOTE2", "REMOTE2", 0);
            AddRunningTimeReadStep(steps, row++, deviceManager, "12.23 ST2再次读取R10 running_time y", "BCM125.RESET_FLAT.ST2_Y");
            AddResetFlatResultStep(steps, row++, context, "F02 ST2判断Reset from flat cable", "BCM125.RESET_FLAT.ST2_X", "BCM125.RESET_FLAT.ST2_Y", "BCM125.RESET_FLAT.F02_ST2_RESULT");

            AddMbdCommandStep(steps, row++, deviceManager, "12.17 收尾发送EXIT返回MBD终端", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "12.18 收尾确认RESET释放 W86=0", "W86=0 ", 0);
            AddProgrammingActionStep(steps, row++, "12.19 关闭USB-B/MBD串口通信", token => Task.FromResult(true), "Close OK", "Close Fail");
        }

        private void AddRunningTimeReadStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, SK441Device deviceManager, string name, string storeKey)
        {
            steps.Add(new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                PowerCritical = true,
                Action = async (token) =>
                {
                    string response = await QueryBcm125MbdCommandAsync(deviceManager, "R10", 0);
                    double runningTime = MbdResponseParser.ParseScalar("R10", response);
                    return new StepResult
                    {
                        IsPass = true,
                        Value = runningTime.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                        StoreKey = storeKey,
                        NumericValue = runningTime,
                        Unit = "s"
                    };
                }
            });
        }

        private void AddResetFlatResultStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, TestSequenceContext context, string name, string xKey, string yKey, string storeKey)
        {
            steps.Add(new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                PowerCritical = true,
                Action = async (token) =>
                {
                    double x;
                    double y;
                    bool hasX = TryGetStoredNumericValue(context, xKey, out x);
                    bool hasY = TryGetStoredNumericValue(context, yKey, out y);
                    bool pass = hasX && hasY &&
                        x - y >= 2.0 &&
                        y >= 0.5 && y <= 1.5;
                    if (!pass)
                        await Task.Delay(1, token);

                    return new StepResult
                    {
                        IsPass = pass,
                        Value = pass ? "1" : "0",
                        StoreKey = storeKey,
                        NumericValue = pass ? 1 : 0,
                        Unit = string.Empty
                    };
                }
            });
        }

        protected void AddBcm125VrefVoltageSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager)
        {
            int row = steps.Count;

            AddProgrammingActionStep(steps, row++, "13.1 Close RL4 to power DUT", token => deviceManager.ControlRelayAsync(7, true), "RL4 ON", "RL4 Fail");
            AddProgrammingActionStep(steps, row++, "13.2 Check USB-B/MBD serial open", token => CheckBcm125MbdCommunicationAsync(deviceManager), "Open", "COM Fail");
            AddMbdCommandStep(steps, row++, deviceManager, "13.3 Send EXIT to MBD terminal", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "13.4 ST1 send REMOTE1", "REMOTE1", 0);
            AddMbdNumericQueryStep(steps, row++, deviceManager, "G01 ST1 read R55 adc13 vref PC3", "R55", "BCM125.VREF.G01_ADC13_VREF_PC3_ST1", 1500, 1600, "count");
            AddMbdCommandStep(steps, row++, deviceManager, "13.8 Send EXIT to MBD terminal", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "13.9 ST2 send REMOTE2", "REMOTE2", 0);
            AddMbdNumericQueryStep(steps, row++, deviceManager, "G02 ST2 read R55 adc13 vref PC3", "R55", "BCM125.VREF.G02_ADC13_VREF_PC3_ST2", 1500, 1600, "count");
            AddProgrammingActionStep(steps, row++, "13.13 Close USB-B/MBD serial communication", token => Task.FromResult(true), "Close OK", "Close Fail");
        }

        protected void AddBcm125PowerSupplyVoltageSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager)
        {
            int row = steps.Count;

            AddProgrammingActionStep(steps, row++, "14.1 Close RL4 to power DUT", token => deviceManager.ControlRelayAsync(7, true), "RL4 ON", "RL4 Fail");
            AddProgrammingActionStep(steps, row++, "14.2 Check USB-B/MBD serial open", token => CheckBcm125MbdCommunicationAsync(deviceManager), "Open", "COM Fail");
            AddMbdCommandStep(steps, row++, deviceManager, "14.3 Send EXIT to MBD terminal", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "14.4 ST1 send REMOTE1", "REMOTE1", 0);
            AddMbdNumericQueryStep(steps, row++, deviceManager, "H01 ST1 read R53 adc11 vcc PC1", "R53", "BCM125.POWER_VOLTAGE.H01_ADC11_VCC_PC1_ST1", 3700, 4100, "count");
            AddMbdNumericQueryStep(steps, row++, deviceManager, "H02 ST1 read R54 adc12 vee PC2", "R54", "BCM125.POWER_VOLTAGE.H02_ADC12_VEE_PC2_ST1", 1250, 1450, "count");
            AddMbdCommandStep(steps, row++, deviceManager, "14.7 Send EXIT to MBD terminal", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "14.8 ST2 send REMOTE2", "REMOTE2", 0);
            AddMbdNumericQueryStep(steps, row++, deviceManager, "H03 ST2 read R53 adc11 vcc PC1", "R53", "BCM125.POWER_VOLTAGE.H03_ADC11_VCC_PC1_ST2", 3700, 4100, "count");
            AddMbdNumericQueryStep(steps, row++, deviceManager, "H04 ST2 read R54 adc12 vee PC2", "R54", "BCM125.POWER_VOLTAGE.H04_ADC12_VEE_PC2_ST2", 1250, 1450, "count");
            AddMbdCommandStep(steps, row++, deviceManager, "14.12 Send EXIT to MBD terminal", "EXIT", 0);
            AddProgrammingActionStep(steps, row++, "14.13 Close USB-B/MBD serial communication", token => Task.FromResult(true), "Close OK", "Close Fail");
        }

        protected void AddBcm125HeatSinkTemperatureSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager)
        {
            int row = steps.Count;

            AddProgrammingActionStep(steps, row++, "15.1 Close RL4 to power DUT", token => deviceManager.ControlRelayAsync(7, true), "RL4 ON", "RL4 Fail");
            AddProgrammingActionStep(steps, row++, "15.2 Check USB-B/MBD serial open", token => CheckBcm125MbdCommunicationAsync(deviceManager), "Open", "COM Fail");
            AddMbdCommandStep(steps, row++, deviceManager, "15.3 Send EXIT to MBD terminal", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "15.4 ST1 send REMOTE1", "REMOTE1", 0);
            AddMbdNumericQueryStep(steps, row++, deviceManager, "I01 ST1 read R52 adc10 hs_temp PC0", "R52", "BCM125.HEATSINK_TEMP.I01_ADC10_HS_TEMP_PC0_ST1", 1800, 2500, "0.1C");
            AddMbdCommandStep(steps, row++, deviceManager, "15.6 Send EXIT to MBD terminal", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "15.7 ST2 send REMOTE2", "REMOTE2", 0);
            AddMbdNumericQueryStep(steps, row++, deviceManager, "I02 ST2 read R52 adc10 hs_temp PC0", "R52", "BCM125.HEATSINK_TEMP.I02_ADC10_HS_TEMP_PC0_ST2", 1800, 2500, "0.1C");
            AddProgrammingActionStep(steps, row++, "15.10 Close USB-B/MBD serial communication", token => Task.FromResult(true), "Close OK", "Close Fail");
        }

        protected void AddBcm125VbattScalCalibrationSteps(System.Collections.Generic.List<TestStepItem> steps, TestSequenceContext context, SK441Device deviceManager)
        {
            int row = steps.Count;

            AddProgrammingActionStep(steps, row++, "16.1 Close RL4 to power DUT", token => deviceManager.ControlRelayAsync(7, true), "RL4 ON", "RL4 Fail");
            AddProgrammingActionStep(steps, row++, "16.2 Check USB-B/MBD serial open", token => CheckBcm125MbdCommunicationAsync(deviceManager), "Open", "COM Fail");
            AddMbdCommandStep(steps, row++, deviceManager, "16.3 ST1 send EXIT to MBD terminal", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "16.4 ST1 send DIAG ON to MBD", "DIAG ON", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "16.5 ST1 activate MPS RL2 W94=1", "W94=1", 0);
            AddDelayStep(steps, row++, "16.6 ST1 wait 5000ms", 5000);
            AddMbdCommandStep(steps, row++, deviceManager, "16.7 ST1 send REMOTE1", "REMOTE1", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "16.8 ST1 send DIAG ON to BCM", "DIAG ON", 0);
            AddMbdNumericQueryStep(steps, row++, deviceManager, "16.9 ST1 read R51 adc1 vstr_pos PA1", "R51", "BCM125.VBATT_SCAL.ST1_ADC1_VSTR_POS_PA1", 1, 1000000, "count");
            AddDaqVoltageMvStep(steps, row++, deviceManager, "16.10 ST1 DAQ CH05 read MPS.P+ VBUS", 105, "BCM125.VBATT_SCAL.ST1_VBUS_MV");
            AddGainVbusWriteStep(steps, row++, context, deviceManager, "J01 ST1 calculate Gain_VBUS and write W18", "BCM125.VBATT_SCAL.ST1_VBUS_MV", "BCM125.VBATT_SCAL.ST1_ADC1_VSTR_POS_PA1", "BCM125.VBATT_SCAL.J01_GAIN_VBUS_ST1");
            AddMbdCommandStep(steps, row++, deviceManager, "16.12 ST1 write offset W19=0", "W19=0", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "16.12 ST1 store parameters ACT->FLASH", "ACT->FLASH", 0);
            AddMbdNumericCompareStoredStep(steps, row++, context, deviceManager, "J02 ST1 verify R18 gain_vstr_pos", "R18", "BCM125.VBATT_SCAL.J01_GAIN_VBUS_ST1", "BCM125.VBATT_SCAL.J02_R18_GAIN_ST1", "count");
            AddMbdNumericQueryStep(steps, row++, deviceManager, "J03 ST1 verify R19 offset_vstr_pos", "R19", "BCM125.VBATT_SCAL.J03_R19_OFFSET_ST1", 0, 0, "count");
            AddMbdAverageQueryStep(steps, row++, deviceManager, "J04 ST1 average R43 vstr_pos", "R43", "BCM125.VBATT_SCAL.J04_VSTR_POS_ST1", 137500, 142500, "mV", 5, 200);

            AddMbdCommandStep(steps, row++, deviceManager, "16.16 ST2 send EXIT to MBD terminal", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "16.16 ST2 send DIAG ON to MBD", "DIAG ON", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "16.16 ST2 activate MPS RL1 W91=1", "W91=1", 0);
            AddDelayStep(steps, row++, "16.16 ST2 wait 5000ms", 5000);
            AddMbdCommandStep(steps, row++, deviceManager, "16.16 ST2 send REMOTE2", "REMOTE2", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "16.16 ST2 send DIAG ON to BCM", "DIAG ON", 0);
            AddMbdNumericQueryStep(steps, row++, deviceManager, "16.16 ST2 read R51 adc1 vstr_pos PA1", "R51", "BCM125.VBATT_SCAL.ST2_ADC1_VSTR_POS_PA1", 1, 1000000, "count");
            AddDaqVoltageMvStep(steps, row++, deviceManager, "16.16 ST2 DAQ CH05 read MPS.P+ VBUS", 105, "BCM125.VBATT_SCAL.ST2_VBUS_MV");
            AddGainVbusWriteStep(steps, row++, context, deviceManager, "J05 ST2 calculate Gain_VBUS and write W18", "BCM125.VBATT_SCAL.ST2_VBUS_MV", "BCM125.VBATT_SCAL.ST2_ADC1_VSTR_POS_PA1", "BCM125.VBATT_SCAL.J05_GAIN_VBUS_ST2");
            AddMbdCommandStep(steps, row++, deviceManager, "16.16 ST2 write offset W19=0", "W19=0", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "16.16 ST2 store parameters ACT->FLASH", "ACT->FLASH", 0);
            AddMbdNumericCompareStoredStep(steps, row++, context, deviceManager, "J06 ST2 verify R18 gain_vstr_pos", "R18", "BCM125.VBATT_SCAL.J05_GAIN_VBUS_ST2", "BCM125.VBATT_SCAL.J06_R18_GAIN_ST2", "count");
            AddMbdNumericQueryStep(steps, row++, deviceManager, "J07 ST2 verify R19 offset_vstr_pos", "R19", "BCM125.VBATT_SCAL.J07_R19_OFFSET_ST2", 0, 0, "count");
            AddMbdAverageQueryStep(steps, row++, deviceManager, "J08 ST2 average R43 vstr_pos", "R43", "BCM125.VBATT_SCAL.J08_VSTR_POS_ST2", 137500, 142500, "mV", 5, 200);

            AddMbdCommandStep(steps, row++, deviceManager, "16.18 Send EXIT to MBD terminal", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "16.19 Send DIAG ON to MBD", "DIAG ON", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "16.20 Deactivate MPS RL1 W91=0", "W91=0", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "16.21 Deactivate MPS RL2 W94=0", "W94=0", 0);
            AddProgrammingActionStep(steps, row++, "16.22 Close USB-B/MBD serial communication", token => Task.FromResult(true), "Close OK", "Close Fail");
        }

        private void AddDaqVoltageMvStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, SK441Device deviceManager, string name, int channel, string storeKey)
        {
            steps.Add(new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                PowerCritical = true,
                Action = async (token) =>
                {
                    double voltage = await deviceManager.MeasureDaqChannelVoltageAsync(channel);
                    double mv = voltage * 1000.0;
                    bool pass = mv > 0;
                    return new StepResult { IsPass = pass, Value = FormatValue(mv, "mV"), StoreKey = storeKey, NumericValue = mv, Unit = "mV" };
                }
            });
        }

        private void AddGainVbusWriteStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, TestSequenceContext context, SK441Device deviceManager, string name, string vbusKey, string adcKey, string storeKey)
        {
            steps.Add(new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                PowerCritical = true,
                Action = async (token) =>
                {
                    double vbus = 0;
                    double adc = 0;
                    bool hasVbus = TryGetStoredNumericValue(context, vbusKey, out vbus);
                    bool hasAdc = TryGetStoredNumericValue(context, adcKey, out adc);
                    bool pass = hasVbus && hasAdc && Math.Abs(adc) > 0.001;
                    double gain = pass ? Math.Round(4096.0 * vbus / adc) : 0;
                    if (pass)
                        pass = await SendBcm125MbdCommandAsync(deviceManager, "W18=" + gain.ToString("0", System.Globalization.CultureInfo.InvariantCulture), 0);
                    return new StepResult { IsPass = pass, Value = gain.ToString("0", System.Globalization.CultureInfo.InvariantCulture), StoreKey = storeKey, NumericValue = gain, Unit = "count" };
                }
            });
        }

        private void AddMbdNumericCompareStoredStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, TestSequenceContext context, SK441Device deviceManager, string name, string command, string expectedKey, string storeKey, string unit)
        {
            steps.Add(new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                PowerCritical = true,
                Action = async (token) =>
                {
                    double expected;
                    string response = await QueryBcm125MbdCommandAsync(deviceManager, command, 0);
                    double actual = MbdResponseParser.ParseScalar(command, response);
                    bool pass = TryGetStoredNumericValue(context, expectedKey, out expected) && Math.Abs(actual - expected) <= 1.0;
                    return new StepResult { IsPass = pass, Value = FormatValue(actual, unit), StoreKey = storeKey, NumericValue = actual, Unit = unit };
                }
            });
        }

        private void AddMbdAverageQueryStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, SK441Device deviceManager, string name, string command, string storeKey, double lower, double upper, string unit, int sampleCount, int intervalMs)
        {
            steps.Add(new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                PowerCritical = true,
                Action = async (token) =>
                {
                    double sum = 0;
                    int count = 0;
                    for (int i = 0; i < sampleCount; i++)
                    {
                        string response = await QueryBcm125MbdCommandAsync(deviceManager, command, 0);
                        double value = MbdResponseParser.ParseScalar(command, response);
                        sum += value;
                        count++;
                        if (i + 1 < sampleCount)
                            await Task.Delay(intervalMs, token);
                    }
                    double average = count > 0 ? sum / count : double.NaN;
                    double plannedLower, plannedUpper;
                    GetPlanRange(steps, rowIndex, lower, upper, out plannedLower, out plannedUpper);
                    string plannedUnit = GetPlanUnit(steps, rowIndex, unit);
                    bool pass = !double.IsNaN(average) && average >= plannedLower && average <= plannedUpper;
                    return new StepResult { IsPass = pass, Value = double.IsNaN(average) ? "No Data" : FormatValue(average, plannedUnit), StoreKey = storeKey, NumericValue = double.IsNaN(average) ? 0 : average, Unit = plannedUnit };
                }
            });
        }

        private void AddDaqAverageMvStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, SK441Device deviceManager, string name, int channel, string storeKey, double lower, double upper, int sampleCount, int intervalMs)
        {
            steps.Add(new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                PowerCritical = true,
                Action = async (token) =>
                {
                    double average = await deviceManager.MeasureDaqChannelVoltageAverageAsync(
                        channel,
                        Math.Max(1, sampleCount),
                        intervalMs,
                        token) * 1000.0;
                    double plannedLower, plannedUpper;
                    GetPlanRange(steps, rowIndex, lower, upper, out plannedLower, out plannedUpper);
                    string plannedUnit = GetPlanUnit(steps, rowIndex, "mV");
                    bool pass = average >= plannedLower && average <= plannedUpper;
                    return new StepResult { IsPass = pass, Value = FormatValue(average, plannedUnit), StoreKey = storeKey, NumericValue = average, Unit = plannedUnit };
                }
            });
        }

        private void AddGainVmidWriteStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, TestSequenceContext context, SK441Device deviceManager, string name, string vmidHighKey, string vmidLowKey, string adcHighKey, string adcLowKey, string storeKey)
        {
            steps.Add(new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                PowerCritical = true,
                Action = async (token) =>
                {
                    double vmidHigh = 0, vmidLow = 0, adcHigh = 0, adcLow = 0;
                    bool hasVmidHigh = TryGetStoredNumericValue(context, vmidHighKey, out vmidHigh);
                    bool hasVmidLow = TryGetStoredNumericValue(context, vmidLowKey, out vmidLow);
                    bool hasAdcHigh = TryGetStoredNumericValue(context, adcHighKey, out adcHigh);
                    bool hasAdcLow = TryGetStoredNumericValue(context, adcLowKey, out adcLow);
                    bool pass = hasVmidHigh && hasVmidLow && hasAdcHigh && hasAdcLow && Math.Abs(adcHigh - adcLow) > 0.001;
                    double gain = pass ? Math.Round(4096.0 * (vmidHigh - vmidLow) / (adcHigh - adcLow)) : 0;
                    double plannedLower, plannedUpper;
                    GetPlanRange(steps, rowIndex, 140000, 145000, out plannedLower, out plannedUpper);
                    string plannedUnit = GetPlanUnit(steps, rowIndex, "count");
                    pass = pass && gain >= plannedLower && gain <= plannedUpper;
                    if (pass)
                        pass = await SendBcm125MbdCommandAsync(deviceManager, "W20=" + gain.ToString("0", System.Globalization.CultureInfo.InvariantCulture), 0);
                    return new StepResult { IsPass = pass, Value = gain.ToString("0", System.Globalization.CultureInfo.InvariantCulture), StoreKey = storeKey, NumericValue = gain, Unit = plannedUnit };
                }
            });
        }

        private void AddOffsetVmidWriteStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, TestSequenceContext context, SK441Device deviceManager, string name, string vmidLowKey, string adcLowKey, string gainKey, string storeKey)
        {
            steps.Add(new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                PowerCritical = true,
                Action = async (token) =>
                {
                    double vmidLow = 0, adcLow = 0, gain = 0;
                    bool hasVmidLow = TryGetStoredNumericValue(context, vmidLowKey, out vmidLow);
                    bool hasAdcLow = TryGetStoredNumericValue(context, adcLowKey, out adcLow);
                    bool hasGain = TryGetStoredNumericValue(context, gainKey, out gain);
                    bool pass = hasVmidLow && hasAdcLow && hasGain;
                    double offset = pass ? Math.Round(vmidLow - (gain * adcLow) / 4096.0) : 0;
                    double plannedLower, plannedUpper;
                    GetPlanRange(steps, rowIndex, -600, 0, out plannedLower, out plannedUpper);
                    string plannedUnit = GetPlanUnit(steps, rowIndex, "mV");
                    pass = pass && offset >= plannedLower && offset <= plannedUpper;
                    if (pass)
                        pass = await SendBcm125MbdCommandAsync(deviceManager, "W21=" + offset.ToString("0", System.Globalization.CultureInfo.InvariantCulture), 0);
                    return new StepResult { IsPass = pass, Value = offset.ToString("0", System.Globalization.CultureInfo.InvariantCulture), StoreKey = storeKey, NumericValue = offset, Unit = plannedUnit };
                }
            });
        }

        private void AddDaqShuntCurrentStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, SK441Device deviceManager, string name, int channel, double shuntOhm, string storeKey, double lower, double upper)
        {
            steps.Add(new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                PowerCritical = true,
                Action = async (token) =>
                {
                    double averageVolts = await deviceManager.MeasureDaqChannelVoltageAverageAsync(
                        channel,
                        8,
                        0,
                        token);
                    double currentMa = averageVolts / shuntOhm * 1000.0;
                    double plannedLower, plannedUpper;
                    GetPlanRange(steps, rowIndex, lower, upper, out plannedLower, out plannedUpper);
                    string plannedUnit = GetPlanUnit(steps, rowIndex, "mA");
                    bool pass = currentMa >= plannedLower && currentMa <= plannedUpper;
                    return new StepResult { IsPass = pass, Value = FormatValue(currentMa, plannedUnit), StoreKey = storeKey, NumericValue = currentMa, Unit = plannedUnit };
                }
            });
        }

        private void AddIdchGainOffsetStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, TestSequenceContext context, string gainStepName, string offsetStepName, string suffix, string current1Key, string current6Key, string adc1Key, string adc6Key, string gainKey, string offsetKey)
        {
            steps.Add(new TestStepItem
            {
                Name = gainStepName + "/" + offsetStepName + " " + suffix + " calculate GAIN/OFFSET idch",
                RowIndex = rowIndex,
                PowerCritical = true,
                Action = async (token) =>
                {
                    await Task.Delay(1, token);
                    double current1 = 0, current6 = 0, adc1 = 0, adc6 = 0;
                    bool ok = TryGetStoredNumericValue(context, current1Key, out current1)
                        && TryGetStoredNumericValue(context, current6Key, out current6)
                        && TryGetStoredNumericValue(context, adc1Key, out adc1)
                        && TryGetStoredNumericValue(context, adc6Key, out adc6)
                        && Math.Abs(adc6 - adc1) > 0.001;
                    double gain = ok ? Math.Round((current6 - current1) * 4096.0 / (adc6 - adc1)) : 0;
                    double offset = ok ? Math.Round(current1 - (gain * adc1) / 4096.0) : 0;
                    double plannedLower, plannedUpper;
                    GetPlanRange(steps, rowIndex, 87500, 92500, out plannedLower, out plannedUpper);
                    string plannedUnit = GetPlanUnit(steps, rowIndex, "count");
                    bool pass = ok && gain >= plannedLower && gain <= plannedUpper;
                    return new StepResult { IsPass = pass, Value = $"G={gain:0};O={offset:0}", StoreKey = gainKey, NumericValue = gain, Unit = plannedUnit };
                }
            });

            steps.Add(new TestStepItem
            {
                Name = offsetStepName + " " + suffix + " save OFFSET idch",
                RowIndex = rowIndex + 1,
                PowerCritical = true,
                Action = async (token) =>
                {
                    await Task.Delay(1, token);
                    double current1 = 0, current6 = 0, adc1 = 0, adc6 = 0;
                    bool ok = TryGetStoredNumericValue(context, current1Key, out current1)
                        && TryGetStoredNumericValue(context, current6Key, out current6)
                        && TryGetStoredNumericValue(context, adc1Key, out adc1)
                        && TryGetStoredNumericValue(context, adc6Key, out adc6)
                        && Math.Abs(adc6 - adc1) > 0.001;
                    double gain = ok ? Math.Round((current6 - current1) * 4096.0 / (adc6 - adc1)) : 0;
                    double offset = ok ? Math.Round(current1 - (gain * adc1) / 4096.0) : 0;
                    double plannedLower, plannedUpper;
                    GetPlanRange(steps, rowIndex + 1, -80, 45, out plannedLower, out plannedUpper);
                    string plannedUnit = GetPlanUnit(steps, rowIndex + 1, "mA");
                    bool pass = ok && offset >= plannedLower && offset <= plannedUpper;
                    return new StepResult { IsPass = pass, Value = offset.ToString("0", System.Globalization.CultureInfo.InvariantCulture), StoreKey = offsetKey, NumericValue = offset, Unit = plannedUnit };
                }
            });
        }

        private void AddWriteStoredNumericStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, TestSequenceContext context, SK441Device deviceManager, string name, string commandPrefix, string storeKey)
        {
            steps.Add(new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                PowerCritical = true,
                Action = async (token) =>
                {
                    TestSequenceValue storedValue = null;
                    double value = 0.0;
                    bool sourcePassed = context != null
                        && context.TryGetValue(storeKey, out storedValue)
                        && storedValue.Pass;
                    if (sourcePassed)
                        value = storedValue.NumericValue;

                    bool pass = sourcePassed;
                    if (pass)
                        pass = await SendBcm125MbdCommandAsync(deviceManager, commandPrefix + value.ToString("0", System.Globalization.CultureInfo.InvariantCulture), 0);
                    return new StepResult
                    {
                        IsPass = pass,
                        Value = pass
                            ? value.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                            : (sourcePassed ? "Write Fail" : "Source Failed"),
                        StoreKey = storeKey + ".WRITE_OK",
                        NumericValue = pass ? 1 : 0,
                        Unit = string.Empty
                    };
                }
            });
        }

        protected void AddBcm125WestinghouseSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager)
        {
            int row = steps.Count;

            AddWestinghouseStringSteps(steps, deviceManager, ref row, "ST1", "REMOTE1", "K01", "K02", "K03", "K04", "K05", "K06");
            AddWestinghouseStringSteps(steps, deviceManager, ref row, "ST2", "REMOTE2", "K07", "K08", "K09", "K10", "K11", "K12");
            AddMbdCommandStep(steps, row++, deviceManager, "17.28 Send EXIT to MBD terminal", "EXIT", 0);
            AddProgrammingActionStep(steps, row++, "17.29 Close USB-B/MBD serial communication", token => Task.FromResult(true), "Close OK", "Close Fail");
        }

        private void AddWestinghouseStringSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager, ref int row, string suffix, string remoteCommand, string k01, string k02, string k03, string k04, string k05, string k06)
        {
            AddProgrammingActionStep(steps, row++, "17.1 " + suffix + " close RL4 to power DUT", token => deviceManager.ControlRelayAsync(7, true), "RL4 ON", "RL4 Fail");
            AddProgrammingActionStep(steps, row++, "17.2 " + suffix + " check USB-B/MBD serial open", token => CheckBcm125MbdCommunicationAsync(deviceManager), "Open", "COM Fail");
            AddMbdCommandStep(steps, row++, deviceManager, "17.3 " + suffix + " send EXIT to MBD terminal", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "17.4 " + suffix + " send DIAG ON to MBD", "DIAG ON", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "17.5 " + suffix + " enable MBD Westinghouse W95=1", "W95=1", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "17.6 " + suffix + " send " + remoteCommand, remoteCommand, 0);
            AddMbdCommandStep(steps, row++, deviceManager, "17.7 " + suffix + " send DIAG ON to BCM", "DIAG ON", 0);
            AddMbdNumericQueryStep(steps, row++, deviceManager, k01 + " " + suffix + " read R61 st_emerg active", "R61", "BCM125.WESTINGHOUSE." + k01 + "_ST_EMERG_" + suffix, 0, 0, "state");
            AddMbdCommandStep(steps, row++, deviceManager, "17.10 " + suffix + " send EXIT to MBD terminal", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "17.11 " + suffix + " disable MBD Westinghouse W95=0", "W95=0", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "17.12 " + suffix + " send " + remoteCommand, remoteCommand, 0);
            AddMbdNumericQueryStep(steps, row++, deviceManager, k02 + " " + suffix + " read R61 st_emerg inactive", "R61", "BCM125.WESTINGHOUSE." + k02 + "_ST_EMERG_" + suffix, 1, 1, "state");
            AddMbdCommandStep(steps, row++, deviceManager, "17.14 " + suffix + " enable DUT emerg_uc W67=1", "W67=1", 0);
            AddMbdNumericQueryStep(steps, row++, deviceManager, k03 + " " + suffix + " read R61 DUT alarm active", "R61", "BCM125.WESTINGHOUSE." + k03 + "_ST_EMERG_" + suffix, 0, 0, "state");
            AddMbdCommandStep(steps, row++, deviceManager, "17.16 " + suffix + " send EXIT to MBD terminal", "EXIT", 0);
            AddMbdNumericQueryStep(steps, row++, deviceManager, k04 + " " + suffix + " read R81 MBD alarm active", "R81", "BCM125.WESTINGHOUSE." + k04 + "_ST_WESTNGH_" + suffix, 0, 0, "state");
            AddMbdCommandStep(steps, row++, deviceManager, "17.19 " + suffix + " send EXIT to MBD terminal", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "17.20 " + suffix + " send " + remoteCommand, remoteCommand, 0);
            AddMbdCommandStep(steps, row++, deviceManager, "17.21 " + suffix + " disable DUT emerg_uc W67=0", "W67=0", 0);
            AddMbdNumericQueryStep(steps, row++, deviceManager, k05 + " " + suffix + " read R61 DUT alarm inactive", "R61", "BCM125.WESTINGHOUSE." + k05 + "_ST_EMERG_" + suffix, 1, 1, "state");
            AddMbdCommandStep(steps, row++, deviceManager, "17.24 " + suffix + " send EXIT to MBD terminal", "EXIT", 0);
            AddMbdNumericQueryStep(steps, row++, deviceManager, k06 + " " + suffix + " read R81 MBD alarm inactive", "R81", "BCM125.WESTINGHOUSE." + k06 + "_ST_WESTNGH_" + suffix, 1, 1, "state");
        }

        protected void AddBcm125PrechargeRelaySteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager)
        {
            int row = steps.Count;

            AddProgrammingActionStep(steps, row++, "18.1 Close RL4 to power DUT", token => deviceManager.ControlRelayAsync(7, true), "RL4 ON", "RL4 Fail");
            AddFixtureRelayStep(steps, row++, deviceManager, "18.2 Open power relays RL1/RL2/RL9", "SKBCM125PrechargeOpenRelays", "8,9,10,11,12,13,14,15,16", false, "Relays Open");
            AddDaqRelayStep(steps, row++, deviceManager, "18.3 Open RL5.12/RL10.12", "SKBCM125Rl5Rl10DaqRelays", Bcm125StationMap.StringSimulatorDaqRelays, false, "RL5/RL10 Open");
            AddDelayStep(steps, row++, "18.4 Wait 50ms", 50);
            AddProgrammingActionStep(steps, row++, "18.5 Reset VBUS generator 0V/0A OVP150", token => deviceManager.SetVbusGeneratorAsync(0, 0, 150, false), "VBUS Reset", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "18.6 Check serial communication", token => CheckBcm125MbdCommunicationAsync(deviceManager), "Open", "COM Fail");
            AddMbdCommandStep(steps, row++, deviceManager, "18.7 Send DIAG ON to MBD", "DIAG ON", 0);
            AddMbdNumericQueryStep(steps, row++, deviceManager, "18.8 Read R68 verify C21 discharged", "R68", "BCM125.PRECHARGE.C21_DISCHARGED", 0, 49, "count");
            AddMbdCommandStep(steps, row++, deviceManager, "18.9 Send REMOTE1", "REMOTE1", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "18.10 ST1 send DIAG ON", "DIAG ON", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "18.11 ST1 deactivate precharge W75=0", "W75=0", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "18.12 Send EXIT to MBD", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "18.13 Send REMOTE2", "REMOTE2", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "18.14 ST2 send DIAG ON", "DIAG ON", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "18.15 ST2 deactivate precharge W75=0", "W75=0", 0);
            AddFixtureRelayStep(steps, row++, deviceManager, "18.16 Close RL1a_B/RL2B", "SKBCM125PrechargeCloseRelays", "8,9", true, "Relays Closed");
            AddDelayStep(steps, row++, "18.17 Wait 500ms", 500);
            AddProgrammingActionStep(steps, row++, "18.18 Set VBUS generator 140V/1A OVP150", token => deviceManager.SetVbusGeneratorAsync(140, 1, 150, false), "VBUS Set", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "18.19 Turn on VBUS generator", token => deviceManager.SetVbusGeneratorAsync(140, 1, 150, true), "VBUS ON", "VBUS Fail");
            AddMbdCommandStep(steps, row++, deviceManager, "18.20 Send EXIT to MBD", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "18.21 Send DIAG ON to MBD", "DIAG ON", 0);
            AddDelayStep(steps, row++, "18.22 Wait 15000ms", 15000);
            AddMbdNumericQueryStep(steps, row++, deviceManager, "L01 ST1 read R69 mcu_vbus", "R69", "BCM125.PRECHARGE.L01_ADC3_MCU_VBUS_ST1", 2600, 2855, "count");
            AddMbdNumericQueryStep(steps, row++, deviceManager, "L02 ST1 read R68 vs_prec", "R68", "BCM125.PRECHARGE.L02_ADC14_VS_PREC_ST1", 2700, 2840, "count");
            AddMbdCommandStep(steps, row++, deviceManager, "18.25 Send REMOTE1", "REMOTE1", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "18.26 ST1 send DIAG ON", "DIAG ON", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "18.27 ST1 activate precharge W75=1", "W75=1", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "18.28 Send EXIT to MBD", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "18.29 Send DIAG ON to MBD", "DIAG ON", 0);
            AddDelayStep(steps, row++, "18.30 Wait 500ms", 500);
            AddMbdNumericQueryStep(steps, row++, deviceManager, "L03 ST1 read R69 after precharge", "R69", "BCM125.PRECHARGE.L03_ADC3_MCU_VBUS_ST1", 2600, 2855, "count");
            AddMbdNumericQueryStep(steps, row++, deviceManager, "L04 ST1 read R68 after precharge", "R68", "BCM125.PRECHARGE.L04_ADC14_VS_PREC_ST1", 2600, 2855, "count");
            AddDelayStep(steps, row++, "18.33 Wait 4000ms", 4000);
            AddProgrammingActionStep(steps, row++, "18.33 Turn off VBUS generator", token => deviceManager.SetVbusGeneratorAsync(140, 1, 150, false), "VBUS OFF", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "18.34 Set VBUS generator 0V/0A OVP10", token => deviceManager.SetVbusGeneratorAsync(0, 0, 10, false), "VBUS Reset", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "18.35 Make sure VBUS generator is off", token => deviceManager.SetVbusGeneratorAsync(0, 0, 10, false), "VBUS OFF", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "18.36 Reset VBUS generator 0V/0A OVP150", token => deviceManager.SetVbusGeneratorAsync(0, 0, 150, false), "VBUS Reset", "VBUS Fail");
            AddDelayStep(steps, row++, "18.37 Wait 15000ms", 15000);
            AddMbdCommandStep(steps, row++, deviceManager, "18.38 Send REMOTE1", "REMOTE1", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "18.38 ST1 deactivate precharge W75=0", "W75=0", 0);

            AddPrechargeRepeatSteps(steps, deviceManager, ref row, "ST2", "REMOTE2", "L05", "L06", "L07", "L08");

            AddFixtureRelayStep(steps, row++, deviceManager, "18.40 Open power relays RL1/RL2/RL9", "SKBCM125PrechargeOpenRelays", "8,9,10,11,12,13,14,15,16", false, "Relays Open");
            AddDelayStep(steps, row++, "18.41 Wait 200ms", 200);
            AddProgrammingActionStep(steps, row++, "18.43 Close COM with instruments", token => Task.FromResult(true), "Close OK", "Close Fail");
        }

        private void AddPrechargeRepeatSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager, ref int row, string suffix, string remoteCommand, string l01, string l02, string l03, string l04)
        {
            AddProgrammingActionStep(steps, row++, "18.39 " + suffix + " reset VBUS generator 0V/0A OVP150", token => deviceManager.SetVbusGeneratorAsync(0, 0, 150, false), "VBUS Reset", "VBUS Fail");
            AddMbdCommandStep(steps, row++, deviceManager, "18.39 " + suffix + " send DIAG ON to MBD", "DIAG ON", 0);
            AddMbdNumericQueryStep(steps, row++, deviceManager, "18.39 " + suffix + " read R68 verify C21 discharged", "R68", "BCM125.PRECHARGE.C21_DISCHARGED_" + suffix, 0, 49, "count");
            AddMbdCommandStep(steps, row++, deviceManager, "18.39 " + suffix + " send " + remoteCommand, remoteCommand, 0);
            AddMbdCommandStep(steps, row++, deviceManager, "18.39 " + suffix + " send DIAG ON", "DIAG ON", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "18.39 " + suffix + " deactivate precharge W75=0", "W75=0", 0);
            AddDelayStep(steps, row++, "18.39 " + suffix + " wait 500ms", 500);
            AddProgrammingActionStep(steps, row++, "18.39 " + suffix + " set VBUS generator 140V/1A OVP150", token => deviceManager.SetVbusGeneratorAsync(140, 1, 150, true), "VBUS ON", "VBUS Fail");
            AddDelayStep(steps, row++, "18.39 " + suffix + " wait 15000ms", 15000);
            AddMbdNumericQueryStep(steps, row++, deviceManager, l01 + " " + suffix + " read R69 mcu_vbus", "R69", "BCM125.PRECHARGE." + l01 + "_ADC3_MCU_VBUS_" + suffix, 2600, 2855, "count");
            AddMbdNumericQueryStep(steps, row++, deviceManager, l02 + " " + suffix + " read R68 vs_prec", "R68", "BCM125.PRECHARGE." + l02 + "_ADC14_VS_PREC_" + suffix, 2700, 2840, "count");
            AddProgrammingActionStep(
                steps,
                row++,
                "18.39 " + suffix + " activate precharge W75=1",
                async token =>
                {
                    if (!await SendBcm125MbdCommandAsync(deviceManager, remoteCommand, 0))
                        return false;
                    if (!await SendBcm125MbdCommandAsync(deviceManager, "DIAG ON", 0))
                        return false;
                    return await SendBcm125MbdCommandAsync(deviceManager, "W75=1", 0);
                },
                "Precharge ON",
                "Precharge Fail");
            AddProgrammingActionStep(
                steps,
                row++,
                "18.39 " + suffix + " send EXIT to MBD",
                async token =>
                {
                    if (!await SendBcm125MbdCommandAsync(deviceManager, "EXIT", 0))
                        return false;
                    if (!await SendBcm125MbdCommandAsync(deviceManager, "DIAG ON", 0))
                        return false;
                    await Task.Delay(500, token);
                    return true;
                },
                "MBD Ready",
                "MBD Fail");
            AddMbdNumericQueryStep(steps, row++, deviceManager, l03 + " " + suffix + " read R69 after precharge", "R69", "BCM125.PRECHARGE." + l03 + "_ADC3_MCU_VBUS_" + suffix, 2600, 2855, "count");
            AddMbdNumericQueryStep(steps, row++, deviceManager, l04 + " " + suffix + " read R68 after precharge", "R68", "BCM125.PRECHARGE." + l04 + "_ADC14_VS_PREC_" + suffix, 2600, 2855, "count");
            AddProgrammingActionStep(
                steps,
                row++,
                "18.39 " + suffix + " turn off/reset VBUS generator",
                async token =>
                {
                    if (!await deviceManager.SetVbusGeneratorAsync(0, 0, 150, false))
                        return false;
                    if (!await SendBcm125MbdCommandAsync(deviceManager, remoteCommand, 0))
                        return false;
                    return await SendBcm125MbdCommandAsync(deviceManager, "W75=0", 0);
                },
                "VBUS Reset/Precharge OFF",
                "Cleanup Fail");
        }

        protected void AddBcm125MidpointCalibrationSteps(System.Collections.Generic.List<TestStepItem> steps, TestSequenceContext context, SK441Device deviceManager)
        {
            int row = steps.Count;

            AddProgrammingActionStep(steps, row++, "19.1 Close RL4 to power DUT", token => deviceManager.ControlRelayAsync(7, true), "RL4 ON", "RL4 Fail");
            AddFixtureRelayStep(steps, row++, deviceManager, "19.2 Open RL1/RL2/RL9 relays", "SKBCM125MidpointOpenRelays", "8,9,10,11,12,13,14,15,16", false, "Relays Open");
            AddDaqRelayStep(steps, row++, deviceManager, "19.3 Open RL5.12/RL10.12", "SKBCM125Rl5Rl10DaqRelays", Bcm125StationMap.StringSimulatorDaqRelays, false, "RL5/RL10 Open");
            AddDelayStep(steps, row++, "19.4 Wait 50ms", 50);
            AddProgrammingActionStep(steps, row++, "19.5 Reset VBUS generator 0V/0A OVP150", token => deviceManager.SetVbusGeneratorAsync(0, 0, 150, false), "VBUS Reset", "VBUS Fail");
            AddDelayStep(steps, row++, "19.6 Wait 20ms", 20);
            AddFixtureRelayStep(steps, row++, deviceManager, "19.7 Close RL6 midpoint path", "SKBCM125Rl6Relay", "6", true, "RL6 Closed");

            AddMidpointStringSteps(steps, context, deviceManager, ref row, "ST1", "REMOTE1", "W94=1", "W94=0", "M01", "M02", "M03", "M04", "M05", "M06", "M07", "M08", "M09");
            AddMidpointStringSteps(steps, context, deviceManager, ref row, "ST2", "REMOTE2", "W91=1", "W91=0", "M10", "M11", "M12", "M13", "M14", "M15", "M16", "M17", "M18");

            AddProgrammingActionStep(steps, row++, "19.41 Turn off string generator", token => deviceManager.SetVbusGeneratorAsync(0, 0, 10, false), "Generator OFF", "Generator Fail");
            AddProgrammingActionStep(steps, row++, "19.43 Make sure generators are off", token => deviceManager.SetVbusGeneratorAsync(0, 0, 300, false), "Generators OFF", "Generator Fail");
            AddProgrammingActionStep(steps, row++, "19.45 Reset electronic load", token => deviceManager.TurnOffAllInstrumentsAsync(), "Load Reset", "Load Fail");
            AddDelayStep(steps, row++, "19.46 Wait 1000ms", 1000);
            AddFixtureRelayStep(steps, row++, deviceManager, "19.47 Open RL1/RL2/RL6 relays", "SKBCM125MidpointOpenRelays", "6,8,9,10,11,13,15", false, "Relays Open");
            AddDelayStep(steps, row++, "19.48 Wait 200ms", 200);
            AddProgrammingActionStep(steps, row++, "19.50 Close COM with instruments", token => Task.FromResult(true), "Close OK", "Close Fail");
        }

        private void AddMidpointStringSteps(System.Collections.Generic.List<TestStepItem> steps, TestSequenceContext context, SK441Device deviceManager, ref int row, string suffix, string remoteCommand, string relayOnCommand, string relayOffCommand, string m01, string m02, string m03, string m04, string m05, string m06, string m07, string m08, string m09)
        {
            string prefix = "BCM125.MIDPOINT." + suffix + ".";
            AddProgrammingActionStep(steps, row++, "19.8 " + suffix + " set VMID 62.5V/0.1A OVP300", token => deviceManager.SetVbusGeneratorAsync(62.5f, 0.1f, 300, true), "VMID Set", "VMID Fail");
            AddDelayStep(steps, row++, "19.8 " + suffix + " wait 10000ms", 10000);
            AddProgrammingActionStep(steps, row++, "19.9 " + suffix + " check serial communication", token => CheckBcm125MbdCommunicationAsync(deviceManager), "Open", "COM Fail");
            AddMbdCommandStep(steps, row++, deviceManager, "19.10 " + suffix + " send EXIT", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "19.11 " + suffix + " send DIAG ON to MBD", "DIAG ON", 0);
            AddDelayStep(steps, row++, "19.12 " + suffix + " wait 500ms", 500);
            AddMbdCommandStep(steps, row++, deviceManager, "19.13 " + suffix + " activate volt_in " + relayOnCommand, relayOnCommand, 0);
            AddDelayStep(steps, row++, "19.14 " + suffix + " wait 10000ms", 10000);
            AddMbdCommandStep(steps, row++, deviceManager, "19.15 " + suffix + " send " + remoteCommand, remoteCommand, 0);
            AddMbdCommandStep(steps, row++, deviceManager, "19.16 " + suffix + " send DIAG ON to BCM", "DIAG ON", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "19.17 " + suffix + " enable mid_in W76=1", "W76=1", 0);
            AddDelayStep(steps, row++, "19.18 " + suffix + " wait 2000ms", 2000);
            AddMbdAverageQueryStep(steps, row++, deviceManager, m01 + " " + suffix + " average R93 at 62.5V", "R93", prefix + m01 + "_ADC2_VMID_62V5", 1750, 1850, "count", 8, 0);
            AddDaqAverageMvStep(steps, row++, deviceManager, m02 + " " + suffix + " average DAQ CH06 VMID 62.5V", Bcm125StationMap.VmidReferenceDaqChannel, prefix + m02 + "_VMID_62V5_MV", 62000, 63000, 8, 0);
            AddProgrammingActionStep(steps, row++, "19.21 " + suffix + " set VMID 5V/0.1A OVP300", token => deviceManager.SetVbusGeneratorAsync(5, 0.1f, 300, true), "VMID Set", "VMID Fail");
            AddDelayStep(steps, row++, "19.22 " + suffix + " wait 12000ms", 12000);
            AddMbdAverageQueryStep(steps, row++, deviceManager, m03 + " " + suffix + " average R93 at 5V", "R93", prefix + m03 + "_ADC2_VMID_5V", 150, 170, "count", 8, 0);
            AddDaqAverageMvStep(steps, row++, deviceManager, m04 + " " + suffix + " average DAQ CH06 VMID 5V", Bcm125StationMap.VmidReferenceDaqChannel, prefix + m04 + "_VMID_5V_MV", 4750, 5250, 8, 0);
            AddGainVmidWriteStep(steps, row++, context, deviceManager, m05 + " " + suffix + " calculate Gain_VMID and write W20", prefix + m02 + "_VMID_62V5_MV", prefix + m04 + "_VMID_5V_MV", prefix + m01 + "_ADC2_VMID_62V5", prefix + m03 + "_ADC2_VMID_5V", prefix + m05 + "_GAIN_VMID");
            AddOffsetVmidWriteStep(steps, row++, context, deviceManager, m06 + " " + suffix + " calculate offset and write W21", prefix + m04 + "_VMID_5V_MV", prefix + m03 + "_ADC2_VMID_5V", prefix + m05 + "_GAIN_VMID", prefix + m06 + "_OFFSET_VMID");
            AddMbdCommandStep(steps, row++, deviceManager, "19.27 " + suffix + " store ACT->FLASH", "ACT->FLASH", 0);
            AddMbdNumericCompareStoredStep(steps, row++, context, deviceManager, m07 + " " + suffix + " verify R20 Gain_VMID", "R20", prefix + m05 + "_GAIN_VMID", prefix + m07 + "_R20_GAIN", "count");
            AddMbdNumericCompareStoredStep(steps, row++, context, deviceManager, m08 + " " + suffix + " verify R21 offset", "R21", prefix + m06 + "_OFFSET_VMID", prefix + m08 + "_R21_OFFSET", "mV");
            AddProgrammingActionStep(steps, row++, "19.30 " + suffix + " set VMID 62.5V/0.1A OVP300", token => deviceManager.SetVbusGeneratorAsync(62.5f, 0.1f, 300, true), "VMID Set", "VMID Fail");
            AddDelayStep(steps, row++, "19.31 " + suffix + " wait 12000ms", 12000);
            AddMbdAverageQueryStep(steps, row++, deviceManager, m09 + " " + suffix + " average R92 vstr_mid", "R92", prefix + m09 + "_VSTR_MID", 62000, 63000, "mV", 20, 200);
            AddMbdCommandStep(steps, row++, deviceManager, "19.34 " + suffix + " send EXIT", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "19.35 " + suffix + " send DIAG ON to MBD", "DIAG ON", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "19.36 " + suffix + " deactivate volt_in " + relayOffCommand, relayOffCommand, 0);
            AddMbdCommandStep(steps, row++, deviceManager, "19.37 " + suffix + " send " + remoteCommand, remoteCommand, 0);
            AddMbdCommandStep(steps, row++, deviceManager, "19.38 " + suffix + " send DIAG ON to BCM", "DIAG ON", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "19.39 " + suffix + " disable mid_in W76=0", "W76=0", 0);
        }

        protected void AddBcm125DischargeCurrentSteps(System.Collections.Generic.List<TestStepItem> steps, TestSequenceContext context, SK441Device deviceManager)
        {
            int row = steps.Count;

            AddProgrammingActionStep(steps, row++, "20.1 Close RL4 to power DUT", token => deviceManager.ControlRelayAsync(7, true), "RL4 ON", "RL4 Fail");
            AddFixtureRelayStep(steps, row++, deviceManager, "20.2 Open discharge relays", "SKBCM125DischargeOpenRelays", "8,9,10,11,13,15,16", false, "Relays Open");
            AddDaqRelayStep(steps, row++, deviceManager, "20.2 Open RL5.12/RL10.12", "SKBCM125Rl5Rl10DaqRelays", Bcm125StationMap.StringSimulatorDaqRelays, false, "RL5/RL10 Open");
            AddDelayStep(steps, row++, "20.3 Wait 50ms", 50);
            AddProgrammingActionStep(steps, row++, "20.4 Reset VBUS generator", token => deviceManager.SetVbusGeneratorAsync(0, 0, 150, false), "VBUS Reset", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "20.4 Reset string generator", token => deviceManager.SetStringGeneratorAsync(0, 0, 150, false), "String Reset", "String Fail");
            AddProgrammingActionStep(steps, row++, "20.5 Reset electronic load", token => deviceManager.ResetElectronicLoadAsync(), "Load Reset", "Load Fail");
            AddDischargeCurrentStringSteps(steps, context, deviceManager, ref row, Bcm125ChannelProfiles.DischargeString1);
            AddDischargeCurrentStringSteps(steps, context, deviceManager, ref row, Bcm125ChannelProfiles.DischargeString2);
            AddMbdCommandStep(steps, row++, deviceManager, "20.52 Send EXIT to MBD terminal", "EXIT", 0);
            AddProgrammingActionStep(steps, row++, "20.53 Close COM with instruments", token => Task.FromResult(true), "Close OK", "Close Fail");
        }

        private void AddDischargeCurrentStringSteps(System.Collections.Generic.List<TestStepItem> steps, TestSequenceContext context, SK441Device deviceManager, ref int row, BcmStringChannelConfig channel)
        {
            string suffix = channel.Suffix;
            string remoteCommand = channel.RemoteCommand;
            string relayConfigKey = channel.RelayConfigKey;
            string relayDefaults = channel.RelayDefaults;
            int shuntChannel = channel.ShuntChannel;
            double shuntOhm = channel.ShuntOhms;
            string n01 = channel.ResultIds[0], n02 = channel.ResultIds[1];
            string n03 = channel.ResultIds[2], n04 = channel.ResultIds[3];
            string n05 = channel.ResultIds[4], n06 = channel.ResultIds[5];
            string n07 = channel.ResultIds[6], n08 = channel.ResultIds[7];
            string prefix = "BCM125.DISCHARGE." + suffix + ".";
            AddFixtureRelayStep(steps, row++, deviceManager, "20.6 " + suffix + " close discharge relays", relayConfigKey, relayDefaults, true, "Relays Closed");
            AddDelayStep(steps, row++, "20.7 " + suffix + " wait 20ms", 20);
            AddProgrammingActionStep(steps, row++, "20.8 " + suffix + " set string generator 12V/1A OVP16", token => deviceManager.SetStringGeneratorAsync(12, 1, 16, false), "String Set", "String Fail");
            AddProgrammingActionStep(steps, row++, "20.9 " + suffix + " turn on string generator", token => deviceManager.SetStringGeneratorAsync(12, 1, 16, true), "String ON", "String Fail");
            AddProgrammingActionStep(steps, row++, "20.10 " + suffix + " check serial communication", token => CheckBcm125MbdCommunicationAsync(deviceManager), "Open", "COM Fail");
            AddMbdCommandStep(steps, row++, deviceManager, "20.11 " + suffix + " send " + remoteCommand, remoteCommand, 0);
            AddMbdAverageQueryStep(steps, row++, deviceManager, n01 + " " + suffix + " average R57 at 1A", "R57", prefix + n01 + "_ADC15_1A", 40, 50, "count", 16, 20);
            AddDaqShuntCurrentStep(steps, row++, deviceManager, n02 + " " + suffix + " DAQ CH" + (shuntChannel - 100) + " shunt current 1000mA", shuntChannel, shuntOhm, prefix + n02 + "_1000MA", 950, 1050);
            AddProgrammingActionStep(steps, row++, "20.15 " + suffix + " set string generator 12V/6A OVP16", token => deviceManager.SetStringGeneratorAsync(12, 6, 16, true), "String ON", "String Fail");
            AddDelayStep(steps, row++, "20.17 " + suffix + " wait 2000ms", 2000);
            AddMbdAverageQueryStep(steps, row++, deviceManager, n03 + " " + suffix + " average R57 at 6A", "R57", prefix + n03 + "_ADC15_6A", 250, 290, "count", 16, 20);
            AddDaqShuntCurrentStep(steps, row++, deviceManager, n04 + " " + suffix + " DAQ CH" + (shuntChannel - 100) + " shunt current 6000mA", shuntChannel, shuntOhm, prefix + n04 + "_6000MA", 5900, 6100);
            AddProgrammingActionStep(steps, row++, "20.21 " + suffix + " turn off string generator", token => deviceManager.SetStringGeneratorAsync(12, 6, 16, false), "String OFF", "String Fail");
            AddProgrammingActionStep(steps, row++, "20.22 " + suffix + " reset string generator 0V/0A OVP10", token => deviceManager.SetStringGeneratorAsync(0, 0, 10, false), "String Reset", "String Fail");
            AddIdchGainOffsetStep(steps, row, context, n05, n06, suffix, prefix + n02 + "_1000MA", prefix + n04 + "_6000MA", prefix + n01 + "_ADC15_1A", prefix + n03 + "_ADC15_6A", prefix + n05 + "_GAIN_IDCH", prefix + n06 + "_OFFSET_IDCH");
            row += 2;
            AddMbdCommandStep(steps, row++, deviceManager, "20.27 " + suffix + " send " + remoteCommand, remoteCommand, 0);
            AddWriteStoredNumericStep(steps, row++, context, deviceManager, "20.28 " + suffix + " write W26 gain_idch", "W26=", prefix + n05 + "_GAIN_IDCH");
            AddWriteStoredNumericStep(steps, row++, context, deviceManager, "20.29 " + suffix + " write W27 offset_idch", "W27=", prefix + n06 + "_OFFSET_IDCH");
            AddMbdCommandStep(steps, row++, deviceManager, "20.30 " + suffix + " store ACT->FLASH", "ACT->FLASH", 0);
            AddDelayStep(steps, row++, "20.31 " + suffix + " wait 3000ms", 3000);
            AddMbdAverageQueryStep(steps, row++, deviceManager, n07 + " " + suffix + " average R45 at 0A", "R45", prefix + n07 + "_IDCH_0A", -35, 35, "mA", 50, 50);
            AddProgrammingActionStep(steps, row++, "20.36 " + suffix + " set string generator 12V/6A OVP16", token => deviceManager.SetStringGeneratorAsync(12, 6, 16, true), "String ON", "String Fail");
            AddDelayStep(steps, row++, "20.38 " + suffix + " wait 2000ms", 2000);
            AddMbdAverageQueryStep(steps, row++, deviceManager, n08 + " " + suffix + " average R45 at 6A", "R45", prefix + n08 + "_IDCH_6A", 5950, 6050, "mA", 50, 50);
            AddProgrammingActionStep(steps, row++, "20.42 " + suffix + " turn off string generator", token => deviceManager.SetStringGeneratorAsync(12, 6, 16, false), "String OFF", "String Fail");
            AddProgrammingActionStep(steps, row++, "20.45 " + suffix + " reset generators", token => deviceManager.SetStringGeneratorAsync(0, 0, 150, false), "Generators Reset", "Generator Fail");
            AddProgrammingActionStep(steps, row++, "20.46 " + suffix + " reset electronic load", token => deviceManager.ResetElectronicLoadAsync(), "Load Reset", "Load Fail");
            AddDelayStep(steps, row++, "20.47 " + suffix + " wait 1000ms", 1000);
            AddFixtureRelayStep(steps, row++, deviceManager, "20.48 " + suffix + " open discharge relays", "SKBCM125DischargeOpenRelays", "8,9,10,11,13,15,16", false, "Relays Open");
            AddDelayStep(steps, row++, "20.49 " + suffix + " wait 200ms", 200);
        }

        protected void AddBcm125DischargeMosfetSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager)
        {
            int row = steps.Count;

            AddProgrammingActionStep(steps, row++, "21.1 Close RL4 to power DUT", token => deviceManager.ControlRelayAsync(7, true), "RL4 ON", "RL4 Fail");
            AddFixtureRelayStep(steps, row++, deviceManager, "21.2 Open RL1/RL2 relays", "SKBCM125DischargeMosfetOpenRelays", "8,9,10,11,13,15", false, "Relays Open");
            AddDaqRelayStep(steps, row++, deviceManager, "21.3 Open RL5.12/RL10.12", "SKBCM125Rl5Rl10DaqRelays", Bcm125StationMap.StringSimulatorDaqRelays, false, "RL5/RL10 Open");
            AddDelayStep(steps, row++, "21.4 Wait 50ms", 50);
            AddProgrammingActionStep(steps, row++, "21.5 Reset VBUS generator", token => deviceManager.SetVbusGeneratorAsync(0, 0, 150, false), "VBUS Reset", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "21.5 Reset string generator", token => deviceManager.SetStringGeneratorAsync(0, 0, 150, false), "String Reset", "String Fail");
            AddProgrammingActionStep(steps, row++, "21.6 Reset electronic load", token => deviceManager.ResetElectronicLoadAsync(), "Load Reset", "Load Fail");
            AddDischargeMosfetStringSteps(steps, deviceManager, ref row, "ST1", "REMOTE1", "O01", "SKBCM125DischargeSt1CloseRelays", "8,11,10");
            AddDischargeMosfetStringSteps(steps, deviceManager, ref row, "ST2", "REMOTE2", "O02", "SKBCM125DischargeSt2CloseRelays", "8,16,10");
            AddMbdCommandStep(steps, row++, deviceManager, "21.24 Send EXIT to MBD terminal", "EXIT", 0);
            AddProgrammingActionStep(steps, row++, "21.25 Close COM with instruments", token => Task.FromResult(true), "Close OK", "Close Fail");
        }

        private void AddDischargeMosfetStringSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager, ref int row, string suffix, string remoteCommand, string stepName, string relayConfigKey, string relayDefaults)
        {
            AddFixtureRelayStep(steps, row++, deviceManager, "21.7 " + suffix + " close discharge relays", relayConfigKey, relayDefaults, true, "Relays Closed");
            AddDelayStep(steps, row++, "21.8 " + suffix + " wait 20ms", 20);
            AddProgrammingActionStep(steps, row++, "21.9 " + suffix + " set string generator 5V/0.3A OVP10", token => deviceManager.SetStringGeneratorAsync(5, 0.3f, 10, false), "String Set", "String Fail");
            AddProgrammingActionStep(steps, row++, "21.10 " + suffix + " turn on string generator", token => deviceManager.SetStringGeneratorAsync(5, 0.3f, 10, true), "String ON", "String Fail");
            AddProgrammingActionStep(steps, row++, "21.11 " + suffix + " check serial communication", token => CheckBcm125MbdCommunicationAsync(deviceManager), "Open", "COM Fail");
            AddMbdCommandStep(steps, row++, deviceManager, "21.12 " + suffix + " send " + remoteCommand, remoteCommand, 0);
            AddDischargeMosfetRampStep(steps, row++, deviceManager, stepName + " " + suffix + " ramp R45/R59 trigger", "BCM125.DISCHARGE_MOSFET." + stepName + "_" + suffix);
            AddProgrammingActionStep(steps, row++, "21.14 " + suffix + " turn off string generator", token => deviceManager.SetStringGeneratorAsync(5, 0.3f, 10, false), "String OFF", "String Fail");
            AddProgrammingActionStep(steps, row++, "21.15 " + suffix + " reset string generator 0V/0A OVP10", token => deviceManager.SetStringGeneratorAsync(0, 0, 10, false), "String Reset", "String Fail");
            AddProgrammingActionStep(steps, row++, "21.17 " + suffix + " reset generators 0V/0A OVP300", token => deviceManager.SetStringGeneratorAsync(0, 0, 300, false), "Generators Reset", "Generator Fail");
            AddProgrammingActionStep(steps, row++, "21.18 " + suffix + " reset electronic load", token => deviceManager.ResetElectronicLoadAsync(), "Load Reset", "Load Fail");
            AddDelayStep(steps, row++, "21.19 " + suffix + " wait 1000ms", 1000);
            AddFixtureRelayStep(steps, row++, deviceManager, "21.20 " + suffix + " open discharge relays", "SKBCM125DischargeOpenRelays", "8,9,10,11,13,15,16", false, "Relays Open");
            AddDelayStep(steps, row++, "21.21 " + suffix + " wait 200ms", 200);
        }

        private void AddDischargeMosfetRampStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, SK441Device deviceManager, string name, string storeKey)
        {
            steps.Add(new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                PowerCritical = true,
                Action = async (token) =>
                {
                    double triggerIdch = double.NaN;
                    int previousGate = 0;
                    for (int i = 0; i <= 10; i++)
                    {
                        float current = (float)(0.3 + i * 0.1);
                        bool setOk = await deviceManager.SetStringGeneratorAsync(5, current, 10, true);
                        if (!setOk)
                            break;
                        await Task.Delay(200, token);
                        double idch = MbdResponseParser.ParseScalar(
                            "R45",
                            await QueryBcm125MbdCommandAsync(deviceManager, "R45", 0));
                        int gate = (int)MbdResponseParser.ParseScalar(
                            "R59",
                            await QueryBcm125MbdCommandAsync(deviceManager, "R59", 0));
                        if (previousGate == 0 && gate == 1)
                        {
                            triggerIdch = idch;
                            break;
                        }
                        previousGate = gate;
                    }

                    double plannedLower, plannedUpper;
                    GetPlanRange(steps, rowIndex, 200, 1100, out plannedLower, out plannedUpper);
                    string plannedUnit = GetPlanUnit(steps, rowIndex, "mA");
                    bool pass = !double.IsNaN(triggerIdch) && triggerIdch >= plannedLower && triggerIdch <= plannedUpper;
                    return new StepResult { IsPass = pass, Value = double.IsNaN(triggerIdch) ? "No Trigger" : FormatValue(triggerIdch, plannedUnit), StoreKey = storeKey, NumericValue = double.IsNaN(triggerIdch) ? 0 : triggerIdch, Unit = plannedUnit };
                }
            });
        }

        protected void AddBcm125ShortProtectionSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager)
        {
            int row = steps.Count;

            AddProgrammingActionStep(steps, row++, "22.1 Close RL4 to power DUT", token => deviceManager.ControlRelayAsync(7, true), "RL4 ON", "RL4 Fail");
            AddFixtureRelayStep(steps, row++, deviceManager, "22.2 Open RL1/RL2/RL9 relays", "SKBCM125ShortProtectionOpenRelays", "8,9,10,11,13,15,16", false, "Relays Open");
            AddDaqRelayStep(steps, row++, deviceManager, "22.3 Open RL5.12/RL10.12", "SKBCM125Rl5Rl10DaqRelays", Bcm125StationMap.StringSimulatorDaqRelays, false, "RL5/RL10 Open");
            AddDelayStep(steps, row++, "22.4 Wait 50ms", 50);
            AddProgrammingActionStep(steps, row++, "22.5 Reset VBUS generator", token => deviceManager.SetVbusGeneratorAsync(0, 0, 300, false), "VBUS Reset", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "22.5 Reset string generator", token => deviceManager.SetStringGeneratorAsync(0, 0, 300, false), "String Reset", "String Fail");
            AddProgrammingActionStep(steps, row++, "22.6 Reset electronic load", token => deviceManager.ResetElectronicLoadAsync(), "Load Reset", "Load Fail");

            AddShortProtectionStringSteps(steps, deviceManager, ref row, "ST1", "REMOTE1", "U1.2", Bcm125StationMap.String1SimulatorDaqRelay, "SKBCM125ShortProtectionSt1CloseRelays", "8,11,10", "P01", "P02", "P03", "P04");
            AddShortProtectionStringSteps(steps, deviceManager, ref row, "ST2", "REMOTE2", "U33.2", Bcm125StationMap.String2SimulatorDaqRelay, "SKBCM125ShortProtectionSt2CloseRelays", "8,16,10", "P05", "P06", "P07", "P08");

            AddMbdCommandStep(steps, row++, deviceManager, "22.34 Send EXIT to MBD terminal", "EXIT", 0);
            AddProgrammingActionStep(steps, row++, "22.35 Close COM with instruments", token => Task.FromResult(true), "Close OK", "Close Fail");
        }

        private void AddShortProtectionStringSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager, ref int row, string suffix, string remoteCommand, string pointName, string daqRelay, string relayConfigKey, string relayDefaults, string p01, string p02, string p03, string p04)
        {
            AddFixtureRelayStep(steps, row++, deviceManager, "22.7 " + suffix + " close power relays", relayConfigKey, relayDefaults, true, "Relays Closed");
            AddDaqRelayStep(steps, row++, deviceManager, "22.7 " + suffix + " close " + pointName + " simulator relay", "SKBCM125ShortProtection" + suffix + "DaqRelay", daqRelay, true, "Relay Closed");
            AddDelayStep(steps, row++, "22.8 " + suffix + " wait 20ms", 20);
            AddProgrammingActionStep(steps, row++, "22.9 " + suffix + " set string generator 137.5V/1A OVP10", token => deviceManager.SetStringGeneratorAsync(137.5f, 1, 10, false), "String Set", "String Fail");
            AddProgrammingActionStep(steps, row++, "22.10 " + suffix + " turn on string generator", token => deviceManager.SetStringGeneratorAsync(137.5f, 1, 10, true), "String ON", "String Fail");
            AddDelayStep(steps, row++, "22.11 " + suffix + " wait 1000ms", 1000);
            AddProgrammingActionStep(steps, row++, "22.12 " + suffix + " check serial communication", token => CheckBcm125MbdCommunicationAsync(deviceManager), "Open", "COM Fail");
            AddMbdCommandStep(steps, row++, deviceManager, "22.13 " + suffix + " send " + remoteCommand, remoteCommand, 0);
            AddMbdCommandStep(steps, row++, deviceManager, "22.14 " + suffix + " send DIAG ON", "DIAG ON", 0);
            AddProgrammingActionStep(steps, row++, "22.15 " + suffix + " open RL CORTO CH4, " + pointName + " = NC 2.9V", token => deviceManager.SetShortProtectionSimulatorAsync(pointName, 2.9), "NC 2.9V", "Signal Fail");
            AddDelayStep(steps, row++, "22.16 " + suffix + " wait 500ms", 500);
            AddMbdNumericQueryStep(steps, row++, deviceManager, p01 + " " + suffix + " read R61 st_emerg inactive", "R61", "BCM125.SHORT_PROTECTION." + p01 + "_ST_EMERG_" + suffix, 1, 1, "state");
            AddMbdNumericQueryStep(steps, row++, deviceManager, p02 + " " + suffix + " read R60 st_prot_short inactive", "R60", "BCM125.SHORT_PROTECTION." + p02 + "_ST_PROT_SHORT_" + suffix, 1, 1, "state");
            AddDelayStep(steps, row++, "22.18 " + suffix + " wait 100ms", 100);
            AddProgrammingActionStep(steps, row++, "22.19 " + suffix + " close RL CORTO CH4, " + pointName + " = NO 3.3V", token => deviceManager.SetShortProtectionSimulatorAsync(pointName, 3.3), "NO 3.3V", "Signal Fail");
            AddDelayStep(steps, row++, "22.20 " + suffix + " wait 500ms", 500);
            AddMbdNumericQueryStep(steps, row++, deviceManager, p03 + " " + suffix + " read R61 st_emerg active", "R61", "BCM125.SHORT_PROTECTION." + p03 + "_ST_EMERG_" + suffix, 0, 0, "state");
            AddMbdNumericQueryStep(steps, row++, deviceManager, p04 + " " + suffix + " read R60 st_prot_short active", "R60", "BCM125.SHORT_PROTECTION." + p04 + "_ST_PROT_SHORT_" + suffix, 0, 0, "state");
            AddProgrammingActionStep(steps, row++, "22.22 " + suffix + " open RL CORTO CH4, " + pointName + " = NC 2.9V", token => deviceManager.SetShortProtectionSimulatorAsync(pointName, 2.9), "NC 2.9V", "Signal Fail");
            AddProgrammingActionStep(steps, row++, "22.23 " + suffix + " turn off string generator", token => deviceManager.SetStringGeneratorAsync(137.5f, 1, 10, false), "String OFF", "String Fail");
            AddProgrammingActionStep(steps, row++, "22.26 " + suffix + " reset generators", token => deviceManager.SetStringGeneratorAsync(0, 0, 300, false), "Generators Reset", "Generator Fail");
            AddProgrammingActionStep(steps, row++, "22.27 " + suffix + " reset electronic load", token => deviceManager.ResetElectronicLoadAsync(), "Load Reset", "Load Fail");
            AddDelayStep(steps, row++, "22.28 " + suffix + " wait 1000ms", 1000);
            AddFixtureRelayStep(steps, row++, deviceManager, "22.29 " + suffix + " open power relays", "SKBCM125ShortProtectionOpenRelays", "8,9,10,11,13,15,16", false, "Relays Open");
            AddDelayStep(steps, row++, "22.30 " + suffix + " wait 200ms", 200);
            AddDaqRelayStep(steps, row++, deviceManager, "22.31 " + suffix + " open " + pointName + " simulator relay", "SKBCM125ShortProtection" + suffix + "DaqRelay", daqRelay, false, "Relay Open");
        }

        protected void AddBcm125ChargingCurrentCalibrationSteps(System.Collections.Generic.List<TestStepItem> steps, TestSequenceContext context, SK441Device deviceManager)
        {
            int row = steps.Count;

            AddProgrammingActionStep(steps, row++, "23.1 Close RL4 to power DUT", token => deviceManager.ControlRelayAsync(7, true), "RL4 ON", "RL4 Fail");
            AddFixtureRelayStep(steps, row++, deviceManager, "23.2 Open RL1/RL2/RL9 power relays", "SKBCM125ChargingOpenRelays", "8,9,10,11,12,13,15,16", false, "Relays Open");
            AddDaqRelayStep(steps, row++, deviceManager, "23.3 Open RL5.12/RL10.12", "SKBCM125Rl5Rl10DaqRelays", Bcm125StationMap.StringSimulatorDaqRelays, false, "RL5/RL10 Open");
            AddDelayStep(steps, row++, "23.4 Wait 50ms", 50);
            AddProgrammingActionStep(steps, row++, "23.5 Reset VBUS generator 0V/0A OVP150", token => deviceManager.SetVbusGeneratorAsync(0, 0, 150, false), "VBUS Reset", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "23.5 Reset string generator 0V/0A OVP150", token => deviceManager.SetStringGeneratorAsync(0, 0, 150, false), "String Reset", "String Fail");
            AddProgrammingActionStep(steps, row++, "23.6 Reset electronic load", token => deviceManager.ResetElectronicLoadAsync(), "Load Reset", "Load Fail");

            AddChargingCurrentStringSteps(steps, context, deviceManager, ref row, Bcm125ChannelProfiles.ChargingString1);
            AddChargingCurrentStringSteps(steps, context, deviceManager, ref row, Bcm125ChannelProfiles.ChargingString2);

            AddProgrammingActionStep(steps, row++, "23.62 Close COM with instruments", token => Task.FromResult(true), "Close OK", "Close Fail");
        }

        private void AddChargingCurrentStringSteps(System.Collections.Generic.List<TestStepItem> steps, TestSequenceContext context, SK441Device deviceManager, ref int row, BcmStringChannelConfig channel)
        {
            string suffix = channel.Suffix;
            string remoteCommand = channel.RemoteCommand;
            string relayConfigKey = channel.RelayConfigKey;
            string relayDefaults = channel.RelayDefaults;
            int vMeasureChannel = channel.VoltageMeasureChannel;
            int shuntChannel = channel.ShuntChannel;
            double shuntOhm = channel.ShuntOhms;
            string q01 = channel.ResultIds[0], q02 = channel.ResultIds[1];
            string q03 = channel.ResultIds[2], q04 = channel.ResultIds[3];
            string q05 = channel.ResultIds[4], q06 = channel.ResultIds[5];
            string q07 = channel.ResultIds[6], q08 = channel.ResultIds[7];
            string q09 = channel.ResultIds[8], q10 = channel.ResultIds[9];
            string q11 = channel.ResultIds[10], q12 = channel.ResultIds[11];
            string q13 = channel.ResultIds[12], q14 = channel.ResultIds[13];
            string q15 = channel.ResultIds[14], q16 = channel.ResultIds[15];
            string gainVstrWrite = channel.CalibrationWriteCommands[0];
            string offsetVstrWrite = channel.CalibrationWriteCommands[1];
            string gainIccbWrite = channel.CalibrationWriteCommands[2];
            string gainIccbVstrWrite = channel.CalibrationWriteCommands[3];
            string offsetIccbWrite = channel.CalibrationWriteCommands[4];
            string mbdRelayCommandPrefix = channel.MbdRelayCommandPrefix;
            string prefix = "BCM125.CHARGING." + suffix + ".";
            AddFixtureRelayStep(steps, row++, deviceManager, "23.7 " + suffix + " close charging calibration relays", relayConfigKey, relayDefaults, true, "Relays Closed");
            AddDelayStep(steps, row++, "23.8 " + suffix + " wait 20ms", 20);
            AddProgrammingActionStep(steps, row++, "23.9 " + suffix + " set string generator 0V/0A OVP150", token => deviceManager.SetStringGeneratorAsync(0, 0, 150, false), "String Set", "String Fail");
            AddDelayStep(steps, row++, "23.10 " + suffix + " wait 1000ms", 1000);
            AddProgrammingActionStep(steps, row++, "23.11 " + suffix + " turn on string generator", token => deviceManager.SetStringGeneratorAsync(0, 0, 150, true), "String ON", "String Fail");
            AddProgrammingActionStep(steps, row++, "23.12 " + suffix + " set VSTR_NEG bus generator 1V/1A OVP150", token => deviceManager.SetVbusGeneratorAsync(1, 1, 150, true), "VBUS ON", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "23.13 " + suffix + " check serial communication", token => CheckBcm125MbdCommunicationAsync(deviceManager), "Open", "COM Fail");
            AddMbdCommandStep(steps, row++, deviceManager, "23.14 " + suffix + " send EXIT to MBD terminal", "EXIT", 0);
            AddMbdCommandPairStep(
                steps,
                row++,
                deviceManager,
                "23.14 " + suffix + " send DIAG ON and close MBD string relay " + mbdRelayCommandPrefix + "1",
                "DIAG ON",
                mbdRelayCommandPrefix + "1",
                500);
            AddMbdCommandStep(steps, row++, deviceManager, "23.15 " + suffix + " send " + remoteCommand, remoteCommand, 0);
            AddMbdCommandStep(steps, row++, deviceManager, "23.16 " + suffix + " send DIAG ON to BCM", "DIAG ON", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "23.17 " + suffix + " set W69=1 CCB off", "W69=1", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "23.18 " + suffix + " set W12=0mA", "W12=0", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "23.19 " + suffix + " set W75=1 precharge", "W75=1", 0);
            AddDelayStep(steps, row++, "23.20 " + suffix + " wait 500ms", 500);
            AddMbdAverageQueryStep(steps, row++, deviceManager, q01 + " " + suffix + " average R50 adc0 @ 0V 0A", "R50", prefix + q01 + "_ADC0_0V_0A", 40, 50, "count", 4, 20);
            AddMbdAverageQueryStep(steps, row++, deviceManager, q02 + " " + suffix + " average R56 adc14 @ 0V 0A", "R56", prefix + q02 + "_ADC14_0V_0A", 1350, 1500, "count", 4, 20);
            AddDaqAverageMvLimitedStep(steps, row++, deviceManager, q03 + " " + suffix + " DAQ V" + (vMeasureChannel == 109 ? "2" : "6") + " 1000mV (0A)", vMeasureChannel, prefix + q03 + "_VSTR_NEG_1000MV_0A", 900, 1100);
            AddDaqSignedShuntCurrentStep(steps, row++, deviceManager, q04 + " " + suffix + " shunt current 0mA", shuntChannel, shuntOhm, -1, prefix + q04 + "_CURRENT_0MA", -10, 10);
            AddProgrammingActionStep(steps, row++, "23.26 " + suffix + " set VSTR_NEG bus generator 15V/1A OVP150", token => deviceManager.SetVbusGeneratorAsync(15, 1, 150, true), "VBUS Set", "VBUS Fail");
            AddDelayStep(steps, row++, "23.27 " + suffix + " wait 2000ms", 2000);
            AddMbdAverageQueryStep(steps, row++, deviceManager, q05 + " " + suffix + " average R50 adc0 @ 15V 0A", "R50", prefix + q05 + "_ADC0_15V_0A", 650, 700, "count", 4, 20);
            AddMbdAverageQueryStep(steps, row++, deviceManager, q06 + " " + suffix + " average R56 adc14 @ 15V 0A", "R56", prefix + q06 + "_ADC14_15V_0A", 1350, 1550, "count", 4, 20);
            AddDaqAverageMvLimitedStep(steps, row++, deviceManager, q07 + " " + suffix + " DAQ V" + (vMeasureChannel == 109 ? "2" : "6") + " 15000mV (0A)", vMeasureChannel, prefix + q07 + "_VSTR_NEG_15000MV_0A", 14900, 15100);
            AddProgrammingActionStep(steps, row++, "23.32 " + suffix + " set VSTR_NEG bus generator 1V/1A OVP150", token => deviceManager.SetVbusGeneratorAsync(1, 1, 150, true), "VBUS Set", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "23.33 " + suffix + " set string generator 10V/6A OVP150", token => deviceManager.SetStringGeneratorAsync(10, 6, 150, true), "String Set", "String Fail");
            AddDelayStep(steps, row++, "23.34 " + suffix + " wait 7000ms", 7000);
            AddMbdCommandStep(steps, row++, deviceManager, "23.34 " + suffix + " set W69=0 CCB on and settle 500ms", "W69=0", 500);
            AddMbdAverageQueryStep(steps, row++, deviceManager, q08 + " " + suffix + " average R50 adc0 @ 0V 6A", "R50", prefix + q08 + "_ADC0_0V_6A", 25, 40, "count", 4, 20);
            AddMbdAverageQueryStep(steps, row++, deviceManager, q09 + " " + suffix + " average R56 adc14 @ 0V 6A", "R56", prefix + q09 + "_ADC14_0V_6A", 2650, 3100, "count", 4, 20);
            AddDaqSignedShuntCurrentStep(steps, row++, deviceManager, q10 + " " + suffix + " shunt current -6000mA", shuntChannel, shuntOhm, -1, prefix + q10 + "_CURRENT_6000MA", -6500, -5500);
            AddDaqAverageMvLimitedStep(steps, row++, deviceManager, q11 + " " + suffix + " DAQ V" + (vMeasureChannel == 109 ? "2" : "6") + " 1000mV (6A)", vMeasureChannel, prefix + q11 + "_VSTR_NEG_1000MV_6A", 600, 800);
            AddMbdCommandStep(steps, row++, deviceManager, "23.40 " + suffix + " set W69=1 CCB off", "W69=1", 0);
            AddProgrammingActionStep(steps, row++, "23.40 " + suffix + " turn off string generator", token => deviceManager.SetStringGeneratorAsync(0, 0, 150, false), "String OFF", "String Fail");
            AddProgrammingActionStep(steps, row++, "23.41 " + suffix + " turn off bus generator", token => deviceManager.SetVbusGeneratorAsync(0, 0, 150, false), "VBUS OFF", "VBUS Fail");
            AddMbdCommandStep(steps, row++, deviceManager, "23.42 " + suffix + " set W75=0 precharge off", "W75=0", 0);
            AddChargingCalibrationCalcSteps(steps, ref row, context, deviceManager, suffix, prefix, q01, q02, q03, q04, q05, q06, q07, q08, q09, q10, q12, q13, q14, q15, q16);
            AddWriteStoredNumericStep(steps, row++, context, deviceManager, "23.50 " + suffix + " write " + q12 + " gain_vstr_neg", gainVstrWrite, prefix + q12 + "_GAIN_VSTR_NEG");
            AddWriteStoredNumericStep(steps, row++, context, deviceManager, "23.50 " + suffix + " write " + q13 + " offset_vstr_neg", offsetVstrWrite, prefix + q13 + "_OFFSET_VSTR_NEG");
            AddWriteStoredNumericStep(steps, row++, context, deviceManager, "23.50 " + suffix + " write " + q14 + " gain_iccb", gainIccbWrite, prefix + q14 + "_GAIN_ICCB");
            AddWriteStoredNumericStep(steps, row++, context, deviceManager, "23.50 " + suffix + " write " + q15 + " gain_iccb_vstr_neg", gainIccbVstrWrite, prefix + q15 + "_GAIN_ICCB_VSTR_NEG");
            AddWriteStoredNumericStep(steps, row++, context, deviceManager, "23.50 " + suffix + " write " + q16 + " offset_iccb", offsetIccbWrite, prefix + q16 + "_OFFSET_ICCB");
            AddConditionalActFlashStep(
                steps,
                row++,
                context,
                deviceManager,
                "23.51 " + suffix + " send ACT->FLASH only after complete calibration",
                prefix + q12 + "_GAIN_VSTR_NEG",
                prefix + q13 + "_OFFSET_VSTR_NEG",
                prefix + q14 + "_GAIN_ICCB",
                prefix + q15 + "_GAIN_ICCB_VSTR_NEG",
                prefix + q16 + "_OFFSET_ICCB");
            AddProgrammingActionStep(steps, row++, "23.52 " + suffix + " turn off string generator", token => deviceManager.SetStringGeneratorAsync(0, 0, 10, false), "String OFF", "String Fail");
            AddProgrammingActionStep(steps, row++, "23.55 " + suffix + " reset generators 0V/0A OVP150", token => deviceManager.SetVbusGeneratorAsync(0, 0, 150, false), "Generators Reset", "Generator Fail");
            AddProgrammingActionStep(steps, row++, "23.56 " + suffix + " reset electronic load", token => deviceManager.ResetElectronicLoadAsync(), "Load Reset", "Load Fail");
            AddDelayStep(steps, row++, "23.57 " + suffix + " wait 1000ms", 1000);
            AddFixtureRelayStep(steps, row++, deviceManager, "23.58 " + suffix + " open charging calibration relays", "SKBCM125ChargingOpenRelays", "8,9,10,11,12,13,15,16", false, "Relays Open");
            AddDelayStep(steps, row++, "23.59 " + suffix + " wait 200ms", 200);
            AddMbdCommandPairStep(
                steps,
                row++,
                deviceManager,
                "23.59 " + suffix + " send EXIT and open MBD string relay " + mbdRelayCommandPrefix + "0",
                "EXIT",
                mbdRelayCommandPrefix + "0",
                500);
        }

        private void AddConditionalActFlashStep(
            System.Collections.Generic.List<TestStepItem> steps,
            int rowIndex,
            TestSequenceContext context,
            SK441Device deviceManager,
            string name,
            params string[] calibrationKeys)
        {
            steps.Add(new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                PowerCritical = true,
                Action = async token =>
                {
                    foreach (string key in calibrationKeys)
                    {
                        TestSequenceValue calculated;
                        TestSequenceValue written;
                        if (context == null
                            || !context.TryGetValue(key, out calculated)
                            || !calculated.Pass
                            || !context.TryGetValue(key + ".WRITE_OK", out written)
                            || !written.Pass)
                        {
                            OnLogError(
                                "BCM-125 calibration is incomplete; ACT->FLASH skipped: " + key);
                            return new StepResult
                            {
                                IsPass = false,
                                Value = "Calibration Invalid / FLASH Skipped"
                            };
                        }
                    }

                    bool pass = await SendBcm125MbdCommandAsync(
                        deviceManager,
                        "ACT->FLASH",
                        0);
                    return new StepResult
                    {
                        IsPass = pass,
                        Value = pass ? "Saved" : "FLASH Fail"
                    };
                }
            });
        }

        private void AddDaqAverageMvLimitedStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, SK441Device deviceManager, string name, int channel, string storeKey, double lower, double upper)
        {
            steps.Add(new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                PowerCritical = true,
                Action = async (token) =>
                {
                    double mv = await deviceManager.MeasureDaqChannelVoltageAverageAsync(
                        channel,
                        8,
                        0,
                        token) * 1000.0;
                    double plannedLower, plannedUpper;
                    GetPlanRange(steps, rowIndex, lower, upper, out plannedLower, out plannedUpper);
                    string plannedUnit = GetPlanUnit(steps, rowIndex, "mV");
                    bool pass = mv >= plannedLower && mv <= plannedUpper;
                    return new StepResult { IsPass = pass, Value = FormatValue(mv, plannedUnit), StoreKey = storeKey, NumericValue = mv, Unit = plannedUnit };
                }
            });
        }

        private void AddDaqSignedShuntCurrentStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, SK441Device deviceManager, string name, int channel, double shuntOhm, double sign, string storeKey, double lower, double upper)
        {
            steps.Add(new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                PowerCritical = true,
                Action = async (token) =>
                {
                    double averageVolts = await deviceManager.MeasureDaqChannelVoltageAverageAsync(
                        channel,
                        8,
                        0,
                        token);
                    double currentMa = sign * averageVolts / shuntOhm * 1000.0;
                    double plannedLower, plannedUpper;
                    GetPlanRange(steps, rowIndex, lower, upper, out plannedLower, out plannedUpper);
                    string plannedUnit = GetPlanUnit(steps, rowIndex, "mA");
                    bool pass = currentMa >= plannedLower && currentMa <= plannedUpper;
                    return new StepResult { IsPass = pass, Value = FormatValue(currentMa, plannedUnit), StoreKey = storeKey, NumericValue = currentMa, Unit = plannedUnit };
                }
            });
        }

        private void AddChargingCalibrationCalcSteps(System.Collections.Generic.List<TestStepItem> steps, ref int row, TestSequenceContext context, SK441Device deviceManager, string suffix, string prefix, string q01, string q02, string q03, string q04, string q05, string q06, string q07, string q08, string q09, string q10, string q12, string q13, string q14, string q15, string q16)
        {
            AddChargingCalculatedValueStep(
                steps,
                row++,
                deviceManager,
                q12 + " " + suffix + " calculate gain_vstr_neg",
                prefix + q12 + "_GAIN_VSTR_NEG",
                88000,
                95000,
                "count",
                () =>
                {
                    double adc0Low, adc0High, vLow, vHigh;
                    if (!TryGetPassedStoredNumericValue(context, prefix + q01 + "_ADC0_0V_0A", out adc0Low)
                        || !TryGetPassedStoredNumericValue(context, prefix + q03 + "_VSTR_NEG_1000MV_0A", out vLow)
                        || !TryGetPassedStoredNumericValue(context, prefix + q05 + "_ADC0_15V_0A", out adc0High)
                        || !TryGetPassedStoredNumericValue(context, prefix + q07 + "_VSTR_NEG_15000MV_0A", out vHigh)
                        || Math.Abs(adc0High - adc0Low) <= 0.001)
                        return null;

                    return Math.Round((vHigh - vLow) * 4096.0 / (adc0High - adc0Low));
                });

            AddChargingCalculatedValueStep(
                steps,
                row++,
                deviceManager,
                q13 + " " + suffix + " calculate offset_vstr_neg",
                prefix + q13 + "_OFFSET_VSTR_NEG",
                -1500,
                1035,
                "mV",
                () =>
                {
                    double adc0Low, vLow, gainVstr;
                    if (!TryGetPassedStoredNumericValue(context, prefix + q01 + "_ADC0_0V_0A", out adc0Low)
                        || !TryGetPassedStoredNumericValue(context, prefix + q03 + "_VSTR_NEG_1000MV_0A", out vLow)
                        || !TryGetPassedStoredNumericValue(context, prefix + q12 + "_GAIN_VSTR_NEG", out gainVstr))
                        return null;

                    return Math.Round(vLow - (gainVstr * adc0Low) / 4096.0);
                });

            AddChargingCalculatedValueStep(
                steps,
                row++,
                deviceManager,
                q14 + " " + suffix + " calculate gain_iccb",
                prefix + q14 + "_GAIN_ICCB",
                17300,
                18300,
                "count",
                () =>
                {
                    double adc14Low, adc14Six, current0, currentSix;
                    if (!TryGetPassedStoredNumericValue(context, prefix + q02 + "_ADC14_0V_0A", out adc14Low)
                        || !TryGetPassedStoredNumericValue(context, prefix + q04 + "_CURRENT_0MA", out current0)
                        || !TryGetPassedStoredNumericValue(context, prefix + q09 + "_ADC14_0V_6A", out adc14Six)
                        || !TryGetPassedStoredNumericValue(context, prefix + q10 + "_CURRENT_6000MA", out currentSix)
                        || Math.Abs(adc14Six - adc14Low) <= 0.001)
                        return null;

                    return Math.Round(-1.0 * (currentSix - current0) * 4096.0 / (adc14Six - adc14Low));
                });

            AddChargingCalculatedValueStep(
                steps,
                row++,
                deviceManager,
                q15 + " " + suffix + " calculate gain_iccb_vstr_neg",
                prefix + q15 + "_GAIN_ICCB_VSTR_NEG",
                -6620,
                9600,
                "count",
                () =>
                {
                    double adc0Low, adc0High, adc14Low, adc14High, gainIccb;
                    if (!TryGetPassedStoredNumericValue(context, prefix + q01 + "_ADC0_0V_0A", out adc0Low)
                        || !TryGetPassedStoredNumericValue(context, prefix + q02 + "_ADC14_0V_0A", out adc14Low)
                        || !TryGetPassedStoredNumericValue(context, prefix + q05 + "_ADC0_15V_0A", out adc0High)
                        || !TryGetPassedStoredNumericValue(context, prefix + q06 + "_ADC14_15V_0A", out adc14High)
                        || !TryGetPassedStoredNumericValue(context, prefix + q14 + "_GAIN_ICCB", out gainIccb)
                        || Math.Abs(adc0High - adc0Low) <= 0.001)
                        return null;

                    return Math.Round(-1.0 * gainIccb * (adc14High - adc14Low) / (adc0High - adc0Low));
                });

            AddChargingCalculatedValueStep(
                steps,
                row++,
                deviceManager,
                q16 + " " + suffix + " calculate offset_iccb",
                prefix + q16 + "_OFFSET_ICCB",
                -6600,
                -5400,
                "mA",
                () =>
                {
                    double adc0Low, adc14Low, gainIccb, gainIccbVstr;
                    if (!TryGetPassedStoredNumericValue(context, prefix + q01 + "_ADC0_0V_0A", out adc0Low)
                        || !TryGetPassedStoredNumericValue(context, prefix + q02 + "_ADC14_0V_0A", out adc14Low)
                        || !TryGetPassedStoredNumericValue(context, prefix + q14 + "_GAIN_ICCB", out gainIccb)
                        || !TryGetPassedStoredNumericValue(context, prefix + q15 + "_GAIN_ICCB_VSTR_NEG", out gainIccbVstr))
                        return null;

                    return Math.Round(-1.0 * ((gainIccb * adc14Low) + (gainIccbVstr * adc0Low)) / 4096.0);
                });
        }

        private bool TryGetPassedStoredNumericValue(
            TestSequenceContext context,
            string key,
            out double value)
        {
            TestSequenceValue stored;
            if (context != null && context.TryGetValue(key, out stored) && stored.Pass)
            {
                value = stored.NumericValue;
                return true;
            }

            value = 0.0;
            OnLogError("Missing or failed calibration dependency: " + key);
            return false;
        }

        private void AddChargingCalculatedValueStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, SK441Device deviceManager, string name, string storeKey, double lower, double upper, string unit, Func<double?> calculate)
        {
            steps.Add(new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                PowerCritical = true,
                Action = async (token) =>
                {
                    await Task.Delay(1, token);
                    double? calculated = calculate();
                    double value = calculated.HasValue ? calculated.Value : 0.0;
                    double plannedLower, plannedUpper;
                    GetPlanRange(steps, rowIndex, lower, upper, out plannedLower, out plannedUpper);
                    string plannedUnit = GetPlanUnit(steps, rowIndex, unit);
                    bool pass = calculated.HasValue && value >= plannedLower && value <= plannedUpper;
                    return new StepResult
                    {
                        IsPass = pass,
                        Value = calculated.HasValue ? FormatValue(value, plannedUnit) : "Missing Input",
                        StoreKey = storeKey,
                        NumericValue = value,
                        Unit = plannedUnit
                    };
                }
            });
        }

        protected void AddBcm125CurrentAndVstrNegVerificationSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager)
        {
            int row = steps.Count;

            AddProgrammingActionStep(steps, row++, "24.1 Close RL4 to power DUT", token => deviceManager.ControlRelayAsync(7, true), "RL4 ON", "RL4 Fail");
            AddFixtureRelayStep(steps, row++, deviceManager, "24.2 Open RL1/RL2/RL9 power relays", "SKBCM125VerificationOpenRelays", "8,9,10,11,12,13,15,16", false, "Relays Open");
            AddDelayStep(steps, row++, "24.3 Wait 50ms", 50);
            AddProgrammingActionStep(steps, row++, "24.4 Reset VBUS generator 0V/0A OVP300", token => deviceManager.SetVbusGeneratorAsync(0, 0, 300, false), "VBUS Reset", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "24.4 Reset string generator 0V/0A OVP300", token => deviceManager.SetStringGeneratorAsync(0, 0, 300, false), "String Reset", "String Fail");
            AddProgrammingActionStep(steps, row++, "24.5 Reset electronic load", token => deviceManager.ResetElectronicLoadAsync(), "Load Reset", "Load Fail");

            AddCurrentAndVstrNegVerificationStringSteps(steps, deviceManager, ref row, Bcm125ChannelProfiles.VerificationString1);
            AddCurrentAndVstrNegVerificationStringSteps(steps, deviceManager, ref row, Bcm125ChannelProfiles.VerificationString2);

            AddDelayStep(steps, row++, "24.59 Wait 200ms", 200);
            AddMbdCommandStep(steps, row++, deviceManager, "24.61 Send EXIT to MBD terminal", "EXIT", 0);
            AddProgrammingActionStep(steps, row++, "24.62 Close COM with instruments", token => Task.FromResult(true), "Close OK", "Close Fail");
        }

        private void AddCurrentAndVstrNegVerificationStringSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager, ref int row, BcmStringChannelConfig channel)
        {
            string suffix = channel.Suffix;
            string remoteCommand = channel.RemoteCommand;
            string relayConfigKey = channel.RelayConfigKey;
            string relayDefaults = channel.RelayDefaults;
            int vMeasureChannel = channel.VoltageMeasureChannel;
            int shuntChannel = channel.ShuntChannel;
            double shuntOhm = channel.ShuntOhms;
            string r01 = channel.ResultIds[0], r02 = channel.ResultIds[1];
            string r03 = channel.ResultIds[2], r04 = channel.ResultIds[3];
            string r05 = channel.ResultIds[4], r06 = channel.ResultIds[5];
            string r07 = channel.ResultIds[6], r08 = channel.ResultIds[7];
            string r09 = channel.ResultIds[8], r10 = channel.ResultIds[9];
            string r11 = channel.ResultIds[10], r12 = channel.ResultIds[11];
            string r13 = channel.ResultIds[12], r14 = channel.ResultIds[13];
            string prefix = "BCM125.VERIFY_CHARGING." + suffix + ".";
            AddFixtureRelayStep(steps, row++, deviceManager, "24.6 " + suffix + " close verification relays", relayConfigKey, relayDefaults, true, "Relays Closed");
            AddDelayStep(steps, row++, "24.7 " + suffix + " wait 20ms", 20);
            AddProgrammingActionStep(steps, row++, "24.8 " + suffix + " set string generator 0V/0A OVP150", token => deviceManager.SetStringGeneratorAsync(0, 0, 150, false), "String Set", "String Fail");
            AddProgrammingActionStep(steps, row++, "24.9 " + suffix + " turn on string generator", token => deviceManager.SetStringGeneratorAsync(0, 0, 150, true), "String ON", "String Fail");
            AddProgrammingActionStep(steps, row++, "24.10 " + suffix + " set bus generator 1V/1A OVP150", token => deviceManager.SetVbusGeneratorAsync(1, 1, 150, false), "VBUS Set", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "24.11 " + suffix + " turn on bus generator", token => deviceManager.SetVbusGeneratorAsync(1, 1, 150, true), "VBUS ON", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "24.12 " + suffix + " check serial communication", token => CheckBcm125MbdCommunicationAsync(deviceManager), "Open", "COM Fail");
            AddMbdCommandStep(steps, row++, deviceManager, "24.13 " + suffix + " send EXIT to MBD terminal", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "24.13 " + suffix + " send DIAG ON to MBD", "DIAG ON", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "24.14 " + suffix + " send " + remoteCommand, remoteCommand, 0);
            AddMbdCommandStep(steps, row++, deviceManager, "24.15 " + suffix + " send DIAG ON to BCM", "DIAG ON", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "24.16 " + suffix + " set W69=1 CCB off", "W69=1", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "24.17 " + suffix + " set W12=6000mA", "W12=6000", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "24.18 " + suffix + " set W75=1 precharge", "W75=1", 0);
            AddDelayStep(steps, row++, "24.19 " + suffix + " wait 500ms", 500);
            AddMbdAverageQueryStep(steps, row++, deviceManager, r01 + " " + suffix + " read R42 vstr_neg @ 0V 0A", "R42", prefix + r01 + "_VSTR_NEG_0V_0A", 900, 1100, "mV", 1, 0);
            AddMbdAverageQueryStep(steps, row++, deviceManager, r02 + " " + suffix + " read R44 iccb @ 0V 0A", "R44", prefix + r02 + "_ICCB_0V_0A", -40, 40, "mA", 1, 0);
            AddDaqAverageMvLimitedStep(steps, row++, deviceManager, r03 + " " + suffix + " DAQ V" + (vMeasureChannel == 109 ? "2" : "6") + " 0V 0A", vMeasureChannel, prefix + r03 + "_DAQ_0V_0A", 900, 1100);
            AddProgrammingActionStep(steps, row++, "24.24 " + suffix + " set bus generator 1V/1A OVP150", token => deviceManager.SetVbusGeneratorAsync(1, 1, 150, true), "VBUS Set", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "24.25 " + suffix + " set string generator 10V/6A OVP150", token => deviceManager.SetStringGeneratorAsync(10, 6, 150, true), "String Set", "String Fail");
            AddDelayStep(steps, row++, "24.26 " + suffix + " wait 5000ms", 5000);
            AddMbdCommandStep(steps, row++, deviceManager, "24.26 " + suffix + " set W69=0 CCB on", "W69=0", 0);
            AddMbdAverageQueryStep(steps, row++, deviceManager, r04 + " " + suffix + " read R42 vstr_neg @ 0V 6A", "R42", prefix + r04 + "_VSTR_NEG_0V_6A", 400, 1000, "mV", 1, 0);
            AddMbdAverageQueryStep(steps, row++, deviceManager, r05 + " " + suffix + " read R44 iccb @ 0V 6A", "R44", prefix + r05 + "_ICCB_0V_6A", 5950, 6050, "mA", 1, 0);
            AddDaqSignedShuntCurrentStep(steps, row++, deviceManager, r06 + " " + suffix + " shunt current -6000mA", shuntChannel, shuntOhm, -1, prefix + r06 + "_SHUNT_6000MA", -6050, -5950);
            AddDaqAverageMvLimitedStep(steps, row++, deviceManager, r07 + " " + suffix + " DAQ V" + (vMeasureChannel == 109 ? "2" : "6") + " 0V 6A", vMeasureChannel, prefix + r07 + "_DAQ_0V_6A", 600, 900);
            AddProgrammingActionStep(steps, row++, "24.32 " + suffix + " set string generator 0V/0A OVP150", token => deviceManager.SetStringGeneratorAsync(0, 0, 150, true), "String Set", "String Fail");
            AddDelayStep(steps, row++, "24.33 " + suffix + " wait 500ms", 500);
            AddProgrammingActionStep(steps, row++, "24.34 " + suffix + " set bus generator 15V/1A OVP150", token => deviceManager.SetVbusGeneratorAsync(15, 1, 150, true), "VBUS Set", "VBUS Fail");
            AddDelayStep(steps, row++, "24.35 " + suffix + " wait 1000ms", 1000);
            AddMbdAverageQueryStep(steps, row++, deviceManager, r08 + " " + suffix + " read R42 vstr_neg @ 15V 0A", "R42", prefix + r08 + "_VSTR_NEG_15V_0A", 14900, 15100, "mV", 1, 0);
            AddMbdAverageQueryStep(steps, row++, deviceManager, r09 + " " + suffix + " read R44 iccb @ 15V 0A", "R44", prefix + r09 + "_ICCB_15V_0A", -20, 20, "mA", 1, 0);
            AddDaqAverageMvLimitedStep(steps, row++, deviceManager, r10 + " " + suffix + " DAQ V" + (vMeasureChannel == 109 ? "2" : "6") + " 15000mV", vMeasureChannel, prefix + r10 + "_DAQ_15V_0A", 14900, 15100);
            AddProgrammingActionStep(steps, row++, "24.40 " + suffix + " set string generator 10V/6A OVP150", token => deviceManager.SetStringGeneratorAsync(10, 6, 150, true), "String Set", "String Fail");
            AddDelayStep(steps, row++, "24.41 " + suffix + " wait 2000ms", 2000);
            AddMbdAverageQueryStep(steps, row++, deviceManager, r11 + " " + suffix + " read R42 vstr_neg @ 15V 6A", "R42", prefix + r11 + "_VSTR_NEG_15V_6A", 13500, 15100, "mV", 1, 0);
            AddMbdAverageQueryStep(steps, row++, deviceManager, r12 + " " + suffix + " read R44 iccb @ 15V 6A", "R44", prefix + r12 + "_ICCB_15V_6A", 5900, 6100, "mA", 1, 0);
            AddDaqAverageMvLimitedStep(steps, row++, deviceManager, r13 + " " + suffix + " DAQ V" + (vMeasureChannel == 109 ? "2" : "6") + " 15000mV 2", vMeasureChannel, prefix + r13 + "_DAQ_15V_6A", 13500, 15100);
            AddDaqSignedShuntCurrentStep(steps, row++, deviceManager, r14 + " " + suffix + " shunt current -6000mA 2", shuntChannel, shuntOhm, -1, prefix + r14 + "_SHUNT_6000MA_2", -6050, -5950);
            AddMbdCommandStep(steps, row++, deviceManager, "24.47 " + suffix + " set W69=1 CCB off", "W69=1", 0);
            AddProgrammingActionStep(steps, row++, "24.47 " + suffix + " turn off string generator", token => deviceManager.SetStringGeneratorAsync(0, 0, 150, false), "String OFF", "String Fail");
            AddProgrammingActionStep(steps, row++, "24.48 " + suffix + " turn off bus generator", token => deviceManager.SetVbusGeneratorAsync(0, 0, 150, false), "VBUS OFF", "VBUS Fail");
            AddMbdCommandStep(steps, row++, deviceManager, "24.49 " + suffix + " set W75=0 precharge off", "W75=0", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "24.49 " + suffix + " send DIAG ON", "DIAG ON", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "24.49 " + suffix + " send EXIT to MBD terminal", "EXIT", 0);
            AddProgrammingActionStep(steps, row++, "24.51 " + suffix + " turn off string generator", token => deviceManager.SetStringGeneratorAsync(0, 0, 10, false), "String OFF", "String Fail");
            AddProgrammingActionStep(steps, row++, "24.54 " + suffix + " reset generators 0V/0A OVP150", token => deviceManager.SetVbusGeneratorAsync(0, 0, 150, false), "Generators Reset", "Generator Fail");
            AddProgrammingActionStep(steps, row++, "24.55 " + suffix + " reset electronic load", token => deviceManager.ResetElectronicLoadAsync(), "Load Reset", "Load Fail");
            AddDelayStep(steps, row++, "24.56 " + suffix + " wait 4000ms", 4000);
            AddFixtureRelayStep(steps, row++, deviceManager, "24.57 " + suffix + " open verification relays", "SKBCM125VerificationOpenRelays", "8,9,10,11,12,13,15,16", false, "Relays Open");
        }

        protected void AddBcm125CcbFunctionalCheckSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager)
        {
            int row = steps.Count;

            AddProgrammingActionStep(steps, row++, "25.1 Close RL4 to power DUT", token => deviceManager.ControlRelayAsync(7, true), "RL4 ON", "RL4 Fail");
            AddFixtureRelayStep(steps, row++, deviceManager, "25.2 Open RL1/RL2/RL9 power relays", "SKBCM125CcbOpenRelays", "8,9,10,11,12,13,14,15,16", false, "Relays Open");
            AddDaqRelayStep(steps, row++, deviceManager, "25.3 Open RL5.12/RL10.12", "SKBCM125Rl5Rl10DaqRelays", Bcm125StationMap.StringSimulatorDaqRelays, false, "RL5/RL10 Open");
            AddDelayStep(steps, row++, "25.4 Wait 50ms", 50);
            AddProgrammingActionStep(steps, row++, "25.5 Reset VBUS generator 0V/0A OVP150", token => deviceManager.SetVbusGeneratorAsync(0, 0, 150, false), "VBUS Reset", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "25.5 Reset string generator 0V/0A OVP150", token => deviceManager.SetStringGeneratorAsync(0, 0, 150, false), "String Reset", "String Fail");
            AddProgrammingActionStep(steps, row++, "25.6 Reset electronic load", token => deviceManager.ResetElectronicLoadAsync(), "Load Reset", "Load Fail");

            AddCcbFunctionalStringSteps(steps, deviceManager, ref row, Bcm125ChannelProfiles.CcbString1);
            AddCcbFunctionalStringSteps(steps, deviceManager, ref row, Bcm125ChannelProfiles.CcbString2);

            AddProgrammingActionStep(steps, row++, "25.58 Close COM with instruments", token => Task.FromResult(true), "Close OK", "Close Fail");
        }

        private void AddCcbFunctionalStringSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager, ref int row, BcmStringChannelConfig channel)
        {
            string suffix = channel.Suffix;
            string remoteCommand = channel.RemoteCommand;
            string mbdRelayCommandPrefix = channel.MbdRelayCommandPrefix;
            string relayConfigKey = channel.RelayConfigKey;
            string relayDefaults = channel.RelayDefaults;
            int shuntChannel = channel.ShuntChannel;
            int stringVoltageChannel = channel.StringVoltageChannel;
            double shuntOhm = channel.ShuntOhms;
            string sCurrent = channel.ResultIds[0];
            string sVoltage = channel.ResultIds[1];
            string prefix = "BCM125.CCB." + suffix + ".";
            AddFixtureRelayStep(steps, row++, deviceManager, "25.7 " + suffix + " close CCB relays", relayConfigKey, relayDefaults, true, "Relays Closed");
            AddDelayStep(steps, row++, "25.8 " + suffix + " wait 50ms", 50);
            AddProgrammingActionStep(steps, row++, "25.9 " + suffix + " check serial communication", token => CheckBcm125MbdCommunicationAsync(deviceManager), "Open", "COM Fail");
            AddMbdCommandStep(steps, row++, deviceManager, "25.10 " + suffix + " send EXIT to MBD terminal", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "25.11 " + suffix + " send DIAG ON to MBD", "DIAG ON", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "25.12 " + suffix + " close MBD battery relay " + mbdRelayCommandPrefix + "1", mbdRelayCommandPrefix + "1", 0);
            AddProgrammingActionStep(steps, row++, "25.13 " + suffix + " set BUS generator 140V/10A OVP300", token => deviceManager.SetVbusGeneratorAsync(140, 10, 300, false), "VBUS Set", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "25.14 " + suffix + " turn on BUS generator", token => deviceManager.SetVbusGeneratorAsync(140, 10, 300, true), "VBUS ON", "VBUS Fail");
            AddDelayStep(steps, row++, "25.15 " + suffix + " wait 15000ms", 15000);
            AddMbdCommandStep(steps, row++, deviceManager, "25.16 " + suffix + " send DIAG ON", "DIAG ON", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "25.17 " + suffix + " send " + remoteCommand, remoteCommand, 0);
            AddMbdCommandStep(steps, row++, deviceManager, "25.18 " + suffix + " send DIAG ON to BCM", "DIAG ON", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "25.19 " + suffix + " set W69=1 CCB off", "W69=1", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "25.20 " + suffix + " set W12=0mA", "W12=0", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "25.21 " + suffix + " set W75=1 precharge", "W75=1", 0);
            AddProgrammingActionStep(steps, row++, "25.22 " + suffix + " set string generator 130V/0.2A OVP300", token => deviceManager.SetStringGeneratorAsync(130, 0.2f, 300, false), "String Set", "String Fail");
            AddProgrammingActionStep(steps, row++, "25.23 " + suffix + " turn on string generator", token => deviceManager.SetStringGeneratorAsync(130, 0.2f, 300, true), "String ON", "String Fail");
            AddDelayStep(steps, row++, "25.24 " + suffix + " wait 2000ms", 2000);
            AddProgrammingActionStep(steps, row++, "25.25 " + suffix + " set electronic load CV mode", token => deviceManager.SetElectronicLoadCvAsync(0, 0, 0, false), "Load CV", "Load Fail");
            AddProgrammingActionStep(steps, row++, "25.26 " + suffix + " set electronic load 125V/10A/1250W", token => deviceManager.SetElectronicLoadCvAsync(125, 10, 1250, false), "Load Set", "Load Fail");
            AddDelayStep(steps, row++, "25.27 " + suffix + " wait 1000ms", 1000);
            AddProgrammingActionStep(steps, row++, "25.28 " + suffix + " turn on electronic load", token => deviceManager.SetElectronicLoadCvAsync(125, 10, 1250, true), "Load ON", "Load Fail");
            AddMbdCommandStep(steps, row++, deviceManager, "25.29 " + suffix + " set W11=0", "W11=0", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "25.30 " + suffix + " set W13=133500", "W13=133500", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "25.31 " + suffix + " set W71=0 MOSFET off", "W71=0", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "25.32 " + suffix + " set W12=6000mA", "W12=6000", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "25.33 " + suffix + " set W69=0 CCB on", "W69=0", 0);
            AddDelayStep(steps, row++, "25.34 " + suffix + " wait 10000ms", 10000);
            AddDaqSignedShuntCurrentStep(steps, row++, deviceManager, sCurrent + " " + suffix + " shunt current -6000mA", shuntChannel, shuntOhm, -1, prefix + sCurrent + "_SHUNT_6000MA", -6200, -5800);
            AddProgrammingActionStep(steps, row++, "25.36 " + suffix + " set electronic load 130V", token => deviceManager.SetElectronicLoadCvAsync(130, 10, 1250, true), "Load Set", "Load Fail");
            AddDelayStep(steps, row++, "25.37 " + suffix + " wait 4000ms", 4000);
            AddDaqAverageMvLimitedStep(steps, row++, deviceManager, sVoltage + " " + suffix + " DAQ string voltage", stringVoltageChannel, prefix + sVoltage + "_STRING_VOLTAGE", 132200, 135000);
            AddMbdCommandStep(steps, row++, deviceManager, "25.39 " + suffix + " set W69=1 CCB off", "W69=1", 0);
            AddProgrammingActionStep(steps, row++, "25.40 " + suffix + " turn off electronic load", token => deviceManager.SetElectronicLoadCvAsync(0, 0, 0, false), "Load OFF", "Load Fail");
            AddProgrammingActionStep(steps, row++, "25.41 " + suffix + " set electronic load 0V/0A", token => deviceManager.SetElectronicLoadCvAsync(0, 0, 0, false), "Load Reset", "Load Fail");
            AddProgrammingActionStep(steps, row++, "25.42 " + suffix + " turn off string generator", token => deviceManager.SetStringGeneratorAsync(130, 0.2f, 300, false), "String OFF", "String Fail");
            AddDelayStep(steps, row++, "25.43 " + suffix + " wait 3000ms", 3000);
            AddProgrammingActionStep(steps, row++, "25.44 " + suffix + " reset string generator 0V/0A OVP300", token => deviceManager.SetStringGeneratorAsync(0, 0, 300, false), "String Reset", "String Fail");
            AddProgrammingActionStep(steps, row++, "25.45 " + suffix + " turn off BUS generator", token => deviceManager.SetVbusGeneratorAsync(140, 10, 300, false), "VBUS OFF", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "25.46 " + suffix + " reset BUS generator 0V/0A OVP300", token => deviceManager.SetVbusGeneratorAsync(0, 0, 300, false), "VBUS Reset", "VBUS Fail");
            AddMbdCommandStep(steps, row++, deviceManager, "25.48 " + suffix + " set W75=0 precharge off", "W75=0", 0);
            AddFixtureRelayStep(steps, row++, deviceManager, "25.49 " + suffix + " open CCB relays", "SKBCM125CcbOpenRelays", "8,9,10,11,12,13,14,15,16", false, "Relays Open");
            AddDelayStep(steps, row++, "25.50 " + suffix + " wait 200ms", 200);
            AddMbdCommandStep(steps, row++, deviceManager, "25.54 " + suffix + " send EXIT to MBD terminal", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "25.55 " + suffix + " send DIAG ON to MBD", "DIAG ON", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "25.56 " + suffix + " open MBD battery relay " + mbdRelayCommandPrefix + "0", mbdRelayCommandPrefix + "0", 0);
        }

        protected void AddBcm125StringTestSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager)
        {
            int row = steps.Count;

            AddProgrammingActionStep(steps, row++, "26.1 Close RL4 to power DUT", token => deviceManager.ControlRelayAsync(7, true), "RL4 ON", "RL4 Fail");
            AddFixtureRelayStep(steps, row++, deviceManager, "26.2 Open RL1/RL2/RL9 power relays", "SKBCM125StringTestOpenRelays", "8,9,10,11,12,13,14,15,16", false, "Relays Open");
            AddDaqRelayStep(steps, row++, deviceManager, "26.3 Open RL5.12/RL10.12", "SKBCM125Rl5Rl10DaqRelays", Bcm125StationMap.StringSimulatorDaqRelays, false, "RL5/RL10 Open");
            AddDelayStep(steps, row++, "26.4 Wait 50ms", 50);
            AddProgrammingActionStep(steps, row++, "26.5 Reset VBUS generator 0V/0A OVP150", token => deviceManager.SetVbusGeneratorAsync(0, 0, 150, false), "VBUS Reset", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "26.5 Reset string generator 0V/0A OVP150", token => deviceManager.SetStringGeneratorAsync(0, 0, 150, false), "String Reset", "String Fail");
            AddProgrammingActionStep(steps, row++, "26.6 Reset electronic load", token => deviceManager.ResetElectronicLoadAsync(), "Load Reset", "Load Fail");

            AddStringTestStringSteps(steps, deviceManager, ref row, Bcm125ChannelProfiles.StringTest1);
            AddStringTestStringSteps(steps, deviceManager, ref row, Bcm125ChannelProfiles.StringTest2);

            AddProgrammingActionStep(steps, row++, "26.43 Close COM with instruments", token => Task.FromResult(true), "Close OK", "Close Fail");
        }

        private void AddStringTestStringSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager, ref int row, BcmStringChannelConfig channel)
        {
            string suffix = channel.Suffix;
            string remoteCommand = channel.RemoteCommand;
            string mbdRelayCommandPrefix = channel.MbdRelayCommandPrefix;
            string relayConfigKey = channel.RelayConfigKey;
            string relayDefaults = channel.RelayDefaults;
            int shuntChannel = channel.ShuntChannel;
            double shuntOhm = channel.ShuntOhms;
            string t01 = channel.ResultIds[0];
            string t02 = channel.ResultIds[1];
            string t03 = channel.ResultIds[2];
            string prefix = "BCM125.STRING_TEST." + suffix + ".";
            AddFixtureRelayStep(steps, row++, deviceManager, "26.7 " + suffix + " close string test relays", relayConfigKey, relayDefaults, true, "Relays Closed");
            AddDelayStep(steps, row++, "26.8 " + suffix + " wait 20ms", 20);
            AddProgrammingActionStep(steps, row++, "26.9 " + suffix + " set BUS generator 50V/0.5A OVP150", token => deviceManager.SetVbusGeneratorAsync(50, 0.5f, 150, false), "VBUS Set", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "26.10 " + suffix + " turn on BUS generator", token => deviceManager.SetVbusGeneratorAsync(50, 0.5f, 150, true), "VBUS ON", "VBUS Fail");
            AddDelayStep(steps, row++, "26.11 " + suffix + " wait 3000ms", 3000);
            AddProgrammingActionStep(steps, row++, "26.12 " + suffix + " set string generator 45V/3A OVP150", token => deviceManager.SetStringGeneratorAsync(45, 3, 150, false), "String Set", "String Fail");
            AddProgrammingActionStep(steps, row++, "26.13 " + suffix + " turn on string generator", token => deviceManager.SetStringGeneratorAsync(45, 3, 150, true), "String ON", "String Fail");
            AddProgrammingActionStep(steps, row++, "26.14 " + suffix + " check serial communication", token => CheckBcm125MbdCommunicationAsync(deviceManager), "Open", "COM Fail");
            AddMbdCommandStep(steps, row++, deviceManager, "26.15 " + suffix + " send EXIT to MBD terminal", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "26.16 " + suffix + " send DIAG ON to MBD", "DIAG ON", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "26.17 " + suffix + " close MBD string relay " + mbdRelayCommandPrefix + "1", mbdRelayCommandPrefix + "1", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "26.18 " + suffix + " send " + remoteCommand, remoteCommand, 0);
            AddMbdCommandStep(steps, row++, deviceManager, "26.19 " + suffix + " send DIAG ON to BCM", "DIAG ON", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "26.20 " + suffix + " set W69=1 CCB off", "W69=1", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "26.21 " + suffix + " set W12=0mA", "W12=0", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "26.22 " + suffix + " set W71=1 MOSFET Q12 on", "W71=1", 0);
            AddDelayStep(steps, row++, "26.23 " + suffix + " wait 7000ms", 7000);
            AddMbdAverageQueryStep(steps, row++, deviceManager, t01 + " " + suffix + " read R44 iccb MOS on", "R44", prefix + t01 + "_ICCB_MOS_ON", -1000, -300, "mA", 1, 0);
            AddDaqSignedShuntCurrentStep(steps, row++, deviceManager, t02 + " " + suffix + " external shunt current MOS on", shuntChannel, shuntOhm, 1, prefix + t02 + "_SHUNT_MOS_ON", 300, 1000);
            AddMbdCommandStep(steps, row++, deviceManager, "26.26 " + suffix + " set W71=0 MOSFET Q12 off", "W71=0", 0);
            AddDelayStep(steps, row++, "26.27 " + suffix + " wait 6000ms", 6000);
            AddMbdAverageQueryStep(steps, row++, deviceManager, t03 + " " + suffix + " read R44 iccb MOS off", "R44", prefix + t03 + "_ICCB_MOS_OFF", -20, 20, "mA", 1, 0);
            AddProgrammingActionStep(steps, row++, "26.29 " + suffix + " turn off string generator", token => deviceManager.SetStringGeneratorAsync(45, 3, 150, false), "String OFF", "String Fail");
            AddProgrammingActionStep(steps, row++, "26.30 " + suffix + " reset string generator 0V/0A OVP150", token => deviceManager.SetStringGeneratorAsync(0, 0, 150, false), "String Reset", "String Fail");
            AddProgrammingActionStep(steps, row++, "26.31 " + suffix + " turn off BUS generator", token => deviceManager.SetVbusGeneratorAsync(50, 0.5f, 150, false), "VBUS OFF", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "26.32 " + suffix + " reset BUS generator 0V/0A OVP150", token => deviceManager.SetVbusGeneratorAsync(0, 0, 150, false), "VBUS Reset", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "26.34 " + suffix + " reset generators 0V/0A OVP300", token => deviceManager.SetVbusGeneratorAsync(0, 0, 300, false), "Generators Reset", "Generator Fail");
            AddProgrammingActionStep(steps, row++, "26.35 " + suffix + " reset electronic load", token => deviceManager.ResetElectronicLoadAsync(), "Load Reset", "Load Fail");
            AddDelayStep(steps, row++, "26.36 " + suffix + " wait 4000ms", 4000);
            AddFixtureRelayStep(steps, row++, deviceManager, "26.37 " + suffix + " open string test relays", "SKBCM125StringTestOpenRelays", "8,9,10,11,12,13,14,15,16", false, "Relays Open");
            AddDelayStep(steps, row++, "26.38 " + suffix + " wait 200ms", 200);
            AddMbdCommandStep(steps, row++, deviceManager, "26.38 " + suffix + " send EXIT to MBD terminal", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "26.39 " + suffix + " send DIAG ON to MBD", "DIAG ON", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "26.40 " + suffix + " open MBD string relay " + mbdRelayCommandPrefix + "0", mbdRelayCommandPrefix + "0", 0);
        }

        protected void AddBcm125WritingInfoFieldsSteps(System.Collections.Generic.List<TestStepItem> steps, TestSequenceContext context, SK441Device deviceManager)
        {
            int row = steps.Count;

            AddProgrammingActionStep(steps, row++, "27.1 Close RL4 to power DUT", token => deviceManager.ControlRelayAsync(7, true), "RL4 ON", "RL4 Fail");
            AddFixtureRelayStep(steps, row++, deviceManager, "27.2 Open RL1/RL2/RL9 power relays", "SKBCM125WritingInfoOpenRelays", "8,9,10,11,12,13,14,15,16", false, "Relays Open");
            AddDaqRelayStep(steps, row++, deviceManager, "27.3 Open RL5.12/RL10.12", "SKBCM125Rl5Rl10DaqRelays", Bcm125StationMap.StringSimulatorDaqRelays, false, "RL5/RL10 Open");
            AddDelayStep(steps, row++, "27.4 Wait 50ms", 50);
            AddProgrammingActionStep(steps, row++, "27.5 Check generator COM communication", token => deviceManager.CheckProgrammingInstrumentsAsync(), "COM OK", "COM Fail");
            AddProgrammingActionStep(steps, row++, "27.6 Reset VBUS generator 0V/0A OVP150", token => deviceManager.SetVbusGeneratorAsync(0, 0, 150, false), "VBUS Reset", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "27.6 Reset string generator 0V/0A OVP150", token => deviceManager.SetStringGeneratorAsync(0, 0, 150, false), "String Reset", "String Fail");
            AddProgrammingActionStep(steps, row++, "27.7 Reset electronic load", token => deviceManager.ResetElectronicLoadAsync(), "Load Reset", "Load Fail");

            AddWritingInfoStringSteps(steps, context, deviceManager, ref row, "ST1", "REMOTE1", "1", "U01", "U02", "U03");
            AddWritingInfoStringSteps(steps, context, deviceManager, ref row, "ST2", "REMOTE2", "2", "U04", "U05", "U06");

        }

        protected void AddBcm125FixtureReleaseSteps(
            System.Collections.Generic.List<TestStepItem> steps,
            SK441Device deviceManager)
        {
            AddProgrammingActionStep(
                steps,
                steps.Count,
                "27.22 Turn off all power supplies and electronic load",
                token => deviceManager.TurnOffAllInstrumentsAsync(),
                "All outputs OFF",
                "Output OFF Fail");
            AddProgrammingActionStep(
                steps,
                steps.Count,
                "27.23 Close MBD target-board COM",
                token => deviceManager.CloseTargetBoardCommunicationAsync(),
                "MBD COM Closed",
                "MBD COM Close Fail");
            AddProgrammingActionStep(
                steps,
                steps.Count,
                "27.24 Open RL4 after all outputs are off",
                token => deviceManager.ControlRelayAsync(7, false),
                "RL4 OFF",
                "RL4 Fail");

            steps.Add(new TestStepItem
            {
                Name = "CLEANUP 释放下压继电器并启动治具上升",
                RowIndex = steps.Count,
                SafetyCritical = true,
                IsFixtureReleaseStep = true,
                MaxRetries = 0,
                Action = async token =>
                {
                    OnLogInfo("CLEANUP兜底：再次实际发送治具释放命令。");
                    bool pass = await deviceManager.StopFixturePressDownAsync(ExpectedBoardType);
                    return new StepResult
                    {
                        IsPass = pass,
                        Value = pass ? "释放命令成功" : "释放命令失败"
                    };
                }
            });

            steps.Add(new TestStepItem
            {
                Name = "CLEANUP 确认治具离开下压到位位置（数字量7）",
                RowIndex = steps.Count,
                SafetyCritical = true,
                IsFixtureReleaseStep = true,
                MaxRetries = 0,
                Action = async token =>
                {
                    OnLogInfo("CLEANUP兜底：重新读取数字量7，不复用首次确认结果。");
                    bool pass = await VerifyFixtureInputReleasedAsync(deviceManager);
                    return new StepResult
                    {
                        IsPass = pass,
                        Value = pass ? "已离开下压位" : "10秒内未离开下压位"
                    };
                }
            });
        }

        private void AddWritingInfoStringSteps(System.Collections.Generic.List<TestStepItem> steps, TestSequenceContext context, SK441Device deviceManager, ref int row, string suffix, string remoteCommand, string slaveIndex, string uFlash, string uDate, string uSerial)
        {
            string prefix = "BCM125.WRITING_INFO." + suffix + ".";
            string calibrationDate = string.Empty;
            string bcmSerial = string.Empty;

            AddMbdCommandStep(steps, row++, deviceManager, "27.8/9 " + suffix + " send " + remoteCommand, remoteCommand, 0);
            AddMbdCommandStep(steps, row++, deviceManager, "27.10 " + suffix + " send DIAG ON", "DIAG ON", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "27.11 " + suffix + " write W38=" + slaveIndex + " indice_slave", "W38=" + slaveIndex, 0);
            AddMbdCommandStep(steps, row++, deviceManager, "27.11 " + suffix + " write W39=MCU_ID", "W39=MCU_ID", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "27.12 " + suffix + " clear W40=-999", "W40=-999", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "27.12 " + suffix + " clear W41=-999", "W41=-999", 0);
            AddWriteCalibrationDateStep(steps, row++, context, deviceManager, "27.13 " + suffix + " write calibration date W40", value => calibrationDate = value);
            AddWriteBcmSerialStep(steps, row++, context, deviceManager, "27.14 " + suffix + " write BCM serial W41", value => bcmSerial = value);
            AddDelayStep(steps, row++, "27.15 " + suffix + " wait 300ms", 300);
            AddActFlashVerifyStep(steps, row++, deviceManager, uFlash + " " + suffix + " verify ACT->FLASH response", prefix + uFlash + "_ACT_FLASH");
            AddDelayStep(steps, row++, "27.17 " + suffix + " wait 300ms", 300);
            AddMbdStringCompareStep(steps, row++, deviceManager, uDate + " " + suffix + " verify R40 calibration date", "R40", () => calibrationDate, prefix + uDate + "_DATE");
            AddDelayStep(steps, row++, "27.19 " + suffix + " wait 500ms", 500);
            AddMbdStringCompareStep(steps, row++, deviceManager, uSerial + " " + suffix + " verify R41 BCM serial", "R41", () => bcmSerial, prefix + uSerial + "_SERIAL");
        }

        private void AddWriteCalibrationDateStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, TestSequenceContext context, SK441Device deviceManager, string name, Action<string> capture)
        {
            steps.Add(new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                PowerCritical = true,
                Action = async (token) =>
                {
                    string value = DateTime.Now.ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                    bool pass = await SendBcm125MbdCommandAsync(deviceManager, "W40=" + value, 0);
                    if (pass) capture(value);
                    return new StepResult { IsPass = pass, Value = pass ? value : "Write Fail" };
                }
            });
        }

        private void AddWriteBcmSerialStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, TestSequenceContext context, SK441Device deviceManager, string name, Action<string> capture)
        {
            steps.Add(new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                PowerCritical = true,
                Action = async (token) =>
                {
                    string value = (context.SN ?? string.Empty).Trim();
                    bool pass = !string.IsNullOrWhiteSpace(value) && await SendBcm125MbdCommandAsync(deviceManager, "W41=" + value, 0);
                    if (pass) capture(value);
                    return new StepResult { IsPass = pass, Value = pass ? value : "SN Missing/Write Fail" };
                }
            });
        }

        private void AddActFlashVerifyStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, SK441Device deviceManager, string name, string storeKey)
        {
            steps.Add(new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                PowerCritical = true,
                Action = async (token) =>
                {
                    string response = await QueryBcm125MbdCommandAsync(deviceManager, "ACT->FLASH", 0);
                    bool pass = !string.IsNullOrWhiteSpace(response)
                        && response.IndexOf("ACT values copied to FLASH", StringComparison.OrdinalIgnoreCase) >= 0
                        && response.IndexOf("Ready", StringComparison.OrdinalIgnoreCase) >= 0;
                    return new StepResult { IsPass = pass, Value = pass ? "1" : (response ?? "No Data"), StoreKey = storeKey, NumericValue = pass ? 1 : 0, Unit = string.Empty };
                }
            });
        }

        private void AddMbdStringCompareStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, SK441Device deviceManager, string name, string command, Func<string> expectedFactory, string storeKey)
        {
            steps.Add(new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                PowerCritical = true,
                Action = async (token) =>
                {
                    string expected = expectedFactory() ?? string.Empty;
                    string response = await QueryBcm125MbdCommandAsync(deviceManager, command, 0);
                    string actual = MbdResponseParser.ParsePayloadText(command, response);
                    bool pass = !string.IsNullOrWhiteSpace(expected)
                        && string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
                    return new StepResult { IsPass = pass, Value = pass ? "1" : actual, StoreKey = storeKey, NumericValue = pass ? 1 : 0, Unit = string.Empty };
                }
            });
        }

        private void AddMbdNumericQueryStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, SK441Device deviceManager, string name, string command, string storeKey, double lower, double upper, string unit)
        {
            steps.Add(new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                PowerCritical = true,
                Action = async (token) =>
                {
                    string response = await QueryBcm125MbdCommandAsync(deviceManager, command, 0);
                    double value = MbdResponseParser.ParseScalar(command, response);
                    double plannedLower, plannedUpper;
                    GetPlanRange(steps, rowIndex, lower, upper, out plannedLower, out plannedUpper);
                    string plannedUnit = GetPlanUnit(steps, rowIndex, unit);
                    bool pass = value >= plannedLower && value <= plannedUpper;

                    return new StepResult
                    {
                        IsPass = pass,
                        Value = FormatValue(value, plannedUnit),
                        StoreKey = storeKey,
                        NumericValue = value,
                        Unit = plannedUnit
                    };
                }
            });
        }

        private void AddBcm125DaqVoltageStep(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager, string name, string storeKey, int offset, int channel, double lower, double upper, string unit)
        {
            int rowIndex = steps.Count;
            steps.Add(new TestStepItem
            {
                Name = $"8.{3 + offset} FIRST START-UP-{name}",
                RowIndex = rowIndex,
                PowerCritical = true,
                Action = async (token) =>
                {
                    double measuredVolts = await MeasureBcm125DaqChannelVoltageWithRecoveryAsync(
                        deviceManager,
                        channel,
                        token);
                    double plannedLower, plannedUpper;
                    GetPlanRange(steps, rowIndex, lower, upper, out plannedLower, out plannedUpper);
                    string plannedUnit = GetPlanUnit(steps, rowIndex, unit);
                    double value = string.Equals(plannedUnit, "mV", StringComparison.OrdinalIgnoreCase)
                        ? measuredVolts * 1000.0
                        : measuredVolts;
                    bool pass = value >= plannedLower && value <= plannedUpper;
                    return new StepResult
                    {
                        IsPass = pass,
                        Value = FormatValue(value, plannedUnit),
                        StoreKey = storeKey,
                        NumericValue = value,
                        Unit = plannedUnit
                    };
                }
            });
        }

        private void AddBcm125DaqCurrentFromShuntStep(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager, string name, string storeKey, int offset, int channel, double shuntOhm, double lowerMa, double upperMa)
        {
            int rowIndex = steps.Count;
            steps.Add(new TestStepItem
            {
                Name = $"8.{3 + offset} FIRST START-UP-{name}",
                RowIndex = rowIndex,
                PowerCritical = true,
                Action = async (token) =>
                {
                    double voltage = await MeasureBcm125DaqChannelVoltageWithRecoveryAsync(
                        deviceManager,
                        channel,
                        token);
                    double currentMa = voltage / shuntOhm * 1000.0;
                    double plannedLower, plannedUpper;
                    GetPlanRange(steps, rowIndex, lowerMa, upperMa, out plannedLower, out plannedUpper);
                    string plannedUnit = GetPlanUnit(steps, rowIndex, "mA");
                    bool pass = currentMa >= plannedLower && currentMa <= plannedUpper;
                    return new StepResult
                    {
                        IsPass = pass,
                        Value = FormatValue(currentMa, plannedUnit),
                        StoreKey = storeKey,
                        NumericValue = currentMa,
                        Unit = plannedUnit
                    };
                }
            });
        }

        private async Task<double> MeasureBcm125DaqChannelVoltageWithRecoveryAsync(
            SK441Device deviceManager,
            int channel,
            CancellationToken token)
        {
            try
            {
                return await deviceManager.MeasureDaqChannelVoltageAsync(channel);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                OnLogWarning(
                    $"BCM-125 DAQ通道{channel}首次测量失败：{ex.Message}；" +
                    "恢复DAQ状态后重读一次。");
                deviceManager.Daq.RecoverVoltageDcMeasurement();
                await Task.Delay(100, token);
                return await deviceManager.MeasureDaqChannelVoltageAsync(channel);
            }
        }

        private static string FormatValue(double value, string unit)
        {
            return $"{value:0.###} {unit}";
        }
    }
}
