# Graph Report - ARDU_OTK  (2026-10-07)

## Corpus Check
- cluster-only mode — file stats not available

## Summary
- 3145 nodes · 6981 edges · 203 communities (134 shown, 69 thin omitted)
- Extraction: 93% EXTRACTED · 7% INFERRED · 0% AMBIGUOUS · INFERRED: 492 edges (avg confidence: 0.84)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `308b058c`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- SqliteCalibrationStore
- .Verify
- ReferenceEditorPage
- CompassIdentity
- OtkServer
- AcceptanceSession
- SerialCompassCalibrationJob
- Page
- ParameterRoleMap
- PayloadReader
- SerialVehicleLink
- MavFtpOpcode
- Page
- Page
- ReferenceParameters
- CompassCalibrationPage
- CheckResult
- AppTheme
- graph_freshness.py
- Page
- ARDU_OTK.csproj
- CalibrationContracts.cs
- NetworkService
- MainPage
- ParameterRoleRow
- IVehicleLink
- RoutedEventArgs
- NetworkService.cs
- MainPage.xaml.cs
- .RenderAll
- Page
- CalibrationStage
- ReferencePackage
- system
- .ApplyNetworkSnapshotAsync
- OsdPage
- RunContext
- NetworkSeal
- OtkRoles
- release Workflow (GitHub Actions)
- .RunAcceptanceAsync
- CompassRow
- UpdateService
- system_collections_generic
- Window
- ReferenceScript
- ParameterDifferenceRow
- Page
- Page
- StatusTextEvent
- .ApplyAsync
- .Build
- Graph-breaking change categories
- .RenderCalibrationState
- VehicleLiveState
- ReferencesPage
- ExpectedCompassSlotRow
- .AuditAsync
- AppServices
- Connect Handshake (HEARTBEAT, autopilot==3 gate, AUTOPILOT_VERSION)
- Write then verify procedure (PARAM_SET + independent read by name)
- ParamMismatchRow
- OsdScreen
- ServerStore
- ARDU_OTK.Server/Program.cs
- AcceptanceStepRow
- CalibrationCheckRow
- CalibrationStageRow
- NetworkAdminPage
- TelemetrySession
- Transfer State Machine (states 0-14)
- ScriptDifferenceRow
- .ReceiveLoopAsync
- AppPaths
- Immersive Frontend UI Craftsmanship
- Capability Map (requirement to owning reference)
- Controls, Layout, and Adaptive UI
- Setup and Project Selection
- Project Guardrails
- RunRow
- Run (one verification session against one board under test)
- Deployment: unpackaged, self-contained, Velopack
- Startup Failure Debugging Path
- .Compose
- RoutedEventArgs
- OsdPanelRow
- OsdValueRow
- TokenAuthentication.cs
- .LoginAsync
- MavParamType
- .ReadAsync
- ParameterEnums
- Telemetry Data Model (attitude, voltage, current, mode, sentinels)
- ReferenceRow
- Page
- RunsPage
- CancellationToken
- WorkstationSettings
- Classify (external/internal decision procedure)
- Accessibility, Input, and Localization
- Sample and Source Map
- Windows App SDK Lifecycle, Notifications, and Deployment
- Navy + Green Brand Palette (shared app color identity)
- StackPanel
- TokenAuthenticationHandler
- .MapUsers
- MavlinkEncoder
- MavlinkFrame
- WinUI Reference Sections Index
- CommunityToolkit Controls and Helpers
- ARDU OTK App Icon Mark
- OsdPanel
- IProgress
- Procedure: make external compass primary and set use flags (Phases A-F)
- .OnFormFieldChanged
- CompassComplaintKind
- MavBusType
- OsdValue
- MAV_CMD_FIXED_MAG_CAL_YAW (42006)
- graph-freshness CI workflow
- ARDU OTK Square 44x44 App Tile Icon (scale-200)
- Grid
- CountText
- MavResult
- EstimatorFaultKind
- LockScreenLogo.scale-200 (app lock screen logo asset)
- Square44x44Logo targetsize-24 altform-unplated (app icon asset)
- Green circular checkmark badge overlay (bottom-right corner)
- .OnFormFieldChanged
- Border
- .OnUserSelected
- MavlinkFtp.cs
- UpdateService.IsBusy update interlock
- Square44x44Logo targetsize-48 altform-lightunplated (app tile icon 48px)
- ARDU OTK Wide Tile Logo (310x150 @200%)
- CalibrationLogRow
- ScriptComparison
- .TryDetectAsync
- WinUI WinGet DSC Bootstrap Configuration
- NetworkState
- NetworkUserRow
- ArduPilotModes
- AutopilotVersionMessage
- .ListAsync
- .Hash
- .OnPortSelectionChanged
- ProgressRing
- InfoBar
- InfoBar
- .OnScopeChanged
- Corrupted Binary Asset (UTF-8 Mojibake Re-encoding)
- DiffList
- FixedHost
- PortFlyout
- PortsBelowScroll
- MotorCompToggle
- OsdReferenceChoice
- Telemetry coalescing pattern (latest-value slot + display timer)
- CompareProgress
- CompareTickIcon
- CompareTickScale
- CompassList
- HudClip
- PitchTranslate
- RollRotate
- UnitIdBox
- ParameterProgress
- WinUI App Skill Interface Metadata (openai.yaml)
- Apache License 2.0 (winui-app skill)

## God Nodes (most connected - your core abstractions)
1. `MainPage` - 125 edges
2. `Page` - 97 edges
3. `SerialVehicleLink` - 92 edges
4. `SqliteCalibrationStore` - 75 edges
5. `ReferenceEditorPage` - 64 edges
6. `CompassCalibrationPage` - 60 edges
7. `AppServices` - 55 edges
8. `Page` - 53 edges
9. `Page` - 50 edges
10. `AcceptanceSession` - 49 edges

## Surprising Connections (you probably didn't know these)
- `ARDU_OTK.Shared` --references--> `NetworkSeal`  [INFERRED]
  README.md → ARDU_OTK.Shared/Network/NetworkSeal.cs
- `GRAPH HEALTH WARNING Check In GRAPH_REPORT.md` --semantically_similar_to--> `Version From Tag (SemVer Validation Step)`  [INFERRED] [semantically similar]
  CLAUDE.md → .github/workflows/release.yml
- `Confidentiality by Network Code (AES-256-GCM)` --references--> `NetworkSeal`  [EXTRACTED]
  README.md → ARDU_OTK.Shared/Network/NetworkSeal.cs
- `ARDU_OTK.Shared` --references--> `NetworkTrust`  [INFERRED]
  README.md → ARDU_OTK.Shared/Network/NetworkTrust.cs
- `ARDU_OTK.Shared` --references--> `OtkRoles`  [INFERRED]
  README.md → ARDU_OTK.Shared/Security/OtkRoles.cs

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Badge-over-glyph icon composition: navy backplate + white node graph + green check overlay** — ardu_otk_ardu_otk_assets_square150x150logo_scale_200_navy_gradient_squircle, ardu_otk_ardu_otk_assets_square150x150logo_scale_200_node_graph_glyph, ardu_otk_ardu_otk_assets_square150x150logo_scale_200_green_check_badge, ardu_otk_ardu_otk_assets_square150x150logo_scale_200_app_icon_mark [EXTRACTED 1.00]
- **App tile composition: blue rounded plate + node-graph glyph + green check badge form the ARDU OTK identity** — ardu_otk_ardu_otk_assets_square44x44logo_scale_200_app_tile_icon, ardu_otk_ardu_otk_assets_square44x44logo_scale_200_blue_rounded_tile, ardu_otk_ardu_otk_assets_square44x44logo_scale_200_node_graph_glyph, ardu_otk_ardu_otk_assets_square44x44logo_scale_200_green_check_badge [EXTRACTED 1.00]
- **Splash logo composition: tile + quadcopter glyph + verdict badge on transparent canvas** — ardu_otk_assets_splashscreen_scale_200_transparent_letterbox_canvas, ardu_otk_assets_splashscreen_scale_200_dark_blue_rounded_square, ardu_otk_assets_splashscreen_scale_200_quadcopter_glyph, ardu_otk_assets_splashscreen_scale_200_green_checkmark_badge [EXTRACTED 1.00]
- **Sources that must report the bench busy to UpdateService.IsBusy** — _github_skills_ardupilot_firmware_references_dotnet_mavlink_and_winui_integration_update_busy_interlock, _github_skills_ardupilot_firmware_references_dotnet_mavlink_and_winui_integration_isbenchbusy, _github_skills_ardupilot_firmware_references_parameter_protocol_and_profiles_write_verify_procedure, _github_skills_ardupilot_firmware_references_dotnet_mavlink_and_winui_integration_reboot_survival, _github_skills_ardupilot_firmware_references_reference_profiles_and_storage_run, _github_skills_ardupilot_firmware_references_reference_profiles_and_storage_write_as_you_go [EXTRACTED 1.00]
- **Compass calibration transfer: write, verify, reboot, prove acceptance** — _github_skills_ardupilot_firmware_references_compass_calibration_transfer_transfer_state_machine, _github_skills_ardupilot_firmware_references_compass_calibration_transfer_dev_id_validity_rule, _github_skills_ardupilot_firmware_references_compass_calibration_transfer_instance_mapping, _github_skills_ardupilot_firmware_references_compass_calibration_transfer_rollback_snapshot, _github_skills_ardupilot_firmware_references_imu_level_and_health_verification_verificationverdict [EXTRACTED 1.00]
- **Domain services, one per sibling reference** — _github_skills_ardupilot_firmware_references_dotnet_mavlink_and_winui_integration_iparameterservice, _github_skills_ardupilot_firmware_references_dotnet_mavlink_and_winui_integration_icompassservice, _github_skills_ardupilot_firmware_references_dotnet_mavlink_and_winui_integration_icalibrationservice, _github_skills_ardupilot_firmware_references_dotnet_mavlink_and_winui_integration_itelemetryservice, _github_skills_ardupilot_firmware_references_dotnet_mavlink_and_winui_integration_layering [EXTRACTED 1.00]
- **Graph freshness enforcement points** — claude_sessionstart_hook, claude_posttooluse_write_hook, _github_workflows_graph_freshness_workflow, tools_graph_freshness, claude_manifest_json [EXTRACTED 1.00]
- **Knowledge Graph Maintenance Protocol** — claude_graph_first_entry_point, claude_graph_freshness, claude_graph_breaking_changes, claude_graph_update_commands, claude_graph_health_warning [EXTRACTED 1.00]
- **OTK network: bench, server, shared contracts** — readme_ardu_otk_shared [EXTRACTED 1.00]
- **Network package trust and confidentiality chain** — readme_network_package, readme_signature_trust, readme_network_code_encryption, readme_anti_rollback, readme_admin_signing_key, ardu_otk_shared_network_networktrust_ardu_otk_shared_network_networktrust, ardu_otk_shared_network_networkseal_ardu_otk_shared_network_networkseal [EXTRACTED 1.00]
- **Release and update delivery flow** — readme_release_workflow, readme_semver_tag, readme_github_releases, readme_velopack, readme_update_service [EXTRACTED 1.00]
- **Tag-Driven Release Pipeline (tag → version → publish → delta → pack → upload)** — _github_workflows_release_tag_trigger, _github_workflows_release_version_from_tag, _github_workflows_release_build_step, _github_workflows_release_delta_download, _github_workflows_release_vpk_pack, _github_workflows_release_vpk_upload, readme_release_procedure [EXTRACTED 1.00]
- **Release and update pipeline** — readme_release_workflow, readme_velopack, readme_deployment_model [EXTRACTED 1.00]
- **Foundation Flow: Audit, Select, Scaffold, Build, Recover, Verify** — _github_skills_winui_app_references_foundation_environment_audit_and_remediation_environment_audit_and_remediation, _github_skills_winui_app_references_foundation_setup_and_project_selection_setup_and_project_selection, _github_skills_winui_app_references_foundation_winui_app_structure_winui_app_structure, _github_skills_winui_app_references_foundation_template_first_recovery_template_first_recovery, _github_skills_winui_app_references_build_run_and_launch_verification_build_run_and_launch_verification [EXTRACTED 1.00]
- **Brand meaning system: connected-device topology validated by QC acceptance, expressed in the navy/green palette** — ardu_otk_ardu_otk_assets_square150x150logo_scale_200_device_topology_metaphor, ardu_otk_ardu_otk_assets_square150x150logo_scale_200_qc_pass_semantics, ardu_otk_ardu_otk_assets_square150x150logo_scale_200_brand_palette, ardu_otk_ardu_otk_assets_square150x150logo_scale_200_app_icon_mark [INFERRED 0.75]
- **Brand identity system: UAV QC domain meaning conveyed via palette, badge pattern and launch surface** — ardu_otk_assets_splashscreen_scale_200_uav_qc_domain_semantics, ardu_otk_assets_splashscreen_scale_200_pass_fail_verdict_visual_language, ardu_otk_assets_splashscreen_scale_200_navy_green_brand_palette, ardu_otk_assets_splashscreen_scale_200_app_launch_branding [INFERRED 0.75]
- **Badged-icon composition: base motif + status badge + size constraint yield the app's Store identity** — ardu_otk_assets_storelogo_network_motif, ardu_otk_assets_storelogo_green_check_badge, ardu_otk_assets_storelogo_small_size_legibility, ardu_otk_assets_storelogo_brand_identity [INFERRED 0.75]
- **Badged-icon composition: base network glyph + status overlay expressing QC acceptance branding** — ardu_otk_ardu_otk_assets_lockscreenlogo_scale_200_lockscreenlogo, ardu_otk_ardu_otk_assets_lockscreenlogo_scale_200_node_graph_glyph, ardu_otk_ardu_otk_assets_lockscreenlogo_scale_200_green_check_badge, ardu_otk_ardu_otk_assets_lockscreenlogo_scale_200_brand_identity [INFERRED 0.85]
- **Windows packaging icon pipeline: named logo asset + density qualifier + shared asset set** — ardu_otk_ardu_otk_assets_square44x44logo_scale_200_app_tile_icon, ardu_otk_ardu_otk_assets_square44x44logo_scale_200_scale_200_density_qualifier, ardu_otk_ardu_otk_assets_square44x44logo_scale_200_windows_app_icon_asset_set [INFERRED 0.85]
- **Windows app icon variant matrix (base logo x targetsize x altform) resolved at package install** — ardu_otk_assets_square44x44logo_targetsize_24_altform_unplated_asset, ardu_otk_assets_square44x44logo_targetsize_24_altform_unplated_targetsize_24_scaling, ardu_otk_assets_square44x44logo_targetsize_24_altform_unplated_unplated_variant, ardu_otk_assets_square44x44logo_targetsize_24_altform_unplated_msix_packaging [INFERRED 0.85]
- **48px light-unplated tile composes node-graph glyph plus green pass badge to express ARDU OTK identity** — ardu_otk_assets_square44x44logo_targetsize_48_altform_lightunplated_icon, ardu_otk_assets_square44x44logo_targetsize_48_altform_lightunplated_node_graph_glyph, ardu_otk_assets_square44x44logo_targetsize_48_altform_lightunplated_green_check_badge, ardu_otk_assets_square44x44logo_targetsize_48_altform_lightunplated_otk_brand_identity [INFERRED 0.85]
- **App brand identity composition: drone glyph + QC checkmark badge on navy rounded-square, packaged as an MSIX wide tile** — ardu_otk_assets_wide310x150logo_scale_200_wide_tile_logo, ardu_otk_assets_wide310x150logo_scale_200_quadcopter_mark, ardu_otk_assets_wide310x150logo_scale_200_qc_checkmark_badge, ardu_otk_assets_wide310x150logo_scale_200_brand_palette, ardu_otk_assets_wide310x150logo_scale_200_msix_tile_asset [INFERRED 0.85]
- **Compass calibration pipeline** — readme_fixed_mag_cal_yaw, readme_reference_transfer_verification, readme_acceptance_checks, readme_run_history [INFERRED 0.85]
- **Unpackaged Deployment Constraint Set (guardrails preserving the delivery model)** — agents_unpackaged_deployment, agents_disable_xaml_generated_main, agents_enablemsixtooling, agents_publishtrimmed_false, agents_apppaths_storage_safety, readme_deployment_model [INFERRED 0.85]
- **Packaging Model Coherence Across Setup, Launch, and Deployment** — _github_skills_winui_app_references_foundation_setup_and_project_selection_packaged_by_default, _github_skills_winui_app_references_build_run_and_launch_verification_packaged_vs_unpackaged_rules, _github_skills_winui_app_references_build_run_and_launch_verification_package_identity_assumption, _github_skills_winui_app_references_windows_app_sdk_lifecycle_notifications_and_deployment_deployment_model_explicitness, _github_skills_winui_app_references_windows_app_sdk_lifecycle_notifications_and_deployment_bootstrapper_runtime_initialization [INFERRED 0.95]
- **Phone-Width Adaptive Strategy Across Shell, Layout, and Review** — _github_skills_winui_app_references_controls_layout_and_adaptive_ui_phone_width_layout_plan, _github_skills_winui_app_references_controls_layout_and_adaptive_ui_adaptive_breakpoint_intent, _github_skills_winui_app_references_shell_navigation_and_windowing_narrow_width_nav_mode, _github_skills_winui_app_references_testing_debugging_and_review_checklists_runtime_breakpoint_verification, _github_skills_winui_app_references_testing_debugging_and_review_checklists_design_review_checklist [INFERRED 0.95]

## Communities (203 total, 69 thin omitted)

### Community 0 - "SqliteCalibrationStore"
Cohesion: 0.08
Nodes (9): NetworkStoreTests, NewCalibrationReference, Firmware, Roles, Scripts, CalibrationStoreException, DatabaseFilePath, HasOpenRun (+1 more)

### Community 1 - ".Verify"
Cohesion: 0.05
Nodes (29): ReferencePackageHeader, Name, ParamHash, ParamText, Scripts, Version, Script, Hash (+21 more)

### Community 2 - "ReferenceEditorPage"
Cohesion: 0.06
Nodes (21): AddScriptButton, BrowseButton, CancelButton, CollapseAllGroupsButton, ExpandAllGroupsButton, ReadScriptsButton, SaveButton, SnapshotButton (+13 more)

### Community 3 - "CompassIdentity"
Cohesion: 0.07
Nodes (27): CompassDeviceId, Address, Bus, BusType, DevType, IsEmpty, CompassSlot, IsPresent (+19 more)

### Community 4 - "OtkServer"
Cohesion: 0.10
Nodes (16): PublishReferenceRequest, SyncPullResponse, UserReplica, CreateUserRequest, SetPasswordRequest, SetPinRequest, UpdateUserRequest, AuthTests (+8 more)

### Community 5 - "AcceptanceSession"
Cohesion: 0.15
Nodes (11): AcceptanceSession, FirmwareBanner, IsConnected, LastBoard, LiveState, Messages, PortName, ArmReadiness (+3 more)

### Community 6 - "SerialCompassCalibrationJob"
Cohesion: 0.15
Nodes (4): ParamMismatch, SerialCompassCalibrationJob, AppVersion, SlotNames

### Community 7 - "Page"
Cohesion: 0.06
Nodes (46): AzimuthBar, ChecksList, ChecksSummaryText, ErrorBar, GateBar, HistoryCard, HistoryHintText, HistoryList (+38 more)

### Community 8 - "ParameterRoleMap"
Cohesion: 0.07
Nodes (16): ParameterControl, ControlledHidden, ControlledVisible, Uncontrolled, ParameterRole, IsControlled, IsHazardousOverride, IsOverridden (+8 more)

### Community 9 - "PayloadReader"
Cohesion: 0.12
Nodes (13): AttitudeMessage, CommandAckMessage, Gps2RawMessage, GpsRawIntMessage, HeartbeatMessage, IsAutopilot, ImuMessage, MavlinkFraming (+5 more)

### Community 10 - "SerialVehicleLink"
Cohesion: 0.06
Nodes (14): StatusTextMessage, SerialVehicleLink, DiagnosticLog, FirmwareBanner, IsConnected, LiveState, PortName, TargetComponent (+6 more)

### Community 11 - "MavFtpOpcode"
Cohesion: 0.05
Nodes (37): MavFtpDirectory, MavFtpEntry, MavFtpError, EndOfFile, Fail, FailErrno, FileExists, FileNotFound (+29 more)

### Community 12 - "Page"
Cohesion: 0.08
Nodes (41): AuthorPanel, ErrorBar, FrozenBar, GateBar, HeadingToleranceBox, MissingCoreBar, MissingMotBar, Page (+33 more)

### Community 14 - "Page"
Cohesion: 0.10
Nodes (39): AccelCalText, CompareDiffText, CompareMatchedText, CompareSkippedText, CompareStateText, CompareTickCount, CompareTickName, CompassBusyText (+31 more)

### Community 15 - "ReferenceParameters"
Cohesion: 0.11
Nodes (7): ReferenceParamSet, ReferenceParamFile, ReferenceParameters, CompassParamCount, DescribedSlots, Firmware, ParamCount

### Community 16 - "CompassCalibrationPage"
Cohesion: 0.09
Nodes (12): NewRunButton, CompassCalibrationPage, Checks, History, LoadHistory, LogEntries, Mismatches, RewriteFromReference (+4 more)

### Community 17 - "CheckResult"
Cohesion: 0.13
Nodes (12): MagSample, IsEmpty, Magnitude, TelemetrySnapshot, AcceptanceChecks, CheckIds, CompassComplaint, CheckOutcome (+4 more)

### Community 18 - "AppTheme"
Cohesion: 0.08
Nodes (16): Application, App, CrashLogPath, MainWindow, Program, UiScale, Current, UiState (+8 more)

### Community 19 - "graph_freshness.py"
Cohesion: 0.11
Nodes (17): PostToolUse Write freshness hook, SessionStart freshness hook, compare(), corpus_on_disk(), describe(), emit_hook(), force_utf8_streams(), git() (+9 more)

### Community 20 - "Page"
Cohesion: 0.13
Nodes (14): AdminLoginBox, AdminNameBox, CodeBox, ConnectPanel, LoginBox, LoginButton, LoginPanel, Page (+6 more)

### Community 21 - "ARDU_OTK.csproj"
Cohesion: 0.07
Nodes (31): ARDU_OTK.App.Tests, net10.0-windows10.0.26100.0, Microsoft.NET.Test.Sdk (17.12.0), xunit (2.9.2), xunit.runner.visualstudio (2.8.2), Microsoft.NET.Sdk, net10.0-windows10.0.26100.0, Microsoft.Data.Sqlite (10.0.10) (+23 more)

### Community 22 - "CalibrationContracts.cs"
Cohesion: 0.10
Nodes (20): CalibrationRunResult, Mismatches, PortName, RunId, CalibrationTolerances, HeadingVsJigDeg, InterCompassSpreadDeg, ParamVerifyTolerance (+12 more)

### Community 23 - "NetworkService"
Cohesion: 0.13
Nodes (7): NetworkRefreshResult, NetworkService, HasGitHubToken, HasSigningKey, IsAdmin, Session, TokenPath

### Community 24 - "MainPage"
Cohesion: 0.08
Nodes (12): MainPage, Compasses, Differences, Ink, InkDim, LogEntries, ScriptDiffs, SelectedPort (+4 more)

### Community 25 - "ParameterRoleRow"
Cohesion: 0.07
Nodes (25): RoleModeFilterBox, ComboBox, ParameterGroupRow, CountText, HazardVisibility, IsExpanded, ModeText, OverrideVisibility (+17 more)

### Community 26 - "IVehicleLink"
Cohesion: 0.12
Nodes (11): FullParameterSet, IsComplete, IVehicleFileTransfer, IVehicleLink, FirmwareBanner, IsConnected, LiveState, TargetComponent (+3 more)

### Community 27 - "RoutedEventArgs"
Cohesion: 0.08
Nodes (14): AcceptDiffButton, ApplyAllScriptsButton, EditReferenceButton, FinishButton, HeadingBox, LevelButton, LinkToggleButton, PortCard (+6 more)

### Community 28 - "NetworkService.cs"
Cohesion: 0.13
Nodes (6): NetworkSource, ARDU_OTK.App.Tests, ARDU_OTK.Shared.Contracts, ARDU_OTK.Shared.Network, ARDU_OTK.Tests, ARDU_OTK.Shared.Security

### Community 30 - ".RenderAll"
Cohesion: 0.11
Nodes (7): AutoConnectCheck, ClearPinBox, SerialPortDescription, Caption, Details, LooksLikeArduPilot, CheckBox

### Community 31 - "Page"
Cohesion: 0.10
Nodes (24): FontScaleBox, FontScaleText, OperatorBox, Page, StandLatBox, StandLonBox, StoreBar, StorePathText (+16 more)

### Community 32 - "CalibrationStage"
Cohesion: 0.09
Nodes (22): PageProgress, MavSeverity, Alert, Critical, Debug, Emergency, Error, Info (+14 more)

### Community 33 - "ReferencePackage"
Cohesion: 0.07
Nodes (23): ReferencePackage, CreatedBy, CreatedUtc, Description, ExportedBy, ExportedUtc, Firmware, HeadingVsJigDeg (+15 more)

### Community 34 - "system"
Cohesion: 0.17
Nodes (4): MavCommand, SysStatusSensor, ARDU_OTK.Services.Network, ARDU_OTK.Services.Store

### Community 36 - "OsdPage"
Cohesion: 0.13
Nodes (5): OsdPage, GeneralRows, HasReference, PanelRows, SettingRows

### Community 37 - "RunContext"
Cohesion: 0.08
Nodes (21): RunContext, Checks, Fix, Inbox, Messages, Mismatches, Pending, PrearmMessages (+13 more)

### Community 38 - "NetworkSeal"
Cohesion: 0.13
Nodes (10): Envelope, Ciphertext, CodeId, Format, FormatVersion, Nonce, Tag, NetworkSeal (+2 more)

### Community 39 - "OtkRoles"
Cohesion: 0.10
Nodes (13): NetworkTrust, OtkRoles, Admin Signing Key (DPAPI, ARDU_OTK.Keys), ARDU_OTK.App.Tests, ARDU_OTK.Server (deferred), ARDU_OTK.Shared, ARDU_OTK.Tests, Run Operator = Logged-in User (+5 more)

### Community 40 - "release Workflow (GitHub Actions)"
Cohesion: 0.11
Nodes (19): dotnet publish Build Step (win-x64 Release), release Workflow (GitHub Actions), Tag Push Trigger v*, Velopack CLI (vpk 1.2.0), Version From Tag (SemVer Validation Step), vpk pack (Installer Packaging Step), vpk upload github (Publish GitHub Release), Build And Release Commands (+11 more)

### Community 42 - "CompassRow"
Cohesion: 0.10
Nodes (19): CompassIdentityRow, CompassRow, Detail, DeviceText, ExternalVisibility, FieldText, Instance, InternalVisibility (+11 more)

### Community 43 - "UpdateService"
Cohesion: 0.09
Nodes (14): UpdateService, CurrentVersion, IsBusy, LastError, PendingVersion, State, UpdateState, Checking (+6 more)

### Community 45 - "Window"
Cohesion: 0.11
Nodes (15): AppTitleBar, NetworkItem, OsdItem, ReferencesItem, RootFrame, RootNav, RunsItem, SessionItem (+7 more)

### Community 46 - "ReferenceScript"
Cohesion: 0.16
Nodes (4): ReferenceScript, Caption, FileName, ScriptTransfer

### Community 47 - "ParameterDifferenceRow"
Cohesion: 0.09
Nodes (18): ParameterDifferenceRow, ActualText, CanWrite, Detail, DiffVisibility, ExpectedText, MatchVisibility, Name (+10 more)

### Community 48 - "Page"
Cohesion: 0.12
Nodes (21): BackupPasswordBox, FormTitle, IssueButton, IssueFileButton, IssueStateText, KeyStateText, NetworkCodeBox, Page (+13 more)

### Community 49 - "Page"
Cohesion: 0.13
Nodes (20): LocalReferencesText, NetworkInfoText, BoardStateText, EmptyText, GeneralPanel, MockHost, MockNoteText, MockSizeText (+12 more)

### Community 51 - "StatusTextEvent"
Cohesion: 0.14
Nodes (6): PrearmReport, CompassMessages, SensorHealth, StatusTextEvent, EstimatorDiagnosis, EstimatorReadiness

### Community 52 - ".ApplyAsync"
Cohesion: 0.12
Nodes (9): ParameterDifference, ParameterDiffKind, Differs, MissingOnBoard, NotInReference, ParameterTransfer, ParameterTransferPlan, IsClean (+1 more)

### Community 53 - ".Build"
Cohesion: 0.16
Nodes (9): OsdFieldKind, Column, Enable, Row, OsdLayout, PanelBuilder, Column, Enable (+1 more)

### Community 54 - "Graph-breaking change categories"
Cohesion: 0.14
Nodes (15): Agent Skills Index, ardupilot-firmware Skill, premium-frontend-ui Skill, winui-app Skill, Project Overview (WinUI 3 + .NET 10 Desktop App), Graph-breaking change categories, graphify-out/manifest.json, Acceptance Checks (COMPASS_OFFS_MAX, field magnitude, prearm) (+7 more)

### Community 55 - ".RenderCalibrationState"
Cohesion: 0.22
Nodes (3): CalibrationStatus, CalibrationVerdict, Unconfirmed

### Community 56 - "VehicleLiveState"
Cohesion: 0.11
Nodes (14): AirData, AttitudeSample, GpsFix, AltitudeMeters, FixTypeText, Hdop, Is3D, LatitudeDeg (+6 more)

### Community 57 - "ReferencesPage"
Cohesion: 0.25
Nodes (4): ReferenceEditorArgs, Page, ReferencesPage, Rows

### Community 58 - "ExpectedCompassSlotRow"
Cohesion: 0.10
Nodes (18): ExpectedCompassSlotRow, AmbiguousVisibility, DeviceText, EmptyVisibility, IsExternal, IsPresent, Kind, KindText (+10 more)

### Community 59 - ".AuditAsync"
Cohesion: 0.27
Nodes (3): SessionPrincipal, StoreRuleException, SecretPolicy

### Community 60 - "AppServices"
Cohesion: 0.10
Nodes (15): AppServices, ConnectedFirmware, ConnectedPort, Instance, IsAcceptanceRunning, IsBusy, IsLinkConnected, IsReadingParameters (+7 more)

### Community 61 - "Connect Handshake (HEARTBEAT, autopilot==3 gate, AUTOPILOT_VERSION)"
Cohesion: 0.11
Nodes (14): Per-instance mag feed (RAW_IMU/SCALED_IMU2/SCALED_IMU3 to priority slot), Connect Handshake (HEARTBEAT, autopilot==3 gate, AUTOPILOT_VERSION), Freshness and Link Loss (staleness timeouts, degradation), Flight Mode Tables (COPTER_MODE/PLANE_MODE/ROVER_MODE by MAV_TYPE), Reading<T> (value + UpdatedUtc staleness wrapper), Stream Rate Policy via MAV_CMD_SET_MESSAGE_INTERVAL (511), Device discovery via Win32_PnPEntity / SetupAPI, WinUI 3 UI patterns and stock-first rule (+6 more)

### Community 62 - "Write then verify procedure (PARAM_SET + independent read by name)"
Cohesion: 0.11
Nodes (15): IParameterService, Clean-run rule, Comparison rules (integer exact, REAL32 relative + absolute floor), Detect (param file format detection), Diff outcome model (Match/Differs/MissingOnBoard/NotInReference/Excluded/ReadOnly/Coalesced/ReadFailed), Exportable diff report, MAV_PARAM_TYPE handling and C-cast integer encoding, Reference file formats: Mission Planner .param/.parm and QGC .params (+7 more)

### Community 63 - "ParamMismatchRow"
Cohesion: 0.12
Nodes (10): ParamMismatchRow, ActualText, CanRewrite, Detail, ExpectedText, Name, OutcomeText, OutcomeVisibility (+2 more)

### Community 64 - "OsdScreen"
Cohesion: 0.13
Nodes (7): OsdConfiguration, IsEmpty, OsdScreen, DefectCount, Overflowing, ShownCount, OsdScreenSize

### Community 65 - "ServerStore"
Cohesion: 0.13
Nodes (4): ServerStore, ServerId, ReferenceRecord, UserDto

### Community 66 - "ARDU_OTK.Server/Program.cs"
Cohesion: 0.12
Nodes (7): AdminCommand, ServerPaths, Program, ARDU_OTK.Server.Auth, ARDU_OTK.Server.Endpoints, ARDU_OTK.Server.Hosting, ARDU_OTK.Server.Data

### Community 67 - "AcceptanceStepRow"
Cohesion: 0.14
Nodes (13): AcceptanceStepRow, Detail, DetailVisibility, FailVisibility, PassVisibility, PendingVisibility, RunningVisibility, Title (+5 more)

### Community 68 - "CalibrationCheckRow"
Cohesion: 0.12
Nodes (15): CalibrationCheckRow, Detail, FailVisibility, HeaderText, InconclusiveVisibility, MeasuredText, MeasuredVisibility, PassVisibility (+7 more)

### Community 69 - "CalibrationStageRow"
Cohesion: 0.13
Nodes (15): CalibrationStageRow, FailVisibility, InconclusiveVisibility, PassVisibility, PendingVisibility, RunningVisibility, Stage, StateText (+7 more)

### Community 72 - "Transfer State Machine (states 0-14)"
Cohesion: 0.16
Nodes (10): Compass::force_save_calibration() path (UNVERIFIED), Rollback Snapshot (pre-write .param capture), Transfer State Machine (states 0-14), Workflow (a): Compass Calibration Transfer, Reboot survival and COM re-enumeration, Thirteen safety rules for a tool that writes to flight hardware, Ordered board-came-back detection (heartbeat gap, banner, time_boot_ms), MAV_CMD_PREFLIGHT_REBOOT_SHUTDOWN (246), param1=1 (+2 more)

### Community 73 - "ScriptDifferenceRow"
Cohesion: 0.12
Nodes (15): ScriptDifferenceRow, ActionText, ActionVisibility, CanApply, Detail, DiffVisibility, FileName, HashText (+7 more)

### Community 74 - ".ReceiveLoopAsync"
Cohesion: 0.22
Nodes (3): MavlinkCrc, MavlinkParser, DroppedFrames

### Community 75 - "AppPaths"
Cohesion: 0.16
Nodes (7): AppPaths, BackupsDirectory, DatabaseFilePath, DataRoot, IsDevelopmentStore, ProtocolExportDirectory, StoreRoot

### Community 76 - "Immersive Frontend UI Craftsmanship"
Cohesion: 0.12
Nodes (15): 1. Establishing the Creative Foundation, 2.1 The Entry Sequence (Preloading & Initialization), 2.2 The Hero Architecture, 2.3 Fluid & Contextual Navigation, 2. Structural Requirements for Immersive UI, 3.1 Scroll-Driven Narratives, 3.2 High-Fidelity Micro-Interactions, 3. The Motion Design System (+7 more)

### Community 77 - "Capability Map (requirement to owning reference)"
Cohesion: 0.17
Nodes (10): Reference Index, Transferable Parameter Classification (COPY / NEVER COPY / OPT-IN), Compass Parameter Map (per-instance lookup tables), Built-in default exclusions (Mission Planner skip list), Comparison profile JSON schema (the configurable block), Rule resolution order (first matching include wins, then excludes), Compass-calibration block (CalBlock), Reference profile (snapshot + comparison revision + optional cal block) (+2 more)

### Community 78 - "Controls, Layout, and Adaptive UI"
Cohesion: 0.21
Nodes (11): Explicit Adaptive Breakpoint Intent, Controls, Layout, and Adaptive UI, Phone-Width Single-Column Layout Plan, Remove Redundant Outer Section Borders, Single Main Shell Window Owning Navigation, Simpler Visual Trees and Lighter Templates, Custom Title Bar as Functional Chrome, Narrow/Phone-Width Navigation Mode (+3 more)

### Community 79 - "Setup and Project Selection"
Cohesion: 0.16
Nodes (13): Developer Mode as Optional-Not-Universal Requirement, Environment Audit and Remediation, Manual Non-Mutating Readiness Audit, Required WinUI Prerequisite Baseline, Setup-and-Scaffold Flow (SKILL.md), C#-First WinUI 3 Desktop App on Windows App SDK, Setup and Project Selection, Setup Baseline Versions (Win10 1809, SDK 19041, .NET) (+5 more)

### Community 80 - "Project Guardrails"
Cohesion: 0.19
Nodes (9): Download Previous Releases For Delta Computation, App Startup Flow (Program.cs → App.xaml.cs → MainWindow → MainPage), Architecture Map, IsBusy Gate Semantics In UpdateService.ApplyAndRestart, Project Guardrails, Unpackaged Self-Contained Deployment Model, UI/Update Flow (MainPage + UpdateService State Machine), Дельта-обновления (110 МБ первая загрузка, далее дельты) (+1 more)

### Community 81 - "RunRow"
Cohesion: 0.15
Nodes (12): RunRow, DetailText, IsAborted, IsFailed, IsPassed, OperatorText, ReferenceText, StartedText (+4 more)

### Community 82 - "Run (one verification session against one board under test)"
Cohesion: 0.15
Nodes (7): PREARM_CHECK bit (0x10000000), SYS_STATUS health bits (present && enabled && health), VerificationVerdict record, ParamWriteAudit table (append-only write audit log), Run (one verification session against one board under test), Unit (operator-entered board id vs auto-detected hardware identity), Work history (list, filtering, drill-down, per-unit timeline, export)

### Community 83 - "Deployment: unpackaged, self-contained, Velopack"
Cohesion: 0.15
Nodes (6): MAVLink library choice: Asv.Mavlink, Deployment: unpackaged, self-contained, Velopack, Backup, export and portability, SQLite store via Microsoft.Data.Sqlite (WAL, foreign_keys ON), Startup order after an update (8 fixed steps), App facts: net10.0-windows, unpackaged, Velopack, PublishTrimmed=False

### Community 84 - "Startup Failure Debugging Path"
Cohesion: 0.19
Nodes (9): High-Contrast-Safe Visuals, Build, Run, and Launch Verification, Startup Failure Debugging Path, dotnet new winui Comparison Scaffold, Opaque MSB3073 / XamlCompiler.exe Failures, Template-First Recovery Loop, Template-First Recovery for Startup and XAML Failures, Avoid Window.Current in WinUI 3 Startup (+1 more)

### Community 87 - "RoutedEventArgs"
Cohesion: 0.19
Nodes (7): BrowseButton, CancelButton, RefreshHistoryButton, RefreshPortsButton, RewriteAllButton, StartButton, Button

### Community 88 - "OsdPanelRow"
Cohesion: 0.17
Nodes (11): OsdPanelRow, Caption, ColumnText, DiffVisibility, EnableText, MatchVisibility, ReferenceLineText, ReferenceLineVisibility (+3 more)

### Community 89 - "OsdValueRow"
Cohesion: 0.15
Nodes (10): OsdRowState, OsdValueRow, DiffVisibility, MatchVisibility, Name, ReferenceLineText, ReferenceLineVisibility, Tip (+2 more)

### Community 91 - ".LoginAsync"
Cohesion: 0.22
Nodes (7): LoginOutcome, InvalidCredentials, LockedOut, Success, LoginRequest, LoginResponse, UserInfo

### Community 92 - "MavParamType"
Cohesion: 0.15
Nodes (13): MavParamType, Int16, Int32, Int8, Real32, PendingWrite, Accepted, AlreadyEqual (+5 more)

### Community 94 - "ParameterEnums"
Cohesion: 0.24
Nodes (5): ParameterEnums, VehicleClass, Copter, Plane, Unknown

### Community 95 - "Telemetry Data Model (attitude, voltage, current, mode, sentinels)"
Cohesion: 0.18
Nodes (8): Telemetry Data Model (attitude, voltage, current, mode, sentinels), ICalibrationService, ICompassService, ITelemetryService, Six-layer one-directional stack (transport to XAML), AHRS_TRIM_X/Y/Z (what level calibration writes), Level calibration preconditions and gating (7 rows), Level (trim) calibration: MAV_CMD_PREFLIGHT_CALIBRATION param5=2

### Community 96 - "ReferenceRow"
Cohesion: 0.17
Nodes (11): ReferenceRow, CanEdit, Id, IsRetired, ManageVisibility, Name, OriginText, RetireActionText (+3 more)

### Community 97 - "Page"
Cohesion: 0.23
Nodes (11): AbortedCountText, CountText, FailedCountText, Page, PageBar, PassedCountText, RunList, UnitsCountText (+3 more)

### Community 98 - "RunsPage"
Cohesion: 0.20
Nodes (4): UnitFilterBox, RunsPage, Rows, TextBox

### Community 101 - "WorkstationSettings"
Cohesion: 0.17
Nodes (10): WorkstationSettings, AutoConnect, DefaultOperator, HasStandPosition, IsComplete, LastPortName, LastReferenceId, Problems (+2 more)

### Community 102 - "Classify (external/internal decision procedure)"
Cohesion: 0.22
Nodes (10): MAG_CAL_REPORT.fitness judged against COMPASS_CAL_FIT (x2 rule), Instance Mapping by decoded device id, BusType enum (0-7, no EXTERNALAHRS), Classify (external/internal decision procedure), CompassDevId (DEV_ID bitfield decode), CompassRow (compass panel per-instance view model), Compass devtype table (AP_Compass_Backend.h authoritative), COMPASS_EXTERNAL flag semantics (2 = ForcedExternal operator lock) (+2 more)

### Community 103 - "Accessibility, Input, and Localization"
Cohesion: 0.24
Nodes (11): Accessibility, Input, and Localization, Automation Properties and Accessible Naming, Mouse, Touch, Pen, and Keyboard Input Parity, Localization and RTL Readiness, Narrator Support, Acrylic for Transient Surfaces, Mica for Long-Lived Base Layers, Styling, Theming, Materials, and Icons (+3 more)

### Community 104 - "Sample and Source Map"
Cohesion: 0.24
Nodes (9): Virtualization-Friendly Collection Controls, Performance, Diagnostics, and Responsiveness, Keep the UI Thread Free, WPR/WPA with XAML Frame Analysis, Sample and Source Map, Performance Checklist, Required Verification Loop, Runtime Verification at Multiple Breakpoints (+1 more)

### Community 105 - "Windows App SDK Lifecycle, Notifications, and Deployment"
Cohesion: 0.22
Nodes (9): Hidden Package-Identity Assumptions, Packaged vs Unpackaged Launch Rules, C#-First Folder Split (Pages, Controls, ViewModels, Services, Styles, Assets), WindowsAppSDK-Samples, AppWindow and Windows App SDK Windowing, AppLifecycle Activation, Instancing, and Restart, Bootstrapper and Runtime Initialization for Unpackaged Apps, Push and App Notifications via Samples (+1 more)

### Community 106 - "Navy + Green Brand Palette (shared app color identity)"
Cohesion: 0.36
Nodes (9): ARDU OTK Splash Screen Image (scale-200), App Launch Branding Surface (splash shown during startup), Badge-Overlay Icon Pattern (base subject + status badge on corner), Dark Blue Rounded-Square App Tile with Gradient, Green Checkmark Badge Overlay (bottom-right quadrant), Navy + Green Brand Palette (shared app color identity), Quadcopter Rotor Glyph (four white rotor rings on rounded dark-blue square), Transparent Wide Canvas with Centered Logo (splash letterbox layout) (+1 more)

### Community 107 - "StackPanel"
Cohesion: 0.18
Nodes (11): CompassBusyPanel, FcSelector, PortFlyoutRoot, PortsAbove, PortsBelow, ReferenceFlyoutRoot, ReferencesAbove, ReferencesBelow (+3 more)

### Community 110 - "MavlinkEncoder"
Cohesion: 0.31
Nodes (3): MavlinkEncoder, ComponentId, SystemId

### Community 112 - "WinUI Reference Sections Index"
Cohesion: 0.22
Nodes (7): WinUI Reference Sections Index, WinUI App Structure, Connected Animation, Motion, Animations, and Polish, CommunityToolkit/Windows Repository, Microsoft Learn Windows Apps Docs, WinUI Gallery (microsoft/WinUI-Gallery)

### Community 113 - "CommunityToolkit Controls and Helpers"
Cohesion: 0.22
Nodes (7): Full Keyboard Reachability and Focus Order, CommunityToolkit Controls and Helpers, Toolkit HeaderedControls, Toolkit Segmented Control, Toolkit SettingsControls, Toolkit Animations Package, Code Review Checklist

### Community 114 - "ARDU OTK App Icon Mark"
Cohesion: 0.29
Nodes (8): ARDU OTK App Icon Mark, Brand Palette: Navy #1B3A5C + Accent Green #22B14C, Green Checkmark Badge (bottom-right overlay), MSIX scale-200 Asset Naming Convention, Navy Gradient Rounded-Square Backplate, White Node-Graph Glyph (three connected nodes), Package.appxmanifest Tile Declaration (implied consumer), Square150x150Logo.scale-200 (Medium Tile Asset)

### Community 115 - "OsdPanel"
Cohesion: 0.20
Nodes (9): OsdPanel, AbsentOnBoard, BoardCell, Caption, DiffersAnywhere, IsDefect, ReferenceCell, ShownInReference (+1 more)

### Community 117 - "Procedure: make external compass primary and set use flags (Phases A-F)"
Cohesion: 0.25
Nodes (7): Handoff to verification (exact conditions), Procedure: make external compass primary and set use flags (Phases A-F), COMPASS_PRIOx_ID Priority Model, STATUSTEXT chunk reassembly (id + chunk_seq) before string matching, STATUSTEXT Ingestion and MAV_SEVERITY inversion, MAV_CMD_RUN_PREARM_CHECKS (401) and its collection window, Prearm catalogue (exact IMU and compass PreArm strings)

### Community 118 - ".OnFormFieldChanged"
Cohesion: 0.31
Nodes (6): AuthorBox, DescriptionBox, NameBox, ReferencePathBox, RoleFilterBox, TextBox

### Community 119 - "CompassComplaintKind"
Cohesion: 0.22
Nodes (8): CompassComplaintKind, Calibration, Environment, Hardware, Reboot, Running, Unknown, ComplaintRule

### Community 120 - "MavBusType"
Cohesion: 0.22
Nodes (9): MavBusType, DroneCan, I2C, Msp, Serial, Sitl, Spi, Unknown (+1 more)

### Community 121 - "OsdValue"
Cohesion: 0.22
Nodes (7): OsdValue, BoardCell, BoardText, Differs, IsDefect, ReferenceCell, ReferenceText

### Community 122 - "MAV_CMD_FIXED_MAG_CAL_YAW (42006)"
Cohesion: 0.25
Nodes (7): MAG_CAL_STATUS enum (with ArduPilot extensions 6-10), Onboard mag cal commands (DO_START/ACCEPT/CANCEL_MAG_CAL), MAV_CMD_FIXED_MAG_CAL_YAW (42006), _reset_compass_id() side effect on priority slots, Workflow (b): Fixed-Yaw / Large-Vehicle Calibration, ATTITUDE.yaw and VFR_HUD.heading are TRUE north, CalibrationOp table (command id, params sent, MAV_RESULT, STATUSTEXT)

### Community 123 - "graph-freshness CI workflow"
Cohesion: 0.29
Nodes (3): graph-freshness CI workflow, Instruction Quality Rules, graphify-out/graph.json

### Community 124 - "ARDU OTK Square 44x44 App Tile Icon (scale-200)"
Cohesion: 0.50
Nodes (7): ARDU OTK Square 44x44 App Tile Icon (scale-200), Badge-Overlay Icon Composition Pattern (base glyph + status badge), Dark Blue Rounded-Square Tile Background, Green Circular Checkmark Badge Overlay, White Node-Graph / Network Topology Glyph, scale-200 Resource Density Qualifier (88x88 px effective), Windows App Icon Asset Set (scale-qualified logo variants)

### Community 125 - "Grid"
Cohesion: 0.25
Nodes (6): CompareActionsPanel, CompareCountsPanel, CompareTickPanel, HudViewport, PanelsRow, Grid

### Community 127 - "CountText"
Cohesion: 0.14
Nodes (8): CountText, ManagePanel, PageBar, ReferenceList, InfoBar, ListView, StackPanel, TextBlock

### Community 128 - "MavResult"
Cohesion: 0.25
Nodes (8): MavResult, Accepted, Cancelled, Denied, Failed, InProgress, TemporarilyRejected, Unsupported

### Community 129 - "EstimatorFaultKind"
Cohesion: 0.25
Nodes (8): EstimatorFaultKind, BlockedByComplaints, Disabled, HardwareMismatch, Missing, None, Settling, Unknown

### Community 130 - "LockScreenLogo.scale-200 (app lock screen logo asset)"
Cohesion: 0.52
Nodes (5): Green checkmark badge overlay (QC pass indicator), LockScreenLogo.scale-200 (app lock screen logo asset), Node-graph glyph (three connected blue circles), scale-200 density variant (Windows packaging asset naming convention), Windows app manifest lock-screen logo asset slot

### Community 131 - "Square44x44Logo targetsize-24 altform-unplated (app icon asset)"
Cohesion: 0.52
Nodes (6): Square44x44Logo targetsize-24 altform-unplated (app icon asset), ARDU OTK visual brand identity (dark navy + white check), Checkmark glyph on dark rounded square, MSIX / WinUI app package manifest asset set, targetsize-24 asset scaling convention, Unplated altform variant (transparent-background taskbar icon)

### Community 132 - "Green circular checkmark badge overlay (bottom-right corner)"
Cohesion: 0.52
Nodes (5): ARDU OTK visual brand identity: connected devices verified by QC, Green circular checkmark badge overlay (bottom-right corner), StoreLogo.png — Microsoft Store tile logo for ARDU OTK, MSIX/WinUI packaging asset convention (Assets/ logo set), Dark-blue node-and-link (molecule/network) motif

### Community 133 - ".OnFormFieldChanged"
Cohesion: 0.43
Nodes (5): AzimuthBox, OperatorBox, ReferenceFileBox, UnitIdBox, TextBox

### Community 134 - "Border"
Cohesion: 0.29
Nodes (7): AccelCalCard, GyroCalCard, HudCard, MagCalCard, PortGlow, ReferenceGlow, Border

### Community 135 - ".OnUserSelected"
Cohesion: 0.29
Nodes (5): UsersList, GeneralList, PanelList, ScreenSettingsList, ListView

### Community 137 - "UpdateService.IsBusy update interlock"
Cohesion: 0.33
Nodes (4): IsBenchBusy predicate (fail-safe to busy), SITL testing scope and its limits, UpdateService.IsBusy update interlock, Interrupted-run sweep to Verdict='aborted'

### Community 138 - "Square44x44Logo targetsize-48 altform-lightunplated (app tile icon 48px)"
Cohesion: 0.60
Nodes (4): Green check-mark badge overlay (pass / QC accepted), Square44x44Logo targetsize-48 altform-lightunplated (app tile icon 48px), MSIX/UWP asset naming convention (Square44x44Logo.targetsize-N_altform-*), Blue node-graph glyph (connected circles / network of sensors)

### Community 139 - "ARDU OTK Wide Tile Logo (310x150 @200%)"
Cohesion: 0.53
Nodes (4): Dark Navy Blue Brand Palette, Green Checkmark QC Badge, Quadcopter/Drone Glyph Mark, ARDU OTK Wide Tile Logo (310x150 @200%)

### Community 140 - "CalibrationLogRow"
Cohesion: 0.33
Nodes (6): CalibrationLogRow, CriticalVisibility, InfoVisibility, Text, TimeText, WarningVisibility

### Community 142 - "ScriptComparison"
Cohesion: 0.33
Nodes (6): ScriptComparison, ContentDiffers, ExtraOnBoard, Match, MissingOnBoard, Unreadable

### Community 144 - "WinUI WinGet DSC Bootstrap Configuration"
Cohesion: 0.40
Nodes (5): Enable Developer Mode (WindowsSettings resource), OsVersion Assertion (min 10.0.17763), Install Visual Studio Community 2026 (WinGetPackage), VS Workloads: ManagedDesktop, Universal, WindowsAppSDK.Cs, WinUI WinGet DSC Bootstrap Configuration

### Community 146 - "NetworkUserRow"
Cohesion: 0.40
Nodes (4): NetworkUserRow, Details, DisplayName, User

### Community 148 - "AutopilotVersionMessage"
Cohesion: 0.40
Nodes (5): AutopilotVersionMessage, HasVersion, Major, Minor, Patch

### Community 152 - "ProgressRing"
Cohesion: 0.50
Nodes (4): CompassBusyRing, LinkRing, ProgressRing, BusyRing

### Community 153 - "InfoBar"
Cohesion: 0.50
Nodes (4): LinkBar, ReadyBar, ReferenceBar, InfoBar

### Community 154 - "InfoBar"
Cohesion: 0.50
Nodes (4): PageBar, GateBar, PageBar, InfoBar

### Community 157 - "DiffList"
Cohesion: 0.67
Nodes (3): DiffList, ScriptList, ListView

### Community 158 - "FixedHost"
Cohesion: 0.67
Nodes (3): FixedHost, LadderHost, Canvas

### Community 159 - "PortFlyout"
Cohesion: 0.67
Nodes (3): PortFlyout, ReferenceFlyout, Flyout

### Community 160 - "PortsBelowScroll"
Cohesion: 0.67
Nodes (3): PortsBelowScroll, ReferencesBelowScroll, ScrollViewer

### Community 161 - "MotorCompToggle"
Cohesion: 0.67
Nodes (3): ActiveSwitch, MotorCompToggle, ToggleSwitch

### Community 162 - "OsdReferenceChoice"
Cohesion: 0.67
Nodes (3): OsdReferenceChoice, Caption, Reference

## Ambiguous Edges - Review These
- `C#-First Folder Split (Pages, Controls, ViewModels, Services, Styles, Assets)` → `Explicit Deployment Model Before Build Steps`  [AMBIGUOUS]
  .github/skills/winui-app/references/windows-app-sdk-lifecycle-notifications-and-deployment.md · relation: conceptually_related_to
- `Navy + Green Brand Palette (shared app color identity)` → `Quadcopter Rotor Glyph (four white rotor rings on rounded dark-blue square)`  [AMBIGUOUS]
  ARDU_OTK/Assets/SplashScreen.scale-200.png · relation: conceptually_related_to
- `ARDU OTK Splash Screen Image (scale-200)` → `UAV Quality-Control (ОТК) Domain Semantics Encoded in Logo`  [AMBIGUOUS]
  ARDU_OTK/Assets/SplashScreen.scale-200.png · relation: references
- `MSIX scale-200 Asset Naming Convention` → `Navy Gradient Rounded-Square Backplate`  [AMBIGUOUS]
  ARDU_OTK/Assets/Square150x150Logo.scale-200.png · relation: conceptually_related_to
- `Windows App Icon Asset Set (scale-qualified logo variants)` → `White Node-Graph / Network Topology Glyph`  [AMBIGUOUS]
  ARDU_OTK/Assets/Square44x44Logo.scale-200.png · relation: shares_data_with
- `Dark Blue Rounded-Square Tile Background` → `ОТК Quality-Control Pass/Accept Branding Motif`  [AMBIGUOUS]
  ARDU_OTK/Assets/Square44x44Logo.scale-200.png · relation: conceptually_related_to
- `LockScreenLogo.scale-200 (app lock screen logo asset)` → `OTK (ОТК) quality-control acceptance domain`  [AMBIGUOUS]
  ARDU_OTK/Assets/LockScreenLogo.scale-200.png · relation: conceptually_related_to
- `ARDU OTK visual brand identity (dark navy + white check)` → `MSIX / WinUI app package manifest asset set`  [AMBIGUOUS]
  ARDU_OTK/Assets/Square44x44Logo.targetsize-24_altform-unplated.png · relation: conceptually_related_to
- `Dark-blue node-and-link (molecule/network) motif` → `OTK (ОТК) quality-control / pass-fail verdict semantics`  [AMBIGUOUS]
  ARDU_OTK/Assets/StoreLogo.png · relation: conceptually_related_to
- `Square44x44Logo targetsize-48 altform-lightunplated (app tile icon 48px)` → `ARDU OTK brand identity: device-under-test passes quality control`  [AMBIGUOUS]
  ARDU_OTK/Assets/Square44x44Logo.targetsize-48_altform-lightunplated.png · relation: shares_data_with
- `ARDU OTK Wide Tile Logo (310x150 @200%)` → `ОТК (Quality Control) Domain Identity`  [AMBIGUOUS]
  ARDU_OTK/Assets/Wide310x150Logo.scale-200.png · relation: shares_data_with
- `winui-app Skill (WinUI 3 / Windows App SDK)` → `Corrupted Binary Asset (UTF-8 Mojibake Re-encoding)`  [AMBIGUOUS]
  .github/skills/winui-app/assets/winui.png · relation: conceptually_related_to
- `premium-frontend-ui Skill` → `winui-app Skill`  [AMBIGUOUS]
  .github/skills/README.md · relation: semantically_similar_to
- `Unpackaged Self-Contained Deployment Model` → `EnableMsixTooling=true Retained For XAML/PRI Asset Targets`  [AMBIGUOUS]
  AGENTS.md · relation: conceptually_related_to

## Knowledge Gaps
- **709 isolated node(s):** `CheckIds`, `NetworkSource`, `MavCommand`, `SysStatusSensor`, `SetPasswordRequest` (+704 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 1092 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **69 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **What is the exact relationship between `C#-First Folder Split (Pages, Controls, ViewModels, Services, Styles, Assets)` and `Explicit Deployment Model Before Build Steps`?**
  _Edge tagged AMBIGUOUS (relation: conceptually_related_to) - confidence is low._
- **Why does `MainPage` connect `MainPage` to `ReferenceEditorPage`, `CompassIdentity`, `AcceptanceSession`, `CalibrationLogRow`, `Page`, `CheckResult`, `IVehicleLink`, `RoutedEventArgs`, `MainPage.xaml.cs`, `.RenderAll`, `.RunAcceptanceAsync`, `CompassRow`, `ParameterProgress`, `ParameterDifferenceRow`, `StatusTextEvent`, `.ApplyAsync`, `.RenderCalibrationState`, `VehicleLiveState`, `AppServices`, `ScriptDifferenceRow`, `RunsPage`, `WorkstationSettings`, `Grid`, `.BuildTiles`?**
  _High betweenness centrality (0.171) - this node is a cross-community bridge._
- **What connects `CheckIds`, `NetworkSource`, `MavCommand` to the rest of the system?**
  _709 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `SqliteCalibrationStore` be split into smaller, more focused modules?**
  _Cohesion score 0.07587719298245614 - nodes in this community are weakly interconnected._
- **What is the exact relationship between `Navy + Green Brand Palette (shared app color identity)` and `Quadcopter Rotor Glyph (four white rotor rings on rounded dark-blue square)`?**
  _Edge tagged AMBIGUOUS (relation: conceptually_related_to) - confidence is low._
- **Why does `AppServices` connect `AppServices` to `SqliteCalibrationStore`, `ReferenceEditorPage`, `RunsPage`, `OsdPage`, `system`, `.ApplyOneScriptAsync`, `CancellationToken`, `WorkstationSettings`, `IProgress`, `Task`, `NetworkService`, `MainPage`, `ReferencesPage`?**
  _High betweenness centrality (0.117) - this node is a cross-community bridge._
- **Should `.Verify` be split into smaller, more focused modules?**
  _Cohesion score 0.05223880597014925 - nodes in this community are weakly interconnected._