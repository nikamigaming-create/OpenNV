using OpenNV.Runtime.Content;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutSkyModeSnapshot(FalloutSkyModeCall? Call, IReadOnlyList<FalloutSkyModeChild> Children,
    string? DeferredUnowned, Guid? HistoricalSky, Guid? HistoricalProcess);
internal sealed partial class FalloutSkyTransferState
{
    private FalloutSkyModeSnapshot? CaptureMode()
    {
        if (!Source.IsStandalone) return null;
        if (ModeSaveBlocker is { } blocker) throw new NotSupportedException(blocker);
        return new(_modeCall, _modeChildren.Values.OrderBy(row => row.Role).ToArray(), _modeDeferredUnowned,
            _modeHistoricalSky, _modeHistoricalProcess);
    }
    private static void ValidateMode(FalloutSkyTransferSnapshot saved, FalloutPluginStack records)
    {
        if (!saved.Source.IsStandalone)
        {
            if (saved.ModeFields is not null || saved.Mode != 4)
                throw new InvalidDataException("Saved FNV Sky introduced standalone-only mode consumers.");
            return;
        }
        var fields = saved.ModeFields ?? throw new InvalidDataException("Sky capture omitted actual constructor/mode child fields.");
        if (fields.Children is null || fields.Children.Count != Enum.GetValues<FalloutSkyModeRole>().Length ||
            !fields.Children.Select(row => row.Role).ToHashSet().SetEquals(Enum.GetValues<FalloutSkyModeRole>()) ||
            fields.Children.Any(row => row.Owner == Guid.Empty || row.Owner is not null && string.IsNullOrWhiteSpace(row.Unowned)) ||
            fields.DeferredUnowned is not null || saved.ModeFailure is not null)
            throw new InvalidDataException("Saved Sky mode lost constructor fields or admitted an unreturned native child.");
        if (fields.Call is not { } call)
        {
            if (saved.Mode != 4) throw new InvalidDataException("Sky mode has no actual returned writer.");
            return;
        }
        var source = FalloutSkyModeSource.Read(saved.Source);
        var record = records.GetEffective(call.Cell);
        var data = record.ReadSubrecords().Where(row => row.Signature == "DATA").ToArray();
        if (fields.HistoricalSky.HasValue != fields.HistoricalProcess.HasValue ||
            fields.HistoricalSky == Guid.Empty || fields.HistoricalProcess == Guid.Empty ||
            fields.HistoricalSky == saved.CapturedSky || fields.HistoricalProcess == saved.CapturedProcess ||
            call.Call == Guid.Empty || call.Main == Guid.Empty || call.Process != (fields.HistoricalProcess ?? saved.CapturedProcess) ||
            call.Sky != (fields.HistoricalSky ?? saved.CapturedSky) ||
            call.SourceContract != source.Contract || call.Entered < 1 || call.Changed <= call.Entered || call.Changed > saved.Changed ||
            call.CellSha256.Length != 64 || !call.CellSha256.All(Uri.IsHexDigit) || !call.Returned || call.EnteredChild is not null ||
            call.FailureType is not null || call.Error is not null || call.After != saved.Mode ||
            source.Requested(call.Before, call.CellFlags) != call.After || !call.Completed.SequenceEqual(source.Steps(call.Before, call.After)) ||
            data.Length != 1 || data[0].Data.Length is not (1 or 2) || data[0].Data.Span[0] != call.CellFlags)
            throw new InvalidDataException("Saved Sky mode substituted residency or a changed CELL/child prefix for source execution.");
        if (record.Signature != "CELL" || !Convert.ToHexString(SHA256.HashData(record.ReadData())).Equals(call.CellSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Saved Sky mode's winning CELL payload changed.");
    }
    private void RestoreMode(FalloutSkyTransferSnapshot saved)
    {
        if (!Source.IsStandalone) return;
        var fields = saved.ModeFields!;
        _modeCall = fields.Call;
        _modeHistoricalSky = fields.Call?.Sky; _modeHistoricalProcess = fields.Call?.Process;
        foreach (var row in fields.Children)
        {
            if (row.Owner is not null)
            {
                _modeColdChildren.Add(row.Role);
                _modeChildren[row.Role] = new(row.Role, null, "source-Sky-mode-cold-actual-" + row.Role + "-republication-pending");
            }
            else _modeChildren[row.Role] = row;
        }
        _modeDeferredUnowned = fields.DeferredUnowned;
    }
}
