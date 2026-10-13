using System;
using System.Runtime.CompilerServices;
using Monocle;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class LevelEnterSkipTests
{
    [Fact]
    public void MissingEntityListDoesNotOfferPostcardSkip()
    {
        Assert.False(AkronModule.ContainsLevelEnterPostcard(null));
    }

    [Fact]
    public void EmptySceneDoesNotOfferPostcardSkip()
    {
        Assert.False(AkronModule.ContainsLevelEnterPostcard(Array.Empty<Entity>()));
    }

    [Fact]
    public void UnrelatedEntitiesDoNotOfferPostcardSkip()
    {
        Entity unrelated = (Entity)RuntimeHelpers.GetUninitializedObject(typeof(Entity));
        Assert.False(AkronModule.ContainsLevelEnterPostcard(new[] { unrelated }));
    }

    [Fact]
    public void UntrackedPostcardIsFoundWithoutTrackerRegistration()
    {
        // Avoid postcard content/audio initialization: this lookup needs only type identity.
        Postcard postcard = (Postcard)RuntimeHelpers.GetUninitializedObject(typeof(Postcard));
        Entity unrelated = (Entity)RuntimeHelpers.GetUninitializedObject(typeof(Entity));
        Assert.True(AkronModule.ContainsLevelEnterPostcard(new Entity[] { unrelated, postcard }));
    }

    [Fact]
    public void LookupDoesNotRetainAPostcardFromThePreviousEntityList()
    {
        Postcard postcard = (Postcard)RuntimeHelpers.GetUninitializedObject(typeof(Postcard));
        Assert.True(AkronModule.ContainsLevelEnterPostcard(new Entity[] { postcard }));
        Assert.False(AkronModule.ContainsLevelEnterPostcard(Array.Empty<Entity>()));
    }
}
