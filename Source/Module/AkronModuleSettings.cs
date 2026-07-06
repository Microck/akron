using Celeste;
using Celeste.Mod;
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.Akron;

[SettingName("modoptions_akron_title")]
public partial class AkronModuleSettings : EverestModuleSettings {
    public const string DefaultAutoDeafenHotkey = "";

    public const int DefaultHazardAccuracyInvalidLimit = 0;
    public const bool DefaultHazardAccuracyTint = true;
    public const AkronNoclipAccuracyTintMode DefaultHazardAccuracyTintMode = AkronNoclipAccuracyTintMode.WhileTouching;
    public const int DefaultHazardAccuracyTintColor = 0xFF0000;
    public const int DefaultHazardAccuracyTintOpacity = 30;
    public const int DefaultHazardAccuracyTintDurationMs = 250;
    public const string DefaultDeathStatsFormat = "$C ($B)";
    public const int DefaultRefillClarityColor = 0xFF2929;
    public const int DefaultHitboxPlayerColor = 0xFF0000;
    public const int DefaultHitboxPlayerHurtboxColor = 0x9ACD32;
    public const int DefaultHitboxSolidColor = 0xFF7F50;
    public const int DefaultHitboxHazardColor = 0xFF0000;
    public const int DefaultHitboxTriggerColor = 0x9370DB;
    public const int DefaultHitboxOtherColor = 0xFF0000;
    public const int DefaultHitboxDeathColor = 0x8B0000;
    public const int DefaultHitboxDeathPlayerColor = 0xF5F5F5;
    public const string DefaultScreenshotScannerExportPath = "AkronScreenshotTool_Exports";
    public const string DefaultDeathParticleCustomShape = "0000000000111100001111000011110000111100001111000000000000000000";
    public const int DeathParticleCanvasSize = 8;
    public const int DeathParticleCanvasCells = DeathParticleCanvasSize * DeathParticleCanvasSize;
    private bool fpsBypass;
    private int fpsBypassTarget = 120;
    private bool tpsBypass;
    private int tpsBypassTarget = 60;
    private AkronFrameIncreaseMethod frameBypassMethod = AkronFrameIncreaseMethod.Interval;
    private AkronCameraSmoothingMode frameBypassCameraSmoothing = AkronCameraSmoothingMode.Fancy;
    private AkronObjectSmoothingMode frameBypassObjectSmoothing = AkronObjectSmoothingMode.Extrapolate;
    private bool frameBypassTasMode;
    private bool frameBypassSubpixelMadeline;
    private bool frameBypassSmoothBackground;
    private bool frameBypassSmoothForeground;
    private bool frameBypassHideStretchedEdges;
    private bool frameBypassSillyMode;

    public bool StreamerMode { get; set; }
    // ProofModeOverlay is normally enabled indirectly by Submission Mode. The
    // direct command path exists for QA and review automation, not as a
    // first-case player setting.
    public bool ProofModeOverlay { get; set; }
    public bool SubmissionMode { get; set; }
    public bool ProofRecorderGuard { get; set; }
    public bool EndScreenHelper { get; set; }
    public bool PauseTracker { get; set; }
    public bool MapVersionStamp { get; set; }
    public bool GoldenTransparency { get; set; }
    public int GoldenTransparencyOpacity { get; set; } = 55;
    public bool LagPauser { get; set; }
    public int LagPauserThresholdMs { get; set; } = 250;
    public bool LagPauserIgnoreSpeedrunToolLoadStates { get; set; }
    // Derived compatibility flag for exported/imported setup state. Runtime UI
    // should derive low-distraction from the visual-noise channel settings.
    public bool LowDistractionOverlay { get; set; }

    public bool SafeMode { get; set; }
    public IndicatorVisibility IndicatorVisibility { get; set; } = IndicatorVisibility.ShowWhenFlagged;
    public IndicatorCorner IndicatorCorner { get; set; } = IndicatorCorner.TopRight;
    public int IndicatorOffsetX { get; set; }
    public int IndicatorOffsetY { get; set; }
    public bool ConsumeGameplayInputInMenu { get; set; } = true;
    public bool PauseGameplayInMenu { get; set; }
    public int OverlayOpacity { get; set; } = 96;
    public AkronOverlayThemePreset OverlayThemePreset { get; set; } = AkronOverlayThemePreset.Default;
    public int OverlayScale { get; set; } = 100;
    public int OverlayBlur { get; set; }
    public int OverlayAnimationMs { get; set; } = 80;
    public bool Logging { get; set; } = true;
    public AkronLoggingLevel LoggingLevel { get; set; } = AkronLoggingLevel.Diagnostic;
    public bool LoggingMirrorWarningsToEverest { get; set; } = true;
    public int LoggingMaxFileSizeMb { get; set; } = 5;
    public int LoggingRetainedFiles { get; set; } = 5;
    public AkronSetupSection SetupPackSection { get; set; } = AkronSetupSection.Whole;
    public string SetupPackExportName { get; set; } = string.Empty;
    public string CommunityPackIndexUrl { get; set; } = AkronCommunityPacks.DefaultIndexUrl;
    public AkronSetupSection CommunityPackSection { get; set; } = AkronSetupSection.Whole;
    public string CommunityPackSearchQuery { get; set; } = string.Empty;
    public string CommunityPackUploadEndpoint { get; set; } = AkronCommunityPackUploads.DefaultUploadEndpoint;
    public AkronSetupSection CommunityPackUploadSection { get; set; } = AkronSetupSection.StartPos;
    public string CommunityPackUploadInstallId { get; set; } = string.Empty;
    public bool CommunityPackUploadUseDiscordAttribution { get; set; }
    public string CommunityPackUploadDiscordUserId { get; set; } = string.Empty;
    public string CommunityPackUploadTitleOverride { get; set; } = string.Empty;
    public string CommunityPackUploadDescriptionOverride { get; set; } = string.Empty;
    public string CustomOverlayThemeName { get; set; } = "Custom";
    public int CustomOverlayWindowColor { get; set; } = 0x292929;
    public int CustomOverlayHeaderColor { get; set; } = 0xC42A30;
    public int CustomOverlayHeaderHoverColor { get; set; } = 0xDC3C42;
    public int CustomOverlayFrameColor { get; set; } = 0x000000;
    public int CustomOverlayTextColor { get; set; } = 0xFFFFFF;
    public int CustomOverlayMutedColor { get; set; } = 0x7D8080;
    public int CustomOverlayDisabledColor { get; set; } = 0x909090;
    public bool FloatingButton { get; set; }
    public int FloatingButtonOpacity { get; set; } = 70;
    public int FloatingButtonScale { get; set; } = 100;
    public bool FloatingButtonInLevels { get; set; } = true;
    public bool FloatingButtonInMenus { get; set; }
    public bool SearchAutofocus { get; set; }
    public List<string> CollapsedOverlaySections { get; set; } = new List<string> {
        "Speedrun Tool",
        "CelesteTAS",
        "Extended Variant Mode",
        "Extended Camera Dynamics"
    };
    public bool MenuBindingsInGameOnly { get; set; } = true;
    public bool ConfirmRetry { get; set; }
    public bool ConfirmRestart { get; set; }
    public bool ConfirmReloadRoom { get; set; }
    public bool ConfirmReloadChapter { get; set; }
    public bool ConfirmFullReset { get; set; }
    public bool ConfirmLoadState { get; set; }
    public bool AllowPauseBuffering { get; set; }
    public bool FastLookout { get; set; }
    public int FastLookoutMultiplier { get; set; } = 3;
    public bool SkipPostcards { get; set; }
    public bool SkipIntro { get; set; }
    public bool DeathPbLossPrompt { get; set; }
    public bool CameraOffset { get; set; }
    public int CameraOffsetX { get; set; }
    public int CameraOffsetY { get; set; }
    public bool CursorZoom { get; set; }
    public int CursorZoomPercent { get; set; } = 100;
    public int CursorZoomStepPercent { get; set; } = 10;
    public bool CursorZoomAllowZoomOut { get; set; }
    public bool CursorZoomResetOnDeactivate { get; set; }
    public AkronCursorZoomActivationMode CursorZoomActivationMode { get; set; } = AkronCursorZoomActivationMode.Hold;
    public bool CursorTools { get; set; }
    public AkronCursorToolsClickAction CursorToolsClickAction { get; set; } = AkronCursorToolsClickAction.ClickTeleport;
    public bool CursorToolsCursorZoom { get; set; } = true;
    public bool CursorToolsFreeCamera { get; set; } = true;
    public bool CursorToolsFreezeGameplay { get; set; }

    [DefaultButtonBinding(0, Keys.Tab)]
    public ButtonBinding ToggleOverlay { get; set; }

    [DefaultButtonBinding(0, Keys.None)]
    public ButtonBinding FastLookoutHold { get; set; }

    [DefaultButtonBinding(0, Keys.None)]
    public ButtonBinding Retry { get; set; }

    [DefaultButtonBinding(0, Keys.None)]
    public ButtonBinding ReloadRoom { get; set; }

    [DefaultButtonBinding(0, Keys.None)]
    public ButtonBinding OpenDebugMap { get; set; }

    [DefaultButtonBinding(0, Keys.None)]
    public ButtonBinding ReloadChapter { get; set; }

    [DefaultButtonBinding(0, Keys.None)]
    public ButtonBinding SaveState { get; set; }

    [DefaultButtonBinding(0, Keys.None)]
    public ButtonBinding LoadState { get; set; }

    [DefaultButtonBinding(0, Keys.None)]
    public ButtonBinding PreviousSlot { get; set; }

    [DefaultButtonBinding(0, Keys.None)]
    public ButtonBinding NextSlot { get; set; }

    [DefaultButtonBinding(0, Keys.None)]
    public ButtonBinding CycleGrabMode { get; set; }

    [DefaultButtonBinding(0, Keys.None)]
    public ButtonBinding FreezeGameplay { get; set; }

    [DefaultButtonBinding(0, Keys.None)]
    public ButtonBinding StepFrame { get; set; }

    [DefaultButtonBinding(0, Keys.None)]
    public ButtonBinding DecreaseTimescale { get; set; }

    [DefaultButtonBinding(0, Keys.None)]
    public ButtonBinding IncreaseTimescale { get; set; }

    [DefaultButtonBinding(0, Keys.None)]
    public ButtonBinding SetStartPos { get; set; }

    [DefaultButtonBinding(0, Keys.None)]
    public ButtonBinding LoadStartPos { get; set; }

    [DefaultButtonBinding(0, Keys.None)]
    public ButtonBinding ClearStartPos { get; set; }

    [DefaultButtonBinding(0, Keys.None)]
    public ButtonBinding PreviousStartPos { get; set; }

    [DefaultButtonBinding(0, Keys.None)]
    public ButtonBinding NextStartPos { get; set; }

    [DefaultButtonBinding(0, Keys.None)]
    public ButtonBinding LoadStartPosSlot1 { get; set; }

    [DefaultButtonBinding(0, Keys.None)]
    public ButtonBinding LoadStartPosSlot2 { get; set; }

    [DefaultButtonBinding(0, Keys.None)]
    public ButtonBinding LoadStartPosSlot3 { get; set; }

    [DefaultButtonBinding(0, Keys.None)]
    public ButtonBinding LoadStartPosSlot4 { get; set; }

    [DefaultButtonBinding(0, Keys.None)]
    public ButtonBinding LoadStartPosSlot5 { get; set; }

    [DefaultButtonBinding(0, Keys.None)]
    public ButtonBinding LoadStartPosSlot6 { get; set; }

    [DefaultButtonBinding(0, Keys.None)]
    public ButtonBinding LoadStartPosSlot7 { get; set; }

    [DefaultButtonBinding(0, Keys.None)]
    public ButtonBinding LoadStartPosSlot8 { get; set; }

    [DefaultButtonBinding(0, Keys.None)]
    public ButtonBinding LoadStartPosSlot9 { get; set; }

    [DefaultButtonBinding(0, Keys.None)]
    public ButtonBinding ToggleHitboxes { get; set; }

    [DefaultButtonBinding(0, Keys.None)]
    public ButtonBinding ToggleEntityInspector { get; set; }

    [DefaultButtonBinding(0, Keys.LeftAlt)]
    public ButtonBinding EntityInspectorCursorHold { get; set; }
    public AkronInspectorPinPlacement EntityInspectorPinPlacement { get; set; } = AkronInspectorPinPlacement.NearClick;
    public int EntityInspectorPinX { get; set; } = 16;
    public int EntityInspectorPinY { get; set; } = 16;
    public bool EntityInspectorPinShowPropertiesByDefault { get; set; } = true;
    public bool EntityInspectorPinHoverPreview { get; set; } = true;

    [DefaultButtonBinding(0, Keys.None)]
    public ButtonBinding ToggleFrameBypass { get; set; }

    [DefaultButtonBinding(0, Keys.None)]
    public ButtonBinding CycleFrameBypassCameraSmoothing { get; set; }

    [DefaultButtonBinding(0, Keys.LeftAlt)]
    public ButtonBinding ClickTeleportCursor { get; set; }

    [DefaultButtonBinding(0, Keys.LeftAlt)]
    public ButtonBinding CursorZoomHold { get; set; }

    [DefaultButtonBinding(0, Keys.LeftAlt)]
    public ButtonBinding CursorToolsHold { get; set; }

    public bool ReducedVisualNoise { get; set; }
    public bool NoParticles { get; set; }
    public bool NoTrails { get; set; }
    public bool NoGlitch { get; set; }
    public bool NoAnxiety { get; set; }
    public bool NoDistortion { get; set; }
    public bool HideSnow { get; set; }
    public bool HideWindSnow { get; set; }
    public bool HideWaterfalls { get; set; }
    public bool HideTentacles { get; set; }
    public bool HideHeatDistortion { get; set; }
    public bool NoStaminaFlash { get; set; }
    public bool RefillClarity { get; set; }
    public int RefillClarityColor { get; set; } = DefaultRefillClarityColor;
    public int RefillClarityOpacity { get; set; } = 100;
    public bool LightLevel { get; set; }
    public int LightLevelPercent { get; set; } = 100;
    public bool BloomLevel { get; set; }
    public int BloomLevelPercent { get; set; } = 50;
    public bool ScreenTint { get; set; }
    public int ScreenTintColor { get; set; } = 0x000000;
    public int ScreenTintOpacity { get; set; } = 20;
    public bool Screenshake { get; set; }
    public int ScreenshakeIntensity { get; set; }
    public bool HitboxViewer { get; set; }
    public bool ShowTriggers { get; set; }
    public bool HitboxActiveOnly { get; set; }
    public bool HitboxHidePlayer { get; set; }
    public bool HitboxShowPlayerHurtbox { get; set; } = true;
    public bool HitboxShowHazards { get; set; } = true;
    public bool HitboxShowSolids { get; set; } = true;
    public bool HitboxShowTriggers { get; set; } = true;
    public bool HitboxShowLastDeath { get; set; }
    public bool HitboxShowAllOnDeath { get; set; }
    public bool HitboxShowDeathPlayerMarker { get; set; }
    public bool FixHitboxPixels { get; set; }
    public bool ShowHitboxTrail { get; set; }
    public int HitboxTrailLength { get; set; } = 30;
    public int HitboxTrailOpacity { get; set; } = 55;
    public float HitboxLineThickness { get; set; } = 5f;
    public int HitboxFillOpacity { get; set; }
    public bool HitboxBlackOutline { get; set; }
    public int HitboxPlayerColor { get; set; } = DefaultHitboxPlayerColor;
    public int HitboxPlayerHurtboxColor { get; set; } = DefaultHitboxPlayerHurtboxColor;
    public int HitboxSolidColor { get; set; } = DefaultHitboxSolidColor;
    public int HitboxHazardColor { get; set; } = DefaultHitboxHazardColor;
    public int HitboxTriggerColor { get; set; } = DefaultHitboxTriggerColor;
    public int HitboxOtherColor { get; set; } = DefaultHitboxOtherColor;
    public int HitboxDeathColor { get; set; } = DefaultHitboxDeathColor;
    public int HitboxDeathPlayerColor { get; set; } = DefaultHitboxDeathPlayerColor;
    public bool EntityInspector { get; set; }
    public AkronInspectorPinFilter InspectorPinFilter { get; set; } = AkronInspectorPinFilter.Both;
    public bool FrameStepper { get; set; }
    public bool StepHoldRepeat { get; set; }
    public int StepHoldDelayFrames { get; set; } = 18;
    public int StepHoldIntervalFrames { get; set; } = 4;

    public bool InfiniteStamina { get; set; }
    public bool InfiniteDash { get; set; }
    public int SetInventoryDashes { get; set; } = 2;
    public int SetInventoryJumps { get; set; }
    public bool SetInventoryRestoreOnDeath { get; set; }
    public bool DashCountOverride { get; set; }
    public int DashCountOverrideValue { get; set; } = 2;
    public bool DashCountRefillOnRoomEntry { get; set; } = true;
    public bool DashCountRefillOnTransition { get; set; } = true;
    public bool JumpHack { get; set; }
    public bool JumpHackInfinite { get; set; } = true;
    public int JumpHackExtraJumps { get; set; } = 1;
    public bool JumpHackAllowVerticalDashJumps { get; set; }
    public bool NoFreezeFrames { get; set; }
    public bool GroundRefillRules { get; set; }
    public bool GroundDashRefill { get; set; } = true;
    public bool GroundStaminaRefill { get; set; } = true;
    public bool DashRedirectEnabled { get; set; }
    public AkronDashRedirectDirection DashRedirectDirections { get; set; } = AkronDashRedirectDirection.Down;
    public bool GrabModeOverrideEnabled { get; set; }
    public GrabModes GrabModeOverrideMode { get; set; } = GrabModes.Toggle;
    public bool AutoKill { get; set; }
    public bool AutoKillTimer { get; set; } = true;
    public int AutoKillSeconds { get; set; } = 60;
    public bool AutoKillArea { get; set; }
    public bool AutoKillShowArea { get; set; } = true;
    public bool AutoKillShowAreaOnDeath { get; set; }
    public AkronAutoKillAreaData AutoKillDefaultAreaConditions { get; set; } = new AkronAutoKillAreaData();
    public List<AkronAutoKillAreaData> AutoKillAreas { get; set; } = new List<AkronAutoKillAreaData>();
    public int AutoKillAreaX { get; set; }
    public int AutoKillAreaY { get; set; }
    public int AutoKillAreaWidth { get; set; }
    public int AutoKillAreaHeight { get; set; }
    public bool AutoDeafen { get; set; }
    public string AutoDeafenHotkey { get; set; } = DefaultAutoDeafenHotkey;
    public bool AutoDeafenArea { get; set; }
    public bool AutoDeafenShowArea { get; set; } = true;
    public List<AkronRectangleData> AutoDeafenAreas { get; set; } = new List<AkronRectangleData>();
    public int AutoDeafenAreaX { get; set; }
    public int AutoDeafenAreaY { get; set; }
    public int AutoDeafenAreaWidth { get; set; }
    public int AutoDeafenAreaHeight { get; set; }
    public bool CoreModeOverrideEnabled { get; set; }
    public AkronCoreModeOverride CoreModeOverride { get; set; } = AkronCoreModeOverride.Hot;
    public AkronCoreModeClickBehavior CoreModeClickBehavior { get; set; } = AkronCoreModeClickBehavior.Toggle;
    public float TransitionSpeedMultiplier { get; set; } = 1f;
    public AkronTrailVisibility TrailVisibility { get; set; } = AkronTrailVisibility.Vanilla;
    public int TrailCuttingRate { get; set; } = 1;
    public bool CustomTrail { get; set; }
    public AkronCustomTrailMode CustomTrailMode { get; set; } = AkronCustomTrailMode.Fixed;
    public bool CustomTrailPulse { get; set; }
    public int CustomTrailColor { get; set; } = 0x44B7FF;
    public int CustomTrailOpacity { get; set; } = 45;
    public float CustomTrailRainbowSpeed { get; set; } = 1f;
    public bool MadelineColors { get; set; }
    public bool MadelineColorNoDash { get; set; }
    public bool MadelineColorOneDash { get; set; } = true;
    public bool MadelineColorTwoDash { get; set; }
    public bool MadelineColorThreeDash { get; set; }
    public bool MadelineColorFourDash { get; set; }
    public bool MadelineColorFiveDash { get; set; }
    public bool MadelineColorGradient { get; set; }
    public float MadelineColorGradientSpeed { get; set; } = 1f;
    public int MadelineNoDashColor { get; set; } = 0x44B7FF;
    public int MadelineOneDashColor { get; set; } = 0xAC3232;
    public int MadelineTwoDashColor { get; set; } = 0xFF6DEF;
    public int MadelineThreeDashColor { get; set; } = 0xFFFC40;
    public int MadelineFourDashColor { get; set; } = 0x63E5FF;
    public int MadelineFiveDashColor { get; set; } = 0x69FF47;
    public int MadelineGradientColorA { get; set; } = 0xAC3232;
    public int MadelineGradientColorB { get; set; } = 0xFF6DEF;
    public bool MadelineHairLength { get; set; }
    public int MadelineNoDashHairLength { get; set; } = 4;
    public int MadelineOneDashHairLength { get; set; } = 4;
    public int MadelineTwoDashHairLength { get; set; } = 5;
    public int MadelineThreeDashHairLength { get; set; } = 5;
    public int MadelineFourDashHairLength { get; set; } = 5;
    public int MadelineFiveDashHairLength { get; set; } = 5;
    public bool MadelineEffectSync { get; set; }
    public AkronMadelineEffectSyncMode MadelineDashParticleSync { get; set; } = AkronMadelineEffectSyncMode.MatchHair;
    public AkronMadelineEffectSyncMode MadelineDashTrailSync { get; set; } = AkronMadelineEffectSyncMode.MatchHair;
    public AkronMadelineEffectSyncMode MadelineDeathEffectSync { get; set; } = AkronMadelineEffectSyncMode.MatchHair;
    public AkronMadelineEffectSyncMode MadelineFeatherColorSync { get; set; } = AkronMadelineEffectSyncMode.MatchHair;
    public AkronMadelineEffectSyncMode MadelineCrownColorSync { get; set; } = AkronMadelineEffectSyncMode.MatchHair;
    public bool CustomDeathParticles { get; set; }
    public AkronDeathParticleColorMode DeathParticleColorMode { get; set; } = AkronDeathParticleColorMode.Hair;
    public int DeathParticleColor { get; set; } = 0xAC3232;
    public int DeathParticleFlashColor { get; set; } = 0xFFFFFF;
    public int DeathParticleOutlineColor { get; set; } = 0x000000;
    public AkronDeathParticleShape DeathParticleShape { get; set; } = AkronDeathParticleShape.Vanilla;
    public float DeathParticleDurationSeconds { get; set; } = 0.834f;
    public string DeathParticleCustomShape { get; set; } = DefaultDeathParticleCustomShape;
    public bool Noclip { get; set; }
    public int NoclipSpeed { get; set; } = 240;
    public int NoclipFloatSpeed { get; set; } = 90;
    public bool NoclipDrawOnTop { get; set; }
    public bool NoclipHidePlayer { get; set; }
    public bool NoclipAccuracy { get; set; }
    public int NoclipAccuracyInvalidLimit { get; set; } = DefaultHazardAccuracyInvalidLimit;
    public bool NoclipAccuracyTint { get; set; } = DefaultHazardAccuracyTint;
    public AkronNoclipAccuracyTintMode NoclipAccuracyTintMode { get; set; } = DefaultHazardAccuracyTintMode;
    public int NoclipAccuracyTintColor { get; set; } = DefaultHazardAccuracyTintColor;
    public int NoclipAccuracyTintOpacity { get; set; } = DefaultHazardAccuracyTintOpacity;
    public int NoclipAccuracyTintDurationMs { get; set; } = DefaultHazardAccuracyTintDurationMs;
    public bool HidePlayer { get; set; }
    public bool NoDeathEffect { get; set; }
    public bool NoDeathWipe { get; set; }
    public AkronNoDeathWipeMode NoDeathWipeMode { get; set; } = AkronNoDeathWipeMode.DeathOnly;
    public bool NoDeathWipeRunCallbacks { get; set; } = true;
    public bool NoRespawnAnimation { get; set; }
    public bool Invincibility { get; set; }
    public AkronInvincibilityMode InvincibilityMode { get; set; } = AkronInvincibilityMode.Akron;
    public bool InvincibilityBottomlessFallRescue { get; set; } = true;
    public bool InvincibilityCrushCollisionChanges { get; set; } = true;
    public bool InvincibilityLavaIcePushback { get; set; } = true;
    public bool InvincibilitySpikeGroundRefills { get; set; } = true;
    public bool RespawnTimeModifier { get; set; }
    public float RespawnTimeSeconds { get; set; } = 1f;
    public bool RespawnTimeIgnoreSpeedhack { get; set; } = true;
    public bool HidePauseMenu { get; set; }
    public bool PauseCountdown { get; set; }
    public bool PauseCountdownHidePauseTint { get; set; } = true;
    public float PauseCountdownSeconds { get; set; } = 3f;
    public bool FreezeTimerWhilePaused { get; set; }
    public bool ShowTrajectory { get; set; }
    public int ShowTrajectoryFrames { get; set; } = 300;
    public int ShowTrajectoryPressColor { get; set; } = 0x00FF19;
    public int ShowTrajectoryReleaseColor { get; set; } = 0xFF0019;
    public int ShowTrajectoryEndMarkerColor { get; set; } = 0xFFFF00;
    public bool ShowTrajectoryUseHitboxColor { get; set; } = true;
    public bool ShowTrajectoryLines { get; set; } = true;
    public bool ShowTrajectoryLineShadow { get; set; } = true;
    public bool ShowTrajectoryPointMarkers { get; set; } = true;
    public bool ShowTrajectoryStartMarker { get; set; } = true;
    public bool ShowTrajectoryEndMarkers { get; set; } = true;
    public bool ShowTrajectoryFrameHitboxes { get; set; }
    public int ShowTrajectoryFrameHitboxInterval { get; set; } = 6;
    public bool ShowTrajectoryHitboxOutlines { get; set; } = true;
    public bool ShowTrajectoryHitboxFill { get; set; } = true;
    public int ShowTrajectoryOpacity { get; set; } = 100;
    public int ShowTrajectoryLineThickness { get; set; } = 2;
    public bool ShowTrajectoryMapAware { get; set; }
    public bool ShowTrajectoryStopOnSolids { get; set; } = true;
    public bool ShowTrajectoryStopOnHazards { get; set; } = true;
    public bool AllowLowVolume { get; set; }
    public float LowVolumeMusic { get; set; }
    public float LowVolumeSfx { get; set; }
    public bool AudioSpeed { get; set; }
    public AkronAudioSpeedPolicy AudioSpeedPolicy { get; set; } = AkronAudioSpeedPolicy.SyncTimescale;
    public float AudioSpeedMultiplier { get; set; } = 1f;
    public bool PitchShift { get; set; }
    public AkronPitchPolicy PitchShiftPolicy { get; set; } = AkronPitchPolicy.Preserve;
    public float PitchShiftMultiplier { get; set; } = 1f;
    public bool FpsBypass {
        get => fpsBypass;
        set {
            if (fpsBypass == value) {
                return;
            }

            fpsBypass = value;
            NotifyFrameBypassChanged();
        }
    }

    public int FpsBypassTarget {
        get => fpsBypassTarget;
        set {
            int clamped = ClampFpsTarget(value);
            if (fpsBypassTarget == clamped) {
                return;
            }

            fpsBypassTarget = clamped;
            NotifyFrameBypassChanged();
        }
    }

    public bool TpsBypass {
        get => tpsBypass;
        set {
            if (tpsBypass == value) {
                return;
            }

            tpsBypass = value;
            NotifyFrameBypassChanged();
        }
    }

    public int TpsBypassTarget {
        get => tpsBypassTarget;
        set {
            int clamped = ClampTpsTarget(value);
            if (tpsBypassTarget == clamped) {
                return;
            }

            tpsBypassTarget = clamped;
            NotifyFrameBypassChanged();
        }
    }

    public AkronFrameIncreaseMethod FrameBypassMethod {
        get => frameBypassMethod;
        set {
            if (frameBypassMethod == value) {
                return;
            }

            frameBypassMethod = value;
            NotifyFrameBypassChanged();
        }
    }

    public AkronCameraSmoothingMode FrameBypassCameraSmoothing {
        get => frameBypassCameraSmoothing;
        set {
            if (frameBypassCameraSmoothing == value) {
                return;
            }

            frameBypassCameraSmoothing = value;
            NotifyFrameBypassChanged();
        }
    }

    public AkronObjectSmoothingMode FrameBypassObjectSmoothing {
        get => frameBypassObjectSmoothing;
        set {
            if (frameBypassObjectSmoothing == value) {
                return;
            }

            frameBypassObjectSmoothing = value;
            NotifyFrameBypassChanged();
        }
    }

    public bool FrameBypassTasMode {
        get => frameBypassTasMode;
        set {
            if (frameBypassTasMode == value) {
                return;
            }

            frameBypassTasMode = value;
            NotifyFrameBypassChanged();
        }
    }

    public bool FrameBypassSubpixelMadeline {
        get => frameBypassSubpixelMadeline;
        set {
            if (frameBypassSubpixelMadeline == value) {
                return;
            }

            frameBypassSubpixelMadeline = value;
            NotifyFrameBypassChanged();
        }
    }

    public bool FrameBypassSmoothBackground {
        get => frameBypassSmoothBackground;
        set {
            if (frameBypassSmoothBackground == value) {
                return;
            }

            frameBypassSmoothBackground = value;
            NotifyFrameBypassChanged();
        }
    }

    public bool FrameBypassSmoothForeground {
        get => frameBypassSmoothForeground;
        set {
            if (frameBypassSmoothForeground == value) {
                return;
            }

            frameBypassSmoothForeground = value;
            NotifyFrameBypassChanged();
        }
    }

    public bool FrameBypassHideStretchedEdges {
        get => frameBypassHideStretchedEdges;
        set {
            if (frameBypassHideStretchedEdges == value) {
                return;
            }

            frameBypassHideStretchedEdges = value;
            NotifyFrameBypassChanged();
        }
    }

    public bool FrameBypassSillyMode {
        get => frameBypassSillyMode;
        set {
            if (frameBypassSillyMode == value) {
                return;
            }

            frameBypassSillyMode = value;
            NotifyFrameBypassChanged();
        }
    }
    public bool SafeModeFreezeAttempts { get; set; }
    public bool SafeModeFreezeJumps { get; set; }
    public bool SafeModeFreezeBestRun { get; set; }
    public bool FreeCamera { get; set; }
    public int FreeCameraSpeed { get; set; } = 240;
    public bool FreeCameraFreezeGameplay { get; set; } = true;
    public bool FreeCameraMouseControl { get; set; }
    public bool SmartStartPos { get; set; }
    public bool RespawnAtStartPos { get; set; }
    public bool StartPosShowLabel { get; set; }
    public int StartPosLabelColor { get; set; } = 0xFFFFFF;
    public AkronHudAnchor StartPosLabelAnchor { get; set; } = AkronHudAnchor.TopLeft;
    public AkronStartPosLabelFormat StartPosLabelFormat { get; set; } = AkronStartPosLabelFormat.Prefix;
    public bool StartPosMousePlacement { get; set; }
    public int StartPosPlacementPanelX { get; set; } = 8;
    public int StartPosPlacementPanelY { get; set; } = 8;
    public bool StartPosPlacementPanelMinimized { get; set; }
    public int StartPosPreviewOpacity { get; set; } = 35;
    public int StartPosConfiguredDashes { get; set; } = -1;
    public int StartPosConfiguredStaminaPercent { get; set; } = -1;
    public AkronStartPosFacing StartPosConfiguredFacing { get; set; } = AkronStartPosFacing.Current;
    public bool StartPosConfiguredIdle { get; set; } = true;
    public bool StartPosConfiguredGrab { get; set; }
    public int StartPosSlotCount { get; set; } = 9;
    public bool BackupsEnabled { get; set; } = true;
    public bool BackupsOnStartup { get; set; } = true;
    public bool BackupsOnShutdown { get; set; }
    public bool BackupsOnSave { get; set; }
    public bool BackupsOnLevelBegin { get; set; }
    public bool BackupsEveryInterval { get; set; }
    public int BackupsIntervalMinutes { get; set; } = 30;
    public int BackupsDeleteOlderThanDays { get; set; } = 15;
    public int BackupsMaxCount { get; set; } = 100;
    public int BackupsKeepAtLeast { get; set; } = 5;
    public int BackupsMaxTotalSizeMb { get; set; } = 1024;
    public long BackupsLastBackupUtcTicks { get; set; }
    public bool ClickTeleport { get; set; }
    public bool BerryObtainIncludeRegular { get; set; } = true;
    public bool BerryObtainIncludeGolden { get; set; }
    public bool BerryObtainIncludeMoon { get; set; }

    private static void NotifyFrameBypassChanged() {
        AkronModule.ApplyMotionSmoothingSettings();
    }

    public bool SpeedrunToolBrokerWarnings { get; set; } = true;
    public bool EverestSafeAutoBlock { get; set; } = true;
    public bool SaveTimeAndDeaths { get; set; }
    public bool UnsafeSavestateOverride { get; set; }
    public int ScreenshotScale { get; set; } = 1;
    public bool ScreenshotStatus { get; set; }
    public string ScreenshotScannerExportPath { get; set; } = DefaultScreenshotScannerExportPath;
    public AkronScreenshotImageFormat ScreenshotScannerImageFormat { get; set; } = AkronScreenshotImageFormat.Png;
    public bool ScreenshotScannerExportMarkers { get; set; }
    public bool ScreenshotScannerExportStartPositions { get; set; } = true;
    public bool ScreenshotScannerExportAutoKillAreas { get; set; } = true;
    public bool ScreenshotScannerExportAutoDeafenAreas { get; set; } = true;
    public bool ScreenshotScannerDownscaleMapCapture { get; set; }
    public bool ScreenshotScannerFreezeTime { get; set; } = true;
    public bool ScreenshotScannerRemoveBackground { get; set; }
    public bool ScreenshotScannerRemoveForeground { get; set; }
    public bool ScreenshotScannerNoclipHideMadeline { get; set; } = true;
    public int ScreenshotScannerWaitFrames { get; set; } = 20;
    public int ScreenshotScannerHorizontalOffsetTiles { get; set; } = 20;
    public int ScreenshotScannerVerticalOffsetTiles { get; set; } = 15;
    public bool Autosave { get; set; }
    public bool AutosaveOnSpawnUpdate { get; set; } = true;
    public bool AutosaveOnRoomLoad { get; set; } = true;
    public bool AutosaveOnRespawn { get; set; }
    public bool AutosaveOnPause { get; set; }
    public bool AutosaveAvoidGameplay { get; set; } = true;
    public bool AutosaveSaveSettings { get; set; }
    public int AutosaveIntervalSeconds { get; set; } = 600;
    public int AutosaveMinimumDelaySeconds { get; set; } = 60;
    public bool AutosaveHideSavingIcon { get; set; }
    public bool DeloadSpinners { get; set; }
    public float DeloadSpinnerDelaySeconds { get; set; }
    public bool DashCountStats { get; set; }
    public AkronCounterDisplayMode DashCountStatsMode { get; set; } = AkronCounterDisplayMode.Session;
    public bool DashCountStatsDoNotResetOnDeath { get; set; }
    public bool JumpCount { get; set; }
    public AkronCounterDisplayMode JumpCountMode { get; set; } = AkronCounterDisplayMode.Session;
    public bool JumpCountDoNotResetOnDeath { get; set; }
    public Dictionary<string, int> SoundVolumes { get; set; } = AkronEarAid.CreateDefaultVolumes();
    public Dictionary<string, bool> SoundVolumeOverrides { get; set; } = AkronEarAid.CreateDefaultOverrideToggles();
    public bool AudioSplitter { get; set; }
    public string AudioSplitterMainDevice { get; set; } = "Default";
    public string AudioSplitterMusicDevice { get; set; } = "Default";
    public string AudioSplitterSfxDevice { get; set; } = "Default";
    public string RecordingOutputFolder { get; set; } = string.Empty;
    public string RecordingFilenameTemplate { get; set; } = DefaultRecordingFilenameTemplate;
    public AkronRecordingContainerFormat RecordingContainerFormat { get; set; } = AkronRecordingContainerFormat.Mkv;
    public int RecordingReplayBufferSeconds { get; set; }
    public AkronRecordingReplayAutoStart RecordingReplayAutoStart { get; set; }
    public bool RecordingTriggerLastDeath { get; set; } = true;
    public bool RecordingTriggerRespawnToDeath { get; set; }
    public bool RecordingTriggerRoomEntryToClear { get; set; }
    public bool RecordingTriggerCheckpointClear { get; set; }
    public bool RecordingTriggerBerryCollect { get; set; } = true;
    public bool RecordingTriggerGoldenDeath { get; set; } = true;
    public int RecordingPreRollSeconds { get; set; } = 5;
    public int RecordingPostRollSeconds { get; set; } = 3;
    public bool RecordingAudioFullMixTrack { get; set; } = true;
    public bool RecordingAudioMusicTrack { get; set; }
    public bool RecordingAudioSfxTrack { get; set; }
    public bool RecordingAudioAmbienceTrack { get; set; }
    public bool RecordingRecordMutedAudio { get; set; }
    public int RecordingAudioFullMixLevel { get; set; } = 100;
    public int RecordingAudioMusicLevel { get; set; } = 100;
    public int RecordingAudioSfxLevel { get; set; } = 100;
    public int RecordingAudioAmbienceLevel { get; set; } = 100;
    public AkronRecordingQualityPreset RecordingQualityPreset { get; set; } = AkronRecordingQualityPreset.Balanced;
    public AkronRecordingRateControl RecordingRateControl { get; set; } = AkronRecordingRateControl.Crf;
    public int RecordingKeyframeIntervalSeconds { get; set; } = 2;
    public bool RecordingDroppedFrameWarning { get; set; } = true;
    public bool RecordingAutoRemux { get; set; } = true;
    public AkronRecordingClipSort RecordingClipBrowserSort { get; set; } = AkronRecordingClipSort.Date;
    public AkronRecordingClipFilter RecordingClipBrowserFilter { get; set; } = AkronRecordingClipFilter.All;
    public bool InternalRecorderExperimentalWarningDismissed { get; set; }
    public int RecordingFramerate { get; set; } = 60;
    public float RecordingEndscreenDurationSeconds { get; set; } = 3.4f;
    public int RecordingBitrateMbps { get; set; } = 30;
    public int RecordingResolutionX { get; set; } = 1920;
    public int RecordingResolutionY { get; set; } = 1080;
    public bool RecordingHidePreview { get; set; }
    public AkronRecordingCodec RecordingCodec { get; set; } = AkronRecordingCodec.Libx264;
    public string RecordingColorspaceArgs { get; set; } = string.Empty;
    public AkronRecordingPreset RecordingPreset { get; set; } = AkronRecordingPreset.Cpu;
    public int ActiveSavestateSlot { get; set; } = 1;
    public int ActiveStartPosSlot { get; set; } = 1;
    public string EditableFlagName { get; set; } = "akron-debug-flag";
    public string TasFilePath { get; set; } = string.Empty;
    public Dictionary<string, AkronMapOverride> MapOverrides { get; set; } = new Dictionary<string, AkronMapOverride>();
    public Dictionary<string, string> MenuActionBindings { get; set; } = new Dictionary<string, string>();
    public Dictionary<string, string> ExtendedVariantConfiguredValues { get; set; } = new Dictionary<string, string>();
}
