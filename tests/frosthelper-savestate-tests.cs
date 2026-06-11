using System;
using Celeste.Mod.Akron;
using Xunit;

namespace FrostHelper.ModIntegration {
    internal interface ISavestatePersisted {
    }
}

namespace Celeste.Mod.Akron.Tests {
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

        private sealed class FrostHelperPersistedProbe : FrostHelper.ModIntegration.ISavestatePersisted {
        }
    }
}
