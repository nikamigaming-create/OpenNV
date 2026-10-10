namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutMainPlayerPendingState
{
    private FalloutSourceDeferredRelease? _deferredRelease;
    private FalloutNewVegasDeferredRelease? _newVegasDeferredRelease;
    private Action? _requireNewVegasMainClosed;
    internal object? DeferredReleaseState => _source.Player.Main.HasNewVegasChildren ? _newVegasDeferredRelease?.State : _deferredRelease?.State;
    internal FalloutNewVegasDeferredRelease CurrentNewVegasDeferredRelease => _newVegasDeferredRelease ??
        throw new NotSupportedException("actual-source-New-Vegas-deferred-release-construction-unbound");
    internal FalloutSourceDeferredRelease CurrentDeferredRelease => _deferredRelease ??
        throw new NotSupportedException(_source.Player.Main.HasNewVegasChildren ?
            "source-NV-deferred-release-calling-thread-TLS-skip-mask-and-try-lock-unowned" :
            "actual-source-standalone-deferred-release-construction-unbound");
    private string? DeferredReleaseSaveBlocker => _source.Player.Main.HasNewVegasChildren ?
        _newVegasDeferredRelease?.SaveBlocker ?? (_newVegasDeferredRelease is null ? "actual-source-New-Vegas-deferred-release-construction-unbound" : null) :
        _deferredRelease?.SaveBlocker ?? (_deferredRelease is null ? "actual-source-standalone-deferred-release-construction-unbound" : null);

    internal void BindNewVegasDeferredRelease(FalloutNewVegasDeferredRelease manager, Action requireMainClosed)
    {
        ObjectDisposedException.ThrowIf(_retired, this); ArgumentNullException.ThrowIfNull(manager); ArgumentNullException.ThrowIfNull(requireMainClosed);
        if (_busy || !_source.Player.Main.HasNewVegasChildren || _newVegasDeferredRelease is not null)
            throw new InvalidOperationException("New Vegas release constructor changed its actual pending source lifetime.");
        manager.RequireBinding(_source.Player, _stack, _process);
        _newVegasDeferredRelease = manager; _requireNewVegasMainClosed = requireMainClosed;
    }

    internal void BindCurrentDeferredRelease(FalloutSourceDeferredRelease manager)
    {
        ObjectDisposedException.ThrowIf(_retired, this); ArgumentNullException.ThrowIfNull(manager);
        if (_busy) { _callbackFault = checked(_callbackFault + 1); throw new InvalidOperationException("Release construction cannot reenter a real pending child."); }
        if (_source.Player.Main.HasNewVegasChildren || _deferredRelease is not null)
            throw new InvalidOperationException("Current global release arrays must bind their selected actual Main process once.");
        manager.RequireBinding(_source.Player, _stack, _process); _deferredRelease = manager;
    }

    internal void RetireDeferredChildren(FalloutMainPlayerCellInvocation invocation) =>
        Run(invocation, FalloutMainPlayerCellStep.PendingDeferredDestruction, () =>
        {
            if (_source.Player.Main.HasNewVegasChildren) CurrentNewVegasDeferredRelease.FlushPending(invocation);
            else CurrentDeferredRelease.FlushPending(invocation);
        });

    private FalloutSourceDeferredReleaseSnapshot? CaptureDeferredRelease() =>
        _source.Player.Main.HasNewVegasChildren ? null : CurrentDeferredRelease.Capture();

    private static void ValidateDeferredRelease(FalloutMainPlayerPendingSnapshot saved)
    {
        if (saved.Source.Player.Main.HasNewVegasChildren)
        {
            if (saved.DeferredRelease is not null)
                throw new InvalidDataException("New Vegas pending continuation imported the independent standalone release manager.");
            var newVegas = saved.NewVegasDeferredRelease ?? throw new InvalidDataException("New Vegas pending continuation omitted its actual lock/queue/thread constructor.");
            FalloutNewVegasDeferredRelease.Validate(newVegas);
            if (newVegas.Player != saved.Source.Player || newVegas.Stack != saved.Stack || newVegas.Process != saved.CapturedProcess)
                throw new InvalidDataException("Pending Player and New Vegas release manager belong to different source/current processes.");
            return;
        }
        if (saved.NewVegasDeferredRelease is not null)
            throw new InvalidDataException("Standalone pending continuation imported New Vegas compiler-thread and lock owners.");
        var release = saved.DeferredRelease ?? throw new InvalidDataException("Standalone pending continuation omitted its actual global release arrays/enable cell.");
        FalloutSourceDeferredRelease.Validate(release);
        if (release.Player != saved.Source.Player || release.Stack != saved.Stack || release.Process != saved.CapturedProcess)
            throw new InvalidDataException("Pending Player and global release manager belong to different source/current processes.");
    }

    private void RetireCurrentDeferredRelease()
    {
        if (_source.Player.Main.HasNewVegasChildren)
        {
            if (_newVegasDeferredRelease is not null)
                _newVegasDeferredRelease.Retire(_requireNewVegasMainClosed ?? throw new InvalidOperationException("New Vegas release lost its actual Main closure guard."));
        }
        else _deferredRelease?.Retire();
    }
}
