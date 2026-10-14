using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessCommonState
{
    internal void RequireControllerMoverDelta(FalloutControllerMoverDeltaInvocation invocation,
        FalloutEarlyController actualController)
    {
        RequireNotBusy();
        var process = ReadSceneQueries(invocation.Frame.Common.Actor.Reference, invocation.Frame.Common.Epoch);
        var entry = Require(process.Actor.Reference);
        // The retained pointer getter is valid inside its actual child. Calling
        // the controller's independent idle/resource gate here would reenter
        // that child and reject a legitimate source mutation.
        if (!_earlyControllerFields.TryGetValue(process.Actor.Reference, out var field) || field.Lease is null ||
            field.Failure is not null || !ReferenceEquals(field.Lease.Controller, actualController) ||
            entry.SceneFields?.PostSceneContext != actualController.Identity ||
            invocation.Context.Context != actualController.Identity || process.Process != invocation.Frame.Common.Process ||
            process.GameplayOwnership != invocation.Frame.Common.GameplayOwnership || field.Epoch != process.Epoch)
            throw new InvalidOperationException("Controller delta lost its actual same retained Common pointer/epoch/lease.");
    }
    private string? MoverDeltaProcessSaveBlocker => _current.Values.Select(entry => entry.SceneFields?.MoverDelta)
        .Where(fields => fields is not null).Select(fields => fields!.SpecialBoundary ?? fields.DialogBoundary).FirstOrDefault(boundary => boundary is not null);
    internal FalloutMoverDeltaProcessRead ReadMoverDeltaProcessByte(FalloutPlayerLateFrameInvocation invocation,
        FalloutMoverDeltaProcessCell cell, Action<FalloutPlayerLateFrameInvocation> requireActual)
    {
        RequireNotBusy(); ArgumentNullException.ThrowIfNull(requireActual);
        requireActual(invocation); var process = ReadSceneQueries(invocation.Common.Actor.Reference, invocation.Common.Epoch);
        if (process.Process != invocation.Common.Process || process.GameplayOwnership != invocation.Common.GameplayOwnership ||
            process.Level != invocation.Common.Level || process.ExecutableSha256 != invocation.Common.ExecutableSha256)
            throw new InvalidDataException("Mover delta process byte changed its actual mutable class/lease/current epoch.");
        var fields = Require(process.Actor.Reference).SceneFields?.MoverDelta ??
            throw new NotSupportedException("source-mover-delta-selected-process-byte-constructor-unqualified");
        var (value, boundary) = cell switch
        {
            FalloutMoverDeltaProcessCell.SpecialMotion => (fields.SpecialMotion, fields.SpecialBoundary),
            FalloutMoverDeltaProcessCell.DialogMotion => (fields.DialogMotion, fields.DialogBoundary),
            _ => throw new InvalidDataException("Mover delta read an unreviewed process cell."),
        };
        if (boundary is not null) throw new NotSupportedException(boundary);
        requireActual(invocation); return new(process, cell, value);
    }
    internal void ObserveUnownedMoverDeltaProcessWriter(FalloutFormKey actor, long epoch,
        FalloutMoverDeltaProcessCell cell, string boundary)
    {
        RequireNotBusy(); ArgumentException.ThrowIfNullOrWhiteSpace(boundary);
        _ = ReadSceneQueries(actor, epoch); var current = Require(actor);
        var fields = current.SceneFields?.MoverDelta ?? throw new NotSupportedException("source-mover-delta-current-process-byte-unconstructed");
        fields = cell switch
        {
            FalloutMoverDeltaProcessCell.SpecialMotion => fields with { SpecialBoundary = boundary },
            FalloutMoverDeltaProcessCell.DialogMotion => fields with { DialogBoundary = boundary },
            _ => throw new InvalidDataException("Mover delta invalidated an unreviewed process cell."),
        };
        _current[actor] = current with { SceneFields = current.SceneFields! with { MoverDelta = fields }, Changed = Next() };
    }
    private static void ValidateMoverDeltaProcessFields(FalloutProcessCommonEntry entry, string executable)
    {
        var fields = entry.SceneFields?.MoverDelta;
        var expected = FalloutSourceMainFamily.IsFallout3(executable) && entry.Level == FalloutDetectionProcessLevel.High;
        if (expected != (fields is not null) ||
            fields is { } value && (value.SpecialMotion != 0 || value.DialogMotion != 0 ||
                value.SpecialBoundary is { } first && string.IsNullOrWhiteSpace(first) ||
                value.DialogBoundary is { } second && string.IsNullOrWhiteSpace(second)))
            throw new InvalidDataException("Mover delta process fields lost their constructor or admitted an unowned byte writer.");
    }
}
