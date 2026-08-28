using System;
using System.Buffers.Binary;
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
using System.Runtime.Loader;
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
    // Bumped whenever the fresh room a document is measured against changes shape.
    //
    // Every node in a document is addressed by a structural path into a clean reload of
    // the same room - "the third entity in the entity list", not "the entity with this
    // id" - so a document is only meaningful against the baseline the build that wrote
    // it produced. Two changes moved that baseline: TryLoadFreshRoom now clears the
    // trails before it unloads the room (AkronSaveLoadService), and PlayerPlayback is
    // now excluded from a capture (AkronSnapshotExclusion). Both remove objects the
    // older baseline contained, so every index after them shifts by one.
    //
    // A shifted index does not always refuse. Two entities of the same type with no
    // distinguishing SourceId pair by occurrence, so the shift can hand one entity's
    // saved state to the other and report success - a silent wrong restore, which is
    // worse than any refusal. That is why this is a version rather than a best effort:
    // v7 and earlier documents are refused outright, never upgraded and never read.
    //
    // v7 -> v8: fresh-room baseline changed (trail parity fix, PlayerPlayback exclusion).
    // v9 -> v10: the JSON shrank to fit real maps under the snapshot size limit.
    //   Measured on real v9 snapshots (Summit, Farewell, 70-95 MB decompressed each),
    //   36-44% of every file was the same few hundred assembly-qualified type names
    //   written hundreds of thousands of times, and another ~21% was property names.
    //   v10 writes each distinct type name once in the document's TypeNames table and
    //   an integer index at every use, shortens the per-record property names, omits
    //   empty lists and default values, and shortens the per-value kind strings. The
    //   in-memory model is unchanged: the string properties are rebuilt from the table
    //   right after a document is read, so capture and restore never see an index.
    //   A v9 document is refused by the header gate like every older version, and the
    //   snapshot file prefix moved with the format so no v10 read can reach one.
    // v8 -> v9: nodes now carry the capture-side identity evidence the restore needs.
    //   A saved live resource records whether its ResourceKey names the resource
    //   (PortableResourceKey) and a saved map entity records whether the map placed
    //   its EntityID (MapPlacedEntity). Without that evidence the restore cannot tell
    //   a key or an id that a second process genuinely cannot produce from one that
    //   merely got a new label, so it falls back to a wildcarded structural path and
    //   can hand the rebuilt room a different object while reporting success. A v8
    //   document carries none of it, and reading one anyway would give two documents
    //   claiming the same format two different guarantees, so v8 is refused outright.
    //
    //   The map half covers the room document only. ActionStateDocument is captured
    //   and restored from Dictionary roots (AkronSaveLoadService.PersistRuntimeStateSnapshot
    //   and RestoreActionState), which hold no Level, so GetMapPlacedEntityIds has no
    //   map to read there and every Entity a registered action's state reaches is
    //   stamped false whatever the map says. That is symmetric - the restore reads the
    //   same false and refuses nothing - so it fails to the behaviour of a v8 document
    //   for those nodes rather than to a wrong restore. The key half has no such
    //   limit: it is read off the saved object and applies to every node in both
    //   documents.
    public const string CurrentFormat = "akron-reconstruction-v10";

    public string Format { get; set; } = CurrentFormat;
    // Every distinct type name in this document, in first-use order, shared by the
    // nested ActionStateDocument. Declared before Nodes so it streams first: the
    // resolver that rebuilds the string properties needs the whole table, and the
    // whole document is materialized before resolution anyway, but keeping the table
    // ahead of its uses also lets a streaming reader bound each name on arrival.
    public List<string> TypeNames { get; set; } = new List<string>();
    public bool ShouldSerializeTypeNames() => TypeNames != null && TypeNames.Count > 0;
    public string SlotName { get; set; } = string.Empty;
    public string MapSid { get; set; } = string.Empty;
    public string Room { get; set; } = string.Empty;
    public int FileSlot { get; set; } = -1;
    public AkronBerryProgressSnapshot BerryProgress { get; set; }
    public int RootNodeId { get; set; }
    public List<AkronReconstructionNode> Nodes { get; set; } = new List<AkronReconstructionNode>();
    public AkronReconstructionDocument ActionStateDocument { get; set; }
    public List<string> RegisteredActionIds { get; set; } = new List<string>();
    public List<AkronGameplayBufferSnapshot> GameplayBuffers { get; set; } = new List<AkronGameplayBufferSnapshot>();
}

// One name per serialized v10 tag that more than one place has to agree on: the
// [JsonProperty] attribute that writes it, the bounded reader's record matchers,
// and the composition analyzer's byte attribution. Referencing the same constant
// everywhere makes a tag rename a compile error at every consumer instead of a
// silently wrong report. Tags read by nothing but their own attribute stay as
// inline literals on the model. Two constants share a value on purpose and must
// not be merged: PathStepFieldName is a scalar on a path step while Fields is an
// array on a node, and NodeId is an integer on a value while FieldName is a
// string on a field - consumers tell them apart by token type and record shape.
internal static class AkronReconstructionTags {
    internal const string Nodes = "Nodes";
    internal const string Fields = "f";
    internal const string DelegateCalls = "dc";
    internal const string FreshPath = "fp";
    internal const string Items = "it";
    internal const string Kind = "k";
    internal const string ParentKind = "pk";
    internal const string ParentFieldName = "pf";
    internal const string FieldName = "n";
    internal const string NodeId = "n";
    internal const string MethodName = "m";
    internal const string HookTargetMethodName = "hm";
    internal const string ResourceKey = "rk";
    internal const string Scalar = "s";
    internal const string PackedPrimitiveArrayBytes = "pb";
    internal const string PathStepFieldName = "f";
    internal const string TypeNameIndex = "t";
    internal const string DeclaringTypeNameIndex = "d";
    internal const string ParentDeclaringTypeNameIndex = "pd";
    internal const string ReturnTypeNameIndex = "r";
    internal const string HookTargetDeclaringTypeNameIndex = "hd";
    internal const string HookTargetReturnTypeNameIndex = "hr";
    internal const string ParameterTypeNameIndexes = "pt";
    internal const string HookTargetParameterTypeNameIndexes = "hpt";
}

// The per-record property names below are one or two characters because they are
// written millions of times per snapshot: measured on real v9 files, the long
// names alone were ~21% of 70-95 MB documents. Type names are indexes into
// AkronReconstructionDocument.TypeNames for the same reason; the string
// properties they shadow are [JsonIgnore], populated by capture and rebuilt by
// AkronReconstructionGraph.ResolveTypeNames right after a read, so everything
// outside the serialization boundary keeps working with plain strings. Empty
// lists and default values are omitted; optional node lists remain null until
// capture or deserialization has an element to store.
internal sealed class AkronReconstructionNode {
    private string diagnosticPath = string.Empty;
    private List<int> parentArrayIndices;
    private List<AkronReconstructionPathStep> freshPath;
    private List<AkronReconstructionField> fields;
    private List<AkronReconstructionValue> items;
    private List<int> arrayLengths;
    private List<int> arrayLowerBounds;
    private List<AkronReconstructionDelegateCall> delegateCalls;

    [JsonProperty("i")]
    public int Id { get; set; }
    [JsonProperty(AkronReconstructionTags.Kind)]
    public string Kind { get; set; } = string.Empty;
    [JsonIgnore]
    public string TypeName { get; set; } = string.Empty;
    [JsonProperty(AkronReconstructionTags.TypeNameIndex, DefaultValueHandling = DefaultValueHandling.Ignore)]
    [System.ComponentModel.DefaultValue(-1)]
    public int TypeNameIndex { get; set; } = -1;
    // Full diagnostic paths grow quadratically with graph depth. Keep the
    // compact first-owner edge on disk and rebuild this text after loading.
    [JsonIgnore]
    public string Path {
        get {
            if (!DiagnosticPathReady || !string.IsNullOrWhiteSpace(diagnosticPath) ||
                DiagnosticPathParent == null) {
                return diagnosticPath;
            }
            diagnosticPath = AkronReconstructionGraph.MaterializeDiagnosticPath(this);
            return diagnosticPath;
        }
        set {
            diagnosticPath = value;
            DiagnosticPathParent = null;
            DiagnosticPathLength = string.IsNullOrWhiteSpace(value) ? -1 : value.Length;
            DiagnosticPathReady = !string.IsNullOrWhiteSpace(value);
        }
    }
    internal AkronReconstructionNode DiagnosticPathParent { get; private set; }
    internal int DiagnosticPathLength { get; private set; } = -1;
    internal bool DiagnosticPathReady { get; private set; }

    internal void SetLazyDiagnosticPath(AkronReconstructionNode parent, int length) {
        diagnosticPath = string.Empty;
        DiagnosticPathParent = parent;
        DiagnosticPathLength = length;
        DiagnosticPathReady = true;
    }
    [JsonProperty("p", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public int ParentNodeId { get; set; }
    [JsonProperty(AkronReconstructionTags.ParentKind, DefaultValueHandling = DefaultValueHandling.Ignore)]
    [System.ComponentModel.DefaultValue("")]
    public string ParentKind { get; set; } = string.Empty;
    [JsonIgnore]
    public string ParentDeclaringTypeName { get; set; } = string.Empty;
    [JsonProperty(AkronReconstructionTags.ParentDeclaringTypeNameIndex, DefaultValueHandling = DefaultValueHandling.Ignore)]
    [System.ComponentModel.DefaultValue(-1)]
    public int ParentDeclaringTypeNameIndex { get; set; } = -1;
    [JsonProperty(AkronReconstructionTags.ParentFieldName, DefaultValueHandling = DefaultValueHandling.Ignore)]
    [System.ComponentModel.DefaultValue("")]
    public string ParentFieldName { get; set; } = string.Empty;
    [JsonProperty("pa")]
    public List<int> ParentArrayIndices {
        get => parentArrayIndices ??= new List<int>();
        set => parentArrayIndices = value;
    }
    internal List<int> ParentArrayIndicesOrNull {
        get => parentArrayIndices;
        set => parentArrayIndices = value;
    }
    public bool ShouldSerializeParentArrayIndices() => parentArrayIndices != null && parentArrayIndices.Count > 0;
    [JsonProperty("pi", DefaultValueHandling = DefaultValueHandling.Ignore)]
    [System.ComponentModel.DefaultValue(-1)]
    public int ParentDelegateIndex { get; set; } = -1;
    [JsonProperty("uf", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public bool UseFreshObject { get; set; }
    [JsonProperty(AkronReconstructionTags.ResourceKey, DefaultValueHandling = DefaultValueHandling.Ignore)]
    [System.ComponentModel.DefaultValue("")]
    public string ResourceKey { get; set; } = string.Empty;
    // The two identity facts capture knows and restore cannot work out for itself,
    // because working them out needs the saved object and the saved map, and a
    // restore has neither. Both are written on the saved side and read on the fresh
    // side, and both are absent from the JSON when false, which is every node that
    // is not a keyed live resource or a map entity.
    //
    // PortableResourceKey: the ResourceKey above names this resource rather than
    // labelling this instance, so a process that cannot find that key does not have
    // the resource. Set for a content-addressed or registry-addressed key - a
    // culture sort name, a file-backed texture path, a reflection key from an
    // assembly loaded off disk. Not set for a key built from a name the running
    // process made up, which a second process renames for the same resource.
    [JsonProperty("pr", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public bool PortableResourceKey { get; set; }
    // MapPlacedEntity: the map laid this entity's EntityID out in its room when the
    // slot was set. An id the map owns going missing means the map changed; an id
    // the map never owned going missing means nothing, because a mod made it up.
    [JsonProperty("mp", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public bool MapPlacedEntity { get; set; }
    [JsonProperty(AkronReconstructionTags.FreshPath)]
    public List<AkronReconstructionPathStep> FreshPath {
        get => freshPath ??= new List<AkronReconstructionPathStep>();
        set => freshPath = value;
    }
    internal List<AkronReconstructionPathStep> FreshPathOrNull {
        get => freshPath;
        set => freshPath = value;
    }
    public bool ShouldSerializeFreshPath() => freshPath != null && freshPath.Count > 0;
    [JsonProperty(AkronReconstructionTags.Fields)]
    public List<AkronReconstructionField> Fields {
        get => fields ??= new List<AkronReconstructionField>();
        set => fields = value;
    }
    internal List<AkronReconstructionField> FieldsOrNull {
        get => fields;
        set => fields = value;
    }
    public bool ShouldSerializeFields() => fields != null && fields.Count > 0;
    [JsonProperty(AkronReconstructionTags.Items)]
    public List<AkronReconstructionValue> Items {
        get => items ??= new List<AkronReconstructionValue>();
        set => items = value;
    }
    internal List<AkronReconstructionValue> ItemsOrNull {
        get => items;
        set => items = value;
    }
    public bool ShouldSerializeItems() => items != null && items.Count > 0;
    [JsonProperty("al")]
    public List<int> ArrayLengths {
        get => arrayLengths ??= new List<int>();
        set => arrayLengths = value;
    }
    internal List<int> ArrayLengthsOrNull {
        get => arrayLengths;
        set => arrayLengths = value;
    }
    public bool ShouldSerializeArrayLengths() => arrayLengths != null && arrayLengths.Count > 0;
    [JsonProperty("ab")]
    public List<int> ArrayLowerBounds {
        get => arrayLowerBounds ??= new List<int>();
        set => arrayLowerBounds = value;
    }
    internal List<int> ArrayLowerBoundsOrNull {
        get => arrayLowerBounds;
        set => arrayLowerBounds = value;
    }
    public bool ShouldSerializeArrayLowerBounds() => arrayLowerBounds != null && arrayLowerBounds.Count > 0;
    [JsonProperty(AkronReconstructionTags.PackedPrimitiveArrayBytes, NullValueHandling = NullValueHandling.Ignore)]
    public byte[] PackedPrimitiveArrayBytes { get; set; }
    [JsonProperty(AkronReconstructionTags.DelegateCalls)]
    public List<AkronReconstructionDelegateCall> DelegateCalls {
        get => delegateCalls ??= new List<AkronReconstructionDelegateCall>();
        set => delegateCalls = value;
    }
    internal List<AkronReconstructionDelegateCall> DelegateCallsOrNull {
        get => delegateCalls;
        set => delegateCalls = value;
    }
    public bool ShouldSerializeDelegateCalls() => delegateCalls != null && delegateCalls.Count > 0;
    [JsonProperty("ev", NullValueHandling = NullValueHandling.Ignore)]
    public AkronPersistentEventInstanceState EventInstance { get; set; }
    [JsonProperty("rp", NullValueHandling = NullValueHandling.Ignore)]
    public AkronReconstructionResourcePayload ResourcePayload { get; set; }
}

internal sealed class AkronReconstructionField {
    private string diagnosticPath = string.Empty;
    private AkronReconstructionNode diagnosticPathParent;
    private AkronReconstructionNode diagnosticPathChild;
    private bool diagnosticPathReady;

    [JsonIgnore]
    public string DeclaringTypeName { get; set; } = string.Empty;
    [JsonProperty(AkronReconstructionTags.DeclaringTypeNameIndex, DefaultValueHandling = DefaultValueHandling.Ignore)]
    [System.ComponentModel.DefaultValue(-1)]
    public int DeclaringTypeNameIndex { get; set; } = -1;
    [JsonProperty(AkronReconstructionTags.FieldName)]
    public string Name { get; set; } = string.Empty;
    [JsonIgnore]
    public string Path {
        get {
            if (!diagnosticPathReady || !string.IsNullOrWhiteSpace(diagnosticPath)) {
                return diagnosticPath;
            }
            diagnosticPath = diagnosticPathChild != null
                ? diagnosticPathChild.Path
                : AkronReconstructionGraph.BuildFieldDiagnosticPath(diagnosticPathParent?.Path, Name);
            return diagnosticPath;
        }
        set {
            diagnosticPath = value;
            diagnosticPathParent = null;
            diagnosticPathChild = null;
            diagnosticPathReady = !string.IsNullOrWhiteSpace(value);
        }
    }

    internal void SetLazyParentDiagnosticPath(AkronReconstructionNode parent) {
        diagnosticPath = string.Empty;
        diagnosticPathParent = parent;
        diagnosticPathChild = null;
        diagnosticPathReady = true;
    }

    internal void SetLazyChildDiagnosticPath(AkronReconstructionNode child) {
        diagnosticPath = string.Empty;
        diagnosticPathParent = null;
        diagnosticPathChild = child;
        diagnosticPathReady = true;
    }
    [JsonProperty("v")]
    public AkronReconstructionValue Value { get; set; }
}

internal sealed class AkronReconstructionValue {
    [System.ComponentModel.DefaultValue(AkronReconstructionGraph.NullValueKind)]
    [JsonProperty(AkronReconstructionTags.Kind, DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string Kind { get; set; } = AkronReconstructionGraph.NullValueKind;
    [JsonIgnore]
    public string TypeName { get; set; } = string.Empty;
    [JsonProperty(AkronReconstructionTags.TypeNameIndex, DefaultValueHandling = DefaultValueHandling.Ignore)]
    [System.ComponentModel.DefaultValue(-1)]
    public int TypeNameIndex { get; set; } = -1;
    [System.ComponentModel.DefaultValue("")]
    [JsonProperty(AkronReconstructionTags.Scalar, DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string Scalar { get; set; } = string.Empty;
    [JsonProperty(AkronReconstructionTags.NodeId, DefaultValueHandling = DefaultValueHandling.Ignore)]
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
    public AkronReconstructionResourcePayload Payload { get; set; }
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
    private static readonly AsyncLocal<IReadOnlyDictionary<object, AkronReconstructionResourcePayload>> CapturedPayloads =
        new AsyncLocal<IReadOnlyDictionary<object, AkronReconstructionResourcePayload>>();

    public bool CanPersist(Type type) {
        return type == typeof(VirtualRenderTarget);
    }

    public AkronReconstructionResourcePayload Capture(object resource) {
        VirtualRenderTarget renderTarget = (VirtualRenderTarget) resource;
        IReadOnlyDictionary<object, AkronReconstructionResourcePayload> captured = CapturedPayloads.Value;
        if (captured != null) {
            if (!captured.TryGetValue(renderTarget, out AkronReconstructionResourcePayload payload)) {
                throw new InvalidOperationException(
                    "Set-frame pixels are missing for VirtualRenderTarget " + (renderTarget.Name ?? "unnamed") + ".");
            }
            return ClonePayload(payload);
        }
        return CaptureOnGameThread(renderTarget);
    }

    internal static IReadOnlyDictionary<object, AkronReconstructionResourcePayload> CaptureSetFramePayloads(
        IEnumerable<VirtualRenderTarget> renderTargets
    ) {
        Dictionary<object, AkronReconstructionResourcePayload> payloads =
            new Dictionary<object, AkronReconstructionResourcePayload>(ReferenceEqualityComparer.Instance);
        foreach (VirtualRenderTarget renderTarget in renderTargets ?? Enumerable.Empty<VirtualRenderTarget>()) {
            if (renderTarget != null && !renderTarget.IsDisposed && renderTarget.Target != null) {
                payloads[renderTarget] = CaptureOnGameThread(renderTarget);
            }
        }
        return payloads;
    }

    internal static IDisposable UseCapturedPayloads(
        IReadOnlyDictionary<object, AkronReconstructionResourcePayload> payloads
    ) {
        IReadOnlyDictionary<object, AkronReconstructionResourcePayload> previous = CapturedPayloads.Value;
        CapturedPayloads.Value = payloads ?? new Dictionary<object, AkronReconstructionResourcePayload>();
        return new AkronCapturedResourceScope(previous);
    }

    private static AkronReconstructionResourcePayload CaptureOnGameThread(VirtualRenderTarget renderTarget) {
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

    private static AkronReconstructionResourcePayload ClonePayload(AkronReconstructionResourcePayload payload) {
        return new AkronReconstructionResourcePayload {
            Kind = payload.Kind,
            Name = payload.Name,
            Width = payload.Width,
            Height = payload.Height,
            MultiSampleCount = payload.MultiSampleCount,
            Depth = payload.Depth,
            Preserve = payload.Preserve,
            Bytes = payload.Bytes?.ToArray() ?? Array.Empty<byte>()
        };
    }

    private sealed class AkronCapturedResourceScope : IDisposable {
        private readonly IReadOnlyDictionary<object, AkronReconstructionResourcePayload> previous;

        public AkronCapturedResourceScope(IReadOnlyDictionary<object, AkronReconstructionResourcePayload> previous) {
            this.previous = previous;
        }

        public void Dispose() {
            CapturedPayloads.Value = previous;
        }
    }

    public object Restore(AkronReconstructionResourcePayload payload, object freshResource) {
        ValidatePayload(payload);
        VirtualRenderTarget renderTarget = freshResource as VirtualRenderTarget;
        bool created = false;
        if (!PixelLayoutMatches(payload, renderTarget)) {
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

    public bool RestoreExisting(AkronReconstructionResourcePayload payload, VirtualRenderTarget renderTarget) {
        ValidatePayload(payload);
        if (!PixelLayoutMatches(payload, renderTarget)) {
            return false;
        }
        renderTarget.Target.SetData(payload.Bytes);
        return true;
    }

    public bool Verify(AkronReconstructionResourcePayload payload, object resource) {
        try {
            ValidatePayload(payload);
            if (resource is not VirtualRenderTarget renderTarget || !PixelLayoutMatches(payload, renderTarget)) {
                return false;
            }
            byte[] pixels = new byte[payload.Bytes.Length];
            renderTarget.Target.GetData(pixels);
            return pixels.SequenceEqual(payload.Bytes);
        } catch {
            return false;
        }
    }

    private static bool PixelLayoutMatches(
        AkronReconstructionResourcePayload payload,
        VirtualRenderTarget renderTarget
    ) {
        // Name is a debug label and does not affect the XNA resource layout.
        return renderTarget != null && !renderTarget.IsDisposed &&
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

    public static void RestoreBestEffort(IReadOnlyList<AkronGameplayBufferSnapshot> snapshots) {
        Dictionary<string, AkronGameplayBufferSnapshot> savedByName = new Dictionary<string, AkronGameplayBufferSnapshot>(
            StringComparer.Ordinal);
        foreach (AkronGameplayBufferSnapshot snapshot in snapshots ?? Array.Empty<AkronGameplayBufferSnapshot>()) {
            if (snapshot == null || string.IsNullOrEmpty(snapshot.FieldName) || !savedByName.TryAdd(snapshot.FieldName, snapshot)) {
                LogSkippedBuffer(snapshot?.FieldName, "snapshot entry is invalid or duplicated");
            }
        }

        foreach (FieldInfo field in GetBufferFields()) {
            if (!savedByName.TryGetValue(field.Name, out AkronGameplayBufferSnapshot snapshot)) {
                LogSkippedBuffer(field.Name, "snapshot is missing");
                continue;
            }
            if (field.GetValue(null) is not VirtualRenderTarget renderTarget) {
                LogSkippedBuffer(field.Name, "current render target is unavailable");
                continue;
            }

            try {
                // Gameplay buffers are derived presentation state. A camera or graphics
                // mod can resize them after Set, so an incompatible buffer must not turn
                // an otherwise valid StartPos into a half-applied failed restore.
                if (!Adapter.RestoreExisting(snapshot.Payload, renderTarget)) {
                    LogSkippedBuffer(field.Name, "current render target dimensions differ");
                    continue;
                }
                if (!Adapter.Verify(snapshot.Payload, renderTarget)) {
                    LogSkippedBuffer(field.Name, "restored pixels differ");
                }
            } catch (Exception exception) {
                LogSkippedBuffer(field.Name, exception.GetType().Name + ": " + exception.Message);
            }
        }
    }

    private static void LogSkippedBuffer(string fieldName, string reason) {
        Logger.Log(LogLevel.Warn, nameof(AkronGameplayBufferState),
            "Skipped StartPos gameplay buffer " +
            (string.IsNullOrEmpty(fieldName) ? "<unknown>" : fieldName) + ": " + reason);
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
    private List<int> arrayIndices;

    [JsonProperty(AkronReconstructionTags.Kind)]
    public string Kind { get; set; } = string.Empty;
    [JsonIgnore]
    public string DeclaringTypeName { get; set; } = string.Empty;
    [JsonProperty(AkronReconstructionTags.DeclaringTypeNameIndex, DefaultValueHandling = DefaultValueHandling.Ignore)]
    [System.ComponentModel.DefaultValue(-1)]
    public int DeclaringTypeNameIndex { get; set; } = -1;
    [JsonProperty(AkronReconstructionTags.PathStepFieldName, DefaultValueHandling = DefaultValueHandling.Ignore)]
    [System.ComponentModel.DefaultValue("")]
    public string FieldName { get; set; } = string.Empty;
    [JsonProperty("a")]
    public List<int> ArrayIndices {
        get => arrayIndices ??= new List<int>();
        set => arrayIndices = value;
    }
    internal List<int> ArrayIndicesOrNull {
        get => arrayIndices;
        set => arrayIndices = value;
    }
    public bool ShouldSerializeArrayIndices() => arrayIndices != null && arrayIndices.Count > 0;
}

internal sealed class AkronReconstructionDelegateCall {
    [JsonProperty(AkronReconstructionTags.Kind)]
    public string Kind { get; set; } = "method";
    [JsonProperty("tg")]
    public AkronReconstructionValue Target { get; set; }
    [JsonIgnore]
    public string DeclaringTypeName { get; set; } = string.Empty;
    [JsonProperty(AkronReconstructionTags.DeclaringTypeNameIndex, DefaultValueHandling = DefaultValueHandling.Ignore)]
    [System.ComponentModel.DefaultValue(-1)]
    public int DeclaringTypeNameIndex { get; set; } = -1;
    [JsonProperty(AkronReconstructionTags.MethodName, DefaultValueHandling = DefaultValueHandling.Ignore)]
    [System.ComponentModel.DefaultValue("")]
    public string MethodName { get; set; } = string.Empty;
    [JsonIgnore]
    public string ReturnTypeName { get; set; } = string.Empty;
    [JsonProperty(AkronReconstructionTags.ReturnTypeNameIndex, DefaultValueHandling = DefaultValueHandling.Ignore)]
    [System.ComponentModel.DefaultValue(-1)]
    public int ReturnTypeNameIndex { get; set; } = -1;
    [JsonIgnore]
    public List<string> ParameterTypeNames { get; set; }
    [JsonProperty(AkronReconstructionTags.ParameterTypeNameIndexes)]
    public List<int> ParameterTypeNameIndexes { get; set; }
    public bool ShouldSerializeParameterTypeNameIndexes() =>
        ParameterTypeNameIndexes != null && ParameterTypeNameIndexes.Count > 0;
    [JsonIgnore]
    public string HookTargetDeclaringTypeName { get; set; } = string.Empty;
    [JsonProperty(AkronReconstructionTags.HookTargetDeclaringTypeNameIndex, DefaultValueHandling = DefaultValueHandling.Ignore)]
    [System.ComponentModel.DefaultValue(-1)]
    public int HookTargetDeclaringTypeNameIndex { get; set; } = -1;
    [JsonProperty(AkronReconstructionTags.HookTargetMethodName, DefaultValueHandling = DefaultValueHandling.Ignore)]
    [System.ComponentModel.DefaultValue("")]
    public string HookTargetMethodName { get; set; } = string.Empty;
    [JsonIgnore]
    public string HookTargetReturnTypeName { get; set; } = string.Empty;
    [JsonProperty(AkronReconstructionTags.HookTargetReturnTypeNameIndex, DefaultValueHandling = DefaultValueHandling.Ignore)]
    [System.ComponentModel.DefaultValue(-1)]
    public int HookTargetReturnTypeNameIndex { get; set; } = -1;
    [JsonIgnore]
    public List<string> HookTargetParameterTypeNames { get; set; }
    [JsonProperty(AkronReconstructionTags.HookTargetParameterTypeNameIndexes)]
    public List<int> HookTargetParameterTypeNameIndexes { get; set; }
    public bool ShouldSerializeHookTargetParameterTypeNameIndexes() =>
        HookTargetParameterTypeNameIndexes != null && HookTargetParameterTypeNameIndexes.Count > 0;
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
    private AkronReconstructionRestore(
        bool success,
        string errorPath,
        string error,
        string refusedTypeName,
        AkronReconstructionRefusalKind refusedKind,
        Dictionary<int, object> objects
    ) {
        Success = success;
        ErrorPath = errorPath ?? string.Empty;
        Error = error ?? string.Empty;
        RefusedTypeName = refusedTypeName ?? string.Empty;
        RefusedKind = refusedKind;
        Objects = objects;
    }

    public bool Success { get; }
    public string ErrorPath { get; }
    public string Error { get; }
    // The assembly-qualified name of the saved object the refusal is about, empty when
    // the refusal names no object. Error carries the same name inside its flag text for
    // the log; this is the copy the player-facing message is built from.
    public string RefusedTypeName { get; }
    // What the refusal is about, which decides which sentence the name above goes into.
    public AkronReconstructionRefusalKind RefusedKind { get; }
    internal Dictionary<int, object> Objects { get; }

    public static AkronReconstructionRestore Succeeded(Dictionary<int, object> objects) {
        return new AkronReconstructionRestore(
            true,
            string.Empty,
            string.Empty,
            string.Empty,
            AkronReconstructionRefusalKind.SavedObject,
            objects);
    }

    public static AkronReconstructionRestore Failed(
        string path,
        string error,
        string refusedTypeName = "",
        AkronReconstructionRefusalKind refusedKind = AkronReconstructionRefusalKind.SavedObject
    ) {
        string normalizedPath = string.IsNullOrWhiteSpace(path) ? "$" : path;
        return new AkronReconstructionRestore(
            false,
            normalizedPath,
            normalizedPath + ": " + error,
            refusedTypeName,
            refusedKind,
            null);
    }
}

internal sealed class AkronReconstructionVerification {
    private AkronReconstructionVerification(
        bool success,
        string errorPath,
        string error,
        string refusedTypeName,
        AkronReconstructionRefusalKind refusedKind
    ) {
        Success = success;
        ErrorPath = errorPath ?? string.Empty;
        Error = error ?? string.Empty;
        RefusedTypeName = refusedTypeName ?? string.Empty;
        RefusedKind = refusedKind;
    }

    public bool Success { get; }
    public string ErrorPath { get; }
    public string Error { get; }
    public string RefusedTypeName { get; }
    public AkronReconstructionRefusalKind RefusedKind { get; }

    public static AkronReconstructionVerification Succeeded() {
        return new AkronReconstructionVerification(
            true,
            string.Empty,
            string.Empty,
            string.Empty,
            AkronReconstructionRefusalKind.SavedObject);
    }

    public static AkronReconstructionVerification Failed(
        string path,
        string error,
        string refusedTypeName = "",
        AkronReconstructionRefusalKind refusedKind = AkronReconstructionRefusalKind.SavedObject
    ) {
        string normalizedPath = string.IsNullOrWhiteSpace(path) ? "$" : path;
        return new AkronReconstructionVerification(
            false,
            normalizedPath,
            normalizedPath + ": " + error,
            refusedTypeName,
            refusedKind);
    }
}

// Json.NET builds lists and objects before the document-level checks can run.
// Count the stream as Json.NET reads it so hostile pack data cannot create an
// unbounded object graph or scalar before those checks get control.
internal sealed class AkronBoundedJsonTextReader : JsonTextReader {
    private enum RecordArrayKind : byte {
        None,
        Nodes,
        Expensive
    }

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
    private RecordArrayKind[] recordArrayKindsByDepth = new RecordArrayKind[8];
    private string pendingPropertyName;

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
        string valuePropertyName = pendingPropertyName;
        RecordArrayKind recordArrayKind = TrackRecordArrayKind();
        tokenCount++;
        if (tokenCount > maxTokenCount) {
            throw new InvalidOperationException(
                $"Reconstruction JSON token count exceeds the supported limit of {maxTokenCount:N0}.");
        }
        if (TokenType is JsonToken.StartObject or JsonToken.StartArray or JsonToken.StartConstructor) {
            containerCount++;
            if (containerCount > maxContainerCount) {
                throw new InvalidOperationException(
                    $"Reconstruction JSON container count exceeds the supported limit of {maxContainerCount:N0}.");
            }
        }
        if (TokenType == JsonToken.StartObject) {
            recordCount++;
            if (recordCount > maxRecordCount) {
                throw new InvalidOperationException(
                    $"Reconstruction JSON record count exceeds the supported limit of {maxRecordCount:N0}.");
            }
            if (recordArrayKind == RecordArrayKind.Expensive) {
                expensiveRecordCount++;
                if (expensiveRecordCount > maxExpensiveRecordCount) {
                    throw new InvalidOperationException(
                        $"Reconstruction JSON complex record count exceeds the supported limit of {maxExpensiveRecordCount:N0}.");
                }
            }
            if (recordArrayKind == RecordArrayKind.Nodes) {
                nodeCount++;
                if (nodeCount > maxNodeCount) {
                    throw new InvalidOperationException(
                        $"Reconstruction JSON node count exceeds the supported limit of {maxNodeCount:N0}.");
                }
            }
        }
        bool streamedBinary = TokenType == JsonToken.String &&
                              IsBinaryProperty(valuePropertyName) &&
                              Value is string;
        if (streamedBinary) {
            RecordBase64Bytes((string) Value);
        } else if (Value is string text && text.Length > maxStringChars) {
            throw new InvalidOperationException(
                $"Reconstruction JSON string length exceeds the supported limit of {maxStringChars:N0} characters.");
        }
        if (Value is byte[] bytes) {
            RecordBinaryBytes(bytes.LongLength);
        }
    }

    private static bool IsBinaryProperty(string propertyName) {
        return propertyName == AkronReconstructionTags.PackedPrimitiveArrayBytes ||
               propertyName == nameof(AkronReconstructionResourcePayload.Bytes);
    }

    private void RecordBase64Bytes(string encoded) {
        if ((encoded.Length & 3) != 0) {
            throw new InvalidOperationException("Reconstruction JSON binary data is invalid.");
        }
        int padding = encoded.Length > 0 && encoded[encoded.Length - 1] == '=' ? 1 : 0;
        if (encoded.Length > 1 && encoded[encoded.Length - 2] == '=') {
            padding++;
        }
        RecordBinaryBytes(checked((long) (encoded.Length / 4) * 3L - padding));
    }

    private void RecordBinaryBytes(long count) {
        binaryBytes = checked(binaryBytes + count);
        if (binaryBytes > maxBinaryBytes) {
            throw new InvalidOperationException(
                $"Reconstruction JSON binary data exceeds the supported limit of {maxBinaryBytes:N0} bytes.");
        }
    }

    private RecordArrayKind TrackRecordArrayKind() {
        if (TokenType == JsonToken.PropertyName) {
            pendingPropertyName = Value as string;
            return RecordArrayKind.None;
        }

        RecordArrayKind kind = RecordArrayKind.None;
        if (TokenType == JsonToken.StartArray) {
            int depth = Depth;
            if (depth >= recordArrayKindsByDepth.Length) {
                int newLength = Math.Max(checked(depth + 1), checked(recordArrayKindsByDepth.Length * 2));
                Array.Resize(ref recordArrayKindsByDepth, newLength);
            }
            recordArrayKindsByDepth[depth] = pendingPropertyName switch {
                AkronReconstructionTags.Nodes => RecordArrayKind.Nodes,
                AkronReconstructionTags.Fields or
                    AkronReconstructionTags.DelegateCalls or
                    AkronReconstructionTags.FreshPath => RecordArrayKind.Expensive,
                _ => RecordArrayKind.None
            };
        } else if (TokenType == JsonToken.EndArray) {
            if (Depth < recordArrayKindsByDepth.Length) {
                recordArrayKindsByDepth[Depth] = RecordArrayKind.None;
            }
        } else if (TokenType == JsonToken.StartObject && Depth > 0 &&
                   Depth - 1 < recordArrayKindsByDepth.Length) {
            kind = recordArrayKindsByDepth[Depth - 1];
        }

        if (TokenType != JsonToken.Comment) {
            pendingPropertyName = null;
        }
        return kind;
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
    // Structural caps on one snapshot document, each derived from
    // MaxDecompressedSnapshotBytes so the byte cap is the only limit a document
    // Akron wrote itself can reach. They used to be fixed counts calibrated one
    // real map at a time - 500,000 complex records was the Heart of the Storm
    // bump - and the 12,000,000-token cap sat inside the real-data band: a
    // vanilla 7-Summit session wrote slots past it that every later cold load
    // then refused, the same released-build failure the byte cap's own comment
    // at MaxDecompressedSnapshotBytes describes. A limit under what real maps
    // produce is not a limit on hostile input, it is a slot that cannot be
    // loaded.
    //
    // Each divisor sits under the sustained bytes-per-element of anything
    // Serialize emits at scale, so a document inside the byte cap cannot trip
    // these and a document that does is denser than this writer's output -
    // crafted, not saved. Measured on real v10 snapshots (Summit and Farewell,
    // 32-36 MB decompressed): ~5 bytes per token against the 4-byte divisor,
    // ~18.5 bytes per record against 16, and ~1.8 KB per node against 512, so
    // the byte cap still binds first at every element the writer produces. The
    // v9 format ran 15-25 bytes per token; v10's type-name table and short
    // property names are what moved real output toward the floors. What
    // actually bounds a hostile document's memory is the record cap: at ~64
    // bytes per materialized value object its ceiling is about 1.5 GiB, the
    // same order as the 1.4 GiB of RSS the byte cap already budgets for one
    // cold load.
    internal const long DefaultMaxJsonTokenCount =
        AkronStartPosReconstruction.MaxDecompressedSnapshotBytes / 4;
    internal const long DefaultMaxJsonContainerCount = DefaultMaxJsonTokenCount / 2;
    internal const long DefaultMaxJsonNodeCount =
        AkronStartPosReconstruction.MaxDecompressedSnapshotBytes / 512;
    internal const long DefaultMaxJsonRecordCount =
        AkronStartPosReconstruction.MaxDecompressedSnapshotBytes / 16;
    internal const long DefaultMaxJsonExpensiveRecordCount =
        AkronStartPosReconstruction.MaxDecompressedSnapshotBytes / 64;
    private const int DefaultMaxJsonStringChars = 16 * 1024 * 1024;
    private const long DefaultMaxJsonBinaryBytes = 192L * 1024L * 1024L;
    private const string ObjectKind = "object";
    private const string ArrayKind = "array";
    private const string AnchorKind = "anchor";
    private const string DelegateKind = "delegate";
    private const string EventInstanceKind = "event-instance";
    private const string PersistentResourceKind = "persistent-resource";
    // A WeakReference owns one process GC handle and nothing else, so walking its
    // fields reaches an IntPtr and refuses the slot - which is what a Spring
    // Collab 2020 backdrop holding a weak reference to itself did to every
    // Heart of the Storm capture. This kind stores what the handle means
    // instead: Items[0] is the target and Items[1] is the non-generic type's
    // resurrection flag, and the restore builds a new WeakReference around the
    // restored target - the same shape AkronDeepClone gives a warm copy.
    private const string WeakReferenceKind = "weak-reference";
    // The value kinds are one character because a real snapshot writes them
    // hundreds of thousands of times ("reference" alone was 325,000 uses and
    // 3.4 MB on a measured Farewell document). Node kinds above appear once per
    // node - tens of thousands - and stay readable words. Internal because the
    // value model's [DefaultValue] attribute names the null kind.
    internal const string NullValueKind = "n";
    internal const string ScalarValueKind = "s";
    internal const string ReferenceValueKind = "r";
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
    // Monocle.StateMachine addresses its four callback arrays by state id and
    // keeps a parallel names array saying which state each id is. Everest's
    // AddState writes a name; the base SetCallbacks leaves the slot unnamed.
    private static readonly string StateMachineTypeName = TypeName(typeof(StateMachine));
    private static readonly string[] StateMachineCallbackFieldNames = { "begins", "updates", "ends", "coroutines" };
    private static readonly FieldInfo[] StateMachineCallbackFields = StateMachineCallbackFieldNames
        .Select(name => typeof(StateMachine).GetField(name, RuntimeInstanceFields))
        .ToArray();
    // The two of those four that say what a state IS rather than what happens at
    // its edges: StateMachine.Update calls updates[state] and starts
    // coroutines[state], so between them they are the code a state runs. begins
    // and ends are deliberately left out - a mod is free to rewire the edges of a
    // state it already owns while the room is played, and that is a move
    // ValidateStateSlotAssignment must not refuse. The two sides of the
    // comparison read this same list in this same order.
    private static readonly string[] StateMachineDriverFieldNames = { "updates", "coroutines" };
    private static readonly FieldInfo[] StateMachineDriverFields = StateMachineDriverFieldNames
        .Select(name => typeof(StateMachine).GetField(name, RuntimeInstanceFields))
        .ToArray();
    private static readonly FieldInfo StateMachineNamesField =
        typeof(StateMachine).GetField("names", RuntimeInstanceFields);

    private readonly Func<Type, bool> isLiveResource;
    private readonly Func<object, bool> isAdditionalLiveResource;
    private readonly Func<Type, string, bool> hasDeferredDetachedLiveResourceKey;
    private readonly Func<object, string> getLiveResourceKey;
    private readonly IAkronReconstructionResourceAdapter resourceAdapter;
    private readonly Func<Type, string, object> resolveDetachedLiveResource;
    // Restore's last resort for a labelled live resource that resolved nowhere.
    // Asked only after the fresh key index, the detached registry, and the
    // structural owner path all came up empty, and never for a portable key: a
    // name that resolves nowhere is a resource this install does not have,
    // which stays a refusal. Capture never calls this - capture records what
    // the saved frame holds and must not create process state to do it, and it
    // runs on the persistence worker where a graphics resource must not be
    // created anyway.
    private readonly Func<Type, string, object> recreateDetachedLiveResource;
    private readonly Func<Type, bool> areEquivalentLiveResources;
    // Asked of the saved object at capture, never of a fresh candidate. The question
    // is whether the saved key names the resource, and only the saved object can
    // answer it: a fresh candidate is a different object whose own key may be
    // classified the other way, which would waive exactly the keys this exists to
    // hold. Both callbacks below are optional, and a graph without them writes no
    // evidence and reads none, which is what every graph that has no live-resource
    // policy and no map wants.
    private readonly Func<object, bool> hasPortableLiveResourceKey;
    // The EntityIDs the map lays out in one room, asked of the room a clean load
    // produced. Called once per room name per capture or restore, so the map is
    // walked once however many entities ask about it.
    //
    // Returning null means there is no map data to read, and it is a different answer
    // from an empty set: an empty room places nothing, while no map data proves
    // nothing. IsMapPlacedEntityId keeps the two apart because the refusal built on
    // this rule accuses the player's map of having changed.
    private readonly Func<object, string, IEnumerable<int>> getMapPlacedEntityIds;
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
            AkronHashIndex.Rebuild(value);
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
        long maxJsonExpensiveRecordCount = DefaultMaxJsonExpensiveRecordCount,
        Func<Type, bool> areEquivalentLiveResources = null,
        Func<object, bool> hasPortableLiveResourceKey = null,
        Func<object, string, IEnumerable<int>> getMapPlacedEntityIds = null,
        Func<Type, string, object> recreateDetachedLiveResource = null,
        Func<object, bool> isAdditionalLiveResource = null,
        Func<Type, string, bool> hasDeferredDetachedLiveResourceKey = null
    ) {
        this.isLiveResource = isLiveResource ?? throw new ArgumentNullException(nameof(isLiveResource));
        this.isAdditionalLiveResource = isAdditionalLiveResource;
        this.hasDeferredDetachedLiveResourceKey = hasDeferredDetachedLiveResourceKey;
        this.getLiveResourceKey = getLiveResourceKey;
        this.resourceAdapter = resourceAdapter;
        this.resolveDetachedLiveResource = resolveDetachedLiveResource;
        this.recreateDetachedLiveResource = recreateDetachedLiveResource;
        this.areEquivalentLiveResources = areEquivalentLiveResources;
        this.hasPortableLiveResourceKey = hasPortableLiveResourceKey;
        this.getMapPlacedEntityIds = getMapPlacedEntityIds;
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
            AkronReconstructionValue root = context.CaptureValue(savedRoot, freshBaselineRoot, "$");
            if (root.Kind != ReferenceValueKind) {
                return AkronReconstructionCapture.Failed("$", "root must be a reference node");
            }

            context.Document.RootNodeId = root.NodeId;
            return AkronReconstructionCapture.Succeeded(context.Document);
        } catch (AkronReconstructionException exception) {
            return AkronReconstructionCapture.Failed(exception.Path, exception.Message);
        } catch (OperationCanceledException) {
            // Not a capture failure and must not be described as one. Quitting cancels
            // through AkronSnapshotPacing.Pace, which this walk calls once per node, so
            // the common cancellation lands right here - and the only handler that knows
            // how to say it in a sentence a player can read is in
            // AkronStartPosPersistence.RunWorker. Reporting it below instead put
            // "$: OperationCanceledException: ..." on screen.
            throw;
        } catch (Exception exception) {
            return AkronReconstructionCapture.Failed("$", exception.GetType().Name + ": " + exception.Message);
        }
    }

    // v10 writes each distinct assembly-qualified type name once, in the document's
    // TypeNames table, and a small integer at every use. Build runs at the top of a
    // serialize and fills the serialized index shadows from the [JsonIgnore] strings
    // capture populated; Resolve runs right after a deserialize and rebuilds those
    // strings, so capture, validation, and restore only ever see plain strings. The
    // nested ActionStateDocument shares the root table: it is serialized inside the
    // root document, so its indexes always travel with the list they point into.
    //
    // One walker owns the list of type-name slots so the two directions cannot
    // drift: a slot added to the model gets indexed and resolved in the same place
    // or not at all, and the round-trip test fails loudly on the latter.
    private static void MapTypeNames(
        AkronReconstructionDocument document,
        bool build,
        Func<string, int> toIndex,
        Func<int, string> toName
    ) {
        List<int> MapNames(List<string> names) {
            if (names == null || names.Count == 0) {
                return null;
            }
            List<int> indexes = new List<int>(names.Count);
            foreach (string name in names) {
                indexes.Add(toIndex(name));
            }
            return indexes;
        }

        List<string> MapIndexes(List<int> indexes) {
            if (indexes == null || indexes.Count == 0) {
                return null;
            }
            List<string> names = new List<string>(indexes.Count);
            foreach (int index in indexes) {
                names.Add(toName(index));
            }
            return names;
        }

        void MapValue(AkronReconstructionValue value) {
            if (value == null) {
                return;
            }
            if (build) {
                value.TypeNameIndex = toIndex(value.TypeName);
            } else {
                value.TypeName = toName(value.TypeNameIndex);
            }
        }

        void Walk(AkronReconstructionDocument current) {
            if (current == null) {
                return;
            }
            foreach (AkronReconstructionNode node in current.Nodes ?? new List<AkronReconstructionNode>()) {
                if (node == null) {
                    continue;
                }
                if (build) {
                    node.TypeNameIndex = toIndex(node.TypeName);
                    node.ParentDeclaringTypeNameIndex = toIndex(node.ParentDeclaringTypeName);
                } else {
                    node.TypeName = toName(node.TypeNameIndex);
                    node.ParentDeclaringTypeName = toName(node.ParentDeclaringTypeNameIndex);
                }
                foreach (AkronReconstructionField field in node.FieldsOrNull ?? Enumerable.Empty<AkronReconstructionField>()) {
                    if (field == null) {
                        continue;
                    }
                    if (build) {
                        field.DeclaringTypeNameIndex = toIndex(field.DeclaringTypeName);
                    } else {
                        field.DeclaringTypeName = toName(field.DeclaringTypeNameIndex);
                    }
                    MapValue(field.Value);
                }
                foreach (AkronReconstructionValue item in node.ItemsOrNull ?? Enumerable.Empty<AkronReconstructionValue>()) {
                    MapValue(item);
                }
                foreach (AkronReconstructionPathStep step in node.FreshPathOrNull ?? Enumerable.Empty<AkronReconstructionPathStep>()) {
                    if (step == null) {
                        continue;
                    }
                    if (build) {
                        step.DeclaringTypeNameIndex = toIndex(step.DeclaringTypeName);
                    } else {
                        step.DeclaringTypeName = toName(step.DeclaringTypeNameIndex);
                    }
                }
                foreach (AkronReconstructionDelegateCall call in node.DelegateCallsOrNull ?? Enumerable.Empty<AkronReconstructionDelegateCall>()) {
                    if (call == null) {
                        continue;
                    }
                    if (build) {
                        call.DeclaringTypeNameIndex = toIndex(call.DeclaringTypeName);
                        call.ReturnTypeNameIndex = toIndex(call.ReturnTypeName);
                        call.ParameterTypeNameIndexes = MapNames(call.ParameterTypeNames);
                        call.HookTargetDeclaringTypeNameIndex = toIndex(call.HookTargetDeclaringTypeName);
                        call.HookTargetReturnTypeNameIndex = toIndex(call.HookTargetReturnTypeName);
                        call.HookTargetParameterTypeNameIndexes = MapNames(call.HookTargetParameterTypeNames);
                    } else {
                        call.DeclaringTypeName = toName(call.DeclaringTypeNameIndex);
                        call.ReturnTypeName = toName(call.ReturnTypeNameIndex);
                        call.ParameterTypeNames = MapIndexes(call.ParameterTypeNameIndexes);
                        call.HookTargetDeclaringTypeName = toName(call.HookTargetDeclaringTypeNameIndex);
                        call.HookTargetReturnTypeName = toName(call.HookTargetReturnTypeNameIndex);
                        call.HookTargetParameterTypeNames = MapIndexes(call.HookTargetParameterTypeNameIndexes);
                    }
                    MapValue(call.Target);
                }
            }
            Walk(current.ActionStateDocument);
        }

        Walk(document);
    }

    // Internal so tests can craft structurally invalid v10 files: a hostile writer
    // still produces a table, and Serialize cannot produce those files because it
    // validates the document first.
    internal static void BuildTypeNameTable(AkronReconstructionDocument document) {
        List<string> table = new List<string>();
        Dictionary<string, int> indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        MapTypeNames(document, build: true, typeName => {
            if (string.IsNullOrEmpty(typeName)) {
                return -1;
            }
            if (!indexes.TryGetValue(typeName, out int index)) {
                index = table.Count;
                table.Add(typeName);
                indexes[typeName] = index;
            }
            return index;
        }, toName: null);
        document.TypeNames = table;
        if (document.ActionStateDocument != null) {
            // The nested document's uses are indexed into the root table above, so a
            // table of its own would be dead weight that a reader might resolve against.
            document.ActionStateDocument.TypeNames = new List<string>();
        }
    }

    private static void ValidateTypeNameIndexes(AkronReconstructionDocument document) {
        List<string> table = document?.TypeNames;
        if (table == null) {
            throw new InvalidOperationException("Reconstruction type name table is missing.");
        }
        ValidateTypeNameIndexes(document, table);
    }

    private static void ValidateTypeNameIndexes(
        AkronReconstructionDocument document,
        List<string> table
    ) {
        if (document == null) {
            return;
        }
        foreach (AkronReconstructionNode node in document.Nodes) {
            if (node == null) {
                continue;
            }
            ValidateTypeNameIndex(node.TypeName, node.TypeNameIndex, table);
            ValidateTypeNameIndex(
                node.ParentDeclaringTypeName,
                node.ParentDeclaringTypeNameIndex,
                table);
            if (node.FieldsOrNull != null) {
                foreach (AkronReconstructionField field in node.FieldsOrNull) {
                    if (field == null) {
                        continue;
                    }
                    ValidateTypeNameIndex(field.DeclaringTypeName, field.DeclaringTypeNameIndex, table);
                    ValidateValueTypeNameIndex(field.Value, table);
                }
            }
            if (node.ItemsOrNull != null) {
                foreach (AkronReconstructionValue item in node.ItemsOrNull) {
                    ValidateValueTypeNameIndex(item, table);
                }
            }
            if (node.FreshPathOrNull != null) {
                foreach (AkronReconstructionPathStep step in node.FreshPathOrNull) {
                    if (step != null) {
                        ValidateTypeNameIndex(step.DeclaringTypeName, step.DeclaringTypeNameIndex, table);
                    }
                }
            }
            if (node.DelegateCallsOrNull != null) {
                foreach (AkronReconstructionDelegateCall call in node.DelegateCallsOrNull) {
                    if (call == null) {
                        continue;
                    }
                    ValidateTypeNameIndex(call.DeclaringTypeName, call.DeclaringTypeNameIndex, table);
                    ValidateTypeNameIndex(call.ReturnTypeName, call.ReturnTypeNameIndex, table);
                    ValidateTypeNameIndexList(call.ParameterTypeNames, call.ParameterTypeNameIndexes, table);
                    ValidateTypeNameIndex(
                        call.HookTargetDeclaringTypeName,
                        call.HookTargetDeclaringTypeNameIndex,
                        table);
                    ValidateTypeNameIndex(
                        call.HookTargetReturnTypeName,
                        call.HookTargetReturnTypeNameIndex,
                        table);
                    ValidateTypeNameIndexList(
                        call.HookTargetParameterTypeNames,
                        call.HookTargetParameterTypeNameIndexes,
                        table);
                    ValidateValueTypeNameIndex(call.Target, table);
                }
            }
        }
        ValidateTypeNameIndexes(document.ActionStateDocument, table);
    }

    private static void ValidateValueTypeNameIndex(
        AkronReconstructionValue value,
        List<string> table
    ) {
        if (value != null) {
            ValidateTypeNameIndex(value.TypeName, value.TypeNameIndex, table);
        }
    }

    private static void ValidateTypeNameIndexList(
        List<string> names,
        List<int> indexes,
        List<string> table
    ) {
        int count = names?.Count ?? 0;
        if (count != (indexes?.Count ?? 0)) {
            throw new InvalidOperationException("Reconstruction type name index list differs from its names.");
        }
        for (int index = 0; index < count; index++) {
            ValidateTypeNameIndex(names[index], indexes[index], table);
        }
    }

    private static void ValidateTypeNameIndex(string name, int index, List<string> table) {
        if (string.IsNullOrEmpty(name)) {
            if (index == -1) {
                return;
            }
        } else if (index >= 0 && index < table.Count &&
                   string.Equals(table[index], name, StringComparison.Ordinal)) {
            return;
        }
        throw new InvalidOperationException("Reconstruction type name index differs from its name.");
    }

    private static bool CanReuseTypeNameTable(AkronReconstructionDocument document) {
        List<string> table = document?.TypeNames;
        if (table == null || table.Count == 0 ||
            document.ActionStateDocument?.TypeNames is { Count: > 0 }) {
            return false;
        }
        // BuildTypeNameTable deduplicates names in first-use order. Preserve that
        // canonical wire form rather than merely accepting any internally
        // consistent table an external writer might have supplied.
        for (int index = 0; index < table.Count; index++) {
            string name = table[index];
            if (string.IsNullOrEmpty(name)) {
                return false;
            }
            for (int earlier = 0; earlier < index; earlier++) {
                if (string.Equals(table[earlier], name, StringComparison.Ordinal)) {
                    return false;
                }
            }
        }
        try {
            ValidateTypeNameIndexes(document, table);
        } catch (InvalidOperationException) {
            return false;
        }

        int nextIndex = 0;
        bool Observe(int index) {
            if (index == -1) {
                return true;
            }
            if (index == nextIndex) {
                nextIndex++;
                return true;
            }
            return index >= 0 && index < nextIndex;
        }
        bool ObserveValue(AkronReconstructionValue value) {
            return value == null || Observe(value.TypeNameIndex);
        }
        bool ObserveList(List<int> indexes) {
            if (indexes == null) {
                return true;
            }
            foreach (int index in indexes) {
                if (!Observe(index)) {
                    return false;
                }
            }
            return true;
        }
        bool Walk(AkronReconstructionDocument current) {
            if (current == null) {
                return true;
            }
            foreach (AkronReconstructionNode node in current.Nodes) {
                if (node == null) {
                    continue;
                }
                if (!Observe(node.TypeNameIndex) || !Observe(node.ParentDeclaringTypeNameIndex)) {
                    return false;
                }
                if (node.FieldsOrNull != null) {
                    foreach (AkronReconstructionField field in node.FieldsOrNull) {
                        if (field != null &&
                            (!Observe(field.DeclaringTypeNameIndex) || !ObserveValue(field.Value))) {
                            return false;
                        }
                    }
                }
                if (node.ItemsOrNull != null) {
                    foreach (AkronReconstructionValue item in node.ItemsOrNull) {
                        if (!ObserveValue(item)) {
                            return false;
                        }
                    }
                }
                if (node.FreshPathOrNull != null) {
                    foreach (AkronReconstructionPathStep step in node.FreshPathOrNull) {
                        if (step != null && !Observe(step.DeclaringTypeNameIndex)) {
                            return false;
                        }
                    }
                }
                if (node.DelegateCallsOrNull != null) {
                    foreach (AkronReconstructionDelegateCall call in node.DelegateCallsOrNull) {
                        if (call != null &&
                            (!Observe(call.DeclaringTypeNameIndex) ||
                             !Observe(call.ReturnTypeNameIndex) ||
                             !ObserveList(call.ParameterTypeNameIndexes) ||
                             !Observe(call.HookTargetDeclaringTypeNameIndex) ||
                             !Observe(call.HookTargetReturnTypeNameIndex) ||
                             !ObserveList(call.HookTargetParameterTypeNameIndexes) ||
                             !ObserveValue(call.Target))) {
                            return false;
                        }
                    }
                }
            }
            return Walk(current.ActionStateDocument);
        }
        return Walk(document) && nextIndex == table.Count;
    }

    private static void ResolveTypeNames(AkronReconstructionDocument document) {
        List<string> table = document?.TypeNames ?? new List<string>();
        MapTypeNames(document, build: false, toIndex: null, index => {
            if (index == -1) {
                return string.Empty;
            }
            if (index < 0 || index >= table.Count) {
                throw new InvalidOperationException("Reconstruction type name index is out of range.");
            }
            return table[index];
        });
    }

    public string Serialize(AkronReconstructionDocument document) {
        PrepareForSerialization(document);
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
        ResolveTypeNames(document);
        IReadOnlyDictionary<int, AkronReconstructionNode> nodes = ValidateDocumentHeader(document);
        RestoreDiagnosticPaths(document, nodes);
        return document;
    }

    public void Serialize(AkronReconstructionDocument document, Stream stream) {
        PrepareForSerialization(document);
        if (stream == null || !stream.CanWrite) {
            throw new InvalidOperationException("Reconstruction output stream is unavailable.");
        }

        JsonSerializer serializer = JsonSerializer.Create(JsonSettings);
        using StreamWriter streamWriter = new StreamWriter(stream, new UTF8Encoding(false), 65536, leaveOpen: true);
        using JsonTextWriter jsonWriter = new JsonTextWriter(streamWriter) { CloseOutput = false };
        serializer.Serialize(jsonWriter, document);
        jsonWriter.Flush();
    }

    private void PrepareForSerialization(AkronReconstructionDocument document) {
        ValidateDocumentHeader(document);
        if (!CanReuseTypeNameTable(document)) {
            BuildTypeNameTable(document);
        }
        // Validate the exact indexed view that will go to disk, not only the
        // pre-index strings. This retains the old read-back guarantee without
        // constructing a second copy of the complete object graph.
        ValidateTypeNameIndexes(document);
    }

    public AkronReconstructionDocument Deserialize(Stream stream) {
        if (stream == null || !stream.CanRead) {
            throw new InvalidOperationException("Reconstruction input stream is unavailable.");
        }

        JsonSerializer serializer = JsonSerializer.Create(JsonSettings);
        using StreamReader streamReader = new StreamReader(stream, Encoding.UTF8, true, 65536, leaveOpen: true);
        using AkronBoundedJsonTextReader jsonReader = CreateJsonReader(streamReader);
        AkronReconstructionDocument document = serializer.Deserialize<AkronReconstructionDocument>(jsonReader);
        ResolveTypeNames(document);
        IReadOnlyDictionary<int, AkronReconstructionNode> nodes = ValidateDocumentHeader(document);
        RestoreDiagnosticPaths(document, nodes);
        return document;
    }

    internal void ValidateSerializedDocument(Stream stream) {
        if (stream == null || !stream.CanRead) {
            throw new InvalidOperationException("Reconstruction input stream is unavailable.");
        }

        using StreamReader streamReader = new StreamReader(stream, Encoding.UTF8, true, 65536, leaveOpen: true);
        using AkronBoundedJsonTextReader jsonReader = CreateJsonReader(streamReader);
        bool readAny = false;
        while (jsonReader.Read()) {
            readAny = true;
        }
        if (!readAny) {
            throw new InvalidOperationException("Reconstruction document is empty.");
        }
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
            return AkronReconstructionRestore.Failed(
                exception.Path,
                exception.Message,
                exception.RefusedTypeName,
                exception.RefusedKind);
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
            return AkronReconstructionVerification.Failed(
                exception.Path,
                exception.Message,
                exception.RefusedTypeName,
                exception.RefusedKind);
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
            return AkronReconstructionVerification.Failed(
                exception.Path,
                exception.Message,
                exception.RefusedTypeName,
                exception.RefusedKind);
        } catch (Exception exception) {
            return AkronReconstructionVerification.Failed("$", exception.GetType().Name + ": " + exception.Message);
        }
    }

    private IReadOnlyDictionary<int, AkronReconstructionNode> ValidateDocumentHeader(
        AkronReconstructionDocument document
    ) {
        if (document == null || !string.Equals(document.Format, AkronReconstructionDocument.CurrentFormat, StringComparison.Ordinal)) {
            // The one gate that decides whether a document may be read at all, so it is
            // also the one place that can say why. A document names the objects in a
            // room by where they sit in a clean reload of it, and Akron now rebuilds
            // that room differently, so an older document does not describe this room -
            // it describes a room that had one more object in it. Reading it anyway can
            // give one entity another entity's saved state without noticing, so it is
            // refused here and never upgraded.
            // The action comes first because this text reaches a toast through
            // ReportStartPosLoadFailure, which cuts at 180 characters, and the two
            // format names would otherwise push it off the end.
            throw new InvalidOperationException(
                "Reconstruction document format is unsupported: set this StartPos again. It is " +
                DescribeDocumentFormat(document) + " and Akron now writes " +
                AkronReconstructionDocument.CurrentFormat +
                ", and a snapshot from an older Akron describes a fresh room this build no longer loads.");
        }
        if (document.RootNodeId <= 0 || document.Nodes == null || document.Nodes.Count == 0) {
            throw new InvalidOperationException("Reconstruction document has no root node.");
        }
        Dictionary<int, AkronReconstructionNode> nodes =
            new Dictionary<int, AkronReconstructionNode>(document.Nodes.Count);
        bool rootFound = false;
        foreach (AkronReconstructionNode node in document.Nodes) {
            if (node == null || node.Id <= 0 || !nodes.TryAdd(node.Id, node)) {
                throw new InvalidOperationException("Reconstruction document has invalid node IDs.");
            }
            rootFound |= node.Id == document.RootNodeId;
        }
        if (!rootFound) {
            throw new InvalidOperationException("Reconstruction document root node is missing.");
        }
        ValidateNodeKindContracts(document);
        ValidateNodeParentEdges(document, nodes);
        ValidateNodeReachability(document, nodes);
        if (document.ActionStateDocument != null) {
            ValidateDocumentHeader(document.ActionStateDocument);
        }
        return nodes;
    }

    // The format string comes out of a snapshot file and the reader allows a string into
    // the megabytes, so a corrupt or hostile one must not end up in a message that
    // reaches a toast. Real format names are under twenty characters.
    private static string DescribeDocumentFormat(AkronReconstructionDocument document) {
        string format = document?.Format;
        if (string.IsNullOrWhiteSpace(format)) {
            return "unnamed";
        }
        return format.Length <= MaxReportedDocumentFormatChars
            ? format
            : format.Substring(0, MaxReportedDocumentFormatChars) + "...";
    }

    private const int MaxReportedDocumentFormatChars = 32;

    private void ValidateNodeKindContracts(AkronReconstructionDocument document) {
        foreach (AkronReconstructionNode node in document.Nodes) {
            Type type = ResolveType(node.TypeName, "$");
            bool valid = node.Kind switch {
                ObjectKind => true,
                ArrayKind => type.IsArray,
                DelegateKind => typeof(Delegate).IsAssignableFrom(type),
                EventInstanceKind => type == typeof(EventInstance) && node.EventInstance != null,
                PersistentResourceKind => resourceAdapter?.CanPersist(type) == true && node.ResourcePayload != null,
                // Exactly two items: the target, then the resurrection flag as a
                // scalar. Requiring the flag's slot to be a scalar here is what
                // lets RefuseAReferenceInASlotTheRestoreNeverReads treat the whole
                // item list as read: a reference can only ever sit in Items[0].
                WeakReferenceKind => IsWeakReferenceType(type) &&
                                     node.ItemsOrNull is { Count: 2 } &&
                                     node.ItemsOrNull[1]?.Kind == ScalarValueKind,
                AnchorKind => node.UseFreshObject &&
                              (isLiveResource(type) ||
                               typeof(Delegate).IsAssignableFrom(type) ||
                               IsDeferredDetachedAnchor(node, type) ||
                               IsResolvableDetachedAnchor(node, type)),
                _ => false
            };
            if (!valid) {
                string kind = string.IsNullOrWhiteSpace(node.Kind) ? "empty" : node.Kind;
                throw new InvalidOperationException(
                    "Reconstruction " + kind + " type is invalid: " + (type.FullName ?? type.Name));
            }
            // After the type check, so a node relabelled to a kind its type cannot
            // be still fails on the type - which is the more useful thing to say,
            // and what RestoreRejectsAnOrdinaryObjectRelabeledAsAnAnchor reads.
            RefuseAReferenceInASlotTheRestoreNeverReads(node, type);
        }
    }

    private bool IsResolvableDetachedAnchor(AkronReconstructionNode node, Type resourceType) {
        return node.PortableResourceKey &&
               !string.IsNullOrWhiteSpace(node.ResourceKey) &&
               resolveDetachedLiveResource?.Invoke(resourceType, node.ResourceKey) != null;
    }

    private bool IsDeferredDetachedAnchor(AkronReconstructionNode node, Type resourceType) {
        return node.PortableResourceKey &&
               !string.IsNullOrWhiteSpace(node.ResourceKey) &&
               hasDeferredDetachedLiveResourceKey?.Invoke(resourceType, node.ResourceKey) == true;
    }

    // A document names objects by where they sit, and the restore attaches each
    // one by writing the slot its parent holds it in. Which slot that is depends
    // on the parent's kind, and each kind is read from exactly one container: an
    // object's fields, an array's items, a delegate's calls. An anchor, a
    // persistent resource and an FMOD event are read from no container at all -
    // the first is the fresh room's own object and the other two carry a payload.
    // Two slots inside a container that is read are skipped as well: a packed
    // primitive array is restored from its bytes rather than its items, and a
    // detour-next delegate call binds no target.
    //
    // A reference in any other slot claims two things that are not true.
    // ValidateNodeReachability walks fields, items and calls without asking what
    // kind the parent is, so the node it points at counts as reached and the
    // document passes as complete, while nothing ever writes that slot: the
    // object is created, its own state is applied, it joins Objects, and Verify
    // walks Objects rather than the room, so the restore reports success with
    // that object attached to nothing. And IndexSavedFieldAliases indexes every
    // node's fields whatever its kind, while IndexSavedArrayAliases reads a packed
    // array's items, so the same dead edge is also read as evidence that the saved
    // graph held one object in two places, which is what licenses handing a
    // reconstruction a live object from the fresh room.
    //
    // Capture cannot write one. CaptureValue returns as soon as it has made a
    // live anchor, stores only a payload for a persistent resource or an FMOD
    // event, and otherwise hands the node to exactly one of CaptureObject,
    // CaptureArray or CaptureDelegate; CaptureArray returns after packing a
    // primitive grid without adding an item; CaptureDelegate writes a null target
    // for the one detour-next call it ever emits; and CaptureObject skips a
    // derived collection's version counter, which is the third slot inside a read
    // container that the restore skips - and a field skipped by name is never
    // type-checked either, so a reference parked there is not even required to fit
    // the slot.
    //
    // Scalars in those slots are left alone rather than refused. A scalar
    // attaches nothing and aliases nothing, since both index builders skip a value
    // that is not a reference, and every lie a scalar could tell fits just as well
    // in a slot the restore does read, so refusing it would buy nothing;
    // CollectionVersionChangesDoNotInvalidateEquivalentContents pins one
    // deliberately, a version counter the document may carry and the restore must
    // ignore. Only a reference is refused, because a reference in a slot nothing
    // reads is the one claim a document cannot make anywhere else.
    private static void RefuseAReferenceInASlotTheRestoreNeverReads(
        AkronReconstructionNode node,
        Type type
    ) {
        // The kind is one of the seven the switch above admits, so these are exact.
        // A weak reference's items are both read: the target by CreateWeakReference
        // and the flag by its scalar decode, and the kind contract has already
        // pinned the flag slot to a scalar, so a reference there cannot exist.
        bool readsFields = node.Kind == ObjectKind;
        bool readsItems = node.Kind == ArrayKind && node.PackedPrimitiveArrayBytes == null ||
                          node.Kind == WeakReferenceKind;
        bool readsCalls = node.Kind == DelegateKind;

        foreach (AkronReconstructionField field in node.FieldsOrNull ?? Enumerable.Empty<AkronReconstructionField>()) {
            if (field?.Value?.Kind != ReferenceValueKind) {
                continue;
            }
            if (!readsFields) {
                throw UnreadSlot(node, field.Value.NodeId, "a field");
            }
            if (IsDerivedCollectionVersionField(type, field.Name)) {
                throw UnreadSlot(node, field.Value.NodeId, "a derived collection version field");
            }
        }
        if (!readsItems) {
            foreach (AkronReconstructionValue item in node.ItemsOrNull ?? Enumerable.Empty<AkronReconstructionValue>()) {
                if (item?.Kind == ReferenceValueKind) {
                    throw UnreadSlot(
                        node,
                        item.NodeId,
                        node.PackedPrimitiveArrayBytes != null ? "a packed primitive array item" : "an item");
                }
            }
        }
        foreach (AkronReconstructionDelegateCall call in node.DelegateCallsOrNull ?? Enumerable.Empty<AkronReconstructionDelegateCall>()) {
            if (call?.Target?.Kind != ReferenceValueKind) {
                continue;
            }
            if (!readsCalls) {
                throw UnreadSlot(node, call.Target.NodeId, "a delegate call target");
            }
            // Of a delegate node's calls, only a method call's target is bound.
            // CreateDelegate rebuilds a detour-next call from its position in the
            // live detour chain and binds no target, so it never reads the saved
            // one, and CaptureDelegate writes a null there for that reason. Any
            // other kind CreateDelegate refuses outright; keying on the one kind
            // that is read says what is true rather than listing what is not.
            if (!string.Equals(call.Kind, MethodDelegateCallKind, StringComparison.Ordinal)) {
                throw UnreadSlot(node, call.Target.NodeId, "the target of a delegate call that binds none");
            }
        }
    }

    // Node ids and the kind rather than the field name or the type: everything
    // here comes out of a snapshot file, and the reader lets a name run into the
    // megabytes, so only bounded values go into a message that can reach a toast.
    private static InvalidOperationException UnreadSlot(
        AkronReconstructionNode node,
        int referencedNodeId,
        string slot
    ) {
        return new InvalidOperationException(
            "Reconstruction " + node.Kind + " node " +
            node.Id.ToString(CultureInfo.InvariantCulture) +
            " holds node " + referencedNodeId.ToString(CultureInfo.InvariantCulture) +
            " in " + slot + ", which the restore never reads.");
    }

    private static void ValidateNodeParentEdges(
        AkronReconstructionDocument document,
        IReadOnlyDictionary<int, AkronReconstructionNode> nodes
    ) {
        // Index each field once. Looking through every parent field for every
        // child makes a wide crafted snapshot quadratic to validate.
        int fieldCount = 0;
        foreach (AkronReconstructionNode parent in document.Nodes) {
            fieldCount = checked(fieldCount + (parent.FieldsOrNull?.Count ?? 0));
        }
        Dictionary<(int ParentNodeId, string DeclaringTypeName, string FieldName), AkronReconstructionValue>
            parentFieldValues = new Dictionary<(int, string, string), AkronReconstructionValue>(fieldCount);
        foreach (AkronReconstructionNode parent in document.Nodes) {
            foreach (AkronReconstructionField field in parent.FieldsOrNull ?? Enumerable.Empty<AkronReconstructionField>()) {
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
                       TryGetFlatArrayIndex(parent, node.ParentArrayIndicesOrNull, out int itemIndex) &&
                       parent.ItemsOrNull != null && itemIndex < parent.ItemsOrNull.Count) {
                parentValue = parent.ItemsOrNull[itemIndex];
            } else if (node.ParentKind == "delegate" &&
                       node.ParentDelegateIndex >= 0 &&
                       parent.DelegateCallsOrNull != null &&
                       node.ParentDelegateIndex < parent.DelegateCallsOrNull.Count) {
                parentValue = parent.DelegateCallsOrNull[node.ParentDelegateIndex]?.Target;
            } else if (node.ParentKind == "weak-target" &&
                       string.Equals(parent.Kind, WeakReferenceKind, StringComparison.Ordinal) &&
                       parent.ItemsOrNull is { Count: > 0 }) {
                parentValue = parent.ItemsOrNull[0];
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
        if (arrayNode.ArrayLengthsOrNull == null || arrayNode.ArrayLowerBoundsOrNull == null || indices == null ||
            arrayNode.ArrayLengthsOrNull.Count == 0 ||
            arrayNode.ArrayLengthsOrNull.Count != arrayNode.ArrayLowerBoundsOrNull.Count ||
            arrayNode.ArrayLengthsOrNull.Count != indices.Count) {
            return false;
        }
        long offset = 0;
        for (int dimension = 0; dimension < indices.Count; dimension++) {
            int length = arrayNode.ArrayLengthsOrNull[dimension];
            int lowerBound = arrayNode.ArrayLowerBoundsOrNull[dimension];
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

    private static void ValidateNodeReachability(
        AkronReconstructionDocument document,
        IReadOnlyDictionary<int, AkronReconstructionNode> nodes
    ) {
        HashSet<int> reached = new HashSet<int>(document.Nodes.Count);
        Stack<int> pending = new Stack<int>(document.Nodes.Count);
        pending.Push(document.RootNodeId);
        while (pending.Count > 0) {
            int nodeId = pending.Pop();
            if (!reached.Add(nodeId)) {
                continue;
            }
            AkronReconstructionNode node = nodes[nodeId];
            if (node.FieldsOrNull != null) {
                foreach (AkronReconstructionField field in node.FieldsOrNull) {
                    QueueReachableReference(field?.Value, nodes, pending);
                }
            }
            if (node.ItemsOrNull != null) {
                foreach (AkronReconstructionValue item in node.ItemsOrNull) {
                    QueueReachableReference(item, nodes, pending);
                }
            }
            if (node.DelegateCallsOrNull != null) {
                foreach (AkronReconstructionDelegateCall call in node.DelegateCallsOrNull) {
                    QueueReachableReference(call?.Target, nodes, pending);
                }
            }
        }
        if (reached.Count != nodes.Count) {
            throw new InvalidOperationException("Reconstruction document contains nodes that are not reachable from its root.");
        }
    }

    private static void QueueReachableReference(
        AkronReconstructionValue value,
        IReadOnlyDictionary<int, AkronReconstructionNode> nodes,
        Stack<int> pending
    ) {
        if (value?.Kind != ReferenceValueKind) {
            return;
        }
        if (!nodes.ContainsKey(value.NodeId)) {
            throw new InvalidOperationException("Reconstruction document contains an invalid node reference.");
        }
        pending.Push(value.NodeId);
    }

    private static void RestoreDiagnosticPaths(
        AkronReconstructionDocument document,
        IReadOnlyDictionary<int, AkronReconstructionNode> nodes
    ) {
        long totalPathChars = 0;
        RestoreDiagnosticPaths(document, nodes, ref totalPathChars);
    }

    private static void RestoreDiagnosticPaths(
        AkronReconstructionDocument document,
        IReadOnlyDictionary<int, AkronReconstructionNode> nodes,
        ref long totalPathChars
    ) {
        int scratchCapacity = Math.Min(document.Nodes.Count, MaxParentChainDepth);
        List<AkronReconstructionNode> unresolved = new List<AkronReconstructionNode>(scratchCapacity);
        HashSet<int> resolving = new HashSet<int>(scratchCapacity);
        foreach (AkronReconstructionNode node in document.Nodes) {
            RestoreNodePath(
                node,
                document.RootNodeId,
                nodes,
                unresolved,
                resolving,
                ref totalPathChars);
        }
        foreach (AkronReconstructionNode node in document.Nodes) {
            foreach (AkronReconstructionField field in node.FieldsOrNull ?? Enumerable.Empty<AkronReconstructionField>()) {
                int pathLength;
                if (field.Value?.Kind == ReferenceValueKind &&
                    nodes.TryGetValue(field.Value.NodeId, out AkronReconstructionNode child) &&
                    child.ParentNodeId == node.Id &&
                    child.ParentKind == "field" &&
                    string.Equals(child.ParentDeclaringTypeName, field.DeclaringTypeName, StringComparison.Ordinal) &&
                    string.Equals(child.ParentFieldName, field.Name, StringComparison.Ordinal)) {
                    field.SetLazyChildDiagnosticPath(child);
                    pathLength = child.DiagnosticPathLength;
                } else {
                    field.SetLazyParentDiagnosticPath(node);
                    pathLength = GetFieldDiagnosticPathLength(node.DiagnosticPathLength, field.Name);
                }
                AddDiagnosticPathChars(pathLength, ref totalPathChars);
            }
        }
        if (document.ActionStateDocument != null) {
            RestoreDiagnosticPaths(
                document.ActionStateDocument,
                document.ActionStateDocument.Nodes.ToDictionary(node => node.Id),
                ref totalPathChars);
        }
    }

    private static int RestoreNodePath(
        AkronReconstructionNode node,
        int rootNodeId,
        IReadOnlyDictionary<int, AkronReconstructionNode> nodes,
        List<AkronReconstructionNode> unresolved,
        HashSet<int> resolving,
        ref long totalPathChars
    ) {
        if (node.DiagnosticPathReady) {
            return node.DiagnosticPathLength;
        }
        unresolved.Clear();
        resolving.Clear();
        AkronReconstructionNode current = node;
        while (!current.DiagnosticPathReady) {
            if (current.Id == rootNodeId) {
                current.Path = "$";
                AddDiagnosticPathChars(current.DiagnosticPathLength, ref totalPathChars);
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

        int parentPathLength = current.DiagnosticPathLength;
        for (int index = unresolved.Count - 1; index >= 0; index--) {
            AkronReconstructionNode child = unresolved[index];
            int childPathLength;
            switch (child.ParentKind) {
                case "field":
                    childPathLength = GetFieldDiagnosticPathLength(parentPathLength, child.ParentFieldName);
                    break;
                case "array":
                    childPathLength = GetArrayDiagnosticPathLength(parentPathLength, child.ParentArrayIndicesOrNull);
                    break;
                case "delegate":
                    childPathLength = GetDelegateDiagnosticPathLength(parentPathLength, child.ParentDelegateIndex);
                    break;
                case "weak-target":
                    childPathLength = GetDiagnosticPathLength(parentPathLength, ".<weak-target>");
                    break;
                default:
                    throw new InvalidOperationException("Reconstruction node parent kind is invalid.");
            }
            child.SetLazyDiagnosticPath(current, childPathLength);
            AddDiagnosticPathChars(childPathLength, ref totalPathChars);
            current = child;
            parentPathLength = childPathLength;
        }
        return node.DiagnosticPathLength;
    }

    private static bool IsWeakReferenceType(Type type) {
        return type == typeof(WeakReference) ||
               type is { IsGenericType: true } && type.GetGenericTypeDefinition() == typeof(WeakReference<>);
    }

    // WeakReference<T> exposes its target through TryGetTarget alone and never
    // exposes its resurrection flag, so a generic weak reference reads as
    // (target, false) and is rebuilt without the flag. The flag only matters in
    // the window between a finalizer running and its object being collected,
    // which nothing in a room capture can observe.
    private static (object Target, bool TrackResurrection) ReadWeakReference(object weakReference) {
        if (weakReference is WeakReference plain) {
            return (plain.Target, plain.TrackResurrection);
        }
        object[] arguments = { null };
        weakReference.GetType().GetMethod(nameof(WeakReference<object>.TryGetTarget))!
            .Invoke(weakReference, arguments);
        return (arguments[0], false);
    }

    internal static string MaterializeDiagnosticPath(AkronReconstructionNode node) {
        string parentPath = node.DiagnosticPathParent?.Path;
        return node.ParentKind switch {
            "field" => BuildFieldDiagnosticPath(parentPath, node.ParentFieldName),
            "array" => BuildArrayDiagnosticPath(parentPath, node.ParentArrayIndicesOrNull),
            "delegate" => BuildDelegateDiagnosticPath(parentPath, node.ParentDelegateIndex),
            "weak-target" => BuildDiagnosticPath(parentPath, ".<weak-target>"),
            _ => throw new InvalidOperationException("Reconstruction node parent kind is invalid.")
        };
    }

    private static int GetDiagnosticPathLength(int parentPathLength, string suffix) {
        int suffixLength = suffix?.Length ?? 0;
        if (parentPathLength < 0 || parentPathLength > MaxDiagnosticPathChars - suffixLength) {
            throw new InvalidOperationException("Reconstruction diagnostic path exceeds the supported limit.");
        }
        return parentPathLength + suffixLength;
    }

    private static string BuildDiagnosticPath(string parentPath, string suffix) {
        parentPath ??= string.Empty;
        suffix ??= string.Empty;
        GetDiagnosticPathLength(parentPath.Length, suffix);
        return parentPath + suffix;
    }

    private static int GetFieldDiagnosticPathLength(int parentPathLength, string fieldName) {
        fieldName ??= string.Empty;
        if (parentPathLength < 0 || fieldName.Length >= MaxDiagnosticPathChars ||
            parentPathLength > MaxDiagnosticPathChars - fieldName.Length - 1) {
            throw new InvalidOperationException("Reconstruction diagnostic path exceeds the supported limit.");
        }
        return parentPathLength + fieldName.Length + 1;
    }

    internal static string BuildFieldDiagnosticPath(string parentPath, string fieldName) {
        parentPath ??= string.Empty;
        fieldName ??= string.Empty;
        GetFieldDiagnosticPathLength(parentPath.Length, fieldName);
        return string.Concat(parentPath, ".", fieldName);
    }

    private static int GetArrayDiagnosticPathLength(
        int parentPathLength,
        IReadOnlyList<int> indices
    ) {
        indices ??= Array.Empty<int>();
        long suffixLength = 2L + Math.Max(0, indices.Count - 1);
        for (int index = 0; index < indices.Count; index++) {
            suffixLength += Int32FormattedLength(indices[index]);
        }
        if (parentPathLength < 0 || suffixLength > MaxDiagnosticPathChars ||
            parentPathLength > MaxDiagnosticPathChars - suffixLength) {
            throw new InvalidOperationException("Reconstruction diagnostic path exceeds the supported limit.");
        }
        return parentPathLength + (int) suffixLength;
    }

    private static string BuildArrayDiagnosticPath(
        string parentPath,
        IReadOnlyList<int> indices
    ) {
        parentPath ??= string.Empty;
        indices ??= Array.Empty<int>();
        int pathLength = GetArrayDiagnosticPathLength(parentPath.Length, indices);
        return string.Create(
            pathLength,
            (Parent: parentPath, Indices: indices),
            static (destination, state) => {
                state.Parent.AsSpan().CopyTo(destination);
                int position = state.Parent.Length;
                destination[position++] = '[';
                for (int index = 0; index < state.Indices.Count; index++) {
                    if (index > 0) {
                        destination[position++] = ',';
                    }
                    state.Indices[index].TryFormat(
                        destination[position..],
                        out int written,
                        provider: CultureInfo.InvariantCulture);
                    position += written;
                }
                destination[position] = ']';
            });
    }

    private static int GetDelegateDiagnosticPathLength(int parentPathLength, int delegateIndex) {
        const string prefix = ".<target>[";
        int suffixLength = prefix.Length + Int32FormattedLength(delegateIndex) + 1;
        if (parentPathLength < 0 || parentPathLength > MaxDiagnosticPathChars - suffixLength) {
            throw new InvalidOperationException("Reconstruction diagnostic path exceeds the supported limit.");
        }
        return parentPathLength + suffixLength;
    }

    private static string BuildDelegateDiagnosticPath(string parentPath, int delegateIndex) {
        parentPath ??= string.Empty;
        const string prefix = ".<target>[";
        int pathLength = GetDelegateDiagnosticPathLength(parentPath.Length, delegateIndex);
        return string.Create(
            pathLength,
            (Parent: parentPath, Index: delegateIndex),
            static (destination, state) => {
                state.Parent.AsSpan().CopyTo(destination);
                int position = state.Parent.Length;
                prefix.AsSpan().CopyTo(destination[position..]);
                position += prefix.Length;
                state.Index.TryFormat(
                    destination[position..],
                    out int written,
                    provider: CultureInfo.InvariantCulture);
                destination[position + written] = ']';
            });
    }

    private static int Int32FormattedLength(int value) {
        uint magnitude = value < 0 ? (uint) -(long) value : (uint) value;
        int length = value < 0 ? 2 : 1;
        while (magnitude >= 10) {
            magnitude /= 10;
            length++;
        }
        return length;
    }

    private static void AddDiagnosticPathChars(int pathChars, ref long totalPathChars) {
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
            // The saved room referenced a type this process cannot load at all, which is
            // what an uninstalled or disabled mod looks like from here.
            throw new AkronReconstructionException(path, "type is unavailable: " + typeName, typeName);
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

    internal static NotSupportedException UnsupportedDetourReflection(string member) {
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

    // IntPtr and UIntPtr report IsPrimitive, and neither is IConvertible, so a
    // scalar written from one can never be read back: Convert.ChangeType throws
    // InvalidCastException on the way in. They are also process pointers, which
    // CaptureValue refuses on purpose. Keeping them out of every primitive gate
    // is what lets that refusal run instead of writing a snapshot that no later
    // process can rebuild. CanPackPrimitiveElementType already knew this; the
    // scalar gate did not, which is the whole of this defect.
    private static bool IsPersistablePrimitive(Type type) {
        return type?.IsPrimitive == true &&
               type != typeof(IntPtr) &&
               type != typeof(UIntPtr);
    }

    private static bool IsScalarType(Type type) {
        return type.IsEnum ||
               IsPersistablePrimitive(type) ||
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
        if (IsPersistablePrimitive(type)) {
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
        if (IsPersistablePrimitive(type)) {
            return Convert.ChangeType(scalar, type, CultureInfo.InvariantCulture);
        }

        // Reached by a snapshot written before the scalar gate excluded process
        // pointers, and by any document that claims one. Refusing here names the
        // field path and the type; Convert.ChangeType threw a bare
        // InvalidCastException that the restore could only report against "$".
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
        AkronReconstructionField eventNameField = (sourceNode.FieldsOrNull ?? Enumerable.Empty<AkronReconstructionField>())
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

    internal static bool IsTrailSnapshotComponentOwnerType(Type ownerType) {
        // Vanilla dash trails point at Player components. Playback tutorials,
        // including Heart of the Storm's, use the same built-in components on
        // PlayerPlayback. No other entity owner is part of this alias contract.
        return ownerType == typeof(Player) || ownerType == typeof(PlayerPlayback);
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

    internal static bool IsRendererListStorageField(string fieldName) {
        return fieldName is "Renderers" or "adding" or "removing";
    }

    internal static bool IsAuthenticatedCompilerIteratorOwner(
        Type iteratorType,
        bool ownerIsFresh,
        bool ownerIsAuthenticatedReconstruction,
        object owner
    ) {
        return (ownerIsFresh || ownerIsAuthenticatedReconstruction) &&
               typeof(IEnumerator).IsAssignableFrom(iteratorType) &&
               iteratorType.GetCustomAttribute<CompilerGeneratedAttribute>() != null &&
               iteratorType.DeclaringType != null &&
               IsCapturedCompilerThisOwner(iteratorType, owner?.GetType());
    }

    internal static bool IsCapturedCompilerThisOwner(Type stateMachineType, Type ownerType) {
        // A compiler-generated iterator or closure keeps the captured `this`
        // in <>4__this, declared as the type that owns the method. The only
        // value the CLR can ever store there is an instance of that type, so
        // the owner has to BE one - it does not have to be exactly it.
        // Requiring exact equality refuses every routine declared on a base
        // class and run by a subclass, which is most of what modded maps do:
        // Sprite.PlayUtil driven by a Sprite subclass, an NPC routine on a
        // custom NPC, a Booster routine on a custom booster. Reading the
        // field's own type rather than the nested type's DeclaringType also
        // keeps generic owners exact, because the field carries the
        // constructed type while DeclaringType carries the open one.
        FieldInfo capturedThis = stateMachineType?.GetField(
            "<>4__this",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return capturedThis != null &&
               ownerType != null &&
               capturedThis.FieldType.IsAssignableFrom(ownerType);
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

    // Does the map lay this EntityID out in its room? Capture asks it of the clean
    // reload it measures against and writes the answer onto the node; restore asks
    // it of the room it is rebuilding into. The two answers together say whether the
    // map changed under a saved entity, which is the only thing that tells a map
    // edit apart from session state deciding not to build an entity this time.
    //
    // Three answers, not two, and the third is the point. True is "the map places
    // this id", false is "the map does not place it", and null is "there is no map
    // data to ask" - no callback, no id worth asking about, or a map the callback
    // could not read. At capture the last two are the same thing and nothing is
    // stamped either way. At restore they are not: false is a refusal that tells the
    // player their map or their collab changed, and saying that because the map could
    // not be read would be a false story about their install. Only an explicit false
    // may refuse.
    //
    // The per-room answer is built once and reused, because a room's map data is a
    // flat list and a room with several hundred entities would otherwise be scanned
    // once per entity. A null entry caches "no map data for this room" so a second
    // ask does not repeat a read that already failed.
    private bool? IsMapPlacedEntityId(
        object roomRoot,
        EntityID entityId,
        Dictionary<string, HashSet<int>> placedIdsByRoom
    ) {
        if (getMapPlacedEntityIds == null || !HasStableSourceId(entityId)) {
            return null;
        }
        if (!placedIdsByRoom.TryGetValue(entityId.Level, out HashSet<int> placedIds)) {
            IEnumerable<int> placed = getMapPlacedEntityIds(roomRoot, entityId.Level);
            // An empty set is map data that places nothing, which an empty room is.
            // Only null is the absence of map data.
            placedIds = placed == null ? null : new HashSet<int>(placed);
            placedIdsByRoom[entityId.Level] = placedIds;
        }
        return placedIds?.Contains(entityId.ID);
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

    internal static bool IsDerivedCollectionVersionField(Type ownerType, string fieldName) {
        // BCL collections increment _version to invalidate enumerators. The
        // contents own gameplay state; this counter is only derived bookkeeping.
        return string.Equals(fieldName, "_version", StringComparison.Ordinal) &&
               string.Equals(ownerType?.Namespace, "System.Collections.Generic", StringComparison.Ordinal) &&
               typeof(IEnumerable).IsAssignableFrom(ownerType);
    }

    private sealed class CaptureContext {
        private readonly AkronReconstructionGraph owner;
        private readonly Dictionary<object, int> savedNodeIds = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<object, int> pairedFreshObjects = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, HashSet<FreshResource>> freshResources = new Dictionary<string, HashSet<FreshResource>>(StringComparer.Ordinal);
        private readonly Dictionary<Type, HashSet<FreshResource>> freshRoomObjects = new Dictionary<Type, HashSet<FreshResource>>();
        private readonly Dictionary<object, FreshResource> freshCandidates =
            new Dictionary<object, FreshResource>(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, HashSet<int>> mapPlacedEntityIdsByRoom =
            new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        // The clean reload this capture is measured against. Its map is the map the
        // slot was set on, so it is what decides whether an entity's id is one the
        // map owns.
        private readonly object freshBaselineRoot;

        public CaptureContext(AkronReconstructionGraph owner, object freshRoot) {
            this.owner = owner;
            freshBaselineRoot = freshRoot;
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

            // This index pass runs before a single document node exists and walks
            // the whole fresh room, so without its own stop point the worker would
            // spend the largest single burst of a job with the pacing gate shut
            // and never look at it. Once per object about to have its children
            // walked is the same granularity the capture walk uses.
            AkronSnapshotPacing.Pace();

            // One path list is pushed and popped across the whole walk instead of
            // copying the ancestor chain at every step, which was quadratic in
            // graph depth. The list is only ever read by GetFreshCandidate, which
            // deep-copies it before storing, so no candidate can alias it.
            if (value is Array array) {
                foreach (int[] indices in EnumerateArrayIndices(array)) {
                    path.Add(new AkronReconstructionPathStep {
                        Kind = "array",
                        ArrayIndices = indices.ToList()
                    });
                    IndexFreshValue(array.GetValue(indices), path, visited);
                    path.RemoveAt(path.Count - 1);
                }
                return;
            }

            foreach (FieldInfo field in GetInstanceFields(type)) {
                path.Add(new AkronReconstructionPathStep {
                    Kind = "field",
                    DeclaringTypeName = TypeName(field.DeclaringType),
                    FieldName = field.Name
                });
                IndexFreshValue(field.GetValue(value), path, visited);
                path.RemoveAt(path.Count - 1);
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

        private FreshResource FindFreshResource(Type resourceType, string key, string path) {
            if (!freshResources.TryGetValue(key, out HashSet<FreshResource> matches)) {
                object detachedResource = owner.resolveDetachedLiveResource?.Invoke(resourceType, key);
                if (detachedResource != null &&
                    detachedResource.GetType() == resourceType &&
                    string.Equals(key, ResourceKey(detachedResource), StringComparison.Ordinal)) {
                    return GetFreshCandidate(detachedResource, Array.Empty<AkronReconstructionPathStep>());
                }
                throw new AkronReconstructionException(path, "fresh resource key is unavailable: " + key);
            }
            if (matches.Count != 1) {
                if (owner.areEquivalentLiveResources?.Invoke(resourceType) == true) {
                    FreshResource equivalent = matches.FirstOrDefault(candidate =>
                        !pairedFreshObjects.ContainsKey(candidate.Value)) ?? matches.FirstOrDefault();
                    if (equivalent != null) {
                        return equivalent;
                    }
                }
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

        // No fresh path descends into this walk. A node only records a fresh path
        // when a saved object is re-paired with a fresh object it was not already
        // sitting next to, and both re-pairing sites below carry the matched
        // object's own recorded path. Threading the ancestor path down instead
        // copied the whole path at every field, which is quadratic in graph depth
        // and produced a value no node ever read.
        public AkronReconstructionValue CaptureValue(
            object savedValue,
            object freshValue,
            string path,
            Type containingType = null,
            string knownEventPath = null,
            AkronReconstructionNode parentNode = null,
            AkronReconstructionPathStep parentStep = null,
            int parentDelegateIndex = -1
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
                // The graph path is field names only, so it cannot say which type
                // holds the pointer, and that name is what identifies the mod a
                // refused pointer came from. Carry both: a refusal nobody can act
                // on costs the player the slot and tells them nothing.
                throw new AkronReconstructionException(
                    path,
                    "process pointer cannot be persisted" +
                    ";pointer-type=" + savedType.FullName +
                    ";owner-type=" + (containingType?.FullName ?? "unknown"));
            }
            if (savedNodeIds.TryGetValue(savedValue, out int existingNodeId)) {
                return new AkronReconstructionValue { Kind = ReferenceValueKind, NodeId = existingNodeId };
            }

            // Set only when this value is re-paired with a fresh object found
            // elsewhere in the fresh room, which is the only case a node needs a
            // fresh path for. Last writer wins, exactly as the previous local
            // reassignment of the descending path did.
            List<AkronReconstructionPathStep> matchedFreshPath = null;
            bool persistentEventInstance = savedValue is EventInstance;
            bool weakReference = IsWeakReferenceType(savedType);
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
                    matchedFreshPath = ClonePath(matchedRoomObject.Path);
                    freshTypeMatches = true;
                    entityIdentityMatches = true;
                } else if (!entityIdentityMatches) {
                    freshValue = null;
                    freshTypeMatches = false;
                }
            }
            bool additionalLiveAnchor = owner.isAdditionalLiveResource?.Invoke(savedValue) == true;
            bool liveAnchor = !persistentEventInstance &&
                              !persistentResource &&
                              (owner.isLiveResource(savedType) || additionalLiveAnchor);
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
                    FreshResource matchedResource = FindFreshResource(savedType, savedResourceKey, path);
                    freshValue = matchedResource.Value;
                    matchedFreshPath = ClonePath(matchedResource.Path);
                    freshTypeMatches = freshValue.GetType() == savedType;
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

            if (liveAnchor && freshTypeMatches &&
                owner.areEquivalentLiveResources?.Invoke(savedType) == true &&
                pairedFreshObjects.TryGetValue(freshValue, out int equivalentOwnerId)) {
                // Some rooms create fewer wrappers for an immutable asset than
                // the saved frame retained. The configured key contract makes
                // those wrappers interchangeable, so preserve every saved
                // reference by folding it onto the existing anchor node.
                savedNodeIds[savedValue] = equivalentOwnerId;
                return new AkronReconstructionValue {
                    Kind = ReferenceValueKind,
                    NodeId = equivalentOwnerId
                };
            }

            int nodeId = Document.Nodes.Count + 1;
            savedNodeIds[savedValue] = nodeId;
            // A weak reference is constructed on restore, never paired with the
            // fresh one, for the same reason a delegate is: its whole state is a
            // construction argument, not a field an existing object can take.
            bool useFreshObject = freshTypeMatches && !savedType.IsValueType &&
                                  savedValue is not Delegate && !persistentEventInstance && !weakReference;
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
                        : weakReference
                            ? WeakReferenceKind
                        : savedValue is Delegate
                            ? DelegateKind
                            : savedType.IsArray ? ArrayKind : ObjectKind,
                TypeName = TypeName(savedType),
                Path = path,
                ParentNodeId = parentNode?.Id ?? 0,
                ParentKind = parentStep?.Kind ?? (parentDelegateIndex >= 0 ? "delegate" : string.Empty),
                ParentDeclaringTypeName = parentStep?.DeclaringTypeName ?? string.Empty,
                ParentFieldName = parentStep?.FieldName ?? string.Empty,
                ParentArrayIndices = parentStep?.ArrayIndicesOrNull is { Count: > 0 } parentArrayIndices
                    ? new List<int>(parentArrayIndices)
                    : null,
                ParentDelegateIndex = parentDelegateIndex,
                UseFreshObject = liveAnchor || useFreshObject,
                ResourceKey = savedLiveResourceKey,
                // Both facts are read off the saved object, which is the only place
                // they exist. A restore holds the fresh room and the document and
                // neither of them can say what the saved key was derived from or
                // what the map looked like when the slot was set.
                PortableResourceKey = liveAnchor &&
                                      !string.IsNullOrWhiteSpace(savedLiveResourceKey) &&
                                      (additionalLiveAnchor ||
                                       owner.hasPortableLiveResourceKey?.Invoke(savedValue) == true),
                // == true, so "there was no map data to ask" stamps nothing rather than
                // stamping the answer a map that dropped the id would have given.
                MapPlacedEntity = savedValue is Entity mapEntity &&
                                  owner.IsMapPlacedEntityId(
                                      freshBaselineRoot,
                                      GetEntitySourceId(mapEntity),
                                      mapPlacedEntityIdsByRoom) == true,
                FreshPath = matchedFreshPath
            };
            Document.Nodes.Add(node);
            // The walk allocates in proportion to the nodes it produces, so this
            // is where the background worker stops while the player has control.
            AkronSnapshotPacing.Pace();

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
            } else if (weakReference) {
                CaptureWeakReference(node, savedValue, freshTypeMatches ? freshValue : null, path);
            } else if (savedValue is Delegate savedDelegate) {
                CaptureDelegate(node, savedDelegate, freshValue as Delegate, path, containingType);
            } else if (savedValue is Array savedArray) {
                CaptureArray(node, savedArray, freshValue as Array, path);
            } else {
                CaptureObject(node, savedValue, useFreshObject ? freshValue : null, path);
            }

            return new AkronReconstructionValue { Kind = ReferenceValueKind, NodeId = nodeId };
        }

        // The fresh weak reference's target is handed down as the target's fresh
        // counterpart, exactly as CaptureObject hands a fresh field value down,
        // so a target the fresh room also reaches weakly still pairs. A target
        // first reached through a weak edge and nowhere else records this edge
        // as its parent, which no fresh-slot authentication matches - the
        // restore then rebuilds it as a plain reconstructed object, and refuses
        // loudly if that object's type needs room authentication.
        private void CaptureWeakReference(
            AkronReconstructionNode node,
            object savedWeakReference,
            object freshWeakReference,
            string path
        ) {
            (object savedTarget, bool trackResurrection) = ReadWeakReference(savedWeakReference);
            object freshTarget = freshWeakReference == null ? null : ReadWeakReference(freshWeakReference).Target;
            string targetPath = path + ".<weak-target>";
            AkronReconstructionValue targetValue = CaptureValue(
                savedTarget,
                freshTarget,
                targetPath,
                savedWeakReference.GetType(),
                null,
                node,
                new AkronReconstructionPathStep { Kind = "weak-target" });
            // Weak-reference nodes are rebuilt in one ascending-id pass after every
            // other node, so a target that is itself a weak reference must have been
            // captured before this one to exist when this one is created. Capture
            // walks the target inline, giving it a higher id - or, for a weak
            // reference targeting itself, this very id - so either way the target
            // would not be in Objects yet and every load would refuse the slot.
            // Refuse it here instead, at the Set, so no such slot is ever written.
            // A target-first weak chain keeps its lower id and is left alone.
            if (targetValue.Kind == ReferenceValueKind && targetValue.NodeId >= node.Id &&
                string.Equals(Document.Nodes[targetValue.NodeId - 1].Kind, WeakReferenceKind, StringComparison.Ordinal)) {
                throw new AkronReconstructionException(
                    targetPath,
                    "a weak reference whose target is itself or a later weak reference cannot be persisted");
            }
            node.ItemsOrNull = new List<AkronReconstructionValue>(2) {
                targetValue,
                new AkronReconstructionValue {
                    Kind = ScalarValueKind,
                    TypeName = TypeName(typeof(bool)),
                    Scalar = EncodeScalar(trackResurrection, typeof(bool), path)
                }
            };
        }

        private void CaptureObject(
            AkronReconstructionNode node,
            object savedObject,
            object freshObject,
            string path
        ) {
            foreach (FieldInfo field in GetInstanceFields(savedObject.GetType())) {
                if (IsDerivedCollectionVersionField(savedObject.GetType(), field.Name)) {
                    continue;
                }
                string childPath = FieldPath(path, field.Name);
                AkronReconstructionPathStep pathStep = new AkronReconstructionPathStep {
                    Kind = "field",
                    DeclaringTypeName = TypeName(field.DeclaringType),
                    FieldName = field.Name
                };
                object freshFieldValue = freshObject == null ? null : field.GetValue(freshObject);
                string knownEventPath = AkronEventInstanceUtils.GetOwnerEventPath(savedObject, field.Name);
                (node.FieldsOrNull ??= new List<AkronReconstructionField>()).Add(new AkronReconstructionField {
                    DeclaringTypeName = TypeName(field.DeclaringType),
                    Name = field.Name,
                    Path = childPath,
                    Value = CaptureValue(
                        field.GetValue(savedObject),
                        freshFieldValue,
                        childPath,
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
            string path
        ) {
            node.ArrayLengthsOrNull = new List<int>(savedArray.Rank);
            node.ArrayLowerBoundsOrNull = new List<int>(savedArray.Rank);
            for (int dimension = 0; dimension < savedArray.Rank; dimension++) {
                node.ArrayLengthsOrNull.Add(savedArray.GetLength(dimension));
                node.ArrayLowerBoundsOrNull.Add(savedArray.GetLowerBound(dimension));
            }

            // A packed grid is one allocation of however many megabytes the map
            // needs, with no node boundary inside it to stop at. Check the gate
            // immediately before starting it rather than after, so the worker
            // never begins a multi-megabyte copy just as the player takes
            // control. This is the largest single allocation in the whole walk.
            AkronSnapshotPacing.Pace();

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
                (node.ItemsOrNull ??= new List<AkronReconstructionValue>()).Add(CaptureValue(
                    savedArray.GetValue(indices),
                    freshItem,
                    childPath,
                    parentNode: node,
                    parentStep: pathStep));
            }
        }

        private void CaptureDelegate(
            AkronReconstructionNode node,
            Delegate savedDelegate,
            Delegate freshDelegate,
            string path,
            Type containingType
        ) {
            Delegate[] savedCalls = savedDelegate.GetInvocationList();
            Delegate[] freshCalls = freshDelegate?.GetInvocationList() ?? Array.Empty<Delegate>();
            bool hasAnonymousRuntimeMethod = savedCalls.Any(call => call.Method.DeclaringType == null);
            if (hasAnonymousRuntimeMethod) {
                if (savedCalls.Length == 1 &&
                    TryDescribeDetourNext(savedCalls[0], containingType, out MethodInfo sourceMethod, out MethodInfo hookTarget)) {
                    (node.DelegateCallsOrNull ??= new List<AkronReconstructionDelegateCall>()).Add(new AkronReconstructionDelegateCall {
                        Kind = DetourNextDelegateCallKind,
                        Target = new AkronReconstructionValue { Kind = NullValueKind },
                        DeclaringTypeName = TypeName(sourceMethod.DeclaringType),
                        MethodName = sourceMethod.Name,
                        ReturnTypeName = TypeName(sourceMethod.ReturnType),
                        ParameterTypeNames = GetParameterTypeNames(sourceMethod),
                        HookTargetDeclaringTypeName = TypeName(hookTarget.DeclaringType),
                        HookTargetMethodName = hookTarget.Name,
                        HookTargetReturnTypeName = TypeName(hookTarget.ReturnType),
                        HookTargetParameterTypeNames = GetParameterTypeNames(hookTarget)
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
                (node.DelegateCallsOrNull ??= new List<AkronReconstructionDelegateCall>()).Add(new AkronReconstructionDelegateCall {
                    Kind = MethodDelegateCallKind,
                    Target = CaptureValue(
                        savedCall.Target,
                        freshCall?.Target,
                        path + ".<target>[" + index.ToString(CultureInfo.InvariantCulture) + "]",
                        parentNode: node,
                        parentDelegateIndex: index),
                    DeclaringTypeName = TypeName(method.DeclaringType),
                    MethodName = method.Name,
                    ReturnTypeName = TypeName(method.ReturnType),
                    ParameterTypeNames = GetParameterTypeNames(method)
                });
            }
        }

        private List<string> GetParameterTypeNames(MethodBase method) {
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 0) {
                return null;
            }
            List<string> names = new List<string>(parameters.Length);
            foreach (ParameterInfo parameter in parameters) {
                names.Add(TypeName(parameter.ParameterType));
            }
            return names;
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
                ArrayIndices = step.ArrayIndicesOrNull is { Count: > 0 }
                    ? new List<int>(step.ArrayIndicesOrNull)
                    : null
            }).ToList();
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
        private readonly Dictionary<string, object> detachedLiveResources =
            new Dictionary<string, object>(StringComparer.Ordinal);
        private readonly Dictionary<int, int?> entityListOwnerIds = new Dictionary<int, int?>();
        private readonly HashSet<int> indexedEntityListTypeOrdinals = new HashSet<int>();
        private readonly Dictionary<int, (int Ordinal, int Count)> entityListTypeOrdinals =
            new Dictionary<int, (int Ordinal, int Count)>();
        private readonly Dictionary<int, Dictionary<Type, List<Entity>>> freshEntityTypesByEntityList =
            new Dictionary<int, Dictionary<Type, List<Entity>>>();
        private readonly Dictionary<object, int> freshFieldAliasReservations =
            new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<int, object> freshFieldAliasesByNode = new Dictionary<int, object>();
        private readonly Dictionary<int, (int ParentId, string DeclaringTypeName, string FieldName)>
            freshFieldAliasSourcesByNode =
                new Dictionary<int, (int ParentId, string DeclaringTypeName, string FieldName)>();
        private readonly HashSet<string> freshStructuralTypes = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> freshListStructuralTypeCounts =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<int>> mapPlacedEntityIdsByRoom =
            new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        private readonly HashSet<string> freshStaticDelegateMethods = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> freshStructuralDelegateCalls = new HashSet<string>(StringComparer.Ordinal);
        // What each state slot of every fresh Monocle.StateMachine means, keyed
        // by the machine itself so two machines of one type in one room cannot
        // lend each other a state name. Name is what the machine calls the slot
        // and Driver is the code it runs there; see ValidateStateSlotAssignment
        // for which of the two decides a write and why.
        private readonly Dictionary<object, (string Name, string Driver)[]> freshStateSlots =
            new Dictionary<object, (string Name, string Driver)[]>(ReferenceEqualityComparer.Instance);
        private Dictionary<int, AkronReconstructionNode> savedStateSlotArrays;
        private readonly Dictionary<object, HashSet<string>> freshInstanceDelegateMethods =
            new Dictionary<object, HashSet<string>>(ReferenceEqualityComparer.Instance);
        private readonly HashSet<object> activeFreshSafeObjects = new HashSet<object>(ReferenceEqualityComparer.Instance);
        private readonly HashSet<int> resolvedFreshObjectNodes = new HashSet<int>();
        // One nesting level of the coroutine stack costs seven steps and the
        // deepest Celeste routine nests a handful of levels, so this bound only
        // ever stops a document whose parent links do not terminate.
        private const int MaxCoroutineStackWalkSteps = 64;

        private readonly HashSet<int> authenticatedRuntimeStateNodes = new HashSet<int>();
        private readonly HashSet<int> authenticatedRuntimeEntityNodes = new HashSet<int>();
        private readonly HashSet<int> authenticatedOwnedNestedStateNodes = new HashSet<int>();
        private readonly HashSet<int> authenticatedOwnedComponentNodes = new HashSet<int>();
        private readonly HashSet<int> authenticatedDelegateTargetNodes = new HashSet<int>();
        private readonly HashSet<int> authenticatedIteratorClosureNodes = new HashSet<int>();
        private readonly HashSet<int> authenticatedScreenWipeNodes = new HashSet<int>();
        private readonly HashSet<int> authenticatedFieldBuiltComponentNodes = new HashSet<int>();
        // Iterators whose runtime-state membership is provisional until
        // VerifyDeferredIteratorStates confirms or withdraws it. Node licences
        // must not build on these; see authenticatedDirectIteratorClosure.
        private readonly HashSet<int> deferredProvisionalIteratorIds = new HashSet<int>();
        private readonly HashSet<int> authenticatedCoroutinePlumbingNodes = new HashSet<int>();
        // Iterator nodes whose captured owner had not resolved yet, with what the
        // rest of CreateAuthenticatedObject was able to prove about each one
        // without that owner. VerifyDeferredIteratorStates needs both.
        private readonly List<(AkronReconstructionNode Node, bool AuthenticWithoutTheOwnerProof)>
            deferredIteratorStateNodes =
                new List<(AkronReconstructionNode Node, bool AuthenticWithoutTheOwnerProof)>();
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
                // Before any resolver or authenticator sees this node. See the method.
                RefuseMapEntityTheMapNoLongerPlaces(node, type);
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
                } else if (node.Kind == DelegateKind || node.Kind == EventInstanceKind ||
                           node.Kind == WeakReferenceKind) {
                    continue;
                } else if (node.Kind == ArrayKind) {
                    restoredObject = CreateAuthenticatedObject(node, type);
                } else {
                    restoredObject = CreateAuthenticatedObject(node, type);
                }

                bool equivalentAnchorAlias = node.Kind == AnchorKind &&
                                             owner.areEquivalentLiveResources?.Invoke(type) == true;
                if (!type.IsValueType && restoredObject != null &&
                    freshOwners.TryGetValue(restoredObject, out int ownerId) &&
                    !equivalentAnchorAlias) {
                    throw new AkronReconstructionException(node.Path, "fresh object is already paired with node " + ownerId.ToString(CultureInfo.InvariantCulture));
                }
                if (!type.IsValueType && restoredObject != null && !freshOwners.ContainsKey(restoredObject)) {
                    freshOwners[restoredObject] = node.Id;
                }
                Objects[node.Id] = restoredObject;
            }

            VerifyDeferredIteratorStates();
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
            // After delegates and events so a target that is one of those already
            // exists. A weak reference whose target is another weak reference
            // would need yet another ordering and has never been seen; its target
            // reference resolves to nothing here and the restore refuses it.
            foreach (AkronReconstructionNode node in document.Nodes.Where(node => node.Kind == WeakReferenceKind).OrderBy(node => node.Id)) {
                Objects[node.Id] = CreateWeakReference(node);
            }
        }

        // Mirrors ReadWeakReference: the non-generic type takes its target and
        // flag through the constructor, and WeakReference<T> takes the target
        // alone because its flag is not readable off a live instance either.
        private object CreateWeakReference(AkronReconstructionNode node) {
            Type type = ResolveType(node.TypeName, node.Path);
            object target = ResolveValue(node.ItemsOrNull[0], node.Path + ".<weak-target>");
            bool trackResurrection = DecodeScalar(node.ItemsOrNull[1], node.Path) is true;
            if (type == typeof(WeakReference)) {
                return new WeakReference(target, trackResurrection);
            }
            if (target != null && !type.GetGenericArguments()[0].IsInstanceOfType(target)) {
                throw new AkronReconstructionException(node.Path, "weak reference target type differs");
            }
            return Activator.CreateInstance(type, new[] { target });
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
                // A structural call key states that the fresh room runs this
                // callback at this path - as loosely as StructuralDelegateCallKey
                // reads a path, which already wildcards every array index - so it
                // has to be recorded at every path the delegate is reachable
                // from. Recording it at one path only makes the index disagree
                // with the room depending on walk order. Ordinary objects below
                // already work the other way: they record their structural type
                // on every visit and only skip walking their fields again. A
                // delegate used to skip the whole visit instead, so a callback
                // object a room holds in two slots - which is what a cached
                // non-capturing lambda or one shared handler is - left the
                // second slot with no record, and a saved document whose own
                // path was that second slot was refused for a callback the
                // fresh room does have there. Object identity still ends the
                // walk: the target below is indexed once.
                bool firstDelegateVisit = visited.Add(value);
                foreach (Delegate call in freshDelegate.GetInvocationList()) {
                    if (call.Target != null) {
                        freshStructuralDelegateCalls.Add(
                            StructuralDelegateCallKey(path, call.Target.GetType(), call.Method));
                    }
                    if (!firstDelegateVisit) {
                        continue;
                    }
                    string methodKey = DelegateMethodKey(call.Method);
                    if (call.Target == null) {
                        freshStaticDelegateMethods.Add(methodKey);
                        continue;
                    }
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

            // Record what this machine's state slots mean. It is a fact about
            // the machine rather than about a path, so once is enough however
            // many places the room reaches it from.
            if (value is StateMachine freshStateMachine) {
                IndexFreshStateSlots(freshStateMachine);
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

        // One entry per state slot the fresh machine has, holding what that slot
        // is called and what it runs. Read here rather than off the machine
        // later, because names and the callback arrays are both fields the
        // document restores: by the time a callback array is written, reading the
        // machine would read the document's own answer back.
        private void IndexFreshStateSlots(StateMachine machine) {
            string[] names = StateMachineNamesField?.GetValue(machine) as string[];
            int slots = 0;
            foreach (FieldInfo callbackField in StateMachineCallbackFields) {
                if (callbackField?.GetValue(machine) is Array callbacks && callbacks.Length > slots) {
                    slots = callbacks.Length;
                }
            }
            (string Name, string Driver)[] slotCoordinates = new (string Name, string Driver)[slots];
            for (int slot = 0; slot < slots; slot++) {
                // Monocle keeps names no longer than the callback arrays, and
                // GetStateName finds no name for a slot past its end - it
                // returns the state id as a string instead - so record that
                // slot as unnamed rather than leaving it unrecorded. Recording
                // null rather than the id keeps a state a mod happened to name
                // "2" from matching an unnamed slot 2 on the other side.
                slotCoordinates[slot] = (
                    names != null && slot < names.Length ? names[slot] : null,
                    FreshStateSlotDriver(machine, slot));
            }
            freshStateSlots[machine] = slotCoordinates;
        }

        // The code the fresh machine runs at one slot, written the way
        // SavedStateSlotDriver writes it from the document so the two strings are
        // comparable. A delegate's invocation list is spelled out because a mod
        // can add to a callback rather than replace it, and one call is the
        // ordinary case.
        private static string FreshStateSlotDriver(StateMachine machine, int slot) {
            StringBuilder driver = new StringBuilder();
            // Walked by name rather than by FieldInfo, the same way SavedStateSlotDriver
            // walks it, because the two strings are compared: a field Monocle no longer
            // has must leave the same empty "<name>=;" on both sides rather than an NRE
            // on one of them. StateMachineDriverFields is built from these names in this
            // order, so the index addresses the same field the name does, and a lookup
            // that found nothing is a null entry there.
            for (int index = 0; index < StateMachineDriverFieldNames.Length; index++) {
                driver.Append(StateMachineDriverFieldNames[index]).Append('=');
                if (StateMachineDriverFields[index]?.GetValue(machine) is Array callbacks &&
                    slot < callbacks.Length &&
                    callbacks.GetValue(slot) is Delegate callback) {
                    foreach (Delegate call in callback.GetInvocationList()) {
                        driver.Append(TypeName(call.Method.DeclaringType))
                            .Append('.')
                            .Append(call.Method.Name)
                            .Append('+');
                    }
                }
                driver.Append(';');
            }
            return driver.ToString();
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
            // A second visit still has a path-keyed fact to record: an ordinary
            // object its structural type here, a delegate its structural call
            // key here. Neither walks its fields again. A live resource is
            // identified by its own key rather than by a path, so revisiting
            // one would add nothing.
            return value is Delegate || !owner.isLiveResource(type);
        }

        private object CreateAuthenticatedObject(AkronReconstructionNode node, Type type) {
            // A compiler iterator proves itself through its captured <>4__this
            // owner, and nodes resolve in document order. An iterator whose owner
            // is reached later in the document - the owner's own first path runs
            // through this iterator, or the owner is a component of an entity
            // further down the room - cannot be asked that question yet, so it
            // carries the question to the end of the run instead of being refused
            // for a reason that says nothing about the saved state.
            //
            // Two independent proofs, and keeping them apart is what this method
            // is careful about. The owner proof is the identity-bearing one: it
            // says this iterator is the routine of an object the fresh room
            // supplied, and it is the only thing that may put a node in
            // authenticatedRuntimeStateNodes, because every rule that reads that
            // set rests on it and on nothing else - the coroutine-stack alias,
            // the iterator-owned-component alias, the <>4__this owner edge, the
            // closure-local rule below, and reconstructedSafeParentEdge on either
            // side of an edge. The structural proof below is the ordinary licence
            // every reconstruction gets - the fresh room supplies an object of
            // this type at this path - and it authenticates the object without
            // saying whose routine it is.
            //
            // So a deferred node is still asked the structural question here, and
            // its answer is carried to VerifyDeferredIteratorStates. Skipping it
            // was a measured regression: a coroutine that has never updated holds
            // its iterator exactly once, because Monocle.Coroutine stores its
            // constructor argument raw and only Coroutine.Update wraps it in
            // Everest's Flattened, so one unspent fresh occurrence admits the
            // whole room and the deferral was throwing that proof away. Measured:
            // three instances of one entity doing
            // `Add(new Coroutine(tween.Wait())); Add(tween);`, two finished during
            // play and one added during play, loaded on 010f660 and was refused
            // when the deferral discarded the structural proof. See
            // ARawCoroutineIteratorLoadsOnItsOwnStructuralEvidence.
            // An OWNERLESS frame stored in a coroutine stack proves itself by
            // position. A mod can wrap a routine in a static hook iterator
            // (FemtoHelper's dash hook), whose compiled type declares no
            // <>4__this, so the owner question is unaskable rather than
            // failed; the canonical chain being pure stack plumbing says the
            // frame is the coroutine's own saved state, and its hoisted fields
            // still validate one by one. Ownerlessness is read off the TYPE,
            // never the document: a crafted file omitting the field for a type
            // that declares it keeps the owner question - a Tween's Wait whose
            // Tween is not authentic stays refused whatever stack it sits in.
            bool provedIteratorState = IsAuthenticatedCompilerIteratorState(node, type) ||
                                       (IsCompilerGeneratedIterator(type) &&
                                        type.GetField(
                                            "<>4__this",
                                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null &&
                                        TryGetCoroutineEnumeratorStackOwner(node, CoroutineStackWalk.IncludingYieldedValues, out _));
            bool deferredIteratorState =
                !provedIteratorState && HasUnresolvedCompilerIteratorOwner(node, type);
            bool authenticatedRuntimeEntity =
                IsAuthenticatedBuiltInRuntimeEntity(node, type) ||
                IsAuthenticatedGeneratedRuntimeEntity(node, type);
            bool authenticatedOwnedNestedState =
                IsAuthenticatedFreshEntityOwnedNestedState(node, type) ||
                IsAuthenticatedFreshRendererOwnedRuntimeState(node, type) ||
                IsAuthenticatedRuntimeEntityOwnedState(node, type) ||
                IsAuthenticatedGeneratedEntityOwnedState(node, type);
            bool authenticatedOwnedComponent = IsAuthenticatedReconstructedOwnedComponent(node, type);
            bool authenticatedFieldBuiltComponent = IsAuthenticatedLazilyBuiltFieldComponent(node, type);
            bool authenticatedDelegateTarget = IsStructurallyAuthenticDelegateTarget(node, type);
            bool authenticatedIteratorClosure = IsAuthenticatedIteratorClosure(node, type);
            bool authenticatedScreenWipe = IsAuthenticatedBuiltInScreenWipe(node, type);
            // Everest wraps yielded enumerators in SwapImmediately, so a saved
            // mid-flight frame carries one as its current value while an idle
            // fresh routine has none. It is Everest's own passive plumbing,
            // proved the same way a frame is: by its position inside a
            // coroutine's stack, yielded values included.
            bool authenticatedCoroutinePlumbing =
                type == typeof(SwapImmediately) &&
                TryGetCoroutineEnumeratorStackOwner(node, CoroutineStackWalk.IncludingYieldedValues, out _);
            // A deferred node's membership is provisional: the owner proof is the
            // only thing that grants it and that proof is not in yet, so
            // VerifyDeferredIteratorStates confirms it or withdraws it before
            // ValidateReferenceAuthenticity, where every reader but one sits. The
            // exception is the reader inside this same loop,
            // IsAuthenticatedIteratorClosure, asked of the compiler closure this
            // iterator hoisted - a <>8__ field of it, so a node reached later in
            // the same document. Granting the membership here rather than at the
            // verify keeps that verdict what it is today, and it cannot carry the
            // withdrawal's weight: what closure-local membership licenses is one
            // <>4__this edge whose edge parent is a reconstruction and never a
            // fresh node, so it cannot reach the displacement guard.
            if (provedIteratorState || deferredIteratorState) {
                authenticatedRuntimeStateNodes.Add(node.Id);
            }
            if (deferredIteratorState) {
                deferredProvisionalIteratorIds.Add(node.Id);
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
            if (authenticatedFieldBuiltComponent) {
                authenticatedFieldBuiltComponentNodes.Add(node.Id);
            }
            if (authenticatedCoroutinePlumbing) {
                authenticatedCoroutinePlumbingNodes.Add(node.Id);
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
            // A hoisted closure of an owner-proved iterator. The fresh room's
            // silence about it is expected rather than suspicious: a routine
            // that was idle or finished in the clean load hoisted no closure,
            // so a mid-flight CrushBlock attack was refused over exactly this.
            // The licence rests only on a DIRECT owner proof: a provisionally
            // admitted (deferred) iterator can still be withdrawn by
            // VerifyDeferredIteratorStates, and a node licence granted on it
            // would outlive its foundation, so a closure under a deferred
            // iterator keeps the structural question below instead.
            bool authenticatedDirectIteratorClosure =
                authenticatedIteratorClosure &&
                !deferredProvisionalIteratorIds.Contains(node.ParentNodeId);
            // Everything that authenticates this reconstruction without appealing
            // to a compiler iterator's owner. A deferred node computes it too and
            // hands it to VerifyDeferredIteratorStates, which is why the throw is
            // guarded rather than the whole block.
            bool authenticWithoutTheOwnerProof =
                IsExplicitlySafeReconstructionType(type) ||
                authenticatedRuntimeEntity ||
                authenticatedOwnedNestedState ||
                authenticatedOwnedComponent ||
                authenticatedFieldBuiltComponent ||
                authenticatedDirectIteratorClosure ||
                authenticatedCoroutinePlumbing ||
                authenticatedScreenWipe;
            if (!authenticWithoutTheOwnerProof && !provedIteratorState) {
                List<AkronReconstructionPathStep> structuralPath = GetDocumentStructuralPath(node);
                string typePathKey = StructuralResourcePathKey(type, structuralPath);
                string listTypePathKey = StructuralResourcePathKey(
                    type,
                    structuralPath,
                    wildcardListStorageIndices: true);
                bool listTypeIsAvailable = HasListStorageIndex(structuralPath) &&
                                           freshListStructuralTypeCounts.ContainsKey(listTypePathKey);
                bool exactTypeIsAvailable = freshStructuralTypes.Contains(typePathKey);
                // IsAuthenticatedByExactParentSlot stays behind the path evidence,
                // the way the negated form it replaces left it: it resolves the
                // parent field and throws when the build no longer declares it, so
                // asking it when the path already answers would turn a loadable
                // room into a refusal about a field nothing needed.
                //
                // And a deferred node does not ask it at all. That throw is exactly
                // the refusal the deferral exists to avoid - a mod update that drops
                // the field an iterator was held in would answer "field is
                // unavailable", with no type on it for the report to name the mod
                // with, in place of the owner refusal that says what is actually
                // wrong with the document. The owner question is the better question
                // for this node and it is about to be asked. Nothing is lost by not
                // asking: for an array parent this predicate reads the same fresh
                // slot the exact path key already covers, and for a field parent it
                // wants a field declared as the iterator's own compiler-generated
                // type, which no source can write.
                authenticWithoutTheOwnerProof =
                    (structuralPath.Count > 0 && (exactTypeIsAvailable || listTypeIsAvailable)) ||
                    (!deferredIteratorState && IsAuthenticatedByExactParentSlot(node, type)) ||
                    authenticatedDelegateTarget;
                if (!authenticWithoutTheOwnerProof && !deferredIteratorState) {
                    throw new AkronReconstructionException(
                        node.Path,
                        "reconstructed type is not authentic to the fresh room;type=" + type.FullName +
                        ";path-depth=" + structuralPath.Count.ToString(CultureInfo.InvariantCulture) +
                        ";list-path=" + HasListStorageIndex(structuralPath).ToString().ToLowerInvariant() +
                        ";exact-match=" + exactTypeIsAvailable.ToString().ToLowerInvariant() +
                        ";list-match=" + listTypeIsAvailable.ToString().ToLowerInvariant(),
                        node.TypeName);
                }
            }
            if (deferredIteratorState) {
                deferredIteratorStateNodes.Add((node, authenticWithoutTheOwnerProof));
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

        // A saved entity whose EntityID the map no longer lays out in the room being
        // rebuilt. Two different things stop the reloaded room from containing a saved
        // entity's id, and only one of them is safe to rebuild through:
        //
        //   session state. The map still lays this id out; this run's flags meant
        //   LoadLevel skipped it. The saved frame is the truth, the entity is rebuilt
        //   beside the ones the room did build, and that restore works.
        //
        //   a changed map. The id is gone from the map, so the room the document was
        //   measured against no longer exists. Every same-typed entity in the room is
        //   a different map entity, and rebuilding the saved one hands it a live
        //   entity's list slot, its components and its saved state while the entity
        //   LoadLevel built is dropped - reported as success, because the only thing
        //   consulted is that the fresh room holds SOME object of this type at this
        //   wildcarded list path.
        //
        // The map is what tells them apart, and one bit per node is all it takes: an
        // id the map owned when the slot was set, gone from the map now, is a changed
        // map. An id the map never owned - one a mod made up for an entity it spawns
        // itself - carries no evidence either way and is left alone, which is why the
        // node records what capture saw rather than the restore asking the map alone.
        //
        // Only ids belonging to document.Room count, and that is the whole jurisdiction
        // of the rule. An entity the player is carrying keeps the SourceId the room it
        // was picked up in gave it - Leader.GainFollower leaves Tags.Persistent on a
        // strawberry and Level.TransitionRoutine carries persistent entities across, so
        // a berry picked up in a01 is still a01:5 while the player stands in a40 - and
        // UnloadLevel keeps only Tags.Global, so that node never pairs and always
        // reaches here. An edit to a01 says nothing about whether rebuilding it would
        // displace one of a40's entities, which is the harm above, and a golden-berry
        // run would otherwise make every slot in the chapter depend on the first room's
        // ids. document.Room is the room TryLoadFreshRoom loaded, so it is exactly the
        // population being rebuilt.
        //
        // Called once per node before anything resolves it, and deliberately: the
        // resolvers decide whether an authenticator ever runs, and none of them is
        // entitled to overrule an identity the map itself has dropped.
        // TryResolveFreshFieldAlias in particular has no SourceId check, so a saved map
        // entity held in an ordinary field outside entity or component list storage
        // used to bind to whatever the fresh room kept in that field - measured: the
        // saved state of entity 10 landing on the entity the edited map calls 99, with
        // that entity's SourceId overwritten to 10 and the load reporting success.
        //
        // Running first also covers a saved map entity that would have paired, and that
        // is the right answer rather than an accident of placement. Everest derives an
        // EntityID from map data for everything LoadLevel builds and for everything
        // EntityList.Add sees during a map entity's construction, so a reloaded room
        // carrying a stable SourceId the current map does not place got it from mod code
        // setting the field itself - and the document is still measured against a room
        // this map no longer produces.
        //
        // The last clause is "!= false" rather than a plain truth test, and that is the
        // whole of what keeps this rule from lying. Its message says the map changed, so
        // it may only fire on evidence that the map changed: a map this process could
        // not read answers null, which is no evidence at all, and refusing on it would
        // tell a player their collab was updated because a map reload happened to be in
        // flight. Null falls through here exactly as a placed id does.
        private void RefuseMapEntityTheMapNoLongerPlaces(AkronReconstructionNode node, Type type) {
            if (!node.MapPlacedEntity || !typeof(Entity).IsAssignableFrom(type) ||
                !TryGetSavedEntityId(node, out EntityID savedEntityId) ||
                !string.Equals(savedEntityId.Level, document.Room, StringComparison.Ordinal) ||
                owner.IsMapPlacedEntityId(freshRoot, savedEntityId, mapPlacedEntityIdsByRoom) != false) {
                return;
            }
            // ChangedMap, not the default: the only thing this rule has proved is that the
            // map dropped an id it used to own. Which mod ships the entity's type is not
            // evidence of anything here, so the message must not be built from it - a
            // vanilla entity edited out of a collab room would otherwise be attributed to
            // Celeste and reported to the player as an Akron bug.
            throw new AkronReconstructionException(
                node.Path,
                "saved map entity is no longer placed by this map;type=" + type.FullName +
                ";saved-entity-id=" + (savedEntityId.Level ?? string.Empty) + ":" +
                savedEntityId.ID.ToString(CultureInfo.InvariantCulture),
                node.TypeName,
                AkronReconstructionRefusalKind.ChangedMap);
        }

        private bool IsAuthenticatedCompilerIteratorState(AkronReconstructionNode node, Type type) {
            AkronReconstructionValue ownerReference = FindReferenceField(node, "<>4__this");
            // An owned-nested-state owner counts the same as a runtime entity:
            // LightningRenderer's Bolt runs its own Run() coroutine, so the
            // iterator's captured `this` is a nested object the owned-nested-
            // state licence already proved, not an entity or component.
            return ownerReference != null &&
                   Objects.TryGetValue(ownerReference.NodeId, out object ownerObject) &&
                   AkronReconstructionGraph.IsAuthenticatedCompilerIteratorOwner(
                       type,
                       resolvedFreshObjectNodes.Contains(ownerReference.NodeId),
                       authenticatedRuntimeEntityNodes.Contains(ownerReference.NodeId) ||
                       authenticatedOwnedNestedStateNodes.Contains(ownerReference.NodeId),
                       ownerObject);
        }

        private bool HasUnresolvedCompilerIteratorOwner(AkronReconstructionNode node, Type type) {
            AkronReconstructionValue ownerReference = FindReferenceField(node, "<>4__this");
            return ownerReference != null &&
                   nodes.ContainsKey(ownerReference.NodeId) &&
                   !Objects.ContainsKey(ownerReference.NodeId) &&
                   typeof(IEnumerator).IsAssignableFrom(type) &&
                   type.GetCustomAttribute<CompilerGeneratedAttribute>() != null &&
                   type.DeclaringType != null;
        }

        // The deferred owner question, asked once every node is resolved. Runs
        // before ValidateReferenceAuthenticity, so every rule that reads
        // authenticatedRuntimeStateNodes there sees the settled answer.
        private void VerifyDeferredIteratorStates() {
            foreach ((AkronReconstructionNode node, bool authenticWithoutTheOwnerProof)
                     in deferredIteratorStateNodes) {
                Type type = ResolveType(node.TypeName, node.Path);
                if (IsAuthenticatedCompilerIteratorState(node, type)) {
                    // Confirmed is as good as direct for everything that runs
                    // after this pass: the withdrawal risk the provisional
                    // marker guards against is gone, so the closure edge and
                    // delegate licences treat this iterator like one proved on
                    // first sight. The closure NODE licence stays direct-only
                    // because it was already decided before this pass ran.
                    deferredProvisionalIteratorIds.Remove(node.Id);
                    continue;
                }
                if (!authenticWithoutTheOwnerProof) {
                    throw new AkronReconstructionException(
                        node.Path,
                        "reconstructed compiler iterator owner is not authentic to the fresh room;type=" +
                        type.FullName,
                        node.TypeName);
                }
                // The owner is not authentic and something else admits the object
                // anyway, which is the licence every other reconstruction in the
                // room is holding. So the object stands and the iterator licence
                // goes: withdraw the provisional membership, and every reference to
                // this node has to earn its own edge the way an ordinary
                // reconstruction's references do.
                //
                // For a compiler iterator that something else is always the
                // structural evidence. The owned-nested-state and owned-component
                // licences cannot reach one: both reject IDisposable types, and
                // every C# iterator state machine implements IDisposable - measured
                // against the real assemblies, Monocle.Tween+<Wait>d__45 included.
                //
                // Keeping the membership here was measured and it opens a wrong
                // restore. IsAuthenticatedCoroutineStackIteratorAlias would license
                // the node's Flattened.current alias, that becomes exactOwnerEdge,
                // and an exactOwnerEdge is exempt from the displacement question -
                // ValidateReferenceEdge returns before asking it - so the
                // reconstruction is written over a live iterator the document still
                // keeps through another field, and the restore reports success. w50
                // and w51 have that room and its measurement on both builds; the room
                // in the suite that fails without this line is
                // ADeferredCompilerIteratorWhoseOwnerIsOnlyAnOwnedComponentIsRefusedBesideThatSibling,
                // which loads on the membership alone.
                authenticatedRuntimeStateNodes.Remove(node.Id);
            }
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

        // Everest wraps every coroutine frame in SwapImmediatelyExtension's
        // Flattened, whose MoveNext copies the value the inner iterator yielded
        // into its own `current` field, and Monocle.Coroutine then pushes that
        // value onto its own stack and wraps it in a second Flattened. So the
        // stock Celeste idiom `yield return sprite.PlayRoutine("anim")` inside a
        // StateMachine leaves one mid-flight iterator held three times inside a
        // single Coroutine: the yielding routine's <>2__current, the
        // Flattened.current it was copied into, and the inner Flattened's own
        // stack. The document canonicalises one of the three and the other two
        // arrive here as alias edges with nothing else to prove them.
        //
        // This is not a fresh-room claim and the fresh room's occurrence counts
        // say nothing about it: the iterator does not exist in a clean load at
        // all. It is a reconstructed object that already proved itself through
        // its captured <>4__this owner, and it is already reachable from this
        // Coroutine through its own document path. A second reference from
        // elsewhere in the same Coroutine's stack therefore admits no object,
        // reaches nothing new, and consumes no fresh occurrence. Confining the
        // alias to that one Coroutine is what stops an authenticated iterator
        // being planted into an unrelated component's stack.
        private bool IsAuthenticatedCoroutineStackIteratorAlias(
            AkronReconstructionNode target,
            AkronReconstructionNode edgeParent,
            Type edgeParentType,
            AkronReconstructionField edgeField,
            bool savedOwnerEdge
        ) {
            // Second references only. The document's own first-owner path to
            // the iterator has to carry its own proof; if this rule could admit
            // that path too, "already reachable through its canonical path"
            // would stop being true and the rule would be proving itself.
            if (savedOwnerEdge ||
                !authenticatedRuntimeStateNodes.Contains(target.Id) ||
                !IsCoroutineStackIteratorSlot(edgeParent, edgeParentType, edgeField)) {
                return false;
            }

            return TryGetCoroutineEnumeratorStackOwner(
                       edgeParent,
                       CoroutineStackWalk.StorageOnly,
                       out int aliasCoroutineId) &&
                   TryGetCoroutineEnumeratorStackOwner(
                       target,
                       CoroutineStackWalk.IncludingYieldedValues,
                       out int ownerCoroutineId) &&
                   aliasCoroutineId == ownerCoroutineId;
        }

        // The three places a coroutine can hold a frame it is running, and the
        // only edges this rule will authenticate. Being canonically inside the
        // stack is not enough on its own: without this, any reference field on
        // a frame that happens to sit in the stack would qualify, including a
        // hoisted local the compiler named <spare>5__2, which is a value the
        // saved room can set to anything.
        private static bool IsCoroutineStackIteratorSlot(
            AkronReconstructionNode edgeParent,
            Type edgeParentType,
            AkronReconstructionField edgeField
        ) {
            if (edgeField == null) {
                return edgeParent.Kind == ArrayKind && edgeParentType == typeof(IEnumerator[]);
            }
            return edgeField.Name switch {
                "current" => edgeParentType == typeof(SwapImmediatelyExtension.Flattened),
                "<>2__current" => IsCompilerGeneratedIterator(edgeParentType),
                _ => false
            };
        }

        // Walk a node's document parents up to a Coroutine's own `enumerators`
        // stack, crossing only the stack's plumbing. Every step names both the
        // edge and the type it lands on, so a reference parked in some other
        // field of a frame cannot be walked back to the Coroutine and passed
        // off as part of its stack.
        private bool TryGetCoroutineEnumeratorStackOwner(
            AkronReconstructionNode node,
            CoroutineStackWalk walk,
            out int coroutineNodeId
        ) {
            coroutineNodeId = 0;
            AkronReconstructionNode current = node;
            // Restore can be handed an in-memory document that never went
            // through the deserializer's parent-cycle check, so the walk is
            // bounded rather than trusting the links to terminate.
            for (int step = 0; step < MaxCoroutineStackWalkSteps; step++) {
                if (!nodes.TryGetValue(current.ParentNodeId, out AkronReconstructionNode parent)) {
                    return false;
                }
                Type parentType = ResolveType(parent.TypeName, parent.Path);
                if (IsCoroutineEnumeratorStackField(current, parentType)) {
                    coroutineNodeId = parent.Id;
                    return true;
                }
                if (!IsCoroutineEnumeratorStackStep(current, parentType, walk)) {
                    return false;
                }
                current = parent;
            }
            return false;
        }

        // The two walks are not the same question, and the difference is what
        // keeps the rule honest.
        //
        // The container an alias lives in has to be the stack's own storage, so
        // that walk never crosses a value a frame yielded: a routine is free to
        // `yield return new IEnumerator[] { ... }`, and the contents of that
        // array are the routine's own data, not slots of the coroutine's stack.
        // Treating them as slots would let any array a frame yielded hold an
        // authenticated iterator.
        //
        // The walk that establishes which coroutine owns the iterator does
        // cross them, because the iterator being aliased legitimately IS the
        // yielded value of one of those frames - that is the whole shape.
        private enum CoroutineStackWalk {
            StorageOnly,
            IncludingYieldedValues
        }

        // The terminal step: Monocle.Coroutine's own private stack. Coroutine
        // is not sealed, so a subclass declaring its own `enumerators` field
        // would otherwise be read as Monocle's stack. The declaring type
        // recorded on the edge is what decides, not the field name.
        private bool IsCoroutineEnumeratorStackField(AkronReconstructionNode node, Type parentType) {
            if (node.ParentKind != "field" ||
                node.ParentFieldName != "enumerators" ||
                !typeof(Coroutine).IsAssignableFrom(parentType)) {
                return false;
            }
            return ResolveType(node.ParentDeclaringTypeName, node.Path) == typeof(Coroutine);
        }

        // One step up the stack's plumbing: a slot in an IEnumerator[], that
        // array as a Stack's backing store, and a Flattened's own inner stack.
        // The last two shapes are the yielded values, and only the owning walk
        // may cross those - see CoroutineStackWalk.
        private static bool IsCoroutineEnumeratorStackStep(
            AkronReconstructionNode node,
            Type parentType,
            CoroutineStackWalk walk
        ) {
            if (node.ParentKind == "array") {
                return parentType == typeof(IEnumerator[]);
            }
            if (node.ParentKind != "field") {
                return false;
            }
            bool yieldedValuesAllowed = walk == CoroutineStackWalk.IncludingYieldedValues;
            return node.ParentFieldName switch {
                "_array" => parentType == typeof(Stack<IEnumerator>),
                "enums" => parentType == typeof(SwapImmediatelyExtension.Flattened),
                "current" => yieldedValuesAllowed &&
                             parentType == typeof(SwapImmediatelyExtension.Flattened),
                "<>2__current" => yieldedValuesAllowed && IsCompilerGeneratedIterator(parentType),
                // The enumerator a SwapImmediately wrapper carries; mods chain
                // dash-coroutine hooks through exactly this.
                "Inner" => yieldedValuesAllowed && parentType == typeof(SwapImmediately),
                _ => false
            };
        }

        // IsDefined rather than GetCustomAttribute: this runs on a per-edge
        // path and materialising the attribute instance to throw it away
        // allocates for nothing.
        private static bool IsCompilerGeneratedIterator(Type type) {
            return typeof(IEnumerator).IsAssignableFrom(type) &&
                   type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) &&
                   type.DeclaringType != null;
        }

        private bool IsAuthenticatedIteratorOwnedComponentAlias(
            AkronReconstructionNode target,
            AkronReconstructionNode edgeParent
        ) {
            if (!authenticatedRuntimeStateNodes.Contains(target.Id)) {
                return false;
            }
            AkronReconstructionValue iteratorOwner = FindReferenceField(target, "<>4__this");
            if (iteratorOwner == null) {
                return false;
            }

            // Coroutine composition can keep the same compiler iterator in
            // more than one nested stack. Authenticate those aliases only when
            // the containing Component belongs to the iterator's exact owner
            // entity, so an unrelated collection cannot adopt the iterator.
            AkronReconstructionNode current = edgeParent;
            while (current != null) {
                Type currentType = ResolveType(current.TypeName, current.Path);
                if (typeof(Component).IsAssignableFrom(currentType)) {
                    bool componentIsAuthenticated = resolvedFreshObjectNodes.Contains(current.Id) ||
                                                    authenticatedOwnedComponentNodes.Contains(current.Id) ||
                                                    IsAuthenticatedReconstructedOwnedComponent(current, currentType);
                    return componentIsAuthenticated &&
                           TryGetComponentOwnerNodes(current, out _, out int componentOwnerId) &&
                           componentOwnerId == iteratorOwner.NodeId;
                }
                if (typeof(Entity).IsAssignableFrom(currentType) ||
                    !nodes.TryGetValue(current.ParentNodeId, out current)) {
                    return false;
                }
            }
            return false;
        }

        private bool IsAuthenticatedByExactParentSlot(AkronReconstructionNode node, Type type) {
            // Generic array and field slots cannot authenticate gameplay
            // objects after reconstruction has already populated those slots.
            // Entities and components must prove ownership through their
            // EntityList or ComponentList instead.
            if (typeof(Entity).IsAssignableFrom(type) || typeof(Component).IsAssignableFrom(type)) {
                return false;
            }
            if (node.ParentKind == "array" &&
                nodes.TryGetValue(node.ParentNodeId, out AkronReconstructionNode arrayParent) &&
                Objects.TryGetValue(node.ParentNodeId, out object parentObject) &&
                parentObject is Array freshArray &&
                HasArrayIndex(freshArray, node.ParentArrayIndicesOrNull)) {
                Type arrayType = ResolveType(arrayParent.TypeName, arrayParent.Path);
                if (!arrayType.IsArray) {
                    return false;
                }
                Type elementType = arrayType.GetElementType();
                object freshItem = freshArray.GetValue(node.ParentArrayIndicesOrNull.ToArray());
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
                       HasArrayIndex(array, node.ParentArrayIndicesOrNull)) {
                matchedObject = array.GetValue(node.ParentArrayIndicesOrNull.ToArray());
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
                foreach (AkronReconstructionField field in parent.FieldsOrNull ?? Enumerable.Empty<AkronReconstructionField>()) {
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
                int itemCount = Math.Min(parent.ItemsOrNull?.Count ?? 0, array.Length);
                for (int itemIndex = 0; itemIndex < itemCount; itemIndex++) {
                    AkronReconstructionValue item = parent.ItemsOrNull[itemIndex];
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
                target.Kind is AnchorKind or PersistentResourceKind or DelegateKind or EventInstanceKind
                    or WeakReferenceKind) {
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
            bool screenWipeRendererListAlias = IsAuthenticatedScreenWipeRendererListAlias(
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
            // The runtime-entity counterpart of freshComponentCapturedFreshEdge:
            // an entity the room builds on first use keeps a reference to the
            // live object that built it. DustGraphic adds its Eyeballs entity in
            // AddDustNodesIfInCamera and hands it `this` in the constructor, so
            // the reconstructed Eyeballs points at the fresh DustGraphic through
            // its own declared, exactly-typed field. The reference is written
            // into the reconstruction's own field and displaces nothing the
            // fresh room built.
            bool runtimeEntityCapturedFreshEdge = edgeField != null &&
                                                  resolvedFreshObjectNodes.Contains(target.Id) &&
                                                  (authenticatedRuntimeEntityNodes.Contains(edgeParent.Id) ||
                                                   IsAuthenticatedBuiltInRuntimeEntity(edgeParent, edgeParentType) ||
                                                   IsAuthenticatedGeneratedRuntimeEntity(edgeParent, edgeParentType)) &&
                                                  Objects.TryGetValue(target.Id, out object runtimeCapturedObject) &&
                                                  ResolveField(
                                                      edgeField.DeclaringTypeName,
                                                      edgeField.Name,
                                                      edgeField.Path).FieldType == targetType &&
                                                  runtimeCapturedObject?.GetType() == targetType;
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
            bool authenticatedFreshOrRuntimeEntity =
                resolvedFreshObjectNodes.Contains(target.Id) ||
                authenticatedBuiltInRuntimeEntity;
            bool authenticatedRuntimeEntityTrackerAlias =
                authenticatedFreshOrRuntimeEntity &&
                IsAuthenticatedRuntimeEntityTrackerAlias(target, targetType, edgeParent);
            bool authenticatedRuntimeEntityTagListAlias =
                authenticatedFreshOrRuntimeEntity &&
                IsAuthenticatedRuntimeEntityTagListAlias(target, targetType, edgeParent);
            bool freshEntityListAlias = IsAuthenticatedEntityListAlias(
                target,
                targetType,
                edgeParent,
                authenticatedBuiltInRuntimeEntity);
            bool entityOwnedCollectionAlias =
                IsAuthenticatedEntityOwnedCollectionAlias(target, targetType, edgeParent);
            if (typeof(Entity).IsAssignableFrom(targetType) &&
                !resolvedFreshObjectNodes.Contains(target.Id) &&
                savedOwnerEdge &&
                target.ParentKind == "array" &&
                target.ParentNodeId == edgeParent.Id &&
                (!TryGetEntityListOwnerNode(target, out AkronReconstructionNode canonicalEntityListNode) ||
                 (!IsOwnedCollectionStorageDescendant(
                      edgeParent,
                      canonicalEntityListNode.Id,
                      componentList: false) &&
                  !entityOwnedCollectionAlias))) {
                throw new AkronReconstructionException(
                    target.Path,
                    "entity canonical array is not owned by its scene EntityList;type=" + targetType.FullName,
                    target.TypeName);
            }
            bool freshEntityPeerLink = IsAuthenticatedFreshEntityPeerLink(
                target,
                targetType,
                edgeParent,
                edgeField);
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
            // The canonical owner edge of a component built on first use: the
            // node licence carries the ownership proof, so only the exact edge
            // the saved graph proves is admitted, never an alias from elsewhere.
            bool lazilyBuiltFieldComponentEdge =
                savedOwnerEdge &&
                target.ParentKind == "field" &&
                target.ParentNodeId == edgeParent.Id &&
                (authenticatedFieldBuiltComponentNodes.Contains(target.Id) ||
                 IsAuthenticatedLazilyBuiltFieldComponent(target, targetType));
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
            bool iteratorOwnedComponentAlias =
                IsAuthenticatedIteratorOwnedComponentAlias(target, edgeParent);
            bool coroutineStackIteratorAlias =
                IsAuthenticatedCoroutineStackIteratorAlias(
                    target,
                    edgeParent,
                    edgeParentType,
                    edgeField,
                    savedOwnerEdge);
            // The canonical owner edge of an owner-proved iterator into a
            // Coroutine stack slot. The alias rule refuses this edge so it
            // cannot prove itself; what proves it instead is the iterator's own
            // captured <>4__this owner. The fresh room's silence here is
            // expected rather than suspicious: a routine that finished before
            // the fresh baseline froze leaves an empty stack, so there is no
            // occurrence to spend where the saved room was mid-flight -
            // LightningRenderer's bolts hold exactly that shape. The storage-
            // only walk is the confinement: the canonical chain must be pure
            // stack plumbing, so a value a frame yielded cannot carry this.
            bool coroutineStackIteratorOwnerEdge =
                savedOwnerEdge &&
                authenticatedRuntimeStateNodes.Contains(target.Id) &&
                IsCoroutineStackIteratorSlot(edgeParent, edgeParentType, edgeField) &&
                TryGetCoroutineEnumeratorStackOwner(target, CoroutineStackWalk.StorageOnly, out _);
            // The canonical owner edge of a closure the node licence carried:
            // its owner iterator holds a direct proof, so the hoisted <>8__
            // field edge from that same iterator is the ownership the licence
            // already established. Deferred iterators stay out for the same
            // withdrawal reason the node licence keeps them out.
            bool directIteratorClosureOwnerEdge =
                savedOwnerEdge &&
                target.ParentKind == "field" &&
                target.ParentNodeId == edgeParent.Id &&
                authenticatedIteratorClosureNodes.Contains(target.Id) &&
                !deferredProvisionalIteratorIds.Contains(edgeParent.Id);
            // The two canonical edges of Everest's yielded-value plumbing: a
            // frame's <>2__current holding the SwapImmediately wrapper, and the
            // wrapper's Inner holding the frame it wraps. The node licences
            // carry the position proof; these edges spend it.
            bool coroutinePlumbingEdge =
                savedOwnerEdge &&
                target.ParentNodeId == edgeParent.Id &&
                (authenticatedCoroutinePlumbingNodes.Contains(target.Id) ||
                 (authenticatedCoroutinePlumbingNodes.Contains(edgeParent.Id) &&
                  authenticatedRuntimeStateNodes.Contains(target.Id)));
            bool authenticatedIteratorOwnerEdge = edgeField?.Name == "<>4__this" &&
                                                   authenticatedRuntimeStateNodes.Contains(edgeParent.Id) &&
                                                   IsCapturedCompilerThisOwner(edgeParentType, targetType) &&
                                                   (resolvedFreshObjectNodes.Contains(target.Id) ||
                                                    authenticatedRuntimeEntityNodes.Contains(target.Id));
            bool authenticatedDelegateTargetOwnerEdge = edgeField?.Name == "<>4__this" &&
                                                        authenticatedDelegateTargetNodes.Contains(edgeParent.Id) &&
                                                        IsCapturedCompilerThisOwner(edgeParentType, targetType) &&
                                                        resolvedFreshObjectNodes.Contains(target.Id);
            bool authenticatedDelegateAliasOwnerEdge = edgeField?.Name == "<>4__this" &&
                                                       IsCapturedCompilerThisOwner(edgeParentType, targetType) &&
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
                                                         IsCapturedCompilerThisOwner(edgeParentType, targetType) &&
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
                                  authenticatedRuntimeEntityTrackerAlias || authenticatedRuntimeEntityTagListAlias ||
                                  freshArrayMembershipAlias || screenWipeRendererListAlias ||
                                  freshRendererComponentIndexAlias ||
                                  freshEntityListAlias || freshEntityPeerLink ||
                                  freshComponentCapturedFreshEdge ||
                                  runtimeEntityCapturedFreshEdge ||
                                  entityOwnedCollectionAlias ||
                                  freshOwnedNestedState || reconstructedOwnedComponentAlias || freshFieldAlias ||
                                  lazilyBuiltFieldComponentEdge ||
                                  reconstructedOwnedComponentOwnerEdge ||
                                  freshOwnerAliasMerge ||
                                  trailSnapshotPlayerComponentAlias ||
                                  reconstructedBuiltInComponentAlias ||
                                  freshSceneEntityAlias || entityComponentListBackReference ||
                                  sceneRendererBackReference || reconstructedEntitySceneBackReference ||
                                  freshHashSetMembership || iteratorOwnedComponentAlias ||
                                  coroutineStackIteratorAlias ||
                                  coroutineStackIteratorOwnerEdge ||
                                  directIteratorClosureOwnerEdge ||
                                  coroutinePlumbingEdge ||
                                  reconstructedSafeParentEdge || authenticatedIteratorOwnerEdge ||
                                  authenticatedDelegateTargetOwnerEdge ||
                                  authenticatedDelegateAliasOwnerEdge ||
                                  authenticatedDelegateCapturedFreshEdge ||
                                  authenticatedIteratorClosureOwnerEdge;
            if (HasListStorageIndex(structuralPath)) {
                // An edge ownership has already proved does not draw on the occurrence
                // count, so it may not spend from it either. This escape used to sit
                // inside the exhausted branch below, which made an ownership-proved
                // edge exempt from needing an occurrence but not from paying for one -
                // and which of the two it did depended on whether an occurrence
                // happened to be left when the document reached it.
                //
                // That decided the same room by document order alone. Two instances of
                // one mod entity, each carrying a component that holds a runtime state
                // object, where the reload built one of the two: one occurrence of that
                // state type at
                // entities._items[*].<Components>k__BackingField.components._items[*].State
                // against the document's two edges. With the paired component first its
                // edge spent the occurrence and the rebuilt one was refused; with the
                // rebuilt one first it took the occurrence, the paired edge fell through
                // this escape, and the room that came out was right in both halves -
                // the paired component keeping its own live object and the other
                // getting a rebuilt one. So the refusal was the wrong answer of the two.
                // 010f660 refuses that room the same way, so this is not a defect of
                // this branch.
                //
                // The other repair, charging the edge and refusing it when there is
                // nothing to charge, was built and measured: it fails 32 rooms, because
                // the alias rules exist precisely to admit edges the fresh room's count
                // says nothing about. An edge that needs no occurrence cannot owe one.
                //
                // Nothing else moves. RefuseAnEdgeThatDropsAFreshObjectTheDocumentKeeps
                // already returned on its first clause for an ownership-proved edge, so
                // no edge loses a check it used to get, and the exact-path branch below
                // spends nothing and so was never order dependent.
                if (exactOwnerEdge) {
                    return;
                }
                string listPathKey = StructuralResourcePathKey(
                    targetType,
                    structuralPath,
                    wildcardListStorageIndices: true);
                if (!freshListStructuralTypeCounts.TryGetValue(listPathKey, out int remaining) || remaining <= 0) {
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
                        ";authenticated-runtime-entity-tag-list-alias=" + authenticatedRuntimeEntityTagListAlias.ToString().ToLowerInvariant() +
                        ";fresh-array-membership-alias=" + freshArrayMembershipAlias.ToString().ToLowerInvariant() +
                        ";screen-wipe-renderer-list-alias=" + screenWipeRendererListAlias.ToString().ToLowerInvariant() +
                        ";fresh-renderer-component-index-alias=" + freshRendererComponentIndexAlias.ToString().ToLowerInvariant() +
                        ";fresh-component-captured-fresh-edge=" + freshComponentCapturedFreshEdge.ToString().ToLowerInvariant() +
                        ";runtime-entity-captured-fresh-edge=" + runtimeEntityCapturedFreshEdge.ToString().ToLowerInvariant() +
                        ";trail-snapshot-player-component-alias=" + trailSnapshotPlayerComponentAlias.ToString().ToLowerInvariant() +
                        ";reconstructed-built-in-component-alias=" + reconstructedBuiltInComponentAlias.ToString().ToLowerInvariant() +
                        ";authenticated-built-in-runtime-entity=" + authenticatedBuiltInRuntimeEntity.ToString().ToLowerInvariant() +
                        ";fresh-entity-list-alias=" + freshEntityListAlias.ToString().ToLowerInvariant() +
                        ";fresh-entity-peer-link=" + freshEntityPeerLink.ToString().ToLowerInvariant() +
                        ";entity-owned-collection-alias=" + entityOwnedCollectionAlias.ToString().ToLowerInvariant() +
                        ";fresh-owned-nested-state=" + freshOwnedNestedState.ToString().ToLowerInvariant() +
                        ";reconstructed-owned-component-alias=" + reconstructedOwnedComponentAlias.ToString().ToLowerInvariant() +
                        ";lazily-built-field-component=" + lazilyBuiltFieldComponentEdge.ToString().ToLowerInvariant() +
                        ";reconstructed-owned-component-owner-edge=" + reconstructedOwnedComponentOwnerEdge.ToString().ToLowerInvariant() +
                        ";fresh-field-alias=" + freshFieldAlias.ToString().ToLowerInvariant() +
                        ";fresh-owner-alias-merge=" + freshOwnerAliasMerge.ToString().ToLowerInvariant() +
                        ";fresh-scene-entity-alias=" + freshSceneEntityAlias.ToString().ToLowerInvariant() +
                        ";entity-component-list-back-reference=" + entityComponentListBackReference.ToString().ToLowerInvariant() +
                        ";scene-renderer-back-reference=" + sceneRendererBackReference.ToString().ToLowerInvariant() +
                        ";reconstructed-entity-scene-back-reference=" + reconstructedEntitySceneBackReference.ToString().ToLowerInvariant() +
                        ";fresh-hash-set-membership=" + freshHashSetMembership.ToString().ToLowerInvariant() +
                        ";iterator-owned-component-alias=" + iteratorOwnedComponentAlias.ToString().ToLowerInvariant() +
                        ";coroutine-stack-iterator-alias=" + coroutineStackIteratorAlias.ToString().ToLowerInvariant() +
                        ";coroutine-stack-iterator-owner-edge=" + coroutineStackIteratorOwnerEdge.ToString().ToLowerInvariant() +
                        ";direct-iterator-closure-owner-edge=" + directIteratorClosureOwnerEdge.ToString().ToLowerInvariant() +
                        ";coroutine-plumbing-edge=" + coroutinePlumbingEdge.ToString().ToLowerInvariant() +
                        ";reconstructed-safe-parent-edge=" + reconstructedSafeParentEdge.ToString().ToLowerInvariant() +
                        ";authenticated-iterator-owner-edge=" + authenticatedIteratorOwnerEdge.ToString().ToLowerInvariant() +
                        ";authenticated-delegate-target-owner-edge=" + authenticatedDelegateTargetOwnerEdge.ToString().ToLowerInvariant() +
                        ";authenticated-delegate-alias-owner-edge=" + authenticatedDelegateAliasOwnerEdge.ToString().ToLowerInvariant() +
                        ";authenticated-delegate-captured-fresh-edge=" + authenticatedDelegateCapturedFreshEdge.ToString().ToLowerInvariant() +
                        ";authenticated-iterator-closure-owner-edge=" + authenticatedIteratorClosureOwnerEdge.ToString().ToLowerInvariant() +
                        ";edge-parent-type=" + edgeParent.TypeName +
                        ";edge-field=" + (edgeField?.Name ?? "<array>"),
                        target.TypeName);
                }
                // The count admits this edge, so ask the one question it cannot.
                RefuseAnEdgeThatDropsAFreshObjectTheDocumentKeeps(
                    target,
                    targetType,
                    edgeParent,
                    edgeParentType,
                    edgeField,
                    exactOwnerEdge);
                // Every edge the count admits spends, including one whose target the
                // fresh room already holds. Exempting those was tried and reverted
                // twice: it leaves the budget standing longer for everything, and the
                // thing that then gets in is whatever carries no identity at all.
                //
                // The escape above is a different exemption, and it is not the smaller
                // of the two - measured over the 749 list-path edges this suite's rooms
                // produce, 613 are both ownership-proved and already in the room, 82 are
                // ownership-proved reconstructions that the reverted exemption would
                // still have charged, and 9 are already in the room with nothing proving
                // the edge, which the escape above still charges. So the two overlap
                // heavily and neither contains the other. What separates them is the
                // fact each rests on. The reverted one said the target is already in the
                // room, which is a fact about the object and says nothing about this
                // edge. The escape above says this edge was admitted without consulting
                // the count, so it has nothing to pay from.
                //
                // No test fails when this line is reverted any more, and that is worth
                // saying rather than leaving to be discovered. The room that used to
                // fail is AnUnpairableTrailedMapEntityIsRefusedInEitherDocumentOrder,
                // and the refusal above now catches it whichever way the count is kept.
                // Keeping the spend is still right: it is what makes the count mean the
                // number of objects the fresh room has here, and a widened count would
                // admit reconstructions at every path where nothing is displaced and so
                // where the refusal above is silent.
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
                    ";authenticated-runtime-entity-tag-list-alias=" + authenticatedRuntimeEntityTagListAlias.ToString().ToLowerInvariant() +
                    ";fresh-array-membership-alias=" + freshArrayMembershipAlias.ToString().ToLowerInvariant() +
                    ";screen-wipe-renderer-list-alias=" + screenWipeRendererListAlias.ToString().ToLowerInvariant() +
                    ";fresh-renderer-component-index-alias=" + freshRendererComponentIndexAlias.ToString().ToLowerInvariant() +
                    ";fresh-component-captured-fresh-edge=" + freshComponentCapturedFreshEdge.ToString().ToLowerInvariant() +
                    ";runtime-entity-captured-fresh-edge=" + runtimeEntityCapturedFreshEdge.ToString().ToLowerInvariant() +
                    ";trail-snapshot-player-component-alias=" + trailSnapshotPlayerComponentAlias.ToString().ToLowerInvariant() +
                    ";reconstructed-built-in-component-alias=" + reconstructedBuiltInComponentAlias.ToString().ToLowerInvariant() +
                    ";authenticated-built-in-runtime-entity=" + authenticatedBuiltInRuntimeEntity.ToString().ToLowerInvariant() +
                    ";fresh-entity-list-alias=" + freshEntityListAlias.ToString().ToLowerInvariant() +
                    ";fresh-entity-peer-link=" + freshEntityPeerLink.ToString().ToLowerInvariant() +
                    ";entity-owned-collection-alias=" + entityOwnedCollectionAlias.ToString().ToLowerInvariant() +
                    ";fresh-owned-nested-state=" + freshOwnedNestedState.ToString().ToLowerInvariant() +
                    ";reconstructed-owned-component-alias=" + reconstructedOwnedComponentAlias.ToString().ToLowerInvariant() +
                    ";lazily-built-field-component=" + lazilyBuiltFieldComponentEdge.ToString().ToLowerInvariant() +
                    ";reconstructed-owned-component-owner-edge=" + reconstructedOwnedComponentOwnerEdge.ToString().ToLowerInvariant() +
                    ";fresh-field-alias=" + freshFieldAlias.ToString().ToLowerInvariant() +
                    ";fresh-owner-alias-merge=" + freshOwnerAliasMerge.ToString().ToLowerInvariant() +
                    ";fresh-scene-entity-alias=" + freshSceneEntityAlias.ToString().ToLowerInvariant() +
                    ";entity-component-list-back-reference=" + entityComponentListBackReference.ToString().ToLowerInvariant() +
                    ";scene-renderer-back-reference=" + sceneRendererBackReference.ToString().ToLowerInvariant() +
                    ";reconstructed-entity-scene-back-reference=" + reconstructedEntitySceneBackReference.ToString().ToLowerInvariant() +
                    ";fresh-hash-set-membership=" + freshHashSetMembership.ToString().ToLowerInvariant() +
                    ";iterator-owned-component-alias=" + iteratorOwnedComponentAlias.ToString().ToLowerInvariant() +
                    ";coroutine-stack-iterator-alias=" + coroutineStackIteratorAlias.ToString().ToLowerInvariant() +
                    ";coroutine-stack-iterator-owner-edge=" + coroutineStackIteratorOwnerEdge.ToString().ToLowerInvariant() +
                    ";direct-iterator-closure-owner-edge=" + directIteratorClosureOwnerEdge.ToString().ToLowerInvariant() +
                    ";coroutine-plumbing-edge=" + coroutinePlumbingEdge.ToString().ToLowerInvariant() +
                    ";reconstructed-safe-parent-edge=" + reconstructedSafeParentEdge.ToString().ToLowerInvariant() +
                    ";authenticated-iterator-owner-edge=" + authenticatedIteratorOwnerEdge.ToString().ToLowerInvariant() +
                    ";authenticated-delegate-target-owner-edge=" + authenticatedDelegateTargetOwnerEdge.ToString().ToLowerInvariant() +
                    ";authenticated-delegate-alias-owner-edge=" + authenticatedDelegateAliasOwnerEdge.ToString().ToLowerInvariant() +
                    ";authenticated-delegate-captured-fresh-edge=" + authenticatedDelegateCapturedFreshEdge.ToString().ToLowerInvariant() +
                    ";authenticated-iterator-closure-owner-edge=" + authenticatedIteratorClosureOwnerEdge.ToString().ToLowerInvariant() +
                    ";edge-parent-type=" + edgeParent.TypeName +
                    ";edge-field=" + (edgeField?.Name ?? "<array>"),
                    target.TypeName);
            }
            // The exact path admits this edge, so ask the one question it cannot.
            RefuseAnEdgeThatDropsAFreshObjectTheDocumentKeeps(
                target,
                targetType,
                edgeParent,
                edgeParentType,
                edgeField,
                exactOwnerEdge);
        }

        // The last thing asked of an edge the two structural tests above have already
        // admitted: would writing this reconstruction into a named field of an object
        // the fresh room supplied drop the value that field holds, when some other node
        // of this same document is already paired with it?
        //
        // A document that asks for that says two contradictory things about one room:
        // that the displaced object is still in it - a node holds it and restores its
        // state onto it - and that a live object's field belongs to something the room
        // does not have. Neither structural test can see the contradiction. The
        // occurrence budget counts objects, "the fresh room holds N of this type at
        // this shape of path", and a reconstruction spends from that count in document
        // order, so reversing two entities in the saved list flips the same room from
        // refused to accepted. freshStructuralTypes is weaker still: the exact path it
        // matches is the path of the very object being dropped, so the evidence
        // admitting the reconstruction is the existence of the thing it destroys.
        //
        // The room this closes is
        // AnUnpairableTrailedMapEntityIsRefusedInEitherDocumentOrder: two trailed map
        // entities the map still places, one of which the reloaded room rebuilt under a
        // different EntityID. With the paired entity's trail first the budget was
        // already gone and the unpairable one was refused; with the unpairable one first
        // it took the occurrence, and the restore reported success while the ghost the
        // reload built was dropped from the room and the surviving trail's live
        // PlayerSprite was pointed at the reconstruction. Both orders refuse now.
        //
        // The pairing is what makes the refusal safe, and "the fresh field holds
        // anything" is deliberately not the test. A fresh field holding an object the
        // saved frame deleted is the crossed population - the saved frame is the truth,
        // dropping that object is the correct outcome, and the restore has to succeed.
        // Measured: refusing on a non-null value alone refuses
        // AFreshEntityTakesBackThePeerTheSavedFrameKeptWhenTheReloadCachedAnother, which
        // restores correctly today. Only a displaced object the document itself keeps
        // is evidence of anything.
        //
        // Collection storage is excluded because an element position is not a named
        // slot. Arrays reach their elements with no field at all; a Dictionary or
        // HashSet entry reaches its value through a field of the entry struct, and
        // which entry holds which object is an artefact of hash layout, so writing a
        // different object into one displaces nothing. Without that exclusion the
        // membership set of a room's own EntityList refuses every rebuilt entity.
        //
        // The exemption on the first clause is the one thing this rule does not prove
        // for itself, so here is what it rests on and what was tried instead.
        //
        // Measured over the suite: two rooms reach the displacement this rule looks for
        // with an ownership-proved edge, and both load correctly.
        // RestoreSeparatesOrdinaryObjectsThatTheFreshRoomAliases is the fresh room
        // holding one object in two named fields where the document holds two, proved by
        // savedOwnerEdge with exactParentSlot.
        // AnEntityKeepsTheStateItRanLastWhenTheDocumentSeparatesTwoOfThem is the same
        // separation on an entity's own nested state, and there the only proof is
        // freshOwnedNestedState - exactParentSlot is false, because the node's own slot
        // is the one a clean load leaves empty. So narrowing the exemption to the proof
        // the first room uses refuses the second, and an allowlist of the two is a list
        // of the rooms that happen to have been built rather than a reason.
        //
        // Replacing the exemption with a proof that the displaced object is retained
        // elsewhere cannot be stated, because in a document this capture produces there
        // is nothing to prove it against. ValidateAssignments writes every field of every
        // ordinary node and every item of every array node, and every node this capture
        // produces reaches the root through those two containers or through a delegate
        // call, whose target CreateDelegate binds into the delegate it rebuilds. Nothing
        // hangs anywhere else: CaptureValue returns as soon as it has made a live anchor,
        // and stores only a payload for a persistent resource or an FMOD event. The
        // restore therefore always puts a displaced object back into the slot the
        // document gives it, so retention holds for every incumbent, and a test narrow
        // enough to refuse anything either refuses a room that loads - which is what
        // narrowing to the first room's proof does to the second - or refuses only rooms
        // the structural tests above already refuse, which is what asking for the
        // incumbent at its own canonical slot does, since a paired object whose canonical
        // slot the reload left empty loses its own edge there first.
        //
        // One shape used to break that, and it is a document this capture cannot
        // produce: a reference parked in a slot the restore never reads, which
        // reachability counted as reach while nothing ever wrote it. That is a hole in
        // the document contract rather than a question about this rule, and it is now
        // refused where the contract is stated, by
        // RefuseAReferenceInASlotTheRestoreNeverReads. Every slot a node can be reached
        // through is therefore one the restore writes - assignment for a field or an
        // item, CreateDelegate for a call target - which is what makes the paragraph
        // above true of every document this build will read rather than only of the ones
        // capture writes.
        //
        // What is left is the same shape as the two call sites above: this is a third
        // authenticity test, for edges the structural tests admitted on weak evidence -
        // a count that wildcards list indices, or a path shared with the very object
        // being displaced. An edge carrying its own ownership proof needed neither
        // structural test, which is why it does not need this one either. If a future
        // change ever leaves an assigned document edge unwritten, or puts an object's
        // canonical home somewhere the restore cannot reach, the retention question
        // becomes real and this exemption has to be reopened.
        //
        // Order-independent by construction: freshOwners is complete before
        // ValidateReferenceAuthenticity runs, so the verdict cannot depend on which
        // edge is validated first, which is the whole point of the rule.
        private void RefuseAnEdgeThatDropsAFreshObjectTheDocumentKeeps(
            AkronReconstructionNode target,
            Type targetType,
            AkronReconstructionNode edgeParent,
            Type edgeParentType,
            AkronReconstructionField edgeField,
            bool exactOwnerEdge
        ) {
            if (exactOwnerEdge || edgeField == null || edgeParentType.IsValueType ||
                resolvedFreshObjectNodes.Contains(target.Id) ||
                !resolvedFreshObjectNodes.Contains(edgeParent.Id) ||
                !Objects.TryGetValue(edgeParent.Id, out object freshParent)) {
                return;
            }
            FieldInfo field = ResolveField(edgeField.DeclaringTypeName, edgeField.Name, edgeField.Path);
            if (!field.DeclaringType.IsInstanceOfType(freshParent) ||
                field.GetValue(freshParent) is not object displaced ||
                !freshOwners.TryGetValue(displaced, out int displacedNodeId)) {
                return;
            }
            throw new AkronReconstructionException(
                target.Path,
                "reconstructed reference edge would drop a fresh object this document keeps;type=" +
                targetType.FullName +
                ";edge-parent-type=" + edgeParent.TypeName +
                ";edge-field=" + edgeField.Name +
                ";displaced-type=" + displaced.GetType().FullName +
                ";displaced-node=" + displacedNodeId.ToString(CultureInfo.InvariantCulture),
                target.TypeName);
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

        // The hostile-surface limit both component licences share: a component
        // owning native or process state through IDisposable or a finalizer
        // never enters a room through either of them. One predicate, so the
        // two licences cannot drift apart.
        private static bool IsComponentTypeSafeToReconstruct(Type targetType) {
            return typeof(Component).IsAssignableFrom(targetType) &&
                   !typeof(IDisposable).IsAssignableFrom(targetType) &&
                   targetType.GetMethod(
                       "Finalize",
                       BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) == null;
        }

        private bool IsAuthenticatedReconstructedOwnedComponent(
            AkronReconstructionNode target,
            Type targetType
        ) {
            if (!IsComponentTypeSafeToReconstruct(targetType) ||
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

        // A Component built on first use and kept in a declared field that no
        // ComponentList ever carries. DustGraphic builds its blink Coroutine in
        // BeforeRender and updates it by hand, and LightningRenderer's Bolt does
        // the same with its routine, so no list can vouch for the saved one and
        // a fresh room that never rendered holds null at the slot - which is why
        // the list-owned licence above cannot reach these and Celestial Resort
        // and Farewell rooms were refused over them.
        //
        // What vouches instead: the node's canonical owner is a field the
        // owner's own type declares at the exact component type, and that owner
        // has already proved itself - a fresh pairing, the list-owned component
        // licence, the owned-nested-state licence, or the runtime-entity
        // licence. IsAuthenticatedByExactParentSlot deliberately refuses
        // Component targets because a generic slot proves nothing about
        // gameplay ownership; this asks the owner to be proved first, which is
        // the ownership that generic rule lacks. When the owner is a fresh
        // object its slot must still be empty, so nothing the room built is
        // displaced. IsComponentTypeSafeToReconstruct carries the same
        // hostile-surface limit the list-owned licence holds.
        private bool IsAuthenticatedLazilyBuiltFieldComponent(AkronReconstructionNode target, Type targetType) {
            if (!IsComponentTypeSafeToReconstruct(targetType) ||
                target.ParentKind != "field" ||
                !nodes.TryGetValue(target.ParentNodeId, out AkronReconstructionNode ownerNode)) {
                return false;
            }
            Type ownerType = ResolveType(ownerNode.TypeName, ownerNode.Path);
            bool ownerIsFresh = resolvedFreshObjectNodes.Contains(ownerNode.Id);
            if (!ownerIsFresh &&
                !authenticatedOwnedComponentNodes.Contains(ownerNode.Id) &&
                !authenticatedOwnedNestedStateNodes.Contains(ownerNode.Id) &&
                !authenticatedRuntimeEntityNodes.Contains(ownerNode.Id) &&
                !IsAuthenticatedReconstructedOwnedComponent(ownerNode, ownerType)) {
                return false;
            }
            FieldInfo field = ResolveField(
                target.ParentDeclaringTypeName,
                target.ParentFieldName,
                target.Path);
            if (field.FieldType != targetType || !field.DeclaringType.IsAssignableFrom(ownerType)) {
                return false;
            }
            if (!ownerIsFresh) {
                return true;
            }
            return Objects.TryGetValue(ownerNode.Id, out object ownerObject) &&
                   field.DeclaringType.IsInstanceOfType(ownerObject) &&
                   field.GetValue(ownerObject) == null;
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

        private bool IsAuthenticatedRuntimeEntityTagListAlias(
            AkronReconstructionNode target,
            Type targetType,
            AkronReconstructionNode edgeParent
        ) {
            if (!typeof(Entity).IsAssignableFrom(targetType) ||
                !TryGetEntityListOwnerNode(target, out AkronReconstructionNode entityListNode) ||
                FindReferenceField(entityListNode, "<Scene>k__BackingField") is not AkronReconstructionValue entityScene) {
                return false;
            }

            AkronReconstructionNode child = edgeParent;
            AkronReconstructionNode current = nodes.TryGetValue(
                edgeParent.ParentNodeId,
                out AkronReconstructionNode parent)
                ? parent
                : null;
            while (current != null) {
                if (ResolveType(current.TypeName, current.Path) == typeof(TagLists)) {
                    if (child.ParentNodeId != current.Id || child.ParentKind != "field" ||
                        child.ParentFieldName != "lists" ||
                        current.ParentKind != "field" || current.ParentFieldName != "<TagLists>k__BackingField" ||
                        !nodes.TryGetValue(current.ParentNodeId, out AkronReconstructionNode sceneNode) ||
                        !typeof(Scene).IsAssignableFrom(ResolveType(sceneNode.TypeName, sceneNode.Path)) ||
                        !resolvedFreshObjectNodes.Contains(sceneNode.Id) ||
                        !resolvedFreshObjectNodes.Contains(current.Id) ||
                        !resolvedFreshObjectNodes.Contains(entityListNode.Id)) {
                        return false;
                    }

                    return entityScene.NodeId == sceneNode.Id &&
                           FindReferenceField(sceneNode, "<Entities>k__BackingField")?.NodeId == entityListNode.Id &&
                           FindReferenceField(sceneNode, "<TagLists>k__BackingField")?.NodeId == current.Id;
                }
                if (typeof(Entity).IsAssignableFrom(ResolveType(current.TypeName, current.Path)) ||
                    !nodes.TryGetValue(current.ParentNodeId, out parent)) {
                    return false;
                }
                child = current;
                current = parent;
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

        private bool IsAuthenticatedScreenWipeRendererListAlias(
            AkronReconstructionNode target,
            Type targetType,
            AkronReconstructionNode edgeParent,
            AkronReconstructionField edgeField
        ) {
            if (edgeField != null ||
                !resolvedFreshObjectNodes.Contains(target.Id) ||
                !resolvedFreshObjectNodes.Contains(edgeParent.Id) ||
                !IsAuthenticatedBuiltInScreenWipe(target, targetType) ||
                !TryGetFieldParent(edgeParent.Id, "_items", out AkronReconstructionNode rendererStorage) ||
                rendererStorage.ParentKind != "field" ||
                !AkronReconstructionGraph.IsRendererListStorageField(rendererStorage.ParentFieldName) ||
                !nodes.TryGetValue(rendererStorage.ParentNodeId, out AkronReconstructionNode rendererListNode) ||
                ResolveType(rendererListNode.TypeName, rendererListNode.Path) != typeof(RendererList) ||
                !resolvedFreshObjectNodes.Contains(rendererListNode.Id)) {
                return false;
            }

            return nodes.Values.Any(levelNode =>
                typeof(Level).IsAssignableFrom(ResolveType(levelNode.TypeName, levelNode.Path)) &&
                resolvedFreshObjectNodes.Contains(levelNode.Id) &&
                FindReferenceField(levelNode, nameof(Level.Wipe))?.NodeId == target.Id &&
                FindReferenceField(levelNode, "<RendererList>k__BackingField")?.NodeId == rendererListNode.Id &&
                FindReferenceField(rendererListNode, "scene")?.NodeId == levelNode.Id);
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
                !TryGetComponentOwnerNodes(target, out _, out int playerNodeId) ||
                !nodes.TryGetValue(playerNodeId, out AkronReconstructionNode playerOwnerNode) ||
                !AkronReconstructionGraph.IsTrailSnapshotComponentOwnerType(
                    ResolveType(playerOwnerNode.TypeName, playerOwnerNode.Path))) {
                return false;
            }

            // The caller already proved that the restored component belongs to
            // its saved owner. A trail intentionally outlives the Player or
            // PlayerPlayback that supplied it, so the typed saved owner loop
            // is the remaining proof for this built-in Snapshot field.
            return true;
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
            bool authenticatedBuiltInRuntimeEntity
        ) {
            return typeof(Entity).IsAssignableFrom(targetType) &&
                   (resolvedFreshObjectNodes.Contains(target.Id) ||
                    authenticatedBuiltInRuntimeEntity) &&
                   TryGetEntityListOwnerNode(target, out AkronReconstructionNode entityListNode) &&
                   IsOwnedCollectionStorageDescendant(
                       edgeParent,
                       entityListNode.Id,
                       componentList: false);
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
                !HasAuthenticatedEntityListSceneOwnership(node, out _)) {
                return false;
            }

            // Runtime effects have no map EntityID, so a clean room cannot
            // create them. Authenticate the built-in type through the saved
            // Entity <-> EntityList <-> Scene ownership loop and the exact
            // fresh Scene/List pair instead of trusting a type name alone.
            return true;
        }

        private bool IsAuthenticatedGeneratedRuntimeEntity(
            AkronReconstructionNode node,
            Type type
        ) {
            if (!typeof(Entity).IsAssignableFrom(type) || type.IsAbstract ||
                type.Assembly == typeof(Entity).Assembly ||
                typeof(IDisposable).IsAssignableFrom(type) ||
                type.GetMethod(
                    "Finalize",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) != null ||
                TryGetSavedEntityId(node, out _) ||
                !HasAuthenticatedEntityListSceneOwnership(
                    node,
                    out (AkronReconstructionNode Node, EntityList List) entityList)) {
                return false;
            }

            // Some mods generate a room's runtime entities from shuffled or
            // random layouts. Their exact count and EntityList paths can differ
            // after a cold reload even though the fresh room loaded the same
            // concrete type. That fresh type occurrence, plus the saved
            // Entity/List/Scene ownership loop, authenticates reconstruction of
            // the saved population. A type absent from the fresh room remains
            // rejected.
            return GetFreshEntityTypes(entityList.Node, entityList.List).ContainsKey(type);
        }

        private bool HasAuthenticatedEntityListSceneOwnership(
            AkronReconstructionNode node,
            out (AkronReconstructionNode Node, EntityList List) ownerList
        ) {
            ownerList = default;
            if (!TryGetEntityListOwnerNode(node, out AkronReconstructionNode entityListNode)) {
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
                !Objects.TryGetValue(entityListNode.Id, out object listObject) || listObject is not EntityList liveList) {
                return false;
            }

            if (!ReferenceEquals(GetSceneEntities(scene), liveList)) {
                return false;
            }
            ownerList = (entityListNode, liveList);
            return true;
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

        private bool IsAuthenticatedEntityOwnedCollectionAlias(
            AkronReconstructionNode target,
            Type targetType,
            AkronReconstructionNode edgeParent
        ) {
            bool targetIsFresh = resolvedFreshObjectNodes.Contains(target.Id);
            bool targetIsAuthenticatedRuntime = authenticatedRuntimeEntityNodes.Contains(target.Id);
            if (!typeof(Entity).IsAssignableFrom(targetType) ||
                (!targetIsFresh && !targetIsAuthenticatedRuntime) ||
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
            bool ownerIsFresh = current != null && resolvedFreshObjectNodes.Contains(current.Id);
            bool ownerIsAuthenticatedRuntime = current != null && authenticatedRuntimeEntityNodes.Contains(current.Id);
            if (current == null || (!ownerIsFresh && !ownerIsAuthenticatedRuntime) ||
                !Objects.TryGetValue(current.Id, out object ownerObject) || ownerObject is not Entity ownerEntity ||
                !TryGetEntityListOwnerNode(current, out AkronReconstructionNode ownerEntityList) ||
                ownerEntityList.Id != targetEntityList.Id ||
                !Objects.TryGetValue(targetEntityList.Id, out object listObject) || listObject is not EntityList entityList) {
                return false;
            }
            IEnumerable<Entity> freshEntities = GetEntityListEntities(entityList);
            return (!targetIsFresh || freshEntities.Any(candidate => ReferenceEquals(candidate, targetEntity))) &&
                   (!ownerIsFresh || freshEntities.Any(candidate => ReferenceEquals(candidate, ownerEntity)));
        }

        private bool IsAuthenticatedFreshEntityOwnedNestedState(
            AkronReconstructionNode node,
            Type type
        ) {
            if (!type.IsClass || type.IsAbstract || type.IsGenericType ||
                typeof(Entity).IsAssignableFrom(type) || typeof(Component).IsAssignableFrom(type) ||
                typeof(Renderer).IsAssignableFrom(type) || typeof(Delegate).IsAssignableFrom(type) ||
                typeof(IDisposable).IsAssignableFrom(type) ||
                type.GetMethod("Finalize", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) != null) {
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
                if (typeof(Entity).IsAssignableFrom(ownerType)) {
                    if (type.DeclaringType != ownerType ||
                        child.ParentNodeId != current.Id || child.ParentKind != "field" ||
                        !resolvedFreshObjectNodes.Contains(current.Id) ||
                        !Objects.TryGetValue(current.Id, out object ownerObject) ||
                        ownerObject is not Entity) {
                        return false;
                    }

                    FieldInfo field = ResolveField(
                        child.ParentDeclaringTypeName,
                        child.ParentFieldName,
                        child.Path);
                    bool ownsValue = field.FieldType == type ||
                                     field.FieldType.IsArray && field.FieldType.GetElementType() == type ||
                                     IsSupportedCollectionType(field.FieldType) &&
                                     field.FieldType.GetGenericArguments().Contains(type);
                    return field.DeclaringType.IsAssignableFrom(ownerType) && ownsValue;
                }
                if (typeof(Component).IsAssignableFrom(ownerType) ||
                    typeof(Renderer).IsAssignableFrom(ownerType) ||
                    !nodes.TryGetValue(current.ParentNodeId, out parent)) {
                    return false;
                }
                child = current;
                current = parent;
            }
            return false;
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

        private bool IsAuthenticatedGeneratedEntityOwnedState(
            AkronReconstructionNode node,
            Type type
        ) {
            if (node.ParentKind != "field" || !type.IsClass || type.IsAbstract || type.IsGenericType ||
                typeof(Entity).IsAssignableFrom(type) || typeof(Component).IsAssignableFrom(type) ||
                typeof(Renderer).IsAssignableFrom(type) || typeof(Delegate).IsAssignableFrom(type) ||
                typeof(IDisposable).IsAssignableFrom(type) ||
                type.GetMethod(
                    "Finalize",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) != null ||
                (!resolvedFreshObjectNodes.Contains(node.ParentNodeId) &&
                 !authenticatedRuntimeEntityNodes.Contains(node.ParentNodeId)) ||
                !nodes.TryGetValue(node.ParentNodeId, out AkronReconstructionNode ownerNode) ||
                !Objects.TryGetValue(ownerNode.Id, out object ownerObject) || ownerObject is not Entity ownerEntity ||
                !TryGetEntityListOwnerNode(ownerNode, out AkronReconstructionNode entityListNode) ||
                !Objects.TryGetValue(entityListNode.Id, out object listObject) || listObject is not EntityList entityList) {
                return false;
            }

            Type ownerType = ResolveType(ownerNode.TypeName, ownerNode.Path);
            if (ownerType.Assembly == typeof(Entity).Assembly &&
                !authenticatedRuntimeEntityNodes.Contains(ownerNode.Id)) {
                // A vanilla map-placed entity pairs by EntityID and its owned
                // state aliases the fresh object, so the population evidence
                // below is only for owners a clean load cannot recreate
                // one-for-one. That is not only mod entities: Celestial
                // Resort's clutter blocks are vanilla, generated at load, and
                // cross-reference each other, so the surplus the fresh room did
                // not build carries colliders whose only other evidence is a
                // structural path through another block's hash set - a path the
                // fresh room can honestly lack. A vanilla owner that proved
                // itself through the runtime-entity licence gets the same
                // owned-state licence a generated mod entity gets.
                return false;
            }
            FieldInfo field = ResolveField(
                node.ParentDeclaringTypeName,
                node.ParentFieldName,
                node.Path);
            if (!field.DeclaringType.IsAssignableFrom(ownerType) ||
                !field.FieldType.IsAssignableFrom(type)) {
                return false;
            }

            // A generated mod entity can own a constructor-created object
            // through a base-typed field, such as Entity.Collider -> Hitbox.
            // Authenticate that concrete child from the exact fresh owner when
            // available, or from the same field on another fresh entity of the
            // same generated type when the saved owner was reconstructed.
            if (resolvedFreshObjectNodes.Contains(ownerNode.Id)) {
                return field.GetValue(ownerEntity)?.GetType() == type;
            }
            return GetFreshEntityTypes(entityListNode, entityList)
                .TryGetValue(ownerType, out List<Entity> freshOwnersOfType) &&
                freshOwnersOfType.Any(candidate => field.GetValue(candidate)?.GetType() == type);
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
                         .SelectMany(node => node.DelegateCallsOrNull ?? Enumerable.Empty<AkronReconstructionDelegateCall>())) {
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
                foreach (AkronReconstructionField field in parent.FieldsOrNull ?? Enumerable.Empty<AkronReconstructionField>()) {
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
                foreach (AkronReconstructionValue item in parent.ItemsOrNull ?? Enumerable.Empty<AkronReconstructionValue>()) {
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

            if (!TryGetSavedEntityId(target, out _) &&
                TryResolveFreshEntityByTypeOrdinal(
                    target,
                    targetType,
                    entityListNode,
                    entityList,
                    out matchedEntity)) {
                return true;
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

        private bool TryResolveFreshEntityByTypeOrdinal(
            AkronReconstructionNode target,
            Type targetType,
            AkronReconstructionNode entityListNode,
            EntityList freshEntityList,
            out Entity matchedEntity
        ) {
            matchedEntity = null;
            IndexEntityListTypeOrdinals(entityListNode);
            if (!entityListTypeOrdinals.TryGetValue(
                    target.Id,
                    out (int Ordinal, int Count) savedTypeOrdinal)) {
                return false;
            }

            // Map entities without an EntityID still have deterministic EntityList
            // order. Match their ordinal among the same concrete type only when
            // the saved and fresh populations are equal, so runtime additions or
            // removals fail closed instead of shifting identity.
            Dictionary<Type, List<Entity>> freshTypes = GetFreshEntityTypes(entityListNode, freshEntityList);
            if (!freshTypes.TryGetValue(targetType, out List<Entity> freshTypeEntities) ||
                freshTypeEntities.Count != savedTypeOrdinal.Count) {
                return false;
            }
            Entity candidate = freshTypeEntities[savedTypeOrdinal.Ordinal];
            if (freshOwners.ContainsKey(candidate) ||
                (freshFieldAliasReservations.TryGetValue(candidate, out int reservedNodeId) &&
                 reservedNodeId != target.Id)) {
                return false;
            }
            matchedEntity = candidate;
            return true;
        }

        private Dictionary<Type, List<Entity>> GetFreshEntityTypes(
            AkronReconstructionNode entityListNode,
            EntityList freshEntityList
        ) {
            if (!freshEntityTypesByEntityList.TryGetValue(
                    entityListNode.Id,
                    out Dictionary<Type, List<Entity>> freshTypes)) {
                freshTypes = GetEntityListEntities(freshEntityList)
                    .Where(candidate => candidate != null)
                    .GroupBy(candidate => candidate.GetType())
                    .ToDictionary(group => group.Key, group => group.ToList());
                freshEntityTypesByEntityList[entityListNode.Id] = freshTypes;
            }
            return freshTypes;
        }

        private void IndexEntityListTypeOrdinals(AkronReconstructionNode entityListNode) {
            if (!indexedEntityListTypeOrdinals.Add(entityListNode.Id)) {
                return;
            }
            AkronReconstructionValue savedEntitiesReference = FindReferenceField(entityListNode, "entities");
            if (savedEntitiesReference == null ||
                !nodes.TryGetValue(savedEntitiesReference.NodeId, out AkronReconstructionNode savedEntitiesNode)) {
                return;
            }
            AkronReconstructionValue savedStorageReference = FindReferenceField(savedEntitiesNode, "_items");
            if (savedStorageReference == null ||
                !nodes.TryGetValue(savedStorageReference.NodeId, out AkronReconstructionNode savedStorageNode) ||
                savedStorageNode.Kind != ArrayKind) {
                return;
            }

            Dictionary<Type, List<int>> nodeIdsByType = new Dictionary<Type, List<int>>();
            foreach (AkronReconstructionValue item in
                     savedStorageNode.ItemsOrNull ?? Enumerable.Empty<AkronReconstructionValue>()) {
                if (item?.Kind != ReferenceValueKind ||
                    !nodes.TryGetValue(item.NodeId, out AkronReconstructionNode itemNode)) {
                    continue;
                }
                Type itemType = ResolveType(itemNode.TypeName, itemNode.Path);
                if (!typeof(Entity).IsAssignableFrom(itemType)) {
                    continue;
                }
                if (!nodeIdsByType.TryGetValue(itemType, out List<int> typeNodeIds)) {
                    typeNodeIds = new List<int>();
                    nodeIdsByType[itemType] = typeNodeIds;
                }
                typeNodeIds.Add(item.NodeId);
            }

            foreach (List<int> typeNodeIds in nodeIdsByType.Values) {
                if (typeNodeIds.Distinct().Count() != typeNodeIds.Count) {
                    continue;
                }
                for (int ordinal = 0; ordinal < typeNodeIds.Count; ordinal++) {
                    entityListTypeOrdinals[typeNodeIds[ordinal]] = (ordinal, typeNodeIds.Count);
                }
            }
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
            AkronReconstructionField levelField = sourceIdNode.FieldsOrNull?.FirstOrDefault(field =>
                field.Name == nameof(EntityID.Level) && field.Value?.Kind == ScalarValueKind);
            AkronReconstructionField idField = sourceIdNode.FieldsOrNull?.FirstOrDefault(field =>
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
            if (entityListOwnerIds.TryGetValue(entityNode.Id, out int? cachedOwnerId)) {
                return cachedOwnerId.HasValue && nodes.TryGetValue(cachedOwnerId.Value, out entityListNode);
            }
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
                    entityListOwnerIds[entityNode.Id] = null;
                    return false;
                }
                entityListNode = candidate;
            }
            entityListOwnerIds[entityNode.Id] = entityListNode?.Id;
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
            return (node.FieldsOrNull ?? Enumerable.Empty<AkronReconstructionField>())
                .FirstOrDefault(field =>
                    string.Equals(field.Name, fieldName, StringComparison.Ordinal) &&
                    field.Value?.Kind == ReferenceValueKind)
                ?.Value;
        }

        private bool IsStructurallyAuthenticDelegateTarget(AkronReconstructionNode targetNode, Type targetType) {
            if (targetNode.ParentKind != "delegate" ||
                !nodes.TryGetValue(targetNode.ParentNodeId, out AkronReconstructionNode delegateNode) ||
                targetNode.ParentDelegateIndex < 0 ||
                targetNode.ParentDelegateIndex >= (delegateNode.DelegateCallsOrNull?.Count ?? 0)) {
                return false;
            }
            AkronReconstructionDelegateCall call = delegateNode.DelegateCallsOrNull[targetNode.ParentDelegateIndex];
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
                arrayParent is Array array && HasArrayIndex(array, delegateNode.ParentArrayIndicesOrNull)) {
                candidate = array.GetValue(delegateNode.ParentArrayIndicesOrNull.ToArray());
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
            if ((node.FieldsOrNull?.Count ?? 0) != expectedFields.Length || expectedFields.Any(expected =>
                    node.FieldsOrNull == null || !node.FieldsOrNull.Any(captured =>
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

        // What this key deliberately does not capture, because reviewers keep
        // reading its looseness as an oversight.
        //
        // It keeps the complete owner field chain, the target's type, and the
        // method. It drops the array index, and with it the identity of the
        // instance an array holds. Both are required, not tolerated:
        //
        // - Index. A clean load rebuilds the entity and component lists in an
        //   order the saved room does not have to agree with, so a saved
        //   callback at slot 3 has to authenticate against a fresh one at slot
        //   7. Restoring exact indices refuses those reloads, which is the
        //   false-refusal class this component keeps having to fix.
        // - Owner identity. The path is the only place identity could live, so
        //   two owners of one type inside one list are already indistinguishable
        //   once the index is gone. That is also the point: which instance holds
        //   a callback the room arms while it is played is decided by play, and
        //   a key that pinned it would refuse a legitimate saved frame.
        //
        // The cost of both is real and is not a bug in this method: a saved
        // document whose slot or owner no longer matches the fresh room is
        // accepted rather than refused.
        //
        // Keeping exact indices for StateMachine.ends and its three siblings
        // has been proposed twice as the one safe place to close that, on the
        // grounds that a state id is the slot's identity and no reload moves
        // it. It was built and measured, and it does not work. The document a
        // mod-set shift produces - saved ends[2] against fresh ends[1] - is
        // byte for byte the document a mod produces by calling the public
        // SetCallbacks during play to move that same callback from state 1 to
        // state 2. Refusing is right for the first and a false refusal for the
        // second, and the index alone cannot tell them apart, so an exact index
        // trades one silent wrong restore for one silent refusal of a valid
        // frame. An array index is not evidence of identity one level up
        // either, because Celeste hands out the first free slot in
        // TrailManager.snapshots and LightingRenderer.lights,
        // ColliderList.colliders compacts on removal, and Dictionary, Queue and
        // HashSet all move entries inside their backing arrays.
        //
        // The sibling names array is the coordinate that does tell those two
        // documents apart, and that is where the fix went. It is in
        // ValidateStateSlotAssignment below, not here. Four things it settled,
        // so nobody re-derives them:
        //
        // - The name cannot go in this key. This index records where the fresh
        //   room HAS the callback and a document asks where it WANTS it, and in
        //   both stories those are different slots. Rendering each side's own
        //   name accepts the shift and refuses the valid frame, which is
        //   backwards; rendering the fresh room's name at the document's index
        //   is the exact index again. The test that works is a comparison of
        //   the document's names[i] against the fresh room's names[i], at the
        //   one slot being written, beside this key rather than inside it. So
        //   this key stays wildcarded: a callback is allowed to have moved
        //   between slots both rooms agree about.
        // - It must sit on the array ELEMENT, not on the delegate. A document
        //   keeps one owner edge per node, so a callback object held both in a
        //   state slot and in an ordinary field - a cached mod lambda - records
        //   the field, and a check reading the delegate's own edge never sees
        //   the slot. Measured: that document restores into the wrong slot with
        //   a delegate-side check in place.
        // - Celeste publishes the coordinate everywhere it matters. Player,
        //   Seeker and AngryOshiro are the only StateMachines in the game and
        //   all three name every state through SetStateName at construction,
        //   immediately before mods add theirs through AddState.
        // - A name is mutable through the public SetStateName, so "names differ
        //   at slot i" cannot on its own tell a relabelled slot from a
        //   reinterpreted one. Two attempts to excuse the relabel with a
        //   delegate-equality test failed in opposite directions. What settled
        //   it was measurement rather than a third test: no published mod
        //   relabels after construction, so there is no carve-out and the rule
        //   refuses a relabel too. ValidateStateSlotAssignment carries the
        //   census and what it still leaves open.
        //
        // Two more omissions, which are unfinished rather than load-bearing:
        // the invocation count and the delegate's own runtime type. Neither can
        // make a restore wrong on its own, because both come from the document
        // rather than from this key, and CreateDelegate below rejects a method
        // the saved delegate type cannot bind. Constraining either one here is
        // not free: an invocation list grows while a room is played, and the
        // static and instance method evidence would still authenticate the same
        // call without consulting this key.
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
            List<object> allKeyMatches = freshResources.TryGetValue(node.ResourceKey, out List<object> matches)
                ? matches
                : new List<object>();
            List<object> keyMatches = allKeyMatches
                .Where(match => !freshOwners.ContainsKey(match))
                .ToList();
            if (keyMatches.Count == 1) {
                return keyMatches[0];
            }
            if (owner.areEquivalentLiveResources?.Invoke(type) == true && allKeyMatches.Count > 0) {
                // The key fully identifies immutable loader content for the
                // configured types. Prefer an unpaired wrapper, then share an
                // existing one if the saved frame has more wrappers than the
                // fresh room created.
                return keyMatches.FirstOrDefault() ?? allKeyMatches[0];
            }

            // A candidate a delegate hands back is authenticated the same way
            // wherever it came from: exact type, and its recomputed key must be
            // the saved key.
            bool MatchesSavedKey(object candidate) =>
                candidate != null &&
                candidate.GetType() == type &&
                string.Equals(node.ResourceKey, owner.GetTypedResourceKey(candidate), StringComparison.Ordinal);

            // An exact process-registry key is stronger than an approximate
            // owner path. Resolve it first so successful detached lookups do
            // not build structural paths, and cache misses for this restore.
            if (keyMatches.Count == 0) {
                if (!detachedLiveResources.TryGetValue(node.ResourceKey, out object detachedResource)) {
                    detachedResource = owner.resolveDetachedLiveResource?.Invoke(type, node.ResourceKey);
                    detachedLiveResources[node.ResourceKey] = detachedResource;
                }
                if (MatchesSavedKey(detachedResource)) {
                    return detachedResource;
                }
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
                //
                // That is true of a key built from a name the running process
                // made up, and false of one that names the resource. Capture
                // classified the saved key on the saved object and wrote the
                // answer onto the node, so a key this process cannot find is
                // either a resource this install does not have - refuse, the
                // caller's key comparison says so by name - or a label the
                // reload regenerated, which is what this path is for. The
                // candidate is still returned either way: the refusal that
                // follows names the key, which says more than "path
                // unavailable" would.
                matchedByStructuralPath = HasListStorageIndex(structuralPath) &&
                                          !node.PortableResourceKey;
                return structuralMatches[0];
            }
            if (keyMatches.Count > 1 || structuralMatches.Count > 1) {
                throw new AkronReconstructionException(
                    node.Path,
                    "fresh resource key is ambiguous;matches=" +
                    Math.Max(keyMatches.Count, structuralMatches.Count).ToString(CultureInfo.InvariantCulture) +
                    ";key=" + node.ResourceKey);
            }

            // Last resort, and only for a label. A resource its owner creates
            // on first use can be absent from every index above at once:
            // DustEdges builds its noise textures in BeforeRender, so a fresh
            // baseline that never rendered holds null at the anchor's owner
            // field, and the process registry lost the captured level's
            // instances when the map was exited. The owner delegate recreates
            // the equivalent wrapper - content the room regenerates on its own,
            // identity checked by the same key comparison the detached lookup
            // uses. A portable key is a name rather than a label, and a name
            // that resolves nowhere is a resource this install does not have,
            // so it keeps the refusal below.
            if (!node.PortableResourceKey) {
                object recreated = owner.recreateDetachedLiveResource?.Invoke(type, node.ResourceKey);
                if (MatchesSavedKey(recreated)) {
                    // Later nodes carrying this key pair with the same wrapper
                    // through the detached cache instead of recreating another.
                    detachedLiveResources[node.ResourceKey] = recreated;
                    return recreated;
                }
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
                    key.Append(wildcardListStorageIndices && IsCollectionStorageField(previous)
                        ? "[*]"
                        : "[" + string.Join(",", step.ArrayIndicesOrNull ?? Enumerable.Empty<int>()) + "]");
                }
                previous = step;
            }
            return key.ToString();
        }

        // Storage an index means nothing durable in. List`1 keeps _items in
        // insertion order, so a wildcarded index still says "one of this list's
        // slots" while a concrete one names a position two loads can honestly
        // disagree on. HashSet`1 and Dictionary`2 place _entries by per-process
        // hash codes - AkronHashIndex.Rebuild exists because those positions do
        // not survive a process change - so a concrete entry index is a fact
        // about the capturing process, never about the set, and it wildcards
        // for the same reason list indices do.
        private static bool IsCollectionStorageField(AkronReconstructionPathStep step) {
            if (step?.Kind != "field" || step.DeclaringTypeName == null) {
                return false;
            }
            if (string.Equals(step.FieldName, "_items", StringComparison.Ordinal)) {
                return step.DeclaringTypeName.StartsWith("System.Collections.Generic.List`1", StringComparison.Ordinal);
            }
            return string.Equals(step.FieldName, "_entries", StringComparison.Ordinal) &&
                   (step.DeclaringTypeName.StartsWith("System.Collections.Generic.HashSet`1", StringComparison.Ordinal) ||
                    step.DeclaringTypeName.StartsWith("System.Collections.Generic.Dictionary`2", StringComparison.Ordinal));
        }

        private static bool HasListStorageIndex(IEnumerable<AkronReconstructionPathStep> path) {
            AkronReconstructionPathStep previous = null;
            foreach (AkronReconstructionPathStep step in path ?? Enumerable.Empty<AkronReconstructionPathStep>()) {
                if (step.Kind == "array" && IsCollectionStorageField(previous)) {
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
                    ArrayIndices = step.ArrayIndicesOrNull is { Count: > 0 }
                        ? new List<int>(step.ArrayIndicesOrNull)
                        : null
                })
                .ToList();
            appended.Add(next);
            return appended;
        }

        public void ValidateAssignments() {
            foreach (AkronReconstructionNode node in document.Nodes.OrderBy(node => node.Id)) {
                if (node.Kind == AnchorKind || node.Kind == PersistentResourceKind ||
                    node.Kind == DelegateKind || node.Kind == EventInstanceKind ||
                    node.Kind == WeakReferenceKind) {
                    continue;
                }

                object target = Objects[node.Id];
                if (node.Kind == ArrayKind) {
                    ValidateArrayAssignments(node, (Array) target);
                    continue;
                }

                foreach (AkronReconstructionField savedField in
                         node.FieldsOrNull ?? Enumerable.Empty<AkronReconstructionField>()) {
                    if (IsDerivedCollectionVersionField(target.GetType(), savedField.Name)) {
                        continue;
                    }
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
            IReadOnlyList<AkronReconstructionValue> items =
                node.ItemsOrNull ?? (IReadOnlyList<AkronReconstructionValue>) Array.Empty<AkronReconstructionValue>();
            if (target.LongLength != items.Count) {
                throw new AkronReconstructionException(node.Path, "array item count differs");
            }

            Type elementType = target.GetType().GetElementType();
            int[] itemIndices = GetInitialArrayIndices(target);
            for (int index = 0; index < items.Count; index++) {
                AkronReconstructionValue savedItem = items[index];
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
                ValidateStateSlotAssignment(node, index, itemValue, itemPath);
                TrackDisplacedEventInstance(target.GetValue(itemIndices), itemValue);
                IncrementArrayIndices(target, itemIndices);
            }
            assignments.Add(() => {
                int[] assignmentIndices = GetInitialArrayIndices(target);
                for (int assignmentIndex = 0; assignmentIndex < items.Count; assignmentIndex++) {
                    target.SetValue(ResolveValue(items[assignmentIndex], node.Path), assignmentIndices);
                    IncrementArrayIndices(target, assignmentIndices);
                }
            });
        }

        // Monocle.StateMachine addresses its four callback arrays by state id,
        // and a state id is handed out by AddState in whatever order the
        // installed mods add states rather than declared, so the same state can
        // sit at a different id in two sessions. Writing a saved callback into
        // slot i of a freshly loaded machine is only faithful if slot i is the
        // same state in both rooms, and that is checkable because the machine
        // says so: the sibling names array names the state each slot belongs to.
        //
        // This is the pair two earlier passes could not separate with the index
        // alone, and it is why the structural delegate key above stays
        // wildcarded rather than keeping exact indices:
        //
        // - A mod set whose AddState order changed moves a state to another id.
        //   The document's slot then reads as a different state in the fresh
        //   room and the callback would land where this room never runs it. The
        //   names disagree at that slot, so it is refused.
        // - A mod calling the public SetCallbacks during play moves a callback
        //   between two slots both rooms agree about. The names agree, so it is
        //   accepted. An exact index refused this, on a document byte for byte
        //   identical to the first.
        //
        // Two writes are deliberately not refused, because neither can misplace
        // anything:
        //
        // - A slot the fresh machine does not have at all. A mod that calls
        //   AddState during play leaves the saved frame holding a state a clean
        //   load has not added yet; it is adding a state, not renaming one.
        // - Every other array in the game. Nothing else says what its slots
        //   mean, so there is nothing to check and the wildcard stands.
        //
        // There is deliberately no third case for a write that changes nothing.
        // An earlier version carried one, to spare a state a mod had merely
        // relabelled mid-play through the public SetStateName while its
        // callbacks stayed put. Deciding "this write changes nothing" needs an
        // equality test over two delegates whose targets the restore rebuilt,
        // and there is none that is both sound and complete: by reference it
        // refuses an ordinary mod closure, and by target type and method it
        // admits a shifted slot whenever the fresh slot already runs that method
        // on that type. Both were reproduced. The carve-out is gone because the
        // mutation it protected does not occur: a census of all 6,015 mods in
        // Everest's published database on 2026-08-15 read every shipped
        // assembly and found exactly one caller of SetStateName - Aqua, inside
        // an On.Celeste.Player.ctor hook, which is construction - and Celeste's
        // own Player, Seeker and AngryOshiro name their states in constructors
        // and never again. The only post-construction relabel found anywhere is
        // Magedeline's unpublished Desolo Zantas (MaggyHelper, ontalkend), whose
        // sole release asset has zero downloads and which is absent from
        // everest_update.yaml. If it or anything like it ships, this rule
        // refuses those frames rather than restoring them wrong.
        //
        // Every frame this can refuse wrongly has one shape: a state whose name
        // differs between the two sessions while its callbacks never moved. A
        // name comes from construction - the maxStates the constructor was
        // given, the AddState arguments, and SetStateName - so on an install
        // whose game and mod binaries did not change between setting the slot
        // and loading it, both sessions build the same names and this refuses
        // nothing. It bites when a mod update renames a state, or migrates one
        // from the unnamed reflection idiom to AddState, and it would bite if a
        // mod named a state from something that varies per run. Of the 66 mod
        // repositories read for the census, every named AddState passes a string
        // literal or a method returning one, so the per-run case was not found;
        // it is unmeasured for published mods with no source. Across an update a
        // rename and a reinterpretation are genuinely indistinguishable, and a
        // reinterpretation is the silent wrong restore this exists to stop, so
        // refusing is the conservative call rather than an accident.
        //
        // The check sits on the array element rather than on the delegate,
        // because a document keeps one canonical owner edge per node: one
        // callback object held both in a state slot and in an ordinary field -
        // which is what a cached mod lambda is - records the field, and a check
        // reading the delegate's own edge would never see the slot.
        //
        // A slot neither side names has no name to compare, and a name
        // comparison alone reads that as agreement. The pre-2023 reflection
        // idiom produces exactly that: a mod-local extension method resizes
        // begins, updates, ends and coroutines and never names, so the state it
        // adds is unnamed in both sessions and a shift among such slots used to
        // be accepted silently. Everest's own patch expects unnamed slots -
        // GetStateName hands back the state id as a string for one - and the
        // idiom is still shipped by XaphanHelper, BrokemiaHelper, JackalHelper,
        // IsaGrabBag and PrismaticHelper among others, so refusing every write
        // into an unnamed slot would cost all of their players every slot on
        // every load, shift or no shift.
        //
        // So an unnamed slot falls back to the only other thing the machine says
        // about it: the code it runs there, updates[slot] and coroutines[slot],
        // taken by declaring type and method name. A shift moves the slot from
        // one mod's state to another's and those differ; a mod set that did not
        // change wires the same methods in both sessions and this refuses
        // nothing. The condition is "neither side names this slot" rather than
        // the shorter names array those helpers leave behind, because the two
        // are not the same set: Seeker asks for ten states and names eight,
        // AngryOshiro asks for ten and names six, so a machine can have unnamed
        // slots inside a names array of full length, and a mod that fills one of
        // those through the public SetCallbacks has the same defect with no
        // short names to show for it.
        //
        // What the fallback costs: a mod that changes an unnamed state's own
        // update or coroutine while the room is played is refused, because
        // without a name there is nothing to tell that change from the slot
        // having become another mod's state, and the second is the silent wrong
        // restore. Rewiring begins and ends is not affected, and neither is
        // moving one of them between two unnamed slots - that is the same in-play
        // move the named half accepts above, and the drivers still pin both
        // slots while it happens.
        //
        // Three wrong restores stay open, none reachable through any published
        // mod. Two were reproduced when the named half landed. A slot whose four
        // saved callbacks are all null is never checked, because the check only
        // runs where a value is written, so a machine that lost a state like
        // that can still take a callback at the wrong id - and no driver reaches
        // it either, for the same reason. And SavedStateSlotArrays below keeps
        // one owner per array node, so two machines sharing one callback array -
        // which needs reflection, the public API cannot do it - let the later
        // one lend the earlier one its coordinates; keeping every owner and
        // refusing when any disagrees is the fix if a mod is ever seen to alias
        // them. The third is this fallback's own floor and is reasoned rather
        // than reproduced: two unnamed slots that both hold no update and no
        // coroutine have the same empty driver, so a shift between them is
        // accepted and their begin and end callbacks swap. A state like that
        // runs nothing while it is current and never advances out of itself.
        private void ValidateStateSlotAssignment(
            AkronReconstructionNode arrayNode,
            int slot,
            object restoredValue,
            string itemPath
        ) {
            if (!SavedStateSlotArrays().TryGetValue(arrayNode.Id, out AkronReconstructionNode machineNode) ||
                !Objects.TryGetValue(machineNode.Id, out object freshMachine) ||
                !freshStateSlots.TryGetValue(freshMachine, out (string Name, string Driver)[] freshSlots) ||
                slot >= freshSlots.Length) {
                return;
            }
            string savedName = SavedStateSlotName(machineNode, slot);
            string freshName = freshSlots[slot].Name;
            if (freshName == null && savedName == null) {
                if (string.Equals(
                        freshSlots[slot].Driver,
                        SavedStateSlotDriver(machineNode, slot),
                        StringComparison.Ordinal)) {
                    return;
                }
            } else if (string.Equals(freshName, savedName, StringComparison.Ordinal)) {
                return;
            }
            // Name the mod the same way a refused callback does, from the type
            // that declares the method, so the load message can still say whose
            // state this was.
            string refusedTypeName = restoredValue is Delegate refusedCall
                ? refusedCall.Method?.DeclaringType?.AssemblyQualifiedName ?? string.Empty
                : string.Empty;
            throw new AkronReconstructionException(
                itemPath,
                "saved state slot is a different state in the fresh room" +
                ";state=" + (savedName ?? "<unnamed>") +
                ";slot=" + slot.ToString(CultureInfo.InvariantCulture),
                refusedTypeName);
        }

        // Which document nodes are a state machine's callback array, and which
        // machine owns each. Built from the machine's own field references
        // rather than from the array node's owner edge, for the same reason the
        // check above sits on the element: an owner edge records only the first
        // place the capture reached a node from.
        private Dictionary<int, AkronReconstructionNode> SavedStateSlotArrays() {
            if (savedStateSlotArrays != null) {
                return savedStateSlotArrays;
            }
            savedStateSlotArrays = new Dictionary<int, AkronReconstructionNode>();
            foreach (AkronReconstructionNode machineNode in nodes.Values) {
                foreach (AkronReconstructionField field in machineNode.FieldsOrNull ?? Enumerable.Empty<AkronReconstructionField>()) {
                    if (field?.Value?.Kind == ReferenceValueKind &&
                        string.Equals(field.DeclaringTypeName, StateMachineTypeName, StringComparison.Ordinal) &&
                        Array.IndexOf(StateMachineCallbackFieldNames, field.Name) >= 0) {
                        savedStateSlotArrays[field.Value.NodeId] = machineNode;
                    }
                }
            }
            return savedStateSlotArrays;
        }

        private string SavedStateSlotName(AkronReconstructionNode machineNode, int slot) {
            AkronReconstructionValue names = FindReferenceField(machineNode, "names");
            if (names == null ||
                !nodes.TryGetValue(names.NodeId, out AkronReconstructionNode namesNode) ||
                slot < 0 ||
                namesNode.ItemsOrNull == null ||
                slot >= namesNode.ItemsOrNull.Count) {
                return null;
            }
            AkronReconstructionValue name = namesNode.ItemsOrNull[slot];
            return name?.Kind == ScalarValueKind ? name.Scalar : null;
        }

        // The code the saved machine ran at one slot, read out of the document
        // rather than off any live object, and written the way
        // FreshStateSlotDriver writes the fresh room's answer. No such array, no
        // such index or a null callback all contribute nothing here, and the
        // fresh machine holding nothing there contributes nothing too, so the
        // two match.
        //
        // One shape does not match, and it refuses rather than accepts: a
        // callback whose method is generated at runtime. CaptureDelegate records
        // no call for one unless it can name the detour behind it, while the
        // fresh machine still spells out whatever it holds, so the slot is
        // refused. That takes a state callback built by MonoMod or by
        // DynamicMethod rather than passed to SetCallbacks as a method or a
        // lambda, which is not a shape any mod has been seen to produce, and
        // refusing is the direction this rule is for.
        private string SavedStateSlotDriver(AkronReconstructionNode machineNode, int slot) {
            StringBuilder driver = new StringBuilder();
            foreach (string driverFieldName in StateMachineDriverFieldNames) {
                driver.Append(driverFieldName).Append('=');
                AkronReconstructionValue callbacks = FindReferenceField(machineNode, driverFieldName);
                if (callbacks != null &&
                    nodes.TryGetValue(callbacks.NodeId, out AkronReconstructionNode callbacksNode) &&
                    slot >= 0 &&
                    callbacksNode.ItemsOrNull != null &&
                    slot < callbacksNode.ItemsOrNull.Count &&
                    callbacksNode.ItemsOrNull[slot]?.Kind == ReferenceValueKind &&
                    nodes.TryGetValue(callbacksNode.ItemsOrNull[slot].NodeId, out AkronReconstructionNode callbackNode)) {
                    foreach (AkronReconstructionDelegateCall call in
                             callbackNode.DelegateCallsOrNull ?? Enumerable.Empty<AkronReconstructionDelegateCall>()) {
                        driver.Append(call.DeclaringTypeName)
                            .Append('.')
                            .Append(call.MethodName)
                            .Append('+');
                    }
                }
                driver.Append(';');
            }
            return driver.ToString();
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
                    if (current is not Array array || !HasArrayIndex(array, step.ArrayIndicesOrNull?.ToArray())) {
                        return null;
                    }
                    current = array.GetValue(step.ArrayIndicesOrNull.ToArray());
                } else {
                    throw new AkronReconstructionException(errorPath, "fresh path step is unsupported");
                }
            }
            return current;
        }

        private object ResolveFreshObject(AkronReconstructionNode node) {
            if (node.FreshPathOrNull != null && node.FreshPathOrNull.Count > 0) {
                return ResolveFreshPath(node.FreshPathOrNull, node.Path);
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
                    return parent is Array array && HasArrayIndex(array, node.ParentArrayIndicesOrNull)
                        ? array.GetValue(node.ParentArrayIndicesOrNull.ToArray())
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
                if (current.FreshPathOrNull != null && current.FreshPathOrNull.Count > 0) {
                    List<AkronReconstructionPathStep> path = ClonePathSteps(current.FreshPathOrNull);
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
                        ArrayIndices = new List<int>(current.ParentArrayIndicesOrNull ?? Enumerable.Empty<int>())
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
                        ArrayIndices = new List<int>(current.ParentArrayIndicesOrNull ?? Enumerable.Empty<int>())
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
                    ArrayIndices = step.ArrayIndicesOrNull is { Count: > 0 }
                        ? new List<int>(step.ArrayIndicesOrNull)
                        : null
                })
                .ToList();
        }

        private object CreateDelegate(AkronReconstructionNode node) {
            Type delegateType = ResolveType(node.TypeName, node.Path);
            IReadOnlyList<AkronReconstructionDelegateCall> calls =
                node.DelegateCallsOrNull ?? (IReadOnlyList<AkronReconstructionDelegateCall>) Array.Empty<AkronReconstructionDelegateCall>();
            Delegate combined = null;
            for (int index = 0; index < calls.Count; index++) {
                AkronReconstructionDelegateCall call = calls[index];
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
                    if (!authentic && target != null) {
                        authentic = IsAuthenticatedDirectIteratorClosureDelegateCall(node, call, target, method);
                    }
                    if (!authentic) {
                        // What the fresh room does not have here is the callback, not the
                        // delegate field's own type, so the refusal is reported against the
                        // type that declares the method. For a mod hook's lambda that is the
                        // mod's closure type, which is what names the mod in the load message.
                        throw new AkronReconstructionException(
                            node.Path,
                            "delegate method is not authentic to the fresh room;type=" +
                            (method.DeclaringType?.FullName ?? "<unknown>") +
                            ";method=" + method.Name,
                            method.DeclaringType?.AssemblyQualifiedName ?? string.Empty);
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

        // A lambda hoisted from an owner-proved iterator: the method is the
        // compiler-generated closure's own, and the closure already carried the
        // direct-proof licence, so the callback is the iterator's own code
        // rather than anything a census could vouch for. CrushBlock's
        // AttackSequence hands exactly this to Alarm.Set while a Kevin attacks,
        // and an idle fresh room has no such callback anywhere. Deferred
        // iterators stay out for the withdrawal reason the node licence names.
        //
        // The callback must also stay inside the entity that owns the routine:
        // the delegate node's own document chain has to reach the iterator's
        // captured owner before any other entity, so a crafted document cannot
        // relocate the lambda into an unrelated delegate field.
        private bool IsAuthenticatedDirectIteratorClosureDelegateCall(
            AkronReconstructionNode delegateNode,
            AkronReconstructionDelegateCall call,
            object targetObject,
            MethodInfo method
        ) {
            if (call.Target?.Kind != ReferenceValueKind ||
                method.IsStatic ||
                method.DeclaringType != targetObject.GetType() ||
                method.DeclaringType?.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) != true ||
                !nodes.TryGetValue(call.Target.NodeId, out AkronReconstructionNode targetNode) ||
                !Objects.TryGetValue(targetNode.Id, out object restoredTarget) ||
                !ReferenceEquals(restoredTarget, targetObject) ||
                !authenticatedIteratorClosureNodes.Contains(targetNode.Id) ||
                deferredProvisionalIteratorIds.Contains(targetNode.ParentNodeId) ||
                !nodes.TryGetValue(targetNode.ParentNodeId, out AkronReconstructionNode iteratorNode)) {
                return false;
            }
            AkronReconstructionValue iteratorOwner = FindReferenceField(iteratorNode, "<>4__this");
            if (iteratorOwner == null) {
                return false;
            }
            AkronReconstructionNode current = delegateNode;
            while (current != null) {
                if (current.Id == iteratorOwner.NodeId) {
                    return true;
                }
                if (typeof(Entity).IsAssignableFrom(ResolveType(current.TypeName, current.Path))) {
                    return false;
                }
                current = nodes.TryGetValue(current.ParentNodeId, out AkronReconstructionNode parent)
                    ? parent
                    : null;
            }
            return false;
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
            if (elementType == null || node.ArrayLengthsOrNull == null || node.ArrayLowerBoundsOrNull == null ||
                node.ArrayLengthsOrNull.Count == 0 || node.ArrayLengthsOrNull.Count != node.ArrayLowerBoundsOrNull.Count) {
                throw new AkronReconstructionException(path, "array shape is invalid");
            }
            if (node.ArrayLengthsOrNull.Count > MaxRestoredArrayRank || node.ArrayLengthsOrNull.Count != arrayType.GetArrayRank()) {
                throw new AkronReconstructionException(path, "array rank exceeds the supported limit");
            }
            long elementCount = 1;
            for (int dimension = 0; dimension < node.ArrayLengthsOrNull.Count; dimension++) {
                int length = node.ArrayLengthsOrNull[dimension];
                int lowerBound = node.ArrayLowerBoundsOrNull[dimension];
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
            if (node.PackedPrimitiveArrayBytes == null && elementCount != (node.ItemsOrNull?.Count ?? 0)) {
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
            return Array.CreateInstance(elementType, node.ArrayLengthsOrNull.ToArray(), node.ArrayLowerBoundsOrNull.ToArray());
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
            if (array == null || node.ArrayLengthsOrNull == null || node.ArrayLowerBoundsOrNull == null ||
                node.ArrayLengthsOrNull.Count != array.Rank || node.ArrayLowerBoundsOrNull.Count != array.Rank) {
                return false;
            }
            for (int dimension = 0; dimension < array.Rank; dimension++) {
                if (array.GetLength(dimension) != node.ArrayLengthsOrNull[dimension] ||
                    array.GetLowerBound(dimension) != node.ArrayLowerBoundsOrNull[dimension]) {
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
                } else if (node.Kind == WeakReferenceKind) {
                    VerifyWeakReference(node, current);
                } else if (node.Kind == ArrayKind) {
                    VerifyArray(node, (Array) current);
                } else {
                    VerifyObject(node, current);
                }
            }
        }

        private void VerifyObject(AkronReconstructionNode node, object current) {
            foreach (AkronReconstructionField savedField in
                     node.FieldsOrNull ?? Enumerable.Empty<AkronReconstructionField>()) {
                if (IsMasked(savedField.Path)) {
                    continue;
                }
                if (IsDerivedMembershipField(current.GetType(), savedField.Name)) {
                    continue;
                }
                if (IsDerivedCollectionVersionField(current.GetType(), savedField.Name)) {
                    continue;
                }
                FieldInfo field = ResolveField(savedField.DeclaringTypeName, savedField.Name, savedField.Path);
                if (IsRewrittenHashIndexField(node, current, field)) {
                    continue;
                }
                VerifyValue(savedField.Value, field.GetValue(current), savedField.Path);
            }
        }

        // The scalar fields AkronHashIndex.Rebuild wrote, and only those. An
        // entry is not skipped wholesale: the rebuild steps over the free slots,
        // and the free chain runs through next, so a free slot keeps the values
        // the document holds and keeps being compared against them. Only a slot
        // the document calls live and the restore also left live has its hash
        // and its chain link skipped; a slot that changed sides fails at its own
        // path like any other field.
        // Keyed on the type that declares the field rather than on the object's
        // runtime type, so a mod that derives from Dictionary and gives itself a
        // field of the same name keeps that field verified.
        private bool IsRewrittenHashIndexField(
            AkronReconstructionNode node,
            object current,
            FieldInfo field
        ) {
            if (!AkronHashIndex.IsDerivedIndexField(field.DeclaringType, field.Name)) {
                return false;
            }
            if (!AkronHashIndex.IsHashEntryType(field.DeclaringType)) {
                return true;
            }
            // Both sides have to agree the slot is live. A slot the document
            // calls live and the restore left free would be stepped over by the
            // rebuild and by every enumerator, so it is compared rather than
            // skipped and fails at its own path.
            return SavedHashEntryIsLive(node) && AkronHashIndex.IsLiveHashEntry(current);
        }

        private static bool SavedHashEntryIsLive(AkronReconstructionNode node) {
            foreach (AkronReconstructionField savedField in
                     node.FieldsOrNull ?? Enumerable.Empty<AkronReconstructionField>()) {
                if (savedField.Name is "next" or "Next") {
                    return savedField.Value?.Kind == ScalarValueKind &&
                           int.TryParse(
                               savedField.Value.Scalar,
                               NumberStyles.Integer,
                               CultureInfo.InvariantCulture,
                               out int chain) &&
                           chain >= -1;
                }
            }
            return false;
        }

        // A bucket array holds nothing but positions, so its contents are this
        // process's after the rebuild. Its length is not derived - it is what the
        // container's bucket count is - and the rebuild never replaces the array,
        // so the field that points at it and the length both keep being compared.
        // Only the direct parent is consulted: the array's elements are positions
        // or chain heads and have no children of their own to reach.
        private bool IsDerivedHashIndexArrayNode(AkronReconstructionNode node) {
            return node.ParentKind == "field" &&
                   AkronHashIndex.IsDerivedIndexArrayFieldName(node.ParentFieldName) &&
                   !string.IsNullOrEmpty(node.ParentDeclaringTypeName) &&
                   AkronHashIndex.IsDerivedIndexArrayField(
                       ResolveType(node.ParentDeclaringTypeName, node.Path),
                       node.ParentFieldName);
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
            if (IsDerivedHashIndexArrayNode(node)) {
                if (node.PackedPrimitiveArrayBytes != null) {
                    if (!CanPackPrimitiveArray(current) ||
                        Buffer.ByteLength(current) != node.PackedPrimitiveArrayBytes.Length) {
                        throw new AkronReconstructionException(node.Path, "packed primitive array size differs");
                    }
                } else if (current.LongLength != (node.ItemsOrNull?.Count ?? 0)) {
                    throw new AkronReconstructionException(node.Path, "array item count differs");
                }
                return;
            }
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
            IReadOnlyList<AkronReconstructionValue> items =
                node.ItemsOrNull ?? (IReadOnlyList<AkronReconstructionValue>) Array.Empty<AkronReconstructionValue>();
            if (current.LongLength != items.Count) {
                throw new AkronReconstructionException(node.Path, "array item count differs");
            }
            int[] itemIndices = GetInitialArrayIndices(current);
            for (int index = 0; index < items.Count; index++) {
                AkronReconstructionValue expected = items[index];
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

        private void VerifyWeakReference(AkronReconstructionNode node, object current) {
            (object currentTarget, bool currentTrackResurrection) = ReadWeakReference(current);
            // A scalar target was boxed into the constructor and nothing holds
            // that box strongly, so a collection between construction and this
            // check legitimately leaves the weak reference dead. Every other
            // target is a node object the restore still holds.
            if (node.ItemsOrNull[0]?.Kind != ScalarValueKind || currentTarget != null) {
                VerifyValue(node.ItemsOrNull[0], currentTarget, node.Path + ".<weak-target>");
            }
            if (current is WeakReference && currentTrackResurrection != (DecodeScalar(node.ItemsOrNull[1], node.Path) is true)) {
                throw new AkronReconstructionException(node.Path, "weak reference resurrection flag differs");
            }
        }

        private void VerifyDelegate(AkronReconstructionNode node, Delegate current) {
            Delegate[] calls = current.GetInvocationList();
            IReadOnlyList<AkronReconstructionDelegateCall> expectedCalls =
                node.DelegateCallsOrNull ?? (IReadOnlyList<AkronReconstructionDelegateCall>) Array.Empty<AkronReconstructionDelegateCall>();
            if (calls.Length != expectedCalls.Count) {
                throw new AkronReconstructionException(node.Path, "delegate invocation count differs");
            }
            for (int index = 0; index < calls.Length; index++) {
                AkronReconstructionDelegateCall expected = expectedCalls[index];
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
            foreach (AkronReconstructionField savedField in
                     node.FieldsOrNull ?? Enumerable.Empty<AkronReconstructionField>()) {
                FieldInfo field = ResolveField(savedField.DeclaringTypeName, savedField.Name, path);
                if (IsRewrittenHashIndexField(node, current, field)) {
                    continue;
                }
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
        Type[] parameterTypes;
        if (call.ParameterTypeNames == null || call.ParameterTypeNames.Count == 0) {
            parameterTypes = Type.EmptyTypes;
        } else {
            parameterTypes = new Type[call.ParameterTypeNames.Count];
            for (int index = 0; index < parameterTypes.Length; index++) {
                parameterTypes[index] = ResolveType(call.ParameterTypeNames[index], path);
            }
        }
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
            ParameterTypeNames = call.HookTargetParameterTypeNames
        }, path);
    }

    private static bool CanPackPrimitiveArray(Array array) {
        Type elementType = array?.GetType().GetElementType();
        return CanPackPrimitiveElementType(elementType);
    }

    private static bool CanPackPrimitiveElementType(Type elementType) {
        return IsPersistablePrimitive(elementType);
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

}

// Which question a refusal answers, which is what decides the sentence the player gets.
//
// AkronStartPosRefusal resolves the refused type to the mod that owns it. That is the
// right axis for a refusal about an object the fresh room cannot supply, because the mod
// that ships the object is the mod whose setup changed. It is the wrong axis for a
// refusal about the room itself: an entity the map has stopped placing says nothing
// about who wrote that entity's type, so attribution lands on Celeste for a vanilla
// entity and asks for a bug report about a refusal working exactly as designed.
//
// The kind travels beside the refused type from the throw all the way to the message,
// rather than being read back out of the reason text, for the same reason the type does:
// the reason text is written for whoever reads the log and its wording is free to change.
internal enum AkronReconstructionRefusalKind {
    // The fresh room cannot supply an object the saved state names. The refused type is
    // that object's, and the mod that owns the type is the one the player has to look at.
    SavedObject = 0,

    // This room's map data no longer places an entity the saved state names. The refused
    // type is that entity's and it is not what is wrong - the map is, and a map is not
    // something a player checks a settings menu for. Setting the slot again against the
    // map as it stands now is the whole fix, and it always works.
    ChangedMap
}

// Top-level rather than nested and private because it can escape the graph: a type that
// will not load is refused while the document is being walked outside Restore's own
// catch, and the StartPos load reports that through its outermost handler. That handler
// needs RefusedTypeName to tell the player which mod is missing.
internal sealed class AkronReconstructionException : Exception {
    public AkronReconstructionException(string path, string message)
        : this(path, message, string.Empty) {
    }

    // refusedTypeName is the assembly-qualified name of the saved object the refusal
    // is about. It is carried as data rather than parsed back out of the message
    // text: the load-failure sentence a player reads is built from it, while the
    // message keeps the graph path and the authenticity flags for the log.
    //
    // refusedKind defaults to SavedObject because that is what a refusal is unless it
    // says otherwise: the fresh room did not supply what the document asked for. A
    // refusal that is about the room rather than about the object has to say so.
    public AkronReconstructionException(
        string path,
        string message,
        string refusedTypeName,
        AkronReconstructionRefusalKind refusedKind = AkronReconstructionRefusalKind.SavedObject
    )
        : base(message) {
        Path = string.IsNullOrWhiteSpace(path) ? "$" : path;
        RefusedTypeName = refusedTypeName ?? string.Empty;
        RefusedKind = refusedKind;
    }

    public string Path { get; }
    public string RefusedTypeName { get; }
    public AkronReconstructionRefusalKind RefusedKind { get; }
}

// What one prewarm read did with its slot. A bool could not tell a slot that was
// already in the cache apart from one that failed, so a queue re-issued over a warm
// map logged "warmed 0 of 3" while working perfectly.
internal enum AkronPrewarmOutcome {
    Stored,
    AlreadyCached,
    BudgetFull,
    NotStored
}

internal static class AkronStartPosReconstruction {
    private static readonly FieldInfo VirtualContentAssetsField = typeof(VirtualContent).GetField(
        "assets",
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(VirtualContent).FullName, "assets");
    private static readonly FieldInfo VirtualTexturePathField = typeof(VirtualTexture).GetField(
        "<Path>k__BackingField",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(VirtualTexture).FullName, "<Path>k__BackingField");
    private static readonly FieldInfo VirtualAssetNameField = typeof(VirtualAsset).GetField(
        "<Name>k__BackingField",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(VirtualAsset).FullName, "<Name>k__BackingField");
    private static readonly FieldInfo VirtualAssetWidthField = typeof(VirtualAsset).GetField(
        "<Width>k__BackingField",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(VirtualAsset).FullName, "<Width>k__BackingField");
    private static readonly FieldInfo VirtualAssetHeightField = typeof(VirtualAsset).GetField(
        "<Height>k__BackingField",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(VirtualAsset).FullName, "<Height>k__BackingField");
    // The hard limit on one snapshot, hostile or not. Raised from 192 MiB, which was
    // 13% below the largest snapshot a real install has produced: 231,081,666 bytes
    // decompressed, one of 17 measured off the test box. A limit under the largest
    // real measurement is not a limit on hostile input at all, it is a slot that
    // cannot be loaded, which is what the released build did with that file.
    // 384 MiB is 1.74x that largest real snapshot, so the headroom is against a
    // modded map that grows rather than against the measurement itself.
    // It is also the ceiling on what one cold load can cost: at the 3.8x
    // of process RSS per decompressed byte measured in game, a document at this
    // limit is about 1.4 GiB of RSS, which is below what the whole prewarm cache is
    // allowed to hold (MaxPrewarmedSnapshotBytes, 512 MiB, ~1.9 GiB of RSS).
    internal const long MaxDecompressedSnapshotBytes = 384L * 1024L * 1024L;
    // Also the name the save backup uses to leave this folder out of a backup archive,
    // so the two cannot drift apart.
    internal const string SnapshotDirectoryName = "AkronStartPos";
    // Persistence capture runs on its worker while restore owns live resources
    // on the game thread. Separate graph instances keep those lifecycles from
    // sharing mutable resource ownership.
    private static readonly AkronReconstructionGraph CaptureGraph = CreateGraph();
    private static readonly AkronReconstructionGraph RestoreGraph = CreateGraph();
    // Prewarm deserializes on a worker thread while the game thread can be in the
    // middle of a restore on RestoreGraph. Deserialize touches no live object, but
    // it gets its own instance for the same reason capture does: no graph instance
    // is ever entered from two threads at once.
    private static readonly AkronReconstructionGraph PrewarmGraph = CreateGraph();

    private static AkronReconstructionGraph CreateGraph() {
        return new AkronReconstructionGraph(
            IsLiveResourceType,
            GetLiveResourceKey,
            new AkronVirtualRenderTargetResourceAdapter(),
            ResolveDetachedLiveResource,
            areEquivalentLiveResources: AreEquivalentLiveResources,
            hasPortableLiveResourceKey: HasPortableLiveResourceKey,
            getMapPlacedEntityIds: GetMapPlacedEntityIds,
            recreateDetachedLiveResource: RecreateDetachedLiveResource,
            isAdditionalLiveResource: IsLiveHookOwner,
            hasDeferredDetachedLiveResourceKey: HasDeferredHookOwnerKey);
    }

    // Does GetLiveResourceKey name this resource, or label this instance? The two
    // read the same way and mean opposite things when a second process cannot find
    // the key. A name that is missing means the resource is missing: a sort this
    // install cannot open, a texture file this install does not have, a type from an
    // assembly this install did not load. A label that is missing means nothing at
    // all, because the process that wrote it made it up and the process reading it
    // made up a different one for the same resource - which is what the restore's
    // structural owner path exists to carry.
    //
    // Asked of the saved object during capture and written onto its node. It is a
    // property of the individual resource and not of its type: two Types are named
    // or labelled by where their assembly came from, and two VirtualAssets by
    // whether the wrapper has a content path. Asking a fresh candidate instead would
    // classify a different object, which is how the previous attempt at this waived
    // the exact keys it was written to hold.
    //
    // The branches follow GetLiveResourceKey's, in its order, so no object can take
    // corresponding branches in the two functions. Four of them deliberately collapse
    // rather than mirror: Atlas and ModAsset answer no whether or not their path is
    // set, MemberInfo answers no outright for the reason spelled out below, and both
    // arms of VirtualAsset - including the file-backed VirtualTexture arm - fall to the
    // last return for the reason spelled out there. Anything with no key never reaches
    // here at all, because capture only asks when the key is non-empty.
    internal static bool HasPortableLiveResourceKey(object resource) {
        return HasPortableLiveResourceKey(resource, HasReproducibleAssemblyName);
    }

    internal static bool HasPortableLiveResourceKey(
        object resource,
        Func<Assembly, bool> hasReproducibleAssemblyName
    ) {
        if (!string.IsNullOrWhiteSpace(GetHookOwnerResourceKey(resource))) {
            // HookGen rebuilds this registry from the installed mods on every
            // launch. A missing exact hook set means this install no longer has
            // the owner that produced the saved iterator.
            return true;
        }
        if (resource is CompareInfo) {
            // A sort name. Every install derives the same one for the same
            // collation, and one it cannot open is a collation it does not have.
            return true;
        }
        if (resource is Type type) {
            // GetLiveResourceKey uses the assembly-qualified name and falls back to a
            // bare name when there is none. Requiring the key to actually be that name
            // is what keeps out every shape whose name leaves something unqualified.
            // Measured on .NET 8: a generic parameter, an array of one, and a generic
            // type still holding one - List<T>, Dictionary<int,T> - all return null
            // here, while a generic type definition and a fully closed generic return
            // a real name. Without this, List<T> would be judged on the core library
            // alone while its name carries a bare "T" that two different parameters
            // share.
            return type.AssemblyQualifiedName != null &&
                   HasReproducibleMetadataName(type, hasReproducibleAssemblyName);
        }
        // A MemberInfo deliberately falls through to the last return. Its key is the
        // assembly's full name plus a metadata token, and a token names a position in
        // one build's member table rather than the member. Rebuilding an assembly at
        // the same name and version moves them: measured on .NET 8, adding one method
        // ahead of two others shifted both their tokens by one, so the saved token
        // named a different method in the rebuilt assembly under an identical
        // Assembly.FullName. Celeste itself is rebuilt at the same version, and Everest
        // regenerates MMHOOK_Celeste.dll whenever the mod set changes.
        //
        // HasReproducibleAssemblyName below says the next process derives the same
        // assembly name. It does not say the assembly is the same build, and nothing in
        // this key does. So a MemberInfo key is a label on this build rather than a name
        // for the member, and the structural owner path that carries it today keeps
        // carrying it.
        if (resource is Assembly assembly) {
            return hasReproducibleAssemblyName(assembly);
        }
        if (resource is EverestModule || resource is EverestModuleSettings) {
            return HasReproducibleMetadataName(resource.GetType(), hasReproducibleAssemblyName);
        }
        if (resource is Atlas || resource is ModAsset) {
            // Both keys read like content - a data path, a virtual path, a source
            // name - and both are built from publicly writable properties that a mod
            // is free to set per process for identical content. There is no
            // reproduction of either being satisfied by the wrong object, and
            // calling a label a name costs a slot that loads today, so these keep
            // the owner path they have always had.
            return false;
        }
        // A VirtualAsset deliberately falls through to the last return, both halves of
        // it. A texture built from data is keyed on a name its creator passed in, which
        // is regenerated per process for the same asset, and carrying that on the owner
        // path is exactly what the structural override was written for.
        //
        // A texture loaded from a file is keyed on its path and its dimensions, and
        // that is the same defect MemberInfo has by a different route: half the key is
        // a name and half is a measurement of the file's current contents. A mod that
        // retextures a PNG at a new size leaves the asset present under the same path
        // and changes the key, so a process that cannot produce the key can still hold
        // the resource - which is the one thing "portable" is supposed to mean here.
        // Reading a miss as absence would refuse a whole slot over a decal being
        // redrawn, where the owner path hands the room the fresh texture today.
        // ResolveDetachedLiveResource compares the whole key including the dimensions,
        // so it is a miss, not a near match. AreEquivalentLiveResources already treats
        // two VirtualTexture wrappers with one key as interchangeable, so this file
        // already says a texture reference is about content rather than about one
        // particular wrapper, and claiming the key names the content contradicts it.
        return false;
    }

    // Does the next process derive this assembly's name again, or did this one make
    // it up? A Type's key, an EverestModule's and an EverestModuleSettings' are all
    // built out of the assembly's name, so this is the whole of what decides whether
    // those keys name their resource.
    //
    // Three populations, and each answers differently:
    //
    // - Emitted. AssemblyBuilder takes whatever name the caller passed and callers
    //   number them in the order this process happened to build them. IsDynamic says
    //   so.
    // - Loaded off a file by the runtime. The name came out of that file's metadata,
    //   so the next process reads the same name off the same file. Location says so.
    // - Loaded from bytes. Location is empty and IsDynamic is false for a mod's
    //   assembly and for a helper a mod compiled at startup and handed to
    //   Assembly.Load(byte[]) alike, and nothing on Assembly separates them. The
    //   load context does. EverestModuleAssemblyContext.LoadRelinkedAssembly reads a
    //   relinked dll into memory and calls LoadFromStream so the file on disk is not
    //   locked, so every installed mod's assembly is in one of those contexts and its
    //   name is the mod's own, off the mod's own dll. Assembly.Load(byte[]) builds an
    //   IndividualAssemblyLoadContext of its own instead - measured on .NET 8 - so it
    //   is not one of Everest's, and neither is a context a mod builds for itself.
    //
    // Asking whether the assembly came off disk, which is what this used to do,
    // answers no for every mod assembly there has ever been, because of the
    // LoadFromStream above. That waived the key for every mod-owned Type,
    // EverestModule and EverestModuleSettings at site A, and the error direction there
    // is a wrong restore rather than a false refusal: a mod-owned reflection resource
    // this process genuinely lacks was answered by whatever object sat at the saved
    // structural position, silently.
    //
    // This says which assembly is loaded and deliberately not which BUILD of it. A
    // MemberInfo key is an assembly name plus a metadata token, and rebuilding an
    // assembly at the same name and version moves tokens, so a MemberInfo key is a
    // label on one build rather than a name for the member. MemberInfo therefore never
    // reaches here: HasPortableLiveResourceKey falls it through to its last return.
    private static bool HasReproducibleAssemblyName(Assembly assembly) {
        if (assembly == null || assembly.IsDynamic) {
            return false;
        }
        if (!string.IsNullOrEmpty(assembly.Location)) {
            return true;
        }
        return AssemblyLoadContext.GetLoadContext(assembly) is EverestModuleAssemblyContext;
    }

    // A Type's key is its assembly-qualified name, which spells out the assembly of
    // every type inside it as well as its own. List<T> over an emitted type lives in
    // the core library and still carries the emitted assembly's made-up name in its
    // key, so every assembly the shape names has to be reproducibly named for the key
    // to name anything.
    private static bool HasReproducibleMetadataName(
        Type type,
        Func<Assembly, bool> hasReproducibleAssemblyName
    ) {
        if (type == null) {
            return false;
        }
        if (type.IsFunctionPointer) {
            // Measured: a function pointer type's assembly-qualified name says the
            // core library and spells its signature types out inside the name without
            // qualifying them, and GetElementType returns nothing, so the recursion
            // below cannot see what it is built from. Nothing here names it.
            return false;
        }
        if (type.HasElementType) {
            return HasReproducibleMetadataName(type.GetElementType(), hasReproducibleAssemblyName);
        }
        if (type.IsGenericType &&
            type.GetGenericArguments().Any(argument =>
                !HasReproducibleMetadataName(argument, hasReproducibleAssemblyName))) {
            return false;
        }
        return hasReproducibleAssemblyName(type.Assembly);
    }

    // Every EntityID the map lays out in one room, however this run's session flags
    // decided to build it. LoadLevel skips entities a flag has retired, so the room
    // is a subset of this and the map data is the only session-independent record of
    // what the room is meant to contain. Both the capture baseline and the room a
    // restore rebuilds into resolve the same static AreaData, so a difference between
    // the two answers is a difference in the map file itself.
    //
    // Triggers carry EntityIDs from the same per-room numbering as entities, so both
    // lists belong here. Decals do not have ids and never reach this.
    //
    // Session.MapData resolves through the process-wide AreaData rather than through
    // the room graph, so a capture reads it from the persistence worker rather than
    // from the game thread. That is the same live process state the worker already
    // reads through ResolveDetachedLiveResource - VirtualContent.Assets,
    // Everest.Content.Map, the loaded assembly list - and map data is the least
    // volatile of them: it is built once when the map is loaded and only rebuilt by
    // an explicit map reload.
    //
    // Three answers, and the third is the one this exists for. A set of ids is map data
    // that places them. An empty set is map data that places nothing in that room, which
    // covers both an empty room and a room the map does not have at all. Null is no map
    // data to read: a root that is not a loaded room, a session whose area or side is not
    // in the loaded area list, or a map that was being rebuilt while this read it.
    //
    // Only the second may lead to a refusal. The refusal built on this rule tells the
    // player their map or their collab changed, so it may only fire where the map has
    // actually been read and has actually dropped the id. Folding the third case into the
    // second - which returning an empty set for it would do - would tell a player their
    // collab was updated because a map reload happened to overlap a load.
    //
    // The ids are materialised inside the guard rather than handed back lazily. The
    // caller enumerates them to build its per-room set, and LevelData.Entities is a
    // List the game thread owns: an enumeration that ran outside this try would put
    // the "Collection was modified" throw back where nothing catches it.
    internal static IEnumerable<int> GetMapPlacedEntityIds(object roomRoot, string roomName) {
        try {
            MapData map = ResolveMapData((roomRoot as AkronPersistentRuntimeState)?.Level?.Session);
            if (map == null) {
                return null;
            }
            LevelData room = map.Levels.FirstOrDefault(level =>
                string.Equals(level.Name, roomName ?? string.Empty, StringComparison.Ordinal));
            return room == null ? Array.Empty<int>() : GetMapPlacedEntityIds(room).ToList();
        } catch (Exception exception) when (exception is ArgumentOutOfRangeException ||
                                           exception is IndexOutOfRangeException ||
                                           exception is InvalidOperationException) {
            // The three shapes a rebuild of AreaData.Areas under a reader takes, and the
            // reason the bounds checks in ResolveMapData are not on their own a mitigation:
            // they are check-then-act on a list another thread owns. ArgumentOutOfRange is
            // the List indexer after the list shrank past the checked Count,
            // IndexOutOfRange is the ModeProperties array indexer, and InvalidOperation is
            // "Collection was modified" out of MapData.Levels or LevelData.Entities. Any
            // other exception is a defect here rather than a race and is left to travel.
            return null;
        }
    }

    // The map this session is playing, or null when there is none to read.
    //
    // Session.MapData is "AreaData.Areas[Area.ID].Mode[(int)Area.Mode].MapData" - two
    // array indexes with no bounds check - so reading it through the property throws
    // ArgumentOutOfRangeException for an Area.ID past the end of the loaded area list
    // and IndexOutOfRangeException for a side the map has no ModeProperties for. Both
    // were measured, and both are what these bounds checks are for.
    //
    // What the bounds checks are not for is the race. Capture runs on the persistence
    // worker against a deep-cloned Session while the game thread is free to rebuild
    // AreaData.Areas for a map reload or a mod-set change, and no amount of checking a
    // shared list before indexing it makes the pair atomic. The caller's catch is what
    // covers that, and it turns the race into the same "no map data" answer these
    // checks produce.
    //
    // Deliberately stops at the MapData rather than resolving the room. A map that has
    // no such room is still map data, and its answer for that room is "nothing", not
    // "I could not look".
    private static MapData ResolveMapData(Session session) {
        List<AreaData> areas = AreaData.Areas;
        int areaId = session?.Area.ID ?? -1;
        if (areas == null || areaId < 0 || areaId >= areas.Count) {
            return null;
        }
        ModeProperties[] modes = areas[areaId]?.Mode;
        int modeIndex = (int) session.Area.Mode;
        if (modes == null || modeIndex < 0 || modeIndex >= modes.Length) {
            return null;
        }
        return modes[modeIndex]?.MapData;
    }

    internal static IEnumerable<int> GetMapPlacedEntityIds(LevelData room) {
        return room.Entities
            .Where(entityData => entityData != null)
            .Select(entityData => entityData.ID)
            .Concat(room.Triggers
                .Where(entityData => entityData != null)
                .Select(entityData => entityData.ID + TriggerEntityIdOffset));
    }

    // Everest numbers triggers in their own range. patch_Level.CreateEntityId is
    // "new EntityID(levelData.Name, entityData.ID + (_isLoadingTriggers ? 10000000 : 0))",
    // and the IL patch sets that flag around the LevelData.Triggers loop, so a
    // trigger's live SourceId is its map id plus this and an entity's is its map id
    // alone. Reading both lists as one range would leave every trigger unmatched -
    // no protection for triggers - and worse, an entity whose id the map dropped
    // would still be found through a trigger that happens to carry the same raw
    // number, which is common because the two lists number independently.
    private const int TriggerEntityIdOffset = 10000000;

    internal static bool AreEquivalentLiveResources(Type type) {
        // A VirtualTexture key includes its source path and dimensions. A
        // ModAsset key includes its source module, path, data type, and format.
        // Multiple fresh wrappers with either key expose the same immutable
        // loader content, unlike other process-owned resource types. What
        // earns a type a place here is that the fresh room cannot be relied on
        // to produce as many distinct wrappers as the saved frame held, so
        // folding saved references onto one is the only way through.
        // CompareInfo is deliberately not here even though two instances with
        // the same sort name do sort identically. Sorting the same is not being
        // the same object, and folding would quietly turn two saved references
        // into one. The process caches one CompareInfo per sort name, so a
        // saved graph holding two of them cannot be satisfied twice: the second
        // anchor collides with the first and is refused by name, which is the
        // outcome this file prefers everywhere else.
        return typeof(VirtualTexture).IsAssignableFrom(type) ||
               typeof(ModAsset).IsAssignableFrom(type);
    }

    public static AkronReconstructionCapture Capture(
        AkronPersistentRuntimeState savedState,
        AkronPersistentRuntimeState freshState
    ) {
        return CaptureGraph.Capture(savedState, freshState);
    }

    public static IDisposable UseCapturedRenderTargets(
        IReadOnlyDictionary<object, AkronReconstructionResourcePayload> payloads
    ) {
        return AkronVirtualRenderTargetResourceAdapter.UseCapturedPayloads(payloads);
    }

    public static AkronReconstructionCapture CaptureActionState(
        Dictionary<string, Dictionary<Type, Dictionary<string, object>>> savedState,
        Dictionary<string, Dictionary<Type, Dictionary<string, object>>> freshState
    ) {
        return CaptureGraph.Capture(savedState, freshState);
    }

    public static string Serialize(AkronReconstructionDocument document) {
        return CaptureGraph.Serialize(document);
    }

    public static AkronReconstructionDocument Deserialize(string json) {
        return RestoreGraph.Deserialize(json);
    }

    public static AkronReconstructionRestore Restore(
        AkronReconstructionDocument document,
        AkronPersistentRuntimeState freshState
    ) {
        using IDisposable hookOwners = UseHookOwnerRegistrations();
        return RestoreGraph.Restore(document, freshState);
    }

    public static AkronReconstructionRestore RestoreActionState(
        AkronReconstructionDocument document,
        Dictionary<string, Dictionary<Type, Dictionary<string, object>>> freshState
    ) {
        using IDisposable hookOwners = UseHookOwnerRegistrations();
        return RestoreGraph.Restore(document, freshState);
    }

    public static AkronReconstructionVerification Reapply(
        AkronReconstructionDocument document,
        AkronReconstructionRestore restore
    ) {
        return RestoreGraph.Reapply(document, restore);
    }

    public static AkronReconstructionVerification Verify(
        AkronReconstructionDocument document,
        AkronReconstructionRestore restore,
        IEnumerable<string> maskedPaths
    ) {
        return RestoreGraph.Verify(document, restore, maskedPaths);
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
        RestoreGraph.ReleaseOwnedPersistentResources();
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
            foreach (AkronReconstructionField field in node.FieldsOrNull ?? Enumerable.Empty<AkronReconstructionField>()) {
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
        string directory = null,
        long maxDecompressedBytes = MaxDecompressedSnapshotBytes,
        AkronReconstructionGraph verificationGraph = null
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
            byte[] serializedHash;
            using (FileStream file = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (GZipStream compressed = new GZipStream(file, CompressionLevel.Optimal, leaveOpen: false))
            using (AkronPacedWriteStream paced = new AkronPacedWriteStream(compressed, maxDecompressedBytes)) {
                CaptureGraph.Serialize(document, paced);
                serializedHash = paced.GetHashAndReset();
            }
            // Read the file back through the same bounded reader before committing
            // it. The paced writer only caps bytes, but the reader also bounds
            // structure - node, record, container and token counts - and a real
            // capture dense in tiny nodes can pass the byte cap yet trip a
            // structural ceiling, which would put back the unreadable-slot failure
            // this whole path exists to prevent. Verifying readability here makes a
            // successful Set mean the slot loads, whatever ceiling is tightest. The
            // document's exact indexed type/header/graph view was validated immediately
            // before serialization; this pass streams the written JSON through every
            // reader ceiling and requires its uncompressed SHA-256 to match every byte
            // handed to gzip, catching corruption without building a discarded second
            // object graph. CaptureGraph is safe to reuse:
            // the persistence worker runs one job at a time, so serialize and this
            // read never overlap on it. Same thread, so the read paces too; a
            // shutdown cancel surfaces as a cancellation rather than a save failure.
            using (FileStream verify = new FileStream(temporaryPath, FileMode.Open, FileAccess.Read, FileShare.None)) {
                // verificationGraph is a test seam for driving the read ceilings with
                // small caps; production passes null and verifies with CaptureGraph,
                // whose default caps match the loader the slot will actually face.
                if (!TryValidateSnapshot(
                        verificationGraph ?? CaptureGraph,
                        verify,
                        serializedHash,
                        out string readBackError,
                        maxDecompressedBytes)) {
                    if (AkronSnapshotPacing.Cancelled) {
                        throw new OperationCanceledException(AkronSnapshotPacing.CancelledMessage);
                    }
                    error = "snapshot could not be read back after writing: " + readBackError;
                    return false;
                }
            }
            File.Move(temporaryPath, path, overwrite: true);
            return true;
        } catch (OperationCanceledException) {
            // Same reason Capture rethrows it: the paced writer cancels through
            // AkronSnapshotPacing.Pace once per buffer flush, and quitting is not a
            // snapshot failure to describe with an exception name. Only the persistence
            // worker paces, so this can only be thrown on the thread whose caller has the
            // handler for it. The finally below still removes the partial temp file.
            throw;
        } catch (Exception exception) {
            error = exception.GetType().Name + ": " + exception.Message;
            // Diagnostic, not Warn: the short form above travels out through the
            // caller and is already reported at Warn where the slot is rolled back.
            // This line adds the one thing that report cannot carry - the stack.
            AkronLog.Diagnostic(nameof(AkronStartPosReconstruction),
                "SaveSnapshot failed for " + slotName + ": " + exception);
            return false;
        } finally {
            if (File.Exists(temporaryPath)) {
                File.Delete(temporaryPath);
            }
            // SaveSnapshot writes the destination directly rather than through
            // PreparedSnapshotInstall, so it has to drop the cached existence answer
            // itself. Run on failure too: the move may have partly happened.
            InvalidateSnapshotExistence(path);
        }
    }

    public static bool TryLoadSnapshot(
        string slotName,
        out AkronReconstructionDocument document,
        out string error,
        string directory = null
    ) {
        return TryLoadSnapshot(slotName, out document, out error, out _, directory);
    }

    // refusedTypeName is set when the read failed because the snapshot names a type this
    // process cannot load, which is what an uninstalled or blacklisted mod looks like
    // from in here. The document is walked far enough during deserialization to hit that,
    // so this refusal never reaches Restore and the load message is built from here
    // instead.
    public static bool TryLoadSnapshot(
        string slotName,
        out AkronReconstructionDocument document,
        out string error,
        out string refusedTypeName,
        string directory = null
    ) {
        document = null;
        error = string.Empty;
        refusedTypeName = string.Empty;
        string path = GetSnapshotPath(slotName, directory);
        // A prewarmed document was produced by this same reader from this same file.
        // Taking it removes it, so one prewarm serves at most one load and a second
        // load of the same slot reads the file again exactly as it does today.
        if (directory == null) {
            document = TakePrewarmedSnapshot(path);
            if (document != null) {
                return true;
            }
        }
        if (!File.Exists(path)) {
            error = "snapshot file is missing";
            return false;
        }

        try {
            using FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!TryReadSnapshot(RestoreGraph, file, out document, out error, out refusedTypeName, out _, MaxDecompressedSnapshotBytes)) {
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
            refusedTypeName = exception is AkronReconstructionException refusal ? refusal.RefusedTypeName : string.Empty;
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
        return TryReadSnapshot(RestoreGraph, snapshotStream, out document, out error, out _, out _, maxDecompressedBytes);
    }

    private static bool TryReadSnapshot(
        AkronReconstructionGraph graph,
        Stream snapshotStream,
        out AkronReconstructionDocument document,
        out string error,
        out string refusedTypeName,
        out long decompressedBytes,
        long maxDecompressedBytes
    ) {
        document = null;
        error = string.Empty;
        refusedTypeName = string.Empty;
        decompressedBytes = 0;
        if (snapshotStream == null || !snapshotStream.CanRead) {
            error = "snapshot stream is unavailable";
            return false;
        }

        try {
            using GZipStream compressed = new GZipStream(snapshotStream, CompressionMode.Decompress, leaveOpen: true);
            using AkronBoundedReadStream bounded = new AkronBoundedReadStream(compressed, maxDecompressedBytes);
            document = graph.Deserialize(bounded);
            decompressedBytes = bounded.Position;
            return true;
        } catch (Exception exception) {
            error = exception.GetType().Name + ": " + exception.Message;
            // A snapshot that names a type this process cannot load is refused during
            // deserialization, before Restore ever sees the document. Carrying the type
            // out of here is what lets the load message name the mod that is missing.
            refusedTypeName = exception is AkronReconstructionException refusal ? refusal.RefusedTypeName : string.Empty;
            document = null;
            return false;
        }
    }

    private static bool TryValidateSnapshot(
        AkronReconstructionGraph graph,
        Stream snapshotStream,
        byte[] expectedHash,
        out string error,
        long maxDecompressedBytes
    ) {
        error = string.Empty;
        if (snapshotStream == null || !snapshotStream.CanRead) {
            error = "snapshot stream is unavailable";
            return false;
        }
        if (expectedHash == null || expectedHash.Length == 0) {
            error = "snapshot write hash is unavailable";
            return false;
        }

        try {
            using GZipStream compressed = new GZipStream(snapshotStream, CompressionMode.Decompress, leaveOpen: true);
            using AkronBoundedReadStream bounded = new AkronBoundedReadStream(
                compressed,
                maxDecompressedBytes,
                hashContents: true);
            graph.ValidateSerializedDocument(bounded);
            byte[] actualHash = bounded.GetHashAndReset();
            if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash)) {
                error = "snapshot bytes differ after writing";
                return false;
            }
            return true;
        } catch (Exception exception) {
            error = exception.GetType().Name + ": " + exception.Message;
            return false;
        }
    }

    // Serializing a snapshot allocates roughly in proportion to the JSON it
    // emits, so the byte stream is the natural pacing point for the write half
    // of the job. Writes arrive here in whole buffer flushes, so each one is a
    // safe place to stop and none of them is large enough to matter if the
    // player takes control halfway through.
    //
    // It also enforces MaxDecompressedSnapshotBytes on the way out. The read
    // side has always refused a snapshot past that limit, so writing one is
    // writing a slot that every later load refuses; failing the save here puts
    // the message on the Set that caused it instead.
    private sealed class AkronPacedWriteStream : Stream {
        private readonly Stream destination;
        private readonly long maxBytes;
        private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private long bytesWritten;

        public AkronPacedWriteStream(Stream destination, long maxBytes) {
            this.destination = destination ?? throw new ArgumentNullException(nameof(destination));
            this.maxBytes = maxBytes > 0 ? maxBytes : throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        private void RecordWrite(long count) {
            bytesWritten += count;
            if (bytesWritten > maxBytes) {
                throw new InvalidOperationException(
                    "Snapshot is larger than the " + (maxBytes >> 20) + " MiB size limit, so this state cannot be saved.");
            }
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) {
            AkronSnapshotPacing.Pace();
            RecordWrite(count);
            hash.AppendData(buffer, offset, count);
            destination.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer) {
            AkronSnapshotPacing.Pace();
            RecordWrite(buffer.Length);
            hash.AppendData(buffer);
            destination.Write(buffer);
        }

        public override void WriteByte(byte value) {
            RecordWrite(1);
            Span<byte> oneByte = stackalloc byte[1];
            oneByte[0] = value;
            hash.AppendData(oneByte);
            destination.WriteByte(value);
        }

        public byte[] GetHashAndReset() {
            return hash.GetHashAndReset();
        }

        public override void Flush() {
            destination.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing) {
            if (disposing) {
                hash.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private sealed class AkronBoundedReadStream : Stream {
        private readonly Stream source;
        private readonly long maxBytes;
        private readonly IncrementalHash hash;
        private long bytesRead;

        public AkronBoundedReadStream(Stream source, long maxBytes, bool hashContents = false) {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            this.maxBytes = maxBytes >= 0 ? maxBytes : throw new ArgumentOutOfRangeException(nameof(maxBytes));
            hash = hashContents ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position {
            get => bytesRead;
            set => throw new NotSupportedException();
        }

        // Reading a snapshot allocates roughly 2.3 bytes of managed heap for every
        // decompressed byte that passes through here, so this is the natural pacing
        // point for the read half of the pipeline, exactly as the write stream is for
        // the write half. The reader above fills a 64 KiB buffer at a time, which
        // bounds how much a prewarm can still allocate after the player takes control.
        // Pace is a thread-static check and does nothing at all on the game thread, so
        // a load reading its own snapshot is unaffected.
        public override int Read(byte[] buffer, int offset, int count) {
            AkronSnapshotPacing.Pace();
            int read = source.Read(buffer, offset, LimitReadCount(count));
            RecordRead(read);
            hash?.AppendData(buffer, offset, read);
            return read;
        }

        public override int Read(Span<byte> buffer) {
            AkronSnapshotPacing.Pace();
            int read = source.Read(buffer[..LimitReadCount(buffer.Length)]);
            RecordRead(read);
            hash?.AppendData(buffer[..read]);
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
                if (hash != null) {
                    Span<byte> oneByte = stackalloc byte[1];
                    oneByte[0] = (byte) value;
                    hash.AppendData(oneByte);
                }
            }
            return value;
        }

        public byte[] GetHashAndReset() {
            if (hash == null) {
                throw new InvalidOperationException("Snapshot hashing is not enabled.");
            }
            return hash.GetHashAndReset();
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

        protected override void Dispose(bool disposing) {
            if (disposing) {
                hash?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // GetSnapshotPath is a pure function of (directory, slot name) but costs a SHA-256
    // plus roughly 35 allocations. HUD and overlay code reach it through HasSnapshot on
    // every rendered frame, so the result is memoized. The key carries the directory
    // because a staging install resolves the same slot name against a temp directory;
    // keying on the slot name alone would hand back the wrong file.
    private static readonly ConcurrentDictionary<string, string> SnapshotPathCache =
        new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

    // File.Exists is a stat syscall and must not run once per slot per frame. Akron is
    // the only writer of this directory, so every write and delete below invalidates the
    // affected path, and LoadStartPositionsForLevel resets the whole cache on each room
    // load to bound staleness if a player edits the Saves folder while the game runs.
    private static readonly ConcurrentDictionary<string, bool> SnapshotExistenceCache =
        new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);

    private static long snapshotExistenceRevision;
    private static long snapshotWriteRevision;

    // Callers that cache a value derived from HasSnapshot compare this counter to know
    // whether any snapshot appeared or disappeared since they last looked.
    internal static long SnapshotExistenceRevision => Interlocked.Read(ref snapshotExistenceRevision);

    // Moves only when a snapshot file is actually written or deleted. The existence
    // revision above also moves when the cached answers are merely flushed so the next
    // caller re-stats, which happens on every room load and changes no file. A prewarm
    // read that spans one of those flushes has read a file nobody touched, so it must
    // keep its result; a read that spans a real write must not.
    internal static long SnapshotWriteRevision => Interlocked.Read(ref snapshotWriteRevision);

    internal static void InvalidateSnapshotExistence(string path) {
        if (string.IsNullOrEmpty(path)) {
            return;
        }
        SnapshotExistenceCache.TryRemove(path, out _);
        DropPrewarmedSnapshot(path);
        Interlocked.Increment(ref snapshotWriteRevision);
        Interlocked.Increment(ref snapshotExistenceRevision);
    }

    // A cold StartPos load spends its snapshot time in gzip plus a reflection-driven
    // JSON parse before it touches anything live. That half is pure data, so it can be
    // produced ahead of time on a worker. Everything after it - the rollback clone, the
    // fresh-room load, the graph restore, reapply and verify passes - mutates the live
    // Level and can only run on the game thread, so this cache is the whole of what
    // prewarming is able to move off the critical path.
    //
    // Documents are large. The budget is expressed in decompressed snapshot bytes,
    // which is the one size known exactly at read time, and the read is bounded by the
    // remaining budget so a snapshot that cannot fit is abandoned partway instead of
    // materializing in full and then being rejected. When the budget is full, prewarm
    // simply stops: a slot that was not prewarmed loads exactly as it does today.
    //
    // 512 MiB, which implies an RSS ceiling of about 1.9 GiB. The numbers, all
    // measured rather than assumed:
    //
    //   * A prewarmed slot costs 3.8x its decompressed bytes in process RSS. Measured
    //     in game on Midnight Aquarium: two slots, 136,094,710 decompressed bytes
    //     cached, RSS 498 MiB above the same load with nothing queued (n=2 per side),
    //     so 249 MiB of RSS for a 64.9 MiB snapshot. An earlier managed-heap figure of
    //     2.30x is what this budget used to be sized on; RSS is 65% higher than that
    //     because it carries committed GC segments and fragmentation the managed
    //     number does not, and RSS is what the machine has to find.
    //   * 512 MiB x 3.8 is about 1.9 GiB of resident memory when the cache is
    //     completely full. That is the number to judge this constant by, not the
    //     512 MiB. Celeste with mods sat at 1055 MB of RSS before any of this, so a
    //     full cache puts the process near 3 GB. The test box has 7751 MB; the budget
    //     is set so that a 4 GB machine still survives a full cache, because the
    //     player's machine is not the test box.
    //   * 17 real snapshots off the test box, decompressed: 42-85 MB for vanilla maps,
    //     150-231 MB for modded ones. At the original 96 MiB, 8 of the 17 - every
    //     modded one - did not fit, so on exactly the maps with the slowest loads the
    //     cache was never used at all. 512 MiB clears the largest of them (231 MB)
    //     2.3x, so a modded map still warms two slots and a vanilla map six to twelve.
    //
    // What this does not buy is a full fifteen-slot map on every map. Fifteen of the
    // heaviest vanilla snapshots would be 4.7 GiB of RSS and fifteen modded ones
    // 12 GiB; no budget a real machine can pay warms those, and the ratio that
    // decides it is the 3.8x, not this constant. Lowering the 3.8x means making
    // AkronReconstructionNode's eager lists lazy, which is a snapshot format change.
    //
    // The cache is not a steady-state cost. Every entry is removed when its slot is
    // loaded, when its file is written or deleted, and when the map or save file
    // changes, so the ceiling is only reached by a player who leaves the game sitting
    // outside gameplay long enough for the worker to read a whole map.
    internal const long MaxPrewarmedSnapshotBytes = 512L * 1024L * 1024L;

    private sealed class PrewarmedSnapshot {
        internal AkronReconstructionDocument Document;
        internal long DecompressedBytes;
        internal long FileLength;
        internal DateTime FileWriteTimeUtc;
    }

    private static readonly Dictionary<string, PrewarmedSnapshot> PrewarmedSnapshots =
        new Dictionary<string, PrewarmedSnapshot>(StringComparer.Ordinal);
    private static readonly object PrewarmedSnapshotsLock = new object();
    private static long prewarmedSnapshotBytes;
    private static long prewarmedSnapshotHits;
    private static long prewarmedSnapshotStores;

    internal static long PrewarmedSnapshotBytes {
        get {
            lock (PrewarmedSnapshotsLock) {
                return prewarmedSnapshotBytes;
            }
        }
    }

    internal static int PrewarmedSnapshotCount {
        get {
            lock (PrewarmedSnapshotsLock) {
                return PrewarmedSnapshots.Count;
            }
        }
    }

    // Cumulative for the session, and deliberately not reset with the cache: these are
    // what makes the cache observable at all. Two verification passes could only infer
    // that a load had been served from the cache by comparing its wall-clock time
    // against an earlier one, which cannot tell a cache hit from a faster machine, and
    // would not have noticed a change that silently disabled the cache.
    internal static long PrewarmedSnapshotHits => Interlocked.Read(ref prewarmedSnapshotHits);
    internal static long PrewarmedSnapshotStores => Interlocked.Read(ref prewarmedSnapshotStores);

    internal static void ResetPrewarmedSnapshots() {
        lock (PrewarmedSnapshotsLock) {
            PrewarmedSnapshots.Clear();
            prewarmedSnapshotBytes = 0;
        }
    }

    // Test seam, called from nowhere in the mod. A cache with too little room left to
    // accept a snapshot holds more than MaxPrewarmedSnapshotBytes minus that snapshot,
    // which for anything but a toy document is hundreds of megabytes of real captured
    // rooms - a state no unit test can build and the exact state whose reporting
    // regressed. This writes the same field a real store writes, under the same lock,
    // and ResetPrewarmedSnapshots puts it back in step with the dictionary.
    internal static void HoldPrewarmedSnapshotBytesForTests(long bytes) {
        lock (PrewarmedSnapshotsLock) {
            prewarmedSnapshotBytes = bytes;
        }
    }

    private static void DropPrewarmedSnapshot(string path) {
        lock (PrewarmedSnapshotsLock) {
            if (PrewarmedSnapshots.Remove(path, out PrewarmedSnapshot dropped)) {
                prewarmedSnapshotBytes -= dropped.DecompressedBytes;
            }
        }
    }

    // Reads the snapshot for one slot into the cache, and reports what happened to it.
    // Runs on the prewarm worker. isCancelled is polled inside the read so a queue that
    // is cancelled mid-file stops immediately instead of finishing a multi-second parse
    // nobody wants, and the read paces on the same gate the restart copy uses, so it
    // makes no progress at all while the player is in control.
    internal static AkronPrewarmOutcome PrewarmSnapshot(string slotName, Func<bool> isCancelled) {
        string path = GetSnapshotPath(slotName);
        long remainingBudget;
        lock (PrewarmedSnapshotsLock) {
            if (PrewarmedSnapshots.ContainsKey(path)) {
                return AkronPrewarmOutcome.AlreadyCached;
            }
            remainingBudget = MaxPrewarmedSnapshotBytes - prewarmedSnapshotBytes;
        }

        // Every writer of this directory bumps the write revision. Reading it before the
        // file and rejecting the result if it moved closes the window where a Set
        // replaces the snapshot while this read is in flight: the entry is not in the
        // cache yet, so the writer's DropPrewarmedSnapshot has nothing to remove.
        //
        // The write revision rather than the existence revision, because a read now runs
        // during the load that queued it, and a load reloads the room, and a room load
        // flushes the existence cache. Comparing the existence revision would throw away
        // the result of every read started inside the window this feature exists to use.
        long revisionBeforeRead = SnapshotWriteRevision;
        FileInfo info = new FileInfo(path);
        if (!info.Exists) {
            return AkronPrewarmOutcome.NotStored;
        }
        long fileLength = info.Length;
        DateTime fileWriteTimeUtc = info.LastWriteTimeUtc;

        AkronReconstructionDocument document;
        long decompressedBytes;
        try {
            // ReadWrite | Delete rather than Read, because this read now parks at its
            // pace points and can hold the handle for as long as the player keeps
            // playing. Windows refuses to rename or delete a file another handle holds
            // open unless that handle shares the right, so a narrower share would let a
            // speculative read block a Set from installing its own snapshot. Reading
            // across a write is already refused by the revision check below.
            using FileStream file = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            // The budget question is answered before a byte is decompressed, from the
            // size this snapshot expands to. Clamping the read to the remaining budget
            // instead - which is what this used to do - makes a snapshot that does not
            // fit throw its way out of the bounded stream, so a full cache reported
            // every slot it skipped as one it "could not be read", which reads like a
            // corrupt file and sent a maintainer hunting for one. It also decompressed
            // up to the whole remaining budget per skipped slot for nothing.
            if (!TryReadExpandedSnapshotSize(file, out long expandedBytes)) {
                return AkronPrewarmOutcome.NotStored;
            }
            // Ordered before the budget on purpose. A snapshot that expands past the
            // cold-read limit is one no load can use either, and a trailer claiming more
            // than that belongs to a truncated or corrupt file. Calling either of them a
            // budget refusal would blame a full cache for a file that will never load.
            if (expandedBytes > MaxDecompressedSnapshotBytes) {
                return AkronPrewarmOutcome.NotStored;
            }
            if (expandedBytes > remainingBudget) {
                return AkronPrewarmOutcome.BudgetFull;
            }
            file.Position = 0;

            using AkronCancellableReadStream cancellable = new AkronCancellableReadStream(file, isCancelled);
            // The cache exists to serve documents a cold read would have produced, so it
            // must never accept one a cold read would refuse. The budget is larger than a
            // whole legal snapshot, so without this bound an oversized snapshot would be
            // prewarmed successfully and then fail when read from disk, which would make
            // whether a slot loads at all depend on whether the worker got to it first.
            if (!TryReadSnapshot(PrewarmGraph, cancellable, out document, out _, out _, out decompressedBytes, MaxDecompressedSnapshotBytes)) {
                return AkronPrewarmOutcome.NotStored;
            }
        } catch (Exception) {
            // A prewarm failure is not a load failure. The slot still loads from disk
            // on the normal path, which reports its own errors to the player.
            return AkronPrewarmOutcome.NotStored;
        }
        if (document == null ||
            !string.Equals(document.SlotName, slotName, StringComparison.Ordinal) ||
            revisionBeforeRead != SnapshotWriteRevision) {
            return AkronPrewarmOutcome.NotStored;
        }

        lock (PrewarmedSnapshotsLock) {
            // CancelPrewarm changes the generation before resetting this cache. Check
            // while holding the cache lock so cancellation either prevents this store
            // or the following reset waits for it and removes it.
            if (isCancelled()) {
                return AkronPrewarmOutcome.NotStored;
            }
            if (PrewarmedSnapshots.ContainsKey(path)) {
                return AkronPrewarmOutcome.AlreadyCached;
            }
            if (prewarmedSnapshotBytes + decompressedBytes > MaxPrewarmedSnapshotBytes) {
                return AkronPrewarmOutcome.BudgetFull;
            }
            PrewarmedSnapshots[path] = new PrewarmedSnapshot {
                Document = document,
                DecompressedBytes = decompressedBytes,
                FileLength = fileLength,
                FileWriteTimeUtc = fileWriteTimeUtc
            };
            prewarmedSnapshotBytes += decompressedBytes;
        }
        Interlocked.Increment(ref prewarmedSnapshotStores);
        return AkronPrewarmOutcome.Stored;
    }

    // How many bytes a snapshot expands to, taken from the gzip trailer instead of by
    // decompressing it. RFC 1952 stores the uncompressed length in the last four bytes
    // of a member, little endian, modulo 2^32. SaveSnapshot writes each snapshot as a
    // single member and MaxDecompressedSnapshotBytes keeps every legal one far below
    // 4 GiB, so this is the exact expanded size for any file the prewarm reads.
    //
    // The caller leaves the stream where it wants it; this one only seeks.
    private static bool TryReadExpandedSnapshotSize(FileStream file, out long expandedBytes) {
        expandedBytes = 0;
        // A gzip member is at least a 10-byte header, a 2-byte deflate payload and an
        // 8-byte trailer. Anything shorter is not a snapshot at all.
        if (file.Length < 20) {
            return false;
        }
        Span<byte> trailer = stackalloc byte[4];
        file.Position = file.Length - trailer.Length;
        if (file.ReadAtLeast(trailer, trailer.Length, throwOnEndOfStream: false) != trailer.Length) {
            return false;
        }
        expandedBytes = BinaryPrimitives.ReadUInt32LittleEndian(trailer);
        return true;
    }

    // Removes and returns the prewarmed document for a path, but only when the file on
    // disk is still the one it was read from. Every Akron writer of this directory calls
    // InvalidateSnapshotExistence, which already drops the entry; the stamp closes the
    // remaining window, where a setup-pack import or the player replaces the file
    // without going through those writers.
    private static AkronReconstructionDocument TakePrewarmedSnapshot(string path) {
        PrewarmedSnapshot prewarmed;
        lock (PrewarmedSnapshotsLock) {
            if (!PrewarmedSnapshots.Remove(path, out prewarmed)) {
                return null;
            }
            prewarmedSnapshotBytes -= prewarmed.DecompressedBytes;
        }

        FileInfo info = new FileInfo(path);
        if (!info.Exists ||
            info.Length != prewarmed.FileLength ||
            info.LastWriteTimeUtc != prewarmed.FileWriteTimeUtc) {
            return null;
        }
        Interlocked.Increment(ref prewarmedSnapshotHits);
        return prewarmed.Document;
    }

    private sealed class AkronCancellableReadStream : Stream {
        private readonly Stream source;
        private readonly Func<bool> isCancelled;

        public AkronCancellableReadStream(Stream source, Func<bool> isCancelled) {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            this.isCancelled = isCancelled;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) {
            ThrowIfCancelled();
            return source.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer) {
            ThrowIfCancelled();
            return source.Read(buffer);
        }

        private void ThrowIfCancelled() {
            if (isCancelled != null && isCancelled()) {
                throw new OperationCanceledException("StartPos prewarm was cancelled.");
            }
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    internal static void ResetSnapshotExistenceCache() {
        if (SnapshotExistenceCache.IsEmpty) {
            return;
        }
        SnapshotExistenceCache.Clear();
        Interlocked.Increment(ref snapshotExistenceRevision);
    }

    public static bool HasSnapshot(string slotName, string directory = null) {
        string path = GetSnapshotPath(slotName, directory);
        if (SnapshotExistenceCache.TryGetValue(path, out bool cachedExists)) {
            return cachedExists;
        }
        bool exists = File.Exists(path);
        SnapshotExistenceCache[path] = exists;
        return exists;
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
        } finally {
            // Run even when the delete threw: the file may be half gone, and a stale
            // "exists" answer would keep a dead slot in the StartPos list.
            InvalidateSnapshotExistence(path);
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

    internal static string GetSnapshotDirectory() {
        return Path.Combine(AppContext.BaseDirectory, "Saves", SnapshotDirectoryName);
    }

    internal static string GetSnapshotPath(string slotName, string directory = null) {
        // Only the canonical root is memoized. Callers that pass an explicit directory
        // are staging installs with a fresh GUID directory per capture, so caching those
        // would grow without bound for no benefit; they are not on any per-frame path.
        if (!string.IsNullOrWhiteSpace(directory)) {
            return Path.Combine(directory, BuildSnapshotFileName(slotName ?? string.Empty));
        }
        return SnapshotPathCache.GetOrAdd(slotName ?? string.Empty, static key =>
            Path.Combine(GetSnapshotDirectory(), BuildSnapshotFileName(key)));
    }

    // Tracks AkronReconstructionDocument.CurrentFormat. A snapshot written against a
    // different fresh-room baseline gets a different path, so no read can reach it and
    // no write can replace it in place.
    private const string SnapshotFileNamePrefix = "v10-";
    // Internal so the snapshot-report command can glob the same files this writes.
    internal const string SnapshotFileNameSuffix = ".json.gz";

    private static string BuildSnapshotFileName(string slotName) {
        return SnapshotFileNamePrefix + BuildSnapshotSlotDigest(slotName) + SnapshotFileNameSuffix;
    }

    private static string BuildSnapshotSlotDigest(string slotName) {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(slotName ?? string.Empty));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    internal sealed class PreparedSnapshotInstall : IDisposable {
        private readonly string sourcePath;
        private readonly string destinationPath;
        private readonly string backupPath;
        // What this install did to the slot, recorded as it happens, because the rollback
        // cannot read it back off the filesystem: a file at the destination with no backup
        // beside it is both "this install put it there" and "this install never took the
        // destination at all", and those two want opposite actions - remove it, or leave
        // the player's snapshot exactly where it is.
        private bool previousSnapshotBanked;
        private bool destinationClaimed;

        private bool attempted;
        private bool installed;
        private bool committed;

        public PreparedSnapshotInstall(string sourcePath, string destinationPath, string stagingDirectory) {
            this.sourcePath = sourcePath;
            this.destinationPath = destinationPath;
            backupPath = Path.Combine(stagingDirectory, "replaced-" + Guid.NewGuid().ToString("N"));
        }

        // True once the move that puts the slot's previous snapshot back has failed. What
        // that leaves cannot be assumed to be a loadable slot: the copy of the snapshot is
        // in the staging directory, which AkronStartPosPersistence.Update deletes as soon
        // as the completion returns, so the failed Set says the slot has to be set again
        // rather than reporting that the previous StartPos was kept.
        //
        // One outcome reports here and is not a loss: a cross-volume move is a copy
        // followed by a delete of its source, so a copy that landed and a delete that then
        // failed throws with the slot's snapshot back in place. That direction is the one
        // to err in - a re-set the player did not need costs a Set, while silence about a
        // slot that is gone costs the slot.
        public bool PreviousSnapshotLost { get; private set; }

        // One attempt per prepared install, refused rather than retried. The two facts
        // recorded above describe one attempt, and the rollback of that attempt has already
        // acted on them: a claim this object made and then removed would otherwise license
        // a second attempt's rollback to delete whatever had arrived at the destination
        // since. A caller that wants another go prepares another install.
        public bool Install(out string error) {
            error = string.Empty;
            if (attempted) {
                error = "staged snapshot install has already been attempted";
                return false;
            }
            attempted = true;
            try {
                if (!File.Exists(sourcePath)) {
                    error = "staged snapshot file is missing";
                    return false;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                try {
                    File.Move(destinationPath, backupPath);
                    previousSnapshotBanked = true;
                } catch (FileNotFoundException) {
                    // "Nothing to bank" is not something to take on trust. This exception
                    // and File.Exists come from the same query, which answers "missing"
                    // for a directory in the way and for a query it could not complete -
                    // a folder in the path that has lost its search permission, an IO
                    // error, a Windows attribute read something is holding off - so a
                    // slot that does hold a snapshot can land here. Measured: with the
                    // snapshot folder made unsearchable, this move reports
                    // FileNotFoundException for a file that is there.
                    //
                    // The exclusive create is the proof, and the only airtight one on
                    // offer: it can only succeed on a free name, so the slot really was
                    // empty and the file the rollback may delete is the one this install
                    // created. A slot that turns out to be occupied fails it, and the
                    // install is refused with nothing moved and nothing removed.
                    //
                    // The cost is a zero-byte file at the slot's path until the move below
                    // replaces it, and two things can see it. The prewarm reader, if it
                    // opens the path in that window, reads nothing usable and counts the
                    // slot as one it could not read in a Diagnostic log line; it caches
                    // nothing, because a zero-byte file fails its size check, and it cannot
                    // wedge the move or the delete either, because it opens snapshots with
                    // FileShare.Delete for that reason (see PrewarmSnapshot). A program
                    // outside this process can wedge both, on Windows, and leave the slot
                    // reporting a snapshot it cannot read. Neither is created by the claim:
                    // a cross-volume install with no claim in it publishes a growing
                    // partial file at the same path for as long as the copy takes, which
                    // reads as unreadable and wedges the same delete. What the claim buys
                    // is that the delete below can never take a file this install did not
                    // create, short of an outside writer replacing it inside those two
                    // statements.
                    using (new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
                    destinationClaimed = true;
                }
                // The overwrite has this install's own claim to overwrite and nothing
                // else: the other way here banked what the slot held and left the name
                // free. Nothing outside this process is expected at that path, and a
                // writer that does appear there is not something a move could refuse
                // usefully.
                File.Move(sourcePath, destinationPath, overwrite: true);
                installed = true;
                // The destination just gained a file. Drop any cached "missing" answer
                // so the slot becomes visible in the StartPos list on the next query.
                InvalidateSnapshotExistence(destinationPath);
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

        // Puts the slot back the way it was, and never throws doing it.
        //
        // Both callers are already carrying a failure: Install's catch block is about to
        // report an IOException, and Dispose runs inside a using whose body may be
        // propagating one. A throw from here would replace that failure with this one or
        // lose it entirely, so the catch is total by design rather than filtered.
        //
        // Which branch runs is decided by what Install recorded doing, never by what is
        // on disk now, because the two states that matter look identical from outside.
        //
        // The move back overwrites instead of being preceded by a delete. Deleting first
        // left a window with no snapshot in the slot at all, and the copy that was going
        // to fill it lives in the staging directory AkronStartPosPersistence.Update
        // deletes unconditionally - so a move that failed after the delete lost the
        // slot's last good snapshot rather than keeping it.
        private void RollBack() {
            try {
                if (previousSnapshotBanked) {
                    // Overwrites whatever this install left at the destination: the staged
                    // file it moved in, or a part-copied one from a cross-volume move that
                    // failed on the way there.
                    File.Move(backupPath, destinationPath, overwrite: true);
                } else if (destinationClaimed) {
                    // The slot was proved free and this install is what put a file at
                    // the destination, whole, half-copied or still the zero-byte claim,
                    // so removing it takes nothing of the player's.
                    File.Delete(destinationPath);
                }
                // Neither flag: the install never took the destination, and a failed move
                // leaves its source alone, so the slot still holds the file it held
                // before this install started. Nothing to put back, and nothing here
                // that this install can prove it created.
            } catch (Exception exception) {
                if (previousSnapshotBanked) {
                    PreviousSnapshotLost = true;
                }
                Logger.Log(LogLevel.Warn, nameof(AkronStartPosReconstruction),
                    "Could not roll back the StartPos snapshot install for " + destinationPath + ": " + exception.Message);
            } finally {
                // Cleared even when the rollback failed, so Dispose does not retry a move
                // that has already been reported.
                installed = false;
                // Rollback can leave the destination present or absent depending on which
                // branch ran and whether it worked, so re-stat on the next query rather
                // than guessing.
                InvalidateSnapshotExistence(destinationPath);
            }
        }

        public void Dispose() {
            if (installed && !committed) {
                RollBack();
            }
        }
    }

    // A CompareInfo's sort name is empty for the invariant culture, and an
    // empty resource key means "this type has no key" to the graph. Prefix it
    // so every culture, the invariant one included, gets a resolvable key.
    private const string CompareInfoSortNameKeyPrefix = "sort-name=";

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
        // CompareInfo is an immutable culture-owned wrapper around this
        // process's native collation state: _sortHandle is a handle into the
        // platform's globalization data, opened when the wrapper is built.
        // Nothing about it describes the room, and it is two hops from any
        // string collection built with a culture-aware comparer - ordinary mod
        // code. Walking into it reaches an IntPtr, which capture must refuse,
        // so one such collection anywhere in a mod session costs the player
        // every StartPos slot. Its whole identity is its sort name, so
        // GetLiveResourceKey and ResolveDetachedLiveResource can hand the
        // rebuilt room the same collation the saved frame used. Note this
        // fixes the pointer only: a hash-based collection also stores hash
        // codes that belong to the capturing process, which is a separate
        // problem this file solves for EntityList and ComponentList alone
        // (see ValidateAndNormalizeMembershipSet) and nowhere else yet.
        return type == typeof(Pathfinder) ||
               type == DynamicDataCacheType ||
               type == typeof(CompareInfo) ||
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

    // GetTypedResourceKey writes "<type name>|<resource key>", and both detached
    // lookups below read the key back out of it. One parser, so the guard - no
    // separator, or nothing after it - cannot drift between them.
    private static string StripTypedKeyPrefix(string typedResourceKey) {
        int separator = typedResourceKey.IndexOf('|');
        return separator < 0 || separator == typedResourceKey.Length - 1
            ? null
            : typedResourceKey.Substring(separator + 1);
    }

    internal static object ResolveDetachedLiveResource(Type resourceType, string typedResourceKey) {
        if (resourceType == null || string.IsNullOrWhiteSpace(typedResourceKey)) {
            return null;
        }

        string resourceKey = StripTypedKeyPrefix(typedResourceKey);
        if (resourceKey == null) {
            return null;
        }
        if (resourceType == DynamicDataCacheType) {
            return ResolveDynamicDataCache(resourceKey);
        }
        if (resourceKey.StartsWith(HookOwnerKeyPrefix, StringComparison.Ordinal)) {
            return ResolveHookOwner(resourceType, resourceKey);
        }
        if (typeof(VirtualTexture).IsAssignableFrom(resourceType)) {
            // Randomized decals can select a different texture wrapper each
            // time their room loads. VirtualContent retains every wrapper by
            // asset identity, so the saved texture can still be authenticated
            // even when the fresh entity graph did not select it.
            IEnumerable<VirtualAsset> assets =
                (IEnumerable<VirtualAsset>) VirtualContentAssetsField.GetValue(null);
            return assets.FirstOrDefault(asset =>
                asset?.GetType() == resourceType &&
                string.Equals(GetLiveResourceKey(asset), resourceKey, StringComparison.Ordinal));
        }
        if (typeof(ModAsset).IsAssignableFrom(resourceType)) {
            return Everest.Content.Map.Values.FirstOrDefault(asset =>
                asset?.GetType() == resourceType &&
                string.Equals(GetLiveResourceKey(asset), resourceKey, StringComparison.Ordinal));
        }
        if (typeof(Atlas).IsAssignableFrom(resourceType)) {
            // A content atlas is loaded once for the process and handed out by
            // name. The fresh room only holds a reference to one while
            // something the room built happens to draw from it, so an atlas the
            // saved frame reached is routinely absent from the fresh-room index
            // while the process still owns the identical object: a Textbox
            // takes its frame from GFX.Portraits, and a room that loaded
            // without dialogue on screen has no path to that atlas at all.
            // Without this the anchor has nothing to pair with and the whole
            // slot is refused over content that never went anywhere.
            return ResolveContentAtlas(resourceType, resourceKey);
        }
        if (typeof(EverestModule).IsAssignableFrom(resourceType)) {
            return Everest.Modules.FirstOrDefault(module => module?.GetType() == resourceType);
        }
        if (typeof(EverestModuleSettings).IsAssignableFrom(resourceType)) {
            return Everest.Modules
                .Select(module => module?._Settings)
                .FirstOrDefault(settings => settings?.GetType() == resourceType);
        }
        if (resourceType == typeof(CompareInfo)) {
            if (!resourceKey.StartsWith(CompareInfoSortNameKeyPrefix, StringComparison.Ordinal)) {
                return null;
            }
            try {
                return CompareInfo.GetCompareInfo(resourceKey.Substring(CompareInfoSortNameKeyPrefix.Length));
            } catch (Exception exception) when (
                exception is CultureNotFoundException || exception is ExternalException) {
                // The saved frame names a sort this install cannot open:
                // CultureNotFoundException when the name is unknown, and
                // ExternalException when the platform has the name but fails to
                // build a collator for it. Report nothing here: the caller
                // falls through to a refusal that names the node, which beats a
                // raw exception the restore can only report against the
                // document root. Anything else is a real fault and is left to
                // propagate.
                return null;
            }
        }
        if (typeof(Type).IsAssignableFrom(resourceType)) {
            Type resolved = Type.GetType(resourceKey, throwOnError: false);
            return resolved != null && resourceType.IsInstanceOfType(resolved)
                ? resolved
                : null;
        }
        if (typeof(Assembly).IsAssignableFrom(resourceType)) {
            return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly =>
                assembly.GetType() == resourceType &&
                string.Equals(assembly.FullName, resourceKey, StringComparison.Ordinal));
        }
        if (!typeof(MemberInfo).IsAssignableFrom(resourceType)) {
            return null;
        }

        int tokenSeparator = resourceKey.LastIndexOf('|');
        if (tokenSeparator <= 0 ||
            !int.TryParse(
                resourceKey.Substring(tokenSeparator + 1),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int metadataToken)) {
            return null;
        }

        string assemblyName = resourceKey.Substring(0, tokenSeparator);
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies().Where(candidate =>
                     string.Equals(candidate.FullName, assemblyName, StringComparison.Ordinal))) {
            foreach (Module module in assembly.GetModules()) {
                try {
                    MemberInfo member = module.ResolveMember(metadataToken);
                    if (member?.GetType() == resourceType) {
                        return member;
                    }
                } catch (ArgumentException) {
                    // The metadata token belongs to a different module in this assembly.
                }
            }
        }
        return null;
    }

    // Every runtime texture this build agrees to recreate, at exactly the size
    // its creator hardcodes: DustEdges.CreateTextures builds both at 128x72.
    // The table is deliberately exact on both name and dimensions. The key
    // alone cannot tell a data-built texture's made-up name from a file-backed
    // texture's bare content path, so an unlisted name keeps today's refusal,
    // which names the key. Pinning the dimensions closes the allocation
    // surface too: a doctored snapshot cannot mint distinct keys out of made-up
    // sizes, so one process can ever materialize at most this table - a repeat
    // key reuses the registered wrapper through the detached lookup.
    private static readonly Dictionary<string, (int Width, int Height)> RecreatableRuntimeTextures =
        new Dictionary<string, (int Width, int Height)>(StringComparer.Ordinal) {
            ["dust-noise-a"] = (128, 72),
            ["dust-noise-b"] = (128, 72),
        };

    // Restore's last resort, asked by the graph only after the fresh key index,
    // the detached registry above, and the structural owner path all came up
    // empty, and only for a non-portable key. The population that lands here is
    // a texture an entity builds on first render: DustEdges creates its noise
    // textures in BeforeRender, so a fresh-room baseline that never rendered
    // holds null at the anchor's owner field, and exiting the captured map
    // disposed its instances out of VirtualContent.Assets.
    internal static object RecreateDetachedLiveResource(Type resourceType, string typedResourceKey) {
        if (resourceType != typeof(VirtualTexture) || string.IsNullOrWhiteSpace(typedResourceKey)) {
            return null;
        }
        string resourceKey = StripTypedKeyPrefix(typedResourceKey);
        if (resourceKey == null) {
            return null;
        }
        // The key reads name|WxH. The dimensions are the last segment so the
        // recomputed key round-trips through GetLiveResourceKey.
        int dimensionsSeparator = resourceKey.LastIndexOf('|');
        if (dimensionsSeparator <= 0) {
            return null;
        }
        string name = resourceKey.Substring(0, dimensionsSeparator);
        if (!RecreatableRuntimeTextures.TryGetValue(name, out (int Width, int Height) size)) {
            return null;
        }
        string[] dimensions = resourceKey.Substring(dimensionsSeparator + 1).Split('x');
        if (dimensions.Length != 2 ||
            !int.TryParse(dimensions[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width) ||
            !int.TryParse(dimensions[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int height) ||
            width != size.Width || height != size.Height) {
            return null;
        }
        // White because that is what DustEdges creates; the room regenerates
        // the noise within one cycle of the first render either way.
        return VirtualContent.CreateTexture(name, width, height, Color.White);
    }

    // DynamicData's per-type member cache. Every DynamicData instance's _Cache
    // field points at the process-wide entry in the static _CacheMap, and the
    // entry holds compiled FastReflection invokers - anonymous delegates no
    // fresh room can vouch for. It is pure memoization DynamicData rebuilds on
    // demand, so it is a live resource keyed by its target type: capture never
    // walks into it, and restore rebinds to (or builds) this process's own
    // entry. Surfaced by a mod attaching DynamicData to a room entity, which
    // removed every Set in the room over the cache's delegates.
    private static readonly Type DynamicDataCacheType =
        typeof(MonoMod.Utils.DynamicData).GetNestedType("_Cache_", BindingFlags.NonPublic);
    private static readonly FieldInfo DynamicDataCacheMapField =
        typeof(MonoMod.Utils.DynamicData).GetField("_CacheMap", BindingFlags.Static | BindingFlags.NonPublic);

    private static string GetDynamicDataCacheKey(object cache) {
        if (DynamicDataCacheMapField?.GetValue(null) is not IDictionary cacheMap) {
            return string.Empty;
        }
        foreach (DictionaryEntry entry in cacheMap) {
            if (ReferenceEquals(entry.Value, cache)) {
                Type target = (Type) entry.Key;
                return target.AssemblyQualifiedName ?? target.FullName ?? string.Empty;
            }
        }
        return string.Empty;
    }

    private static object ResolveDynamicDataCache(string typeName) {
        Type target = Type.GetType(typeName, throwOnError: false);
        if (target == null || DynamicDataCacheMapField?.GetValue(null) is not IDictionary cacheMap) {
            return null;
        }
        if (!cacheMap.Contains(target)) {
            // Building a DynamicData for the type is the public way to make
            // MonoMod publish the type's cache entry.
            _ = new MonoMod.Utils.DynamicData(target);
        }
        return cacheMap.Contains(target) ? cacheMap[target] : null;
    }

    // HookGen is the canonical registry for On.* hooks. Its keys retain the
    // exact delegate a mod registered, including the target instance for an
    // instance method. A registered target is process state only when a loaded
    // Everest module also owns it through a registry whose process lifetime is
    // known from that mod's load contract. A scalar dictionary key alone proves
    // identity, not lifetime: room objects can sit behind stable keys too.
    // Room-scoped objects can register hooks too, so registration alone is not
    // enough evidence to keep an object live across a StartPos clone.
    private const string HookOwnerKeyPrefix = "hook-owner:";
    private static readonly FieldInfo HookEndpointHooksField =
        typeof(Hook).Assembly
            .GetType("MonoMod.RuntimeDetour.HookGen.HookEndpointManager", throwOnError: false)
            ?.GetField("Hooks", BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly FieldInfo EverestModulesField =
        typeof(Everest).GetField("_Modules", BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly IReadOnlyDictionary<object, string> EmptyHookOwnerRegistrations =
        new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
    private static readonly AsyncLocal<IReadOnlyDictionary<object, string>>
        CurrentHookOwnerRegistrations =
            new AsyncLocal<IReadOnlyDictionary<object, string>>();

    private static object ResolveHookOwner(Type resourceType, string resourceKey) {
        object match = null;
        foreach (KeyValuePair<object, string> registration in GetHookOwnerRegistrations()) {
            if (registration.Key.GetType() != resourceType ||
                !string.Equals(registration.Value, resourceKey, StringComparison.Ordinal)) {
                continue;
            }
            if (match != null) {
                return null;
            }
            match = registration.Key;
        }
        return match;
    }

    internal static bool HasDeferredHookOwnerKey(Type resourceType, string typedResourceKey) {
        return resourceType != null &&
               StripTypedKeyPrefix(typedResourceKey)?.StartsWith(
                   HookOwnerKeyPrefix,
                   StringComparison.Ordinal) == true;
    }

    private static string GetHookOwnerResourceKey(object resource) {
        return resource != null &&
               GetHookOwnerRegistrations().TryGetValue(resource, out string resourceKey)
            ? resourceKey
            : string.Empty;
    }

    internal static bool IsLiveHookOwner(object resource) {
        return resource != null && GetHookOwnerRegistrations().ContainsKey(resource);
    }

    private static IReadOnlyDictionary<object, string> GetHookOwnerRegistrations() {
        // The hook and module registries form one operation snapshot. Outside
        // that scope there is no coherent ownership claim to make.
        return CurrentHookOwnerRegistrations.Value ?? EmptyHookOwnerRegistrations;
    }

    internal static IReadOnlyDictionary<object, string> CaptureHookOwnerRegistrations(
        IReadOnlyList<EverestModule> loadedModules = null,
        Func<EverestModule, FieldInfo, bool> isStableRegistry = null) {
        return BuildHookOwnerRegistrations(
            loadedModules ?? GetLoadedHookOwnerModules(),
            isStableRegistry ?? IsSupportedHookOwnerRegistry);
    }

    internal static IDisposable UseHookOwnerRegistrations() {
        IReadOnlyDictionary<object, string> previous = CurrentHookOwnerRegistrations.Value;
        if (previous == null) {
            CurrentHookOwnerRegistrations.Value = CaptureHookOwnerRegistrations();
        }
        return new HookOwnerRegistrationScope(previous);
    }

    internal static IDisposable UseHookOwnerRegistrations(
        IReadOnlyDictionary<object, string> registrations) {
        IReadOnlyDictionary<object, string> previous = CurrentHookOwnerRegistrations.Value;
        if (previous == null) {
            CurrentHookOwnerRegistrations.Value = registrations ?? EmptyHookOwnerRegistrations;
        }
        return new HookOwnerRegistrationScope(previous);
    }

    internal static bool AreHookOwnerRegistrationsCurrent(
        IReadOnlyDictionary<object, string> savedRegistrations,
        IReadOnlyDictionary<object, string> currentRegistrations) {
        if (savedRegistrations == null || savedRegistrations.Count == 0) {
            return true;
        }
        if (currentRegistrations == null) {
            return false;
        }
        foreach (KeyValuePair<object, string> savedRegistration in savedRegistrations) {
            if (!currentRegistrations.TryGetValue(savedRegistration.Key, out string currentKey) ||
                !string.Equals(savedRegistration.Value, currentKey, StringComparison.Ordinal)) {
                return false;
            }
        }
        return true;
    }

    private static IReadOnlyList<EverestModule> GetLoadedHookOwnerModules() {
        // HookGen's registry is private too. Read Everest's backing list in the
        // same snapshot so test reference assemblies and runtime wrappers cannot
        // make the two sources observe different moments.
        if (EverestModulesField == null) {
            throw UnsupportedEverestReflection("Everest._Modules");
        }
        return EverestModulesField.GetValue(null) as IReadOnlyList<EverestModule> ??
               throw UnsupportedEverestReflection("Everest._Modules value");
    }

    private static NotSupportedException UnsupportedEverestReflection(string member) {
        Version version = typeof(Everest).Assembly.GetName().Version;
        return new NotSupportedException(
            "Everest " + (version?.ToString() ?? "unknown") +
            " does not provide the required module registry member " + member + ".");
    }

    private static IReadOnlyDictionary<object, string> BuildHookOwnerRegistrations(
        IReadOnlyList<EverestModule> loadedModules,
        Func<EverestModule, FieldInfo, bool> isStableRegistry) {
        Dictionary<object, HashSet<MethodInfo>> methodsByTarget =
            new Dictionary<object, HashSet<MethodInfo>>(ReferenceEqualityComparer.Instance);
        if (HookEndpointHooksField == null) {
            throw AkronReconstructionGraph.UnsupportedDetourReflection(
                "HookGen.HookEndpointManager.Hooks");
        }
        if (HookEndpointHooksField.GetValue(null) is not IEnumerable hooks) {
            throw AkronReconstructionGraph.UnsupportedDetourReflection(
                "HookGen.HookEndpointManager.Hooks value");
        }
        Dictionary<object, string> moduleOwnerKeys = BuildModuleOwnerKeys(
            loadedModules,
            isStableRegistry);
        foreach (object entry in hooks) {
            PropertyInfo keyProperty = entry?.GetType().GetProperty("Key");
            if (keyProperty == null) {
                throw AkronReconstructionGraph.UnsupportedDetourReflection(
                    (entry?.GetType().FullName ?? "HookGen hook entry") + ".Key");
            }
            object key = keyProperty.GetValue(entry);
            FieldInfo delegateField = key?.GetType().GetField("Item2");
            if (delegateField == null) {
                throw AkronReconstructionGraph.UnsupportedDetourReflection(
                    (key?.GetType().FullName ?? "HookGen hook key") + ".Item2");
            }
            if (delegateField.GetValue(key) is not Delegate hook) {
                throw AkronReconstructionGraph.UnsupportedDetourReflection(
                    key.GetType().FullName + ".Item2 value");
            }
            foreach (Delegate invocation in hook.GetInvocationList()) {
                if (invocation.Target == null || !moduleOwnerKeys.ContainsKey(invocation.Target)) {
                    continue;
                }
                if (!methodsByTarget.TryGetValue(invocation.Target, out HashSet<MethodInfo> methods)) {
                    methods = new HashSet<MethodInfo>();
                    methodsByTarget[invocation.Target] = methods;
                }
                methods.Add(invocation.Method);
            }
        }
        Dictionary<object, string> registrations =
            new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
        foreach (KeyValuePair<object, HashSet<MethodInfo>> pair in methodsByTarget) {
            if (moduleOwnerKeys.TryGetValue(pair.Key, out string moduleOwnerKey)) {
                registrations[pair.Key] = HookOwnerKeyPrefix + moduleOwnerKey + "|" + string.Join(
                    ";",
                    pair.Value
                        .Select(GetHookMethodKey)
                        .Where(key => !string.IsNullOrWhiteSpace(key))
                        .OrderBy(key => key, StringComparer.Ordinal));
            }
        }
        return registrations;
    }

    private static Dictionary<object, string> BuildModuleOwnerKeys(
        IReadOnlyList<EverestModule> loadedModules,
        Func<EverestModule, FieldInfo, bool> isStableRegistry) {
        Dictionary<object, string> ownerKeys =
            new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
        foreach (EverestModule module in loadedModules.Where(module => module != null)) {
            for (Type type = module.GetType();
                 type != null && typeof(EverestModule).IsAssignableFrom(type);
                 type = type.BaseType) {
                foreach (FieldInfo field in type.GetFields(
                             BindingFlags.Instance |
                             BindingFlags.Public |
                             BindingFlags.NonPublic |
                             BindingFlags.DeclaredOnly)) {
                    if (!isStableRegistry(module, field)) {
                        continue;
                    }
                    object value = field.GetValue(module);
                    if (value is not IDictionary dictionary) {
                        continue;
                    }
                    string fieldKey = (module.GetType().AssemblyQualifiedName ?? module.GetType().FullName) + "|" +
                                      (field.DeclaringType?.AssemblyQualifiedName ?? field.DeclaringType?.FullName) +
                                      "|" + field.Name;
                    foreach (DictionaryEntry entry in dictionary) {
                        if (entry.Value == null ||
                            entry.Value.GetType().Assembly != module.GetType().Assembly) {
                            continue;
                        }
                        string dictionaryKey = GetStableModuleDictionaryKey(entry.Key);
                        if (!string.IsNullOrWhiteSpace(dictionaryKey)) {
                            ownerKeys.TryGetValue(entry.Value, out string currentOwnerKey);
                            ownerKeys[entry.Value] = EarlierOrdinalKey(
                                currentOwnerKey,
                                fieldKey + "|" + dictionaryKey);
                        }
                    }
                }
            }
        }
        return ownerKeys;
    }

    private static bool IsSupportedHookOwnerRegistry(EverestModule module, FieldInfo field) {
        Type moduleType = module?.GetType();
        return moduleType?.Assembly.GetName().Name == "XaphanHelper" &&
               moduleType.FullName == "Celeste.Mod.XaphanHelper.XaphanModule" &&
               field?.DeclaringType == moduleType &&
               field.Name == "UpgradeHandlers";
    }

    private static string EarlierOrdinalKey(string current, string candidate) {
        return string.IsNullOrEmpty(current) || string.CompareOrdinal(candidate, current) < 0
            ? candidate
            : current;
    }

    private static string GetStableModuleDictionaryKey(object key) {
        if (key == null) {
            return string.Empty;
        }
        Type keyType = key.GetType();
        if (!keyType.IsEnum &&
            key is not string &&
            key is not char &&
            key is not bool &&
            key is not byte &&
            key is not sbyte &&
            key is not short &&
            key is not ushort &&
            key is not int &&
            key is not uint &&
            key is not long &&
            key is not ulong &&
            key is not Guid) {
            return string.Empty;
        }
        string value = key is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : key.ToString();
        return (keyType.AssemblyQualifiedName ?? keyType.FullName) + "|" + value;
    }

    private static string GetHookMethodKey(MethodInfo method) {
        if (method?.DeclaringType == null) {
            return string.Empty;
        }
        string declaringType = method.DeclaringType.AssemblyQualifiedName ?? method.DeclaringType.FullName;
        string parameters = string.Join(",", method.GetParameters().Select(parameter =>
            parameter.ParameterType.AssemblyQualifiedName ?? parameter.ParameterType.FullName));
        return declaringType + "|" + method.Name + "(" + parameters + ")";
    }

    private sealed class HookOwnerRegistrationScope : IDisposable {
        private readonly IReadOnlyDictionary<object, string> previous;

        public HookOwnerRegistrationScope(IReadOnlyDictionary<object, string> previous) {
            this.previous = previous;
        }

        public void Dispose() {
            CurrentHookOwnerRegistrations.Value = previous;
        }
    }

    // Where the game keeps the atlases it loads for the whole process. Both are
    // plain static fields, and Everest rebinds them in place when it reloads an
    // atlas, so the field is read on every lookup rather than the object being
    // cached here. The fields are found by type rather than by name, so an
    // atlas either type gains is covered without being listed.
    //
    // These three types are deliberately the whole list. There is no process-wide
    // registry of Atlas instances to enumerate - Monocle does not keep one and
    // Everest does not add one - so an atlas a helper mod built and kept to
    // itself is still refused, by name, with its data path in the message.
    // Reaching those would mean walking the statics of every type in every
    // loaded assembly on a capture worker, which costs more than the refusal it
    // would avoid. OVR is the overworld atlas: chapter-panel UI reaches it
    // through button callbacks a room can retain, and a Spring Collab 2020
    // capture was refused over exactly that key when only GFX and MTN were
    // listed here.
    private static readonly Type[] ContentAtlasOwners = { typeof(GFX), typeof(MTN), typeof(OVR) };

    // Two of these carrying one key would be two distinct objects the rebuilt
    // room cannot tell apart, and Atlas is deliberately absent from
    // AreEquivalentLiveResources, so handing back whichever came first would be
    // the quiet fold this file refuses everywhere else. Returning nothing
    // instead falls through to a refusal that names the key. No two atlases the
    // game loads share one: the key carries the data path, and each field is
    // loaded from a path of its own.
    private static Atlas ResolveContentAtlas(Type resourceType, string resourceKey) {
        Atlas match = null;
        foreach (Type owner in ContentAtlasOwners) {
            foreach (FieldInfo field in owner.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)) {
                if (!typeof(Atlas).IsAssignableFrom(field.FieldType) ||
                    field.GetValue(null) is not Atlas atlas ||
                    atlas.GetType() != resourceType ||
                    !string.Equals(GetLiveResourceKey(atlas), resourceKey, StringComparison.Ordinal)) {
                    continue;
                }
                if (match != null && !ReferenceEquals(match, atlas)) {
                    return null;
                }
                match = atlas;
            }
        }
        return match;
    }

    internal static string GetLiveResourceKey(object resource) {
        string hookOwnerKey = GetHookOwnerResourceKey(resource);
        if (!string.IsNullOrWhiteSpace(hookOwnerKey)) {
            return hookOwnerKey;
        }
        if (resource.GetType() == DynamicDataCacheType) {
            return GetDynamicDataCacheKey(resource);
        }
        if (resource is CompareInfo compareInfo) {
            // This names a collation, not a particular wrapper object. The sort
            // name carries alternate sort orders too - "de-DE_phoneb" is a
            // different collation from "de-DE" and gets a different key - so
            // two wrappers sharing this key sort identically, and one process
            // can hold several of them. It names the sort rather than its data
            // across processes: an install whose globalization data is a
            // different version, or which runs NLS where the saved one ran ICU,
            // orders some strings differently under the same name. Pinning the
            // collation version here would only refuse a slot over a difference
            // the player cannot act on, and the rebuilt room would have used
            // the local data anyway.
            return CompareInfoSortNameKeyPrefix + compareInfo.Name;
        }
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
        if (resource is VirtualTexture texture &&
            VirtualTexturePathField.GetValue(texture) is string texturePath &&
            !string.IsNullOrWhiteSpace(texturePath)) {
            return texturePath + "|" +
                   ((int) VirtualAssetWidthField.GetValue(texture)).ToString(CultureInfo.InvariantCulture) + "x" +
                   ((int) VirtualAssetHeightField.GetValue(texture)).ToString(CultureInfo.InvariantCulture);
        }
        if (resource is VirtualAsset asset &&
            VirtualAssetNameField.GetValue(asset) is string assetName &&
            !string.IsNullOrWhiteSpace(assetName)) {
            return assetName + "|" +
                   ((int) VirtualAssetWidthField.GetValue(asset)).ToString(CultureInfo.InvariantCulture) + "x" +
                   ((int) VirtualAssetHeightField.GetValue(asset)).ToString(CultureInfo.InvariantCulture);
        }
        return string.Empty;
    }
}
