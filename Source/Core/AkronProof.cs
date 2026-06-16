using Celeste;
using Celeste.Mod;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
namespace Celeste.Mod.Akron;

public static class AkronProof {
    public static void ShowProofPanel(Level level, string eventName, string path = null) {
        if (level == null) {
            return;
        }

        string summary = "Reason: " + AkronModule.Session.AttemptReason;
        string legitimacy = AkronPolicy.CanExposeCleanLegitimacy()
            ? "Clean legitimacy surfaces are available."
            : "Safe mode blocks clean legitimacy surfaces in Akron proof and status outputs.";
        string pathLine = string.IsNullOrWhiteSpace(path)
            ? "Event: " + eventName
            : "JSON: " + AkronModule.Settings.FormatPathForDisplay(path);

        level.Add(new AkronProofPanel(
            "Proof context - " + eventName,
            "Classification: " + AkronPolicy.GetLegitimacySensitiveStatusLabel(AkronModule.Session.AttemptStatus),
            summary,
            legitimacy,
            pathLine
        ));
    }

    public static string BuildSummaryJson(Level level, string eventName) {
        AkronModuleSettings settings = AkronModule.Settings;
        AkronModuleSession session = AkronModule.Session;
        AkronMapOverride mapOverride = AkronMapOverrides.Get(level);
        List<string> overlays = new List<string>();
        if (settings.StreamerMode) overlays.Add("Streamer Mode");
        if (settings.ProofModeOverlay) overlays.Add("Proof-mode");
        if (settings.IsLowDistractionActive()) overlays.Add("Low-distraction");
        List<string> activeFeatures = GetActiveFeatures(level).ToList();

        StringBuilder builder = new StringBuilder();
        builder.AppendLine("{");
        AppendJson(builder, "event", eventName, true);
        AppendJson(builder, "mapSid", level?.Session?.Area.GetSID() ?? "unknown", true);
        AppendJson(builder, "room", level?.Session?.Level ?? "unknown", true);
        AppendJson(builder, "classification", AkronPolicy.GetLegitimacySensitiveStatusLabel(session.AttemptStatus), true);
        AppendJson(builder, "submissionMode", settings.SubmissionMode.ToString().ToLowerInvariant(), true, true);
        builder.Append("  \"presentationOverlays\": [");
        for (int overlayIndex = 0; overlayIndex < overlays.Count; overlayIndex++) {
            if (overlayIndex > 0) builder.Append(", ");
            builder.Append('"').Append(Escape(overlays[overlayIndex])).Append('"');
        }
        builder.AppendLine("],");
        AppendJson(builder, "reason", session.AttemptReason, true);
        builder.AppendLine("  \"activeOverrides\": {");
        AppendJson(builder, "safeMode", settings.SafeMode.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "proofRecorderGuard", settings.ProofRecorderGuard.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "recorderActive", AkronInternalRecorder.IsRecording.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "replayBufferActive", AkronInternalRecorder.IsReplayBuffering.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "endScreenHelper", settings.EndScreenHelper.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "recordingEndscreenDurationSeconds", settings.RecordingEndscreenDurationSeconds.ToString("0.00"), true, true);
        AppendJson(builder, "cleanLegitimacyAvailable", AkronPolicy.CanExposeCleanLegitimacy().ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "forceBrokerForMap", (mapOverride?.AlwaysUseBroker ?? false).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "allowUnsafeStartPosRestoreForMap", (mapOverride?.AllowUnsafeSavestates ?? false).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "disableEverestSafeBlockForMap", (mapOverride?.DisableEverestSafeBlock ?? false).ToString().ToLowerInvariant(), false, true);
        builder.AppendLine("  },");
        builder.AppendLine("  \"activeFeatures\": {");
        AppendJson(builder, "roomLabels", settings.RoomLabels.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "staminaWidget", settings.StaminaWidget.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "speedWidget", settings.SpeedWidget.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "dashWidget", settings.DashWidget.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "inputViewer", settings.InputViewer.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "roomTimerWidget", settings.RoomTimerWidget.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "deathStatsWidget", settings.DeathStatsWidget.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "reducedVisualNoise", settings.ReducedVisualNoise.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "noParticles", settings.NoParticles.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "noTrails", settings.NoTrails.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "noGlitch", settings.NoGlitch.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "noAnxiety", settings.NoAnxiety.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "noDistortion", settings.NoDistortion.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "hideSnow", settings.HideSnow.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "hideWindSnow", settings.HideWindSnow.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "hideWaterfalls", settings.HideWaterfalls.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "hideTentacles", settings.HideTentacles.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "hideHeatDistortion", settings.HideHeatDistortion.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "hitboxViewer", settings.HitboxViewer.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "entityInspector", settings.EntityInspector.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "infiniteStamina", settings.InfiniteStamina.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "infiniteDash", settings.InfiniteDash.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "noclip", settings.Noclip.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "noclipSpeed", settings.NoclipSpeed.ToString(), true, true);
        AppendJson(builder, "noclipFloatSpeed", settings.NoclipFloatSpeed.ToString(), true, true);
        AppendJson(builder, "noclipDrawOnTop", settings.NoclipDrawOnTop.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "invincibility", settings.Invincibility.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "invincibilityMode", AkronModuleSettings.NormalizeInvincibilityMode(settings.InvincibilityMode).ToString(), true);
        AppendJson(builder, "invincibilityBottomlessFallRescue", settings.InvincibilityBottomlessFallRescue.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "invincibilityCrushCollisionChanges", settings.InvincibilityCrushCollisionChanges.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "invincibilityLavaIcePushback", settings.InvincibilityLavaIcePushback.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "invincibilitySpikeGroundRefills", settings.InvincibilitySpikeGroundRefills.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "freezeGameplay", session.FreezeGameplay.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "timescaleMultiplier", session.TimescaleMultiplier.ToString("0.0"), true, true);
        AppendJson(builder, "respawnAtStartPos", settings.RespawnAtStartPos.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "tasFileConfigured", (!string.IsNullOrWhiteSpace(settings.TasFilePath)).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "brokeredStartPosState", session.UsedBrokeredSavestate.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "unsafeStartPosRestoreOverride", session.UsedUnsafeSavestateOverride.ToString().ToLowerInvariant(), false, true);
        builder.AppendLine("  },");
        builder.AppendLine("  \"proofTelemetry\": {");
        AppendJson(builder, "pauseTrackerEnabled", settings.PauseTracker.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "pauseCount", session.PauseTrackerPauseCount.ToString(), true, true);
        AppendJson(builder, "rapidPauseCount", session.PauseTrackerRapidPauseCount.ToString(), true, true);
        AppendJson(builder, "pausedSeconds", session.PauseTrackerPausedSeconds.ToString("0.000"), true, true);
        AppendJson(builder, "lagPauserEnabled", settings.LagPauser.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "lagPauserThresholdMs", settings.LagPauserThresholdMs.ToString(), true, true);
        AppendJson(builder, "lagPauserTriggerCount", session.LagPauserTriggerCount.ToString(), true, true);
        AppendJson(builder, "lastLagSpikeMs", session.LagPauserLastSpikeMs.ToString("0.000"), true, true);
        AppendJson(builder, "usedGoldenStartHelper", session.UsedGoldenStartHelper.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "usedJournalSnapshotCompare", session.UsedJournalSnapshotCompare.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "journalSnapshot", string.IsNullOrWhiteSpace(session.LastJournalSnapshotPath) ? "" : Path.GetFileName(session.LastJournalSnapshotPath), true);
        AppendJson(builder, "journalCompare", session.LastJournalCompareSummary, false);
        builder.AppendLine("  },");
        if (settings.MapVersionStamp) {
            builder.AppendLine("  \"mapVersionStamp\": {");
            AppendJson(builder, "mapSid", level?.Session?.Area.GetSID() ?? "unknown", true);
            AppendJson(builder, "room", level?.Session?.Level ?? "unknown", true);
            AppendJson(builder, "areaMode", (level?.Session?.Area.Mode.ToString() ?? "unknown"), true);
            AppendJson(builder, "loadedModules", BuildLoadedModuleStamp(), false);
            builder.AppendLine("  },");
        }
        builder.Append("  \"activeFeatureList\": [");
        for (int featureIndex = 0; featureIndex < activeFeatures.Count; featureIndex++) {
            if (featureIndex > 0) {
                builder.Append(", ");
            }
            builder.Append('"').Append(Escape(activeFeatures[featureIndex])).Append('"');
        }
        builder.AppendLine("]");
        builder.AppendLine("}");
        return builder.ToString();
    }

    public static string WriteSidecar(Level level, string eventName) {
        string directory = Path.Combine(Everest.PathGame, "Saves", "AkronProof");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "akron-proof-" + System.DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json");
        File.WriteAllText(path, BuildSummaryJson(level, eventName));
        return path;
    }

    private static void AppendJson(StringBuilder builder, string key, string value, bool comma, bool raw = false) {
        builder.Append("  \"").Append(Escape(key)).Append("\": ");
        if (raw) {
            builder.Append(value);
        } else {
            builder.Append('"').Append(Escape(value)).Append('"');
        }
        if (comma) {
            builder.Append(',');
        }
        builder.AppendLine();
    }

    private static string Escape(string value) {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static IEnumerable<string> GetActiveFeatures(Level level) {
        AkronModuleSettings settings = AkronModule.Settings;
        AkronModuleSession session = AkronModule.Session;

        if (settings.RoomLabels) yield return "RoomLabels";
        if (settings.SubmissionMode) yield return "SubmissionMode";
        if (settings.ProofRecorderGuard) yield return "ProofRecorderGuard";
        if (settings.EndScreenHelper) yield return "EndScreenHelper";
        if (settings.PauseTracker) yield return "PauseTracker";
        if (settings.MapVersionStamp) yield return "MapVersionStamp";
        if (session.UsedGoldenStartHelper) yield return "GoldenStartHelper";
        if (settings.GoldenTransparency) yield return "GoldenTransparency";
        if (settings.LagPauser) yield return "LagPauser";
        if (session.UsedJournalSnapshotCompare) yield return "JournalSnapshotCompare";
        if (settings.StaminaWidget) yield return "StaminaWidget";
        if (settings.SpeedWidget) yield return "SpeedWidget";
        if (settings.DashWidget) yield return "DashWidget";
        if (settings.InputViewer) yield return "InputViewer";
        if (settings.RoomTimerWidget) yield return "RoomTimerWidget";
        if (settings.DeathStatsWidget) yield return "DeathStatsWidget";
        if (settings.ReducedVisualNoise) yield return "ReducedVisualNoise";
        if (settings.NoParticles) yield return "NoParticles";
        if (settings.NoTrails) yield return "NoTrails";
        if (settings.NoGlitch) yield return "NoGlitch";
        if (settings.NoAnxiety) yield return "NoAnxiety";
        if (settings.NoDistortion) yield return "NoDistortion";
        if (settings.HideSnow) yield return "HideSnow";
        if (settings.HideWindSnow) yield return "HideWindSnow";
        if (settings.HideWaterfalls) yield return "HideWaterfalls";
        if (settings.HideTentacles) yield return "HideTentacles";
        if (settings.HideHeatDistortion) yield return "HideHeatDistortion";
        if (settings.HitboxViewer) yield return "HitboxViewer";
        if (settings.EntityInspector) yield return "EntityInspector";
        if (settings.InfiniteStamina) yield return "InfiniteStamina";
        if (settings.InfiniteDash) yield return "InfiniteDash";
        if (settings.Noclip) yield return "Noclip";
        if (settings.Invincibility) yield return "Invincibility";
        if (session.FreezeGameplay) yield return "FreezeGameplay";
        if (session.TimescaleEnabled && session.TimescaleMultiplier != 1f) yield return "Timescale";
        if (settings.RespawnAtStartPos) yield return "StartPosRespawn";
        if (session.UsedBrokeredSavestate) yield return "BrokeredStartPosState";
        if (session.UsedUnsafeSavestateOverride) yield return "UnsafeStartPosRestoreOverride";
        if (!string.IsNullOrWhiteSpace(settings.TasFilePath)) yield return "TasHandoff";
        if (!string.IsNullOrWhiteSpace(session.LastScreenshotPath)) yield return "ScreenshotTool";
        if (AkronMapOverrides.ShouldForceBroker(level)) yield return "MapOverrideForceBroker";
        if (AkronMapOverrides.ShouldAllowUnsafeSavestates(level)) yield return "MapOverrideAllowUnsafe";
        if (AkronMapOverrides.ShouldDisableEverestSafeBlock(level)) yield return "MapOverrideDisableEverestSafe";
    }

    private static string BuildLoadedModuleStamp() {
        return string.Join("; ", Everest.Modules
            .Where(module => module?.Metadata != null && module.GetType().Name != "NullModule")
            .Select(module => module.Metadata.Name + "@" + module.Metadata.Version)
            .OrderBy(value => value));
    }
}
