using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeCreature
{
    private FalloutSandboxPackage? _sandboxSource;
    private FalloutSandboxState? _sandbox;

    private void BeginSandbox(FalloutPluginRecord record, bool restored)
    {
        var source = FalloutSandboxPackage.Read(record);
        var saved = restored ? _aiState!.PackageMotion?.Sandbox : null;
        _sandboxSource = source; _sandbox = null;
        if (saved is not null)
        {
            saved.Validate();
            var area = source.LocationType == 2 ? saved.Area : source.Resolve(_aiRecords!, _aiWorld!, Appearance.Reference!.Value);
            _sandbox = new(source, Hash(record.ReadData()), area, _aiState!.SoundRandom.NextBounded, saved);
        }
        else Combat!.BeginSandboxObservation();
    }

    private void AdvanceSandbox(double seconds)
    {
        var source = _sandboxSource!;
        if (_sandbox is null)
        {
            var area = source.Resolve(_aiRecords!, _aiWorld!, Appearance.Reference!.Value);
            if (source.LocationType == 2)
            {
                var local = GetParent<Node3D>().ToLocal(GlobalPosition) / Skeleton.UnitsToMetres;
                area = area with { Center = [local.X, -local.Z, local.Y] };
            }
            _sandbox = new(source, Hash(_aiRecords!.GetEffective(source.Form).ReadData()), area, _aiState!.SoundRandom.NextBounded);
        }
        try { Combat!.AdvanceSandbox(_aiPackage!, source, _sandbox, seconds); }
        catch (Exception error)
        {
            _sandbox.RetainFailure(error);
            _aiState!.ProcedureCaptureBlocker ??= "Sandbox native suffix failed: " + error.Message;
            throw;
        }
    }

    private FalloutActorPackageMotion CaptureSandboxMotion()
    {
        if (_sandbox is null || _aiState!.PendingPackageChoice is not null || _aiState.ScriptPackage?.Pending == true ||
            _packageEvents is not { Error: null, Done: false } lifecycle)
            throw new NotSupportedException("Sandbox has no returned native area owner or has an entered package election/event.");
        return Combat!.CaptureSandboxMotion(_sandbox, new(_packageClock, _aiScheduleTime, _aiQuestRevision,
            null, _evaluateRequested, Activity.Capture(), lifecycle.Revision, lifecycle.LastEvent, lifecycle.LastPackage));
    }

    private void RestoreSandboxLifecycleBeforeSelection()
    {
        if (_aiState is not { PackageMotion: { Sandbox: { } saved } motion, PackageAssignment: { } assignment } state ||
            motion.Package != assignment.Package) return;
        if (state.ScriptPackage?.Pending == true || state.PendingPackageChoice is not null || assignment.Done)
            throw new NotSupportedException("Cold continuous Sandbox has a different election or completed lifecycle.");
        _ = FalloutSandboxPackage.Read(_aiRecords!.GetEffective(assignment.Package));
        saved.Validate();
        if (_packageEvents!.Active is null) assignment.Bind(_aiRecords, _packageEvents);
        if (_packageEvents.Active?.Form != assignment.Package || _packageEvents.Done)
            throw new InvalidDataException("Cold Sandbox differs from its bound original package lifecycle.");
        (saved.Election ?? throw new InvalidDataException("Cold Sandbox lost its actual election clock.")).RestoreHistory(_packageEvents);
    }

    private void RestoreSandboxElection(FalloutSandboxSnapshot saved, double elapsed)
    {
        var election = saved.Election ?? throw new InvalidDataException("Cold Sandbox has no election continuation.");
        Activity.Restore(election.Activity);
        _packageClock = election.PollRemaining - elapsed; _aiScheduleTime = election.ScheduleTime;
        _aiQuestRevision = election.QuestRevision; _evaluateRequested = election.EvaluateRequested;
    }

    private bool ClearSandbox()
    {
        if (_sandboxSource is null) return true;
        if (!Combat!.RetireSandbox(_sandbox)) return false;
        _sandboxSource = null; _sandbox = null;
        return true;
    }
}
