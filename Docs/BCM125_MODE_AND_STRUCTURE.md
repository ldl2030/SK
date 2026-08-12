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
- ST1/ST2 shunts: 0.0215909 ohm / 0.0215914 ohm;
- programming RLP4/RLP8: DAQ relays 305/306;
- short-protection selector: DAQ relay 304.

The curated SK/BMS instrument mapping takes precedence over legacy channel
labels in the PDF procedure.

## Offline structure test

Build the solution, then run:

```powershell
& '.\Tests\Bcm125StructureTests\bin\Debug\Bcm125StructureTests.exe' (Get-Location).Path
```

The test does not initialize physical instruments. It verifies all 956
executable steps, StepIds, chapter binding, XML numeric/delay parameters,
dependencies, centralized mapping, five cleanup steps, and proves that
changing an in-memory XML limit or delay changes the simulated action result.
