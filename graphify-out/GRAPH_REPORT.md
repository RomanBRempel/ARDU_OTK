# Graph Report - ARDU_OTK  (2026-08-13)

## Corpus Check
- 92 files · ~180,697 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 1972 nodes · 4464 edges · 128 communities (111 shown, 17 thin omitted)
- Extraction: 92% EXTRACTED · 7% INFERRED · 0% AMBIGUOUS · INFERRED: 330 edges (avg confidence: 0.81)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `2764a916`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- SerialVehicleLink
- SerialCompassCalibrationJob
- AcceptanceChecks
- MainPage
- CompassIdentity
- SqliteCalibrationStore
- Page
- MavlinkProtocol.cs
- Agent Skills Index
- Page
- ReferenceEditorPage
- Page
- ParameterRoleMap
- CompassCalibrationPage
- Abstractions.cs
- UpdateService
- Connect Handshake (HEARTBEAT, autopilot==3 gate, AUTOPILOT_VERSION)
- Write then verify procedure (PARAM_SET + independent read by name)
- RoutedEventArgs
- AcceptancePage
- MotorCompToggle
- CalibrationStageRow
- AppServices
- Transfer State Machine (states 0-14)
- Button
- Capability Map (requirement to owning reference)
- .Log
- Run (one verification session against one board under test)
- Deployment: unpackaged, self-contained, Velopack
- Controls, Layout, and Adaptive UI
- Telemetry Data Model (attitude, voltage, current, mode, sentinels)
- CommunityToolkit Controls and Helpers
- Startup Failure Debugging Path
- Classify (external/internal decision procedure)
- AcceptanceSession
- WinUI Reference Sections Index
- Styling, Theming, Materials, and Icons
- Navy + Green Brand Palette (shared app color identity)
- Task
- WinUI WinGet DSC Bootstrap Configuration
- ARDU_OTK.csproj
- ARDU OTK App Icon Mark
- Procedure: make external compass primary and set use flags (Phases A-F)
- graph_freshness.py
- The Performance Imperative
- WinUI App Structure
- UpdateService.IsBusy update interlock
- .OnFormFieldChanged
- TelemetrySession
- MAV_CMD_FIXED_MAG_CAL_YAW (42006)
- MavlinkFtp.cs
- Window
- ARDU OTK Square 44x44 App Tile Icon (scale-200)
- LockScreenLogo.scale-200 (app lock screen logo asset)
- Square44x44Logo targetsize-24 altform-unplated (app icon asset)
- Green circular checkmark badge overlay (bottom-right corner)
- SerialPortCatalog
- .RunPrearmChecksAsync
- .OnFormFieldChanged
- .Subscribe
- Square44x44Logo targetsize-48 altform-lightunplated (app tile icon 48px)
- ARDU OTK Wide Tile Logo (310x150 @200%)
- ReferenceRows.cs
- ARDU_OTK.Services.Fc
- Establishing the Creative Foundation
- .OnPortSelectionChanged
- .Dispatch
- .OnNewRunClick
- .ConnectAsync
- Corrupted Binary Asset (UTF-8 Mojibake Re-encoding)
- Telemetry coalescing pattern (latest-value slot + display timer)
- StackPanel
- Windows App SDK Lifecycle, Notifications, and Deployment
- Regex
- .ReadAllAsync
- WinUI Required Flow (task classification pipeline)
- UpdateService
- SerialPortDescription
- Accessibility, Input, and Localization
- Performance, Diagnostics, and Responsiveness
- Page
- AppPaths
- WinUI Required Flow (task classification pipeline)
- .TryDetectAsync
- UpdateService.IsBusy update interlock
- Border
- NavigationEventArgs
- NumberBox
- NumberBoxValueChangedEventArgs
- ArduPilotModes
- RoutedEventArgs
- FixedHost
- ArduPilotModes
- Grid
- PortPopup
- PortsBelowScroll
- ListView
- NumberBox
- InfoBar
- HudClip
- AutoConnectCheck
- PitchTranslate
- RollRotate
- AppServices
- Visibility
- ReadOnlySpan
- Path
- CompassList
- LinkRing
- IEnumerable
- ExpectedCompassSlotRow
- double
- ReferenceParameters
- ushort
- CalibrationLogRow
- CancellationTokenSource
- CompassSnapshot
- ObservableCollection
- Dictionary
- MavSeverity
- ParameterProgress
- ParameterTransferPlan
- InfoBarSeverity
- IReadOnlyList
- Dictionary
- byte
- SysStatusMessage
- string

## God Nodes (most connected - your core abstractions)
1. `MainPage` - 106 edges
2. `Page` - 93 edges
3. `SerialVehicleLink` - 91 edges
4. `CompassCalibrationPage` - 55 edges
5. `ReferenceEditorPage` - 54 edges
6. `SqliteCalibrationStore` - 52 edges
7. `Page` - 50 edges
8. `Page` - 50 edges
9. `SerialCompassCalibrationJob` - 50 edges
10. `AppServices` - 39 edges

## Surprising Connections (you probably didn't know these)
- `GRAPH HEALTH WARNING Check In GRAPH_REPORT.md` --semantically_similar_to--> `Version From Tag (SemVer Validation Step)`  [INFERRED] [semantically similar]
  CLAUDE.md → .github/workflows/release.yml
- `Граф знаний — первая точка входа` --semantically_similar_to--> `Skill-Led Reasoning Over Pre-Training Reasoning`  [INFERRED] [semantically similar]
  CLAUDE.md → .github/skills/README.md
- `Native WinUI / Fluent First (no bespoke chrome)` --semantically_similar_to--> `Establishing the Creative Foundation`  [AMBIGUOUS] [semantically similar]
  .github/skills/winui-app/SKILL.md → .github/skills/premium-frontend-ui/SKILL.md
- `--packTitle и --icon обязательны для паритета локальной и релизной сборки` --semantically_similar_to--> `vpk CLI Version Must Match Velopack Package Version`  [INFERRED] [semantically similar]
  README.md → .github/workflows/release.yml
- `Локальная сборка установщика` --semantically_similar_to--> `vpk pack (Installer Packaging Step)`  [INFERRED] [semantically similar]
  README.md → .github/workflows/release.yml

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Compass calibration transfer: write, verify, reboot, prove acceptance** — _github_skills_ardupilot_firmware_references_compass_calibration_transfer_transfer_state_machine, _github_skills_ardupilot_firmware_references_compass_calibration_transfer_dev_id_validity_rule, _github_skills_ardupilot_firmware_references_compass_calibration_transfer_instance_mapping, _github_skills_ardupilot_firmware_references_compass_calibration_transfer_rollback_snapshot, _github_skills_ardupilot_firmware_references_imu_level_and_health_verification_verificationverdict [EXTRACTED 1.00]
- **Sources that must report the bench busy to UpdateService.IsBusy** — _github_skills_ardupilot_firmware_references_dotnet_mavlink_and_winui_integration_update_busy_interlock, _github_skills_ardupilot_firmware_references_dotnet_mavlink_and_winui_integration_isbenchbusy, _github_skills_ardupilot_firmware_references_parameter_protocol_and_profiles_write_verify_procedure, _github_skills_ardupilot_firmware_references_dotnet_mavlink_and_winui_integration_reboot_survival, _github_skills_ardupilot_firmware_references_reference_profiles_and_storage_run, _github_skills_ardupilot_firmware_references_reference_profiles_and_storage_write_as_you_go [EXTRACTED 1.00]
- **Domain services, one per sibling reference** — _github_skills_ardupilot_firmware_references_dotnet_mavlink_and_winui_integration_iparameterservice, _github_skills_ardupilot_firmware_references_dotnet_mavlink_and_winui_integration_icompassservice, _github_skills_ardupilot_firmware_references_dotnet_mavlink_and_winui_integration_icalibrationservice, _github_skills_ardupilot_firmware_references_dotnet_mavlink_and_winui_integration_itelemetryservice, _github_skills_ardupilot_firmware_references_dotnet_mavlink_and_winui_integration_layering [EXTRACTED 1.00]
- **Premium Motion System Constrained by Performance Guardrails** — _github_skills_premium_frontend_ui_skill_motion_design_system, _github_skills_premium_frontend_ui_skill_scroll_driven_narratives, _github_skills_premium_frontend_ui_skill_high_fidelity_micro_interactions, _github_skills_premium_frontend_ui_skill_performance_imperative, _github_skills_premium_frontend_ui_skill_hardware_acceleration, _github_skills_premium_frontend_ui_skill_reduced_motion_accessibility [INFERRED 0.85]
- **WinUI Environment Bootstrap and Scaffold Flow** — _github_skills_winui_app_skill_setup_and_scaffold_flow, _github_skills_winui_app_skill_winget_configure_bootstrap, _github_skills_winui_app_config_winui_bootstrap_configuration, _github_skills_winui_app_config_developer_mode, _github_skills_winui_app_config_visual_studio_community, _github_skills_winui_app_config_vscomponents_workloads, _github_skills_winui_app_skill_dotnet_new_winui, _github_skills_winui_app_skill_launch_verification [EXTRACTED 1.00]
- **Foundation Flow: Audit, Select, Scaffold, Build, Recover, Verify** — _github_skills_winui_app_references_foundation_environment_audit_and_remediation_environment_audit_and_remediation, _github_skills_winui_app_references_foundation_setup_and_project_selection_setup_and_project_selection, _github_skills_winui_app_references_foundation_winui_app_structure_winui_app_structure, _github_skills_winui_app_references_foundation_template_first_recovery_template_first_recovery, _github_skills_winui_app_references_build_run_and_launch_verification_build_run_and_launch_verification [EXTRACTED 1.00]
- **Phone-Width Adaptive Strategy Across Shell, Layout, and Review** — _github_skills_winui_app_references_controls_layout_and_adaptive_ui_phone_width_layout_plan, _github_skills_winui_app_references_controls_layout_and_adaptive_ui_adaptive_breakpoint_intent, _github_skills_winui_app_references_shell_navigation_and_windowing_narrow_width_nav_mode, _github_skills_winui_app_references_testing_debugging_and_review_checklists_runtime_breakpoint_verification, _github_skills_winui_app_references_testing_debugging_and_review_checklists_design_review_checklist [INFERRED 0.95]
- **Packaging Model Coherence Across Setup, Launch, and Deployment** — _github_skills_winui_app_references_foundation_setup_and_project_selection_packaged_by_default, _github_skills_winui_app_references_build_run_and_launch_verification_packaged_vs_unpackaged_rules, _github_skills_winui_app_references_build_run_and_launch_verification_package_identity_assumption, _github_skills_winui_app_references_windows_app_sdk_lifecycle_notifications_and_deployment_deployment_model_explicitness, _github_skills_winui_app_references_windows_app_sdk_lifecycle_notifications_and_deployment_bootstrapper_runtime_initialization [INFERRED 0.95]
- **Tag-Driven Release Pipeline (tag → version → publish → delta → pack → upload)** — _github_workflows_release_tag_trigger, _github_workflows_release_version_from_tag, _github_workflows_release_build_step, _github_workflows_release_delta_download, _github_workflows_release_vpk_pack, _github_workflows_release_vpk_upload, readme_release_procedure [EXTRACTED 1.00]
- **Unpackaged Deployment Constraint Set (guardrails preserving the delivery model)** — agents_unpackaged_deployment, agents_disable_xaml_generated_main, agents_enablemsixtooling, agents_publishtrimmed_false, agents_apppaths_storage_safety, readme_deployment_model [INFERRED 0.85]
- **Knowledge Graph Maintenance Protocol** — claude_graph_first_entry_point, claude_graph_freshness, claude_graph_breaking_changes, claude_graph_update_commands, claude_graph_health_warning [EXTRACTED 1.00]
- **Badged-icon composition: base network glyph + status overlay expressing QC acceptance branding** — ardu_otk_ardu_otk_assets_lockscreenlogo_scale_200_lockscreenlogo, ardu_otk_ardu_otk_assets_lockscreenlogo_scale_200_node_graph_glyph, ardu_otk_ardu_otk_assets_lockscreenlogo_scale_200_green_check_badge, ardu_otk_ardu_otk_assets_lockscreenlogo_scale_200_brand_identity [INFERRED 0.85]
- **Splash logo composition: tile + quadcopter glyph + verdict badge on transparent canvas** — ardu_otk_assets_splashscreen_scale_200_transparent_letterbox_canvas, ardu_otk_assets_splashscreen_scale_200_dark_blue_rounded_square, ardu_otk_assets_splashscreen_scale_200_quadcopter_glyph, ardu_otk_assets_splashscreen_scale_200_green_checkmark_badge [EXTRACTED 1.00]
- **Brand identity system: UAV QC domain meaning conveyed via palette, badge pattern and launch surface** — ardu_otk_assets_splashscreen_scale_200_uav_qc_domain_semantics, ardu_otk_assets_splashscreen_scale_200_pass_fail_verdict_visual_language, ardu_otk_assets_splashscreen_scale_200_navy_green_brand_palette, ardu_otk_assets_splashscreen_scale_200_app_launch_branding [INFERRED 0.75]
- **Badge-over-glyph icon composition: navy backplate + white node graph + green check overlay** — ardu_otk_ardu_otk_assets_square150x150logo_scale_200_navy_gradient_squircle, ardu_otk_ardu_otk_assets_square150x150logo_scale_200_node_graph_glyph, ardu_otk_ardu_otk_assets_square150x150logo_scale_200_green_check_badge, ardu_otk_ardu_otk_assets_square150x150logo_scale_200_app_icon_mark [EXTRACTED 1.00]
- **Brand meaning system: connected-device topology validated by QC acceptance, expressed in the navy/green palette** — ardu_otk_ardu_otk_assets_square150x150logo_scale_200_device_topology_metaphor, ardu_otk_ardu_otk_assets_square150x150logo_scale_200_qc_pass_semantics, ardu_otk_ardu_otk_assets_square150x150logo_scale_200_brand_palette, ardu_otk_ardu_otk_assets_square150x150logo_scale_200_app_icon_mark [INFERRED 0.75]
- **App tile composition: blue rounded plate + node-graph glyph + green check badge form the ARDU OTK identity** — ardu_otk_ardu_otk_assets_square44x44logo_scale_200_app_tile_icon, ardu_otk_ardu_otk_assets_square44x44logo_scale_200_blue_rounded_tile, ardu_otk_ardu_otk_assets_square44x44logo_scale_200_node_graph_glyph, ardu_otk_ardu_otk_assets_square44x44logo_scale_200_green_check_badge [EXTRACTED 1.00]
- **Windows packaging icon pipeline: named logo asset + density qualifier + shared asset set** — ardu_otk_ardu_otk_assets_square44x44logo_scale_200_app_tile_icon, ardu_otk_ardu_otk_assets_square44x44logo_scale_200_scale_200_density_qualifier, ardu_otk_ardu_otk_assets_square44x44logo_scale_200_windows_app_icon_asset_set [INFERRED 0.85]
- **Windows app icon variant matrix (base logo x targetsize x altform) resolved at package install** — ardu_otk_assets_square44x44logo_targetsize_24_altform_unplated_asset, ardu_otk_assets_square44x44logo_targetsize_24_altform_unplated_targetsize_24_scaling, ardu_otk_assets_square44x44logo_targetsize_24_altform_unplated_unplated_variant, ardu_otk_assets_square44x44logo_targetsize_24_altform_unplated_msix_packaging [INFERRED 0.85]
- **48px light-unplated tile composes node-graph glyph plus green pass badge to express ARDU OTK identity** — ardu_otk_assets_square44x44logo_targetsize_48_altform_lightunplated_icon, ardu_otk_assets_square44x44logo_targetsize_48_altform_lightunplated_node_graph_glyph, ardu_otk_assets_square44x44logo_targetsize_48_altform_lightunplated_green_check_badge, ardu_otk_assets_square44x44logo_targetsize_48_altform_lightunplated_otk_brand_identity [INFERRED 0.85]
- **Badged-icon composition: base motif + status badge + size constraint yield the app's Store identity** — ardu_otk_assets_storelogo_network_motif, ardu_otk_assets_storelogo_green_check_badge, ardu_otk_assets_storelogo_small_size_legibility, ardu_otk_assets_storelogo_brand_identity [INFERRED 0.75]
- **App brand identity composition: drone glyph + QC checkmark badge on navy rounded-square, packaged as an MSIX wide tile** — ardu_otk_assets_wide310x150logo_scale_200_wide_tile_logo, ardu_otk_assets_wide310x150logo_scale_200_quadcopter_mark, ardu_otk_assets_wide310x150logo_scale_200_qc_checkmark_badge, ardu_otk_assets_wide310x150logo_scale_200_brand_palette, ardu_otk_assets_wide310x150logo_scale_200_msix_tile_asset [INFERRED 0.85]

## Communities (128 total, 17 thin omitted)

### Community 1 - "SerialCompassCalibrationJob"
Cohesion: 0.13
Nodes (13): MavParamType, PendingWrite, SerialCompassCalibrationJob, SlotNames, CancellationToken, double, IEnumerable, int (+5 more)

### Community 2 - "AcceptanceChecks"
Cohesion: 0.22
Nodes (9): CalibrationCheckRow, CalibrationHistoryRow, CalibrationLogRow, CalibrationStageRow, ParamMismatchRow, StageRowState, bool, string (+1 more)

### Community 3 - "MainPage"
Cohesion: 0.18
Nodes (3): CancellationToken, Task, CalibrationReference

### Community 4 - "CompassIdentity"
Cohesion: 0.15
Nodes (13): CompassDeviceId, CompassSlot, MavBusType, CompassFieldComparison, CompassIdentity, CompassSlotComparison, CompassTopologyVerdict, ExpectedCompassSlot (+5 more)

### Community 5 - "SqliteCalibrationStore"
Cohesion: 0.09
Nodes (24): AppPaths, DateTimeOffset, string, SqliteCalibrationStore, bool, CancellationToken, DateTimeOffset, double (+16 more)

### Community 6 - "Page"
Cohesion: 0.10
Nodes (37): CompareDiffText, CompareMatchedText, CompareSkippedText, CompareStateText, CompareTickCount, CompareTickName, CompassBusyText, CompassHintText (+29 more)

### Community 7 - "MavlinkProtocol.cs"
Cohesion: 0.11
Nodes (20): AttitudeMessage, AutopilotVersionMessage, CommandAckMessage, Gps2RawMessage, GpsRawIntMessage, HeartbeatMessage, ImuMessage, MavlinkFraming (+12 more)

### Community 8 - "Agent Skills Index"
Cohesion: 0.06
Nodes (47): Agent Skills Index, ardupilot-firmware Skill, premium-frontend-ui Skill, Skill-Led Reasoning Over Pre-Training Reasoning, winui-app Skill, dotnet publish Build Step (win-x64 Release), vpk CLI Version Must Match Velopack Package Version, Download Previous Releases For Delta Computation (+39 more)

### Community 9 - "Page"
Cohesion: 0.06
Nodes (46): AzimuthBar, ChecksList, ChecksSummaryText, ErrorBar, GateBar, HistoryCard, HistoryHintText, HistoryList (+38 more)

### Community 10 - "ReferenceEditorPage"
Cohesion: 0.11
Nodes (14): SaveButton, ReferenceEditorPage, bool, Exception, int, List, NavigationEventArgs, NumberBox (+6 more)

### Community 11 - "Page"
Cohesion: 0.06
Nodes (49): AddScriptButton, AuthorPanel, BrowseButton, CancelButton, ErrorBar, FrozenBar, GateBar, HeadingToleranceBox (+41 more)

### Community 12 - "ParameterRoleMap"
Cohesion: 0.10
Nodes (13): ParameterRole, ParameterRoleMap, ParameterRoleOverride, ParameterRoleRule, Dictionary, IEnumerable, int, IReadOnlyList (+5 more)

### Community 13 - "CompassCalibrationPage"
Cohesion: 0.11
Nodes (11): BrowseButton, CancelButton, NewRunButton, RefreshHistoryButton, RefreshPortsButton, RewriteAllButton, StartButton, Exception (+3 more)

### Community 15 - "UpdateService"
Cohesion: 0.11
Nodes (24): BoardStateText, EmptyText, GeneralList, GeneralPanel, MockHost, MockNoteText, MockSizeText, Page (+16 more)

### Community 16 - "Connect Handshake (HEARTBEAT, autopilot==3 gate, AUTOPILOT_VERSION)"
Cohesion: 0.11
Nodes (19): Per-instance mag feed (RAW_IMU/SCALED_IMU2/SCALED_IMU3 to priority slot), Baud is a no-op over USB CDC, Connect Handshake (HEARTBEAT, autopilot==3 gate, AUTOPILOT_VERSION), Freshness and Link Loss (staleness timeouts, degradation), Flight Mode Tables (COPTER_MODE/PLANE_MODE/ROVER_MODE by MAV_TYPE), Reading<T> (value + UpdatedUtc staleness wrapper), On Copter every SRn_* group defaults to 0 Hz; intervals do not survive reboot, Stream Rate Policy via MAV_CMD_SET_MESSAGE_INTERVAL (511) (+11 more)

### Community 17 - "Write then verify procedure (PARAM_SET + independent read by name)"
Cohesion: 0.11
Nodes (19): IParameterService, Clean-run rule, Comparison rules (integer exact, REAL32 relative + absolute floor), Detect (param file format detection), Diff outcome model (Match/Differs/MissingOnBoard/NotInReference/Excluded/ReadOnly/Coalesced/ReadFailed), Exportable diff report, MAV_PARAM_TYPE handling and C-cast integer encoding, Reference file formats: Mission Planner .param/.parm and QGC .params (+11 more)

### Community 18 - "RoutedEventArgs"
Cohesion: 0.50
Nodes (3): PortCombo, SelectionChangedEventArgs, ComboBox

### Community 19 - "AcceptancePage"
Cohesion: 0.13
Nodes (15): CompassCalibrationPage, bool, CancellationToken, CancellationTokenSource, Func, IProgress, IReadOnlyList, long (+7 more)

### Community 20 - "MotorCompToggle"
Cohesion: 0.15
Nodes (11): FullParameterSet, OsdConfiguration, OsdFieldKind, OsdLayout, OsdValue, PanelBuilder, Dictionary, int (+3 more)

### Community 21 - "CalibrationStageRow"
Cohesion: 0.21
Nodes (8): SettingsPage, bool, double, EventArgs, InfoBarSeverity, NumberBox, NumberBoxValueChangedEventArgs, Task

### Community 22 - "AppServices"
Cohesion: 0.12
Nodes (12): ReferenceParamSet, ReferenceParamFile, char, int, IReadOnlyDictionary, IReadOnlyList, string, ReferenceParameters (+4 more)

### Community 23 - "Transfer State Machine (states 0-14)"
Cohesion: 0.16
Nodes (16): DEV_ID Validity Rule (read-back equality is not proof), Compass::force_save_calibration() path (UNVERIFIED), Rollback Snapshot (pre-write .param capture), Soft-iron destruction conflict (fixed-yaw forces DIA=1,1,1 / ODI=0,0,0), Transfer State Machine (states 0-14), Workflow (a): Compass Calibration Transfer, Reboot survival and COM re-enumeration, Thirteen safety rules for a tool that writes to flight hardware (+8 more)

### Community 25 - "Capability Map (requirement to owning reference)"
Cohesion: 0.17
Nodes (15): Reference Index, Transferable Parameter Classification (COPY / NEVER COPY / OPT-IN), COMPASS_DIA* default version trap (0 in 4.1, 1.0 in master), COMPASS_MOT*/COMPASS_MOTCT opt-in gating rule, Compass Parameter Map (per-instance lookup tables), Irregular compass parameter naming (EXTERNAL to EXTERN2/3, middle-digit OFS2_X), Built-in default exclusions (Mission Planner skip list), Comparison profile JSON schema (the configurable block) (+7 more)

### Community 26 - ".Log"
Cohesion: 0.16
Nodes (11): OsdPage, bool, double, int, IReadOnlyDictionary, ObservableCollection, SizeChangedEventArgs, OsdScreen (+3 more)

### Community 27 - "Run (one verification session against one board under test)"
Cohesion: 0.15
Nodes (13): COMPASS_TYPEMASK to COMPASS_DISBLMSK rename: probe both names, ARMING_CHECK vs ARMING_SKIPCHK inverted polarity: probe by name, A clean run can be an artefact of disabled check bits, PREARM_CHECK bit (0x10000000), SYS_STATUS health bits (present && enabled && health), VerificationVerdict record, ChecksEnabledJson: a clean result must not be mistaken for a complete one, ParamWriteAudit table (append-only write audit log) (+5 more)

### Community 28 - "Deployment: unpackaged, self-contained, Velopack"
Cohesion: 0.15
Nodes (13): MAVLink library choice: Asv.Mavlink, Deployment: unpackaged, self-contained, Velopack, EnableMsixTooling kept for the XAML resource copy (0xC000027B), There is no serialcommunication capability question in this app, PublishTrimmed=False is deliberate (WinUI resolves by reflection), Version pinning against net10.0-windows10.0.26100.0, Backup, export and portability, A NotInstalled app must not touch the production store (+5 more)

### Community 29 - "Controls, Layout, and Adaptive UI"
Cohesion: 0.17
Nodes (16): Explicit Adaptive Breakpoint Intent, CommandBar as Native Command Surface, Controls, Layout, and Adaptive UI, Single Search Field with Live Filtering, Phone-Width Single-Column Layout Plan, Virtualization-Friendly Collection Controls, Single Main Shell Window Owning Navigation, Keep the UI Thread Free (+8 more)

### Community 30 - "Telemetry Data Model (attitude, voltage, current, mode, sentinels)"
Cohesion: 0.18
Nodes (12): AHRS2 must never be a silent fallback for ATTITUDE, BATTERY_STATUS voltages[] summation rule and two sentinels, Telemetry Data Model (attitude, voltage, current, mode, sentinels), ICalibrationService, ICompassService, ITelemetryService, Six-layer one-directional stack (transport to XAML), No generated MAVLink type ever reaches a view model or XAML (+4 more)

### Community 31 - "CommunityToolkit Controls and Helpers"
Cohesion: 0.20
Nodes (11): Full Keyboard Reachability and Focus Order, CommunityToolkit Controls and Helpers, Toolkit HeaderedControls, Platform Controls First Before Toolkit Dependencies, Toolkit Segmented Control, Toolkit SettingsControls, Toolkit Animations Package, Built-In WinUI Controls First (+3 more)

### Community 32 - "Startup Failure Debugging Path"
Cohesion: 0.21
Nodes (12): Build, Run, and Launch Verification, Explicit x64 Platform Target for Local Verification, Objective Launch Verification Evidence, Startup Failure Debugging Path, Standard Blank App Template First, dotnet new winui Comparison Scaffold, Opaque MSB3073 / XamlCompiler.exe Failures, Template-First Recovery Loop (+4 more)

### Community 33 - "Classify (external/internal decision procedure)"
Cohesion: 0.22
Nodes (11): MAG_CAL_REPORT.fitness judged against COMPASS_CAL_FIT (x2 rule), Instance Mapping by decoded device id, Boot-time per-instance block swap (_reorder_compass_params), BusType enum (0-7, no EXTERNALAHRS), Classify (external/internal decision procedure), CompassDevId (DEV_ID bitfield decode), CompassRow (compass panel per-instance view model), Compass devtype table (AP_Compass_Backend.h authoritative) (+3 more)

### Community 34 - "AcceptanceSession"
Cohesion: 0.19
Nodes (10): AcceptanceStepRow, AcceptanceStepState, ParameterDifferenceRow, ScriptDifferenceRow, bool, int, IReadOnlyList, string (+2 more)

### Community 35 - "WinUI Reference Sections Index"
Cohesion: 0.27
Nodes (11): WinUI Reference Sections Index, Developer Mode as Optional-Not-Universal Requirement, Environment Audit and Remediation, Manual Non-Mutating Readiness Audit, Required WinUI Prerequisite Baseline, Setup-and-Scaffold Flow (SKILL.md), C#-First WinUI 3 Desktop App on Windows App SDK, Setup and Project Selection (+3 more)

### Community 36 - "Styling, Theming, Materials, and Icons"
Cohesion: 0.20
Nodes (14): Accessibility, Input, and Localization, Automation Properties and Accessible Naming, High-Contrast-Safe Visuals, Mouse, Touch, Pen, and Keyboard Input Parity, Localization and RTL Readiness, Narrator Support, Custom Title Bar as Functional Chrome, Acrylic for Transient Surfaces (+6 more)

### Community 37 - "Navy + Green Brand Palette (shared app color identity)"
Cohesion: 0.36
Nodes (11): ARDU OTK Splash Screen Image (scale-200), App Launch Branding Surface (splash shown during startup), Badge-Overlay Icon Pattern (base subject + status badge on corner), Dark Blue Rounded-Square App Tile with Gradient, Green Checkmark Badge Overlay (bottom-right quadrant), Navy + Green Brand Palette (shared app color identity), Pass/Fail Verdict Visual Language (green = accepted result), Quadcopter Rotor Glyph (four white rotor rings on rounded dark-blue square) (+3 more)

### Community 38 - "Task"
Cohesion: 0.07
Nodes (22): AirData, SerialVehicleLink, StatusTextAssembly, Action, bool, byte, CancellationTokenSource, DateTimeOffset (+14 more)

### Community 39 - "WinUI WinGet DSC Bootstrap Configuration"
Cohesion: 0.16
Nodes (15): Atmospheric Filters (noise, grain, glass), Typography & Visual Texture, Enable Developer Mode (WindowsSettings resource), OsVersion Assertion (min 10.0.17763), Install Visual Studio Community 2026 (WinGetPackage), VS Workloads: ManagedDesktop, Universal, WindowsAppSDK.Cs, WinUI WinGet DSC Bootstrap Configuration, Common Routes Reference Table (+7 more)

### Community 40 - "ARDU_OTK.csproj"
Cohesion: 0.18
Nodes (9): net10.0-windows10.0.26100.0, Microsoft.Data.Sqlite (10.0.10), Microsoft.Windows.SDK.BuildTools (10.0.28000.2526), Microsoft.WindowsAppSDK (2.3.1), SQLitePCLRaw.bundle_e_sqlite3 (3.0.5), System.IO.Ports (10.0.10), System.Management (10.0.10), Velopack (1.2.0) (+1 more)

### Community 41 - "ARDU OTK App Icon Mark"
Cohesion: 0.29
Nodes (10): ARDU OTK App Icon Mark, Brand Palette: Navy #1B3A5C + Accent Green #22B14C, Device/Sensor Topology Metaphor, Green Checkmark Badge (bottom-right overlay), MSIX scale-200 Asset Naming Convention, Navy Gradient Rounded-Square Backplate, White Node-Graph Glyph (three connected nodes), Package.appxmanifest Tile Declaration (implied consumer) (+2 more)

### Community 42 - "Procedure: make external compass primary and set use flags (Phases A-F)"
Cohesion: 0.25
Nodes (9): Handoff to verification (exact conditions), Procedure: make external compass primary and set use flags (Phases A-F), Never leave the board with zero COMPASS_USE* set, COMPASS_PRIOx_ID Priority Model, STATUSTEXT chunk reassembly (id + chunk_seq) before string matching, STATUSTEXT Ingestion and MAV_SEVERITY inversion, MAV_CMD_RUN_PREARM_CHECKS (401) and its collection window, Prearm catalogue (exact IMU and compass PreArm strings) (+1 more)

### Community 43 - "graph_freshness.py"
Cohesion: 0.17
Nodes (25): compare(), corpus_on_disk(), describe(), emit_hook(), force_utf8_streams(), git(), health_report(), is_corpus_path() (+17 more)

### Community 44 - "The Performance Imperative"
Cohesion: 0.32
Nodes (8): Custom Cursor Tracking with Lerp Interpolation, Hardware Acceleration (animate only transform/opacity), High-Fidelity Micro-Interactions, Magnetic Components, The Motion Design System, The Performance Imperative, prefers-reduced-motion Accessibility Guardrail, Responsive Degradation for Touch Devices

### Community 45 - "WinUI App Structure"
Cohesion: 0.17
Nodes (9): PageProgress, Action, MavSeverity, CalibrationRequest, CalibrationStage, CalibrationTolerances, ICalibrationProgress, WriteOutcome (+1 more)

### Community 46 - "UpdateService.IsBusy update interlock"
Cohesion: 0.29
Nodes (4): Border, Column, Row, OsdScreenSize

### Community 47 - ".OnFormFieldChanged"
Cohesion: 0.13
Nodes (13): AirData, AttitudeSample, GpsFix, MavCommand, MavResult, ParameterProgress, SensorHealth, SysStatusSensor (+5 more)

### Community 48 - "TelemetrySession"
Cohesion: 0.15
Nodes (10): MagAccumulator, TelemetrySession, Dictionary, double, int, MagSample, object, TelemetrySnapshot (+2 more)

### Community 49 - "MAV_CMD_FIXED_MAG_CAL_YAW (42006)"
Cohesion: 0.25
Nodes (8): Azimuth input must be TRUE north, not magnetic, MAG_CAL_STATUS enum (with ArduPilot extensions 6-10), Onboard mag cal commands (DO_START/ACCEPT/CANCEL_MAG_CAL), MAV_CMD_FIXED_MAG_CAL_YAW (42006), _reset_compass_id() side effect on priority slots, Workflow (b): Fixed-Yaw / Large-Vehicle Calibration, ATTITUDE.yaw and VFR_HUD.heading are TRUE north, CalibrationOp table (command id, params sent, MAV_RESULT, STATUSTEXT)

### Community 50 - "MavlinkFtp.cs"
Cohesion: 0.15
Nodes (7): ChannelField, Func, IEnumerable, ChannelOrder, string, Number, Prefix

### Community 51 - "Window"
Cohesion: 0.21
Nodes (10): IVehicleFileTransfer, IProgress, IReadOnlyList, ReadOnlyMemory, ScriptTransfer, CancellationToken, IProgress, IReadOnlyList (+2 more)

### Community 52 - "ARDU OTK Square 44x44 App Tile Icon (scale-200)"
Cohesion: 0.50
Nodes (8): ARDU OTK Square 44x44 App Tile Icon (scale-200), Badge-Overlay Icon Composition Pattern (base glyph + status badge), Dark Blue Rounded-Square Tile Background, Green Circular Checkmark Badge Overlay, White Node-Graph / Network Topology Glyph, ОТК Quality-Control Pass/Accept Branding Motif, scale-200 Resource Density Qualifier (88x88 px effective), Windows App Icon Asset Set (scale-qualified logo variants)

### Community 53 - "LockScreenLogo.scale-200 (app lock screen logo asset)"
Cohesion: 0.52
Nodes (7): ARDU OTK visual brand identity (device-network + acceptance check), Green checkmark badge overlay (QC pass indicator), LockScreenLogo.scale-200 (app lock screen logo asset), Node-graph glyph (three connected blue circles), OTK (ОТК) quality-control acceptance domain, scale-200 density variant (Windows packaging asset naming convention), Windows app manifest lock-screen logo asset slot

### Community 54 - "Square44x44Logo targetsize-24 altform-unplated (app icon asset)"
Cohesion: 0.52
Nodes (7): Square44x44Logo targetsize-24 altform-unplated (app icon asset), ARDU OTK visual brand identity (dark navy + white check), Checkmark glyph on dark rounded square, MSIX / WinUI app package manifest asset set, Quality-control / pass-inspection metaphor (OTK acceptance check), targetsize-24 asset scaling convention, Unplated altform variant (transparent-background taskbar icon)

### Community 55 - "Green circular checkmark badge overlay (bottom-right corner)"
Cohesion: 0.52
Nodes (7): ARDU OTK visual brand identity: connected devices verified by QC, Green circular checkmark badge overlay (bottom-right corner), StoreLogo.png — Microsoft Store tile logo for ARDU OTK, MSIX/WinUI packaging asset convention (Assets/ logo set), Dark-blue node-and-link (molecule/network) motif, OTK (ОТК) quality-control / pass-fail verdict semantics, Small-size legibility constraint (flat shapes, 2-color contrast at ~50px)

### Community 56 - "SerialPortCatalog"
Cohesion: 0.20
Nodes (8): ParamValue, StatusTextEvent, RunContext, DateTimeOffset, Dictionary, List, ConcurrentQueue, PendingWrite

### Community 58 - ".OnFormFieldChanged"
Cohesion: 0.21
Nodes (9): ExpectedCompassSlotRow, ParameterRoleRow, ParameterRoleSectionRow, ReferenceCaption, ReferenceScriptRow, ObservableCollection, Visibility, ParameterControl (+1 more)

### Community 59 - ".Subscribe"
Cohesion: 0.19
Nodes (7): OsdPanelRow, OsdRowState, OsdValueRow, Visibility, OsdPanel, Column, Row

### Community 60 - "Square44x44Logo targetsize-48 altform-lightunplated (app tile icon 48px)"
Cohesion: 0.60
Nodes (6): Green check-mark badge overlay (pass / QC accepted), Square44x44Logo targetsize-48 altform-lightunplated (app tile icon 48px), altform-lightunplated asset variant (unplated icon for light taskbar/theme), MSIX/UWP asset naming convention (Square44x44Logo.targetsize-N_altform-*), Blue node-graph glyph (connected circles / network of sensors), ARDU OTK brand identity: device-under-test passes quality control

### Community 61 - "ARDU OTK Wide Tile Logo (310x150 @200%)"
Cohesion: 0.53
Nodes (6): Dark Navy Blue Brand Palette, MSIX/UWP Wide Tile Asset Convention, ОТК (Quality Control) Domain Identity, Green Checkmark QC Badge, Quadcopter/Drone Glyph Mark, ARDU OTK Wide Tile Logo (310x150 @200%)

### Community 62 - "ReferenceRows.cs"
Cohesion: 0.18
Nodes (14): Hidden Package-Identity Assumptions, Packaged vs Unpackaged Launch Rules, Packaged App by Default, Centralized Shared Resource Dictionaries, C#-First Folder Split (Pages, Controls, ViewModels, Services, Styles, Assets), WinUI App Structure, x:Bind vs Binding Guidance, WinUI Gallery (microsoft/WinUI-Gallery) (+6 more)

### Community 63 - "ARDU_OTK.Services.Fc"
Cohesion: 0.19
Nodes (14): AcceptanceSession, ArmReadiness, StepResult, bool, CancellationToken, Failure, IProgress, IReadOnlyDictionary (+6 more)

### Community 64 - "Establishing the Creative Foundation"
Cohesion: 0.10
Nodes (15): OsdReferenceChoice, ReferenceEditorArgs, ReferenceRow, Visibility, RunRow, Brush, string, BoardParameterSnapshot (+7 more)

### Community 65 - ".OnPortSelectionChanged"
Cohesion: 0.34
Nodes (5): IVehicleLink, CancellationToken, Task, TimeSpan, IAsyncDisposable

### Community 66 - ".Dispatch"
Cohesion: 0.19
Nodes (6): IReadOnlyList, ReferenceScript, char, int, ReadOnlySpan, string

### Community 67 - ".OnNewRunClick"
Cohesion: 0.40
Nodes (3): IEnumerable, Stage, Title

### Community 68 - ".ConnectAsync"
Cohesion: 0.18
Nodes (6): Application, App, Exception, Program, LaunchActivatedEventArgs, STAThread

### Community 69 - "Corrupted Binary Asset (UTF-8 Mojibake Re-encoding)"
Cohesion: 1.00
Nodes (3): Corrupted Binary Asset (UTF-8 Mojibake Re-encoding), WinUI Skill Icon (winui.png), winui-app Skill (WinUI 3 / Windows App SDK)

### Community 71 - "StackPanel"
Cohesion: 0.20
Nodes (10): CompassBusyPanel, FcSelector, PortFlyoutRoot, PortsAbove, PortsBelow, ReferenceFlyoutRoot, ReferencesAbove, ReferencesBelow (+2 more)

### Community 73 - "Regex"
Cohesion: 0.30
Nodes (3): MavlinkCrc, MavlinkParser, bool

### Community 74 - ".ReadAllAsync"
Cohesion: 0.23
Nodes (6): UiScale, double, string, FontScaleBox, SelectionChangedEventArgs, ComboBox

### Community 75 - "WinUI Required Flow (task classification pipeline)"
Cohesion: 0.22
Nodes (7): ReferencePackage, ReferencePackageScript, DateTimeOffset, int, List, string, JsonSerializerOptions

### Community 76 - "UpdateService"
Cohesion: 0.24
Nodes (8): UpdateService, UpdateState, CancellationToken, Func, string, Task, UpdateInfo, UpdateManager

### Community 77 - "SerialPortDescription"
Cohesion: 0.31
Nodes (6): SerialPortCatalog, SerialPortDescription, IReadOnlyList, Regex, string, Task

### Community 78 - "Accessibility, Input, and Localization"
Cohesion: 0.44
Nodes (3): ICalibrationStore, CancellationToken, Task

### Community 79 - "Performance, Diagnostics, and Responsiveness"
Cohesion: 0.22
Nodes (9): Cinematic Pacing (visual identity), Establishing the Creative Foundation, Cyber / Technical (visual identity), Editorial Brutalism (visual identity), Entry Sequence (Preloading & Initialization), Fluid & Contextual Navigation, Immersive Digital Environments, Organic Fluidity (visual identity) (+1 more)

### Community 80 - "Page"
Cohesion: 0.17
Nodes (15): FontScaleText, Page, StandLatBox, StandLonBox, StoreBar, StorePathText, UpdateBar, UpdateProgress (+7 more)

### Community 82 - "WinUI Required Flow (task classification pipeline)"
Cohesion: 0.40
Nodes (6): WinUI App Skill Interface Metadata (openai.yaml), Apache License 2.0 (winui-app skill), Grounding Sources (Microsoft Learn, WinUI Gallery, WindowsAppSDK-Samples), WinUI Required Flow (task classification pipeline), Bundled Setup-and-Scaffold Flow, winui-app Skill

### Community 83 - ".TryDetectAsync"
Cohesion: 0.33
Nodes (5): WorkstationFix, WorkstationLocator, CancellationToken, Task, TimeSpan

### Community 84 - "UpdateService.IsBusy update interlock"
Cohesion: 0.27
Nodes (7): CompassParameterSnapshot, CompassSnapshot, CancellationToken, Dictionary, IProgress, List, Task

### Community 85 - "Border"
Cohesion: 0.33
Nodes (6): HudCard, ImuCalCard, MagCalCard, PortGlow, ReferenceGlow, Border

### Community 86 - "NavigationEventArgs"
Cohesion: 0.43
Nodes (6): AzimuthBox, OperatorBox, ReferenceFileBox, UnitIdBox, TextChangedEventArgs, TextBox

### Community 87 - "NumberBox"
Cohesion: 0.29
Nodes (8): Framer Motion, GSAP / ScrollTrigger, Hero Architecture, Implementation Ecosystem (framework-tailored libraries), Lenis Smooth Scrolling, React Three Fiber, Scroll-Driven Narratives, SplitType Typography Chunking

### Community 88 - "NumberBoxValueChangedEventArgs"
Cohesion: 0.33
Nodes (6): Choose the Narrowest Reference File, CommunityToolkit/Windows Repository, Microsoft Learn Windows Apps Docs, Canonical Source Preference Order, WindowsAppSDK-Samples, AppWindow and Windows App SDK Windowing

### Community 89 - "ArduPilotModes"
Cohesion: 0.47
Nodes (6): Remove Redundant Outer Section Borders, Simpler Visual Trees and Lighter Templates, Measurement Before Optimization, Performance, Diagnostics, and Responsiveness, WPR/WPA with XAML Frame Analysis, Card-Around-Cards Border Anti-Pattern

### Community 90 - "RoutedEventArgs"
Cohesion: 0.33
Nodes (6): IsBenchBusy predicate (fail-safe to busy), SITL testing scope and its limits, UpdateService.IsBusy update interlock, Interrupted-run sweep to Verdict='aborted', Write as you go; never commit a run only at the end, Hold the update interlock while the bench is working

### Community 91 - "FixedHost"
Cohesion: 0.67
Nodes (3): FixedHost, LadderHost, Canvas

### Community 93 - "Grid"
Cohesion: 0.25
Nodes (7): CompareActionsPanel, CompareCountsPanel, CompareTickPanel, HudViewport, PanelsRow, SizeChangedEventArgs, Grid

### Community 94 - "PortPopup"
Cohesion: 0.33
Nodes (3): MainWindow, NavigationView, NavigationViewSelectionChangedEventArgs

### Community 95 - "PortsBelowScroll"
Cohesion: 0.67
Nodes (3): PortsBelowScroll, ReferencesBelowScroll, ScrollViewer

### Community 97 - "NumberBox"
Cohesion: 0.50
Nodes (4): Prefer Native CommandBar for Grouped Commands, CommunityToolkit Only When Built-ins Fall Short, Native WinUI / Fluent First (no bespoke chrome), Avoid Double-Card Border Wrappers

### Community 98 - "InfoBar"
Cohesion: 0.33
Nodes (3): UpdateActionButton, RoutedEventArgs, Button

### Community 100 - "AutoConnectCheck"
Cohesion: 0.21
Nodes (8): MavFtpDirectory, MavFtpEntry, MavFtpError, MavFtpPayload, int, ReadOnlySpan, IReadOnlyList, ICollection

### Community 103 - "AppServices"
Cohesion: 0.16
Nodes (8): ReadBoardButton, ReferenceBox, InfoBarSeverity, RoutedEventArgs, SelectionChangedEventArgs, Task, Button, ComboBox

### Community 104 - "Visibility"
Cohesion: 0.50
Nodes (3): OperatorBox, TextChangedEventArgs, TextBox

### Community 106 - "Path"
Cohesion: 0.67
Nodes (3): PortFlyout, ReferenceFlyout, Flyout

### Community 108 - "LinkRing"
Cohesion: 0.67
Nodes (3): CompassBusyRing, LinkRing, ProgressRing

### Community 110 - "IEnumerable"
Cohesion: 0.11
Nodes (20): MagSample, PrearmReport, TelemetrySnapshot, IEnumerable, AcceptanceChecks, CheckIds, CompassComplaint, CompassComplaintKind (+12 more)

### Community 126 - "double"
Cohesion: 0.31
Nodes (5): CompassIdentityRow, CompassRow, WatchedParameterRow, double, Visibility

### Community 128 - "ReferenceParameters"
Cohesion: 0.19
Nodes (3): EventArgs, InfoBarSeverity, TextChangedEventArgs

### Community 138 - "ushort"
Cohesion: 0.50
Nodes (4): LinkBar, ReadyBar, ReferenceBar, InfoBar

### Community 143 - "CalibrationLogRow"
Cohesion: 0.13
Nodes (8): Action, Func, IReadOnlyList, Button, Flyout, FrameworkElement, StackPanel, UIElement

### Community 146 - "CancellationTokenSource"
Cohesion: 0.11
Nodes (13): MainPage, bool, Brush, CancellationTokenSource, DateTimeOffset, Dictionary, IEnumerable, List (+5 more)

### Community 147 - "CompassSnapshot"
Cohesion: 0.10
Nodes (24): AppServices, bool, CancellationToken, Failure, IProgress, IReadOnlyList, List, Path (+16 more)

### Community 164 - "Dictionary"
Cohesion: 0.67
Nodes (3): DiffList, ScriptList, ListView

### Community 167 - "MavSeverity"
Cohesion: 0.19
Nodes (12): AppTitleBar, OsdItem, ReferencesItem, RootFrame, RootNav, RunsItem, StandItem, Window (+4 more)

### Community 191 - "ParameterProgress"
Cohesion: 0.22
Nodes (7): Border, CalibrationStatus, CalibrationVerdict, double, IReadOnlyDictionary, string, TextBlock

### Community 200 - "InfoBarSeverity"
Cohesion: 0.17
Nodes (12): AcceptDiffButton, ApplyAllScriptsButton, CompassCalButton, EditReferenceButton, FinishButton, LevelButton, LinkToggleButton, PortCard (+4 more)

### Community 213 - "IReadOnlyList"
Cohesion: 0.17
Nodes (13): MavFtpOpcode, CancellationToken, FullParameterSet, HashSet, IProgress, MavParamType, ParameterProgress, ParamValue (+5 more)

### Community 217 - "Dictionary"
Cohesion: 0.31
Nodes (7): AuthorBox, DescriptionBox, NameBox, ReferencePathBox, RoleFilterBox, TextChangedEventArgs, TextBox

### Community 222 - "byte"
Cohesion: 0.31
Nodes (5): VehicleClass, ParameterEnums, VehicleClass, byte, FrozenDictionary

### Community 239 - "SysStatusMessage"
Cohesion: 0.29
Nodes (5): FrameSubscription, Func, Channel, FrameSubscription, IDisposable

### Community 242 - "string"
Cohesion: 0.06
Nodes (37): CountText, Page, PageBar, ReferenceList, ScopeBox, ReferencesPage, bool, InfoBarSeverity (+29 more)

## Ambiguous Edges - Review These
- `premium-frontend-ui Skill` → `winui-app Skill`  [AMBIGUOUS]
  .github/skills/README.md · relation: semantically_similar_to
- `Establishing the Creative Foundation` → `Native WinUI / Fluent First (no bespoke chrome)`  [AMBIGUOUS]
  .github/skills/winui-app/SKILL.md · relation: semantically_similar_to
- `C#-First Folder Split (Pages, Controls, ViewModels, Services, Styles, Assets)` → `Explicit Deployment Model Before Build Steps`  [AMBIGUOUS]
  .github/skills/winui-app/references/windows-app-sdk-lifecycle-notifications-and-deployment.md · relation: conceptually_related_to
- `Unpackaged Self-Contained Deployment Model` → `EnableMsixTooling=true Retained For XAML/PRI Asset Targets`  [AMBIGUOUS]
  AGENTS.md · relation: conceptually_related_to
- `winui-app Skill (WinUI 3 / Windows App SDK)` → `Corrupted Binary Asset (UTF-8 Mojibake Re-encoding)`  [AMBIGUOUS]
  .github/skills/winui-app/assets/winui.png · relation: conceptually_related_to
- `LockScreenLogo.scale-200 (app lock screen logo asset)` → `OTK (ОТК) quality-control acceptance domain`  [AMBIGUOUS]
  ARDU_OTK/Assets/LockScreenLogo.scale-200.png · relation: conceptually_related_to
- `ARDU OTK Splash Screen Image (scale-200)` → `UAV Quality-Control (ОТК) Domain Semantics Encoded in Logo`  [AMBIGUOUS]
  ARDU_OTK/Assets/SplashScreen.scale-200.png · relation: references
- `Quadcopter Rotor Glyph (four white rotor rings on rounded dark-blue square)` → `Navy + Green Brand Palette (shared app color identity)`  [AMBIGUOUS]
  ARDU_OTK/Assets/SplashScreen.scale-200.png · relation: conceptually_related_to
- `Navy Gradient Rounded-Square Backplate` → `MSIX scale-200 Asset Naming Convention`  [AMBIGUOUS]
  ARDU_OTK/Assets/Square150x150Logo.scale-200.png · relation: conceptually_related_to
- `White Node-Graph / Network Topology Glyph` → `Windows App Icon Asset Set (scale-qualified logo variants)`  [AMBIGUOUS]
  ARDU_OTK/Assets/Square44x44Logo.scale-200.png · relation: shares_data_with
- `Dark Blue Rounded-Square Tile Background` → `ОТК Quality-Control Pass/Accept Branding Motif`  [AMBIGUOUS]
  ARDU_OTK/Assets/Square44x44Logo.scale-200.png · relation: conceptually_related_to
- `ARDU OTK visual brand identity (dark navy + white check)` → `MSIX / WinUI app package manifest asset set`  [AMBIGUOUS]
  ARDU_OTK/Assets/Square44x44Logo.targetsize-24_altform-unplated.png · relation: conceptually_related_to
- `Square44x44Logo targetsize-48 altform-lightunplated (app tile icon 48px)` → `ARDU OTK brand identity: device-under-test passes quality control`  [AMBIGUOUS]
  ARDU_OTK/Assets/Square44x44Logo.targetsize-48_altform-lightunplated.png · relation: shares_data_with
- `Dark-blue node-and-link (molecule/network) motif` → `OTK (ОТК) quality-control / pass-fail verdict semantics`  [AMBIGUOUS]
  ARDU_OTK/Assets/StoreLogo.png · relation: conceptually_related_to
- `ARDU OTK Wide Tile Logo (310x150 @200%)` → `ОТК (Quality Control) Domain Identity`  [AMBIGUOUS]
  ARDU_OTK/Assets/Wide310x150Logo.scale-200.png · relation: shares_data_with

## Knowledge Gaps
- **108 isolated node(s):** `net10.0-windows10.0.26100.0`, `Microsoft.Windows.SDK.BuildTools (10.0.28000.2526)`, `Microsoft.WindowsAppSDK (2.3.1)`, `Velopack (1.2.0)`, `Microsoft.Data.Sqlite (10.0.10)` (+103 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **17 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **What is the exact relationship between `premium-frontend-ui Skill` and `winui-app Skill`?**
  _Edge tagged AMBIGUOUS (relation: semantically_similar_to) - confidence is low._
- **What is the exact relationship between `Establishing the Creative Foundation` and `Native WinUI / Fluent First (no bespoke chrome)`?**
  _Edge tagged AMBIGUOUS (relation: semantically_similar_to) - confidence is low._
- **What is the exact relationship between `C#-First Folder Split (Pages, Controls, ViewModels, Services, Styles, Assets)` and `Explicit Deployment Model Before Build Steps`?**
  _Edge tagged AMBIGUOUS (relation: conceptually_related_to) - confidence is low._
- **What is the exact relationship between `Unpackaged Self-Contained Deployment Model` and `EnableMsixTooling=true Retained For XAML/PRI Asset Targets`?**
  _Edge tagged AMBIGUOUS (relation: conceptually_related_to) - confidence is low._
- **What is the exact relationship between `winui-app Skill (WinUI 3 / Windows App SDK)` and `Corrupted Binary Asset (UTF-8 Mojibake Re-encoding)`?**
  _Edge tagged AMBIGUOUS (relation: conceptually_related_to) - confidence is low._
- **What is the exact relationship between `LockScreenLogo.scale-200 (app lock screen logo asset)` and `OTK (ОТК) quality-control acceptance domain`?**
  _Edge tagged AMBIGUOUS (relation: conceptually_related_to) - confidence is low._
- **What is the exact relationship between `ARDU OTK Splash Screen Image (scale-200)` and `UAV Quality-Control (ОТК) Domain Semantics Encoded in Logo`?**
  _Edge tagged AMBIGUOUS (relation: references) - confidence is low._