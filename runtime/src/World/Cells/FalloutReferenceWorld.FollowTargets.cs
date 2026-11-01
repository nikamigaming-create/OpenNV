using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal float[]? CompletedFollowTargetPosition(FalloutFormKey reference)
    {
        if (reference == records.RuntimeFormKey(0x14)) return null;
        var state = Actor(reference);
        if (IsResident(reference) || state.QuerySpatialPlacement is not null || state.QueryCurrentPackage is not null ||
            !IsEnabled(reference) || state.Injury?.Dead == true || state.Unconscious || state.KnockedDown ||
            state.ScriptError is not null || state.ProcedureCaptureBlocker is not null ||
            state.PendingPackageSelection is not null || state.PendingSandboxStart is not null ||
            state.DeferredPackageContinuation is not null || state.Engagement is not null || state.HitReaction is not null ||
            state.AggroWarning is not null || state.HitKnockback is not null || state.Ragdoll is not null ||
            state.PackageBindingFailure is not null || state.SelectionFailure is not null ||
            state.SandboxAdmissionFailure is not null || state.SandboxMotionFailure is not null ||
            state.FurnitureContinuation is not null || state.DialogueContinuation is not null || state.PackageIdle is not null)
            return null;
        var position = FalloutFollowContinuation.CompletedTravelTarget(state.PackageAssignment, state.PackageMotion);
        if (position is null) return null;
        var source = records.GetEffective(state.PackageAssignment!.Package);
        if (FalloutPackageData.Read(source).Procedure != 6 ||
            !Convert.ToHexString(SHA256.HashData(source.ReadData())).Equals(state.PackageAssignment.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Retained Follow target differs from its winning completed Travel source.");
        return position;
    }
}
