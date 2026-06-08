using System.Collections.Generic;
using System.Reflection;
using Monocle;

namespace Celeste.Mod.Akron;

internal static class AkronVirtualAssetReloadTracker {
    private static readonly List<VirtualAsset> Assets = new List<VirtualAsset>();

    public static void Add(VirtualAsset asset) {
        if (asset != null) {
            Assets.Add(asset);
        }
    }

    public static void Clear() {
        Assets.Clear();
    }

    public static void ReloadDisposedAssets(Level level) {
        List<VirtualAsset> assets = new List<VirtualAsset>(Assets);
        foreach (VirtualAsset asset in assets) {
            switch (asset) {
                case VirtualTexture { IsDisposed: true } texture:
                    if (!texture.Name.StartsWith("dust-noise-")) {
                        texture.Reload();
                    }
                    break;
                case VirtualRenderTarget { IsDisposed: true } renderTarget:
                    renderTarget.Reload();
                    break;
            }
        }

        Assets.Clear();

        if (level?.Tracker.GetEntity<TrailManager>() is { } trailManager &&
            typeof(TrailManager).GetField("buffers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(trailManager) is VirtualRenderTarget[] buffers) {
            for (int index = 0; index < buffers.Length; index++) {
                if (buffers[index] != null && buffers[index].IsDisposed) {
                    buffers[index].Reload();
                }
            }
        }
    }
}
