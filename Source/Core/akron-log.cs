using Celeste.Mod;
using Monocle;
using System;
using System.Globalization;
using System.IO;

namespace Celeste.Mod.Akron;

public static class AkronLog {
    private const string DirectoryName = "AkronLogs";
    private const string CurrentFileName = "akron-current.log";

    public static void Normal(string source, string message) {
        Write(AkronLoggingLevel.Normal, source, message, mirrorLogLevel: null);
    }

    public static void Verbose(string source, string message) {
        Write(AkronLoggingLevel.Verbose, source, message, mirrorLogLevel: null);
    }

    public static void Trace(string source, string message) {
        Write(AkronLoggingLevel.Trace, source, message, mirrorLogLevel: null);
    }

    public static void Info(string source, string message) {
        Write(AkronLoggingLevel.Normal, source, message, mirrorLogLevel: null);
    }

    public static void Warn(string source, string message) {
        Write(AkronLoggingLevel.Normal, source, message, LogLevel.Warn);
    }

    public static void Error(string source, string message) {
        Write(AkronLoggingLevel.Normal, source, message, LogLevel.Error);
    }

    public static string GetLogDirectory() {
        return Path.Combine(Everest.PathGame, "Saves", DirectoryName);
    }

    public static string GetCurrentLogPath() {
        return Path.Combine(GetLogDirectory(), CurrentFileName);
    }

    public static string DescribeSettings() {
        AkronModuleSettings settings = AkronModule.Settings;
        return (settings.Logging ? "On" : "Off") +
               " / " + FormatLevel(settings.LoggingLevel) +
               " / " + settings.LoggingMaxFileSizeMb.ToString(CultureInfo.InvariantCulture) + " MB x " +
               settings.LoggingRetainedFiles.ToString(CultureInfo.InvariantCulture);
    }

    public static string FormatLevel(AkronLoggingLevel level) {
        return AkronModuleSettings.NormalizeLoggingLevel(level) switch {
            AkronLoggingLevel.Normal => "Normal",
            AkronLoggingLevel.Verbose => "Verbose",
            _ => "Trace"
        };
    }

    public static void LogSettingsChanged(string detail) {
        Normal(nameof(AkronLog), "logging settings changed: " + detail + "; " + DescribeSettings());
    }

    private static void Write(AkronLoggingLevel level, string source, string message, LogLevel? mirrorLogLevel) {
        AkronModuleSettings settings = AkronModule.Settings;
        string safeSource = string.IsNullOrWhiteSpace(source) ? nameof(AkronLog) : source.Trim();
        string safeMessage = RedactForStreamerMode(message ?? string.Empty, settings.StreamerMode);

        if (settings.Logging && ShouldWrite(level, settings.LoggingLevel)) {
            WriteFileLine(level, safeSource, safeMessage, settings);
        }

        if (mirrorLogLevel.HasValue && settings.LoggingMirrorWarningsToEverest) {
            Logger.Log(mirrorLogLevel.Value, safeSource, safeMessage);
        }
    }

    private static bool ShouldWrite(AkronLoggingLevel level, AkronLoggingLevel configured) {
        return AkronModuleSettings.NormalizeLoggingLevel(level) <= AkronModuleSettings.NormalizeLoggingLevel(configured);
    }

    private static void WriteFileLine(AkronLoggingLevel level, string source, string message, AkronModuleSettings settings) {
        try {
            string directory = GetLogDirectory();
            Directory.CreateDirectory(directory);
            string current = Path.Combine(directory, CurrentFileName);
            RotateIfNeeded(current, settings);
            string line = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) +
                          " " + FormatLevel(level).ToUpperInvariant() +
                          " " + source +
                          " " + message +
                          Environment.NewLine;
            File.AppendAllText(current, line);
        } catch (Exception exception) {
            if (settings.LoggingMirrorWarningsToEverest) {
                Logger.Log(LogLevel.Warn, nameof(AkronLog), "Failed to write Akron log file: " + exception.Message);
            }
        }
    }

    private static void RotateIfNeeded(string current, AkronModuleSettings settings) {
        if (!File.Exists(current)) {
            return;
        }

        long maxBytes = Math.Max(1, AkronModuleSettings.ClampLoggingMaxFileSizeMb(settings.LoggingMaxFileSizeMb)) * 1024L * 1024L;
        FileInfo info = new FileInfo(current);
        if (info.Length < maxBytes) {
            return;
        }

        int retainedFiles = AkronModuleSettings.ClampLoggingRetainedFiles(settings.LoggingRetainedFiles);
        string directory = Path.GetDirectoryName(current) ?? GetLogDirectory();
        for (int index = retainedFiles; index >= 1; index--) {
            string source = Path.Combine(directory, "akron-" + index.ToString(CultureInfo.InvariantCulture) + ".log");
            string destination = Path.Combine(directory, "akron-" + (index + 1).ToString(CultureInfo.InvariantCulture) + ".log");
            if (index == retainedFiles && File.Exists(source)) {
                File.Delete(source);
                continue;
            }

            if (File.Exists(source)) {
                if (File.Exists(destination)) {
                    File.Delete(destination);
                }

                File.Move(source, destination);
            }
        }

        string first = Path.Combine(directory, "akron-1.log");
        if (File.Exists(first)) {
            File.Delete(first);
        }

        if (retainedFiles > 0) {
            File.Move(current, first);
        } else {
            File.Delete(current);
        }
    }

    private static string RedactForStreamerMode(string message, bool streamerMode) {
        if (!streamerMode || string.IsNullOrWhiteSpace(message)) {
            return message;
        }

        string gamePath = Everest.PathGame;
        if (string.IsNullOrWhiteSpace(gamePath)) {
            return message;
        }

        string trimmed = gamePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string redacted = AkronModuleSettings.FormatPathForDisplay(trimmed, streamerMode);
        return message
            .Replace(trimmed, redacted)
            .Replace(trimmed.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), redacted);
    }
}
