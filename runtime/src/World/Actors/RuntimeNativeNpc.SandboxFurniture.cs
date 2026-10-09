using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private void EnterSandboxFurniture(FalloutFormKey reference)
    {
        _ = _aiWorld!.RequireSandboxPublication(reference);
        if (_seat is not null || _furnitureApproaching)
            throw new NotSupportedException("Sandbox furniture overlaps an actual source seat/entry child.");
        var placed = _aiCell!.References.SingleOrDefault(value => value.FormKey == reference) ??
            throw new NotSupportedException("Sandbox furniture is outside the actor's actual resident native CELL.");
        var furniture = _aiStack!.GetEffective(placed.Base);
        if (furniture.Signature != "FURN") throw new InvalidDataException("Sandbox furniture selection lost its source class.");
        if (FalloutFurnitureSource.ReadKind(furniture) != FalloutPlayerFurnitureKind.Sitting)
            throw new NotSupportedException("Sandbox furniture requires another source procedure kind.");
        _sandboxFurnitureAction = true;
        CancelIdle();
        // Current entry must use its actual native approach, source root-motion
        // KF and real seat reservation; initializing placement is never used.
        if (!TryBeginFurniturePackage(_aiPackage!, placed, furniture, initializing: false))
            throw new NotSupportedException("Sandbox furniture no longer has an unreserved actual source seat.");
    }

    private void AdvanceSandboxFurnitureRetirement()
    {
        if (!_sandboxFurnitureAction || !_sandboxNativeRetiring || _sandboxNativeRetired) return;
        if (_furnitureApproaching)
        {
            // No source entry pose was published. Retire current motion and its
            // actual reservation without moving or completing the actor.
            Combat!.StopPackageMotion();
            ClearFurniture(); _travelProgress?.Cancel();
            PlayLocomotion(false);
            _sandboxFurnitureAction = false; _sandboxNativeRetired = true;
            return;
        }
        if (_sitting is 2 or 4) return; // Actual enter/exit KF still owns the body.
        if (_sitting != 1 || _seat is null)
            throw new InvalidOperationException("Sandbox furniture lost its entered physical phase or seat.");
        CancelIdle();
        _furnitureOccupied = Transform;
        _sitting = 4;
        StartFurnitureAnimation();
    }

    private bool CompleteSandboxFurnitureExit()
    {
        if (!_sandboxFurnitureAction) return false;
        if (!_sandboxNativeRetiring)
            throw new InvalidOperationException("Sandbox furniture exit has no genuine requested child retirement.");
        _sandboxFurnitureAction = false; _sandboxNativeRetired = true;
        _pendingPackage = null; _aiPollRemaining = 0; _aiQuestRevision = -1;
        _baseAnimation = null;
        PlayLocomotion(false);
        // Keep the continuous PACK and its source IDLC/election/history. The
        // caller observes this actual KF return before changing/re-electing it.
        return true;
    }
}
