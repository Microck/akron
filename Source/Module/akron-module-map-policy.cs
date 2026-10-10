using Monocle;

namespace Celeste.Mod.Akron;

public partial class AkronModule
{
    private static bool mapRestrictionEffectsPending;
    private static Session mapPolicySession;
    private static bool ownsTimescale;
    private static float timescaleBeforeAkron;
    private static float lastAkronTimescale;

    private static void EnterMapPolicy(Session session)
    {
        mapPolicySession = session;
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

    private static void LeaveMapPolicy(Session expectedSession = null)
    {
        // LevelEnter/LevelLoader can install the next session before the old Level.End.
        bool ownsPolicy = expectedSession == null || ReferenceEquals(mapPolicySession, expectedSession);
        if (ownsPolicy)
        {
            mapPolicySession = null;
        }
        if (ownsPolicy && AkronPolicy.LeaveMap())
        {
            mapRestrictionEffectsPending = true;
        }
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

    internal static bool IsGameplayFreezeEffective => Session != null && Session.FreezeGameplay &&
        AkronPolicy.CanUse(AkronFeatureKind.Freeze).Allowed;

    internal static bool CanStepGameplay => IsGameplayFreezeEffective && Settings.FrameStepper &&
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

    internal static void ApplyTimescale()
    {
        if (Session == null || !Session.TimescaleEnabled || !AkronPolicy.CanUse(AkronFeatureKind.Timescale).Allowed)
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
        lastAkronTimescale = Session.TimescaleMultiplier;
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
