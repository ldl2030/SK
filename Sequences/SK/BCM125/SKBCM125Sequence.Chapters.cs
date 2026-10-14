using System;
using System.Diagnostics;
using System.Globalization;
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

        protected void AddBcm125ProgrammingSteps(System.Collections.Generic.List<TestStepItem> steps, TestSequenceContext context, SK441Device deviceManager)
        {
            int row = steps.Count;
            int chapterStart = row;

            AddProgrammingActionStep(steps, row++, "9.1 检查编程电源及两颗MCU现有固件，决定是否烧录", async token =>
                await deviceManager.CheckBcm125ProgrammingInstrumentsAsync() &&
                await EvaluateBcm125FirmwareAsync(context, deviceManager, token), "Firmware Checked", "Firmware Check Fail");
            AddProgrammingActionStep(steps, row++, "9.1 关闭仪器输出并复位设定值为0", token => deviceManager.TurnOffAllInstrumentsAsync(), "Off/Reset", "Off Fail");
            AddFixtureRelayStep(steps, row++, deviceManager, "9.2 BCM1 闭合RLP1/RLP2/RLP3连接J4", "SKBCM125Rlp1To3Relays", Bcm125StationMap.ProgrammingBcm1FixtureRelays, true, "RLP1-3 ON");
            AddDelayStep(steps, row++, "9.3 BCM1 等待300ms", 300);
            AddProgrammingActionStep(steps, row++, "9.4 BCM1 闭合RL4后开启ANS给DUT供电", token => deviceManager.CloseRl4ThenEnableAnsAsync(7, 140, 1), "RL4/ANS ON", "Safe Power-On Fail");
            AddMbdStartupReadinessDelayStep(steps, row++, deviceManager, "9.5 BCM1 等待6000ms", 6000, 2000);
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
            AddFixtureRelayStep(steps, row++, deviceManager, "9.16 BCM1 断开RLP1/RLP2/RLP3", "SKBCM125Rlp1To3Relays", Bcm125StationMap.ProgrammingBcm1FixtureRelays, false, "RLP1-3 OFF");

            AddFixtureRelayStep(steps, row++, deviceManager, "9.17 BCM2 闭合RLP5/RLP6/RLP7连接J8", "SKBCM125Rlp5To7Relays", Bcm125StationMap.ProgrammingBcm2FixtureRelays, true, "RLP5-7 ON");
            AddDelayStep(steps, row++, "9.18 BCM2 等待300ms", 300);
            AddMbdCommandStep(steps, row++, deviceManager, "9.19 BCM2 RESET置位 W86=1", "W86=1", 50);
            AddDaqRelayStep(steps, row++, deviceManager, "9.20 BCM2 闭合RLP8=DAQ CH6引入BOOT0 3.3V", "SKBCM125Rlp8DaqRelays", Bcm125StationMap.ProgrammingBcm2DaqRelay, true, "RLP8 ON");
            AddDelayStep(steps, row++, "9.21 BCM2 等待50ms", 50);
            AddMbdCommandStep(steps, row++, deviceManager, "9.22 BCM2 RESET释放 W86=0", "W86=0 ", 50);
            AddFirmwareFlashResultStep(steps, row++, deviceManager, "C02 BCM2 TTL烧录BCM_125_V8.hex", "BCM125.PROGRAMMING.C02_BCM2_FLASH");
            AddMbdCommandStep(steps, row++, deviceManager, "9.24 BCM2 烧录后RESET置位 W86=1", "W86=1", 50);
            AddDaqRelayStep(steps, row++, deviceManager, "9.25 BCM2 断开RLP8=DAQ CH6", "SKBCM125Rlp8DaqRelays", Bcm125StationMap.ProgrammingBcm2DaqRelay, false, "RLP8 OFF");
            AddMbdCommandStep(steps, row++, deviceManager, "9.26 BCM2 RESET释放 W86=0", "W86=0", 50);
            AddFixtureRelayStep(steps, row++, deviceManager, "9.27 BCM2 断开RLP5/RLP6/RLP7", "SKBCM125Rlp5To7Relays", Bcm125StationMap.ProgrammingBcm2FixtureRelays, false, "RLP5-7 OFF");
            AddProgrammingActionStep(steps, row++, "9.28 关闭USB-B/MBD串口通信", token => Task.FromResult(true), "Close OK", "Close Fail");

            // Keep stable XML/result IDs and show skipped physical actions. The
            // firmware decision is made before touching TTL, BOOT0 or RESET.
            for (int offset = 1; offset < row - chapterStart; offset++)
            {
                if (offset == 12 || offset == 23) continue; // C01/C02 own their result.
                int controller = (offset == 2 || offset == 3 || (offset >= 8 && offset <= 16))
                    ? 0 : (offset >= 17 && offset <= 27) ? 1 : -1;
                TestStepItem step = steps[chapterStart + offset];
                var originalAction = step.Action;
                step.Action = async token =>
                {
                    token.ThrowIfCancellationRequested();
                    if (!_firmwarePrecheckCompleted)
                        return new StepResult { IsPass = false, Value = "Firmware precheck missing" };
                    bool program = controller < 0
                        ? _programController.Any(value => value)
                        : _programController[controller];
                    if (!program)
                        return new StepResult { IsPass = true, Value = "SKIPPED (firmware checked)" };
                    return await originalAction(token);
                };
            }
        }

        private bool NeedsBcm125Programming(bool versionMatches)
        {
            return _forceFirmwareProgramming || !versionMatches;
        }

        private async Task<bool> EvaluateBcm125FirmwareAsync(
            TestSequenceContext context, SK441Device device, CancellationToken token)
        {
            _firmwarePrecheckCompleted = false;
            if (!await SendBcm125MbdCommandAsync(device, "EXIT", 0)) return false;
            token.ThrowIfCancellationRequested();
            try
            {
                string safeResponse = await QueryBcm125MbdCommandAsync(device, "R15", 0);
                int[] safeStatus = MbdResponseParser.ParseIntegerVector("R15", safeResponse, 16);
                if (safeStatus[15] != 0)
                {
                    OnLogError("BCM125 SAFE内部CAN异常，烧录BCM不能修复；禁止准备Bootloader。");
                    return false;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                OnLogError("BCM125烧录前SAFE通信检查失败：" + ex.Message);
                return false;
            }

            for (int controller = 0; controller < 2; controller++)
            {
                token.ThrowIfCancellationRequested();
                string expectedStepId = controller == 0 ? "BCM125.CH11.S004.E01" : "BCM125.CH11.S007.E02";
                string expected = context.TestPlan.Definition.Steps.Single(
                    definition => definition.StepId == expectedStepId).ExpectedValue;
                if (string.IsNullOrWhiteSpace(expected))
                    throw new TestPlanConfigurationException(expectedStepId + " has no ExpectedValue.");
                _precheckedFirmware[controller] = null;
                try
                {
                    if (await SendBcm125MbdCommandAsync(device, "REMOTE" + (controller + 1), 0))
                    {
                        string response = await QueryBcm125MbdCommandAsync(device, "R0", 0);
                        _precheckedFirmware[controller] = MbdResponseParser.ParsePayloadText("R0", response).Trim('"');
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    OnLogWarning($"BCM125 MCU{controller + 1}固件预检未通过：{ex.Message}");
                }
                token.ThrowIfCancellationRequested();
                if (!await SendBcm125MbdCommandAsync(device, "EXIT", 0))
                {
                    OnLogError("固件预检后无法返回MBD根终端，禁止继续烧录。");
                    return false;
                }
                bool matches = string.Equals(_precheckedFirmware[controller], expected, StringComparison.OrdinalIgnoreCase);
                _programController[controller] = NeedsBcm125Programming(matches);
                if (_programController[controller] && SkipFirmwareProgramming)
                {
                    OnLogError($"MCU{controller + 1}固件不匹配或无响应，但当前禁止烧录，不能按通过放行。");
                    return false;
                }
                OnLogInfo($"BCM125 MCU{controller + 1}固件预检：当前={_precheckedFirmware[controller] ?? "无有效版本"}，目标={expected}；" +
                    (_programController[controller] ? (_forceFirmwareProgramming ? "强制烧录。" : "按需烧录。") : "版本及通信正常，跳过TTL/BOOT0/RESET烧录操作。"));
            }
            if (_programController.Any(value => value) && !device.SkipComInit)
            {
                string firmwarePath;
                string firmwareError;
                if (!device.TryResolveBcm125FirmwarePath(Bcm125Commands.FirmwareFile, out firmwarePath, out firmwareError))
                {
                    OnLogError("进入Bootloader前检查固件失败：" + firmwareError);
                    return false;
                }
                if (string.IsNullOrWhiteSpace(ComName.uartComName) &&
                    string.IsNullOrWhiteSpace(System.Configuration.ConfigurationManager.AppSettings["TtlPort"]))
                {
                    OnLogError("需要烧录但没有配置独立TTL串口；禁止切换BOOT0/RESET。");
                    return false;
                }
            }
            _firmwarePrecheckCompleted = true;
            return true;
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

        private void AddPowerReadbackDelayStep(
            System.Collections.Generic.List<TestStepItem> steps,
            int rowIndex,
            SK441Device deviceManager,
            string name,
            int delayMs,
            int minimumWaitMs,
            string domain,
            float targetVoltage,
            bool timeoutIsFailure = true)
        {
            steps.Add(new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                PowerCritical = timeoutIsFailure,
                Action = async token =>
                {
                    int maximumWaitMs = GetPlanDelayMs(steps, rowIndex, delayMs);
                    if (maximumWaitMs == 0)
                        return new StepResult { IsPass = true, Value = "0 ms" };

                    var timer = Stopwatch.StartNew();
                    bool stable = await deviceManager.WaitForPowerVoltageStableAsync(
                        domain,
                        targetVoltage,
                        Math.Min(minimumWaitMs, maximumWaitMs),
                        maximumWaitMs,
                        token,
                        reportTimeoutAsError: timeoutIsFailure);
                    return new StepResult
                    {
                        IsPass = stable || !timeoutIsFailure,
                        IsWarning = !stable && !timeoutIsFailure,
                        Value = stable
                            ? timer.ElapsedMilliseconds + " ms (power stable)"
                            : maximumWaitMs + (timeoutIsFailure
                                ? " ms (power not stable)"
                                : " ms (TIMEOUT, continued)")
                    };
                }
            });
        }

        private void AddMbdStartupReadinessDelayStep(
            System.Collections.Generic.List<TestStepItem> steps,
            int rowIndex,
            SK441Device deviceManager,
            string name,
            int delayMs,
            int minimumWaitMs)
        {
            steps.Add(new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                PowerCritical = true,
                Action = async token =>
                {
                    int maximumWaitMs = GetPlanDelayMs(steps, rowIndex, delayMs);
                    if (maximumWaitMs == 0)
                        return new StepResult { IsPass = true, Value = "0 ms" };

                    var timer = Stopwatch.StartNew();
                    await Task.Delay(Math.Min(minimumWaitMs, maximumWaitMs), token);
                    while (timer.ElapsedMilliseconds < maximumWaitMs)
                    {
                        token.ThrowIfCancellationRequested();
                        if (await deviceManager.CheckMbdCommunicationAsync())
                        {
                            OnLogInfo(name + "：MBD串口已响应，提前结束启动等待。");
                            return new StepResult
                            {
                                IsPass = true,
                                Value = timer.ElapsedMilliseconds + " ms (MBD ready)"
                            };
                        }

                        int remainingMs = maximumWaitMs - (int)timer.ElapsedMilliseconds;
                        if (remainingMs > 0)
                            await Task.Delay(Math.Min(300, remainingMs), token);
                    }

                    // The following command or communication step makes the formal decision.
                    OnLogWarning(name + "：MBD未在等待上限内响应，继续后续正式通信步骤。");
                    return new StepResult { IsPass = true, Value = maximumWaitMs + " ms" };
                }
            });
        }

        private sealed class Bcm125ReadinessProbe
        {
            public string Command { get; set; }
            public int? DaqChannel { get; set; }
            public double? DaqScale { get; set; }
            public string ResultStepId { get; set; }
            public bool RequireElectronicLoadOn { get; set; }
            public bool FailOnTimeout { get; set; }
        }

        private void AddMeasuredReadinessDelayStep(
            System.Collections.Generic.List<TestStepItem> steps,
            int rowIndex,
            SK441Device deviceManager,
            string name,
            int delayMs,
            int minimumWaitMs,
            params Bcm125ReadinessProbe[] probes)
        {
            steps.Add(new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                PowerCritical = true,
                Action = async token =>
                {
                    int maximumWaitMs = GetPlanDelayMs(steps, rowIndex, delayMs);
                    var definitions = probes.Select(probe =>
                    {
                        TestStepItem resultStep = steps.SingleOrDefault(step =>
                            string.Equals(step.StepId, probe.ResultStepId, StringComparison.OrdinalIgnoreCase));
                        if (resultStep?.PlanDefinition?.LowerLimit == null ||
                            resultStep.PlanDefinition.UpperLimit == null ||
                            steps.IndexOf(resultStep) <= rowIndex)
                        {
                            throw new TestPlanConfigurationException(
                                "BCM125 readiness requires a later XML result with limits: " +
                                probe.ResultStepId + ".");
                        }
                        return resultStep.PlanDefinition;
                    }).ToArray();

                    if (maximumWaitMs == 0 || deviceManager.SkipComInit)
                    {
                        if (maximumWaitMs > 0)
                            await Task.Delay(maximumWaitMs, token);
                        return new StepResult { IsPass = true, Value = maximumWaitMs + " ms" };
                    }

                    var timer = Stopwatch.StartNew();
                    int stableSamples = 0;
                    int minimumMs = Math.Min(minimumWaitMs, maximumWaitMs);
                    bool warned = false;
                    bool requireLoadOn = probes.Any(probe => probe.RequireElectronicLoadOn);
                    bool failOnTimeout = probes.Any(probe => probe.FailOnTimeout);
                    bool? lastLoadOn = null;
                    long lastLoadCheckMs = -2000;
                    var lastValues = Enumerable.Repeat(double.NaN, probes.Length).ToArray();
                    while (timer.ElapsedMilliseconds < maximumWaitMs)
                    {
                        token.ThrowIfCancellationRequested();
                        if (timer.ElapsedMilliseconds < minimumMs)
                        {
                            int remainingMinimumMs = minimumMs - (int)timer.ElapsedMilliseconds;
                            await Task.Delay(Math.Min(200, remainingMinimumMs), token);
                            continue;
                        }

                        try
                        {
                            bool ready = true;
                            for (int i = 0; i < probes.Length; i++)
                            {
                                double value;
                                if (probes[i].DaqChannel.HasValue)
                                {
                                    value = await deviceManager.MeasureDaqChannelVoltageAverageAsync(
                                        probes[i].DaqChannel.Value, 2, 0, token, false) *
                                        (probes[i].DaqScale ?? 1000.0);
                                }
                                else
                                {
                                    string response = await QueryBcm125MbdCommandAsync(
                                        deviceManager, probes[i].Command, 0, false);
                                    value = MbdResponseParser.ParseScalar(probes[i].Command, response);
                                }
                                lastValues[i] = value;
                                if (double.IsNaN(value) || double.IsInfinity(value) ||
                                    value < definitions[i].LowerLimit.Value ||
                                    value > definitions[i].UpperLimit.Value)
                                    ready = false;
                            }

                            if (requireLoadOn)
                            {
                                // INP? is logged by the driver. Poll it less often while
                                // the DAQ is still out of range, but check every ready sample.
                                if (ready || timer.ElapsedMilliseconds - lastLoadCheckMs >= 2000)
                                {
                                    lastLoadOn = deviceManager.ElectronicLoad.GetInputState();
                                    lastLoadCheckMs = timer.ElapsedMilliseconds;
                                }
                                ready &= lastLoadOn == true;
                            }

                            stableSamples = ready ? stableSamples + 1 : 0;
                            if (stableSamples >= 3)
                            {
                                OnLogInfo(name + "：连续三次达到XML测量范围，等待" +
                                    timer.ElapsedMilliseconds + "ms后继续；正式测量行仍会重新读取。");
                                return new StepResult
                                {
                                    IsPass = true,
                                    Value = timer.ElapsedMilliseconds + " ms (ready)"
                                };
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            stableSamples = 0;
                            if (!warned)
                            {
                                OnLogWarning(name + "就绪探测暂未成功：" + ex.Message);
                                warned = true;
                            }
                        }

                        int remainingMs = maximumWaitMs - (int)timer.ElapsedMilliseconds;
                        if (remainingMs > 0)
                            await Task.Delay(Math.Min(300, remainingMs), token);
                    }

                    string loadState = !requireLoadOn ? string.Empty :
                        ", load=" + (lastLoadOn.HasValue ?
                            (lastLoadOn.Value ? "ON" : "OFF") : "unknown");
                    string measurements = string.Join(", ", probes.Select((probe, index) =>
                        (probe.DaqChannel.HasValue ? "DAQ" + probe.DaqChannel.Value : probe.Command) +
                        "=" + (double.IsNaN(lastValues[index]) ? "unknown" :
                            lastValues[index].ToString("0.###", CultureInfo.InvariantCulture) +
                            (definitions[index].Unit ?? string.Empty))));
                    string detail = maximumWaitMs + " ms (not stable" + loadState +
                        (measurements.Length > 0 ? ", " + measurements : string.Empty) + ")";
                    OnLogWarning(name + "在" + maximumWaitMs + "ms内未连续就绪：" + detail +
                        (failOnTimeout ? "；该步骤判失败。" : "；继续正式测量。"));
                    return new StepResult { IsPass = !failOnTimeout, Value = detail };
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

        private static bool AllStoredValuesPassed(TestSequenceContext context, params string[] keys)
        {
            if (context == null)
                return false;
            foreach (string key in keys)
            {
                TestSequenceValue value;
                if (!context.TryGetValue(key, out value) || !value.Pass)
                    return false;
            }
            return true;
        }

        private void AddConditionalFlashStep(
            System.Collections.Generic.List<TestStepItem> steps,
            int rowIndex,
            TestSequenceContext context,
            SK441Device deviceManager,
            string name,
            params string[] prerequisiteKeys)
        {
            steps.Add(new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                PowerCritical = true,
                Action = async token =>
                {
                    if (!AllStoredValuesPassed(context, prerequisiteKeys))
                    {
                        OnLogWarning(name + "：校准值未成功写入，跳过ACT->FLASH。");
                        return new StepResult { IsPass = false, Value = "Source Failed" };
                    }
                    bool stored = await SendBcm125MbdCommandAsync(deviceManager, "ACT->FLASH", 0);
                    return new StepResult
                    {
                        IsPass = stored,
                        Value = stored ? "Stored" : "Store Fail"
                    };
                }
            });
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

        private async Task<bool> CheckBcm125MbdCommunicationAsync(
            SK441Device deviceManager,
            bool synchronizeAfterPowerChange = false)
        {
            if (!synchronizeAfterPowerChange && IsBcm125RemoteTerminal &&
                deviceManager.IsTargetBoardCommunicationConnected)
            {
                OnLogInfo(
                    "BCM-125已处于REMOTE终端且串口保持连接；跳过空命令探测，避免破坏终端上下文。");
                return true;
            }

            if (!deviceManager.IsTargetBoardCommunicationConnected)
            {
                bool connected = await deviceManager.CheckMbdCommunicationAsync();
                if (!connected)
                {
                    _mbdTerminalState = Bcm125MbdTerminalState.Unknown;
                    return false;
                }
            }

            if (!synchronizeAfterPowerChange &&
                _mbdTerminalState == Bcm125MbdTerminalState.Mbd)
                return true;

            OnLogInfo(
                synchronizeAfterPowerChange
                    ? "BCM-125供电路径变化后发送EXIT验证串口并同步MBD根终端。"
                    : "BCM-125终端状态未知或连接刚恢复；发送EXIT同步到MBD根终端。");
            bool synchronized = await deviceManager.SendMbdCommandAsync("EXIT", 0);
            if (!synchronized)
            {
                _mbdTerminalState = Bcm125MbdTerminalState.Unknown;
                if (synchronizeAfterPowerChange && !deviceManager.SkipComInit)
                    LogBcm125DischargePowerSnapshot(deviceManager);
                return false;
            }

            _mbdTerminalState = Bcm125MbdTerminalState.Mbd;
            return true;
        }

        private void LogBcm125DischargePowerSnapshot(SK441Device deviceManager)
        {
            try
            {
                bool outputOn;
                float voltage;
                float current;
                deviceManager.HengHuiPower2.ReadOutputTelemetry(
                    400, out outputOn, out voltage, out current);
                OnLogWarning($"BCM-125串口失联时String电源回读：" +
                    $"输出={(outputOn ? "ON" : "OFF")}, {voltage:0.###}V, {current:0.###}A。");
            }
            catch (Exception ex)
            {
                OnLogWarning("BCM-125串口失联时String电源回读失败：" + ex.Message);
            }

            try
            {
                float voltage;
                float current;
                ushort status1;
                ushort status2;
                deviceManager.AnsPower.ReadVoltageCurrent(out voltage, out current);
                deviceManager.AnsPower.ReadStatus(out status1, out status2);
                OnLogWarning($"BCM-125串口失联时ANS电源回读：" +
                    $"{voltage:0.###}V, {current:0.###}A, 状态=0x{status1:X4}/0x{status2:X4}。");
            }
            catch (Exception ex)
            {
                OnLogWarning("BCM-125串口失联时ANS电源回读失败：" + ex.Message);
            }
        }

        private async Task<bool> SendBcm125MbdCommandAsync(
            SK441Device deviceManager,
            string command,
            int waitAfterMs)
        {
            string normalized = (command ?? string.Empty).Trim().ToUpperInvariant();
            bool enteringRemote = normalized == "REMOTE1" || normalized == "REMOTE2";
            bool rootOnlyCommand = IsBcm125MbdRootOnlyCommand(normalized);
            bool remoteOnlyCommand = IsBcm125RemoteOnlyCommand(normalized);

            if (rootOnlyCommand && _mbdTerminalState != Bcm125MbdTerminalState.Mbd)
            {
                OnLogInfo(
                    $"BCM-125根终端指令{normalized}执行前先发送EXIT同步MBD。");
                if (!await deviceManager.SendMbdCommandAsync("EXIT", 0))
                {
                    _mbdTerminalState = Bcm125MbdTerminalState.Unknown;
                    return false;
                }

                _mbdTerminalState = Bcm125MbdTerminalState.Mbd;
            }

            if (enteringRemote && _mbdTerminalState != Bcm125MbdTerminalState.Mbd)
            {
                OnLogInfo(
                    $"BCM-125进入{normalized}前先发送EXIT，同步到MBD根终端。");
                if (!await deviceManager.SendMbdCommandAsync("EXIT", 0))
                {
                    _mbdTerminalState = Bcm125MbdTerminalState.Unknown;
                    return false;
                }

                _mbdTerminalState = Bcm125MbdTerminalState.Mbd;
            }

            if (remoteOnlyCommand && !IsBcm125RemoteTerminal)
            {
                OnLogWarning(
                    $"BCM-125拒绝在{_mbdTerminalState}终端执行SLAVE专用指令{normalized}；" +
                    "必须先由REMOTE1或REMOTE2确认目标MCU。");
                return false;
            }

            if (normalized == "DIAG ON" &&
                _mbdTerminalState == Bcm125MbdTerminalState.Unknown)
            {
                OnLogWarning(
                    "BCM-125拒绝在未知终端执行DIAG ON；必须先发送EXIT或确认REMOTE1/2。");
                return false;
            }

            // EXIT deliberately leaves the remote terminal. If it times out,
            // recover the MBD root connection rather than restoring the old
            // REMOTE1/2 context before the next chapter can select its target.
            bool keepCurrentConnection = IsBcm125RemoteTerminal && normalized != "EXIT";
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
                _mbdTerminalState = Bcm125MbdTerminalState.Mbd;

            return true;
        }

        private static bool IsBcm125MbdRootOnlyCommand(string normalizedCommand)
        {
            return normalizedCommand.StartsWith("W86=", StringComparison.Ordinal) ||
                   normalizedCommand.StartsWith("W91=", StringComparison.Ordinal) ||
                   normalizedCommand.StartsWith("W94=", StringComparison.Ordinal) ||
                   normalizedCommand.StartsWith("W95=", StringComparison.Ordinal);
        }

        private static bool IsBcm125RemoteOnlyCommand(string normalizedCommand)
        {
            if (string.Equals(normalizedCommand, "ACT->FLASH", StringComparison.Ordinal))
                return true;

            return normalizedCommand.StartsWith("W", StringComparison.Ordinal) &&
                   !IsBcm125MbdRootOnlyCommand(normalizedCommand);
        }

        private static bool IsBcm125MbdRootOnlyQuery(string normalizedCommand)
        {
            return normalizedCommand == "R14" ||
                   normalizedCommand == "R15" ||
                   normalizedCommand == "R68" ||
                   normalizedCommand == "R69" ||
                   normalizedCommand == "R81";
        }

        private async Task<string> QueryBcm125MbdCommandAsync(
            SK441Device deviceManager,
            string command,
            int waitAfterMs,
            bool logSuccessfulTrace = true)
        {
            string normalized = (command ?? string.Empty).Trim().ToUpperInvariant();
            bool rootOnlyQuery = IsBcm125MbdRootOnlyQuery(normalized);
            if (rootOnlyQuery && _mbdTerminalState != Bcm125MbdTerminalState.Mbd)
            {
                if (!await SendBcm125MbdCommandAsync(deviceManager, "EXIT", 0))
                {
                    throw new InvalidOperationException(
                        "BCM-125无法在查询" + normalized + "前同步到MBD根终端。");
                }
            }

            if (rootOnlyQuery)
            {
                string rootResponse = await deviceManager.QueryMbdCommandAsync(
                    command,
                    waitAfterMs,
                    logSuccessfulTrace);
                return RequireValidBcm125QueryResponse(command, rootResponse);
            }

            if (!IsBcm125RemoteTerminal)
            {
                throw new InvalidOperationException(
                    "BCM-125拒绝在" + _mbdTerminalState + "终端查询SLAVE寄存器" +
                    normalized + "；必须先确认REMOTE1或REMOTE2。");
            }

            Bcm125MbdTerminalState remoteState = _mbdTerminalState;
            try
            {
                string response = await deviceManager.QueryMbdCommandOnCurrentConnectionAsync(
                    command,
                    waitAfterMs,
                    logSuccessfulTrace);
                return RequireValidBcm125QueryResponse(command, response);
            }
            catch (Exception ex)
            {
                OnLogWarning(
                    $"BCM-125有状态查询失败：{command}，{ex.Message}；" +
                    "准备重新连接并恢复REMOTE终端状态。");
                if (!await RestoreBcm125RemoteTerminalAsync(deviceManager, remoteState))
                    throw;

                string retryResponse = await deviceManager.QueryMbdCommandOnCurrentConnectionAsync(
                    command,
                    waitAfterMs,
                    logSuccessfulTrace);
                return RequireValidBcm125QueryResponse(command, retryResponse);
            }
        }

        private static string RequireValidBcm125QueryResponse(
            string command,
            string response)
        {
            string error;
            if (!MbdResponseParser.IsSuccessfulQueryResponse(command, response, out error))
            {
                throw new InvalidOperationException(
                    "BCM-125查询响应无效：" + command + "，" + error +
                    " 原始响应=" + (response ?? "<null>"));
            }

            return response;
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

        private void AddStringPowerPathRelayStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, SK441Device deviceManager, string name, string configKey, string defaultRelays, bool close, string passValue)
        {
            AddProgrammingActionStep(
                steps,
                rowIndex,
                name,
                token => deviceManager.SetBcm125PowerPathRelaysAsync(
                    Bcm125StationMap.GetStringGeneratorPowerRelayIds()
                        .Concat(deviceManager.GetConfiguredRelayList(configKey, defaultRelays))
                        .Distinct()
                        .ToArray(),
                    close,
                    name),
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
                    token.ThrowIfCancellationRequested();
                    int controller = storeKey.IndexOf("C01", StringComparison.Ordinal) >= 0 ? 0 : 1;
                    if (!_firmwarePrecheckCompleted)
                        return new StepResult { IsPass = false, Value = "Firmware precheck missing", StoreKey = storeKey, NumericValue = 0 };
                    if (_programController[controller])
                    {
                        if (SkipFirmwareProgramming)
                            return new StepResult { IsPass = false, Value = "Programming disabled", StoreKey = storeKey, NumericValue = 0 };
                        bool flashed;
                        if (deviceManager.SkipComInit)
                        {
                            OnLogInfo($"[调试模式] 模拟MCU{controller + 1}烧录；verify={(!_skipFirmwareVerification ? "启用" : "跳过")}。");
                            flashed = true;
                        }
                        else
                        {
                            flashed = await deviceManager.FlashBcm125FirmwareAsync(
                                Bcm125Commands.FirmwareFile,
                                token);
                        }
                        return new StepResult
                        {
                            IsPass = flashed,
                            Value = flashed ? "1" : "0",
                            StoreKey = storeKey,
                            NumericValue = flashed ? 1 : 0,
                            Unit = string.Empty
                        };
                    }

                    OnLogInfo(name + "：现有应用固件通信及版本已通过预检，不重复烧录。版本=" + _precheckedFirmware[controller]);
                    return new StepResult
                    {
                        IsPass = true,
                        Value = "EXISTING " + _precheckedFirmware[controller],
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
            int powerCycleStart = row;

            AddProgrammingActionStep(steps, row++, "10.1 先关闭ANS输出再断开RL4", token => deviceManager.DisableAnsThenOpenRl4Async(7), "ANS/RL4 OFF", "Safe Power-Off Fail");
            AddDelayStep(steps, row++, "10.2 等待6500ms", 6500);
            AddProgrammingActionStep(steps, row++, "10.3 闭合RL4后重新开启ANS给DUT供电", token => deviceManager.CloseRl4ThenEnableAnsAsync(7, 140, 1), "RL4/ANS ON", "Safe Power-On Fail");
            AddProgrammingActionStep(steps, row++, "10.4 检查USB-B/MBD串口通信已打开", token => CheckBcm125MbdCommunicationAsync(deviceManager), "Open", "COM Fail");
            AddMbdCommandStep(steps, row++, deviceManager, "10.5 发送EXIT到MBD", "EXIT ", 0);
            AddCanBusQueryStep(steps, row++, deviceManager, "D01 读取R14检查BCM内部CAN响应", "R14", "BCM125.CAN.D01_R14_BCM", 1);
            AddCanBusQueryStep(steps, row++, deviceManager, "D02 读取R15检查SAFE内部CAN响应", "R15", "BCM125.CAN.D02_R15_SAFE", -1);
            AddProgrammingActionStep(steps, row++, "10.8 关闭USB-B/MBD串口通信", token => Task.FromResult(true), "Close OK", "Close Fail");
            for (int index = powerCycleStart; index < powerCycleStart + 3; index++)
            {
                TestStepItem step = steps[index];
                var originalAction = step.Action;
                step.Action = token =>
                {
                    token.ThrowIfCancellationRequested();
                    return _firmwarePrecheckCompleted && !_programController.Any(program => program)
                        ? Task.FromResult(new StepResult { IsPass = true, Value = "SKIPPED (no firmware write)" })
                        : originalAction(token);
                };
            }
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
                    string expected = steps[rowIndex].PlanDefinition.ExpectedValue;
                    if (string.IsNullOrWhiteSpace(expected))
                        throw new TestPlanConfigurationException(steps[rowIndex].StepId + " has no ExpectedValue.");
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
            AddMbdStartupReadinessDelayStep(steps, row++, deviceManager, "12.2 等待3000ms", 3000, 1000);
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
                    if (storeKey.EndsWith("_X", StringComparison.Ordinal))
                    {
                        // F01/F02 require x-y >= 2 and y <= 1.5. An adaptive
                        // startup wait must not sample x before that is possible.
                        const double minimumBaselineSeconds = 2.0 + 1.5;
                        int maximumWaitMs = GetPlanDelayMs(steps, rowIndex, 3000);
                        var timer = Stopwatch.StartNew();
                        while (!double.IsNaN(runningTime) && !double.IsInfinity(runningTime) &&
                            runningTime < minimumBaselineSeconds && timer.ElapsedMilliseconds < maximumWaitMs)
                        {
                            token.ThrowIfCancellationRequested();
                            int remainingMs = maximumWaitMs - (int)timer.ElapsedMilliseconds;
                            int waitMs = (int)Math.Min(remainingMs,
                                Math.Max(50, (minimumBaselineSeconds - runningTime) * 1000 + 50));
                            OnLogInfo($"{name}：复位前R10={runningTime:0.###}s，等待{waitMs}ms建立有效复位判定基线。");
                            await Task.Delay(waitMs, token);
                            response = await QueryBcm125MbdCommandAsync(deviceManager, "R10", 0);
                            runningTime = MbdResponseParser.ParseScalar("R10", response);
                        }
                        if (double.IsNaN(runningTime) || double.IsInfinity(runningTime) || runningTime < minimumBaselineSeconds)
                            throw new InvalidOperationException("复位前running_time未达到有效基线，不能判定复位结果。");
                    }
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
                    OnLogInfo($"{name}：x={x:0.###}s，y={y:0.###}s，x-y={x - y:0.###}s；" +
                        "要求x-y≥2s且0.5s≤y≤1.5s，结果=" + (pass ? "PASS" : "FAIL"));
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
            bool mosOff = storeKey.StartsWith("BCM125.STRING_TEST.", StringComparison.Ordinal) &&
                storeKey.EndsWith("_ICCB_MOS_OFF", StringComparison.Ordinal);
            steps.Add(new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                PowerCritical = true,
                MaxRetries = mosOff ? 0 : -1,
                Action = async (token) =>
                {
                    if (mosOff)
                    {
                        double offLower, offUpper;
                        GetPlanRange(steps, rowIndex, lower, upper, out offLower, out offUpper);
                        return await MeasureStringTestMosOffCurrentAsync(deviceManager, name,
                            storeKey, offLower, offUpper, GetPlanUnit(steps, rowIndex, unit), token);
                    }
                    double sum = 0;
                    double minimum = double.PositiveInfinity;
                    double maximum = double.NegativeInfinity;
                    int count = 0;
                    var sampleTimer = Stopwatch.StartNew();
                    long previousStartTimestamp = 0;
                    double shortestIntervalMs = double.PositiveInfinity;
                    double longestIntervalMs = 0;
                    for (int i = 0; i < sampleCount; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        // The interval is between transmissions. Response time
                        // already occupies that interval; never add it twice or
                        // send catch-up requests after a slow response.
                        if (i > 0)
                        {
                            double remainingMs = intervalMs -
                                (Stopwatch.GetTimestamp() - previousStartTimestamp) *
                                1000.0 / Stopwatch.Frequency;
                            if (remainingMs > 0)
                                await Task.Delay((int)Math.Ceiling(remainingMs), token);
                        }
                        token.ThrowIfCancellationRequested();
                        long requestedAt = Stopwatch.GetTimestamp();
                        string response = await QueryBcm125MbdCommandAsync(
                            deviceManager, command, 0, sampleCount <= 1);
                        long startTimestamp = deviceManager.SkipComInit
                            ? requestedAt : deviceManager.TargetBoard.LastCommandStartedTimestamp;
                        if (startTimestamp <= 0)
                            startTimestamp = requestedAt;
                        if (i > 0)
                        {
                            double actualIntervalMs = (startTimestamp - previousStartTimestamp) *
                                1000.0 / Stopwatch.Frequency;
                            shortestIntervalMs = Math.Min(shortestIntervalMs, actualIntervalMs);
                            longestIntervalMs = Math.Max(longestIntervalMs, actualIntervalMs);
                        }
                        previousStartTimestamp = startTimestamp;
                        double value = MbdResponseParser.ParseScalar(command, response);
                        sum += value;
                        minimum = Math.Min(minimum, value);
                        maximum = Math.Max(maximum, value);
                        count++;
                    }
                    double average = count > 0 ? sum / count : double.NaN;
                    double plannedLower, plannedUpper;
                    GetPlanRange(steps, rowIndex, lower, upper, out plannedLower, out plannedUpper);
                    string plannedUnit = GetPlanUnit(steps, rowIndex, unit);
                    bool pass = !double.IsNaN(average) && average >= plannedLower && average <= plannedUpper;
                    if (sampleCount > 1)
                        OnLogInfo($"{name} {command}采样{count}次：最小={minimum:0.###}，" +
                            $"最大={maximum:0.###}，平均={average:0.###}{plannedUnit}，" +
                            $"发送间隔要求≥{intervalMs}ms，实测={shortestIntervalMs:0.###}～{longestIntervalMs:0.###}ms，" +
                            $"总耗时={sampleTimer.ElapsedMilliseconds}ms，" +
                            $"限值=[{plannedLower:0.###},{plannedUpper:0.###}]。");
                    return new StepResult { IsPass = pass, Value = double.IsNaN(average) ? "No Data" : FormatValue(average, plannedUnit), StoreKey = storeKey, NumericValue = double.IsNaN(average) ? (double?)null : average, Unit = plannedUnit };
                }
            });
        }

        private sealed class StringTestOnWindow
        {
            public readonly string Id = Guid.NewGuid().ToString("N").Substring(0, 8);
            public readonly System.Collections.Generic.List<double> Values = new System.Collections.Generic.List<double>();
            public readonly System.Collections.Generic.List<double> TimesMs = new System.Collections.Generic.List<double>();
            public DaqVoltageSampleSeries ExternalSamples;
            public Exception InternalError;
            public Exception ExternalError;
        }

        private void AddStringTestMosOnWindowSteps(System.Collections.Generic.List<TestStepItem> steps,
            SK441Device deviceManager, ref int row, BcmStringChannelConfig channel)
        {
            StringTestOnWindow cachedWindow = null;
            string prefix = "BCM125.STRING_TEST." + channel.Suffix + ".";
            int internalRow = row++;
            string internalName = channel.ResultIds[0] + " " + channel.Suffix + " read R44 iccb MOS on";
            steps.Add(new TestStepItem
            {
                Name = internalName,
                RowIndex = internalRow,
                PowerCritical = true,
                MaxRetries = 0,
                Action = async token =>
                {
                    // A new attempt always invalidates previous-run/unused data.
                    cachedWindow = null;
                    StringTestOnWindow window = await CaptureStringTestMosOnWindowAsync(deviceManager,
                        channel.ShuntChannel, token);
                    token.ThrowIfCancellationRequested();
                    cachedWindow = window;
                    if (window.InternalError != null)
                    {
                        LogStringTestMosOnExternalRawSamples(internalName, window, channel.ShuntOhms);
                        throw new InvalidOperationException(internalName + " R44同窗采样无效，不使用部分平均：" +
                            window.InternalError.Message, window.InternalError);
                    }
                    double lower, upper;
                    GetPlanRange(steps, internalRow, -1000, -300, out lower, out upper);
                    string unit = GetPlanUnit(steps, internalRow, "mA");
                    StepResult result = CreateStringTestMosOnResult(window.Values.ToArray(),
                        deviceManager.Bcm125MosOnSampleCount,
                        prefix + channel.ResultIds[0] + "_ICCB_MOS_ON", lower, upper, unit);
                    OnLogInfo($"{internalName} 同窗{window.Id}：R44={window.Values.Count}点，" +
                        $"请求跨度={window.TimesMs.Last() - window.TimesMs.First():0.###}ms，" +
                        $"平均={result.NumericValue:0.###}mA，最小={window.Values.Min():0.###}mA，" +
                        $"最大={window.Values.Max():0.###}mA，限值=[{lower:0.###},{upper:0.###}]mA（全部读数参与）。");
                    if (!result.IsPass)
                    {
                        LogStringTestMosOnRawSamples(internalName, window);
                        LogStringTestMosOnExternalRawSamples(internalName, window, channel.ShuntOhms);
                    }
                    return result;
                }
            });
            string externalName = channel.ResultIds[1] + " " + channel.Suffix + " external shunt current MOS on";
            AddDaqSignedShuntCurrentStep(steps, row++, deviceManager, externalName,
                channel.ShuntChannel, channel.ShuntOhms,
                prefix + channel.ResultIds[1] + "_SHUNT_MOS_ON", 300, 1000,
                sharedWindowSamples: token => ConsumeStringTestOnExternalSamples(ref cachedWindow, externalName, token));
        }

        private DaqVoltageSampleSeries ConsumeStringTestOnExternalSamples(ref StringTestOnWindow cachedWindow,
            string name, CancellationToken token)
        {
            StringTestOnWindow window = cachedWindow;
            cachedWindow = null; // Consume once, including failure and cancellation.
            token.ThrowIfCancellationRequested();
            if (window == null)
                throw new InvalidOperationException(name + " 缺少紧前内部结果行的同窗数据，禁止重新采集/复用旧值。");
            if (window.ExternalError != null || window.ExternalSamples == null)
                throw new InvalidOperationException(name + " DAQ同窗采样无效，不重新采集：" +
                    (window.ExternalError?.Message ?? "No Data"), window.ExternalError);
            OnLogInfo($"{name} 使用同窗{window.Id}的{window.ExternalSamples.Voltages.Length}点DAQ缓存，无新增采集等待。");
            return window.ExternalSamples;
        }

        private static void ValidateStringTestMosOnWindowSettings(int internalCount, int internalIntervalMs,
            int externalCount, int externalIntervalMs)
        {
            if (internalCount < 2 || internalCount > 100 || internalIntervalMs < 100 || internalIntervalMs > 1000 ||
                externalCount < 2 || externalCount > 1000 || externalIntervalMs <= 0 ||
                internalCount * (long)internalIntervalMs != externalCount * (long)externalIntervalMs)
                throw new InvalidOperationException("MOS-on internal/DAQ acquisitions require matching fixed-window durations and valid sample counts/intervals.");
        }

        private async Task<StringTestOnWindow> CaptureStringTestMosOnWindowAsync(SK441Device deviceManager,
            int shuntChannel, CancellationToken token)
        {
            int count = deviceManager.Bcm125MosOnSampleCount;
            int intervalMs = deviceManager.Bcm125MosOnIntervalMs;
            ValidateStringTestMosOnWindowSettings(count, intervalMs,
                deviceManager.Bcm125StringTestSampleCount, deviceManager.Bcm125StringTestIntervalMs);
            token.ThrowIfCancellationRequested();
            var window = new StringTestOnWindow();
            var elapsed = new Stopwatch();
            using (var captureCancellation = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                Task<DaqVoltageSampleSeries> externalCapture = deviceManager.MeasureBcm125StringTestShuntSamplesAsync(
                    shuntChannel, captureCancellation.Token, onScanStarted: () => elapsed.Start());
                // A DAQ setup failure before INIT must not mask the independent R44 result.
                if (!elapsed.IsRunning) elapsed.Start();
                try
                {
                    try
                    {
                        await ReadStringTestCurrentWindowAsync(deviceManager, count, intervalMs, elapsed,
                            window.Values, window.TimesMs, token);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        window.InternalError = ex;
                        OnLogWarning($"同窗{window.Id} R44采集无效，已收{window.Values.Count}/{count}点：{ex.Message}");
                        LogStringTestMosOnRawSamples("R44无效窗口", window);
                    }
                    try { window.ExternalSamples = await externalCapture; }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        window.ExternalError = ex;
                        OnLogWarning($"同窗{window.Id} DAQ采集无效，外部结果行将保持NG：{ex.Message}");
                    }
                    token.ThrowIfCancellationRequested();
                    return window;
                }
                finally
                {
                    captureCancellation.Cancel();
                    // Always drain the DAQ abort/restore path before advancing or cleanup.
                    try { await externalCapture; }
                    catch (OperationCanceledException) { }
                    catch (Exception) { /* Reported by the acquisition path. */ }
                }
            }
        }

        private async Task ReadStringTestCurrentWindowAsync(SK441Device deviceManager, int count, int intervalMs,
            Stopwatch elapsed, System.Collections.Generic.List<double> values,
            System.Collections.Generic.List<double> times, CancellationToken token)
        {
            for (int index = 0; index < count; index++)
            {
                token.ThrowIfCancellationRequested();
                double targetMs = index * intervalMs;
                if (!deviceManager.SkipComInit)
                {
                    double remaining = targetMs - elapsed.Elapsed.TotalMilliseconds;
                    if (remaining > 0)
                        await Task.Delay((int)Math.Ceiling(remaining), token);
                    double lateMs = elapsed.Elapsed.TotalMilliseconds - targetMs;
                    if (lateMs > Math.Max(20.0, intervalMs * 0.25))
                        throw new InvalidOperationException($"R44 fixed window overrun at sample {index + 1}: {lateMs:0.###}ms late.");
                }
                double startMs = deviceManager.SkipComInit ? targetMs : elapsed.Elapsed.TotalMilliseconds;
                int queryBudgetMs = deviceManager.SkipComInit ? intervalMs :
                    Math.Max(1, (int)Math.Floor(targetMs + intervalMs - elapsed.Elapsed.TotalMilliseconds));
                string response = await QueryStringTestFixedWindowCommandAsync(deviceManager, "R44", token, queryBudgetMs);
                double value = MbdResponseParser.ParseScalar("R44", response);
                if (double.IsNaN(value) || double.IsInfinity(value))
                    throw new InvalidOperationException("R44 returned a non-finite current.");
                values.Add(value);
                times.Add(startMs);
                if (!deviceManager.SkipComInit && elapsed.Elapsed.TotalMilliseconds > targetMs + intervalMs)
                    throw new InvalidOperationException($"R44 sample {index + 1} did not complete within its {intervalMs}ms slot.");
            }
            double windowMs = count * (double)intervalMs;
            if (!deviceManager.SkipComInit && elapsed.Elapsed.TotalMilliseconds < windowMs)
                await Task.Delay((int)Math.Ceiling(windowMs - elapsed.Elapsed.TotalMilliseconds), token);
            token.ThrowIfCancellationRequested();
        }

        private void LogStringTestMosOnRawSamples(string name, StringTestOnWindow window)
        {
            for (int start = 0; start < window.Values.Count; start += 20)
                OnLogInfo($"{name} 同窗{window.Id} R44原始全样本（序号:DAQ_INIT后请求_t_ms/I_mA）：" +
                    string.Join("; ", Enumerable.Range(start, Math.Min(20, window.Values.Count - start))
                        .Select(i => string.Format(CultureInfo.InvariantCulture, "{0}:{1:0.###}/{2:0.###}",
                            i + 1, window.TimesMs[i], window.Values[i]))));
        }

        private void LogStringTestMosOnExternalRawSamples(string name, StringTestOnWindow window, double shuntOhm)
        {
            if (window.ExternalSamples == null) return;
            DaqVoltageSampleSeries series = window.ExternalSamples;
            for (int start = 0; start < series.Voltages.Length; start += 20)
                OnLogInfo($"{name} 同窗{window.Id} 内部NG关联外部分流原始（序号:DAQ_t_ms/V_mV/I_mA）：" +
                    string.Join("; ", Enumerable.Range(start, Math.Min(20, series.Voltages.Length - start))
                        .Select(i => string.Format(CultureInfo.InvariantCulture, "{0}:{1:0.###}/{2:0.######}/{3:0.###}",
                            i + 1, series.RelativeTimesSeconds[i] * 1000,
                            series.Voltages[i] * 1000, series.Voltages[i] / shuntOhm * 1000))));
        }

        private static StepResult CreateStringTestMosOnResult(double[] values, int expectedCount,
            string storeKey, double lower, double upper, string unit)
        {
            if (expectedCount < 2 || values == null || values.Length != expectedCount ||
                values.Any(v => double.IsNaN(v) || double.IsInfinity(v)))
                throw new InvalidOperationException("MOS-on current requires the complete finite R44 sample window.");
            double average = values.Average();
            return new StepResult { IsPass = average >= lower && average <= upper,
                Value = FormatValue(average, unit), NumericValue = average, Unit = unit, StoreKey = storeKey };
        }

        private async Task<StepResult> MeasureStringTestMosOffCurrentAsync(SK441Device deviceManager,
            string name, string storeKey, double lower, double upper, string unit, CancellationToken token)
        {
            int count = deviceManager.Bcm125MosOffSampleCount;
            int intervalMs = deviceManager.Bcm125MosOffIntervalMs;
            if (count < 2 || count > 100 || intervalMs < 100 || intervalMs > 1000 || intervalMs % 100 != 0)
                throw new InvalidOperationException("MOS-off window requires 2..100 readings and a 100..1000ms interval divisible by 100.");
            token.ThrowIfCancellationRequested();
            var values = new System.Collections.Generic.List<double>();
            var times = new System.Collections.Generic.List<double>();
            var elapsed = Stopwatch.StartNew();
            try
            {
                await ReadStringTestCurrentWindowAsync(deviceManager, count, intervalMs, elapsed,
                    values, times, token);
            }
            catch
            {
                OnLogWarning($"{name} R44窗口无效：已收{values.Count}/{count}点，不使用部分平均判PASS；" +
                    string.Join("; ", values.Select((v, i) => string.Format(CultureInfo.InvariantCulture,
                        "{0}:{1:0.###}/{2:0.###}", i + 1, times[i], v))));
                throw;
            }

            StepResult result = CreateStringTestMosOffResult(values.ToArray(), count, storeKey, lower, upper, unit);
            LogStringTestMosOffSamples(name, values.ToArray(), times.ToArray(), lower, upper);
            if (!result.IsPass)
                await DiagnoseStringTestMosOffFailureAsync(deviceManager, name, storeKey, token);
            token.ThrowIfCancellationRequested();
            return result;
        }

        private async Task DiagnoseStringTestMosOffFailureAsync(SK441Device deviceManager,
            string name, string storeKey, CancellationToken token)
        {
            var channel = storeKey.Contains(".ST1.")
                ? Bcm125ChannelProfiles.StringTest1 : Bcm125ChannelProfiles.StringTest2;
            OnLogInfo(name + " 关断NG追加一次诊断，保持原NG；诊断采样在R44判定窗口之后。");
            // Await the single diagnostic acquisition, including its abort/restore
            // on failure or cancellation, before allowing the next step to run.
            try
            {
                DaqVoltageSampleSeries external = await deviceManager.MeasureDaqChannelVoltageTimedSamplesAsync(
                    channel.ShuntChannel, 10, 100, 1.0, 1.0, token, allowStartupInterval: true);
                double[] currents = external.Voltages.Select(v => v / channel.ShuntOhms * 1000.0).ToArray();
                OnLogInfo($"{name} 关断外部分流诊断 DAQ{channel.ShuntChannel}：" +
                    $"平均={currents.Average():0.###}mA，最小={currents.Min():0.###}mA，最大={currents.Max():0.###}mA；" +
                    "仅诊断，不替代R44判定。");
                OnLogInfo($"{name} 外部分流原始诊断（序号:DAQ_t_ms/V_mV/I_mA）：" +
                    string.Join("; ", Enumerable.Range(0, currents.Length)
                        .Select(i => string.Format(CultureInfo.InvariantCulture, "{0}:{1:0.###}/{2:0.######}/{3:0.###}",
                            i + 1, external.RelativeTimesSeconds[i] * 1000, external.Voltages[i] * 1000, currents[i]))));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { OnLogWarning(name + " 外部分流诊断无效，原NG保持：" + ex.Message); }
            await LogStringTestMosOffRegistersAsync(deviceManager, name, "关断NG后",
                new[] { "R42", "R50", "R56", "R22", "R23", "R24" }, token);
        }

        private async Task LogStringTestMosOffRegistersAsync(SK441Device deviceManager, string name,
            string phase, string[] commands, CancellationToken token)
        {
            foreach (string command in commands)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    string response = await QueryStringTestFixedWindowCommandAsync(deviceManager, command, token);
                    token.ThrowIfCancellationRequested();
                    double value = MbdResponseParser.ParseScalar(command, response);
                    OnLogInfo($"{name} {phase}诊断 {command}={value:0.###}（只读，不修正R44）。");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { OnLogWarning($"{name} {phase}诊断{command}读取失败：{ex.Message}"); }
            }
        }

        private async Task<string> QueryStringTestFixedWindowCommandAsync(SK441Device deviceManager,
            string command, CancellationToken token, int timeoutMs = 2000)
        {
            token.ThrowIfCancellationRequested();
            if (!IsBcm125RemoteTerminal)
                throw new InvalidOperationException("Fixed-window query requires an already confirmed REMOTE terminal.");
            // Do not reconnect/replay a failed query at a different sampling time.
            string response = await deviceManager.QueryMbdCommandOnceOnCurrentConnectionAsync(command, timeoutMs, token);
            token.ThrowIfCancellationRequested();
            return RequireValidBcm125QueryResponse(command, response);
        }

        private void LogStringTestMosOffSamples(string name, double[] values, double[] times, double lower, double upper)
        {
            double mean = values.Average();
            if (mean >= lower && mean <= upper)
            {
                OnLogInfo($"{name} R44窗口：{values.Length}点，跨度={times.Last() - times.First():0.###}ms，" +
                    $"平均={mean:0.###}mA，最小={values.Min():0.###}mA，最大={values.Max():0.###}mA，" +
                    $"限值=[{lower:0.###},{upper:0.###}]mA。");
                return;
            }
            int segment = Math.Min(5, values.Length);
            OnLogInfo($"{name} R44固定窗口统计：全样本={values.Length}，平均={mean:0.###}mA，" +
                $"最小={values.Min():0.###}mA，最大={values.Max():0.###}mA，" +
                $"标准差={Math.Sqrt(values.Average(v => (v - mean) * (v - mean))):0.###}mA，" +
                $"首{segment}点平均={values.Take(segment).Average():0.###}mA，末{segment}点平均={values.Skip(values.Length - segment).Average():0.###}mA，" +
                $"原限值=[{lower:0.###},{upper:0.###}]mA；不扣零点、不剔除读数。");
            for (int start = 0; start < values.Length; start += 20)
                OnLogInfo($"{name} R44原始全样本（序号:请求_t_ms/I_mA）：" +
                    string.Join("; ", Enumerable.Range(start, Math.Min(20, values.Length - start))
                        .Select(i => string.Format(CultureInfo.InvariantCulture, "{0}:{1:0.###}/{2:0.###}", i + 1, times[i], values[i]))));
        }

        private static StepResult CreateStringTestMosOffResult(double[] values, int expectedCount,
            string storeKey, double lower, double upper, string unit)
        {
            if (expectedCount < 2 || values == null || values.Length != expectedCount ||
                values.Any(v => double.IsNaN(v) || double.IsInfinity(v)))
                throw new InvalidOperationException("MOS-off current requires the complete finite R44 sample window.");
            double average = values.Average();
            return new StepResult { IsPass = average >= lower && average <= upper,
                Value = FormatValue(average, unit), NumericValue = average, Unit = unit, StoreKey = storeKey };
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
                    bool validInputs = hasVmidHigh && hasVmidLow && hasAdcHigh && hasAdcLow &&
                        Math.Abs(adcHigh - adcLow) > 0.001;
                    double gain = validInputs ? Math.Round(4096.0 * (vmidHigh - vmidLow) / (adcHigh - adcLow)) : 0;
                    double plannedLower, plannedUpper;
                    GetPlanRange(steps, rowIndex, 140000, 145000, out plannedLower, out plannedUpper);
                    string plannedUnit = GetPlanUnit(steps, rowIndex, "count");
                    bool pass = validInputs &&
                        AllStoredValuesPassed(context, vmidHighKey, vmidLowKey, adcHighKey, adcLowKey) &&
                        gain >= plannedLower && gain <= plannedUpper;
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
                    bool hasGain = TryGetStoredNumericValue(context, gainKey, out gain) &&
                        AllStoredValuesPassed(context, gainKey);
                    bool validInputs = hasVmidLow && hasAdcLow && hasGain;
                    double offset = validInputs ? Math.Round(vmidLow - (gain * adcLow) / 4096.0) : 0;
                    double plannedLower, plannedUpper;
                    GetPlanRange(steps, rowIndex, -600, 0, out plannedLower, out plannedUpper);
                    string plannedUnit = GetPlanUnit(steps, rowIndex, "mV");
                    bool pass = validInputs && AllStoredValuesPassed(context, vmidLowKey, adcLowKey) &&
                        offset >= plannedLower && offset <= plannedUpper;
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
                    OnLogInfo($"BCM-125 DAQ{channel}分流回读：V={averageVolts:0.########}V，" +
                        $"R={shuntOhm:0.########}Ω，I={currentMa:0.###}mA。");
                    double plannedLower, plannedUpper;
                    GetPlanRange(steps, rowIndex, lower, upper, out plannedLower, out plannedUpper);
                    string plannedUnit = GetPlanUnit(steps, rowIndex, "mA");
                    bool pass = currentMa >= plannedLower && currentMa <= plannedUpper;
                    if (!pass && !deviceManager.SkipComInit && deviceManager.HengHuiPower2.IsConnected)
                    {
                        try
                        {
                            float sourceVoltage;
                            float sourceCurrent;
                            deviceManager.HengHuiPower2.ReadVoltageCurrent(out sourceVoltage, out sourceCurrent);
                            double inferredOhms = Math.Abs(sourceCurrent) > 0.1f
                                ? Math.Abs(averageVolts / sourceCurrent)
                                : double.NaN;
                            OnLogInfo($"BCM-125 分流失配诊断：String电源回读=" +
                                $"{sourceVoltage:0.###}V/{sourceCurrent:0.###}A，" +
                                $"由电源电流推算分流值={inferredOhms:0.########}Ω；请用独立表确认后再修改站位分流系数。");
                        }
                        catch (Exception ex)
                        {
                            OnLogInfo("BCM-125 分流失配诊断：String电源回读失败：" + ex.Message);
                        }
                    }
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
                    bool ok = TryGetAvailableCalibrationNumericValue(context, current1Key, out current1)
                        && TryGetAvailableCalibrationNumericValue(context, current6Key, out current6)
                        && TryGetAvailableCalibrationNumericValue(context, adc1Key, out adc1)
                        && TryGetAvailableCalibrationNumericValue(context, adc6Key, out adc6)
                        && Math.Abs(adc6 - adc1) > 0.001;
                    double gain = ok ? Math.Round((current6 - current1) * 4096.0 / (adc6 - adc1)) : 0;
                    double offset = ok ? Math.Round(current1 - (gain * adc1) / 4096.0) : 0;
                    double plannedLower, plannedUpper;
                    GetPlanRange(steps, rowIndex, 87500, 92500, out plannedLower, out plannedUpper);
                    string plannedUnit = GetPlanUnit(steps, rowIndex, "count");
                    bool pass = ok && gain >= plannedLower && gain <= plannedUpper;
                    return new StepResult { IsPass = pass, Value = ok ? $"G={gain:0};O={offset:0}" : "Missing Input", StoreKey = gainKey, NumericValue = ok ? (double?)gain : null, Unit = plannedUnit };
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
                    bool ok = TryGetAvailableCalibrationNumericValue(context, current1Key, out current1)
                        && TryGetAvailableCalibrationNumericValue(context, current6Key, out current6)
                        && TryGetAvailableCalibrationNumericValue(context, adc1Key, out adc1)
                        && TryGetAvailableCalibrationNumericValue(context, adc6Key, out adc6)
                        && Math.Abs(adc6 - adc1) > 0.001;
                    double gain = ok ? Math.Round((current6 - current1) * 4096.0 / (adc6 - adc1)) : 0;
                    double offset = ok ? Math.Round(current1 - (gain * adc1) / 4096.0) : 0;
                    double plannedLower, plannedUpper;
                    GetPlanRange(steps, rowIndex + 1, -80, 45, out plannedLower, out plannedUpper);
                    string plannedUnit = GetPlanUnit(steps, rowIndex + 1, "mA");
                    bool pass = ok && offset >= plannedLower && offset <= plannedUpper;
                    return new StepResult { IsPass = pass, Value = ok ? offset.ToString("0", System.Globalization.CultureInfo.InvariantCulture) : "Missing Input", StoreKey = offsetKey, NumericValue = ok ? (double?)offset : null, Unit = plannedUnit };
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
                    bool hasValue = context != null
                        && context.TryGetValue(storeKey, out storedValue)
                        && storedValue.HasNumericValue
                        && !double.IsNaN(storedValue.NumericValue)
                        && !double.IsInfinity(storedValue.NumericValue);
                    double value = hasValue ? storedValue.NumericValue : 0.0;
                    if (hasValue && !storedValue.Pass)
                        OnLogWarning(name + "：源结果超限，但有有效数值，继续写入；原测试结果仍为NG。");

                    bool pass = hasValue && await SendBcm125MbdCommandAsync(
                        deviceManager,
                        commandPrefix + value.ToString("0", System.Globalization.CultureInfo.InvariantCulture),
                        0);
                    return new StepResult
                    {
                        IsPass = pass,
                        Value = pass
                            ? value.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                            : (hasValue ? "Write Fail" : "Source Missing"),
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
            AddFixtureRelayStep(steps, row++, deviceManager, "18.2 Open power relays RL1/RL2/RL9", "SKBCM125PrechargeOpenRelays", Bcm125StationMap.AllSwitchingPowerRelays, false, "Relays Open");
            AddDaqRelayStep(steps, row++, deviceManager, "18.3 Open RL5.12/RL10.12", "SKBCM125Rl5Rl10DaqRelays", Bcm125StationMap.StringSimulatorDaqRelays, false, "RL5/RL10 Open");
            AddDelayStep(steps, row++, "18.4 Wait 50ms", 50);
            AddProgrammingActionStep(steps, row++, "18.5 Reset VBUS generator 0V/0A OVP150", token => deviceManager.SetVbusGeneratorAsync(0, 0, 150, false), "VBUS Reset", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "18.6 Check serial communication", token => CheckBcm125MbdCommunicationAsync(deviceManager), "Open", "COM Fail");
            AddMbdCommandPairStep(
                steps,
                row++,
                deviceManager,
                "18.7 Send EXIT and DIAG ON to MBD",
                "EXIT",
                "DIAG ON",
                0);
            AddMbdNumericQueryStep(steps, row++, deviceManager, "18.8 Observe R68 C21 discharge state", "R68", "BCM125.PRECHARGE.C21_DISCHARGED", 0, 49, "count", advisoryOutsideRange: true);
            AddMbdCommandStep(steps, row++, deviceManager, "18.9 Send REMOTE1", "REMOTE1", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "18.10 ST1 send DIAG ON", "DIAG ON", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "18.11 ST1 deactivate precharge W75=0", "W75=0", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "18.12 Send EXIT to MBD", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "18.13 Send REMOTE2", "REMOTE2", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "18.14 ST2 send DIAG ON", "DIAG ON", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "18.15 ST2 deactivate precharge W75=0", "W75=0", 0);
            AddFixtureRelayStep(steps, row++, deviceManager, "18.16 Close RL1a_B/RL2B", "SKBCM125PrechargeCloseRelays", Bcm125StationMap.PrechargePowerRelays, true, "Relays Closed");
            AddDelayStep(steps, row++, "18.17 Wait 500ms", 500);
            AddProgrammingActionStep(steps, row++, "18.18 Set VBUS generator 140V/1A OVP150", token => deviceManager.SetVbusGeneratorAsync(140, 1, 150, false), "VBUS Set", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "18.19 Turn on VBUS generator", token => deviceManager.SetVbusGeneratorAsync(140, 1, 150, true), "VBUS ON", "VBUS Fail");
            AddMbdCommandStep(steps, row++, deviceManager, "18.20 Send EXIT to MBD", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "18.21 Send DIAG ON to MBD", "DIAG ON", 0);
            AddMeasuredReadinessDelayStep(steps, row++, deviceManager, "18.22 Wait 15000ms", 15000, 2500,
                new Bcm125ReadinessProbe { Command = "R69", ResultStepId = "BCM125.CH18.S023.L01" },
                new Bcm125ReadinessProbe { Command = "R68", ResultStepId = "BCM125.CH18.S024.L02" });
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
            AddMeasuredReadinessDelayStep(steps, row++, deviceManager, "18.37 Wait 15000ms", 15000, 2500,
                new Bcm125ReadinessProbe { Command = "R68", ResultStepId = "BCM125.CH18.S043.R68" });
            AddMbdCommandStep(steps, row++, deviceManager, "18.38 Send REMOTE1", "REMOTE1", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "18.38 ST1 deactivate precharge W75=0", "W75=0", 0);

            AddPrechargeRepeatSteps(steps, deviceManager, ref row, "ST2", "REMOTE2", "L05", "L06", "L07", "L08");

            AddFixtureRelayStep(steps, row++, deviceManager, "18.40 Open power relays RL1/RL2/RL9", "SKBCM125PrechargeOpenRelays", Bcm125StationMap.AllSwitchingPowerRelays, false, "Relays Open");
            AddDelayStep(steps, row++, "18.41 Wait 200ms", 200);
            AddProgrammingActionStep(steps, row++, "18.43 Close COM with instruments", token => Task.FromResult(true), "Close OK", "Close Fail");
        }

        private void AddPrechargeRepeatSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager, ref int row, string suffix, string remoteCommand, string l01, string l02, string l03, string l04)
        {
            AddProgrammingActionStep(steps, row++, "18.39 " + suffix + " reset VBUS generator 0V/0A OVP150", token => deviceManager.SetVbusGeneratorAsync(0, 0, 150, false), "VBUS Reset", "VBUS Fail");
            AddMbdCommandPairStep(
                steps,
                row++,
                deviceManager,
                "18.39 " + suffix + " send EXIT and DIAG ON to MBD",
                "EXIT",
                "DIAG ON",
                0);
            AddMbdNumericQueryStep(steps, row++, deviceManager, "18.39 " + suffix + " read R68 verify C21 discharged", "R68", "BCM125.PRECHARGE.C21_DISCHARGED_" + suffix, 0, 49, "count");
            AddMbdCommandStep(steps, row++, deviceManager, "18.39 " + suffix + " send " + remoteCommand, remoteCommand, 0);
            AddMbdCommandStep(steps, row++, deviceManager, "18.39 " + suffix + " send DIAG ON", "DIAG ON", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "18.39 " + suffix + " deactivate precharge W75=0", "W75=0", 0);
            AddDelayStep(steps, row++, "18.39 " + suffix + " wait 500ms", 500);
            AddProgrammingActionStep(steps, row++, "18.39 " + suffix + " set VBUS generator 140V/1A OVP150", token => deviceManager.SetVbusGeneratorAsync(140, 1, 150, true), "VBUS ON", "VBUS Fail");
            AddMeasuredReadinessDelayStep(steps, row++, deviceManager, "18.39 " + suffix + " wait 15000ms", 15000, 2500,
                new Bcm125ReadinessProbe { Command = "R69", ResultStepId = "BCM125.CH18.S050.L05" },
                new Bcm125ReadinessProbe { Command = "R68", ResultStepId = "BCM125.CH18.S051.L06" });
            AddMbdNumericQueryStep(steps, row++, deviceManager, l01 + " " + suffix + " read R69 mcu_vbus", "R69", "BCM125.PRECHARGE." + l01 + "_ADC3_MCU_VBUS_" + suffix, 2600, 2855, "count");
            AddMbdNumericQueryStep(steps, row++, deviceManager, l02 + " " + suffix + " read R68 vs_prec", "R68", "BCM125.PRECHARGE." + l02 + "_ADC14_VS_PREC_" + suffix, 2700, 2840, "count");
            AddProgrammingActionStep(
                steps,
                row++,
                "18.39 " + suffix + " activate precharge W75=1",
                async token =>
                {
                    if (!await SendBcm125MbdCommandAsync(deviceManager, "EXIT", 0))
                        return false;
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
            AddFixtureRelayStep(steps, row++, deviceManager, "19.2 Open RL1/RL2/RL9 relays", "SKBCM125MidpointOpenRelays", Bcm125StationMap.AllSwitchingPowerRelays, false, "Relays Open");
            AddDaqRelayStep(steps, row++, deviceManager, "19.3 Open RL5.12/RL10.12", "SKBCM125Rl5Rl10DaqRelays", Bcm125StationMap.StringSimulatorDaqRelays, false, "RL5/RL10 Open");
            AddDelayStep(steps, row++, "19.4 Wait 50ms", 50);
            AddProgrammingActionStep(steps, row++, "19.5 Reset VBUS generator 0V/0A OVP150", token => deviceManager.SetVbusGeneratorAsync(0, 0, 150, false), "VBUS Reset", "VBUS Fail");
            AddDelayStep(steps, row++, "19.6 Wait 20ms", 20);
            AddDaqRelayStep(steps, row++, deviceManager, "19.7 Close RL6 midpoint path DAQ301", "SKBCM125Rl6DaqRelay", Bcm125StationMap.MidpointDaqRelay, true, "RL6 Closed");

            AddMidpointStringSteps(steps, context, deviceManager, ref row, "ST1", "REMOTE1", "W94=1", "W94=0", "M01", "M02", "M03", "M04", "M05", "M06", "M07", "M08", "M09");
            AddMidpointStringSteps(steps, context, deviceManager, ref row, "ST2", "REMOTE2", "W91=1", "W91=0", "M10", "M11", "M12", "M13", "M14", "M15", "M16", "M17", "M18");

            AddProgrammingActionStep(steps, row++, "19.41 Turn off string generator", token => deviceManager.SetVbusGeneratorAsync(0, 0, 10, false), "Generator OFF", "Generator Fail");
            AddProgrammingActionStep(steps, row++, "19.43 Make sure generators are off", token => deviceManager.SetVbusGeneratorAsync(0, 0, 300, false), "Generators OFF", "Generator Fail");
            AddProgrammingActionStep(steps, row++, "19.45 Reset electronic load", token => deviceManager.ResetElectronicLoadAsync(), "Load Reset", "Load Fail");
            AddDelayStep(steps, row++, "19.46 Wait 1000ms", 1000);
            AddDaqRelayStep(steps, row++, deviceManager, "19.47 Open RL6 midpoint path DAQ301", "SKBCM125Rl6DaqRelay", Bcm125StationMap.MidpointDaqRelay, false, "RL6 Open");
            AddDelayStep(steps, row++, "19.48 Wait 200ms", 200);
            AddProgrammingActionStep(steps, row++, "19.50 Close COM with instruments", token => Task.FromResult(true), "Close OK", "Close Fail");
        }

        private void AddMidpointStringSteps(System.Collections.Generic.List<TestStepItem> steps, TestSequenceContext context, SK441Device deviceManager, ref int row, string suffix, string remoteCommand, string relayOnCommand, string relayOffCommand, string m01, string m02, string m03, string m04, string m05, string m06, string m07, string m08, string m09)
        {
            string prefix = "BCM125.MIDPOINT." + suffix + ".";
            AddProgrammingActionStep(steps, row++, "19.8 " + suffix + " set VMID 62.5V/0.1A OVP300", token => deviceManager.SetVbusGeneratorAsync(62.5f, 0.1f, 300, true), "VMID Set", "VMID Fail");
            AddMeasuredReadinessDelayStep(steps, row++, deviceManager, "19.8 " + suffix + " wait 10000ms", 10000, 2000,
                new Bcm125ReadinessProbe { DaqChannel = Bcm125StationMap.VmidReferenceDaqChannel,
                    ResultStepId = suffix == "ST1" ? "BCM125.CH19.S021.M02" : "BCM125.CH19.S053.M11" });
            AddProgrammingActionStep(steps, row++, "19.9 " + suffix + " check serial communication", token => CheckBcm125MbdCommunicationAsync(deviceManager), "Open", "COM Fail");
            AddMbdCommandStep(steps, row++, deviceManager, "19.10 " + suffix + " send EXIT", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "19.11 " + suffix + " send DIAG ON to MBD", "DIAG ON", 0);
            AddDelayStep(steps, row++, "19.12 " + suffix + " wait 500ms", 500);
            AddMbdCommandStep(steps, row++, deviceManager, "19.13 " + suffix + " activate volt_in " + relayOnCommand, relayOnCommand, 0);
            AddDelayStep(steps, row++, "19.14 " + suffix + " wait 500ms for volt_in relay", 500);
            AddMbdCommandStep(steps, row++, deviceManager, "19.15 " + suffix + " send " + remoteCommand, remoteCommand, 0);
            AddMbdCommandStep(steps, row++, deviceManager, "19.16 " + suffix + " send DIAG ON to BCM", "DIAG ON", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "19.17 " + suffix + " enable mid_in W76=1", "W76=1", 0);
            AddMeasuredReadinessDelayStep(steps, row++, deviceManager,
                "19.18 " + suffix + " wait for VMID readiness", 18000, 2000,
                new Bcm125ReadinessProbe
                {
                    Command = "R92",
                    ResultStepId = suffix == "ST1" ? "BCM125.CH19.S033.M09" : "BCM125.CH19.S065.M18"
                },
                new Bcm125ReadinessProbe
                {
                    Command = "R93",
                    ResultStepId = suffix == "ST1" ? "BCM125.CH19.S020.M01" : "BCM125.CH19.S052.M10"
                },
                new Bcm125ReadinessProbe
                {
                    DaqChannel = Bcm125StationMap.VmidReferenceDaqChannel,
                    ResultStepId = suffix == "ST1" ? "BCM125.CH19.S021.M02" : "BCM125.CH19.S053.M11"
                });
            AddMbdAverageQueryStep(steps, row++, deviceManager, m01 + " " + suffix + " average R93 at 62.5V", "R93", prefix + m01 + "_ADC2_VMID_62V5", 1750, 1850, "count", 8, 0);
            AddDaqAverageMvStep(steps, row++, deviceManager, m02 + " " + suffix + " average DAQ CH06 VMID 62.5V", Bcm125StationMap.VmidReferenceDaqChannel, prefix + m02 + "_VMID_62V5_MV", 62000, 63000, 8, 0);
            AddProgrammingActionStep(steps, row++, "19.21 " + suffix + " set VMID 5V/0.1A OVP300", token => deviceManager.SetVbusGeneratorAsync(5, 0.1f, 300, true), "VMID Set", "VMID Fail");
            AddMeasuredReadinessDelayStep(steps, row++, deviceManager, "19.22 " + suffix + " wait 12000ms", 12000, 2000,
                new Bcm125ReadinessProbe { Command = "R93", ResultStepId =
                    suffix == "ST1" ? "BCM125.CH19.S024.M03" : "BCM125.CH19.S056.M12" },
                new Bcm125ReadinessProbe { DaqChannel = Bcm125StationMap.VmidReferenceDaqChannel,
                    ResultStepId = suffix == "ST1" ? "BCM125.CH19.S025.M04" : "BCM125.CH19.S057.M13" });
            AddMbdAverageQueryStep(steps, row++, deviceManager, m03 + " " + suffix + " average R93 at 5V", "R93", prefix + m03 + "_ADC2_VMID_5V", 150, 170, "count", 8, 0);
            AddDaqAverageMvStep(steps, row++, deviceManager, m04 + " " + suffix + " average DAQ CH06 VMID 5V", Bcm125StationMap.VmidReferenceDaqChannel, prefix + m04 + "_VMID_5V_MV", 4750, 5250, 8, 0);
            AddGainVmidWriteStep(steps, row++, context, deviceManager, m05 + " " + suffix + " calculate Gain_VMID and write W20", prefix + m02 + "_VMID_62V5_MV", prefix + m04 + "_VMID_5V_MV", prefix + m01 + "_ADC2_VMID_62V5", prefix + m03 + "_ADC2_VMID_5V", prefix + m05 + "_GAIN_VMID");
            AddOffsetVmidWriteStep(steps, row++, context, deviceManager, m06 + " " + suffix + " calculate offset and write W21", prefix + m04 + "_VMID_5V_MV", prefix + m03 + "_ADC2_VMID_5V", prefix + m05 + "_GAIN_VMID", prefix + m06 + "_OFFSET_VMID");
            AddConditionalFlashStep(steps, row++, context, deviceManager,
                "19.27 " + suffix + " store ACT->FLASH",
                prefix + m05 + "_GAIN_VMID", prefix + m06 + "_OFFSET_VMID");
            AddMbdNumericCompareStoredStep(steps, row++, context, deviceManager, m07 + " " + suffix + " verify R20 Gain_VMID", "R20", prefix + m05 + "_GAIN_VMID", prefix + m07 + "_R20_GAIN", "count");
            AddMbdNumericCompareStoredStep(steps, row++, context, deviceManager, m08 + " " + suffix + " verify R21 offset", "R21", prefix + m06 + "_OFFSET_VMID", prefix + m08 + "_R21_OFFSET", "mV");
            AddProgrammingActionStep(steps, row++, "19.30 " + suffix + " set VMID 62.5V/0.1A OVP300", token => deviceManager.SetVbusGeneratorAsync(62.5f, 0.1f, 300, true), "VMID Set", "VMID Fail");
            AddMeasuredReadinessDelayStep(steps, row++, deviceManager, "19.31 " + suffix + " wait 18000ms", 18000, 2000,
                new Bcm125ReadinessProbe { Command = "R92", ResultStepId =
                    suffix == "ST1" ? "BCM125.CH19.S033.M09" : "BCM125.CH19.S065.M18" });
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

            AddProgrammingActionStep(steps, row++, "20.1 Close RL4 to power DUT", async token =>
            {
                if (!await deviceManager.CloseRl4ThenEnableAnsAsync(7, 140, 1))
                    return false;
                return await deviceManager.WaitForPowerVoltageStableAsync("ANS", 140, 500, 5000, token);
            }, "RL4/ANS ON", "DUT Power Fail");
            AddStringPowerPathRelayStep(steps, row++, deviceManager, "20.2 Open String power path Y1/Y4/Y6 and discharge relays", "SKBCM125DischargeOpenRelays", Bcm125StationMap.AllStringPowerPathRelays, false, "Relays Open");
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
            AddStringPowerPathRelayStep(steps, row++, deviceManager, "20.6 " + suffix + " close String power path Y1/Y4/Y6 and discharge relays", relayConfigKey, relayDefaults, true, "Relays Closed");
            AddDelayStep(steps, row++, "20.7 " + suffix + " wait 20ms", 20);
            AddProgrammingActionStep(steps, row++, "20.8 " + suffix + " set string generator 12V/1A OVP16", token => deviceManager.SetStringGeneratorAsync(12, 1, 16, false), "String Set", "String Fail");
            AddProgrammingActionStep(steps, row++, "20.9 " + suffix + " turn on string generator and wait for stable 1A", async token =>
            {
                if (!await deviceManager.SetStringGeneratorAsync(12, 1, 16, true))
                    return false;
                return await deviceManager.WaitForPowerCurrentStableAsync("SECONDARY", 1, 300, 2500, token);
            }, "String 1A Stable", "String 1A Fail");
            AddProgrammingActionStep(steps, row++, "20.10 " + suffix + " check serial communication", token => CheckBcm125MbdCommunicationAsync(deviceManager, true), "Open", "COM Fail");
            AddMbdCommandPairStep(
                steps,
                row++,
                deviceManager,
                "20.11 " + suffix + " send EXIT then " + remoteCommand,
                "EXIT",
                remoteCommand,
                0);
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
            AddMbdCommandPairStep(
                steps,
                row++,
                deviceManager,
                "20.27 " + suffix + " send EXIT then " + remoteCommand,
                "EXIT",
                remoteCommand,
                0);
            AddWriteStoredNumericStep(steps, row++, context, deviceManager, "20.28 " + suffix + " write W26 gain_idch", "W26=", prefix + n05 + "_GAIN_IDCH");
            AddWriteStoredNumericStep(steps, row++, context, deviceManager, "20.29 " + suffix + " write W27 offset_idch", "W27=", prefix + n06 + "_OFFSET_IDCH");
            AddConditionalFlashStep(steps, row++, context, deviceManager,
                "20.30 " + suffix + " store ACT->FLASH",
                prefix + n05 + "_GAIN_IDCH.WRITE_OK",
                prefix + n06 + "_OFFSET_IDCH.WRITE_OK");
            AddDelayStep(steps, row++, "20.31 " + suffix + " wait 3000ms", 3000);
            AddMbdAverageQueryStep(steps, row++, deviceManager, n07 + " " + suffix + " average R45 at 0A", "R45", prefix + n07 + "_IDCH_0A", -35, 35, "mA", 50, 50);
            AddProgrammingActionStep(steps, row++, "20.36 " + suffix + " set string generator 12V/6A OVP16", token => deviceManager.SetStringGeneratorAsync(12, 6, 16, true), "String ON", "String Fail");
            AddDelayStep(steps, row++, "20.38 " + suffix + " wait 2000ms", 2000);
            AddMbdAverageQueryStep(steps, row++, deviceManager, n08 + " " + suffix + " average R45 at 6A", "R45", prefix + n08 + "_IDCH_6A", 5950, 6050, "mA", 50, 50);
            AddProgrammingActionStep(steps, row++, "20.42 " + suffix + " turn off string generator", token => deviceManager.SetStringGeneratorAsync(12, 6, 16, false), "String OFF", "String Fail");
            AddProgrammingActionStep(steps, row++, "20.45 " + suffix + " reset generators", token => deviceManager.SetStringGeneratorAsync(0, 0, 150, false), "Generators Reset", "Generator Fail");
            AddProgrammingActionStep(steps, row++, "20.46 " + suffix + " reset electronic load", token => deviceManager.ResetElectronicLoadAsync(), "Load Reset", "Load Fail");
            AddDelayStep(steps, row++, "20.47 " + suffix + " wait 1000ms", 1000);
            AddStringPowerPathRelayStep(steps, row++, deviceManager, "20.48 " + suffix + " open String power path Y1/Y4/Y6 and discharge relays", "SKBCM125DischargeOpenRelays", Bcm125StationMap.AllStringPowerPathRelays, false, "Relays Open");
            AddDelayStep(steps, row++, "20.49 " + suffix + " wait 200ms", 200);
        }

        protected void AddBcm125DischargeMosfetSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager)
        {
            int row = steps.Count;

            AddProgrammingActionStep(steps, row++, "21.1 Close RL4 to power DUT", token => deviceManager.ControlRelayAsync(7, true), "RL4 ON", "RL4 Fail");
            AddStringPowerPathRelayStep(steps, row++, deviceManager, "21.2 Open String power path Y1/Y4/Y6 and RL1/RL2 relays", "SKBCM125DischargeMosfetOpenRelays", Bcm125StationMap.AllStringPowerPathRelays, false, "Relays Open");
            AddDaqRelayStep(steps, row++, deviceManager, "21.3 Open RL5.12/RL10.12", "SKBCM125Rl5Rl10DaqRelays", Bcm125StationMap.StringSimulatorDaqRelays, false, "RL5/RL10 Open");
            AddDelayStep(steps, row++, "21.4 Wait 50ms", 50);
            AddProgrammingActionStep(steps, row++, "21.5 Reset VBUS generator", token => deviceManager.SetVbusGeneratorAsync(0, 0, 150, false), "VBUS Reset", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "21.5 Reset string generator", token => deviceManager.SetStringGeneratorAsync(0, 0, 150, false), "String Reset", "String Fail");
            AddProgrammingActionStep(steps, row++, "21.6 Reset electronic load", token => deviceManager.ResetElectronicLoadAsync(), "Load Reset", "Load Fail");
            AddDischargeMosfetStringSteps(steps, deviceManager, ref row, "ST1", "REMOTE1", "O01", "SKBCM125DischargeSt1CloseRelays", Bcm125StationMap.DischargeString1PowerRelays);
            AddDischargeMosfetStringSteps(steps, deviceManager, ref row, "ST2", "REMOTE2", "O02", "SKBCM125DischargeSt2CloseRelays", Bcm125StationMap.DischargeString2PowerRelays);
            AddMbdCommandStep(steps, row++, deviceManager, "21.24 Send EXIT to MBD terminal", "EXIT", 0);
            AddProgrammingActionStep(steps, row++, "21.25 Close COM with instruments", token => Task.FromResult(true), "Close OK", "Close Fail");
        }

        private void AddDischargeMosfetStringSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager, ref int row, string suffix, string remoteCommand, string stepName, string relayConfigKey, string relayDefaults)
        {
            AddStringPowerPathRelayStep(steps, row++, deviceManager, "21.7 " + suffix + " close String power path Y1/Y4/Y6 and discharge relays", relayConfigKey, relayDefaults, true, "Relays Closed");
            AddDelayStep(steps, row++, "21.8 " + suffix + " wait 20ms", 20);
            AddProgrammingActionStep(steps, row++, "21.9 " + suffix + " set string generator 5V/0.3A OVP10", token => deviceManager.SetStringGeneratorAsync(5, 0.3f, 10, false), "String Set", "String Fail");
            AddProgrammingActionStep(steps, row++, "21.10 " + suffix + " turn on string generator", token => deviceManager.SetStringGeneratorAsync(5, 0.3f, 10, true), "String ON", "String Fail");
            AddProgrammingActionStep(steps, row++, "21.11 " + suffix + " check serial communication", token => CheckBcm125MbdCommunicationAsync(deviceManager), "Open", "COM Fail");
            AddMbdCommandPairStep(
                steps,
                row++,
                deviceManager,
                "21.12 " + suffix + " send EXIT then " + remoteCommand,
                "EXIT",
                remoteCommand,
                0);
            AddDischargeMosfetRampStep(steps, row++, deviceManager, stepName + " " + suffix + " ramp R45/R59 trigger", "BCM125.DISCHARGE_MOSFET." + stepName + "_" + suffix);
            AddProgrammingActionStep(steps, row++, "21.14 " + suffix + " turn off string generator", token => deviceManager.SetStringGeneratorAsync(5, 0.3f, 10, false), "String OFF", "String Fail");
            AddProgrammingActionStep(steps, row++, "21.15 " + suffix + " reset string generator 0V/0A OVP10", token => deviceManager.SetStringGeneratorAsync(0, 0, 10, false), "String Reset", "String Fail");
            AddProgrammingActionStep(steps, row++, "21.17 " + suffix + " reset generators 0V/0A OVP300", token => deviceManager.SetStringGeneratorAsync(0, 0, 300, false), "Generators Reset", "Generator Fail");
            AddProgrammingActionStep(steps, row++, "21.18 " + suffix + " reset electronic load", token => deviceManager.ResetElectronicLoadAsync(), "Load Reset", "Load Fail");
            AddDelayStep(steps, row++, "21.19 " + suffix + " wait 1000ms", 1000);
            AddStringPowerPathRelayStep(steps, row++, deviceManager, "21.20 " + suffix + " open String power path Y1/Y4/Y6 and discharge relays", "SKBCM125DischargeOpenRelays", Bcm125StationMap.AllStringPowerPathRelays, false, "Relays Open");
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
            AddStringPowerPathRelayStep(steps, row++, deviceManager, "22.2 Open String power path Y1/Y4/Y6 and RL1/RL2/RL9 relays", "SKBCM125ShortProtectionOpenRelays", Bcm125StationMap.AllStringPowerPathRelays, false, "Relays Open");
            AddDaqRelayStep(steps, row++, deviceManager, "22.3 Open RL5.12/RL10.12", "SKBCM125Rl5Rl10DaqRelays", Bcm125StationMap.StringSimulatorDaqRelays, false, "RL5/RL10 Open");
            AddDelayStep(steps, row++, "22.4 Wait 50ms", 50);
            AddProgrammingActionStep(steps, row++, "22.5 Reset VBUS generator", token => deviceManager.SetVbusGeneratorAsync(0, 0, 300, false), "VBUS Reset", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "22.5 Reset string generator", token => deviceManager.SetStringGeneratorAsync(0, 0, 300, false), "String Reset", "String Fail");
            AddProgrammingActionStep(steps, row++, "22.6 Reset electronic load", token => deviceManager.ResetElectronicLoadAsync(), "Load Reset", "Load Fail");

            AddShortProtectionStringSteps(steps, deviceManager, ref row, "ST1", "REMOTE1", "U1.2", Bcm125StationMap.String1SimulatorDaqRelay, "SKBCM125ShortProtectionSt1CloseRelays", Bcm125StationMap.DischargeString1PowerRelays, "P01", "P02", "P03", "P04");
            AddShortProtectionStringSteps(steps, deviceManager, ref row, "ST2", "REMOTE2", "U33.2", Bcm125StationMap.String2SimulatorDaqRelay, "SKBCM125ShortProtectionSt2CloseRelays", Bcm125StationMap.DischargeString2PowerRelays, "P05", "P06", "P07", "P08");

            AddMbdCommandStep(steps, row++, deviceManager, "22.34 Send EXIT to MBD terminal", "EXIT", 0);
            AddProgrammingActionStep(steps, row++, "22.35 Close COM with instruments", token => Task.FromResult(true), "Close OK", "Close Fail");
        }

        private void AddShortProtectionStringSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager, ref int row, string suffix, string remoteCommand, string pointName, string daqRelay, string relayConfigKey, string relayDefaults, string p01, string p02, string p03, string p04)
        {
            AddStringPowerPathRelayStep(steps, row++, deviceManager, "22.7 " + suffix + " close String power path Y1/Y4/Y6 and test relays", relayConfigKey, relayDefaults, true, "Relays Closed");
            AddDaqRelayStep(steps, row++, deviceManager, "22.7 " + suffix + " close " + pointName + " simulator relay", "SKBCM125ShortProtection" + suffix + "DaqRelay", daqRelay, true, "Relay Closed");
            AddDelayStep(steps, row++, "22.8 " + suffix + " wait 20ms", 20);
            AddProgrammingActionStep(steps, row++, "22.9 " + suffix + " set string generator 137.5V/1A OVP10", token => deviceManager.SetStringGeneratorAsync(137.5f, 1, 10, false), "String Set", "String Fail");
            AddProgrammingActionStep(steps, row++, "22.10 " + suffix + " turn on string generator", token => deviceManager.SetStringGeneratorAsync(137.5f, 1, 10, true), "String ON", "String Fail");
            AddDelayStep(steps, row++, "22.11 " + suffix + " wait 1000ms", 1000);
            AddProgrammingActionStep(steps, row++, "22.12 " + suffix + " check serial communication", token => CheckBcm125MbdCommunicationAsync(deviceManager), "Open", "COM Fail");
            AddMbdCommandPairStep(
                steps,
                row++,
                deviceManager,
                "22.13 " + suffix + " send EXIT then " + remoteCommand,
                "EXIT",
                remoteCommand,
                0);
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
            AddStringPowerPathRelayStep(steps, row++, deviceManager, "22.29 " + suffix + " open String power path Y1/Y4/Y6 and test relays", "SKBCM125ShortProtectionOpenRelays", Bcm125StationMap.AllStringPowerPathRelays, false, "Relays Open");
            AddDelayStep(steps, row++, "22.30 " + suffix + " wait 200ms", 200);
            AddDaqRelayStep(steps, row++, deviceManager, "22.31 " + suffix + " open " + pointName + " simulator relay", "SKBCM125ShortProtection" + suffix + "DaqRelay", daqRelay, false, "Relay Open");
        }

        protected void AddBcm125ChargingCurrentCalibrationSteps(System.Collections.Generic.List<TestStepItem> steps, TestSequenceContext context, SK441Device deviceManager)
        {
            int row = steps.Count;

            AddProgrammingActionStep(steps, row++, "23.1 Close RL4 to power DUT", token => deviceManager.ControlRelayAsync(7, true), "RL4 ON", "RL4 Fail");
            AddStringPowerPathRelayStep(steps, row++, deviceManager, "23.2 Open String power path Y1/Y4/Y6 and RL1/RL2/RL9 relays", "SKBCM125ChargingOpenRelays", Bcm125StationMap.AllStringPowerPathRelays, false, "Relays Open");
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
            string prefix = "BCM125.CHARGING." + suffix + ".";
            AddStringPowerPathRelayStep(steps, row++, deviceManager, "23.7 " + suffix + " close String power path Y1/Y4/Y6 and charging relays", relayConfigKey, relayDefaults, true, "Relays Closed");
            AddDelayStep(steps, row++, "23.8 " + suffix + " wait 20ms", 20);
            AddProgrammingActionStep(steps, row++, "23.9 " + suffix + " set string generator 0V/0A OVP150", token => deviceManager.SetStringGeneratorAsync(0, 0, 150, false), "String Set", "String Fail");
            AddDelayStep(steps, row++, "23.10 " + suffix + " wait 1000ms", 1000);
            AddProgrammingActionStep(steps, row++, "23.11 " + suffix + " turn on string generator", token => deviceManager.SetStringGeneratorAsync(0, 0, 150, true), "String ON", "String Fail");
            AddProgrammingActionStep(steps, row++, "23.12 " + suffix + " set VSTR_NEG bus generator 1V/1A OVP150", token => deviceManager.SetVbusGeneratorAsync(1, 1, 150, true), "VBUS ON", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "23.13 " + suffix + " check serial communication", token => CheckBcm125MbdCommunicationAsync(deviceManager), "Open", "COM Fail");
            AddMbdCommandStep(steps, row++, deviceManager, "23.14 " + suffix + " send EXIT to MBD terminal", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "23.14 " + suffix + " send DIAG ON to MBD", "DIAG ON", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "23.15 " + suffix + " send " + remoteCommand, remoteCommand, 0);
            AddMbdCommandStep(steps, row++, deviceManager, "23.16 " + suffix + " send DIAG ON to BCM", "DIAG ON", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "23.17 " + suffix + " set W69=1 CCB off", "W69=1", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "23.18 " + suffix + " set W12=0mA", "W12=0", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "23.19 " + suffix + " set W75=1 precharge", "W75=1", 0);
            AddDelayStep(steps, row++, "23.20 " + suffix + " wait 500ms", 500);
            int zeroCurrentRow = row;
            AddMbdAverageQueryStep(steps, row++, deviceManager, q01 + " " + suffix + " average R50 adc0 @ 0V 0A", "R50", prefix + q01 + "_ADC0_0V_0A", 40, 50, "count", 4, 20);
            AddMbdAverageQueryStep(steps, row++, deviceManager, q02 + " " + suffix + " average R56 adc14 @ 0V 0A", "R56", prefix + q02 + "_ADC14_0V_0A", 1350, 1500, "count", 4, 20);
            AddDaqAverageMvLimitedStep(steps, row++, deviceManager, q03 + " " + suffix + " DAQ V" + (vMeasureChannel == 109 ? "2" : "6") + " 1000mV (0A)", vMeasureChannel, prefix + q03 + "_VSTR_NEG_1000MV_0A", 900, 1100);
            AddDaqSignedShuntCurrentStep(steps, row++, deviceManager, q04 + " " + suffix + " shunt current 0mA", shuntChannel, shuntOhm, prefix + q04 + "_CURRENT_0MA", -10, 10);
            AddChargingMeasurementBatch(steps, context, zeroCurrentRow, 2, 2);
            AddProgrammingActionStep(steps, row++, "23.26 " + suffix + " set VSTR_NEG bus generator 15V/1A OVP150", token => deviceManager.SetVbusGeneratorAsync(15, 1, 150, true), "VBUS Set", "VBUS Fail");
            AddDelayStep(steps, row++, "23.27 " + suffix + " wait 2000ms", 2000);
            int highVoltageRow = row;
            AddMbdAverageQueryStep(steps, row++, deviceManager, q05 + " " + suffix + " average R50 adc0 @ 15V 0A", "R50", prefix + q05 + "_ADC0_15V_0A", 650, 700, "count", 4, 20);
            AddMbdAverageQueryStep(steps, row++, deviceManager, q06 + " " + suffix + " average R56 adc14 @ 15V 0A", "R56", prefix + q06 + "_ADC14_15V_0A", 1350, 1550, "count", 4, 20);
            AddDaqAverageMvLimitedStep(steps, row++, deviceManager, q07 + " " + suffix + " DAQ V" + (vMeasureChannel == 109 ? "2" : "6") + " 15000mV (0A)", vMeasureChannel, prefix + q07 + "_VSTR_NEG_15000MV_0A", 14900, 15100);
            AddChargingMeasurementBatch(steps, context, highVoltageRow, 2, 1);
            AddProgrammingActionStep(steps, row++, "23.32 " + suffix + " set VSTR_NEG bus generator 1V/1A OVP150", token => deviceManager.SetVbusGeneratorAsync(1, 1, 150, true), "VBUS Set", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "23.33 " + suffix + " set string generator 10V/6A OVP150", token => deviceManager.SetStringGeneratorAsync(10, 6, 150, true), "String Set", "String Fail");
            AddPowerReadbackDelayStep(steps, row++, deviceManager,
                "23.34 " + suffix + " wait up to 7000ms for String supply (timeout continues)",
                7000, 500, "SECONDARY", 10, timeoutIsFailure: false);
            AddMbdCommandStep(steps, row++, deviceManager, "23.34 " + suffix + " set W69=0 CCB on and settle 500ms", "W69=0", 500);
            // Keep the newly enabled 6A working point sequential: its DAQ
            // current must not be prefetched before the two ADC series settle.
            AddMbdAverageQueryStep(steps, row++, deviceManager, q08 + " " + suffix + " average R50 adc0 @ 0V 6A", "R50", prefix + q08 + "_ADC0_0V_6A", 25, 40, "count", 4, 20);
            AddMbdAverageQueryStep(steps, row++, deviceManager, q09 + " " + suffix + " average R56 adc14 @ 0V 6A", "R56", prefix + q09 + "_ADC14_0V_6A", 2650, 3100, "count", 4, 20);
            AddDaqSignedShuntCurrentStep(steps, row++, deviceManager, q10 + " " + suffix + " shunt current -6000mA", shuntChannel, shuntOhm, prefix + q10 + "_CURRENT_6000MA", -6500, -5500);
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
                "23.51 " + suffix + " send ACT->FLASH after all calculated values are written",
                prefix + q12 + "_GAIN_VSTR_NEG",
                prefix + q13 + "_OFFSET_VSTR_NEG",
                prefix + q14 + "_GAIN_ICCB",
                prefix + q15 + "_GAIN_ICCB_VSTR_NEG",
                prefix + q16 + "_OFFSET_ICCB");
            AddProgrammingActionStep(steps, row++, "23.52 " + suffix + " turn off string generator", token => deviceManager.SetStringGeneratorAsync(0, 0, 10, false), "String OFF", "String Fail");
            AddProgrammingActionStep(steps, row++, "23.55 " + suffix + " reset generators 0V/0A OVP150", token => deviceManager.SetVbusGeneratorAsync(0, 0, 150, false), "Generators Reset", "Generator Fail");
            AddProgrammingActionStep(steps, row++, "23.56 " + suffix + " reset electronic load", token => deviceManager.ResetElectronicLoadAsync(), "Load Reset", "Load Fail");
            AddDelayStep(steps, row++, "23.57 " + suffix + " wait 1000ms", 1000);
            AddStringPowerPathRelayStep(steps, row++, deviceManager, "23.58 " + suffix + " open String power path Y1/Y4/Y6 and charging relays", "SKBCM125ChargingOpenRelays", Bcm125StationMap.AllStringPowerPathRelays, false, "Relays Open");
            AddDelayStep(steps, row++, "23.59 " + suffix + " wait 200ms", 200);
            AddMbdCommandStep(steps, row++, deviceManager, "23.59 " + suffix + " send EXIT to MBD terminal", "EXIT", 0);
        }

        private void AddChargingMeasurementBatch(
            System.Collections.Generic.List<TestStepItem> steps,
            TestSequenceContext context,
            int firstRow,
            int mbdCount,
            int daqCount)
        {
            // Only the consecutive, read-only Q rows at one working point are
            // eligible. One branch owns MBD; the other owns DAQ. Neither branch
            // changes generators, relays, terminal selection or calibration.
            TestStepItem[] members = steps.Skip(firstRow).Take(mbdCount + daqCount).ToArray();
            TestStepItem.AsyncStepAction[] actions = members.Select(step => step.Action).ToArray();
            StepResult[] cachedResults = null;
            Exception[] cachedErrors = null;
            Stopwatch cacheAge = null;
            int nextMember = -1;
            Bcm125MbdTerminalState sampledTerminal = Bcm125MbdTerminalState.Unknown;

            for (int index = 0; index < members.Length; index++)
            {
                int memberIndex = index;
                members[index].Action = async token =>
                {
                    token.ThrowIfCancellationRequested();
                    bool canBatch = context?.TestPlan != null &&
                        !context.StopOnFail && !context.PromptOnFail && context.FailRetryCount <= 0 &&
                        context.RequiresSynchronousUiResults?.Invoke() != true &&
                        members.All(step => context.TestPlan.ShouldRun(step.StepId) && step.MaxRetries <= 0);
                    bool cacheValid = canBatch && cachedResults != null &&
                        memberIndex == nextMember && cacheAge.ElapsedMilliseconds <= 1000 &&
                        sampledTerminal == _mbdTerminalState;
                    if (!cacheValid)
                    {
                        cachedResults = null;
                        cachedErrors = null;
                        nextMember = -1;
                    }

                    if (canBatch && memberIndex == 0)
                    {
                        var results = new StepResult[members.Length];
                        var errors = new Exception[members.Length];
                        OnLogInfo("BCM-125同工况并行采样：" +
                            string.Join(" + ", members.Select(step => step.StepId)));
                        // Task.Run also separates the synchronous serial/SCPI
                        // portions before each driver's first asynchronous wait.
                        await Task.WhenAll(
                            Task.Run(() => ReadChargingMeasurementBranchAsync(actions, 0, mbdCount,
                                results, errors, token), token),
                            Task.Run(() => ReadChargingMeasurementBranchAsync(actions, mbdCount, daqCount,
                                results, errors, token), token));
                        token.ThrowIfCancellationRequested();
                        cachedResults = results;
                        cachedErrors = errors;
                        sampledTerminal = _mbdTerminalState;
                        cacheAge = Stopwatch.StartNew();
                        nextMember = 0;
                    }

                    if (cachedResults == null || memberIndex != nextMember)
                        return await actions[memberIndex](token);

                    StepResult result = cachedResults[memberIndex];
                    Exception error = cachedErrors[memberIndex];
                    nextMember++;
                    // Never preserve values across a failed row, pause, retry,
                    // skipped member or an engineering breakpoint.
                    if (error != null || result == null || !result.IsPass || nextMember == members.Length)
                    {
                        cachedResults = null;
                        cachedErrors = null;
                        nextMember = -1;
                    }
                    if (error != null)
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
                    return result ?? await actions[memberIndex](token);
                };
            }
        }

        private static async Task ReadChargingMeasurementBranchAsync(
            TestStepItem.AsyncStepAction[] actions,
            int first,
            int count,
            StepResult[] results,
            Exception[] errors,
            CancellationToken token)
        {
            for (int index = first; index < first + count; index++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    results[index] = await actions[index](token);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    errors[index] = ex;
                    break;
                }
            }
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
                            || !calculated.HasNumericValue
                            || double.IsNaN(calculated.NumericValue)
                            || double.IsInfinity(calculated.NumericValue)
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
                        if (!calculated.Pass)
                            OnLogWarning(name + "：" + key + "超限但已写入有效数值，继续保存FLASH；原测试结果仍为NG。");
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
                    bool ccbLoadOn = !storeKey.StartsWith("BCM125.CCB.", StringComparison.Ordinal) ||
                        CheckCcbLoadInputOn(deviceManager, name);
                    bool pass = ccbLoadOn && mv >= plannedLower && mv <= plannedUpper;
                    return new StepResult { IsPass = pass,
                        Value = FormatValue(mv, plannedUnit) + (ccbLoadOn ? string.Empty : " (load OFF/unknown)"),
                        StoreKey = storeKey, NumericValue = mv, Unit = plannedUnit };
                }
            });
        }

        private void AddDaqSignedShuntCurrentStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, SK441Device deviceManager, string name, int channel, double shuntOhm, string storeKey, double lower, double upper,
            Func<CancellationToken, DaqVoltageSampleSeries> sharedWindowSamples = null)
        {
            bool stringTest = storeKey.StartsWith("BCM125.STRING_TEST.", StringComparison.Ordinal);
            steps.Add(new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                PowerCritical = true,
                // Evaluate one predetermined window, never retry until it passes.
                MaxRetries = stringTest ? 0 : -1,
                Action = async (token) =>
                {
                    double plannedLower, plannedUpper;
                    GetPlanRange(steps, rowIndex, lower, upper, out plannedLower, out plannedUpper);
                    double averageVolts;
                    if (stringTest)
                    {
                        DaqVoltageSampleSeries series;
                        if (sharedWindowSamples != null)
                            series = sharedWindowSamples(token);
                        else
                        {
                            token.ThrowIfCancellationRequested();
                            series = await deviceManager.MeasureBcm125StringTestShuntSamplesAsync(channel, token);
                        }
                        token.ThrowIfCancellationRequested();
                        averageVolts = series.Voltages.Average();
                        LogStringTestShuntSamples(name, series, shuntOhm, plannedLower, plannedUpper);
                    }
                    else
                    {
                        averageVolts = await deviceManager.MeasureDaqChannelVoltageAverageAsync(channel, 8, 0, token);
                    }
                    // The DAQ differential voltage already carries the current direction.
                    double currentMa = averageVolts / shuntOhm * 1000.0;
                    if (!stringTest)
                        OnLogInfo($"BCM-125 DAQ{channel}双向分流回读：V={averageVolts:0.########}V，" +
                            $"R={shuntOhm:0.########}Ω，I={currentMa:0.###}mA。");
                    string plannedUnit = GetPlanUnit(steps, rowIndex, "mA");
                    bool ccbTest = storeKey.StartsWith("BCM125.CCB.", StringComparison.Ordinal);
                    bool ccbLoadOn = !ccbTest || CheckCcbLoadInputOn(deviceManager, name);
                    bool pass = ccbLoadOn && currentMa >= plannedLower && currentMa <= plannedUpper;
                    if ((!pass || ccbTest) && !deviceManager.SkipComInit)
                    {
                        try
                        {
                            if (ccbTest)
                            {
                                float loadVoltage = deviceManager.ElectronicLoad.MeasureVoltage();
                                float loadCurrent = deviceManager.ElectronicLoad.MeasureCurrent();
                                OnLogInfo($"BCM-125 分流测量同步负载回读：输入={(ccbLoadOn ? "ON" : "OFF/unknown")}，" +
                                    $"{loadVoltage:0.###}V/{loadCurrent:0.###}A。");
                            }
                            else if (deviceManager.HengHuiPower2.IsConnected)
                            {
                                float sourceVoltage;
                                float sourceCurrent;
                                deviceManager.HengHuiPower2.ReadVoltageCurrent(out sourceVoltage, out sourceCurrent);
                                OnLogInfo($"BCM-125 分流测试同步String电源回读：" +
                                    $"{sourceVoltage:0.###}V/{sourceCurrent:0.###}A，" +
                                    $"DAQ{channel}={averageVolts:0.########}V。");
                                if (storeKey.Contains("_SHUNT_6000MA_2") &&
                                    deviceManager.AinuoPowerA.IsConnected)
                                {
                                    float busVoltage;
                                    float busCurrent;
                                    deviceManager.AinuoPowerA.ReadVoltageCurrent(
                                        out busVoltage, out busCurrent);
                                    OnLogInfo($"BCM-125 15V/6A分流异常时VBUS电源回读：" +
                                        $"{busVoltage:0.###}V/{busCurrent:0.###}A。");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            OnLogInfo("BCM-125 分流测试同步回读失败：" + ex.Message);
                        }
                    }
                    return new StepResult { IsPass = pass,
                        Value = FormatValue(currentMa, plannedUnit) + (ccbLoadOn ? string.Empty : " (load OFF/unknown)"),
                        StoreKey = storeKey, NumericValue = currentMa, Unit = plannedUnit };
                }
            });
        }

        private void LogStringTestShuntSamples(string name, DaqVoltageSampleSeries series,
            double shuntOhm, double lowerMa, double upperMa)
        {
            if (shuntOhm <= 0 || double.IsNaN(shuntOhm) || double.IsInfinity(shuntOhm))
                throw new InvalidOperationException("String-test shunt resistance must be finite and positive.");
            double[] currents = series.Voltages.Select(volts => volts / shuntOhm * 1000.0).ToArray();
            double average = currents.Average();
            if (average >= lowerMa && average <= upperMa)
            {
                OnLogInfo($"{name} 固定窗口：{currents.Length}点，" +
                    $"平均={average:0.###}mA，最小={currents.Min():0.###}mA，最大={currents.Max():0.###}mA，" +
                    $"低于{lowerMa:0.###}mA={currents.Count(v => v < lowerMa)}点，" +
                    $"高于{upperMa:0.###}mA={currents.Count(v => v > upperMa)}点，" +
                    $"限值=[{lowerMa:0.###},{upperMa:0.###}]mA（全部读数参与平均）。");
                return;
            }
            double[] sorted = currents.OrderBy(value => value).ToArray();
            int middle = sorted.Length / 2;
            double median = sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2.0 : sorted[middle];
            double deviation = Math.Sqrt(currents.Average(value => (value - average) * (value - average)));
            int lowCount = 0;
            int highCount = 0;
            int runStart = -1;
            int longestStart = -1;
            int longestEnd = -1;
            int longestCount = 0;
            for (int index = 0; index < currents.Length; index++)
            {
                if (currents[index] > upperMa) highCount++;
                if (currents[index] < lowerMa)
                {
                    lowCount++;
                    if (runStart < 0) runStart = index;
                    int count = index - runStart + 1;
                    if (count > longestCount)
                    {
                        longestCount = count;
                        longestStart = runStart;
                        longestEnd = index;
                    }
                }
                else runStart = -1;
            }
            double lowSpanMs = longestCount == 0 ? 0 :
                (series.RelativeTimesSeconds[longestEnd] - series.RelativeTimesSeconds[longestStart]) * 1000.0;
            string mode = series.IsSimulated ? "模拟" : "实测";
            OnLogInfo($"{name} 固定窗口统计（{mode}）：全样本={currents.Length}，R={shuntOhm:0.########}Ω，" +
                $"平均={average:0.###}mA，中位数={median:0.###}mA，最小={sorted.First():0.###}mA，" +
                $"最大={sorted.Last():0.###}mA，标准差={deviation:0.###}mA；" +
                $"低于{lowerMa:0.###}mA={lowCount}点/{lowCount * 100.0 / currents.Length:0.##}%，" +
                $"高于{upperMa:0.###}mA={highCount}点/{highCount * 100.0 / currents.Length:0.##}%；" +
                $"最长连续低值={longestCount}点，低值首末采样跨度={lowSpanMs:0.###}ms（非精确跌落时长）；" +
                "所有低值保留参与平均，仅平均值用于原限值判定。");
            // Preserve every measured value and actual relative timestamp in
            // manageable log lines; no trimming, clipping or absolute values.
            for (int start = 0; start < currents.Length; start += 20)
            {
                string samples = string.Join("; ", Enumerable.Range(start, Math.Min(20, currents.Length - start))
                    .Select(index => string.Format(CultureInfo.InvariantCulture, "{0}:{1:0.###}/{2:0.######}/{3:0.###}",
                        index + 1, series.RelativeTimesSeconds[index] * 1000.0,
                        series.Voltages[index] * 1000.0, currents[index])));
                OnLogInfo($"{name} 全样本（{mode}，序号:t_ms/V_mV/I_mA）：{samples}");
            }
        }

        private bool CheckCcbLoadInputOn(SK441Device deviceManager, string stepName)
        {
            if (deviceManager.SkipComInit)
                return true;

            try
            {
                if (deviceManager.ElectronicLoad.GetInputState())
                    return true;

                OnLogWarning(stepName + "：电子负载输入已关闭，测量结果判失败。");
            }
            catch (Exception ex)
            {
                OnLogWarning(stepName + "：无法确认电子负载输入状态，测量结果判失败：" + ex.Message);
            }
            return false;
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
                    if (!TryGetAvailableCalibrationNumericValue(context, prefix + q01 + "_ADC0_0V_0A", out adc0Low)
                        || !TryGetAvailableCalibrationNumericValue(context, prefix + q03 + "_VSTR_NEG_1000MV_0A", out vLow)
                        || !TryGetAvailableCalibrationNumericValue(context, prefix + q05 + "_ADC0_15V_0A", out adc0High)
                        || !TryGetAvailableCalibrationNumericValue(context, prefix + q07 + "_VSTR_NEG_15000MV_0A", out vHigh)
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
                    if (!TryGetAvailableCalibrationNumericValue(context, prefix + q01 + "_ADC0_0V_0A", out adc0Low)
                        || !TryGetAvailableCalibrationNumericValue(context, prefix + q03 + "_VSTR_NEG_1000MV_0A", out vLow)
                        || !TryGetAvailableCalibrationNumericValue(context, prefix + q12 + "_GAIN_VSTR_NEG", out gainVstr))
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
                    if (!TryGetAvailableCalibrationNumericValue(context, prefix + q02 + "_ADC14_0V_0A", out adc14Low)
                        || !TryGetAvailableCalibrationNumericValue(context, prefix + q04 + "_CURRENT_0MA", out current0)
                        || !TryGetAvailableCalibrationNumericValue(context, prefix + q09 + "_ADC14_0V_6A", out adc14Six)
                        || !TryGetAvailableCalibrationNumericValue(context, prefix + q10 + "_CURRENT_6000MA", out currentSix)
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
                    if (!TryGetAvailableCalibrationNumericValue(context, prefix + q01 + "_ADC0_0V_0A", out adc0Low)
                        || !TryGetAvailableCalibrationNumericValue(context, prefix + q02 + "_ADC14_0V_0A", out adc14Low)
                        || !TryGetAvailableCalibrationNumericValue(context, prefix + q05 + "_ADC0_15V_0A", out adc0High)
                        || !TryGetAvailableCalibrationNumericValue(context, prefix + q06 + "_ADC14_15V_0A", out adc14High)
                        || !TryGetAvailableCalibrationNumericValue(context, prefix + q14 + "_GAIN_ICCB", out gainIccb)
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
                    if (!TryGetAvailableCalibrationNumericValue(context, prefix + q01 + "_ADC0_0V_0A", out adc0Low)
                        || !TryGetAvailableCalibrationNumericValue(context, prefix + q02 + "_ADC14_0V_0A", out adc14Low)
                        || !TryGetAvailableCalibrationNumericValue(context, prefix + q14 + "_GAIN_ICCB", out gainIccb)
                        || !TryGetAvailableCalibrationNumericValue(context, prefix + q15 + "_GAIN_ICCB_VSTR_NEG", out gainIccbVstr))
                        return null;

                    return Math.Round(-1.0 * ((gainIccb * adc14Low) + (gainIccbVstr * adc0Low)) / 4096.0);
                });
        }

        private bool TryGetAvailableCalibrationNumericValue(
            TestSequenceContext context,
            string key,
            out double value)
        {
            TestSequenceValue stored;
            if (context != null && context.TryGetValue(key, out stored) &&
                stored.HasNumericValue && !double.IsNaN(stored.NumericValue) &&
                !double.IsInfinity(stored.NumericValue))
            {
                value = stored.NumericValue;
                return true;
            }

            value = 0.0;
            OnLogError("Missing or invalid calibration dependency: " + key);
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
                        NumericValue = calculated.HasValue ? (double?)value : null,
                        Unit = plannedUnit
                    };
                }
            });
        }

        protected void AddBcm125CurrentAndVstrNegVerificationSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager)
        {
            int row = steps.Count;

            AddProgrammingActionStep(steps, row++, "24.1 Close RL4 to power DUT", token => deviceManager.ControlRelayAsync(7, true), "RL4 ON", "RL4 Fail");
            AddStringPowerPathRelayStep(steps, row++, deviceManager, "24.2 Open String power path Y1/Y4/Y6 and RL1/RL2/RL9 relays", "SKBCM125VerificationOpenRelays", Bcm125StationMap.AllStringPowerPathRelays, false, "Relays Open");
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
            AddStringPowerPathRelayStep(steps, row++, deviceManager, "24.6 " + suffix + " close String power path Y1/Y4/Y6 and verification relays", relayConfigKey, relayDefaults, true, "Relays Closed");
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
            AddPowerReadbackDelayStep(steps, row++, deviceManager, "24.26 " + suffix + " wait up to 5000ms for String supply (timeout continues)", 5000, 200, "SECONDARY", 10, timeoutIsFailure: false);
            int ccbOnRow = row;
            AddMbdCommandStep(steps, row++, deviceManager, "24.26 " + suffix + " set W69=0 CCB on", "W69=0", 0);
            var enableCcb = steps[ccbOnRow].Action;
            steps[ccbOnRow].Action = async token =>
            {
                StepResult result = await enableCcb(token);
                if (!result.IsPass) return result;
                await WaitForBcm125CcbCurrentAsync(steps, ccbOnRow, deviceManager, shuntChannel,
                    shuntOhm, prefix + r06 + "_SHUNT_6000MA", token);
                return result;
            };
            AddMbdAverageQueryStep(steps, row++, deviceManager, r04 + " " + suffix + " read R42 vstr_neg @ 0V 6A", "R42", prefix + r04 + "_VSTR_NEG_0V_6A", 400, 1000, "mV", 1, 0);
            AddMbdAverageQueryStep(steps, row++, deviceManager, r05 + " " + suffix + " read R44 iccb @ 0V 6A", "R44", prefix + r05 + "_ICCB_0V_6A", 5950, 6050, "mA", 1, 0);
            AddDaqSignedShuntCurrentStep(steps, row++, deviceManager, r06 + " " + suffix + " shunt current -6000mA", shuntChannel, shuntOhm, prefix + r06 + "_SHUNT_6000MA", -6050, -5950);
            AddDaqAverageMvLimitedStep(steps, row++, deviceManager, r07 + " " + suffix + " DAQ V" + (vMeasureChannel == 109 ? "2" : "6") + " 0V 6A", vMeasureChannel, prefix + r07 + "_DAQ_0V_6A", 600, 900);
            AddProgrammingActionStep(steps, row++, "24.32 " + suffix + " turn CCB off and set string generator 0V/0A OVP150", async token =>
            {
                if (!await SendBcm125MbdCommandAsync(deviceManager, "W69=1", 0))
                    return false;
                return await deviceManager.SetStringGeneratorAsync(0, 0, 150, true);
            }, "CCB OFF / String Set", "CCB / String Fail");
            AddDelayStep(steps, row++, "24.33 " + suffix + " wait 500ms", 500);
            AddProgrammingActionStep(steps, row++, "24.34 " + suffix + " set bus generator 15V/1A OVP150", token => deviceManager.SetVbusGeneratorAsync(15, 1, 150, true), "VBUS Set", "VBUS Fail");
            AddDelayStep(steps, row++, "24.35 " + suffix + " wait 1000ms", 1000);
            AddMbdAverageQueryStep(steps, row++, deviceManager, r08 + " " + suffix + " read R42 vstr_neg @ 15V 0A", "R42", prefix + r08 + "_VSTR_NEG_15V_0A", 14900, 15100, "mV", 1, 0);
            AddMbdAverageQueryStep(steps, row++, deviceManager, r09 + " " + suffix + " read R44 iccb @ 15V 0A", "R44", prefix + r09 + "_ICCB_15V_0A", -20, 20, "mA", 1, 0);
            AddDaqAverageMvLimitedStep(steps, row++, deviceManager, r10 + " " + suffix + " DAQ V" + (vMeasureChannel == 109 ? "2" : "6") + " 15000mV", vMeasureChannel, prefix + r10 + "_DAQ_15V_0A", 14900, 15100);
            AddProgrammingActionStep(steps, row++, "24.40 " + suffix + " set string generator 10V/6A OVP150 and turn CCB on", async token =>
            {
                if (!await deviceManager.SetStringGeneratorAsync(10, 6, 150, true))
                    return false;
                return await SendBcm125MbdCommandAsync(deviceManager, "W69=0", 0);
            }, "String Set / CCB ON", "String / CCB Fail");
            AddDelayStep(steps, row++, "24.41 " + suffix + " wait 2000ms", 2000);
            AddMbdAverageQueryStep(steps, row++, deviceManager, r11 + " " + suffix + " read R42 vstr_neg @ 15V 6A", "R42", prefix + r11 + "_VSTR_NEG_15V_6A", 13500, 15100, "mV", 1, 0);
            AddMbdAverageQueryStep(steps, row++, deviceManager, r12 + " " + suffix + " read R44 iccb @ 15V 6A", "R44", prefix + r12 + "_ICCB_15V_6A", 5900, 6100, "mA", 1, 0);
            AddDaqAverageMvLimitedStep(steps, row++, deviceManager, r13 + " " + suffix + " DAQ V" + (vMeasureChannel == 109 ? "2" : "6") + " 15000mV 2", vMeasureChannel, prefix + r13 + "_DAQ_15V_6A", 13500, 15100);
            AddDaqSignedShuntCurrentStep(steps, row++, deviceManager, r14 + " " + suffix + " shunt current -6000mA 2", shuntChannel, shuntOhm, prefix + r14 + "_SHUNT_6000MA_2", -6050, -5950);
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
            AddStringPowerPathRelayStep(steps, row++, deviceManager, "24.57 " + suffix + " open String power path Y1/Y4/Y6 and verification relays", "SKBCM125VerificationOpenRelays", Bcm125StationMap.AllStringPowerPathRelays, false, "Relays Open");
        }

        private async Task WaitForBcm125CcbCurrentAsync(
            System.Collections.Generic.List<TestStepItem> steps, int rowIndex, SK441Device device,
            int shuntChannel, double shuntOhm, string resultKey, CancellationToken token)
        {
            if (device.SkipComInit) return;
            string resultId = resultKey.Split('.').Last().Split('_')[0];
            TestStepItem resultStep = steps.Single(step => step.StepId.EndsWith("." + resultId, StringComparison.OrdinalIgnoreCase));
            double? lower = resultStep.PlanDefinition.LowerLimit;
            double? upper = resultStep.PlanDefinition.UpperLimit;
            if (!lower.HasValue || !upper.HasValue)
                throw new TestPlanConfigurationException("CCB readiness requires XML shunt-current limits.");
            int maximumMs = GetPlanDelayMs(steps, rowIndex, 2500);
            var timer = Stopwatch.StartNew();
            int stableSamples = 0;
            while (timer.ElapsedMilliseconds < maximumMs)
            {
                token.ThrowIfCancellationRequested();
                double current = await device.MeasureDaqChannelVoltageAverageAsync(shuntChannel, 2, 0, token, false)
                    / shuntOhm * 1000;
                stableSamples = !double.IsNaN(current) && !double.IsInfinity(current) && current >= lower && current <= upper
                    ? stableSamples + 1 : 0;
                if (stableSamples >= 3)
                {
                    OnLogInfo($"CCB开启后DAQ{shuntChannel}电流连续三次就绪，耗时{timer.ElapsedMilliseconds}ms；正式结果仍重新采样。");
                    return;
                }
                int remainingMs = maximumMs - (int)timer.ElapsedMilliseconds;
                if (remainingMs > 0) await Task.Delay(Math.Min(100, remainingMs), token);
            }
            OnLogWarning($"CCB开启后电流在{maximumMs}ms内未连续就绪；保留正式测量及XML判定，不放宽阈值。");
        }

        protected void AddBcm125CcbFunctionalCheckSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager)
        {
            int row = steps.Count;

            AddProgrammingActionStep(steps, row++, "25.1 Close RL4 to power DUT", token => deviceManager.ControlRelayAsync(7, true), "RL4 ON", "RL4 Fail");
            AddStringPowerPathRelayStep(steps, row++, deviceManager, "25.2 Open String power path Y1/Y4/Y6 and RL1/RL2/RL9 relays", "SKBCM125CcbOpenRelays", Bcm125StationMap.AllStringPowerPathRelays, false, "Relays Open");
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
            AddStringPowerPathRelayStep(steps, row++, deviceManager, "25.7 " + suffix + " close String power path Y1/Y4/Y6 and CCB relays", relayConfigKey, relayDefaults, true, "Relays Closed");
            AddDelayStep(steps, row++, "25.8 " + suffix + " wait 50ms", 50);
            AddProgrammingActionStep(steps, row++, "25.9 " + suffix + " check serial communication", token => CheckBcm125MbdCommunicationAsync(deviceManager), "Open", "COM Fail");
            AddMbdCommandStep(steps, row++, deviceManager, "25.10 " + suffix + " send EXIT to MBD terminal", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "25.11 " + suffix + " send DIAG ON to MBD", "DIAG ON", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "25.12 " + suffix + " close MBD battery relay " + mbdRelayCommandPrefix + "1", mbdRelayCommandPrefix + "1", 0);
            AddProgrammingActionStep(steps, row++, "25.13 " + suffix + " set BUS generator 140V/10A OVP300", token => deviceManager.SetVbusGeneratorAsync(140, 10, 300, false), "VBUS Set", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "25.14 " + suffix + " turn on BUS generator", token => deviceManager.SetVbusGeneratorAsync(140, 10, 300, true), "VBUS ON", "VBUS Fail");
            AddPowerReadbackDelayStep(steps, row++, deviceManager, "25.15 " + suffix + " wait 15000ms", 15000, 3000, "VBUS", 140);
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
            AddMeasuredReadinessDelayStep(steps, row++, deviceManager,
                "25.34 " + suffix + " wait up to 10000ms for load/current", 10000, 1500,
                new Bcm125ReadinessProbe
                {
                    DaqChannel = shuntChannel,
                    DaqScale = 1000.0 / shuntOhm,
                    ResultStepId = suffix == "ST1" ? "BCM125.CH25.S036.S01" : "BCM125.CH25.S082.S03",
                    RequireElectronicLoadOn = true,
                    FailOnTimeout = true
                });
            AddDaqSignedShuntCurrentStep(steps, row++, deviceManager, sCurrent + " " + suffix + " shunt current -6000mA", shuntChannel, shuntOhm, prefix + sCurrent + "_SHUNT_6000MA", -6200, -5800);
            AddProgrammingActionStep(steps, row++, "25.36 " + suffix + " set electronic load 130V", token => deviceManager.SetElectronicLoadCvAsync(130, 10, 1250, true), "Load Set", "Load Fail");
            AddMeasuredReadinessDelayStep(steps, row++, deviceManager,
                "25.37 " + suffix + " wait up to 4000ms for load/voltage", 4000, 500,
                new Bcm125ReadinessProbe
                {
                    DaqChannel = stringVoltageChannel,
                    ResultStepId = suffix == "ST1" ? "BCM125.CH25.S039.S02" : "BCM125.CH25.S085.S04",
                    RequireElectronicLoadOn = true,
                    FailOnTimeout = true
                });
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
            AddStringPowerPathRelayStep(steps, row++, deviceManager, "25.49 " + suffix + " open String power path Y1/Y4/Y6 and CCB relays", "SKBCM125CcbOpenRelays", Bcm125StationMap.AllStringPowerPathRelays, false, "Relays Open");
            AddDelayStep(steps, row++, "25.50 " + suffix + " wait 200ms", 200);
            AddMbdCommandStep(steps, row++, deviceManager, "25.54 " + suffix + " send EXIT to MBD terminal", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "25.55 " + suffix + " send DIAG ON to MBD", "DIAG ON", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "25.56 " + suffix + " open MBD battery relay " + mbdRelayCommandPrefix + "0", mbdRelayCommandPrefix + "0", 0);
        }

        protected void AddBcm125StringTestSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager)
        {
            int row = steps.Count;

            AddProgrammingActionStep(steps, row++, "26.1 Close RL4 to power DUT", token => deviceManager.ControlRelayAsync(7, true), "RL4 ON", "RL4 Fail");
            AddStringPowerPathRelayStep(steps, row++, deviceManager, "26.2 Open String power path Y1/Y4/Y6 and RL1/RL2/RL9 relays", "SKBCM125StringTestOpenRelays", Bcm125StationMap.AllStringPowerPathRelays, false, "Relays Open");
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
            AddStringPowerPathRelayStep(steps, row++, deviceManager, "26.7 " + suffix + " close String power path Y1/Y4/Y6 and string test relays", relayConfigKey, relayDefaults, true, "Relays Closed");
            AddDelayStep(steps, row++, "26.8 " + suffix + " wait 20ms", 20);
            AddProgrammingActionStep(steps, row++, "26.9 " + suffix + " set BUS generator 50V/0.5A OVP150", token => deviceManager.SetVbusGeneratorAsync(50, 0.5f, 150, false), "VBUS Set", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "26.10 " + suffix + " turn on BUS generator", token => deviceManager.SetVbusGeneratorAsync(50, 0.5f, 150, true), "VBUS ON", "VBUS Fail");
            AddPowerReadbackDelayStep(steps, row++, deviceManager, "26.11 " + suffix + " wait 3000ms", 3000, 1000, "VBUS", 50);
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
            AddStringTestMosOnWindowSteps(steps, deviceManager, ref row, channel);
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
            AddStringPowerPathRelayStep(steps, row++, deviceManager, "26.37 " + suffix + " open String power path Y1/Y4/Y6 and string test relays", "SKBCM125StringTestOpenRelays", Bcm125StationMap.AllStringPowerPathRelays, false, "Relays Open");
            AddDelayStep(steps, row++, "26.38 " + suffix + " wait 200ms", 200);
            AddMbdCommandStep(steps, row++, deviceManager, "26.38 " + suffix + " send EXIT to MBD terminal", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "26.39 " + suffix + " send DIAG ON to MBD", "DIAG ON", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "26.40 " + suffix + " open MBD string relay " + mbdRelayCommandPrefix + "0", mbdRelayCommandPrefix + "0", 0);
        }

        protected void AddBcm125WritingInfoFieldsSteps(System.Collections.Generic.List<TestStepItem> steps, TestSequenceContext context, SK441Device deviceManager)
        {
            int row = steps.Count;

            AddProgrammingActionStep(steps, row++, "27.1 Close RL4 to power DUT", token => deviceManager.ControlRelayAsync(7, true), "RL4 ON", "RL4 Fail");
            AddStringPowerPathRelayStep(steps, row++, deviceManager, "27.2 Open String power path Y1/Y4/Y6 and RL1/RL2/RL9 relays", "SKBCM125WritingInfoOpenRelays", Bcm125StationMap.AllStringPowerPathRelays, false, "Relays Open");
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
                "27.24 Open RL4 and electronic load path Y2/Y5 after outputs are off",
                async token =>
                {
                    bool dutRelayOff = await deviceManager.ControlRelayAsync(7, false);
                    bool loadPathOff = await deviceManager.SetBcm125PowerPathRelaysAsync(
                        new[] { 2, 5 }, false, "BCM125 cleanup electronic load path");
                    return dutRelayOff && loadPathOff;
                },
                "RL4 / Load Path OFF",
                "Relay Fail");

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

            AddMbdCommandPairStep(
                steps,
                row++,
                deviceManager,
                "27.8/9 " + suffix + " send EXIT then " + remoteCommand,
                "EXIT",
                remoteCommand,
                0);
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
            AddMbdStringCompareStep(steps, row++, deviceManager, uDate + " " + suffix + " verify R40 calibration date", "R40", () => calibrationDate, prefix + uDate + "_DATE", retryMalformedDate: true);
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
                        && TargetBoardComm.HasTerminalPrompt(response);
                    return new StepResult { IsPass = pass, Value = pass ? "1" : (response ?? "No Data"), StoreKey = storeKey, NumericValue = pass ? 1 : 0, Unit = string.Empty };
                }
            });
        }

        private void AddMbdStringCompareStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, SK441Device deviceManager, string name, string command, Func<string> expectedFactory, string storeKey, bool retryMalformedDate = false)
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
                    bool reread = false;
                    DateTime parsedDate;
                    if (retryMalformedDate && !string.IsNullOrWhiteSpace(expected) &&
                        !DateTime.TryParseExact(actual, "dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out parsedDate))
                    {
                        OnLogWarning($"{name}：日期回读格式异常；写入='{expected}'，回读='{actual}'。仅追加一次R40只读复核，不重写或截断日期。");
                        await Task.Delay(GetPlanDelayMs(steps, rowIndex, 200), token);
                        response = await QueryBcm125MbdCommandAsync(deviceManager, command, 0);
                        actual = MbdResponseParser.ParsePayloadText(command, response);
                        reread = true;
                    }
                    bool pass = !string.IsNullOrWhiteSpace(expected)
                        && string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
                    if (!pass)
                        OnLogError($"{name}：写入='{expected}'，回读='{actual}'，严格比较不一致，保留失败。");
                    return new StepResult { IsPass = pass, Value = pass ? (reread ? "1 (re-read verified)" : "1") : actual, StoreKey = storeKey, NumericValue = pass ? 1 : 0, Unit = string.Empty };
                }
            });
        }

        private void AddMbdNumericQueryStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, SK441Device deviceManager, string name, string command, string storeKey, double lower, double upper, string unit, bool advisoryOutsideRange = false)
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
                        IsPass = pass || advisoryOutsideRange,
                        IsWarning = !pass && advisoryOutsideRange,
                        Value = FormatValue(value, plannedUnit) + (!pass && advisoryOutsideRange
                            ? " (outside advisory range, continued)" : string.Empty),
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
                // CH8's repeated MEAS? calls timed out on every channel after
                // B01, while later DAQ buffered reads completed reliably.
                return await deviceManager.MeasureDaqChannelVoltageAverageAsync(
                    channel, 2, 0, token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                OnLogWarning(
                    $"BCM-125 DAQ通道{channel}批量测量失败：{ex.Message}；" +
                    "恢复DAQ状态后改用单次读取。");
                deviceManager.RecoverDaqVoltageMeasurement("BCM125第八章批量读取失败");
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
