using System.Collections.Generic;

namespace Celeste.Mod.Akron;

public partial class AkronModuleSettings {
    public static AkronHudLabelStyleSettings CloneLabelStyle(AkronHudLabelStyleSettings style) {
        if (style == null) {
            return new AkronHudLabelStyleSettings();
        }

        return new AkronHudLabelStyleSettings {
            OffsetX = ClampValue(style.OffsetX, -1920, 1920),
            OffsetY = ClampValue(style.OffsetY, -1080, 1080),
            Scale = ClampPercent(style.Scale, 50, 250),
            Opacity = ClampOpacity(style.Opacity),
            LineSpacing = ClampCustomLabelLineSpacing(style.LineSpacing),
            Shadow = style.Shadow,
            ShadowColor = ClampRgb(style.ShadowColor),
            ShadowOpacity = ClampOpacity(style.ShadowOpacity),
            ShadowOffsetX = ClampValue(style.ShadowOffsetX, -24, 24),
            ShadowOffsetY = ClampValue(style.ShadowOffsetY, -24, 24)
        };
    }

    public bool RoomLabels { get; set; }
    public bool LabelSystemVisible { get; set; }
    public int RoomLabelColor { get; set; } = 0xFFFFFF;
    public bool StaminaWidget { get; set; }
    public bool SpeedWidget { get; set; }
    public bool DashWidget { get; set; }
    public bool InputViewer { get; set; }
    public bool InputHistoryPanel { get; set; }
    public int InputHistoryLength { get; set; } = 8;
    public AkronHudPlacement InputHistoryPlacement { get; set; } = AkronHudPlacement.Left;
    public int InputHistoryOpacity { get; set; } = 72;
    public int InputHistoryTextColor { get; set; } = 0xFFFFFF;
    public int InputHistoryEventColor { get; set; } = 0xFFFFFF;
    public bool InputHistoryCompact { get; set; } = true;
    public bool InputHistoryPinOnDeath { get; set; } = true;
    public bool InputHistoryShowOnDeath { get; set; }
    public bool InputHistoryShowTransitions { get; set; }
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
    public bool ResourceBars { get; set; }
    public bool ResourceStaminaBar { get; set; } = true;
    public bool ResourceDashPips { get; set; } = true;
    public int LowStaminaThreshold { get; set; } = 20;
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
    public string DeathStatsFormat { get; set; } = DefaultDeathStatsFormat;
    public AkronDeathStatsVisibility DeathStatsVisibility { get; set; } = AkronDeathStatsVisibility.AfterDeathAndInMenu;
    public int DeathStatsColor { get; set; } = 0xFFFFFF;
    public bool TotalAttemptsWidget { get; set; }
    public int TotalAttemptsColor { get; set; } = 0xFFFFFF;
    public bool StatusLabelsWidget { get; set; }
    public int StatusLabelsColor { get; set; } = 0xFFFFFF;
    public bool ToastLabels { get; set; }
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
    public List<string> LabelRowOrder { get; set; } = BuildDefaultLabelRowOrder();
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
}
