using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Celeste;
using Celeste.Editor;
using Celeste.Mod;
using FMOD.Studio;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mono.Cecil;
using MonoMod.ModInterop;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using Monocle;

namespace Celeste.Mod.Akron;

public partial class AkronModule : EverestModule {
    public static AkronModule Instance { get; private set; }

    public override Type SettingsType => typeof(AkronModuleSettings);
    public static AkronModuleSettings Settings => (AkronModuleSettings) Instance._Settings;
    internal static AkronModuleSettings TryGetSettings() {
        return Instance?._Settings as AkronModuleSettings;
    }

    public override Type SessionType => typeof(AkronModuleSession);
    public static AkronModuleSession Session => (AkronModuleSession) Instance._Session;
    internal static AkronModuleSession TryGetSession() {
        return Instance?._Session as AkronModuleSession;
    }

    public override Type SaveDataType => typeof(AkronModuleSaveData);
    public static AkronModuleSaveData SaveData => (AkronModuleSaveData) Instance._SaveData;
    internal static bool IsOverlayVisible => Overlay?.Visible == true;
    internal static bool IsOverlayBindingCaptureActive => Overlay?.HasActiveBindingCapture == true;
    internal static bool IsOverlayKeyboardInputOwned => Overlay?.SearchOwnsCurrentKeyboardFrame == true;

    internal static bool EndStartPosPlacementForLoad() {
        return Overlay?.EndStartPosPlacementForLoad() == true;
    }

    internal static bool TryGetPracticeAreaSelectionPreview(Level level, bool isAutoDeafen, out Rectangle area, out bool hasAnchor) {
        area = Rectangle.Empty;
        hasAnchor = false;
        return Overlay?.TryGetPracticeAreaSelectionPreview(level, isAutoDeafen, out area, out hasAnchor) == true;
    }

    private static AkronOverlay Overlay;
    private static bool nativeAssistInvincibilityCaptured;
    private static bool previousAssistMode;
    private static bool previousAssistInvincible;
    private static Player noclipDepthPlayer;
    private static int previousNoclipDepth;
    private static Player noclipVisibilityPlayer;
    private static bool previousNoclipVisible;
    private static int noclipAccuracySamples;
    private static int noclipAccuracyInvalidSamples;
    private static int noclipAccuracyInvalidEntries;
    private static bool noclipAccuracyInvalidLastFrame;
    private static bool noclipAccuracyLimitToastShown;
    private static float noclipAccuracyTintTimer;
    private static bool hazardAccuracyHasLastPosition;
    private static Vector2 hazardAccuracyLastPosition;
    private static int forcedTrailFrame;
    private static bool clickTeleportLastLeftDown;
    private static Vector2? pendingClickTeleportTarget;
    private static int cursorZoomLastScrollValue;
    private static bool cursorZoomHadScrollSample;
    private static Vector2 cursorZoomFocusGamePosition;
    private static bool cursorZoomApplied;
    private static bool cursorZoomOwnedByExtendedCamera;
    private static bool cursorZoomToggleActive;
    private static bool cursorZoomLastBindDown;
    private static bool pauseTimerFreezeStoppedTimer;
    private static bool captureSuppressionHooksInstalled;
    private static int fastLookoutPatchedConstantCount;
    private static bool startPosPlacementLastLeftDown;
    private static int jumpHackAirJumpsUsed;
    private static readonly Dictionary<PlayerDeadBody, float> respawnTimeElapsed = new Dictionary<PlayerDeadBody, float>();
    private static readonly HashSet<PlayerDeadBody> noDeathEffectBodies = new HashSet<PlayerDeadBody>();
    private static bool renderCoreDiagnosticLogged;
    private static ulong renderedStartPosFrameGeneration;
    private static int freshRoomInitializationUpdateDepth;
    private static readonly Queue<Action> afterEngineUpdateActions = new Queue<Action>();
    private static readonly MethodInfo CreateKeyboardConfigUiMethod =
        typeof(EverestModule).GetMethod("CreateKeyboardConfigUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly MethodInfo CreateButtonConfigUiMethod =
        typeof(EverestModule).GetMethod("CreateButtonConfigUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly MethodInfo PlayerDeadBodyEndMethod =
        typeof(PlayerDeadBody).GetMethod("End", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo PlayerDeadBodyDeathEffectField =
        typeof(PlayerDeadBody).GetField("deathEffect", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo PlayerSuperJumpMethod =
        typeof(Player).GetMethod("SuperJump", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo PlayerJumpGraceTimerField =
        typeof(Player).GetField("jumpGraceTimer", BindingFlags.Instance | BindingFlags.NonPublic);
    // Everest saves module settings from its own background thread as well as from Akron's own
    // callers, so two writers can reach SaveSettings at once and would otherwise share one
    // temporary file.
    private static readonly object SettingsFileSync = new object();
    private static MethodInfo playerDashCoroutineMethod;
    private static ILHook dashCoroutineHook;
    private static MethodInfo lookoutLookRoutineMethod;
    private static ILHook lookoutRoutineHook;
    private static readonly FieldInfo LevelEnterSessionField =
        typeof(LevelEnter).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic);
    private static Vector2 preRedirectDashAim;
    private static readonly ConditionalWeakTable<Refill, RefillClaritySpriteState> RefillClaritySpriteStates =
        new ConditionalWeakTable<Refill, RefillClaritySpriteState>();
    private static readonly Dictionary<RefillClaritySourceCacheKey, RefillClaritySourceFrame[]> RefillClaritySourceFrameCache =
        new Dictionary<RefillClaritySourceCacheKey, RefillClaritySourceFrame[]>();
    private static readonly Dictionary<RefillClarityFrameCacheKey, MTexture[]> RefillClarityFrameCache =
        new Dictionary<RefillClarityFrameCacheKey, MTexture[]>();
    private static readonly List<VirtualTexture> RefillClarityFrameTextures = new List<VirtualTexture>();

    public AkronModule() {
        Instance = this;
        Logger.SetLogLevel(nameof(AkronModule), LogLevel.Info);
    }

    public override void Load() {
        renderedStartPosFrameGeneration = AkronActions.StartPosFrameGeneration;
        AkronModuleSettings.EnsureCurrentKeybindDefaults(Settings);
        AkronModuleSettings.DropUnkeyedAutomationAreas(Settings);
        AkronLog.Normal(nameof(AkronModule), "load start; " + AkronLog.DescribeSettings());
        AkronAudioSplitter.Load();
        try {
            AkronImGuiRenderer.EnsureNativeResolverRegistered();
            typeof(AkronSaveLoadExports).ModInterop();
            AkronSpeedrunToolBroker.Initialize();
            AkronInterop.Initialize();
            AkronNativeSavestateSupport.Initialize();
            AkronStartPosPersistence.Start();
            AkronScreenshotScanner.Load();
        } catch (Exception exception) {
            Logger.Log(LogLevel.Error, nameof(AkronModule), "Akron startup helper initialization failed; continuing so the module menu and overlay can still load: " + exception);
        }
        if (Engine.Instance != null) {
            // Everest does not unload modules during every normal game exit.
            // Drain restart copies while the game and save APIs are still alive.
            Engine.Instance.Exiting += EngineOnExiting;
        }
        On.Celeste.Level.Begin += LevelOnBegin;
        On.Celeste.Level.End += LevelOnEnd;
        On.Celeste.Level.UpdateTime += LevelOnUpdateTime;
        On.Celeste.Level.Update += LevelOnUpdate;
        On.Celeste.Level.BeforeRender += LevelOnBeforeRender;
        IL.Celeste.Level.Render += LevelOnRenderForStartPosPresentation;
        AkronEngineGarbageCollection.Load();
        On.Celeste.GameplayRenderer.Render += GameplayRendererOnRender;
        On.Celeste.HudRenderer.RenderContent += HudRendererOnRenderContent;
        On.Celeste.TalkComponent.TalkComponentUI.Render += TalkComponentUiOnRender;
        On.Celeste.MiniTextbox.Render += MiniTextboxOnRender;
        On.Celeste.BackdropRenderer.Render += BackdropRendererOnRender;
        On.Celeste.WaterFall.Render += WaterFallOnRender;
        On.Celeste.WaterFall.RenderDisplacement += WaterFallOnRenderDisplacement;
        On.Celeste.WaterFall.Update += WaterFallOnUpdate;
        On.Celeste.BigWaterfall.Render += BigWaterfallOnRender;
        On.Celeste.BigWaterfall.RenderDisplacement += BigWaterfallOnRenderDisplacement;
        On.Celeste.ReflectionTentacles.Render += ReflectionTentaclesOnRender;
        On.Celeste.PlayerPlayback.Update += PlayerPlaybackOnUpdate;
        On.Celeste.HeatWave.RenderDisplacement += HeatWaveOnRenderDisplacement;
        On.Celeste.AreaData.DoScreenWipe += AreaDataOnDoScreenWipe;
        On.Celeste.ScreenWipe.DrawPrimitives += ScreenWipeOnDrawPrimitives;
        On.Celeste.TextMenu.Update += TextMenuOnUpdate;
        On.Celeste.TextMenu.Button.ConfirmPressed += TextMenuButtonOnConfirmPressed;
        On.Celeste.AutoSavingNotice.Update += AutoSavingNoticeOnUpdate;
        On.Celeste.AutoSavingNotice.Render += AutoSavingNoticeOnRender;
        On.Celeste.SaveLoadIcon.Show += SaveLoadIconOnShow;
        On.Celeste.SaveLoadIcon.Render += SaveLoadIconOnRender;
        On.Celeste.Refill.ctor_Vector2_bool_bool += RefillOnCtor;
        On.Monocle.Commands.UpdateClosed += AkronOverlay.CommandsOnUpdateClosed;
        On.Monocle.Engine.Update += EngineOnUpdate;
        On.Monocle.Engine.RenderCore += EngineOnRenderCore;
        On.Celeste.Level.CompleteArea_bool_bool += LevelOnCompleteArea;
        On.Celeste.Level.CompleteArea_bool_bool_bool += LevelOnCompleteArea;
        On.Celeste.Level.RegisterAreaComplete += LevelOnRegisterAreaComplete;
        On.Celeste.PlayerCollider.Check += PlayerColliderOnCheckForDeathHazard;
        On.Celeste.Player.Die += PlayerOnDie;
        On.Celeste.Player.OnSquish += PlayerOnSquish;
        On.Celeste.Player.DashBegin += PlayerOnDashBegin;
        On.Celeste.Player.Render += PlayerOnRender;
        On.Celeste.Player.Update += PlayerOnUpdate;
        On.Celeste.Player.Jump += PlayerOnJump;
        On.Celeste.Player.SuperJump += PlayerOnSuperJump;
        On.Celeste.Player.UpdateHair += PlayerOnUpdateHair;
        On.Celeste.Player.RefillDash += PlayerOnRefillDash;
        On.Celeste.Player.Added += PlayerOnAdded;
        On.Celeste.Player.OnTransition += PlayerOnTransition;
        On.Celeste.PlayerDeadBody.Update += PlayerDeadBodyOnUpdate;
        On.Celeste.DeathEffect.Render += DeathEffectOnRender;
        On.Celeste.DeathEffect.Draw += DeathEffectOnDraw;
        On.Celeste.RisingLava.OnPlayer += RisingLavaOnPlayer;
        On.Celeste.SandwichLava.OnPlayer += SandwichLavaOnPlayer;
        On.Celeste.Strawberry.OnCollect += StrawberryOnCollect;
        On.Celeste.Celeste.Freeze += CelesteOnFreeze;
        On.Celeste.UserIO.SaveHandler += UserIOOnSaveHandler;
        On.FMOD.Studio.EventDescription.createInstance += EventDescriptionOnCreateInstance;
        Everest.Events.Level.OnPause += LevelOnPause;
        Everest.Events.Level.OnUnpause += LevelOnUnpause;
        Everest.Events.Level.OnExit += LevelOnExit;
        AkronEntityInspector.LoadInspectorPin();
        MethodInfo dashCoroutineMethod = ResolvePlayerDashCoroutineMethod();
        if (dashCoroutineMethod != null) {
            dashCoroutineHook = new ILHook(dashCoroutineMethod, PlayerDashCoroutineIlHook);
        }
        MethodInfo lookoutRoutineMethod = ResolveLookoutRoutineMethod();
        if (lookoutRoutineMethod != null) {
            lookoutRoutineHook = new ILHook(lookoutRoutineMethod, LookoutRoutineIlHook);
        }
        AkronBackupActions.NotifyStartupReady();
    }

    private static MethodInfo ResolvePlayerDashCoroutineMethod() {
        if (playerDashCoroutineMethod != null) {
            return playerDashCoroutineMethod;
        }

        try {
            playerDashCoroutineMethod = typeof(Player)
                .GetMethod("DashCoroutine", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetStateMachineTarget();
        } catch (Exception exception) {
            Logger.Log(LogLevel.Warn, nameof(AkronModule), "Failed to resolve Player.DashCoroutine hook target: " + exception);
        }

        return playerDashCoroutineMethod;
    }

    private static MethodInfo ResolveLookoutRoutineMethod() {
        if (lookoutLookRoutineMethod != null) {
            return lookoutLookRoutineMethod;
        }

        try {
            lookoutLookRoutineMethod = typeof(Lookout)
                .GetMethod("LookRoutine", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetStateMachineTarget();
        } catch (Exception exception) {
            Logger.Log(LogLevel.Warn, nameof(AkronModule), "Failed to resolve Lookout.LookRoutine hook target: " + exception);
        }

        return lookoutLookRoutineMethod;
    }

    public override void Initialize() {
        try {
            AkronMotionSmoothingInterop.RefreshLoadedState();
            AkronMotionSmoothingInterop.ApplyAkronSettings();
            AkronAudioSplitter.Initialize();
        } catch (Exception exception) {
            Logger.Log(LogLevel.Error, nameof(AkronModule), "Akron startup helper initialization failed during Initialize; continuing so the module menu and overlay can still load: " + exception);
        }
    }

    public override void LoadContent(bool firstLoad) {
        try {
            AkronImGuiRenderer.WarmUp();
        } catch (Exception exception) {
            Logger.Log(LogLevel.Error, nameof(AkronModule), "Akron startup helper initialization failed during LoadContent; continuing so the module menu and overlay can still load: " + exception);
        }
    }

    public override void Unload() {
        if (Engine.Instance != null) {
            Engine.Instance.Exiting -= EngineOnExiting;
        }
        AkronGameplayBufferState.ResetLevelPresentation();
        AkronAudioSplitter.Unload();
        AkronStartPosPersistence.Shutdown();
        AkronActions.ClearPendingStartPosState();
        AkronActions.ClearStartPosInputWait();
        SaveAkronSettingsNow("unload");
        AkronLog.FlushDiagnosticSummaries();
        AkronLog.Normal(nameof(AkronModule), "unload start");
        AkronBackupActions.NotifyShutdown();
        AkronInterop.UnregisterSpeedrunToolSaveLoadHooks();
        AkronScreenshotScanner.Unload();
        AkronNativeSavestateSupport.Reset();
        AkronSaveLoadService.ClearRuntimeState();
        On.Celeste.Level.Begin -= LevelOnBegin;
        On.Celeste.Level.End -= LevelOnEnd;
        On.Celeste.Level.UpdateTime -= LevelOnUpdateTime;
        On.Celeste.Level.Update -= LevelOnUpdate;
        On.Celeste.Level.BeforeRender -= LevelOnBeforeRender;
        IL.Celeste.Level.Render -= LevelOnRenderForStartPosPresentation;
        AkronEngineGarbageCollection.Unload();
        On.Celeste.GameplayRenderer.Render -= GameplayRendererOnRender;
        On.Celeste.HudRenderer.RenderContent -= HudRendererOnRenderContent;
        On.Celeste.TalkComponent.TalkComponentUI.Render -= TalkComponentUiOnRender;
        On.Celeste.MiniTextbox.Render -= MiniTextboxOnRender;
        if (captureSuppressionHooksInstalled) {
            On.Celeste.SpeedrunTimerDisplay.Render -= SpeedrunTimerDisplayOnRender;
            On.Celeste.SpeedrunTimerDisplay.DrawTime -= SpeedrunTimerDisplayOnDrawTime;
            captureSuppressionHooksInstalled = false;
        }
        On.Celeste.BackdropRenderer.Render -= BackdropRendererOnRender;
        On.Celeste.WaterFall.Render -= WaterFallOnRender;
        On.Celeste.WaterFall.RenderDisplacement -= WaterFallOnRenderDisplacement;
        On.Celeste.WaterFall.Update -= WaterFallOnUpdate;
        On.Celeste.BigWaterfall.Render -= BigWaterfallOnRender;
        On.Celeste.BigWaterfall.RenderDisplacement -= BigWaterfallOnRenderDisplacement;
        On.Celeste.ReflectionTentacles.Render -= ReflectionTentaclesOnRender;
        On.Celeste.PlayerPlayback.Update -= PlayerPlaybackOnUpdate;
        On.Celeste.HeatWave.RenderDisplacement -= HeatWaveOnRenderDisplacement;
        On.Celeste.AreaData.DoScreenWipe -= AreaDataOnDoScreenWipe;
        On.Celeste.ScreenWipe.DrawPrimitives -= ScreenWipeOnDrawPrimitives;
        On.Celeste.TextMenu.Update -= TextMenuOnUpdate;
        On.Celeste.TextMenu.Button.ConfirmPressed -= TextMenuButtonOnConfirmPressed;
        On.Celeste.AutoSavingNotice.Update -= AutoSavingNoticeOnUpdate;
        On.Celeste.AutoSavingNotice.Render -= AutoSavingNoticeOnRender;
        On.Celeste.SaveLoadIcon.Show -= SaveLoadIconOnShow;
        On.Celeste.SaveLoadIcon.Render -= SaveLoadIconOnRender;
        On.Celeste.Refill.ctor_Vector2_bool_bool -= RefillOnCtor;
        On.Monocle.Commands.UpdateClosed -= AkronOverlay.CommandsOnUpdateClosed;
        On.Monocle.Engine.Update -= EngineOnUpdate;
        On.Monocle.Engine.RenderCore -= EngineOnRenderCore;
        On.Celeste.Level.CompleteArea_bool_bool -= LevelOnCompleteArea;
        On.Celeste.Level.CompleteArea_bool_bool_bool -= LevelOnCompleteArea;
        On.Celeste.Level.RegisterAreaComplete -= LevelOnRegisterAreaComplete;
        On.Celeste.PlayerCollider.Check -= PlayerColliderOnCheckForDeathHazard;
        On.Celeste.Player.Die -= PlayerOnDie;
        On.Celeste.Player.OnSquish -= PlayerOnSquish;
        On.Celeste.Player.DashBegin -= PlayerOnDashBegin;
        On.Celeste.Player.Render -= PlayerOnRender;
        On.Celeste.Player.Update -= PlayerOnUpdate;
        On.Celeste.Player.Jump -= PlayerOnJump;
        On.Celeste.Player.SuperJump -= PlayerOnSuperJump;
        On.Celeste.Player.UpdateHair -= PlayerOnUpdateHair;
        On.Celeste.Player.RefillDash -= PlayerOnRefillDash;
        On.Celeste.Player.Added -= PlayerOnAdded;
        On.Celeste.Player.OnTransition -= PlayerOnTransition;
        On.Celeste.PlayerDeadBody.Update -= PlayerDeadBodyOnUpdate;
        On.Celeste.DeathEffect.Render -= DeathEffectOnRender;
        On.Celeste.DeathEffect.Draw -= DeathEffectOnDraw;
        On.Celeste.RisingLava.OnPlayer -= RisingLavaOnPlayer;
        On.Celeste.SandwichLava.OnPlayer -= SandwichLavaOnPlayer;
        On.Celeste.Strawberry.OnCollect -= StrawberryOnCollect;
        On.Celeste.Celeste.Freeze -= CelesteOnFreeze;
        On.Celeste.UserIO.SaveHandler -= UserIOOnSaveHandler;
        On.FMOD.Studio.EventDescription.createInstance -= EventDescriptionOnCreateInstance;
        Everest.Events.Level.OnPause -= LevelOnPause;
        Everest.Events.Level.OnUnpause -= LevelOnUnpause;
        Everest.Events.Level.OnExit -= LevelOnExit;
        AkronEntityInspector.UnloadInspectorPin();
        dashCoroutineHook?.Dispose();
        dashCoroutineHook = null;
        lookoutRoutineHook?.Dispose();
        lookoutRoutineHook = null;
        respawnTimeElapsed.Clear();
        noDeathEffectBodies.Clear();
        afterEngineUpdateActions.Clear();
        RestoreCursorVisibility();
        RestoreNativeAssistInvincibility();
        RestoreNoclipDepth();
        RestorePlayerVisibilityOverride();
        ClearRefillClarityFrameCache();
        ResetNoclipAccuracy();
        AkronActions.RestoreAutoDeafen();
        AkronActions.RestoreLowVolumeBypass();
        AkronRuntimeOptions.Reset();
        AkronOverlayBlur.Unload();
        deferredScreenWipeAction = null;
        ClearDeathWipeRenderSuppression();
        if (AkronInternalRecorder.IsRecording) {
            AkronInternalRecorder.Stop();
        }
#pragma warning disable CS0618
        Engine.TimeRate = 1f;
#pragma warning restore CS0618
        // AkronLog holds the log file open for the whole session, so unload has to release the handle.
        // Reloading the mod would otherwise leave a second appender on the same file.
        AkronLog.CloseLogFile();
    }

    private static void EngineOnExiting(object sender, EventArgs eventArgs) {
        AkronStartPosPersistence.Shutdown();
        AkronActions.ClearPendingStartPosState();
        // Unload does not run on a normal quit, and an FFmpeg process that is killed with the
        // game leaves a file with no index, no remux and no audio. Finalize it first.
        if (AkronInternalRecorder.IsRecording) {
            AkronInternalRecorder.Stop();
        }
    }

    internal static void ApplyMotionSmoothingSettings() {
        if (Instance?._Settings == null || Engine.Instance == null) {
            return;
        }

        AkronMotionSmoothingInterop.ApplyAkronSettings();
    }

    private static void LevelOnBegin(On.Celeste.Level.orig_Begin orig, Level self) {
        AkronActions.ClearStartPosInputWait();
        try {
            orig(self);
        } catch (NullReferenceException ex) when (ex.StackTrace?.IndexOf("DustEdges.BeforeRender", StringComparison.Ordinal) >= 0) {
            AkronSaveLoadService.RemoveClonedDustEdges(self);
            orig(self);
        }
        AkronInterop.EnsureSpeedrunToolTabDoesNotStealAkronOverlayBinding();
        AkronInterop.EnsureSpeedrunToolSaveLoadHooksRegistered();
        SuppressAkronRenderSurfacesAfterStateTransition();
        Session.CurrentSessionNonce = Guid.NewGuid().ToString("N");
        AkronPolicy.ResetAttempt("New level entry started a clean Akron attempt.");
        Session.DeathsSinceLevelLoad = 0;
        Session.DeathsSinceRoomTransition = 0;
        Session.DeathStatsAfterDeathTimer = 0f;
        ResetProofRuntimeTelemetry();
        proofRecorderGuardWarningShown = false;
        AkronEntityInspector.ClearLastDeathHitbox();
        noDeathEffectBodies.Clear();
        ResetNoclipAccuracy();
        AkronActions.RestoreAutoDeafen();
        AkronInputHistory.ResetInputsPerSecond();
        AkronActions.LoadStartPositionsForLevel(self);
        AkronSaveLoadService.OnLevelBegin(self);
        AkronStartPosPersistence.NotifyLevelReady(self);
        AkronPracticeStats.OnLevelBegin(self);
        AkronPracticeCounters.OnLevelBegin(self);
        AkronAutosave.NotifyLevelBegin(self);
        AkronBackupActions.NotifyLevelBegin(self);
        AkronInternalRecorder.NotifyLevelBegin(self);
        AkronActions.ApplyCameraOffset(self);
        EnsureOverlay(self);
        Overlay?.PrewarmLayout(self);
    }

    private static void LevelOnEnd(On.Celeste.Level.orig_End orig, Level self) {
        AkronGameplayBufferState.ResetLevelPresentation();
        AkronActions.ClearStartPosInputWait();
        // Both of these reach outside the level: Discord stays deafened through the
        // overworld, and the Assist flags would ride into the save file. A restart re-applies
        // them from the first frame of the next level, so restoring here costs nothing.
        RestoreNativeAssistInvincibility();
        AkronActions.RestoreAutoDeafen();
        orig(self);
    }

    private static void LevelOnExit(Level level, LevelExit levelExit, LevelExit.Mode mode, Session session, HiresSnow hiresSnow) {
        AkronInternalRecorder.NotifyLevelExit(mode);
    }

    private static void LevelOnUpdate(On.Celeste.Level.orig_Update orig, Level self) {
        if (freshRoomInitializationUpdateDepth > 0) {
            orig(self);
            return;
        }
        if (AkronStartPosPersistence.ConsumeFreshBaselineInitializationUpdate(self)) {
            // Give each normal room load exactly one Celeste initialization update.
            orig(self);
            return;
        }
        if (AkronStartPosPersistence.IsFreshBaselineCapturePending(self)) {
            // Engine.Update can run several fixed updates before one render. Hold
            // every update after room initialization until the stable-boundary
            // capture runs, or the disk baseline would depend on frame catch-up.
            AkronRuntimeOptions.HoldSceneClockForSkippedLevelUpdate(self);
            return;
        }
        ulong startPosFrameGeneration = AkronActions.StartPosFrameGeneration;
        if (startPosFrameGeneration != renderedStartPosFrameGeneration) {
            // A fixed-timestep game loop can run more than one update before a
            // render. Keep the saved frame unchanged until it is actually drawn.
            AkronRuntimeOptions.HoldSceneClockForSkippedLevelUpdate(self);
            return;
        }
        RunDeferredScreenWipeAction();
        if (AkronActions.StartPosFrameGeneration != startPosFrameGeneration) {
            return;
        }
        UpdateDeathWipeRenderSuppression();
        UpdateStateTransitionRenderSuppression();
        EnsureOverlay(self);
        AkronScreenshotScanner.MaintainActiveScanHost(self);
        AkronAutomationService.ProcessPendingCommands(self);
        if (AkronActions.StartPosFrameGeneration != startPosFrameGeneration) {
            return;
        }
#if DEBUG
        StressUpdate(self);
#endif
        HandleHotkeys(self);
        if (AkronActions.StartPosFrameGeneration != startPosFrameGeneration) {
            return;
        }
        if (Settings.InputViewer || Settings.InputHistoryPanel || Settings.InputHistoryShowOnDeath || Settings.ShowTaps) {
            AkronInputHistory.RecordFrame();
        }
        if (Settings.InputsPerSecondCounter || Settings.CustomHudLabels) {
            AkronInputHistory.RecordInputsPerSecondFrame();
        }
        UpdateDeathStatsTimer();
        AkronEntityInspector.UpdateInspectorPin(self);

        bool overlayUpdated = false;
        if (Overlay?.Visible == true || Overlay?.IsTransientMouseUiActive == true || Settings.StartPosMousePlacement) {
            UpdateOverlayCursorState();
            Overlay.Active = false;
            Overlay.Update();
            overlayUpdated = true;
            UpdateOverlayCursorState();
            if (AkronActions.StartPosFrameGeneration != startPosFrameGeneration) {
                return;
            }
            if (Overlay.SearchOwnsGameplayInputThisFrame) {
                AkronRuntimeOptions.HoldSceneClockForSkippedLevelUpdate(self);
                return;
            }
            if (Settings.PauseGameplayInMenu) {
                AkronRuntimeOptions.HoldSceneClockForSkippedLevelUpdate(self);
                return;
            }
        } else {
            UpdateOverlayCursorState();
        }

        if (AkronActions.UpdateStartPosInputWait(self)) {
            return;
        }

        RefreshRefillClaritySprites(self);
        ApplyEnabledRuntimeFeatures(self);
        UpdateLagPauser(self);
        UpdateGoldenTransparency(self);
        AkronAutosave.Update(self);
        AkronActions.ApplyLowVolumeBypass();
        UpdateNoclipAccuracyTintTimer();
        if (UpdatePauseCountdown(self)) {
            if (!overlayUpdated) {
                Overlay?.Update();
            }
            return;
        }
        if (AkronRuntimeOptions.ShouldFreezeGameplayForFreeCamera(self)) {
            AkronRuntimeOptions.HoldSceneClockForSkippedLevelUpdate(self);
            if (!overlayUpdated) {
                Overlay?.Update();
            }
            return;
        }

        if (Session.FreezeGameplay && !Session.StepFrameRequested) {
            AkronRuntimeOptions.HoldSceneClockForSkippedLevelUpdate(self);
            if (!overlayUpdated) {
                Overlay?.Update();
            }
            return;
        }
        Session.StepFrameRequested = false;
        // MInput.Disabled makes every virtual input read as released for this update, so the
        // level still runs (timers, entities) while Madeline gets no input behind the open
        // overlay. The overlay itself read the keyboard and mouse directly before this point.
        bool blockGameplayInput = overlayUpdated && Overlay?.Visible == true && Settings.ConsumeGameplayInputInMenu;
        bool previousInputDisabled = MInput.Disabled;
        if (blockGameplayInput) {
            MInput.Disabled = true;
        }
        try {
            orig(self);
        } finally {
            if (blockGameplayInput) {
                MInput.Disabled = previousInputDisabled;
            }
        }
        RememberNativeFreezeFrameForLagPauser();
        AkronRuntimeOptions.ApplyScreenshakeAfterLevelUpdate(self);
        ApplyJumpHackAfterPlayerUpdate(self);
        ClearLastDeathHitboxAfterRespawn(self);
        AkronPracticeStats.OnLevelUpdate(self);
        UpdateProofRecorderGuard(self);
    }

    internal static void RunFreshRoomInitializationUpdate(Level level) {
        if (level == null) {
            return;
        }

        freshRoomInitializationUpdateDepth++;
        try {
            // Match the one full Scene update used before normal baseline capture.
            // BeforeUpdate installs queued room objects, while AfterUpdate runs the
            // callbacks that the reconstruction graph must also see after restart.
            level.BeforeUpdate();
            level.Update();
            level.AfterUpdate();
        } finally {
            freshRoomInitializationUpdateDepth--;
        }
    }


    private static void UpdateLevelEnterSkip(LevelEnter self) {
        if (self == null || Engine.Scene != self || Session == null) {
            return;
        }

        Session levelEnterSession = LevelEnterSessionField?.GetValue(self) as Session;
        bool postcardSkipAvailable = Settings.SkipPostcards && self.Tracker.GetEntity<Postcard>() != null;
        bool introSkipAvailable = Settings.SkipIntro &&
                                  levelEnterSession != null &&
                                  levelEnterSession.StartedFromBeginning &&
                                  levelEnterSession.Area.Mode == AreaMode.BSide;
        if ((!postcardSkipAvailable && !introSkipAvailable) ||
            !Input.MenuConfirm.Check ||
            !AkronPolicy.CanUse(AkronFeatureKind.LevelEnterSkip).Allowed) {
            Session.LevelEnterSkipHoldSeconds = 0f;
            return;
        }

        Session.LevelEnterSkipHoldSeconds += Math.Max(0f, Engine.RawDeltaTime);
        if (Session.LevelEnterSkipHoldSeconds < 0.73f || levelEnterSession == null) {
            return;
        }

        Session.LevelEnterSkipHoldSeconds = 0f;
        if (!TryUse(AkronFeatureKind.LevelEnterSkip)) {
            return;
        }

        Engine.Scene = new LevelLoader(levelEnterSession);
        Engine.Scene?.Add(new AkronToast("Skipped level intro."));
    }

    private static void LevelOnUpdateTime(On.Celeste.Level.orig_UpdateTime orig, Level self) {
        bool freezeTimerEnabled = Settings.FreezeTimerWhilePaused;
        bool freezeTimerDuringPause = ShouldFreezeTimerDuringPause(self);
        bool canFreezeTimer = freezeTimerEnabled &&
                              freezeTimerDuringPause &&
                              TryUse(AkronFeatureKind.PauseTimerFreeze);
        if (ShouldReleasePauseTimerFreezeStop(pauseTimerFreezeStoppedTimer, freezeTimerEnabled, canFreezeTimer, freezeTimerDuringPause)) {
            self.TimerStopped = false;
            pauseTimerFreezeStoppedTimer = false;
        }

        long previousSessionTime = self.Session?.Time ?? 0L;
        AreaKey area = self.Session.Area;
        AreaModeStats modeStats = TryGetAreaModeStats(area);
        long previousAreaTime = modeStats?.TimePlayed ?? 0L;

        orig(self);

        if (!canFreezeTimer) {
            return;
        }

        self.Session.Time = previousSessionTime;
        if (modeStats != null) {
            modeStats.TimePlayed = previousAreaTime;
        }

        self.TimerStopped = true;
        pauseTimerFreezeStoppedTimer = true;
    }

    private static void UserIOOnSaveHandler(On.Celeste.UserIO.orig_SaveHandler orig, bool file, bool settings) {
        if (AkronBackupActions.ShouldBackupBeforeSave(file, settings)) {
            AkronBackupActions.CreateBackup(settings && !file ? "settings-save" : "save");
        }

        // Native invincibility borrows Celeste's Assist Mode and Invincible flags. Celeste
        // serializes SaveData on its save routine's next step, before the level updates again,
        // so putting the player's own values back here keeps them out of the file; the next
        // level update re-applies the override.
        if (file) {
            RestoreNativeAssistInvincibility();
        }

        orig(file, settings);
    }

    // Everest rewrites modsettings-<mod>.celeste in place: it deletes the file and then streams the
    // new contents into a fresh file at the same path. Anything that interrupts that - a crash, a
    // kill, a full disk - leaves the player's settings truncated at whatever buffer boundary the
    // writer reached, and every Akron setting they ever chose is gone. Writing the whole file beside
    // the target and renaming it over the target makes the target either the old file or the new
    // file and never a fragment, because rename is the one filesystem operation that is atomic
    // everywhere the game runs.
    //
    // The temporary file is a fixed sibling name rather than a random one, so a run of failures
    // cannot fill the Saves folder with fragments, and the caller holds SettingsFileSync so the two
    // threads that can save settings never share it.
    internal static void WriteFileAtomically(string path, Action<Stream> writeContent) {
        string temporaryPath = path + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        try {
            using (FileStream stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None)) {
                writeContent(stream);
                // A rename only publishes bytes the operating system already has, so push them to
                // the device before publishing them.
                stream.Flush(true);
            }

            File.Move(temporaryPath, path, true);
        } catch {
            // The target is untouched: the previous settings, or nothing at all on a first save.
            // Drop the fragment so nothing sitting next to the real file looks like a settings
            // file, but never let a failure to do that hide why the write failed.
            try {
                File.Delete(temporaryPath);
            } catch {
                // Preserve the original write exception; cleanup failure must not replace it.
            }

            throw;
        }
    }

    // Overridden purely to make the write atomic; the path, the format and the serializer are
    // Everest's. Akron's settings file is the only state a player builds up by hand over months,
    // and the stock implementation can destroy all of it in one interrupted write.
    public override void SaveSettings() {
        TrySaveSettings();
    }

    // Returns whether the settings actually reached disk, so a caller can say that they did not
    // rather than logging a save that never happened.
    internal bool TrySaveSettings() {
        if (SettingsType == null || _Settings == null) {
            return false;
        }

        string path = UserIO.GetSaveFilePath("modsettings-" + Metadata.Name);
        lock (SettingsFileSync) {
            try {
                WriteFileAtomically(path, stream => {
                    // Deliberately not disposed: disposing the writer closes the stream, and the
                    // stream still has to be flushed to the device after the YAML is in it.
                    StreamWriter writer = new StreamWriter(stream);
                    YamlHelper.Serializer.Serialize(writer, _Settings, SettingsType);
                    writer.Flush();
                });
                return true;
            } catch (Exception exception) {
                // Everest saves every installed module's settings from one background thread and
                // stops at the first exception, so throwing here would cost every other mod its
                // save as well.
                AkronLog.Warn(nameof(AkronModule), "Could not save Akron settings: " + exception);
                return false;
            }
        }
    }

    // Save Akron settings when the player changes a toggle so killing the game with the
    // overlay open does not lose the change. Draining Everest's background save coroutine
    // on the game thread can spin while it waits for a worker and also rewrites every mod's
    // settings. Write only Akron's file here and leave Celeste's settings to Celeste.
    internal static bool SaveAkronSettingsNow(string reason) {
        try {
            bool saved = Instance?.TrySaveSettings() == true;
            // Celeste's own settings, which Akron changes through actions like Grab Mode. This
            // hands off to Celeste's save routine and returns; it does not block the caller.
            UserIO.SaveHandler(false, true);
            if (saved) {
                AkronLog.Verbose(nameof(AkronModule), "settings saved; reason=" + (reason ?? "unknown"));
            } else {
                // TrySaveSettings already logged why. Saying it here as well is what connects the
                // failure to the thing the player did.
                AkronLog.Warn(nameof(AkronModule), "settings not saved; reason=" + (reason ?? "unknown"));
            }

            return saved;
        } catch (Exception exception) {
            AkronLog.Warn(nameof(AkronModule), "settings save failed; reason=" + (reason ?? "unknown") + "; error=" + exception);
            return false;
        }
    }

    private static bool ShouldFreezeTimerDuringPause(Level level) {
        return level != null && (level.Paused || level.wasPaused);
    }

    internal static bool ShouldReleasePauseTimerFreezeStop(bool stoppedByAkron, bool freezeTimerEnabled, bool canFreezeTimer, bool freezeTimerDuringPause) {
        return stoppedByAkron && (!freezeTimerEnabled || !canFreezeTimer || !freezeTimerDuringPause);
    }

    private static AreaModeStats TryGetAreaModeStats(AreaKey area) {
        if (global::Celeste.SaveData.Instance?.Areas_Safe == null ||
            area.ID < 0 ||
            area.ID >= global::Celeste.SaveData.Instance.Areas_Safe.Count ||
            area.Mode < 0) {
            return null;
        }

        AreaStats areaStats = global::Celeste.SaveData.Instance.Areas_Safe[area.ID];
        int mode = (int) area.Mode;
        if (areaStats?.Modes == null || mode >= areaStats.Modes.Length) {
            return null;
        }

        return areaStats.Modes[mode];
    }

    private static void EngineOnUpdate(On.Monocle.Engine.orig_Update orig, Engine self, GameTime gameTime) {
        // First thing in the hook, so the recorded interval spans a whole engine
        // update including everything Akron itself adds to the frame.
        AkronPerformanceTelemetry.RecordUpdateFrame();
        AkronStartPosPersistence.Update();
        // Engine-level rather than Level.Update: completion clips and the endscreen auto-stop
        // come due after the level has stopped updating.
        AkronInternalRecorder.Update(Engine.Scene);
        RunDeferredScreenWipeAction();
        UpdateDeathWipeRenderSuppression();
        AkronAudioSplitter.Update();
        AkronBackupActions.UpdateInterval((float) gameTime.ElapsedGameTime.TotalSeconds);
        // Downloads finish on a worker, but setup state and toasts belong to the
        // game thread even when the overlay was closed while the request ran.
        AkronCommunityPacks.CompleteImportIfReady(out _, out _);
        if (Engine.Scene is Level) {
            orig(self, gameTime);
            return;
        }

        Scene scene = Engine.Scene;
        if (scene == null) {
            orig(self, gameTime);
            return;
        }

        EnsureOverlay(scene);
        AkronAutomationService.ProcessPendingCommands(scene);
#if DEBUG
        StressUpdate(scene);
#endif
        HandleGlobalOverlayHotkeys(scene);
        if (!Settings.MenuBindingsInGameOnly) {
            AkronOverlay.ExecuteCustomBoundActions(scene);
        }

        if (Overlay?.Visible == true || Overlay?.IsTransientMouseUiActive == true || Settings.StartPosMousePlacement) {
            UpdateOverlayCursorState();
            Overlay.Active = false;
            Overlay.Update();
            UpdateOverlayCursorState();
            if (Overlay.SearchOwnsGameplayInputThisFrame || Settings.PauseGameplayInMenu) {
                return;
            }
        } else {
            UpdateOverlayCursorState();
        }

        AkronRuntimeOptions.Apply(null, null);
        orig(self, gameTime);
        if (Engine.Scene is LevelEnter levelEnter) {
            UpdateLevelEnterSkip(levelEnter);
        }
    }

    internal static void ScheduleAfterEngineUpdate(Action action) {
        if (action != null) {
            afterEngineUpdateActions.Enqueue(action);
        }
    }

    internal static void ScheduleAfterStableEngineUpdate(Action action) {
        if (action == null) {
            return;
        }

        ScheduleAfterEngineUpdate(RunWhenStable);
        void RunWhenStable() {
            // A mod can wrap Akron's Engine.Update hook and keep Calc.PushRandom
            // active until Akron returns. Reloading a room inside that scope
            // destroys the stack before its owner can pop it. Wait until no
            // temporary random scope is active, then perform the state change.
            if (AkronRandomState.HasActiveScope) {
                ScheduleAfterEngineUpdate(RunWhenStable);
                return;
            }

            action();
        }
    }

    private static void RunAfterEngineUpdateActions() {
        // Drain only the actions that existed at this boundary. An action can
        // schedule the next capture phase, which must wait for another complete
        // engine update so a freshly loaded room can initialize normally.
        int count = afterEngineUpdateActions.Count;
        for (int index = 0; index < count; index++) {
            try {
                afterEngineUpdateActions.Dequeue().Invoke();
            } catch (Exception exception) {
                Logger.Log(LogLevel.Error, nameof(AkronModule),
                    "Deferred engine-update action failed: " + exception);
            }
        }
    }

    private static void ClearLastDeathHitboxAfterRespawn(Level level) {
        if (!AkronEntityInspector.HasVisibleLastDeathHitbox()) {
            return;
        }

        Player player = level.Tracker.GetEntity<Player>();
        PlayerDeadBody deadBody = level.Entities.OfType<PlayerDeadBody>().FirstOrDefault();
        bool deathStateActive = player == null || player.Dead || deadBody != null || level.Transitioning;
        if (deathStateActive) {
            Session.LastDeathHitboxSawDeathState = true;
            return;
        }

        ulong framesSinceRecord = Engine.FrameCounter - Session.LastDeathHitboxRecordedFrame;
        // Some forced/debug deaths return to a live player before Akron observes
        // Celeste's dead-body/transition state. Keep the death object briefly
        // visible in that path, but still guarantee it cannot leak into a later
        // attempt.
        if (!Session.LastDeathHitboxSawDeathState && framesSinceRecord < 180) {
            return;
        }

        if (framesSinceRecord >= 6) {
            AkronEntityInspector.ClearLastDeathHitbox();
        }
    }

    private static void LevelOnBeforeRender(On.Celeste.Level.orig_BeforeRender orig, Level self) {
        try {
            orig(self);
        } catch (NullReferenceException ex) when (IsDustEdgesBeforeRenderCrash(ex)) {
            int removed = AkronSaveLoadService.RemoveClonedVisualRuntimeEntities(self);
            Logger.Log(LogLevel.Warn, nameof(AkronModule), "Recovered from DustEdges.BeforeRender crash by removing " + removed + " cloned visual runtime entity/entities.");
        }
    }

    private static void LevelOnRenderForStartPosPresentation(ILContext context) {
        ILCursor cursor = new ILCursor(context);
        // Level.Render first builds GameplayBuffers.Level, then unbinds that
        // target before drawing it to the screen. Replace the rebuilt pixels at
        // that exact point so the normal color-grade, zoom, HUD, and wipe path
        // presents the saved Set frame without duplicating Celeste's renderer.
        if (!cursor.TryGotoNext(
                MoveType.After,
                instruction => instruction.OpCode == OpCodes.Ldsfld &&
                               instruction.Operand is FieldReference field &&
                               field.DeclaringType.FullName == typeof(GameplayBuffers).FullName &&
                               field.Name == nameof(GameplayBuffers.Level)) ||
            !cursor.TryGotoNext(
                MoveType.After,
                instruction => instruction.MatchCallvirt<GraphicsDevice>("SetRenderTarget")) ||
            !cursor.TryGotoNext(
                MoveType.After,
                instruction => instruction.MatchLdnull(),
                instruction => instruction.MatchCallvirt<GraphicsDevice>("SetRenderTarget"))) {
            Logger.Log(LogLevel.Warn, nameof(AkronModule),
                "Could not install exact StartPos frame presentation hook.");
            return;
        }

        cursor.Emit(OpCodes.Ldarg_0);
        cursor.EmitDelegate<Action<Level>>(AkronGameplayBufferState.PresentArmedLevelBuffer);
        // Same point: the room buffer is complete and unbound, and is about to be drawn to the
        // screen. Blurring it here keeps the overlay's background blur out of Celeste's HUD.
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.EmitDelegate<Action<Level>>(AkronOverlayBlur.ApplyToLevelBuffer);
    }

    private static bool IsDustEdgesBeforeRenderCrash(Exception ex) {
        return ex?.StackTrace?.IndexOf("DustEdges.BeforeRender", StringComparison.Ordinal) >= 0;
    }

    private static void RenderAkronLevelHud(Level level, bool ignoreDeathWipeSuppression = false) {
        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, HudSamplerState(), DepthStencilState.None, RasterizerState.CullNone);
        try {
            AkronHudRenderer.Render(level, ignoreDeathWipeSuppression);
            if (AkronCapture.IsCapturingGameFrame) {
                return;
            }

            RenderVisualTuningTint();
            RenderNoclipAccuracyTint();
        } finally {
            Draw.SpriteBatch.End();
        }
    }

    private static void RenderAkronHitboxesUnderDeathWipe(Level level) {
        Viewport viewport = Engine.Viewport;
        // ScreenWipe draws while the graphics viewport already contributes its
        // X/Y origin. WorldToHud includes that origin too, so cancel one copy or
        // letterboxed displays shift this pass down and right.
        Matrix transform = Matrix.CreateTranslation(-viewport.X, -viewport.Y, 0f);
        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, transform);
        try {
            // GameplayRenderer's regular hitbox pass is suppressed after the
            // wipe starts. Draw in final-screen coordinates here so the wipe
            // itself, rendered next, hides only the pixels it has reached.
            AkronEntityInspector.RenderHitboxes(level, level.Tracker.GetEntity<Player>());
        } finally {
            Draw.SpriteBatch.End();
        }
    }

    private static void GameplayRendererOnRender(On.Celeste.GameplayRenderer.orig_Render orig, GameplayRenderer self, Scene scene) {
        orig(self, scene);

        if (scene is not Level level ||
            AkronCapture.IsCapturingGameFrame ||
            ShouldHideAkronRenderSurfaces() ||
            !ShouldRenderGameplayDebugPass(level)) {
            return;
        }

        // CelesteTAS renders hitboxes inside GameplayRenderer.Render so Monocle's
        // active gameplay camera owns the world-to-screen transform. Auto Kill
        // and Auto Deafen areas use the same pass because their placement
        // preview, saved display, and death hitbox all describe world pixels.
        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
        try {
            AkronHudRenderer.RenderAutomationAreasToGameplayBuffer(level);
            AkronEntityInspector.RenderInspectorPinOutlinesToGameplayBuffer(level);
            AkronEntityInspector.RenderHitboxesToGameplayBuffer(level, level.Tracker.GetEntity<Player>());
        } finally {
            Draw.SpriteBatch.End();
        }
    }

    private static bool ShouldRenderAkronHitboxes() {
        AkronModuleSettings settings = Settings;
        return settings.HitboxViewer ||
               settings.HitboxShowLastDeath &&
               AkronEntityInspector.HasVisibleLastDeathHitbox();
    }

    private static bool ShouldRenderGameplayDebugPass(Level level) {
        bool showHitboxes = ShouldRenderAkronHitboxes();
        AkronModuleSettings settings = Settings;
        bool showAutoKillArea = settings.AutoKill &&
                                settings.AutoKillArea &&
                                settings.AutoKillShowArea;
        bool showAutoDeafenArea = settings.AutoDeafen &&
                                  settings.AutoDeafenArea &&
                                  settings.AutoDeafenShowArea;
        bool selectingArea = TryGetPracticeAreaSelectionPreview(level, isAutoDeafen: false, out _, out _) ||
                             TryGetPracticeAreaSelectionPreview(level, isAutoDeafen: true, out _, out _);

        bool showInspectorPin = AkronEntityInspector.HasInspectorPinSelection() ||
                                AkronEntityInspector.HasInspectorPinPreview();

        return showHitboxes || showInspectorPin || showAutoKillArea || showAutoDeafenArea || selectingArea;
    }

    private static void RefillOnCtor(On.Celeste.Refill.orig_ctor_Vector2_bool_bool orig, Refill self, Vector2 position, bool twoDashes, bool oneUse) {
        orig(self, position, twoDashes, oneUse);
        ApplyRefillClaritySprite(self, twoDashes, oneUse);
    }

    private static void RefreshRefillClaritySprites(Level level) {
        if (level?.Tracker == null) {
            return;
        }

        foreach (Refill refill in level.Entities.OfType<Refill>()) {
            DynData<Refill> refillData = new DynData<Refill>(refill);
            ApplyRefillClaritySprite(refill, refillData.Get<bool>("twoDashes"), refillData.Get<bool>("oneUse"));
        }
    }

    private static void ApplyRefillClaritySprite(Refill refill, bool twoDashes, bool oneUse) {
        string path = twoDashes ? "objects/refillTwo/" : "objects/refill/";
        DynData<Refill> refillData = new DynData<Refill>(refill);
        Sprite sprite = refillData.Get<Sprite>("sprite");

        if (!oneUse ||
            !Settings.RefillClarity ||
            !AkronPolicy.CanUse(AkronFeatureKind.RefillClarity).Allowed) {
            if (RefillClaritySpriteStates.TryGetValue(refill, out RefillClaritySpriteState inactiveState)) {
                RestoreRefillClaritySprite(sprite, path, inactiveState);
            }
            return;
        }

        RefillClaritySpriteState state = RefillClaritySpriteStates.GetValue(refill, _ => new RefillClaritySpriteState());
        int color = AkronModuleSettings.ClampRgb(Settings.RefillClarityColor);
        int opacity = AkronModuleSettings.ClampOpacity(Settings.RefillClarityOpacity);

        if (state.Applied &&
            state.TwoDashes == twoDashes &&
            state.Color == color &&
            state.Opacity == opacity) {
            return;
        }

        MTexture[] frames = GetRefillClarityFrames(sprite, twoDashes, color, opacity);
        if (frames == null || frames.Length == 0) {
            return;
        }

        sprite.Path = path + "idlenr";
        if (sprite.Has("idlenr")) {
            Sprite.Animation animation = sprite.Animations["idlenr"];
            animation.Delay = 0.1f;
            animation.Frames = frames;
        } else {
            sprite.AddLoop("idlenr", 0.1f, frames);
        }

        sprite.Play("idlenr");
        state.Applied = true;
        state.TwoDashes = twoDashes;
        state.Color = color;
        state.Opacity = opacity;
    }

    private static MTexture[] GetRefillClarityFrames(Sprite sprite, bool twoDashes, int color, int opacity) {
        if (!sprite.Has("idle")) {
            return null;
        }

        MTexture[] idleFrames = sprite.Animations["idle"].Frames;
        RefillClarityFrameCacheKey key = GetRefillClarityFrameCacheKey(idleFrames, twoDashes, color, opacity);
        if (RefillClarityFrameCache.TryGetValue(key, out MTexture[] cached)) {
            return cached;
        }

        RefillClaritySourceFrame[] sourceFrames = GetRefillClaritySourceFrames(idleFrames, twoDashes);
        if (sourceFrames == null || sourceFrames.Length == 0) {
            return null;
        }

        string textureKey = RefillClarityFrameCache.Count.ToString(CultureInfo.InvariantCulture);
        MTexture[] frames = new MTexture[sourceFrames.Length];
        for (int index = 0; index < sourceFrames.Length; index++) {
            frames[index] = CreateRefillClarityFrame(sourceFrames[index], textureKey + "|" + index.ToString(CultureInfo.InvariantCulture), color, opacity);
        }

        RefillClarityFrameCache[key] = frames;
        return frames;
    }

    private static RefillClaritySourceFrame[] GetRefillClaritySourceFrames(MTexture[] idleFrames, bool twoDashes) {
        RefillClaritySourceCacheKey key = GetRefillClaritySourceFrameCacheKey(idleFrames, twoDashes);
        if (RefillClaritySourceFrameCache.TryGetValue(key, out RefillClaritySourceFrame[] cached)) {
            return cached;
        }

        if (Engine.Graphics?.GraphicsDevice == null) {
            return null;
        }

        RefillClaritySourceFrame[] frames = new RefillClaritySourceFrame[idleFrames.Length];
        for (int index = 0; index < idleFrames.Length; index++) {
            frames[index] = ReadRefillClaritySourceFrame(idleFrames[index]);
        }

        RefillClaritySourceFrameCache[key] = frames;
        return frames;
    }

    private static RefillClaritySourceFrame ReadRefillClaritySourceFrame(MTexture frame) {
        Rectangle clip = frame.ClipRect;
        Color[] clippedPixels = new Color[clip.Width * clip.Height];
        frame.Texture.Texture_Safe.GetData(0, clip, clippedPixels, 0, clippedPixels.Length);
        Color[] pixels = ExpandRefillClaritySourcePixels(
            clippedPixels,
            clip.Width,
            clip.Height,
            (int) frame.DrawOffset.X,
            (int) frame.DrawOffset.Y,
            frame.Width,
            frame.Height);
        return new RefillClaritySourceFrame(
            frame.Width,
            frame.Height,
            Vector2.Zero,
            frame.Width,
            frame.Height,
            pixels);
    }

    internal static T[] ExpandRefillClaritySourcePixels<T>(
        T[] clippedPixels,
        int clippedWidth,
        int clippedHeight,
        int offsetX,
        int offsetY,
        int frameWidth,
        int frameHeight) {
        T[] framePixels = new T[frameWidth * frameHeight];
        for (int y = 0; y < clippedHeight; y++) {
            Array.Copy(clippedPixels, y * clippedWidth, framePixels, offsetX + (offsetY + y) * frameWidth, clippedWidth);
        }

        return framePixels;
    }

    internal static RefillClarityFrameCacheKey GetRefillClarityFrameCacheKey(
        MTexture[] idleFrames,
        bool twoDashes,
        int color,
        int opacity) {
        return new RefillClarityFrameCacheKey(idleFrames, twoDashes, color, opacity);
    }

    internal static RefillClaritySourceCacheKey GetRefillClaritySourceFrameCacheKey(MTexture[] idleFrames, bool twoDashes) {
        return new RefillClaritySourceCacheKey(idleFrames, twoDashes);
    }

    private static MTexture CreateRefillClarityFrame(RefillClaritySourceFrame source, string key, int rgb, int opacity) {
        Color[] pixels = BuildRefillClarityPixels(source.Pixels, source.PixelWidth, source.PixelHeight, rgb, opacity);
        VirtualTexture texture = VirtualContent.CreateTexture("akron-refill-clarity-" + key, source.PixelWidth, source.PixelHeight, Color.Transparent);
        texture.Texture_Safe.SetData(pixels);
        RefillClarityFrameTextures.Add(texture);
        return new MTexture(texture, source.DrawOffset, source.FrameWidth, source.FrameHeight);
    }

    internal static Color[] BuildRefillClarityPixels(Color[] source, int width, int height, int rgb, int opacity) {
        Color[] pixels = new Color[source.Length];
        bool[] opaquePixels = new bool[source.Length];
        for (int index = 0; index < source.Length; index++) {
            opaquePixels[index] = source[index].A > 0;
        }

        // Refill.Render draws its own black outline outside these texture pixels.
        // Color the texture's interior edge so enabling clarity does not enlarge it.
        bool[] outlineMask = BuildRefillClarityOutlineMask(opaquePixels, width, height);
        byte red = (byte) ((rgb >> 16) & 0xFF);
        byte green = (byte) ((rgb >> 8) & 0xFF);
        byte blue = (byte) (rgb & 0xFF);

        for (int index = 0; index < pixels.Length; index++) {
            Color pixel = source[index];
            if (outlineMask[index]) {
                pixels[index] = new Color(
                    BlendRefillClarityChannel(pixel.R, red, pixel.A, opacity),
                    BlendRefillClarityChannel(pixel.G, green, pixel.A, opacity),
                    BlendRefillClarityChannel(pixel.B, blue, pixel.A, opacity),
                    pixel.A);
                continue;
            }

            if (pixel.A > 0) {
                pixels[index] = PremultiplyTexturePixel(pixel);
                continue;
            }

            pixels[index] = Color.Transparent;
        }

        return pixels;
    }

    internal static bool[] BuildRefillClarityOutlineMask(bool[] opaquePixels, int width, int height) {
        bool[] outlineMask = new bool[opaquePixels.Length];
        for (int index = 0; index < opaquePixels.Length; index++) {
            if (!opaquePixels[index]) {
                continue;
            }

            int x = index % width;
            int y = index / width;
            outlineMask[index] = !IsOpaqueRefillPixel(opaquePixels, width, height, x - 1, y) ||
                                 !IsOpaqueRefillPixel(opaquePixels, width, height, x + 1, y) ||
                                 !IsOpaqueRefillPixel(opaquePixels, width, height, x, y - 1) ||
                                 !IsOpaqueRefillPixel(opaquePixels, width, height, x, y + 1);
        }

        return outlineMask;
    }

    internal static byte BlendRefillClarityChannel(byte source, byte target, byte alpha, int opacity) {
        byte blended = (byte) Math.Round(source + (target - source) * (opacity / 100f));
        return (byte) (blended * alpha / 255);
    }

    private static bool IsOpaqueRefillPixel(bool[] pixels, int width, int height, int x, int y) {
        return x >= 0 && x < width && y >= 0 && y < height && pixels[x + y * width];
    }

    private static Color PremultiplyTexturePixel(Color pixel) {
        if (pixel.A == 0) {
            return Color.Transparent;
        }

        return new Color(
            (byte) (pixel.R * pixel.A / 255),
            (byte) (pixel.G * pixel.A / 255),
            (byte) (pixel.B * pixel.A / 255),
            pixel.A);
    }

    private static void RestoreRefillClaritySprite(Sprite sprite, string path, RefillClaritySpriteState state) {
        if (sprite != null && state.Applied) {
            sprite.Path = path + "idle";
            if (sprite.Has("idle")) {
                sprite.Play("idle");
            }
        }

        state.Applied = false;
    }

    private static void ClearRefillClarityFrameCache() {
        foreach (VirtualTexture texture in RefillClarityFrameTextures) {
            texture.Dispose();
        }

        RefillClarityFrameTextures.Clear();
        RefillClarityFrameCache.Clear();
        RefillClaritySourceFrameCache.Clear();
    }

    private readonly struct RefillClaritySourceFrame {
        public RefillClaritySourceFrame(int pixelWidth, int pixelHeight, Vector2 drawOffset, int frameWidth, int frameHeight, Color[] pixels) {
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            DrawOffset = drawOffset;
            FrameWidth = frameWidth;
            FrameHeight = frameHeight;
            Pixels = pixels;
        }

        public int PixelWidth { get; }
        public int PixelHeight { get; }
        public Vector2 DrawOffset { get; }
        public int FrameWidth { get; }
        public int FrameHeight { get; }
        public Color[] Pixels { get; }
    }

    internal readonly struct RefillClaritySourceCacheKey : IEquatable<RefillClaritySourceCacheKey> {
        public RefillClaritySourceCacheKey(MTexture[] frames, bool twoDashes) {
            Frames = frames;
            TwoDashes = twoDashes;
        }

        private MTexture[] Frames { get; }
        private bool TwoDashes { get; }

        public bool Equals(RefillClaritySourceCacheKey other) {
            return ReferenceEquals(Frames, other.Frames) && TwoDashes == other.TwoDashes;
        }

        public override bool Equals(object obj) {
            return obj is RefillClaritySourceCacheKey other && Equals(other);
        }

        public override int GetHashCode() {
            return HashCode.Combine(RuntimeHelpers.GetHashCode(Frames), TwoDashes);
        }
    }

    internal readonly struct RefillClarityFrameCacheKey : IEquatable<RefillClarityFrameCacheKey> {
        public RefillClarityFrameCacheKey(MTexture[] frames, bool twoDashes, int color, int opacity) {
            Frames = frames;
            TwoDashes = twoDashes;
            Color = color;
            Opacity = opacity;
        }

        private MTexture[] Frames { get; }
        private bool TwoDashes { get; }
        private int Color { get; }
        private int Opacity { get; }

        public bool Equals(RefillClarityFrameCacheKey other) {
            return ReferenceEquals(Frames, other.Frames) &&
                   TwoDashes == other.TwoDashes &&
                   Color == other.Color &&
                   Opacity == other.Opacity;
        }

        public override bool Equals(object obj) {
            return obj is RefillClarityFrameCacheKey other && Equals(other);
        }

        public override int GetHashCode() {
            return HashCode.Combine(RuntimeHelpers.GetHashCode(Frames), TwoDashes, Color, Opacity);
        }
    }

    private sealed class RefillClaritySpriteState {
        public bool Applied;
        public bool TwoDashes;
        public int Color;
        public int Opacity;
    }

    internal static SamplerState HudSamplerState() {
        // Akron HUD text is intentionally user-scalable. Linear filtering keeps
        // labels readable at non-native scale settings while solid rectangles
        // and world-area outlines still render with stable edges.
        return SamplerState.LinearClamp;
    }

    private static void RenderAkronScreenProjection(Scene scene) {
        Viewport viewport = Engine.Viewport;
        Matrix transform = Matrix.CreateTranslation(viewport.X, viewport.Y, 0f);
        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, transform);
        try {
            AkronScreenProjection.RenderLayer(scene, viewport.Width, viewport.Height);
        } finally {
            Draw.SpriteBatch.End();
        }
    }

    private static void HudRendererOnRenderContent(On.Celeste.HudRenderer.orig_RenderContent orig, HudRenderer self, Scene scene) {
        if (AkronCapture.IsCapturingGameFrame) {
            return;
        }

        if (AkronRuntimeOptions.ShouldSuppressPauseHud(scene)) {
            return;
        }

        if (!AkronRuntimeOptions.ShouldSuppressPauseBackgroundFade(scene)) {
            orig(self, scene);
            return;
        }

        float backgroundFade = self.BackgroundFade;
        self.BackgroundFade = 0f;
        try {
            orig(self, scene);
        } finally {
            self.BackgroundFade = backgroundFade;
        }
    }

    internal static void EnsureCaptureSuppressionHooks() {
        if (captureSuppressionHooksInstalled) {
            return;
        }

        // Speedrun Tool and other late-loading mods can hook timer rendering
        // after Akron's Load hook runs. Installing this on first capture puts
        // Akron's capture suppression at the end of the active render chain.
        using (new DetourConfigContext(new DetourConfig(
                   "Akron.CaptureSuppression.SpeedrunTimerDisplay",
                   after: new List<string> { "*" }
               )).Use()) {
            On.Celeste.SpeedrunTimerDisplay.Render += SpeedrunTimerDisplayOnRender;
            On.Celeste.SpeedrunTimerDisplay.DrawTime += SpeedrunTimerDisplayOnDrawTime;
        }
        captureSuppressionHooksInstalled = true;
    }

    private static void SpeedrunTimerDisplayOnRender(On.Celeste.SpeedrunTimerDisplay.orig_Render orig, SpeedrunTimerDisplay self) {
        if (AkronCapture.IsCapturingGameFrame) {
            return;
        }

        orig(self);
    }

    private static void SpeedrunTimerDisplayOnDrawTime(
        On.Celeste.SpeedrunTimerDisplay.orig_DrawTime orig,
        Vector2 position,
        string timeString,
        float scale,
        bool valid,
        bool finished,
        bool bestTime,
        float alpha) {
        if (AkronCapture.IsCapturingGameFrame) {
            return;
        }

        orig(position, timeString, scale, valid, finished, bestTime, alpha);
    }

    private static void TalkComponentUiOnRender(On.Celeste.TalkComponent.TalkComponentUI.orig_Render orig, TalkComponent.TalkComponentUI self) {
        if (AkronCapture.IsCapturingGameFrame) {
            return;
        }

        orig(self);
    }

    private static void MiniTextboxOnRender(On.Celeste.MiniTextbox.orig_Render orig, MiniTextbox self) {
        if (AkronCapture.IsCapturingGameFrame) {
            return;
        }

        orig(self);
    }

    private static void TextMenuOnUpdate(On.Celeste.TextMenu.orig_Update orig, TextMenu self) {
        ReplacePauseMenuButtonActionIfNeeded(self?.Current as TextMenu.Button);
        orig(self);
        KeepNativeTextMenuInsideViewport(self);
    }

    private static void TextMenuButtonOnConfirmPressed(On.Celeste.TextMenu.Button.orig_ConfirmPressed orig, TextMenu.Button self) {
        ReplacePauseMenuButtonActionIfNeeded(self);
        orig(self);
    }

    private static void KeepNativeTextMenuInsideViewport(TextMenu menu) {
        if (menu == null || !menu.Visible || menu.Width <= 0f || Engine.Width <= 0) {
            return;
        }

        // Everest/native TextMenu pages can be wider than the current window scale allows.
        // Preserve their native layout and only move the anchor far enough to keep the
        // readable left edge on screen.
        menu.X = CalculateSafeTextMenuX(menu.X, menu.Width, Engine.Width, menu.Justify.X);
    }

    internal static float CalculateSafeTextMenuX(
        float currentX,
        float menuWidth,
        float displayWidth,
        float justifyX,
        float margin = 96f) {
        if (menuWidth <= 0f || displayWidth <= 0f) {
            return currentX;
        }

        float clampedJustify = Math.Min(1f, Math.Max(0f, justifyX));
        float safeMargin = Math.Min(Math.Max(0f, margin), Math.Max(0f, (displayWidth / 2f) - 1f));
        float left = currentX - (menuWidth * clampedJustify);
        if (left < safeMargin) {
            return safeMargin + (menuWidth * clampedJustify);
        }

        float right = currentX + (menuWidth * (1f - clampedJustify));
        float rightLimit = displayWidth - safeMargin;
        if (menuWidth <= displayWidth - (safeMargin * 2f) && right > rightLimit) {
            return rightLimit - (menuWidth * (1f - clampedJustify));
        }

        return currentX;
    }

    private static void AutoSavingNoticeOnUpdate(On.Celeste.AutoSavingNotice.orig_Update orig, AutoSavingNotice self, Scene scene) {
        if (ShouldSuppressSavingNotice(AkronCapture.IsCapturingGameFrame, Settings.AutosaveHideSavingIcon)) {
            self.Display = false;
            self.StillVisible = false;
            return;
        }

        orig(self, scene);
    }

    private static void AutoSavingNoticeOnRender(On.Celeste.AutoSavingNotice.orig_Render orig, AutoSavingNotice self, Scene scene) {
        if (ShouldSuppressSavingNotice(AkronCapture.IsCapturingGameFrame, Settings.AutosaveHideSavingIcon)) {
            return;
        }

        orig(self, scene);
    }

    internal static bool ShouldSuppressSavingNotice(bool isCapturingGameFrame, bool hideSavingIcon) {
        return isCapturingGameFrame || hideSavingIcon;
    }

    private static void SaveLoadIconOnShow(On.Celeste.SaveLoadIcon.orig_Show orig, Scene scene) {
        if (ShouldSuppressSaveLoadIcon(AkronCapture.IsCapturingGameFrame, Settings.AutosaveHideSavingIcon)) {
            return;
        }

        orig(scene);
    }

    private static void SaveLoadIconOnRender(On.Celeste.SaveLoadIcon.orig_Render orig, SaveLoadIcon self) {
        if (ShouldSuppressSaveLoadIcon(AkronCapture.IsCapturingGameFrame, Settings.AutosaveHideSavingIcon)) {
            return;
        }

        orig(self);
    }

    internal static bool ShouldSuppressSaveLoadIcon(bool isCapturingGameFrame, bool hideSavingIcon) {
        return isCapturingGameFrame || hideSavingIcon;
    }

    private static void EngineOnRenderCore(On.Monocle.Engine.orig_RenderCore orig, Engine self) {
        // The render boundary runs after every Engine.Update wrapper has
        // returned. A helper can therefore release a temporary random scope
        // before a deferred StartPos capture or restore checks for stability.
        RunAfterEngineUpdateActions();
        orig(self);
        UpdateStateTransitionRenderSuppression();
        Scene scene = Engine.Scene;
        bool isLevelScene = scene is Level;

        if (scene is Level level) {
            // Read the completed 320x180 room buffer before Akron draws its HUD.
            // StartPos restoration tests need exact game pixels without timer text or
            // desktop compositor noise.
            AkronCapture.CapturePendingGameplayBufferQaFrame();
            renderedStartPosFrameGeneration = AkronActions.StartPosFrameGeneration;
            AkronInternalRecorder.CaptureFrame(level);
            if (deathWipeRenderSuppressionActive && level.Transitioning) {
                deathWipeRenderSuppressionHasDrawnPrimitives = true;
            }
        } else {
            AkronInternalRecorder.CaptureFrame(scene);
        }

        bool hideAkronRenderSurfaces = ShouldHideAkronRenderSurfaces();

        if (!hideAkronRenderSurfaces && scene is Level postRenderLevel) {
            RenderAkronLevelHud(postRenderLevel);
        }

        if (!hideAkronRenderSurfaces) {
            RenderAkronScreenProjection(scene);
        }

        bool overlayVisible = Overlay?.Visible == true || Overlay?.IsStartPosPlacementActive == true;
        Level inspectorPinLevel = scene as Level;
        bool inspectorPinVisible = !overlayVisible &&
                                   !hideAkronRenderSurfaces &&
                                   inspectorPinLevel != null &&
                                   AkronEntityInspector.ShouldRenderInspectorPinImGui(inspectorPinLevel);
        AkronPerformanceTelemetry.RecordRenderFrame(overlayVisible);
        if (overlayVisible && !renderCoreDiagnosticLogged) {
            renderCoreDiagnosticLogged = true;
            Logger.Log(LogLevel.Info, nameof(AkronModule), "Akron overlay visible during Engine.RenderCore final pass.");
        }

        if (!isLevelScene &&
            !hideAkronRenderSurfaces &&
            Settings.LabelSystemVisible &&
            Settings.CustomHudLabels &&
            Settings.CustomHudLabelsInNonLevelScenes &&
            !overlayVisible &&
            !Settings.HideAkronHud) {
            Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, HudSamplerState(), DepthStencilState.None, RasterizerState.CullNone, null, Engine.ScreenMatrix);
            try {
                float y = 0f;
                AkronCustomHudLabels.Render(null, null, ref y, Engine.Width, Engine.Height);
            } finally {
                Draw.SpriteBatch.End();
            }
        }

        bool overlayImGuiFrameRequested = overlayVisible || Overlay?.NeedsImGuiFrame == true;
        if (overlayImGuiFrameRequested) {
            bool overlayImGuiRendered = Overlay.RenderImGui();
            if (overlayVisible && !overlayImGuiRendered) {
                Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, Engine.ScreenMatrix);
                try {
                    Overlay.RenderSpriteBatchFallback();
                } finally {
                    Draw.SpriteBatch.End();
                }
            }
        } else if (inspectorPinVisible && TryUse(AkronFeatureKind.EntityInspector)) {
            AkronEntityInspector.RenderInspectorPinImGui(inspectorPinLevel);
        }
    }

    private static ScreenWipe LevelOnCompleteArea(On.Celeste.Level.orig_CompleteArea_bool_bool orig, Level self, bool spotlightWipe, bool skipScreenWipe) {
        AkronScreenProjection.Attach(self);
        return orig(self, spotlightWipe, skipScreenWipe);
    }

    private static ScreenWipe LevelOnCompleteArea(On.Celeste.Level.orig_CompleteArea_bool_bool_bool orig, Level self, bool spotlightWipe, bool skipScreenWipe, bool skipCompleteScreen) {
        AkronScreenProjection.Attach(self);
        return orig(self, spotlightWipe, skipScreenWipe, skipCompleteScreen);
    }

    private static void LevelOnRegisterAreaComplete(On.Celeste.Level.orig_RegisterAreaComplete orig, Level self) {
        orig(self);
        AkronScreenProjection.Attach(self);
        AkronInternalRecorder.NotifyAreaComplete(self);
        AkronActions.RestoreAutoDeafen();
        // Every completion gets a sidecar: it is the record of what the run was played with, and
        // that is worth having whether or not a proof helper happened to be on. The panel stays a
        // helper surface, so it only appears when a helper is on or the attempt is not clean.
        string path = AkronProof.WriteSidecar(self, "area-complete");
        if (Settings.ProofModeOverlay || Settings.EndScreenHelper || Session.AttemptStatus != AkronStatus.GoldberryHardlistClean) {
            AkronProof.ShowProofPanel(self, "area-complete", path);
        }
    }


    private static void RenderVisualTuningTint() {
        if (!Settings.ScreenTint || !TryUse(AkronFeatureKind.VisualTuning)) {
            return;
        }

        float opacity = AkronModuleSettings.ClampOpacity(Settings.ScreenTintOpacity) / 100f;
        if (opacity <= 0f) {
            return;
        }

        Color color = ColorFromRgb(Settings.ScreenTintColor) * opacity;
        Draw.Rect(0f, 0f, Engine.Width, Engine.Height, color);
    }

    private static void EnsureNativeAssistInvincibility() {
        if (global::Celeste.SaveData.Instance == null) {
            return;
        }

        if (!nativeAssistInvincibilityCaptured) {
            previousAssistMode = global::Celeste.SaveData.Instance.AssistMode;
            previousAssistInvincible = global::Celeste.SaveData.Instance.Assists.Invincible;
            nativeAssistInvincibilityCaptured = true;
        }

        global::Celeste.SaveData.Instance.AssistMode = true;
        global::Celeste.SaveData.Instance.Assists.Invincible = true;
    }

    private static void RestoreNativeAssistInvincibility() {
        if (!nativeAssistInvincibilityCaptured || global::Celeste.SaveData.Instance == null) {
            return;
        }

        global::Celeste.SaveData.Instance.AssistMode = previousAssistMode;
        global::Celeste.SaveData.Instance.Assists.Invincible = previousAssistInvincible;
        nativeAssistInvincibilityCaptured = false;
    }

    private static void RescueInvinciblePlayerFromBottomlessFall(Level level, Player player, bool recordHazardAccuracy) {
        bool touchingBottomKillbox = IsPlayerTouchingBottomKillbox(player);
        if (!IsPlayerPastBottomKillboxRescueBoundary(player) ||
            player.StateMachine.State == Player.StReflectionFall ||
            player.StateMachine.State == Player.StTempleFall ||
            level.Transitioning) {
            return;
        }

        if (ShouldRecordBottomKillboxHazardAccuracyBeforeRescue(recordHazardAccuracy, touchingBottomKillbox)) {
            RecordHazardAccuracyInvalidContact(player);
        }

        Vector2 respawn = level.Session.RespawnPoint ?? level.GetSpawnPoint(level.Camera.Position);
        player.Position = respawn;
        player.Speed = Vector2.Zero;
        player.StateMachine.State = Player.StNormal;
    }
}
