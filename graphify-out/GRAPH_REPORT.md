# Graph Report - .  (2026-08-11)

## Corpus Check
- 83 files · ~118,961 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 1285 nodes · 2658 edges · 71 communities (63 shown, 8 thin omitted)
- Extraction: 91% EXTRACTED · 8% INFERRED · 1% AMBIGUOUS · INFERRED: 223 edges (avg confidence: 0.82)
- Token cost: 543,056 input · 0 output

## Community Hubs (Navigation)
- Compass Calibration Job Engine
- Serial MAVLink Vehicle Link
- SQLite Calibration Store
- Reference Parameter Profiles
- Profile Editor Page
- MAVLink Protocol Codec
- Acceptance Check Engine
- Release and Repo Governance
- Compass Panel Markup
- Run History Markup
- Update Service and Settings
- Profiles Page
- Compass Calibration Page
- Main Page Connection Flow
- Telemetry Connection Doctrine
- Parameter Write and Diff Rules
- Build and Debug Practices
- WinUI Shell Structure
- Compass Transfer State Machine
- Skill Capability Map
- Namespaces and Entry Files
- Main Window Navigation
- Accessibility and Theming
- Layout and Performance Review
- Calibration Row View Models
- Verification Verdict Model
- Deployment and Storage Model
- Main Page Card Interactions
- Service Interfaces and Layering
- Compass Identity Decoding
- WinUI Skill Routing
- Splash Screen Branding
- WinUI Environment Bootstrap
- Project and Package Dependencies
- Medium Tile Icon Branding
- Compass Priority and Prearm
- Creative Design Foundation
- CommunityToolkit Usage Rules
- Profile Row Models
- Serial Port Catalog
- Fixed-Yaw Magnetometer Calibration
- Motion and Performance System
- Scroll Narrative Ecosystem
- Packaging and Lifecycle Rules
- WinUI Setup Prerequisites
- App Startup Host
- Square Tile Icon Branding
- Lock Screen Logo Branding
- Unplated Icon Variant Branding
- Store Logo Branding
- Operator Form Fields
- Run Control Buttons
- Stage Row Building
- Bench Busy Interlock
- Premium Frontend UI Skill
- Light Unplated Icon Branding
- Wide Tile Logo Branding
- Tile Builder Helpers
- ArduPilot Flight Modes
- Port Selection Control
- Status InfoBars
- Program Entry Point
- Store and Link Exceptions
- WinUI Skill Icon Asset
- Adaptive Visual States
- UI Thread Coalescing
- Run Progress Bar
- Run Progress Ring
- Link Progress Ring
- Pitch Translate Transform
- Roll Rotate Transform

## God Nodes (most connected - your core abstractions)
1. `SerialVehicleLink` - 71 edges
2. `SqliteCalibrationStore` - 49 edges
3. `SerialCompassCalibrationJob` - 47 edges
4. `Page` - 42 edges
5. `CompassCalibrationPage` - 42 edges
6. `Page` - 40 edges
7. `MainPage` - 38 edges
8. `CompassIdentity` - 34 edges
9. `Page` - 31 edges
10. `ProfileEditorPage` - 29 edges

## Surprising Connections (you probably didn't know these)
- `GRAPH HEALTH WARNING Check In GRAPH_REPORT.md` --semantically_similar_to--> `Version From Tag (SemVer Validation Step)`  [INFERRED] [semantically similar]
  CLAUDE.md → .github/workflows/release.yml
- `Граф знаний — первая точка входа` --semantically_similar_to--> `Skill-Led Reasoning Over Pre-Training Reasoning`  [INFERRED] [semantically similar]
  CLAUDE.md → .github/skills/README.md
- `--packTitle и --icon обязательны для паритета локальной и релизной сборки` --semantically_similar_to--> `vpk CLI Version Must Match Velopack Package Version`  [INFERRED] [semantically similar]
  README.md → .github/workflows/release.yml
- `Локальная сборка установщика` --semantically_similar_to--> `vpk pack (Installer Packaging Step)`  [INFERRED] [semantically similar]
  README.md → .github/workflows/release.yml
- `Unpackaged Self-Contained Deployment Model` --semantically_similar_to--> `Модель развёртывания (unpackaged, self-contained, %LocalAppData%)`  [INFERRED] [semantically similar]
  AGENTS.md → README.md

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

## Communities (71 total, 8 thin omitted)

### Community 0 - "Compass Calibration Job Engine"
Cohesion: 0.05
Nodes (43): PageProgress, Action, GpsFix, IVehicleLink, MavParamType, MavResult, MavSeverity, ParamValue (+35 more)

### Community 1 - "Serial MAVLink Vehicle Link"
Cohesion: 0.05
Nodes (44): MavlinkFrame, FrameSubscription, MagAccumulator, SerialVehicleLink, StatusTextAssembly, TelemetrySession, Action, bool (+36 more)

### Community 2 - "SQLite Calibration Store"
Cohesion: 0.07
Nodes (34): AppServices, bool, CancellationToken, CompassSnapshot, IReadOnlyList, Task, VehicleLiveState, RunSummary (+26 more)

### Community 3 - "Reference Parameter Profiles"
Cohesion: 0.06
Nodes (25): CompassDeviceId, CompassSlot, MavBusType, ReferenceParamSet, CompassFieldComparison, CompassIdentity, CompassSlotComparison, CompassTopologyVerdict (+17 more)

### Community 4 - "Profile Editor Page"
Cohesion: 0.05
Nodes (50): AuthorBox, AuthorPanel, BrowseButton, CancelButton, DescriptionBox, ErrorBar, FrozenBar, GateBar (+42 more)

### Community 5 - "MAVLink Protocol Codec"
Cohesion: 0.08
Nodes (21): AttitudeMessage, CommandAckMessage, GpsRawIntMessage, HeartbeatMessage, ImuMessage, MavlinkCrc, MavlinkEncoder, MavlinkFraming (+13 more)

### Community 6 - "Acceptance Check Engine"
Cohesion: 0.09
Nodes (25): AttitudeSample, MagSample, MavCommand, PrearmReport, SensorHealth, StatusTextEvent, SysStatusSensor, TelemetrySnapshot (+17 more)

### Community 7 - "Release and Repo Governance"
Cohesion: 0.06
Nodes (47): Agent Skills Index, ardupilot-firmware Skill, premium-frontend-ui Skill, Skill-Led Reasoning Over Pre-Training Reasoning, winui-app Skill, dotnet publish Build Step (win-x64 Release), vpk CLI Version Must Match Velopack Package Version, Download Previous Releases For Delta Computation (+39 more)

### Community 8 - "Compass Panel Markup"
Cohesion: 0.09
Nodes (34): CompassHintText, CompassList, FcSelector, HorizonInner, HudArmedText, HudCard, HudCurrentText, HudModeBigText (+26 more)

### Community 9 - "Run History Markup"
Cohesion: 0.09
Nodes (33): AzimuthBar, ChecksList, ChecksSummaryText, ErrorBar, GateBar, HistoryCard, HistoryHintText, HistoryList (+25 more)

### Community 10 - "Update Service and Settings"
Cohesion: 0.08
Nodes (23): UpdateService, UpdateState, CancellationToken, Func, string, Task, Page, StoreBar (+15 more)

### Community 11 - "Profiles Page"
Cohesion: 0.10
Nodes (20): CreateButton, EditButton, EmptyPanel, ErrorBar, Page, ProfilesList, RetireButton, ShowRetiredCheck (+12 more)

### Community 12 - "Compass Calibration Page"
Cohesion: 0.14
Nodes (13): BrowseButton, CompassCalibrationPage, bool, CancellationToken, CancellationTokenSource, Exception, Func, IReadOnlyList (+5 more)

### Community 13 - "Main Page Connection Flow"
Cohesion: 0.18
Nodes (9): MainPage, bool, EventArgs, IReadOnlyList, List, ObservableCollection, Task, DispatcherTimer (+1 more)

### Community 14 - "Telemetry Connection Doctrine"
Cohesion: 0.11
Nodes (19): Per-instance mag feed (RAW_IMU/SCALED_IMU2/SCALED_IMU3 to priority slot), Baud is a no-op over USB CDC, Connect Handshake (HEARTBEAT, autopilot==3 gate, AUTOPILOT_VERSION), Freshness and Link Loss (staleness timeouts, degradation), Flight Mode Tables (COPTER_MODE/PLANE_MODE/ROVER_MODE by MAV_TYPE), Reading<T> (value + UpdatedUtc staleness wrapper), On Copter every SRn_* group defaults to 0 Hz; intervals do not survive reboot, Stream Rate Policy via MAV_CMD_SET_MESSAGE_INTERVAL (511) (+11 more)

### Community 15 - "Parameter Write and Diff Rules"
Cohesion: 0.11
Nodes (19): IParameterService, Clean-run rule, Comparison rules (integer exact, REAL32 relative + absolute floor), Detect (param file format detection), Diff outcome model (Match/Differs/MissingOnBoard/NotInReference/Excluded/ReadOnly/Coalesced/ReadFailed), Exportable diff report, MAV_PARAM_TYPE handling and C-cast integer encoding, Reference file formats: Mission Planner .param/.parm and QGC .params (+11 more)

### Community 16 - "Build and Debug Practices"
Cohesion: 0.15
Nodes (19): Build, Run, and Launch Verification, Explicit x64 Platform Target for Local Verification, Objective Launch Verification Evidence, Startup Failure Debugging Path, Standard Blank App Template First, dotnet new winui Comparison Scaffold, Opaque MSB3073 / XamlCompiler.exe Failures, Template-First Recovery Loop (+11 more)

### Community 17 - "WinUI Shell Structure"
Cohesion: 0.14
Nodes (17): Choose the Narrowest Reference File, WinUI Reference Sections Index, CommandBar as Native Command Surface, Single Main Shell Window Owning Navigation, WinUI App Structure, x:Bind vs Binding Guidance, Connected Animation, Motion, Animations, and Polish (+9 more)

### Community 18 - "Compass Transfer State Machine"
Cohesion: 0.16
Nodes (16): DEV_ID Validity Rule (read-back equality is not proof), Compass::force_save_calibration() path (UNVERIFIED), Rollback Snapshot (pre-write .param capture), Soft-iron destruction conflict (fixed-yaw forces DIA=1,1,1 / ODI=0,0,0), Transfer State Machine (states 0-14), Workflow (a): Compass Calibration Transfer, Reboot survival and COM re-enumeration, Thirteen safety rules for a tool that writes to flight hardware (+8 more)

### Community 19 - "Skill Capability Map"
Cohesion: 0.17
Nodes (15): Reference Index, Transferable Parameter Classification (COPY / NEVER COPY / OPT-IN), COMPASS_DIA* default version trap (0 in 4.1, 1.0 in master), COMPASS_MOT*/COMPASS_MOTCT opt-in gating rule, Compass Parameter Map (per-instance lookup tables), Irregular compass parameter naming (EXTERNAL to EXTERN2/3, middle-digit OFS2_X), Built-in default exclusions (Mission Planner skip list), Comparison profile JSON schema (the configurable block) (+7 more)

### Community 20 - "Namespaces and Entry Files"
Cohesion: 0.19
Nodes (8): CompassRow, double, Visibility, ProfileEditorArgs, ARDU_OTK, ARDU_OTK.Services.Store, ARDU_OTK.Services.Fc.Mavlink, ARDU_OTK.Services

### Community 21 - "Main Window Navigation"
Cohesion: 0.13
Nodes (12): AppTitleBar, RootFrame, RootNav, StandItem, Window, MainWindow, NavigationView, NavigationViewSelectionChangedEventArgs (+4 more)

### Community 22 - "Accessibility and Theming"
Cohesion: 0.20
Nodes (14): Accessibility, Input, and Localization, Automation Properties and Accessible Naming, High-Contrast-Safe Visuals, Mouse, Touch, Pen, and Keyboard Input Parity, Localization and RTL Readiness, Narrator Support, Custom Title Bar as Functional Chrome, Acrylic for Transient Surfaces (+6 more)

### Community 23 - "Layout and Performance Review"
Cohesion: 0.22
Nodes (14): Explicit Adaptive Breakpoint Intent, Controls, Layout, and Adaptive UI, Single Search Field with Live Filtering, Phone-Width Single-Column Layout Plan, Remove Redundant Outer Section Borders, Explicit Scroll Ownership for Collections, Virtualization-Friendly Collection Controls, Simpler Visual Trees and Lighter Templates (+6 more)

### Community 24 - "Calibration Row View Models"
Cohesion: 0.24
Nodes (9): CalibrationCheckRow, CalibrationHistoryRow, CalibrationLogRow, CalibrationStageRow, StageRowState, string, Visibility, CheckOutcome (+1 more)

### Community 25 - "Verification Verdict Model"
Cohesion: 0.15
Nodes (13): COMPASS_TYPEMASK to COMPASS_DISBLMSK rename: probe both names, ARMING_CHECK vs ARMING_SKIPCHK inverted polarity: probe by name, A clean run can be an artefact of disabled check bits, PREARM_CHECK bit (0x10000000), SYS_STATUS health bits (present && enabled && health), VerificationVerdict record, ChecksEnabledJson: a clean result must not be mistaken for a complete one, ParamWriteAudit table (append-only write audit log) (+5 more)

### Community 26 - "Deployment and Storage Model"
Cohesion: 0.15
Nodes (13): MAVLink library choice: Asv.Mavlink, Deployment: unpackaged, self-contained, Velopack, EnableMsixTooling kept for the XAML resource copy (0xC000027B), There is no serialcommunication capability question in this app, PublishTrimmed=False is deliberate (WinUI resolves by reflection), Version pinning against net10.0-windows10.0.26100.0, Backup, export and portability, A NotInstalled app must not touch the production store (+5 more)

### Community 27 - "Main Page Card Interactions"
Cohesion: 0.18
Nodes (7): AutoConnectCheck, ManageProfilesButton, PortCard, ProfileCard, RoutedEventArgs, Button, CheckBox

### Community 28 - "Service Interfaces and Layering"
Cohesion: 0.18
Nodes (12): AHRS2 must never be a silent fallback for ATTITUDE, BATTERY_STATUS voltages[] summation rule and two sentinels, Telemetry Data Model (attitude, voltage, current, mode, sentinels), ICalibrationService, ICompassService, ITelemetryService, Six-layer one-directional stack (transport to XAML), No generated MAVLink type ever reaches a view model or XAML (+4 more)

### Community 29 - "Compass Identity Decoding"
Cohesion: 0.22
Nodes (11): MAG_CAL_REPORT.fitness judged against COMPASS_CAL_FIT (x2 rule), Instance Mapping by decoded device id, Boot-time per-instance block swap (_reorder_compass_params), BusType enum (0-7, no EXTERNALAHRS), Classify (external/internal decision procedure), CompassDevId (DEV_ID bitfield decode), CompassRow (compass panel per-instance view model), Compass devtype table (AP_Compass_Backend.h authoritative) (+3 more)

### Community 30 - "WinUI Skill Routing"
Cohesion: 0.20
Nodes (11): Atmospheric Filters (noise, grain, glass), Typography & Visual Texture, WinUI App Skill Interface Metadata (openai.yaml), Apache License 2.0 (winui-app skill), Common Routes Reference Table, Grounding Sources (Microsoft Learn, WinUI Gallery, WindowsAppSDK-Samples), Light and Dark Mode by Default (theme-aware resources), WinUI Required Flow (task classification pipeline) (+3 more)

### Community 31 - "Splash Screen Branding"
Cohesion: 0.36
Nodes (11): ARDU OTK Splash Screen Image (scale-200), App Launch Branding Surface (splash shown during startup), Badge-Overlay Icon Pattern (base subject + status badge on corner), Dark Blue Rounded-Square App Tile with Gradient, Green Checkmark Badge Overlay (bottom-right quadrant), Navy + Green Brand Palette (shared app color identity), Pass/Fail Verdict Visual Language (green = accepted result), Quadcopter Rotor Glyph (four white rotor rings on rounded dark-blue square) (+3 more)

### Community 32 - "WinUI Environment Bootstrap"
Cohesion: 0.24
Nodes (10): Enable Developer Mode (WindowsSettings resource), OsVersion Assertion (min 10.0.17763), Install Visual Studio Community 2026 (WinGetPackage), VS Workloads: ManagedDesktop, Universal, WindowsAppSDK.Cs, WinUI WinGet DSC Bootstrap Configuration, dotnet new winui Scaffolding, WinUI Environment Rules (verify, never guess), Launch Verification (fail closed on ambiguous launch) (+2 more)

### Community 33 - "Project and Package Dependencies"
Cohesion: 0.20
Nodes (8): net10.0-windows10.0.26100.0, Microsoft.Data.Sqlite (10.0.10), Microsoft.Windows.SDK.BuildTools (10.0.28000.2526), Microsoft.WindowsAppSDK (2.3.1), System.IO.Ports (10.0.10), System.Management (10.0.10), Velopack (1.2.0), Microsoft.NET.Sdk

### Community 34 - "Medium Tile Icon Branding"
Cohesion: 0.29
Nodes (10): ARDU OTK App Icon Mark, Brand Palette: Navy #1B3A5C + Accent Green #22B14C, Device/Sensor Topology Metaphor, Green Checkmark Badge (bottom-right overlay), MSIX scale-200 Asset Naming Convention, Navy Gradient Rounded-Square Backplate, White Node-Graph Glyph (three connected nodes), Package.appxmanifest Tile Declaration (implied consumer) (+2 more)

### Community 35 - "Compass Priority and Prearm"
Cohesion: 0.25
Nodes (9): Handoff to verification (exact conditions), Procedure: make external compass primary and set use flags (Phases A-F), Never leave the board with zero COMPASS_USE* set, COMPASS_PRIOx_ID Priority Model, STATUSTEXT chunk reassembly (id + chunk_seq) before string matching, STATUSTEXT Ingestion and MAV_SEVERITY inversion, MAV_CMD_RUN_PREARM_CHECKS (401) and its collection window, Prearm catalogue (exact IMU and compass PreArm strings) (+1 more)

### Community 36 - "Creative Design Foundation"
Cohesion: 0.22
Nodes (9): Cinematic Pacing (visual identity), Establishing the Creative Foundation, Cyber / Technical (visual identity), Editorial Brutalism (visual identity), Organic Fluidity (visual identity), Prefer Native CommandBar for Grouped Commands, CommunityToolkit Only When Built-ins Fall Short, Native WinUI / Fluent First (no bespoke chrome) (+1 more)

### Community 37 - "CommunityToolkit Usage Rules"
Cohesion: 0.25
Nodes (9): Full Keyboard Reachability and Focus Order, CommunityToolkit Controls and Helpers, Toolkit HeaderedControls, Platform Controls First Before Toolkit Dependencies, Toolkit Segmented Control, Toolkit SettingsControls, Toolkit Animations Package, Built-In WinUI Controls First (+1 more)

### Community 38 - "Profile Row Models"
Cohesion: 0.25
Nodes (5): ExpectedCompassSlotRow, ProfileListRow, Visibility, NewCalibrationProfile, ARDU_OTK.Services.Fc

### Community 39 - "Serial Port Catalog"
Cohesion: 0.31
Nodes (6): SerialPortCatalog, SerialPortDescription, IReadOnlyList, string, Task, Regex

### Community 40 - "Fixed-Yaw Magnetometer Calibration"
Cohesion: 0.25
Nodes (8): Azimuth input must be TRUE north, not magnetic, MAG_CAL_STATUS enum (with ArduPilot extensions 6-10), Onboard mag cal commands (DO_START/ACCEPT/CANCEL_MAG_CAL), MAV_CMD_FIXED_MAG_CAL_YAW (42006), _reset_compass_id() side effect on priority slots, Workflow (b): Fixed-Yaw / Large-Vehicle Calibration, ATTITUDE.yaw and VFR_HUD.heading are TRUE north, CalibrationOp table (command id, params sent, MAV_RESULT, STATUSTEXT)

### Community 41 - "Motion and Performance System"
Cohesion: 0.32
Nodes (8): Custom Cursor Tracking with Lerp Interpolation, Hardware Acceleration (animate only transform/opacity), High-Fidelity Micro-Interactions, Magnetic Components, The Motion Design System, The Performance Imperative, prefers-reduced-motion Accessibility Guardrail, Responsive Degradation for Touch Devices

### Community 42 - "Scroll Narrative Ecosystem"
Cohesion: 0.29
Nodes (8): Framer Motion, GSAP / ScrollTrigger, Implementation Ecosystem (framework-tailored libraries), Lenis Smooth Scrolling, React Three Fiber, Scroll-Driven Narratives, Responsiveness as a Shell-Plus-Page Problem, Explicit Scroll Ownership for Collection Layouts

### Community 43 - "Packaging and Lifecycle Rules"
Cohesion: 0.32
Nodes (8): Hidden Package-Identity Assumptions, Packaged vs Unpackaged Launch Rules, Packaged App by Default, C#-First Folder Split (Pages, Controls, ViewModels, Services, Styles, Assets), Bootstrapper and Runtime Initialization for Unpackaged Apps, Explicit Deployment Model Before Build Steps, Push and App Notifications via Samples, Windows App SDK Lifecycle, Notifications, and Deployment

### Community 44 - "WinUI Setup Prerequisites"
Cohesion: 0.29
Nodes (8): Developer Mode as Optional-Not-Universal Requirement, Environment Audit and Remediation, Manual Non-Mutating Readiness Audit, Required WinUI Prerequisite Baseline, Setup-and-Scaffold Flow (SKILL.md), C#-First WinUI 3 Desktop App on Windows App SDK, Setup and Project Selection, Setup Baseline Versions (Win10 1809, SDK 19041, .NET)

### Community 45 - "App Startup Host"
Cohesion: 0.29
Nodes (4): Application, App, Exception, LaunchActivatedEventArgs

### Community 46 - "Square Tile Icon Branding"
Cohesion: 0.50
Nodes (8): ARDU OTK Square 44x44 App Tile Icon (scale-200), Badge-Overlay Icon Composition Pattern (base glyph + status badge), Dark Blue Rounded-Square Tile Background, Green Circular Checkmark Badge Overlay, White Node-Graph / Network Topology Glyph, ОТК Quality-Control Pass/Accept Branding Motif, scale-200 Resource Density Qualifier (88x88 px effective), Windows App Icon Asset Set (scale-qualified logo variants)

### Community 47 - "Lock Screen Logo Branding"
Cohesion: 0.52
Nodes (7): ARDU OTK visual brand identity (device-network + acceptance check), Green checkmark badge overlay (QC pass indicator), LockScreenLogo.scale-200 (app lock screen logo asset), Node-graph glyph (three connected blue circles), OTK (ОТК) quality-control acceptance domain, scale-200 density variant (Windows packaging asset naming convention), Windows app manifest lock-screen logo asset slot

### Community 48 - "Unplated Icon Variant Branding"
Cohesion: 0.52
Nodes (7): Square44x44Logo targetsize-24 altform-unplated (app icon asset), ARDU OTK visual brand identity (dark navy + white check), Checkmark glyph on dark rounded square, MSIX / WinUI app package manifest asset set, Quality-control / pass-inspection metaphor (OTK acceptance check), targetsize-24 asset scaling convention, Unplated altform variant (transparent-background taskbar icon)

### Community 49 - "Store Logo Branding"
Cohesion: 0.52
Nodes (7): ARDU OTK visual brand identity: connected devices verified by QC, Green circular checkmark badge overlay (bottom-right corner), StoreLogo.png — Microsoft Store tile logo for ARDU OTK, MSIX/WinUI packaging asset convention (Assets/ logo set), Dark-blue node-and-link (molecule/network) motif, OTK (ОТК) quality-control / pass-fail verdict semantics, Small-size legibility constraint (flat shapes, 2-color contrast at ~50px)

### Community 50 - "Operator Form Fields"
Cohesion: 0.43
Nodes (6): AzimuthBox, OperatorBox, ReferenceFileBox, UnitIdBox, TextChangedEventArgs, TextBox

### Community 51 - "Run Control Buttons"
Cohesion: 0.29
Nodes (5): CancelButton, RefreshHistoryButton, RefreshPortsButton, StartButton, Button

### Community 52 - "Stage Row Building"
Cohesion: 0.29
Nodes (4): NewRunButton, IEnumerable, Stage, Title

### Community 53 - "Bench Busy Interlock"
Cohesion: 0.33
Nodes (6): IsBenchBusy predicate (fail-safe to busy), SITL testing scope and its limits, UpdateService.IsBusy update interlock, Interrupted-run sweep to Verdict='aborted', Write as you go; never commit a run only at the end, Hold the update interlock while the bench is working

### Community 54 - "Premium Frontend UI Skill"
Cohesion: 0.33
Nodes (6): Entry Sequence (Preloading & Initialization), Fluid & Contextual Navigation, Hero Architecture, Immersive Digital Environments, premium-frontend-ui Skill, SplitType Typography Chunking

### Community 55 - "Light Unplated Icon Branding"
Cohesion: 0.60
Nodes (6): Green check-mark badge overlay (pass / QC accepted), Square44x44Logo targetsize-48 altform-lightunplated (app tile icon 48px), altform-lightunplated asset variant (unplated icon for light taskbar/theme), MSIX/UWP asset naming convention (Square44x44Logo.targetsize-N_altform-*), Blue node-graph glyph (connected circles / network of sensors), ARDU OTK brand identity: device-under-test passes quality control

### Community 56 - "Wide Tile Logo Branding"
Cohesion: 0.53
Nodes (6): Dark Navy Blue Brand Palette, MSIX/UWP Wide Tile Asset Convention, ОТК (Quality Control) Domain Identity, Green Checkmark QC Badge, Quadcopter/Drone Glyph Mark, ARDU OTK Wide Tile Logo (310x150 @200%)

### Community 57 - "Tile Builder Helpers"
Cohesion: 0.40
Nodes (4): Action, Func, Button, StackPanel

### Community 59 - "Port Selection Control"
Cohesion: 0.50
Nodes (3): PortCombo, SelectionChangedEventArgs, ComboBox

### Community 60 - "Status InfoBars"
Cohesion: 0.50
Nodes (4): LinkBar, ProfileBar, ReadyBar, InfoBar

### Community 62 - "Store and Link Exceptions"
Cohesion: 0.50
Nodes (3): VehicleLinkException, CalibrationStoreException, Exception

### Community 63 - "WinUI Skill Icon Asset"
Cohesion: 1.00
Nodes (3): Corrupted Binary Asset (UTF-8 Mojibake Re-encoding), WinUI Skill Icon (winui.png), winui-app Skill (WinUI 3 / Windows App SDK)

### Community 64 - "Adaptive Visual States"
Cohesion: 0.67
Nodes (3): NarrowLayout, WideLayout, VisualState

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
- **90 isolated node(s):** `net10.0-windows10.0.26100.0`, `Microsoft.Windows.SDK.BuildTools (10.0.28000.2526)`, `Microsoft.WindowsAppSDK (2.3.1)`, `Velopack (1.2.0)`, `Microsoft.Data.Sqlite (10.0.10)` (+85 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **8 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

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