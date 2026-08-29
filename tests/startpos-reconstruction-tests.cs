using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoMod.RuntimeDetour;
using Monocle;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class StartPosReconstructionTests {
    private const BindingFlags RuntimeInstanceFields =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // Serializes a structurally invalid document the way a hostile writer would:
    // with a real v10 type-name table, but without the validation that keeps
    // graph.Serialize from ever producing such a file.
    private static string SerializeCraftedDocument(AkronReconstructionDocument document) {
        AkronReconstructionGraph.BuildTypeNameTable(document);
        return Newtonsoft.Json.JsonConvert.SerializeObject(document);
    }

    [Fact]
    public void SoundSourceProvidesItsExactEventPathWhenFmodDropsTheDormantDescription() {
        SoundSource soundSource = (SoundSource) RuntimeHelpers.GetUninitializedObject(typeof(SoundSource));
        soundSource.EventName = "event:/env/local/09_core/lavagate_idle";

        string path = AkronEventInstanceUtils.GetOwnerEventPath(soundSource, "instance");

        Assert.Equal("event:/env/local/09_core/lavagate_idle", path);
    }

    [Fact]
    public void SavedSoundEmitterUsesItsExactEventPathForReconstruction() {
        AkronReconstructionNode emitter = new AkronReconstructionNode { Id = 1 };
        emitter.Fields.Add(new AkronReconstructionField {
            Name = "<Source>k__BackingField",
            Value = new AkronReconstructionValue { Kind = AkronReconstructionGraph.ReferenceValueKind, NodeId = 2 }
        });
        AkronReconstructionNode source = new AkronReconstructionNode { Id = 2 };
        source.Fields.Add(new AkronReconstructionField {
            Name = nameof(SoundSource.EventName),
            Value = new AkronReconstructionValue {
                Kind = AkronReconstructionGraph.ScalarValueKind,
                TypeName = typeof(string).AssemblyQualifiedName!,
                Scalar = "event:/game/general/touchswitch_last_oneshot"
            }
        });

        string eventName = AkronReconstructionGraph.GetSavedSoundEmitterEventName(
            emitter,
            new Dictionary<int, AkronReconstructionNode> { [1] = emitter, [2] = source });

        Assert.Equal("event:/game/general/touchswitch_last_oneshot", eventName);
    }

    [Fact]
    public void EverestModAssetsAreTreatedAsLiveLoaderResources() {
        Assert.True(AkronStartPosReconstruction.IsLiveResourceType(typeof(ModAsset)));
    }

    [Fact]
    public void TextureAndModAssetKeysIdentifyEquivalentFreshResources() {
        Assert.True(AkronStartPosReconstruction.AreEquivalentLiveResources(typeof(VirtualTexture)));
        Assert.True(AkronStartPosReconstruction.AreEquivalentLiveResources(typeof(ModAsset)));
        Assert.False(AkronStartPosReconstruction.AreEquivalentLiveResources(typeof(Atlas)));
    }

    [Fact]
    public void DetachedVirtualTextureResolvesFromTheVirtualContentRegistry() {
        VirtualTexture texture = (VirtualTexture) RuntimeHelpers.GetUninitializedObject(typeof(VirtualTexture));
        SetRuntimeField(texture, "<Path>k__BackingField", "Graphics/Atlases/Gameplay/decals/randomized");
        SetRuntimeField(texture, "<Width>k__BackingField", 32);
        SetRuntimeField(texture, "<Height>k__BackingField", 32);
        List<VirtualAsset>? assets = GetRuntimeStaticField<List<VirtualAsset>?>(typeof(VirtualContent), "assets");
        if (assets == null) {
            assets = new List<VirtualAsset>();
            SetRuntimeStaticField(typeof(VirtualContent), "assets", assets);
        }
        assets.Add(texture);
        try {
            string key = typeof(VirtualTexture).AssemblyQualifiedName + "|" +
                         AkronStartPosReconstruction.GetLiveResourceKey(texture);

            object resolved = AkronStartPosReconstruction.ResolveDetachedLiveResource(
                typeof(VirtualTexture),
                key);

            Assert.Same(texture, resolved);
        } finally {
            assets.Remove(texture);
        }
    }

    [Fact]
    public void EverestSettingsHaveAStableDetachedResourceKey() {
        TestEverestSettings settings = new TestEverestSettings();

        string key = AkronStartPosReconstruction.GetLiveResourceKey(settings);

        Assert.Equal(typeof(TestEverestSettings).AssemblyQualifiedName, key);
    }

    [Fact]
    public void PersistentRuntimeRootRestoresRunScopedGlobalsAndModuleSessionsTogether() {
        TestSharedState savedShared = new TestSharedState { Value = 42 };
        AkronPersistentRuntimeState saved = new AkronPersistentRuntimeState {
            GrabMode = GrabModes.Invert,
            CrouchDashMode = (CrouchDashModes) 2,
            EngineTimeRate = 0.5f,
            GlitchValue = 0.25f,
            DistortAnxiety = 0.75f,
            DistortGameRate = 1.5f
        };
        saved.ModuleSessions["helper"] = new TestEverestSession { Shared = savedShared };

        TestSharedState baselineShared = new TestSharedState();
        AkronPersistentRuntimeState baseline = new AkronPersistentRuntimeState();
        baseline.ModuleSessions["helper"] = new TestEverestSession { Shared = baselineShared };

        AkronReconstructionGraph graph = new AkronReconstructionGraph(
            AkronStartPosReconstruction.IsLiveResourceType,
            resource => ((Type) resource).AssemblyQualifiedName,
            null,
            AkronStartPosReconstruction.ResolveDetachedLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionDocument document = graph.Deserialize(graph.Serialize(capture.Document));

        TestSharedState freshShared = new TestSharedState();
        AkronPersistentRuntimeState fresh = new AkronPersistentRuntimeState();
        fresh.ModuleSessions["helper"] = new TestEverestSession { Shared = freshShared };
        AkronReconstructionRestore restore = graph.Restore(document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Equal(GrabModes.Invert, fresh.GrabMode);
        Assert.Equal((CrouchDashModes) 2, fresh.CrouchDashMode);
        Assert.Equal(0.5f, fresh.EngineTimeRate);
        Assert.Equal(0.25f, fresh.GlitchValue);
        Assert.Equal(0.75f, fresh.DistortAnxiety);
        Assert.Equal(1.5f, fresh.DistortGameRate);
        TestEverestSession restoredSession = Assert.IsType<TestEverestSession>(fresh.ModuleSessions["helper"]);
        Assert.Equal(42, restoredSession.Shared.Value);
        Assert.True(graph.Verify(document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void RestoreResolvesALoadedRuntimeTypeMissingFromTheFreshTrackerGraph() {
        RuntimeTypeRoot saved = new RuntimeTypeRoot { TrackerKey = typeof(TextMenu) };
        RuntimeTypeRoot baseline = new RuntimeTypeRoot { TrackerKey = typeof(TextMenu) };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(
            AkronStartPosReconstruction.IsLiveResourceType,
            resource => ((Type) resource).AssemblyQualifiedName,
            null,
            AkronStartPosReconstruction.ResolveDetachedLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionDocument document = graph.Deserialize(graph.Serialize(capture.Document));
        RuntimeTypeRoot fresh = new RuntimeTypeRoot();

        AkronReconstructionRestore restore = graph.Restore(document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Same(typeof(TextMenu), fresh.TrackerKey);
        Assert.True(graph.Verify(document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void CaptureAndRestoreResolveAFieldInfoMissingFromTheFreshGraph() {
        FieldInfo member = typeof(string).GetField(nameof(string.Empty))!;
        RuntimeMemberRoot saved = new RuntimeMemberRoot { Member = member };
        RuntimeMemberRoot baseline = new RuntimeMemberRoot();
        AkronReconstructionGraph graph = new AkronReconstructionGraph(
            AkronStartPosReconstruction.IsLiveResourceType,
            AkronStartPosReconstruction.GetLiveResourceKey,
            null,
            AkronStartPosReconstruction.ResolveDetachedLiveResource);

        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionDocument document = graph.Deserialize(graph.Serialize(capture.Document));
        RuntimeMemberRoot fresh = new RuntimeMemberRoot();
        AkronReconstructionRestore restore = graph.Restore(document, fresh);

        Assert.True(restore.Success, restore.Error);
        FieldInfo restored = Assert.IsAssignableFrom<FieldInfo>(fresh.Member);
        Assert.Equal(member.Module, restored.Module);
        Assert.Equal(member.MetadataToken, restored.MetadataToken);
        Assert.True(graph.Verify(document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void CollectionVersionCountersAreDerivedBookkeeping() {
        Assert.True(AkronReconstructionGraph.IsDerivedCollectionVersionField(
            typeof(List<int>),
            "_version"));
        Assert.True(AkronReconstructionGraph.IsDerivedCollectionVersionField(
            typeof(Dictionary<string, int>),
            "_version"));
        Assert.True(AkronReconstructionGraph.IsDerivedCollectionVersionField(
            typeof(HashSet<string>),
            "_version"));
        Assert.False(AkronReconstructionGraph.IsDerivedCollectionVersionField(
            typeof(List<int>.Enumerator),
            "_version"));
        Assert.False(AkronReconstructionGraph.IsDerivedCollectionVersionField(
            typeof(ScalarListRoot),
            "_version"));
    }

    [Fact]
    public void CollectionVersionChangesDoNotInvalidateEquivalentContents() {
        ScalarListRoot saved = new ScalarListRoot { Values = new List<int> { 3, 5, 8 } };
        ScalarListRoot baseline = new ScalarListRoot { Values = new List<int> { 3, 5, 8 } };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode listNode = capture.Document.Nodes.Single(node =>
            node.TypeName == typeof(List<int>).AssemblyQualifiedName);
        Assert.DoesNotContain(listNode.Fields, field => field.Name == "_version");
        listNode.Fields.Add(new AkronReconstructionField {
            DeclaringTypeName = typeof(List<int>).AssemblyQualifiedName!,
            Name = "_version",
            Path = listNode.Path + "._version",
            Value = new AkronReconstructionValue {
                Kind = AkronReconstructionGraph.ScalarValueKind,
                TypeName = typeof(int).AssemblyQualifiedName!,
                Scalar = "99"
            }
        });
        ScalarListRoot fresh = new ScalarListRoot { Values = new List<int> { 3, 5, 8 } };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);
        Assert.True(restore.Success, restore.Error);
        int restoredVersion = (int) GetRuntimeFieldInfo(typeof(List<int>), "_version")
            .GetValue(fresh.Values)!;
        SetRuntimeField(fresh.Values, "_version", restoredVersion + 1);

        Assert.Equal(new[] { 3, 5, 8 }, fresh.Values);
        AkronReconstructionVerification verification = graph.Verify(
            capture.Document,
            restore,
            Array.Empty<string>());
        Assert.True(verification.Success, verification.Error);
    }

    [Fact]
    public void ReconstructionRestoresCyclesSharedReferencesAndCallbacksOntoFreshObjects() {
        TestResource savedResource = new TestResource("saved-process");
        TestNode savedChild = new TestNode {
            Name = "child",
            Value = 37,
            Resource = savedResource
        };
        TestRoot saved = new TestRoot {
            Counter = 91,
            Primary = savedChild,
            Secondary = savedChild,
            Resource = savedResource
        };
        savedChild.Parent = saved;
        saved.Callback = savedChild.Increment;

        TestResource baselineResource = new TestResource("capture-baseline");
        TestNode baselineChild = new TestNode {
            Name = "child",
            Resource = baselineResource
        };
        TestRoot baseline = new TestRoot {
            Primary = baselineChild,
            Secondary = baselineChild,
            Resource = baselineResource
        };
        baselineChild.Parent = baseline;
        baseline.Callback = baselineChild.Increment;

        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);

        Assert.True(capture.Success, capture.Error);
        string json = graph.Serialize(capture.Document);
        AkronReconstructionDocument document = graph.Deserialize(json);

        TestResource freshResource = new TestResource("restored-process");
        TestNode freshChild = new TestNode {
            Name = "child",
            Resource = freshResource
        };
        TestRoot fresh = new TestRoot {
            Primary = freshChild,
            Secondary = freshChild,
            Resource = freshResource
        };
        freshChild.Parent = fresh;
        fresh.Callback = freshChild.Increment;

        AkronReconstructionRestore restore = graph.Restore(document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Equal(91, fresh.Counter);
        Assert.Equal(37, fresh.Primary.Value);
        Assert.Same(fresh.Primary, fresh.Secondary);
        Assert.Same(fresh, fresh.Primary.Parent);
        Assert.Same(freshResource, fresh.Resource);
        Assert.Same(freshResource, fresh.Primary.Resource);
        Assert.True(graph.Verify(document, restore, Array.Empty<string>()).Success);

        fresh.Callback();

        Assert.Equal(38, fresh.Primary.Value);
    }

    [Fact]
    public void RestoreRejectsADelegateMethodThatIsNotAuthenticToTheFreshGraph() {
        TestNode savedChild = new TestNode();
        TestRoot saved = new TestRoot { Primary = savedChild };
        saved.Callback = savedChild.Increment;
        TestNode baselineChild = new TestNode();
        TestRoot baseline = new TestRoot { Primary = baselineChild };
        baseline.Callback = baselineChild.Increment;
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode delegateNode = Assert.Single(
            capture.Document.Nodes,
            node => node.DelegateCalls.Count > 0);
        AkronReconstructionDelegateCall call = Assert.Single(delegateNode.DelegateCalls);
        MethodInfo untrustedMethod = typeof(StartPosReconstructionTests).GetMethod(
            nameof(UntrustedSnapshotCallback),
            BindingFlags.Static | BindingFlags.NonPublic)!;
        call.Target = new AkronReconstructionValue { Kind = AkronReconstructionGraph.NullValueKind };
        call.DeclaringTypeName = untrustedMethod.DeclaringType!.AssemblyQualifiedName!;
        call.MethodName = untrustedMethod.Name;
        call.ReturnTypeName = untrustedMethod.ReturnType.AssemblyQualifiedName!;
        call.ParameterTypeNames = new List<string>();
        TestNode freshChild = new TestNode();
        TestRoot fresh = new TestRoot { Primary = freshChild };
        fresh.Callback = freshChild.Increment;

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.False(restore.Success);
        Assert.Contains("delegate method is not authentic to the fresh room", restore.Error);
    }

    [Fact]
    public void RestoreRejectsADifferentMethodOnAnAuthenticFreshTarget() {
        TestNode savedChild = new TestNode();
        TestRoot saved = new TestRoot { Primary = savedChild };
        saved.Callback = savedChild.Increment;
        TestNode baselineChild = new TestNode();
        TestRoot baseline = new TestRoot { Primary = baselineChild };
        baseline.Callback = baselineChild.Increment;
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode delegateNode = Assert.Single(
            capture.Document.Nodes,
            node => node.DelegateCalls.Count > 0);
        AkronReconstructionDelegateCall call = Assert.Single(delegateNode.DelegateCalls);
        MethodInfo untrustedMethod = typeof(TestNode).GetMethod(nameof(TestNode.Reset))!;
        call.DeclaringTypeName = untrustedMethod.DeclaringType!.AssemblyQualifiedName!;
        call.MethodName = untrustedMethod.Name;
        call.ReturnTypeName = untrustedMethod.ReturnType.AssemblyQualifiedName!;
        call.ParameterTypeNames = new List<string>();
        TestNode freshChild = new TestNode();
        TestRoot fresh = new TestRoot { Primary = freshChild };
        fresh.Callback = freshChild.Increment;

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.False(restore.Success);
        Assert.Contains("delegate method is not authentic to the fresh room", restore.Error);
    }

    // A refused callback has to name the mod whose method it is, or the player gets the
    // graph's own text and nothing to act on. The delegate refusal reports the type that
    // declares the method rather than the field's own type, because the field is usually
    // a plain Action and the method is the only part of the edge a mod owns.
    //
    // The document is edited rather than captured twice, the same way the two refusals
    // above are built: the room this stands for is a mod that installs a different method
    // in that slot than the one it installed when the slot was set, and a snapshot naming
    // a method the fresh room does not run is exactly what that produces. The reader
    // cannot tell the two apart, and building it from two mod versions is not something a
    // unit test can do.
    [Fact]
    public void ARefusedDelegateMethodNamesTheModThatDeclaresIt() {
        TestNode savedChild = new TestNode();
        TestRoot saved = new TestRoot { Primary = savedChild };
        saved.Callback = savedChild.Increment;
        TestNode baselineChild = new TestNode();
        TestRoot baseline = new TestRoot { Primary = baselineChild };
        baseline.Callback = baselineChild.Increment;
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode delegateNode = Assert.Single(
            capture.Document.Nodes,
            node => node.DelegateCalls.Count > 0);
        AkronReconstructionDelegateCall call = Assert.Single(delegateNode.DelegateCalls);
        MethodInfo modMethod = typeof(ProbeHelperModA).GetMethod(
            nameof(ProbeHelperModA.Leave),
            BindingFlags.Static | BindingFlags.NonPublic)!;
        call.Target = new AkronReconstructionValue { Kind = AkronReconstructionGraph.NullValueKind };
        call.DeclaringTypeName = modMethod.DeclaringType!.AssemblyQualifiedName!;
        call.MethodName = modMethod.Name;
        call.ReturnTypeName = modMethod.ReturnType.AssemblyQualifiedName!;
        call.ParameterTypeNames = new List<string>();
        TestNode freshChild = new TestNode();
        TestRoot fresh = new TestRoot { Primary = freshChild };
        fresh.Callback = freshChild.Increment;

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.False(restore.Success);
        Assert.Contains("delegate method is not authentic to the fresh room", restore.Error);
        Assert.Contains(";method=" + nameof(ProbeHelperModA.Leave), restore.Error);
        // The type the load message is built from, and the sentence it builds.
        Assert.Equal(typeof(ProbeHelperModA).AssemblyQualifiedName, restore.RefusedTypeName);
        Assert.Equal(
            "StartPos 3 needs ProbeHelperModA from SampleHelper, and this room does not have it. " +
            "Check that mod's settings, or set the slot again.",
            AkronStartPosRefusal.Describe(
                "StartPos 3",
                restore.RefusedTypeName,
                AkronReconstructionRefusalKind.SavedObject,
                new[] { ("SampleHelper", typeof(ProbeHelperModA).Assembly.GetName().Name!) }));
    }

    [Fact]
    public void RestoreRejectsAnArrayAllocationLargerThanTheSnapshotLimit() {
        TestRoot saved = new TestRoot { Numbers = new[] { 1 } };
        TestRoot baseline = new TestRoot { Numbers = new[] { 0 } };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode arrayNode = Assert.Single(
            capture.Document.Nodes,
            node => node.ParentFieldName == nameof(TestRoot.Numbers));
        arrayNode.UseFreshObject = false;
        arrayNode.ArrayLengths[0] = int.MaxValue;
        TestRoot fresh = new TestRoot { Numbers = new[] { 0 } };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.False(restore.Success);
        Assert.Contains("array allocation exceeds", restore.Error);
    }

    [Fact]
    public void SerializeAndDeserializeRejectAParentChainThatExceedsTheDepthLimit() {
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        const int nodeCount = 1000;
        AkronReconstructionDocument document = new AkronReconstructionDocument { RootNodeId = 1 };
        for (int id = nodeCount; id >= 2; id--) {
            AkronReconstructionNode node = new AkronReconstructionNode {
                Id = id,
                Kind = "object",
                TypeName = typeof(TestNode).AssemblyQualifiedName!,
                ParentNodeId = id - 1,
                ParentKind = "field",
                ParentDeclaringTypeName = typeof(TestNode).AssemblyQualifiedName!,
                ParentFieldName = "Next"
            };
            if (id < nodeCount) {
                node.Fields.Add(new AkronReconstructionField {
                    DeclaringTypeName = typeof(TestNode).AssemblyQualifiedName!,
                    Name = "Next",
                    Value = new AkronReconstructionValue { Kind = AkronReconstructionGraph.ReferenceValueKind, NodeId = id + 1 }
                });
            }
            document.Nodes.Add(node);
        }
        AkronReconstructionNode root = new AkronReconstructionNode {
            Id = 1,
            Kind = "object",
            TypeName = typeof(TestNode).AssemblyQualifiedName!
        };
        root.Fields.Add(new AkronReconstructionField {
            DeclaringTypeName = typeof(TestNode).AssemblyQualifiedName!,
            Name = "Next",
            Value = new AkronReconstructionValue { Kind = AkronReconstructionGraph.ReferenceValueKind, NodeId = 2 }
        });
        document.Nodes.Add(root);

        InvalidOperationException deserializeException = Assert.Throws<InvalidOperationException>(
            () => graph.Deserialize(SerializeCraftedDocument(document)));
        InvalidOperationException serializeException = Assert.Throws<InvalidOperationException>(
            () => graph.Serialize(document));

        Assert.Contains("parent depth exceeds", deserializeException.Message);
        Assert.Contains("parent depth exceeds", serializeException.Message);
    }

    [Fact]
    public void SerializeAndDeserializeRejectDiagnosticPathsPastTheSizeLimit() {
        ChainNode saved = BuildChain(64, valueOffset: 1000);
        ChainNode baseline = BuildChain(64, valueOffset: 0);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        Dictionary<int, AkronReconstructionNode> nodes = capture.Document.Nodes.ToDictionary(node => node.Id);
        foreach (AkronReconstructionNode child in capture.Document.Nodes.Where(node => node.ParentNodeId > 0)) {
            AkronReconstructionNode parent = nodes[child.ParentNodeId];
            AkronReconstructionField parentField = parent.Fields.Single(field =>
                field.Value?.Kind == AkronReconstructionGraph.ReferenceValueKind && field.Value.NodeId == child.Id);
            string longFieldName = parentField.Name + new string('x', 1024);
            parentField.Name = longFieldName;
            child.ParentFieldName = longFieldName;
        }

        InvalidOperationException deserializeException = Assert.Throws<InvalidOperationException>(
            () => graph.Deserialize(SerializeCraftedDocument(capture.Document)));
        InvalidOperationException serializeException = Assert.Throws<InvalidOperationException>(
            () => graph.Serialize(capture.Document));

        Assert.Contains("diagnostic path exceeds", deserializeException.Message);
        Assert.Contains("diagnostic path exceeds", serializeException.Message);
    }

    [Fact]
    public void DeserializeRejectsAFieldWithoutItsValue() {
        TestNode savedChild = new TestNode();
        TestNode baselineChild = new TestNode();
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new TestRoot { Primary = savedChild, Secondary = savedChild },
            new TestRoot { Primary = baselineChild, Secondary = baselineChild });
        Assert.True(capture.Success, capture.Error);

        JObject json = JObject.Parse(graph.Serialize(capture.Document));
        JObject root = json[nameof(AkronReconstructionDocument.Nodes)]!
            .Children<JObject>()
            .Single(node => node["i"]!.Value<int>() == capture.Document.RootNodeId);
        JObject secondary = root[AkronReconstructionTags.Fields]!
            .Children<JObject>()
            .Single(field => field[AkronReconstructionTags.FieldName]!.Value<string>() == nameof(TestRoot.Secondary));
        Assert.True(secondary.Remove("v"));

        Newtonsoft.Json.JsonSerializationException exception =
            Assert.Throws<Newtonsoft.Json.JsonSerializationException>(() =>
                graph.Deserialize(json.ToString(Newtonsoft.Json.Formatting.None)));

        Assert.Contains("Required property 'v'", exception.Message);
    }

    [Fact]
    public void DeserializeRejectsADelegateCallWithoutItsTarget() {
        TestNode savedChild = new TestNode();
        TestNode baselineChild = new TestNode();
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new TestRoot { Primary = savedChild, Callback = savedChild.Increment },
            new TestRoot { Primary = baselineChild, Callback = baselineChild.Increment });
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode delegateNode = Assert.Single(
            capture.Document.Nodes,
            node => node.DelegateCalls.Count > 0);

        JObject json = JObject.Parse(graph.Serialize(capture.Document));
        JObject serializedDelegate = json[nameof(AkronReconstructionDocument.Nodes)]!
            .Children<JObject>()
            .Single(node => node["i"]!.Value<int>() == delegateNode.Id);
        JObject call = Assert.Single(
            serializedDelegate[AkronReconstructionTags.DelegateCalls]!.Children<JObject>());
        Assert.True(call.Remove("tg"));

        Newtonsoft.Json.JsonSerializationException exception =
            Assert.Throws<Newtonsoft.Json.JsonSerializationException>(() =>
                graph.Deserialize(json.ToString(Newtonsoft.Json.Formatting.None)));

        Assert.Contains("Required property 'tg'", exception.Message);
    }

    [Fact]
    public void DeserializeRebuildsNonemptyPathsFromCapturedParentEdges() {
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            BuildChain(3, valueOffset: 10),
            BuildChain(3, valueOffset: 0));
        Assert.True(capture.Success, capture.Error);

        AkronReconstructionDocument document = graph.Deserialize(graph.Serialize(capture.Document));

        Assert.All(document.Nodes, node => Assert.False(string.IsNullOrWhiteSpace(node.Path)));
        Assert.All(document.Nodes.Where(node => node.Id != document.RootNodeId), node =>
            Assert.StartsWith("$.", node.Path, StringComparison.Ordinal));
    }

    [Fact]
    public void DeserializeRejectsParentCycles() {
        string nodeType = typeof(ChainNode).AssemblyQualifiedName!;
        AkronReconstructionDocument document = new AkronReconstructionDocument { RootNodeId = 1 };
        AkronReconstructionNode root = new AkronReconstructionNode {
            Id = 1,
            Kind = "object",
            TypeName = nodeType,
            Path = "$"
        };
        root.Fields.Add(new AkronReconstructionField {
            DeclaringTypeName = nodeType,
            Name = "Next",
            Value = new AkronReconstructionValue { Kind = AkronReconstructionGraph.ReferenceValueKind, NodeId = 2 }
        });
        AkronReconstructionNode first = new AkronReconstructionNode {
            Id = 2,
            Kind = "object",
            TypeName = nodeType,
            ParentNodeId = 3,
            ParentKind = "field",
            ParentDeclaringTypeName = nodeType,
            ParentFieldName = "Next"
        };
        first.Fields.Add(new AkronReconstructionField {
            DeclaringTypeName = nodeType,
            Name = "Next",
            Value = new AkronReconstructionValue { Kind = AkronReconstructionGraph.ReferenceValueKind, NodeId = 3 }
        });
        AkronReconstructionNode second = new AkronReconstructionNode {
            Id = 3,
            Kind = "object",
            TypeName = nodeType,
            ParentNodeId = 2,
            ParentKind = "field",
            ParentDeclaringTypeName = nodeType,
            ParentFieldName = "Next"
        };
        second.Fields.Add(new AkronReconstructionField {
            DeclaringTypeName = nodeType,
            Name = "Next",
            Value = new AkronReconstructionValue { Kind = AkronReconstructionGraph.ReferenceValueKind, NodeId = 2 }
        });
        document.Nodes.Add(root);
        document.Nodes.Add(first);
        document.Nodes.Add(second);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            graph.Deserialize(SerializeCraftedDocument(document)));

        Assert.Contains("parent cycle is invalid", exception.Message);
    }

    [Fact]
    public void DeserializeRejectsDuplicateFieldIdentitiesBeforeResolvingParentEdges() {
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new TestRoot { Primary = new TestNode() },
            new TestRoot { Primary = new TestNode() });
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode root = capture.Document.Nodes.Single(node =>
            node.Id == capture.Document.RootNodeId);
        AkronReconstructionField primary = root.Fields.Single(field =>
            field.Name == nameof(TestRoot.Primary));
        root.Fields.Add(new AkronReconstructionField {
            DeclaringTypeName = primary.DeclaringTypeName,
            Name = primary.Name,
            Value = primary.Value
        });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            graph.Deserialize(graph.Serialize(capture.Document)));

        Assert.Contains("parent field identity is duplicated", exception.Message);
    }

    [Fact]
    public void DeserializeRejectsANodeWhoseClaimedParentDoesNotReferenceIt() {
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new TestRoot { Counter = 91 },
            new TestRoot());
        Assert.True(capture.Success, capture.Error);
        capture.Document.Nodes.Add(new AkronReconstructionNode {
            Id = capture.Document.Nodes.Max(node => node.Id) + 1,
            Kind = "object",
            TypeName = typeof(TestNode).AssemblyQualifiedName!,
            ParentNodeId = capture.Document.RootNodeId,
            ParentKind = "field",
            ParentDeclaringTypeName = typeof(TestRoot).AssemblyQualifiedName!,
            ParentFieldName = nameof(TestRoot.Primary)
        });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            graph.Deserialize(SerializeCraftedDocument(capture.Document)));

        Assert.Contains("parent edge is invalid", exception.Message);
    }

    [Fact]
    public void RestoreRejectsAnUntrustedSubtypeThatWasNotAtTheFreshStructuralPath() {
        Entity savedEntity = CreateUninitializedEntity<Entity>();
        Entity baselineEntity = CreateUninitializedEntity<Entity>();
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new TestRoot { RoomEntity = savedEntity },
            new TestRoot { RoomEntity = baselineEntity });
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode entityNode = capture.Document.Nodes.Single(node =>
            node.ParentFieldName == nameof(TestRoot.RoomEntity));
        entityNode.TypeName = typeof(UniqueTestEntity).AssemblyQualifiedName!;
        entityNode.UseFreshObject = false;

        AkronReconstructionRestore restore = graph.Restore(
            capture.Document,
            new TestRoot { RoomEntity = CreateUninitializedEntity<Entity>() });

        Assert.False(restore.Success);
        Assert.Contains("authentic to the fresh room", restore.Error);
    }

    [Fact]
    public void RestoreRejectsACelesteGameplaySubtypeThatWasNotInTheFreshRoom() {
        Entity savedEntity = CreateUninitializedEntity<Entity>();
        Entity baselineEntity = CreateUninitializedEntity<Entity>();
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new TestRoot { RoomEntity = savedEntity },
            new TestRoot { RoomEntity = baselineEntity });
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode entityNode = capture.Document.Nodes.Single(node =>
            node.ParentFieldName == nameof(TestRoot.RoomEntity));
        entityNode.TypeName = typeof(Player).AssemblyQualifiedName!;
        entityNode.UseFreshObject = false;

        AkronReconstructionRestore restore = graph.Restore(
            capture.Document,
            new TestRoot { RoomEntity = CreateUninitializedEntity<Entity>() });

        Assert.False(restore.Success);
        Assert.Contains("type is not authentic to the fresh room", restore.Error);
    }

    // The load-failure message a player reads is built from the refused type, so the
    // refusal has to carry it as data. Reading it back out of the flag text would break
    // the moment a flag is added.
    [Fact]
    public void ARefusedTypeReachesTheRestoreResultAlongsideItsDiagnosticText() {
        Entity savedEntity = CreateUninitializedEntity<Entity>();
        Entity baselineEntity = CreateUninitializedEntity<Entity>();
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new TestRoot { RoomEntity = savedEntity },
            new TestRoot { RoomEntity = baselineEntity });
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode entityNode = capture.Document.Nodes.Single(node =>
            node.ParentFieldName == nameof(TestRoot.RoomEntity));
        entityNode.TypeName = typeof(Player).AssemblyQualifiedName!;
        entityNode.UseFreshObject = false;

        AkronReconstructionRestore restore = graph.Restore(
            capture.Document,
            new TestRoot { RoomEntity = CreateUninitializedEntity<Entity>() });

        Assert.False(restore.Success);
        Assert.Equal(typeof(Player).AssemblyQualifiedName, restore.RefusedTypeName);
    }

    // An uninstalled or disabled mod shows up as a saved type this process cannot load
    // at all, which is a different refusal site from the one above and needs the same
    // name to reach the message.
    [Fact]
    public void ARefusedTypeThatWillNotLoadStillReachesTheRestoreResult() {
        const string missingModType =
            "SampleVariants.Variants.SampleZoom+<>c, SampleVariantMode, Version=1.0.0.0, " +
            "Culture=neutral, PublicKeyToken=null";
        Entity savedEntity = CreateUninitializedEntity<Entity>();
        Entity baselineEntity = CreateUninitializedEntity<Entity>();
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new TestRoot { RoomEntity = savedEntity },
            new TestRoot { RoomEntity = baselineEntity });
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode entityNode = capture.Document.Nodes.Single(node =>
            node.ParentFieldName == nameof(TestRoot.RoomEntity));
        entityNode.TypeName = missingModType;
        entityNode.UseFreshObject = false;

        AkronReconstructionRestore restore = graph.Restore(
            capture.Document,
            new TestRoot { RoomEntity = CreateUninitializedEntity<Entity>() });

        Assert.False(restore.Success);
        Assert.Contains("type is unavailable", restore.Error);
        Assert.Equal(missingModType, restore.RefusedTypeName);
    }

    // The production case: ExtendedVariantMode is loaded, its hooks are not installed
    // because the master switch is off, and the room really is missing what the slot
    // saved. The player can fix that and the message has to say so.
    [Fact]
    public void ARefusalOnALoadedModNamesTheModAndWhatToDoAboutIt() {
        // A real cached-closure singleton in an assembly that is neither the game's nor
        // Akron's, which is the shape ExtendedVariantMode's ZoomLevel refusal lands on.
        string refusedTypeName = SampleZoomLevel.Callback.Target!.GetType().AssemblyQualifiedName!;

        string message = AkronStartPosRefusal.Describe(
            "StartPos 3",
            refusedTypeName,
            AkronReconstructionRefusalKind.SavedObject,
            new[] { ("SampleVariantMode", typeof(SampleZoomLevel).Assembly.GetName().Name!) });

        Assert.Equal(
            "StartPos 3 needs SampleZoomLevel from SampleVariantMode, and this room does not have it. " +
            "Check that mod's settings, or set the slot again.",
            message);
        Assert.True(message!.Length <= AkronActions.MaxStartPosFailureToastLength);
    }

    [Fact]
    public void ARefusalOnAModThatIsNoLongerLoadedSaysToTurnItBackOn() {
        string message = AkronStartPosRefusal.Describe(
            "StartPos 3",
            "ExtendedVariants.Variants.ZoomLevel+<>c, ExtendedVariantMode, Version=0.50.3.0, " +
            "Culture=neutral, PublicKeyToken=null",
            AkronReconstructionRefusalKind.SavedObject,
            new[] { ("CelesteTAS", "CelesteTAS") });

        Assert.Equal(
            "StartPos 3 needs ZoomLevel from ExtendedVariantMode, which Akron cannot load now. " +
            "Turn that mod back on if you removed it, or set the slot again.",
            message);
        Assert.True(message!.Length <= AkronActions.MaxStartPosFailureToastLength);
    }

    // No mod owns Monocle.Sprite, so nothing the player can change explains a room that
    // reloads without it. That is Akron's defect to fix, and the message must not send
    // the player looking through their mod list for it.
    [Fact]
    public void ARefusalOnATypeNoModOwnsIsReportedAsAnAkronBug() {
        string message = AkronStartPosRefusal.Describe(
            "StartPos 3",
            typeof(Sprite).AssemblyQualifiedName!,
            AkronReconstructionRefusalKind.SavedObject,
            new[] { ("ExtendedVariantMode", "ExtendedVariantMode") });

        Assert.Equal(
            "StartPos 3 could not be rebuilt: this room has no Sprite to match, and no mod owns " +
            "it. If your mods have not changed, this is an Akron bug; report akron-current.log.",
            message);
        Assert.True(message!.Length <= AkronActions.MaxStartPosFailureToastLength);
    }

    // The map rule refuses because the map dropped an entity id it used to own, and the
    // type of the entity that carried that id decides nothing about it. Run through the
    // assembly split, a vanilla entity edited out of a collab room answers Celeste and
    // the player is asked to file a bug report about a refusal working exactly as
    // designed - with no mention of the one thing that fixes it. This is that room, and
    // it is refused through the real restore rather than by handing Describe a string.
    [Fact]
    public void AMapEditIsReportedAsAChangedMapRatherThanAsAnAkronBug() {
        PlaybackGhostReloadRoom fresh = CreateReloadedGhostRoomWithRenumberedGhost();

        AkronReconstructionRestore restore = RestoreTrailingGhostDocumentInto(
            fresh,
            mapIdsWhenSet: new[] { 42, 7 },
            mapIdsAtReload: new[] { 43, 7 });

        Assert.False(restore.Success);
        Assert.Contains("saved map entity is no longer placed by this map", restore.Error);
        // A vanilla Celeste entity, which is the case that produced the bug report.
        Assert.Equal(typeof(PlayerPlayback).AssemblyQualifiedName, restore.RefusedTypeName);
        Assert.Equal(AkronReconstructionRefusalKind.ChangedMap, restore.RefusedKind);

        string message = AkronStartPosRefusal.Describe(
            "StartPos 1",
            restore.RefusedTypeName,
            restore.RefusedKind,
            new[] { ("ExtendedVariantMode", "ExtendedVariantMode") });

        Assert.Equal(
            "StartPos 1 could not be rebuilt: this map no longer places the PlayerPlayback the " +
            "slot saved. Updating a map or a collab does this. Set the slot again.",
            message);
        Assert.True(message!.Length <= AkronActions.MaxStartPosFailureToastLength);
    }

    // The same refusal on an entity a helper ships. Attributing it would name that helper
    // and send the player to its settings, which is just as wrong in the other direction:
    // a mapper removing a custom spinner is not the spinner mod's doing and its settings
    // cannot bring the id back. One loaded type, one mod list that claims its assembly,
    // both kinds - so the only thing that can separate the two sentences is the kind, and
    // this fails if the map branch ever consults attribution.
    [Fact]
    public void AMapEditOnAModsOwnEntityNamesTheMapRatherThanTheMod() {
        string refusedTypeName = typeof(ModdedPlayerPlayback).AssemblyQualifiedName!;
        (string, string)[] loadedMods =
            new[] { ("SampleHelper", typeof(ModdedPlayerPlayback).Assembly.GetName().Name!) };

        Assert.Equal(
            "StartPos 2 could not be rebuilt: this map no longer places the ModdedPlayerPlayback " +
            "the slot saved. Updating a map or a collab does this. Set the slot again.",
            AkronStartPosRefusal.Describe(
                "StartPos 2",
                refusedTypeName,
                AkronReconstructionRefusalKind.ChangedMap,
                loadedMods));

        // The control: this really is a type the assembly split can attribute, so the
        // sentence above is the kind's doing and not a mod list that failed to match.
        Assert.Equal(
            "StartPos 2 needs ModdedPlayerPlayback from SampleHelper, and this room does not have " +
            "it. Check that mod's settings, or set the slot again.",
            AkronStartPosRefusal.Describe(
                "StartPos 2",
                refusedTypeName,
                AkronReconstructionRefusalKind.SavedObject,
                loadedMods));
    }

    // The other side of the split, through a real refusal, and the proof that the
    // bug-report branch is still reachable after the map refusal stopped taking it. This
    // room's map lays out every id in play; what it cannot do is rebuild the saved ghost
    // without dropping a live object the document keeps. Nothing the player owns explains
    // that, so it keeps the bug-report sentence.
    [Fact]
    public void ARefusalTheMapCannotExplainStillAsksForABugReport() {
        PlaybackGhostReloadRoom fresh =
            CreateTwoTrailReloadedGhostRoomTheSessionBuiltDifferently(unpairableFirst: true);

        AkronReconstructionRestore restore = RestoreTwoTrailGhostDocumentInto(
            fresh,
            unpairableFirst: true,
            mapIdsWhenSet: new[] { 42, 7, 8 },
            mapIdsAtReload: new[] { 42, 7, 8 });

        Assert.False(restore.Success);
        Assert.Contains(
            "reconstructed reference edge would drop a fresh object this document keeps",
            restore.Error);
        // A vanilla Celeste type, same as the map refusal above, and it gets the other
        // sentence. The kind is what separates them, not the assembly.
        Assert.Equal(typeof(PlayerHair).AssemblyQualifiedName, restore.RefusedTypeName);
        Assert.Equal(AkronReconstructionRefusalKind.SavedObject, restore.RefusedKind);

        string message = AkronStartPosRefusal.Describe(
            "StartPos 1",
            restore.RefusedTypeName,
            restore.RefusedKind,
            new[] { ("ExtendedVariantMode", "ExtendedVariantMode") });

        Assert.Equal(
            "StartPos 1 could not be rebuilt: this room has no PlayerHair to match, and no mod " +
            "owns it. If your mods have not changed, this is an Akron bug; report " +
            "akron-current.log.",
            message);
    }

    // Everest's own CoreModule is a real EverestModule named "Everest" and it lives in
    // Celeste.dll next to Monocle, so a plain module-list match blames "Everest" for
    // every vanilla type Akron fails to rebuild. It is the one entry in that list that
    // is not a mod anyone installed.
    [Fact]
    public void EverestsOwnModuleNeverTakesTheBlameForAVanillaType() {
        string message = AkronStartPosRefusal.Describe(
            "StartPos 3",
            typeof(Sprite).AssemblyQualifiedName!,
            AkronReconstructionRefusalKind.SavedObject,
            new[] { ("Everest", typeof(EverestModule).Assembly.GetName().Name!) });

        Assert.Equal(
            "StartPos 3 could not be rebuilt: this room has no Sprite to match, and no mod owns " +
            "it. If your mods have not changed, this is an Akron bug; report akron-current.log.",
            message);
    }

    // A generic container is declared by the runtime and still fails to load when one of
    // its arguments belongs to a mod that is gone. Blaming Akron for that would be wrong
    // and so would telling the player to reinstall System.Private.CoreLib, so this says
    // nothing and the caller falls back to the diagnostic text, which names the argument.
    [Fact]
    public void AGenericWhoseArgumentBelongsToAMissingModIsNotBlamedOnAkron() {
        Assert.Null(AkronStartPosRefusal.Describe(
            "StartPos 3",
            "System.Collections.Generic.List`1[[Sample.Thing, SampleMod, Version=1.0.0.0, " +
            "Culture=neutral, PublicKeyToken=null]], " + typeof(List<int>).Assembly.FullName,
            AkronReconstructionRefusalKind.SavedObject,
            Array.Empty<(string, string)>()));
    }

    // An uninstalled mod is refused while the snapshot is being read, before Restore
    // ever sees the document, so that refusal reaches the player down a different route
    // from every other one and has to carry the same type name with it.
    [Fact]
    public void ASnapshotNamingAnUnloadableTypeReportsThatTypeFromTheReader() {
        string directory = Path.Combine(Path.GetTempPath(), "akron-refusal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
            AkronReconstructionCapture capture = graph.Capture(
                new TestRoot { RoomEntity = CreateUninitializedEntity<Entity>() },
                new TestRoot { RoomEntity = CreateUninitializedEntity<Entity>() });
            Assert.True(capture.Success, capture.Error);
            Assert.True(AkronStartPosReconstruction.SaveSnapshot(
                "Akron StartPos refusal 1",
                "Celeste/1-ForsakenCity",
                "1",
                0,
                capture.Document,
                out string writeError,
                directory), writeError);

            // The file has to be edited after it is written, because writing it resolves
            // every type it names. That is the real shape of this failure: the snapshot
            // was written while the mod was installed and is read after it was removed.
            const string missingModType =
                "SampleVariants.Entities.SampleController, SampleVariantMode, Version=1.0.0.0, " +
                "Culture=neutral, PublicKeyToken=null";
            string snapshotPath = AkronStartPosReconstruction.GetSnapshotPath("Akron StartPos refusal 1", directory);
            string json;
            using (FileStream reading = File.OpenRead(snapshotPath))
            using (GZipStream decompressing = new GZipStream(reading, CompressionMode.Decompress))
            using (StreamReader reader = new StreamReader(decompressing)) {
                json = reader.ReadToEnd();
            }
            json = json.Replace(typeof(Entity).AssemblyQualifiedName!, missingModType);
            using (FileStream writing = File.Create(snapshotPath))
            using (GZipStream compressing = new GZipStream(writing, CompressionMode.Compress))
            using (StreamWriter writer = new StreamWriter(compressing)) {
                writer.Write(json);
            }

            bool loaded = AkronStartPosReconstruction.TryLoadSnapshot(
                "Akron StartPos refusal 1",
                out _,
                out string loadError,
                out string refusedTypeName,
                directory);

            Assert.False(loaded);
            Assert.Contains("type is unavailable", loadError);
            Assert.Equal(missingModType, refusedTypeName);
            Assert.Equal(
                "StartPos 1 needs SampleController from SampleVariantMode, which Akron cannot load " +
                "now. Turn that mod back on if you removed it, or set the slot again.",
                AkronStartPosRefusal.Describe(
                    "StartPos 1",
                    refusedTypeName,
                    AkronReconstructionRefusalKind.SavedObject,
                    Array.Empty<(string, string)>()));
        } finally {
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    // A mod that ships more than one assembly is only matched through the one its
    // EverestModule lives in, so a type from any other loaded assembly cannot be
    // attributed either way. Guessing would either send the player through their mod
    // list or file a bug report, and both would be wrong about half the time.
    [Fact]
    public void ALoadedAssemblyNoModuleClaimsProducesNoPlayerMessage() {
        Assert.Null(AkronStartPosRefusal.Describe(
            "StartPos 3",
            typeof(JObject).AssemblyQualifiedName!,
            AkronReconstructionRefusalKind.SavedObject,
            new[] { ("ExtendedVariantMode", "ExtendedVariantMode") }));
    }

    // Refusals land on compiler-generated members more often than on anything else, and
    // "ZoomLevel+<>c" names nothing a player has ever seen in a menu.
    [Theory]
    [InlineData("Monocle.Sprite+<PlayUtil>d__40, Celeste, Version=1.0.0.0", "Sprite")]
    [InlineData("ExtendedVariants.Variants.ZoomLevel+<>c, ExtendedVariantMode, Version=0.50.3.0", "ZoomLevel")]
    [InlineData("Celeste.Mod.SwapImmediatelyExtension+Flattened, Celeste, Version=1.0.0.0", "SwapImmediatelyExtension")]
    [InlineData("Celeste.Player, Celeste, Version=1.0.0.0", "Player")]
    public void ADisplayTypeNameIsTheOutermostTypeAPlayerCouldRecognise(string typeName, string expected) {
        Assert.Equal(expected, AkronStartPosRefusal.GetDisplayTypeName(typeName));
    }

    // Not every refusal names an object: an array whose length differs, or a field that
    // no longer exists, names none. There is nothing to explain, and the caller keeps
    // showing the head of the diagnostic text instead of inventing a reason.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Monocle.Sprite")]
    public void ARefusalThatNamesNoAssemblyProducesNoPlayerMessage(string refusedTypeName) {
        Assert.Null(AkronStartPosRefusal.Describe(
            "StartPos 3",
            refusedTypeName,
            AkronReconstructionRefusalKind.SavedObject,
            new[] { ("ExtendedVariantMode", "ExtendedVariantMode") }));
    }

    // ModA.Container<ModB.State> names ModA's assembly and fails to load because ModB is
    // the one that is gone. ModA is loaded and its settings are not the problem, so it
    // must not be the mod the message names.
    [Fact]
    public void AGenericOfALoadedModWhoseArgumentIsMissingBlamesNeitherMod() {
        Assert.Null(AkronStartPosRefusal.Describe(
            "StartPos 3",
            "SampleHelper.Containers.Holder`1[[Sample.Thing, SampleOtherMod, Version=1.0.0.0, " +
            "Culture=neutral, PublicKeyToken=null]], SampleHelper, Version=1.0.0.0, " +
            "Culture=neutral, PublicKeyToken=null",
            AkronReconstructionRefusalKind.SavedObject,
            new[] { ("SampleHelper", "SampleHelper") }));
    }

    // Type names come out of a snapshot file and the reader lets a string run into the
    // megabytes, so a corrupt or hostile one must not reach the screen.
    [Fact]
    public void AnAbsurdlyLongTypeNameProducesNoPlayerMessage() {
        string message = AkronStartPosRefusal.Describe(
            "StartPos 3",
            "Sample." + new string('x', 4096) + ", SampleVariantMode, Version=1.0.0.0, " +
            "Culture=neutral, PublicKeyToken=null",
            AkronReconstructionRefusalKind.SavedObject,
            Array.Empty<(string, string)>());

        Assert.Null(message);
    }

    [Fact]
    public void AGenericTypeNameResolvesToTheAssemblyThatOwnsItRatherThanItsArguments() {
        string typeName = typeof(List<Sprite>).AssemblyQualifiedName!;

        Assert.Equal(
            typeof(List<Sprite>).Assembly.GetName().Name,
            AkronStartPosRefusal.GetAssemblyName(typeName));
        Assert.Equal("List", AkronStartPosRefusal.GetDisplayTypeName(typeName));
    }

    [Fact]
    public void ASlotLoadedWithoutANumberStillReadsAsAnInstruction() {
        string message = AkronStartPosRefusal.Describe(
            "StartPos",
            typeof(SampleUnderwaterSwitchController).AssemblyQualifiedName!,
            AkronReconstructionRefusalKind.SavedObject,
            new[] { ("SampleVariantMode", typeof(SampleZoomLevel).Assembly.GetName().Name!) });

        Assert.Equal(
            "StartPos needs SampleUnderwaterSwitchController from SampleVariantMode, and this room " +
            "does not have it. Check that mod's settings, or set the slot again.",
            message);
        Assert.True(message!.Length <= AkronActions.MaxStartPosFailureToastLength);
    }

    [Fact]
    public void RestoreAllowsAFieldlessBuiltInMarkerEntityWithoutAFreshInstance() {
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new TestRoot { RoomEntity = CreateUninitializedEntity<WaterSurface>() },
            new TestRoot());
        Assert.True(capture.Success, capture.Error);
        TestRoot fresh = new TestRoot();

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.IsType<WaterSurface>(fresh.RoomEntity);
    }

    [Fact]
    public void RestoreAllowsAPassiveRuntimeDataRecordWithoutAFreshInstance() {
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new PassiveDataRoot {
                Value = new Water.Ripple {
                    Position = 37f,
                    Speed = -91f,
                    Height = 4f,
                    Percent = 0.625f,
                    Duration = 3f
                }
            },
            new PassiveDataRoot());
        Assert.True(capture.Success, capture.Error);
        PassiveDataRoot fresh = new PassiveDataRoot();

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Water.Ripple ripple = Assert.IsType<Water.Ripple>(fresh.Value);
        Assert.Equal(37f, ripple.Position);
        Assert.Equal(-91f, ripple.Speed);
        Assert.Equal(0.625f, ripple.Percent);
    }

    [Fact]
    public void RestoreAllowsABuiltInChooserWithoutAFreshInstance() {
        Chooser<string> savedChooser = (Chooser<string>) RuntimeHelpers.GetUninitializedObject(typeof(Chooser<string>));
        List<Chooser<string>.Choice> savedChoices = new List<Chooser<string>.Choice> {
            CreateChoice("idle", 2f),
            CreateChoice("run", 3f)
        };
        SetRuntimeField(savedChooser, "choices", savedChoices);
        SetRuntimeField(savedChooser, "<TotalWeight>k__BackingField", 5f);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new PassiveDataRoot { Value = savedChooser },
            new PassiveDataRoot());
        Assert.True(capture.Success, capture.Error);
        PassiveDataRoot fresh = new PassiveDataRoot();

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Chooser<string> chooser = Assert.IsType<Chooser<string>>(fresh.Value);
        List<Chooser<string>.Choice> choices = GetRuntimeField<List<Chooser<string>.Choice>>(chooser, "choices");
        Assert.Equal(5f, (float) GetRuntimeFieldInfo(typeof(Chooser<string>), "<TotalWeight>k__BackingField").GetValue(chooser)!);
        Assert.Collection(
            choices,
            choice => { Assert.Equal("idle", choice.Value); Assert.Equal(2f, choice.Weight); },
            choice => { Assert.Equal("run", choice.Value); Assert.Equal(3f, choice.Weight); });
    }

    [Fact]
    public void RestoreAllowsABuiltInAudioTrackStateWithoutAFreshInstance() {
        AudioTrackState savedTrack = (AudioTrackState) RuntimeHelpers.GetUninitializedObject(typeof(AudioTrackState));
        SetRuntimeField(savedTrack, "ev", "event:/music/lvl1/main");
        savedTrack.Parameters = new List<MEP> {
            new MEP { Key = "progress", Value = 3f },
            new MEP { Key = "layer", Value = 0.75f }
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new PassiveDataRoot { Value = savedTrack },
            new PassiveDataRoot());
        Assert.True(capture.Success, capture.Error);
        PassiveDataRoot fresh = new PassiveDataRoot();

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        AudioTrackState track = Assert.IsType<AudioTrackState>(fresh.Value);
        Assert.Equal("event:/music/lvl1/main", GetRuntimeField<string>(track, "ev"));
        Assert.Collection(
            track.Parameters,
            parameter => { Assert.Equal("progress", parameter.Key); Assert.Equal(3f, parameter.Value); },
            parameter => { Assert.Equal("layer", parameter.Key); Assert.Equal(0.75f, parameter.Value); });
    }

    [Fact]
    public void RestoreAllowsBuiltInAreaStatsReferencedBySavedRuntimeState() {
        AreaModeStats savedStats = new AreaModeStats {
            TimePlayed = 37,
            Deaths = 11,
            BestDeaths = 3
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new PassiveDataRoot { Value = savedStats },
            new PassiveDataRoot());
        Assert.True(capture.Success, capture.Error);
        PassiveDataRoot fresh = new PassiveDataRoot();

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        AreaModeStats restoredStats = Assert.IsType<AreaModeStats>(fresh.Value);
        Assert.Equal(37, restoredStats.TimePlayed);
        Assert.Equal(11, restoredStats.Deaths);
        Assert.Equal(3, restoredStats.BestDeaths);
    }

    [Fact]
    public void RestoreAllowsASavedBuiltInParticleTypeWithoutAFreshInstance() {
        ParticleType savedType = (ParticleType) RuntimeHelpers.GetUninitializedObject(typeof(ParticleType));
        savedType.SpeedMin = 37f;
        savedType.SpeedMax = 91f;
        savedType.LifeMin = 0.25f;
        savedType.LifeMax = 0.75f;
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new PassiveDataRoot { Value = savedType },
            new PassiveDataRoot());
        Assert.True(capture.Success, capture.Error);
        PassiveDataRoot fresh = new PassiveDataRoot();

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        ParticleType restoredType = Assert.IsType<ParticleType>(fresh.Value);
        Assert.Equal(37f, restoredType.SpeedMin);
        Assert.Equal(91f, restoredType.SpeedMax);
        Assert.Equal(0.25f, restoredType.LifeMin);
        Assert.Equal(0.75f, restoredType.LifeMax);
    }

    [Fact]
    public void RestoreAllowsASavedBuiltInTalkPromptWithoutAFreshInstance() {
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new PassiveDataRoot { Value = new Water.Ripple() },
            new PassiveDataRoot());
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode promptNode = capture.Document.Nodes.Single(node =>
            node.ParentFieldName == nameof(PassiveDataRoot.Value));
        promptNode.TypeName = typeof(TalkComponent.TalkComponentUI).AssemblyQualifiedName!;
        promptNode.Fields.Clear();
        PassiveDataRoot fresh = new PassiveDataRoot();

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.IsType<TalkComponent.TalkComponentUI>(fresh.Value);
    }

    [Fact]
    public void SavedBuiltInTalkPromptKeepsItsReconstructedWigglerAlias() {
        Assert.True(AkronReconstructionGraph.IsBuiltInSavedComponentAliasField(
            typeof(TalkComponent.TalkComponentUI), "wiggler", typeof(Wiggler)));
        Assert.False(AkronReconstructionGraph.IsBuiltInSavedComponentAliasField(
            typeof(TalkComponent.TalkComponentUI), "wiggler", typeof(Component)));
        Assert.False(AkronReconstructionGraph.IsBuiltInSavedComponentAliasField(
            typeof(Entity), "wiggler", typeof(Wiggler)));
    }

    [Fact]
    public void SavedBuiltInEntityCanPointBackToItsFreshScene() {
        Scene savedScene = (Scene) RuntimeHelpers.GetUninitializedObject(typeof(Scene));
        EntityList savedEntities = CreateDetachedEntityList();
        TalkComponent.TalkComponentUI savedPrompt =
            (TalkComponent.TalkComponentUI) RuntimeHelpers.GetUninitializedObject(
                typeof(TalkComponent.TalkComponentUI));
        AddDetachedEntity(savedEntities, savedPrompt);
        LinkEntityListToScene(savedEntities, savedScene);
        typeof(Entity).GetField("<Scene>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(savedPrompt, savedScene);
        Scene baselineScene = (Scene) RuntimeHelpers.GetUninitializedObject(typeof(Scene));
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new SavedSceneRoot { Scene = savedScene, Entities = savedEntities },
            new SavedSceneRoot {
                Scene = baselineScene,
                Entities = LinkEntityListToScene(CreateDetachedEntityList(), baselineScene)
            });
        Assert.True(capture.Success, capture.Error);
        Scene freshScene = (Scene) RuntimeHelpers.GetUninitializedObject(typeof(Scene));
        SavedSceneRoot fresh = new SavedSceneRoot {
            Scene = freshScene,
            Entities = LinkEntityListToScene(CreateDetachedEntityList(), freshScene)
        };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        TalkComponent.TalkComponentUI restoredPrompt = Assert.IsType<TalkComponent.TalkComponentUI>(
            Assert.Single(GetEntityListContents(fresh.Entities)));
        Assert.Same(freshScene, GetRuntimeField<Scene>(restoredPrompt, "<Scene>k__BackingField"));
    }

    [Fact]
    public void SavedOnlyBuiltInRuntimeEntityRestoresThroughItsFreshSceneOwnership() {
        Scene savedScene = (Scene) RuntimeHelpers.GetUninitializedObject(typeof(Scene));
        EntityList savedEntities = LinkSceneEntities(savedScene, CreateDetachedEntityList());
        SlashFx savedSlash = CreateUninitializedEntity<SlashFx>();
        InitializeEmptyComponentList(savedSlash);
        typeof(Entity).GetField("<Scene>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(savedSlash, savedScene);
        AddDetachedEntity(savedEntities, savedSlash);

        Scene baselineScene = (Scene) RuntimeHelpers.GetUninitializedObject(typeof(Scene));
        EntityList baselineEntities = LinkSceneEntities(baselineScene, CreateDetachedEntityList());
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new SavedSceneRoot { Scene = savedScene, Entities = savedEntities },
            new SavedSceneRoot { Scene = baselineScene, Entities = baselineEntities });
        Assert.True(capture.Success, capture.Error);

        Scene freshScene = (Scene) RuntimeHelpers.GetUninitializedObject(typeof(Scene));
        EntityList freshEntities = LinkSceneEntities(freshScene, CreateDetachedEntityList());
        SavedSceneRoot fresh = new SavedSceneRoot { Scene = freshScene, Entities = freshEntities };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        SlashFx restoredSlash = Assert.IsType<SlashFx>(Assert.Single(GetEntityListContents(fresh.Entities)));
        Assert.Same(freshScene, GetRuntimeField<Scene>(restoredSlash, "<Scene>k__BackingField"));
        Assert.NotNull(GetRuntimeField<ComponentList>(restoredSlash, "<Components>k__BackingField"));
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void CompilerIteratorOwnerAcceptsAnAuthenticatedRuntimeEntity() {
        Type routineType = typeof(SaveLoadIcon)
            .GetNestedTypes(BindingFlags.NonPublic)
            .Single(type =>
                typeof(IEnumerator).IsAssignableFrom(type) &&
                type.GetFields(RuntimeInstanceFields).Any(field => field.FieldType == typeof(SaveLoadIcon)));
        SaveLoadIcon owner = CreateUninitializedEntity<SaveLoadIcon>();

        Assert.True(AkronReconstructionGraph.IsAuthenticatedCompilerIteratorOwner(
            routineType,
            ownerIsFresh: false,
            ownerIsAuthenticatedReconstruction: true,
            owner));
        Assert.False(AkronReconstructionGraph.IsAuthenticatedCompilerIteratorOwner(
            routineType,
            ownerIsFresh: false,
            ownerIsAuthenticatedReconstruction: false,
            owner));
        Assert.False(AkronReconstructionGraph.IsAuthenticatedCompilerIteratorOwner(
            routineType,
            ownerIsFresh: false,
            ownerIsAuthenticatedReconstruction: true,
            CreateUninitializedEntity<SlashFx>()));
    }

    [Fact]
    public void CompilerIteratorOwnerAcceptsASubclassOfTheDeclaringType() {
        Type playUtilType = typeof(Sprite)
            .GetNestedTypes(BindingFlags.NonPublic)
            .Single(type => type.Name.StartsWith("<PlayUtil>", StringComparison.Ordinal));

        Assert.True(AkronReconstructionGraph.IsAuthenticatedCompilerIteratorOwner(
            playUtilType,
            ownerIsFresh: true,
            ownerIsAuthenticatedReconstruction: false,
            RuntimeHelpers.GetUninitializedObject(typeof(PlayerSprite))));
        Assert.True(AkronReconstructionGraph.IsAuthenticatedCompilerIteratorOwner(
            playUtilType,
            ownerIsFresh: true,
            ownerIsAuthenticatedReconstruction: false,
            RuntimeHelpers.GetUninitializedObject(typeof(Sprite))));
        Assert.False(AkronReconstructionGraph.IsAuthenticatedCompilerIteratorOwner(
            playUtilType,
            ownerIsFresh: true,
            ownerIsAuthenticatedReconstruction: false,
            RuntimeHelpers.GetUninitializedObject(typeof(Image))));
        Assert.False(AkronReconstructionGraph.IsAuthenticatedCompilerIteratorOwner(
            playUtilType,
            ownerIsFresh: true,
            ownerIsAuthenticatedReconstruction: false,
            CreateUninitializedEntity<SlashFx>()));
    }

    [Theory]
    [InlineData("Renderers", true)]
    [InlineData("adding", true)]
    [InlineData("removing", true)]
    [InlineData("unrelated", false)]
    public void ScreenWipeAliasesUseOnlyRendererListStorage(string fieldName, bool expected) {
        Assert.Equal(expected, AkronReconstructionGraph.IsRendererListStorageField(fieldName));
    }

    [Fact]
    public void FreshEntityCanRestoreItsSavedOnlyNestedCollectionRecord() {
        SeekerBarrierRenderer savedRenderer = CreateSeekerBarrierRenderer(edgeCount: 1);
        SeekerBarrierRenderer baselineRenderer = CreateSeekerBarrierRenderer(edgeCount: 0);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new PassiveDataRoot { Value = savedRenderer },
            new PassiveDataRoot { Value = baselineRenderer });
        Assert.True(capture.Success, capture.Error);
        SeekerBarrierRenderer freshRenderer = CreateSeekerBarrierRenderer(edgeCount: 0);
        PassiveDataRoot fresh = new PassiveDataRoot { Value = freshRenderer };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Single((System.Collections.IList) GetRuntimeFieldInfo(
            typeof(SeekerBarrierRenderer), "edges").GetValue(freshRenderer)!);
    }

    [Fact]
    public void SavedOnlyBuiltInRuntimeEntityRestoresThroughFreshEntityListStorage() {
        Scene savedScene = (Scene) RuntimeHelpers.GetUninitializedObject(typeof(Scene));
        EntityList savedEntities = LinkSceneEntities(savedScene, CreateDetachedEntityList());
        SetEntityListCapacity(savedEntities, 128);
        SlashFx savedSlash = CreateUninitializedEntity<SlashFx>();
        InitializeEmptyComponentList(savedSlash);
        typeof(Entity).GetField("<Scene>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(savedSlash, savedScene);
        AddDetachedEntity(savedEntities, savedSlash);

        Scene baselineScene = (Scene) RuntimeHelpers.GetUninitializedObject(typeof(Scene));
        EntityList baselineEntities = LinkSceneEntities(baselineScene, CreateDetachedEntityList());
        SetEntityListCapacity(baselineEntities, 128);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new SavedSceneRoot { Scene = savedScene, Entities = savedEntities },
            new SavedSceneRoot { Scene = baselineScene, Entities = baselineEntities });
        Assert.True(capture.Success, capture.Error);

        Scene freshScene = (Scene) RuntimeHelpers.GetUninitializedObject(typeof(Scene));
        EntityList freshEntities = LinkSceneEntities(freshScene, CreateDetachedEntityList());
        SetEntityListCapacity(freshEntities, 128);
        SavedSceneRoot fresh = new SavedSceneRoot { Scene = freshScene, Entities = freshEntities };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        SlashFx restoredSlash = Assert.IsType<SlashFx>(Assert.Single(GetEntityListContents(fresh.Entities)));
        Assert.Same(freshScene, GetRuntimeField<Scene>(restoredSlash, "<Scene>k__BackingField"));
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void RuntimeEntityCanonicalArrayMustBelongToItsSceneEntityList() {
        Scene savedScene = (Scene) RuntimeHelpers.GetUninitializedObject(typeof(Scene));
        EntityList savedEntities = LinkSceneEntities(savedScene, CreateDetachedEntityList());
        SetEntityListCapacity(savedEntities, 128);
        SlashFx savedSlash = CreateUninitializedEntity<SlashFx>();
        InitializeEmptyComponentList(savedSlash);
        typeof(Entity).GetField("<Scene>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(savedSlash, savedScene);
        AddDetachedEntity(savedEntities, savedSlash);

        Scene baselineScene = (Scene) RuntimeHelpers.GetUninitializedObject(typeof(Scene));
        EntityList baselineEntities = LinkSceneEntities(baselineScene, CreateDetachedEntityList());
        SetEntityListCapacity(baselineEntities, 128);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new UnrelatedEntityArrayRoot {
                Unrelated = Array.Empty<Entity>(),
                Scene = savedScene,
                Entities = savedEntities
            },
            new UnrelatedEntityArrayRoot {
                Unrelated = Array.Empty<Entity>(),
                Scene = baselineScene,
                Entities = baselineEntities
            });
        Assert.True(capture.Success, capture.Error);

        AkronReconstructionNode rootNode = Assert.Single(
            capture.Document.Nodes,
            node => node.Id == capture.Document.RootNodeId);
        int unrelatedArrayNodeId = Assert.Single(
            rootNode.Fields,
            field => field.Name == nameof(UnrelatedEntityArrayRoot.Unrelated)).Value.NodeId;
        AkronReconstructionNode unrelatedArrayNode = Assert.Single(
            capture.Document.Nodes,
            node => node.Id == unrelatedArrayNodeId);
        AkronReconstructionNode savedSlashNode = Assert.Single(
            capture.Document.Nodes,
            node => node.TypeName == typeof(SlashFx).AssemblyQualifiedName);

        // Simulate an imported snapshot that makes an unrelated array the
        // entity's canonical parent while retaining the real EntityList alias.
        unrelatedArrayNode.Items.Add(new AkronReconstructionValue {
            Kind = AkronReconstructionGraph.ReferenceValueKind,
            TypeName = typeof(SlashFx).AssemblyQualifiedName!,
            NodeId = savedSlashNode.Id
        });
        unrelatedArrayNode.ArrayLengths = new List<int> { 1 };
        unrelatedArrayNode.ArrayLowerBounds = new List<int> { 0 };
        savedSlashNode.ParentNodeId = unrelatedArrayNode.Id;
        savedSlashNode.ParentKind = "array";
        savedSlashNode.ParentArrayIndices = new List<int> { 0 };

        Scene freshScene = (Scene) RuntimeHelpers.GetUninitializedObject(typeof(Scene));
        EntityList freshEntities = LinkSceneEntities(freshScene, CreateDetachedEntityList());
        SetEntityListCapacity(freshEntities, 128);
        UnrelatedEntityArrayRoot fresh = new UnrelatedEntityArrayRoot {
            Unrelated = Array.Empty<Entity>(),
            Scene = freshScene,
            Entities = freshEntities
        };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.False(
            restore.Success,
            $"unrelated={fresh.Unrelated.Length};entities={GetEntityListContents(fresh.Entities).Count};error={restore.Error}");
        Assert.Contains("entity canonical array is not owned by its scene EntityList", restore.Error);
        Assert.Empty(GetEntityListContents(fresh.Entities));
    }

    [Fact]
    public void SavedOnlyBuiltInRuntimeEntityRestoresThroughItsSceneTrackerIndex() {
        (SavedSceneRoot saved, _) = CreateTrackedRuntimeEntityScene(includeSlash: true);
        (SavedSceneRoot baseline, _) = CreateTrackedRuntimeEntityScene(includeSlash: false);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(
            AkronStartPosReconstruction.IsLiveResourceType,
            resource => ((Type) resource).AssemblyQualifiedName,
            null,
            AkronStartPosReconstruction.ResolveDetachedLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        (SavedSceneRoot fresh, _) = CreateTrackedRuntimeEntityScene(includeSlash: false);

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        SlashFx restoredSlash = Assert.IsType<SlashFx>(Assert.Single(GetEntityListContents(fresh.Entities)));
        Tracker tracker = GetRuntimeField<Tracker>(fresh.Scene, "<Tracker>k__BackingField");
        Dictionary<Type, List<Entity>> trackedEntities =
            GetRuntimeField<Dictionary<Type, List<Entity>>>(tracker, "<Entities>k__BackingField");
        List<Entity> tracked = trackedEntities[typeof(SlashFx)];
        Assert.Same(restoredSlash, Assert.Single(tracked));
        Sprite restoredComponent = Assert.IsType<Sprite>(Assert.Single(
            GetRuntimeField<List<Component>>(
                GetRuntimeField<ComponentList>(restoredSlash, "<Components>k__BackingField"),
                "components")));
        Dictionary<Type, List<Component>> trackedComponents =
            GetRuntimeField<Dictionary<Type, List<Component>>>(tracker, "<Components>k__BackingField");
        Assert.Same(
            restoredComponent,
            Assert.Single(trackedComponents[typeof(Sprite)]));
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void FreshModEntityRestoresThroughItsSceneTrackerIndex() {
        (SavedSceneRoot saved, SourceIdentifiedEntity savedEntity) =
            CreateTrackedSourceEntityScene(includeTrackerEntry: true);
        (SavedSceneRoot baseline, _) = CreateTrackedSourceEntityScene(includeTrackerEntry: false);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(
            AkronStartPosReconstruction.IsLiveResourceType,
            resource => ((Type) resource).AssemblyQualifiedName!,
            null,
            AkronStartPosReconstruction.ResolveDetachedLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        (SavedSceneRoot fresh, SourceIdentifiedEntity freshEntity) =
            CreateTrackedSourceEntityScene(includeTrackerEntry: false);

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.NotSame(savedEntity, freshEntity);
        Tracker tracker = GetRuntimeField<Tracker>(fresh.Scene, "<Tracker>k__BackingField");
        Dictionary<Type, List<Entity>> trackedEntities =
            GetRuntimeField<Dictionary<Type, List<Entity>>>(tracker, "<Entities>k__BackingField");
        Assert.Same(freshEntity, Assert.Single(trackedEntities[typeof(SourceIdentifiedEntity)]));
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void FreshModEntityRestoresThroughItsSceneTagIndex() {
        (SavedSceneRoot saved, SourceIdentifiedEntity savedEntity) =
            CreateTaggedSourceEntityScene(includeTagEntry: true);
        (SavedSceneRoot baseline, _) = CreateTaggedSourceEntityScene(includeTagEntry: false);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource, _ => string.Empty);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        (SavedSceneRoot fresh, SourceIdentifiedEntity freshEntity) =
            CreateTaggedSourceEntityScene(includeTagEntry: false);

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.NotSame(savedEntity, freshEntity);
        TagLists tagLists = GetRuntimeField<TagLists>(fresh.Scene, "<TagLists>k__BackingField");
        List<Entity>[] lists = GetRuntimeField<List<Entity>[]>(tagLists, "lists");
        Assert.Same(freshEntity, Assert.Single(lists[0]));
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void SavedOnlyBuiltInRuntimeEntityRestoresThroughItsSceneTagIndex() {
        (SavedSceneRoot saved, _) = CreateTaggedRuntimeEntityScene(includeSlash: true);
        (SavedSceneRoot baseline, _) = CreateTaggedRuntimeEntityScene(includeSlash: false);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        (SavedSceneRoot fresh, _) = CreateTaggedRuntimeEntityScene(includeSlash: false);

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        SlashFx restoredSlash = Assert.IsType<SlashFx>(Assert.Single(GetEntityListContents(fresh.Entities)));
        TagLists tagLists = GetRuntimeField<TagLists>(fresh.Scene, "<TagLists>k__BackingField");
        List<Entity>[] lists = GetRuntimeField<List<Entity>[]>(tagLists, "lists");
        Assert.Same(restoredSlash, Assert.Single(lists[0]));
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void FreshSpriteCallbackCanAuthenticateItsReconstructedRuntimeEntityOwner() {
        (SavedSceneRoot saved, SlashFx? savedSlash) = CreateTrackedRuntimeEntityScene(includeSlash: true);
        saved.FreshSprite = Assert.IsType<Sprite>(Assert.Single(
            GetRuntimeField<List<Component>>(
                GetRuntimeField<ComponentList>(savedSlash!, "<Components>k__BackingField"),
                "components")));
        (SavedSceneRoot baseline, _) = CreateTrackedRuntimeEntityScene(includeSlash: false);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(
            AkronStartPosReconstruction.IsLiveResourceType,
            resource => ((Type) resource).AssemblyQualifiedName,
            null,
            AkronStartPosReconstruction.ResolveDetachedLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode spriteNode = capture.Document.Nodes.Single(node =>
            node.TypeName == typeof(Sprite).AssemblyQualifiedName);
        spriteNode.UseFreshObject = true;
        spriteNode.FreshPath = new List<AkronReconstructionPathStep> {
            new AkronReconstructionPathStep {
                Kind = "field",
                DeclaringTypeName = typeof(SavedSceneRoot).AssemblyQualifiedName!,
                FieldName = nameof(SavedSceneRoot.FreshSprite)
            }
        };
        (SavedSceneRoot fresh, _) = CreateTrackedRuntimeEntityScene(includeSlash: false);
        fresh.FreshSprite = (Sprite) RuntimeHelpers.GetUninitializedObject(typeof(Sprite));

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        SlashFx restoredSlash = Assert.IsType<SlashFx>(Assert.Single(GetEntityListContents(fresh.Entities)));
        Sprite restoredSprite = Assert.IsType<Sprite>(Assert.Single(
            GetRuntimeField<List<Component>>(
                GetRuntimeField<ComponentList>(restoredSlash, "<Components>k__BackingField"),
                "components")));
        Assert.Same(fresh.FreshSprite, restoredSprite);
        Assert.Same(restoredSlash, GetRuntimeField<Entity>(restoredSprite, "<Entity>k__BackingField"));
        Assert.NotNull(restoredSprite.OnFinish);
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void SavedOnlyBuiltInRendererRecordRestoresThroughItsFreshOwnerCollection() {
        DisplacementRenderer savedRenderer = CreateDisplacementRenderer();
        DisplacementRenderer.Burst savedBurst =
            (DisplacementRenderer.Burst) RuntimeHelpers.GetUninitializedObject(
                typeof(DisplacementRenderer.Burst));
        savedBurst.Percent = 0.375f;
        GetDisplacementBursts(savedRenderer).Add(savedBurst);
        DisplacementRenderer baselineRenderer = CreateDisplacementRenderer();
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new RendererRuntimeRecordRoot { Renderer = savedRenderer },
            new RendererRuntimeRecordRoot { Renderer = baselineRenderer });
        Assert.True(capture.Success, capture.Error);
        RendererRuntimeRecordRoot fresh = new RendererRuntimeRecordRoot {
            Renderer = CreateDisplacementRenderer()
        };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        DisplacementRenderer.Burst restoredBurst = Assert.Single(GetDisplacementBursts(fresh.Renderer));
        Assert.Equal(0.375f, restoredBurst.Percent);
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void SavedOnlyBuiltInRendererRecordRestoresThroughItsFreshBackingArray() {
        DisplacementRenderer savedRenderer = CreateDisplacementRenderer(collectionCapacity: 4);
        Ease.Easer savedInnerEaser = new Ease.Easer(QuadraticEase);
        DisplacementRenderer.Burst savedBurst =
            (DisplacementRenderer.Burst) RuntimeHelpers.GetUninitializedObject(
                typeof(DisplacementRenderer.Burst));
        savedBurst.Percent = 0.625f;
        savedBurst.AlphaEaser = CreateBuiltInInvertedEaser(savedInnerEaser);
        GetDisplacementBursts(savedRenderer).Add(savedBurst);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new RendererRuntimeRecordRoot { Renderer = savedRenderer, AuthenticatedEaser = savedInnerEaser },
            new RendererRuntimeRecordRoot {
                Renderer = CreateDisplacementRenderer(collectionCapacity: 4),
                AuthenticatedEaser = new Ease.Easer(QuadraticEase)
            });
        Assert.True(capture.Success, capture.Error);
        RendererRuntimeRecordRoot fresh = new RendererRuntimeRecordRoot {
            Renderer = CreateDisplacementRenderer(collectionCapacity: 4),
            AuthenticatedEaser = new Ease.Easer(QuadraticEase)
        };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        DisplacementRenderer.Burst restoredBurst = Assert.Single(GetDisplacementBursts(fresh.Renderer));
        Assert.Equal(0.625f, restoredBurst.Percent);
        Assert.Equal("<Invert>b__0", restoredBurst.AlphaEaser.Method.Name);
        Assert.Equal("<>c__DisplayClass35_0", restoredBurst.AlphaEaser.Target?.GetType().Name);
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void TrailSnapshotOnlyAcceptsItsTwoPlayerComponentAliases() {
        Assert.True(AkronReconstructionGraph.IsTrailSnapshotComponentReference(
            typeof(TrailManager.Snapshot), nameof(TrailManager.Snapshot.Hair), typeof(PlayerHair)));
        Assert.True(AkronReconstructionGraph.IsTrailSnapshotComponentReference(
            typeof(TrailManager.Snapshot), nameof(TrailManager.Snapshot.Sprite), typeof(PlayerSprite)));
        Assert.True(AkronReconstructionGraph.IsTrailSnapshotComponentOwnerType(typeof(Player)));
        Assert.True(AkronReconstructionGraph.IsTrailSnapshotComponentOwnerType(typeof(PlayerPlayback)));
        Assert.False(AkronReconstructionGraph.IsTrailSnapshotComponentReference(
            typeof(TrailManager.Snapshot), nameof(TrailManager.Snapshot.Manager), typeof(TrailManager)));
        Assert.False(AkronReconstructionGraph.IsTrailSnapshotComponentReference(
            typeof(TalkComponent.TalkComponentUI), nameof(TrailManager.Snapshot.Hair), typeof(PlayerHair)));
        Assert.False(AkronReconstructionGraph.IsTrailSnapshotComponentOwnerType(typeof(Entity)));
    }

    [Fact]
    public void SnapshotExclusionCoversThePlaybackGhostAndTheTrailsThatRenderIt() {
        PlaybackGhostRoom room = CreatePlaybackGhostRoom();

        Assert.True(AkronSnapshotExclusion.IsExcludedFromSnapshot(room.Ghost));
        Assert.True(AkronSnapshotExclusion.IsExcludedFromSnapshot(room.GhostTrail));
        // The player's dash trail has the same shape and must survive: dropping it
        // would silently stop restoring a real piece of the player's own state.
        Assert.False(AkronSnapshotExclusion.IsExcludedFromSnapshot(room.PlayerTrail));
        Assert.False(AkronSnapshotExclusion.IsExcludedFromSnapshot(room.Player));
        Assert.False(AkronSnapshotExclusion.IsExcludedFromSnapshot(room.Manager));
    }

    [Fact]
    public void SnapshotExclusionLeavesModSubclassesOfThePlaybackGhostAlone() {
        // The evidence that nothing reads a playback ghost is evidence about
        // Celeste's type. A mod that subclasses it can add whatever state it likes,
        // and dropping that state while reporting a successful load is exactly the
        // outcome this exclusion must not produce.
        ModdedPlayerPlayback subclassGhost = CreateUninitializedEntity<ModdedPlayerPlayback>();

        Assert.False(AkronSnapshotExclusion.IsExcludedFromSnapshot(subclassGhost));
        Assert.True(AkronSnapshotExclusion.IsExcludedFromSnapshot(
            CreateUninitializedEntity<PlayerPlayback>()));
    }

    [Fact]
    public void OnlyAGhostHiddenPartWayThroughItsTimelineIsTreatedAsAkronSuppressed() {
        // Vanilla hides a ghost when its timeline runs out and shows it again when
        // the loop restarts, so mid-timeline plus invisible is a state only the
        // Disable Playback hook produces.
        Assert.True(AkronModule.WasPlaybackHiddenByAkron(
            visible: false, frameIndex: 1, frameCount: 3, time: 2f, trimEnd: 5f));

        Assert.False(AkronModule.WasPlaybackHiddenByAkron(
            visible: true, frameIndex: 1, frameCount: 3, time: 2f, trimEnd: 5f));

        // The just-constructed state is invisible with the index past the end, and
        // the end-of-loop state is invisible at TrimEnd. Neither is Akron's doing.
        Assert.False(AkronModule.WasPlaybackHiddenByAkron(
            visible: false, frameIndex: 3, frameCount: 3, time: 2f, trimEnd: 5f));

        Assert.False(AkronModule.WasPlaybackHiddenByAkron(
            visible: false, frameIndex: 1, frameCount: 3, time: 5f, trimEnd: 5f));
    }

    [Fact]
    public void SnapshotExclusionNamesOneEntityTypeAndNoOther() {
        // The exclusion is one named type, not a growing category. Every entity
        // type Akron refuses to save costs the player exactness, so this list
        // changing has to be a deliberate edit with its own evidence.
        List<Type> excluded = typeof(Entity).Assembly
            .GetTypes()
            .Where(type => typeof(Entity).IsAssignableFrom(type) && !type.IsAbstract &&
                           !type.ContainsGenericParameters &&
                           AkronSnapshotExclusion.IsExcludedFromSnapshot(
                               (Entity) RuntimeHelpers.GetUninitializedObject(type)))
            .ToList();

        Assert.Equal(new[] { typeof(PlayerPlayback) }, excluded);
    }

    [Fact]
    public void AnExcludedTrailSnapshotHandsItsManagerSlotBack() {
        PlaybackGhostRoom room = CreatePlaybackGhostRoom();
        TrailManager.Snapshot[] slots = GetRuntimeField<TrailManager.Snapshot[]>(room.Manager, "snapshots");

        AkronSnapshotExclusion.ReleaseTrailManagerSlot(room.GhostTrail);
        AkronSnapshotExclusion.ReleaseTrailManagerSlot(room.Ghost);

        Assert.Null(slots[room.GhostTrail.Index]);
        // Releasing a slot the caller did not remove would drop a live dash trail.
        Assert.Same(room.PlayerTrail, slots[room.PlayerTrail.Index]);
    }

    [Fact]
    public void DetachedGhostsAreNotPutBackIntoARoomThatAlreadyRebuiltItsOwn() {
        PlaybackGhostRoom room = CreatePlaybackGhostRoom();
        Level level = (Level) RuntimeHelpers.GetUninitializedObject(typeof(Level));
        SetRuntimeField(level, "<Entities>k__BackingField", room.Entities);

        // Every failing StartPos restore reloads the room, and a room load builds
        // its own ghosts. Putting the detached ones back on top would leave one more
        // ghost after every failed Load.
        AkronSnapshotExclusion.ReattachToLevel(level, new List<Entity> { room.Ghost });

        Assert.Single(GetEntityListContents(room.Entities), entity => entity is PlayerPlayback);
    }

    [Fact]
    public void TrailSnapshotCanRetainAPlaybackComponentAfterItsOwnerLeavesTheScene() {
        TrailPlaybackRoot saved = CreateDetachedPlaybackTrailScene(0.625f);
        TrailPlaybackRoot baseline = CreateDetachedPlaybackTrailScene(0f);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        TrailPlaybackRoot fresh = CreateDetachedPlaybackTrailScene(0f);
        PlayerHair freshHair = fresh.Snapshot.Hair;
        Image freshSprite = fresh.Snapshot.Sprite;

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Equal(0.625f, fresh.Snapshot.Percent);
        Assert.Same(freshHair, fresh.Snapshot.Hair);
        Assert.Same(freshSprite, fresh.Snapshot.Sprite);
        Entity freshOwner = GetRuntimeField<Entity>(freshHair, "<Entity>k__BackingField");
        Assert.IsType<PlayerPlayback>(freshOwner);
        Assert.Null(GetRuntimeField<Scene?>(freshOwner, "<Scene>k__BackingField"));
        Assert.DoesNotContain(
            GetEntityListContents(fresh.Entities),
            entity => ReferenceEquals(entity, freshOwner));
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void AFreshRoomThatKeptTheDestroyedGhostsTrailRefusesTheRestore() {
        // The room Akron's fresh-room reload used to leave behind. UnloadLevel keeps
        // every Tags.Global entity, and a TrailManager.Snapshot is Tags.Global while
        // holding the PlayerSprite and PlayerHair of the entity it was made from, so
        // the snapshot outlives the ghost the reload destroyed and keeps it reachable
        // with a null Scene. LoadLevel rebuilds the same map entity with the same
        // EntityID, so the room holds two indistinguishable ghosts and the saved node
        // pairs with the dead one.
        PlaybackGhostReloadRoom fresh = CreateReloadedGhostRoom(trailBelongsToDestroyedGhost: true);

        AkronReconstructionRestore restore = RestoreTrailingGhostDocumentInto(fresh);

        Assert.False(restore.Success);
        Assert.Contains(
            "reconstructed reference edge is not authentic to the fresh room",
            restore.Error);
        Assert.Contains("edge-parent-type=Celeste.PlayerPlayback", restore.Error);
        Assert.Contains("edge-field=<Scene>k__BackingField", restore.Error);
        // The saved node did pair. It paired with the destroyed ghost, whose Scene is
        // null, which is why no fresh-room edge can authenticate it.
        Assert.Contains("edge-parent-fresh=true", restore.Error);
        Assert.Contains("fresh-field-alias=false", restore.Error);
    }

    [Fact]
    public void AFreshRoomWhoseTrailsBelongToItRestoresTheGhostOntoItsOwnLiveCopy() {
        // What TryLoadFreshRoom leaves behind now that it clears the trails before
        // UnloadLevel, the way Celeste.Level.Reload does: every snapshot the room
        // still holds belongs to an entity the room still holds.
        PlaybackGhostReloadRoom fresh = CreateReloadedGhostRoom(trailBelongsToDestroyedGhost: false);
        PlayerPlayback liveGhost = fresh.Ghost;

        AkronReconstructionRestore restore = RestoreTrailingGhostDocumentInto(fresh);

        Assert.True(restore.Success, restore.Error);
        // The saved ghost has to land on the ghost the room actually holds, not on a
        // reconstructed copy: a reconstructed one would leave the room's own trail
        // rendering a corpse.
        Assert.Same(liveGhost, GetRuntimeField<Entity>(fresh.Snapshot.Sprite, "<Entity>k__BackingField"));
        Assert.Same(fresh.Level, GetRuntimeField<Scene>(liveGhost, "<Scene>k__BackingField"));
        Assert.Contains(GetEntityListContents(fresh.Entities), entity => ReferenceEquals(entity, liveGhost));
        Assert.Equal(2.5f, GetRuntimeField<float>(liveGhost, "time"));
    }

    private sealed class DestroyedTrailOwnerRoom {
        public PlaybackGhostReloadRoot Root = null!;
        public Level Level = null!;
        public EntityList Entities = null!;
        public Entity Owner = null!;
        public TrailManager.Snapshot Snapshot = null!;
    }

    // The same reloaded-room shape on an entity that is not the playback ghost.
    // Celeste.Player is the type this stands for - it trails on every dash and the
    // fresh-room reload removes it explicitly - but Player cannot be walked headlessly
    // because its own graph reaches stripped FNA members, so a built-in Monocle.Entity
    // with the default EntityID takes its place. What the shape needs is only that a
    // Tags.Global snapshot holds the entity's PlayerSprite and that the entity sits at a
    // lower Depth than the TrailManager, which is true of the player (0) and its trails
    // (1) exactly as it is of the ghost.
    private static DestroyedTrailOwnerRoom CreateDestroyedTrailOwnerRoom(
        bool ownerIsTrailing,
        bool trailBelongsToDestroyedOwner
    ) {
        Level level = (Level) RuntimeHelpers.GetUninitializedObject(typeof(Level));
        EntityList entities = LinkSceneEntities(level, CreateDetachedEntityList());

        Entity owner = CreateUninitializedEntity<Entity>();
        AttachGhostRoomHairAndSprite(owner);
        SetRuntimeField(owner, "<Scene>k__BackingField", level);
        SetGhostRoomDepth(owner, 0);

        Entity? destroyed = null;
        if (trailBelongsToDestroyedOwner) {
            destroyed = CreateUninitializedEntity<Entity>();
            AttachGhostRoomHairAndSprite(destroyed);
            SetRuntimeField(destroyed, "<Scene>k__BackingField", null);
            SetGhostRoomDepth(destroyed, 0);
        }

        TrailManager manager = CreateGhostRoomTrailManager(level, out TrailManager.Snapshot[] slots);
        TrailManager.Snapshot? snapshot = ownerIsTrailing
            ? CreateTrailSnapshotFrom(level, manager, slots, 0, destroyed ?? owner, depth: 1)
            : null;

        // The manager is Depth 10 and the owner is Depth 0, so a descending depth sort
        // puts the manager first and the capture walk reaches the owner through
        // TrailManager.snapshots[i].Sprite.<Entity> rather than through its own slot.
        AddDetachedEntity(entities, manager);
        if (snapshot != null) {
            AddDetachedEntity(entities, snapshot);
        }
        AddDetachedEntity(entities, owner);

        return new DestroyedTrailOwnerRoom {
            Root = new PlaybackGhostReloadRoot { Level = level },
            Level = level,
            Entities = entities,
            Owner = owner,
            Snapshot = snapshot!
        };
    }

    private static AkronReconstructionRestore RestoreTrailingOwnerDocumentInto(DestroyedTrailOwnerRoom fresh) {
        DestroyedTrailOwnerRoom saved = CreateDestroyedTrailOwnerRoom(
            ownerIsTrailing: true,
            trailBelongsToDestroyedOwner: false);
        DestroyedTrailOwnerRoom baseline = CreateDestroyedTrailOwnerRoom(
            ownerIsTrailing: true,
            trailBelongsToDestroyedOwner: false);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved.Root, baseline.Root);
        Assert.True(capture.Success, capture.Error);
        return graph.Restore(capture.Document, fresh.Root);
    }

    [Fact]
    public void AnyTrailedEntityTheReloadDestroysProducesTheSameRefusalNotJustTheGhost() {
        // The playback ghost is the reported case, not the whole of it. Every entity a
        // TrailManager.Snapshot holds satisfies the same three conditions: the snapshot
        // carries Tags.Global so it survives UnloadLevel, it keeps the entity's live
        // PlayerSprite and PlayerHair, and it sorts before the entity, so the entity's
        // canonical document path runs through the snapshot. Celeste.Player is the other
        // stock type that reaches this shape - it trails on every dash, and
        // TryLoadFreshRoom removes it by hand before UnloadLevel - and no snapshot
        // exclusion covers it. Clearing the trails, which is what Celeste.Level.Reload
        // does and what TryLoadFreshRoom now does, is what covers it.
        DestroyedTrailOwnerRoom stale = CreateDestroyedTrailOwnerRoom(
            ownerIsTrailing: true,
            trailBelongsToDestroyedOwner: true);

        AkronReconstructionRestore refused = RestoreTrailingOwnerDocumentInto(stale);

        Assert.False(refused.Success);
        Assert.Contains("reconstructed reference edge is not authentic to the fresh room", refused.Error);
        Assert.Contains("edge-field=<Scene>k__BackingField", refused.Error);
        Assert.Contains("edge-parent-fresh=true", refused.Error);
        Assert.Contains("fresh-field-alias=false", refused.Error);
        Assert.DoesNotContain("edge-parent-type=Celeste.PlayerPlayback", refused.Error);

        // Same room, with the surviving trail belonging to the entity the room still
        // holds. That is the room the reload leaves once it clears the trails first.
        DestroyedTrailOwnerRoom fresh = CreateDestroyedTrailOwnerRoom(
            ownerIsTrailing: true,
            trailBelongsToDestroyedOwner: false);
        Image freshSprite = fresh.Snapshot.Sprite;

        AkronReconstructionRestore restored = RestoreTrailingOwnerDocumentInto(fresh);

        Assert.True(restored.Success, restored.Error);
        Assert.Same(fresh.Owner, GetRuntimeField<Entity>(freshSprite, "<Entity>k__BackingField"));
        Assert.Same(fresh.Level, GetRuntimeField<Scene>(fresh.Owner, "<Scene>k__BackingField"));
    }

    // The map-edited half of the reproduction below, and what closes it. A ghost the
    // reloaded room rebuilt under a different EntityID means one of two things, and
    // this is the one where the room the document measured is gone: the map no longer
    // lays entity 42 out at all. Rebuilding it there would hand it the live ghost's
    // entity-list slot, the room's own PlayerSprite and the saved state, and report
    // success.
    [Fact]
    public void AnUnpairedGhostIsRefusedWhenTheMapNoLongerPlacesIt() {
        PlaybackGhostReloadRoom fresh = CreateReloadedGhostRoomWithRenumberedGhost();
        PlayerPlayback liveGhost = fresh.Ghost;
        Image freshSprite = fresh.Snapshot.Sprite;

        AkronReconstructionRestore restore = RestoreTrailingGhostDocumentInto(
            fresh,
            mapIdsWhenSet: new[] { 42, 7 },
            mapIdsAtReload: new[] { 43, 7 });

        Assert.False(restore.Success);
        Assert.Contains("saved map entity is no longer placed by this map", restore.Error);
        Assert.Contains("saved-entity-id=CANADIAN_00:42", restore.Error);
        Assert.Equal(typeof(PlayerPlayback).AssemblyQualifiedName, restore.RefusedTypeName);
        // Refused before any assignment, so the room is still the room the reload
        // built: its own ghost, in its own list slot, holding its own sprite.
        Assert.Same(liveGhost, GetRuntimeField<Entity>(freshSprite, "<Entity>k__BackingField"));
        Assert.Contains(GetEntityListContents(fresh.Entities), entity => ReferenceEquals(entity, liveGhost));
        Assert.Equal(0f, GetRuntimeField<float>(liveGhost, "time"));
    }

    [Fact]
    public void AnUnpairedGhostStillTakesItsSceneEdgeOnStructuralBudgetAloneAndRestoresWrongly() {
        // THIS TEST PINS BEHAVIOUR THAT IS WRONG. It is here so that the day it is
        // fixed, it fails and says so.
        //
        // ValidateReferenceEdge accepts a reference edge with no authenticator at all
        // whenever freshListStructuralTypeCounts holds a remaining occurrence for
        // (target type, structural path with every list index wildcarded). That budget
        // records that SOME object of that type sits at that path in the fresh room. It
        // does not record WHICH, and it does not require the edge's parent to be an
        // object the fresh room holds. So when a saved entity fails to pair - here
        // because the fresh room rebuilt it with a different EntityID - the
        // reconstructed copy's <Scene> edge spends the occurrence that the room's own
        // live entity put there, and the restore reports Success.
        //
        // What ends up wrong is not only that edge. The room's own PlayerSprite is
        // fresh-resolved, so its <Entity> back reference is rewritten to the
        // reconstructed copy, the saved state lands on that copy, and the entity the
        // room actually holds keeps its clean-load state. In game the surviving trail
        // would render the room's sprite at the reconstructed copy's position.
        //
        // The map here is the same map it always was: it lays out both ghosts, and
        // this reload's session state is why entity 42 was not built. The refusal
        // above cannot reach that, and it should not - a room whose session no longer
        // spawns one of its entities has to keep restoring.
        //
        // What that leaves is not "rebuilt beside the live ghost". The saved entity
        // list holds four entities and so does the reloaded room, so the rebuilt ghost
        // takes the live ghost's slot rather than being added next to it, and the
        // ghost the reload built is dropped. That much is the saved population winning,
        // which is what a restore is for.
        //
        // What is wrong is what happens to that dropped ghost, and it is measured
        // below rather than described. Several edges here carry no authenticator and
        // ride the occurrence budget, the <Scene> edge above among them; Snapshot.Hair
        // is the only one of them whose target is a component, and it is the one the
        // "component aliases on occurrence budget alone" question is about. That write
        // is not what makes the room wrong: the rebuilt hair lands in the rebuilt
        // ghost's own Hair field and both halves of the trail end up pointing at that
        // same rebuilt ghost, so the trail is not split between two owners.
        //
        // The harm is on the other side of the same room. The room's own PlayerSprite
        // is fresh-resolved and relabelled, so the object the reload built for ghost 43
        // now belongs to the rebuilt ghost 42 while ghost 43's own component list still
        // lists it, and ghost 43 is left out of the entity list with its Scene still
        // pointing at the Level. That write is a pairing rather than a budget
        // admission - the snapshot's Sprite field is a fresh path and the resolver
        // takes what is in it, with no identity check - so no rule about which
        // component edges the budget admits reaches it. A stricter budget would still
        // refuse this document as a whole, because the restore only gets far enough to
        // make that write while its count-only edges are admitted.
        PlaybackGhostReloadRoom fresh = CreateReloadedGhostRoomWithRenumberedGhost();
        Level level = fresh.Level;
        EntityList entities = fresh.Entities;
        PlayerPlayback liveGhost = fresh.Ghost;
        TrailManager.Snapshot snapshot = fresh.Snapshot;
        Image freshSprite = snapshot.Sprite;
        PlayerHair freshHair = snapshot.Hair;

        AkronReconstructionRestore restore = RestoreTrailingGhostDocumentInto(
            fresh,
            mapIdsWhenSet: new[] { 42, 7 },
            mapIdsAtReload: new[] { 42, 43, 7 });

        // Accepted, with no authenticator: the saved document asked for the fresh Level
        // at a path the fresh room does hold a Level at, and that was enough.
        Assert.True(restore.Success, restore.Error);

        // WRONG: the room's own sprite no longer points at the ghost the room holds.
        Entity? spriteOwner = GetRuntimeField<Entity>(freshSprite, "<Entity>k__BackingField");
        Assert.NotSame(liveGhost, spriteOwner);
        PlayerPlayback reconstructedGhost = Assert.IsType<PlayerPlayback>(spriteOwner);
        // WRONG: the reconstructed copy takes the entity-list slot of the ghost the room
        // load built, and gets the live Level in its Scene on the occurrence budget
        // alone. The ghost LoadLevel produced is dropped from the room entirely.
        Assert.Same(level, GetRuntimeField<Scene>(reconstructedGhost, "<Scene>k__BackingField"));
        Assert.Contains(GetEntityListContents(entities), entity => ReferenceEquals(entity, reconstructedGhost));
        Assert.DoesNotContain(GetEntityListContents(entities), entity => ReferenceEquals(entity, liveGhost));
        // WRONG: the saved state landed on the reconstructed copy, and the ghost the
        // room actually holds kept its clean-load state.
        Assert.Equal(2.5f, GetRuntimeField<float>(reconstructedGhost, "time"));
        Assert.Equal(0f, GetRuntimeField<float>(liveGhost, "time"));
        // The surviving snapshot keeps the room's PlayerSprite and is handed a
        // reconstructed PlayerHair on the occurrence budget alone.
        Assert.Same(freshSprite, snapshot.Sprite);
        Assert.NotSame(freshHair, snapshot.Hair);
        // NOT wrong, and pinned because the comment above used to claim it was: both
        // halves of the trail point at the same ghost afterwards, and it is the rebuilt
        // one. The rebuilt hair goes where the document says it goes.
        Assert.Same(reconstructedGhost, GetRuntimeField<Entity>(snapshot.Hair!, "<Entity>k__BackingField"));
        Assert.Same(reconstructedGhost, GetRuntimeField<Entity>(snapshot.Sprite!, "<Entity>k__BackingField"));
        Assert.Contains(snapshot.Hair, GetComponentListContents(reconstructedGhost));
        // WRONG, and this is the part no rule about component edges reaches: the ghost
        // the reload built is out of the entity list while its Scene still points at
        // the Level, and its own component list still holds the PlayerSprite that now
        // belongs to the rebuilt ghost.
        Assert.Same(level, GetRuntimeField<Scene>(liveGhost, "<Scene>k__BackingField"));
        Assert.Contains(freshSprite, GetComponentListContents(liveGhost));
        Assert.Contains(freshHair, GetComponentListContents(liveGhost));
    }

    // The room the occurrence budget decided by document order alone, and the one this
    // rule exists for. The map is identical on both sides and still lays out every id
    // in play, so the map rule is inert; a saved entity fails to pair only because this
    // run's session built a different one of two same-typed map entities. Two saved
    // trails put two PlayerSprite.<Entity> back references on one wildcarded path,
    // entities._items[*].Sprite.<Entity>, and the reload left one PlayerPlayback there:
    // one occurrence against two edges.
    //
    // Measured before this was closed, same population, same map, only the order of the
    // two entities in the saved document reversed:
    //
    //   paired trail first  - the paired edge spent the occurrence and the unpairable
    //                         one was refused.
    //   unpairable first    - the unpairable edge took the occurrence and the restore
    //                         reported success, with the ghost the reload built gone
    //                         from the room, a reconstruction wearing SourceId 42 and
    //                         the saved time=2.5 in its list slot, and the surviving
    //                         trail's live PlayerSprite pointed at the reconstruction
    //                         while its PlayerHair still pointed at the reload's ghost.
    //
    // Both orders refuse now, and for the same reason in both:
    // RefuseAnEdgeThatDropsAFreshObjectTheDocumentKeeps. The count could not see the
    // contradiction - this document says the paired ghost is still in the room and also
    // says that ghost's live sprite belongs to something the room does not have - and
    // freshOwners is complete before any edge is validated, so the verdict no longer
    // depends on which edge is reached first.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnUnpairableTrailedMapEntityIsRefusedInEitherDocumentOrder(bool unpairableFirst) {
        PlaybackGhostReloadRoom fresh =
            CreateTwoTrailReloadedGhostRoomTheSessionBuiltDifferently(unpairableFirst);
        PlayerPlayback liveGhost = fresh.Ghost;
        Image freshSprite = fresh.Snapshot.Sprite;
        Entity? pairedGhost = GetRuntimeField<Entity>(freshSprite, "<Entity>k__BackingField");

        AkronReconstructionRestore restore = RestoreTwoTrailGhostDocumentInto(
            fresh,
            unpairableFirst,
            mapIdsWhenSet: new[] { 42, 7, 8 },
            mapIdsAtReload: new[] { 42, 7, 8 });

        Assert.False(restore.Success);
        Assert.Contains(
            "reconstructed reference edge would drop a fresh object this document keeps",
            restore.Error);
        // The refusal names the node the write would have dropped, which is the one
        // thing a log reader needs to see that the two claims contradict each other.
        Assert.Contains(";displaced-node=", restore.Error);
        // Refused before any assignment, so the room is still the room the reload built:
        // its own ghost, in the list, with its clean-load state, and the trail that
        // survived still pointing at the ghost that owns it.
        Assert.Contains(GetEntityListContents(fresh.Entities), entity => ReferenceEquals(entity, liveGhost));
        Assert.Equal(0f, GetRuntimeField<float>(liveGhost, "time"));
        Assert.Same(pairedGhost, GetRuntimeField<Entity>(freshSprite, "<Entity>k__BackingField"));
    }

    // The other half of the same order dependence, and the one that was still open.
    // An edge ownership proves needs no occurrence - the exhausted branch lets it
    // through - and it used to spend one anyway whenever one was left, so the two
    // edges below were decided by which of them the document reached first.
    //
    // Two instances of one mod entity, each carrying a component that holds a runtime
    // state object, and the reload built one of the two: the fresh room records one
    // occurrence of that state type at
    // entities._items[*].<Components>k__BackingField.components._items[*].State and
    // the document has two edges there. The paired component's edge is proved twice
    // over and independently - by savedOwnerEdge with exactParentSlot, because the
    // fresh field holds an object of the same type, and by
    // freshComponentCapturedFreshEdge, because a fresh component holds a fresh object
    // in an exactly typed field - so the room does not turn on which of the two.
    // The rebuilt one has nothing but the count.
    //
    // Measured before this was closed, same room, only the two entities swapped in the
    // saved list:
    //
    //   paired first  - the proved edge spent the occurrence and the rebuilt one was
    //                   refused, on 010f660 as well as here.
    //   rebuilt first - it took the occurrence, the proved edge fell through the
    //                   exhausted branch's escape, and the room was right in both
    //                   halves.
    //
    // So the refusal was the wrong answer of the two, which is why this test asserts
    // the same load in both orders rather than the same refusal.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARoomIsNotRefusedBecauseAnOwnershipProvedEdgeWasReachedFirst(bool pairedFirst) {
        OwnershipProvedEdgeRoot saved = CreateRuntimeStateHolderRoom(cleanReload: false, pairedFirst);
        OwnershipProvedEdgeRoot baseline = CreateRuntimeStateHolderRoom(cleanReload: true, pairedFirst);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource, _ => string.Empty);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        OwnershipProvedEdgeRoot fresh = CreateRuntimeStateHolderRoom(cleanReload: true, pairedFirst);
        List<Entity> freshEntities = GetEntityListContents(fresh.Entities);
        RuntimeStateHolderComponent paired = HolderComponentOf(freshEntities[pairedFirst ? 0 : 1]);
        RuntimeStateHolderComponent rebuilt = HolderComponentOf(freshEntities[pairedFirst ? 1 : 0]);
        HeldRuntimeState liveState = paired.State!;

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        // The component the reload wired keeps its own live object, with the saved
        // value written onto it.
        Assert.Same(liveState, paired.State);
        Assert.Equal(7, paired.State!.Value);
        // The other one had nothing to keep, so it gets a rebuilt object of its own.
        Assert.NotNull(rebuilt.State);
        Assert.NotSame(paired.State, rebuilt.State);
        Assert.Equal(9, rebuilt.State!.Value);
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    // cleanReload false is the Set frame, where both components hold their state.
    // cleanReload true is what a clean load of the same room leaves: entity 31's
    // component has its state and entity 32's has not built one yet, which is the
    // ordinary shape for an object a component creates the first time it needs it.
    //
    // The Scene is a room slice rather than a constructed Scene, which is how every
    // room fixture in this file is built. A real Scene's constructor also leaves a
    // Tracker, TagLists, a RendererList, an actualDepthLookup and a HelperEntity in
    // the entity list, and none of those is read by the two State edges this room
    // turns on: the occurrence this test is about is counted only where a
    // HeldRuntimeState sits at
    // entities._items[*].<Components>k__BackingField.components._items[*].State, and
    // nothing else in the room puts one there.
    private static OwnershipProvedEdgeRoot CreateRuntimeStateHolderRoom(bool cleanReload, bool pairedFirst) {
        Scene scene = (Scene) RuntimeHelpers.GetUninitializedObject(typeof(Scene));
        EntityList entities = LinkSceneEntities(scene, CreateDetachedEntityList());
        (int Id, int? Value) paired = (31, 7);
        (int Id, int? Value) rebuilt = (32, cleanReload ? null : 9);
        int spawnOrder = 0;
        foreach ((int Id, int? Value) holder in pairedFirst
                     ? new[] { paired, rebuilt }
                     : new[] { rebuilt, paired }) {
            AddRuntimeStateHolderEntity(scene, entities, holder.Id, holder.Value, spawnOrder++);
        }
        return new OwnershipProvedEdgeRoot { Scene = scene, Entities = entities };
    }

    private static void AddRuntimeStateHolderEntity(
        Scene scene,
        EntityList entities,
        int sourceId,
        int? stateValue,
        int spawnOrder
    ) {
        RuntimeStateHolderEntity owner = CreateUninitializedEntity<RuntimeStateHolderEntity>();
        ComponentList components = CreateDetachedComponentList(owner);
        SetRuntimeField(owner, "Active", true);
        SetRuntimeField(owner, "Visible", true);
        SetRuntimeField(owner, "Collidable", true);
        SetRuntimeField(owner, "<Scene>k__BackingField", scene);
        SetRuntimeField(owner, "<SourceId>k__BackingField", CreateEntityId("a00", sourceId));
        // Two instances of one entity type share a Depth, and Monocle.Scene hands out
        // a strictly decreasing actualDepth within one depth, so EntityList.CompareDepth
        // leaves them in spawn order. That is what makes the list order this room turns
        // on a real order rather than an artefact of the fixture. The magnitude of the
        // step is immaterial here rather than unread: capture records actualDepth like
        // every other instance field and Verify compares it, and all three rooms are
        // built by this method, so only the order it produces reaches the verdict.
        SetRuntimeField(owner, "depth", 0);
        SetRuntimeField(owner, "actualDepth", -0.000001d * spawnOrder);

        RuntimeStateHolderComponent holder =
            (RuntimeStateHolderComponent) RuntimeHelpers.GetUninitializedObject(
                typeof(RuntimeStateHolderComponent));
        SetRuntimeField(holder, "<Entity>k__BackingField", owner);
        SetRuntimeField(holder, "Active", true);
        if (stateValue != null) {
            holder.State = new HeldRuntimeState { Value = stateValue.Value };
        }

        List<Component> ordered = new List<Component> { holder };
        SetRuntimeField(components, "components", ordered);
        SetRuntimeField(components, "current", new HashSet<Component>(ordered));
        AddDetachedEntity(entities, owner);
    }

    private static RuntimeStateHolderComponent HolderComponentOf(Entity entity) {
        return GetComponentListContents(entity).OfType<RuntimeStateHolderComponent>().Single();
    }

    [Fact]
    public void ASavedGhostThatWasNotTrailingPairsThroughItsOwnEntityListSlot() {
        // The saved half of the same two-factor failure. With no live trail at the
        // Set frame the ghost's canonical document parent is its own entity-list slot
        // rather than a foreign entity's field, so the destroyed ghost the reloaded
        // room still holds is never reachable at that path and the pairing goes
        // through TryResolveFreshOwnedEntity instead.
        PlaybackGhostReloadRoom fresh = CreateReloadedGhostRoom(trailBelongsToDestroyedGhost: true);
        PlayerPlayback liveGhost = fresh.Ghost;

        AkronReconstructionRestore restore = RestoreTrailingGhostDocumentInto(
            fresh,
            savedGhostIsTrailing: false);

        Assert.True(restore.Success, restore.Error);
        Assert.Same(liveGhost, fresh.Ghost);
        Assert.Same(fresh.Level, GetRuntimeField<Scene>(liveGhost, "<Scene>k__BackingField"));
    }

    [Theory]
    [InlineData("collider", "normalHitbox")]
    [InlineData("collider", "duckHitbox")]
    [InlineData("hurtbox", "normalHurtbox")]
    [InlineData("hurtbox", "duckHurtbox")]
    public void PlayerRuntimeColliderAliasesAreLimitedToNamedStoredShapes(
        string activeField,
        string storedField
    ) {
        Assert.True(AkronReconstructionGraph.IsPlayerRuntimeColliderAlias(
            typeof(Player), activeField, storedField, typeof(Hitbox)));
        Assert.False(AkronReconstructionGraph.IsPlayerRuntimeColliderAlias(
            typeof(Player), activeField, "starFlyHitbox", typeof(Hitbox)));
        Assert.False(AkronReconstructionGraph.IsPlayerRuntimeColliderAlias(
            typeof(Entity), activeField, storedField, typeof(Hitbox)));
    }

    [Fact]
    public void RestoreAllowsAMissingCoreStackCollection() {
        Stack<IEnumerator> savedStack = new Stack<IEnumerator>();
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new PassiveDataRoot { Value = savedStack },
            new PassiveDataRoot());
        Assert.True(capture.Success, capture.Error);
        PassiveDataRoot fresh = new PassiveDataRoot();

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Empty(Assert.IsType<Stack<IEnumerator>>(fresh.Value));
    }

    [Fact]
    public void RestoreAllowsACompilerIteratorOwnedByAnAuthenticatedFreshObject() {
        IteratorOwner savedOwner = new IteratorOwner { Value = 10 };
        IEnumerator savedIterator = savedOwner.Routine().GetEnumerator();
        Assert.True(savedIterator.MoveNext());
        IteratorStateRoot saved = new IteratorStateRoot {
            Owner = savedOwner,
            States = new Stack<IEnumerator>(new[] { savedIterator })
        };
        IteratorStateRoot baseline = new IteratorStateRoot {
            Owner = new IteratorOwner(),
            States = new Stack<IEnumerator>()
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        IteratorStateRoot fresh = new IteratorStateRoot {
            Owner = new IteratorOwner(),
            States = new Stack<IEnumerator>()
        };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        IEnumerator restoredIterator = Assert.Single(fresh.States);
        Assert.True(restoredIterator.MoveNext());
        Assert.Equal(11, restoredIterator.Current);
    }

    [Fact]
    public void CompilerIteratorCanRepeatInsideItsOwnerEntityCoroutineStack() {
        SavedSceneRoot saved = CreateDuplicateIteratorScene(includeIterator: true);
        SavedSceneRoot baseline = CreateDuplicateIteratorScene(includeIterator: false);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource, _ => string.Empty);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        SavedSceneRoot fresh = CreateDuplicateIteratorScene(includeIterator: false);

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        IteratorOwnerEntity restoredOwner = Assert.IsType<IteratorOwnerEntity>(
            Assert.Single(GetEntityListContents(fresh.Entities)));
        Coroutine restoredCoroutine = Assert.IsType<Coroutine>(Assert.Single(
            GetComponentListContents(restoredOwner)));
        IEnumerator[] restoredStack = GetRuntimeField<Stack<IEnumerator>>(
                restoredCoroutine,
                "enumerators")
            .ToArray();
        Assert.Equal(2, restoredStack.Length);
        Assert.Same(restoredStack[0], restoredStack[1]);
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void CompilerIteratorCannotAliasIntoAnUnrelatedRootArray() {
        SavedSceneRoot savedScene = CreateDuplicateIteratorScene(includeIterator: true);
        IteratorOwnerEntity savedOwner = Assert.IsType<IteratorOwnerEntity>(
            Assert.Single(GetEntityListContents(savedScene.Entities)));
        Coroutine savedCoroutine = Assert.IsType<Coroutine>(Assert.Single(
            GetComponentListContents(savedOwner)));
        IEnumerator savedIterator = GetRuntimeField<Stack<IEnumerator>>(
                savedCoroutine,
                "enumerators")
            .Peek();
        IteratorAliasSceneRoot saved = new IteratorAliasSceneRoot {
            Scene = savedScene.Scene,
            Entities = savedScene.Entities,
            Unrelated = new[] { savedIterator }
        };
        SavedSceneRoot baselineScene = CreateDuplicateIteratorScene(includeIterator: false);
        IteratorAliasSceneRoot baseline = new IteratorAliasSceneRoot {
            Scene = baselineScene.Scene,
            Entities = baselineScene.Entities
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource, _ => string.Empty);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        SavedSceneRoot freshScene = CreateDuplicateIteratorScene(includeIterator: false);
        IteratorAliasSceneRoot fresh = new IteratorAliasSceneRoot {
            Scene = freshScene.Scene,
            Entities = freshScene.Entities
        };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.False(restore.Success);
        Assert.Contains("reference edge is not authentic", restore.Error);
    }

    [Fact]
    public void RestoreRejectsABuiltInScreenWipeWithoutFreshLevelOwnership() {
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new PassiveDataRoot { Value = new Water.Ripple() },
            new PassiveDataRoot());
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode wipeNode = capture.Document.Nodes.Single(node =>
            node.ParentFieldName == nameof(PassiveDataRoot.Value));
        wipeNode.TypeName = typeof(SpotlightWipe).AssemblyQualifiedName!;
        wipeNode.Fields.Clear();
        PassiveDataRoot fresh = new PassiveDataRoot();

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.False(restore.Success);
        Assert.Contains("not authentic", restore.Error);
    }

    [Fact]
    public void RestoreAllowsAnMTextureWrapperWithoutFreshAliasPaths() {
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new PassiveDataRoot { Value = new Water.Ripple() },
            new PassiveDataRoot());
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode textureNode = capture.Document.Nodes.Single(node =>
            node.ParentFieldName == nameof(PassiveDataRoot.Value));
        textureNode.TypeName = typeof(MTexture).AssemblyQualifiedName!;
        textureNode.Fields.Clear();
        PassiveDataRoot fresh = new PassiveDataRoot();

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.IsType<MTexture>(fresh.Value);
    }

    [Fact]
    public void ConcurrentDictionaryStorageCanRestoreEntriesMissingFromTheFreshState() {
        ConcurrentDictionaryRoot saved = new ConcurrentDictionaryRoot();
        saved.Values["saved-key"] = 37f;
        ConcurrentDictionaryRoot baseline = new ConcurrentDictionaryRoot();
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        ConcurrentDictionaryRoot fresh = new ConcurrentDictionaryRoot();

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Equal(37f, fresh.Values["saved-key"]);
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void RestoreDoesNotReuseOneFreshListEntryToAuthenticateAnotherEntity() {
        EntityListRoot saved = new EntityListRoot {
            Entities = new List<Entity> {
                CreateUninitializedEntity<Entity>(),
                CreateUninitializedEntity<Entity>()
            }
        };
        EntityListRoot baseline = new EntityListRoot {
            Entities = new List<Entity> { CreateUninitializedEntity<Entity>() }
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        EntityListRoot fresh = new EntityListRoot {
            Entities = new List<Entity> { CreateUninitializedEntity<Entity>() }
        };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.False(restore.Success);
        Assert.Contains("entity canonical array is not owned by its scene EntityList", restore.Error);
    }

    [Fact]
    public void RestoreDoesNotReuseOneAuthenticatedEntityReferenceAtAnotherListIndex() {
        Entity savedEntity = CreateUninitializedEntity<Entity>();
        EntityListRoot saved = new EntityListRoot {
            Entities = new List<Entity>(1) { savedEntity }
        };
        EntityListRoot baseline = new EntityListRoot {
            Entities = new List<Entity>(1) { CreateUninitializedEntity<Entity>() }
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode listNode = capture.Document.Nodes.Single(node =>
            node.TypeName == typeof(List<Entity>).AssemblyQualifiedName);
        AkronReconstructionNode itemsNode = capture.Document.Nodes.Single(node =>
            node.ParentNodeId == listNode.Id && node.ParentFieldName == "_items");
        Assert.Single(itemsNode.Items);
        itemsNode.ArrayLengths[0] = 2;
        itemsNode.Items.Add(itemsNode.Items[0]);
        AkronReconstructionField size = listNode.Fields.Single(field => field.Name == "_size");
        size.Value.Scalar = "2";
        EntityListRoot fresh = new EntityListRoot {
            Entities = new List<Entity>(1) { CreateUninitializedEntity<Entity>() }
        };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.False(restore.Success);
        Assert.Contains("entity canonical array is not owned by its scene EntityList", restore.Error);
    }

    [Fact]
    public void DeserializeRejectsTooManyReconstructionNodesWhileStreaming() {
        AkronReconstructionGraph graph = new AkronReconstructionGraph(
            IsLiveResource,
            maxJsonTokenCount: 100,
            maxJsonContainerCount: 100,
            maxJsonStringChars: 100,
            maxJsonBinaryBytes: 100,
            maxJsonNodeCount: 1);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            graph.Deserialize("{\"Nodes\":[{},{}]}"));

        Assert.Contains("node count exceeds", exception.Message);
    }

    [Fact]
    public void DeserializeRejectsTooManyNestedReconstructionRecordsWhileStreaming() {
        AkronReconstructionGraph graph = new AkronReconstructionGraph(
            IsLiveResource,
            maxJsonTokenCount: 100,
            maxJsonContainerCount: 100,
            maxJsonStringChars: 100,
            maxJsonBinaryBytes: 100,
            maxJsonNodeCount: 10,
            maxJsonRecordCount: 3);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            graph.Deserialize("{\"Nodes\":[{\"dc\":[{\"tg\":{}},{\"tg\":{}}]}]}"));

        Assert.Contains("record count exceeds", exception.Message);
    }

    [Fact]
    public void DeserializeSeparatelyCapsComplexNestedRecordsWhileStreaming() {
        AkronReconstructionGraph graph = new AkronReconstructionGraph(
            IsLiveResource,
            maxJsonTokenCount: 100,
            maxJsonContainerCount: 100,
            maxJsonStringChars: 100,
            maxJsonBinaryBytes: 100,
            maxJsonNodeCount: 10,
            maxJsonRecordCount: 100,
            maxJsonExpensiveRecordCount: 1);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            graph.Deserialize("{\"Nodes\":[{\"dc\":[{\"tg\":{}},{\"tg\":{}}]}]}"));

        Assert.Contains("complex record count exceeds", exception.Message);
    }

    [Fact]
    public void DefaultComplexRecordBudgetCoversHeartOfStormSnapshots() {
        // Spring Collab 2020's Heart of the Storm produced 337,736 complex
        // records in a real two-slot remote capture. This is valid map state,
        // so the hostile-input guard must leave room for it.
        Assert.True(
            AkronReconstructionGraph.DefaultMaxJsonExpensiveRecordCount >= 337_736,
            "The default complex-record budget rejects Heart of the Storm.");
    }

    // The read side has always refused a snapshot past MaxDecompressedSnapshotBytes,
    // so a save allowed to pass it is a slot every later load refuses with no hint
    // of when it went wrong. The failure has to land on the Set that wrote it.
    [Fact]
    public void SaveSnapshotRefusesAStatePastTheSnapshotSizeLimit() {
        string directory = Path.Combine(Path.GetTempPath(), "akron-write-cap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
            AkronReconstructionCapture capture = graph.Capture(
                new TestRoot { Primary = new TestNode() },
                new TestRoot { Primary = new TestNode() });
            Assert.True(capture.Success, capture.Error);

            bool saved = AkronStartPosReconstruction.SaveSnapshot(
                "Akron StartPos write cap 1",
                "Celeste/1-ForsakenCity",
                "1",
                0,
                capture.Document,
                out string error,
                directory,
                maxDecompressedBytes: 64);

            Assert.False(saved);
            Assert.Contains("size limit", error);
            // Neither the slot file nor the temporary file may survive a refused save.
            Assert.Empty(Directory.GetFiles(directory, "*", SearchOption.AllDirectories));
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    // A capture can pass the byte cap and still trip a structural read ceiling
    // (many tiny nodes serialize small). Before this, such a slot saved and then
    // refused on every load, which is the failure the whole path exists to stop.
    // SaveSnapshot now reads its own output back and fails the Set instead.
    [Fact]
    public void SaveSnapshotRefusesAStateThatWouldTripAStructuralReadCeiling() {
        string directory = Path.Combine(Path.GetTempPath(), "akron-readback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
            AkronReconstructionCapture capture = graph.Capture(
                new TestRoot { Primary = new TestNode { Value = 5 } },
                new TestRoot { Primary = new TestNode() });
            Assert.True(capture.Success, capture.Error);
            Assert.True(capture.Document.Nodes.Count > 1);

            // A verifier whose node ceiling is 1 stands in for a real graph whose
            // default ceiling a huge tiny-node capture would exceed while staying
            // under the byte cap.
            AkronReconstructionGraph tinyCeiling = new AkronReconstructionGraph(
                IsLiveResource,
                maxJsonNodeCount: 1);

            bool saved = AkronStartPosReconstruction.SaveSnapshot(
                "Akron StartPos readback 1",
                "Celeste/1-ForsakenCity",
                "1",
                0,
                capture.Document,
                out string error,
                directory,
                maxDecompressedBytes: AkronStartPosReconstruction.MaxDecompressedSnapshotBytes,
                verificationGraph: tinyCeiling);

            Assert.False(saved);
            Assert.Contains("read back", error);
            Assert.Contains("node count exceeds", error);
            // The unreadable slot must not survive on disk.
            Assert.Empty(Directory.GetFiles(directory, "*", SearchOption.AllDirectories));
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    // A weak reference whose target is a later weak reference would be rebuilt
    // before its target exists, so it loaded on no cold restore. Capture refuses
    // it now, at the Set, so the dead slot is never written.
    [Fact]
    public void CaptureRefusesAWeakReferenceTargetingALaterWeakReference() {
        WeakChainRoot saved = new WeakChainRoot();
        saved.Outer = new WeakReference(new WeakReference(new TestNode { Value = 9 }));
        WeakChainRoot baseline = new WeakChainRoot();
        baseline.Outer = new WeakReference(new WeakReference(new TestNode()));
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);

        AkronReconstructionCapture capture = graph.Capture(saved, baseline);

        Assert.False(capture.Success);
        Assert.Contains("weak reference whose target is itself or a later weak reference", capture.Error);
    }

    // A weak reference can legally target itself. Its target id equals its own
    // id, so the rebuild pass would resolve the target before the node exists
    // in Objects and every cold load would refuse the slot. Capture refuses it.
    [Fact]
    public void CaptureRefusesAWeakReferenceTargetingItself() {
        WeakChainRoot saved = new WeakChainRoot { Outer = new WeakReference(null) };
        saved.Outer.Target = saved.Outer;
        WeakChainRoot baseline = new WeakChainRoot { Outer = new WeakReference(null) };
        baseline.Outer.Target = baseline.Outer;
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);

        AkronReconstructionCapture capture = graph.Capture(saved, baseline);

        Assert.False(capture.Success);
        Assert.Contains("weak reference whose target is itself or a later weak reference", capture.Error);
    }

    // The composition report exists to size format work from real files, so its
    // accounting has to be checked against a snapshot the real writer produced:
    // identity read from the header, counts matching the document, and every
    // attributed byte fitting inside the actual decompressed size.
    [Fact]
    public void SnapshotCompositionReportAccountsForAWrittenSnapshot() {
        string directory = Path.Combine(Path.GetTempPath(), "akron-composition-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
            AkronReconstructionCapture capture = graph.Capture(
                new TestRoot { Primary = new TestNode { Value = 37 } },
                new TestRoot { Primary = new TestNode() });
            Assert.True(capture.Success, capture.Error);
            Assert.True(AkronStartPosReconstruction.SaveSnapshot(
                "Akron StartPos composition 1",
                "Celeste/1-ForsakenCity",
                "1",
                0,
                capture.Document,
                out string error,
                directory), error);
            string path = AkronStartPosReconstruction.GetSnapshotPath("Akron StartPos composition 1", directory);

            AkronSnapshotComposition.Report report = AkronSnapshotComposition.AnalyzeFile(path);

            long decompressedBytes;
            using (FileStream reading = File.OpenRead(path))
            using (GZipStream decompressing = new GZipStream(reading, CompressionMode.Decompress)) {
                byte[] buffer = new byte[65536];
                decompressedBytes = 0;
                int read;
                while ((read = decompressing.Read(buffer, 0, buffer.Length)) > 0) {
                    decompressedBytes += read;
                }
            }
            Assert.Equal(decompressedBytes, report.DecompressedBytes);
            Assert.Equal(new FileInfo(path).Length, report.CompressedBytes);
            Assert.Equal("Akron StartPos composition 1", report.SlotName);
            Assert.Equal("Celeste/1-ForsakenCity", report.MapSid);
            Assert.Equal("1", report.Room);
            Assert.Equal(capture.Document.Nodes.Count, report.NodeCount);
            Assert.True(report.TokenCount > 0);
            Assert.True(report.TypeNameBytes > 0, "No type-name bytes were attributed.");
            Assert.True(report.DistinctTypeNameCount >= 1);
            // The byte figures are token-text estimates that undercount escapes
            // and skip commas, so they must land inside the real size, never past it.
            Assert.InRange(report.AttributedBytes, 1, report.DecompressedBytes);
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    // The report echoes snapshot-derived strings (slot name, map, room, and the
    // most-repeated values) into a log a player may share. A crafted snapshot
    // must not be able to forge extra log records with newlines or the Unicode
    // line/paragraph separators, which char.IsControl alone misses.
    // The report echoes snapshot-derived strings (slot name, map, room, and the
    // most-repeated values) into a log a player may share. A crafted snapshot
    // must not be able to forge extra log records with newlines or the Unicode
    // line and paragraph separators, which char.IsControl alone misses.
    [Fact]
    public void SnapshotCompositionReportNeverEmitsLineBreaksFromSnapshotData() {
        string poison = "a\rb\nc\u2028d\u2029e\tf";
        AkronSnapshotComposition.Report report = new AkronSnapshotComposition.Report {
            FilePath = "v10-" + poison + ".json.gz",
            SlotName = poison,
            MapSid = poison,
            Room = poison,
            DecompressedBytes = 1000,
            TokenCount = 1,
            TopWaste = { (poison, 5, 100) }
        };

        foreach (string line in AkronSnapshotComposition.Describe(report)) {
            foreach (char forbidden in new[] { '\r', '\n', '\u2028', '\u2029' }) {
                Assert.DoesNotContain(forbidden, line);
            }
        }
    }

    // The failure path echoes the exception message, and a file-open failure
    // puts the file's path inside that message, so a poisoned file name must
    // not forge log records through it either.
    [Fact]
    public void SnapshotCompositionFailureLogNeverEmitsLineBreaksFromThePath() {
        string poisonedPath = "v10-a\rb\nc\u2028d\u2029e.json.gz";
        Exception failure = new IOException("could not open '" + poisonedPath + "'");

        string line = AkronSnapshotComposition.DescribeFailure(poisonedPath, failure);

        Assert.Contains(nameof(IOException), line);
        foreach (char forbidden in new[] { '\r', '\n', '\u2028', '\u2029' }) {
            Assert.DoesNotContain(forbidden, line);
        }
    }

    // Walking a WeakReference's fields reaches its GC-handle IntPtr, which used
    // to refuse the slot; Spring Collab 2020's stylegrounds hold one, so every
    // Heart of the Storm capture died on it. The capture now stores the target
    // and flag, and the restore rebuilds the weak reference around the restored
    // target - through a full serialize/deserialize, like a real cold load.
    [Fact]
    public void WeakReferencesRoundTripAroundTheirRestoredTarget() {
        TestNode savedTarget = new TestNode { Value = 41 };
        WeakReferenceRoot saved = new WeakReferenceRoot {
            Strong = savedTarget,
            Weak = new WeakReference(savedTarget, trackResurrection: true),
            TypedWeak = new WeakReference<TestNode>(savedTarget)
        };
        TestNode baselineTarget = new TestNode();
        WeakReferenceRoot baseline = new WeakReferenceRoot {
            Strong = baselineTarget,
            Weak = new WeakReference(baselineTarget),
            TypedWeak = new WeakReference<TestNode>(baselineTarget)
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionDocument document = graph.Deserialize(graph.Serialize(capture.Document));

        TestNode freshTarget = new TestNode();
        WeakReferenceRoot fresh = new WeakReferenceRoot {
            Strong = freshTarget,
            Weak = new WeakReference(freshTarget),
            TypedWeak = new WeakReference<TestNode>(freshTarget)
        };
        AkronReconstructionRestore restore = graph.Restore(document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Equal(41, fresh.Strong.Value);
        Assert.True(fresh.Weak.TrackResurrection);
        Assert.Same(fresh.Strong, fresh.Weak.Target);
        Assert.True(fresh.TypedWeak.TryGetTarget(out TestNode? typedTarget));
        Assert.Same(fresh.Strong, typedTarget);
        Assert.True(graph.Verify(document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void ADeadWeakReferenceRestoresDead() {
        WeakReferenceRoot saved = new WeakReferenceRoot {
            Strong = new TestNode(),
            Weak = new WeakReference(null),
            TypedWeak = new WeakReference<TestNode>(null!)
        };
        WeakReferenceRoot baseline = new WeakReferenceRoot {
            Strong = new TestNode(),
            Weak = new WeakReference(null),
            TypedWeak = new WeakReference<TestNode>(null!)
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionDocument document = graph.Deserialize(graph.Serialize(capture.Document));

        WeakReferenceRoot fresh = new WeakReferenceRoot {
            Strong = new TestNode(),
            Weak = new WeakReference(null),
            TypedWeak = new WeakReference<TestNode>(null!)
        };
        AkronReconstructionRestore restore = graph.Restore(document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Null(fresh.Weak.Target);
        Assert.False(fresh.Weak.TrackResurrection);
        Assert.False(fresh.TypedWeak.TryGetTarget(out _));
        Assert.True(graph.Verify(document, restore, Array.Empty<string>()).Success);
    }

    // The measured Heart of the Storm shape exactly: a backdrop-like object
    // holding a weak reference to itself.
    [Fact]
    public void AWeakReferenceToItsOwnHolderRoundTrips() {
        WeakReferenceRoot saved = new WeakReferenceRoot {
            Strong = new TestNode(),
            Weak = null!,
            TypedWeak = new WeakReference<TestNode>(null!)
        };
        saved.Weak = new WeakReference(saved);
        WeakReferenceRoot baseline = new WeakReferenceRoot {
            Strong = new TestNode(),
            Weak = null!,
            TypedWeak = new WeakReference<TestNode>(null!)
        };
        baseline.Weak = new WeakReference(baseline);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionDocument document = graph.Deserialize(graph.Serialize(capture.Document));

        WeakReferenceRoot fresh = new WeakReferenceRoot {
            Strong = new TestNode(),
            Weak = null!,
            TypedWeak = new WeakReference<TestNode>(null!)
        };
        fresh.Weak = new WeakReference(fresh);
        AkronReconstructionRestore restore = graph.Restore(document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Same(fresh, fresh.Weak.Target);
        Assert.True(graph.Verify(document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void RestoreRejectsAForgedStructuralParentEdge() {
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new TestRoot { Primary = new TestNode() },
            new TestRoot { Primary = new TestNode() });
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode primaryNode = capture.Document.Nodes.Single(node =>
            node.ParentFieldName == nameof(TestRoot.Primary));
        primaryNode.ParentFieldName = nameof(TestRoot.Secondary);

        AkronReconstructionRestore restore = graph.Restore(
            capture.Document,
            new TestRoot { Primary = new TestNode() });

        Assert.False(restore.Success);
        Assert.Contains("parent edge is invalid", restore.Error);
    }

    [Fact]
    public void ReconstructedCallbackTargetRequiresTheFreshStructuralMethod() {
        TestNode savedTarget = new TestNode { Value = 37 };
        TestNode baselineTarget = new TestNode();
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new TestRoot { Callback = savedTarget.Increment },
            new TestRoot { Callback = baselineTarget.Increment });
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode delegateNode = capture.Document.Nodes.Single(node => node.Kind == "delegate");
        AkronReconstructionNode targetNode = capture.Document.Nodes.Single(node =>
            node.ParentNodeId == delegateNode.Id && node.ParentKind == "delegate");
        targetNode.UseFreshObject = false;
        targetNode.FreshPath.Clear();
        TestNode freshTarget = new TestNode();
        TestRoot fresh = new TestRoot { Callback = freshTarget.Increment };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.NotSame(freshTarget, fresh.Callback.Target);
        fresh.Callback();
        Assert.Equal(38, Assert.IsType<TestNode>(fresh.Callback.Target).Value);
    }

    [Fact]
    public void ReconstructedCallbackClosureCanPointBackToItsFreshDeclaringOwner() {
        CallbackClosureOwner savedOwner = new CallbackClosureOwner { Value = 37 };
        CallbackClosureOwner baselineOwner = new CallbackClosureOwner();
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new CallbackClosureRoot { Owner = savedOwner, Callback = savedOwner.CreateCallback(5) },
            new CallbackClosureRoot {
                Owner = baselineOwner,
                Callback = baselineOwner.CreateCallback(5)
            });
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode closureNode = capture.Document.Nodes.Single(node =>
            node.ParentKind == "delegate" &&
            node.TypeName.Contains("DisplayClass", StringComparison.Ordinal));
        closureNode.UseFreshObject = false;
        closureNode.FreshPath.Clear();
        CallbackClosureOwner freshOwner = new CallbackClosureOwner();
        CallbackClosureRoot fresh = new CallbackClosureRoot {
            Owner = freshOwner,
            Callback = freshOwner.CreateCallback(5)
        };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        fresh.Callback();
        Assert.Equal(42, freshOwner.Value);
    }

    [Fact]
    public void ReconstructedCallbackClosureCanCaptureAnExactFreshObject() {
        CallbackCapturedTarget savedTarget = CreateCallbackCapturedTarget(37);
        CallbackCapturedTarget baselineTarget = CreateCallbackCapturedTarget(0);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new CapturedFreshCallbackRoot {
                Entities = CreateSourceEntityListOwnerRoot(savedTarget).Entities,
                Callback = CapturedFreshCallbackFactory.Create(savedTarget)
            },
            new CapturedFreshCallbackRoot {
                Entities = CreateSourceEntityListOwnerRoot(baselineTarget).Entities,
                Callback = CapturedFreshCallbackFactory.Create(baselineTarget)
            });
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode closureNode = capture.Document.Nodes.Single(node =>
            node.ParentKind == "delegate" &&
            node.TypeName.Contains("DisplayClass", StringComparison.Ordinal));
        closureNode.UseFreshObject = false;
        closureNode.FreshPath.Clear();
        CallbackCapturedTarget freshTarget = CreateCallbackCapturedTarget(0);
        CapturedFreshCallbackRoot fresh = new CapturedFreshCallbackRoot {
            Entities = CreateSourceEntityListOwnerRoot(freshTarget).Entities,
            Callback = CapturedFreshCallbackFactory.Create(freshTarget)
        };
        FieldInfo capturedTargetField = fresh.Callback.Target!.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(field => field.FieldType == typeof(CallbackCapturedTarget));
        capturedTargetField.SetValue(fresh.Callback.Target, null);

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        fresh.Callback();
        Assert.Equal(38, freshTarget.Value);
    }

    [Fact]
    public void CompilerClosureOwnedByAnIteratorRequiresItsGeneratedLocalField() {
        Type closureType = typeof(SwitchGate)
            .GetNestedTypes(BindingFlags.NonPublic)
            .Single(type => type.Name.Contains("DisplayClass16_0", StringComparison.Ordinal));

        Assert.True(AkronReconstructionGraph.IsCompilerClosureIteratorLocal(
            closureType, "<>8__1"));
        Assert.False(AkronReconstructionGraph.IsCompilerClosureIteratorLocal(
            closureType, "unrelated"));
        Assert.False(AkronReconstructionGraph.IsCompilerClosureIteratorLocal(
            typeof(TestNode), "<>8__1"));
    }

    [Fact]
    public void ReconstructedCallbackUsesAFreshSavedAliasWhenItsFirstOwnerIsMissing() {
        TestNode savedTarget = new TestNode { Value = 37 };
        Action savedCallback = savedTarget.Increment;
        CallbackAliasFirstRoot saved = new CallbackAliasFirstRoot {
            Holder = new PassiveCallbackHolder { Callback = savedCallback },
            Callback = savedCallback
        };
        TestNode baselineTarget = new TestNode();
        Action baselineCallback = baselineTarget.Increment;
        CallbackAliasFirstRoot baseline = new CallbackAliasFirstRoot {
            Holder = new PassiveCallbackHolder { Callback = baselineCallback },
            Callback = baselineCallback
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode delegateNode = capture.Document.Nodes.Single(node => node.Kind == "delegate");
        AkronReconstructionNode targetNode = capture.Document.Nodes.Single(node =>
            node.ParentNodeId == delegateNode.Id && node.ParentKind == "delegate");
        targetNode.UseFreshObject = false;
        targetNode.FreshPath.Clear();
        TestNode freshTarget = new TestNode();
        CallbackAliasFirstRoot fresh = new CallbackAliasFirstRoot { Callback = freshTarget.Increment };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Same(fresh.Callback, fresh.Holder.Callback);
        fresh.Callback();
        Assert.Equal(38, Assert.IsType<TestNode>(fresh.Callback.Target).Value);
    }

    [Fact]
    public void ReconstructedCallbackAllowsFreshArrayOrderChanges() {
        TestNode savedTarget = new TestNode { Value = 37 };
        TestNode baselineTarget = new TestNode();
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new TestRoot { Callbacks = new Action[] { savedTarget.Increment } },
            new TestRoot { Callbacks = new Action[] { null!, baselineTarget.Increment } });
        Assert.True(capture.Success, capture.Error);
        TestNode freshTarget = new TestNode();
        TestRoot fresh = new TestRoot { Callbacks = new Action[] { null!, freshTarget.Increment } };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Action restoredCallback = Assert.Single(fresh.Callbacks);
        restoredCallback();
        Assert.Equal(38, Assert.IsType<TestNode>(restoredCallback.Target).Value);
    }

    [Fact]
    public void ReconstructedCallbackAuthenticatesNestedTargetCallbacks() {
        TestNode savedInnerTarget = new TestNode { Value = 37 };
        TestNode savedOuterTarget = new TestNode { OnUpdate = savedInnerTarget.Increment };
        TestNode baselineInnerTarget = new TestNode();
        TestNode baselineOuterTarget = new TestNode { OnUpdate = baselineInnerTarget.Increment };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new TestRoot { Callback = savedOuterTarget.Reset },
            new TestRoot { Callback = baselineOuterTarget.Reset });
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode outerDelegate = capture.Document.Nodes.Single(node =>
            node.Kind == "delegate" && node.ParentFieldName == nameof(TestRoot.Callback));
        AkronReconstructionNode outerTarget = capture.Document.Nodes.Single(node =>
            node.ParentNodeId == outerDelegate.Id && node.ParentKind == "delegate");
        outerTarget.UseFreshObject = false;
        outerTarget.FreshPath.Clear();
        TestNode freshInnerTarget = new TestNode();
        TestNode freshOuterTarget = new TestNode { OnUpdate = freshInnerTarget.Increment };
        TestRoot fresh = new TestRoot { Callback = freshOuterTarget.Reset };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        TestNode restoredOuterTarget = Assert.IsType<TestNode>(fresh.Callback.Target);
        restoredOuterTarget.OnUpdate();
        Assert.Equal(38, Assert.IsType<TestNode>(restoredOuterTarget.OnUpdate.Target).Value);
    }

    // A v8 document has the same shape as a v9 one and none of its evidence: no node
    // in it says whether a resource key names its resource or whether the map placed
    // an entity. Reading one would give two documents claiming one format two
    // different guarantees, with nothing on screen to say which you got, so the
    // format moved instead.
    [Fact]
    public void ASnapshotFromBeforeTheIdentityEvidenceIsRefusedRatherThanReadWithoutIt() {
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new TestRoot { Counter = 7 },
            new TestRoot());
        Assert.True(capture.Success, capture.Error);
        string json = graph.Serialize(capture.Document).Replace(
            AkronReconstructionDocument.CurrentFormat,
            "akron-reconstruction-v8",
            StringComparison.Ordinal);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            graph.Deserialize(json));

        Assert.StartsWith(
            "Reconstruction document format is unsupported: set this StartPos again.",
            exception.Message);
        Assert.Contains("akron-reconstruction-v8", exception.Message);
        Assert.Contains("akron-reconstruction-v10", exception.Message);
    }

    [Fact]
    public void DeserializeRejectsTooManyJsonContainersWhileStreaming() {
        AkronReconstructionGraph graph = new AkronReconstructionGraph(
            IsLiveResource,
            maxJsonTokenCount: 100,
            maxJsonContainerCount: 1,
            maxJsonStringChars: 100,
            maxJsonBinaryBytes: 100);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            graph.Deserialize("{\"Format\":\"akron-reconstruction-v10\",\"Nodes\":[]}"));

        Assert.Contains("container count exceeds", exception.Message);
    }

    [Fact]
    public void DeserializeRejectsAnOversizedJsonStringWhileStreaming() {
        AkronReconstructionGraph graph = new AkronReconstructionGraph(
            IsLiveResource,
            maxJsonTokenCount: 100,
            maxJsonContainerCount: 100,
            maxJsonStringChars: 4,
            maxJsonBinaryBytes: 100);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            graph.Deserialize("{\"Format\":\"12345\"}"));

        Assert.Contains("string length exceeds", exception.Message);
    }

    [Fact]
    public void RestoreRejectsArrayItemCountBeforeEnumeratingItsIndices() {
        TestRoot saved = new TestRoot { Numbers = new[] { 1 } };
        TestRoot baseline = new TestRoot { Numbers = new[] { 0 } };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode arrayNode = Assert.Single(
            capture.Document.Nodes,
            node => node.ParentFieldName == nameof(TestRoot.Numbers));
        arrayNode.UseFreshObject = false;
        arrayNode.PackedPrimitiveArrayBytes = null;
        arrayNode.ArrayLengths[0] = 1_000_000;
        arrayNode.Items.Clear();

        AkronReconstructionRestore restore = graph.Restore(capture.Document, new TestRoot());

        Assert.False(restore.Success);
        Assert.Contains("array item count differs", restore.Error);
    }

    [Fact]
    public void RestoreRejectsAnInvalidPackedPrimitiveMarkerBeforeAllocatingItsArray() {
        TestRoot saved = new TestRoot { Numbers = new[] { 1 } };
        TestRoot baseline = new TestRoot { Numbers = new[] { 0 } };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode arrayNode = Assert.Single(
            capture.Document.Nodes,
            node => node.ParentFieldName == nameof(TestRoot.Numbers));
        arrayNode.UseFreshObject = false;
        arrayNode.TypeName = typeof(object[]).AssemblyQualifiedName!;
        arrayNode.ArrayLengths[0] = 10_000_000;
        arrayNode.PackedPrimitiveArrayBytes = new byte[1];

        long beforeRestore = GC.GetAllocatedBytesForCurrentThread();
        AkronReconstructionRestore restore = graph.Restore(capture.Document, new TestRoot());
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeRestore;

        Assert.False(restore.Success);
        Assert.Contains("packed primitive array", restore.Error);
        Assert.True(allocatedBytes < 1_000_000, "Invalid packed input allocated " + allocatedBytes + " bytes.");
    }

    [Fact]
    public void SparseArrayRestoreDoesNotAllocateReferencePathsForEmptyItems() {
        int[] lengths = { 1_000, 100 };
        int[] lowerBounds = { -2, 5 };
        int[] finalIndices = { 997, 104 };
        Array savedItems = Array.CreateInstance(typeof(object), lengths, lowerBounds);
        savedItems.SetValue(new TestNode { Name = "saved", Value = 37 }, finalIndices);
        SparseArrayRoot saved = new SparseArrayRoot { Items = savedItems };
        Array baselineItems = Array.CreateInstance(typeof(object), lengths, lowerBounds);
        baselineItems.SetValue(new TestNode { Name = "baseline" }, finalIndices);
        SparseArrayRoot baseline = new SparseArrayRoot { Items = baselineItems };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        Array freshItems = Array.CreateInstance(typeof(object), lengths, lowerBounds);
        freshItems.SetValue(new TestNode { Name = "fresh" }, finalIndices);
        SparseArrayRoot fresh = new SparseArrayRoot { Items = freshItems };

        long beforeRestore = GC.GetAllocatedBytesForCurrentThread();
        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeRestore;

        Assert.True(restore.Success, restore.Error);
        Assert.True(allocatedBytes < 50_000_000, "Sparse restore allocated " + allocatedBytes + " bytes.");
        TestNode restoredNode = Assert.IsType<TestNode>(fresh.Items.GetValue(finalIndices));
        Assert.Equal(37, restoredNode.Value);
    }

    [Fact]
    public void PrimitiveArrayRestoreDoesNotIndexEveryScalarElementPath() {
        const int ItemCount = 100_000;
        PrimitiveArrayRoot saved = new PrimitiveArrayRoot {
            Integers = new int[1, ItemCount],
            Booleans = Array.Empty<bool>()
        };
        saved.Integers[0, ItemCount - 1] = 37;
        PrimitiveArrayRoot baseline = new PrimitiveArrayRoot {
            Integers = new int[1, ItemCount],
            Booleans = Array.Empty<bool>()
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        PrimitiveArrayRoot fresh = new PrimitiveArrayRoot {
            Integers = new int[1, ItemCount],
            Booleans = Array.Empty<bool>()
        };

        long beforeRestore = GC.GetAllocatedBytesForCurrentThread();
        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeRestore;

        Assert.True(restore.Success, restore.Error);
        Assert.Equal(37, fresh.Integers[0, ItemCount - 1]);
        Assert.True(allocatedBytes < 25_000_000, "Primitive array restore allocated " + allocatedBytes + " bytes.");
    }

    [Fact]
    public void SharedSafeContainerStillIndexesChildrenAtTheSavedOwnerPath() {
        object[] savedItems = { new TestNode { Name = "saved", Value = 37 } };
        object[] baselineItems = { new TestNode { Name = "baseline" } };
        SharedContainerRoot saved = new SharedContainerRoot { Expected = savedItems };
        SharedContainerRoot baseline = new SharedContainerRoot {
            Earlier = baselineItems,
            Expected = baselineItems
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        object[] freshItems = { new TestNode { Name = "fresh" } };
        SharedContainerRoot fresh = new SharedContainerRoot {
            Earlier = freshItems,
            Expected = freshItems
        };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Null(fresh.Earlier);
        TestNode restoredNode = Assert.IsType<TestNode>(fresh.Expected[0]);
        Assert.Equal(37, restoredNode.Value);
    }

    [Fact]
    public void ExactTypedArraySlotCanAuthenticateAMissingRuntimeObject() {
        ExactSlotRoot saved = new ExactSlotRoot {
            Items = new[] { new ExactSlotObject { Value = 37 } }
        };
        ExactSlotRoot baseline = new ExactSlotRoot {
            Items = new ExactSlotObject[] { new DerivedExactSlotObject() }
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        ExactSlotRoot fresh = new ExactSlotRoot {
            Items = new ExactSlotObject[] { new DerivedExactSlotObject() }
        };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Equal(37, Assert.IsType<ExactSlotObject>(fresh.Items[0]).Value);
    }

    [Fact]
    public void SerializedGraphUsesCompactParentLinksInsteadOfRepeatedFullPaths() {
        ChainNode saved = BuildChain(200, valueOffset: 1000);
        ChainNode baseline = BuildChain(200, valueOffset: 0);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);

        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        string json = graph.Serialize(capture.Document);
        ChainNode fresh = BuildChain(200, valueOffset: -1000);
        AkronReconstructionDocument document = graph.Deserialize(json);
        AkronReconstructionRestore restore = graph.Restore(document, fresh);

        Assert.True(capture.Success, capture.Error);
        Assert.DoesNotContain("\"Path\":", json);
        Assert.True(json.Length < 250_000, "Compact 200-node graph was " + json.Length + " bytes.");
        Assert.True(restore.Success, restore.Error);
        Assert.Equal(1199, fresh.NextAt(199).Value);
        Assert.True(graph.Verify(document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void FrameworkValueStructsStayInlineAndRestoreExactBits() {
        float negativeZero = BitConverter.Int32BitsToSingle(unchecked((int) 0x80000000));
        FrameworkValueRoot saved = new FrameworkValueRoot {
            Vector2 = new Vector2(negativeZero, 37.25f),
            Vector3 = new Vector3(-91.5f, 0.125f, 2048.75f),
            Color = new Color(17, 83, 149, 211),
            Rectangle = new Rectangle(-37, 19, 320, 180),
            Vertex = new VertexPositionColor(new Vector3(3.5f, -7.25f, 11.75f), new Color(5, 10, 15, 20))
        };
        FrameworkValueRoot baseline = new FrameworkValueRoot();
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);

        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        string json = graph.Serialize(capture.Document);
        AkronReconstructionDocument document = graph.Deserialize(json);
        FrameworkValueRoot fresh = new FrameworkValueRoot();
        AkronReconstructionRestore restore = graph.Restore(document, fresh);

        Assert.Single(capture.Document.Nodes);
        Assert.True(restore.Success, restore.Error);
        Assert.Equal(BitConverter.SingleToInt32Bits(saved.Vector2.X), BitConverter.SingleToInt32Bits(fresh.Vector2.X));
        AssertFloatBits(saved.Vector2.Y, fresh.Vector2.Y);
        AssertFloatBits(saved.Vector3.X, fresh.Vector3.X);
        AssertFloatBits(saved.Vector3.Y, fresh.Vector3.Y);
        AssertFloatBits(saved.Vector3.Z, fresh.Vector3.Z);
        Assert.Equal(PackedColor(saved.Color), PackedColor(fresh.Color));
        Assert.Equal(saved.Rectangle.X, fresh.Rectangle.X);
        Assert.Equal(saved.Rectangle.Y, fresh.Rectangle.Y);
        Assert.Equal(saved.Rectangle.Width, fresh.Rectangle.Width);
        Assert.Equal(saved.Rectangle.Height, fresh.Rectangle.Height);
        AssertFloatBits(saved.Vertex.Position.X, fresh.Vertex.Position.X);
        AssertFloatBits(saved.Vertex.Position.Y, fresh.Vertex.Position.Y);
        AssertFloatBits(saved.Vertex.Position.Z, fresh.Vertex.Position.Z);
        Assert.Equal(PackedColor(saved.Vertex.Color), PackedColor(fresh.Vertex.Color));
        Assert.True(graph.Verify(document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void PathfinderCacheUsesTheFreshRoomInstanceWithoutSerializingItsTiles() {
        PathfinderRoot saved = new PathfinderRoot {
            Pathfinder = (Pathfinder) RuntimeHelpers.GetUninitializedObject(typeof(Pathfinder))
        };
        PathfinderRoot baseline = new PathfinderRoot {
            Pathfinder = (Pathfinder) RuntimeHelpers.GetUninitializedObject(typeof(Pathfinder))
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(AkronStartPosReconstruction.IsLiveResourceType);

        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionDocument document = graph.Deserialize(graph.Serialize(capture.Document));
        Pathfinder freshPathfinder = (Pathfinder) RuntimeHelpers.GetUninitializedObject(typeof(Pathfinder));
        PathfinderRoot fresh = new PathfinderRoot { Pathfinder = freshPathfinder };
        AkronReconstructionRestore restore = graph.Restore(document, fresh);

        Assert.Equal(2, capture.Document.Nodes.Count);
        Assert.Contains(capture.Document.Nodes, node => node.Kind == "anchor" && node.TypeName.Contains("Celeste.Pathfinder", StringComparison.Ordinal));
        Assert.True(restore.Success, restore.Error);
        Assert.Same(freshPathfinder, fresh.Pathfinder);
        Assert.True(graph.Verify(document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void PrimitiveArraysUseOneExactBinaryBlockInsteadOfPerItemJsonObjects() {
        PrimitiveArrayRoot saved = new PrimitiveArrayRoot {
            Integers = new[,] { { int.MinValue, -37, 0 }, { 19, 320, int.MaxValue } },
            Booleans = Enumerable.Range(0, 5000).Select(index => index % 3 == 0).ToArray()
        };
        PrimitiveArrayRoot baseline = new PrimitiveArrayRoot {
            Integers = new int[2, 3],
            Booleans = new bool[5000]
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);

        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        string json = graph.Serialize(capture.Document);
        AkronReconstructionDocument document = graph.Deserialize(json);
        PrimitiveArrayRoot fresh = new PrimitiveArrayRoot {
            Integers = new int[2, 3],
            Booleans = new bool[5000]
        };
        AkronReconstructionRestore restore = graph.Restore(document, fresh);

        Assert.True(json.Length < 20_000, "Packed primitive arrays were " + json.Length + " bytes.");
        Assert.All(capture.Document.Nodes.Where(node => node.Kind == "array"), node => {
            Assert.NotNull(node.PackedPrimitiveArrayBytes);
            Assert.Empty(node.Items);
        });
        Assert.True(restore.Success, restore.Error);
        Assert.Equal(saved.Integers.Cast<int>(), fresh.Integers.Cast<int>());
        Assert.Equal(saved.Booleans, fresh.Booleans);
        Assert.True(graph.Verify(document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void NullAndReferenceValuesOmitEmptyScalarMetadata() {
        TestRoot saved = new TestRoot {
            Primary = new TestNode(),
            Secondary = null!
        };
        TestRoot baseline = new TestRoot {
            Primary = new TestNode(),
            Secondary = null!
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);

        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        string json = graph.Serialize(capture.Document);
        AkronReconstructionDocument document = graph.Deserialize(json);
        TestRoot fresh = new TestRoot { Primary = new TestNode() };
        AkronReconstructionRestore restore = graph.Restore(document, fresh);

        JObject serializedDocument = JObject.Parse(json);
        IEnumerable<JObject> emptyMetadataValues = serializedDocument
            .Descendants()
            .OfType<JObject>()
            .Where(value => value["k"]?.Value<string>() is
                AkronReconstructionGraph.NullValueKind or AkronReconstructionGraph.ReferenceValueKind);
        Assert.NotEmpty(emptyMetadataValues);
        Assert.All(emptyMetadataValues, value => {
            Assert.Null(value.Property("t"));
            Assert.Null(value.Property("s"));
        });
        Assert.True(restore.Success, restore.Error);
        Assert.NotNull(fresh.Primary);
        Assert.Null(fresh.Secondary);
        Assert.True(graph.Verify(document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void PersistentEventVerificationAllowsOnlySmallFmodReadbackDrift() {
        AkronPersistentEventInstanceState expected = new AkronPersistentEventInstanceState {
            Path = "event:/test",
            Volume = 0.5f,
            Pitch = 1.25f,
            HasListenerMask = true,
            ListenerMask = 3,
            Has3DAttributes = true,
            PositionX = 12f,
            TimelinePosition = 100,
            Parameters = new Dictionary<string, float> { ["mode"] = 0.75f }
        };
        AkronPersistentEventInstanceState actual = new AkronPersistentEventInstanceState {
            Path = "event:/test",
            Volume = 0.500001f,
            Pitch = 1.250001f,
            HasListenerMask = true,
            ListenerMask = 3,
            Has3DAttributes = true,
            PositionX = 12.000001f,
            TimelinePosition = 101,
            Parameters = new Dictionary<string, float> { ["mode"] = 0.750001f }
        };

        Assert.True(AkronReconstructionGraph.PersistentEventStatesMatch(expected, actual));

        actual.Volume = 0.51f;
        Assert.False(AkronReconstructionGraph.PersistentEventStatesMatch(expected, actual));
        actual.Volume = expected.Volume;
        actual.HasListenerMask = false;
        Assert.False(AkronReconstructionGraph.PersistentEventStatesMatch(expected, actual));
    }

    [Fact]
    public void ReapplyRestoresSavedFieldsAfterARegisteredActionMutatesTheRoom() {
        TestRoot saved = new TestRoot {
            Counter = 91,
            Primary = new TestNode { Name = "saved", Value = 37 }
        };
        TestRoot baseline = new TestRoot {
            Primary = new TestNode { Name = "fresh" }
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        TestRoot fresh = new TestRoot {
            Primary = new TestNode { Name = "fresh" }
        };
        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        fresh.Counter = -1;
        fresh.Primary.Value = -1;
        AkronReconstructionVerification reapply = graph.Reapply(capture.Document, restore);

        Assert.True(reapply.Success, reapply.Error);
        Assert.Equal(91, fresh.Counter);
        Assert.Equal(37, fresh.Primary.Value);
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void ReconstructionUsesTheFreshResourceAtTheSavedStructuralPath() {
        TestResource savedResource = new TestResource("saved-process");
        TestRoot saved = new TestRoot {
            Resource = savedResource,
            Primary = new TestNode { Resource = savedResource }
        };
        TestResource baselineResource = new TestResource("capture-baseline");
        TestRoot baseline = new TestRoot {
            Resource = baselineResource,
            Primary = new TestNode { Resource = baselineResource }
        };

        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        TestResource freshResource = new TestResource("restored-process");
        TestRoot fresh = new TestRoot {
            Resource = freshResource,
            Primary = new TestNode { Resource = freshResource }
        };

        AkronReconstructionDocument document = graph.Deserialize(graph.Serialize(capture.Document));
        AkronReconstructionRestore restore = graph.Restore(document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Same(freshResource, fresh.Resource);
        Assert.Same(freshResource, fresh.Primary.Resource);
        Assert.Equal("restored-process", fresh.Resource.ProcessIdentity);
    }

    [Fact]
    public void RestoreSeparatesOrdinaryObjectsThatTheFreshRoomAliases() {
        TestRoot saved = new TestRoot {
            Primary = new TestNode { Value = 37 },
            Secondary = new TestNode { Value = 91 }
        };
        TestRoot baseline = new TestRoot {
            Primary = new TestNode(),
            Secondary = new TestNode()
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        TestNode aliasedFreshNode = new TestNode();
        TestRoot fresh = new TestRoot {
            Primary = aliasedFreshNode,
            Secondary = aliasedFreshNode
        };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.NotSame(fresh.Primary, fresh.Secondary);
        Assert.Equal(37, fresh.Primary.Value);
        Assert.Equal(91, fresh.Secondary.Value);
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    // The same separation one level in, where the aliased slot belongs to an entity and
    // the edge that takes it is proved by the owned-nested-state licence rather than by
    // the fresh room's occupancy of that slot.
    //
    // The entity keeps the state it built and the state it ran last. A clean load builds
    // Running and has not built Pending, so Last still holds the Running one; the saved
    // frame had built Pending and run it, so its Last holds that. Restoring separates
    // them: Running keeps the live object, and Pending and Last both take a rebuilt one.
    //
    // The Last edge is the interesting one. It displaces the live state object, which the
    // document keeps at Running, and the only thing that proves it is freshOwnedNestedState -
    // the state's type is declared inside its owner and its canonical slot is a field of
    // that same fresh entity. It is not proved by the fresh room holding an object of that
    // type in the slot the edge writes, because the node this edge carries lives at Pending,
    // which a clean load leaves empty.
    //
    // That last fact is what the two document assertions below pin. Without them the room
    // still loads if the fields are reordered, and reordering changes which proof the edge
    // has: with Last declared before Pending the Last edge becomes that node's own slot and
    // savedOwnerEdge with exactParentSlot proves it instead, so the room would stop being
    // about the licence this test is named for. Declaring Pending first fails outright:
    // the alias reservation hands the live object to that node instead, and its own slot
    // is then the empty one, so the room is refused at $.Owner.Pending for a reference
    // edge that is not authentic to the fresh room.
    [Fact]
    public void AnEntityKeepsTheStateItRanLastWhenTheDocumentSeparatesTwoOfThem() {
        OwnedStateRoot saved = new OwnedStateRoot {
            Owner = CreateOwnedStateEntity(runningValue: 37, pendingValue: 91)
        };
        OwnedStateRoot baseline = new OwnedStateRoot {
            Owner = CreateOwnedStateEntity(runningValue: 0, pendingValue: null)
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        // The node the Last edge carries is the one whose own slot is Pending, so the edge
        // is not that node's own document path.
        AkronReconstructionNode rootNode = capture.Document.Nodes
            .Single(node => node.Id == capture.Document.RootNodeId);
        AkronReconstructionValue ownerValue = rootNode.Fields
            .Single(field => field.Name == nameof(OwnedStateRoot.Owner))
            .Value;
        AkronReconstructionValue lastValue = capture.Document.Nodes
            .Single(node => node.Id == ownerValue.NodeId)
            .Fields
            .Single(field => field.Name == nameof(OwnedStateEntity.Last))
            .Value;
        AkronReconstructionNode lastTarget = capture.Document.Nodes
            .Single(node => node.Id == lastValue.NodeId);
        Assert.Equal(nameof(OwnedStateEntity.Pending), lastTarget.ParentFieldName);
        OwnedStateEntity freshOwner = CreateOwnedStateEntity(runningValue: 0, pendingValue: null);
        OwnedStateEntity.OwnedState liveState = freshOwner.Running;
        // And a clean load leaves that slot empty, so the fresh room says nothing about the
        // type the edge writes into Last.
        Assert.Null(freshOwner.Pending);

        AkronReconstructionRestore restore = graph.Restore(
            capture.Document,
            new OwnedStateRoot { Owner = freshOwner });

        Assert.True(restore.Success, restore.Error);
        // The slot the reload did build keeps its own object, with the saved value on it.
        Assert.Same(liveState, freshOwner.Running);
        Assert.Equal(37, freshOwner.Running.Value);
        // The one it had not built is rebuilt, and the slot that aliased the live object
        // follows the document onto the rebuilt one.
        OwnedStateEntity.OwnedState rebuilt =
            Assert.IsType<OwnedStateEntity.OwnedState>(freshOwner.Pending);
        Assert.NotSame(liveState, rebuilt);
        Assert.Equal(91, rebuilt.Value);
        Assert.Same(rebuilt, freshOwner.Last);
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void MissingFreshResourceFailsAtItsExactPathBeforeChangingTheRoom() {
        TestResource savedResource = new TestResource("saved-process");
        TestRoot saved = new TestRoot {
            Counter = 91,
            Primary = new TestNode { Value = 37 },
            Resource = savedResource
        };
        TestResource baselineResource = new TestResource("capture-baseline");
        TestRoot baseline = new TestRoot {
            Primary = new TestNode(),
            Resource = baselineResource
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        TestRoot fresh = new TestRoot {
            Counter = 12,
            Primary = new TestNode { Value = 5 }
        };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.False(restore.Success);
        Assert.Equal("$.Resource", restore.ErrorPath);
        Assert.Equal(12, fresh.Counter);
        Assert.Equal(5, fresh.Primary.Value);
        Assert.Null(fresh.Resource);
    }

    [Fact]
    public void PersistedResourceRecreatesAMissingFreshResourceFromItsSavedPayload() {
        TestRoot saved = new TestRoot {
            Resource = new TestResource("saved-content", "trail-buffer")
        };
        TestRoot baseline = new TestRoot();
        AkronReconstructionGraph graph = new AkronReconstructionGraph(
            IsLiveResource,
            resource => ((TestResource) resource).StableKey,
            new TestResourceAdapter());

        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionDocument document = graph.Deserialize(graph.Serialize(capture.Document));
        TestRoot fresh = new TestRoot();
        AkronReconstructionRestore restore = graph.Restore(document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.NotNull(fresh.Resource);
        Assert.Equal("saved-content", fresh.Resource.ProcessIdentity);
        Assert.Equal("trail-buffer", fresh.Resource.StableKey);
        Assert.True(graph.Verify(document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void ResourceKeyFindsAFreshResourceAfterItsStructuralSlotChanges() {
        TestResource savedResource = new TestResource("saved-process", "shared-texture");
        TestRoot saved = new TestRoot { Resource = savedResource };
        TestResource baselineResource = new TestResource("baseline-process", "shared-texture");
        TestRoot baseline = new TestRoot {
            Resource = new TestResource("wrong-baseline", "other-texture"),
            Secondary = new TestNode { Resource = baselineResource }
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(
            IsLiveResource,
            resource => ((TestResource) resource).StableKey);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        TestResource freshResource = new TestResource("fresh-process", "shared-texture");
        TestRoot fresh = new TestRoot {
            Resource = new TestResource("wrong-fresh", "other-texture"),
            Primary = new TestNode { Resource = freshResource },
            Secondary = new TestNode { Resource = new TestResource("other-fresh", "other-secondary-texture") }
        };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Same(freshResource, fresh.Resource);
        Assert.Equal("fresh-process", fresh.Resource.ProcessIdentity);
    }

    [Fact]
    public void EquivalentStableKeysReuseAvailableFreshResources() {
        DuplicateResourceRoot saved = new DuplicateResourceRoot {
            TargetA = new TestResource("saved-a", "shared-texture"),
            TargetB = new TestResource("saved-b", "shared-texture"),
            TargetC = new TestResource("saved-c", "shared-texture")
        };
        TestResource baselineA = new TestResource("baseline-a", "shared-texture");
        DuplicateResourceRoot baseline = new DuplicateResourceRoot {
            TargetA = baselineA,
            TargetB = new TestResource("baseline-b", "shared-texture"),
            TargetC = baselineA
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(
            IsLiveResource,
            resource => ((TestResource) resource).StableKey,
            areEquivalentLiveResources: type => type == typeof(TestResource));
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        TestResource freshA = new TestResource("fresh-a", "shared-texture");
        TestResource freshB = new TestResource("fresh-b", "shared-texture");
        DuplicateResourceRoot fresh = new DuplicateResourceRoot {
            TargetA = new TestResource("wrong-a", "other-a"),
            TargetB = new TestResource("wrong-b", "other-b"),
            TargetC = new TestResource("wrong-c", "other-c"),
            CandidateA = freshA,
            CandidateB = freshB
        };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Equal(new[] { "fresh-a", "fresh-b" }, new[] {
            fresh.TargetA.ProcessIdentity,
            fresh.TargetB.ProcessIdentity,
            fresh.TargetC.ProcessIdentity
        }.Distinct().OrderBy(value => value));
    }

    [Fact]
    public void DuplicateStableKeysAreRejectedWithoutAnEquivalenceContract() {
        DuplicateResourceRoot saved = new DuplicateResourceRoot {
            TargetA = new TestResource("saved-a", "shared-texture"),
            TargetB = new TestResource("saved-b", "shared-texture")
        };
        DuplicateResourceRoot baseline = new DuplicateResourceRoot {
            TargetA = new TestResource("baseline-a", "shared-texture"),
            TargetB = new TestResource("baseline-b", "shared-texture")
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(
            IsLiveResource,
            resource => ((TestResource) resource).StableKey);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        DuplicateResourceRoot fresh = new DuplicateResourceRoot {
            TargetA = new TestResource("wrong-a", "other-a"),
            TargetB = new TestResource("wrong-b", "other-b"),
            CandidateA = new TestResource("fresh-a", "shared-texture"),
            CandidateB = new TestResource("fresh-b", "shared-texture")
        };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.False(restore.Success);
        Assert.Contains("fresh resource", restore.Error);
    }

    [Fact]
    public void DetachedStableResourceWinsOverFreshStructuralCandidatesWithOtherKeys() {
        TestResource savedResource = new TestResource("saved", "target-texture");
        TestResourceListRoot saved = new TestResourceListRoot {
            Holders = new List<TestResourceHolder> {
                new TestResourceHolder { Resource = savedResource }
            }
        };
        TestResourceListRoot baseline = new TestResourceListRoot {
            Holders = new List<TestResourceHolder> {
                new TestResourceHolder { Resource = new TestResource("baseline", "target-texture") }
            }
        };
        TestResource detached = new TestResource("detached", "target-texture");
        AkronReconstructionGraph graph = new AkronReconstructionGraph(
            IsLiveResource,
            resource => ((TestResource) resource).StableKey,
            resolveDetachedLiveResource: (_, key) => key.EndsWith("|target-texture", StringComparison.Ordinal)
                ? detached
                : null,
            areEquivalentLiveResources: type => type == typeof(TestResource));
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        TestResourceListRoot fresh = new TestResourceListRoot {
            Holders = new List<TestResourceHolder> {
                new TestResourceHolder { Resource = new TestResource("fresh-a", "other-a") },
                new TestResourceHolder { Resource = new TestResource("fresh-b", "other-b") }
            }
        };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Same(detached, fresh.Holders[0].Resource);
    }

    [Fact]
    public void DetachedStableResourceLookupIsCachedWithinARestore() {
        DuplicateResourceRoot saved = new DuplicateResourceRoot {
            TargetA = new TestResource("saved-a", "target-texture"),
            TargetB = new TestResource("saved-b", "target-texture")
        };
        DuplicateResourceRoot baseline = new DuplicateResourceRoot {
            TargetA = new TestResource("baseline-a", "target-texture"),
            TargetB = new TestResource("baseline-b", "target-texture")
        };
        TestResource detached = new TestResource("detached", "target-texture");
        int lookupCount = 0;
        AkronReconstructionGraph graph = new AkronReconstructionGraph(
            IsLiveResource,
            resource => ((TestResource) resource).StableKey,
            resolveDetachedLiveResource: (_, _) => {
                lookupCount++;
                return detached;
            },
            areEquivalentLiveResources: type => type == typeof(TestResource));
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        DuplicateResourceRoot fresh = new DuplicateResourceRoot {
            TargetA = new TestResource("fresh-a", "other-a"),
            TargetB = new TestResource("fresh-b", "other-b")
        };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Same(detached, fresh.TargetA);
        Assert.Same(detached, fresh.TargetB);
        Assert.Equal(1, lookupCount);
    }

    // The DustEdges shape: a resource its owner creates on first render is live at
    // capture (the running level made it), null in both fresh baselines (never
    // rendered), and gone from the process registry at restore (exiting the map
    // disposed it). The recreate delegate is the only thing that can answer, and
    // its answer must pass the same key comparison the detached lookup uses.
    [Fact]
    public void ARecreatedResourceAnswersALabelledKeyThatResolvesNowhere() {
        TestResource liveAtCapture = new TestResource("live", "runtime-noise");
        TestResource recreated = new TestResource("recreated", "runtime-noise");
        bool captureDone = false;
        AkronReconstructionGraph graph = new AkronReconstructionGraph(
            IsLiveResource,
            resource => ((TestResource) resource).StableKey,
            resolveDetachedLiveResource: (_, _) => captureDone ? null : liveAtCapture,
            recreateDetachedLiveResource: (_, key) => key.EndsWith("|runtime-noise", StringComparison.Ordinal)
                ? recreated
                : null);
        DuplicateResourceRoot saved = new DuplicateResourceRoot { TargetA = new TestResource("saved", "runtime-noise") };
        AkronReconstructionCapture capture = graph.Capture(saved, new DuplicateResourceRoot());
        Assert.True(capture.Success, capture.Error);
        captureDone = true;
        DuplicateResourceRoot fresh = new DuplicateResourceRoot();

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Same(recreated, fresh.TargetA);
    }

    [Fact]
    public void ARecreatedResourceWithTheWrongKeyIsRefused() {
        TestResource liveAtCapture = new TestResource("live", "runtime-noise");
        bool captureDone = false;
        AkronReconstructionGraph graph = new AkronReconstructionGraph(
            IsLiveResource,
            resource => ((TestResource) resource).StableKey,
            resolveDetachedLiveResource: (_, _) => captureDone ? null : liveAtCapture,
            recreateDetachedLiveResource: (_, _) => new TestResource("recreated", "some-other-key"));
        DuplicateResourceRoot saved = new DuplicateResourceRoot { TargetA = new TestResource("saved", "runtime-noise") };
        AkronReconstructionCapture capture = graph.Capture(saved, new DuplicateResourceRoot());
        Assert.True(capture.Success, capture.Error);
        captureDone = true;

        AkronReconstructionRestore restore = graph.Restore(capture.Document, new DuplicateResourceRoot());

        Assert.False(restore.Success);
        Assert.Contains("fresh resource key and structural path are unavailable", restore.Error);
    }

    // A portable key is a name, and a name that resolves nowhere is a resource
    // this install does not have. Recreating one would hand the room a blank
    // where real content was expected, so the delegate is never even asked.
    [Fact]
    public void APortableKeyIsNeverRecreated() {
        TestResource liveAtCapture = new TestResource("live", "named-content");
        bool captureDone = false;
        int recreateCalls = 0;
        AkronReconstructionGraph graph = new AkronReconstructionGraph(
            IsLiveResource,
            resource => ((TestResource) resource).StableKey,
            resolveDetachedLiveResource: (_, _) => captureDone ? null : liveAtCapture,
            hasPortableLiveResourceKey: _ => true,
            recreateDetachedLiveResource: (_, _) => {
                recreateCalls++;
                return new TestResource("recreated", "named-content");
            });
        DuplicateResourceRoot saved = new DuplicateResourceRoot { TargetA = new TestResource("saved", "named-content") };
        AkronReconstructionCapture capture = graph.Capture(saved, new DuplicateResourceRoot());
        Assert.True(capture.Success, capture.Error);
        captureDone = true;

        AkronReconstructionRestore restore = graph.Restore(capture.Document, new DuplicateResourceRoot());

        Assert.False(restore.Success);
        Assert.Contains("fresh resource key and structural path are unavailable", restore.Error);
        Assert.Equal(0, recreateCalls);
    }

    // The refusal arms of the game-side recreate delegate. The creating arm needs
    // a graphics device, so it is exercised in game rather than here.
    [Fact]
    public void RecreateDetachedLiveResourceRefusesEverythingButAListedRuntimeTexture() {
        Assert.Null(AkronStartPosReconstruction.RecreateDetachedLiveResource(
            typeof(VirtualRenderTarget), "t|dust-noise-a|128x72"));
        // Only the listed names are recreatable. A file-backed texture's key is
        // its content path, and a bare path is indistinguishable from a made-up
        // name, so anything unlisted keeps the refusal.
        Assert.Null(AkronStartPosReconstruction.RecreateDetachedLiveResource(
            typeof(VirtualTexture), "t|Graphics/Atlases/Gameplay|128x72"));
        Assert.Null(AkronStartPosReconstruction.RecreateDetachedLiveResource(
            typeof(VirtualTexture), "t|icon.png|128x72"));
        Assert.Null(AkronStartPosReconstruction.RecreateDetachedLiveResource(
            typeof(VirtualTexture), "t|dust-noise-c|128x72"));
        // Dimensions must be exactly what the creator hardcodes, so a doctored
        // key cannot mint distinct allocations out of made-up sizes.
        Assert.Null(AkronStartPosReconstruction.RecreateDetachedLiveResource(
            typeof(VirtualTexture), "t|dust-noise-a|0x72"));
        Assert.Null(AkronStartPosReconstruction.RecreateDetachedLiveResource(
            typeof(VirtualTexture), "t|dust-noise-a|128x-72"));
        Assert.Null(AkronStartPosReconstruction.RecreateDetachedLiveResource(
            typeof(VirtualTexture), "t|dust-noise-a|4096x4096"));
        Assert.Null(AkronStartPosReconstruction.RecreateDetachedLiveResource(
            typeof(VirtualTexture), "t|dust-noise-a|127x72"));
        Assert.Null(AkronStartPosReconstruction.RecreateDetachedLiveResource(
            typeof(VirtualTexture), "t|dust-noise-a|axb"));
        // A key with no name or no dimensions segment never parses.
        Assert.Null(AkronStartPosReconstruction.RecreateDetachedLiveResource(
            typeof(VirtualTexture), "t|128x72"));
        Assert.Null(AkronStartPosReconstruction.RecreateDetachedLiveResource(
            typeof(VirtualTexture), "no-separator"));
    }

    [Fact]
    public void StructuralOwnerPathFindsAFreshResourceWhenItsRuntimeNameAndListIndexChange() {
        TestResource savedResource = new TestResource("saved-process", "snapshot-0");
        TestResourceListRoot saved = new TestResourceListRoot {
            Holders = new List<TestResourceHolder> {
                new TestResourceHolder { Resource = savedResource }
            }
        };
        TestResourceListRoot baseline = new TestResourceListRoot {
            Holders = new List<TestResourceHolder> {
                new TestResourceHolder(),
                new TestResourceHolder { Resource = new TestResource("baseline-process", "snapshot-0") }
            }
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(
            IsLiveResource,
            resource => ((TestResource) resource).StableKey);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        TestResource freshResource = new TestResource("fresh-process", "snapshot-1");
        TestResourceListRoot fresh = new TestResourceListRoot {
            Holders = new List<TestResourceHolder> {
                new TestResourceHolder { Resource = freshResource },
                new TestResourceHolder()
            }
        };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(capture.Success, capture.Error);
        Assert.True(restore.Success, restore.Error);
        Assert.Same(freshResource, fresh.Holders[0].Resource);
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void RestoreRejectsAResourceWhoseStableIdentityChangedAtTheSamePath() {
        TestRoot saved = new TestRoot {
            Resource = new TestResource("saved-process", "texture-a")
        };
        TestRoot baseline = new TestRoot {
            Resource = new TestResource("baseline-process", "texture-a")
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(
            IsLiveResource,
            resource => ((TestResource) resource).StableKey);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        TestRoot fresh = new TestRoot {
            Counter = 12,
            Resource = new TestResource("fresh-process", "texture-b")
        };

        AkronReconstructionDocument document = graph.Deserialize(graph.Serialize(capture.Document));
        AkronReconstructionRestore restore = graph.Restore(document, fresh);

        Assert.False(restore.Success);
        Assert.Equal("$.Resource", restore.ErrorPath);
        Assert.Equal(12, fresh.Counter);
    }

    [Fact]
    public void RestoreRejectsAnOrdinaryObjectRelabeledAsAnAnchor() {
        TestRoot saved = new TestRoot { Primary = new TestNode { Value = 37 } };
        TestRoot baseline = new TestRoot { Primary = new TestNode() };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode primaryNode = capture.Document.Nodes.Single(node =>
            node.ParentFieldName == nameof(TestRoot.Primary));
        primaryNode.Kind = "anchor";
        primaryNode.UseFreshObject = true;
        primaryNode.ResourceKey = string.Empty;

        AkronReconstructionRestore restore = graph.Restore(
            capture.Document,
            new TestRoot { Primary = new TestNode() });

        Assert.False(restore.Success);
        Assert.Contains("anchor type is invalid", restore.Error);
    }

    // A live anchor is the fresh room's own object, so assignment skips the node
    // whole and never writes a field of it. Reachability walks those fields
    // anyway, so this document passed as complete. Measured before the refusal
    // existed: the restore and Verify both reported success, the room's Numbers
    // slot was left null, and the saved int[] { 4, 5, 6 } was built, filled and
    // attached to nothing. The crafted field also enters savedFieldAliases, which
    // is read whatever the parent's kind is, so the dead edge is evidence for
    // pairing decisions as well.
    //
    // The reference sits in a string field, and that is the point rather than an
    // accident. No slot the restore reads would take it - ValidateAssignable
    // refuses an int[] in a string field - and it got through because a slot that
    // is never read is never type-checked either.
    [Fact]
    public void RestoreRefusesAnObjectKeptOnlyByALiveAnchorsField() {
        TestRoot saved = new TestRoot {
            Resource = new TestResource("saved-process"),
            Numbers = new[] { 4, 5, 6 }
        };
        TestRoot baseline = new TestRoot {
            Resource = new TestResource("baseline-process"),
            Numbers = new[] { 0, 0, 0 }
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode anchorNode = capture.Document.Nodes.Single(node =>
            node.ParentFieldName == nameof(TestRoot.Resource));
        Assert.Equal("anchor", anchorNode.Kind);
        Assert.Empty(anchorNode.Fields);
        AkronReconstructionNode keptNode = capture.Document.Nodes.Single(node =>
            node.ParentFieldName == nameof(TestRoot.Numbers));

        ParkNodeInAField(
            capture.Document,
            keptNode,
            anchorNode,
            typeof(TestResource),
            "<StableKey>k__BackingField");
        TestRoot fresh = new TestRoot {
            Resource = new TestResource("fresh-process"),
            Numbers = new[] { 0, 0, 0 }
        };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.False(restore.Success);
        Assert.Contains("in a field, which the restore never reads", restore.Error);
        Assert.Equal(new[] { 0, 0, 0 }, fresh.Numbers);
    }

    // The sharper half of the same hole. The container here is one the restore
    // does read, but ValidateAssignments skips this one field by name, because a
    // BCL collection's version counter is derived bookkeeping that capture never
    // writes. A field skipped by name is never written and never type-checked, so
    // it parks an object exactly as a whole unread container does: measured before
    // the refusal existed, this document restored and verified while the room's
    // Numbers slot was left null.
    //
    // A scalar in that field stays allowed, which
    // CollectionVersionChangesDoNotInvalidateEquivalentContents pins.
    [Fact]
    public void RestoreRefusesAnObjectKeptOnlyByACollectionVersionField() {
        TestRoot saved = new TestRoot {
            Values = new Dictionary<string, int> { ["a"] = 1 },
            Numbers = new[] { 4, 5, 6 }
        };
        TestRoot baseline = new TestRoot {
            Values = new Dictionary<string, int> { ["a"] = 1 },
            Numbers = new[] { 0, 0, 0 }
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode valuesNode = capture.Document.Nodes.Single(node =>
            node.ParentFieldName == nameof(TestRoot.Values));
        Assert.DoesNotContain(valuesNode.Fields, field => field.Name == "_version");
        AkronReconstructionNode keptNode = capture.Document.Nodes.Single(node =>
            node.ParentFieldName == nameof(TestRoot.Numbers));

        ParkNodeInAField(
            capture.Document,
            keptNode,
            valuesNode,
            typeof(Dictionary<string, int>),
            "_version");
        TestRoot fresh = new TestRoot {
            Values = new Dictionary<string, int> { ["a"] = 1 },
            Numbers = new[] { 0, 0, 0 }
        };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.False(restore.Success);
        Assert.Contains("in a derived collection version field", restore.Error);
        Assert.Equal(new[] { 0, 0, 0 }, fresh.Numbers);
    }

    // An array is the one kind whose unread container depends on the node rather
    // than only on its kind: a packed primitive grid is restored from its bytes,
    // and ValidateArrayAssignments returns before it looks at an item. So does
    // ValidateReferenceAuthenticity, which skips a packed parent outright, while
    // IndexSavedArrayAliases does not - it reads every array node's items, so a
    // reference parked in one is also counted as evidence that the saved graph
    // held that object in a second place. Measured before the refusal existed:
    // restore and Verify both reported success with the room's Numbers slot null.
    [Fact]
    public void RestoreRefusesAnObjectKeptOnlyByAPackedPrimitiveArrayItem() {
        TestRoot saved = new TestRoot {
            Values = new Dictionary<string, int> { ["a"] = 1 },
            Numbers = new[] { 4, 5, 6 }
        };
        TestRoot baseline = new TestRoot {
            Values = new Dictionary<string, int> { ["a"] = 1 },
            Numbers = new[] { 0, 0, 0 }
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode valuesNode = capture.Document.Nodes.Single(node =>
            node.ParentFieldName == nameof(TestRoot.Values));
        // The dictionary's bucket grid: its only child the capture packs, because
        // its entry array holds string keys and so keeps one item per slot.
        AkronReconstructionNode packedNode = capture.Document.Nodes.Single(node =>
            node.ParentNodeId == valuesNode.Id && node.PackedPrimitiveArrayBytes != null);
        Assert.Empty(packedNode.Items);
        AkronReconstructionNode keptNode = capture.Document.Nodes.Single(node =>
            node.ParentFieldName == nameof(TestRoot.Numbers));

        RemoveOriginalOwningField(capture.Document, keptNode);
        packedNode.Items.Add(new AkronReconstructionValue {
            Kind = AkronReconstructionGraph.ReferenceValueKind,
            NodeId = keptNode.Id
        });
        keptNode.ParentNodeId = packedNode.Id;
        keptNode.ParentKind = "array";
        keptNode.ParentDeclaringTypeName = string.Empty;
        keptNode.ParentFieldName = string.Empty;
        keptNode.ParentArrayIndices = new List<int> { 0 };
        TestRoot fresh = new TestRoot {
            Values = new Dictionary<string, int> { ["a"] = 1 },
            Numbers = new[] { 0, 0, 0 }
        };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.False(restore.Success);
        Assert.Contains("in a packed primitive array item", restore.Error);
        Assert.Equal(new[] { 0, 0, 0 }, fresh.Numbers);
    }

    // Only a delegate node's calls are read, by CreateDelegate. A call list on any
    // other node is walked by reachability and by nothing else - not even
    // ValidateReferenceAuthenticity, which never looks at a call target - so this
    // is the plainest form of the hole: no alias is fabricated, the object is
    // simply built and left out of the room. Measured before the refusal existed:
    // restore and Verify both reported success with the room's Numbers slot null.
    //
    // The crafted call is deliberately a method call, the one kind whose target
    // CreateDelegate does bind, so the only thing that can refuse this document is
    // the parent's kind. A detour-next call would be refused by the second clause
    // instead and this room would stop being about the first.
    [Fact]
    public void RestoreRefusesAnObjectKeptOnlyByANonDelegateNodesCallTarget() {
        TestRoot saved = new TestRoot { Numbers = new[] { 4, 5, 6 } };
        TestRoot baseline = new TestRoot { Numbers = new[] { 0, 0, 0 } };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode rootNode = capture.Document.Nodes.Single(node =>
            node.Id == capture.Document.RootNodeId);
        Assert.Equal("object", rootNode.Kind);
        AkronReconstructionNode keptNode = capture.Document.Nodes.Single(node =>
            node.ParentFieldName == nameof(TestRoot.Numbers));

        RemoveOriginalOwningField(capture.Document, keptNode);
        rootNode.DelegateCalls.Add(new AkronReconstructionDelegateCall {
            Kind = "method",
            Target = new AkronReconstructionValue { Kind = AkronReconstructionGraph.ReferenceValueKind, NodeId = keptNode.Id },
            DeclaringTypeName = typeof(TestNode).AssemblyQualifiedName!,
            MethodName = nameof(TestNode.Increment),
            ReturnTypeName = typeof(void).AssemblyQualifiedName!
        });
        keptNode.ParentNodeId = rootNode.Id;
        keptNode.ParentKind = "delegate";
        keptNode.ParentDeclaringTypeName = string.Empty;
        keptNode.ParentFieldName = string.Empty;
        keptNode.ParentArrayIndices = new List<int>();
        keptNode.ParentDelegateIndex = 0;
        TestRoot fresh = new TestRoot { Numbers = new[] { 0, 0, 0 } };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.False(restore.Success);
        Assert.Contains("in a delegate call target", restore.Error);
        Assert.Equal(new[] { 0, 0, 0 }, fresh.Numbers);
    }

    // Makes another node the only thing that holds keptNode, by emptying the slot
    // the room keeps it in and adding a named field on the new holder. Both halves
    // matter: adding the holder alone leaves the node attached where it was, and
    // emptying the room's slot alone leaves the document with no parent edge for
    // it, which ValidateNodeParentEdges refuses on its own.
    private static void ParkNodeInAField(
        AkronReconstructionDocument document,
        AkronReconstructionNode keptNode,
        AkronReconstructionNode holder,
        Type declaringType,
        string fieldName
    ) {
        RemoveOriginalOwningField(document, keptNode);
        holder.Fields.Add(new AkronReconstructionField {
            DeclaringTypeName = declaringType.AssemblyQualifiedName!,
            Name = fieldName,
            Path = holder.Path + "." + fieldName,
            Value = new AkronReconstructionValue { Kind = AkronReconstructionGraph.ReferenceValueKind, NodeId = keptNode.Id }
        });
        keptNode.ParentNodeId = holder.Id;
        keptNode.ParentKind = "field";
        keptNode.ParentDeclaringTypeName = declaringType.AssemblyQualifiedName!;
        keptNode.ParentFieldName = fieldName;
        keptNode.ParentArrayIndices = new List<int>();
    }

    private static void RemoveOriginalOwningField(
        AkronReconstructionDocument document,
        AkronReconstructionNode keptNode
    ) {
        AkronReconstructionNode originalOwner = document.Nodes.Single(node => node.Id == keptNode.ParentNodeId);
        AkronReconstructionField originalEdge = originalOwner.Fields.Single(field =>
            field.Name == keptNode.ParentFieldName &&
            field.Value.Kind == AkronReconstructionGraph.ReferenceValueKind &&
            field.Value.NodeId == keptNode.Id);
        Assert.True(originalOwner.Fields.Remove(originalEdge));
    }

    private static void EmptyTheSlotTheRoomKeepsItIn(
        AkronReconstructionDocument document,
        AkronReconstructionNode keptNode
    ) {
        AkronReconstructionNode parent = document.Nodes.Single(node => node.Id == keptNode.ParentNodeId);
        parent.Fields.Single(field =>
                field.Name == keptNode.ParentFieldName &&
                field.Value?.Kind == AkronReconstructionGraph.ReferenceValueKind &&
                field.Value.NodeId == keptNode.Id)
            .Value = new AkronReconstructionValue();
    }

    [Fact]
    public void RestoreDoesNotAuthenticateAReferenceAtItsClaimedFreshPath() {
        UniqueTestEntity savedEntity = CreateUninitializedEntity<UniqueTestEntity>();
        savedEntity.Value = 37;
        savedEntity.Resource = new TestResource("saved-process", "player-audio");
        TestRoot saved = new TestRoot { RoomEntity = savedEntity };
        UniqueTestEntity baselineEntity = CreateUninitializedEntity<UniqueTestEntity>();
        baselineEntity.Resource = new TestResource("baseline-process", "player-audio");
        TestRoot baseline = new TestRoot {
            RoomEntity = CreateUninitializedEntity<Entity>(),
            AlternativeRoomEntity = baselineEntity
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(
            IsLiveResource,
            resource => ((TestResource) resource).StableKey);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode entityNode = capture.Document.Nodes.Single(node =>
            node.ParentFieldName == nameof(TestRoot.RoomEntity));
        Assert.NotEmpty(entityNode.FreshPath);
        UniqueTestEntity freshEntity = CreateUninitializedEntity<UniqueTestEntity>();
        freshEntity.Resource = new TestResource("fresh-process", "player-audio");
        TestRoot fresh = new TestRoot {
            RoomEntity = CreateUninitializedEntity<Entity>(),
            AlternativeRoomEntity = freshEntity
        };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.False(restore.Success);
        Assert.Contains("reference edge is not authentic", restore.Error);
    }

    [Fact]
    public void CoreLibraryComparerSingletonCanBeSharedAcrossFreshPaths() {
        IEqualityComparer<string> comparer = EqualityComparer<string>.Default;
        ComparerRoot saved = new ComparerRoot {
            Primary = comparer,
            Secondary = comparer
        };
        ComparerRoot baseline = new ComparerRoot {
            Primary = comparer
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        ComparerRoot fresh = new ComparerRoot {
            Primary = comparer
        };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Same(comparer, fresh.Primary);
        Assert.Same(comparer, fresh.Secondary);
    }

    [Fact]
    public void FreshDirectOwnerFieldCanBeReusedWhenCaptureDidNotPairIt() {
        TestRoot saved = new TestRoot { Primary = new TestNode { Value = 37 } };
        TestRoot baseline = new TestRoot { Primary = new TestNode() };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode primaryNode = capture.Document.Nodes.Single(node =>
            node.ParentNodeId == capture.Document.RootNodeId &&
            node.ParentFieldName == nameof(TestRoot.Primary));
        primaryNode.UseFreshObject = false;
        primaryNode.FreshPath.Clear();
        TestNode freshPrimary = new TestNode();
        TestRoot fresh = new TestRoot { Primary = freshPrimary };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Same(freshPrimary, fresh.Primary);
        Assert.Equal(37, fresh.Primary.Value);
    }

    [Fact]
    public void UniqueRoomObjectTypeMatchesAfterEntityListOrderChanges() {
        UniqueTestEntity savedEntity = CreateUninitializedEntity<UniqueTestEntity>();
        savedEntity.Value = 37;
        savedEntity.Resource = new TestResource("saved-process", "player-audio");
        EntityListRoot saved = new EntityListRoot {
            Entities = new List<Entity> { savedEntity }
        };
        UniqueTestEntity baselineEntity = CreateUninitializedEntity<UniqueTestEntity>();
        baselineEntity.Resource = new TestResource("baseline-process", "player-audio");
        EntityListRoot baseline = new EntityListRoot {
            Entities = new List<Entity> {
                CreateUninitializedEntity<Entity>(),
                baselineEntity
            }
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(
            IsLiveResource,
            resource => ((TestResource) resource).StableKey);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode entityNode = capture.Document.Nodes.Single(node =>
            node.TypeName == typeof(UniqueTestEntity).AssemblyQualifiedName);
        Assert.NotEmpty(entityNode.FreshPath);
        UniqueTestEntity freshEntity = CreateUninitializedEntity<UniqueTestEntity>();
        freshEntity.Resource = new TestResource("fresh-process", "player-audio");
        EntityListRoot fresh = new EntityListRoot {
            Entities = new List<Entity> {
                CreateUninitializedEntity<Entity>(),
                freshEntity
            }
        };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Same(freshEntity, fresh.Entities[0]);
        Assert.Equal(37, freshEntity.Value);
        Assert.Equal("fresh-process", freshEntity.Resource.ProcessIdentity);
    }

    [Fact]
    public void RepeatedEntityTypesMatchBySourceIdAfterEntityListOrderChanges() {
        SourceIdentifiedEntity savedFirst = CreateSourceIdentifiedEntity("a00", 10, 37);
        SourceIdentifiedEntity savedSecond = CreateSourceIdentifiedEntity("a00", 20, 81);
        SourceEntityListOwnerRoot saved = CreateSourceEntityListOwnerRoot(savedFirst, savedSecond);
        SourceEntityListOwnerRoot baseline = CreateSourceEntityListOwnerRoot(
            CreateSourceIdentifiedEntity("a00", 20, 0),
            CreateSourceIdentifiedEntity("a00", 10, 0));
        AkronReconstructionGraph graph = new AkronReconstructionGraph(
            IsLiveResource,
            _ => string.Empty);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        foreach (AkronReconstructionNode entityNode in capture.Document.Nodes.Where(node =>
                     node.TypeName == typeof(SourceIdentifiedEntity).AssemblyQualifiedName)) {
            entityNode.UseFreshObject = false;
            entityNode.FreshPath.Clear();
        }
        SourceIdentifiedEntity freshSecond = CreateSourceIdentifiedEntity("a00", 20, 0);
        SourceIdentifiedEntity freshFirst = CreateSourceIdentifiedEntity("a00", 10, 0);
        SourceEntityListOwnerRoot fresh = CreateSourceEntityListOwnerRoot(freshSecond, freshFirst);

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Same(freshFirst, GetEntityListContents(fresh.Entities)[0]);
        Assert.Same(freshSecond, GetEntityListContents(fresh.Entities)[1]);
        Assert.Equal(37, freshFirst.Value);
        Assert.Equal(81, freshSecond.Value);
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    // The map half of the same rule. A saved entity that failed to pair carries an
    // EntityID the reloaded room does not contain, and the room holds a same-typed
    // entity under a different one. Which of the two is true decides everything:
    //
    //   the map still lays the saved id out, and this run's session flags meant the
    //   room did not build it. Rebuild it beside the ones the room did build.
    //
    //   the map does not lay it out any more. The room the document measured no
    //   longer exists, and rebuilding hands the saved entity a live entity's list
    //   slot and its saved state while the entity the room built is dropped - all of
    //   it reported as success, because the only thing consulted was that the fresh
    //   room holds SOME entity of this type at this wildcarded list path.
    [Fact]
    public void ASavedMapEntityTheMapNoLongerPlacesIsRefusedRatherThanTakingALiveEntitysPlace() {
        SourceEntityListOwnerRoot saved = CreateSourceEntityListOwnerRoot(
            CreateSourceIdentifiedEntity("a00", 10, 37));
        SourceEntityListOwnerRoot baseline = CreateSourceEntityListOwnerRoot(
            CreateSourceIdentifiedEntity("a00", 10, 0));
        SourceIdentifiedEntity freshEntity = CreateSourceIdentifiedEntity("a00", 20, 0);
        SourceEntityListOwnerRoot fresh = CreateSourceEntityListOwnerRoot(freshEntity);
        // The map was edited between the two: entity 10 became entity 20.
        AkronReconstructionGraph graph = CreateMapAwareGraph(new TestMapPlacement()
            .Place(baseline, "a00", 10)
            .Place(fresh, "a00", 20));
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        Assert.Single(capture.Document.Nodes, node => node.MapPlacedEntity);
        // SaveSnapshot stamps this on the way to disk; the map rule reads it to decide
        // which room's ids it has any business refusing over.
        capture.Document.Room = "a00";

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.False(restore.Success);
        Assert.Contains("saved map entity is no longer placed by this map", restore.Error);
        Assert.Contains("saved-entity-id=a00:10", restore.Error);
        Assert.Equal(typeof(SourceIdentifiedEntity).AssemblyQualifiedName, restore.RefusedTypeName);
        // Refused before a single assignment ran, so the room is the room the reload
        // built: its own entity, with its own state.
        Assert.Same(freshEntity, Assert.Single(GetEntityListContents(fresh.Entities)));
        Assert.Equal(0, freshEntity.Value);
    }

    // The control that decides the shape of the rule, and the reason it cannot be
    // "refuse a saved map entity the reloaded room did not build". A room whose
    // session no longer spawns one of its entities restores correctly today, and it
    // has to keep doing so.
    //
    // Only the order that works is asserted, and that is deliberate. W30 5.1 measured
    // that this same population is refused when the document lists the live entity
    // first, because that entity's edge spends the one occurrence the rebuilt one then
    // needs. That half of the order dependence is still here: the room is refused
    // rather than restored, which costs a slot and corrupts nothing, and both candidate
    // fixes for it loosen the occurrence budget.
    //
    // The half that mattered is closed.
    // AnUnpairableTrailedMapEntityIsRefusedInEitherDocumentOrder is the room where the
    // other order used to report success and leave the room wrong; it now refuses
    // whichever way the document lists it. What separates the two is not the map - by
    // map evidence they are the same room - but whether the write drops a fresh object
    // this document keeps. Here it drops nothing: the entity is rebuilt beside the one
    // the room did build.
    [Fact]
    public void ASavedMapEntityTheReloadDidNotBuildIsRebuiltBesideTheOnesItDid() {
        SourceIdentifiedEntity savedAbsent = CreateSourceIdentifiedEntity("a00", 10, 37);
        SourceIdentifiedEntity savedLive = CreateSourceIdentifiedEntity("a00", 20, 81);
        SourceEntityListOwnerRoot saved = CreateSourceEntityListOwnerRoot(savedAbsent, savedLive);
        SourceIdentifiedEntity baselineAbsent = CreateSourceIdentifiedEntity("a00", 10, 0);
        SourceIdentifiedEntity baselineLive = CreateSourceIdentifiedEntity("a00", 20, 0);
        SourceEntityListOwnerRoot baseline = CreateSourceEntityListOwnerRoot(baselineAbsent, baselineLive);
        SourceIdentifiedEntity freshLive = CreateSourceIdentifiedEntity("a00", 20, 0);
        SourceEntityListOwnerRoot fresh = CreateSourceEntityListOwnerRoot(freshLive);
        // The map is the same on both sides. Only this run's session decided not to
        // build entity 10.
        AkronReconstructionGraph graph = CreateMapAwareGraph(new TestMapPlacement()
            .Place(baseline, "a00", 10, 20)
            .Place(fresh, "a00", 10, 20));
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        capture.Document.Room = "a00";

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        List<Entity> restored = GetEntityListContents(fresh.Entities);
        Assert.Equal(2, restored.Count);
        // The entity the room built keeps its place and takes its saved state.
        Assert.Contains(restored, entity => ReferenceEquals(entity, freshLive));
        Assert.Equal(81, freshLive.Value);
        // The one the room did not build is rebuilt beside it rather than replacing it.
        SourceIdentifiedEntity rebuilt = restored
            .OfType<SourceIdentifiedEntity>()
            .Single(entity => !ReferenceEquals(entity, freshLive));
        Assert.Equal(37, rebuilt.Value);
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    // The crossed population, which is where "would this displace a live entity?"
    // gives the wrong answer. The saved frame deleted entity 10 before the slot was
    // set, and this reload did not build entity 20. The saved population is the
    // truth, so entity 10 is not state to preserve - it is the entity the document
    // deliberately does not have, and dropping it is the correct outcome.
    [Fact]
    public void AMapEntityTheSavedFrameDeletedIsStillDroppedWhenTheReloadBuiltOnlyIt() {
        SourceEntityListOwnerRoot saved = CreateSourceEntityListOwnerRoot(
            CreateSourceIdentifiedEntity("a00", 20, 81));
        SourceEntityListOwnerRoot baseline = CreateSourceEntityListOwnerRoot(
            CreateSourceIdentifiedEntity("a00", 10, 0),
            CreateSourceIdentifiedEntity("a00", 20, 0));
        SourceIdentifiedEntity freshDeleted = CreateSourceIdentifiedEntity("a00", 10, 0);
        SourceEntityListOwnerRoot fresh = CreateSourceEntityListOwnerRoot(freshDeleted);
        AkronReconstructionGraph graph = CreateMapAwareGraph(new TestMapPlacement()
            .Place(baseline, "a00", 10, 20)
            .Place(fresh, "a00", 10, 20));
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        capture.Document.Room = "a00";

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        SourceIdentifiedEntity restored = Assert.IsType<SourceIdentifiedEntity>(
            Assert.Single(GetEntityListContents(fresh.Entities)));
        Assert.NotSame(freshDeleted, restored);
        Assert.Equal(81, restored.Value);
    }

    // An EntityID the map never laid out - one a mod made up for an entity it spawns
    // itself - says nothing about whether the map changed, so it is left alone. This
    // is the same shape as the refusal above and differs only in what the map owned
    // when the slot was set, which is why the evidence is recorded per node at
    // capture rather than worked out from the reloaded map alone.
    [Fact]
    public void AnEntityIdTheMapNeverPlacedIsStillRebuiltWhenTheReloadedRoomLacksIt() {
        SourceEntityListOwnerRoot saved = CreateSourceEntityListOwnerRoot(
            CreateSourceIdentifiedEntity("a00", 10, 37));
        SourceEntityListOwnerRoot baseline = CreateSourceEntityListOwnerRoot(
            CreateSourceIdentifiedEntity("a00", 10, 0));
        SourceIdentifiedEntity freshEntity = CreateSourceIdentifiedEntity("a00", 20, 0);
        SourceEntityListOwnerRoot fresh = CreateSourceEntityListOwnerRoot(freshEntity);
        AkronReconstructionGraph graph = CreateMapAwareGraph(new TestMapPlacement()
            .Place(baseline, "a00", 20)
            .Place(fresh, "a00", 20));
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        Assert.DoesNotContain(capture.Document.Nodes, node => node.MapPlacedEntity);
        capture.Document.Room = "a00";

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        // The saved population is one entity, so that is what the room ends with: the
        // made-up id rebuilt carrying its saved state, and the entity the reload built
        // dropped because the document does not contain it. That is the same outcome
        // the refusal above stops, and it is allowed here for the one reason that
        // matters - the map never owned this id, so its absence is not evidence of
        // anything.
        SourceIdentifiedEntity restored = Assert.IsType<SourceIdentifiedEntity>(
            Assert.Single(GetEntityListContents(fresh.Entities)));
        Assert.NotSame(freshEntity, restored);
        Assert.Equal(37, restored.Value);
    }

    // The rule's jurisdiction, and the reason it cannot be "any saved map entity the
    // reloaded room does not hold". A saved entity's EntityID names the room the map
    // placed it in, and that is not always the room being restored: Leader.GainFollower
    // leaves Tags.Persistent on a strawberry and Level.TransitionRoutine carries
    // persistent entities between rooms, so a berry picked up in a01 is still a01:5
    // while the player stands in a40. TryLoadFreshRoom's UnloadLevel keeps only
    // Tags.Global, so that entity is never in the reloaded room and its node always
    // reaches the rule.
    //
    // An edit to a01 says nothing about whether rebuilding it would displace one of
    // a40's entities, which is the only harm the rule exists to stop. Without the room
    // test a golden-berry run - which carries an entity named by the chapter's first
    // room for the whole chapter - would make every slot in the chapter refuse over an
    // edit to a room the player is not in.
    [Fact]
    public void AnEntityCarriedInFromAnotherRoomIsNotRefusedWhenThatRoomsMapChanges() {
        SourceIdentifiedEntity savedCarried = CreateSourceIdentifiedEntity("a01", 5, 37);
        SourceIdentifiedEntity savedLive = CreateSourceIdentifiedEntity("a40", 20, 81);
        SourceEntityListOwnerRoot saved = CreateSourceEntityListOwnerRoot(savedCarried, savedLive);
        SourceEntityListOwnerRoot baseline = CreateSourceEntityListOwnerRoot(
            CreateSourceIdentifiedEntity("a01", 5, 0),
            CreateSourceIdentifiedEntity("a40", 20, 0));
        SourceIdentifiedEntity freshLive = CreateSourceIdentifiedEntity("a40", 20, 0);
        SourceEntityListOwnerRoot fresh = CreateSourceEntityListOwnerRoot(freshLive);
        // a01 was edited between the two: the berry's placement was deleted and a new
        // one put down, which gets a new id. a40, the room being restored, is identical.
        AkronReconstructionGraph graph = CreateMapAwareGraph(new TestMapPlacement()
            .Place(baseline, "a01", 5, 6)
            .Place(baseline, "a40", 20)
            .Place(fresh, "a01", 6, 7)
            .Place(fresh, "a40", 20));
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        // The evidence is there and says the map did place a01:5, so the room test is
        // what makes it inert rather than an absent bit.
        Assert.Equal(2, capture.Document.Nodes.Count(node => node.MapPlacedEntity));
        capture.Document.Room = "a40";

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        List<Entity> restored = GetEntityListContents(fresh.Entities);
        Assert.Equal(2, restored.Count);
        Assert.Contains(restored, entity => ReferenceEquals(entity, freshLive));
        Assert.Equal(81, freshLive.Value);
        SourceIdentifiedEntity rebuiltCarried = restored
            .OfType<SourceIdentifiedEntity>()
            .Single(entity => !ReferenceEquals(entity, freshLive));
        Assert.Equal(37, rebuiltCarried.Value);
        Assert.Equal("a01", GetRuntimeField<EntityID>(rebuiltCarried, "<SourceId>k__BackingField").Level);
    }

    // The rule has to run before the resolvers, not only before the authenticators.
    // TryResolveFreshFieldAlias binds a node to whatever object the fresh room keeps in
    // the same field, and it has no SourceId check of its own - the SourceId gate above
    // it only nulls a candidate out, and the alias resolver then puts one back. So a
    // saved map entity also held in an ordinary field outside entity or component list
    // storage used to bypass the rule entirely: measured, the saved state of entity 10
    // landed on the entity the edited map calls 99, that entity's SourceId was
    // overwritten to 10, its clean-load state was gone, and the load reported success.
    // That is the exact outcome the rule exists to stop, arrived at by binding rather
    // than by rebuilding.
    [Fact]
    public void ASavedMapEntityTheMapNoLongerPlacesIsRefusedBeforeAFieldAliasCanBindIt() {
        SourceIdentifiedEntity savedTarget = CreateSourceIdentifiedEntity("a00", 10, 37);
        SourceIdentifiedEntity savedOther = CreateSourceIdentifiedEntity("a00", 20, 81);
        EntityAliasFirstOwnerRoot saved = new EntityAliasFirstOwnerRoot {
            Holder = new PassiveEntityAliasHolder { Alias = savedTarget },
            Entities = CreateSourceEntityListOwnerRoot(savedOther, savedTarget).Entities
        };
        SourceIdentifiedEntity baselineTarget = CreateSourceIdentifiedEntity("a00", 10, 0);
        SourceIdentifiedEntity baselineOther = CreateSourceIdentifiedEntity("a00", 20, 0);
        EntityAliasFirstOwnerRoot baseline = new EntityAliasFirstOwnerRoot {
            Holder = new PassiveEntityAliasHolder { Alias = baselineTarget },
            Entities = CreateSourceEntityListOwnerRoot(baselineOther, baselineTarget).Entities
        };
        // The map was edited: entity 10 is gone and the room now places 99 instead. The
        // reload built 99 and wired it into the same field the saved frame reached 10
        // through, which is the field that used to bind it.
        SourceIdentifiedEntity freshOther = CreateSourceIdentifiedEntity("a00", 20, 0);
        SourceIdentifiedEntity freshRenumbered = CreateSourceIdentifiedEntity("a00", 99, 0);
        EntityAliasFirstOwnerRoot fresh = new EntityAliasFirstOwnerRoot {
            Holder = new PassiveEntityAliasHolder { Alias = freshRenumbered },
            Entities = CreateSourceEntityListOwnerRoot(freshRenumbered, freshOther).Entities
        };
        AkronReconstructionGraph graph = CreateMapAwareGraph(new TestMapPlacement()
            .Place(baseline, "a00", 10, 20)
            .Place(fresh, "a00", 99, 20));
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        capture.Document.Room = "a00";

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.False(restore.Success);
        Assert.Contains("saved map entity is no longer placed by this map", restore.Error);
        Assert.Contains("saved-entity-id=a00:10", restore.Error);
        // The room is the room the reload built: the entity the edited map calls 99 is
        // still called 99 and still holds its own state.
        Assert.Same(freshRenumbered, fresh.Holder.Alias);
        Assert.Equal(99, GetRuntimeField<EntityID>(freshRenumbered, "<SourceId>k__BackingField").ID);
        Assert.Equal(0, freshRenumbered.Value);
        Assert.Equal(0, freshOther.Value);
    }

    // The only overload the running game ever calls, against a real Session. Every map
    // test above stands in for the map with a callback of its own, and the graphs that
    // do wire the production reader feed it roots whose Level is null, so before this
    // it had never returned a non-empty set under test.
    //
    // The rows split three ways, and the split is the contract. A set of ids is map data
    // that places them. An empty set is map data that places nothing in that room, which
    // a room the map does not have also is. Null is no map data at all, and it is the
    // only answer that must never reach the refusal: the refusal says the player's map
    // changed, and a map nobody could read is not evidence of that.
    //
    // The last two rows are why the reader walks the area list by hand instead of
    // reading Session.MapData. That property is "AreaData.Areas[Area.ID].Mode[(int)
    // Area.Mode].MapData" - two unchecked array indexes - and it throws for both. A
    // capture runs on the persistence worker while the game thread is free to rebuild
    // AreaData.Areas, and a throw there fails the whole slot with an exception name at
    // path "$" rather than with anything a player can act on.
    [Fact]
    public void TheProductionMapReaderReadsARealSessionAndAnswersNothingRatherThanThrowing() {
        List<AreaData> installedAreas = AreaData.Areas;
        try {
            AkronPersistentRuntimeState state = InstallTestMapAndCreateRoot("a00", 0, AreaMode.Normal);

            Assert.Equal(
                new[] { 10, 11, 10000004 },
                AkronStartPosReconstruction.GetMapPlacedEntityIds(state, "a00").ToArray());
            Assert.Equal(
                new[] { 20 },
                AkronStartPosReconstruction.GetMapPlacedEntityIds(state, "a01").ToArray());
            // Map data that has been read and has no such room: it places nothing there,
            // which is an answer rather than an absence of one.
            Assert.Empty(AkronStartPosReconstruction.GetMapPlacedEntityIds(state, "no-such-room"));
            Assert.Empty(AkronStartPosReconstruction.GetMapPlacedEntityIds(state, null));
            // The two roots that are not a loaded room: an action-state document's
            // Dictionary, and a room state whose level has no session. Neither has a map
            // behind it, so neither may make a saved id look dropped.
            Assert.Null(AkronStartPosReconstruction.GetMapPlacedEntityIds(
                new Dictionary<string, object>(), "a00"));
            Assert.Null(AkronStartPosReconstruction.GetMapPlacedEntityIds(
                new AkronPersistentRuntimeState {
                    Level = (Level) RuntimeHelpers.GetUninitializedObject(typeof(Level))
                },
                "a00"));

            Assert.Null(AkronStartPosReconstruction.GetMapPlacedEntityIds(
                CreateMapRoomState("a00", 7, AreaMode.Normal), "a00"));
            Assert.Null(AkronStartPosReconstruction.GetMapPlacedEntityIds(
                CreateMapRoomState("a00", 0, AreaMode.BSide), "a00"));
        } finally {
            AreaData.Areas = installedAreas;
        }
    }

    // The unsafe direction of the map rule, and the reason the reader distinguishes "no
    // map data" from "map data without this id" at all.
    //
    // A stamped node plus a fresh room whose map cannot be read used to produce the map
    // refusal, which tells the player "this map no longer places the <Type> the slot
    // saved. Updating a map or a collab does this." That sentence is a false story about
    // their install when nothing about the map changed and the read simply failed - a map
    // reload in flight on the game thread, or a session whose area is not in the loaded
    // area list. It has to fall through silently instead, and it has to keep refusing for
    // a map that was read and really did drop the id.
    [Fact]
    public void AMapThatCannotBeReadDoesNotAccuseThePlayersMapOfHavingChanged() {
        // The same population as the refusal test above: a saved entity 10 the fresh room
        // does not build, and a live entity 20 of the same type in the same list slot.
        SourceEntityListOwnerRoot saved = CreateSourceEntityListOwnerRoot(
            CreateSourceIdentifiedEntity("a00", 10, 37));
        SourceEntityListOwnerRoot baseline = CreateSourceEntityListOwnerRoot(
            CreateSourceIdentifiedEntity("a00", 10, 0));
        SourceIdentifiedEntity freshEntity = CreateSourceIdentifiedEntity("a00", 20, 0);
        SourceEntityListOwnerRoot fresh = CreateSourceEntityListOwnerRoot(freshEntity);
        // The one difference: the map places 10 when the slot is set and cannot be read at
        // all when it is loaded. Null, not an empty set - which is what the production
        // reader answers for a map rebuild in flight, or a session whose area is not in
        // the loaded area list.
        AkronReconstructionGraph graph = new AkronReconstructionGraph(
            IsLiveResource,
            _ => string.Empty,
            getMapPlacedEntityIds: (root, _) => ReferenceEquals(root, baseline) ? new[] { 10 } : null);

        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        Assert.Single(capture.Document.Nodes, node => node.MapPlacedEntity);
        capture.Document.Room = "a00";

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        // Whether this document restores is decided by everything else in it, exactly as
        // it was in every build before the map evidence existed. What must not happen is
        // this one refusal, because its message names the player's map as the thing that
        // changed and nothing here says it did.
        Assert.DoesNotContain("saved map entity is no longer placed by this map", restore.Error);
        Assert.NotEqual(AkronReconstructionRefusalKind.ChangedMap, restore.RefusedKind);
    }

    // Site B end to end through the production map reader rather than through a test
    // stand-in, on the root type the game uses, with the map edited between the two
    // reads by deleting an entity from the LevelData the map holds.
    [Fact]
    public void AMapEditIsRefusedThroughTheProductionMapReader() {
        List<AreaData> installedAreas = AreaData.Areas;
        try {
            AkronPersistentRuntimeState unusedRoot = InstallTestMapAndCreateRoot("a00", 0, AreaMode.Normal);
            Assert.NotNull(unusedRoot);
            LevelData room = GetMapRoom(AreaData.Areas[0].Mode[0].MapData, "a00");
            room.Entities = new List<EntityData> { new EntityData { ID = 10 }, new EntityData { ID = 20 } };
            room.Triggers = new List<EntityData>();

            // The map is the same on both sides: the entity the reload did not build is
            // rebuilt beside the one it did.
            AkronReconstructionGraph graph = CreateStartPosGraph();
            AkronReconstructionCapture capture = CaptureMapRoom(graph, savedValue: 37);
            capture.Document.Room = "a00";
            AkronPersistentRuntimeState unchangedFresh = CreateMapRoomState(
                "a00", 0, AreaMode.Normal, CreateSourceIdentifiedEntity("a00", 20, 0));

            AkronReconstructionRestore unchanged = graph.Restore(capture.Document, unchangedFresh);

            Assert.True(unchanged.Success, unchanged.Error);

            // Now the map drops entity 10, which is the only difference.
            room.Entities = new List<EntityData> { new EntityData { ID = 20 } };
            AkronPersistentRuntimeState editedFresh = CreateMapRoomState(
                "a00", 0, AreaMode.Normal, CreateSourceIdentifiedEntity("a00", 20, 0));

            AkronReconstructionRestore edited = graph.Restore(capture.Document, editedFresh);

            Assert.False(edited.Success);
            Assert.Contains("saved map entity is no longer placed by this map", edited.Error);
            Assert.Contains("saved-entity-id=a00:10", edited.Error);
        } finally {
            AreaData.Areas = installedAreas;
        }
    }

    // The map half of the evidence covers the room document only, and this is what says
    // so. CaptureActionState and RestoreActionState walk Dictionary roots, which hold no
    // Level, so the production reader has no map to read there and every Entity a
    // registered action's state reaches is stamped false whatever the map says. It is
    // symmetric - the restore reads the same false and refuses nothing - so it fails to
    // the behaviour of a document written before the evidence existed rather than to a
    // wrong restore. The key half has no such limit: it is read off the saved object and
    // applies to every node in both documents.
    [Fact]
    public void TheMapHalfOfTheIdentityEvidenceCoversTheRoomDocumentOnly() {
        List<AreaData> installedAreas = AreaData.Areas;
        try {
            AkronPersistentRuntimeState roomRoot = InstallTestMapAndCreateRoot("a00", 0, AreaMode.Normal);
            // The same map, read the way the room document reads it.
            Assert.Contains(10, AkronStartPosReconstruction.GetMapPlacedEntityIds(roomRoot, "a00"));

            Dictionary<string, Dictionary<Type, Dictionary<string, object>>> savedActionState =
                CreateActionStateHolding(CreateSourceIdentifiedEntity("a00", 10, 37));
            Dictionary<string, Dictionary<Type, Dictionary<string, object>>> baselineActionState =
                CreateActionStateHolding(CreateSourceIdentifiedEntity("a00", 10, 0));

            AkronReconstructionCapture capture = AkronStartPosReconstruction.CaptureActionState(
                savedActionState,
                baselineActionState);

            Assert.True(capture.Success, capture.Error);
            Assert.Contains(capture.Document.Nodes, node =>
                node.TypeName == typeof(SourceIdentifiedEntity).AssemblyQualifiedName);
            Assert.DoesNotContain(capture.Document.Nodes, node => node.MapPlacedEntity);
        } finally {
            AreaData.Areas = installedAreas;
        }
    }

    // Captures one saved map entity, a00:10, against a baseline holding the same entity
    // with no state on it, both under a root the production map reader can walk.
    private static AkronReconstructionCapture CaptureMapRoom(AkronReconstructionGraph graph, int savedValue) {
        AkronPersistentRuntimeState saved = CreateMapRoomState(
            "a00", 0, AreaMode.Normal, CreateSourceIdentifiedEntity("a00", 10, savedValue));
        AkronPersistentRuntimeState baseline = CreateMapRoomState(
            "a00", 0, AreaMode.Normal, CreateSourceIdentifiedEntity("a00", 10, 0));
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        Assert.Single(capture.Document.Nodes, node => node.MapPlacedEntity);
        return capture;
    }

    private static Dictionary<string, Dictionary<Type, Dictionary<string, object>>> CreateActionStateHolding(
        Entity entity
    ) {
        return new Dictionary<string, Dictionary<Type, Dictionary<string, object>>> {
            ["helper"] = new Dictionary<Type, Dictionary<string, object>> {
                [typeof(SourceIdentifiedEntity)] = new Dictionary<string, object> { ["held"] = entity }
            }
        };
    }

    // A real map chain, the way a loaded map holds it: AreaData.Areas -> ModeProperties
    // -> MapData -> LevelData. Replaces AreaData.Areas for the length of one test, which
    // is the only way to reach Session.MapData at all - it resolves through that static
    // rather than through the room graph. The caller restores it.
    private static AkronPersistentRuntimeState InstallTestMapAndCreateRoot(
        string room,
        int areaId,
        AreaMode mode
    ) {
        LevelData first = (LevelData) RuntimeHelpers.GetUninitializedObject(typeof(LevelData));
        first.Name = "a00";
        first.Entities = new List<EntityData> { new EntityData { ID = 10 }, new EntityData { ID = 11 } };
        first.Triggers = new List<EntityData> { new EntityData { ID = 4 } };
        LevelData second = (LevelData) RuntimeHelpers.GetUninitializedObject(typeof(LevelData));
        second.Name = "a01";
        second.Entities = new List<EntityData> { new EntityData { ID = 20 } };
        second.Triggers = new List<EntityData>();

        MapData mapData = (MapData) RuntimeHelpers.GetUninitializedObject(typeof(MapData));
        mapData.Levels = new List<LevelData> { first, second };
        ModeProperties modeProperties = (ModeProperties) RuntimeHelpers.GetUninitializedObject(typeof(ModeProperties));
        modeProperties.MapData = mapData;
        AreaData area = (AreaData) RuntimeHelpers.GetUninitializedObject(typeof(AreaData));
        area.Mode = new[] { modeProperties };
        AreaData.Areas = new List<AreaData> { area };

        return CreateMapRoomState(room, areaId, mode);
    }

    // The root shape the game hands the graph: a runtime state whose Level carries the
    // Session the map is read through, with the room's entities in a mod session beside
    // it. AreaKey is built field by field because its constructor indexes
    // AreaData.Areas, and the point of two of these is to name an area that is not there.
    private static AkronPersistentRuntimeState CreateMapRoomState(
        string room,
        int areaId,
        AreaMode mode,
        params Entity[] entities
    ) {
        AreaKey area = default;
        area.ID = areaId;
        area.Mode = mode;
        Session session = (Session) RuntimeHelpers.GetUninitializedObject(typeof(Session));
        session.Area = area;
        session.Level = room;
        Level level = (Level) RuntimeHelpers.GetUninitializedObject(typeof(Level));
        level.Session = session;
        AkronPersistentRuntimeState state = new AkronPersistentRuntimeState { Level = level };
        state.ModuleSessions["helper"] = new TestEntityListSession {
            Entities = CreateSourceEntityListOwnerRoot(entities).Entities
        };
        return state;
    }

    // Triggers are numbered in their own range. Everest's patch_Level.CreateEntityId
    // adds 10,000,000 to a trigger's map id while it loads the trigger list, so the
    // EntityID a trigger carries at runtime is not the number its map data holds.
    // Reading both map lists as one range leaves every trigger unmatched, and an
    // entity whose id the map dropped can still be found through a trigger that
    // happens to carry the same raw number - which is common, because the two lists
    // number independently from 1.
    [Fact]
    public void TriggersAreCountedInTheEntityIdRangeTheyActuallyCarry() {
        LevelData room = (LevelData) RuntimeHelpers.GetUninitializedObject(typeof(LevelData));
        room.Entities = new List<EntityData> { new EntityData { ID = 4 }, new EntityData { ID = 9 } };
        room.Triggers = new List<EntityData> { new EntityData { ID = 4 } };

        List<int> placed = AkronStartPosReconstruction.GetMapPlacedEntityIds(room).ToList();

        Assert.Equal(new[] { 4, 9, 10000004 }, placed);
        // The trigger's raw 4 must not stand in for an entity 4 the map still has, and
        // must not stand in for one it no longer has either.
        Assert.DoesNotContain(10000009, placed);
    }

    // Stands in for Session.MapData: which EntityIDs a map lays out in a room,
    // answered per graph root, so one test can give the capture baseline and the
    // reloaded room two different maps and model a map edited between them.
    private sealed class TestMapPlacement {
        private readonly Dictionary<object, Dictionary<string, int[]>> placements =
            new Dictionary<object, Dictionary<string, int[]>>(ReferenceEqualityComparer.Instance);

        public TestMapPlacement Place(object root, string room, params int[] ids) {
            if (!placements.TryGetValue(root, out Dictionary<string, int[]>? rooms)) {
                rooms = new Dictionary<string, int[]>(StringComparer.Ordinal);
                placements[root] = rooms;
            }
            rooms[room] = ids;
            return this;
        }

        public IEnumerable<int> Ids(object root, string room) {
            return placements.TryGetValue(root, out Dictionary<string, int[]>? rooms) &&
                   rooms.TryGetValue(room, out int[]? ids)
                ? ids
                : Array.Empty<int>();
        }
    }

    private static AkronReconstructionGraph CreateMapAwareGraph(TestMapPlacement placement) {
        return new AkronReconstructionGraph(
            IsLiveResource,
            _ => string.Empty,
            getMapPlacedEntityIds: placement.Ids);
    }

    [Fact]
    public void FreshEntityListOwnerCanAppearAfterAnEarlierReconstructedAlias() {
        SourceIdentifiedEntity savedTarget = CreateSourceIdentifiedEntity("a00", 10, 37);
        SourceIdentifiedEntity savedOther = CreateSourceIdentifiedEntity("a00", 20, 81);
        EntityAliasFirstOwnerRoot saved = new EntityAliasFirstOwnerRoot {
            Holder = new PassiveEntityAliasHolder { Alias = savedTarget },
            Entities = CreateSourceEntityListOwnerRoot(savedOther, savedTarget).Entities
        };
        SourceIdentifiedEntity baselineTarget = CreateSourceIdentifiedEntity("a00", 10, 0);
        SourceIdentifiedEntity baselineOther = CreateSourceIdentifiedEntity("a00", 20, 0);
        EntityAliasFirstOwnerRoot baseline = new EntityAliasFirstOwnerRoot {
            Holder = new PassiveEntityAliasHolder { Alias = baselineTarget },
            Entities = CreateSourceEntityListOwnerRoot(baselineOther, baselineTarget).Entities
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource, _ => string.Empty);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode targetNode = capture.Document.Nodes.Single(node =>
            node.TypeName == typeof(SourceIdentifiedEntity).AssemblyQualifiedName &&
            node.ParentFieldName == nameof(PassiveEntityAliasHolder.Alias));
        Assert.Equal("field", targetNode.ParentKind);

        SourceIdentifiedEntity freshOther = CreateSourceIdentifiedEntity("a00", 20, 0);
        SourceIdentifiedEntity freshTarget = CreateSourceIdentifiedEntity("a00", 10, 0);
        EntityAliasFirstOwnerRoot fresh = new EntityAliasFirstOwnerRoot {
            Entities = CreateSourceEntityListOwnerRoot(freshTarget, freshOther).Entities
        };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Same(freshTarget, fresh.Holder.Alias);
        Assert.Equal(37, freshTarget.Value);
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void FreshComponentIsAuthenticatedByItsTwoWayEntityOwnership() {
        OwnedComponentEntity savedOwner = CreateOwnedComponentEntity();
        savedOwner.Owned.Value = 37;
        OwnedComponentEntity baselineOwner = CreateOwnedComponentEntity();
        ComponentOwnerRoot saved = new ComponentOwnerRoot { Owner = savedOwner };
        ComponentOwnerRoot baseline = new ComponentOwnerRoot { Owner = baselineOwner };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode componentNode = capture.Document.Nodes.Single(node =>
            node.TypeName == typeof(OwnedTestComponent).AssemblyQualifiedName);
        Assert.Equal("array", componentNode.ParentKind);
        Assert.Contains(componentNode.Fields, field =>
            field.Name == "<Entity>k__BackingField" && field.Value.NodeId != 0);

        OwnedComponentEntity freshOwner = CreateOwnedComponentEntity(ownedFirst: true);
        OwnedTestComponent freshOwnedComponent = freshOwner.Owned;
        AkronReconstructionRestore restore = graph.Restore(
            capture.Document,
            new ComponentOwnerRoot { Owner = freshOwner });

        Assert.True(restore.Success, restore.Error);
        Assert.Same(freshOwnedComponent, freshOwner.Owned);
        Assert.Same(freshOwner, GetRuntimeField<Entity>(freshOwner.Owned, "<Entity>k__BackingField"));
        Assert.Equal(37, freshOwner.Owned.Value);
        Assert.Same(freshOwner.Owned, Assert.Single(GetComponentListContents(freshOwner).OfType<OwnedTestComponent>()));
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    // DustGraphic builds its blink Coroutine in BeforeRender, keeps it in a
    // private field, and updates it by hand without adding it to any
    // ComponentList, so no list can vouch for the saved one and a fresh room
    // that never rendered holds null at the slot. A slot saved with dust on
    // camera then refused its whole room over that empty slot.
    [Fact]
    public void AComponentBuiltOnFirstUseRestoresIntoItsFreshOwnersEmptyField() {
        LazyBlinkOwnerRoot saved = new LazyBlinkOwnerRoot { Owner = CreateLazyBlinkOwnerEntity(withBlink: true) };
        LazyBlinkOwnerRoot baseline = new LazyBlinkOwnerRoot { Owner = CreateLazyBlinkOwnerEntity() };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        LazyBlinkOwnerEntity freshOwner = CreateLazyBlinkOwnerEntity();
        LazyBlinkOwnerRoot fresh = new LazyBlinkOwnerRoot { Owner = freshOwner };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.NotNull(freshOwner.Graphic.Blink);
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    // DustGraphic.Eyeballs' shape: the room builds an extra entity on first
    // render and hands it the component that built it. The surplus watcher the
    // fresh room did not build must keep its reference to the fresh component.
    [Fact]
    public void ARuntimeEntityKeepsItsReferenceToTheFreshComponentThatBuiltIt() {
        (SavedSceneRoot saved, LazyBlinkOwnerEntity savedOwner, EyeballsWatcherEntity[] savedWatchers) =
            CreateEyeballsScene(2);
        foreach (EyeballsWatcherEntity watcher in savedWatchers) {
            watcher.Dust = savedOwner.Graphic;
        }
        (SavedSceneRoot baseline, _, _) = CreateEyeballsScene(1);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        (SavedSceneRoot fresh, LazyBlinkOwnerEntity freshOwner, _) = CreateEyeballsScene(1);

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        EyeballsWatcherEntity[] watchers = GetEntityListContents(fresh.Entities)
            .OfType<EyeballsWatcherEntity>()
            .ToArray();
        Assert.Equal(2, watchers.Length);
        Assert.All(watchers, watcher => Assert.Same(freshOwner.Graphic, watcher.Dust));
    }

    // DynamicData's per-type member cache holds compiled FastReflection
    // invokers - anonymous delegates no fresh room can vouch for - and every
    // instance points at the process-wide entry. A mod attaching DynamicData
    // to a room entity removed every Set in the room over those delegates.
    // The cache is a live resource now: capture never walks in, and restore
    // rebinds to this process's own entry for the same target type.
    [Fact]
    public void ADynamicDataMemberCacheRestoresAsThisProcessesOwnEntry() {
        DynamicDataSubject subject = new DynamicDataSubject();
        MonoMod.Utils.DynamicData data = new MonoMod.Utils.DynamicData(subject);
        Assert.Equal(5, data.Get<int>("Exposed"));
        DynamicDataHolder saved = new DynamicDataHolder { Data = data, Value = 3 };
        DynamicDataHolder baseline = new DynamicDataHolder {
            Data = new MonoMod.Utils.DynamicData(new DynamicDataSubject())
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(
            AkronStartPosReconstruction.IsLiveResourceType,
            AkronStartPosReconstruction.GetLiveResourceKey,
            resolveDetachedLiveResource: AkronStartPosReconstruction.ResolveDetachedLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        DynamicDataHolder fresh = new DynamicDataHolder {
            Data = new MonoMod.Utils.DynamicData(new DynamicDataSubject())
        };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Equal(3, fresh.Value);
        object restoredCache = GetRuntimeField<object>(fresh.Data!, "_Cache");
        object liveCache = GetRuntimeField<object>(new MonoMod.Utils.DynamicData(new DynamicDataSubject()), "_Cache");
        Assert.Same(liveCache, restoredCache);
    }

    // Frost Helper's EntityBatcher starts with a shader id in its parameter
    // dictionary, then replaces that value with the live Effect after the first
    // render. A clean-room baseline therefore has null where the saved room has
    // an Effect. The helper's own registry is the stable recipe that bridges
    // that difference without copying native graphics handles.
    [Fact]
    public void ARegisteredEffectMissingFromTheFreshGraphIsResolvedByItsHelperRegistry() {
        Effect effect = (Effect) RuntimeHelpers.GetUninitializedObject(typeof(Effect));
        Effect preindexedEffect = (Effect) RuntimeHelpers.GetUninitializedObject(typeof(Effect));
        RegisteredEffectFixture.Effects["tests/mask"] = effect;
        RegisteredEffectFixture.Effects["tests/preindexed"] = preindexedEffect;
        Assembly[] moduleAssemblies = { typeof(RegisteredEffectFixture).Assembly };
        try {
            AkronReconstructionGraph graph = new AkronReconstructionGraph(
                type => typeof(Effect).IsAssignableFrom(type),
                resource => AkronStartPosReconstruction.GetRegisteredEffectResourceKey(
                    (Effect) resource,
                    moduleAssemblies),
                resolveDetachedLiveResource: (type, typedKey) =>
                    AkronStartPosReconstruction.ResolveRegisteredEffect(
                        type,
                        typedKey.Substring(typedKey.IndexOf('|') + 1),
                        moduleAssemblies),
                hasPortableLiveResourceKey: _ => true);
            RegisteredEffectRoot saved = new RegisteredEffectRoot { Effect = effect };
            AkronReconstructionCapture capture = graph.Capture(
                saved,
                new RegisteredEffectRoot());

            Assert.True(capture.Success, capture.Error);
            AkronReconstructionDocument document = graph.Deserialize(graph.Serialize(capture.Document));
            RegisteredEffectRoot fresh = new RegisteredEffectRoot();

            AkronReconstructionRestore restore = graph.Restore(document, fresh);

            Assert.True(restore.Success, restore.Error);
            Assert.Same(effect, fresh.Effect);
            Assert.True(graph.Verify(document, restore, Array.Empty<string>()).Success);

            // The first registry scan indexes every Effect it sees. A second
            // Effect must use that reverse index without enumerating the same
            // registry again.
            Dictionary<string, Effect> enumerableRegistry = new Dictionary<string, Effect>(
                RegisteredEffectFixture.Effects);
            RegisteredEffectFixture.Effects = new NonEnumerableEffectRegistry(enumerableRegistry);
            Assert.NotEmpty(AkronStartPosReconstruction.GetRegisteredEffectResourceKey(
                preindexedEffect,
                moduleAssemblies));
            RegisteredEffectFixture.Effects = enumerableRegistry;

            // The field catalog is stable for the loaded mod set, but a helper
            // can hot-reload one shader entry. A cached key must follow the live
            // registry value rather than authenticating the replaced Effect.
            Effect replacement = (Effect) RuntimeHelpers.GetUninitializedObject(typeof(Effect));
            RegisteredEffectFixture.Effects["tests/mask"] = replacement;
            Assert.Equal(
                string.Empty,
                AkronStartPosReconstruction.GetRegisteredEffectResourceKey(effect, moduleAssemblies));
            string replacementKey = AkronStartPosReconstruction.GetRegisteredEffectResourceKey(
                replacement,
                moduleAssemblies);
            Assert.NotEmpty(replacementKey);
            Assert.Same(
                replacement,
                AkronStartPosReconstruction.ResolveRegisteredEffect(
                    typeof(Effect),
                    replacementKey,
                    moduleAssemblies));
            Assert.Same(
                replacement,
                AkronStartPosReconstruction.ResolveRegisteredEffect(
                    typeof(Effect),
                    replacementKey,
                    EnumerateModuleAssemblyOnce()));
        } finally {
            RegisteredEffectFixture.Effects = new Dictionary<string, Effect>();
        }

        static IEnumerable<Assembly> EnumerateModuleAssemblyOnce() {
            yield return typeof(RegisteredEffectFixture).Assembly;
        }
    }

    // XaphanHelper's LightningDash shape: HookGen owns one upgrade handler,
    // while the active dash iterator captures a dormant clone of that handler
    // in <>4__this. The clone reaches process-only hook and reflection state,
    // so reconstruction must stop at the owner and rebind it to HookGen's exact
    // registered delegate target.
    [Fact]
    public void AHookIteratorRebindsItsCapturedOwnerFromHookGensRegistry() {
        AkronDeepClone.Initialize();
        HookIteratorOwner liveOwner = new HookIteratorOwner();
        HookIteratorOwner roomOwner = new HookIteratorOwner();
        HookOwnerModule module = new HookOwnerModule();
        module.Owners["lightning-dash"] = liveOwner;
        module.RoomOwners["a-00"] = roomOwner;
        EverestModule[] loadedModules = { module };
        HookRoutine hook = liveOwner.RunHook;
        HookRoutine roomHook = roomOwner.RunHook;
        Type endpointManager = typeof(Hook).Assembly.GetType(
            "MonoMod.RuntimeDetour.HookGen.HookEndpointManager",
            throwOnError: true)!;
        MethodInfo add = endpointManager.GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Single(method => method.Name == "Add" && !method.IsGenericMethod);
        MethodInfo remove = endpointManager.GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Single(method => method.Name == "Remove" && !method.IsGenericMethod);
        MethodInfo source = typeof(StartPosReconstructionTests).GetMethod(
            nameof(HookRoutineSource),
            BindingFlags.Static | BindingFlags.NonPublic)!;
        add.Invoke(null, new object[] { source, hook });
        add.Invoke(null, new object[] { source, roomHook });
        bool liveHookRegistered = true;
        try {
            AkronReconstructionGraph graph = CreateStartPosGraph();
            IReadOnlyDictionary<object, string> setFrameRegistrations =
                AkronStartPosReconstruction.CaptureHookOwnerRegistrations(
                    loadedModules,
                    (_, field) => field.Name == nameof(HookOwnerModule.Owners));
            Assert.True(AkronStartPosReconstruction.AreHookOwnerRegistrationsCurrent(
                setFrameRegistrations,
                setFrameRegistrations));
            HookIteratorOwner replacementOwner = new();
            IReadOnlyDictionary<object, string> replacementRegistrations = new Dictionary<object, string>(
                ReferenceEqualityComparer.Instance) {
                [replacementOwner] = Assert.Single(setFrameRegistrations).Value
            };
            Assert.False(AkronStartPosReconstruction.AreHookOwnerRegistrationsCurrent(
                setFrameRegistrations,
                replacementRegistrations));
            HookIteratorRoot saved;
            using (AkronStartPosReconstruction.UseHookOwnerRegistrations(setFrameRegistrations)) {
                HookIteratorRoot ordinary = (HookIteratorRoot) AkronSaveLoadService.DeepClone(
                    CreateHookIteratorRoot(roomOwner, running: true));
                IEnumerator ordinaryIterator = Assert.Single(
                    GetRuntimeField<Stack<IEnumerator>>(ordinary.Routine, "enumerators"));
                HookIteratorOwner clonedOrdinaryOwner = GetRuntimeField<HookIteratorOwner>(
                    ordinaryIterator,
                    "<>4__this");
                Assert.NotSame(liveOwner, clonedOrdinaryOwner);
                Assert.NotSame(roomOwner, clonedOrdinaryOwner);

                saved = (HookIteratorRoot) AkronSaveLoadService.DeepClone(
                    CreateHookIteratorRoot(liveOwner, running: true));
                IEnumerator savedIterator = Assert.Single(
                    GetRuntimeField<Stack<IEnumerator>>(saved.Routine, "enumerators"));
                Assert.Same(liveOwner, GetRuntimeField<HookIteratorOwner>(savedIterator, "<>4__this"));
            }
            remove.Invoke(null, new object[] { source, hook });
            liveHookRegistered = false;

            // Persistence can run after Set on a worker. It must use the exact
            // ownership snapshot that preserved the saved clone even if the mod
            // has since removed or replaced its live hook.
            HookIteratorRoot baseline = CreateHookIteratorRoot(null, running: false);
            string serialized;
            using (AkronStartPosReconstruction.UseHookOwnerRegistrations(setFrameRegistrations)) {
                AkronReconstructionCapture capture = graph.Capture(saved, baseline);
                Assert.True(capture.Success, capture.Error);
                AkronReconstructionNode ownerNode = Assert.Single(
                    capture.Document.Nodes,
                    node => node.TypeName == typeof(HookIteratorOwner).AssemblyQualifiedName);
                Assert.Equal("anchor", ownerNode.Kind);
                serialized = graph.Serialize(capture.Document);
            }

            // The snapshot is read before its fresh room has a chance to
            // register room-scoped hooks. Its portable owner key is safe to
            // validate now, but exact runtime resolution belongs to restore.
            AkronReconstructionDocument document = graph.Deserialize(serialized);
            add.Invoke(null, new object[] { source, hook });
            liveHookRegistered = true;
            HookIteratorRoot fresh = CreateHookIteratorRoot(null, running: false);

            AkronReconstructionRestore restore;
            IReadOnlyDictionary<object, string> restoreRegistrations =
                AkronStartPosReconstruction.CaptureHookOwnerRegistrations(
                    loadedModules,
                    (_, field) => field.Name == nameof(HookOwnerModule.Owners));
            using (AkronStartPosReconstruction.UseHookOwnerRegistrations(restoreRegistrations)) {
                restore = graph.Restore(document, fresh);
            }

            Assert.True(restore.Success, restore.Error);
            IEnumerator iterator = Assert.Single(GetRuntimeField<Stack<IEnumerator>>(fresh.Routine, "enumerators"));
            Assert.Same(liveOwner, GetRuntimeField<HookIteratorOwner>(iterator, "<>4__this"));
        } finally {
            remove.Invoke(null, new object[] { source, roomHook });
            if (liveHookRegistered) {
                remove.Invoke(null, new object[] { source, hook });
            }
        }
    }

    private sealed class HookOwnerModule : EverestModule {
        public readonly Dictionary<string, HookIteratorOwner> Owners =
            new Dictionary<string, HookIteratorOwner>();
        public readonly Dictionary<string, HookIteratorOwner> RoomOwners =
            new Dictionary<string, HookIteratorOwner>();

        public override void Load() {
        }

        public override void Unload() {
        }
    }

    private static IEnumerator HookRoutineSource() {
        yield break;
    }

    private static HookIteratorRoot CreateHookIteratorRoot(HookIteratorOwner? owner, bool running) {
        Coroutine routine = (Coroutine) RuntimeHelpers.GetUninitializedObject(typeof(Coroutine));
        Stack<IEnumerator> iterators = new Stack<IEnumerator>();
        if (running) {
            IEnumerator iterator = owner!.RunHook(null!);
            Assert.True(iterator.MoveNext());
            iterators.Push(iterator);
        }
        SetRuntimeField(routine, "enumerators", iterators);
        return new HookIteratorRoot { Routine = routine };
    }

    // Level.StartCutscene stores the skip callback and nothing in Celeste ever
    // clears it, so a slot set after any skipped cutscene dragged the finished
    // cutscene entity into the graph through a callback that can never fire
    // again. The dormant clone drops the dead callback; a running cutscene's
    // callback is real state and stays.
    [Fact]
    public void ADeadCutsceneSkipCallbackIsDroppedFromTheDormantClone() {
        Level level = (Level) RuntimeHelpers.GetUninitializedObject(typeof(Level));
        Action<Level> callback = _ => { };
        SetRuntimeField(level, "onCutsceneSkip", callback);
        level.InCutscene = false;

        AkronSaveLoadService.ClearDeadCutsceneSkipCallback(level);
        Assert.Null(GetRuntimeField<Action<Level>>(level, "onCutsceneSkip"));

        SetRuntimeField(level, "onCutsceneSkip", callback);
        level.InCutscene = true;
        AkronSaveLoadService.ClearDeadCutsceneSkipCallback(level);
        Assert.Same(callback, GetRuntimeField<Action<Level>>(level, "onCutsceneSkip"));
    }

    // CrushBlock's shape: the saved attack routine is mid-flight and its
    // iterator hoisted a lambda closure. The fresh room's routine is idle, so
    // no structural path vouches for the closure; the iterator's own direct
    // owner proof carries it. A slot set while riding a punched Kevin was
    // refused over exactly this node.
    [Fact]
    public void AMidFlightIteratorClosureRestoresWhenTheFreshRoutineIsIdle() {
        (SavedSceneRoot saved, ClosureRoutineEntity savedOwner) = CreateClosureRoutineScene(midFlight: true);
        savedOwner.Steps = 3;
        (SavedSceneRoot baseline, _) = CreateClosureRoutineScene(midFlight: false);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        (SavedSceneRoot fresh, ClosureRoutineEntity freshOwner) = CreateClosureRoutineScene(midFlight: false);

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Equal(3, freshOwner.Steps);
        Assert.Single(GetRuntimeField<Stack<IEnumerator>>(freshOwner.Routine!, "enumerators"));
    }

    [Fact]
    public void IteratorClosureCanRetainARuntimeComponentOwnedByTheSameEntity() {
        (SavedSceneRoot saved, _) = CreateClosureRoutineScene(midFlight: true, withOwnedComponent: true);
        (SavedSceneRoot baseline, _) = CreateClosureRoutineScene(midFlight: false, withOwnedComponent: true);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode componentNode = capture.Document.Nodes.Single(node =>
            node.TypeName == typeof(OwnedTestComponent).AssemblyQualifiedName);
        Assert.True(componentNode.Path.Contains("<>8__", StringComparison.Ordinal), componentNode.Path);
        (SavedSceneRoot fresh, ClosureRoutineEntity freshOwner) =
            CreateClosureRoutineScene(midFlight: false, withOwnedComponent: true);

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        IEnumerator iterator = GetRuntimeField<Stack<IEnumerator>>(
            freshOwner.Routine!,
            "enumerators").Peek();
        object closure = GetRuntimeField<object>(iterator, "<>8__1");
        OwnedTestComponent component = GetRuntimeField<OwnedTestComponent>(closure, "component");
        Assert.Same(freshOwner, GetRuntimeField<Entity>(component, "<Entity>k__BackingField"));
    }

    // The containment side of the closure-lambda licence: a document that moves
    // the routine's callback into another entity's delegate field is refused,
    // because the delegate no longer lives inside the entity that owns the
    // routine.
    [Fact]
    public void AClosureLambdaRelocatedToAnotherEntityIsRefused() {
        (SavedSceneRoot saved, ClosureRoutineEntity savedOwner) = CreateClosureRoutineScene(midFlight: true);
        RelocatedCallbackEntity thief = CreateUninitializedEntity<RelocatedCallbackEntity>();
        InitializeEmptyComponentList(thief);
        SetRuntimeField(thief, "<Scene>k__BackingField", saved.Scene);
        SetRuntimeField(thief, "<SourceId>k__BackingField", CreateEntityId("a00", 11));
        IEnumerator iterator = GetRuntimeField<Stack<IEnumerator>>(savedOwner.Routine!, "enumerators").Peek();
        object closure = GetRuntimeField<object>(iterator, "<>8__1");
        MethodInfo lambda = closure.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Single(candidate => candidate.Name.Contains("b__", StringComparison.Ordinal));
        thief.Stolen = (Action) lambda.CreateDelegate(typeof(Action), closure);
        AddDetachedEntity(saved.Entities, thief);
        SavedSceneRoot baseline = BuildRelocatedCallbackBaseline();

        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        SavedSceneRoot fresh = BuildRelocatedCallbackBaseline();

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.False(restore.Success);
        Assert.Contains("not authentic", restore.Error);
    }

    private static SavedSceneRoot BuildRelocatedCallbackBaseline() {
        (SavedSceneRoot root, _) = CreateClosureRoutineScene(midFlight: false);
        RelocatedCallbackEntity bystander = CreateUninitializedEntity<RelocatedCallbackEntity>();
        InitializeEmptyComponentList(bystander);
        SetRuntimeField(bystander, "<Scene>k__BackingField", root.Scene);
        SetRuntimeField(bystander, "<SourceId>k__BackingField", CreateEntityId("a00", 11));
        AddDetachedEntity(root.Entities, bystander);
        return root;
    }

    // LightningRenderer's shape: a saved bolt mid-flight against a fresh bolt
    // whose own routine already finished. The fresh room's empty stack is
    // expected silence, not missing evidence, so the iterator restores on its
    // captured owner's proof.
    [Fact]
    public void AMidFlightRoutineRestoresIntoAFreshBoltWhoseOwnRoutineFinished() {
        (SavedSceneRoot saved, BoltOwnerEntity savedOwner) = CreateBoltScene(midFlight: true);
        savedOwner.Bolts[0].Flashes = 4;
        (SavedSceneRoot baseline, _) = CreateBoltScene(midFlight: false);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        (SavedSceneRoot fresh, BoltOwnerEntity freshOwner) = CreateBoltScene(midFlight: false);

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Equal(4, freshOwner.Bolts[0].Flashes);
        Assert.Single(GetRuntimeField<Stack<IEnumerator>>(freshOwner.Bolts[0].Routine, "enumerators"));
    }

    // The licence stays narrow the same way the list-owned rule keeps it: a
    // component that owns process state through IDisposable never enters a
    // room through an empty field slot.
    [Fact]
    public void ADisposableComponentIsNotRestoredIntoAnEmptyFieldSlot() {
        LazyDisposableOwnerRoot saved = new LazyDisposableOwnerRoot { Owner = CreateLazyDisposableOwnerEntity(withBlink: true) };
        LazyDisposableOwnerRoot baseline = new LazyDisposableOwnerRoot { Owner = CreateLazyDisposableOwnerEntity() };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);

        AkronReconstructionRestore restore = graph.Restore(
            capture.Document,
            new LazyDisposableOwnerRoot { Owner = CreateLazyDisposableOwnerEntity() });

        Assert.False(restore.Success);
        Assert.Contains("not authentic", restore.Error);
    }

    [Fact]
    public void FreshComponentCanRestoreThroughAReorderedFreshArrayMembership() {
        OwnedComponentEntity savedOwner = CreateOwnedComponentEntity();
        ComponentArrayAliasRoot saved = new ComponentArrayAliasRoot {
            Owner = savedOwner,
            Components = new OwnedTestComponent[] { null!, savedOwner.Owned }
        };
        OwnedComponentEntity baselineOwner = CreateOwnedComponentEntity();
        ComponentArrayAliasRoot baseline = new ComponentArrayAliasRoot {
            Owner = baselineOwner,
            Components = new OwnedTestComponent[] { baselineOwner.Owned, null! }
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);

        OwnedComponentEntity freshOwner = CreateOwnedComponentEntity();
        OwnedTestComponent freshComponent = freshOwner.Owned;
        ComponentArrayAliasRoot fresh = new ComponentArrayAliasRoot {
            Owner = freshOwner,
            Components = new OwnedTestComponent[] { freshComponent, null! }
        };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Null(fresh.Components[0]);
        Assert.Same(freshComponent, fresh.Components[1]);
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void FreshComponentCanRestoreIntoItsSceneRendererDerivedIndex() {
        RendererComponentIndexRoot saved = CreateRendererComponentIndexRoot(includeLightInRenderer: true);
        RendererComponentIndexRoot baseline = CreateRendererComponentIndexRoot(includeLightInRenderer: false);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        RendererComponentIndexRoot fresh = CreateRendererComponentIndexRoot(includeLightInRenderer: false);
        VertexLight freshLight = Assert.IsType<VertexLight>(Assert.Single(GetComponentListContents(fresh.Entity)));

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Same(freshLight, GetLightingRendererLights(fresh.Renderer)[5]);
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void FreshComponentOwnerCanAppearAfterAnEarlierSavedAlias() {
        OwnedComponentEntity savedOwner = CreateOwnedComponentEntity();
        ComponentAliasFirstRoot saved = new ComponentAliasFirstRoot {
            Alias = savedOwner.Owned,
            Owner = savedOwner
        };
        ComponentAliasFirstRoot baseline = new ComponentAliasFirstRoot {
            Owner = CreateOwnedComponentEntity()
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        OwnedComponentEntity freshOwner = CreateOwnedComponentEntity();
        ComponentAliasFirstRoot fresh = new ComponentAliasFirstRoot { Owner = freshOwner };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Same(freshOwner.Owned, fresh.Alias);
        Assert.Same(freshOwner, GetRuntimeField<Entity>(freshOwner.Owned, "<Entity>k__BackingField"));
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void FreshComponentCanRestoreAnExactCapturedFreshObject() {
        ExactSlotObject savedTarget = new ExactSlotObject { Value = 37 };
        OwnedComponentEntity savedOwner = CreateOwnedComponentEntity();
        savedOwner.Owned.Captured = savedTarget;
        ExactSlotObject baselineTarget = new ExactSlotObject();
        OwnedComponentEntity baselineOwner = CreateOwnedComponentEntity();
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new ComponentCapturedFreshRoot { Target = savedTarget, Owner = savedOwner },
            new ComponentCapturedFreshRoot { Target = baselineTarget, Owner = baselineOwner });
        Assert.True(capture.Success, capture.Error);
        ExactSlotObject freshTarget = new ExactSlotObject();
        OwnedComponentEntity freshOwner = CreateOwnedComponentEntity();
        ComponentCapturedFreshRoot fresh = new ComponentCapturedFreshRoot {
            Target = freshTarget,
            Owner = freshOwner
        };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Same(freshTarget, freshOwner.Owned.Captured);
        Assert.Equal(37, freshTarget.Value);
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void FreshComponentCanRestoreThroughItsSceneTrackerAlias() {
        TrackedComponentEntity savedOwner = CreateTrackedComponentEntity();
        TrackedComponentEntity baselineOwner = CreateTrackedComponentEntity();
        Scene savedScene = CreateTrackedComponentScene(savedOwner, includeComponent: true);
        Scene baselineScene = CreateTrackedComponentScene(baselineOwner, includeComponent: false);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(
            AkronStartPosReconstruction.IsLiveResourceType,
            resource => ((Type) resource).AssemblyQualifiedName,
            null,
            AkronStartPosReconstruction.ResolveDetachedLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new TrackerComponentAliasRoot {
                Owner = savedOwner,
                Scene = savedScene
            },
            new TrackerComponentAliasRoot {
                Owner = baselineOwner,
                Scene = baselineScene
            });
        Assert.True(capture.Success, capture.Error);
        TrackedComponentEntity freshOwner = CreateTrackedComponentEntity(ownedFirst: true);
        LevelEndingHook freshComponent = freshOwner.Owned;
        Scene freshScene = CreateTrackedComponentScene(freshOwner, includeComponent: false);
        TrackerComponentAliasRoot fresh = new TrackerComponentAliasRoot {
            Owner = freshOwner,
            Scene = freshScene
        };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Tracker tracker = GetRuntimeField<Tracker>(fresh.Scene, "<Tracker>k__BackingField");
        Dictionary<Type, List<Component>> components =
            GetRuntimeField<Dictionary<Type, List<Component>>>(tracker, "<Components>k__BackingField");
        Assert.Equal(2, components[typeof(LevelEndingHook)].Count);
        Assert.All(
            components[typeof(LevelEndingHook)],
            component => Assert.Same(freshComponent, component));
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void ExactTypedFreshAliasWinsOverAnEarlierBaseTypedAlias() {
        TypedAliasEntity savedEntity = CreateUninitializedEntity<TypedAliasEntity>();
        InitializeEmptyComponentList(savedEntity);
        savedEntity.Value = 37;
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new ExactTypedAliasRoot { General = savedEntity, Exact = savedEntity },
            CreateExactTypedAliasRoot(11, 12));
        Assert.True(capture.Success, capture.Error);
        ExactTypedAliasRoot fresh = CreateExactTypedAliasRoot(21, 22);
        TypedAliasEntity expected = fresh.Exact;

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Same(expected, fresh.General);
        Assert.Same(expected, fresh.Exact);
        Assert.Equal(37, expected.Value);
    }

    [Fact]
    public void FreshComponentCannotBeAliasedFromAnUnrelatedSavedObject() {
        OwnedComponentEntity savedOwner = CreateOwnedComponentEntity();
        ComponentOwnerRoot saved = new ComponentOwnerRoot {
            Owner = savedOwner,
            UnrelatedAlias = savedOwner.Owned
        };
        ComponentOwnerRoot baseline = new ComponentOwnerRoot {
            Owner = CreateOwnedComponentEntity()
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);

        AkronReconstructionRestore restore = graph.Restore(
            capture.Document,
            new ComponentOwnerRoot { Owner = CreateOwnedComponentEntity() });

        Assert.False(restore.Success);
        Assert.Contains("reference edge is not authentic", restore.Error);
    }

    [Fact]
    public void MissingSavedComponentRestoresThroughItsFreshEntityOwnership() {
        MissingComponentOwnerEntity savedOwner = CreateMissingComponentOwnerEntity(includeComponent: true);
        MissingComponentOwnerEntity baselineOwner = CreateMissingComponentOwnerEntity(includeComponent: false);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new MissingComponentRoot { Owner = savedOwner },
            new MissingComponentRoot { Owner = baselineOwner });
        Assert.True(capture.Success, capture.Error);
        MissingComponentOwnerEntity freshOwner = CreateMissingComponentOwnerEntity(includeComponent: false);

        AkronReconstructionRestore restore = graph.Restore(
            capture.Document,
            new MissingComponentRoot { Owner = freshOwner });

        Assert.True(restore.Success, restore.Error);
        MissingOwnedComponent restored = Assert.Single(freshOwner.Cached);
        Assert.Equal(37, restored.Value);
        Assert.Same(freshOwner, GetRuntimeField<Entity>(restored, "<Entity>k__BackingField"));
        Assert.Same(restored, Assert.Single(GetComponentListContents(freshOwner).OfType<MissingOwnedComponent>()));
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void FreshEntityListKeepsItsEntityAcrossInternalMembershipAliases() {
        (EntityListOwnerRoot saved, _) = CreateEntityListOwnerRoot(targetFirst: false);
        (EntityListOwnerRoot baseline, _) = CreateEntityListOwnerRoot(targetFirst: false);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode rootNode = capture.Document.Nodes.Single(node => node.Id == capture.Document.RootNodeId);
        AkronReconstructionValue canonicalAlias = rootNode.Fields.Single(field =>
            field.Name == nameof(EntityListOwnerRoot.CanonicalAlias)).Value;
        AkronReconstructionNode targetNode = capture.Document.Nodes.Single(node => node.Id == canonicalAlias.NodeId);
        Assert.Equal("array", targetNode.ParentKind);
        (EntityListOwnerRoot fresh, Entity freshTarget) = CreateEntityListOwnerRoot(targetFirst: true);

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Same(freshTarget, fresh.CanonicalAlias);
        Assert.Contains(GetEntityListContents(fresh.Entities), entity => ReferenceEquals(entity, freshTarget));
        HashSet<Entity> current = (HashSet<Entity>) typeof(EntityList)
            .GetField("current", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(fresh.Entities)!;
        Assert.Contains(freshTarget, current);
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void FreshEntityListCannotAliasAnEntityFromAnUnrelatedSavedObject() {
        (EntityListOwnerRoot saved, Entity savedTarget) = CreateEntityListOwnerRoot(targetFirst: false);
        saved.UnrelatedAlias = savedTarget;
        (EntityListOwnerRoot baseline, _) = CreateEntityListOwnerRoot(targetFirst: false);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        (EntityListOwnerRoot fresh, _) = CreateEntityListOwnerRoot(targetFirst: true);

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.False(restore.Success);
        Assert.Contains("reference edge is not authentic", restore.Error);
    }

    [Fact]
    public void FreshEntityCanRestoreAnExactTypedPeerLinkWithinItsEntityList() {
        PeerTargetEntity savedTarget = CreatePeerTargetEntity("a00", 20);
        PeerLinkEntity savedOwner = CreatePeerLinkEntity("a00", 10, savedTarget);
        SourceEntityListOwnerRoot saved = CreateSourceEntityListOwnerRoot(savedOwner, savedTarget);
        PeerLinkEntity baselineOwner = CreatePeerLinkEntity("a00", 10, null);
        PeerTargetEntity baselineTarget = CreatePeerTargetEntity("a00", 20);
        SourceEntityListOwnerRoot baseline = CreateSourceEntityListOwnerRoot(baselineOwner, baselineTarget);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource, _ => string.Empty);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        PeerTargetEntity freshTarget = CreatePeerTargetEntity("a00", 20);
        PeerLinkEntity freshOwner = CreatePeerLinkEntity("a00", 10, null);
        SourceEntityListOwnerRoot fresh = CreateSourceEntityListOwnerRoot(freshTarget, freshOwner);

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Same(freshTarget, freshOwner.Peer);
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    // The crossed population through a named field, and the reason
    // RefuseAnEdgeThatDropsAFreshObjectTheDocumentKeeps asks whether the displaced
    // object is one the document keeps rather than only whether the slot is occupied.
    //
    // The saved frame deleted entity 22 and this reload did not build entity 21, so the
    // fresh owner cached 22 in the field the document wants 21 in. The saved population
    // is the truth: 21 is rebuilt, 22 is dropped, and the owner ends up pointing at 21.
    // Nothing in the document keeps 22 - no node is paired with it - so the write is not
    // a contradiction, and refusing on an occupied slot alone would refuse this room.
    [Fact]
    public void AFreshEntityTakesBackThePeerTheSavedFrameKeptWhenTheReloadCachedAnother() {
        PeerTargetEntity savedKeptPeer = CreatePeerTargetEntity("a00", 20);
        PeerTargetEntity savedAbsentPeer = CreatePeerTargetEntity("a00", 21);
        PeerLinkEntity savedKeptOwner = CreatePeerLinkEntity("a00", 10, savedKeptPeer);
        PeerLinkEntity savedCrossedOwner = CreatePeerLinkEntity("a00", 11, savedAbsentPeer);
        SourceEntityListOwnerRoot saved = CreateSourceEntityListOwnerRoot(
            savedKeptOwner, savedKeptPeer, savedCrossedOwner, savedAbsentPeer);
        PeerTargetEntity baselineKeptPeer = CreatePeerTargetEntity("a00", 20);
        PeerTargetEntity baselineAbsentPeer = CreatePeerTargetEntity("a00", 21);
        SourceEntityListOwnerRoot baseline = CreateSourceEntityListOwnerRoot(
            CreatePeerLinkEntity("a00", 10, baselineKeptPeer),
            baselineKeptPeer,
            CreatePeerLinkEntity("a00", 11, baselineAbsentPeer),
            baselineAbsentPeer,
            // The one the player destroyed before the slot was set.
            CreatePeerTargetEntity("a00", 22));
        PeerTargetEntity freshKeptPeer = CreatePeerTargetEntity("a00", 20);
        PeerTargetEntity freshDeletedPeer = CreatePeerTargetEntity("a00", 22);
        PeerLinkEntity freshKeptOwner = CreatePeerLinkEntity("a00", 10, freshKeptPeer);
        PeerLinkEntity freshCrossedOwner = CreatePeerLinkEntity("a00", 11, freshDeletedPeer);
        SourceEntityListOwnerRoot fresh = CreateSourceEntityListOwnerRoot(
            freshKeptOwner, freshKeptPeer, freshCrossedOwner, freshDeletedPeer);
        AkronReconstructionGraph graph = CreateMapAwareGraph(new TestMapPlacement()
            .Place(baseline, "a00", 10, 11, 20, 21, 22)
            .Place(fresh, "a00", 10, 11, 20, 21, 22));
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        capture.Document.Room = "a00";

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        // The owner the reload did wire keeps the room's own peer.
        Assert.Same(freshKeptPeer, freshKeptOwner.Peer);
        // The other one is rebuilt and takes the slot the reload had filled with 22.
        PeerTargetEntity rebuiltPeer = Assert.IsType<PeerTargetEntity>(freshCrossedOwner.Peer);
        Assert.Equal(21, GetRuntimeField<EntityID>(rebuiltPeer, "<SourceId>k__BackingField").ID);
        // Monocle.Entity is IEnumerable<Component>, so xUnit compares two entities by
        // their components rather than by reference. Ask for the reference directly.
        Assert.DoesNotContain(
            GetEntityListContents(fresh.Entities),
            entity => ReferenceEquals(entity, freshDeletedPeer));
        Assert.Contains(
            GetEntityListContents(fresh.Entities),
            entity => ReferenceEquals(entity, rebuiltPeer));
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
        Assert.True(graph.Reapply(capture.Document, restore).Success);
    }

    [Fact]
    public void FreshEntityCanRestoreIntoAPeerCollectionOwnedByTheSameEntityList() {
        PeerTargetEntity savedTarget = CreatePeerTargetEntity("a00", 20);
        PeerCollectionOwnerEntity savedOwner = CreatePeerCollectionOwnerEntity(
            "a00",
            10,
            savedTarget,
            savedTarget);
        SourceEntityListOwnerRoot saved = CreateSourceEntityListOwnerRoot(savedOwner, savedTarget);
        PeerTargetEntity baselineTarget = CreatePeerTargetEntity("a00", 20);
        PeerCollectionOwnerEntity baselineOwner = CreatePeerCollectionOwnerEntity("a00", 10);
        SourceEntityListOwnerRoot baseline = CreateSourceEntityListOwnerRoot(baselineOwner, baselineTarget);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource, _ => string.Empty);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        PeerTargetEntity freshTarget = CreatePeerTargetEntity("a00", 20);
        PeerCollectionOwnerEntity freshOwner = CreatePeerCollectionOwnerEntity("a00", 10);
        SourceEntityListOwnerRoot fresh = CreateSourceEntityListOwnerRoot(freshTarget, freshOwner);

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Equal(2, freshOwner.Peers.Count);
        Assert.All(freshOwner.Peers, peer => Assert.Same(freshTarget, peer));
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void RepeatedEntitiesWithoutSourceIdsMatchByEntityListTypeOrdinal() {
        ClutterLinkedEntity savedFirst = CreateClutterLinkedEntity(11);
        ClutterLinkedEntity savedSecond = CreateClutterLinkedEntity(22);
        ClutterLinkedEntity savedThird = CreateClutterLinkedEntity(33);
        savedFirst.HasBelow[savedSecond] = true;
        savedSecond.HasBelow[savedThird] = true;
        savedSecond.Above.Add(savedFirst);
        savedThird.Above.Add(savedSecond);
        SourceEntityListOwnerRoot saved = CreateSourceEntityListOwnerRoot(
            savedFirst,
            savedSecond,
            savedThird);
        SourceEntityListOwnerRoot baseline = CreateSourceEntityListOwnerRoot(
            CreateClutterLinkedEntity(0),
            CreateClutterLinkedEntity(0),
            CreateClutterLinkedEntity(0));
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource, _ => string.Empty);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        ClutterLinkedEntity freshFirst = CreateClutterLinkedEntity(0);
        ClutterLinkedEntity freshSecond = CreateClutterLinkedEntity(0);
        ClutterLinkedEntity freshThird = CreateClutterLinkedEntity(0);
        SourceEntityListOwnerRoot fresh = CreateSourceEntityListOwnerRoot(
            freshFirst,
            freshSecond,
            freshThird);

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Equal(new[] { 11, 22, 33 }, new[] { freshFirst.Value, freshSecond.Value, freshThird.Value });
        Assert.Same(freshSecond, Assert.Single(freshFirst.HasBelow).Key);
        Assert.Same(freshThird, Assert.Single(freshSecond.HasBelow).Key);
        Assert.Same(freshFirst, Assert.Single(freshSecond.Above));
        Assert.Same(freshSecond, Assert.Single(freshThird.Above));
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void RepeatedGeneratedEntitiesRestoreWhenFreshTypePopulationDiffers() {
        (SavedSceneRoot saved, ClutterLinkedEntity[] savedEntities) =
            CreateClutterLinkedScene(11, 22, 33);
        ClutterLinkedEntity savedFirst = savedEntities[0];
        ClutterLinkedEntity savedSecond = savedEntities[1];
        ClutterLinkedEntity savedThird = savedEntities[2];
        savedFirst.HasBelow[savedSecond] = true;
        savedSecond.Above.Add(savedThird);
        (SavedSceneRoot baseline, _) = CreateClutterLinkedScene(0, 0, 0);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource, _ => string.Empty);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        (SavedSceneRoot fresh, ClutterLinkedEntity[] freshEntities) = CreateClutterLinkedScene(0, 0);

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        ClutterLinkedEntity[] restored = GetEntityListContents(fresh.Entities)
            .Cast<ClutterLinkedEntity>()
            .ToArray();
        Assert.Equal(new[] { 11, 22, 33 }, restored.Select(entity => entity.Value));
        Assert.Equal(new[] { 11f, 22f, 33f }, restored.Select(entity => GetRuntimeField<float>(
            Assert.IsType<Hitbox>(GetRuntimeField<Collider>(entity, "collider")),
            "<Width>k__BackingField")));
        Assert.Contains(restored, entity => freshEntities.All(freshEntity =>
            !ReferenceEquals(entity, freshEntity)));
        Assert.Same(restored[1], Assert.Single(restored[0].HasBelow).Key);
        Assert.Empty(restored[1].HasBelow);
        Assert.Same(restored[2], Assert.Single(restored[1].Above));
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    // A hash container places its live entries at positions derived from
    // per-process hash codes - AkronHashIndex.Rebuild exists because those
    // positions do not survive a process change - so the saved process's entry
    // indices never match the fresh process's for the same logical set. One
    // process hands out one hash code per object, so the saved side shifts its
    // entries by inserting and removing a dummy first, which is what a real
    // cross-process document looks like to the authenticity keys. Celestial
    // Resort's clutter blocks cross-reference each other through exactly such
    // sets and their whole room was refused over the entry position.
    [Fact]
    public void HashEntryPositionsFromTheSavedProcessDoNotDecideAuthenticity() {
        (SavedSceneRoot saved, ClutterLinkedEntity[] savedEntities) =
            CreateClutterLinkedScene(11, 22, 33);
        ClutterLinkedEntity dummy = CreateClutterLinkedEntity(99);
        savedEntities[0].HasBelow[dummy] = true;
        savedEntities[0].HasBelow[savedEntities[1]] = true;
        savedEntities[0].HasBelow.Remove(dummy);
        savedEntities[1].Above.Add(savedEntities[2]);
        (SavedSceneRoot baseline, _) = CreateClutterLinkedScene(0, 0, 0);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource, _ => string.Empty);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        (SavedSceneRoot fresh, _) = CreateClutterLinkedScene(0, 0);

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        ClutterLinkedEntity[] restored = GetEntityListContents(fresh.Entities)
            .Cast<ClutterLinkedEntity>()
            .ToArray();
        Assert.Equal(new[] { 11, 22, 33 }, restored.Select(entity => entity.Value));
        Assert.Same(restored[1], Assert.Single(restored[0].HasBelow).Key);
        Assert.Same(restored[2], Assert.Single(restored[1].Above));
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void GeneratedEntityTypeAbsentFromFreshEntityListFailsClosed() {
        (SavedSceneRoot saved, _) = CreateClutterLinkedScene(11, 22);
        (SavedSceneRoot baseline, _) = CreateClutterLinkedScene(0, 0);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource, _ => string.Empty);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        (SavedSceneRoot fresh, _) = CreateClutterLinkedScene();

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.False(restore.Success);
        Assert.Contains("reconstructed type is not authentic", restore.Error);
        Assert.Empty(GetEntityListContents(fresh.Entities));
    }

    [Fact]
    public void FreshEntityCannotRestoreAPeerLinkAcrossDifferentEntityLists() {
        PeerTargetEntity savedTarget = CreatePeerTargetEntity("b00", 20);
        PeerLinkEntity savedOwner = CreatePeerLinkEntity("a00", 10, savedTarget);
        TwoEntityListsRoot saved = new TwoEntityListsRoot {
            First = CreateSourceEntityListOwnerRoot(savedOwner).Entities,
            Second = CreateSourceEntityListOwnerRoot(savedTarget).Entities
        };
        TwoEntityListsRoot baseline = new TwoEntityListsRoot {
            First = CreateSourceEntityListOwnerRoot(CreatePeerLinkEntity("a00", 10, null)).Entities,
            Second = CreateSourceEntityListOwnerRoot(CreatePeerTargetEntity("b00", 20)).Entities
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource, _ => string.Empty);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        TwoEntityListsRoot fresh = new TwoEntityListsRoot {
            First = CreateSourceEntityListOwnerRoot(CreatePeerLinkEntity("a00", 10, null)).Entities,
            Second = CreateSourceEntityListOwnerRoot(CreatePeerTargetEntity("b00", 20)).Entities
        };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.False(restore.Success);
        Assert.Contains("reference edge is not authentic", restore.Error);
    }

    [Fact]
    public void FreshEntityCanRestoreItsMissingExactNestedState() {
        NestedStateOwnerEntity savedOwner = CreateNestedStateOwnerEntity("a00", 10, 37);
        NestedStateOwnerEntity savedOther = CreateNestedStateOwnerEntity("a00", 20, 81);
        SourceEntityListOwnerRoot saved = CreateSourceEntityListOwnerRoot(savedOwner, savedOther);
        NestedStateOwnerEntity baselineOwner = CreateNestedStateOwnerEntity("a00", 10, null);
        NestedStateOwnerEntity baselineOther = CreateNestedStateOwnerEntity("a00", 20, null);
        SourceEntityListOwnerRoot baseline = CreateSourceEntityListOwnerRoot(baselineOwner, baselineOther);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource, _ => string.Empty);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        NestedStateOwnerEntity freshOther = CreateNestedStateOwnerEntity("a00", 20, null);
        NestedStateOwnerEntity freshOwner = CreateNestedStateOwnerEntity("a00", 10, null);
        SourceEntityListOwnerRoot fresh = CreateSourceEntityListOwnerRoot(freshOther, freshOwner);

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.NotNull(freshOwner.State);
        Assert.Equal(37, freshOwner.State.Value);
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void FreshEntityArrayKeepsItsExactChildrenWhenEntityOrderChanges() {
        ColliderArrayOwnerEntity savedFirst = CreateColliderArrayOwnerEntity("a00", 10, 6f);
        ColliderArrayOwnerEntity savedSecond = CreateColliderArrayOwnerEntity("a00", 20, 7f);
        SourceEntityListOwnerRoot saved = CreateSourceEntityListOwnerRoot(savedFirst, savedSecond);
        SourceEntityListOwnerRoot baseline = CreateSourceEntityListOwnerRoot(
            CreateColliderArrayOwnerEntity("a00", 10, 1f),
            CreateColliderArrayOwnerEntity("a00", 20, 2f));
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource, _ => string.Empty);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        ColliderArrayOwnerEntity freshSecond = CreateColliderArrayOwnerEntity("a00", 20, 12f);
        ColliderArrayOwnerEntity freshFirst = CreateColliderArrayOwnerEntity("a00", 10, 11f);
        Circle freshFirstCircle = (Circle) freshFirst.Colliders[0];
        Circle freshSecondCircle = (Circle) freshSecond.Colliders[0];
        SourceEntityListOwnerRoot fresh = CreateSourceEntityListOwnerRoot(freshSecond, freshFirst);

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Same(freshFirstCircle, freshFirst.Colliders[0]);
        Assert.Same(freshSecondCircle, freshSecond.Colliders[0]);
        Assert.Equal(6f, freshFirstCircle.Radius);
        Assert.Equal(7f, freshSecondCircle.Radius);
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void FreshAliasedEntityKeepsItsExactArrayChildrenWhenAliasOrderChanges() {
        ColliderArrayOwnerEntity savedFirst = CreateColliderArrayOwnerEntity("a00", 10, 6f);
        ColliderArrayOwnerEntity savedSecond = CreateColliderArrayOwnerEntity("a00", 20, 7f);
        AliasedColliderEntityRoot saved = CreateAliasedColliderEntityRoot(savedFirst, savedSecond);
        AliasedColliderEntityRoot baseline = CreateAliasedColliderEntityRoot(
            CreateColliderArrayOwnerEntity("a00", 10, 1f),
            CreateColliderArrayOwnerEntity("a00", 20, 2f));
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource, _ => string.Empty);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        ColliderArrayOwnerEntity freshSecond = CreateColliderArrayOwnerEntity("a00", 20, 12f);
        ColliderArrayOwnerEntity freshFirst = CreateColliderArrayOwnerEntity("a00", 10, 11f);
        Circle freshFirstCircle = (Circle) freshFirst.Colliders[0];
        Circle freshSecondCircle = (Circle) freshSecond.Colliders[0];
        AliasedColliderEntityRoot fresh = CreateAliasedColliderEntityRoot(freshSecond, freshFirst);

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Same(freshFirstCircle, freshFirst.Colliders[0]);
        Assert.Same(freshSecondCircle, freshSecond.Colliders[0]);
        Assert.Equal(6f, freshFirstCircle.Radius);
        Assert.Equal(7f, freshSecondCircle.Radius);
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    [Fact]
    public void FreshOwnedHashSetCanRestoreSavedEntityMembership() {
        (EntityListOwnerRoot saved, Entity savedTarget) = CreateEntityListOwnerRoot(targetFirst: false);
        saved.ActiveEntities.Add(savedTarget);
        (EntityListOwnerRoot baseline, _) = CreateEntityListOwnerRoot(targetFirst: false);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        (EntityListOwnerRoot fresh, Entity freshTarget) = CreateEntityListOwnerRoot(targetFirst: true);
        Assert.Empty(fresh.ActiveEntities);

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Same(freshTarget, Assert.Single(fresh.ActiveEntities));
        Assert.True(graph.Verify(capture.Document, restore, Array.Empty<string>()).Success);
    }

    private static T CreateUninitializedEntity<T>() where T : Entity {
        return (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
    }

    private static SourceIdentifiedEntity CreateSourceIdentifiedEntity(string room, int id, int value) {
        SourceIdentifiedEntity entity = CreateUninitializedEntity<SourceIdentifiedEntity>();
        InitializeEmptyComponentList(entity);
        typeof(Entity).GetField("<SourceId>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(entity, CreateEntityId(room, id));
        entity.Value = value;
        return entity;
    }

    private static SourceEntityListOwnerRoot CreateSourceEntityListOwnerRoot(params Entity[] entities) {
        EntityList entityList = CreateDetachedEntityList();
        List<Entity> values = entities.ToList();
        typeof(EntityList).GetField("entities", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(entityList, values);
        typeof(EntityList).GetField("current", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(entityList, new HashSet<Entity>(values));
        return new SourceEntityListOwnerRoot { Entities = entityList };
    }

    private static OwnedComponentEntity CreateOwnedComponentEntity(bool ownedFirst = false) {
        OwnedComponentEntity entity = CreateUninitializedEntity<OwnedComponentEntity>();
        ComponentList components = CreateDetachedComponentList(entity);
        entity.Owned = new OwnedTestComponent();
        entity.Cached = new List<OwnedTestComponent>();
        Component first = new AkronIgnoreSaveStateComponent(false);
        FieldInfo componentEntityField = typeof(Component).GetField(
            "<Entity>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        componentEntityField.SetValue(first, entity);
        componentEntityField.SetValue(entity.Owned, entity);
        List<Component> orderedComponents = ownedFirst
            ? new List<Component> { entity.Owned, first }
            : new List<Component> { first, entity.Owned };
        typeof(ComponentList).GetField("components", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(components, orderedComponents);
        typeof(ComponentList).GetField("current", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(components, new HashSet<Component>(orderedComponents));
        return entity;
    }

    // withBlink mirrors what BeforeRender does for the saved side; the fresh and
    // baseline sides never rendered, so their private slot stays null. The blink
    // coroutine is deliberately never added to the component list, exactly like
    // DustGraphic's.
    private static LazyBlinkOwnerEntity CreateLazyBlinkOwnerEntity(bool withBlink = false) {
        LazyBlinkOwnerEntity entity = CreateUninitializedEntity<LazyBlinkOwnerEntity>();
        ComponentList components = CreateDetachedComponentList(entity);
        entity.Graphic = new LazyBlinkComponent();
        SetRuntimeField(entity.Graphic, "<Entity>k__BackingField", entity);
        if (withBlink) {
            entity.Graphic.Blink = new Coroutine(entity.Graphic.BlinkRoutine());
        }
        List<Component> orderedComponents = new List<Component> { entity.Graphic };
        SetRuntimeField(components, "components", orderedComponents);
        SetRuntimeField(components, "current", new HashSet<Component>(orderedComponents));
        return entity;
    }

    private static LazyDisposableOwnerEntity CreateLazyDisposableOwnerEntity(bool withBlink = false) {
        LazyDisposableOwnerEntity entity = CreateUninitializedEntity<LazyDisposableOwnerEntity>();
        ComponentList components = CreateDetachedComponentList(entity);
        entity.Holder = new LazyDisposableHolderComponent();
        SetRuntimeField(entity.Holder, "<Entity>k__BackingField", entity);
        if (withBlink) {
            entity.Holder.Blink = new LazyDisposableComponent { Ticks = 3 };
        }
        List<Component> orderedComponents = new List<Component> { entity.Holder };
        SetRuntimeField(components, "components", orderedComponents);
        SetRuntimeField(components, "current", new HashSet<Component>(orderedComponents));
        return entity;
    }

    // pendingValue null is what a clean load leaves: the entity built the state it
    // starts with and has not needed the other one yet, so the slot that records what
    // ran last still holds the first.
    private static OwnedStateEntity CreateOwnedStateEntity(int runningValue, int? pendingValue) {
        OwnedStateEntity entity = CreateUninitializedEntity<OwnedStateEntity>();
        InitializeEmptyComponentList(entity);
        entity.Running = new OwnedStateEntity.OwnedState { Value = runningValue };
        entity.Pending = pendingValue == null
            ? null!
            : new OwnedStateEntity.OwnedState { Value = pendingValue.Value };
        entity.Last = entity.Pending ?? entity.Running;
        return entity;
    }

    private static TrackedComponentEntity CreateTrackedComponentEntity(bool ownedFirst = false) {
        TrackedComponentEntity entity = CreateUninitializedEntity<TrackedComponentEntity>();
        ComponentList components = CreateDetachedComponentList(entity);
        entity.Owned = (LevelEndingHook) RuntimeHelpers.GetUninitializedObject(typeof(LevelEndingHook));
        Component first = new AkronIgnoreSaveStateComponent(false);
        FieldInfo componentEntityField = typeof(Component).GetField(
            "<Entity>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        componentEntityField.SetValue(first, entity);
        componentEntityField.SetValue(entity.Owned, entity);
        List<Component> orderedComponents = ownedFirst
            ? new List<Component> { entity.Owned, first }
            : new List<Component> { first, entity.Owned };
        typeof(ComponentList).GetField("components", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(components, orderedComponents);
        typeof(ComponentList).GetField("current", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(components, new HashSet<Component>(orderedComponents));
        return entity;
    }

    private static Scene CreateTrackedComponentScene(TrackedComponentEntity owner, bool includeComponent) {
        Scene scene = (Scene) RuntimeHelpers.GetUninitializedObject(typeof(Scene));
        Tracker tracker = (Tracker) RuntimeHelpers.GetUninitializedObject(typeof(Tracker));
        typeof(Tracker).GetField("<Entities>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(tracker, new Dictionary<Type, List<Entity>>());
        typeof(Tracker).GetField("<Components>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(
                tracker,
                includeComponent
                    ? new Dictionary<Type, List<Component>> {
                        [typeof(LevelEndingHook)] = new List<Component> { owner.Owned, owner.Owned }
                    }
                    : new Dictionary<Type, List<Component>> {
                        [typeof(LevelEndingHook)] = new List<Component>()
                    });
        typeof(Scene).GetField("<Tracker>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(scene, tracker);
        typeof(Entity).GetField("<Scene>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(owner, scene);
        return scene;
    }

    private static MissingComponentOwnerEntity CreateMissingComponentOwnerEntity(bool includeComponent) {
        MissingComponentOwnerEntity entity = CreateUninitializedEntity<MissingComponentOwnerEntity>();
        ComponentList components = CreateDetachedComponentList(entity);
        entity.Cached = new List<MissingOwnedComponent>();
        List<Component> componentValues = new List<Component>();
        if (includeComponent) {
            MissingOwnedComponent component = new MissingOwnedComponent { Value = 37 };
            typeof(Component).GetField("<Entity>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(component, entity);
            entity.Cached.Add(component);
            componentValues.Add(component);
        }
        typeof(ComponentList).GetField("components", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(components, componentValues);
        typeof(ComponentList).GetField("current", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(components, new HashSet<Component>(componentValues));
        return entity;
    }

    private static CallbackCapturedTarget CreateCallbackCapturedTarget(int value) {
        CallbackCapturedTarget target = CreateUninitializedEntity<CallbackCapturedTarget>();
        InitializeEmptyComponentList(target);
        target.Value = value;
        return target;
    }

    private static (EntityListOwnerRoot Root, Entity Target) CreateEntityListOwnerRoot(bool targetFirst) {
        Entity target = CreateUninitializedEntity<Entity>();
        Entity other = CreateUninitializedEntity<Entity>();
        InitializeEmptyComponentList(target);
        InitializeEmptyComponentList(other);
        EntityList entities = CreateDetachedEntityList();
        List<Entity> orderedEntities = targetFirst
            ? new List<Entity> { target, other }
            : new List<Entity> { other, target };
        typeof(EntityList).GetField("entities", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(entities, orderedEntities);
        typeof(EntityList).GetField("current", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(entities, new HashSet<Entity>(orderedEntities));
        return (new EntityListOwnerRoot { Entities = entities, CanonicalAlias = target }, target);
    }

    private static void InitializeEmptyComponentList(Entity entity) {
        CreateDetachedComponentList(entity);
    }

    private static PeerTargetEntity CreatePeerTargetEntity(string room, int id) {
        PeerTargetEntity entity = CreateUninitializedEntity<PeerTargetEntity>();
        InitializeEmptyComponentList(entity);
        typeof(Entity).GetField("<SourceId>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(entity, CreateEntityId(room, id));
        return entity;
    }

    private static PeerLinkEntity CreatePeerLinkEntity(string room, int id, PeerTargetEntity? peer) {
        PeerLinkEntity entity = CreateUninitializedEntity<PeerLinkEntity>();
        InitializeEmptyComponentList(entity);
        typeof(Entity).GetField("<SourceId>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(entity, CreateEntityId(room, id));
        entity.Peer = peer;
        return entity;
    }

    private static PeerCollectionOwnerEntity CreatePeerCollectionOwnerEntity(
        string room,
        int id,
        params PeerTargetEntity[] peers
    ) {
        PeerCollectionOwnerEntity entity = CreateUninitializedEntity<PeerCollectionOwnerEntity>();
        InitializeEmptyComponentList(entity);
        typeof(Entity).GetField("<SourceId>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(entity, CreateEntityId(room, id));
        entity.Peers = peers.ToList();
        return entity;
    }

    private static ClutterLinkedEntity CreateClutterLinkedEntity(int value) {
        ClutterLinkedEntity entity = CreateUninitializedEntity<ClutterLinkedEntity>();
        InitializeEmptyComponentList(entity);
        entity.HasBelow = new Dictionary<ClutterLinkedEntity, bool>();
        entity.Above = new List<ClutterLinkedEntity>();
        entity.Value = value;
        Hitbox collider = (Hitbox) RuntimeHelpers.GetUninitializedObject(typeof(Hitbox));
        SetRuntimeField(collider, "width", (float) Math.Max(1, value));
        SetRuntimeField(collider, "height", 8f);
        SetRuntimeField(collider, "<Width>k__BackingField", (float) Math.Max(1, value));
        SetRuntimeField(collider, "<Height>k__BackingField", 8f);
        SetRuntimeField(entity, "collider", collider);
        return entity;
    }

    private static (SavedSceneRoot Root, ClutterLinkedEntity[] Entities) CreateClutterLinkedScene(
        params int[] values
    ) {
        Scene scene = (Scene) RuntimeHelpers.GetUninitializedObject(typeof(Scene));
        EntityList entityList = LinkSceneEntities(scene, CreateDetachedEntityList());
        ClutterLinkedEntity[] entities = values.Select(CreateClutterLinkedEntity).ToArray();
        foreach (ClutterLinkedEntity entity in entities) {
            SetRuntimeField(entity, "<Scene>k__BackingField", scene);
            AddDetachedEntity(entityList, entity);
        }
        return (new SavedSceneRoot { Scene = scene, Entities = entityList }, entities);
    }

    private static (SavedSceneRoot Root, LazyBlinkOwnerEntity Owner, EyeballsWatcherEntity[] Watchers) CreateEyeballsScene(
        int watcherCount
    ) {
        Scene scene = (Scene) RuntimeHelpers.GetUninitializedObject(typeof(Scene));
        EntityList entityList = LinkSceneEntities(scene, CreateDetachedEntityList());
        LazyBlinkOwnerEntity owner = CreateLazyBlinkOwnerEntity();
        SetRuntimeField(owner, "<Scene>k__BackingField", scene);
        SetRuntimeField(owner, "<SourceId>k__BackingField", CreateEntityId("a00", 3));
        AddDetachedEntity(entityList, owner);
        List<EyeballsWatcherEntity> watchers = new List<EyeballsWatcherEntity>();
        for (int i = 0; i < watcherCount; i++) {
            EyeballsWatcherEntity watcher = CreateUninitializedEntity<EyeballsWatcherEntity>();
            InitializeEmptyComponentList(watcher);
            SetRuntimeField(watcher, "<Scene>k__BackingField", scene);
            AddDetachedEntity(entityList, watcher);
            watchers.Add(watcher);
        }
        return (new SavedSceneRoot { Scene = scene, Entities = entityList }, owner, watchers.ToArray());
    }

    private static (SavedSceneRoot Root, ClosureRoutineEntity Owner) CreateClosureRoutineScene(
        bool midFlight,
        bool withOwnedComponent = false
    ) {
        Scene scene = (Scene) RuntimeHelpers.GetUninitializedObject(typeof(Scene));
        EntityList entityList = LinkSceneEntities(scene, CreateDetachedEntityList());
        ClosureRoutineEntity owner = CreateUninitializedEntity<ClosureRoutineEntity>();
        ComponentList components = CreateDetachedComponentList(owner);
        SetRuntimeField(owner, "<Scene>k__BackingField", scene);
        SetRuntimeField(owner, "<SourceId>k__BackingField", CreateEntityId("a00", 9));
        Coroutine routine = (Coroutine) RuntimeHelpers.GetUninitializedObject(typeof(Coroutine));
        SetRuntimeField(routine, "<Entity>k__BackingField", owner);
        Stack<IEnumerator> iterators = new Stack<IEnumerator>();
        List<Component> orderedComponents = new List<Component> { routine };
        if (midFlight) {
            OwnedTestComponent? ownedComponent = null;
            if (withOwnedComponent) {
                ownedComponent = new OwnedTestComponent();
                SetRuntimeField(ownedComponent, "<Entity>k__BackingField", owner);
                owner.PendingComponent = ownedComponent;
            }
            IEnumerator iterator = owner.AttackSequence();
            Assert.True(iterator.MoveNext());
            owner.PendingComponent = null;
            iterators.Push(iterator);
        }
        SetRuntimeField(routine, "enumerators", iterators);
        SetRuntimeField(components, "components", orderedComponents);
        SetRuntimeField(components, "current", new HashSet<Component>(orderedComponents));
        owner.Routine = routine;
        AddDetachedEntity(entityList, owner);
        return (new SavedSceneRoot { Scene = scene, Entities = entityList }, owner);
    }

    private static (SavedSceneRoot Root, BoltOwnerEntity Owner) CreateBoltScene(bool midFlight) {
        Scene scene = (Scene) RuntimeHelpers.GetUninitializedObject(typeof(Scene));
        EntityList entityList = LinkSceneEntities(scene, CreateDetachedEntityList());
        BoltOwnerEntity owner = CreateUninitializedEntity<BoltOwnerEntity>();
        InitializeEmptyComponentList(owner);
        SetRuntimeField(owner, "<Scene>k__BackingField", scene);
        SetRuntimeField(owner, "<SourceId>k__BackingField", CreateEntityId("a00", 7));
        BoltOwnerEntity.BoltState bolt = new BoltOwnerEntity.BoltState();
        Coroutine routine = (Coroutine) RuntimeHelpers.GetUninitializedObject(typeof(Coroutine));
        Stack<IEnumerator> iterators = new Stack<IEnumerator>();
        if (midFlight) {
            IEnumerator iterator = bolt.Run();
            Assert.True(iterator.MoveNext());
            iterators.Push(iterator);
        }
        SetRuntimeField(routine, "enumerators", iterators);
        bolt.Routine = routine;
        // GetUninitializedObject skips field initializers, so the list is built here.
        owner.Bolts = new List<BoltOwnerEntity.BoltState> { bolt };
        AddDetachedEntity(entityList, owner);
        return (new SavedSceneRoot { Scene = scene, Entities = entityList }, owner);
    }

    private static SavedSceneRoot CreateDuplicateIteratorScene(bool includeIterator) {
        Scene scene = (Scene) RuntimeHelpers.GetUninitializedObject(typeof(Scene));
        EntityList entityList = LinkSceneEntities(scene, CreateDetachedEntityList());
        IteratorOwnerEntity owner = CreateUninitializedEntity<IteratorOwnerEntity>();
        ComponentList components = CreateDetachedComponentList(owner);
        SetRuntimeField(owner, "<Scene>k__BackingField", scene);
        SetRuntimeField(owner, "<SourceId>k__BackingField", CreateEntityId("a00", 10));
        owner.Value = 11;

        Coroutine coroutine = (Coroutine) RuntimeHelpers.GetUninitializedObject(typeof(Coroutine));
        SetRuntimeField(coroutine, "<Entity>k__BackingField", owner);
        Stack<IEnumerator> iterators = new Stack<IEnumerator>();
        if (includeIterator) {
            IEnumerator iterator = owner.Routine().GetEnumerator();
            Assert.True(iterator.MoveNext());
            iterators = new Stack<IEnumerator>(new[] { iterator, iterator });
        }
        SetRuntimeField(coroutine, "enumerators", iterators);
        SetRuntimeField(components, "components", new List<Component> { coroutine });
        SetRuntimeField(components, "current", new HashSet<Component> { coroutine });
        AddDetachedEntity(entityList, owner);
        return new SavedSceneRoot { Scene = scene, Entities = entityList };
    }

    private static NestedStateOwnerEntity CreateNestedStateOwnerEntity(string room, int id, int? value) {
        NestedStateOwnerEntity entity = CreateUninitializedEntity<NestedStateOwnerEntity>();
        InitializeEmptyComponentList(entity);
        typeof(Entity).GetField("<SourceId>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(entity, CreateEntityId(room, id));
        entity.State = value.HasValue ? new NestedStateOwnerEntity.NestedState { Value = value.Value } : null;
        return entity;
    }

    private static ColliderArrayOwnerEntity CreateColliderArrayOwnerEntity(string room, int id, float radius) {
        ColliderArrayOwnerEntity entity = CreateUninitializedEntity<ColliderArrayOwnerEntity>();
        InitializeEmptyComponentList(entity);
        typeof(Entity).GetField("<SourceId>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(entity, CreateEntityId(room, id));
        Circle circle = (Circle) RuntimeHelpers.GetUninitializedObject(typeof(Circle));
        circle.Radius = radius;
        entity.Colliders = new Collider[] { circle };
        return entity;
    }

    private static AliasedColliderEntityRoot CreateAliasedColliderEntityRoot(
        params ColliderArrayOwnerEntity[] entities
    ) {
        return new AliasedColliderEntityRoot {
            RenderItems = entities.ToList(),
            Entities = CreateSourceEntityListOwnerRoot(entities).Entities
        };
    }

    [Fact]
    public void MissingFreshOrdinaryCollectionStorageIsReconstructed() {
        TestRoot saved = new TestRoot {
            Values = new Dictionary<string, int> { ["saved"] = 37 }
        };
        TestRoot baseline = new TestRoot {
            Values = new Dictionary<string, int> { ["saved"] = 0 }
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        TestRoot fresh = new TestRoot { Values = new Dictionary<string, int>() };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        AkronReconstructionVerification verification = graph.Verify(capture.Document, restore, Array.Empty<string>());
        Assert.True(verification.Success, verification.Error);
        Assert.Equal(37, fresh.Values["saved"]);
    }

    [Fact]
    public void DifferentFreshOrdinaryArrayCapacityUsesTheSavedShape() {
        TestRoot saved = new TestRoot { Numbers = new[] { 1, 2, 3 } };
        TestRoot baseline = new TestRoot { Numbers = new[] { 0, 0, 0 } };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        TestRoot fresh = new TestRoot { Numbers = new[] { 9 } };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Equal(new[] { 1, 2, 3 }, fresh.Numbers);
    }

    [Fact]
    public void CertificateComparesValueTypeFieldsWhenEqualsIsNotUsable() {
        TestRoot saved = new TestRoot { SpecialValue = new NeverEqualValue { Number = 37 } };
        TestRoot baseline = new TestRoot { SpecialValue = new NeverEqualValue { Number = 0 } };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        TestRoot fresh = new TestRoot { SpecialValue = new NeverEqualValue { Number = 99 } };
        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        AkronReconstructionVerification verification = graph.Verify(capture.Document, restore, Array.Empty<string>());

        Assert.True(restore.Success, restore.Error);
        Assert.True(verification.Success, verification.Error);
        Assert.Equal(37, fresh.SpecialValue.Number);
    }

    [Fact]
    public void CertificateRejectsTheFirstUnmaskedDifference() {
        TestRoot saved = new TestRoot {
            Counter = 91,
            Deaths = 4,
            Primary = new TestNode { Name = "child", Value = 37 }
        };
        TestRoot baseline = new TestRoot {
            Primary = new TestNode { Name = "child" }
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        TestRoot fresh = new TestRoot {
            Primary = new TestNode { Name = "child" }
        };
        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);
        fresh.Deaths = 8;

        AkronReconstructionVerification masked = graph.Verify(
            capture.Document,
            restore,
            new[] { "$.Deaths" });

        Assert.True(masked.Success, masked.Error);

        fresh.Primary.Value = 38;
        AkronReconstructionVerification mismatch = graph.Verify(
            capture.Document,
            restore,
            new[] { "$.Deaths" });

        Assert.False(mismatch.Success);
        Assert.Equal("$.Primary.Value", mismatch.ErrorPath);
    }

    [Fact]
    public void CaptureRejectsAResourceWithoutAFreshStructuralMatch() {
        TestRoot saved = new TestRoot {
            Resource = new TestResource("saved-process")
        };
        TestRoot baseline = new TestRoot();
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);

        AkronReconstructionCapture capture = graph.Capture(saved, baseline);

        Assert.False(capture.Success);
        Assert.Equal("$.Resource", capture.ErrorPath);
        Assert.Null(capture.Document);
    }

    // IntPtr.IsPrimitive is true, so the scalar gate used to accept a process
    // pointer and write it as a decimal string. The snapshot was accepted and
    // could never be rebuilt: Convert.ChangeType(string, IntPtr) throws, because
    // IntPtr is not IConvertible. Refuse at capture instead, where a slot that
    // cannot be rebuilt is still rolled back and reported.
    [Fact]
    public void CaptureRefusesAProcessPointerInsteadOfWritingItAsAScalar() {
        NativeHandleRoot saved = new NativeHandleRoot { Handle = new IntPtr(0x2A) };
        NativeHandleRoot baseline = new NativeHandleRoot();
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);

        AkronReconstructionCapture capture = graph.Capture(saved, baseline);

        Assert.False(capture.Success);
        Assert.Equal("$.Handle", capture.ErrorPath);
        Assert.Contains("process pointer cannot be persisted", capture.Error);
        // The refusal has to name what holds the pointer: the path carries field
        // names and array indices, never a type, and the type is what points at
        // the mod responsible.
        Assert.Contains("pointer-type=System.IntPtr", capture.Error);
        Assert.Contains("owner-type=" + typeof(NativeHandleRoot).FullName, capture.Error);
        Assert.Null(capture.Document);
    }

    [Fact]
    public void CaptureRefusesANativeUnsignedPointerOnTheSameGate() {
        NativeUnsignedHandleRoot saved = new NativeUnsignedHandleRoot { Handle = new UIntPtr(0x2A) };
        NativeUnsignedHandleRoot baseline = new NativeUnsignedHandleRoot();
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);

        AkronReconstructionCapture capture = graph.Capture(saved, baseline);

        Assert.False(capture.Success);
        Assert.Equal("$.Handle", capture.ErrorPath);
        Assert.Contains("pointer-type=System.UIntPtr", capture.Error);
    }

    // A snapshot written before that gate was fixed holds a scalar whose type is
    // System.IntPtr, and capture can no longer produce one, so the document is
    // edited into the shape those files already have on disk. Rebuilding one has
    // to say which field it refused; it used to surface a bare InvalidCastException
    // that the restore could only report against "$".
    [Fact]
    public void RebuildRefusesAStoredProcessPointerScalarByItsFieldPath() {
        TestRoot saved = new TestRoot { Counter = 7 };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, new TestRoot());
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionValue counter = capture.Document.Nodes
            .SelectMany(node => node.Fields)
            .Single(field => field.Name == nameof(TestRoot.Counter))
            .Value;
        counter.TypeName = typeof(IntPtr).AssemblyQualifiedName!;
        AkronReconstructionDocument document = graph.Deserialize(graph.Serialize(capture.Document));

        AkronReconstructionRestore restore = graph.Restore(document, new TestRoot());

        Assert.False(restore.Success);
        Assert.Equal("$.Counter", restore.ErrorPath);
        Assert.Contains("unsupported scalar type: System.IntPtr", restore.Error);
        Assert.DoesNotContain("InvalidCastException", restore.Error);
    }

    // A mod session holding a string collection with a culture-aware comparer
    // is ordinary code, and it reaches CompareInfo._sortHandle - a handle into
    // the platform's collation data - in two hops. Capture refused the whole
    // snapshot at that pointer, so every StartPos slot on such an install died
    // at the first load that had to read the document, which is what a chapter
    // change forces. The collation belongs to the process, so the rebuilt room
    // takes its own instead of carrying a pointer across processes.
    //
    // This is the shape the failing install named: the log path ran through a
    // field called "comparer", which is SortedList and SortedSet rather than
    // the underscored "_comparer" of the hash collections. A sorted collection
    // keeps no hash codes, so its lookups and its order are correct after a
    // restore into a new process, which was checked with a real two-process
    // round trip through a file.
    [Fact]
    public void ModuleSessionWithACultureAwareSortedListSurvivesTheDocumentRoundTrip() {
        AkronPersistentRuntimeState saved = new AkronPersistentRuntimeState();
        saved.ModuleSessions["helper"] = new TestCultureSession {
            SummitGems = new SortedList<string, int>(StringComparer.InvariantCultureIgnoreCase) {
                ["beta"] = 2,
                ["Alpha"] = 1
            }
        };
        AkronPersistentRuntimeState baseline = new AkronPersistentRuntimeState();
        baseline.ModuleSessions["helper"] = new TestCultureSession {
            SummitGems = new SortedList<string, int>(StringComparer.InvariantCultureIgnoreCase)
        };
        AkronReconstructionGraph graph = CreateStartPosGraph();

        AkronReconstructionCapture capture = graph.Capture(saved, baseline);

        Assert.True(capture.Success, capture.Error);
        AkronReconstructionDocument document = graph.Deserialize(graph.Serialize(capture.Document));
        AkronPersistentRuntimeState fresh = new AkronPersistentRuntimeState();
        fresh.ModuleSessions["helper"] = new TestCultureSession {
            SummitGems = new SortedList<string, int>(StringComparer.InvariantCultureIgnoreCase)
        };

        AkronReconstructionRestore restore = graph.Restore(document, fresh);

        Assert.True(restore.Success, restore.Error);
        TestCultureSession restored = Assert.IsType<TestCultureSession>(fresh.ModuleSessions["helper"]);
        // Verify before touching Keys: SortedList.keyList is a lazily created
        // wrapper and the saved snapshot has it null, so reading Keys first
        // would make the restored object differ from the document for a reason
        // that has nothing to do with the restore.
        AkronReconstructionVerification verification =
            graph.Verify(document, restore, Array.Empty<string>());
        Assert.True(verification.Success, verification.ErrorPath + ": " + verification.Error);
        Assert.Equal(new[] { "Alpha", "beta" }, restored.SummitGems.Keys.ToArray());
        // The comparer has to still compare the way the saved one did, or the
        // collection silently answers a different question after a restore.
        Assert.True(restored.SummitGems.ContainsKey("ALPHA"));
        Assert.False(restored.SummitGems.ContainsKey("gamma"));
    }

    // The same two-hop path through a hash collection instead. Capture refused
    // this too, and no longer does.
    //
    // What this test deliberately does NOT assert is that the restored set can
    // find its own items. A hash collection stores a hash code per entry, and a
    // culture-aware comparer hashes through a per-process seed, so a set
    // rebuilt in a second process holds hash codes that belong to the first
    // one. Measured with a real two-process round trip: the restored set
    // enumerates both items and reports Count 2, and Contains returns false for
    // both. That is not this fix's doing and not something the CompareInfo
    // boundary can reach - a mod comparer that calls string.GetHashCode has the
    // same defect today, with no CompareInfo anywhere near it, and the graph
    // already re-derives exactly this state for EntityList and ComponentList
    // and for nothing else. Asserting Contains here would pass only because the
    // test captures and restores inside one process, which is the one case that
    // cannot happen in game.
    [Fact]
    public void ModuleSessionWithACultureAwareStringSetIsCapturedAndItsContentsComeBack() {
        AkronPersistentRuntimeState saved = new AkronPersistentRuntimeState();
        saved.ModuleSessions["helper"] = new TestCultureSession {
            HashedGems = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase) {
                "summit-a",
                "summit-b"
            }
        };
        AkronPersistentRuntimeState baseline = new AkronPersistentRuntimeState();
        baseline.ModuleSessions["helper"] = new TestCultureSession {
            HashedGems = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
        };
        AkronReconstructionGraph graph = CreateStartPosGraph();

        AkronReconstructionCapture capture = graph.Capture(saved, baseline);

        Assert.True(capture.Success, capture.Error);
        AkronReconstructionDocument document = graph.Deserialize(graph.Serialize(capture.Document));
        AkronPersistentRuntimeState fresh = new AkronPersistentRuntimeState();
        fresh.ModuleSessions["helper"] = new TestCultureSession {
            HashedGems = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
        };

        AkronReconstructionRestore restore = graph.Restore(document, fresh);

        Assert.True(restore.Success, restore.Error);
        TestCultureSession restored = Assert.IsType<TestCultureSession>(fresh.ModuleSessions["helper"]);
        Assert.Equal(
            new[] { "summit-a", "summit-b" },
            restored.HashedGems.OrderBy(gem => gem, StringComparer.Ordinal).ToArray());
        Assert.True(graph.Verify(document, restore, Array.Empty<string>()).Success);
    }

    // The fresh room does not have to contain the collation the saved frame
    // used: a CompareInfo is named by its sort name and the process hands one
    // back on demand. Without that the anchor would have to find a fresh
    // CompareInfo sitting at the same place in the graph, and a mod session
    // whose shape moved after a reload would be refused for a pointer it never
    // owned.
    //
    // The saved value is built with useUserOverride false, which is the one way
    // to get a CompareInfo that is NOT the process-cached instance for its sort
    // name. Asserting the restore comes back with the cached one is the
    // cross-process contract stated in a single process: the rebuilding process
    // supplies its own wrapper, matched on the name, and the saved wrapper is
    // not carried over. Asserting Assert.Same against the saved object would
    // pass only because one process is doing both halves.
    [Fact]
    public void CompareInfoMissingFromTheFreshGraphIsReacquiredByItsSortName() {
        CompareInfo savedCollation = new CultureInfo("de-DE", useUserOverride: false).CompareInfo;
        CompareInfo processCollation = CultureInfo.GetCultureInfo("de-DE").CompareInfo;
        Assert.NotSame(savedCollation, processCollation);
        AkronPersistentRuntimeState saved = new AkronPersistentRuntimeState();
        saved.ModuleSessions["helper"] = new TestCultureSession { Collation = savedCollation };
        AkronPersistentRuntimeState baseline = new AkronPersistentRuntimeState();
        baseline.ModuleSessions["helper"] = new TestCultureSession();
        AkronReconstructionGraph graph = CreateStartPosGraph();

        AkronReconstructionCapture capture = graph.Capture(saved, baseline);

        Assert.True(capture.Success, capture.Error);
        AkronReconstructionDocument document = graph.Deserialize(graph.Serialize(capture.Document));
        AkronPersistentRuntimeState fresh = new AkronPersistentRuntimeState();
        fresh.ModuleSessions["helper"] = new TestCultureSession();

        AkronReconstructionRestore restore = graph.Restore(document, fresh);

        Assert.True(restore.Success, restore.Error);
        TestCultureSession restored = Assert.IsType<TestCultureSession>(fresh.ModuleSessions["helper"]);
        Assert.NotSame(savedCollation, restored.Collation);
        Assert.Same(processCollation, restored.Collation);
        Assert.Equal("de-DE", restored.Collation.Name);
        // "strasse" against "stra\u00dfe": German collation treats the sharp s
        // as "ss", which ordinal comparison does not, so this says the restored
        // wrapper really is sorting as de-DE. The escape keeps the file ASCII.
        Assert.Equal(
            savedCollation.Compare("strasse", "stra\u00dfe", CompareOptions.None),
            restored.Collation.Compare("strasse", "stra\u00dfe", CompareOptions.None));
        Assert.True(graph.Verify(document, restore, Array.Empty<string>()).Success);
    }

    // The same shape again, with the whole collection missing from the fresh
    // session: the list, its comparer and the collation all have to come back
    // without a fresh counterpart to pair against. This is what a mod session
    // that builds its collections lazily looks like on the first frame after a
    // reload, and it is the case an anchor with no key could not serve.
    [Fact]
    public void ACultureAwareSortedListRebuildsWholeWhenTheFreshSessionHasNone() {
        AkronPersistentRuntimeState saved = new AkronPersistentRuntimeState();
        saved.ModuleSessions["helper"] = new TestCultureSession {
            SummitGems = new SortedList<string, int>(StringComparer.InvariantCultureIgnoreCase) {
                ["beta"] = 2,
                ["Alpha"] = 1
            }
        };
        AkronPersistentRuntimeState baseline = new AkronPersistentRuntimeState();
        baseline.ModuleSessions["helper"] = new TestCultureSession();
        AkronReconstructionGraph graph = CreateStartPosGraph();

        AkronReconstructionCapture capture = graph.Capture(saved, baseline);

        Assert.True(capture.Success, capture.Error);
        AkronReconstructionDocument document = graph.Deserialize(graph.Serialize(capture.Document));
        AkronPersistentRuntimeState fresh = new AkronPersistentRuntimeState();
        fresh.ModuleSessions["helper"] = new TestCultureSession();

        AkronReconstructionRestore restore = graph.Restore(document, fresh);

        Assert.True(restore.Success, restore.Error);
        TestCultureSession restored = Assert.IsType<TestCultureSession>(fresh.ModuleSessions["helper"]);
        Assert.Equal(new[] { "Alpha", "beta" }, restored.SummitGems.Keys.ToArray());
        Assert.True(restored.SummitGems.ContainsKey("ALPHA"));
    }

    // An alternate sort order is a different collation under the same culture
    // name, so a key that stopped at the culture would hand the rebuilt room
    // the wrong one without saying so.
    [Fact]
    public void AlternateSortOrdersGetDistinctResourceKeys() {
        string standard = AkronStartPosReconstruction.GetLiveResourceKey(
            CultureInfo.GetCultureInfo("de-DE").CompareInfo);
        string phonebook = AkronStartPosReconstruction.GetLiveResourceKey(
            CultureInfo.GetCultureInfo("de-DE_phoneb").CompareInfo);
        string invariant = AkronStartPosReconstruction.GetLiveResourceKey(
            CultureInfo.InvariantCulture.CompareInfo);

        Assert.Equal("sort-name=de-DE", standard);
        Assert.Equal("sort-name=de-DE_phoneb", phonebook);
        // The invariant culture's sort name is empty, and an empty key means
        // "no key" to the graph, which would drop the identity check.
        Assert.Equal("sort-name=", invariant);
    }

    // One process holds more than one CompareInfo per sort name: the invariant
    // culture has CultureInfo.InvariantCulture.CompareInfo and the separate
    // instance CompareInfo.GetCompareInfo("") returns. They sort identically,
    // so the anchor is matched on the key rather than on reference identity and
    // the fresh room's own instance is the one that survives.
    [Fact]
    public void AFreshCompareInfoWithTheSameSortNameIsAcceptedThoughItIsAnotherInstance() {
        CompareInfo savedCollation = CultureInfo.InvariantCulture.CompareInfo;
        CompareInfo freshCollation = CompareInfo.GetCompareInfo(string.Empty);
        Assert.NotSame(savedCollation, freshCollation);
        AkronPersistentRuntimeState saved = new AkronPersistentRuntimeState();
        saved.ModuleSessions["helper"] = new TestCultureSession { Collation = savedCollation };
        AkronPersistentRuntimeState baseline = new AkronPersistentRuntimeState();
        baseline.ModuleSessions["helper"] = new TestCultureSession { Collation = freshCollation };
        AkronReconstructionGraph graph = CreateStartPosGraph();

        AkronReconstructionCapture capture = graph.Capture(saved, baseline);

        Assert.True(capture.Success, capture.Error);
        AkronReconstructionDocument document = graph.Deserialize(graph.Serialize(capture.Document));
        AkronPersistentRuntimeState fresh = new AkronPersistentRuntimeState();
        fresh.ModuleSessions["helper"] = new TestCultureSession { Collation = freshCollation };

        AkronReconstructionRestore restore = graph.Restore(document, fresh);

        Assert.True(restore.Success, restore.Error);
        TestCultureSession restored = Assert.IsType<TestCultureSession>(fresh.ModuleSessions["helper"]);
        Assert.Same(freshCollation, restored.Collation);
    }

    // Two distinct instances with one sort name sort identically, but they are
    // still two objects, and a saved graph that told them apart must not have
    // them quietly folded into one. CompareInfo is kept out of
    // AreEquivalentLiveResources for exactly this: the process can always hand
    // back an instance, so nothing forces a fold, and a graph that cannot be
    // rebuilt with its saved reference identity intact is refused by name.
    [Fact]
    public void TwoSavedCompareInfoInstancesSharingASortNameAreRefusedRatherThanFolded() {
        CompareInfo first = CultureInfo.InvariantCulture.CompareInfo;
        CompareInfo second = CompareInfo.GetCompareInfo(string.Empty);
        Assert.NotSame(first, second);
        AkronPersistentRuntimeState saved = new AkronPersistentRuntimeState();
        saved.ModuleSessions["helper"] = new TestCultureSession { Collation = first };
        saved.ModuleSessions["second"] = new TestCultureSession { Collation = second };
        AkronPersistentRuntimeState baseline = new AkronPersistentRuntimeState();
        baseline.ModuleSessions["helper"] = new TestCultureSession { Collation = first };
        baseline.ModuleSessions["second"] = new TestCultureSession { Collation = first };
        AkronReconstructionGraph graph = CreateStartPosGraph();

        AkronReconstructionCapture capture = graph.Capture(saved, baseline);

        Assert.False(capture.Success);
        Assert.EndsWith(".Collation", capture.ErrorPath);
        Assert.Contains("fresh resource", capture.Error);
        Assert.Contains("sort-name=", capture.Error);
    }

    // A document is a file on disk and its resource key is text. Handing an
    // unknown sort name to the process is not safe by itself: the platform
    // parses "not-a-culture-at-all" down to the language "not" and returns a
    // real CompareInfo for it, so the resolver can come back with a valid
    // object that is the wrong collation. What refuses it is the key being
    // re-derived from whatever came back and compared, and the refusal names
    // the node rather than the document root.
    [Fact]
    public void ARebuildRefusesACompareInfoKeyThisProcessResolvesToADifferentSort() {
        AkronPersistentRuntimeState saved = new AkronPersistentRuntimeState();
        saved.ModuleSessions["helper"] = new TestCultureSession {
            Collation = CultureInfo.GetCultureInfo("de-DE").CompareInfo
        };
        AkronPersistentRuntimeState baseline = new AkronPersistentRuntimeState();
        baseline.ModuleSessions["helper"] = new TestCultureSession();
        AkronReconstructionGraph graph = CreateStartPosGraph();
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode collation = capture.Document.Nodes
            .Single(node => node.ResourceKey.Contains("sort-name=", StringComparison.Ordinal));
        collation.ResourceKey = collation.ResourceKey.Replace(
            "sort-name=de-DE",
            "sort-name=not-a-culture-at-all",
            StringComparison.Ordinal);
        AkronReconstructionDocument document = graph.Deserialize(graph.Serialize(capture.Document));
        AkronPersistentRuntimeState fresh = new AkronPersistentRuntimeState();
        fresh.ModuleSessions["helper"] = new TestCultureSession();

        AkronReconstructionRestore restore = graph.Restore(document, fresh);

        Assert.False(restore.Success);
        Assert.EndsWith(".Collation", restore.ErrorPath);
        Assert.Contains("sort-name=not-a-culture-at-all", restore.Error);
        Assert.DoesNotContain("Exception", restore.Error);
    }

    // The three tests below are the live-resource half of the same rule: a key that
    // names its resource is not waived by a wildcarded owner path, and a key that
    // merely labels this process's copy still is.
    //
    // A resource held under a List<T> slot reaches the owner-path fallback when its
    // key resolves to nothing, and that fallback used to skip the key comparison for
    // every keyed resource alike. It exists for a real case - a runtime-named asset
    // gets a new name and a new list index on every reload, so the owner field is
    // the only stable identity it has - and for a sort name or a file path it is
    // simply wrong: those name the resource, and a process that cannot find one does
    // not have it.
    [Fact]
    public void ARebuildRefusesAListHeldCompareInfoKeyThisProcessCannotOpen() {
        AkronPersistentRuntimeState saved = new AkronPersistentRuntimeState();
        saved.ModuleSessions["helper"] = new TestListHeldResourceSession {
            Holders = new List<TestListHeldResourceHolder> {
                new TestListHeldResourceHolder {
                    Collation = CultureInfo.GetCultureInfo("de-DE").CompareInfo
                }
            }
        };
        AkronPersistentRuntimeState baseline = new AkronPersistentRuntimeState();
        baseline.ModuleSessions["helper"] = new TestListHeldResourceSession {
            Holders = new List<TestListHeldResourceHolder> { new TestListHeldResourceHolder() }
        };
        AkronReconstructionGraph graph = CreateStartPosGraph();
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode collation = capture.Document.Nodes
            .Single(node => node.ResourceKey.Contains("sort-name=", StringComparison.Ordinal));
        // Capture classified the saved object, so the node already says the key names
        // a collation rather than labelling one wrapper.
        Assert.True(collation.PortableResourceKey);
        // A sort name no install can open. "not-a-culture-at-all" is not one of them:
        // GetCompareInfo parses it down to the language "not" and hands back a real
        // collation, which the caller then refuses by name. This has to resolve to
        // nothing so the owner path is what would carry it.
        collation.ResourceKey = collation.ResourceKey.Replace(
            "sort-name=de-DE",
            "sort-name=" + new string('z', 200),
            StringComparison.Ordinal);
        AkronReconstructionDocument document = graph.Deserialize(graph.Serialize(capture.Document));
        AkronPersistentRuntimeState fresh = new AkronPersistentRuntimeState();
        fresh.ModuleSessions["helper"] = new TestListHeldResourceSession {
            Holders = new List<TestListHeldResourceHolder> {
                new TestListHeldResourceHolder {
                    Collation = CultureInfo.GetCultureInfo("en-US").CompareInfo
                }
            }
        };

        AkronReconstructionRestore restore = graph.Restore(document, fresh);

        Assert.False(restore.Success);
        Assert.Contains("fresh resource identity differs", restore.Error);
        Assert.EndsWith(".Collation", restore.ErrorPath);
        // The room keeps the collation it loaded with rather than being handed the
        // saved frame's node bound to a different sort.
        TestListHeldResourceSession freshSession =
            Assert.IsType<TestListHeldResourceSession>(fresh.ModuleSessions["helper"]);
        Assert.Equal("en-US", freshSession.Holders[0].Collation!.Name);
    }

    [Fact]
    public void ARebuildRefusesAListHeldTypeKeyThisProcessCannotResolve() {
        AkronPersistentRuntimeState saved = new AkronPersistentRuntimeState();
        saved.ModuleSessions["helper"] = new TestListHeldResourceSession {
            Holders = new List<TestListHeldResourceHolder> {
                new TestListHeldResourceHolder { Kind = typeof(int) }
            }
        };
        AkronPersistentRuntimeState baseline = new AkronPersistentRuntimeState();
        baseline.ModuleSessions["helper"] = new TestListHeldResourceSession {
            Holders = new List<TestListHeldResourceHolder> { new TestListHeldResourceHolder() }
        };
        AkronReconstructionGraph graph = CreateStartPosGraph();
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode kind = capture.Document.Nodes
            .Single(node => node.ResourceKey.Contains(typeof(int).AssemblyQualifiedName!, StringComparison.Ordinal));
        Assert.True(kind.PortableResourceKey);
        kind.ResourceKey = kind.ResourceKey.Replace(
            typeof(int).AssemblyQualifiedName!,
            "Celeste.NoSuchTypeAnywhere",
            StringComparison.Ordinal);
        AkronReconstructionDocument document = graph.Deserialize(graph.Serialize(capture.Document));
        AkronPersistentRuntimeState fresh = new AkronPersistentRuntimeState();
        fresh.ModuleSessions["helper"] = new TestListHeldResourceSession {
            Holders = new List<TestListHeldResourceHolder> {
                new TestListHeldResourceHolder { Kind = typeof(string) }
            }
        };

        AkronReconstructionRestore restore = graph.Restore(document, fresh);

        Assert.False(restore.Success);
        Assert.Contains("fresh resource identity differs", restore.Error);
        Assert.EndsWith(".Kind", restore.ErrorPath);
        TestListHeldResourceSession freshSession =
            Assert.IsType<TestListHeldResourceSession>(fresh.ModuleSessions["helper"]);
        Assert.Same(typeof(string), freshSession.Holders[0].Kind);
    }

    // The same rule over the population it used to miss entirely. Everest never
    // hands a mod's dll to the runtime by path - EverestModuleAssemblyContext
    // reads the relinked dll into memory and calls LoadFromStream so the file is
    // not locked - so no mod assembly has ever had a Location, and asking whether
    // one came off disk answered no for every mod-owned Type there has ever been.
    // Their names are the mod's own, off the mod's own dll, so the key does name
    // the resource and a process that cannot resolve it does not have it.
    //
    // The saved room is one this install could set: the mod was installed, so the
    // room it loaded with holds the type too. The room it rebuilds into does not,
    // which is what uninstalling the mod leaves behind, and the fresh holder's
    // own Type is what the wildcarded owner path would hand over in its place.
    [Fact]
    public void ARebuildRefusesAListHeldModOwnedTypeThisInstallNoLongerHas() {
        Type modOwned = LoadThroughProbeModContext(
                "AkronProbeUninstalledMod",
                BuildProbeModAssembly("AkronProbeUninstalledModAsm"))
            .GetTypes()
            .Single(candidate => candidate.Name == "HelperState");
        // The premise, both halves. The assembly is the shape Everest produces,
        // and nothing in this process can resolve its name back to it, which is
        // what an uninstalled mod looks like from the restore's side.
        Assert.False(modOwned.Assembly.IsDynamic);
        Assert.Empty(modOwned.Assembly.Location);
        Assert.Null(Type.GetType(modOwned.AssemblyQualifiedName!, throwOnError: false));

        AkronPersistentRuntimeState saved = new AkronPersistentRuntimeState();
        saved.ModuleSessions["helper"] = new TestListHeldResourceSession {
            Holders = new List<TestListHeldResourceHolder> {
                new TestListHeldResourceHolder { Kind = modOwned }
            }
        };
        AkronPersistentRuntimeState baseline = new AkronPersistentRuntimeState();
        baseline.ModuleSessions["helper"] = new TestListHeldResourceSession {
            Holders = new List<TestListHeldResourceHolder> {
                new TestListHeldResourceHolder { Kind = modOwned }
            }
        };
        AkronReconstructionGraph graph = CreateStartPosGraph(acceptProbeModContext: true);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode kind = capture.Document.Nodes
            .Single(node => node.ResourceKey.Contains("AkronProbeUninstalledModAsm", StringComparison.Ordinal));
        Assert.True(kind.PortableResourceKey);
        AkronReconstructionDocument document = graph.Deserialize(graph.Serialize(capture.Document));
        AkronPersistentRuntimeState fresh = new AkronPersistentRuntimeState();
        fresh.ModuleSessions["helper"] = new TestListHeldResourceSession {
            Holders = new List<TestListHeldResourceHolder> {
                new TestListHeldResourceHolder { Kind = typeof(string) }
            }
        };

        AkronReconstructionRestore restore = graph.Restore(document, fresh);

        Assert.False(restore.Success);
        Assert.Contains("fresh resource identity differs", restore.Error);
        Assert.EndsWith(".Kind", restore.ErrorPath);
        // Without the fix this reports success and the rebuilt room holds
        // typeof(string) where the saved frame held the mod's type.
        TestListHeldResourceSession freshSession =
            Assert.IsType<TestListHeldResourceSession>(fresh.ModuleSessions["helper"]);
        Assert.Same(typeof(string), freshSession.Holders[0].Kind);
    }

    // The control, and the reason the classification is per resource rather than per
    // type. Everest emits assemblies while the game runs and numbers them in the
    // order that process happened to build them, so the same logical type is
    // "LuaDynAsm0" in one run and "LuaDynAsm1" in the next and every reflection key
    // under it changes. Nothing names it across processes, the owner path is all
    // there is, and refusing here would cost a slot that loads today. A policy that
    // answered from the type - every Type is portable - fails this test.
    [Fact]
    public void AListHeldTypeFromAnEmittedAssemblyIsStillFoundByItsOwnerPath() {
        Type savedEmitted = EmitProbeType("AkronEmittedProbe0");
        Type freshEmitted = EmitProbeType("AkronEmittedProbe1");
        Assert.NotEqual(savedEmitted.AssemblyQualifiedName, freshEmitted.AssemblyQualifiedName);
        AkronPersistentRuntimeState saved = new AkronPersistentRuntimeState();
        saved.ModuleSessions["helper"] = new TestListHeldResourceSession {
            Holders = new List<TestListHeldResourceHolder> {
                new TestListHeldResourceHolder { Kind = savedEmitted }
            }
        };
        AkronPersistentRuntimeState baseline = new AkronPersistentRuntimeState();
        baseline.ModuleSessions["helper"] = new TestListHeldResourceSession {
            Holders = new List<TestListHeldResourceHolder> {
                new TestListHeldResourceHolder { Kind = savedEmitted }
            }
        };
        AkronReconstructionGraph graph = CreateStartPosGraph();
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        AkronReconstructionNode kind = capture.Document.Nodes
            .Single(node => node.ResourceKey.Contains("AkronEmittedProbe0", StringComparison.Ordinal));
        Assert.False(kind.PortableResourceKey);
        AkronReconstructionDocument document = graph.Deserialize(graph.Serialize(capture.Document));
        AkronPersistentRuntimeState fresh = new AkronPersistentRuntimeState();
        fresh.ModuleSessions["helper"] = new TestListHeldResourceSession {
            Holders = new List<TestListHeldResourceHolder> {
                new TestListHeldResourceHolder { Kind = freshEmitted }
            }
        };

        AkronReconstructionRestore restore = graph.Restore(document, fresh);

        Assert.True(restore.Success, restore.Error);
        TestListHeldResourceSession freshSession =
            Assert.IsType<TestListHeldResourceSession>(fresh.ModuleSessions["helper"]);
        Assert.Same(freshEmitted, freshSession.Holders[0].Kind);
    }

    // What the classification actually answers, resource by resource. The reflection
    // rows are the ones that matter: dynamic is not the only way to get an assembly
    // whose name a process makes up, because bytes compiled at startup and handed to
    // Assembly.Load report IsDynamic false and carry no file behind them either.
    [Fact]
    public void APortableResourceKeyIsOneASecondProcessCanDeriveAgain() {
        Type emitted = EmitProbeType("AkronEmittedProbe2");
        // Compiled ahead of time, loaded from bytes: exactly what a mod that builds a
        // helper at startup produces. The core library cannot be loaded this way, so
        // this borrows a data-only dependency off disk rather than emitting one.
        Assembly byteLoaded = Assembly.Load(File.ReadAllBytes(typeof(JObject).Assembly.Location));

        Assert.True(AkronStartPosReconstruction.HasPortableLiveResourceKey(
            CultureInfo.GetCultureInfo("de-DE").CompareInfo));
        Assert.True(AkronStartPosReconstruction.HasPortableLiveResourceKey(typeof(int)));
        Assert.True(AkronStartPosReconstruction.HasPortableLiveResourceKey(typeof(int).Assembly));
        // A MemberInfo key is a metadata token, and a rebuild of the same assembly at
        // the same version moves tokens. File backing says the assembly came off disk,
        // not that it is the same build, so nothing here names the member.
        Assert.False(AkronStartPosReconstruction.HasPortableLiveResourceKey(
            typeof(int).GetMethod(nameof(int.ToString), Type.EmptyTypes)!));
        Assert.True(AkronStartPosReconstruction.HasPortableLiveResourceKey(typeof(List<int>)));

        Assert.False(AkronStartPosReconstruction.HasPortableLiveResourceKey(emitted));
        Assert.False(AkronStartPosReconstruction.HasPortableLiveResourceKey(emitted.Assembly));
        Assert.False(AkronStartPosReconstruction.HasPortableLiveResourceKey(byteLoaded));

        // A mod's assembly. Everest loads every one of them from a stream, so
        // Location is empty and IsDynamic is false - the same two answers the
        // byte-loaded row above gives - and the load context is what separates
        // them. Everest's context means the bytes came off the mod's own dll, so
        // the name is the mod's; Assembly.Load(byte[]) builds a context of its
        // own, so the name is whatever this process chose.
        Assembly modAssembly = LoadThroughProbeModContext(
            "AkronProbeNamedMod",
            BuildProbeModAssembly("AkronProbeNamedModAsm"));
        Type modOwned = modAssembly.GetTypes().Single(candidate => candidate.Name == "HelperState");
        Assert.Empty(modAssembly.Location);
        Assert.False(modAssembly.IsDynamic);
        Assert.True(HasPortableProbeModResourceKey(modAssembly));
        Assert.True(HasPortableProbeModResourceKey(modOwned));
        // What "portable" is claiming, shown rather than asserted about: a second
        // load of the same mod at the same version derives the same key, so a
        // process that cannot produce the key does not have the type.
        Type reloaded = LoadThroughProbeModContext(
                "AkronProbeNamedModAgain",
                BuildProbeModAssembly("AkronProbeNamedModAsm"))
            .GetTypes()
            .Single(candidate => candidate.Name == "HelperState");
        Assert.NotSame(modOwned, reloaded);
        Assert.Equal(
            AkronStartPosReconstruction.GetLiveResourceKey(modOwned),
            AkronStartPosReconstruction.GetLiveResourceKey(reloaded));
        // The assembly-qualified name of a constructed generic spells out the
        // assembly of every type argument, so one emitted argument makes the whole
        // key unrepeatable even though List<> itself came off disk.
        Assert.False(AkronStartPosReconstruction.HasPortableLiveResourceKey(
            typeof(List<>).MakeGenericType(emitted)));
        // An array of an emitted type belongs to the emitted assembly outright.
        Assert.False(AkronStartPosReconstruction.HasPortableLiveResourceKey(emitted.MakeArrayType()));

        // A texture built from data carries whatever name its creator passed in, which
        // the next process makes up again.
        VirtualTexture dataBacked = (VirtualTexture) RuntimeHelpers.GetUninitializedObject(typeof(VirtualTexture));
        SetRuntimeField(dataBacked, "<Path>k__BackingField", string.Empty);
        SetRuntimeField(dataBacked, "<Name>k__BackingField", "runtime-target-17");
        Assert.False(AkronStartPosReconstruction.HasPortableLiveResourceKey(dataBacked));

        // A texture loaded from a file is keyed on the file AND on its dimensions, and
        // dimensions are a measurement of what is in the file rather than a name for
        // it. A mod that redraws one PNG at another size leaves the asset present under
        // the same path and gives it a different key, so the process holds the resource
        // and cannot produce the key - which is the one thing this classification is
        // allowed to rule out. Same defect as the MemberInfo token above by another
        // route, and it collapses the same way: the owner path goes on carrying it.
        VirtualTexture fileBacked = (VirtualTexture) RuntimeHelpers.GetUninitializedObject(typeof(VirtualTexture));
        SetRuntimeField(fileBacked, "<Path>k__BackingField", "Graphics/Atlases/Gameplay.png");
        SetRuntimeField(fileBacked, "<Width>k__BackingField", 4096);
        SetRuntimeField(fileBacked, "<Height>k__BackingField", 4096);
        VirtualTexture retextured = (VirtualTexture) RuntimeHelpers.GetUninitializedObject(typeof(VirtualTexture));
        SetRuntimeField(retextured, "<Path>k__BackingField", "Graphics/Atlases/Gameplay.png");
        SetRuntimeField(retextured, "<Width>k__BackingField", 2048);
        SetRuntimeField(retextured, "<Height>k__BackingField", 2048);
        Assert.NotEqual(
            AkronStartPosReconstruction.GetLiveResourceKey(fileBacked),
            AkronStartPosReconstruction.GetLiveResourceKey(retextured));
        Assert.False(AkronStartPosReconstruction.HasPortableLiveResourceKey(fileBacked));
    }

    private static Type EmitProbeType(string assemblyName) {
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName(assemblyName),
            AssemblyBuilderAccess.Run);
        return assembly.DefineDynamicModule("m")
            .DefineType("AkronProbe.Emitted", TypeAttributes.Public)
            .CreateType();
    }

    // A complete managed assembly with one public type, assembled in memory and
    // never written anywhere. Its name comes out of the metadata written here,
    // which is what makes building it twice produce the same name - the property
    // a mod's dll on disk has and an assembly this process named for itself does
    // not.
    private static byte[] BuildProbeModAssembly(string assemblyName) {
        MetadataBuilder metadata = new MetadataBuilder();
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            default,
            default,
            0,
            System.Reflection.AssemblyHashAlgorithm.None);
        metadata.AddModule(
            0,
            metadata.GetOrAddString(assemblyName + ".dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        AssemblyReferenceHandle runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(8, 0, 0, 0),
            default,
            metadata.GetOrAddBlob(new byte[] { 0xB0, 0x3F, 0x5F, 0x7F, 0x11, 0xD5, 0x0A, 0x3A }),
            default,
            default);
        TypeReferenceHandle systemObject = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Object"));
        // Every assembly begins with the <Module> pseudo-type, and the type after
        // it is the one this probe holds.
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            metadata.GetOrAddString("AkronProbe"),
            metadata.GetOrAddString("HelperState"),
            systemObject,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        BlobBuilder image = new BlobBuilder();
        new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly).Serialize(image);
        return image.ToArray();
    }

    // Everest loads each relinked mod from a stream into its own AssemblyLoadContext.
    // CI's Everest constructor is reference-only, so this test-owned context performs
    // the same runtime operation and the injected policy marks that context as the
    // reproducibly named mod source.
    private static Assembly LoadThroughProbeModContext(string modName, byte[] image) {
        ProbeModAssemblyContext context = new ProbeModAssemblyContext(modName);
        return context.LoadFromStream(new MemoryStream(image));
    }

    private static bool HasPortableProbeModResourceKey(object resource) {
        return AkronStartPosReconstruction.HasPortableLiveResourceKey(
            resource,
            assembly => AssemblyLoadContext.GetLoadContext(assembly) is ProbeModAssemblyContext);
    }

    private sealed class ProbeModAssemblyContext : AssemblyLoadContext {
        public ProbeModAssemblyContext(string name) : base(name, isCollectible: false) {
        }
    }

    // A pointer that is not a collation handle still has to be refused: the
    // fix names one BCL type whose native handle carries no room state, it
    // does not open the scalar gate.
    [Fact]
    public void APointerOutsideTheNamedLiveResourcesIsStillRefused() {
        AkronPersistentRuntimeState saved = new AkronPersistentRuntimeState();
        saved.ModuleSessions["helper"] = new TestNativeHandleSession { Handle = new IntPtr(0x2A) };
        AkronPersistentRuntimeState baseline = new AkronPersistentRuntimeState();
        baseline.ModuleSessions["helper"] = new TestNativeHandleSession();
        AkronReconstructionGraph graph = CreateStartPosGraph();

        AkronReconstructionCapture capture = graph.Capture(saved, baseline);

        Assert.False(capture.Success);
        Assert.Contains("process pointer cannot be persisted", capture.Error);
        Assert.Contains("owner-type=" + typeof(TestNativeHandleSession).FullName, capture.Error);
    }

    // The in-game shape from W29 and W31: a StartPos taken while dialogue is up
    // captures a Textbox, whose frame is an MTexture out of GFX.Portraits, and
    // the room this StartPos rebuilds into loaded with no dialogue on screen -
    // so nothing in the fresh room references that atlas. A skippable cutscene
    // widens this from "while the box is on screen" to "for as long as the
    // handler holds the cutscene's textbox", because Level.onCutsceneSkip is
    // never cleared once set. The atlas is process content either way, so the
    // rebuilt frame gets the one this process loaded.
    //
    // GFX.Portraits is pointed at a second instance carrying the same key,
    // which is the only way to tell "the process supplied it" from "the saved
    // object was carried over" inside one process. Assert.Same against the
    // saved atlas would pass on a broken resolver.
    [Fact]
    public void APortraitsAtlasMissingFromTheFreshRoomIsReacquiredFromTheProcess() {
        Atlas savedAtlas = CreatePortraitsAtlas();
        Atlas processAtlas = CreatePortraitsAtlas();
        Assert.NotSame(savedAtlas, processAtlas);
        Atlas previousPortraits = GFX.Portraits;
        GFX.Portraits = processAtlas;
        try {
            AkronPersistentRuntimeState saved = new AkronPersistentRuntimeState();
            saved.ModuleSessions["helper"] = new TestPortraitSession {
                Frame = CreateAtlasTexture(savedAtlas)
            };
            AkronPersistentRuntimeState baseline = new AkronPersistentRuntimeState();
            baseline.ModuleSessions["helper"] = new TestPortraitSession();
            AkronReconstructionGraph graph = CreateStartPosGraph();

            AkronReconstructionCapture capture = graph.Capture(saved, baseline);

            Assert.True(capture.Success, capture.Error);
            AkronReconstructionDocument document = graph.Deserialize(graph.Serialize(capture.Document));
            AkronPersistentRuntimeState fresh = new AkronPersistentRuntimeState();
            fresh.ModuleSessions["helper"] = new TestPortraitSession();

            AkronReconstructionRestore restore = graph.Restore(document, fresh);

            Assert.True(restore.Success, restore.Error);
            TestPortraitSession restored = Assert.IsType<TestPortraitSession>(fresh.ModuleSessions["helper"]);
            Assert.NotNull(restored.Frame);
            Atlas restoredAtlas = ReadTextureAtlas(restored.Frame!);
            Assert.NotSame(savedAtlas, restoredAtlas);
            Assert.Same(processAtlas, restoredAtlas);
            Assert.True(graph.Verify(document, restore, Array.Empty<string>()).Success);
        } finally {
            GFX.Portraits = previousPortraits;
        }
    }

    // The resolver names the game's own content and nothing else, so an atlas
    // this process never loaded is still refused rather than silently paired
    // with whichever atlas happens to be lying around. The refusal has to carry
    // the data path, because that is the only thing that says which mod's
    // content the slot was lost over.
    [Fact]
    public void AnAtlasThisProcessNeverLoadedIsStillRefused() {
        Atlas savedAtlas = new Atlas {
            DataMethod = "FromAtlas",
            DataPath = Path.Combine("Graphics", "Atlases", "AkronHelperNeverLoaded"),
            RelativeDataPath = "Graphics/Atlases/AkronHelperNeverLoaded/",
            DataFormat = Atlas.AtlasDataFormat.PackerNoAtlas
        };
        AkronPersistentRuntimeState saved = new AkronPersistentRuntimeState();
        saved.ModuleSessions["helper"] = new TestPortraitSession {
            Frame = CreateAtlasTexture(savedAtlas)
        };
        AkronPersistentRuntimeState baseline = new AkronPersistentRuntimeState();
        baseline.ModuleSessions["helper"] = new TestPortraitSession();
        AkronReconstructionGraph graph = CreateStartPosGraph();

        AkronReconstructionCapture capture = graph.Capture(saved, baseline);

        Assert.False(capture.Success);
        Assert.EndsWith("._Atlas", capture.ErrorPath);
        Assert.Contains("fresh resource key is unavailable", capture.Error);
        Assert.Contains("AkronHelperNeverLoaded", capture.Error);
    }

    // Every atlas the game loads has to be reachable, not just the one the
    // observed defect named. The lookup is over the static Atlas fields of GFX
    // and MTN, so a field added to either is picked up without being listed
    // anywhere, and this is what would fail if that lookup were narrowed to
    // GFX.Portraits by name.
    [Fact]
    public void EveryContentAtlasTheGameLoadedResolvesByItsOwnKey() {
        Atlas savedAtlas = new Atlas {
            DataMethod = "FromAtlas",
            DataPath = Path.Combine("Graphics", "Atlases", "Checkpoints"),
            RelativeDataPath = "Graphics/Atlases/Checkpoints/",
            DataFormat = Atlas.AtlasDataFormat.PackerNoAtlas
        };
        Atlas processAtlas = new Atlas {
            DataMethod = savedAtlas.DataMethod,
            DataPath = savedAtlas.DataPath,
            RelativeDataPath = savedAtlas.RelativeDataPath,
            DataFormat = savedAtlas.DataFormat
        };
        Atlas previousCheckpoints = MTN.Checkpoints;
        MTN.Checkpoints = processAtlas;
        try {
            AkronPersistentRuntimeState saved = new AkronPersistentRuntimeState();
            saved.ModuleSessions["helper"] = new TestPortraitSession {
                Frame = CreateAtlasTexture(savedAtlas)
            };
            AkronPersistentRuntimeState baseline = new AkronPersistentRuntimeState();
            baseline.ModuleSessions["helper"] = new TestPortraitSession();
            AkronReconstructionGraph graph = CreateStartPosGraph();

            AkronReconstructionCapture capture = graph.Capture(saved, baseline);

            Assert.True(capture.Success, capture.Error);
            AkronReconstructionDocument document = graph.Deserialize(graph.Serialize(capture.Document));
            AkronPersistentRuntimeState fresh = new AkronPersistentRuntimeState();
            fresh.ModuleSessions["helper"] = new TestPortraitSession();

            AkronReconstructionRestore restore = graph.Restore(document, fresh);

            Assert.True(restore.Success, restore.Error);
            TestPortraitSession restored = Assert.IsType<TestPortraitSession>(fresh.ModuleSessions["helper"]);
            Assert.Same(processAtlas, ReadTextureAtlas(restored.Frame!));
        } finally {
            MTN.Checkpoints = previousCheckpoints;
        }
    }

    // Sorting the same is not being the same object, and neither is loading
    // from the same path. Two content atlases carrying one key are two objects
    // the rebuilt room cannot tell apart, so the slot is refused by key rather
    // than paired with whichever the lookup reached first. This is the same
    // stance CompareInfo takes, and it is why the lookup is not a FirstOrDefault.
    [Fact]
    public void TwoContentAtlasesSharingOneKeyAreRefusedRatherThanGuessedBetween() {
        Atlas previousPortraits = GFX.Portraits;
        Atlas previousMisc = GFX.Misc;
        GFX.Portraits = CreatePortraitsAtlas();
        GFX.Misc = CreatePortraitsAtlas();
        try {
            AkronPersistentRuntimeState saved = new AkronPersistentRuntimeState();
            saved.ModuleSessions["helper"] = new TestPortraitSession {
                Frame = CreateAtlasTexture(CreatePortraitsAtlas())
            };
            AkronPersistentRuntimeState baseline = new AkronPersistentRuntimeState();
            baseline.ModuleSessions["helper"] = new TestPortraitSession();
            AkronReconstructionGraph graph = CreateStartPosGraph();

            AkronReconstructionCapture capture = graph.Capture(saved, baseline);

            Assert.False(capture.Success);
            Assert.EndsWith("._Atlas", capture.ErrorPath);
            Assert.Contains("fresh resource key is unavailable", capture.Error);
            Assert.Contains("Graphics", capture.Error);
        } finally {
            GFX.Portraits = previousPortraits;
            GFX.Misc = previousMisc;
        }
    }

    private static Atlas CreatePortraitsAtlas() {
        // The key GFX.Portraits carries in game, verbatim from the W29 log:
        // FromAtlas|Graphics\Atlases\Portraits|Graphics/Atlases/Portraits/|PackerNoAtlas.
        return new Atlas {
            DataMethod = "FromAtlas",
            DataPath = Path.Combine("Graphics", "Atlases", "Portraits"),
            RelativeDataPath = "Graphics/Atlases/Portraits/",
            DataFormat = Atlas.AtlasDataFormat.PackerNoAtlas
        };
    }

    // MTexture.Atlas has a setter, but these tests build against the stripped
    // Celeste reference assembly, where property bodies do nothing. The backing
    // field is also the node the in-game refusal named, so writing it directly
    // is the closer reproduction as well as the working one.
    private static MTexture CreateAtlasTexture(Atlas atlas) {
        MTexture texture = new MTexture { AtlasPath = "textbox/default" };
        typeof(MTexture)
            .GetField("_Atlas", RuntimeInstanceFields)!
            .SetValue(texture, atlas);
        return texture;
    }

    private static Atlas ReadTextureAtlas(MTexture texture) {
        return (Atlas) typeof(MTexture)
            .GetField("_Atlas", RuntimeInstanceFields)!
            .GetValue(texture)!;
    }

    private static AkronReconstructionGraph CreateStartPosGraph(bool acceptProbeModContext = false) {
        return new AkronReconstructionGraph(
            AkronStartPosReconstruction.IsLiveResourceType,
            AkronStartPosReconstruction.GetLiveResourceKey,
            null,
            AkronStartPosReconstruction.ResolveDetachedLiveResource,
            areEquivalentLiveResources: AkronStartPosReconstruction.AreEquivalentLiveResources,
            hasPortableLiveResourceKey: acceptProbeModContext
                ? HasPortableProbeModResourceKey
                : AkronStartPosReconstruction.HasPortableLiveResourceKey,
            getMapPlacedEntityIds: AkronStartPosReconstruction.GetMapPlacedEntityIds,
            isAdditionalLiveResource: AkronStartPosReconstruction.IsLiveHookOwner,
            hasDeferredDetachedLiveResourceKey: AkronStartPosReconstruction.HasDeferredHookOwnerKey);
    }

    [Fact]
    public void AnonymousTargetFreeRuntimeCallbackUsesTheFreshStructuralCallback() {
        Action savedCallback = CreateAnonymousCallback();
        Action baselineCallback = CreateAnonymousCallback();
        TestRoot saved = new TestRoot { Callback = savedCallback };
        TestRoot baseline = new TestRoot { Callback = baselineCallback };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        Action freshCallback = CreateAnonymousCallback();
        TestRoot fresh = new TestRoot { Callback = freshCallback };

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Same(freshCallback, fresh.Callback);
    }

    [Fact]
    public void HookOriginalCallbackIsRebuiltFromTheCurrentDetourChain() {
        MethodInfo source = typeof(StartPosReconstructionTests).GetMethod(
            nameof(HookedSequence),
            BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo target = typeof(StartPosReconstructionTests).GetMethod(
            nameof(InterceptedSequence),
            BindingFlags.Static | BindingFlags.NonPublic)!;
        using Hook hook = new Hook(source, target);
        IEnumerable<int> saved = HookedSequence(5);
        object captureBaseline = RuntimeHelpers.GetUninitializedObject(saved.GetType());
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);

        AkronReconstructionCapture capture = graph.Capture(saved, captureBaseline);

        Assert.True(capture.Success, capture.Error);
        string json = graph.Serialize(capture.Document);
        object fresh = RuntimeHelpers.GetUninitializedObject(saved.GetType());
        AkronReconstructionRestore restore = graph.Restore(graph.Deserialize(json), fresh);

        Assert.True(restore.Success, restore.Error);
        AkronReconstructionVerification verification = graph.Verify(capture.Document, restore, Array.Empty<string>());
        Assert.True(verification.Success, verification.Error);
        Assert.Equal(new[] { 16 }, ((IEnumerable<int>) fresh).ToArray());
    }

    [Fact]
    public void FailedAndReplacedPersistentResourcesAreDisposed() {
        TestResourceAdapter adapter = new TestResourceAdapter();
        AkronReconstructionGraph graph = new AkronReconstructionGraph(
            IsLiveResource,
            resource => ((TestResource) resource).StableKey,
            adapter);
        TestRoot saved = new TestRoot {
            Counter = 1,
            Resource = new TestResource("saved", "buffer")
        };
        TestRoot baseline = new TestRoot {
            Resource = new TestResource("baseline", "buffer")
        };
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);

        TestRoot fresh = new TestRoot { Resource = new TestResource("fresh", "buffer") };
        AkronReconstructionRestore firstRestore = graph.Restore(capture.Document, fresh);
        Assert.True(firstRestore.Success, firstRestore.Error);
        TestResource firstOwnedResource = fresh.Resource;

        AkronReconstructionRestore secondRestore = graph.Restore(capture.Document, fresh);
        Assert.True(secondRestore.Success, secondRestore.Error);
        Assert.True(firstOwnedResource.IsDisposed);

        AkronReconstructionDocument invalidDocument = graph.Deserialize(graph.Serialize(capture.Document));
        AkronReconstructionNode root = invalidDocument.Nodes.Single(node => node.Id == invalidDocument.RootNodeId);
        root.Fields.Single(field => field.Name == nameof(TestRoot.Counter)).Name = "MissingField";
        AkronReconstructionRestore failedRestore = graph.Restore(invalidDocument, fresh);

        Assert.False(failedRestore.Success);
        Assert.True(adapter.LastRestored.IsDisposed);
    }

    [Fact]
    public void CompressedSnapshotFileRoundTripsGraphAndIdentity() {
        string directory = Path.Combine(Path.GetTempPath(), "akron-startpos-test-" + Guid.NewGuid().ToString("N"));
        try {
            TestRoot saved = new TestRoot {
                Counter = 91,
                Primary = new TestNode { Name = "saved", Value = 37 }
            };
            TestRoot baseline = new TestRoot {
                Primary = new TestNode { Name = "fresh" }
            };
            AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
            AkronReconstructionCapture capture = graph.Capture(saved, baseline);
            Assert.True(capture.Success, capture.Error);
            capture.Document.GameplayBuffers.Add(new AkronGameplayBufferSnapshot {
                FieldName = "LightBuffer",
                Payload = new AkronReconstructionResourcePayload {
                    Kind = "virtual-render-target-rgba-v1",
                    Name = "gameplay-buffer-5",
                    Width = 1,
                    Height = 1,
                    Bytes = new byte[] { 1, 2, 3, 4 }
                }
            });

            bool wrote = AkronStartPosReconstruction.SaveSnapshot(
                "Akron StartPos test 1",
                "Celeste/1-ForsakenCity",
                "1",
                0,
                capture.Document,
                out string writeError,
                directory);
            bool loaded = AkronStartPosReconstruction.TryLoadSnapshot(
                "Akron StartPos test 1",
                out AkronReconstructionDocument document,
                out string loadError,
                directory);

            Assert.True(wrote, writeError);
            Assert.True(loaded, loadError);
            Assert.Equal("Celeste/1-ForsakenCity", document.MapSid);
            Assert.Equal("1", document.Room);
            Assert.Equal(0, document.FileSlot);
            Assert.Equal("akron-reconstruction-v10", document.Format);
            Assert.Equal("LightBuffer", Assert.Single(document.GameplayBuffers).FieldName);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, document.GameplayBuffers[0].Payload.Bytes);
            Assert.Contains("v10-", Path.GetFileName(AkronStartPosReconstruction.GetSnapshotPath("Akron StartPos test 1", directory)));
            Assert.True(File.Exists(AkronStartPosReconstruction.GetSnapshotPath("Akron StartPos test 1", directory)));
        } finally {
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void SnapshotReaderUsesAFiniteExpandedSizeLimitByDefault() {
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(
            new TestRoot { Counter = 91 },
            new TestRoot());
        Assert.True(capture.Success, capture.Error);
        using MemoryStream snapshot = new MemoryStream();
        using (GZipStream compressed = new GZipStream(snapshot, CompressionLevel.Fastest, leaveOpen: true)) {
            graph.Serialize(capture.Document, compressed);
        }
        snapshot.Position = 0;

        bool loaded = AkronStartPosReconstruction.TryReadSnapshot(
            snapshot,
            out _,
            out string error,
            maxDecompressedBytes: 64);
        MethodInfo reader = typeof(AkronStartPosReconstruction).GetMethod(
            nameof(AkronStartPosReconstruction.TryReadSnapshot),
            BindingFlags.Static | BindingFlags.Public)!;
        object defaultLimit = reader.GetParameters().Single(parameter => parameter.Name == "maxDecompressedBytes").DefaultValue!;

        Assert.False(loaded);
        Assert.Contains("Snapshot expands beyond its size limit", error);
        Assert.Equal(AkronStartPosReconstruction.MaxDecompressedSnapshotBytes, Assert.IsType<long>(defaultLimit));
        Assert.NotEqual(long.MaxValue, defaultLimit);
    }

    [Fact]
    public void DefaultExpandedSizeBudgetCoversHeartOfStormSnapshots() {
        // The larger remote capture expanded to 228,139,144 bytes. Keep this
        // regression threshold tied to the real player environment instead of
        // a small synthetic graph that cannot expose decompression limits.
        Assert.True(
            AkronStartPosReconstruction.MaxDecompressedSnapshotBytes >= 228_139_144,
            "The default expanded-size budget rejects Heart of the Storm.");

        // And it has to leave room for a modded map that grows. The largest snapshot
        // measured across 17 real installs is 231,081,666 bytes decompressed; at the
        // earlier 256 MiB limit that was 11% of headroom, and the failure it buys is a
        // slot that cannot be loaded at all rather than one that loads slowly.
        const long largestMeasuredSnapshotBytes = 231_081_666L;
        Assert.True(
            AkronStartPosReconstruction.MaxDecompressedSnapshotBytes >= largestMeasuredSnapshotBytes * 3L / 2L,
            "The expanded-size budget leaves less than 50% over the largest real snapshot.");
    }

    [Fact]
    public void DefaultSnapshotPathUsesTheApplicationBaseDirectory() {
        string path = AkronStartPosReconstruction.GetSnapshotPath("Akron StartPos default path test");

        Assert.StartsWith(
            Path.Combine(AppContext.BaseDirectory, "Saves", "AkronStartPos"),
            path,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CompressedSnapshotFileRoundTripsRegisteredActionState() {
        string directory = Path.Combine(Path.GetTempPath(), "akron-startpos-action-test-" + Guid.NewGuid().ToString("N"));
        try {
            AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
            AkronReconstructionCapture levelCapture = graph.Capture(
                new TestRoot { Counter = 91 },
                new TestRoot());
            Assert.True(levelCapture.Success, levelCapture.Error);
            AkronReconstructionCapture actionCapture = graph.Capture(
                new TestActionState { FrameCounter = 1234, TimeRate = 0.5f },
                new TestActionState());
            Assert.True(actionCapture.Success, actionCapture.Error);
            levelCapture.Document.ActionStateDocument = actionCapture.Document;

            bool wrote = AkronStartPosReconstruction.SaveSnapshot(
                "Akron StartPos action test",
                "Celeste/1-ForsakenCity",
                "1",
                0,
                levelCapture.Document,
                out string writeError,
                directory);
            bool loaded = AkronStartPosReconstruction.TryLoadSnapshot(
                "Akron StartPos action test",
                out AkronReconstructionDocument document,
                out string loadError,
                directory);
            TestActionState freshActionState = new TestActionState {
                FrameCounter = 9,
                TimeRate = 1f
            };
            AkronReconstructionRestore restore = graph.Restore(
                document.ActionStateDocument,
                freshActionState);

            Assert.True(wrote, writeError);
            Assert.True(loaded, loadError);
            Assert.True(restore.Success, restore.Error);
            Assert.Equal(1234, freshActionState.FrameCounter);
            Assert.Equal(0.5f, freshActionState.TimeRate);
            Assert.True(graph.Verify(document.ActionStateDocument, restore, Array.Empty<string>()).Success);
        } finally {
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void RegisteredActionIdentityIsStableAcrossProcesses() {
        AkronRegisteredSaveLoadAction action = new AkronRegisteredSaveLoadAction(
            "core-runtime-0",
            saveState: null,
            loadState: null,
            clearState: null,
            beforeSaveState: null,
            beforeLoadState: null,
            preCloneEntities: null);

        Assert.Equal("core-runtime-0", action.Id);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IEnumerable<int> HookedSequence(int value) {
        yield return value + 1;
    }

    private delegate IEnumerable<int> orig_HookedSequence(int value);

    private static IEnumerable<int> InterceptedSequence(orig_HookedSequence orig, int value) {
        foreach (int item in orig(value)) {
            yield return item + 10;
        }
    }

    private static void UntrustedSnapshotCallback() {
    }

    private static Action CreateAnonymousCallback() {
        DynamicMethod method = new DynamicMethod(
            "akron-reconstruction-test-runtime-callback",
            typeof(void),
            Type.EmptyTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        return (Action) method.CreateDelegate(typeof(Action));
    }

    private static bool IsLiveResource(Type type) {
        return type == typeof(TestResource);
    }

    private sealed class TestRoot {
        public int Counter;
        public int Deaths;
        public TestNode Primary = null!;
        public TestNode Secondary = null!;
        public TestResource Resource = null!;
        public Action Callback = null!;
        public Action[] Callbacks = Array.Empty<Action>();
        public Dictionary<string, int> Values = new Dictionary<string, int>();
        public int[] Numbers = Array.Empty<int>();
        public NeverEqualValue SpecialValue;
        public Entity RoomEntity = null!;
        public Entity AlternativeRoomEntity = null!;
    }

    // A mod object that keeps a native handle. No object a vanilla room graph
    // reaches is known to hold one, but a helper that talks to native code can.
    private sealed class NativeHandleRoot {
        public IntPtr Handle;
    }

    // The Spring Collab 2020 shape that used to refuse every Heart of the Storm
    // capture: an ordinary room object holding a WeakReference alongside a
    // strong edge to the same target.
    private sealed class WeakReferenceRoot {
        public TestNode Strong = null!;
        public WeakReference Weak = null!;
        public WeakReference<TestNode> TypedWeak = null!;
    }

    // A weak reference pointing at another weak reference, to exercise the
    // capture-time refusal of a chain that would restore out of order.
    private sealed class WeakChainRoot {
        public WeakReference Outer = null!;
    }

    private sealed class NativeUnsignedHandleRoot {
        public UIntPtr Handle;
    }

    private sealed class RuntimeTypeRoot {
        public Type TrackerKey = null!;
    }

    private sealed class RuntimeMemberRoot {
        public MemberInfo Member = null!;
    }

    private sealed class ScalarListRoot {
        public List<int> Values = new List<int>();
    }

    private sealed class EntityListRoot {
        public List<Entity> Entities = new List<Entity>();
    }

    private sealed class ComparerRoot {
        public IEqualityComparer<string>? Primary;
        public IEqualityComparer<string>? Secondary;
    }

    private sealed class ConcurrentDictionaryRoot {
        public ConcurrentDictionary<string, float> Values = new ConcurrentDictionary<string, float>();
    }

    private sealed class TestEverestSettings : EverestModuleSettings {
    }

    private sealed class TestEverestSession : EverestModuleSession {
        public TestSharedState Shared = null!;
    }

    // The shape the maintainer's install actually produced: a mod session that
    // keeps a string collection whose comparer is culture-aware, plus the two
    // narrower cases that shape is built out of.
    private sealed class TestCultureSession : EverestModuleSession {
        public SortedList<string, int> SummitGems = null!;
        public HashSet<string> HashedGems = null!;
        public CompareInfo Collation = null!;
    }

    private sealed class TestNativeHandleSession : EverestModuleSession {
        public IntPtr Handle;
    }

    // A keyed live resource reached through a List<T> slot, which is the shape whose
    // owner path the restore is allowed to fall back on.
    private sealed class TestEntityListSession : EverestModuleSession {
        public EntityList Entities = null!;
    }

    private sealed class TestListHeldResourceSession : EverestModuleSession {
        public List<TestListHeldResourceHolder> Holders = new List<TestListHeldResourceHolder>();
    }

    private sealed class TestListHeldResourceHolder {
        public CompareInfo? Collation;
        public Type? Kind;
    }

    // Stands in for the Textbox field the in-game refusal walked through:
    // an MTexture the saved frame owns, whose atlas is process content.
    private sealed class TestPortraitSession : EverestModuleSession {
        public MTexture? Frame;
    }

    private sealed class TestSharedState {
        public int Value;
    }

    private sealed class TestNode {
        public string Name = string.Empty;
        public int Value;
        public TestRoot Parent = null!;
        public TestResource Resource = null!;
        public Action OnUpdate = null!;

        public void Increment() {
            Value++;
        }

        public void Reset() {
            Value = 0;
        }
    }

    private sealed class ChainNode {
        public int Value;
        public ChainNode Next = null!;

        public ChainNode NextAt(int index) {
            ChainNode current = this;
            for (int step = 0; step < index; step++) {
                current = current.Next;
            }
            return current;
        }
    }

    private sealed class FrameworkValueRoot {
        public Vector2 Vector2;
        public Vector3 Vector3;
        public Color Color;
        public Rectangle Rectangle;
        public VertexPositionColor Vertex;
    }

    private sealed class PathfinderRoot {
        public Pathfinder Pathfinder = null!;
    }

    private sealed class PrimitiveArrayRoot {
        public int[,] Integers = null!;
        public bool[] Booleans = null!;
    }

    private sealed class SparseArrayRoot {
        public Array Items = Array.Empty<object>();
    }

    private sealed class SharedContainerRoot {
        public object[] Earlier = null!;
        public object[] Expected = null!;
    }

    private sealed class ExactSlotRoot {
        public ExactSlotObject[] Items = null!;
    }

    private class ExactSlotObject {
        public int Value;

        public void SetValue(int value) {
            Value = value;
        }
    }

    private sealed class DerivedExactSlotObject : ExactSlotObject {
    }

    private sealed class PassiveDataRoot {
        public object? Value;
    }

    private sealed class IteratorStateRoot {
        public IteratorOwner Owner = null!;
        public Stack<IEnumerator> States = new Stack<IEnumerator>();
    }

    private sealed class IteratorOwner {
        public int Value;

        public IEnumerable<int> Routine() {
            yield return Value;
            yield return Value + 1;
        }
    }

    private sealed class CallbackAliasFirstRoot {
        public PassiveCallbackHolder Holder = null!;
        public Action Callback = null!;
    }

    private sealed class PassiveCallbackHolder {
        public Action Callback = null!;
    }

    private sealed class CallbackClosureRoot {
        public CallbackClosureOwner Owner = null!;
        public Action Callback = null!;
    }

    private sealed class CallbackClosureOwner {
        public int Value;

        public Action CreateCallback(int amount) {
            return () => Value += amount;
        }
    }

    private sealed class CapturedFreshCallbackRoot {
        public EntityList Entities = null!;
        public Action Callback = null!;
    }

    private sealed class CallbackCapturedTarget : Entity {
        public int Value;
    }

    private static class CapturedFreshCallbackFactory {
        public static Action Create(CallbackCapturedTarget target) {
            return () => target.Value++;
        }
    }

    // A capture used to copy the whole ancestor fresh-path at every field it
    // visited, so its cost grew with the square of graph depth. That copy could
    // never reach the document - a node only records a fresh path when it is
    // re-paired with a fresh object found elsewhere, and both re-pairing sites
    // supply that object's own recorded path. These two tests pin the removal:
    // the shape test catches a reintroduced per-field copy, the identity test
    // catches a re-pairing that lost its path.

    [Fact]
    public void CaptureCostGrowsWithGraphSizeRatherThanWithTheSquareOfItsDepth() {
        // Doubling the depth of a chain doubles the node count. A capture that
        // copies the ancestor path per field roughly quadruples instead; the
        // measured figures were 3.83x before the copy was removed and 2.87x
        // after (233,776 bytes at depth 100 against 670,688 at depth 200, the
        // same values on every run now that the counter is thread-local), so
        // anything at or above 3.2x is the old behaviour returning.
        long shallow = MeasureCaptureAllocation(100);
        long deep = MeasureCaptureAllocation(200);

        Assert.True(shallow > 0);
        Assert.True(
            deep < shallow * 3.2,
            "depth 100 allocated " + shallow + " bytes, depth 200 allocated " + deep + " bytes");
    }

    private static long MeasureCaptureAllocation(int depth) {
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        ChainNode saved = BuildChain(depth, valueOffset: 10);
        ChainNode baseline = BuildChain(depth, valueOffset: 0);
        // Warm the reflection and type-name caches so the measurement covers the
        // walk itself rather than one-time setup.
        graph.Capture(BuildChain(depth, valueOffset: 10), BuildChain(depth, valueOffset: 0));

        // GC.GetTotalAllocatedBytes counts every thread in the process. xUnit runs test
        // collections in parallel, so it charged this measurement for whatever else was
        // running, which made this test fail about 30% of the time on an idle machine
        // (the recorded flake read 233,776 bytes at depth 100 against 2,682,496 at depth
        // 200, an 11.5x that no capture ever allocated). Capture is synchronous and
        // single-threaded, so the thread-local counter measures exactly this call and
        // nothing else - the same counter every other allocation assertion here uses.
        long before = GC.GetAllocatedBytesForCurrentThread();
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.True(capture.Success, capture.Error);
        Assert.Equal(depth, capture.Document.Nodes.Count);
        return after - before;
    }

    [Fact]
    public void ARepairedEntityStillCarriesItsFreshPathThroughAWholeSnapshotRoundTrip() {
        // The re-pairing branch is the only producer of a non-empty FreshPath,
        // and it is the branch the removed parameter used to feed. Drive it and
        // follow the value all the way through the real gzip snapshot file.
        string directory = Path.Combine(Path.GetTempPath(), "akron-freshpath-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            TestRoot saved = new TestRoot {
                Counter = 77,
                Primary = new TestNode { Name = "child", Value = 21 },
                Resource = new TestResource("shared-key")
            };
            // The baseline holds the same resource key behind a different
            // wrapper, which is what forces the capture down the re-pairing
            // branch instead of pairing the object it was handed.
            TestRoot baseline = new TestRoot {
                Primary = new TestNode { Name = "child" },
                Resource = new TestResource("shared-key")
            };
            AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
            AkronReconstructionCapture capture = graph.Capture(saved, baseline);
            Assert.True(capture.Success, capture.Error);

            AkronReconstructionDocument captured = capture.Document;
            List<AkronReconstructionNode> withFreshPath =
                captured.Nodes.Where(node => node.FreshPath.Count > 0).ToList();

            string path = Path.Combine(directory, "round-trip.json.gz");
            using (FileStream file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (GZipStream compressed = new GZipStream(file, CompressionLevel.Optimal, leaveOpen: false)) {
                graph.Serialize(captured, compressed);
            }

            AkronReconstructionDocument loaded;
            using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (GZipStream compressed = new GZipStream(file, CompressionMode.Decompress, leaveOpen: false)) {
                loaded = graph.Deserialize(compressed);
            }

            // Every fresh path survives the format unchanged, step for step.
            Assert.Equal(captured.Nodes.Count, loaded.Nodes.Count);
            for (int index = 0; index < captured.Nodes.Count; index++) {
                List<AkronReconstructionPathStep> expected = captured.Nodes[index].FreshPath;
                List<AkronReconstructionPathStep> actual = loaded.Nodes[index].FreshPath;
                Assert.Equal(expected.Count, actual.Count);
                for (int step = 0; step < expected.Count; step++) {
                    Assert.Equal(expected[step].Kind, actual[step].Kind);
                    Assert.Equal(expected[step].DeclaringTypeName, actual[step].DeclaringTypeName);
                    Assert.Equal(expected[step].FieldName, actual[step].FieldName);
                    Assert.Equal(expected[step].ArrayIndices, actual[step].ArrayIndices);
                }
            }

            // And the loaded document still restores the saved state exactly.
            TestRoot fresh = new TestRoot {
                Primary = new TestNode { Name = "child" },
                Resource = new TestResource("shared-key")
            };
            AkronReconstructionRestore restore = graph.Restore(loaded, fresh);
            Assert.True(restore.Success, restore.Error);
            Assert.Equal(77, fresh.Counter);
            Assert.Equal(21, fresh.Primary.Value);
            Assert.True(graph.Verify(loaded, restore, Array.Empty<string>()).Success);
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ChainNode BuildChain(int count, int valueOffset) {
        ChainNode root = new ChainNode { Value = valueOffset };
        ChainNode current = root;
        for (int index = 1; index < count; index++) {
            current.Next = new ChainNode { Value = valueOffset + index };
            current = current.Next;
        }
        return root;
    }

    private static void AssertFloatBits(float expected, float actual) {
        Assert.Equal(BitConverter.SingleToInt32Bits(expected), BitConverter.SingleToInt32Bits(actual));
    }

    private static uint PackedColor(Color color) {
        return Unsafe.As<Color, uint>(ref color);
    }

    private sealed class TestActionState {
        public long FrameCounter;
        public float TimeRate;
    }

    private sealed class TestResourceListRoot {
        public List<TestResourceHolder> Holders = new List<TestResourceHolder>();
    }

    private sealed class DuplicateResourceRoot {
        public TestResource TargetA = null!;
        public TestResource TargetB = null!;
        public TestResource TargetC = null!;
        public TestResource CandidateA = null!;
        public TestResource CandidateB = null!;
    }

    private sealed class TestResourceHolder {
        public TestResource Resource = null!;
    }

    private sealed class TestResource : IDisposable {
        public TestResource(string processIdentity, string? stableKey = null) {
            ProcessIdentity = processIdentity;
            StableKey = stableKey ?? processIdentity;
        }

        public string ProcessIdentity { get; }
        public string StableKey { get; }
        public bool IsDisposed { get; private set; }

        public void Dispose() {
            IsDisposed = true;
        }
    }

    private sealed class TestResourceAdapter : IAkronReconstructionResourceAdapter {
        public TestResource LastRestored { get; private set; } = null!;

        public bool CanPersist(Type type) {
            return type == typeof(TestResource);
        }

        public AkronReconstructionResourcePayload Capture(object resource) {
            TestResource testResource = (TestResource) resource;
            return new AkronReconstructionResourcePayload {
                Kind = "test-resource",
                Name = testResource.StableKey,
                Bytes = System.Text.Encoding.UTF8.GetBytes(testResource.ProcessIdentity)
            };
        }

        public object Restore(AkronReconstructionResourcePayload payload, object freshResource) {
            LastRestored = new TestResource(System.Text.Encoding.UTF8.GetString(payload.Bytes), payload.Name);
            return LastRestored;
        }

        public bool Verify(AkronReconstructionResourcePayload payload, object resource) {
            TestResource testResource = (TestResource) resource;
            return payload.Name == testResource.StableKey &&
                   System.Text.Encoding.UTF8.GetString(payload.Bytes) == testResource.ProcessIdentity;
        }
    }

    private struct NeverEqualValue {
        public int Number;

        public override bool Equals(object? obj) {
            return false;
        }

        public override int GetHashCode() {
            return Number;
        }
    }

    private sealed class UniqueTestEntity : Entity {
        public int Value;
        public TestResource Resource = null!;
    }

    private sealed class SourceIdentifiedEntity : Entity {
        public int Value;
    }

    private sealed class PeerTargetEntity : Entity {
    }

    private sealed class PeerLinkEntity : Entity {
        public PeerTargetEntity? Peer;
    }

    private sealed class PeerCollectionOwnerEntity : Entity {
        public List<PeerTargetEntity> Peers = new List<PeerTargetEntity>();
    }

    private sealed class ClutterLinkedEntity : Entity {
        public Dictionary<ClutterLinkedEntity, bool> HasBelow = new Dictionary<ClutterLinkedEntity, bool>();
        public List<ClutterLinkedEntity> Above = new List<ClutterLinkedEntity>();
        public int Value;
    }

    private sealed class IteratorOwnerEntity : Entity {
        public int Value;

        public IEnumerable Routine() {
            yield return Value;
        }
    }

    private sealed class NestedStateOwnerEntity : Entity {
        public sealed class NestedState {
            public int Value;

            public int ReadValue() {
                return Value;
            }
        }

        public NestedState? State;
    }

    private sealed class ColliderArrayOwnerEntity : Entity {
        public Collider[] Colliders = null!;
    }

    private sealed class AliasedColliderEntityRoot {
        public List<ColliderArrayOwnerEntity> RenderItems = new List<ColliderArrayOwnerEntity>();
        public EntityList Entities = null!;
    }

    private sealed class SourceEntityListOwnerRoot {
        public EntityList Entities = null!;
    }

    private sealed class TwoEntityListsRoot {
        public EntityList First = null!;
        public EntityList Second = null!;
    }

    private sealed class SavedSceneRoot {
        public Scene Scene = null!;
        public EntityList Entities = null!;
        public Sprite? FreshSprite;
    }

    private sealed class UnrelatedEntityArrayRoot {
        public Entity[] Unrelated = Array.Empty<Entity>();
        public Scene Scene = null!;
        public EntityList Entities = null!;
    }

    private sealed class IteratorAliasSceneRoot {
        public Scene Scene = null!;
        public EntityList Entities = null!;
        public IEnumerator[] Unrelated = Array.Empty<IEnumerator>();
    }

    private sealed class DerivedTrackedScene : Scene {
    }

    private sealed class RendererRuntimeRecordRoot {
        public DisplacementRenderer Renderer = null!;
        public Ease.Easer? AuthenticatedEaser;
    }

    private sealed class RendererComponentIndexRoot {
        public Scene Scene = null!;
        public Entity Entity = null!;
        public LightingRenderer Renderer = null!;
    }

    private sealed class TrailPlaybackRoot {
        public Scene Scene = null!;
        public EntityList Entities = null!;
        public TrailManager.Snapshot Snapshot = null!;
    }

    private sealed class PlaybackGhostRoom {
        public EntityList Entities = null!;
        public TrailManager Manager = null!;
        public PlayerPlayback Ghost = null!;
        public TrailManager.Snapshot GhostTrail = null!;
        public Player Player = null!;
        public TrailManager.Snapshot PlayerTrail = null!;
    }

    // A room shaped like the one the exclusion exists for: a map-placed playback
    // ghost that is trailing, a player that is also trailing, and the manager that
    // owns both trail slots.
    private static PlaybackGhostRoom CreatePlaybackGhostRoom() {
        EntityList entities = CreateDetachedEntityList();
        TrailManager manager = CreateUninitializedEntity<TrailManager>();
        TrailManager.Snapshot[] slots = new TrailManager.Snapshot[64];
        SetRuntimeField(manager, "snapshots", slots);
        AddDetachedEntity(entities, manager);

        PlayerPlayback ghost = CreateUninitializedEntity<PlayerPlayback>();
        TrailManager.Snapshot ghostTrail = CreateTrailSnapshotFor(ghost, manager, slots, index: 3);
        AddDetachedEntity(entities, ghost);
        AddDetachedEntity(entities, ghostTrail);

        Player player = CreateUninitializedEntity<Player>();
        TrailManager.Snapshot playerTrail = CreateTrailSnapshotFor(player, manager, slots, index: 7);
        AddDetachedEntity(entities, player);
        AddDetachedEntity(entities, playerTrail);

        return new PlaybackGhostRoom {
            Entities = entities,
            Manager = manager,
            Ghost = ghost,
            GhostTrail = ghostTrail,
            Player = player,
            PlayerTrail = playerTrail
        };
    }

    private static TrailManager.Snapshot CreateTrailSnapshotFor(
        Entity owner,
        TrailManager manager,
        TrailManager.Snapshot[] slots,
        int index
    ) {
        ComponentList ownerComponents = CreateDetachedComponentList(owner);
        PlayerSprite sprite = (PlayerSprite) RuntimeHelpers.GetUninitializedObject(typeof(PlayerSprite));
        PlayerHair hair = (PlayerHair) RuntimeHelpers.GetUninitializedObject(typeof(PlayerHair));
        SetRuntimeField(sprite, "<Entity>k__BackingField", owner);
        SetRuntimeField(hair, "<Entity>k__BackingField", owner);
        hair.Sprite = sprite;
        SetRuntimeField(ownerComponents, "components", new List<Component> { hair, sprite });
        SetRuntimeField(ownerComponents, "current", new HashSet<Component> { hair, sprite });

        TrailManager.Snapshot snapshot = CreateUninitializedEntity<TrailManager.Snapshot>();
        InitializeEmptyComponentList(snapshot);
        snapshot.Manager = manager;
        snapshot.Index = index;
        snapshot.Sprite = sprite;
        snapshot.Hair = hair;
        slots[index] = snapshot;
        return snapshot;
    }

    // Production's root chain is AkronPersistentRuntimeState.Level, so a saved ghost's
    // document path starts at $.<Level>k__BackingField. The auto-property gives the
    // same first step here.
    private sealed class PlaybackGhostReloadRoot {
        public Level Level { get; set; } = null!;
    }

    private sealed class PlaybackGhostReloadRoom {
        public PlaybackGhostReloadRoot Root = null!;
        public Level Level = null!;
        public EntityList Entities = null!;
        public PlayerPlayback Ghost = null!;
        public PlayerPlayback? DestroyedGhost;
        public TrailManager.Snapshot Snapshot = null!;
    }

    // Builds a room in the order Monocle keeps EntityList.entities in.
    // EntityList.CompareDepth is Math.Sign(b.actualDepth - a.actualDepth), so the list
    // runs from the highest Depth to the lowest, and a trail snapshot takes its owner's
    // Depth + 1 (PlayerPlayback.Update and TrailManager.Add both pass entity.Depth + 1).
    // A trailing ghost's snapshot therefore sits at 9009, before the ghost at 9008, and
    // the capture walk reaches the ghost through TrailManager.Snapshot.Sprite.<Entity>
    // instead of through its own entity-list slot. That relocation is one half of the
    // failure these tests are about.
    private static PlaybackGhostReloadRoom CreateTrailingGhostRoom(bool ghostIsTrailing, float ghostTime) {
        Level level = (Level) RuntimeHelpers.GetUninitializedObject(typeof(Level));
        EntityList entities = LinkSceneEntities(level, CreateDetachedEntityList());

        PlayerPlayback ghost = CreatePlaybackGhost(level, sourceId: 42, depth: 9008, time: ghostTime);
        Entity trailer = CreateGhostRoomTrailer(level);
        TrailManager manager = CreateGhostRoomTrailManager(level, out TrailManager.Snapshot[] slots);

        Entity trailOwner = ghostIsTrailing ? ghost : trailer;
        TrailManager.Snapshot snapshot = CreateTrailSnapshotFrom(
            level,
            manager,
            slots,
            index: 0,
            owner: trailOwner,
            depth: GetEntityDepth(trailOwner) + 1);

        if (ghostIsTrailing) {
            AddDetachedEntity(entities, snapshot);
        }
        AddDetachedEntity(entities, ghost);
        AddDetachedEntity(entities, manager);
        if (!ghostIsTrailing) {
            AddDetachedEntity(entities, snapshot);
        }
        AddDetachedEntity(entities, trailer);

        return new PlaybackGhostReloadRoom {
            Root = new PlaybackGhostReloadRoot { Level = level },
            Level = level,
            Entities = entities,
            Ghost = ghost,
            Snapshot = snapshot
        };
    }

    // The room Akron's fresh-room reload hands the rebuild. UnloadLevel removes every
    // entity whose Tag does not carry Tags.Global, so the ghost is destroyed - Scene
    // null, gone from entities - while TrailManager and TrailManager.Snapshot keep
    // Tags.Global and survive. Nothing clears the snapshot's reference to the destroyed
    // ghost's PlayerSprite, and LoadLevel rebuilds the ghost from the same map entity,
    // so it carries the same EntityID.
    //
    // trailBelongsToDestroyedGhost false is the room the reload produces once it clears
    // the trails first the way Celeste.Level.Reload does: whatever snapshots the room
    // still holds belong to entities the room still holds.
    private static PlaybackGhostReloadRoom CreateReloadedGhostRoom(bool trailBelongsToDestroyedGhost) {
        Level level = (Level) RuntimeHelpers.GetUninitializedObject(typeof(Level));
        EntityList entities = LinkSceneEntities(level, CreateDetachedEntityList());

        PlayerPlayback destroyedGhost = CreatePlaybackGhost(scene: null, sourceId: 42, depth: 9008, time: 0f);
        PlayerPlayback ghost = CreatePlaybackGhost(level, sourceId: 42, depth: 9008, time: 0f);
        Entity trailer = CreateGhostRoomTrailer(level);
        TrailManager manager = CreateGhostRoomTrailManager(level, out TrailManager.Snapshot[] slots);

        TrailManager.Snapshot snapshot = CreateTrailSnapshotFrom(
            level,
            manager,
            slots,
            index: 0,
            owner: trailBelongsToDestroyedGhost ? destroyedGhost : ghost,
            depth: 9009);

        AddDetachedEntity(entities, snapshot);
        AddDetachedEntity(entities, ghost);
        AddDetachedEntity(entities, manager);
        AddDetachedEntity(entities, trailer);

        return new PlaybackGhostReloadRoom {
            Root = new PlaybackGhostReloadRoot { Level = level },
            Level = level,
            Entities = entities,
            Ghost = ghost,
            DestroyedGhost = destroyedGhost,
            Snapshot = snapshot
        };
    }

    // The reloaded room from the two tests about a ghost the room rebuilt under a
    // different EntityID. Everything else about it matches CreateReloadedGhostRoom;
    // only the ghost's id moved, which is what makes the saved ghost unpairable.
    private static PlaybackGhostReloadRoom CreateReloadedGhostRoomWithRenumberedGhost() {
        Level level = (Level) RuntimeHelpers.GetUninitializedObject(typeof(Level));
        EntityList entities = LinkSceneEntities(level, CreateDetachedEntityList());
        PlayerPlayback liveGhost = CreatePlaybackGhost(level, sourceId: 43, depth: 9008, time: 0f);
        Entity trailer = CreateGhostRoomTrailer(level);
        TrailManager manager = CreateGhostRoomTrailManager(level, out TrailManager.Snapshot[] slots);
        TrailManager.Snapshot snapshot = CreateTrailSnapshotFrom(level, manager, slots, 0, liveGhost, depth: 9009);
        AddDetachedEntity(entities, snapshot);
        AddDetachedEntity(entities, liveGhost);
        AddDetachedEntity(entities, manager);
        AddDetachedEntity(entities, trailer);
        return new PlaybackGhostReloadRoom {
            Root = new PlaybackGhostReloadRoot { Level = level },
            Level = level,
            Entities = entities,
            Ghost = liveGhost,
            Snapshot = snapshot
        };
    }

    // The same ghost room with a second trail, owned by a ghost that does pair. Both
    // trails put their owner's <Scene> edge at the same wildcarded path -
    // entities._items[*].Sprite.<Entity>.<Scene> - so the two edges draw on one
    // occurrence budget. That is the only thing this room adds over
    // CreateTrailingGhostRoom.
    //
    // unpairableFirst swaps which of the two entities the document reaches first, and
    // nothing else: the depths that decide EntityList.CompareDepth order swap with it,
    // and so does which trail TrailManager still points at, because the trail the room
    // holds first has to be the registered one either way. Without that second swap the
    // leading trail's Manager.snapshots edge reaches the other ghost first and moves it
    // to a different wildcarded path, so the two edges stop competing and the room stops
    // being the same room.
    private static PlaybackGhostReloadRoom CreateTwoTrailGhostRoom(float ghostTime, bool unpairableFirst) {
        Level level = (Level) RuntimeHelpers.GetUninitializedObject(typeof(Level));
        EntityList entities = LinkSceneEntities(level, CreateDetachedEntityList());
        int pairedDepth = unpairableFirst ? 9008 : 9011;
        int ghostDepth = unpairableFirst ? 9011 : 9008;
        PlayerPlayback pairedGhost = CreatePlaybackGhost(level, sourceId: 7, depth: pairedDepth, time: 0f);
        PlayerPlayback ghost = CreatePlaybackGhost(level, sourceId: 42, depth: ghostDepth, time: ghostTime);
        TrailManager manager = CreateGhostRoomTrailManager(level, out TrailManager.Snapshot[] slots);
        // The trail that is not written back into manager.snapshots is a real state:
        // TrailManager recycles a slot by overwriting it while the snapshot it replaced
        // is still in the scene. It is also what keeps that snapshot's document path in
        // the entity list beside the other one rather than underneath the array.
        TrailManager.Snapshot pairedTrail = CreateTrailSnapshotFrom(
            level,
            manager,
            unpairableFirst ? new TrailManager.Snapshot[64] : slots,
            index: 0,
            owner: pairedGhost,
            depth: pairedDepth + 1);
        TrailManager.Snapshot ghostTrail = CreateTrailSnapshotFrom(
            level,
            manager,
            unpairableFirst ? slots : new TrailManager.Snapshot[64],
            index: unpairableFirst ? 0 : 1,
            owner: ghost,
            depth: ghostDepth + 1);
        if (unpairableFirst) {
            AddDetachedEntity(entities, ghostTrail);
            AddDetachedEntity(entities, ghost);
            AddDetachedEntity(entities, pairedTrail);
            AddDetachedEntity(entities, pairedGhost);
        } else {
            AddDetachedEntity(entities, pairedTrail);
            AddDetachedEntity(entities, pairedGhost);
            AddDetachedEntity(entities, ghostTrail);
            AddDetachedEntity(entities, ghost);
        }
        AddDetachedEntity(entities, manager);
        return new PlaybackGhostReloadRoom {
            Root = new PlaybackGhostReloadRoot { Level = level },
            Level = level,
            Entities = entities,
            Ghost = ghost,
            Snapshot = ghostTrail
        };
    }

    // What that room's reload leaves. The map is identical on both sides and lays out
    // 42, 7 and 8; the session built 42 and 7 when the slot was set and 7 and 8 at the
    // reload, which is the ordinary way one of two same-typed map entities changes
    // between two loads of one room - Session.DoNotLoad, a flag a mod reads in its
    // EntityLoader, a follower the room does not rebuild. No id moved: Everest derives
    // every EntityID from the map file's own id attribute, so an entity cannot be
    // renumbered without the map changing, and a changed map is the other rule's.
    //
    // So the saved ghost 42 cannot pair, and both surviving trails belong to the ghost
    // that can - it dashed twice and ghost 8 has not trailed yet. Both trails hold that
    // one ghost's PlayerSprite, so the reload's walk reaches the Level through a trail
    // exactly once however many trails point at it, while the second trail still
    // records that a PlayerPlayback sits at the end of that path. One occurrence
    // against the saved document's two edges is what makes them compete.
    private static PlaybackGhostReloadRoom CreateTwoTrailReloadedGhostRoomTheSessionBuiltDifferently(
        bool unpairableFirst
    ) {
        Level level = (Level) RuntimeHelpers.GetUninitializedObject(typeof(Level));
        EntityList entities = LinkSceneEntities(level, CreateDetachedEntityList());
        int pairedDepth = unpairableFirst ? 9008 : 9011;
        int ghostDepth = unpairableFirst ? 9011 : 9008;
        PlayerPlayback pairedGhost = CreatePlaybackGhost(level, sourceId: 7, depth: pairedDepth, time: 0f);
        PlayerPlayback liveGhost = CreatePlaybackGhost(level, sourceId: 8, depth: ghostDepth, time: 0f);
        TrailManager manager = CreateGhostRoomTrailManager(level, out TrailManager.Snapshot[] slots);
        TrailManager.Snapshot pairedTrail = CreateTrailSnapshotFrom(
            level,
            manager,
            unpairableFirst ? new TrailManager.Snapshot[64] : slots,
            index: 0,
            owner: pairedGhost,
            depth: pairedDepth + 1);
        TrailManager.Snapshot secondTrail = CreateTrailSnapshotFrom(
            level,
            manager,
            unpairableFirst ? slots : new TrailManager.Snapshot[64],
            index: unpairableFirst ? 0 : 1,
            owner: pairedGhost,
            depth: ghostDepth + 1);
        if (unpairableFirst) {
            AddDetachedEntity(entities, secondTrail);
            AddDetachedEntity(entities, liveGhost);
            AddDetachedEntity(entities, pairedTrail);
            AddDetachedEntity(entities, pairedGhost);
        } else {
            AddDetachedEntity(entities, pairedTrail);
            AddDetachedEntity(entities, pairedGhost);
            AddDetachedEntity(entities, secondTrail);
            AddDetachedEntity(entities, liveGhost);
        }
        AddDetachedEntity(entities, manager);
        return new PlaybackGhostReloadRoom {
            Root = new PlaybackGhostReloadRoot { Level = level },
            Level = level,
            Entities = entities,
            Ghost = liveGhost,
            Snapshot = secondTrail
        };
    }

    private static AkronReconstructionRestore RestoreTwoTrailGhostDocumentInto(
        PlaybackGhostReloadRoom fresh,
        bool unpairableFirst,
        int[] mapIdsWhenSet,
        int[] mapIdsAtReload
    ) {
        PlaybackGhostReloadRoom saved = CreateTwoTrailGhostRoom(2.5f, unpairableFirst);
        PlaybackGhostReloadRoom baseline = CreateTwoTrailGhostRoom(0f, unpairableFirst);
        TestMapPlacement placement = new TestMapPlacement()
            .Place(baseline.Root, "CANADIAN_00", mapIdsWhenSet)
            .Place(fresh.Root, "CANADIAN_00", mapIdsAtReload);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(
            IsLiveResource,
            getMapPlacedEntityIds: placement.Ids);
        AkronReconstructionCapture capture = graph.Capture(saved.Root, baseline.Root);
        Assert.True(capture.Success, capture.Error);
        capture.Document.Room = "CANADIAN_00";
        return graph.Restore(capture.Document, fresh.Root);
    }

    // mapIdsWhenSet is what the map laid out in CANADIAN_00 when the slot was set,
    // and mapIdsAtReload what it lays out now. Leaving both out gives a graph with no
    // map at all, which is what every test here wanted before the map became evidence.
    private static AkronReconstructionRestore RestoreTrailingGhostDocumentInto(
        PlaybackGhostReloadRoom fresh,
        bool savedGhostIsTrailing = true,
        int[]? mapIdsWhenSet = null,
        int[]? mapIdsAtReload = null
    ) {
        PlaybackGhostReloadRoom saved = CreateTrailingGhostRoom(savedGhostIsTrailing, ghostTime: 2.5f);
        PlaybackGhostReloadRoom baseline = CreateTrailingGhostRoom(savedGhostIsTrailing, ghostTime: 0f);
        TestMapPlacement? placement = mapIdsWhenSet == null
            ? null
            : new TestMapPlacement()
                .Place(baseline.Root, "CANADIAN_00", mapIdsWhenSet)
                .Place(fresh.Root, "CANADIAN_00", mapIdsAtReload ?? mapIdsWhenSet);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(
            IsLiveResource,
            getMapPlacedEntityIds: placement == null ? null : placement.Ids);
        AkronReconstructionCapture capture = graph.Capture(saved.Root, baseline.Root);
        Assert.True(capture.Success, capture.Error);
        capture.Document.Room = "CANADIAN_00";
        return graph.Restore(capture.Document, fresh.Root);
    }

    private static PlayerPlayback CreatePlaybackGhost(Scene? scene, int sourceId, int depth, float time) {
        PlayerPlayback ghost = CreateUninitializedEntity<PlayerPlayback>();
        (PlayerSprite sprite, PlayerHair hair) = AttachGhostRoomHairAndSprite(ghost);
        ghost.Sprite = sprite;
        ghost.Hair = hair;
        SetRuntimeField(ghost, "<Scene>k__BackingField", scene);
        SetRuntimeField(ghost, "<SourceId>k__BackingField", CreateEntityId("CANADIAN_00", sourceId));
        SetGhostRoomDepth(ghost, depth);
        SetRuntimeField(ghost, "time", time);
        return ghost;
    }

    // Stands in for the room's other trailing entity. Celeste.Player cannot be built
    // here: its own trail comes from a live dash, and what this needs is only a second
    // map entity whose PlayerSprite a snapshot can hold.
    private static Entity CreateGhostRoomTrailer(Level level) {
        Entity trailer = CreateUninitializedEntity<Entity>();
        AttachGhostRoomHairAndSprite(trailer);
        SetRuntimeField(trailer, "<Scene>k__BackingField", level);
        SetRuntimeField(trailer, "<SourceId>k__BackingField", CreateEntityId("CANADIAN_00", 7));
        SetGhostRoomDepth(trailer, 0);
        return trailer;
    }

    private static TrailManager CreateGhostRoomTrailManager(Level level, out TrailManager.Snapshot[] slots) {
        TrailManager manager = CreateUninitializedEntity<TrailManager>();
        InitializeEmptyComponentList(manager);
        SetRuntimeField(manager, "<Scene>k__BackingField", level);
        SetGhostRoomDepth(manager, 10);
        slots = new TrailManager.Snapshot[64];
        SetRuntimeField(manager, "snapshots", slots);
        SetRuntimeField(manager, "buffers", new VirtualRenderTarget[64]);
        return manager;
    }

    private static TrailManager.Snapshot CreateTrailSnapshotFrom(
        Level level,
        TrailManager manager,
        TrailManager.Snapshot[] slots,
        int index,
        Entity owner,
        int depth
    ) {
        TrailManager.Snapshot snapshot = CreateUninitializedEntity<TrailManager.Snapshot>();
        InitializeEmptyComponentList(snapshot);
        SetRuntimeField(snapshot, "<Scene>k__BackingField", level);
        SetGhostRoomDepth(snapshot, depth);
        snapshot.Manager = manager;
        snapshot.Index = index;
        // The snapshot keeps the owner's live components rather than copies of them:
        // TrailManager.Add hands it entity.Get<PlayerSprite>() and entity.Get<PlayerHair>(),
        // and BeforeRender moves and renders those very objects.
        snapshot.Sprite = GetComponentListContents(owner).OfType<PlayerSprite>().First();
        snapshot.Hair = GetComponentListContents(owner).OfType<PlayerHair>().First();
        slots[index] = snapshot;
        return snapshot;
    }

    private static (PlayerSprite Sprite, PlayerHair Hair) AttachGhostRoomHairAndSprite(Entity owner) {
        PlayerSprite sprite = (PlayerSprite) RuntimeHelpers.GetUninitializedObject(typeof(PlayerSprite));
        PlayerHair hair = (PlayerHair) RuntimeHelpers.GetUninitializedObject(typeof(PlayerHair));
        SetRuntimeField(sprite, "<Entity>k__BackingField", owner);
        SetRuntimeField(hair, "<Entity>k__BackingField", owner);
        hair.Sprite = sprite;
        hair.Nodes = new List<Vector2>();
        ComponentList components = CreateDetachedComponentList(owner);
        SetRuntimeField(components, "components", new List<Component> { hair, sprite });
        SetRuntimeField(components, "current", new HashSet<Component> { hair, sprite });
        return (sprite, hair);
    }

    private static void SetGhostRoomDepth(Entity entity, int depth) {
        SetRuntimeField(entity, "depth", depth);
        SetRuntimeField(entity, "actualDepth", (double) depth);
    }

    private static TrailPlaybackRoot CreateDetachedPlaybackTrailScene(float percent) {
        Scene scene = (Scene) RuntimeHelpers.GetUninitializedObject(typeof(DerivedTrackedScene));
        EntityList entities = LinkSceneEntities(scene, CreateDetachedEntityList());

        PlayerPlayback playback = CreateUninitializedEntity<PlayerPlayback>();
        ComponentList playbackComponents = CreateDetachedComponentList(playback);
        PlayerSprite sprite = (PlayerSprite) RuntimeHelpers.GetUninitializedObject(typeof(PlayerSprite));
        PlayerHair hair = (PlayerHair) RuntimeHelpers.GetUninitializedObject(typeof(PlayerHair));
        SetRuntimeField(sprite, "<Entity>k__BackingField", playback);
        SetRuntimeField(hair, "<Entity>k__BackingField", playback);
        hair.Sprite = sprite;
        hair.Nodes = new List<Vector2>();
        playback.Sprite = sprite;
        playback.Hair = hair;
        SetRuntimeField(playbackComponents, "components", new List<Component> { hair, sprite });
        SetRuntimeField(playbackComponents, "current", new HashSet<Component> { hair, sprite });

        TrailManager.Snapshot snapshot = CreateUninitializedEntity<TrailManager.Snapshot>();
        InitializeEmptyComponentList(snapshot);
        SetRuntimeField(snapshot, "<Scene>k__BackingField", scene);
        snapshot.Sprite = sprite;
        snapshot.Hair = hair;
        snapshot.Percent = percent;
        AddDetachedEntity(entities, snapshot);
        return new TrailPlaybackRoot { Scene = scene, Entities = entities, Snapshot = snapshot };
    }

    private static RendererComponentIndexRoot CreateRendererComponentIndexRoot(bool includeLightInRenderer) {
        Scene scene = (Scene) RuntimeHelpers.GetUninitializedObject(typeof(Scene));
        EntityList entities = LinkSceneEntities(scene, CreateDetachedEntityList());
        RendererList renderers = (RendererList) Activator.CreateInstance(
            typeof(RendererList),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: new object?[] { scene },
            culture: null
        )!;
        renderers.Renderers = new List<Renderer>();
        SetRuntimeField(renderers, "adding", new List<Renderer>());
        SetRuntimeField(renderers, "removing", new List<Renderer>());
        SetRuntimeField(renderers, "scene", scene);
        typeof(Scene).GetField("<RendererList>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(scene, renderers);

        Entity entity = CreateUninitializedEntity<Entity>();
        InitializeEmptyComponentList(entity);
        typeof(Entity).GetField("<Scene>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(entity, scene);
        VertexLight light = (VertexLight) RuntimeHelpers.GetUninitializedObject(typeof(VertexLight));
        typeof(Component).GetField("<Entity>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(light, entity);
        List<Component> components = new List<Component> { light };
        ComponentList componentList = GetRuntimeField<ComponentList>(entity, "<Components>k__BackingField");
        SetRuntimeField(componentList, "components", components);
        SetRuntimeField(componentList, "current", new HashSet<Component>(components));
        AddDetachedEntity(entities, entity);

        LightingRenderer renderer = (LightingRenderer) RuntimeHelpers.GetUninitializedObject(typeof(LightingRenderer));
        VertexLight[] lights = new VertexLight[8];
        if (includeLightInRenderer) {
            lights[5] = light;
        }
        typeof(LightingRenderer).GetField("lights", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(renderer, lights);
        renderers.Renderers.Add(renderer);
        return new RendererComponentIndexRoot { Scene = scene, Entity = entity, Renderer = renderer };
    }

    private static SeekerBarrierRenderer CreateSeekerBarrierRenderer(int edgeCount) {
        SeekerBarrierRenderer renderer = CreateUninitializedEntity<SeekerBarrierRenderer>();
        InitializeEmptyComponentList(renderer);
        SetRuntimeField(renderer, "list", new List<SeekerBarrier>());
        Type edgeType = typeof(SeekerBarrierRenderer).GetNestedType(
            "Edge", BindingFlags.NonPublic) ?? throw new InvalidOperationException("SeekerBarrierRenderer.Edge is unavailable.");
        System.Collections.IList edges = (System.Collections.IList) Activator.CreateInstance(
            typeof(List<>).MakeGenericType(edgeType))!;
        for (int index = 0; index < edgeCount; index++) {
            edges.Add(RuntimeHelpers.GetUninitializedObject(edgeType));
        }
        SetRuntimeField(renderer, "edges", edges);
        return renderer;
    }

    private static VertexLight[] GetLightingRendererLights(LightingRenderer renderer) {
        return (VertexLight[]) typeof(LightingRenderer)
            .GetField("lights", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(renderer)!;
    }

    private static DisplacementRenderer CreateDisplacementRenderer(int collectionCapacity = 0) {
        DisplacementRenderer renderer =
            (DisplacementRenderer) RuntimeHelpers.GetUninitializedObject(typeof(DisplacementRenderer));
        typeof(DisplacementRenderer).GetField("points", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(renderer, new List<DisplacementRenderer.Burst>(collectionCapacity));
        return renderer;
    }

    private static List<DisplacementRenderer.Burst> GetDisplacementBursts(DisplacementRenderer renderer) {
        return (List<DisplacementRenderer.Burst>) typeof(DisplacementRenderer)
            .GetField("points", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(renderer)!;
    }

    private static EntityList CreateDetachedEntityList() {
        EntityList entities = (EntityList) Activator.CreateInstance(
            typeof(EntityList),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: new object?[] { null },
            culture: null)!;
        SetRuntimeField(entities, "entities", new List<Entity>());
        SetRuntimeField(entities, "toAdd", new List<Entity>());
        SetRuntimeField(entities, "toAwake", new List<Entity>());
        SetRuntimeField(entities, "toRemove", new List<Entity>());
        SetRuntimeField(entities, "current", new HashSet<Entity>());
        SetRuntimeField(entities, "adding", new HashSet<Entity>());
        SetRuntimeField(entities, "removing", new HashSet<Entity>());
        return entities;
    }

    private static ComponentList CreateDetachedComponentList(Entity entity) {
        ComponentList components = (ComponentList) Activator.CreateInstance(
            typeof(ComponentList),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: new object?[] { entity },
            culture: null)!;
        SetRuntimeField(components, "<Entity>k__BackingField", entity);
        SetRuntimeField(components, "components", new List<Component>());
        SetRuntimeField(components, "toAdd", new List<Component>());
        SetRuntimeField(components, "toRemove", new List<Component>());
        SetRuntimeField(components, "current", new HashSet<Component>());
        SetRuntimeField(components, "adding", new HashSet<Component>());
        SetRuntimeField(components, "removing", new HashSet<Component>());
        SetRuntimeField(entity, "<Components>k__BackingField", components);
        return components;
    }

    private static void SetRuntimeField(object owner, string name, object? value) {
        GetRuntimeFieldInfo(owner.GetType(), name).SetValue(owner, value);
    }

    private static T GetRuntimeField<T>(object owner, string name) {
        return (T) GetRuntimeFieldInfo(owner.GetType(), name).GetValue(owner)!;
    }

    private static T GetRuntimeStaticField<T>(Type type, string name) {
        FieldInfo field = type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(type.FullName + "." + name + " is unavailable.");
        return (T) field.GetValue(null)!;
    }

    private static void SetRuntimeStaticField(Type type, string name, object? value) {
        FieldInfo field = type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(type.FullName + "." + name + " is unavailable.");
        field.SetValue(null, value);
    }

    private static int GetEntityDepth(Entity entity) {
        return GetRuntimeField<int>(entity, "depth");
    }

    private static LevelData GetMapRoom(MapData map, string roomName) {
        return map.Levels.Single(room => string.Equals(room.Name, roomName, StringComparison.Ordinal));
    }

    private static List<Entity> GetEntityListContents(EntityList entities) {
        return GetRuntimeField<List<Entity>>(entities, "entities");
    }

    private static List<Component> GetComponentListContents(Entity entity) {
        return GetRuntimeField<List<Component>>(
            GetRuntimeField<ComponentList>(entity, "<Components>k__BackingField"),
            "components");
    }

    private static FieldInfo GetRuntimeFieldInfo(Type type, string name) {
        for (Type? current = type; current != null; current = current.BaseType) {
            FieldInfo? field = current.GetField(name, RuntimeInstanceFields | BindingFlags.DeclaredOnly);
            if (field != null) {
                return field;
            }
        }
        throw new InvalidOperationException(type.FullName + "." + name + " is unavailable.");
    }

    private static EntityID CreateEntityId(string room, int id) {
        return new EntityID { Level = room, ID = id };
    }

    private static Chooser<string>.Choice CreateChoice(string value, float weight) {
        Chooser<string>.Choice choice = (Chooser<string>.Choice) RuntimeHelpers.GetUninitializedObject(
            typeof(Chooser<string>.Choice));
        choice.Value = value;
        choice.Weight = weight;
        return choice;
    }

    private static Ease.Easer CreateBuiltInInvertedEaser(Ease.Easer innerEaser) {
        Type closureType = typeof(Ease).GetNestedType(
            "<>c__DisplayClass35_0",
            BindingFlags.NonPublic) ?? throw new InvalidOperationException("Ease.Invert closure is unavailable.");
        object closure = RuntimeHelpers.GetUninitializedObject(closureType);
        SetRuntimeField(closure, "easer", innerEaser);
        MethodInfo method = closureType.GetMethod(
            "<Invert>b__0",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Ease.Invert callback is unavailable.");
        return (Ease.Easer) method.CreateDelegate(typeof(Ease.Easer), closure);
    }

    private static float QuadraticEase(float value) {
        return value * value;
    }

    private static EntityList LinkEntityListToScene(EntityList entities, Scene scene) {
        typeof(EntityList).GetField("<Scene>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(entities, scene);
        return entities;
    }

    private static EntityList LinkSceneEntities(Scene scene, EntityList entities) {
        LinkEntityListToScene(entities, scene);
        typeof(Scene).GetField("<Entities>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(scene, entities);
        return entities;
    }

    private static void AddDetachedEntity(EntityList entities, Entity entity) {
        GetRuntimeField<List<Entity>>(entities, "entities").Add(entity);
        GetRuntimeField<HashSet<Entity>>(entities, "current").Add(entity);
    }

    private static void SetEntityListCapacity(EntityList entities, int capacity) {
        GetRuntimeField<List<Entity>>(entities, "entities").Capacity = capacity;
    }

    private static (SavedSceneRoot Root, SlashFx? Slash) CreateTrackedRuntimeEntityScene(bool includeSlash) {
        Scene scene = (Scene) RuntimeHelpers.GetUninitializedObject(typeof(DerivedTrackedScene));
        EntityList entities = LinkSceneEntities(scene, CreateDetachedEntityList());
        SlashFx? slash = null;
        Sprite? component = null;
        if (includeSlash) {
            slash = CreateUninitializedEntity<SlashFx>();
            InitializeEmptyComponentList(slash);
            component = (Sprite) RuntimeHelpers.GetUninitializedObject(typeof(Sprite));
            typeof(Component).GetField("<Entity>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(component, slash);
            MethodInfo onFinish = typeof(SlashFx).GetMethod(
                "<.ctor>b__2_0",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            component.OnFinish = (Action<string>) onFinish.CreateDelegate(typeof(Action<string>), slash);
            List<Component> components = new List<Component> { component };
            ComponentList componentList = GetRuntimeField<ComponentList>(slash, "<Components>k__BackingField");
            SetRuntimeField(componentList, "components", components);
            SetRuntimeField(componentList, "current", new HashSet<Component>(components));
            typeof(Entity).GetField("<Scene>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(slash, scene);
            AddDetachedEntity(entities, slash);
        }

        Tracker tracker = (Tracker) RuntimeHelpers.GetUninitializedObject(typeof(Tracker));
        typeof(Tracker).GetField("<Entities>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(
                tracker,
                new Dictionary<Type, List<Entity>> {
                    [typeof(SlashFx)] = slash == null ? new List<Entity>() : new List<Entity> { slash }
                });
        typeof(Tracker).GetField("<Components>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(
                tracker,
                new Dictionary<Type, List<Component>> {
                    [typeof(Sprite)] = component == null
                        ? new List<Component>()
                        : new List<Component> { component }
                });
        typeof(Scene).GetField("<Tracker>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(scene, tracker);
        return (new SavedSceneRoot { Scene = scene, Entities = entities }, slash);
    }

    private static (SavedSceneRoot Root, SourceIdentifiedEntity Entity) CreateTrackedSourceEntityScene(
        bool includeTrackerEntry
    ) {
        Scene scene = (Scene) RuntimeHelpers.GetUninitializedObject(typeof(DerivedTrackedScene));
        EntityList entities = LinkSceneEntities(scene, CreateDetachedEntityList());
        SourceIdentifiedEntity entity = CreateSourceIdentifiedEntity("a00", 10, 0);
        InitializeEmptyComponentList(entity);
        SetRuntimeField(entity, "<Scene>k__BackingField", scene);
        AddDetachedEntity(entities, entity);

        Tracker tracker = (Tracker) RuntimeHelpers.GetUninitializedObject(typeof(Tracker));
        SetRuntimeField(
            tracker,
            "<Entities>k__BackingField",
            new Dictionary<Type, List<Entity>> {
                [typeof(SourceIdentifiedEntity)] = includeTrackerEntry
                    ? new List<Entity> { entity }
                    : new List<Entity>()
            });
        SetRuntimeField(tracker, "<Components>k__BackingField", new Dictionary<Type, List<Component>>());
        SetRuntimeField(scene, "<Tracker>k__BackingField", tracker);
        return (new SavedSceneRoot { Scene = scene, Entities = entities }, entity);
    }

    private static (SavedSceneRoot Root, SourceIdentifiedEntity Entity) CreateTaggedSourceEntityScene(
        bool includeTagEntry
    ) {
        Scene scene = (Scene) RuntimeHelpers.GetUninitializedObject(typeof(DerivedTrackedScene));
        EntityList entities = LinkSceneEntities(scene, CreateDetachedEntityList());
        SourceIdentifiedEntity entity = CreateSourceIdentifiedEntity("a00", 10, 0);
        InitializeEmptyComponentList(entity);
        SetRuntimeField(entity, "<Scene>k__BackingField", scene);
        AddDetachedEntity(entities, entity);

        TagLists tagLists = (TagLists) RuntimeHelpers.GetUninitializedObject(typeof(TagLists));
        List<Entity>[] lists = {
            includeTagEntry ? new List<Entity> { entity } : new List<Entity>()
        };
        SetRuntimeField(tagLists, "lists", lists);
        SetRuntimeField(tagLists, "unsorted", new bool[lists.Length]);
        SetRuntimeField(scene, "<TagLists>k__BackingField", tagLists);
        return (new SavedSceneRoot { Scene = scene, Entities = entities }, entity);
    }

    private static (SavedSceneRoot Root, SlashFx? Slash) CreateTaggedRuntimeEntityScene(bool includeSlash) {
        Scene scene = (Scene) RuntimeHelpers.GetUninitializedObject(typeof(DerivedTrackedScene));
        EntityList entities = LinkSceneEntities(scene, CreateDetachedEntityList());
        SlashFx? slash = null;
        if (includeSlash) {
            slash = CreateUninitializedEntity<SlashFx>();
            InitializeEmptyComponentList(slash);
            SetRuntimeField(slash, "<Scene>k__BackingField", scene);
            AddDetachedEntity(entities, slash);
        }

        TagLists tagLists = (TagLists) RuntimeHelpers.GetUninitializedObject(typeof(TagLists));
        List<Entity>[] lists = { slash == null ? new List<Entity>() : new List<Entity> { slash } };
        SetRuntimeField(tagLists, "lists", lists);
        SetRuntimeField(tagLists, "unsorted", new bool[lists.Length]);
        SetRuntimeField(scene, "<TagLists>k__BackingField", tagLists);
        return (new SavedSceneRoot { Scene = scene, Entities = entities }, slash);
    }

    private sealed class EntityAliasFirstOwnerRoot {
        public PassiveEntityAliasHolder Holder = null!;
        public EntityList Entities = null!;
    }

    private sealed class PassiveEntityAliasHolder {
        public Entity Alias = null!;
    }

    private sealed class ComponentOwnerRoot {
        public OwnedComponentEntity Owner = null!;
        public Component? UnrelatedAlias;
    }

    // DustGraphic's shape: a component the room pairs fresh builds a Coroutine
    // in BeforeRender, keeps it in a private field, and updates it by hand
    // without ever adding it to a ComponentList. A fresh room that never
    // rendered holds null at that slot.
    private sealed class LazyBlinkOwnerEntity : Entity {
        public LazyBlinkComponent Graphic = null!;
    }

    private sealed class LazyBlinkComponent : Component {
        public LazyBlinkComponent() : base(true, true) {
        }

        private Coroutine? blink;

        public Coroutine? Blink {
            get => blink;
            set => blink = value;
        }

        public int Blinks;

        public IEnumerator BlinkRoutine() {
            while (true) {
                Blinks++;
                yield return 0.1f;
            }
        }
    }

    private sealed class LazyDisposableHolderComponent : Component {
        public LazyDisposableHolderComponent() : base(true, true) {
        }

        private LazyDisposableComponent? blink;

        public LazyDisposableComponent? Blink {
            get => blink;
            set => blink = value;
        }
    }

    private sealed class LazyDisposableComponent : Component, IDisposable {
        public LazyDisposableComponent() : base(true, true) {
        }

        public int Ticks;

        public void Dispose() {
        }
    }

    private sealed class LazyDisposableOwnerEntity : Entity {
        public LazyDisposableHolderComponent Holder = null!;
    }

    private sealed class LazyBlinkOwnerRoot {
        public LazyBlinkOwnerEntity Owner = null!;
    }

    // DustGraphic.Eyeballs' shape: an entity the room builds on first use keeps
    // a reference to the live component that built it in a declared field.
    private sealed class EyeballsWatcherEntity : Entity {
        public LazyBlinkComponent Dust = null!;
    }

    // CrushBlock's shape: a map-placed entity runs an attack routine whose
    // iterator hoists a lambda closure (<>8__1). The fresh room's routine is
    // idle, so nothing structural vouches for the mid-flight closure.
    private sealed class ClosureRoutineEntity : Entity {
        public Coroutine? Routine;
        public OwnedTestComponent? PendingComponent;
        public int Steps;

        public IEnumerator AttackSequence() {
            OwnedTestComponent? component = PendingComponent;
            int steps = 0;
            Action advance = () => {
                steps++;
                if (component != null) {
                    component.Value++;
                }
            };
            while (true) {
                advance();
                Steps = steps;
                yield return null;
            }
        }
    }

    private sealed class RelocatedCallbackEntity : Entity {
        public Action? Stolen;
    }

    private sealed class DynamicDataHolder {
        public MonoMod.Utils.DynamicData? Data;
        public int Value;
    }

    private sealed class DynamicDataSubject {
        public int Exposed = 5;
    }

    private sealed class RegisteredEffectRoot {
        public Effect? Effect;
    }

    private static class RegisteredEffectFixture {
        internal static IDictionary<string, Effect> Effects = new Dictionary<string, Effect>();
    }

    // Assembly.GetTypes returns this open definition. Its closed registry field shape
    // looks valid, but FieldInfo.GetValue cannot read it without a concrete T.
    private static class OpenGenericRegisteredEffectFixture<T> {
        internal static IDictionary<string, Effect> Effects = new Dictionary<string, Effect>();
    }

    private sealed class NonEnumerableEffectRegistry : Dictionary<string, Effect>, IDictionary<string, Effect> {
        internal NonEnumerableEffectRegistry(IEnumerable<KeyValuePair<string, Effect>> entries)
            : base(entries.ToDictionary(entry => entry.Key, entry => entry.Value)) {
        }

        IEnumerator<KeyValuePair<string, Effect>> IEnumerable<KeyValuePair<string, Effect>>.GetEnumerator() {
            throw new InvalidOperationException("The registry was enumerated more than once.");
        }
    }

    private delegate IEnumerator HookRoutine(OrigHookRoutine orig);
    private delegate IEnumerator OrigHookRoutine();

    private sealed class HookIteratorRoot {
        public Coroutine Routine = null!;
    }

    private sealed class HookIteratorOwner {
        // The production repro reaches ILHook and FieldInfo instead. IntPtr is
        // the smallest process-only value that proves capture did not walk the
        // dormant clone after recognizing the owner anchor.
        public IntPtr ProcessHandle = new IntPtr(1);

        public IEnumerator RunHook(OrigHookRoutine orig) {
            _ = ProcessHandle;
            yield return null;
        }
    }

    // LightningRenderer's shape: an entity builds nested plain objects in its
    // constructor, each running its own Coroutine by hand without any
    // ComponentList carrying it.
    private sealed class BoltOwnerEntity : Entity {
        public List<BoltState> Bolts = new List<BoltState>();

        public sealed class BoltState {
            public Coroutine Routine = null!;
            public int Flashes;

            public IEnumerator Run() {
                while (true) {
                    Flashes++;
                    yield return null;
                }
            }
        }
    }

    private sealed class LazyDisposableOwnerRoot {
        public LazyDisposableOwnerEntity Owner = null!;
    }

    private sealed class ComponentArrayAliasRoot {
        public OwnedComponentEntity Owner = null!;
        public OwnedTestComponent[] Components = null!;
    }

    private sealed class TrackerComponentAliasRoot {
        public TrackedComponentEntity Owner = null!;
        public Scene Scene = null!;
    }

    private sealed class TrackedComponentEntity : Entity {
        public LevelEndingHook Owned = null!;
    }

    private sealed class ComponentAliasFirstRoot {
        public Component? Alias;
        public OwnedComponentEntity Owner = null!;
    }

    private sealed class ComponentCapturedFreshRoot {
        public ExactSlotObject Target = null!;
        public OwnedComponentEntity Owner = null!;
    }

    private sealed class ExactTypedAliasRoot {
        public Entity General = null!;
        public TypedAliasEntity Exact = null!;
    }

    private sealed class TypedAliasEntity : Entity {
        public int Value;
    }

    private sealed class EntityListOwnerRoot {
        public EntityList Entities = null!;
        public Entity CanonicalAlias = null!;
        public Entity? UnrelatedAlias;
        public HashSet<Entity> ActiveEntities = new HashSet<Entity>();
    }

    private sealed class OwnedComponentEntity : Entity {
        public OwnedTestComponent Owned = null!;
        public List<OwnedTestComponent> Cached = new List<OwnedTestComponent>();
    }

    private sealed class OwnedTestComponent : Component {
        public OwnedTestComponent() : base(true, true) {
        }

        public int Value;
        public ExactSlotObject? Captured;

        public void SetValue(int value) {
            Value = value;
        }
    }

    private sealed class OwnedStateRoot {
        public OwnedStateEntity Owner = null!;
    }

    // The declaration order is load-bearing and the test that uses these fields pins it:
    // the capture walk reaches an entity's fields in declaration order, so Running first
    // makes the state the reload does build the node that pairs with it, and Last after
    // Pending keeps the Last edge an edge into another node's slot.
    private sealed class OwnedStateEntity : Entity {
        public OwnedState Running = null!;
        public OwnedState Pending = null!;
        public OwnedState Last = null!;

        // Nested inside its owner, which is what the owned-nested-state licence asks
        // for, and declares a method so it is not read as a passive data record - one
        // of those reconstructs with no fresh-room evidence at all and would never
        // reach the displacement question.
        internal sealed class OwnedState {
            public int Value;

            public void Advance() {
                Value++;
            }
        }
    }

    private sealed class OwnershipProvedEdgeRoot {
        public Scene Scene = null!;
        public EntityList Entities = null!;
    }

    private sealed class RuntimeStateHolderEntity : Entity {
    }

    private sealed class RuntimeStateHolderComponent : Component {
        public RuntimeStateHolderComponent() : base(true, false) {
        }

        public HeldRuntimeState? State;
    }

    // Declares a method so it is not read as a passive data record. One of those
    // reconstructs with no fresh-room evidence at all, so it would never reach the
    // occurrence count these two edges compete for.
    private sealed class HeldRuntimeState {
        public int Value;

        public void Advance() {
            Value++;
        }
    }

    private sealed class MissingComponentRoot {
        public MissingComponentOwnerEntity Owner = null!;
    }

    private sealed class MissingComponentOwnerEntity : Entity {
        public List<MissingOwnedComponent> Cached = new List<MissingOwnedComponent>();
    }

    private sealed class MissingOwnedComponent : Component {
        public MissingOwnedComponent() : base(true, true) {
        }

        public int Value;
    }

    private static ExactTypedAliasRoot CreateExactTypedAliasRoot(int generalValue, int exactValue) {
        TypedAliasEntity general = CreateUninitializedEntity<TypedAliasEntity>();
        TypedAliasEntity exact = CreateUninitializedEntity<TypedAliasEntity>();
        InitializeEmptyComponentList(general);
        InitializeEmptyComponentList(exact);
        general.Value = generalValue;
        exact.Value = exactValue;
        return new ExactTypedAliasRoot { General = general, Exact = exact };
    }

    // A state machine running `yield return sprite.PlayRoutine(...)` is the
    // standard Celeste animation wait, and modded maps reach it through
    // Sprite subclasses far more often than vanilla does. The iterator is
    // Monocle.Sprite+<PlayUtil>d__40 whatever the sprite's concrete type is.
    [Fact]
    public void RestoreAllowsACompilerIteratorDeclaredOnTheOwnersBaseClass() {
        RestoreSpriteRoutineScene(spriteFirst: true, spriteType: typeof(PlayerSprite));
    }

    // The same room, with the state machine ahead of the sprite in the
    // component list, so the document reaches the iterator before the sprite
    // it captured. Resolution order is not evidence about the saved state.
    [Fact]
    public void RestoreAllowsACompilerIteratorReachedBeforeItsCapturedOwner() {
        RestoreSpriteRoutineScene(spriteFirst: false, spriteType: typeof(Sprite));
    }

    // The alias is only sound because it stays inside the one Coroutine that is
    // already running the iterator. A second Coroutine on the same entity
    // holding that same iterator is not something Monocle can produce - an
    // iterator reaches a Coroutine's stack only by being yielded into it - and
    // it has to keep failing loudly, or an authenticated iterator could be
    // planted into a component the saved room never ran it on.
    //
    // The control half is what makes the refusal attributable: the identical
    // room with the extra Coroutine present but empty restores, so the second
    // component itself is not what the graph is objecting to.
    [Fact]
    public void RestoreRefusesACompilerIteratorAliasedIntoAnotherCoroutinesStack() {
        Assert.True(RestoreSpriteRoutineSceneWithExtraCoroutine(ExtraCoroutine.Empty).Success);

        AkronReconstructionRestore restore =
            RestoreSpriteRoutineSceneWithExtraCoroutine(ExtraCoroutine.HoldingTheRunningIterator);

        Assert.False(restore.Success);
        Assert.Contains("Monocle.Sprite+<PlayUtil>d__40", restore.Error);
        Assert.Contains("coroutine-stack-iterator-alias=false", restore.Error);
    }

    // The other half of the deferral. Taking an iterator on trust because its
    // captured owner has not been reached yet is only sound if the verdict is
    // re-asked once every node is resolved, and this is the room where the
    // deferred answer is no.
    //
    // A mod adds a Sprite to a map entity while the room is played - Add(new
    // Sprite(...)) from a trigger or a routine, which is ordinary mod code - and
    // plays an animation on it. The state machine is mid `yield return
    // sprite.PlayUtil()`, so the coroutine holds Monocle.Sprite+<PlayUtil>d__40
    // with that sprite in <>4__this. A clean reload of the same room rebuilds the
    // entity and the state machine, because the map places both, and does not
    // rebuild the sprite, because nothing has run the mod code that added it. So
    // the document reconstructs the sprite as an owned component of the fresh
    // entity, and the iterator's owner is that reconstruction rather than
    // anything the reloaded room supplied.
    //
    // The state machine sits ahead of the sprite in the component list, so the
    // document reaches the iterator first and the iterator is deferred. Nothing
    // later makes its owner authentic, and the load has to refuse: an iterator
    // whose captured this is not an object the fresh room supplied is one the
    // fresh room cannot be running.
    [Fact]
    public void RestoreRefusesADeferredCompilerIteratorWhoseOwnerTheFreshRoomDoesNotSupply() {
        // Control. The same room, same document order, with the sprite placed by
        // the map so a clean reload carries it. It restores, so what the graph
        // objects to below is the missing owner and not the ordering.
        Assert.True(RestoreRuntimeAddedSpriteRoutineScene(
            cleanReloadCarriesTheSprite: true,
            spriteFirst: false).Success);

        AkronReconstructionRestore restore = RestoreRuntimeAddedSpriteRoutineScene(
            cleanReloadCarriesTheSprite: false,
            spriteFirst: false,
            out SpriteRoutineSceneRoot fresh);

        Assert.False(restore.Success);
        Assert.Contains(
            "reconstructed compiler iterator owner is not authentic to the fresh room",
            restore.Error);
        Assert.Contains("Monocle.Sprite+<PlayUtil>d__40", restore.Error);
        // The refusal runs before any assignment, so the room is still the room
        // the reload built: its entity, carrying only the component the map
        // placed.
        Entity freshEntity = Assert.Single(GetEntityListContents(fresh.Entities));
        Assert.IsType<StateMachine>(Assert.Single(GetComponentListContents(freshEntity)));
    }

    // The same room with the sprite ahead of the state machine in the component
    // list. Its owner is resolved before the iterator, so nothing is deferred and
    // the structural rule refuses it instead - the rule that already refused this
    // room before the deferral existed. Recorded here because it is the evidence
    // that the deferred refusal above replaces a refusal rather than adding one:
    // this room does not load on either side of the change, only the sentence
    // differs.
    [Fact]
    public void ACompilerIteratorWhoseOwnerTheFreshRoomDoesNotSupplyIsRefusedInEitherDocumentOrder() {
        AkronReconstructionRestore restore = RestoreRuntimeAddedSpriteRoutineScene(
            cleanReloadCarriesTheSprite: false,
            spriteFirst: true);

        Assert.False(restore.Success);
        Assert.Contains("reconstructed type is not authentic to the fresh room", restore.Error);
        Assert.Contains("Monocle.Sprite+<PlayUtil>d__40", restore.Error);
    }

    // A deferred iterator whose owner resolves late and IS the sprite the reload
    // built, with a sibling of the same entity type also mid-routine so the fresh
    // occurrence at that path is already spoken for by the sibling's own paired
    // nodes. Entity 31's animation runs on room entry, entity 32's is
    // player-triggered, so the reload catches 31 mid-routine and never 32.
    //
    // This is here as a guard, not as evidence about the structural leg: the
    // one-entity version of the same room already loads
    // (RestoreAllowsACompilerIteratorReachedBeforeItsCapturedOwner), and what the
    // sibling adds is that the occurrence is spent, so the rebuilt iterator's second
    // and third references have nothing to draw on and coroutine-stack-iterator-alias
    // is the only thing carrying them. That rule needs the node in
    // authenticatedRuntimeStateNodes, and here the owner proof is what earns it: the
    // owner resolves late and is the sprite the reload built, so
    // VerifyDeferredIteratorStates confirms the provisional membership rather than
    // withdrawing it.
    //
    // This room is also the price of the other shape considered for the raw-coroutine
    // regression - not deferring a node the structural test already cleared. That
    // shape closes the regression too, but this room's structural test does clear the
    // node, so the node would never be deferred, would never earn the membership, and
    // the room would refuse. Measured with both shapes; w51 has the table.
    //
    // 010f660 refuses this room, because it charges each of those three references
    // its own fresh occurrence.
    [Fact]
    public void ADeferredCompilerIteratorLoadsBesideASiblingRunningTheSameRoutine() {
        SpriteRoutineSceneRoot fresh = CreateSiblingSpriteRoutineScene(
            cleanReload: true,
            firstStillRunning: true,
            secondSpriteIsMapPlaced: true);
        Sprite freshSprite = (Sprite) GetComponentListContents(GetEntityListContents(fresh.Entities)[1])
            .Single(component => component is Sprite);

        AkronReconstructionRestore restore = RestoreSiblingSpriteRoutineScene(
            firstStillRunning: true,
            secondSpriteIsMapPlaced: true,
            fresh);

        Assert.True(restore.Success, restore.Error);
        Coroutine coroutine = GetRuntimeField<Coroutine>(
            (StateMachine) GetComponentListContents(GetEntityListContents(fresh.Entities)[1])
                .Single(component => component is StateMachine),
            "currentCoroutine");
        IEnumerator[] frames = GetRuntimeField<Stack<IEnumerator>>(coroutine, "enumerators").ToArray();
        Assert.Equal(2, frames.Length);
        IEnumerator routine = GetRuntimeField<Stack<IEnumerator>>(frames[1], "enums").Single();
        object current = GetRuntimeFieldInfo(routine.GetType(), "<>2__current").GetValue(routine)!;
        Assert.Equal("Monocle.Sprite+<PlayUtil>d__40", current.GetType().FullName);
        // The owner is the sprite the reload built, and all three references are one
        // object - which is why one occurrence is enough for the room to be safe.
        Assert.Same(freshSprite, GetRuntimeFieldInfo(current.GetType(), "<>4__this").GetValue(current));
        Assert.Same(current, GetRuntimeFieldInfo(frames[1].GetType(), "current").GetValue(frames[1]));
        Assert.Same(current, GetRuntimeField<Stack<IEnumerator>>(frames[0], "enums").Single());
    }

    // The same rooms with entity 32's sprite added by mod code during play, so a
    // clean reload does not carry it and the rebuilt iterator's captured owner is a
    // reconstruction - authentic as an owned component of the fresh entity, which is
    // neither of the two things IsAuthenticatedCompilerIteratorOwner accepts.
    //
    // These two are the coroutine-stack half of the picture, and unlike the raw
    // coroutine above they are NOT a regression: 010f660 refuses them too, and for
    // the same reason this build now does, because it charges each of the three
    // references Everest's Flattened leaves for one mid-flight iterator and the room
    // has at most one occurrence to give.
    //
    // What these two pin is the withdrawal in VerifyDeferredIteratorStates. The
    // iterator's own structural licence is there - the sibling supplies an occurrence
    // at that path - so the node is admitted and only the iterator licence is
    // refused. coroutine-stack-iterator-alias=false in the message is that
    // withdrawal: keep the membership instead and this room loads, because the alias
    // rule carries the second and third references and nothing else is asked. That is
    // the widening w50 measured into a wrong restore, so the assertion is on the
    // reason and not only on the refusal.
    //
    // Both of the sibling's own states are covered because that is what decides
    // whether the fresh occurrence is spent, and neither answer changes the verdict -
    // only which of the three references runs out first, which is why the edge in the
    // message is not asserted.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADeferredCompilerIteratorWhoseOwnerIsOnlyAnOwnedComponentIsRefusedBesideThatSibling(
        bool firstStillRunning
    ) {
        SpriteRoutineSceneRoot fresh = CreateSiblingSpriteRoutineScene(
            cleanReload: true,
            firstStillRunning,
            secondSpriteIsMapPlaced: false);

        AkronReconstructionRestore restore = RestoreSiblingSpriteRoutineScene(
            firstStillRunning,
            secondSpriteIsMapPlaced: false,
            fresh);

        Assert.False(restore.Success);
        Assert.Contains(
            "reconstructed reference edge is not authentic to the fresh room",
            restore.Error);
        Assert.Contains("Monocle.Sprite+<PlayUtil>d__40", restore.Error);
        Assert.Contains("coroutine-stack-iterator-alias=false", restore.Error);
        // Refused before any assignment, so entity 32 still carries only the
        // component the map placed.
        Assert.IsType<StateMachine>(
            Assert.Single(GetComponentListContents(GetEntityListContents(fresh.Entities)[1])));
    }

    // The room the deferral used to refuse and 010f660 loads, and the reason
    // CreateAuthenticatedObject asks a deferred node the structural question
    // instead of skipping it. Both verdicts were measured with this fixture text
    // compiled against both builds; w51 has the table.
    //
    // Every other room in this file runs an iterator that Everest's Flattened holds
    // three times, and three references need three unspent fresh occurrences, which
    // is why 010f660 refuses those rooms whatever their structural path says. This
    // room does not: Monocle.Coroutine stores its constructor argument raw and only
    // Coroutine.Update ever wraps it, so a coroutine that has not updated holds its
    // iterator exactly ONCE and a single occurrence admits it.
    //
    // Three instances of one entity doing
    // `Add(new Coroutine(tween.Wait())); Add(tween);` - the Coroutine ahead of the
    // Tween, so the document reaches the iterator before the owner it captured. Two
    // finished during play and dropped the pair, so the reload supplies two
    // occurrences of Monocle.Tween+<Wait>d__45 at that path that the saved frame does
    // not spend. The third's pair was added during play, so it is a reconstruction
    // whose owner is authentic only as an owned component of the fresh entity - which
    // is neither of the two things IsAuthenticatedCompilerIteratorOwner accepts, so
    // the owner proof answers no and the structural proof is the whole licence.
    [Fact]
    public void ARawCoroutineIteratorLoadsOnItsOwnStructuralEvidence() {
        AkronReconstructionRestore restore = RestoreRawCoroutineIteratorScene(
            siblingsThatFinished: 2,
            out RawCoroutineSceneRoot fresh);

        Assert.True(restore.Success, restore.Error);
        List<Entity> entities = GetEntityListContents(fresh.Entities);
        List<Component> rebuilt = GetComponentListContents(entities[2]);
        Coroutine coroutine = (Coroutine) rebuilt.Single(component => component is Coroutine);
        Tween tween = (Tween) rebuilt.Single(component => component is Tween);
        // One frame, because nothing has updated this coroutine - the shape the whole
        // room turns on. Its owner has to be this entity's own rebuilt Tween rather
        // than a sibling's, or the room would be running one entity's wait on
        // another's timer.
        IEnumerator[] frames = GetRuntimeField<Stack<IEnumerator>>(coroutine, "enumerators").ToArray();
        IEnumerator wait = Assert.Single(frames);
        Assert.Equal("Monocle.Tween+<Wait>d__45", wait.GetType().FullName);
        Assert.Same(tween, GetRuntimeFieldInfo(wait.GetType(), "<>4__this").GetValue(wait));
        Assert.Same(entities[2], GetRuntimeField<Entity>(tween, "<Entity>k__BackingField"));
        // The siblings' pairs finished during play, so the saved frame holds no node
        // for them and emptying those two entities is the correct outcome rather than
        // a dropped object. Their occurrences are what admitted the rebuild above.
        Assert.Empty(GetComponentListContents(entities[0]));
        Assert.Empty(GetComponentListContents(entities[1]));
    }

    // The same room with no sibling to supply the evidence, so the structural leg has
    // nothing to read either and the owner question is the only one left. Recorded
    // because it is what stops the room above being read as "the graph loads every raw
    // coroutine iterator": the two differ only in whether the reload holds an unspent
    // occurrence of that iterator at that path, and both this build and 010f660 answer
    // them differently on exactly that. Only the sentence differs between the builds -
    // 010f660 refuses this one on the structural rule, because it never defers.
    [Fact]
    public void ARawCoroutineIteratorWithNoFreshEvidenceIsStillRefused() {
        AkronReconstructionRestore restore = RestoreRawCoroutineIteratorScene(
            siblingsThatFinished: 0,
            out RawCoroutineSceneRoot fresh);

        Assert.False(restore.Success);
        Assert.Contains(
            "reconstructed compiler iterator owner is not authentic to the fresh room",
            restore.Error);
        Assert.Contains("Monocle.Tween+<Wait>d__45", restore.Error);
        Assert.Empty(GetComponentListContents(Assert.Single(GetEntityListContents(fresh.Entities))));
    }

    private sealed class RawCoroutineSceneRoot {
        public Scene Scene = null!;
        public EntityList Entities = null!;
    }

    private sealed class TweenHolderEntity : Entity {
    }

    private static AkronReconstructionRestore RestoreRawCoroutineIteratorScene(
        int siblingsThatFinished,
        out RawCoroutineSceneRoot fresh
    ) {
        RawCoroutineSceneRoot saved = CreateRawCoroutineIteratorScene(siblingsThatFinished, cleanReload: false);
        RawCoroutineSceneRoot baseline = CreateRawCoroutineIteratorScene(siblingsThatFinished, cleanReload: true);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource, _ => string.Empty);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        fresh = CreateRawCoroutineIteratorScene(siblingsThatFinished, cleanReload: true);
        return graph.Restore(capture.Document, fresh);
    }

    // siblingsThatFinished instances whose Tween ran to completion during play and
    // removed itself along with the Coroutine waiting on it, so the reload rebuilds
    // the pair and the saved frame has none - which is what leaves their fresh
    // occurrences unspent. Then one instance whose pair was added during play, so the
    // reload has none and the saved frame has one.
    //
    // What makes those two populations one room rather than two: which instance holds
    // a pair is per-instance session state, the same mechanism the changed-map rule's
    // comment describes - a mod arms this one and not that one from the flag its
    // entity reads, and the saved frame is later in the session than the reload's
    // starting state.
    private static RawCoroutineSceneRoot CreateRawCoroutineIteratorScene(
        int siblingsThatFinished,
        bool cleanReload
    ) {
        Scene scene = (Scene) RuntimeHelpers.GetUninitializedObject(typeof(Scene));
        EntityList entityList = LinkSceneEntities(scene, CreateDetachedEntityList());
        for (int sibling = 0; sibling < siblingsThatFinished; sibling++) {
            AddTweenHolderEntity(scene, entityList, 31 + sibling, withPair: cleanReload);
        }
        AddTweenHolderEntity(scene, entityList, 31 + siblingsThatFinished, withPair: !cleanReload);
        return new RawCoroutineSceneRoot { Scene = scene, Entities = entityList };
    }

    // The pair is `Add(new Coroutine(tween.Wait())); Add(tween);` with the tween made
    // by Tween.Create(Oneshot, easer: null, duration: 1f, start: true), which is what
    // makes it remove itself and its waiter when it completes. Every field below that
    // a constructor would have set is set, read out of the real Monocle in
    // lib-stripped rather than assumed: Coroutine's `Coroutine(IEnumerator, bool)`
    // does `base(active: true, visible: false)` and leaves RemoveOnComplete true, and
    // Tween's Init plus Start leave Mode, Duration, TimeLeft and Active as below while
    // Tween's own constructor is `base(active: false, visible: false)`.
    //
    // None of those is read by anything that decides a verdict - the graph's
    // authenticators read types, document paths, occurrence counts and reference
    // ownership, and Active, Finished, RemoveOnComplete, Mode, Duration and TimeLeft
    // travel as ordinary scalars. They are set because the room is only evidence
    // about the game if its objects are ones the game could have made: an inactive
    // Persist tween of zero duration could never have run to completion and removed
    // itself, which is the history the siblings here are supposed to have.
    private static void AddTweenHolderEntity(
        Scene scene,
        EntityList entityList,
        int sourceId,
        bool withPair
    ) {
        TweenHolderEntity owner = CreateUninitializedEntity<TweenHolderEntity>();
        ComponentList components = CreateDetachedComponentList(owner);
        SetRuntimeField(owner, "Active", true);
        SetRuntimeField(owner, "Visible", true);
        SetRuntimeField(owner, "Collidable", true);
        SetRuntimeField(owner, "<Scene>k__BackingField", scene);
        SetRuntimeField(owner, "<SourceId>k__BackingField", CreateEntityId("a00", sourceId));

        List<Component> ordered = new List<Component>();
        if (withPair) {
            Tween tween = (Tween) RuntimeHelpers.GetUninitializedObject(typeof(Tween));
            SetRuntimeField(tween, "<Entity>k__BackingField", owner);
            SetRuntimeField(tween, "Active", true);
            SetRuntimeField(tween, "<Mode>k__BackingField", Tween.TweenMode.Oneshot);
            SetRuntimeField(tween, "<Duration>k__BackingField", 1f);
            SetRuntimeField(tween, "<TimeLeft>k__BackingField", 1f);
            Coroutine coroutine = (Coroutine) RuntimeHelpers.GetUninitializedObject(typeof(Coroutine));
            // Unlike a StateMachine's own coroutine this one was added with Add, so
            // the game does give it an Entity.
            SetRuntimeField(coroutine, "<Entity>k__BackingField", owner);
            SetRuntimeField(coroutine, "Active", true);
            SetRuntimeField(coroutine, "RemoveOnComplete", true);
            Stack<IEnumerator> frames = new Stack<IEnumerator>();
            // What Coroutine(IEnumerator, bool) does, and all it does: push the
            // argument raw. Only Coroutine.Update ever wraps a frame in Everest's
            // Flattened, so a pair installed after its own entity updated this frame -
            // which is what another entity's update adding it does, and what the
            // fresh room's single initialization update leaves - still holds the
            // iterator raw, and holds it exactly once. That is the whole of what this
            // room is about; an active coroutine that HAS updated holds it three
            // times and every other room in this file is that shape.
            frames.Push(CreateCompilerIterator(tween, "<Wait>d__45"));
            SetRuntimeField(coroutine, "enumerators", frames);
            ordered.Add(coroutine);
            ordered.Add(tween);
        }
        SetRuntimeField(components, "components", ordered);
        SetRuntimeField(components, "current", new HashSet<Component>(ordered));
        AddDetachedEntity(entityList, owner);
    }

    private static AkronReconstructionRestore RestoreRuntimeAddedSpriteRoutineScene(
        bool cleanReloadCarriesTheSprite,
        bool spriteFirst
    ) {
        return RestoreRuntimeAddedSpriteRoutineScene(
            cleanReloadCarriesTheSprite,
            spriteFirst,
            out _);
    }

    // The saved room always carries the sprite, because that is the frame the
    // player set the slot on. cleanReloadCarriesTheSprite is what separates a
    // sprite the map places from one mod code added after the level loaded, and
    // it applies to the baseline and to the fresh room together - both are clean
    // reloads of the same room.
    private static AkronReconstructionRestore RestoreRuntimeAddedSpriteRoutineScene(
        bool cleanReloadCarriesTheSprite,
        bool spriteFirst,
        out SpriteRoutineSceneRoot fresh
    ) {
        SpriteRoutineSceneRoot saved = CreateSpriteRoutineScene(true, spriteFirst, typeof(Sprite));
        SpriteRoutineSceneRoot baseline = CreateSpriteRoutineScene(
            false,
            spriteFirst,
            typeof(Sprite),
            includeSprite: cleanReloadCarriesTheSprite);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource, _ => string.Empty);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        fresh = CreateSpriteRoutineScene(
            false,
            spriteFirst,
            typeof(Sprite),
            includeSprite: cleanReloadCarriesTheSprite);
        return graph.Restore(capture.Document, fresh);
    }

    private static AkronReconstructionRestore RestoreSpriteRoutineSceneWithExtraCoroutine(
        ExtraCoroutine extra
    ) {
        SpriteRoutineSceneRoot saved = CreateSpriteRoutineScene(true, true, typeof(PlayerSprite), extra);
        SpriteRoutineSceneRoot baseline = CreateSpriteRoutineScene(false, true, typeof(PlayerSprite), extra);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource, _ => string.Empty);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        SpriteRoutineSceneRoot fresh = CreateSpriteRoutineScene(false, true, typeof(PlayerSprite), extra);
        return graph.Restore(capture.Document, fresh);
    }

    private enum ExtraCoroutine {
        None,
        Empty,
        HoldingTheRunningIterator
    }

    private static void RestoreSpriteRoutineScene(bool spriteFirst, Type spriteType) {
        SpriteRoutineSceneRoot saved = CreateSpriteRoutineScene(true, spriteFirst, spriteType);
        SpriteRoutineSceneRoot baseline = CreateSpriteRoutineScene(false, spriteFirst, spriteType);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource, _ => string.Empty);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        SpriteRoutineSceneRoot fresh = CreateSpriteRoutineScene(false, spriteFirst, spriteType);

        Entity freshEntity = Assert.Single(GetEntityListContents(fresh.Entities));
        List<Component> freshComponents = GetComponentListContents(freshEntity);
        Sprite freshSprite = (Sprite) freshComponents.Single(c => c is Sprite);
        Assert.Equal(spriteType, freshSprite.GetType());
        StateMachine freshMachine = (StateMachine) freshComponents.Single(c => c is StateMachine);
        Coroutine freshCoroutine = GetRuntimeField<Coroutine>(freshMachine, "currentCoroutine");

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Same(freshEntity, Assert.Single(GetEntityListContents(fresh.Entities)));
        Assert.Same(freshSprite, GetComponentListContents(freshEntity).Single(c => c is Sprite));
        Assert.Same(freshCoroutine, GetRuntimeField<Coroutine>(freshMachine, "currentCoroutine"));

        // Stack<T> enumerates top first, so [0] is the frame running the sprite
        // routine and [1] is the state routine that yielded it.
        IEnumerator[] frames = GetRuntimeField<Stack<IEnumerator>>(freshCoroutine, "enumerators").ToArray();
        Assert.Equal(2, frames.Length);
        IEnumerator routine = GetRuntimeField<Stack<IEnumerator>>(frames[1], "enums").Single();
        object current = GetRuntimeFieldInfo(routine.GetType(), "<>2__current").GetValue(routine)!;
        Assert.Equal("Monocle.Sprite+<PlayUtil>d__40", current.GetType().FullName);
        Assert.Same(freshSprite, GetRuntimeFieldInfo(current.GetType(), "<>4__this").GetValue(current));

        // The live room holds that one iterator in three places: the state
        // routine's own <>2__current, the Flattened that produced it, and the
        // Flattened the Coroutine then pushed to run it. Restoring them as
        // separate objects would leave the coroutine advancing an iterator the
        // routine cannot see, so all three have to come back as one object -
        // being accepted is not enough.
        Assert.Same(current, GetRuntimeFieldInfo(frames[1].GetType(), "current").GetValue(frames[1]));
        Assert.Same(current, GetRuntimeField<Stack<IEnumerator>>(frames[0], "enums").Single());
    }

    private sealed class SpriteRoutineSceneRoot {
        public Scene Scene = null!;
        public EntityList Entities = null!;
    }

    // How many Coroutine.Update calls a room has behind it, and it is not a detail.
    // One update leaves the top stack frame the raw iterator the routine yielded;
    // the second wraps that frame in Everest's Flattened, and every update after
    // that leaves the shape alone. Measured against the real Coroutine and
    // Flattened, not read off their source.
    //
    // A freshly loaded room gets one Level.Update before its baseline is captured
    // (AkronModule.LevelOnUpdate holds every later update until the capture runs) and
    // one from AkronModule.RunFreshRoomInitializationUpdate when a load rebuilds a
    // room. One Level.Update is not the same as one update of every coroutine -
    // Celeste skips ordinary entities while paused, frozen or transitioning, and
    // neither call site clears those - so one is the ordinary unpaused case and zero
    // is reachable. A saved frame can be at any count including zero and one, because
    // the hotkey capture runs before the frame's own Level.Update.
    //
    // These two are the counts the sibling rooms below use: the reload at the
    // ordinary one, and a saved frame whose animation has been running long enough to
    // have settled.
    private const int FreshRoomCoroutineUpdates = 1;
    private const int SteadyCoroutineUpdates = 2;

    private sealed class SpriteStateMachineEntity : Entity {
        public Sprite Animation = null!;

        public IEnumerator StateRoutine() {
            yield return InvokeSpritePlayUtil(Animation);
        }
    }

    private static IEnumerator InvokeSpritePlayUtil(Sprite sprite) {
        return CreateCompilerIterator(sprite, "<PlayUtil>d__40");
    }

    private static SpriteRoutineSceneRoot CreateSpriteRoutineScene(
        bool includeIterator,
        bool spriteFirst,
        Type spriteType,
        ExtraCoroutine extra = ExtraCoroutine.None,
        bool includeSprite = true
    ) {
        Scene scene = (Scene) RuntimeHelpers.GetUninitializedObject(typeof(Scene));
        EntityList entityList = LinkSceneEntities(scene, CreateDetachedEntityList());
        AddSpriteRoutineEntity(
            scene,
            entityList,
            sourceId: 31,
            includeIterator,
            spriteFirst,
            spriteType,
            extra,
            includeSprite);
        return new SpriteRoutineSceneRoot { Scene = scene, Entities = entityList };
    }

    private static void AddSpriteRoutineEntity(
        Scene scene,
        EntityList entityList,
        int sourceId,
        bool includeIterator,
        bool spriteFirst,
        Type spriteType,
        ExtraCoroutine extra = ExtraCoroutine.None,
        bool includeSprite = true,
        bool spriteIsAnimating = true,
        int coroutineUpdates = SteadyCoroutineUpdates
    ) {
        SpriteStateMachineEntity owner = CreateUninitializedEntity<SpriteStateMachineEntity>();
        ComponentList components = CreateDetachedComponentList(owner);
        SetRuntimeField(owner, "<Scene>k__BackingField", scene);
        SetRuntimeField(owner, "<SourceId>k__BackingField", CreateEntityId("a00", sourceId));

        // includeSprite: false is the room whose sprite mod code added after the
        // level loaded, so a clean reload of that room does not carry it. The
        // entity still does, and the state machine still does, because both come
        // from the map.
        Sprite sprite = null!;
        if (includeSprite) {
            sprite = (Sprite) RuntimeHelpers.GetUninitializedObject(spriteType);
            SetRuntimeField(sprite, "<Entity>k__BackingField", owner);
            // Sprite.PlayUtil is `while (Animating) yield return null`, so the
            // routine only stays mid-flight while the sprite reports it is playing.
            // spriteIsAnimating: false is a frame after that animation ended, once
            // PlayUtil has returned and the state routine has run off its end and
            // left the coroutine empty. That takes more than one update to unwind,
            // which is why this only ever pairs with includeIterator: false rather
            // than naming a particular frame.
            SetRuntimeField(sprite, "<Animating>k__BackingField", spriteIsAnimating);
            owner.Animation = sprite;
        }

        StateMachine machine = (StateMachine) RuntimeHelpers.GetUninitializedObject(typeof(StateMachine));
        SetRuntimeField(machine, "<Entity>k__BackingField", owner);
        SetRuntimeField(machine, "Active", true);

        // StateMachine's constructor does `currentCoroutine = new Coroutine()`
        // and never adds it to the entity's ComponentList, so the live
        // Coroutine's Entity stays null. Setting it here would hand the graph
        // an ownership link the game does not provide.
        Coroutine coroutine = (Coroutine) RuntimeHelpers.GetUninitializedObject(typeof(Coroutine));
        Stack<IEnumerator> iterators = new Stack<IEnumerator>();
        SetRuntimeField(coroutine, "enumerators", iterators);
        SetRuntimeField(machine, "currentCoroutine", coroutine);
        // An empty stack only ever gets there through Coroutine.Cancel or by
        // draining, and both set Finished. A state machine whose state has no
        // coroutine is cancelled by StateMachine, so this is the state every empty
        // coroutine in these rooms is really in.
        SetRuntimeField(coroutine, "<Finished>k__BackingField", !includeIterator);
        if (includeIterator) {
            // CI's Celeste assembly has no executable Coroutine or Sprite method
            // bodies. Build the exact stack those updates leave behind: the state
            // routine's Flattened frame holds the yielded PlayUtil iterator, and a
            // second update wraps that iterator in another Flattened frame.
            IEnumerator playIterator = InvokeSpritePlayUtil(sprite);
            IEnumerator stateRoutine = owner.StateRoutine();
            SetRuntimeField(stateRoutine, "<>1__state", 1);
            SetRuntimeField(stateRoutine, "<>2__current", playIterator);
            IEnumerator stateFrame = CreateFlattened(stateRoutine, playIterator);
            iterators.Push(stateFrame);
            if (coroutineUpdates == FreshRoomCoroutineUpdates) {
                iterators.Push(playIterator);
            } else {
                iterators.Push(CreateFlattened(playIterator, null));
            }
            SetRuntimeField(coroutine, "Active", true);
        }

        List<Component> ordered = !includeSprite
            ? new List<Component> { machine }
            : spriteFirst
                ? new List<Component> { sprite, machine }
                : new List<Component> { machine, sprite };
        if (extra != ExtraCoroutine.None) {
            // A plain Coroutine component, the way an entity adds one with
            // Add(new Coroutine(...)), so this one really does have an Entity.
            Coroutine second = (Coroutine) RuntimeHelpers.GetUninitializedObject(typeof(Coroutine));
            SetRuntimeField(second, "<Entity>k__BackingField", owner);
            Stack<IEnumerator> secondFrames = new Stack<IEnumerator>();
            if (extra == ExtraCoroutine.HoldingTheRunningIterator && includeIterator) {
                IEnumerator running = GetRuntimeField<Stack<IEnumerator>>(coroutine, "enumerators").ToArray()[0];
                IEnumerator aliased = GetRuntimeField<Stack<IEnumerator>>(running, "enums").Single();
                secondFrames.Push(CreateFlattened(aliased, null));
            }
            SetRuntimeField(second, "enumerators", secondFrames);
            ordered.Add(second);
        }
        SetRuntimeField(components, "components", ordered);
        SetRuntimeField(components, "current", new HashSet<Component>(ordered));
        AddDetachedEntity(entityList, owner);
    }

    private static IEnumerator CreateCompilerIterator(object owner, string nestedTypeName) {
        Type iteratorType = owner.GetType().GetNestedType(nestedTypeName, BindingFlags.NonPublic)
            ?? owner.GetType().BaseType?.GetNestedType(nestedTypeName, BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(owner.GetType().FullName + "+" + nestedTypeName + " is unavailable.");
        IEnumerator iterator = (IEnumerator) RuntimeHelpers.GetUninitializedObject(iteratorType);
        SetRuntimeField(iterator, "<>1__state", 1);
        SetRuntimeField(iterator, "<>4__this", owner);
        return iterator;
    }

    private static IEnumerator CreateFlattened(IEnumerator iterator, object? current) {
        Type flattenedType = typeof(Sprite).Assembly.GetType(
            "Celeste.Mod.SwapImmediatelyExtension+Flattened",
            throwOnError: true)!;
        IEnumerator flattened = (IEnumerator) RuntimeHelpers.GetUninitializedObject(flattenedType);
        SetRuntimeField(flattened, "enums", new Stack<IEnumerator>(new[] { iterator }));
        SetRuntimeField(flattened, "current", current);
        return flattened;
    }

    // Two instances of the same custom entity in one room, which is what makes the
    // structural evidence below possible at all: the fresh occurrence index keys on
    // the iterator's type and its path shape with list indices wildcarded, so one
    // sibling running the routine is a record of that routine's iterator living at
    // that path, whichever entity and component slot it sits in.
    //
    // Entity 31 is the ordinary one: the map places its sprite and its animation
    // runs on room entry, so a clean reload always catches it mid-routine.
    // firstStillRunning says whether it was still running when the player set the
    // slot, which is the whole of what decides whether the fresh occurrence at that
    // path is already spoken for by the saved frame.
    //
    // Entity 32 is the one being rebuilt. Its animation is player-triggered, so it
    // runs in the saved frame and never in a clean reload, and its machine sits
    // ahead of it in the component list so the document reaches the iterator before
    // its captured owner. secondSpriteIsMapPlaced decides whether the reload still
    // supplies that owner.
    private static SpriteRoutineSceneRoot CreateSiblingSpriteRoutineScene(
        bool cleanReload,
        bool firstStillRunning,
        bool secondSpriteIsMapPlaced
    ) {
        Scene scene = (Scene) RuntimeHelpers.GetUninitializedObject(typeof(Scene));
        EntityList entityList = LinkSceneEntities(scene, CreateDetachedEntityList());
        // A clean reload restarts entity 31's animation whatever the saved frame
        // caught it doing, because LoadLevel plus the one initialization update is
        // the whole of its history there. Both stacks are two frames deep, but the
        // reload's top frame is the raw iterator the routine yielded while the
        // saved one has it wrapped in Flattened. See FreshRoomCoroutineUpdates.
        int firstUpdates = cleanReload
            ? FreshRoomCoroutineUpdates
            : firstStillRunning ? SteadyCoroutineUpdates : 0;
        AddSpriteRoutineEntity(
            scene,
            entityList,
            sourceId: 31,
            includeIterator: firstUpdates > 0,
            spriteFirst: true,
            spriteType: typeof(Sprite),
            spriteIsAnimating: firstUpdates > 0,
            coroutineUpdates: firstUpdates);
        AddSpriteRoutineEntity(
            scene,
            entityList,
            sourceId: 32,
            includeIterator: !cleanReload,
            spriteFirst: false,
            spriteType: typeof(Sprite),
            includeSprite: !cleanReload || secondSpriteIsMapPlaced,
            spriteIsAnimating: !cleanReload,
            coroutineUpdates: cleanReload ? 0 : SteadyCoroutineUpdates);
        return new SpriteRoutineSceneRoot { Scene = scene, Entities = entityList };
    }

    // The fresh room is built by the caller so a test can record what the reload
    // produced before the restore writes into it.
    private static AkronReconstructionRestore RestoreSiblingSpriteRoutineScene(
        bool firstStillRunning,
        bool secondSpriteIsMapPlaced,
        SpriteRoutineSceneRoot fresh
    ) {
        SpriteRoutineSceneRoot saved = CreateSiblingSpriteRoutineScene(
            cleanReload: false,
            firstStillRunning,
            secondSpriteIsMapPlaced);
        SpriteRoutineSceneRoot baseline = CreateSiblingSpriteRoutineScene(
            cleanReload: true,
            firstStillRunning,
            secondSpriteIsMapPlaced);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource, _ => string.Empty);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        return graph.Restore(capture.Document, fresh);
    }

    // One callback object standing in two slots of the freshly loaded room.
    //
    // Monocle.Alarm.Set is how the game arms a one-shot timer: the component
    // sits on its entity while it counts down and calls RemoveSelf the moment
    // it fires, so a room a player has been standing in for a few seconds no
    // longer carries it while a clean reload of that same room still does. The
    // mod here hands one static Action to that timer and to a state machine's
    // end callback. A non-capturing lambda is cached once, so this is a single
    // delegate object whose target is the cached-closure singleton Roslyn emits
    // per enclosing type - the shape ExtendedVariantMode's ZoomLevel callback
    // has in a real room.
    //
    // So the fresh room reaches that one object through Alarm.OnComplete first
    // and through StateMachine.ends second, while the saved frame reaches it
    // only through StateMachine.ends. That is the path the document records for
    // it, and it is the path the fresh index has to have a record of.
    //
    // Monocle.StateMachine.AddState resizes all five callback arrays, so a mod
    // state present in one session and not the other leaves the saved ends
    // array a different length from the fresh one. The graph then rebuilds that
    // array from the document instead of keeping the fresh one, which is what
    // takes the delegate's direct route to its fresh counterpart away and
    // leaves only the structural call key.
    [Fact]
    public void RestoreAcceptsACallbackTheFreshRoomAlsoHoldsInAnEarlierSlot() {
        // Controls. Dropping either ingredient restores on its own, so the
        // refusal this test exists for is attributable to the pair and not to
        // the vanished alarm or the resized array by itself.
        Assert.True(RestoreSharedCallbackScene(freshAlarm: false, freshExtraState: true).Success);
        Assert.True(RestoreSharedCallbackScene(freshAlarm: true, freshExtraState: false).Success);

        AkronReconstructionRestore restore = RestoreSharedCallbackScene(
            freshAlarm: true,
            freshExtraState: true,
            freshEndCallback: true,
            out SharedCallbackSceneRoot fresh,
            out StateMachine machineBeforeRestore);

        Assert.True(restore.Success, restore.Error);

        // Being accepted is not enough. The fresh state machine has to keep its
        // identity, the rebuilt array has to be the saved room's shape, and the
        // slot has to come back holding the callback the saved room ran.
        StateMachine machine = (StateMachine) GetComponentListContents(
            GetEntityListContents(fresh.Entities)[1]).Single(component => component is StateMachine);
        Assert.Same(machineBeforeRestore, machine);
        Action[] ends = GetRuntimeField<Action[]>(machine, "ends");
        Assert.Equal(4, ends.Length);
        Action restored = Assert.IsType<Action>(ends[1]);
        Assert.Equal(SampleTimerMod.Callback.Method, restored.Method);
        Assert.Equal(SampleTimerMod.Callback.Target!.GetType(), restored.Target!.GetType());

        // The timer really is gone from the restored room: the saved frame had
        // no alarm, so the entity that carried one in the fresh room comes back
        // with an empty component list.
        Assert.Empty(GetComponentListContents(GetEntityListContents(fresh.Entities)[0]));
    }

    // The boundary. Same room, same two ingredients, except the fresh state
    // machine never carries the callback at all - only the alarm does. Nothing
    // in a clean reload runs that callback from that slot, so the document is
    // asking for one the fresh room does not have there and the restore has to
    // keep refusing. This is the ExtendedVariants shape W6 proved is correct
    // behaviour, reduced to one slot.
    //
    // The control half is what makes the refusal attributable: the identical
    // room whose fresh state machine does carry the callback restores, so what
    // the graph objects to is the missing callback and not the vanished alarm
    // or the resized array.
    [Fact]
    public void RestoreStillRefusesACallbackTheFreshRoomDoesNotHoldInThatSlot() {
        Assert.True(RestoreSharedCallbackScene(
            freshAlarm: true,
            freshExtraState: true,
            freshEndCallback: true,
            out _).Success);

        AkronReconstructionRestore restore = RestoreSharedCallbackScene(
            freshAlarm: true,
            freshExtraState: true,
            freshEndCallback: false,
            out _);

        Assert.False(restore.Success);
        Assert.Contains("is not authentic to the fresh room", restore.Error);
        Assert.Contains("SampleTimerMod", restore.Error);
    }

    private static AkronReconstructionRestore RestoreSharedCallbackScene(
        bool freshAlarm,
        bool freshExtraState
    ) {
        return RestoreSharedCallbackScene(freshAlarm, freshExtraState, true, out _, out _);
    }

    private static AkronReconstructionRestore RestoreSharedCallbackScene(
        bool freshAlarm,
        bool freshExtraState,
        bool freshEndCallback,
        out SharedCallbackSceneRoot fresh
    ) {
        return RestoreSharedCallbackScene(freshAlarm, freshExtraState, freshEndCallback, out fresh, out _);
    }

    private static AkronReconstructionRestore RestoreSharedCallbackScene(
        bool freshAlarm,
        bool freshExtraState,
        bool freshEndCallback,
        out SharedCallbackSceneRoot fresh,
        out StateMachine freshMachine
    ) {
        // Chronology matters here and it is easy to get wrong. The baseline is
        // a clone of the room as it loaded in the session that SET the slot, so
        // it has to agree with the saved frame about everything that session
        // could not have changed: AddState only grows those arrays, so a
        // baseline with more states than the saved frame taken later in the
        // same session is a shape no session can produce. What the baseline may
        // differ in is what the room itself did while it was played - the timer
        // fired and took its component away. The extra mod state and the
        // missing callback belong to the OTHER session, the one that loads the
        // slot back, so they appear only in the fresh room.
        SharedCallbackSceneRoot saved = CreateSharedCallbackScene(
            includeAlarm: false,
            includeExtraState: false,
            includeEndCallback: true);
        SharedCallbackSceneRoot baseline = CreateSharedCallbackScene(
            freshAlarm,
            includeExtraState: false,
            includeEndCallback: true);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource, _ => string.Empty);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        fresh = CreateSharedCallbackScene(freshAlarm, freshExtraState, freshEndCallback);
        freshMachine = (StateMachine) GetComponentListContents(
            GetEntityListContents(fresh.Entities)[1]).Single(component => component is StateMachine);
        return graph.Restore(capture.Document, fresh);
    }

    private sealed class SharedCallbackSceneRoot {
        public Scene Scene = null!;
        public EntityList Entities = null!;
    }

    private sealed class SharedCallbackEntity : Entity {
        public int RunState() {
            return 1;
        }

        public int RunModState() {
            return 2;
        }
    }

    private static SharedCallbackSceneRoot CreateSharedCallbackScene(
        bool includeAlarm,
        bool includeExtraState,
        bool includeEndCallback
    ) {
        Scene scene = (Scene) RuntimeHelpers.GetUninitializedObject(typeof(Scene));
        EntityList entityList = LinkSceneEntities(scene, CreateDetachedEntityList());

        // The timer entity is first in the room, so a walk of the fresh room
        // reaches the shared callback through the alarm before it reaches the
        // state machine.
        SharedCallbackEntity timerOwner = CreateUninitializedEntity<SharedCallbackEntity>();
        ComponentList timerComponents = CreateDetachedComponentList(timerOwner);
        SetRuntimeField(timerOwner, "<Scene>k__BackingField", scene);
        SetRuntimeField(timerOwner, "<SourceId>k__BackingField", CreateEntityId("a00", 11));
        List<Component> timerOrdered = new List<Component>();
        if (includeAlarm) {
            // The private fields are the state Alarm.Create leaves behind. CI's
            // Celeste assembly is reference-only, so the fixture writes that state
            // directly instead of invoking a method body the assembly omits.
            Alarm alarm = (Alarm) RuntimeHelpers.GetUninitializedObject(typeof(Alarm));
            SetRuntimeField(alarm, "OnComplete", SampleTimerMod.Callback);
            SetRuntimeField(alarm, "<Mode>k__BackingField", Alarm.AlarmMode.Oneshot);
            SetRuntimeField(alarm, "<Duration>k__BackingField", 0.25f);
            SetRuntimeField(alarm, "<TimeLeft>k__BackingField", 0.25f);
            SetRuntimeField(alarm, "Active", true);
            SetRuntimeField(alarm, "<Entity>k__BackingField", timerOwner);
            timerOrdered.Add(alarm);
        }
        SetRuntimeField(timerComponents, "components", timerOrdered);
        SetRuntimeField(timerComponents, "current", new HashSet<Component>(timerOrdered));
        AddDetachedEntity(entityList, timerOwner);

        SharedCallbackEntity stateOwner = CreateUninitializedEntity<SharedCallbackEntity>();
        ComponentList stateComponents = CreateDetachedComponentList(stateOwner);
        SetRuntimeField(stateOwner, "<Scene>k__BackingField", scene);
        SetRuntimeField(stateOwner, "<SourceId>k__BackingField", CreateEntityId("a00", 12));

        // The update callback is a method group on the entity, the way Celeste's
        // Player wires its own states, so its target is the entity rather than a
        // closure. The fixture arrays are the state a four-slot constructor and
        // SetCallbacks produce.
        StateMachine machine = CreateDetachedStateMachine(stateOwner, 4);
        SetStateCallbacks(
            machine,
            1,
            stateOwner.RunState,
            includeEndCallback ? SampleTimerMod.Callback : null);
        if (includeExtraState) {
            AddNamedState(machine, "mod-state", stateOwner.RunModState, null);
        }
        SetRuntimeField(machine, "state", 1);
        List<Component> stateOrdered = new List<Component> { machine };
        SetRuntimeField(stateComponents, "components", stateOrdered);
        SetRuntimeField(stateComponents, "current", new HashSet<Component>(stateOrdered));
        AddDetachedEntity(entityList, stateOwner);

        return new SharedCallbackSceneRoot { Scene = scene, Entities = entityList };
    }
    // W16 proved a saved document that asks for a callback at
    // StateMachine.ends[2] while the fresh room runs it at ends[1] is accepted,
    // and that the restore then writes it into slot 2 of the fresh machine's
    // own array and leaves slot 1 empty. W21 proved an exact index cannot be
    // the fix, because a mod calling the public SetCallbacks during play to
    // move that same callback from one state to another produces a document
    // byte for byte identical to the one that has to be refused.
    //
    // The first two tests are that pair. They restore the same saved room
    // against two fresh rooms that differ in exactly one array - names - and in
    // nothing else. The callback sits at ends[1] in both fresh rooms and at
    // ends[2] in both documents, so whatever separates them is the names array.
    // That is the point: a state's identity is its name and not its id, because
    // Monocle's AddState hands ids out in whatever order the installed mods add
    // states.
    //
    // Both mod states share one update method on purpose. That leaves the end
    // callback's slot, and what names says that slot is, as the only thing that
    // differs between the rooms.

    // The wrong restore. Two mods each add a state; in the loading session they
    // ran in the other order, so "second-mod-state" came back as id 1 instead
    // of id 2. The document's ends[2] is that state in the saved room and
    // "first-mod-state" in the fresh one, so the callback would land on a state
    // this room never runs it for.
    //
    // Swapping two states makes BOTH their slots disagree, and there is no
    // carve-out for a write that happens to change nothing, so the refusal
    // lands on the first slot the document writes into rather than on the end
    // callback: the shared update at slot 1. That is the whole machine being
    // refused as misaligned, which is what it is. The end callback W16 measured
    // being misplaced is covered below by the fresh room's own ends array
    // coming through untouched.
    [Fact]
    public void RestoreRefusesACallbackWhoseStateSlotTheFreshRoomReadsAsAnotherState() {
        StateSlotSceneRoot fresh = CreateStateSlotScene(
            true, ("second-mod-state", true), ("first-mod-state", false));
        StateMachine machineBeforeRestore = GetStateSlotMachine(fresh);
        Action[] endsBeforeRestore = GetRuntimeField<Action[]>(machineBeforeRestore, "ends");

        AkronReconstructionRestore restore = RestoreStateSlotScene(fresh, true, movedDuringPlay: false);

        Assert.False(restore.Success);
        Assert.Contains("saved state slot is a different state in the fresh room", restore.Error);
        Assert.Contains("state=first-mod-state", restore.Error);
        Assert.Contains("slot=1", restore.Error);
        Assert.Contains("updates[1]", restore.Error);

        // The refused restore leaves the fresh room's own slot alone, which is
        // the half W16 measured going wrong: the callback stays where a clean
        // load of this room puts it instead of moving to the saved id.
        Assert.Same(machineBeforeRestore, GetStateSlotMachine(fresh));
        Assert.Same(endsBeforeRestore, GetRuntimeField<Action[]>(machineBeforeRestore, "ends"));
        Assert.Equal(SampleTimerMod.Callback.Method, endsBeforeRestore[1]!.Method);
        Assert.Null(endsBeforeRestore[2]);
    }

    // The valid frame, and the one an exact index got wrong. Same two mods in
    // the same order, so both rooms agree that slot 2 is "second-mod-state". A
    // clean load wires the callback to "first-mod-state" and during play the mod
    // called the public SetCallbacks to move it to "second-mod-state". The
    // document is the one above and only the fresh room's names differ.
    // Restoring it reproduces the frame the player saved, so it has to be
    // accepted.
    [Fact]
    public void RestoreAcceptsACallbackAModMovedToAnotherStateSlotDuringPlay() {
        StateSlotSceneRoot fresh = CreateStateSlotScene(
            true, ("first-mod-state", true), ("second-mod-state", false));
        StateMachine machineBeforeRestore = GetStateSlotMachine(fresh);

        AkronReconstructionRestore restore = RestoreStateSlotScene(fresh, true, movedDuringPlay: true);

        Assert.True(restore.Success, restore.Error);

        // Accepted is not enough: the fresh machine has to keep its identity and
        // the callback has to come back on the state the player left it on.
        StateMachine machine = GetStateSlotMachine(fresh);
        Assert.Same(machineBeforeRestore, machine);
        Action[] ends = GetRuntimeField<Action[]>(machine, "ends");
        Assert.Equal(3, ends.Length);
        Assert.Null(ends[1]);
        Action restored = Assert.IsType<Action>(ends[2]);
        Assert.Equal(SampleTimerMod.Callback.Method, restored.Method);
        Assert.Equal(SampleTimerMod.Callback.Target!.GetType(), restored.Target!.GetType());
    }

    // The same pair again over the callback shape Celeste's own states use: a
    // method group on the entity rather than a mod's cached lambda. It takes a
    // different route through the gate - the target is an object the fresh room
    // already has, so it authenticates against that object's method set, which
    // carries no path at all - and the first version of this fix left that route
    // open. Both halves are here because closing a route is only right if the
    // valid frame still goes through it - the first half of this test restores
    // the in-play move over the same callback shape and asserts it lands.
    [Fact]
    public void RestoreRefusesAnEntityMethodCallbackWhoseStateSlotTheFreshRoomReadsAsAnotherState() {
        StateSlotSceneRoot moved = CreateStateSlotScene(
            false, ("first-mod-state", true), ("second-mod-state", false));
        AkronReconstructionRestore inPlay = RestoreStateSlotScene(moved, false, movedDuringPlay: true);
        Assert.True(inPlay.Success, inPlay.Error);
        Assert.Equal(
            nameof(StateSlotEntity.EndState),
            GetRuntimeField<Action[]>(GetStateSlotMachine(moved), "ends")[2]!.Method.Name);

        StateSlotSceneRoot fresh = CreateStateSlotScene(
            false, ("second-mod-state", true), ("first-mod-state", false));
        Action[] endsBeforeRestore = GetRuntimeField<Action[]>(GetStateSlotMachine(fresh), "ends");

        AkronReconstructionRestore restore = RestoreStateSlotScene(fresh, false, movedDuringPlay: false);

        Assert.False(restore.Success);
        Assert.Contains("saved state slot is a different state in the fresh room", restore.Error);
        Assert.Contains("state=first-mod-state", restore.Error);
        Assert.Equal(nameof(StateSlotEntity.EndState), endsBeforeRestore[1]!.Method.Name);
        Assert.Null(endsBeforeRestore[2]);
    }

    // The other valid frame, and the one the first version of this fix got
    // wrong. AddState is callable during play, so a mod that adds a state only
    // once the player has something leaves the saved frame holding a state a
    // clean load of the same room has not added yet. The freshly loaded room
    // has no slot 2 at all, so there is nothing there for the document to
    // disagree with: it is adding a state rather than renaming one, and the
    // restore has to grow the machine and put the callback on it.
    [Fact]
    public void RestoreAcceptsACallbackOnAStateAModAddedWhileTheRoomWasPlayed() {
        // The baseline is the room as it loaded in the session that set the
        // slot, before the mod added its second state. AddState only ever grows
        // these arrays, so a baseline shorter than the saved frame is exactly
        // what one session playing forward produces.
        StateSlotSceneRoot saved = CreateStateSlotScene(
            true, ("first-mod-state", true), ("added-while-playing", true));
        StateSlotSceneRoot baseline = CreateStateSlotScene(true, ("first-mod-state", true));
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource, _ => string.Empty);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);

        StateSlotSceneRoot fresh = CreateStateSlotScene(true, ("first-mod-state", true));
        Assert.Equal(2, GetRuntimeField<Action[]>(GetStateSlotMachine(fresh), "ends").Length);

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Action[] ends = GetRuntimeField<Action[]>(GetStateSlotMachine(fresh), "ends");
        Assert.Equal(3, ends.Length);
        Assert.Equal(SampleTimerMod.Callback.Method, ends[1]!.Method);
        Assert.Equal(SampleTimerMod.Callback.Method, ends[2]!.Method);
    }

    // The same wrong restore reached through the shape that made the first
    // version of this fix miss it. Celeste and its mods routinely hold one
    // callback object in more than one place, and a document records only the
    // first place its capture walked to a node from. Here that first place is a
    // plain field, so the delegate's own owner edge says "field" and says
    // nothing about the state slot the array also puts it in. The check has to
    // sit on the array element for this to be refused, and this test is the
    // reason it does.
    [Fact]
    public void RestoreRefusesAWrongStateSlotForACallbackAFieldReachedFirst() {
        CachedCallbackSceneRoot saved = CreateCachedCallbackScene(
            ("first-mod-state", false), ("second-mod-state", true));
        CachedCallbackSceneRoot baseline = CreateCachedCallbackScene(
            ("first-mod-state", false), ("second-mod-state", true));
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource, _ => string.Empty);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);

        // The premise: the shared callback really is recorded under the field,
        // so nothing about the delegate node names the state slot.
        AkronReconstructionNode delegateNode = capture.Document.Nodes.Single(candidate =>
            candidate.DelegateCalls.Count == 1 &&
            candidate.DelegateCalls[0].MethodName.Contains("b__", StringComparison.Ordinal));
        Assert.Equal("field", delegateNode.ParentKind);
        Assert.Equal(nameof(CachedCallbackSceneRoot.CachedEnd), delegateNode.ParentFieldName);

        CachedCallbackSceneRoot fresh = CreateCachedCallbackScene(
            ("second-mod-state", true), ("first-mod-state", false));
        StateMachine freshMachine = (StateMachine) GetComponentListContents(
            GetEntityListContents(fresh.Entities)[0]).Single(component => component is StateMachine);
        Action[] endsBeforeRestore = GetRuntimeField<Action[]>(freshMachine, "ends");

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.False(restore.Success);
        Assert.Contains("saved state slot is a different state in the fresh room", restore.Error);
        Assert.Contains("state=first-mod-state", restore.Error);
        Assert.Equal(SampleTimerMod.Callback.Method, endsBeforeRestore[1]!.Method);
        Assert.Null(endsBeforeRestore[2]);
    }

    // A root that holds the shared callback in an ordinary field declared
    // before the room, so the capture reaches the delegate through that field
    // first and records it as the delegate's owner edge. A mod caching its own
    // handler in a field of its own is the everyday shape of this.
    private sealed class CachedCallbackSceneRoot {
        public Action CachedEnd = null!;
        public Scene Scene = null!;
        public EntityList Entities = null!;
    }

    private static CachedCallbackSceneRoot CreateCachedCallbackScene(
        params (string Name, bool EndCallback)[] modStates
    ) {
        StateSlotSceneRoot room = CreateStateSlotScene(true, modStates);
        return new CachedCallbackSceneRoot {
            CachedEnd = SampleTimerMod.Callback,
            Scene = room.Scene,
            Entities = room.Entities
        };
    }

    // Two chronologies reach the same saved frame - a machine whose callback is
    // on "second-mod-state" at id 2 - and each test needs its own, because the
    // baseline is a clone of the room as it LOADED in the session that set the
    // slot and capture reads it.
    //
    // movedDuringPlay false: the mod wired the callback to "second-mod-state"
    // at load, so the baseline already carries it there.
    // movedDuringPlay true: the mod wired it to "first-mod-state" at load, the
    // baseline is cloned there, and only then does the saved room call the
    // public SetCallbacks twice to move it - the mutation the test is about,
    // performed rather than imitated.
    private static AkronReconstructionRestore RestoreStateSlotScene(
        StateSlotSceneRoot fresh,
        bool sharedModClosure,
        bool movedDuringPlay
    ) {
        StateSlotSceneRoot saved = CreateStateSlotScene(
            sharedModClosure,
            ("first-mod-state", movedDuringPlay),
            ("second-mod-state", !movedDuringPlay));
        StateSlotSceneRoot baseline = CreateStateSlotScene(
            sharedModClosure,
            ("first-mod-state", movedDuringPlay),
            ("second-mod-state", !movedDuringPlay));
        if (movedDuringPlay) {
            StateMachine savedMachine = GetStateSlotMachine(saved);
            StateSlotEntity savedOwner =
                (StateSlotEntity) GetRuntimeField<Entity>(savedMachine, "<Entity>k__BackingField");
            Action endCallback = sharedModClosure ? SampleTimerMod.Callback : savedOwner.EndState;
            SetStateCallbacks(savedMachine, 1, savedOwner.RunState, null);
            SetStateCallbacks(savedMachine, 2, savedOwner.RunState, endCallback);
        }
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource, _ => string.Empty);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);

        // Whichever chronology produced it, the saved frame is the same room.
        Action[] savedEnds = GetRuntimeField<Action[]>(GetStateSlotMachine(saved), "ends");
        Assert.Null(savedEnds[1]);
        Assert.NotNull(savedEnds[2]);
        return graph.Restore(capture.Document, fresh);
    }

    private static StateMachine GetStateSlotMachine(StateSlotSceneRoot room) {
        return (StateMachine) GetComponentListContents(GetEntityListContents(room.Entities)[0])
            .Single(component => component is StateMachine);
    }

    private sealed class StateSlotSceneRoot {
        public Scene Scene = null!;
        public EntityList Entities = null!;
    }

    private sealed class StateSlotEntity : Entity {
        public int RunState() {
            return 1;
        }

        public void EndState() {
        }
    }

    // The unnamed half of the state-slot defect. Nine popular helpers still add
    // states with the pre-2023 reflection idiom, which resizes the four callback
    // arrays and never names, so both sides of the restore read the added slot as
    // unnamed and a name comparison reads that as agreement. The two rooms below
    // are the same population - one base state, mod A's state and mod B's state -
    // and differ only in the order the two mods ran, which is what a mod install
    // or removal changes between setting a slot and loading it.
    //
    // Both tests build the machine through Monocle's own SetCallbacks and assert
    // on the machine's own ends array afterwards, so the callbacks the rooms
    // actually run are what is measured.
    [Fact]
    public void ARestoreRefusesAnUnnamedStateSlotTheFreshRoomBuiltForAnotherMod() {
        StateSlotSceneRoot saved = CreateUnnamedStateSlotScene(
            (ProbeHelperModA.Drive, ProbeHelperModA.Leave),
            (ProbeHelperModB.Drive, ProbeHelperModB.Leave));
        StateSlotSceneRoot baseline = CreateUnnamedStateSlotScene(
            (ProbeHelperModA.Drive, ProbeHelperModA.Leave),
            (ProbeHelperModB.Drive, ProbeHelperModB.Leave));
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource, _ => string.Empty);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);

        StateSlotSceneRoot fresh = CreateUnnamedStateSlotScene(
            (ProbeHelperModB.Drive, ProbeHelperModB.Leave),
            (ProbeHelperModA.Drive, ProbeHelperModA.Leave));
        StateMachine freshMachine = GetStateSlotMachine(fresh);
        // The premise: the machine really is short-named, so nothing in names
        // separates slot 1 from slot 2 in either room.
        Assert.Single(GetRuntimeField<string[]>(freshMachine, "names"));
        Assert.Equal(3, GetRuntimeField<Action[]>(freshMachine, "ends").Length);
        Assert.Equal(
            new[] { "<null>", nameof(ProbeHelperModB), nameof(ProbeHelperModA) },
            UnnamedSceneEndMethodNames(fresh));

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.False(restore.Success);
        Assert.Contains("saved state slot is a different state in the fresh room", restore.Error);
        // The refused restore leaves this room running its own arrangement: mod
        // B's state is still the one this session numbered 1. Without the fix the
        // restore reports success and both arrays come back in the saved order,
        // [<null>,ProbeHelperModA,ProbeHelperModB], while this session's mods go
        // on holding the ids a clean load handed them.
        Assert.Equal(
            new[] { "<null>", nameof(ProbeHelperModB), nameof(ProbeHelperModA) },
            UnnamedSceneEndMethodNames(fresh));
    }

    // The control, and the reason an unnamed slot cannot simply be refused. Every
    // player of those nine helpers loads this room on every restore: the mod set
    // did not change, both sessions numbered the states the same way, and the
    // saved frame is the arrangement the fresh room already has. Refusing a write
    // into an unnamed slot outright would cost all of them their slot.
    [Fact]
    public void AnUnnamedStateSlotTheFreshRoomNumberedTheSameWayIsStillRestored() {
        StateSlotSceneRoot saved = CreateUnnamedStateSlotScene(
            (ProbeHelperModA.Drive, ProbeHelperModA.Leave),
            (ProbeHelperModB.Drive, ProbeHelperModB.Leave));
        StateSlotSceneRoot baseline = CreateUnnamedStateSlotScene(
            (ProbeHelperModA.Drive, ProbeHelperModA.Leave),
            (ProbeHelperModB.Drive, ProbeHelperModB.Leave));
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource, _ => string.Empty);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);

        StateSlotSceneRoot fresh = CreateUnnamedStateSlotScene(
            (ProbeHelperModA.Drive, ProbeHelperModA.Leave),
            (ProbeHelperModB.Drive, ProbeHelperModB.Leave));

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Equal(
            new[] { "<null>", nameof(ProbeHelperModA), nameof(ProbeHelperModB) },
            UnnamedSceneEndMethodNames(fresh));
    }

    // The unnamed analogue of RestoreAcceptsACallbackAModMovedToAnotherStateSlot
    // DuringPlay, and the reason the fallback reads updates and coroutines rather
    // than all four callback arrays. Both sessions numbered the two states the
    // same way and the same mods drive them, so both rooms agree about what slot
    // 1 and slot 2 are. The one difference is where mod A's end callback sits:
    // the clean load leaves it on slot 2 and the saved frame has it on slot 1,
    // which is the public SetCallbacks being used during play. Widening the
    // fallback to begins and ends would refuse this frame.
    [Fact]
    public void AnUnnamedStateSlotAcceptsAnEndCallbackAModMovedWhileTheRoomWasPlayed() {
        StateSlotSceneRoot saved = CreateUnnamedStateSlotScene(
            (ProbeHelperModA.Drive, ProbeHelperModA.Leave),
            (ProbeHelperModB.Drive, null!));
        StateSlotSceneRoot baseline = CreateUnnamedStateSlotScene(
            (ProbeHelperModA.Drive, ProbeHelperModA.Leave),
            (ProbeHelperModB.Drive, null!));
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource, _ => string.Empty);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);

        StateSlotSceneRoot fresh = CreateUnnamedStateSlotScene(
            (ProbeHelperModA.Drive, null!),
            (ProbeHelperModB.Drive, ProbeHelperModA.Leave));

        AkronReconstructionRestore restore = graph.Restore(capture.Document, fresh);

        Assert.True(restore.Success, restore.Error);
        Assert.Equal(
            new[] { "<null>", nameof(ProbeHelperModA), "<null>" },
            UnnamedSceneEndMethodNames(fresh));
    }

    // One entity carrying one Monocle StateMachine whose mod states are added
    // through Monocle's own AddState, in the order given. AddState is what
    // assigns the state id and what writes the names entry, so the order here
    // is the installed mod order two sessions can disagree about.
    private static StateSlotSceneRoot CreateStateSlotScene(
        bool sharedModClosure,
        params (string Name, bool EndCallback)[] modStates
    ) {
        return CreateStateSlotScene((machine, stateOwner) => {
            Action endCallback = sharedModClosure ? SampleTimerMod.Callback : stateOwner.EndState;
            foreach ((string name, bool endCallback_) in modStates) {
                AddNamedState(machine, name, stateOwner.RunState, endCallback_ ? endCallback : null);
            }
        });
    }

    private static StateSlotSceneRoot CreateStateSlotScene(Action<StateMachine, StateSlotEntity> addModStates) {
        Scene scene = (Scene) RuntimeHelpers.GetUninitializedObject(typeof(Scene));
        EntityList entityList = LinkSceneEntities(scene, CreateDetachedEntityList());

        StateSlotEntity stateOwner = CreateUninitializedEntity<StateSlotEntity>();
        ComponentList stateComponents = CreateDetachedComponentList(stateOwner);
        SetRuntimeField(stateOwner, "<Scene>k__BackingField", scene);
        SetRuntimeField(stateOwner, "<SourceId>k__BackingField", CreateEntityId("a00", 12));

        // One base state, so the mod states start at id 1 the way they do on a
        // machine the game built. CI supplies Celeste as a reference assembly,
        // so this test-owned fixture writes the constructor state directly.
        StateMachine machine = CreateDetachedStateMachine(stateOwner, 1);
        addModStates(machine, stateOwner);
        SetRuntimeField(machine, "state", 1);
        List<Component> stateOrdered = new List<Component> { machine };
        SetRuntimeField(stateComponents, "components", stateOrdered);
        SetRuntimeField(stateComponents, "current", new HashSet<Component>(stateOrdered));
        AddDetachedEntity(entityList, stateOwner);

        return new StateSlotSceneRoot { Scene = scene, Entities = entityList };
    }

    // The pre-2023 reflection idiom: resize the four callback arrays, leave names
    // alone, then write the new slot. This is what XaphanHelper,
    // BrokemiaHelper, JackalHelper, IsaGrabBag and PrismaticHelper still ship,
    // and the slot it produces is unnamed on both sides of a restore.
    private static int AddUnnamedState(StateMachine machine, Func<int> update, Action end) {
        Action[] begins = GetRuntimeField<Action[]>(machine, "begins");
        Func<int>[] updates = GetRuntimeField<Func<int>[]>(machine, "updates");
        Action[] ends = GetRuntimeField<Action[]>(machine, "ends");
        Func<IEnumerator>[] coroutines = GetRuntimeField<Func<IEnumerator>[]>(machine, "coroutines");
        int slot = begins.Length;
        Array.Resize(ref begins, slot + 1);
        Array.Resize(ref updates, slot + 1);
        Array.Resize(ref ends, slot + 1);
        Array.Resize(ref coroutines, slot + 1);
        updates[slot] = update;
        ends[slot] = end;
        SetRuntimeField(machine, "begins", begins);
        SetRuntimeField(machine, "updates", updates);
        SetRuntimeField(machine, "ends", ends);
        SetRuntimeField(machine, "coroutines", coroutines);
        return slot;
    }

    private static StateMachine CreateDetachedStateMachine(Entity owner, int stateCount) {
        StateMachine machine = (StateMachine) RuntimeHelpers.GetUninitializedObject(typeof(StateMachine));
        SetRuntimeField(machine, "<Entity>k__BackingField", owner);
        SetRuntimeField(machine, "Active", true);
        SetRuntimeField(machine, "state", -1);
        SetRuntimeField(machine, "<PreviousState>k__BackingField", -1);
        SetRuntimeField(machine, "begins", new Action[stateCount]);
        SetRuntimeField(machine, "updates", new Func<int>[stateCount]);
        SetRuntimeField(machine, "ends", new Action[stateCount]);
        SetRuntimeField(machine, "coroutines", new Func<IEnumerator>[stateCount]);
        SetRuntimeField(machine, "names", new string[stateCount]);

        // StateMachine owns this coroutine without adding it to an Entity. Match
        // `new Coroutine(false)`: inactive, not removable, and with an empty stack.
        Coroutine currentCoroutine = (Coroutine) RuntimeHelpers.GetUninitializedObject(typeof(Coroutine));
        SetRuntimeField(currentCoroutine, "enumerators", new Stack<IEnumerator>());
        SetRuntimeField(machine, "currentCoroutine", currentCoroutine);
        return machine;
    }

    private static void SetStateCallbacks(
        StateMachine machine,
        int slot,
        Func<int> update,
        Action? end
    ) {
        GetRuntimeField<Func<int>[]>(machine, "updates")[slot] = update;
        GetRuntimeField<Action[]>(machine, "ends")[slot] = end!;
    }

    private static int AddNamedState(
        StateMachine machine,
        string name,
        Func<int> update,
        Action? end
    ) {
        Action[] begins = GetRuntimeField<Action[]>(machine, "begins");
        Func<int>[] updates = GetRuntimeField<Func<int>[]>(machine, "updates");
        Action[] ends = GetRuntimeField<Action[]>(machine, "ends");
        Func<IEnumerator>[] coroutines = GetRuntimeField<Func<IEnumerator>[]>(machine, "coroutines");
        string[] names = GetRuntimeField<string[]>(machine, "names");
        int slot = begins.Length;
        Array.Resize(ref begins, slot + 1);
        Array.Resize(ref updates, slot + 1);
        Array.Resize(ref ends, slot + 1);
        Array.Resize(ref coroutines, slot + 1);
        Array.Resize(ref names, slot + 1);
        updates[slot] = update;
        ends[slot] = end!;
        names[slot] = name;
        SetRuntimeField(machine, "begins", begins);
        SetRuntimeField(machine, "updates", updates);
        SetRuntimeField(machine, "ends", ends);
        SetRuntimeField(machine, "coroutines", coroutines);
        SetRuntimeField(machine, "names", names);
        return slot;
    }

    // One room whose only mod states came through the reflection idiom, in the
    // order given. The order is the installed mod order two sessions disagree
    // about, exactly as it is for AddState above.
    private static StateSlotSceneRoot CreateUnnamedStateSlotScene(
        params (Func<int> Update, Action End)[] modStates
    ) {
        return CreateStateSlotScene((machine, _) => {
            foreach ((Func<int> update, Action end) in modStates) {
                AddUnnamedState(machine, update, end);
            }
        });
    }

    private static string[] UnnamedSceneEndMethodNames(StateSlotSceneRoot room) {
        return GetRuntimeField<Action[]>(GetStateSlotMachine(room), "ends")
            .Select(callback => callback?.Method.DeclaringType?.Name ?? "<null>")
            .ToArray();
    }
}

// Stand-ins for a mod's own types, used by the refusal-message tests. They are top-level
// and in this assembly on purpose: the message builder decides what to say from the
// assembly a refused type belongs to, so a test needs a real, loadable type in an
// assembly that is neither the game's nor Akron's. SampleZoomLevel's callback is a
// non-capturing lambda, so Roslyn gives it a cached-closure singleton and its type is
// the exact compiler-generated shape ExtendedVariantMode's refusal lands on.
internal sealed class SampleZoomLevel {
    internal static readonly Action Callback = () => { };
}

internal sealed class SampleUnderwaterSwitchController {
}

// A mod's own type for the shared-callback room. One static readonly Action is
// one delegate object for the whole process, and the lambda captures nothing,
// so Roslyn puts it on this type's cached-closure singleton. That is what makes
// the room hold the same instance in two slots with a compiler-generated target.
internal sealed class SampleTimerMod {
    internal static readonly Action Callback = () => { };
}

// Two helper mods that add a state through the pre-2023 reflection idiom. Each
// drives its own state with its own update method and leaves it with its own end
// method, which is the whole of what separates the two states once neither is
// named. They are top-level and distinct types on purpose: a shift moves a slot
// from one mod's code to the other's, and that is what the fix reads.
internal static class ProbeHelperModA {
    internal static int Drive() {
        return 1;
    }

    internal static void Leave() {
    }
}

internal static class ProbeHelperModB {
    internal static int Drive() {
        return 1;
    }

    internal static void Leave() {
    }
}

// A helper mod's own playback ghost. Its saved state is not Akron's to drop.
internal sealed class ModdedPlayerPlayback : PlayerPlayback {
    private ModdedPlayerPlayback()
        : base(Microsoft.Xna.Framework.Vector2.Zero, PlayerSpriteMode.Playback, null) {
    }
}
