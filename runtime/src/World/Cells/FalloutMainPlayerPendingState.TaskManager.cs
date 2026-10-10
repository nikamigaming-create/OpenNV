namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutMainPlayerPendingState
{
    private FalloutSourceTaskManager? _standaloneTaskManager;
    private Action? _requireTaskManagerNativeMainClosed;

    internal void BindStandaloneTaskManager(FalloutSourceTaskManager manager, Action requireNativeMainClosed)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_busy) { _callbackFault = checked(_callbackFault + 1); throw new InvalidOperationException("Task-manager construction cannot reenter a living pending child."); }
        ArgumentNullException.ThrowIfNull(manager); ArgumentNullException.ThrowIfNull(requireNativeMainClosed);
        if (_source.Player.Main.HasNewVegasChildren || _standaloneTaskManager is not null)
            throw new InvalidOperationException("Standalone task-manager owner must bind its selected actual pending process once.");
        manager.RequireBinding(_source.Player, _stack, _process);
        // A fresh current constructor may bind a failed cold prefix for
        // diagnostics/retirement. Binding never clears that prefix or permits
        // replay through Run/Reset.
        _standaloneTaskManager = manager; _requireTaskManagerNativeMainClosed = requireNativeMainClosed;
    }

    private FalloutSourceTaskManager StandaloneTaskManager => _standaloneTaskManager ??
        throw new NotSupportedException("actual-source-standalone-task-manager-construction-and-current-cohort-unbound");

    private void ResetSourceTaskManager(FalloutMainPlayerCellInvocation invocation)
    {
        if (_source.Player.Main.HasNewVegasChildren) ExteriorLoaders.CancelAll(invocation);
        else StandaloneTaskManager.Reset(invocation);
    }

    private FalloutSourceTaskManagerSnapshot? CaptureStandaloneTaskManager() =>
        _source.Player.Main.HasNewVegasChildren ? null : StandaloneTaskManager.Capture();

    private void RetireStandaloneTaskManager()
    {
        if (_source.Player.Main.HasNewVegasChildren) return;
        // A constructor failure before binding is visible through the world
        // construction receipt. It owns no manager to fabricate or destroy.
        if (_standaloneTaskManager is null) return;
        _standaloneTaskManager.Retire(_requireTaskManagerNativeMainClosed ??
            throw new NotSupportedException("Actual task-manager native delivery retirement guard is absent."));
    }

    private static void ValidateStandaloneTaskManager(FalloutMainPlayerPendingSnapshot saved)
    {
        if (saved.Source.Player.Main.HasNewVegasChildren)
        {
            if (saved.TaskManager is not null)
                throw new InvalidDataException("New Vegas pending continuation imported another selected task reset owner.");
            return;
        }
        var manager = saved.TaskManager ?? throw new InvalidDataException("Standalone Player pending continuation omitted its actual task-manager constructor/cohort.");
        FalloutSourceTaskManager.Validate(manager);
        if (manager.Player != saved.Source.Player || manager.Stack != saved.Stack || manager.Process != saved.CapturedProcess)
            throw new InvalidDataException("Task manager and Player pending fields belong to different selected source/current processes.");
    }
}
