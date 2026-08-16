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
using System.Runtime.CompilerServices;
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
            Value = new AkronReconstructionValue { Kind = "reference", NodeId = 2 }
        });
        AkronReconstructionNode source = new AkronReconstructionNode { Id = 2 };
        source.Fields.Add(new AkronReconstructionField {
            Name = nameof(SoundSource.EventName),
            Value = new AkronReconstructionValue {
                Kind = "scalar",
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
        VirtualContent.Assets.Add(texture);
        try {
            string key = typeof(VirtualTexture).AssemblyQualifiedName + "|" +
                         AkronStartPosReconstruction.GetLiveResourceKey(texture);

            object resolved = AkronStartPosReconstruction.ResolveDetachedLiveResource(
                typeof(VirtualTexture),
                key);

            Assert.Same(texture, resolved);
        } finally {
            VirtualContent.Assets.Remove(texture);
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
                Kind = "scalar",
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
        call.Target = new AkronReconstructionValue { Kind = "null" };
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
    public void DeserializeRejectsAParentChainThatExceedsTheDepthLimit() {
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
                    Value = new AkronReconstructionValue { Kind = "reference", NodeId = id + 1 }
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
            Value = new AkronReconstructionValue { Kind = "reference", NodeId = 2 }
        });
        document.Nodes.Add(root);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        string json = graph.Serialize(document);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => graph.Deserialize(json));

        Assert.Contains("parent depth exceeds", exception.Message);
    }

    [Fact]
    public void DeserializeRejectsDiagnosticPathsPastTheSizeLimit() {
        ChainNode saved = BuildChain(64, valueOffset: 1000);
        ChainNode baseline = BuildChain(64, valueOffset: 0);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved, baseline);
        Assert.True(capture.Success, capture.Error);
        Dictionary<int, AkronReconstructionNode> nodes = capture.Document.Nodes.ToDictionary(node => node.Id);
        foreach (AkronReconstructionNode child in capture.Document.Nodes.Where(node => node.ParentNodeId > 0)) {
            AkronReconstructionNode parent = nodes[child.ParentNodeId];
            AkronReconstructionField parentField = parent.Fields.Single(field =>
                field.Value?.Kind == "reference" && field.Value.NodeId == child.Id);
            string longFieldName = parentField.Name + new string('x', 1024);
            parentField.Name = longFieldName;
            child.ParentFieldName = longFieldName;
        }
        string json = graph.Serialize(capture.Document);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => graph.Deserialize(json));

        Assert.Contains("diagnostic path exceeds", exception.Message);
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
            Value = new AkronReconstructionValue { Kind = "reference", NodeId = 2 }
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
            Value = new AkronReconstructionValue { Kind = "reference", NodeId = 3 }
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
            Value = new AkronReconstructionValue { Kind = "reference", NodeId = 2 }
        });
        document.Nodes.Add(root);
        document.Nodes.Add(first);
        document.Nodes.Add(second);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            graph.Deserialize(Newtonsoft.Json.JsonConvert.SerializeObject(document)));

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
            graph.Deserialize(Newtonsoft.Json.JsonConvert.SerializeObject(capture.Document)));

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
            new[] { ("ExtendedVariantMode", "ExtendedVariantMode") });

        Assert.Equal(
            "StartPos 3 could not be rebuilt: this room has no Sprite to match, and no mod owns " +
            "it. If your mods have not changed, this is an Akron bug; report akron-current.log.",
            message);
        Assert.True(message!.Length <= AkronActions.MaxStartPosFailureToastLength);
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
                AkronStartPosRefusal.Describe("StartPos 1", refusedTypeName, Array.Empty<(string, string)>()));
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
            ownerIsAuthenticatedRuntimeEntity: true,
            owner));
        Assert.False(AkronReconstructionGraph.IsAuthenticatedCompilerIteratorOwner(
            routineType,
            ownerIsFresh: false,
            ownerIsAuthenticatedRuntimeEntity: false,
            owner));
        Assert.False(AkronReconstructionGraph.IsAuthenticatedCompilerIteratorOwner(
            routineType,
            ownerIsFresh: false,
            ownerIsAuthenticatedRuntimeEntity: true,
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
            ownerIsAuthenticatedRuntimeEntity: false,
            RuntimeHelpers.GetUninitializedObject(typeof(PlayerSprite))));
        Assert.True(AkronReconstructionGraph.IsAuthenticatedCompilerIteratorOwner(
            playUtilType,
            ownerIsFresh: true,
            ownerIsAuthenticatedRuntimeEntity: false,
            RuntimeHelpers.GetUninitializedObject(typeof(Sprite))));
        Assert.False(AkronReconstructionGraph.IsAuthenticatedCompilerIteratorOwner(
            playUtilType,
            ownerIsFresh: true,
            ownerIsAuthenticatedRuntimeEntity: false,
            RuntimeHelpers.GetUninitializedObject(typeof(Image))));
        Assert.False(AkronReconstructionGraph.IsAuthenticatedCompilerIteratorOwner(
            playUtilType,
            ownerIsFresh: true,
            ownerIsAuthenticatedRuntimeEntity: false,
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
            Kind = "reference",
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
        PlayerPlayback playback = CreateUninitializedEntity<PlayerPlayback>();
        playback.Timeline = new List<Player.ChaserState> {
            new Player.ChaserState(), new Player.ChaserState(), new Player.ChaserState()
        };
        playback.TrimEnd = 5f;
        SetRuntimeField(playback, "index", 1);
        SetRuntimeField(playback, "time", 2f);

        playback.Visible = false;
        Assert.True(AkronModule.WasPlaybackHiddenByAkron(playback));

        playback.Visible = true;
        Assert.False(AkronModule.WasPlaybackHiddenByAkron(playback));

        // The just-constructed state is invisible with the index past the end, and
        // the end-of-loop state is invisible at TrimEnd. Neither is Akron's doing.
        playback.Visible = false;
        SetRuntimeField(playback, "index", playback.Timeline.Count);
        Assert.False(AkronModule.WasPlaybackHiddenByAkron(playback));

        SetRuntimeField(playback, "index", 1);
        SetRuntimeField(playback, "time", 5f);
        Assert.False(AkronModule.WasPlaybackHiddenByAkron(playback));
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
        Assert.IsType<PlayerPlayback>(freshHair.Entity);
        Assert.Null(freshHair.Entity.Scene);
        Assert.DoesNotContain(freshHair.Entity, GetEntityListContents(fresh.Entities));
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
        Assert.Same(liveGhost, fresh.Snapshot.Sprite.Entity);
        Assert.Same(fresh.Level, liveGhost.Scene);
        Assert.Contains(liveGhost, GetEntityListContents(fresh.Entities));
        Assert.Equal(2.5f, liveGhost.Time);
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
        Assert.Same(fresh.Owner, freshSprite.Entity);
        Assert.Same(fresh.Level, fresh.Owner.Scene);
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
        PlaybackGhostReloadRoom fresh = new PlaybackGhostReloadRoom {
            Root = new PlaybackGhostReloadRoot { Level = level },
            Level = level,
            Entities = entities,
            Ghost = liveGhost,
            Snapshot = snapshot
        };
        Image freshSprite = snapshot.Sprite;
        PlayerHair freshHair = snapshot.Hair;

        AkronReconstructionRestore restore = RestoreTrailingGhostDocumentInto(fresh);

        // Accepted, with no authenticator: the saved document asked for the fresh Level
        // at a path the fresh room does hold a Level at, and that was enough.
        Assert.True(restore.Success, restore.Error);

        // WRONG: the room's own sprite no longer points at the ghost the room holds.
        Entity? spriteOwner = freshSprite.Entity;
        Assert.NotSame(liveGhost, spriteOwner);
        PlayerPlayback reconstructedGhost = Assert.IsType<PlayerPlayback>(spriteOwner);
        // WRONG: the reconstructed copy takes the entity-list slot of the ghost the room
        // load built, and gets the live Level in its Scene on the occurrence budget
        // alone. The ghost LoadLevel produced is dropped from the room entirely.
        Assert.Same(level, reconstructedGhost.Scene);
        Assert.Contains(reconstructedGhost, GetEntityListContents(entities));
        Assert.DoesNotContain(liveGhost, GetEntityListContents(entities));
        // WRONG: the saved state landed on the reconstructed copy, and the ghost the
        // room actually holds kept its clean-load state.
        Assert.Equal(2.5f, reconstructedGhost.Time);
        Assert.Equal(0f, liveGhost.Time);
        // WRONG: the surviving snapshot keeps the room's PlayerSprite but is handed a
        // reconstructed PlayerHair, so the two halves of one trail disagree about who
        // they belong to.
        Assert.Same(freshSprite, snapshot.Sprite);
        Assert.NotSame(freshHair, snapshot.Hair);
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
        Assert.Same(fresh.Level, liveGhost.Scene);
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
            graph.Deserialize("{\"Nodes\":[{\"DelegateCalls\":[{},{}]}]}"));

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
            graph.Deserialize("{\"Nodes\":[{\"DelegateCalls\":[{},{}]}]}"));

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

    [Fact]
    public void DeserializeRejectsTooManyJsonContainersWhileStreaming() {
        AkronReconstructionGraph graph = new AkronReconstructionGraph(
            IsLiveResource,
            maxJsonTokenCount: 100,
            maxJsonContainerCount: 1,
            maxJsonStringChars: 100,
            maxJsonBinaryBytes: 100);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            graph.Deserialize("{\"Format\":\"akron-reconstruction-v8\",\"Nodes\":[]}"));

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
            .Where(value => value["Kind"]?.Value<string>() is "null" or "reference");
        Assert.NotEmpty(emptyMetadataValues);
        Assert.All(emptyMetadataValues, value => {
            Assert.Null(value.Property("TypeName"));
            Assert.Null(value.Property("Scalar"));
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
        Assert.Equal(new[] { 11f, 22f, 33f }, restored.Select(entity =>
            Assert.IsType<Hitbox>(entity.Collider).Width));
        Assert.Contains(restored, entity => freshEntities.All(freshEntity =>
            !ReferenceEquals(entity, freshEntity)));
        Assert.Same(restored[1], Assert.Single(restored[0].HasBelow).Key);
        Assert.Empty(restored[1].HasBelow);
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
        entity.Collider = new Hitbox(Math.Max(1, value), 8f);
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

    private static AkronReconstructionGraph CreateStartPosGraph() {
        return new AkronReconstructionGraph(
            AkronStartPosReconstruction.IsLiveResourceType,
            AkronStartPosReconstruction.GetLiveResourceKey,
            null,
            AkronStartPosReconstruction.ResolveDetachedLiveResource,
            areEquivalentLiveResources: AkronStartPosReconstruction.AreEquivalentLiveResources);
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
            Assert.Equal("akron-reconstruction-v8", document.Format);
            Assert.Equal("LightBuffer", Assert.Single(document.GameplayBuffers).FieldName);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, document.GameplayBuffers[0].Payload.Bytes);
            Assert.Contains("v8-", Path.GetFileName(AkronStartPosReconstruction.GetSnapshotPath("Akron StartPos test 1", directory)));
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
            depth: trailOwner.Depth + 1);

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

    private static AkronReconstructionRestore RestoreTrailingGhostDocumentInto(
        PlaybackGhostReloadRoom fresh,
        bool savedGhostIsTrailing = true
    ) {
        PlaybackGhostReloadRoom saved = CreateTrailingGhostRoom(savedGhostIsTrailing, ghostTime: 2.5f);
        PlaybackGhostReloadRoom baseline = CreateTrailingGhostRoom(savedGhostIsTrailing, ghostTime: 0f);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(IsLiveResource);
        AkronReconstructionCapture capture = graph.Capture(saved.Root, baseline.Root);
        Assert.True(capture.Success, capture.Error);
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

    private static T GetRuntimeField<T>(object owner, string name) where T : class {
        return (T) GetRuntimeFieldInfo(owner.GetType(), name).GetValue(owner)!;
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

    private sealed class SpriteStateMachineEntity : Entity {
        public Sprite Animation = null!;

        public IEnumerator StateRoutine() {
            yield return InvokeSpritePlayUtil(Animation);
        }
    }

    private static IEnumerator InvokeSpritePlayUtil(Sprite sprite) {
        return (IEnumerator) typeof(Sprite)
            .GetMethod("PlayUtil", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(sprite, null)!;
    }

    private static SpriteRoutineSceneRoot CreateSpriteRoutineScene(
        bool includeIterator,
        bool spriteFirst,
        Type spriteType,
        ExtraCoroutine extra = ExtraCoroutine.None
    ) {
        Scene scene = (Scene) RuntimeHelpers.GetUninitializedObject(typeof(Scene));
        EntityList entityList = LinkSceneEntities(scene, CreateDetachedEntityList());
        SpriteStateMachineEntity owner = CreateUninitializedEntity<SpriteStateMachineEntity>();
        ComponentList components = CreateDetachedComponentList(owner);
        SetRuntimeField(owner, "<Scene>k__BackingField", scene);
        SetRuntimeField(owner, "<SourceId>k__BackingField", CreateEntityId("a00", 31));

        Sprite sprite = (Sprite) RuntimeHelpers.GetUninitializedObject(spriteType);
        SetRuntimeField(sprite, "<Entity>k__BackingField", owner);
        // Sprite.PlayUtil is `while (Animating) yield return null`, so the
        // routine only stays mid-flight while the sprite reports it is playing.
        SetRuntimeField(sprite, "<Animating>k__BackingField", true);
        owner.Animation = sprite;

        StateMachine machine = (StateMachine) RuntimeHelpers.GetUninitializedObject(typeof(StateMachine));
        SetRuntimeField(machine, "<Entity>k__BackingField", owner);

        // StateMachine's constructor does `currentCoroutine = new Coroutine()`
        // and never adds it to the entity's ComponentList, so the live
        // Coroutine's Entity stays null. Setting it here would hand the graph
        // an ownership link the game does not provide.
        Coroutine coroutine = (Coroutine) RuntimeHelpers.GetUninitializedObject(typeof(Coroutine));
        Stack<IEnumerator> iterators = new Stack<IEnumerator>();
        SetRuntimeField(coroutine, "enumerators", iterators);
        SetRuntimeField(machine, "currentCoroutine", coroutine);
        if (includeIterator) {
            // Build the stack the way the game does rather than by hand.
            // StateMachine.State pushes the bare state routine through
            // Coroutine.Replace, and Coroutine.Update is then the only thing
            // that ever advances it: it wraps the bare iterator in Everest's
            // Flattened, calls MoveNext on the Flattened, and pushes whatever
            // the routine yielded. Two frames is the first moment the shape is
            // steady, and it is the shape a StartPos set during the wake-up
            // intro captures. Advancing the inner routine by hand instead
            // leaves Flattened.current null, which is a shape the game never
            // produces and which hides the alias edge this room exists to
            // exercise.
            iterators.Push(owner.StateRoutine());
            coroutine.Update();
            coroutine.Update();
        }

        List<Component> ordered = spriteFirst
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
                secondFrames.Push(GetRuntimeField<Stack<IEnumerator>>(running, "enums").Single().SafeEnumerate());
            }
            SetRuntimeField(second, "enumerators", secondFrames);
            ordered.Add(second);
        }
        SetRuntimeField(components, "components", ordered);
        SetRuntimeField(components, "current", new HashSet<Component>(ordered));
        AddDetachedEntity(entityList, owner);
        return new SpriteRoutineSceneRoot { Scene = scene, Entities = entityList };
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
            // Built through Monocle's own factory rather than by hand, so the
            // component is armed the way Alarm.Set arms one.
            Alarm alarm = Alarm.Create(Alarm.AlarmMode.Oneshot, SampleTimerMod.Callback, 0.25f, start: true);
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

        // Monocle's own constructor and callback plumbing. The update callback
        // is a method group on the entity, the way Celeste's Player wires its
        // own states, so its target is the entity rather than a closure.
        StateMachine machine = new StateMachine(4);
        SetRuntimeField(machine, "<Entity>k__BackingField", stateOwner);
        machine.SetCallbacks(
            1,
            stateOwner.RunState,
            null,
            null,
            includeEndCallback ? SampleTimerMod.Callback : null);
        if (includeExtraState) {
            // AddState is what resizes the five callback arrays.
            machine.AddState("mod-state", stateOwner.RunModState);
        }
        // Monocle's own state setter, so the machine is actually running the
        // state whose end callback this room is about rather than sitting on
        // the -1 no entity has ever driven it out of.
        machine.State = 1;
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
            StateSlotEntity savedOwner = (StateSlotEntity) savedMachine.Entity;
            Action endCallback = sharedModClosure ? SampleTimerMod.Callback : savedOwner.EndState;
            savedMachine.SetCallbacks(1, savedOwner.RunState, null, null, null);
            savedMachine.SetCallbacks(2, savedOwner.RunState, null, null, endCallback);
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

    // One entity carrying one Monocle StateMachine whose mod states are added
    // through Monocle's own AddState, in the order given. AddState is what
    // assigns the state id and what writes the names entry, so the order here
    // is the installed mod order two sessions can disagree about.
    private static StateSlotSceneRoot CreateStateSlotScene(
        bool sharedModClosure,
        params (string Name, bool EndCallback)[] modStates
    ) {
        Scene scene = (Scene) RuntimeHelpers.GetUninitializedObject(typeof(Scene));
        EntityList entityList = LinkSceneEntities(scene, CreateDetachedEntityList());

        StateSlotEntity stateOwner = CreateUninitializedEntity<StateSlotEntity>();
        ComponentList stateComponents = CreateDetachedComponentList(stateOwner);
        SetRuntimeField(stateOwner, "<Scene>k__BackingField", scene);
        SetRuntimeField(stateOwner, "<SourceId>k__BackingField", CreateEntityId("a00", 12));

        // One base state, so the mod states start at id 1 the way they do on a
        // machine the game built.
        StateMachine machine = new StateMachine(1);
        SetRuntimeField(machine, "<Entity>k__BackingField", stateOwner);
        Action endCallback = sharedModClosure ? SampleTimerMod.Callback : stateOwner.EndState;
        foreach ((string name, bool endCallback_) in modStates) {
            machine.AddState(
                name,
                stateOwner.RunState,
                null,
                null,
                endCallback_ ? endCallback : null);
        }
        // Monocle's own state setter, so the machine is actually running a state
        // rather than sitting on the -1 no entity has ever driven it out of.
        machine.State = 1;
        List<Component> stateOrdered = new List<Component> { machine };
        SetRuntimeField(stateComponents, "components", stateOrdered);
        SetRuntimeField(stateComponents, "current", new HashSet<Component>(stateOrdered));
        AddDetachedEntity(entityList, stateOwner);

        return new StateSlotSceneRoot { Scene = scene, Entities = entityList };
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

// A helper mod's own playback ghost. Its saved state is not Akron's to drop.
internal sealed class ModdedPlayerPlayback : PlayerPlayback {
    private ModdedPlayerPlayback()
        : base(Microsoft.Xna.Framework.Vector2.Zero, PlayerSpriteMode.Playback, null) {
    }
}
