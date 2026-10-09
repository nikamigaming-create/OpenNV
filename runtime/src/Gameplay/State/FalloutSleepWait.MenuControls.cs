namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutSleepWait
{
    internal FalloutRestMenuControls RequireCurrentMenuControls() => _menuControls ??
        throw new NotSupportedException("The actual source request has no original menu-control owner.");

    internal void BeginSourceMenuCounting(Action<FalloutRestMenuTarget> publishActualNativeTarget)
    {
        RequirePublished();
        if (Phase != FalloutRestPhase.Choosing || Request?.Origin == FalloutRestOrigin.ScriptHours ||
            !_busy || Failure is not null)
            throw new InvalidOperationException("Source menu controls must run inside the actual Start operation before player hours.");
        RequireCurrentMenuControls().Begin(RequestOrdinal, publishActualNativeTarget);
    }
}
