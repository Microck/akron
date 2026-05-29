using Celeste;
using ImGuiNET;
using Monocle;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private void DrawScreenshakePopupControls(string popupId) {
        DrawIntStepperRow(
            "Intensity",
            () => AkronModule.Settings.ScreenshakeIntensity,
            value => AkronModule.Settings.ScreenshakeIntensity = AkronModuleSettings.ClampScreenshakeIntensity(value),
            -10,
            10,
            0,
            100,
            popupId,
            "Screenshake override intensity. 0 disables native Celeste camera shake while Screenshake is enabled.");
    }

    private void DrawLightLevelPopupControls(string popupId) {
        DrawIntStepperRow(
            "Percent",
            () => AkronModule.Settings.LightLevelPercent,
            value => AkronModule.Settings.LightLevelPercent = AkronModuleSettings.ClampLightLevelPercent(value),
            -5,
            5,
            0,
            100,
            popupId,
            "100% is fully lit; 0% is fully dark. This value only applies while Light Level is on.");
    }

    private void DrawBloomLevelPopupControls(string popupId) {
        DrawIntStepperRow(
            "Percent",
            () => AkronModule.Settings.BloomLevelPercent,
            value => AkronModule.Settings.BloomLevelPercent = AkronModuleSettings.ClampBloomLevelPercent(value),
            -5,
            5,
            0,
            300,
            popupId,
            "0% removes bloom; values over 100% amplify glow. This value only applies while Bloom Level is on.");
    }

    private void DrawScreenTintPopupControls(string popupId) {
        DrawIntStepperRow(
            "Opacity",
            () => AkronModule.Settings.ScreenTintOpacity,
            value => AkronModule.Settings.ScreenTintOpacity = AkronModuleSettings.ClampOpacity(value),
            -5,
            5,
            0,
            100,
            popupId,
            "Screen tint opacity percentage.");

        DrawHitboxColorRow("Tint", () => AkronModule.Settings.ScreenTintColor, value => AkronModule.Settings.ScreenTintColor = AkronModuleSettings.ClampRgb(value), popupId, "Screen tint color.");
    }

    private void DrawGoldenStartPopupControls(string popupId) {
        Level level = Engine.Scene as Level;
        ImGui.TextUnformatted(level == null ? "Unavailable outside a level." : "Status: " + AkronActions.DescribeGoldenStartHelper(level));
        ImGui.TextUnformatted("Used this session: " + (AkronModule.Session?.UsedGoldenStartHelper == true ? "Yes" : "No"));

        bool canUse = level != null && AkronActions.IsGoldenStartHelperSafe(level);
        if (!canUse) {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("Run Golden Start##" + popupId) && level != null) {
            AkronActions.GiveGoldenFromStart(level);
        }

        if (!canUse) {
            ImGui.EndDisabled();
        }

        DrawPopupTooltip("Runs Celeste's give_golden helper only when Akron detects the first-room start context.");
    }

    private void DrawGoldenTransparencyPopupControls(string popupId) {
        DrawIntStepperRow(
            "Opacity",
            () => AkronModule.Settings.GoldenTransparencyOpacity,
            value => AkronModule.Settings.GoldenTransparencyOpacity = AkronModuleSettings.ClampGoldenTransparencyOpacity(value),
            -5,
            5,
            0,
            100,
            popupId,
            "Golden berry and follower opacity percentage used while Golden Transparency is toggled on.");

        if (ImGui.Button("Reset opacity##" + popupId)) {
            AkronModule.Settings.GoldenTransparencyOpacity = 55;
        }
        DrawPopupTooltip("Restore the default 55% golden transparency opacity.");
    }

    private void DrawFreeCameraPopupControls(string popupId) {
        DrawIntStepperRow(
            "Speed",
            () => AkronModule.Settings.FreeCameraSpeed,
            value => AkronModule.Settings.FreeCameraSpeed = AkronModuleSettings.ClampFreeCameraSpeed(value),
            -20,
            20,
            20,
            2000,
            popupId,
            "Camera pan speed in world pixels per second.");

        bool freezeGameplay = AkronModule.Settings.FreeCameraFreezeGameplay;
        if (ImGui.Checkbox("Freeze gameplay##" + popupId, ref freezeGameplay)) {
            AkronModule.Settings.FreeCameraFreezeGameplay = freezeGameplay;
        }
        DrawPopupTooltip("Pause the whole level while free camera is active. Madeline is not player-controlled either way.", "Freeze gameplay");
    }

    private void DrawCameraOffsetPopupControls(string popupId) {
        DrawIntStepperRow(
            "Offset X",
            () => AkronModule.Settings.CameraOffsetX,
            value => {
                AkronModule.Settings.CameraOffsetX = AkronModuleSettings.ClampCameraOffset(value);
                if (Engine.Scene is Level level && AkronModule.Settings.CameraOffset) {
                    AkronActions.ApplyCameraOffset(level);
                }
            },
            -1,
            1,
            -20,
            20,
            popupId,
            "Configured horizontal camera offset. The level camera only uses this while Camera Offset is toggled on.");

        DrawIntStepperRow(
            "Offset Y",
            () => AkronModule.Settings.CameraOffsetY,
            value => {
                AkronModule.Settings.CameraOffsetY = AkronModuleSettings.ClampCameraOffset(value);
                if (Engine.Scene is Level level && AkronModule.Settings.CameraOffset) {
                    AkronActions.ApplyCameraOffset(level);
                }
            },
            -1,
            1,
            -20,
            20,
            popupId,
            "Configured vertical camera offset. The level camera only uses this while Camera Offset is toggled on.");

        if (ImGui.Button("Reset offset##" + popupId)) {
            AkronActions.ResetCameraOffset(Engine.Scene as Level);
        }
        DrawPopupTooltip("Return the configured offset to 0,0 and restore the live camera offset.");
    }

    private void DrawCursorZoomPopupControls(string popupId) {
        DrawIntStepperRow(
            "Zoom",
            () => AkronModule.Settings.CursorZoomPercent,
            value => AkronModule.Settings.CursorZoomPercent = AkronModuleSettings.ClampCursorZoomPercent(value, AkronModule.Settings.CursorZoomAllowZoomOut),
            -10,
            10,
            AkronModule.Settings.CursorZoomAllowZoomOut ? 25 : 100,
            32000,
            popupId,
            "Whole-screen level zoom percentage. Hold " + AkronModuleSettings.DescribeBinding(AkronModule.Settings.CursorZoomHold) + " and scroll to zoom toward the cursor.");

        DrawPopupCheckbox(
            "Allow zoom out",
            () => AkronModule.Settings.CursorZoomAllowZoomOut,
            value => {
                AkronModule.Settings.CursorZoomAllowZoomOut = value;
                AkronModule.Settings.CursorZoomPercent = AkronModuleSettings.ClampCursorZoomPercent(AkronModule.Settings.CursorZoomPercent, value);
            },
            popupId,
            "Allow Cursor Zoom to go below the normal 100% view. Off keeps the maximum zoom-out at the original game view.");

        DrawCursorZoomModeRadio(
            "Hold",
            AkronCursorZoomActivationMode.Hold,
            popupId,
            "Apply zoom only while the Cursor Zoom bind is held.");
        DrawCursorZoomModeRadio(
            "Toggle",
            AkronCursorZoomActivationMode.Toggle,
            popupId,
            "Press the Cursor Zoom bind to toggle the zoomed view on or off.");

        DrawPopupCheckbox(
            "Reset on deactivate",
            () => AkronModule.Settings.CursorZoomResetOnDeactivate,
            value => AkronModule.Settings.CursorZoomResetOnDeactivate = value,
            popupId,
            "Reset the configured zoom back to 100% whenever Hold is released or Toggle is turned off.");

        DrawIntStepperRow(
            "Scroll step",
            () => AkronModule.Settings.CursorZoomStepPercent,
            value => AkronModule.Settings.CursorZoomStepPercent = AkronModuleSettings.ClampCursorZoomStepPercent(value),
            -1,
            1,
            1,
            100,
            popupId,
            "Percent added or removed for each mouse-wheel notch.");

        if (ImGui.Button("Reset zoom##" + popupId)) {
            AkronModule.Settings.CursorZoomPercent = 100;
            AkronModule.ResetCursorZoom(Engine.Scene as Level);
        }
        DrawPopupTooltip("Return the level camera to neutral 1.0x without changing whether Cursor Zoom is enabled.");
    }

    private void DrawCursorZoomModeRadio(string label, AkronCursorZoomActivationMode mode, string popupId, string tooltip) {
        bool selected = AkronModule.Settings.CursorZoomActivationMode == mode;
        DrawPopupRowLabel(label, CalculatePopupLabelWidth(24f));
        if (ImGui.RadioButton("##cursor-zoom-mode-" + label + popupId, selected)) {
            if (!selected) {
                AkronModule.Settings.CursorZoomActivationMode = AkronCursorZoomActivationMode.Hold;
                if (mode == AkronCursorZoomActivationMode.Toggle) {
                    AkronModule.Settings.CursorZoomActivationMode = AkronCursorZoomActivationMode.Toggle;
                }

                AkronModule.ResetCursorZoom(Engine.Scene as Level);
            }
        }
        DrawPopupTooltip(tooltip, label);
    }

    private void DrawShowTrajectoryPopupControls(string popupId) {
        bool mapAware = AkronModule.Settings.ShowTrajectoryMapAware;
        if (ImGui.Button("Mode: " + (mapAware ? "Map-aware" : "Simple") + "##" + popupId)) {
            AkronModule.Settings.ShowTrajectoryMapAware = !mapAware;
        }
        DrawPopupTooltip("Simple uses the lightweight movement preview. Map-aware truncates the preview when the projected player hitbox reaches selected map collisions.");

        bool stopOnSolids = AkronModule.Settings.ShowTrajectoryStopOnSolids;
        if (ImGui.Checkbox("Stop on solids##" + popupId, ref stopOnSolids)) {
            AkronModule.Settings.ShowTrajectoryStopOnSolids = stopOnSolids;
        }
        DrawPopupTooltip("In map-aware mode, stop the trajectory when it reaches solid map collision.");

        bool stopOnHazards = AkronModule.Settings.ShowTrajectoryStopOnHazards;
        if (ImGui.Checkbox("Stop on hazards##" + popupId, ref stopOnHazards)) {
            AkronModule.Settings.ShowTrajectoryStopOnHazards = stopOnHazards;
        }
        DrawPopupTooltip("In map-aware mode, stop the trajectory when it reaches spikes or hazard-style death objects.");

        DrawIntStepperRow(
            "Frames",
            () => AkronModule.Settings.ShowTrajectoryFrames,
            value => AkronModule.Settings.ShowTrajectoryFrames = AkronModuleSettings.ClampShowTrajectoryFrames(value),
            -10,
            10,
            1,
            1000,
            popupId,
            "Preview length. Green estimates jump-held, red estimates jump-released. Hidden while Free Camera is active.");

        DrawIntStepperRow(
            "Opacity",
            () => AkronModule.Settings.ShowTrajectoryOpacity,
            value => AkronModule.Settings.ShowTrajectoryOpacity = AkronModuleSettings.ClampOpacity(value),
            -5,
            5,
            0,
            100,
            popupId,
            "Trajectory opacity percentage.");

        DrawIntStepperRow(
            "Thickness",
            () => AkronModule.Settings.ShowTrajectoryLineThickness,
            value => AkronModule.Settings.ShowTrajectoryLineThickness = AkronModuleSettings.ClampShowTrajectoryLineThickness(value),
            -1,
            1,
            1,
            12,
            popupId,
            "Trajectory line thickness in screen pixels.");

        DrawIntStepperRow(
            "Hitbox step",
            () => AkronModule.Settings.ShowTrajectoryFrameHitboxInterval,
            value => AkronModule.Settings.ShowTrajectoryFrameHitboxInterval = AkronModuleSettings.ClampShowTrajectoryFrameHitboxInterval(value),
            -1,
            1,
            1,
            60,
            popupId,
            "Draw one predicted frame hitbox every N simulated frames when frame hitboxes are visible.");

        bool lines = AkronModule.Settings.ShowTrajectoryLines;
        if (ImGui.Checkbox("Path lines##" + popupId, ref lines)) {
            AkronModule.Settings.ShowTrajectoryLines = lines;
        }
        DrawPopupTooltip("Draw the red and green trajectory path lines.");

        bool lineShadow = AkronModule.Settings.ShowTrajectoryLineShadow;
        if (ImGui.Checkbox("Black shadow##" + popupId, ref lineShadow)) {
            AkronModule.Settings.ShowTrajectoryLineShadow = lineShadow;
        }
        DrawPopupTooltip("Draw black outlines/shadows behind trajectory lines, markers, and hitboxes.");

        bool pointMarkers = AkronModule.Settings.ShowTrajectoryPointMarkers;
        if (ImGui.Checkbox("Point markers##" + popupId, ref pointMarkers)) {
            AkronModule.Settings.ShowTrajectoryPointMarkers = pointMarkers;
        }
        DrawPopupTooltip("Draw small dots along each predicted path.");

        bool startMarker = AkronModule.Settings.ShowTrajectoryStartMarker;
        if (ImGui.Checkbox("Start marker##" + popupId, ref startMarker)) {
            AkronModule.Settings.ShowTrajectoryStartMarker = startMarker;
        }
        DrawPopupTooltip("Draw the square marker at the prediction start position.");

        bool frameHitboxes = AkronModule.Settings.ShowTrajectoryFrameHitboxes;
        if (ImGui.Checkbox("Frame hitboxes##" + popupId, ref frameHitboxes)) {
            AkronModule.Settings.ShowTrajectoryFrameHitboxes = frameHitboxes;
        }
        DrawPopupTooltip("Draw player hitboxes at regular predicted frames along each trajectory.");

        bool endMarkers = AkronModule.Settings.ShowTrajectoryEndMarkers;
        if (ImGui.Checkbox("End hitboxes##" + popupId, ref endMarkers)) {
            AkronModule.Settings.ShowTrajectoryEndMarkers = endMarkers;
        }
        DrawPopupTooltip("Draw predicted final player collider rectangles at the end of each path.");

        bool hitboxOutlines = AkronModule.Settings.ShowTrajectoryHitboxOutlines;
        if (ImGui.Checkbox("Hitbox outlines##" + popupId, ref hitboxOutlines)) {
            AkronModule.Settings.ShowTrajectoryHitboxOutlines = hitboxOutlines;
        }
        DrawPopupTooltip("Draw colored outlines around trajectory frame and end hitboxes.");

        bool hitboxFill = AkronModule.Settings.ShowTrajectoryHitboxFill;
        if (ImGui.Checkbox("Hitbox fill##" + popupId, ref hitboxFill)) {
            AkronModule.Settings.ShowTrajectoryHitboxFill = hitboxFill;
        }
        DrawPopupTooltip("Fill trajectory frame and end hitboxes with a low-opacity branch color.");

        bool useHitboxColor = AkronModule.Settings.ShowTrajectoryUseHitboxColor;
        if (ImGui.Checkbox("Use hitbox color##" + popupId, ref useHitboxColor)) {
            AkronModule.Settings.ShowTrajectoryUseHitboxColor = useHitboxColor;
        }
        DrawPopupTooltip("Use Show Hitboxes' player color for trajectory end hitboxes, matching Akron's source behavior.");

        DrawHitboxColorRow("Jump held", () => AkronModule.Settings.ShowTrajectoryPressColor, value => AkronModule.Settings.ShowTrajectoryPressColor = value, popupId, "Path color for jump-pressed/held prediction.");
        DrawHitboxColorRow("Jump released", () => AkronModule.Settings.ShowTrajectoryReleaseColor, value => AkronModule.Settings.ShowTrajectoryReleaseColor = value, popupId, "Path color for jump-released prediction.");
        DrawHitboxColorRow("End hitbox", () => AkronModule.Settings.ShowTrajectoryEndMarkerColor, value => {
            AkronModule.Settings.ShowTrajectoryEndMarkerColor = value;
            AkronModule.Settings.ShowTrajectoryUseHitboxColor = false;
        }, popupId, "Custom end-hitbox color when not inheriting Show Hitboxes' player color.");
    }
}
