using System.Buffers.Binary;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutCompassMarker(FalloutFormKey Reference, string Kind, float Heading,
    float Distance, bool Discovered);

internal sealed record FalloutCompassFrame(float? HeadingRadians, float? StripOffset,
    IReadOnlyList<FalloutCompassMarker> Markers, IReadOnlyList<string> Unbound);

internal sealed partial class FalloutReferenceWorld
{
    private readonly Dictionary<FalloutFormKey, FalloutReferencePlacement> _compassSourcePlacements = [];
    private readonly Dictionary<FalloutFormKey, IReadOnlyList<FalloutQuestTarget>> _compassQuestTargets = [];
    private readonly Dictionary<FalloutFormKey, float> _compassNorthRotations = [];
    private readonly Dictionary<FalloutFormKey, FalloutFormKey?> _compassNorthMarkers = [];
    private readonly Dictionary<FalloutFormKey, string> _compassNorthFailures = [];

    // This is a projection of shared world/quest state. Drawing never discovers
    // locations, awards experience, changes objectives or admits actor targets.
    internal FalloutCompassFrame Compass(FalloutReferencePlacement player, float unitsToMetres,
        float stripWidth, FalloutQuestState quests, Func<FalloutFormKey, FalloutCondition, float> evaluate)
    {
        player.Validate();
        if (!float.IsFinite(stripWidth) || stripWidth <= 70)
            throw new InvalidDataException("Source compass strip has no positive heading band.");
        var cell = FalloutCellSceneReader.ReadDefinition(records, player.Cell);
        var unbound = new List<string> { "npc-detection-hostility-ticks", "player-placed-marker", "distance-blink" };
        var world = cell.Worldspace;
        var north = world is null ? CompassNorthRotation(player.Cell, unbound) : 0;
        if (north is null) return new(null, null, [], unbound);
        var heading = player.RotationRadians[2];
        var phase = (heading + north.Value) / (2 * MathF.PI) - 0.15f;
        phase -= MathF.Floor(phase);
        var halfBand = (int)(stripWidth / 2) - 35;
        var markers = new List<FalloutCompassMarker>();
        var maximumDistance = unchecked((int)records.NumericSettings.IntegerBits("iMapMarkerVisibleDistance"));
        if (maximumDistance < 0) throw new InvalidDataException("Source compass marker distance is negative.");
        foreach (var source in FalloutWorldMap.SourceMarkers(records))
        {
            if (world is null) continue;
            var reference = source.Marker.Reference;
            FalloutReferencePlacement placement;
            if (_instances.TryGetValue(reference, out var instance) &&
                (instance.Placement is not null || instance.QuerySpatialPlacement is not null ||
                    instance.PackageMotion?.Position is not null || instance.Engagement?.Position is not null ||
                    instance.CaptureEngagement is not null || instance.SandboxAdmissionFailure is not null))
                placement = SpatialPlacement(reference, player, unitsToMetres);
            else
            {
                if (source.World != world) continue;
                if (!_compassSourcePlacements.TryGetValue(reference, out placement!))
                {
                    var record = records.GetEffective(reference);
                    var data = record.ReadSubrecords().Single(field => field.Signature == "DATA").Data;
                    if (data.Length != 24) throw new InvalidDataException("Compass marker has no source transform.");
                    placement = new(FalloutCellSceneReader.ParentCell(record) ??
                        throw new InvalidDataException("Compass marker has no CELL."),
                        [BinaryPrimitives.ReadSingleLittleEndian(data.Span), BinaryPrimitives.ReadSingleLittleEndian(data.Span[4..]),
                            BinaryPrimitives.ReadSingleLittleEndian(data.Span[8..])], [0, 0, 0]);
                    placement.Validate();
                    _compassSourcePlacements.Add(reference, placement);
                }
            }
            if (!SameSpace(placement)) continue;
            var distance = DistanceTo(placement, false);
            if (distance > maximumDistance || !IsEnabled(reference)) continue;
            Add(reference, "landmark", placement, distance, MapMarkerState(source.Marker).CanTravel, false);
        }
        if (quests.ActiveQuest is { } quest && !quests.IsCompleted(quest))
        {
            try
            {
                if (!_compassQuestTargets.TryGetValue(quest, out var targets))
                    _compassQuestTargets.Add(quest, targets = FalloutQuestTargets.Read(records, quest));
                foreach (var target in targets)
                {
                    if (!quests.ObjectiveDisplayed(quest, target.Objective) || quests.Objective(quest, target.Objective).Completed) continue;
                    try
                    {
                        if (!FalloutCondition.AllPass(target.Conditions, condition => evaluate(target.Reference, condition), evaluateRunOn: true)) continue;
                        var placement = SpatialPlacement(target.Reference, player, unitsToMetres);
                        if (!SameSpace(placement))
                        {
                            unbound.Add($"quest-portal-target:{target.Reference}");
                            continue;
                        }
                        Add(target.Reference, "quest", placement, DistanceTo(placement, true), false, true);
                    }
                    catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException)
                    { unbound.Add($"quest-target:{target.Reference}:{error.Message}"); }
                }
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException)
            { unbound.Add($"quest:{quest}:{error.Message}"); }
        }
        return new(heading, phase, markers, unbound);

        bool SameSpace(FalloutReferencePlacement placement) => placement.Cell == player.Cell ||
            world is not null && FalloutCellSceneReader.ReadDefinition(records, placement.Cell).Worldspace == world;
        float DistanceTo(FalloutReferencePlacement placement, bool horizontal)
        {
            var x = (double)placement.Position[0] - player.Position[0];
            var y = (double)placement.Position[1] - player.Position[1];
            var z = horizontal ? 0 : (double)placement.Position[2] - player.Position[2];
            return (float)Math.Sqrt(x * x + y * y + z * z);
        }
        void Add(FalloutFormKey reference, string kind, FalloutReferencePlacement placement, float distance, bool discovered, bool clamp)
        {
            var angle = FalloutHeadingAngle.Between(player.Position, placement.Position, heading) * 1.45f;
            if (!clamp && MathF.Abs(angle) >= 70) return;
            angle = Math.Clamp(angle, -70, 70);
            markers.Add(new(reference, kind, (angle + 70) / 140 * (2 * halfBand), distance, discovered));
        }
    }

    // CELL initialization derives north from its engine NorthMarker reference,
    // with the opposite sign to the placed clockwise Z angle. No marker means
    // the native extra-data getter returns zero; lighting rotation is unrelated.
    private float? CompassNorthRotation(FalloutFormKey cell, List<string> unbound)
    {
        if (_compassNorthFailures.TryGetValue(cell, out var failure))
        {
            unbound.Add(failure);
            return null;
        }
        if (_compassNorthRotations.TryGetValue(cell, out var cached))
            return Current(cached, _compassNorthMarkers[cell]);
        var markerBase = records.RuntimeFormKey(3);
        var markers = records.EffectiveCellChildren(cell, new HashSet<string> { "REFR" })
            .Where(record => FalloutDialogueTopic.RequiredForm(record, "NAME") == markerBase).ToArray();
        if (markers.Length > 1)
        {
            failure = $"interior-multiple-north-marker-order:{cell}";
            _compassNorthFailures.Add(cell, failure);
            unbound.Add(failure);
            return null;
        }
        var rotation = 0f;
        if (markers.Length == 1)
        {
            var data = markers[0].ReadSubrecords().Single(field => field.Signature == "DATA").Data;
            if (data.Length != 24) throw new InvalidDataException("NorthMarker requires a source transform.");
            rotation = -BinaryPrimitives.ReadSingleLittleEndian(data.Span[20..]);
            if (!float.IsFinite(rotation)) throw new InvalidDataException("NorthMarker rotation is not finite.");
        }
        FalloutFormKey? marker = markers.Length == 1 ? markers[0].FormKey : null;
        _compassNorthRotations.Add(cell, rotation);
        _compassNorthMarkers.Add(cell, marker);
        return Current(rotation, marker);

        float? Current(float sourceRotation, FalloutFormKey? sourceMarker)
        {
            if (sourceMarker is { } reference && _instances.TryGetValue(reference, out var retained) && retained.Placement is not null)
            {
                unbound.Add($"interior-runtime-north-marker-transform:{reference}");
                return null;
            }
            return sourceRotation;
        }
    }
}
