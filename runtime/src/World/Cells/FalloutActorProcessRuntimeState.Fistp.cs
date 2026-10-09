using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessRuntimeState
{
    private FalloutSourceFistpState _sourceFistp = null!;
    private void ConstructSourceFistp() => _sourceFistp = new(_mainFrameDeclaration, _stack, _process);
    internal IDisposable BindCallingThreadFistp(FalloutCallingThreadFistpHost host)
    {
        RequireNotBusy(); RequireMainScriptClosureBoundary(retiring: false);
        return _sourceFistp.Bind(host);
    }
    private FalloutSourceFistpSnapshot CaptureSourceFistp() => _sourceFistp.Capture();
    private void RestoreSourceFistp(FalloutSourceFistpSnapshot saved)
    {
        _sourceFistp.Dispose(); _sourceFistp = new(_mainFrameDeclaration, _stack, _process, saved);
    }
    private static void ValidateRuntimeSourceFistp(FalloutActorProcessRuntimeSnapshot saved)
    {
        FalloutSourceFistpState.Validate(saved.Fistp);
        if (saved.Fistp.CapturedProcess != saved.CapturedProcess || saved.Fistp.Stack != saved.Stack ||
            saved.Fistp.Contract != saved.MainFrame.Contract ||
            FalloutActorProcessRuntimeDeclaration.ForExecutable(saved.Fistp.ExecutableSha256).Contract != saved.Contract)
            throw new InvalidDataException("Current FISTP changed its exact owning Main/process/source continuation.");
    }
    internal (int X, int Y) ConvertMainPlayerSourceGrid(FalloutMainPlayerCellInvocation invocation,
        uint xBits, uint yBits, FalloutSourceFistpSite site, FalloutPlayerPendingRequest? pending = null)
    {
        var step = site switch
        {
            FalloutSourceFistpSite.PlayerContainment => FalloutMainPlayerCellStep.ContainmentQuery,
            FalloutSourceFistpSite.PlayerTargetCell => FalloutMainPlayerCellStep.TargetCellRead,
            FalloutSourceFistpSite.PlayerPendingWorldspace => FalloutMainPlayerCellStep.PendingDestination,
            _ => throw new InvalidDataException("No selected original Main Player consumer owns this FISTP site.")
        };
        Require();
        var context = new FalloutSourceFistpContext(_process, invocation.Main.Identity, invocation.Main.Ordinal,
            step, site, pending?.Identity);
        return _sourceFistp.ConvertPair(context, xBits, yBits, Require);
        void Require()
        {
            RequireMainPlayerCellChild(invocation, step);
            _mainPlayerCellSource?.Validate();
            if (_mainPlayerCellSource is null || _mainPlayerCellSource.Main.EngineSha256 != _source.ExecutableSha256 ||
                (site == FalloutSourceFistpSite.PlayerPendingWorldspace) != (pending is not null))
                throw new InvalidDataException("FISTP lost its selected source Player/request consumer.");
            if (pending is not null)
            {
                (_mainPlayerPending ?? throw new InvalidOperationException("Pending FISTP has no actual source slot.")).Require(pending);
                var payload = pending.SourcePayload ?? throw new InvalidDataException("Pending FISTP has no source raw factory payload.");
                if (payload.Target != FalloutPlayerTransferTarget.Worldspace || payload.PositionX != xBits || payload.PositionY != yBits)
                    throw new InvalidDataException("Pending FISTP changed the source-captured worldspace operand bits.");
            }
        }
    }
    internal FalloutMainPlayerCellInvocation RequireCurrentPendingConversion(FalloutPlayerPendingRequest request)
    {
        var child = _mainPlayerCellChild ?? throw new NotSupportedException("Player placement lookup has no actual current Main pending-destination consumer.");
        RequireMainPlayerCellChild(child, FalloutMainPlayerCellStep.PendingDestination);
        (_mainPlayerPending ?? throw new InvalidOperationException("Current Player source slot is absent.")).Require(request);
        return child;
    }
    private bool MainPlayerPositionContained(FalloutMainPlayerCellInvocation invocation,
        FalloutMainPlayerSourceCell cell, FalloutMainPlayerSourcePosition position)
    {
        RequireMainPlayerCellChild(invocation, FalloutMainPlayerCellStep.ContainmentQuery);
        if ((cell.CellFlags & 1) != 0 || cell.X is null || cell.Y is null)
            throw new InvalidDataException("Original exterior containment has no exact XCLC source coordinates.");
        var coordinates = ConvertMainPlayerSourceGrid(invocation, position.XBits, position.YBits, FalloutSourceFistpSite.PlayerContainment);
        return cell.X == coordinates.X && cell.Y == coordinates.Y;
    }
}
