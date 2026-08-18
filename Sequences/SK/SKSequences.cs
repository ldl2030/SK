using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;

namespace TestPlatform.TestSequences
{
    public abstract partial class SKBaseSequence : ITestSequence
    {
        public abstract string SequenceKey { get; }
        
        // 期望放上的板子类型，由子类指定，用于产线防呆
        public abstract string ExpectedBoardType { get; }
        protected virtual bool SkipFirmwareProgramming => false;

        public event Action<string> LogInfo;
        public event Action<string> LogWarning;
        public event Action<string> LogError;
        public event Action<string> LogSuccess;

        protected void OnLogInfo(string msg) => LogInfo?.Invoke(msg);
        protected void OnLogWarning(string msg) => LogWarning?.Invoke(msg);
        protected void OnLogError(string msg) => LogError?.Invoke(msg);
        protected void OnLogSuccess(string msg) => LogSuccess?.Invoke(msg);

        private readonly ITestGridService _grid;
        private readonly Func<string, Task<bool>> _confirmAsync;
        private readonly object _fixtureReleaseSync = new object();
        private Task<bool> _fixtureReleaseCommandTask;
        private Task<bool> _fixtureReleaseVerificationTask;
        private Task<bool> _rl4OffTask;
        private Task<bool> _allInstrumentsOffTask;
        private Task<bool> _ansPowerOffTask;

        protected SKBaseSequence(ITestGridService grid = null, Func<string, Task<bool>> confirmAsync = null)
        {
            _grid = grid;
            _confirmAsync = confirmAsync;
        }

        // 子类必须实现这个方法，把后续的电气测试项 Add 到 steps 里
        protected abstract void AddElectricalTestSteps(System.Collections.Generic.List<TestStepItem> steps, TestSequenceContext context, SK441Device deviceManager);

        protected class TestStepItem
        {
            public string Name { get; set; }
            public string StepId { get; set; }
            public TestPlanStepDefinition PlanDefinition { get; set; }
            public string ExpectedGroupId { get; set; }
            public int RowIndex { get; set; }
            public int MaxRetries { get; set; } = -1;
            public bool SafetyCritical { get; set; }
            public bool PowerCritical { get; set; }
            public bool AlwaysRun { get; set; }
            public bool IsFixtureReleaseStep { get; set; }
            public delegate Task<StepResult> AsyncStepAction(CancellationToken token);
            public AsyncStepAction Action { get; set; }
        }

        protected class StepResult
        {
            public bool IsPass { get; set; }
            public bool IsCanceled { get; set; }
            public int AttemptCount { get; set; } = 1;
            public string Value { get; set; }
            public string StoreKey { get; set; }
            public double? NumericValue { get; set; }
            public string Unit { get; set; }
        }

        public async Task<bool> RunAsync(TestSequenceContext context)
        {
            ResetCleanupState();
            LogInfo?.Invoke("==============================================");
            LogInfo?.Invoke("SK441 测试流程开始 - 正在初始化...");
            LogInfo?.Invoke("==============================================");

            // 获取所需的 7 个串口号
            ISet<string> requiredPorts = context.RequiredComPorts;
            if (SkipFirmwareProgramming && requiredPorts != null)
            {
                requiredPorts = new HashSet<string>(
                    requiredPorts.Where(port =>
                        !string.Equals(port, "uartComName", StringComparison.OrdinalIgnoreCase)),
                    StringComparer.OrdinalIgnoreCase);
            }
            string ansPort = ResolveProjectPort(requiredPorts, "ansPowerComName", ComName.ansPowerComName, "AnsPort");
            string henghui1Port = ResolveProjectPort(requiredPorts, "hengHuiPowerAComName", ComName.hengHuiPowerAComName, "HengHui1Port");
            string henghui2Port = ResolveProjectPort(requiredPorts, "hengHuiPowerBComName", ComName.hengHuiPowerBComName, "HengHui2Port");
            string loadPort = ResolveProjectPort(requiredPorts, "electronicLoadComName", ComName.electronicLoadComName, "LoadPort");
            string daqPort = ResolveProjectPort(requiredPorts, "daqComName", ComName.daqComName, "DaqPort");
            string digitalInputPort = ResolveProjectPort(requiredPorts, "digitalInputComName", ComName.digitalInputComName, "SKDigitalInputPort");
            string fixtureRelayPort = ResolveProjectPort(requiredPorts, "fixtureRelayComName", ComName.fixtureRelayComName, "SKFixtureRelayComPort");
            string relay16Port = ResolveProjectPort(requiredPorts, "relay16ComName", ComName.relay16ComName, "SKRelayComPort");
            string targetBoardPort = FirstNonEmpty(ComName.testComName, System.Configuration.ConfigurationManager.AppSettings["TargetBoardPort"]);
            string ttlPort = FirstNonEmpty(ComName.uartComName, System.Configuration.ConfigurationManager.AppSettings["TtlPort"]);

            using (var deviceManager = new SK441Device())
            {
                deviceManager.LogInfo = msg => LogInfo?.Invoke(msg);
                deviceManager.LogError = msg => LogError?.Invoke(msg);
                deviceManager.SimulationBoardType = ExpectedBoardType;
                deviceManager.SkipFirmwareProgramming = SkipFirmwareProgramming;
                LogWarning?.Invoke(
                    $">> SK execution mode: {deviceManager.ExecutionModeName}. " +
                    "Switch App.config SKExecutionMode between Simulation and Hardware.");

                try
                {
                    if (IsBcm250FixtureOnlyMode(deviceManager))
                    {
                        return await RunBcm250FixtureOnlyAsync(
                            context,
                            deviceManager,
                            context.CancellationToken);
                    }

                    bool autoDiscover = !string.Equals(
                        System.Configuration.ConfigurationManager.AppSettings["SKAutoDiscoverPorts"],
                        "false",
                        StringComparison.OrdinalIgnoreCase);
                    if (!deviceManager.SkipComInit && autoDiscover)
                    {
                        bool requireTtl = !string.Equals(
                            System.Configuration.ConfigurationManager.AppSettings["SKRequireTtl"],
                            "false",
                            StringComparison.OrdinalIgnoreCase);
                        bool alreadyDiscovered =
                            IsRequiredPortReady(requiredPorts, "ansPowerComName", ansPort) &&
                            IsRequiredPortReady(requiredPorts, "hengHuiPowerAComName", henghui1Port) &&
                            IsRequiredPortReady(requiredPorts, "hengHuiPowerBComName", henghui2Port) &&
                            IsRequiredPortReady(requiredPorts, "electronicLoadComName", loadPort) &&
                            IsRequiredPortReady(requiredPorts, "daqComName", daqPort) &&
                            IsRequiredPortReady(requiredPorts, "digitalInputComName", digitalInputPort) &&
                            IsRequiredPortReady(requiredPorts, "fixtureRelayComName", fixtureRelayPort) &&
                            IsRequiredPortReady(requiredPorts, "relay16ComName", relay16Port) &&
                            (!requireTtl || IsRequiredPortReady(requiredPorts, "uartComName", ttlPort));
                        if (alreadyDiscovered)
                        {
                            ansPort = ResolveProjectPort(requiredPorts, "ansPowerComName", ComName.ansPowerComName, "AnsPort");
                            henghui1Port = ResolveProjectPort(requiredPorts, "hengHuiPowerAComName", ComName.hengHuiPowerAComName, "HengHui1Port");
                            henghui2Port = ResolveProjectPort(requiredPorts, "hengHuiPowerBComName", ComName.hengHuiPowerBComName, "HengHui2Port");
                            loadPort = ResolveProjectPort(requiredPorts, "electronicLoadComName", ComName.electronicLoadComName, "LoadPort");
                            daqPort = ResolveProjectPort(requiredPorts, "daqComName", ComName.daqComName, "DaqPort");
                            digitalInputPort = ResolveProjectPort(requiredPorts, "digitalInputComName", ComName.digitalInputComName, "SKDigitalInputPort");
                            fixtureRelayPort = ResolveProjectPort(requiredPorts, "fixtureRelayComName", ComName.fixtureRelayComName, "SKFixtureRelayComPort");
                            relay16Port = ResolveProjectPort(requiredPorts, "relay16ComName", ComName.relay16ComName, "SKRelayComPort");
                            targetBoardPort = ComName.testComName;
                            ttlPort = ComName.uartComName;
                        }
                        else
                        {
                            LogInfo?.Invoke("正在读取预设COM口；仅对未配置的必需设备自动扫描...");
                            SKSerialDeviceDiscoveryResult discovery =
                                await SKSerialDeviceDiscovery.ResolveConfiguredOrDiscoverAsync(
                                    msg => LogInfo?.Invoke(msg),
                                    requiredPorts);
                            foreach (string error in discovery.Errors)
                                LogError?.Invoke(error);
                            if (!discovery.IsCompleteFor(requiredPorts))
                            {
                                LogError?.Invoke(
                                    $"缺少必需设备：{string.Join("、", discovery.GetMissingDeviceNames(requiredPorts))}");
                                return false;
                            }

                            ansPort = discovery.AnsPowerPort;
                            henghui1Port = discovery.HengHuiPowerAPort;
                            henghui2Port = discovery.HengHuiPowerBPort;
                            loadPort = discovery.ElectronicLoadPort;
                            daqPort = discovery.DaqPort;
                            digitalInputPort = discovery.DigitalInputPort;
                            fixtureRelayPort = discovery.FixtureRelayPort;
                            relay16Port = discovery.Relay16Port;
                            targetBoardPort = discovery.TargetBoardPort;
                            ttlPort = discovery.TtlPort;
                        }
                    }

                    LogInfo?.Invoke("尝试打开物理串口硬件...");
                    bool initSuccess = await deviceManager.InitializeAllDevicesAsync(
                        ansPort,
                        henghui1Port,
                        henghui2Port,
                        loadPort,
                        daqPort,
                        digitalInputPort,
                        fixtureRelayPort,
                        relay16Port,
                        targetBoardPort,
                        ttlPort,
                        requiredPorts);

                    if (!initSuccess)
                    {
                        LogError?.Invoke(">> 基础串口连接失败！测试中止。");
                        return false;
                    }

                    // 安装和下压前先清除全部软件输出；急停治具上升由硬件安全回路负责。
                    bool startupOutputsOff = await deviceManager.TurnOffAllInstrumentsAsync();
                    bool startupRelaysOff = startupOutputsOff && await deviceManager.OpenAllRelaysAsync();
                    bool startupFixtureCommandOff =
                        await deviceManager.ResetFixturePressDownCommandAsync(ExpectedBoardType);
                    DigitalInputReadResult startupSafety =
                        await deviceManager.ReadDigitalInputsSafeAsync();
                    bool startupInputLost =
                        !startupSafety.CommunicationOk ||
                        startupSafety.States == null ||
                        startupSafety.States.Length < 16;
                    bool startupEmergency =
                        !startupInputLost &&
                        deviceManager.IsEmergencyStopTriggered(startupSafety.States[7]);
                    if (!startupOutputsOff || !startupRelaysOff ||
                        !startupFixtureCommandOff || startupInputLost || startupEmergency)
                    {
                        string reason = startupInputLost
                            ? "测试初始化时数字输入模块通信失败"
                            : startupEmergency
                                ? "测试初始化时急停未复位"
                                : "测试初始化安全输出复位失败";
                        await deviceManager.EmergencyStopOutputsAsync(ExpectedBoardType, reason);
                        LogError?.Invoke("测试初始化安全检查失败，禁止进入安装和下压流程。");
                        return false;
                    }

                    if (string.Equals(
                            ExpectedBoardType,
                            "BCM-250",
                            StringComparison.OrdinalIgnoreCase) &&
                        !await deviceManager.EnableBcm250ElectronicLoadSupplyAsync())
                    {
                        await deviceManager.EmergencyStopOutputsAsync(
                            ExpectedBoardType,
                            "电子负载 Y2/Y5 上电或通信确认失败");
                        LogError?.Invoke(
                            "电子负载供电初始化失败，禁止进入后续功率继电器测试。");
                        return false;
                    }

                    // 构建前置安全检查动作
                    var steps = BuildCommonSteps(context, deviceManager);
                    // 补充该派生类专属的后续电气测试动作
                    AddElectricalTestSteps(steps, context, deviceManager);
                    BindStableStepIds(context, steps);
                    
                    bool allPassed = true;
                    var executedStepIds = new System.Collections.Generic.HashSet<string>(
                        StringComparer.OrdinalIgnoreCase);

                    // 核心修改：创建一个融合了用户取消和后台安全打断的 CancellationTokenSource
                    using (var safetyCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken))
                    {
                        // 启动一个后台全局安全监控线程
                        var safetyTask = Task.Run(async () =>
                        {
                            try
                            {
                                while (!safetyCts.Token.IsCancellationRequested)
                                {
                                    await Task.Delay(
                                        deviceManager.GetSafetyPollMilliseconds(),
                                        safetyCts.Token);
                                    if (safetyCts.Token.IsCancellationRequested) break;
                                    
                                    DigitalInputReadResult inputResult =
                                        await deviceManager.ReadDigitalInputsSafeAsync();
                                    bool inputLost =
                                        !inputResult.CommunicationOk ||
                                        inputResult.States == null ||
                                        inputResult.States.Length < 16;
                                    bool emergencyPressed =
                                        !inputLost &&
                                        deviceManager.IsEmergencyStopTriggered(inputResult.States[7]);

                                    if (inputLost || emergencyPressed)
                                    {
                                        string reason = inputLost
                                            ? "数字输入模块通信失联"
                                            : "数字量8检测到急停开关按下";
                                        LogError?.Invoke("【系统全局报警】" + reason + "，立即关闭全部测试输出！");
                                        deviceManager.LatchEmergencyStop(reason);
                                        safetyCts.Cancel();
                                        await deviceManager.EmergencyStopOutputsAsync(
                                            ExpectedBoardType,
                                            reason);
                                        break;
                                    }
                                }
                            }
                            catch (OperationCanceledException) { /* 忽略监控器由于正常取消导致的异常 */ }
                            catch (Exception ex)
                            {
                                string reason = "急停监控线程异常: " + ex.Message;
                                LogError?.Invoke(reason);
                                deviceManager.LatchEmergencyStop(reason);
                                safetyCts.Cancel();
                                await deviceManager.EmergencyStopOutputsAsync(
                                    ExpectedBoardType,
                                    reason);
                            }
                        });

                        try
                        {
                            if (_grid != null)
                            {
                                for (int i = 0; i < steps.Count; i++)
                                {
                                    if (safetyCts.Token.IsCancellationRequested)
                                    {
                                        allPassed = false;
                                        break;
                                    }

                                    var step = steps[i];
                                    
                                    bool isSelected = context.TestPlan != null
                                        ? context.TestPlan.ShouldRun(step.StepId)
                                        : await _grid.IsRowSelectedAsync(step.RowIndex);
                                    if (!isSelected) continue;

                                    StepResult result = await RunStepAsync(
                                        context,
                                        deviceManager,
                                        step,
                                        i + 1,
                                        steps.Count,
                                        safetyCts.Token);
                                    StoreStepResult(context, result);
                                    if (!string.IsNullOrWhiteSpace(step.StepId))
                                        executedStepIds.Add(step.StepId);

                                    if (!result.IsPass)
                                    {
                                        allPassed = false;

                                        if (result.IsCanceled)
                                        {
                                            LogWarning?.Invoke($">> {step.Name} 已被用户停止，不计为产品功能失败。");
                                            await SafeShutdownAsync(
                                                deviceManager,
                                                $"用户停止: {step.Name}",
                                                true);
                                            break;
                                        }

                                        LogError?.Invoke($">> {step.Name} 测试失败！");
                                        bool powerCriticalMustStop =
                                            step.PowerCritical &&
                                            !context.ContinueAfterPowerCriticalFailure;
                                        bool terminalFailure =
                                            step.SafetyCritical ||
                                            powerCriticalMustStop ||
                                            context.StopOnFail;
                                        if (terminalFailure)
                                        {
                                            if (!context.StopOnFail &&
                                                (step.SafetyCritical || step.PowerCritical))
                                            {
                                                OnLogWarning(
                                                    $"步骤 '{step.Name}' 属于" +
                                                    $"{(step.SafetyCritical ? "安全关键" : "供电关键")}项；" +
                                                    "已覆盖普通失败后继续设置并执行安全停机。");
                                            }
                                            await ExecuteChapterFailurePolicyAsync(
                                                context,
                                                deviceManager,
                                                step);
                                            string level = step.SafetyCritical
                                                ? "安全关键"
                                                : step.PowerCritical ? "供电关键" : "普通";
                                            await SafeShutdownAsync(
                                                deviceManager,
                                                $"{level}步骤失败: {step.Name}",
                                                step.SafetyCritical);
                                            break;
                                        }

                                        if (context.PromptOnFail)
                                        {
                                            bool continueTesting = await ConfirmAsync(
                                                $"测试项失败：\n{step.Name}\n\n" +
                                                "失败结果已经记录。是否继续执行后续测试项？");
                                            if (!continueTesting)
                                            {
                                                OnLogWarning(
                                                    $"操作员选择在步骤 '{step.Name}' 失败后停止后续测试。");
                                                await ExecuteChapterFailurePolicyAsync(
                                                    context,
                                                    deviceManager,
                                                    step);
                                                await SafeShutdownAsync(
                                                    deviceManager,
                                                    $"操作员选择停止: {step.Name}",
                                                    false);
                                                break;
                                            }

                                            OnLogWarning(
                                                $"操作员确认在步骤 '{step.Name}' 失败后继续测试；失败结果保持为FAIL。");
                                        }
                                        if (step.PowerCritical &&
                                            context.ContinueAfterPowerCriticalFailure)
                                        {
                                            OnLogWarning(
                                                $"工程配置允许供电关键步骤 '{step.Name}' 失败后继续；" +
                                                "当前失败已记录，未执行章节断电处理。");
                                        }
                                    }
                                }

                                if (deviceManager.IsEmergencyStopLatched)
                                {
                                    await deviceManager.EmergencyStopOutputsAsync(
                                        ExpectedBoardType,
                                        deviceManager.EmergencyStopReason);
                                }

                                bool cleanupPassed = await RunPendingAlwaysRunStepsAsync(
                                    context,
                                    deviceManager,
                                    steps,
                                    executedStepIds);
                                allPassed = allPassed && cleanupPassed;
                            }
                        }
                        finally
                        {
                            // 必须先停止并等待安全监控退出，再释放 deviceManager。
                            safetyCts.Cancel();
                            try
                            {
                                await safetyTask;
                            }
                            catch (OperationCanceledException)
                            {
                                // 正常退出。
                            }
                        }
                    }

                    if (_grid == null || !steps.Any(x => x.IsFixtureReleaseStep))
                    {
                        await HandleFixtureStopAsync(
                            deviceManager,
                            allPassed ? "测试流程完成" : "测试流程结束，存在失败项",
                            false);
                    }

                    if (allPassed)
                        LogSuccess?.Invoke("SK441 测试流程全部执行通过。");
                    else
                        LogError?.Invoke("SK441 测试流程结束，存在失败项或被急停熔断。");

                    return allPassed;
                }
                catch (OperationCanceledException)
                {
                    LogWarning?.Invoke("SK441 测试被用户手动取消或因急停被打断。");
                    await SafeShutdownAsync(deviceManager, "用户取消或急停打断", true);
                    return false;
                }
                catch (Exception ex)
                {
                    LogError?.Invoke($"SK441 测试发生异常: {ex.Message}");
                    await SafeShutdownAsync(deviceManager, "测试异常", true);
                    return false;
                }
            }
        }

        private static string ResolveProjectPort(
            ISet<string> requiredPorts,
            string propertyName,
            string runtimePort,
            string appSettingKey)
        {
            // RequiredComPorts controls whether a connection failure blocks startup.
            // It must not hide configured optional instruments: later test chapters may
            // need them, so SK441Device gets a chance to connect them opportunistically.
            return FirstNonEmpty(
                runtimePort,
                System.Configuration.ConfigurationManager.AppSettings[appSettingKey]);
        }

        private static bool IsRequiredPortReady(
            ISet<string> requiredPorts,
            string propertyName,
            string port)
        {
            return requiredPorts != null && requiredPorts.Count > 0
                ? !requiredPorts.Contains(propertyName) || IsPortAvailable(port)
                : IsPortAvailable(port);
        }

        private static bool IsPortAvailable(string port)
        {
            return !string.IsNullOrWhiteSpace(port) &&
                   System.IO.Ports.SerialPort.GetPortNames().Contains(
                       port,
                       StringComparer.OrdinalIgnoreCase);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
        }

        private bool IsBcm250FixtureOnlyMode(SK441Device deviceManager)
        {
            bool enabled;
            bool.TryParse(
                System.Configuration.ConfigurationManager.AppSettings["SKBCM250FixtureOnly"],
                out enabled);
            return enabled &&
                   !deviceManager.SkipComInit &&
                   string.Equals(
                       ExpectedBoardType,
                       "BCM-250",
                       StringComparison.OrdinalIgnoreCase);
        }

        private async Task<bool> RunBcm250FixtureOnlyAsync(
            TestSequenceContext context,
            SK441Device deviceManager,
            CancellationToken token)
        {
            LogWarning?.Invoke(
                "BCM-250真实下压验证模式：只读取安全数字量并操作工装继电器1；" +
                "跳过其它继电器和全部电气测试。");

            if (string.IsNullOrWhiteSpace(ComName.digitalInputComName) ||
                string.IsNullOrWhiteSpace(ComName.fixtureRelayComName))
            {
                SKSerialDeviceDiscoveryResult discovery =
                    await SKSerialDeviceDiscovery.ResolveConfiguredOrDiscoverAsync(
                        msg => LogInfo?.Invoke(msg));
                foreach (string error in discovery.Errors)
                    LogWarning?.Invoke(error);
                if (string.IsNullOrWhiteSpace(discovery.DigitalInputPort) ||
                    string.IsNullOrWhiteSpace(discovery.FixtureRelayPort))
                {
                    LogError?.Invoke(
                        "未同时识别到工装继电器站1和数字输入站2，不能执行真实下压验证。");
                    return false;
                }
            }

            ComName.rs485ComName = ComName.fixtureRelayComName;
            LogInfo?.Invoke(
                $"下压验证总线：继电器={ComName.fixtureRelayComName}，" +
                $"数字输入={ComName.digitalInputComName}，继电器站号=" +
                $"{System.Configuration.ConfigurationManager.AppSettings["SKFixtureRelayAddress"] ?? "1"}，" +
                "仅继电器1。");

            TestStepItem pressDownStep = BuildCommonSteps(context, deviceManager)
                .First(item => item.Name.StartsWith("下压许可检查", StringComparison.Ordinal));
            StepResult result = await pressDownStep.Action(token);
            if (!result.IsPass)
            {
                await deviceManager.StopFixturePressDownAsync("BCM-250");
                LogError?.Invoke($"BCM-250下压验证失败：{result.Value}");
                return false;
            }

            LogSuccess?.Invoke("BCM-250下压已到位；保持2秒后自动断开继电器1并释放治具。");
            await Task.Delay(2000, token);
            bool released = await deviceManager.StopFixturePressDownAsync("BCM-250");
            if (!released)
            {
                LogError?.Invoke("BCM-250下压到位，但断开继电器1释放治具失败。");
                return false;
            }

            LogSuccess?.Invoke("BCM-250真实下压验证完成，继电器1已断开。");
            return true;
        }

        private async Task<StepResult> RunStepAsync(
            TestSequenceContext context,
            SK441Device deviceManager,
            TestStepItem step,
            int currentStep,
            int totalSteps,
            CancellationToken token)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            StepResult finalResult = new StepResult { IsPass = false, Value = "未执行" };
            int effectiveRetries = step.MaxRetries >= 0 ? step.MaxRetries : Math.Max(0, context.FailRetryCount);
            int attemptsUsed = 0;

            if (!string.IsNullOrWhiteSpace(step.StepId))
            {
                await _grid.ScrollToStepAsync(step.StepId);
                await _grid.SetValueAndStatusByStepIdAsync(
                    context.ChannelIndex,
                    step.StepId,
                    string.Empty,
                    step.AlwaysRun
                        ? StepExecutionStatus.CleanupRunning
                        : StepExecutionStatus.Running);
            }
            else
                await _grid.ScrollToRowAsync(step.RowIndex);

            for (int attempt = 0; attempt <= effectiveRetries; attempt++)
            {
                token.ThrowIfCancellationRequested();
                attemptsUsed = attempt + 1;

                if (attempt > 0)
                {
                    OnLogWarning($"[通道{context.ChannelIndex + 1}] 步骤 '{step.Name}' 第 {attempt} 次自动重试...");
                    if (!string.IsNullOrWhiteSpace(step.StepId))
                    {
                        await _grid.SetValueAndStatusByStepIdAsync(
                            context.ChannelIndex,
                            step.StepId,
                            $"第 {attempt + 1} 次执行",
                            StepExecutionStatus.Retrying);
                    }
                }

                try
                {
                    OnLogInfo($"[通道{context.ChannelIndex + 1}] 开始执行步骤 {currentStep}/{totalSteps}，StepId={step.StepId ?? "LEGACY"}: {step.Name}");
                    finalResult = await step.Action(token);
                    if (finalResult == null)
                        finalResult = new StepResult { IsPass = false, Value = "无结果" };

                    if (finalResult.IsPass)
                        break;
                }
                catch (OperationCanceledException)
                {
                    OnLogWarning($"[通道{context.ChannelIndex + 1}] 步骤 '{step.Name}' 已取消");
                    finalResult = new StepResult
                    {
                        IsPass = false,
                        IsCanceled = true,
                        Value = "已取消"
                    };
                    break;
                }
                catch (Exception ex)
                {
                    OnLogError($"[通道{context.ChannelIndex + 1}] 步骤 '{step.Name}' 异常: {ex.Message}");
                    finalResult = new StepResult { IsPass = false, Value = "异常" };
                }
            }

            stopwatch.Stop();
            finalResult.AttemptCount = attemptsUsed;
            if (!string.IsNullOrWhiteSpace(step.StepId))
            {
                await _grid.SetExecTimeByStepIdAsync(step.StepId, stopwatch.ElapsedMilliseconds);
                StepExecutionStatus status = finalResult.IsCanceled
                    ? StepExecutionStatus.Canceled
                    : finalResult.IsPass
                        ? (attemptsUsed > 1
                            ? StepExecutionStatus.PassedAfterRetry
                            : StepExecutionStatus.Passed)
                        : step.AlwaysRun
                            ? StepExecutionStatus.CleanupFailed
                            : StepExecutionStatus.Failed;
                await _grid.SetValueAndStatusByStepIdAsync(
                    context.ChannelIndex,
                    step.StepId,
                    finalResult.Value,
                    status);
            }
            else
            {
                await _grid.SetExecTimeAsync(step.RowIndex, stopwatch.ElapsedMilliseconds);
                await _grid.SetValueAndResultAsync(
                    context.ChannelIndex,
                    step.RowIndex,
                    finalResult.Value,
                    finalResult.IsPass);
            }

            if (finalResult.IsPass)
                OnLogSuccess($"[通道{context.ChannelIndex + 1}] 步骤 '{step.Name}' 通过 (耗时 {stopwatch.ElapsedMilliseconds} ms)");
            else if (finalResult.IsCanceled)
                OnLogWarning($"[通道{context.ChannelIndex + 1}] 步骤 '{step.Name}' 已取消 (耗时 {stopwatch.ElapsedMilliseconds} ms)");
            else
                OnLogError($"[通道{context.ChannelIndex + 1}] 步骤 '{step.Name}' 失败 (耗时 {stopwatch.ElapsedMilliseconds} ms)");

            return finalResult;
        }

        private async Task ExecuteChapterFailurePolicyAsync(
            TestSequenceContext context,
            SK441Device deviceManager,
            TestStepItem failedStep)
        {
            string groupId = string.Empty;
            if (context?.TestPlan != null && !string.IsNullOrWhiteSpace(failedStep.StepId))
                groupId = context.TestPlan.GetStep(failedStep.StepId).GroupId ?? string.Empty;

            OnLogWarning($"开始执行章节失败处理：{groupId}，失败步骤：{failedStep.Name}");

            await TryFailureActionAsync(
                "关闭全部仪器输出",
                () => deviceManager.TurnOffAllInstrumentsAsync());
            await TryFailureActionAsync(
                "断开DUT主功率继电器",
                () => deviceManager.ControlRelayAsync(
                    ExpectedBoardType.StartsWith("MPS", StringComparison.OrdinalIgnoreCase) ? 1 : 7,
                    false));

            if (groupId.EndsWith(".CH09", StringComparison.OrdinalIgnoreCase))
            {
                await TryFailureActionAsync(
                    "发送W86=0释放RESET",
                    () => deviceManager.SendMbdCommandAsync("W86=0", 0));
                await TryFailureActionAsync(
                    "断开RLP4/RLP8",
                    () => deviceManager.SetDaqRelaysAsync(
                        "@305,@306",
                        false,
                        "编程失败复位RLP4/RLP8"));
                await TryFailureActionAsync(
                    "断开编程继电器",
                    () => deviceManager.SetFixtureRelaysAsync(
                        new[] { 3, 4, 5, 6, 7, 8 },
                        false,
                        "编程失败复位"));
            }

            if (groupId.EndsWith(".CH12", StringComparison.OrdinalIgnoreCase))
            {
                await TryFailureActionAsync(
                    "发送EXIT",
                    () => deviceManager.SendMbdCommandAsync("EXIT", 0));
                await TryFailureActionAsync(
                    "发送W86=0",
                    () => deviceManager.SendMbdCommandAsync("W86=0", 0));
            }

            if (groupId.EndsWith(".CH16", StringComparison.OrdinalIgnoreCase))
            {
                await TryFailureActionAsync(
                    "发送EXIT",
                    () => deviceManager.SendMbdCommandAsync("EXIT", 0));
                await TryFailureActionAsync(
                    "发送DIAG ON",
                    () => deviceManager.SendMbdCommandAsync("DIAG ON", 0));
                await TryFailureActionAsync(
                    "发送W91=0",
                    () => deviceManager.SendMbdCommandAsync("W91=0", 0));
                await TryFailureActionAsync(
                    "发送W94=0",
                    () => deviceManager.SendMbdCommandAsync("W94=0", 0));
            }

            if (groupId.EndsWith(".CH14", StringComparison.OrdinalIgnoreCase) ||
                groupId.EndsWith(".CH17", StringComparison.OrdinalIgnoreCase) ||
                groupId.EndsWith(".CH20", StringComparison.OrdinalIgnoreCase) ||
                groupId.EndsWith(".CH21", StringComparison.OrdinalIgnoreCase) ||
                groupId.EndsWith(".CH22", StringComparison.OrdinalIgnoreCase) ||
                groupId.EndsWith(".CH24", StringComparison.OrdinalIgnoreCase))
            {
                await TryFailureActionAsync(
                    "发送EXIT",
                    () => deviceManager.SendMbdCommandAsync("EXIT", 0));
            }
        }

        private async Task TryFailureActionAsync(string actionName, Func<Task<bool>> action)
        {
            try
            {
                bool passed = await action();
                if (passed)
                    OnLogSuccess($"章节失败处理通过：{actionName}");
                else
                    OnLogError($"章节失败处理失败：{actionName}；继续后续安全动作。");
            }
            catch (Exception ex)
            {
                OnLogError($"章节失败处理异常：{actionName}，{ex.Message}；继续后续安全动作。");
            }
        }

        private static void BindStableStepIds(
            TestSequenceContext context,
            System.Collections.Generic.IList<TestStepItem> steps)
        {
            if (context?.TestPlan == null)
                return;

            var definitions = context.TestPlan.Definition.Steps
                .Where(x => !string.Equals(
                    x.RunCondition,
                    "OnChapterFailure",
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.SequenceOrder)
                .ToList();

            if (steps.Count != definitions.Count)
            {
                throw new TestPlanConfigurationException(
                    $"Sequence step count ({steps.Count}) does not match test plan step count ({definitions.Count}).");
            }

            var definitionsById = definitions.ToDictionary(
                x => x.StepId,
                StringComparer.OrdinalIgnoreCase);
            var duplicateIds = steps
                .Where(x => !string.IsNullOrWhiteSpace(x.StepId))
                .GroupBy(x => x.StepId, StringComparer.OrdinalIgnoreCase)
                .Where(x => x.Count() > 1)
                .Select(x => x.Key)
                .ToArray();
            if (duplicateIds.Length > 0)
                throw new TestPlanConfigurationException(
                    $"Sequence contains duplicate StepId: {string.Join(", ", duplicateIds)}");

            for (int index = 0; index < steps.Count; index++)
            {
                TestStepItem step = steps[index];
                if (string.IsNullOrWhiteSpace(step.StepId))
                    throw new TestPlanConfigurationException(
                        $"Sequence step does not declare StepId at creation: {step.Name}");

                TestPlanStepDefinition definition;
                if (!definitionsById.TryGetValue(step.StepId, out definition))
                    throw new TestPlanConfigurationException(
                        $"Sequence StepId is not present in XML: {step.StepId}");

                if (!string.IsNullOrWhiteSpace(step.ExpectedGroupId) &&
                    !string.Equals(
                        step.ExpectedGroupId,
                        definition.GroupId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new TestPlanConfigurationException(
                        $"StepId {step.StepId} group mismatch: code={step.ExpectedGroupId}, XML={definition.GroupId}.");
                }

                step.PlanDefinition = definition;
                step.RowIndex = -1;
                step.AlwaysRun = definition.AlwaysRun;
                if (step.AlwaysRun)
                    step.MaxRetries = 0;
            }
        }

        private void StoreStepResult(TestSequenceContext context, StepResult result)
        {
            if (context == null || result == null || string.IsNullOrWhiteSpace(result.StoreKey))
                return;

            double numericValue = result.NumericValue.HasValue ? result.NumericValue.Value : 0.0;
            context.StoreValue(result.StoreKey, numericValue, result.Value, result.Unit, result.IsPass);
            OnLogInfo($"已保存测试变量 {result.StoreKey} = {result.Value}");
        }

        protected bool TryGetStoredNumericValue(TestSequenceContext context, string key, out double value)
        {
            if (context != null && context.TryGetNumericValue(key, out value))
                return true;

            value = 0.0;
            OnLogError($"未找到前序测试变量: {key}");
            return false;
        }

        private async Task<bool> RunPendingAlwaysRunStepsAsync(
            TestSequenceContext context,
            SK441Device deviceManager,
            System.Collections.Generic.IList<TestStepItem> steps,
            System.Collections.Generic.ISet<string> executedStepIds)
        {
            bool allCleanupPassed = true;
            foreach (TestStepItem step in steps.Where(x => x.AlwaysRun))
            {
                if (!string.IsNullOrWhiteSpace(step.StepId) &&
                    executedStepIds.Contains(step.StepId))
                {
                    continue;
                }

                StepResult result = await RunStepAsync(
                    context,
                    deviceManager,
                    step,
                    steps.IndexOf(step) + 1,
                    steps.Count,
                    CancellationToken.None);
                StoreStepResult(context, result);
                allCleanupPassed = allCleanupPassed && result.IsPass;

                if (!string.IsNullOrWhiteSpace(step.StepId))
                    executedStepIds.Add(step.StepId);
            }

            return allCleanupPassed;
        }

        private void ResetCleanupState()
        {
            lock (_fixtureReleaseSync)
            {
                _fixtureReleaseCommandTask = null;
                _fixtureReleaseVerificationTask = null;
                _rl4OffTask = null;
                _allInstrumentsOffTask = null;
                _ansPowerOffTask = null;
            }
        }

        protected bool HasFixtureReleaseStarted
        {
            get
            {
                lock (_fixtureReleaseSync)
                    return _fixtureReleaseCommandTask != null;
            }
        }

        protected Task<bool> EnsureRl4OffAsync(SK441Device deviceManager)
        {
            lock (_fixtureReleaseSync)
            {
                if (_rl4OffTask == null)
                {
                    int dutPowerRelay = ExpectedBoardType.StartsWith(
                        "MPS",
                        StringComparison.OrdinalIgnoreCase) ? 1 : 7;
                    _rl4OffTask = deviceManager.ControlRelayAsync(dutPowerRelay, false);
                }
                return _rl4OffTask;
            }
        }

        private Task<bool> EnsureAllInstrumentsOffAsync(SK441Device deviceManager)
        {
            lock (_fixtureReleaseSync)
            {
                if (_allInstrumentsOffTask == null)
                {
                    _allInstrumentsOffTask = deviceManager.TurnOffAllInstrumentsAsync();
                    _ansPowerOffTask = _allInstrumentsOffTask;
                }
                return _allInstrumentsOffTask;
            }
        }

        private Task<bool> EnsureAnsPowerOffAsync(SK441Device deviceManager)
        {
            lock (_fixtureReleaseSync)
            {
                if (_ansPowerOffTask == null)
                {
                    _ansPowerOffTask =
                        deviceManager.SetAnsVoltageCurrentOutputAsync(0, 0, false);
                }
                return _ansPowerOffTask;
            }
        }

        private Task<bool> EnsureFixtureReleaseCommandAsync(SK441Device deviceManager)
        {
            lock (_fixtureReleaseSync)
            {
                if (_fixtureReleaseCommandTask == null)
                {
                    OnLogInfo("首次执行治具释放命令；本次测试后续释放请求将复用该结果。");
                    _fixtureReleaseCommandTask =
                        deviceManager.StopFixturePressDownAsync(ExpectedBoardType);
                }
                else
                {
                    OnLogInfo("治具释放命令本次测试已执行，复用首次执行结果。");
                }

                return _fixtureReleaseCommandTask;
            }
        }

        private Task<bool> EnsureFixtureReleaseVerifiedAsync(SK441Device deviceManager)
        {
            lock (_fixtureReleaseSync)
            {
                if (_fixtureReleaseVerificationTask == null)
                    _fixtureReleaseVerificationTask = VerifyFixtureReleasedAsync(deviceManager);

                return _fixtureReleaseVerificationTask;
            }
        }

        private async Task<bool> VerifyFixtureReleasedAsync(SK441Device deviceManager)
        {
            if (!await EnsureFixtureReleaseCommandAsync(deviceManager))
                return false;

            return await VerifyFixtureInputReleasedAsync(deviceManager);
        }

        protected async Task<bool> VerifyFixtureInputReleasedAsync(SK441Device deviceManager)
        {
            if (deviceManager.SkipComInit)
            {
                await Task.Delay(50);
                return true;
            }

            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                bool[] states = await deviceManager.ReadDigitalInputsAsync();
                if (states != null && states.Length > 6 && states[6])
                    return true;

                await Task.Delay(300);
            }

            return false;
        }

        private async Task HandleFixtureStopAsync(SK441Device deviceManager, string reason, bool urgent)
        {
            LogWarning?.Invoke("==============================================");
            LogWarning?.Invoke($"{(urgent ? "检测到安全中断" : "测试流程收尾")}：{reason}");
            LogWarning?.Invoke("正在尝试释放/上升下压治具，请现场确认治具已回到安全位置。");
            LogWarning?.Invoke("如治具未自动上升，请按设备说明手动复位，确认板子无夹斜后再继续。");
            LogWarning?.Invoke("==============================================");

            bool commandOk = await EnsureFixtureReleaseCommandAsync(deviceManager);
            bool released = await EnsureFixtureReleaseVerifiedAsync(deviceManager);
            if (commandOk && released)
            {
                LogWarning?.Invoke("治具释放命令成功，数字量7已确认离开下压到位状态。");
            }
            else
            {
                LogError?.Invoke(
                    "治具释放或行程确认失败，请立即检查急停、气源/电磁阀和治具位置。");
            }
        }

        private async Task SafeShutdownAsync(SK441Device deviceManager, string reason, bool urgent)
        {
            OnLogWarning("==============================================");
            OnLogWarning($"正在执行 SK 安全收尾：{reason}");
            OnLogWarning("将先关闭全部仪器输出，再断开DUT主功率继电器，最后释放/上升治具。");
            OnLogWarning("==============================================");

            if (deviceManager.IsEmergencyStopLatched)
            {
                await deviceManager.EmergencyStopOutputsAsync(
                    ExpectedBoardType,
                    deviceManager.EmergencyStopReason ?? reason);
                OnLogWarning("急停治具上升由硬件安全回路完成；软件已关闭测试输出并清除下压命令。");
                return;
            }

            bool outputsOff = false;
            bool powerRelayOff = false;
            try
            {
                outputsOff = await EnsureAllInstrumentsOffAsync(deviceManager);
            }
            catch (Exception ex)
            {
                OnLogError($"关闭仪器输出失败: {ex.Message}");
            }

            try
            {
                powerRelayOff = outputsOff && await EnsureRl4OffAsync(deviceManager);
            }
            catch (Exception ex)
            {
                OnLogError($"断开DUT主功率继电器失败: {ex.Message}");
            }

            if (!outputsOff || !powerRelayOff)
            {
                OnLogError("安全收尾未确认全部输出关闭和主功率继电器断开，治具保持锁定，请人工排查。");
                return;
            }

            await HandleFixtureStopAsync(deviceManager, reason, urgent);
        }

        protected System.Collections.Generic.List<TestStepItem> BuildCommonSteps(TestSequenceContext context, SK441Device deviceManager)
        {
            var steps = new System.Collections.Generic.List<TestStepItem>();

            steps.Add(new TestStepItem
            {
                Name = "镭雕扫描获取SN",
                RowIndex = steps.Count,
                Action = async (token) =>
                {
                    await Task.Delay(100, token);
                    return new StepResult { IsPass = !string.IsNullOrEmpty(context.SN), Value = context.SN };
                }
            });

            steps.Add(new TestStepItem
            {
                Name = "急停状态检查（数字量8）",
                RowIndex = steps.Count,
                SafetyCritical = true,
                Action = async (token) =>
                {
                    bool[] states = await deviceManager.ReadDigitalInputsAsync();
                    bool isEstopPressed = deviceManager.IsEmergencyStopTriggered(states[7]);
                    if (isEstopPressed)
                    {
                        LogError?.Invoke("检测到急停开关被按下，请先复位急停开关后再启动测试。");
                        return new StepResult { IsPass = false, Value = "急停未复位" };
                    }

                    return new StepResult { IsPass = true, Value = "正常" };
                }
            });

            steps.Add(new TestStepItem
            {
                Name = "接口A连接检查（数字量1）",
                RowIndex = steps.Count,
                SafetyCritical = true,
                Action = async (token) =>
                {
                    bool[] states = await deviceManager.ReadDigitalInputsAsync();
                    bool isConnected = !states[0]; // 数字量1: 0=连接, 1=未连接
                    return new StepResult { IsPass = isConnected, Value = isConnected ? "已连接" : "未连接" };
                }
            });

            steps.Add(new TestStepItem
            {
                Name = "接口B连接检查（数字量2）",
                RowIndex = steps.Count,
                SafetyCritical = true,
                Action = async (token) =>
                {
                    bool[] states = await deviceManager.ReadDigitalInputsAsync();
                    bool isConnected = !states[1]; // 数字量2: 0=连接, 1=未连接
                    return new StepResult { IsPass = isConnected, Value = isConnected ? "已连接" : "未连接" };
                }
            });

            steps.Add(new TestStepItem
            {
                Name = "接口C连接检查（数字量3）",
                RowIndex = steps.Count,
                SafetyCritical = true,
                Action = async (token) =>
                {
                    bool[] states = await deviceManager.ReadDigitalInputsAsync();
                    bool isConnected = !states[2]; // 数字量3: 0=连接, 1=未连接
                    return new StepResult { IsPass = isConnected, Value = isConnected ? "已连接" : "未连接" };
                }
            });

            steps.Add(new TestStepItem
            {
                Name = "测试板安装检查（闭合318/319，数字量4）",
                RowIndex = steps.Count,
                SafetyCritical = true,
                Action = async (token) =>
                {
                    bool relayReady = await deviceManager.ClosePlacementDetectionRelaysAsync();
                    if (!relayReady)
                        return new StepResult { IsPass = false, Value = "继电器318/319失败" };

                    bool[] states = await deviceManager.ReadDigitalInputsAsync();
                    bool isInstalled = !states[3]; // 数字量4/P2: 0=安装正确, 1=未安装
                    return new StepResult { IsPass = isInstalled, Value = isInstalled ? "安装正确" : "未安装" };
                }
            });

            steps.Add(new TestStepItem
            {
                Name = "测试板型号识别（数字量5/6）",
                RowIndex = steps.Count,
                SafetyCritical = true,
                Action = async (token) =>
                {
                    bool[] states = await deviceManager.ReadDigitalInputsAsync();
                    bool isMps = states[4]; // 数字量5/P1: 1=MPS 0=BCM
                    bool is250 = states[5]; // 数字量6/P0: 1=250 0=125
                    
                    string boardType = $"{(isMps ? "MPS" : "BCM")}-{(is250 ? "250" : "125")}";
                    LogInfo?.Invoke($"识别到测试板型号: {boardType}");

                    if (ExpectedBoardType != "ANY" && boardType != ExpectedBoardType)
                    {
                        LogError?.Invoke($"【防呆报错】您选择了 {ExpectedBoardType} 测试项目，但放入的板型为 {boardType}！");
                        return new StepResult { IsPass = false, Value = $"错误: {boardType}" };
                    }

                    return new StepResult { IsPass = true, Value = boardType };
                }
            });

            steps.Add(new TestStepItem
            {
                Name = "下压许可检查并等待到位（数字量4/7）",
                RowIndex = steps.Count,
                SafetyCritical = true,
                Action = async (token) =>
                {
                    LogInfo?.Invoke("正在检查急停、板子安装和下压行程开关状态...");
                    bool[] initialStates = await deviceManager.ReadDigitalInputsAsync();
                    if (deviceManager.IsEmergencyStopTriggered(initialStates[7]))
                    {
                        LogError?.Invoke("急停触发，禁止下压！");
                        return new StepResult { IsPass = false, Value = "急停触发" };
                    }

                    bool initialInstalled = !initialStates[3]; // 数字量4/P2: 0=安装正确
                    bool initialTravelClosed = !initialStates[6]; // 数字量7: 0=闭合(下压到位), 1=未闭合

                    if (!initialInstalled)
                    {
                        LogError?.Invoke("数字量4未检测到板子安装信号，禁止下压，请重新放置板子。");
                        return new StepResult { IsPass = false, Value = "禁止下压: 未安装" };
                    }

                    if (initialTravelClosed)
                    {
                        LogError?.Invoke("数字量7已检测到行程开关闭合，治具已处于下压到位状态，禁止重复下压。");
                        return new StepResult { IsPass = false, Value = "禁止下压: 已到位" };
                    }

                    bool confirmed = await ConfirmFixturePressDownAsync(
                        "下压前安全检查已通过：\n\n" +
                        "✓ 急停未触发\n" +
                        "✓ 被测板安装正确\n" +
                        "✓ 治具尚未下压到位\n\n" +
                        "请确认治具区域内无手、工具或其他异物，然后点击“确认下压”。");
                    if (!confirmed)
                    {
                        LogWarning?.Invoke("操作员取消下压，未闭合下压许可继电器。");
                        return new StepResult { IsPass = false, Value = "操作员取消下压" };
                    }

                    token.ThrowIfCancellationRequested();

                    // 操作员确认期间现场状态可能发生变化，闭合许可继电器前必须再次读取。
                    bool[] confirmedStates = await deviceManager.ReadDigitalInputsAsync();
                    bool emergencyAfterConfirm =
                        deviceManager.IsEmergencyStopTriggered(confirmedStates[7]);
                    bool installedAfterConfirm = !confirmedStates[3];
                    bool travelClosedAfterConfirm = !confirmedStates[6];
                    if (emergencyAfterConfirm || !installedAfterConfirm || travelClosedAfterConfirm)
                    {
                        string changedState = emergencyAfterConfirm
                            ? "急停触发"
                            : !installedAfterConfirm
                                ? "板子安装信号丢失"
                                : "治具已处于下压到位状态";
                        LogError?.Invoke($"确认后安全状态发生变化（{changedState}），未开放下压许可。");
                        return new StepResult { IsPass = false, Value = $"禁止下压: {changedState}" };
                    }

                    bool fixtureEnabled = await deviceManager.EnableFixturePressDownAsync(ExpectedBoardType);
                    if (!fixtureEnabled)
                    {
                        LogError?.Invoke("工装下压允许继电器闭合失败，禁止下压。");
                        return new StepResult { IsPass = false, Value = "下压允许失败" };
                    }

                    ConfirmDisplayWindow pressNotice = await ShowNoticeAsync(
                        "已确认下压，下压许可回路已开放。\n\n" +
                        "请双手按下绿色启动按钮；如需终止，请点击“取消下压”。",
                        allowCancel: true);
                    DateTime pressNoticeShownAt = DateTime.Now;
                    double noticeMinimumSeconds = deviceManager.GetFixtureNoticeMinimumSeconds();
                    DateTime fixtureDownDeadline = DateTime.UtcNow.AddMilliseconds(
                        deviceManager.GetFixtureDownTimeoutMilliseconds());
                    bool? lastTravelClosed = null;

                    LogInfo?.Invoke("==============================================");
                    LogInfo?.Invoke("板子安装 OK，治具未下压到位，下压回路已允许。请双手按下绿色启动按钮...");
                    LogInfo?.Invoke("==============================================");

                    while (!token.IsCancellationRequested)
                    {
                        if (pressNotice != null && pressNotice.WasCancelled)
                        {
                            LogWarning?.Invoke("操作员在等待下压期间取消操作，正在断开下压许可继电器。");
                            bool released = await deviceManager.StopFixturePressDownAsync(ExpectedBoardType);
                            await CloseNoticeAsync(
                                pressNotice,
                                pressNoticeShownAt,
                                noticeMinimumSeconds,
                                CancellationToken.None);
                            return new StepResult
                            {
                                IsPass = false,
                                Value = released ? "操作员取消下压" : "取消下压，继电器释放失败"
                            };
                        }

                        bool[] states = await deviceManager.ReadDigitalInputsAsync();
                        
                        // 全局安全检查：在等待期间也应监控急停
                        if (deviceManager.IsEmergencyStopTriggered(states[7]))
                        {
                            LogError?.Invoke("下压等待期间触发急停！");
                            await CloseNoticeAsync(pressNotice, pressNoticeShownAt, noticeMinimumSeconds, token);
                            return new StepResult { IsPass = false, Value = "急停触发" };
                        }

                        bool isInstalled = !states[3]; // 数字量4/P2: 0=安装正确
                        bool isTravelClosed = !states[6]; // 数字量7: 0=闭合(下压到位), 1=未闭合

                        if (!lastTravelClosed.HasValue || lastTravelClosed.Value != isTravelClosed)
                        {
                            LogInfo?.Invoke(
                                $"下压行程DI7判定变化：{(isTravelClosed ? "已闭合/到位" : "未闭合/等待")}" +
                                $"，归一化值={(states[6] ? 1 : 0)}");
                            lastTravelClosed = isTravelClosed;
                        }

                        if (!isInstalled)
                        {
                            LogError?.Invoke("等待下压期间数字量4安装信号丢失，禁止继续下压。");
                            await CloseNoticeAsync(pressNotice, pressNoticeShownAt, noticeMinimumSeconds, token);
                            return new StepResult { IsPass = false, Value = "安装信号丢失" };
                        }

                        if (isTravelClosed)
                        {
                            break;
                        }

                        if (DateTime.UtcNow >= fixtureDownDeadline)
                        {
                            LogError?.Invoke("等待下压到位超时：DI7在30秒内未切换到闭合状态。");
                            bool released = await deviceManager.StopFixturePressDownAsync(ExpectedBoardType);
                            await CloseNoticeAsync(
                                pressNotice,
                                pressNoticeShownAt,
                                noticeMinimumSeconds,
                                CancellationToken.None);
                            return new StepResult
                            {
                                IsPass = false,
                                Value = released ? "下压到位超时" : "下压到位超时且释放命令失败"
                            };
                        }
                        
                        // 循环读取的间隔时间，避免CPU空转
                        await Task.Delay(300, token);
                    }

                    if (token.IsCancellationRequested)
                    {
                        await CloseNoticeAsync(pressNotice, pressNoticeShownAt, noticeMinimumSeconds, CancellationToken.None);
                        return new StepResult { IsPass = false, Value = "已取消" };
                    }

                    await CloseNoticeAsync(pressNotice, pressNoticeShownAt, noticeMinimumSeconds, token);
                    LogInfo?.Invoke(">>> 治具下压到位 (行程开关闭合)，开始执行后续电气测试。");

                    return new StepResult { IsPass = true, Value = "下压完成" };
                }
            });

            return steps;
        }

    }

}
