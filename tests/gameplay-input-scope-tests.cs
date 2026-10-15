using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Monocle;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

[Collection(AkronSharedStateCollection.Name)]
public sealed class GameplayInputScopeTests {
    private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    private static FieldInfo Field(Type type, string name) => type.GetField(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
    private static void Set(object target, string name, object value) => Field(target.GetType(), name).SetValue(target, value);
    private static T Get<T>(object target, string name) => (T)Field(target.GetType(), name).GetValue(target)!;

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NeutralizesAndRestoresAllDirectionalCaches(bool disabled, bool throws) {
        bool original = MInput.Disabled;
        var integer = Empty<VirtualIntegerAxis>();
        integer.Value = int.MaxValue;
        integer.PreviousValue = int.MinValue;
        var axis = Empty<VirtualAxis>();
        Set(axis, "<Value>k__BackingField", .75f);
        Set(axis, "<PreviousValue>k__BackingField", -.25f);
        var joystick = Empty<VirtualJoystick>();
        Set(joystick, "<Value>k__BackingField", new Vector2(.3f, .7f));
        Set(joystick, "<PreviousValue>k__BackingField", new Vector2(-.3f, -.7f));
        Set(joystick, "value", new Vector2(.2f, .8f));
        Set(joystick, "previousValue", new Vector2(-.2f, -.8f));
        var inputs = new List<VirtualInput> { integer, axis, joystick };
        try {
            MInput.Disabled = disabled;
            void Update() {
                Assert.True(MInput.Disabled);
                Assert.Equal(0, integer.Value);
                Assert.Equal(0, integer.PreviousValue);
                Assert.Equal(0f, Get<float>(axis, "<Value>k__BackingField"));
                Assert.Equal(0f, Get<float>(axis, "<PreviousValue>k__BackingField"));
                foreach (string name in new[] { "<Value>k__BackingField", "<PreviousValue>k__BackingField", "value", "previousValue" })
                    Assert.Equal(Vector2.Zero, Get<Vector2>(joystick, name));
                // Changing the registry must not change restoration targets.
                inputs.Clear();
                if (throws) throw new InvalidOperationException("fixture");
            }
            if (throws) Assert.Throws<InvalidOperationException>(() => AkronGameplayInputScope.Run(inputs, Update));
            else AkronGameplayInputScope.Run(inputs, Update);
            Assert.Equal(disabled, MInput.Disabled);
            Assert.Equal(int.MaxValue, integer.Value);
            Assert.Equal(int.MinValue, integer.PreviousValue);
            Assert.Equal(.75f, Get<float>(axis, "<Value>k__BackingField"));
            Assert.Equal(-.25f, Get<float>(axis, "<PreviousValue>k__BackingField"));
            Assert.Equal(new Vector2(.3f,.7f), Get<Vector2>(joystick, "<Value>k__BackingField"));
            Assert.Equal(new Vector2(-.3f,-.7f), Get<Vector2>(joystick, "<PreviousValue>k__BackingField"));
            Assert.Equal(new Vector2(.2f,.8f), Get<Vector2>(joystick, "value"));
            Assert.Equal(new Vector2(-.2f,-.8f), Get<Vector2>(joystick, "previousValue"));
        } finally { MInput.Disabled = original; }
    }

    [Fact]
    public void NestedScopeRestoresOuterNeutralState() {
        bool original = MInput.Disabled;
        var axis = Empty<VirtualIntegerAxis>();
        axis.Value = 1; axis.PreviousValue = -1;
        var inputs = new VirtualInput[] { axis };
        try {
            MInput.Disabled = false;
            AkronGameplayInputScope.Run(inputs, () => {
                AkronGameplayInputScope.Run(inputs, () => Assert.Equal(0, axis.Value));
                Assert.True(MInput.Disabled);
                Assert.Equal(0, axis.Value);
                Assert.Equal(0, axis.PreviousValue);
            });
            Assert.False(MInput.Disabled);
            Assert.Equal(1, axis.Value);
            Assert.Equal(-1, axis.PreviousValue);
        } finally { MInput.Disabled = original; }
    }
}
