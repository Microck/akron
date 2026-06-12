using System;
using System.Collections.Generic;
using System.Linq;
using Celeste;
using FMOD.Studio;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using Monocle;

namespace Celeste.Mod.Akron;

public partial class AkronModule {
    internal const int PlayerDashState = 2;

    private static PlayerDeadBody PlayerOnDie(On.Celeste.Player.orig_Die orig, Player self, Vector2 direction, bool evenIfInvincible, bool registerDeathInStats) {
        Level level = self.Scene as Level;
        Vector2 deathPosition = self.Center;
        Rectangle? autoKillDeathArea = pendingAutoKillDeathArea;
        bool shouldFreezeDeathSprite = Settings.NoDeathEffect && AkronPolicy.CanUse(AkronFeatureKind.DeathVisuals).Allowed;
        AkronFrozenDeathSprite frozenDeathSprite = shouldFreezeDeathSprite ? AkronFrozenDeathSprite.Capture(self) : null;

        AkronStartPos startPosRespawn = ApplyPracticeRespawnPoint(self);
        RestoreNoclipDepth(self);
        RestorePlayerVisibilityOverride(self);
        AkronActions.RestoreAutoDeafen();
        bool hazardAccuracyAllowed = IsHazardAccuracyAllowed();
        if (hazardAccuracyAllowed) {
            RecordHazardAccuracyInvalidContact(self);
        }

        if (!evenIfInvincible && (hazardAccuracyAllowed || Settings.Invincibility && TryUse(AkronFeatureKind.Invincibility))) {
            EnsureNativeAssistInvincibility();
        }

        PlayerDeadBody deadBody = orig(self, direction, evenIfInvincible, registerDeathInStats);
        bool goldenDeath = level?.Entities.OfType<Strawberry>().Any(strawberry => strawberry.Golden && strawberry.Follower.Leader != null) == true;
        if (deadBody != null && Settings.NoDeathEffect && TryUse(AkronFeatureKind.DeathVisuals) && level != null) {
            noDeathEffectBodies.Add(deadBody);
            SuppressDeathEffects(level, deadBody, frozenDeathSprite);
        }
        if (deadBody != null && Settings.NoRespawnAnimation && TryUse(AkronFeatureKind.RespawnAnimation)) {
            deadBody.Visible = false;
            deadBody.ActionDelay = Math.Min(deadBody.ActionDelay, 0.05f);
            respawnTimeElapsed[deadBody] = 0f;
        }
        if (deadBody != null && Settings.RespawnTimeModifier && TryUse(AkronFeatureKind.RespawnTime)) {
            deadBody.ActionDelay = AkronModuleSettings.ClampRespawnTimeSeconds(Settings.RespawnTimeSeconds);
            respawnTimeElapsed[deadBody] = 0f;
        }
        if (deadBody != null || self.Dead) {
            if (registerDeathInStats) {
                Session.DeathsSinceLevelLoad++;
                Session.DeathsSinceRoomTransition++;
            }
            Session.DeathStatsAfterDeathTimer = 3f;
            AkronInputHistory.PinOnDeath();
            AkronInputHistory.ResetInputsPerSecond();
            AkronPolicy.ResetAttempt("Death ended the previous attempt.");
            if (level != null) {
                MaybeShowDeathPbLossPrompt(level);
                AkronPracticeStats.NotifyRespawnTimerReset(level);
                AkronActions.RestoreSetInventoryOnDeath(level, self);
                if (autoKillDeathArea.HasValue) {
                    AkronEntityInspector.RecordLastDeath(level, deathPosition, autoKillDeathArea.Value, "AutoKillArea");
                } else {
                    AkronEntityInspector.RecordLastDeath(level, deathPosition);
                }
                AkronPracticeStats.ResetAttemptTimer(level);
            }
        }

        if (deadBody != null || self.Dead) {
            AkronInternalRecorder.NotifyDeath(level, goldenDeath);
            if (startPosRespawn != null && level != null) {
                AkronActions.RestoreStartPosAfterDeath(level, startPosRespawn);
            }
        }

        return deadBody;
    }

    private static void PlayerDeadBodyOnUpdate(On.Celeste.PlayerDeadBody.orig_Update orig, PlayerDeadBody self) {
        orig(self);
        if (Settings.NoDeathEffect && noDeathEffectBodies.Contains(self) && self.Scene is Level level) {
            SuppressDeathEffects(level, self, null);
        }

        if ((!Settings.RespawnTimeModifier && !Settings.NoRespawnAnimation) || !respawnTimeElapsed.ContainsKey(self)) {
            return;
        }

        if (self.Scene == null) {
            respawnTimeElapsed.Remove(self);
            noDeathEffectBodies.Remove(self);
            return;
        }

        float elapsedStep = Settings.RespawnTimeIgnoreSpeedhack ? Engine.RawDeltaTime : Engine.DeltaTime;
        float elapsed = respawnTimeElapsed[self] + Math.Max(0f, elapsedStep);
        respawnTimeElapsed[self] = elapsed;
        float target = Settings.NoRespawnAnimation ? 0.05f : AkronModuleSettings.ClampRespawnTimeSeconds(Settings.RespawnTimeSeconds);
        if (elapsed >= target) {
            PlayerDeadBodyEndMethod?.Invoke(self, Array.Empty<object>());
            respawnTimeElapsed.Remove(self);
            noDeathEffectBodies.Remove(self);
        }
    }

    private static void SuppressDeathEffects(Level level, PlayerDeadBody deadBody = null, AkronFrozenDeathSprite frozenDeathSprite = null) {
        level.Particles.Clear();
        level.ParticlesFG.Clear();
        if (deadBody != null) {
            deadBody.Visible = false;
            if (PlayerDeadBodyDeathEffectField?.GetValue(deadBody) is DeathEffect deathEffect) {
                deathEffect.RemoveSelf();
                PlayerDeadBodyDeathEffectField.SetValue(deadBody, null);
            }
        }

        if (frozenDeathSprite != null && frozenDeathSprite.Scene == null) {
            level.Add(frozenDeathSprite);
        }

        foreach (Entity entity in level.Entities.ToList()) {
            string name = entity.GetType().Name;
            if (name.Contains("DeathEffect") || name.Contains("RespawnDebris")) {
                entity.RemoveSelf();
            }
        }
    }

    private static AkronStartPos ApplyPracticeRespawnPoint(Player player) {
        if (player.Scene is not Level level) {
            return null;
        }

        if (Settings.RespawnAtStartPos) {
            AkronStartPos startPos = AkronActions.GetDeathRespawnStartPos(level, player.Position);
            if (startPos != null &&
                string.Equals(startPos.Room, level.Session.Level) &&
                (string.IsNullOrWhiteSpace(startPos.AreaSid) || string.Equals(startPos.AreaSid, level.Session.Area.GetSID())) &&
                TryUse(AkronFeatureKind.StartPosTools)) {
                // Keep vanilla death accounting and visuals, then reload the
                // StartPos state at frame end so entities, sounds, and player
                // state match the latest loaded practice snapshot.
                level.Session.RespawnPoint = startPos.Position;
                return startPos;
            }
        }

        return null;
    }

    private static void PlayerOnUpdate(On.Celeste.Player.orig_Update orig, Player self) {
        if (!Settings.Noclip || !TryUse(AkronFeatureKind.Noclip)) {
            RestoreNoclipDepth();
            bool wasGrounded = self.OnGround();
            int dashesBefore = self.Dashes;
            float staminaBefore = self.Stamina;
            orig(self);
            ApplyGroundRefillRulesAfterPlayerUpdate(self, wasGrounded, dashesBefore, staminaBefore);
            ApplyPlayerVisibilityOverride(self);
            TrackHazardAccuracy(self);
            return;
        }

        ApplyNoclip(self);
    }

    private static void CelesteOnFreeze(On.Celeste.Celeste.orig_Freeze orig, float time) {
        if (Instance?._Settings != null &&
            Settings.NoFreezeFrames &&
            ShouldSuppressFreezeFrames(time) &&
            TryUse(AkronFeatureKind.FreezeFrames)) {
            return;
        }

        orig(time);
    }

    public static bool ShouldSuppressFreezeFrames(float time) {
        // Crystal-heart collection and other scripted sequences use longer
        // freezes as cutscene timing gates. The option is meant to remove the
        // short action-hit pauses without skipping those scripted beats.
        return time < 0.15f;
    }

    private static void EnsureOverlay(Scene scene) {
        if (scene == null) {
            return;
        }

        if (Overlay != null && Overlay.Scene == scene) {
            return;
        }

        Overlay = scene.Tracker.GetEntity<AkronOverlay>();
        if (Overlay == null) {
            Overlay = new AkronOverlay();
            scene.Add(Overlay);
        }
    }


    private static void ApplyEnabledRuntimeFeatures(Level level) {
        if (!Settings.Noclip) {
            RestoreNoclipDepth();
            RestorePlayerVisibilityOverride();
        }

        bool hazardAccuracyAllowed = IsHazardAccuracyAllowed();
        if (!Settings.Invincibility && !hazardAccuracyAllowed) {
            RestoreNativeAssistInvincibility();
        }

        Player player = level.Tracker.GetEntity<Player>();
        AkronRuntimeOptions.Apply(level, player);
        if (player == null) {
            return;
        }

#pragma warning disable CS0618
        if (Session.TimescaleEnabled) {
            Engine.TimeRate = Session.TimescaleMultiplier;
        }
#pragma warning restore CS0618

        if (Settings.InfiniteStamina && TryUse(AkronFeatureKind.InfiniteStamina)) {
            player.Stamina = 110f;
        }

        ApplyDashCountOverride(player, false);

        if (Settings.InfiniteDash && TryUse(AkronFeatureKind.InfiniteDash)) {
            int maxDashes = EffectiveDashCountLimit(player);
            if (maxDashes <= 0) {
                player.Dashes = 0;
            } else {
                player.Dashes = Math.Max(player.Dashes, maxDashes);
            }
        }

        if (hazardAccuracyAllowed) {
            AkronPolicy.RecordFeatureUse(AkronFeatureKind.HazardAccuracy);
            EnsureNativeAssistInvincibility();
            RescueInvinciblePlayerFromBottomlessFall(level, player, true);
        } else if (Settings.Invincibility && TryUse(AkronFeatureKind.Invincibility)) {
            EnsureNativeAssistInvincibility();
            RescueInvinciblePlayerFromBottomlessFall(level, player, false);
        } else {
            RestoreNativeAssistInvincibility();
        }

        ApplyAutoKill(level, player);
        ApplyAutoDeafen(level, player);
        ApplyStartPosMousePlacement(level, player);
        ApplyClickTeleport(level, player);
        UpdateCursorZoom(level);
        ApplyTransitionSpeed(level);
        ApplyVisualPlayerOverrides(player);

        if (ShouldApplyAnyVisualNoiseSuppression() && TryUse(AkronFeatureKind.ReducedVisualNoise)) {
            ApplyReducedVisualNoise(level);
        }
    }

    private static void ApplyJumpHackAfterPlayerUpdate(Level level) {
        Player player = level.Tracker.GetEntity<Player>();
        if (player == null || player.Dead || player.Scene == null) {
            jumpHackAirJumpsUsed = 0;
            return;
        }

        if (!Settings.JumpHack) {
            jumpHackAirJumpsUsed = 0;
            return;
        }

        if (player.OnGround()) {
            jumpHackAirJumpsUsed = 0;
            return;
        }

        if (ShouldPreserveVanillaJump(player) ||
            !player.InControl ||
            !player.CanUnDuck ||
            !Input.Jump.Pressed ||
            TalkComponent.PlayerOver != null && Input.Talk.Pressed) {
            return;
        }

        int state = player.StateMachine.State;
        Vector2 dashDirection = player.DashDir;
        if (ShouldSkipAirJumpForDashDirection(state, dashDirection, Settings.JumpHackAllowVerticalDashJumps)) {
            return;
        }

        if (!Settings.JumpHackInfinite &&
            jumpHackAirJumpsUsed >= AkronModuleSettings.ClampJumpHackExtraJumps(Settings.JumpHackExtraJumps)) {
            return;
        }

        if (!TryUse(AkronFeatureKind.MovementStatMutation)) {
            return;
        }

        if (ShouldUseSuperJumpForAirJump(state, dashDirection)) {
            InvokePlayerSuperJump(player);
        } else {
            player.Jump();
        }
        jumpHackAirJumpsUsed++;
    }

    private static bool ShouldPreserveVanillaJump(Player player) {
        return ShouldPreserveVanillaJumpForAirJump(GetPlayerJumpGraceTimer(player));
    }

    private static float GetPlayerJumpGraceTimer(Player player) {
        if (player == null || PlayerJumpGraceTimerField?.GetValue(player) is not float jumpGraceTimer) {
            return 0f;
        }

        return jumpGraceTimer;
    }

    internal static bool ShouldPreserveVanillaJumpForAirJump(float jumpGraceTimer) {
        return jumpGraceTimer > 0f;
    }

    internal static bool ShouldSkipAirJumpForDashDirection(int playerState, Vector2 dashDirection, bool allowVerticalDashJumps) {
        return playerState == PlayerDashState &&
               !allowVerticalDashJumps &&
               !IsVanillaDashJumpDirection(dashDirection);
    }

    internal static bool ShouldUseSuperJumpForAirJump(int playerState, Vector2 dashDirection) {
        return playerState == PlayerDashState && IsVanillaDashJumpDirection(dashDirection);
    }

    internal static bool IsVanillaDashJumpDirection(Vector2 dashDirection) {
        return Math.Abs(dashDirection.X) > 0.001f && dashDirection.Y >= -0.001f;
    }

    private static void InvokePlayerSuperJump(Player player) {
        if (PlayerSuperJumpMethod == null) {
            player.Jump();
            return;
        }

        PlayerSuperJumpMethod.Invoke(player, Array.Empty<object>());
    }

    private static void ApplyDashCountOverride(Player player, bool refillCurrent) {
        if (player == null || !Settings.DashCountOverride) {
            return;
        }

        if (!TryUse(AkronFeatureKind.DashCountOverride)) {
            return;
        }

        int target = AkronModuleSettings.ClampDashCountOverride(Settings.DashCountOverrideValue);
        if (refillCurrent) {
            player.Dashes = target;
            return;
        }

        if (player.Dashes > target) {
            player.Dashes = target;
        }
    }

    private static void ApplyGroundRefillRulesAfterPlayerUpdate(Player player, bool wasGrounded, int dashesBefore, float staminaBefore) {
        if (player == null ||
            player.Dead ||
            !Settings.GroundRefillRules ||
            Settings.GroundDashRefill && Settings.GroundStaminaRefill) {
            return;
        }

        bool grounded = wasGrounded || player.OnGround();
        if (!grounded || !TryUse(AkronFeatureKind.GroundRefillRules)) {
            return;
        }

        if (!Settings.GroundDashRefill && player.Dashes > dashesBefore) {
            player.Dashes = dashesBefore;
        }

        if (!Settings.GroundStaminaRefill && player.Stamina > staminaBefore) {
            player.Stamina = staminaBefore;
        }
    }

    private static int EffectiveDashCountLimit(Player player) {
        if (Settings.DashCountOverride) {
            return AkronModuleSettings.ClampDashCountOverride(Settings.DashCountOverrideValue);
        }

        return Math.Max(1, player?.Inventory.Dashes ?? 1);
    }

    private static bool PlayerOnRefillDash(On.Celeste.Player.orig_RefillDash orig, Player self) {
        if (!Settings.DashCountOverride) {
            return orig(self);
        }

        if (!TryUse(AkronFeatureKind.DashCountOverride)) {
            return orig(self);
        }

        int target = AkronModuleSettings.ClampDashCountOverride(Settings.DashCountOverrideValue);
        if (self.Dashes < target) {
            self.Dashes = target;
            return true;
        }

        return false;
    }

    private static void PlayerOnAdded(On.Celeste.Player.orig_Added orig, Player self, Scene scene) {
        orig(self, scene);
        AkronInternalRecorder.NotifyPlayerRespawn(scene as Level);
        AkronPracticeCounters.OnRespawn(scene as Level);
        AkronAutosave.NotifyRespawn(scene as Level);
        if (Settings.DashCountRefillOnRoomEntry) {
            ApplyDashCountOverride(self, true);
        } else {
            ApplyDashCountOverride(self, false);
        }
        EnsureRespawnCameraContainsPlayer(scene as Level, self);
    }

    private static void PlayerOnTransition(On.Celeste.Player.orig_OnTransition orig, Player self) {
        AkronInternalRecorder.NotifyRoomLeaving(self.Scene as Level);
        orig(self);
        AkronInternalRecorder.NotifyRoomEntered(self.Scene as Level);
        AkronInputHistory.RecordTransition();
        Session.DeathsSinceRoomTransition = 0;
        if (Settings.DashCountRefillOnTransition) {
            ApplyDashCountOverride(self, true);
        } else {
            ApplyDashCountOverride(self, false);
        }
    }

    private static void PlayerOnRender(On.Celeste.Player.orig_Render orig, Player self) {
        if (Settings.NoStaminaFlash && TryUse(AkronFeatureKind.ReducedVisualNoise)) {
            self.flash = true;
        }

        if (TryRenderMadelineSyncedDeathEffect(self)) {
            return;
        }

        orig(self);
    }

    private static void StrawberryOnCollect(On.Celeste.Strawberry.orig_OnCollect orig, Strawberry self) {
        bool golden = self.Golden;
        Level level = self.Scene as Level;
        orig(self);
        if (level != null) {
            AkronPracticeStats.NotifyRoomStrawberryCollected();
        }
        AkronInternalRecorder.NotifyBerryCollect(level, golden);
    }

    private static void PlayerOnDashBegin(On.Celeste.Player.orig_DashBegin orig, Player self) {
        preRedirectDashAim = Input.LastAim;
        if (TryRunMadelineSyncedDashParticles(orig, self)) {
            return;
        }

        orig(self);
    }

    private static void PlayerDashCoroutineIlHook(ILContext il) {
        ILCursor cursor = new ILCursor(il);
        if (!cursor.TryGotoNext(MoveType.After, instruction => instruction.MatchLdfld<Player>("lastAim"))) {
            Logger.Log(LogLevel.Warn, nameof(AkronModule), "Could not find Player.lastAim in DashCoroutine; down-dash redirect guard is disabled.");
            return;
        }

        cursor.EmitDelegate<Func<Vector2, Vector2>>(ApplyDownDashRedirectGuard);
    }

    private static Vector2 ApplyDownDashRedirectGuard(Vector2 redirectedAim) {
        AkronPreventDownDashRedirectMode mode = Settings.PreventDownDashRedirects;
        if (!Settings.PreventDownDashRedirectsEnabled ||
            global::Celeste.SaveData.Instance?.Assists.ThreeSixtyDashing == true ||
            preRedirectDashAim.Y <= 0.01f ||
            Math.Abs(redirectedAim.Y) > 0.01f) {
            return redirectedAim;
        }

        Vector2 guardedAim = redirectedAim;
        guardedAim.Y = preRedirectDashAim.Y;
        if (mode == AkronPreventDownDashRedirectMode.Normal && Math.Abs(Input.Aim.PreviousValue.X) <= 0.01f) {
            guardedAim.X = 0f;
        }

        if (guardedAim != redirectedAim && TryUse(AkronFeatureKind.InputAssistShortcut)) {
            return guardedAim.EightWayNormal();
        }

        return redirectedAim;
    }

    private static void LookoutRoutineIlHook(ILContext il) {
        ILCursor cursor = new ILCursor(il);
        int patched = 0;
        while (cursor.TryGotoNext(MoveType.After, instruction => instruction.MatchLdcR4(800f))) {
            cursor.EmitDelegate<Func<float, float>>(ApplyFastLookoutMultiplier);
            patched++;
        }

        cursor.Index = 0;
        while (cursor.TryGotoNext(MoveType.After, instruction => instruction.MatchLdcR4(240f))) {
            cursor.EmitDelegate<Func<float, float>>(ApplyFastLookoutMultiplier);
            patched++;
        }

        if (patched == 0) {
            Logger.Log(LogLevel.Warn, nameof(AkronModule), "Could not find Lookout movement constants; Fast Lookout is disabled.");
        }
    }

    private static float ApplyFastLookoutMultiplier(float vanillaValue) {
        if (!Settings.FastLookout ||
            Settings.FastLookoutHold?.Check != true ||
            !AkronPolicy.CanUse(AkronFeatureKind.FastLookout).Allowed) {
            return vanillaValue;
        }

        return vanillaValue * AkronModuleSettings.ClampFastLookoutMultiplier(Settings.FastLookoutMultiplier);
    }

    private static void PlayerOnJump(On.Celeste.Player.orig_Jump orig, Player self, bool particles, bool playSfx) {
        orig(self, particles, playSfx);
        AkronPracticeCounters.OnPlayerJump(self);
    }

    private static void PlayerOnSuperJump(On.Celeste.Player.orig_SuperJump orig, Player self) {
        orig(self);
        AkronPracticeCounters.OnPlayerJump(self);
    }

    private static FMOD.RESULT EventDescriptionOnCreateInstance(On.FMOD.Studio.EventDescription.orig_createInstance orig, EventDescription self, out EventInstance instance) {
        FMOD.RESULT result = orig(self, out instance);
        AkronEarAid.ApplyVolume(self, instance);
        return result;
    }
}
