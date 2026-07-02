using Celeste;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

public sealed class AkronToast : Entity {
    private const float HudWidth = 1920f;
    private const float HudHeight = 1080f;
    private const float HudPadding = 48f;
    private const float BaseScale = 0.42f;
    private const float StackGap = 6f;
    private static int nextSequence;
    private readonly string message;
    private readonly bool forceVisible;
    private readonly int sequence;
    private float timer;

    public AkronToast(string message, bool forceVisible = false, float durationSeconds = 2.8f) {
        this.message = message;
        this.forceVisible = forceVisible;
        timer = Math.Max(0.1f, durationSeconds);
        sequence = ++nextSequence;
        Tag = Tags.HUD | Tags.Global | Tags.PauseUpdate;
    }

    public override void Update() {
        base.Update();
        timer -= Engine.DeltaTime;
        if (timer <= 0f) {
            RemoveSelf();
        }
    }

    public override void Render() {
        if (AkronCapture.IsCapturingGameFrame ||
            AkronModule.ShouldHideAkronRenderSurfaces() ||
            (!forceVisible && (!AkronModule.Settings.LabelSystemVisible || !AkronModule.Settings.ToastLabels))) {
            return;
        }

        AkronModuleSettings settings = AkronModule.Settings;
        float alpha = Calc.Clamp(timer, 0f, 1f);
        AkronHudLabelStyleSettings style = AkronModuleSettings.CloneLabelStyle(settings.ToastLabelStyle);
        float scale = BaseScale * (style.Scale / 100f);
        float opacity = AkronModuleSettings.ClampOpacity(style.Opacity) / 100f;
        Vector2 size = ActiveFont.Measure(message) * scale;
        Vector2 position = AnchorPosition(settings.ToastLabelAnchor, size) + new Vector2(style.OffsetX, style.OffsetY);
        position += StackOffset(settings.ToastLabelAnchor, scale);
        if (Engine.Scene is Level level) {
            Player player = level.Tracker.GetEntity<Player>();
            AkronHudRenderer.TryApplyHudElementPlayerOverlap(
                settings,
                AkronHudRenderer.ResolvePlayerHudRectForLabels(level, player),
                anyHudLabelObstructed: false,
                size: size,
                position: ref position,
                opacity: ref opacity);
        }

        Color textColor = ColorFromRgb(settings.ToastLabelColor) * opacity * alpha;

        if (style.Shadow) {
            Color shadow = ColorFromRgb(style.ShadowColor) * (AkronModuleSettings.ClampOpacity(style.ShadowOpacity) / 100f * opacity * alpha);
            ActiveFont.Draw(message, position + new Vector2(style.ShadowOffsetX, style.ShadowOffsetY), Vector2.Zero, Vector2.One * scale, shadow);
            ActiveFont.DrawOutline(message, position, Vector2.Zero, Vector2.One * scale, textColor, 2f, shadow);
            return;
        }

        ActiveFont.Draw(message, position, Vector2.Zero, Vector2.One * scale, textColor);
    }

    private Vector2 StackOffset(AkronHudAnchor anchor, float scale) {
        if (Engine.Scene == null) {
            return Vector2.Zero;
        }

        List<AkronToast> toasts = Engine.Scene.Entities.FindAll<AkronToast>()
            .Where(toast => toast != null)
            .OrderByDescending(toast => toast.sequence)
            .ToList();
        List<float> newerHeights = new List<float>();
        foreach (AkronToast toast in toasts) {
            if (toast == this) {
                break;
            }

            newerHeights.Add(ActiveFont.Measure(toast.message).Y * scale);
        }

        float offset = CalculateStackOffset(newerHeights);

        if (offset <= 0f) {
            return Vector2.Zero;
        }

        return new Vector2(0f, StackDirection(anchor) * offset);
    }

    internal static float CalculateStackOffset(IReadOnlyList<float> newerToastHeights) {
        if (newerToastHeights == null || newerToastHeights.Count == 0) {
            return 0f;
        }

        float offset = 0f;
        foreach (float height in newerToastHeights) {
            offset += Math.Max(0f, height) + StackGap;
        }

        return offset;
    }

    private static float StackDirection(AkronHudAnchor anchor) {
        return anchor switch {
            AkronHudAnchor.TopLeft or AkronHudAnchor.TopCenter or AkronHudAnchor.TopRight => 1f,
            _ => -1f
        };
    }

    private static Vector2 AnchorPosition(AkronHudAnchor anchor, Vector2 size) {
        return anchor switch {
            AkronHudAnchor.TopLeft => new Vector2(HudPadding, HudPadding),
            AkronHudAnchor.TopCenter => new Vector2(HudWidth / 2f - size.X / 2f, HudPadding),
            AkronHudAnchor.TopRight => new Vector2(HudWidth - HudPadding - size.X, HudPadding),
            AkronHudAnchor.MiddleLeft => new Vector2(HudPadding, HudHeight / 2f - size.Y / 2f),
            AkronHudAnchor.Center => new Vector2(HudWidth / 2f - size.X / 2f, HudHeight / 2f - size.Y / 2f),
            AkronHudAnchor.MiddleRight => new Vector2(HudWidth - HudPadding - size.X, HudHeight / 2f - size.Y / 2f),
            AkronHudAnchor.BottomCenter => new Vector2(HudWidth / 2f - size.X / 2f, HudHeight - HudPadding - size.Y),
            AkronHudAnchor.BottomRight => new Vector2(HudWidth - HudPadding - size.X, HudHeight - HudPadding - size.Y),
            _ => new Vector2(HudPadding, HudHeight - HudPadding - size.Y)
        };
    }

    private static Color ColorFromRgb(int rgb) {
        return new Color((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
    }
}
