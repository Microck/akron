using Celeste.Mod.Akron;
using Celeste.Mod.Akron.MotionSmoothing.Utilities;
using Monocle;

namespace Celeste.Mod.Akron.MotionSmoothing;

public class MotionSmoothingInputHandler : ToggleableFeature<MotionSmoothingInputHandler>
{
    public override void Load()
    {
        base.Load();
        On.Monocle.Scene.Begin += SceneBeginHook;
    }

    public override void Unload()
    {
        base.Unload();
        On.Monocle.Scene.Begin -= SceneBeginHook;
    }

    private static void SceneBeginHook(On.Monocle.Scene.orig_Begin orig, Scene self)
    {
        orig(self);

        var handler = self.Entities.FindFirst<MotionSmoothingInputHandlerEntity>();
        if (handler == null)
        {
            handler = new MotionSmoothingInputHandlerEntity();
            handler.Tag |= Tags.Persistent | Tags.Global;
            self.Add(handler);
        }
        else
        {
            handler.Active = true;
        }
    }

    private class MotionSmoothingInputHandlerEntity : Entity
    {
        public override void Update()
        {
            base.Update();

            if (MotionSmoothingModule.Settings.ButtonToggleMotionSmoothingEnabled.Pressed)
            {
                Logger.Log(LogLevel.Info, "MotionSmoothingInputHandler", "Toggling motion smoothing");
                MotionSmoothingModule.Settings.Enabled = !MotionSmoothingModule.Settings.Enabled;
                Engine.Scene?.Add(new AkronToast(MotionSmoothingModule.Settings.Enabled ? "Motion Smoothing enabled" : "Motion Smoothing disabled"));
            }



            else if (MotionSmoothingModule.Settings.ButtonChangeCameraSmoothingMode.Pressed)
            {
                if (!MotionSmoothingModule.Settings.Enabled)
                {
                    return;
                }

                Logger.Log(LogLevel.Info, "MotionSmoothingInputHandler", "Toggling unlock strategy");

                if (MotionSmoothingModule.Settings.UnlockCameraStrategy == UnlockCameraStrategy.Hires)
                {
                    MotionSmoothingModule.Settings.UnlockCameraStrategy = UnlockCameraStrategy.Unlock;
                }

                else if (MotionSmoothingModule.Settings.UnlockCameraStrategy == UnlockCameraStrategy.Unlock)
                {
                    MotionSmoothingModule.Settings.UnlockCameraStrategy = UnlockCameraStrategy.Off;
                }

                else
                {
                    MotionSmoothingModule.Settings.UnlockCameraStrategy = UnlockCameraStrategy.Hires;
                }



				var strategyString = MotionSmoothingModule.Settings.UnlockCameraStrategy == UnlockCameraStrategy.Hires
					? "Fancy"
					: MotionSmoothingModule.Settings.UnlockCameraStrategy == UnlockCameraStrategy.Unlock
						? "Fast"
						: "Off";

                Engine.Scene?.Add(new AkronToast("Smooth Camera: " + strategyString));
            }
        }
    }
}
