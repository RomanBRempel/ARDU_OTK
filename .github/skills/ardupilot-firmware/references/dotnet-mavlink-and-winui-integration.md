# .NET MAVLink and WinUI 3 Integration

How this app is assembled on Windows: which MAVLink library to take, how the code is layered, how threads and the UI thread interact, how a USB flight controller is discovered and survives a reboot, how the app is deployed and updated (unpackaged, self-contained, Velopack) and what that forbids, and the safety/UI rules a tool that writes to flight hardware must obey.

Ground truth for this file is the app that exists in this repo: `ARDU_OTK/ARDU_OTK.csproj`, `ARDU_OTK/Program.cs` and `ARDU_OTK/Services/UpdateService.cs`. Where this file states a project setting, it is quoting that csproj — check there before contradicting it.

Scope note: firmware behaviour, parameter names, message fields, calibration semantics and stream rates live in the sibling reference files. This file only covers the Windows/.NET/WinUI side and the safety envelope around it.

---

## 1. MAVLink library choice

### 1.1 Verified options (all checked on nuget.org)

| Option | License | Latest | TFM | Verdict |
|---|---|---|---|---|
| `Asv.Mavlink` | MIT | 4.3.0 (2026-07-31) | **4.0.17 = last net8; 4.0.18 = net9; 4.0.19+ = net10 only** | **Recommended.** Only option with a real parameter-protocol client (`IParamsClient` / `IParamsClientEx` with caching + full sync), MAVLink v2, ardupilotmega dialect, serial+UDP+TCP. Pin the version to your TFM. Pulls in `Asv.Common/IO/Cfg/Store`, `ZLogger`. |
| mavgen `--lang CS` (vendored) | your code | n/a | any | **Second choice.** Pure codec (`partial class MAVLink`, structs, CRC-extra table). No transport, no param state machine — you write those. Zero license ambiguity, no dependency drift. |
| `MAVLink` (MissionPlanner's) | **GPL-3.0 source, no NuGet license metadata** | 1.0.8 (2021) | netstandard2.0 | **Avoid** for non-GPL products. Codec only, 4.5 years stale. |
| `MavLinkSharp` | unclear | 1.9.0 (2026-07) | net10 + netstandard2.0 | Viable fallback; runtime XML dialect parsing. |
| MAVSDK C# | — | — | — | **Dead** — `mavlink/MAVSDK-CSharp` archived 2024-04-10. |

### 1.2 Recommendation

1. **Take `Asv.Mavlink`.** Reason: it is the only package that ships a parameter-protocol client rather than just a codec. The parameter workflow (full fetch with gap detection, read-back verification, diff) is the largest and most failure-prone part of this app; writing that state machine yourself is the bulk of the work the second choice implies.
2. **Second choice: vendored mavgen `--lang CS` output.** Reason to fall back to it: the code is yours, license is unambiguous, there is no transitive dependency chain (`Asv.Common`, `Asv.IO`, `Asv.Cfg`, `Asv.Store`, `ZLogger`) and no TFM coupling. Cost: you implement transport, retry/timeout, the parameter protocol and MAVFTP by hand.
   - ⚠️ Whether mavgen `--lang CS` output compiles cleanly on modern .NET is **UNVERIFIED** (MissionPlanner's copy targets netstandard2.0; the doubt was raised for .NET 8 and this project is two majors past that). Verify before relying on it — generate, compile against this project's TFM, and only then commit to this path.

### 1.3 🔴 Version pinning — this project is `net10.0-windows10.0.26100.0`

`ARDU_OTK.csproj` targets **`net10.0-windows10.0.26100.0`** (`TargetPlatformMinVersion` `10.0.17763.0`). `Asv.Mavlink` moved its target framework forward inside the 4.0.x band, and **4.0.19, 4.2.0 and 4.3.0 are net10.0-only** — which on this project is not a constraint but a licence to take the newest release.

**Primary instruction: pin `Asv.Mavlink` to `4.3.0`** (latest, 2026-07-31, net10.0). It matches the app's TFM and there is no reason to hold the package several releases stale.

```xml
<!-- Asv.Mavlink 4.0.19+ is net10.0-only; this project is net10.0-windows10.0.26100.0.
     Do not downgrade this pin without also retargeting the project — see §1.3. -->
<PackageReference Include="Asv.Mavlink" Version="4.3.0" />
```

Note — **only** if someone ever retargets this project downward (there is no current reason to):

| TFM | Last usable `Asv.Mavlink` |
|---|---|
| `net8.0-windows…` | **4.0.17** |
| `net9.0-windows…` | **4.0.18** |
| `net10.0-windows…` ← **this project** | 4.0.19 and up, through **4.3.0 (take this)** |

Rules:
1. **Never** add the package without an explicit `Version=` — no floating ranges, no `*`, no "latest". "Latest" happens to be right here today; it must still be written down as a number.
2. Pin the version centrally (`Directory.Packages.props` / `Directory.Build.props`) so every project in the solution agrees. Note that this repo's `Directory.Build.props` currently carries only the local build version — adding central package pinning is a deliberate act, not an assumption.
3. Dropping to a 4.0.17/4.0.18 pin is a **decision to move the app's TFM backwards**, not a package downgrade. Treat it as such; nothing in this app needs it.
4. Record the TFM↔version pair in a comment next to the pin (as above); the next person will otherwise "fix" it in one direction or the other.
5. The same TFM check applies to every other package: `MavLinkSharp` ships net10 + netstandard2.0 and is fine; `MAVLink` (MissionPlanner's) is netstandard2.0 and would resolve — its problem is the licence (§1.5), not the TFM.

### 1.4 Packages that do not exist — do not reference

`MavLinkNet`, `MAVLink.NET`, `Asv.Mavlink.Minimal`, `MAVSDK` are **not on nuget.org**. Any of them appearing in a `.csproj` or a plan is a hallucination; restore will fail.

### 1.5 License hazard

The NuGet package literally named `MAVLink` is Mission Planner's codec, **GPL-3.0 source with no license metadata on the package**. For a non-GPL product this is a licensing dead end — do not take it "just for the structs". Vendor mavgen output instead if you need a bare codec.

---

## 2. Layering

Build the app as a strict, one-directional stack. Each layer may call downward only; nothing calls upward except through events/observables that carry **domain** types.

| # | Layer | Owns | Must NOT leak upward |
|---|---|---|---|
| 1 | **Transport** | `System.IO.Ports.SerialPort` (and TCP/UDP for SITL), open/close, exclusive-access errors, raw byte stream, surprise-removal detection. | `SerialPort`, `Stream`, byte buffers, Win32 error codes. |
| 2 | **MAVLink codec** | Frame parse/serialise, MAVLink v2, CRC-extra, ardupilotmega dialect, seq/sysid/compid. | Generated MAVLink structs, message IDs, enum ints. |
| 3 | **Connection / session service** | One live vehicle session: heartbeat tracking, link state (`Disconnected / Connecting / Connected / Rebooting / Lost`), request-response correlation (`COMMAND_LONG` → `COMMAND_ACK`), retries and timeouts, `STATUSTEXT` reassembly, stream-interval re-issue after reconnect. | Raw acks, `MAV_RESULT` ints, message IDs. Emits a `CommandOutcome` domain result instead. |
| 4 | **Domain services** | The vehicle-facing use cases; see 2.1. | Anything from layers 1–3. |
| 5 | **View models** | Observable state, commands, cancellation, formatting to display units, staleness tracking, validation. | Nothing above — this is the top of the non-UI code. |
| 6 | **XAML / views** | Layout, theme resources, control choice, animation. | — |

### 2.1 Domain services ↔ sibling reference files

Each domain service corresponds to one sibling reference in this skill; the authoritative file list is the reference index in `SKILL.md`. Implement one service per domain, do not merge them:

| Domain service | Responsibility | Sibling reference (domain material) |
|---|---|---|
| `IParameterService` | Full fetch with gap detection, read-by-name, verified write, diff, `.param`/`.parm`/`.params` file I/O, reboot-required tracking | parameter protocol, files and comparison |
| `ICompassService` | Compass identity/`DEV_ID` decode, priority ordering, external/internal resolution, calibration transfer | compass ordering, identity and calibration data |
| `ICalibrationService` | Level calibration, gyro cal, onboard mag cal lifecycle, reboot, post-reboot health and prearm verdict | IMU level calibration, reboot and health verification |
| `ITelemetryService` | Attitude, battery, mode, per-compass mag, EKF variances, stream-interval management | telemetry messages, units and stream rates |

### 2.2 The hard rule

**No generated MAVLink type ever reaches a view model or XAML.** Not `HEARTBEAT`, not `PARAM_VALUE`, not a `MAV_RESULT`, not a `MAV_SEVERITY` int. Layer 4 converts to domain records with real units and real enums:

```csharp
// domain, not wire
public sealed record AttitudeSample(double RollDeg, double PitchDeg, double HeadingDeg, DateTimeOffset At);
public sealed record ParameterWriteResult(string Name, float Requested, float ReadBack, WriteVerdict Verdict);
public enum WriteVerdict { Verified, Coalesced, Mismatch, Denied, Timeout }
```

Why this is not style pedantry: the wire types carry sentinels (`65535`, `-1`, `32767`), raw units (rad, mV, cA, mgauss, µs) and version-dependent field meanings. A binding straight onto a wire struct is how a sentinel gets rendered as a plausible number on screen (see §9.6).

### 2.3 🔴 Dependency rules imposed by the deployment model

These constrain what any layer may depend on. They come from `ARDU_OTK.csproj` (§6.1), and they are load-bearing, not preferences.

1. **`PublishTrimmed=False` is deliberate — never enable trimming.** The csproj comment states the reason: WinUI XAML and bindings resolve **by reflection**, trimming removes types that are only reached that way, and the failure does not appear at build time — it appears at runtime, on one specific screen, at a bench, as a shift's downtime.
   Consequences for code this app adds:
   - Any **MAVLink codec** (generated or library) may use reflection over message types; do not assume it survives trimming and do not add trim annotations as a substitute for testing.
   - Any **reflection-based parameter mapping** (parameter name → property/record member, metadata binding from `apm.pdef`) is legitimate here precisely because trimming is off. It must not be re-engineered around a trimming plan.
   - Any **JSON profile deserialisation** (comparison profiles, reference snapshots — see `reference-profiles-and-storage.md` and `parameter-protocol-and-profiles.md`) may use reflection-based `System.Text.Json`. Source-generated `JsonSerializerContext` is fine and often better for other reasons, but it is **not** a licence to turn trimming on.
   - **Nobody "optimises" the publish size by setting `PublishTrimmed=True`.** If size ever becomes a real problem, the answer is the delta-update mechanism (§6.1, §7), not trimming. A library whose documentation only describes its behaviour "when trimmed" tells you nothing about this app.
   - `PublishReadyToRun` is on for Release and off for Debug; that is unrelated to trimming and must not be conflated with it.
2. **The app is self-contained — every added dependency ships in the installer.** `SelfContained=true` and `WindowsAppSDKSelfContained=true` mean the .NET runtime *and* the Windows App SDK are inside the package that every workstation downloads (~110 MB first time, deltas afterwards). Weigh a new dependency on licence, maintenance and transitive sprawl — `Asv.Mavlink` alone pulls `Asv.Common`/`Asv.IO`/`Asv.Cfg`/`Asv.Store` and `ZLogger` — rather than on kilobytes, but do weigh it: nothing here is "already on the machine".
3. **No dependency may assume a packaged identity.** The process is unpackaged (§6.2), so APIs that require package identity (`Package.Current`, `Windows.Storage.ApplicationData.Current`, package-scoped storage and settings) are not available. Use `Environment.GetFolderPath(SpecialFolder.LocalApplicationData)` and plain file I/O for the run database, audit log and profiles. A library that quietly depends on identity fails at first use on the shop floor, not in the developer's build directory.

---

## 3. Threading and the UI thread

### 3.1 Rules

1. **MAVLink receive runs off the UI thread.** A dedicated read loop (or the library's own scheduler) owns the serial stream. The UI thread never reads or writes the port.
2. **Marshal to the UI with `DispatcherQueue.TryEnqueue`.** Capture the `DispatcherQueue` once on the UI thread; never assume `DispatcherQueue.GetForCurrentThread()` is non-null on a worker.
3. **Never `.Result` / `.Wait()` / `GetAwaiter().GetResult()` on the UI thread.** Every vehicle operation is `async Task` with a `CancellationToken`.
4. **Do not bind every packet.** Telemetry arrives at up to tens of hertz (`ATTITUDE` at 10–30 Hz, per-compass mag at 5 Hz each). Coalesce to a display refresh cadence.
5. Vehicle-side blocking is real: some commands block the flight controller's main loop while they run, so **client timeouts must be generous** — see §3.3.

### 3.2 Coalescing pattern

Keep a latest-value slot per signal, flush on a timer at display rate (10–15 Hz is plenty for a readout; nothing on screen benefits from 30 Hz):

```csharp
private volatile AttitudeSample? _pendingAttitude;   // written by the MAVLink thread

// MAVLink thread — no dispatch, no allocation storm
void OnAttitude(AttitudeSample s) => _pendingAttitude = s;

// UI thread, DispatcherQueueTimer at ~66–100 ms
void OnTick(object? _, object? __)
{
    var s = Interlocked.Exchange(ref _pendingAttitude, null);
    if (s is not null) { RollDeg = s.RollDeg; PitchDeg = s.PitchDeg; HeadingDeg = s.HeadingDeg; }
}
```

Corollaries:
- Last-value-wins is correct for *state* (attitude, voltage, mode). It is **wrong for events** — `STATUSTEXT`, `MAG_CAL_PROGRESS` transitions and command acks must be queued, never dropped, because a dropped `PreArm:` line is a lost diagnosis.
- Never rebuild an `ObservableCollection` per packet. For the parameter table, update items in place and raise change notification only for the rows that actually changed.

### 3.3 Long-running operations

| Operation | Shape | Timeout guidance |
|---|---|---|
| Level calibration (`MAV_CMD_PREFLIGHT_CALIBRATION`) | `async Task<CalibrationResult>` + `CancellationToken` | The vehicle **blocks** in its main thread and the handler wraps the work in `EXPECT_DELAY_MS(30000)`. Allow at least 30 s. Expected duration is much shorter, but the ArduPilot-derived ≈0.5 s + gyro cal figure is **UNVERIFIED** — do not build a UI that assumes it, verify against target firmware. |
| Full parameter fetch | `async IAsyncEnumerable<…>` or `Task` with `IProgress<T>` | ArduPilot throttles the stream to ~30% of link bandwidth (5 params/burst without flow control) — expect it slow and bursty. Use a per-`PARAM_VALUE` restarting timeout, not one deadline for the whole list. |
| Reboot-and-wait | `async Task<ReconnectResult>` | See §5; needs its own bounded wait and an explicit failure path. |
| Onboard mag calibration | long-lived operation with progress + cancel | Driven by progress/report messages; cancellable from the UI at any time. |

Additional rules:
1. Exactly one vehicle operation at a time. Serialise on the session (an `AsyncLock`/`SemaphoreSlim(1,1)`); a second concurrent write while a calibration is running is an operator-visible error, not a queued request.
2. Cancellation must be honest: cancelling a `Task` does **not** cancel work already running on the flight controller. Cancel means "stop waiting and stop issuing"; the UI must say so rather than implying the vehicle was rolled back.
3. Retries and timeouts belong in layer 3, not in view models. A view model awaiting an operation sees one outcome, not a retry storm.

---

## 4. Device discovery and identification

### 4.1 Why `SerialPort.GetPortNames()` is not enough

`SerialPort.GetPortNames()` returns **names only** — no VID, no PID, no manufacturer. It reads `HKLM\HARDWARE\DEVICEMAP\SERIALCOMM` and **can return stale entries**. You cannot tell an ArduPilot board from a USB-serial dongle, a Bluetooth virtual port or a ghost entry with it. Use it, if at all, only as a cross-check.

### 4.2 What to query instead

Enumerate **`Win32_PnPEntity`** filtered by the Ports device class GUID **`{4d36e978-e325-11ce-bfc1-08002be10318}`**, reading `PNPDeviceID`, `Name`, `Manufacturer`, `HardwareID`. Alternative: SetupAPI on **`GUID_DEVINTERFACE_COMPORT`**.

- The `COMn` name comes out of `Name` (or the device's `PortName` registry value); the **identity** comes out of `PNPDeviceID` / `HardwareID`.
- Keep the **device instance path** (`PNPDeviceID`) as the stable key for a board. `COMn` is a *rendering* of that key at a point in time, not an identity (§5).

### 4.3 VID/PID and the manufacturer-string rule

| Source | VID | PIDs |
|---|---|---|
| ArduPilot default (hwdef declares none) — pid.codes | `0x1209` | `0x5740` composite/dual-CDC, `0x5741` single CDC |
| CubePilot | `0x2DAE` | `0x1016` CubeOrange, `0x1058` CubeOrange+, `0x1011` CubeBlack, … |
| Holybro | `0x3162` | `0x0053` Pixhawk6C, `0x004B` Durandal |
| 3DR (pre-2018) | `0x26AC` | — |
| ST Micro (early ChibiOS) | `0x0483` | `0x5740` |

- MatekH743/F405 and Pixhawk6X ship the **generic `0x1209` IDs**.
- **Manufacturer string is `ArduPilot`**; product string is the board name, and the **bootloader appends `-BL`**.
- 🔴 **Match on the VID set *plus* the `ArduPilot` manufacturer string. Never on VID alone** — `0x1209` is a shared community VID and `0x0483/0x5740` is generic ST.
- UNVERIFIED, treat as "verify against the actual board": Pixhawk 6X apparently having no Holybro VID, and the CubeOrange bootloader sharing the application PID. Do not build identity logic that depends on either being true.
- Show unmatched ports in the picker as "unknown device" rather than hiding them, but never auto-connect to one.

### 4.4 Open behaviour and baud

1. **`SerialPort` opens exclusively (`FileShare.None`).** If Mission Planner, QGC or a previous instance of this app holds the port, `Open()` throws `UnauthorizedAccessException`. Surface that as "port is in use by another application — close it and retry", not as a generic failure.
2. **Baud is a no-op over USB CDC** — the ChibiOS driver stores the host line coding but never applies it. **Open at 115200 and move on.** Do not offer a baud picker on a USB connection; it is a knob that changes nothing and invites false diagnoses.
3. Baud matters only for real UARTs / SiK radios: `SERIAL1`/`SERIAL2` default 57600, `SERIAL0` USB nominal 115200. If the app ever grows a radio-link mode, expose baud only there.
4. Always dispose the port deterministically. A leaked handle blocks the next connect and, after a reboot, blocks re-enumeration handling.

---

## 5. 🔴 Reboot survival

This is the single most fragile lifecycle in the app. Get it wrong and the app appears to hang, or worse, talks to the wrong device.

### 5.1 What actually happens

1. `MAV_CMD_PREFLIGHT_REBOOT_SHUTDOWN` **does ack before rebooting**, then never returns.
2. The device then **drops off the USB bus and re-enumerates — there is no graceful detach** (`port_disable(); NVIC_SystemReset();`). The host sees a surprise removal.
3. **The COM number can change** on composite F7/H7 boards.
4. **Cube-class boards appear twice**: first the bootloader (product string with `-BL`), then the application. Connecting to the first appearance connects to the bootloader.
5. Mission Planner's own handling is "sleep ~500 ms then reopen" — that is a lower bound and a poor model. Do not copy it.
6. **Nothing about the session survives.** Stream intervals set with `MAV_CMD_SET_MESSAGE_INTERVAL` **do not persist across reboot**, and parameters must be re-fetched.

### 5.2 Ordered recovery procedure

1. **Before sending the reboot**, snapshot what you will need to restore: the device instance path (`PNPDeviceID`), VID/PID, the set of stream intervals you had requested, and any in-flight operation state. Put the UI into an explicit `Rebooting` state.
2. **Send the reboot** and consume the ack. Treat the ack as "the command was accepted", not "the board rebooted".
3. **Close the serial port immediately** — do not wait for a read error. Dispose it. Holding a handle across re-enumeration produces stuck handles and `UnauthorizedAccessException` on the way back in.
4. **Watch for device-change notifications rather than polling blindly**: `WM_DEVICECHANGE` (register for `GUID_DEVINTERFACE_COMPORT`) or a `DeviceWatcher`. A blind poll loop both wastes the reconnect window and races the bootloader appearance. A slow *corroborating* re-enumeration query is acceptable as a backstop, but notifications drive the flow.
5. **Re-resolve the port from the device instance path + VID/PID. Never reuse the cached `COMn`.** Re-run the §4.2 enumeration and map instance path → current `COMn`.
6. **Skip the bootloader appearance** on Cube-class boards: a product string ending in `-BL` is the bootloader, not the application. Ignore it and keep waiting for the application enumeration. Expect two device-arrival events, not one.
7. **Debounce**: after the arrival event, allow a short settle before opening — the port can exist momentarily before it is openable. Retry `Open()` a few times with a short backoff rather than failing on the first `UnauthorizedAccessException`/`IOException`.
8. **Re-establish the session**: wait for `HEARTBEAT`, re-derive vehicle type/mode, then confirm the board actually restarted rather than never having gone away. Detectors: a heartbeat gap followed by resumption; the boot-banner `STATUSTEXT` (forceable with `MAV_CMD_DO_SEND_BANNER`); `time_boot_ms` decreasing.
   - UNVERIFIED, verify against target firmware: `time_boot_ms` reset as a reboot detector is a correct inference, not a documented API guarantee; and whether the banner fires automatically on reconnect versus only on `MAV_CMD_DO_SEND_BANNER`. Prefer **forcing** the banner with the command over hoping for it, and treat any single detector as corroborating evidence rather than proof.
9. **Re-issue every stream interval** from the step-1 snapshot. They do not survive the reboot. On Copter this is not optional: nothing streams until you ask.
   - UNVERIFIED: the `MAV_RESULT` ArduPilot returns for an unmappable message ID on `MAV_CMD_SET_MESSAGE_INTERVAL` — treat "not `ACCEPTED`" as unsupported, and report which intervals failed rather than assuming all took.
10. **Re-fetch parameters.** Do not trust the pre-reboot cache: a reboot is exactly when reboot-required parameters take effect and when the firmware may have rewritten values itself. Invalidate the cache at step 3 so no stale value can be rendered during the gap.
11. Only then leave the `Rebooting` state and re-enable operator actions.

### 5.3 Timeout and the "board did not come back" path

1. Run the whole of §5.2 steps 4–8 under one bounded wait. Choose a deadline generous enough for a two-stage (bootloader → application) enumeration; a Cube can take noticeably longer than a single-CDC board.
2. On expiry, fail **explicitly and specifically**. The failure message must distinguish:
   - no device-arrival event at all (board did not re-enumerate — power/USB/bootloader stuck),
   - device arrived but only the `-BL` appearance persisted (stuck in bootloader),
   - port opened but no `HEARTBEAT` (firmware not running or wrong port matched).
3. Never silently fall back to "connected". Never auto-retry forever. Offer the operator a single explicit "Rescan and reconnect" action, and keep the diagnosis on screen.
4. Any operation that was pending across the reboot is reported as **not applied / unknown**, never as succeeded.

---

## 6. Deployment: unpackaged, self-contained, Velopack

**This app is not an MSIX app.** Everything below is what `ARDU_OTK.csproj`, `ARDU_OTK/Program.cs` and `README.md` actually specify. Plans, prompts and reviews that assume an MSIX package, an AppContainer or a capability manifest are describing a different application.

### 6.1 The deployment settings and why each one is there

| Setting (from `ARDU_OTK.csproj`) | Value | Why |
|---|---|---|
| `TargetFramework` | `net10.0-windows10.0.26100.0`, `TargetPlatformMinVersion` `10.0.17763.0` | Drives every package pin (§1.3, §6.3). |
| `WindowsPackageType` | **`None`** | Unpackaged. It also enables Windows App SDK bootstrapper auto-initialisation — **without it an unpackaged WinUI 3 app fails at startup.** |
| `SelfContained` / `WindowsAppSDKSelfContained` | `true` / `true` | The .NET runtime *and* the Windows App Runtime ship inside the installer. A shop-floor PC needs no prerequisites and **no administrator rights**. |
| `EnableMsixTooling` | `true` — **and no MSIX is ever built** | ⚠️ Kept only because those targets copy the compiled XAML (`.xbf`), `ARDU_OTK.pri` and `Assets` into publish. With `false`, publish "succeeds" and the app dies at startup with **`0xC000027B`** — XAML cannot find its resources. **Do not "clean this up"**: its presence is not evidence of MSIX packaging. |
| Custom entry point `Program.cs` + `DISABLE_XAML_GENERATED_MAIN` | | Required by Velopack: `VelopackApp.Build().Run()` must be the **first line of the process**, before `Application.Start`. The installer/updater relaunch the same exe with service arguments (install, first run, uninstall) and Velopack intercepts them and exits; if XAML comes up first, those service runs flash the app window. |
| `PublishTrimmed` | **`False`**, deliberately | See §2.3.1. Load-bearing. |
| `PublishReadyToRun` | `True` in Release, `False` in Debug | Unrelated to trimming. |
| `Microsoft.Windows.SDK.BuildTools.WinApp` | **removed from the template on purpose** | It hooks the Run target and registers a debug *package identity*, which is meaningless for an unpackaged app and breaks `dotnet run`. Do not re-add it. |
| Install location | `%LocalAppData%\ARDU_OTK` | Per-user, no elevation, no admin prompt. |
| Update channel | Velopack ← GitHub Releases (public repo, no token) | §7. |
| Installer signature | none (SmartScreen once per machine) | Not a code concern, but do not design a UI that promises a silent *first* install; updates after that are silent. |

### 6.2 There is no capability question in this app

- There is **no MSIX package, no `Package.appxmanifest` identity, no AppContainer, and no `runFullTrust` declaration to make.** The process is a plain Win32 process running at the user's own integrity level, launched from `%LocalAppData%`.
- ⇒ **`System.IO.Ports.SerialPort` simply works.** There is nothing to declare, nothing to enable, nothing to "be safe" about. `serialcommunication` is **not** a question this app has to answer — do not raise it in a plan, a review or a manifest that does not exist.
- Device enumeration via `Win32_PnPEntity` / SetupAPI (§4.2) is likewise unconstrained.
- The manifest that *does* exist (`ARDU_OTK/app.manifest`, referenced by `ApplicationManifest`) is a classic Win32 side-by-side manifest: `PerMonitorV2` DPI awareness and the Windows 10 `supportedOS` declaration that unpackaged Windows App SDK apps need for OS-version-gated features. It has **no capability section and cannot have one** — do not confuse it with `Package.appxmanifest`.

**Note for a future reader, so this is not re-introduced:** capabilities would only ever become relevant if the app moved to MSIX **and** switched from `System.IO.Ports` to the WinRT `Windows.Devices.SerialCommunication.SerialDevice` API — that WinRT API is the only thing `<DeviceCapability Name="serialcommunication"/>` gates. Both halves are required. Even inside an MSIX, a default WinUI 3 package declares `<rescap:Capability Name="runFullTrust" />` and the process runs at medium IL, where capability enforcement (AppContainer-scoped) does not apply — so `System.IO.Ports` would keep working there too and the capability would still buy nothing. And a device capability added to a single-project MSIX runs into the known **manifest element-ordering bug**, whose fix is element order, not more capabilities. None of this applies today; it is recorded only so nobody re-derives the confusion from a WinUI template.

### 6.3 `System.IO.Ports`

`System.IO.Ports` is an **out-of-band NuGet package** on modern .NET — it is not in the shared framework and never becomes implicit, self-contained or not. Reference it explicitly with a pinned version, like every other package (§1.3), and remember it lands in the installer (§2.3.2).

---

## 7. 🔴 Update delivery and the flight-controller busy interlock

`ARDU_OTK/Services/UpdateService.cs` delivers updates with Velopack from GitHub Releases (public repo — no token). The app checks at startup, downloads in the background, and applies with a restart.

**This section is a hard requirement on the flight-controller code, not on the update code.** The interlock exists and is correct — and it is **inert until the session wires it up**.

### 7.1 The contract, exactly as implemented

| Member | Behaviour |
|---|---|
| `CheckAndDownloadAsync(CancellationToken)` | Checks and, if an update exists, downloads it immediately. **Safe to run at any time, including mid-operation: it does not touch the files of the running version.** Drives `State` through `Checking` → `Downloading` → `ReadyToApply`. Never restarts anything. |
| `ApplyAndRestart()` | The **only** call that replaces files and restarts the process. Returns `false` and does nothing when there is nothing pending **or when `IsBusy()` returns `true`**. A deferred update stays downloaded and applies at the next call — in practice, at the next app start. |
| `IsBusy` | `public Func<bool> IsBusy { get; set; } = static () => false;` — a settable delegate that **defaults to "never busy"**. |
| `State` | `Idle, NotInstalled, Checking, UpToDate, Downloading, ReadyToApply, Failed`. `NotInstalled` = running from a build directory (development); `Failed` = the check did not succeed, typically no network. |
| `StateChanged` / `LastError` / `CurrentVersion` / `PendingVersion` | UI inputs. `CurrentVersion` is `null` for an uninstalled build — Velopack reads it from install metadata, not from assembly attributes. |

🔴 **The default is the hazard.** `IsBusy` defaults to `static () => false`. If the flight-controller code never assigns it, the bench is permanently reported as free and an update can replace the app's files and restart the process **in the middle of an operation on a real board**. Assigning it is part of bringing the session service up, not a later refinement. The `README.md` says the same thing in one line; this section says what "busy" means.

### 7.2 States that MUST report busy

The predicate returns `true` whenever any of the following holds. This is a **minimum**, not an exhaustive list — a new operation that touches the board is busy by default until someone argues otherwise.

1. **Any parameter write batch in flight** — from *before* the first `PARAM_SET` until the last read-back verification has been recorded (§8.3, §8.6), including the pre-write snapshot of §8.7. The gap between a write and its verifying read is the single most dangerous moment in the app.
2. **Any calibration** — level/trim calibration (`MAV_CMD_PREFLIGHT_CALIBRATION` `param5 = 2`), fixed-yaw / large-vehicle calibration (`MAV_CMD_FIXED_MAG_CAL_YAW`), and onboard mag calibration from start until accept, cancel or final report. See `imu-level-and-health-verification.md` and `compass-calibration-transfer.md`.
3. **A reboot-and-wait window** — from *before* `MAV_CMD_PREFLIGHT_REBOOT_SHUTDOWN` is sent until §5.2 has finished or failed explicitly. The whole re-enumeration window (§5) is busy, including the period when no port is open.
4. **A verification run in progress** — a prearm run (`MAV_CMD_RUN_PREARM_CHECKS`), a post-reboot health verdict, or any read-back sweep that produces an operator-facing verdict.
5. **A run started but not yet committed to the database** — even when nothing is on the wire. A part is in the fixture and its results are uncommitted; see `reference-profiles-and-storage.md` for the run record. Losing the process here loses the run.
6. **Anything holding the session's single-operation lock** (§3.3.1). That lock is the cheapest correct source of truth for 1–4; items 3 and 5 are the ones it does *not* cover on its own.

Not busy: an idle connected session, live telemetry alone, browsing a diff that has not been applied, a completed and committed run, disconnected.

### 7.3 The concrete damage if this is skipped

**A restart between a `PARAM_SET` and its verifying read-back leaves the board partially written, in a state nobody recorded.** Some values took, some did not; the audit log (§8.8) has no closing record for the batch; and after the restart the app cannot reconstruct which writes landed. ArduPilot persists every write immediately — there is no commit step and no rollback — so the board carries the half-applied set through its own power cycles. The operator is handed a new version of the app and a board that passes some checks and fails others, **with no way to know which writes landed**.

The rest of the list fails the same way:
- A restart during a calibration abandons the procedure mid-flight; fixed-yaw calibration in particular **auto-saves offsets with no accept step**, so the board can be left holding results from a procedure that was never completed or reviewed.
- A restart inside the reboot window destroys the §5.2 step-1 snapshot (device instance path, VID/PID, stream intervals, pending operation). The new process cannot even identify which board it was waiting for, and cannot report the pending operation as "not applied" — it does not know one existed.
- A restart before commit loses the run entirely: the physical part has been touched, and there is no record that it was.

In every case the app violates §8.5 (no silent failure) and §8.11 (unknown outcomes reported as unknown) — not through a bug in those code paths, but because the process that owed the report no longer exists.

### 7.4 Wiring, and the rules the predicate must obey

```csharp
// Composition root, at the moment the session service is created — not later, not from a page.
_updateService.IsBusy = () => _session.IsBenchBusy;

// In the session service: one cheap, non-blocking, always-answerable property.
public bool IsBenchBusy
{
    get
    {
        try
        {
            return _operationLock.CurrentCount == 0        // a vehicle operation holds the session lock (§3.3.1)
                || Volatile.Read(ref _rebootWindowOpen)     // §5.2 in progress, port may be closed
                || _activeRun is { Committed: false };      // a started run not yet in the database
        }
        catch
        {
            return true;                                    // unknown state ⇒ busy (rule 2)
        }
    }
}
```

1. **Cheap and non-blocking.** It is polled from the update path and may be called on any thread. No `lock` that a long operation can hold, no I/O, no database query, no vehicle round-trip, no `await` bridged with `.Result`. Read `volatile`/`Interlocked` fields or a `SemaphoreSlim.CurrentCount`. A predicate that can block turns the updater into a source of deadlock.
2. **Fail safe: if the session state cannot be determined, report busy.** Any exception, any partially initialised session, any "I don't know" answers `true`. Never let an unknown state resolve to "free".
3. **Assign it once, at the composition root**, when the session service is constructed. Never from page code-behind, where navigation or a re-created view model can silently drop it back to the default.
4. **Never assign it back to a default.** No code path writes `IsBusy = () => false`. Tearing the session down must not leave the predicate more permissive than the state it left behind — an uncommitted run keeps reporting busy.
5. **It is a bench-state question, not a UI question.** It must be correct while the window is minimised, while a dialog is open, and while the UI thread is doing something else.
6. It is pure logic and must be unit-tested without hardware (§10.4).

### 7.5 UI rules for updates

1. **A downloaded, pending update is shown but never applied silently mid-session.** Surface `ReadyToApply` as a quiet, non-modal `InfoBar` or status-bar item naming the pending version. Applying is an explicit operator action — or it simply happens at the next start.
2. If the operator asks to restart while the bench is busy, `ApplyAndRestart()` returns `false`. The UI must say **what is holding it** (the operation in progress, the uncommitted run) and offer the action again when the bench goes idle — not a dead button, not a generic failure (§8.5, §9.7).
3. **`UpdateState.Failed` is a normal condition on a shop-floor PC, not an error.** No network or unreachable GitHub is the expected state of an isolated bench. Render it as a quiet status ("update check unavailable"), never a dialog, never a blocking banner, never an automatic retry storm. `LastError` goes to the log and behind a "Details" expander (§9.7.2).
4. `NotInstalled` is a developer condition (running from a build directory) — show nothing to an operator.
5. **Never block operator work on update state.** The bench must work indefinitely offline, on any state including `Failed`.

---

## 8. Safety rules for a tool that writes to flight hardware

Non-negotiable. Each is a requirement on the implementation, not a suggestion.

1. **Never write parameters, start a calibration, or trigger a reboot while the vehicle is armed.** Check armed state from the heartbeat before every such operation and re-check immediately before issuing it. If armed state is unknown or stale, treat it as armed and refuse. Refusal text names arming as the cause.
2. **Every destructive or irreversible operation requires explicit, specific confirmation.** "Specific" means the dialog names the actual thing being done and its consequence (which parameters, which compass, that a calibration overwrites stored data, that a reboot drops the link). A generic "Are you sure?" does not satisfy this rule. Confirmation is never pre-checked, never remembered by default.
3. **Every parameter write is verified by read-back. No fire-and-forget, ever.** Write, then independently re-read **by name** and compare. Never accept the vehicle's asynchronous echo as verification — it is broadcast, asynchronous, and carries a meaningless index.
4. **Distinguish "coalesced" from "failed".** A write whose value lands inside the firmware's internal skip band can legitimately no-op, and the verify read then shows the old value. Report that as **coalesced**, with an explanation — never as a success, never as a generic failure.
5. **No silent failure anywhere.** Every failed, rejected, timed-out or partially applied operation surfaces to the operator with the **actual cause** — the rejection reason, the failing parameter name, the reported status text, the timeout that expired. Swallowing an exception, logging-only, or a bare "operation failed" is a defect.
6. **A partially applied batch is reported as partial, not as a generic error.** The result of a bulk write is a per-item table: applied+verified / coalesced / rejected / not attempted. The operator must be able to see exactly which writes took and which did not, and to retry only the failures.
7. **Snapshot before every bulk write.** Take a full parameter snapshot immediately before applying a batch, store it, and offer a one-click restore from it. The snapshot is written to disk before the first write is issued, not after.
8. **Append-only audit log of every write.** One record per write attempt: timestamp, parameter name, old value, new value, verification outcome, and the operation that caused it. Append-only — never rewritten, never truncated by the app. **Exportable** to a file the operator can keep. The log survives disconnects and reboots.
9. **Reboot-required changes are tracked and surfaced.** If a written parameter only takes effect after a reboot, the app says so and keeps saying so until the reboot happens. Never report such a change as "applied" without qualification.
10. **Read-only and firmware-managed parameters are never offered for write.** Not greyed-out-but-writable, not "advanced mode" — not offered.
11. **An operation whose outcome is unknown is reported as unknown.** Cancellation, timeout and link loss produce "unknown — the vehicle may or may not have applied this", plus the next action. They never produce a success or a rollback claim the app cannot back up.
12. **One vehicle-mutating operation at a time**, serialised at the session (§3.3.1). Concurrent writes are refused with a clear reason, not queued silently.
13. **The session owns the update interlock (§7).** `UpdateService.IsBusy` is assigned by the flight-controller code at the composition root, reports busy for every state in §7.2, and fails safe to busy when the state is unknown. Leaving it at its default is a defect of the same severity as fire-and-forget writes: it lets a process restart land between a write and its verification, which no other rule in this list can recover from.

---

## 9. UI patterns

### 9.1 Surface mapping (prefer stock WinUI 3)

| Need | Stock surface | Notes |
|---|---|---|
| Persistent warning / verification verdict | `InfoBar` (`Severity` = `Informational` / `Success` / `Warning` / `Error`) | Inline, dismissible only when the condition is actually gone. Carries an action button for the next step. |
| Blocking confirmation for a destructive action | `ContentDialog` | Primary button labelled with the verb ("Write 14 parameters"), not "OK". Default button is the safe one. |
| Transient, non-blocking notice | `InfoBar` inline, or `TeachingTip` anchored to the control that caused it | Do not use dialogs for information. |
| Long operation with cancellation | `ProgressBar` (determinate when a count exists, e.g. parameter fetch) / `ProgressRing` (indeterminate, e.g. level cal) + an always-enabled Cancel | Cancel must remain responsive — it is the proof the UI thread is not blocked. |
| Parameter diff view | `ListView` with a data template, or `DataGrid` from CommunityToolkit when true column semantics are needed | Columns: name, current, incoming, verdict. Group by changed / unchanged / not-present. Selection drives partial-apply. |
| Compass panel | `Expander` per compass inside an `ItemsRepeater`/`ListView`; `InfoBadge` for per-compass status | One card per compass with identity, external/internal, priority slot, field length and calibration verdict. |
| Telemetry readouts | Plain `TextBlock` bound to formatted view-model strings, in a `Grid`; `ProgressBar` for bounded scalars | Fixed-width numeric formatting so digits do not jitter at refresh rate. |
| Top-level actions | `CommandBar` / `CommandBarFlyout` | Standard command surfaces, standard keyboard/accessibility behaviour for free. |
| App shell | `NavigationView` | One section per domain service (parameters, compass, calibration, telemetry). |
| Pending-update notice | Quiet `InfoBar` (`Informational`) or a status-bar item, never a dialog | Names the pending version; the action applies it only when the bench is idle. `UpdateState.Failed` is a quiet status line, not an error. Rules in §7.5. |

### 9.2 Stock-first rule

1. Use built-in WinUI 3 controls and command surfaces. Do not build custom chrome, custom title-bar buttons, custom dialogs or a bespoke control set.
2. Reach for `CommunityToolkit` only where the built-ins genuinely do not cover the need (the data grid for the parameter diff is the canonical example). Justify each toolkit dependency; do not pull it in for styling.
3. Custom drawing is acceptable only where there is no control at all (e.g. an attitude/horizon indicator or a calibration-coverage sphere).

### 9.3 Theming

1. **Light and dark must both work.** Use `{ThemeResource ...}` against the system brushes — `SystemFillColorCriticalBrush`, `SystemFillColorCautionBrush`, `SystemFillColorSuccessBrush`, `TextFillColorPrimaryBrush`, `TextFillColorSecondaryBrush`, `TextFillColorDisabledBrush`, `LayerFillColorDefaultBrush`, and the accent brushes.
2. **No hard-coded colours** in XAML or code-behind — no literal hex, no `Colors.Red`. A status colour that is legible in dark and unreadable in light is a defect.
3. Do not encode status by colour alone: pair every colour with an icon and text (accessibility, and it survives a theme the author did not test).
4. Verify both themes explicitly; theme switching at runtime must not leave stale brushes.

### 9.4 Long-operation UX

1. Disable only what the operation actually conflicts with. The whole window must not go dead.
2. Show what is happening in domain terms ("Fetching parameters 412 / 1180"), not "Please wait".
3. Cancellation always available, and its effect stated honestly (§3.3.2, §8.11).
4. Never show a progress bar that cannot advance. If no count exists, use an indeterminate `ProgressRing` plus a status line.

### 9.5 Confirmation and verdict UX

1. Destructive confirmations state: what will change, how many items, whether it is reversible, whether a reboot is required, and whether a snapshot was taken.
2. Verification verdicts are per-item and visible: verified / coalesced / rejected / not attempted (§8.6). A single aggregate "Done" over a partial batch is forbidden.
3. Prearm and status-text diagnostics are shown verbatim in a scrollable log **in addition to** any interpreted verdict — the operator must be able to see what the vehicle actually said.

### 9.6 Stale and unknown values

**A stale or unknown telemetry value must render distinctly from a real value. Never as a plausible-looking number.**

1. Track a timestamp per signal. Past a per-signal staleness deadline (a small multiple of its expected interval), the readout switches to a stale presentation: an em dash `—`, a "no data" label, reduced opacity via `TextFillColorDisabledBrush`, plus an age indication.
2. Sentinel values from the wire are converted to `null` in the domain layer, never to `0`, never to a default. A missing voltage renders as `—`, not `0.0 V`. A missing current renders as `—`, not `-0.01 A`.
3. Never freeze the last good value in place with no indication — a frozen plausible number is the most dangerous possible rendering.
4. On disconnect and during `Rebooting`, all live readouts go stale at once. No readout survives a link loss looking live.
5. The same rule applies to derived values: if any input is stale or unknown, the derived readout is stale.

### 9.7 Error text

1. Operator-facing error text states **the cause and the next action**. Not an exception message, not a stack trace, not an error code alone.
   - Bad: `System.UnauthorizedAccessException: Access to the port 'COM7' is denied.`
   - Good: "COM7 is in use by another application. Close Mission Planner or QGroundControl and try again."
   - Bad: "Write failed."
   - Good: "`COMPASS_PRIO1_ID` was rejected by the vehicle (permission denied). The value was not changed. Check that the vehicle is disarmed."
2. Include the technical detail, but subordinate to the plain-language cause — a "Details" expander, and always in the log.
3. Never invent a cause. If the cause is unknown, say the operation failed without a reported reason and give the diagnostic next step.

---

## 10. Testing without hardware

### 10.1 SITL as the development target

1. Run ArduPilot SITL and connect the app over **TCP or UDP** instead of serial. The connection/session layer (layer 3) and everything above it are transport-agnostic by design (§2) — this is one of the reasons the transport is its own layer.
2. Keep the transport selectable in developer builds (serial / TCP / UDP) so the same session, domain and view-model code runs against SITL and against a real board.
3. SITL is a simulated vehicle: identity that comes from simulated sensors will differ from real hardware. Expect a SITL-flavoured compass device type and bus type; never bake SITL-specific identity assumptions into production code paths.

### 10.2 What SITL can exercise

- MAVLink framing, heartbeat handling, session state machine, command/ack correlation, timeouts and retries.
- The full parameter workflow: list fetch with gap detection, read-by-name, write + read-back verification, diff, file import/export.
- Stream-interval requests and the resulting telemetry rates, unit conversions and coalescing.
- Calibration command dispatch and result handling, status-text reassembly and prearm-string parsing.
- Reboot **command** dispatch, the ack-before-reboot behaviour, session-state transitions, stream-interval re-issue and parameter re-fetch after the link returns.
- Threading, cancellation, UI responsiveness and the stale-value rendering (simply stop the simulator to produce staleness).
- **The busy interlock (§7.2) against a fake update path**: drive a SITL operation, assert `IsBusy()` is `true` for the whole of it — including the reboot window and an uncommitted run — and assert `ApplyAndRestart()` returns `false` throughout and `true` only once the bench is idle.

### 10.3 What SITL cannot exercise

| Not testable on SITL | Why |
|---|---|
| **USB re-enumeration behaviour** | There is no USB device. The surprise-removal, changed `COMn`, and two-stage bootloader→application appearance of §5 simply do not occur. |
| Device-change notifications (`WM_DEVICECHANGE` / `DeviceWatcher`) | No device arrives or departs. |
| VID/PID + `ArduPilot` manufacturer-string matching, `-BL` bootloader filtering | Nothing to enumerate. |
| Exclusive `SerialPort` open, `UnauthorizedAccessException` on a busy port | No serial port. |
| Baud being a no-op over USB CDC | No CDC layer. |
| Real sensor identity: `DEV_ID` bus/address/devtype decoding for physical compasses | Simulated sensors report simulated identity. |
| Real calibration outcomes and timing, real prearm failures from real hardware | Simulated dynamics. |
| Deployment and update behaviour: the unpackaged self-contained publish, the Velopack installer, `%LocalAppData%\ARDU_OTK` install, delta updates, and the `EnableMsixTooling` resource-copy dependency (`0xC000027B` if it regresses) | Nothing to do with the vehicle. Test it by publishing, packing with `vpk`, installing on a clean machine and publishing a test release — a run from the build directory reports `UpdateState.NotInstalled` and exercises none of it. There is **no MSIX package and no capability behaviour to test** (§6). |

### 10.4 Consequences for the test plan

1. Unit-test the pure logic against recorded byte streams and fixtures: parameter diff, float comparison, unit conversion, sentinel handling, staleness, status-text reassembly, mode mapping. These need neither SITL nor hardware.
2. **Unit-test the busy predicate (§7.4) — it is pure logic and there is no excuse for it being untested.** Cover: every state in §7.2 returns busy; an idle session returns free; a thrown exception inside the predicate returns **busy**, not free; and the delegate is actually assigned by the composition root (a test that constructs the app's services and asserts `IsBusy` is not the default `static () => false` catches the whole class of failure in §7.3).
3. Integration-test layers 3–5 against SITL.
4. **Hardware is mandatory before release** for: enumeration and identification (§4), the entire reboot-survival lifecycle (§5), calibration end-to-end, and every unverified item flagged in this file and its siblings. Ship no reboot-recovery code that has never met a real board.
5. Fake the device layer for automated tests of §5's *state machine* (a scriptable `IDeviceWatcher`/`ISerialPortFactory` can replay "port vanished → `-BL` arrived → application arrived on a different `COMn`"), but treat that as a test of your logic, not evidence the real behaviour is handled.
6. **An installed build is its own test target.** The startup path (`Program.cs` → `VelopackApp.Build().Run()` → `Application.Start`), the resource copy that `EnableMsixTooling` performs, and the whole update cycle only exist in a published, installed copy — none of them are covered by `dotnet run` or by any SITL test.
