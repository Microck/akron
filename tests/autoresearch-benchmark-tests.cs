using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

[Collection("Performance")]
public sealed class AutoresearchBenchmarkTests {
    private const int ExpectedVanillaSnapshots = 11;
    private const int ExpectedModdedSnapshots = 12;

    [Fact]
    public void AutoresearchCorpusBenchmark() {
        // This benchmark only runs under the autoresearch harness, which supplies
        // the corpus and result locations. Ordinary CI and contributor test runs do
        // not set them, so return a passing no-op instead of failing the suite.
        // Supplying only one of the two is a harness error and still fails loudly.
        string? configuredCorpusRoot = Environment.GetEnvironmentVariable("AKRON_AUTORESEARCH_CORPUS_ROOT");
        string? configuredResultPath = Environment.GetEnvironmentVariable("AKRON_AUTORESEARCH_RESULT");
        if (string.IsNullOrWhiteSpace(configuredCorpusRoot) && string.IsNullOrWhiteSpace(configuredResultPath)) {
            return;
        }

        string corpusRoot = RequireEnvironment("AKRON_AUTORESEARCH_CORPUS_ROOT");
        string resultPath = RequireEnvironment("AKRON_AUTORESEARCH_RESULT");
        string workRoot = Path.Combine(Path.GetDirectoryName(resultPath)!, "benchmark-work");
        string outputRoot = Path.Combine(workRoot, "candidate");
        string warmupRoot = Path.Combine(workRoot, "warmup");

        List<SnapshotFixture> fixtures = new List<SnapshotFixture>();
        fixtures.AddRange(ReadManifest(Path.Combine(corpusRoot, "corpus"), "vanilla", ExpectedVanillaSnapshots));
        fixtures.AddRange(ReadManifest(Path.Combine(corpusRoot, "corpus-modded"), null, ExpectedModdedSnapshots));
        Assert.Equal(ExpectedVanillaSnapshots + ExpectedModdedSnapshots, fixtures.Count);

        foreach (SnapshotFixture fixture in fixtures) {
            VerifyDigest(fixture);
            fixture.SourceReport = AkronSnapshotComposition.AnalyzeFile(fixture.Path);
        }

        if (Directory.Exists(workRoot)) {
            Directory.Delete(workRoot, recursive: true);
        }
        Directory.CreateDirectory(outputRoot);
        Directory.CreateDirectory(warmupRoot);
        PrepareAssemblyResolver(corpusRoot, Path.Combine(workRoot, "assemblies"));

        // Warm the reflection, serializer, type-name and bounded-reader paths before
        // allocation accounting. Each dotnet test invocation is a fresh process, so
        // this produces the same cache state for every candidate.
        RunSyntheticSnapshot(warmupRoot, "warmup", measureCorrectness: true);
        foreach (SnapshotFixture warm in fixtures.GroupBy(fixture => fixture.Cohort).Select(group => group.First())) {
            string cohortWarmup = Path.Combine(warmupRoot, warm.Cohort);
            Directory.CreateDirectory(cohortWarmup);
            VerifyCorpusRewrite(warm, RewriteCorpusSnapshot(warm, cohortWarmup));
        }
        Directory.Delete(warmupRoot, recursive: true);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Process process = Process.GetCurrentProcess();
        double workingMs = 0;
        double cpuMs = 0;

        process.Refresh();
        TimeSpan cpuBefore = process.TotalProcessorTime;
        Stopwatch working = Stopwatch.StartNew();
        long syntheticBefore = GC.GetAllocatedBytesForCurrentThread();
        RunSyntheticSnapshot(outputRoot, "measured-synthetic", measureCorrectness: false);
        long syntheticAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - syntheticBefore;
        working.Stop();
        process.Refresh();
        workingMs += working.Elapsed.TotalMilliseconds;
        cpuMs += (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
        Assert.True(syntheticAllocatedBytes > 0);
        VerifySyntheticSnapshot(outputRoot, "measured-synthetic");

        long corpusAllocatedBytes = 0;
        long vanillaAllocatedBytes = 0;
        long cookieAllocatedBytes = 0;
        long hyperlifeAllocatedBytes = 0;
        long dsidesAllocatedBytes = 0;
        long sourceCompressedBytes = 0;
        long candidateCompressedBytes = 0;
        long decompressedBytes = 0;

        foreach (SnapshotFixture fixture in fixtures) {
            string fixtureOutput = Path.Combine(outputRoot, fixture.Cohort, fixture.Ordinal.ToString("D2", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(fixtureOutput);

            process.Refresh();
            cpuBefore = process.TotalProcessorTime;
            working.Restart();
            long before = GC.GetAllocatedBytesForCurrentThread();
            CorpusRewrite rewrite = RewriteCorpusSnapshot(fixture, fixtureOutput);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            working.Stop();
            process.Refresh();
            workingMs += working.Elapsed.TotalMilliseconds;
            cpuMs += (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
            Assert.True(allocated > 0, fixture.RelativePath);

            corpusAllocatedBytes = checked(corpusAllocatedBytes + allocated);
            switch (fixture.Cohort) {
                case "vanilla": vanillaAllocatedBytes = checked(vanillaAllocatedBytes + allocated); break;
                case "cookie": cookieAllocatedBytes = checked(cookieAllocatedBytes + allocated); break;
                case "hyperlife": hyperlifeAllocatedBytes = checked(hyperlifeAllocatedBytes + allocated); break;
                case "dsides": dsidesAllocatedBytes = checked(dsidesAllocatedBytes + allocated); break;
                default: throw new InvalidOperationException("Unknown corpus cohort: " + fixture.Cohort);
            }

            VerifyCorpusRewrite(fixture, rewrite);
            sourceCompressedBytes = checked(sourceCompressedBytes + fixture.SourceReport.CompressedBytes);
            candidateCompressedBytes = checked(candidateCompressedBytes + new FileInfo(rewrite.OutputPath).Length);
            decompressedBytes = checked(decompressedBytes + fixture.SourceReport.DecompressedBytes);
        }

        process.Refresh();
        long peakWorkingSetBytes = process.PeakWorkingSet64;

        Assert.True(corpusAllocatedBytes > 0);
        Assert.True(sourceCompressedBytes > 0);
        Assert.True(candidateCompressedBytes > 0);
        Assert.True(decompressedBytes > 0);
        Assert.True(vanillaAllocatedBytes > 0);
        Assert.True(cookieAllocatedBytes > 0);
        Assert.True(hyperlifeAllocatedBytes > 0);
        Assert.True(dsidesAllocatedBytes > 0);
        Assert.True(workingMs > 0);
        Assert.True(cpuMs > 0);
        Assert.True(peakWorkingSetBytes > 0);

        WriteResultAtomically(resultPath, new Dictionary<string, string>(StringComparer.Ordinal) {
            ["snapshot_count"] = fixtures.Count.ToString(CultureInfo.InvariantCulture),
            ["source_compressed_bytes"] = sourceCompressedBytes.ToString(CultureInfo.InvariantCulture),
            ["candidate_compressed_bytes"] = candidateCompressedBytes.ToString(CultureInfo.InvariantCulture),
            ["decompressed_bytes"] = decompressedBytes.ToString(CultureInfo.InvariantCulture),
            ["corpus_allocated_bytes"] = corpusAllocatedBytes.ToString(CultureInfo.InvariantCulture),
            ["synthetic_allocated_bytes"] = syntheticAllocatedBytes.ToString(CultureInfo.InvariantCulture),
            ["vanilla_allocated_bytes"] = vanillaAllocatedBytes.ToString(CultureInfo.InvariantCulture),
            ["cookie_allocated_bytes"] = cookieAllocatedBytes.ToString(CultureInfo.InvariantCulture),
            ["hyperlife_allocated_bytes"] = hyperlifeAllocatedBytes.ToString(CultureInfo.InvariantCulture),
            ["dsides_allocated_bytes"] = dsidesAllocatedBytes.ToString(CultureInfo.InvariantCulture),
            ["working_ms"] = workingMs.ToString("0.###", CultureInfo.InvariantCulture),
            ["cpu_ms"] = cpuMs.ToString("0.###", CultureInfo.InvariantCulture),
            ["peak_working_set_bytes"] = peakWorkingSetBytes.ToString(CultureInfo.InvariantCulture),
        });
    }

    private static List<SnapshotFixture> ReadManifest(string directory, string? fixedCohort, int expectedCount) {
        string manifestPath = Path.Combine(directory, "manifest.sha256");
        Assert.True(File.Exists(manifestPath), "Missing corpus manifest: " + manifestPath);
        string directoryFull = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        List<SnapshotFixture> fixtures = new List<SnapshotFixture>();

        foreach (string rawLine in File.ReadAllLines(manifestPath)) {
            string line = rawLine.TrimEnd();
            Assert.True(line.Length > 66 && line[64] == ' ' && line[65] == ' ', "Malformed manifest line: " + rawLine);
            string expectedDigest = line.Substring(0, 64);
            Assert.True(expectedDigest.All(IsLowerHex), "Malformed SHA-256 in manifest: " + rawLine);
            string relativePath = line.Substring(66);
            Assert.False(Path.IsPathRooted(relativePath), "Manifest path must be relative: " + relativePath);

            string fullPath = Path.GetFullPath(Path.Combine(directory, relativePath));
            Assert.StartsWith(directoryFull, fullPath, StringComparison.Ordinal);
            Assert.True(seen.Add(fullPath), "Duplicate manifest path: " + relativePath);
            Assert.True(File.Exists(fullPath), "Manifest snapshot missing: " + fullPath);

            string cohort = fixedCohort ?? ClassifyModdedCohort(relativePath);
            fixtures.Add(new SnapshotFixture(fixtures.Count, cohort, relativePath, fullPath, expectedDigest));
        }

        string[] actualFiles = Directory.EnumerateFiles(directory, "*.json.gz", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        string[] manifestFiles = seen.OrderBy(path => path, StringComparer.Ordinal).ToArray();
        Assert.Equal(manifestFiles, actualFiles);
        Assert.Equal(expectedCount, fixtures.Count);
        return fixtures;
    }

    private static string ClassifyModdedCohort(string relativePath) {
        if (relativePath.Contains("SpringCollab2020", StringComparison.Ordinal)) {
            return "cookie";
        }
        if (relativePath.Contains("StrawberryJam2021", StringComparison.Ordinal)) {
            return "hyperlife";
        }
        if (relativePath.Contains("monikadsidespack", StringComparison.Ordinal)) {
            return "dsides";
        }
        throw new InvalidOperationException("Unclassified modded snapshot: " + relativePath);
    }

    private static bool IsLowerHex(char value) => value is >= '0' and <= '9' or >= 'a' and <= 'f';

    private static void PrepareAssemblyResolver(string corpusRoot, string extractionDirectory) {
        string gameRoot = Path.Combine(corpusRoot, "game", "Celeste");
        string modsRoot = Path.Combine(gameRoot, "Mods");
        Assert.True(Directory.Exists(gameRoot), "Missing sandbox game root: " + gameRoot);
        Assert.True(Directory.Exists(modsRoot), "Missing sandbox mods root: " + modsRoot);
        Directory.CreateDirectory(extractionDirectory);

        List<string> candidates = Directory.EnumerateFiles(gameRoot, "*.dll", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
        candidates.AddRange(Directory.EnumerateFiles(modsRoot, "*.dll", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal));

        int archiveOrdinal = 0;
        foreach (string archivePath in Directory.EnumerateFiles(modsRoot, "*.zip", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.Ordinal)) {
            using ZipArchive archive = ZipFile.OpenRead(archivePath);
            int entryOrdinal = 0;
            foreach (ZipArchiveEntry entry in archive.Entries
                         .Where(entry => entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(entry => entry.FullName, StringComparer.Ordinal)) {
                string extracted = Path.Combine(
                    extractionDirectory,
                    archiveOrdinal.ToString("D3", CultureInfo.InvariantCulture) + "-" +
                    entryOrdinal.ToString("D3", CultureInfo.InvariantCulture) + "-" + Path.GetFileName(entry.FullName));
                entry.ExtractToFile(extracted, overwrite: true);
                candidates.Add(extracted);
                entryOrdinal++;
            }
            archiveOrdinal++;
        }

        Dictionary<string, (Version Version, string Path)> assemblies =
            new Dictionary<string, (Version, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (string candidate in candidates) {
            try {
                AssemblyName name = AssemblyName.GetAssemblyName(candidate);
                if (string.IsNullOrWhiteSpace(name.Name)) {
                    continue;
                }
                Version version = name.Version ?? new Version(0, 0);
                if (!assemblies.TryGetValue(name.Name, out (Version Version, string Path) existing) ||
                    version > existing.Version) {
                    assemblies[name.Name] = (version, candidate);
                }
            } catch (BadImageFormatException) {
                // Native DLL in a mod archive; it cannot satisfy a managed assembly request.
            } catch (FileLoadException) {
                // Invalid managed metadata is likewise not a usable resolver candidate.
            }
        }

        AssemblyLoadContext.Default.Resolving += (context, name) => {
            if (name.Name != null && assemblies.TryGetValue(name.Name, out (Version Version, string Path) candidate)) {
                return context.LoadFromAssemblyPath(candidate.Path);
            }
            return null;
        };
    }

    private static void VerifyDigest(SnapshotFixture fixture) {
        using FileStream stream = File.OpenRead(fixture.Path);
        string actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        Assert.Equal(fixture.ExpectedDigest, actual);
    }

    private static CorpusRewrite RewriteCorpusSnapshot(
        SnapshotFixture fixture,
        string outputDirectory
    ) {
        using FileStream source = File.OpenRead(fixture.Path);
        Assert.True(
            AkronStartPosReconstruction.TryReadSnapshot(source, out AkronReconstructionDocument document, out string readError),
            fixture.RelativePath + ": " + readError);

        Assert.True(AkronStartPosReconstruction.SaveSnapshot(
            document.SlotName,
            document.MapSid,
            document.Room,
            document.FileSlot,
            document,
            out string writeError,
            outputDirectory), fixture.RelativePath + ": " + writeError);

        string outputPath = AkronStartPosReconstruction.GetSnapshotPath(document.SlotName, outputDirectory);
        return new CorpusRewrite(outputPath, document);
    }

    private static void VerifyCorpusRewrite(SnapshotFixture fixture, CorpusRewrite rewrite) {
        using FileStream candidate = File.OpenRead(rewrite.OutputPath);
        Assert.True(
            AkronStartPosReconstruction.TryReadSnapshot(candidate, out AkronReconstructionDocument roundTrip, out string roundTripError),
            fixture.RelativePath + ": " + roundTripError);
        AkronReconstructionDocument document = rewrite.SourceDocument;
        Assert.Equal(document.SlotName, roundTrip.SlotName);
        Assert.Equal(document.MapSid, roundTrip.MapSid);
        Assert.Equal(document.Room, roundTrip.Room);
        Assert.Equal(document.FileSlot, roundTrip.FileSlot);
        Assert.Equal(document.RootNodeId, roundTrip.RootNodeId);
        Assert.Equal(document.Nodes.Count, roundTrip.Nodes.Count);
        Assert.Equal(document.RegisteredActionIds.Count, roundTrip.RegisteredActionIds.Count);
        Assert.Equal(document.GameplayBuffers.Count, roundTrip.GameplayBuffers.Count);
        Assert.Equal(document.ActionStateDocument?.Nodes.Count ?? 0, roundTrip.ActionStateDocument?.Nodes.Count ?? 0);

        AkronSnapshotComposition.Report output = AkronSnapshotComposition.AnalyzeFile(rewrite.OutputPath);
        AkronSnapshotComposition.Report source = fixture.SourceReport;
        Assert.Equal(source.SlotName, output.SlotName);
        Assert.Equal(source.MapSid, output.MapSid);
        Assert.Equal(source.Room, output.Room);
        Assert.Equal(source.NodeCount, output.NodeCount);
    }

    private static void RunSyntheticSnapshot(string directory, string slotName, bool measureCorrectness) {
        AkronReconstructionGraph graph = new AkronReconstructionGraph(_ => false);
        SyntheticNode saved = BuildSyntheticChain(600, valueOffset: 10);
        SyntheticNode baseline = BuildSyntheticChain(600, valueOffset: 0);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        Assert.True(AkronStartPosReconstruction.SaveSnapshot(
            slotName, "Autoresearch/Synthetic", "fixed-room", 0, capture.Document, out string error, directory), error);
        if (measureCorrectness) {
            VerifySyntheticSnapshot(directory, slotName);
        }
    }

    private static void VerifySyntheticSnapshot(string directory, string slotName) {
        string path = AkronStartPosReconstruction.GetSnapshotPath(slotName, directory);
        using FileStream stream = File.OpenRead(path);
        Assert.True(AkronStartPosReconstruction.TryReadSnapshot(
            stream, out AkronReconstructionDocument document, out string readError), readError);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(_ => false);
        SyntheticNode target = BuildSyntheticChain(600, valueOffset: 0);
        AkronReconstructionRestore restore = graph.Restore(document, target);
        Assert.True(restore.Success, restore.Error);
        SyntheticNode current = target;
        for (int index = 0; index < 600; index++) {
            Assert.Equal(10 + index, current.Value);
            Assert.Equal(10 + index, current.Payload[0]);
            if (index < 599) {
                Assert.NotNull(current.Next);
                current = current.Next;
            }
        }
    }

    private static SyntheticNode BuildSyntheticChain(int count, int valueOffset) {
        SyntheticNode root = NewSyntheticNode(valueOffset);
        SyntheticNode current = root;
        for (int index = 1; index < count; index++) {
            current.Next = NewSyntheticNode(valueOffset + index);
            current = current.Next;
        }
        return root;
    }

    private static SyntheticNode NewSyntheticNode(int value) {
        int[] payload = new int[64];
        for (int index = 0; index < payload.Length; index++) {
            payload[index] = value + index;
        }
        return new SyntheticNode {
            Value = value,
            Label = "fixed-synthetic-label-" + (value % 17).ToString(CultureInfo.InvariantCulture),
            Payload = payload,
        };
    }

    private static string RequireEnvironment(string name) {
        string? value = Environment.GetEnvironmentVariable(name);
        Assert.False(string.IsNullOrWhiteSpace(value), "Set " + name + " for the autoresearch benchmark.");
        return value!;
    }

    private static void WriteResultAtomically(string resultPath, IReadOnlyDictionary<string, string> values) {
        Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
        string temporaryPath = resultPath + ".tmp";
        File.WriteAllLines(temporaryPath, values.Select(entry => entry.Key + "=" + entry.Value));
        File.Move(temporaryPath, resultPath, overwrite: true);
    }

    private sealed class SnapshotFixture {
        public SnapshotFixture(int ordinal, string cohort, string relativePath, string path, string expectedDigest) {
            Ordinal = ordinal;
            Cohort = cohort;
            RelativePath = relativePath;
            Path = path;
            ExpectedDigest = expectedDigest;
        }

        public int Ordinal { get; }
        public string Cohort { get; }
        public string RelativePath { get; }
        public string Path { get; }
        public string ExpectedDigest { get; }
        public AkronSnapshotComposition.Report SourceReport { get; set; } = null!;
    }

    private sealed class CorpusRewrite {
        public CorpusRewrite(string outputPath, AkronReconstructionDocument sourceDocument) {
            OutputPath = outputPath;
            SourceDocument = sourceDocument;
        }

        public string OutputPath { get; }
        public AkronReconstructionDocument SourceDocument { get; }
    }

    private sealed class SyntheticNode {
        public int Value;
        public string Label = string.Empty;
        public int[] Payload = Array.Empty<int>();
        public SyntheticNode? Next;
    }
}
