# Reference Snapshots, Profiles and the Local Store

How the app captures a known-good board, keeps several named references side by side, stores them in its own
database, makes the operator pick one **before** the board under test is connected, records every result of the
work against the operator-entered ID of that board, and lets the work history be searched afterwards.

Scope boundaries — reference these files, never restate them:

- `parameter-protocol-and-profiles.md` — full-fetch mechanics and gap detection, write/verify, `.param`/`.parm`/
  `.params` formats and format detection, the **comparison-profile JSON schema**, comparison epsilons, and the
  diff outcome model (`Match`, `Differs`, `MissingOnBoard`, `NotInReference`, `Excluded`, `ReadOnly`,
  `Coalesced`, `ReadFailed`) and the clean-run rule.
- `connection-and-telemetry.md` — link setup, the connect handshake that yields `HEARTBEAT.type`,
  `autopilot`, and the firmware version from `AUTOPILOT_VERSION`; staleness rules.
- `compass-topology-and-flags.md` — `COMPASS_DEV_ID*` decoding, priority and use flags.
- `compass-calibration-transfer.md` — which compass parameters are transferable and the identical-hardware rule.
- `imu-level-and-health-verification.md` — level calibration, reboot, prearm verdict.
- `dotnet-mavlink-and-winui-integration.md` — library choice, layering, threading, packaging, the twelve safety
  rules (including §7.7 pre-write snapshot and §7.8 the append-only write audit log), and the UI patterns,
  theming and stock-first rules this document's panel spec builds on.

---

## 1. Concepts and vocabulary

Keep these four words distinct in code, in the UI and in every log line. They are not synonyms and the app
fails confusingly when they are conflated.

### 1.1 Definitions

**Reference snapshot** — an **immutable** captured parameter set plus its provenance. One row per parameter:
name, value exactly as the board reported it, and its `MAV_PARAM_TYPE`. Produced either by a full fetch from a
known-good board or by importing a parameter file. A snapshot has a content hash and is never edited after it
is written. It is the *evidence*, not the *policy*.

**Comparison profile** — the configurable include/exclude/severity/tolerance block that decides *which*
parameters are compared, how they are grouped into sections, and how severe each difference is. **Its JSON
schema, rule-resolution order, built-in exclusions and write-safety gates are owned by
`parameter-protocol-and-profiles.md` §6 — do not redefine them here.** This document only says how a comparison
profile is stored, revised and bound into a reference profile. It is the *policy*, not the *evidence*.

**Reference profile** — the named, operator-selectable bundle. Exactly one snapshot revision + exactly one
comparison-profile revision + an optional compass-calibration block + metadata (name, description, vehicle
family, status, default flag, tags). **This is the unit the operator picks in the top-left panel before
connecting.** The operator never picks a raw snapshot and never picks a bare comparison profile.

**Run** — one verification session against one board under test: arm the session with a profile, connect,
fetch, compare, optionally write/calibrate/reboot, produce a verdict. A run is bound to the exact snapshot
revision and comparison-profile revision it used, so it stays reproducible after the profile is edited.

**Compass-calibration block** (optional part of a profile) — the subset of the snapshot's compass parameters
the operator has blessed for transfer to the board under test: `COMPASS_OFS*_X/_Y/_Z`, `COMPASS_DIA*_X/_Y/_Z`,
`COMPASS_ODI*_X/_Y/_Z`, `COMPASS_SCALE*`, `COMPASS_ORIENT*`, plus the config flags `COMPASS_EXTERNAL` /
`COMPASS_EXTERN2` / `COMPASS_EXTERN3` and `COMPASS_USE` / `COMPASS_USE2` / `COMPASS_USE3`; `COMPASS_MOT*_X/_Y/_Z`
and `COMPASS_MOTCT` only when the operator explicitly opts in for identical builds. `COMPASS_DEV_ID*` and
`COMPASS_PRIO1_ID` / `COMPASS_PRIO2_ID` / `COMPASS_PRIO3_ID` are **stored in the snapshot for identity but are
never members of the block** — see `compass-calibration-transfer.md`.

**Unit** — the physical board under test as the *organisation* identifies it: an operator-entered unit /
serial / tail number (`UnitId`). It is entirely distinct from the **auto-detected hardware identity** (USB
VID/PID, manufacturer and product strings, device instance path, and the `COMPASS_DEV_ID*` / `INS_ACC*_ID`
fingerprint), which the app reads off the board and the operator cannot type. Both are recorded on every run:

| | Operator-entered `UnitId` | Auto-detected hardware identity |
|---|---|---|
| Origin | Typed or picked by the operator (§7.7) | Read from the transport and from the board's parameters |
| Meaning | "which asset is this, in our paperwork" | "which silicon is this, physically" |
| Stability | Survives a board swap inside the same airframe — which is precisely why a change must be surfaced | Changes when the board changes |
| Trust | Assertion; can be mistyped | Observation; cannot be mistyped, but can be absent (imported reference, unknown VID) |
| Storage | `Run.UnitRowId` (FK) + the frozen `Run.UnitIdAtRun` / `Run.UnitIdRawAtRun`, resolving to the `Unit` table (§3.2) | `Run.DutBoardVid/Pid/Product/InstancePath`, `Run.DutHardwareFingerprint` |

They relate one-to-many-over-time: one `UnitId` accumulates a history of runs, and normally every run for that
unit reports the same hardware identity. **A `UnitId` that appears with a hardware identity different from its
previous runs is a signal, not a data-entry problem** — see §7.7.4.

### 1.2 Immutable vs editable

| Artefact | Mutability | Rule |
|---|---|---|
| Reference snapshot (parameter rows, provenance, hash) | **Immutable** | Never updated in place. A change produces a new revision (§4). |
| Comparison profile revision (the JSON body) | **Immutable** | Editing produces a new revision with a new hash; the old revision stays for existing runs. |
| Compass-calibration block selection | **Immutable per revision** | Belongs to the profile revision that references it; changing the member list makes a new profile revision. |
| Reference profile — name, description, tags | **Editable** | Cosmetic; does not invalidate runs. Renames are audited. |
| Reference profile — status (`active` / `retired`), default flag | **Editable** | Operational state, not content. |
| Reference profile — which snapshot / comparison revision it points at | **Editable, audited** | Repointing is an explicit operator action (§4.4). Existing runs keep their own binding. |
| Run, run findings, parameter-write audit rows, calibration ops | **Append-only** | Never edited, never deleted by the app (§3.7, §9, §10.6). |
| `Run.UnitIdAtRun` / `Run.UnitIdRawAtRun` — the unit the run was recorded against | **Immutable once the run starts** | It is part of the evidence. A typo is corrected by a separate, audited re-assignment that re-points `Run.UnitRowId` and keeps the original asserted value (§7.7.5). |
| `Unit` record — display name, description, airframe, tags, status | **Editable** | Organisational metadata about the asset; does not change any past verdict. |

> **Hard rule.** If a piece of data can change the meaning of a past verdict, it is immutable and versioned.
> If it only changes how the artefact is displayed or organised, it is editable.

---

## 2. What a reference snapshot must contain

### 2.1 Parameter payload

One row per parameter, exactly as reported by the board or parsed from the file:

| Field | Type | Notes |
|---|---|---|
| `Name` | text | Upper-cased. Decoded from `PARAM_VALUE.param_id`, which is a fixed `char[16]` field — decode the full 16 bytes and trim, and handle a name that fills the field exactly. |
| `ParamType` | int | The `MAV_PARAM_TYPE` from `PARAM_VALUE.param_type`: `INT8 = 2`, `INT16 = 4`, `INT32 = 6`, `REAL32 = 9`. **Mandatory.** |
| `ValueBits` | int | The IEEE-754 binary32 bit pattern of `param_value`, stored as an integer. This is the byte-exact wire value and the only lossless form. |
| `ValueReal` | real | The same value as a double, for human display, sorting and ad-hoc SQL. **Never comparison input.** |
| `ValueInt` | int, nullable | For `INT8`/`INT16`/`INT32` only: `(int32_t)lroundf(param_value)`. Never a reinterpret cast. Null for `REAL32`. |
| `ParamIndex` | int, nullable | Index at capture time, for diagnostics only. **Never a key** — `param_count`/`param_index` are unstable across a session. |

> 🔴 **The `MAV_PARAM_TYPE` must be preserved.** The comparison in `parameter-protocol-and-profiles.md` §7
> applies **integer-exact** comparison to `INT8`/`INT16`/`INT32` and a **relative float epsilon** to `REAL32`.
> A snapshot that stores only a number, or that stores every value as `REAL32`, silently converts exact integer
> checks into epsilon checks and can pass a board whose mode-select or enable-flag integers differ.
> ArduPilot does **not** send everything as `REAL32`; capture the type it actually sent.
>
> Imported Mission Planner `.param`/`.parm` files carry **no type column**. See §5.2 for the required handling —
> the snapshot must record that the type is inferred, not reported.

### 2.2 Provenance — and why each field exists

| Field | Source | Why it is operationally required |
|---|---|---|
| `VehicleType` | `HEARTBEAT.type` (`MAV_TYPE`), numeric | Selects the mode table and the parameter-metadata build. Drives the vehicle-family compatibility check (§7.4). Store the raw number **and** the resolved family; unknown `MAV_TYPE` stores "unknown" and **never defaults to Copter**. |
| `AutopilotId` | `HEARTBEAT.autopilot` | A reference is only meaningful if it came from an ArduPilot board (`autopilot == 3`, `MAV_AUTOPILOT_ARDUPILOTMEGA`). Anything else must be refused as a reference source. |
| `FirmwareVersion` | The version string resolved by the connect handshake in `connection-and-telemetry.md` §2 (`AUTOPILOT_VERSION`, requested one-shot, and/or the boot banner `STATUSTEXT`) | Parameters are renamed between releases — `COMPASS_TYPEMASK` → `COMPASS_DISBLMSK`, `ARMING_CHECK` → `ARMING_SKIPCHK`. Without the reference's firmware version, a rename pair shows up as one `MissingOnBoard` + one `NotInReference` with no explanation. Also selects the version-pinned `apm.pdef` metadata build. |
| `BoardIdentity` | USB descriptor of the capture link: VID, PID, manufacturer string, product string (= board name), device instance path | Answers "which physical board is this reference from". Required for the identical-hardware gate on compass-calibration transfer. Match on **VID set plus the `ArduPilot` manufacturer string**, never VID alone. |
| `HardwareFingerprint` | The captured values of `COMPASS_DEV_ID`, `COMPASS_DEV_ID2`, `COMPASS_DEV_ID3` and `INS_ACC_ID` / `INS_ACC2_ID` / `INS_ACC3_ID` | Sensor-level identity that survives a board name collision. Feeds the compass-transfer gate: the `DEV_ID` validity rule in `compass-calibration-transfer.md` §2 means a copied calibration only validates when the target's detected `DEV_IDx` already matches. Note the spelling — **`INS_ACC1_ID` does not exist**. |
| `CapturedAtUtc` | Wall clock at capture, UTC, ISO-8601 | Ordering, retention, and "how old is this reference". Always UTC in storage; render local. |
| `OperatorId` | Signed-in Windows account or an app-level operator name | Accountability: who blessed this board as the reference. Required in the exported run report. |
| `Source` | `LiveBoard` \| `ImportedFile` | Trust tier (§5.2). Drives the reduced-trust badge. |
| `SourceFormat` | `Mavlink` \| `MavFtpParamPck` \| `MissionPlannerParam` \| `QgcParams` | A `.param`/`.parm` import has no types; a `.params` import has numeric `MAV_PARAM_TYPE`; a MAVFTP `@PARAM/param.pck` fetch is gap-free by construction. The comparison must know which. |
| `ParamCountCaptured` | Count of rows actually stored | The number the operator sees and the number the compatibility check compares against. |
| `ParamCountReported` | `PARAM_VALUE.param_count` from the first packet, nullable | Live captures only. Together with `ParamCountCaptured` it proves the fetch was complete. **Unstable across a session** — informational, never a key. |
| `GapCount` | Unresolved indices after the re-request pass | **Must be `0` for a snapshot of kind `Reference` (§5.1 step 6).** |
| `ContentHash` | SHA-256, see §2.3 | Identity, deduplication, tamper evidence, revision numbering. |
| `Notes` | Free text | Bench conditions, jig number, why this board was chosen. Appears in the exported report. |

### 2.3 Content hash

Canonicalise before hashing so two captures of the same board hash identically regardless of arrival order or
formatting:

1. Upper-case every name; sort by ordinal name.
2. For each row emit `NAME\t<paramType>\t<valueBits as 8 hex digits>\n` in UTF-8.
3. SHA-256 the byte stream; store lower-case hex.

Rules:

- The hash covers **parameter content only** — not provenance, not notes. Two captures from two identical
  boards therefore hash equal, which is exactly what "the parameter set did not change" should mean.
- Recompute and re-verify the hash whenever a snapshot is loaded for a run. A mismatch is a **hard failure**:
  the store has been edited outside the app. Refuse the run and say so.
- The hash is the revision identity (§4). Re-capturing a board that has not changed produces the same hash and
  therefore **no new revision** — offer "identical to r3, nothing to save".

---

## 3. Storage design

### 3.1 Engine

**Default: SQLite via `Microsoft.Data.Sqlite`.** Rationale: a single-file, zero-install, transactional store is
exactly right for a bench tool that must keep append-only audit history intact across crashes and be copyable
to another workstation as one file.

- **EF Core is optional.** `Microsoft.EntityFrameworkCore.Sqlite` on top is acceptable for the profile/snapshot
  CRUD, but the parameter-row bulk insert and the audit-log append should stay on raw
  `SqliteCommand` + an explicit transaction — a full fetch is over a thousand rows and change tracking buys
  nothing there.
- Connection setup, every connection: `PRAGMA foreign_keys = ON;` (SQLite defaults it **off**, per connection),
  `PRAGMA journal_mode = WAL;` (once, persists in the file), `PRAGMA synchronous = NORMAL;`.
- All timestamps are UTC ISO-8601 text (`yyyy-MM-ddTHH:mm:ss.fffZ`). All decimal text is `InvariantCulture`.
- All database work happens on a background thread — see `dotnet-mavlink-and-winui-integration.md` §3.

### 3.2 Schema

```sql
-- ---------- comparison profiles (schema of Json is owned by parameter-protocol-and-profiles.md §6) ----------
CREATE TABLE ComparisonProfile (
  Id            INTEGER PRIMARY KEY,
  LineageId     TEXT    NOT NULL,          -- groups revisions of "the same" comparison profile
  Revision      INTEGER NOT NULL,          -- 1-based, monotonic within LineageId
  Name          TEXT    NOT NULL,
  Json          TEXT    NOT NULL,          -- the profile document, verbatim
  ContentHash   TEXT    NOT NULL,          -- SHA-256 of Json, whitespace-normalised
  CreatedAtUtc  TEXT    NOT NULL,
  CreatedBy     TEXT    NOT NULL,
  UNIQUE (LineageId, Revision)
);

-- ---------- snapshots ----------
CREATE TABLE Snapshot (
  Id                 INTEGER PRIMARY KEY,
  LineageId          TEXT    NOT NULL,
  Revision           INTEGER NOT NULL,
  Kind               TEXT    NOT NULL CHECK (Kind IN ('Reference','Rollback','RunPre','RunPost')),
  ContentHash        TEXT    NOT NULL,
  VehicleType        INTEGER,              -- HEARTBEAT.type, raw MAV_TYPE
  VehicleFamily      TEXT,                 -- resolved family, or 'unknown'
  AutopilotId        INTEGER,              -- HEARTBEAT.autopilot
  FirmwareVersion    TEXT,
  BoardVid           INTEGER, BoardPid INTEGER,
  BoardManufacturer  TEXT, BoardProduct TEXT, BoardInstancePath TEXT,
  HardwareFingerprint TEXT,                -- JSON: COMPASS_DEV_ID*, INS_ACC*_ID
  CapturedAtUtc      TEXT    NOT NULL,
  OperatorId         TEXT    NOT NULL,
  Source             TEXT    NOT NULL CHECK (Source IN ('LiveBoard','ImportedFile')),
  SourceFormat       TEXT    NOT NULL,
  SourceFileName     TEXT,
  ParamCountCaptured INTEGER NOT NULL,
  ParamCountReported INTEGER,
  GapCount           INTEGER NOT NULL DEFAULT 0,
  TypesInferred      INTEGER NOT NULL DEFAULT 0,   -- 1 when no type column existed (MP file import)
  SupersededById     INTEGER REFERENCES Snapshot(Id),
  Notes              TEXT,
  UNIQUE (LineageId, Revision)
);

CREATE TABLE SnapshotParam (
  SnapshotId INTEGER NOT NULL REFERENCES Snapshot(Id) ON DELETE CASCADE,
  Name       TEXT    NOT NULL,             -- upper-case
  ParamType  INTEGER NOT NULL,             -- MAV_PARAM_TYPE: 2,4,6,9
  ValueBits  INTEGER NOT NULL,             -- binary32 bit pattern
  ValueReal  REAL    NOT NULL,             -- display / ad-hoc query only
  ValueInt   INTEGER,                      -- (int32_t)lroundf(v) for INT8/16/32, else NULL
  ParamIndex INTEGER,                      -- diagnostics only, never a key
  PRIMARY KEY (SnapshotId, Name)
) WITHOUT ROWID;

-- ---------- reference profiles ----------
CREATE TABLE Profile (
  Id                  INTEGER PRIMARY KEY,
  Name                TEXT    NOT NULL UNIQUE,
  Description         TEXT,
  SnapshotId          INTEGER NOT NULL REFERENCES Snapshot(Id) ON DELETE RESTRICT,
  ComparisonProfileId INTEGER NOT NULL REFERENCES ComparisonProfile(Id) ON DELETE RESTRICT,
  CalBlockId          INTEGER REFERENCES CalBlock(Id) ON DELETE RESTRICT,   -- optional
  VehicleFamily       TEXT,                -- denormalised from the snapshot, for fast filtering
  Status              TEXT    NOT NULL CHECK (Status IN ('active','retired')) DEFAULT 'active',
  IsDefault           INTEGER NOT NULL DEFAULT 0,
  Tags                TEXT,                -- JSON array
  CreatedAtUtc        TEXT    NOT NULL,
  UpdatedAtUtc        TEXT    NOT NULL,
  UpdatedBy           TEXT    NOT NULL
);
CREATE UNIQUE INDEX UX_Profile_Default ON Profile(IsDefault) WHERE IsDefault = 1;

CREATE TABLE CalBlock (
  Id            INTEGER PRIMARY KEY,
  SnapshotId    INTEGER NOT NULL REFERENCES Snapshot(Id) ON DELETE RESTRICT,
  Name          TEXT    NOT NULL,
  IncludeMot    INTEGER NOT NULL DEFAULT 0,   -- COMPASS_MOT*/COMPASS_MOTCT, identical builds only
  MemberNames   TEXT    NOT NULL,             -- JSON array of parameter names, resolved at creation
  ContentHash   TEXT    NOT NULL,
  CreatedAtUtc  TEXT    NOT NULL
);

-- ---------- units: the board under test, as the organisation identifies it (§7.7) ----------
CREATE TABLE Unit (
  Id             INTEGER PRIMARY KEY,
  UnitId         TEXT    NOT NULL UNIQUE,   -- NORMALISED form: trimmed, collapsed, upper-cased (§7.7.2)
  UnitIdDisplay  TEXT    NOT NULL,          -- exactly as first entered, for display
  Description    TEXT,                      -- airframe, customer, batch
  Tags           TEXT,                      -- JSON array
  Status         TEXT    NOT NULL CHECK (Status IN ('active','retired')) DEFAULT 'active',
  FirstSeenUtc   TEXT    NOT NULL,
  LastSeenUtc    TEXT    NOT NULL,
  RunCount       INTEGER NOT NULL DEFAULT 0,       -- denormalised, maintained in the run transaction
  LastHardwareFingerprint TEXT,                    -- fingerprint of the most recent run, for §7.4 check 12
  LastBoardVid   INTEGER, LastBoardPid INTEGER, LastBoardProduct TEXT
);

-- ---------- runs ----------
CREATE TABLE Run (
  Id                    INTEGER PRIMARY KEY,
  UnitRowId             INTEGER NOT NULL REFERENCES Unit(Id) ON DELETE RESTRICT,
  UnitIdAtRun           TEXT    NOT NULL,   -- frozen normalised copy; survives a Unit rename
  UnitIdRawAtRun        TEXT    NOT NULL,   -- exactly what the operator typed, before normalisation
  UnitNotes             TEXT,               -- free-text notes about THIS run of THIS unit
  UnitHwMatch           TEXT CHECK (UnitHwMatch IN ('first-run','match','changed','unknown')),
  ProfileId             INTEGER NOT NULL REFERENCES Profile(Id) ON DELETE RESTRICT,
  SnapshotId            INTEGER NOT NULL REFERENCES Snapshot(Id) ON DELETE RESTRICT,
  ComparisonProfileId   INTEGER NOT NULL REFERENCES ComparisonProfile(Id) ON DELETE RESTRICT,
  CalBlockId            INTEGER REFERENCES CalBlock(Id) ON DELETE RESTRICT,
  ProfileNameAtRun      TEXT    NOT NULL,   -- frozen copy: the profile may be renamed later
  SnapshotHashAtRun     TEXT    NOT NULL,
  ComparisonHashAtRun   TEXT    NOT NULL,
  StartedAtUtc          TEXT    NOT NULL,
  EndedAtUtc            TEXT,
  OperatorId            TEXT    NOT NULL,
  DutVehicleType        INTEGER, DutFirmwareVersion TEXT,
  DutBoardVid INTEGER, DutBoardPid INTEGER, DutBoardProduct TEXT, DutInstancePath TEXT,
  DutHardwareFingerprint TEXT,
  CompatVerdict         TEXT,               -- 'match' | 'mismatch-accepted' | 'blocked'
  CompatDetailsJson     TEXT,               -- per-check result, see §7.4
  Verdict               TEXT CHECK (Verdict IN ('pass','fail','incomplete','aborted','invalidated')),
  VerdictReason         TEXT,
  ChecksEnabledJson     TEXT    NOT NULL,   -- §9.3 - what was actually run
  PendingRebootJson     TEXT,
  ReportPath            TEXT
);

CREATE TABLE RunFinding (
  Id         INTEGER PRIMARY KEY,
  RunId      INTEGER NOT NULL REFERENCES Run(Id) ON DELETE CASCADE,
  SectionId  TEXT,                          -- section id from the comparison profile
  Category   TEXT NOT NULL,                 -- 'param' | 'compass' | 'imu' | 'prearm' | 'link'
  Name       TEXT,                          -- parameter name where applicable
  Outcome    TEXT NOT NULL,                 -- diff outcome, owned by parameter-protocol-and-profiles.md §8.1
  Severity   TEXT NOT NULL CHECK (Severity IN ('critical','warning','informational')),
  BoardValue TEXT, ReferenceValue TEXT, Delta TEXT,   -- InvariantCulture text, display only
  Message    TEXT,                          -- verbatim STATUSTEXT where the finding came from one
  DetectedAtUtc TEXT NOT NULL
);

-- ---------- audit: dotnet-mavlink-and-winui-integration.md §7.8, append-only ----------
CREATE TABLE ParamWriteAudit (
  Id             INTEGER PRIMARY KEY,
  RunId          INTEGER REFERENCES Run(Id) ON DELETE RESTRICT,  -- NULL for writes outside a run
  AtUtc          TEXT    NOT NULL,
  OperatorId     TEXT    NOT NULL,
  Operation      TEXT    NOT NULL,   -- 'diff-apply' | 'cal-transfer' | 'priority-reorder' | 'manual' | 'rollback'
  Name           TEXT    NOT NULL,
  ParamType      INTEGER NOT NULL,
  OldValueBits   INTEGER, NewValueBits INTEGER, ReadBackBits INTEGER,
  Verification   TEXT    NOT NULL,   -- 'verified' | 'coalesced' | 'rejected' | 'unknown' | 'not-attempted'
  RebootRequired INTEGER NOT NULL DEFAULT 0,
  Detail         TEXT               -- rejection reason / STATUSTEXT / timeout that expired
);

CREATE TABLE CalibrationOp (
  Id        INTEGER PRIMARY KEY,
  RunId     INTEGER NOT NULL REFERENCES Run(Id) ON DELETE RESTRICT,
  AtUtc     TEXT NOT NULL,
  OpType    TEXT NOT NULL,   -- 'level-trim' | 'fixed-yaw' | 'onboard-mag' | 'reboot' | 'prearm-check'
  CommandId INTEGER,         -- e.g. 241, 42006, 42424, 246, 401
  ParamsJson TEXT,           -- the param1..param7 actually sent
  MavResult TEXT,            -- ACCEPTED | FAILED | TEMPORARILY_REJECTED | DENIED | UNSUPPORTED | UNKNOWN
  StatusTexts TEXT,          -- reassembled STATUSTEXT lines, verbatim, with severity
  Outcome   TEXT NOT NULL
);

-- ---------- corrections: the ONLY sanctioned change to a stored run, append-only (§7.7.5, §10.6) ----------
CREATE TABLE RunCorrection (
  Id        INTEGER PRIMARY KEY,
  RunId     INTEGER NOT NULL REFERENCES Run(Id) ON DELETE RESTRICT,
  AtUtc     TEXT NOT NULL,
  OperatorId TEXT NOT NULL,
  Field     TEXT NOT NULL,     -- 'UnitRowId' | 'UnitNotes'  - nothing else is correctable
  OldValue  TEXT, NewValue TEXT,
  Reason    TEXT NOT NULL
);

CREATE TABLE AppMeta (Key TEXT PRIMARY KEY, Value TEXT NOT NULL);  -- schema version, install id
```

### 3.3 Indexes that matter

```sql
CREATE INDEX IX_Snapshot_Lineage   ON Snapshot(LineageId, Revision DESC);
CREATE INDEX IX_Snapshot_Hash      ON Snapshot(ContentHash);           -- dedup on capture (§2.3)
CREATE INDEX IX_Snapshot_Kind_Date ON Snapshot(Kind, CapturedAtUtc DESC);
CREATE INDEX IX_Profile_Status     ON Profile(Status, VehicleFamily, Name);
CREATE INDEX IX_Run_Profile        ON Run(ProfileId, StartedAtUtc DESC);
CREATE INDEX IX_Run_Snapshot       ON Run(SnapshotId);                 -- referential-safety check (§6.6)
CREATE INDEX IX_Finding_Run_Sev    ON RunFinding(RunId, Severity, Outcome);
CREATE INDEX IX_Audit_Run_At       ON ParamWriteAudit(RunId, AtUtc);
CREATE INDEX IX_Audit_Name_At      ON ParamWriteAudit(Name, AtUtc DESC); -- "when did this param last change"

-- units and the history view (§7.7, §10.7)
CREATE INDEX IX_Run_Unit_Started   ON Run(UnitRowId, StartedAtUtc DESC);  -- per-unit timeline (§10.4)
CREATE INDEX IX_Run_Started        ON Run(StartedAtUtc DESC, Id DESC);    -- default history sort + keyset paging
CREATE INDEX IX_Run_Verdict_Start  ON Run(Verdict, StartedAtUtc DESC);
CREATE INDEX IX_Run_Operator_Start ON Run(OperatorId, StartedAtUtc DESC);
CREATE INDEX IX_Run_UnitIdAtRun    ON Run(UnitIdAtRun, StartedAtUtc DESC);-- history for a unit id that was later re-assigned
CREATE INDEX IX_Unit_LastSeen      ON Unit(LastSeenUtc DESC);
CREATE INDEX IX_Unit_Status_Id     ON Unit(Status, UnitId);
CREATE INDEX IX_Correction_Run     ON RunCorrection(RunId, AtUtc);
```

`Unit.UnitId` already carries a unique index from its `UNIQUE` constraint; that is the autocomplete and
exact-lookup path (§7.7.3). Prefix search (`UnitId LIKE 'ABC%'`) uses the same index; **substring** search does
not and needs the FTS table of §10.2.

`SnapshotParam` needs no extra index: its `WITHOUT ROWID` primary key `(SnapshotId, Name)` is the lookup path
for the comparison, which walks one snapshot by name.

### 3.4 File location — a store that must outlive the installed application

The deployment model this section is written against, taken from the repository's own `README.md`,
`ARDU_OTK/ARDU_OTK.csproj` and `ARDU_OTK/Services/UpdateService.cs`:

- **Unpackaged** (`WindowsPackageType=None`), self-contained WinUI 3 on `net10.0-windows10.0.26100.0`. There is
  **no MSIX package and no package identity.**
- Installed per-user into `%LOCALAPPDATA%\ARDU_OTK`, with no administrator rights.
- Updates are delivered by Velopack from GitHub Releases. Applying one **replaces the application directory and
  restarts the process** (§3.5).

> 🔴 **Hard requirement. The database lives in a stable per-user data directory that is outside the application
> directory and carries no version number in its path.** Reference snapshots, comparison profiles, units, runs,
> findings and the write audit log are the operator's accumulated work over years. The application directory is
> disposable and is replaced on every update; the store is not, and must never share its lifetime.

Recommended path convention — the only place these strings appear is `IAppPaths`:

| Purpose | Path | Notes |
|---|---|---|
| **Store root** | `Environment.GetFolderPath(SpecialFolder.LocalApplicationData)` + `\ARDU_OTK.Data` | Deliberately a **sibling of** the Velopack install root `%LOCALAPPDATA%\ARDU_OTK`, never a child of it. Per-user, not roamed. |
| Database | `<store root>\ardu_otk.db` | Plus its `-wal` / `-shm` companions while open. |
| Backups | `<store root>\backups\` | §3.6. |
| Development store | `<store root>\dev\ardu_otk.db` | Used only when the app is not installed — §3.4.1. |

Why the obvious alternatives are wrong:

| Candidate location | Why it is wrong here |
|---|---|
| Next to the executable — `AppContext.BaseDirectory`, `Assembly.Location` | It resolves **inside the versioned application folder** that Velopack swaps out (`current\`, historically `app-<version>\`). The update replaces or orphans that folder, so the store is lost or silently reset on the first update — and the failure is silent: the app starts, reports "No runs recorded yet", and years of evidence are simply gone. |
| Any per-version subdirectory (`…\app-1.4.2\data`) | Same loss, dressed as a policy: every version begins with an empty history, and old stores accumulate as orphans that nobody knows to back up. **A version number must never appear in the store path.** |
| Inside the install root `%LOCALAPPDATA%\ARDU_OTK` | Owned by the installer and the updater. Uninstall removes the tree; a repair or a re-install may sweep files it does not recognise. Data would survive an update by luck, not by contract. |
| `ApplicationData.Current.LocalFolder` | Requires package identity, which `WindowsPackageType=None` does not provide — the call throws. **There is no packaged deployment of this app, so there is no packaged branch to write:** no packaging probe, no two-row path table, no dead code. If MSIX is ever adopted, the decision comes back to this section and migrating the existing store becomes part of that work. |
| `Program Files` or any machine-wide location | Needs administrator rights, which the installer deliberately does not take. |
| Roaming `%APPDATA%`, a OneDrive-synced folder, or a roaming user profile | A sync engine or a profile copy will move a live SQLite file and its WAL out from under the app. Corruption, or a silently stale store. |

Rules:

1. Resolve the path through **one `IAppPaths` service**, once at startup. Nothing else in the app composes a
   store path, and nothing derives one from `AppContext.BaseDirectory`, `Assembly.Location` or
   `Process.MainModule`.
2. Create the store root on first run. A missing database file is a **first run**, not an error: create it at
   the current schema version (§3.7.8) and continue.
3. Show the resolved path in the About / diagnostics surface with an "open containing folder" action. An
   operator must be able to find, copy and hand over the file without a developer.
4. Support a `--store <path>` override for a bench that keeps the store on a shared drive, with two stated
   constraints: WAL requires shared memory and does not work over SMB/UNC, so a network path must fall back to
   `journal_mode = DELETE` or be refused with a plain message; and the single-instance guard of §3.5.3 is
   per-machine, so a shared store is supported for **one workstation at a time**, not for concurrent benches.
5. Record in `AppMeta`: the install id, the schema version, the app version that last opened the store
   (`LastAppVersion`, §3.7.1) and the store path at last open. That is what lets §3.5.3 and §3.7.3 tell an
   update apart from a rollback.
6. **The store is not removed by uninstall and must not be.** Nothing on the platform backs it up either —
   there is no MSIX-managed per-package store, `%LOCALAPPDATA%` is excluded from roaming and is not covered by
   OneDrive Known Folder Move. Every copy that exists is one the app or the operator made, so
   **backup and export are not optional** (§3.6).

#### 3.4.1 Development runs — a `NotInstalled` app must not touch the production store

`UpdateService` sets `State = UpdateState.NotInstalled` when `UpdateManager.IsInstalled` is false — the app was
launched from a build output rather than from an installed copy. `CurrentVersion` is `null` in that case,
because Velopack reads the version from installation metadata, not from assembly attributes.

> 🔴 **When the app is not installed, `IAppPaths` resolves the store to the separate development store
> (`<store root>\dev\ardu_otk.db`), never to the production database.**

1. The decision comes from `UpdateManager.IsInstalled` (surfaced as `UpdateState.NotInstalled`), **not** from a
   build symbol: a Release build run from `bin\` is still not installed, and a Debug build can be installed.
2. A development run says so where it cannot be missed — window title and About: "Development store — not the
   production database" — and the diagnostics surface shows the resolved path (rule 3 above).
3. Three reasons this is a hard rule, not hygiene:
   - a developer run would otherwise write test runs, test units and audit rows into the operator's evidence,
     and §10.6 offers no way to remove them — the retention purge is the only deletion path and it is
     export-gated and age-based;
   - a development build normally carries a **newer schema**; opening the production store migrates it, after
     which the installed production app refuses to open it (§3.7.3) and the bench is down until a backup is
     restored;
   - the same trap applies to `--store` pointed at a copy taken from a workstation. Inspecting a copy must not
     migrate it, so `--store` on a foreign store offers an explicit **read-only open** (`Mode=ReadOnly`) that
     never migrates and disables every mutating action.
4. An installed copy never opens the development store, whatever is in it.

### 3.5 Update safety — the process can be replaced and restarted mid-run

Velopack's *download* is safe at any time: `CheckAndDownloadAsync` touches only the update cache, never the
running application or the store. **Applying** is not safe at any time: `ApplyAndRestart()` replaces the
application directory and restarts the process. The store layer therefore has to be designed for the process
ending abruptly — and ending at the app's own initiative, not only through a crash.

#### 3.5.1 The store is one of the things that decides "busy"

`UpdateService.ApplyAndRestart()` refuses while `IsBusy()` is true and leaves the update downloaded for the next
opportunity. But the contract is:

```csharp
public Func<bool> IsBusy { get; set; } = static () => false;
```

> 🔴 **`IsBusy` defaults to "the bench is free". If nothing sets it, the app will restart itself in the middle of
> a run.** A started-but-uncommitted run means busy, and the store layer is one of the sources that must say so.

1. The composition root sets `UpdateService.IsBusy` **once, at startup, before the first call to
   `CheckAndDownloadAsync`** (§3.5.4 step 7), to a delegate that ORs every busy source. `IsBusy` is a single
   settable delegate — a second assignment *replaces* the first rather than adding to it, so the sources are
   composed in one place and no subsystem sets the property for itself.
2. The store contributes busy when any of these holds:
   - a `Run` row has been inserted for this session and not yet closed — `EndedAtUtc IS NULL` / `Verdict IS NULL`
     (the whole window from §7.2 step 11 to the verdict, i.e. `RunInProgress` in §7.1);
   - a snapshot capture or file import is in flight (§5), including the not-yet-saved fetch;
   - a schema migration, a backup, a restore, an export or a retention purge is running (§3.6, §3.7, §10.6);
   - any write transaction is open.
   The bench session contributes its own sources — a live link, an unacknowledged compatibility dialog, a
   non-empty pending-reboot set on a connected board. They compose with OR; none of them replaces another.
3. The predicate must be **cheap, non-blocking and non-throwing**: it reads in-memory session state and never
   queries the database or waits on a lock. It is called on the UI path at the moment a restart is decided. If
   it cannot determine the state, it returns **`true`** — failing safe defers an update, which costs nothing,
   whereas failing open costs a run.
4. Deferral is **visible**: while an update is `ReadyToApply` and the bench is busy, the UI states "Update
   `<version>` ready — it will install when the bench is free". It is applied at a natural boundary (the run is
   closed and its report exported, or the operator is idle) or simply at the next launch. Never a countdown
   that restarts anyway.
5. The gate is symmetric: once `ApplyAndRestart()` has returned `true` the process is going down, so arming and
   starting a run are disabled from that moment. A run started in the second before a restart is the same data
   loss by a different route.

#### 3.5.2 Write as you go, never commit a run only at the end

> 🔴 **Nothing about a run may exist only in memory.** A process killed by an update must leave a *recoverable
> partial run*, not nothing.

1. This is §9.2 rule 1, restated for the update case and binding on the store layer: the `Run` row is inserted
   **at start** (§9.1 rule 1); `RunFinding`, `ParamWriteAudit` and `CalibrationOp` rows are committed **as they
   occur**, each in its own short transaction. Never hold one transaction open for the length of a run; never
   accumulate findings in a list to insert at the end. A run interrupted at parameter 900 of 1180 must leave
   900 findings on disk.
2. The verdict is the only field written at completion. `Verdict IS NULL` with `EndedAtUtc IS NULL` is therefore
   the on-disk signature of an interrupted run, and §3.5.3 turns that signature into an explicit verdict rather
   than leaving it to be read as "blank".
3. `journal_mode = WAL` with `synchronous = NORMAL` (§3.1) is sufficient for **process death**: a committed
   transaction survives the process being killed and restarted, which is exactly what applying an update does.
   `NORMAL` trades durability only against an **OS crash or power loss**; that residual risk is accepted for a
   bench tool, and it is not the risk an update introduces.
4. Checkpoint the WAL at natural boundaries — after a run closes, before a backup, before shutdown — so the
   `.db` file someone copies is close to complete. A raw copy taken with an uncheckpointed WAL is missing the
   newest commits (§3.6.2).
5. The board keeps no such guarantee. Parameters already written to the board are **not** undone by the
   restart, which is precisely why `ParamWriteAudit` rows must reach disk before a restart can happen: after
   it, they are the only record that the board was modified at all.

#### 3.5.3 Representing and recovering an interrupted run

A startup sweep runs after migration and the integrity check, and before the UI binds to data (§3.5.4 step 5).

1. The app is **single-instance per store** — a named mutex plus a lock file in the store root. This is what
   makes the sweep safe: at startup, any `Run` with `EndedAtUtc IS NULL` necessarily belongs to a session that
   no longer exists. Without the guard the sweep would close another instance's live run. A second instance
   states plainly that the store is already open and exits.
2. Every such run is closed as `Verdict = 'aborted'` — **never `pass`, never `fail`, never left `NULL`** — with
   `VerdictReason` naming the cause. `EndedAtUtc` is set to the timestamp of the last row actually recorded for
   that run (`MAX` over `RunFinding.DetectedAtUtc`, `ParamWriteAudit.AtUtc`, `CalibrationOp.AtUtc`), falling
   back to `StartedAtUtc`. The end time is the last evidence, not the moment the sweep noticed.
3. Name the cause honestly by comparing `AppMeta.LastAppVersion` (§3.4 rule 5) with the running version:
   different ⇒ "application updated from `<old>` to `<new>` during the run"; same ⇒ "application closed during
   the run". The update case is the one the operator most needs to read, because the app did it to itself.
4. **Findings, writes and calibration operations recorded before the interruption are retained, never deleted** —
   the same rule as an invalidated run (§7.6.3). They are the only record of what was done to the board.
5. **A run is never resumed.** After a restart the link, the board's live state and any pending reboot are gone;
   continuing would attribute post-restart evidence to a pre-restart binding. The operator arms and starts a new
   run; the aborted one stays in the history as evidence.
6. If the aborted run has `ParamWriteAudit` rows, its history row and its detail must say so — "board partially
   written" — together with the pending-reboot set stored at that point (§9.1). That is the fact the next
   operator needs before touching that unit again.
7. `Unit.LastHardwareFingerprint` and `Unit.LastBoard*` are untouched by an aborted run, per §7.7.4 rule 3 —
   only a completed run updates them.

How it must be shown (§10.1, §10.8):

- Icon **plus the word `ABORTED`**, never colour alone (§8.3.1), and visually distinct from `PASS`,
  `PASS (partial scope)`, `FAIL`, `INCOMPLETE` and `IN PROGRESS`.
- **`INCOMPLETE` and `ABORTED` stay distinct.** `incomplete` means the run reached a verdict but its scope or
  results were not complete (`ReadFailed` rows, an unresolved gap, a non-empty pending-reboot set, disabled
  checks — §8.3.2). `aborted` means the run never reached a verdict because the session ended under it.
  Collapsing the two hides which of them happened.
- Duration, finding counts and write counts on an aborted row are labelled **partial** / "recorded up to the
  interruption", never presented as final totals.
- The run stays in the unit's timeline (§10.4) and in `Unit.RunCount`, is exportable (§10.5), and is included
  by the "problems only" verdict preset (§10.2). It is **never silently discarded, never hidden from the
  default list, and never rendered as a completed run.**

#### 3.5.4 Startup order after an update

The first launch after an update is where every rule above meets. The order is fixed:

1. Resolve paths (§3.4) and choose production vs development store from `UpdateManager.IsInstalled` (§3.4.1).
2. Acquire the single-instance guard for that store (§3.5.3.1).
3. Open the store and read `PRAGMA user_version` plus `AppMeta` (schema version, `LastAppVersion`).
   **Refuse a store written by a newer schema or a newer app version before touching it in any way** (§3.7.3).
4. Automatic backup (§3.6.1), then migrate (§3.7), then the integrity check (§3.7.6).
5. Sweep interrupted runs to `aborted` (§3.5.3).
6. Write the new `LastAppVersion` and schema version to `AppMeta` — only after 4 and 5 have both succeeded.
7. Wire `UpdateService.IsBusy` (§3.5.1). This happens **before** the first `CheckAndDownloadAsync`, not after.
8. Bind the UI, then start the background update check.

Steps 3–6 report busy, so an update that is already downloaded cannot restart the process while the store is
being backed up or migrated. A failure in 3, 4 or 5 goes to the recovery screen of §3.7.6, never to the bench
view with a half-opened store.

### 3.6 Backup, export and portability

The store now outlives many application versions and is backed up by nothing but this app and the operator
(§3.4 rule 6). Backup is therefore a feature of the product, not a maintenance detail.

1. **Automatic backup before every schema migration** (§3.7) and before any destructive maintenance — retention
   purge (§10.6), bulk import, restore. Copy to
   `<store root>\backups\ardu_otk-<appVersion>-<schemaVersion>-<utcStamp>.db`. The **app version is in the file
   name** because a store can only be opened by that version or a newer one (§3.7.3): the name states what can
   read it. Keep the last N (default 10), never auto-delete the most recent, and never delete the newest backup
   that predates the current schema version — that is the only file a downgrade can fall back to.
2. **Manual "Back up now"** in the UI, producing a single consistent file via SQLite's backup API or
   `VACUUM INTO 'file'` — not a raw file copy, which with WAL active yields a file missing the newest committed
   transactions (§3.5.2 rule 4).
3. **Backups live in the store root, never in the application directory.** A backup written beside the
   executable is destroyed by the next update — precisely the moment it is most likely to be needed (§3.4).
4. **Rolling backup on a schedule, not only on migration.** Migrations may be months apart while runs accumulate
   daily, so take a backup on the first launch of each day (or every N closed runs), under the same retention.
   A store holding a year of evidence whose most recent backup dates from the last schema change is not
   backed up.
5. **Restore is an explicit, confirmed action.** It refuses a backup whose schema or app version is newer than
   the running app (the same rule as §3.7.3), takes a safety copy of the current store first, restores while the
   store is closed, and re-runs the integrity check (§3.7.6) afterwards. It replaces; it never merges.
6. **Portability between workstations — two mechanisms with different guarantees, and the UI must not blur them:**
   - the **whole store**: the single checkpointed file produced by "Back up now", carrying profiles, units,
     runs, findings and the audit log. Copy *that* file, never the live `.db` alongside its `-wal`/`-shm`. It is
     readable only by the same app version or a newer one (§3.7.3), so it moves a bench's history forward in
     time, not backward;
   - **profile bundles** (§6.7) and **report / history exports** (§9.4, §10.5): version-independent JSON and CSV
     that a different app version — and other tooling — can read. This is the form that survives in both
     directions and across the years the store is expected to live.
   Moving one reference to another bench is a `.otkprofile` job; moving a whole bench is a backup-file job;
   handing evidence to someone without the app is an export job.
7. **Profile export/import** as a self-contained bundle (§6.7) for moving one reference between workstations.
8. **Run report export** per run (§9.4) and filtered-set export from the history (§10.5), independent of the
   database.
9. The audit log must additionally be exportable on its own to a file the operator keeps
   (`dotnet-mavlink-and-winui-integration.md` §7.8).
10. Backups and exports are written **before** the operation they protect, never after, and are **verified**
    — re-opened and integrity-checked — before that operation starts. An unverified backup is not a backup.
11. Every backup and export records the app version, the schema version, the UTC timestamp and the store's
    install id, so a file found on a shared drive years later can be placed and read back.

### 3.7 Schema migration requirements

An update installs a new binary against an existing database file, so the very first thing the new version does
is meet a store it did not create. This section is that contract.

1. Store the schema version in `PRAGMA user_version` (and mirror it in `AppMeta` for human inspection), and the
   app version that last opened the store in `AppMeta.LastAppVersion`, written per §3.5.4 step 6.
2. Migrations are **ordered, forward-only, idempotent**, each in its own transaction, applied on startup before
   any UI binds to data — and while `IsBusy` reports true (§3.5.1), so a downloaded update cannot restart the
   process in the middle of a migration.
3. > 🔴 **Refuse to open a store whose schema version, or whose `LastAppVersion`, is NEWER than the running
   > app.** Say so plainly, naming both versions and the store path, and offer exactly two ways out: install the
   > newer app version, or restore a backup taken before it (§3.6.5). Never "best-effort" a downgrade, never
   > drop columns or tables it does not recognise, never migrate backwards. This is not hypothetical for this
   > deployment: workstations update themselves independently, a development build is routinely ahead of the
   > installed one (§3.4.1), and a store copied from another bench can be ahead of this one.
4. Migrations may add tables, columns and indexes. They may **not** rewrite `SnapshotParam` values,
   `ParamWriteAudit` rows or `RunFinding` rows — nor the other append-only tables of §10.6 — because those are
   evidence. If a migration would change their meaning, add a new column and leave the old data intact.
5. Every migration runs after the automatic backup of §3.6.1, and logs start/finish/row counts.
6. Ship a startup integrity check: `PRAGMA quick_check` plus a foreign-key check. A failure blocks the app into
   a recovery screen offering "restore from backup" (§3.6.5) — it never silently continues. The recovery screen
   names the store path (§3.4) so the operator can secure a copy of the file before anything else touches it.
7. **A failed migration leaves the store at its last good version.** Roll back that migration's transaction,
   stop the chain, do not attempt later migrations, keep the backup, and go to the recovery screen naming the
   migration that failed and the path of the backup taken in §3.6.1. A half-migrated store is worse than an
   unmigrated one.
8. A missing store is created **directly at the current schema version** and stamped, not built by replaying
   every historical migration — with a test asserting that a freshly created store and a fully migrated one are
   structurally identical.
9. Migration finishes before the bench view appears. If it takes more than about a second, show a determinate
   progress surface naming the step: the operator has just been restarted by an update (§3.5) and is entitled to
   know why the app is not ready yet.

---

## 4. Immutability and revisions

### 4.1 The rule

> A captured snapshot is **never edited in place**. Any change to its parameter content produces a **new
> revision** with its own `ContentHash`. Existing runs keep pointing at the revision they actually used.

The same applies to comparison-profile revisions and to calibration blocks. Only profile metadata and the
profile's *pointers* are editable (§1.2).

### 4.2 Numbering and display

- Revisions are **1-based integers, monotonic within a `LineageId`**, allocated as `MAX(Revision) + 1` inside
  the same transaction that inserts the new revision. Never renumbered, never reused, gaps impossible.
- Display form: `"<Profile name> · snapshot r<N>"`, e.g. `OTK Copter bench · snapshot r4`. The comparison
  profile carries its own revision: `comparison r2`. Both appear in the top-left panel (§8) and in the report.
- Alongside the revision, always show the **first 8 hex characters of the content hash** and the capture date.
  Two revisions with the same number in two different exported bundles are only the same artefact if the hash
  matches.
- A re-capture whose hash equals the current revision's hash **creates nothing** — the UI reports "identical to
  r4; no new revision created". This is the normal outcome of re-verifying an unchanged golden board.

### 4.3 Creating a new revision

1. Capture or import as in §5. Compute `ContentHash`.
2. If the hash matches any existing revision in the lineage, stop and report which one (§4.2).
3. Otherwise insert a new `Snapshot` row with `Revision = MAX + 1`, same `LineageId`.
4. Set `SupersededById` on the previous revision to the new row.
5. Do **not** touch any `Profile` row yet — repointing is a separate, explicit step (§4.4).

### 4.4 A profile whose snapshot is superseded

`Profile.SnapshotId` keeps pointing at the revision it was bound to. Nothing changes automatically. Instead:

1. The profile list and the top-left panel show a **"newer revision available (r5)"** affordance on that
   profile — an `InfoBar` with an action, not a silent auto-upgrade.
2. Choosing the action opens a **revision diff**: what changed between the bound revision and the new one,
   grouped by the comparison profile's sections, so the operator sees what accepting would change about future
   verdicts.
3. Accepting repoints `Profile.SnapshotId`, bumps `UpdatedAtUtc` / `UpdatedBy`, and writes an audit entry naming
   the old and new revision and hash.
4. **Runs are untouched.** Every past run keeps its own `SnapshotId`, `SnapshotHashAtRun` and
   `ComparisonHashAtRun`, so its report remains reproducible and its verdict remains meaningful.
5. Repointing is **blocked while a run is in progress** on that profile. Superseding mid-run is exactly the
   silent re-scoring §7.6 forbids.
6. A superseded revision is never deleted while any run or profile references it (§6.6). It may be hidden from
   the picker behind a "show superseded" toggle.

---

## 5. Creating a reference

### 5.1 Procedure A — capture from a live known-good board

Preconditions: the golden board is the **only** board connected; the app is not armed to any existing profile;
the operator has confirmed in the UI that this board is the reference, not the device under test.

1. **Connect** using the handshake in `connection-and-telemetry.md` §2. Do not proceed until a `HEARTBEAT` has
   arrived and the system/component ids are latched.
2. **Refuse a non-ArduPilot source.** If `autopilot != 3` (`MAV_AUTOPILOT_ARDUPILOTMEGA`), abort the capture
   with an explicit message. A PX4 or unknown stack must never become an ArduPilot reference.
3. **Resolve provenance before the fetch**: `HEARTBEAT.type` and family, firmware version via the one-shot
   `AUTOPILOT_VERSION` request, and the USB board identity from the transport layer. Capturing provenance first
   means a fetch that fails halfway still tells you what it was talking to.
4. **Full parameter fetch** exactly as `parameter-protocol-and-profiles.md` §1.2 specifies (or the MAVFTP
   `@PARAM/param.pck` fast path of §1.4). Record `param_count` from the first `PARAM_VALUE` as
   `ParamCountReported`.
5. **Gap check.** Allocate the seen-vector from the reported count, mark received indices, individually
   re-request missing ones by index on timeout, and repeat until the set is closed or the retry budget is spent.
6. > 🔴 **A fetch with unresolved gaps must never be saved as a reference.** If `GapCount > 0`, the Save button
   > stays disabled. The UI names the missing count and offers only "retry the missing parameters" or "discard".
   > A snapshot with holes silently becomes `MissingOnBoard` noise on every future run and can hide a real
   > difference. Storing it as `Kind = 'RunPre'` for diagnostics is acceptable; storing it as `'Reference'` is not.
7. **Build the hardware fingerprint** from the captured `COMPASS_DEV_ID` / `COMPASS_DEV_ID2` /
   `COMPASS_DEV_ID3` and `INS_ACC_ID` / `INS_ACC2_ID` / `INS_ACC3_ID` values.
8. **Compute `ContentHash`** (§2.3) and check for an identical existing revision (§4.2).
9. **Name and save** in one transaction: insert the `Snapshot` (`Kind = 'Reference'`, `Source = 'LiveBoard'`,
   `SourceFormat = 'Mavlink'` or `'MavFtpParamPck'`, `TypesInferred = 0`), then either create a new profile or
   add the revision to an existing lineage.
10. **Optionally define the compass-calibration block** now, from this snapshot, per §1.1 and
    `compass-calibration-transfer.md`. The block resolves member names at creation time and is hashed with the
    profile revision.
11. **Bind a comparison profile**: pick an existing revision or start from the shipped default. A reference
    profile is not usable until both a snapshot and a comparison profile are bound.

Illustrative insert of the parameter rows — one transaction, one prepared command:

```csharp
using var tx = conn.BeginTransaction();
using var cmd = conn.CreateCommand();
cmd.CommandText = """
    INSERT INTO SnapshotParam (SnapshotId, Name, ParamType, ValueBits, ValueReal, ValueInt, ParamIndex)
    VALUES ($s, $n, $t, $b, $r, $i, $x);
    """;
// ... AddWithValue placeholders created once, reassigned per row ...
foreach (var p in captured)
{
    pName.Value  = p.Name.ToUpperInvariant();
    pType.Value  = (int)p.ParamType;                                   // MAV_PARAM_TYPE as reported
    pBits.Value  = (long)(uint)BitConverter.SingleToUInt32Bits(p.Value);
    pReal.Value  = (double)p.Value;
    pInt.Value   = p.IsInteger ? (object)(int)MathF.Round(p.Value) : DBNull.Value;
    cmd.ExecuteNonQuery();
}
tx.Commit();
```

### 5.2 Procedure B — import from a parameter file

1. **Select the file.** Accept `.param`, `.parm` and `.params`.
2. **Detect the format explicitly and parse** — both are owned by `parameter-protocol-and-profiles.md` §5.
   Do not reimplement, and do not rely on the extension: Mission Planner's loose splitter silently mis-parses a
   QGroundControl file, so detection is a hard requirement. Record the detected format as `SourceFormat`.
3. **Types.**
   - QGC `.params` supplies a numeric `MAV_PARAM_TYPE` in column 5 → store it, `TypesInferred = 0`.
   - Mission Planner `.param`/`.parm` has **no type column** → set `TypesInferred = 1` and resolve each type at
     comparison time from the connected board's `PARAM_VALUE.param_type` (or from `apm.pdef` metadata for the
     resolved firmware version). Store the parsed token's value as `REAL32` bits, parsed with
     `float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture)` so it lands in the same binary32 lattice
     the board's value lives in.
4. **Harvest whatever provenance the file offers.** QGC writes `#` header lines (`# Stack:`, `# Vehicle:`,
   `# Version:`, `# Git Revision:`) — parse them into `FirmwareVersion` / `VehicleFamily` and mark them
   *file-declared*, not *observed*. Mission Planner files carry none unless someone added `#` comments.
5. **Record what is unavoidably missing.** An imported snapshot has **no** board identity (VID/PID/product/
   instance path), **no** hardware fingerprint (unless the file happens to contain `COMPASS_DEV_ID*` /
   `INS_ACC*_ID` rows, in which case they are file-declared values, not detected ones), **no** observed
   `HEARTBEAT.type`, **no** `param_count` to gap-check against, and **no** proof the file was ever read off a
   healthy board. `ParamCountReported` is null and `GapCount` is meaningless — a file cannot be checked for
   completeness.
6. **Mark reduced trust in the UI, permanently and visibly.**
   - The profile card, the top-left panel and every exported report carry an **"Imported — reduced provenance"**
     badge: icon **plus** text, never colour alone.
   - The badge tooltip/expander lists precisely which provenance fields are absent, and for a Mission Planner
     import states that parameter types are inferred.
   - **Compass-calibration transfer is disabled for a profile whose snapshot has no hardware fingerprint.** The
     identical-hardware gate in `compass-calibration-transfer.md` §2.3 cannot be evaluated without detected
     `COMPASS_DEV_ID*` values, and an ungated transfer produces a board that reads back correctly and still
     fails `PreArm: Compass not calibrated`.
   - The board-identity compatibility check (§7.4) reports **"unknown"**, never "match".
7. **Save** exactly as §5.1 steps 8–11.

> Upgrade path: once the same reference is captured live, create it as a **new revision in the same lineage**.
> The profile then gains full provenance and the reduced-trust badge disappears for runs bound to that revision
> — while old runs keep their honest "imported" label.

---

## 6. Managing multiple profiles

There are several references, and the operator switches between them per board family, per customer build, per
production batch. The profile manager must support all of the following.

| # | Operation | Behaviour and constraints |
|---|---|---|
| 6.1 | **Create** | From a live capture (§5.1) or a file import (§5.2). Name is unique and required. A profile is not selectable until it has both a snapshot revision and a comparison-profile revision. |
| 6.2 | **Clone** | Copies profile metadata and **pointers** — the cloned profile shares the same immutable snapshot and comparison revisions, it does not duplicate the parameter rows. Name gets a `" (copy)"` suffix that the operator must change. `IsDefault` is never cloned. |
| 6.3 | **Rename** | Metadata only; does not invalidate anything. Past runs display `ProfileNameAtRun`, so historical reports keep the name that was in force. |
| 6.4 | **Edit the comparison profile** | Opens the JSON block of `parameter-protocol-and-profiles.md` §6 in an editor with schema validation. **Saving creates a new `ComparisonProfile` revision** and repoints the profile at it; the previous revision stays for existing runs. Reject unknown `profileVersion` majors. Blocked while a run using this profile is in progress. |
| 6.5 | **Set default** | Exactly one profile may be `IsDefault = 1` (enforced by the partial unique index in §3.3). The default is pre-selected at startup but **still requires an explicit operator confirmation to arm the session** (§7.2) — a default is a convenience, never an automatic arm. A retired profile cannot be the default. |
| 6.6 | **Mark active / retired** | `retired` hides the profile from the default picker (available behind "show retired"), and **blocks arming a new run with it** unless the operator explicitly confirms. Retiring never affects past runs. Retire is the correct alternative to delete in almost every case. |
| 6.7 | **Search and filter** | Free-text over name, description, tags, notes and board product string; filters for vehicle family, status, source (`LiveBoard` / `ImportedFile`), firmware version, "has calibration block", "has newer revision", and last-used date. Sort by name, last used, or capture date. The picker in the top-left panel uses the same query surface. |
| 6.8 | **Delete with referential safety** | See below. |
| 6.9 | **Export / import a whole profile** | See below. |

### 6.6 Referential safety on delete

`Profile → Snapshot`, `Profile → ComparisonProfile`, `Profile → CalBlock`, `Run → Unit`, `Run → *`,
`ParamWriteAudit → Run`, `CalibrationOp → Run` and `RunCorrection → Run` are all declared `ON DELETE RESTRICT`.
That is the enforcement, not a UI convention.

> **Must not be deletable:** any profile, snapshot revision, comparison-profile revision, calibration block
> **or unit** that is referenced by **at least one `Run`**. Deleting it would orphan an audit trail and make a
> past verdict unexplainable.

Behaviour:

1. Attempting to delete such a profile shows how many runs reference it, the date of the most recent one, and
   offers **Retire** instead. The delete button is disabled, not silently failing.
2. A profile with **zero** runs may be deleted. Deleting it removes the `Profile` row only; snapshot and
   comparison revisions survive if any other profile or run points at them, and are otherwise offered for
   cleanup as a separate, explicitly confirmed maintenance action.
3. `SnapshotParam` cascades from its `Snapshot`, so a snapshot that is legitimately deletable takes its rows
   with it.
4. **`ParamWriteAudit`, `RunFinding`, `CalibrationOp` and `RunCorrection` are never deletable from the UI at
   all.** Retention is a maintenance operation on whole runs older than a configured age, gated by an
   export-first step (§10.6).
5. **A `Unit` with at least one run is never deletable — retire it.** A unit with zero runs (created by a typo
   that was caught before the run started) may be deleted, and the UI offers that specifically as "remove
   mistyped unit". Merging a mistyped unit into the correct one is a `RunCorrection` operation (§7.7.5), never
   a delete.

### 6.7 Profile export / import bundle

For moving a reference between bench workstations:

- Format: a single `.otkprofile` file — a zip containing `profile.json` (metadata + pointers),
  `snapshot.json` (provenance + all parameter rows with `ParamType` and `ValueBits`),
  `comparison.json` (verbatim, §6 of the parameter reference), optional `calblock.json`, and a `manifest.json`
  with the app version, schema version and all content hashes.
- **On import, recompute every hash and compare with the manifest.** A mismatch aborts the import.
- Import is **additive and never overwrites**: an incoming snapshot whose hash already exists is reused; an
  incoming lineage that already exists gains a new revision; a name collision prompts for a new name.
- The bundle records the exporting workstation and operator, and the import records the importing ones — a
  reference that travelled must say so in its provenance.
- Also offer a plain Mission Planner `.param` export of the snapshot for interoperability, with `#` provenance
  comment lines — but state in the UI that this form **loses the types and all provenance** and is therefore a
  one-way convenience, not a backup.

---

## 7. The pre-connection selection workflow

> **Stated requirement: the operator selects the reference BEFORE connecting the board under test.** The app
> enforces this; it is not a suggestion in the manual.
>
> **Second stated requirement: the operator enters the ID of the board under test.** A run is never recorded
> without one (§7.7). The unit ID is entered *with* the reference selection, and both together arm the session.

### 7.1 Session states

| State | Meaning | Connect | Fetch / compare | Writes, calibration, reboot |
|---|---|---|---|---|
| `NoReference` | Nothing selected | **Disabled** | Disabled | Disabled |
| `AwaitingUnitId` | A profile is selected and confirmed; **no valid unit ID yet** | **Disabled** | Disabled | Disabled |
| `Armed` | Profile confirmed **and** unit ID valid; not connected | Enabled | Disabled | Disabled |
| `Connected` | Link up, handshake complete, compatibility evaluated | — | Enabled | Gated by the compatibility verdict and by the safety rules of `dotnet-mavlink-and-winui-integration.md` §7 |
| `RunInProgress` | A run is executing | — | Enabled | Enabled per verdict; profile and unit switching blocked |
| `RunComplete` | Verdict produced | — | Re-run allowed | Enabled |

> **The missing unit ID is a hard block, not a warning.** It gates `Connect` exactly as a missing reference
> does, and it uses the same mechanism — a disabled command with the reason on it. The rationale is that a run
> which cannot be attributed to a unit cannot be found again in the history, cannot be compared with the unit's
> previous run, and therefore fails the reason the history exists. Making it a soft warning guarantees a
> proportion of unattributable runs, which is worse than a two-second prompt at the bench.

### 7.2 Procedure

1. App starts in `NoReference`. The connect command and **every** write, calibration and reboot command are
   disabled. Disabled controls carry the reason — "Select a reference profile first" — not a bare grey button.
2. The operator picks a profile in the top-left panel (§8). The default profile may be pre-highlighted but
   selection is still an explicit act.
3. The panel shows the selection's full provenance for review **before** arming: profile name, snapshot
   revision + hash prefix + capture date, source (live / imported, with the reduced-trust badge if applicable),
   vehicle family, firmware version, parameter count, comparison-profile name + revision, and whether a
   calibration block is attached.
4. The operator confirms the reference → state becomes `AwaitingUnitId`. Connect is still disabled, and its
   reason text changes to "Enter the ID of the board under test".
5. **The operator enters the unit ID** in the same panel, directly beneath the reference selection (§7.7). The
   field validates and normalises as it is typed, autocompletes against previously seen units, and shows the
   matched unit's last run date and verdict as soon as it resolves to a known unit. Free-text run notes sit
   beside it and are optional.
6. On a valid unit ID → state becomes `Armed`. Only now does the connect command enable.
7. The operator connects the board under test. Run the handshake in `connection-and-telemetry.md` §2 — nothing
   is claimed connected before the first `HEARTBEAT` and the identity fields it yields.
8. **Compatibility evaluation** (§7.4) runs immediately after the handshake and before any fetch. Its verdict
   is rendered in the panel and drives what is enabled. This is also where the unit's hardware identity is
   compared with its history (§7.4 check 12).
9. On a hard block: the link stays open (so the operator can read the mismatch and the vehicle's own
   `STATUSTEXT`), but every mutating action stays disabled and the panel states the blocking check by name.
10. On a confirm-to-proceed mismatch: mutating actions stay disabled until the operator acknowledges a
    `ContentDialog` that names the specific mismatch, its consequence, and what will still be checked. The
    acknowledgement is recorded in `Run.CompatDetailsJson` and appears in the report. It is never remembered
    across sessions and never pre-checked.
11. On a full match, or after acknowledgement, the run starts and `Run` is inserted with its frozen bindings
    (`UnitRowId`, `UnitIdAtRun`, `UnitIdRawAtRun`, `SnapshotId`, `ComparisonProfileId`, hashes,
    `ChecksEnabledJson`) in one transaction that also upserts `Unit` and bumps `LastSeenUtc` / `RunCount`.
    **The unit ID becomes immutable at this point** (§7.7.5).

### 7.3 What "armed" must not do

- Arming must **not** trigger a connection, a port scan write, or any command to a board.
- Arming must **not** be inferred from "the last profile used". Restoring the last selection into the picker is
  fine; auto-arming it is not.
- An expired or invalid arm (store integrity failure, missing snapshot, hash mismatch) drops back to
  `NoReference` with the cause named.
- **The unit ID must never be pre-filled from the previous run.** Autocomplete *as the operator types* is
  required; carrying the last unit forward silently is forbidden — it is the single most likely way to attribute
  a run to the wrong board. The field starts empty on every arm.

### 7.4 Compatibility checks

Evaluated once at connect, and re-evaluated after any reboot (parameters and identity must be re-read anyway).
Each produces a per-check result stored in `Run.CompatDetailsJson`.

| # | Check | Reference side | Board side | Verdict on mismatch |
|---|---|---|---|---|
| 1 | **Autopilot stack** | `Snapshot.AutopilotId` (always 3 for a valid reference) | `HEARTBEAT.autopilot` | **Hard block** if `autopilot != 3`. ArduPilot-specific decoding is invalid otherwise; every configuration panel stays disabled. |
| 2 | **Vehicle family** | `Snapshot.VehicleFamily` | family resolved from `HEARTBEAT.type` | **Hard block** on a family change (Copter reference vs Plane board, or either side "unknown"). The parameter sets are different products. |
| 3 | **Vehicle type within family** | `Snapshot.VehicleType` (raw `MAV_TYPE`) | `HEARTBEAT.type` | **Confirm to proceed** (e.g. `QUADROTOR` reference vs `HEXAROTOR` board). Frame-dependent parameters will differ legitimately. |
| 4 | **Firmware version — patch** | `Snapshot.FirmwareVersion` | resolved at handshake | **Informational note.** Same major.minor; expect no renames. |
| 5 | **Firmware version — minor or major** | as above | as above | **Confirm to proceed.** Parameters get renamed across releases (`COMPASS_TYPEMASK` → `COMPASS_DISBLMSK`; `ARMING_CHECK` → `ARMING_SKIPCHK`, whose polarity is also inverted). The dialog must warn that a rename shows as one `MissingOnBoard` + one `NotInReference` pair. Exact rename releases are **unverified — verify against target firmware**; the app probes for both spellings rather than assuming. |
| 6 | **Firmware version unknown on either side** | — | — | **Confirm to proceed**, and the metadata is marked "approximate — not version-matched" as `parameter-protocol-and-profiles.md` §4.3 requires. |
| 7 | **Board identity** (VID/PID + manufacturer + product string) | `Snapshot.Board*` | transport layer of the live link | **Informational note** when equal; **confirm to proceed** when different; **"unknown"** when the reference was imported from a file. Never render "unknown" as "match". |
| 8 | **Hardware fingerprint** (`COMPASS_DEV_ID*`, `INS_ACC*_ID`) | `Snapshot.HardwareFingerprint` | read from the board after the fetch | **Informational** for the parameter comparison. **Hard block on the calibration-transfer action specifically** when the detected `COMPASS_DEV_IDx` set differs — the copied calibration will read back correctly and still produce `PreArm: Compass not calibrated`. Blocks the action, not the run. |
| 9 | **Parameter-set shape** | `ParamCountCaptured` and the name set | `param_count` from the first `PARAM_VALUE`, then the fetched name set | Count difference alone is **informational** — `param_count` is unstable across a session and shifts when an `AP_PARAM_FLAG_ENABLE` parameter changes. The meaningful signal is **name-set overlap**: below a configured threshold (default 90 %), **confirm to proceed** and state the overlap percentage and the largest missing groups. |
| 10 | **Snapshot integrity** | recomputed `ContentHash` vs stored | — | **Hard block.** The store was modified outside the app. |
| 11 | **Reduced-provenance reference** | `Source = 'ImportedFile'` | — | **Informational**, always visible, and it disables calibration transfer (§5.2 step 6). |
| 12 | **Unit ↔ hardware continuity** | `Unit.LastHardwareFingerprint`, `Unit.LastBoardVid/Pid/Product` from this unit's previous run | the live board's fingerprint and USB identity | `first-run` ⇒ **informational** ("first run for this unit"). `match` ⇒ **informational**. `changed` ⇒ **confirm to proceed** (§7.7.4) — the board inside this unit is not the board this unit last carried. `unknown` (either side absent) ⇒ **informational**, rendered as unknown, never as match. Result stored in `Run.UnitHwMatch`. |

Aggregate verdict for the panel: `match` (no mismatch above informational), `mismatch-accepted` (at least one
confirm-to-proceed acknowledged), `blocked` (any hard block). Store it in `Run.CompatVerdict`.

### 7.5 Reboot within a session

A reboot (`MAV_CMD_PREFLIGHT_REBOOT_SHUTDOWN`) invalidates every cached parameter and can change the COM
number. The session stays armed to the same profile, the run continues, and the app re-runs the handshake,
re-issues stream intervals, re-fetches parameters and re-evaluates §7.4. Handling of the link itself belongs to
`connection-and-telemetry.md` and `imu-level-and-health-verification.md`.

### 7.6 Switching the reference mid-session

> 🔴 **Switching the selected reference while a run is in progress invalidates the current run's results. The
> app never silently re-scores an existing diff against a different reference.**

1. The profile picker is **disabled** during `RunInProgress`; switching requires an explicit "change reference"
   command.
2. That command opens a confirmation that states plainly: the current run will be closed with
   `Verdict = 'invalidated'`, its findings so far are kept as evidence, and a new run must be started.
3. On confirmation: set `Run.EndedAtUtc`, `Verdict = 'invalidated'`, `VerdictReason = 'reference changed mid-run'`.
   **Findings and audit rows are retained, never deleted** — parameters may already have been written to the
   board, and that history must survive.
4. The panel returns to `Armed` with the new profile. A fresh run starts only after the compatibility
   evaluation runs again against the new reference.
5. The same rule applies to editing the bound comparison profile or repointing the snapshot revision (§4.4,
   §6.4) — both are blocked during a run for exactly this reason.
6. **Changing the unit ID mid-run is treated identically.** A run already carries writes and findings attributed
   to a unit; re-attributing them silently would corrupt that unit's history and the other unit's. Close the run
   as `invalidated` and start a new one.

### 7.7 Unit identification — the ID of the board under test

#### 7.7.1 Where it is entered

In the large top-left panel, immediately beneath the reference selection, as part of arming — never in a modal
that appears after connect, and never at save time. Entering it before connect is what makes the run
attributable even if the board fails to enumerate, refuses to connect or is disconnected mid-fetch: the run row
already exists with its unit.

#### 7.7.2 Input rules, validation and normalisation

| Rule | Specification |
|---|---|
| Required | Yes. Empty or whitespace-only ⇒ `AwaitingUnitId`, connect disabled (§7.1). |
| Length | 1–64 characters after trimming. Reject longer with the actual limit in the message. |
| Character set | Letters, digits, and `-` `_` `/` `.` `space`. Reject control characters and anything that would break CSV/report export. Configurable per organisation via a settings-level regex, with the default stated in the UI. |
| **Normalisation** (`Unit.UnitId`, `Run.UnitIdAtRun`) | 1. Trim leading/trailing whitespace. 2. Collapse internal whitespace runs to a single space. 3. Upper-case with `ToUpperInvariant()` — **invariant, never the current culture** (a Turkish-locale `ToUpper` maps `i` to `İ` and silently splits `UAV-i7` into two units). 4. Normalise Unicode to NFC. |
| **Raw form preserved** | `Run.UnitIdRawAtRun` stores exactly what was typed, and `Unit.UnitIdDisplay` stores the form used the first time the unit was created. Display the display form; **match, index and join on the normalised form.** |
| Uniqueness | `Unit.UnitId` is `UNIQUE` on the normalised value. `uav-07`, `UAV-07 ` and `UAV-07` are the same unit — which is the whole point of normalising, because otherwise history lookups quietly return a subset. |
| Near-miss warning | On a *new* unit whose normalised value is within a small edit distance of an existing one (default Levenshtein ≤ 1, or differing only by `-`/`_`/`.`/space), warn inline: "Similar to `UAV-07` (14 runs, last 2026-07-30). Create new unit, or use `UAV-07`?" This is a **warning, not a block** — genuinely similar serials exist. |
| Free-text notes | `Run.UnitNotes`, optional, multi-line, entered next to the ID and editable until the run starts. It is about *this run of this unit* (jig position, fault reported, rework done). Unit-level description belongs on the `Unit` row (§6 style operations), not here. |

#### 7.7.3 Autocomplete

1. Suggest from `Unit` on prefix of the **normalised** input, ordered by `LastSeenUtc DESC`, capped at ~10.
   `Unit.UnitId`'s unique index serves this directly; substring search falls back to §10.2's FTS table.
2. Each suggestion shows the display form, run count, last run date and last verdict — enough for the operator
   to recognise the right unit without leaving the field.
3. Selecting a suggestion binds to that existing `Unit` row. Typing a value that matches nothing shows an
   explicit **"New unit — will be created"** affordance, so creating a unit is always a visible, deliberate act
   and never the silent result of a typo.
4. Retired units (§6.6.5) are excluded from suggestions by default and reachable behind "include retired".

#### 7.7.4 When the unit disagrees with its history

The hardware identity is read at connect; the unit's previous hardware identity comes from
`Unit.LastHardwareFingerprint` and `Unit.LastBoard*`. This is §7.4 check 12, and its `changed` result is
**surfaced, never silently overwritten**:

1. Classify into `first-run` / `match` / `changed` / `unknown` and store it in `Run.UnitHwMatch`.
2. On `changed`, raise a `ContentDialog` before the run starts that states plainly: *this unit last ran with
   board `<product, VID:PID>` and compass IDs `<…>`; the connected board is `<…>`. The board in this unit
   appears to have been replaced.* Offer three outcomes:
   - **Yes, the board was replaced** — proceed; the run records `UnitHwMatch = 'changed'` and the acknowledgement
     in `Run.CompatDetailsJson`; `Unit.Last*` is updated **from this run** on completion.
   - **No, I typed the wrong unit** — cancel; return to `AwaitingUnitId` with the field focused. Nothing is
     written, no `Unit` row is created or touched.
   - **Cancel the run** — return to `Armed` without connecting further.
3. `Unit.LastHardwareFingerprint` and `Unit.LastBoard*` are **only** updated by a completed run, and only after
   the acknowledgement above. A cancelled or aborted run never mutates the unit's identity history.
4. The full per-run hardware identity always stays on the `Run` row, so the unit's board-swap history is
   reconstructable from `Run` alone even if `Unit.Last*` is later recomputed — the denormalised columns are a
   cache, not the record.
5. The `changed` flag is visible in the history list and in the per-unit timeline (§10.4) as a distinct marker,
   because "this airframe got a new flight controller on this date" is exactly the fact a bench log exists to
   preserve.

#### 7.7.5 Correcting a mis-entered unit after the run started

`Run.UnitIdAtRun` and `Run.UnitIdRawAtRun` are **immutable** — they record what was asserted at the time. The
only sanctioned correction re-points `Run.UnitRowId`:

1. Available from the history drill-down (§10.3), gated on a confirmation that names both units and requires a
   typed reason.
2. It writes a `RunCorrection` row (`Field = 'UnitRowId'`, old and new value, operator, reason, timestamp) and
   updates `RunCount` / `LastSeenUtc` on **both** units.
3. The run's report and history row afterwards show the corrected unit **and** a "re-assigned from `<old>`"
   marker with the reason. The original assertion is never hidden.
4. `Run.UnitNotes` is correctable by the same mechanism. **Nothing else on a stored run is correctable** —
   findings, writes, calibration operations and verdicts are evidence (§10.6).

---

## 8. The large top-left panel

### 8.1 Role

**This panel is the app's primary surface.** It occupies the large top-left region of the shell and is the only
place the operator needs to look to answer: *which reference am I using, is the board compatible, and did it
pass?* Everything else — compass panel, telemetry readouts, parameter diff, log — is supporting detail arranged
around it.

### 8.2 What it shows, by stage

| Stage | Must show |
|---|---|
| Always | Selected reference profile name; snapshot revision + hash prefix + capture date; source badge (live / imported); comparison-profile name + revision; vehicle family; reference firmware version; parameter count; whether a calibration block is attached. **Plus the unit ID of the board under test**, in its display form, prominent enough to read from arm's length. |
| No reference | A single, unmistakable call to action: pick a reference. The profile picker with search/filter (§6.7). Nothing else competes for attention. |
| Awaiting unit ID | The confirmed reference block, plus the unit-ID field focused, its autocomplete list, the "New unit — will be created" affordance, and the optional run-notes box. Connect is disabled with the reason "Enter the ID of the board under test". Once the ID resolves to a known unit, show that unit's run count, last run date and last verdict, with a one-click jump to its history (§10.4). |
| Armed, not connected | The full provenance block above, the unit ID, plus the primary **Connect** action and the explicit statement that writes are disabled until connected and matched. |
| Connected | Board-under-test identity (board product string, firmware version, vehicle type) directly beneath the reference identity, **paired for visual comparison**, plus the aggregate compatibility verdict and a expandable per-check list (§7.4). The unit ID sits with the board identity, with the unit ↔ hardware continuity result (§7.4 check 12) rendered beside it. |
| Run in progress | The current phase in domain terms ("Fetching parameters 412 / 1180"), a determinate `ProgressBar` where a count exists, an always-enabled Cancel, and the running counts of critical / warning / informational findings. |
| Run complete | The overall verdict, the reason, and a **per-section breakdown** using the comparison profile's own section labels — each section with its counts per outcome and its own pass/fail state. Plus the pending-reboot set if non-empty, and the export-report action. |

Primary actions live in the panel's own `CommandBar`: **Select reference / Change reference**, **Connect /
Disconnect**, **Start run**, **Apply selected differences**, **Export report**. Secondary actions
(calibration, reboot) live in their own panels but their availability is mirrored here as enabled/disabled with
a stated reason.

### 8.3 Visual states

Each state must be identifiable **at a glance, from across a bench**, and each must be distinguishable without
relying on colour.

| State | What it must make obvious | Rendering |
|---|---|---|
| **No reference selected** | That nothing can happen yet, and what to do about it | Large picker occupying the panel; `InfoBar` `Informational`: "Select a reference profile before connecting the board." Connect and all write commands disabled with tooltips naming the reason. |
| **Awaiting unit ID** | That the reference is settled and one field stands between the operator and connecting | Reference block collapsed to a compact summary; the unit-ID `AutoSuggestBox` is the visual focus and holds keyboard focus; `InfoBar` `Informational`: "Enter the ID of the board under test." Connect disabled with the same reason. A near-miss warning (§7.7.2) renders inline beneath the field, not as a dialog. |
| **Armed, not connected** | Which reference is armed, **which unit is about to be tested**, and that the board is not yet connected | Provenance block plus unit ID prominent; a neutral "Not connected" status line with an em dash for every board-side field; `Connect` is the accent-styled primary button. |
| **Unit hardware changed** | That this unit is not carrying the board it last carried | `InfoBar` `Warning` + icon + text "Board changed for this unit"; previous and current board identity shown side by side; run blocked until acknowledged (§7.7.4). Never colour alone; the words "board changed" must be present. |
| **Connected and matched** | That the board is the right kind of board and the run may proceed | `InfoBar` `Success` + check icon + text "Compatible"; board identity rendered beside the reference identity; `Start run` becomes the primary action. |
| **Connected with mismatch** | Exactly **which** check failed and whether it is a block or an acknowledgeable difference | `InfoBar` `Warning` (confirm-to-proceed) or `Error` (hard block), each with an icon **and** the check name in text; the per-check list expanded by default; blocked actions disabled with the blocking check named. |
| **Run in progress** | That the app is working, on what, how far, and that it can be cancelled | Determinate `ProgressBar` with "Fetching parameters N / M"; indeterminate `ProgressRing` only where no count exists (e.g. level calibration); Cancel always responsive; live finding counts. |
| **Run complete — pass** | That it passed, and **what was actually checked** | `InfoBar` `Success` + check icon + "PASS" text; the section breakdown; and immediately adjacent, the enabled-checks summary of §9.3 — a pass is never shown without it. |
| **Run complete — fail** | The failing sections, the top critical findings, and the next action | `InfoBar` `Error` + error icon + "FAIL" text + the reason sentence; the failing sections sorted first; a direct jump to the diff filtered to critical `Differs` / `MissingOnBoard` / `ReadFailed`. |

Additional required behaviours:

1. **A verdict is never rendered by colour alone.** Every verdict carries an icon **and** a word — `PASS`,
   `FAIL`, `INCOMPLETE`, `BLOCKED`. This is both the accessibility requirement and insurance against a theme
   the author did not test.
2. **`INCOMPLETE` is a first-class verdict**, visually distinct from both pass and fail. A run with
   `ReadFailed` rows, an unresolved gap, a non-empty pending-reboot set, or disabled checks is not a pass.
3. Stale board-side values follow `dotnet-mavlink-and-winui-integration.md` §8.6 — em dash and disabled
   foreground, never a frozen plausible number.
4. The reference-side provenance block is **static within a session**; it must not animate, blink or reflow
   when telemetry updates, or the operator stops trusting it.

### 8.4 Shell layout and responsive behaviour

Shell: `NavigationView` per `dotnet-mavlink-and-winui-integration.md` §8.1. The content root is a `Grid` with
star-sized columns and rows; the panel is a single stock `Border`/`Expander`-free card built from standard
surfaces.

| Width | Layout |
|---|---|
| **≥ 1400 epx** | Three columns. **Column 1 (widest, top row, ~2 rows tall): the reference/run panel.** Column 2: compass panel above telemetry readouts. Column 3: the parameter diff (`DataGrid`), full height. `STATUSTEXT` log docked along the bottom, collapsible. |
| **1000–1400 epx** | Two columns. Panel top-left spanning the left column and the full first row; compass + telemetry below it in the same column; diff occupies the right column full height. |
| **< 1000 epx** | Single column, stacked, **panel first**. The panel's verdict header (`PASS`/`FAIL`/state + profile name + revision) becomes a sticky compact header so the verdict is never scrolled out of sight. Compass, telemetry and diff follow in that order. |

1. Drive the breakpoints with `VisualStateManager` + `AdaptiveTrigger` on the page, not with code-behind size
   handlers.
2. The panel never shrinks below the point where the profile name, revision and verdict are all legible — those
   three collapse last. Provenance detail collapses into an `Expander` before they do.
3. Use only stock WinUI 3 surfaces: `InfoBar`, `ContentDialog`, `CommandBar`, `ProgressBar`/`ProgressRing`,
   `ListView`/`DataGrid`, `Expander`, `TextBlock`, `InfoBadge`. No custom chrome, no bespoke controls.
4. **Theme-aware resources only.** `{ThemeResource ...}` against `SystemFillColorSuccessBrush`,
   `SystemFillColorCautionBrush`, `SystemFillColorCriticalBrush`, `TextFillColorPrimaryBrush`,
   `TextFillColorSecondaryBrush`, `TextFillColorDisabledBrush`, `LayerFillColorDefaultBrush`. **No literal hex,
   no `Colors.Red`.** Verify light and dark explicitly, including a runtime theme switch.
5. Numeric readouts use fixed-width formatting so digits do not jitter at stream rate.
6. **The work history is a sibling `NavigationView` destination, not part of this panel** (§10.8). The panel
   owns the *current* run; the history owns *past* runs. The only history content allowed inside the top-left
   panel is the compact "this unit: N runs, last <date>, last verdict <X>" line with a link into the history —
   it must never grow into a list that competes with the live verdict.

---

## 9. Run records

### 9.1 What a completed run stores

| Group | Stored | Table |
|---|---|---|
| **Unit under test** | `UnitRowId` (FK), plus frozen `UnitIdAtRun` (normalised) and `UnitIdRawAtRun` (as typed), `UnitNotes`, and `UnitHwMatch` (`first-run` / `match` / `changed` / `unknown`) | `Run` → `Unit` |
| Binding | `ProfileId`, `SnapshotId` (**the exact revision**), `ComparisonProfileId` (**the exact revision**), `CalBlockId`, plus frozen `ProfileNameAtRun`, `SnapshotHashAtRun`, `ComparisonHashAtRun` | `Run` |
| Board under test (auto-detected) | vehicle type, firmware version, board VID/PID/product, device instance path, hardware fingerprint | `Run` |
| Timing and people | `StartedAtUtc`, `EndedAtUtc`, `OperatorId` | `Run` |
| Compatibility | aggregate verdict + per-check results, including every confirm-to-proceed acknowledgement and who made it | `Run.CompatDetailsJson` |
| Findings | every finding with section, parameter name, outcome, severity, board value, reference value, delta, and the verbatim `STATUSTEXT` where one was the source | `RunFinding` |
| Writes | every write attempt: timestamp, operator, causing operation, name, type, old / new / read-back value, verification outcome (`verified` / `coalesced` / `rejected` / `unknown` / `not-attempted`), reboot-required flag, and the rejection reason or timeout detail | `ParamWriteAudit` |
| Calibration | every calibration, prearm-check and reboot command: which command id, the `param1..param7` actually sent, the `MAV_RESULT`, the reassembled `STATUSTEXT` lines with severity, and the outcome | `CalibrationOp` |
| Pending reboot | the set of written parameters whose effect awaits a reboot | `Run.PendingRebootJson` |
| Verdict | `pass` / `fail` / `incomplete` / `aborted` / `invalidated`, plus a one-sentence reason | `Run` |
| Scope | **which checks were actually enabled** | `Run.ChecksEnabledJson` |

Rules:

1. The run is inserted **at start**, not at completion, so an aborted or crashed session still leaves evidence —
   and because the unit is known before connect (§7.7.1), even a run that never reaches the board is attributed.
2. `ParamWriteAudit` rows are appended **as each write completes**, never batched at the end. The log survives
   disconnects and reboots because it is already on disk.
3. Values in `RunFinding` are formatted `InvariantCulture` text for display only — the numeric comparison
   already happened per `parameter-protocol-and-profiles.md` §7.
4. Nothing in these tables is ever rewritten. A correction is a new run, except for the two fields §7.7.5
   permits, which are re-pointed through an appended `RunCorrection` row rather than an in-place edit.

### 9.2 Everything the work produces is persisted — coverage map

> **Requirement, stated plainly: the results of the work and of the checks are written to the database, keyed to
> the unit and to the run.** Nothing that the operator saw on screen during a run may exist only on screen.

| Result of the work | Persisted as | Table (already defined in §3.2) |
|---|---|---|
| Parameter comparison — every in-block outcome, including `Match` counts, not just the differences | one `RunFinding` row per reported parameter, with `Outcome` and `Severity` from `parameter-protocol-and-profiles.md` §8.1 | `RunFinding` — **already covered, no new table** |
| Per-section breakdown shown in the panel | derived from `RunFinding.SectionId` + `Severity` + `Outcome`; the section labels come from the bound `ComparisonProfile.Json` revision | `RunFinding` + `ComparisonProfile` |
| Every parameter write, with old value, new value, read-back and verification outcome | one `ParamWriteAudit` row per **attempt**, appended as it completes, including `not-attempted` items of a partial batch | `ParamWriteAudit` — **already covered** |
| Compass topology findings (priority, use flags, external classification, per-instance identity) | `RunFinding` with `Category = 'compass'`; the writes they cause appear in `ParamWriteAudit` with `Operation = 'priority-reorder'` | `RunFinding`, `ParamWriteAudit` |
| Compass calibration transfer | the copied values as `ParamWriteAudit` rows (`Operation = 'cal-transfer'`); the operation itself, its gate decision and its outcome as a `CalibrationOp` row | `ParamWriteAudit`, `CalibrationOp` |
| Every calibration command and its outcome — level trim, fixed-yaw, onboard mag, reboot | one `CalibrationOp` row with the command id, the `param1..param7` actually sent, the `MAV_RESULT`, the reassembled `STATUSTEXT` lines verbatim with severity, and the outcome | `CalibrationOp` — **already covered** |
| Prearm verification verdict | a `CalibrationOp` row for the `MAV_CMD_RUN_PREARM_CHECKS` invocation with its `MAV_RESULT` and collected `STATUSTEXT`, **plus** one `RunFinding` per distinct `PreArm:` message with `Category = 'prearm'` and its severity, so the verdict is queryable and not buried in a text blob | `CalibrationOp` + `RunFinding` |
| Health-bit evidence backing the verdict | `RunFinding` rows with `Category = 'imu'` / `'compass'` carrying the evaluated result; the raw `STATUSTEXT` stays in `CalibrationOp.StatusTexts` | `RunFinding`, `CalibrationOp` |
| Pending-reboot set at the end of the run | `Run.PendingRebootJson` | `Run` |
| Scope of the run | `Run.ChecksEnabledJson` (§9.3) | `Run` |
| Final verdict and its reason | `Run.Verdict`, `Run.VerdictReason` | `Run` |
| Link/session events that changed the outcome (disconnect, reboot, cancellation) | `RunFinding` with `Category = 'link'`, plus a `CalibrationOp` row for the reboot command itself | `RunFinding`, `CalibrationOp` |

Rules:

1. **Write as you go, not at the end.** Findings, writes and calibration operations are committed as they occur.
   A crash, a link loss, a pulled USB cable **or an update that restarts the process** (§3.5.2) must leave the
   run partially recorded, not empty. The verdict is the only field written at completion; an unfinished run
   keeps `Verdict = NULL` until §10.8's "in progress" rendering or the startup sweep of §3.5.3 closes it as
   `aborted`.
2. **Matches are stored, not only differences.** A history that records only failures cannot prove that a
   parameter was checked and was correct — which is the question an audit asks. Storing `Match` rows is what
   makes `ChecksEnabledJson` verifiable rather than a claim.
3. **`STATUSTEXT` is stored verbatim**, after multi-chunk reassembly, alongside any interpreted verdict. The
   interpretation may be wrong; the vehicle's own words are evidence.
4. Every row above is reachable from `UnitId` by one join through `Run`, which is what makes §10.4's per-unit
   history possible.

### 9.3 `ChecksEnabledJson` — a clean result must not be mistaken for a complete one

> 🔴 **A run must record which checks were actually enabled.** Without it, a green `PASS` from a run with the
> compass section excluded is indistinguishable from a green `PASS` from a full run.

Minimum content:

- The comparison profile's section ids **and which of them were active**, plus the effective
  `unmatchedOnBoard`, `missingInReference` and `useBuiltInExclusions` settings and the count of parameters
  excluded by each source.
- Whether the parameter comparison, the compass topology check, the compass-calibration transfer, the IMU level
  verification and the prearm verification each ran, were skipped, or were blocked — and why.
- The vehicle-side check mask actually in force: the value of `ARMING_CHECK` (4.5.x/4.6.x, inclusion bitmask)
  **or** `ARMING_SKIPCHK` (master, inverted polarity), whichever the board exposes. Probe for both; do not
  assume the polarity. The exact release of the rename is **unverified — verify against target firmware.** A
  board with checks disabled can produce a clean prearm result that proves nothing, and the run report must say
  so.
- Whether parameter metadata (`apm.pdef`) was version-matched, approximate, or unavailable — because
  `@ReadOnly` / `@Volatile` / `@RebootRequired` classification depends on it.
- Any retry budget that was exhausted, and any parameter left in `ReadFailed`.

The top-left panel renders a one-line summary of this next to the verdict ("Full profile, 6 of 6 sections,
metadata version-matched" / "**4 of 6 sections — compass and IMU skipped**"), and the exported report repeats
it in the header. A pass whose scope was reduced is displayed as **`PASS (partial scope)`**, never as a bare
pass.

### 9.4 Exportable per-run report (required)

Every run must be exportable, standalone, without the database.

1. **Header** — run id, start/end UTC and local, operator, profile name at run, snapshot revision + hash +
   capture date + source, comparison-profile name + revision + hash, calibration block name if any, board under
   test identity and firmware, **the unit ID (display form, plus the raw entered form and any re-assignment
   marker of §7.7.5) and the unit ↔ hardware continuity result**, compatibility verdict with every check and
   every acknowledgement, and the `ChecksEnabledJson` summary of §9.3.
2. **Findings** — one row per finding: `Section, Name, Type, BoardValue, ReferenceValue, Delta, Severity,
   Outcome, Note`. The diff-report content requirements of `parameter-protocol-and-profiles.md` §8.3 apply
   verbatim; this section is that report plus the run binding.
3. **Writes** — the full `ParamWriteAudit` slice for this run, in time order, with verification outcomes.
4. **Calibration and reboots** — the `CalibrationOp` slice, with the verbatim `STATUSTEXT` lines.
5. **Summary** — counts per outcome and per severity, pending-reboot set, the final verdict and its reason.
6. **Formats** — CSV and JSON for records; optionally a Mission Planner `.param` file containing only the
   write-eligible `Differs` rows, comma-separated, `InvariantCulture`, with `#` provenance comment lines.
7. The report path is stored in `Run.ReportPath` so the record can be found again from the run history.

---

## 10. Work history

Everything §9 persists exists so it can be looked at again. The history is the second major surface of the app,
after the top-left panel.

### 10.1 The history list

Default landing view of the History destination. One row per `Run`.

| Column | Source | Notes |
|---|---|---|
| **Unit ID** | `Unit.UnitIdDisplay`, with `Run.UnitIdRawAtRun` in the tooltip | First column — it is the primary thing an operator searches by. A re-assigned run (§7.7.5) shows a marker. |
| Date / time | `Run.StartedAtUtc` | Rendered **local**, stored UTC. Show the date group header (Today / Yesterday / date) for scanability. |
| Duration | `EndedAtUtc − StartedAtUtc` | Em dash while the run is unfinished. |
| Verdict | `Run.Verdict` + `VerdictReason` tooltip | Icon **and** word: `PASS`, `PASS (partial scope)`, `FAIL`, `INCOMPLETE`, `ABORTED`, `INVALIDATED`, `IN PROGRESS`. Never colour alone (§8.3.1). |
| Counts by severity | aggregated `RunFinding` | Three numbers — critical / warning / informational — as `InfoBadge`s. Aggregate at write time into a small denormalised trio on `Run` if profiling shows the join hurts; the source of truth stays `RunFinding`. |
| Reference profile + revision | `Run.ProfileNameAtRun` + snapshot `r<N>` + comparison `r<M>` | **The names frozen at run time**, not the profile's current name. |
| Board identity | `Run.DutBoardProduct`, VID:PID | With the `UnitHwMatch` marker when `changed`. |
| Operator | `Run.OperatorId` | |
| Writes | count of `ParamWriteAudit` rows for the run | Zero-write runs (pure verification) must be visually distinct from runs that modified a board. |
| Scope | one-line `ChecksEnabledJson` summary | "6/6 sections" or "**4/6 — compass, IMU skipped**". |

Rules:

1. **Default sort: `StartedAtUtc DESC`** — most recent first. Secondary sort on `Id DESC` so equal timestamps
   are deterministic.
2. **Paging is keyset, not `OFFSET`.** A store that accumulates runs for years makes `LIMIT n OFFSET m` degrade
   linearly. Page with
   `WHERE (StartedAtUtc, Id) < (@lastStarted, @lastId) ORDER BY StartedAtUtc DESC, Id DESC LIMIT 50`,
   which rides `IX_Run_Started` directly. Load further pages on scroll (incremental loading), never all rows.
3. The list is **virtualised** (`ListView`/`DataGrid` with data virtualisation). Never materialise the whole
   table into a view-model collection.
4. Aggregates for the visible page are fetched in one grouped query keyed by the page's run ids, not N+1 per
   row.

### 10.2 Filtering and search

| Filter | Behaviour |
|---|---|
| **Unit ID** | Exact match on the normalised form (index `IX_Run_UnitIdAtRun` / `Unit.UnitId`), plus prefix match. Picking a unit from autocomplete switches to the per-unit view (§10.4). |
| **Date range** | From / to on `StartedAtUtc`, plus presets (today, last 7 days, last 30 days, this year). Rides `IX_Run_Started`. Compare on the stored UTC ISO-8601 text — lexicographic ordering of that format equals chronological ordering. |
| **Operator** | Multi-select from distinct `Run.OperatorId`; `IX_Run_Operator_Start`. |
| **Reference profile / revision** | Profile picker, optionally narrowed to one snapshot revision; `IX_Run_Profile`. Filtering by *revision* is what answers "did anything change after we moved to r5". |
| **Verdict** | Multi-select; `IX_Run_Verdict_Start`. Default includes everything; a "problems only" preset selects `fail` + `incomplete` + `aborted`. |
| **Board changed** | Boolean on `Run.UnitHwMatch = 'changed'`. |
| **Has writes** | Runs that modified a board (`EXISTS` on `ParamWriteAudit`). |
| **Free text** | Across unit id (display and raw), unit description, run notes, board product string, firmware version, verdict reason, and finding parameter names. |

Free-text implementation: back it with an SQLite **FTS5** virtual table over the searchable projection of each
run, kept in sync by triggers or by the same transaction that writes the run. `LIKE '%term%'` over `Run` does
not use any index and becomes a full scan as history grows — acceptable for a first release, but the FTS table
is the correct answer and the schema must leave room for it. Rebuildable from the base tables at any time, so
it is a cache, never evidence.

All filters compose (AND), the active filter set is visible as removable chips, and the result count is always
shown next to it — "142 runs" versus "0 of 3 812 runs" is the difference between a working filter and a
confusing empty screen.

### 10.3 Drill-down: one historical run

Selecting a run opens the run detail, which is the exported report (§9.4) rendered live, in tabs:

1. **Summary** — unit id and notes, board identity and continuity result, reference profile + snapshot revision
   + hash + comparison revision + hash, operator, timings, compatibility checks with every acknowledgement,
   `ChecksEnabledJson` scope summary, final verdict and reason.
2. **Findings** — the `RunFinding` rows, grouped by section, filterable by severity and outcome, with `Match`
   rows collapsed by default and a count so the operator can see they exist.
3. **Writes** — the `ParamWriteAudit` slice in time order: parameter, type, old, new, read-back, verification
   outcome, reboot-required, and the failure detail.
4. **Calibration** — the `CalibrationOp` slice with command ids, sent parameters, `MAV_RESULT` and the verbatim
   reassembled `STATUSTEXT` lines with severities.
5. **Corrections** — the `RunCorrection` rows, if any.

> 🔴 **A historical run is always rendered against the profile revision it actually used — never re-scored
> against the current revision.**

1. Resolve section labels, severities and tolerances from the `ComparisonProfile` row identified by
   `Run.ComparisonProfileId`, and the reference values from `Run.SnapshotId`. Both are immutable (§4), so this
   always resolves.
2. Verify `Run.SnapshotHashAtRun` and `Run.ComparisonHashAtRun` against the stored rows on open. A mismatch
   means the store was altered outside the app: show the detail with a prominent integrity warning and mark the
   rendering as unverified.
3. `RunFinding` rows are the record; the detail view **displays** them, it does not recompute them. There is no
   code path in the history view that runs the comparison algorithm.
4. If the profile has since moved to a newer revision, show an informational note — "this run used snapshot r3;
   the profile now uses r5" — with a link to the revision diff (§4.4.2). Informational only; it never changes
   the displayed verdict.

### 10.4 Per-unit history

Reachable from any unit id in the list, from the autocomplete in the arm panel (§7.7.3), and from a unit
directory.

1. **Timeline** — all runs for one `UnitId`, newest first, on `IX_Run_Unit_Started`. Header shows the unit's
   display id, description, first/last seen, total runs, and the verdict tally.
2. **Board-change markers** — each run whose `UnitHwMatch = 'changed'` renders as a divider in the timeline
   ("flight controller replaced — `CubeOrange` → `Pixhawk6C`, 2026-06-14"), because the timeline's most valuable
   fact is often *when the hardware under the label changed*.
3. **Reference drift** — the timeline shows which profile revision each run used, so a change in outcome that
   coincides with a reference revision change is visible rather than mysterious.
4. **Compare two runs of the same unit.** Select any two runs → a three-column view:
   - **Findings**: parameter name, run A outcome/value, run B outcome/value, restricted by default to rows whose
     outcome or value differs between the two runs.
   - **Writes**: what was written in each run.
   - **Context banner**: if the two runs used **different profile or snapshot revisions**, say so prominently
     and state that some differences may come from the reference having changed, not from the board. Comparing
     across revisions is allowed and useful; presenting it as a pure board comparison is not.
   - The comparison is computed over stored `RunFinding` rows only. It never re-reads a board and never
     re-scores against a current revision.

### 10.5 Export from the history

1. **Per-run** — the full report of §9.4, from the drill-down, CSV or JSON, with the optional
   `.param` of write-eligible differences.
2. **Filtered-set export** — export exactly the rows the current filter produced. Two levels:
   - *Summary export* — one row per run with every column of §10.1 plus run id, unit raw id, snapshot and
     comparison hashes, and the scope summary. CSV for spreadsheets, JSON for tooling.
   - *Full export* — a zip containing the summary file plus one per-run report per included run, and a
     `manifest.json` naming the app version, schema version, export timestamp, exporting operator, the
     **filter criteria used**, and the run count.
3. **For an export to be auditable it must carry, for every run:** unit id (display and raw), board identity and
   hardware fingerprint, operator, start/end timestamps in UTC **with** the local offset used for display,
   reference profile name at run + snapshot revision + snapshot hash + comparison revision + comparison hash,
   the scope summary (`ChecksEnabledJson`), the verdict and its reason, counts per outcome and severity, and any
   `RunCorrection` history. **An export without the hashes and the scope summary is not auditable** — the reader
   cannot tell what was compared or whether the reference has changed since.
4. Exports never include the database file itself; backups (§3.6) are the mechanism for that, and they are
   distinct operations with distinct wording in the UI.
5. Every export records its own filter criteria. A set of "all passing runs" that does not say it was filtered
   to passing runs is a misleading document.

### 10.6 Retention and integrity

> **From the operator's point of view the history is append-only.** The normal UI offers no way to delete or
> edit a run, a finding, a write-audit row or a calibration operation. There is no "edit" affordance to hunt for.

| Operation | Who | Rules |
|---|---|---|
| Edit a finding, a write-audit row, a calibration op, a verdict | **Nobody, ever** | Not exposed, and blocked at the data layer. A wrong result is superseded by a new run, never rewritten. |
| Re-assign a run's unit, or fix its notes | Operator, from the drill-down | Only via `RunCorrection` (§7.7.5): appended, reasoned, attributed, and always displayed alongside the original value. |
| Delete a single run | **Not offered** | Deleting one run breaks the unit's timeline and the audit chain. Retirement and filtering solve the real need ("hide the botched run"), so add a `Run` visibility flag if the need is genuine — a flag that hides from the default list but never from an export or a count. |
| Bulk retention purge of runs older than N | A maintenance action, gated | 1. Requires an explicit confirmation naming the cut-off date and the exact run count. 2. **Requires a successful full export of everything about to be deleted, written and verified first** (§10.5 full export) — the purge refuses to start otherwise. 3. Runs in one transaction. 4. Deletes whole runs with their `RunFinding` (cascade) and, explicitly, their `ParamWriteAudit` / `CalibrationOp` / `RunCorrection` rows, which are `ON DELETE RESTRICT` and so must be removed deliberately in the same transaction — the restriction exists to make "delete a run" impossible by accident, not impossible by policy. 5. Never deletes `Unit`, `Profile`, `Snapshot` or `ComparisonProfile` rows. 6. Logged with cut-off, counts and the export path. |
| Delete a `Unit` | Only when it has zero runs | §6.6.5. Otherwise retire. |

Interaction with §6.6: the `ON DELETE RESTRICT` chain means an ordinary profile or unit delete can never silently
take history with it. The retention purge is the **only** code path that removes run evidence, and it is
export-gated. Nothing else in the app issues a `DELETE` against `Run`, `RunFinding`, `ParamWriteAudit`,
`CalibrationOp` or `RunCorrection`.

Integrity: the startup check of §3.7.6 covers the history tables. Additionally, opening a run detail re-verifies
the two stored hashes (§10.3.2), so tampering surfaces at the point where someone would rely on the record.

### 10.7 Query performance

The indexes this view needs are already in §3.3: `IX_Run_Started` (default sort + keyset paging),
`IX_Run_Unit_Started` (per-unit timeline), `IX_Run_Verdict_Start`, `IX_Run_Operator_Start`, `IX_Run_Profile`,
`IX_Run_UnitIdAtRun`, `IX_Unit_LastSeen`, `IX_Finding_Run_Sev` (per-run severity counts), `IX_Audit_Run_At` and
`IX_Correction_Run`.

Additional rules:

1. **Never `SELECT *` the run list.** Project only the list columns; the detail query loads the rest.
2. Severity counts for a page come from one grouped query over `RunFinding` filtered by the page's run ids
   (`IX_Finding_Run_Sev`), not one query per row.
3. If profiling shows the count join dominates, denormalise `CriticalCount` / `WarningCount` / `InfoCount` onto
   `Run`, written in the same transaction that closes the run. Treat them as a cache: rebuildable from
   `RunFinding`, and rebuilt by a maintenance action, never hand-edited.
4. Run `ANALYZE` after the retention purge and after any bulk import so the query planner keeps choosing the
   indexes above.
5. All history queries run off the UI thread and are cancellable — an operator typing in the search box must be
   able to cancel the in-flight query, per `dotnet-mavlink-and-winui-integration.md` §3.

### 10.8 Where the history lives, and its visual states

**Placement.** The history is its own `NavigationView` destination, a **sibling of the bench view**, not a
region of the large top-left panel. The top-left panel owns the *current* run and must never be crowded by past
ones (§8.4.6). Inside the History destination the layout inverts: the run list occupies the large left region
and the selected run's detail fills the right, with the same responsive collapse to a single stacked column
below ~1000 epx (list first, detail as a pushed page). The only history that appears on the bench view is the
one-line "this unit: N runs, last `<date>`, last verdict `<X>`" summary next to the unit-ID field, which links
into this destination.

| State | What it must make obvious | Rendering |
|---|---|---|
| **Empty history** (no runs at all) | That the store is new and not that something failed | Centred message "No runs recorded yet" plus the action that creates one — "Go to the bench view and select a reference". Never an empty grid with headers and no explanation. |
| **Filtered to nothing** | That the *filter*, not the data, is the reason the list is empty — **visually distinct from empty history** | "No runs match the current filter" plus the active filter chips restated and a one-click **Clear filters**; the unfiltered total ("3 812 runs total") is shown so the operator can see data exists. |
| **Run in progress** | That this row is live and its verdict is not yet decided | The current run appears at the top with an indeterminate `ProgressRing` and the word `IN PROGRESS`; verdict, duration and counts render as em dashes or as live-updating counts explicitly labelled "so far". **Never render an unfinished run as a pass, a fail, or a blank verdict** — an in-flight run with zero findings so far must not look like a clean result. The row is not selectable for comparison (§10.4.4) or export until it closes. |
| **Aborted / crashed / update-interrupted run recovered at startup** (§3.5.3) | That the app, not the operator, ended this run — and whether an **update** ended it | Verdict `ABORTED` with the reason the startup sweep recorded: "application updated from `<old>` to `<new>` during the run" or "application closed during the run"; findings and writes recorded up to that point remain and are labelled partial, with "board partially written" where `ParamWriteAudit` rows exist. Distinct from `INCOMPLETE`, never rendered as a pass or a fail, never hidden. |
| **Loading a further page** | That more history exists and is arriving | Inline progress at the bottom of the list; never a modal, never a spinner that replaces already-loaded rows. |
| **Integrity warning** | That a stored run no longer matches its hashes | `InfoBar` `Error` on the run detail with icon and text; the detail still renders, explicitly marked unverified (§10.3.2). |

The same theming rules apply as in §8.4: stock WinUI 3 surfaces, `{ThemeResource}` brushes, light and dark both
verified, and **no verdict rendered by colour alone** — icon plus word, everywhere, including in the dense
history rows.
