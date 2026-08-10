# Connection and Telemetry

Scope: opening the live link to an ArduPilot flight controller, the connect handshake, stream-rate policy, and the exact decode of every value the telemetry panel shows — attitude on all three axes, current, voltage, flight mode — plus the per-instance magnetometer feed consumed by the compass panel.

Everything here is operational. Message and command names, parameter names, field names, enum numbers and units are exact — do not paraphrase them in code.

---

## 1. Link setup

### 1.1 Serial over USB CDC — baud is a no-op

- **Baud rate does nothing over USB CDC.** The ChibiOS USB serial driver stores the host line coding and never applies it. Open at `115200` and move on. Do not expose a baud picker for USB-connected boards, and do not "try other baud rates" as a reconnect strategy — it cannot fix anything.
- Baud matters **only** for real UARTs and SiK radios: `SERIAL1`/`SERIAL2` default `57600`; `SERIAL0` (USB) is nominally `115200`.
- `System.IO.Ports.SerialPort` opens **exclusively** (`FileShare.None`). A second open of the same COM port fails — treat "access denied" as "another GCS (Mission Planner, QGC) already owns the port", not as a driver fault, and say so in the error.
- `System.IO.Ports` is an **out-of-band NuGet package** on .NET 8+; add the package reference explicitly.

### 1.2 UDP / TCP alternative

MAVLink is transport-agnostic; the same framing runs over UDP and TCP (companion computer, SITL, WiFi telemetry bridge). Keep the transport behind one interface so the handshake, stream-rate and decode layers are identical across all three. **Nothing in sections 2–8 depends on the transport.**

Library choice, version and TFM pinning, and the package names that must not be used, are owned by `dotnet-mavlink-and-winui-integration.md` §1. Do not restate that table here — two copies of a version pin will drift.

### 1.3 Board identification — what the handshake needs

The VID/PID table and the enumeration mechanics are owned by `dotnet-mavlink-and-winui-integration.md` §4. Three rules from it are repeated here because the handshake in §2 depends on them:

1. Match on the **VID set _and_ the manufacturer string `ArduPilot`**. Never on VID alone — `0x1209`/`0x0483` are shared with unrelated hardware.
2. Product string = board name. **The bootloader appends `-BL`** — a `…-BL` product string means the board is in DFU/bootloader mode, not running firmware; do not attempt a MAVLink handshake against it.
3. Cube-class boards **enumerate twice** after a reboot (bootloader first, then the app). Wait for the non-`-BL` entry.

### 1.4 Enumeration, reconnect and packaging — owned elsewhere

| Topic | Owner |
|---|---|
| Why `SerialPort.GetPortNames()` is insufficient, and what to query instead | `dotnet-mavlink-and-winui-integration.md` §4 |
| Reconnect after a reboot, USB re-enumeration, device-change watching | `dotnet-mavlink-and-winui-integration.md` §5 |
| MSIX packaging, `runFullTrust`, the `serialcommunication` capability | `dotnet-mavlink-and-winui-integration.md` §6 |

One consequence is repeated here because §3 depends on it: **after a reboot the device re-enumerates and the COM number can change** — re-resolve from the device instance path, never from the cached `COMn` — and **stream intervals do not survive the reboot**, so §3 must be re-run on every reconnect.

---

## 2. Connect handshake

The UI must not claim "connected" until the vehicle is identified. Ordered procedure:

1. **Resolve and open the transport** (section 1). For serial, open at `115200`, exclusive.
2. **Wait for the first `HEARTBEAT` (0).** It is the only proof there is a live autopilot on the other end. Until it arrives the state is "opening", not "connected". `HEARTBEAT` is emitted at 1 Hz by default — no request needed.
3. **Latch the system/component ids** from that first `HEARTBEAT` and address every subsequent command to them.
4. **Read `autopilot`.** **Gate all ArduPilot-specific decoding on `autopilot == 3` (`MAV_AUTOPILOT_ARDUPILOTMEGA`).** PX4 packs main/sub mode into the same `custom_mode` field, so decoding a PX4 heartbeat with a `COPTER_MODE` table produces confident nonsense. If `autopilot != 3`, connect but show the vehicle as unsupported and disable every configuration panel.
5. **Read `type` (`MAV_TYPE`) and select the mode table** (section 4.6). Unknown types ⇒ show "unknown"; **never default to copter**.
6. **Request `AUTOPILOT_VERSION` once** with `MAV_CMD_REQUEST_MESSAGE` (512), `param1 = 148`. This is a one-shot, not a stream. Use it for the firmware version/capability display and to drive every version-dependent probe elsewhere in the skill.
7. **Issue the stream-rate set** (section 3). Copter streams *nothing* until you do.
8. **Only now flip the UI to "connected"**, with the vehicle type, mode table and firmware version already populated. Panels bound to messages that have not yet arrived show "—" (section 8), not zeros.

Notes:

- `MAV_CMD_REQUEST_MESSAGE` (512) is also the right call for other one-shots such as `HOME_POSITION`.
- The boot banner STATUSTEXT (firmware string) may or may not fire automatically on reconnect — **verify against target firmware**; it can be forced with `MAV_CMD_DO_SEND_BANNER` (42428) if you need it deterministically.
- Reboot detection (used by the reboot-and-verify flows in sibling references): a `HEARTBEAT` gap followed by resumption, the boot banner, and `time_boot_ms` decreasing. `time_boot_ms` reset is a correct inference, **not a documented API guarantee — verify against target firmware.**

---

## 3. Stream rate policy

### 3.1 `MAV_CMD_SET_MESSAGE_INTERVAL` (511) is the only sane approach

| Param | Meaning |
|---|---|
| `param1` | Message ID to control |
| `param2` | **Interval in MICROSECONDS.** `-1` = disable, `0` = default rate |
| `param7` | Response target — `0` (flight-stack default) recommended |

- Supported **ArduPilot 4.0+** and is the documented best practice: it gives *"the most precise control and reduces bandwidth requirements"*. The `SRn_*` parameters are officially *"not recommended for most applications"*.
- Convert with `interval_us = 1000000 / Hz`.
- Only messages present in ArduPilot's `ap_message` enum can be rate-controlled.
- **ArduPilot clamps:** sub-millisecond intervals → 1 ms; intervals > 60 s → 60 s; and the effective rate is capped at ~80 % of the scheduler loop rate. A request is silently clamped, not rejected — never assume the rate you asked for is the rate you got (see 3.3).
- The `MAV_RESULT` ArduPilot returns for an **unmappable** message ID on cmd 511 is **unverified — verify against target firmware**; treat any result that is not `ACCEPTED` as "unsupported for this message" and degrade that readout rather than failing the connection.

### 3.2 🔴 Two hard facts that break naive implementations

- 🔴 **On Copter every `SRn_*` stream group defaults to 0 Hz — nothing streams until you ask.** A Copter connection with no `SET_MESSAGE_INTERVAL` calls shows an empty telemetry panel forever, and that is normal firmware behaviour, not a link fault. (Plane/Rover/Tracker default to 1 Hz, which is why a bug here can look "fixed" on a Plane bench test.)
- 🔴 **`SET_MESSAGE_INTERVAL` does not persist across reboot.** Re-issue the **entire** set after every reboot **and** every reconnect. Any flow that reboots the board (compass priority change, `AHRS_ORIENTATION` change, `PREFLIGHT_REBOOT_SHUTDOWN`) must re-run the stream-rate set as part of its reconnect path.

### 3.3 Verifying an interval — and the `0` asymmetry

- `MAV_CMD_GET_MESSAGE_INTERVAL` (510) → the vehicle replies with `MESSAGE_INTERVAL` (244).
- `MESSAGE_INTERVAL.interval_us`: `-1` = disabled, **`0` = not available**, `>0` = the actual interval in microseconds.
- ⚠️ **The asymmetry is a trap:** on command 511 `0` means *"use the default rate"*; on message 244 `0` means *"this message is not available"*. Do not share a single "zero means default" helper between the request and the readback paths.
- Use 510/244 to confirm what the clamps actually produced before reporting a rate to the user.

### 3.4 Legacy fallback and the shared-link hazard

- `REQUEST_DATA_STREAM` (66) is **SUPERSEDED (2015)**. Keep it *only* as a pre-4.0 fallback, selected from the `AUTOPILOT_VERSION` result, never as the default path.
- ⚠️ **Shared-link hazard:** a GCS using `REQUEST_DATA_STREAM` while a companion computer (or a second GCS) uses `SET_MESSAGE_INTERVAL` on the same link produces conflicting rates — each side keeps overriding the other. If the observed rate oscillates, suspect another client on the link before suspecting your own code.

### 3.5 Why 511 wins: the five needed messages span four `SRn_*` groups

Group membership on Copter:

| `SRn_*` group | Contains (relevant members in bold) |
|---|---|
| `SRn_RAW_SENS` | **`RAW_IMU`**, **`SCALED_IMU2`**, **`SCALED_IMU3`**, `SCALED_PRESSURE*` |
| `SRn_EXT_STAT` | **`SYS_STATUS`**, `POWER_STATUS`, `GPS_RAW_INT`, … |
| `SRn_EXTRA1` | **`ATTITUDE`**, `SIMSTATE`, `AHRS2`, `PID_TUNING` |
| `SRn_EXTRA2` | **`VFR_HUD`** (alone) |
| `SRn_EXTRA3` | `AHRS`, `SYSTEM_TIME`, **`BATTERY_STATUS`**, **`MAG_CAL_REPORT`**, **`MAG_CAL_PROGRESS`**, **`EKF_STATUS_REPORT`**, `VIBRATION` |

The messages this app needs are spread across **four different groups**. Raising a group to get one message drags every other member of that group onto the link at the same rate — wasted bandwidth on a 57600 baud radio, and no way to rate one member independently. `SET_MESSAGE_INTERVAL` addresses messages individually. Additionally, the `SRn` index ↔ port mapping is **not** a fixed `SERIALn` mapping (groups follow the MAVLink-enabled ports in ascending order) — **do not hard-code which `SRn_*` group your link uses.**

### 3.6 Recommended interval table

| Message | Interval (`param2`, µs) | Rate |
|---|---|---|
| `ATTITUDE` (30) | 100000–33333 | 10–30 Hz |
| `VFR_HUD` (74) | 200000 | 5 Hz |
| `SYS_STATUS` (1) | 500000–1000000 | 1–2 Hz |
| `BATTERY_STATUS` (147) | 1000000 | 1 Hz (round-robin ⇒ N batteries = N s per instance) |
| `RAW_IMU` / `SCALED_IMU2` / `SCALED_IMU3` | 200000 | 5 Hz per compass |
| `EKF_STATUS_REPORT` (193) | 1000000 | 1 Hz |
| `MAG_CAL_REPORT` / `MAG_CAL_PROGRESS` | default | raise only during calibration |
| `HEARTBEAT` | leave at 1 Hz | mode/armed are latched, not sampled |

```csharp
// Illustrative fragment. Re-run this whole set after every reboot and reconnect.
static readonly (uint MsgId, uint IntervalUs)[] StreamSet =
{
    (30,  100000),  // ATTITUDE
    (74,  200000),  // VFR_HUD
    (1,   500000),  // SYS_STATUS
    (147, 1000000), // BATTERY_STATUS
    (27,  200000),  // RAW_IMU        -> compass instance 0
    (116, 200000),  // SCALED_IMU2    -> compass instance 1
    (129, 200000),  // SCALED_IMU3    -> compass instance 2
    (193, 1000000), // EKF_STATUS_REPORT
};
// each entry -> MAV_CMD_SET_MESSAGE_INTERVAL (511): param1 = MsgId, param2 = IntervalUs, param7 = 0
```

---

## 4. Telemetry data model

One row per readout the UI shows. Never render a raw field without applying the sentinel guard first.

### 4.1 Attitude — all three axes

| Readout | Message | Field | Raw unit | Sentinel | UI unit | Conversion |
|---|---|---|---|---|---|---|
| Roll | `ATTITUDE` (30) | `roll` | rad | none | ° | `deg = rad × 57.29578` |
| Pitch | `ATTITUDE` (30) | `pitch` | rad | none | ° | `deg = rad × 57.29578` |
| Yaw / heading | `ATTITUDE` (30) | `yaw` | rad, −π…π | none | ° 0–360 | `deg = rad × 57.29578;  heading = (deg + 360) % 360` |
| Roll rate | `ATTITUDE` (30) | `rollspeed` | rad/s | none | °/s | `× 57.29578` |
| Pitch rate | `ATTITUDE` (30) | `pitchspeed` | rad/s | none | °/s | `× 57.29578` |
| Yaw rate | `ATTITUDE` (30) | `yawspeed` | rad/s | none | °/s | `× 57.29578` |

`ATTITUDE` also carries `time_boot_ms` — keep it; a decreasing `time_boot_ms` is one of the reboot indicators (section 2).

### 4.2 Voltage and current

| Readout | Message | Field | Raw unit | Sentinel / invalid | UI unit | Conversion |
|---|---|---|---|---|---|---|
| Pack voltage (preferred) | `BATTERY_STATUS` (147) | `voltages[10]` | mV per entry | entry `65535` (`UINT16_MAX`) = unused | V | `V = (Σ entries where e != 65535) / 1000` |
| Pack voltage, cells 11–14 — **added to the row above, not a separate readout** | `BATTERY_STATUS` (147) | `voltages_ext[4]` | mV | **unused = `0`** (different sentinel!) | V | sum non-zero entries into the same total, `/1000` |
| Current (preferred) | `BATTERY_STATUS` (147) | `current_battery` | cA (10 mA) | `-1` = not sent | A | `A = cA / 100` |
| Consumed | `BATTERY_STATUS` (147) | `current_consumed` | mAh | — | mAh | as-is |
| Battery temperature | `BATTERY_STATUS` (147) | `temperature` | cdegC | `32767` = unknown | °C | `°C = cdegC / 100` |
| Remaining | `BATTERY_STATUS` (147) | `battery_remaining` | % | `-1` = unknown | % | as-is |
| Voltage (legacy) | `SYS_STATUS` (1) | `voltage_battery` | mV | `65535` = not sent | V | `/1000` |
| Current (legacy) | `SYS_STATUS` (1) | `current_battery` | cA | `-1` = not sent | A | `/100` |
| Remaining (legacy) | `SYS_STATUS` (1) | `battery_remaining` | % | `-1` = unknown | % | as-is |

⚠️ For current, **`-1` is the only "not sent" sentinel — other negative values are legitimate charging current** and must be displayed, not filtered out.

### 4.3 Flight mode and armed state

| Readout | Message | Field | Decode |
|---|---|---|---|
| Armed | `HEARTBEAT` (0) | `base_mode` | **`armed = (base_mode & 0x80) != 0`** (`MAV_MODE_FLAG_SAFETY_ARMED`) |
| Flight mode | `HEARTBEAT` (0) | `custom_mode` | plain integer (`flightmode->mode_number()`), **not a bitfield**; look up in the table selected by `MAV_TYPE` |
| Vehicle class | `HEARTBEAT` (0) | `type` | `MAV_TYPE` → mode table (4.6) |
| Stack gate | `HEARTBEAT` (0) | `autopilot` | decode only when `== 3` (`MAV_AUTOPILOT_ARDUPILOTMEGA`) |

- ArduPilot always sets `MAV_MODE_FLAG_CUSTOM_MODE_ENABLED (0x01)`; presence of that flag is not by itself evidence of anything.
- ⚠️ **Do not infer arming from `system_status` / `MAV_STATE`.** Copter reports `STANDBY` whenever `land_complete`, which is also true while **armed** on the ground. The only correct armed test is `base_mode & 0x80`.

### 4.4 Per-instance magnetometer (feeds the compass panel)

| Compass instance | Message | Mag fields | Unit | Conversion |
|---|---|---|---|---|
| 0 | `RAW_IMU` (27) | `xmag`, `ymag`, `zmag` | **milligauss** | µT = `mgauss × 0.1` |
| 1 | `SCALED_IMU2` (116) | `xmag`, `ymag`, `zmag` | milligauss | µT = `mgauss × 0.1` |
| 2 | `SCALED_IMU3` (129) | `xmag`, `ymag`, `zmag` | milligauss | µT = `mgauss × 0.1` |

- The index is the **compass instance = priority slot**, **not** the IMU index. ArduPilot's guard is `compass.get_count() > instance`.
- **Units are milligauss in all three, including `RAW_IMU`** — ArduPilot passes `get_field()` (documented in milligauss) through unconverted even though the `RAW_IMU` spec declares no unit. Do not apply a different scale to `RAW_IMU`.
- ⚠️ **On a 3-IMU / 2-compass vehicle `SCALED_IMU3` carries IMU3 accel + gyro with a ZEROED mag vector.** An all-zero mag triple must **not** be rendered as a third compass. Cross-check the number of compasses (device-id/priority parameters, owned by the compass reference) before binding a third row.
- Field length = `sqrt(x² + y² + z²)` mgauss. Prearm band: **expected 530, min 185, max 875** (docs say ">874 / <185" — off by one; trust the source).
- Accel/gyro in `SCALED_IMU2`/`SCALED_IMU3`: accel is `mG` (milli-g) ⇒ `g = mG / 1000`; gyro is `mrad/s` ⇒ `°/s = mrad/s × 0.0572958`.

### 4.5 Other panel inputs

| Readout | Message | Field | Unit | Caveat |
|---|---|---|---|---|
| Heading | `VFR_HUD` (74) | `heading` | ° 0–360 | Same angle as `wrap_360(degrees(ATTITUDE.yaw))`. **TRUE north, not magnetic**, despite the spec calling it "compass units". |
| Ground speed | `VFR_HUD` (74) | `groundspeed` | m/s | — |
| "Airspeed" | `VFR_HUD` (74) | `airspeed` | m/s | **This is GPS ground speed on vehicles without an airspeed sensor — never label it "true airspeed".** |
| Throttle | `VFR_HUD` (74) | `throttle` | % | `abs()`-ed by ArduPilot; reverse throttle loses its sign. |
| Altitude | `VFR_HUD` (74) | `alt` | m | AMSL normally, but **relative on Copter when `DEV_OPTIONS` sets `DevOptionVFR_HUDRelativeAlt` — the message gives no way to tell.** Use `GLOBAL_POSITION_INT.relative_alt` when you need certainty. |
| Climb rate | `VFR_HUD` (74) | `climb` | m/s | positive = climbing |
| EKF variances | `EKF_STATUS_REPORT` (193) | `velocity_variance`, `pos_horiz_variance`, `pos_vert_variance`, `compass_variance`, `terrain_alt_variance` | ratio | *"0 = very trustworthy, >1.0 = very untrustworthy"*, **not clamped to 1.0**. Failsafe fires when any **two** exceed `FS_EKF_THRESH` (default 0.8) for 1 s. UI: `>0.8` red, `0.5–0.8` amber. |

### 4.6 Mode tables — full numeric maps

**Use a `Dictionary<uint,string>` (a map), NOT array indexing.** Several numbers are permanently unassigned; array indexing produces off-by-one mislabels or index-out-of-range.

**`COPTER_MODE`**

| # | Mode | # | Mode | # | Mode |
|---|---|---|---|---|---|
| 0 | STABILIZE | 13 | SPORT | 22 | FLOWHOLD |
| 1 | ACRO | 14 | FLIP | 23 | FOLLOW |
| 2 | ALT_HOLD | 15 | AUTOTUNE | 24 | ZIGZAG |
| 3 | AUTO | 16 | POSHOLD | 25 | SYSTEMID |
| 4 | GUIDED | 17 | BRAKE | 26 | AUTOROTATE |
| 5 | LOITER | 18 | THROW | 27 | AUTO_RTL |
| 6 | RTL | 19 | AVOID_ADSB | 28 | TURTLE |
| 7 | CIRCLE | 20 | GUIDED_NOGPS | | |
| 9 | LAND | 21 | SMART_RTL | | |
| 11 | DRIFT | | | | |

**`8`, `10` and `12` are permanently unassigned.** `27 AUTO_RTL` is **not pilot-selectable** — AUTO reports it during a `DO_LAND_START` sequence; label it accordingly rather than offering it as a mode to set.

**`PLANE_MODE`**

| # | Mode | # | Mode | # | Mode |
|---|---|---|---|---|---|
| 0 | MANUAL | 11 | RTL | 20 | QLAND |
| 1 | CIRCLE | 12 | LOITER | 21 | QRTL |
| 2 | STABILIZE | 13 | TAKEOFF | 22 | QAUTOTUNE |
| 3 | TRAINING | 14 | AVOID_ADSB | 23 | QACRO |
| 4 | ACRO | 15 | GUIDED | 24 | THERMAL |
| 5 | FBWA | 16 | INITIALIZING | 25 | LOITER_ALT_QLAND |
| 6 | FBWB | 17 | QSTABILIZE | 26 | AUTOLAND |
| 7 | CRUISE | 18 | QHOVER | | |
| 8 | AUTOTUNE | 19 | QLOITER | | |
| 10 | AUTO | | | | |

**`9` is unassigned.**

**`ROVER_MODE`**

| # | Mode | # | Mode |
|---|---|---|---|
| 0 | MANUAL | 9 | CIRCLE |
| 1 | ACRO | 10 | AUTO |
| 3 | STEERING | 11 | RTL |
| 4 | HOLD | 12 | SMART_RTL |
| 5 | LOITER | 15 | GUIDED |
| 6 | FOLLOW | 16 | INITIALIZING |
| 7 | SIMPLE | | |
| 8 | DOCK | | |

**`2`, `13`, `14` are unassigned.**

**`MAV_TYPE` → which table**

| Table | `MAV_TYPE` values |
|---|---|
| Copter | `2 QUADROTOR`, `3 COAXIAL`, `4 HELICOPTER`, `13 HEXAROTOR`, `14 OCTOROTOR`, `15 TRICOPTER`, `29 DODECAROTOR`, `35 DECAROTOR`, **`43 GENERIC_MULTIROTOR`** |
| Plane | `1 FIXED_WING` and VTOL types `19`, `20`, `21`, `22`, `23`, `24` |
| Rover | `10 GROUND_ROVER`, `11 SURFACE_BOAT` |
| Tracker | `5` |
| Sub | `12` |
| Blimp | `7` |

⚠️ **pymavlink's own map is incomplete**: it omits `43 GENERIC_MULTIROTOR` from the copter set and maps only `21` of the VTOL types to Plane. **Patch these locally** rather than trusting a generated map.

⚠️ **Unknown `MAV_TYPE` ⇒ show "unknown". Never fall back to the copter table.** A mislabelled mode is worse than no label.

```csharp
// Illustrative fragment.
static readonly IReadOnlyDictionary<uint, string> CopterModes = new Dictionary<uint, string>
{
    [0]="STABILIZE", [1]="ACRO", [2]="ALT_HOLD", [3]="AUTO", [4]="GUIDED", [5]="LOITER",
    [6]="RTL", [7]="CIRCLE", [9]="LAND", [11]="DRIFT", [13]="SPORT", [14]="FLIP",
    [15]="AUTOTUNE", [16]="POSHOLD", [17]="BRAKE", [18]="THROW", [19]="AVOID_ADSB",
    [20]="GUIDED_NOGPS", [21]="SMART_RTL", [22]="FLOWHOLD", [23]="FOLLOW", [24]="ZIGZAG",
    [25]="SYSTEMID", [26]="AUTOROTATE", [27]="AUTO_RTL", [28]="TURTLE",
}; // 8, 10, 12 intentionally absent

string ModeText(uint custom) => CopterModes.TryGetValue(custom, out var m) ? m : $"MODE {custom}";
```

### 4.7 Conversion cheat sheet

| From | To | Operation |
|---|---|---|
| rad | ° | `× 57.29578` |
| yaw −π…π | heading 0–360° | `(deg + 360) % 360` |
| mV | V | `/ 1000` (guard `65535`) |
| **cA** | **A** | **`/ 100`** (guard `-1`; other negatives are real charging current) |
| cdegC | °C | `/ 100` (guard `32767`) |
| mgauss | µT | `× 0.1` |
| mG (milli-g) | g | `/ 1000` |
| mrad/s | °/s | `× 0.0572958` |
| Hz | µs | `1000000 / Hz` |

---

## 5. Battery

### 5.1 Prefer `BATTERY_STATUS` keyed by `id`

- mavlink.io is explicit: *"GCS should not rely on the value of `SYS_STATUS`."* `SYS_STATUS` is **discouraged/legacy** (neither message is formally `<deprecated>`).
- `SYS_STATUS` always reports **primary instance 0 only** — there is no way to see a second battery through it.
- `BATTERY_STATUS` adds mAh consumed, temperature, faults, and an **unambiguous instance `id`**. Key your battery model on `id`; do not assume the first `BATTERY_STATUS` you see is instance 0.
- On a single-battery ArduPilot vehicle the two messages agree (both come from `gcs_voltage()`, sag-corrected when `BATT_OPTIONS` enables it). Use `SYS_STATUS` only as a fallback when `BATTERY_STATUS` has not been seen.

### 5.2 The `voltages[]` summation rule — and two different sentinels

**Pack voltage = the SUM of all `voltages[]` entries that are not `65535`.**

- Unused cell slots are `UINT16_MAX` = `65535`.
- When the driver has **no per-cell data**, ArduPilot **splits the pack voltage across multiple entries** using `max_cell_mV = 0xFFFE` (65534) per entry.
- ⇒ 🔴 **Reading `voltages[0]` alone is WRONG above 65.534 V.** A 14S pack reads ~51 V and works by luck; a 20S pack silently reads 65.534 V forever. Always sum.
- `voltages_ext[4]` uses a **different sentinel: unused = `0`**, not `65535`. Do not reuse the same filter predicate for both arrays.

```csharp
// Illustrative fragment.
int mv = 0;
foreach (var v in msg.voltages)      if (v != 65535) mv += v;   // sentinel 65535
foreach (var v in msg.voltages_ext)  if (v != 0)     mv += v;   // sentinel 0 (different!)
double packVolts = mv / 1000.0;
```

### 5.3 Round-robin emission

ArduPilot emits **one battery instance per tick, round-robin**. At the recommended 1 Hz `BATTERY_STATUS` interval, an N-battery vehicle refreshes each instance only once every **N seconds**. Consequences:

- Set the staleness timeout for battery readouts from the *effective per-instance* period, not from the requested interval (section 8).
- Do not treat "instance 1 has not updated in 1 s" as a fault on a 2-battery vehicle.
- Raise the interval if you need faster per-instance refresh; the round-robin divisor still applies.

### 5.4 `SYS_STATUS` current overflow

`SYS_STATUS.current_battery` is `int16` in centiamps ⇒ it **overflows above 327.67 A**. On a high-current vehicle the legacy readout wraps to nonsense. `BATTERY_STATUS.current_battery` is the correct source for high-current airframes. ArduPilot also **forces `SYS_STATUS` current to `-1` when the battery is unhealthy** — render that as "—", never as 0 A.

---

## 6. Attitude

### 6.1 Frame and sign conventions

`ATTITUDE` (30) uses the aeronautical frame, right-handed with **Z down**:

- **`+roll` = right wing down**
- **`+pitch` = nose up**
- **`+yaw` = clockwise seen from above**

Roll and pitch are relative to the local **NED earth frame** — **directly usable as a level/horizon readout with no transform.** Do not apply any rotation before display.

### 6.2 Yaw is TRUE north

- `yaw` is referenced to **TRUE north**, not magnetic: `calculate_heading()` returns `wrap_PI(heading + _declination)`, and `COMPASS_AUTODEC` (default 1) pulls declination from the built-in WMM table.
- `VFR_HUD.heading` is the **same angle** as `wrap_360(degrees(ATTITUDE.yaw))`, expressed 0–360. Despite the MAVLink spec calling it "compass units", **it is not magnetic.** Label the UI "True heading".
- Consequence for the compass/cal panels: any yaw the operator supplies to `MAV_CMD_FIXED_MAG_CAL_YAW` must likewise be **true**, not a raw phone-compass magnetic reading.

### 6.3 Mounting trim

Board-vs-airframe mounting error is what `AHRS_TRIM_X` / `AHRS_TRIM_Y` remove (written by level calibration). **A persistent non-zero level reading on a level vehicle is a real trim issue** — surface it, do not zero it in software.

### 6.4 When `ATTITUDE_QUATERNION` (31) is worth requesting

- Fields: `q1..q4 = w, x, y, z`.
- **It is not in any ArduPilot stream group** — the only way to get it is `MAV_CMD_SET_MESSAGE_INTERVAL` (511).
- Request it only when you need gimbal-lock-free interpolation (a smooth 3D attitude visualisation) or when you need to compose rotations. For a numeric roll/pitch/yaw panel, `ATTITUDE` is sufficient and cheaper.
- ArduPilot sends `repr_offset_q = {0,0,0,0}` — that is **invalid, not identity**. Treat an all-zero `repr_offset_q` as "no offset" and skip the composition; do not normalise it or you will divide by zero.

### 6.5 🔴 `AHRS2` must never be a silent fallback

- `AHRS2` (178, `ardupilotmega`) is the **secondary/backup estimator** (DCM when EKF3 is primary), not a second copy of the primary attitude.
- 🔴 **If no secondary estimator exists, `AHRS2` is never sent at all.** Code that falls back to `AHRS2` when `ATTITUDE` stops will simply show nothing, or — worse, when a secondary does exist — show a *different* estimator's attitude while the label still says "attitude".
- Rule: **`ATTITUDE` is the horizon. `AHRS2` is a divergence diagnostic only** — compare the two and warn on disagreement, in its own clearly-labelled readout. Never substitute it.
- `AHRS` (163) is **DCM health, not attitude**: `error_rp`, `error_yaw`, gyro drift. Do not bind it to attitude fields.
- `AHRS3` exists in the dialect but **ArduPilot never sends it.** Do not implement it.

---

## 7. `STATUSTEXT` ingestion

`STATUSTEXT` (253) is how the vehicle reports prearm failures, calibration results and errors. Every panel in this app depends on it.

### 7.1 `MAV_SEVERITY` → UI severity — **lower value = more severe**

| Value | `MAV_SEVERITY` | Suggested UI treatment |
|---|---|---|
| 0 | EMERGENCY | Critical / red, latch until acknowledged |
| 1 | ALERT | Critical / red |
| 2 | CRITICAL | Critical / red |
| 3 | ERROR | Error / red |
| 4 | WARNING | Warning / amber |
| 5 | NOTICE | Info |
| 6 | INFO | Info |
| 7 | DEBUG | Verbose, hidden by default |

⚠️ **The ordering is inverted relative to most logging frameworks.** A naive `if (severity >= threshold)` filter shows DEBUG and hides EMERGENCY. Map explicitly through a table; never compare severity to a log-level enum from another library.

### 7.2 Decoding `text` — char[50] **without null termination**

`text` is `char[50]`, **UTF-8, and is NOT null-terminated** when the message fills the field. Decode procedure:

1. Take the 50 bytes.
2. Truncate at the **first `0x00`** if one exists; otherwise use all 50 bytes.
3. Decode as UTF-8 (not ASCII, not the ANSI code page).
4. Do **not** `TrimEnd('\0')` a string that was already decoded with trailing NULs — decode-then-trim leaves `\0` inside .NET strings and breaks equality comparisons against prearm strings.

```csharp
// Illustrative fragment.
int len = Array.IndexOf(raw, (byte)0, 0, 50);
if (len < 0) len = 50;                      // no terminator: field is full
string chunk = Encoding.UTF8.GetString(raw, 0, len);
```

### 7.3 Multi-chunk reassembly via `id` / `chunk_seq`

The MAVLink v2 extension fields carry messages longer than 50 chars:

- `id` — non-zero identifies a multi-chunk message; all chunks of one message share the same `id`.
- `chunk_seq` — **starts at 0** and increments.
- The **last chunk is the one containing a null character** (or the sequence simply ends).
- Chunks with `id == 0` are single, complete messages.

Procedure:

1. If `id == 0`, emit the decoded text immediately.
2. Otherwise buffer by `id`, ordered by `chunk_seq`.
3. Emit the concatenation when a chunk containing a null character arrives.
4. Apply a timeout (a few seconds) to abandoned partial buffers so a dropped chunk cannot leak or block later messages sharing the `id`.

### 7.4 🔴 Prearm string matching requires reassembly first

🔴 **State plainly: matching prearm strings against raw `STATUSTEXT` chunks is unreliable.** A prearm message longer than 50 characters arrives split; the matcher sees two fragments, neither of which equals the expected string, and reports "no prearm failure" while the vehicle is refusing to arm. **Reassemble chunks before any string matching**, and match against the reassembled text only.

Additional facts for the panels that consume prearm text:

- The `PreArm: ` prefix is added by `AP_Arming::check_failed` — match on the reassembled full line including the prefix.
- Failing prearms are **re-broadcast about every 30 s while disarmed** (suppressible via `ARMING_OPTIONS` bit 1). A panel that waits for a prearm message must therefore wait up to ~30 s, or force the checks with `MAV_CMD_RUN_PREARM_CHECKS` (401) — see the arming/prearm reference.
- Calibration diagnostics arrive the same way (e.g. `Mag: no position available`, `Trim OK: roll=%.2f pitch=%.2f yaw=%.2f`). Route STATUSTEXT to a shared bus that individual panels subscribe to; do not have each panel open its own decoder.

---

## 8. Freshness and link loss

**Hard rule: every displayed telemetry value carries a last-updated timestamp, and the UI degrades visibly when that value goes stale. Showing a stale number as if it were live is a defect, not a cosmetic issue.** This tool exists to tell an operator the truth about a flight controller; a frozen "12.4 V" after the link dropped is exactly the failure mode it must prevent.

### 8.1 Model requirement

Wrap every readout, not every message:

```csharp
// Illustrative fragment.
sealed record Reading<T>(T Value, DateTimeOffset UpdatedUtc);

bool IsStale<T>(Reading<T>? r, TimeSpan timeout, DateTimeOffset now)
    => r is null || (now - r.UpdatedUtc) > timeout;
```

- Stamp `UpdatedUtc` from the **host monotonic clock at packet receipt**, not from `time_boot_ms` (which resets on reboot).
- A value that has **never** arrived is not zero — it is "—". Never initialise a displayed field to `0`.

### 8.2 Staleness timeouts

These are **app-side policy values** derived from the requested intervals in section 3.6, not firmware constants. Baseline rule: **timeout = 3 × the requested interval, with a 1 s floor**, then widen where the firmware's own emission pattern demands it.

| Readout | Source | Requested interval | Suggested staleness timeout | Why |
|---|---|---|---|---|
| Roll / pitch / yaw | `ATTITUDE` (30) | 33–100 ms | 1 s | Fast stream; anything over a second is visibly frozen. |
| Heading / speed / throttle | `VFR_HUD` (74) | 200 ms | 1 s | — |
| Voltage / current | `BATTERY_STATUS` (147) | 1 s | **3 s × battery count** | Round-robin emission: N batteries ⇒ N s per instance (5.3). |
| Voltage / current (legacy) | `SYS_STATUS` (1) | 0.5–1 s | 3 s | — |
| Per-compass mag | `RAW_IMU` / `SCALED_IMU2` / `SCALED_IMU3` | 200 ms | 1.5 s | — |
| EKF variances | `EKF_STATUS_REPORT` (193) | 1 s | 3 s | — |
| Flight mode / armed | `HEARTBEAT` (0) | 1 Hz default | 3 s | Latched values, but a missing HEARTBEAT is link loss. |

### 8.3 Degradation behaviour

1. **Per-readout stale** — dim or strike through the value and show its age ("2.4 s ago"). Keep the last value visible but unmistakably not-live. Never keep animating a gauge from a stale number.
2. **Never-received** — render `—`. Distinguish this from stale: "never arrived" usually means the `SET_MESSAGE_INTERVAL` for that message was rejected or clamped out (3.1), which is an actionable diagnostic.
3. **HEARTBEAT lost (> 3 s)** — declare **link loss**: mark the whole telemetry panel stale at once, stop accepting user actions that send commands, and show a reconnect affordance. Do not silently retry commands whose ACKs can no longer arrive.
4. **Reconnect** — clear every cached reading (do not carry pre-reboot values across), re-run the handshake (section 2) and **re-issue the entire stream-rate set** (3.2) before un-dimming anything.
5. **Reboot in progress** — a deliberate reboot is not link loss: label it as "rebooting", close the serial port immediately, and re-resolve the device by instance path + VID/PID rather than the cached `COMn` (1.5).

### 8.4 Do not paper over gaps

- No interpolation, no smoothing across a gap, no "last known good" that outlives its timeout.
- Do not restart a staleness timer on an unrelated message. A `HEARTBEAT` arriving does **not** refresh the battery reading.
- Log the gap. An intermittent link that recovers before the user notices is still a finding worth showing in the session log.
