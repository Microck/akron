namespace Celeste.Mod.Akron;

internal sealed class AkronMouseButtonReleaseGate {
    private bool waitingForRelease;

    internal void SuppressUntilRelease() => waitingForRelease = true;

    internal bool Filter(bool physicallyDown) {
        if (!waitingForRelease) return physicallyDown;
        if (!physicallyDown) waitingForRelease = false;
        return false;
    }
}
