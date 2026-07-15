using System;
using System.Collections;
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

    public override Type SaveDataType => typeof(AkronModuleSaveData);
    public static AkronModuleSaveData SaveData => (AkronModuleSaveData) Instance._SaveData;
    internal static bool IsOverlayVisible => Overlay?.Visible == true;

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
    private static readonly MethodInfo EverestSaveSettingsMethod =
        typeof(Everest).GetMethod("_SaveSettings", BindingFlags.Static | BindingFlags.NonPublic);
    private static MethodInfo playerDashCoroutineMethod;
    private static ILHook dashCoroutineHook;
    private static MethodInfo lookoutLookRoutineMethod;
    private static ILHook lookoutRoutineHook;
    private static readonly FieldInfo LevelEnterSessionField =
        typeof(LevelEnter).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic);
    private static Vector2 preRedirectDashAim;
    private static readonly ConditionalWeakTable<Refill, RefillClaritySpriteState> RefillClaritySpriteStates =
        new ConditionalWeakTable<Refill, RefillClaritySpriteState>();
    private static readonly Dictionary<string, RefillClaritySourceFrame[]> RefillClaritySourceFrameCache = new Dictionary<string, RefillClaritySourceFrame[]>();
    private static readonly Dictionary<string, MTexture[]> RefillClarityFrameCache = new Dictionary<string, MTexture[]>();
    private static readonly List<VirtualTexture> RefillClarityFrameTextures = new List<VirtualTexture>();

    public AkronModule() {
        Instance = this;
        Logger.SetLogLevel(nameof(AkronModule), LogLevel.Info);
    }

    public override void Load() {
        AkronModuleSettings.EnsureCurrentKeybindDefaults(Settings);
        AkronModuleSettings.ClearOneShotRuntimeActions(Settings);
        AkronLog.Normal(nameof(AkronModule), "load start; " + AkronLog.DescribeSettings());
        try {
            AkronImGuiRenderer.EnsureNativeResolverRegistered();
            typeof(AkronSaveLoadExports).ModInterop();
            AkronSpeedrunToolBroker.Initialize();
            AkronInterop.Initialize();
            AkronNativeSavestateSupport.Initialize();
            AkronScreenshotScanner.Load();
        } catch (Exception exception) {
            Logger.Log(LogLevel.Error, nameof(AkronModule), "Akron startup helper initialization failed; continuing so the module menu and overlay can still load: " + exception);
        }
        On.Celeste.Level.Begin += LevelOnBegin;
        On.Celeste.Level.UpdateTime += LevelOnUpdateTime;
        On.Celeste.Level.Update += LevelOnUpdate;
        On.Celeste.Level.BeforeRender += LevelOnBeforeRender;
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
        On.Monocle.Engine.Update += EngineOnUpdate;
        On.Monocle.Engine.RenderCore += EngineOnRenderCore;
        On.Celeste.Level.CompleteArea_bool_bool += LevelOnCompleteArea;
        On.Celeste.Level.CompleteArea_bool_bool_bool += LevelOnCompleteArea;
        On.Celeste.Level.RegisterAreaComplete += LevelOnRegisterAreaComplete;
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
        SaveAkronSettingsNow("unload");
        AkronLog.FlushDiagnosticSummaries();
        AkronLog.Normal(nameof(AkronModule), "unload start");
        AkronBackupActions.NotifyShutdown();
        AkronInterop.UnregisterSpeedrunToolSaveLoadHooks();
        AkronScreenshotScanner.Unload();
        AkronNativeSavestateSupport.Reset();
        AkronSaveLoadService.ClearRuntimeState();
        On.Celeste.Level.Begin -= LevelOnBegin;
        On.Celeste.Level.UpdateTime -= LevelOnUpdateTime;
        On.Celeste.Level.Update -= LevelOnUpdate;
        On.Celeste.Level.BeforeRender -= LevelOnBeforeRender;
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
        On.Monocle.Engine.Update -= EngineOnUpdate;
        On.Monocle.Engine.RenderCore -= EngineOnRenderCore;
        On.Celeste.Level.CompleteArea_bool_bool -= LevelOnCompleteArea;
        On.Celeste.Level.CompleteArea_bool_bool_bool -= LevelOnCompleteArea;
        On.Celeste.Level.RegisterAreaComplete -= LevelOnRegisterAreaComplete;
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
        AkronEntityInspector.UnloadInspectorPin();
        dashCoroutineHook?.Dispose();
        dashCoroutineHook = null;
        lookoutRoutineHook?.Dispose();
        lookoutRoutineHook = null;
        respawnTimeElapsed.Clear();
        noDeathEffectBodies.Clear();
        RestoreCursorVisibility();
        RestoreNativeAssistInvincibility();
        RestoreNoclipDepth();
        RestorePlayerVisibilityOverride();
        ClearRefillClarityFrameCache();
        ResetNoclipAccuracy();
        AkronActions.RestoreAutoDeafen();
        AkronActions.RestoreLowVolumeBypass();
        AkronRuntimeOptions.Reset();
        deferredScreenWipeAction = null;
        ClearDeathWipeRenderSuppression();
        if (AkronInternalRecorder.IsRecording) {
            AkronInternalRecorder.Stop();
        }
#pragma warning disable CS0618
        Engine.TimeRate = 1f;
#pragma warning restore CS0618
    }

    internal static void ApplyMotionSmoothingSettings() {
        if (Instance?._Settings == null || Engine.Instance == null) {
            return;
        }

        AkronMotionSmoothingInterop.ApplyAkronSettings();
    }

    private static void LevelOnBegin(On.Celeste.Level.orig_Begin orig, Level self) {
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
        AkronPracticeStats.OnLevelBegin(self);
        AkronPracticeCounters.OnLevelBegin(self);
        AkronAutosave.NotifyLevelBegin(self);
        AkronBackupActions.NotifyLevelBegin(self);
        AkronInternalRecorder.NotifyLevelBegin(self);
        AkronActions.ApplyCameraOffset(self);
        EnsureOverlay(self);
        Overlay?.PrewarmLayout(self);
    }

    private static void LevelOnUpdate(On.Celeste.Level.orig_Update orig, Level self) {
        RunDeferredScreenWipeAction();
        UpdateDeathWipeRenderSuppression();
        UpdateStateTransitionRenderSuppression();
        EnsureOverlay(self);
        AkronScreenshotScanner.MaintainActiveScanHost(self);
        AkronAutomationService.ProcessPendingCommands(self);
#if DEBUG
        StressUpdate(self);
#endif
        HandleHotkeys(self);
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
        orig(self);
        RememberNativeFreezeFrameForLagPauser();
        AkronRuntimeOptions.ApplyScreenshakeAfterLevelUpdate(self);
        ApplyJumpHackAfterPlayerUpdate(self);
        ClearLastDeathHitboxAfterRespawn(self);
        AkronPracticeStats.OnLevelUpdate(self);
        AkronInternalRecorder.Update(self);
        UpdateProofRecorderGuard(self);
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

        orig(file, settings);
    }

    internal static void SaveAkronSettingsNow(string reason) {
        try {
            if (EverestSaveSettingsMethod != null) {
                if (EverestSaveSettingsMethod.Invoke(null, Array.Empty<object>()) is IEnumerator routine) {
                    while (routine.MoveNext()) {
                    }
                }
            } else {
                UserIO.SaveHandler(false, true);
            }

            AkronLog.Verbose(nameof(AkronModule), "settings saved; reason=" + (reason ?? "unknown"));
        } catch (Exception exception) {
            AkronLog.Warn(nameof(AkronModule), "settings save failed; reason=" + (reason ?? "unknown") + "; error=" + exception);
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
        RunDeferredScreenWipeAction();
        UpdateDeathWipeRenderSuppression();
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

        MTexture[] frames = GetRefillClarityFrames(twoDashes, color, opacity);
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

    private static MTexture[] GetRefillClarityFrames(bool twoDashes, int color, int opacity) {
        string key = (twoDashes ? "two" : "one") + "|" + color.ToString("X6", CultureInfo.InvariantCulture) + "|" + opacity.ToString(CultureInfo.InvariantCulture);
        if (RefillClarityFrameCache.TryGetValue(key, out MTexture[] cached)) {
            return cached;
        }

        RefillClaritySourceFrame[] sourceFrames = GetRefillClaritySourceFrames(twoDashes);
        if (sourceFrames == null || sourceFrames.Length == 0) {
            return null;
        }

        MTexture[] frames = new MTexture[sourceFrames.Length];
        for (int index = 0; index < sourceFrames.Length; index++) {
            frames[index] = CreateRefillClarityFrame(sourceFrames[index], key + "|" + index.ToString(CultureInfo.InvariantCulture), color, opacity);
        }

        RefillClarityFrameCache[key] = frames;
        return frames;
    }

    private static RefillClaritySourceFrame[] GetRefillClaritySourceFrames(bool twoDashes) {
        string key = twoDashes ? "two" : "one";
        if (RefillClaritySourceFrameCache.TryGetValue(key, out RefillClaritySourceFrame[] cached)) {
            return cached;
        }

        Assembly assembly = typeof(AkronModule).Assembly;
        string resourcePrefix = twoDashes
            ? "Celeste.Mod.Akron.Resources.RefillClarity.RefillTwo."
            : "Celeste.Mod.Akron.Resources.RefillClarity.Refill.";
        string[] resourceNames = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(resourcePrefix, StringComparison.OrdinalIgnoreCase) &&
                           name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (resourceNames.Length == 0 || Engine.Graphics?.GraphicsDevice == null) {
            return null;
        }

        RefillClaritySourceFrame[] frames = new RefillClaritySourceFrame[resourceNames.Length];
        for (int index = 0; index < resourceNames.Length; index++) {
            frames[index] = LoadRefillClaritySourceFrame(assembly, resourceNames[index]);
        }

        RefillClaritySourceFrameCache[key] = frames;
        return frames;
    }

    private static RefillClaritySourceFrame LoadRefillClaritySourceFrame(Assembly assembly, string resourceName) {
        using Stream stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null) {
            throw new InvalidOperationException("Unable to open embedded Refill Clarity resource: " + resourceName);
        }

        using Texture2D texture = Texture2D.FromStream(Engine.Graphics.GraphicsDevice, stream);
        Color[] pixels = new Color[texture.Width * texture.Height];
        texture.GetData(pixels);
        return new RefillClaritySourceFrame(texture.Width, texture.Height, pixels);
    }

    private static MTexture CreateRefillClarityFrame(RefillClaritySourceFrame source, string key, int rgb, int opacity) {
        Color[] pixels = new Color[source.Pixels.Length];
        Array.Copy(source.Pixels, pixels, source.Pixels.Length);

        byte red = (byte) ((rgb >> 16) & 0xFF);
        byte green = (byte) ((rgb >> 8) & 0xFF);
        byte blue = (byte) (rgb & 0xFF);
        for (int index = 0; index < pixels.Length; index++) {
            Color pixel = pixels[index];
            if (!IsBetterRefillGemsOutlinePixel(pixel)) {
                pixels[index] = PremultiplyTexturePixel(pixel);
                continue;
            }

            byte alpha = (byte) Math.Round(pixel.A * (opacity / 100f));
            pixels[index] = new Color(
                (byte) (red * alpha / 255),
                (byte) (green * alpha / 255),
                (byte) (blue * alpha / 255),
                alpha);
        }

        VirtualTexture texture = VirtualContent.CreateTexture("akron-refill-clarity-" + key, source.Width, source.Height, Color.Transparent);
        texture.Texture_Safe.SetData(pixels);
        RefillClarityFrameTextures.Add(texture);
        return new MTexture(texture, Vector2.Zero, source.Width, source.Height);
    }

    private static bool IsBetterRefillGemsOutlinePixel(Color pixel) {
        return pixel.A > 0 &&
               pixel.R == 255 &&
               pixel.G == 41 &&
               pixel.B == 41;
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
        public RefillClaritySourceFrame(int width, int height, Color[] pixels) {
            Width = width;
            Height = height;
            Pixels = pixels;
        }

        public int Width { get; }
        public int Height { get; }
        public Color[] Pixels { get; }
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
        orig(self);
        UpdateStateTransitionRenderSuppression();
        Scene scene = Engine.Scene;
        bool isLevelScene = scene is Level;

        if (scene is Level level) {
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

        if (overlayVisible && !Overlay.RenderImGui()) {
            Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, Engine.ScreenMatrix);
            try {
                Overlay.RenderSpriteBatchFallback();
            } finally {
                Draw.SpriteBatch.End();
            }
        } else if (inspectorPinVisible) {
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
        if (Settings.ProofModeOverlay || Settings.EndScreenHelper || Session.AttemptStatus != AkronStatus.GoldberryHardlistClean) {
            string path = AkronProof.WriteSidecar(self, "area-complete");
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
