# Compass Calibration: Transfer and Fixed-Yaw Calibration

Operational reference for the two operator-triggered compass workflows:

- **Workflow (a) — Transfer**: copy compass calibration data from a reference firmware/parameter set into the connected board, write it, read it back, compare, reboot, re-read, compare again, warn on any mismatch.
- **Workflow (b) — Fixed-yaw ("large vehicle") calibration**: run `MAV_CMD_FIXED_MAG_CAL_YAW` with an operator-entered azimuth, reboot, verify no IMU or compass calibration errors remain.

Scope boundaries — do not duplicate here:

| Topic | Owning reference |
|---|---|
| Compass ordering, `COMPASS_PRIOx_ID`, `EXTERNAL`/`EXTERN2/3` semantics, `DEV_ID` bitfield decoding, per-compass mag panel | `compass-topology-and-flags.md` |
| `PARAM_SET` / `PARAM_REQUEST_READ` mechanics, retries/timeouts, type decoding, float comparison rules, `.param`/`.params` file parsing | `parameter-protocol-and-profiles.md` |
| Reboot command and reconnect mechanics, `SYS_STATUS` health bits, `MAV_CMD_RUN_PREARM_CHECKS`, prearm STATUSTEXT collection, IMU level cal | `imu-level-and-health-verification.md` |

---

## 1. What is transferable

"Compass calibration data" is exactly the per-instance block ArduPilot itself persists on a successful calibration and physically moves between slots on a priority reorder (`mag_state::copy_from`). Everything else is either board identity or vehicle-specific.

Parameter names are irregular per instance — never generate them by appending an index blindly. Instance 1 has **no** digit; `EXTERNAL` becomes `EXTERN2`/`EXTERN3`, **not** `EXTERNAL2`. Take the exact name table from `compass-topology-and-flags.md`.

### 1.1 Classification table

| Group | Instance 1 / 2 / 3 names | Class | Reason |
|---|---|---|---|
| Offsets (hard iron) | `COMPASS_OFS_X/_Y/_Z` · `COMPASS_OFS2_X/_Y/_Z` · `COMPASS_OFS3_X/_Y/_Z` | **COPY** | Core calibration result. Part of the block ArduPilot persists on a successful cal. |
| Diagonals (soft iron) | `COMPASS_DIA_X/_Y/_Z` · `COMPASS_DIA2_X/_Y/_Z` · `COMPASS_DIA3_X/_Y/_Z` | **COPY** | Same block. ⚠️ Default differs by version: `0` in Copter-4.1, `1.0` in master — see §1.3. |
| Off-diagonals (soft iron) | `COMPASS_ODI_X/_Y/_Z` · `COMPASS_ODI2_X/_Y/_Z` · `COMPASS_ODI3_X/_Y/_Z` | **COPY** | Same block. |
| Scale factor | `COMPASS_SCALE` · `COMPASS_SCALE2` · `COMPASS_SCALE3` | **COPY** | Same block. |
| Orientation | `COMPASS_ORIENT` · `COMPASS_ORIENT2` · `COMPASS_ORIENT3` | **COPY** | Same block; also a hard precondition for workflow (b) — see §5. |
| External flag | `COMPASS_EXTERNAL` · `COMPASS_EXTERN2` · `COMPASS_EXTERN3` | **COPY (config)** | Part of the moved block. Note `0`/`1` can be overridden by bus auto-detection at boot; `2` (ForcedExternal) is an operator lock that `set_external()` will not override. Details in `compass-topology-and-flags.md`. |
| Use flag | `COMPASS_USE` · `COMPASS_USE2` · `COMPASS_USE3` | **COPY (config)** — with an ordering caveat | Expresses operator intent, not board identity. 🔴 **Not part of the block ArduPilot swaps at boot** — that block is `external, orientation, offset, diagonals, offdiagonals, scale_factor, dev_id, motor_compensation`. Use flags follow the priority slot, not the sensor, so set them **after** any reboot that changes priority, against the re-read topology: `compass-topology-and-flags.md` §5 Phase E. |
| Motor compensation | `COMPASS_MOT_X/_Y/_Z` · `COMPASS_MOT2_X/_Y/_Z` · `COMPASS_MOT3_X/_Y/_Z` | **COPY ONLY WITH OPT-IN** | CompassMot results. Vehicle-specific: they encode the power wiring and current path of *that* airframe. See §1.2. |
| Motor comp type | `COMPASS_MOTCT` | **COPY ONLY WITH OPT-IN** | Global companion to `COMPASS_MOT*` (`0:Disabled, 1:Use Throttle, 2:Use Current`); parameter doc says *"Do not change manually"*. Meaningless without the matching `MOT*` values. |
| Device IDs | `COMPASS_DEV_ID` · `COMPASS_DEV_ID2` · `COMPASS_DEV_ID3` | **NEVER COPY** | `@ReadOnly: True`, *"Automatically detected, do not set manually"*. See §1.4. |
| Extra device IDs | `COMPASS_DEV_ID4` … `COMPASS_DEV_ID8` | **NEVER COPY** | Extra/unregistered slots, never persisted by the firmware. Writing them is noise at best. |
| Priority IDs | `COMPASS_PRIO1_ID` · `COMPASS_PRIO2_ID` · `COMPASS_PRIO3_ID` | **NEVER COPY** | They hold device IDs of *that other board's* sensors. See §1.4. |

Not calibration data, and out of scope for the transfer button (leave them to the general profile-restore feature in `parameter-protocol-and-profiles.md`): `COMPASS_ENABLE`, `COMPASS_AUTO_ROT`, `COMPASS_OFFS_MAX`, `COMPASS_CAL_FIT`, `COMPASS_LEARN`, `COMPASS_DEC`, `COMPASS_AUTODEC`, `COMPASS_OPTIONS`, `COMPASS_FLTR_RNG`, `COMPASS_TYPEMASK`/`COMPASS_DISBLMSK`.

### 1.2 `COMPASS_MOT*` / `COMPASS_MOTCT` gating rule

1. Default the opt-in checkbox to **off**.
2. Label it: *"Also transfer motor interference compensation (CompassMot). Only valid if this vehicle is an identical build — same frame, same battery routing, same ESC/power-cable placement — as the reference."*
3. If the operator enables it, transfer `COMPASS_MOT*_X/_Y/_Z` for every mapped instance **and** `COMPASS_MOTCT` together, as one atomic group. Never transfer `COMPASS_MOTCT` alone, and never transfer `MOT*` while leaving `MOTCT` at the target's value — the pair is only meaningful together.
4. If the opt-in is off, do not write these and do not include them in the comparison — otherwise the compare step will report false mismatches.

### 1.3 `COMPASS_DIA*` version trap

The default of `COMPASS_DIA*` is `0` in Copter-4.1 and `1.0` in master. Consequences for the transfer:

- A reference file captured from a 4.1-era board can legitimately contain `COMPASS_DIA*_X = 0`. Writing `0` into a master-era board yields a degenerate soft-iron matrix.
- Before writing, if any `COMPASS_DIA*` component in the reference set is exactly `0` **and** the other two components of the same instance are also `0`, treat the instance's soft-iron data as *absent*, not as *zero*: warn the operator and offer to skip `DIA`/`ODI` for that instance rather than writing zeros.
- Never "fix up" the value silently to `1.0`. Show it and let the operator decide.

### 1.4 Why `COMPASS_DEV_ID*` and `COMPASS_PRIO*_ID` are never copied

| Parameter | Concrete failure if copied |
|---|---|
| `COMPASS_DEV_ID` / `_ID2` / `_ID3` | These are matched at boot against the sensors actually detected on the bus. A device id from another board names a sensor at a bus/address/devtype that this board may not have, mis-seating the instance→sensor assignment. The calibration is then applied to the wrong physical sensor, or the instance is invalidated. The parameter is `@ReadOnly` precisely to prevent this. The documented rule is absolute: *never change a compass's `COMPASS_DEV_IDx` ID value manually and then reboot!* The firmware commits `COMPASS_DEV_IDx` itself, only via `save_offsets()`, `force_save_calibration()` or `_reset_compass_id()`. |
| `COMPASS_PRIO1_ID` / `PRIO2_ID` / `PRIO3_ID` | These hold device IDs (not slot indices) of the reference board's sensors. Copying them names compasses that do not exist on this board ⇒ `PreArm: Compass N not found`. They are also `@RebootRequired`, so the damage appears one reboot later, after the tool has already reported success. |

**Rule:** if the tool needs to set a priority order at all, it must build `COMPASS_PRIOx_ID` from the **target board's own detected `COMPASS_DEV_IDx` values**, never from the reference file. The reorder procedure itself belongs to `compass-topology-and-flags.md`; see §4.3 here for the sequencing interaction.

---

## 2. The `DEV_ID` validity rule — why read-back equality is not proof

**Verified firmware behaviour:** when the stored device id does not match the detected sensor, ArduPilot **invalidates but does not zero** the calibration.

`Compass::configured(i)` returns false when any of these hold:

1. the offsets are all zero, **or**
2. `detected_dev_id == 0`, **or**
3. stored `dev_id != detected_dev_id`.

`OFS`/`DIA`/`ODI`/`SCALE` are left completely untouched. The only visible symptom is `PreArm: Compass not calibrated`.

### 2.1 The consequences for workflow (a)

- 🔴 **A successful read-back comparison proves only that the parameters were stored. It does NOT prove the calibration is accepted.** The values will read back byte-identical to what you wrote while the board still refuses to arm.
- ⇒ The transfer workflow **must** include a prearm-level check as its final acceptance criterion, not just a parameter diff. Trigger it with `MAV_CMD_RUN_PREARM_CHECKS`, then take the verdict from `imu-level-and-health-verification.md` §9 — which requires the `SYS_STATUS` `PREARM_CHECK` bit **and** the absence of compass prearm failures, not either one alone. Mechanics and the ArduPilot 4.1+ availability constraint are in that file.

### 2.2 The identical-hardware assumption, stated plainly

- **Identical hardware** (same sensor model, same bus type, same bus number, same I²C address ⇒ same decoded device id): the target's `COMPASS_DEV_IDx` already equals the reference's. The copied calibration validates at the next boot and prearm passes. This is the supported case.
- **Different hardware**: the copy will be stored and will read back correctly, yet `configured()` stays false and the board reports `PreArm: Compass not calibrated`. The tool must **detect this before writing** and tell the operator that the calibration will not be accepted.

### 2.3 Mandatory pre-write hardware check

1. Read the target's `COMPASS_DEV_ID`, `COMPASS_DEV_ID2`, `COMPASS_DEV_ID3`.
2. Read the same three from the reference set.
3. Decode both sides (`bus_type`, `bus`, `address`, `devtype` — decoder in `compass-topology-and-flags.md`).
4. Compare per mapped instance pair (mapping per §4):
   - **Exact integer equality** ⇒ transfer will validate. Proceed.
   - **Same `devtype` but different bus/address** ⇒ the sensor model matches but it is wired differently. Warn: *"the transferred calibration will be stored but not accepted by the firmware."* Require explicit operator confirmation to continue.
   - **Different `devtype`, or the target instance is missing** ⇒ block the transfer for that instance by default. It is not the same sensor; the offsets are physically meaningless for it.
5. Never attempt to "fix" a mismatch by writing `COMPASS_DEV_IDx` (see §1.4).

### 2.4 Force-save path — UNVERIFIED, do not ship as an assumption

`Compass::force_save_calibration()` would be the clean way to bless a copied calibration against the detected device ids. `MAV_CMD_PREFLIGHT_CALIBRATION` (241) with `param2 = 76` (`FORCE_SAVE`) appears to map to it. **This is unverified — verify against target firmware on real hardware before relying on it.** Whether it is reachable from MAVLink at all is likewise unverified.

Implementation guidance: keep it behind a developer/advanced flag, run it only as an *optional recovery step offered after* a mismatch has already been reported, and never make the shipped success path depend on it. If used, always re-run the read-back + prearm check afterwards; a non-`ACCEPTED` `MAV_RESULT` on this command means the path is unavailable on this firmware, not that the transfer failed.

---

## 3. Transfer state machine (workflow (a))

Each state has an explicit success condition and an explicit failure action. `WARN + STOP` means: surface the message to the operator, leave the board untouched from here on, offer the rollback snapshot (§3.2), and do not proceed to any later state.

### 3.1 States

| # | State | Success condition | Failure action |
|---|---|---|---|
| 0 | **Preflight gate** | Vehicle **disarmed** (`(HEARTBEAT.base_mode & 0x80) == 0`); link healthy (HEARTBEAT within the last ~3 s, no outstanding param timeouts); a reference set is loaded and parsed; vehicle/firmware provenance of the reference set is either matched or explicitly acknowledged by the operator. | Refuse to start. Explain which gate failed. Never write while armed. |
| 1 | **Load + validate reference set** | The reference file parses (format detection per `parameter-protocol-and-profiles.md`); it contains at least the `OFS` triplet for one instance; parameter names are recognised. | WARN + STOP. Report which names were unparseable or unknown. |
| 2 | **Fetch current values** | Independent `PARAM_REQUEST_READ` **by name** returns every parameter in the transfer set plus `COMPASS_DEV_ID`/`_ID2`/`_ID3` from the board. | WARN + STOP. A parameter that does not exist on the target (`PARAM_ERROR` `DOES_NOT_EXIST`, or timeout after the configured retries) means a compass instance the reference has and the board does not — report the instance, do not guess. |
| 3 | **Snapshot for rollback** | The full current value of every parameter the transfer *would* write is captured in memory and persisted to a timestamped `.param` file in the app's session folder, **before any write**. | WARN + STOP. Never write without a snapshot. |
| 4 | **Resolve instance mapping** | Every reference instance is mapped to exactly one target instance, unambiguously or by operator confirmation (§4). | WARN + STOP. Do not fall back to slot-number identity silently. |
| 5 | **Hardware-identity check** | Per-instance `DEV_ID` comparison per §2.3 passes, or the operator explicitly acknowledges the mismatch warning. | WARN + STOP (default), or continue under acknowledgement with the outcome pre-labelled *"will be stored but probably not accepted"*. |
| 6 | **Write** | Every parameter is written with `PARAM_SET` and the configured retry policy; no `PARAM_ERROR` returned. | WARN + STOP. Report the first failing name. Do not continue past a failed write — a partial calibration block is worse than none. Offer rollback. |
| 7 | **Independent read-back** | Every written parameter is re-read with `PARAM_REQUEST_READ` **by name**. 🔴 Never accept the asynchronous broadcast `PARAM_SET` echo as verification — it carries `param_index = 0xFFFF` and is not a response to your request. | WARN + STOP on a read timeout after retries. |
| 8 | **Compare #1 (pre-reboot)** | Every parameter compares equal under the comparison rules of `parameter-protocol-and-profiles.md` (type-aware: integers exact after rounding, `REAL32` with the relative/absolute epsilon). | 🔴 **WARN and STOP. Never reboot after a failed write/compare.** Report every differing name with expected vs actual. Offer rollback. |
| 8b | **Coalesced-write exception** | For non-`INT32` parameters the firmware can skip the store when the change is inside a small relative band, so the read-back shows the old value. That is **not a failure** — classify it as **"coalesced"**, show it as an informational row, and let it pass. **The band has exactly one definition, in `parameter-protocol-and-profiles.md` §3.2 — use it, do not restate the formula here.** | n/a — this is a classification, not a gate. |
| 9 | **Reboot** | `MAV_CMD_PREFLIGHT_REBOOT_SHUTDOWN` (246) with `param1 = 1` returns `ACCEPTED`. Note the firmware acks *before* rebooting. Command and reconnect mechanics: `imu-level-and-health-verification.md`. | WARN + STOP. A `DENIED` means the wrong target component; `UNSUPPORTED` means a bad `param1`. |
| 10 | **Wait for the board to come back** | The serial device re-enumerates and HEARTBEAT resumes. 🔴 The board drops off the USB bus and re-enumerates; **the COM number can change**. Close the port immediately on reboot and re-resolve the device from its instance path + VID/PID, never from the cached `COMn`. Cube-class boards appear twice (bootloader `…-BL`, then app). Re-enumeration and reconnect are owned by `dotnet-mavlink-and-winui-integration.md` §5. | WARN + STOP with a "board did not come back" message and a manual-reconnect prompt. Do not report the transfer as successful. |
| 11 | **Re-fetch** | 🔴 **Parameters must be re-fetched after reboot** — never reuse the pre-reboot cache. Re-read every transferred parameter by name. Also re-issue any `SET_MESSAGE_INTERVAL` stream configuration, which does not persist across reboot. | WARN + STOP on timeouts. |
| 12 | **Compare #2 (post-reboot)** | Same comparison rules as state 8. | WARN. Report the differing names. A post-reboot difference that was equal pre-reboot usually means a `@RebootRequired` reordering swapped the per-instance blocks (§4.3) or a driver overwrote a `COMPASS_EXTERNAL*` value at detection time — say which, do not just print a diff. |
| 13 | **Prearm check** | The verdict produced by `imu-level-and-health-verification.md` §9 is `Verified` — which requires **all** of: `MAV_CMD_RUN_PREARM_CHECKS` (401) returned `ACCEPTED`, at least one `SYS_STATUS` arrived inside the collection window, the `PREARM_CHECK` bit is present, enabled and healthy, and no compass-related prearm failure was seen. 🔴 "No message arrived" is `Inconclusive`, never success. | WARN with the exact prearm string and its interpretation (§8); an `Inconclusive` verdict is reported as such, not as a pass. 🔴 This state, not state 12, is what proves the transfer was *accepted*. |
| 14 | **Report** | Structured result: per-parameter written/coalesced/mismatched, per-instance `DEV_ID` verdict, prearm verdict, rollback file path. | n/a |

### 3.2 The rollback snapshot

- **What it is:** the target board's own values, read in state 2, for exactly the set of parameters the transfer will write — nothing more. It is not a full parameter backup (that belongs to the profile feature).
- **Why:** the transfer overwrites a possibly-valid existing calibration. If compare #1 fails, or the board prearm-fails after the transfer, the operator must be able to return to the previous known state in one action.
- **When it is offered:** on any `WARN + STOP` from state 6 onward, and always as a passive option in the final report. Rollback is a plain re-write of the snapshot followed by the same states 6→13 (write, read-back, compare, reboot, re-read, compare, prearm) — reuse the same machine, do not write a second one.
- **Never auto-rollback.** A partially-written block that the operator can inspect is more useful than a silent revert. Ask.

### 3.3 Illustrative fragment

```csharp
// One transfer step: write, then verify with an INDEPENDENT read-by-name.
// Comparison + coalesce classification live in the parameter service
// (see parameter-protocol-and-profiles.md) — do not reimplement them here.
foreach (var p in plan.Writes)                      // plan excludes DEV_ID*/PRIO*_ID by construction
{
    await _params.SetAsync(p.Name, p.Value, ct);    // PARAM_SET + retries
}

var readback = await _params.ReadManyByNameAsync(plan.Writes.Select(w => w.Name), ct);
var diff = _compare.Diff(plan.Writes, readback);    // type-aware; marks Coalesced separately

if (diff.HasHardMismatch)
{
    // 🔴 never reboot after a failed write
    return TransferResult.Stop(diff, rollback: _snapshot);
}
```

---

## 4. Instance mapping

The reference set's instance 1/2/3 do **not** necessarily correspond to the board's instance 1/2/3. Slot number is a priority position, not a sensor identity.

### 4.1 Matching rule

1. For each side (reference set, target board), read `COMPASS_DEV_ID`, `COMPASS_DEV_ID2`, `COMPASS_DEV_ID3` and decode each into `(bus_type, bus, address, devtype)`.
2. Match on **decoded device id**, in this order of strength:
   - **Exact device-id integer equality** ⇒ unambiguous match. Auto-map.
   - **Same `devtype` + same `bus_type` + same `address`, different `bus`** ⇒ strong candidate; present it as a proposed mapping and require confirmation.
   - **Same `devtype` only** ⇒ weak candidate; require confirmation and carry the §2.3 warning that the calibration will be stored but not accepted.
   - **No `devtype` match** ⇒ no mapping. Do not transfer that instance.
3. **Never map by slot number alone.** A reference instance 1 and a target instance 1 that decode to different sensors are different compasses.
4. If any reference instance has more than one plausible target (e.g. two identical internal sensors on different buses), or fewer/more instances exist on one side, the mapping is **ambiguous** ⇒ present the decoded description of both sides (render as e.g. `I2C:bus 1:addr 0x0E:IST8310`) and require explicit operator confirmation per instance before any write.
5. Show the "which one is external" hint alongside each row, resolved from `COMPASS_EXTERNAL*` as read after boot and corroborated by `bus_type` — the heuristic is defined in `compass-topology-and-flags.md`. Never decide external/internal from the instance index.

### 4.2 UI requirement

The mapping table is a first-class step, not a hidden detail. Minimum columns: reference slot · decoded reference sensor · → · target slot · decoded target sensor · match strength · action (transfer / skip). No write starts until every row is either mapped or explicitly skipped.

### 4.3 Interaction with priority reordering — sequence deliberately

`COMPASS_PRIOx_ID` is `@RebootRequired`. At boot, `_reorder_compass_params()` **physically swaps the entire per-instance block** between slots (`external, orientation, offset, diagonals, offdiagonals, scale_factor, dev_id, motor_compensation`). After that reboot the unsuffixed parameters (`COMPASS_OFS_*`, `DIA_*`, `ODI_*`, `SCALE`, `ORIENT`, `EXTERNAL`, `MOT_*`, `DEV_ID`) belong to the **priority-1** compass, which may not be the one they belonged to before.

Therefore:

1. 🔴 **Never perform a calibration transfer and a priority reorder in the same write batch.** The slot meanings change under you at the next boot and compare #2 will diff against the wrong sensor.
2. **Recommended order: reorder first, then transfer.**
   - Write only `COMPASS_PRIOx_ID` (from the target's own detected `COMPASS_DEV_IDx`), reboot, re-fetch, and re-derive the instance mapping against the *new* slot layout. The reorder procedure is owned by `compass-topology-and-flags.md`.
   - Then run the transfer state machine from state 0 with the fresh mapping.
3. If a reorder is already pending (a `COMPASS_PRIOx_ID` change written but not yet rebooted), the transfer must refuse to start. Symptoms to detect: `PreArm: Compass order change requires reboot`, and — if a normal calibration is attempted — the STATUSTEXT `Compass cal requires reboot after priority change` (severity ERROR, no `PreArm:` prefix).

---

## 5. Fixed-yaw ("large vehicle") calibration — workflow (b)

`MAV_CMD_FIXED_MAG_CAL_YAW`, command id **42006**.

### 5.1 Parameter table

| Field | Meaning | Units / type | Notes |
|---|---|---|---|
| `param1` | Yaw, earth frame | **degrees, TRUE north** | 🔴 **NOT magnetic.** See §6. |
| `param2` | CompassMask | uint8 bitmask | Bit *i* = compass **priority index** *i*. **`0` = all enabled compasses.** |
| `param3` | Latitude | **decimal degrees, float** | Not 1e7 integers. |
| `param4` | Longitude | **decimal degrees, float** | Not 1e7 integers. |
| `param5`–`param7` | — | — | Empty / 0. |

### 5.2 `compass_mask = 0`

`0` means **all enabled compasses**, not "none". This is the normal value for the app: the operator wants every compass on the vehicle calibrated to the same known heading. Only set individual bits when deliberately calibrating a single compass, and remember the bits index **priority slots**, not device ids and not the `COMPASS_DEV_IDx` suffix ordering.

### 5.3 Latitude / longitude behaviour

- If `param3` **and** `param4` are **both zero**, the firmware falls back to the AHRS location and then to GPS. That requires a **3D fix**; without one the command fails with STATUSTEXT `Mag: no position available` and `MAV_RESULT_FAILED`.
- **Passing explicit non-zero lat/lon removes the GPS requirement entirely.** This is the reason the app should offer a location field: an indoor bench or hangar with no sky view can still run a fixed-yaw calibration.
- The firmware computes the WMM earth field at that location, rotates it into the body frame using the AHRS roll/pitch plus the supplied yaw, and sets `offsets = field − uncorrected_measurement`. Location accuracy therefore matters at the level of the local field model — a nearby city is fine, a wrong country is not.
- UI: offer "use vehicle GPS position" (sends 0/0) vs "enter position manually" (sends explicit decimal degrees). If GPS is selected, check for a 3D fix first and say so rather than letting the command fail.

### 5.4 Arming state

- **Disarmed is NOT enforced by the firmware** on this path — unlike `MAV_CMD_DO_START_MAG_CAL`, the handler contains no armed check.
- Whether any vehicle-level code adds an arming or mode gate beyond the common GCS path is **unverified — verify against target firmware**.
- ⇒ **Enforce disarmed in the UI anyway.** Gate the button on `(HEARTBEAT.base_mode & 0x80) == 0`. Running a calibration that instantly auto-saves offsets on an armed vehicle has no legitimate use in this app.

### 5.5 Operator procedure

1. Vehicle **stationary** and **level** on the ground. Roll/pitch are taken from AHRS at the instant of the command; motion or a tilted vehicle corrupts the result.
2. `COMPASS_ORIENT` / `COMPASS_ORIENT2` / `COMPASS_ORIENT3` are **already correct**. ⚠️ Automatic orientation determination happens **only in normal (onboard) calibration**. Docs verbatim: *"Large Vehicle Mag Calibration require that the orientation be properly set BEFORE"*, and *"If orientation is incorrect this procedure will appear to succeed while leaving the compass calibration in a very bad state."* The UI must display the current `COMPASS_ORIENT*` values on the confirmation dialog and require the operator to acknowledge them.
3. Operator enters the **current azimuth of the vehicle's nose** — TRUE north (§6).
4. Operator chooses the position source (§5.3).
5. Check for non-trivial `DIA`/`ODI` and warn if present (§7).
6. Send the command; read `MAV_RESULT` and STATUSTEXT (§8).
7. On `ACCEPTED`: **reboot**, reconnect, re-fetch parameters, then verify no IMU or compass calibration errors — handoff per §10.

---

## 6. The azimuth input

🔴 **`param1` must be TRUE north, not magnetic north.** The firmware rotates the WMM-derived earth field by this yaw; feeding it a magnetic heading injects the local declination as a permanent heading error.

### 6.1 Why a phone compass reading is wrong as-is

A handheld/phone magnetic compass reads the direction of the local magnetic field. True heading = magnetic heading + local declination (east declination positive). ArduPilot's own documentation states this explicitly: **add the local declination to a phone compass reading** before entering it.

### 6.2 UI requirements

1. **Label unambiguously.** Not "Heading". Use: **"Vehicle heading — TRUE north (degrees)"** with a persistent inline note *"Not magnetic. If you read this from a phone or handheld compass, add your local magnetic declination."*
2. **State units and range** in the control: degrees, `0`–`360` (accept `360` as `0`; normalise into `[0, 360)` before sending). Reject non-numeric input; do not silently coerce.
3. **Pick exactly one of these two input models — do not guess:**
   - **(A) True-only.** One field, true degrees. If the operator has a magnetic reading, they convert it themselves. Simplest, and refuses to guess.
   - **(B) Magnetic + explicit declination.** Two fields: magnetic heading and a **required, operator-supplied** local declination in degrees (sign convention shown: east positive). Compute `true = wrap360(magnetic + declination)` and **display the computed true value** before sending. Never infer the declination from an unstated source or default it to zero.
4. **Never** accept a magnetic value into a field labelled true, and never silently apply a declination the operator did not enter or see. The vehicle's own `COMPASS_DEC` / `COMPASS_AUTODEC` are firmware-side settings for heading computation and must not be repurposed to silently correct the operator's typed input.
5. Show the value that will actually be transmitted in the confirmation dialog, e.g. *"Will send yaw = 187.5° TRUE"*.

---

## 7. 🔴 The soft-iron destruction conflict

**Verified firmware behaviour of `MAV_CMD_FIXED_MAG_CAL_YAW`:**

- It **auto-saves immediately**. There is **no accept step** — `set_and_save_offsets()` is called directly, unlike onboard calibration which can wait for `MAV_CMD_DO_ACCEPT_MAG_CAL`. There is no undo.
- It **forces `COMPASS_DIA*_X/_Y/_Z = (1, 1, 1)` and `COMPASS_ODI*_X/_Y/_Z = (0, 0, 0)`** for every compass in the mask.
- `COMPASS_SCALE*` is **untouched**.
- `set_and_save_offsets()` also commits `COMPASS_DEV_IDx = detected_dev_id`.

### 7.1 The conflict, stated plainly

⚠️ **Running workflow (b) after workflow (a) discards most of what (a) transferred.** The transferred soft-iron calibration (`DIA`/`ODI`) is overwritten with the identity matrix; only the offsets are then meaningful, and even those are replaced by the fixed-yaw result. What survives from the transfer is `COMPASS_SCALE*`, `COMPASS_ORIENT*`, `COMPASS_EXTERNAL*`/`EXTERN2/3`, `COMPASS_USE*` and (if opted in) `COMPASS_MOT*`.

### 7.2 Ordering guidance

| Operator intent | Correct order |
|---|---|
| Reproduce a known-good calibration from a reference vehicle | Workflow (a) **only**. Do not follow it with fixed-yaw. |
| Vehicle too large / cannot be rotated; no reference calibration available | Workflow (b) **only**. |
| Both are wanted | Run **(b) first, then (a)**. The transfer's `DIA`/`ODI`/`OFS` then land on top of the fixed-yaw result. Note that (b) auto-commits `COMPASS_DEV_IDx = detected_dev_id`, which is harmless and helps the §2 validity rule. Follow with the full transfer state machine including reboot and prearm check. |
| Compass reordering is also wanted | Full order: **reorder → reboot → (b) fixed-yaw → reboot → (a) transfer → reboot → verify.** 🔴 Fixed-yaw runs `_reset_compass_id()` (§8.3), which can zero a `COMPASS_PRIOx_ID`; the next boot then auto-fills and compacts the slots, so the priority order you just established is **not guaranteed to survive step (b)**. Re-run the Phase A read and the Phase F assertions of `compass-topology-and-flags.md` §5 after (b), before starting (a). |
| Never | (a) then (b) as a single "do everything" button. Do not offer such a button. |

### 7.3 Required pre-run warning

Before sending `MAV_CMD_FIXED_MAG_CAL_YAW`, read the current `COMPASS_DIA*` and `COMPASS_ODI*` for every compass in the mask. If any of them is **non-trivial** — i.e. any `DIA` component differs from `1.0` or any `ODI` component differs from `0.0`, under the comparison rules of `parameter-protocol-and-profiles.md` — show a blocking confirmation:

> This compass has soft-iron calibration data (`DIA`/`ODI`). Fixed-yaw calibration will **permanently overwrite** it with `DIA = 1,1,1` and `ODI = 0,0,0`. It saves immediately and cannot be undone. Continue?

Offer to snapshot the current `OFS`/`DIA`/`ODI`/`SCALE` block to a `.param` file first, using the same snapshot mechanism as §3.2. Treat the version trap of §1.3 when deciding "non-trivial": on a Copter-4.1-era board `DIA = 0,0,0` is the default, not a calibration — say "no soft-iron data", not "will be destroyed".

### 7.4 Required orientation warning

⚠️ Carry this forward on the same dialog: **an incorrect `COMPASS_ORIENT*` makes fixed-yaw calibration *appear* to succeed while leaving the compass badly calibrated.** The command returns `MAV_RESULT_ACCEPTED` and the offsets are saved; nothing in the result indicates the problem. Show the current `COMPASS_ORIENT*` values and require acknowledgement (§5.5 step 2).

---

## 8. Expected results and diagnosis

### 8.1 `MAV_RESULT` for `MAV_CMD_FIXED_MAG_CAL_YAW`

| Result | Meaning |
|---|---|
| `MAV_RESULT_ACCEPTED` | Offsets computed and **already saved**. `DIA`/`ODI` have been forced to identity. Proceed to reboot + verification. |
| `MAV_RESULT_FAILED` | Rejected. Diagnose from the accompanying STATUSTEXT (below) — the result code alone does not distinguish the causes. |
| `MAV_RESULT_UNSUPPORTED` | Firmware built without `AP_COMPASS_CALIBRATION_FIXED_YAW_ENABLED`. Fixed-yaw is not available on this board; disable the feature in the UI and say why. |

### 8.2 STATUSTEXT strings that diagnose the failure

Reassemble chunked `STATUSTEXT` before string-matching (`text` is `char[50]` without null termination; extensions `id` + `chunk_seq` carry multi-chunk messages) — reassembly is owned by `connection-and-telemetry.md` §7.

| STATUSTEXT | Cause | Operator action |
|---|---|---|
| `Mag: no position available` | `param3`/`param4` were both zero and neither AHRS nor GPS could supply a position (no 3D fix). | Enter an explicit latitude/longitude, or move the vehicle where it can get a 3D fix. |
| `Mag: WMM table error` | The world magnetic model lookup failed for the supplied location. | Check the entered coordinates are plausible decimal degrees (not 1e7 integers, not swapped lat/lon). |
| `Mag[%u]: unhealthy` | The named compass instance is not producing healthy data. | Fix the sensor/wiring first. Cross-check the per-compass field readings and health per `compass-topology-and-flags.md`. |
| `Mag[%u]: bad uncorrected field` | The raw field measured on that instance is implausible. | Check for nearby ferrous mass / powered equipment, check `COMPASS_ORIENT*`, re-seat an external compass. Compare against the prearm magfield band: expected **530 mgauss**, min **185**, max **875**. |

### 8.3 The `_reset_compass_id()` side effect

`MAV_CMD_FIXED_MAG_CAL_YAW` calls `_reset_compass_id()` **first**. That call may zero a `COMPASS_PRIOx_ID` and emit, at severity **ALERT**:

```
Mag: Compass #%d with DEVID %lu removed
```

Treat this as an **expected, informational side effect**, not a failure — but do surface it, because it means the priority table changed. Consequences for the app:

1. Re-fetch `COMPASS_PRIO1_ID` / `PRIO2_ID` / `PRIO3_ID` after the command; the cached values are stale.
2. A zeroed slot is auto-filled at the next boot and compasses compact upward — so the slot→sensor mapping may differ after the reboot in §5.5 step 7. Re-derive the mapping (§4) rather than reusing it.
3. `MAV_CMD_DO_START_MAG_CAL` with `mask = 0` also runs `_reset_compass_id()` and produces the same message.
4. 🔴 **The priority order itself may no longer hold.** A zeroed slot is auto-filled in detection order at the next boot, so a previously-established "external compass is priority 1" arrangement can be undone silently. After **any** command that runs `_reset_compass_id()`, re-run the Phase A read and the Phase F assertions of `compass-topology-and-flags.md` §5 before continuing — re-deriving the instance mapping (consequence 2) is not sufficient on its own.

### 8.4 Symptom → cause → action

| Symptom | Cause | Operator action |
|---|---|---|
| Read-back matches perfectly, but `PreArm: Compass not calibrated` after reboot | Stored `COMPASS_DEV_IDx` ≠ detected device id ⇒ `configured()` false; values kept, calibration invalidated (§2). | Confirm the hardware is truly identical (§2.3). If not, run an onboard or fixed-yaw calibration on this board instead of transferring. Do **not** write `COMPASS_DEV_IDx`. |
| `PreArm: Compass N not found` | A `COMPASS_PRIOx_ID` names a device id that does not exist on this board — typically because priority ids were copied from the reference set (§1.4). | Reset `COMPASS_PRIOx_ID` to `0` (auto-fill at boot) or to this board's own detected `COMPASS_DEV_IDx`, then reboot. |
| `PreArm: Compass order change requires reboot` | `COMPASS_PRIOx_ID` was changed and not yet rebooted. | Reboot, then re-derive the instance mapping (§4.3). |
| STATUSTEXT `Compass cal requires reboot after priority change` (ERROR, no `PreArm:` prefix) when starting an onboard cal | Same as above, hit from the calibration path. | Reboot first. |
| `PreArm: Compass calibrated requires reboot` (note: no "has") | A calibration was saved and the board has not rebooted. Fires even when the COMPASS check bit is disabled. | Reboot. This is the normal state immediately after a successful fixed-yaw calibration. |
| `PreArm: Compass offsets too high` | Offsets exceed `COMPASS_OFFS_MAX` (default **1800**). ⚠️ The docs page claiming a 600 threshold is stale — the code uses `COMPASS_OFFS_MAX`. | Recalibrate on this vehicle; a transferred offset set from a different magnetic environment can land here. |
| `PreArm: Compasses inconsistent` | The compasses disagree: thresholds are 90° xyz / 60° xy / 200 mgauss length difference. | Frequently follows a partial transfer where only some instances were mapped, or a fixed-yaw run with a wrong `compass_mask`. Calibrate all compasses together (`compass_mask = 0`). |
| `PreArm: Check mag field: %4.0f, max %d, min %d` / `(xy diff:%.0f>%d)` / `(z diff:%.0f>%d)` | Measured field length outside the band (expected 530, min 185, max 875 mgauss) or axis disagreement. | Move away from ferrous structures/power cables; check `COMPASS_ORIENT*`. |
| `PreArm: Compass %d not healthy` | Sensor not producing data. | Hardware/wiring, not calibration. |
| `PreArm: Compass calibration running` | An onboard calibration is still in progress. | Wait, accept, or cancel (§9). |
| Fixed-yaw returned `ACCEPTED` but the heading is visibly wrong | Wrong `COMPASS_ORIENT*` (§7.4), or a **magnetic** azimuth was entered where TRUE was required (§6). | Correct the orientation and/or the azimuth and re-run. |
| A written parameter reads back as its old value | Possibly the firmware's `save_sync()` coalescing for non-`INT32` when `\|Δ\| < 1e-4 × \|v\|`. | Not a failure — report as "coalesced" (§3.1 state 8b). |

---

## 9. Onboard calibration commands (for completeness)

Not part of workflows (a) or (b), but the app must be able to recognise these in progress and to display their results.

### 9.1 Commands

| Command | ID | Parameters |
|---|---|---|
| `MAV_CMD_DO_START_MAG_CAL` | 42424 | `param1` mask (`0` = all, range 0–255) · `param2` retry · `param3` autosave · `param4` delay [s] · `param5` autoreboot |
| `MAV_CMD_DO_ACCEPT_MAG_CAL` | 42425 | `param1` mask (`0` = all) |
| `MAV_CMD_DO_CANCEL_MAG_CAL` | 42426 | `param1` mask (`0` = all) |

- **`MAV_CMD_DO_START_MAG_CAL` requires the vehicle to be disarmed** — otherwise STATUSTEXT `Disarm to allow compass calibration`. (Contrast with fixed-yaw, §5.4, which has no such check.)
- `mask = 0` also runs `_reset_compass_id()` — expect the `Mag: Compass #%d with DEVID %lu removed` side effect (§8.3).
- Start-failure STATUSTEXT: `Compass cal requires reboot after priority change` · `Compass cal object not initialised` · `Compass cal requires GPS lock` (emitted **only** when `COMPASS_OPTIONS` bit 0 `CalRequireGPS` is set) · `CompassCalibrator: Cannot start compass thread.`

### 9.2 Progress and report messages

`MAG_CAL_PROGRESS` (191): `compass_id`, `cal_mask`, `cal_status`, `attempt`, `completion_pct`, `completion_mask[10]`, `direction_x/y/z`. ⚠️ ArduPilot hardcodes `direction_x/y/z` to 0 — do not render them as a live orientation cue.

`MAG_CAL_REPORT` (192): `compass_id`, `cal_mask`, `cal_status`, `autosaved` (**0 = needs `DO_ACCEPT_MAG_CAL`, 1 = already saved**), `fitness` [mgauss RMS residual], `ofs_x/y/z`, `diag_x/y/z`, `offdiag_x/y/z`; extension fields `orientation_confidence`, `old_orientation`, `new_orientation`, `scale_factor`.

Both live in the `SRn_EXTRA3` stream group and are effectively off by default on Copter — request them explicitly with `MAV_CMD_SET_MESSAGE_INTERVAL` (511) for the duration of the calibration and restore afterwards. Stream-rate mechanics belong to the telemetry reference.

### 9.3 `MAG_CAL_STATUS` enum

| Value | Name | Reading |
|---|---|---|
| 0 | `NOT_STARTED` | idle |
| 1 | `WAITING_TO_START` | queued (see `param4` delay) |
| 2 | `RUNNING_STEP_ONE` | sample collection |
| 3 | `RUNNING_STEP_TWO` | fit refinement |
| 4 | `SUCCESS` | check `autosaved`; if 0, send `MAV_CMD_DO_ACCEPT_MAG_CAL` |
| 5 | `FAILED` | generic failure |
| 6 | `FAILED_ORIENTATION` | ArduPilot-specific: orientation could not be determined/confirmed |
| 7 | `FAILED_RADIUS` | ArduPilot-specific: sphere radius implausible |
| 8 | `FAILED_OFFSETS` | ArduPilot-specific: offsets out of range |
| 9 | `FAILED_DIAG_SCALING` | ArduPilot-specific: soft-iron diagonal scaling implausible |
| 10 | `FAILED_RESIDUALS_HIGH` | ArduPilot-specific: residuals above threshold — this is the `fitness` failure |

Values 6–10 are ArduPilot extensions to the enum; a generic MAVLink decoder may render them as unknown. Map them explicitly.

### 9.4 Judging `fitness` against `COMPASS_CAL_FIT`

- `MAG_CAL_REPORT.fitness` is an **RMS residual in milligauss — lower is better**.
- `COMPASS_CAL_FIT` range 4–32, values `4:Very Strict, 8:Strict, 16:Default, 32:Relaxed`, **default 16.0**. Lower = stricter.
- **The ×2 rule:** the plain `COMPASS_CAL_FIT` threshold applies only to a **priority-1 external** compass. An internal priority-1 compass and **all** secondary compasses are judged against **2 × `COMPASS_CAL_FIT`**.
- ⇒ To display a pass/fail per compass, the app needs the compass's priority slot **and** its resolved external/internal state (`compass-topology-and-flags.md`), then compares `fitness` against `COMPASS_CAL_FIT` or `2 × COMPASS_CAL_FIT` accordingly. Never apply the plain threshold uniformly — it produces false failures on internal and secondary compasses.

---

## 10. Handoff to verification

After **either** workflow, the app does not decide success on its own. The verification step is owned by **`imu-level-and-health-verification.md`**.

**Exact handoff condition — hand off when all of the following are true, and not before:**

1. The workflow's write phase completed:
   - workflow (a): state 8 (compare #1) passed with no hard mismatch, **or**
   - workflow (b): `MAV_CMD_FIXED_MAG_CAL_YAW` returned `MAV_RESULT_ACCEPTED`.
2. A reboot was commanded (`MAV_CMD_PREFLIGHT_REBOOT_SHUTDOWN`, `param1 = 1`) and **acknowledged**.
3. The board re-enumerated and the link is re-established (port re-resolved from the device instance path + VID/PID, HEARTBEAT flowing).
4. Parameters have been re-fetched after the reboot and stream intervals re-issued.

**What is handed over:** the list of compass instances touched, their decoded device ids, whether `DEV_ID` identity was exact/approximate/mismatched (§2.3), and — for workflow (b) — the fact that `DIA`/`ODI` were reset to identity.

**What comes back:** the verification verdict, from `MAV_CMD_RUN_PREARM_CHECKS` (401) plus the `SYS_STATUS` `PREARM_CHECK` bit and the collected `PreArm:` STATUSTEXT.

🔴 Two rules that must not be relaxed at this boundary:

- **`SYS_STATUS` health bits do not prove calibration.** `3D_MAG` health is only `compass.healthy()` and `3D_ACCEL` health is only "data flowing" — an uncalibrated board still reports both healthy. Proof of calibration requires `PREARM_CHECK` (`0x10000000`) and/or the prearm STATUSTEXT.
- Report **warn**, not **fail**, and never auto-remediate. Every failure path in §3 and §8 ends with an operator-visible message and a stop, not a retry loop.
