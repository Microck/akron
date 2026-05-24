using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

internal static class AkronEntityListInternals {
    private const BindingFlags EntityListFlags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo EntitiesField = typeof(EntityList).GetField("entities", EntityListFlags);
    private static readonly FieldInfo ToAddField = typeof(EntityList).GetField("toAdd", EntityListFlags);
    private static readonly FieldInfo ToAwakeField = typeof(EntityList).GetField("toAwake", EntityListFlags);
    private static readonly FieldInfo ToRemoveField = typeof(EntityList).GetField("toRemove", EntityListFlags);
    private static readonly FieldInfo CurrentField = typeof(EntityList).GetField("current", EntityListFlags);
    private static readonly FieldInfo AddingField = typeof(EntityList).GetField("adding", EntityListFlags);
    private static readonly FieldInfo RemovingField = typeof(EntityList).GetField("removing", EntityListFlags);

    public static List<Entity> GetAll(EntityList list) {
        List<Entity> entities = new List<Entity>();
        AddRange(entities, EntitiesField, list);
        AddRange(entities, ToAddField, list);
        AddRange(entities, ToAwakeField, list);
        AddRange(entities, ToRemoveField, list);
        AddRange(entities, CurrentField, list);
        AddRange(entities, AddingField, list);
        AddRange(entities, RemovingField, list);
        return entities;
    }

    public static bool Remove(EntityList list, Entity entity) {
        bool removed = false;
        removed |= RemoveFromList(EntitiesField, list, entity);
        removed |= RemoveFromList(ToAddField, list, entity);
        removed |= RemoveFromList(ToAwakeField, list, entity);
        removed |= RemoveFromList(ToRemoveField, list, entity);
        removed |= RemoveFromSet(CurrentField, list, entity);
        removed |= RemoveFromSet(AddingField, list, entity);
        removed |= RemoveFromSet(RemovingField, list, entity);
        return removed;
    }

    private static void AddRange(List<Entity> target, FieldInfo field, EntityList list) {
        if (field?.GetValue(list) is IEnumerable<Entity> source) {
            target.AddRange(source);
        }
    }

    private static bool RemoveFromList(FieldInfo field, EntityList list, Entity entity) {
        if (field?.GetValue(list) is not List<Entity> entities) {
            return false;
        }

        return entities.RemoveAll(candidate => ReferenceEquals(candidate, entity)) > 0;
    }

    private static bool RemoveFromSet(FieldInfo field, EntityList list, Entity entity) {
        return field?.GetValue(list) is HashSet<Entity> entities && entities.Remove(entity);
    }
}

internal sealed class AkronLevelRenderState {
    private const BindingFlags SceneFlags = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    internal static readonly FieldInfo RendererListField = typeof(Scene).GetField("<RendererList>k__BackingField", SceneFlags);
    private static readonly FieldInfo RendererListSceneField = typeof(RendererList).GetField("scene", InstanceFlags);
    private static readonly FieldInfo GameplayRendererInstanceField = typeof(GameplayRenderer).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly FieldInfo[] LevelRenderFields = {
        typeof(Level).GetField(nameof(Level.FormationBackdrop)),
        typeof(Level).GetField(nameof(Level.Background)),
        typeof(Level).GetField(nameof(Level.Foreground)),
        typeof(Level).GetField(nameof(Level.GameplayRenderer)),
        typeof(Level).GetField(nameof(Level.HudRenderer)),
        typeof(Level).GetField(nameof(Level.Lighting)),
        typeof(Level).GetField(nameof(Level.Displacement)),
        typeof(Level).GetField(nameof(Level.Bloom)),
        typeof(Level).GetField(nameof(Level.FgTilesLightMask)),
        typeof(Level).GetField(nameof(Level.Particles)),
        typeof(Level).GetField(nameof(Level.ParticlesBG)),
        typeof(Level).GetField(nameof(Level.ParticlesFG)),
        typeof(Level).GetField(nameof(Level.HiresSnow)),
        typeof(Level).GetField(nameof(Level.SubHudRenderer))
    };

    private readonly object rendererList;
    private readonly Dictionary<FieldInfo, object> fieldValues;

    private AkronLevelRenderState(Level level) {
        rendererList = RendererListField?.GetValue(level);
        fieldValues = new Dictionary<FieldInfo, object>();
        foreach (FieldInfo field in LevelRenderFields) {
            if (field != null) {
                fieldValues[field] = field.GetValue(level);
            }
        }
    }

    public static AkronLevelRenderState Capture(Level level) {
        return level == null ? null : new AkronLevelRenderState(level);
    }

    public void Restore(Level level) {
        if (level == null) {
            return;
        }

        RendererListField?.SetValue(level, rendererList);
        foreach (KeyValuePair<FieldInfo, object> pair in fieldValues) {
            pair.Key.SetValue(level, pair.Value);
        }
    }

    public static void RelinkRendererCameras(Level level) {
        if (level?.Camera == null) {
            return;
        }

        RendererListSceneField?.SetValue(RendererListField?.GetValue(level), level);
        if (level.GameplayRenderer != null) {
            // GameplayRenderer.Render calls the static Begin(), and Begin() reads a
            // private static GameplayRenderer.instance instead of the renderer that
            // is currently rendering. Copying a saved Level graph can leave that
            // static pointed at the saved camera, making room entities render
            // offscreen while backdrops still draw from the live Level camera.
            GameplayRendererInstanceField?.SetValue(null, level.GameplayRenderer);
        }

        // GameplayRenderer owns a Camera field used by the final gameplay draw.
        // After a full Level graph clone, that field can point at the saved
        // camera object while logic, culling, and player CameraTarget use
        // level.Camera. Keeping renderer cameras tied to the live Level camera
        // prevents the visible viewport from freezing at the restore position.
        foreach (object renderer in EnumerateRenderers(level).Distinct()) {
            if (renderer == null) {
                continue;
            }

            FieldInfo field = renderer.GetType().GetField("Camera", InstanceFlags);
            if (field != null && typeof(Camera).IsAssignableFrom(field.FieldType) && field.GetValue(renderer) != level.Camera) {
                field.SetValue(renderer, level.Camera);
            }
        }
    }

    private static IEnumerable<object> EnumerateRenderers(Level level) {
        yield return level.GameplayRenderer;
        yield return level.HudRenderer;
        yield return level.SubHudRenderer;
        yield return level.Background;
        yield return level.Foreground;
        yield return level.Lighting;
        yield return level.Displacement;
        yield return level.Bloom;

        object rendererList = RendererListField?.GetValue(level);
        if (rendererList == null) {
            yield break;
        }

        foreach (FieldInfo field in rendererList.GetType().GetFields(InstanceFlags)) {
            if (field.GetValue(rendererList) is not IEnumerable values || field.FieldType == typeof(string)) {
                continue;
            }

            foreach (object value in values) {
                yield return value;
            }
        }
    }
}

internal static class AkronLevelGraphRepair {
    private static readonly Dictionary<Type, List<FieldInfo>> SceneReferenceFields = new Dictionary<Type, List<FieldInfo>>();

    public static void RelinkEntitiesToLevel(Level level) {
        if (level == null) {
            return;
        }

        level.Entities.Scene = level;
        foreach (Entity entity in AkronEntityListInternals.GetAll(level.Entities).Concat(level.Entities.ToList()).Distinct()) {
            RelinkObjectToLevel(entity, level);
            foreach (Component component in entity.Components) {
                RelinkObjectToLevel(component, level);
            }
        }
    }

    private static void RelinkObjectToLevel(object target, Level level) {
        if (target == null) {
            return;
        }

        foreach (FieldInfo field in GetSceneReferenceFields(target.GetType())) {
            object value = field.GetValue(target);
            if (value == null || value is Scene) {
                field.SetValue(target, level);
            }
        }
    }

    private static List<FieldInfo> GetSceneReferenceFields(Type type) {
        if (SceneReferenceFields.TryGetValue(type, out List<FieldInfo> fields)) {
            return fields;
        }

        fields = new List<FieldInfo>();
        Type current = type;
        while (current != null) {
            fields.AddRange(current
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(field => !field.IsInitOnly && typeof(Scene).IsAssignableFrom(field.FieldType)));
            current = current.BaseType;
        }

        SceneReferenceFields[type] = fields;
        return fields;
    }
}
