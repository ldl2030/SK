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

No standalone automated test project is present in this checkout. Validate changes by building, running the relevant UI flow, and checking logs. For hardware-sensitive work, first test with communication skipped or simulated where supported, then confirm real ports, relays, digital inputs, and fixture state before enabling physical actions.

## Commit & Pull Request Guidelines

Git history is not accessible from this working directory, so use concise imperative commit messages such as `Add BCM-125 initialization checks`. Pull requests should include a short summary, affected product configs such as `ProjectConfig/SK/BCM-125/BCM-125_autoTest.xml`, build results, and screenshots or log excerpts for UI and hardware-flow changes.

## Security & Configuration Tips

Do not commit credentials, production-only machine settings, or private network paths. Review `App.config`, serial/COM settings, and project XML copy behavior before deployment. Treat `bin/Debug/ProjectConfig/` as runtime output; update source files under `ProjectConfig/` first, then rebuild or intentionally sync runtime copies for local validation.
