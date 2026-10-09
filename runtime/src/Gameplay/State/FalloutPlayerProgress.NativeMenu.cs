namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerProgress
{
    internal bool PublishPendingMenu()
    {
        RequireHealthy();
        try { return _advancement?.PublishPendingMenu() == true; }
        catch (Exception error) when (Retainable(error)) { _error ??= FailureMessage(error); throw; }
    }
    internal bool OwnsMenu(FalloutLevelUpMenuSession menu) => ReferenceEquals(Menu, menu);
    internal bool OwnsNativeMenuReceipt(FalloutLevelUpMenuSession menu) => _advancement?.OwnsNativeMenuReceipt(menu) == true;

    internal void RetainMenuFailure(FalloutLevelUpMenuSession menu, Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (!OwnsNativeMenuReceipt(menu)) throw new InvalidOperationException("Native level-up failure does not own an actual menu receipt.", error);
        _advancement!.RetainNativeMenuFailure(menu, error);
        _error ??= FailureMessage(error);
    }
}
