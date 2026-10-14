using System.Collections.Generic;

namespace TestPlatform.TestSequences
{
    public abstract partial class SKBaseSequence
    {
        protected void AddInitializingToolsStep(
            List<TestStepItem> steps,
            SK441Device deviceManager,
            string boardName)
        {
            steps.Add(new TestStepItem
            {
                Name = "7.1 仪器在线检查",
                RowIndex = steps.Count,
                Action = async token =>
                {
                    OnLogInfo($"正在执行 {boardName} 仪器在线检查 (Chapter 7.1)...");
                    bool ready = await deviceManager.CheckRequiredInstrumentsAsync();
                    return new StepResult
                    {
                        IsPass = ready,
                        Value = ready ? "Ready" : "Check Fail"
                    };
                }
            });

            steps.Add(new TestStepItem
            {
                Name = "7.2 关闭所有仪器输出",
                RowIndex = steps.Count,
                Action = async token =>
                {
                    OnLogInfo($"正在执行 {boardName} 关闭所有仪器输出 (Chapter 7.2)...");
                    bool passed = await deviceManager.TurnOffAllInstrumentsAsync();
                    return new StepResult
                    {
                        IsPass = passed,
                        Value = passed ? "Off OK" : "Off Fail"
                    };
                }
            });

            steps.Add(new TestStepItem
            {
                Name = boardName == "BCM-125"
                    ? "7.3 断开测试继电器并恢复电子负载Y2/Y5"
                    : "7.3 断开测试继电器（保持Y1/Y2）",
                RowIndex = steps.Count,
                Action = async token =>
                {
                    OnLogInfo($"正在执行 {boardName} 测试继电器复位 (Chapter 7.3)...");
                    bool placementPassed = await deviceManager.OpenPlacementDetectionRelaysAsync();
                    bool relaysPassed = await deviceManager.OpenAllRelaysAsync();
                    bool loadPathPassed = true;
                    if (placementPassed && relaysPassed && boardName == "BCM-125")
                    {
                        // The precheck powers the load through station-3 Y2/Y5, but the
                        // all-relay reset above opens them again. Restore that path while
                        // every source is still off so CH25 can use the load terminals.
                        loadPathPassed = await deviceManager.EnableBcmElectronicLoadSupplyAsync();
                    }
                    bool passed = placementPassed && relaysPassed && loadPathPassed;
                    if (!passed)
                        OnLogError("测试继电器或万用表继电器复位失败！");
                    return new StepResult
                    {
                        IsPass = passed,
                        Value = passed
                            ? boardName == "BCM-125" ? "Relay Reset / Load Path Ready" : "Relay Open OK"
                            : "Relay Fail"
                    };
                }
            });
        }
    }
}
