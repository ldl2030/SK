# Repository Guidelines

## Project Structure & Module Organization

This repository contains a WPF test platform targeting .NET Framework 4.7.2. The main solution and project files are `TestPlatform.sln` and `TestPlatform.csproj`. Application windows live as paired `.xaml` and `.xaml.cs` files in the repository root, with `MainWindow.xaml.cs` coordinating the primary workflow.

Hardware and test-flow code is also in the root: `SKSequences.cs` defines SK/BCM/MPS sequence logic, `SK441Device.cs` aggregates device control, and instrument drivers include `RelayController.cs`, `DigitalInputController.cs`, `DaqMultimeter.cs`, `AnsPowerSupply.cs`, `HengHuiPowerSupply.cs`, and `HengHuiElectronicLoad.cs`. Product and station XML files are under `ProjectConfig/`; copied runtime files appear under `bin/Debug/`.

## Build, Test, and Development Commands

Build with Visual Studio MSBuild:

```powershell
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\Msbuild\Current\Bin\MSBuild.exe' .\TestPlatform.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU" /v:minimal
```

Run locally from Visual Studio, or launch `bin\Debug\TestPlatform.exe` after a successful build. Prefer MSBuild for this legacy WPF project; `dotnet build` may not match the required .NET Framework/XAML toolchain.

## Coding Style & Naming Conventions

Use C# conventions with 4-space indentation. Use `PascalCase` for types, methods, properties, and events; use `camelCase` for locals and parameters. Keep async methods suffixed with `Async`. Keep XAML code-behind names aligned with their views, for example `WaitDialog.xaml` and `WaitDialog.xaml.cs`. When changing sequence logic, keep XML test-step order and code row handling synchronized.

## Testing Guidelines

The solution includes four console regression projects under `Tests/`: Bcm125StructureTests, Bcm250StructureTests, Mps125StructureTests, and Mps250StructureTests. Run them with `TESTPLATFORM_SK_MODE=Simulation` and the repository root as their first argument. They cover XML structure, simulated flows, fault cleanup, and linked shared checks in `Tests/Shared/SkCommonBehaviorTests.cs`. For hardware-sensitive work, first test with communication skipped or simulated where supported, then confirm real ports, relays, digital inputs, and fixture state before enabling physical actions.

## Shared Behavior Across the Four SK Products

When a useful improvement is found in BCM-125, BCM-250, MPS-125, or MPS-250, inspect its applicability to all four products in the same change. Put shared behavior in `Sequences/SK/Common/`, `SKBaseSequence`, or `SK441Device` rather than copying implementations into product sequences. Keep product wiring, limits, protocol commands, and required timing in their own station maps, catalogs, and XML. See `Docs/SK四产品共性规则与推广矩阵.md`.

Use XML names for both the grid and logs, and `SKCompactLoggingEnabled` for the common logging policy. Keep fixture station-1, power station-3, and DAQ relay state separate. Reuse only successfully issued relay command states; invalidate affected states after communication failure and when reinitializing. Changes to shared behavior must build and pass all four regression executables. Timing improvements require measured readiness and a fallback to the original wait; preserve required acquisition windows and ramp timing.

## Commit & Pull Request Guidelines

Git history is not accessible from this working directory, so use concise imperative commit messages such as `Add BCM-125 initialization checks`. Pull requests should include a short summary, affected product configs such as `ProjectConfig/SK/BCM-125/BCM-125_autoTest.xml`, build results, and screenshots or log excerpts for UI and hardware-flow changes.

## Security & Configuration Tips

Do not commit credentials, production-only machine settings, or private network paths. Review `App.config`, serial/COM settings, and project XML copy behavior before deployment. Treat `bin/Debug/ProjectConfig/` as runtime output; update source files under `ProjectConfig/` first, then rebuild or intentionally sync runtime copies for local validation.
