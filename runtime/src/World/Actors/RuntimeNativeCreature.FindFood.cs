using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Content.Definitions;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeCreature
{
    private const string FindFoodCaptureBlocker = "Find Food awaits its first native motion observation.";
    private const string FindFoodFailureBlocker = "Find Food continuation is unbound: ";
    private FalloutFindFoodPackage? _findFoodPackage;
    private FalloutFindFoodProgress? _findFoodProgress;

    private int CurrentFindFoodProcedure => (int)(_findFoodProgress?.Phase switch
    {
        FalloutFindFoodPhase.Travel => FalloutAiProcedure.Travel,
        FalloutFindFoodPhase.Acquire => FalloutAiProcedure.Acquire,
        FalloutFindFoodPhase.Wander => FalloutAiProcedure.Wander,
        FalloutFindFoodPhase.Done => FalloutAiProcedure.Done,
        _ => throw new NotSupportedException("Find Food has no observed source procedure phase.")
    });

    private void BeginFindFood(FalloutPluginRecord record, FalloutFindFoodPackage source, bool restoring)
    {
        var state = _aiState!;
        var actor = Appearance.Reference!.Value;
        if (restoring)
        {
            _ = _aiWorld!.ActorPackageStartLocation(actor) ??
                throw new InvalidDataException("Restored Find Food has no retained source start location.");
            _findFoodProgress = state.PackageMotion!.FindFood!;
        }
        else
        {
            if (!IsInsideTree()) throw new NotSupportedException("Find Food needs its live source start pose.");
            if (!_initialPackageSelected && state.PackageAssignment is { Done: false } assignment && assignment.Package == record.FormKey &&
                !IsFirstResidentProcedureAdmission(record))
                throw new InvalidDataException("Saved active Find Food has no retained procedure continuation.");
            _aiWorld!.CaptureActorPackageStartLocation(actor, Skeleton.UnitsToMetres);
            _findFoodProgress = new(source.Declaration.Location(_aiRecords!, _aiWorld, actor).Cell);
        }
        source.Validate(_aiRecords!, _aiWorld!, actor, _findFoodProgress);
        _findFoodPackage = source;
    }

    private Vector3 FindFoodCenter(FalloutFindFoodPackage source)
    {
        var location = source.Declaration.Location(_aiRecords!, _aiWorld!, Appearance.Reference!.Value);
        return GetParent<Node3D>().ToGlobal(new Vector3(location.Position[0], location.Position[2], -location.Position[1]) * Skeleton.UnitsToMetres);
    }

    private void AdvanceFindFood(double delta)
    {
        var source = _findFoodPackage!;
        var progress = _findFoodProgress!;
        source.Validate(_aiRecords!, _aiWorld!, Appearance.Reference!.Value, progress);
        if (!_aiWorld!.SameTravelRegion(_aiWorld.Placement(Appearance.Reference!.Value).Cell, progress.Cell))
            throw new NotSupportedException("Find Food requires its resident retained source region.");
        var center = FindFoodCenter(source);
        var radius = source.Declaration.Source.LocationRadius * Skeleton.UnitsToMetres;
        if (progress.Phase == FalloutFindFoodPhase.Travel)
        {
            var arrived = Combat!.AdvancePackageMotion(_aiPackage!, Combat.ProjectPackageDestination(center), radius,
                source.Running, delta, source.WeaponDrawn, requireArrivalHeight: true,
                navigationSha256: _aiWorld.TravelNavigation(_aiWorld.Placement(Appearance.Reference.Value).Cell).SourceSha256);
            RetainFindFood(arrived ? progress with { Phase = FalloutFindFoodPhase.Acquire } : progress);
            return;
        }
        if (progress.Phase == FalloutFindFoodPhase.Wander)
        {
            if (GlobalPosition.DistanceTo(center) > radius)
            {
                Combat!.RetirePackageMotionNavigation(source.Form);
                RetainFindFood(progress with { Phase = FalloutFindFoodPhase.Travel });
                return;
            }
            var retry = progress.AdvanceWander(delta, Combat!.FindFoodWanderCanRetry);
            progress = retry.Progress;
            if (retry.Retry)
            {
                Combat.RetirePackageMotionNavigation(source.Form);
                RetainFindFood(progress);
                return;
            }
            Combat.AdvanceFindFoodWander(_aiPackage!, source, progress, delta);
            RetainFindFood(progress);
            return;
        }
        Combat!.AdvancePackageMotion(_aiPackage!, GlobalPosition, 0, false, delta, source.WeaponDrawn);
        RetainFindFood(progress);
        if (progress.Phase == FalloutFindFoodPhase.Done) return;
        if (Combat.FindFoodQuotaSatisfied(source))
        {
            RetainFindFood(progress with { Phase = FalloutFindFoodPhase.Done });
            _packageEvents!.Complete();
            _evaluateRequested = true;
            return;
        }
        Combat.RequireEmptyFindFoodSearch(source);
        RetainFindFood(progress with { Phase = FalloutFindFoodPhase.Wander });
    }

    private void RetainFindFood(FalloutFindFoodProgress progress)
    {
        _findFoodProgress = progress;
        Combat!.SetFindFoodProgress(progress);
        if (_aiState!.ProcedureCaptureBlocker == FindFoodCaptureBlocker ||
            _aiState.ProcedureCaptureBlocker?.StartsWith(FindFoodFailureBlocker, StringComparison.Ordinal) == true)
            _aiState.ProcedureCaptureBlocker = null;
    }
}
