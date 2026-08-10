# Parameter Protocol, Reference Files and Comparison Profiles

Operational reference for the parameter layer of the ARDU_OTK app: how to read, write and verify ArduPilot
parameters over MAVLink, how to load a user-supplied reference firmware parameter set, and how to compare the
board against it over a **configurable block** of parameters.

Scope note: everything here concerns the parameter transport and the reference comparison. Compass-specific
semantics, calibration commands, telemetry and serial/port handling live in the sibling reference files.

---

## 1. Reading parameters

### 1.1 Message set

| Message / field | Meaning |
|---|---|
| `PARAM_REQUEST_LIST` | Ask the vehicle to stream every parameter. |
| `PARAM_REQUEST_READ` | Ask for one parameter, by name or by index. |
| `PARAM_SET` | Write one parameter. |
| `PARAM_VALUE` | The vehicle's reply / echo. Fields: `param_id[16]`, `param_value` (float), `param_type`, `param_count`, `param_index`. |

`param_id` is a 16-byte field that is **not null-terminated when the name is exactly 16 characters**. Decode as
"take up to 16 bytes, stop at the first `NUL`".

### 1.2 Full fetch with gap detection

The stream is UDP-ish in practice: ArduPilot throttles it to roughly **30% of link bandwidth (5 params per burst
without flow control)**. Expect it **slow and bursty**, and expect drops. A naive "count the messages" loop will
hang or silently return an incomplete set.

Procedure:

1. Send `PARAM_REQUEST_LIST` to the vehicle's system/component id.
2. On the **first** `PARAM_VALUE`, read `param_count` and allocate `bool[param_count]` (`received`), plus a
   name-keyed dictionary for the actual values.
3. For each `PARAM_VALUE`: store by **name**, and if `param_index < param_count` set `received[param_index] = true`.
4. Restart the list timeout (**5000 ms**) on every `PARAM_VALUE` received. Retry the initial
   `PARAM_REQUEST_LIST` up to **4 times** if nothing at all arrives.
5. When the list timeout expires, scan `received` for `false` slots and re-request each missing index individually
   with `PARAM_REQUEST_READ` (`param_index = i`, `param_id` empty), **2 retries** each, 1000 ms ack timeout.
6. Repeat step 5 until no gaps remain or a retry budget is exhausted; report the still-missing indices as
   `ReadFailed` rather than pretending the set is complete.

⚠️ **`param_count` and `param_index` are unstable across a session.** Changing an `AP_PARAM_FLAG_ENABLE`
parameter invalidates the count — hidden parameters appear or disappear and every index after that point shifts.
Consequences that are mandatory, not optional:

- The index array is valid **only for the duration of one fetch**. Never cache it across a write, a reboot or a
  reconnect.
- If a `PARAM_VALUE` arrives with a `param_count` different from the one you allocated against, **abort the fetch
  and restart it**. Do not resize and carry on.
- The **name is the only stable key.** Store, compare and display by name at all times.

### 1.3 Single reads — always by name

```csharp
// Read one parameter BY NAME: param_index = -1 and param_id filled in.
var req = new mavlink_param_request_read_t {
    target_system    = sysId,
    target_component = compId,
    param_id         = ToParamId("COMPASS_OFFS_MAX"), // char[16], NOT null-terminated at 16
    param_index      = -1                              // -1 == "use param_id"
};
```

**Prefer by name over by index** for every read that is not gap-filling. ArduPilot's own documentation warns that
indices shift when hidden parameters are exposed, so an index captured before a write can address a different
parameter after it. Use index reads **only** inside a single fetch's gap-filling loop (§1.2 step 5), and validate
the returned `param_id` against what you expected before trusting the value.

### 1.4 MAVFTP `@PARAM/param.pck` fast path (optimisation)

A faster, gap-free alternative to `PARAM_REQUEST_LIST`. Both Mission Planner and QGroundControl use it.

| Property | Value |
|---|---|
| Path | `@PARAM/param.pck` |
| Format | packed, prefix-compressed |
| Magic | `0x671b` |
| Magic with defaults | `0x671c` |
| Query options | `?start=`, `?count=`, `?withdefaults=1` |

Constraints to honour:

- **All reads on one MAVFTP handle must use the same read size.** Mixing sizes on a handle breaks the transfer.
- Requires working MAVFTP on the link and on the firmware; older builds may not serve the path at all.
- Treat it strictly as an **optimisation with a mandatory fallback**: if the open fails, the magic does not match
  `0x671b`/`0x671c`, or decoding errors out, fall back to §1.2 without surfacing an error to the user.
- The packed set still yields name/value/type triples — feed them into the same name-keyed store, so the rest of
  the app is unaware of which path was used.

---

## 2. Types

**ArduPilot does NOT send everything as `MAV_PARAM_TYPE_REAL32`.** It sets `param_type` per parameter and uses
exactly four values:

| `MAV_PARAM_TYPE` | Numeric | Notes |
|---|---|---|
| `MAV_PARAM_TYPE_INT8` | 2 | integer |
| `MAV_PARAM_TYPE_INT16` | 4 | integer |
| `MAV_PARAM_TYPE_INT32` | 6 | integer |
| `MAV_PARAM_TYPE_REAL32` | 9 | float |

### 2.1 Integer encoding — C cast, not a reinterpret cast

Integers are **C-cast into the float field**, not bit-packed. There is no byte-wise reinterpretation anywhere in
the ArduPilot path.

```csharp
// DECODE an integer parameter
int  value = (int)MathF.Round(pv.param_value);   // == (int32_t)lroundf(param_value)
// ENCODE an integer parameter for PARAM_SET
float wire = (float)value;
```

- Decode rule: `(int32_t)lroundf(param_value)`. **Never** `BitConverter`/`reinterpret_cast`.
- ⚠️ **`INT32` values above 2^24 (16 777 216) lose precision on the wire** — binary32 cannot represent every
  integer past that point. Flag such parameters in the UI as "value exceeds float-exact range"; do not silently
  round-trip them and do not treat a round-trip difference there as a user-visible mismatch without saying why.

### 2.2 `param_type` is advisory on write

`handle_param_set` **ignores `packet.param_type`** — the vehicle resolves the type by name lookup on its own
parameter table. Practical consequences:

- Sending the wrong `param_type` in `PARAM_SET` does not corrupt anything; sending the wrong **value encoding**
  does. Always encode integers as `(float)intValue`.
- Never derive the type from a reference file's type column for the purpose of writing. Use the type reported by
  the **board** (`PARAM_VALUE.param_type`), or `apm.pdef` metadata, as the authority.

---

## 3. Writing and verifying

Writes are **persisted immediately** (`vp->save()`). **There is no separate "write to EEPROM" / save command** —
do not look for one, do not offer one in the UI.

The `PARAM_VALUE` echo after a `PARAM_SET` deserves special handling. mavlink.io claims ArduPilot does not echo;
current source **does** echo (`GCS_SEND_PARAM`), but the echo is:

- **broadcast** (not addressed to you — another GCS's write can produce one),
- **asynchronous** (arrival order relative to your own traffic is not guaranteed),
- and carries **`param_index = 0xFFFF`**.

🔴 **Never use the echo to fill an index-keyed table.** `0xFFFF` is a sentinel, not slot 65535. If your fetch code
indexes blindly you will corrupt the table or throw. Route echoes into the name-keyed store only, and treat them
as a hint, never as proof.

### 3.1 Write → verify procedure

1. **Refuse the write** if the parameter's metadata says `@ReadOnly: True` or `@Volatile: True` (§4). These must
   never be proposed for write by the comparison engine (§6.5).
2. Record `before` = the board's current value and type from the name-keyed store.
3. Encode the new value per §2.1 (integers as `(float)value`).
4. Send `PARAM_SET`. Wait up to **1000 ms** for a matching `PARAM_VALUE`. Retry the `PARAM_SET` up to **2 times**.
   The echo is a liveness hint only — proceed to step 5 regardless of whether it arrived.
5. **Verify with an independent `PARAM_REQUEST_READ` by name** (`param_index = -1`). This is the *only*
   authoritative confirmation. Timeout 1000 ms, **2 retries**.
6. Classify the read-back:
   - equal to the requested value under §7's rules ⇒ **`Match`** (write confirmed);
   - equal to `before`, **and** the parameter's type is **not `INT32`**, **and**
     `|requested − before| < 1e-4 * |before|` ⇒ **`Coalesced`** (see §3.2) — **not an error**;
   - anything else ⇒ **write failure**; retry once, then report `ReadFailed`/`Differs` with both values.
7. If the parameter carries `@RebootRequired: True`, add it to the pending-reboot set and surface it (§4.4).
8. After any reboot, **discard the whole parameter cache and re-fetch** — indices, counts and values are all
   suspect (and stream intervals must be re-issued; see the telemetry reference).

### 3.2 The write-coalescing band (mandatory)

ArduPilot's `save_sync()` **skips the write for non-`INT32` parameters when `|v1 − v2| < 1e-4 * |v1|`**.

⚠️ **A `REAL32` write inside that 1e-4 relative band can silently no-op. The verify read then shows the OLD
value. That is NOT a failure.** Report it as **`Coalesced`**, never as an error:

- UI text: "value unchanged on the board — the requested change is within the firmware's write-coalescing band
  (1e-4 relative) and was not committed."
- Do **not** retry a coalesced write in a loop; it will coalesce again every time.
- Do **not** count it toward the failure budget of a batch write.
- Do **not** apply this excuse to `INT32` parameters — the band does not apply to them, so an unchanged `INT32`
  read-back is a genuine failure.
- Note the band is *relative to the current value*, so it is a no-op for `v1 == 0`.

### 3.3 Related tolerances worth knowing

| Tolerance | Value | Where it bites |
|---|---|---|
| `is_equal()` | absolute `FLT_EPSILON` ≈ `1.19e-7` | ArduPilot-internal equality |
| `save_sync()` coalescing | relative `1e-4`, non-`INT32` only | silent no-op writes (§3.2) |
| App comparison rule | relative `1e-6`, absolute floor `1e-9` | §7 |

### 3.4 `PARAM_ERROR`

Recent firmware **may** send `PARAM_ERROR` with `DOES_NOT_EXIST` / `VALUE_OUT_OF_RANGE` / `PERMISSION_DENIED`,
accompanied by STATUSTEXT `Param write denied (%s)`.

⚠️ **Treat its absence as normal.** Older firmware simply does not emit it, so:

- Handle `PARAM_ERROR` when it arrives (it is a fast, precise failure reason — surface the code verbatim).
- **Never** wait for it, and never treat "no `PARAM_ERROR`" as success. The read-back in §3.1 step 5 remains the
  contract on every firmware.

---

## 4. Reboot-required, read-only and volatile parameters

### 4.1 The three metadata flags

| Flag | Meaning | App behaviour |
|---|---|---|
| `@RebootRequired: True` | The change takes effect only after a reboot. | Write is allowed; UI must show a pending-reboot state (§4.4). |
| `@ReadOnly: True` | Never offer for write. | Write blocked. Compare may still *report* a difference, outcome `ReadOnly`. |
| `@Volatile: True` | Firmware rewrites it at runtime. | **Do not restore from a file.** Excluded from write proposals by default. |

Everything in ArduPilot is persisted; "volatile" is a **metadata concept**, not a storage class.

⚠️ Compass identity parameters are the canonical trap here: `COMPASS_DEV_ID`/`DEV_ID2`/`DEV_ID3` are
`@ReadOnly: True` ("Automatically detected, do not set manually"), and `COMPASS_PRIO1_ID`/`PRIO2_ID`/`PRIO3_ID`
hold device IDs *of that board's own sensors*. Restoring either from a reference file names sensors that do not
exist and produces `PreArm: Compass N not found`. They must be default-excluded from any restore/apply flow
(§6.5). See the compass reference file for the full rule.

### 4.2 Metadata source

`apm.pdef.xml` / `apm.pdef.json`, with per-parameter entries of the form
`<field name="RebootRequired">True</field>`.

| Layout | URL |
|---|---|
| Latest | `https://autotest.ardupilot.org/Parameters/{ArduCopter\|ArduPlane\|Rover\|…}/apm.pdef.xml.gz` |
| Version-pinned | `https://autotest.ardupilot.org/Parameters/versioned/{Copter\|Plane\|Rover\|Sub\|Tracker}/stable-{X.Y.Z}/apm.pdef.xml` |

⚠️ The **vehicle directory names differ between the two layouts** (`ArduCopter` in the latest layout vs `Copter`
in the versioned layout). Keep two separate name maps; do not build one string and reuse it.

### 4.3 Picking the metadata build for the connected board

1. Determine the **vehicle family** from `HEARTBEAT.type` (`MAV_TYPE`) using the mapping table in the telemetry
   reference. Unknown `MAV_TYPE` ⇒ "unknown", **never default to Copter**.
2. Determine the **firmware version** from the boot banner STATUSTEXT (forceable with `MAV_CMD_DO_SEND_BANNER`)
   and/or `AUTOPILOT_VERSION` requested via `MAV_CMD_REQUEST_MESSAGE`.
3. If the version resolves to a stable `X.Y.Z`, fetch the **version-pinned** URL. Otherwise fall back to the
   **latest** URL and mark the metadata as "approximate — not version-matched" in the UI.
4. If neither is reachable, run with **no metadata**: writes still work, but `@RebootRequired`/`@ReadOnly`/
   `@Volatile` are unknown. Degrade explicitly — show "reboot requirement unknown" rather than "no reboot needed".

**Local caching:** cache the downloaded metadata per (vehicle, version) on disk. Mission Planner's de-facto
policy is to refresh when the cache is **older than 7 days**; match it. Serve from cache when offline.

⚠️ Metadata is version-specific and parameters get renamed between releases. Example in scope: `COMPASS_TYPEMASK`
(AP 4.1–4.4) was renamed to `COMPASS_DISBLMSK` in AP 4.5+/master — same index 33, same variable. The exact
release of the rename, and whether an alias exists, is **unverified — verify against target firmware**. A
comparison run against a reference file from the other side of a rename will show one `MissingOnBoard` and one
`NotInReference` for what is physically the same setting; §8 requires that both be visible so an operator can
recognise the pair.

### 4.4 Surfacing "this change needs a reboot"

Hard UI requirements:

1. **Before the write** — when a proposed change targets a `@RebootRequired` parameter, mark the row in the diff
   with a reboot badge so the operator knows before committing.
2. **After the write** — add the parameter to a session-scoped **pending-reboot set** and show a persistent,
   non-dismissible banner: *"N parameter(s) changed require a reboot to take effect"*, expandable to the list of
   names.
3. **Block the "verified / clean" claim.** A comparison or acceptance run must not be reported as passed while
   the pending-reboot set is non-empty — the board's running behaviour does not yet match its stored parameters.
4. **Offer the reboot** from the banner, and after it: close the port, wait for re-enumeration, **re-fetch all
   parameters** (§1.2), re-issue stream intervals, and clear the pending-reboot set only once the re-fetch
   confirms the new values.
5. Read-back of a `@RebootRequired` parameter reflects the **stored** value immediately — a correct read-back is
   *not* evidence the change is active. Never word it as "applied".

---

## 5. Reference file formats

The user supplies the reference firmware parameter set as a file. Two formats must be supported.

### 5.1 Mission Planner `.param` / `.parm`

| Property | Rule |
|---|---|
| Header | none |
| Separator on **write** | **comma** |
| Separator on **read** | tolerate **space, comma or tab** |
| Comments | lines starting with `#` are skipped |
| Fields used | `[0]` = name, `[1]` = value — all further fields ignored |
| Number parsing | `InvariantCulture` — decimal point is always `.` |
| Type column | **none** |
| Provenance | none — MP files carry no firmware/vehicle information |

Worked example (`reference-copter46.param`):

```
# ARDU_OTK reference set - Copter 4.6.0 - CubeOrange - 2026-08-10
ACRO_BAL_PITCH,1
ATC_RAT_RLL_P,0.135
COMPASS_ENABLE,1
COMPASS_OFFS_MAX,1800
COMPASS_CAL_FIT,16
AHRS_TRIM_X,0.0043
```

Because MP files carry no provenance, **write `#` comment lines** with vehicle, firmware version and board when
this app exports one — both MP's and this app's readers skip them.

### 5.2 QGroundControl `.params`

| Property | Rule |
|---|---|
| Separator | **tab**, exactly **5 columns** |
| Columns | `Vehicle-Id`, `Component-Id`, `Name`, `Value`, `Type` |
| `Type` | numeric `MAV_PARAM_TYPE` (2 / 4 / 6 / 9) |
| Header | `#` lines: `# Onboard parameters for Vehicle 1`, `# Stack: ArduPilot`, `# Vehicle:`, `# Version:`, `# Git Revision:` |

Worked example (`reference-copter46.params`):

```
# Onboard parameters for Vehicle 1
#
# Stack: ArduPilot
# Vehicle: Copter
# Version: 4.6.0
# Git Revision: 0a1b2c3d
#
# Vehicle-Id	Component-Id	Name	Value	Type
1	1	ACRO_BAL_PITCH	1	9
1	1	ATC_RAT_RLL_P	0.135000	9
1	1	COMPASS_ENABLE	1	2
1	1	COMPASS_OFFS_MAX	1800	4
```

The `Type` column is useful **provenance and a cross-check**, but per §2.2 it is not authoritative for writing —
the board's `param_type` wins.

### 5.3 Format detection is a HARD REQUIREMENT

⚠️ **Mission Planner's loose splitter silently mis-parses a QGC file.** Because it splits on space/comma/**tab**
and takes only fields `[0]` and `[1]`, every QGC data row `1⇥1⇥NAME⇥VALUE⇥TYPE` collapses to
**`name = "1", value = 1`**. The concrete corruption:

- The entire reference set reduces to a **single bogus parameter named `1`**, repeated and overwritten.
- Every real parameter in the profile then reports `MissingInReference`, so the comparison looks catastrophically
  wrong for reasons that have nothing to do with the board.
- In a restore/apply flow it is worse: the app would attempt to write a parameter named `1`, which does not exist
  — best case a `PARAM_ERROR` / `DOES_NOT_EXIST`, worst case a silent no-op that the operator reads as "restored".
- **It fails silently.** There is no parse exception to catch. This is why detection must be explicit.

Detection rule (implement exactly):

> Scan for the **first non-comment, non-empty line**. If it splits into **5 tab-separated fields** and field `[2]`
> is a valid parameter name, the file is **QGC `.params`**. Otherwise it is **Mission Planner `.param`/`.parm`**.

Do **not** decide from the file extension. Do not decide from the presence of `#` headers (MP files may have
comment lines too).

```csharp
static ParamFileFormat Detect(IEnumerable<string> lines)
{
    foreach (var raw in lines)
    {
        var line = raw.Trim();
        if (line.Length == 0 || line[0] == '#') continue;
        var f = line.Split('\t');
        return (f.Length == 5 && IsValidParamName(f[2].Trim()))
            ? ParamFileFormat.QGroundControl
            : ParamFileFormat.MissionPlanner;
    }
    throw new InvalidDataException("No parameter rows found.");
}

// param_id is char[16]; names observed in ArduPilot are upper-case A-Z, 0-9 and '_'.
static bool IsValidParamName(string s) =>
    s.Length is > 0 and <= 16 && s.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_');
```

Both readers must parse values with
`float.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture)` — see §7.

---

## 6. THE CONFIGURABLE COMPARISON BLOCK

The core deliverable. The operator selects (or edits) a **comparison profile** that declares *which block of
parameters* is compared, how the results are grouped, and how severely each difference is treated. The profile is
a plain JSON file the user can edit and version alongside the reference `.param`/`.params` file.

### 6.1 Profile schema

| Field | Type | Meaning |
|---|---|---|
| `profileVersion` | int | Schema version. Reject unknown majors. |
| `name` | string | Displayed profile name. |
| `description` | string | Free text shown in the UI header and in the exported report. |
| `defaults.severity` | `critical` \| `warning` \| `informational` | Severity applied when a rule does not set one. |
| `defaults.tolerance.relative` | float | Default REAL32 relative epsilon (`1e-6`). |
| `defaults.tolerance.absoluteFloor` | float | Default absolute floor (`1e-9`). |
| `unmatchedOnBoard` | `ignore` \| `informational` \| `warning` \| `critical` | **How to treat parameters present on the board but matched by no `include` rule** (i.e. outside the configured block). |
| `missingInReference` | `ignore` \| `informational` \| `warning` \| `critical` | How to treat in-block parameters the board has but the reference file does not (`NotInReference`). |
| `useBuiltInExclusions` | bool | Apply the built-in skip list of §6.4. Default `true`. |
| `sections[]` | array | **Ordered** labelled groups; drives UI grouping and report sections. |
| `sections[].id` / `.label` | string | Stable id + display label. |
| `sections[].include[]` | array | **Ordered** rules. |
| `include[].pattern` | glob | Glob over parameter names: `*` = any run of chars, `?` = one char. |
| `include[].severity` | enum | Overrides `defaults.severity`. |
| `include[].tolerance` | object | Overrides the default tolerance. **Use the same two keys as `defaults.tolerance`: `{"relative":…}` and/or `{"absoluteFloor":…}`.** Do not introduce an `absolute` alias — one spelling only, or a parser written from this table will reject valid profiles. |
| `include[].note` | string | Shown as the row hint in the UI. |
| `exclude[]` | array of glob | Global exclusions, applied **after** all includes. |
| `sections[].exclude[]` | array of glob | Section-scoped exclusions. |

### 6.2 Rule resolution order (implement exactly)

1. Normalise the parameter name to upper case.
2. Walk `sections` in order; within a section walk `include` in order. **First matching rule wins** and fixes the
   parameter's section, severity and tolerance. Later rules never override an earlier match — this is what lets
   the user put a narrow high-severity rule above a broad one.
3. If no `include` matched ⇒ the parameter is **outside the configured block**: apply `unmatchedOnBoard`.
4. Apply the section `exclude`, then the global `exclude`, then the built-in exclusions (§6.4, when
   `useBuiltInExclusions` is true). Any hit ⇒ outcome **`Excluded`**, regardless of what matched in step 2.
5. Apply metadata gates: `@ReadOnly` / `@Volatile` ⇒ never a write candidate (§6.5).

```csharp
static Regex GlobToRegex(string glob) => new(
    "^" + Regex.Escape(glob).Replace("\\*", ".*").Replace("\\?", ".") + "$",
    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);
```

### 6.3 Complete example profile

`profiles/otk-acceptance-copter.json`:

```json
{
  "profileVersion": 1,
  "name": "OTK acceptance - Copter, sensor + attitude block",
  "description": "Compares a board against the OTK reference set. Compass and INS identity/config are critical; rate-loop gains are warnings; logging and telemetry are informational.",
  "defaults": {
    "severity": "warning",
    "tolerance": { "relative": 1e-6, "absoluteFloor": 1e-9 }
  },
  "unmatchedOnBoard": "ignore",
  "missingInReference": "informational",
  "useBuiltInExclusions": true,
  "sections": [
    {
      "id": "compass",
      "label": "Compass",
      "include": [
        { "pattern": "COMPASS_DEV_ID*", "severity": "informational",
          "note": "@ReadOnly - reported for identity only, never written" },
        { "pattern": "COMPASS_PRIO?_ID", "severity": "informational",
          "note": "Board-specific device IDs - never restore from the reference file" },
        { "pattern": "COMPASS_OFS*",   "severity": "critical", "tolerance": { "absolute": 1.0 } },
        { "pattern": "COMPASS_DIA*",   "severity": "critical", "tolerance": { "relative": 1e-3 } },
        { "pattern": "COMPASS_ODI*",   "severity": "critical", "tolerance": { "absolute": 0.01 } },
        { "pattern": "COMPASS_SCALE*", "severity": "critical" },
        { "pattern": "COMPASS_MOT*",   "severity": "informational",
          "note": "CompassMot result - vehicle-specific, transfer only for identical builds" },
        { "pattern": "COMPASS_*",      "severity": "critical" }
      ],
      "exclude": [
        "COMPASS_DEV_ID4", "COMPASS_DEV_ID5", "COMPASS_DEV_ID6",
        "COMPASS_DEV_ID7", "COMPASS_DEV_ID8"
      ]
    },
    {
      "id": "ins",
      "label": "IMU / INS",
      "include": [
        { "pattern": "INS_ACC*_ID",   "severity": "informational",
          "note": "Board-specific IMU identity" },
        { "pattern": "INS_ACCOFFS*",  "severity": "critical", "tolerance": { "absolute": 0.05 } },
        { "pattern": "INS_ACCSCAL*",  "severity": "critical", "tolerance": { "relative": 1e-3 } },
        { "pattern": "INS_GYROFFS*",  "severity": "informational",
          "note": "Re-derived at every boot - do not treat as a config difference" },
        { "pattern": "INS_*",         "severity": "warning" }
      ]
    },
    {
      "id": "ahrs",
      "label": "AHRS / trim",
      "include": [
        { "pattern": "AHRS_TRIM_X", "severity": "critical", "tolerance": { "absolute": 0.0017 } },
        { "pattern": "AHRS_TRIM_Y", "severity": "critical", "tolerance": { "absolute": 0.0017 } },
        { "pattern": "AHRS_TRIM_Z", "severity": "informational", "note": "Not Used" },
        { "pattern": "AHRS_ORIENTATION", "severity": "critical",
          "note": "@RebootRequired - must be set before level calibration" },
        { "pattern": "AHRS_*", "severity": "warning" }
      ]
    },
    {
      "id": "attitude-tuning",
      "label": "Attitude rate loops",
      "include": [
        { "pattern": "ATC_RAT_*", "severity": "warning", "tolerance": { "relative": 1e-4 },
          "note": "Rate-loop gains - compared with a loose relative tolerance" },
        { "pattern": "ATC_*",     "severity": "informational" }
      ]
    },
    {
      "id": "arming",
      "label": "Arming and safety",
      "include": [
        { "pattern": "ARMING_CHECK",   "severity": "critical",
          "note": "AP 4.5/4.6 name - probe for ARMING_SKIPCHK on master, polarity is inverted" },
        { "pattern": "ARMING_SKIPCHK", "severity": "critical",
          "note": "master name - verify against target firmware" },
        { "pattern": "ARMING_*",       "severity": "warning" }
      ]
    },
    {
      "id": "logging-telemetry",
      "label": "Logging and telemetry",
      "include": [
        { "pattern": "LOG_*", "severity": "informational" },
        { "pattern": "SR?_*", "severity": "informational",
          "note": "Legacy stream rates - the app uses SET_MESSAGE_INTERVAL, which does not persist" }
      ]
    }
  ],
  "exclude": [
    "*_CALTEMP"
  ]
}
```

Notes on the example that generalise:

- `COMPASS_*` appears **last** in its section so the specific rules above it win — this is the intended use of
  ordered rules.
- Both `ARMING_CHECK` and `ARMING_SKIPCHK` are listed because the rename is **unverified — verify against target
  firmware**, and the polarity is inverted between them (`ARMING_CHECK` is an inclusion bitmask, default all;
  `ARMING_SKIPCHK` defaults to 0 with `-1` skipping all). Whichever name the board does not have simply yields
  `MissingOnBoard` at whatever severity you set; keep such probe rules at `informational` if you do not want them
  to break a clean run.
- Absolute tolerances are the right tool for offsets in physical units (mgauss, m/s², rad); relative for gains.

### 6.4 Built-in default exclusions

Mission Planner's de-facto skip list on restore/compare, applied when `useBuiltInExclusions` is `true`
(verbatim):

```
SYSID_SW_MREV, WP_TOTAL, CMD_TOTAL, FENCE_TOTAL, SYS_NUM_RESETS,
ARSPD_OFFSET, GND_ABS_PRESS, GND_TEMP, BARO1_GND_PRESS, BARO2_GND_PRESS,
BARO3_GND_PRESS, BARO_GND_TEMP, CMD_INDEX, LOG_LASTFILE, FORMAT_VERSION
```

These are mission counters, ground-reference pressures/temperatures and firmware bookkeeping — they differ on
every board and every power-up and are meaningless in a comparison. Ship the list as a resource, show it in the
UI as "built-in exclusions", and allow the user to disable it wholesale (`useBuiltInExclusions: false`) but log
loudly when they do.

### 6.5 Write-safety gates (non-negotiable)

1. A parameter whose metadata says **`@ReadOnly: True` is never proposed for write.** Show the difference,
   outcome `ReadOnly`, with the write action disabled.
2. A parameter whose metadata says **`@Volatile: True` is never restored from a file** — the firmware rewrites it
   at runtime, so writing it is at best pointless.
3. When metadata is unavailable (§4.3 step 4), fall back to a hard-coded deny list for the identity parameters
   that are known `@ReadOnly` — at minimum `COMPASS_DEV_ID*` — and mark the write gate as "metadata unavailable,
   conservative mode".
4. Board-identity parameters (`COMPASS_DEV_ID*`, `COMPASS_PRIO?_ID`, `INS_ACC*_ID`) must be reportable but never
   part of a bulk "apply reference" action, even if the profile's severity says `critical`.

---

## 7. Comparison rules

The wire type is IEEE-754 **binary32** (~7.2 significant decimal digits). QGC writes shortest-round-trip decimals;
Mission Planner writes the float's short decimal form. Both land back on the same lattice **only if you parse them
as `float`**.

### 7.1 Algorithm

1. **Resolve the type first.** Use `PARAM_VALUE.param_type` from the board; fall back to `apm.pdef`. Do not use
   the reference file's type column as the authority (§2.2).
2. **`INT8` / `INT16` / `INT32` ⇒ compare `(int)MathF.Round(v)` exactly. No epsilon, ever.** Bitmasks and enums
   have no meaningful "close". A profile tolerance on an integer rule is ignored — say so in the UI rather than
   applying it.
3. **`REAL32` ⇒ equal if** `a == b` **OR** `MathF.Abs(a - b) <= rel * MathF.Max(MathF.Abs(a), MathF.Abs(b))`,
   with an **absolute floor** so that near-zero comparisons do not collapse:
   `MathF.Abs(a - b) <= MathF.Max(absFloor, rel * MathF.Max(|a|, |b|))`.
   Defaults `rel = 1e-6f`, `absFloor = 1e-9f`; a rule's `tolerance.relative` / `tolerance.absoluteFloor` overrides them (§6.1 — one spelling only, no `absolute` alias).
4. **Normalise `-0f` → `0f`** on both sides before comparing or displaying. `-0f == 0f` is true in IEEE, but the
   two format differently and will produce a cosmetic "difference" in any string-based path.
5. Treat `NaN` on either side as `ReadFailed`, not as `Differs` — `NaN != NaN` would otherwise report a permanent
   phantom difference.

```csharp
static float ParseRef(string token) =>
    Norm(float.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture));

static float Norm(float v) => v == 0f ? 0f : v;   // collapses -0f to 0f

static bool IntEqual(float board, float reference) =>
    (int)MathF.Round(board) == (int)MathF.Round(reference);

static bool RealEqual(float a, float b, float rel = 1e-6f, float absFloor = 1e-9f)
{
    if (a == b) return true;
    float scale = MathF.Max(MathF.Abs(a), MathF.Abs(b));
    return MathF.Abs(a - b) <= MathF.Max(absFloor, rel * scale);
}
```

### 7.2 Parse as `float`, not `double`

`float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture)` — **never `double.Parse` and compare**.

Reason: the board's value *is* a binary32. Parsing the reference token into a `double` produces the nearest
*double* to the decimal text, which is generally **not** the same number as the nearest *float*. Comparing that
double against a float widened to double leaves a residue on the order of `1e-8` relative — right at the edge of
any sane epsilon — so identical parameter sets intermittently report differences that vanish when the value is
re-exported. Parsing as `float` lands the reference on the same lattice as the board and the comparison becomes
exact for values that truly round-trip.

### 7.3 Never compare formatted strings

🔴 **Never compare formatted strings.** `"1"` vs `"1.0"` vs `"1.000000"`, `"0"` vs `"-0"`, `"1e-05"` vs
`"0.00001"` are all the same parameter value and all different strings. Formatting is a **display** concern only
— and if the app ever formats with the current culture instead of `InvariantCulture`, a comma decimal separator
turns `0,135` into a parse failure or a truncated `0`. Parse to `float`, compare numerically, format last.

---

## 8. Diff result model

### 8.1 Outcomes

| Outcome | Meaning | UI requirement |
|---|---|---|
| `Match` | In-block, present both sides, equal under §7. | Neutral row; collapsed by default; counts toward "N of M matched". |
| `Differs` | In-block, present both sides, not equal. | Show **board value, reference value, delta**, section label and rule severity colour. Offer "write reference value" **unless** gated by §6.5. |
| `MissingOnBoard` | In the reference file, not on the board. | Show as a gap, not a difference. No write action (the board has no such parameter). Common causes: different firmware version, a renamed parameter, a disabled feature whose sub-tree is hidden. Prompt the operator to look for a `NotInReference` twin (rename). |
| `NotInReference` | On the board and in-block, absent from the reference file. | Severity from `missingInReference`. Offer "add to reference" in the export. |
| `Excluded` | Matched an exclusion (profile or built-in §6.4). | Hidden by default; visible behind a "show excluded" toggle, with the exclusion source named. Never counted as pass or fail. |
| `ReadOnly` | Differs, but metadata says `@ReadOnly` (or `@Volatile`). | Show the difference; **write action disabled** with the reason. Do not let it block a clean run unless the profile explicitly makes it `critical`. |
| `Coalesced` | A write was inside the firmware's 1e-4 relative band and silently no-opped (§3.2). | **Report as an informational outcome, never as an error.** Text must explain the band. Do not retry. |
| `ReadFailed` | The parameter could not be read after the retry budget, or the value was `NaN`. | 🔴 **Never render as `Match`.** Distinct icon, explicit "unknown" state, and it invalidates a clean run. |

### 8.2 Clean-run rule

> A comparison run is **"clean"** only when **no parameter at `critical` severity has outcome `Differs`,
> `MissingOnBoard` or `ReadFailed`.**

Additional bindings:

- `warning` and `informational` differences are surfaced and counted but do **not** break a clean run.
- `Coalesced` and `Excluded` never break a clean run.
- A non-empty **pending-reboot set** (§4.4) also blocks the clean claim — the stored parameters match, the running
  behaviour does not.
- The run summary must state the counts per outcome **and** the profile name + reference file name, so a "clean"
  result is never ambiguous about *what* was compared.

### 8.3 Exportable diff report (required)

The app must be able to export the run. Minimum content:

1. **Header:** timestamp, profile name + description, reference file name and detected format (§5.3), vehicle
   family and firmware version as resolved in §4.3, board identity, and whether metadata was version-matched.
2. **Rows:** `Section, Name, Type, BoardValue, ReferenceValue, Delta, Severity, Outcome, Note`.
   Values formatted with `InvariantCulture` (the numeric comparison already happened — §7.3).
3. **Summary:** counts per outcome, per severity, and the explicit clean/not-clean verdict with the reason.
4. **Formats:** CSV or JSON for records; plus an optional Mission Planner `.param` export containing **only** the
   `Differs` rows that are write-eligible, so the corrections can be re-applied or reviewed — comma-separated,
   `InvariantCulture`, with `#` provenance comment lines (§5.1).
