using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutExteriorCellLoaderState
{
    internal FalloutExteriorCellLoaderSnapshot Capture()
    {
        if (_busy) { _callbackFault = checked(_callbackFault + 1); throw new NotSupportedException("Entered exterior loader traversal cannot become a cold completed map."); }
        if (_buckets.Any(bucket => bucket.Count != 0) || _failedConstructions.Count != 0)
            throw new NotSupportedException("Source exterior loader tasks require genuine native/task-manager cold continuation.");
        var result = new FalloutExteriorCellLoaderSnapshot(_source.Contract, _stack, _process, _sequence,
            [], _last, _failureType, _error, _cold);
        Validate(result, _source); return result;
    }
    private void Restore(FalloutExteriorCellLoaderSnapshot saved)
    {
        Validate(saved, _source);
        if (saved.Stack != _stack || saved.Process == _process || saved.Tasks.Count != 0)
            throw new InvalidDataException("Cold exterior loader manager changed stack/process or promoted opaque native tasks.");
        _sequence = saved.Sequence; _last = saved.LastCancellation; _failureType = saved.FailureType; _error = saved.Error;
        _cold = new(saved.Process, _process, Next());
    }
    internal static void Validate(FalloutExteriorCellLoaderSnapshot saved, FalloutMainPlayerPendingSource source)
    {
        source.Validate();
        if (saved is null || saved.Source != source.Contract || string.IsNullOrWhiteSpace(saved.Stack) ||
            saved.Process == Guid.Empty || saved.Sequence < 0 || saved.Tasks is null || saved.Tasks.Count != 0 ||
            (saved.Error is null) != (saved.FailureType is null) || saved.Error is { Length: 0 } ||
            saved.LastCancellation is { } last && (last.Main == Guid.Empty || last.Tasks is null ||
                last.Tasks.Any(identity => identity == Guid.Empty) || last.Tasks.Distinct().Count() != last.Tasks.Count ||
                last.Returned < 0 || last.Returned > last.Tasks.Count || last.Changed < 1 || last.Changed > saved.Sequence ||
                (last.Error is null) != (last.FailureType is null) || last.Error is null && last.Returned != last.Tasks.Count) ||
            saved.Handoff is { } cold && (cold.PreviousProcess == Guid.Empty || cold.CurrentProcess != saved.Process ||
                cold.PreviousProcess == cold.CurrentProcess || cold.Sequence < 1 || cold.Sequence > saved.Sequence))
            throw new InvalidDataException("Exterior loader continuation lost source map, cancellation order or actual cold handoff.");
    }
    private static void ValidateTask(FalloutExteriorCellLoaderTaskSource task, FalloutMainPlayerPendingSource source)
    {
        if (task is null || task.Identity == Guid.Empty || task.Invocation == Guid.Empty || string.IsNullOrWhiteSpace(task.Owner) ||
            task.Cell is null || task.Cell.Cell.ObjectId == 0 || task.Cell.Worldspace is null ||
            task.Cell.Sha256 is not { Length: 64 } || !task.Cell.Sha256.All(Uri.IsHexDigit) ||
            task.Cell.WorldspaceSha256 is not { Length: 64 } || !task.Cell.WorldspaceSha256.All(Uri.IsHexDigit) ||
            task.Key != source.PackCellKey(task.X, task.Y))
            throw new InvalidDataException("Exterior loader task differs from its actual winning world/CELL/packed coordinate source.");
    }
}
