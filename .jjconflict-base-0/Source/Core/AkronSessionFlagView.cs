using System.Collections.Generic;
using System.Linq;
using Celeste;

namespace Celeste.Mod.Akron;

public static class AkronSessionFlagView {
    public static IEnumerable<string> GetVisibleFlags(Level level, int maxCount) {
        if (level?.Session == null || maxCount <= 0) {
            return Enumerable.Empty<string>();
        }

        return level.Session.Flags
            .OrderBy(flag => flag)
            .Take(maxCount)
            .ToArray();
    }

    public static IEnumerable<string> GetEditableFlags(Level level, int maxCount) {
        if (level?.Session == null || maxCount <= 0) {
            return Enumerable.Empty<string>();
        }

        string editableFlag = AkronModule.Settings.EditableFlagName;
        return level.Session.Flags
            .Concat(string.IsNullOrWhiteSpace(editableFlag) ? Enumerable.Empty<string>() : new[] { editableFlag })
            .Where(flag => !string.IsNullOrWhiteSpace(flag))
            .Distinct()
            .OrderBy(flag => flag)
            .Take(maxCount)
            .ToArray();
    }
}
