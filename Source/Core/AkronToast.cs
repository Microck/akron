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
    // How many recent messages are kept for read-back. A refusal is judged against the
    // handful of messages around it, and the count of messages raised says whether
    // anything was missed, so keeping more would only be a bigger buffer to read. A power
    // of two, so the index below is a mask rather than a remainder.
    private const int RecordedMessageCount = 32;
    // Newest at (nextSequence - 1) & (RecordedMessageCount - 1). Indexed by sequence
    // rather than shifted so recording a message is one array store.
    private static readonly string[] RecordedMessages = new string[RecordedMessageCount];
    // Counts every message raised, and is 64-bit for that reason. One of the callers below
    // can raise a message per frame, and a 32-bit counter overflowing turns the count and
    // the read-back below into nonsense rather than into a wrong number; at 64 bits no run
    // can reach it.
    private static long nextSequence;
    private readonly string message;
    private readonly bool forceVisible;
    private readonly long sequence;
    private float timer;

    public AkronToast(string message, bool forceVisible = false, float durationSeconds = 2.8f) {
        this.message = message;
        this.forceVisible = forceVisible;
        timer = Math.Max(0.1f, durationSeconds);
        sequence = RecordRaisedMessage(message);
        Tag = Tags.HUD | Tags.Global | Tags.PauseUpdate;
        // Toasts report Akron actions; they are not room gameplay state. A Set
        // notification can still be queued when the room clone is captured.
        // Excluding it avoids replaying stale UI after every StartPos load.
        AkronSaveLoadService.IgnoreSaveState(this);
    }

    // Assign the sequence and buffer entry together so the sequence controls both
    // read-back and stacking order. Keep this path to an array store and increment:
    // policy checks and HUD rendering can raise messages per frame, so logging here
    // would add filesystem work to the render path. Record messages when raised rather
    // than displayed because some scenes cannot take a toast entity.
    internal static long RecordRaisedMessage(string message) {
        long raised = ++nextSequence;
        RecordedMessages[(int) ((raised - 1) & (RecordedMessageCount - 1))] = message ?? string.Empty;
        return raised;
    }

    // How many messages Akron has raised this run, counting every caller. A reader
    // compares it against the previous answer to see how many messages were raised between
    // the two reads, without matching text.
    internal static long RaisedMessageCount => nextSequence;

    // The most recent messages, oldest first, at most count and at most what the buffer
    // holds. Paired with RaisedMessageCount, this is what lets an automation query assert
    // the sentence a refusal produced.
    internal static IReadOnlyList<string> GetRecentMessages(int count) {
        int available = (int) Math.Min(Math.Min(count, RecordedMessageCount), nextSequence);
        List<string> messages = new List<string>(Math.Max(0, available));
        for (int offset = available; offset > 0; offset--) {
            messages.Add(RecordedMessages[(int) ((nextSequence - offset) & (RecordedMessageCount - 1))] ?? string.Empty);
        }
        return messages;
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
