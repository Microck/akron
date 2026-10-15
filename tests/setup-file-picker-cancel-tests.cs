using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class SetupFilePickerCancelTests {
    private const string Diagnostic = "Gtk-Message: 08:20:26.722: GtkDialog mapped without a transient parent. This is discouraged.";

    [Theory]
    [InlineData("zenity")]
    [InlineData("/usr/bin/zenity")]
    [InlineData("flatpak-spawn")]
    [InlineData("/usr/bin/flatpak-spawn")]
    public void KnownZenityDiagnosticDoesNotTurnCancelIntoFailure(string command) {
        Assert.True(AkronSetupPacks.IsFilePickerCancel(1, "", Diagnostic, command, new[] { "--host", "zenity", "--file-selection" }));
    }

    [Theory]
    [InlineData("Gtk-Message: GtkDialog mapped without a transient parent. This is discouraged.")]
    [InlineData("(zenity:1234): Gtk-Message: 08:20:26.722: GtkDialog mapped without a transient parent. This is discouraged.")]
    [InlineData(" \r\n" + Diagnostic + "\r\n" + Diagnostic + "\r\n ")]
    public void RecognizesOnlyNormalDiagnosticPrefixesAndWhitespace(string diagnostic) {
        Assert.True(AkronSetupPacks.IsFilePickerCancel(1, "  ", diagnostic, "zenity", new[] { "--file-selection" }));
    }

    [Theory]
    [InlineData("cannot open display")]
    [InlineData("Unknown option --invalid")]
    [InlineData("Failed to spawn child process")]
    [InlineData("unknown diagnostic")]
    public void MixedDiagnosticAndRealErrorRemainFailure(string error) {
        Assert.False(AkronSetupPacks.IsFilePickerCancel(1, "", Diagnostic + "\n" + error, "zenity", new[] { "--file-selection" }));
    }

    [Theory]
    [InlineData("powershell.exe", "--host", "zenity")]
    [InlineData("osascript", "--host", "zenity")]
    [InlineData("kdialog", "--host", "zenity")]
    [InlineData("flatpak-spawn", "--host", "kdialog")]
    [InlineData("flatpak-spawn", "--sandbox", "zenity")]
    public void DoesNotApplyZenityAllowanceToOtherCommands(string command, string first, string second) {
        Assert.False(AkronSetupPacks.IsFilePickerCancel(1, "", Diagnostic, command, new[] { first, second }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(255)]
    public void RequiresCancelExitCode(int code) {
        Assert.False(AkronSetupPacks.IsFilePickerCancel(code, "", Diagnostic, "zenity", new[] { "--file-selection" }));
    }

    [Fact]
    public void DoesNotTreatUnexpectedOutputAsKnownDiagnosticCancellation() {
        Assert.False(AkronSetupPacks.IsFilePickerCancel(1, "/tmp/setup.akr", Diagnostic, "zenity", new[] { "--file-selection" }));
    }

    [Fact]
    public void ExistingEmptyCancellationStillWorks() {
        Assert.True(AkronSetupPacks.IsFilePickerCancel(1, "", "", "zenity", new[] { "--file-selection" }));
    }
}
