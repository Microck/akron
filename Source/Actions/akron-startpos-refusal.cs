using System;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.Akron;

// Turns a StartPos reconstruction refusal into a sentence a player can act on.
//
// The reconstruction graph refuses a load when the saved room does not match a clean
// reload of the same room, and it says so with the graph path and every authenticity
// flag it evaluated. That text is what a maintainer needs and it is what the log keeps.
// It tells a player nothing: the proven case is ExtendedVariantMode's ZoomLevel, whose
// hooks only install while that mod's master switch is on, so a slot set with it on
// genuinely cannot load with it off. The refusal is correct and the correct action -
// turn the mod back on, or set the slot again - is nowhere in the message.
//
// The one fact that makes the message useful is the assembly the refused object came
// from, which splits the refusals we understand into three:
//
//   1. a mod the player installed owns that assembly  -> their setup, they can fix it
//   2. Akron cannot load that code at all             -> the mod is gone, turn it back on
//   3. the assembly is one the game itself ships      -> Akron's defect, ask for a report
//
// Anything else - a second DLL of a multi-assembly mod, a shared library - is left
// without a sentence rather than guessed at, and the caller keeps showing the head of
// the diagnostic text. Saying nothing new is not worse than today; saying the wrong
// thing sends the player through their mod list for an Akron bug, or files a bug report
// for a switch they turned off themselves.
internal static class AkronStartPosRefusal {
    private const int MaxNameChars = 64;

    // The assemblies the game itself ships. A refused object from one of these cannot be
    // explained by anything the player installed. Anchored on types rather than on a
    // list of names so this cannot drift from what is actually loaded.
    //
    // typeof(EverestModule).Assembly is Celeste.dll, which is also where Monocle and
    // Everest's own CoreModule live. CoreModule is a real EverestModule named "Everest",
    // so without this set every Monocle.Sprite refusal would be reported as "needs
    // Sprite from Everest" instead of as the Akron bug it is.
    private static readonly HashSet<string> GameAssemblyNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            typeof(EverestModule).Assembly.GetName().Name,
            typeof(AkronModule).Assembly.GetName().Name,
            typeof(Vector2).Assembly.GetName().Name,
            typeof(object).Assembly.GetName().Name
        };

    // Assembly-qualified names are "Namespace.Type+Nested[[generic args]], Assembly,
    // Version=..., Culture=..., PublicKeyToken=...". Generic argument lists are
    // themselves bracketed assembly-qualified names, so the assembly this name belongs
    // to is the segment after the first comma that is not inside brackets.
    internal static string GetAssemblyName(string assemblyQualifiedTypeName) {
        if (string.IsNullOrWhiteSpace(assemblyQualifiedTypeName)) {
            return string.Empty;
        }

        int depth = 0;
        for (int index = 0; index < assemblyQualifiedTypeName.Length; index++) {
            char character = assemblyQualifiedTypeName[index];
            if (character == '[') {
                depth++;
            } else if (character == ']') {
                depth--;
            } else if (character == ',' && depth == 0) {
                string remainder = assemblyQualifiedTypeName.Substring(index + 1);
                int nextSeparator = remainder.IndexOf(',');
                return (nextSeparator < 0 ? remainder : remainder.Substring(0, nextSeparator)).Trim();
            }
        }
        return string.Empty;
    }

    // The name to show the player. Compiler-generated members are what a refusal most
    // often lands on, and "ZoomLevel+<>c" or "Sprite+<PlayUtil>d__40" name nothing a
    // player recognises, so the outermost declaring type is used instead. Generic
    // argument lists and arity markers are dropped for the same reason.
    internal static string GetDisplayTypeName(string assemblyQualifiedTypeName) {
        if (string.IsNullOrWhiteSpace(assemblyQualifiedTypeName)) {
            return string.Empty;
        }

        string name = assemblyQualifiedTypeName;
        int bracket = name.IndexOf('[');
        if (bracket >= 0) {
            name = name.Substring(0, bracket);
        }
        int comma = name.IndexOf(',');
        if (comma >= 0) {
            name = name.Substring(0, comma);
        }
        int lastDot = name.LastIndexOf('.');
        if (lastDot >= 0) {
            name = name.Substring(lastDot + 1);
        }
        int nested = name.IndexOf('+');
        if (nested >= 0) {
            name = name.Substring(0, nested);
        }
        int arity = name.IndexOf('`');
        if (arity >= 0) {
            name = name.Substring(0, arity);
        }
        return name.Trim();
    }

    // Returns null when the refusal names no object, or names one this cannot attribute.
    internal static string Describe(string slotLabel, string refusedTypeName) {
        // Checked before the module list is built so a refusal that names nothing - an
        // array whose length differs, a field that no longer exists - costs nothing.
        if (string.IsNullOrWhiteSpace(refusedTypeName)) {
            return null;
        }
        return Describe(slotLabel, refusedTypeName, GetLoadedMods());
    }

    internal static string Describe(
        string slotLabel,
        string refusedTypeName,
        IReadOnlyList<(string ModName, string AssemblyName)> loadedMods
    ) {
        string assemblyName = GetAssemblyName(refusedTypeName);
        string displayTypeName = GetDisplayTypeName(refusedTypeName);
        // Both come out of a snapshot file, and the reader allows a string into the
        // megabytes. The longest real name either of these has produced is 28 characters,
        // so anything past this is a corrupt or hostile document rather than a name worth
        // putting in a sentence.
        if (assemblyName.Length == 0 || assemblyName.Length > MaxNameChars ||
            displayTypeName.Length == 0 || displayTypeName.Length > MaxNameChars) {
            return null;
        }

        // Asking the runtime rather than trusting a list of loaded assembly names: Everest
        // gives every mod its own assembly load context, and a mod whose own dependencies
        // are missing stays loaded somewhere while being unreachable from Akron's context,
        // which is what a StartPos actually cannot rebuild. This repeats a lookup the graph
        // already made on the same string moments earlier in this load, so it is never the
        // first time a snapshot's type name is resolved.
        bool isTypeAvailable = Type.GetType(refusedTypeName, throwOnError: false) != null;
        bool isGameAssembly = GameAssemblyNames.Contains(assemblyName);
        string modName = loadedMods == null || isGameAssembly
            ? null
            : loadedMods
                .FirstOrDefault(mod => string.Equals(mod.AssemblyName, assemblyName, StringComparison.OrdinalIgnoreCase))
                .ModName;

        // The mod's code is here and the room still does not contain the object, so
        // something about how that mod is set up differs from when the slot was set. The
        // type has to resolve for this to hold: ModA.Container<ModB.State> names ModA and
        // fails to load when ModB is the one that is gone, and blaming ModA would be wrong.
        if (!string.IsNullOrEmpty(modName) && isTypeAvailable) {
            return slotLabel + " needs " + displayTypeName + " from " + modName +
                   ", and this room does not have it. Check that mod's settings, or set the slot again.";
        }

        // Akron cannot reach this code at all. The assembly name stands in for the mod name
        // because there is no module left to ask; for the great majority of Everest code
        // mods the two are the same word. It says what is true - Akron cannot load it - and
        // leaves the cause conditional, because a mod that ships several assemblies can be
        // enabled and still no longer carry the type an older slot names.
        //
        // A game assembly is excluded because a generic container declared by the runtime
        // fails to load whenever one of its arguments belongs to a missing mod, and naming
        // the runtime there would be nonsense. So is an assembly a loaded mod owns, for the
        // same reason from the other direction.
        if (!isTypeAvailable && !isGameAssembly && string.IsNullOrEmpty(modName)) {
            return slotLabel + " needs " + displayTypeName + " from " + assemblyName +
                   ", which Akron cannot load now. Turn that mod back on if you removed it, or set the slot again.";
        }

        // The assembly it came from is the game's own, so no mod can be named for it. That
        // is not the same as no mod being involved: a mod hook can add a plain Celeste or
        // Monocle object to a room, and disabling that mod leaves a refusal on a vanilla
        // type. The message says which of the two it is by the one test the player can
        // make, and asks for the log either way.
        if (isGameAssembly && isTypeAvailable) {
            return slotLabel + " could not be rebuilt: this room has no " + displayTypeName +
                   " to match, and no mod owns it. If your mods have not changed, this is an Akron bug; report akron-current.log.";
        }

        return null;
    }

    // NullModule is Everest's placeholder for mods that ship no code; it carries no
    // assembly worth matching. Everest's own CoreModule is kept in the list and filtered
    // by assembly instead, so the exclusion lives in one place and is testable.
    private static IReadOnlyList<(string ModName, string AssemblyName)> GetLoadedMods() {
        return Everest.Modules
            .Where(module => module != null &&
                             module.GetType().Name != "NullModule" &&
                             !string.IsNullOrWhiteSpace(module.Metadata?.Name))
            .Select(module => (module.Metadata.Name, module.GetType().Assembly.GetName().Name))
            .ToList();
    }
}
