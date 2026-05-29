using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Celeste.Mod.Akron;

internal enum AkronProfileFilePickerResult {
    Selected,
    Canceled,
    Failed
}

public static partial class AkronProfilePacks {
    internal static AkronProfileFilePickerResult TryPickProfileArchive(string initialDirectory, out string path, out string error) {
        path = string.Empty;
        error = string.Empty;
        string directory = string.IsNullOrWhiteSpace(initialDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : initialDirectory;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            return TryPickWindowsProfileArchive(directory, out path, out error);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            return TryPickMacProfileArchive(directory, out path, out error);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
            return TryPickLinuxProfileArchive(directory, out path, out error);
        }

        error = "Unsupported operating system.";
        return AkronProfileFilePickerResult.Failed;
    }

    private static AkronProfileFilePickerResult TryPickWindowsProfileArchive(string initialDirectory, out string path, out string error) {
        string command =
            "Add-Type -AssemblyName System.Windows.Forms; " +
            "$dialog = New-Object System.Windows.Forms.OpenFileDialog; " +
            "$dialog.Title = 'Import Akron profile'; " +
            "$dialog.Filter = 'Akron profile packs (*.akr)|*.akr|All files (*.*)|*.*'; " +
            "$dialog.InitialDirectory = [System.IO.Path]::GetFullPath($args[0]); " +
            "if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { [Console]::Out.Write($dialog.FileName) }";

        List<string> errors = new List<string>();
        AkronProfileFilePickerResult result = TryRunFilePickerProcess(
            "powershell.exe",
            new[] { "-NoProfile", "-STA", "-ExecutionPolicy", "Bypass", "-Command", command, initialDirectory },
            out path,
            out error);
        if (result != AkronProfileFilePickerResult.Failed) {
            return result;
        }
        errors.Add("powershell.exe: " + error);

        result = TryRunFilePickerProcess(
            "pwsh",
            new[] { "-NoProfile", "-Command", command, initialDirectory },
            out path,
            out error);
        if (result != AkronProfileFilePickerResult.Failed) {
            return result;
        }
        errors.Add("pwsh: " + error);

        error = string.Join(" | ", errors);
        return AkronProfileFilePickerResult.Failed;
    }

    private static AkronProfileFilePickerResult TryPickMacProfileArchive(string initialDirectory, out string path, out string error) {
        string directory = EnsureDirectorySeparator(initialDirectory);
        string[] arguments = {
            "-e", "on run argv",
            "-e", "set initialPath to item 1 of argv",
            "-e", "set selectedFile to choose file with prompt \"Import Akron profile\" default location POSIX file initialPath of type {\"akr\"}",
            "-e", "POSIX path of selectedFile",
            "-e", "end run",
            directory
        };
        return TryRunFilePickerProcess("osascript", arguments, out path, out error);
    }

    private static AkronProfileFilePickerResult TryPickLinuxProfileArchive(string initialDirectory, out string path, out string error) {
        path = string.Empty;
        string directory = EnsureDirectorySeparator(initialDirectory);
        List<string> errors = new List<string>();
        foreach ((string fileName, string[] arguments) in BuildLinuxProfilePickerCommands(directory)) {
            AkronProfileFilePickerResult result = TryRunFilePickerProcess(fileName, arguments, out path, out error);
            if (result != AkronProfileFilePickerResult.Failed) {
                return result;
            }

            errors.Add(fileName + ": " + error);
        }

        error = errors.Count == 0
            ? "No Linux file picker command was configured."
            : string.Join(" | ", errors);
        return AkronProfileFilePickerResult.Failed;
    }

    private static IEnumerable<(string FileName, string[] Arguments)> BuildLinuxProfilePickerCommands(string initialDirectory) {
        string[] zenityArguments = {
            "--file-selection",
            "--title=Import Akron profile",
            "--filename=" + initialDirectory,
            "--file-filter=Akron profile packs (*.akr) | *.akr",
            "--file-filter=All files | *"
        };
        string[] kdialogArguments = {
            "--title", "Import Akron profile",
            "--getopenfilename", initialDirectory,
            "Akron profile packs (*.akr)"
        };

        yield return ("/usr/bin/flatpak-spawn", new[] { "--host", "zenity" }.Concat(zenityArguments).ToArray());
        yield return ("flatpak-spawn", new[] { "--host", "zenity" }.Concat(zenityArguments).ToArray());
        yield return ("/usr/bin/flatpak-spawn", new[] { "--host", "kdialog" }.Concat(kdialogArguments).ToArray());
        yield return ("flatpak-spawn", new[] { "--host", "kdialog" }.Concat(kdialogArguments).ToArray());
        yield return ("zenity", zenityArguments);
        yield return ("kdialog", kdialogArguments);
    }

    private static AkronProfileFilePickerResult TryRunFilePickerProcess(string fileName, IReadOnlyList<string> arguments, out string path, out string error) {
        path = string.Empty;
        error = string.Empty;
        try {
            ProcessStartInfo startInfo = new ProcessStartInfo {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            foreach (string argument in arguments) {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(startInfo);
            if (process == null) {
                error = "process did not start.";
                return AkronProfileFilePickerResult.Failed;
            }

            process.WaitForExit();
            string stdout = process.StandardOutput.ReadToEnd().Trim();
            string stderr = process.StandardError.ReadToEnd().Trim();
            if (process.ExitCode == 0) {
                path = stdout;
                return string.IsNullOrWhiteSpace(path)
                    ? AkronProfileFilePickerResult.Canceled
                    : AkronProfileFilePickerResult.Selected;
            }

            if (IsFilePickerCancel(process.ExitCode, stdout, stderr)) {
                return AkronProfileFilePickerResult.Canceled;
            }

            error = stderr.Length > 0 ? stderr : (stdout.Length > 0 ? stdout : "exit " + process.ExitCode.ToString(CultureInfo.InvariantCulture));
            return AkronProfileFilePickerResult.Failed;
        } catch (Exception exception) when (exception is InvalidOperationException || exception is System.ComponentModel.Win32Exception) {
            error = exception.Message;
            return AkronProfileFilePickerResult.Failed;
        }
    }

    private static bool IsFilePickerCancel(int exitCode, string stdout, string stderr) {
        string text = ((stdout ?? string.Empty) + " " + (stderr ?? string.Empty)).Trim();
        return exitCode == 1 &&
               (string.IsNullOrWhiteSpace(text) ||
                text.IndexOf("cancel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("No file selected", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string EnsureDirectorySeparator(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return Path.DirectorySeparatorChar.ToString();
        }

        return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
               path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
