using System;
using System.Collections.Generic;
using System.Linq;
using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;

namespace Celeste.Mod.Akron;

internal sealed class AkronFrozenDeathSprite : Entity {
    private readonly MTexture bodyTexture;
    private readonly Vector2 bodyPosition;
    private readonly Vector2 bodyOrigin;
    private readonly Vector2 bodyScale;
    private readonly float bodyRotation;
    private readonly Color bodyColor;
    private readonly SpriteEffects bodyEffects;
    private readonly List<HairNodeSnapshot> hairNodes;
    private readonly int hairFrame;
    private readonly int facing;
    private readonly int hairCount;
    private readonly Color hairColor;
    private readonly Color hairBorder;
    private readonly float hairAlpha;

    private AkronFrozenDeathSprite(
        Player player,
        MTexture bodyTexture,
        Vector2 bodyPosition,
        Vector2 bodyOrigin,
        Vector2 bodyScale,
        float bodyRotation,
        Color bodyColor,
        SpriteEffects bodyEffects,
        List<HairNodeSnapshot> hairNodes,
        int hairFrame,
        int facing,
        int hairCount,
        Color hairColor,
        Color hairBorder,
        float hairAlpha)
        : base(Vector2.Zero) {
        Depth = player.Depth;
        this.bodyTexture = bodyTexture;
        this.bodyPosition = bodyPosition;
        this.bodyOrigin = bodyOrigin;
        this.bodyScale = bodyScale;
        this.bodyRotation = bodyRotation;
        this.bodyColor = bodyColor;
        this.bodyEffects = bodyEffects;
        this.hairNodes = hairNodes;
        this.hairFrame = hairFrame;
        this.facing = facing;
        this.hairCount = hairCount;
        this.hairColor = hairColor;
        this.hairBorder = hairBorder;
        this.hairAlpha = hairAlpha;
    }

    public static AkronFrozenDeathSprite Capture(Player player) {
        if (player?.Sprite?.Texture == null) {
            return null;
        }

        PlayerSprite sprite = player.Sprite;
        PlayerHair hair = player.Hair;
        int facing = player.Facing == Facings.Left ? -1 : 1;
        Vector2 scale = sprite.Scale;
        scale.X *= facing;
        List<HairNodeSnapshot> hairNodes = new List<HairNodeSnapshot>();
        if (hair?.Nodes != null) {
            for (int i = 0; i < hair.Nodes.Count; i++) {
                hairNodes.Add(new HairNodeSnapshot(hair.Nodes[i]));
            }
        }

        return new AkronFrozenDeathSprite(
            player,
            sprite.Texture,
            sprite.RenderPosition.Floor(),
            sprite.Origin,
            scale,
            sprite.Rotation,
            sprite.Color,
            sprite.Effects,
            hairNodes,
            sprite.HairFrame,
            facing,
            sprite.HairCount,
            hair?.Color ?? Player.NormalHairColor,
            hair?.Border ?? Color.Black,
            hair?.Alpha ?? 1f);
    }

    public override void Update() {
        base.Update();
        if (Scene is not Level level) {
            RemoveSelf();
            return;
        }

        Player player = level.Tracker.GetEntity<Player>();
        bool hasDeadBody = level.Entities.OfType<PlayerDeadBody>().Any();
        if (player != null && !player.Dead && !hasDeadBody && !level.Transitioning) {
            RemoveSelf();
        }
    }

    public override void Render() {
        if (AkronModule.ShouldHideAkronRenderSurfacesBehindDeathWipe()) {
            return;
        }

        DrawHair();
        bodyTexture.Draw(bodyPosition, bodyOrigin, bodyColor, bodyScale, bodyRotation, bodyEffects);
    }

    private void DrawHair() {
        if (hairNodes.Count == 0 || hairCount <= 0) {
            return;
        }

        Vector2 origin = new Vector2(5f, 5f);
        Color borderColor = hairBorder * hairAlpha;
        Color centerColor = hairColor * hairAlpha;
        int clampedHairFrame = Math.Max(0, hairFrame);
        if (borderColor.A > 0) {
            for (int i = 0; i < hairCount && i < hairNodes.Count; i++) {
                MTexture texture = HairTexture(i, clampedHairFrame);
                Vector2 scale = HairScale(i);
                Vector2 node = hairNodes[i].Position.Floor();
                texture.Draw(node + new Vector2(-1f, 0f), origin, borderColor, scale);
                texture.Draw(node + new Vector2(1f, 0f), origin, borderColor, scale);
                texture.Draw(node + new Vector2(0f, -1f), origin, borderColor, scale);
                texture.Draw(node + new Vector2(0f, 1f), origin, borderColor, scale);
            }
        }

        for (int i = Math.Min(hairCount, hairNodes.Count) - 1; i >= 0; i--) {
            HairTexture(i, clampedHairFrame).Draw(hairNodes[i].Position.Floor(), origin, centerColor, HairScale(i));
        }
    }

    private static MTexture HairTexture(int index, int hairFrame) {
        return index == 0
            ? GFX.Game.GetAtlasSubtexturesAt("characters/player/bangs", hairFrame)
            : GFX.Game["characters/player/hair00"];
    }

    private Vector2 HairScale(int index) {
        float scale = 0.25f + (1f - index / (float) hairCount) * 0.75f;
        return new Vector2((index == 0 ? facing : scale) * Math.Abs(bodyScale.X), scale);
    }

    private readonly struct HairNodeSnapshot {
        public HairNodeSnapshot(Vector2 position) {
            Position = position;
        }

        public Vector2 Position { get; }
    }
}
