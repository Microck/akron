using Celeste;
using System.Collections.Generic;

namespace Celeste.Mod.Akron;

public sealed class AkronSetupState {
    public bool SafeMode { get; set; }
    public bool StreamerMode { get; set; }
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
    public bool LowDistractionOverlay { get; set; }
    public bool PauseGameplayInMenu { get; set; }
    public int OverlayOpacity { get; set; } = 96;
    public AkronOverlayThemePreset OverlayThemePreset { get; set; } = AkronOverlayThemePreset.Default;
    public int OverlayScale { get; set; } = 100;
    public int OverlayBlur { get; set; }
    public int OverlayAnimationMs { get; set; } = 80;
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
    public bool MenuBindingsInGameOnly { get; set; } = true;
    public bool ConfirmRestart { get; set; }
    public bool ConfirmFullReset { get; set; }
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
    public bool RoomLabels { get; set; }
    public bool LabelSystemVisible { get; set; }
    public int RoomLabelColor { get; set; } = 0xFFFFFF;
    public bool StaminaWidget { get; set; }
    public bool SpeedWidget { get; set; }
    public bool DashWidget { get; set; }
    public bool InputViewer { get; set; }
    public int InputHistoryTextColor { get; set; } = 0xFFFFFF;
    public int InputHistoryEventColor { get; set; } = 0xFFFFFF;
    public bool ShowTaps { get; set; }
    public IndicatorCorner TapDisplayCorner { get; set; } = IndicatorCorner.BottomRight;
    public int TapDisplayScale { get; set; } = 100;
    public int TapDisplayOpacity { get; set; } = 80;
    public AkronInputBoardSource InputBoardSource { get; set; }
    public AkronInputBoardLabelPreset InputBoardLabelPreset { get; set; } = AkronInputBoardLabelPreset.Keyboard;
    public List<AkronInputBoardElement> InputBoardElements { get; set; } = AkronInputBoard.BuildDefaultElements();
    public bool InputsPerSecondCounter { get; set; }
    public AkronHudPlacement InputsPerSecondPlacement { get; set; } = AkronHudPlacement.Left;
    public int InputsPerSecondScale { get; set; } = 100;
    public int InputsPerSecondOpacity { get; set; } = 90;
    public int InputsPerSecondTextColor { get; set; } = 0xFFFFFF;
    public bool InputsPerSecondShowTotal { get; set; } = true;
    public bool InputsPerSecondShowMax { get; set; } = true;
    public bool InputsPerSecondCountMovement { get; set; } = true;
    public bool InputsPerSecondCountActions { get; set; } = true;
    public bool InputsPerSecondCountMenu { get; set; }
    public bool RoomTimerWidget { get; set; }
    public int RoomTimerColor { get; set; } = 0xFFFFFF;
    public bool RoomStatTracker { get; set; }
    public int RoomStatTrackerColor { get; set; } = 0xFFFFFF;
    public bool RoomStatShowRoomName { get; set; } = true;
    public bool RoomStatShowDeaths { get; set; } = true;
    public bool RoomStatShowInGameTime { get; set; } = true;
    public bool RoomStatShowStrawberries { get; set; } = true;
    public bool RoomStatShowAliveTime { get; set; }
    public bool RoomStatHideIfGolden { get; set; }
    public AkronRoomStatTimerFreezeMode RoomStatTimerFreezeMode { get; set; } = AkronRoomStatTimerFreezeMode.PausedOrInactive;
    public bool DeathStatsWidget { get; set; }
    public string DeathStatsFormat { get; set; } = AkronModuleSettings.DefaultDeathStatsFormat;
    public AkronDeathStatsVisibility DeathStatsVisibility { get; set; } = AkronDeathStatsVisibility.AfterDeathAndInMenu;
    public int DeathStatsColor { get; set; } = 0xFFFFFF;
    public bool ResourceStaminaBar { get; set; } = true;
    public bool StaminaBar { get; set; }
    public bool StaminaBarPlayer { get; set; }
    public bool StaminaBarHud { get; set; } = true;
    public AkronStaminaPlayerBarPosition StaminaBarPlayerPosition { get; set; } = AkronStaminaPlayerBarPosition.Above;
    public AkronStaminaHudPosition StaminaBarHudPosition { get; set; } = AkronStaminaHudPosition.TopRight;
    public AkronStaminaBarStyle StaminaBarStyle { get; set; } = AkronStaminaBarStyle.Bar;
    public int StaminaPlayerOffsetX { get; set; }
    public int StaminaPlayerOffsetY { get; set; }
    public int StaminaPlayerScale { get; set; } = 100;
    public bool StaminaAlwaysVisible { get; set; }
    public bool StaminaShowDangerMarker { get; set; } = true;
    public bool StaminaShowChangePulse { get; set; } = true;
    public bool StaminaShowOverflow { get; set; } = true;
    public bool StaminaHideWhilePaused { get; set; }
    public int StaminaHudOffsetX { get; set; }
    public int StaminaHudOffsetY { get; set; }
    public int StaminaNormalColor { get; set; } = 0x69FF47;
    public int StaminaLowColor { get; set; } = 0xFF3030;
    public int StaminaFillColor { get; set; } = 0x000000;
    public int StaminaLineColor { get; set; } = 0x000000;
    public int StaminaOverflowColor { get; set; } = 0x63E5FF;
    public bool DashBar { get; set; }
    public bool DashBarPlayer { get; set; }
    public bool DashBarHud { get; set; } = true;
    public AkronStaminaPlayerBarPosition DashBarPlayerPosition { get; set; } = AkronStaminaPlayerBarPosition.Above;
    public AkronStaminaHudPosition DashBarHudPosition { get; set; } = AkronStaminaHudPosition.TopLeft;
    public AkronDashBarStyle DashBarStyle { get; set; } = AkronDashBarStyle.Pips;
    public int DashBarPlayerOffsetX { get; set; }
    public int DashBarPlayerOffsetY { get; set; }
    public int DashBarPlayerScale { get; set; } = 100;
    public bool DashBarAlwaysVisible { get; set; } = true;
    public bool DashBarShowText { get; set; } = true;
    public bool DashBarShowEmptyPips { get; set; } = true;
    public bool DashBarHideWhilePaused { get; set; }
    public int DashBarHudOffsetX { get; set; }
    public int DashBarHudOffsetY { get; set; } = 48;
    public int DashBarAvailableColor { get; set; } = 0xFF6DEF;
    public int DashBarEmptyColor { get; set; } = 0x000000;
    public int DashBarFillColor { get; set; } = 0x000000;
    public int DashBarLineColor { get; set; } = 0x000000;
    public int DashBarLowColor { get; set; } = 0x44B7FF;
    public bool DashNumber { get; set; }
    public int DashNumberOffsetY { get; set; } = -18;
    public int DashNumberColor { get; set; } = 0xFFFFFF;
    public int DashNumberOutlineColor { get; set; } = 0x000000;
    public int DashNumberOpacity { get; set; } = 100;
    public bool SpeedNumber { get; set; }
    public AkronSpeedNumberMode SpeedNumberMode { get; set; } = AkronSpeedNumberMode.Total;
    public int SpeedNumberOffsetY { get; set; } = -34;
    public int SpeedNumberColor { get; set; } = 0xFFFFFF;
    public int SpeedNumberOutlineColor { get; set; } = 0x000000;
    public int SpeedNumberOpacity { get; set; } = 100;
    public bool TotalAttemptsWidget { get; set; }
    public int TotalAttemptsColor { get; set; } = 0xFFFFFF;
    public bool StatusLabelsWidget { get; set; }
    public int StatusLabelsColor { get; set; } = 0xFFFFFF;
    public bool ToastLabels { get; set; } = true;
    public int ToastLabelColor { get; set; } = 0xFFFFFF;
    public AkronHudAnchor ToastLabelAnchor { get; set; } = AkronHudAnchor.BottomLeft;
    public bool NoShortNumbers { get; set; }
    public bool HideVanillaHud { get; set; }
    public bool HideAkronHud { get; set; }
    public bool CustomHudLabels { get; set; }
    public bool CustomHudLabelsInNonLevelScenes { get; set; }
    public int CustomHudLabelPadding { get; set; } = 5;
    public int CustomHudLabelGap { get; set; } = 8;
    public bool CustomHudLabelObstructionEnabled { get; set; }
    public AkronLabelObstructionMode CustomHudLabelObstructionMode { get; set; } = AkronLabelObstructionMode.Fade;
    public int CustomHudLabelObstructedOpacity { get; set; } = 35;
    public int CustomHudLabelObstructionPaddingPixels { get; set; } = 100;
    public bool CustomHudLabelObstructionOnlyOverlappedLabel { get; set; }
    public AkronHudAnchor CustomHudLabelObstructedAnchor { get; set; } = AkronHudAnchor.BottomRight;
    public int CustomHudLabelObstructedOffsetX { get; set; }
    public int CustomHudLabelObstructedOffsetY { get; set; }
    public int CustomHudLabelIndex { get; set; }
    public List<AkronCustomHudLabel> CustomHudLabelDefinitions { get; set; } = AkronCustomHudLabels.BuildDefaultLabels();
    public List<string> LabelRowOrder { get; set; } = AkronModuleSettings.BuildDefaultLabelRowOrder();
    public AkronHudLabelStyleSettings LabelBulkStyle { get; set; } = new AkronHudLabelStyleSettings();
    public AkronHudLabelStyleSettings RoomLabelStyle { get; set; } = new AkronHudLabelStyleSettings();
    public AkronHudLabelStyleSettings InputHistoryLabelStyle { get; set; } = new AkronHudLabelStyleSettings();
    public AkronHudLabelStyleSettings InputsPerSecondLabelStyle { get; set; } = new AkronHudLabelStyleSettings();
    public AkronHudLabelStyleSettings StartPosLabelStyle { get; set; } = new AkronHudLabelStyleSettings();
    public AkronHudLabelStyleSettings RoomTimerLabelStyle { get; set; } = new AkronHudLabelStyleSettings();
    public AkronHudLabelStyleSettings DeathStatsLabelStyle { get; set; } = new AkronHudLabelStyleSettings();
    public AkronHudLabelStyleSettings TotalAttemptsLabelStyle { get; set; } = new AkronHudLabelStyleSettings();
    public AkronHudLabelStyleSettings StatusLabelsLabelStyle { get; set; } = new AkronHudLabelStyleSettings();
    public AkronHudLabelStyleSettings ToastLabelStyle { get; set; } = new AkronHudLabelStyleSettings();
    public bool HudCheatIndicator { get; set; }
    public bool HudCheatIndicatorOnlyFlagged { get; set; }
    public int HudCheatIndicatorScale { get; set; } = 100;
    public int HudCheatIndicatorOpacity { get; set; } = 100;
    public AkronHudAnchor HudCheatIndicatorAnchor { get; set; } = AkronHudAnchor.TopLeft;
    public AkronHudCheatIndicatorStyle HudCheatIndicatorStyle { get; set; } = AkronHudCheatIndicatorStyle.Dot;
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
    public int RefillClarityColor { get; set; } = AkronModuleSettings.DefaultRefillClarityColor;
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
    public bool ShowHitboxTrail { get; set; }
    public int HitboxTrailLength { get; set; } = 30;
    public int HitboxTrailOpacity { get; set; } = 55;
    public float HitboxLineThickness { get; set; } = 5f;
    public int HitboxFillOpacity { get; set; }
    public bool HitboxBlackOutline { get; set; }
    public int HitboxPlayerColor { get; set; } = AkronModuleSettings.DefaultHitboxPlayerColor;
    public bool HitboxShowPlayerHurtbox { get; set; } = true;
    public int HitboxPlayerHurtboxColor { get; set; } = AkronModuleSettings.DefaultHitboxPlayerHurtboxColor;
    public int HitboxSolidColor { get; set; } = AkronModuleSettings.DefaultHitboxSolidColor;
    public int HitboxHazardColor { get; set; } = AkronModuleSettings.DefaultHitboxHazardColor;
    public int HitboxTriggerColor { get; set; } = AkronModuleSettings.DefaultHitboxTriggerColor;
    public int HitboxOtherColor { get; set; } = AkronModuleSettings.DefaultHitboxOtherColor;
    public int HitboxDeathColor { get; set; } = AkronModuleSettings.DefaultHitboxDeathColor;
    public int HitboxDeathPlayerColor { get; set; } = AkronModuleSettings.DefaultHitboxDeathPlayerColor;
    public bool FixHitboxPixels { get; set; }
    public bool ShowTriggers { get; set; }
    public bool EntityInspector { get; set; }
    public bool FrameStepper { get; set; }
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
    public bool AutoKillTimer { get; set; }
    public int AutoKillSeconds { get; set; } = 60;
    public bool AutoKillArea { get; set; }
    public bool AutoKillShowArea { get; set; } = true;
    public bool AutoKillShowAreaOnDeath { get; set; }
    public List<AkronRectangleData> AutoKillAreas { get; set; } = new List<AkronRectangleData>();
    public int AutoKillAreaX { get; set; }
    public int AutoKillAreaY { get; set; }
    public int AutoKillAreaWidth { get; set; }
    public int AutoKillAreaHeight { get; set; }
    public bool AutoKillSpeedCondition { get; set; }
    public int AutoKillMinSpeed { get; set; }
    public int AutoKillMaxSpeed { get; set; } = 1000;
    public bool AutoKillHorizontalSpeedCondition { get; set; }
    public int AutoKillMinHorizontalSpeed { get; set; }
    public int AutoKillMaxHorizontalSpeed { get; set; } = 1000;
    public bool AutoKillVerticalSpeedCondition { get; set; }
    public int AutoKillMinVerticalSpeed { get; set; }
    public int AutoKillMaxVerticalSpeed { get; set; } = 1000;
    public bool AutoKillDashCountCondition { get; set; }
    public int AutoKillDashCount { get; set; }
    public AkronAutoKillGroundCondition AutoKillGroundCondition { get; set; }
    public AkronAutoKillAxisCondition AutoKillHorizontalDirection { get; set; }
    public AkronAutoKillAxisCondition AutoKillVerticalDirection { get; set; }
    public bool AutoKillPlayerStateCondition { get; set; }
    public int AutoKillPlayerState { get; set; }
    public bool AutoKillInvertConditions { get; set; }
    public bool AutoDeafen { get; set; }
    public string AutoDeafenHotkey { get; set; } = AkronModuleSettings.DefaultAutoDeafenHotkey;
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
    public bool Noclip { get; set; }
    public int NoclipSpeed { get; set; } = 240;
    public int NoclipFloatSpeed { get; set; } = 90;
    public bool NoclipDrawOnTop { get; set; }
    public bool NoclipHidePlayer { get; set; }
    public bool NoclipAccuracy { get; set; }
    public int NoclipAccuracyInvalidLimit { get; set; } = AkronModuleSettings.DefaultHazardAccuracyInvalidLimit;
    public bool NoclipAccuracyTint { get; set; } = AkronModuleSettings.DefaultHazardAccuracyTint;
    public AkronNoclipAccuracyTintMode NoclipAccuracyTintMode { get; set; } = AkronModuleSettings.DefaultHazardAccuracyTintMode;
    public int NoclipAccuracyTintColor { get; set; } = AkronModuleSettings.DefaultHazardAccuracyTintColor;
    public int NoclipAccuracyTintOpacity { get; set; } = AkronModuleSettings.DefaultHazardAccuracyTintOpacity;
    public int NoclipAccuracyTintDurationMs { get; set; } = AkronModuleSettings.DefaultHazardAccuracyTintDurationMs;
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
    public bool AudioSpeed { get; set; }
    public AkronAudioSpeedPolicy AudioSpeedPolicy { get; set; } = AkronAudioSpeedPolicy.SyncTimescale;
    public float AudioSpeedMultiplier { get; set; } = 1f;
    public bool PitchShift { get; set; }
    public AkronPitchPolicy PitchShiftPolicy { get; set; } = AkronPitchPolicy.Preserve;
    public float PitchShiftMultiplier { get; set; } = 1f;
    public bool FpsBypass { get; set; }
    public int FpsBypassTarget { get; set; } = 120;
    public bool TpsBypass { get; set; }
    public int TpsBypassTarget { get; set; } = 60;
    public AkronFrameIncreaseMethod FrameBypassMethod { get; set; } = AkronFrameIncreaseMethod.Interval;
    public AkronCameraSmoothingMode FrameBypassCameraSmoothing { get; set; } = AkronCameraSmoothingMode.Fancy;
    public AkronObjectSmoothingMode FrameBypassObjectSmoothing { get; set; } = AkronObjectSmoothingMode.Extrapolate;
    public bool FrameBypassTasMode { get; set; }
    public bool FrameBypassSubpixelMadeline { get; set; } = true;
    public bool FrameBypassSmoothBackground { get; set; } = true;
    public bool FrameBypassSmoothForeground { get; set; } = true;
    public bool FrameBypassHideStretchedEdges { get; set; } = true;
    public bool FrameBypassSillyMode { get; set; }
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
    public bool ClickTeleport { get; set; }
    public bool EverestSafeAutoBlock { get; set; }
    public bool SaveTimeAndDeaths { get; set; }
    public bool UnsafeSavestateOverride { get; set; }
    public int ScreenshotScale { get; set; } = 1;
    public bool ScreenshotStatus { get; set; }
    public string ScreenshotScannerExportPath { get; set; } = AkronModuleSettings.DefaultScreenshotScannerExportPath;
    public AkronScreenshotImageFormat ScreenshotScannerImageFormat { get; set; } = AkronScreenshotImageFormat.Png;
    public bool ScreenshotScannerExportMarkers { get; set; }
    public bool ScreenshotScannerExportStartPositions { get; set; } = true;
    public bool ScreenshotScannerExportAutoKillAreas { get; set; } = true;
    public bool ScreenshotScannerExportAutoDeafenAreas { get; set; } = true;
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
    public string RecordingOutputFolder { get; set; } = string.Empty;
    public string RecordingFilenameTemplate { get; set; } = AkronModuleSettings.DefaultRecordingFilenameTemplate;
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
    public int RecordingFramerate { get; set; } = 60;
    public float RecordingEndscreenDurationSeconds { get; set; } = 3.4f;
    public int RecordingBitrateMbps { get; set; } = 30;
    public int RecordingResolutionX { get; set; } = 1920;
    public int RecordingResolutionY { get; set; } = 1080;
    public bool RecordingHidePreview { get; set; }
    public AkronRecordingCodec RecordingCodec { get; set; } = AkronRecordingCodec.Libx264;
    public string RecordingColorspaceArgs { get; set; } = string.Empty;
    public AkronRecordingPreset RecordingPreset { get; set; } = AkronRecordingPreset.Cpu;
    public Dictionary<string, int> SoundVolumes { get; set; } = AkronEarAid.CreateDefaultVolumes();
    public Dictionary<string, bool> SoundVolumeOverrides { get; set; } = AkronEarAid.CreateDefaultOverrideToggles();
    public bool AudioSplitter { get; set; }
    public string AudioSplitterMainDevice { get; set; } = "Default";
    public string AudioSplitterMusicDevice { get; set; } = "Default";
    public string AudioSplitterSfxDevice { get; set; } = "Default";

    public void SetLowDistractionChannels(bool enabled) {
        LowDistractionOverlay = enabled;
        NoParticles = enabled;
        NoTrails = enabled;
        NoGlitch = enabled;
        NoAnxiety = enabled;
        NoDistortion = enabled;
    }
}
