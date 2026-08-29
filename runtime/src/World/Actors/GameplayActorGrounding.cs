using Godot;
using OpenNV.Runtime.World.Streaming;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class GameplayActorGrounding : Node3D
{
    private const string GroundOffsetMetadata = "opennv_ground_offset_game_units";

    private RuntimeConfiguration _configuration = null!;
    private CellActiveSet _activeSet = null!;
    private IReadOnlyList<Space> _spaces = Array.Empty<Space>();
    private readonly HashSet<string> _groundedReferences =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Result> _results = [];

    internal IReadOnlyList<Result> Results => _results;

    internal static GameplayActorGrounding Install(
        Node parent,
        RuntimeConfiguration configuration,
        CellActiveSet activeSet,
        IReadOnlyList<Space> spaces)
    {
        var grounding = new GameplayActorGrounding
        {
            Name = "GameplayActorGrounding",
            _configuration = configuration,
            _activeSet = activeSet,
            _spaces = spaces,
        };
        parent.AddChild(grounding);
        return grounding;
    }

    internal static Vector3 ApplyGroundOffset(
        CellActorLoader.PlacedActor actor,
        Vector3 sourceCellPosition)
    {
        if (!actor.Placement.HasMeta(GroundOffsetMetadata))
            return sourceCellPosition;
        return sourceCellPosition + Vector3.Up *
            (float)actor.Placement.GetMeta(GroundOffsetMetadata).AsDouble();
    }

    internal void PreserveAuthoredFurnitureOccupancy(
        CellActorLoader.PlacedActor actor,
        Node3D furniture)
    {
        if (!_spaces.SelectMany(space => space.Actors).Any(value =>
                value.ReferenceFormId.Equals(
                    actor.ReferenceFormId,
                    StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                "Furniture occupant is absent from the actor-grounding spaces: " +
                actor.ReferenceFormId);
        if (!_groundedReferences.Add(actor.ReferenceFormId))
            return;
        actor.Placement.SetMeta(GroundOffsetMetadata, 0.0f);
        _results.Add(new Result(
            actor.ReferenceFormId,
            _spaces.Single(space => space.Actors.Any(value =>
                value.ReferenceFormId.Equals(
                    actor.ReferenceFormId,
                    StringComparison.OrdinalIgnoreCase))).CellFormId,
            actor.Placement.GlobalPosition,
            actor.Placement.GlobalPosition,
            0.0f,
            0.0f,
            actor.Placement.GlobalPosition,
            furniture.GetPath().ToString(),
            "owned-furn-initial-occupancy-preserves-authored-achr-transform"));
        GD.Print(
            $"OPENNV_GAMEPLAY_ACTOR_FURNITURE_OCCUPANCY " +
            $"reference={actor.ReferenceFormId} support={furniture.GetPath()}");
    }

    public override void _PhysicsProcess(double delta)
    {
        foreach (var space in _spaces.Where(space =>
                     _activeSet.ActiveCellFormIds.Contains(space.CellFormId)))
        {
            foreach (var actor in space.Actors.Where(actor =>
                         !_groundedReferences.Contains(actor.ReferenceFormId)))
            {
                var visualBounds = ActorModelSlice.PosedWorldBounds(
                    actor.Actor,
                    includeWeapons: false);
                var alignment = GalleryGroundContact.Align(
                    GetWorld3D().DirectSpaceState,
                    actor,
                    visualBounds,
                    _configuration,
                    space.CollisionMask,
                    space.CellRoot.GlobalPosition);
                var offsetGameUnits = -alignment.CorrectionGameUnits;
                actor.Placement.SetMeta(GroundOffsetMetadata, offsetGameUnits);
                _groundedReferences.Add(actor.ReferenceFormId);
                _results.Add(new Result(
                    actor.ReferenceFormId,
                    space.CellFormId,
                    alignment.RootBefore,
                    alignment.RootAfter,
                    alignment.CorrectionMeters,
                    alignment.CorrectionGameUnits,
                    alignment.GroundPosition,
                    alignment.ColliderPath,
                    alignment.Derivation));
                GD.Print(
                    $"OPENNV_GAMEPLAY_ACTOR_GROUNDED reference={actor.ReferenceFormId} " +
                    $"cell={space.CellFormId} correctionGameUnits=" +
                    $"{alignment.CorrectionGameUnits:F6} support={alignment.ColliderPath}");
            }
        }

        if (_groundedReferences.Count == _spaces.Sum(space => space.Actors.Count))
            SetPhysicsProcess(false);
    }

    internal readonly record struct Space(
        string CellFormId,
        Node3D CellRoot,
        uint CollisionMask,
        IReadOnlyList<CellActorLoader.PlacedActor> Actors);

    internal readonly record struct Result(
        string ReferenceFormId,
        string CellFormId,
        Vector3 RootBefore,
        Vector3 RootAfter,
        float CorrectionMeters,
        float CorrectionGameUnits,
        Vector3 GroundPosition,
        string ColliderPath,
        string Derivation);
}
