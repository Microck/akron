using System.Collections.Generic;
using System.Linq;
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
            .Where(entity => entity?.Get<AkronIgnoreSaveStateComponent>() != null)
            .Distinct()
            .ToList();
        foreach (Entity entity in ignored) {
            AkronEntityListInternals.Remove(level.Entities, entity);
            level.TagLists.EntityRemoved(entity);
            level.Tracker.EntityRemoved(entity);
            entity.Scene = null;
        }

        // Tracker keeps separate entity and component indexes. Rebuild both so
        // no component from a filtered UI entity remains reachable in the graph.
        Tracker.Refresh(level, force: true);
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

    public static void RemoveImmediately(this Level level, Entity entity, bool based = false) {
        EntityList entityList = level.Entities;
        if (!entityList.current.Remove(entity)) {
            return;
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
