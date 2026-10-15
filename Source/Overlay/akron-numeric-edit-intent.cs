using System;

namespace Celeste.Mod.Akron;

// One active native text field per overlay. A fresh activation always replaces the intent.
internal sealed class AkronNumericEditIntent {
    private string key;
    private bool applyCurrent;

    internal void Begin(string widgetKey, bool wasActive) {
        key = widgetKey;
        applyCurrent = wasActive;
    }

    internal bool? Get(string widgetKey) =>
        key != null && string.Equals(key, widgetKey, StringComparison.Ordinal) ? applyCurrent : null;

    internal void End(string widgetKey) {
        if (string.Equals(key, widgetKey, StringComparison.Ordinal)) key = null;
    }
}
