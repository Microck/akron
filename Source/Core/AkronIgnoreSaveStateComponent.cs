using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

[Tracked]
public sealed class AkronIgnoreSaveStateComponent : Component {
    private static readonly List<(Entity Entity, bool Based)> IgnoredEntities = new List<(Entity Entity, bool Based)>();

    public AkronIgnoreSaveStateComponent(bool based) : base(false, false) {
        Based = based;
    }

    public bool Based { get; }

    public static void RemoveAll(Level level) {
        IgnoredEntities.Clear();
        level.Tracker.GetComponentsCopy<AkronIgnoreSaveStateComponent>().ForEach(component => {
            AkronIgnoreSaveStateComponent ignoreComponent = (AkronIgnoreSaveStateComponent) component;
            // This bracket spans the fresh room load, so anything it stashes belongs
            // to the room about to be destroyed. That is right for entities Akron
            // owns and wrong for anything the room rebuilds, and a mod is free to
            // call IgnoreSaveState on one of those. AkronSnapshotExclusion owns the
            // lifetime of the entities it names; leave them to it.
            if (AkronSnapshotExclusion.IsExcludedFromSnapshot(ignoreComponent.Entity)) {
                return;
            }
            IgnoredEntities.Add((ignoreComponent.Entity, ignoreComponent.Based));
            level.RemoveImmediately(ignoreComponent.Entity, ignoreComponent.Based);
        });
    }

    public static void ReAddAll(Level level) {
        foreach ((Entity entity, bool based) in IgnoredEntities) {
            level.AddImmediately(entity, based);
        }

        IgnoredEntities.Clear();
    }

    internal static void RemoveAllFromSnapshot(Level level) {
        if (level == null) {
            return;
        }

        // StartPos clones the live room before filtering ignored entities. Work
        // only on that clone so Set cannot disturb a live helper, renderer, or
        // entity for even one frame. Skip lifecycle callbacks because snapshot
        // entities can own process-wide hooks that still belong to the live copy.
        List<Entity> ignored = AkronEntityListInternals.GetAll(level.Entities)
            .Concat(level.Entities)
            .Where(entity => entity != null &&
                             (entity.Get<AkronIgnoreSaveStateComponent>() != null ||
                              AkronSnapshotExclusion.IsExcludedFromSnapshot(entity)))
            .Distinct()
            .ToList();
        foreach (Entity entity in ignored) {
            AkronEntityListInternals.Remove(level.Entities, entity);
            level.TagLists.EntityRemoved(entity);
            level.Tracker.EntityRemoved(entity);
            AkronSnapshotExclusion.ReleaseTrailManagerSlot(entity);
            entity.Scene = null;
        }

        // Tracker keeps separate entity and component indexes. Rebuild both so
        // no component from a filtered UI entity remains reachable in the graph.
        Tracker.Refresh(level, force: true);
    }
}

// The one Celeste entity StartPos deliberately leaves out of a snapshot.
//
// A StartPos load rebuilds the saved room on top of a freshly loaded copy of the
// same room, and every saved object has to be provably the same object as one in
// that fresh copy. Celeste's playback tutorial ghost, the map's "playbackTutorial"
// entity, is the one map-placed entity that has never paired: a load of a room
// holding one refuses on the ghost's Scene back reference even though both rooms
// contain a ghost. The only other way to make that load succeed is to loosen the
// pairing rules, which trades a loud refusal for a silent wrong restore, so the
// ghost is left out of the snapshot and the freshly loaded room keeps its own.
//
// The cost is bounded and stateable: after a StartPos load the ghost is wherever
// a clean room load puts it, one second from the start of its loop, instead of
// where it was when the slot was set.
internal static class AkronSnapshotExclusion {
    private static readonly FieldInfo TrailManagerSnapshotsField =
        typeof(TrailManager).GetField("snapshots", BindingFlags.Instance | BindingFlags.NonPublic);

    internal static bool IsExcludedFromSnapshot(Entity entity) {
        // One named type, not a category, and the exact type rather than anything
        // assignable to it. "Rebuilt from map data" describes every map entity,
        // including the strawberries, spinners and switches StartPos exists to
        // restore exactly. What separates the ghost from those is that its state is
        // a free-running demonstration loop no gameplay code reads: PlayerPlayback
        // is not [Tracked], and nothing in Celeste does CollideCheck<PlayerPlayback>,
        // CollideFirst<PlayerPlayback> or Tracker.GetEntity<PlayerPlayback>. That
        // evidence is about Celeste's type and says nothing about a mod's subclass,
        // which can add whatever state it likes, so a subclass is saved normally. A
        // second entry here needs the same evidence for that type, not just a load
        // that failed on it.
        if (entity != null && entity.GetType() == typeof(PlayerPlayback)) {
            return true;
        }

        // Removing the ghost from the entity list is not enough on its own. A
        // trail snapshot keeps the live PlayerSprite and PlayerHair of the entity
        // it was made from and renders them itself, so the ghost stays reachable
        // through TrailManager.snapshots[i].Sprite.Entity and lands back in the
        // graph at a path no fresh room can match. A playback ghost emits one
        // trail every 0.1s for as long as it is visible, so this is the ordinary
        // case rather than an edge case, and the trail has to go with it.
        return entity is TrailManager.Snapshot snapshot &&
               (IsExcludedGhost(snapshot.Sprite?.Entity) ||
                IsExcludedGhost(snapshot.Hair?.Entity));
    }

    private static bool IsExcludedGhost(Entity owner) {
        return owner != null && owner.GetType() == typeof(PlayerPlayback);
    }

    // A trail snapshot owns a slot in its manager's fixed array, and Entity.Removed
    // is what hands that slot back. Snapshot filtering runs without lifecycle
    // callbacks by design, so the slot has to be released here or the manager keeps
    // pointing at the snapshot and the exclusion does not hold.
    internal static void ReleaseTrailManagerSlot(Entity entity) {
        if (entity is not TrailManager.Snapshot snapshot || snapshot.Manager == null ||
            TrailManagerSnapshotsField?.GetValue(snapshot.Manager) is not TrailManager.Snapshot[] slots ||
            snapshot.Index < 0 || snapshot.Index >= slots.Length ||
            !ReferenceEquals(slots[snapshot.Index], snapshot)) {
            return;
        }
        slots[snapshot.Index] = null;
    }

    // A restore resolves saved objects by their path in the live fresh room, so the
    // live room has to have the same shape the snapshot was measured against: one
    // extra entity in one list shifts every later index in that list. The snapshot
    // clone drops these entities, so the live room has to drop them for the length
    // of the restore too.
    internal static List<Entity> DetachFromLevel(Level level) {
        List<Entity> detached = new List<Entity>();
        if (level == null) {
            return detached;
        }

        foreach (Entity entity in AkronEntityListInternals.GetAll(level.Entities)
                     .Concat(level.Entities)
                     .Where(candidate => candidate != null && IsExcludedFromSnapshot(candidate))
                     .Distinct()
                     .ToList()) {
            if (level.RemoveImmediately(entity)) {
                detached.Add(entity);
            }
        }
        return detached;
    }

    // Only the ghosts come back. A trail is a one-second visual echo of a ghost that
    // was not saved either, so putting one back would draw a trail behind nothing.
    //
    // Safe to call on any exit path, including one that already reloaded the room.
    // A room load builds its own ghosts, and DetachFromLevel takes every ghost the
    // room had, so a ghost already in the list means these are stale and belong to a
    // room that no longer exists.
    internal static void ReattachToLevel(Level level, IReadOnlyList<Entity> detached) {
        if (level == null || detached == null || detached.Count == 0 ||
            level.Entities.Any(IsExcludedGhost)) {
            return;
        }

        foreach (Entity entity in detached) {
            if (IsExcludedGhost(entity)) {
                level.AddImmediately(entity);
            }
        }
    }
}

internal static class AkronImmediateEntityExtensions {
    public static void AddImmediately(this Level level, Entity entity, bool based = false) {
        EntityList entityList = level.Entities;
        if (!entityList.current.Add(entity)) {
            return;
        }

        entityList.entities.Add(entity);
        level.TagLists.EntityAdded(entity);
        level.Tracker.EntityAdded(entity);
        if (based) {
            entity.BasedAdded(level);
        } else {
            entity.Added(level);
        }
    }

    // Reports whether the entity was actually in the scene. A caller that intends
    // to put the entity back needs that answer: an entity still queued in toAdd is
    // not removed here, and re-adding it later would add it twice.
    public static bool RemoveImmediately(this Level level, Entity entity, bool based = false) {
        EntityList entityList = level.Entities;
        if (!entityList.current.Remove(entity)) {
            return false;
        }

        entityList.entities.Remove(entity);
        if (based) {
            entity.BasedRemoved(level);
        } else {
            entity.Removed(level);
        }

        level.TagLists.EntityRemoved(entity);
        level.Tracker.EntityRemoved(entity);
        Engine.Pooler.EntityRemoved(entity);
        return true;
    }

    private static void BasedAdded(this Entity entity, Level level) {
        entity.Scene = level;
        if (entity.Components != null) {
            foreach (Component component in entity.Components) {
                component.EntityAdded(level);
            }
        }

        level.SetActualDepth(entity);
    }

    private static void BasedRemoved(this Entity entity, Level level) {
        if (entity.Components != null) {
            foreach (Component component in entity.Components) {
                component.EntityRemoved(level);
            }
        }

        entity.Scene = null;
    }
}
