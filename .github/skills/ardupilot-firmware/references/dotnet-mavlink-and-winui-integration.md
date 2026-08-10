# .NET MAVLink and WinUI 3 Integration

How this app is assembled on Windows: which MAVLink library to take, how the code is layered, how threads and the UI thread interact, how a USB flight controller is discovered and survives a reboot, what MSIX packaging does and does not require, and the safety/UI rules a tool that writes to flight hardware must obey.

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
   - ⚠️ Whether mavgen `--lang CS` output compiles cleanly on .NET 8 is **UNVERIFIED** (MissionPlanner's copy targets netstandard2.0). Verify before relying on it — generate, compile, and only then commit to this path.

### 1.3 🔴 Version / TFM pinning trap — read before adding the package

`Asv.Mavlink` moved its target framework forward inside the 4.0.x band. Taking "latest" on a .NET 8 project silently breaks the build or drags the whole app forward a runtime:

| Your TFM | Last usable `Asv.Mavlink` |
|---|---|
| `net8.0-windows…` | **4.0.17** |
| `net9.0-windows…` | **4.0.18** |
| `net10.0-windows…` | 4.0.19 and up, through 4.3.0 |

Rules:
1. **Never** add the package without an explicit `Version=` — no floating ranges, no `*`, no "latest".
2. Pin the version centrally (`Directory.Packages.props` / `Directory.Build.props`) so every project in the solution agrees.
3. If you deliberately want the newest `Asv.Mavlink` features, that is a **decision to move the app's TFM**, not a package bump. Treat it as such.
4. Record the TFM↔version pair in a comment next to the pin; the next person will otherwise "fix" it by upgrading.

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

Why this is not style pedantry: the wire types carry sentinels (`65535`, `-1`, `32767`), raw units (rad, mV, cA, mgauss, µs) and version-dependent field meanings. A binding straight onto a wire struct is how a sentinel gets rendered as a plausible number on screen (see §8.6).

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

## 6. Packaging and capabilities

| Fact | Consequence for this app |
|---|---|
| A default WinUI 3 MSIX package declares `<rescap:Capability Name="runFullTrust" />` ⇒ the process runs at **medium IL, not in an AppContainer**. | Capability enforcement is AppContainer-scoped, so it does not apply. |
| **`System.IO.Ports.SerialPort` works, and `DeviceCapability serialcommunication` is NOT required.** | Do not add the capability "to be safe". Adding it buys nothing and costs you the bug below. |
| `serialcommunication` gates **only** `Windows.Devices.SerialCommunication.SerialDevice`. | Only if you switch to that WinRT API do you declare `<DeviceCapability Name="serialcommunication"/>` (Win10 1809+ form). |
| Known **single-project-MSIX manifest-ordering bug**. | If a device capability is added anyway, expect manifest element-ordering validation failures at packaging time. The fix is element order in `Package.appxmanifest`, not more capabilities. |
| `System.IO.Ports` is an **out-of-band NuGet package on .NET 8+**. | It must be referenced explicitly; it is not in the shared framework. Pin its version like every other package. |

Procedure:
1. Use `System.IO.Ports.SerialPort` for the transport. Reference the `System.IO.Ports` package explicitly.
2. Leave `Package.appxmanifest` with the template's `runFullTrust` and **no** `serialcommunication` device capability.
3. If a future requirement forces `Windows.Devices.SerialCommunication`, declare the capability *and* budget time for the manifest-ordering bug; re-validate packaging immediately after the edit, before writing any code against the new API.
4. Device enumeration via `Win32_PnPEntity` / SetupAPI (§4.2) is likewise unaffected by capabilities at medium IL.

---

## 7. Safety rules for a tool that writes to flight hardware

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

---

## 8. UI patterns

### 8.1 Surface mapping (prefer stock WinUI 3)

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

### 8.2 Stock-first rule

1. Use built-in WinUI 3 controls and command surfaces. Do not build custom chrome, custom title-bar buttons, custom dialogs or a bespoke control set.
2. Reach for `CommunityToolkit` only where the built-ins genuinely do not cover the need (the data grid for the parameter diff is the canonical example). Justify each toolkit dependency; do not pull it in for styling.
3. Custom drawing is acceptable only where there is no control at all (e.g. an attitude/horizon indicator or a calibration-coverage sphere).

### 8.3 Theming

1. **Light and dark must both work.** Use `{ThemeResource ...}` against the system brushes — `SystemFillColorCriticalBrush`, `SystemFillColorCautionBrush`, `SystemFillColorSuccessBrush`, `TextFillColorPrimaryBrush`, `TextFillColorSecondaryBrush`, `TextFillColorDisabledBrush`, `LayerFillColorDefaultBrush`, and the accent brushes.
2. **No hard-coded colours** in XAML or code-behind — no literal hex, no `Colors.Red`. A status colour that is legible in dark and unreadable in light is a defect.
3. Do not encode status by colour alone: pair every colour with an icon and text (accessibility, and it survives a theme the author did not test).
4. Verify both themes explicitly; theme switching at runtime must not leave stale brushes.

### 8.4 Long-operation UX

1. Disable only what the operation actually conflicts with. The whole window must not go dead.
2. Show what is happening in domain terms ("Fetching parameters 412 / 1180"), not "Please wait".
3. Cancellation always available, and its effect stated honestly (§3.3.2, §7.11).
4. Never show a progress bar that cannot advance. If no count exists, use an indeterminate `ProgressRing` plus a status line.

### 8.5 Confirmation and verdict UX

1. Destructive confirmations state: what will change, how many items, whether it is reversible, whether a reboot is required, and whether a snapshot was taken.
2. Verification verdicts are per-item and visible: verified / coalesced / rejected / not attempted (§7.6). A single aggregate "Done" over a partial batch is forbidden.
3. Prearm and status-text diagnostics are shown verbatim in a scrollable log **in addition to** any interpreted verdict — the operator must be able to see what the vehicle actually said.

### 8.6 Stale and unknown values

**A stale or unknown telemetry value must render distinctly from a real value. Never as a plausible-looking number.**

1. Track a timestamp per signal. Past a per-signal staleness deadline (a small multiple of its expected interval), the readout switches to a stale presentation: an em dash `—`, a "no data" label, reduced opacity via `TextFillColorDisabledBrush`, plus an age indication.
2. Sentinel values from the wire are converted to `null` in the domain layer, never to `0`, never to a default. A missing voltage renders as `—`, not `0.0 V`. A missing current renders as `—`, not `-0.01 A`.
3. Never freeze the last good value in place with no indication — a frozen plausible number is the most dangerous possible rendering.
4. On disconnect and during `Rebooting`, all live readouts go stale at once. No readout survives a link loss looking live.
5. The same rule applies to derived values: if any input is stale or unknown, the derived readout is stale.

### 8.7 Error text

1. Operator-facing error text states **the cause and the next action**. Not an exception message, not a stack trace, not an error code alone.
   - Bad: `System.UnauthorizedAccessException: Access to the port 'COM7' is denied.`
   - Good: "COM7 is in use by another application. Close Mission Planner or QGroundControl and try again."
   - Bad: "Write failed."
   - Good: "`COMPASS_PRIO1_ID` was rejected by the vehicle (permission denied). The value was not changed. Check that the vehicle is disarmed."
2. Include the technical detail, but subordinate to the plain-language cause — a "Details" expander, and always in the log.
3. Never invent a cause. If the cause is unknown, say the operation failed without a reported reason and give the diagnostic next step.

---

## 9. Testing without hardware

### 9.1 SITL as the development target

1. Run ArduPilot SITL and connect the app over **TCP or UDP** instead of serial. The connection/session layer (layer 3) and everything above it are transport-agnostic by design (§2) — this is one of the reasons the transport is its own layer.
2. Keep the transport selectable in developer builds (serial / TCP / UDP) so the same session, domain and view-model code runs against SITL and against a real board.
3. SITL is a simulated vehicle: identity that comes from simulated sensors will differ from real hardware. Expect a SITL-flavoured compass device type and bus type; never bake SITL-specific identity assumptions into production code paths.

### 9.2 What SITL can exercise

- MAVLink framing, heartbeat handling, session state machine, command/ack correlation, timeouts and retries.
- The full parameter workflow: list fetch with gap detection, read-by-name, write + read-back verification, diff, file import/export.
- Stream-interval requests and the resulting telemetry rates, unit conversions and coalescing.
- Calibration command dispatch and result handling, status-text reassembly and prearm-string parsing.
- Reboot **command** dispatch, the ack-before-reboot behaviour, session-state transitions, stream-interval re-issue and parameter re-fetch after the link returns.
- Threading, cancellation, UI responsiveness and the stale-value rendering (simply stop the simulator to produce staleness).

### 9.3 What SITL cannot exercise

| Not testable on SITL | Why |
|---|---|
| **USB re-enumeration behaviour** | There is no USB device. The surprise-removal, changed `COMn`, and two-stage bootloader→application appearance of §5 simply do not occur. |
| Device-change notifications (`WM_DEVICECHANGE` / `DeviceWatcher`) | No device arrives or departs. |
| VID/PID + `ArduPilot` manufacturer-string matching, `-BL` bootloader filtering | Nothing to enumerate. |
| Exclusive `SerialPort` open, `UnauthorizedAccessException` on a busy port | No serial port. |
| Baud being a no-op over USB CDC | No CDC layer. |
| Real sensor identity: `DEV_ID` bus/address/devtype decoding for physical compasses | Simulated sensors report simulated identity. |
| Real calibration outcomes and timing, real prearm failures from real hardware | Simulated dynamics. |
| MSIX packaging behaviour and capability effects | Independent of the vehicle; test by packaging and running the packaged app. |

### 9.4 Consequences for the test plan

1. Unit-test the pure logic against recorded byte streams and fixtures: parameter diff, float comparison, unit conversion, sentinel handling, staleness, status-text reassembly, mode mapping. These need neither SITL nor hardware.
2. Integration-test layers 3–5 against SITL.
3. **Hardware is mandatory before release** for: enumeration and identification (§4), the entire reboot-survival lifecycle (§5), calibration end-to-end, and every unverified item flagged in this file and its siblings. Ship no reboot-recovery code that has never met a real board.
4. Fake the device layer for automated tests of §5's *state machine* (a scriptable `IDeviceWatcher`/`ISerialPortFactory` can replay "port vanished → `-BL` arrived → application arrived on a different `COMn`"), but treat that as a test of your logic, not evidence the real behaviour is handled.
