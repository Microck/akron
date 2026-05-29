namespace Celeste.Mod.Akron;

public partial class AkronModuleSettings {
    private void SaveProfileState(AkronProfile profile) {
        AkronProfileState snapshot = CaptureCurrentState();
        switch (profile) {
            case AkronProfile.Practice:
                PracticeProfileState = snapshot;
                break;
            case AkronProfile.LeaderboardClean:
                LeaderboardCleanProfileState = snapshot;
                break;
            case AkronProfile.Sandbox:
                SandboxProfileState = snapshot;
                break;
            case AkronProfile.MapMaker:
                MapMakerProfileState = snapshot;
                break;
            case AkronProfile.Accessibility:
                AccessibilityProfileState = snapshot;
                break;
            default:
                CasualProfileState = snapshot;
                break;
        }
    }

    private AkronProfileState GetProfileState(AkronProfile profile) {
        AkronProfileState state = profile switch {
            AkronProfile.Practice => PracticeProfileState,
            AkronProfile.LeaderboardClean => LeaderboardCleanProfileState,
            AkronProfile.Sandbox => SandboxProfileState,
            AkronProfile.MapMaker => MapMakerProfileState,
            AkronProfile.Accessibility => AccessibilityProfileState,
            _ => CasualProfileState
        };

        if (state != null) {
            return state;
        }

        AkronProfileState builtIn = BuildProfileState(profile);
        switch (profile) {
            case AkronProfile.Practice:
                PracticeProfileState = builtIn;
                break;
            case AkronProfile.LeaderboardClean:
                LeaderboardCleanProfileState = builtIn;
                break;
            case AkronProfile.Sandbox:
                SandboxProfileState = builtIn;
                break;
            case AkronProfile.MapMaker:
                MapMakerProfileState = builtIn;
                break;
            case AkronProfile.Accessibility:
                AccessibilityProfileState = builtIn;
                break;
            default:
                CasualProfileState = builtIn;
                break;
        }

        return builtIn;
    }

    private static AkronProfileState BuildRulesetState(PrimaryRuleset ruleset) {
        AkronProfileState state = new AkronProfileState {
            PrimaryRuleset = ruleset,
            SafeMode = true,
            RoomLabels = true,
            EverestSafeAutoBlock = true
        };

        switch (ruleset) {
            case PrimaryRuleset.Practice:
                state.StaminaWidget = true;
                state.SpeedWidget = true;
                state.DashWidget = true;
                state.InputViewer = true;
                state.RoomTimerWidget = true;
                state.DeathStatsWidget = true;
                break;
            case PrimaryRuleset.LeaderboardClean:
                break;
            case PrimaryRuleset.Sandbox:
                state.SafeMode = false;
                break;
            case PrimaryRuleset.EverestSafe:
                state.UnsafeSavestateOverride = false;
                break;
            case PrimaryRuleset.MapMaker:
                state.InputViewer = true;
                state.ReducedVisualNoise = true;
                state.NoParticles = true;
                state.HitboxViewer = true;
                state.EntityInspector = true;
                break;
        }

        return state;
    }

    private static AkronProfileState BuildProfileState(AkronProfile profile) {
        AkronProfileState state = profile switch {
            AkronProfile.Practice => BuildRulesetState(PrimaryRuleset.Practice),
            AkronProfile.LeaderboardClean => BuildRulesetState(PrimaryRuleset.LeaderboardClean),
            AkronProfile.Sandbox => BuildRulesetState(PrimaryRuleset.Sandbox),
            AkronProfile.MapMaker => BuildRulesetState(PrimaryRuleset.MapMaker),
            AkronProfile.Accessibility => BuildRulesetState(PrimaryRuleset.Casual),
            _ => BuildRulesetState(PrimaryRuleset.Casual)
        };

        switch (profile) {
            case AkronProfile.Practice:
                break;
            case AkronProfile.MapMaker:
                state.RoomTimerWidget = true;
                break;
            case AkronProfile.Accessibility:
                state.StaminaWidget = true;
                state.SpeedWidget = true;
                state.DashWidget = true;
                state.InputViewer = true;
                state.StaminaBar = true;
                state.DashBar = true;
                state.SetLowDistractionChannels(true);
                break;
        }

        return state;
    }
}
