using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public sealed class AkronCommunityPackUploadDraft {
    public string MapSid { get; set; } = string.Empty;
    public string MapDisplayName { get; set; } = string.Empty;
    public AkronSetupSection Section { get; set; } = AkronSetupSection.StartPos;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AttributionMode { get; set; } = AkronCommunityPackUploads.AnonymousAttribution;
    public string DiscordUserId { get; set; } = string.Empty;
}

public sealed class AkronCommunityPackUploadPrepareRequest {
    public string InstallId { get; set; } = string.Empty;
    public int TermsVersion { get; set; } = AkronCommunityPackUploads.CurrentTermsVersion;
    public AkronCommunityPackUploadCaptureInput Capture { get; set; } = new AkronCommunityPackUploadCaptureInput();
    public System.Collections.Generic.List<AkronCommunityPackUploadSubmissionInput> Submissions { get; set; } = new System.Collections.Generic.List<AkronCommunityPackUploadSubmissionInput>();
}

public sealed class AkronCommunityPackUploadCaptureInput {
    public long SizeBytes { get; set; }
    public string ContentType { get; set; } = "image/png";
}

public sealed class AkronCommunityPackUploadSubmissionInput {
    public AkronSetupSection Section { get; set; } = AkronSetupSection.StartPos;
    public string MapSid { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long PackSizeBytes { get; set; }
    public AkronCommunityPackUploadAttributionInput Attribution { get; set; } = new AkronCommunityPackUploadAttributionInput();
}

public sealed class AkronCommunityPackUploadAttributionInput {
    public string Mode { get; set; } = AkronCommunityPackUploads.AnonymousAttribution;
    public string DiscordUserId { get; set; } = string.Empty;
}

public sealed class AkronCommunityPackUploadPreparedResponse {
    public string BatchId { get; set; } = string.Empty;
    public string ExpiresUtc { get; set; } = string.Empty;
    public AkronCommunityPackPreparedObject Capture { get; set; } = new AkronCommunityPackPreparedObject();
    public System.Collections.Generic.List<AkronCommunityPackPreparedSubmission> Submissions { get; set; } = new System.Collections.Generic.List<AkronCommunityPackPreparedSubmission>();
}

public sealed class AkronCommunityPackPreparedSubmission {
    public string SubmissionId { get; set; } = string.Empty;
    public AkronCommunityPackPreparedObject Pack { get; set; } = new AkronCommunityPackPreparedObject();
}

public sealed class AkronCommunityPackPreparedObject {
    public string ObjectId { get; set; } = string.Empty;
    public string UploadUrl { get; set; } = string.Empty;
    public long MaxBytes { get; set; }
}

public sealed class AkronCommunityPackUploadCompleteResponse {
    public string BatchId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public static class AkronCommunityPackUploads {
    public const string DefaultUploadEndpoint = "https://akron.micr.dev/uploads";
    public const int CurrentTermsVersion = 1;
    public const string AnonymousAttribution = "anonymous";
    public const string DiscordAttribution = "discord";
    private static readonly JsonSerializerOptions UploadJsonOptions = new JsonSerializerOptions {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly JsonSerializerOptions SetupPackJsonOptions = new JsonSerializerOptions {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly HttpClient UploadHttp = new HttpClient {
        Timeout = TimeSpan.FromMinutes(10)
    };
    private static bool uploadInProgress;

    public static bool IsSupportedUploadSection(AkronSetupSection section) {
        return section == AkronSetupSection.StartPos ||
               section == AkronSetupSection.AutoKill ||
               section == AkronSetupSection.AutoDeafen;
    }

    public static AkronSetupSection NormalizeUploadSection(AkronSetupSection section) {
        return IsSupportedUploadSection(section) ? section : AkronSetupSection.StartPos;
    }

    internal static bool IsUploadInProgress {
        get { return uploadInProgress; }
    }

    internal static bool TryReserveUploadSlot() {
        if (uploadInProgress) {
            return false;
        }

        uploadInProgress = true;
        return true;
    }

    internal static void ReleaseUploadSlot() {
        uploadInProgress = false;
    }

    public static string EnsureInstallId(AkronModuleSettings settings) {
        if (settings == null) {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(settings.CommunityPackUploadInstallId)) {
            settings.CommunityPackUploadInstallId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        }

        return settings.CommunityPackUploadInstallId;
    }

    public static string ResolveEndpoint(string endpoint) {
        return string.IsNullOrWhiteSpace(endpoint) ? DefaultUploadEndpoint : endpoint.Trim().TrimEnd('/');
    }

    public static string GetTempUploadDirectory() {
        return Path.Combine(Path.GetTempPath(), "AkronCommunityPackUploads");
    }

    public static string WriteTempArchive(AkronSetupSection section, string title, string mapSid) {
        if (!IsSupportedUploadSection(section)) {
            throw new InvalidOperationException("Only StartPos, Auto Kill, and Auto Deafen packs can be uploaded.");
        }

        Directory.CreateDirectory(GetTempUploadDirectory());
        string fileName = SanitizeFileName(string.IsNullOrWhiteSpace(title) ? GenerateTitle("Akron", section) : title)
                          + "-"
                          + DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture)
                          + AkronArchive.Extension;
        string path = Path.Combine(GetTempUploadDirectory(), fileName);
        AkronSetupPack pack = BuildScopedUploadPack(AkronModule.Settings, AkronModule.Session, title, section, mapSid);
        AkronArchive.WriteSinglePayloadArchive(
            path,
            new AkronArchiveManifest {
                Kind = AkronSetupPacks.SetupArchiveKind,
                KindVersion = 1,
                CreatedAt = pack.CreatedUtc,
                Target = new AkronArchiveTarget {
                    Game = "Celeste",
                    MapSid = mapSid?.Trim() ?? string.Empty
                }
            },
            AkronSetupPacks.SetupArchivePayload,
            JsonSerializer.Serialize(pack, SetupPackJsonOptions));
        return path;
    }

    internal static AkronSetupPack BuildScopedUploadPack(
        AkronModuleSettings settings,
        AkronModuleSession session,
        string title,
        AkronSetupSection section,
        string mapSid) {
        mapSid = mapSid?.Trim() ?? string.Empty;
        section = NormalizeUploadSection(section);
        return new AkronSetupPack {
            Name = string.IsNullOrWhiteSpace(title) ? GenerateTitle("Akron", section) : title,
            CreatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Section = section,
            State = BuildSectionOnlyUploadState(settings, section),
            ButtonBindings = new Dictionary<string, AkronButtonBindingPack>(),
            MenuActionBindings = new Dictionary<string, string>(),
            StartPositions = section == AkronSetupSection.StartPos
                ? CaptureUploadStartPositions(session, mapSid)
                : new Dictionary<int, AkronStartPosPackEntry>()
        };
    }

    private static AkronSetupState BuildSectionOnlyUploadState(AkronModuleSettings settings, AkronSetupSection section) {
        AkronSetupState source = (settings ?? new AkronModuleSettings()).CaptureSetupPackState();
        AkronSetupState target = new AkronSetupState();
        switch (NormalizeUploadSection(section)) {
            case AkronSetupSection.AutoKill:
                CopyAutoKillUploadState(target, source);
                break;
            case AkronSetupSection.AutoDeafen:
                CopyAutoDeafenUploadState(target, source);
                break;
            case AkronSetupSection.StartPos:
            default:
                CopyStartPosUploadState(target, source);
                break;
        }
        return target;
    }

    private static Dictionary<int, AkronStartPosPackEntry> CaptureUploadStartPositions(AkronModuleSession session, string mapSid) {
        Dictionary<int, AkronStartPosPackEntry> entries = new Dictionary<int, AkronStartPosPackEntry>();
        foreach (KeyValuePair<int, AkronStartPos> pair in session?.StartPositions ?? new Dictionary<int, AkronStartPos>()) {
            AkronStartPos startPos = pair.Value;
            if (startPos == null || !string.Equals(startPos.AreaSid ?? string.Empty, mapSid, StringComparison.Ordinal)) {
                continue;
            }

            entries[pair.Key] = new AkronStartPosPackEntry {
                X = startPos.Position.X,
                Y = startPos.Position.Y,
                Room = startPos.Room,
                AreaSid = startPos.AreaSid,
                UsesSpawnConfig = startPos.UsesSpawnConfig,
                Dashes = startPos.Dashes,
                StaminaPercent = startPos.StaminaPercent,
                Facing = startPos.Facing,
                Idle = startPos.Idle,
                Grab = startPos.Grab
            };
        }
        return entries;
    }

    private static void CopyStartPosUploadState(AkronSetupState target, AkronSetupState source) {
        target.SmartStartPos = source.SmartStartPos;
        target.RespawnAtStartPos = source.RespawnAtStartPos;
        target.StartPosShowLabel = source.StartPosShowLabel;
        target.StartPosLabelColor = source.StartPosLabelColor;
        target.StartPosLabelAnchor = source.StartPosLabelAnchor;
        target.StartPosLabelFormat = source.StartPosLabelFormat;
        target.StartPosLabelStyle = AkronModuleSettings.CloneLabelStyle(source.StartPosLabelStyle);
        target.StartPosMousePlacement = source.StartPosMousePlacement;
        target.StartPosPlacementPanelX = source.StartPosPlacementPanelX;
        target.StartPosPlacementPanelY = source.StartPosPlacementPanelY;
        target.StartPosPlacementPanelMinimized = source.StartPosPlacementPanelMinimized;
        target.StartPosPreviewOpacity = source.StartPosPreviewOpacity;
        target.StartPosConfiguredDashes = source.StartPosConfiguredDashes;
        target.StartPosConfiguredStaminaPercent = source.StartPosConfiguredStaminaPercent;
        target.StartPosConfiguredFacing = source.StartPosConfiguredFacing;
        target.StartPosConfiguredIdle = source.StartPosConfiguredIdle;
        target.StartPosConfiguredGrab = source.StartPosConfiguredGrab;
        target.StartPosSlotCount = source.StartPosSlotCount;
    }

    private static void CopyAutoKillUploadState(AkronSetupState target, AkronSetupState source) {
        target.AutoKill = source.AutoKill;
        target.AutoKillTimer = source.AutoKillTimer;
        target.AutoKillSeconds = source.AutoKillSeconds;
        target.AutoKillArea = source.AutoKillArea;
        target.AutoKillShowArea = source.AutoKillShowArea;
        target.AutoKillShowAreaOnDeath = source.AutoKillShowAreaOnDeath;
        target.AutoKillDefaultAreaConditions = CopyAutoKillArea(source.AutoKillDefaultAreaConditions ?? new AkronAutoKillAreaData());
        target.AutoKillAreas = CopyAutoKillAreas(source.AutoKillAreas);
        target.AutoKillAreaX = source.AutoKillAreaX;
        target.AutoKillAreaY = source.AutoKillAreaY;
        target.AutoKillAreaWidth = source.AutoKillAreaWidth;
        target.AutoKillAreaHeight = source.AutoKillAreaHeight;
    }

    private static void CopyAutoDeafenUploadState(AkronSetupState target, AkronSetupState source) {
        target.AutoDeafen = source.AutoDeafen;
        target.AutoDeafenHotkey = source.AutoDeafenHotkey;
        target.AutoDeafenArea = source.AutoDeafenArea;
        target.AutoDeafenShowArea = source.AutoDeafenShowArea;
        target.AutoDeafenAreas = CopyRectangles(source.AutoDeafenAreas);
        target.AutoDeafenAreaX = source.AutoDeafenAreaX;
        target.AutoDeafenAreaY = source.AutoDeafenAreaY;
        target.AutoDeafenAreaWidth = source.AutoDeafenAreaWidth;
        target.AutoDeafenAreaHeight = source.AutoDeafenAreaHeight;
    }

    private static List<AkronRectangleData> CopyRectangles(IEnumerable<AkronRectangleData> areas) {
        return (areas ?? Array.Empty<AkronRectangleData>())
            .Where(area => area != null)
            .Select(area => new AkronRectangleData {
                X = area.X,
                Y = area.Y,
                Width = area.Width,
                Height = area.Height
            })
            .ToList();
    }

    private static List<AkronAutoKillAreaData> CopyAutoKillAreas(IEnumerable<AkronAutoKillAreaData> areas) {
        return (areas ?? Array.Empty<AkronAutoKillAreaData>())
            .Where(area => area != null)
            .Select(CopyAutoKillArea)
            .ToList();
    }

    private static AkronAutoKillAreaData CopyAutoKillArea(AkronAutoKillAreaData area) {
        return new AkronAutoKillAreaData {
            X = area.X,
            Y = area.Y,
            Width = area.Width,
            Height = area.Height,
            SpeedCondition = area.SpeedCondition,
            MinSpeed = area.MinSpeed,
            MaxSpeed = area.MaxSpeed,
            HorizontalSpeedCondition = area.HorizontalSpeedCondition,
            MinHorizontalSpeed = area.MinHorizontalSpeed,
            MaxHorizontalSpeed = area.MaxHorizontalSpeed,
            VerticalSpeedCondition = area.VerticalSpeedCondition,
            MinVerticalSpeed = area.MinVerticalSpeed,
            MaxVerticalSpeed = area.MaxVerticalSpeed,
            DashCountCondition = area.DashCountCondition,
            DashCount = area.DashCount,
            GroundCondition = area.GroundCondition,
            HorizontalDirection = area.HorizontalDirection,
            VerticalDirection = area.VerticalDirection,
            PlayerStateCondition = area.PlayerStateCondition,
            PlayerState = area.PlayerState,
            InvertConditions = area.InvertConditions
        };
    }

    public static AkronCommunityPackUploadPrepareRequest BuildPrepareRequest(
        AkronCommunityPackUploadDraft draft,
        string packPath,
        string capturePath,
        string installId,
        int termsVersion) {
        if (draft == null) {
            throw new ArgumentNullException(nameof(draft));
        }

        if (!IsSupportedUploadSection(draft.Section)) {
            throw new InvalidOperationException("Only StartPos, Auto Kill, and Auto Deafen packs can be uploaded.");
        }

        FileInfo packFile = RequireExistingFile(packPath, nameof(packPath));
        FileInfo captureFile = RequireExistingFile(capturePath, nameof(capturePath));
        string attributionMode = draft.AttributionMode == DiscordAttribution && !string.IsNullOrWhiteSpace(draft.DiscordUserId)
            ? DiscordAttribution
            : AnonymousAttribution;

        return new AkronCommunityPackUploadPrepareRequest {
            InstallId = string.IsNullOrWhiteSpace(installId) ? EnsureInstallId(AkronModule.Settings) : installId.Trim(),
            TermsVersion = termsVersion,
            Capture = new AkronCommunityPackUploadCaptureInput {
                SizeBytes = captureFile.Length,
                ContentType = GuessCaptureContentType(captureFile.FullName)
            },
            Submissions = new System.Collections.Generic.List<AkronCommunityPackUploadSubmissionInput> {
                new AkronCommunityPackUploadSubmissionInput {
                    Section = draft.Section,
                    MapSid = draft.MapSid,
                    Title = draft.Title,
                    Description = draft.Description,
                    PackSizeBytes = packFile.Length,
                    Attribution = new AkronCommunityPackUploadAttributionInput {
                        Mode = attributionMode,
                        DiscordUserId = attributionMode == DiscordAttribution ? draft.DiscordUserId.Trim() : string.Empty
                    }
                }
            }
        };
    }

    public static async Task<AkronCommunityPackUploadCompleteResponse> UploadAsync(
        HttpClient http,
        string endpoint,
        AkronCommunityPackUploadPrepareRequest request,
        string packPath,
        string capturePath,
        CancellationToken cancellationToken = default) {
        if (http == null) {
            throw new ArgumentNullException(nameof(http));
        }
        if (request == null) {
            throw new ArgumentNullException(nameof(request));
        }

        endpoint = ResolveEndpoint(endpoint);
        AkronCommunityPackUploadPreparedResponse prepared = await PostJsonAsync<AkronCommunityPackUploadPreparedResponse>(
            http,
            endpoint + "/prepare",
            request,
            cancellationToken);

        if (prepared.Capture == null ||
            string.IsNullOrWhiteSpace(prepared.Capture.UploadUrl) ||
            prepared.Submissions == null ||
            prepared.Submissions.Count != request.Submissions.Count ||
            prepared.Submissions.Count != 1 ||
            prepared.Submissions[0]?.Pack == null ||
            string.IsNullOrWhiteSpace(prepared.Submissions[0].Pack.UploadUrl)) {
            throw new InvalidDataException("Upload prepare response did not match the requested submissions.");
        }

        await PutFileAsync(http, prepared.Capture.UploadUrl, capturePath, request.Capture.ContentType, cancellationToken);
        await PutFileAsync(http, prepared.Submissions[0].Pack.UploadUrl, packPath, "application/octet-stream", cancellationToken);

        return await PostJsonAsync<AkronCommunityPackUploadCompleteResponse>(
            http,
            endpoint + "/complete",
            new {
                installId = request.InstallId,
                batchId = prepared.BatchId
            },
            cancellationToken);
    }

    public static AkronCommunityPackUploadDraft BuildDraft(
        string mapSid,
        string mapDisplayName,
        AkronSetupSection section,
        string savedDiscordUserId,
        bool useDiscordAttribution,
        string titleOverride = "",
        string descriptionOverride = "") {
        section = NormalizeUploadSection(section);
        mapSid = (mapSid ?? string.Empty).Trim();
        mapDisplayName = string.IsNullOrWhiteSpace(mapDisplayName) ? mapSid : mapDisplayName.Trim();
        string discordUserId = (savedDiscordUserId ?? string.Empty).Trim();
        bool canUseDiscordAttribution = useDiscordAttribution && !string.IsNullOrWhiteSpace(discordUserId);
        string generatedTitle = GenerateTitle(mapDisplayName, section);
        string generatedDescription = GenerateDescription(mapDisplayName, section);

        return new AkronCommunityPackUploadDraft {
            MapSid = mapSid,
            MapDisplayName = mapDisplayName,
            Section = section,
            Title = string.IsNullOrWhiteSpace(titleOverride) ? generatedTitle : titleOverride.Trim(),
            Description = string.IsNullOrWhiteSpace(descriptionOverride) ? generatedDescription : descriptionOverride.Trim(),
            AttributionMode = canUseDiscordAttribution ? DiscordAttribution : AnonymousAttribution,
            DiscordUserId = canUseDiscordAttribution ? discordUserId : string.Empty
        };
    }

    public static AkronCommunityPackUploadDraft BuildDraft(Level level, AkronSetupSection section, bool useDiscordAttribution) {
        string mapSid = level?.Session?.Area.GetSID() ?? string.Empty;
        string mapDisplayName = ResolveMapDisplayName(level, mapSid);
        return BuildDraft(
            mapSid,
            mapDisplayName,
            section,
            AkronModule.Settings.CommunityPackUploadDiscordUserId,
            useDiscordAttribution,
            AkronModule.Settings.CommunityPackUploadTitleOverride,
            AkronModule.Settings.CommunityPackUploadDescriptionOverride);
    }

    public static string GenerateTitle(string mapDisplayName, AkronSetupSection section) {
        string mapName = CleanMapDisplayName(mapDisplayName);
        switch (NormalizeUploadSection(section)) {
            case AkronSetupSection.AutoKill:
                return mapName + " Auto Kill Areas";
            case AkronSetupSection.AutoDeafen:
                return mapName + " Auto Deafen Areas";
            case AkronSetupSection.StartPos:
            default:
                return mapName + " StartPos Pack";
        }
    }

    public static string GenerateDescription(string mapDisplayName, AkronSetupSection section) {
        string mapName = CleanMapDisplayName(mapDisplayName);
        switch (NormalizeUploadSection(section)) {
            case AkronSetupSection.AutoKill:
                return "Auto Kill areas for routing and reset practice in " + mapName + ".";
            case AkronSetupSection.AutoDeafen:
                return "Auto Deafen areas for focus sections in " + mapName + ".";
            case AkronSetupSection.StartPos:
            default:
                return "Start positions for practicing " + mapName + ".";
        }
    }

    public static string DescribeOverlayAction() {
        if (Engine.Scene is not Level level) {
            return "No map";
        }

        string mapSid = level.Session?.Area.GetSID() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(mapSid)) {
            return "No map";
        }

        AkronSetupSection section = NormalizeUploadSection(AkronModule.Settings.CommunityPackUploadSection);
        if (AkronModule.Settings.CommunityPackUploadUseDiscordAttribution &&
            string.IsNullOrWhiteSpace(AkronModule.Settings.CommunityPackUploadDiscordUserId)) {
            return "Discord ID";
        }
        return AkronSetupPacks.FormatSection(section);
    }

    public static void OpenUploadPrompt(Level level) {
        if (level == null) {
            Engine.Scene?.Add(new AkronToast("Upload Pack needs an active map."));
            return;
        }

        if (IsUploadInProgress || AkronScreenshotScanner.IsScanning) {
            Engine.Scene?.Add(new AkronToast("Wait for the current Upload Pack submission to finish."));
            return;
        }

        EnsureInstallId(AkronModule.Settings);
        AkronModule.Settings.CommunityPackUploadSection = NormalizeUploadSection(AkronModule.Settings.CommunityPackUploadSection);
        if (AkronModule.Settings.CommunityPackUploadUseDiscordAttribution &&
            string.IsNullOrWhiteSpace(AkronModule.Settings.CommunityPackUploadDiscordUserId)) {
            Engine.Scene?.Add(new AkronToast("Set a Discord user ID or choose Anonymous."));
            return;
        }

        AkronCommunityPackUploadDraft draft = BuildDraft(
            level,
            AkronModule.Settings.CommunityPackUploadSection,
            AkronModule.Settings.CommunityPackUploadUseDiscordAttribution);
        string packPath;
        try {
            packPath = WriteTempArchive(draft.Section, draft.Title, draft.MapSid);
        } catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidOperationException) {
            AkronLog.Warn(nameof(AkronCommunityPackUploads), "Could not create temp upload archive: " + exception.Message);
            Engine.Scene?.Add(new AkronToast("Upload Pack could not create the .akr file."));
            return;
        }

        DateTime captureStartedUtc = DateTime.UtcNow;
        if (!AkronScreenshotScanner.ScanChapter(level)) {
            try {
                if (File.Exists(packPath)) {
                    File.Delete(packPath);
                }
            } catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) {
                AkronLog.Warn(nameof(AkronCommunityPackUploads), "Could not delete temp upload archive: " + exception.Message);
            }
            Engine.Scene?.Add(new AkronToast("Upload Pack could not start the map capture."));
            return;
        }
        if (!TryReserveUploadSlot()) {
            try {
                if (File.Exists(packPath)) {
                    File.Delete(packPath);
                }
            } catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) {
                AkronLog.Warn(nameof(AkronCommunityPackUploads), "Could not delete temp upload archive: " + exception.Message);
            }
            Engine.Scene?.Add(new AkronToast("Wait for the current Upload Pack submission to finish."));
            return;
        }

        Engine.Scene?.Add(new AkronCommunityPackUploadHost(draft, packPath, captureStartedUtc));
        Engine.Scene?.Add(new AkronToast("Upload Pack is capturing the full map."));
    }

    private static string ResolveMapDisplayName(Level level, string mapSid) {
        AreaData areaData = level == null ? null : AreaData.Get(level.Session?.Area ?? default);
        if (areaData != null && !string.IsNullOrWhiteSpace(areaData.Name)) {
            return areaData.Name;
        }

        return string.IsNullOrWhiteSpace(mapSid) ? "this map" : mapSid;
    }

    private static string CleanMapDisplayName(string mapDisplayName) {
        return string.IsNullOrWhiteSpace(mapDisplayName) ? "This Map" : mapDisplayName.Trim();
    }

    private static FileInfo RequireExistingFile(string path, string parameterName) {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
            throw new FileNotFoundException("Upload file not found.", parameterName);
        }

        return new FileInfo(path);
    }

    private static async Task<T> PostJsonAsync<T>(HttpClient http, string url, object body, CancellationToken cancellationToken) {
        string json = JsonSerializer.Serialize(body, UploadJsonOptions);
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url) {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        string responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) {
            throw new HttpRequestException("Upload request failed with HTTP " + (int) response.StatusCode + ": " + responseText);
        }

        T parsed = JsonSerializer.Deserialize<T>(responseText, UploadJsonOptions);
        if (parsed == null) {
            throw new InvalidDataException("Upload response JSON was empty or invalid.");
        }
        return parsed;
    }

    private static async Task PutFileAsync(HttpClient http, string uploadUrl, string path, string contentType, CancellationToken cancellationToken) {
        FileInfo file = RequireExistingFile(path, nameof(path));
        using FileStream stream = file.OpenRead();
        using StreamContent content = new StreamContent(stream);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        content.Headers.ContentLength = file.Length;
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Put, uploadUrl) {
            Content = content
        };
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        string responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) {
            throw new HttpRequestException("Upload object failed with HTTP " + (int) response.StatusCode + ": " + responseText);
        }
    }

    private static string GuessCaptureContentType(string path) {
        string extension = Path.GetExtension(path)?.ToLowerInvariant() ?? string.Empty;
        return extension switch {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "image/png"
        };
    }

    internal static bool IsCompletedMapCaptureForUpload(string capturePath, DateTime captureStartedUtc) {
        if (string.IsNullOrWhiteSpace(capturePath) || !File.Exists(capturePath)) {
            return false;
        }

        string fileName = Path.GetFileName(capturePath);
        if (!string.Equals(fileName, "map.png", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fileName, "map.jpg", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fileName, "map.jpeg", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        try {
            return File.GetLastWriteTimeUtc(capturePath) >= captureStartedUtc.AddSeconds(-2);
        } catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) {
            return false;
        }
    }

    private static string SanitizeFileName(string name) {
        foreach (char invalid in Path.GetInvalidFileNameChars()) {
            name = name.Replace(invalid, '-');
        }

        name = name.Trim();
        return string.IsNullOrWhiteSpace(name) ? "upload-pack" : name;
    }

    private sealed class AkronCommunityPackUploadHost : Entity
    {
        private readonly AkronCommunityPackUploadDraft draft;
        private readonly string packPath;
        private readonly DateTime captureStartedUtc;
        private readonly CancellationTokenSource uploadCancellation = new CancellationTokenSource();
        private bool ownsCaptureScan = true;
        private bool cleanedUp;

        public AkronCommunityPackUploadHost(AkronCommunityPackUploadDraft draft, string packPath, DateTime captureStartedUtc)
        {
            this.draft = draft;
            this.packPath = packPath;
            this.captureStartedUtc = captureStartedUtc;
            Tag = Tags.HUD | Tags.Global | Tags.Persistent | Tags.PauseUpdate;
            Add(new Coroutine(Run()));
        }

        public override void Removed(Scene scene)
        {
            uploadCancellation.Cancel();
            if (ownsCaptureScan && AkronScreenshotScanner.IsScanning) {
                AkronScreenshotScanner.Stop();
            }
            CleanupPack();
            base.Removed(scene);
        }

        private IEnumerator Run()
        {
            while (AkronScreenshotScanner.IsScanning) {
                yield return null;
            }
            ownsCaptureScan = false;

            string capturePath = AkronScreenshotScanner.LastExportPath;
            if (!IsCompletedMapCaptureForUpload(capturePath, captureStartedUtc)) {
                CleanupPack();
                Engine.Scene?.Add(new AkronToast("Upload Pack could not find the map capture."));
                RemoveSelf();
                yield break;
            }

            Task<AkronCommunityPackUploadCompleteResponse> uploadTask = null;
            try {
                AkronCommunityPackUploadPrepareRequest request = BuildPrepareRequest(
                    draft,
                    packPath,
                    capturePath,
                    EnsureInstallId(AkronModule.Settings),
                    CurrentTermsVersion);
                uploadTask = UploadAsync(
                    UploadHttp,
                    AkronModule.Settings.CommunityPackUploadEndpoint,
                    request,
                    packPath,
                    capturePath,
                    uploadCancellation.Token);
            } catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is InvalidOperationException || exception is UnauthorizedAccessException) {
                CleanupPack();
                AkronLog.Warn(nameof(AkronCommunityPackUploads), "Could not prepare upload: " + exception.Message);
                Engine.Scene?.Add(new AkronToast("Upload Pack could not prepare the upload."));
                RemoveSelf();
                yield break;
            }

            while (!uploadTask.IsCompleted) {
                yield return null;
            }

            CleanupPack();
            if (uploadTask.IsFaulted) {
                string message = uploadTask.Exception?.GetBaseException().Message ?? "Unknown upload failure.";
                AkronLog.Warn(nameof(AkronCommunityPackUploads), "Upload Pack failed: " + message);
                Engine.Scene?.Add(new AkronToast("Upload Pack failed."));
                RemoveSelf();
                yield break;
            }

            if (uploadTask.IsCanceled) {
                AkronLog.Warn(nameof(AkronCommunityPackUploads), "Upload Pack was canceled.");
                Engine.Scene?.Add(new AkronToast("Upload Pack canceled."));
                RemoveSelf();
                yield break;
            }

            AkronCommunityPackUploadCompleteResponse response = uploadTask.GetAwaiter().GetResult();
            Engine.Scene?.Add(new AkronToast("Upload Pack submitted: " + response.Status + "."));
            RemoveSelf();
        }

        private void CleanupPack()
        {
            if (cleanedUp) {
                return;
            }

            cleanedUp = true;
            try {
                if (File.Exists(packPath)) {
                    File.Delete(packPath);
                }
            } catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) {
                AkronLog.Warn(nameof(AkronCommunityPackUploads), "Could not delete temp upload archive: " + exception.Message);
            } finally {
                ReleaseUploadSlot();
            }
        }
    }
}
