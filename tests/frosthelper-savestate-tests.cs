using System;
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
