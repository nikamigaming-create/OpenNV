using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal sealed record ClassicActorScriptSave(string Map, string MapSha256, int Serial, string Program,
    string ProgramSha256, ClassicIntProcedureState State, int[] Events, string? Failure, bool Retired = false);
internal sealed record ClassicActorScriptsSave(ClassicActorScriptSave[] Entries,
    IReadOnlyDictionary<string, ClassicIntTraitAssignment[]> Traits, ClassicActorWalk[]? Walks = null);

/// <summary>Source actor event prefixes. Shared globals, timers and RNG remain in DoorWorld/session.</summary>
internal sealed class ClassicActorScripts(ClassicPlayerSession player)
{
    private sealed record Entry(ClassicMapLevel Level, Fallout1NativeMapObject Source, ClassicIntProgram Program,
        string Path, string Hash, ClassicIntProcedureState State, int[] Events, string? Failure, bool Retired = false);
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Map, int Handle, int Type, int Trait), ClassicIntTraitAssignment> _traits = [];
    private static string Key(string map, int serial) => map + ":" + serial;
    internal IEnumerable<string> Failures => _entries.Values.Where(row => row.Level.Path == player.MapPath && row.Failure is not null).Select(row => row.Failure!);
    internal bool Contains(int serial) => _entries.TryGetValue(Key(player.MapPath, serial), out var entry) && !entry.Retired;
    internal void Retire(int serial)
    { var key = Key(player.MapPath, serial); if (_entries.TryGetValue(key, out var entry)) _entries[key] = entry with { Retired = true }; }
    internal int Trait(int serial, int trait, int fallback) => Trait(player.MapPath, serial, trait, fallback);
    internal int Trait(string map, int serial, int trait, int fallback) => _traits.TryGetValue((map, serial + 1, 1, trait), out var value) ? value.Amount : fallback;
    internal IReadOnlyDictionary<(int, int, int), int> TraitValues => _traits.Where(row => row.Key.Map == player.MapPath).ToDictionary(row => (row.Key.Handle, row.Key.Type, row.Key.Trait), row => row.Value.Amount);

    private Entry Read(ClassicMapLevel level, Fallout1NativeMapObject source)
    {
        var slot = level.Map.LiveScripts.Single(row => row.ScriptId == source.ScriptId && row.ObjectId == source.ObjectId);
        var names = Fallout1NativeLists.Read(player.Catalog.Read("scripts/scripts.lst", out _));
        var path = ClassicMapCatalog.Canonical("scripts/" + Path.ChangeExtension(names[slot.ScriptProgramIndex], ".int"));
        var bytes = player.Catalog.Read(path, out _);
        return new(level, source, ClassicNativeIntReader.Read(bytes, path), path, ClassicPremadeReader.Hash(bytes),
            player.Doors.InitialSceneryState(level, source), [], null);
    }

    internal void Enter()
    {
        var sources = player.Level.Map.LiveScripts.Select(slot => player.Level.Objects.TopLevelObjects.SingleOrDefault(row =>
            row.ScriptId == slot.ScriptId && row.ObjectId == slot.ObjectId && row.Prototype.ObjectType == 1)).Where(row => row is not null).ToArray();
        foreach (var source in sources)
        {
            var key = Key(player.MapPath, source!.Serial);
            if (!_entries.ContainsKey(key)) _entries.Add(key, Read(player.Level, source));
        }
        foreach (var source in sources)
            if (_entries[Key(player.MapPath, source!.Serial)].Events.Length == 0) TryDispatch(source.Serial, 1, initialize: true);
        foreach (var source in sources)
            if (_entries[Key(player.MapPath, source!.Serial)] is { Failure: null, Retired: false }) TryDispatch(source.Serial, 15);
    }

    internal void RequireReady(int serial)
    {
        var source = player.Level.Objects.TopLevelObjects.Single(row => row.Serial == serial);
        if (source.ScriptId == uint.MaxValue) return;
        if (!_entries.TryGetValue(Key(player.MapPath, serial), out var entry) || entry.Failure is not null || entry.Retired || !entry.Events.Contains(15))
            throw new NotSupportedException(entry?.Failure ?? "Actor has not completed source startup/map entry.");
    }

    internal bool Dispatch(int serial, int action, int? fixedParameter = null, int sourceObject = 0,
        ClassicIntTimerState? timers = null, bool initialize = false, int? hitPoints = null)
    {
        var key = Key(player.MapPath, serial);
        if (!_entries.TryGetValue(key, out var entry) || entry.Retired) return false;
        if (entry.Failure is not null) throw new NotSupportedException(entry.Failure);
        try
        {
            var result = player.Doors.ActorEvent(entry.Level, entry.Source, entry.Program, entry.State,
                action, initialize, fixedParameter, sourceObject, timers, hitPoints);
            _entries[key] = entry with { State = result.State, Events = [.. entry.Events, action] };
            return result.WorldObjects.ScriptOverrides;
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or FileNotFoundException or OverflowException)
        {
            _entries[key] = entry with { Failure = $"Actor {serial} {ClassicIntEvents.Name(player.Choice.Campaign, action)}: {error.Message}" };
            throw;
        }
    }

    private void TryDispatch(int serial, int action, bool initialize = false)
    {
        try { Dispatch(serial, action, initialize: initialize); }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or FileNotFoundException or OverflowException) { }
    }

    internal Action PrepareTraits(IReadOnlyList<ClassicIntTraitAssignment> changes, string? map = null)
    {
        map ??= player.MapPath;
        foreach (var row in changes)
            if (!_entries.ContainsKey(Key(map, row.ObjectHandle - 1)) || row.TraitType != 1 || row.Trait is not (5 or 6 or 10) ||
                row.Amount < 0 || row.Trait == 10 && (row.Amount >= 6 || map == player.MapPath && player.ActorMotion.Moving(row.ObjectHandle - 1)))
                throw new NotSupportedException("Actor script changes an unowned trait or foreign target.");
        return () =>
        {
            foreach (var row in changes) _traits[(map, row.ObjectHandle, row.TraitType, row.Trait)] = row;
            foreach (var row in changes.Where(row => row.Trait == 10)) player.ActorMotion.Rotate(map, row.ObjectHandle - 1, row.Amount);
            player.Combat.ApplyActorTraits(map, changes);
        };
    }

    internal ClassicActorScriptsSave Save() => new(_entries.Values.Select(row => new ClassicActorScriptSave(
        row.Level.Path, row.Level.Sha256, row.Source.Serial, row.Path, row.Hash, row.State, row.Events, row.Failure, row.Retired)).ToArray(), _traits.GroupBy(row => row.Key.Map).ToDictionary(group => group.Key, group => group.Select(row => row.Value).ToArray()), player.ActorMotion.Save());

    internal void Restore(ClassicActorScriptsSave saved)
    {
        foreach (var row in saved.Entries)
        {
            var level = player.Catalog.Load(row.Map);
            var entry = Read(level, level.Objects.TopLevelObjects.Single(item => item.Serial == row.Serial));
            if (level.Sha256 != row.MapSha256 || entry.Path != row.Program || entry.Hash != row.ProgramSha256 ||
                row.Retired && row.Events.LastOrDefault() != 18 || row.State.RandomState is not null || row.State.ValueStack.Count != 0 || row.Events.Any(action => action is < 0 or >= 28) ||
                !_entries.TryAdd(Key(row.Map, row.Serial), entry with { State = row.State, Events = row.Events, Failure = row.Failure, Retired = row.Retired }))
                throw new InvalidDataException("Actor script source or consumed prefix changed.");
        }
        foreach (var (map, traits) in saved.Traits) PrepareTraits(traits, map)();
        player.ActorMotion.Restore(saved.Walks ?? []);
    }
}
