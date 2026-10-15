using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

// MInput.Disabled guards buttons at read time, but directional inputs cache
// their values before Level.Update. Mask those caches without polling twice.
internal static class AkronGameplayInputScope {
    private static readonly FieldInfo AxisValue = CachedField(typeof(VirtualAxis), "Value");
    private static readonly FieldInfo AxisPrevious = CachedField(typeof(VirtualAxis), "PreviousValue");
    private static readonly FieldInfo JoystickValue = CachedField(typeof(VirtualJoystick), "Value");
    private static readonly FieldInfo JoystickPrevious = CachedField(typeof(VirtualJoystick), "PreviousValue");

    private static FieldInfo CachedField(Type type, string property) =>
        type.GetField("<" + property + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(type.FullName, property);

    internal static void Run(On.Celeste.Level.orig_Update update, Level level) {
        Run(() => update(level));
    }

    internal static void Run(Action update) {
        VirtualInput[] inputs;
        lock (MInput.VirtualInputs) {
            inputs = MInput.VirtualInputs.ToArray();
        }
        Run(inputs, update);
    }

    internal static void Run(IEnumerable<VirtualInput> inputs, Action update) {
        var snapshots = new List<Snapshot>();
        // Capture before mutating, and retain object references even if the
        // callback changes the registry. This also makes nested scopes safe.
        foreach (VirtualInput input in inputs) {
            if (input is VirtualIntegerAxis or VirtualAxis or VirtualJoystick)
                snapshots.Add(new Snapshot(input));
        }
        bool previousDisabled = MInput.Disabled;
        try {
            MInput.Disabled = true;
            foreach (Snapshot snapshot in snapshots) snapshot.Apply(true);
            update();
        } finally {
            try {
                foreach (Snapshot snapshot in snapshots) snapshot.Apply(false);
            } finally {
                MInput.Disabled = previousDisabled;
            }
        }
    }

    private readonly struct Snapshot {
        private readonly VirtualInput input;
        private readonly Vector2 current, previous, raw, previousRaw;
        private readonly int integerCurrent, integerPrevious;
        internal Snapshot(VirtualInput input) {
            this.input = input;
            current = previous = raw = previousRaw = default(Vector2);
            integerCurrent = integerPrevious = 0;
            switch (input) {
                case VirtualIntegerAxis axis:
                    integerCurrent = axis.Value;
                    integerPrevious = axis.PreviousValue;
                    break;
                case VirtualAxis axis:
                    current = new Vector2 { X = (float)AxisValue.GetValue(axis) };
                    previous = new Vector2 { X = (float)AxisPrevious.GetValue(axis) };
                    break;
                case VirtualJoystick joystick:
                    current = (Vector2)JoystickValue.GetValue(joystick);
                    previous = (Vector2)JoystickPrevious.GetValue(joystick);
                    raw = joystick.value;
                    previousRaw = joystick.previousValue;
                    break;
            }
        }
        internal void Apply(bool neutral) {
            switch (input) {
                case VirtualIntegerAxis axis:
                    axis.Value = neutral ? 0 : integerCurrent;
                    axis.PreviousValue = neutral ? 0 : integerPrevious;
                    break;
                case VirtualAxis axis:
                    AxisValue.SetValue(axis, neutral ? 0 : current.X);
                    AxisPrevious.SetValue(axis, neutral ? 0 : previous.X);
                    break;
                case VirtualJoystick joystick:
                    JoystickValue.SetValue(joystick, neutral ? default(Vector2) : current);
                    JoystickPrevious.SetValue(joystick, neutral ? default(Vector2) : previous);
                    joystick.value = neutral ? default(Vector2) : raw;
                    joystick.previousValue = neutral ? default(Vector2) : previousRaw;
                    break;
            }
        }
    }
}
