using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public static class AkronAutosave {
    public static void NotifyLevelBegin(Level level) {
        if (AkronModule.Session == null) {
            return;
        }

        AkronModule.Session.AkronAutosaveTimer = 0f;
        AkronModule.Session.AkronAutosaveCooldown = AkronModuleSettings.ClampAutosaveMinimumDelaySeconds(AkronModule.Settings.AutosaveMinimumDelaySeconds);
        if (AkronModule.Settings.Autosave && AkronModule.Settings.AutosaveOnRoomLoad) {
            Request("room load", force: false);
        }
    }

    public static void Update(Level level) {
        if (level == null || AkronModule.Session == null || !AkronModule.Settings.Autosave) {
            return;
        }

        float delta = Engine.DeltaTime;
        if (AkronModule.Session.AkronAutosaveCooldown > 0f) {
            AkronModule.Session.AkronAutosaveCooldown -= delta;
        }

        int interval = AkronModuleSettings.ClampAutosaveIntervalSeconds(AkronModule.Settings.AutosaveIntervalSeconds);
        AkronModule.Session.AkronAutosaveTimer += delta;
        if (AkronModule.Session.AkronAutosaveTimer >= interval) {
            Request("interval", force: false);
        }
    }

    public static void NotifyRespawn(Level level) {
        if (AkronModule.Settings.Autosave && AkronModule.Settings.AutosaveOnSpawnUpdate) {
            Request("spawn update", force: false);
        }

        if (AkronModule.Settings.Autosave && AkronModule.Settings.AutosaveOnRespawn) {
            Request("respawn", force: false);
        }
    }

    public static void NotifyPause() {
        if (AkronModule.Settings.Autosave && AkronModule.Settings.AutosaveOnPause) {
            Request("pause", force: false);
        }
    }

    public static void SaveNow() {
        Request("manual", force: true);
    }

    private static void Request(string reason, bool force) {
        if (SaveData.Instance == null || UserIO.Saving) {
            return;
        }

        if (!force && AkronModule.Settings.AutosaveAvoidGameplay && AkronModule.Session?.AkronAutosaveCooldown > 0f) {
            return;
        }

        AkronModule.Session.AkronAutosaveTimer = 0f;
        AkronModule.Session.AkronAutosaveCooldown = AkronModuleSettings.ClampAutosaveMinimumDelaySeconds(AkronModule.Settings.AutosaveMinimumDelaySeconds);
        UserIO.SaveHandler(true, AkronModule.Settings.AutosaveSaveSettings);
        Engine.Scene?.Add(new AkronToast("Autosave: " + reason + "."));
    }
}
