---
name: ardupilot-firmware
description: Work with ArduPilot flight controller firmware from the native Windows app in this repo — connect over MAVLink, store and select reference profiles, compare a board's parameters against a reference over a configurable block, display attitude on three axes plus current, voltage and flight mode, force the external compass primary and set the use flags, transfer and verify compass calibration, run fixed-yaw calibration with an operator-entered azimuth, run accelerometer level calibration, reboot, and prove there are no IMU or compass calibration errors. Use when connecting to a flight controller, reading or writing parameters, comparing against a reference, calibrating, rebooting, verifying calibration health, or designing any UI surface that shows flight-controller state.
---

# ArduPilot Firmware

This repo's product is a native Windows bench tool that verifies and configures ArduPilot flight controllers
against a stored known-good reference. This skill is the operational contract for every part of the app that
touches the flight controller.

Pair it with the `winui-app` skill for WinUI 3 setup, shell, control and theming decisions. This skill owns the
flight-controller domain; `winui-app` owns the platform.

## The app as it actually exists

Check these against the repo before relying on them; they are the facts the references are written against.

| | |
| --- | --- |
| Project | `ARDU_OTK/ARDU_OTK.csproj`, solution `ARDU_OTK.slnx` |
| Target framework | `net10.0-windows10.0.26100.0` — **.NET 10**, not .NET 8/9 |
| Deployment | **Unpackaged** (`WindowsPackageType=None`), self-contained app *and* Windows App SDK. No MSIX, no package identity, no administrator rights. Installs to `%LocalAppData%\ARDU_OTK`. |
| Updates | Velopack via GitHub Releases; released by pushing a `vX.Y.Z` tag. See `README.md`. |
| Update interlock | `ARDU_OTK/Services/UpdateService.cs` — `IsBusy` is `Func<bool>` defaulting to `static () => false`. |
| Trimming | `PublishTrimmed=False`, deliberately. Do not enable it. |

Two consequences bite immediately and are easy to get wrong from memory:

- **On .NET 10 the latest `Asv.Mavlink` is the correct choice**, not the old net8-era pin. The downward pins exist
  only for a hypothetical retarget.
- **There is no `serialcommunication` capability question.** That applies to MSIX plus the WinRT serial API;
  this app is unpackaged and uses `System.IO.Ports`.

## Required Flow

1. Read `references/_sections.md`, then load **only** the references that match the task. Do not load all of them.
2. Do not write ArduPilot parameter, message, command or enum names from memory. The compass parameter naming is
   deliberately irregular (`COMPASS_EXTERNAL` → `COMPASS_EXTERN2`/`COMPASS_EXTERN3`, `COMPASS_OFS_X` → `COMPASS_OFS2_X`)
   and several names were renamed between firmware releases. Take every name from the reference files.
3. Classify the task first — reference store, parameter compare, telemetry, compass topology, compass calibration,
   IMU level and verification, or app integration — and use the capability map below to pick the owning reference.
4. Before writing anything to a board, satisfy the safety gates in `references/dotnet-mavlink-and-winui-integration.md`.
   They are not optional and they are not per-feature.
5. When a reference marks something "verify against target firmware", treat it as unverified. Do not promote it to
   a shipped assumption, and do not build a workflow whose success depends on it.
6. Any workflow that ends in a verdict must produce an explicit verdict value. Absence of evidence is never success.

## Capability map

Each product requirement has exactly one owning reference. Start there.

| Product requirement | Owning reference |
| --- | --- |
| Store reference snapshots in the local database; several selectable reference profiles; operator selects the reference before connecting the board under test; large top-left primary panel | `references/reference-profiles-and-storage.md` |
| Operator enters the ID of the board under test; results of the work and the checks are written to the database; work history is browsable | `references/reference-profiles-and-storage.md` |
| Compare the board's parameters against the reference over a **configurable block** | `references/parameter-protocol-and-profiles.md` |
| Display attitude on all three axes, current, voltage, flight mode | `references/connection-and-telemetry.md` |
| Put the external compass first automatically, set its use flag, clear the internal one's; compass panel showing the compasses and their flags | `references/compass-topology-and-flags.md` |
| On a button press: transfer compass calibration from the reference, write, read back, compare, warn; if OK reboot, re-read, compare, warn | `references/compass-calibration-transfer.md` |
| Fixed-yaw / large-vehicle calibration with an operator-entered azimuth | `references/compass-calibration-transfer.md` |
| Run the level calibration procedure | `references/imu-level-and-health-verification.md` |
| Restart, then verify there are no IMU or compass calibration errors | `references/imu-level-and-health-verification.md` |
| MAVLink library choice, layering, threading, device lifecycle, UI patterns | `references/dotnet-mavlink-and-winui-integration.md` |
| Unpackaged/Velopack deployment, updates, and the `UpdateService.IsBusy` interlock | `references/dotnet-mavlink-and-winui-integration.md` |
| Where the store lives so it survives an update | `references/reference-profiles-and-storage.md` |

## Hard rules

These hold across every workflow. A change that breaks one of them is wrong regardless of what it enables.

- **Never write `COMPASS_DEV_ID*`.** It is read-only, auto-detected, and matched against detected sensors at boot.
  Writing it mis-seats the compasses. Reorder by writing `COMPASS_PRIO1_ID`/`PRIO2_ID`/`PRIO3_ID` only.
- **Never copy `COMPASS_DEV_ID*` or `COMPASS_PRIO*_ID` from a reference into another board.** They name that board's
  sensors. Take priority values from the target's own detected device ids.
- **Never leave a board with zero compasses enabled.** Clearing the internal `COMPASS_USE*` is only valid once an
  external compass is confirmed present and enabled.
- **Every parameter write is verified by an independent read-back by name.** The `PARAM_VALUE` echo is broadcast,
  asynchronous and carries `param_index = 0xFFFF`; it is not a receipt.
- **A read-back that shows the old value is not automatically a failure.** ArduPilot coalesces float writes inside a
  1e-4 relative band. Report that as coalesced, never as an error, and never as a silent success either.
- **`SYS_STATUS` health bits do not prove calibration.** `3D_ACCEL` health means data is flowing and `3D_MAG` means the
  compass answers; an uncalibrated board reports both healthy. Proof comes from the `SYS_STATUS` `PREARM_CHECK` bit
  (`0x10000000`) and the prearm `STATUSTEXT`, forced with `MAV_CMD_RUN_PREARM_CHECKS`. Command 401 returning
  `ACCEPTED` only means the checks ran.
- **Never write parameters or start a calibration while the vehicle is armed.**
- **Reordering compasses and changing calibration require a reboot to take effect**, and a reboot drops the USB device
  off the bus. Nothing cached survives it — not parameters, not stream intervals, not the COM port name.
- **No silent failure.** Every partially applied batch is reported as partial, with the actual cause, in operator
  language. A stale or unknown telemetry value renders distinctly from a real one, never as a plausible number.
- **Every write and every check result is persisted** — parameter, old value, new value, verification outcome,
  timestamp, operator, board id, reference profile revision. A run also records which checks were actually enabled,
  so a clean result is never mistaken for a complete one.
- **Hold the update interlock while the bench is working.** `UpdateService.IsBusy` defaults to "free", so unless the
  flight-controller session sets it, Velopack can apply an update and restart the process mid-operation. A restart
  between a `PARAM_SET` and its verifying read-back leaves the board partially written with no audit record and no
  way for the operator to know what landed. Report busy for any write batch, any calibration, any reboot-and-wait,
  any verification run, and any started-but-uncommitted run — and report busy when the state cannot be determined.
- **The operator's data outlives the installed app.** Updates replace the application directory, so the reference
  store, run history and audit log live in a stable per-user location outside it, never in the app folder.

## Two traps that have no obvious symptom

Both look like success and fail later. Read the owning reference before touching either.

- **Fixed-yaw calibration destroys transferred soft-iron data.** `MAV_CMD_FIXED_MAG_CAL_YAW` auto-saves immediately
  and forces `COMPASS_DIA*` to (1,1,1) and `COMPASS_ODI*` to (0,0,0). Running it after a calibration transfer discards
  most of what was transferred. The operator-entered azimuth is also **true** north, not magnetic — entering a magnetic
  reading silently biases the result. See `references/compass-calibration-transfer.md`.
- **A calibration transferred onto different hardware reads back correctly and still fails prearm.** ArduPilot
  invalidates but does not zero a calibration whose stored device id does not match the detected sensor. Read-back
  equality alone is not proof; the workflow must end in a prearm-level check. See the same reference.

## Common routes

| Request | Read first |
| --- | --- |
| Capture or import a reference, manage profiles, design the shell or the top-left panel | `references/reference-profiles-and-storage.md` |
| Enter the board id, record results, browse history | `references/reference-profiles-and-storage.md` |
| Define or edit the configurable comparison block | `references/parameter-protocol-and-profiles.md` |
| Fetch, write, verify or diff parameters; read a `.param` / `.parm` / `.params` file | `references/parameter-protocol-and-profiles.md` |
| Connect to a board, request streams, build a live readout, parse `STATUSTEXT` | `references/connection-and-telemetry.md` |
| Read compass topology, reorder compasses, set use flags, build the compass panel | `references/compass-topology-and-flags.md` |
| Transfer compass calibration, or run fixed-yaw calibration | `references/compass-calibration-transfer.md` |
| Run level calibration, reboot the board, produce a verification verdict | `references/imu-level-and-health-verification.md` |
| Choose the MAVLink library, structure the app, handle reboot and COM re-enumeration, package it | `references/dotnet-mavlink-and-winui-integration.md` |

## Reference rules

- Keep C# and WinUI 3 as the primary path. Follow `winui-app` for platform conventions: stock WinUI controls and
  command surfaces first, light and dark theme by default via theme-aware resources, CommunityToolkit only when the
  built-ins do not cover the need.
- Put detailed protocol, parameter, calibration, storage and UI-pattern guidance in the matching reference file
  instead of duplicating it here.
- When firmware behaviour and documentation disagree, the reference files follow the firmware source. Do not "correct"
  them back toward the wiki.
