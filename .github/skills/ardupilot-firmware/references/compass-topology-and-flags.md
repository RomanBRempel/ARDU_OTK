# Compass Topology, Ordering and Flags

Scope: how ArduPilot models multiple compasses (instances, priority slots, device IDs), how to tell an
external compass from an internal one, the exact restartable procedure to **make the external compass
primary and set the use flags**, and the data model behind the **compass panel**.

Out of scope — owned by sibling references, do not duplicate:
- Copying calibration between boards, `mag_state` transfer block, DEV_ID mismatch handling → `compass-calibration-transfer.md`
- `MAV_CMD_FIXED_MAG_CAL_YAW` / large-vehicle fixed-yaw calibration → `compass-calibration-transfer.md`
  (⚠️ note in passing: fixed-yaw cal **overwrites `DIA_*` to (1,1,1) and `ODI_*` to (0,0,0)**, so ordering
  against a transfer matters — that conflict is resolved in the sibling file, not here.)

Rule for this whole document: **every name below is spelled exactly as the firmware spells it.** The compass
parameter naming is deliberately irregular. Never construct a parameter name by string concatenation with an
instance number — use the lookup tables in §1.

---

## 1. Parameter map

### 1.1 Per-instance parameters (instances 1 / 2 / 3)

| Function | Inst 1 | Inst 2 | Inst 3 |
|---|---|---|---|
| USE | `COMPASS_USE` | `COMPASS_USE2` | `COMPASS_USE3` |
| EXTERNAL | `COMPASS_EXTERNAL` | **`COMPASS_EXTERN2`** | **`COMPASS_EXTERN3`** |
| ORIENT | `COMPASS_ORIENT` | `COMPASS_ORIENT2` | `COMPASS_ORIENT3` |
| DEV_ID | `COMPASS_DEV_ID` | `COMPASS_DEV_ID2` | `COMPASS_DEV_ID3` |
| Offsets | `COMPASS_OFS_X` / `_Y` / `_Z` | `COMPASS_OFS2_X` / `_Y` / `_Z` | `COMPASS_OFS3_X` / `_Y` / `_Z` |
| Diagonals | `COMPASS_DIA_X` / `_Y` / `_Z` | `COMPASS_DIA2_X` / `_Y` / `_Z` | `COMPASS_DIA3_X` / `_Y` / `_Z` |
| Off-diagonals | `COMPASS_ODI_X` / `_Y` / `_Z` | `COMPASS_ODI2_X` / `_Y` / `_Z` | `COMPASS_ODI3_X` / `_Y` / `_Z` |
| Scale | `COMPASS_SCALE` | `COMPASS_SCALE2` | `COMPASS_SCALE3` |
| Motor comp | `COMPASS_MOT_X` / `_Y` / `_Z` | `COMPASS_MOT2_X` / `_Y` / `_Z` | `COMPASS_MOT3_X` / `_Y` / `_Z` |

**Irregular spellings — the two traps that break naive code:**

1. **`COMPASS_EXTERNAL` → `COMPASS_EXTERN2` / `COMPASS_EXTERN3`.** Instance 1 is spelled in full
   (`EXTERNAL`); instances 2 and 3 are truncated to `EXTERN`. **`COMPASS_EXTERNAL2` does not exist** and a
   `PARAM_REQUEST_READ` for it will simply never be answered (or answer `PARAM_ERROR DOES_NOT_EXIST` on
   recent firmware).
2. **The `OFS` / `OFS2` / `OFS3` pattern: the instance digit goes in the middle, before the axis suffix.**
   `COMPASS_OFS_X`, `COMPASS_OFS2_X`, `COMPASS_OFS3_X` — *not* `COMPASS_OFS_X2`. The same middle-digit
   pattern applies to `DIA`/`ODI`/`MOT`. Instance 1 has **no** digit at all (`COMPASS_OFS_X`), and
   instance 1's unsuffixed form is what makes §2's block-swap so dangerous.

Consequence: the only safe implementation is a fixed 3-element table per function.

```csharp
// One table per function. Index 0 == instance 1. Never build these names by concatenation.
static readonly string[] Use      = { "COMPASS_USE",      "COMPASS_USE2",     "COMPASS_USE3" };
static readonly string[] External = { "COMPASS_EXTERNAL", "COMPASS_EXTERN2",  "COMPASS_EXTERN3" }; // NOT EXTERNAL2
static readonly string[] Orient   = { "COMPASS_ORIENT",   "COMPASS_ORIENT2",  "COMPASS_ORIENT3" };
static readonly string[] DevId    = { "COMPASS_DEV_ID",   "COMPASS_DEV_ID2",  "COMPASS_DEV_ID3" };
static readonly string[] Prio     = { "COMPASS_PRIO1_ID", "COMPASS_PRIO2_ID", "COMPASS_PRIO3_ID" };
static readonly string[] OfsX     = { "COMPASS_OFS_X",    "COMPASS_OFS2_X",   "COMPASS_OFS3_X" };   // digit in the MIDDLE
static readonly string[] OfsY     = { "COMPASS_OFS_Y",    "COMPASS_OFS2_Y",   "COMPASS_OFS3_Y" };
static readonly string[] OfsZ     = { "COMPASS_OFS_Z",    "COMPASS_OFS2_Z",   "COMPASS_OFS3_Z" };
```

Note the priority parameters are the odd ones out in the other direction: they are `COMPASS_PRIO1_ID`,
`COMPASS_PRIO2_ID`, `COMPASS_PRIO3_ID` — **instance 1 does carry its digit here** (`PRIO1`, not `PRIO`).

`COMPASS_DEV_IDx` are declared `@ReadOnly: True`, *"Automatically detected, do not set manually"*.

### 1.2 Global compass parameters

| Parameter | Default | Values / range | Notes |
|---|---|---|---|
| `COMPASS_ENABLE` | `1` | — | `@RebootRequired` |
| `COMPASS_MOTCT` | — | `0:Disabled, 1:Use Throttle, 2:Use Current` | doc: *"Do not change manually"* |
| `COMPASS_AUTO_ROT` | `2` | `0:Disabled, 1:CheckOnly, 2:CheckAndFix, 3:…45deg` | |
| `COMPASS_OFFS_MAX` | **`1800`** | 500–3000 | prearm offset ceiling |
| `COMPASS_CAL_FIT` | **`16.0`** | 4–32; `4:Very Strict, 8:Strict, 16:Default, 32:Relaxed` | **lower = stricter** |
| `COMPASS_LEARN` | — | `0:Disabled, 2:EKF-Learning, 3:InFlight-Learning` | |
| `COMPASS_DEC` | — | radians | |
| `COMPASS_AUTODEC` | `1` | — | |
| `COMPASS_OPTIONS` | — | bit0 `CalRequireGPS`, bit1 DroneCAN auto-replace | |
| `COMPASS_FLTR_RNG` | — | — | |
| `COMPASS_DEV_ID4` … `COMPASS_DEV_ID8` | — | — | extra/unregistered compasses, **never persisted** |

⚠️ **Stale documentation:** the ardupilot.org page still claims the prearm offset threshold is `600`. It is
not — the code uses `COMPASS_OFFS_MAX`, default **1800**. Render the panel's offset limit from the live
parameter, never from a hard-coded 600.

### 1.3 The `COMPASS_TYPEMASK` → `COMPASS_DISBLMSK` rename trap

- AP 4.1–4.4 spell it **`COMPASS_TYPEMASK`**. AP 4.5+/master spell it **`COMPASS_DISBLMSK`**.
- Same parameter index (33), same underlying variable — only the name changed.
- The exact release of the rename, and whether a backward-compatible alias exists, is **UNVERIFIED —
  verify against target firmware.** Do not branch on a version number.

**Rule: probe for both names, in this order, and cache which one this board answered to.**

1. `PARAM_REQUEST_READ` by name for `COMPASS_DISBLMSK`.
2. If no `PARAM_VALUE` arrives within the read timeout (or `PARAM_ERROR DOES_NOT_EXIST` comes back),
   `PARAM_REQUEST_READ` by name for `COMPASS_TYPEMASK`.
3. If neither answers, treat the feature as absent and grey it out — **do not write either name blindly.**
4. Bind every later read/write of that setting to the resolved name.

Absence of `PARAM_ERROR` is normal on older firmware; a silent timeout is the expected "does not exist"
signal there. Apply the same probe-both pattern anywhere else a rename is suspected (e.g. the
`ARMING_CHECK` / `ARMING_SKIPCHK` split — also UNVERIFIED as to release, verify against target firmware).

Related version drift: **`COMPASS_DIA*` default is `0` in Copter-4.1 but `1.0` in master.** Never treat
`DIA = 0` as "obviously wrong" without knowing the firmware; render the raw value.

---

## 2. Priority model

### 2.1 What the priority parameters hold

- `COMPASS_PRIO1_ID`, `COMPASS_PRIO2_ID`, `COMPASS_PRIO3_ID` hold **device IDs** — the same integer that
  appears in `COMPASS_DEV_IDx`. **They are NOT instance indices.** A value of `73225` is a device id;
  a value of `2` is *not* "instance 2", it is a (nonsensical) device id.
- Parameter doc, verbatim: *"Compass device id with 1st order priority, set automatically if 0. Reboot
  required after change."* — plus `@RebootRequired: True`.
- **`0` = empty slot.** An empty slot is auto-filled at the next boot from the detected compasses, and
  **compasses compact upward to fill gaps** (a hole in the middle does not persist).
- Available on **AP 4.1+**. On older firmware the priority mechanism does not exist.
- The device-id integer is sized deliberately so it survives the float MAVLink parameter transport without
  loss (see §3.1) — but it is still an `INT32`-typed parameter: decode with `(int32_t)lroundf(param_value)`,
  never a reinterpret cast.

### 2.2 How to reorder — and what never to touch

> **To reorder: write ONLY `COMPASS_PRIOx_ID`, set to the target compass's own `COMPASS_DEV_IDx` value.**
> **NEVER write `COMPASS_DEV_IDx`.**

ArduPilot documentation, verbatim: *"never change a compass's `COMPASS_DEV_IDx` ID value manually and then
reboot!"* `COMPASS_DEV_IDx` is `@ReadOnly` and is matched at boot against the physically detected sensors;
a hand-written value mis-seats sensors.

Equally forbidden: **copying `PRIOx_ID` values from another board.** Those integers name *that* board's
sensors. On a different board they name devices that do not exist ⇒ `PreArm: Compass %d not found`.
Always source the value from the board in front of you.

### 2.3 🔴 The block swap at boot — the single most important consequence

**Reboot is mandatory after any `COMPASS_PRIOx_ID` change.** At boot, `_reorder_compass_params()` does not
merely re-rank the compasses — it **physically swaps the entire per-instance parameter block between
slots**, including:

`external`, `orientation`, `offset`, `diagonals`, `offdiagonals`, `scale_factor`, `dev_id`, `motor_compensation`

⇒ **After the reboot, the unsuffixed parameters — `COMPASS_OFS_*`, `COMPASS_DIA_*`, `COMPASS_ODI_*`,
`COMPASS_SCALE`, `COMPASS_ORIENT`, `COMPASS_EXTERNAL`, `COMPASS_MOT_*`, `COMPASS_DEV_ID` — belong to the
priority-1 compass**, which is a *different physical sensor* than before the reorder.

What this means for code:

| Situation | Consequence |
|---|---|
| You cached `COMPASS_DEV_ID = X` before the reboot | After the reboot `COMPASS_DEV_ID` may be a completely different sensor's id. The cache is **not stale, it is wrong** — it names the right parameter and the wrong device. |
| You cached offsets/orientation "for instance 2" | After the reboot those values live under a different instance suffix. Anything keyed by instance index is invalidated. |
| You have a compass panel open | Every row must be rebuilt from scratch, not diffed. |
| You are mid-way through a multi-parameter write | Abort. Do not resume a write sequence across a reboot. |

**Mandatory rule: discard the entire compass parameter cache on reboot and re-fetch every compass
parameter by name.** Key all in-memory state by **device id**, never by instance index, if you need to
correlate a compass across a reboot. (General reboot rule from the parameter reference: *params must be
re-fetched after reboot* — for compasses it is not an optimisation, it is a correctness requirement.)

### 2.4 Symptoms of an incomplete reorder

- Prearm, if the reboot has not happened: **`PreArm: Compass order change requires reboot`**
- Starting a normal calibration after a priority change without a reboot: STATUSTEXT
  **`Compass cal requires reboot after priority change`** — severity ERROR, and note it carries **no
  `PreArm: ` prefix**, so a matcher keyed on that prefix will miss it.
- A priority slot naming a device that is not present: **`PreArm: Compass %d not found`**

⚠️ `STATUSTEXT` `text` is `char[50]` **without null termination**, and long messages arrive chunked via the
`id` + `chunk_seq` extensions. **Reassemble chunked STATUSTEXT before string-matching any of the above.**

---

## 3. Identifying each compass

### 3.1 Decoding `COMPASS_DEV_IDx`

Bitfield from `AP_HAL/Device.h` (little-endian packing, per ArduPilot's own `Tools/scripts/decode_devid.py`):

| Field | Extraction | Width |
|---|---|---|
| `bus_type` | `devid & 0x07` | 3 bits |
| `bus` | `(devid >> 3) & 0x1F` | 5 bits |
| `address` | `(devid >> 8) & 0xFF` | 8 bits |
| `devtype` | `(devid >> 16) & 0xFF` | 8 bits |

The layout is sized deliberately so the value survives the float MAVLink parameter transport without loss.

`devid == 0` means **no compass in that slot** — not "unknown device".

### 3.2 `enum BusType`

| Value | Name |
|---|---|
| `0` | `UNKNOWN` |
| `1` | `I2C` |
| `2` | `SPI` |
| `3` | `UAVCAN` / `DRONECAN` |
| `4` | `SITL` |
| `5` | `MSP` |
| `6` | `SERIAL` |
| `7` | `WSPI` |

🔴 **There is no `BUS_TYPE_EXTERNALAHRS`.** An ExternalAHRS compass registers as **`SERIAL` (6)**; an MSP
compass registers as **`MSP` (5)**. Do not add a synthetic bus type, and do not treat `SERIAL` as
"something went wrong" — on many airframes it is the external AHRS compass.

### 3.3 Compass `devtype` table

Authoritative source: `AP_Compass_Backend.h`.

| devtype | Sensor | devtype | Sensor |
|---|---|---|---|
| `0x01` | `HMC5883_OLD` | `0x0E` | `MAG3110` |
| `0x02` | `LSM303D` | `0x0F` | `SITL` |
| `0x04` | `AK8963` | `0x10` | `IST8308` |
| `0x05` | `BMM150` | `0x11` | `RM3100` |
| `0x06` | `LSM9DS1` | `0x12` | `RM3100_2` (unused) |
| `0x07` | `HMC5883` | `0x13` | `MMC5983` |
| `0x08` | `LIS3MDL` | `0x14` | `AK09918` |
| `0x09` | `AK09916` | `0x15` | `AK09915` |
| `0x0A` | `IST8310` | `0x16` | `QMC5883P` |
| `0x0B` | `ICM20948` | `0x17` | `BMM350` |
| `0x0C` | `MMC3416` | `0x18` | `IIS2MDC` |
| `0x0D` | `QMC5883L` | | |

- `0x19 LIS2MDL` is **retired** — the same sensor as `IIS2MDC`. Do not reuse the code, do not render it.
- ⚠️ **`decode_devid.py` disagrees with the header on `0x09`, `0x11`, `0x12`, `0x13`. Trust the header
  (the table above).** This disagreement is itself flagged UNVERIFIED as to which tools in the wild follow
  which — if you cross-check against another GCS's rendering and it differs on those four codes, the
  header is still the answer; **verify against target firmware** before "fixing" your table.
- An unknown devtype must render as `devtype 0x??` (hex), never blank and never guessed.

### 3.4 Recommended rendered form

`BUSTYPE:bus N:addr 0xHH:SENSOR` — e.g. **`I2C:bus 1:addr 0x0E:IST8310`**

```csharp
public readonly record struct CompassDevId(int Raw)
{
    public int  BusType => Raw & 0x07;
    public int  Bus     => (Raw >> 3) & 0x1F;
    public int  Address => (Raw >> 8) & 0xFF;
    public int  DevType => (Raw >> 16) & 0xFF;
    public bool IsEmpty => Raw == 0;

    // BusTypeName / DevTypeName come from the §3.2 and §3.3 tables; unknown -> "0x{v:X2}".
    public string Render() => IsEmpty
        ? "(none)"
        : $"{BusTypeName(BusType)}:bus {Bus}:addr 0x{Address:X2}:{DevTypeName(DevType)}";
}
```

Show the raw integer somewhere too (tooltip or secondary line) — it is the value that goes into
`COMPASS_PRIOx_ID`, and an operator comparing against another tool will want it.

---

## 4. External vs internal

### 4.1 Flag semantics

`COMPASS_EXTERNAL` / `COMPASS_EXTERN2` / `COMPASS_EXTERN3`:

| Value | Meaning | Behaviour |
|---|---|---|
| `0` | Internal | **Runtime-resolved**: auto-detection by bus connection can override it |
| `1` | External | **Runtime-resolved**: auto-detection by bus connection can override it |
| `2` | ForcedExternal | **Operator lock**: auto-detection is disabled |

Parameter doc, verbatim: *"If set to 0 or 1 then auto-detection by bus connection can override the value.
If set to 2 then auto-detection will be disabled."*

The backend enforces this: `set_external()` is a **no-op when the stored value is already `2`**. Therefore:

- **`2` is an operator lock.** The driver will not override it. If you see `2`, a human (or a prior tool
  run) deliberately pinned this compass as external. Surface it as *locked* in the UI; do not silently
  rewrite it to `1`.
- **`0` or `1` read back *after boot* is the runtime-resolved truth** — the driver has already overwritten
  the stored value via `set_and_notify`. That is why the flag is trustworthy once the board is up, and why
  reading it before the driver has settled (or from a saved parameter file) is not.

### 4.2 Backends that always force external

**DroneCAN, MSP, ExternalAHRS, SITL, and any probe on an external I2C bus.** A compass on any of those
will resolve to external regardless of the stored `0`/`1`.

### 4.3 🔴 Instance index never identifies the external compass

There is **no** rule that instance 1 is internal or that the last instance is external. The mapping between
instance index and physical sensor depends on probe order, on the board's hwdef, and — after any reorder —
on the priority block swap of §2.3. **Never classify by instance index.** Never label a row "internal"
because it happens to be first.

### 4.4 Decision procedure — classify one detected compass

Run per instance `i` (1..3), after the board has fully booted and streamed:

1. Read `COMPASS_DEV_IDx` for instance `i`. If `0` ⇒ **slot empty**, stop; this is not a compass.
2. Decode `bus_type` per §3.1.
3. Read the external flag for instance `i` (`COMPASS_EXTERNAL` / `COMPASS_EXTERN2` / `COMPASS_EXTERN3`).
4. Classify:

| Flag | `bus_type` | Classification | Confidence |
|---|---|---|---|
| `2` | any | **External (locked)** | Certain — operator lock, driver will not override |
| `1` | `3 DRONECAN`, `6 SERIAL`, `5 MSP`, `4 SITL` | **External** | Certain — backend forces external and flag agrees |
| `1` | `1 I2C` | **External** | High — external I2C probe forces external |
| `1` | `2 SPI` | **External** | Conflicting: SPI is normally an on-board bus. Flag as ambiguous. |
| `0` | `2 SPI` | **Internal** | High — on-board SPI sensor, flag agrees |
| `0` | `1 I2C` | **Internal** | Moderate — internal I2C sensors exist; accept the flag |
| `0` | `3`/`5`/`6`/`4` | — | **Contradiction**: backend forces external for these buses but the flag reads internal. Do not classify; treat as ambiguous and refuse (§5). |
| any | `0 UNKNOWN`, `7 WSPI` | — | **Ambiguous** — do not guess. |

5. Priority of evidence: **the resolved `COMPASS_EXTERNAL*` flag is primary; `bus_type` corroborates.**
   Where they contradict, the compass is *ambiguous*, not "probably external".

```csharp
enum Mount { Empty, Internal, External, ExternalLocked, Ambiguous }

static Mount Classify(CompassDevId id, int externalFlag)
{
    if (id.IsEmpty) return Mount.Empty;
    if (externalFlag == 2) return Mount.ExternalLocked;          // operator lock, driver no-ops set_external()
    bool busForcesExternal = id.BusType is 3 or 4 or 5 or 6;     // DRONECAN, SITL, MSP, SERIAL
    if (busForcesExternal) return externalFlag == 1 ? Mount.External : Mount.Ambiguous;
    if (id.BusType is 0 or 7) return Mount.Ambiguous;            // UNKNOWN, WSPI
    if (externalFlag == 1) return id.BusType == 2 ? Mount.Ambiguous : Mount.External; // SPI+external = odd
    if (externalFlag == 0) return Mount.Internal;
    return Mount.Ambiguous;
}
```

---

## 5. Procedure: make the external compass primary and set the use flags

Goal: the external compass becomes priority 1, `COMPASS_USE*` is set on it, and the use flag on the
internal compass is cleared.

**Hard prohibitions carried into every step:**
- 🔴 **Never write `COMPASS_DEV_IDx`.** *"never change a compass's `COMPASS_DEV_IDx` ID value manually and
  then reboot!"* It is `@ReadOnly`.
- 🔴 **Never copy `COMPASS_PRIOx_ID` values from another board or from a saved parameter file.** They are
  device ids of *that* board's sensors ⇒ `PreArm: Compass %d not found`. Take them only from **this
  board's own detected `COMPASS_DEV_IDx`**.
- 🔴 **Never leave the board with zero compasses that have `COMPASS_USE*` set.** See §6.

The procedure is **restartable**: every step is idempotent, and step 1 re-derives all state from the board,
so a run interrupted at any point can be started again from step 1.

### Phase A — read the current topology (no writes)

1. **Fetch, by name**, every parameter you will reason about:
   `COMPASS_DEV_ID`, `COMPASS_DEV_ID2`, `COMPASS_DEV_ID3`;
   `COMPASS_PRIO1_ID`, `COMPASS_PRIO2_ID`, `COMPASS_PRIO3_ID`;
   `COMPASS_EXTERNAL`, `COMPASS_EXTERN2`, `COMPASS_EXTERN3`;
   `COMPASS_USE`, `COMPASS_USE2`, `COMPASS_USE3`;
   `COMPASS_ORIENT`, `COMPASS_ORIENT2`, `COMPASS_ORIENT3`; `COMPASS_ENABLE`.
   Use `PARAM_REQUEST_READ` with `param_index = -1` + `param_id` — **by name, never by index** (indices
   shift when hidden parameters are exposed).
2. If `COMPASS_ENABLE == 0`, stop: *"Compasses are disabled (`COMPASS_ENABLE` = 0). Enable and reboot
   before changing compass order."* (`COMPASS_ENABLE` is `@RebootRequired`.)
3. If `COMPASS_PRIO1_ID` does not exist on this board, stop: the priority mechanism is AP 4.1+. Report the
   firmware is too old for ordered compasses rather than writing anything.
4. Decode each non-zero `COMPASS_DEV_IDx` (§3) and classify each instance (§4.4). Build the topology table
   and **show it to the operator before any write**.

### Phase B — identify the external compass and decide whether to proceed

5. Count instances classified `External` or `ExternalLocked`.
6. **Refuse and stop, with the exact message, in any of these situations:**

| Situation | Message to the operator |
|---|---|
| Zero compasses detected | *"No compass detected. Nothing to reorder."* |
| Exactly one compass, and it is internal | *"Only an internal compass is present. Refusing: clearing its use flag would leave the vehicle with no usable compass."* |
| No external compass among 2+ compasses | *"No external compass detected. Refusing to reorder — check the external compass wiring, then re-read the topology."* |
| Any instance classified `Ambiguous` | *"Compass on instance N could not be classified (flag=…, bus=…). Refusing to guess. Verify the compass wiring / `COMPASS_EXTERNAL*` value against the target firmware."* |
| More than one external compass | *"Two external compasses detected. Choose which one is primary."* — require an explicit operator choice; do not auto-pick (§6). |

   **Never proceed on a guess.** An incorrect priority-1 choice puts a wrongly-oriented or wrongly-calibrated
   sensor in charge of yaw.
7. Let `E` = the chosen external compass, `devIdE = COMPASS_DEV_IDx` of `E`. Sanity-check `devIdE != 0`.

### Phase C — write the priority order

8. Compute the desired priority list: `devIdE` first, then the remaining detected device ids in their
   current relative order, then `0` for any unused slot.
9. **If the desired list already equals the current `PRIO1/2/3` values, skip to Phase D** — do not write,
   and do not force an unnecessary reboot.
10. Write `COMPASS_PRIO1_ID = devIdE` via `PARAM_SET`. Then `COMPASS_PRIO2_ID`, `COMPASS_PRIO3_ID`.
    - `handle_param_set` **ignores `packet.param_type`**; the type is resolved by name on the vehicle.
    - The `PARAM_VALUE` echo after a `PARAM_SET` is **broadcast, asynchronous, and carries
      `param_index = 0xFFFF`** — do not treat it as the acknowledgement and never use it to fill an
      index-keyed table.
11. **Verify each write with an independent `PARAM_REQUEST_READ` by name.** Compare as `INT32`:
    `(int)MathF.Round(v)`, **exact, no epsilon**. Retry the set up to 2 times on mismatch, then abort and
    report which parameter would not take the value.

### Phase D — set the use flags

12. Set the use flag on the external compass: `COMPASS_USE*` for `E` ⇒ `1`.
13. Clear the use flag on the internal compass/compasses: `COMPASS_USE*` ⇒ `0`.
    **Before writing, assert that at least one compass will still have `COMPASS_USE* == 1`.** If the
    assertion fails, abort the whole operation and leave the board as found.
14. Verify each use-flag write by read-back, by name, exact integer compare.
    - If a read-back shows the old value on a **float** parameter, remember `save_sync()` can coalesce a
      write whose relative change is `< 1e-4` — report that as **"coalesced"**, not as a failure. This does
      **not** apply to `COMPASS_USE*` / `COMPASS_PRIOx_ID` (integer-typed): there a mismatch is a real
      failure.
15. Note that the use flags are **not** `@RebootRequired` — but the priority change is, and the block swap
    of §2.3 will move the flags with their compass, so do not report success yet.

### Phase E — reboot

16. Require a reboot (`COMPASS_PRIOx_ID` is `@RebootRequired: True`). Tell the operator explicitly that a
    reboot is part of this operation, not an optional follow-up.
17. Issue the reboot through the project's reboot path. **The board drops off the USB bus and
    re-enumerates; the COM number can change.** Close the port immediately and re-resolve the device —
    never reconnect to the cached `COMn`.
18. **Discard the entire compass parameter cache** (§2.3). Anything read in Phase A is now untrustworthy.

### Phase F — re-read and re-verify

19. Re-run Phase A in full against the rebooted board.
20. Assert all of the following; any failure is a **failed operation**, reported with the topology table:
    - `COMPASS_DEV_ID` (unsuffixed, i.e. priority-1 slot) decodes to the external compass — this is the
      block swap having happened.
    - `COMPASS_EXTERNAL` (unsuffixed) is `1` or `2`.
    - `COMPASS_USE` (unsuffixed) is `1`.
    - The internal compass's `COMPASS_USE*` is `0`.
    - At least one `COMPASS_USE*` is `1`.
21. Run the prearm checks and collect `PreArm:` STATUSTEXT (reassembling chunks first). Treat these as
    **the operation is incomplete**:
    - **`PreArm: Compass order change requires reboot`** — the reboot did not take effect, or a further
      priority write happened after it. Return to Phase E.
    - **`PreArm: Compass %d not found`** — a `COMPASS_PRIOx_ID` names a device that is not present.
      Almost always the result of a device id sourced from somewhere other than this board. Zero the
      offending slot and restart from Phase A.
    - STATUSTEXT **`Compass cal requires reboot after priority change`** (ERROR, **no `PreArm:` prefix**)
      if a calibration is started before the reboot — same remedy.
22. Only after step 20 passes and none of the strings in step 21 are present, report success.

---

## 6. Edge cases

| Case | Required behaviour |
|---|---|
| **Only one compass present, and it is external** | Nothing to reorder. Ensure `COMPASS_PRIO1_ID` = its device id (or `0`, which auto-fills at boot) and its `COMPASS_USE*` = `1`. Do not write a priority change just to force a reboot. |
| **Only an internal compass present** | 🔴 **Refuse.** Do not clear its `COMPASS_USE*`. Leaving the board with zero compasses having `COMPASS_USE*` set is a state the tool must **never** create. Report: *"Only an internal compass is present — its use flag will be left set."* |
| **Two external compasses** | Do not auto-pick. Present both with their decoded device ids (§3.4) and their orientation/offset state, and require an explicit operator choice for priority 1. The unchosen external may keep `COMPASS_USE* = 1` as a secondary; this is a choice, not a default — surface it. |
| **DroneCAN compass** | `bus_type == 3`. Always forced external by the backend. Note `COMPASS_OPTIONS` bit1 is *DroneCAN auto-replace* — the device id can change when the node is replaced, which silently breaks a `PRIOx_ID` that names the old node. Re-read the topology rather than trusting a stored id. |
| **Serial / ExternalAHRS / MSP compass** | `bus_type == 6 SERIAL` (ExternalAHRS **and** plain serial) or `5 MSP`. All forced external. Remember **there is no external-AHRS bus type** — do not look for one, and do not report `SERIAL` as an anomaly. |
| **A priority slot names a device that is not present** | Prearm reports `PreArm: Compass %d not found`. Remedy: set that `COMPASS_PRIOx_ID` to `0` (empty ⇒ auto-filled and compacted at the next boot) or to a device id read from **this** board, then reboot. Never "repair" it by writing `COMPASS_DEV_IDx`. |
| **A priority slot is `0` while compasses exist** | Not an error. `0` = empty slot, auto-filled at next boot, and compasses compact upward to close gaps. Render as *"auto (assigned at next boot)"*, not as *"missing"*. |
| **More compasses than priority slots** | Only three priority slots exist. Detected compasses beyond them land in `COMPASS_DEV_ID4`…`COMPASS_DEV_ID8` (extra/unregistered) and are **never persisted**. Show them read-only as "detected, not registered"; do not offer to prioritise or use them, and do not attempt to write anything about them. |
| **`COMPASS_ENABLE == 0`** | Refuse the whole operation; `COMPASS_ENABLE` is `@RebootRequired`, so enabling it is itself a reboot cycle before topology can be read meaningfully. |
| **Firmware without the priority mechanism (pre-4.1)** | Refuse; report the firmware version rather than falling back to writing `COMPASS_DEV_IDx` — that fallback does not exist. |

---

## 7. Compass panel data model

One row per **priority slot** (= compass instance 1/2/3, after any reorder has taken effect). Extra
detected-but-unregistered compasses (`COMPASS_DEV_ID4`…`COMPASS_DEV_ID8`) appear in a separate,
read-only "detected, not registered" list.

### 7.1 Per-instance view model

| Field | Source | Rendered as |
|---|---|---|
| Slot / priority | instance index 1/2/3, i.e. the priority rank | `Priority 1 (primary)` / `Priority 2` / `Priority 3` |
| Device id (raw) | `COMPASS_DEV_ID` / `_ID2` / `_ID3` | integer, secondary line or tooltip |
| Device id (decoded) | §3.1 decode | `I2C:bus 1:addr 0x0E:IST8310`; empty slot ⇒ `(none)`; unknown devtype ⇒ `devtype 0x??` |
| Priority slot value | `COMPASS_PRIO1_ID` / `PRIO2_ID` / `PRIO3_ID` | device id; `0` ⇒ `auto (assigned at next boot)`. Show a warning icon when it does **not** match any detected `COMPASS_DEV_IDx` (⇒ `PreArm: Compass %d not found`). |
| Mount classification | §4.4 | `External` / `External (locked)` / `Internal` / `Ambiguous — verify` |
| Locked | external flag `== 2` | explicit `locked` badge — the driver will not override this value |
| Use flag | `COMPASS_USE` / `USE2` / `USE3` | `Used` / `Not used`, as a toggle |
| Orientation | `COMPASS_ORIENT` / `ORIENT2` / `ORIENT3` | raw enum value **plus** the firmware's `ROTATION` name if you have the metadata; if you do not, render the bare integer — **do not invent rotation names.** |
| Offsets | `COMPASS_OFS*_X/_Y/_Z` (note the middle digit) | three values + magnitude `sqrt(x²+y²+z²)` |
| Offset limit | `COMPASS_OFFS_MAX` (default **1800**) | amber/red when the magnitude approaches/exceeds it. ⚠️ Never hard-code 600 — the docs' 600 is stale. Prearm string: `PreArm: Compass offsets too high` |
| Live field vector | see §7.2 | x/y/z **milligauss** |
| Field length | `sqrt(x²+y²+z²)` of the live vector | value + band indicator against **expected 530, min 185, max 875 mgauss**. Prearm string: `PreArm: Check mag field: %4.0f, max %d, min %d` |
| Health | `SYS_STATUS` `3D_MAG` bit | `Healthy` / `Unhealthy`, see §7.3 |
| Last cal fitness | `MAG_CAL_REPORT.fitness` | mgauss RMS residual, **lower is better**, against `COMPASS_CAL_FIT` (default `16.0`), see §7.4 |
| Pending reboot | see §7.5 | explicit banner/badge |

```csharp
public sealed record CompassRow(
    int      Instance,          // 1..3 == priority rank
    int      DevIdRaw,
    string   DevIdRendered,     // "I2C:bus 1:addr 0x0E:IST8310"
    int      PrioSlotValue,     // COMPASS_PRIOx_ID; 0 == auto at next boot
    bool     PrioSlotMatchesDetected,
    Mount    Mount,             // Internal / External / ExternalLocked / Ambiguous / Empty
    bool     ExternalLocked,    // COMPASS_EXTERNAL* == 2
    bool     Use,               // COMPASS_USE*
    int      Orient,            // COMPASS_ORIENT* raw
    float    OfsX, float OfsY, float OfsZ,
    float    OffsMax,           // COMPASS_OFFS_MAX, default 1800
    float?   FieldX, float? FieldY, float? FieldZ,   // milligauss, null == no live data
    bool?    Healthy,           // from SYS_STATUS 3D_MAG; null == bit not present/enabled
    float?   LastFitness,       // MAG_CAL_REPORT.fitness, lower is better
    float    CalFit,            // COMPASS_CAL_FIT, default 16.0
    bool     RebootPending);
```

### 7.2 Which live message feeds which instance

| Message | Feeds |
|---|---|
| `RAW_IMU` (27) | compass **instance 0** (priority slot 1) |
| `SCALED_IMU2` (116) | compass **instance 1** (priority slot 2) |
| `SCALED_IMU3` (129) | compass **instance 2** (priority slot 3) |

- The index is the **compass instance = priority slot**, not the IMU index (firmware guard is
  `compass.get_count() > instance`).
- **Units are milligauss in all three, including `RAW_IMU`** — ArduPilot passes `get_field()` through
  unconverted even though the `RAW_IMU` spec declares no unit. Convert to µT with `× 0.1` if you display µT.
- ⚠️ **On a 3-IMU / 2-compass vehicle, `SCALED_IMU3` carries IMU3 accel+gyro with a zeroed mag vector.
  Do not render that as a third compass.** Gate the third row on `COMPASS_DEV_ID3 != 0`, and additionally
  suppress a live field of exactly `(0,0,0)`.
- These messages are in the `SRn_RAW_SENS` group, which **defaults to 0 Hz on Copter — nothing streams
  until you ask.** Request them with `MAV_CMD_SET_MESSAGE_INTERVAL` (511), interval in **microseconds**;
  `200000 µs` (5 Hz) per compass is sufficient for the panel. **Stream intervals do not persist across a
  reboot — re-issue the whole set after every reboot/reconnect**, including the reboot in §5 Phase E.
- Until live data arrives, render field/length as `—`, never as `0`.

### 7.3 Health

- `SYS_STATUS` bits: `3D_MAG = 0x04`, `3D_MAG2 = 0x80000`.
- Rule: **healthy iff `(present & bit) && (enabled & bit) && (health & bit)`.** All three, not just health.
- 🔴 **`3D_MAG` health is only `compass.healthy()` — it is NOT "calibrated".** An uncalibrated board still
  reports `3D_MAG` healthy. Never render "healthy" as "ready to fly" or "calibrated". To prove calibration
  you must use the `PREARM_CHECK` bit (`0x10000000`) and/or the prearm STATUSTEXT.
- Relevant prearm strings to surface next to the health indicator (all exact):
  `PreArm: Compass %d not healthy` · `PreArm: Compass not calibrated` ·
  `PreArm: Compass calibrated requires reboot` (no "has"; fires even when the COMPASS check bit is
  disabled) · `PreArm: Compass calibration running` · `PreArm: Compasses inconsistent` ·
  `PreArm: Check mag field (xy diff:%.0f>%d)` · `PreArm: Check mag field (z diff:%.0f>%d)`.
  Consistency limits: 90° xyz, 60° xy, 200 mgauss length difference.
- Failing prearms are re-broadcast roughly every 30 s while disarmed, so the panel can stay current
  without polling; force an immediate refresh with `MAV_CMD_RUN_PREARM_CHECKS` (401, no parameters,
  AP 4.1+, `TEMPORARILY_REJECTED` while armed).
- If `SYS_STATUS` is not streaming, health is `unknown` — render `?`, not `Unhealthy`.

### 7.4 Calibration fitness

- `MAG_CAL_REPORT.fitness` is in **mgauss RMS residual, lower is better**, compared against
  `COMPASS_CAL_FIT` (range 4–32; `4:Very Strict, 8:Strict, 16:Default, 32:Relaxed`; **default 16.0**).
- 🔴 **Threshold doubling:** the plain `COMPASS_CAL_FIT` threshold is used **only for a priority-1
  external compass.** An **internal priority-1 compass and all secondary compasses get `× 2`.**
  The panel must show the effective threshold per row, not one global number — otherwise a row will look
  failed when the firmware considers it passed, or vice versa.
- `fitness` arrives on `MAG_CAL_REPORT` (192), in the `SRn_EXTRA3` group; raise its rate only during a
  calibration. When no report has been seen this session, render `—`, never `0` (which would read as a
  perfect fit).

### 7.5 Pending-reboot state must be visible

Any of these puts the panel into a **pending-reboot** state, and it must be rendered explicitly — a banner
on the panel plus a per-row badge — never merely implied by a greyed-out button:

1. A `COMPASS_PRIOx_ID` was written this session and the board has not rebooted since
   (`COMPASS_PRIOx_ID` is `@RebootRequired: True`).
2. `COMPASS_ENABLE` was written and the board has not rebooted (`@RebootRequired`).
3. `PreArm: Compass order change requires reboot` was received.
4. `PreArm: Compass calibrated requires reboot` was received.

While pending, the panel must additionally warn that **the displayed per-instance values do not yet
correspond to the priority order the operator asked for** — at boot the whole per-instance block is
physically swapped between slots (§2.3), so the mapping of parameter suffix → physical sensor is about to
change. On reboot, drop the cache and rebuild every row from a fresh read (§5 Phase F). Do not diff rows
across a reboot; correlate by **device id** only.
