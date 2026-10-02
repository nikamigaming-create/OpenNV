using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private FalloutEditorTravelPackage? _editorTravel;
    private FalloutEditorTravelProgress? _editorTravelProgress;
    private Vector3? _editorTravelDestination;
    private string? _editorTravelStatus;
    private object? EditorTravelState => _editorTravel is null ? null : new
    {
        package = _editorTravel.Form.ToString(),
        progress = _editorTravelProgress,
        status = _editorTravelStatus,
        projectedEndpoint = _editorTravelDestination is { } point ? new[] { point.X, point.Y, point.Z } : null,
        owner = "source-editor-location-native-capsule-and-kf",
        unbound = new[] { "other-cell-route", "must-complete-reevaluation", "retail-timing" }
    };

    private void BeginEditorTravel(FalloutPluginRecord package, bool initializing)
    {
        var world = _aiWorld ?? throw new NotSupportedException("Editor travel has no shared reference owner.");
        var source = FalloutEditorTravelPackage.Read(package);
        var actor = Appearance.Reference!.Value;
        var restored = initializing && world.Get(actor).PackageMotion?.Package == package.FormKey
            ? world.Get(actor).PackageMotion!.EditorTravel : null;
        var progress = restored ?? source.Start(world, actor);
        source.Validate(world, actor, progress);
        if (progress.Cell != _aiCell!.Cell.FormKey)
            throw new NotSupportedException("Editor travel destination requires its other-cell route owner.");
        _editorTravel = source; _editorTravelProgress = progress; _editorTravelDestination = null;
        _editorTravelStatus = restored is null ? "pending-native-observation" : "restored";
        _aiPackage = package; _travelProgress?.Cancel();
        world.Get(actor).ProcedureCaptureBlocker = restored is null
            ? "Editor travel has started but its native motion snapshot is not initialized." : null;
        if (restored is null) _packageEvents!.Change(_packageIdleSource);
        else _packageEvents!.Restore(_packageIdleSource!, restored.Complete);
    }

    private void AdvanceEditorTravel(double delta)
    {
        if (_editorTravel is not { } source || Combat is null || _aiError is not null || Combat.OwnsPose ||
            !Combat.PackageMovementReady || _conversationTarget is not null) return;
        try
        {
            var progress = _editorTravelProgress!;
            var authored = GetParent<Node3D>().ToGlobal(new Vector3(progress.Location[0], progress.Location[2], -progress.Location[1]) * Skeleton.UnitsToMetres);
            var destination = _editorTravelDestination ??= Combat.ProjectPackageDestination(authored);
            var radius = Math.Max(source.Radius * Skeleton.UnitsToMetres, .25f);
            var reached = IsOnFloor() && GlobalPosition.DistanceTo(destination) <= radius + .1f;
            Combat.AdvancePackageMotion(_aiPackage!, progress.Complete ? GlobalPosition : destination,
                progress.Complete ? 0 : radius, source.Running, delta, source.WeaponDrawn, requireArrivalHeight: true);
            var complete = reached && !progress.Complete;
            if (complete) progress = progress with { Complete = true };
            _editorTravelProgress = progress; Combat.SetEditorTravelProgress(progress);
            _aiWorld!.Get(Appearance.Reference!.Value).ProcedureCaptureBlocker = null;
            _editorTravelStatus = progress.Complete ? "complete" : "travelling";
            // Publish arrival before source results can select a new package.
            if (complete) _packageEvents!.Complete();
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or IOException)
        {
            Combat.StopPackageMotion(); _aiError = error.Message; _failedPackage = _aiPackage?.FormKey;
            GD.PushError($"OPENNV_NATIVE_EDITOR_TRAVEL_DIVERGENCE reference={Appearance.Reference}: {_aiError}");
        }
    }
}
