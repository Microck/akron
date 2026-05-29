using System.Collections.Generic;
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
