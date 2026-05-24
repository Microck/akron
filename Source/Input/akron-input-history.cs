using System;
using System.Collections.Generic;
using System.Linq;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public sealed class AkronInputHistoryEntry {
    public AkronInputHistoryEntry(string chord, AkronInputHistoryEntryKind kind = AkronInputHistoryEntryKind.Input) {
        Chord = chord;
        Kind = kind;
        Frames = 1;
    }

    public string Chord { get; }
    public AkronInputHistoryEntryKind Kind { get; }
    public int Frames { get; set; }
}

public enum AkronInputHistoryEntryKind {
    Input,
    Event
}

public static class AkronInputHistory {
    private static readonly List<AkronInputHistoryEntry> Entries = new List<AkronInputHistoryEntry>();
    private static readonly Queue<long> InputPressTimestamps = new Queue<long>();
    private static int totalInputPresses;
    private static int maxInputsPerSecond;
    private static int previousMoveX;
    private static int previousMoveY;
    private static bool previousJump;
    private static bool previousDash;
    private static bool previousGrab;
    private static bool previousCrouchDash;
    private static bool previousTalk;
    private static bool previousConfirm;
    private static bool previousCancel;
    private static bool previousPause;
    private static bool deathPinned;
    private static bool deathPinSawNeutral;

    public static IReadOnlyList<AkronInputHistoryEntry> Current => Entries;
    public static bool DeathPinned => deathPinned;

    public static void RecordFrame() {
        string chord = FormatCurrentChord();

        if (deathPinned) {
            if (chord == "-") {
                deathPinSawNeutral = true;
            }

            if (!deathPinSawNeutral || chord == "-") {
                return;
            }

            deathPinned = false;
        }

        if (Entries.Count > 0 && Entries[0].Kind == AkronInputHistoryEntryKind.Input && Entries[0].Chord == chord) {
            Entries[0].Frames++;
        } else {
            Entries.Insert(0, new AkronInputHistoryEntry(chord));
        }

        TrimEntries();
    }

    public static void RecordTransition() {
        if (!AkronModule.Settings.InputHistoryShowTransitions) {
            return;
        }

        Entries.Insert(0, new AkronInputHistoryEntry("Transition", AkronInputHistoryEntryKind.Event));
        TrimEntries();
    }

    public static void PinOnDeath() {
        if (!AkronModule.Settings.InputHistoryPinOnDeath && !AkronModule.Settings.InputHistoryShowOnDeath) {
            return;
        }

        deathPinned = true;
        deathPinSawNeutral = FormatCurrentChord() == "-";
    }

    public static void RecordInputsPerSecondFrame() {
        AkronModuleSettings settings = AkronModule.Settings;
        int currentMoveX = Math.Sign(Input.MoveX.Value);
        int currentMoveY = Math.Sign(Input.MoveY.Value);
        bool currentJump = Input.Jump.Check;
        bool currentDash = Input.Dash.Check;
        bool currentGrab = Input.Grab.Check;
        bool currentCrouchDash = Input.CrouchDash.Check;
        bool currentTalk = Input.Talk.Check;
        bool currentConfirm = Input.MenuConfirm.Check;
        bool currentCancel = Input.MenuCancel.Check;
        bool currentPause = Input.Pause.Check;
        int pressedThisFrame = 0;

        if (settings.InputsPerSecondCountMovement) {
            if (currentMoveX < 0 && previousMoveX >= 0) {
                pressedThisFrame++;
            }
            if (currentMoveX > 0 && previousMoveX <= 0) {
                pressedThisFrame++;
            }
            if (currentMoveY < 0 && previousMoveY >= 0) {
                pressedThisFrame++;
            }
            if (currentMoveY > 0 && previousMoveY <= 0) {
                pressedThisFrame++;
            }
        }

        if (settings.InputsPerSecondCountActions) {
            pressedThisFrame += CountRisingEdge(currentJump, previousJump);
            pressedThisFrame += CountRisingEdge(currentDash, previousDash);
            pressedThisFrame += CountRisingEdge(currentGrab, previousGrab);
            pressedThisFrame += CountRisingEdge(currentCrouchDash, previousCrouchDash);
            pressedThisFrame += CountRisingEdge(currentTalk, previousTalk);
        }

        if (settings.InputsPerSecondCountMenu) {
            pressedThisFrame += CountRisingEdge(currentConfirm, previousConfirm);
            pressedThisFrame += CountRisingEdge(currentCancel, previousCancel);
            pressedThisFrame += CountRisingEdge(currentPause, previousPause);
        }

        previousMoveX = currentMoveX;
        previousMoveY = currentMoveY;
        previousJump = currentJump;
        previousDash = currentDash;
        previousGrab = currentGrab;
        previousCrouchDash = currentCrouchDash;
        previousTalk = currentTalk;
        previousConfirm = currentConfirm;
        previousCancel = currentCancel;
        previousPause = currentPause;

        long now = DateTime.UtcNow.Ticks;
        for (int index = 0; index < pressedThisFrame; index++) {
            InputPressTimestamps.Enqueue(now);
        }

        totalInputPresses += pressedThisFrame;
        CleanupInputPresses(now);
        maxInputsPerSecond = Math.Max(maxInputsPerSecond, InputPressTimestamps.Count);
    }

    public static AkronInputsPerSecondSnapshot GetInputsPerSecondSnapshot() {
        CleanupInputPresses(DateTime.UtcNow.Ticks);
        return new AkronInputsPerSecondSnapshot(InputPressTimestamps.Count, totalInputPresses, maxInputsPerSecond);
    }

    public static void ResetInputsPerSecond() {
        InputPressTimestamps.Clear();
        totalInputPresses = 0;
        maxInputsPerSecond = 0;
        previousMoveX = 0;
        previousMoveY = 0;
        previousJump = false;
        previousDash = false;
        previousGrab = false;
        previousCrouchDash = false;
        previousTalk = false;
        previousConfirm = false;
        previousCancel = false;
        previousPause = false;
    }

    public static string FormatCurrentChord() {
        List<string> parts = new List<string>();
        if (Input.MoveX.Value < 0) {
            parts.Add("L");
        } else if (Input.MoveX.Value > 0) {
            parts.Add("R");
        }

        if (Input.MoveY.Value < 0) {
            parts.Add("U");
        } else if (Input.MoveY.Value > 0) {
            parts.Add("D");
        }

        if (Input.Jump.Check) {
            parts.Add("J");
        }
        if (Input.Dash.Check) {
            parts.Add("X");
        }
        if (Input.Grab.Check) {
            parts.Add("G");
        }
        if (Input.CrouchDash.Check) {
            parts.Add("C");
        }

        return parts.Count == 0 ? "-" : string.Join("+", parts);
    }

    public static string Describe() {
        return Entries.Count == 0
            ? "empty"
            : string.Join(", ", Entries.Select(DescribeEntry));
    }

    public static string DescribeInputsPerSecond() {
        AkronInputsPerSecondSnapshot snapshot = GetInputsPerSecondSnapshot();
        return snapshot.Current.ToString(System.Globalization.CultureInfo.InvariantCulture) +
               "/" + snapshot.Total.ToString(System.Globalization.CultureInfo.InvariantCulture) +
               "/" + snapshot.Max.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static int ClampHistoryLength(int length) {
        return Calc.Clamp(length, 1, 20);
    }

    private static void TrimEntries() {
        int historyLength = ClampHistoryLength(AkronModule.Settings.InputHistoryLength);
        while (Entries.Count > historyLength) {
            Entries.RemoveAt(Entries.Count - 1);
        }
    }

    private static string DescribeEntry(AkronInputHistoryEntry entry) {
        return entry.Kind == AkronInputHistoryEntryKind.Event
            ? "[" + entry.Chord + "]"
            : entry.Chord + "x" + entry.Frames;
    }

    private static int CountRisingEdge(bool current, bool previous) {
        return current && !previous ? 1 : 0;
    }

    private static void CleanupInputPresses(long now) {
        long windowStart = now - TimeSpan.TicksPerSecond;
        while (InputPressTimestamps.Count > 0 && InputPressTimestamps.Peek() < windowStart) {
            InputPressTimestamps.Dequeue();
        }
    }
}

public readonly struct AkronInputsPerSecondSnapshot {
    public AkronInputsPerSecondSnapshot(int current, int total, int max) {
        Current = current;
        Total = total;
        Max = max;
    }

    public int Current { get; }
    public int Total { get; }
    public int Max { get; }
}
