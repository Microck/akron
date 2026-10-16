using Monocle;

namespace Celeste.Mod.Akron;

// Gameplay state belongs to the player graph. SRT clones this inert component
// with its Player, preserving a spent allowance in airborne savestates without
// introducing an Entity-typed static field or retaining another player graph.
internal sealed class AkronAirJumpQuota : Component {
    internal int Used;

    internal AkronAirJumpQuota() : base(false, false) {
    }
}
