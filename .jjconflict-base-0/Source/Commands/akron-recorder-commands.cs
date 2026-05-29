using System.Globalization;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronCommands {
    [Command("akron_internal_recorder", "control Internal Recorder for automation: start|stop|start-replay|stop-replay|disarm-completion|save-replay|arm-completion|flag-completion|build-clear-video|status")]
    public static void InternalRecorder(string action = "status") {
        switch (NormalizeToken(action)) {
            case "":
            case "status":
                break;
            case "start": {
                Level level = RequireLevel();
                if (level == null) {
                    return;
                }

                AkronActions.StartInternalRecording(level);
                break;
            }
            case "stop":
                AkronActions.StopInternalRecording();
                break;
            case "startreplay":
            case "startbuffer": {
                Scene scene = RequireScene();
                if (scene == null) {
                    return;
                }

                AkronActions.StartReplayBuffer(scene);
                break;
            }
            case "stopreplay":
            case "stopbuffer":
                AkronActions.StopReplayBuffer();
                break;
            case "disarmcompletion":
            case "disarmclear":
            case "disarmreplay":
                AkronActions.DisarmReplayBufferAutoStart();
                break;
            case "savereplay":
            case "replay": {
                Scene scene = RequireScene();
                if (scene == null) {
                    return;
                }

                AkronActions.SaveReplayBuffer(scene);
                break;
            }
            case "armcompletion":
            case "completion":
            case "armclear": {
                Scene scene = RequireScene();
                if (scene == null) {
                    return;
                }

                AkronActions.ArmCompletionCapture(scene);
                break;
            }
            case "flagcompletion":
            case "flagclear": {
                Scene scene = RequireScene();
                if (scene == null) {
                    return;
                }

                AkronActions.FlagCompletion(scene);
                break;
            }
            case "buildclearvideo":
            case "buildcompletion":
            case "clearvideo": {
                Scene scene = RequireScene();
                if (scene == null) {
                    return;
                }

                AkronActions.BuildCompletionVideo(scene);
                break;
            }
            default:
                Log("unknown internal recorder action: " + action);
                Log("usage: akron_internal_recorder start|stop|start-replay|stop-replay|disarm-completion|save-replay|arm-completion|flag-completion|build-clear-video|status");
                return;
        }

        Log("internal-recorder: " + AkronInternalRecorder.DescribeStatus());
        Log("internal-recorder-replay: " + AkronInternalRecorder.DescribeReplayBufferStatus());
        Log("internal-recorder-frames: " + AkronInternalRecorder.CapturedFrames.ToString(CultureInfo.InvariantCulture));
        Log("internal-recorder-dropped-frames: " + AkronInternalRecorder.DroppedFrames.ToString(CultureInfo.InvariantCulture));
        Log("internal-recorder-warnings: " + AkronInternalRecorder.DescribeWarnings());
        if (!string.IsNullOrWhiteSpace(AkronInternalRecorder.LastClipPath)) {
            Log("internal-recorder-last-clip: " + AkronModule.Settings.FormatPathForDisplay(AkronInternalRecorder.LastClipPath));
        }

        if (!string.IsNullOrWhiteSpace(AkronInternalRecorder.LastError)) {
            Log("internal-recorder-error: " + AkronInternalRecorder.LastError);
        }
    }
}
