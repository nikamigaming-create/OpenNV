namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerAdvancement
{
    // Presentation is process-local. A restored source request is published
    // separately from consuming its level, skill budget or acquired rank.
    private bool _menuPublished;
    internal bool OwnsNativeMenuReceipt(FalloutLevelUpMenuSession menu) => ReferenceEquals(Menu, menu) ||
        ReferenceEquals(_retiredNativeMenu, menu) && menu.Generation == _generation;
    internal void RetainNativeMenuFailure(FalloutLevelUpMenuSession menu, Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (!OwnsNativeMenuReceipt(menu))
            throw new InvalidOperationException("Native failure has no actual current or completed menu receipt.", error);
        _ = Retain(error);
    }
    internal bool PublishPendingMenu()
    {
        RequireHealthy();
        if (Menu is null || Menu.Completed || _menuPublished) return false;
        try
        {
            var admission = _binding.Admission();
            if (admission.UnsupportedOwner is { } unsupported) throw new NotSupportedException(unsupported);
            if (!admission.Ready) return false;
            PublishMenu(); return true;
        }
        catch (Exception error) when (Retainable(error)) { throw Retain(error); }
    }
    private void PublishMenu()
    {
        var menu = Menu ?? throw new InvalidOperationException("Advancement has no consumed menu request to publish.");
        if (_menuPublished) throw new InvalidOperationException("Advancement menu has already been published in this process.");
        _menuPublished = true;
        _binding.Present(menu);
    }
}
