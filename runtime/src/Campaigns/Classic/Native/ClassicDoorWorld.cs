using System.Buffers.Binary;
using System.Text.Json;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal sealed record ClassicDoorDefinition(ClassicMapLevel Level, Fallout1NativeMapObject Object, string Art,
    string Identity, int Frames, int ActionFrame, int Fps, bool CanUse, string? Script, ClassicIntProgram? Program);
internal sealed record ClassicDoorPose(bool Open, bool Locked, int Frame, int Direction = 0, double Elapsed = 0)
{
    internal bool Blocked(ClassicDoorDefinition source) => Direction switch
    {
        1 => Frame < source.ActionFrame,
        -1 => Frame <= source.ActionFrame,
        _ => !Open,
    };
}
internal sealed record ClassicDoorSave(string Map, string MapSha256, int Serial, int Pid, string Identity,
    ClassicDoorPose Pose, ClassicIntProcedureState? ScriptState, string? Failure);
internal sealed record ClassicDoorWorldSave(ClassicDoorSave[] Doors, IReadOnlyDictionary<string, int> Externals,
    IReadOnlyDictionary<int, int> Globals, IReadOnlyDictionary<string, Dictionary<int, int>> MapVariables);

/// <summary>Live source doors, their INT events and FRM clock. No donor collision or animation owns passage.</summary>
internal sealed class ClassicDoorWorld(ClassicPlayerSession player)
{
    private sealed record Entry(ClassicDoorDefinition Definition, ClassicDoorPose Pose, ClassicIntProcedureState? State, string? Failure);
    private readonly Dictionary<string, Entry> _entries = [];
    private readonly ClassicNativeScriptMessages _messages = new(path => player.Catalog.Read(path, out _));
    private readonly Queue<string> _pendingMessages = [];
    private Dictionary<string, int> _externals = new(StringComparer.Ordinal);
    private Dictionary<int, int> _globals = new(player.Initialization?.ScriptState?.GlobalVariables ??
        ClassicCampaignInitialization.Variables(player.Catalog.Read("data/vault13.gam", out _), "GAME_GLOBAL_VARS"));
    private Dictionary<string, Dictionary<int, int>> _mapVariables = player.Initialization?.ScriptState is { } initial
        ? new(StringComparer.Ordinal) { [player.Initialization.Map] = new(initial.MapVariables) } : new(StringComparer.Ordinal);
    internal event Action<int>? FrameChanged;
    internal event Action? PassageChanged;
    internal IReadOnlyList<string> Failures => Current.Where(row => row.Failure is not null).Select(row => row.Failure!).ToArray();
    private IEnumerable<Entry> Current => _entries.Values.Where(row => row.Definition.Level.Path == player.MapPath && row.Definition.Object.Elevation == player.Elevation);
    internal IReadOnlyList<int> Serials => Current.Select(row => row.Definition.Object.Serial).ToArray();
    internal static bool IsDoor(Fallout1NativeMapObject row) => row.Prototype is { ObjectType: 2, Subtype: 0 };
    private static string Key(string map, int serial) => map + ":" + serial;
    private Entry Get(int serial) => _entries.TryGetValue(Key(player.MapPath, serial), out var entry) ? entry : throw new InvalidDataException("Door is absent from the active map.");
    internal ClassicDoorDefinition Definition(int serial) => Get(serial).Definition;
    internal ClassicDoorPose Pose(int serial) => Get(serial).Pose;
    internal string[] TakeMessages()
    {
        var result = _pendingMessages.ToArray(); _pendingMessages.Clear(); return result;
    }
    internal bool? Blocks(Fallout1NativeMapObject row) => BlocksAt(player.MapPath, row);
    internal bool? BlocksAt(string map, Fallout1NativeMapObject row) => _entries.TryGetValue(Key(map, row.Serial), out var entry)
        ? entry.Failure is not null || entry.Pose.Blocked(entry.Definition) : null;

    internal void Enter()
    {
        var level = player.Level;
        // Preserve MAP script-list ordering for source door entry events.
        var scriptOrder = level.Map.LiveScripts.Select((row, index) => (row.ScriptId, index)).ToDictionary(row => row.ScriptId, row => row.index);
        var pending = new List<string>();
        foreach (var door in level.Objects.TopLevelObjects.Where(IsDoor).OrderBy(row => scriptOrder.GetValueOrDefault(row.ScriptId, int.MaxValue)).ThenBy(row => row.Serial))
        {
            var key = Key(level.Path, door.Serial);
            if (_entries.ContainsKey(key)) continue;
            var definition = ReadDefinition(level, door);
            var pose = new ClassicDoorPose(door.Frame != 0, (door.InstanceValues[0] & 0x02000000) != 0, door.Frame);
            var entry = new Entry(definition, pose, null, null);
            _entries.Add(key, entry); pending.Add(key);
        }
        // All source handles and door states exist before the first script can
        // refer to another door later in the MAP object section.
        foreach (var key in pending)
        {
            var entry = _entries[key]; var definition = entry.Definition; var door = definition.Object;
            if (definition.Program is null) continue;
            try
            {
                var state = InitialState(definition);
                var result = Execute(entry with { State = state }, definition.Program.StartupProcedure!, 0);
                Validate(result);
                // Validate startup and entry together before publishing either.
                var startup = entry with { State = result.State };
                var entered = definition.Program.Procedures.ContainsKey("map_enter_p_proc")
                    ? Execute(startup, "map_enter_p_proc", 15, result.WorldObjects, result.ExternalVariables) : result;
                Validate(entered);
                if (!ReferenceEquals(entered, result)) entered = entered with { MessageEffects = result.MessageEffects.Concat(entered.MessageEffects).ToArray() };
                Commit(entry, entered);
            }
            catch (Exception error) when (error is InvalidOperationException or InvalidDataException or NotSupportedException or FileNotFoundException)
            { _entries[key] = entry with { Failure = $"{level.Path}:{door.Serial} {definition.Script}: {error.Message}" }; }
        }
    }

    private ClassicDoorDefinition ReadDefinition(ClassicMapLevel level, Fallout1NativeMapObject row)
    {
        if (row.InstanceValues.Count != 1) throw new InvalidDataException("Door instance layout is invalid.");
        var pro = player.Catalog.Read(row.Prototype.LogicalPath!, out _);
        var art = Fallout1NativePrototypeReader.ResolveArt(player.Catalog, row.Fid);
        var bytes = player.Catalog.Read(art, out _);
        var frame = Fallout1NativeFrmReader.ReadFrame(bytes, row.Rotation, row.Frame);
        var action = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(6));
        if (frame.FramesPerDirection < 2 || action >= frame.FramesPerDirection) throw new InvalidDataException("Door FRM action/frame extent is invalid.");
        // Validate the complete source animation before the object can open.
        for (var index = 0; index < frame.FramesPerDirection; index++) _ = Fallout1NativeFrmReader.ReadFrame(bytes, row.Rotation, index);
        string? path = null; byte[]? scriptBytes = null; ClassicIntProgram? program = null;
        if (row.ScriptId != uint.MaxValue)
        {
            var slot = level.Map.LiveScripts.SingleOrDefault(slot => slot.ScriptId == row.ScriptId)
                ?? throw new InvalidDataException("Placed door has no live MAP script slot.");
            if (slot.ObjectId != row.ObjectId) throw new InvalidDataException("MAP script owner does not match its door.");
            var names = Fallout1NativeLists.Read(player.Catalog.Read("scripts/scripts.lst", out _));
            if (slot.ScriptProgramIndex < 0 || slot.ScriptProgramIndex >= names.Count) throw new InvalidDataException("Door script index exceeds scripts.lst.");
            path = ClassicMapCatalog.Canonical("scripts/" + Path.ChangeExtension(names[slot.ScriptProgramIndex], ".int"));
            scriptBytes = player.Catalog.Read(path, out _); program = ClassicNativeIntReader.Read(scriptBytes, path);
        }
        var identity = ClassicPremadeReader.Hash(JsonSerializer.SerializeToUtf8Bytes(new
        { pro = ClassicPremadeReader.Hash(pro), art = ClassicPremadeReader.Hash(bytes), script = scriptBytes is null ? null : ClassicPremadeReader.Hash(scriptBytes) }));
        return new(level, row, art, identity, frame.FramesPerDirection, action, frame.StoredFps == 0 ? 10 : frame.StoredFps,
            (BinaryPrimitives.ReadUInt32BigEndian(pro.AsSpan(24)) & 0x800) != 0, path, program);
    }

    private ClassicIntProcedureState InitialState(ClassicDoorDefinition definition)
    {
        if (!_mapVariables.TryGetValue(definition.Level.Path, out var variables))
            _mapVariables.Add(definition.Level.Path, variables = ClassicCampaignInitialization.MapVariables(player.Catalog.Read(definition.Level.Path, out _)));
        var locals = new Dictionary<int, int>();
        var slot = definition.Level.Map.LiveScripts.Single(row => row.ScriptId == definition.Object.ScriptId);
        var bytes = player.Catalog.Read(definition.Level.Path, out _);
        var localOffset = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(slot.SourceOffset + 24));
        var count = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(slot.SourceOffset + 28));
        var total = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(32));
        if (count < 0 || localOffset < -1 || count > 0 && (localOffset < 0 || localOffset > total - count)) throw new InvalidDataException("Door script locals exceed MAP variables.");
        for (var index = 0; index < count; index++)
            locals[index] = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(0xec + (variables.Count + localOffset + index) * 4));
        return new(definition.Program!.InitialVariables, new Dictionary<int, int>(), locals, variables, _globals, [], null);
    }

    private ClassicIntProcedureResult Execute(Entry entry, string procedure, int action,
        ClassicIntWorldObjectState? prior = null, IReadOnlyDictionary<string, int>? externals = null)
    {
        var level = entry.Definition.Level;
        var objects = level.Objects.TopLevelObjects.ToDictionary(row => row.Serial + 1,
            row => new ClassicIntWorldObject(row.Serial + 1, row.Pid, row.Tile, row.Elevation, (row.Flags & 1) == 0));
        const int dude = int.MaxValue;
        objects.Add(dude, new(dude, 0x01000000, player.Tile, player.Elevation, true));
        var doors = _entries.Values.Where(row => row.Definition.Level.Path == level.Path).ToDictionary(row => row.Definition.Object.Serial + 1,
            row => new ClassicIntDoorObjectState(row.Pose.Open, row.Pose.Locked));
        var world = prior ?? ClassicIntWorldObjectState.Empty with { Objects = objects, Doors = doors };
        var state = entry.State! with { GlobalVariables = _globals!, MapVariables = _mapVariables[level.Path] };
        if (prior is not null) state = entry.State!;
        var stats = Enumerable.Range(0, 7).ToDictionary(stat => (dude, stat), stat => player.Stats.Special[stat]);
        stats[(dude, 34)] = player.Choice.Character.Female ? 1 : 0;
        var game = new ClassicIntExpressionContext(state.ProgramVariables, state.LocalVariables, state.ScriptLocalVariables,
            state.MapVariables, state.GlobalVariables, dude, entry.Definition.Object.Serial + 1, null, null,
            stats, new Dictionary<(int, int), int>(), new Dictionary<int, int>(),
            new Dictionary<(int, int), int>(), externals ?? _externals, null, null, null,
            new ClassicIntObjectHandleTable(new Dictionary<ClassicIntObjectCreation, int>()), SourceObject: dude,
            CurrentMapIndex: level.Map.MapIndex, ScriptAction: action, MessageSource: _messages);
        var result = ClassicIntProcedureVm.Execute(entry.Definition.Program!, procedure, state, game, world, null, 100000);
        if (!result.WorldObjects.Objects.OrderBy(row => row.Key).SequenceEqual(world.Objects.OrderBy(row => row.Key)))
            throw new NotSupportedException("Door event changes a world object's placement or visibility without its runtime owner.");
        return result;
    }

    private static void Validate(ClassicIntProcedureResult result)
    {
        var world = result.WorldObjects;
        if (result.State.ValueStack.Count != 0 || result.MessageEffects.Any(row => row.Text is null || row.ObjectHandle is not null || row.Color is not null) || result.SoundEffects.Count != 0 ||
            world.CreatedObjects.Count != 0 || world.Inventory.Count != 0 || world.MapStartOverride is not null ||
            world.AttackRequests.Count != 0 || world.Movements.Count != 0 || world.TraitAssignments.Count != 0 ||
            world.Timers.Pending.Count != 0 || world.DialogueSystemEntered || world.DialogueStart is not null || world.LightLevel is not null)
            throw new NotSupportedException("Door event reached an effect whose runtime owner is still unavailable.");
    }

    private void Commit(Entry entry, ClassicIntProcedureResult result)
    {
        // A script is one transaction: validate every target before applying
        // its locals, locks, map/global writes and open/close requests.
        var updates = new List<(string Key, Entry Entry)>();
        foreach (var (handle, state) in result.WorldObjects.Doors)
        {
            var key = Key(entry.Definition.Level.Path, handle - 1);
            var target = _entries[key];
            if (state.Open != target.Pose.Open && !state.Open) CheckClear(target.Definition.Object);
            var pose = target.Pose with { Locked = state.Locked };
            if (state.Open != pose.Open) pose = pose with { Open = state.Open, Direction = state.Open ? 1 : -1, Elapsed = 0 };
            updates.Add((key, target with { Pose = pose }));
        }
        foreach (var (key, update) in updates) _entries[key] = update;
        var sourceKey = Key(entry.Definition.Level.Path, entry.Definition.Object.Serial);
        _entries[sourceKey] = _entries[sourceKey] with { State = result.State, Failure = null };
        _globals = new(result.State.GlobalVariables); _mapVariables[entry.Definition.Level.Path] = new(result.State.MapVariables);
        _externals = new(result.ExternalVariables, StringComparer.Ordinal);
        foreach (var message in result.MessageEffects) _pendingMessages.Enqueue(message.Text!);
        PassageChanged?.Invoke();
    }

    internal void Use(int serial)
    {
        var entry = Get(serial); var source = entry.Definition.Object;
        if (player.Moving || source.Elevation != player.Elevation || ClassicHexGrid.Distance(player.Tile, source.Tile) > 1 || (source.Flags & 1) != 0)
            throw new InvalidOperationException("Move next to the door first.");
        if (entry.Failure is not null) throw new NotSupportedException(entry.Failure);
        if (entry.Pose.Direction != 0) throw new InvalidOperationException("The door is moving.");
        if (entry.Definition.Program is { } program && program.Procedures.ContainsKey("use_p_proc"))
        {
            var result = Execute(entry, "use_p_proc", 6); Validate(result); Commit(entry, result);
            if (result.WorldObjects.ScriptOverrides) return;
            entry = Get(serial);
        }
        if (!entry.Definition.CanUse) throw new InvalidOperationException("This door requires its control or another source interaction.");
        if (entry.Pose.Locked) throw new InvalidOperationException("The door is locked.");
        if (entry.Pose.Open) CheckClear(source);
        _entries[Key(player.MapPath, serial)] = entry with { Pose = entry.Pose with { Open = !entry.Pose.Open, Direction = entry.Pose.Open ? -1 : 1, Elapsed = 0 } };
        FrameChanged?.Invoke(serial); PassageChanged?.Invoke();
    }

    internal void Examine(int serial)
    {
        var entry = Get(serial); var source = entry.Definition.Object;
        if (source.Elevation != player.Elevation || (source.Flags & 1) != 0)
            throw new InvalidOperationException("That door is not visible on this level.");
        if (entry.Failure is not null) throw new NotSupportedException(entry.Failure);
        if (entry.Definition.Program is { } program && program.Procedures.ContainsKey("description_p_proc"))
        {
            var result = Execute(entry, "description_p_proc", 3); Validate(result); Commit(entry, result);
            if (result.WorldObjects.ScriptOverrides) return;
        }
        var pro = player.Catalog.Read(source.Prototype.LogicalPath!, out _);
        var id = BinaryPrimitives.ReadInt32BigEndian(pro.AsSpan(4));
        var messages = ClassicNativeScriptMessages.ReadMessages(player.Catalog.Read("text/english/game/pro_scen.msg", out _));
        if (!messages.TryGetValue(checked(id + 1), out var description))
            throw new InvalidDataException("Door prototype description is absent from its source message list.");
        _pendingMessages.Enqueue(description);
    }

    private void CheckClear(Fallout1NativeMapObject door)
    {
        var footprint = ((door.Flags & 0x800) != 0 ? ClassicHexGrid.Neighbors(door.Tile).Append(door.Tile) : [door.Tile]).ToHashSet();
        if (door.Elevation == player.Elevation && (footprint.Contains(player.Tile) || player.Moving && footprint.Contains(player.NextTile)) ||
            player.Level.Objects.TopLevelObjects.Any(row => row.Serial != door.Serial && row.Elevation == door.Elevation &&
                row.Prototype.ObjectType is 1 or 2 or 3 && (row.Flags & 0x11) == 0 &&
                (footprint.Contains(row.Tile) || (row.Flags & 0x800) != 0 && ClassicHexGrid.Neighbors(row.Tile).Any(footprint.Contains))))
            throw new InvalidOperationException("The doorway is blocked.");
    }

    internal void Advance(double delta)
    {
        if (delta < 0 || !double.IsFinite(delta)) throw new ArgumentOutOfRangeException(nameof(delta));
        foreach (var entry in Current.Where(row => row.Pose.Direction != 0).ToArray())
        {
            var pose = entry.Pose; var elapsed = pose.Elapsed + delta; var frame = pose.Frame;
            while (elapsed >= 1.0 / entry.Definition.Fps && frame != (pose.Open ? entry.Definition.Frames - 1 : 0))
            { elapsed -= 1.0 / entry.Definition.Fps; frame += pose.Direction; }
            var done = frame == (pose.Open ? entry.Definition.Frames - 1 : 0);
            var next = pose with { Frame = frame, Direction = done ? 0 : pose.Direction, Elapsed = done ? 0 : elapsed };
            _entries[Key(player.MapPath, entry.Definition.Object.Serial)] = entry with { Pose = next };
            if (frame != pose.Frame) FrameChanged?.Invoke(entry.Definition.Object.Serial);
            if (pose.Blocked(entry.Definition) != next.Blocked(entry.Definition)) PassageChanged?.Invoke();
        }
    }

    internal ClassicDoorWorldSave Save() => new(_entries.Values.Select(row => new ClassicDoorSave(row.Definition.Level.Path,
        row.Definition.Level.Sha256, row.Definition.Object.Serial, row.Definition.Object.Pid, row.Definition.Identity, row.Pose, row.State, row.Failure)).ToArray(),
        _externals, _globals, _mapVariables);

    internal void Restore(ClassicDoorWorldSave save)
    {
        var restored = new Dictionary<string, Entry>();
        foreach (var row in save.Doors)
        {
            var level = player.Catalog.Load(row.Map);
            var source = level.Objects.TopLevelObjects.SingleOrDefault(item => item.Serial == row.Serial && item.Pid == row.Pid && IsDoor(item));
            if (level.Path != row.Map || level.Sha256 != row.MapSha256 || source is null) throw new InvalidDataException("Saved door source map changed.");
            var definition = ReadDefinition(level, source); var pose = row.Pose;
            if (definition.Identity != row.Identity || pose.Frame < 0 || pose.Frame >= definition.Frames || pose.Direction is < -1 or > 1 ||
                !double.IsFinite(pose.Elapsed) || pose.Elapsed < 0 || pose.Elapsed >= 1.0 / definition.Fps ||
                pose.Direction == 0 && (pose.Frame != (pose.Open ? definition.Frames - 1 : 0) || pose.Elapsed != 0) ||
                pose.Direction != 0 && pose.Direction != (pose.Open ? 1 : -1) || row.ScriptState?.ValueStack.Count > 0 ||
                row.ScriptState?.RandomState is not null || (definition.Program is null && row.ScriptState is not null) ||
                (definition.Program is not null && row.ScriptState is null && row.Failure is null) ||
                !restored.TryAdd(Key(row.Map, row.Serial), new(definition, pose, row.ScriptState, row.Failure)))
                throw new InvalidDataException("Saved door/script state no longer matches its source.");
        }
        _entries.Clear(); foreach (var (key, entry) in restored) _entries.Add(key, entry);
        _externals = new(save.Externals, StringComparer.Ordinal); _globals = new(save.Globals);
        _mapVariables = save.MapVariables.ToDictionary(row => row.Key, row => new Dictionary<int, int>(row.Value), StringComparer.Ordinal);
        Enter(); PassageChanged?.Invoke();
    }
}
