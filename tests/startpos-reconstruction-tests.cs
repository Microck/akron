using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        Assert.False(AkronReconstructionGraph.IsTrailSnapshotComponentReference(
            typeof(TrailManager.Snapshot), nameof(TrailManager.Snapshot.Manager), typeof(TrailManager)));
        Assert.False(AkronReconstructionGraph.IsTrailSnapshotComponentReference(
            typeof(TalkComponent.TalkComponentUI), nameof(TrailManager.Snapshot.Hair), typeof(PlayerHair)));
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
            graph.Deserialize("{\"Format\":\"akron-reconstruction-v7\",\"Nodes\":[]}"));

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
            Assert.Equal("akron-reconstruction-v7", document.Format);
            Assert.Equal("LightBuffer", Assert.Single(document.GameplayBuffers).FieldName);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, document.GameplayBuffers[0].Payload.Bytes);
            Assert.Contains("v7-", Path.GetFileName(AkronStartPosReconstruction.GetSnapshotPath("Akron StartPos test 1", directory)));
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

    private sealed class RuntimeTypeRoot {
        public Type TrackerKey = null!;
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
}
