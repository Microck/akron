using System;
using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private bool UpdateAutoKillAreaSelection() {
        return UpdatePracticeAreaSelection(isAutoDeafen: false);
    }

    private bool UpdateAutoDeafenAreaSelection() {
        return UpdatePracticeAreaSelection(isAutoDeafen: true);
    }

    private bool UpdateStartPosPlacement() {
        if (!startPosPlacementActive) {
            startPosPlacementLastLeftDown = Mouse.GetState().LeftButton == ButtonState.Pressed;
            return false;
        }

        if (!CanUseStartPosPlacementEditor()) {
            EndStartPosPlacement(true);
            Engine.Scene?.Add(new AkronToast("StartPos placement cancelled outside gameplay."));
            return true;
        }

        SearchInputConsumedThisFrame = true;
        SearchOwnsGameplayInputThisFrame = true;
        AkronModule.ShowManagedCursorForTransientUi();

        MouseState mouse = Mouse.GetState();
        if (MInput.Keyboard.Pressed(Keys.Escape) || MInput.Keyboard.Pressed(Keys.Back) || mouse.RightButton == ButtonState.Pressed) {
            EndStartPosPlacement(true);
            Engine.Scene?.Add(new AkronToast("StartPos placement closed."));
            return true;
        }

        bool leftDown = mouse.LeftButton == ButtonState.Pressed;
        bool leftPressed = leftDown && !startPosPlacementLastLeftDown;
        startPosPlacementLastLeftDown = leftDown;
        if (IsMouseInsideStartPosPlacementPanel(mouse)) {
            return true;
        }

        if (!leftPressed || Engine.Scene is not Level level || !AkronPolicy.CanUse(AkronFeatureKind.StartPosTools).Allowed) {
            return true;
        }

        Vector2 world = AkronScreenProjection.MouseScreenToWorld(level, new Vector2(mouse.X, mouse.Y));
        AkronActions.SetStartPosAtMouse(level, world);
        return true;
    }

    private bool UpdatePracticeAreaSelection(bool isAutoDeafen) {
        bool selectionActive = isAutoDeafen ? autoDeafenAreaSelectionActive : autoKillAreaSelectionActive;
        if (!selectionActive) {
            if (isAutoDeafen) {
                autoDeafenAreaLastLeftDown = Mouse.GetState().LeftButton == ButtonState.Pressed;
                return false;
            }

            autoKillAreaLastLeftDown = Mouse.GetState().LeftButton == ButtonState.Pressed;
            return false;
        }

        SearchInputConsumedThisFrame = true;
        SearchOwnsGameplayInputThisFrame = true;
        AkronModule.ShowManagedCursorForTransientUi();
        if (MInput.Keyboard.Pressed(Keys.Escape) || MInput.Keyboard.Pressed(Keys.Back) || Mouse.GetState().RightButton == ButtonState.Pressed) {
            if (isAutoDeafen) {
                EndAutoDeafenAreaSelection(true);
                Engine.Scene?.Add(new AkronToast("Auto Deafen area selection cancelled."));
            } else {
                EndAutoKillAreaSelection(true);
                Engine.Scene?.Add(new AkronToast("Auto Kill area selection cancelled."));
            }
            return true;
        }

        MouseState mouse = Mouse.GetState();
        bool leftDown = mouse.LeftButton == ButtonState.Pressed;
        bool lastLeftDown = isAutoDeafen ? autoDeafenAreaLastLeftDown : autoKillAreaLastLeftDown;
        bool leftPressed = leftDown && !lastLeftDown;
        if (isAutoDeafen) {
            autoDeafenAreaLastLeftDown = leftDown;
        } else {
            autoKillAreaLastLeftDown = leftDown;
        }
        if (!leftPressed || Engine.Scene is not Level level) {
            return true;
        }

        Vector2 world = AkronScreenProjection.MouseScreenToWorld(level, new Vector2(mouse.X, mouse.Y));
        bool hasFirstCorner = isAutoDeafen ? autoDeafenAreaHasFirstCorner : autoKillAreaHasFirstCorner;
        if (!hasFirstCorner) {
            if (isAutoDeafen) {
                autoDeafenAreaFirstCorner = world;
                autoDeafenAreaHasFirstCorner = true;
                Engine.Scene?.Add(new AkronToast("Auto Deafen: click opposite corner."));
            } else {
                autoKillAreaFirstCorner = world;
                autoKillAreaHasFirstCorner = true;
                Engine.Scene?.Add(new AkronToast("Auto Kill: click opposite corner."));
            }
            return true;
        }

        Rectangle area = RectFromWorldCorners(isAutoDeafen ? autoDeafenAreaFirstCorner : autoKillAreaFirstCorner, world);
        if (isAutoDeafen) {
            AkronModule.AddAutoDeafenArea(area);
            AkronModule.Settings.AutoDeafen = true;
            autoDeafenAreaHasFirstCorner = false;
            Engine.Scene?.Add(new AkronToast("Auto Deafen area added: " + area.Width + "x" + area.Height + ". Esc/right-click to finish."));
        } else {
            AkronModule.AddAutoKillArea(area);
            AkronModule.Settings.AutoKill = true;
            autoKillAreaHasFirstCorner = false;
            Engine.Scene?.Add(new AkronToast("Auto Kill area added: " + area.Width + "x" + area.Height + ". Esc/right-click to finish."));
        }
        return true;
    }

    private void BeginAutoKillAreaSelection() {
        if (autoDeafenAreaSelectionActive) {
            EndAutoDeafenAreaSelection(false);
        }

        autoKillSelectionPreviousFreeze = AkronModule.Session?.FreezeGameplay ?? false;
        if (AkronModule.Session != null) {
            AkronModule.Session.FreezeGameplay = true;
        }

        autoKillAreaSelectionActive = true;
        autoKillAreaHasFirstCorner = false;
        autoKillAreaLastLeftDown = Mouse.GetState().LeftButton == ButtonState.Pressed;
        Visible = false;
        Active = false;
        AkronModule.ShowManagedCursorForTransientUi();
        Engine.Scene?.Add(new AkronToast("Auto Kill: frozen. Click two corners per area; Esc/right-click ends."));
    }

    internal void BeginAutoKillAreaSelectionFromAutomation() {
        BeginAutoKillAreaSelection();
    }

    private void BeginStartPosPlacement() {
        if (!CanUseStartPosPlacementEditor()) {
            AkronModule.Settings.StartPosMousePlacement = false;
            Engine.Scene?.Add(new AkronToast("StartPos placement needs an active level."));
            return;
        }

        if (autoKillAreaSelectionActive) {
            EndAutoKillAreaSelection(false);
        }
        if (autoDeafenAreaSelectionActive) {
            EndAutoDeafenAreaSelection(false);
        }

        startPosPlacementPreviousFreeze = AkronModule.Session?.FreezeGameplay ?? false;
        startPosPlacementPreviousFreeCamera = AkronModule.Settings.FreeCamera;
        startPosPlacementPreviousFreeCameraFreeze = AkronModule.Settings.FreeCameraFreezeGameplay;

        if (AkronModule.Session != null) {
            AkronModule.Session.FreezeGameplay = true;
        }

        AkronModule.Settings.FreeCamera = true;
        AkronModule.Settings.FreeCameraFreezeGameplay = true;
        AkronModule.Settings.StartPosMousePlacement = true;
        startPosPlacementActive = true;
        startPosPlacementLastLeftDown = Mouse.GetState().LeftButton == ButtonState.Pressed;
        Visible = false;
        Active = false;
        AkronModule.ShowManagedCursorForTransientUi();
        Engine.Scene?.Add(new AkronToast("StartPos placement: frozen free camera. Click to place; Esc/right-click ends."));
    }

    private static bool CanUseStartPosPlacementEditor() {
        return Engine.Scene is Level level &&
               !level.Transitioning &&
               !level.InCutscene &&
               !level.SkippingCutscene &&
               level.Tracker.GetEntity<Player>() is { Dead: false } &&
               AkronPolicy.CanUse(AkronFeatureKind.StartPosTools).Allowed;
    }

    private bool IsMouseInsideStartPosPlacementPanel(MouseState mouse) {
        return mouse.X >= startPosPlacementPanelMin.X &&
               mouse.X <= startPosPlacementPanelMax.X &&
               mouse.Y >= startPosPlacementPanelMin.Y &&
               mouse.Y <= startPosPlacementPanelMax.Y;
    }

    private void EndStartPosPlacement(bool restoreOverlay) {
        startPosPlacementActive = false;
        AkronModule.Settings.StartPosMousePlacement = false;
        AkronModule.Settings.FreeCamera = startPosPlacementPreviousFreeCamera;
        AkronModule.Settings.FreeCameraFreezeGameplay = startPosPlacementPreviousFreeCameraFreeze;
        if (AkronModule.Session != null) {
            AkronModule.Session.FreezeGameplay = startPosPlacementPreviousFreeze;
        }

        if (restoreOverlay) {
            Visible = true;
            Active = AkronModule.Settings.ConsumeGameplayInputInMenu;
        }
    }

    private void EndAutoKillAreaSelection(bool restoreOverlay) {
        autoKillAreaSelectionActive = false;
        autoKillAreaHasFirstCorner = false;
        if (AkronModule.Session != null) {
            AkronModule.Session.FreezeGameplay = autoKillSelectionPreviousFreeze;
        }

        if (restoreOverlay) {
            Visible = true;
            Active = AkronModule.Settings.ConsumeGameplayInputInMenu;
        }
    }

    private void BeginAutoDeafenAreaSelection() {
        if (autoKillAreaSelectionActive) {
            EndAutoKillAreaSelection(false);
        }

        autoDeafenSelectionPreviousFreeze = AkronModule.Session?.FreezeGameplay ?? false;
        if (AkronModule.Session != null) {
            AkronModule.Session.FreezeGameplay = true;
        }

        autoDeafenAreaSelectionActive = true;
        autoDeafenAreaHasFirstCorner = false;
        autoDeafenAreaLastLeftDown = Mouse.GetState().LeftButton == ButtonState.Pressed;
        Visible = false;
        Active = false;
        AkronModule.ShowManagedCursorForTransientUi();
        Engine.Scene?.Add(new AkronToast("Auto Deafen: frozen. Click two corners per area; Esc/right-click ends."));
    }

    internal void BeginAutoDeafenAreaSelectionFromAutomation() {
        BeginAutoDeafenAreaSelection();
    }

    private void EndAutoDeafenAreaSelection(bool restoreOverlay) {
        autoDeafenAreaSelectionActive = false;
        autoDeafenAreaHasFirstCorner = false;
        if (AkronModule.Session != null) {
            AkronModule.Session.FreezeGameplay = autoDeafenSelectionPreviousFreeze;
        }

        if (restoreOverlay) {
            Visible = true;
            Active = AkronModule.Settings.ConsumeGameplayInputInMenu;
        }
    }

    internal bool TryGetPracticeAreaSelectionPreview(Level level, bool isAutoDeafen, out Rectangle area, out bool hasAnchor) {
        area = new Rectangle();
        hasAnchor = false;
        if (level == null || !(isAutoDeafen ? autoDeafenAreaSelectionActive : autoKillAreaSelectionActive)) {
            return false;
        }

        Vector2 mouseWorld = AkronScreenProjection.MouseScreenToWorld(level, new Vector2(Mouse.GetState().X, Mouse.GetState().Y));
        hasAnchor = isAutoDeafen ? autoDeafenAreaHasFirstCorner : autoKillAreaHasFirstCorner;
        PracticeAreaSelectionPreviewBounds bounds = PracticeAreaSelectionPreviewBoundsFor(
            mouseWorld,
            hasAnchor,
            isAutoDeafen ? autoDeafenAreaFirstCorner : autoKillAreaFirstCorner);
        area = new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        return true;
    }

    internal static PracticeAreaSelectionPreviewBounds PracticeAreaSelectionPreviewBoundsFor(Vector2 mouseWorld, bool hasAnchor, Vector2 firstCorner) {
        return PracticeAreaSelectionPreviewBoundsFor(mouseWorld.X, mouseWorld.Y, hasAnchor, firstCorner.X, firstCorner.Y);
    }

    internal static PracticeAreaSelectionPreviewBounds PracticeAreaSelectionPreviewBoundsFor(float mouseWorldX, float mouseWorldY, bool hasAnchor, float firstCornerX, float firstCornerY) {
        if (hasAnchor) {
            int left = (int) Math.Floor(Math.Min(firstCornerX, mouseWorldX));
            int top = (int) Math.Floor(Math.Min(firstCornerY, mouseWorldY));
            int right = (int) Math.Ceiling(Math.Max(firstCornerX, mouseWorldX));
            int bottom = (int) Math.Ceiling(Math.Max(firstCornerY, mouseWorldY));
            return new PracticeAreaSelectionPreviewBounds(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
        }

        int x = (int) Math.Floor(mouseWorldX) - 4;
        int y = (int) Math.Floor(mouseWorldY) - 4;
        return new PracticeAreaSelectionPreviewBounds(x, y, 8, 8);
    }

    internal readonly struct PracticeAreaSelectionPreviewBounds {
        public PracticeAreaSelectionPreviewBounds(int x, int y, int width, int height) {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }
    }

    private static Rectangle RectFromWorldCorners(Vector2 first, Vector2 second) {
        int left = (int) Math.Floor(Math.Min(first.X, second.X));
        int top = (int) Math.Floor(Math.Min(first.Y, second.Y));
        int right = (int) Math.Ceiling(Math.Max(first.X, second.X));
        int bottom = (int) Math.Ceiling(Math.Max(first.Y, second.Y));
        return new Rectangle(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

}
