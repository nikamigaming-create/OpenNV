using Godot;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed record SourceGamebryoFurniture(
    string FurnitureReferenceFormId,
    int MarkerId,
    Transform3D FurnitureTransform,
    Vector3 MarkerOffsetCellUnits,
    Quaternion MarkerRotationGodot,
    Vector3 ActorPlacementOffsetCellUnits,
    Quaternion ActorHeadingDeltaGodot,
    Vector3 ActorScale,
    SourceActorAnimation Loop,
    SourceActorAnimation? PackageLoop,
    SourceActorAnimation Exit,
    Vector3 ExitRootDisplacementGodotGameUnits);

internal sealed record GamebryoFurnitureState(
    string FurnitureReferenceFormId,
    int MarkerId,
    string Phase,
    double AnimationPositionSeconds,
    double? PackagePositionSeconds,
    Transform3D ActorTransform);

internal sealed class GamebryoFurnitureSession
{
    private const string OccupiedPhase = "occupied";
    private const string ExitingPhase = "exiting";
    private const string ReleasedPhase = "released";

    private readonly SourceGamebryoFurniture _source;
    private ActorAnimationPlayback? _exitPlayback;

    private GamebryoFurnitureSession(
        SourceGamebryoFurniture source,
        SourcePackagePlacement placement,
        double loopPositionSeconds,
        double? packagePositionSeconds)
    {
        _source = source;
        Placement = placement;
        AnimationPositionSeconds = loopPositionSeconds;
        PackagePositionSeconds = packagePositionSeconds;
    }

    internal SourcePackagePlacement Placement { get; }
    internal string Phase { get; private set; } = OccupiedPhase;
    internal double AnimationPositionSeconds { get; private set; }
    internal double? PackagePositionSeconds { get; private set; }
    internal ActorAnimationPlayback? ExitPlayback => _exitPlayback;

    internal static GamebryoFurnitureSession Occupy(
        CellActorLoader.PlacedActor actor,
        SourceGamebryoFurniture source,
        double loopPositionSeconds,
        double? packagePositionSeconds)
    {
        Validate(source);
        RequirePhase(source.Loop, loopPositionSeconds, allowStop: false);
        _ = ActorAnimationPlayback.Resolve(actor.Actor, source.Loop);
        RequirePackagePhase(source, packagePositionSeconds);
        if (source.PackageLoop is not null)
            _ = ActorAnimationPlayback.Resolve(actor.Actor, source.PackageLoop);
        var placement = PlacementFromSource(source);
        GamebryoPackagePlacement.Publish(actor, placement);
        var session = new GamebryoFurnitureSession(
            source,
            placement,
            loopPositionSeconds,
            packagePositionSeconds);
        session.Publish(actor.Placement);
        return session;
    }

    internal static SourcePackagePlacement PlacementFromSource(
        SourceGamebryoFurniture source)
    {
        Validate(source);
        return GamebryoPackagePlacement.FromFurnitureMarker(
            source.FurnitureReferenceFormId,
            source.FurnitureTransform,
            source.MarkerOffsetCellUnits,
            source.MarkerRotationGodot,
            source.ActorPlacementOffsetCellUnits,
            source.ActorHeadingDeltaGodot,
            source.ActorScale);
    }

    internal void PublishLoopPhase(
        CellActorLoader.PlacedActor actor,
        double positionSeconds,
        double? packagePositionSeconds)
    {
        if (Phase != OccupiedPhase ||
            !actor.Placement.Transform.IsEqualApprox(Placement.SourceTransform))
            throw new InvalidOperationException(
                "Source furniture loop advanced outside its exact occupancy root.");
        RequirePhase(_source.Loop, positionSeconds, allowStop: true);
        RequirePackagePhase(_source, packagePositionSeconds);
        AnimationPositionSeconds = positionSeconds;
        PackagePositionSeconds = packagePositionSeconds;
        Publish(actor.Placement);
    }

    internal ActorAnimationPlayback BeginExit(CellActorLoader.PlacedActor actor)
    {
        if (Phase != OccupiedPhase || _exitPlayback is not null ||
            !actor.Placement.Transform.IsEqualApprox(Placement.SourceTransform))
            throw new InvalidOperationException(
                "Source furniture exit began from an invalid occupancy root.");
        _exitPlayback = ActorAnimationPlayback.Start(
            actor.Actor,
            _source.Exit,
            _source.Exit.StartSeconds);
        Phase = ExitingPhase;
        AnimationPositionSeconds = _exitPlayback.PositionSeconds;
        PackagePositionSeconds = null;
        Publish(actor.Placement);
        return _exitPlayback;
    }

    internal bool AdvanceExit(CellActorLoader.PlacedActor actor, double deltaSeconds)
    {
        if (Phase != ExitingPhase || _exitPlayback is null)
            throw new InvalidOperationException(
                "Source furniture exit advanced without playback.");
        _exitPlayback.Advance(deltaSeconds);
        AnimationPositionSeconds = _exitPlayback.PositionSeconds;
        Publish(actor.Placement);
        return _exitPlayback.Terminal;
    }

    internal PackageRootTransfer CompleteExit(CellActorLoader.PlacedActor actor)
    {
        if (Phase != ExitingPhase || _exitPlayback is not { Terminal: true })
            throw new InvalidOperationException(
                "Source furniture exit completed before its terminal phase.");
        var transfer = GamebryoPackagePlacement.TransferRoot(
            actor,
            _source.ExitRootDisplacementGodotGameUnits);
        Phase = ReleasedPhase;
        Publish(actor.Placement);
        return transfer;
    }

    internal GamebryoFurnitureState CaptureState(Node3D actorPlacement)
    {
        if (!actorPlacement.Transform.IsFinite())
            throw new InvalidOperationException(
                "Source furniture actor transform is invalid.");
        return new GamebryoFurnitureState(
            _source.FurnitureReferenceFormId,
            _source.MarkerId,
            Phase,
            AnimationPositionSeconds,
            PackagePositionSeconds,
            actorPlacement.Transform);
    }

    private void Publish(Node3D actorPlacement)
    {
        actorPlacement.SetMeta(
            "opennv_furniture_reference_form_id",
            _source.FurnitureReferenceFormId);
        actorPlacement.SetMeta("opennv_furniture_marker_id", _source.MarkerId);
        actorPlacement.SetMeta("opennv_furniture_phase", Phase);
        actorPlacement.SetMeta(
            "opennv_furniture_animation_position_seconds",
            AnimationPositionSeconds);
        if (PackagePositionSeconds is { } packagePositionSeconds)
            actorPlacement.SetMeta(
                "opennv_furniture_package_position_seconds",
                packagePositionSeconds);
        else if (actorPlacement.HasMeta("opennv_furniture_package_position_seconds"))
            actorPlacement.RemoveMeta("opennv_furniture_package_position_seconds");
    }

    private static void Validate(SourceGamebryoFurniture source)
    {
        if (string.IsNullOrWhiteSpace(source.FurnitureReferenceFormId) ||
            source.MarkerId < 0 ||
            !source.FurnitureTransform.IsFinite() ||
            source.FurnitureTransform.Basis.Determinant() <= 0.0f ||
            !source.MarkerOffsetCellUnits.IsFinite() ||
            !source.MarkerRotationGodot.IsNormalized() ||
            !source.ActorPlacementOffsetCellUnits.IsFinite() ||
            !source.ActorHeadingDeltaGodot.IsNormalized() ||
            !source.ActorScale.IsFinite() ||
            source.ActorScale.X <= 0.0f ||
            source.ActorScale.Y <= 0.0f ||
            source.ActorScale.Z <= 0.0f ||
            !source.ExitRootDisplacementGodotGameUnits.IsFinite() ||
            !ValidAnimation(source.Loop) ||
            source.PackageLoop is not null && !ValidAnimation(source.PackageLoop) ||
            !ValidAnimation(source.Exit) ||
            source.Loop.CycleType != ActorAnimationPlayback.LoopCycleType ||
            source.Exit.CycleType != ActorAnimationPlayback.ClampCycleType)
            throw new InvalidOperationException(
                "Source furniture contract is invalid.");
    }

    private static void RequirePackagePhase(
        SourceGamebryoFurniture source,
        double? positionSeconds)
    {
        if ((source.PackageLoop is null) != (positionSeconds is null))
            throw new InvalidOperationException(
                "Source furniture package phase is incomplete.");
        if (source.PackageLoop is not null && positionSeconds is { } position)
            RequirePhase(source.PackageLoop, position, allowStop: true);
    }

    private static bool ValidAnimation(SourceActorAnimation animation) =>
        !string.IsNullOrWhiteSpace(animation.LogicalPath) &&
        !string.IsNullOrWhiteSpace(animation.Sha256) &&
        !string.IsNullOrWhiteSpace(animation.SequenceName) &&
        !string.IsNullOrWhiteSpace(animation.AccumulationRootTranslationDisposition) &&
        float.IsFinite(animation.StartSeconds) &&
        float.IsFinite(animation.StopSeconds) &&
        animation.StopSeconds > animation.StartSeconds;

    private static void RequirePhase(
        SourceActorAnimation animation,
        double positionSeconds,
        bool allowStop)
    {
        if (!double.IsFinite(positionSeconds) ||
            positionSeconds < animation.StartSeconds ||
            (allowStop
                ? positionSeconds > animation.StopSeconds
                : positionSeconds >= animation.StopSeconds))
            throw new InvalidOperationException(
                "Source furniture animation phase is invalid.");
    }
}
