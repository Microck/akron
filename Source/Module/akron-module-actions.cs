using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Celeste;
using Celeste.Editor;
using Microsoft.Xna.Framework;
using Monocle;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;

namespace Celeste.Mod.Akron;

public partial class AkronModule {
    private static void Retry(Level level, bool confirmed = false) {
        if (level == null || Engine.Scene != level || !TryUse(AkronFeatureKind.RetryHotkey)) {
            return;
        }

        if ((Settings.ConfirmRestart || Settings.ConfirmRetry) && !confirmed) {
            ShowConfirmPrompt(level, "CONFIRM RETRY", "Restart the current room attempt?", () => Retry(level, confirmed: true));
            return;
        }

        ForceRetryDeath(level);
    }

    private static void ReplacePauseMenuButtonActionIfNeeded(TextMenu.Button button) {
        if (button == null ||
            Engine.Scene is not Level level ||
            !level.Paused ||
            AkronPromptMenu.IsOpen) {
            return;
        }

        if ((Settings.ConfirmRestart || Settings.ConfirmRetry) &&
            IsPauseMenuRetryButton(button)) {
            // TextMenu.Update invokes Button.ConfirmPressed first and then the
            // button's OnPressed delegate directly. Replacing the delegate here
            // intercepts the vanilla pause action at the last safe point before
            // Celeste would kill the player.
            button.OnPressed = () => ShowConfirmPrompt(level, "RESTART ROOM", "Restart the current room attempt?", () => ForceRetryDeath(level));
            return;
        }

        if ((Settings.ConfirmFullReset || Settings.ConfirmReloadChapter) &&
            IsPauseMenuChapterRestartButton(button)) {
            button.OnPressed = () => ShowConfirmPrompt(level, "RESET PROGRESS", "Restart this chapter from the beginning?", () => RestartChapterFromPauseMenu(level));
            return;
        }
    }

    private static bool IsPauseMenuRetryButton(TextMenu.Button button) {
        return PauseButtonLabelMatches(button, "menu_pause_retry", "retry", "reintentar", "restartroom");
    }

    private static bool IsPauseMenuChapterRestartButton(TextMenu.Button button) {
        return PauseButtonLabelMatches(button, "menu_pause_restartarea", "restartchapter", "restartarea", "reiniciarcapitulo");
    }

    private static bool PauseButtonLabelMatches(TextMenu.Button button, string dialogKey, params string[] fallbackLabels) {
        string label = NormalizePauseButtonLabel(button?.Label);
        if (string.IsNullOrEmpty(label)) {
            return false;
        }

        string dialogLabel = NormalizePauseButtonLabel(Dialog.Clean(dialogKey));
        if (!string.IsNullOrEmpty(dialogLabel) && string.Equals(label, dialogLabel, StringComparison.Ordinal)) {
            return true;
        }

        foreach (string fallback in fallbackLabels) {
            if (string.Equals(label, NormalizePauseButtonLabel(fallback), StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }

    private static string NormalizePauseButtonLabel(string label) {
        if (string.IsNullOrWhiteSpace(label)) {
            return string.Empty;
        }

        string decomposed = label.Normalize(NormalizationForm.FormD);
        char[] buffer = new char[decomposed.Length];
        int length = 0;
        foreach (char character in decomposed) {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) {
                continue;
            }

            if (char.IsLetterOrDigit(character)) {
                buffer[length++] = char.ToLowerInvariant(character);
            }
        }

        return new string(buffer, 0, length);
    }

    private static void RestartChapterFromPauseMenu(Level level) {
        if (level == null || Engine.Scene != level) {
            return;
        }

#pragma warning disable CS0618
        Engine.TimeRate = 1f;
#pragma warning restore CS0618
        Audio.SetMusic(null);
        Audio.BusStopAll("bus:/gameplay_sfx", true);
        AkronPolicy.ResetAttempt("Vanilla pause-menu chapter restart ended the previous attempt.");
        AkronPracticeStats.ResetAttemptTimer();
        level.OnEndOfFrame += () => {
            if (Engine.Scene == level) {
                Engine.Scene = new LevelLoader(level.Session.Restart());
            }
        };
    }

    private static void ForceRetryDeath(Level level) {
        Player player = level.Tracker.GetEntity<Player>();
        if (player == null || player.Dead || level.Transitioning || level.InCutscene || level.SkippingCutscene) {
            return;
        }

#pragma warning disable CS0618
        Engine.TimeRate = 1f;
#pragma warning restore CS0618
        Distort.GameRate = 1f;
        Distort.Anxiety = 0f;
        level.InCutscene = false;
        level.SkippingCutscene = false;
        player.Die(Vector2.UnitY, evenIfInvincible: true);

        // EasyRetry runs level-ending hooks after forcing the death. Akron keeps
        // that compatibility, but snapshots the hook list because custom maps may
        // mutate tracked components during retry cleanup.
        foreach (Component component in new List<Component>(level.Tracker.GetComponents<LevelEndingHook>())) {
            ((LevelEndingHook) component).OnEnd?.Invoke();
        }
    }

    private static void ReloadRoom(Level level, bool confirmed = false) {
        if (level == null || level.Completed || Engine.Scene != level || !TryUse(AkronFeatureKind.RoomReload)) {
            return;
        }

        if (Settings.ConfirmReloadRoom && !confirmed) {
            ShowConfirmPrompt(level, "CONFIRM ROOM RELOAD", "Reload the current room and reset the attempt timer?", () => ReloadRoom(level, confirmed: true));
            return;
        }

        level.OnEndOfFrame += () => {
            if (Engine.Scene != level || level.Completed || !AkronPolicy.CanUse(AkronFeatureKind.RoomReload).Allowed) {
                return;
            }

            AkronPolicy.ResetAttempt("Room reload ended the previous attempt.");
            AkronPracticeStats.ResetAttemptTimer();
            level.Session.RespawnPoint = level.GetSpawnPoint(level.Camera.Position);
            ClearCustomDeathParticleBursts();
            bool wasPaused = level.Paused;
            RunRoomReloadWithTeardown(level, level.Reload);
            if (wasPaused && Engine.Scene == level && level.Paused) {
                // Reload removes the native pause menu. End its pause only after
                // the old menu is gone, so Unpause cannot attach a settings-save
                // coroutine to a menu that this reload is about to destroy.
                level.Unpause();
                // The discarded menu cannot end its audio effects. Do this
                // before notifying subscribers rather than waiting for Update.
                level.EndPauseEffects();
                NotifyLevelUnpausedAfterReload(level);
            }
        };
    }

    [ThreadStatic]
    private static Level roomReloadTeardownLevel;

    internal static void RunRoomReloadWithTeardown(Level level, Action reload) {
        Level previous = roomReloadTeardownLevel;
        roomReloadTeardownLevel = level;
        try {
            reload();
        } finally {
            roomReloadTeardownLevel = previous;
        }
    }

    internal static List<Entity> OrderRoomReloadEntities(List<Entity> entities, Level level) {
        if (level == null || !ReferenceEquals(level, roomReloadTeardownLevel) || entities == null) {
            return entities;
        }
        // Reorder only native-selected entities; retain Global exclusions and
        // leave the caller's list untouched if a downstream hook vetoes unload.
        var ordered = new List<Entity>(entities.Count);
        foreach (Entity entity in entities) {
            if (entity is Player) ordered.Add(entity);
        }
        foreach (Entity entity in entities) {
            if (entity is not Player) ordered.Add(entity);
        }
        return ordered;
    }

    internal static void LevelOnUnloadLevelForRoomReload(ILContext il) {
        Instruction unload = null;
        foreach (Instruction instruction in il.Body.Instructions) {
            if ((instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
                instruction.Operand is MethodReference method &&
                method.DeclaringType.FullName == "Celeste.Level" && method.Name == "UnloadEntities" &&
                !method.HasGenericParameters && method.HasThis && method.Parameters.Count == 1 &&
                method.Parameters[0].ParameterType.FullName == "System.Collections.Generic.List`1<Monocle.Entity>" &&
                method.ReturnType.MetadataType == MetadataType.Void) {
                if (unload != null) {
                    Logger.Log(LogLevel.Warn, nameof(AkronModule), "Ambiguous native unload call; Reload Room ordering hook disabled.");
                    return;
                }
                unload = instruction;
            }
        }
        if (unload == null) {
            Logger.Log(LogLevel.Warn, nameof(AkronModule), "Native unload call unavailable; Reload Room ordering hook disabled.");
            return;
        }
        var cursor = new ILCursor(il);
        cursor.Goto(unload, MoveType.Before);
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.EmitDelegate<Func<List<Entity>, Level, List<Entity>>>(OrderRoomReloadEntities);
    }

    // Everest keeps the event raiser internal. Resolve that exact dispatcher,
    // rather than capturing its event field, so later subscribers are included.
    private static readonly Action<Level> everestLevelUnpause = ResolveEverestLevelUnpause(typeof(Everest.Events.Level));

    internal static Action<Level> ResolveEverestLevelUnpause(Type dispatcherType) {
        try {
            MethodInfo method = dispatcherType.GetMethod("Unpause",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(Level) }, null);
            return method?.ReturnType == typeof(void) ? method.CreateDelegate<Action<Level>>() : null;
        } catch (ArgumentException) {
            return null;
        } catch (MemberAccessException) {
            return null;
        }
    }
    private static bool missingEverestUnpauseWarningLogged;

    internal static void NotifyLevelUnpausedAfterReload(Level level) {
        if (everestLevelUnpause != null) {
            everestLevelUnpause(level);
            return;
        }
        // Preserve Akron's own recovery on an incompatible Everest version,
        // while explicitly reporting that global lifecycle delivery is missing.
        if (!missingEverestUnpauseWarningLogged) {
            missingEverestUnpauseWarningLogged = true;
            Logger.Log(LogLevel.Warn, nameof(AkronModule),
                "Everest's unpause dispatcher is unavailable; paused Reload Room can only notify Akron.");
        }
        LevelOnUnpause(level);
    }

    private static void OpenDebugMap(Level level) {
        if (!TryUse(AkronFeatureKind.DebugMapLauncher)) {
            return;
        }

        level.OnEndOfFrame += () => {
            if (Engine.Scene == level && AkronPolicy.CanUse(AkronFeatureKind.DebugMapLauncher).Allowed) {
                Engine.Scene = new MapEditor(level.Session.Area);
                Engine.Commands.Open = false;
            }
        };
    }

    private static void ReloadChapter(Level level, bool confirmed = false) {
        if (level == null || Engine.Scene != level || !TryUse(AkronFeatureKind.ChapterReload)) {
            return;
        }

        if ((Settings.ConfirmFullReset || Settings.ConfirmReloadChapter) && !confirmed) {
            ShowConfirmPrompt(level, "CONFIRM FULL RESET", "Restart this chapter from the beginning?", () => ReloadChapter(level, confirmed: true));
            return;
        }

        AkronPolicy.ResetAttempt("Chapter reload ended the previous attempt.");
        AkronPracticeStats.ResetAttemptTimer();
        level.OnEndOfFrame += () => {
            if (Engine.Scene != level || !AkronPolicy.CanUse(AkronFeatureKind.ChapterReload).Allowed) {
                return;
            }
            Engine.Scene = new LevelLoader(level.Session.Restart());
        };
    }

    private static void SaveState(Level level) {
        AkronSaveLoadResult result = AkronSaveLoadService.Save(level, Settings.ActiveSavestateSlot);
        Engine.Scene?.Add(new AkronToast(DescribeSaveLoadResult("Save", result, Settings.ActiveSavestateSlot)));
    }

    private static void LoadState(Level level, bool confirmed = false)
    {
        if (Settings.ConfirmLoadState && !confirmed)
        {
            ShowConfirmPrompt(level, "CONFIRM SPEEDRUN TOOL RESTORE", "Restore Speedrun Tool slot " + Settings.ActiveSavestateSlot + "?", () => LoadState(level, confirmed: true));
            return;
        }

        int slot = Settings.ActiveSavestateSlot;
        if (confirmed)
        {
            Session requestedSession = level?.Session;
            // A native prompt runs inside Level.Update's pause-entity iteration.
            // SRT replaces that entity graph, so wait until all update wrappers
            // and temporary random scopes have finished before invoking it.
            ScheduleAfterStableEngineUpdate(() =>
            {
                if (IsConfirmedStateLoadContextCurrent(level, requestedSession, Engine.Scene))
                {
                    LoadStateNow(level, slot);
                }
            });
            return;
        }

        LoadStateNow(level, slot);
    }

    internal static bool IsConfirmedStateLoadContextCurrent(Level level, Session session, Scene scene)
    {
        return level != null && session != null &&
               ReferenceEquals(level, scene) && ReferenceEquals(level.Session, session);
    }

    private static void LoadStateNow(Level level, int slot)
    {
        // The service rechecks policy when the queued action actually executes.
        AkronSaveLoadResult result = AkronSaveLoadService.Load(level, slot);
        Engine.Scene?.Add(new AkronToast(DescribeSaveLoadResult("Load", result, slot)));
    }

    private static void ShiftSavestateSlot(int delta) {
        int nextSlot = Calc.Clamp(Settings.ActiveSavestateSlot + delta, 1, 9);
        if (nextSlot == Settings.ActiveSavestateSlot) {
            return;
        }

        Settings.ActiveSavestateSlot = nextSlot;
        Engine.Scene?.Add(new AkronToast("Active Speedrun Tool slot: " + nextSlot));
    }

    private static string DescribeSaveLoadResult(string verb, AkronSaveLoadResult result, int slot) {
        switch (result) {
            case AkronSaveLoadResult.Success:
                return (verb == "Save" ? "Saved" : "Loaded") + " slot " + slot + ".";
            case AkronSaveLoadResult.NoState:
                return "Slot " + slot + " is empty.";
            case AkronSaveLoadResult.SessionMismatch:
                return "Slot " + slot + " belongs to a different save file or map session.";
            case AkronSaveLoadResult.Blocked:
                return verb + " blocked by Akron policy.";
            case AkronSaveLoadResult.BrokerUnavailable:
                return "Speedrun Tool broker is unavailable.";
            default:
                return verb + " failed for slot " + slot + ".";
        }
    }
    public static bool TryUse(AkronFeatureKind feature) {
        AkronPolicyDecision decision = AkronPolicy.CanUse(feature);
        AkronLog.RecordPolicyCheck(feature, decision);
        if (!decision.Allowed) {
            Engine.Scene?.Add(new AkronToast(decision.Message));
            return false;
        }

        AkronPolicy.RecordFeatureUse(feature);
        AkronLog.RecordFeatureUse(feature);
        return true;
    }

    internal static bool TryUseRuntime(AkronFeatureKind feature) {
        AkronPolicyDecision decision = AkronPolicy.CanUse(feature);
        if (!decision.Allowed) {
            return false;
        }

        AkronLog.RecordPolicyCheck(feature, decision);
        AkronPolicy.RecordFeatureUse(feature);
        AkronLog.RecordFeatureUse(feature);
        return true;
    }

    private static void ShowConfirmPrompt(Level level, string title, string body, Action confirmedAction) {
        if (level == null) {
            return;
        }

        AkronPromptMenu.Show(
            level,
            title,
            body,
            new AkronPromptOption("Confirm", () => {
                if (Engine.Scene == level) {
                    confirmedAction();
                }
            })
        );
    }

    public static void SetActiveSavestateSlot(int slot) {
        Settings.ActiveSavestateSlot = Calc.Clamp(slot, 1, 9);
    }

    public static void PerformRetry(Level level) => Retry(level);
    public static void PerformReloadRoom(Level level) => ReloadRoom(level);
    public static void PerformOpenDebugMap(Level level) => OpenDebugMap(level);
    public static void PerformReloadChapter(Level level) => ReloadChapter(level);
    public static void PerformSaveState(Level level) => SaveState(level);
    public static void PerformLoadState(Level level) => LoadState(level);
    public static string DescribeSavestateResult(string verb, AkronSaveLoadResult result, int slot) {
        return DescribeSaveLoadResult(verb, result, slot);
    }
}
