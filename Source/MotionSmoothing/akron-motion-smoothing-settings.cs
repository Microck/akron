using Celeste.Mod;

namespace Celeste.Mod.Akron.MotionSmoothing;

public enum SmoothingMode {
    Extrapolate,
    Interpolate
}

public enum UpdateMode {
    Interval,
    Dynamic
}

public enum UnlockCameraStrategy {
    Hires,
    Unlock,
    Off
}

// Adapter over Akron settings for the vendored Motion Smoothing runtime.
// Keeping this shape avoids invasive edits across the upstream smoothing code
// while making Akron's settings the single persisted source of truth.
public sealed class MotionSmoothingSettings {
    private double gameSpeed = 60;
    private bool gameSpeedInLevelOnly = true;

    public bool Enabled {
        get => AkronModule.Settings.FpsBypass || AkronModule.Settings.TpsBypass;
        set {
            AkronModule.Settings.FpsBypass = value;
            if (!value) {
                AkronModule.Settings.TpsBypass = false;
            }

            MotionSmoothingModule.Instance.EnabledActions.ForEach(action => action(value));
        }
    }

    public ButtonBinding ButtonToggleMotionSmoothingEnabled {
        get => AkronModule.Settings.ToggleFrameBypass;
        set => AkronModule.Settings.ToggleFrameBypass = value;
    }

    public ButtonBinding ButtonChangeCameraSmoothingMode {
        get => AkronModule.Settings.CycleFrameBypassCameraSmoothing;
        set => AkronModule.Settings.CycleFrameBypassCameraSmoothing = value;
    }

    public int FrameRate {
        get => AkronModuleSettings.ClampFpsTarget(AkronModule.Settings.FpsBypassTarget);
        set => AkronModule.Settings.FpsBypassTarget = AkronModuleSettings.ClampFpsTarget(value);
    }

    public UnlockCameraStrategy UnlockCameraStrategy {
        get => AkronModule.Settings.FrameBypassCameraSmoothing switch {
            AkronCameraSmoothingMode.Fancy => UnlockCameraStrategy.Hires,
            AkronCameraSmoothingMode.Fast => UnlockCameraStrategy.Unlock,
            _ => UnlockCameraStrategy.Off
        };
        set => AkronModule.Settings.FrameBypassCameraSmoothing = value switch {
            UnlockCameraStrategy.Hires => AkronCameraSmoothingMode.Fancy,
            UnlockCameraStrategy.Unlock => AkronCameraSmoothingMode.Fast,
            _ => AkronCameraSmoothingMode.Off
        };
    }

    public bool RenderMadelineWithSubpixels {
        get => AkronModule.Settings.FrameBypassSubpixelMadeline;
        set => AkronModule.Settings.FrameBypassSubpixelMadeline = value;
    }

    public bool RenderBackgroundHires {
        get => AkronModule.Settings.FrameBypassSmoothBackground;
        set => AkronModule.Settings.FrameBypassSmoothBackground = value;
    }

    public bool RenderForegroundHires {
        get => AkronModule.Settings.FrameBypassSmoothForeground;
        set => AkronModule.Settings.FrameBypassSmoothForeground = value;
    }

    public bool HideStretchedEdges {
        get => AkronModule.Settings.FrameBypassHideStretchedEdges;
        set => AkronModule.Settings.FrameBypassHideStretchedEdges = value;
    }

    public SmoothingMode ObjectSmoothing {
        get => AkronModule.Settings.FrameBypassObjectSmoothing == AkronObjectSmoothingMode.Interpolate
            ? SmoothingMode.Interpolate
            : SmoothingMode.Extrapolate;
        set => AkronModule.Settings.FrameBypassObjectSmoothing = value == SmoothingMode.Interpolate
            ? AkronObjectSmoothingMode.Interpolate
            : AkronObjectSmoothingMode.Extrapolate;
    }

    public UpdateMode FramerateIncreaseMethod {
        get => AkronModule.Settings.FrameBypassMethod == AkronFrameIncreaseMethod.Dynamic
            ? UpdateMode.Dynamic
            : UpdateMode.Interval;
        set => AkronModule.Settings.FrameBypassMethod = value == UpdateMode.Dynamic
            ? AkronFrameIncreaseMethod.Dynamic
            : AkronFrameIncreaseMethod.Interval;
    }

    public bool TasMode {
        get => AkronModule.Settings.FrameBypassTasMode;
        set => AkronModule.Settings.FrameBypassTasMode = value;
    }

    public bool SillyMode {
        get => AkronModule.Settings.FrameBypassSillyMode;
        set => AkronModule.Settings.FrameBypassSillyMode = value;
    }

    public double GameSpeed {
        get => AkronModule.Settings.TpsBypass
            ? AkronModuleSettings.ClampTpsTarget(AkronModule.Settings.TpsBypassTarget)
            : gameSpeed;
        set {
            gameSpeed = value;
            AkronModule.ApplyMotionSmoothingSettings();
        }
    }

    public bool GameSpeedModified => AkronModule.Settings.TpsBypass || System.Math.Abs(gameSpeed - 60) > double.Epsilon;

    public bool GameSpeedInLevelOnly {
        get => gameSpeedInLevelOnly;
        set {
            gameSpeedInLevelOnly = value;
            AkronModule.ApplyMotionSmoothingSettings();
        }
    }
}
