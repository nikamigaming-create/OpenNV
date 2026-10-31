using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Diagnostics.Parity;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private FalloutFormKey? _rigTarget, _rigEntry;
    private string? _rigCase;
    private FalloutFormKey? _rigTargetAnchor;
    private long _rigPlacementRevision;
    private bool? _rigRequestedRestraint;
    private readonly Dictionary<FalloutFormKey, bool> _rigOriginalRestraints = [];

    private void RestoreNativeRigRestraints()
    {
        if (_nativeReferences is not { } world) return;
        foreach (var (reference, restrained) in _rigOriginalRestraints) world.SetRestrained(reference, restrained);
        _rigOriginalRestraints.Clear();
    }

    private Node3D ResolveNativeRigReference(FalloutFormKey reference)
    {
        var world = _nativeReferences ?? throw new InvalidOperationException("No authoritative rig world.");
        if (!world.IsResident(reference) || !world.IsEnabled(reference) || world.Get(reference) is { DeletePending: true } or { Deleted: true })
            throw new NotSupportedException($"Rig reference {reference} is not enabled and resident.");
        var node = _nativeReferencePresentation?.TryResolve(reference);
        return node is not null && GodotObject.IsInstanceValid(node) && node.IsInsideTree() && !node.IsQueuedForDeletion()
            ? node : throw new NotSupportedException($"Rig reference {reference} has no actual resident presentation.");
    }

    private object ControlNativeRig(JsonElement command)
    {
        var records = _nativePluginStack ?? throw new InvalidOperationException("No indexed rig source.");
        var world = _nativeReferences ?? throw new InvalidOperationException("No authoritative rig world.");
        var operation = command.GetProperty("op").GetString();
        if (operation == "rig.source") return new
        {
            plugins = records.Plugins.Select(plugin => new { name = Path.GetFileName(plugin.Plugin.Path), plugin.Sha256, plugin.LoadOrderIndex }).ToArray(),
            supportedActions = new[] { "object.playgroup", "actor.playidle", "reference.activate", "observe" },
            unsupported = new[] { "arbitrary actor PlayGroup", "anonymous actor creation", "weapon inventory/equip setup" }
        };
        if (operation == "rig.quiesce")
        {
            if (_nativeManualSaves.Receipt?.CleanupError is { } cleanup)
                throw new InvalidOperationException("Shared save cleanup did not settle: " + cleanup);
            if (world.PlayerMoves.Error is { } transferFailure)
                throw new InvalidOperationException("Shared world transfer failed: " + transferFailure);
            if (_nativeManualSaves.Pending || world.PlayerMoves.Pending || _nativeDoorLoading || _nativeSessionTransitioning || _nativeReferenceEvents?.HasPendingActivations == true)
                return new { disposition = "pending", owner = "actual-world-transfer-or-activation" };
            RestoreNativeRigRestraints();
            _rigTarget = null; _rigEntry = null; _rigCase = null; _rigTargetAnchor = null;
            _rigRequestedRestraint = null; _rigPlacementRevision = 0;
            return new { disposition = "completed", pendingInputReleasedByHarness = true };
        }
        if (operation == "rig.prepare")
        {
            if (GetTree().Paused || _nativePlayer is null || _nativeDoorLoading || _nativeSessionTransitioning || world.PlayerMoves.Pending)
                throw new InvalidOperationException("Rig preparation requires a settled unpaused world.");
            var target = RuntimeLiveHarness.ParseRigReference(command.GetProperty("reference").GetString()!);
            var entry = command.TryGetProperty("entryReference", out var entryValue) && entryValue.ValueKind == JsonValueKind.String
                ? RuntimeLiveHarness.ParseRigReference(entryValue.GetString()!) : (FalloutFormKey?)null;
            var expected = RuntimeLiveHarness.ParseRigReference(command.GetProperty("sourceForm").GetString()!);
            if (world.PlacedSource(target).Reference.Base != expected)
                throw new InvalidDataException("Rig reference base differs from its source catalog case.");
            var targetAnchor = command.TryGetProperty("targetAnchorReference", out var anchorValue) && anchorValue.ValueKind == JsonValueKind.String
                ? RuntimeLiveHarness.ParseRigReference(anchorValue.GetString()!) : (FalloutFormKey?)null;
            var restraint = command.TryGetProperty("restrained", out var restraintValue) && restraintValue.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? restraintValue.GetBoolean() : (bool?)null;
            if (restraint is not null && records.GetEffective(expected).Signature is not ("NPC_" or "CREA"))
                throw new InvalidDataException("Rig restraint requires an actual actor base.");
            // Resolve every source marker before making either preparation move
            // or restoring a previous case's gameplay control state.
            if (entry is { } cameraMarker) _ = world.PlacedSource(cameraMarker);
            if (targetAnchor is { } actorMarker) _ = world.PlacedSource(actorMarker);
            RestoreNativeRigRestraints();
            if (targetAnchor is { } targetMarker)
            {
                world.MoveTo(target, targetMarker);
            }
            _rigPlacementRevision = world.PlacementRevision;
            // Exact authored marker pose. The shared Player MoveTo owner loads
            // the actual cell; this diagnostic does not place actors or meshes.
            if (entry is { } marker)
            {
                world.PlayerMoves.Enqueue(new(records.RuntimeFormKey(0x14), marker, 0, 0, 0));
            }
            else if (targetAnchor is null) _ = ResolveNativeRigReference(target); // Retain the actual checkpoint camera/location.
            _rigTarget = target; _rigEntry = entry; _rigCase = command.GetProperty("caseId").GetString();
            _rigTargetAnchor = targetAnchor; _rigRequestedRestraint = restraint;
            return new { disposition = "admitted", owner = "shared-player-MoveTo" };
        }
        if (_rigTarget is not { } reference)
            throw new InvalidOperationException("No prepared rig case.");
        if (command.TryGetProperty("caseId", out var caseId) && caseId.GetString() != _rigCase)
            throw new InvalidDataException("Rig command belongs to another case.");
        if (world.PlayerMoves.Error is { } moveError) throw new NotSupportedException(moveError);
        if (world.PlayerMoves.Pending || _nativeDoorLoading || _nativeSessionTransitioning)
            return new { disposition = "pending", owner = "shared-player-MoveTo", caseId = _rigCase };
        if (_nativePlacementRevision < _rigPlacementRevision)
            return new { disposition = "pending", owner = "shared-reference-placement-publication", caseId = _rigCase };
        var targetNode = ResolveNativeRigReference(reference);
        if (_rigRequestedRestraint is { } requested)
        {
            _rigOriginalRestraints.TryAdd(reference, world.Get(reference).Restrained);
            world.SetRestrained(reference, requested);
        }
        if (operation == "rig.action")
        {
            var action = command.GetProperty("action").GetString();
            switch (action)
            {
                case "observe": break;
                case "object.playgroup":
                    _nativeReferencePresentation!.PlayGroup(reference, command.GetProperty("argument").GetString()!,
                        command.TryGetProperty("initialization", out var initialization) ? initialization.GetInt32() : 0);
                    break;
                case "actor.playidle":
                    var idle = command.GetProperty("argument").GetString()!;
                    if (targetNode is RuntimeNativeNpc npc) npc.PlayIdle(records, idle);
                    else if (targetNode is RuntimeNativeCreature creature) creature.PlayIdle(records, idle);
                    else throw new NotSupportedException("PlayIdle requires an actual actor skeleton owner.");
                    break;
                case "reference.activate":
                    (_nativeReferenceEvents ?? throw new NotSupportedException("No resident source event owner."))
                        .ScriptActivate(reference, records.RuntimeFormKey(0x14), true);
                    break;
                default: throw new NotSupportedException($"No shared rig action owner for {action}.");
            }
            return new
            {
                disposition = "admitted",
                caseId = _rigCase,
                action,
                boundary = "Actual source owner requested; action completion/pose/pixels remain separate evidence."
            };
        }
        if (operation != "rig.status") throw new NotSupportedException($"Unknown rig operation {operation}.");
        var camera = GetViewport().GetCamera3D() ?? throw new NotSupportedException("Rig world has no actual camera.");
        return new
        {
            disposition = "prepared",
            caseId = _rigCase,
            reference = reference.ToString(),
            entryReference = _rigEntry?.ToString(),
            targetAnchorReference = _rigTargetAnchor?.ToString(),
            targetAnchorPlacement = _rigTargetAnchor is { } anchor ? world.Placement(anchor) : null,
            restrained = world.Get(reference).Restrained,
            baseSource = world.PlacedSource(reference).Reference.Base.ToString(),
            nodeInstance = targetNode.GetInstanceId(),
            sourcePlacement = world.Placement(reference),
            targetWorld = RigMatrix(targetNode.GlobalTransform),
            cameraWorld = RigMatrix(camera.GlobalTransform),
            camera.Fov,
            camera.Near,
            camera.Far,
            viewport = new { width = GetViewport().GetVisibleRect().Size.X, height = GetViewport().GetVisibleRect().Size.Y },
            targetVisibleInTree = targetNode.IsVisibleInTree(),
            boundary = "Actual source/camera identified; frustum/pixels, 60FPS, action completion and paired state require independent evidence."
        };
    }

    private static float[] RigMatrix(Transform3D matrix) =>
        [matrix.Basis.X.X, matrix.Basis.X.Y, matrix.Basis.X.Z, matrix.Basis.Y.X, matrix.Basis.Y.Y, matrix.Basis.Y.Z,
            matrix.Basis.Z.X, matrix.Basis.Z.Y, matrix.Basis.Z.Z, matrix.Origin.X, matrix.Origin.Y, matrix.Origin.Z];
}
