using System;
using Celeste;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.Akron;

[Flags]
internal enum AkronStartPosInputFlags {
    None = 0,
    MoveLeft = 1 << 0,
    MoveRight = 1 << 1,
    MoveUp = 1 << 2,
    MoveDown = 1 << 3,
    AimLeft = 1 << 4,
    AimRight = 1 << 5,
    AimUp = 1 << 6,
    AimDown = 1 << 7,
    Jump = 1 << 8,
    Dash = 1 << 9,
    Grab = 1 << 10,
    CrouchDash = 1 << 11,
    Talk = 1 << 12,
    Pause = 1 << 13
}

internal sealed class AkronStartPosInputWait {
    private AkronStartPosInputFlags previousInput;

    public bool Active { get; private set; }
    public bool WaitingForWipe { get; private set; }

    public void Begin(AkronStartPosInputFlags input, bool waitingForWipe) {
        previousInput = input;
        WaitingForWipe = waitingForWipe;
        Active = true;
    }

    public void CompleteWipe(AkronStartPosInputFlags input) {
        if (!Active || !WaitingForWipe) {
            return;
        }

        previousInput = input;
        WaitingForWipe = false;
    }

    public bool Advance(AkronStartPosInputFlags input) {
        if (!Active || WaitingForWipe) {
            return false;
        }

        AkronStartPosInputFlags newlyPressed = input & ~previousInput;
        previousInput = input;
        if (newlyPressed == AkronStartPosInputFlags.None) {
            return false;
        }

        Clear();
        return true;
    }

    public void Clear() {
        previousInput = AkronStartPosInputFlags.None;
        WaitingForWipe = false;
        Active = false;
    }
}

public static partial class AkronActions {
    private static readonly AkronStartPosInputWait StartPosInputWait = new AkronStartPosInputWait();
    private static Level startPosInputWaitLevel;

    internal static void BeginStartPosInputWait(Level level, bool waitingForWipe) {
        ClearStartPosInputWait();
        if (level == null || !AkronModule.Settings.StartPosWaitForInput) {
            return;
        }

        startPosInputWaitLevel = level;
        StartPosInputWait.Begin(CaptureStartPosInput(), waitingForWipe);
        if (!waitingForWipe) {
            ShowStartPosInputWaitToast(level);
        }
    }

    internal static void CompleteStartPosInputWaitWipe(Level level) {
        if (!ReferenceEquals(level, startPosInputWaitLevel) || !StartPosInputWait.WaitingForWipe) {
            return;
        }

        StartPosInputWait.CompleteWipe(CaptureStartPosInput());
        ShowStartPosInputWaitToast(level);
    }

    internal static bool UpdateStartPosInputWait(Level level) {
        if (!StartPosInputWait.Active) {
            return false;
        }
        if (!AkronModule.Settings.StartPosWaitForInput || !ReferenceEquals(level, startPosInputWaitLevel)) {
            ClearStartPosInputWait();
            return false;
        }

        // A suppressed wipe can intentionally omit its completion callback.
        // Treat the missing wipe as complete so that configuration cannot leave
        // a StartPos permanently stuck in the pre-input phase.
        if (StartPosInputWait.WaitingForWipe && level.Wipe == null) {
            CompleteStartPosInputWaitWipe(level);
        }

        if (StartPosInputWait.Advance(CaptureStartPosInput())) {
            startPosInputWaitLevel = null;
            return false;
        }

        UpdateStartPosInputWaitPresentation(level);
        return true;
    }

    internal static void ClearStartPosInputWait() {
        StartPosInputWait.Clear();
        startPosInputWaitLevel = null;
    }

    private static void UpdateStartPosInputWaitPresentation(Level level) {
        // Keep backdrop-only presentation alive without advancing entities,
        // collision, coroutines, or any other gameplay-owned Level.Update work.
        level.Wipe?.Update(level);
        level.HiresSnow?.Update(level);
        level.Foreground.Update(level);
        level.Background.Update(level);
        AkronRuntimeOptions.HoldSceneClockForSkippedLevelUpdate(level);
    }

    private static AkronStartPosInputFlags CaptureStartPosInput() {
        AkronStartPosInputFlags input = AkronStartPosInputFlags.None;
        AddDirectionFlags(Input.MoveX.Value, Input.MoveY.Value, ref input,
            AkronStartPosInputFlags.MoveLeft, AkronStartPosInputFlags.MoveRight,
            AkronStartPosInputFlags.MoveUp, AkronStartPosInputFlags.MoveDown);

        Vector2 aim = Input.Aim.Value;
        AddDirectionFlags(aim.X, aim.Y, ref input,
            AkronStartPosInputFlags.AimLeft, AkronStartPosInputFlags.AimRight,
            AkronStartPosInputFlags.AimUp, AkronStartPosInputFlags.AimDown);

        AddButtonFlag(Input.Jump.Check, AkronStartPosInputFlags.Jump, ref input);
        AddButtonFlag(Input.Dash.Check, AkronStartPosInputFlags.Dash, ref input);
        AddButtonFlag(Input.Grab.Check, AkronStartPosInputFlags.Grab, ref input);
        AddButtonFlag(Input.CrouchDash.Check, AkronStartPosInputFlags.CrouchDash, ref input);
        AddButtonFlag(Input.Talk.Check, AkronStartPosInputFlags.Talk, ref input);
        AddButtonFlag(Input.Pause.Check, AkronStartPosInputFlags.Pause, ref input);
        return input;
    }

    private static void AddDirectionFlags(
        float x,
        float y,
        ref AkronStartPosInputFlags input,
        AkronStartPosInputFlags left,
        AkronStartPosInputFlags right,
        AkronStartPosInputFlags up,
        AkronStartPosInputFlags down
    ) {
        input |= x < 0f ? left : x > 0f ? right : AkronStartPosInputFlags.None;
        input |= y < 0f ? up : y > 0f ? down : AkronStartPosInputFlags.None;
    }

    private static void AddButtonFlag(bool active, AkronStartPosInputFlags flag, ref AkronStartPosInputFlags input) {
        if (active) {
            input |= flag;
        }
    }

    private static void ShowStartPosInputWaitToast(Level level) {
        level.Add(new AkronToast("StartPos loaded. Press a gameplay input to start.", durationSeconds: 2.8f));
    }
}
