using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal enum ClassicMapUpdateOrigin { CommittedElevation, CommittedCombatExit }
internal sealed record ClassicCampaignEventReceipt(long Sequence, ClassicMapUpdateOrigin Origin, string Map,
    string MapSha256, int Elevation, bool HeaderConsumed, uint[] ConsumedScripts, string? Failure,
    long? BlockedBySequence = null);
internal sealed record ClassicCampaignEventSave(string Schema, long NextSequence,
    ClassicCampaignEventReceipt[] Events, IReadOnlyDictionary<string, ClassicIntProcedureState> MapStates,
    IReadOnlyDictionary<string, string> ProgramHashes);

/// <summary>Ordered source update delivery from committed native events. No polling timer.</summary>
internal sealed class ClassicCampaignEvents(ClassicPlayerSession player)
{
    private const string Schema = "opennv-classic-campaign-events/v1";
    private readonly List<ClassicCampaignEventReceipt> _events = [];
    private readonly Dictionary<string, ClassicIntProcedureState> _mapStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _programHashes = new(StringComparer.Ordinal);
    private long _nextSequence;
    internal IReadOnlyList<string> Failures => _events.Where(row => row.Failure is not null).Select(row => row.Failure!).ToArray();

    internal void CommittedElevation(string previousMap, int previousElevation)
    {
        if (previousMap == player.MapPath && previousElevation == player.Elevation) return;
        // The qualified source elevation commit requests update only when the
        // current map has a system script. Do not invent an initial/periodic call.
        if (player.Level.Map.MapScriptIndex > 0) Dispatch(ClassicMapUpdateOrigin.CommittedElevation);
    }

    internal void CommittedCombatExit()
    {
        if (player.Combat.Active != false) throw new InvalidOperationException("Map update requires the committed combat exit.");
        Dispatch(ClassicMapUpdateOrigin.CommittedCombatExit);
    }

    private void Dispatch(ClassicMapUpdateOrigin origin)
    {
        var level = player.Level;
        // Retain each actual producer request even when an earlier callback
        // prevents delivery. This is a blocked ledger entry, never a retry or
        // a successful source callback prefix.
        var blockedBy = _events.FirstOrDefault(row => row.Failure is not null);
        var receipt = new ClassicCampaignEventReceipt(_nextSequence++, origin, level.Path, level.Sha256,
            player.Elevation, false, [], null);
        _events.Add(receipt);
        if (blockedBy is not null)
        {
            Publish(receipt with
            {
                BlockedBySequence = blockedBy.Sequence,
                Failure = $"{level.Path}: map-update {origin} retained without dispatch; source event {blockedBy.Sequence} remains failed."
            });
            return;
        }
        try
        {
            if (player.Choice.Campaign != "fallout-2")
                throw new NotSupportedException("This campaign's native map-update origin contract is not yet qualified.");
            if (level.Map.MapScriptIndex > 0)
            {
                var program = ReadProgram(level.Map.MapScriptIndex - 1);
                if (!_mapStates.TryGetValue(level.Path, out var state))
                {
                    if (player.Initialization is not { ScriptState: { } initial } entry || entry.Map != level.Path || entry.Program != program.Identity)
                        throw new NotSupportedException("Map-update header requires this map's completed source entry state.");
                    state = initial;
                }
                var result = player.Doors.MapUpdateEvent(program, state, player.Animations.AcceptEventCommands);
                _mapStates[level.Path] = result.State;
            }
            receipt = receipt with { HeaderConsumed = true }; Publish(receipt);
            foreach (var slot in level.Map.LiveScripts)
            {
                var program = ReadProgram(slot.ScriptProgramIndex);
                if (!ClassicIntEvents.HasHandler(program, player.Choice.Campaign, 23)) continue;
                var source = level.Objects.TopLevelObjects.SingleOrDefault(row => row.ScriptId == slot.ScriptId && row.ObjectId == slot.ObjectId)
                    ?? throw new NotSupportedException($"Map-update script {slot.ScriptId} has no bound source object owner.");
                if (source.Prototype.ObjectType == 1 && player.Combat.Dead(source.Serial)) continue;
                if (ClassicDoorWorld.IsDoor(source)) player.Doors.DispatchMapUpdate(source.Serial);
                else if (player.Actors.Contains(source.Serial)) player.Actors.Dispatch(source.Serial, 23);
                else if (player.Animations.Contains(source.Serial)) player.Animations.DispatchMapUpdate(source.Serial);
                else throw new NotSupportedException($"Map-update script {slot.ScriptId} requires its {source.Prototype.ObjectType} object lifecycle owner.");
                receipt = receipt with { ConsumedScripts = [.. receipt.ConsumedScripts, slot.ScriptId] }; Publish(receipt);
            }
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or FileNotFoundException or OverflowException)
        {
            Publish(receipt with { Failure = $"{level.Path}: map-update {origin}: {error.Message}" });
        }
    }

    private void Publish(ClassicCampaignEventReceipt receipt) => _events[^1] = receipt;

    private ClassicIntProgram ReadProgram(int index)
    {
        var names = Fallout1NativeLists.Read(player.Catalog.Read("scripts/scripts.lst", out _));
        if (index < 0 || index >= names.Count) throw new InvalidDataException("Campaign script index exceeds scripts.lst.");
        var path = ClassicMapCatalog.Canonical("scripts/" + Path.ChangeExtension(names[index], ".int"));
        var bytes = player.Catalog.Read(path, out _);
        var hash = ClassicPremadeReader.Hash(bytes);
        if (_programHashes.TryGetValue(path, out var previous) && previous != hash)
            throw new InvalidDataException("Campaign event INT changed while its source prefix is retained.");
        _programHashes[path] = hash;
        return ClassicNativeIntReader.Read(bytes, path);
    }

    internal ClassicCampaignEventSave Save() => new(Schema, _nextSequence, _events.ToArray(),
        new Dictionary<string, ClassicIntProcedureState>(_mapStates, StringComparer.Ordinal),
        new Dictionary<string, string>(_programHashes, StringComparer.Ordinal));

    internal void Restore(ClassicCampaignEventSave saved)
    {
        if (saved.Schema != Schema || saved.Events is null || saved.MapStates is null || saved.ProgramHashes is null || saved.NextSequence < 0)
            throw new InvalidDataException("Invalid saved campaign event ledger.");
        foreach (var (path, hash) in saved.ProgramHashes)
        {
            if (ClassicMapCatalog.Canonical(path) != path || ClassicPremadeReader.Hash(player.Catalog.Read(path, out _)) != hash)
                throw new InvalidDataException("Saved campaign INT identity changed.");
            _programHashes.Add(path, hash);
        }
        long sequence = 0;
        long? firstFailure = null;
        foreach (var receipt in saved.Events)
        {
            var level = player.Catalog.Load(receipt.Map);
            if (receipt.Sequence != sequence++ || !Enum.IsDefined(receipt.Origin) ||
                level.Path != receipt.Map || level.Sha256 != receipt.MapSha256 || !level.Map.Elevations.ContainsKey(receipt.Elevation) ||
                receipt.ConsumedScripts is null || receipt.ConsumedScripts.Distinct().Count() != receipt.ConsumedScripts.Length ||
                !receipt.HeaderConsumed && receipt.ConsumedScripts.Length != 0 ||
                receipt.BlockedBySequence != firstFailure || firstFailure is not null &&
                (receipt.HeaderConsumed || receipt.ConsumedScripts.Length != 0 || receipt.Failure is null))
                throw new InvalidDataException("Saved campaign source event/prefix is invalid.");
            var order = level.Map.LiveScripts.Select(slot => slot.ScriptId).ToArray();
            var previous = -1;
            foreach (var id in receipt.ConsumedScripts)
            {
                var index = Array.IndexOf(order, id);
                if (index <= previous) throw new InvalidDataException("Saved campaign callback order changed.");
                previous = index;
            }
            _events.Add(receipt);
            if (receipt.Failure is not null) firstFailure ??= receipt.Sequence;
        }
        if (sequence != saved.NextSequence) throw new InvalidDataException("Saved campaign event cursor is inconsistent.");
        foreach (var (map, state) in saved.MapStates)
        {
            if (state.ValueStack.Count != 0 || !_events.Any(row => row.Map == map && row.HeaderConsumed))
                throw new InvalidDataException("Saved map script has no committed event prefix.");
            _mapStates.Add(map, state);
        }
        _nextSequence = saved.NextSequence;
    }
}
