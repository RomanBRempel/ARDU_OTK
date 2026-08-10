<!-- agent-ninja-START -->
## Agent Skills

> **IMPORTANT**: Prefer skill-led reasoning over pre-training-led reasoning.
> See [Agent Skills](.github/skills/README.md) before working on tasks covered by these skills.

<!-- agent-ninja-END -->

## Project Overview

- Windows desktop app on WinUI 3 + .NET 10 (`ARDU_OTK/`).
- Deployment model is **unpackaged** (`WindowsPackageType=None`) and **self-contained**.
- Auto-updates are delivered by Velopack from GitHub Releases in this repository.

Read first:
- [README.md](README.md)
- [ARDU_OTK/ARDU_OTK.csproj](ARDU_OTK/ARDU_OTK.csproj)
- [ARDU_OTK/Services/UpdateService.cs](ARDU_OTK/Services/UpdateService.cs)
- [ARDU_OTK/Services/Store/AppPaths.cs](ARDU_OTK/Services/Store/AppPaths.cs)
- [release workflow](.github/workflows/release.yml)

## Build And Release Commands

Run from repository root.

- Restore/build:
	- `dotnet restore ARDU_OTK/ARDU_OTK.csproj`
	- `dotnet build ARDU_OTK/ARDU_OTK.csproj -c Debug -r win-x64`
- Local publish (installer input):
	- `dotnet publish ARDU_OTK/ARDU_OTK.csproj -c Release -r win-x64 -p:Version=0.1.0 -o publish`
- Velopack pack locally:
	- `dotnet tool install -g vpk --version 1.2.0`
	- `vpk pack --packId ARDU_OTK --packVersion 0.1.0 --packDir publish --mainExe ARDU_OTK.exe --outputDir releases`
- CI release trigger:
	- `git tag v1.2.3`
	- `git push origin v1.2.3`

## Architecture Map

- App startup:
	- `ARDU_OTK/Program.cs`: custom entry point; `VelopackApp.Build().Run()` must execute first.
	- `ARDU_OTK/App.xaml.cs`: creates and activates `MainWindow`.
	- `ARDU_OTK/MainWindow.xaml.cs`: window chrome/title bar + navigates to `MainPage`.
- UI/update flow:
	- `ARDU_OTK/MainPage.xaml.cs`: startup update check and update state rendering.
	- `ARDU_OTK/Services/UpdateService.cs`: update state machine and apply/restart gate.
- Data paths/storage safety:
	- `ARDU_OTK/Services/Store/AppPaths.cs`: canonical data roots and backup/protocol paths.

## Project Guardrails

- Do not replace custom startup with generated XAML `Main`; `DISABLE_XAML_GENERATED_MAIN` is intentional.
- Keep deployment **unpackaged**. Do not introduce MSIX-only assumptions unless explicitly requested.
- Keep `EnableMsixTooling=true` in this project; publish output depends on XAML/PRI asset targets.
- Keep `PublishTrimmed=false` unless explicitly validated for WinUI reflection/bindings.
- When changing update behavior, preserve the `IsBusy` gate semantics in `UpdateService.ApplyAndRestart()`.
- Never store production data under app install/version folders. Respect `AppPaths` design (`%LOCALAPPDATA%/ARDU_OTK.Data`).

## Editing Focus

- Prefer touching source under `ARDU_OTK/`.
- Treat `publish/`, `releases/`, `bin/`, and `obj/` as build artifacts unless task explicitly targets packaging outputs.
- Keep comments and operator-facing strings consistent with existing Russian-language domain context when editing nearby code.

## Instruction Quality Rules

- Link to existing docs/code instead of copying long explanations.
- Keep changes minimal and preserve current deployment/update model.
- Validate build-impacting changes with a local `dotnet build` when feasible.
