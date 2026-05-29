using System;
using System.Threading;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

public static class AkronMountainViewer {
    public static void Open() {
        Engine.Scene = new AkronMountainViewerLoader(Overworld.StartMode.MainMenu) {
            PostLoadAction = overworld => {
                overworld.Mountain.SnapCamera(-1, new MountainCamera(new Vector3(0f, 6.0f, 12.0f), MountainRenderer.RotateLookAt));
                overworld.Mountain.GotoRotationMode();
                overworld.Maddy.Hide();

                if (overworld.Current is OuiMainMenu mainMenu) {
                    mainMenu.mountainStartFront = false;
                }

                overworld.Current.Components.RemoveAll<Coroutine>();
                overworld.Current.Focused = false;
                overworld.Current.Visible = false;
            }
        };
    }
}

public class AkronMountainViewerLoader : OverworldLoader {
    public Action<Overworld> PostLoadAction { get; set; }
    private bool postLoadApplied;

    public AkronMountainViewerLoader(Overworld.StartMode startMode, HiresSnow snow = null) : base(startMode, snow) {
        Snow = null;
        fadeIn = false;
    }

    public override void Begin() {
        Add(new HudRenderer());
        RendererList.UpdateLists();

        Session session = SaveData.Instance?.CurrentSession_Safe;
        Entity loaderEntity = new Entity();
        loaderEntity.Add(new Coroutine(Routine(session)));
        Add(loaderEntity);

        activeThread = Thread.CurrentThread;
        activeThread.Priority = ThreadPriority.Lowest;
        RunThread.Start(LoadThreadExtended, "AKRON_MOUNTAIN_VIEWER", highPriority: true);
    }

    public override void Update() {
        base.Update();

        // Overworld scene mutation needs to stay on the main thread. The loader thread
        // should only finish construction and let Update apply the final UI changes.
        if (!postLoadApplied && overworld != null) {
            postLoadApplied = true;
            PostLoadAction?.Invoke(overworld);
        }
    }

    private void LoadThreadExtended() {
        LoadThread();
    }
}
