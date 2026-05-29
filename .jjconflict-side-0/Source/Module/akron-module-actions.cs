using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Celeste;
using Celeste.Editor;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

public partial class AkronModule {
    private static void Retry(Level level, bool confirmed = false) {
        if (!TryUse(AkronFeatureKind.RetryHotkey)) {
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
        AkronPracticeStats.ResetAttemptTimer(level);
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
        if (!TryUse(AkronFeatureKind.RoomReload)) {
            return;
        }

        if (Settings.ConfirmReloadRoom && !confirmed) {
            ShowConfirmPrompt(level, "CONFIRM ROOM RELOAD", "Reload the current room and reset the attempt timer?", () => ReloadRoom(level, confirmed: true));
            return;
        }

        level.OnEndOfFrame += () => {
            if (Engine.Scene != level) {
                return;
            }

            AkronPolicy.ResetAttempt("Room reload ended the previous attempt.");
            AkronPracticeStats.ResetAttemptTimer(level);
            level.Session.RespawnPoint = level.GetSpawnPoint(level.Camera.Position);
            level.Reload();
        };
    }

    private static void OpenDebugMap(Level level) {
        if (!TryUse(AkronFeatureKind.DebugMapLauncher)) {
            return;
        }

        level.OnEndOfFrame += () => {
            if (Engine.Scene is Level) {
                Engine.Scene = new MapEditor(level.Session.Area);
                Engine.Commands.Open = false;
            }
        };
    }

    private static void ReloadChapter(Level level, bool confirmed = false) {
        if (!TryUse(AkronFeatureKind.ChapterReload)) {
            return;
        }

        if ((Settings.ConfirmFullReset || Settings.ConfirmReloadChapter) && !confirmed) {
            ShowConfirmPrompt(level, "CONFIRM FULL RESET", "Restart this chapter from the beginning?", () => ReloadChapter(level, confirmed: true));
            return;
        }

        AkronPolicy.ResetAttempt("Chapter reload ended the previous attempt.");
        AkronPracticeStats.ResetAttemptTimer(level);
        level.OnEndOfFrame += () => {
            Engine.Scene = new LevelLoader(level.Session.Restart());
        };
    }

    private static void SaveState(Level level) {
        if (TryPromptForBroker(level, load: false)) {
            return;
        }

        AkronSaveLoadResult result = AkronSaveLoadService.Save(level, Settings.ActiveSavestateSlot);
        Engine.Scene?.Add(new AkronToast(DescribeSaveLoadResult("Save", result, Settings.ActiveSavestateSlot)));
    }

    private static void LoadState(Level level, bool confirmed = false) {
        if (Settings.ConfirmLoadState && !confirmed) {
            ShowConfirmPrompt(level, "CONFIRM STARTPOS RESTORE", "Restore Akron StartPos snapshot slot " + Settings.ActiveSavestateSlot + "?", () => LoadState(level, confirmed: true));
            return;
        }

        if (TryPromptForBroker(level, load: true)) {
            return;
        }

        AkronSaveLoadResult result = AkronSaveLoadService.Load(level, Settings.ActiveSavestateSlot);
        Engine.Scene?.Add(new AkronToast(DescribeSaveLoadResult("Load", result, Settings.ActiveSavestateSlot)));
    }

    private static void ShiftSavestateSlot(int delta) {
        int nextSlot = Calc.Clamp(Settings.ActiveSavestateSlot + delta, 1, 9);
        if (nextSlot == Settings.ActiveSavestateSlot) {
            return;
        }

        Settings.ActiveSavestateSlot = nextSlot;
        Engine.Scene?.Add(new AkronToast("Active StartPos snapshot slot: " + nextSlot));
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
                return verb + " blocked by the current ruleset or StartPos restore safety check.";
            case AkronSaveLoadResult.BrokerUnavailable:
                return "Speedrun Tool broker is unavailable.";
            default:
                return verb + " failed for slot " + slot + ".";
        }
    }
    public static bool TryUse(AkronFeatureKind feature) {
        AkronPolicyDecision decision = AkronPolicy.CanUse(feature);
        if (!decision.Allowed) {
            if (Engine.Scene is Level level && AkronPolicy.ShouldOfferRulesetEscape(feature)) {
                ShowRulesetConflictPrompt(level, feature);
            } else {
                Engine.Scene?.Add(new AkronToast(decision.Message));
            }

            return false;
        }

        AkronPolicy.RecordFeatureUse(feature);
        return true;
    }

    private static bool TryPromptForBroker(Level level, bool load) {
        int slot = Settings.ActiveSavestateSlot;
        if (!AkronSaveLoadService.ShouldPromptForBroker(level, slot, out string reason)) {
            return false;
        }

        ShowBrokerPrompt(level, load, reason);
        return true;
    }

    private static void ShowBrokerPrompt(Level level, bool load, string reason) {
        int slot = Settings.ActiveSavestateSlot;
        string actionNoun = load ? "load" : "save";
        string mapSid = level.Session.Area.GetSID();
        AkronPromptMenu.Show(
            level,
            "BROKERED SAVESTATE",
            "Native Akron " + actionNoun + " is blocked on this map.\n" +
            reason + "\n" +
            "Akron can hand this off to Speedrun Tool for slot " + slot + " on " + mapSid + ".",
            new AkronPromptOption("Use Broker Once", () => FinishBrokerPromptAction(level, load)),
            new AkronPromptOption("Always Use Broker On This Map", () => {
                AkronMapOverrides.GetOrCreate(level).AlwaysUseBroker = true;
                FinishBrokerPromptAction(level, load);
            }),
            new AkronPromptOption("Disable Broker Warnings Globally", () => {
                Settings.SpeedrunToolBrokerWarnings = false;
                FinishBrokerPromptAction(level, load);
            })
        );
    }

    private static void FinishBrokerPromptAction(Level level, bool load) {
        AkronSaveLoadResult result = load
            ? AkronSaveLoadService.Load(level, Settings.ActiveSavestateSlot)
            : AkronSaveLoadService.Save(level, Settings.ActiveSavestateSlot);
        Engine.Scene?.Add(new AkronToast(DescribeSaveLoadResult(load ? "Load" : "Save", result, Settings.ActiveSavestateSlot)));
    }

    private static void ShowRulesetConflictPrompt(Level level, AkronFeatureKind feature) {
        FeatureDefinition definition = AkronFeatureRegistry.Get(feature);
        PrimaryRuleset nextRuleset = AkronPolicy.GetSuggestedRuleset(feature);
        string currentRulesetLabel = AkronModuleSettings.FormatPrimaryRuleset(PrimaryRuleset.LeaderboardClean);
        string stayLabel = AkronPolicy.CanExposeCleanLegitimacy() ? "Stay Leaderboard Clean" : "Keep Current Guardrails";
        AkronPromptMenu.Show(
            level,
            "RULESET CONFLICT",
            currentRulesetLabel + " blocks " + definition.Label + ".\n" +
            "Akron does not auto-switch rulesets for legitimacy-sensitive features.",
            new AkronPromptOption(stayLabel, () => { }),
            new AkronPromptOption("Switch To " + FormatRulesetLabel(nextRuleset), () => {
                ApplyRuleset(nextRuleset);
                Engine.Scene?.Add(new AkronToast("Switched ruleset to " + FormatRulesetLabel(nextRuleset) + ". Use the action again."));
            })
        );
    }

    private static void ShowConfirmPrompt(Level level, string title, string body, Action confirmedAction) {
        if (level == null) {
            return;
        }

        AkronPromptMenu.Show(
            level,
            title,
            body,
            new AkronPromptOption("Confirm", confirmedAction)
        );
    }

    private static void ApplyRuleset(PrimaryRuleset ruleset) {
        Settings.ApplyRulesetDefaults(ruleset);
    }

    private static string FormatRulesetLabel(PrimaryRuleset ruleset) {
        return AkronModuleSettings.FormatPrimaryRuleset(ruleset);
    }

    public static void SetActiveSavestateSlot(int slot) {
        Settings.ActiveSavestateSlot = Calc.Clamp(slot, 1, 9);
    }

    public static void PerformApplyRuleset(PrimaryRuleset ruleset) => ApplyRuleset(ruleset);
    public static void PerformRetry(Level level) => Retry(level);
    public static void PerformReloadRoom(Level level) => ReloadRoom(level);
    public static void PerformOpenDebugMap(Level level) => OpenDebugMap(level);
    public static void PerformReloadChapter(Level level) => ReloadChapter(level);
    public static void PerformSaveState(Level level) => SaveState(level);
    public static void PerformLoadState(Level level) => LoadState(level);
    public static void PerformBrokerPromptForAutomation(Level level, bool load, string reason = "Automation broker prompt exercise.") {
        if (level == null) {
            return;
        }

        ShowBrokerPrompt(level, load, string.IsNullOrWhiteSpace(reason) ? "Automation broker prompt exercise." : reason);
    }

    public static string DescribeSavestateResult(string verb, AkronSaveLoadResult result, int slot) {
        return DescribeSaveLoadResult(verb, result, slot);
    }
}
