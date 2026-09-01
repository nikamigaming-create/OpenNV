using Godot;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Streaming;


using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Interactions;

namespace OpenNV.Runtime.World.Portals;

internal sealed class CellPortalTravel
{
    private const float MinimumFacingDot = 0.95f;

    private readonly GameplaySession _session;
    private readonly CellActiveSet _activeSet;
    private readonly CellEnvironmentSet? _environmentSet;
    private readonly List<Passage> _passages;
    private readonly Action<string>? _materializeAdjacent;
    private readonly List<Transition> _transitions = new();

    internal CellPortalTravel(
        IEnumerable<CellSceneLoader.PortalLink> links,
        GameplaySession session,
        CellActiveSet activeSet,
        CellEnvironmentSet? environmentSet,
        Action<string>? materializeAdjacent = null)
    {
        _session = session;
        _activeSet = activeSet;
        _environmentSet = environmentSet;
        _passages = links.Select(BuildPassage).ToList();
        _materializeAdjacent = materializeAdjacent;
    }

    internal void AddLink(CellSceneLoader.PortalLink link) =>
        _passages.Add(BuildPassage(link));

    private static Passage BuildPassage(CellSceneLoader.PortalLink link) => new(
            Endpoint.Create(
                link.FromCellFormId,
                link.FromCellEditorId,
                link.FromRoot,
                link.FromOriginGameUnits,
                link.FromCollisionLayer,
                (link.FromFrame.From + link.FromFrame.To) / 2.0f,
                link.FromDoor),
            Endpoint.Create(
                link.ToCellFormId,
                link.ToCellEditorId,
                link.ToRoot,
                link.ToOriginGameUnits,
                link.ToCollisionLayer,
                (link.ToFrame.From + link.ToFrame.To) / 2.0f,
                link.ToDoor));

    internal IReadOnlyList<Transition> Transitions => _transitions;

    internal bool TryActivate(DoorInstance collidedDoor, CellPlayer player)
    {
        _materializeAdjacent?.Invoke(_session.ActiveCellFormId);
        foreach (var passage in _passages.Where(value =>
                     value.From.Door == collidedDoor || value.To.Door == collidedDoor))
        {
            var source = passage.SourceFor(_session.ActiveCellFormId);
            if (source is null)
                continue;
            return TravelThrough(passage, source.Value, player);
        }
        return false;
    }

    internal bool TryActivateFacing(
        Node3D aimSource,
        CellPlayer player,
        float maximumDistance,
        out string activatedDoorFormId)
    {
        activatedDoorFormId = "none";
        _materializeAdjacent?.Invoke(_session.ActiveCellFormId);
        var forward = -aimSource.GlobalBasis.Z.Normalized();
        (Passage Passage, Endpoint Source)? match = null;
        foreach (var passage in _passages)
        {
            var source = passage.SourceFor(_session.ActiveCellFormId);
            if (source is null)
                continue;
            var offset = source.Value.InteractionCenter - aimSource.GlobalPosition;
            var distance = offset.Length();
            if (distance <= maximumDistance &&
                distance > 0.0f &&
                forward.Dot(offset / distance) >= MinimumFacingDot)
            {
                if (match is not null)
                    return false;
                match = (passage, source.Value);
            }
        }
        if (match is null)
            return false;
        activatedDoorFormId = match.Value.Source.Door.ReferenceFormId;
        return TravelThrough(match.Value.Passage, match.Value.Source, player);
    }

    private bool TravelThrough(Passage passage, Endpoint source, CellPlayer player)
    {
        var target = passage.Other(source);
        var destination = source.Door.Destination
            ?? throw new InvalidOperationException(
                $"Portal XTEL destination is missing: {source.Door.ReferenceFormId}");
        source.Door.SetOpen(true);
        _activeSet.Activate(target.CellFormId);
        _environmentSet?.Activate(target.CellFormId);
        player.CollisionMask = target.CollisionLayer;
        player.ApplyPortalArrival(
            target.Root,
            target.OriginGameUnits,
            destination);
        _session.CrossPortal(source.CellFormId, target.CellFormId, source.Door);
        _transitions.Add(new Transition(
            source.CellFormId,
            target.CellFormId,
            source.Door.ReferenceFormId,
            target.Door.ReferenceFormId,
            player.GlobalPosition));
        _materializeAdjacent?.Invoke(target.CellFormId);
        return true;
    }

    private readonly record struct Passage(Endpoint From, Endpoint To)
    {
        internal Endpoint? SourceFor(string activeCellFormId) =>
            From.CellFormId.Equals(activeCellFormId, StringComparison.OrdinalIgnoreCase)
                ? From
                : To.CellFormId.Equals(activeCellFormId, StringComparison.OrdinalIgnoreCase)
                    ? To
                    : null;

        internal Endpoint Other(Endpoint source) => source.Equals(From) ? To : From;
    }

    private readonly record struct Endpoint(
        string CellFormId,
        string CellEditorId,
        Node3D Root,
        Vector3 OriginGameUnits,
        uint CollisionLayer,
        Vector3 InteractionCenter,
        DoorInstance Door)
    {
        internal static Endpoint Create(
            string cellFormId,
            string cellEditorId,
            Node3D root,
            Vector3 originGameUnits,
            uint collisionLayer,
            Vector3 interactionCenter,
            DoorInstance door) => new(
                cellFormId,
                cellEditorId,
                root,
                originGameUnits,
                collisionLayer,
                interactionCenter,
                door);
    }

    internal readonly record struct Transition(
        string FromCellFormId,
        string ToCellFormId,
        string FromDoorReferenceFormId,
        string ToDoorReferenceFormId,
        Vector3 ArrivalPosition);
}
