using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using TestPlatform;

namespace TestPlatform.TestSequences
{
    /// <summary>
    /// LO LR4-2912-0D 独立一拖三测试序列。
    ///
    /// 设计目标：
    /// 1. 不调用 MainWindow.SN_Input / ConfirmFixtureDownward_FC / GetTPVolt / ExecuteTestStepAsync。
    /// 2. XML 保留旧 CSV 的测试项，同时把继电器动作和延时也作为 DataGrid 可见步骤。
    /// 3. 开启哪个通道，就按 channelIndex 自动发送 01/02/03 站位电压采集命令，并写入对应 ChannelXValue / ChannelXResult。
    /// 4. 只有一个 RS485 串口，因此所有继电器命令和电压读取都通过 SerialAccessLock 串行执行。
    /// 5. 三通道同时测试时，共享动作/等待/测量步骤采用会话机制，按通道 1 → 2 → 3 顺序读取并更新。
    /// </summary>
    public class LR4_2912Sequence : ITestSequence
    {
        private readonly ITestGridService _grid;
        private readonly Dispatcher _dispatcher;
        private readonly Window _owner;

        private static readonly SemaphoreSlim SerialAccessLock = new SemaphoreSlim(1, 1);

        private static readonly object FixtureDownSessionLock = new object();
        private static FixtureDownWaitSession ActiveFixtureDownSession;

        private static readonly object SharedStepSessionLock = new object();
        private static readonly Dictionary<string, SharedStepSession> SharedStepSessions = new Dictionary<string, SharedStepSession>();

        public string SequenceKey => "LR4_2912_CLASS";

        public event Action<string> LogInfo;
        public event Action<string> LogWarning;
        public event Action<string> LogError;
        public event Action<string> LogSuccess;

        public LR4_2912Sequence(ITestGridService grid, Dispatcher dispatcher, Window owner = null)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _owner = owner;
        }

        public async Task<bool> RunAsync(TestSequenceContext context)
        {
            try
            {
                int channelIndex = context.ChannelIndex;
                string sn = context.SN;
                CancellationToken ct = context.CancellationToken;

                List<TestStepItem> steps = BuildSteps(context, channelIndex, sn);
                bool allPassed = true;

                for (int i = 0; i < steps.Count; i++)
                {
                    TestStepItem step = steps[i];
                    bool pass = await RunStepAsync(
                        context,
                        channelIndex,
                        step.Action,
                        step.Name,
                        step.RowIndex,
                        ct,
                        i + 1,
                        steps.Count,
                        step.MaxRetries);

                    if (!pass)
                    {
                        allPassed = false;
                        if (context.StopOnFail)
                            break;
                    }
                }

                return allPassed;
            }
            catch (Exception ex)
            {
                OnError($"LR4-2912-0D 测试错误，错误类型：{ex.GetType().Name}；错误信息：{ex.Message}");
                return false;
            }
        }

        private List<TestStepItem> BuildSteps(TestSequenceContext context, int channelIndex, string sn)
        {
            var steps = new List<TestStepItem>();

            // 新版 XML 使用“一个测试项行 + 多通道结果列”的结构：
            // Channel1/2/3 的 SN、电压值、结果都写入同一行的不同通道列。
            steps.Add(new TestStepItem
            {
                Name = $"SN输入-DUT{channelIndex + 1}",
                RowIndex = RowMap.Sn,
                MaxRetries = -1,
                Action = token => SN_Input_LR4(channelIndex, RowMap.Sn, sn, token)
            });

            steps.Add(SharedActionStep("关闭所有继电器", RowMap.CloseAllRelay,
                token => SendRawCommandWithCrcAsync(CommandList.CloseAllRelay_01, 38400, token), context));

            steps.Add(new TestStepItem
            {
                Name = "等待主板下压",
                RowIndex = RowMap.FixtureDown,
                MaxRetries = 0,
                Action = token => ConfirmFixtureDownward_FC_LR4(context, channelIndex, RowMap.FixtureDown, token, openRelay: false)
            });

            // 注意：按你的说明，旧 CSV 的“1号主板_TP3/TP5、2号主板_TP3/TP5、3号主板_TP3/TP5”
            // 只是 1/2/3 通道的重复行。现在 XML 只保留一组测试项，通道结果写到 Channel1/2/3 列。
            steps.Add(VoltageStep(context, channelIndex, "TP5输入电压", "INPUT_TP5", RowMap.InputTp5, 0));
            steps.Add(VoltageStep(context, channelIndex, "TP3输入电压", "INPUT_TP3", RowMap.InputTp3, 1));

            steps.Add(SharedActionStep("开启电机继电器06-08", RowMap.OpenMotorRelay,
                token => SendRelayRangeAsync(6, true, 3, token), context));
            steps.Add(SharedWaitStep("等待电机稳定", RowMap.WaitMotorStable, context));
            steps.Add(VoltageStep(context, channelIndex, "电机输出电压", "MOTOR_VOLTAGE", RowMap.MotorVolt, 2));
            steps.Add(SharedActionStep("关闭电机继电器06-08", RowMap.CloseMotorRelay,
                token => SendRelayRangeAsync(6, false, 3, token), context));

            steps.Add(SharedActionStep("开启1.65V输入继电器09-11", RowMap.OpenApply165Relay,
                token => SendRelayRangeAsync(9, true, 3, token), context));
            steps.Add(SharedWaitStep("等待1.65V输入稳定", RowMap.WaitApply165Stable, context));
            steps.Add(VoltageStep(context, channelIndex, "Pin7电压_Apply1.65V", "APPLY165_PIN7", RowMap.Apply165Pin7, 3));
            steps.Add(VoltageStep(context, channelIndex, "TP8电压_Apply1.65V", "APPLY165_TP8", RowMap.Apply165Tp8, 4));
            steps.Add(SharedActionStep("关闭11号继电器，准备1.66V输入", RowMap.CloseRelay11For166,
                token => SendRelayRangeAsync(11, false, 1, token), context));
            steps.Add(SharedActionStep("开启12号继电器，准备1.66V输入", RowMap.OpenRelay12For166,
                token => SendRelayRangeAsync(12, true, 1, token), context));
            steps.Add(SharedWaitStep("等待1.66V输入稳定", RowMap.WaitApply166Stable, context));
            steps.Add(VoltageStep(context, channelIndex, "Pin7电压_Apply1.66V", "APPLY166_PIN7", RowMap.Apply166Pin7, 3));
            steps.Add(VoltageStep(context, channelIndex, "TP8电压_Apply1.66V", "APPLY166_TP8", RowMap.Apply166Tp8, 4));
            steps.Add(SharedActionStep("关闭09-12号继电器", RowMap.CloseApplyRelays,
                token => SendRelayRangeAsync(9, false, 4, token), context));

            steps.Add(SharedActionStep("开启电磁北继电器01-02", RowMap.OpenMagnetNorthRelay,
                token => SendRelayRangeAsync(1, true, 2, token), context));
            steps.Add(SharedWaitStep("等待电磁北稳定", RowMap.WaitMagnetNorthStable, context));
            steps.Add(VoltageStep(context, channelIndex, "磁性北", "MAGNET_NORTH", RowMap.MagnetNorth, 5));
            steps.Add(SharedActionStep("关闭电磁北继电器01-02", RowMap.CloseMagnetNorthRelay,
                token => SendRelayRangeAsync(1, false, 2, token), context));

            // 保留旧方法 Check_MagneticPole 开头再次关闭 01-02 的动作。
            steps.Add(SharedActionStep("再次确认关闭电磁北继电器01-02", RowMap.CloseMagnetNorthAgain,
                token => SendRelayRangeAsync(1, false, 2, token), context));
            steps.Add(SharedActionStep("开启电磁南继电器03-04", RowMap.OpenMagnetSouthRelay,
                token => SendRelayRangeAsync(3, true, 2, token), context));
            steps.Add(SharedWaitStep("等待电磁南稳定", RowMap.WaitMagnetSouthStable, context));
            steps.Add(VoltageStep(context, channelIndex, "磁性南", "MAGNET_SOUTH", RowMap.MagnetSouth, 5));
            steps.Add(SharedActionStep("关闭电磁南继电器03-04", RowMap.CloseMagnetSouthRelay,
                token => SendRelayRangeAsync(3, false, 2, token), context));

            steps.Add(SharedWaitStep("等待无磁性稳定", RowMap.WaitNoMagnetStable, context));
            steps.Add(VoltageStep(context, channelIndex, "无磁性", "NO_MAGNET", RowMap.NoMagnet, 5));

            return steps;
        }

        private TestStepItem VoltageStep(TestSequenceContext context, int channelIndex, string name, string groupName, int rowIndex, int voltageIndex)
        {
            return new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                MaxRetries = -1,
                Action = token => MeasureSharedVoltageStep_LR4(context, channelIndex, rowIndex, groupName, voltageIndex, token)
            };
        }

        private TestStepItem SharedActionStep(string name, int rowIndex, Func<CancellationToken, Task<bool>> action, TestSequenceContext context)
        {
            return new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                MaxRetries = -1,
                Action = token => RunSharedActionStep_LR4(context, rowIndex, "ACTION_" + rowIndex, action, token)
            };
        }

        private TestStepItem SharedWaitStep(string name, int rowIndex, TestSequenceContext context)
        {
            return new TestStepItem
            {
                Name = name,
                RowIndex = rowIndex,
                MaxRetries = -1,
                Action = token => RunSharedWaitStep_LR4(context, rowIndex, "WAIT_" + rowIndex, token)
            };
        }

        private async Task<bool> RunStepAsync(
            TestSequenceContext context,
            int channelIndex,
            Func<CancellationToken, Task<bool>> stepAction,
            string stepName,
            int rowIndex,
            CancellationToken cancellationToken,
            int currentStep,
            int totalSteps,
            int maxRetries)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            bool finalResult = false;
            // 执行到当前步骤时自动滚动到当前行。
            await _grid.ScrollToRowAsync(rowIndex, 150);
            int effectiveRetries = maxRetries >= 0 ? maxRetries : Math.Max(0, context.FailRetryCount);
            
            for (int attempt = 0; attempt <= effectiveRetries; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (attempt > 0)
                    OnWarning($"[通道{channelIndex + 1}] 步骤 '{stepName}' 第 {attempt} 次自动重试...");

                try
                {
                    OnInfo($"[通道{channelIndex + 1}] 开始执行步骤 {currentStep}/{totalSteps}: {stepName}");
                    finalResult = await stepAction(cancellationToken);
                    if (finalResult)
                        break;
                }
                catch (OperationCanceledException)
                {
                    OnWarning($"[通道{channelIndex + 1}] 步骤 '{stepName}' 已取消");
                    finalResult = false;
                    break;
                }
                catch (Exception ex)
                {
                    OnError($"[通道{channelIndex + 1}] 步骤 '{stepName}' 异常: {ex.Message}");
                    finalResult = false;
                }
            }

            stopwatch.Stop();
            await _grid.SetExecTimeAsync(rowIndex, stopwatch.ElapsedMilliseconds);

            if (finalResult)
                OnSuccess($"[通道{channelIndex + 1}] 步骤 '{stepName}' 通过 (耗时 {stopwatch.ElapsedMilliseconds} ms)");
            else
                OnError($"[通道{channelIndex + 1}] 步骤 '{stepName}' 失败 (耗时 {stopwatch.ElapsedMilliseconds} ms)");

            return finalResult;
        }

        private async Task<bool> SN_Input_LR4(int channelIndex, int rowIndex, string sn, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await _grid.IsRowSelectedAsync(rowIndex))
            {
                await _grid.SetValueAndResultAsync(channelIndex, rowIndex, "Skip", true);
                return true;
            }

            if (string.IsNullOrWhiteSpace(sn))
            {
                await _grid.SetValueAndResultAsync(channelIndex, rowIndex, string.Empty, false);
                OnError($"通道 {channelIndex + 1} SN为空");
                return false;
            }

            await _grid.SetValueAndResultAsync(channelIndex, rowIndex, sn, true);
            return true;
        }

        private async Task<bool> ConfirmFixtureDownward_FC_LR4(
            TestSequenceContext context,
            int channelIndex,
            int rowIndex,
            CancellationToken cancellationToken,
            bool openRelay)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await _grid.IsRowSelectedAsync(rowIndex))
            {
                await _grid.SetValueAndResultAsync(channelIndex, rowIndex, "Skip", true);
                return true;
            }

            FixtureDownWaitSession session = JoinOrCreateFixtureDownSession(
                channelIndex,
                rowIndex,
                openRelay,
                GetExpectedParticipants(context));

            await _grid.SetValueAsync(channelIndex, rowIndex, "等待01");

            bool isPressed = await WaitSharedSessionForCallerAsync(session, cancellationToken);
            await _grid.SetValueAndResultAsync(channelIndex, rowIndex, isPressed ? "01" : "00", isPressed);
            return isPressed;
        }

        private FixtureDownWaitSession JoinOrCreateFixtureDownSession(int channelIndex, int rowIndex, bool openRelay, int expectedParticipants)
        {
            lock (FixtureDownSessionLock)
            {
                if (ActiveFixtureDownSession == null || ActiveFixtureDownSession.IsCompleted)
                {
                    ActiveFixtureDownSession = new FixtureDownWaitSession
                    {
                        ExpectedParticipants = expectedParticipants
                    };
                    ActiveFixtureDownSession.AddParticipant(channelIndex, rowIndex);
                    ActiveFixtureDownSession.WaitTask = RunSharedFixtureDownWindowAsync(ActiveFixtureDownSession, openRelay);
                    OnInfo("LR4 治具下压共享检测已启动，等待串口返回 01...");
                }
                else
                {
                    ActiveFixtureDownSession.AddParticipant(channelIndex, rowIndex);
                    OnInfo($"通道 {channelIndex + 1} 加入治具下压共享检测。");
                }

                return ActiveFixtureDownSession;
            }
        }

        private async Task<bool> WaitSharedSessionForCallerAsync(FixtureDownWaitSession session, CancellationToken cancellationToken)
        {
            Task cancelTask = Task.Delay(Timeout.Infinite, cancellationToken);
            Task completedTask = await Task.WhenAny(session.WaitTask, cancelTask);
            if (completedTask == cancelTask)
                throw new OperationCanceledException(cancellationToken);
            return await session.WaitTask;
        }

        private async Task<bool> RunSharedFixtureDownWindowAsync(FixtureDownWaitSession session, bool openRelay)
        {
            try
            {
                if (openRelay)
                    await SendRawCommandWithCrcAsync(CommandList.CloseAllRelay_01, 38400, CancellationToken.None);

                Func<CancellationToken, Task<bool>> detectAsync = token => WaitForFixtureDown_Shared_LR4Async(session, token);

                bool isPressed = await _dispatcher.InvokeAsync(() =>
                {
                    var waitWindow = new FixtureDownWindow(detectAsync);
                    if (_owner != null)
                        waitWindow.Owner = _owner;
                    return waitWindow.ShowDialog() == true;
                });

                await SetAllFixtureParticipantsResultAsync(session, isPressed);
                return isPressed;
            }
            catch (Exception ex)
            {
                OnError($"LR4 共享治具下压检测异常：{ex.Message}");
                await SetAllFixtureParticipantsResultAsync(session, false);
                return false;
            }
            finally
            {
                lock (FixtureDownSessionLock)
                {
                    if (ReferenceEquals(ActiveFixtureDownSession, session))
                        ActiveFixtureDownSession = null;
                }
            }
        }

        private async Task<bool> WaitForFixtureDown_Shared_LR4Async(FixtureDownWaitSession session, CancellationToken cancellationToken)
        {
            byte[] command = { 0x01, 0x04, 0x00, 0x00, 0x00, 0x02, 0x71, 0xCB };

            if (string.IsNullOrEmpty(ComName.rs485ComName))
            {
                OnError("RS485串口未配置，无法检测治具下压");
                return false;
            }

            await WaitForExpectedFixtureParticipantsAsync(session, cancellationToken);

            await SerialAccessLock.WaitAsync(cancellationToken);
            try
            {
                using (SerialPort port = new SerialPort(ComName.rs485ComName, 38400, Parity.None, 8, StopBits.One))
                {
                    port.Open();
                    Stopwatch stopwatch = Stopwatch.StartNew();
                    TimeSpan timeout = TimeSpan.FromSeconds(60);

                    while (stopwatch.Elapsed < timeout)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        port.DiscardInBuffer();
                        port.Write(command, 0, command.Length);
                        OnInfo($"发送下压检测命令: {BytesToHex(command)}");

                        await Task.Delay(100, cancellationToken);

                        if (port.BytesToRead > 0)
                        {
                            byte[] buffer = new byte[port.BytesToRead];
                            port.Read(buffer, 0, buffer.Length);
                            string hexResponse = BytesToHex(buffer);
                            OnInfo($"收到下压检测响应: {hexResponse}");

                            if (buffer.Length > 4)
                            {
                                string displayValue = buffer[4].ToString("X2");
                                await SetAllFixtureParticipantsValueAsync(session, displayValue);
                                if (buffer[4] == 0x01)
                                    return true;
                            }
                        }

                        await Task.Delay(500, cancellationToken);
                    }

                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                OnWarning("LR4 治具下压共享检测已取消");
                return false;
            }
            catch (Exception ex)
            {
                OnError($"治具下压串口错误：{ex.Message}");
                return false;
            }
            finally
            {
                SerialAccessLock.Release();
            }
        }

        private async Task<bool> RunSharedActionStep_LR4(
            TestSequenceContext context,
            int rowIndex,
            string key,
            Func<CancellationToken, Task<bool>> action,
            CancellationToken cancellationToken)
        {
            SharedStepSession session = JoinOrCreateSharedStepSession(
                key,
                context.ChannelIndex,
                rowIndex,
                GetExpectedParticipants(context),
                s => RunSharedActionSessionAsync(s, rowIndex, action, cancellationToken));

            await _grid.SetValueAsync(context.ChannelIndex, rowIndex, "等待执行");
            return await WaitSharedStepForCallerAsync(session, context.ChannelIndex, rowIndex, cancellationToken);
        }

        private async Task<bool> RunSharedActionSessionAsync(
            SharedStepSession session,
            int rowIndex,
            Func<CancellationToken, Task<bool>> action,
            CancellationToken cancellationToken)
        {
            bool pass = false;
            try
            {
                await WaitForExpectedSharedParticipantsAsync(session, cancellationToken);

                if (!await _grid.IsRowSelectedAsync(rowIndex))
                {
                    await SetAllSharedParticipantsValueResultAsync(session, "Skip", true);
                    pass = true;
                }
                else
                {
                    pass = await action(cancellationToken);
                    await SetAllSharedParticipantsValueResultAsync(session, pass ? "OK" : "FAIL", pass);
                }

                foreach (SharedStepParticipant p in session.GetParticipantsSnapshot())
                    session.SetParticipantResult(p.ChannelIndex, p.RowIndex, pass);
                return pass;
            }
            catch (Exception ex)
            {
                OnError($"共享动作步骤异常：{ex.Message}");
                await SetAllSharedParticipantsValueResultAsync(session, "异常", false);
                foreach (SharedStepParticipant p in session.GetParticipantsSnapshot())
                    session.SetParticipantResult(p.ChannelIndex, p.RowIndex, false);
                return false;
            }
            finally
            {
                RemoveSharedSession(session);
            }
        }

        private async Task<bool> RunSharedWaitStep_LR4(TestSequenceContext context, int rowIndex, string key, CancellationToken cancellationToken)
        {
            SharedStepSession session = JoinOrCreateSharedStepSession(
                key,
                context.ChannelIndex,
                rowIndex,
                GetExpectedParticipants(context),
                s => RunSharedWaitSessionAsync(s, rowIndex, cancellationToken));

            await _grid.SetValueAsync(context.ChannelIndex, rowIndex, "等待");
            return await WaitSharedStepForCallerAsync(session, context.ChannelIndex, rowIndex, cancellationToken);
        }

        private async Task<bool> RunSharedWaitSessionAsync(SharedStepSession session, int rowIndex, CancellationToken cancellationToken)
        {
            bool pass = false;
            try
            {
                await WaitForExpectedSharedParticipantsAsync(session, cancellationToken);

                if (!await _grid.IsRowSelectedAsync(rowIndex))
                {
                    await SetAllSharedParticipantsValueResultAsync(session, "Skip", true);
                    pass = true;
                }
                else
                {
                    double milliseconds = await GetRowNumberAsync(rowIndex);
                    if (milliseconds <= 0)
                    {
                        OnError($"第 {rowIndex + 1} 行等待时间无效，请在上限或下限填写毫秒数。");
                        await SetAllSharedParticipantsValueResultAsync(session, "时间错误", false);
                        pass = false;
                    }
                    else
                    {
                        double seconds = milliseconds / 1000.0;
                        string rowName = await GetRowNameAsync(rowIndex);
                        string message = string.IsNullOrWhiteSpace(rowName) ? "请等待延时完成..." : rowName;
                        OnInfo($"读取到等待时间：{milliseconds} ms → {seconds:F2} 秒");

                        Task waitTask = await _dispatcher.InvokeAsync(() =>
                            WaitDialog.WaitOrThrowAsync(message, seconds, _owner, cancellationToken));
                        await waitTask;

                        await SetAllSharedParticipantsValueResultAsync(session, $"{seconds:F2} 秒", true);
                        pass = true;
                    }
                }

                foreach (SharedStepParticipant p in session.GetParticipantsSnapshot())
                    session.SetParticipantResult(p.ChannelIndex, p.RowIndex, pass);
                return pass;
            }
            catch (OperationCanceledException)
            {
                await SetAllSharedParticipantsValueResultAsync(session, "取消", false);
                foreach (SharedStepParticipant p in session.GetParticipantsSnapshot())
                    session.SetParticipantResult(p.ChannelIndex, p.RowIndex, false);
                return false;
            }
            catch (Exception ex)
            {
                OnError($"共享等待步骤异常：{ex.Message}");
                await SetAllSharedParticipantsValueResultAsync(session, "异常", false);
                foreach (SharedStepParticipant p in session.GetParticipantsSnapshot())
                    session.SetParticipantResult(p.ChannelIndex, p.RowIndex, false);
                return false;
            }
            finally
            {
                RemoveSharedSession(session);
            }
        }

        private async Task<bool> MeasureSharedVoltageStep_LR4(
            TestSequenceContext context,
            int channelIndex,
            int rowIndex,
            string groupName,
            int voltageIndex,
            CancellationToken cancellationToken)
        {
            SharedStepSession session = JoinOrCreateSharedStepSession(
                "MEASURE_" + groupName,
                channelIndex,
                rowIndex,
                GetExpectedParticipants(context),
                s => RunSharedVoltageMeasurementSessionAsync(s, voltageIndex, cancellationToken));

            await _grid.SetValueAsync(channelIndex, rowIndex, "排队读取");
            return await WaitSharedStepForCallerAsync(session, channelIndex, rowIndex, cancellationToken);
        }

        private async Task<bool> RunSharedVoltageMeasurementSessionAsync(
            SharedStepSession session,
            int voltageIndex,
            CancellationToken cancellationToken)
        {
            bool allPass = true;
            try
            {
                await WaitForExpectedSharedParticipantsAsync(session, cancellationToken);
                List<SharedStepParticipant> participants = session.GetParticipantsSnapshot()
                    .OrderBy(p => p.ChannelIndex)
                    .ToList();

                foreach (SharedStepParticipant participant in participants)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    bool pass;
                    if (!await _grid.IsRowSelectedAsync(participant.RowIndex))
                    {
                        await _grid.SetValueAndResultAsync(participant.ChannelIndex, participant.RowIndex, "Skip", true);
                        pass = true;
                    }
                    else
                    {
                        List<double> voltages = await ReadVoltagesForChannelAsync(participant.ChannelIndex, cancellationToken);
                        pass = await JudgeVoltageAndUpdateAsync(participant.ChannelIndex, participant.RowIndex, voltages, voltageIndex);
                    }

                    session.SetParticipantResult(participant.ChannelIndex, participant.RowIndex, pass);
                    if (!pass)
                        allPass = false;
                }

                return allPass;
            }
            catch (Exception ex)
            {
                OnError($"共享电压读取异常：{ex.Message}");
                foreach (SharedStepParticipant p in session.GetParticipantsSnapshot())
                {
                    session.SetParticipantResult(p.ChannelIndex, p.RowIndex, false);
                    await _grid.SetValueAndResultAsync(p.ChannelIndex, p.RowIndex, "异常", false);
                }
                return false;
            }
            finally
            {
                RemoveSharedSession(session);
            }
        }

        private SharedStepSession JoinOrCreateSharedStepSession(
            string key,
            int channelIndex,
            int rowIndex,
            int expectedParticipants,
            Func<SharedStepSession, Task<bool>> taskFactory)
        {
            lock (SharedStepSessionLock)
            {
                if (!SharedStepSessions.TryGetValue(key, out SharedStepSession session) || session == null || session.IsCompleted)
                {
                    session = new SharedStepSession
                    {
                        Key = key,
                        ExpectedParticipants = expectedParticipants
                    };
                    session.AddParticipant(channelIndex, rowIndex);
                    session.WaitTask = taskFactory(session);
                    SharedStepSessions[key] = session;
                    OnInfo($"共享步骤启动：{key}");
                }
                else
                {
                    session.AddParticipant(channelIndex, rowIndex);
                    OnInfo($"通道 {channelIndex + 1} 加入共享步骤：{key}");
                }

                return session;
            }
        }

        private async Task WaitForExpectedFixtureParticipantsAsync(FixtureDownWaitSession session, CancellationToken cancellationToken)
        {
            Stopwatch sw = Stopwatch.StartNew();
            TimeSpan maxWait = TimeSpan.FromMilliseconds(1000);
            while (sw.Elapsed < maxWait)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (session.ParticipantCount >= session.ExpectedParticipants)
                    return;
                await Task.Delay(50, cancellationToken);
            }
        }

        private async Task WaitForExpectedSharedParticipantsAsync(SharedStepSession session, CancellationToken cancellationToken)
        {
            Stopwatch sw = Stopwatch.StartNew();
            TimeSpan maxWait = TimeSpan.FromMilliseconds(1000);
            while (sw.Elapsed < maxWait)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (session.ParticipantCount >= session.ExpectedParticipants)
                    return;
                await Task.Delay(50, cancellationToken);
            }
        }

        private async Task<bool> WaitSharedStepForCallerAsync(SharedStepSession session, int channelIndex, int rowIndex, CancellationToken cancellationToken)
        {
            Task cancelTask = Task.Delay(Timeout.Infinite, cancellationToken);
            Task completedTask = await Task.WhenAny(session.WaitTask, cancelTask);
            if (completedTask == cancelTask)
                throw new OperationCanceledException(cancellationToken);
            await session.WaitTask;
            return session.GetParticipantResult(channelIndex, rowIndex);
        }

        private void RemoveSharedSession(SharedStepSession session)
        {
            lock (SharedStepSessionLock)
            {
                if (SharedStepSessions.TryGetValue(session.Key, out SharedStepSession current) && ReferenceEquals(current, session))
                    SharedStepSessions.Remove(session.Key);
            }
        }

        private async Task<List<double>> ReadVoltagesForChannelAsync(int channelIndex, CancellationToken cancellationToken)
        {
            byte[] command = GetVoltageCommandForChannel(channelIndex);

            await SerialAccessLock.WaitAsync(cancellationToken);
            try
            {
                OnInfo($"通道 {channelIndex + 1} 开始读取电压，RS485 串口已锁定。");
                List<double> voltages = await RelayController.ReadModbusRegistersAsync(
                    ComName.rs485ComName,
                    command,
                    9600,
                    3000,
                    msg => OnInfo(msg));
                return voltages ?? new List<double>();
            }
            finally
            {
                SerialAccessLock.Release();
            }
        }

        private async Task<bool> JudgeVoltageAndUpdateAsync(int channelIndex, int rowIndex, List<double> voltages, int voltageIndex)
        {
            var limit = await _grid.GetLimitsAsync(rowIndex);
            if (!limit.IsValid)
            {
                await _grid.SetValueAndResultAsync(channelIndex, rowIndex, "限值错误", false);
                OnWarning(limit.ErrorMessage);
                return false;
            }

            if (voltages == null || voltages.Count == 0)
            {
                await _grid.SetValueAndResultAsync(channelIndex, rowIndex, "无响应", false);
                return false;
            }

            if (voltageIndex < 0 || voltageIndex >= voltages.Count)
            {
                await _grid.SetValueAndResultAsync(channelIndex, rowIndex, "索引错误", false);
                OnError($"电压索引 {voltageIndex} 超出范围，当前返回 {voltages.Count} 个值。");
                return false;
            }

            double voltage = voltages[voltageIndex];
            bool pass = voltage >= limit.Lower && voltage <= limit.Upper;
            await _grid.SetValueAndResultAsync(channelIndex, rowIndex, voltage.ToString("F3") + " V", pass);
            return pass;
        }

        private byte[] GetVoltageCommandForChannel(int channelIndex)
        {
            switch (channelIndex)
            {
                case 0:
                    return CommandList.Get01Volt_16;
                case 1:
                    return CommandList.Read16_02Volt;
                case 2:
                    return CommandList.Get03Volt_16;
                default:
                    throw new ArgumentOutOfRangeException(nameof(channelIndex), $"LR4 只支持 1~3 通道，当前 channelIndex={channelIndex}");
            }
        }

        private async Task<bool> SendRelayRangeAsync(int relayIndex, bool isOpen, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await SerialAccessLock.WaitAsync(cancellationToken);
            try
            {
                string response = await RelayController.SendCommandAsync(
                    address: 1,
                    relayIndex: relayIndex,
                    isOpen: isOpen,
                    count: count,
                    baudRate: 38400,
                    comPort: ComName.rs485ComName,
                    logAction: msg => OnInfo(msg));

                bool pass = !string.IsNullOrWhiteSpace(response) &&
                            !response.StartsWith("错误", StringComparison.OrdinalIgnoreCase) &&
                            !response.Contains("TIMEOUT");
                return pass;
            }
            finally
            {
                SerialAccessLock.Release();
            }
        }

        private async Task<bool> SendRawCommandWithCrcAsync(byte[] command, int baudRate, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await SerialAccessLock.WaitAsync(cancellationToken);
            try
            {
                string response = await RelayController.SendCommandWithCrcAsync(
                    command,
                    baudRate,
                    ComName.rs485ComName,
                    1000,
                    msg => OnInfo(msg));

                return !string.IsNullOrWhiteSpace(response);
            }
            finally
            {
                SerialAccessLock.Release();
            }
        }

        private async Task SetAllFixtureParticipantsValueAsync(FixtureDownWaitSession session, string value)
        {
            foreach (FixtureDownParticipant p in session.GetParticipantsSnapshot())
                await _grid.SetValueAsync(p.ChannelIndex, p.RowIndex, value);
        }

        private async Task SetAllFixtureParticipantsResultAsync(FixtureDownWaitSession session, bool pass)
        {
            foreach (FixtureDownParticipant p in session.GetParticipantsSnapshot())
                await _grid.SetValueAndResultAsync(p.ChannelIndex, p.RowIndex, pass ? "01" : "00", pass);
        }

        private async Task SetAllSharedParticipantsValueResultAsync(SharedStepSession session, string value, bool pass)
        {
            foreach (SharedStepParticipant p in session.GetParticipantsSnapshot())
                await _grid.SetValueAndResultAsync(p.ChannelIndex, p.RowIndex, value, pass);
        }

        private int GetExpectedParticipants(TestSequenceContext context)
        {
            int count = context.ParallelTestCount;
            if (count < 1) count = 1;
            if (count > 3) count = 3;
            return count;
        }

        private async Task<double> GetRowNumberAsync(int rowIndex)
        {
            return await _dispatcher.InvokeAsync(() =>
            {
                DataTable dt = ProjectSettings.testDataTable;
                if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count)
                    return 0.0;

                string upper = dt.Rows[rowIndex]["UpperLimit"]?.ToString()?.Trim();
                string lower = dt.Rows[rowIndex]["LowerLimit"]?.ToString()?.Trim();

                if (!string.IsNullOrWhiteSpace(upper) && double.TryParse(upper, out double upperVal))
                    return upperVal;
                if (!string.IsNullOrWhiteSpace(lower) && double.TryParse(lower, out double lowerVal))
                    return lowerVal;
                return 0.0;
            });
        }

        private async Task<string> GetRowNameAsync(int rowIndex)
        {
            return await _dispatcher.InvokeAsync(() =>
            {
                DataTable dt = ProjectSettings.testDataTable;
                if (dt == null || rowIndex < 0 || rowIndex >= dt.Rows.Count)
                    return string.Empty;
                return dt.Rows[rowIndex]["TestItem"]?.ToString() ?? string.Empty;
            });
        }

        private string BytesToHex(byte[] bytes)
        {
            return bytes == null || bytes.Length == 0
                ? string.Empty
                : BitConverter.ToString(bytes).Replace("-", " ");
        }

        private void OnInfo(string message) => LogInfo?.Invoke(message);
        private void OnWarning(string message) => LogWarning?.Invoke(message);
        private void OnError(string message) => LogError?.Invoke(message);
        private void OnSuccess(string message) => LogSuccess?.Invoke(message);

        private class TestStepItem
        {
            public Func<CancellationToken, Task<bool>> Action { get; set; }
            public string Name { get; set; }
            public int RowIndex { get; set; }
            public int MaxRetries { get; set; }
        }

        private static class RowMap
        {
            public const int Sn = 0;
            public const int CloseAllRelay = 1;
            public const int FixtureDown = 2;
            public const int InputTp5 = 3;
            public const int InputTp3 = 4;
            public const int OpenMotorRelay = 5;
            public const int WaitMotorStable = 6;
            public const int MotorVolt = 7;
            public const int CloseMotorRelay = 8;
            public const int OpenApply165Relay = 9;
            public const int WaitApply165Stable = 10;
            public const int Apply165Pin7 = 11;
            public const int Apply165Tp8 = 12;
            public const int CloseRelay11For166 = 13;
            public const int OpenRelay12For166 = 14;
            public const int WaitApply166Stable = 15;
            public const int Apply166Pin7 = 16;
            public const int Apply166Tp8 = 17;
            public const int CloseApplyRelays = 18;
            public const int OpenMagnetNorthRelay = 19;
            public const int WaitMagnetNorthStable = 20;
            public const int MagnetNorth = 21;
            public const int CloseMagnetNorthRelay = 22;
            public const int CloseMagnetNorthAgain = 23;
            public const int OpenMagnetSouthRelay = 24;
            public const int WaitMagnetSouthStable = 25;
            public const int MagnetSouth = 26;
            public const int CloseMagnetSouthRelay = 27;
            public const int WaitNoMagnetStable = 28;
            public const int NoMagnet = 29;
        }

        private class FixtureDownParticipant
        {
            public int ChannelIndex { get; set; }
            public int RowIndex { get; set; }
        }

        private class FixtureDownWaitSession
        {
            private readonly object _participantLock = new object();
            private readonly List<FixtureDownParticipant> _participants = new List<FixtureDownParticipant>();

            public Task<bool> WaitTask { get; set; }
            public int ExpectedParticipants { get; set; } = 1;

            public bool IsCompleted => WaitTask != null && WaitTask.IsCompleted;

            public int ParticipantCount
            {
                get
                {
                    lock (_participantLock)
                    {
                        return _participants.Count;
                    }
                }
            }

            public void AddParticipant(int channelIndex, int rowIndex)
            {
                lock (_participantLock)
                {
                    if (!_participants.Any(x => x.ChannelIndex == channelIndex && x.RowIndex == rowIndex))
                        _participants.Add(new FixtureDownParticipant { ChannelIndex = channelIndex, RowIndex = rowIndex });
                }
            }

            public List<FixtureDownParticipant> GetParticipantsSnapshot()
            {
                lock (_participantLock)
                {
                    return _participants.ToList();
                }
            }
        }

        private class SharedStepParticipant
        {
            public int ChannelIndex { get; set; }
            public int RowIndex { get; set; }
        }

        private class SharedStepSession
        {
            private readonly object _lock = new object();
            private readonly List<SharedStepParticipant> _participants = new List<SharedStepParticipant>();
            private readonly Dictionary<string, bool> _participantResults = new Dictionary<string, bool>();

            public string Key { get; set; }
            public int ExpectedParticipants { get; set; } = 1;
            public Task<bool> WaitTask { get; set; }
            public bool IsCompleted => WaitTask != null && WaitTask.IsCompleted;

            public int ParticipantCount
            {
                get
                {
                    lock (_lock)
                    {
                        return _participants.Count;
                    }
                }
            }

            public void AddParticipant(int channelIndex, int rowIndex)
            {
                lock (_lock)
                {
                    if (!_participants.Any(x => x.ChannelIndex == channelIndex && x.RowIndex == rowIndex))
                        _participants.Add(new SharedStepParticipant { ChannelIndex = channelIndex, RowIndex = rowIndex });
                }
            }

            public List<SharedStepParticipant> GetParticipantsSnapshot()
            {
                lock (_lock)
                {
                    return _participants.ToList();
                }
            }

            public void SetParticipantResult(int channelIndex, int rowIndex, bool result)
            {
                lock (_lock)
                    _participantResults[BuildResultKey(channelIndex, rowIndex)] = result;
            }

            public bool GetParticipantResult(int channelIndex, int rowIndex)
            {
                lock (_lock)
                {
                    string key = BuildResultKey(channelIndex, rowIndex);
                    return _participantResults.TryGetValue(key, out bool result) && result;
                }
            }

            private static string BuildResultKey(int channelIndex, int rowIndex)
            {
                return channelIndex + ":" + rowIndex;
            }
        }
    }
}
