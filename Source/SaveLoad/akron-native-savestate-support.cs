using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste;
using FMOD.Studio;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;

namespace Celeste.Mod.Akron;

internal sealed class AkronRandomState {
    private static readonly FieldInfo RandomStackField = typeof(Calc).GetField(
        "randomStack",
        BindingFlags.Static | BindingFlags.NonPublic
    ) ?? throw new InvalidOperationException("Monocle.Calc.randomStack is unavailable.");

    private static readonly MethodInfo MemberwiseCloneMethod = typeof(object).GetMethod(
        "MemberwiseClone",
        BindingFlags.Instance | BindingFlags.NonPublic
    ) ?? throw new InvalidOperationException("System.Object.MemberwiseClone is unavailable.");

    private readonly Random currentRandom;
    private readonly Random[] randomStackTopFirst;

    private AkronRandomState(Random currentRandom, Random[] randomStackTopFirst) {
        this.currentRandom = currentRandom;
        this.randomStackTopFirst = randomStackTopFirst;
    }

    public static AkronRandomState Capture() {
        Stack<Random> randomStack = GetRandomStack();
        return new AkronRandomState(
            CloneRandom(Calc.Random),
            randomStack.Select(CloneRandom).ToArray()
        );
    }

    public static AkronRandomState CaptureStable() {
        Stack<Random> randomStack = GetRandomStack();
        // PushRandom stores the prior generator in the stack. The bottom item
        // is therefore the generator Celeste will use after every temporary
        // scope has unwound. StartPos resumes at a frame boundary, so persisting
        // that generator avoids restoring another mod's half-finished scope.
        Random stableRandom = randomStack.Count == 0 ? Calc.Random : randomStack.Last();
        return new AkronRandomState(CloneRandom(stableRandom), Array.Empty<Random>());
    }

    public static bool HasActiveScope => GetRandomStack().Count > 0;

    public void Restore() {
        Stack<Random> currentStack = GetRandomStack();
        if (randomStackTopFirst.Length == 0 && currentStack.Count > 0) {
            // A stable frame snapshot represents the base generator, not a
            // temporary PushRandom scope owned by another mod. Keep the active
            // generator and stack object intact, but replace the bottom entry
            // so the saved base generator resumes after every scope unwinds.
            Random[] activeStackTopFirst = currentStack.ToArray();
            activeStackTopFirst[^1] = CloneRandom(currentRandom);
            currentStack.Clear();
            foreach (Random generator in activeStackTopFirst.Reverse()) {
                currentStack.Push(generator);
            }
            return;
        }

        Calc.Random = CloneRandom(currentRandom);

        // Stack<T> enumerates from top to bottom, but its constructor pushes in
        // enumeration order. Reverse the saved order so the same RNG stays on top.
        Stack<Random> restoredStack = new Stack<Random>(randomStackTopFirst
            .Reverse()
            .Select(CloneRandom));
        RandomStackField.SetValue(null, restoredStack);
    }

    private static Stack<Random> GetRandomStack() {
        // Calc initializes this field lazily in some Celeste builds. A null
        // field has the same meaning as an empty stack before the first scope.
        return RandomStackField.GetValue(null) as Stack<Random> ?? new Stack<Random>();
    }

    private static Random CloneRandom(Random source) {
        if (source == null) {
            return null;
        }
        return (Random) CloneObjectGraph(source, new Dictionary<object, object>(ReferenceEqualityComparer.Instance));
    }

    private static object CloneObjectGraph(object source, Dictionary<object, object> visited) {
        Type type = source.GetType();
        if (IsDeeplyImmutable(type)) {
            return source;
        }
        if (visited.TryGetValue(source, out object existingClone)) {
            return existingClone;
        }

        if (source is Array sourceArray) {
            Array clonedArray = (Array) sourceArray.Clone();
            visited[source] = clonedArray;
            if (!IsDeeplyImmutable(sourceArray.GetType().GetElementType())) {
                for (int index = 0; index < sourceArray.Length; index++) {
                    object item = sourceArray.GetValue(index);
                    if (item != null) {
                        clonedArray.SetValue(CloneObjectGraph(item, visited), index);
                    }
                }
            }
            return clonedArray;
        }

        object clone = MemberwiseCloneMethod.Invoke(source, Array.Empty<object>());
        visited[source] = clone;
        for (Type currentType = type; currentType != null; currentType = currentType.BaseType) {
            foreach (FieldInfo field in currentType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)) {
                if (IsDeeplyImmutable(field.FieldType)) {
                    continue;
                }

                object fieldValue = field.GetValue(source);
                if (fieldValue != null) {
                    field.SetValue(clone, CloneObjectGraph(fieldValue, visited));
                }
            }
        }
        return clone;
    }

    private static bool IsDeeplyImmutable(Type type) {
        return type.IsPrimitive ||
               type.IsEnum ||
               type == typeof(string) ||
               type == typeof(decimal) ||
               type == typeof(IntPtr) ||
               type == typeof(UIntPtr);
    }
}

internal static class AkronNativeSavestateSupport {
    private static bool initialized;
    private static readonly List<IDisposable> InstalledHooks = new List<IDisposable>();
    private static object deathTrackerGeneratedObject;
    private static Type deathTrackerDisplayType;

    public static void Initialize() {
        if (initialized) {
            return;
        }

        initialized = true;
        AkronDeepClone.Initialize();

        AkronSaveLoadService.AddReturnSameObjectProcessor(ShouldReturnSameObjectForNativeClone);

        AkronEventInstanceUtils.Initialize();
        RegisterCoreRuntimeSupport();
        RegisterThirdPartySupport();
    }

    public static void Reset() {
        foreach (IDisposable hook in InstalledHooks) {
            hook.Dispose();
        }

        InstalledHooks.Clear();
        AkronEventInstanceUtils.Reset();
        AkronDeepClone.Reset();
        deathTrackerGeneratedObject = null;
        deathTrackerDisplayType = null;
        initialized = false;
    }

    internal static bool ShouldReturnSameObjectForNativeClone(Type type) {
        return type == typeof(AkronRandomState) ||
               type == typeof(Type) ||
               // Level snapshots own gameplay state, not GPU resources. Cloning FNA
               // GraphicsResource instances creates unregistered wrapper objects whose
               // finalizers can dispose texture handles that belong to the live game.
               typeof(GraphicsResource).IsAssignableFrom(type) ||
               typeof(MTexture).IsAssignableFrom(type) ||
               typeof(MemberInfo).IsAssignableFrom(type) ||
               typeof(Assembly).IsAssignableFrom(type) ||
               (typeof(IDisposable).IsAssignableFrom(type) && type.FullName?.IndexOf("ILHook", StringComparison.Ordinal) >= 0) ||
               IsFrostHelperSavestatePersisted(type);
    }

    private static bool IsFrostHelperSavestatePersisted(Type type) {
        // FrostHelper registers this marker with SpeedrunTool so packed sprite
        // sources are kept by reference. Akron's native StartPos clone path may
        // run without receiving SpeedrunTool's import callback, and cloning those
        // sources can finalize clones that dispose live spinner VirtualTextures.
        return type.GetInterfaces().Any(candidate =>
            string.Equals(candidate.FullName, "FrostHelper.ModIntegration.ISavestatePersisted", StringComparison.Ordinal));
    }

    private static void RegisterCoreRuntimeSupport() {
        List<FieldInfo> inputFields = typeof(Input).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(info => typeof(VirtualInput).IsAssignableFrom(info.FieldType))
            .ToList();

        AkronSaveLoadService.RegisterSaveLoadAction(
            saveState: (savedValues, level) => {
                AkronSaveLoadService.SaveStaticMembers(savedValues, typeof(Engine),
                    nameof(Engine.DashAssistFreeze),
                    nameof(Engine.DashAssistFreezePress),
                    nameof(Engine.DeltaTime),
                    nameof(Engine.FrameCounter),
                    nameof(Engine.FreezeTimer),
                    nameof(Engine.RawDeltaTime),
                    "TimeRate",
                    "TimeRateB");
                // Force.DeepCloner cannot safely construct the runtime-specific
                // System.Random implementation. Keep a valid memberwise clone instead,
                // including Calc's nested RNG stack, so later random events repeat.
                savedValues[typeof(Calc)] = new Dictionary<string, object> {
                    ["RandomState"] = AkronSaveLoadService.CurrentSlotName.StartsWith(AkronActions.StartPosStateSlotPrefix, StringComparison.Ordinal)
                        ? AkronRandomState.CaptureStable()
                        : AkronRandomState.Capture()
                };
                AkronSaveLoadService.SaveStaticMembers(savedValues, typeof(Glitch), nameof(Glitch.Value));
                AkronSaveLoadService.SaveStaticMembers(savedValues, typeof(Distort), nameof(Distort.Anxiety), nameof(Distort.GameRate));
                AkronSaveLoadService.SaveStaticMembers(savedValues, typeof(ScreenWipe), nameof(ScreenWipe.WipeColor));
                AkronSaveLoadService.SaveStaticMembers(savedValues, typeof(MInput),
                    nameof(MInput.Active),
                    nameof(MInput.Disabled),
                    nameof(MInput.Keyboard),
                    nameof(MInput.Mouse),
                    nameof(MInput.GamePads));

                Dictionary<string, object> inputState = new Dictionary<string, object> {
                    ["grabToggle"] = Input.grabToggle,
                    ["LastAim"] = Input.LastAim
                };
                foreach (FieldInfo inputField in inputFields) {
                    inputState[inputField.Name] = inputField.GetValue(null);
                }
                savedValues[typeof(Input)] = inputState;

                savedValues[typeof(Audio)] = new Dictionary<string, object> {
                    ["currentMusicEvent"] = Audio.GetEventName(Audio.CurrentMusicEventInstance),
                    ["currentMusicEventParameters"] = Audio.CurrentMusicEventInstance.GetSavedParameterValues(),
                    ["currentAmbienceEvent"] = Audio.GetEventName(Audio.CurrentAmbienceEventInstance),
                    ["currentAmbienceEventParameters"] = Audio.CurrentAmbienceEventInstance.GetSavedParameterValues(),
                    ["currentAltMusicEvent"] = Audio.GetEventName(Audio.currentAltMusicEvent),
                    ["currentAltMusicEventParameters"] = Audio.currentAltMusicEvent.GetSavedParameterValues(),
                    ["MusicUnderwater"] = Audio.MusicUnderwater,
                    ["PauseMusic"] = Audio.PauseMusic,
                    ["PauseGameplaySfx"] = Audio.PauseGameplaySfx
                };

                // DustStyles is static visual support for vanilla DustEdges. Restoring
                // a cloned copy can leave DustEdges with invalid style references on
                // the next render; keep the live table instead of snapshotting it.
            },
            loadState: (savedValues, level) => {
                AkronSaveLoadService.LoadStaticMembers(savedValues, typeof(Engine),
                    nameof(Engine.DashAssistFreeze),
                    nameof(Engine.DashAssistFreezePress),
                    nameof(Engine.DeltaTime),
                    nameof(Engine.FrameCounter),
                    nameof(Engine.FreezeTimer),
                    nameof(Engine.RawDeltaTime),
                    "TimeRate",
                    "TimeRateB");
                if (savedValues.TryGetValue(typeof(Calc), out Dictionary<string, object> calcState) &&
                    calcState.TryGetValue("RandomState", out object randomState)) {
                    ((AkronRandomState) randomState).Restore();
                }
                AkronSaveLoadService.LoadStaticMembers(savedValues, typeof(Glitch), nameof(Glitch.Value));
                AkronSaveLoadService.LoadStaticMembers(savedValues, typeof(Distort), nameof(Distort.Anxiety), nameof(Distort.GameRate));
                AkronSaveLoadService.LoadStaticMembers(savedValues, typeof(ScreenWipe), nameof(ScreenWipe.WipeColor));
                AkronSaveLoadService.LoadStaticMembers(savedValues, typeof(MInput),
                    nameof(MInput.Active),
                    nameof(MInput.Disabled),
                    nameof(MInput.Keyboard),
                    nameof(MInput.Mouse),
                    nameof(MInput.GamePads));

                if (savedValues.TryGetValue(typeof(Input), out Dictionary<string, object> inputState)) {
                    if (inputState.TryGetValue("grabToggle", out object grabToggle)) {
                        Input.grabToggle = (bool) grabToggle;
                    }
                    if (inputState.TryGetValue("LastAim", out object lastAim)) {
                        Input.LastAim = (Vector2) lastAim;
                    }

                    foreach (FieldInfo inputField in inputFields) {
                        if (!inputState.TryGetValue(inputField.Name, out object savedInput)) {
                            continue;
                        }

                        object currentInput = inputField.GetValue(null);
                        if (currentInput is VirtualJoystick joystick && savedInput is VirtualJoystick savedJoystick) {
                            joystick.InvertedX = savedJoystick.InvertedX;
                            joystick.InvertedY = savedJoystick.InvertedY;
                        } else if (currentInput is VirtualIntegerAxis axis && savedInput is VirtualIntegerAxis savedAxis) {
                            axis.Inverted = savedAxis.Inverted;
                        }
                    }
                }

                if (savedValues.TryGetValue(typeof(Audio), out Dictionary<string, object> audioState)) {
                    Audio.SetMusic(audioState.TryGetValue("currentMusicEvent", out object musicEvent) ? musicEvent as string : null);
                    Audio.CurrentMusicEventInstance?.CopyParametersFrom(audioState.TryGetValue("currentMusicEventParameters", out object musicParameters)
                        ? musicParameters as ConcurrentDictionary<string, float>
                        : null);

                    Audio.SetAmbience(audioState.TryGetValue("currentAmbienceEvent", out object ambienceEvent) ? ambienceEvent as string : null);
                    Audio.CurrentAmbienceEventInstance?.CopyParametersFrom(audioState.TryGetValue("currentAmbienceEventParameters", out object ambienceParameters)
                        ? ambienceParameters as ConcurrentDictionary<string, float>
                        : null);

                    Audio.SetAltMusic(audioState.TryGetValue("currentAltMusicEvent", out object altMusicEvent) ? altMusicEvent as string : null);
                    Audio.currentAltMusicEvent?.CopyParametersFrom(audioState.TryGetValue("currentAltMusicEventParameters", out object altMusicParameters)
                        ? altMusicParameters as ConcurrentDictionary<string, float>
                        : null);

                    if (audioState.TryGetValue("MusicUnderwater", out object musicUnderwater)) {
                        Audio.MusicUnderwater = (bool) musicUnderwater;
                    }
                    if (audioState.TryGetValue("PauseMusic", out object pauseMusic)) {
                        Audio.PauseMusic = (bool) pauseMusic;
                    }
                    if (audioState.TryGetValue("PauseGameplaySfx", out object pauseGameplaySfx)) {
                        Audio.PauseGameplaySfx = (bool) pauseGameplaySfx;
                    }

                    if (!level.Paused && Level._PauseSnapshot != null) {
                        Audio.ReleaseSnapshot(Level._PauseSnapshot);
                        Level.PauseSnapshot = null;
                    }
                }

                MInput.GamePads[Input.Gamepad].Rumble(0f, 0f);
            },
            clearState: null,
            beforeSaveState: null,
            beforeLoadState: null,
            preCloneEntities: null
        );
    }

    private static void RegisterThirdPartySupport() {
        CloneModTypeFields("CommunalHelper", "Celeste.Mod.CommunalHelper.DashStates.SeekerDash",
            "hasSeekerDash", "seekerDashAttacking", "seekerDashTimer", "seekerDashLaunched", "launchPossible");
        CloneModTypeFields("CrystallineHelper", "vitmod.VitModule", "timeStopScaleTimer", "timeStopType", "noMoveScaleTimer");
        CloneModTypeFields("CrystallineHelper", "vitmod.TriggerTrigger", "collidedEntities");
        CloneModTypeFields("VivHelper", "VivHelper.Entities.RefillCancel", "inSpace", "DashRefillRestrict", "DashRestrict", "StaminaRefillRestrict", "p");
        CloneModTypeFields("VivHelper", "VivHelper.Entities.SpeedPowerup", "Store", "Launch");
        CloneModTypeFields("VivHelper", "VivHelper.Entities.BooMushroom", "color", "mode");
        CloneModTypeFields("VivHelper", "VivHelper.Entities.Boosters.BoostFunctions", "dyn");
        CloneModTypeFields("VivHelper", "VivHelper.Entities.Boosters.OrangeBoost", "timer");
        CloneModTypeFields("VivHelper", "VivHelper.Entities.Boosters.PinkBoost", "timer");
        CloneModTypeFields("VivHelper", "VivHelper.Entities.Boosters.WindBoost", "timer");
        CloneModTypeFields("VivHelper", "VivHelper.Entities.ExplodeLaunchModifier", "DisableFreeze", "DetectFreeze", "bumperWrapperType");
        CloneModTypeFields("VivHelper", "VivHelper.Entities.Blockout", "alphaFade");
        CloneModTypeFields("VivHelper", "VivHelper.MoonHooks", "FloatyFix");
        CloneModTypeFields("VivHelper", "VivHelper.HelperEntities", "AllUpdateHelperEntity");
        CloneModTypeFields("VivHelper", "VivHelper.Module__Extensions__Etc.TeleportV2Hooks", "HackedFocusPoint");
        CloneModTypeFields("XaphanHelper", "Celeste.Mod.XaphanHelper.Upgrades.SpaceJump", "jumpBuffer");
        CloneModTypeFields("IsaGrabBag", "Celeste.Mod.IsaGrabBag.GrabBagModule", "ZipLineState", "playerInstance");
        CloneModTypeFields("IsaGrabBag", "Celeste.Mod.IsaGrabBag.BadelineFollower", "booster", "LookForBubble");
        CloneModTypeFields("LocksmithHelper", "Celeste.Mod.LocksmithHelper.Entities.Key", "Inventory");
        CloneModTypeFields("SpringCollab2020", "Celeste.Mod.SpringCollab2020.Entities.RainbowSpinnerColorController",
            "spinnerControllerOnScreen", "nextSpinnerController", "transitionProgress");

        RegisterExtendedVariantsSupport();
        RegisterSpringCollabSupport();
        RegisterBrokemiaSupport();
        RegisterSpirialisSupport();
        RegisterDeathTrackerSupport();
        RegisterPandorasBoxSupport();
    }

    private static void CloneModTypeFields(string metadataName, string typeName, params string[] fieldNames) {
        Type type = AkronReflection.GetType(metadataName, typeName);
        if (type != null) {
            AkronSaveLoadService.RegisterStaticTypes(type, fieldNames);
        }
    }

    private static void RegisterExtendedVariantsSupport() {
        EverestModule module = AkronReflection.GetModule("ExtendedVariantMode");
        Type moduleType = AkronReflection.GetType("ExtendedVariantMode", "ExtendedVariants.Module.ExtendedVariantsModule");
        Type settingsType = AkronReflection.GetType("ExtendedVariantMode", "ExtendedVariants.Module.ExtendedVariantsSettings");
        MethodInfo refreshDisplayList = AkronReflection.GetType("ExtendedVariantMode", "ExtendedVariants.VariantRandomizer")?.GetMethodInfo("RefreshEnabledVariantsDisplayList");
        if (module == null || moduleType == null || settingsType == null) {
            return;
        }

        List<PropertyInfo> settingsProperties = settingsType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.CanWrite && property.HasCustomAttributeNamed("SettingIgnoreAttribute") && !property.Name.StartsWith("Display", StringComparison.Ordinal))
            .ToList();

        AkronSaveLoadService.RegisterSaveLoadAction(
            saveState: (savedValues, _) => {
                object triggerManager = module.GetFieldValue("TriggerManager");
                if (triggerManager != null) {
                    savedValues[moduleType] = new Dictionary<string, object> {
                        ["TriggerManager"] = AkronSaveLoadService.DeepClone(triggerManager)
                    };
                }

                object settings = moduleType.GetPropertyValue("Settings");
                if (settings == null) {
                    return;
                }

                Dictionary<string, object> values = new Dictionary<string, object>();
                foreach (PropertyInfo property in settingsProperties) {
                    values[property.Name] = property.GetValue(settings);
                }

                savedValues[settingsType] = (Dictionary<string, object>) AkronSaveLoadService.DeepClone(values);
            },
            loadState: (savedValues, _) => {
                if (savedValues.TryGetValue(moduleType, out Dictionary<string, object> moduleValues) &&
                    moduleValues.TryGetValue("TriggerManager", out object savedTriggerManager) &&
                    module.GetFieldValue("TriggerManager") is { } currentTriggerManager) {
                    AkronDeepClone.CopyInto(savedTriggerManager, currentTriggerManager);
                }

                object settings = moduleType.GetPropertyValue("Settings");
                if (settings != null && savedValues.TryGetValue(settingsType, out Dictionary<string, object> settingsValues)) {
                    Dictionary<string, object> clonedValues = (Dictionary<string, object>) AkronSaveLoadService.DeepClone(settingsValues);
                    foreach (KeyValuePair<string, object> pair in clonedValues) {
                        settings.SetPropertyValue(pair.Key, pair.Value);
                    }
                }

                refreshDisplayList?.Invoke(null, Array.Empty<object>());
            },
            clearState: null,
            beforeSaveState: null,
            beforeLoadState: null,
            preCloneEntities: null
        );
    }

    private static void RegisterSpringCollabSupport() {
        Type controllerType = AkronReflection.GetType("SpringCollab2020", "Celeste.Mod.SpringCollab2020.Entities.RainbowSpinnerColorController");
        Type areaControllerType = AkronReflection.GetType("SpringCollab2020", "Celeste.Mod.SpringCollab2020.Entities.RainbowSpinnerColorAreaController");
        Type spikeControllerType = AkronReflection.GetType("SpringCollab2020", "Celeste.Mod.SpringCollab2020.Entities.SpikeJumpThroughController");
        if (controllerType == null || areaControllerType == null || spikeControllerType == null) {
            return;
        }

        MethodInfo controllerHueMethod = controllerType.GetMethodInfo("getRainbowSpinnerHue");
        MethodInfo areaHueMethod = areaControllerType.GetMethodInfo("getRainbowSpinnerHue");
        MethodInfo spikeCollideMethod = spikeControllerType.GetMethodInfo("OnCollideHook");
        if (controllerHueMethod == null || areaHueMethod == null || spikeCollideMethod == null) {
            return;
        }

        AkronSaveLoadService.RegisterSaveLoadAction(
            saveState: (savedValues, _) => {
                savedValues[typeof(AkronNativeSavestateSupport)] = new Dictionary<string, object> {
                    ["RainbowSpinnerColorController"] = controllerType.GetFieldValue<bool>("rainbowSpinnerHueHooked"),
                    ["RainbowSpinnerColorAreaController"] = areaControllerType.GetFieldValue<bool>("rainbowSpinnerHueHooked"),
                    ["SpikeJumpThroughController"] = spikeControllerType.GetFieldValue<bool>("SpikeHooked")
                };
            },
            loadState: (savedValues, _) => {
                if (!savedValues.TryGetValue(typeof(AkronNativeSavestateSupport), out Dictionary<string, object> values)) {
                    return;
                }

                RestoreHook(values, "RainbowSpinnerColorController", controllerType, "rainbowSpinnerHueHooked",
                    () => (On.Celeste.CrystalStaticSpinner.hook_GetHue) Delegate.CreateDelegate(typeof(On.Celeste.CrystalStaticSpinner.hook_GetHue), controllerHueMethod));
                RestoreHook(values, "RainbowSpinnerColorAreaController", areaControllerType, "rainbowSpinnerHueHooked",
                    () => (On.Celeste.CrystalStaticSpinner.hook_GetHue) Delegate.CreateDelegate(typeof(On.Celeste.CrystalStaticSpinner.hook_GetHue), areaHueMethod));
                RestoreHook(values, "SpikeJumpThroughController", spikeControllerType, "SpikeHooked",
                    () => (On.Celeste.Spikes.hook_OnCollide) Delegate.CreateDelegate(typeof(On.Celeste.Spikes.hook_OnCollide), spikeCollideMethod));
            },
            clearState: null,
            beforeSaveState: null,
            beforeLoadState: null,
            preCloneEntities: null
        );

        static void RestoreHook<TDelegate>(Dictionary<string, object> values, string key, Type type, string flagField, Func<TDelegate> createDelegate) where TDelegate : Delegate {
            if (!values.TryGetValue(key, out object enabledValue)) {
                return;
            }

            TDelegate hook = createDelegate();
            switch (hook) {
                case On.Celeste.CrystalStaticSpinner.hook_GetHue getHue:
                    On.Celeste.CrystalStaticSpinner.GetHue -= getHue;
                    if ((bool) enabledValue) {
                        On.Celeste.CrystalStaticSpinner.GetHue += getHue;
                    }
                    break;
                case On.Celeste.Spikes.hook_OnCollide onCollide:
                    On.Celeste.Spikes.OnCollide -= onCollide;
                    if ((bool) enabledValue) {
                        On.Celeste.Spikes.OnCollide += onCollide;
                    }
                    break;
            }

            type.SetFieldValue(flagField, enabledValue);
        }
    }

    private static void RegisterBrokemiaSupport() {
        Type vineinatorType = AkronReflection.GetType("BrokemiaHelper", "BrokemiaHelper.PixelRendered.Vineinator");
        Type lizardType = AkronReflection.GetType("BrokemiaHelper", "BrokemiaHelper.PixelRendered.RWLizard");
        if (vineinatorType == null && lizardType == null) {
            return;
        }

        AkronSaveLoadService.RegisterSaveLoadAction(
            saveState: null,
            loadState: (_, level) => {
                foreach (Entity entity in level.Entities.Where(entity =>
                             (vineinatorType != null && vineinatorType.IsInstanceOfType(entity)) ||
                             (lizardType != null && lizardType.IsInstanceOfType(entity)))) {
                    object pixelComponent = entity.GetFieldValue("pixelComponent");
                    if (pixelComponent == null) {
                        continue;
                    }

                    pixelComponent.SetFieldValue("textureChunks", null);
                    pixelComponent.InvokeMethod("CommitChunks");
                }
            },
            clearState: null,
            beforeSaveState: null,
            beforeLoadState: null,
            preCloneEntities: null
        );
    }

    private static void RegisterSpirialisSupport() {
        CloneModTypeFields("SpirialisHelper", "Celeste.Mod.Spirialis.TimePlayerSettings", "instance", "stoppedX", "stoppedY");
        CloneModTypeFields("SpirialisHelper", "Celeste.Mod.Spirialis.CustomRainBG", "timeSinceFreeze");

        Type timeControllerType = AkronReflection.GetType("SpirialisHelper", "Celeste.Mod.Spirialis.TimeController");
        if (timeControllerType != null) {
            AkronSaveLoadService.RegisterSaveLoadAction(
                saveState: null,
                loadState: (_, level) => {
                    Entity timeController = level.Entities.FirstOrDefault(entity => entity.GetType() == timeControllerType);
                    if (timeController == null) {
                        return;
                    }

                    if (Delegate.CreateDelegate(typeof(ILContext.Manipulator), timeController, timeControllerType.GetMethodInfo("CustomLevelRender")) is ILContext.Manipulator manipulator) {
                        IL.Celeste.Level.Render -= manipulator;
                        IL.Celeste.Level.Render += manipulator;
                    }

                    if (timeController.GetFieldValue<bool>("hookAdded") &&
                        Delegate.CreateDelegate(typeof(On.Monocle.EntityList.hook_Update), timeController, timeControllerType.GetMethodInfo("CustomELUpdate")) is On.Monocle.EntityList.hook_Update customUpdate) {
                        On.Monocle.EntityList.Update -= customUpdate;
                        On.Monocle.EntityList.Update += customUpdate;
                    }
                },
                clearState: null,
                beforeSaveState: null,
                beforeLoadState: null,
                preCloneEntities: null
            );
        }

        Type timeZipMoverType = AkronReflection.GetType("SpirialisHelper", "Celeste.Mod.Spirialis.TimeZipMover");
        FieldInfo timeStreetlightHookField = timeZipMoverType?.GetFieldInfo("TimeStreetlightUpdate");
        MethodInfo zipMoverSequenceMethod = typeof(ZipMover).GetMethod("Sequence", BindingFlags.Instance | BindingFlags.NonPublic)?.GetStateMachineTarget();
        MethodInfo zipSequenceMethod = timeZipMoverType?.GetMethodInfo("ZipSequence");
        if (timeZipMoverType == null || timeStreetlightHookField == null || zipMoverSequenceMethod == null || zipSequenceMethod == null) {
            return;
        }

        AkronSaveLoadService.RegisterSaveLoadAction(
            saveState: null,
            loadState: (_, level) => {
                if (!level.Tracker.Entities.TryGetValue(timeZipMoverType, out List<Entity> zips)) {
                    return;
                }

                foreach (Entity entity in zips) {
                    if (timeStreetlightHookField.GetValue(entity) is IDisposable existingHook) {
                        existingHook.Dispose();
                    }

                    if (Delegate.CreateDelegate(typeof(ILContext.Manipulator), entity, zipSequenceMethod) is not ILContext.Manipulator manipulator) {
                        continue;
                    }

                    object newHook = CreateLegacyIlHook(timeStreetlightHookField.FieldType, zipMoverSequenceMethod, manipulator);
                    if (newHook != null) {
                        timeStreetlightHookField.SetValue(entity, newHook);
                    }
                }
            },
            clearState: null,
            beforeSaveState: null,
            beforeLoadState: null,
            preCloneEntities: null
        );
    }

    private static void RegisterDeathTrackerSupport() {
        Type generatedType = AkronReflection.GetType("DeathTracker", "CelesteDeathTracker.DeathTrackerModule+<>c__DisplayClass6_0");
        Type deathDisplayType = AkronReflection.GetType("DeathTracker", "CelesteDeathTracker.DeathDisplay");
        MethodInfo onPlayerSpawn = generatedType?.GetMethodInfo("<Load>b__2");
        if (generatedType == null || deathDisplayType == null || onPlayerSpawn == null) {
            return;
        }

        deathTrackerDisplayType = deathDisplayType;

        InstallIlHook(onPlayerSpawn, (cursor, _) => {
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.EmitDelegate<Action<object>>(CaptureDeathTrackerGeneratedObject);
        });

        InstallIlHook(onPlayerSpawn, (cursor, _) => {
            while (cursor.TryGotoNext(MoveType.After, instruction => instruction.MatchLdfld(generatedType, "display"))) {
                cursor.EmitDelegate<Func<object, object>>(ResolveDeathTrackerDisplay);
            }
        });

        AkronSaveLoadService.RegisterSaveLoadAction(
            saveState: null,
            loadState: (_, level) => {
                if (!AkronModule.Settings.SaveTimeAndDeaths && deathTrackerGeneratedObject != null && level.Tracker.GetEntity<Player>() is { } player) {
                    onPlayerSpawn.Invoke(deathTrackerGeneratedObject, new object[] { player });
                }
            },
            clearState: null,
            beforeSaveState: null,
            beforeLoadState: null,
            preCloneEntities: null
        );
    }

    private static void RegisterPandorasBoxSupport() {
        Type timeFieldType = AkronReflection.GetType("PandorasBox", "Celeste.Mod.PandorasBox.TimeField");
        Type pipeHelperType = AkronReflection.GetType("PandorasBox", "Celeste.Mod.PandorasBox.MarioClearPipeHelper");
        MethodInfo allowComponentsForList = pipeHelperType?.GetMethodInfo("AllowComponentsForList");
        MethodInfo shouldAddComponentsForList = pipeHelperType?.GetMethodInfo("ShouldAddComponentsForList");

        if (pipeHelperType?.GetFieldInfo("CurrentlyTransportedEntities") != null) {
            AkronSaveLoadService.RegisterStaticTypes(pipeHelperType, "CurrentlyTransportedEntities");
        }

        AkronSaveLoadService.RegisterSaveLoadAction(
            saveState: (savedValues, level) => {
                if (shouldAddComponentsForList != null) {
                    savedValues[pipeHelperType] = new Dictionary<string, object> {
                        ["ShouldAddComponents"] = pipeHelperType.InvokeMethod("ShouldAddComponentsForList", level.Entities) as bool? == true
                    };
                }
            },
            loadState: (savedValues, level) => {
                if (timeFieldType != null) {
                    MethodInfo playerUpdateHookMethod = timeFieldType.GetMethodInfo("PlayerUpdateHook");
                    if (playerUpdateHookMethod != null &&
                        Delegate.CreateDelegate(typeof(On.Celeste.Player.hook_Update), playerUpdateHookMethod) is On.Celeste.Player.hook_Update hookUpdate) {
                        On.Celeste.Player.Update -= hookUpdate;
                        if (timeFieldType.GetFieldValue<bool>("hookAdded")) {
                            On.Celeste.Player.Update += hookUpdate;
                        }
                    }
                }

                if (pipeHelperType != null && allowComponentsForList != null &&
                    savedValues.TryGetValue(pipeHelperType, out Dictionary<string, object> values) &&
                    values.TryGetValue("ShouldAddComponents", out object shouldAdd) &&
                    shouldAdd is true) {
                    pipeHelperType.InvokeMethod("AllowComponentsForList", level.Entities);
                }
            },
            clearState: null,
            beforeSaveState: level => {
                if (pipeHelperType != null && allowComponentsForList != null && shouldAddComponentsForList != null &&
                    pipeHelperType.InvokeMethod("ShouldAddComponentsForList", level.Entities) as bool? == true) {
                    pipeHelperType.InvokeMethod("AllowComponentsForList", level.Entities);
                }
            },
            beforeLoadState: null,
            preCloneEntities: null
        );
    }

    private static void InstallIlHook(MethodBase method, Action<ILCursor, ILContext> manipulator) {
        ILHook hook = new ILHook(method, context => manipulator(new ILCursor(context), context));
        InstalledHooks.Add(hook);
    }

    private static object CreateLegacyIlHook(Type hookType, MethodInfo targetMethod, ILContext.Manipulator manipulator) {
        ConstructorInfo constructor = hookType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(candidate => {
                ParameterInfo[] parameters = candidate.GetParameters();
                return parameters.Length == 2 &&
                       parameters[1].ParameterType == typeof(ILContext.Manipulator) &&
                       parameters[0].ParameterType.IsAssignableFrom(typeof(MethodInfo));
            });

        return constructor?.Invoke(new object[] { targetMethod, manipulator });
    }

    private static void CaptureDeathTrackerGeneratedObject(object obj) {
        deathTrackerGeneratedObject = obj;
    }

    private static object ResolveDeathTrackerDisplay(object display) {
        if (Engine.Scene is not Level level || deathTrackerDisplayType == null) {
            return display;
        }

        return level.Entities.FirstOrDefault(entity => entity.GetType() == deathTrackerDisplayType) ?? display;
    }
}
