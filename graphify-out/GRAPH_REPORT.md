# Graph Report - ARDU_OTK  (2026-10-01)

## Corpus Check
- 95 files · ~192,133 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 6 file(s) not represented in the graph (top: (none) 3, .ico 1, .manifest 1)

## Summary
- 2525 nodes · 5533 edges · 145 communities (114 shown, 31 thin omitted)
- Extraction: 93% EXTRACTED · 7% INFERRED · 0% AMBIGUOUS · INFERRED: 369 edges (avg confidence: 0.82)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `65271336`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- CompassIdentity
- SqliteCalibrationStore
- AcceptanceSession
- RunRow
- Agent Skills Index
- SerialCompassCalibrationJob
- AcceptanceChecks
- ParameterRoleMap
- SerialVehicleLink
- PayloadReader
- Page
- ReferenceEditorPage
- Page
- WinUI WinGet DSC Bootstrap Configuration
- Task
- MainPage
- SerialPortDescription
- system
- ParameterGroupRow
- ReferenceScript
- graph_freshness.py
- Page
- RunContext
- OsdPage
- ParameterRoleRow
- .Build
- AcceptanceStepRow
- CompassDeviceId
- CalibrationReference
- .RunPrearmChecksAsync
- Page
- Connect Handshake (HEARTBEAT, autopilot==3 gate, AUTOPILOT_VERSION)
- Write then verify procedure (PARAM_SET + independent read by name)
- Controls, Layout, and Adaptive UI
- Window
- MavParamType
- SettingsPage
- Page
- Button
- IVehicleLink
- Transfer State Machine (states 0-14)
- Windows App SDK Lifecycle, Notifications, and Deployment
- .Compose
- CalibrationCheckRow
- MavFtpOpcode
- Capability Map (requirement to owning reference)
- ParameterDifferenceRow
- Setup and Project Selection
- Accessibility, Input, and Localization
- PrearmReport
- .RenderCalibrationState
- Run (one verification session against one board under test)
- Deployment: unpackaged, self-contained, Velopack
- OsdPanelRow
- .Dispatch
- UiScale
- Telemetry Data Model (attitude, voltage, current, mode, sentinels)
- CommunityToolkit Controls and Helpers
- Startup Failure Debugging Path
- App
- Button
- ParameterEnums
- OsdScreen
- .ReceiveLoopAsync
- ReferencePackage
- UpdateService
- Classify (external/internal decision procedure)
- ARDU_OTK.csproj
- Navy + Green Brand Palette (shared app color identity)
- CompassRow
- ReferenceParamFile
- CalibrationRunResult
- ARDU OTK App Icon Mark
- StackPanel
- CalibrationStageRow
- CompassSlot
- Procedure: make external compass primary and set use flags (Phases A-F)
- .OnFormFieldChanged
- MavlinkEncoder
- ScriptDifferenceRow
- MAV_CMD_FIXED_MAG_CAL_YAW (42006)
- ARDU OTK Square 44x44 App Tile Icon (scale-200)
- Grid
- AppTheme
- LockScreenLogo.scale-200 (app lock screen logo asset)
- Square44x44Logo targetsize-24 altform-unplated (app icon asset)
- Green circular checkmark badge overlay (bottom-right corner)
- Border
- Immersive Frontend UI Craftsmanship
- StatusTextEvent
- UpdateService.IsBusy update interlock
- Square44x44Logo targetsize-48 altform-lightunplated (app tile icon 48px)
- ARDU OTK Wide Tile Logo (310x150 @200%)
- OsdValueRow
- Page
- ReferencesPage
- ParamMismatchRow
- WorkstationSettings
- InfoBar
- Sample and Source Map
- .OnRoleModeFilterChanged
- ReferenceRow
- WinUI Reference Sections Index
- Corrupted Binary Asset (UTF-8 Mojibake Re-encoding)
- ParameterDifference
- ReferenceParamSet
- .OnPortSelectionChanged
- StatusTextAssembly
- CompassBusyRing
- DiffList
- FixedHost
- PortFlyout
- PortsBelowScroll
- AppServices
- Telemetry coalescing pattern (latest-value slot + display timer)
- OsdValue
- CalibrationTolerances
- AutoConnectCheck
- CompareProgress
- CompareTickIcon
- CompareTickScale
- CompassList
- HeadingBox
- HudClip
- PitchTranslate
- RollRotate
- UnitIdBox
- CheckOutcome
- EstimatorFaultKind
- CalibrationStage
- .FromReference
- CompassCalibrationPage
- ScriptComparison
- AutopilotVersionMessage
- .OnScopeChanged
- OsdReferenceChoice
- WinUI App Skill Interface Metadata (openai.yaml)
- Apache License 2.0 (winui-app skill)

## God Nodes (most connected - your core abstractions)
1. `MainPage` - 125 edges
2. `Page` - 97 edges
3. `SerialVehicleLink` - 95 edges
4. `ReferenceEditorPage` - 64 edges
5. `CompassCalibrationPage` - 60 edges
6. `Page` - 53 edges
7. `AppServices` - 52 edges
8. `AcceptanceSession` - 51 edges
9. `Page` - 50 edges
10. `SqliteCalibrationStore` - 50 edges

## Surprising Connections (you probably didn't know these)
- `GRAPH HEALTH WARNING Check In GRAPH_REPORT.md` --semantically_similar_to--> `Version From Tag (SemVer Validation Step)`  [INFERRED] [semantically similar]
  CLAUDE.md → .github/workflows/release.yml
- `Граф знаний — первая точка входа` --semantically_similar_to--> `Skill-Led Reasoning Over Pre-Training Reasoning`  [INFERRED] [semantically similar]
  CLAUDE.md → .github/skills/README.md
- `--packTitle и --icon обязательны для паритета локальной и релизной сборки` --semantically_similar_to--> `vpk CLI Version Must Match Velopack Package Version`  [INFERRED] [semantically similar]
  README.md → .github/workflows/release.yml
- `Локальная сборка установщика` --semantically_similar_to--> `vpk pack (Installer Packaging Step)`  [INFERRED] [semantically similar]
  README.md → .github/workflows/release.yml
- `ardupilot-firmware Skill` --conceptually_related_to--> `ARDU ОТК (Product)`  [INFERRED]
  .github/skills/README.md → README.md

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Compass calibration transfer: write, verify, reboot, prove acceptance** — _github_skills_ardupilot_firmware_references_compass_calibration_transfer_transfer_state_machine, _github_skills_ardupilot_firmware_references_compass_calibration_transfer_dev_id_validity_rule, _github_skills_ardupilot_firmware_references_compass_calibration_transfer_instance_mapping, _github_skills_ardupilot_firmware_references_compass_calibration_transfer_rollback_snapshot, _github_skills_ardupilot_firmware_references_imu_level_and_health_verification_verificationverdict [EXTRACTED 1.00]
- **Sources that must report the bench busy to UpdateService.IsBusy** — _github_skills_ardupilot_firmware_references_dotnet_mavlink_and_winui_integration_update_busy_interlock, _github_skills_ardupilot_firmware_references_dotnet_mavlink_and_winui_integration_isbenchbusy, _github_skills_ardupilot_firmware_references_parameter_protocol_and_profiles_write_verify_procedure, _github_skills_ardupilot_firmware_references_dotnet_mavlink_and_winui_integration_reboot_survival, _github_skills_ardupilot_firmware_references_reference_profiles_and_storage_run, _github_skills_ardupilot_firmware_references_reference_profiles_and_storage_write_as_you_go [EXTRACTED 1.00]
- **Domain services, one per sibling reference** — _github_skills_ardupilot_firmware_references_dotnet_mavlink_and_winui_integration_iparameterservice, _github_skills_ardupilot_firmware_references_dotnet_mavlink_and_winui_integration_icompassservice, _github_skills_ardupilot_firmware_references_dotnet_mavlink_and_winui_integration_icalibrationservice, _github_skills_ardupilot_firmware_references_dotnet_mavlink_and_winui_integration_itelemetryservice, _github_skills_ardupilot_firmware_references_dotnet_mavlink_and_winui_integration_layering [EXTRACTED 1.00]
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

## Communities (145 total, 31 thin omitted)

### Community 0 - "CompassIdentity"
Cohesion: 0.15
Nodes (6): CompassIdentity, NeverWriteNames, MagAxis, X, Y, Z

### Community 1 - "SqliteCalibrationStore"
Cohesion: 0.07
Nodes (14): ICalibrationStore, HasOpenRun, SerialPortCatalog, AppPaths, BackupsDirectory, DatabaseFilePath, DataRoot, IsDevelopmentStore (+6 more)

### Community 2 - "AcceptanceSession"
Cohesion: 0.16
Nodes (10): AcceptanceSession, FirmwareBanner, IsConnected, LastBoard, LiveState, Messages, PortName, ArmReadiness (+2 more)

### Community 3 - "RunRow"
Cohesion: 0.07
Nodes (27): AbortedCountText, CountText, FailedCountText, Page, PageBar, PassedCountText, RunList, UnitFilterBox (+19 more)

### Community 4 - "Agent Skills Index"
Cohesion: 0.06
Nodes (33): Agent Skills Index, ardupilot-firmware Skill, premium-frontend-ui Skill, winui-app Skill, dotnet publish Build Step (win-x64 Release), Download Previous Releases For Delta Computation, release Workflow (GitHub Actions), Tag Push Trigger v* (+25 more)

### Community 5 - "SerialCompassCalibrationJob"
Cohesion: 0.20
Nodes (3): SerialCompassCalibrationJob, AppVersion, SlotNames

### Community 6 - "AcceptanceChecks"
Cohesion: 0.08
Nodes (23): AirData, AttitudeSample, MagSample, IsEmpty, Magnitude, SensorHealth, TelemetrySnapshot, VehicleLiveState (+15 more)

### Community 7 - "ParameterRoleMap"
Cohesion: 0.09
Nodes (12): ParameterRole, IsControlled, IsHazardousOverride, IsOverridden, ParameterRoleMap, Default, DefaultRules, HasOverrides (+4 more)

### Community 8 - "SerialVehicleLink"
Cohesion: 0.08
Nodes (8): SerialVehicleLink, DiagnosticLog, FirmwareBanner, IsConnected, LiveState, PortName, TargetComponent, TargetSystem

### Community 9 - "PayloadReader"
Cohesion: 0.15
Nodes (13): AttitudeMessage, CommandAckMessage, Gps2RawMessage, GpsRawIntMessage, HeartbeatMessage, IsAutopilot, ImuMessage, MavlinkFraming (+5 more)

### Community 10 - "Page"
Cohesion: 0.08
Nodes (43): AuthorPanel, ErrorBar, FrozenBar, GateBar, HeadingToleranceBox, MissingCoreBar, MissingMotBar, MotorCompToggle (+35 more)

### Community 11 - "ReferenceEditorPage"
Cohesion: 0.07
Nodes (15): AddScriptButton, BrowseButton, CancelButton, CollapseAllGroupsButton, ExpandAllGroupsButton, ReadScriptsButton, SaveButton, SnapshotButton (+7 more)

### Community 13 - "Page"
Cohesion: 0.10
Nodes (39): AccelCalText, CompareDiffText, CompareMatchedText, CompareSkippedText, CompareStateText, CompareTickCount, CompareTickName, CompassBusyText (+31 more)

### Community 14 - "WinUI WinGet DSC Bootstrap Configuration"
Cohesion: 0.40
Nodes (5): Enable Developer Mode (WindowsSettings resource), OsVersion Assertion (min 10.0.17763), Install Visual Studio Community 2026 (WinGetPackage), VS Workloads: ManagedDesktop, Universal, WindowsAppSDK.Cs, WinUI WinGet DSC Bootstrap Configuration

### Community 16 - "MainPage"
Cohesion: 0.08
Nodes (12): MainPage, Compasses, Differences, Ink, InkDim, LogEntries, ScriptDiffs, SelectedPort (+4 more)

### Community 17 - "SerialPortDescription"
Cohesion: 0.11
Nodes (4): SerialPortDescription, Caption, Details, LooksLikeArduPilot

### Community 18 - "system"
Cohesion: 0.07
Nodes (10): MavCommand, SysStatusSensor, ArduPilotModes, WorkstationFix, WorkstationLocator, ARDU_OTK, ARDU_OTK.Services.Fc, ARDU_OTK.Services.Store (+2 more)

### Community 19 - "ParameterGroupRow"
Cohesion: 0.06
Nodes (27): ExpectedCompassSlotRow, AmbiguousVisibility, DeviceText, EmptyVisibility, IsExternal, IsPresent, Kind, KindText (+19 more)

### Community 20 - "ReferenceScript"
Cohesion: 0.18
Nodes (5): ReferenceScript, Caption, FileName, ScriptDifference, ScriptTransfer

### Community 21 - "graph_freshness.py"
Cohesion: 0.12
Nodes (15): compare(), corpus_on_disk(), describe(), emit_hook(), force_utf8_streams(), git(), health_report(), is_corpus_path() (+7 more)

### Community 22 - "Page"
Cohesion: 0.11
Nodes (24): BoardStateText, EmptyText, GeneralList, GeneralPanel, MockHost, MockNoteText, MockSizeText, Page (+16 more)

### Community 23 - "RunContext"
Cohesion: 0.08
Nodes (21): RunContext, Checks, Fix, Inbox, Messages, Mismatches, Pending, PrearmMessages (+13 more)

### Community 25 - "OsdPage"
Cohesion: 0.12
Nodes (5): OsdPage, GeneralRows, HasReference, PanelRows, SettingRows

### Community 26 - "ParameterRoleRow"
Cohesion: 0.09
Nodes (18): ParameterRoleRow, CannotMatch, Control, HazardText, HazardVisibility, IsHazardous, IsOverridden, ModeText (+10 more)

### Community 27 - ".Build"
Cohesion: 0.16
Nodes (9): OsdFieldKind, Column, Enable, Row, OsdLayout, PanelBuilder, Column, Enable (+1 more)

### Community 29 - "AcceptanceStepRow"
Cohesion: 0.14
Nodes (13): AcceptanceStepRow, Detail, DetailVisibility, FailVisibility, PassVisibility, PendingVisibility, RunningVisibility, Title (+5 more)

### Community 30 - "CompassDeviceId"
Cohesion: 0.09
Nodes (19): CompassDeviceId, Address, Bus, BusType, DevType, IsEmpty, MavBusType, DroneCan (+11 more)

### Community 31 - "CalibrationReference"
Cohesion: 0.10
Nodes (15): ReferenceCaption, CalibrationReference, HasFirmware, HasRuns, IsRetired, ShortCaption, NewCalibrationReference, Firmware (+7 more)

### Community 33 - "Page"
Cohesion: 0.13
Nodes (21): FontScaleBox, FontScaleText, OperatorBox, Page, StandLatBox, StandLonBox, StoreBar, StorePathText (+13 more)

### Community 34 - "Connect Handshake (HEARTBEAT, autopilot==3 gate, AUTOPILOT_VERSION)"
Cohesion: 0.11
Nodes (14): Per-instance mag feed (RAW_IMU/SCALED_IMU2/SCALED_IMU3 to priority slot), Connect Handshake (HEARTBEAT, autopilot==3 gate, AUTOPILOT_VERSION), Freshness and Link Loss (staleness timeouts, degradation), Flight Mode Tables (COPTER_MODE/PLANE_MODE/ROVER_MODE by MAV_TYPE), Reading<T> (value + UpdatedUtc staleness wrapper), Stream Rate Policy via MAV_CMD_SET_MESSAGE_INTERVAL (511), Device discovery via Win32_PnPEntity / SetupAPI, WinUI 3 UI patterns and stock-first rule (+6 more)

### Community 35 - "Write then verify procedure (PARAM_SET + independent read by name)"
Cohesion: 0.11
Nodes (15): IParameterService, Clean-run rule, Comparison rules (integer exact, REAL32 relative + absolute floor), Detect (param file format detection), Diff outcome model (Match/Differs/MissingOnBoard/NotInReference/Excluded/ReadOnly/Coalesced/ReadFailed), Exportable diff report, MAV_PARAM_TYPE handling and C-cast integer encoding, Reference file formats: Mission Planner .param/.parm and QGC .params (+7 more)

### Community 36 - "Controls, Layout, and Adaptive UI"
Cohesion: 0.21
Nodes (11): Explicit Adaptive Breakpoint Intent, Controls, Layout, and Adaptive UI, Phone-Width Single-Column Layout Plan, Remove Redundant Outer Section Borders, Single Main Shell Window Owning Navigation, Simpler Visual Trees and Lighter Templates, Custom Title Bar as Functional Chrome, Narrow/Phone-Width Navigation Mode (+3 more)

### Community 37 - "Window"
Cohesion: 0.13
Nodes (13): AppTitleBar, OsdItem, ReferencesItem, RootFrame, RootNav, RunsItem, StandItem, Window (+5 more)

### Community 38 - "MavParamType"
Cohesion: 0.12
Nodes (15): MavParamType, Int16, Int32, Int8, Real32, ParamValue, IsInteger, PendingWrite (+7 more)

### Community 40 - "Page"
Cohesion: 0.06
Nodes (51): AzimuthBar, AzimuthBox, ChecksList, ChecksSummaryText, ErrorBar, GateBar, HistoryCard, HistoryHintText (+43 more)

### Community 41 - "Button"
Cohesion: 0.29
Nodes (6): BrowseButton, CancelButton, RefreshHistoryButton, RefreshPortsButton, StartButton, Button

### Community 42 - "IVehicleLink"
Cohesion: 0.06
Nodes (25): GpsFix, AltitudeMeters, FixTypeText, Hdop, Is3D, LatitudeDeg, LongitudeDeg, Vdop (+17 more)

### Community 43 - "Transfer State Machine (states 0-14)"
Cohesion: 0.16
Nodes (10): Compass::force_save_calibration() path (UNVERIFIED), Rollback Snapshot (pre-write .param capture), Transfer State Machine (states 0-14), Workflow (a): Compass Calibration Transfer, Reboot survival and COM re-enumeration, Thirteen safety rules for a tool that writes to flight hardware, Ordered board-came-back detection (heartbeat gap, banner, time_boot_ms), MAV_CMD_PREFLIGHT_REBOOT_SHUTDOWN (246), param1=1 (+2 more)

### Community 44 - "Windows App SDK Lifecycle, Notifications, and Deployment"
Cohesion: 0.22
Nodes (9): Hidden Package-Identity Assumptions, Packaged vs Unpackaged Launch Rules, C#-First Folder Split (Pages, Controls, ViewModels, Services, Styles, Assets), WindowsAppSDK-Samples, AppWindow and Windows App SDK Windowing, AppLifecycle Activation, Instancing, and Restart, Bootstrapper and Runtime Initialization for Unpackaged Apps, Push and App Notifications via Samples (+1 more)

### Community 46 - "CalibrationCheckRow"
Cohesion: 0.09
Nodes (21): CalibrationCheckRow, Detail, FailVisibility, HeaderText, InconclusiveVisibility, MeasuredText, MeasuredVisibility, PassVisibility (+13 more)

### Community 47 - "MavFtpOpcode"
Cohesion: 0.05
Nodes (37): MavFtpDirectory, MavFtpEntry, MavFtpError, EndOfFile, Fail, FailErrno, FileExists, FileNotFound (+29 more)

### Community 48 - "Capability Map (requirement to owning reference)"
Cohesion: 0.17
Nodes (10): Reference Index, Transferable Parameter Classification (COPY / NEVER COPY / OPT-IN), Compass Parameter Map (per-instance lookup tables), Built-in default exclusions (Mission Planner skip list), Comparison profile JSON schema (the configurable block), Rule resolution order (first matching include wins, then excludes), Compass-calibration block (CalBlock), Reference profile (snapshot + comparison revision + optional cal block) (+2 more)

### Community 49 - "ParameterDifferenceRow"
Cohesion: 0.09
Nodes (18): ParameterDifferenceRow, ActualText, CanWrite, Detail, DiffVisibility, ExpectedText, MatchVisibility, Name (+10 more)

### Community 50 - "Setup and Project Selection"
Cohesion: 0.16
Nodes (13): Developer Mode as Optional-Not-Universal Requirement, Environment Audit and Remediation, Manual Non-Mutating Readiness Audit, Required WinUI Prerequisite Baseline, Setup-and-Scaffold Flow (SKILL.md), C#-First WinUI 3 Desktop App on Windows App SDK, Setup and Project Selection, Setup Baseline Versions (Win10 1809, SDK 19041, .NET) (+5 more)

### Community 51 - "Accessibility, Input, and Localization"
Cohesion: 0.24
Nodes (11): Accessibility, Input, and Localization, Automation Properties and Accessible Naming, Mouse, Touch, Pen, and Keyboard Input Parity, Localization and RTL Readiness, Narrator Support, Acrylic for Transient Surfaces, Mica for Long-Lived Base Layers, Styling, Theming, Materials, and Icons (+3 more)

### Community 52 - "PrearmReport"
Cohesion: 0.18
Nodes (4): PrearmReport, CompassMessages, EstimatorDiagnosis, EstimatorReadiness

### Community 53 - ".RenderCalibrationState"
Cohesion: 0.24
Nodes (3): CalibrationStatus, CalibrationVerdict, Unconfirmed

### Community 54 - "Run (one verification session against one board under test)"
Cohesion: 0.15
Nodes (7): PREARM_CHECK bit (0x10000000), SYS_STATUS health bits (present && enabled && health), VerificationVerdict record, ParamWriteAudit table (append-only write audit log), Run (one verification session against one board under test), Unit (operator-entered board id vs auto-detected hardware identity), Work history (list, filtering, drill-down, per-unit timeline, export)

### Community 55 - "Deployment: unpackaged, self-contained, Velopack"
Cohesion: 0.15
Nodes (6): MAVLink library choice: Asv.Mavlink, Deployment: unpackaged, self-contained, Velopack, Backup, export and portability, SQLite store via Microsoft.Data.Sqlite (WAL, foreign_keys ON), Startup order after an update (8 fixed steps), App facts: net10.0-windows, unpackaged, Velopack, PublishTrimmed=False

### Community 56 - "OsdPanelRow"
Cohesion: 0.09
Nodes (20): OsdPanelRow, Caption, ColumnText, DiffVisibility, EnableText, MatchVisibility, ReferenceLineText, ReferenceLineVisibility (+12 more)

### Community 58 - "UiScale"
Cohesion: 0.27
Nodes (3): UiScale, Current, UiState

### Community 59 - "Telemetry Data Model (attitude, voltage, current, mode, sentinels)"
Cohesion: 0.18
Nodes (8): Telemetry Data Model (attitude, voltage, current, mode, sentinels), ICalibrationService, ICompassService, ITelemetryService, Six-layer one-directional stack (transport to XAML), AHRS_TRIM_X/Y/Z (what level calibration writes), Level calibration preconditions and gating (7 rows), Level (trim) calibration: MAV_CMD_PREFLIGHT_CALIBRATION param5=2

### Community 60 - "CommunityToolkit Controls and Helpers"
Cohesion: 0.22
Nodes (7): Full Keyboard Reachability and Focus Order, CommunityToolkit Controls and Helpers, Toolkit HeaderedControls, Toolkit Segmented Control, Toolkit SettingsControls, Toolkit Animations Package, Code Review Checklist

### Community 61 - "Startup Failure Debugging Path"
Cohesion: 0.19
Nodes (9): High-Contrast-Safe Visuals, Build, Run, and Launch Verification, Startup Failure Debugging Path, dotnet new winui Comparison Scaffold, Opaque MSB3073 / XamlCompiler.exe Failures, Template-First Recovery Loop, Template-First Recovery for Startup and XAML Failures, Avoid Window.Current in WinUI 3 Startup (+1 more)

### Community 62 - "App"
Cohesion: 0.21
Nodes (5): Application, App, CrashLogPath, MainWindow, Program

### Community 63 - "Button"
Cohesion: 0.17
Nodes (12): AcceptDiffButton, ApplyAllScriptsButton, CompassCalButton, EditReferenceButton, FinishButton, LevelButton, LinkToggleButton, PortCard (+4 more)

### Community 64 - "ParameterEnums"
Cohesion: 0.24
Nodes (5): ParameterEnums, VehicleClass, Copter, Plane, Unknown

### Community 65 - "OsdScreen"
Cohesion: 0.15
Nodes (7): OsdConfiguration, IsEmpty, OsdScreen, DefectCount, Overflowing, ShownCount, OsdScreenSize

### Community 66 - ".ReceiveLoopAsync"
Cohesion: 0.22
Nodes (3): MavlinkCrc, MavlinkParser, DroppedFrames

### Community 67 - "ReferencePackage"
Cohesion: 0.08
Nodes (19): ReferencePackage, CreatedBy, CreatedUtc, Description, ExportedBy, ExportedUtc, Firmware, HeadingVsJigDeg (+11 more)

### Community 68 - "UpdateService"
Cohesion: 0.08
Nodes (16): UpdateService, CurrentVersion, IsBusy, LastError, PendingVersion, State, UpdateState, Checking (+8 more)

### Community 69 - "Classify (external/internal decision procedure)"
Cohesion: 0.22
Nodes (10): MAG_CAL_REPORT.fitness judged against COMPASS_CAL_FIT (x2 rule), Instance Mapping by decoded device id, BusType enum (0-7, no EXTERNALAHRS), Classify (external/internal decision procedure), CompassDevId (DEV_ID bitfield decode), CompassRow (compass panel per-instance view model), Compass devtype table (AP_Compass_Backend.h authoritative), COMPASS_EXTERNAL flag semantics (2 = ForcedExternal operator lock) (+2 more)

### Community 70 - "ARDU_OTK.csproj"
Cohesion: 0.18
Nodes (9): net10.0-windows10.0.26100.0, Microsoft.Data.Sqlite (10.0.10), Microsoft.Windows.SDK.BuildTools (10.0.28000.2526), Microsoft.WindowsAppSDK (2.3.1), SQLitePCLRaw.bundle_e_sqlite3 (3.0.5), System.IO.Ports (10.0.10), System.Management (10.0.10), Velopack (1.2.0) (+1 more)

### Community 71 - "Navy + Green Brand Palette (shared app color identity)"
Cohesion: 0.36
Nodes (9): ARDU OTK Splash Screen Image (scale-200), App Launch Branding Surface (splash shown during startup), Badge-Overlay Icon Pattern (base subject + status badge on corner), Dark Blue Rounded-Square App Tile with Gradient, Green Checkmark Badge Overlay (bottom-right quadrant), Navy + Green Brand Palette (shared app color identity), Quadcopter Rotor Glyph (four white rotor rings on rounded dark-blue square), Transparent Wide Canvas with Centered Logo (splash letterbox layout) (+1 more)

### Community 72 - "CompassRow"
Cohesion: 0.10
Nodes (19): CompassIdentityRow, CompassRow, Detail, DeviceText, ExternalVisibility, FieldText, Instance, InternalVisibility (+11 more)

### Community 74 - "CalibrationRunResult"
Cohesion: 0.14
Nodes (13): CalibrationRunResult, Mismatches, PortName, RunId, ParamMismatch, ParamWriteRecord, RunSummary, WriteOutcome (+5 more)

### Community 75 - "ARDU OTK App Icon Mark"
Cohesion: 0.29
Nodes (8): ARDU OTK App Icon Mark, Brand Palette: Navy #1B3A5C + Accent Green #22B14C, Green Checkmark Badge (bottom-right overlay), MSIX scale-200 Asset Naming Convention, Navy Gradient Rounded-Square Backplate, White Node-Graph Glyph (three connected nodes), Package.appxmanifest Tile Declaration (implied consumer), Square150x150Logo.scale-200 (Medium Tile Asset)

### Community 76 - "StackPanel"
Cohesion: 0.20
Nodes (10): CompassBusyPanel, FcSelector, PortFlyoutRoot, PortsAbove, PortsBelow, ReferenceFlyoutRoot, ReferencesAbove, ReferencesBelow (+2 more)

### Community 77 - "CalibrationStageRow"
Cohesion: 0.13
Nodes (15): CalibrationStageRow, FailVisibility, InconclusiveVisibility, PassVisibility, PendingVisibility, RunningVisibility, Stage, StateText (+7 more)

### Community 78 - "CompassSlot"
Cohesion: 0.20
Nodes (9): CompassSlot, IsPresent, OffsetMagnitude, CompassTopologyVerdict, ExternalKind, Ambiguous, External, ExternalLocked (+1 more)

### Community 79 - "Procedure: make external compass primary and set use flags (Phases A-F)"
Cohesion: 0.25
Nodes (7): Handoff to verification (exact conditions), Procedure: make external compass primary and set use flags (Phases A-F), COMPASS_PRIOx_ID Priority Model, STATUSTEXT chunk reassembly (id + chunk_seq) before string matching, STATUSTEXT Ingestion and MAV_SEVERITY inversion, MAV_CMD_RUN_PREARM_CHECKS (401) and its collection window, Prearm catalogue (exact IMU and compass PreArm strings)

### Community 80 - ".OnFormFieldChanged"
Cohesion: 0.31
Nodes (6): AuthorBox, DescriptionBox, NameBox, ReferencePathBox, RoleFilterBox, TextBox

### Community 81 - "MavlinkEncoder"
Cohesion: 0.31
Nodes (3): MavlinkEncoder, ComponentId, SystemId

### Community 82 - "ScriptDifferenceRow"
Cohesion: 0.12
Nodes (15): ScriptDifferenceRow, ActionText, ActionVisibility, CanApply, Detail, DiffVisibility, FileName, HashText (+7 more)

### Community 83 - "MAV_CMD_FIXED_MAG_CAL_YAW (42006)"
Cohesion: 0.25
Nodes (7): MAG_CAL_STATUS enum (with ArduPilot extensions 6-10), Onboard mag cal commands (DO_START/ACCEPT/CANCEL_MAG_CAL), MAV_CMD_FIXED_MAG_CAL_YAW (42006), _reset_compass_id() side effect on priority slots, Workflow (b): Fixed-Yaw / Large-Vehicle Calibration, ATTITUDE.yaw and VFR_HUD.heading are TRUE north, CalibrationOp table (command id, params sent, MAV_RESULT, STATUSTEXT)

### Community 84 - "ARDU OTK Square 44x44 App Tile Icon (scale-200)"
Cohesion: 0.50
Nodes (7): ARDU OTK Square 44x44 App Tile Icon (scale-200), Badge-Overlay Icon Composition Pattern (base glyph + status badge), Dark Blue Rounded-Square Tile Background, Green Circular Checkmark Badge Overlay, White Node-Graph / Network Topology Glyph, scale-200 Resource Density Qualifier (88x88 px effective), Windows App Icon Asset Set (scale-qualified logo variants)

### Community 85 - "Grid"
Cohesion: 0.25
Nodes (6): CompareActionsPanel, CompareCountsPanel, CompareTickPanel, HudViewport, PanelsRow, Grid

### Community 86 - "AppTheme"
Cohesion: 0.24
Nodes (8): AppTheme, Dark, Light, System, UiTheme, Applied, Current, RestartRequired

### Community 87 - "LockScreenLogo.scale-200 (app lock screen logo asset)"
Cohesion: 0.52
Nodes (5): Green checkmark badge overlay (QC pass indicator), LockScreenLogo.scale-200 (app lock screen logo asset), Node-graph glyph (three connected blue circles), scale-200 density variant (Windows packaging asset naming convention), Windows app manifest lock-screen logo asset slot

### Community 88 - "Square44x44Logo targetsize-24 altform-unplated (app icon asset)"
Cohesion: 0.52
Nodes (6): Square44x44Logo targetsize-24 altform-unplated (app icon asset), ARDU OTK visual brand identity (dark navy + white check), Checkmark glyph on dark rounded square, MSIX / WinUI app package manifest asset set, targetsize-24 asset scaling convention, Unplated altform variant (transparent-background taskbar icon)

### Community 89 - "Green circular checkmark badge overlay (bottom-right corner)"
Cohesion: 0.52
Nodes (5): ARDU OTK visual brand identity: connected devices verified by QC, Green circular checkmark badge overlay (bottom-right corner), StoreLogo.png — Microsoft Store tile logo for ARDU OTK, MSIX/WinUI packaging asset convention (Assets/ logo set), Dark-blue node-and-link (molecule/network) motif

### Community 90 - "Border"
Cohesion: 0.29
Nodes (7): AccelCalCard, GyroCalCard, HudCard, MagCalCard, PortGlow, ReferenceGlow, Border

### Community 91 - "Immersive Frontend UI Craftsmanship"
Cohesion: 0.12
Nodes (15): 1. Establishing the Creative Foundation, 2.1 The Entry Sequence (Preloading & Initialization), 2.2 The Hero Architecture, 2.3 Fluid & Contextual Navigation, 2. Structural Requirements for Immersive UI, 3.1 Scroll-Driven Narratives, 3.2 High-Fidelity Micro-Interactions, 3. The Motion Design System (+7 more)

### Community 92 - "StatusTextEvent"
Cohesion: 0.13
Nodes (10): MavSeverity, Alert, Critical, Debug, Emergency, Error, Info, Notice (+2 more)

### Community 93 - "UpdateService.IsBusy update interlock"
Cohesion: 0.33
Nodes (4): IsBenchBusy predicate (fail-safe to busy), SITL testing scope and its limits, UpdateService.IsBusy update interlock, Interrupted-run sweep to Verdict='aborted'

### Community 94 - "Square44x44Logo targetsize-48 altform-lightunplated (app tile icon 48px)"
Cohesion: 0.60
Nodes (4): Green check-mark badge overlay (pass / QC accepted), Square44x44Logo targetsize-48 altform-lightunplated (app tile icon 48px), MSIX/UWP asset naming convention (Square44x44Logo.targetsize-N_altform-*), Blue node-graph glyph (connected circles / network of sensors)

### Community 95 - "ARDU OTK Wide Tile Logo (310x150 @200%)"
Cohesion: 0.53
Nodes (4): Dark Navy Blue Brand Palette, Green Checkmark QC Badge, Quadcopter/Drone Glyph Mark, ARDU OTK Wide Tile Logo (310x150 @200%)

### Community 96 - "OsdValueRow"
Cohesion: 0.15
Nodes (10): OsdRowState, OsdValueRow, DiffVisibility, MatchVisibility, Name, ReferenceLineText, ReferenceLineVisibility, Tip (+2 more)

### Community 97 - "Page"
Cohesion: 0.21
Nodes (8): ReferenceEditorArgs, CountText, Page, PageBar, ReferenceList, InfoBar, ListView, TextBlock

### Community 100 - "ParamMismatchRow"
Cohesion: 0.17
Nodes (10): ParamMismatchRow, ActualText, CanRewrite, Detail, ExpectedText, Name, OutcomeText, OutcomeVisibility (+2 more)

### Community 101 - "WorkstationSettings"
Cohesion: 0.17
Nodes (10): WorkstationSettings, AutoConnect, DefaultOperator, HasStandPosition, IsComplete, LastPortName, LastReferenceId, Problems (+2 more)

### Community 102 - "InfoBar"
Cohesion: 0.50
Nodes (4): LinkBar, ReadyBar, ReferenceBar, InfoBar

### Community 103 - "Sample and Source Map"
Cohesion: 0.24
Nodes (9): Virtualization-Friendly Collection Controls, Performance, Diagnostics, and Responsiveness, Keep the UI Thread Free, WPR/WPA with XAML Frame Analysis, Sample and Source Map, Performance Checklist, Required Verification Loop, Runtime Verification at Multiple Breakpoints (+1 more)

### Community 105 - "ReferenceRow"
Cohesion: 0.18
Nodes (10): ReferenceRow, CanEdit, Id, IsRetired, Name, OriginText, RetireActionText, StateBadgeVisibility (+2 more)

### Community 106 - "WinUI Reference Sections Index"
Cohesion: 0.22
Nodes (7): WinUI Reference Sections Index, WinUI App Structure, Connected Animation, Motion, Animations, and Polish, CommunityToolkit/Windows Repository, Microsoft Learn Windows Apps Docs, WinUI Gallery (microsoft/WinUI-Gallery)

### Community 108 - "ParameterDifference"
Cohesion: 0.20
Nodes (9): ChannelField, ParameterDifference, ParameterDiffKind, Differs, MissingOnBoard, NotInReference, ParameterTransferPlan, IsClean (+1 more)

### Community 109 - "ReferenceParamSet"
Cohesion: 0.33
Nodes (3): ReferenceParamSet, ExpectedCompassSlot, IsPresent

### Community 111 - "StatusTextAssembly"
Cohesion: 0.22
Nodes (6): StatusTextMessage, StatusTextAssembly, NextChunk, Severity, StartedUtc, Text

### Community 112 - "CompassBusyRing"
Cohesion: 0.67
Nodes (3): CompassBusyRing, LinkRing, ProgressRing

### Community 113 - "DiffList"
Cohesion: 0.67
Nodes (3): DiffList, ScriptList, ListView

### Community 114 - "FixedHost"
Cohesion: 0.67
Nodes (3): FixedHost, LadderHost, Canvas

### Community 115 - "PortFlyout"
Cohesion: 0.67
Nodes (3): PortFlyout, ReferenceFlyout, Flyout

### Community 116 - "PortsBelowScroll"
Cohesion: 0.67
Nodes (3): PortsBelowScroll, ReferencesBelowScroll, ScrollViewer

### Community 117 - "AppServices"
Cohesion: 0.11
Nodes (16): AppServices, ConnectedFirmware, ConnectedPort, Instance, IsAcceptanceRunning, IsLinkConnected, IsReadingParameters, LastParameters (+8 more)

### Community 119 - "OsdValue"
Cohesion: 0.22
Nodes (7): OsdValue, BoardCell, BoardText, Differs, IsDefect, ReferenceCell, ReferenceText

### Community 120 - "CalibrationTolerances"
Cohesion: 0.25
Nodes (6): CalibrationTolerances, HeadingVsJigDeg, InterCompassSpreadDeg, ParamVerifyTolerance, PrearmWindow, TelemetryWindow

### Community 131 - "CheckOutcome"
Cohesion: 0.25
Nodes (4): CheckOutcome, Fail, Inconclusive, Pass

### Community 132 - "EstimatorFaultKind"
Cohesion: 0.25
Nodes (8): EstimatorFaultKind, BlockedByComplaints, Disabled, HardwareMismatch, Missing, None, Settling, Unknown

### Community 133 - "CalibrationStage"
Cohesion: 0.13
Nodes (13): PageProgress, CalibrationStage, Acceptance, Connect, FixedYaw, GpsFix, Protocol, Reboot (+5 more)

### Community 134 - ".FromReference"
Cohesion: 0.33
Nodes (4): ReferencePackageScript, Hash, Path, Text

### Community 135 - "CompassCalibrationPage"
Cohesion: 0.08
Nodes (13): NewRunButton, RewriteAllButton, CompassCalibrationPage, Checks, History, LoadHistory, LogEntries, Mismatches (+5 more)

### Community 136 - "ScriptComparison"
Cohesion: 0.33
Nodes (6): ScriptComparison, ContentDiffers, ExtraOnBoard, Match, MissingOnBoard, Unreadable

### Community 137 - "AutopilotVersionMessage"
Cohesion: 0.40
Nodes (5): AutopilotVersionMessage, HasVersion, Major, Minor, Patch

### Community 141 - "OsdReferenceChoice"
Cohesion: 0.67
Nodes (3): OsdReferenceChoice, Caption, Reference

## Ambiguous Edges - Review These
- `premium-frontend-ui Skill` → `winui-app Skill`  [AMBIGUOUS]
  .github/skills/README.md · relation: semantically_similar_to
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
- **635 isolated node(s):** `net10.0-windows10.0.26100.0`, `Microsoft.Windows.SDK.BuildTools (10.0.28000.2526)`, `Microsoft.WindowsAppSDK (2.3.1)`, `Velopack (1.2.0)`, `Microsoft.Data.Sqlite (10.0.10)` (+630 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 881 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **31 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **What is the exact relationship between `premium-frontend-ui Skill` and `winui-app Skill`?**
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
- **What is the exact relationship between `Quadcopter Rotor Glyph (four white rotor rings on rounded dark-blue square)` and `Navy + Green Brand Palette (shared app color identity)`?**
  _Edge tagged AMBIGUOUS (relation: conceptually_related_to) - confidence is low._