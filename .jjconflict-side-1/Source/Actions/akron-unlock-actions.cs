using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronActions {
    public static bool UncompleteCurrentLevel(Level level) {
        if (level == null || SaveData.Instance == null) {
            Engine.Scene?.Add(new AkronToast("Open a chapter before clearing completion."));
            return false;
        }

        if (!AkronModule.TryUse(AkronFeatureKind.UnlockSystem)) {
            return false;
        }

        AreaKey area = level.Session.Area;
        if (area.ID < 0 || area.ID >= SaveData.Instance.Areas.Count) {
            Engine.Scene?.Add(new AkronToast("Save area data is unavailable."));
            return false;
        }

        AreaModeStats mode = SaveData.Instance.Areas[area.ID].Modes[(int) area.Mode];
        SetUnlockMember(mode, "Completed", false);
        SetUnlockMember(mode, "SingleRunCompleted", false);
        level.Completed = false;
        SaveUnlockState();
        Engine.Scene?.Add(new AkronToast("Current chapter completion cleared."));
        return true;
    }

    public static bool UnlockPaths() {
        if (!AkronModule.TryUse(AkronFeatureKind.UnlockSystem) || SaveData.Instance == null) {
            Engine.Scene?.Add(new AkronToast("Save data is unavailable."));
            return false;
        }

        // Celeste's own gate/checkpoint checks branch on CheatMode, so this is the
        // smallest unlock-path mutation that still follows native behavior.
        SaveData.Instance.CheatMode = true;
        SaveUnlockState();
        Engine.Scene?.Add(new AkronToast("Path and checkpoint gates unlocked."));
        return true;
    }

    public static bool UnlockASides() {
        if (!AkronModule.TryUse(AkronFeatureKind.UnlockSystem) || SaveData.Instance == null) {
            Engine.Scene?.Add(new AkronToast("Save data is unavailable."));
            return false;
        }

        SaveData.Instance.UnlockedAreas = SaveData.Instance.MaxArea;
        SaveData.Instance.RevealedChapter9 = true;
        SaveUnlockState();
        Engine.Scene?.Add(new AkronToast("A-Sides unlocked."));
        return true;
    }

    public static bool UnlockBSides() {
        if (!AkronModule.TryUse(AkronFeatureKind.UnlockSystem) || SaveData.Instance == null) {
            Engine.Scene?.Add(new AkronToast("Save data is unavailable."));
            return false;
        }

        for (int areaId = 0; areaId <= SaveData.Instance.MaxArea && areaId < SaveData.Instance.Areas.Count; areaId++) {
            AreaData areaData = AreaData.Get(areaId);
            if (areaData != null && !areaData.Interlude && areaData.HasMode(AreaMode.BSide)) {
                SaveData.Instance.Areas[areaId].Cassette = true;
            }
        }

        SaveUnlockState();
        Engine.Scene?.Add(new AkronToast("B-Sides unlocked."));
        return true;
    }

    public static bool UnlockCSides() {
        if (!AkronModule.TryUse(AkronFeatureKind.UnlockSystem) || SaveData.Instance == null) {
            Engine.Scene?.Add(new AkronToast("Save data is unavailable."));
            return false;
        }

        // Native C-side visibility is derived from UnlockedModes. CheatMode is the
        // only built-in save flag that unlocks C-sides without fabricating hearts.
        SaveData.Instance.CheatMode = true;
        SaveUnlockState();
        Engine.Scene?.Add(new AkronToast("C-Sides unlocked."));
        return true;
    }

    public static bool UnlockGoldenBerries() {
        if (!AkronModule.TryUse(AkronFeatureKind.UnlockSystem) || SaveData.Instance == null) {
            Engine.Scene?.Add(new AkronToast("Save data is unavailable."));
            return false;
        }

        // Golden berry display is also gated by CheatMode in journal/overworld UI.
        // This exposes berry availability without marking individual berries as
        // collected, which would be misleading recovery state.
        SaveData.Instance.CheatMode = true;
        SaveUnlockState();
        Engine.Scene?.Add(new AkronToast("Golden berry visibility enabled."));
        return true;
    }

    public static bool UnlockAllLevels() {
        if (!AkronModule.TryUse(AkronFeatureKind.UnlockSystem) || SaveData.Instance == null) {
            Engine.Scene?.Add(new AkronToast("Save data is unavailable."));
            return false;
        }

        ApplyNativeUnlockEverything();
        SaveUnlockState();
        Engine.Scene?.Add(new AkronToast("All native unlocks applied."));
        return true;
    }

    public static int ObtainRoomBerries(Level level) {
        return ObtainBerries(level, levelData => string.Equals(levelData.Name, level.Session.Level, StringComparison.Ordinal));
    }

    public static int ObtainChapterBerries(Level level) {
        return ObtainBerries(level, _ => true);
    }

    public static string DescribeBerryObtainOptions() {
        List<string> parts = new List<string>();
        if (AkronModule.Settings.BerryObtainIncludeRegular) {
            parts.Add("regular");
        }

        if (AkronModule.Settings.BerryObtainIncludeGolden) {
            parts.Add("golden");
        }

        if (AkronModule.Settings.BerryObtainIncludeMoon) {
            parts.Add("moon");
        }

        return parts.Count == 0 ? "none" : string.Join(", ", parts);
    }

    public static string DescribeUnlockState() {
        if (SaveData.Instance == null) {
            return "No save";
        }

        return "Cheat " + (SaveData.Instance.CheatMode ? "on" : "off") +
               " / Areas " + SaveData.Instance.UnlockedAreas + "/" + SaveData.Instance.MaxArea;
    }

    private static int ObtainBerries(Level level, Func<LevelData, bool> levelFilter) {
        if (level == null || level.Session?.MapData == null || SaveData.Instance == null) {
            Engine.Scene?.Add(new AkronToast("Open a chapter before obtaining berries."));
            return 0;
        }

        if (!AkronModule.TryUse(AkronFeatureKind.UnlockSystem)) {
            return 0;
        }

        if (!AkronModule.Settings.BerryObtainIncludeRegular &&
            !AkronModule.Settings.BerryObtainIncludeGolden &&
            !AkronModule.Settings.BerryObtainIncludeMoon) {
            Engine.Scene?.Add(new AkronToast("No berry types selected."));
            return 0;
        }

        AreaKey area = level.Session.Area;
        if (area.ID < 0 || area.ID >= SaveData.Instance.Areas.Count) {
            Engine.Scene?.Add(new AkronToast("Save area data is unavailable."));
            return 0;
        }

        AreaModeStats mode = SaveData.Instance.Areas[area.ID].Modes[(int) area.Mode];
        List<EntityID> obtained = new List<EntityID>();
        foreach (LevelData levelData in level.Session.MapData.Levels.Where(levelFilter)) {
            foreach (EntityData entity in levelData.Entities) {
                if (!ShouldObtainBerryEntity(entity, out bool golden)) {
                    continue;
                }

                EntityID id = new EntityID(levelData.Name, entity.ID);
                if (mode.Strawberries.Contains(id)) {
                    continue;
                }

                SaveData.Instance.AddStrawberry(area, id, golden);
                level.Session.DoNotLoad.Add(id);
                level.Session.Strawberries.Add(id);
                obtained.Add(id);
            }
        }

        RemoveObtainedBerryEntities(level, obtained);
        SaveUnlockState();
        Engine.Scene?.Add(new AkronToast("Obtained " + obtained.Count + " berries."));
        return obtained.Count;
    }

    private static bool ShouldObtainBerryEntity(EntityData entity, out bool golden) {
        golden = false;
        if (entity == null) {
            return false;
        }

        if (IsGoldenBerryEntity(entity)) {
            golden = true;
            return AkronModule.Settings.BerryObtainIncludeGolden;
        }

        if (!string.Equals(entity.Name, "strawberry", StringComparison.Ordinal)) {
            return false;
        }

        if (entity.Bool("moon")) {
            return AkronModule.Settings.BerryObtainIncludeMoon;
        }

        return AkronModule.Settings.BerryObtainIncludeRegular;
    }

    private static bool IsGoldenBerryEntity(EntityData entity) {
        return string.Equals(entity.Name, "goldenBerry", StringComparison.Ordinal) ||
               string.Equals(entity.Name, "memorialTextController", StringComparison.Ordinal);
    }

    private static void RemoveObtainedBerryEntities(Level level, List<EntityID> obtained) {
        if (obtained.Count == 0) {
            return;
        }

        HashSet<EntityID> obtainedIds = new HashSet<EntityID>(obtained);
        foreach (Strawberry strawberry in level.Entities.OfType<Strawberry>().ToList()) {
            if (obtainedIds.Contains(strawberry.ID)) {
                strawberry.RemoveSelf();
            }
        }
    }

    private static void ApplyNativeUnlockEverything() {
        SaveData.Instance.RevealedChapter9 = true;
        SaveData.Instance.UnlockedAreas = SaveData.Instance.MaxArea;
        SaveData.Instance.CheatMode = true;
        Settings.Instance.Pico8OnMainMenu = true;
        Settings.Instance.VariantsUnlocked = true;
    }

    private static void SaveUnlockState() {
        UserIO.SaveHandler(true, true);
    }

    private static void SetUnlockMember(object target, string name, object value) {
        if (target == null) {
            return;
        }

        Type type = target.GetType();
        PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.CanWrite == true) {
            property.SetValue(target, value);
            return;
        }

        FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        field?.SetValue(target, value);
    }
}
