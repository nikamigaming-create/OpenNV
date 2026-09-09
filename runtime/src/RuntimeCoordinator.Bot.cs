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
        var aim = target;
        if (resident)
        {
            if (node is RuntimeNativeNpc actor)
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
        var interaction = JsonSerializer.Serialize(new
        {
            cell = _nativeActiveCell?.Cell.FormKey.ToString(),
            paused = GetTree().Paused,
            player.ModalInput,
            player.FurnitureActive,
            referenceState.Taken,
            referenceState.DoorOpen,
            referenceState.Deleted,
            stage = _nativeOpeningStageDriver?.Stage,
            conversation = _nativeOpeningStageDriver?.ConversationState,
        });
        static System.Numerics.Vector3 Numeric(Vector3 value) => new(value.X, value.Y, value.Z);
        return new(_nativeActiveCell!.Cell.FormKey.ToString(), Numeric(player.GlobalPosition), Numeric(player.Camera.GlobalPosition),
            Numeric(-player.Camera.GlobalBasis.Z), Numeric(target), Numeric(aim), aimed, GetTree().Paused || player.ModalInput || _nativeDoorLoading,
            player.GetMeta("opennv_source_movement_enabled", false).AsBool() && !player.FurnitureActive,
            player.GetMeta("opennv_source_looking_enabled", false).AsBool(), resident && player.CollisionResident,
            player.BlockingShape, interaction);
    }

    private IReadOnlyList<System.Numerics.Vector3> FindNativeBotRoute(System.Numerics.Vector3 start, System.Numerics.Vector3 end)
    {
        var scene = _nativeActiveCell ?? throw new InvalidOperationException("No active navigation scene.");
        var units = _configuration.World.GameUnitsToMeters;
        var cells = scene.Cell.Worldspace is null ? new HashSet<FalloutFormKey> { scene.Cell.FormKey } :
            scene.References.Select(reference => reference.Cell).Append(scene.Cell.FormKey).ToHashSet();
        var identity = string.Join(',', cells.OrderBy(cell => cell.ToString()).Select(cell => cell.ToString()));
        if (_botNavigationIdentity != identity)
        {
            _botNavigation = CellNavigationGraph.LoadOwned(_nativePluginStack!, cells);
            _botNavigationIdentity = identity;
        }
        Vector3 Source(System.Numerics.Vector3 point) => new Vector3(point.X, -point.Z, point.Y) / units;
        var path = _botNavigation!.FindPath(Source(start), Source(end));
        return path.Select(point => new System.Numerics.Vector3(point.X, point.Z, -point.Y) * units).ToArray();
    }
}
