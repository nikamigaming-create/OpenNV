using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Diagnostics.Parity;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private sealed record DiagnosticPlayerTravel(FalloutReferenceWorld World, FalloutPlayerMove Move,
        string CaseId, object Source, string Disposition = "queued", string? Error = null, string? CompletedCell = null);
    private DiagnosticPlayerTravel? _diagnosticPlayerTravel;
    private object? _diagnosticMapReveal;
    private FalloutReferenceWorld? _diagnosticMapWorld;

    private object? CaptureNativeDiagnosticTravel(bool includeRetired = false)
    {
        if (!includeRetired && !ReferenceEquals(_diagnosticMapWorld, _nativeReferences) &&
            (_diagnosticPlayerTravel is null || !ReferenceEquals(_diagnosticPlayerTravel.World, _nativeReferences))) return null;
        if (_diagnosticMapReveal is null && _diagnosticPlayerTravel is null) return null;
        return new
        {
            evidenceKind = "diagnostic-source-travel-not-ordinary-fast-travel-acceptance",
            ordinaryInput = false,
            ordinaryFastTravelAcceptance = false,
            mapReveal = ReferenceEquals(_diagnosticMapWorld, _nativeReferences) ? _diagnosticMapReveal : null,
            travel = _diagnosticPlayerTravel is not { } travel ? null : new
            {
                travel.CaseId,
                disposition = ReferenceEquals(travel.World, _nativeReferences) ? travel.Disposition : "session-retired",
                travel.Source,
                travel.Error,
                travel.CompletedCell,
                currentCell = _nativeActiveCell?.Cell.FormKey.ToString(),
            }
        };
    }

    private object ControlNativeDiagnosticTravel(JsonElement command, FalloutPluginStack records, FalloutReferenceWorld world)
    {
        var operation = command.GetProperty("op").GetString();
        if (operation == "rig.travel.status") return CaptureNativeDiagnosticTravel(includeRetired: true) ?? new { disposition = "none", ordinaryInput = false };
        if (_retiringNativeSession || _nativeSessionTransitioning || _nativeDoorLoading || _nativeManualSaves.Pending)
            throw new InvalidOperationException("Diagnostic setup requires settled existing session/transition/save owners.");
        if (_rigOriginalRestraints.Count != 0)
            throw new InvalidOperationException("Retire previous controlled-rig restraints through rig.quiesce before habitat setup.");
        var caseId = command.GetProperty("caseId").GetString();
        if (string.IsNullOrWhiteSpace(caseId)) throw new ArgumentException("Diagnostic setup requires a trace caseId.");
        if (operation == "rig.map.reveal")
        {
            // Source index/placement validation precedes the first map-state write.
            // No XP, enable/AI edit, discovery event or source-flag mutation.
            var markers = FalloutWorldMap.SourceMarkers(records)
                .Where(marker => (records.GetEffective(marker.Marker.Reference).Flags & FalloutPluginRecord.DeletedFlag) == 0)
                .Where(marker => world.Get(marker.Marker.Reference) is { Deleted: false, DeletePending: false }).ToArray();
            foreach (var marker in markers)
            {
                var placed = world.PlacedSource(marker.Marker.Reference);
                var placement = world.Placement(marker.Marker.Reference); placement.Validate();
                var cell = FalloutCellSceneReader.ReadDefinition(records, placement.Cell);
                if (placed.Reference.FormKey != marker.Marker.Reference ||
                    cell.Worldspace is { } space && records.GetEffective(space).Signature != "WRLD")
                    throw new InvalidDataException("Source map marker has no actual CELL/world owner.");
            }
            foreach (var marker in markers) world.ShowMap(marker.Marker.Reference, canTravel: true);
            _diagnosticMapWorld = world;
            _diagnosticMapReveal = new
            {
                caseId,
                owner = "FalloutReferenceWorld.ShowMap",
                disposition = "completed",
                count = markers.Length,
                canTravel = true,
                persistedBy = "ordinary-reference-map-state",
                ordinaryDiscoveryAcceptance = false,
            };
            GD.Print($"OPENNV_DIAGNOSTIC_MAP_REVEAL case={caseId} count={markers.Length} ordinaryDiscovery=false");
            return CaptureNativeDiagnosticTravel() ?? throw new InvalidOperationException("Diagnostic setup lost its own trace publication.");
        }
        if (operation != "rig.player.moveto") throw new NotSupportedException("Unknown diagnostic travel operation.");
        var player = _nativePlayer ?? throw new InvalidOperationException("Diagnostic travel has no actual player.");
        if (GetTree().Paused || player.ModalInput || player.FurnitureActive || _nativeActiveCell is null || _nativeOpeningStageDriver is null)
            throw new InvalidOperationException("Diagnostic travel requires an unpaused player outside active menus/furniture.");
        world.PlayerMoves.RequireSettled();
        var reference = RuntimeLiveHarness.ParseRigReference(command.GetProperty("reference").GetString()!);
        var expected = RuntimeLiveHarness.ParseRigReference(command.GetProperty("sourceForm").GetString()!);
        var source = records.GetEffective(reference);
        if (source.Signature is not ("REFR" or "ACHR" or "ACRE") || (source.Flags & FalloutPluginRecord.DeletedFlag) != 0)
            throw new InvalidDataException("Diagnostic destination is not a winning placed REFR/ACHR/ACRE.");
        if (world.Get(reference) is { Deleted: true } or { DeletePending: true })
            throw new InvalidDataException("Diagnostic destination has retired from the actual reference world.");
        var placedSource = world.PlacedSource(reference);
        if (placedSource.Reference.Base != expected)
            throw new InvalidDataException("Diagnostic source anchor differs from its expected winning base form.");
        // This is the same retained/live spatial owner used by queued script
        // Player MoveTo. Missing cached actor authority still refuses normally.
        var destination = world.SpatialPlacement(reference, null, player.UnitsToMeters);
        destination.Validate();
        var destinationCell = FalloutCellSceneReader.ReadDefinition(records, destination.Cell);
        if (destinationCell.Worldspace is { } worldspace && records.GetEffective(worldspace).Signature != "WRLD")
            throw new InvalidDataException("Diagnostic destination has no winning source worldspace.");
        if (command.TryGetProperty("cell", out var expectedCell) &&
            RuntimeLiveHarness.ParseRigReference(expectedCell.GetString()!) != destination.Cell)
            throw new InvalidDataException("Diagnostic destination differs from its expected current source CELL.");
        if (command.TryGetProperty("worldspace", out var expectedWorld))
        {
            var space = expectedWorld.ValueKind == JsonValueKind.Null ? (FalloutFormKey?)null :
                RuntimeLiveHarness.ParseRigReference(expectedWorld.GetString()!);
            if (space != destinationCell.Worldspace)
                throw new InvalidDataException("Diagnostic destination differs from its expected source worldspace.");
        }
        var offset = command.TryGetProperty("offsetSource", out var sourceOffset)
            ? sourceOffset.EnumerateArray().Select(value => value.GetSingle()).ToArray()
            : new float[] { 0, 0, 0 };
        if (offset.Length != 3 || offset.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Diagnostic MoveTo offset requires three finite source coordinates.");
        var move = new FalloutPlayerMove(records.RuntimeFormKey(0x14), reference, offset[0], offset[1], offset[2]);
        var trace = new
        {
            reference = reference.ToString(),
            baseForm = expected.ToString(),
            signature = source.Signature,
            winner = source.Plugin.Name,
            sha256 = Convert.ToHexString(SHA256.HashData(source.ReadData())),
            admittedCell = destination.Cell.ToString(),
            worldspace = destinationCell.Worldspace?.ToString(),
            admittedPositionSource = destination.Position.ToArray(),
            admittedRotationSource = destination.RotationRadians.ToArray(),
            owner = "existing-queued-player-MoveTo-and-world-transition",
            destinationResolution = "shared owner resolves current anchor again at execution",
            offsetSource = offset,
        };
        // The source queue is the existing diagnostic/script adapter boundary;
        // only PlayerRef moves. No NPC/prop placement, restraint or combat edits.
        world.PlayerMoves.Enqueue(move);
        _diagnosticPlayerTravel = new(world, move, caseId, trace);
        GD.Print($"OPENNV_DIAGNOSTIC_PLAYER_MOVETO case={caseId} reference={reference} cell={destination.Cell} ordinaryFastTravel=false");
        return CaptureNativeDiagnosticTravel() ?? throw new InvalidOperationException("Diagnostic setup lost its own trace publication.");
    }

    private void CompleteDiagnosticPlayerMove(FalloutReferenceWorld world, FalloutPlayerMove move, Exception? error)
    {
        if (_diagnosticPlayerTravel is not { } trace || !ReferenceEquals(trace.World, world) || !ReferenceEquals(trace.Move, move)) return;
        _diagnosticPlayerTravel = trace with
        { Disposition = error is null ? "completed" : "failed", Error = error?.Message, CompletedCell = _nativeActiveCell?.Cell.FormKey.ToString() };
    }
}
