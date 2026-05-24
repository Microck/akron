using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Celeste.Mod.Akron;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class RulesetManifestTests {
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static IEnumerable<object[]> BundledManifestFiles() {
        string directory = Path.Combine(AppContext.BaseDirectory, "rulesets");
        foreach (string path in Directory.GetFiles(directory, "*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase)) {
            yield return new object[] { path };
        }
    }

    [Fact]
    public void BundledRulesetDirectoryIsCopiedToTestOutput() {
        string directory = Path.Combine(AppContext.BaseDirectory, "rulesets");

        Assert.True(Directory.Exists(directory), $"Expected ruleset manifests at {directory}.");
        Assert.NotEmpty(Directory.GetFiles(directory, "*.json"));
    }

    [Theory]
    [MemberData(nameof(BundledManifestFiles))]
    public void BundledRulesetManifestFilesParseIntoTheRuntimeModel(string path) {
        AkronCommunityRulesetManifest? manifest = JsonSerializer.Deserialize<AkronCommunityRulesetManifest>(
            File.ReadAllText(path),
            JsonOptions);

        Assert.NotNull(manifest);
        Assert.False(string.IsNullOrWhiteSpace(manifest!.Id));
        Assert.False(string.IsNullOrWhiteSpace(manifest.Label));
        Assert.False(string.IsNullOrWhiteSpace(manifest.Description));
        Assert.True(manifest.PrimaryRuleset == null || Enum.IsDefined(manifest.PrimaryRuleset.Value));
        Assert.True(manifest.IndicatorVisibility == null || Enum.IsDefined(manifest.IndicatorVisibility.Value));
        Assert.True(manifest.IndicatorCorner == null || Enum.IsDefined(manifest.IndicatorCorner.Value));
    }
}
