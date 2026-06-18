using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Celeste;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Monocle;
using NumericsVector2 = System.Numerics.Vector2;

namespace Celeste.Mod.Akron;

public enum AkronInspectorPinFilter
{
    Entities,
    Triggers,
    Both
}

internal enum AkronInspectorMapBindingStatus
{
    EverestSourceBound,
    RuntimeOnlyNoExactMapNode,
    RuntimeOnlyAmbiguousMapNode,
    RuntimeOnlyGeneratedEntity,
    MapDataUnavailable
}

internal sealed class AkronInspectorPropertyRow
{
    public AkronInspectorPropertyRow(string key, string value)
    {
        Key = key ?? string.Empty;
        Value = value ?? string.Empty;
    }

    public string Key { get; }
    public string Value { get; }
}

internal sealed class AkronInspectorReportData
{
    public string Summary { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string FullTypeName { get; set; } = string.Empty;
    public string Room { get; set; } = string.Empty;
    public string MapSid { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string ColliderKind { get; set; } = string.Empty;
    public string MapPlacementLabel { get; set; } = string.Empty;
    public int InspectorId { get; set; }
    public int RoomSessionId { get; set; }
    public int StackIndex { get; set; }
    public int StackCount { get; set; }
    public int EntityCount { get; set; }
    public int TriggerCount { get; set; }
    public int SourceNodeIndex { get; set; } = int.MaxValue;
    public int SourceOrdinal { get; set; } = int.MaxValue;
    public int SourceObjectCount { get; set; }
    public float ColliderArea { get; set; }
    public float Depth { get; set; }
    public Vector2 Position { get; set; }
    public Vector2 Center { get; set; }
    public Rectangle ColliderBounds { get; set; }
    public Vector2 ClickScreenPoint { get; set; }
    public Vector2 ClickGamePoint { get; set; }
    public Vector2 ClickWorldPoint { get; set; }
    public Point HitProbePixel { get; set; }
    public bool Active { get; set; }
    public bool Visible { get; set; }
    public bool Collidable { get; set; }
    public AkronInspectorPinFilter Filter { get; set; }
    public AkronInspectorMapBindingStatus MapBindingStatus { get; set; }
    public List<AkronInspectorPropertyRow> RuntimeRows { get; } = new List<AkronInspectorPropertyRow>();
    public List<AkronInspectorPropertyRow> PlacementRows { get; } = new List<AkronInspectorPropertyRow>();
    public List<AkronInspectorPropertyRow> AuthoredRows { get; } = new List<AkronInspectorPropertyRow>();
    public List<string> StackEntries { get; } = new List<string>();
    public string StackSignature { get; set; } = string.Empty;
}

public static partial class AkronEntityInspector
{
    private const float InspectorPinCycleTolerancePixels = 8f;
    private const int InspectorPinProbeRadiusPixels = 3;
    private const int InspectorPinFallbackSourceSize = 8;
    private static readonly ReferenceEqualityComparer ReferenceComparer = new ReferenceEqualityComparer();
    private static readonly Dictionary<Entity, int> inspectorIds = new Dictionary<Entity, int>(ReferenceComparer);
    private static readonly Dictionary<Entity, InspectorSourceRecord> sourceRecords = new Dictionary<Entity, InspectorSourceRecord>(ReferenceComparer);
    private static readonly Dictionary<EntityData, InspectorSourceOrdinalState> sourceOrdinalStates = new Dictionary<EntityData, InspectorSourceOrdinalState>(ReferenceComparer);
    private static readonly List<InspectorHit> currentStack = new List<InspectorHit>();
    private static readonly Rectangle emptyHudRect = new Rectangle(0, 0, 0, 0);
    private static Rectangle inspectorPinCardRect = emptyHudRect;
    private static bool inspectorPinLastLeftDown;
    private static bool inspectorPinPropertiesOpen;
    private static bool inspectorPinPositionInitialized;
    private static int inspectorPinNextId = 1;
    private static int inspectorPinRoomSessionId;
    private static string inspectorPinRoomKey = string.Empty;
    private static string inspectorPinStackSignature = string.Empty;
    private static Vector2 inspectorPinAnchorScreen;
    private static Entity inspectorPinSelectedEntity;
    private static int inspectorPinSelectedIndex;
    private static int inspectorPinCopiedUntilFrame;
    private static AkronInspectorPinFilter inspectorPinLastFilter = AkronInspectorPinFilter.Both;
    private static AkronInspectorPinPlacement inspectorPinLastPlacement = AkronInspectorPinPlacement.NearClick;

    public static void LoadInspectorPin()
    {
        On.Celeste.Level.LoadLevel += LevelOnLoadLevelForInspectorPin;
        On.Monocle.EntityList.Add_Entity += EntityListOnAddEntityForInspectorPin;
    }

    public static void UnloadInspectorPin()
    {
        On.Celeste.Level.LoadLevel -= LevelOnLoadLevelForInspectorPin;
        On.Monocle.EntityList.Add_Entity -= EntityListOnAddEntityForInspectorPin;
        ClearInspectorPinRoomState();
    }

    public static void UpdateInspectorPin(Level level)
    {
        AkronInspectorPinFilter filter = NormalizeInspectorPinFilter(AkronModule.Settings.InspectorPinFilter);
        if (!AkronModule.Settings.EntityInspector || level == null)
        {
            inspectorPinLastLeftDown = false;
            AkronModule.ClearEntityInspectorPickMode();
            ClearInspectorPinSelection();
            return;
        }

        if (filter != inspectorPinLastFilter)
        {
            inspectorPinLastFilter = filter;
            ClearInspectorPinSelection();
        }

        EnsureInspectorRoomSession(level);
        ClearInspectorPinIfSelectedObjectRemoved(level);

        MouseState mouse = Mouse.GetState();
        bool leftDown = mouse.LeftButton == ButtonState.Pressed;
        bool pressed = leftDown && !inspectorPinLastLeftDown;
        inspectorPinLastLeftDown = leftDown;
        if (!pressed)
        {
            return;
        }

        Vector2 screenPoint = new Vector2(mouse.X, mouse.Y);
        if (HandleInspectorPinCardClick(screenPoint))
        {
            return;
        }

        if (ShouldIgnoreInspectorPinGameplayClick(level) ||
            !AkronPolicy.CanUse(AkronFeatureKind.EntityInspector).Allowed)
        {
            return;
        }

        if (!IsInsideGameplayViewport(screenPoint))
        {
            return;
        }

        Vector2 gamePoint = AkronScreenProjection.MouseScreenToGame(screenPoint);
        Vector2 worldPoint = AkronScreenProjection.MouseScreenToWorld(level, screenPoint);
        Point probePixel = new Point((int)Math.Floor(worldPoint.X), (int)Math.Floor(worldPoint.Y));
        List<InspectorHit> hits = BuildInspectorHitStack(level, filter, screenPoint, gamePoint, worldPoint, probePixel);
        if (hits.Count == 0)
        {
            ClearInspectorPinSelection();
            return;
        }

        string signature = BuildInspectorStackSignature(filter, hits);
        bool sameStack = string.Equals(signature, inspectorPinStackSignature, StringComparison.Ordinal) &&
                         Vector2.Distance(screenPoint, inspectorPinAnchorScreen) <= InspectorPinCycleTolerancePixels &&
                         currentStack.Count == hits.Count;

        ApplyInspectorPinHits(screenPoint, signature, hits, sameStack);
    }

    internal static string PinInspectorAtWorldPointForQa(Level level, Vector2 worldPoint)
    {
        if (level == null)
        {
            ClearInspectorPinSelection();
            return "missing-level";
        }

        EnsureInspectorRoomSession(level);
        AkronInspectorPinFilter filter = NormalizeInspectorPinFilter(AkronModule.Settings.InspectorPinFilter);
        Vector2 screenPoint = AkronScreenProjection.WorldToHud(level, worldPoint);
        Vector2 gamePoint = AkronScreenProjection.MouseScreenToGame(screenPoint);
        Point probePixel = new Point((int)Math.Floor(worldPoint.X), (int)Math.Floor(worldPoint.Y));
        List<InspectorHit> hits = BuildInspectorHitStack(level, filter, screenPoint, gamePoint, worldPoint, probePixel);
        if (hits.Count == 0)
        {
            ClearInspectorPinSelection();
            return "miss";
        }

        string signature = BuildInspectorStackSignature(filter, hits);
        bool sameStack = string.Equals(signature, inspectorPinStackSignature, StringComparison.Ordinal) &&
                         Vector2.Distance(screenPoint, inspectorPinAnchorScreen) <= InspectorPinCycleTolerancePixels &&
                         currentStack.Count == hits.Count;
        ApplyInspectorPinHits(screenPoint, signature, hits, sameStack);
        InspectorHit selected = currentStack[inspectorPinSelectedIndex];
        return "pinned: " + (selected.Entity.GetType().FullName ?? selected.Entity.GetType().Name) +
               ";stack=" + (inspectorPinSelectedIndex + 1).ToString(CultureInfo.InvariantCulture) + "/" + currentStack.Count.ToString(CultureInfo.InvariantCulture);
    }

    internal static string DiagnoseInspectorPinScreenPointForQa(Level level, Vector2 screenPoint)
    {
        if (level == null)
        {
            return "missing-level";
        }

        AkronInspectorPinFilter filter = NormalizeInspectorPinFilter(AkronModule.Settings.InspectorPinFilter);
        Vector2 gamePoint = AkronScreenProjection.MouseScreenToGame(screenPoint);
        Vector2 worldPoint = AkronScreenProjection.MouseScreenToWorld(level, screenPoint);
        Point probePixel = new Point((int)Math.Floor(worldPoint.X), (int)Math.Floor(worldPoint.Y));
        List<InspectorHit> hits = BuildInspectorHitStack(level, filter, screenPoint, gamePoint, worldPoint, probePixel);
        string first = hits.Count == 0
            ? "none"
            : hits[0].Entity.GetType().FullName ?? hits[0].Entity.GetType().Name;

        return "screen=" + FormatVector(screenPoint) +
               ";game=" + FormatVector(gamePoint) +
               ";world=" + FormatVector(worldPoint) +
               ";probe=" + probePixel.X.ToString(CultureInfo.InvariantCulture) + "," + probePixel.Y.ToString(CultureInfo.InvariantCulture) +
               ";inside=" + IsInsideGameplayViewport(screenPoint).ToString().ToLowerInvariant() +
               ";ignored=" + ShouldIgnoreInspectorPinGameplayClick(level).ToString().ToLowerInvariant() +
               ";cursor=" + AkronModule.ShouldShowEntityInspectorCursor().ToString().ToLowerInvariant() +
               ";hits=" + hits.Count.ToString(CultureInfo.InvariantCulture) +
               ";first=" + first;
    }

    public static void RenderInspectorPinOutlinesToGameplayBuffer(Level level)
    {
        if (!AkronModule.Settings.EntityInspector || level == null || currentStack.Count == 0)
        {
            return;
        }

        Rectangle cameraBounds = CameraWorldBounds(level);
        for (int index = 0; index < currentStack.Count; index++)
        {
            InspectorHit hit = currentStack[index];
            if (hit.Entity?.Collider == null || !IntersectsCamera(hit.Entity.Collider, cameraBounds))
            {
                continue;
            }

            bool selected = index == inspectorPinSelectedIndex;
            Color color = selected
                ? new Color(255, 191, 64) * 0.95f
                : new Color(255, 191, 64) * 0.35f;

            if (selected && hit.IsTrigger)
            {
                DrawDashedWorldRect(level, hit.Bounds, color);
                continue;
            }

            DrawCollider(level, hit.Entity.Collider, color, cameraBounds);
        }
    }

    public static bool ShouldRenderInspectorPinImGui(Level level)
    {
        return AkronModule.Settings.EntityInspector &&
               level != null &&
               inspectorPinSelectedEntity != null &&
               currentStack.Count > 0;
    }

    public static bool RenderInspectorPinImGui(Level level)
    {
        if (!ShouldRenderInspectorPinImGui(level))
        {
            inspectorPinCardRect = emptyHudRect;
            return false;
        }

        return AkronImGuiRenderer.Render(() => DrawInspectorPinImGui(level));
    }

    public static bool HasInspectorPinSelection()
    {
        return inspectorPinSelectedEntity != null && currentStack.Count > 0;
    }

    internal static AkronInspectorPinFilter NormalizeInspectorPinFilter(AkronInspectorPinFilter filter)
    {
        return Enum.IsDefined(typeof(AkronInspectorPinFilter), filter)
            ? filter
            : AkronInspectorPinFilter.Both;
    }

    internal static string FormatInspectorValue(object value)
    {
        if (value == null)
        {
            return "null";
        }

        switch (value)
        {
            case string text:
                return QuoteJsonString(text);
            case bool boolean:
                return boolean ? "true" : "false";
            case float single:
                return FormatFloat(single);
            case double number:
                return FormatDouble(number);
            case decimal decimalValue:
                return decimalValue.ToString(CultureInfo.InvariantCulture);
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            case Vector2 vector:
                return "{ \"x\": " + FormatFloat(vector.X) + ", \"y\": " + FormatFloat(vector.Y) + " }";
            case IDictionary dictionary:
                return FormatDictionary(dictionary);
            case IEnumerable enumerable when value is not string:
                return FormatEnumerable(enumerable);
            default:
                return "{ \"unsupported\": " + QuoteJsonString(value.GetType().FullName ?? value.GetType().Name) + " }";
        }
    }

    internal static string BuildCopyReport(AkronInspectorReportData data)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("Akron Inspector Pin Report");
        builder.AppendLine("akronVersion: " + AkronModule.Instance?.Metadata?.VersionString);
        builder.AppendLine("filter: " + data.Filter);
        builder.AppendLine("room: " + data.Room);
        builder.AppendLine("mapSid: " + data.MapSid);
        builder.AppendLine("roomSessionId: " + data.RoomSessionId.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("clickScreen: " + FormatVector(data.ClickScreenPoint));
        builder.AppendLine("clickGame: " + FormatVector(data.ClickGamePoint));
        builder.AppendLine("clickWorld: " + FormatVector(data.ClickWorldPoint));
        builder.AppendLine("hitProbePixel: " + data.HitProbePixel.X.ToString(CultureInfo.InvariantCulture) + "," + data.HitProbePixel.Y.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("cycle: " + (data.StackIndex + 1).ToString(CultureInfo.InvariantCulture) + "/" + data.StackCount.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("stackSignature: " + data.StackSignature);
        builder.AppendLine("stack:");
        foreach (string entry in data.StackEntries)
        {
            builder.AppendLine("- " + entry);
        }

        builder.AppendLine("selected:");
        AppendReportRow(builder, "category", data.Category);
        AppendReportRow(builder, "displayName", data.DisplayName);
        AppendReportRow(builder, "type", data.FullTypeName);
        AppendReportRow(builder, "inspectorId", data.InspectorId.ToString(CultureInfo.InvariantCulture));
        AppendReportRow(builder, "sourceId", data.SourceId);
        AppendReportRow(builder, "position", FormatVector(data.Position));
        AppendReportRow(builder, "center", FormatVector(data.Center));
        AppendReportRow(builder, "colliderKind", data.ColliderKind);
        AppendReportRow(builder, "colliderBounds", FormatRectangle(data.ColliderBounds));
        AppendReportRow(builder, "colliderArea", FormatFloat(data.ColliderArea));
        AppendReportRow(builder, "depth", data.Depth.ToString(CultureInfo.InvariantCulture));
        AppendReportRow(builder, "active", data.Active.ToString().ToLowerInvariant());
        AppendReportRow(builder, "visible", data.Visible.ToString().ToLowerInvariant());
        AppendReportRow(builder, "collidable", data.Collidable.ToString().ToLowerInvariant());
        AppendReportRow(builder, "mapPlacement", data.MapPlacementLabel);
        if (data.SourceObjectCount > 0)
        {
            AppendReportRow(builder, "sourceObjectCount", data.SourceObjectCount.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(builder, "sourceOrdinal", data.SourceOrdinal.ToString(CultureInfo.InvariantCulture));
        }

        AppendRows(builder, "runtime", data.RuntimeRows);
        AppendRows(builder, "placement", data.PlacementRows);
        AppendRows(builder, "authoredProperties", data.AuthoredRows);
        return builder.ToString();
    }

    internal static string BuildVisibleCopyReport(AkronInspectorReportData data, bool includeProperties)
    {
        if (includeProperties)
        {
            return BuildCopyReport(data);
        }

        StringBuilder builder = new StringBuilder();
        builder.AppendLine("Akron Inspector Pin Report");
        builder.AppendLine("akronVersion: " + AkronModule.Instance?.Metadata?.VersionString);
        builder.AppendLine("target: " + data.Filter);
        builder.AppendLine("selected: " + data.Category + " " + data.DisplayName);
        builder.AppendLine("room: " + data.Room);
        builder.AppendLine("position: " + FormatVector(data.Position));
        builder.AppendLine("cycle: " + (data.StackIndex + 1).ToString(CultureInfo.InvariantCulture) + "/" + data.StackCount.ToString(CultureInfo.InvariantCulture));
        if (data.Filter == AkronInspectorPinFilter.Both && data.StackCount > 1)
        {
            builder.AppendLine("stack: " + data.EntityCount.ToString(CultureInfo.InvariantCulture) + " entities, " + data.TriggerCount.ToString(CultureInfo.InvariantCulture) + " triggers");
        }

        return builder.ToString();
    }

    private static void LevelOnLoadLevelForInspectorPin(On.Celeste.Level.orig_LoadLevel orig, Level self, Player.IntroTypes playerIntro, bool isFromLoader)
    {
        BeginInspectorPinRoomSession(self);
        orig(self, playerIntro, isFromLoader);
    }

    private static void EntityListOnAddEntityForInspectorPin(On.Monocle.EntityList.orig_Add_Entity orig, EntityList self, Entity entity)
    {
        orig(self, entity);
        if (entity == null || Engine.Scene is not Level level || !ReferenceEquals(level.Entities, self))
        {
            return;
        }

        EnsureInspectorRoomSession(level);
        EntityData sourceData = entity.SourceData;
        InspectorSourceOrdinalState state = null;
        int ordinal = 0;
        if (sourceData != null)
        {
            if (!sourceOrdinalStates.TryGetValue(sourceData, out state))
            {
                state = new InspectorSourceOrdinalState();
                sourceOrdinalStates[sourceData] = state;
            }

            state.Count++;
            ordinal = state.Count;
        }

        sourceRecords[entity] = new InspectorSourceRecord(sourceData, entity.SourceId, ordinal, state, inspectorPinRoomSessionId);
    }

    private static void BeginInspectorPinRoomSession(Level level)
    {
        inspectorPinRoomSessionId++;
        inspectorPinRoomKey = BuildRoomKey(level);
        inspectorIds.Clear();
        sourceRecords.Clear();
        sourceOrdinalStates.Clear();
        inspectorPinNextId = 1;
        ClearInspectorPinSelection();
    }

    private static void EnsureInspectorRoomSession(Level level)
    {
        string key = BuildRoomKey(level);
        if (inspectorPinRoomSessionId == 0 || !string.Equals(key, inspectorPinRoomKey, StringComparison.Ordinal))
        {
            BeginInspectorPinRoomSession(level);
        }
    }

    private static string BuildRoomKey(Level level)
    {
        if (level?.Session == null)
        {
            return string.Empty;
        }

        return (level.Session.Area.GetSID() ?? string.Empty) + "|" + (level.Session.Level ?? string.Empty) + "|" + RuntimeHelpers.GetHashCode(level).ToString(CultureInfo.InvariantCulture);
    }

    private static void ClearInspectorPinRoomState()
    {
        inspectorPinRoomSessionId = 0;
        inspectorPinRoomKey = string.Empty;
        inspectorIds.Clear();
        sourceRecords.Clear();
        sourceOrdinalStates.Clear();
        inspectorPinNextId = 1;
        ClearInspectorPinSelection();
    }

    private static void ClearInspectorPinSelection()
    {
        currentStack.Clear();
        inspectorPinStackSignature = string.Empty;
        inspectorPinSelectedEntity = null;
        inspectorPinSelectedIndex = 0;
        inspectorPinPositionInitialized = false;
        inspectorPinCardRect = emptyHudRect;
    }

    private static void ClearInspectorPinIfSelectedObjectRemoved(Level level)
    {
        if (inspectorPinSelectedEntity == null)
        {
            return;
        }

        bool stillPresent = EnumerateInspectorEntities(level)
            .Any(entity => ReferenceEquals(entity, inspectorPinSelectedEntity));
        if (!stillPresent)
        {
            ClearInspectorPinSelection();
        }
    }

    private static bool HandleInspectorPinCardClick(Vector2 screenPoint)
    {
        if (inspectorPinCardRect.Width <= 0)
        {
            return false;
        }

        Point screenPixel = new Point((int)screenPoint.X, (int)screenPoint.Y);
        return inspectorPinCardRect.Contains(screenPixel);
    }

    private static void CopyInspectorReport(string report)
    {
        try
        {
            typeof(Microsoft.Xna.Framework.Input.TextInputEXT)
                .GetMethod("SetClipboardText", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(null, new object[] { report });
            Engine.Scene?.Add(new AkronToast("Inspector report copied."));
        }
        catch (Exception exception)
        {
            AkronLog.Warn(nameof(AkronEntityInspector), "Failed to copy inspector report: " + exception.Message);
            Engine.Scene?.Add(new AkronToast("Inspector report copy failed."));
        }
    }

    private static bool ShouldIgnoreInspectorPinGameplayClick(Level level)
    {
        return AkronModule.IsOverlayVisible || AkronPromptMenu.IsOpen || level.Paused;
    }

    private static bool IsInsideGameplayViewport(Vector2 screenPoint)
    {
        Viewport viewport = Engine.Viewport;
        float scale = AkronScreenProjection.CurrentViewportScale();
        float left = viewport.X;
        float top = viewport.Y;
        float right = left + 320f * scale;
        float bottom = top + 180f * scale;
        return screenPoint.X >= left &&
               screenPoint.Y >= top &&
               screenPoint.X < right &&
               screenPoint.Y < bottom;
    }

    private static List<InspectorHit> BuildInspectorHitStack(Level level, AkronInspectorPinFilter filter, Vector2 screenPoint, Vector2 gamePoint, Vector2 worldPoint, Point probePixel)
    {
        AssignInspectorIds(level);
        Rectangle probe = new Rectangle(
            probePixel.X - InspectorPinProbeRadiusPixels,
            probePixel.Y - InspectorPinProbeRadiusPixels,
            InspectorPinProbeRadiusPixels * 2 + 1,
            InspectorPinProbeRadiusPixels * 2 + 1);
        List<InspectorHit> hits = new List<InspectorHit>();
        foreach (Entity entity in EnumerateInspectorEntities(level))
        {
            if (!IsInspectorPinCandidate(entity, filter))
            {
                continue;
            }

            if (!TryGetInspectorHitBounds(level, entity, out Rectangle bounds, out bool colliderBacked))
            {
                continue;
            }

            float area = Math.Max(0, bounds.Width) * Math.Max(0, bounds.Height);
            if (area <= 0f || !IntersectsProbe(entity.Collider, bounds, probe))
            {
                continue;
            }

            hits.Add(CreateInspectorHit(level, entity, screenPoint, gamePoint, worldPoint, probePixel, bounds, area, colliderBacked, filter));
        }

        hits.Sort(CompareInspectorHits);
        return hits;
    }

    private static void AssignInspectorIds(Level level)
    {
        foreach (Entity entity in EnumerateInspectorEntities(level))
        {
            if (entity != null && !inspectorIds.ContainsKey(entity))
            {
                inspectorIds[entity] = inspectorPinNextId++;
            }
        }
    }

    private static IEnumerable<Entity> EnumerateInspectorEntities(Level level)
    {
        if (level?.Entities == null)
        {
            return Enumerable.Empty<Entity>();
        }

        List<Entity> entities = AkronEntityListInternals.GetAll(level.Entities)
            .Concat(level.Entities.ToList())
            .ToList();
        if (level.SolidTiles != null)
        {
            entities.Add(level.SolidTiles);
        }

        return entities.Distinct(ReferenceComparer);
    }

    private static bool IsInspectorPinCandidate(Entity entity, AkronInspectorPinFilter filter)
    {
        if (entity == null)
        {
            return false;
        }

        if (entity is Player || entity is PlayerDeadBody || entity is AkronOverlay || entity is AkronToast)
        {
            return false;
        }

        string typeName = entity.GetType().FullName ?? string.Empty;
        if (typeName.StartsWith("Celeste.Mod.UI.", StringComparison.Ordinal) ||
            typeName.StartsWith("Celeste.UI.", StringComparison.Ordinal))
        {
            return false;
        }

        bool isTrigger = entity is Trigger;
        return filter switch
        {
            AkronInspectorPinFilter.Entities => !isTrigger,
            AkronInspectorPinFilter.Triggers => isTrigger,
            _ => true
        };
    }

    private static bool TryGetInspectorHitBounds(Level level, Entity entity, out Rectangle bounds, out bool colliderBacked)
    {
        if (entity?.Collider != null)
        {
            bounds = ColliderWorldBounds(entity.Collider);
            colliderBacked = true;
            return true;
        }

        if (!sourceRecords.TryGetValue(entity, out InspectorSourceRecord record) || record.SourceData == null)
        {
            bounds = default;
            colliderBacked = false;
            return false;
        }

        LevelData room = level?.Session?.MapData?.Get(level.Session.Level);
        Vector2 roomOffset = room?.Position ?? Vector2.Zero;
        float width = record.SourceData.Width > 0 ? record.SourceData.Width : InspectorPinFallbackSourceSize;
        float height = record.SourceData.Height > 0 ? record.SourceData.Height : InspectorPinFallbackSourceSize;
        Vector2 topLeft = roomOffset + record.SourceData.Position;
        if (record.SourceData.Width <= 0)
        {
            topLeft.X -= width * 0.5f;
        }
        if (record.SourceData.Height <= 0)
        {
            topLeft.Y -= height * 0.5f;
        }

        bounds = ToRectangle(topLeft.X, topLeft.Y, width, height);
        colliderBacked = false;
        return true;
    }

    private static bool IntersectsProbe(Collider collider, Rectangle bounds, Rectangle probe)
    {
        if (!bounds.Intersects(probe))
        {
            return false;
        }

        return collider == null || collider.Collide(probe);
    }

    private static InspectorHit CreateInspectorHit(Level level, Entity entity, Vector2 screenPoint, Vector2 gamePoint, Vector2 worldPoint, Point probePixel, Rectangle bounds, float area, bool colliderBacked, AkronInspectorPinFilter filter)
    {
        InspectorSourceBinding binding = ResolveSourceBinding(level, entity);
        return new InspectorHit
        {
            Entity = entity,
            InspectorId = inspectorIds.TryGetValue(entity, out int id) ? id : 0,
            IsTrigger = entity is Trigger,
            Filter = filter,
            Bounds = bounds,
            Area = area,
            ColliderBacked = colliderBacked,
            SourceNodeIndex = binding.SourceNodeIndex,
            SourceOrdinal = binding.SourceOrdinal,
            ClickScreenPoint = screenPoint,
            ClickGamePoint = gamePoint,
            ClickWorldPoint = worldPoint,
            ProbePixel = probePixel,
            Binding = binding
        };
    }

    private static int CompareInspectorHits(InspectorHit left, InspectorHit right)
    {
        int result = right.ColliderBacked.CompareTo(left.ColliderBacked);
        if (result != 0)
        {
            return result;
        }

        result = left.Area.CompareTo(right.Area);
        if (result != 0)
        {
            return result;
        }

        result = left.Entity.Depth.CompareTo(right.Entity.Depth);
        if (result != 0)
        {
            return result;
        }

        result = CategoryRank(left).CompareTo(CategoryRank(right));
        if (result != 0)
        {
            return result;
        }

        result = left.SourceNodeIndex.CompareTo(right.SourceNodeIndex);
        if (result != 0)
        {
            return result;
        }

        result = left.SourceOrdinal.CompareTo(right.SourceOrdinal);
        if (result != 0)
        {
            return result;
        }

        result = string.Compare(left.Entity.GetType().FullName, right.Entity.GetType().FullName, StringComparison.Ordinal);
        if (result != 0)
        {
            return result;
        }

        result = string.Compare(left.Entity.SourceId.ToString(), right.Entity.SourceId.ToString(), StringComparison.Ordinal);
        if (result != 0)
        {
            return result;
        }

        return left.InspectorId.CompareTo(right.InspectorId);
    }

    private static int CategoryRank(InspectorHit hit)
    {
        if (hit.Entity is SolidTiles)
        {
            return 2;
        }

        return hit.IsTrigger ? 1 : 0;
    }

    private static string BuildInspectorStackSignature(AkronInspectorPinFilter filter, List<InspectorHit> hits)
    {
        return inspectorPinRoomSessionId.ToString(CultureInfo.InvariantCulture) +
               "|" + filter +
               "|" + string.Join(",", hits.Select(hit => hit.InspectorId.ToString(CultureInfo.InvariantCulture)));
    }

    private static void ApplyInspectorPinHits(Vector2 screenPoint, string signature, List<InspectorHit> hits, bool sameStack)
    {
        currentStack.Clear();
        currentStack.AddRange(hits);
        inspectorPinStackSignature = signature;
        inspectorPinAnchorScreen = screenPoint;
        inspectorPinSelectedIndex = sameStack
            ? (inspectorPinSelectedIndex + 1) % currentStack.Count
            : 0;
        inspectorPinSelectedEntity = currentStack[inspectorPinSelectedIndex].Entity;
        if (!sameStack)
        {
            inspectorPinPositionInitialized = false;
            inspectorPinPropertiesOpen = AkronModule.Settings.EntityInspectorPinShowPropertiesByDefault;
        }
    }

    private static InspectorSourceBinding ResolveSourceBinding(Level level, Entity entity)
    {
        if (level?.Session?.MapData == null)
        {
            return InspectorSourceBinding.Unavailable();
        }

        if (!sourceRecords.TryGetValue(entity, out InspectorSourceRecord record))
        {
            return InspectorSourceBinding.NoExact();
        }

        if (record.SourceData == null)
        {
            return InspectorSourceBinding.Generated(record.SourceId);
        }

        LevelData room = level.Session.MapData.Get(level.Session.Level);
        if (room == null)
        {
            return InspectorSourceBinding.Unavailable();
        }

        List<EntityData> nodes = entity is Trigger ? room.Triggers : room.Entities;
        List<int> matches = new List<int>();
        if (nodes != null)
        {
            for (int index = 0; index < nodes.Count; index++)
            {
                if (ReferenceEquals(nodes[index], record.SourceData))
                {
                    matches.Add(index);
                }
            }
        }

        if (matches.Count == 1)
        {
            return InspectorSourceBinding.Bound(record, matches[0]);
        }

        return matches.Count > 1
            ? InspectorSourceBinding.Ambiguous(record.SourceId)
            : InspectorSourceBinding.NoExact(record.SourceId);
    }

    private static AkronInspectorReportData BuildInspectorReportData(Level level, InspectorHit selected, List<InspectorHit> stack, int selectedIndex)
    {
        Entity entity = selected.Entity;
        InspectorSourceBinding binding = ResolveSourceBinding(level, entity);
        AkronInspectorReportData data = new AkronInspectorReportData
        {
            Category = entity is SolidTiles ? "Solid Tiles" : entity is Trigger ? "Trigger" : "Entity",
            DisplayName = entity.GetType().Name,
            FullTypeName = entity.GetType().FullName ?? entity.GetType().Name,
            Room = level.Session?.Level ?? string.Empty,
            MapSid = level.Session?.Area.GetSID() ?? string.Empty,
            SourceId = entity.SourceId.ToString(),
            InspectorId = selected.InspectorId,
            RoomSessionId = inspectorPinRoomSessionId,
            StackIndex = selectedIndex,
            StackCount = stack.Count,
            EntityCount = stack.Count(hit => !hit.IsTrigger),
            TriggerCount = stack.Count(hit => hit.IsTrigger),
            Position = entity.Position,
            Center = entity.Center,
            ColliderKind = entity.Collider?.GetType().Name ?? "none",
            ColliderBounds = selected.Bounds,
            ColliderArea = selected.Area,
            Depth = entity.Depth,
            Active = entity.Active,
            Visible = entity.Visible,
            Collidable = entity.Collidable,
            Filter = selected.Filter,
            ClickScreenPoint = selected.ClickScreenPoint,
            ClickGamePoint = selected.ClickGamePoint,
            ClickWorldPoint = selected.ClickWorldPoint,
            HitProbePixel = selected.ProbePixel,
            MapBindingStatus = binding.Status,
            MapPlacementLabel = FormatMapBindingStatus(binding.Status),
            SourceNodeIndex = binding.SourceNodeIndex,
            SourceOrdinal = binding.SourceOrdinal,
            SourceObjectCount = binding.SourceObjectCount,
            StackSignature = inspectorPinStackSignature
        };

        data.Summary = data.Category + " " + data.DisplayName + " @ " + FormatVector(entity.Position);
        foreach (InspectorHit hit in stack)
        {
            data.StackEntries.Add(hit.InspectorId.ToString(CultureInfo.InvariantCulture) + ":" + (hit.Entity.GetType().FullName ?? hit.Entity.GetType().Name));
        }

        AddRuntimeRows(data);
        AddPlacementRows(level, data, binding);
        return data;
    }

    private static void AddRuntimeRows(AkronInspectorReportData data)
    {
        data.RuntimeRows.Add(new AkronInspectorPropertyRow("fullType", data.FullTypeName));
        data.RuntimeRows.Add(new AkronInspectorPropertyRow("inspectorId", data.InspectorId.ToString(CultureInfo.InvariantCulture)));
        data.RuntimeRows.Add(new AkronInspectorPropertyRow("sourceId", data.SourceId));
        data.RuntimeRows.Add(new AkronInspectorPropertyRow("room", data.Room));
        data.RuntimeRows.Add(new AkronInspectorPropertyRow("position", FormatVector(data.Position)));
        data.RuntimeRows.Add(new AkronInspectorPropertyRow("center", FormatVector(data.Center)));
        data.RuntimeRows.Add(new AkronInspectorPropertyRow("colliderKind", data.ColliderKind));
        data.RuntimeRows.Add(new AkronInspectorPropertyRow("colliderBounds", FormatRectangle(data.ColliderBounds)));
        data.RuntimeRows.Add(new AkronInspectorPropertyRow("boundingBoxArea", FormatFloat(data.ColliderArea)));
        data.RuntimeRows.Add(new AkronInspectorPropertyRow("depth", data.Depth.ToString(CultureInfo.InvariantCulture)));
        data.RuntimeRows.Add(new AkronInspectorPropertyRow("active", data.Active.ToString().ToLowerInvariant()));
        data.RuntimeRows.Add(new AkronInspectorPropertyRow("visible", data.Visible.ToString().ToLowerInvariant()));
        data.RuntimeRows.Add(new AkronInspectorPropertyRow("collidable", data.Collidable.ToString().ToLowerInvariant()));
        data.RuntimeRows.Add(new AkronInspectorPropertyRow("filter", data.Filter.ToString()));
        data.RuntimeRows.Add(new AkronInspectorPropertyRow("clickScreen", FormatVector(data.ClickScreenPoint)));
        data.RuntimeRows.Add(new AkronInspectorPropertyRow("clickGame", FormatVector(data.ClickGamePoint)));
        data.RuntimeRows.Add(new AkronInspectorPropertyRow("clickWorld", FormatVector(data.ClickWorldPoint)));
        data.RuntimeRows.Add(new AkronInspectorPropertyRow("stack", (data.StackIndex + 1).ToString(CultureInfo.InvariantCulture) + "/" + data.StackCount.ToString(CultureInfo.InvariantCulture)));
    }

    private static void AddPlacementRows(Level level, AkronInspectorReportData data, InspectorSourceBinding binding)
    {
        if (binding.Status != AkronInspectorMapBindingStatus.EverestSourceBound || binding.SourceData == null)
        {
            return;
        }

        EntityData source = binding.SourceData;
        LevelData room = level.Session.MapData.Get(level.Session.Level);
        Vector2 roomOffset = room?.Position ?? Vector2.Zero;
        data.PlacementRows.Add(new AkronInspectorPropertyRow("room", data.Room));
        data.PlacementRows.Add(new AkronInspectorPropertyRow("category", data.Category));
        data.PlacementRows.Add(new AkronInspectorPropertyRow("mapName", source.Name ?? string.Empty));
        data.PlacementRows.Add(new AkronInspectorPropertyRow("mapId", source.ID.ToString(CultureInfo.InvariantCulture)));
        data.PlacementRows.Add(new AkronInspectorPropertyRow("localX", FormatFloat(source.Position.X)));
        data.PlacementRows.Add(new AkronInspectorPropertyRow("localY", FormatFloat(source.Position.Y)));
        data.PlacementRows.Add(new AkronInspectorPropertyRow("roomOffsetX", FormatFloat(roomOffset.X)));
        data.PlacementRows.Add(new AkronInspectorPropertyRow("roomOffsetY", FormatFloat(roomOffset.Y)));
        data.PlacementRows.Add(new AkronInspectorPropertyRow("worldX", FormatFloat(roomOffset.X + source.Position.X)));
        data.PlacementRows.Add(new AkronInspectorPropertyRow("worldY", FormatFloat(roomOffset.Y + source.Position.Y)));
        data.PlacementRows.Add(new AkronInspectorPropertyRow("width", source.Width.ToString(CultureInfo.InvariantCulture)));
        data.PlacementRows.Add(new AkronInspectorPropertyRow("height", source.Height.ToString(CultureInfo.InvariantCulture)));
        data.PlacementRows.Add(new AkronInspectorPropertyRow("nodes", FormatInspectorValue(source.Nodes ?? Array.Empty<Vector2>())));

        if (source.Values == null)
        {
            return;
        }

        foreach (string key in source.Values.Keys.OrderBy(key => key, StringComparer.Ordinal))
        {
            string displayKey = BuiltInPlacementKeys.Contains(key) ? "values." + key : key;
            data.AuthoredRows.Add(new AkronInspectorPropertyRow(displayKey, FormatInspectorValue(source.Values[key])));
        }
    }

    private static readonly HashSet<string> BuiltInPlacementKeys = new HashSet<string>(StringComparer.Ordinal) {
        "room",
        "category",
        "mapName",
        "mapId",
        "localX",
        "localY",
        "roomOffsetX",
        "roomOffsetY",
        "worldX",
        "worldY",
        "width",
        "height",
        "nodes"
    };

    private static string FormatMapBindingStatus(AkronInspectorMapBindingStatus status)
    {
        return status switch
        {
            AkronInspectorMapBindingStatus.EverestSourceBound => "Everest source-bound",
            AkronInspectorMapBindingStatus.RuntimeOnlyAmbiguousMapNode => "Runtime only: ambiguous map node",
            AkronInspectorMapBindingStatus.RuntimeOnlyGeneratedEntity => "Runtime only: generated entity",
            AkronInspectorMapBindingStatus.MapDataUnavailable => "Map data unavailable",
            _ => "Runtime only: no exact map node"
        };
    }

    private static void DrawInspectorPinImGui(Level level)
    {
        float scale = AkronOverlay.CurrentOverlayScale();
        AkronOverlay.ApplyOverlayThemePreset(scale);
        AkronInspectorReportData data = BuildInspectorReportData(level, currentStack[inspectorPinSelectedIndex], currentStack, inspectorPinSelectedIndex);

        NumericsVector2 displaySize = ImGui.GetIO().DisplaySize;
        NumericsVector2 expectedSize = new NumericsVector2(
            (inspectorPinPropertiesOpen ? 620f : 420f) * scale,
            (inspectorPinPropertiesOpen ? 620f : 260f) * scale);
        AkronInspectorPinPlacement placement = NormalizeInspectorPinPlacement(AkronModule.Settings.EntityInspectorPinPlacement);
        if (placement != inspectorPinLastPlacement)
        {
            inspectorPinLastPlacement = placement;
            inspectorPinPositionInitialized = false;
        }
        NumericsVector2 position = CalculateInspectorPinImGuiPosition(displaySize, expectedSize, placement);
        string cycle = (data.StackIndex + 1).ToString(CultureInfo.InvariantCulture) + "/" + data.StackCount.ToString(CultureInfo.InvariantCulture);

        if (!inspectorPinPositionInitialized || IsFixedInspectorPinPlacement(placement))
        {
            ImGui.SetNextWindowPos(position, ImGuiCond.Always);
            inspectorPinPositionInitialized = true;
        }
        ImGui.SetNextWindowSizeConstraints(
            new NumericsVector2(expectedSize.X, 0f),
            new NumericsVector2(expectedSize.X, Math.Max(1f, displaySize.Y - 16f * scale)));
        // Match Akron's regular HUD panels: the selected theme provides the
        // title/header colors and the overlay opacity setting controls the
        // window surface alpha.
        ImGui.SetNextWindowBgAlpha(AkronModuleSettings.ClampOverlayOpacity(AkronModule.Settings.OverlayOpacity) / 100f);

        ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.AlwaysAutoResize;

        bool visible = ImGui.Begin("Inspector Pin " + cycle + "##akron_inspector_pin", flags);
        if (!visible)
        {
            NumericsVector2 collapsedWindowPos = ImGui.GetWindowPos();
            NumericsVector2 collapsedWindowSize = ImGui.GetWindowSize();
            SaveInspectorPinWindowPosition(collapsedWindowPos, position, placement);
            inspectorPinCardRect = ToRectangle(
                collapsedWindowPos.X,
                collapsedWindowPos.Y,
                collapsedWindowSize.X,
                collapsedWindowSize.Y);
            ImGui.End();
            return;
        }

        DrawInspectorPinInfoRow("Target", data.Filter.ToString());
        ImGui.Separator();
        DrawInspectorPinInfoRow("Selected", data.Category + " " + data.DisplayName);
        DrawInspectorPinInfoRow("Room", data.Room);
        DrawInspectorPinInfoRow("Position", FormatVector(data.Position));
        DrawInspectorPinInfoRow("Cycle", cycle);
        if (data.Filter == AkronInspectorPinFilter.Both && data.StackCount > 1)
        {
            DrawInspectorPinInfoRow("Stack", data.EntityCount.ToString(CultureInfo.InvariantCulture) + " entities, " + data.TriggerCount.ToString(CultureInfo.InvariantCulture) + " triggers");
        }

        if (inspectorPinPropertiesOpen)
        {
            ImGui.Spacing();
            DrawInspectorPinSectionTitle("Runtime");
            DrawInspectorPinRows(data.RuntimeRows);
            DrawInspectorPinSectionTitle("Map Placement");
            DrawInspectorPinInfoRow("Status", data.MapPlacementLabel);
            DrawInspectorPinRows(data.PlacementRows);
            if (data.AuthoredRows.Count > 0)
            {
                DrawInspectorPinSectionTitle("Authored Properties");
                DrawInspectorPinRows(data.AuthoredRows);
            }
        }

        ImGui.Spacing();
        float buttonWidth = Math.Max(112f * scale, (ImGui.GetContentRegionAvail().X - 8f * scale) / 2f);
        if (ImGui.Button((inspectorPinPropertiesOpen ? "Hide Properties" : "Properties") + "##akron_inspector_pin_properties", new NumericsVector2(buttonWidth, 0f)))
        {
            inspectorPinPropertiesOpen = !inspectorPinPropertiesOpen;
            if (placement != AkronInspectorPinPlacement.Custom)
            {
                inspectorPinPositionInitialized = false;
            }
        }
        ImGui.SameLine();
        string copyLabel = Engine.FrameCounter < (ulong)inspectorPinCopiedUntilFrame ? "Copied" : "Copy";
        if (ImGui.Button(copyLabel + "##akron_inspector_pin_copy", new NumericsVector2(buttonWidth, 0f)))
        {
            CopyInspectorReport(BuildVisibleCopyReport(data, inspectorPinPropertiesOpen));
            inspectorPinCopiedUntilFrame = (int)Engine.FrameCounter + 54;
        }

        NumericsVector2 windowPos = ImGui.GetWindowPos();
        NumericsVector2 windowSize = ImGui.GetWindowSize();
        SaveInspectorPinWindowPosition(windowPos, position, placement);
        inspectorPinCardRect = ToRectangle(windowPos.X, windowPos.Y, windowSize.X, windowSize.Y);
        ImGui.End();
    }

    private static AkronInspectorPinPlacement NormalizeInspectorPinPlacement(AkronInspectorPinPlacement placement)
    {
        return Enum.IsDefined(typeof(AkronInspectorPinPlacement), placement)
            ? placement
            : AkronInspectorPinPlacement.NearClick;
    }

    private static bool IsFixedInspectorPinPlacement(AkronInspectorPinPlacement placement)
    {
        return placement == AkronInspectorPinPlacement.TopLeft ||
               placement == AkronInspectorPinPlacement.TopRight ||
               placement == AkronInspectorPinPlacement.BottomLeft ||
               placement == AkronInspectorPinPlacement.BottomRight;
    }

    private static NumericsVector2 CalculateInspectorPinImGuiPosition(NumericsVector2 displaySize, NumericsVector2 expectedSize, AkronInspectorPinPlacement placement)
    {
        const float margin = 8f;
        if (placement == AkronInspectorPinPlacement.TopLeft)
        {
            return new NumericsVector2(margin, margin);
        }
        if (placement == AkronInspectorPinPlacement.TopRight)
        {
            return new NumericsVector2(Math.Max(margin, displaySize.X - expectedSize.X - margin), margin);
        }
        if (placement == AkronInspectorPinPlacement.BottomLeft)
        {
            return new NumericsVector2(margin, Math.Max(margin, displaySize.Y - expectedSize.Y - margin));
        }
        if (placement == AkronInspectorPinPlacement.BottomRight)
        {
            return new NumericsVector2(
                Math.Max(margin, displaySize.X - expectedSize.X - margin),
                Math.Max(margin, displaySize.Y - expectedSize.Y - margin));
        }
        if (placement == AkronInspectorPinPlacement.Custom)
        {
            return new NumericsVector2(
                Calc.Clamp(AkronModule.Settings.EntityInspectorPinX, (int)margin, (int)Math.Max(margin, displaySize.X - expectedSize.X - margin)),
                Calc.Clamp(AkronModule.Settings.EntityInspectorPinY, (int)margin, (int)Math.Max(margin, displaySize.Y - expectedSize.Y - margin)));
        }

        float x = inspectorPinAnchorScreen.X + 20f;
        float y = inspectorPinAnchorScreen.Y + 20f;
        if (x + expectedSize.X > displaySize.X - 8f)
        {
            x = inspectorPinAnchorScreen.X - expectedSize.X - 20f;
        }
        if (y + expectedSize.Y > displaySize.Y - 8f)
        {
            y = inspectorPinAnchorScreen.Y - expectedSize.Y - 20f;
        }

        return new NumericsVector2(
            Calc.Clamp(x, 8f, Math.Max(8f, displaySize.X - expectedSize.X - 8f)),
            Calc.Clamp(y, 8f, Math.Max(8f, displaySize.Y - expectedSize.Y - 8f)));
    }

    private static void SaveInspectorPinWindowPosition(NumericsVector2 windowPos, NumericsVector2 expectedPosition, AkronInspectorPinPlacement placement)
    {
        AkronModule.Settings.EntityInspectorPinX = Calc.Clamp((int)Math.Round(windowPos.X), 0, 10000);
        AkronModule.Settings.EntityInspectorPinY = Calc.Clamp((int)Math.Round(windowPos.Y), 0, 10000);
        if (placement != AkronInspectorPinPlacement.Custom &&
            NumericsVector2.DistanceSquared(windowPos, expectedPosition) > 4f &&
            ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows))
        {
            AkronModule.Settings.EntityInspectorPinPlacement = AkronInspectorPinPlacement.Custom;
            inspectorPinLastPlacement = AkronInspectorPinPlacement.Custom;
        }
    }

    private static void DrawInspectorPinSectionTitle(string title)
    {
        ImGui.TextUnformatted(title);
        ImGui.Separator();
    }

    private static void DrawInspectorPinInfoRow(string label, string value)
    {
        float width = ImGui.GetContentRegionAvail().X;
        float labelWidth = CalculateInspectorPinLabelWidth();
        ImGui.PushStyleColor(ImGuiCol.Text, AkronImGuiTheme.Muted);
        ImGui.TextUnformatted(TruncateInspectorPinImGuiText(label ?? string.Empty, Math.Max(1f, labelWidth - 8f)));
        ImGui.PopStyleColor();
        ImGui.SameLine(labelWidth);
        ImGui.PushStyleColor(ImGuiCol.Text, AkronImGuiTheme.Foreground);
        ImGui.TextUnformatted(TruncateInspectorPinImGuiText(value ?? string.Empty, Math.Max(1f, width - labelWidth - 8f)));
        ImGui.PopStyleColor();
    }

    private static float CalculateInspectorPinLabelWidth()
    {
        float scale = AkronOverlay.CurrentOverlayScale();
        float width = ImGui.GetContentRegionAvail().X;
        return Math.Min(132f * scale, Math.Max(86f * scale, width * 0.34f));
    }

    private static void DrawInspectorPinRows(List<AkronInspectorPropertyRow> rows)
    {
        foreach (AkronInspectorPropertyRow row in rows)
        {
            DrawInspectorPinInfoRow(row.Key, row.Value);
        }
    }

    private static string TruncateInspectorPinImGuiText(string text, float maxWidth)
    {
        if (string.IsNullOrEmpty(text) || ImGui.CalcTextSize(text).X <= maxWidth)
        {
            return text;
        }

        const string suffix = "...";
        for (int length = text.Length - 1; length > 0; length--)
        {
            string candidate = text[..length].TrimEnd() + suffix;
            if (ImGui.CalcTextSize(candidate).X <= maxWidth)
            {
                return candidate;
            }
        }

        return suffix;
    }

    private static void DrawDashedWorldRect(Level level, Rectangle worldBounds, Color color)
    {
        AkronHudRect rect = WorldToHitboxSurfaceRect(level, worldBounds);
        const float dash = 8f;
        const float gap = 5f;
        float thickness = 2f;
        for (float x = rect.X; x < rect.X + rect.Width; x += dash + gap)
        {
            Draw.Rect(x, rect.Y, Math.Min(dash, rect.X + rect.Width - x), thickness, color);
            Draw.Rect(x, rect.Y + rect.Height - thickness, Math.Min(dash, rect.X + rect.Width - x), thickness, color);
        }

        for (float y = rect.Y; y < rect.Y + rect.Height; y += dash + gap)
        {
            Draw.Rect(rect.X, y, thickness, Math.Min(dash, rect.Y + rect.Height - y), color);
            Draw.Rect(rect.X + rect.Width - thickness, y, thickness, Math.Min(dash, rect.Y + rect.Height - y), color);
        }
    }

    private static Rectangle ToRectangle(float x, float y, float width, float height)
    {
        return new Rectangle((int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Ceiling(width), (int)Math.Ceiling(height));
    }

    private static string FormatVector(Vector2 vector)
    {
        return FormatFloat(vector.X) + ", " + FormatFloat(vector.Y);
    }

    private static string FormatRectangle(Rectangle rectangle)
    {
        return rectangle.X.ToString(CultureInfo.InvariantCulture) + "," +
               rectangle.Y.ToString(CultureInfo.InvariantCulture) + " " +
               rectangle.Width.ToString(CultureInfo.InvariantCulture) + "x" +
               rectangle.Height.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatFloat(float value)
    {
        if (float.IsNaN(value))
        {
            return "\"NaN\"";
        }
        if (float.IsPositiveInfinity(value))
        {
            return "\"Infinity\"";
        }
        if (float.IsNegativeInfinity(value))
        {
            return "\"-Infinity\"";
        }

        return value.ToString("G9", CultureInfo.InvariantCulture);
    }

    private static string FormatDouble(double value)
    {
        if (double.IsNaN(value))
        {
            return "\"NaN\"";
        }
        if (double.IsPositiveInfinity(value))
        {
            return "\"Infinity\"";
        }
        if (double.IsNegativeInfinity(value))
        {
            return "\"-Infinity\"";
        }

        return value.ToString("G17", CultureInfo.InvariantCulture);
    }

    private static string FormatEnumerable(IEnumerable enumerable)
    {
        return "[" + string.Join(", ", enumerable.Cast<object>().Select(FormatInspectorValue)) + "]";
    }

    private static string FormatDictionary(IDictionary dictionary)
    {
        List<object> sortedKeys = dictionary.Keys.Cast<object>()
            .OrderBy(key => key?.ToString() ?? string.Empty, StringComparer.Ordinal)
            .ToList();
        List<string> entries = new List<string>();
        foreach (object key in sortedKeys)
        {
            entries.Add(QuoteJsonString(key?.ToString() ?? string.Empty) + ": " + FormatInspectorValue(dictionary[key]));
        }

        return "{ " + string.Join(", ", entries) + " }";
    }

    private static string QuoteJsonString(string text)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append('"');
        foreach (char character in text ?? string.Empty)
        {
            switch (character)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }
        builder.Append('"');
        return builder.ToString();
    }

    private static void AppendReportRow(StringBuilder builder, string key, string value)
    {
        builder.AppendLine("  " + key + ": " + (value ?? string.Empty));
    }

    private static void AppendRows(StringBuilder builder, string heading, List<AkronInspectorPropertyRow> rows)
    {
        builder.AppendLine(heading + ":");
        foreach (AkronInspectorPropertyRow row in rows)
        {
            builder.AppendLine("  " + row.Key + ": " + row.Value);
        }
    }

    private sealed class InspectorHit
    {
        public Entity Entity { get; set; }
        public int InspectorId { get; set; }
        public bool IsTrigger { get; set; }
        public AkronInspectorPinFilter Filter { get; set; }
        public Rectangle Bounds { get; set; }
        public float Area { get; set; }
        public bool ColliderBacked { get; set; }
        public int SourceNodeIndex { get; set; } = int.MaxValue;
        public int SourceOrdinal { get; set; } = int.MaxValue;
        public Vector2 ClickScreenPoint { get; set; }
        public Vector2 ClickGamePoint { get; set; }
        public Vector2 ClickWorldPoint { get; set; }
        public Point ProbePixel { get; set; }
        public InspectorSourceBinding Binding { get; set; }
    }

    private sealed class InspectorSourceRecord
    {
        public InspectorSourceRecord(EntityData sourceData, EntityID sourceId, int ordinal, InspectorSourceOrdinalState ordinalState, int roomSessionId)
        {
            SourceData = sourceData;
            SourceId = sourceId;
            Ordinal = ordinal;
            OrdinalState = ordinalState;
            RoomSessionId = roomSessionId;
        }

        public EntityData SourceData { get; }
        public EntityID SourceId { get; }
        public int Ordinal { get; }
        public InspectorSourceOrdinalState OrdinalState { get; }
        public int RoomSessionId { get; }
    }

    private sealed class InspectorSourceOrdinalState
    {
        public int Count { get; set; }
    }

    private readonly struct InspectorSourceBinding
    {
        private InspectorSourceBinding(AkronInspectorMapBindingStatus status, EntityData sourceData, EntityID sourceId, int sourceNodeIndex, int sourceOrdinal, int sourceObjectCount)
        {
            Status = status;
            SourceData = sourceData;
            SourceId = sourceId;
            SourceNodeIndex = sourceNodeIndex;
            SourceOrdinal = sourceOrdinal;
            SourceObjectCount = sourceObjectCount;
        }

        public AkronInspectorMapBindingStatus Status { get; }
        public EntityData SourceData { get; }
        public EntityID SourceId { get; }
        public int SourceNodeIndex { get; }
        public int SourceOrdinal { get; }
        public int SourceObjectCount { get; }

        public static InspectorSourceBinding Bound(InspectorSourceRecord record, int sourceNodeIndex)
        {
            return new InspectorSourceBinding(
                AkronInspectorMapBindingStatus.EverestSourceBound,
                record.SourceData,
                record.SourceId,
                sourceNodeIndex,
                record.Ordinal,
                record.OrdinalState?.Count ?? 1);
        }

        public static InspectorSourceBinding Generated(EntityID sourceId)
        {
            return new InspectorSourceBinding(AkronInspectorMapBindingStatus.RuntimeOnlyGeneratedEntity, null, sourceId, int.MaxValue, int.MaxValue, 0);
        }

        public static InspectorSourceBinding NoExact(EntityID sourceId = default)
        {
            return new InspectorSourceBinding(AkronInspectorMapBindingStatus.RuntimeOnlyNoExactMapNode, null, sourceId, int.MaxValue, int.MaxValue, 0);
        }

        public static InspectorSourceBinding Ambiguous(EntityID sourceId)
        {
            return new InspectorSourceBinding(AkronInspectorMapBindingStatus.RuntimeOnlyAmbiguousMapNode, null, sourceId, int.MaxValue, int.MaxValue, 0);
        }

        public static InspectorSourceBinding Unavailable()
        {
            return new InspectorSourceBinding(AkronInspectorMapBindingStatus.MapDataUnavailable, null, default, int.MaxValue, int.MaxValue, 0);
        }
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>, IEqualityComparer<Entity>
    {
        public new bool Equals(object left, object right)
        {
            return ReferenceEquals(left, right);
        }

        public int GetHashCode(object value)
        {
            return RuntimeHelpers.GetHashCode(value);
        }

        public bool Equals(Entity left, Entity right)
        {
            return ReferenceEquals(left, right);
        }

        public int GetHashCode(Entity value)
        {
            return RuntimeHelpers.GetHashCode(value);
        }
    }
}
