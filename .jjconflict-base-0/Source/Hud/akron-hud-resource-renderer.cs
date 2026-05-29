using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using System;

namespace Celeste.Mod.Akron;

public static partial class AkronHudRenderer {
    private const float MaxStamina = 110f;
    private const float DangerStamina = 20f;
    private const float HudResourceWidth = 300f;
    private const float HudResourceHeight = 35f;
    private const float PlayerResourceWidth = 96f;
    private const float PlayerResourceHeight = 12f;
    private static float displayedStamina = MaxStamina;
    private static float staminaDisplayTimer;
    private static ulong staminaStateFrame;
    private static float frameCurrentStamina = MaxStamina;

    private static void RenderStaminaBars(Level level, Player player, float x, ref float y) {
        if (!AkronModule.TryUse(AkronFeatureKind.ResourceBars)) {
            return;
        }

        AkronModuleSettings settings = AkronModule.Settings;
        float currentStamina = UpdateStaminaDisplay(level, player);
        displayedStamina = settings.StaminaShowChangePulse ? displayedStamina : currentStamina;
        if ((settings.StaminaAlwaysVisible || displayedStamina != MaxStamina) && !player.Dead) {
            staminaDisplayTimer = 0.75f;
        } else if (staminaDisplayTimer > 0f) {
            staminaDisplayTimer -= Engine.DeltaTime;
        }

        if ((!settings.StaminaAlwaysVisible && staminaDisplayTimer <= 0f) || level.Paused && settings.StaminaHideWhilePaused) {
            return;
        }

        Color color = ColorFromRgb(currentStamina < settings.LowStaminaThreshold ? settings.StaminaLowColor : settings.StaminaNormalColor);
        Color fillColor = ColorFromRgb(settings.StaminaFillColor);
        Color lineColor = ColorFromRgb(settings.StaminaLineColor);
        Color overflowColor = ColorFromRgb(settings.StaminaOverflowColor);

        if (settings.StaminaBarPlayer) {
            RenderPlayerStaminaBar(level, player, currentStamina, color, fillColor, lineColor, overflowColor);
        }

        if (settings.StaminaBarHud) {
            RenderHudStaminaBar(level, currentStamina, color, fillColor, lineColor, overflowColor);
        } else {
            RenderInlineStaminaBar(x, ref y, currentStamina, color, fillColor, lineColor, overflowColor);
        }
    }

    private static void RenderInlineStaminaBar(float x, ref float y, float currentStamina, Color color, Color fillColor, Color lineColor, Color overflowColor) {
        float width = 170f;
        float height = 14f;
        DrawStaminaMeter(x, y + 5f, width, height, currentStamina, color, fillColor, lineColor, overflowColor, 1f);
        ActiveFont.Draw("STA", new Vector2(x + width + 10f, y - 1f), Vector2.Zero, Vector2.One * 0.30f, color);
        y += 26f;
    }

    private static void RenderPlayerStaminaBar(Level level, Player player, float currentStamina, Color color, Color fillColor, Color lineColor, Color overflowColor) {
        Vector2 playerPosition = AkronScreenProjection.WorldToHud(level, player.Position);
        if (SaveData.Instance != null && SaveData.Instance.Assists.MirrorMode) {
            playerPosition.X = 1920f - playerPosition.X;
        }
        if (Input.MoveY.Inverted) {
            playerPosition.Y = 1080f - playerPosition.Y + 96f;
        }

        float scale = AkronModuleSettings.ClampResourcePlayerScale(AkronModule.Settings.StaminaPlayerScale) / 100f;
        Vector2 size = new Vector2(PlayerResourceWidth * scale, PlayerResourceHeight * scale);
        Vector2 position = AkronModule.Settings.StaminaBarPlayerPosition == AkronStaminaPlayerBarPosition.Below
            ? new Vector2(playerPosition.X - size.X / 2f, playerPosition.Y + 6f * scale)
            : new Vector2(playerPosition.X - size.X / 2f, playerPosition.Y - 114f * scale);
        position += new Vector2(
            AkronModuleSettings.ClampResourcePlayerOffset(AkronModule.Settings.StaminaPlayerOffsetX),
            AkronModuleSettings.ClampResourcePlayerOffset(AkronModule.Settings.StaminaPlayerOffsetY));
        if (AkronModule.Settings.StaminaBarStyle == AkronStaminaBarStyle.Ring) {
            DrawStaminaRing(position + size / 2f, 14f * scale, 5f * scale, currentStamina, color, fillColor, lineColor, overflowColor);
            return;
        }

        DrawStaminaMeter(position.X, position.Y, size.X, size.Y, currentStamina, color, fillColor, lineColor, overflowColor, Math.Max(1f, scale));
    }

    private static void RenderHudStaminaBar(Level level, float currentStamina, Color color, Color fillColor, Color lineColor, Color overflowColor) {
        Vector2 position = HudAnchorPosition(AkronModule.Settings.StaminaBarHudPosition);
        position += new Vector2(AkronModule.Settings.StaminaHudOffsetX, AkronModule.Settings.StaminaHudOffsetY);
        if (AkronModule.Settings.StaminaBarStyle == AkronStaminaBarStyle.Ring) {
            DrawStaminaRing(position + new Vector2(30f, 30f), 28f, 8f, currentStamina, color, fillColor, lineColor, overflowColor);
        } else {
            DrawStaminaMeter(position.X, position.Y, HudResourceWidth, HudResourceHeight, currentStamina, color, fillColor, lineColor, overflowColor, 2f);
        }
    }

    private static float UpdateStaminaDisplay(Level level, Player player) {
        if (staminaStateFrame == Engine.FrameCounter) {
            return frameCurrentStamina;
        }

        AkronModuleSettings settings = AkronModule.Settings;
        frameCurrentStamina = settings.StaminaShowOverflow ? player.Stamina : Calc.Min(player.Stamina, MaxStamina);
        displayedStamina = settings.StaminaShowChangePulse
            ? Calc.Approach(displayedStamina, frameCurrentStamina, 250f * Engine.DeltaTime)
            : frameCurrentStamina;
        staminaStateFrame = Engine.FrameCounter;
        return frameCurrentStamina;
    }

    private static void DrawStaminaMeter(float x, float y, float width, float height, float currentStamina, Color color, Color fillColor, Color lineColor, Color overflowColor, float border) {
        Color darkColor = Color.Lerp(color, fillColor, 0.5f);
        Draw.Rect(x - border, y - border, width + border * 2f, height + border * 2f, lineColor);
        Draw.Rect(x, y, width, height, fillColor);
        float shownCurrent = Calc.Clamp(currentStamina, 0f, MaxStamina);
        float shownDisplayed = Calc.Clamp(displayedStamina, 0f, MaxStamina);

        if (displayedStamina > 0f) {
            if (shownDisplayed > shownCurrent) {
                Draw.Rect(x, y, width * shownDisplayed / MaxStamina, height, darkColor);
                if (shownCurrent > 0f) {
                    Draw.Rect(x, y, width * shownCurrent / MaxStamina, height, color);
                }
            } else {
                if (shownCurrent > 0f) {
                    Draw.Rect(x, y, width * shownCurrent / MaxStamina, height, darkColor);
                }
                Draw.Rect(x, y, width * shownDisplayed / MaxStamina, height, color);
            }
        }

        if (currentStamina > MaxStamina && AkronModule.Settings.StaminaShowOverflow) {
            float overflowWidth = width * Calc.Clamp((currentStamina - MaxStamina) / MaxStamina, 0f, 1f);
            Draw.Rect(x + width - overflowWidth, y, overflowWidth, height, overflowColor);
        }

        if (AkronModule.Settings.StaminaShowDangerMarker) {
            Draw.Rect(x + width * DangerStamina / MaxStamina, y, border, height, lineColor);
        }
    }

    private static void DrawStaminaRing(Vector2 center, float radius, float thickness, float currentStamina, Color color, Color fillColor, Color lineColor, Color overflowColor) {
        const int resolution = 96;
        float outline = Math.Max(2f, thickness * 0.35f);
        DrawRingArcLines(center, radius, thickness + outline * 2f, 1f, lineColor, resolution);
        DrawRingArcLines(center, radius, thickness, 1f, fillColor, resolution);
        float fill = Calc.Clamp(currentStamina / MaxStamina, 0f, 1f);
        DrawRingArcLines(center, radius, thickness, fill, color, resolution);
        if (currentStamina > MaxStamina && AkronModule.Settings.StaminaShowOverflow) {
            float overflowRadius = radius + thickness + outline + 3f;
            float overflowFill = Calc.Clamp((currentStamina - MaxStamina) / MaxStamina, 0f, 1f);
            DrawRingArcLines(center, overflowRadius, 7f, overflowFill, lineColor, resolution);
            DrawRingArcLines(center, overflowRadius, 4f, overflowFill, overflowColor, resolution);
        }
        if (AkronModule.Settings.StaminaShowDangerMarker) {
            float dangerEnd = DangerStamina / MaxStamina;
            DrawRingArcLines(center, radius, thickness, dangerEnd, lineColor * 0.20f, 8);
        }
    }

    private static void DrawRingArcLines(Vector2 center, float radius, float thickness, float percent, Color color, int resolution) {
        percent = Calc.Clamp(percent, 0f, 1f);
        if (percent <= 0f || radius <= 0f || thickness <= 0f) {
            return;
        }

        int segments = Math.Max(1, (int) Math.Ceiling(resolution * percent));
        Vector2 previous = center + Calc.AngleToVector(-MathHelper.PiOver2, radius);
        for (int index = 1; index <= segments; index++) {
            float angle = -MathHelper.PiOver2 + MathHelper.TwoPi * percent * index / segments;
            Vector2 next = center + Calc.AngleToVector(angle, radius);
            Draw.Line(previous, next, color, thickness);
            previous = next;
        }
    }

    private static void RenderDashBar(Level level, Player player, float x, ref float y) {
        if (!AkronModule.TryUse(AkronFeatureKind.ResourceBars)) {
            return;
        }

        AkronModuleSettings settings = AkronModule.Settings;
        if (level.Paused && settings.DashBarHideWhilePaused) {
            return;
        }

        int maxDashes = EffectiveDashSlots(player);
        bool shouldShow = settings.DashBarAlwaysVisible || player.Dashes < maxDashes || player.Dashes > 0;
        if (!shouldShow || player.Dead) {
            return;
        }

        Color availableColor = ColorFromRgb(player.Dashes <= 0 ? settings.DashBarLowColor : settings.DashBarAvailableColor);
        Color emptyColor = ColorFromRgb(settings.DashBarEmptyColor) * 0.58f;
        Color fillColor = ColorFromRgb(settings.DashBarFillColor);
        Color lineColor = ColorFromRgb(settings.DashBarLineColor);

        if (settings.DashBarPlayer) {
            RenderPlayerDashBar(level, player, maxDashes, availableColor, emptyColor, fillColor, lineColor);
        }

        if (settings.DashBarHud) {
            RenderHudDashBar(level, player, maxDashes, availableColor, emptyColor, fillColor, lineColor);
            return;
        }

        RenderInlineDashBar(player, x, ref y, maxDashes, availableColor, emptyColor, lineColor);
    }

    private static void RenderInlineDashBar(Player player, float x, ref float y, int maxDashes, Color availableColor, Color emptyColor, Color lineColor) {
        RenderDashPips(player, x, ref y, maxDashes, availableColor, emptyColor, lineColor, true, 1f);
    }

    private static void RenderDashPips(Player player, float x, ref float y, int maxDashes, Color availableColor, Color emptyColor, Color lineColor, bool showLabel, float scale) {
        for (int index = 0; index < maxDashes; index++) {
            float pipX = x + index * 22f * scale;
            Color color = index < player.Dashes ? availableColor : emptyColor;
            Draw.Rect(pipX, y + 3f * scale, 16f * scale, 16f * scale, color * 0.94f);
            if (AkronModule.Settings.DashBarShowEmptyPips || index < player.Dashes) {
                Draw.HollowRect(pipX, y + 3f * scale, 16f * scale, 16f * scale, lineColor * 0.38f);
            }
        }
        if (showLabel && AkronModule.Settings.DashBarShowText) {
            ActiveFont.Draw("DASH", new Vector2(x + maxDashes * 22f + 4f, y - 1f), Vector2.Zero, Vector2.One * 0.30f, availableColor);
        }
        y += 28f * scale;
    }

    private static void RenderPlayerDashBar(Level level, Player player, int maxDashes, Color availableColor, Color emptyColor, Color fillColor, Color lineColor) {
        Vector2 playerPosition = AkronScreenProjection.WorldToHud(level, player.Position);
        if (SaveData.Instance != null && SaveData.Instance.Assists.MirrorMode) {
            playerPosition.X = 1920f - playerPosition.X;
        }
        if (Input.MoveY.Inverted) {
            playerPosition.Y = 1080f - playerPosition.Y + 96f;
        }

        float scale = AkronModuleSettings.ClampResourcePlayerScale(AkronModule.Settings.DashBarPlayerScale) / 100f;
        Vector2 size = new Vector2(PlayerResourceWidth * scale, PlayerResourceHeight * scale);
        Vector2 position = AkronModule.Settings.DashBarPlayerPosition == AkronStaminaPlayerBarPosition.Below
            ? new Vector2(playerPosition.X - size.X / 2f, playerPosition.Y + 22f * scale)
            : new Vector2(playerPosition.X - size.X / 2f, playerPosition.Y - 132f * scale);
        position += new Vector2(
            AkronModuleSettings.ClampResourcePlayerOffset(AkronModule.Settings.DashBarPlayerOffsetX),
            AkronModuleSettings.ClampResourcePlayerOffset(AkronModule.Settings.DashBarPlayerOffsetY));
        if (AkronModule.Settings.DashBarStyle == AkronDashBarStyle.Pips) {
            float pipY = position.Y - 3f * scale;
            RenderDashPips(player, position.X + 3f * scale, ref pipY, maxDashes, availableColor, emptyColor, lineColor, false, scale);
            return;
        }

        DrawDashMeter(position.X, position.Y, size.X, size.Y, player.Dashes, maxDashes, availableColor, emptyColor, fillColor, lineColor, Math.Max(1f, scale));
    }

    private static void RenderHudDashBar(Level level, Player player, int maxDashes, Color availableColor, Color emptyColor, Color fillColor, Color lineColor) {
        Vector2 position = HudAnchorPosition(AkronModule.Settings.DashBarHudPosition);
        position += new Vector2(AkronModule.Settings.DashBarHudOffsetX, AkronModule.Settings.DashBarHudOffsetY);
        position = AvoidStaminaHudOverlap(position, maxDashes);
        if (AkronModule.Settings.DashBarStyle == AkronDashBarStyle.Bar) {
            DrawDashMeter(position.X, position.Y, HudResourceWidth, HudResourceHeight, player.Dashes, maxDashes, availableColor, emptyColor, fillColor, lineColor, 2f);
            return;
        }

        float inlineY = position.Y;
        RenderInlineDashBar(player, position.X, ref inlineY, maxDashes, availableColor, emptyColor, lineColor);
    }

    private static Vector2 HudAnchorPosition(AkronStaminaHudPosition position) {
        return position switch {
            AkronStaminaHudPosition.TopLeft => Settings.Instance.SpeedrunClock == SpeedrunType.Off ? new Vector2(48f, 48f) : new Vector2(48f, 146f),
            AkronStaminaHudPosition.TopCenter => new Vector2(810f, 48f),
            AkronStaminaHudPosition.BottomRight => new Vector2(1572f, 997f),
            AkronStaminaHudPosition.BottomCenter => new Vector2(810f, 997f),
            AkronStaminaHudPosition.BottomLeft => new Vector2(48f, 997f),
            _ => new Vector2(1572f, 48f)
        };
    }

    private static Vector2 AvoidStaminaHudOverlap(Vector2 dashPosition, int maxDashes) {
        AkronModuleSettings settings = AkronModule.Settings;
        bool staminaHudActive = (settings.StaminaBar || settings.ResourceBars && settings.ResourceStaminaBar) && settings.StaminaBarHud;
        if (!staminaHudActive) {
            return dashPosition;
        }

        Vector2 staminaPosition = HudAnchorPosition(settings.StaminaBarHudPosition) +
                                  new Vector2(settings.StaminaHudOffsetX, settings.StaminaHudOffsetY);
        Rectangle staminaBounds = HudResourceBounds(
            staminaPosition,
            settings.StaminaBarStyle == AkronStaminaBarStyle.Ring ? 70f : HudResourceWidth,
            settings.StaminaBarStyle == AkronStaminaBarStyle.Ring ? 70f : HudResourceHeight);
        Rectangle dashBounds = HudResourceBounds(
            dashPosition,
            settings.DashBarStyle == AkronDashBarStyle.Bar ? HudResourceWidth : Math.Max(96f, maxDashes * 22f + 56f),
            settings.DashBarStyle == AkronDashBarStyle.Bar ? HudResourceHeight : 28f);
        if (!dashBounds.Intersects(staminaBounds)) {
            return dashPosition;
        }

        float offset = staminaBounds.Height + 12f;
        bool bottom = settings.DashBarHudPosition is AkronStaminaHudPosition.BottomLeft or AkronStaminaHudPosition.BottomCenter or AkronStaminaHudPosition.BottomRight;
        return dashPosition + new Vector2(0f, bottom ? -offset : offset);
    }

    private static Rectangle HudResourceBounds(Vector2 position, float width, float height) {
        return new Rectangle(
            (int) Math.Floor(position.X),
            (int) Math.Floor(position.Y),
            (int) Math.Ceiling(width),
            (int) Math.Ceiling(height));
    }

    private static void DrawDashMeter(float x, float y, float width, float height, int dashes, int maxDashes, Color availableColor, Color emptyColor, Color fillColor, Color lineColor, float border) {
        Draw.Rect(x - border, y - border, width + border * 2f, height + border * 2f, lineColor * 0.82f);
        Draw.Rect(x, y, width, height, fillColor);
        float pipWidth = width / maxDashes;
        for (int index = 0; index < maxDashes; index++) {
            float pipX = x + index * pipWidth;
            if (index < dashes) {
                Draw.Rect(pipX, y, pipWidth - border, height, availableColor);
            } else if (AkronModule.Settings.DashBarShowEmptyPips) {
                Draw.Rect(pipX, y, pipWidth - border, height, emptyColor);
            }
            if (index > 0) {
                Draw.Rect(pipX - border, y, border, height, lineColor * 0.58f);
            }
        }
    }

    private static int EffectiveDashSlots(Player player) {
        if (AkronModule.Settings.DashCountOverride) {
            return Math.Max(1, AkronModuleSettings.ClampDashCountOverride(AkronModule.Settings.DashCountOverrideValue));
        }

        return Calc.Clamp(player.Inventory.Dashes, 1, 5);
    }

    private static void RenderDashNumber(Level level, Player player) {
        if (level == null || player == null || player.Dead) {
            return;
        }

        string text = player.Dashes.ToString(System.Globalization.CultureInfo.InvariantCulture);
        RenderPlayerNumber(
            level,
            player,
            text,
            AkronModuleSettings.ClampDashNumberOffsetY(AkronModule.Settings.DashNumberOffsetY),
            AkronModule.Settings.DashNumberOpacity,
            AkronModule.Settings.DashNumberColor,
            AkronModule.Settings.DashNumberOutlineColor);
    }

    private static void RenderSpeedNumber(Level level, Player player) {
        if (level == null || player == null || player.Dead) {
            return;
        }

        float speed = AkronModule.Settings.SpeedNumberMode switch {
            AkronSpeedNumberMode.Horizontal => Math.Abs(player.Speed.X),
            AkronSpeedNumberMode.Vertical => Math.Abs(player.Speed.Y),
            _ => player.Speed.Length()
        };
        string text = speed.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        RenderPlayerNumber(
            level,
            player,
            text,
            AkronModuleSettings.ClampSpeedNumberOffsetY(AkronModule.Settings.SpeedNumberOffsetY),
            AkronModule.Settings.SpeedNumberOpacity,
            AkronModule.Settings.SpeedNumberColor,
            AkronModule.Settings.SpeedNumberOutlineColor);
    }

    private static void RenderPlayerNumber(Level level, Player player, string text, int offsetY, int opacityPercent, int textColor, int outlineColor) {
        Vector2 position = AkronScreenProjection.WorldToHud(level, player.Center);
        if (SaveData.Instance != null && SaveData.Instance.Assists.MirrorMode) {
            position.X = 1920f - position.X;
        }
        if (Input.MoveY.Inverted) {
            position.Y = 1080f - position.Y + 96f;
        }

        float scale = 0.32f;
        Vector2 size = ActiveFont.Measure(text) * scale;
        position += new Vector2(-size.X / 2f, offsetY - size.Y / 2f);
        float opacity = AkronModuleSettings.ClampOpacity(opacityPercent) / 100f;
        ActiveFont.DrawOutline(
            text,
            position,
            Vector2.Zero,
            Vector2.One * scale,
            ColorFromRgb(textColor) * opacity,
            2f,
            ColorFromRgb(outlineColor) * opacity);
    }
}
