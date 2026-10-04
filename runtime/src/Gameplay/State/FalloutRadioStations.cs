using System.Buffers.Binary;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutRadioStationsSnapshot(IReadOnlyList<FalloutFormKey> Discovered,
    FalloutPipBoyRadioSnapshot? PipBoy = null)
{
    internal void Validate()
    {
        if (Discovered is null || Discovered.Distinct().Count() != Discovered.Count ||
            Discovered.Any(key => key.ObjectId == 0 || string.IsNullOrWhiteSpace(key.OwnerPlugin)))
            throw new InvalidDataException("Saved radio discovery is absent, invalid or duplicated.");
        PipBoy?.Validate();
    }
}
internal sealed record FalloutRadioReception(FalloutRadioStation Station, bool Enabled, bool Available,
    float? Distance, string? Error);

// Availability is derived from the winning graph and applied world state.
// Discovery history is persistent; a cold load cannot rediscover a signal.
internal sealed class FalloutRadioStations(FalloutPluginStack records, FalloutReferenceWorld world,
    FalloutHudNotifications notifications)
{
    private readonly Dictionary<FalloutFormKey, FalloutRadioStation> _stations = [];
    private readonly Dictionary<FalloutFormKey, string> _sourceErrors = [];
    private readonly Dictionary<FalloutFormKey, FalloutCellDefinition> _cells = [];
    private readonly Dictionary<FalloutFormKey, HashSet<FalloutFormKey>> _links = [];
    private readonly HashSet<FalloutFormKey> _discovered = [];
    private bool _indexed, _linked, _initialRefresh = true;
    private IReadOnlyList<FalloutRadioReception> _reception = [];
    internal IReadOnlyList<FalloutRadioReception> Reception => _reception;
    internal IReadOnlyDictionary<FalloutFormKey, string> SourceErrors => _sourceErrors;
    internal IReadOnlyList<FalloutRadioStation> Available => _reception.Where(value => value.Available && value.Station.PipBoy)
        .Select(value => value.Station).ToArray();
    internal long Revision { get; private set; }
    internal long ForcedUpdates { get; private set; }
    internal Action? SignalDiscovered { get; set; }
    internal object State => new
    {
        Revision,
        ForcedUpdates,
        stations = _reception.Select(value => new
        {
            reference = value.Station.Reference.ToString(),
            source = value.Station.Base.ToString(),
            range = value.Station.Range.ToString(),
            value.Station.Radius,
            value.Station.StaticPercent,
            value.Station.PipBoy,
            value.Station.Continuous,
            continuousBroadcast = world.GetBroadcastState(value.Station.Reference),
            value.Enabled,
            value.Available,
            value.Distance,
            value.Error
        }),
        unbound = _sourceErrors.Select(value => new { reference = value.Key.ToString(), error = value.Value }),
        discovered = _discovered.Select(value => value.ToString()).ToArray(),
        pipBoy = world.PipBoyRadio.State,
        playback = "unbound-radio-conversations-static-and-physical-listeners",
        scheduling = "shared-frame-refresh;matched-retail-update-cadence-unverified"
    };

    private void Index()
    {
        if (_indexed) return;
        var bases = records.EffectiveRecords("TACT").Where(value => (value.Flags & 0x20000) != 0)
            .Select(value => value.FormKey).ToHashSet();
        if (bases.Count == 0) { _indexed = true; return; }
        foreach (var reference in records.EffectiveRecords("REFR"))
        {
            if (!bases.Contains(FalloutDialogueTopic.RequiredForm(reference, "NAME"))) continue;
            try { _stations.Add(reference.FormKey, FalloutRadioStation.Read(records, reference)); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or KeyNotFoundException)
            { _sourceErrors.Add(reference.FormKey, error.Message); }
        }
        _indexed = true;
    }

    private FalloutCellDefinition Cell(FalloutFormKey key)
    {
        if (!_cells.TryGetValue(key, out var cell)) _cells.Add(key, cell = FalloutCellSceneReader.ReadDefinition(records, key));
        return cell;
    }
    private bool Interior(FalloutFormKey key) => (Cell(key).Flags & FalloutCellSceneReader.InteriorCellFlag) != 0;

    private bool SameCell(FalloutReferencePlacement from, FalloutReferencePlacement to)
    {
        if (Interior(from.Cell) || Interior(to.Cell)) return from.Cell == to.Cell;
        return Cell(from.Cell).Worldspace == Cell(to.Cell).Worldspace &&
            MathF.Floor(from.Position[0] / 4096) == MathF.Floor(to.Position[0] / 4096) &&
            MathF.Floor(from.Position[1] / 4096) == MathF.Floor(to.Position[1] / 4096);
    }

    private void LinkCells()
    {
        if (_linked) return;
        foreach (var door in records.EffectiveRecords("REFR"))
        {
            var fields = door.ReadSubrecords().Where(field => field.Signature == "XTEL").ToArray();
            if (fields.Length == 0) continue;
            if (fields.Length != 1 || fields[0].Data.Length != 32)
                throw new NotSupportedException($"Radio cell-link XTEL layout is unbound at {door.FormKey}.");
            var target = records.GetEffective(door.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(fields[0].Data.Span)));
            var from = FalloutCellSceneReader.ParentCell(door) ?? throw new InvalidDataException("Radio portal has no source CELL.");
            var to = FalloutCellSceneReader.ParentCell(target) ?? throw new InvalidDataException("Radio portal target has no source CELL.");
            if (!_links.TryGetValue(from, out var neighbors)) _links.Add(from, neighbors = []);
            neighbors.Add(to);
        }
        _linked = true;
    }

    private bool InteriorLink(FalloutFormKey from, FalloutFormKey to)
    {
        if (!Interior(from) || !Interior(to)) return false;
        LinkCells();
        var visited = new HashSet<FalloutFormKey> { from };
        var pending = new Queue<FalloutFormKey>(); pending.Enqueue(from);
        while (pending.TryDequeue(out var current))
        {
            if (current == to) return true;
            foreach (var next in _links.GetValueOrDefault(current) ?? [])
                if (Interior(next) && visited.Add(next)) pending.Enqueue(next);
        }
        return false;
    }

    private bool LinkedWorld(FalloutFormKey interior, FalloutFormKey worldspace)
    {
        if (!Interior(interior)) return Cell(interior).Worldspace == worldspace;
        LinkCells();
        var visited = new HashSet<FalloutFormKey> { interior };
        var pending = new Queue<FalloutFormKey>(); pending.Enqueue(interior);
        while (pending.TryDequeue(out var current))
            foreach (var next in _links.GetValueOrDefault(current) ?? [])
            {
                if (Interior(next)) { if (visited.Add(next)) pending.Enqueue(next); }
                else if (Cell(next).Worldspace == worldspace) return true;
            }
        return false;
    }

    private FalloutRadioReception Evaluate(FalloutRadioStation station, FalloutReferencePlacement player)
    {
        var enabled = world.IsEnabled(station.Reference);
        if (!enabled) return new(station, false, false, null, null);
        try
        {
            var placement = world.Placement(station.Reference);
            bool available; float? distance = null;
            switch (station.Range)
            {
                case FalloutRadioRange.Everywhere: available = true; break;
                case FalloutRadioRange.CurrentCell: available = SameCell(placement, player); break;
                case FalloutRadioRange.LinkedInteriors: available = InteriorLink(placement.Cell, player.Cell); break;
                case FalloutRadioRange.WorldspaceAndLinkedInteriors:
                    var sourceWorld = Cell(placement.Cell).Worldspace ??
                        throw new NotSupportedException("Worldspace radio transmitter has no source worldspace.");
                    available = LinkedWorld(player.Cell, sourceWorld); break;
                case FalloutRadioRange.Radius:
                    var anchor = world.Placement(station.PositionReference ?? station.Reference);
                    var from = Cell(anchor.Cell); var to = Cell(player.Cell);
                    if (anchor.Cell != player.Cell && (from.Worldspace is null || from.Worldspace != to.Worldspace))
                        throw new NotSupportedException("Radio radius across portal spaces requires its native distance owner.");
                    var dx = (double)anchor.Position[0] - player.Position[0];
                    var dy = (double)anchor.Position[1] - player.Position[1];
                    var dz = (double)anchor.Position[2] - player.Position[2];
                    distance = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    available = distance < station.Radius; break;
                default: throw new NotSupportedException("Radio range has no owner.");
            }
            return new(station, true, available, distance, null);
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or KeyNotFoundException)
        { return new(station, true, false, null, error.Message); }
    }

    internal void Refresh(FalloutReferencePlacement player, bool force = false, bool notify = true)
    {
        player.Validate(); _ = Cell(player.Cell); Index();
        var reception = _stations.Values.OrderBy(value => records.RuntimeFormId(value.Reference)).Select(value => Evaluate(value, player)).ToArray();
        var discovered = reception.Where(value => value.Available && value.Station.PipBoy && !_discovered.Contains(value.Station.Reference))
            .Select(value => value.Station.Reference).ToArray();
        foreach (var reference in discovered)
        {
            if (notify && !_initialRefresh)
            {
                (SignalDiscovered ?? throw new NotSupportedException("Radio discovery has no source sound owner."))();
                notifications.Publish([new(FalloutHudEventKind.RadioDiscovered, reference, 0)]);
            }
            _discovered.Add(reference);
        }
        _initialRefresh = false;
        if (!_reception.SequenceEqual(reception)) { _reception = reception; ++Revision; }
        if (force) ++ForcedUpdates;
    }

    internal void SelectPipBoy(FalloutFormKey reference)
    {
        var station = Available.SingleOrDefault(value => value.Reference == reference) ??
            throw new InvalidOperationException("Pip-Boy station is disabled, unavailable or not a source receiver station.");
        world.PipBoyRadio.Select(station);
    }

    internal FalloutRadioStationsSnapshot Capture() => new(_discovered.OrderBy(value => records.RuntimeFormId(value)).ToArray(),
        world.PipBoyRadio.Capture());
    internal void Restore(FalloutRadioStationsSnapshot? snapshot)
    {
        if (_discovered.Count != 0 || _reception.Count != 0 || ForcedUpdates != 0)
            throw new InvalidOperationException("Radio restoration requires a fresh owner.");
        if (snapshot is null) return;
        snapshot.Validate();
        Index();
        if (snapshot.Discovered is null || snapshot.Discovered.Distinct().Count() != snapshot.Discovered.Count ||
            snapshot.Discovered.Any(key => !_stations.TryGetValue(key, out var station) || !station.PipBoy))
            throw new InvalidDataException("Saved radio discovery has no unique winning station owner.");
        world.PipBoyRadio.Restore(snapshot.PipBoy);
        foreach (var key in snapshot.Discovered) _discovered.Add(key);
    }
}
