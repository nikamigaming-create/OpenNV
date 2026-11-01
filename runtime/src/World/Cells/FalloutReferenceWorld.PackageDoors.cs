using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    // Native package door actions require an existing effective lock; they do
    // not manufacture a lock on an otherwise lockless teleport door. This owns
    // only the empty work set, without approximating lock/ownership/CELL writes.
    internal void RequirePackageDoorActions(FalloutFormKey actor, FalloutScriptPackage package, string kind)
    {
        var flags = kind switch
        {
            "POBA" => package.Flags & 0x48u,
            "POEA" or "POCA" => package.Flags & 0x90u,
            _ => throw new InvalidDataException("Package door actions have no typed lifecycle phase.")
        };
        if (flags == 0) return;
        _ = FalloutSleepPackage.Read(records.GetEffective(package.Form));
        var actorState = Get(actor);
        if (records.GetEffective(actorState.Base).Signature != "NPC_")
            throw new NotSupportedException("Sleep door actions require their resident humanoid package owner.");
        var cell = Placement(actor).Cell;
        if (!_placementSourceCells.TryGetValue(cell, out var scene))
            _placementSourceCells.Add(cell, scene = FalloutCellSceneReader.Read(records, cell));
        if ((scene.Cell.Flags & 1) == 0)
            throw new NotSupportedException("Exterior package door actions require their world-space traversal owner.");
        // Include the complete interior source set and runtime moved-in doors.
        // This is a conservative absence proof. A nonempty set is not declared
        // eligible without the native distance/ownership/teleport policy.
        var doors = scene.References.Where(reference => scene.BaseObjects[reference.Base].Signature == "DOOR")
            .Select(reference => reference.FormKey).Concat(_instances.Values
                .Where(instance => instance.Placement?.Cell == cell && records.GetEffective(instance.Base).Signature == "DOOR")
                .Select(instance => instance.Reference)).Distinct().ToArray();
        foreach (var door in doors)
        {
            if (Placement(door).Cell != cell) continue;
            if (EffectiveLock(door).State is not null)
                throw new NotSupportedException($"Package {package.Form} {kind} has potential door-lock work at {door}; native eligibility and connected-CELL effects remain unowned.");
        }
    }
}
