using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections.Generic;

namespace Celeste.Mod.Akron;

public static partial class AkronHudRenderer {
    private static void RenderTrajectory(Level level, Player player) {
        if (level == null ||
            player == null ||
            player.Dead ||
            AkronRuntimeOptions.IsFreeCameraActive(level) ||
            !AkronModule.Settings.ShowTrajectory ||
            !AkronPolicy.CanUse(AkronFeatureKind.ShowTrajectory).Allowed) {
            return;
        }

        int frames = AkronModuleSettings.ClampShowTrajectoryFrames(AkronModule.Settings.ShowTrajectoryFrames);
        int moveX = Math.Sign(Input.MoveX.Value);
        bool onGround = player.OnGround();
        float opacity = AkronModuleSettings.ClampOpacity(AkronModule.Settings.ShowTrajectoryOpacity) / 100f;
        int thickness = AkronModuleSettings.ClampShowTrajectoryLineThickness(AkronModule.Settings.ShowTrajectoryLineThickness);
        TrajectoryShape press = SimulateTrajectory(level, player, frames, moveX, onGround, true, ColorFromRgb(AkronModule.Settings.ShowTrajectoryPressColor));
        TrajectoryShape release = SimulateTrajectory(level, player, frames, moveX, onGround, false, ColorFromRgb(AkronModule.Settings.ShowTrajectoryReleaseColor));

        DrawTrajectoryShape(level, player, press, opacity, thickness);
        DrawTrajectoryShape(level, player, release, opacity, thickness);
    }

    private static TrajectoryShape SimulateTrajectory(Level level, Player player, int frames, int moveX, bool onGround, bool jumpDown, Color color) {
        Vector2 position = player.Center;
        Vector2 speed = player.Speed;
        float maxFall = Input.MoveY.Value == 1 ? PlayerFastMaxFall : Player.MaxFall;
        int varJumpFrames = (int) Math.Round(PlayerVarJumpTime * 60f);
        bool jumpedThisFrame = jumpDown && onGround;

        if (jumpedThisFrame) {
            speed.Y = Math.Min(speed.Y, PlayerJumpSpeed);
            onGround = false;
        }

        List<Vector2> hudPoints = new List<Vector2>(frames + 1) {
            AkronScreenProjection.WorldToHud(level, position)
        };
        List<Vector2> worldCenters = new List<Vector2>(frames + 1) {
            position
        };
        bool mapAware = AkronModule.Settings.ShowTrajectoryMapAware &&
                        (AkronModule.Settings.ShowTrajectoryStopOnSolids || AkronModule.Settings.ShowTrajectoryStopOnHazards);

        // This mirrors the public reference-menu behavior without cloning a live Player:
        // one path previews jump-held, one path previews jump-released. Keeping
        // this side-effect free avoids firing triggers, deaths, sounds, or stats.
        // Map-aware mode is intentionally conservative: it truncates the path at
        // the first projected hitbox overlap instead of trying to execute real
        // Player movement logic against every entity in the room.
        for (int frame = 0; frame < frames; frame++) {
            float dt = 1f / 60f;
            float horizontalAccel = (onGround ? 1f : PlayerAirMultiplier) *
                                    (Math.Abs(speed.X) > PlayerMaxRun && Math.Sign(speed.X) == moveX ? PlayerRunReduce : PlayerRunAccel);
            speed.X = Calc.Approach(speed.X, PlayerMaxRun * moveX, horizontalAccel * dt);

            if (!onGround) {
                speed.Y = Calc.Approach(speed.Y, maxFall, PlayerGravity * dt);
                if (jumpDown && frame < varJumpFrames && speed.Y < 0f) {
                    speed.Y = Math.Min(speed.Y, jumpedThisFrame ? PlayerJumpSpeed : player.Speed.Y);
                }
            }

            Vector2 previousPosition = position;
            position += speed * dt;
            if (mapAware && ShouldStopTrajectoryAt(level, player, position)) {
                position = FindLastClearTrajectoryCenter(level, player, previousPosition, position);
                worldCenters.Add(position);
                hudPoints.Add(AkronScreenProjection.WorldToHud(level, position));
                break;
            }

            worldCenters.Add(position);
            hudPoints.Add(AkronScreenProjection.WorldToHud(level, position));
        }

        return new TrajectoryShape(hudPoints, worldCenters, position, color);
    }

    private static void DrawTrajectoryShape(Level level, Player player, TrajectoryShape shape, float opacity, int thickness) {
        if (shape.Points.Count == 0) {
            return;
        }

        if (AkronModule.Settings.ShowTrajectoryStartMarker) {
            DrawTrajectoryMarker(shape.Points[0], shape.Color, 0.85f * opacity, 9f);
        }

        for (int index = 1; index < shape.Points.Count; index++) {
            Vector2 previous = shape.Points[index - 1];
            Vector2 next = shape.Points[index];
            float fade = 1f - index / (float) shape.Points.Count;

            if (AkronModule.Settings.ShowTrajectoryLines) {
                if (AkronModule.Settings.ShowTrajectoryLineShadow) {
                    Draw.Line(previous, next, Color.Black * ((0.16f + fade * 0.28f) * opacity), thickness + 3f);
                }

                Draw.Line(previous, next, shape.Color * ((0.35f + fade * 0.60f) * opacity), thickness);
            }

            if (AkronModule.Settings.ShowTrajectoryPointMarkers && index % 6 == 0) {
                DrawTrajectoryMarker(next, shape.Color, (0.28f + fade * 0.45f) * opacity, 6f);
            }
        }

        if (AkronModule.Settings.ShowTrajectoryFrameHitboxes) {
            int interval = AkronModuleSettings.ClampShowTrajectoryFrameHitboxInterval(AkronModule.Settings.ShowTrajectoryFrameHitboxInterval);
            for (int index = interval; index < shape.WorldCenters.Count; index += interval) {
                if (AkronModule.Settings.ShowTrajectoryEndMarkers && index == shape.WorldCenters.Count - 1) {
                    continue;
                }

                float fade = 1f - index / (float) shape.WorldCenters.Count;
                DrawTrajectoryHitbox(level, player, shape.WorldCenters[index], shape.Color, opacity * (0.30f + fade * 0.45f), thickness, false);
            }
        }

        if (AkronModule.Settings.ShowTrajectoryEndMarkers) {
            DrawTrajectoryHitbox(level, player, shape.FinalCenter, shape.Color, opacity, thickness, true);
        }
    }

    private static void DrawTrajectoryHitbox(Level level, Player player, Vector2 center, Color branchColor, float opacity, int thickness, bool endMarker) {
        Rectangle finalBounds = PlayerHitboxAtCenter(player, center);
        AkronHudRect rect = AkronScreenProjection.WorldToHudRect(level, finalBounds);
        Color markerColor = AkronModule.Settings.ShowTrajectoryUseHitboxColor
            ? ColorFromRgb(AkronModule.Settings.HitboxPlayerColor)
            : ColorFromRgb(AkronModule.Settings.ShowTrajectoryEndMarkerColor);
        if (AkronModule.Settings.ShowTrajectoryHitboxOutlines) {
            for (int index = 0; index < thickness; index++) {
                if (AkronModule.Settings.ShowTrajectoryLineShadow) {
                    Draw.HollowRect(rect.X - index - 1f, rect.Y - index - 1f, rect.Width + (index + 1f) * 2f, rect.Height + (index + 1f) * 2f, Color.Black * (0.35f * opacity));
                }

                Draw.HollowRect(rect.X - index, rect.Y - index, rect.Width + index * 2f, rect.Height + index * 2f, markerColor * ((endMarker ? 0.9f : 0.65f) * opacity));
            }
        }

        if (AkronModule.Settings.ShowTrajectoryHitboxFill) {
            Draw.Rect(rect.X, rect.Y, rect.Width, rect.Height, branchColor * ((endMarker ? 0.10f : 0.05f) * opacity));
        }
    }

    private static Rectangle PlayerHitboxAtCenter(Player player, Vector2 center) {
        if (player?.Collider != null && player.Collider.Width > 0f && player.Collider.Height > 0f) {
            float offsetX = player.Collider.AbsoluteX - player.Center.X;
            float offsetY = player.Collider.AbsoluteY - player.Center.Y;
            return new Rectangle(
                (int) Math.Floor(center.X + offsetX),
                (int) Math.Floor(center.Y + offsetY),
                Math.Max(1, (int) Math.Ceiling(player.Collider.Width)),
                Math.Max(1, (int) Math.Ceiling(player.Collider.Height)));
        }

        return new Rectangle(
            (int) Math.Floor(center.X - PlayerDefaultHitboxWidth / 2f),
            (int) Math.Floor(center.Y - PlayerDefaultHitboxHeight / 2f),
            (int) PlayerDefaultHitboxWidth,
            (int) PlayerDefaultHitboxHeight);
    }

    private static Vector2 FindLastClearTrajectoryCenter(Level level, Player player, Vector2 from, Vector2 to) {
        if (ShouldStopTrajectoryAt(level, player, from)) {
            return from;
        }

        Vector2 clear = from;
        Vector2 blocked = to;
        for (int index = 0; index < 5; index++) {
            Vector2 midpoint = (clear + blocked) * 0.5f;
            if (ShouldStopTrajectoryAt(level, player, midpoint)) {
                blocked = midpoint;
            } else {
                clear = midpoint;
            }
        }

        return clear;
    }

    private static bool ShouldStopTrajectoryAt(Level level, Player player, Vector2 center) {
        Rectangle bounds = PlayerHitboxAtCenter(player, center);
        if (!level.Bounds.Contains(bounds)) {
            return true;
        }

        if (AkronModule.Settings.ShowTrajectoryStopOnSolids && level.CollideCheck<Solid>(bounds)) {
            return true;
        }

        return AkronModule.Settings.ShowTrajectoryStopOnHazards && CollidesTrajectoryHazard(level, bounds);
    }

    private static bool CollidesTrajectoryHazard(Level level, Rectangle bounds) {
        foreach (Entity entity in level.Entities) {
            if (entity.Collider == null || !AkronEntityInspector.IsHazard(entity)) {
                continue;
            }

            if (ColliderWorldBounds(entity.Collider).Intersects(bounds)) {
                return true;
            }
        }

        return false;
    }

    private static void DrawTrajectoryMarker(Vector2 position, Color color, float alpha, float size) {
        float half = size / 2f;
        if (AkronModule.Settings.ShowTrajectoryLineShadow) {
            Draw.Rect(position.X - half - 1f, position.Y - half - 1f, size + 2f, size + 2f, Color.Black * (0.28f + alpha * 0.28f));
        }

        Draw.Rect(position.X - half, position.Y - half, size, size, color * alpha);
    }

    private readonly struct TrajectoryShape {
        public TrajectoryShape(List<Vector2> points, List<Vector2> worldCenters, Vector2 finalCenter, Color color) {
            Points = points;
            WorldCenters = worldCenters;
            FinalCenter = finalCenter;
            Color = color;
        }

        public List<Vector2> Points { get; }
        public List<Vector2> WorldCenters { get; }
        public Vector2 FinalCenter { get; }
        public Color Color { get; }
    }
}
