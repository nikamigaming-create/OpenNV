using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutDetectionEvents? _detection;
    private float _detectionSimulationSeconds;
    internal FalloutDetectionEvents Detection => _detection ??= NewDetectionOwner();
    internal FalloutDetectionEventsSnapshot? CaptureDetection() => _detection?.Capture();
    internal object? DetectionState => _detection?.State;
    internal object StoppedScriptFrames => _instances.Values.Where(instance => instance.ScriptStoppedFrame is not null ||
        instance.CompletedScriptContinuation is not null).Select(instance => new
        {
            reference = instance.Reference,
            error = instance.ScriptError,
            stopped = instance.ScriptStoppedFrame,
            completedContinuation = instance.CompletedScriptContinuation
        }).ToArray();
    internal void AdvanceDetection(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0 || !float.IsFinite(_detectionSimulationSeconds + seconds))
            throw new InvalidDataException("Detection simulation delta is invalid.");
        _detection?.Advance(seconds);
        _detectionSimulationSeconds += seconds;
    }
    internal void RestoreDetection(FalloutDetectionEventsSnapshot? snapshot)
    {
        if (snapshot is not null)
        {
            foreach (var instance in _instances.Values)
                foreach (var frame in new[] { instance.ScriptStoppedFrame, instance.CompletedScriptContinuation }.OfType<FalloutReferenceScriptStoppedFrame>())
                    if (frame.PreparedDetection is { } request && request.ObservedSeconds > snapshot.SimulationSeconds)
                        throw new InvalidDataException("Stopped source request belongs to a future detection clock.");
            Detection.Restore(snapshot); _detectionSimulationSeconds = snapshot.SimulationSeconds;
        }
    }

    private FalloutDetectionEvents NewDetectionOwner()
    {
        bool Actor(FalloutFormKey key)
        {
            if (key == records.RuntimeFormKey(0x14)) return true;
            if (!records.TryGetEffective(key, out var placed) || placed.IsDeleted || placed.Signature is not ("ACHR" or "ACRE")) return false;
            var basis = records.GetEffective(FalloutDialogueTopic.RequiredForm(placed, "NAME"));
            return !basis.IsDeleted && (placed.Signature == "ACHR" && basis.Signature == "NPC_" || placed.Signature == "ACRE" && basis.Signature == "CREA");
        }
        bool Reference(FalloutFormKey key) => key == records.RuntimeFormKey(0x14) ||
            records.TryGetEffective(key, out var value) && !value.IsDeleted && value.Signature is "REFR" or "ACHR" or "ACRE" or "PGRE" or "PMIS";
        bool Cell(FalloutFormKey key) => records.TryGetEffective(key, out var value) && !value.IsDeleted && value.Signature == "CELL";
        var bound = checked(records.EffectiveRecords("ACHR").Count() + records.EffectiveRecords("ACRE").Count() + 1);
        return new(() => FalloutGameSettingFloats.Read(records, "fDetectionEventExpireTime"),
            bound, Actor, Reference, Cell, _detectionSimulationSeconds);
    }

    internal FalloutReferencePlacement DetectionPlacement(FalloutFormKey reference,
        FalloutReferencePlacement player, float unitsToMetres) => SpatialPlacement(reference, player, unitsToMetres);
}
