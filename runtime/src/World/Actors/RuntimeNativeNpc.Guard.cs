using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private FalloutGuardPackage? _guardPackage;
    private FalloutTravelProgress? _guardProgress;

    private void BeginGuard(FalloutPluginRecord package, bool initializing)
    {
        var source = FalloutGuardPackage.Read(package);
        var world = _aiWorld ?? throw new NotSupportedException("Guard has no reference-world owner.");
        var actor = Appearance.Reference!.Value;
        var retained = initializing && world.Get(actor).PackageMotion?.Package == package.FormKey
            ? world.Get(actor).PackageMotion!.Guard : null;
        var progress = retained ?? source.Start(_aiStack!, world, actor);
        source.Validate(_aiStack!, world, actor, progress);
        if (progress.Cell != _aiCell!.Cell.FormKey) throw new NotSupportedException("Guard needs its other-cell route owner.");
        _guardPackage = source; _guardProgress = progress; _aiPackage = package;
        _travelProgress?.Cancel();
        world.Get(actor).ProcedureCaptureBlocker = retained is null
            ? "Guard approach has no first native motion observation." : null;
        if (retained is null) _packageEvents!.Change(_packageIdleSource);
        else _packageEvents!.Restore(_packageIdleSource!, false);
    }

    private void AdvanceGuard(double delta)
    {
        if (_guardPackage is not { } source || Combat is null || _aiError is not null || Combat.OwnsPose ||
            !Combat.PackageMovementReady || _conversationTarget is not null) return;
        try { _guardProgress = Combat.AdvanceGuard(_aiPackage!, source, _guardProgress!, delta); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or IOException)
        {
            _guardProgress = Combat.PackageMotion?.Guard ?? _guardProgress;
            Combat.StopPackageMotion(); _aiError = error.Message; _failedPackage = _aiPackage?.FormKey;
            Godot.GD.PushError($"OPENNV_NATIVE_GUARD_DIVERGENCE reference={Appearance.Reference}: {_aiError}");
        }
    }
}
