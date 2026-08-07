using System;
using System.Collections.Generic;
using System.Reflection;
using Monocle;

namespace Celeste.Mod.Akron;

internal sealed class AkronTrackedVirtualAssetRegistration {
    internal AkronTrackedVirtualAssetRegistration(VirtualAsset asset) {
        Asset = asset;
    }

    internal VirtualAsset Asset { get; }
}

internal static class AkronVirtualAssetReloadTracker {
    private static readonly List<AkronTrackedVirtualAssetRegistration> Registrations =
        new List<AkronTrackedVirtualAssetRegistration>();

    internal static int Count => Registrations.Count;

    public static void Add(VirtualAsset asset) {
        if (asset != null) {
            Registrations.Add(new AkronTrackedVirtualAssetRegistration(asset));
        }
    }

    public static int Mark() {
        return Registrations.Count;
    }

    public static void DiscardSince(int marker) {
        // Fresh baseline clones only describe how to rebuild a room. They are
        // never restored, so their source assets must not stay in the reload set.
        int start = ClampMarker(marker);
        Registrations.RemoveRange(start, Registrations.Count - start);
    }

    public static IReadOnlyList<VirtualRenderTarget> GetRenderTargetsSince(int marker) {
        List<VirtualRenderTarget> renderTargets = new List<VirtualRenderTarget>();
        HashSet<VirtualRenderTarget> seen = new HashSet<VirtualRenderTarget>();
        for (int index = ClampMarker(marker); index < Registrations.Count; index++) {
            if (Registrations[index].Asset is VirtualRenderTarget renderTarget && seen.Add(renderTarget)) {
                renderTargets.Add(renderTarget);
            }
        }
        return renderTargets;
    }

    public static IReadOnlyList<AkronTrackedVirtualAssetRegistration> GetRegistrationsSince(int marker) {
        int start = ClampMarker(marker);
        return Registrations.GetRange(start, Registrations.Count - start);
    }

    private static int ClampMarker(int marker) {
        return marker < 0 ? 0 : Math.Min(marker, Registrations.Count);
    }

    public static void Remove(IReadOnlyList<AkronTrackedVirtualAssetRegistration> ownedRegistrations) {
        if (ownedRegistrations == null || ownedRegistrations.Count == 0) {
            return;
        }

        // Registration identity, not VirtualAsset identity, owns a reload
        // claim. ReloadDisposedAssets clears an old generation before a warm
        // Load can register the same process-owned asset again. A stale slot
        // must not remove that new generation's claim later.
        HashSet<AkronTrackedVirtualAssetRegistration> owned =
            new HashSet<AkronTrackedVirtualAssetRegistration>(ownedRegistrations, ReferenceEqualityComparer.Instance);
        Registrations.RemoveAll(registration => owned.Contains(registration));
    }

    public static void Clear() {
        Registrations.Clear();
    }

    public static void ReloadDisposedAssets(Level level) {
        List<AkronTrackedVirtualAssetRegistration> registrations =
            new List<AkronTrackedVirtualAssetRegistration>(Registrations);
        foreach (AkronTrackedVirtualAssetRegistration registration in registrations) {
            VirtualAsset asset = registration.Asset;
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

        Registrations.Clear();

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
