using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Celeste.Mod.Akron;
using MonoMod.Utils;
using Xunit;

namespace FrostHelper.ModIntegration {
    internal interface ISavestatePersisted {
    }
}

namespace Celeste.Mod.Akron.Tests {
    [Collection(AkronSharedStateCollection.Name)]
    public sealed class FrostHelperSavestateTests {
        [Fact]
        public void FrostHelperSavestatePersistedObjectsAreReturnedByReference() {
            FrostHelperPersistedProbe probe = new FrostHelperPersistedProbe();
            Func<Type, bool> predicate = AkronNativeSavestateSupport.ShouldReturnSameObjectForNativeClone;

            AkronSaveLoadService.AddReturnSameObjectProcessor(predicate);
            AkronDeepClone.Initialize();
            try {
                object clone = AkronSaveLoadService.DeepClone(probe);

                Assert.Same(probe, clone);
            } finally {
                AkronSaveLoadService.RemoveReturnSameObjectProcessor(predicate);
                AkronDeepClone.Reset();
            }
        }

        [Fact]
        public void DeepClonePreservesMonoModDynamicDataSidecars() {
            DynamicDataProbe probe = new DynamicDataProbe();
            DynData<DynamicDataProbe> sourceData = new DynData<DynamicDataProbe>(probe);
            sourceData.Set("SpringCollab2020_ignoreLighting", true);
            sourceData.Set("owner", probe);
            new DynData<DynamicDataProbeBase>(probe).Set("base-owner", probe);
            new DynData<IDynamicDataProbe>(probe).Set("interface-owner", probe);
            DynamicData sourceDynamicData = new DynamicData(probe);
            sourceDynamicData.Data["mod-name"] = "SpringCollab2020";

            AkronDeepClone.Initialize();
            try {
                DynamicDataProbe clone = Assert.IsType<DynamicDataProbe>(AkronSaveLoadService.DeepClone(probe));
                DynData<DynamicDataProbe> cloneData = new DynData<DynamicDataProbe>(clone);

                Assert.NotSame(probe, clone);
                Assert.True(cloneData.Get<bool>("SpringCollab2020_ignoreLighting"));
                Assert.Same(clone, cloneData.Get<DynamicDataProbe>("owner"));
                Assert.Same(clone, new DynData<DynamicDataProbeBase>(clone).Get<DynamicDataProbe>("base-owner"));
                Assert.Same(clone, new DynData<IDynamicDataProbe>(clone).Get<DynamicDataProbe>("interface-owner"));
                Assert.Equal("SpringCollab2020", new DynamicData(clone).Data["mod-name"]);
            } finally {
                AkronDeepClone.Reset();
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CopyIntoRestoresReadonlyFieldsWithTheRequestedCopyDepth(bool deep) {
            ReadonlyCloneProbe source = new ReadonlyCloneProbe(47, new List<int> { 37 });
            ReadonlyCloneProbe target = new ReadonlyCloneProbe(12, new List<int> { 5 });
            AkronDeepClone.Initialize();
            AkronDeepClone.ClearSharedState();
            try {
                if (deep) {
                    AkronDeepClone.CopyInto(source, target);
                } else {
                    Force.DeepCloner.DeepClonerExtensions.ShallowCloneTo(source, target);
                }

                Assert.Equal(47, target.Value);
                Assert.Same(target.Items, target.Alias);
                source.Items.Add(99);
                Assert.Equal(deep ? new[] { 37 } : new[] { 37, 99 }, target.Items);
            } finally {
                AkronDeepClone.Reset();
            }
        }

        [Theory]
        [InlineData("Celeste.Mod.LuaCoroutine, Celeste")]
        [InlineData("NLua.LuaTable, NLua")]
        public void NativeLuaStateIsRefusedBeforeCloningItsHandles(string typeName) {
            object source = RuntimeHelpers.GetUninitializedObject(Type.GetType(typeName, throwOnError: true)!);
            GC.SuppressFinalize(source);
            object? clone = null;
            AkronDeepClone.Initialize();
            AkronDeepClone.ClearSharedState();
            try {
                Exception? refusal = Record.Exception(() => clone = AkronSaveLoadService.DeepClone(source));
                if (clone != null) {
                    GC.SuppressFinalize(clone);
                }

                Assert.IsType<AkronReconstructionException>(refusal);
            } finally {
                AkronDeepClone.Reset();
            }
        }

        private sealed class ReadonlyCloneProbe {
            public readonly int Value;
            public readonly List<int> Items;
            public readonly List<int> Alias;

            public ReadonlyCloneProbe(int value, List<int> items) {
                Value = value;
                Items = items;
                Alias = items;
            }
        }

        private sealed class FrostHelperPersistedProbe : FrostHelper.ModIntegration.ISavestatePersisted {
        }

        private interface IDynamicDataProbe {
        }

        private class DynamicDataProbeBase {
        }

        private sealed class DynamicDataProbe : DynamicDataProbeBase, IDynamicDataProbe {
        }
    }
}
