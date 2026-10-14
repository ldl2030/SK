# BCM-125 execution mode and structure

## Simulation / hardware switch

The single persistent switch is in `App.config`:

```xml
<add key="SKExecutionMode" value="Simulation" />
```

Allowed values:

- `Simulation`: do not connect instruments; use the SK441 device simulator.
- `Hardware`: initialize and use the configured physical instruments.

After changing `App.config`, rebuild the solution so
`bin\Debug\TestPlatform.exe.config` is updated.

For a temporary mode override without editing a file:

```powershell
$env:TESTPLATFORM_SK_MODE = 'Hardware'
.\bin\Debug\TestPlatform.exe
```

Use `Simulation` instead of `Hardware` to force offline mode. The application
logs the selected mode when an SK sequence starts.

## XML-owned parameters

For BCM-125, the resolved test-plan XML is required. Numeric verdicts read
`LowerLimit`, `UpperLimit`, and `Unit` from the XML definition attached to the
StepId. Delay actions read `DelayMs` from XML. Missing values fail with a
`TestPlanConfigurationException`; code literals are not used as runtime
fallbacks.

Each chapter assigns StepIds while its steps are created. Startup validation
then checks:

- executable code/XML step counts;
- unique and existing StepIds;
- expected chapter/group;
- chapter number and measured-result identifier semantics;
- `AlwaysRun` cleanup binding.

## Authoritative station mapping

Physical BCM-125 channels and shunts are centralized in
`Hardware/Stations/SK441/Bcm125StationMap.cs`. Important mappings include:

- VMID reference: DAQ 106;
- ST1/ST2 current shunts: DAQ 113/115;
- ST1/ST2 shunt conversion defaults: 0.0205 ohm / 0.0205 ohm.
  A station may set `SKBCM125String1ShuntOhms` and
  `SKBCM125String2ShuntOhms` in its application configuration after verifying
  current and Kelvin shunt voltage with independent instruments. The configured
  values apply to discharge, charging, verification, CCB, string test, and
  readiness probes together. Restart the application after changing them;
- programming RLP4/RLP8: DAQ relays 305/306;
- short-protection selector: DAQ relay 304.

The curated SK/BMS instrument mapping takes precedence over legacy channel
labels in the PDF procedure.

## Discharge and charging calibration results

Chapters 20 and 23 keep the XML limit verdict separate from numeric
availability. If a measurement or calculated calibration is outside its XML
range but still has a finite number, subsequent calibration calculations use
that number. The sequence writes it to the BCM and sends `ACT->FLASH` once all
required writes succeed. The original out-of-range result remains NG in the
test report. A missing, nonnumeric, or nonfinite calibration value blocks its
write and the corresponding FLASH action; communication/write failures also
block FLASH. This policy prevents an out-of-range gain from producing false
`Missing Input` rows and a chain of skipped writes.

## Offline structure test

Build the solution, then run:

```powershell
& '.\Tests\Bcm125StructureTests\bin\Debug\Bcm125StructureTests.exe' (Get-Location).Path
```

The test does not initialize physical instruments. It verifies all 956
executable steps, StepIds, chapter binding, XML numeric/delay parameters,
dependencies, centralized mapping, five cleanup steps, and proves that
changing an in-memory XML limit or delay changes the simulated action result.
