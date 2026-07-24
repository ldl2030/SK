using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;

namespace TestPlatform.TestSequences
{
    public abstract class SKBaseSequence : ITestSequence
    {
        public abstract string SequenceKey { get; }
        
        // 期望放上的板子类型，由子类指定，用于产线防呆
        public abstract string ExpectedBoardType { get; }

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

        protected async Task<bool> ConfirmAsync(string message)
        {
            if (_confirmAsync == null)
            {
                OnLogWarning(message);
                return true;
            }

            return await _confirmAsync(message);
        }

        protected async Task<ConfirmDisplayWindow> ShowNoticeAsync(string message, bool allowCancel = false)
        {
            if (Application.Current == null)
            {
                OnLogWarning(message);
                return null;
            }

            return await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var dialog = new ConfirmDisplayWindow(message)
                {
                    Owner = Application.Current.MainWindow
                };
                dialog.SetNoticeMode("操作提示", allowCancel);
                dialog.Show();
                return dialog;
            });
        }

        protected async Task<bool> ConfirmFixturePressDownAsync(string message)
        {
            if (Application.Current == null)
                return await ConfirmAsync(message);

            return await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var dialog = new ConfirmDisplayWindow(message)
                {
                    Owner = Application.Current.MainWindow
                };
                dialog.SetButtonLabels("确认下压", "取消");
                return dialog.ShowDialog() == true;
            });
        }

        protected async Task CloseNoticeAsync(ConfirmDisplayWindow dialog, DateTime shownAt, double minimumSeconds, CancellationToken token)
        {
            TimeSpan minimum = TimeSpan.FromSeconds(Math.Max(0, minimumSeconds));
            TimeSpan elapsed = DateTime.Now - shownAt;
            if (elapsed < minimum)
            {
                await Task.Delay(minimum - elapsed, token);
            }

            await CloseNoticeAsync(dialog);
        }

        protected async Task CloseNoticeAsync(ConfirmDisplayWindow dialog)
        {
            if (dialog == null || Application.Current == null)
                return;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (dialog.IsVisible)
                    dialog.CloseNotice();
            });
        }

        protected async Task UpdateNoticeAsync(ConfirmDisplayWindow dialog, string message)
        {
            if (dialog == null || Application.Current == null)
            {
                OnLogWarning(message);
                return;
            }

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (dialog.IsVisible)
                    dialog.UpdateMessage(message);
            });
        }

        // 子类必须实现这个方法，把后续的电气测试项 Add 到 steps 里
        protected abstract void AddElectricalTestSteps(System.Collections.Generic.List<TestStepItem> steps, TestSequenceContext context, SK441Device deviceManager);

        protected class TestStepItem
        {
            public string Name { get; set; }
            public string StepId { get; set; }
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
            string ansPort = System.Configuration.ConfigurationManager.AppSettings["AnsPort"] ?? ComName.powerSupplyComName ?? "COM1";
            string henghui1Port = System.Configuration.ConfigurationManager.AppSettings["HengHui1Port"] ?? "COM2";
            string henghui2Port = System.Configuration.ConfigurationManager.AppSettings["HengHui2Port"] ?? "COM3";
            string loadPort = System.Configuration.ConfigurationManager.AppSettings["LoadPort"] ?? "COM4";
            string daqPort = System.Configuration.ConfigurationManager.AppSettings["DaqPort"] ?? "COM5";
            string targetBoardPort = System.Configuration.ConfigurationManager.AppSettings["TargetBoardPort"] ?? ComName.testComName ?? "COM6";
            string ttlPort = System.Configuration.ConfigurationManager.AppSettings["TtlPort"] ?? ComName.uartComName ?? "COM7";

            using (var deviceManager = new SK441Device())
            {
                deviceManager.LogInfo = msg => LogInfo?.Invoke(msg);
                deviceManager.LogError = msg => LogError?.Invoke(msg);

                try
                {
                    LogInfo?.Invoke("尝试打开物理串口硬件...");
                    bool initSuccess = await deviceManager.InitializeAllDevicesAsync(
                        ansPort, henghui1Port, henghui2Port, loadPort, daqPort, targetBoardPort, ttlPort);

                    if (!initSuccess)
                    {
                        LogError?.Invoke(">> 基础串口连接失败！测试中止。");
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
                        bool isSafetyMonitorRunning = true;
                        
                        // 启动一个后台全局安全监控线程
                        var safetyTask = Task.Run(async () =>
                        {
                            try
                            {
                                while (isSafetyMonitorRunning && !safetyCts.Token.IsCancellationRequested)
                                {
                                    // 延迟 300ms 轮询
                                    await Task.Delay(300, safetyCts.Token);
                                    if (safetyCts.Token.IsCancellationRequested) break;
                                    
                                    bool[] states = await deviceManager.ReadDigitalInputsAsync(1);
                                    
                                    // states[7] 是急停信号，为 true (1) 表示急停触发
                                    if (states[7]) 
                                    {
                                        LogError?.Invoke("【系统全局报警】检测到急停开关被按下！立刻强制熔断所有正在执行的测试！");
                                        safetyCts.Cancel();
                                        break;
                                    }
                                }
                            }
                            catch (OperationCanceledException) { /* 忽略监控器由于正常取消导致的异常 */ }
                            catch (Exception) { /* 忽略其他通信异常以防监控器自身崩溃 */ }
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
                                        bool terminalFailure =
                                            step.SafetyCritical ||
                                            step.PowerCritical ||
                                            context.StopOnFail;
                                        if (terminalFailure)
                                        {
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
                                    }
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
                            // 测试主循环结束后，通知后台监控线程退出
                            isSafetyMonitorRunning = false;
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
                    await HandleFixtureStopAsync(deviceManager, "用户取消或急停打断", true);
                    return false;
                }
                catch (Exception ex)
                {
                    LogError?.Invoke($"SK441 测试发生异常: {ex.Message}");
                    await HandleFixtureStopAsync(deviceManager, "测试异常", true);
                    return false;
                }
            }
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
                "断开RL4",
                () => deviceManager.ControlRelayAsync(7, false));

            if (groupId.EndsWith(".CH09", StringComparison.OrdinalIgnoreCase))
            {
                await TryFailureActionAsync(
                    "发送W86=0释放RESET",
                    () => deviceManager.SendMbdCommandAsync("W86=0", 0));
                await TryFailureActionAsync(
                    "断开RLP4/RLP8",
                    () => deviceManager.SetDaqRelaysAsync(
                        "@205,@206",
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

            for (int index = 0; index < steps.Count; index++)
            {
                steps[index].StepId = definitions[index].StepId;
                steps[index].RowIndex = -1;
                steps[index].AlwaysRun = definitions[index].AlwaysRun;
                if (steps[index].AlwaysRun)
                    steps[index].MaxRetries = 0;
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

        private Task<bool> EnsureRl4OffAsync(SK441Device deviceManager)
        {
            lock (_fixtureReleaseSync)
            {
                if (_rl4OffTask == null)
                    _rl4OffTask = deviceManager.ControlRelayAsync(7, false);
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

        private async Task<bool> VerifyFixtureInputReleasedAsync(SK441Device deviceManager)
        {
            if (deviceManager.SkipComInit)
            {
                await Task.Delay(50);
                return true;
            }

            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                bool[] states = await deviceManager.ReadDigitalInputsAsync(1);
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
            OnLogWarning("将先断开 RL4/关闭仪器输出，再释放/上升治具。");
            OnLogWarning("==============================================");

            try
            {
                await EnsureRl4OffAsync(deviceManager);
            }
            catch (Exception ex)
            {
                OnLogError($"断开 RL4 失败: {ex.Message}");
            }

            try
            {
                await EnsureAllInstrumentsOffAsync(deviceManager);
            }
            catch (Exception ex)
            {
                OnLogError($"关闭仪器输出失败: {ex.Message}");
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
                    bool[] states = await deviceManager.ReadDigitalInputsAsync(1);
                    bool isEstopPressed = states[7]; // 数字量8: 1=急停触发, 0=正常
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
                    bool[] states = await deviceManager.ReadDigitalInputsAsync(1);
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
                    bool[] states = await deviceManager.ReadDigitalInputsAsync(1);
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
                    bool[] states = await deviceManager.ReadDigitalInputsAsync(1);
                    bool isConnected = !states[2]; // 数字量3: 0=连接, 1=未连接
                    return new StepResult { IsPass = isConnected, Value = isConnected ? "已连接" : "未连接" };
                }
            });

            steps.Add(new TestStepItem
            {
                Name = "测试板安装检查（闭合218/219，数字量4）",
                RowIndex = steps.Count,
                SafetyCritical = true,
                Action = async (token) =>
                {
                    bool relayReady = await deviceManager.ClosePlacementDetectionRelaysAsync();
                    if (!relayReady)
                        return new StepResult { IsPass = false, Value = "继电器218/219失败" };

                    bool[] states = await deviceManager.ReadDigitalInputsAsync(1);
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
                    bool[] states = await deviceManager.ReadDigitalInputsAsync(1);
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
                    bool[] initialStates = await deviceManager.ReadDigitalInputsAsync(1);
                    if (initialStates[7])
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
                    bool[] confirmedStates = await deviceManager.ReadDigitalInputsAsync(1);
                    bool emergencyAfterConfirm = confirmedStates[7];
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

                        bool[] states = await deviceManager.ReadDigitalInputsAsync(1);
                        
                        // 全局安全检查：在等待期间也应监控急停
                        if (states[7]) 
                        {
                            LogError?.Invoke("下压等待期间触发急停！");
                            await CloseNoticeAsync(pressNotice, pressNoticeShownAt, noticeMinimumSeconds, token);
                            return new StepResult { IsPass = false, Value = "急停触发" };
                        }

                        bool isInstalled = !states[3]; // 数字量4/P2: 0=安装正确
                        bool isTravelClosed = !states[6]; // 数字量7: 0=闭合(下压到位), 1=未闭合

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

        protected void AddInitializingToolsStep(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager, string boardName)
        {
            // PDF Chapter 7: Initializing tools
            steps.Add(new TestStepItem
            {
                Name = "7.1 仪器在线检查",
                RowIndex = steps.Count,
                Action = async (token) =>
                {
                    OnLogInfo($"正在执行 {boardName} 仪器在线检查 (Chapter 7.1)...");
                    bool instrumentsReady = await deviceManager.CheckRequiredInstrumentsAsync();
                    return new StepResult { IsPass = instrumentsReady, Value = instrumentsReady ? "Ready" : "Check Fail" };
                }
            });

            steps.Add(new TestStepItem
            {
                Name = "7.2 关闭所有仪器输出",
                RowIndex = steps.Count,
                Action = async (token) =>
                {
                    OnLogInfo($"正在执行 {boardName} 关闭所有仪器输出 (Chapter 7.2)...");
                    bool offRes = await deviceManager.TurnOffAllInstrumentsAsync();
                    return new StepResult { IsPass = offRes, Value = offRes ? "Off OK" : "Off Fail" };
                }
            });

            steps.Add(new TestStepItem
            {
                Name = "7.3 断开测试继电器（保持Y1/Y2）",
                RowIndex = steps.Count,
                Action = async (token) =>
                {
                    OnLogInfo($"正在执行 {boardName} 测试继电器复位 (Chapter 7.3)...");
                    OnLogInfo("注意：此步骤只断开测试/测量继电器，不释放工装下压允许继电器 Y1/Y2。");

                    bool placementRelayRes = await deviceManager.OpenPlacementDetectionRelaysAsync();
                    bool relayRes = await deviceManager.OpenAllRelaysAsync();
                    if (!placementRelayRes || !relayRes)
                    {
                        OnLogError("测试继电器或万用表继电器复位失败！");
                        return new StepResult { IsPass = false, Value = "Relay Fail" };
                    }

                    return new StepResult { IsPass = true, Value = "Relay Open OK" };
                }
            });
        }

        protected void AddBcm125FirstStartupSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager)
        {
            steps.Add(new TestStepItem
            {
                Name = "8.1 FIRST START-UP-设置并开启MPS板电源",
                RowIndex = steps.Count,
                PowerCritical = true,
                Action = async (token) =>
                {
                    bool ok = await deviceManager.SetAnsVoltageCurrentOutputAsync(140.0f, 1.0f, true);
                    return new StepResult { IsPass = ok, Value = ok ? "140V/1A ON" : "Power Fail" };
                }
            });

            steps.Add(new TestStepItem
            {
                Name = "8.2 FIRST START-UP-闭合RL4给DUT供电",
                RowIndex = steps.Count,
                PowerCritical = true,
                Action = async (token) =>
                {
                    bool ok = await deviceManager.ControlRelayAsync(7, true);
                    return new StepResult { IsPass = ok, Value = ok ? "RL4 ON" : "RL4 Fail" };
                }
            });

            AddBcm125DaqVoltageStep(steps, deviceManager, "B01 +5V", "BCM125.FIRST_STARTUP.B01_5V", 0, 101, 4.750, 5.300, "V");
            AddBcm125DaqVoltageStep(steps, deviceManager, "B02 +VCC", "BCM125.FIRST_STARTUP.B02_VCC", 1, 102, 14.690, 15.570, "V");
            AddBcm125DaqVoltageStep(steps, deviceManager, "B03 -VEE1", "BCM125.FIRST_STARTUP.B03_VEE1", 2, 103, -13.890, -13.610, "V");
            AddBcm125DaqVoltageStep(steps, deviceManager, "B04 -VEE2", "BCM125.FIRST_STARTUP.B04_VEE2", 3, 104, -13.890, -13.610, "V");
            AddBcm125DaqVoltageStep(steps, deviceManager, "B05 3V3A-1", "BCM125.FIRST_STARTUP.B05_3V3A_1", 4, 107, 3.200, 3.400, "V");
            AddBcm125DaqVoltageStep(steps, deviceManager, "B06 3V3A-2", "BCM125.FIRST_STARTUP.B06_3V3A_2", 5, 108, 3.200, 3.400, "V");
            AddBcm125DaqVoltageStep(steps, deviceManager, "B07 3V3-1", "BCM125.FIRST_STARTUP.B07_3V3_1", 6, 111, 3.200, 3.400, "V");
            AddBcm125DaqVoltageStep(steps, deviceManager, "B08 3V3-2", "BCM125.FIRST_STARTUP.B08_3V3_2", 7, 112, 3.200, 3.400, "V");
            AddBcm125DaqCurrentFromShuntStep(steps, deviceManager, "B09 +VCC_1电流", "BCM125.FIRST_STARTUP.B09_VCC_1_CURRENT", 8, 119, 2.2, 30.0, 90.0);
            AddBcm125DaqCurrentFromShuntStep(steps, deviceManager, "B10 +VCC_2电流", "BCM125.FIRST_STARTUP.B10_VCC_2_CURRENT", 9, 120, 2.2, 30.0, 90.0);
            AddBcm125DaqCurrentFromShuntStep(steps, deviceManager, "B11 +5V_1电流", "BCM125.FIRST_STARTUP.B11_5V_1_CURRENT", 10, 117, 0.47, 15.0, 50.0);
            AddBcm125DaqCurrentFromShuntStep(steps, deviceManager, "B12 +5V_2电流", "BCM125.FIRST_STARTUP.B12_5V_2_CURRENT", 11, 118, 0.47, 15.0, 50.0);
        }

        protected void AddBcm125ProgrammingSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager)
        {
            int row = steps.Count;

            AddProgrammingActionStep(steps, row++, "9.1 检查编程相关COM连接", token => deviceManager.CheckProgrammingInstrumentsAsync(), "Ready", "Check Fail");
            AddProgrammingActionStep(steps, row++, "9.1 关闭仪器输出并复位设定值为0", token => deviceManager.TurnOffAllInstrumentsAsync(), "Off/Reset", "Off Fail");
            AddFixtureRelayStep(steps, row++, deviceManager, "9.2 BCM1 闭合RLP1/RLP2/RLP3连接J4", "SKBCM125Rlp1To3Relays", "3,4,5", true, "RLP1-3 ON");
            AddDelayStep(steps, row++, "9.3 BCM1 等待300ms", 300);
            AddProgrammingActionStep(steps, row++, "9.4 BCM1 闭合RL4给DUT供电", token => deviceManager.ControlRelayAsync(7, true), "RL4 ON", "RL4 Fail");
            AddDelayStep(steps, row++, "9.5 BCM1 等待6000ms", 6000);
            AddMbdCommandStep(steps, row++, deviceManager, "9.6 BCM1 发送EXIT到MBD", "EXIT", 50);
            AddMbdCommandStep(steps, row++, deviceManager, "9.7 BCM1 发送DIAG ON到MBD", "DIAG ON ", 50);
            AddMbdCommandStep(steps, row++, deviceManager, "9.8 BCM1 RESET置位 W86=1", "W86=1 ", 50);
            AddDaqRelayStep(steps, row++, deviceManager, "9.9 BCM1 闭合RLP4=DAQ CH5引入BOOT0 3.3V", "SKBCM125Rlp4DaqRelays", "@205", true, "RLP4 ON");
            AddDelayStep(steps, row++, "9.10 BCM1 等待50ms", 50);
            AddMbdCommandStep(steps, row++, deviceManager, "9.11 BCM1 RESET释放 W86=0", "W86=0 ", 50);
            AddSkippedFlashResultStep(steps, row++, "C01 BCM1 跳过TTL实际烧录并保存结果", "BCM125.PROGRAMMING.C01_BCM1_FLASH");
            AddMbdCommandStep(steps, row++, deviceManager, "9.13 BCM1 烧录后RESET置位 W86=1", "W86=1 ", 50);
            AddDaqRelayStep(steps, row++, deviceManager, "9.14 BCM1 断开RLP4=DAQ CH5", "SKBCM125Rlp4DaqRelays", "@205", false, "RLP4 OFF");
            AddMbdCommandStep(steps, row++, deviceManager, "9.15 BCM1 RESET释放 W86=0", "W86=0 ", 50);
            AddFixtureRelayStep(steps, row++, deviceManager, "9.16 BCM1 断开RLP1/RLP2/RLP3", "SKBCM125Rlp1To3Relays", "3,4,5", false, "RLP1-3 OFF");

            AddFixtureRelayStep(steps, row++, deviceManager, "9.17 BCM2 闭合RLP5/RLP6/RLP7连接J8", "SKBCM125Rlp5To7Relays", "6,7,8", true, "RLP5-7 ON");
            AddDelayStep(steps, row++, "9.18 BCM2 等待300ms", 300);
            AddMbdCommandStep(steps, row++, deviceManager, "9.19 BCM2 RESET置位 W86=1", "W86=1", 50);
            AddDaqRelayStep(steps, row++, deviceManager, "9.20 BCM2 闭合RLP8=DAQ CH6引入BOOT0 3.3V", "SKBCM125Rlp8DaqRelays", "@206", true, "RLP8 ON");
            AddDelayStep(steps, row++, "9.21 BCM2 等待50ms", 50);
            AddMbdCommandStep(steps, row++, deviceManager, "9.22 BCM2 RESET释放 W86=0", "W86=0 ", 50);
            AddSkippedFlashResultStep(steps, row++, "C02 BCM2 跳过TTL实际烧录并保存结果", "BCM125.PROGRAMMING.C02_BCM2_FLASH");
            AddMbdCommandStep(steps, row++, deviceManager, "9.24 BCM2 烧录后RESET置位 W86=1", "W86=1", 50);
            AddDaqRelayStep(steps, row++, deviceManager, "9.25 BCM2 断开RLP8=DAQ CH6", "SKBCM125Rlp8DaqRelays", "@206", false, "RLP8 OFF");
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
                    await Task.Delay(delayMs, token);
                    return new StepResult { IsPass = true, Value = delayMs + " ms" };
                }
            });
        }

        private void AddMbdCommandStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, SK441Device deviceManager, string name, string command, int waitAfterMs)
        {
            AddProgrammingActionStep(steps, rowIndex, name, token => deviceManager.SendMbdCommandAsync(command, waitAfterMs), "Sent", "Send Fail");
        }

        private void AddFixtureRelayStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, SK441Device deviceManager, string name, string configKey, string defaultRelays, bool close, string passValue)
        {
            AddProgrammingActionStep(
                steps,
                rowIndex,
                name,
                token => deviceManager.SetFixtureRelaysAsync(deviceManager.GetConfiguredRelayList(configKey, defaultRelays), close, name),
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

        private void AddSkippedFlashResultStep(System.Collections.Generic.List<TestStepItem> steps, int rowIndex, string name, string storeKey)
        {
            steps.Add(new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                PowerCritical = true,
                Action = async (token) =>
                {
                    OnLogWarning(name + "：实际 TTL 烧录当前按要求跳过，仅保存模拟烧录成功结果。");
                    await Task.Delay(200, token);
                    return new StepResult
                    {
                        IsPass = true,
                        Value = "1",
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

            AddProgrammingActionStep(steps, row++, "10.1 打开RL4断开DUT供电", token => deviceManager.ControlRelayAsync(7, false), "RL4 OFF", "RL4 Fail");
            AddDelayStep(steps, row++, "10.2 等待6500ms", 6500);
            AddProgrammingActionStep(steps, row++, "10.3 闭合RL4重新给DUT供电", token => deviceManager.ControlRelayAsync(7, true), "RL4 ON", "RL4 Fail");
            AddProgrammingActionStep(steps, row++, "10.4 检查USB-B/MBD串口通信已打开", token => deviceManager.CheckMbdCommunicationAsync(), "Open", "COM Fail");
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
                    string response = await deviceManager.QueryMbdCommandAsync(command, 0);
                    string[] values = ParseMbdRegisterValues(response);
                    if (values.Length == 0)
                    {
                        await OpenBcm125Rl4AfterFailureAsync(deviceManager);
                        return new StepResult { IsPass = false, Value = "No Data" };
                    }

                    int index = expectedZeroIndex >= 0 ? expectedZeroIndex : values.Length - 1;
                    bool pass = index >= 0 && index < values.Length && values[index] == "00";
                    string display = index >= 0 && index < values.Length ? values[index] : "Index Err";
                    if (!pass)
                        await OpenBcm125Rl4AfterFailureAsync(deviceManager);

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

        private static string[] ParseMbdRegisterValues(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return new string[0];

            int equals = response.IndexOf('=');
            int semicolon = response.IndexOf(';');
            if (equals < 0)
                return new string[0];

            string payload = semicolon > equals ? response.Substring(equals + 1, semicolon - equals - 1) : response.Substring(equals + 1);
            return payload.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToArray();
        }

        protected void AddBcm125FirmwareVersionSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager)
        {
            int row = steps.Count;

            AddProgrammingActionStep(steps, row++, "11.1 闭合RL4给DUT供电（如未上电）", token => deviceManager.ControlRelayAsync(7, true), "RL4 ON", "RL4 Fail");
            AddProgrammingActionStep(steps, row++, "11.2 检查USB-B/MBD串口通信已打开", token => deviceManager.CheckMbdCommunicationAsync(), "Open", "COM Fail");
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
                    string response = await deviceManager.QueryMbdCommandAsync("R0", 0);
                    string version = ParseFirmwareVersion(response);
                    string expected = deviceManager.GetBcm125ExpectedFirmwareVersion();
                    bool pass = string.Equals(version, expected, StringComparison.OrdinalIgnoreCase);
                    if (!pass)
                        await OpenBcm125Rl4AfterFailureAsync(deviceManager);

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

        private static string ParseFirmwareVersion(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return string.Empty;

            int equals = response.IndexOf('=');
            string value = equals >= 0 ? response.Substring(equals + 1) : response;
            int semicolon = value.IndexOf(';');
            if (semicolon >= 0)
                value = value.Substring(0, semicolon);

            return value.Trim().Trim('"');
        }

        protected void AddBcm125ResetFlatCableSteps(System.Collections.Generic.List<TestStepItem> steps, TestSequenceContext context, SK441Device deviceManager)
        {
            int row = steps.Count;

            AddProgrammingActionStep(steps, row++, "12.1 闭合RL4给DUT供电（如未上电）", token => deviceManager.ControlRelayAsync(7, true), "RL4 ON", "RL4 Fail");
            AddDelayStep(steps, row++, "12.2 等待3000ms", 3000);
            AddProgrammingActionStep(steps, row++, "12.3 检查USB-B/MBD串口通信已打开", token => deviceManager.CheckMbdCommunicationAsync(), "Open", "COM Fail");
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

            AddMbdCommandStep(steps, row++, deviceManager, "12.15 ST2发送EXIT返回MBD终端", "EXIT", 0);
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
                    string response = await deviceManager.QueryMbdCommandAsync("R10", 0);
                    double runningTime = ParseFirstNumber(response);
                    bool hasValue = !double.IsNaN(runningTime);
                    int integerPart = hasValue ? (int)Math.Floor(runningTime) : 0;
                    return new StepResult
                    {
                        IsPass = hasValue,
                        Value = hasValue ? integerPart.ToString() : "No Data",
                        StoreKey = storeKey,
                        NumericValue = hasValue ? integerPart : 0,
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
                    bool pass = hasX && hasY && Math.Abs(x - y) >= 2 && Math.Abs(y - 1) < 0.001;
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

        private static double ParseFirstNumber(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return double.NaN;

            string source = response;
            int equalsIndex = source.IndexOf('=');
            if (equalsIndex >= 0 && equalsIndex + 1 < source.Length)
                source = source.Substring(equalsIndex + 1);

            var match = System.Text.RegularExpressions.Regex.Match(source, @"[-+]?\d+(\.\d+)?");
            if (!match.Success)
                return double.NaN;

            double value;
            return double.TryParse(match.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out value)
                ? value
                : double.NaN;
        }

        protected void AddBcm125VrefVoltageSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager)
        {
            int row = steps.Count;

            AddProgrammingActionStep(steps, row++, "13.1 Close RL4 to power DUT", token => deviceManager.ControlRelayAsync(7, true), "RL4 ON", "RL4 Fail");
            AddProgrammingActionStep(steps, row++, "13.2 Check USB-B/MBD serial open", token => deviceManager.CheckMbdCommunicationAsync(), "Open", "COM Fail");
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
            AddProgrammingActionStep(steps, row++, "14.2 Check USB-B/MBD serial open", token => deviceManager.CheckMbdCommunicationAsync(), "Open", "COM Fail");
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
            AddProgrammingActionStep(steps, row++, "15.2 Check USB-B/MBD serial open", token => deviceManager.CheckMbdCommunicationAsync(), "Open", "COM Fail");
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
            AddProgrammingActionStep(steps, row++, "16.2 Check USB-B/MBD serial open", token => deviceManager.CheckMbdCommunicationAsync(), "Open", "COM Fail");
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
                    if (!pass) await OpenBcm125Rl4AfterFailureAsync(deviceManager);
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
                        pass = await deviceManager.SendMbdCommandAsync("W18=" + gain.ToString("0", System.Globalization.CultureInfo.InvariantCulture), 0);
                    if (!pass) await OpenBcm125Rl4AfterFailureAsync(deviceManager);
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
                    string response = await deviceManager.QueryMbdCommandAsync(command, 0);
                    double actual = ParseFirstNumber(response);
                    bool pass = TryGetStoredNumericValue(context, expectedKey, out expected) && !double.IsNaN(actual) && Math.Abs(actual - expected) <= 1.0;
                    if (!pass) await OpenBcm125Rl4AfterFailureAsync(deviceManager);
                    return new StepResult { IsPass = pass, Value = double.IsNaN(actual) ? "No Data" : FormatValue(actual, unit), StoreKey = storeKey, NumericValue = double.IsNaN(actual) ? 0 : actual, Unit = unit };
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
                        string response = await deviceManager.QueryMbdCommandAsync(command, 0);
                        double value = ParseFirstNumber(response);
                        if (!double.IsNaN(value))
                        {
                            sum += value;
                            count++;
                        }
                        if (i + 1 < sampleCount)
                            await Task.Delay(intervalMs, token);
                    }
                    double average = count > 0 ? sum / count : double.NaN;
                    bool pass = !double.IsNaN(average) && average >= lower && average <= upper;
                    if (!pass) await OpenBcm125Rl4AfterFailureAsync(deviceManager);
                    return new StepResult { IsPass = pass, Value = double.IsNaN(average) ? "No Data" : FormatValue(average, unit), StoreKey = storeKey, NumericValue = double.IsNaN(average) ? 0 : average, Unit = unit };
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
                    double sum = 0;
                    for (int i = 0; i < sampleCount; i++)
                    {
                        sum += await deviceManager.MeasureDaqChannelVoltageAsync(channel) * 1000.0;
                        if (i + 1 < sampleCount)
                            await Task.Delay(intervalMs, token);
                    }
                    double average = sum / Math.Max(1, sampleCount);
                    bool pass = average >= lower && average <= upper;
                    if (!pass) await OpenBcm125Rl4AfterFailureAsync(deviceManager);
                    return new StepResult { IsPass = pass, Value = FormatValue(average, "mV"), StoreKey = storeKey, NumericValue = average, Unit = "mV" };
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
                    pass = pass && gain >= 140000 && gain <= 145000;
                    if (pass)
                        pass = await deviceManager.SendMbdCommandAsync("W20=" + gain.ToString("0", System.Globalization.CultureInfo.InvariantCulture), 0);
                    if (!pass) await OpenBcm125Rl4AfterFailureAsync(deviceManager);
                    return new StepResult { IsPass = pass, Value = gain.ToString("0", System.Globalization.CultureInfo.InvariantCulture), StoreKey = storeKey, NumericValue = gain, Unit = "count" };
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
                    pass = pass && offset >= -600 && offset <= 0;
                    if (pass)
                        pass = await deviceManager.SendMbdCommandAsync("W21=" + offset.ToString("0", System.Globalization.CultureInfo.InvariantCulture), 0);
                    if (!pass) await OpenBcm125Rl4AfterFailureAsync(deviceManager);
                    return new StepResult { IsPass = pass, Value = offset.ToString("0", System.Globalization.CultureInfo.InvariantCulture), StoreKey = storeKey, NumericValue = offset, Unit = "mV" };
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
                    double sum = 0;
                    for (int i = 0; i < 8; i++)
                        sum += await deviceManager.MeasureDaqChannelVoltageAsync(channel);
                    double currentMa = (sum / 8.0) / shuntOhm * 1000.0;
                    bool pass = currentMa >= lower && currentMa <= upper;
                    if (!pass) await OpenBcm125Rl4AfterFailureAsync(deviceManager);
                    return new StepResult { IsPass = pass, Value = FormatValue(currentMa, "mA"), StoreKey = storeKey, NumericValue = currentMa, Unit = "mA" };
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
                    bool pass = ok && gain >= 87500 && gain <= 92500 && offset >= -80 && offset <= 45;
                    return new StepResult { IsPass = pass, Value = $"G={gain:0};O={offset:0}", StoreKey = gainKey, NumericValue = gain, Unit = "count" };
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
                    bool pass = ok && offset >= -80 && offset <= 45;
                    return new StepResult { IsPass = pass, Value = offset.ToString("0", System.Globalization.CultureInfo.InvariantCulture), StoreKey = offsetKey, NumericValue = offset, Unit = "mA" };
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
                    double value;
                    bool pass = TryGetStoredNumericValue(context, storeKey, out value);
                    if (pass)
                        pass = await deviceManager.SendMbdCommandAsync(commandPrefix + value.ToString("0", System.Globalization.CultureInfo.InvariantCulture), 0);
                    if (!pass) await OpenBcm125Rl4AfterFailureAsync(deviceManager);
                    return new StepResult { IsPass = pass, Value = pass ? value.ToString("0", System.Globalization.CultureInfo.InvariantCulture) : "Missing", Unit = string.Empty };
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
            AddProgrammingActionStep(steps, row++, "17.2 " + suffix + " check USB-B/MBD serial open", token => deviceManager.CheckMbdCommunicationAsync(), "Open", "COM Fail");
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
            AddDaqRelayStep(steps, row++, deviceManager, "18.3 Open RL5.12/RL10.12", "SKBCM125Rl5Rl10DaqRelays", "@202,@203", false, "RL5/RL10 Open");
            AddDelayStep(steps, row++, "18.4 Wait 50ms", 50);
            AddProgrammingActionStep(steps, row++, "18.5 Reset VBUS generator 0V/0A OVP150", token => deviceManager.SetVbusGeneratorAsync(0, 0, 150, false), "VBUS Reset", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "18.6 Check serial communication", token => deviceManager.CheckMbdCommunicationAsync(), "Open", "COM Fail");
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
                    if (!await deviceManager.SendMbdCommandAsync(remoteCommand, 0))
                        return false;
                    if (!await deviceManager.SendMbdCommandAsync("DIAG ON", 0))
                        return false;
                    return await deviceManager.SendMbdCommandAsync("W75=1", 0);
                },
                "Precharge ON",
                "Precharge Fail");
            AddProgrammingActionStep(
                steps,
                row++,
                "18.39 " + suffix + " send EXIT to MBD",
                async token =>
                {
                    if (!await deviceManager.SendMbdCommandAsync("EXIT", 0))
                        return false;
                    if (!await deviceManager.SendMbdCommandAsync("DIAG ON", 0))
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
                    if (!await deviceManager.SendMbdCommandAsync(remoteCommand, 0))
                        return false;
                    return await deviceManager.SendMbdCommandAsync("W75=0", 0);
                },
                "VBUS Reset/Precharge OFF",
                "Cleanup Fail");
        }

        protected void AddBcm125MidpointCalibrationSteps(System.Collections.Generic.List<TestStepItem> steps, TestSequenceContext context, SK441Device deviceManager)
        {
            int row = steps.Count;

            AddProgrammingActionStep(steps, row++, "19.1 Close RL4 to power DUT", token => deviceManager.ControlRelayAsync(7, true), "RL4 ON", "RL4 Fail");
            AddFixtureRelayStep(steps, row++, deviceManager, "19.2 Open RL1/RL2/RL9 relays", "SKBCM125MidpointOpenRelays", "8,9,10,11,12,13,14,15,16", false, "Relays Open");
            AddDaqRelayStep(steps, row++, deviceManager, "19.3 Open RL5.12/RL10.12", "SKBCM125Rl5Rl10DaqRelays", "@202,@203", false, "RL5/RL10 Open");
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
            AddProgrammingActionStep(steps, row++, "19.9 " + suffix + " check serial communication", token => deviceManager.CheckMbdCommunicationAsync(), "Open", "COM Fail");
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
            AddDaqAverageMvStep(steps, row++, deviceManager, m02 + " " + suffix + " average DAQ CH06 VMID 62.5V", 106, prefix + m02 + "_VMID_62V5_MV", 62000, 63000, 8, 0);
            AddProgrammingActionStep(steps, row++, "19.21 " + suffix + " set VMID 5V/0.1A OVP300", token => deviceManager.SetVbusGeneratorAsync(5, 0.1f, 300, true), "VMID Set", "VMID Fail");
            AddDelayStep(steps, row++, "19.22 " + suffix + " wait 12000ms", 12000);
            AddMbdAverageQueryStep(steps, row++, deviceManager, m03 + " " + suffix + " average R93 at 5V", "R93", prefix + m03 + "_ADC2_VMID_5V", 150, 170, "count", 8, 0);
            AddDaqAverageMvStep(steps, row++, deviceManager, m04 + " " + suffix + " average DAQ CH06 VMID 5V", 106, prefix + m04 + "_VMID_5V_MV", 4750, 5250, 8, 0);
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
            AddDaqRelayStep(steps, row++, deviceManager, "20.2 Open RL5.12/RL10.12", "SKBCM125Rl5Rl10DaqRelays", "@202,@203", false, "RL5/RL10 Open");
            AddDelayStep(steps, row++, "20.3 Wait 50ms", 50);
            AddProgrammingActionStep(steps, row++, "20.4 Reset VBUS generator", token => deviceManager.SetVbusGeneratorAsync(0, 0, 150, false), "VBUS Reset", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "20.4 Reset string generator", token => deviceManager.SetStringGeneratorAsync(0, 0, 150, false), "String Reset", "String Fail");
            AddProgrammingActionStep(steps, row++, "20.5 Reset electronic load", token => deviceManager.ResetElectronicLoadAsync(), "Load Reset", "Load Fail");
            AddDischargeCurrentStringSteps(steps, context, deviceManager, ref row, "ST1", "REMOTE1", "N01", "N02", "N03", "N04", "N05", "N06", "N07", "N08", "SKBCM125DischargeSt1CloseRelays", "8,11,10", 113, 0.0215909);
            AddDischargeCurrentStringSteps(steps, context, deviceManager, ref row, "ST2", "REMOTE2", "N09", "N10", "N11", "N12", "N13", "N14", "N15", "N16", "SKBCM125DischargeSt2CloseRelays", "8,16,10", 115, 0.0215914);
            AddMbdCommandStep(steps, row++, deviceManager, "20.52 Send EXIT to MBD terminal", "EXIT", 0);
            AddProgrammingActionStep(steps, row++, "20.53 Close COM with instruments", token => Task.FromResult(true), "Close OK", "Close Fail");
        }

        private void AddDischargeCurrentStringSteps(System.Collections.Generic.List<TestStepItem> steps, TestSequenceContext context, SK441Device deviceManager, ref int row, string suffix, string remoteCommand, string n01, string n02, string n03, string n04, string n05, string n06, string n07, string n08, string relayConfigKey, string relayDefaults, int shuntChannel, double shuntOhm)
        {
            string prefix = "BCM125.DISCHARGE." + suffix + ".";
            AddFixtureRelayStep(steps, row++, deviceManager, "20.6 " + suffix + " close discharge relays", relayConfigKey, relayDefaults, true, "Relays Closed");
            AddDelayStep(steps, row++, "20.7 " + suffix + " wait 20ms", 20);
            AddProgrammingActionStep(steps, row++, "20.8 " + suffix + " set string generator 12V/1A OVP16", token => deviceManager.SetStringGeneratorAsync(12, 1, 16, false), "String Set", "String Fail");
            AddProgrammingActionStep(steps, row++, "20.9 " + suffix + " turn on string generator", token => deviceManager.SetStringGeneratorAsync(12, 1, 16, true), "String ON", "String Fail");
            AddProgrammingActionStep(steps, row++, "20.10 " + suffix + " check serial communication", token => deviceManager.CheckMbdCommunicationAsync(), "Open", "COM Fail");
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
            AddDaqRelayStep(steps, row++, deviceManager, "21.3 Open RL5.12/RL10.12", "SKBCM125Rl5Rl10DaqRelays", "@202,@203", false, "RL5/RL10 Open");
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
            AddProgrammingActionStep(steps, row++, "21.11 " + suffix + " check serial communication", token => deviceManager.CheckMbdCommunicationAsync(), "Open", "COM Fail");
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
                        double idch = ParseFirstNumber(await deviceManager.QueryMbdCommandAsync("R45", 0));
                        int gate = (int)ParseFirstNumber(await deviceManager.QueryMbdCommandAsync("R59", 0));
                        if (previousGate == 0 && gate == 1)
                        {
                            triggerIdch = idch;
                            break;
                        }
                        previousGate = gate;
                    }

                    bool pass = !double.IsNaN(triggerIdch) && triggerIdch >= 200 && triggerIdch <= 1100;
                    if (!pass) await OpenBcm125Rl4AfterFailureAsync(deviceManager);
                    return new StepResult { IsPass = pass, Value = double.IsNaN(triggerIdch) ? "No Trigger" : FormatValue(triggerIdch, "mA"), StoreKey = storeKey, NumericValue = double.IsNaN(triggerIdch) ? 0 : triggerIdch, Unit = "mA" };
                }
            });
        }

        protected void AddBcm125ShortProtectionSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager)
        {
            int row = steps.Count;

            AddProgrammingActionStep(steps, row++, "22.1 Close RL4 to power DUT", token => deviceManager.ControlRelayAsync(7, true), "RL4 ON", "RL4 Fail");
            AddFixtureRelayStep(steps, row++, deviceManager, "22.2 Open RL1/RL2/RL9 relays", "SKBCM125ShortProtectionOpenRelays", "8,9,10,11,13,15,16", false, "Relays Open");
            AddDaqRelayStep(steps, row++, deviceManager, "22.3 Open RL5.12/RL10.12", "SKBCM125Rl5Rl10DaqRelays", "@202,@203", false, "RL5/RL10 Open");
            AddDelayStep(steps, row++, "22.4 Wait 50ms", 50);
            AddProgrammingActionStep(steps, row++, "22.5 Reset VBUS generator", token => deviceManager.SetVbusGeneratorAsync(0, 0, 300, false), "VBUS Reset", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "22.5 Reset string generator", token => deviceManager.SetStringGeneratorAsync(0, 0, 300, false), "String Reset", "String Fail");
            AddProgrammingActionStep(steps, row++, "22.6 Reset electronic load", token => deviceManager.ResetElectronicLoadAsync(), "Load Reset", "Load Fail");

            AddShortProtectionStringSteps(steps, deviceManager, ref row, "ST1", "REMOTE1", "U1.2", "@202", "SKBCM125ShortProtectionSt1CloseRelays", "8,11,10", "P01", "P02", "P03", "P04");
            AddShortProtectionStringSteps(steps, deviceManager, ref row, "ST2", "REMOTE2", "U33.2", "@203", "SKBCM125ShortProtectionSt2CloseRelays", "8,16,10", "P05", "P06", "P07", "P08");

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
            AddProgrammingActionStep(steps, row++, "22.12 " + suffix + " check serial communication", token => deviceManager.CheckMbdCommunicationAsync(), "Open", "COM Fail");
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
            AddDaqRelayStep(steps, row++, deviceManager, "23.3 Open RL5.12/RL10.12", "SKBCM125Rl5Rl10DaqRelays", "@202,@203", false, "RL5/RL10 Open");
            AddDelayStep(steps, row++, "23.4 Wait 50ms", 50);
            AddProgrammingActionStep(steps, row++, "23.5 Reset VBUS generator 0V/0A OVP150", token => deviceManager.SetVbusGeneratorAsync(0, 0, 150, false), "VBUS Reset", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "23.5 Reset string generator 0V/0A OVP150", token => deviceManager.SetStringGeneratorAsync(0, 0, 150, false), "String Reset", "String Fail");
            AddProgrammingActionStep(steps, row++, "23.6 Reset electronic load", token => deviceManager.ResetElectronicLoadAsync(), "Load Reset", "Load Fail");

            AddChargingCurrentStringSteps(steps, context, deviceManager, ref row, "ST1", "REMOTE1", "SKBCM125ChargingSt1CloseRelays", "15,11,8", 109, 113, 0.0215909, "Q01", "Q02", "Q03", "Q04", "Q05", "Q06", "Q07", "Q08", "Q09", "Q10", "Q11", "Q12", "Q13", "Q14", "Q15", "Q16", "W16=", "W17=", "W22=", "W24=", "W23=");
            AddChargingCurrentStringSteps(steps, context, deviceManager, ref row, "ST2", "REMOTE2", "SKBCM125ChargingSt2CloseRelays", "16,12,8", 110, 115, 0.0215914, "Q17", "Q18", "Q19", "Q20", "Q21", "Q22", "Q23", "Q24", "Q25", "Q26", "Q27", "Q28", "Q29", "Q30", "Q31", "Q32", "W16=", "W17=", "W22=", "W24=", "W23=");

            AddProgrammingActionStep(steps, row++, "23.62 Close COM with instruments", token => Task.FromResult(true), "Close OK", "Close Fail");
        }

        private void AddChargingCurrentStringSteps(System.Collections.Generic.List<TestStepItem> steps, TestSequenceContext context, SK441Device deviceManager, ref int row, string suffix, string remoteCommand, string relayConfigKey, string relayDefaults, int vMeasureChannel, int shuntChannel, double shuntOhm, string q01, string q02, string q03, string q04, string q05, string q06, string q07, string q08, string q09, string q10, string q11, string q12, string q13, string q14, string q15, string q16, string gainVstrWrite, string offsetVstrWrite, string gainIccbWrite, string gainIccbVstrWrite, string offsetIccbWrite)
        {
            string prefix = "BCM125.CHARGING." + suffix + ".";
            AddFixtureRelayStep(steps, row++, deviceManager, "23.7 " + suffix + " close charging calibration relays", relayConfigKey, relayDefaults, true, "Relays Closed");
            AddDelayStep(steps, row++, "23.8 " + suffix + " wait 20ms", 20);
            AddProgrammingActionStep(steps, row++, "23.9 " + suffix + " set string generator 0V/0A OVP150", token => deviceManager.SetStringGeneratorAsync(0, 0, 150, false), "String Set", "String Fail");
            AddDelayStep(steps, row++, "23.10 " + suffix + " wait 1000ms", 1000);
            AddProgrammingActionStep(steps, row++, "23.11 " + suffix + " turn on string generator", token => deviceManager.SetStringGeneratorAsync(0, 0, 150, true), "String ON", "String Fail");
            AddProgrammingActionStep(steps, row++, "23.12 " + suffix + " set VSTR_NEG bus generator 1V/1A OVP150", token => deviceManager.SetVbusGeneratorAsync(1, 1, 150, true), "VBUS ON", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "23.13 " + suffix + " check serial communication", token => deviceManager.CheckMbdCommunicationAsync(), "Open", "COM Fail");
            AddMbdCommandStep(steps, row++, deviceManager, "23.14 " + suffix + " send EXIT to MBD terminal", "EXIT", 0);
            AddMbdCommandStep(steps, row++, deviceManager, "23.14 " + suffix + " send DIAG ON to MBD", "DIAG ON", 0);
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
            AddMbdCommandStep(steps, row++, deviceManager, "23.34 " + suffix + " set W69=0 CCB on", "W69=0", 0);
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
            AddMbdCommandStep(steps, row++, deviceManager, "23.51 " + suffix + " send ACT->FLASH", "ACT->FLASH", 0);
            AddProgrammingActionStep(steps, row++, "23.52 " + suffix + " turn off string generator", token => deviceManager.SetStringGeneratorAsync(0, 0, 10, false), "String OFF", "String Fail");
            AddProgrammingActionStep(steps, row++, "23.55 " + suffix + " reset generators 0V/0A OVP150", token => deviceManager.SetVbusGeneratorAsync(0, 0, 150, false), "Generators Reset", "Generator Fail");
            AddProgrammingActionStep(steps, row++, "23.56 " + suffix + " reset electronic load", token => deviceManager.ResetElectronicLoadAsync(), "Load Reset", "Load Fail");
            AddDelayStep(steps, row++, "23.57 " + suffix + " wait 1000ms", 1000);
            AddFixtureRelayStep(steps, row++, deviceManager, "23.58 " + suffix + " open charging calibration relays", "SKBCM125ChargingOpenRelays", "8,9,10,11,12,13,15,16", false, "Relays Open");
            AddDelayStep(steps, row++, "23.59 " + suffix + " wait 200ms", 200);
            AddMbdCommandStep(steps, row++, deviceManager, "23.59 " + suffix + " send EXIT to MBD terminal", "EXIT", 0);
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
                    double sum = 0;
                    for (int i = 0; i < 8; i++)
                        sum += await deviceManager.MeasureDaqChannelVoltageAsync(channel) * 1000.0;
                    double mv = sum / 8.0;
                    bool pass = mv >= lower && mv <= upper;
                    if (!pass) await OpenBcm125Rl4AfterFailureAsync(deviceManager);
                    return new StepResult { IsPass = pass, Value = FormatValue(mv, "mV"), StoreKey = storeKey, NumericValue = mv, Unit = "mV" };
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
                    double sum = 0;
                    for (int i = 0; i < 8; i++)
                        sum += await deviceManager.MeasureDaqChannelVoltageAsync(channel);
                    double currentMa = sign * (sum / 8.0) / shuntOhm * 1000.0;
                    bool pass = currentMa >= lower && currentMa <= upper;
                    if (!pass) await OpenBcm125Rl4AfterFailureAsync(deviceManager);
                    return new StepResult { IsPass = pass, Value = FormatValue(currentMa, "mA"), StoreKey = storeKey, NumericValue = currentMa, Unit = "mA" };
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
                    if (!TryGetStoredNumericValue(context, prefix + q01 + "_ADC0_0V_0A", out adc0Low)
                        || !TryGetStoredNumericValue(context, prefix + q03 + "_VSTR_NEG_1000MV_0A", out vLow)
                        || !TryGetStoredNumericValue(context, prefix + q05 + "_ADC0_15V_0A", out adc0High)
                        || !TryGetStoredNumericValue(context, prefix + q07 + "_VSTR_NEG_15000MV_0A", out vHigh)
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
                    if (!TryGetStoredNumericValue(context, prefix + q01 + "_ADC0_0V_0A", out adc0Low)
                        || !TryGetStoredNumericValue(context, prefix + q03 + "_VSTR_NEG_1000MV_0A", out vLow)
                        || !TryGetStoredNumericValue(context, prefix + q12 + "_GAIN_VSTR_NEG", out gainVstr))
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
                    if (!TryGetStoredNumericValue(context, prefix + q02 + "_ADC14_0V_0A", out adc14Low)
                        || !TryGetStoredNumericValue(context, prefix + q04 + "_CURRENT_0MA", out current0)
                        || !TryGetStoredNumericValue(context, prefix + q09 + "_ADC14_0V_6A", out adc14Six)
                        || !TryGetStoredNumericValue(context, prefix + q10 + "_CURRENT_6000MA", out currentSix)
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
                    if (!TryGetStoredNumericValue(context, prefix + q01 + "_ADC0_0V_0A", out adc0Low)
                        || !TryGetStoredNumericValue(context, prefix + q02 + "_ADC14_0V_0A", out adc14Low)
                        || !TryGetStoredNumericValue(context, prefix + q05 + "_ADC0_15V_0A", out adc0High)
                        || !TryGetStoredNumericValue(context, prefix + q06 + "_ADC14_15V_0A", out adc14High)
                        || !TryGetStoredNumericValue(context, prefix + q14 + "_GAIN_ICCB", out gainIccb)
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
                    if (!TryGetStoredNumericValue(context, prefix + q01 + "_ADC0_0V_0A", out adc0Low)
                        || !TryGetStoredNumericValue(context, prefix + q02 + "_ADC14_0V_0A", out adc14Low)
                        || !TryGetStoredNumericValue(context, prefix + q14 + "_GAIN_ICCB", out gainIccb)
                        || !TryGetStoredNumericValue(context, prefix + q15 + "_GAIN_ICCB_VSTR_NEG", out gainIccbVstr))
                        return null;

                    return Math.Round(-1.0 * ((gainIccb * adc14Low) + (gainIccbVstr * adc0Low)) / 4096.0);
                });
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
                    bool pass = calculated.HasValue && value >= lower && value <= upper;
                    if (!pass) await OpenBcm125Rl4AfterFailureAsync(deviceManager);
                    return new StepResult
                    {
                        IsPass = pass,
                        Value = calculated.HasValue ? FormatValue(value, unit) : "Missing Input",
                        StoreKey = storeKey,
                        NumericValue = value,
                        Unit = unit
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

            AddCurrentAndVstrNegVerificationStringSteps(steps, deviceManager, ref row, "ST1", "REMOTE1", "SKBCM125VerificationSt1CloseRelays", "15,11,8", 109, 113, 0.0215909, "R01", "R02", "R03", "R04", "R05", "R06", "R07", "R08", "R09", "R10", "R11", "R12", "R13", "R14");
            AddCurrentAndVstrNegVerificationStringSteps(steps, deviceManager, ref row, "ST2", "REMOTE2", "SKBCM125VerificationSt2CloseRelays", "16,12,8", 110, 115, 0.0215914, "R15", "R16", "R17", "R18", "R19", "R20", "R21", "R22", "R23", "R24", "R25", "R26", "R27", "R28");

            AddDelayStep(steps, row++, "24.59 Wait 200ms", 200);
            AddMbdCommandStep(steps, row++, deviceManager, "24.61 Send EXIT to MBD terminal", "EXIT", 0);
            AddProgrammingActionStep(steps, row++, "24.62 Close COM with instruments", token => Task.FromResult(true), "Close OK", "Close Fail");
        }

        private void AddCurrentAndVstrNegVerificationStringSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager, ref int row, string suffix, string remoteCommand, string relayConfigKey, string relayDefaults, int vMeasureChannel, int shuntChannel, double shuntOhm, string r01, string r02, string r03, string r04, string r05, string r06, string r07, string r08, string r09, string r10, string r11, string r12, string r13, string r14)
        {
            string prefix = "BCM125.VERIFY_CHARGING." + suffix + ".";
            AddFixtureRelayStep(steps, row++, deviceManager, "24.6 " + suffix + " close verification relays", relayConfigKey, relayDefaults, true, "Relays Closed");
            AddDelayStep(steps, row++, "24.7 " + suffix + " wait 20ms", 20);
            AddProgrammingActionStep(steps, row++, "24.8 " + suffix + " set string generator 0V/0A OVP150", token => deviceManager.SetStringGeneratorAsync(0, 0, 150, false), "String Set", "String Fail");
            AddProgrammingActionStep(steps, row++, "24.9 " + suffix + " turn on string generator", token => deviceManager.SetStringGeneratorAsync(0, 0, 150, true), "String ON", "String Fail");
            AddProgrammingActionStep(steps, row++, "24.10 " + suffix + " set bus generator 1V/1A OVP150", token => deviceManager.SetVbusGeneratorAsync(1, 1, 150, false), "VBUS Set", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "24.11 " + suffix + " turn on bus generator", token => deviceManager.SetVbusGeneratorAsync(1, 1, 150, true), "VBUS ON", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "24.12 " + suffix + " check serial communication", token => deviceManager.CheckMbdCommunicationAsync(), "Open", "COM Fail");
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
            AddDaqRelayStep(steps, row++, deviceManager, "25.3 Open RL5.12/RL10.12", "SKBCM125Rl5Rl10DaqRelays", "@202,@203", false, "RL5/RL10 Open");
            AddDelayStep(steps, row++, "25.4 Wait 50ms", 50);
            AddProgrammingActionStep(steps, row++, "25.5 Reset VBUS generator 0V/0A OVP150", token => deviceManager.SetVbusGeneratorAsync(0, 0, 150, false), "VBUS Reset", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "25.5 Reset string generator 0V/0A OVP150", token => deviceManager.SetStringGeneratorAsync(0, 0, 150, false), "String Reset", "String Fail");
            AddProgrammingActionStep(steps, row++, "25.6 Reset electronic load", token => deviceManager.ResetElectronicLoadAsync(), "Load Reset", "Load Fail");

            AddCcbFunctionalStringSteps(steps, deviceManager, ref row, "ST1", "REMOTE1", "W94=", "SKBCM125CcbSt1CloseRelays", "9,13,8", 113, 114, 0.0215909, "S01", "S02");
            AddCcbFunctionalStringSteps(steps, deviceManager, ref row, "ST2", "REMOTE2", "W91=", "SKBCM125CcbSt2CloseRelays", "9,14,8", 115, 116, 0.0215914, "S03", "S04");

            AddProgrammingActionStep(steps, row++, "25.58 Close COM with instruments", token => Task.FromResult(true), "Close OK", "Close Fail");
        }

        private void AddCcbFunctionalStringSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager, ref int row, string suffix, string remoteCommand, string mbdRelayCommandPrefix, string relayConfigKey, string relayDefaults, int shuntChannel, int stringVoltageChannel, double shuntOhm, string sCurrent, string sVoltage)
        {
            string prefix = "BCM125.CCB." + suffix + ".";
            AddFixtureRelayStep(steps, row++, deviceManager, "25.7 " + suffix + " close CCB relays", relayConfigKey, relayDefaults, true, "Relays Closed");
            AddDelayStep(steps, row++, "25.8 " + suffix + " wait 50ms", 50);
            AddProgrammingActionStep(steps, row++, "25.9 " + suffix + " check serial communication", token => deviceManager.CheckMbdCommunicationAsync(), "Open", "COM Fail");
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
            AddDaqRelayStep(steps, row++, deviceManager, "26.3 Open RL5.12/RL10.12", "SKBCM125Rl5Rl10DaqRelays", "@202,@203", false, "RL5/RL10 Open");
            AddDelayStep(steps, row++, "26.4 Wait 50ms", 50);
            AddProgrammingActionStep(steps, row++, "26.5 Reset VBUS generator 0V/0A OVP150", token => deviceManager.SetVbusGeneratorAsync(0, 0, 150, false), "VBUS Reset", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "26.5 Reset string generator 0V/0A OVP150", token => deviceManager.SetStringGeneratorAsync(0, 0, 150, false), "String Reset", "String Fail");
            AddProgrammingActionStep(steps, row++, "26.6 Reset electronic load", token => deviceManager.ResetElectronicLoadAsync(), "Load Reset", "Load Fail");

            AddStringTestStringSteps(steps, deviceManager, ref row, "ST1", "REMOTE1", "W94=", "SKBCM125StringTestSt1CloseRelays", "9,13,8", 113, 0.0215909, "T01", "T02", "T03");
            AddStringTestStringSteps(steps, deviceManager, ref row, "ST2", "REMOTE2", "W91=", "SKBCM125StringTestSt2CloseRelays", "9,14,8", 115, 0.0215914, "T04", "T05", "T06");

            AddProgrammingActionStep(steps, row++, "26.43 Close COM with instruments", token => Task.FromResult(true), "Close OK", "Close Fail");
        }

        private void AddStringTestStringSteps(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager, ref int row, string suffix, string remoteCommand, string mbdRelayCommandPrefix, string relayConfigKey, string relayDefaults, int shuntChannel, double shuntOhm, string t01, string t02, string t03)
        {
            string prefix = "BCM125.STRING_TEST." + suffix + ".";
            AddFixtureRelayStep(steps, row++, deviceManager, "26.7 " + suffix + " close string test relays", relayConfigKey, relayDefaults, true, "Relays Closed");
            AddDelayStep(steps, row++, "26.8 " + suffix + " wait 20ms", 20);
            AddProgrammingActionStep(steps, row++, "26.9 " + suffix + " set BUS generator 50V/0.5A OVP150", token => deviceManager.SetVbusGeneratorAsync(50, 0.5f, 150, false), "VBUS Set", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "26.10 " + suffix + " turn on BUS generator", token => deviceManager.SetVbusGeneratorAsync(50, 0.5f, 150, true), "VBUS ON", "VBUS Fail");
            AddDelayStep(steps, row++, "26.11 " + suffix + " wait 3000ms", 3000);
            AddProgrammingActionStep(steps, row++, "26.12 " + suffix + " set string generator 45V/3A OVP150", token => deviceManager.SetStringGeneratorAsync(45, 3, 150, false), "String Set", "String Fail");
            AddProgrammingActionStep(steps, row++, "26.13 " + suffix + " turn on string generator", token => deviceManager.SetStringGeneratorAsync(45, 3, 150, true), "String ON", "String Fail");
            AddProgrammingActionStep(steps, row++, "26.14 " + suffix + " check serial communication", token => deviceManager.CheckMbdCommunicationAsync(), "Open", "COM Fail");
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
            AddDaqRelayStep(steps, row++, deviceManager, "27.3 Open RL5.12/RL10.12", "SKBCM125Rl5Rl10DaqRelays", "@202,@203", false, "RL5/RL10 Open");
            AddDelayStep(steps, row++, "27.4 Wait 50ms", 50);
            AddProgrammingActionStep(steps, row++, "27.5 Check generator COM communication", token => deviceManager.CheckProgrammingInstrumentsAsync(), "COM OK", "COM Fail");
            AddProgrammingActionStep(steps, row++, "27.6 Reset VBUS generator 0V/0A OVP150", token => deviceManager.SetVbusGeneratorAsync(0, 0, 150, false), "VBUS Reset", "VBUS Fail");
            AddProgrammingActionStep(steps, row++, "27.6 Reset string generator 0V/0A OVP150", token => deviceManager.SetStringGeneratorAsync(0, 0, 150, false), "String Reset", "String Fail");
            AddProgrammingActionStep(steps, row++, "27.7 Reset electronic load", token => deviceManager.ResetElectronicLoadAsync(), "Load Reset", "Load Fail");

            AddWritingInfoStringSteps(steps, context, deviceManager, ref row, "ST1", "REMOTE1", "1", "U01", "U02", "U03");
            AddWritingInfoStringSteps(steps, context, deviceManager, ref row, "ST2", "REMOTE2", "2", "U04", "U05", "U06");

            AddProgrammingActionStep(steps, row++, "27.22 Open RL4 to turn off BCM DUT", token => deviceManager.ControlRelayAsync(7, false), "RL4 OFF", "RL4 Fail");
            AddProgrammingActionStep(steps, row++, "27.23 Close MBD target-board COM", token => deviceManager.CloseTargetBoardCommunicationAsync(), "MBD COM Closed", "MBD COM Close Fail");
            AddProgrammingActionStep(steps, row++, "27.24 Turn off all power supplies and electronic load", token => deviceManager.TurnOffAllInstrumentsAsync(), "All outputs OFF", "Output OFF Fail");
        }

        protected void AddBcm125FixtureReleaseSteps(
            System.Collections.Generic.List<TestStepItem> steps,
            SK441Device deviceManager)
        {
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
                    bool pass = await deviceManager.SendMbdCommandAsync("W40=" + value, 0);
                    if (pass) capture(value);
                    if (!pass) await OpenBcm125Rl4AfterFailureAsync(deviceManager);
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
                    bool pass = !string.IsNullOrWhiteSpace(value) && await deviceManager.SendMbdCommandAsync("W41=" + value, 0);
                    if (pass) capture(value);
                    if (!pass) await OpenBcm125Rl4AfterFailureAsync(deviceManager);
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
                    string response = await deviceManager.QueryMbdCommandAsync("ACT->FLASH", 0);
                    bool pass = !string.IsNullOrWhiteSpace(response)
                        && response.IndexOf("ACT values copied to FLASH", StringComparison.OrdinalIgnoreCase) >= 0
                        && response.IndexOf("Ready", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!pass) await OpenBcm125Rl4AfterFailureAsync(deviceManager);
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
                    string response = await deviceManager.QueryMbdCommandAsync(command, 0);
                    bool pass = !string.IsNullOrWhiteSpace(expected)
                        && !string.IsNullOrWhiteSpace(response)
                        && response.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!pass) await OpenBcm125Rl4AfterFailureAsync(deviceManager);
                    return new StepResult { IsPass = pass, Value = pass ? "1" : (response ?? "No Data"), StoreKey = storeKey, NumericValue = pass ? 1 : 0, Unit = string.Empty };
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
                    string response = await deviceManager.QueryMbdCommandAsync(command, 0);
                    double value = ParseFirstNumber(response);
                    bool hasValue = !double.IsNaN(value);
                    bool pass = hasValue && value >= lower && value <= upper;
                    if (!pass)
                        await OpenBcm125Rl4AfterFailureAsync(deviceManager);

                    return new StepResult
                    {
                        IsPass = pass,
                        Value = hasValue ? FormatValue(value, unit) : "No Data",
                        StoreKey = storeKey,
                        NumericValue = hasValue ? value : 0,
                        Unit = unit
                    };
                }
            });
        }

        private void AddBcm125DaqVoltageStep(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager, string name, string storeKey, int offset, int channel, double lower, double upper, string unit)
        {
            steps.Add(new TestStepItem
            {
                Name = $"8.{3 + offset} FIRST START-UP-{name}",
                RowIndex = steps.Count,
                PowerCritical = true,
                Action = async (token) =>
                {
                    double value = await deviceManager.MeasureDaqChannelVoltageAsync(channel);
                    bool pass = value >= lower && value <= upper;
                    if (!pass) await OpenBcm125Rl4AfterFailureAsync(deviceManager);
                    return new StepResult
                    {
                        IsPass = pass,
                        Value = FormatValue(value, unit),
                        StoreKey = storeKey,
                        NumericValue = value,
                        Unit = unit
                    };
                }
            });
        }

        private void AddBcm125DaqCurrentFromShuntStep(System.Collections.Generic.List<TestStepItem> steps, SK441Device deviceManager, string name, string storeKey, int offset, int channel, double shuntOhm, double lowerMa, double upperMa)
        {
            steps.Add(new TestStepItem
            {
                Name = $"8.{3 + offset} FIRST START-UP-{name}",
                RowIndex = steps.Count,
                PowerCritical = true,
                Action = async (token) =>
                {
                    double voltage = await deviceManager.MeasureDaqChannelVoltageAsync(channel);
                    double currentMa = voltage / shuntOhm * 1000.0;
                    bool pass = currentMa >= lowerMa && currentMa <= upperMa;
                    if (!pass) await OpenBcm125Rl4AfterFailureAsync(deviceManager);
                    return new StepResult
                    {
                        IsPass = pass,
                        Value = FormatValue(currentMa, "mA"),
                        StoreKey = storeKey,
                        NumericValue = currentMa,
                        Unit = "mA"
                    };
                }
            });
        }

        private async Task OpenBcm125Rl4AfterFailureAsync(SK441Device deviceManager)
        {
            OnLogWarning("BCM-125 供电相关测试失败：断开 RL4，关闭 DUT 供电。");
            await EnsureRl4OffAsync(deviceManager);
        }

        private static string FormatValue(double value, string unit)
        {
            return $"{value:0.###} {unit}";
        }
    }

}
