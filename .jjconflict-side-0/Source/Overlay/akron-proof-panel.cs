using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

public sealed class AkronProofPanel : Entity {
    private readonly string[] lines;
    private float timer = 4.2f;

    public AkronProofPanel(params string[] lines) {
        this.lines = lines ?? System.Array.Empty<string>();
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
        if (AkronCapture.IsCapturingGameFrame || AkronModule.ShouldHideAkronRenderSurfacesBehindDeathWipe()) {
            return;
        }

        float alpha = Calc.Clamp(timer, 0f, 1f);
        float x = 96f;
        float y = 760f;
        float width = 1240f;
        float height = 48f + lines.Length * 32f;

        Draw.Rect(x - 18f, y - 16f, width, height, Color.Black * 0.78f * alpha);
        Draw.HollowRect(x - 18f, y - 16f, width, height, Color.CornflowerBlue * 0.7f * alpha);
        for (int index = 0; index < lines.Length; index++) {
            Color color = index == 0 ? Color.White : Color.LightGray;
            ActiveFont.Draw(lines[index], new Vector2(x, y + index * 30f), Vector2.Zero, Vector2.One * 0.38f, color * alpha);
        }
    }
}
