# IMU Level Calibration, Reboot, and Proving the Board Is Actually Calibrated

Scope: run board-level (trim) calibration, reboot the flight controller, and produce a defensible verdict that there are **no IMU or compass calibration errors**.

Out of scope — do not duplicate, reference these files instead:

| Topic | Owner file |
|---|---|
| Compass ordering, `COMPASS_PRIOx_ID`, `EXTERNAL` flags, `DEV_ID` decoding | `compass-topology-and-flags.md` |
| Compass calibration workflows and cal-data transfer | `compass-calibration-transfer.md` |
| `PARAM_REQUEST_READ` / `PARAM_SET` mechanics, param files, float compare | `parameter-protocol-and-profiles.md` |
| COM-port re-enumeration, `DeviceWatcher`, VID/PID re-resolution | `dotnet-mavlink-and-winui-integration.md` |
| `STATUSTEXT` chunk reassembly (`id` + `chunk_seq`) | `connection-and-telemetry.md` |

This file **owns** the reboot command itself and the post-reboot verification verdict.

---

## 1. Level calibration command

`MAV_CMD_PREFLIGHT_CALIBRATION` = **241**.

### 1.1 🔴 `param5 = 2` IS LEVEL CALIBRATION

> **`param5` (Accelerometer) = `2` → TRIM (board level). This — and only this — is the "level calibration" the app runs.**
> Sending `1` runs a **full 6-position accel calibration** (needs the operator to rotate the board through six attitudes and rewrites the accel offsets/scales).
> Sending `4` runs **simple** accel calibration and **resets the trim to zero**.
> Sending `76` is **force save**.
> Confusing these silently destroys existing accelerometer calibration. Hard-code `2`; never let a UI value bind directly to this field.

`param5` enum:

| Value | Meaning | Use here? |
|---|---|---|
| `0` | NONE | — |
| `1` | FULL (6-position accel cal) | ❌ not level cal |
| **`2`** | **TRIM (board level)** | ✅ **this is level calibration** |
| `3` | TEMPERATURE | ❌ |
| `4` | SIMPLE | ❌ not level cal; also zeroes trim |
| `76` | FORCE_SAVE | ❌ |

### 1.2 Full `param1..param7` table

| Field | Value for level cal | Meaning of a non-zero value |
|---|---|---|
| `param1` | `0` | `1` = gyro calibration |
| `param2` | `0` | `76` = compass force-save. ⚠️ **`param2 == 1` does NOT start a compass calibration in ArduPilot master** — use `MAV_CMD_DO_START_MAG_CAL` (see `compass-calibration-transfer.md`). Do not offer `param2=1` as a "calibrate compass" button. |
| `param3` | `0` | `1` = barometer. This is the **only** path in command 241 that can return `IN_PROGRESS`. |
| `param4` | `0` | (not used by this workflow) |
| **`param5`** | **`2`** | Accelerometer enum, see §1.1 |
| `param6` | `0` | `1` = CompassMot (Copter) |
| `param7` | `0` | `1` = ESC calibration |

⚠️ `param2 = 76` maps to `Compass::force_save_calibration()` — the brief marks the MAVLink reachability of that path as **UNVERIFIED; verify against target firmware** before relying on it. It is not part of this workflow.

### 1.3 `COMMAND_LONG` → `COMMAND_INT` conversion

ArduPilot converts `COMMAND_LONG` to `COMMAND_INT` internally and takes the accel selector as `x = (int32_t)param5`. **Sending `COMMAND_LONG` with `param5 = 2` works** and lands in `x` correctly. No client-side conversion is needed; do not pre-scale `param5` or place it in a lat/lon-style 1e7 encoding.

```csharp
// Illustrative fragment only.
const ushort MAV_CMD_PREFLIGHT_CALIBRATION = 241;
const float ACCEL_CAL_TRIM = 2f;   // NEVER 1 (6-position) / 4 (simple) / 76 (force save)

var cmd = new CommandLong {
    Command = MAV_CMD_PREFLIGHT_CALIBRATION,
    Param1 = 0, Param2 = 0, Param3 = 0, Param4 = 0,
    Param5 = ACCEL_CAL_TRIM,          // → (int32_t)x on the vehicle
    Param6 = 0, Param7 = 0,
    TargetSystem = sysId, TargetComponent = compId, Confirmation = 0
};
```

---

## 2. Preconditions and gating

The button must be **disabled** until every row below is satisfied, and the reason must be shown next to it.

| # | Precondition | How the app checks it | What the operator is told |
|---|---|---|---|
| 1 | **Disarmed** | `HEARTBEAT.base_mode & 0x80` (`MAV_MODE_FLAG_SAFETY_ARMED`) must be `0`. Do **not** infer arming from `system_status`/`MAV_STATE`. | "Disarm the vehicle before levelling." If sent anyway the vehicle emits STATUSTEXT **`Disarm to allow calibration`** and returns `MAV_RESULT_FAILED`. |
| 2 | **`AHRS_ORIENTATION` already set AND the board rebooted since** | Read `AHRS_ORIENTATION` (see `parameter-protocol-and-profiles.md`); if it was written in this session, a reboot is mandatory first. | ⚠️ `AHRS_ORIENTATION` *"takes affect on next boot. After changing you will need to re-level your vehicle."* **Set orientation → reboot → then level.** Levelling before the reboot bakes the wrong mounting into `AHRS_TRIM_*`. |
| 3 | **Physically level** | Cannot be measured by the app beyond `ATTITUDE` roll/pitch. Show the live roll/pitch readout while gating. | "Place the airframe on a level surface in its normal flight attitude. The board is trimmed to *this* attitude — level the **airframe**, not the board." |
| 4 | **Motionless** | Watch `ATTITUDE.rollspeed`/`pitchspeed`/`yawspeed` and require them near zero for a settling window before enabling. | "Do not touch or move the vehicle during the calibration." |
| 5 | **Correction ≤ 10°, roll/pitch only** | Pre-check the live `ATTITUDE` roll/pitch magnitude; warn above ~10°. | ⚠️ Level cal **corrects roll and pitch only, maximum 10°**. A larger tilt fails with `trim over maximum of 10 degrees`. Yaw is not levelled by this command. |
| 6 | **≥ 5 s since the previous accel calibration** | Timestamp every accel/level calibration the app issues and hold the button for 5000 ms after. | `calibrate_trim()` returns `MAV_RESULT_TEMPORARILY_REJECTED` if a calibration is running **or if < 5000 ms have elapsed since the last accel calibration**. Show a countdown rather than letting the user hit a rejection. |
| 7 | **QuadPlane only:** `Q_TRIM_PITCH` not set | If the failure STATUSTEXT `Cannot calibrate with Q_TRIM_PITCH set` appears, surface it verbatim. | "Clear `Q_TRIM_PITCH` before levelling." |

---

## 3. Execution and response handling

### 3.1 The command blocks

The handler performs a gyro calibration, a 100 ms settle, then 400 ms of averaged samples **in the main thread**, wrapped in `EXPECT_DELAY_MS(30000)`.

Consequences the client must design for:

1. **Exactly one `COMMAND_ACK` arrives, and only after the calibration has finished.** There is **no `IN_PROGRESS` on this path** (only `param3 = 1`, the barometer path, can return `IN_PROGRESS`).
2. **Set the client timeout to the worst case, not the expected case.** Use ≥ 30 s to match `EXPECT_DELAY_MS(30000)`. The brief marks the actual wall-clock duration (derived ≈ 0.5 s plus gyro cal) as **UNVERIFIED — verify against target firmware**; never size the timeout from it.
3. Other telemetry may stall while the main thread is blocked. Do not treat a heartbeat hiccup during this window as a disconnect; do not tear down the link.
4. Do **not** retry / re-send the command on a slow reply — a second command inside 5000 ms of the first will be `TEMPORARILY_REJECTED` (§2 row 6). One shot, one long wait.

### 3.2 `MAV_RESULT` outcomes

| Result | Cause | App action |
|---|---|---|
| `MAV_RESULT_ACCEPTED` | Trim computed and saved | Proceed to §5 reboot |
| `MAV_RESULT_FAILED` | Gyro cal failed · accelerometer went unhealthy mid-sample · computed trim > 10° · vehicle armed | Show the matching STATUSTEXT verbatim; keep the workflow on the level step |
| `MAV_RESULT_TEMPORARILY_REJECTED` | A calibration is already running, or < 5000 ms since the last accel calibration | Wait and re-enable the button; do not auto-retry in a tight loop |

### 3.3 Exact STATUSTEXT strings (string-match on these, do not paraphrase)

| Text (exact) | Severity | Meaning |
|---|---|---|
| `Trim OK: roll=%.2f pitch=%.2f yaw=%.2f` | INFO | **Success line** |
| `trim over maximum of 10 degrees` | INFO | Failure: board tilt exceeded the 10° limit (note the **lowercase `t`**) |
| `unsupported trim rotation` | INFO | Failure: the configured rotation cannot be trimmed |
| `Cannot calibrate with Q_TRIM_PITCH set` | INFO | QuadPlane gate |
| `Disarm to allow calibration` | INFO | Armed refusal |

Parsing the success line:

1. Reassemble chunked `STATUSTEXT` first (`id` + `chunk_seq`; `text` is `char[50]` **without null termination**) — owned by `connection-and-telemetry.md`.
2. Match the literal prefix `Trim OK:` (case-sensitive), then extract the three `roll=` / `pitch=` / `yaw=` numbers.
3. Parse each with `float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture)`. The firmware formats with `%.2f`, i.e. always a `.` decimal separator — a culture-sensitive parse will fail on e.g. a Russian or German locale.
4. **Do not assume the printed unit.** The brief does not state whether the three printed values are radians or degrees; the stored parameters `AHRS_TRIM_X/Y/Z` are radians. Confirm the scale by reading `AHRS_TRIM_X`/`AHRS_TRIM_Y` back after the command and comparing — **verify against target firmware** before displaying a unit label.
5. The `yaw=` value is informational — level calibration corrects **roll and pitch only**, and `AHRS_TRIM_Z` is documented "Not Used".

### 3.4 ⚠️ There is no "levelling in progress" text

**There is no `Calibrating barometer` / `Level ...` STATUSTEXT on the level path.** The gyro calibration prints `Init Gyro` to the **console only**, never as a `STATUSTEXT`. The vehicle therefore emits **nothing at all** between the command going out and the single ACK coming back.

⇒ The UI must drive its own progress indication:

- Start an indeterminate progress state the instant the command is written to the wire.
- Do **not** wait for a firmware progress message; none is coming.
- Optionally corroborate with the `SYS_STATUS` IMU **enabled**-bit drop while `ins.calibrating()` (§6.3) as a "really running" signal, treating its absence as inconclusive rather than as failure.
- End the progress state on the `COMMAND_ACK` or on the ≥ 30 s timeout — nothing else terminates it.

---

## 4. What level calibration actually writes

### 4.1 Level (`param5 = 2`)

| Parameter | Unit / range | Note |
|---|---|---|
| `AHRS_TRIM_X` | radians, ±0.1745 (= ±10°) | roll trim |
| `AHRS_TRIM_Y` | radians, ±0.1745 (= ±10°) | pitch trim |
| `AHRS_TRIM_Z` | radians | **"Not Used"** — present but unused |

**It writes nothing else.** It does **not** touch `INS_ACCOFFS*`, `INS_ACCSCAL*`, or `INS_ACC*_ID`. A gyro calibration runs as a side effect (`INS_GYROFFS*`).

⇒ The app's "before/after" diff for a level calibration must be restricted to `AHRS_TRIM_X`/`AHRS_TRIM_Y`/`AHRS_TRIM_Z`. Any change reported in `INS_ACC*` after a level cal means the wrong `param5` was sent.

### 4.2 Contrast — the other accel paths

| `param5` | Writes | Trim effect |
|---|---|---|
| `1` FULL (6-position) | `INS_ACCOFFS*`, `INS_ACCSCAL*`, `INS_ACC*_ID`, `INS_ACC*_CALTEMP` | Then sets trim **per `INS_TRIM_OPTION`** |
| `4` SIMPLE | offsets + IDs | ⚠️ **Resets the trim to zero** — running simple accel cal after a level cal throws the level result away |
| `2` TRIM | `AHRS_TRIM_X/Y/Z` only | this workflow |

`INS_TRIM_OPTION`: `0:Don't adjust trims`, `1:Assume first orientation was level` (default), `2:Assume ACC_BODYFIX aligned`.

### 4.3 Correct parameter spellings

- Accel IDs are **`INS_ACC_ID` / `INS_ACC2_ID` / `INS_ACC3_ID`** (flat form) or `INS<n>_ACC_ID` (group form).
- 🔴 **`INS_ACC1_ID` does not exist.** A lookup for it returns "parameter not found" and, if the app treats a missing ID as "IMU 1 absent", it will report a false hardware fault. Probe `INS_ACC_ID` for instance 1.

### 4.4 `INS_GYROFFS*` is not user calibration

`INS_GYROFFS*` is **re-derived on every boot**. Do not:

- include it in a "calibration to preserve / back up" set,
- include it in a before/after comparison and flag the change as drift,
- copy it between boards.

Treat it as runtime state. (Parameter comparison rules and skip lists: `parameter-protocol-and-profiles.md`.)

---

## 5. Reboot

`MAV_CMD_PREFLIGHT_REBOOT_SHUTDOWN` = **246**.

### 5.1 Accepted parameters

| `param1` | Effect |
|---|---|
| `1` | **Reboot** — this is what the workflow sends |
| `3` | Reboot to bootloader |
| anything else | **`MAV_RESULT_UNSUPPORTED`** |

Practical call: `COMMAND_LONG(246)` with `param1 = 1`, **all other params `0`**.

| Condition | Result |
|---|---|
| Armed, and `param6 != 20190226` | Refused |
| Wrong target component | `MAV_RESULT_DENIED` |
| `param1` not `1` or `3` | `MAV_RESULT_UNSUPPORTED` |

**Force magic — documented, but do not use it here.** `param6 = 20190226` bypasses the armed refusal. This workflow only reboots a **disarmed** bench vehicle, so the app must **never populate `param6`**. Rebooting an armed vehicle is a safety event; if the vehicle is armed, the correct app behaviour is to refuse and tell the operator to disarm.

🔴 **Never send the `42` / `24` / `71` developer magic values** in this command under any circumstances. They are not part of any user-facing workflow.

### 5.2 The ACK arrives *before* the reboot

ArduPilot sends the `COMMAND_ACK` first (`// send ack before we reboot`) and then never returns. Two consequences:

1. Receiving `MAV_RESULT_ACCEPTED` means "the command was taken", **not** "the board is back".
2. **A missing ACK is not automatically a failure.** The ACK can easily be lost in the reset — the link drops mid-flight of the packet, and on USB the device drops off the bus without a graceful detach. Therefore: **do not retry command 246 on ACK timeout.** A retry can hit a board that is already rebooting, or reboot a board that has already come back. Instead, fall straight through to the detection sequence in §5.3, and only conclude failure if the board never reappears.

### 5.3 Ordered "board came back" detection

Run these **in order**; each later signal strengthens the conclusion.

| # | Signal | What to observe | Strength |
|---|---|---|---|
| 1 | **HEARTBEAT gap, then resumption** | `HEARTBEAT` stops for the reset duration, then resumes at ~1 Hz | Primary trigger — necessary, not sufficient |
| 2 | **Boot banner `STATUSTEXT`** | The firmware version string at startup. Forceable with `MAV_CMD_DO_SEND_BANNER` (**42428**) if it was missed. ⚠️ Whether the banner fires automatically on reconnect or only on command 42428 is **UNVERIFIED — verify against target firmware**; therefore treat its absence as inconclusive and explicitly request it with 42428 | Strong when present |
| 3 | **`time_boot_ms` regression** | A `time_boot_ms` **lower than the last value seen before the reboot** proves a fresh boot. ⚠️ Correct inference but **not a documented API guarantee** — **verify against target firmware**; use as corroboration, never as the sole proof | Corroborating |
| 4 | **Re-fetch all parameters** | Mandatory, see §5.4 | Post-condition |

### 5.4 Nothing cached survives the reboot

After the board returns the app **must** re-fetch:

- **All parameters.** Every cached `PARAM_VALUE` is stale, and `param_count` / `param_index` are unstable across the boundary. Re-read by name. See `parameter-protocol-and-profiles.md`.
- **All stream rates.** `SET_MESSAGE_INTERVAL` (511) does **not** persist across reboot — re-issue the entire set, or the verification step will sit waiting for `SYS_STATUS` that is never sent (on Copter every `SRn_*` group defaults to 0 Hz). See `connection-and-telemetry.md`.
- Any derived UI state built from the above.

Invalidate the caches at the moment the reboot command is written, not when the board returns — otherwise the UI shows pre-reboot values as if they were live.

### 5.5 USB re-enumeration

The device drops off the USB bus and re-enumerates; the COM number can change, and some boards enumerate twice (bootloader then application). **All of that is owned by `dotnet-mavlink-and-winui-integration.md`** — close the port on reboot and re-resolve the device there. Do not re-open the cached `COMn` from this workflow.

---

## 6. 🔴 Why `SYS_STATUS` health bits are NOT proof of calibration

### 6.1 Bits

| Sensor | Bit |
|---|---|
| `3D_GYRO` | `0x00000001` |
| `3D_ACCEL` | `0x00000002` |
| `3D_MAG` | `0x00000004` |
| `ABSOLUTE_PRESSURE` | `0x00000008` |
| `3D_GYRO2` | `0x00020000` |
| `3D_ACCEL2` | `0x00040000` |
| `3D_MAG2` | `0x00080000` |
| **`PREARM_CHECK`** | **`0x10000000`** |

**Rule — all three fields, always:**

```
sensorOk = (onboard_control_sensors_present & bit) != 0
        && (onboard_control_sensors_enabled & bit) != 0
        && (onboard_control_sensors_health  & bit) != 0;
```

Checking `health` alone is wrong: a sensor that is not present has an undefined health bit.

### 6.2 🔴 The critical caveat

> **The `3D_ACCEL` health bit only means accelerometer data is flowing** (`get_accel_health_all()`) — it is **not** `accel_calibrated_ok_all()`.
> **The `3D_MAG` health bit only means the compass responds** (`compass.healthy()`) — it is **not** "calibrated".
> **An uncalibrated board reports `3D_ACCEL` and `3D_MAG` healthy.**

Never render "IMU: healthy / Compass: healthy" as "calibration verified". A UI that does so will pass a board that cannot arm.

**The only two things that are proof of calibration:**

1. The **`PREARM_CHECK` bit (`0x10000000`)** in `SYS_STATUS`, evaluated with the full present/enabled/health rule.
2. The **prearm `STATUSTEXT`** messages (§8), matched exactly.

Use both; they answer different questions (a bit vs. a reason).

### 6.3 Useful side effect: enabled bits drop during calibration

While `ins.calibrating()` is true, the **IMU `enabled` bits drop**. This is a usable **"calibration in progress"** indicator during §3 — the only in-band one available, since no progress STATUSTEXT exists. Use it to confirm the calibration really started; do **not** derive success or failure from it, and do not treat the absence of a visible drop as failure (it can simply be faster than the `SYS_STATUS` interval).

---

## 7. Forcing the verification

### 7.1 `MAV_CMD_RUN_PREARM_CHECKS` (401)

- **No parameters** — send `param1..param7 = 0`.
- Behaviour: `MAV_RESULT_TEMPORARILY_REJECTED` if armed; otherwise it runs `pre_arm_checks(true)` — the `true` makes **every failing check emit its `PreArm: …` STATUSTEXT immediately** — and returns `MAV_RESULT_ACCEPTED`.
- **`MAV_RESULT_ACCEPTED` means "the checks ran", not "the checks passed."** The verdict comes from the STATUSTEXT collected and the `PREARM_CHECK` bit, never from this ACK.

### 7.2 Version floor and the fallback

- **Supported on ArduPilot 4.1+** (absent in 4.0.7; present 4.1.5 → master).
- On older firmware the command is unavailable. Then:
  1. Do **not** report a failure of command 401 as a calibration failure.
  2. Fall back to passive observation: failing prearms are **re-broadcast about every 30 s while disarmed**, so open a collection window of at least ~35 s and poll the `SYS_STATUS` `PREARM_CHECK` bit throughout.
  3. If neither a prearm STATUSTEXT nor a `PREARM_CHECK` bit is observed in that window, the verdict is **`Inconclusive`** (§9) — not `Verified`.

### 7.3 Collection window

1. Send command 401.
2. Collect reassembled `STATUSTEXT` beginning with `PreArm:` for **~1–2 s** after the ACK.
3. In parallel, poll `SYS_STATUS` for the `PREARM_CHECK` bit (ensure `SYS_STATUS` is streaming at 1–2 Hz first — see §5.4; on Copter it does not stream by default).
4. Only close the window once at least one `SYS_STATUS` has been received inside it. A window that saw no `SYS_STATUS` at all proves nothing.

### 7.4 `ARMING_CHECK` vs `ARMING_SKIPCHK` — inverted polarity

| Firmware | Parameter | Polarity | Relevant bits |
|---|---|---|---|
| Stable 4.5.x / 4.6.x | `ARMING_CHECK` | **Inclusion** bitmask, default = all checks enabled | `2:Compass`, `4:INS` |
| `master` | `ARMING_SKIPCHK` | **Inverted** — default `0`, `-1` skips all checks | same bit positions |

Rules:

1. **Probe for both names by name** (`PARAM_REQUEST_READ` with `param_index = -1`) and use whichever exists. **Never assume which one is present, and never assume the polarity.**
2. The exact release where `ARMING_CHECK` became `ARMING_SKIPCHK` is **UNVERIFIED** (confirmed absent in 4.6.0, present in master) — **verify against target firmware**; do not gate on a version number, gate on which parameter name resolves.
3. Handle "neither name found" as `Inconclusive`, not as "all checks enabled".

### 7.5 ⚠️ A clean run can be an artefact of disabled checks

If the Compass (`2`) or INS (`4`) check bit is disabled, the corresponding checks do not fail the vehicle — so a run with **zero** `PreArm:` messages can mean "everything passed" **or** "the checks that would have failed were turned off". These are indistinguishable from the text alone.

- Note that a disabled check bit is reported to change the **severity** of the message rather than its text, so **severity-based filtering can hide or reclassify a real problem** — the app must match on the **exact text** and must not drop messages by severity. (The severity behaviour itself is not established in the source brief — **verify against target firmware**.)
- Confirmed counter-example: **`PreArm: Compass calibrated requires reboot` fires even with the COMPASS check bit disabled.** So the presence of a message does not imply its check bit is on, either.

🔴 **Hard requirement:** the verification verdict object must carry **which checks were actually enabled** (the decoded `ARMING_CHECK` / `ARMING_SKIPCHK` value, plus which parameter name was found), and the UI must display it alongside any "clean" result. A pass reported without that context is not a verification.

---

## 8. Prearm catalogue (IMU and compass)

Exact strings. The `PreArm: ` prefix is added by `AP_Arming::check_failed`. `%d` / `%u` / `%.0f` / `%4.0f` are firmware format specifiers — match on the **literal prefix up to the first specifier**, never on the whole rendered line.

⚠️ **Reassemble chunked `STATUSTEXT` before matching** (`id` + `chunk_seq`; `text` is `char[50]` **without null termination**, UTF-8) — owned by `connection-and-telemetry.md`. Matching against raw 50-byte chunks will miss the longer strings below.

⚠️ Failing prearms are **re-broadcast about every 30 s while disarmed** (suppressible via `ARMING_OPTIONS` bit 1). This makes passive verification possible without command 401 (§7.2), and means the app must de-duplicate repeats rather than stacking them in the log.

⚠️ IMU prearm checks are evaluated in order and **the first failure returns** — so only one IMU reason may be visible at a time. Fixing one can reveal the next. Re-run the verification after every fix rather than assuming a single pass clears everything.

### 8.1 IMU

| Exact text | Cat. | Meaning | Resolved by | Operator action |
|---|---|---|---|---|
| `PreArm: Gyros not healthy` | IMU | Gyro data not flowing / sensor fault | Reboot (may recur → hardware) | Reboot; if it persists, the IMU is faulty — do not proceed |
| `PreArm: Gyros not calibrated` | IMU | Boot-time gyro calibration did not complete | Reboot, motionless | Leave the vehicle **completely still**, reboot, do not touch it during boot |
| `PreArm: Accels not healthy` | IMU | Accelerometer data not flowing / sensor fault | Reboot (may recur → hardware) | Reboot; if it persists, the IMU is faulty |
| **`PreArm: 3D Accel calibration needed`** | IMU | Accelerometers have never been calibrated. ⚠️ **The string `PreArm: Accelerometers not calibrated` does not exist — do not match on it** | **Calibration** — full 6-position accel cal (`param5 = 1`), **not** level cal | Run the full 6-position accelerometer calibration. Level calibration (`param5 = 2`) will **not** clear this |
| `PreArm: Accels calibrated requires reboot` | IMU | An accel calibration was saved but is not in effect yet | **Reboot** | Reboot the board, then re-verify |
| `PreArm: Accels inconsistent` | IMU | Multiple accelerometers disagree. ⚠️ Not "inconsistent Accelerometers" | Calibration, or environment/position (vehicle must be still) | Place the vehicle still and level, re-verify; if it persists, redo the full accel calibration |
| `PreArm: Gyros inconsistent` | IMU | Multiple gyros disagree | Environment/position, then reboot | Stop all movement/vibration, reboot with the vehicle untouched |
| `PreArm: temperature cal running` | IMU | A temperature calibration is in progress | Wait, then reboot | Wait for it to finish; do not start a level calibration meanwhile |
| `PreArm: Batch sampling requires reboot` | IMU | Batch-sampling config changed | **Reboot** | Reboot the board |

### 8.2 Compass

Compass **causes** (ordering, priority, `DEV_ID` identity, external flags) are documented in `compass-topology-and-flags.md`; compass **calibration procedures** in `compass-calibration-transfer.md`. This table exists so the verification verdict can classify what it hears.

| Exact text | Cat. | Meaning | Resolved by | Operator action |
|---|---|---|---|---|
| `PreArm: Compass calibration running` | compass | A compass calibration is in progress | Wait / cancel | Wait for completion or cancel it; verification cannot conclude while it runs |
| `PreArm: Compass calibrated requires reboot` | compass | Cal saved but not in effect. ⚠️ Note the wording — no "has". ⚠️ **Fires even with the COMPASS check bit disabled** | **Reboot** | Reboot the board, then re-verify |
| `PreArm: Compass %d not healthy` | compass | Compass N not responding | Reboot; else hardware/wiring | Check wiring, reboot; persistent → hardware fault |
| `PreArm: Compass %d not found` | compass | A configured compass does not exist on this board | Configuration + reboot | A priority slot names a device ID this board does not have — see `compass-topology-and-flags.md` |
| `PreArm: Compass order change requires reboot` | compass | Priority order changed but not applied | **Reboot** | Reboot the board (mandatory — see `compass-topology-and-flags.md`) |
| `PreArm: Compass not calibrated` | compass | Compass calibration missing or invalidated | **Calibration** | Run a compass calibration — see `compass-calibration-transfer.md` |
| `PreArm: Compass offsets too high` | compass | Offsets exceed `COMPASS_OFFS_MAX` (default **1800**). ⚠️ The docs page still claims 600 — **stale**, the code uses `COMPASS_OFFS_MAX` | Calibration + environment | Move away from ferrous metal / magnets, recalibrate |
| `PreArm: Compasses inconsistent` | compass | Multiple compasses disagree beyond 90° xyz / 60° xy / 200 mgauss length | Calibration + environment | Recalibrate; check for magnetic interference near a compass |
| `PreArm: Check mag field: %4.0f, max %d, min %d` | compass | Measured field length outside the band | **Environment / position** | Expected **530 mgauss**, min **185**, max **875**. Move the vehicle away from steel benches, speakers, power cables |
| `PreArm: Check mag field (xy diff:%.0f>%d)` | compass | Horizontal field disagreement between compasses | Environment / position | Relocate the vehicle and re-verify |
| `PreArm: Check mag field (z diff:%.0f>%d)` | compass | Vertical field disagreement between compasses | Environment / position | Relocate the vehicle and re-verify |

---

## 9. Verification verdict model

The app reports exactly one of these after the restart. This is the contract for *"restart, then verify there are no IMU or compass calibration errors."*

```csharp
public enum VerificationState
{
    Verified,               // positive evidence of pass
    NeedsReboot,            // a "requires reboot" prearm was seen
    NeedsCalibration,       // a calibration-class prearm was seen
    EnvironmentalWarning,   // an environment/position-class prearm was seen
    Inconclusive            // no sufficient evidence — the default
}

public sealed record VerificationVerdict(
    VerificationState State,
    IReadOnlyList<string> PrearmMessages,   // reassembled, exact, de-duplicated
    bool? PrearmCheckBit,                   // null = never observed
    bool RanPrearmChecksCommand,            // MAV_CMD_RUN_PREARM_CHECKS accepted
    string? ArmingCheckParamName,           // "ARMING_CHECK" | "ARMING_SKIPCHK" | null
    int? ArmingCheckRawValue,
    bool ImuChecksEnabled,                  // decoded per §7.4 polarity
    bool CompassChecksEnabled,
    bool SysStatusObserved,                 // at least one SYS_STATUS inside the window
    bool RebootConfirmed);                  // §5.3 signals satisfied
```

### 9.1 Evidence required for each state

| State | Required evidence |
|---|---|
| **`Verified`** | ALL of: reboot confirmed (§5.3); parameters and stream rates re-fetched (§5.4); `MAV_CMD_RUN_PREARM_CHECKS` returned `ACCEPTED` (or, on pre-4.1 firmware, a full ≥35 s passive window elapsed); at least one `SYS_STATUS` received inside the window with **`PREARM_CHECK` present && enabled && healthy**; **zero** IMU or compass `PreArm:` strings from §8 collected in the window; `ArmingCheckParamName` resolved and both `ImuChecksEnabled` and `CompassChecksEnabled` are **true**. If the IMU or compass check bits are **disabled**, the state is **not** `Verified` — it is `Inconclusive`, and the UI must say which checks were off (§7.5). |
| **`NeedsReboot`** | Any of `PreArm: Accels calibrated requires reboot`, `PreArm: Batch sampling requires reboot`, `PreArm: Compass calibrated requires reboot`, `PreArm: Compass order change requires reboot`. Offer the §5 reboot and re-verify. |
| **`NeedsCalibration`** | Any of `PreArm: 3D Accel calibration needed`, `PreArm: Gyros not calibrated`, `PreArm: Accels inconsistent`, `PreArm: Compass not calibrated`, `PreArm: Compass offsets too high`, `PreArm: Compasses inconsistent`. Name the specific calibration required; remember `3D Accel calibration needed` requires the **full 6-position** cal, not the level cal. |
| **`EnvironmentalWarning`** | Only environment/position-class strings present: the three `PreArm: Check mag field…` variants, `PreArm: Gyros inconsistent`. The board configuration may be fine; the operator must move or steady the vehicle and re-verify. |
| **`Inconclusive`** | Everything else — see §9.2. |

Precedence when several classes are present: `NeedsReboot` > `NeedsCalibration` > `EnvironmentalWarning`. Always list **every** collected message, not only the winning one — the IMU checks return on first failure, so the visible set can be partial (§8).

### 9.2 🔴 The hard rule

> **"No message received" is `Inconclusive`. It is NEVER `Verified`.**
> **Absence of evidence must never be rendered as success.**

Concretely, all of the following are `Inconclusive`:

1. No `SYS_STATUS` received in the window (e.g. streams not re-requested after reboot — on Copter nothing streams by default).
2. `PREARM_CHECK` never observed as present/enabled/healthy — a `null` `PrearmCheckBit` is not a pass.
3. `MAV_CMD_RUN_PREARM_CHECKS` unsupported (pre-4.1) **and** the passive window did not complete.
4. `MAV_CMD_RUN_PREARM_CHECKS` returned `TEMPORARILY_REJECTED` (vehicle armed) — nothing was checked.
5. Neither `ARMING_CHECK` nor `ARMING_SKIPCHK` could be read, or the IMU/compass check bits are disabled.
6. Reboot never confirmed by any §5.3 signal.
7. A compass calibration was running (`PreArm: Compass calibration running`) — the state is unsettled.

The UI wording for `Inconclusive` must state **what was missing**, e.g. "No `SYS_STATUS` received — stream rates were not re-established after the reboot", so the operator can act. Rendering it as a neutral green "OK" is a defect.
