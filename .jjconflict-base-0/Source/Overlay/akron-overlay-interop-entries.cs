using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay {
    private static List<OverlayEntry> BuildSpeedrunToolEntries(Level level) {
        return new List<OverlayEntry> {
            Action("SRT Status", () => true, DescribeSpeedrunToolStatus, () => { }, "speedrun tool", "status", "broker", "room timer"),
            Action("SRT Slot", () => true, () => AkronModule.Settings.ActiveSavestateSlot.ToString(CultureInfo.InvariantCulture), () => AkronModule.SetActiveSavestateSlot(AkronModule.Settings.ActiveSavestateSlot % 9 + 1), "speedrun tool", "slot", "state"),
            Action("SRT Capture State", () => level != null && AkronSpeedrunToolBroker.Available, () => AkronSpeedrunToolBroker.IsSaved(AkronModule.Settings.ActiveSavestateSlot) ? "Overwrite" : "Empty", () => {
                if (level == null || !AkronModule.TryUse(AkronFeatureKind.BrokeredSavestates)) {
                    return;
                }

                AkronSaveLoadResult result = AkronSpeedrunToolBroker.Save(AkronModule.Settings.ActiveSavestateSlot);
                Engine.Scene?.Add(new AkronToast(AkronModule.DescribeSavestateResult("Save", result, AkronModule.Settings.ActiveSavestateSlot)));
            }, "speedrun tool", "capture", "state"),
            Action("SRT Restore State", () => level != null && AkronSpeedrunToolBroker.Available, () => AkronSpeedrunToolBroker.IsSaved(AkronModule.Settings.ActiveSavestateSlot) ? "Ready" : "Empty", () => {
                if (level == null || !AkronModule.TryUse(AkronFeatureKind.BrokeredSavestates)) {
                    return;
                }

                AkronSaveLoadResult result = AkronSpeedrunToolBroker.Load(AkronModule.Settings.ActiveSavestateSlot);
                Engine.Scene?.Add(new AkronToast(AkronModule.DescribeSavestateResult("Load", result, AkronModule.Settings.ActiveSavestateSlot)));
            }, "speedrun tool", "restore", "state"),
            Action("SRT Clear State", () => AkronSpeedrunToolBroker.Available, () => AkronSpeedrunToolBroker.IsSaved(AkronModule.Settings.ActiveSavestateSlot) ? "Saved" : "Empty", () => {
                if (!AkronModule.TryUse(AkronFeatureKind.BrokeredSavestates)) {
                    return;
                }

                AkronSpeedrunToolBroker.Clear(AkronSaveLoadService.GetSlotName(AkronModule.Settings.ActiveSavestateSlot));
                Engine.Scene?.Add(new AkronToast("Cleared Speedrun Tool slot " + AkronModule.Settings.ActiveSavestateSlot + "."));
            }, "speedrun tool", "clear", "state"),
            Action("SRT Room Time", () => AkronInterop.RoomTimerAvailable, DescribeSpeedrunToolRoomTime, () => { }, "speedrun tool", "room timer", "time"),
            Action("Export Room Times", () => AkronInterop.SpeedrunToolLoaded, () => AkronInterop.RoomTimerAvailable ? "Speedrun Tool" : "Unavailable", AkronActions.ExportRoomTimes, "splits", "room timer", "export")
        };
    }

    private static List<OverlayEntry> BuildCelesteTasEntries() {
        return new List<OverlayEntry> {
            Action("TAS Status", () => true, DescribeCelesteTasStatus, () => { }, "celestetas", "tas", "status"),
            Action("Configured TAS File", () => true, DescribeConfiguredTasFile, () => { }, "celestetas", "tas", "file", "path"),
            Action("Play Configured TAS", () => AkronInterop.CelesteTasLoaded, () => AkronInterop.IsTasRunning() ? "Running" : "Ready", AkronActions.LaunchTas, "celestetas", "tas", "play", "handoff")
        };
    }

    private static string DescribeSpeedrunToolStatus() {
        if (!AkronInterop.SpeedrunToolLoaded) {
            return "Missing";
        }

        if (!AkronSpeedrunToolBroker.Available) {
            return AkronInterop.RoomTimerAvailable ? "Timer only" : "Loaded";
        }

        return AkronInterop.RoomTimerAvailable ? "Broker + timer" : "Broker";
    }

    private static string DescribeSpeedrunToolRoomTime() {
        long? ticks = AkronInterop.TryGetSpeedrunToolRoomTime();
        return ticks.HasValue ? AkronHudRenderer.FormatHudTicks(ticks.Value) : "Unavailable";
    }

    private static string DescribeCelesteTasStatus() {
        if (!AkronInterop.CelesteTasLoaded) {
            return "Missing";
        }

        if (AkronInterop.IsTasRunning()) {
            return "Running";
        }

        return AkronInterop.IsTasActive() ? "Active" : "Idle";
    }

    private static string DescribeConfiguredTasFile() {
        return string.IsNullOrWhiteSpace(AkronModule.Settings.TasFilePath)
            ? "Unset"
            : Path.GetFileName(AkronModule.Settings.TasFilePath);
    }

    private static List<OverlayEntry> BuildExtendedVariantEntries() {
        List<OverlayEntry> entries = new List<OverlayEntry> {
            new OverlayEntry(
                "Extended Variants Master",
                () => AkronExtendedVariants.Available,
                () => AkronExtendedVariants.Available ? AkronExtendedVariants.StatusSummary : "EVM missing",
                () => {
                    if (!AkronExtendedVariants.Available) {
                        Engine.Scene?.Add(new AkronToast("Extended Variant Mode is not loaded."));
                        return;
                    }

                    bool next = !AkronExtendedVariants.MasterSwitch;
                    if (next && !AkronModule.TryUse(AkronFeatureKind.ExtendedVariantMode)) {
                        return;
                    }

                    AkronExtendedVariants.MasterSwitch = next;
                    Engine.Scene?.Add(new AkronToast("Extended Variants " + (next ? "on" : "off")));
                },
                BuildSearchTerms("Extended Variants Master", new[] { "extended variant mode", "master switch" }),
                true),
            new OverlayEntry(
                "Extended Variants Randomizer",
                () => AkronExtendedVariants.Available,
                () => AkronExtendedVariants.RandomizerEnabled ? "On" : "Off",
                () => ApplyOptionsPopupDelta("Extended Variants Randomizer", 1),
                BuildSearchTerms("Extended Variants Randomizer", new[] { "extended variant mode", "randomizer" }),
                true),
            Action("Reset Extended", () => AkronExtendedVariants.Available, () => "Defaults", () => {
                AkronExtendedVariants.ResetExtended();
                Engine.Scene?.Add(new AkronToast("Extended variants reset."));
            }, "extended variant mode", "reset"),
            Action("Reset Vanilla", () => AkronExtendedVariants.Available, () => "Defaults", () => {
                AkronExtendedVariants.ResetVanilla();
                Engine.Scene?.Add(new AkronToast("Vanilla variants reset."));
            }, "extended variant mode", "reset")
        };

        if (!AkronExtendedVariants.Available) {
            return entries;
        }

        foreach (AkronExtendedVariantOption option in AkronExtendedVariants.GetOptionDefinitions()) {
            string label = ToExtendedVariantEntryLabel(option);
            if (option.CurrentValue is bool) {
                entries.Add(new OverlayEntry(
                    label,
                    () => AkronExtendedVariants.Available,
                    () => AkronExtendedVariants.DescribeConfiguredState(AkronExtendedVariants.GetOption(option.Name)),
                    () => {
                        if (!AkronModule.TryUse(AkronFeatureKind.ExtendedVariantMode)) {
                            return;
                        }

                        if (AkronExtendedVariants.TryToggleBoolean(option.Name, out string message)) {
                            Engine.Scene?.Add(new AkronToast(message));
                        }
                    },
                    BuildSearchTerms(label, new[] { "extended variant mode", option.Name, option.TypeName }),
                    true));
            } else {
                entries.Add(new OverlayEntry(
                    label,
                    () => AkronExtendedVariants.Available,
                    () => AkronExtendedVariants.DescribeConfiguredState(AkronExtendedVariants.GetOption(option.Name)),
                    () => ApplyOptionsPopupDelta(label, 1),
                    BuildSearchTerms(label, new[] { "extended variant mode", option.Name, option.TypeName }),
                    true));
            }
        }

        return entries;
    }
}
