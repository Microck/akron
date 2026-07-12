using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Celeste.Mod.Akron;

public static partial class AkronHudRenderer {
    private static void RenderInputHistory(ref float leftColumnY) {
        IReadOnlyList<AkronInputHistoryEntry> entries = AkronInputHistory.Current;
        if (entries.Count == 0) {
            return;
        }

        AkronModuleSettings settings = AkronModule.Settings;
        AkronHudLabelStyleSettings style = AkronModuleSettings.CloneLabelStyle(settings.InputHistoryLabelStyle);
        float styleScale = style.Scale / 100f;
        float scale = (settings.InputHistoryCompact ? 0.30f : 0.36f) * styleScale;
        float rowHeight = (settings.InputHistoryCompact ? 23f : 29f) * styleScale * (AkronModuleSettings.ClampCustomLabelLineSpacing(style.LineSpacing) / 100f);
        float width = (settings.InputHistoryCompact ? 126f : 156f) * styleScale;
        float screenWidth = ResolveHudViewportSize().X;
        float x = (settings.InputHistoryPlacement == AkronHudPlacement.Right ? screenWidth - width - HudEdgePadding : HudEdgePadding + 8f) + style.OffsetX;
        float y = (settings.InputHistoryPlacement == AkronHudPlacement.Right ? 72f : leftColumnY) + style.OffsetY;
        float layoutY = y;
        float opacity = Calc.Clamp(settings.InputHistoryOpacity, 30, 100) / 100f * (AkronModuleSettings.ClampOpacity(style.Opacity) / 100f);
        Vector2 boxPosition = new Vector2(x - 8f, y - 5f);
        Vector2 boxSize = new Vector2(width, entries.Count * rowHeight + 10f);
        if (TryApplyHudElementPlayerOverlap(boxSize, ref boxPosition, ref opacity)) {
            x = boxPosition.X + 8f;
            y = boxPosition.Y + 5f;
        }

        Color textColor = ColorFromRgb(settings.InputHistoryTextColor);
        Color eventColor = ColorFromRgb(settings.InputHistoryEventColor);
        Draw.Rect(x - 8f, y - 5f, width, entries.Count * rowHeight + 10f, Color.Black * (0.58f * opacity));
        Draw.HollowRect(x - 8f, y - 5f, width, entries.Count * rowHeight + 10f, textColor * (0.55f * opacity));
        foreach (AkronInputHistoryEntry entry in entries) {
            bool eventRow = entry.Kind == AkronInputHistoryEntryKind.Event;
            string text = eventRow ? "> " + entry.Chord : entry.Chord + "  " + entry.Frames + "f";
            Vector2 position = new Vector2(x, y);
            Color color = (eventRow ? eventColor : textColor) * opacity;
            if (style.Shadow) {
                Color shadow = ColorFromRgb(style.ShadowColor) * (AkronModuleSettings.ClampOpacity(style.ShadowOpacity) / 100f * opacity);
                ActiveFont.Draw(text, position + new Vector2(style.ShadowOffsetX, style.ShadowOffsetY), Vector2.Zero, Vector2.One * scale, shadow);
                ActiveFont.DrawOutline(text, position, Vector2.Zero, Vector2.One * scale, color, 2f, shadow);
            } else {
                ActiveFont.Draw(text, position, Vector2.Zero, Vector2.One * scale, color);
            }
            y += rowHeight;
        }

        if (settings.InputHistoryPlacement == AkronHudPlacement.Left) {
            leftColumnY = layoutY + entries.Count * rowHeight + 5f;
        }
    }

    private static void RenderTapDisplay(ref float leftColumnY) {
        RenderInputBoard(ref leftColumnY);
    }

    private static void RenderInputBoard(ref float leftColumnY) {
        AkronModuleSettings settings = AkronModule.Settings;
        List<AkronInputBoardElement> elements = settings.InputBoardElements;
        List<AkronInputBoardElement> visible = elements.Where(element => element.Visible).ToList();
        if (visible.Count == 0) {
            return;
        }

        float scale = AkronModuleSettings.ClampPercent(settings.TapDisplayScale, 50, 250) / 100f;
        float opacity = AkronModuleSettings.ClampOpacity(settings.TapDisplayOpacity) / 100f;
        int left = visible.Min(element => element.X);
        int top = visible.Min(element => element.Y);
        int right = visible.Max(element => element.X + element.Width);
        int bottom = visible.Max(element => element.Y + element.Height);
        float width = Math.Max(1f, right - left) * scale;
        float height = Math.Max(1f, bottom - top) * scale;
        Vector2 origin = GetCornerOrigin(width, height, settings.TapDisplayCorner);

        foreach (AkronInputBoardElement element in visible) {
            float x = origin.X + (element.X - left) * scale;
            float y = origin.Y + (element.Y - top) * scale;
            float elementWidth = element.Width * scale;
            float elementHeight = element.Height * scale;
            DrawInputBoardElement(element, x, y, elementWidth, elementHeight, opacity);
        }

        if (settings.TapDisplayCorner == IndicatorCorner.TopLeft) {
            leftColumnY = Math.Max(leftColumnY, origin.Y + height + 8f);
        }
    }

    private static void DrawInputBoardElement(AkronInputBoardElement element, float x, float y, float width, float height, float opacity) {
        bool pressed = AkronInputBoard.IsPressed(element, AkronModule.Settings.InputBoardSource);
        Color fill = ColorFromRgb(pressed ? element.PressedFillColor : element.FillColor) * (pressed ? 0.86f * opacity : 0.62f * opacity);
        Color stroke = ColorFromRgb(element.StrokeColor) * (pressed ? opacity : 0.58f * opacity);
        Color textColor = ColorFromRgb(element.TextColor) * opacity;

        Draw.Rect(x, y, width, height, fill);
        for (int outline = 0; outline < element.OutlineWidth; outline++) {
            Draw.HollowRect(x + outline, y + outline, Math.Max(1f, width - outline * 2f), Math.Max(1f, height - outline * 2f), stroke);
        }

        string label = string.IsNullOrWhiteSpace(element.Label) ? "Key" : element.Label;
        float labelScale = Calc.Clamp(element.TextScale, AkronInputBoard.MinimumTextScale, AkronInputBoard.MaximumTextScale) / 100f;
        float textScale = Math.Max(0.18f, Math.Min(width, height) / 82f) * labelScale;
        Vector2 textSize = ActiveFont.Measure(label) * textScale;
        float maxTextWidth = Math.Max(1f, width - 6f);
        float maxTextHeight = Math.Max(1f, height - 4f);
        if ((textSize.X > maxTextWidth || textSize.Y > maxTextHeight) && textSize.X > 0f && textSize.Y > 0f) {
            textScale *= Math.Max(0.2f, Math.Min(maxTextWidth / textSize.X, maxTextHeight / textSize.Y));
            textSize = ActiveFont.Measure(label) * textScale;
        }

        ActiveFont.Draw(label, new Vector2(x + (width - textSize.X) * 0.5f, y + (height - textSize.Y) * 0.5f - 1f), Vector2.Zero, Vector2.One * textScale, textColor);
    }

    private static Vector2 GetCornerOrigin(float width, float height, IndicatorCorner corner) {
        Vector2 hudSize = ResolveHudViewportSize();
        float screenWidth = hudSize.X;
        float screenHeight = hudSize.Y;
        const float margin = 48f;
        return corner switch {
            IndicatorCorner.TopLeft => new Vector2(margin, margin),
            IndicatorCorner.BottomLeft => new Vector2(margin, screenHeight - height - margin),
            IndicatorCorner.BottomRight => new Vector2(screenWidth - width - margin, screenHeight - height - margin),
            _ => new Vector2(screenWidth - width - margin, margin)
        };
    }

    private static Vector2 ResolveHudViewportSize() {
        Viewport viewport = Engine.Viewport;
        float width = viewport.Width;
        float height = viewport.Height;

        if (width <= 0f || height <= 0f) {
            width = Engine.Instance?.GraphicsDevice?.PresentationParameters.BackBufferWidth ?? 1280f;
            height = Engine.Instance?.GraphicsDevice?.PresentationParameters.BackBufferHeight ?? 720f;
        }

        float scale = MathHelper.Min(width / 320f, height / 180f);
        return scale <= 0f ? new Vector2(1280f, 720f) : new Vector2(320f * scale, 180f * scale);
    }

    private static void RenderInputsPerSecondCounter(ref float leftColumnY) {
        AkronModuleSettings settings = AkronModule.Settings;
        AkronInputsPerSecondSnapshot snapshot = AkronInputHistory.GetInputsPerSecondSnapshot();
        AkronHudLabelStyleSettings style = AkronModuleSettings.CloneLabelStyle(settings.InputsPerSecondLabelStyle);
        float scale = AkronModuleSettings.ClampPercent(settings.InputsPerSecondScale, 50, 250) / 100f;
        float opacity = AkronModuleSettings.ClampOpacity(settings.InputsPerSecondOpacity) / 100f;
        float textScale = 0.42f * scale;
        string text = FormatInputsPerSecondHudText(snapshot, settings);
        Vector2 textSize = ActiveFont.Measure(text) * textScale;

        float screenWidth = ResolveHudViewportSize().X;
        float x = (settings.InputsPerSecondPlacement == AkronHudPlacement.Right ? screenWidth - HudEdgePadding - textSize.X : HudEdgePadding) + style.OffsetX;
        float y = (settings.InputsPerSecondPlacement == AkronHudPlacement.Right ? 72f : leftColumnY) + style.OffsetY;
        float layoutY = y;
        opacity *= AkronModuleSettings.ClampOpacity(style.Opacity) / 100f;
        Vector2 position = new Vector2(x, y);
        TryApplyHudElementPlayerOverlap(textSize, ref position, ref opacity);
        x = position.X;
        y = position.Y;
        Color textColor = ColorFromRgb(settings.InputsPerSecondTextColor) * opacity;

        if (style.Shadow) {
            Color shadow = ColorFromRgb(style.ShadowColor) * (AkronModuleSettings.ClampOpacity(style.ShadowOpacity) / 100f * opacity);
            ActiveFont.Draw(text, new Vector2(x + style.ShadowOffsetX, y + style.ShadowOffsetY), Vector2.Zero, Vector2.One * textScale, shadow);
            ActiveFont.DrawOutline(text, new Vector2(x, y), Vector2.Zero, Vector2.One * textScale, textColor, 2f, shadow);
        } else {
            ActiveFont.Draw(text, new Vector2(x, y), Vector2.Zero, Vector2.One * textScale, textColor);
        }

        if (settings.InputsPerSecondPlacement == AkronHudPlacement.Left) {
            leftColumnY = layoutY + 34f * scale * (AkronModuleSettings.ClampCustomLabelLineSpacing(style.LineSpacing) / 100f);
        }
    }

    private static string FormatInputsPerSecondHudText(AkronInputsPerSecondSnapshot snapshot, AkronModuleSettings settings) {
        List<string> values = new List<string> {
            snapshot.Current.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        if (settings.InputsPerSecondShowTotal) {
            values.Add(snapshot.Total.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (settings.InputsPerSecondShowMax) {
            values.Add(snapshot.Max.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return string.Join("/", values) + " IPS";
    }
}
