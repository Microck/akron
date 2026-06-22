using System;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.Akron;

public enum AkronStatus {
    Unclassified = 0,
    GoldberryHardlistClean = 1,
    RegularClean = 2,
    Cheat = 3
}

public enum IndicatorVisibility {
    Hidden,
    ShowWhenFlagged,
    Always
}

public enum IndicatorCorner {
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

public enum AkronHudAnchor {
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    Center,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
    Absolute
}

public enum AkronHudPlacement {
    Left,
    Right
}

public enum AkronInspectorPinPlacement {
    NearClick,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    Custom
}

public enum AkronStaminaPlayerBarPosition {
    Above,
    Below
}

public enum AkronStaminaHudPosition {
    TopLeft,
    TopCenter,
    TopRight,
    BottomRight,
    BottomCenter,
    BottomLeft
}

public enum AkronStaminaBarStyle {
    Bar,
    Ring
}

public enum AkronDashBarStyle {
    Pips,
    Bar
}

public enum AkronSpeedNumberMode {
    Total,
    Horizontal,
    Vertical
}

public enum AkronFrameIncreaseMethod {
    Interval,
    Dynamic
}

public enum AkronLoggingLevel {
    Normal = 0,
    Verbose = 1,
    Diagnostic = 2,
    Trace = 3
}

public enum AkronCameraSmoothingMode {
    Fancy,
    Fast,
    Off
}

public enum AkronObjectSmoothingMode {
    Extrapolate,
    Interpolate
}

public readonly struct AkronFrameBypassRates {
    public AkronFrameBypassRates(bool active, int updateRate, int drawRate, int requestedDrawRate) {
        Active = active;
        UpdateRate = updateRate;
        DrawRate = drawRate;
        RequestedDrawRate = requestedDrawRate;
    }

    public bool Active { get; }
    public int UpdateRate { get; }
    public int DrawRate { get; }
    public int RequestedDrawRate { get; }

    public string Describe() {
        return Active
            ? DrawRate.ToString(System.Globalization.CultureInfo.InvariantCulture) + " FPS / " +
              UpdateRate.ToString(System.Globalization.CultureInfo.InvariantCulture) + " TPS"
            : "Off";
    }
}

public sealed class AkronRectangleData {
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public AkronRectangleData() {
    }

    public AkronRectangleData(Rectangle rectangle) {
        X = rectangle.X;
        Y = rectangle.Y;
        Width = rectangle.Width;
        Height = rectangle.Height;
    }

    public Rectangle ToRectangle() {
        return new Rectangle(X, Y, Width, Height);
    }
}

public sealed class AkronAutoKillAreaData {
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool SpeedCondition { get; set; }
    public int MinSpeed { get; set; }
    public int MaxSpeed { get; set; } = 1000;
    public bool HorizontalSpeedCondition { get; set; }
    public int MinHorizontalSpeed { get; set; }
    public int MaxHorizontalSpeed { get; set; } = 1000;
    public bool VerticalSpeedCondition { get; set; }
    public int MinVerticalSpeed { get; set; }
    public int MaxVerticalSpeed { get; set; } = 1000;
    public bool DashCountCondition { get; set; }
    public int DashCount { get; set; }
    public AkronAutoKillGroundCondition GroundCondition { get; set; }
    public AkronAutoKillAxisCondition HorizontalDirection { get; set; }
    public AkronAutoKillAxisCondition VerticalDirection { get; set; }
    public bool PlayerStateCondition { get; set; }
    public int PlayerState { get; set; }
    public bool InvertConditions { get; set; }

    public AkronAutoKillAreaData() {
    }

    public AkronAutoKillAreaData(Rectangle rectangle) {
        X = rectangle.X;
        Y = rectangle.Y;
        Width = rectangle.Width;
        Height = rectangle.Height;
    }

    public AkronAutoKillAreaData(AkronAutoKillAreaData source) {
        if (source == null) {
            return;
        }

        X = source.X;
        Y = source.Y;
        Width = source.Width;
        Height = source.Height;
        SpeedCondition = source.SpeedCondition;
        MinSpeed = source.MinSpeed;
        MaxSpeed = source.MaxSpeed;
        HorizontalSpeedCondition = source.HorizontalSpeedCondition;
        MinHorizontalSpeed = source.MinHorizontalSpeed;
        MaxHorizontalSpeed = source.MaxHorizontalSpeed;
        VerticalSpeedCondition = source.VerticalSpeedCondition;
        MinVerticalSpeed = source.MinVerticalSpeed;
        MaxVerticalSpeed = source.MaxVerticalSpeed;
        DashCountCondition = source.DashCountCondition;
        DashCount = source.DashCount;
        GroundCondition = source.GroundCondition;
        HorizontalDirection = source.HorizontalDirection;
        VerticalDirection = source.VerticalDirection;
        PlayerStateCondition = source.PlayerStateCondition;
        PlayerState = source.PlayerState;
        InvertConditions = source.InvertConditions;
    }

    public Rectangle ToRectangle() {
        return new Rectangle(X, Y, Width, Height);
    }

    public AkronAutoKillAreaData CopyWithRectangle(Rectangle rectangle) {
        AkronAutoKillAreaData copy = new AkronAutoKillAreaData(this) {
            X = rectangle.X,
            Y = rectangle.Y,
            Width = rectangle.Width,
            Height = rectangle.Height
        };
        return copy;
    }
}

public readonly struct AkronNoclipAccuracySnapshot {
    public AkronNoclipAccuracySnapshot(int samples, int invalidSamples, int invalidEntries, int invalidLimit, bool invalidNow) {
        Samples = samples;
        InvalidSamples = invalidSamples;
        InvalidEntries = invalidEntries;
        InvalidLimit = invalidLimit;
        InvalidNow = invalidNow;
    }

    public int Samples { get; }
    public int InvalidSamples { get; }
    public int InvalidEntries { get; }
    public int InvalidLimit { get; }
    public bool InvalidNow { get; }
    public int ValidSamples => System.Math.Max(0, Samples - InvalidSamples);
    public bool LimitExceeded => InvalidLimit > 0 && InvalidEntries >= InvalidLimit;
    public float Accuracy => Samples <= 0 ? 100f : ValidSamples * 100f / Samples;

    public string Describe() {
        return Accuracy.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%, " +
               InvalidEntries.ToString(System.Globalization.CultureInfo.InvariantCulture) + " invalid entries";
    }
}

public enum AkronTrailVisibility {
    Vanilla,
    Hidden,
    Always
}

public enum AkronCustomTrailMode {
    Fixed,
    Rainbow
}

public enum AkronMadelineEffectSyncMode {
    Off,
    MatchHair
}

public enum AkronNoclipAccuracyTintMode {
    OnInvalidEntry,
    WhileTouching
}

public enum AkronInvincibilityMode {
    Akron,
    Native
}

public enum AkronNoDeathWipeMode {
    DeathOnly,
    AllWipes
}

public enum AkronRoomStatTimerFreezeMode {
    Never,
    Paused,
    Inactive,
    Cutscene,
    PausedOrInactive,
    PausedInactiveOrCutscene
}

public enum AkronCursorZoomActivationMode {
    Hold,
    Toggle
}

public enum AkronCursorToolsClickAction {
    ClickTeleport,
    InspectorPin
}

[Flags]
public enum AkronDashRedirectDirection {
    None = 0,
    Down = 1 << 0,
    DownLeft = 1 << 1,
    DownRight = 1 << 2,
    Left = 1 << 3,
    Right = 1 << 4,
    UpLeft = 1 << 5,
    Up = 1 << 6,
    UpRight = 1 << 7,
    All = Down | DownLeft | DownRight | Left | Right | UpLeft | Up | UpRight
}

public enum AkronAutoKillGroundCondition {
    Any,
    Grounded,
    Airborne
}

public enum AkronAutoKillAxisCondition {
    Any,
    Negative,
    Positive,
    Zero
}

public enum AkronOverlayThemePreset {
    Default,
    Monochrome,
    HighContrast,
    Midnight,
    Crimson,
    Terminal,
    Symbiote,
    Carbon,
    Retro,
    Coniferous,
    Wine,
    Custom
}

public enum AkronAudioSpeedPolicy {
    Normal,
    SyncTimescale,
    Independent
}

public enum AkronPitchPolicy {
    Preserve,
    FollowSpeed,
    Independent
}

public enum AkronInputBoardSource {
    GameActions,
    KeyboardKeys
}

public enum AkronInputBoardLabelPreset {
    Short,
    Names,
    Keyboard,
    Arrows
}

public enum AkronLabelEventMode {
    Always,
    OnDeath,
    OnButtonHold,
    OnNoclipDeath
}

public enum AkronLabelFontTheme {
    Tiny,
    Small,
    Default,
    Large,
    Huge
}

public enum AkronLabelTextAlignment {
    Left,
    Center,
    Right
}

public enum AkronLabelObstructionMode {
    Off,
    Fade,
    Move
}

public enum AkronStartPosLabelFormat {
    Prefix,
    CountOnly,
    SlotAndCount
}

public enum AkronStartPosFacing {
    Current,
    Left,
    Right
}

public enum AkronScreenshotImageFormat {
    Png,
    Jpeg
}

public enum AkronCounterDisplayMode {
    Off,
    Session,
    Chapter,
    File,
    Both
}

public enum AkronCoreModeOverride {
    Hot,
    Cold
}

public enum AkronCoreModeClickBehavior {
    Toggle,
    Cycle
}

public sealed class AkronSetInventorySnapshot {
    public int SessionInventoryDashes { get; set; }
    public int SessionDashes { get; set; }
    public int PlayerDashes { get; set; }
    public bool JumpHack { get; set; }
    public bool JumpHackInfinite { get; set; }
    public int JumpHackExtraJumps { get; set; }
    public bool JumpHackAllowVerticalDashJumps { get; set; }
}

public readonly struct AkronStartPosEntry {
    public AkronStartPosEntry(int slot, AkronStartPos startPos) {
        Slot = slot;
        StartPos = startPos;
    }

    public int Slot { get; }
    public AkronStartPos StartPos { get; }
}

public enum AkronHudCheatIndicatorStyle {
    Text,
    Dot
}

public enum AkronDeathStatsVisibility {
    Disabled,
    AfterDeath,
    InMenu,
    AfterDeathAndInMenu,
    Always
}

public enum AkronRecordingContainerFormat {
    Mkv,
    Mp4,
    Mov,
    WebM
}

public enum AkronRecordingCodec {
    Libx264,
    H264Nvenc,
    H264Amf,
    HevcNvenc,
    LibVpxVp9
}

public enum AkronRecordingPreset {
    Cpu,
    Nvidia,
    Amd
}

public enum AkronRecordingReplayAutoStart {
    Off,
    InLevels,
    Always
}

public enum AkronRecordingQualityPreset {
    LowImpact,
    Balanced,
    HighQuality,
    Lossless
}

public enum AkronRecordingRateControl {
    Cbr,
    Vbr,
    Cqp,
    Crf,
    Lossless
}

public enum AkronRecordingClipSort {
    Date,
    Chapter,
    Room,
    Death,
    Clear,
    Pb,
    Favorite
}

public enum AkronRecordingClipFilter {
    All,
    Chapter,
    Room,
    Death,
    Clear,
    Pb,
    Favorite
}

public sealed class AkronCustomHudLabel {
    public string Id { get; set; } = System.Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Label";
    public string Text { get; set; } = "{room}";
    public bool Visible { get; set; } = true;
    public AkronHudAnchor Anchor { get; set; } = AkronHudAnchor.TopLeft;
    public bool AbsolutePosition { get; set; }
    public int X { get; set; } = 48;
    public int Y { get; set; } = 72;
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
    public float Scale { get; set; } = 0.42f;
    public int Color { get; set; } = 0xFFFFFF;
    public int Opacity { get; set; } = 100;
    public int LineSpacing { get; set; } = 100;
    public AkronLabelFontTheme Font { get; set; } = AkronLabelFontTheme.Default;
    public AkronLabelTextAlignment TextAlignment { get; set; } = AkronLabelTextAlignment.Left;
    public bool Shadow { get; set; } = true;
    public int ShadowColor { get; set; } = 0x000000;
    public int ShadowOpacity { get; set; } = 85;
    public int ShadowOffsetX { get; set; } = 2;
    public int ShadowOffsetY { get; set; } = 2;
    public AkronLabelEventMode EventMode { get; set; } = AkronLabelEventMode.Always;
    public float EventDelaySeconds { get; set; }
    public float EventDurationSeconds { get; set; } = 2f;
    public bool EventOverridesStyle { get; set; }
    public float EventScale { get; set; } = 0.6f;
    public int EventColor { get; set; } = 0xFF5F5F;
    public int EventOpacity { get; set; } = 100;
}

public sealed class AkronHudLabelStyleSettings {
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
    public int Scale { get; set; } = 100;
    public int Opacity { get; set; } = 100;
    public int LineSpacing { get; set; } = 100;
    public bool Shadow { get; set; } = true;
    public int ShadowColor { get; set; } = 0x000000;
    public int ShadowOpacity { get; set; } = 85;
    public int ShadowOffsetX { get; set; } = 2;
    public int ShadowOffsetY { get; set; } = 2;
}

public enum AkronFeatureKind {
    RoomLabelOverlay,
    StaminaWidget,
    SpeedWidget,
    DashWidget,
    InputViewer,
    InputHistory,
    ResourceBars,
    RoomTimer,
    DeathStats,
    ReducedVisualNoise,
    VisualTuning,
    GrabModeHotkey,
    ScreenshotTool,
    RetryHotkey,
    RoomReload,
    ChapterReload,
    DebugMapLauncher,
    MountainViewer,
    Savestates,
    BrokeredSavestates,
    TasHandoff,
    SplitHelper,
    DeloadSimulation,
    RoomWarp,
    HitboxViewer,
    EntityInspector,
    FlagInspector,
    RespawnTime,
    FrameAdvance,
    Freeze,
    Timescale,
    AutoKill,
    AutoDeafen,
    TransitionSpeed,
    LowVolumeBypass,
    HudVisibility,
    PauseMenuVisibility,
    PauseCountdown,
    ShowTrajectory,
    FreeCamera,
    AudioSpeed,
    PitchShift,
    FpsBypass,
    TpsBypass,
    SafeModeStats,
    Screenshake,
    TriggerViewer,
    StartPosTools,
    ClickTeleport,
    CustomTrail,
    MadelineHairLength,
    MadelineEffectSync,
    HidePlayer,
    DeathVisuals,
    RespawnAnimation,
    ShowTaps,
    InputsPerSecondCounter,
    CustomHudLabels,
    InstantComplete,
    UnlockSystem,
    HazardAccuracy,
    Noclip,
    Invincibility,
    InfiniteStamina,
    InfiniteDash,
    DashCountOverride,
    SpeedNumber,
    RefillClarity,
    FreezeFrames,
    GroundRefillRules,
    MovementStatMutation,
    PauseTimerFreeze,
    InputAssistShortcut,
    ExtendedVariantMode,
    InternalRecorder,
    FastLookout,
    LevelEnterSkip,
    DeathPbLossRestart,
    CameraOffset,
    CursorTools,
    CursorZoom,
    UnsafeNativeSavestateOverride,
    SubmissionMode,
    ProofRecorderGuard,
    EndScreenHelper,
    PauseTracker,
    MapVersionStamp,
    GoldenStartHelper,
    GoldenTransparency,
    LagPauser,
    Logging,
    JournalSnapshotCompare,
    Backups
}

public readonly struct FeatureDefinition {
    public FeatureDefinition(AkronFeatureKind kind, AkronStatus classification, string label, string reason) {
        Kind = kind;
        Classification = classification;
        Label = label;
        Reason = reason;
    }

    public AkronFeatureKind Kind { get; }
    public AkronStatus Classification { get; }
    public string Label { get; }
    public string Reason { get; }
}
