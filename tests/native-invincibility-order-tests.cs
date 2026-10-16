using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

[Collection(AkronSharedStateCollection.Name)]
public sealed class NativeInvincibilityOrderTests {
    private static string UpdateBody() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "../../../../Source/Module/akron-module-player-runtime.cs"))
        .Split("bool hazardAccuracyAllowed = IsHazardAccuracyAllowed();")[2]
        .Split("ApplyAutoDeafen(level, player);")[0];

    [Fact]
    public void NativeOwnershipIsEstablishedIndependentlyBeforeHazardRescue() {
        // Wiring guard; the actual activation orders are verified in native UI QA.
        string body = UpdateBody();
        Assert.Contains("bool nativeAssistInvincibilityAllowed = IsNativeAssistInvincibilityAllowed();", body);
        Assert.Contains("if (nativeAssistInvincibilityAllowed) {\n            EnsureNativeAssistInvincibility();\n        }\n\n        if (hazardAccuracyAllowed)", body);
        Assert.DoesNotContain("else if (IsNativeAssistInvincibilityAllowed())", body);
        Assert.Contains("RescueInvinciblePlayerFromBottomlessFall(level, player, true);", body);
        Assert.Contains("else if (IsAkronInvincibilityAllowed() && Settings.InvincibilityBottomlessFallRescue)", body);
        Assert.Contains("RescueInvinciblePlayerFromBottomlessFall(level, player, false);", body);
    }

    [Fact]
    public void DisabledOrDisallowedNativeModeRestoresBeforeMissingPlayerReturn() {
        string body = UpdateBody();
        int restore = body.IndexOf("if (!nativeAssistInvincibilityAllowed) {\n            RestoreNativeAssistInvincibility();", StringComparison.Ordinal);
        int missingPlayer = body.IndexOf("if (player == null)", StringComparison.Ordinal);
        Assert.True(restore >= 0 && missingPlayer > restore);
    }

    [NativeEverestFact]
    public void RepeatedNativeApplicationRestoresAllOriginalFlagCombinations() {
        var type = typeof(AkronModule);
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var captured = type.GetField("nativeAssistInvincibilityCaptured", flags)!;
        var mode = type.GetField("previousAssistMode", flags)!;
        var invincible = type.GetField("previousAssistInvincible", flags)!;
        var ensure = type.GetMethod("EnsureNativeAssistInvincibility", flags)!;
        var restore = type.GetMethod("RestoreNativeAssistInvincibility", flags)!;
        var originalSave = global::Celeste.SaveData.Instance;
        object oldCaptured = captured.GetValue(null)!;
        object oldMode = mode.GetValue(null)!;
        object oldInvincible = invincible.GetValue(null)!;
        try {
            foreach (bool initialMode in new[] { false, true }) {
                foreach (bool initialInvincible in new[] { false, true }) {
                    var save = (global::Celeste.SaveData)RuntimeHelpers.GetUninitializedObject(typeof(global::Celeste.SaveData));
                    save.AssistMode = initialMode;
                    save.Assists.Invincible = initialInvincible;
                    global::Celeste.SaveData.Instance = save;
                    captured.SetValue(null, false);
                    ensure.Invoke(null, null);
                    ensure.Invoke(null, null);
                    Assert.True(save.AssistMode);
                    Assert.True(save.Assists.Invincible);
                    restore.Invoke(null, null);
                    Assert.Equal(initialMode, save.AssistMode);
                    Assert.Equal(initialInvincible, save.Assists.Invincible);
                    Assert.False((bool)captured.GetValue(null)!);
                    restore.Invoke(null, null);
                    Assert.Equal(initialMode, save.AssistMode);
                    Assert.Equal(initialInvincible, save.Assists.Invincible);
                }
            }
        } finally {
            global::Celeste.SaveData.Instance = originalSave;
            captured.SetValue(null, oldCaptured);
            mode.SetValue(null, oldMode);
            invincible.SetValue(null, oldInvincible);
        }
    }
}
