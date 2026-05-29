using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using Celeste.Mod.Akron.MotionSmoothing.Utilities;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.Akron.MotionSmoothing.FrameUncap;

public class DecoupledGameTick : ToggleableFeature<DecoupledGameTick>, IFrameUncapStrategy
{
    private const BindingFlags AllFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static readonly FieldInfo WorstCaseSleepPrecisionField = typeof(Game).GetField("worstCaseSleepPrecision", AllFlags);
    private static readonly MethodInfo UpdateEstimatedSleepPrecisionMethod = typeof(Game).GetMethod("UpdateEstimatedSleepPrecision", AllFlags);
    private static readonly FieldInfo CurrentAdapterField = typeof(Game).GetField("currentAdapter", AllFlags);
    private static readonly FieldInfo TextInputControlDownField = typeof(Game).GetField("textInputControlDown", AllFlags);
    private static readonly FieldInfo TextInputSuppressField = typeof(Game).GetField("textInputSuppress", AllFlags);
    private static readonly FieldInfo GameTimeField = typeof(Game).GetField("gameTime", AllFlags);
    private static readonly MethodInfo AssertNotDisposedMethod = typeof(Game).GetMethod("AssertNotDisposed", AllFlags);
    private static readonly MethodInfo UpdateMethod = typeof(Game).GetMethod("Update", AllFlags, null, new[] { typeof(GameTime) }, null);
    private static readonly FieldInfo UpdateFrameLagField = typeof(Game).GetField("updateFrameLag", AllFlags);
    private static readonly FieldInfo SuppressDrawField = typeof(Game).GetField("suppressDraw", AllFlags);
    private static readonly MethodInfo BeginDrawMethod = typeof(Game).GetMethod("BeginDraw", AllFlags, null, Type.EmptyTypes, null);
    private static readonly MethodInfo DrawMethod = typeof(Game).GetMethod("Draw", AllFlags, null, new[] { typeof(GameTime) }, null);
    private static readonly MethodInfo EndDrawMethod = typeof(Game).GetMethod("EndDraw", AllFlags, null, Type.EmptyTypes, null);
    private static readonly FieldInfo GameTimerField = typeof(Game).GetField("gameTimer", AllFlags);
    private static readonly FieldInfo MaxElapsedTimeField = typeof(Game).GetField("MaxElapsedTime", AllFlags);
    private static readonly PropertyInfo MaxElapsedTimeProperty = typeof(Game).GetProperty("MaxElapsedTime", AllFlags);
    private static readonly Type FnaPlatformType = typeof(Game).Assembly.GetType("Microsoft.Xna.Framework.FNAPlatform");
    private static readonly MethodInfo PollEventsMethod = FnaPlatformType?.GetMethod("PollEvents", AllFlags);

    // This is how fast Update should be called
    public TimeSpan TargetUpdateElapsedTime { get; set; } = GameUtils.UpdateElapsedTime;

    // This is how fast Draw should be called
    public TimeSpan TargetDrawElapsedTime { get; set; } = GameUtils.UpdateElapsedTime;

    // This is what will be passed to Update, that gets used to calculate DeltaTime
    public TimeSpan TargetUpdateDeltaTime { get; set; } = GameUtils.UpdateElapsedTime;

    private readonly Game _game = Engine.Instance;

    private TimeSpan _accumulatedElapsedTime;
    private TimeSpan _accumulatedUpdateElapsedTime;
    private TimeSpan _accumulatedDrawElapsedTime;
    private long _previousTicks;

    protected override void Hook()
    {
        base.Hook();

        AddHook(new Hook(typeof(Game).GetMethod(nameof(Game.Tick))!, GameTickHook));

        MainThreadHelper.Schedule(() =>
        {
            using (new DetourConfigContext(new DetourConfig(
                       "MotionSmoothingModule.DecoupledGameTick.LevelUpdateHook",
                       after: new List<string> { "SpeedMod" }
                   )).Use())
            {
                IL.Celeste.Level.UpdateTime += LevelUpdateTimeHook;
            }
        });
    }

    protected override void Unhook()
    {
        base.Unhook();

        MainThreadHelper.Schedule(() => { IL.Celeste.Level.UpdateTime -= LevelUpdateTimeHook; });
    }

    public void SetTargetFramerate(double updateFramerate, double drawFramerate)
    {
        TargetDrawElapsedTime = new TimeSpan((long)Math.Round(10_000_000.0 / drawFramerate));
        TargetUpdateElapsedTime = new TimeSpan((long)Math.Round(10_000_000.0 / updateFramerate));
        TargetUpdateDeltaTime = TargetUpdateElapsedTime;
    }

    public void SetTargetDeltaTime(double deltaTime)
    {
        TargetUpdateDeltaTime = new TimeSpan((long)Math.Round(10_000_000.0 / deltaTime));
    }

    private static void LevelUpdateTimeHook(ILContext il)
    {
        var cursor = new ILCursor(il);

        if (cursor.TryGotoNext(MoveType.After, instr => instr.MatchCall<Engine>("get_RawDeltaTime")))
        {
            static double GetDeltaTime(float oldDt)
            {
                if (MotionSmoothingModule.Settings.GameSpeedModified)
                    return Instance.TargetUpdateElapsedTime.TotalSeconds;
                return oldDt;
            }

            cursor.EmitDelegate(GetDeltaTime);
        }
    }

    private void Tick()
    {
        AdvanceElapsedTime();

        // Figure out how much time we need to wait until the next draw and/or update
        var updateTimeLeft = TargetUpdateElapsedTime - _accumulatedUpdateElapsedTime;
        var drawTimeLeft = TargetDrawElapsedTime - _accumulatedDrawElapsedTime;
        var targetElapsedTime = TimeSpan.FromTicks(Math.Min(updateTimeLeft.Ticks, drawTimeLeft.Ticks));

        // Wait for that amount of time
        while (_accumulatedElapsedTime + GetWorstCaseSleepPrecision(_game) < targetElapsedTime)
        {
            Thread.Sleep(1);
            UpdateEstimatedSleepPrecision(_game, AdvanceElapsedTime());
        }

        while (_accumulatedElapsedTime < targetElapsedTime)
        {
            Thread.SpinWait(1);
            AdvanceElapsedTime();
        }

        // Cap the accumulated time
        var maxElapsedTime = GetMaxElapsedTime();
        if (_accumulatedElapsedTime >= maxElapsedTime)
            _accumulatedElapsedTime = maxElapsedTime;

        // Update if ready
        if (_accumulatedUpdateElapsedTime >= TargetUpdateElapsedTime)
        {
            // Poll events. The upstream mod calls FNA internals directly; Akron
            // uses reflection because this build publicizes Celeste, not FNA.
            PollEvents(_game);

            // Update
            // Make sure to use the TargetUpdateDeltaTime for this, since this is how DeltaTime is calculated
            GameTime gameTime = GetGameTime(_game);
            TimeSpan totalGameTime = gameTime.TotalGameTime;
            bool isRunningSlowly = gameTime.IsRunningSlowly;
            var updates = 0;
            while (_accumulatedUpdateElapsedTime >= TargetUpdateElapsedTime)
            {
                totalGameTime += TargetUpdateDeltaTime;
                gameTime = new GameTime(totalGameTime, TargetUpdateDeltaTime, isRunningSlowly);
                SetGameTime(_game, gameTime);
                _accumulatedUpdateElapsedTime -= TargetUpdateElapsedTime;
                ++updates;
                AssertNotDisposed(_game);
                InvokeUpdate(_game, gameTime);
            }

            // Handle lag
            SetUpdateFrameLag(_game, GetUpdateFrameLag(_game) + Math.Max(0, updates - 1));
            if (gameTime.IsRunningSlowly)
            {
                if (GetUpdateFrameLag(_game) == 0)
                    isRunningSlowly = false;
            }
            else if (GetUpdateFrameLag(_game) >= 5)
                isRunningSlowly = true;

            if (updates == 1 && GetUpdateFrameLag(_game) > 0)
                SetUpdateFrameLag(_game, GetUpdateFrameLag(_game) - 1);

            SetGameTime(_game, new GameTime(totalGameTime, TimeSpan.FromTicks(TargetUpdateDeltaTime.Ticks * updates), isRunningSlowly));
        }

        // Draw if ready
        if (_accumulatedDrawElapsedTime >= TargetDrawElapsedTime)
        {
            // Drawing doesn't need to be as accurate as updating, so we can just draw whenever we're ready
            if (GetSuppressDraw(_game))
            {
                SetSuppressDraw(_game, false);
            }
            else
            {
                // Engine.FPS is calculated in Draw, and ends up being 120+, so this fixes that
                GameTime currentGameTime = GetGameTime(_game);
                GameTime gameTime = new GameTime(currentGameTime.TotalGameTime, TargetUpdateDeltaTime, currentGameTime.IsRunningSlowly);
                SetGameTime(_game, gameTime);

                // Ensure DeltaTime is accurate for drawing
                Engine.RawDeltaTime = (float)_accumulatedDrawElapsedTime.TotalSeconds;
                Engine.DeltaTime = GameUtils.CalculateDeltaTime(Engine.RawDeltaTime);

                if (!InvokeBeginDraw(_game))
                    return;
                InvokeDraw(_game, gameTime);
                InvokeEndDraw(_game);

                _accumulatedDrawElapsedTime = TimeSpan.Zero;
            }
        }
    }

    private TimeSpan AdvanceElapsedTime()
    {
        var ticks = GetGameTimer(_game).Elapsed.Ticks;
        var timeSpan = TimeSpan.FromTicks(ticks - _previousTicks);
        _accumulatedElapsedTime += timeSpan;
        _accumulatedUpdateElapsedTime += timeSpan;
        _accumulatedDrawElapsedTime += timeSpan;
        _previousTicks = ticks;
        return timeSpan;
    }

    // ReSharper disable once InconsistentNaming
    private delegate void orig_Tick(Game self);

#pragma warning disable CL0003
    private static void GameTickHook(orig_Tick orig, Game self)
    {
        Instance.Tick();
    }
#pragma warning restore CL0003

    private static TimeSpan GetWorstCaseSleepPrecision(Game game)
    {
        return WorstCaseSleepPrecisionField?.GetValue(game) as TimeSpan? ?? TimeSpan.Zero;
    }

    private static void UpdateEstimatedSleepPrecision(Game game, TimeSpan timeSpan)
    {
        UpdateEstimatedSleepPrecisionMethod?.Invoke(game, new object[] { timeSpan });
    }

    private static TimeSpan GetMaxElapsedTime()
    {
        if (MaxElapsedTimeField?.GetValue(null) is TimeSpan fieldValue)
            return fieldValue;
        if (MaxElapsedTimeProperty?.GetValue(null) is TimeSpan propertyValue)
            return propertyValue;
        return TimeSpan.FromMilliseconds(500);
    }

    private static void PollEvents(Game game)
    {
        if (PollEventsMethod == null || CurrentAdapterField == null || TextInputControlDownField == null || TextInputSuppressField == null)
            return;

        object currentAdapter = CurrentAdapterField.GetValue(game);
        object textInputSuppress = TextInputSuppressField.GetValue(game);
        object[] args = {
            game,
            currentAdapter,
            TextInputControlDownField.GetValue(game),
            textInputSuppress
        };
        PollEventsMethod.Invoke(null, args);
        CurrentAdapterField.SetValue(game, args[1]);
        TextInputSuppressField.SetValue(game, args[3]);
    }

    private static GameTime GetGameTime(Game game)
    {
        return (GameTime)GameTimeField.GetValue(game);
    }

    private static void SetGameTime(Game game, GameTime gameTime)
    {
        GameTimeField.SetValue(game, gameTime);
    }

    private static void AssertNotDisposed(Game game)
    {
        AssertNotDisposedMethod?.Invoke(game, Array.Empty<object>());
    }

    private static void InvokeUpdate(Game game, GameTime gameTime)
    {
        UpdateMethod.Invoke(game, new object[] { gameTime });
    }

    private static int GetUpdateFrameLag(Game game)
    {
        return UpdateFrameLagField?.GetValue(game) as int? ?? 0;
    }

    private static void SetUpdateFrameLag(Game game, int value)
    {
        UpdateFrameLagField?.SetValue(game, value);
    }

    private static bool GetSuppressDraw(Game game)
    {
        return SuppressDrawField?.GetValue(game) as bool? ?? false;
    }

    private static void SetSuppressDraw(Game game, bool value)
    {
        SuppressDrawField?.SetValue(game, value);
    }

    private static bool InvokeBeginDraw(Game game)
    {
        return BeginDrawMethod?.Invoke(game, Array.Empty<object>()) as bool? ?? true;
    }

    private static void InvokeDraw(Game game, GameTime gameTime)
    {
        DrawMethod.Invoke(game, new object[] { gameTime });
    }

    private static void InvokeEndDraw(Game game)
    {
        EndDrawMethod?.Invoke(game, Array.Empty<object>());
    }

    private static Stopwatch GetGameTimer(Game game)
    {
        return (Stopwatch)GameTimerField.GetValue(game);
    }
}
