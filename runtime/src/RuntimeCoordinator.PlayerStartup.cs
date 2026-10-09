using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private enum NativePlayerSetupPhase { Absent, Constructing, Prepared, Published, Failed }
    private NativePlayerSetupPhase _nativePlayerSetupPhase;
    private string? _nativePlayerSetupFailure;
    private bool NativePlayerSetupPrepared => _nativePlayerSetupPhase is
        NativePlayerSetupPhase.Prepared or NativePlayerSetupPhase.Published;

    private void RetireNativeReferenceWorld()
    {
        if (_nativeReferences is not { } world) return;
        var failures = new List<Exception>();
        try { world.BindPreparedSourceMutableRetirement(this); } catch (Exception failure) { failures.Add(failure); }
        try { world.Dispose(); } catch (Exception failure) { failures.Add(failure); }
        if (failures.Count != 0) throw new AggregateException("Native world source handoff and retirement retained failures.", failures);
    }

    private void AddNativePlayer(FalloutCellScene initialCell, FalloutReferencePlacement? startupPlacement = null)
    {
        if (_nativePlayerSetupPhase != NativePlayerSetupPhase.Absent)
            throw new InvalidOperationException("Player setup already entered this campaign lifetime.");
        _nativePlayerSetupPhase = NativePlayerSetupPhase.Constructing;
        try
        {
            PrepareNativePlayer(initialCell, startupPlacement);
            _nativePlayerSetupPhase = NativePlayerSetupPhase.Prepared;
        }
        catch (Exception error)
        {
            RetainNativePlayerSetupFailure(error);
            throw;
        }
    }

    private void PublishNativePlayerGameplay()
    {
        try
        {
            if (_nativePlayerSetupPhase != NativePlayerSetupPhase.Prepared || _nativePlayerSetupFailure is not null ||
                _nativePlayer is not { } player || !GodotObject.IsInstanceValid(player) || !player.IsInsideTree() ||
                player.IsQueuedForDeletion() || player.ProcessMode != ProcessModeEnum.Disabled ||
                _nativeOpeningStageDriver is not { } driver || !GodotObject.IsInstanceValid(driver) || !driver.IsInsideTree() ||
                driver.IsQueuedForDeletion() || driver.ProcessMode != ProcessModeEnum.Disabled ||
                _nativeCurrentCellRoot is not { } root || _nativeActiveCell is not { } cell ||
                !GodotObject.IsInstanceValid(root) || !root.IsInsideTree() || root.IsQueuedForDeletion())
                throw new InvalidOperationException("Player callbacks require the completely constructed living campaign and CELL.");
            RequireNativeSourceCellSelection(root, cell);
            driver.ProcessMode = ProcessModeEnum.Inherit;
            player.ProcessMode = ProcessModeEnum.Inherit;
            _nativePlayerSetupPhase = NativePlayerSetupPhase.Published;
        }
        catch (Exception error)
        {
            RetainNativePlayerSetupFailure(error);
            throw;
        }
    }

    private void RetainNativePlayerSetupFailure(Exception original)
    {
        _nativePlayerSetupPhase = NativePlayerSetupPhase.Failed;
        _nativePlayerSetupFailure ??= original.ToString();
        var failures = new List<Exception> { original };
        foreach (var node in new Node?[] { _nativePlayer, _nativeOpeningStageDriver })
        {
            if (node is null || !GodotObject.IsInstanceValid(node)) continue;
            try { node.ProcessMode = ProcessModeEnum.Disabled; }
            catch (Exception cleanup) { failures.Add(cleanup); }
        }
        if (failures.Count == 1) return;
        var retained = new AggregateException("Player setup failed and its partial callback owners could not all stop.", failures);
        _nativePlayerSetupFailure = retained.ToString();
        throw retained;
    }
}
