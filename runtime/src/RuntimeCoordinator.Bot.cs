using System.Globalization;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Diagnostics.Parity;
using OpenNV.Runtime.Gameplay.Bots;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private CellNavigationGraph? _botNavigation;
    private string? _botNavigationIdentity;
    private Node3D? _botBoundsOwner;
    private Aabb _botLocalBounds;
    private RuntimeSimulatorBotInput? _botSimulatorInput;
    private readonly Dictionary<FalloutFormKey, Vector3> _botAuthoredDestinations = [];
    private readonly Dictionary<Vector3, ulong> _botBlockedPortals = [];
    private readonly BotInteractionEvidence _botInteractions = new();
    private readonly Dictionary<FalloutFormKey, FalloutFormKey> _botPortalDestinations = [];

    private void BeginNativeBotActivation(FalloutFormKey reference)
    {
        try
        {
            var node = _nativeReferencePresentation?.Nodes.GetValueOrDefault(reference);
            if (node?.GetChildren().OfType<RuntimeNativeDoorPortal>().SingleOrDefault() is { } portal)
                _botPortalDestinations[reference] = portal.DestinationCell;
            _botInteractions.Begin(reference.ToString(), NativeBotInteractionSnapshot(reference));
        }
        catch (Exception error)
        {
            _botInteractions.Forget(reference.ToString());
            GD.PushError($"OPENNV_BOT_INTERACTION_OBSERVATION_UNBOUND reference={reference}: {error.Message}");
        }
    }

    private void EndNativeBotActivation(FalloutFormKey reference, bool successful)
    {
        try { _botInteractions.End(reference.ToString(), NativeBotInteractionSnapshot(reference), successful); }
        catch (Exception error)
        {
            _botInteractions.Forget(reference.ToString());
            GD.PushError($"OPENNV_BOT_INTERACTION_OBSERVATION_UNBOUND reference={reference}: {error.Message}");
        }
    }

    private BotInteractionSnapshot NativeBotInteractionSnapshot(FalloutFormKey reference)
    {
        var state = _nativeReferences!.Get(reference);
        var target = JsonSerializer.Serialize(new { state.Taken, state.DoorOpen, state.Deleted, state.Destroyed });
        string? outcome = _nativeContainerLayer is not null && _nativeContainerReference == reference ? "container" :
            _nativeOpeningStageDriver?.PresentedConversationSpeaker == reference ? "conversation" :
            _nativePlayer?.CurrentFurniture == reference ? "furniture" :
            _botPortalDestinations.TryGetValue(reference, out var destination) && _nativeActiveCell?.Cell.FormKey == destination &&
                !_nativeDoorLoading && _nativePlayer?.CollisionResident == true ? "portal:" + destination : null;
        var portal = _nativeReferencePresentation?.Nodes.GetValueOrDefault(reference)?.GetChildren()
            .OfType<RuntimeNativeDoorPortal>().SingleOrDefault();
        var requested = _nativeOpeningStageDriver?.PendingConversationSpeaker == reference ? "conversation" :
            portal is { ActivationRequests: > 0 } ? "portal:" + portal.DestinationCell : null;
        return new(target, outcome, _nativeOpeningStageDriver?.ActiveMenus().Order().ToArray() ?? [], requested,
            requested?.StartsWith("portal:", StringComparison.Ordinal) == true ? portal!.ActivationRequests : 0);
    }

    private bool ApplyNativeBotSimulatorInput(SteeringIntent intent, bool activate)
    {
        if (_nativeXr is null) return false;
        // An unsupported physical XR goal reports its error without injecting
        // desktop keys into a headset session during input release.
        if (RuntimeSimulatorBotInput.DirectoryPath is null) return true;
        _botSimulatorInput ??= new(_nativeXr);
        _botSimulatorInput.Submit(intent, activate);
        return true;
    }

    private BotObservation ObserveNativeBot(string identity)
    {
        var player = _nativePlayer ?? throw new InvalidOperationException("Player is not active.");
        if (_nativeXr is not null && RuntimeSimulatorBotInput.DirectoryPath is null)
            throw new NotSupportedException("Physical headset bot control is unbound; use the simulator input adapter.");
        var separator = identity.LastIndexOf(':');
        if (separator <= 0 || !uint.TryParse(identity.AsSpan(separator + 1), NumberStyles.HexNumber,
            CultureInfo.InvariantCulture, out var objectId)) throw new ArgumentException("Expected plugin:hex source reference.");
        var key = new FalloutFormKey(identity[..separator], objectId);
        var world = _nativeReferences ?? throw new InvalidOperationException("Reference world is not active.");
        var referenceState = world.Get(key);
        var node = _nativeReferencePresentation?.Nodes.GetValueOrDefault(key);
        var resident = node is not null && GodotObject.IsInstanceValid(node) && node.IsInsideTree() && node.IsVisibleInTree();
        var target = resident ? node!.GlobalPosition : Vector3.Zero;
        var travelReady = false;
        if (!resident && _nativeActiveCell?.Cell.Worldspace is { } worldspace &&
            _nativePluginStack!.GetEffective(key).Signature == "REFR" &&
            FalloutCellSceneReader.ParentWorldspace(_nativePluginStack.GetEffective(referenceState.Cell)) == worldspace)
        {
            if (!_botAuthoredDestinations.TryGetValue(key, out target))
            {
                var placement = FalloutCellSceneReader.Read(_nativePluginStack, referenceState.Cell).References.Single(value => value.FormKey == key);
                target = new Vector3(placement.Position[0], placement.Position[2], -placement.Position[1]) * _configuration.World.GameUnitsToMeters;
                _botAuthoredDestinations.Add(key, target);
            }
            travelReady = player.CollisionResident;
        }
        var aim = target;
        if (resident)
        {
            if (RuntimeNativeActorCombat.Find(node) is { Dead: true } combat)
            {
                var points = combat.CorpseAimPoints.ToArray();
                if (points.Length == 0) throw new NotSupportedException("Corpse has no live physical pose for aiming.");
                aim = points[0];
                using var query = PhysicsRayQueryParameters3D.Create(player.Camera.GlobalPosition, aim, player.CollisionMask | player.CollisionLayer);
                query.CollideWithAreas = true;
                query.HitBackFaces = false;
                query.Exclude = new Godot.Collections.Array<Rid>(player.CombatCollisionRids);
                foreach (var point in points)
                {
                    query.To = point;
                    var hit = player.GetWorld3D().DirectSpaceState.IntersectRay(query);
                    if (!hit.TryGetValue("collider", out var value) || value.AsGodotObject() is not Node contact ||
                        _nativeReferenceEvents?.AimedReference(contact)?.FormKey != key) continue;
                    aim = point; break;
                }
            }
            else if (node is RuntimeNativeNpc actor)
                aim = actor.Skeleton.Node.GlobalTransform * actor.Skeleton.Node.GetBoneGlobalPose(actor.Skeleton.BoneIndex("Bip01 Head")).Origin;
            else
            {
                if (_botBoundsOwner != node)
                {
                    var meshes = node!.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>().ToArray();
                    if (meshes.Length == 0) throw new NotSupportedException("Target has no observed geometry for aiming.");
                    var inverse = node.GlobalTransform.AffineInverse();
                    _botLocalBounds = meshes.Select(mesh => (inverse * mesh.GlobalTransform) * mesh.GetAabb()).Aggregate((a, b) => a.Merge(b));
                    _botBoundsOwner = node;
                }
                aim = node!.GlobalTransform * _botLocalBounds.GetCenter();
            }
        }
        var aimed = player.AimedObject() is { } collider ? _nativeReferenceEvents?.AimedReference(collider)?.FormKey.ToString() : null;
        var interaction = _botInteractions.Observe(key.ToString(), NativeBotInteractionSnapshot(key)).ToString(CultureInfo.InvariantCulture);
        static System.Numerics.Vector3 Numeric(Vector3 value) => new(value.X, value.Y, value.Z);
        return new(_nativeActiveCell!.Cell.FormKey.ToString(), Numeric(player.GlobalPosition), Numeric(player.Camera.GlobalPosition),
            Numeric(-player.Camera.GlobalBasis.Z), Numeric(target), Numeric(aim), aimed, GetTree().Paused || player.ModalInput || _nativeDoorLoading,
            player.GetMeta("opennv_source_movement_enabled", false).AsBool() && !player.FurnitureActive,
            player.GetMeta("opennv_source_looking_enabled", false).AsBool(), resident && player.CollisionResident,
            player.BlockingShape, interaction, travelReady);
    }

    private BotNavigationRoute FindNativeNavigationRoute(System.Numerics.Vector3 start, System.Numerics.Vector3 end, float projectionRadius)
        => FindNativeNavigationRoute(start, end, true, projectionRadius);

    private IReadOnlyList<System.Numerics.Vector3> FindNativeNavigationRoute(System.Numerics.Vector3 start, System.Numerics.Vector3 end, bool refinePlayer)
        => FindNativeNavigationRoute(start, end, refinePlayer, 2).Waypoints;

    private BotNavigationRoute FindNativeNavigationRoute(System.Numerics.Vector3 start, System.Numerics.Vector3 end, bool refinePlayer, float projectionRadius)
    {
        var scene = _nativeActiveCell ?? throw new InvalidOperationException("No active navigation scene.");
        var units = _configuration.World.GameUnitsToMeters;
        var identity = (scene.Cell.Worldspace ?? scene.Cell.FormKey).ToString();
        if (_botNavigationIdentity != identity)
        {
            var cells = scene.Cell.Worldspace is not { } worldspace ? new HashSet<FalloutFormKey> { scene.Cell.FormKey } :
                _nativePluginStack!.EffectiveRecords("CELL").Where(record => FalloutCellSceneReader.ParentWorldspace(record) == worldspace)
                    .Select(record => record.FormKey).ToHashSet();
            _botNavigation = CellNavigationGraph.LoadOwned(_nativePluginStack!, cells,
                (mesh, error) => GD.PushError($"OPENNV_BOT_NAVIGATION_UNAVAILABLE mesh={mesh} {error.Message}"));
            _botNavigationIdentity = identity;
            _botBlockedPortals.Clear();
        }
        Vector3 Source(System.Numerics.Vector3 point) => new Vector3(point.X, -point.Z, point.Y) / units;
        Vector3 World(Vector3 point) => new Vector3(point.X, point.Z, -point.Y) * units;
        static System.Numerics.Vector3 Numeric(Vector3 point) => new(point.X, point.Y, point.Z);
        if (!refinePlayer)
        {
            var actorPath = _botNavigation!.FindPath(Source(start), Source(end)).Select(World).Select(Numeric).ToArray();
            return new(actorPath, end, actorPath[^1], true, identity);
        }
        var origin = new Vector3(start.X, start.Y, start.Z);
        var now = Time.GetTicksMsec();
        foreach (var point in _botBlockedPortals.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
            _botBlockedPortals.Remove(point);
        // A portal midpoint is not the complete portal's traversable width.
        // Rejecting a distant midpoint can disconnect an otherwise walkable
        // route. Coarse A* excludes only segments that native refinement has
        // actually rejected; every returned movement segment is still swept
        // against resident collision with the player's complete capsule below.
        bool Permitted(Vector3 point) => !_botBlockedPortals.ContainsKey(point);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            // A reference may rest on an isolated counter/prop navmesh. Reach a
            // nearby authored floor, then let the ordinary interaction ray and
            // distance checks decide whether the target can actually be used.
            var path = _botNavigation!.FindPath(Source(start), Source(end), Permitted, projectionRadius / units);
            var worldPath = path.Select(World).ToArray();
            if (worldPath.Length == 0) throw new InvalidOperationException("Source navigation returned no corridor.");
            var (target, resume) = NativeCapsuleNavigation.CorridorPrefix(origin, worldPath, 8);
            try
            {
                var local = NativeCapsuleNavigation.Find(_nativePlayer!, origin, target,
                    _configuration.Player.StepHeightMeters, Math.Max(.3f, _configuration.Player.CapsuleRadiusMeters), NativeCollisionResident);
                GD.Print($"OPENNV_BOT_CAPSULE_ROUTE from={origin} to={target} sourceWaypoint={resume} " +
                    $"requested={end} projected={worldPath[^1]} projectionRadius={projectionRadius} " +
                    $"reachesProjected={resume == worldPath.Length} blockedPortals={_botBlockedPortals.Count} ms={Time.GetTicksMsec() - now}");
                return new(local.Select(Numeric).ToArray(), end, Numeric(worldPath[^1]), resume == worldPath.Length, identity, projectionRadius);
            }
            catch (InvalidOperationException error) when (attempt < 7 && path.Count > 1)
            {
                var blocked = path[Math.Min(resume, path.Count - 2)];
                _botBlockedPortals[blocked] = now + 30000;
                GD.Print($"OPENNV_BOT_BLOCKED_PORTAL source={blocked} reason={error.Message}");
            }
        }
        throw new InvalidOperationException("No capsule-supported source corridor after bounded A* alternatives.");
    }
}
