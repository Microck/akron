using System;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.Json;
using Celeste.Mod.Akron;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class DiagnosticsTests {
    [Fact]
    public void DiagnosticsIssueFieldsStayTogetherWithTheLogReport() {
        string description = AkronDiagnosticsMenu.FormatDescription(
            "  StartPos fails after reload  ",
            "The room stays empty.",
            "1. Set a StartPos\n2. Reload the room",
            "The room restores.",
            "Map: example/map");
        AkronDiagnosticReport report = new AkronDiagnosticReport {
            ReportId = Guid.NewGuid().ToString("N"),
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            Description = description
        };
        report.Logs.Add(new AkronDiagnosticLog { Name = "log.txt", Text = "room reload" });

        using JsonDocument serialized = JsonDocument.Parse(AkronDiagnostics.SerializeBounded(report));
        JsonElement root = serialized.RootElement;
        Assert.Equal(
            "## Title\nStartPos fails after reload\n\n## What happened\nThe room stays empty.\n\n## Steps to reproduce\n1. Set a StartPos\n2. Reload the room\n\n## Expected behavior\nThe room restores.\n\n## Additional context\nMap: example/map",
            root.GetProperty("description").GetString());
        Assert.Equal("room reload", root.GetProperty("logs")[0].GetProperty("text").GetString());
    }

    [Fact]
    public void DiagnosticsCanSerializeLogsWithoutAnIssueDescription() {
        string description = AkronDiagnosticsMenu.FormatDescription(" ", "\n", "", "\t", "  ");
        AkronDiagnosticReport report = new AkronDiagnosticReport {
            ReportId = Guid.NewGuid().ToString("N"),
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            Description = description
        };
        report.Logs.Add(new AkronDiagnosticLog { Name = "log.txt", Text = "room reload" });

        using JsonDocument serialized = JsonDocument.Parse(AkronDiagnostics.SerializeBounded(report));
        Assert.Equal(string.Empty, serialized.RootElement.GetProperty("description").GetString());
        Assert.Equal("room reload", serialized.RootElement.GetProperty("logs")[0].GetProperty("text").GetString());
    }

    [Fact]
    public void DiagnosticsRedactCredentialsAndLocalIdentifiersWithoutDroppingUsefulLogLines() {
        string text = "transition a -> b\n" +
            "Authorization: Bearer private-auth-value\n" +
            "request bEaReR ab12\n" +
            "request Basic\tYWJjOmRlZg==\n" +
            "api_key=private-key-value\n" +
            "\"password\":\n  \"private-next-line-value\"\n" +
            "loaded /home/private-person/Games/Celeste/Mods/Akron.dll\n" +
            "loaded C:\\Users\\private-person\\Celeste\\Mods\\Akron.dll\n" +
            "service https://private-person:private-password@example.com/upload?signed=private-signature\n" +
            "private-person on private-computer\n" +
            "-----BEGIN PRIVATE KEY-----\nprivate-key-body\n-----END PRIVATE KEY-----\n" +
            "contact private-person@example.com\nroom=room-a elapsed=45ms\n";

        string redacted = AkronDiagnostics.Redact(text, AkronDiagnostics.CreatePrivateValuePattern(new[] { "private-person", "private-computer" }));

        foreach (string forbidden in new[] { "Bearer", "ab12", "Basic", "YWJjOmRlZg==", "private-auth-value", "private-key-value", "private-next-line-value", "private-person", "private-password", "private-signature", "private-computer", "private-key-body" }) {
            Assert.DoesNotContain(forbidden, redacted, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains("transition a -> b", redacted);
        Assert.Contains("room=room-a elapsed=45ms", redacted);
    }

    [Fact]
    public void DiagnosticsPrivateValuesCannotExpandTheirOwnRedactionMarkers() {
        string text = string.Concat(Enumerable.Repeat("e ", 4096));

        string redacted = AkronDiagnostics.Redact(text, AkronDiagnostics.CreatePrivateValuePattern(new[] { "e", "e", "local" }));

        Assert.True(redacted.Length <= text.Length * 32, "Redaction must not repeatedly expand previously inserted markers.");
    }

    [Fact]
    public void DiagnosticsTailDropsPartialCredentialLinesAndBoundsUtf8() {
        string path = Path.Combine(Path.GetTempPath(), "akron-diagnostic-tail-" + Guid.NewGuid().ToString("N"));
        try {
            File.WriteAllText(path, "Authorization: Bearer " + new string('x', 1000) + "\nroom=café\n", new UTF8Encoding(false));
            AkronDiagnosticLog log = AkronDiagnostics.ReadLogTail(Path.GetTempPath(), path, "log.txt", 64, null);

            Assert.True(log.Truncated);
            Assert.Equal("room=café\n", log.Text);
            Assert.True(Encoding.UTF8.GetByteCount(log.Text) <= 64);
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void DiagnosticsTrustTheGameRootButRefuseLinkedLogsAndOutsideFiles() {
        string directory = Path.Combine(Path.GetTempPath(), "akron-diagnostic-links-" + Guid.NewGuid().ToString("N"));
        string game = Path.Combine(directory, "game");
        string trustedRoot = Path.Combine(directory, "game-link");
        string outside = Path.Combine(directory, "outside");
        Directory.CreateDirectory(game);
        Directory.CreateDirectory(outside);
        try {
            Directory.CreateSymbolicLink(trustedRoot, game);
            File.WriteAllText(Path.Combine(game, "log.txt"), "room=room-a elapsed=45ms");
            File.WriteAllText(Path.Combine(outside, "log.txt"), "not a game log");

            AkronDiagnosticLog log = AkronDiagnostics.ReadLogTail(
                trustedRoot, Path.Combine(trustedRoot, "log.txt"), "log.txt", 1024, null);
            Assert.Equal("room=room-a elapsed=45ms", log.Text);

            File.CreateSymbolicLink(Path.Combine(game, "linked.log"), Path.Combine(outside, "log.txt"));
            Directory.CreateSymbolicLink(Path.Combine(game, "Saves"), outside);
            foreach (string refused in new[] {
                Path.Combine(trustedRoot, "linked.log"),
                Path.Combine(trustedRoot, "Saves", "log.txt"),
                Path.Combine(outside, "log.txt")
            }) {
                Assert.Throws<IOException>(() => AkronDiagnostics.ReadLogTail(trustedRoot, refused, "log.txt", 1024, null));
            }
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DiagnosticsBoundSerializedEscapesAndPreserveUtf8Tail() {
        AkronDiagnosticReport report = new AkronDiagnosticReport {
            ReportId = Guid.NewGuid().ToString("N"),
            CreatedUtc = DateTime.UtcNow.ToString("O")
        };
        foreach (string name in new[] { "log.txt", "akron-current.log", "akron-previous.log", "performance.jsonl" }) {
            report.Logs.Add(new AkronDiagnosticLog {
                Name = name,
                Text = string.Concat(Enumerable.Repeat("\u0001\"é𝄞", 150000)) + "\nlatest room sample"
            });
        }

        byte[] body = AkronDiagnostics.SerializeBounded(report);

        Assert.True(body.Length <= AkronDiagnostics.MaxReportBytes);
        using JsonDocument json = JsonDocument.Parse(body);
        foreach (JsonElement log in json.RootElement.GetProperty("logs").EnumerateArray()) {
            string text = log.GetProperty("text").GetString()!;
            Assert.True(Encoding.UTF8.GetByteCount(text) <= AkronDiagnostics.MaxLogBytes);
            Assert.True(log.GetProperty("truncated").GetBoolean());
            Assert.EndsWith("latest room sample", text);
            Assert.DoesNotContain("\uFFFD", text);
        }
    }
}
