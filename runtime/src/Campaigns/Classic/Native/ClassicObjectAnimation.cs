using System.Text.Json;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal sealed record ClassicObjectAnimationPose(int Frame, double Elapsed = 0, bool Repeating = false);
internal sealed record ClassicObjectAnimationEvent(int Action, ClassicIntAnimationCommand[] Commands);
internal sealed record ClassicObjectAnimationEntrySave(string Map, string MapSha256, int Serial, int Pid,
    string? Identity, ClassicObjectAnimationPose Pose, ClassicIntProcedureState? ScriptState,
    ClassicObjectAnimationEvent[] ConsumedEvents, bool Entered, int? FailedAction, string? Failure);
internal sealed record ClassicObjectAnimationSave(string Schema, ClassicObjectAnimationEntrySave[] Entries);

/// <summary>Source scenery event transactions and frame poses, independent of the renderer.</summary>
internal sealed class ClassicObjectAnimation(ClassicPlayerSession player)
{
    private const string Schema = "opennv-classic-object-animation/v1";
    private sealed record Definition(string Art, string Identity, int Frames, int Fps, ClassicIntProgram Program);
    private sealed record Entry(ClassicMapLevel Level, Fallout1NativeMapObject Source, Definition? Definition,
        ClassicObjectAnimationPose Pose, ClassicIntProcedureState? State, ClassicObjectAnimationEvent[] Events,
        bool Entered, int? FailedAction, string? Failure);
    private Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private static string Key(string map, int serial) => map + ":" + serial;
    private static bool HasOwner(Fallout1NativeMapObject row) => row.Prototype.ObjectType == 2 &&
        !ClassicDoorWorld.IsDoor(row) && row.ScriptId != uint.MaxValue;
    private IEnumerable<Entry> Current => _entries.Values.Where(row => row.Level.Path == player.MapPath);
    internal IReadOnlyList<string> Failures => Current.Where(row => row.Failure is not null).Select(row => row.Failure!).ToArray();
    internal bool Contains(int serial) => _entries.ContainsKey(Key(player.MapPath, serial));
    internal int Frame(int serial) => _entries[Key(player.MapPath, serial)].Pose.Frame;

    private Definition Read(ClassicMapLevel level, Fallout1NativeMapObject row)
    {
        var slot = level.Map.LiveScripts.SingleOrDefault(item => item.ScriptId == row.ScriptId)
            ?? throw new InvalidDataException("Scenery has no live MAP script slot.");
        if (slot.ObjectId != row.ObjectId) throw new InvalidDataException("Scenery MAP/script ownership differs.");
        var names = Fallout1NativeLists.Read(player.Catalog.Read("scripts/scripts.lst", out _));
        if (slot.ScriptProgramIndex < 0 || slot.ScriptProgramIndex >= names.Count)
            throw new InvalidDataException("Scenery script index exceeds scripts.lst.");
        var path = ClassicMapCatalog.Canonical("scripts/" + Path.ChangeExtension(names[slot.ScriptProgramIndex], ".int"));
        var script = player.Catalog.Read(path, out _);
        var program = ClassicNativeIntReader.Read(script, path);
        var art = Fallout1NativePrototypeReader.ResolveArt(player.Catalog, row.Fid);
        var bytes = player.Catalog.Read(art, out _);
        var frame = Fallout1NativeFrmReader.ReadFrame(bytes, row.Rotation, row.Frame);
        for (var index = 0; index < frame.FramesPerDirection; index++)
            _ = Fallout1NativeFrmReader.ReadFrame(bytes, row.Rotation, index);
        var identity = ClassicPremadeReader.Hash(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Art = art,
            Frm = ClassicPremadeReader.Hash(bytes),
            Script = path,
            Int = ClassicPremadeReader.Hash(script),
            Prototype = ClassicPremadeReader.Hash(player.Catalog.Read(row.Prototype.LogicalPath!, out _))
        }));
        return new(art, identity, frame.FramesPerDirection, frame.StoredFps == 0 ? 10 : frame.StoredFps, program);
    }

    private Entry Discover(ClassicMapLevel level, Fallout1NativeMapObject row)
    {
        try { return new(level, row, Read(level, row), new(row.Frame), null, [], false, null, null); }
        catch (Exception error) when (OwnedFailure(error))
        { return new(level, row, null, new(row.Frame), null, [], false, null, $"{level.Path}:{row.Serial}: {error.Message}"); }
    }

    private static bool OwnedFailure(Exception error) => error is InvalidDataException or InvalidOperationException or
        NotSupportedException or FileNotFoundException or OverflowException;

    private Entry[] Ordered()
    {
        var order = player.Level.Map.LiveScripts.Select((slot, index) => (slot.ScriptId, index))
            .ToDictionary(row => row.ScriptId, row => row.index);
        return Current.OrderBy(row => order.GetValueOrDefault(row.Source.ScriptId, int.MaxValue))
            .ThenBy(row => row.Source.Serial).ToArray();
    }

    internal void Enter()
    {
        foreach (var row in player.Level.Objects.TopLevelObjects.Where(HasOwner))
        {
            var key = Key(player.MapPath, row.Serial);
            if (!_entries.ContainsKey(key)) _entries.Add(key, Discover(player.Level, row));
        }
        // Initialization is a separate committed prefix. Failure during a later
        // map-entry handler never repeats successful startup or its exports.
        foreach (var entry in Ordered())
            if (entry.Events.Length == 0) Dispatch(entry, 1, initializeModule: true);
        foreach (var entry in Ordered())
            if (!entry.Entered && entry.Failure is null)
            {
                if (Dispatch(entry, 15))
                {
                    var key = Key(entry.Level.Path, entry.Source.Serial);
                    _entries[key] = _entries[key] with { Entered = true };
                }
            }
    }

    internal void Leave(string destination)
    {
        if (destination == player.MapPath) return;
        foreach (var entry in Ordered())
        {
            if (entry.Entered && !Dispatch(entry, 16))
                throw new NotSupportedException(_entries[Key(entry.Level.Path, entry.Source.Serial)].Failure);
            var key = Key(entry.Level.Path, entry.Source.Serial);
            _entries[key] = _entries[key] with { Entered = false };
        }
        foreach (var entry in Current.ToArray())
        {
            var key = Key(entry.Level.Path, entry.Source.Serial);
            _entries[key] = entry with { Entered = false, Pose = entry.Pose with { Repeating = false, Elapsed = 0 } };
        }
    }

    // Only the ordered campaign dispatcher supplies map-update delivery.
    internal void DispatchMapUpdate(int serial)
    {
        var entry = _entries[Key(player.MapPath, serial)];
        if (!entry.Entered || entry.Failure is not null)
            throw new NotSupportedException(entry.Failure ?? "Scenery has no completed map-entry state.");
        if (!Dispatch(entry, 23)) throw new NotSupportedException(_entries[Key(player.MapPath, serial)].Failure);
    }

    internal void AcceptEventCommands(IReadOnlyList<ClassicIntAnimationCommand> commands) => AcceptCommands(commands);

    private bool Dispatch(Entry entry, int action, bool initializeModule = false)
    {
        if (entry.Failure is not null || entry.Definition is null) return false;
        var key = Key(entry.Level.Path, entry.Source.Serial);
        if (!initializeModule && !ClassicIntEvents.HasHandler(entry.Definition.Program, player.Choice.Campaign, action)) return true;
        try
        {
            var state = entry.State ?? player.Doors.InitialSceneryState(entry.Level, entry.Source);
            var result = player.Doors.SceneryEvent(entry.Level, entry.Source, entry.Definition.Program, state, action,
                initializeModule, player.CombatActive?.Invoke(), AcceptCommands);
            var current = _entries[key];
            _entries[key] = current with
            {
                State = result.State,
                Events = [.. current.Events, new(action, result.WorldObjects.AnimationCommands.ToArray())]
            };
            return true;
        }
        catch (Exception error) when (OwnedFailure(error))
        {
            // The failed event publishes no partial effects. Earlier events,
            // their consumed commands and running poses remain authoritative.
            _entries[key] = _entries[key] with { FailedAction = action, Failure = $"{entry.Level.Path}:{entry.Source.Serial} action={action}: {error.Message}" };
            return false;
        }
    }

    private void AcceptCommands(IReadOnlyList<ClassicIntAnimationCommand> commands)
    {
        var next = new Dictionary<string, Entry>(_entries, StringComparer.Ordinal);
        string? pending = null;
        var registering = false;
        var refused = false;
        foreach (var command in commands)
        {
            if (command.SuppressedByCombat) continue;
            switch (command.Command)
            {
                case 1:
                    if (command.Parameter != 1 || registering)
                        throw new NotSupportedException("Animation registration flags or nested registration are not owned.");
                    registering = true; pending = null;
                    // Admitted requests are ordinary unreserved source sequences.
                    refused = next.Values.Count(row => row.Level.Path == player.MapPath && row.Pose.Repeating) >= 20;
                    break;
                case 2:
                    {
                        var key = Key(player.MapPath, command.Parameter - 1);
                        if (!next.TryGetValue(key, out var target) || target.Definition is null)
                            throw new NotSupportedException("Animation clear target has no source scenery owner.");
                        next[key] = target with { Pose = target.Pose with { Repeating = false, Elapsed = 0 } };
                        break;
                    }
                case 4:
                    {
                        if (!registering || pending is not null || command.Parameter != 0)
                            throw new NotSupportedException("Animation requires an unowned sequence, animation code or dependency.");
                        var key = Key(player.MapPath, command.ObjectHandle - 1);
                        if (!next.TryGetValue(key, out var target) || target.Definition is null || target.Failure is not null)
                            throw new NotSupportedException("Animation target has no admitted source scenery definition.");
                        // An already owned ordinary sequence refuses a competing
                        // registration. Repeated update events do not reset phase.
                        refused |= target.Pose.Repeating;
                        pending = key;
                        break;
                    }
                case 3:
                    if (!registering || pending is null)
                        throw new NotSupportedException("Animation end has no supported pending sequence.");
                    if (!refused)
                    {
                        var target = next[pending];
                        next[pending] = target with { Pose = new(0, 0, true) };
                    }
                    registering = false; pending = null; refused = false;
                    break;
                default: throw new NotSupportedException("Animation command has no native owner.");
            }
        }
        if (registering) throw new NotSupportedException("Cross-event open animation registration is not owned.");
        _entries = next;
    }

    internal void Advance(double delta)
    {
        if (!double.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        foreach (var entry in Current.Where(row => row.Pose.Repeating).ToArray())
        {
            var definition = entry.Definition!;
            var period = 1.0 / definition.Fps;
            var elapsed = entry.Pose.Elapsed + delta;
            if (!double.IsFinite(elapsed)) throw new ArgumentOutOfRangeException(nameof(delta));
            var steps = Math.Floor(elapsed / period);
            var frame = (entry.Pose.Frame + (int)(steps % definition.Frames)) % definition.Frames;
            elapsed %= period;
            _entries[Key(entry.Level.Path, entry.Source.Serial)] = entry with { Pose = new(frame, elapsed, true) };
        }
    }

    internal ClassicObjectAnimationSave Save() => new(Schema, _entries.Values.OrderBy(row => row.Level.Path, StringComparer.Ordinal)
        .ThenBy(row => row.Source.Serial).Select(row => new ClassicObjectAnimationEntrySave(row.Level.Path, row.Level.Sha256,
            row.Source.Serial, row.Source.Pid, row.Definition?.Identity, row.Pose, row.State, row.Events, row.Entered, row.FailedAction, row.Failure)).ToArray());

    internal void Restore(ClassicObjectAnimationSave save)
    {
        if (save.Schema != Schema || save.Entries is null) throw new InvalidDataException("Unsupported scenery animation save.");
        var restored = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var saved in save.Entries)
        {
            var level = player.Catalog.Load(saved.Map);
            var source = level.Objects.TopLevelObjects.SingleOrDefault(row => row.Serial == saved.Serial && row.Pid == saved.Pid && HasOwner(row));
            if (source is null || level.Path != saved.Map || level.Sha256 != saved.MapSha256)
                throw new InvalidDataException("Saved scenery MAP or source object changed.");
            var entry = Discover(level, source);
            var pose = saved.Pose;
            if (entry.Definition?.Identity != saved.Identity || saved.ConsumedEvents is null || pose is null ||
                pose.Frame < 0 || pose.Frame >= (entry.Definition?.Frames ?? source.Frame + 1) ||
                !double.IsFinite(pose.Elapsed) || pose.Elapsed < 0 || pose.Elapsed >= 1.0 / (entry.Definition?.Fps ?? 1) ||
                !pose.Repeating && pose.Elapsed != 0 || pose.Repeating && (entry.Definition is null || saved.ConsumedEvents.Length == 0) ||
                saved.ScriptState?.ValueStack.Count > 0 || saved.ScriptState?.RandomState is not null ||
                saved.ConsumedEvents.Length > 0 && (saved.ScriptState is null || saved.ConsumedEvents[0].Action != 1) ||
                saved.FailedAction is not null && saved.Failure is null || saved.Entered && saved.ConsumedEvents.Length == 0 ||
                saved.FailedAction is not (null or 1 or 15 or 16 or 23) ||
                entry.Definition is null && (entry.Failure != saved.Failure || saved.ScriptState is not null || saved.ConsumedEvents.Length != 0) ||
                saved.ConsumedEvents.Any(row => row.Action is not (1 or 15 or 16 or 23) || row.Commands is null ||
                    row.Commands.Any(command => command.Command is < 1 or > 4)) ||
                !restored.TryAdd(Key(saved.Map, saved.Serial), entry with
                {
                    Pose = pose,
                    State = saved.ScriptState,
                    Events = saved.ConsumedEvents,
                    Entered = saved.Entered,
                    FailedAction = saved.FailedAction,
                    Failure = saved.Failure
                }))
                throw new InvalidDataException("Saved scenery animation/event prefix is invalid or its owned source changed.");
        }
        var expected = player.Level.Objects.TopLevelObjects.Where(HasOwner).Select(row => Key(player.MapPath, row.Serial)).ToHashSet();
        if (!expected.SetEquals(restored.Where(row => row.Value.Level.Path == player.MapPath).Select(row => row.Key)))
            throw new InvalidDataException("Saved active-map scenery coverage is incomplete.");
        _entries = restored;
        // Restoration never replays source events or clears a saved failure.
    }
}
