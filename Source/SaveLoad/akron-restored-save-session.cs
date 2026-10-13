using Celeste;

namespace Celeste.Mod.Akron;

public static partial class AkronSaveLoadService
{
    internal static void RelinkRestoredSaveSession(
        SaveData saveData,
        Session liveSession,
        bool restoreGlobalSaveData
    )
    {
        if (restoreGlobalSaveData || saveData == null || liveSession == null)
        {
            return;
        }

        // Full rollback already restores the captured SaveData/session alias.
        // Ordinary warm loads retain SaveData and must update its Everest-safe
        // reference. StartSession would also reset every module's session;
        // legacy CurrentSession is maintained by Everest for vanilla maps only.
        saveData.CurrentSession_Safe = liveSession;
    }
}
