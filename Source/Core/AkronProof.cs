using Celeste;
using Celeste.Mod;
using System.Collections.Generic;
using System.Globalization;
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
        List<string> overlays = new List<string>();
        if (settings.StreamerMode) overlays.Add("Streamer Mode");
        if (settings.ProofModeOverlay && (!settings.SubmissionMode || AkronPolicy.CanUse(AkronFeatureKind.SubmissionMode).Allowed)) overlays.Add("Proof-mode");
        if (Effective(settings.IsLowDistractionActive(), AkronFeatureKind.ReducedVisualNoise)) overlays.Add("Low-distraction");
        List<string> activeFeatures = GetActiveFeatures(level).ToList();

        StringBuilder builder = new StringBuilder();
        builder.AppendLine("{");
        AppendJson(builder, "event", eventName, true);
        AppendJson(builder, "mapSid", level?.Session?.Area.GetSID() ?? "unknown", true);
        AppendJson(builder, "room", level?.Session?.Level ?? "unknown", true);
        AppendJson(builder, "classification", AkronPolicy.GetLegitimacySensitiveStatusLabel(session.AttemptStatus), true);
        AppendJson(builder, "submissionMode", Effective(settings.SubmissionMode, AkronFeatureKind.SubmissionMode).ToString().ToLowerInvariant(), true, true);
        builder.Append("  \"presentationOverlays\": [");
        for (int overlayIndex = 0; overlayIndex < overlays.Count; overlayIndex++) {
            if (overlayIndex > 0) builder.Append(", ");
            builder.Append('"').Append(Escape(overlays[overlayIndex])).Append('"');
        }
        builder.AppendLine("],");
        AppendJson(builder, "reason", session.AttemptReason, true);
        builder.AppendLine("  \"activeOverrides\": {");
        AppendJson(builder, "safeMode", settings.SafeMode.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "proofRecorderGuard", Effective(settings.ProofRecorderGuard, AkronFeatureKind.ProofRecorderGuard).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "recorderActive", AkronInternalRecorder.IsRecording.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "replayBufferActive", AkronInternalRecorder.IsReplayBuffering.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "endScreenHelper", Effective(settings.EndScreenHelper, AkronFeatureKind.EndScreenHelper).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "recordingEndscreenDurationSeconds", settings.RecordingEndscreenDurationSeconds.ToString("0.00", CultureInfo.InvariantCulture), true, true);
        AppendJson(builder, "cleanLegitimacyAvailable", AkronPolicy.CanExposeCleanLegitimacy().ToString().ToLowerInvariant(), false, true);
        builder.AppendLine("  },");
        builder.AppendLine("  \"activeFeatures\": {");
        AppendJson(builder, "roomLabels", Effective(settings.RoomLabels, AkronFeatureKind.RoomLabelOverlay).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "staminaWidget", Effective(settings.StaminaWidget, AkronFeatureKind.StaminaWidget).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "speedWidget", Effective(settings.SpeedWidget, AkronFeatureKind.SpeedWidget).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "dashWidget", Effective(settings.DashWidget, AkronFeatureKind.DashWidget).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "inputViewer", Effective(settings.InputViewer, AkronFeatureKind.InputViewer).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "roomTimerWidget", Effective(settings.RoomTimerWidget, AkronFeatureKind.RoomTimer).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "deathStatsWidget", Effective(settings.DeathStatsWidget, AkronFeatureKind.DeathStats).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "reducedVisualNoise", Effective(settings.ReducedVisualNoise, AkronFeatureKind.ReducedVisualNoise).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "noParticles", Effective(settings.NoParticles, AkronFeatureKind.ReducedVisualNoise).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "noTrails", Effective(settings.NoTrails, AkronFeatureKind.ReducedVisualNoise).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "noGlitch", Effective(settings.NoGlitch, AkronFeatureKind.ReducedVisualNoise).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "noAnxiety", Effective(settings.NoAnxiety, AkronFeatureKind.ReducedVisualNoise).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "noDistortion", Effective(settings.NoDistortion, AkronFeatureKind.ReducedVisualNoise).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "hideSnow", Effective(settings.HideSnow, AkronFeatureKind.ReducedVisualNoise).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "hideWindSnow", Effective(settings.HideWindSnow, AkronFeatureKind.ReducedVisualNoise).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "hideWaterfalls", Effective(settings.HideWaterfalls, AkronFeatureKind.ReducedVisualNoise).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "hideTentacles", Effective(settings.HideTentacles, AkronFeatureKind.ReducedVisualNoise).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "hideHeatDistortion", Effective(settings.HideHeatDistortion, AkronFeatureKind.ReducedVisualNoise).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "hitboxViewer", Effective(settings.HitboxViewer, AkronFeatureKind.HitboxViewer).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "entityInspector", Effective(settings.EntityInspector, AkronFeatureKind.EntityInspector).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "infiniteStamina", Effective(settings.InfiniteStamina, AkronFeatureKind.InfiniteStamina).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "infiniteDash", Effective(settings.InfiniteDash, AkronFeatureKind.InfiniteDash).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "noclip", Effective(settings.Noclip, AkronFeatureKind.Noclip).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "noclipSpeed", settings.NoclipSpeed.ToString(), true, true);
        AppendJson(builder, "noclipFloatSpeed", settings.NoclipFloatSpeed.ToString(), true, true);
        AppendJson(builder, "noclipDrawOnTop", Effective(settings.NoclipDrawOnTop, AkronFeatureKind.Noclip).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "invincibility", Effective(settings.Invincibility, AkronFeatureKind.Invincibility).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "invincibilityMode", AkronModuleSettings.NormalizeInvincibilityMode(settings.InvincibilityMode).ToString(), true);
        AppendJson(builder, "invincibilityBottomlessFallRescue", Effective(settings.InvincibilityBottomlessFallRescue, AkronFeatureKind.Invincibility).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "invincibilityCrushCollisionChanges", Effective(settings.InvincibilityCrushCollisionChanges, AkronFeatureKind.Invincibility).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "invincibilityLavaIcePushback", Effective(settings.InvincibilityLavaIcePushback, AkronFeatureKind.Invincibility).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "invincibilitySpikeGroundRefills", Effective(settings.InvincibilitySpikeGroundRefills, AkronFeatureKind.Invincibility).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "freezeGameplay", Effective(session.FreezeGameplay, AkronFeatureKind.Freeze).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "timescaleMultiplier", (AkronPolicy.CanUse(AkronFeatureKind.Timescale).Allowed ? session.TimescaleMultiplier : 1f).ToString("0.0", CultureInfo.InvariantCulture), true, true);
        AppendJson(builder, "respawnAtStartPos", Effective(settings.RespawnAtStartPos, AkronFeatureKind.StartPosTools).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "tasFileConfigured", (!string.IsNullOrWhiteSpace(settings.TasFilePath)).ToString().ToLowerInvariant(), true, true);
        // Last entry in the block, so no trailing comma: the sidecar has to parse as JSON.
        AppendJson(builder, "brokeredStartPosState", session.UsedBrokeredSavestate.ToString().ToLowerInvariant(), false, true);
        builder.AppendLine("  },");
        builder.AppendLine("  \"proofTelemetry\": {");
        AppendJson(builder, "pauseTrackerEnabled", Effective(settings.PauseTracker, AkronFeatureKind.PauseTracker).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "pauseCount", (AkronPolicy.CanUse(AkronFeatureKind.PauseTracker).Allowed ? session.PauseTrackerPauseCount : 0).ToString(CultureInfo.InvariantCulture), true, true);
        AppendJson(builder, "rapidPauseCount", (AkronPolicy.CanUse(AkronFeatureKind.PauseTracker).Allowed ? session.PauseTrackerRapidPauseCount : 0).ToString(CultureInfo.InvariantCulture), true, true);
        AppendJson(builder, "pausedSeconds", (AkronPolicy.CanUse(AkronFeatureKind.PauseTracker).Allowed ? session.PauseTrackerPausedSeconds : 0f).ToString("0.000", CultureInfo.InvariantCulture), true, true);
        AppendJson(builder, "lagPauserEnabled", Effective(settings.LagPauser, AkronFeatureKind.LagPauser).ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "lagPauserThresholdMs", settings.LagPauserThresholdMs.ToString(CultureInfo.InvariantCulture), true, true);
        AppendJson(builder, "lagPauserRecoveryGraceMs", settings.LagPauserRecoveryGraceMs.ToString(CultureInfo.InvariantCulture), true, true);
        AppendJson(builder, "lagPauserRepeatCooldownMs", settings.LagPauserRepeatCooldownMs.ToString(CultureInfo.InvariantCulture), true, true);
        AppendJson(builder, "lagPauserTriggerCount", session.LagPauserTriggerCount.ToString(CultureInfo.InvariantCulture), true, true);
        AppendJson(builder, "lastLagSpikeMs", session.LagPauserLastSpikeMs.ToString("0.000", CultureInfo.InvariantCulture), true, true);
        AppendJson(builder, "usedGoldenStartHelper", session.UsedGoldenStartHelper.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "usedJournalSnapshotCompare", session.UsedJournalSnapshotCompare.ToString().ToLowerInvariant(), true, true);
        AppendJson(builder, "journalSnapshot", string.IsNullOrWhiteSpace(session.LastJournalSnapshotPath) ? "" : Path.GetFileName(session.LastJournalSnapshotPath), true);
        AppendJson(builder, "journalCompare", session.LastJournalCompareSummary, false);
        builder.AppendLine("  },");
        if (Effective(settings.MapVersionStamp, AkronFeatureKind.MapVersionStamp)) {
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
        return WriteSidecarFile(directory, BuildSummaryJson(level, eventName));
    }

    // Two sidecars in the same second used to land on the same name, and the second one erased
    // the first. The QA proof overlay writes one per StartPos capture and restore, so that is
    // reachable. Milliseconds separate them, and a name is claimed by creating the file rather
    // than by asking whether it exists, so two writers cannot pick the same one.
    private static string WriteSidecarFile(string directory, string content) {
        string stamp = System.DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        for (int attempt = 1; ; attempt++) {
            string suffix = attempt == 1 ? string.Empty : "-" + attempt.ToString(CultureInfo.InvariantCulture);
            string path = Path.Combine(directory, "akron-proof-" + stamp + suffix + ".json");
            try {
                using FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                using StreamWriter writer = new StreamWriter(stream);
                writer.Write(content);
                return path;
            } catch (IOException) when (attempt < 1000 && File.Exists(path)) {
                // Name taken by a sidecar written in the same millisecond. Try the next one.
            }
        }
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

    private static bool Effective(bool enabled, AkronFeatureKind feature) {
        return enabled && AkronPolicy.CanUse(feature).Allowed;
    }

    private static IEnumerable<string> GetActiveFeatures(Level level) {
        AkronModuleSettings settings = AkronModule.Settings;
        AkronModuleSession session = AkronModule.Session;

        if (Effective(settings.RoomLabels, AkronFeatureKind.RoomLabelOverlay)) yield return "RoomLabels";
        if (Effective(settings.SubmissionMode, AkronFeatureKind.SubmissionMode)) yield return "SubmissionMode";
        if (Effective(settings.ProofRecorderGuard, AkronFeatureKind.ProofRecorderGuard)) yield return "ProofRecorderGuard";
        if (Effective(settings.EndScreenHelper, AkronFeatureKind.EndScreenHelper)) yield return "EndScreenHelper";
        if (Effective(settings.PauseTracker, AkronFeatureKind.PauseTracker)) yield return "PauseTracker";
        if (Effective(settings.MapVersionStamp, AkronFeatureKind.MapVersionStamp)) yield return "MapVersionStamp";
        if (session.UsedGoldenStartHelper) yield return "GoldenStartHelper";
        if (Effective(settings.GoldenTransparency, AkronFeatureKind.GoldenTransparency)) yield return "GoldenTransparency";
        if (Effective(settings.LagPauser, AkronFeatureKind.LagPauser)) yield return "LagPauser";
        if (session.UsedJournalSnapshotCompare) yield return "JournalSnapshotCompare";
        if (Effective(settings.StaminaWidget, AkronFeatureKind.StaminaWidget)) yield return "StaminaWidget";
        if (Effective(settings.SpeedWidget, AkronFeatureKind.SpeedWidget)) yield return "SpeedWidget";
        if (Effective(settings.DashWidget, AkronFeatureKind.DashWidget)) yield return "DashWidget";
        if (Effective(settings.InputViewer, AkronFeatureKind.InputViewer)) yield return "InputViewer";
        if (Effective(settings.RoomTimerWidget, AkronFeatureKind.RoomTimer)) yield return "RoomTimerWidget";
        if (Effective(settings.DeathStatsWidget, AkronFeatureKind.DeathStats)) yield return "DeathStatsWidget";
        if (Effective(settings.ReducedVisualNoise, AkronFeatureKind.ReducedVisualNoise)) yield return "ReducedVisualNoise";
        if (Effective(settings.NoParticles, AkronFeatureKind.ReducedVisualNoise)) yield return "NoParticles";
        if (Effective(settings.NoTrails, AkronFeatureKind.ReducedVisualNoise)) yield return "NoTrails";
        if (Effective(settings.NoGlitch, AkronFeatureKind.ReducedVisualNoise)) yield return "NoGlitch";
        if (Effective(settings.NoAnxiety, AkronFeatureKind.ReducedVisualNoise)) yield return "NoAnxiety";
        if (Effective(settings.NoDistortion, AkronFeatureKind.ReducedVisualNoise)) yield return "NoDistortion";
        if (Effective(settings.HideSnow, AkronFeatureKind.ReducedVisualNoise)) yield return "HideSnow";
        if (Effective(settings.HideWindSnow, AkronFeatureKind.ReducedVisualNoise)) yield return "HideWindSnow";
        if (Effective(settings.HideWaterfalls, AkronFeatureKind.ReducedVisualNoise)) yield return "HideWaterfalls";
        if (Effective(settings.HideTentacles, AkronFeatureKind.ReducedVisualNoise)) yield return "HideTentacles";
        if (Effective(settings.HideHeatDistortion, AkronFeatureKind.ReducedVisualNoise)) yield return "HideHeatDistortion";
        if (Effective(settings.HitboxViewer, AkronFeatureKind.HitboxViewer)) yield return "HitboxViewer";
        if (Effective(settings.EntityInspector, AkronFeatureKind.EntityInspector)) yield return "EntityInspector";
        if (Effective(settings.InfiniteStamina, AkronFeatureKind.InfiniteStamina)) yield return "InfiniteStamina";
        if (Effective(settings.InfiniteDash, AkronFeatureKind.InfiniteDash)) yield return "InfiniteDash";
        if (Effective(settings.Noclip, AkronFeatureKind.Noclip)) yield return "Noclip";
        if (Effective(settings.Invincibility, AkronFeatureKind.Invincibility)) yield return "Invincibility";
        if (Effective(session.FreezeGameplay, AkronFeatureKind.Freeze)) yield return "FreezeGameplay";
        if (Effective(session.TimescaleEnabled && session.TimescaleMultiplier != 1f, AkronFeatureKind.Timescale)) yield return "Timescale";
        if (Effective(settings.DisablePlayback, AkronFeatureKind.DisablePlayback)) yield return "DisablePlayback";
        if (Effective(settings.NoStaminaFlash, AkronFeatureKind.ReducedVisualNoise)) yield return "NoStaminaFlash";
        if (Effective(settings.JumpHack, AkronFeatureKind.MovementStatMutation)) yield return "AirJumps";
        if (Effective(settings.DashRedirectEnabled, AkronFeatureKind.InputAssistShortcut)) yield return "DashRedirect";
        if (Effective(settings.GrabModeOverrideEnabled, AkronFeatureKind.GrabModeHotkey)) yield return "GrabMode";
        if (Effective(settings.RespawnAtStartPos, AkronFeatureKind.StartPosTools)) yield return "StartPosRespawn";
        if (session.UsedBrokeredSavestate) yield return "BrokeredStartPosState";
        if (Effective(!string.IsNullOrWhiteSpace(settings.TasFilePath), AkronFeatureKind.TasHandoff)) yield return "TasHandoff";
        if (!string.IsNullOrWhiteSpace(session.LastScreenshotPath)) yield return "ScreenshotTool";
    }

    private static string BuildLoadedModuleStamp() {
        return string.Join("; ", Everest.Modules
            .Where(module => module?.Metadata != null && module.GetType().Name != "NullModule")
            .Select(module => module.Metadata.Name + "@" + module.Metadata.Version)
            .OrderBy(value => value));
    }
}
