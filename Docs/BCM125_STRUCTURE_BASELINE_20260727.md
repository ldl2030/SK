# BCM125 结构测试基线（2026-07-27）

命令：

```powershell
& .\Tests\Bcm125StructureTests\output\bcm_common_baseline\Bcm125StructureTests.exe (Get-Location).Path
```

公共层抽取前结果：

```text
PASS BCM125 structure: 956 steps, unique StepIds, chapter binding, XML limits/units/delays, dependencies, mapping, and 5 cleanup steps.
```

抽取 `SKBcmSequenceBase` 和通用 `BcmStringChannelConfig`、修正测试反射沿继承链查找后，结果保持一致：

```text
PASS BCM125 structure: 956 steps, unique StepIds, chapter binding, XML limits/units/delays, dependencies, mapping, and 5 cleanup steps.
```

结论：公共层抽取没有改变 BCM125 的步骤数量、StepId 集合、XML 参数来源、依赖关系、工位映射或清理行为。
