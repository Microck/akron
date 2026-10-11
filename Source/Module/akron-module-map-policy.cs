using Monocle;

namespace Celeste.Mod.Akron;

public partial class AkronModule
{
    private static bool mapRestrictionEffectsPending;
    private static bool ownsTimescale;
    private static float timescaleBeforeAkron;
    private static float lastAkronTimescale;

    private static void EnterMapPolicy(Session session)
    {
        if (AkronPolicy.EnterMap(session?.MapData))
        {
            mapRestrictionEffectsPending = true;
        }
    }

    private static void LevelEnterOnConstructForMapPolicy(On.Celeste.LevelEnter.orig_ctor orig, LevelEnter self, Session session, bool fromSaveData)
    {
        EnterMapPolicy(session);
        ClearPendingPolicyActions();
        ApplyPendingMapPolicyEffects();
        orig(self, session, fromSaveData);
    }

    private static void EngineOnSceneTransitionForMapPolicy(On.Monocle.Engine.orig_OnSceneTransition orig, Engine self, Scene from, Scene to)
    {
        // Constructors also run for detached snapshot loaders. Adopt their policy only
        // when the engine installs them, after the outgoing scene ends and before Begin.
        if (to is LevelLoader loader)
        {
            EnterMapPolicy(loader.session);
        }
        else if (to is Level level)
        {
            EnterMapPolicy(level.Session);
        }
        else if (to is LevelEnter levelEnter)
        {
            EnterMapPolicy(levelEnter.session);
        }
        else
        {
            LeaveMapPolicy();
        }
        ApplyPendingMapPolicyEffects();
        orig(self, from, to);
    }

    private static void LevelOnLoadLevelForMapPolicy(On.Celeste.Level.orig_LoadLevel orig, Level self, Player.IntroTypes intro, bool isFromLoader)
    {
        // Only policy is scoped here. Global render/audio/recorder cleanup belongs to
        // the engine thread when Level.Begin installs the incoming scene's policy.
        AkronMapFeatureRestrictions previous = AkronPolicy.EnterMapLoadScope(self.Session.MapData);
        try
        {
            orig(self, intro, isFromLoader);
        }
        finally
        {
            AkronPolicy.ExitMapLoadScope(previous);
        }
    }

    private static void LeaveMapPolicy()
    {
        if (AkronPolicy.LeaveMap())
        {
            mapRestrictionEffectsPending = true;
        }
        ResetMapPolicyEffects();
    }

    private static void ResetMapPolicyEffects()
    {
        ClearPendingPolicyActions();
        ReleaseTimescale();
        AkronRuntimeOptions.Reset();
        AkronActions.RestoreLowVolumeBypass();
    }

    private static void ClearPendingPolicyActions()
    {
        AkronActions.CancelPendingStartPosActions();
        AkronScreenshotScanner.CancelForMapChange();
        AkronCapture.CancelPendingCapture();
        if (Session != null)
        {
            Session.StepFrameRequested = false;
            Session.StepFrameHoldFrames = 0;
            Session.StepFrameRepeatCountdown = 0;
            Session.LevelEnterSkipHoldSeconds = 0f;
        }
    }

    private static void ApplyPendingMapPolicyEffects()
    {
        if (!mapRestrictionEffectsPending)
        {
            return;
        }
        mapRestrictionEffectsPending = false;
        ClearPendingPolicyActions();
        AkronRuntimeOptions.Reset();
        if (AkronPolicy.IsMapRestricted(AkronFeatureKind.Invincibility))
        {
            RestoreNativeAssistInvincibility();
        }
        AkronActions.ApplyLowVolumeBypass();
        if (!AkronPolicy.CanUse(AkronFeatureKind.Timescale).Allowed)
        {
            ReleaseTimescale();
        }
        AkronInternalRecorder.ApplyMapRestrictions();
        AkronExtendedVariants.ApplyMapRestrictions();
        ApplyMotionSmoothingSettings();
    }

    internal static bool IsGameplayFreezeEffective(AkronModuleSession session) => session != null && session.FreezeGameplay &&
        AkronPolicy.CanUse(AkronFeatureKind.Freeze).Allowed;

    internal static bool CanStepGameplay(AkronModuleSession session, AkronModuleSettings settings) => IsGameplayFreezeEffective(session) && settings.FrameStepper &&
        AkronPolicy.CanUse(AkronFeatureKind.FrameAdvance).Allowed;

#pragma warning disable CS0618
    internal static bool OwnsCurrentTimescale => ownsTimescale && Engine.TimeRate == lastAkronTimescale;
#pragma warning restore CS0618
    internal static float TimescaleBeforeAkron => timescaleBeforeAkron;

    internal static void RestoreSnapshotTimescale(float value, bool ownedByAkron, float beforeAkron)
    {
        ReleaseTimescale();
        if (ownedByAkron && !AkronPolicy.CanUse(AkronFeatureKind.Timescale).Allowed)
        {
            return;
        }
#pragma warning disable CS0618
        Engine.TimeRate = value;
#pragma warning restore CS0618
        ownsTimescale = ownedByAkron;
        timescaleBeforeAkron = beforeAkron;
        lastAkronTimescale = value;
    }

    internal static void ApplyTimescale(AkronModuleSession session)
    {
        if (session == null || !session.TimescaleEnabled || !AkronPolicy.CanUse(AkronFeatureKind.Timescale).Allowed)
        {
            ReleaseTimescale();
            return;
        }
#pragma warning disable CS0618
        if (!ownsTimescale)
        {
            timescaleBeforeAkron = Engine.TimeRate;
            ownsTimescale = true;
        }
        lastAkronTimescale = session.TimescaleMultiplier;
        Engine.TimeRate = lastAkronTimescale;
#pragma warning restore CS0618
    }

    internal static void ReleaseTimescale()
    {
        if (!ownsTimescale)
        {
            return;
        }
#pragma warning disable CS0618
        // Once another mod has written the clock, it owns that value. In particular a
        // restricted map may provide its own trainer; never keep resetting its clock.
        if (Engine.TimeRate == lastAkronTimescale)
        {
            Engine.TimeRate = timescaleBeforeAkron;
        }
#pragma warning restore CS0618
        ownsTimescale = false;
    }
}
