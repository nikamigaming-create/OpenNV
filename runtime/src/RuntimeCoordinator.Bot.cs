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
    private readonly BotInteractionEvidence _botInteractions = new();
    private readonly Dictionary<FalloutFormKey, FalloutFormKey> _botPortalDestinations = [];

    private bool NativeBotLoading(FalloutReferenceWorld world) =>
        _nativeDoorLoading || _nativeSessionTransitioning || _retiringNativeSession || world.PlayerMoves.Pending ||
        _nativeLoadingLayer is not null || _loadingScreen is not null;

    private RuntimeNativeDoorPortal? NativeBotPortal(FalloutFormKey reference)
    {
        var node = _nativeReferencePresentation?.Nodes.GetValueOrDefault(reference);
        return node is not null && GodotObject.IsInstanceValid(node) && node.IsInsideTree()
            ? node.GetChildren().OfType<RuntimeNativeDoorPortal>().SingleOrDefault() : null;
    }

    private void BeginNativeBotActivation(FalloutFormKey reference)
    {
        try
        {
            if (NativeBotPortal(reference) is { } portal)
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
        var world = _nativeReferences ?? throw new InvalidOperationException("Reference world is not active.");
        var state = world.Get(reference);
        var furniture = _nativePlayer?.ObserveFurnitureInteraction();
        var linked = world.GetLinkedRef(reference);
        var target = BotInteractionSourceState.StableEffects(NativeBotSourceState(state),
            linked is { } linkedReference ? NativeBotSourceState(world.Get(linkedReference)) : null);
        var settledPortal = _botPortalDestinations.TryGetValue(reference, out var destination) &&
            _nativeActiveCell?.Cell.FormKey == destination && !NativeBotLoading(world) &&
            _nativePlayer?.CollisionResident == true;
        string? outcome = _nativeContainerLayer is not null && _nativeContainerReference == reference ? "container" :
            _nativeTerminalLayer is not null && _nativeTerminalReference == reference && _nativeTerminalMenu?.Error is null ? "terminal" :
            _nativeOpeningStageDriver?.PresentedConversationSpeaker == reference ? "conversation" :
            _nativePlayer?.CurrentFurniture == reference ? "furniture" :
            settledPortal ? "portal:" + destination : null;
        var portal = NativeBotPortal(reference);
        var requested = _nativeOpeningStageDriver?.PendingConversationSpeaker == reference ? "conversation" :
            furniture?.Reference == reference ? "furniture" :
            portal is { ActivationRequests: > 0 } ? "portal:" + portal.DestinationCell : null;
        return new(target, outcome, _nativeOpeningStageDriver?.ActiveMenus().Order().ToArray() ?? [], requested,
            requested == "furniture" ? furniture!.Ordinal :
                requested?.StartsWith("portal:", StringComparison.Ordinal) == true ? portal!.ActivationRequests : 0,
            outcome == "furniture" ? BotInteractionOutcomeKind.Furniture :
                settledPortal && outcome == "portal:" + destination ? BotInteractionOutcomeKind.Portal : BotInteractionOutcomeKind.Other,
            requested == "furniture" ? BotInteractionOutcomeKind.Furniture :
                requested?.StartsWith("portal:", StringComparison.Ordinal) == true ? BotInteractionOutcomeKind.Portal : BotInteractionOutcomeKind.Other,
            requested == "furniture" && furniture!.Pending, outcome == "furniture" ? furniture!.Ordinal : 0);
    }

    internal static BotInteractionSourceState NativeBotSourceState(FalloutReferenceInstance state) =>
        new(state.Reference.ToString(), state.Taken, state.DoorOpen, state.Deleted, state.Destroyed,
            (state.CaptureObjectAnimations?.Invoke() ?? state.ObjectAnimations ?? [])
                .Select(animation => new BotInteractionAnimationState(animation.Controller, animation.Sha256,
                    animation.Sequence, animation.ElapsedSeconds, animation.StartPending, animation.PendingSequence)).ToArray());

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
        var combat = ObserveNativeBotCombat();
        if (identity == ReactiveReferenceBot.CombatObservationIdentity) return NativeBotCombatControlObservation(combat);
        try { return ObserveNativeBotGoal(identity, combat); }
        catch (Exception error) when ((error is InvalidOperationException or InvalidDataException or NotSupportedException or KeyNotFoundException) &&
            !combat.Paused && !combat.Loading && !combat.ModalInput && ReactiveCombatSkill.SelectThreat(combat) is not null)
        {
            // A nonresident/faulted travel goal cannot suppress a genuine local
            // threat. Its failure is still re-observed before navigation resumes.
            return NativeBotCombatControlObservation(combat) with { ExecutionFault = "Gameplay execution stopped: " + error.Message };
        }
    }

    private BotObservation ObserveNativeBotGoal(string identity, BotCombatObservation combatObservation)
    {
        var player = _nativePlayer ?? throw new InvalidOperationException("Player is not active.");
        if (_nativeDeathPresented || player.IsDefeated?.Invoke() == true)
            return new(_nativeActiveCell!.Cell.FormKey.ToString(),
                new(player.GlobalPosition.X, player.GlobalPosition.Y, player.GlobalPosition.Z),
                Vector3ToNumeric(player.Camera.GlobalPosition), Vector3ToNumeric(-player.Camera.GlobalBasis.Z),
                System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero, null, GetTree().Paused,
                false, false, false, null, "0", Defeated: true, Combat: combatObservation);
        if (_nativeXr is not null && RuntimeSimulatorBotInput.DirectoryPath is null)
            throw new NotSupportedException("Physical headset bot control is unbound; use the simulator input adapter.");
        var separator = identity.LastIndexOf(':');
        if (separator <= 0 || !uint.TryParse(identity.AsSpan(separator + 1), NumberStyles.HexNumber,
            CultureInfo.InvariantCulture, out var objectId)) throw new ArgumentException("Expected plugin:hex source reference.");
        var key = new FalloutFormKey(identity[..separator], objectId);
        var world = _nativeReferences ?? throw new InvalidOperationException("Reference world is not active.");
        var referenceState = world.Get(key);
        var targetFault = _nativeReferenceEvents?.CanAdmitIndependentDefaultInteraction(key) == true ? null :
            referenceState.ScriptError ?? referenceState.SelectionFailure?.Error ?? referenceState.PackageBindingFailure?.Error;
        var executionFault = _nativeOpeningStageDriver?.BlockingExecutionFault ?? _nativeQuestScripts?.StartupError ??
            world.PlayerMoves.Error ?? _nativeReferencePresentation?.Error ?? targetFault;
        if (executionFault is not null)
            throw new InvalidOperationException("Gameplay execution stopped: " + executionFault);
        var loading = NativeBotLoading(world);
        // Observe the activation-owned destination before touching a model
        // that can belong to the retiring source cell. A settled source portal
        // still requires its genuine request and collision-ready destination.
        var interaction = _botInteractions.ObserveInteraction(key.ToString(), NativeBotInteractionSnapshot(key));
        var geometryRequired = interaction.RequiresNativeGeometry(loading);
        var node = _nativeReferencePresentation?.Nodes.GetValueOrDefault(key);
        var resident = node is not null && GodotObject.IsInstanceValid(node) && node.IsInsideTree() && node.IsVisibleInTree();
        var sourcePoint = resident && node!.GetMeta("opennv_internal_static", false).AsBool() &&
            _nativePluginStack!.GetEffective(key).Signature == "REFR" &&
            _nativePluginStack.GetEffective(referenceState.Base).Signature == "STAT";
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
            travelReady = !loading && player.CollisionResident;
        }
        var aim = target;
        if (resident && geometryRequired && !sourcePoint)
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
            else if (_nativePluginStack!.GetEffective(referenceState.Base).Signature == "DOOR")
                aim = NativeReferenceGeometryTarget.ObserveSurface(node!, player, player.Camera.GlobalPosition,
                    player.CombatCollisionRids, contact => _nativeReferenceEvents?.CollisionReference(contact.GetInstanceId()) == key,
                    NativeCollisionResident).Aim;
            else if (node is not RuntimeNativeCreature)
            {
                var geometry = NativeReferenceGeometryTarget.Observe(node!, player, player.Camera.GlobalPosition,
                    player.CombatCollisionRids, contact => _nativeReferenceEvents?.CollisionReference(contact.GetInstanceId()) == key,
                    NativeCollisionResident, (_configuration.Player.ActivationDistanceMeters +
                        _configuration.Player.CapsuleHeightMeters + _configuration.Player.StepHeightMeters) * player.GlobalBasis.Y.Length());
                target = geometry.Target;
                aim = geometry.Aim;
            }
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
        var aimed = geometryRequired && player.AimedObject() is { } collider
            ? _nativeReferenceEvents?.AimedReference(collider)?.FormKey.ToString() : null;
        BotDoorObservation? door = null;
        if (geometryRequired && _nativePluginStack!.GetEffective(referenceState.Base).Signature == "DOOR")
        {
            var observedDoor = _nativeReferenceEvents?.PlayerRouteDoor(key) ??
                throw new InvalidOperationException("Source route door has no reference event owner.");
            door = new(observedDoor.Open, observedDoor.Moving, observedDoor.Pending, observedDoor.Error);
        }
        static System.Numerics.Vector3 Numeric(Vector3 value) => new(value.X, value.Y, value.Z);
        var controls = player.SourceControls;
        var controlMask = (controls.Movement ? 1 : 0) | (controls.PipBoy ? 2 : 0) | (controls.Fighting ? 4 : 0) |
            (controls.PointOfView ? 8 : 0) | (controls.Looking ? 16 : 0) | (controls.RolloverText ? 32 : 0) | (controls.Sneaking ? 64 : 0);
        var menus = string.Join(',', (NativeActiveMenus() ?? []).Order());
        if (_nativeOpeningStageDriver?.PresentedConversationSpeaker is { } speaker) menus += "|conversation:" + speaker;
        return new(_nativeActiveCell!.Cell.FormKey.ToString(), Numeric(player.GlobalPosition), Numeric(player.Camera.GlobalPosition),
            Numeric(-player.Camera.GlobalBasis.Z), Numeric(target), Numeric(aim), aimed, GetTree().Paused,
            player.GetMeta("opennv_source_movement_enabled", false).AsBool() && !player.FurnitureActive,
            player.GetMeta("opennv_source_looking_enabled", false).AsBool(), resident && player.CollisionResident,
            player.BlockingShape, interaction.Revision.ToString(CultureInfo.InvariantCulture), travelReady, door,
            _nativeQuestState?.ProgressRevision ?? 0, menus, controlMask, player.ModalInput, loading, Combat: combatObservation,
            InteractionPending: interaction.PendingFurniture);
    }

    private static System.Numerics.Vector3 Vector3ToNumeric(Vector3 value) => new(value.X, value.Y, value.Z);

    private BotNavigationRoute FindNativeNavigationRoute(System.Numerics.Vector3 start, System.Numerics.Vector3 end, float projectionRadius)
        => FindNativeNavigationRoute(start, end, true, projectionRadius);

    private IReadOnlyList<System.Numerics.Vector3> FindNativeNavigationRoute(System.Numerics.Vector3 start, System.Numerics.Vector3 end, bool refinePlayer)
        => FindNativeNavigationRoute(start, end, refinePlayer, 2).Waypoints;

    private string EnsureNativeBotNavigation()
    {
        var scene = _nativeActiveCell ?? throw new InvalidOperationException("No active navigation scene.");
        var identity = (scene.Cell.Worldspace ?? scene.Cell.FormKey).ToString();
        if (_botNavigationIdentity != identity)
        {
            var cells = scene.Cell.Worldspace is not { } worldspace ? new HashSet<FalloutFormKey> { scene.Cell.FormKey } :
                _nativePluginStack!.EffectiveRecords("CELL").Where(record => FalloutCellSceneReader.ParentWorldspace(record) == worldspace)
                    .Select(record => record.FormKey).ToHashSet();
            _botNavigation = CellNavigationGraph.LoadOwned(_nativePluginStack!, cells,
                (mesh, error) => GD.PushError($"OPENNV_BOT_NAVIGATION_UNAVAILABLE mesh={mesh} {error.Message}"));
            _botNavigationIdentity = identity;
        }
        return identity;
    }

    private BotNavigationRoute FindNativeNavigationRoute(System.Numerics.Vector3 start, System.Numerics.Vector3 end, bool refinePlayer, float projectionRadius,
        System.Numerics.Vector3? approachTarget = null, float approachRadius = 0)
    {
        var identity = EnsureNativeBotNavigation();
        var units = _configuration.World.GameUnitsToMeters;
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
        var path = _botNavigation!.FindPath(Source(start), Source(end), destinationRadiusGameUnits: projectionRadius / units);
        var worldPath = path.Select(World).ToArray();
        var intent = NativeCapsuleNavigation.Intent(origin, worldPath, approachTarget is { } referenceTarget ?
            new(referenceTarget.X, referenceTarget.Y, referenceTarget.Z) : null, approachRadius,
            projection: projectionRadius > 0 ? new(new(end.X, end.Y, end.Z), worldPath[^1], projectionRadius) : null);
        IReadOnlyList<Vector3> corridor = intent.ReferenceApproach ? [intent.Target] : intent.Corridor;
        Func<ulong, FalloutFormKey?>? source = _nativeReferenceEvents is { } events ? events.CollisionReference : null;
        var probe = new NativeNavigationProbe(NativeCapsuleNavigation.FirstCorridorContact(_nativePlayer!, origin, corridor),
            source);
        var scope = intent.ReferenceApproach ? "reference-approach" : intent.Projection is not null ? "source-projection-region" : "source-corridor";
        Func<Vector3, bool>? arrival = null;
        if (intent.Projection is not null)
        {
            var region = _botNavigation!.ArrivalRegion(Source(start), Source(end), path[^1], projectionRadius / units,
                _nativePlayer!.SafeMargin * 8 / units, NativeCapsuleNavigation.SourceHeightTolerance / units);
            arrival = point => region(new Vector3(point.X, -point.Z, point.Y) / units);
        }
        try
        {
            var spacing = Math.Max(.3f, _configuration.Player.CapsuleRadiusMeters);
            var refinedSpacing = NativeCapsuleNavigation.RefinementSpacing(spacing,
                _configuration.Player.CapsuleRadiusMeters, _nativePlayer!.GlobalBasis.X.Length());
            var local = NativeCapsuleNavigation.FindRefined(_nativePlayer, origin, intent.Target,
                _configuration.Player.StepHeightMeters, spacing, refinedSpacing, NativeCollisionResident,
                targetRadius: intent.ArrivalRadius, probe: probe, corridor: intent.Corridor, arrival: arrival);
            GD.Print($"OPENNV_BOT_CAPSULE_ROUTE from={origin} to={intent.Target} sourceWaypoint={intent.Resume} " +
                $"requested={end} projected={worldPath[^1]} projectionRadius={projectionRadius} accepted={local.Path[^1]} scope={scope} " +
                $"reachesProjected={intent.Resume == worldPath.Length} sourceExclusions=0 " +
                $"arrivalRadius={intent.ArrivalRadius} sourceSha256={_botNavigation.SourceSha256} " +
                $"spacing={local.Spacing} coarseError={local.CoarseError ?? "none"} ms={Time.GetTicksMsec() - now}");
            var accepted = intent.Projection is null ? worldPath[^1] : World(_botNavigation.FindNearestPoint(Source(Numeric(local.Path[^1]))));
            BotNavigationProjection? projection = intent.Projection is { } sourceProjection ?
                new(Numeric(sourceProjection.Requested), Numeric(sourceProjection.Selected), sourceProjection.Radius, Numeric(accepted)) : null;
            return new(local.Path.Select(Numeric).ToArray(), end, Numeric(accepted), intent.Resume == worldPath.Length, identity, projectionRadius,
                Refinement: new(scope, Numeric(intent.Target), intent.ArrivalRadius, _botNavigation.SourceSha256, projection));
        }
        catch (InvalidOperationException error)
        {
            throw new InvalidOperationException($"No capsule-supported {scope}; source portals were not excluded. {error.Message}", error);
        }
    }
}
