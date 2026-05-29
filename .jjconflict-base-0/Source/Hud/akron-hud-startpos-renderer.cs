using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using System;
using System.Collections.Generic;

namespace Celeste.Mod.Akron;

public static partial class AkronHudRenderer {
    private static void RenderStartPosLabel(AkronStartPos startPos, float x, ref float y) {
        string index = AkronActions.DescribeStartPosIndex(Engine.Scene as Level);
        string text = FormatStartPosLabel(index);
        if (startPos == null) {
            text += AkronModule.Settings.StartPosLabelFormat == AkronStartPosLabelFormat.CountOnly ? " unset" : " (unset)";
        }

        AkronHudLabelStyleSettings style = AkronModuleSettings.CloneLabelStyle(AkronModule.Settings.StartPosLabelStyle);
        if (AkronModule.Settings.StartPosLabelAnchor == AkronHudAnchor.TopLeft) {
            DrawText(text, x, ref y, ColorFromRgb(AkronModule.Settings.StartPosLabelColor), style);
            return;
        }

        float scale = 0.42f * (style.Scale / 100f);
        Vector2 size = ActiveFont.Measure(text) * scale;
        Vector2 position = AnchorBoxPosition(AkronModule.Settings.StartPosLabelAnchor, size) + new Vector2(style.OffsetX, style.OffsetY);
        DrawTextAt(text, position, ColorFromRgb(AkronModule.Settings.StartPosLabelColor), style);
    }

    private static string FormatStartPosLabel(string index) {
        return AkronModule.Settings.StartPosLabelFormat switch {
            AkronStartPosLabelFormat.CountOnly => index,
            AkronStartPosLabelFormat.SlotAndCount => "Slot " + AkronModule.Settings.ActiveStartPosSlot + "  " + index,
            _ => "StartPos: " + index
        };
    }

    private static void RenderStartPosMousePreview(Level level, Player player) {
        if (level == null ||
            player == null ||
            !AkronModule.Settings.StartPosMousePlacement ||
            !AkronPolicy.CanUse(AkronFeatureKind.StartPosTools).Allowed) {
            return;
        }

        AkronStartPos active = AkronActions.GetActiveStartPos();
        if (active != null && IsStartPosInCurrentLevel(level, active)) {
            DrawStartPosMarker(level, player, active.Position, AkronModuleSettings.ClampOpacity(AkronModule.Settings.StartPosPreviewOpacity) / 180f, placed: true);
        }

        MouseState mouse = Mouse.GetState();
        Vector2 world = AkronScreenProjection.MouseScreenToWorld(level, new Vector2(mouse.X, mouse.Y));
        world.X = Calc.Clamp(world.X, level.Bounds.Left, level.Bounds.Right);
        world.Y = Calc.Clamp(world.Y, level.Bounds.Top, level.Bounds.Bottom);

        float opacity = AkronModuleSettings.ClampOpacity(AkronModule.Settings.StartPosPreviewOpacity) / 100f;
        DrawStartPosMarker(level, player, world, opacity, placed: false);
    }

    private static void RenderScannerExportOverlays(Level level, Player player) {
        if (level == null || AkronModule.Settings == null) {
            return;
        }

        AkronModuleSettings settings = AkronModule.Settings;
        if (settings.ScreenshotScannerExportAutoKillAreas && settings.AutoKillArea) {
            foreach (Rectangle area in AkronModule.GetAutoKillAreas()) {
                if (area.Width > 0 && area.Height > 0) {
                    DrawWorldRect(level, area, Color.OrangeRed, 0.16f, 2);
                }
            }
        }

        if (settings.ScreenshotScannerExportAutoDeafenAreas && settings.AutoDeafenArea) {
            foreach (Rectangle area in AkronModule.GetAutoDeafenAreas()) {
                if (area.Width > 0 && area.Height > 0) {
                    DrawWorldRect(level, area, Color.DeepSkyBlue, 0.16f, 2);
                }
            }
        }

        if (settings.ScreenshotScannerExportStartPositions) {
            RenderScannerExportStartPositions(level, player);
        }
    }

    private static void RenderScannerExportStartPositions(Level level, Player player) {
        string areaSid = level.Session.Area.GetSID();
        string room = level.Session.Level;
        foreach (KeyValuePair<int, AkronStartPos> pair in AkronModule.Session?.StartPositions ?? new Dictionary<int, AkronStartPos>()) {
            AkronStartPos startPos = pair.Value;
            if (startPos == null ||
                !string.Equals(startPos.AreaSid, areaSid, StringComparison.Ordinal) ||
                !string.Equals(startPos.Room, room, StringComparison.Ordinal)) {
                continue;
            }

            DrawScannerExportStartPosMarker(level, player, pair.Key, startPos.Position);
        }
    }

    private static void DrawScannerExportStartPosMarker(Level level, Player player, int slot, Vector2 world) {
        float opacity = Math.Max(0.45f, AkronModuleSettings.ClampOpacity(AkronModule.Settings.StartPosPreviewOpacity) / 100f);
        Color accent = ColorFromRgb(AkronModule.Settings.StartPosLabelColor) * opacity;
        Rectangle hitbox = new Rectangle((int) Math.Round(world.X - 4f), (int) Math.Round(world.Y - 11f), 8, 11);
        AkronHudRect rect = AkronScreenProjection.WorldToHudRect(level, hitbox);
        Draw.Rect(rect.X, rect.Y, rect.Width, rect.Height, accent * 0.16f);
        Draw.HollowRect(rect.X, rect.Y, rect.Width, rect.Height, accent);
        Draw.Line(new Vector2(rect.X, rect.Y), new Vector2(rect.X + rect.Width, rect.Y + rect.Height), accent * 0.9f);
        Draw.Line(new Vector2(rect.X + rect.Width, rect.Y), new Vector2(rect.X, rect.Y + rect.Height), accent * 0.9f);

        if (player != null) {
            DrawStartPosPlayerPreview(level, player, world, opacity * 0.85f);
        }

        string label = slot.ToString(System.Globalization.CultureInfo.InvariantCulture);
        float scale = 0.26f;
        Vector2 labelPosition = new Vector2(rect.X + rect.Width + 3f, rect.Y - 10f);
        ActiveFont.DrawOutline(label, labelPosition, Vector2.Zero, Vector2.One * scale, accent, 2f, Color.Black * opacity);
    }

    private static bool IsStartPosInCurrentLevel(Level level, AkronStartPos startPos) {
        return startPos != null &&
               string.Equals(startPos.AreaSid, level.Session.Area.GetSID(), StringComparison.Ordinal) &&
               string.Equals(startPos.Room, level.Session.Level, StringComparison.Ordinal);
    }

    private static void DrawStartPosMarker(Level level, Player player, Vector2 world, float opacity, bool placed) {
        Color accent = ColorFromRgb(AkronModule.Settings.StartPosLabelColor) * opacity;
        Rectangle hitbox = new Rectangle((int) Math.Round(world.X - 4f), (int) Math.Round(world.Y - 11f), 8, 11);
        AkronHudRect rect = AkronScreenProjection.WorldToHudRect(level, hitbox);

        DrawStartPosPlayerPreview(level, player, world, opacity);
        Draw.Rect(rect.X, rect.Y, rect.Width, rect.Height, accent * (placed ? 0.08f : 0.16f));
        Draw.HollowRect(rect.X, rect.Y, rect.Width, rect.Height, accent);
        if (placed) {
            float right = rect.X + rect.Width;
            float bottom = rect.Y + rect.Height;
            Draw.Line(new Vector2(rect.X, rect.Y), new Vector2(right, bottom), accent * 0.8f);
            Draw.Line(new Vector2(right, rect.Y), new Vector2(rect.X, bottom), accent * 0.8f);
        } else {
            Draw.Circle(new Vector2(rect.X + rect.Width / 2f, rect.Y + rect.Height), Math.Max(2f, rect.Width * 0.18f), Color.White * opacity, 12);
        }
    }

    private static void DrawStartPosPlayerPreview(Level level, Player player, Vector2 world, float opacity) {
        PlayerSprite sprite = player.Sprite;
        if (sprite?.Texture == null) {
            return;
        }

        float scale = AkronScreenProjection.CurrentViewportScale();
        int facing = ResolveStartPosPreviewFacing(player);
        Vector2 spriteWorld = world + (sprite.RenderPosition - player.Position);
        Vector2 spriteHud = AkronScreenProjection.WorldToHud(level, spriteWorld);
        Vector2 spriteScale = new Vector2(Math.Abs(sprite.Scale.X) * facing * scale, sprite.Scale.Y * scale);
        sprite.Texture.Draw(spriteHud, sprite.Origin, sprite.Color * opacity, spriteScale, sprite.Rotation, sprite.Effects);
        DrawStartPosPreviewHair(level, player, world, facing, scale, opacity);
    }

    private static int ResolveStartPosPreviewFacing(Player player) {
        AkronStartPos startPos = AkronActions.GetActiveStartPos();
        AkronStartPosFacing facing = startPos?.Facing ?? AkronModule.Settings.StartPosConfiguredFacing;
        if (facing == AkronStartPosFacing.Left) {
            return -1;
        }
        if (facing == AkronStartPosFacing.Right) {
            return 1;
        }

        return player.Facing == Facings.Left ? -1 : 1;
    }

    private static void DrawStartPosPreviewHair(Level level, Player player, Vector2 world, int facing, float viewportScale, float opacity) {
        PlayerHair hair = player.Hair;
        PlayerSprite sprite = player.Sprite;
        if (hair?.Nodes == null || sprite == null || sprite.HairCount <= 0) {
            return;
        }

        Vector2 origin = new Vector2(5f, 5f);
        Color border = hair.Border * (hair.Alpha * opacity);
        Color center = hair.Color * (hair.Alpha * opacity);
        int count = Math.Min(sprite.HairCount, hair.Nodes.Count);
        int hairFrame = Math.Max(0, sprite.HairFrame);

        if (border.A > 0) {
            for (int i = 0; i < count; i++) {
                DrawStartPosPreviewHairNode(level, player, world, i, hairFrame, facing, viewportScale, border, origin, true);
            }
        }

        for (int i = count - 1; i >= 0; i--) {
            DrawStartPosPreviewHairNode(level, player, world, i, hairFrame, facing, viewportScale, center, origin, false);
        }
    }

    private static void DrawStartPosPreviewHairNode(Level level, Player player, Vector2 world, int index, int hairFrame, int facing, float viewportScale, Color color, Vector2 origin, bool outline) {
        PlayerHair hair = player.Hair;
        PlayerSprite sprite = player.Sprite;
        Vector2 offset = hair.Nodes[index] - player.Position;
        if (facing != (player.Facing == Facings.Left ? -1 : 1)) {
            offset.X = -offset.X;
        }

        Vector2 hud = AkronScreenProjection.WorldToHud(level, world + offset);
        Vector2 scale = StartPosPreviewHairScale(index, sprite.HairCount, facing, Math.Abs(sprite.Scale.X), viewportScale);
        MTexture texture = index == 0
            ? GFX.Game.GetAtlasSubtexturesAt("characters/player/bangs", hairFrame)
            : GFX.Game["characters/player/hair00"];

        if (outline) {
            texture.Draw(hud + new Vector2(-viewportScale, 0f), origin, color, scale);
            texture.Draw(hud + new Vector2(viewportScale, 0f), origin, color, scale);
            texture.Draw(hud + new Vector2(0f, -viewportScale), origin, color, scale);
            texture.Draw(hud + new Vector2(0f, viewportScale), origin, color, scale);
            return;
        }

        texture.Draw(hud, origin, color, scale);
    }

    private static Vector2 StartPosPreviewHairScale(int index, int hairCount, int facing, float bodyScaleX, float viewportScale) {
        float scale = 0.25f + (1f - index / (float) hairCount) * 0.75f;
        return new Vector2((index == 0 ? facing : scale) * bodyScaleX * viewportScale, scale * viewportScale);
    }
}
