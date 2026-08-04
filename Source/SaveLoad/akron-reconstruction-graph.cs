using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FMOD.Studio;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoMod.RuntimeDetour;
using Monocle;
using Newtonsoft.Json;

namespace Celeste.Mod.Akron;

internal sealed class AkronReconstructionDocument {
    public const string CurrentFormat = "akron-reconstruction-v6";

    public string Format { get; set; } = CurrentFormat;
    public string SlotName { get; set; } = string.Empty;
    public string MapSid { get; set; } = string.Empty;
    public string Room { get; set; } = string.Empty;
    public int FileSlot { get; set; } = -1;
    public int RootNodeId { get; set; }
    public List<AkronReconstructionNode> Nodes { get; set; } = new List<AkronReconstructionNode>();
    public AkronReconstructionDocument ActionStateDocument { get; set; }
    public List<string> RegisteredActionIds { get; set; } = new List<string>();
    public List<AkronGameplayBufferSnapshot> GameplayBuffers { get; set; } = new List<AkronGameplayBufferSnapshot>();
}

internal sealed class AkronReconstructionNode {
    public int Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    // Full diagnostic paths grow quadratically with graph depth. Keep the
    // compact first-owner edge on disk and rebuild this text after loading.
    [JsonIgnore]
    public string Path { get; set; } = string.Empty;
    public int ParentNodeId { get; set; }
    public string ParentKind { get; set; } = string.Empty;
    public string ParentDeclaringTypeName { get; set; } = string.Empty;
    public string ParentFieldName { get; set; } = string.Empty;
    public List<int> ParentArrayIndices { get; set; } = new List<int>();
    public int ParentDelegateIndex { get; set; } = -1;
    public bool UseFreshObject { get; set; }
    public string ResourceKey { get; set; } = string.Empty;
    public List<AkronReconstructionPathStep> FreshPath { get; set; } = new List<AkronReconstructionPathStep>();
    public List<AkronReconstructionField> Fields { get; set; } = new List<AkronReconstructionField>();
    public List<AkronReconstructionValue> Items { get; set; } = new List<AkronReconstructionValue>();
    public List<int> ArrayLengths { get; set; } = new List<int>();
    public List<int> ArrayLowerBounds { get; set; } = new List<int>();
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public byte[] PackedPrimitiveArrayBytes { get; set; }
    public List<AkronReconstructionDelegateCall> DelegateCalls { get; set; } = new List<AkronReconstructionDelegateCall>();
    public AkronPersistentEventInstanceState EventInstance { get; set; }
    public AkronReconstructionResourcePayload ResourcePayload { get; set; }
}

internal sealed class AkronReconstructionField {
    public string DeclaringTypeName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    [JsonIgnore]
    public string Path { get; set; } = string.Empty;
    public AkronReconstructionValue Value { get; set; } = new AkronReconstructionValue();
}

internal sealed class AkronReconstructionValue {
    [System.ComponentModel.DefaultValue("null")]
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string Kind { get; set; } = "null";
    [System.ComponentModel.DefaultValue("")]
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string TypeName { get; set; } = string.Empty;
    [System.ComponentModel.DefaultValue("")]
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string Scalar { get; set; } = string.Empty;
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    public int NodeId { get; set; }
}

internal sealed class AkronReconstructionResourcePayload {
    public string Kind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public int MultiSampleCount { get; set; }
    public bool Depth { get; set; }
    public bool Preserve { get; set; }
    public byte[] Bytes { get; set; } = Array.Empty<byte>();
}

internal sealed class AkronGameplayBufferSnapshot {
    public string FieldName { get; set; } = string.Empty;
    public AkronReconstructionResourcePayload Payload { get; set; } = new AkronReconstructionResourcePayload();
}

internal interface IAkronReconstructionResourceAdapter {
    bool CanPersist(Type type);
    AkronReconstructionResourcePayload Capture(object resource);
    object Restore(AkronReconstructionResourcePayload payload, object freshResource);
    bool Verify(AkronReconstructionResourcePayload payload, object resource);
}

// VirtualRenderTarget is process-owned, but some room effects create targets
// only after they have run. Persist those room-owned pixels because a normal
// fresh-room load cannot provide an equivalent object to rebind.
internal sealed class AkronVirtualRenderTargetResourceAdapter : IAkronReconstructionResourceAdapter {
    private const string PayloadKind = "virtual-render-target-rgba-v1";

    public bool CanPersist(Type type) {
        return type == typeof(VirtualRenderTarget);
    }

    public AkronReconstructionResourcePayload Capture(object resource) {
        VirtualRenderTarget renderTarget = (VirtualRenderTarget) resource;
        ValidateRenderTarget(renderTarget);
        byte[] pixels = new byte[checked(renderTarget.Width * renderTarget.Height * 4)];
        renderTarget.Target.GetData(pixels);
        return new AkronReconstructionResourcePayload {
            Kind = PayloadKind,
            Name = renderTarget.Name ?? string.Empty,
            Width = renderTarget.Width,
            Height = renderTarget.Height,
            MultiSampleCount = renderTarget.MultiSampleCount,
            Depth = renderTarget.Depth,
            Preserve = renderTarget.Preserve,
            Bytes = pixels
        };
    }

    public object Restore(AkronReconstructionResourcePayload payload, object freshResource) {
        ValidatePayload(payload);
        VirtualRenderTarget renderTarget = freshResource as VirtualRenderTarget;
        bool created = false;
        if (!DescriptorMatches(payload, renderTarget)) {
            renderTarget = VirtualContent.CreateRenderTarget(
                payload.Name,
                payload.Width,
                payload.Height,
                payload.Depth,
                payload.Preserve,
                payload.MultiSampleCount);
            created = true;
        }
        try {
            renderTarget.Target.SetData(payload.Bytes);
            return renderTarget;
        } catch {
            if (created) {
                renderTarget.Dispose();
            }
            throw;
        }
    }

    public void RestoreExisting(AkronReconstructionResourcePayload payload, VirtualRenderTarget renderTarget) {
        ValidatePayload(payload);
        if (!DescriptorMatches(payload, renderTarget)) {
            throw new InvalidOperationException("Virtual render target descriptor differs.");
        }
        renderTarget.Target.SetData(payload.Bytes);
    }

    public bool Verify(AkronReconstructionResourcePayload payload, object resource) {
        try {
            ValidatePayload(payload);
            if (resource is not VirtualRenderTarget renderTarget || !DescriptorMatches(payload, renderTarget)) {
                return false;
            }
            byte[] pixels = new byte[payload.Bytes.Length];
            renderTarget.Target.GetData(pixels);
            return pixels.SequenceEqual(payload.Bytes);
        } catch {
            return false;
        }
    }

    private static bool DescriptorMatches(
        AkronReconstructionResourcePayload payload,
        VirtualRenderTarget renderTarget
    ) {
        return renderTarget != null && !renderTarget.IsDisposed &&
               renderTarget.Name == payload.Name &&
               renderTarget.Width == payload.Width &&
               renderTarget.Height == payload.Height &&
               renderTarget.MultiSampleCount == payload.MultiSampleCount &&
               renderTarget.Depth == payload.Depth &&
               renderTarget.Preserve == payload.Preserve;
    }

    private static void ValidateRenderTarget(VirtualRenderTarget renderTarget) {
        if (renderTarget == null || renderTarget.IsDisposed || renderTarget.Target == null) {
            throw new InvalidOperationException("Virtual render target is unavailable.");
        }
        _ = checked(renderTarget.Width * renderTarget.Height * 4);
    }

    private static void ValidatePayload(AkronReconstructionResourcePayload payload) {
        if (payload == null || payload.Kind != PayloadKind || payload.Width <= 0 || payload.Height <= 0) {
            throw new InvalidOperationException("Virtual render target payload is invalid.");
        }
        int expectedBytes = checked(payload.Width * payload.Height * 4);
        if (payload.Bytes == null || payload.Bytes.Length != expectedBytes) {
            throw new InvalidOperationException("Virtual render target pixel count differs.");
        }
    }
}

internal static class AkronGameplayBufferState {
    private static readonly AkronVirtualRenderTargetResourceAdapter Adapter = new AkronVirtualRenderTargetResourceAdapter();
    private static byte[] armedLevelPresentation;
    private static Level armedPresentationLevel;

    public static List<AkronGameplayBufferSnapshot> Capture() {
        List<AkronGameplayBufferSnapshot> snapshots = new List<AkronGameplayBufferSnapshot>();
        foreach (FieldInfo field in GetBufferFields()) {
            if (field.GetValue(null) is not VirtualRenderTarget renderTarget) {
                throw new InvalidOperationException("Gameplay buffer is unavailable: " + field.Name);
            }
            snapshots.Add(new AkronGameplayBufferSnapshot {
                FieldName = field.Name,
                Payload = Adapter.Capture(renderTarget)
            });
        }
        return snapshots;
    }

    public static bool Restore(IReadOnlyList<AkronGameplayBufferSnapshot> snapshots, out string error) {
        error = string.Empty;
        try {
            Dictionary<string, AkronGameplayBufferSnapshot> savedByName =
                (snapshots ?? Array.Empty<AkronGameplayBufferSnapshot>()).ToDictionary(
                    snapshot => snapshot.FieldName,
                    StringComparer.Ordinal);
            List<FieldInfo> fields = GetBufferFields();
            if (savedByName.Count != fields.Count) {
                throw new InvalidOperationException("Gameplay buffer set differs.");
            }

            foreach (FieldInfo field in fields) {
                if (!savedByName.TryGetValue(field.Name, out AkronGameplayBufferSnapshot snapshot)) {
                    throw new InvalidOperationException("Gameplay buffer is missing: " + field.Name);
                }
                if (field.GetValue(null) is not VirtualRenderTarget renderTarget) {
                    throw new InvalidOperationException("Gameplay buffer is unavailable: " + field.Name);
                }
                Adapter.RestoreExisting(snapshot.Payload, renderTarget);
            }

            foreach (FieldInfo field in fields) {
                AkronGameplayBufferSnapshot snapshot = savedByName[field.Name];
                if (!Adapter.Verify(snapshot.Payload, field.GetValue(null))) {
                    throw new InvalidOperationException("Gameplay buffer pixels differ: " + field.Name);
                }
            }
            return true;
        } catch (Exception exception) {
            error = exception.GetType().Name + ": " + exception.Message;
            return false;
        }
    }

    public static void ArmLevelPresentation(Level level, IReadOnlyList<AkronGameplayBufferSnapshot> snapshots) {
        AkronGameplayBufferSnapshot levelSnapshot = snapshots?.FirstOrDefault(snapshot =>
            string.Equals(snapshot.FieldName, nameof(GameplayBuffers.Level), StringComparison.Ordinal));
        armedPresentationLevel = level;
        armedLevelPresentation = levelSnapshot?.Payload?.Bytes == null
            ? null
            : (byte[]) levelSnapshot.Payload.Bytes.Clone();
    }

    public static void ResetLevelPresentation() {
        armedLevelPresentation = null;
        armedPresentationLevel = null;
    }

    public static void PresentArmedLevelBuffer(Level level) {
        if (armedLevelPresentation == null) {
            return;
        }

        byte[] pixels = armedLevelPresentation;
        Level expectedLevel = armedPresentationLevel;
        ResetLevelPresentation();
        if (!ReferenceEquals(level, expectedLevel) || GameplayBuffers.Level?.Target == null) {
            return;
        }

        try {
            GameplayBuffers.Level.Target.SetData(pixels);
        } catch (Exception exception) {
            Logger.Log(LogLevel.Warn, nameof(AkronGameplayBufferState),
                "Could not present the restored StartPos frame: " + exception.Message);
        }
    }

    private static List<FieldInfo> GetBufferFields() {
        return typeof(GameplayBuffers)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(VirtualRenderTarget))
            .OrderBy(field => field.MetadataToken)
            .ToList();
    }
}

internal sealed class AkronReconstructionPathStep {
    public string Kind { get; set; } = string.Empty;
    public string DeclaringTypeName { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public List<int> ArrayIndices { get; set; } = new List<int>();
}

internal sealed class AkronReconstructionDelegateCall {
    public string Kind { get; set; } = "method";
    public AkronReconstructionValue Target { get; set; } = new AkronReconstructionValue();
    public string DeclaringTypeName { get; set; } = string.Empty;
    public string MethodName { get; set; } = string.Empty;
    public string ReturnTypeName { get; set; } = string.Empty;
    public List<string> ParameterTypeNames { get; set; } = new List<string>();
    public string HookTargetDeclaringTypeName { get; set; } = string.Empty;
    public string HookTargetMethodName { get; set; } = string.Empty;
    public string HookTargetReturnTypeName { get; set; } = string.Empty;
    public List<string> HookTargetParameterTypeNames { get; set; } = new List<string>();
}

internal sealed class AkronReconstructionCapture {
    private AkronReconstructionCapture(bool success, AkronReconstructionDocument document, string errorPath, string error) {
        Success = success;
        Document = document;
        ErrorPath = errorPath ?? string.Empty;
        Error = error ?? string.Empty;
    }

    public bool Success { get; }
    public AkronReconstructionDocument Document { get; }
    public string ErrorPath { get; }
    public string Error { get; }

    public static AkronReconstructionCapture Succeeded(AkronReconstructionDocument document) {
        return new AkronReconstructionCapture(true, document, string.Empty, string.Empty);
    }

    public static AkronReconstructionCapture Failed(string path, string error) {
        return new AkronReconstructionCapture(false, null, path, FormatError(path, error));
    }

    private static string FormatError(string path, string error) {
        return (string.IsNullOrWhiteSpace(path) ? "$" : path) + ": " + error;
    }
}

internal sealed class AkronReconstructionRestore {
    private AkronReconstructionRestore(bool success, string errorPath, string error, Dictionary<int, object> objects) {
        Success = success;
        ErrorPath = errorPath ?? string.Empty;
        Error = error ?? string.Empty;
        Objects = objects;
    }

    public bool Success { get; }
    public string ErrorPath { get; }
    public string Error { get; }
    internal Dictionary<int, object> Objects { get; }

    public static AkronReconstructionRestore Succeeded(Dictionary<int, object> objects) {
        return new AkronReconstructionRestore(true, string.Empty, string.Empty, objects);
    }

    public static AkronReconstructionRestore Failed(string path, string error) {
        string normalizedPath = string.IsNullOrWhiteSpace(path) ? "$" : path;
        return new AkronReconstructionRestore(false, normalizedPath, normalizedPath + ": " + error, null);
    }
}

internal sealed class AkronReconstructionVerification {
    private AkronReconstructionVerification(bool success, string errorPath, string error) {
        Success = success;
        ErrorPath = errorPath ?? string.Empty;
        Error = error ?? string.Empty;
    }

    public bool Success { get; }
    public string ErrorPath { get; }
    public string Error { get; }

    public static AkronReconstructionVerification Succeeded() {
        return new AkronReconstructionVerification(true, string.Empty, string.Empty);
    }

    public static AkronReconstructionVerification Failed(string path, string error) {
        string normalizedPath = string.IsNullOrWhiteSpace(path) ? "$" : path;
        return new AkronReconstructionVerification(false, normalizedPath, normalizedPath + ": " + error);
    }
}

// Json.NET builds lists and objects before the document-level checks can run.
// Count the stream as Json.NET reads it so hostile pack data cannot create an
// unbounded object graph or scalar before those checks get control.
internal sealed class AkronBoundedJsonTextReader : JsonTextReader {
    private readonly long maxTokenCount;
    private readonly long maxContainerCount;
    private readonly long maxNodeCount;
    private readonly long maxRecordCount;
    private readonly long maxExpensiveRecordCount;
    private readonly int maxStringChars;
    private readonly long maxBinaryBytes;
    private long tokenCount;
    private long containerCount;
    private long nodeCount;
    private long recordCount;
    private long expensiveRecordCount;
    private long binaryBytes;

    public AkronBoundedJsonTextReader(
        TextReader reader,
        long maxTokenCount,
        long maxContainerCount,
        int maxStringChars,
        long maxBinaryBytes,
        long maxNodeCount,
        long maxRecordCount,
        long maxExpensiveRecordCount
    ) : base(reader) {
        this.maxTokenCount = maxTokenCount > 0 ? maxTokenCount : throw new ArgumentOutOfRangeException(nameof(maxTokenCount));
        this.maxContainerCount = maxContainerCount > 0 ? maxContainerCount : throw new ArgumentOutOfRangeException(nameof(maxContainerCount));
        this.maxNodeCount = maxNodeCount > 0 ? maxNodeCount : throw new ArgumentOutOfRangeException(nameof(maxNodeCount));
        this.maxRecordCount = maxRecordCount > 0 ? maxRecordCount : throw new ArgumentOutOfRangeException(nameof(maxRecordCount));
        this.maxExpensiveRecordCount = maxExpensiveRecordCount > 0
            ? maxExpensiveRecordCount
            : throw new ArgumentOutOfRangeException(nameof(maxExpensiveRecordCount));
        this.maxStringChars = maxStringChars > 0 ? maxStringChars : throw new ArgumentOutOfRangeException(nameof(maxStringChars));
        this.maxBinaryBytes = maxBinaryBytes > 0 ? maxBinaryBytes : throw new ArgumentOutOfRangeException(nameof(maxBinaryBytes));
    }

    public override bool Read() {
        bool read = base.Read();
        if (read) {
            ValidateCurrentToken();
        }
        return read;
    }

    public override int? ReadAsInt32() {
        int? value = base.ReadAsInt32();
        ValidateCurrentToken();
        return value;
    }

    public override DateTime? ReadAsDateTime() {
        DateTime? value = base.ReadAsDateTime();
        ValidateCurrentToken();
        return value;
    }

    public override string ReadAsString() {
        string value = base.ReadAsString();
        ValidateCurrentToken();
        return value;
    }

    public override byte[] ReadAsBytes() {
        byte[] value = base.ReadAsBytes();
        ValidateCurrentToken();
        return value;
    }

    public override bool? ReadAsBoolean() {
        bool? value = base.ReadAsBoolean();
        ValidateCurrentToken();
        return value;
    }

    public override DateTimeOffset? ReadAsDateTimeOffset() {
        DateTimeOffset? value = base.ReadAsDateTimeOffset();
        ValidateCurrentToken();
        return value;
    }

    public override decimal? ReadAsDecimal() {
        decimal? value = base.ReadAsDecimal();
        ValidateCurrentToken();
        return value;
    }

    public override double? ReadAsDouble() {
        double? value = base.ReadAsDouble();
        ValidateCurrentToken();
        return value;
    }

    private void ValidateCurrentToken() {
        if (TokenType == JsonToken.None) {
            return;
        }
        tokenCount++;
        if (tokenCount > maxTokenCount) {
            throw new InvalidOperationException("Reconstruction JSON token count exceeds the supported limit.");
        }
        if (TokenType is JsonToken.StartObject or JsonToken.StartArray or JsonToken.StartConstructor) {
            containerCount++;
            if (containerCount > maxContainerCount) {
                throw new InvalidOperationException("Reconstruction JSON container count exceeds the supported limit.");
            }
        }
        if (TokenType == JsonToken.StartObject) {
            recordCount++;
            if (recordCount > maxRecordCount) {
                throw new InvalidOperationException("Reconstruction JSON record count exceeds the supported limit.");
            }
            if (IsExpensiveRecordPath(Path)) {
                expensiveRecordCount++;
                if (expensiveRecordCount > maxExpensiveRecordCount) {
                    throw new InvalidOperationException("Reconstruction JSON complex record count exceeds the supported limit.");
                }
            }
        }
        if (TokenType == JsonToken.StartObject && IsNodeObjectPath(Path)) {
            nodeCount++;
            if (nodeCount > maxNodeCount) {
                throw new InvalidOperationException("Reconstruction JSON node count exceeds the supported limit.");
            }
        }
        if (Value is string text && text.Length > maxStringChars) {
            throw new InvalidOperationException("Reconstruction JSON string length exceeds the supported limit.");
        }
        if (Value is byte[] bytes) {
            binaryBytes = checked(binaryBytes + bytes.LongLength);
            if (binaryBytes > maxBinaryBytes) {
                throw new InvalidOperationException("Reconstruction JSON binary data exceeds the supported limit.");
            }
        }
    }

    private static bool IsNodeObjectPath(string path) {
        return IsArrayElementObjectPath(path, "Nodes");
    }

    private static bool IsExpensiveRecordPath(string path) {
        return IsArrayElementObjectPath(path, "Fields") ||
               IsArrayElementObjectPath(path, "DelegateCalls") ||
               IsArrayElementObjectPath(path, "FreshPath");
    }

    private static bool IsArrayElementObjectPath(string path, string propertyName) {
        int bracket = path?.LastIndexOf('[') ?? -1;
        if (bracket <= 0 || path[path.Length - 1] != ']' ||
            !path.Substring(0, bracket).EndsWith(propertyName, StringComparison.Ordinal)) {
            return false;
        }
        return path.Substring(bracket + 1, path.Length - bracket - 2)
            .All(character => character is >= '0' and <= '9');
    }
}

// The graph pairs saved managed objects with objects from a normal fresh-room
// load. Static process-owned resources use a stable key and the structural path
// at which Celeste created their fresh replacement. Dynamic room-owned
// resources use an explicit adapter because a fresh room may not create them.
// Restore has a validation phase so a missing field, type, or resource cannot
// leave a partly changed room behind.
internal sealed class AkronReconstructionGraph {
    // Diagnostic paths come from untrusted pack data. Bound and rebuild their
    // parent chains without recursion so a crafted graph cannot exhaust the
    // process stack or create quadratic path data at arbitrary depth.
    private const int MaxParentChainDepth = 256;
    private const int MaxDiagnosticPathChars = 16 * 1024;
    private const long MaxTotalDiagnosticPathChars = 64L * 1024L * 1024L;
    private const int MaxRestoredArrayRank = 32;
    private const long MaxRestoredArrayBytes = 128L * 1024L * 1024L;
    private const long DefaultMaxJsonTokenCount = 12_000_000;
    private const long DefaultMaxJsonContainerCount = 5_000_000;
    private const long DefaultMaxJsonNodeCount = 100_000;
    private const long DefaultMaxJsonRecordCount = 3_000_000;
    private const long DefaultMaxJsonExpensiveRecordCount = 250_000;
    private const int DefaultMaxJsonStringChars = 16 * 1024 * 1024;
    private const long DefaultMaxJsonBinaryBytes = 192L * 1024L * 1024L;
    private const string ObjectKind = "object";
    private const string ArrayKind = "array";
    private const string AnchorKind = "anchor";
    private const string DelegateKind = "delegate";
    private const string EventInstanceKind = "event-instance";
    private const string PersistentResourceKind = "persistent-resource";
    private const string NullValueKind = "null";
    private const string ScalarValueKind = "scalar";
    private const string ReferenceValueKind = "reference";
    private const string MethodDelegateCallKind = "method";
    private const string DetourNextDelegateCallKind = "detour-next";

    private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings {
        Formatting = Formatting.None,
        MissingMemberHandling = MissingMemberHandling.Error,
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        TypeNameHandling = TypeNameHandling.None
    };
    private static readonly ConcurrentDictionary<Type, string> TypeNames = new ConcurrentDictionary<Type, string>();
    private static readonly ConcurrentDictionary<string, Type> ResolvedTypes = new ConcurrentDictionary<string, Type>(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<(string DeclaringTypeName, string FieldName), FieldInfo> ResolvedFields =
        new ConcurrentDictionary<(string DeclaringTypeName, string FieldName), FieldInfo>();
    private static readonly ConcurrentDictionary<Type, FieldInfo[]> InstanceFields = new ConcurrentDictionary<Type, FieldInfo[]>();
    private static readonly ConcurrentDictionary<Type, bool> InertBuiltInEntityMarkerTypes =
        new ConcurrentDictionary<Type, bool>();
    private static readonly ConcurrentDictionary<Type, bool> PassiveDataObjectTypes =
        new ConcurrentDictionary<Type, bool>();
    private const BindingFlags RuntimeInstanceFields =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo EntitySourceIdField =
        typeof(Entity).GetField("<SourceId>k__BackingField", RuntimeInstanceFields);
    private static readonly FieldInfo EntityComponentsField =
        typeof(Entity).GetField("<Components>k__BackingField", RuntimeInstanceFields);
    private static readonly FieldInfo ComponentEntityField =
        typeof(Component).GetField("<Entity>k__BackingField", RuntimeInstanceFields);
    private static readonly FieldInfo SceneEntitiesField =
        typeof(Scene).GetField("<Entities>k__BackingField", RuntimeInstanceFields);
    private static readonly FieldInfo EntityListEntitiesField =
        typeof(EntityList).GetField("entities", RuntimeInstanceFields);
    private static readonly FieldInfo ComponentListComponentsField =
        typeof(ComponentList).GetField("components", RuntimeInstanceFields);

    private readonly Func<Type, bool> isLiveResource;
    private readonly Func<object, string> getLiveResourceKey;
    private readonly IAkronReconstructionResourceAdapter resourceAdapter;
    private readonly Func<Type, string, object> resolveDetachedLiveResource;
    private readonly long maxJsonTokenCount;
    private readonly long maxJsonContainerCount;
    private readonly int maxJsonStringChars;
    private readonly long maxJsonBinaryBytes;
    private readonly long maxJsonNodeCount;
    private readonly long maxJsonRecordCount;
    private readonly long maxJsonExpensiveRecordCount;
    private readonly HashSet<object> ownedPersistentResources =
        new HashSet<object>(ReferenceEqualityComparer.Instance);

    private static void ValidateAndNormalizeDerivedMembershipSets(IEnumerable<object> values) {
        foreach (object value in values.Where(value => value != null).Distinct(ReferenceEqualityComparer.Instance)) {
            if (value is EntityList entityList) {
                ValidateAndNormalizeMembershipSet<Entity>(entityList, "entities", "current");
                ValidateAndNormalizeMembershipSet<Entity>(entityList, "toAdd", "adding");
                ValidateAndNormalizeMembershipSet<Entity>(entityList, "toRemove", "removing");
            } else if (value is ComponentList componentList) {
                ValidateAndNormalizeMembershipSet<Component>(componentList, "components", "current");
                ValidateAndNormalizeMembershipSet<Component>(componentList, "toAdd", "adding");
                ValidateAndNormalizeMembershipSet<Component>(componentList, "toRemove", "removing");
            }
        }
    }

    private static void ValidateAndNormalizeMembershipSet<T>(
        object owner,
        string orderedFieldName,
        string membershipFieldName
    ) where T : class {
        BindingFlags flags = RuntimeInstanceFields;
        Type ownerType = owner.GetType();
        IEnumerable<T> ordered = ownerType.GetField(orderedFieldName, flags)?.GetValue(owner) as IEnumerable<T>
                                 ?? Array.Empty<T>();
        HashSet<T> membership = ownerType.GetField(membershipFieldName, flags)?.GetValue(owner) as HashSet<T>
                                ?? throw new InvalidOperationException(
                                    ownerType.FullName + "." + membershipFieldName + " is unavailable");
        HashSet<object> orderedReferences = new HashSet<object>(
            ordered.Where(item => item != null).Cast<object>(),
            ReferenceEqualityComparer.Instance);
        HashSet<object> membershipReferences = new HashSet<object>(
            membership.Where(item => item != null).Cast<object>(),
            ReferenceEqualityComparer.Instance);
        if (!orderedReferences.SetEquals(membershipReferences)) {
            throw new InvalidOperationException(
                ownerType.FullName + "." + membershipFieldName + " differs from " + orderedFieldName);
        }

        // Object hash codes belong to this process, not the saved process.
        // Re-add the exact saved references so HashSet rebuilds only its
        // derived buckets and keeps the saved logical membership unchanged.
        membership.Clear();
        foreach (T item in ordered.Where(item => item != null)) {
            membership.Add(item);
        }
    }

    public AkronReconstructionGraph(
        Func<Type, bool> isLiveResource,
        Func<object, string> getLiveResourceKey = null,
        IAkronReconstructionResourceAdapter resourceAdapter = null,
        Func<Type, string, object> resolveDetachedLiveResource = null,
        long maxJsonTokenCount = DefaultMaxJsonTokenCount,
        long maxJsonContainerCount = DefaultMaxJsonContainerCount,
        int maxJsonStringChars = DefaultMaxJsonStringChars,
        long maxJsonBinaryBytes = DefaultMaxJsonBinaryBytes,
        long maxJsonNodeCount = DefaultMaxJsonNodeCount,
        long maxJsonRecordCount = DefaultMaxJsonRecordCount,
        long maxJsonExpensiveRecordCount = DefaultMaxJsonExpensiveRecordCount
    ) {
        this.isLiveResource = isLiveResource ?? throw new ArgumentNullException(nameof(isLiveResource));
        this.getLiveResourceKey = getLiveResourceKey;
        this.resourceAdapter = resourceAdapter;
        this.resolveDetachedLiveResource = resolveDetachedLiveResource;
        this.maxJsonTokenCount = maxJsonTokenCount;
        this.maxJsonContainerCount = maxJsonContainerCount;
        this.maxJsonStringChars = maxJsonStringChars;
        this.maxJsonBinaryBytes = maxJsonBinaryBytes;
        this.maxJsonNodeCount = maxJsonNodeCount;
        this.maxJsonRecordCount = maxJsonRecordCount;
        this.maxJsonExpensiveRecordCount = maxJsonExpensiveRecordCount;
    }

    private string GetTypedResourceKey(object resource) {
        if (resource == null) {
            return string.Empty;
        }
        string key = getLiveResourceKey?.Invoke(resource);
        return string.IsNullOrWhiteSpace(key)
            ? string.Empty
            : TypeName(resource.GetType()) + "|" + key;
    }

    public AkronReconstructionCapture Capture(object savedRoot, object freshBaselineRoot) {
        if (savedRoot == null || freshBaselineRoot == null) {
            return AkronReconstructionCapture.Failed("$", "saved and fresh roots are required");
        }
        if (savedRoot.GetType() != freshBaselineRoot.GetType()) {
            return AkronReconstructionCapture.Failed("$", "saved and fresh root types differ");
        }

        CaptureContext context = new CaptureContext(this, freshBaselineRoot);
        try {
            AkronReconstructionValue root = context.CaptureValue(
                savedRoot,
                freshBaselineRoot,
                "$",
                new List<AkronReconstructionPathStep>());
            if (root.Kind != ReferenceValueKind) {
                return AkronReconstructionCapture.Failed("$", "root must be a reference node");
            }

            context.Document.RootNodeId = root.NodeId;
            return AkronReconstructionCapture.Succeeded(context.Document);
        } catch (AkronReconstructionException exception) {
            return AkronReconstructionCapture.Failed(exception.Path, exception.Message);
        } catch (Exception exception) {
            return AkronReconstructionCapture.Failed("$", exception.GetType().Name + ": " + exception.Message);
        }
    }

    public string Serialize(AkronReconstructionDocument document) {
        ValidateDocumentHeader(document);
        return JsonConvert.SerializeObject(document, JsonSettings);
    }

    public AkronReconstructionDocument Deserialize(string json) {
        if (string.IsNullOrWhiteSpace(json)) {
            throw new InvalidOperationException("Reconstruction document is empty.");
        }

        JsonSerializer serializer = JsonSerializer.Create(JsonSettings);
        using StringReader stringReader = new StringReader(json);
        using AkronBoundedJsonTextReader jsonReader = CreateJsonReader(stringReader);
        AkronReconstructionDocument document = serializer.Deserialize<AkronReconstructionDocument>(jsonReader);
        ValidateDocumentHeader(document);
        RestoreDiagnosticPaths(document);
        return document;
    }

    public void Serialize(AkronReconstructionDocument document, Stream stream) {
        ValidateDocumentHeader(document);
        if (stream == null || !stream.CanWrite) {
            throw new InvalidOperationException("Reconstruction output stream is unavailable.");
        }

        JsonSerializer serializer = JsonSerializer.Create(JsonSettings);
        using StreamWriter streamWriter = new StreamWriter(stream, new UTF8Encoding(false), 65536, leaveOpen: true);
        using JsonTextWriter jsonWriter = new JsonTextWriter(streamWriter) { CloseOutput = false };
        serializer.Serialize(jsonWriter, document);
        jsonWriter.Flush();
    }

    public AkronReconstructionDocument Deserialize(Stream stream) {
        if (stream == null || !stream.CanRead) {
            throw new InvalidOperationException("Reconstruction input stream is unavailable.");
        }

        JsonSerializer serializer = JsonSerializer.Create(JsonSettings);
        using StreamReader streamReader = new StreamReader(stream, Encoding.UTF8, true, 65536, leaveOpen: true);
        using AkronBoundedJsonTextReader jsonReader = CreateJsonReader(streamReader);
        AkronReconstructionDocument document = serializer.Deserialize<AkronReconstructionDocument>(jsonReader);
        ValidateDocumentHeader(document);
        RestoreDiagnosticPaths(document);
        return document;
    }

    private AkronBoundedJsonTextReader CreateJsonReader(TextReader reader) {
        return new AkronBoundedJsonTextReader(
            reader,
            maxJsonTokenCount,
            maxJsonContainerCount,
            maxJsonStringChars,
            maxJsonBinaryBytes,
            maxJsonNodeCount,
            maxJsonRecordCount,
            maxJsonExpensiveRecordCount) {
            CloseInput = false
        };
    }

    public AkronReconstructionRestore Restore(AkronReconstructionDocument document, object freshRoot) {
        if (freshRoot == null) {
            return AkronReconstructionRestore.Failed("$", "fresh root is required");
        }

        RestoreContext context = null;
        try {
            ValidateDocumentHeader(document);
            context = new RestoreContext(this, document, freshRoot);
            context.ResolveObjects();
            context.ValidateAssignments();
            context.ApplyAssignments();
            context.ReleaseDisplacedEventInstances();
            context.CommitPersistentResources();
            return AkronReconstructionRestore.Succeeded(context.Objects);
        } catch (AkronReconstructionException exception) {
            context?.ReleaseCreatedPersistentResources();
            AkronEventInstanceUtils.ReleaseDormantEventInstances(context?.Objects.Values.OfType<EventInstance>());
            return AkronReconstructionRestore.Failed(exception.Path, exception.Message);
        } catch (Exception exception) {
            context?.ReleaseCreatedPersistentResources();
            AkronEventInstanceUtils.ReleaseDormantEventInstances(context?.Objects.Values.OfType<EventInstance>());
            return AkronReconstructionRestore.Failed("$", exception.GetType().Name + ": " + exception.Message);
        }
    }

    public void ReleaseOwnedPersistentResources() {
        foreach (object resource in ownedPersistentResources.ToArray()) {
            ReleaseOwnedPersistentResource(resource);
        }
    }

    private void ReleaseOwnedPersistentResource(object resource) {
        if (!ownedPersistentResources.Remove(resource)) {
            return;
        }
        if (resource is IDisposable disposable) {
            disposable.Dispose();
        }
    }

    public AkronReconstructionVerification Reapply(
        AkronReconstructionDocument document,
        AkronReconstructionRestore restore
    ) {
        if (restore == null || !restore.Success || restore.Objects == null) {
            return AkronReconstructionVerification.Failed("$", "a successful restore is required");
        }

        try {
            ValidateDocumentHeader(document);
            RestoreContext context = new RestoreContext(this, document, restore.Objects);
            context.ValidateAssignments();
            context.ApplyAssignments();
            context.ReleaseDisplacedEventInstances();
            return AkronReconstructionVerification.Succeeded();
        } catch (AkronReconstructionException exception) {
            return AkronReconstructionVerification.Failed(exception.Path, exception.Message);
        } catch (Exception exception) {
            return AkronReconstructionVerification.Failed("$", exception.GetType().Name + ": " + exception.Message);
        }
    }

    public AkronReconstructionVerification Verify(
        AkronReconstructionDocument document,
        AkronReconstructionRestore restore,
        IEnumerable<string> maskedPaths
    ) {
        if (restore == null || !restore.Success || restore.Objects == null) {
            return AkronReconstructionVerification.Failed("$", "a successful restore is required");
        }

        try {
            ValidateDocumentHeader(document);
            HashSet<string> masks = new HashSet<string>(maskedPaths ?? Array.Empty<string>(), StringComparer.Ordinal);
            VerificationContext context = new VerificationContext(this, document, restore.Objects, masks);
            context.Verify();
            return AkronReconstructionVerification.Succeeded();
        } catch (AkronReconstructionException exception) {
            return AkronReconstructionVerification.Failed(exception.Path, exception.Message);
        } catch (Exception exception) {
            return AkronReconstructionVerification.Failed("$", exception.GetType().Name + ": " + exception.Message);
        }
    }

    private void ValidateDocumentHeader(AkronReconstructionDocument document) {
        if (document == null || !string.Equals(document.Format, AkronReconstructionDocument.CurrentFormat, StringComparison.Ordinal)) {
            throw new InvalidOperationException("Reconstruction document format is unsupported.");
        }
        if (document.RootNodeId <= 0 || document.Nodes == null || document.Nodes.Count == 0) {
            throw new InvalidOperationException("Reconstruction document has no root node.");
        }
        if (document.Nodes.Any(node => node == null || node.Id <= 0) ||
            document.Nodes.Select(node => node.Id).Distinct().Count() != document.Nodes.Count) {
            throw new InvalidOperationException("Reconstruction document has invalid node IDs.");
        }
        if (document.Nodes.All(node => node.Id != document.RootNodeId)) {
            throw new InvalidOperationException("Reconstruction document root node is missing.");
        }
        ValidateNodeKindContracts(document);
        ValidateNodeParentEdges(document);
        ValidateNodeReachability(document);
        if (document.ActionStateDocument != null) {
            ValidateDocumentHeader(document.ActionStateDocument);
        }
    }

    private void ValidateNodeKindContracts(AkronReconstructionDocument document) {
        foreach (AkronReconstructionNode node in document.Nodes) {
            Type type = ResolveType(node.TypeName, "$");
            bool valid = node.Kind switch {
                ObjectKind => true,
                ArrayKind => type.IsArray,
                DelegateKind => typeof(Delegate).IsAssignableFrom(type),
                EventInstanceKind => type == typeof(EventInstance) && node.EventInstance != null,
                PersistentResourceKind => resourceAdapter?.CanPersist(type) == true && node.ResourcePayload != null,
                AnchorKind => node.UseFreshObject &&
                              (isLiveResource(type) || typeof(Delegate).IsAssignableFrom(type)),
                _ => false
            };
            if (!valid) {
                string kind = string.IsNullOrWhiteSpace(node.Kind) ? "empty" : node.Kind;
                throw new InvalidOperationException(
                    "Reconstruction " + kind + " type is invalid: " + (type.FullName ?? type.Name));
            }
        }
    }

    private static void ValidateNodeParentEdges(AkronReconstructionDocument document) {
        Dictionary<int, AkronReconstructionNode> nodes = document.Nodes.ToDictionary(node => node.Id);
        // Index each field once. Looking through every parent field for every
        // child makes a wide crafted snapshot quadratic to validate.
        Dictionary<(int ParentNodeId, string DeclaringTypeName, string FieldName), AkronReconstructionValue>
            parentFieldValues = new Dictionary<(int, string, string), AkronReconstructionValue>();
        foreach (AkronReconstructionNode parent in document.Nodes) {
            foreach (AkronReconstructionField field in parent.Fields ?? new List<AkronReconstructionField>()) {
                if (field == null) {
                    continue;
                }
                if (!parentFieldValues.TryAdd(
                        (parent.Id, field.DeclaringTypeName, field.Name),
                        field.Value)) {
                    throw new InvalidOperationException(
                        "Reconstruction document parent field identity is duplicated.");
                }
            }
        }

        foreach (AkronReconstructionNode node in document.Nodes) {
            if (node.Id == document.RootNodeId) {
                if (node.ParentNodeId != 0 || !string.IsNullOrEmpty(node.ParentKind)) {
                    throw new InvalidOperationException("Reconstruction document root parent edge is invalid.");
                }
                continue;
            }
            if (!nodes.TryGetValue(node.ParentNodeId, out AkronReconstructionNode parent)) {
                throw new InvalidOperationException("Reconstruction document node parent is missing.");
            }

            AkronReconstructionValue parentValue = null;
            if (node.ParentKind == "field") {
                parentFieldValues.TryGetValue(
                    (parent.Id, node.ParentDeclaringTypeName, node.ParentFieldName),
                    out parentValue);
            } else if (node.ParentKind == "array" &&
                       TryGetFlatArrayIndex(parent, node.ParentArrayIndices, out int itemIndex) &&
                       parent.Items != null && itemIndex < parent.Items.Count) {
                parentValue = parent.Items[itemIndex];
            } else if (node.ParentKind == "delegate" &&
                       node.ParentDelegateIndex >= 0 &&
                       parent.DelegateCalls != null &&
                       node.ParentDelegateIndex < parent.DelegateCalls.Count) {
                parentValue = parent.DelegateCalls[node.ParentDelegateIndex]?.Target;
            }
            if (parentValue?.Kind != ReferenceValueKind || parentValue.NodeId != node.Id) {
                throw new InvalidOperationException("Reconstruction document node parent edge is invalid.");
            }
        }
    }

    private static bool TryGetFlatArrayIndex(
        AkronReconstructionNode arrayNode,
        IReadOnlyList<int> indices,
        out int flatIndex
    ) {
        flatIndex = 0;
        if (arrayNode.ArrayLengths == null || arrayNode.ArrayLowerBounds == null || indices == null ||
            arrayNode.ArrayLengths.Count == 0 ||
            arrayNode.ArrayLengths.Count != arrayNode.ArrayLowerBounds.Count ||
            arrayNode.ArrayLengths.Count != indices.Count) {
            return false;
        }
        long offset = 0;
        for (int dimension = 0; dimension < indices.Count; dimension++) {
            int length = arrayNode.ArrayLengths[dimension];
            int lowerBound = arrayNode.ArrayLowerBounds[dimension];
            long relativeIndex = (long) indices[dimension] - lowerBound;
            if (length < 0 || relativeIndex < 0 || relativeIndex >= length) {
                return false;
            }
            offset = checked(offset * length + relativeIndex);
            if (offset > int.MaxValue) {
                return false;
            }
        }
        flatIndex = (int) offset;
        return true;
    }

    private static void ValidateNodeReachability(AkronReconstructionDocument document) {
        Dictionary<int, AkronReconstructionNode> nodes = document.Nodes.ToDictionary(node => node.Id);
        HashSet<int> reached = new HashSet<int>();
        Stack<int> pending = new Stack<int>();
        pending.Push(document.RootNodeId);
        while (pending.Count > 0) {
            int nodeId = pending.Pop();
            if (!reached.Add(nodeId)) {
                continue;
            }
            AkronReconstructionNode node = nodes[nodeId];
            IEnumerable<AkronReconstructionValue> references =
                (node.Fields ?? new List<AkronReconstructionField>()).Select(field => field?.Value)
                .Concat(node.Items ?? new List<AkronReconstructionValue>())
                .Concat((node.DelegateCalls ?? new List<AkronReconstructionDelegateCall>()).Select(call => call?.Target));
            foreach (AkronReconstructionValue reference in references.Where(value => value?.Kind == ReferenceValueKind)) {
                if (!nodes.ContainsKey(reference.NodeId)) {
                    throw new InvalidOperationException("Reconstruction document contains an invalid node reference.");
                }
                pending.Push(reference.NodeId);
            }
        }
        if (reached.Count != nodes.Count) {
            throw new InvalidOperationException("Reconstruction document contains nodes that are not reachable from its root.");
        }
    }

    private static void RestoreDiagnosticPaths(AkronReconstructionDocument document) {
        long totalPathChars = 0;
        RestoreDiagnosticPaths(document, ref totalPathChars);
    }

    private static void RestoreDiagnosticPaths(
        AkronReconstructionDocument document,
        ref long totalPathChars
    ) {
        Dictionary<int, AkronReconstructionNode> nodes = document.Nodes.ToDictionary(node => node.Id);
        foreach (AkronReconstructionNode node in document.Nodes) {
            RestoreNodePath(node, document.RootNodeId, nodes, ref totalPathChars);
        }
        foreach (AkronReconstructionNode node in document.Nodes) {
            foreach (AkronReconstructionField field in node.Fields ?? new List<AkronReconstructionField>()) {
                field.Path = BuildDiagnosticPath(node.Path, "." + (field.Name ?? string.Empty));
                AddDiagnosticPathChars(field.Path, ref totalPathChars);
            }
        }
        if (document.ActionStateDocument != null) {
            RestoreDiagnosticPaths(document.ActionStateDocument, ref totalPathChars);
        }
    }

    private static string RestoreNodePath(
        AkronReconstructionNode node,
        int rootNodeId,
        IReadOnlyDictionary<int, AkronReconstructionNode> nodes,
        ref long totalPathChars
    ) {
        if (!string.IsNullOrWhiteSpace(node.Path)) {
            return node.Path;
        }
        List<AkronReconstructionNode> unresolved = new List<AkronReconstructionNode>();
        HashSet<int> resolving = new HashSet<int>();
        AkronReconstructionNode current = node;
        while (string.IsNullOrWhiteSpace(current.Path)) {
            if (current.Id == rootNodeId) {
                current.Path = "$";
                AddDiagnosticPathChars(current.Path, ref totalPathChars);
                break;
            }
            if (!resolving.Add(current.Id)) {
                throw new InvalidOperationException("Reconstruction node parent cycle is invalid.");
            }
            if (unresolved.Count >= MaxParentChainDepth) {
                throw new InvalidOperationException("Reconstruction node parent depth exceeds the supported limit.");
            }
            unresolved.Add(current);
            if (current.ParentNodeId <= 0 || !nodes.TryGetValue(current.ParentNodeId, out current)) {
                throw new InvalidOperationException("Reconstruction node parent is missing.");
            }
        }

        string parentPath = current.Path;
        for (int index = unresolved.Count - 1; index >= 0; index--) {
            AkronReconstructionNode child = unresolved[index];
            switch (child.ParentKind) {
                case "field":
                    child.Path = BuildDiagnosticPath(parentPath, "." + (child.ParentFieldName ?? string.Empty));
                    break;
                case "array":
                    child.Path = BuildDiagnosticPath(
                        parentPath,
                        "[" + string.Join(",", child.ParentArrayIndices ?? new List<int>()) + "]");
                    break;
                case "delegate":
                    child.Path = BuildDiagnosticPath(
                        parentPath,
                        ".<target>[" + child.ParentDelegateIndex.ToString(CultureInfo.InvariantCulture) + "]");
                    break;
                default:
                    throw new InvalidOperationException("Reconstruction node parent kind is invalid.");
            }
            AddDiagnosticPathChars(child.Path, ref totalPathChars);
            parentPath = child.Path;
        }
        return node.Path;
    }

    private static string BuildDiagnosticPath(string parentPath, string suffix) {
        parentPath ??= string.Empty;
        suffix ??= string.Empty;
        if (parentPath.Length > MaxDiagnosticPathChars - suffix.Length) {
            throw new InvalidOperationException("Reconstruction diagnostic path exceeds the supported limit.");
        }
        return parentPath + suffix;
    }

    private static void AddDiagnosticPathChars(string path, ref long totalPathChars) {
        int pathChars = path?.Length ?? 0;
        if (pathChars > MaxDiagnosticPathChars || totalPathChars > MaxTotalDiagnosticPathChars - pathChars) {
            throw new InvalidOperationException("Reconstruction diagnostic path exceeds the supported limit.");
        }
        totalPathChars += pathChars;
    }

    private static IEnumerable<FieldInfo> GetInstanceFields(Type type) {
        return InstanceFields.GetOrAdd(type, BuildInstanceFields);
    }

    private static FieldInfo[] BuildInstanceFields(Type type) {
        Stack<Type> hierarchy = new Stack<Type>();
        for (Type current = type; current != null; current = current.BaseType) {
            hierarchy.Push(current);
        }

        List<FieldInfo> fields = new List<FieldInfo>();
        while (hierarchy.Count > 0) {
            Type current = hierarchy.Pop();
            fields.AddRange(current
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(field => !field.IsStatic)
                .OrderBy(field => field.MetadataToken));
        }
        return fields.ToArray();
    }

    private static string TypeName(Type type) {
        return type == null
            ? string.Empty
            : TypeNames.GetOrAdd(type, value => value.AssemblyQualifiedName ?? string.Empty);
    }

    private static Type ResolveType(string typeName, string path) {
        if (string.IsNullOrWhiteSpace(typeName)) {
            throw new AkronReconstructionException(path, "type is unavailable: " + typeName);
        }
        if (ResolvedTypes.TryGetValue(typeName, out Type cachedType)) {
            return cachedType;
        }
        Type type = Type.GetType(typeName, throwOnError: false);
        if (type == null) {
            throw new AkronReconstructionException(path, "type is unavailable: " + typeName);
        }
        ResolvedTypes.TryAdd(typeName, type);
        return type;
    }

    private static Type FindLoadedType(string fullName) {
        if (string.IsNullOrWhiteSpace(fullName)) {
            return null;
        }
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            Type type = assembly.GetType(fullName, throwOnError: false, ignoreCase: false);
            if (type != null) {
                return type;
            }
        }
        return null;
    }

    // MonoMod gives each managed hook a stable public entry method and a
    // process-local trampoline for the next hook in its chain. The entry lets
    // the saved graph find the same chain position after restart. Reflection is
    // limited to obtaining that trampoline because MonoMod does not expose it
    // through its public introspection API.
    private static bool TryResolveDetourNextMethod(
        MethodInfo sourceMethod,
        MethodInfo hookTarget,
        out MethodInfo nextMethod
    ) {
        nextMethod = null;
        if (sourceMethod == null || hookTarget == null) {
            return false;
        }

        DetourInfo detour = DetourManager.GetDetourInfo(sourceMethod).FirstDetour;
        while (detour != null && !MethodIdentityMatches(detour.Entry as MethodInfo, hookTarget)) {
            detour = detour.Next;
        }
        if (detour == null) {
            return false;
        }

        FieldInfo stateField = typeof(DetourInfo).GetField(
            "detour",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (stateField == null) {
            throw UnsupportedDetourReflection("DetourInfo.detour");
        }
        object state = stateField.GetValue(detour);
        if (state == null) {
            throw UnsupportedDetourReflection("DetourInfo.detour value");
        }
        FieldInfo trampolineField = state.GetType().GetField(
            "NextTrampoline",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (trampolineField == null) {
            throw UnsupportedDetourReflection(state.GetType().FullName + ".NextTrampoline");
        }
        object trampoline = trampolineField.GetValue(state);
        if (trampoline == null) {
            throw UnsupportedDetourReflection(state.GetType().FullName + ".NextTrampoline value");
        }
        PropertyInfo trampolineMethodProperty = trampoline.GetType().GetProperty(
            "TrampolineMethod",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (trampolineMethodProperty == null) {
            throw UnsupportedDetourReflection(trampoline.GetType().FullName + ".TrampolineMethod");
        }
        if (trampolineMethodProperty.GetValue(trampoline) is not MethodInfo resolvedMethod) {
            throw UnsupportedDetourReflection(trampoline.GetType().FullName + ".TrampolineMethod value");
        }
        nextMethod = resolvedMethod;
        return true;
    }

    private static NotSupportedException UnsupportedDetourReflection(string member) {
        Version version = typeof(DetourInfo).Assembly.GetName().Version;
        return new NotSupportedException(
            "MonoMod.RuntimeDetour " + (version?.ToString() ?? "unknown") +
            " does not provide the required hook-chain member " + member + ".");
    }

    private static bool MethodIdentityMatches(MethodInfo left, MethodInfo right) {
        if (left == null || right == null || left.Name != right.Name || left.DeclaringType != right.DeclaringType) {
            return false;
        }
        Type[] leftParameters = left.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        Type[] rightParameters = right.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        return left.ReturnType == right.ReturnType && leftParameters.SequenceEqual(rightParameters);
    }

    private static string DelegateMethodKey(MethodInfo method) {
        return TypeName(method?.DeclaringType) + "::" + (method?.Name ?? string.Empty) +
               "->" + TypeName(method?.ReturnType) + "(" +
               string.Join(",", method?.GetParameters().Select(parameter => TypeName(parameter.ParameterType)) ?? Enumerable.Empty<string>()) + ")";
    }

    private static string FieldPath(string parentPath, string fieldName) {
        return parentPath + "." + fieldName;
    }

    private static string ArrayPath(string parentPath, IReadOnlyList<int> indices) {
        return parentPath + "[" + string.Join(",", indices) + "]";
    }

    private static bool IsScalarType(Type type) {
        return type.IsEnum ||
               type.IsPrimitive ||
               type == typeof(string) ||
               type == typeof(decimal) ||
               type == typeof(DateTime) ||
               type == typeof(DateTimeOffset) ||
               type == typeof(TimeSpan) ||
               type == typeof(Guid) ||
               type == typeof(Point) ||
               type == typeof(Rectangle) ||
               type == typeof(Color) ||
               type == typeof(Vector2) ||
               type == typeof(Vector3) ||
               type == typeof(Vector4) ||
               type == typeof(Quaternion) ||
               type == typeof(Matrix) ||
               type == typeof(VertexPositionColor);
    }

    private static string EncodeScalar(object value, Type type, string path) {
        if (type == typeof(string)) {
            return (string) value;
        }
        if (type == typeof(bool)) {
            return (bool) value ? "true" : "false";
        }
        if (type == typeof(char)) {
            return ((int) (char) value).ToString(CultureInfo.InvariantCulture);
        }
        if (type == typeof(float)) {
            return ((float) value).ToString("R", CultureInfo.InvariantCulture);
        }
        if (type == typeof(double)) {
            return ((double) value).ToString("R", CultureInfo.InvariantCulture);
        }
        if (type == typeof(decimal)) {
            return ((decimal) value).ToString(CultureInfo.InvariantCulture);
        }
        if (type == typeof(DateTime)) {
            DateTime dateTime = (DateTime) value;
            return dateTime.Ticks.ToString(CultureInfo.InvariantCulture) + ":" + ((int) dateTime.Kind).ToString(CultureInfo.InvariantCulture);
        }
        if (type == typeof(DateTimeOffset)) {
            DateTimeOffset valueWithOffset = (DateTimeOffset) value;
            return valueWithOffset.Ticks.ToString(CultureInfo.InvariantCulture) + ":" + valueWithOffset.Offset.Ticks.ToString(CultureInfo.InvariantCulture);
        }
        if (type == typeof(TimeSpan)) {
            return ((TimeSpan) value).Ticks.ToString(CultureInfo.InvariantCulture);
        }
        if (type == typeof(Guid)) {
            return ((Guid) value).ToString("N");
        }
        if (type == typeof(Point)) {
            Point point = (Point) value;
            return JoinScalar(point.X, point.Y);
        }
        if (type == typeof(Rectangle)) {
            Rectangle rectangle = (Rectangle) value;
            return JoinScalar(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
        }
        if (type == typeof(Color)) {
            return GetPackedColor((Color) value).ToString("x8", CultureInfo.InvariantCulture);
        }
        if (type == typeof(Vector2)) {
            Vector2 vector = (Vector2) value;
            return JoinScalar(EncodeFloat(vector.X), EncodeFloat(vector.Y));
        }
        if (type == typeof(Vector3)) {
            Vector3 vector = (Vector3) value;
            return JoinScalar(EncodeFloat(vector.X), EncodeFloat(vector.Y), EncodeFloat(vector.Z));
        }
        if (type == typeof(Vector4)) {
            Vector4 vector = (Vector4) value;
            return JoinScalar(EncodeFloat(vector.X), EncodeFloat(vector.Y), EncodeFloat(vector.Z), EncodeFloat(vector.W));
        }
        if (type == typeof(Quaternion)) {
            Quaternion quaternion = (Quaternion) value;
            return JoinScalar(EncodeFloat(quaternion.X), EncodeFloat(quaternion.Y), EncodeFloat(quaternion.Z), EncodeFloat(quaternion.W));
        }
        if (type == typeof(Matrix)) {
            Matrix matrix = (Matrix) value;
            return JoinScalar(
                EncodeFloat(matrix.M11), EncodeFloat(matrix.M12), EncodeFloat(matrix.M13), EncodeFloat(matrix.M14),
                EncodeFloat(matrix.M21), EncodeFloat(matrix.M22), EncodeFloat(matrix.M23), EncodeFloat(matrix.M24),
                EncodeFloat(matrix.M31), EncodeFloat(matrix.M32), EncodeFloat(matrix.M33), EncodeFloat(matrix.M34),
                EncodeFloat(matrix.M41), EncodeFloat(matrix.M42), EncodeFloat(matrix.M43), EncodeFloat(matrix.M44));
        }
        if (type == typeof(VertexPositionColor)) {
            VertexPositionColor vertex = (VertexPositionColor) value;
            return JoinScalar(
                EncodeFloat(vertex.Position.X),
                EncodeFloat(vertex.Position.Y),
                EncodeFloat(vertex.Position.Z),
                GetPackedColor(vertex.Color).ToString("x8", CultureInfo.InvariantCulture));
        }
        if (type.IsEnum) {
            Type underlying = Enum.GetUnderlyingType(type);
            return Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture).ToString();
        }
        if (type.IsPrimitive) {
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        throw new AkronReconstructionException(path, "unsupported scalar type: " + type.FullName);
    }

    private static object DecodeScalar(AkronReconstructionValue value, string path) {
        Type type = ResolveType(value.TypeName, path);
        string scalar = value.Scalar ?? string.Empty;
        if (type == typeof(string)) {
            return scalar;
        }
        if (type == typeof(bool)) {
            return string.Equals(scalar, "true", StringComparison.Ordinal);
        }
        if (type == typeof(char)) {
            return (char) int.Parse(scalar, CultureInfo.InvariantCulture);
        }
        if (type == typeof(float)) {
            return float.Parse(scalar, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        if (type == typeof(double)) {
            return double.Parse(scalar, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        if (type == typeof(decimal)) {
            return decimal.Parse(scalar, NumberStyles.Number, CultureInfo.InvariantCulture);
        }
        if (type == typeof(DateTime)) {
            string[] parts = scalar.Split(':');
            return new DateTime(long.Parse(parts[0], CultureInfo.InvariantCulture), (DateTimeKind) int.Parse(parts[1], CultureInfo.InvariantCulture));
        }
        if (type == typeof(DateTimeOffset)) {
            string[] parts = scalar.Split(':');
            return new DateTimeOffset(long.Parse(parts[0], CultureInfo.InvariantCulture), new TimeSpan(long.Parse(parts[1], CultureInfo.InvariantCulture)));
        }
        if (type == typeof(TimeSpan)) {
            return new TimeSpan(long.Parse(scalar, CultureInfo.InvariantCulture));
        }
        if (type == typeof(Guid)) {
            return Guid.ParseExact(scalar, "N");
        }
        if (type == typeof(Point)) {
            string[] parts = SplitScalar(scalar, 2, path);
            return new Point(ParseInt(parts[0]), ParseInt(parts[1]));
        }
        if (type == typeof(Rectangle)) {
            string[] parts = SplitScalar(scalar, 4, path);
            return new Rectangle(ParseInt(parts[0]), ParseInt(parts[1]), ParseInt(parts[2]), ParseInt(parts[3]));
        }
        if (type == typeof(Color)) {
            return FromPackedColor(uint.Parse(scalar, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }
        if (type == typeof(Vector2)) {
            string[] parts = SplitScalar(scalar, 2, path);
            return new Vector2(DecodeFloat(parts[0]), DecodeFloat(parts[1]));
        }
        if (type == typeof(Vector3)) {
            string[] parts = SplitScalar(scalar, 3, path);
            return new Vector3(DecodeFloat(parts[0]), DecodeFloat(parts[1]), DecodeFloat(parts[2]));
        }
        if (type == typeof(Vector4)) {
            string[] parts = SplitScalar(scalar, 4, path);
            return new Vector4(DecodeFloat(parts[0]), DecodeFloat(parts[1]), DecodeFloat(parts[2]), DecodeFloat(parts[3]));
        }
        if (type == typeof(Quaternion)) {
            string[] parts = SplitScalar(scalar, 4, path);
            return new Quaternion(DecodeFloat(parts[0]), DecodeFloat(parts[1]), DecodeFloat(parts[2]), DecodeFloat(parts[3]));
        }
        if (type == typeof(Matrix)) {
            string[] parts = SplitScalar(scalar, 16, path);
            return new Matrix(
                DecodeFloat(parts[0]), DecodeFloat(parts[1]), DecodeFloat(parts[2]), DecodeFloat(parts[3]),
                DecodeFloat(parts[4]), DecodeFloat(parts[5]), DecodeFloat(parts[6]), DecodeFloat(parts[7]),
                DecodeFloat(parts[8]), DecodeFloat(parts[9]), DecodeFloat(parts[10]), DecodeFloat(parts[11]),
                DecodeFloat(parts[12]), DecodeFloat(parts[13]), DecodeFloat(parts[14]), DecodeFloat(parts[15]));
        }
        if (type == typeof(VertexPositionColor)) {
            string[] parts = SplitScalar(scalar, 4, path);
            Color color = FromPackedColor(uint.Parse(parts[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            return new VertexPositionColor(
                new Vector3(DecodeFloat(parts[0]), DecodeFloat(parts[1]), DecodeFloat(parts[2])),
                color);
        }
        if (type.IsEnum) {
            object underlying = Convert.ChangeType(scalar, Enum.GetUnderlyingType(type), CultureInfo.InvariantCulture);
            return Enum.ToObject(type, underlying);
        }
        if (type.IsPrimitive) {
            return Convert.ChangeType(scalar, type, CultureInfo.InvariantCulture);
        }

        throw new AkronReconstructionException(path, "unsupported scalar type: " + type.FullName);
    }

    internal static string GetSavedSoundEmitterEventName(
        AkronReconstructionNode emitterNode,
        IReadOnlyDictionary<int, AkronReconstructionNode> sourceNodes
    ) {
        AkronReconstructionValue sourceReference = (emitterNode?.Fields ?? new List<AkronReconstructionField>())
            .FirstOrDefault(field => field.Name == "<Source>k__BackingField" &&
                                     field.Value?.Kind == ReferenceValueKind)
            ?.Value;
        if (sourceReference == null || sourceNodes == null ||
            !sourceNodes.TryGetValue(sourceReference.NodeId, out AkronReconstructionNode sourceNode)) {
            throw new AkronReconstructionException(emitterNode?.Path ?? "$", "saved sound emitter source is missing");
        }
        AkronReconstructionField eventNameField = (sourceNode.Fields ?? new List<AkronReconstructionField>())
            .FirstOrDefault(field => field.Name == nameof(SoundSource.EventName) &&
                                     field.Value?.Kind == ScalarValueKind);
        string eventName = eventNameField == null
            ? string.Empty
            : DecodeScalar(eventNameField.Value, eventNameField.Path) as string;
        if (string.IsNullOrWhiteSpace(eventName)) {
            throw new AkronReconstructionException(sourceNode.Path, "saved sound emitter event name is missing");
        }
        return eventName;
    }

    internal static bool IsTrailSnapshotComponentReference(
        Type parentType,
        string fieldName,
        Type targetType
    ) {
        if (parentType != typeof(TrailManager.Snapshot) || targetType == null) {
            return false;
        }
        return fieldName == nameof(TrailManager.Snapshot.Hair)
            ? targetType == typeof(PlayerHair)
            : fieldName == nameof(TrailManager.Snapshot.Sprite) &&
              typeof(Image).IsAssignableFrom(targetType);
    }

    internal static bool IsPlayerRuntimeColliderAlias(
        Type parentType,
        string activeFieldName,
        string storedFieldName,
        Type targetType
    ) {
        if (parentType != typeof(Player) || targetType != typeof(Hitbox)) {
            return false;
        }
        return activeFieldName == "collider"
            ? storedFieldName is "normalHitbox" or "duckHitbox"
            : activeFieldName == "hurtbox" &&
              storedFieldName is "normalHurtbox" or "duckHurtbox";
    }

    internal static bool IsBuiltInSavedComponentAliasField(
        Type parentType,
        string fieldName,
        Type targetType
    ) {
        return parentType == typeof(TalkComponent.TalkComponentUI) &&
               fieldName == "wiggler" &&
               targetType == typeof(Wiggler);
    }

    internal static bool IsCompilerClosureIteratorLocal(Type closureType, string fieldName) {
        return closureType != null &&
               closureType.DeclaringType != null &&
               closureType.GetCustomAttribute<CompilerGeneratedAttribute>() != null &&
               fieldName?.StartsWith("<>8__", StringComparison.Ordinal) == true;
    }

    private static string EncodeFloat(float value) {
        return BitConverter.SingleToInt32Bits(value).ToString("x8", CultureInfo.InvariantCulture);
    }

    private static float DecodeFloat(string value) {
        return BitConverter.Int32BitsToSingle(unchecked((int) uint.Parse(
            value,
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture)));
    }

    private static uint GetPackedColor(Color color) {
        return Unsafe.As<Color, uint>(ref color);
    }

    private static Color FromPackedColor(uint packedColor) {
        return Unsafe.As<uint, Color>(ref packedColor);
    }

    private static string JoinScalar(params object[] values) {
        return string.Join(":", values.Select(value => Convert.ToString(value, CultureInfo.InvariantCulture)));
    }

    private static string[] SplitScalar(string scalar, int count, string path) {
        string[] parts = scalar.Split(':');
        if (parts.Length != count) {
            throw new AkronReconstructionException(path, "scalar component count differs");
        }
        return parts;
    }

    private static int ParseInt(string value) {
        return int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    private static FieldInfo ResolveField(string declaringTypeName, string fieldName, string path) {
        var key = (declaringTypeName, fieldName);
        if (ResolvedFields.TryGetValue(key, out FieldInfo cachedField)) {
            return cachedField;
        }
        Type declaringType = ResolveType(declaringTypeName, path);
        FieldInfo field = declaringType.GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (field == null || field.IsStatic) {
            throw new AkronReconstructionException(path, "field is unavailable");
        }
        ResolvedFields.TryAdd(key, field);
        return field;
    }

    private static bool HasStableSourceId(EntityID id) {
        return !string.IsNullOrEmpty(id.Level);
    }

    private static bool EntityIdsMatch(EntityID left, EntityID right) {
        return left.ID == right.ID && string.Equals(left.Level, right.Level, StringComparison.Ordinal);
    }

    private static EntityID GetEntitySourceId(Entity entity) {
        return EntitySourceIdField?.GetValue(entity) is EntityID sourceId ? sourceId : default;
    }

    private static ComponentList GetEntityComponents(Entity entity) {
        return EntityComponentsField?.GetValue(entity) as ComponentList;
    }

    private static Entity GetComponentEntity(Component component) {
        return ComponentEntityField?.GetValue(component) as Entity;
    }

    private static EntityList GetSceneEntities(Scene scene) {
        return SceneEntitiesField?.GetValue(scene) as EntityList;
    }

    private static IEnumerable<Entity> GetEntityListEntities(EntityList entities) {
        return EntityListEntitiesField?.GetValue(entities) as IEnumerable<Entity> ?? Array.Empty<Entity>();
    }

    private static IEnumerable<Component> GetComponentListComponents(ComponentList components) {
        return ComponentListComponentsField?.GetValue(components) as IEnumerable<Component> ?? Array.Empty<Component>();
    }

    private sealed class CaptureContext {
        private readonly AkronReconstructionGraph owner;
        private readonly Dictionary<object, int> savedNodeIds = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<object, int> pairedFreshObjects = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, HashSet<FreshResource>> freshResources = new Dictionary<string, HashSet<FreshResource>>(StringComparer.Ordinal);
        private readonly Dictionary<Type, HashSet<FreshResource>> freshRoomObjects = new Dictionary<Type, HashSet<FreshResource>>();
        private readonly Dictionary<object, FreshResource> freshCandidates =
            new Dictionary<object, FreshResource>(ReferenceEqualityComparer.Instance);

        public CaptureContext(AkronReconstructionGraph owner, object freshRoot) {
            this.owner = owner;
            if (owner.getLiveResourceKey != null) {
                IndexFreshResources(freshRoot);
            }
        }

        public AkronReconstructionDocument Document { get; } = new AkronReconstructionDocument();

        private void IndexFreshResources(object freshRoot) {
            HashSet<object> visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            IndexFreshValue(freshRoot, new List<AkronReconstructionPathStep>(), visited);
        }

        private void IndexFreshValue(
            object value,
            List<AkronReconstructionPathStep> path,
            HashSet<object> visited
        ) {
            if (value == null) {
                return;
            }

            Type type = value.GetType();
            if (IsScalarType(type) || type == typeof(IntPtr) || type == typeof(UIntPtr) ||
                type.IsPointer || type.IsByRefLike || value is Delegate) {
                return;
            }
            if (!type.IsValueType && !visited.Add(value)) {
                return;
            }
            if (value is Entity || value is Component) {
                if (!freshRoomObjects.TryGetValue(type, out HashSet<FreshResource> roomObjects)) {
                    roomObjects = new HashSet<FreshResource>();
                    freshRoomObjects[type] = roomObjects;
                }
                roomObjects.Add(GetFreshCandidate(value, path));
            }
            if (owner.isLiveResource(type)) {
                string key = ResourceKey(value);
                if (!string.IsNullOrWhiteSpace(key)) {
                    if (!freshResources.TryGetValue(key, out HashSet<FreshResource> matches)) {
                        matches = new HashSet<FreshResource>();
                        freshResources[key] = matches;
                    }
                    matches.Add(GetFreshCandidate(value, path));
                }
                return;
            }

            if (value is Array array) {
                foreach (int[] indices in EnumerateArrayIndices(array)) {
                    IndexFreshValue(
                        array.GetValue(indices),
                        AppendPath(path, new AkronReconstructionPathStep {
                            Kind = "array",
                            ArrayIndices = indices.ToList()
                        }),
                        visited);
                }
                return;
            }

            foreach (FieldInfo field in GetInstanceFields(type)) {
                IndexFreshValue(
                    field.GetValue(value),
                    AppendPath(path, new AkronReconstructionPathStep {
                        Kind = "field",
                        DeclaringTypeName = TypeName(field.DeclaringType),
                        FieldName = field.Name
                    }),
                    visited);
            }
        }

        private string ResourceKey(object resource) {
            return owner.GetTypedResourceKey(resource);
        }

        private FreshResource GetFreshCandidate(object value, IReadOnlyList<AkronReconstructionPathStep> path) {
            if (!freshCandidates.TryGetValue(value, out FreshResource candidate)) {
                candidate = new FreshResource(value, ClonePath(path));
                freshCandidates[value] = candidate;
            }
            return candidate;
        }

        private FreshResource FindFreshResource(string key, string path) {
            if (!freshResources.TryGetValue(key, out HashSet<FreshResource> matches)) {
                throw new AkronReconstructionException(path, "fresh resource key is unavailable: " + key);
            }
            if (matches.Count != 1) {
                throw new AkronReconstructionException(
                    path,
                    "fresh resource key is ambiguous;matches=" + matches.Count.ToString(CultureInfo.InvariantCulture) + ";key=" + key);
            }
            return matches.First();
        }

        private FreshResource FindFreshRoomObject(object savedValue) {
            Type type = savedValue.GetType();
            if (!freshRoomObjects.TryGetValue(type, out HashSet<FreshResource> matches)) {
                return null;
            }
            if (savedValue is Entity savedEntity && HasStableSourceId(GetEntitySourceId(savedEntity))) {
                EntityID savedSourceId = GetEntitySourceId(savedEntity);
                List<FreshResource> sourceMatches = matches
                    .Where(candidate => candidate.Value is Entity freshEntity &&
                                        GetEntitySourceId(freshEntity).Equals(savedSourceId))
                    .ToList();
                return sourceMatches.Count == 1 ? sourceMatches[0] : null;
            }
            return matches.Count == 1 ? matches.First() : null;
        }

        private void RemoveFreshCandidate(object value) {
            if (!freshCandidates.Remove(value, out FreshResource candidate)) {
                return;
            }
            Type type = value.GetType();
            if (freshRoomObjects.TryGetValue(type, out HashSet<FreshResource> roomObjects)) {
                roomObjects.Remove(candidate);
            }
            if (owner.isLiveResource(type)) {
                string key = ResourceKey(value);
                if (freshResources.TryGetValue(key, out HashSet<FreshResource> resources)) {
                    resources.Remove(candidate);
                }
            }
        }

        private sealed class FreshResource {
            public FreshResource(object value, List<AkronReconstructionPathStep> path) {
                Value = value;
                Path = path;
            }

            public object Value { get; }
            public List<AkronReconstructionPathStep> Path { get; }
        }

        public AkronReconstructionValue CaptureValue(
            object savedValue,
            object freshValue,
            string path,
            List<AkronReconstructionPathStep> freshPath,
            Type containingType = null,
            string knownEventPath = null,
            AkronReconstructionNode parentNode = null,
            AkronReconstructionPathStep parentStep = null,
            int parentDelegateIndex = -1,
            bool freshPathOverride = false
        ) {
            if (savedValue == null) {
                return new AkronReconstructionValue { Kind = NullValueKind };
            }

            Type savedType = savedValue.GetType();
            if (IsScalarType(savedType)) {
                return new AkronReconstructionValue {
                    Kind = ScalarValueKind,
                    TypeName = TypeName(savedType),
                    Scalar = EncodeScalar(savedValue, savedType, path)
                };
            }
            if (savedType == typeof(IntPtr) || savedType == typeof(UIntPtr) || savedType.IsPointer || savedType.IsByRefLike) {
                throw new AkronReconstructionException(path, "process pointer cannot be persisted");
            }
            if (savedNodeIds.TryGetValue(savedValue, out int existingNodeId)) {
                return new AkronReconstructionValue { Kind = ReferenceValueKind, NodeId = existingNodeId };
            }

            bool persistentEventInstance = savedValue is EventInstance;
            bool persistentResource = owner.resourceAdapter?.CanPersist(savedType) == true;
            bool freshTypeMatches = freshValue != null && freshValue.GetType() == savedType;
            bool entityIdentityMatches = savedValue is not Entity savedEntity ||
                                         !HasStableSourceId(GetEntitySourceId(savedEntity)) ||
                                         freshValue is Entity freshEntity &&
                                         GetEntitySourceId(freshEntity).Equals(GetEntitySourceId(savedEntity));
            if ((!freshTypeMatches || !entityIdentityMatches) &&
                (savedValue is Entity || savedValue is Component)) {
                FreshResource matchedRoomObject = FindFreshRoomObject(savedValue);
                if (matchedRoomObject != null) {
                    freshValue = matchedRoomObject.Value;
                    freshPath = ClonePath(matchedRoomObject.Path);
                    freshTypeMatches = true;
                    entityIdentityMatches = true;
                    freshPathOverride = true;
                } else if (!entityIdentityMatches) {
                    freshValue = null;
                    freshTypeMatches = false;
                }
            }
            bool liveAnchor = !persistentEventInstance && !persistentResource && owner.isLiveResource(savedType);
            string savedLiveResourceKey = string.Empty;
            if (liveAnchor || persistentResource) {
                string savedResourceKey = ResourceKey(savedValue);
                savedLiveResourceKey = savedResourceKey;
            }
            if (liveAnchor) {
                string savedResourceKey = savedLiveResourceKey;
                string freshResourceKey = freshTypeMatches ? ResourceKey(freshValue) : string.Empty;
                if (!string.IsNullOrWhiteSpace(savedResourceKey) &&
                    (!freshTypeMatches || !string.Equals(savedResourceKey, freshResourceKey, StringComparison.Ordinal))) {
                    FreshResource matchedResource = FindFreshResource(savedResourceKey, path);
                    freshValue = matchedResource.Value;
                    freshPath = ClonePath(matchedResource.Path);
                    freshTypeMatches = freshValue.GetType() == savedType;
                    freshPathOverride = true;
                }
                if (!freshTypeMatches) {
                    throw new AkronReconstructionException(
                        path,
                        "fresh resource is missing or has a different type" +
                        ";saved-type=" + savedType.FullName +
                        ";fresh-type=" + (freshValue?.GetType().FullName ?? "null") +
                        ";resource-key=" + savedResourceKey);
                }
            }

            int nodeId = Document.Nodes.Count + 1;
            savedNodeIds[savedValue] = nodeId;
            bool useFreshObject = freshTypeMatches && !savedType.IsValueType &&
                                  savedValue is not Delegate && !persistentEventInstance;
            if (useFreshObject && pairedFreshObjects.TryGetValue(freshValue, out int existingOwnerId)) {
                if (liveAnchor) {
                    throw new AkronReconstructionException(path, "fresh resource is already paired with node " + existingOwnerId.ToString(CultureInfo.InvariantCulture));
                }
                useFreshObject = false;
                freshValue = null;
            } else if (useFreshObject) {
                pairedFreshObjects[freshValue] = nodeId;
                RemoveFreshCandidate(freshValue);
            }

            AkronReconstructionNode node = new AkronReconstructionNode {
                Id = nodeId,
                Kind = liveAnchor
                    ? AnchorKind
                    : persistentResource
                        ? PersistentResourceKind
                    : persistentEventInstance
                        ? EventInstanceKind
                        : savedValue is Delegate
                            ? DelegateKind
                            : savedType.IsArray ? ArrayKind : ObjectKind,
                TypeName = TypeName(savedType),
                Path = path,
                ParentNodeId = parentNode?.Id ?? 0,
                ParentKind = parentStep?.Kind ?? (parentDelegateIndex >= 0 ? "delegate" : string.Empty),
                ParentDeclaringTypeName = parentStep?.DeclaringTypeName ?? string.Empty,
                ParentFieldName = parentStep?.FieldName ?? string.Empty,
                ParentArrayIndices = new List<int>(parentStep?.ArrayIndices ?? new List<int>()),
                ParentDelegateIndex = parentDelegateIndex,
                UseFreshObject = liveAnchor || useFreshObject,
                ResourceKey = savedLiveResourceKey,
                FreshPath = freshPathOverride
                    ? ClonePath(freshPath)
                    : new List<AkronReconstructionPathStep>()
            };
            Document.Nodes.Add(node);

            if (liveAnchor) {
                return new AkronReconstructionValue { Kind = ReferenceValueKind, NodeId = nodeId };
            }
            if (persistentResource) {
                node.ResourcePayload = owner.resourceAdapter.Capture(savedValue);
                if (node.ResourcePayload == null) {
                    throw new AkronReconstructionException(path, "persistent resource capture returned no payload");
                }
            } else if (persistentEventInstance) {
                string eventPath = knownEventPath;
                if (string.IsNullOrWhiteSpace(eventPath) && freshValue is EventInstance freshEventInstance) {
                    eventPath = AkronEventInstanceUtils.GetEventPath(freshEventInstance);
                }
                node.EventInstance = AkronEventInstanceUtils.CapturePersistentState(
                    (EventInstance) savedValue,
                    eventPath);
                if (node.EventInstance == null) {
                    throw new AkronReconstructionException(path, "FMOD event has no stable event path");
                }
            } else if (savedValue is Delegate savedDelegate) {
                CaptureDelegate(node, savedDelegate, freshValue as Delegate, path, freshPath, containingType);
            } else if (savedValue is Array savedArray) {
                CaptureArray(node, savedArray, freshValue as Array, path, freshPath);
            } else {
                CaptureObject(node, savedValue, useFreshObject ? freshValue : null, path, freshPath);
            }

            return new AkronReconstructionValue { Kind = ReferenceValueKind, NodeId = nodeId };
        }

        private void CaptureObject(
            AkronReconstructionNode node,
            object savedObject,
            object freshObject,
            string path,
            List<AkronReconstructionPathStep> freshPath
        ) {
            foreach (FieldInfo field in GetInstanceFields(savedObject.GetType())) {
                string childPath = FieldPath(path, field.Name);
                AkronReconstructionPathStep pathStep = new AkronReconstructionPathStep {
                    Kind = "field",
                    DeclaringTypeName = TypeName(field.DeclaringType),
                    FieldName = field.Name
                };
                List<AkronReconstructionPathStep> childFreshPath = AppendPath(freshPath, pathStep);
                object freshFieldValue = freshObject == null ? null : field.GetValue(freshObject);
                string knownEventPath = AkronEventInstanceUtils.GetOwnerEventPath(savedObject, field.Name);
                node.Fields.Add(new AkronReconstructionField {
                    DeclaringTypeName = TypeName(field.DeclaringType),
                    Name = field.Name,
                    Path = childPath,
                    Value = CaptureValue(
                        field.GetValue(savedObject),
                        freshFieldValue,
                        childPath,
                        childFreshPath,
                        savedObject.GetType(),
                        knownEventPath,
                        node,
                        pathStep)
                });
            }
        }

        private void CaptureArray(
            AkronReconstructionNode node,
            Array savedArray,
            Array freshArray,
            string path,
            List<AkronReconstructionPathStep> freshPath
        ) {
            for (int dimension = 0; dimension < savedArray.Rank; dimension++) {
                node.ArrayLengths.Add(savedArray.GetLength(dimension));
                node.ArrayLowerBounds.Add(savedArray.GetLowerBound(dimension));
            }

            // Primitive grids contain millions of values in large custom maps.
            // Buffer.BlockCopy keeps their exact in-memory bits without adding
            // a JSON kind and assembly-qualified type name around every item.
            if (CanPackPrimitiveArray(savedArray)) {
                node.PackedPrimitiveArrayBytes = new byte[Buffer.ByteLength(savedArray)];
                Buffer.BlockCopy(
                    savedArray,
                    0,
                    node.PackedPrimitiveArrayBytes,
                    0,
                    node.PackedPrimitiveArrayBytes.Length);
                return;
            }
            foreach (int[] indices in EnumerateArrayIndices(savedArray)) {
                string childPath = ArrayPath(path, indices);
                AkronReconstructionPathStep pathStep = new AkronReconstructionPathStep {
                    Kind = "array",
                    ArrayIndices = indices.ToList()
                };
                object freshItem = HasArrayIndex(freshArray, indices) ? freshArray.GetValue(indices) : null;
                node.Items.Add(CaptureValue(
                    savedArray.GetValue(indices),
                    freshItem,
                    childPath,
                    AppendPath(freshPath, pathStep),
                    parentNode: node,
                    parentStep: pathStep));
            }
        }

        private void CaptureDelegate(
            AkronReconstructionNode node,
            Delegate savedDelegate,
            Delegate freshDelegate,
            string path,
            List<AkronReconstructionPathStep> freshPath,
            Type containingType
        ) {
            Delegate[] savedCalls = savedDelegate.GetInvocationList();
            Delegate[] freshCalls = freshDelegate?.GetInvocationList() ?? Array.Empty<Delegate>();
            bool hasAnonymousRuntimeMethod = savedCalls.Any(call => call.Method.DeclaringType == null);
            if (hasAnonymousRuntimeMethod) {
                if (savedCalls.Length == 1 &&
                    TryDescribeDetourNext(savedCalls[0], containingType, out MethodInfo sourceMethod, out MethodInfo hookTarget)) {
                    node.DelegateCalls.Add(new AkronReconstructionDelegateCall {
                        Kind = DetourNextDelegateCallKind,
                        Target = new AkronReconstructionValue { Kind = NullValueKind },
                        DeclaringTypeName = TypeName(sourceMethod.DeclaringType),
                        MethodName = sourceMethod.Name,
                        ReturnTypeName = TypeName(sourceMethod.ReturnType),
                        ParameterTypeNames = sourceMethod.GetParameters()
                            .Select(parameter => TypeName(parameter.ParameterType))
                            .ToList(),
                        HookTargetDeclaringTypeName = TypeName(hookTarget.DeclaringType),
                        HookTargetMethodName = hookTarget.Name,
                        HookTargetReturnTypeName = TypeName(hookTarget.ReturnType),
                        HookTargetParameterTypeNames = hookTarget.GetParameters()
                            .Select(parameter => TypeName(parameter.ParameterType))
                            .ToList()
                    });
                    return;
                }

                bool canUseFreshRuntimeDelegate = savedCalls.All(call => call.Method.DeclaringType == null && call.Target == null) &&
                                                  freshCalls.Length == savedCalls.Length &&
                                                  freshCalls.All(call => call.Method.DeclaringType == null && call.Target == null);
                if (!canUseFreshRuntimeDelegate) {
                    string savedTargets = string.Join(",", savedCalls.Select(call => call.Target?.GetType().FullName ?? "null"));
                    string freshTargets = string.Join(",", freshCalls.Select(call => call.Target?.GetType().FullName ?? "null"));
                    throw new AkronReconstructionException(
                        path,
                        "anonymous delegate has no safe fresh match" +
                        ";delegate-type=" + savedDelegate.GetType().FullName +
                        ";saved-calls=" + savedCalls.Length.ToString(CultureInfo.InvariantCulture) +
                        ";saved-targets=" + savedTargets +
                        ";fresh-calls=" + freshCalls.Length.ToString(CultureInfo.InvariantCulture) +
                        ";fresh-targets=" + freshTargets);
                }

                // Runtime detours create anonymous target-free methods that have
                // no metadata identity across processes. Their valid replacement
                // already exists in the normally loaded room. Bind the complete
                // delegate to that fresh structural path instead of serializing a
                // process-only function pointer.
                node.Kind = AnchorKind;
                node.UseFreshObject = true;
                return;
            }

            for (int index = 0; index < savedCalls.Length; index++) {
                Delegate savedCall = savedCalls[index];
                MethodInfo method = savedCall.Method;
                if (method.ContainsGenericParameters) {
                    throw new AkronReconstructionException(path, "delegate method contains open generic parameters");
                }

                Delegate freshCall = index < freshCalls.Length && MethodsMatch(savedCall.Method, freshCalls[index].Method)
                    ? freshCalls[index]
                    : null;
                node.DelegateCalls.Add(new AkronReconstructionDelegateCall {
                    Kind = MethodDelegateCallKind,
                    Target = CaptureValue(
                        savedCall.Target,
                        freshCall?.Target,
                        path + ".<target>[" + index.ToString(CultureInfo.InvariantCulture) + "]",
                        freshPath,
                        parentNode: node,
                        parentDelegateIndex: index),
                    DeclaringTypeName = TypeName(method.DeclaringType),
                    MethodName = method.Name,
                    ReturnTypeName = TypeName(method.ReturnType),
                    ParameterTypeNames = method.GetParameters().Select(parameter => TypeName(parameter.ParameterType)).ToList()
                });
            }
        }

        private static bool TryDescribeDetourNext(
            Delegate savedCall,
            Type containingType,
            out MethodInfo sourceMethod,
            out MethodInfo hookTarget
        ) {
            sourceMethod = null;
            hookTarget = FindStateMachineMethod(containingType);
            if (savedCall == null || savedCall.Target != null || savedCall.Method.DeclaringType != null || hookTarget == null) {
                return false;
            }

            sourceMethod = FindHookSourceMethod(savedCall.GetType(), hookTarget);
            if (sourceMethod == null ||
                !TryResolveDetourNextMethod(sourceMethod, hookTarget, out MethodInfo currentNext) ||
                currentNext != savedCall.Method) {
                sourceMethod = null;
                hookTarget = null;
                return false;
            }
            return true;
        }

        private static MethodInfo FindStateMachineMethod(Type stateMachineType) {
            if (stateMachineType == null || stateMachineType.DeclaringType == null) {
                return null;
            }

            foreach (MethodInfo method in stateMachineType.DeclaringType.GetMethods(
                         BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)) {
                IteratorStateMachineAttribute iterator = method.GetCustomAttribute<IteratorStateMachineAttribute>();
                if (iterator?.StateMachineType == stateMachineType) {
                    return method;
                }
                AsyncStateMachineAttribute asyncStateMachine = method.GetCustomAttribute<AsyncStateMachineAttribute>();
                if (asyncStateMachine?.StateMachineType == stateMachineType) {
                    return method;
                }
            }
            return null;
        }

        private static MethodInfo FindHookSourceMethod(Type originalDelegateType, MethodInfo hookTarget) {
            const string OriginalPrefix = "orig_";
            if (originalDelegateType == null || !originalDelegateType.Name.StartsWith(OriginalPrefix, StringComparison.Ordinal)) {
                return null;
            }

            string methodName = originalDelegateType.Name.Substring(OriginalPrefix.Length);
            List<Type> candidateTypes = new List<Type>();
            string generatedOwnerName = originalDelegateType.DeclaringType?.FullName;
            if (!string.IsNullOrWhiteSpace(generatedOwnerName) && generatedOwnerName.StartsWith("On.", StringComparison.Ordinal)) {
                Type generatedSourceType = FindLoadedType(generatedOwnerName.Substring("On.".Length));
                if (generatedSourceType != null) {
                    candidateTypes.Add(generatedSourceType);
                }
            }
            if (hookTarget.DeclaringType != null && !candidateTypes.Contains(hookTarget.DeclaringType)) {
                candidateTypes.Add(hookTarget.DeclaringType);
            }

            MethodInfo invoke = originalDelegateType.GetMethod("Invoke");
            if (invoke == null) {
                return null;
            }
            Type[] invocationParameters = invoke.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
            List<MethodInfo> matches = new List<MethodInfo>();
            foreach (Type candidateType in candidateTypes) {
                foreach (MethodInfo candidate in candidateType.GetMethods(
                             BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)) {
                    if (candidate.Name != methodName || candidate.ReturnType != invoke.ReturnType) {
                        continue;
                    }
                    Type[] sourceParameters = candidate.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
                    bool signatureMatches = candidate.IsStatic
                        ? invocationParameters.SequenceEqual(sourceParameters)
                        : invocationParameters.Length == sourceParameters.Length + 1 &&
                          invocationParameters[0] == candidateType &&
                          invocationParameters.Skip(1).SequenceEqual(sourceParameters);
                    if (signatureMatches) {
                        matches.Add(candidate);
                    }
                }
            }
            return matches.Count == 1 ? matches[0] : null;
        }

        private static bool MethodsMatch(MethodInfo left, MethodInfo right) {
            if (left == null || right == null || left.Name != right.Name || left.DeclaringType != right.DeclaringType) {
                return false;
            }
            Type[] leftParameters = left.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
            Type[] rightParameters = right.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
            return left.ReturnType == right.ReturnType && leftParameters.SequenceEqual(rightParameters);
        }

        private static List<AkronReconstructionPathStep> ClonePath(IEnumerable<AkronReconstructionPathStep> path) {
            return path.Select(step => new AkronReconstructionPathStep {
                Kind = step.Kind,
                DeclaringTypeName = step.DeclaringTypeName,
                FieldName = step.FieldName,
                ArrayIndices = new List<int>(step.ArrayIndices ?? new List<int>())
            }).ToList();
        }

        private static List<AkronReconstructionPathStep> AppendPath(
            IEnumerable<AkronReconstructionPathStep> path,
            AkronReconstructionPathStep next
        ) {
            List<AkronReconstructionPathStep> appended = ClonePath(path);
            appended.Add(next);
            return appended;
        }
    }

    private sealed class RestoreContext {
        private readonly AkronReconstructionGraph owner;
        private readonly AkronReconstructionDocument document;
        private readonly object freshRoot;
        private readonly Dictionary<int, AkronReconstructionNode> nodes;
        private readonly Dictionary<int, List<(AkronReconstructionNode Parent, AkronReconstructionField Field)>> savedFieldAliases;
        private readonly Dictionary<int, List<AkronReconstructionNode>> savedArrayAliases;
        private readonly HashSet<int> savedDelegateTargetAliases;
        private readonly List<Action> assignments = new List<Action>();
        private readonly Dictionary<object, int> freshOwners = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, List<object>> freshResources = new Dictionary<string, List<object>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<object>> freshResourcesByStructuralPath = new Dictionary<string, List<object>>(StringComparer.Ordinal);
        private readonly Dictionary<object, int> freshFieldAliasReservations =
            new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<int, object> freshFieldAliasesByNode = new Dictionary<int, object>();
        private readonly Dictionary<int, (int ParentId, string DeclaringTypeName, string FieldName)>
            freshFieldAliasSourcesByNode =
                new Dictionary<int, (int ParentId, string DeclaringTypeName, string FieldName)>();
        private readonly HashSet<string> freshStructuralTypes = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> freshListStructuralTypeCounts =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> freshStaticDelegateMethods = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> freshStructuralDelegateCalls = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<object, HashSet<string>> freshInstanceDelegateMethods =
            new Dictionary<object, HashSet<string>>(ReferenceEqualityComparer.Instance);
        private readonly HashSet<object> activeFreshSafeObjects = new HashSet<object>(ReferenceEqualityComparer.Instance);
        private readonly HashSet<int> resolvedFreshObjectNodes = new HashSet<int>();
        private readonly HashSet<int> authenticatedRuntimeStateNodes = new HashSet<int>();
        private readonly HashSet<int> authenticatedRuntimeEntityNodes = new HashSet<int>();
        private readonly HashSet<int> authenticatedOwnedNestedStateNodes = new HashSet<int>();
        private readonly HashSet<int> authenticatedOwnedComponentNodes = new HashSet<int>();
        private readonly HashSet<int> authenticatedDelegateTargetNodes = new HashSet<int>();
        private readonly HashSet<int> authenticatedIteratorClosureNodes = new HashSet<int>();
        private readonly HashSet<int> authenticatedScreenWipeNodes = new HashSet<int>();
        private readonly List<object> createdPersistentResources = new List<object>();
        private readonly List<object> replacedPersistentResources = new List<object>();
        private readonly HashSet<EventInstance> displacedEventInstances = new HashSet<EventInstance>();

        public RestoreContext(AkronReconstructionGraph owner, AkronReconstructionDocument document, object freshRoot) {
            this.owner = owner;
            this.document = document;
            this.freshRoot = freshRoot;
            nodes = document.Nodes.ToDictionary(node => node.Id);
            savedFieldAliases = IndexSavedFieldAliases(document);
            savedArrayAliases = IndexSavedArrayAliases(document);
            savedDelegateTargetAliases = IndexSavedDelegateTargetAliases(document, nodes);
            Objects = new Dictionary<int, object>();
        }

        public RestoreContext(
            AkronReconstructionGraph owner,
            AkronReconstructionDocument document,
            Dictionary<int, object> objects
        ) {
            this.owner = owner;
            this.document = document;
            freshRoot = objects[document.RootNodeId];
            nodes = document.Nodes.ToDictionary(node => node.Id);
            savedFieldAliases = IndexSavedFieldAliases(document);
            savedArrayAliases = IndexSavedArrayAliases(document);
            savedDelegateTargetAliases = IndexSavedDelegateTargetAliases(document, nodes);
            Objects = objects;
        }

        public Dictionary<int, object> Objects { get; }

        public void ResolveObjects() {
            AkronReconstructionNode rootNode = nodes[document.RootNodeId];
            Type rootType = ResolveType(rootNode.TypeName, rootNode.Path);
            if (freshRoot.GetType() != rootType) {
                throw new AkronReconstructionException("$", "fresh root type differs from the saved root");
            }
            IndexFreshResources(
                freshRoot,
                new List<AkronReconstructionPathStep>(),
                new HashSet<object>(ReferenceEqualityComparer.Instance));
            ReserveFreshFieldAliases();

            foreach (AkronReconstructionNode node in document.Nodes.OrderBy(node => node.Id)) {
                Type type = ResolveType(node.TypeName, node.Path);
                object restoredObject;
                if (node.Id == document.RootNodeId) {
                    restoredObject = freshRoot;
                    resolvedFreshObjectNodes.Add(node.Id);
                } else if (node.Kind == PersistentResourceKind) {
                    object freshResource = node.UseFreshObject
                        ? ResolveFreshObject(node)
                        : null;
                    if (freshResource != null && freshResource.GetType() != type) {
                        freshResource = null;
                    }
                    restoredObject = owner.resourceAdapter?.Restore(node.ResourcePayload, freshResource);
                    if (restoredObject != null && !ReferenceEquals(restoredObject, freshResource)) {
                        owner.ownedPersistentResources.Add(restoredObject);
                        createdPersistentResources.Add(restoredObject);
                        if (freshResource != null && owner.ownedPersistentResources.Contains(freshResource)) {
                            replacedPersistentResources.Add(freshResource);
                        }
                    }
                    if (restoredObject == null || restoredObject.GetType() != type) {
                        throw new AkronReconstructionException(node.Path, "persistent resource could not be restored");
                    }
                } else if (node.UseFreshObject) {
                    bool resourceMatchedByDynamicOwnerPath = false;
                    bool matchedByExactParentSlot = TryResolveFreshExactParentSlot(
                        node,
                        type,
                        out object exactParentObject);
                    if (matchedByExactParentSlot) {
                        restoredObject = exactParentObject;
                    } else {
                        restoredObject = TryResolveFreshFieldAlias(node, type, out object preferredFieldAlias) &&
                                         freshFieldAliasReservations.TryGetValue(preferredFieldAlias, out int preferredTargetId) &&
                                         preferredTargetId == node.Id
                            ? preferredFieldAlias
                            : ResolveFreshObject(node);
                    }
                    if (!matchedByExactParentSlot &&
                        restoredObject != null && restoredObject.GetType() == type &&
                        TryResolveFreshFieldAlias(
                            node,
                            type,
                            out object exactFieldAlias,
                            out bool exactTypedAlias) &&
                        exactTypedAlias) {
                        restoredObject = exactFieldAlias;
                    }
                    if (!matchedByExactParentSlot && restoredObject != null &&
                        freshFieldAliasReservations.TryGetValue(restoredObject, out int reservedNodeId) &&
                        reservedNodeId != node.Id) {
                        restoredObject = null;
                    }
                    if (node.Kind == AnchorKind &&
                        !string.IsNullOrWhiteSpace(node.ResourceKey) &&
                        (restoredObject == null ||
                         restoredObject.GetType() != type ||
                         !string.Equals(node.ResourceKey, owner.GetTypedResourceKey(restoredObject), StringComparison.Ordinal))) {
                        restoredObject = FindFreshResource(node, type, out resourceMatchedByDynamicOwnerPath);
                    }
                    if (node.Kind == ArrayKind && restoredObject is Array freshArray && !ArrayShapeMatches(freshArray, node)) {
                        restoredObject = null;
                    }
                    if (node.Kind != AnchorKind && restoredObject != null && freshOwners.ContainsKey(restoredObject)) {
                        // A normal reload may intern or share an ordinary wrapper
                        // that was distinct in the saved graph. Reconstruct this
                        // node so the saved reference identity wins.
                        restoredObject = null;
                    }
                    if (restoredObject is Entity restoredEntity &&
                        !SavedEntitySourceMatches(node, restoredEntity)) {
                        restoredObject = null;
                    }
                    if ((restoredObject == null || restoredObject.GetType() != type) &&
                        TryResolveFreshOwnedComponent(node, type, out Component ownedComponent)) {
                        restoredObject = ownedComponent;
                    }
                    if ((restoredObject == null || restoredObject.GetType() != type) &&
                        TryResolveFreshOwnedEntity(node, type, out Entity ownedEntity)) {
                        restoredObject = ownedEntity;
                    }
                    if ((restoredObject == null || restoredObject.GetType() != type) &&
                        TryResolveFreshFieldAlias(node, type, out object fieldAlias)) {
                        restoredObject = fieldAlias;
                    }
                    if ((restoredObject == null || restoredObject.GetType() != type) && node.Kind == AnchorKind) {
                        throw new AkronReconstructionException(node.Path, "fresh object is missing or has a different type");
                    }
                    if (node.Kind == AnchorKind &&
                        !resourceMatchedByDynamicOwnerPath &&
                        !string.IsNullOrWhiteSpace(node.ResourceKey) &&
                        !string.Equals(node.ResourceKey, owner.GetTypedResourceKey(restoredObject), StringComparison.Ordinal)) {
                        throw new AkronReconstructionException(node.Path, "fresh resource identity differs;key=" + node.ResourceKey);
                    }
                    if (restoredObject != null && restoredObject.GetType() == type) {
                        resolvedFreshObjectNodes.Add(node.Id);
                    }
                    if (restoredObject == null || restoredObject.GetType() != type) {
                        restoredObject = CreateAuthenticatedObject(node, type);
                    }
                } else if (TryResolveFreshExactParentSlot(node, type, out object lateParentObject)) {
                    restoredObject = lateParentObject;
                    resolvedFreshObjectNodes.Add(node.Id);
                } else if (TryResolveFreshFieldAlias(node, type, out object lateFieldAlias)) {
                    restoredObject = lateFieldAlias;
                    resolvedFreshObjectNodes.Add(node.Id);
                } else if (TryResolveFreshOwnedComponent(node, type, out Component lateOwnedComponent)) {
                    restoredObject = lateOwnedComponent;
                    resolvedFreshObjectNodes.Add(node.Id);
                } else if (TryResolveFreshOwnedEntity(node, type, out Entity lateOwnedEntity)) {
                    restoredObject = lateOwnedEntity;
                    resolvedFreshObjectNodes.Add(node.Id);
                } else if (node.Kind == DelegateKind || node.Kind == EventInstanceKind) {
                    continue;
                } else if (node.Kind == ArrayKind) {
                    restoredObject = CreateAuthenticatedObject(node, type);
                } else {
                    restoredObject = CreateAuthenticatedObject(node, type);
                }

                if (!type.IsValueType && restoredObject != null && freshOwners.TryGetValue(restoredObject, out int ownerId)) {
                    throw new AkronReconstructionException(node.Path, "fresh object is already paired with node " + ownerId.ToString(CultureInfo.InvariantCulture));
                }
                if (!type.IsValueType && restoredObject != null) {
                    freshOwners[restoredObject] = node.Id;
                }
                Objects[node.Id] = restoredObject;
            }

            ValidateReferenceAuthenticity();
            foreach (AkronReconstructionNode node in document.Nodes.Where(node => node.Kind == DelegateKind).OrderBy(node => node.Id)) {
                Objects[node.Id] = CreateDelegate(node);
            }
            foreach (AkronReconstructionNode node in document.Nodes.Where(node => node.Kind == EventInstanceKind).OrderBy(node => node.Id)) {
                EventInstance eventInstance = AkronEventInstanceUtils.RestorePersistentState(node.EventInstance);
                if (eventInstance == null) {
                    throw new AkronReconstructionException(node.Path, "FMOD event could not be recreated from its event path");
                }
                Objects[node.Id] = eventInstance;
            }
        }

        public void CommitPersistentResources() {
            foreach (object resource in replacedPersistentResources.Distinct(ReferenceEqualityComparer.Instance)) {
                owner.ReleaseOwnedPersistentResource(resource);
            }
            createdPersistentResources.Clear();
            replacedPersistentResources.Clear();
        }

        public void ReleaseCreatedPersistentResources() {
            foreach (object resource in createdPersistentResources.Distinct(ReferenceEqualityComparer.Instance)) {
                owner.ReleaseOwnedPersistentResource(resource);
            }
            createdPersistentResources.Clear();
            replacedPersistentResources.Clear();
        }

        public void ReleaseDisplacedEventInstances() {
            AkronEventInstanceUtils.ReleaseEventInstances(displacedEventInstances);
            displacedEventInstances.Clear();
        }

        private void IndexFreshResources(
            object value,
            List<AkronReconstructionPathStep> path,
            HashSet<object> visited
        ) {
            if (value == null) {
                return;
            }

            Type type = value.GetType();
            if (IsScalarType(type) || type == typeof(IntPtr) || type == typeof(UIntPtr) ||
                type.IsPointer || type.IsByRefLike) {
                return;
            }
            if (value is Delegate freshDelegate) {
                if (!visited.Add(value)) {
                    return;
                }
                foreach (Delegate call in freshDelegate.GetInvocationList()) {
                    string methodKey = DelegateMethodKey(call.Method);
                    if (call.Target == null) {
                        freshStaticDelegateMethods.Add(methodKey);
                    } else {
                        freshStructuralDelegateCalls.Add(
                            StructuralDelegateCallKey(path, call.Target.GetType(), call.Method));
                        if (!freshInstanceDelegateMethods.TryGetValue(call.Target, out HashSet<string> methods)) {
                            methods = new HashSet<string>(StringComparer.Ordinal);
                            freshInstanceDelegateMethods[call.Target] = methods;
                        }
                        methods.Add(methodKey);
                        // Capture serializes delegate targets at the owning
                        // delegate path. Follow the same path here so nested
                        // callbacks inside closure state can be authenticated.
                        IndexFreshResources(call.Target, path, visited);
                    }
                }
                return;
            }
            if (owner.isLiveResource(type)) {
                if (!visited.Add(value)) {
                    return;
                }
                string key = owner.GetTypedResourceKey(value);
                if (!string.IsNullOrWhiteSpace(key)) {
                    if (!freshResources.TryGetValue(key, out List<object> matches)) {
                        matches = new List<object>();
                        freshResources[key] = matches;
                    }
                    matches.Add(value);
                }
                string structuralPathKey = StructuralResourcePathKey(
                    type,
                    path,
                    wildcardListStorageIndices: true);
                if (!freshResourcesByStructuralPath.TryGetValue(structuralPathKey, out List<object> structuralMatches)) {
                    structuralMatches = new List<object>();
                    freshResourcesByStructuralPath[structuralPathKey] = structuralMatches;
                }
                structuralMatches.Add(value);
                return;
            }

            // Only gameplay objects need structural authenticity. Arrays,
            // value types, and collection wrappers already have explicit safe
            // reconstruction contracts. Do not retain one long path string
            // for each scalar or texture-grid cell in a large room.
            bool explicitlySafe = IsExplicitlySafeReconstructionType(type);
            bool firstVisit = true;
            if (!type.IsValueType) {
                firstVisit = visited.Add(value);
            }
            if (!explicitlySafe) {
                freshStructuralTypes.Add(StructuralResourcePathKey(type, path));
                if (HasListStorageIndex(path)) {
                    string listPathKey = StructuralResourcePathKey(
                        type,
                        path,
                        wildcardListStorageIndices: true);
                    freshListStructuralTypeCounts.TryGetValue(listPathKey, out int count);
                    freshListStructuralTypeCounts[listPathKey] = count + 1;
                }
            }
            bool trackActiveSafeObject = false;
            if (!type.IsValueType) {
                if (!firstVisit && !explicitlySafe) {
                    return;
                }
                if (explicitlySafe) {
                    if (!activeFreshSafeObjects.Add(value)) {
                        return;
                    }
                    trackActiveSafeObject = true;
                }
            }

            try {
                if (value is Array array) {
                    int[] indices = GetInitialArrayIndices(array);
                    for (int index = 0; index < array.Length; index++) {
                        object item = array.GetValue(indices);
                        if (ShouldIndexFreshEdge(item, visited)) {
                            path.Add(new AkronReconstructionPathStep {
                                Kind = "array",
                                ArrayIndices = indices.ToList()
                            });
                            IndexFreshResources(item, path, visited);
                            path.RemoveAt(path.Count - 1);
                        }
                        IncrementArrayIndices(array, indices);
                    }
                    return;
                }

                foreach (FieldInfo field in GetInstanceFields(type)) {
                    object fieldValue = field.GetValue(value);
                    if (!ShouldIndexFreshEdge(fieldValue, visited)) {
                        continue;
                    }
                    path.Add(new AkronReconstructionPathStep {
                        Kind = "field",
                        DeclaringTypeName = TypeName(field.DeclaringType),
                        FieldName = field.Name
                    });
                    IndexFreshResources(fieldValue, path, visited);
                    path.RemoveAt(path.Count - 1);
                }
            } finally {
                if (trackActiveSafeObject) {
                    activeFreshSafeObjects.Remove(value);
                }
            }
        }

        private bool ShouldIndexFreshEdge(object value, HashSet<object> visited) {
            if (value == null) {
                return false;
            }
            Type type = value.GetType();
            if (IsScalarType(type) || type == typeof(IntPtr) || type == typeof(UIntPtr) ||
                type.IsPointer || type.IsByRefLike) {
                return false;
            }
            if (type.IsValueType || !visited.Contains(value)) {
                return true;
            }
            return value is not Delegate && !owner.isLiveResource(type);
        }

        private object CreateAuthenticatedObject(AkronReconstructionNode node, Type type) {
            bool authenticatedIteratorState = IsAuthenticatedCompilerIteratorState(node, type);
            bool authenticatedRuntimeEntity = IsAuthenticatedBuiltInRuntimeEntity(node, type);
            bool authenticatedOwnedNestedState =
                IsAuthenticatedFreshEntityOwnedNestedState(node, type) ||
                IsAuthenticatedFreshRendererOwnedRuntimeState(node, type) ||
                IsAuthenticatedRuntimeEntityOwnedState(node, type);
            bool authenticatedOwnedComponent = IsAuthenticatedReconstructedOwnedComponent(node, type);
            bool authenticatedDelegateTarget = IsStructurallyAuthenticDelegateTarget(node, type);
            bool authenticatedIteratorClosure = IsAuthenticatedIteratorClosure(node, type);
            bool authenticatedScreenWipe = IsAuthenticatedBuiltInScreenWipe(node, type);
            if (authenticatedIteratorState) {
                authenticatedRuntimeStateNodes.Add(node.Id);
            }
            if (authenticatedRuntimeEntity) {
                authenticatedRuntimeEntityNodes.Add(node.Id);
            }
            if (authenticatedOwnedNestedState) {
                authenticatedOwnedNestedStateNodes.Add(node.Id);
            }
            if (authenticatedOwnedComponent) {
                authenticatedOwnedComponentNodes.Add(node.Id);
            }
            if (authenticatedDelegateTarget) {
                authenticatedDelegateTargetNodes.Add(node.Id);
            }
            if (authenticatedIteratorClosure) {
                authenticatedIteratorClosureNodes.Add(node.Id);
            }
            if (authenticatedScreenWipe) {
                authenticatedScreenWipeNodes.Add(node.Id);
            }
            if (!IsExplicitlySafeReconstructionType(type) &&
                !authenticatedIteratorState &&
                !authenticatedRuntimeEntity &&
                !authenticatedOwnedNestedState &&
                !authenticatedOwnedComponent &&
                !authenticatedScreenWipe) {
                List<AkronReconstructionPathStep> structuralPath = GetDocumentStructuralPath(node);
                string typePathKey = StructuralResourcePathKey(type, structuralPath);
                string listTypePathKey = StructuralResourcePathKey(
                    type,
                    structuralPath,
                    wildcardListStorageIndices: true);
                bool listTypeIsAvailable = HasListStorageIndex(structuralPath) &&
                                           freshListStructuralTypeCounts.ContainsKey(listTypePathKey);
                bool exactTypeIsAvailable = freshStructuralTypes.Contains(typePathKey);
                if ((structuralPath.Count == 0 || !exactTypeIsAvailable && !listTypeIsAvailable) &&
                    !IsAuthenticatedByExactParentSlot(node, type) &&
                    !authenticatedDelegateTarget) {
                    throw new AkronReconstructionException(
                        node.Path,
                        "reconstructed type is not authentic to the fresh room;type=" + type.FullName +
                        ";path-depth=" + structuralPath.Count.ToString(CultureInfo.InvariantCulture) +
                        ";list-path=" + HasListStorageIndex(structuralPath).ToString().ToLowerInvariant() +
                        ";exact-match=" + exactTypeIsAvailable.ToString().ToLowerInvariant() +
                        ";list-match=" + listTypeIsAvailable.ToString().ToLowerInvariant());
                }
            }
            if (node.Kind == ArrayKind) {
                return CreateArray(type, node, node.Path);
            }
            // These two built-in visual entities own constructor-created
            // ComponentList state. Construct them normally so their fresh
            // components can authenticate the saved ownership graph.
            if (type == typeof(TrailManager) || type == typeof(TrailManager.Snapshot)) {
                return Activator.CreateInstance(type, nonPublic: true);
            }
            if (type == typeof(SoundEmitter)) {
                string eventName = GetSavedSoundEmitterEventName(node, nodes);
                return Activator.CreateInstance(
                    type,
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    args: new object[] { eventName },
                    culture: CultureInfo.InvariantCulture);
            }
            return RuntimeHelpers.GetUninitializedObject(type);
        }

        private bool IsAuthenticatedCompilerIteratorState(AkronReconstructionNode node, Type type) {
            if (!typeof(IEnumerator).IsAssignableFrom(type) ||
                type.GetCustomAttribute<CompilerGeneratedAttribute>() == null ||
                type.DeclaringType == null) {
                return false;
            }
            AkronReconstructionValue ownerReference = FindReferenceField(node, "<>4__this");
            return ownerReference != null &&
                   resolvedFreshObjectNodes.Contains(ownerReference.NodeId) &&
                   Objects.TryGetValue(ownerReference.NodeId, out object ownerObject) &&
                   ownerObject?.GetType() == type.DeclaringType;
        }

        private bool IsAuthenticatedIteratorClosure(AkronReconstructionNode node, Type type) {
            if (node.ParentKind != "field" ||
                !authenticatedRuntimeStateNodes.Contains(node.ParentNodeId) ||
                !AkronReconstructionGraph.IsCompilerClosureIteratorLocal(
                    type,
                    node.ParentFieldName) ||
                !nodes.TryGetValue(node.ParentNodeId, out AkronReconstructionNode iteratorNode)) {
                return false;
            }
            FieldInfo localField = ResolveField(
                node.ParentDeclaringTypeName,
                node.ParentFieldName,
                node.Path);
            return localField.FieldType == type &&
                   typeof(IEnumerator).IsAssignableFrom(
                       ResolveType(iteratorNode.TypeName, iteratorNode.Path));
        }

        private bool IsAuthenticatedByExactParentSlot(AkronReconstructionNode node, Type type) {
            if (node.ParentKind == "array" &&
                nodes.TryGetValue(node.ParentNodeId, out AkronReconstructionNode arrayParent) &&
                Objects.TryGetValue(node.ParentNodeId, out object parentObject) &&
                parentObject is Array freshArray &&
                HasArrayIndex(freshArray, node.ParentArrayIndices)) {
                Type arrayType = ResolveType(arrayParent.TypeName, arrayParent.Path);
                if (!arrayType.IsArray) {
                    return false;
                }
                Type elementType = arrayType.GetElementType();
                object freshItem = freshArray.GetValue(node.ParentArrayIndices.ToArray());
            return (elementType == type && freshItem != null) ||
                   (elementType.IsAssignableFrom(type) && freshItem?.GetType() == type);
            }
            if (node.ParentKind == "field" &&
                Objects.TryGetValue(node.ParentNodeId, out object fieldParent)) {
                FieldInfo field = ResolveField(
                    node.ParentDeclaringTypeName,
                    node.ParentFieldName,
                    node.Path);
                return field.FieldType == type &&
                       field.DeclaringType.IsInstanceOfType(fieldParent) &&
                       field.GetValue(fieldParent) != null;
            }
            return false;
        }

        private bool TryResolveFreshExactParentSlot(
            AkronReconstructionNode node,
            Type type,
            out object matchedObject
        ) {
            matchedObject = null;
            if (typeof(Entity).IsAssignableFrom(type) ||
                typeof(Component).IsAssignableFrom(type) ||
                !Objects.TryGetValue(node.ParentNodeId, out object parentObject)) {
                return false;
            }

            if (node.ParentKind == "field") {
                FieldInfo field = ResolveField(
                    node.ParentDeclaringTypeName,
                    node.ParentFieldName,
                    node.Path);
                if (field.FieldType == type && field.DeclaringType.IsInstanceOfType(parentObject)) {
                    matchedObject = field.GetValue(parentObject);
                }
            } else if (node.ParentKind == "array" &&
                       parentObject is Array array &&
                       array.GetType().GetElementType().IsAssignableFrom(type) &&
                       HasArrayIndex(array, node.ParentArrayIndices)) {
                matchedObject = array.GetValue(node.ParentArrayIndices.ToArray());
            }

            if (matchedObject == null || matchedObject.GetType() != type ||
                freshOwners.ContainsKey(matchedObject) ||
                node.Kind == ArrayKind &&
                (matchedObject is not Array matchedArray || !ArrayShapeMatches(matchedArray, node))) {
                matchedObject = null;
                return false;
            }
            return true;
        }

        private void ValidateReferenceAuthenticity() {
            foreach (AkronReconstructionNode parent in document.Nodes) {
                List<AkronReconstructionPathStep> parentPath = null;
                foreach (AkronReconstructionField field in parent.Fields ?? Enumerable.Empty<AkronReconstructionField>()) {
                    if (field?.Value?.Kind != ReferenceValueKind) {
                        continue;
                    }
                    parentPath ??= GetDocumentStructuralPath(parent);
                    ValidateReferenceEdge(
                        field.Value,
                        AppendFreshPath(parentPath, new AkronReconstructionPathStep {
                            Kind = "field",
                            DeclaringTypeName = field.DeclaringTypeName ?? string.Empty,
                            FieldName = field.Name ?? string.Empty
                        }),
                        parent,
                        field);
                }
                if (parent.Kind != ArrayKind || parent.PackedPrimitiveArrayBytes != null ||
                    !Objects.TryGetValue(parent.Id, out object restoredArray) || restoredArray is not Array array) {
                    continue;
                }

                // Large object arrays often contain mostly null or scalar
                // values. Only references need an authenticated edge. Delay
                // the path work so sparse arrays do not allocate one path and
                // one index array for every empty slot.
                int itemCount = Math.Min(parent.Items.Count, array.Length);
                for (int itemIndex = 0; itemIndex < itemCount; itemIndex++) {
                    AkronReconstructionValue item = parent.Items[itemIndex];
                    if (item?.Kind != ReferenceValueKind) {
                        continue;
                    }
                    parentPath ??= GetDocumentStructuralPath(parent);
                    ValidateReferenceEdge(
                        item,
                        AppendFreshPath(parentPath, new AkronReconstructionPathStep {
                            Kind = "array",
                            ArrayIndices = GetArrayIndicesAtFlatIndex(array, itemIndex).ToList()
                        }),
                        parent,
                        null);
                }
            }
        }

        private void ValidateReferenceEdge(
            AkronReconstructionValue value,
            List<AkronReconstructionPathStep> edgePath,
            AkronReconstructionNode edgeParent,
            AkronReconstructionField edgeField
        ) {
            if (value?.Kind != ReferenceValueKind || !nodes.TryGetValue(value.NodeId, out AkronReconstructionNode target) ||
                target.Kind is AnchorKind or PersistentResourceKind or DelegateKind or EventInstanceKind) {
                return;
            }
            Type targetType = ResolveType(target.TypeName, target.Path);
            if (IsExplicitlySafeReconstructionType(targetType) ||
                authenticatedScreenWipeNodes.Contains(target.Id)) {
                return;
            }

            List<AkronReconstructionPathStep> structuralPath = edgePath;
            bool savedOwnerEdge = IsExactSavedOwnerEdge(target, targetType, edgePath);
            bool exactParentSlot = IsAuthenticatedByExactParentSlot(target, targetType);
            bool freshComponentOwner = IsAuthenticatedFreshComponentOwner(target, targetType);
            bool freshComponentAlias = freshComponentOwner &&
                                       IsAuthenticatedFreshComponentAlias(target, edgeParent, edgeField);
            bool freshComponentTrackerAlias = freshComponentOwner &&
                                              IsAuthenticatedFreshComponentTrackerAlias(target, edgeParent);
            bool authenticatedOwnedComponent =
                authenticatedOwnedComponentNodes.Contains(target.Id) ||
                IsAuthenticatedReconstructedOwnedComponent(target, targetType);
            bool authenticatedOwnedComponentTrackerAlias =
                authenticatedOwnedComponent &&
                IsAuthenticatedFreshComponentTrackerAlias(target, edgeParent);
            bool freshArrayMembershipAlias = IsAuthenticatedFreshArrayMembershipAlias(
                target,
                targetType,
                edgeParent,
                edgeField);
            bool freshRendererComponentIndexAlias = IsAuthenticatedFreshRendererComponentIndexAlias(
                target,
                targetType,
                edgeParent,
                edgeField);
            Type edgeParentType = ResolveType(edgeParent.TypeName, edgeParent.Path);
            bool authenticatedEdgeParentOwnedComponent =
                authenticatedOwnedComponentNodes.Contains(edgeParent.Id) ||
                IsAuthenticatedReconstructedOwnedComponent(edgeParent, edgeParentType);
            bool freshComponentCapturedFreshEdge = edgeField != null &&
                                                   resolvedFreshObjectNodes.Contains(target.Id) &&
                                                   IsAuthenticatedFreshComponentOwner(edgeParent, edgeParentType) &&
                                                   Objects.TryGetValue(edgeParent.Id, out object freshComponentObject) &&
                                                   Objects.TryGetValue(target.Id, out object freshCapturedObject) &&
                                                   ResolveField(
                                                       edgeField.DeclaringTypeName,
                                                       edgeField.Name,
                                                       edgeField.Path).FieldType == targetType &&
                                                   freshComponentObject?.GetType() == edgeParentType &&
                                                   freshCapturedObject?.GetType() == targetType;
            bool trailSnapshotPlayerComponentAlias = freshComponentOwner &&
                                                     IsAuthenticatedTrailSnapshotPlayerComponentAlias(
                                                         target,
                                                         targetType,
                                                         edgeParent,
                                                         edgeField);
            bool reconstructedBuiltInComponentAlias =
                IsAuthenticatedReconstructedBuiltInComponentAlias(
                    target,
                    targetType,
                    edgeParent,
                    edgeField);
            bool authenticatedBuiltInRuntimeEntity =
                authenticatedRuntimeEntityNodes.Contains(target.Id) ||
                IsAuthenticatedBuiltInRuntimeEntity(target, targetType);
            bool authenticatedRuntimeEntityTrackerAlias =
                authenticatedBuiltInRuntimeEntity &&
                IsAuthenticatedRuntimeEntityTrackerAlias(target, targetType, edgeParent);
            bool freshEntityListAlias = IsAuthenticatedEntityListAlias(
                target,
                targetType,
                edgeParent,
                exactParentSlot,
                authenticatedBuiltInRuntimeEntity);
            bool freshEntityPeerLink = IsAuthenticatedFreshEntityPeerLink(
                target,
                targetType,
                edgeParent,
                edgeField);
            bool freshEntityOwnedCollectionAlias =
                IsAuthenticatedFreshEntityOwnedCollectionAlias(target, targetType, edgeParent);
            bool freshOwnedNestedState =
                authenticatedOwnedNestedStateNodes.Contains(target.Id) &&
                target.ParentNodeId == edgeParent.Id &&
                (target.ParentKind == "field" ||
                 (target.ParentKind == "array" && edgeField == null));
            bool reconstructedOwnedComponentAlias =
                authenticatedOwnedComponent &&
                IsAuthenticatedOwnedComponentAlias(target, edgeParent);
            bool reconstructedOwnedComponentOwnerEdge =
                authenticatedEdgeParentOwnedComponent &&
                edgeField?.Name == "<Entity>k__BackingField" &&
                TryGetComponentOwnerNodes(edgeParent, out _, out int reconstructedComponentOwnerId) &&
                reconstructedComponentOwnerId == target.Id &&
                (resolvedFreshObjectNodes.Contains(target.Id) ||
                 authenticatedBuiltInRuntimeEntity);
            bool freshFieldAlias = IsAuthenticatedFreshFieldAlias(target, edgeParent, edgeField);
            bool freshOwnerAliasMerge = IsAuthenticatedFreshOwnerAliasMerge(
                target,
                targetType,
                edgeParent,
                edgeField);
            bool freshSceneEntityAlias = IsAuthenticatedFreshSceneEntityAlias(
                targetType,
                edgeParent,
                edgeField,
                exactParentSlot);
            bool entityComponentListBackReference = IsAuthenticatedEntityComponentListBackReference(
                target,
                targetType,
                edgeParent,
                edgeField,
                exactParentSlot);
            bool sceneRendererBackReference = IsAuthenticatedSceneRendererBackReference(
                target,
                targetType,
                edgeParent,
                edgeField);
            bool reconstructedEntitySceneBackReference =
                IsAuthenticatedReconstructedEntitySceneBackReference(
                    target,
                    targetType,
                    edgeParent,
                    edgeField);
            bool freshHashSetMembership = IsAuthenticatedFreshHashSetMembership(target, edgeParent);
            bool authenticatedIteratorOwnerEdge = edgeField?.Name == "<>4__this" &&
                                                   authenticatedRuntimeStateNodes.Contains(edgeParent.Id) &&
                                                   edgeParentType.DeclaringType == targetType &&
                                                   resolvedFreshObjectNodes.Contains(target.Id);
            bool authenticatedDelegateTargetOwnerEdge = edgeField?.Name == "<>4__this" &&
                                                        authenticatedDelegateTargetNodes.Contains(edgeParent.Id) &&
                                                        edgeParentType.DeclaringType == targetType &&
                                                        resolvedFreshObjectNodes.Contains(target.Id);
            bool authenticatedDelegateAliasOwnerEdge = edgeField?.Name == "<>4__this" &&
                                                       edgeParentType.DeclaringType == targetType &&
                                                       resolvedFreshObjectNodes.Contains(target.Id) &&
                                                       IsSavedDelegateTargetAlias(
                                                           edgeParent.Id,
                                                           edgeParentType);
            bool authenticatedDelegateCapturedFreshEdge = edgeField != null &&
                                                          resolvedFreshObjectNodes.Contains(target.Id) &&
                                                          (authenticatedDelegateTargetNodes.Contains(edgeParent.Id) ||
                                                           IsSavedDelegateTargetAlias(edgeParent.Id, edgeParentType)) &&
                                                          Objects.TryGetValue(edgeParent.Id, out object delegateTargetObject) &&
                                                          Objects.TryGetValue(target.Id, out object capturedFreshObject) &&
                                                          ResolveField(
                                                              edgeField.DeclaringTypeName,
                                                              edgeField.Name,
                                                              edgeField.Path).FieldType == targetType &&
                                                          capturedFreshObject?.GetType() == targetType &&
                                                          edgeParentType.IsInstanceOfType(delegateTargetObject);
            bool authenticatedIteratorClosureOwnerEdge = edgeField?.Name == "<>4__this" &&
                                                         authenticatedIteratorClosureNodes.Contains(edgeParent.Id) &&
                                                         edgeParentType.DeclaringType == targetType &&
                                                         resolvedFreshObjectNodes.Contains(target.Id);
            bool authenticatedTarget = resolvedFreshObjectNodes.Contains(target.Id) ||
                                       authenticatedRuntimeStateNodes.Contains(target.Id) ||
                                       authenticatedRuntimeEntityNodes.Contains(target.Id) ||
                                       authenticatedOwnedNestedStateNodes.Contains(target.Id) ||
                                       authenticatedScreenWipeNodes.Contains(target.Id) ||
                                       authenticatedOwnedComponent;
            bool edgeParentIsFresh = resolvedFreshObjectNodes.Contains(edgeParent.Id);
            bool hasReservedFreshFieldAlias = freshFieldAliasesByNode.ContainsKey(target.Id);
            string reservedFreshFieldSource = freshFieldAliasSourcesByNode.TryGetValue(
                    target.Id,
                    out (int ParentId, string DeclaringTypeName, string FieldName) reservedSource)
                ? reservedSource.ParentId.ToString(CultureInfo.InvariantCulture) + ":" + reservedSource.FieldName
                : "none";
            bool reconstructedSafeParentEdge = savedOwnerEdge && authenticatedTarget &&
                                               !resolvedFreshObjectNodes.Contains(edgeParent.Id) &&
                                               (IsExplicitlySafeReconstructionType(edgeParentType) ||
                                                authenticatedRuntimeStateNodes.Contains(edgeParent.Id) ||
                                               authenticatedRuntimeEntityNodes.Contains(edgeParent.Id) ||
                                               authenticatedOwnedNestedStateNodes.Contains(edgeParent.Id) ||
                                               authenticatedScreenWipeNodes.Contains(edgeParent.Id) ||
                                               authenticatedEdgeParentOwnedComponent);
            bool exactOwnerEdge = (savedOwnerEdge && (exactParentSlot || freshComponentOwner)) ||
                                  freshComponentAlias || freshComponentTrackerAlias ||
                                  authenticatedOwnedComponentTrackerAlias ||
                                  authenticatedRuntimeEntityTrackerAlias ||
                                  freshArrayMembershipAlias ||
                                  freshRendererComponentIndexAlias ||
                                  freshEntityListAlias || freshEntityPeerLink ||
                                  freshComponentCapturedFreshEdge ||
                                  freshEntityOwnedCollectionAlias ||
                                  freshOwnedNestedState || reconstructedOwnedComponentAlias || freshFieldAlias ||
                                  reconstructedOwnedComponentOwnerEdge ||
                                  freshOwnerAliasMerge ||
                                  trailSnapshotPlayerComponentAlias ||
                                  reconstructedBuiltInComponentAlias ||
                                  freshSceneEntityAlias || entityComponentListBackReference ||
                                  sceneRendererBackReference || reconstructedEntitySceneBackReference ||
                                  freshHashSetMembership ||
                                  reconstructedSafeParentEdge || authenticatedIteratorOwnerEdge ||
                                  authenticatedDelegateTargetOwnerEdge ||
                                  authenticatedDelegateAliasOwnerEdge ||
                                  authenticatedDelegateCapturedFreshEdge ||
                                  authenticatedIteratorClosureOwnerEdge;
            if (HasListStorageIndex(structuralPath)) {
                string listPathKey = StructuralResourcePathKey(
                    targetType,
                    structuralPath,
                    wildcardListStorageIndices: true);
                if (!freshListStructuralTypeCounts.TryGetValue(listPathKey, out int remaining) || remaining <= 0) {
                    if (exactOwnerEdge) {
                        return;
                    }
                    throw new AkronReconstructionException(
                        target.Path,
                        "reconstructed reference edge is not authentic to the fresh room;type=" + targetType.FullName +
                        ";fresh-node=" + resolvedFreshObjectNodes.Contains(target.Id).ToString().ToLowerInvariant() +
                        ";edge-parent-fresh=" + edgeParentIsFresh.ToString().ToLowerInvariant() +
                        ";reserved-fresh-field-alias=" + hasReservedFreshFieldAlias.ToString().ToLowerInvariant() +
                        ";reserved-fresh-field-source=" + reservedFreshFieldSource +
                        ";saved-owner-edge=" + savedOwnerEdge.ToString().ToLowerInvariant() +
                        ";exact-parent-slot=" + exactParentSlot.ToString().ToLowerInvariant() +
                        ";fresh-component-owner=" + freshComponentOwner.ToString().ToLowerInvariant() +
                        ";fresh-component-alias=" + freshComponentAlias.ToString().ToLowerInvariant() +
                        ";fresh-component-tracker-alias=" + freshComponentTrackerAlias.ToString().ToLowerInvariant() +
                        ";authenticated-owned-component-tracker-alias=" + authenticatedOwnedComponentTrackerAlias.ToString().ToLowerInvariant() +
                        ";authenticated-runtime-entity-tracker-alias=" + authenticatedRuntimeEntityTrackerAlias.ToString().ToLowerInvariant() +
                        ";fresh-array-membership-alias=" + freshArrayMembershipAlias.ToString().ToLowerInvariant() +
                        ";fresh-renderer-component-index-alias=" + freshRendererComponentIndexAlias.ToString().ToLowerInvariant() +
                        ";fresh-component-captured-fresh-edge=" + freshComponentCapturedFreshEdge.ToString().ToLowerInvariant() +
                        ";trail-snapshot-player-component-alias=" + trailSnapshotPlayerComponentAlias.ToString().ToLowerInvariant() +
                        ";reconstructed-built-in-component-alias=" + reconstructedBuiltInComponentAlias.ToString().ToLowerInvariant() +
                        ";authenticated-built-in-runtime-entity=" + authenticatedBuiltInRuntimeEntity.ToString().ToLowerInvariant() +
                        ";fresh-entity-list-alias=" + freshEntityListAlias.ToString().ToLowerInvariant() +
                        ";fresh-entity-peer-link=" + freshEntityPeerLink.ToString().ToLowerInvariant() +
                        ";fresh-entity-owned-collection-alias=" + freshEntityOwnedCollectionAlias.ToString().ToLowerInvariant() +
                        ";fresh-owned-nested-state=" + freshOwnedNestedState.ToString().ToLowerInvariant() +
                        ";reconstructed-owned-component-alias=" + reconstructedOwnedComponentAlias.ToString().ToLowerInvariant() +
                        ";reconstructed-owned-component-owner-edge=" + reconstructedOwnedComponentOwnerEdge.ToString().ToLowerInvariant() +
                        ";fresh-field-alias=" + freshFieldAlias.ToString().ToLowerInvariant() +
                        ";fresh-owner-alias-merge=" + freshOwnerAliasMerge.ToString().ToLowerInvariant() +
                        ";fresh-scene-entity-alias=" + freshSceneEntityAlias.ToString().ToLowerInvariant() +
                        ";entity-component-list-back-reference=" + entityComponentListBackReference.ToString().ToLowerInvariant() +
                        ";scene-renderer-back-reference=" + sceneRendererBackReference.ToString().ToLowerInvariant() +
                        ";reconstructed-entity-scene-back-reference=" + reconstructedEntitySceneBackReference.ToString().ToLowerInvariant() +
                        ";fresh-hash-set-membership=" + freshHashSetMembership.ToString().ToLowerInvariant() +
                        ";reconstructed-safe-parent-edge=" + reconstructedSafeParentEdge.ToString().ToLowerInvariant() +
                        ";authenticated-iterator-owner-edge=" + authenticatedIteratorOwnerEdge.ToString().ToLowerInvariant() +
                        ";authenticated-delegate-target-owner-edge=" + authenticatedDelegateTargetOwnerEdge.ToString().ToLowerInvariant() +
                        ";authenticated-delegate-alias-owner-edge=" + authenticatedDelegateAliasOwnerEdge.ToString().ToLowerInvariant() +
                        ";authenticated-delegate-captured-fresh-edge=" + authenticatedDelegateCapturedFreshEdge.ToString().ToLowerInvariant() +
                        ";authenticated-iterator-closure-owner-edge=" + authenticatedIteratorClosureOwnerEdge.ToString().ToLowerInvariant() +
                        ";edge-parent-type=" + edgeParent.TypeName +
                        ";edge-field=" + (edgeField?.Name ?? "<array>"));
                }
                freshListStructuralTypeCounts[listPathKey] = remaining - 1;
                return;
            }

            string typePathKey = StructuralResourcePathKey(targetType, structuralPath);
            if (!freshStructuralTypes.Contains(typePathKey)) {
                if (exactOwnerEdge) {
                    return;
                }
                throw new AkronReconstructionException(
                    target.Path,
                    "reconstructed reference edge is not authentic to the fresh room;type=" + targetType.FullName +
                    ";fresh-node=" + resolvedFreshObjectNodes.Contains(target.Id).ToString().ToLowerInvariant() +
                    ";edge-parent-fresh=" + edgeParentIsFresh.ToString().ToLowerInvariant() +
                    ";reserved-fresh-field-alias=" + hasReservedFreshFieldAlias.ToString().ToLowerInvariant() +
                    ";reserved-fresh-field-source=" + reservedFreshFieldSource +
                    ";saved-owner-edge=" + savedOwnerEdge.ToString().ToLowerInvariant() +
                    ";exact-parent-slot=" + exactParentSlot.ToString().ToLowerInvariant() +
                    ";fresh-component-owner=" + freshComponentOwner.ToString().ToLowerInvariant() +
                    ";fresh-component-alias=" + freshComponentAlias.ToString().ToLowerInvariant() +
                    ";fresh-component-tracker-alias=" + freshComponentTrackerAlias.ToString().ToLowerInvariant() +
                    ";authenticated-owned-component-tracker-alias=" + authenticatedOwnedComponentTrackerAlias.ToString().ToLowerInvariant() +
                    ";authenticated-runtime-entity-tracker-alias=" + authenticatedRuntimeEntityTrackerAlias.ToString().ToLowerInvariant() +
                    ";fresh-array-membership-alias=" + freshArrayMembershipAlias.ToString().ToLowerInvariant() +
                    ";fresh-renderer-component-index-alias=" + freshRendererComponentIndexAlias.ToString().ToLowerInvariant() +
                    ";fresh-component-captured-fresh-edge=" + freshComponentCapturedFreshEdge.ToString().ToLowerInvariant() +
                    ";trail-snapshot-player-component-alias=" + trailSnapshotPlayerComponentAlias.ToString().ToLowerInvariant() +
                    ";reconstructed-built-in-component-alias=" + reconstructedBuiltInComponentAlias.ToString().ToLowerInvariant() +
                    ";authenticated-built-in-runtime-entity=" + authenticatedBuiltInRuntimeEntity.ToString().ToLowerInvariant() +
                    ";fresh-entity-list-alias=" + freshEntityListAlias.ToString().ToLowerInvariant() +
                    ";fresh-entity-peer-link=" + freshEntityPeerLink.ToString().ToLowerInvariant() +
                    ";fresh-entity-owned-collection-alias=" + freshEntityOwnedCollectionAlias.ToString().ToLowerInvariant() +
                    ";fresh-owned-nested-state=" + freshOwnedNestedState.ToString().ToLowerInvariant() +
                    ";reconstructed-owned-component-alias=" + reconstructedOwnedComponentAlias.ToString().ToLowerInvariant() +
                    ";reconstructed-owned-component-owner-edge=" + reconstructedOwnedComponentOwnerEdge.ToString().ToLowerInvariant() +
                    ";fresh-field-alias=" + freshFieldAlias.ToString().ToLowerInvariant() +
                    ";fresh-owner-alias-merge=" + freshOwnerAliasMerge.ToString().ToLowerInvariant() +
                    ";fresh-scene-entity-alias=" + freshSceneEntityAlias.ToString().ToLowerInvariant() +
                    ";entity-component-list-back-reference=" + entityComponentListBackReference.ToString().ToLowerInvariant() +
                    ";scene-renderer-back-reference=" + sceneRendererBackReference.ToString().ToLowerInvariant() +
                    ";reconstructed-entity-scene-back-reference=" + reconstructedEntitySceneBackReference.ToString().ToLowerInvariant() +
                    ";fresh-hash-set-membership=" + freshHashSetMembership.ToString().ToLowerInvariant() +
                    ";reconstructed-safe-parent-edge=" + reconstructedSafeParentEdge.ToString().ToLowerInvariant() +
                    ";authenticated-iterator-owner-edge=" + authenticatedIteratorOwnerEdge.ToString().ToLowerInvariant() +
                    ";authenticated-delegate-target-owner-edge=" + authenticatedDelegateTargetOwnerEdge.ToString().ToLowerInvariant() +
                    ";authenticated-delegate-alias-owner-edge=" + authenticatedDelegateAliasOwnerEdge.ToString().ToLowerInvariant() +
                    ";authenticated-delegate-captured-fresh-edge=" + authenticatedDelegateCapturedFreshEdge.ToString().ToLowerInvariant() +
                    ";authenticated-iterator-closure-owner-edge=" + authenticatedIteratorClosureOwnerEdge.ToString().ToLowerInvariant() +
                    ";edge-parent-type=" + edgeParent.TypeName +
                    ";edge-field=" + (edgeField?.Name ?? "<array>"));
            }
        }

        private bool IsExactSavedOwnerEdge(
            AkronReconstructionNode target,
            Type targetType,
            IEnumerable<AkronReconstructionPathStep> edgePath
        ) {
            return string.Equals(
                StructuralResourcePathKey(targetType, edgePath),
                StructuralResourcePathKey(targetType, GetDocumentStructuralPath(target)),
                StringComparison.Ordinal);
        }

        private bool IsAuthenticatedFreshComponentOwner(AkronReconstructionNode target, Type targetType) {
            if (!resolvedFreshObjectNodes.Contains(target.Id) ||
                !typeof(Component).IsAssignableFrom(targetType) ||
                !TryGetComponentOwnerNodes(target, out _, out int ownerEntityId) ||
                !Objects.TryGetValue(target.Id, out object componentObject) ||
                componentObject is not Component component ||
                !Objects.TryGetValue(ownerEntityId, out object ownerObject) ||
                ownerObject is not Entity ownerEntity ||
                GetEntityComponents(ownerEntity) is not ComponentList ownerComponents) {
                return false;
            }

            // A base-typed Component[] slot cannot prove the concrete saved
            // type. The live component and its owner can: the component must
            // point back to the freshly loaded entity and still occur in that
            // entity's live component list. The saved ComponentList wrapper
            // can itself require reconstruction when capture order changed.
            return ReferenceEquals(GetComponentEntity(component), ownerEntity) &&
                   GetComponentListComponents(ownerComponents).Any(candidate => ReferenceEquals(candidate, component));
        }

        private bool IsAuthenticatedReconstructedOwnedComponent(
            AkronReconstructionNode target,
            Type targetType
        ) {
            if (!typeof(Component).IsAssignableFrom(targetType) ||
                typeof(IDisposable).IsAssignableFrom(targetType) ||
                targetType.GetMethod(
                    "Finalize",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) != null ||
                !TryGetComponentOwnerNodes(
                    target,
                    out AkronReconstructionNode componentListNode,
                    out int ownerEntityId) ||
                (!IsSavedComponentListMember(target, componentListNode) &&
                 !IsDocumentDescendantOf(target, ownerEntityId)) ||
                !nodes.TryGetValue(ownerEntityId, out AkronReconstructionNode ownerEntityNode) ||
                !Objects.TryGetValue(ownerEntityId, out object ownerObject) ||
                ownerObject is not Entity) {
                return false;
            }

            Type ownerEntityType = ResolveType(ownerEntityNode.TypeName, ownerEntityNode.Path);
            return resolvedFreshObjectNodes.Contains(ownerEntityId) ||
                   authenticatedRuntimeEntityNodes.Contains(ownerEntityId) ||
                   IsAuthenticatedBuiltInRuntimeEntity(ownerEntityNode, ownerEntityType);
        }

        private bool IsAuthenticatedOwnedComponentAlias(
            AkronReconstructionNode target,
            AkronReconstructionNode edgeParent
        ) {
            if (!TryGetComponentOwnerNodes(target, out _, out int ownerEntityId)) {
                return false;
            }

            AkronReconstructionNode current = edgeParent;
            while (current != null) {
                if (current.Id == ownerEntityId) {
                    return true;
                }
                Type currentType = ResolveType(current.TypeName, current.Path);
                if (typeof(Entity).IsAssignableFrom(currentType)) {
                    return false;
                }
                current = nodes.TryGetValue(current.ParentNodeId, out AkronReconstructionNode parent)
                    ? parent
                    : null;
            }
            return false;
        }

        private bool IsSavedComponentListMember(
            AkronReconstructionNode target,
            AkronReconstructionNode componentListNode
        ) {
            List<AkronReconstructionNode> arrayParents = new List<AkronReconstructionNode>();
            if (target.ParentKind == "array" &&
                nodes.TryGetValue(target.ParentNodeId, out AkronReconstructionNode canonicalArray)) {
                arrayParents.Add(canonicalArray);
            }
            if (savedArrayAliases.TryGetValue(target.Id, out List<AkronReconstructionNode> aliases)) {
                arrayParents.AddRange(aliases);
            }

            return arrayParents.Distinct().Any(arrayParent =>
                TryGetFieldParent(arrayParent.Id, "_items", out AkronReconstructionNode componentStorage) &&
                TryGetFieldParent(componentStorage.Id, "components", out AkronReconstructionNode candidateList) &&
                candidateList.Id == componentListNode.Id);
        }

        private bool IsAuthenticatedFreshComponentAlias(
            AkronReconstructionNode target,
            AkronReconstructionNode edgeParent,
            AkronReconstructionField edgeField
        ) {
            if (!TryGetComponentOwnerNodes(
                    target,
                    out AkronReconstructionNode componentListNode,
                    out int ownerEntityId)) {
                return false;
            }
            if (IsOwnedCollectionStorageDescendant(edgeParent, componentListNode.Id, componentList: true)) {
                return true;
            }
            if (edgeField == null || edgeParent?.Id != ownerEntityId ||
                !Objects.TryGetValue(ownerEntityId, out object ownerObject) ||
                ownerObject is not Entity ownerEntity ||
                !Objects.TryGetValue(target.Id, out object componentObject) ||
                componentObject is not Component component) {
                return false;
            }

            FieldInfo field = ResolveField(edgeField.DeclaringTypeName, edgeField.Name, target.Path);
            return field.DeclaringType.IsInstanceOfType(ownerEntity) &&
                   ReferenceEquals(field.GetValue(ownerEntity), component);
        }

        private bool IsAuthenticatedFreshComponentTrackerAlias(
            AkronReconstructionNode target,
            AkronReconstructionNode edgeParent
        ) {
            if (!TryGetComponentOwnerNodes(target, out _, out int ownerEntityId) ||
                !nodes.TryGetValue(ownerEntityId, out AkronReconstructionNode ownerEntityNode) ||
                FindReferenceField(ownerEntityNode, "<Scene>k__BackingField") is not AkronReconstructionValue savedScene) {
                return false;
            }

            AkronReconstructionNode child = edgeParent;
            AkronReconstructionNode current = nodes.TryGetValue(
                edgeParent.ParentNodeId,
                out AkronReconstructionNode parent)
                ? parent
                : null;
            while (current != null) {
                if (ResolveType(current.TypeName, current.Path) == typeof(Tracker)) {
                    return current.ParentNodeId == savedScene.NodeId &&
                           current.ParentKind == "field" &&
                           current.ParentFieldName == "<Tracker>k__BackingField" &&
                           child.ParentNodeId == current.Id &&
                           child.ParentKind == "field" &&
                           child.ParentFieldName == "<Components>k__BackingField";
                }
                child = current;
                current = nodes.TryGetValue(current.ParentNodeId, out parent) ? parent : null;
            }
            return false;
        }

        private bool IsAuthenticatedRuntimeEntityTrackerAlias(
            AkronReconstructionNode target,
            Type targetType,
            AkronReconstructionNode edgeParent
        ) {
            if (!typeof(Entity).IsAssignableFrom(targetType) ||
                !TryGetEntityListOwnerNode(target, out AkronReconstructionNode entityListNode)) {
                return false;
            }

            AkronReconstructionNode child = edgeParent;
            AkronReconstructionNode current = nodes.TryGetValue(
                edgeParent.ParentNodeId,
                out AkronReconstructionNode parent)
                ? parent
                : null;
            while (current != null) {
                if (ResolveType(current.TypeName, current.Path) == typeof(Tracker)) {
                    if (child.ParentNodeId != current.Id || child.ParentKind != "field" ||
                        child.ParentFieldName != "<Entities>k__BackingField" ||
                        current.ParentKind != "field" || current.ParentFieldName != "<Tracker>k__BackingField" ||
                        !nodes.TryGetValue(current.ParentNodeId, out AkronReconstructionNode sceneNode) ||
                        !typeof(Scene).IsAssignableFrom(ResolveType(sceneNode.TypeName, sceneNode.Path)) ||
                        !resolvedFreshObjectNodes.Contains(sceneNode.Id) ||
                        !resolvedFreshObjectNodes.Contains(current.Id) ||
                        !resolvedFreshObjectNodes.Contains(entityListNode.Id)) {
                        return false;
                    }

                    AkronReconstructionValue entityListScene = FindReferenceField(
                        entityListNode,
                        "<Scene>k__BackingField");
                    return entityListScene?.NodeId == sceneNode.Id &&
                           FindReferenceField(sceneNode, "<Entities>k__BackingField")?.NodeId == entityListNode.Id &&
                           FindReferenceField(sceneNode, "<Tracker>k__BackingField")?.NodeId == current.Id;
                }
                child = current;
                current = nodes.TryGetValue(current.ParentNodeId, out parent) ? parent : null;
            }
            return false;
        }

        private bool IsAuthenticatedFreshArrayMembershipAlias(
            AkronReconstructionNode target,
            Type targetType,
            AkronReconstructionNode edgeParent,
            AkronReconstructionField edgeField
        ) {
            if (edgeField != null ||
                !resolvedFreshObjectNodes.Contains(target.Id) ||
                !resolvedFreshObjectNodes.Contains(edgeParent.Id) ||
                !Objects.TryGetValue(target.Id, out object targetObject) ||
                !Objects.TryGetValue(edgeParent.Id, out object parentObject) ||
                parentObject is not Array freshArray ||
                !freshArray.GetType().GetElementType().IsAssignableFrom(targetType)) {
                return false;
            }

            // Derived runtime indexes can order the same fresh objects
            // differently from the Set frame. Membership by reference proves
            // this saved edge without trusting the old array index.
            foreach (object candidate in freshArray) {
                if (ReferenceEquals(candidate, targetObject)) {
                    return true;
                }
            }
            return false;
        }

        private bool IsAuthenticatedFreshRendererComponentIndexAlias(
            AkronReconstructionNode target,
            Type targetType,
            AkronReconstructionNode edgeParent,
            AkronReconstructionField edgeField
        ) {
            if (edgeField != null ||
                !typeof(Component).IsAssignableFrom(targetType) ||
                !resolvedFreshObjectNodes.Contains(target.Id) ||
                !resolvedFreshObjectNodes.Contains(edgeParent.Id) ||
                !TryGetComponentOwnerNodes(target, out _, out int ownerEntityId) ||
                !nodes.TryGetValue(ownerEntityId, out AkronReconstructionNode ownerEntity) ||
                !TryGetEntityListOwnerNode(ownerEntity, out AkronReconstructionNode entityListNode) ||
                !Objects.TryGetValue(edgeParent.Id, out object parentObject) ||
                parentObject is not Array freshArray ||
                freshArray.GetType().GetElementType() is not Type elementType ||
                !elementType.IsAssignableFrom(targetType) ||
                edgeParent.ParentKind != "field" ||
                !nodes.TryGetValue(edgeParent.ParentNodeId, out AkronReconstructionNode rendererNode)) {
                return false;
            }

            Type rendererType = ResolveType(rendererNode.TypeName, rendererNode.Path);
            if (!typeof(Renderer).IsAssignableFrom(rendererType) ||
                rendererType.Assembly != typeof(Renderer).Assembly ||
                !resolvedFreshObjectNodes.Contains(rendererNode.Id) ||
                !Objects.TryGetValue(rendererNode.Id, out object rendererObject) ||
                !rendererType.IsInstanceOfType(rendererObject) ||
                rendererNode.ParentKind != "array" ||
                !TryGetFieldParent(rendererNode.ParentNodeId, "_items", out AkronReconstructionNode rendererStorageList) ||
                !TryGetFieldParent(rendererStorageList.Id, "Renderers", out AkronReconstructionNode rendererListNode) ||
                ResolveType(rendererListNode.TypeName, rendererListNode.Path) != typeof(RendererList)) {
                return false;
            }

            FieldInfo rendererField = ResolveField(
                edgeParent.ParentDeclaringTypeName,
                edgeParent.ParentFieldName,
                edgeParent.Path);
            AkronReconstructionValue entityScene = FindReferenceField(entityListNode, "<Scene>k__BackingField");
            AkronReconstructionValue rendererScene = FindReferenceField(rendererListNode, "scene");

            // Renderers rebuild typed component indexes during updates. A
            // newly loaded room can therefore omit a valid component until
            // its first renderer update. The two scene ownership loops prove
            // the saved alias without relying on that temporary membership.
            return rendererField.DeclaringType.IsAssignableFrom(rendererType) &&
                   rendererField.FieldType == freshArray.GetType() &&
                   ReferenceEquals(rendererField.GetValue(rendererObject), freshArray) &&
                   entityScene?.NodeId != 0 &&
                   entityScene.NodeId == rendererScene?.NodeId;
        }

        private bool IsAuthenticatedTrailSnapshotPlayerComponentAlias(
            AkronReconstructionNode target,
            Type targetType,
            AkronReconstructionNode edgeParent,
            AkronReconstructionField edgeField
        ) {
            Type parentType = ResolveType(edgeParent.TypeName, edgeParent.Path);
            if (!AkronReconstructionGraph.IsTrailSnapshotComponentReference(
                    parentType,
                    edgeField?.Name,
                    targetType) ||
                !Objects.TryGetValue(target.Id, out object componentObject) ||
                componentObject is not Component component ||
                !TryGetComponentOwnerNodes(target, out _, out int playerNodeId) ||
                !nodes.TryGetValue(playerNodeId, out AkronReconstructionNode playerNode) ||
                ResolveType(playerNode.TypeName, playerNode.Path) != typeof(Player) ||
                !Objects.TryGetValue(playerNode.Id, out object playerObject) ||
                playerObject is not Player player ||
                !ReferenceEquals(GetComponentEntity(component), player) ||
                FindReferenceField(edgeParent, nameof(TrailManager.Snapshot.Manager)) is not AkronReconstructionValue managerReference ||
                !nodes.TryGetValue(managerReference.NodeId, out AkronReconstructionNode managerNode) ||
                ResolveType(managerNode.TypeName, managerNode.Path) != typeof(TrailManager) ||
                FindReferenceField(managerNode, "snapshots") is not AkronReconstructionValue snapshotsReference ||
                edgeParent.ParentKind != "array" ||
                edgeParent.ParentNodeId != snapshotsReference.NodeId) {
                return false;
            }

            AkronReconstructionValue playerScene = FindReferenceField(playerNode, "<Scene>k__BackingField");
            AkronReconstructionValue snapshotScene = FindReferenceField(edgeParent, "<Scene>k__BackingField");
            AkronReconstructionValue managerScene = FindReferenceField(managerNode, "<Scene>k__BackingField");
            return playerScene?.NodeId != 0 &&
                   playerScene.NodeId == managerScene?.NodeId &&
                   (snapshotScene == null || playerScene.NodeId == snapshotScene.NodeId);
        }

        private bool IsAuthenticatedReconstructedBuiltInComponentAlias(
            AkronReconstructionNode target,
            Type targetType,
            AkronReconstructionNode edgeParent,
            AkronReconstructionField edgeField
        ) {
            Type parentType = ResolveType(edgeParent.TypeName, edgeParent.Path);
            if (!AkronReconstructionGraph.IsBuiltInSavedComponentAliasField(
                    parentType,
                    edgeField?.Name,
                    targetType) ||
                resolvedFreshObjectNodes.Contains(target.Id) ||
                resolvedFreshObjectNodes.Contains(edgeParent.Id) ||
                !TryGetComponentOwnerNodes(
                    target,
                    out AkronReconstructionNode componentListNode,
                    out int ownerEntityId) ||
                ownerEntityId != edgeParent.Id) {
                return false;
            }
            AkronReconstructionValue ownerComponents = FindReferenceField(
                edgeParent,
                "<Components>k__BackingField");
            return ownerComponents?.NodeId == componentListNode.Id;
        }

        private bool IsAuthenticatedEntityListAlias(
            AkronReconstructionNode target,
            Type targetType,
            AkronReconstructionNode edgeParent,
            bool exactParentSlot,
            bool authenticatedBuiltInRuntimeEntity
        ) {
            return typeof(Entity).IsAssignableFrom(targetType) &&
                   (exactParentSlot || resolvedFreshObjectNodes.Contains(target.Id) ||
                    authenticatedBuiltInRuntimeEntity) &&
                   TryGetEntityListOwnerNode(target, out AkronReconstructionNode entityListNode) &&
                   IsOwnedCollectionStorageDescendant(edgeParent, entityListNode.Id, componentList: false);
        }

        private bool IsAuthenticatedBuiltInRuntimeEntity(
            AkronReconstructionNode node,
            Type type
        ) {
            if (!typeof(Entity).IsAssignableFrom(type) || type.IsAbstract ||
                type.Assembly != typeof(Entity).Assembly ||
                typeof(IDisposable).IsAssignableFrom(type) ||
                type.GetMethod(
                    "Finalize",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) != null ||
                TryGetSavedEntityId(node, out _) ||
                !TryGetEntityListOwnerNode(node, out AkronReconstructionNode entityListNode)) {
                return false;
            }

            AkronReconstructionValue entityScene = FindReferenceField(node, "<Scene>k__BackingField");
            AkronReconstructionValue listScene = FindReferenceField(entityListNode, "<Scene>k__BackingField");
            if (entityScene == null || listScene?.NodeId != entityScene.NodeId ||
                !nodes.TryGetValue(entityScene.NodeId, out AkronReconstructionNode sceneNode) ||
                FindReferenceField(sceneNode, "<Entities>k__BackingField")?.NodeId != entityListNode.Id ||
                !resolvedFreshObjectNodes.Contains(sceneNode.Id) ||
                !resolvedFreshObjectNodes.Contains(entityListNode.Id) ||
                !Objects.TryGetValue(sceneNode.Id, out object sceneObject) || sceneObject is not Scene scene ||
                !Objects.TryGetValue(entityListNode.Id, out object listObject) || listObject is not EntityList entityList) {
                return false;
            }

            // Runtime effects have no map EntityID, so a clean room cannot
            // create them. Authenticate the built-in type through the saved
            // Entity <-> EntityList <-> Scene ownership loop and the exact
            // fresh Scene/List pair instead of trusting a type name alone.
            return ReferenceEquals(GetSceneEntities(scene), entityList);
        }

        private bool IsAuthenticatedFreshEntityPeerLink(
            AkronReconstructionNode target,
            Type targetType,
            AkronReconstructionNode edgeParent,
            AkronReconstructionField edgeField
        ) {
            if (edgeField == null || !typeof(Entity).IsAssignableFrom(targetType) ||
                !resolvedFreshObjectNodes.Contains(target.Id) ||
                !resolvedFreshObjectNodes.Contains(edgeParent.Id) ||
                !TryGetEntityListOwnerNode(target, out AkronReconstructionNode targetEntityList) ||
                !TryGetEntityListOwnerNode(edgeParent, out AkronReconstructionNode parentEntityList) ||
                targetEntityList.Id != parentEntityList.Id ||
                !Objects.TryGetValue(targetEntityList.Id, out object entityListObject) ||
                entityListObject is not EntityList entityList ||
                !Objects.TryGetValue(target.Id, out object targetObject) || targetObject is not Entity targetEntity ||
                !Objects.TryGetValue(edgeParent.Id, out object parentObject) || parentObject is not Entity parentEntity ||
                !GetEntityListEntities(entityList).Any(candidate => ReferenceEquals(candidate, targetEntity)) ||
                !GetEntityListEntities(entityList).Any(candidate => ReferenceEquals(candidate, parentEntity))) {
                return false;
            }

            // Some room entities cache a peer entity only after their first
            // update or render. A cold room therefore has a null field even
            // though both exact entities already exist in the same fresh
            // EntityList. The concrete field type and two list memberships
            // authenticate the saved peer link without relying on list order.
            FieldInfo field = ResolveField(edgeField.DeclaringTypeName, edgeField.Name, edgeField.Path);
            return field.FieldType == targetType && field.DeclaringType.IsInstanceOfType(parentEntity);
        }

        private bool IsAuthenticatedFreshEntityOwnedCollectionAlias(
            AkronReconstructionNode target,
            Type targetType,
            AkronReconstructionNode edgeParent
        ) {
            if (!typeof(Entity).IsAssignableFrom(targetType) ||
                !resolvedFreshObjectNodes.Contains(target.Id) ||
                !Objects.TryGetValue(target.Id, out object targetObject) || targetObject is not Entity targetEntity ||
                !TryGetEntityListOwnerNode(target, out AkronReconstructionNode targetEntityList)) {
                return false;
            }

            AkronReconstructionNode current = edgeParent;
            while (current != null &&
                   !typeof(Entity).IsAssignableFrom(ResolveType(current.TypeName, current.Path))) {
                current = nodes.TryGetValue(current.ParentNodeId, out AkronReconstructionNode parent)
                    ? parent
                    : null;
            }
            if (current == null || !resolvedFreshObjectNodes.Contains(current.Id) ||
                !Objects.TryGetValue(current.Id, out object ownerObject) || ownerObject is not Entity ownerEntity ||
                !TryGetEntityListOwnerNode(current, out AkronReconstructionNode ownerEntityList) ||
                ownerEntityList.Id != targetEntityList.Id ||
                !Objects.TryGetValue(targetEntityList.Id, out object listObject) || listObject is not EntityList entityList) {
                return false;
            }
            return GetEntityListEntities(entityList).Any(candidate => ReferenceEquals(candidate, targetEntity)) &&
                   GetEntityListEntities(entityList).Any(candidate => ReferenceEquals(candidate, ownerEntity));
        }

        private bool IsAuthenticatedFreshEntityOwnedNestedState(
            AkronReconstructionNode node,
            Type type
        ) {
            if (node.ParentKind != "field" || !type.IsClass || type.IsAbstract || type.IsGenericType ||
                typeof(Entity).IsAssignableFrom(type) || typeof(Component).IsAssignableFrom(type) ||
                typeof(Renderer).IsAssignableFrom(type) || typeof(Delegate).IsAssignableFrom(type) ||
                typeof(IDisposable).IsAssignableFrom(type) ||
                type.GetMethod("Finalize", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) != null ||
                !resolvedFreshObjectNodes.Contains(node.ParentNodeId) ||
                !Objects.TryGetValue(node.ParentNodeId, out object parentObject) || parentObject is not Entity parentEntity ||
                type.DeclaringType != parentEntity.GetType()) {
                return false;
            }

            FieldInfo field = ResolveField(
                node.ParentDeclaringTypeName,
                node.ParentFieldName,
                node.Path);
            return field.FieldType == type && field.DeclaringType.IsInstanceOfType(parentEntity);
        }

        private bool IsAuthenticatedFreshRendererOwnedRuntimeState(
            AkronReconstructionNode node,
            Type type
        ) {
            if (!type.IsClass || type.IsAbstract || type.IsGenericType ||
                type.Assembly != typeof(Renderer).Assembly ||
                typeof(IDisposable).IsAssignableFrom(type) ||
                type.GetMethod(
                    "Finalize",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) != null) {
                return false;
            }

            AkronReconstructionNode child = node;
            AkronReconstructionNode current = nodes.TryGetValue(
                node.ParentNodeId,
                out AkronReconstructionNode parent)
                ? parent
                : null;
            while (current != null) {
                Type ownerType = ResolveType(current.TypeName, current.Path);
                if (typeof(Renderer).IsAssignableFrom(ownerType)) {
                    if (type.DeclaringType != ownerType ||
                        child.ParentNodeId != current.Id || child.ParentKind != "field" ||
                        !resolvedFreshObjectNodes.Contains(current.Id) ||
                        !Objects.TryGetValue(current.Id, out object ownerObject) ||
                        ownerObject is not Renderer) {
                        return false;
                    }

                    FieldInfo field = ResolveField(
                        child.ParentDeclaringTypeName,
                        child.ParentFieldName,
                        child.Path);
                    bool ownsElement = field.FieldType.IsArray
                        ? field.FieldType.GetElementType() == type
                        : IsSupportedCollectionType(field.FieldType) &&
                          field.FieldType.GetGenericArguments().Contains(type);
                    return field.DeclaringType.IsInstanceOfType(ownerObject) && ownsElement;
                }
                if (typeof(Entity).IsAssignableFrom(ownerType) ||
                    typeof(Component).IsAssignableFrom(ownerType) ||
                    !nodes.TryGetValue(current.ParentNodeId, out parent)) {
                    return false;
                }
                child = current;
                current = parent;
            }
            return false;
        }

        private bool IsAuthenticatedRuntimeEntityOwnedState(
            AkronReconstructionNode node,
            Type type
        ) {
            if (node.ParentKind != "field" ||
                node.ParentFieldName != "<Components>k__BackingField" ||
                type != typeof(ComponentList) ||
                !authenticatedRuntimeEntityNodes.Contains(node.ParentNodeId) ||
                !Objects.TryGetValue(node.ParentNodeId, out object parentObject) ||
                parentObject is not Entity) {
                return false;
            }

            FieldInfo field = ResolveField(
                node.ParentDeclaringTypeName,
                node.ParentFieldName,
                node.Path);
            return field.FieldType == typeof(ComponentList) &&
                   field.DeclaringType.IsInstanceOfType(parentObject);
        }

        private bool IsAuthenticatedFreshFieldAlias(
            AkronReconstructionNode target,
            AkronReconstructionNode edgeParent,
            AkronReconstructionField edgeField
        ) {
            if (edgeField == null || !resolvedFreshObjectNodes.Contains(target.Id) ||
                !resolvedFreshObjectNodes.Contains(edgeParent.Id) ||
                !Objects.TryGetValue(edgeParent.Id, out object parentObject) ||
                !Objects.TryGetValue(target.Id, out object targetObject)) {
                return false;
            }
            FieldInfo field = ResolveField(edgeField.DeclaringTypeName, edgeField.Name, target.Path);
            if (!field.DeclaringType.IsInstanceOfType(parentObject)) {
                return false;
            }
            if (ReferenceEquals(field.GetValue(parentObject), targetObject)) {
                return true;
            }

            // ResolveObjects can reserve the exact typed alias before this
            // owner field receives its saved assignment. Accept only that
            // pre-scanned owner edge, not another base-typed alias to the same
            // saved node.
            return field.FieldType == targetObject.GetType() &&
                   freshFieldAliasesByNode.TryGetValue(target.Id, out object reservedAlias) &&
                   ReferenceEquals(reservedAlias, targetObject) &&
                   freshFieldAliasSourcesByNode.TryGetValue(
                       target.Id,
                       out (int ParentId, string DeclaringTypeName, string FieldName) source) &&
                   source.ParentId == edgeParent.Id &&
                   source.DeclaringTypeName == edgeField.DeclaringTypeName &&
                   source.FieldName == edgeField.Name;
        }

        private bool IsAuthenticatedFreshOwnerAliasMerge(
            AkronReconstructionNode target,
            Type targetType,
            AkronReconstructionNode edgeParent,
            AkronReconstructionField edgeField
        ) {
            if (edgeField == null || target.ParentKind != "field" ||
                target.ParentNodeId != edgeParent.Id ||
                !resolvedFreshObjectNodes.Contains(target.Id) ||
                !resolvedFreshObjectNodes.Contains(edgeParent.Id) ||
                !Objects.TryGetValue(target.Id, out object targetObject) ||
                !Objects.TryGetValue(edgeParent.Id, out object parentObject)) {
                return false;
            }

            FieldInfo canonicalField = ResolveField(
                target.ParentDeclaringTypeName,
                target.ParentFieldName,
                target.Path);
            FieldInfo aliasField = ResolveField(
                edgeField.DeclaringTypeName,
                edgeField.Name,
                edgeField.Path);
            object currentAlias = aliasField.DeclaringType.IsInstanceOfType(parentObject)
                ? aliasField.GetValue(parentObject)
                : null;
            Type parentType = ResolveType(edgeParent.TypeName, edgeParent.Path);
            bool fieldShapeMatches =
                (canonicalField.FieldType != targetType &&
                 canonicalField.FieldType.IsAssignableFrom(targetType) &&
                 aliasField.FieldType == targetType) ||
                AkronReconstructionGraph.IsPlayerRuntimeColliderAlias(
                    parentType,
                    target.ParentFieldName,
                    edgeField.Name,
                    targetType);
            return canonicalField.DeclaringType.IsInstanceOfType(parentObject) &&
                   ReferenceEquals(canonicalField.GetValue(parentObject), targetObject) &&
                   fieldShapeMatches &&
                   currentAlias?.GetType() == targetType;
        }

        private bool IsAuthenticatedFreshSceneEntityAlias(
            Type targetType,
            AkronReconstructionNode edgeParent,
            AkronReconstructionField edgeField,
            bool exactParentSlot
        ) {
            if (!exactParentSlot || edgeField == null ||
                !typeof(Entity).IsAssignableFrom(targetType) ||
                !resolvedFreshObjectNodes.Contains(edgeParent.Id) ||
                !Objects.TryGetValue(edgeParent.Id, out object parentObject) ||
                parentObject is not Scene) {
                return false;
            }
            FieldInfo field = ResolveField(edgeField.DeclaringTypeName, edgeField.Name, edgeField.Path);
            return field.FieldType == targetType && field.DeclaringType.IsInstanceOfType(parentObject);
        }

        private bool IsAuthenticatedEntityComponentListBackReference(
            AkronReconstructionNode target,
            Type targetType,
            AkronReconstructionNode edgeParent,
            AkronReconstructionField edgeField,
            bool exactParentSlot
        ) {
            if ((!exactParentSlot && !authenticatedRuntimeEntityNodes.Contains(target.Id)) ||
                edgeField?.Name != "<Entity>k__BackingField" ||
                !typeof(Entity).IsAssignableFrom(targetType) ||
                ResolveType(edgeParent.TypeName, edgeParent.Path) != typeof(ComponentList) ||
                edgeParent.ParentNodeId != target.Id ||
                edgeParent.ParentKind != "field" ||
                edgeParent.ParentFieldName != "<Components>k__BackingField") {
                return false;
            }
            AkronReconstructionValue components = FindReferenceField(target, "<Components>k__BackingField");
            return components?.NodeId == edgeParent.Id;
        }

        private bool IsAuthenticatedSceneRendererBackReference(
            AkronReconstructionNode target,
            Type targetType,
            AkronReconstructionNode edgeParent,
            AkronReconstructionField edgeField
        ) {
            Type rendererType = ResolveType(edgeParent.TypeName, edgeParent.Path);
            if (edgeField?.Name != "Scene" ||
                !typeof(Scene).IsAssignableFrom(targetType) ||
                !typeof(Renderer).IsAssignableFrom(rendererType) ||
                !resolvedFreshObjectNodes.Contains(target.Id) ||
                edgeParent.ParentKind != "array" ||
                !TryGetFieldParent(edgeParent.ParentNodeId, "_items", out AkronReconstructionNode rendererListStorage) ||
                !TryGetFieldParent(rendererListStorage.Id, "Renderers", out AkronReconstructionNode rendererListNode) ||
                ResolveType(rendererListNode.TypeName, rendererListNode.Path) != typeof(RendererList)) {
                return false;
            }
            FieldInfo sceneField = ResolveField(edgeField.DeclaringTypeName, edgeField.Name, edgeField.Path);
            AkronReconstructionValue sceneRendererList = FindReferenceField(
                target,
                "<RendererList>k__BackingField");
            AkronReconstructionValue rendererListScene = FindReferenceField(rendererListNode, "scene");
            return sceneField.DeclaringType.IsAssignableFrom(rendererType) &&
                   sceneField.FieldType.IsAssignableFrom(targetType) &&
                   sceneRendererList?.NodeId == rendererListNode.Id &&
                   rendererListScene?.NodeId == target.Id;
        }

        private bool IsAuthenticatedReconstructedEntitySceneBackReference(
            AkronReconstructionNode target,
            Type targetType,
            AkronReconstructionNode edgeParent,
            AkronReconstructionField edgeField
        ) {
            Type entityType = ResolveType(edgeParent.TypeName, edgeParent.Path);
            if (edgeField?.Name != "<Scene>k__BackingField" ||
                !typeof(Scene).IsAssignableFrom(targetType) ||
                !typeof(Entity).IsAssignableFrom(entityType) ||
                resolvedFreshObjectNodes.Contains(edgeParent.Id) ||
                (!IsExplicitlySafeReconstructionType(entityType) &&
                 !authenticatedRuntimeEntityNodes.Contains(edgeParent.Id)) ||
                !resolvedFreshObjectNodes.Contains(target.Id) ||
                !Objects.TryGetValue(target.Id, out object sceneObject) ||
                sceneObject is not Scene ||
                !Objects.TryGetValue(edgeParent.Id, out object entityObject) ||
                entityObject is not Entity ||
                !TryGetEntityListOwnerNode(edgeParent, out AkronReconstructionNode entityListNode)) {
                return false;
            }

            FieldInfo sceneField = ResolveField(edgeField.DeclaringTypeName, edgeField.Name, edgeField.Path);
            AkronReconstructionValue listScene = FindReferenceField(entityListNode, "<Scene>k__BackingField");
            return sceneField.DeclaringType.IsAssignableFrom(entityType) &&
                   sceneField.FieldType.IsAssignableFrom(targetType) &&
                   listScene?.NodeId == target.Id;
        }

        private bool IsAuthenticatedFreshHashSetMembership(
            AkronReconstructionNode target,
            AkronReconstructionNode edgeParent
        ) {
            if (!resolvedFreshObjectNodes.Contains(target.Id) ||
                !Objects.TryGetValue(target.Id, out object targetObject)) {
                return false;
            }
            AkronReconstructionNode current = edgeParent;
            while (current != null) {
                Type currentType = ResolveType(current.TypeName, current.Path);
                if (currentType.IsGenericType && currentType.GetGenericTypeDefinition() == typeof(HashSet<>)) {
                    if (!resolvedFreshObjectNodes.Contains(current.Id) ||
                        !Objects.TryGetValue(current.Id, out object setObject) ||
                        !currentType.GetGenericArguments()[0].IsInstanceOfType(targetObject) ||
                        current.ParentKind != "field" ||
                        !resolvedFreshObjectNodes.Contains(current.ParentNodeId) ||
                        !Objects.TryGetValue(current.ParentNodeId, out object ownerObject)) {
                        return false;
                    }
                    FieldInfo ownerField = ResolveField(
                        current.ParentDeclaringTypeName,
                        current.ParentFieldName,
                        current.Path);
                    // Membership is the state being restored, so the fresh
                    // set can be empty or contain different values. Prove the
                    // live set's exact owner field and the live element's
                    // type instead of requiring the old membership to exist.
                    return ownerField.FieldType == currentType &&
                           ownerField.DeclaringType.IsInstanceOfType(ownerObject) &&
                           ReferenceEquals(ownerField.GetValue(ownerObject), setObject);
                }
                current = nodes.TryGetValue(current.ParentNodeId, out AkronReconstructionNode parent)
                    ? parent
                    : null;
            }
            return false;
        }

        private bool TryResolveFreshFieldAlias(
            AkronReconstructionNode target,
            Type targetType,
            out object matchedAlias
        ) {
            return TryResolveFreshFieldAlias(target, targetType, out matchedAlias, out _);
        }

        private bool TryResolveFreshFieldAlias(
            AkronReconstructionNode target,
            Type targetType,
            out object matchedAlias,
            out bool exactTypedAlias
        ) {
            matchedAlias = null;
            exactTypedAlias = false;
            int matchedPriority = -1;
            bool ambiguous = false;
            if (freshFieldAliasesByNode.TryGetValue(target.Id, out object reservedAlias) &&
                reservedAlias.GetType() == targetType && !freshOwners.ContainsKey(reservedAlias) &&
                (target.Kind != ArrayKind ||
                 reservedAlias is Array reservedArray && ArrayShapeMatches(reservedArray, target))) {
                matchedAlias = reservedAlias;
                exactTypedAlias = freshFieldAliasSourcesByNode.TryGetValue(
                        target.Id,
                        out (int ParentId, string DeclaringTypeName, string FieldName) source) &&
                    ResolveField(source.DeclaringTypeName, source.FieldName, target.Path).FieldType == targetType;
                return true;
            }
            if (!savedFieldAliases.TryGetValue(
                    target.Id,
                    out List<(AkronReconstructionNode Parent, AkronReconstructionField Field)> aliases)) {
                return false;
            }
            foreach ((AkronReconstructionNode parent, AkronReconstructionField savedField) in aliases) {
                if (IsDocumentDescendantOf(parent, target.Id) || IsDocumentCollectionStorageNode(parent)) {
                    continue;
                }
                if (!Objects.TryGetValue(parent.Id, out object parentObject)) {
                    continue;
                }
                FieldInfo field = ResolveField(savedField.DeclaringTypeName, savedField.Name, savedField.Path);
                object candidate = field.DeclaringType.IsInstanceOfType(parentObject)
                    ? field.GetValue(parentObject)
                    : null;
                if (candidate == null || candidate.GetType() != targetType || freshOwners.ContainsKey(candidate) ||
                    (freshFieldAliasReservations.TryGetValue(candidate, out int reservedNodeId) &&
                     reservedNodeId != target.Id)) {
                    continue;
                }
                if (target.Kind == ArrayKind &&
                    (candidate is not Array candidateArray || !ArrayShapeMatches(candidateArray, target))) {
                    continue;
                }
                int candidatePriority = field.FieldType == targetType ? 1 : 0;
                if (matchedAlias == null || candidatePriority > matchedPriority) {
                    matchedAlias = candidate;
                    matchedPriority = candidatePriority;
                    ambiguous = false;
                    continue;
                }
                if (candidatePriority == matchedPriority && !ReferenceEquals(matchedAlias, candidate)) {
                    ambiguous = true;
                }
            }
            if (ambiguous) {
                matchedAlias = null;
            }
            exactTypedAlias = matchedAlias != null && matchedPriority == 1;
            return matchedAlias != null;
        }

        private void ReserveFreshFieldAliases() {
            HashSet<object> ambiguous = new HashSet<object>(ReferenceEqualityComparer.Instance);
            Dictionary<object, int> aliasPriorities =
                new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
            Dictionary<object, (int ParentId, string DeclaringTypeName, string FieldName)> aliasSources =
                new Dictionary<object, (int ParentId, string DeclaringTypeName, string FieldName)>(
                    ReferenceEqualityComparer.Instance);
            foreach (KeyValuePair<int, List<(AkronReconstructionNode Parent, AkronReconstructionField Field)>> pair
                     in savedFieldAliases) {
                if (!nodes.TryGetValue(pair.Key, out AkronReconstructionNode target)) {
                    continue;
                }
                Type targetType = ResolveType(target.TypeName, target.Path);
                foreach ((AkronReconstructionNode parent, AkronReconstructionField savedField) in pair.Value) {
                    if (IsDocumentDescendantOf(parent, target.Id) || IsDocumentCollectionStorageNode(parent)) {
                        continue;
                    }
                    List<AkronReconstructionPathStep> aliasPath = GetDocumentStructuralPath(parent);
                    aliasPath.Add(new AkronReconstructionPathStep {
                        Kind = "field",
                        DeclaringTypeName = savedField.DeclaringTypeName,
                        FieldName = savedField.Name
                    });
                    object candidate = ResolveFreshPath(aliasPath, target.Path);
                    if (candidate == null || candidate.GetType() != targetType || ambiguous.Contains(candidate)) {
                        continue;
                    }
                    FieldInfo aliasField = ResolveField(
                        savedField.DeclaringTypeName,
                        savedField.Name,
                        savedField.Path);
                    if (freshFieldAliasReservations.TryGetValue(candidate, out int existingTargetId) &&
                        existingTargetId != target.Id) {
                        freshFieldAliasReservations.Remove(candidate);
                        ambiguous.Add(candidate);
                        continue;
                    }
                    freshFieldAliasReservations[candidate] = target.Id;
                    aliasPriorities[candidate] = aliasField.FieldType == targetType ? 1 : 0;
                    aliasSources[candidate] = (parent.Id, savedField.DeclaringTypeName, savedField.Name);
                }
            }

            // Saved aliases can point to an object before its canonical owner
            // appears in node order. Build the reverse lookup once so restore
            // can use the pre-scanned fresh alias without waiting for that
            // later parent node. Prefer an exact typed owner field over a
            // broader alias. For example, Player.duckHitbox identifies the
            // saved Hitbox while Entity.Collider only identifies Collider.
            // Candidates with the same strength stay unresolved.
            HashSet<int> ambiguousNodeIds = new HashSet<int>();
            Dictionary<int, int> selectedPriorities = new Dictionary<int, int>();
            foreach (KeyValuePair<object, int> reservation in freshFieldAliasReservations) {
                if (ambiguousNodeIds.Contains(reservation.Value)) {
                    continue;
                }
                if (freshFieldAliasesByNode.TryGetValue(reservation.Value, out object existingAlias) &&
                    !ReferenceEquals(existingAlias, reservation.Key)) {
                    int currentPriority = aliasPriorities[reservation.Key];
                    int existingPriority = selectedPriorities[reservation.Value];
                    if (currentPriority > existingPriority) {
                        freshFieldAliasesByNode[reservation.Value] = reservation.Key;
                        freshFieldAliasSourcesByNode[reservation.Value] = aliasSources[reservation.Key];
                        selectedPriorities[reservation.Value] = currentPriority;
                        continue;
                    }
                    if (currentPriority < existingPriority) {
                        continue;
                    }
                    freshFieldAliasesByNode.Remove(reservation.Value);
                    freshFieldAliasSourcesByNode.Remove(reservation.Value);
                    selectedPriorities.Remove(reservation.Value);
                    ambiguousNodeIds.Add(reservation.Value);
                    continue;
                }
                freshFieldAliasesByNode[reservation.Value] = reservation.Key;
                freshFieldAliasSourcesByNode[reservation.Value] = aliasSources[reservation.Key];
                selectedPriorities[reservation.Value] = aliasPriorities[reservation.Key];
            }
        }

        private bool IsDocumentDescendantOf(AkronReconstructionNode node, int possibleAncestorId) {
            AkronReconstructionNode current = node;
            while (current != null && current.ParentNodeId > 0) {
                if (current.Id == possibleAncestorId) {
                    return true;
                }
                current = nodes.TryGetValue(current.ParentNodeId, out AkronReconstructionNode parent)
                    ? parent
                    : null;
            }
            return current?.Id == possibleAncestorId;
        }

        private bool IsDocumentCollectionStorageNode(AkronReconstructionNode node) {
            AkronReconstructionNode current = node;
            while (current != null && current.ParentNodeId > 0 &&
                   nodes.TryGetValue(current.ParentNodeId, out AkronReconstructionNode parent)) {
                Type parentType = ResolveType(parent.TypeName, parent.Path);
                if (current.ParentKind == "field" &&
                    ((parentType == typeof(EntityList) && IsEntityListStorageField(current.ParentFieldName)) ||
                     (parentType == typeof(ComponentList) && IsComponentListStorageField(current.ParentFieldName)))) {
                    return true;
                }
                current = parent;
            }
            return false;
        }

        private bool IsSavedDelegateTargetAlias(int targetNodeId, Type targetType) {
            return savedDelegateTargetAliases.Contains(targetNodeId) &&
                   nodes.TryGetValue(targetNodeId, out AkronReconstructionNode targetNode) &&
                   ResolveType(targetNode.TypeName, targetNode.Path) == targetType;
        }

        private static HashSet<int> IndexSavedDelegateTargetAliases(
            AkronReconstructionDocument sourceDocument,
            IReadOnlyDictionary<int, AkronReconstructionNode> sourceNodes
        ) {
            HashSet<int> aliases = new HashSet<int>();
            foreach (AkronReconstructionDelegateCall call in sourceDocument.Nodes
                         .Where(node => node.Kind == DelegateKind)
                         .SelectMany(node => node.DelegateCalls ?? new List<AkronReconstructionDelegateCall>())) {
                if (call.Target?.Kind == ReferenceValueKind &&
                    sourceNodes.TryGetValue(call.Target.NodeId, out AkronReconstructionNode target) &&
                    call.DeclaringTypeName == target.TypeName &&
                    !string.IsNullOrWhiteSpace(call.MethodName)) {
                    aliases.Add(target.Id);
                }
            }
            return aliases;
        }

        private static Dictionary<int, List<(AkronReconstructionNode Parent, AkronReconstructionField Field)>>
            IndexSavedFieldAliases(AkronReconstructionDocument sourceDocument) {
            Dictionary<int, List<(AkronReconstructionNode Parent, AkronReconstructionField Field)>> aliases =
                new Dictionary<int, List<(AkronReconstructionNode Parent, AkronReconstructionField Field)>>();
            foreach (AkronReconstructionNode parent in sourceDocument.Nodes) {
                foreach (AkronReconstructionField field in parent.Fields ?? new List<AkronReconstructionField>()) {
                    if (field?.Value?.Kind != ReferenceValueKind) {
                        continue;
                    }
                    if (!aliases.TryGetValue(
                            field.Value.NodeId,
                            out List<(AkronReconstructionNode Parent, AkronReconstructionField Field)> targetAliases)) {
                        targetAliases = new List<(AkronReconstructionNode Parent, AkronReconstructionField Field)>();
                        aliases[field.Value.NodeId] = targetAliases;
                    }
                    targetAliases.Add((parent, field));
                }
            }
            return aliases;
        }

        private static Dictionary<int, List<AkronReconstructionNode>> IndexSavedArrayAliases(
            AkronReconstructionDocument sourceDocument
        ) {
            Dictionary<int, List<AkronReconstructionNode>> aliases =
                new Dictionary<int, List<AkronReconstructionNode>>();
            foreach (AkronReconstructionNode parent in sourceDocument.Nodes.Where(node => node.Kind == ArrayKind)) {
                foreach (AkronReconstructionValue item in parent.Items ?? new List<AkronReconstructionValue>()) {
                    if (item?.Kind != ReferenceValueKind) {
                        continue;
                    }
                    if (!aliases.TryGetValue(item.NodeId, out List<AkronReconstructionNode> targetAliases)) {
                        targetAliases = new List<AkronReconstructionNode>();
                        aliases[item.NodeId] = targetAliases;
                    }
                    if (!targetAliases.Contains(parent)) {
                        targetAliases.Add(parent);
                    }
                }
            }
            return aliases;
        }

        private bool IsOwnedCollectionStorageDescendant(
            AkronReconstructionNode edgeParent,
            int ownerListNodeId,
            bool componentList
        ) {
            AkronReconstructionNode current = edgeParent;
            while (current != null && current.Id != ownerListNodeId) {
                Type currentType = ResolveType(current.TypeName, current.Path);
                if (typeof(Component).IsAssignableFrom(currentType) ||
                    typeof(Entity).IsAssignableFrom(currentType) ||
                    !nodes.TryGetValue(current.ParentNodeId, out AkronReconstructionNode parent)) {
                    return false;
                }
                if (parent.Id == ownerListNodeId) {
                    return current.ParentKind == "field" &&
                           (componentList
                               ? IsComponentListStorageField(current.ParentFieldName)
                               : IsEntityListStorageField(current.ParentFieldName));
                }
                current = parent;
            }
            return false;
        }

        private static bool IsComponentListStorageField(string fieldName) {
            return fieldName is "components" or "toAdd" or "toRemove" or "current" or "adding" or "removing";
        }

        private static bool IsEntityListStorageField(string fieldName) {
            return fieldName is "entities" or "toAdd" or "toAwake" or "toRemove" or "current" or "adding" or "removing";
        }

        private bool TryResolveFreshOwnedComponent(
            AkronReconstructionNode target,
            Type targetType,
            out Component matchedComponent
        ) {
            matchedComponent = null;
            if (!typeof(Component).IsAssignableFrom(targetType) ||
                !TryGetComponentOwnerNodes(target, out _, out int ownerEntityId) ||
                !TryResolveComponentOwnerEntity(ownerEntityId, out Entity ownerEntity) ||
                GetEntityComponents(ownerEntity) is not ComponentList ownerComponents) {
                return false;
            }

            foreach (Component candidate in GetComponentListComponents(ownerComponents)) {
                if (candidate == null || candidate.GetType() != targetType ||
                    !ReferenceEquals(GetComponentEntity(candidate), ownerEntity) || freshOwners.ContainsKey(candidate)) {
                    continue;
                }
                if (matchedComponent != null) {
                    // Two components of the same concrete type have no stable
                    // identity beyond list position. Do not guess between them.
                    matchedComponent = null;
                    return false;
                }
                matchedComponent = candidate;
            }
            return matchedComponent != null;
        }

        private bool TryResolveComponentOwnerEntity(int ownerEntityId, out Entity ownerEntity) {
            ownerEntity = Objects.TryGetValue(ownerEntityId, out object ownerObject)
                ? ownerObject as Entity
                : null;
            if (ownerEntity != null) {
                return true;
            }
            if (!nodes.TryGetValue(ownerEntityId, out AkronReconstructionNode ownerNode)) {
                return false;
            }
            Type ownerType = ResolveType(ownerNode.TypeName, ownerNode.Path);
            if (TryResolveFreshFieldAlias(ownerNode, ownerType, out object fieldAlias) &&
                fieldAlias is Entity fieldOwner && SavedEntitySourceMatches(ownerNode, fieldOwner)) {
                ownerEntity = fieldOwner;
                return true;
            }
            object directMatch = ResolveFreshObject(ownerNode);
            if (directMatch is Entity directEntity && directEntity.GetType() == ownerType &&
                SavedEntitySourceMatches(ownerNode, directEntity)) {
                ownerEntity = directEntity;
                return true;
            }
            if (TryResolveFreshOwnedEntity(ownerNode, ownerType, out Entity listEntity)) {
                ownerEntity = listEntity;
                return true;
            }
            return false;
        }

        private bool TryResolveFreshOwnedEntity(
            AkronReconstructionNode target,
            Type targetType,
            out Entity matchedEntity
        ) {
            matchedEntity = null;
            if (!typeof(Entity).IsAssignableFrom(targetType) ||
                !TryGetEntityListOwnerNode(target, out AkronReconstructionNode entityListNode)) {
                return false;
            }
            object entityListObject = Objects.TryGetValue(entityListNode.Id, out object restoredEntityList)
                ? restoredEntityList
                : ResolveFreshObject(entityListNode);
            if (entityListObject is not EntityList entityList) {
                return false;
            }

            foreach (Entity candidate in GetEntityListEntities(entityList)) {
                if (candidate == null || candidate.GetType() != targetType || freshOwners.ContainsKey(candidate) ||
                    !SavedEntitySourceMatches(target, candidate) ||
                    (freshFieldAliasReservations.TryGetValue(candidate, out int reservedNodeId) &&
                     reservedNodeId != target.Id)) {
                    continue;
                }
                if (matchedEntity != null) {
                    matchedEntity = null;
                    return false;
                }
                matchedEntity = candidate;
            }
            return matchedEntity != null;
        }

        private bool SavedEntitySourceMatches(AkronReconstructionNode entityNode, Entity candidate) {
            return !TryGetSavedEntityId(entityNode, out EntityID savedId) ||
                EntityIdsMatch(GetEntitySourceId(candidate), savedId);
        }

        private bool TryGetSavedEntityId(AkronReconstructionNode entityNode, out EntityID savedId) {
            savedId = default;
            AkronReconstructionValue sourceIdReference = FindReferenceField(
                entityNode,
                "<SourceId>k__BackingField");
            if (sourceIdReference == null ||
                !nodes.TryGetValue(sourceIdReference.NodeId, out AkronReconstructionNode sourceIdNode)) {
                return false;
            }
            AkronReconstructionField levelField = sourceIdNode.Fields?.FirstOrDefault(field =>
                field.Name == nameof(EntityID.Level) && field.Value?.Kind == ScalarValueKind);
            AkronReconstructionField idField = sourceIdNode.Fields?.FirstOrDefault(field =>
                field.Name == nameof(EntityID.ID) && field.Value?.Kind == ScalarValueKind);
            if (levelField == null || idField == null) {
                return false;
            }
            string room = (string) DecodeScalar(levelField.Value, levelField.Path);
            if (string.IsNullOrEmpty(room)) {
                return false;
            }
            savedId = new EntityID {
                Level = room,
                ID = (int) DecodeScalar(idField.Value, idField.Path)
            };
            return true;
        }

        private bool TryGetComponentOwnerNodes(
            AkronReconstructionNode componentNode,
            out AkronReconstructionNode componentListNode,
            out int ownerEntityId
        ) {
            componentListNode = null;
            ownerEntityId = 0;
            AkronReconstructionValue componentOwner = FindReferenceField(componentNode, "<Entity>k__BackingField");
            if (componentOwner == null ||
                !nodes.TryGetValue(componentOwner.NodeId, out AkronReconstructionNode ownerEntity)) {
                return false;
            }

            AkronReconstructionValue components = FindReferenceField(ownerEntity, "<Components>k__BackingField");
            if (components == null || !nodes.TryGetValue(components.NodeId, out componentListNode) ||
                ResolveType(componentListNode.TypeName, componentListNode.Path) != typeof(ComponentList)) {
                componentListNode = null;
                return false;
            }
            AkronReconstructionValue listOwner = FindReferenceField(componentListNode, "<Entity>k__BackingField");
            ownerEntityId = ownerEntity.Id;
            return componentOwner?.NodeId == ownerEntityId && listOwner?.NodeId == ownerEntityId;
        }

        private bool TryGetEntityListOwnerNode(
            AkronReconstructionNode entityNode,
            out AkronReconstructionNode entityListNode
        ) {
            entityListNode = null;
            List<AkronReconstructionNode> arrayParents = new List<AkronReconstructionNode>();
            if (entityNode.ParentKind == "array" && nodes.TryGetValue(entityNode.ParentNodeId, out AkronReconstructionNode firstParent)) {
                arrayParents.Add(firstParent);
            }
            if (savedArrayAliases.TryGetValue(entityNode.Id, out List<AkronReconstructionNode> aliases)) {
                arrayParents.AddRange(aliases);
            }

            foreach (AkronReconstructionNode arrayParent in arrayParents.Distinct()) {
                if (!TryGetFieldParent(arrayParent.Id, "_items", out AkronReconstructionNode entityStorageList) ||
                    !TryGetFieldParent(entityStorageList.Id, "entities", out AkronReconstructionNode candidate) ||
                    ResolveType(candidate.TypeName, candidate.Path) != typeof(EntityList)) {
                    continue;
                }
                if (entityListNode != null && entityListNode.Id != candidate.Id) {
                    entityListNode = null;
                    return false;
                }
                entityListNode = candidate;
            }
            return entityListNode != null;
        }

        private bool TryGetFieldParent(
            int childNodeId,
            string fieldName,
            out AkronReconstructionNode parent
        ) {
            parent = null;
            return nodes.TryGetValue(childNodeId, out AkronReconstructionNode child) &&
                   child.ParentKind == "field" &&
                   string.Equals(child.ParentFieldName, fieldName, StringComparison.Ordinal) &&
                   nodes.TryGetValue(child.ParentNodeId, out parent);
        }

        private static AkronReconstructionValue FindReferenceField(
            AkronReconstructionNode node,
            string fieldName
        ) {
            return (node.Fields ?? new List<AkronReconstructionField>())
                .FirstOrDefault(field =>
                    string.Equals(field.Name, fieldName, StringComparison.Ordinal) &&
                    field.Value?.Kind == ReferenceValueKind)
                ?.Value;
        }

        private bool IsStructurallyAuthenticDelegateTarget(AkronReconstructionNode targetNode, Type targetType) {
            if (targetNode.ParentKind != "delegate" ||
                !nodes.TryGetValue(targetNode.ParentNodeId, out AkronReconstructionNode delegateNode) ||
                targetNode.ParentDelegateIndex < 0 ||
                targetNode.ParentDelegateIndex >= delegateNode.DelegateCalls.Count) {
                return false;
            }
            AkronReconstructionDelegateCall call = delegateNode.DelegateCalls[targetNode.ParentDelegateIndex];
            if (call?.Kind != MethodDelegateCallKind) {
                return false;
            }
            MethodInfo method = ResolveMethod(call, delegateNode.Path);
            return TryGetAuthenticFreshDelegateCall(
                       delegateNode,
                       targetNode.ParentDelegateIndex,
                       targetType,
                       method,
                       out _) ||
                   freshStructuralDelegateCalls.Contains(
                       StructuralDelegateCallKey(GetDocumentStructuralPath(delegateNode), targetType, method)) ||
                   IsAuthenticatedBuiltInOwnedPureDelegateClosure(targetNode, targetType, delegateNode, method);
        }

        private bool IsAuthenticatedBuiltInOwnedPureDelegateClosure(
            AkronReconstructionNode targetNode,
            Type targetType,
            AkronReconstructionNode delegateNode,
            MethodInfo method
        ) {
            FieldInfo[] capturedFields = GetInstanceFields(targetType).ToArray();
            bool compilerSingleton = targetType.Name == "<>c" && capturedFields.Length == 0;
            if (!targetType.IsClass || !targetType.IsSealed ||
                targetType.Assembly != typeof(Ease).Assembly ||
                targetType.DeclaringType is not Type declaringType ||
                !declaringType.IsAbstract || !declaringType.IsSealed ||
                !targetType.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) ||
                typeof(IDisposable).IsAssignableFrom(targetType) ||
                targetType.GetMethod(
                    "Finalize",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) != null ||
                method.IsStatic || method.DeclaringType != targetType ||
                (!method.Name.StartsWith("<", StringComparison.Ordinal) && !compilerSingleton) ||
                delegateNode.ParentKind != "field" ||
                !nodes.TryGetValue(delegateNode.ParentNodeId, out AkronReconstructionNode ownerNode)) {
                return false;
            }

            Type ownerType = ResolveType(ownerNode.TypeName, ownerNode.Path);
            bool authenticatedOwner =
                authenticatedOwnedNestedStateNodes.Contains(ownerNode.Id) ||
                authenticatedOwnedComponentNodes.Contains(ownerNode.Id) ||
                authenticatedDelegateTargetNodes.Contains(ownerNode.Id) ||
                IsAuthenticatedFreshEntityOwnedNestedState(ownerNode, ownerType) ||
                IsAuthenticatedFreshRendererOwnedRuntimeState(ownerNode, ownerType) ||
                IsAuthenticatedRuntimeEntityOwnedState(ownerNode, ownerType) ||
                IsAuthenticatedReconstructedOwnedComponent(ownerNode, ownerType);
            if (!authenticatedOwner) {
                return false;
            }

            FieldInfo delegateField = ResolveField(
                delegateNode.ParentDeclaringTypeName,
                delegateNode.ParentFieldName,
                delegateNode.Path);
            Type delegateType = ResolveType(delegateNode.TypeName, delegateNode.Path);
            return delegateField.DeclaringType.IsAssignableFrom(ownerType) &&
                   delegateField.FieldType == delegateType &&
                   capturedFields.All(field =>
                       typeof(Delegate).IsAssignableFrom(field.FieldType) || IsScalarType(field.FieldType));
        }

        private bool TryGetAuthenticFreshDelegateCall(
            AkronReconstructionNode delegateNode,
            int callIndex,
            Type targetType,
            MethodInfo method,
            out Delegate freshCall
        ) {
            freshCall = null;
            Type delegateType = ResolveType(delegateNode.TypeName, delegateNode.Path);
            object candidate = Objects.TryGetValue(delegateNode.Id, out object resolvedDelegate)
                ? resolvedDelegate
                : null;
            if (candidate == null && TryResolveFreshFieldAlias(delegateNode, delegateType, out object fieldAlias)) {
                candidate = fieldAlias;
            }
            if (candidate == null && delegateNode.ParentKind == "array" &&
                Objects.TryGetValue(delegateNode.ParentNodeId, out object arrayParent) &&
                arrayParent is Array array && HasArrayIndex(array, delegateNode.ParentArrayIndices)) {
                candidate = array.GetValue(delegateNode.ParentArrayIndices.ToArray());
            }
            if (candidate == null && delegateNode.ParentKind == "field" &&
                Objects.TryGetValue(delegateNode.ParentNodeId, out object fieldParent)) {
                FieldInfo field = ResolveField(
                    delegateNode.ParentDeclaringTypeName,
                    delegateNode.ParentFieldName,
                    delegateNode.Path);
                if (field.DeclaringType.IsInstanceOfType(fieldParent)) {
                    candidate = field.GetValue(fieldParent);
                }
            }
            if (candidate is not Delegate freshDelegate) {
                return false;
            }
            Delegate[] calls = freshDelegate.GetInvocationList();
            if (callIndex < 0 || callIndex >= calls.Length) {
                return false;
            }
            Delegate call = calls[callIndex];
            if (call.Target == null || call.Target.GetType() != targetType ||
                !MethodIdentityMatches(call.Method, method)) {
                return false;
            }
            freshCall = call;
            return true;
        }

        private bool IsAuthenticatedBuiltInScreenWipe(AkronReconstructionNode node, Type type) {
            if (type.IsAbstract || type.Assembly != typeof(ScreenWipe).Assembly ||
                !typeof(ScreenWipe).IsAssignableFrom(type)) {
                return false;
            }

            FieldInfo[] expectedFields = GetInstanceFields(type).ToArray();
            if (node.Fields.Count != expectedFields.Length || expectedFields.Any(expected =>
                    !node.Fields.Any(captured =>
                        captured.Name == expected.Name &&
                        captured.DeclaringTypeName == TypeName(expected.DeclaringType)))) {
                return false;
            }

            return nodes.Values.Any(ownerNode =>
                typeof(Level).IsAssignableFrom(ResolveType(ownerNode.TypeName, ownerNode.Path)) &&
                resolvedFreshObjectNodes.Contains(ownerNode.Id) &&
                FindReferenceField(ownerNode, nameof(Level.Wipe))?.NodeId == node.Id);
        }

        private static bool IsExplicitlySafeReconstructionType(Type type) {
            // These shapes only store other validated values. Gameplay and mod
            // objects must exist with the same type at the same fresh-room path.
            if (type.IsArray || type.IsValueType || type == typeof(object)) {
                return true;
            }
            // MTexture is a mutable crop/draw wrapper, not the GPU resource.
            // Rebuild the wrapper and its aliases from saved fields while its
            // VirtualTexture child remains a separately authenticated anchor.
            if (typeof(MTexture).IsAssignableFrom(type)) {
                return true;
            }
            // Celeste uses fieldless Entity subclasses such as WaterSurface as
            // marker objects. Some exist only in the saved gameplay state, so
            // a fresh room cannot authenticate their path. They are safe to
            // recreate only when they come from Celeste itself, have no type
            // initializer or instance state, and declare no executable method
            // body beyond empty overrides.
            if (InertBuiltInEntityMarkerTypes.GetOrAdd(type, IsInertBuiltInEntityMarkerType)) {
                return true;
            }
            // Celeste creates trail entities only after the first active dash
            // trail. They can exist at the saved frame while being absent from
            // a clean load of the same room. Restrict this to the two built-in
            // visual runtime types instead of allowing arbitrary entities.
            if (type == typeof(TrailManager) || type == typeof(TrailManager.Snapshot) ||
                type == typeof(SoundEmitter)) {
                return true;
            }
            // Chooser<T> and its Choice records are mutable data containers
            // used by built-in sprite animations. Saved-only trail sprites can
            // own these records even when the clean room has no equivalent
            // animation path. Restrict reconstruction to Monocle's exact
            // generic types so mod-defined executable objects stay rejected.
            if (IsBuiltInChooserDataType(type)) {
                return true;
            }
            // AudioTrackState stores an event name and parameter values. It
            // owns no FMOD handle and is safe to rebuild when a saved coroutine
            // still references a track state that the clean room omitted.
            if (type == typeof(AudioTrackState)) {
                return true;
            }
            // Session.OldStats can remain reachable through a saved Celeste
            // coroutine after a cold room load. These built-in records own no
            // process resources, so rebuild the saved record graph here. The
            // post-restore stats boundary reapplies and masks cumulative time
            // and deaths so StartPos still cannot rewind those counters.
            if (type == typeof(AreaStats) || type == typeof(AreaModeStats)) {
                return true;
            }
            // ParticleType is a built-in particle configuration record. Its
            // texture wrappers and chooser remain separately validated graph
            // children, and the record owns no process or native resource.
            if (type == typeof(ParticleType)) {
                return true;
            }
            // TalkComponent creates this HUD entity only after its first
            // update. A Set frame can contain the prompt while the same clean
            // room has not created it yet. Limit the saved-only entity to
            // Celeste's exact prompt type; its handler and Wiggler ownership
            // still pass through the normal graph assignment checks.
            if (type == typeof(TalkComponent.TalkComponentUI)) {
                return true;
            }
            // Runtime effects also use short-lived data records that have no
            // fresh counterpart, such as Water.Ripple. Reconstruct only plain
            // leaf records that cannot execute instance code. Their fields are
            // still decoded and validated through the normal graph rules.
            if (PassiveDataObjectTypes.GetOrAdd(type, IsPassiveDataObjectType)) {
                return true;
            }
            // CoreLib comparer implementations are immutable process helpers.
            // Dictionaries can share the same singleton from a different
            // owner path after a clean process start. Restrict this exception
            // to CoreLib so a mod-defined comparer cannot bypass room-path
            // authentication.
            if (type.Assembly == typeof(string).Assembly && type.GetInterfaces().Any(interfaceType =>
                    interfaceType.IsGenericType &&
                    (interfaceType.GetGenericTypeDefinition() == typeof(IEqualityComparer<>) ||
                     interfaceType.GetGenericTypeDefinition() == typeof(IComparer<>)))) {
                return true;
            }
            // ConcurrentDictionary and other CoreLib collections keep their
            // entries in private nested storage records. Those records have
            // the same data-only trust boundary as the public collection that
            // owns them. A clean room can have different buckets when saved
            // entries are missing, so it cannot authenticate each record by
            // its exact fresh path.
            return IsSupportedCollectionType(type) || IsCoreCollectionStorageType(type);
        }

        private static bool IsSupportedCollectionType(Type type) {
            if (!type.IsGenericType) {
                return false;
            }
            Type genericType = type.GetGenericTypeDefinition();
            return genericType == typeof(List<>) ||
                   genericType == typeof(Dictionary<,>) ||
                   genericType == typeof(HashSet<>) ||
                   genericType == typeof(Queue<>) ||
                   genericType == typeof(Stack<>) ||
                   genericType == typeof(LinkedList<>) ||
                   genericType == typeof(LinkedListNode<>) ||
                   genericType == typeof(SortedDictionary<,>) ||
                   genericType == typeof(SortedList<,>) ||
                   genericType == typeof(SortedSet<>) ||
                   genericType == typeof(ConcurrentDictionary<,>) ||
                   genericType == typeof(ConcurrentQueue<>) ||
                   genericType == typeof(ConcurrentStack<>);
        }

        private static bool IsCoreCollectionStorageType(Type type) {
            string typeName = (type.IsGenericType ? type.GetGenericTypeDefinition() : type).FullName ?? string.Empty;
            int nestedTypeSeparator = typeName.IndexOf('+');
            return nestedTypeSeparator > 0 &&
                   IsSupportedCollectionGenericDefinitionName(
                       typeName.Substring(0, nestedTypeSeparator),
                       type.Assembly);
        }

        private static bool IsSupportedCollectionGenericDefinitionName(string typeName, Assembly assembly) {
            return assembly == typeof(List<>).Assembly &&
                       (typeName == typeof(List<>).FullName ||
                        typeName == typeof(Dictionary<,>).FullName ||
                        typeName == typeof(HashSet<>).FullName ||
                        typeName == typeof(Queue<>).FullName ||
                        typeName == typeof(Stack<>).FullName ||
                        typeName == typeof(LinkedList<>).FullName ||
                        typeName == typeof(LinkedListNode<>).FullName ||
                        typeName == typeof(SortedDictionary<,>).FullName ||
                        typeName == typeof(SortedList<,>).FullName ||
                        typeName == typeof(SortedSet<>).FullName) ||
                   assembly == typeof(ConcurrentDictionary<,>).Assembly &&
                       (typeName == typeof(ConcurrentDictionary<,>).FullName ||
                        typeName == typeof(ConcurrentQueue<>).FullName ||
                        typeName == typeof(ConcurrentStack<>).FullName);
        }

        private static bool IsInertBuiltInEntityMarkerType(Type type) {
            if (type.Assembly != typeof(Entity).Assembly ||
                type.BaseType != typeof(Entity) ||
                type.TypeInitializer != null ||
                type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Length != 0) {
                return false;
            }

            foreach (MethodInfo method in type.GetMethods(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)) {
                if (!method.IsVirtual ||
                    method.ReturnType != typeof(void) ||
                    method.GetBaseDefinition().DeclaringType == method.DeclaringType ||
                    !HasOnlyEmptyMethodBody(method)) {
                    return false;
                }
            }
            return true;
        }

        private static bool IsPassiveDataObjectType(Type type) {
            return type.IsClass &&
                   !type.IsAbstract &&
                   !type.IsGenericType &&
                   type.BaseType == typeof(object) &&
                   type.TypeInitializer == null &&
                   type.GetMethods(
                       BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Length == 0;
        }

        private static bool IsBuiltInChooserDataType(Type type) {
            if (!type.IsGenericType || type.Assembly != typeof(Chooser<>).Assembly) {
                return false;
            }
            Type genericDefinition = type.GetGenericTypeDefinition();
            return genericDefinition == typeof(Chooser<>) ||
                   genericDefinition.DeclaringType == typeof(Chooser<>) && genericDefinition.Name == "Choice";
        }

        private static bool HasOnlyEmptyMethodBody(MethodInfo method) {
            byte[] il = method.GetMethodBody()?.GetILAsByteArray();
            if (il == null || il.Length == 0 || il[il.Length - 1] != 0x2a) {
                return false;
            }
            for (int index = 0; index < il.Length - 1; index++) {
                if (il[index] != 0x00) {
                    return false;
                }
            }
            return true;
        }

        private static string StructuralDelegateCallKey(
            IEnumerable<AkronReconstructionPathStep> delegatePath,
            Type targetType,
            MethodInfo method
        ) {
            StringBuilder key = new StringBuilder(TypeName(targetType));
            foreach (AkronReconstructionPathStep step in delegatePath ?? Enumerable.Empty<AkronReconstructionPathStep>()) {
                if (step.Kind == "field") {
                    key.Append("|field:").Append(step.DeclaringTypeName).Append('.').Append(step.FieldName);
                } else if (step.Kind == "array") {
                    // Entity, component, coroutine, and callback arrays can be
                    // rebuilt in a different order after a clean room load.
                    // The complete owner field chain, target type, and method
                    // remain required below.
                    key.Append("[*]");
                }
            }
            return key.Append("|method=").Append(DelegateMethodKey(method)).ToString();
        }

        private object FindFreshResource(
            AkronReconstructionNode node,
            Type type,
            out bool matchedByStructuralPath
        ) {
            matchedByStructuralPath = false;
            List<object> keyMatches = freshResources.TryGetValue(node.ResourceKey, out List<object> matches)
                ? matches.Where(match => !freshOwners.ContainsKey(match)).ToList()
                : new List<object>();
            if (keyMatches.Count == 1) {
                return keyMatches[0];
            }

            List<AkronReconstructionPathStep> structuralPath = GetFreshStructuralPath(node);
            string structuralPathKey = StructuralResourcePathKey(
                type,
                structuralPath,
                wildcardListStorageIndices: true);
            List<object> structuralMatches = freshResourcesByStructuralPath
                .TryGetValue(structuralPathKey, out List<object> pathMatches)
                ? pathMatches
                .Where(match => !freshOwners.ContainsKey(match))
                .ToList()
                : new List<object>();
            if (structuralMatches.Count == 1) {
                // Runtime-owned resources in lists often receive a new index
                // and generated name after a clean room load. In that case the
                // owner field is the stable identity. A fixed field still
                // requires its explicit resource key to match.
                matchedByStructuralPath = HasListStorageIndex(structuralPath);
                return structuralMatches[0];
            }
            if (keyMatches.Count > 1 || structuralMatches.Count > 1) {
                throw new AkronReconstructionException(
                    node.Path,
                    "fresh resource key is ambiguous;matches=" +
                    Math.Max(keyMatches.Count, structuralMatches.Count).ToString(CultureInfo.InvariantCulture) +
                    ";key=" + node.ResourceKey);
            }

            // Reflection objects can be valid process anchors without being
            // reachable from the freshly loaded room. A tracker, for example,
            // only contains keys for types used in that room. Resolve those
            // anchors from the current process by their stable key instead of
            // making incidental tracker contents part of the snapshot contract.
            object detachedResource = owner.resolveDetachedLiveResource?.Invoke(type, node.ResourceKey);
            if (detachedResource != null &&
                detachedResource.GetType() == type &&
                string.Equals(node.ResourceKey, owner.GetTypedResourceKey(detachedResource), StringComparison.Ordinal)) {
                return detachedResource;
            }
            throw new AkronReconstructionException(
                node.Path,
                "fresh resource key and structural path are unavailable;key=" + node.ResourceKey);
        }

        private static string StructuralResourcePathKey(
            Type resourceType,
            IEnumerable<AkronReconstructionPathStep> path,
            bool wildcardListStorageIndices = false
        ) {
            StringBuilder key = new StringBuilder(TypeName(resourceType));
            AkronReconstructionPathStep previous = null;
            foreach (AkronReconstructionPathStep step in path ?? Enumerable.Empty<AkronReconstructionPathStep>()) {
                if (step.Kind == "field") {
                    key.Append("|field:").Append(step.DeclaringTypeName).Append('.').Append(step.FieldName);
                } else if (step.Kind == "array") {
                    bool listStorageIndex = previous?.Kind == "field" &&
                                            string.Equals(previous.FieldName, "_items", StringComparison.Ordinal) &&
                                            previous.DeclaringTypeName?.StartsWith("System.Collections.Generic.List`1", StringComparison.Ordinal) == true;
                    key.Append(wildcardListStorageIndices && listStorageIndex
                        ? "[*]"
                        : "[" + string.Join(",", step.ArrayIndices ?? new List<int>()) + "]");
                }
                previous = step;
            }
            return key.ToString();
        }

        private static bool HasListStorageIndex(IEnumerable<AkronReconstructionPathStep> path) {
            AkronReconstructionPathStep previous = null;
            foreach (AkronReconstructionPathStep step in path ?? Enumerable.Empty<AkronReconstructionPathStep>()) {
                if (step.Kind == "array" && previous?.Kind == "field" &&
                    previous.FieldName == "_items" &&
                    previous.DeclaringTypeName?.StartsWith("System.Collections.Generic.List`1", StringComparison.Ordinal) == true) {
                    return true;
                }
                previous = step;
            }
            return false;
        }

        private static List<AkronReconstructionPathStep> AppendFreshPath(
            IEnumerable<AkronReconstructionPathStep> path,
            AkronReconstructionPathStep next
        ) {
            List<AkronReconstructionPathStep> appended = (path ?? Enumerable.Empty<AkronReconstructionPathStep>())
                .Select(step => new AkronReconstructionPathStep {
                    Kind = step.Kind,
                    DeclaringTypeName = step.DeclaringTypeName,
                    FieldName = step.FieldName,
                    ArrayIndices = new List<int>(step.ArrayIndices ?? new List<int>())
                })
                .ToList();
            appended.Add(next);
            return appended;
        }

        public void ValidateAssignments() {
            foreach (AkronReconstructionNode node in document.Nodes.OrderBy(node => node.Id)) {
                if (node.Kind == AnchorKind || node.Kind == PersistentResourceKind ||
                    node.Kind == DelegateKind || node.Kind == EventInstanceKind) {
                    continue;
                }

                object target = Objects[node.Id];
                if (node.Kind == ArrayKind) {
                    ValidateArrayAssignments(node, (Array) target);
                    continue;
                }

                foreach (AkronReconstructionField savedField in node.Fields) {
                    FieldInfo field = ResolveField(savedField.DeclaringTypeName, savedField.Name, savedField.Path);
                    if (!field.DeclaringType.IsInstanceOfType(target)) {
                        throw new AkronReconstructionException(savedField.Path, "field does not belong to the restored object");
                    }
                    object fieldValue = ResolveValue(savedField.Value, savedField.Path);
                    ValidateAssignable(field.FieldType, fieldValue, savedField.Path);
                    TrackDisplacedEventInstance(field.GetValue(target), fieldValue);
                    assignments.Add(() => field.SetValue(target, fieldValue));
                }
            }
        }

        public void ApplyAssignments() {
            // Capture assigns node IDs before visiting children. Apply in the
            // reverse order so boxed structs are fully populated before their
            // value is copied into a parent field or array slot.
            for (int index = assignments.Count - 1; index >= 0; index--) {
                assignments[index]();
            }
            ValidateAndNormalizeDerivedMembershipSets(Objects.Values);
        }

        private void ValidateArrayAssignments(AkronReconstructionNode node, Array target) {
            if (node.PackedPrimitiveArrayBytes != null) {
                if (!CanPackPrimitiveArray(target) ||
                    Buffer.ByteLength(target) != node.PackedPrimitiveArrayBytes.Length) {
                    throw new AkronReconstructionException(node.Path, "packed primitive array size differs");
                }
                assignments.Add(() => Buffer.BlockCopy(
                    node.PackedPrimitiveArrayBytes,
                    0,
                    target,
                    0,
                    node.PackedPrimitiveArrayBytes.Length));
                return;
            }
            if (target.LongLength != node.Items.Count) {
                throw new AkronReconstructionException(node.Path, "array item count differs");
            }

            Type elementType = target.GetType().GetElementType();
            int[] itemIndices = GetInitialArrayIndices(target);
            for (int index = 0; index < node.Items.Count; index++) {
                AkronReconstructionValue savedItem = node.Items[index];
                if (savedItem == null || savedItem.Kind == NullValueKind) {
                    if (elementType.IsValueType && Nullable.GetUnderlyingType(elementType) == null) {
                        throw new AkronReconstructionException(
                            ArrayPath(node.Path, itemIndices),
                            "null cannot be assigned to " + elementType.FullName);
                    }
                    IncrementArrayIndices(target, itemIndices);
                    continue;
                }
                string itemPath = ArrayPath(node.Path, itemIndices);
                object itemValue = ResolveValue(savedItem, itemPath);
                ValidateAssignable(elementType, itemValue, itemPath);
                TrackDisplacedEventInstance(target.GetValue(itemIndices), itemValue);
                IncrementArrayIndices(target, itemIndices);
            }
            assignments.Add(() => {
                int[] assignmentIndices = GetInitialArrayIndices(target);
                for (int assignmentIndex = 0; assignmentIndex < node.Items.Count; assignmentIndex++) {
                    target.SetValue(ResolveValue(node.Items[assignmentIndex], node.Path), assignmentIndices);
                    IncrementArrayIndices(target, assignmentIndices);
                }
            });
        }

        private void TrackDisplacedEventInstance(object currentValue, object restoredValue) {
            if (currentValue is EventInstance currentEvent &&
                restoredValue is EventInstance restoredEvent &&
                !ReferenceEquals(currentEvent, restoredEvent)) {
                displacedEventInstances.Add(currentEvent);
            }
        }

        private object ResolveFreshPath(IEnumerable<AkronReconstructionPathStep> path, string errorPath) {
            object current = freshRoot;
            foreach (AkronReconstructionPathStep step in path ?? Enumerable.Empty<AkronReconstructionPathStep>()) {
                if (current == null) {
                    return null;
                }
                if (step.Kind == "field") {
                    FieldInfo field = ResolveField(step.DeclaringTypeName, step.FieldName, errorPath);
                    if (!field.DeclaringType.IsInstanceOfType(current)) {
                        return null;
                    }
                    current = field.GetValue(current);
                } else if (step.Kind == "array") {
                    if (current is not Array array || !HasArrayIndex(array, step.ArrayIndices?.ToArray())) {
                        return null;
                    }
                    current = array.GetValue(step.ArrayIndices.ToArray());
                } else {
                    throw new AkronReconstructionException(errorPath, "fresh path step is unsupported");
                }
            }
            return current;
        }

        private object ResolveFreshObject(AkronReconstructionNode node) {
            if (node.FreshPath != null && node.FreshPath.Count > 0) {
                return ResolveFreshPath(node.FreshPath, node.Path);
            }
            if (node.ParentNodeId <= 0 || !Objects.TryGetValue(node.ParentNodeId, out object parent)) {
                return null;
            }
            switch (node.ParentKind) {
                case "field": {
                    FieldInfo field = ResolveField(
                        node.ParentDeclaringTypeName,
                        node.ParentFieldName,
                        node.Path);
                    return field.DeclaringType.IsInstanceOfType(parent)
                        ? field.GetValue(parent)
                        : null;
                }
                case "array":
                    return parent is Array array && HasArrayIndex(array, node.ParentArrayIndices)
                        ? array.GetValue(node.ParentArrayIndices.ToArray())
                        : null;
                default:
                    // Delegate targets have no ordinary reflected owner edge.
                    // Reconstruct them, or resolve live resources by stable key.
                    return null;
            }
        }

        private List<AkronReconstructionPathStep> GetFreshStructuralPath(AkronReconstructionNode node) {
            List<AkronReconstructionPathStep> suffix = new List<AkronReconstructionPathStep>();
            AkronReconstructionNode current = node;
            while (current != null && current.Id != document.RootNodeId) {
                if (current.FreshPath != null && current.FreshPath.Count > 0) {
                    List<AkronReconstructionPathStep> path = ClonePathSteps(current.FreshPath);
                    suffix.Reverse();
                    path.AddRange(suffix);
                    return path;
                }
                if (current.ParentKind == "field") {
                    suffix.Add(new AkronReconstructionPathStep {
                        Kind = "field",
                        DeclaringTypeName = current.ParentDeclaringTypeName,
                        FieldName = current.ParentFieldName
                    });
                } else if (current.ParentKind == "array") {
                    suffix.Add(new AkronReconstructionPathStep {
                        Kind = "array",
                        ArrayIndices = new List<int>(current.ParentArrayIndices ?? new List<int>())
                    });
                } else {
                    return new List<AkronReconstructionPathStep>();
                }
                current = nodes.TryGetValue(current.ParentNodeId, out AkronReconstructionNode parent)
                    ? parent
                    : null;
            }
            suffix.Reverse();
            return suffix;
        }

        private List<AkronReconstructionPathStep> GetDocumentStructuralPath(AkronReconstructionNode node) {
            List<AkronReconstructionPathStep> path = new List<AkronReconstructionPathStep>();
            AkronReconstructionNode current = node;
            while (current != null && current.Id != document.RootNodeId) {
                if (current.ParentKind == "field") {
                    path.Add(new AkronReconstructionPathStep {
                        Kind = "field",
                        DeclaringTypeName = current.ParentDeclaringTypeName,
                        FieldName = current.ParentFieldName
                    });
                } else if (current.ParentKind == "array") {
                    path.Add(new AkronReconstructionPathStep {
                        Kind = "array",
                        ArrayIndices = new List<int>(current.ParentArrayIndices ?? new List<int>())
                    });
                } else if (current.ParentKind == "delegate") {
                    // Delegate targets share their owning delegate's fresh
                    // structural path during capture.
                } else {
                    return new List<AkronReconstructionPathStep>();
                }
                current = nodes.TryGetValue(current.ParentNodeId, out AkronReconstructionNode parent)
                    ? parent
                    : null;
            }
            path.Reverse();
            return path;
        }

        private static List<AkronReconstructionPathStep> ClonePathSteps(
            IEnumerable<AkronReconstructionPathStep> path
        ) {
            return (path ?? Enumerable.Empty<AkronReconstructionPathStep>())
                .Select(step => new AkronReconstructionPathStep {
                    Kind = step.Kind,
                    DeclaringTypeName = step.DeclaringTypeName,
                    FieldName = step.FieldName,
                    ArrayIndices = new List<int>(step.ArrayIndices ?? new List<int>())
                })
                .ToList();
        }

        private object CreateDelegate(AkronReconstructionNode node) {
            Type delegateType = ResolveType(node.TypeName, node.Path);
            Delegate combined = null;
            for (int index = 0; index < node.DelegateCalls.Count; index++) {
                AkronReconstructionDelegateCall call = node.DelegateCalls[index];
                MethodInfo method = ResolveMethod(call, node.Path);
                object target;
                if (call.Kind == DetourNextDelegateCallKind) {
                    MethodInfo hookTarget = ResolveHookTarget(call, node.Path);
                    if (!TryResolveDetourNextMethod(method, hookTarget, out method)) {
                        throw new AkronReconstructionException(node.Path, "saved hook position is unavailable in the current detour chain");
                    }
                    target = null;
                } else if (call.Kind == MethodDelegateCallKind) {
                    target = ResolveValue(call.Target, node.Path + ".<target>[" + index.ToString(CultureInfo.InvariantCulture) + "]");
                    string methodKey = DelegateMethodKey(method);
                    bool authentic = target == null
                        ? freshStaticDelegateMethods.Contains(methodKey)
                        : freshInstanceDelegateMethods.TryGetValue(target, out HashSet<string> methods) && methods.Contains(methodKey);
                    if (!authentic && target != null) {
                        authentic = freshStructuralDelegateCalls.Contains(
                            StructuralDelegateCallKey(GetDocumentStructuralPath(node), target.GetType(), method));
                    }
                    if (!authentic && target != null) {
                        authentic = TryGetAuthenticFreshDelegateCall(
                            node,
                            index,
                            target.GetType(),
                            method,
                            out _);
                    }
                    if (!authentic && target != null) {
                        authentic = IsAuthenticatedRuntimeEntityOwnedDelegateCall(
                            node,
                            call,
                            target,
                            method);
                    }
                    if (!authentic && target != null) {
                        authentic = IsAuthenticatedBuiltInOwnedPureDelegateCall(
                            node,
                            call,
                            target,
                            method);
                    }
                    if (!authentic) {
                        throw new AkronReconstructionException(
                            node.Path,
                            "delegate method is not authentic to the fresh room");
                    }
                } else {
                    throw new AkronReconstructionException(node.Path, "delegate call kind is unsupported");
                }
                Delegate restoredCall = target == null
                    ? method.CreateDelegate(delegateType)
                    : method.CreateDelegate(delegateType, target);
                combined = combined == null ? restoredCall : Delegate.Combine(combined, restoredCall);
            }
            if (combined == null) {
                throw new AkronReconstructionException(node.Path, "delegate has no invocation entries");
            }
            return combined;
        }

        private bool IsAuthenticatedBuiltInOwnedPureDelegateCall(
            AkronReconstructionNode delegateNode,
            AkronReconstructionDelegateCall call,
            object targetObject,
            MethodInfo method
        ) {
            if (call.Target?.Kind != ReferenceValueKind ||
                !nodes.TryGetValue(call.Target.NodeId, out AkronReconstructionNode targetNode) ||
                !authenticatedDelegateTargetNodes.Contains(targetNode.Id) ||
                !Objects.TryGetValue(targetNode.Id, out object restoredTarget) ||
                !ReferenceEquals(restoredTarget, targetObject)) {
                return false;
            }
            return IsAuthenticatedBuiltInOwnedPureDelegateClosure(
                targetNode,
                targetObject.GetType(),
                delegateNode,
                method);
        }

        private bool IsAuthenticatedRuntimeEntityOwnedDelegateCall(
            AkronReconstructionNode delegateNode,
            AkronReconstructionDelegateCall call,
            object targetObject,
            MethodInfo method
        ) {
            if (call.Target?.Kind != ReferenceValueKind || method.IsStatic ||
                !nodes.TryGetValue(call.Target.NodeId, out AkronReconstructionNode targetNode) ||
                !Objects.TryGetValue(targetNode.Id, out object restoredTarget) ||
                !ReferenceEquals(restoredTarget, targetObject) ||
                ResolveType(targetNode.TypeName, targetNode.Path) != targetObject.GetType() ||
                method.DeclaringType != targetObject.GetType() ||
                !method.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) ||
                delegateNode.ParentKind != "field" ||
                !nodes.TryGetValue(delegateNode.ParentNodeId, out AkronReconstructionNode componentNode)) {
                return false;
            }

            Type componentType = ResolveType(componentNode.TypeName, componentNode.Path);
            if (componentType.Assembly != typeof(Component).Assembly) {
                return false;
            }
            bool authenticatedEntity = authenticatedRuntimeEntityNodes.Contains(targetNode.Id) ||
                                       IsAuthenticatedBuiltInRuntimeEntity(targetNode, targetObject.GetType());
            bool authenticatedComponent = authenticatedOwnedComponentNodes.Contains(componentNode.Id) ||
                                          IsAuthenticatedReconstructedOwnedComponent(componentNode, componentType);
            if (!authenticatedEntity || !authenticatedComponent ||
                !TryGetComponentOwnerNodes(componentNode, out _, out int ownerEntityId) ||
                ownerEntityId != targetNode.Id) {
                return false;
            }

            // These proofs can be reached before the normal reference-edge pass. Cache
            // them once so later callback and alias checks do not rescan the room graph.
            authenticatedRuntimeEntityNodes.Add(targetNode.Id);
            authenticatedOwnedComponentNodes.Add(componentNode.Id);
            FieldInfo delegateField = ResolveField(
                delegateNode.ParentDeclaringTypeName,
                delegateNode.ParentFieldName,
                delegateNode.Path);
            return delegateField.DeclaringType.IsAssignableFrom(componentType) &&
                   delegateField.FieldType == ResolveType(delegateNode.TypeName, delegateNode.Path);
        }

        private object ResolveValue(AkronReconstructionValue value, string path) {
            if (value == null || value.Kind == NullValueKind) {
                return null;
            }
            if (value.Kind == ScalarValueKind) {
                return DecodeScalar(value, path);
            }
            if (value.Kind == ReferenceValueKind && Objects.TryGetValue(value.NodeId, out object referenced)) {
                return referenced;
            }
            throw new AkronReconstructionException(path, "saved value reference is invalid");
        }

        private static Array CreateArray(Type arrayType, AkronReconstructionNode node, string path) {
            Type elementType = arrayType.GetElementType();
            if (elementType == null || node.ArrayLengths.Count == 0 || node.ArrayLengths.Count != node.ArrayLowerBounds.Count) {
                throw new AkronReconstructionException(path, "array shape is invalid");
            }
            if (node.ArrayLengths.Count > MaxRestoredArrayRank || node.ArrayLengths.Count != arrayType.GetArrayRank()) {
                throw new AkronReconstructionException(path, "array rank exceeds the supported limit");
            }
            long elementCount = 1;
            for (int dimension = 0; dimension < node.ArrayLengths.Count; dimension++) {
                int length = node.ArrayLengths[dimension];
                int lowerBound = node.ArrayLowerBounds[dimension];
                long upperBound = (long) lowerBound + length - 1L;
                if (length < 0 ||
                    length > 0 && (upperBound < int.MinValue || upperBound > int.MaxValue)) {
                    throw new AkronReconstructionException(path, "array bounds are invalid");
                }
                if (length > MaxRestoredArrayBytes) {
                    throw new AkronReconstructionException(path, "array allocation exceeds the snapshot limit");
                }
                if (length == 0) {
                    elementCount = 0;
                    continue;
                }
                if (elementCount > MaxRestoredArrayBytes / length) {
                    throw new AkronReconstructionException(path, "array allocation exceeds the snapshot limit");
                }
                elementCount *= length;
            }
            if (node.PackedPrimitiveArrayBytes == null && elementCount != (node.Items?.Count ?? 0)) {
                throw new AkronReconstructionException(path, "array item count differs");
            }
            if (node.PackedPrimitiveArrayBytes != null) {
                if (!CanPackPrimitiveElementType(elementType)) {
                    throw new AkronReconstructionException(path, "packed primitive array element type is invalid");
                }
                long packedByteCount = elementCount * GetPackedPrimitiveElementSize(elementType);
                if (packedByteCount != node.PackedPrimitiveArrayBytes.LongLength) {
                    throw new AkronReconstructionException(path, "packed primitive array size differs");
                }
            }
            long elementSize = EstimateArrayElementSize(elementType);
            if (elementCount > 0 && elementSize > MaxRestoredArrayBytes / elementCount) {
                throw new AkronReconstructionException(path, "array allocation exceeds the snapshot limit");
            }
            return Array.CreateInstance(elementType, node.ArrayLengths.ToArray(), node.ArrayLowerBounds.ToArray());
        }

        private static long EstimateArrayElementSize(Type elementType) {
            if (!elementType.IsValueType) {
                return IntPtr.Size;
            }
            try {
                return Math.Max(1, Marshal.SizeOf(elementType));
            } catch (ArgumentException) {
                // Non-marshallable managed structs still occupy at least one
                // pointer-sized slot. The total element cap remains bounded.
                return IntPtr.Size;
            }
        }

        private static bool ArrayShapeMatches(Array array, AkronReconstructionNode node) {
            if (array == null || node.ArrayLengths.Count != array.Rank || node.ArrayLowerBounds.Count != array.Rank) {
                return false;
            }
            for (int dimension = 0; dimension < array.Rank; dimension++) {
                if (array.GetLength(dimension) != node.ArrayLengths[dimension] ||
                    array.GetLowerBound(dimension) != node.ArrayLowerBounds[dimension]) {
                    return false;
                }
            }
            return true;
        }

        private static void ValidateAssignable(Type targetType, object value, string path) {
            if (value == null) {
                if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null) {
                    throw new AkronReconstructionException(path, "null cannot be assigned to " + targetType.FullName);
                }
                return;
            }
            if (!targetType.IsInstanceOfType(value)) {
                throw new AkronReconstructionException(path, "value type " + value.GetType().FullName + " cannot be assigned to " + targetType.FullName);
            }
        }
    }

    private sealed class VerificationContext {
        private readonly AkronReconstructionGraph owner;
        private readonly AkronReconstructionDocument document;
        private readonly Dictionary<int, object> objects;
        private readonly Dictionary<int, AkronReconstructionNode> nodes;
        private readonly HashSet<string> masks;

        public VerificationContext(
            AkronReconstructionGraph owner,
            AkronReconstructionDocument document,
            Dictionary<int, object> objects,
            HashSet<string> masks
        ) {
            this.owner = owner;
            this.document = document;
            this.objects = objects;
            nodes = document.Nodes.ToDictionary(node => node.Id);
            this.masks = masks;
        }

        public void Verify() {
            ValidateAndNormalizeDerivedMembershipSets(objects.Values);
            foreach (AkronReconstructionNode node in document.Nodes.OrderBy(node => node.Id)) {
                if (!objects.TryGetValue(node.Id, out object current) || current == null) {
                    throw new AkronReconstructionException(node.Path, "restored node is missing");
                }
                Type expectedType = ResolveType(node.TypeName, node.Path);
                if (current.GetType() != expectedType) {
                    throw new AkronReconstructionException(node.Path, "restored node type differs");
                }
                if (IsDerivedMembershipNode(node)) {
                    continue;
                }
                if (node.Kind == AnchorKind || IsMasked(node.Path)) {
                    continue;
                }
                if (node.Kind == PersistentResourceKind) {
                    if (owner.resourceAdapter?.Verify(node.ResourcePayload, current) != true) {
                        throw new AkronReconstructionException(node.Path, "persistent resource state differs");
                    }
                } else if (node.Kind == DelegateKind) {
                    VerifyDelegate(node, (Delegate) current);
                } else if (node.Kind == EventInstanceKind) {
                    VerifyEventInstance(node, (EventInstance) current);
                } else if (node.Kind == ArrayKind) {
                    VerifyArray(node, (Array) current);
                } else {
                    VerifyObject(node, current);
                }
            }
        }

        private void VerifyObject(AkronReconstructionNode node, object current) {
            foreach (AkronReconstructionField savedField in node.Fields) {
                if (IsMasked(savedField.Path)) {
                    continue;
                }
                if (IsDerivedMembershipField(current.GetType(), savedField.Name)) {
                    continue;
                }
                FieldInfo field = ResolveField(savedField.DeclaringTypeName, savedField.Name, savedField.Path);
                VerifyValue(savedField.Value, field.GetValue(current), savedField.Path);
            }
        }

        private bool IsDerivedMembershipNode(AkronReconstructionNode node) {
            AkronReconstructionNode current = node;
            while (current.ParentNodeId > 0 && nodes.TryGetValue(current.ParentNodeId, out AkronReconstructionNode parent)) {
                Type parentType = ResolveType(parent.TypeName, parent.Path);
                if (current.ParentKind == "field" &&
                    IsDerivedMembershipField(parentType, current.ParentFieldName)) {
                    return true;
                }
                current = parent;
            }
            return false;
        }

        private static bool IsDerivedMembershipField(Type ownerType, string fieldName) {
            return (ownerType == typeof(EntityList) || ownerType == typeof(ComponentList)) &&
                   fieldName is "current" or "adding" or "removing";
        }

        private void VerifyArray(AkronReconstructionNode node, Array current) {
            if (node.PackedPrimitiveArrayBytes != null) {
                if (!CanPackPrimitiveArray(current) ||
                    Buffer.ByteLength(current) != node.PackedPrimitiveArrayBytes.Length) {
                    throw new AkronReconstructionException(node.Path, "packed primitive array size differs");
                }
                byte[] currentBytes = new byte[node.PackedPrimitiveArrayBytes.Length];
                Buffer.BlockCopy(current, 0, currentBytes, 0, currentBytes.Length);
                if (!currentBytes.AsSpan().SequenceEqual(node.PackedPrimitiveArrayBytes)) {
                    throw new AkronReconstructionException(node.Path, "packed primitive array value differs");
                }
                return;
            }
            if (current.LongLength != node.Items.Count) {
                throw new AkronReconstructionException(node.Path, "array item count differs");
            }
            int[] itemIndices = GetInitialArrayIndices(current);
            for (int index = 0; index < node.Items.Count; index++) {
                AkronReconstructionValue expected = node.Items[index];
                object actual = current.GetValue(itemIndices);
                if ((expected == null || expected.Kind == NullValueKind) && actual == null) {
                    IncrementArrayIndices(current, itemIndices);
                    continue;
                }
                string path = ArrayPath(node.Path, itemIndices);
                if (!IsMasked(path)) {
                    VerifyValue(expected, actual, path);
                }
                IncrementArrayIndices(current, itemIndices);
            }
        }

        private void VerifyDelegate(AkronReconstructionNode node, Delegate current) {
            Delegate[] calls = current.GetInvocationList();
            if (calls.Length != node.DelegateCalls.Count) {
                throw new AkronReconstructionException(node.Path, "delegate invocation count differs");
            }
            for (int index = 0; index < calls.Length; index++) {
                AkronReconstructionDelegateCall expected = node.DelegateCalls[index];
                MethodInfo expectedMethod = ResolveMethod(expected, node.Path);
                if (expected.Kind == DetourNextDelegateCallKind) {
                    MethodInfo hookTarget = ResolveHookTarget(expected, node.Path);
                    if (!TryResolveDetourNextMethod(expectedMethod, hookTarget, out expectedMethod)) {
                        throw new AkronReconstructionException(node.Path, "saved hook position is unavailable in the current detour chain");
                    }
                } else if (expected.Kind != MethodDelegateCallKind) {
                    throw new AkronReconstructionException(node.Path, "delegate call kind is unsupported");
                }
                if (calls[index].Method != expectedMethod) {
                    throw new AkronReconstructionException(node.Path, "delegate method differs");
                }
                if (expected.Kind == DetourNextDelegateCallKind) {
                    if (calls[index].Target != null) {
                        throw new AkronReconstructionException(node.Path, "hook trampoline target differs");
                    }
                } else {
                    VerifyValue(expected.Target, calls[index].Target, node.Path + ".<target>[" + index.ToString(CultureInfo.InvariantCulture) + "]");
                }
            }
        }

        private void VerifyEventInstance(AkronReconstructionNode node, EventInstance current) {
            AkronPersistentEventInstanceState actual = AkronEventInstanceUtils.CapturePersistentState(current);
            if (!PersistentEventStatesMatch(node.EventInstance, actual)) {
                throw new AkronReconstructionException(node.Path, "FMOD event state differs");
            }
        }

        private void VerifyValue(AkronReconstructionValue expected, object current, string path) {
            if (expected == null || expected.Kind == NullValueKind) {
                if (current != null) {
                    throw new AkronReconstructionException(path, "expected null");
                }
                return;
            }
            if (expected.Kind == ScalarValueKind) {
                string actual = current == null ? "<null>" : EncodeScalar(current, current.GetType(), path);
                if (current == null || current.GetType() != ResolveType(expected.TypeName, path) ||
                    !string.Equals(actual, expected.Scalar, StringComparison.Ordinal)) {
                    throw new AkronReconstructionException(
                        path,
                        "scalar value differs;expected=" + expected.Scalar + ";actual=" + actual);
                }
                return;
            }
            if (expected.Kind == ReferenceValueKind &&
                objects.TryGetValue(expected.NodeId, out object expectedReference)) {
                if (ReferenceEquals(expectedReference, current)) {
                    return;
                }
                if (expectedReference?.GetType().IsValueType == true &&
                    current?.GetType() == expectedReference.GetType() &&
                    nodes.TryGetValue(expected.NodeId, out AkronReconstructionNode valueNode)) {
                    VerifyInlineValueType(valueNode, current, path);
                    return;
                }
            }
            throw new AkronReconstructionException(path, "reference identity differs");
        }

        private void VerifyInlineValueType(AkronReconstructionNode node, object current, string path) {
            foreach (AkronReconstructionField savedField in node.Fields) {
                FieldInfo field = ResolveField(savedField.DeclaringTypeName, savedField.Name, path);
                VerifyValue(savedField.Value, field.GetValue(current), FieldPath(path, savedField.Name));
            }
        }

        private bool IsMasked(string path) {
            foreach (string mask in masks) {
                if (string.Equals(path, mask, StringComparison.Ordinal) ||
                    path.StartsWith(mask + ".", StringComparison.Ordinal) ||
                    path.StartsWith(mask + "[", StringComparison.Ordinal)) {
                    return true;
                }
            }
            return false;
        }
    }

    private static MethodInfo ResolveMethod(AkronReconstructionDelegateCall call, string path) {
        Type declaringType = ResolveType(call.DeclaringTypeName, path);
        Type returnType = ResolveType(call.ReturnTypeName, path);
        Type[] parameterTypes = (call.ParameterTypeNames ?? new List<string>())
            .Select(typeName => ResolveType(typeName, path))
            .ToArray();
        MethodInfo method = declaringType.GetMethod(
            call.MethodName,
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: parameterTypes,
            modifiers: null);
        if (method == null || method.ReturnType != returnType) {
            throw new AkronReconstructionException(path, "delegate method is unavailable");
        }
        return method;
    }

    internal static bool PersistentEventStatesMatch(
        AkronPersistentEventInstanceState expected,
        AkronPersistentEventInstanceState actual
    ) {
        if (expected == null || actual == null ||
            expected.Path != actual.Path ||
            !EventFloatMatches(expected.Volume, actual.Volume) ||
            !EventFloatMatches(expected.Pitch, actual.Pitch) ||
            expected.Has3DAttributes != actual.Has3DAttributes ||
            !EventFloatMatches(expected.PositionX, actual.PositionX) || !EventFloatMatches(expected.PositionY, actual.PositionY) || !EventFloatMatches(expected.PositionZ, actual.PositionZ) ||
            !EventFloatMatches(expected.VelocityX, actual.VelocityX) || !EventFloatMatches(expected.VelocityY, actual.VelocityY) || !EventFloatMatches(expected.VelocityZ, actual.VelocityZ) ||
            !EventFloatMatches(expected.ForwardX, actual.ForwardX) || !EventFloatMatches(expected.ForwardY, actual.ForwardY) || !EventFloatMatches(expected.ForwardZ, actual.ForwardZ) ||
            !EventFloatMatches(expected.UpX, actual.UpX) || !EventFloatMatches(expected.UpY, actual.UpY) || !EventFloatMatches(expected.UpZ, actual.UpZ) ||
            expected.HasListenerMask != actual.HasListenerMask ||
            expected.ListenerMask != actual.ListenerMask ||
            Math.Abs((long) expected.TimelinePosition - actual.TimelinePosition) > 1L ||
            expected.ShouldPlay != actual.ShouldPlay ||
            expected.Paused != actual.Paused ||
            expected.ManualClone != actual.ManualClone) {
            return false;
        }

        Dictionary<string, float> expectedParameters = expected.Parameters ?? new Dictionary<string, float>();
        Dictionary<string, float> actualParameters = actual.Parameters ?? new Dictionary<string, float>();
        return expectedParameters.Count == actualParameters.Count &&
               expectedParameters.All(pair =>
                   actualParameters.TryGetValue(pair.Key, out float value) && EventFloatMatches(value, pair.Value));
    }

    private static bool EventFloatMatches(float left, float right) {
        return left == right || Math.Abs(left - right) <= 0.00001f;
    }

    private static MethodInfo ResolveHookTarget(AkronReconstructionDelegateCall call, string path) {
        return ResolveMethod(new AkronReconstructionDelegateCall {
            DeclaringTypeName = call.HookTargetDeclaringTypeName,
            MethodName = call.HookTargetMethodName,
            ReturnTypeName = call.HookTargetReturnTypeName,
            ParameterTypeNames = call.HookTargetParameterTypeNames ?? new List<string>()
        }, path);
    }

    private static bool CanPackPrimitiveArray(Array array) {
        Type elementType = array?.GetType().GetElementType();
        return CanPackPrimitiveElementType(elementType);
    }

    private static bool CanPackPrimitiveElementType(Type elementType) {
        return elementType?.IsPrimitive == true &&
               elementType != typeof(IntPtr) &&
               elementType != typeof(UIntPtr);
    }

    private static int GetPackedPrimitiveElementSize(Type elementType) {
        if (elementType == typeof(bool) || elementType == typeof(byte) || elementType == typeof(sbyte)) {
            return 1;
        }
        if (elementType == typeof(char) || elementType == typeof(short) || elementType == typeof(ushort)) {
            return 2;
        }
        if (elementType == typeof(int) || elementType == typeof(uint) || elementType == typeof(float)) {
            return 4;
        }
        return 8;
    }

    private static IEnumerable<int[]> EnumerateArrayIndices(Array array) {
        if (array == null || array.Length == 0) {
            yield break;
        }

        int[] indices = new int[array.Rank];
        for (int dimension = 0; dimension < array.Rank; dimension++) {
            indices[dimension] = array.GetLowerBound(dimension);
        }

        while (true) {
            yield return (int[]) indices.Clone();
            int dimension = array.Rank - 1;
            while (dimension >= 0) {
                indices[dimension]++;
                if (indices[dimension] <= array.GetUpperBound(dimension)) {
                    break;
                }
                indices[dimension] = array.GetLowerBound(dimension);
                dimension--;
            }
            if (dimension < 0) {
                yield break;
            }
        }
    }

    private static int[] GetArrayIndicesAtFlatIndex(Array array, int flatIndex) {
        int[] indices = new int[array.Rank];
        for (int dimension = array.Rank - 1; dimension >= 0; dimension--) {
            int length = array.GetLength(dimension);
            indices[dimension] = array.GetLowerBound(dimension) + flatIndex % length;
            flatIndex /= length;
        }
        return indices;
    }

    private static int[] GetInitialArrayIndices(Array array) {
        int[] indices = new int[array.Rank];
        for (int dimension = 0; dimension < array.Rank; dimension++) {
            indices[dimension] = array.GetLowerBound(dimension);
        }
        return indices;
    }

    private static void IncrementArrayIndices(Array array, int[] indices) {
        for (int dimension = array.Rank - 1; dimension >= 0; dimension--) {
            indices[dimension]++;
            if (indices[dimension] <= array.GetUpperBound(dimension)) {
                return;
            }
            indices[dimension] = array.GetLowerBound(dimension);
        }
    }

    private static bool HasArrayIndex(Array array, IReadOnlyList<int> indices) {
        if (array == null || indices == null || indices.Count != array.Rank) {
            return false;
        }
        for (int dimension = 0; dimension < array.Rank; dimension++) {
            if (indices[dimension] < array.GetLowerBound(dimension) || indices[dimension] > array.GetUpperBound(dimension)) {
                return false;
            }
        }
        return true;
    }

    private sealed class AkronReconstructionException : Exception {
        public AkronReconstructionException(string path, string message) : base(message) {
            Path = string.IsNullOrWhiteSpace(path) ? "$" : path;
        }

        public string Path { get; }
    }
}

internal static class AkronStartPosReconstruction {
    // Exact custom-map graphs can legitimately exceed 128 MiB. Keep bounded
    // headroom without allowing 512 MiB of hostile JSON to materialize into
    // several gigabytes of managed objects.
    internal const long MaxDecompressedSnapshotBytes = 192L * 1024L * 1024L;
    private const string SnapshotDirectoryName = "AkronStartPos";
    private static readonly AkronReconstructionGraph Graph = new AkronReconstructionGraph(
        IsLiveResourceType,
        GetLiveResourceKey,
        new AkronVirtualRenderTargetResourceAdapter(),
        ResolveDetachedLiveResource);

    public static AkronReconstructionCapture Capture(
        AkronPersistentRuntimeState savedState,
        AkronPersistentRuntimeState freshState
    ) {
        return Graph.Capture(savedState, freshState);
    }

    public static AkronReconstructionCapture CaptureActionState(
        Dictionary<string, Dictionary<Type, Dictionary<string, object>>> savedState,
        Dictionary<string, Dictionary<Type, Dictionary<string, object>>> freshState
    ) {
        return Graph.Capture(savedState, freshState);
    }

    public static string Serialize(AkronReconstructionDocument document) {
        return Graph.Serialize(document);
    }

    public static AkronReconstructionDocument Deserialize(string json) {
        return Graph.Deserialize(json);
    }

    public static AkronReconstructionRestore Restore(
        AkronReconstructionDocument document,
        AkronPersistentRuntimeState freshState
    ) {
        return Graph.Restore(document, freshState);
    }

    public static AkronReconstructionRestore RestoreActionState(
        AkronReconstructionDocument document,
        Dictionary<string, Dictionary<Type, Dictionary<string, object>>> freshState
    ) {
        return Graph.Restore(document, freshState);
    }

    public static AkronReconstructionVerification Reapply(
        AkronReconstructionDocument document,
        AkronReconstructionRestore restore
    ) {
        return Graph.Reapply(document, restore);
    }

    public static AkronReconstructionVerification Verify(
        AkronReconstructionDocument document,
        AkronReconstructionRestore restore,
        IEnumerable<string> maskedPaths
    ) {
        return Graph.Verify(document, restore, maskedPaths);
    }

    public static void ActivateEventInstances(AkronReconstructionRestore restore) {
        AkronEventInstanceUtils.ActivateDormantEventInstances(
            restore?.Objects?.Values.OfType<EventInstance>());
    }

    public static void ReleaseEventInstances(AkronReconstructionRestore restore) {
        AkronEventInstanceUtils.ReleaseDormantEventInstances(
            restore?.Objects?.Values.OfType<EventInstance>());
    }

    public static void ReleaseOwnedResources() {
        Graph.ReleaseOwnedPersistentResources();
    }

    public static IReadOnlyList<string> GetPostRestoreVerificationMasks(AkronReconstructionDocument document) {
        List<string> masks = new List<string>();
        foreach (AkronReconstructionNode node in document?.Nodes ?? new List<AkronReconstructionNode>()) {
            Type nodeType = Type.GetType(node.TypeName, throwOnError: false);
            if (nodeType != null && typeof(Tracker).IsAssignableFrom(nodeType)) {
                // Tracker is a cache over the restored entity graph. Its saved
                // list order is retained, but a new Everest process can add
                // empty lookup keys for helper types that were registered at
                // a different point during startup.
                masks.Add(node.Path);
            }
            foreach (AkronReconstructionField field in node.Fields ?? new List<AkronReconstructionField>()) {
                Type declaringType = Type.GetType(field.DeclaringTypeName, throwOnError: false);
                if (IsCumulativeStatField(declaringType, field.Name)) {
                    masks.Add(field.Path);
                }
            }
        }
        return masks;
    }

    private static bool IsCumulativeStatField(Type declaringType, string fieldName) {
        if (declaringType == null || string.IsNullOrWhiteSpace(fieldName)) {
            return false;
        }
        if (typeof(Session).IsAssignableFrom(declaringType)) {
            return fieldName == nameof(Session.Time) ||
                   fieldName == nameof(Session.Deaths) ||
                   fieldName == nameof(Session.DeathsInCurrentLevel);
        }
        if (typeof(SaveData).IsAssignableFrom(declaringType)) {
            return fieldName == nameof(SaveData.Time) ||
                   fieldName == nameof(SaveData.TotalDeaths);
        }
        if (typeof(AreaModeStats).IsAssignableFrom(declaringType)) {
            return fieldName == nameof(AreaModeStats.TimePlayed) ||
                   fieldName == nameof(AreaModeStats.Deaths);
        }
        return false;
    }

    public static bool SaveSnapshot(
        string slotName,
        string mapSid,
        string room,
        int fileSlot,
        AkronReconstructionDocument document,
        out string error,
        string directory = null
    ) {
        error = string.Empty;
        string path = GetSnapshotPath(slotName, directory);
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            document.SlotName = slotName ?? string.Empty;
            document.MapSid = mapSid ?? string.Empty;
            document.Room = room ?? string.Empty;
            document.FileSlot = fileSlot;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (FileStream file = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (GZipStream compressed = new GZipStream(file, CompressionLevel.Optimal, leaveOpen: false)) {
                Graph.Serialize(document, compressed);
            }
            File.Move(temporaryPath, path, overwrite: true);
            return true;
        } catch (Exception exception) {
            error = exception.GetType().Name + ": " + exception.Message;
            return false;
        } finally {
            if (File.Exists(temporaryPath)) {
                File.Delete(temporaryPath);
            }
        }
    }

    public static bool TryLoadSnapshot(
        string slotName,
        out AkronReconstructionDocument document,
        out string error,
        string directory = null
    ) {
        document = null;
        error = string.Empty;
        string path = GetSnapshotPath(slotName, directory);
        if (!File.Exists(path)) {
            error = "snapshot file is missing";
            return false;
        }

        try {
            using FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!TryReadSnapshot(file, out document, out error)) {
                return false;
            }
            if (!string.Equals(document.SlotName, slotName, StringComparison.Ordinal)) {
                error = "snapshot slot identity differs";
                document = null;
                return false;
            }
            return true;
        } catch (Exception exception) {
            error = exception.GetType().Name + ": " + exception.Message;
            document = null;
            return false;
        }
    }

    public static bool TryReadSnapshot(
        Stream snapshotStream,
        out AkronReconstructionDocument document,
        out string error,
        long maxDecompressedBytes = MaxDecompressedSnapshotBytes
    ) {
        document = null;
        error = string.Empty;
        if (snapshotStream == null || !snapshotStream.CanRead) {
            error = "snapshot stream is unavailable";
            return false;
        }

        try {
            using GZipStream compressed = new GZipStream(snapshotStream, CompressionMode.Decompress, leaveOpen: true);
            using AkronBoundedReadStream bounded = new AkronBoundedReadStream(compressed, maxDecompressedBytes);
            document = Graph.Deserialize(bounded);
            return true;
        } catch (Exception exception) {
            error = exception.GetType().Name + ": " + exception.Message;
            document = null;
            return false;
        }
    }

    private sealed class AkronBoundedReadStream : Stream {
        private readonly Stream source;
        private readonly long maxBytes;
        private long bytesRead;

        public AkronBoundedReadStream(Stream source, long maxBytes) {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            this.maxBytes = maxBytes >= 0 ? maxBytes : throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position {
            get => bytesRead;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) {
            int read = source.Read(buffer, offset, LimitReadCount(count));
            RecordRead(read);
            return read;
        }

        public override int Read(Span<byte> buffer) {
            int read = source.Read(buffer[..LimitReadCount(buffer.Length)]);
            RecordRead(read);
            return read;
        }

        public override int ReadByte() {
            if (bytesRead >= maxBytes) {
                ThrowIfMoreDataExists();
                return -1;
            }
            int value = source.ReadByte();
            if (value >= 0) {
                bytesRead++;
            }
            return value;
        }

        private int LimitReadCount(int requested) {
            long remaining = maxBytes - bytesRead;
            if (remaining <= 0) {
                ThrowIfMoreDataExists();
                return 0;
            }
            return (int)Math.Min(requested, remaining);
        }

        private void RecordRead(int read) {
            bytesRead += read;
        }

        private void ThrowIfMoreDataExists() {
            if (source.ReadByte() >= 0) {
                throw new InvalidDataException("Snapshot expands beyond its size limit.");
            }
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    public static bool HasSnapshot(string slotName, string directory = null) {
        return File.Exists(GetSnapshotPath(slotName, directory));
    }

    public static void DeleteSnapshot(string slotName, string directory = null) {
        string path = GetSnapshotPath(slotName, directory);
        try {
            if (File.Exists(path)) {
                File.Delete(path);
            }
        } catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) {
            Logger.Log(LogLevel.Warn, nameof(AkronStartPosReconstruction),
                "Could not delete StartPos snapshot " + path + ": " + exception.Message);
        }
    }

    public static bool InstallSnapshot(string slotName, string sourceDirectory, out string error) {
        using PreparedSnapshotInstall install = PrepareSnapshotInstall(slotName, sourceDirectory);
        if (!install.Install(out error)) {
            return false;
        }
        install.Commit();
        return true;
    }

    public static PreparedSnapshotInstall PrepareSnapshotInstall(string slotName, string sourceDirectory) {
        return new PreparedSnapshotInstall(
            GetSnapshotPath(slotName, sourceDirectory),
            GetSnapshotPath(slotName),
            sourceDirectory);
    }

    internal static string GetSnapshotPath(string slotName, string directory = null) {
        string root = string.IsNullOrWhiteSpace(directory)
            ? Path.Combine(AppContext.BaseDirectory, "Saves", SnapshotDirectoryName)
            : directory;
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(slotName ?? string.Empty));
        string key = string.Concat(digest.Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
        return Path.Combine(root, "v6-" + key + ".json.gz");
    }

    internal sealed class PreparedSnapshotInstall : IDisposable {
        private readonly string sourcePath;
        private readonly string destinationPath;
        private readonly string backupPath;
        private bool installed;
        private bool committed;

        public PreparedSnapshotInstall(string sourcePath, string destinationPath, string stagingDirectory) {
            this.sourcePath = sourcePath;
            this.destinationPath = destinationPath;
            backupPath = Path.Combine(stagingDirectory, "replaced-" + Guid.NewGuid().ToString("N"));
        }

        public bool Install(out string error) {
            error = string.Empty;
            if (installed) {
                error = "staged snapshot is already installed";
                return false;
            }
            try {
                if (!File.Exists(sourcePath)) {
                    error = "staged snapshot file is missing";
                    return false;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                if (File.Exists(destinationPath)) {
                    File.Move(destinationPath, backupPath);
                }
                File.Move(sourcePath, destinationPath);
                installed = true;
                return true;
            } catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) {
                error = exception.GetType().Name + ": " + exception.Message;
                RollBack();
                return false;
            }
        }

        public void Commit() {
            if (!installed) {
                throw new InvalidOperationException("Staged snapshot has not been installed.");
            }
            committed = true;
        }

        private void RollBack() {
            if (File.Exists(destinationPath)) {
                File.Delete(destinationPath);
            }
            if (File.Exists(backupPath)) {
                File.Move(backupPath, destinationPath, overwrite: true);
            }
            installed = false;
        }

        public void Dispose() {
            if (installed && !committed) {
                RollBack();
            }
        }
    }

    internal static bool IsLiveResourceType(Type type) {
        if (type == null) {
            return false;
        }

        // These objects own process, native, graphics, audio, reflection, or
        // loader state. Celeste or Everest must create their replacement in the
        // new process. MTexture is intentionally not in this list: its mutable
        // clip and draw state is restored onto a fresh MTexture while its
        // VirtualTexture and ModAsset children remain live anchors.
        // Pathfinder is a room-load cache of static collision tiles. Keeping
        // the fresh room's cache avoids persisting tens of thousands of cells.
        return type == typeof(Pathfinder) ||
               typeof(Type).IsAssignableFrom(type) ||
               typeof(MemberInfo).IsAssignableFrom(type) ||
               typeof(Assembly).IsAssignableFrom(type) ||
               typeof(GraphicsDevice).IsAssignableFrom(type) ||
               typeof(GraphicsDeviceManager).IsAssignableFrom(type) ||
               typeof(GraphicsResource).IsAssignableFrom(type) ||
               typeof(VirtualAsset).IsAssignableFrom(type) ||
               typeof(Atlas).IsAssignableFrom(type) ||
               typeof(ModAsset).IsAssignableFrom(type) ||
               typeof(Stream).IsAssignableFrom(type) ||
               typeof(WaitHandle).IsAssignableFrom(type) ||
               typeof(Thread).IsAssignableFrom(type) ||
               typeof(Task).IsAssignableFrom(type) ||
               typeof(SafeHandle).IsAssignableFrom(type) ||
               typeof(EverestModule).IsAssignableFrom(type) ||
               typeof(EverestModuleSettings).IsAssignableFrom(type) ||
               type == typeof(EventInstance) ||
               string.Equals(type.Namespace, "FMOD", StringComparison.Ordinal) ||
               string.Equals(type.Namespace, "FMOD.Studio", StringComparison.Ordinal) ||
               string.Equals(type.Name, "ILHook", StringComparison.Ordinal) ||
               type.GetInterfaces().Any(candidate =>
                   candidate.FullName?.IndexOf("Detour", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    internal static object ResolveDetachedLiveResource(Type resourceType, string typedResourceKey) {
        if (resourceType == null || string.IsNullOrWhiteSpace(typedResourceKey)) {
            return null;
        }

        int separator = typedResourceKey.IndexOf('|');
        if (separator < 0 || separator == typedResourceKey.Length - 1) {
            return null;
        }

        string assemblyQualifiedName = typedResourceKey.Substring(separator + 1);
        if (typeof(EverestModule).IsAssignableFrom(resourceType)) {
            return Everest.Modules.FirstOrDefault(module => module?.GetType() == resourceType);
        }
        if (typeof(EverestModuleSettings).IsAssignableFrom(resourceType)) {
            return Everest.Modules
                .Select(module => module?._Settings)
                .FirstOrDefault(settings => settings?.GetType() == resourceType);
        }
        if (!typeof(Type).IsAssignableFrom(resourceType)) {
            return null;
        }
        Type resolved = Type.GetType(assemblyQualifiedName, throwOnError: false);
        return resolved != null && resourceType.IsInstanceOfType(resolved)
            ? resolved
            : null;
    }

    internal static string GetLiveResourceKey(object resource) {
        if (resource is Type type) {
            return type.AssemblyQualifiedName ?? type.FullName ?? type.Name;
        }
        if (resource is MemberInfo member) {
            return member.Module.Assembly.FullName + "|" +
                   member.MetadataToken.ToString(CultureInfo.InvariantCulture);
        }
        if (resource is Assembly assembly) {
            return assembly.FullName ?? assembly.GetName().Name;
        }
        if (resource is EverestModule || resource is EverestModuleSettings) {
            Type resourceType = resource.GetType();
            return resourceType.AssemblyQualifiedName ?? resourceType.FullName ?? resourceType.Name;
        }
        if (resource is Atlas atlas && !string.IsNullOrWhiteSpace(atlas.DataPath)) {
            return (atlas.DataMethod ?? string.Empty) + "|" + atlas.DataPath + "|" +
                   (atlas.RelativeDataPath ?? string.Empty) + "|" +
                   (atlas.DataFormat?.ToString() ?? string.Empty);
        }
        if (resource is ModAsset modAsset && !string.IsNullOrWhiteSpace(modAsset.PathVirtual)) {
            return (modAsset.Source?.Name ?? string.Empty) + "|" + modAsset.PathVirtual + "|" +
                   (modAsset.Type?.AssemblyQualifiedName ?? string.Empty) + "|" +
                   (modAsset.Format ?? string.Empty);
        }
        if (resource is VirtualTexture texture && !string.IsNullOrWhiteSpace(texture.Path)) {
            return texture.Path + "|" + texture.Width.ToString(CultureInfo.InvariantCulture) + "x" +
                   texture.Height.ToString(CultureInfo.InvariantCulture);
        }
        if (resource is VirtualAsset asset && !string.IsNullOrWhiteSpace(asset.Name)) {
            return asset.Name + "|" + asset.Width.ToString(CultureInfo.InvariantCulture) + "x" +
                   asset.Height.ToString(CultureInfo.InvariantCulture);
        }
        return string.Empty;
    }
}
