using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework.Input;
using Monocle;

namespace Celeste.Mod.Akron;

internal static class AkronCursorHoldBinding {
    internal static void Apply(Func<ButtonBinding> getter, Action<ButtonBinding> setter, ButtonBinding replacement) {
        if (replacement == null) throw new ArgumentNullException(nameof(replacement));
        // Resolve when capture finishes, not when its popup was drawn: settings may have changed.
        ButtonBinding target = getter();
        if (target == null) {
            setter(replacement);
            return;
        }
        // The initialized VirtualButton retains this Binding. Never replace its wrapper.
        var keys = replacement.Keys?.ToList() ?? new List<Keys>();
        var buttons = replacement.Buttons?.ToList() ?? new List<Buttons>();
        var mouse = replacement.MouseButtons?.ToList() ?? new List<MInput.MouseData.MouseButtons>();
        target.Keys = keys;
        target.Buttons = buttons;
        target.MouseButtons = mouse;
    }
}
