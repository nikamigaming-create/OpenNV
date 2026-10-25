using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal sealed record ClassicActorWalk(string Map, string MapSha256, int Serial, int Pid,
    uint Fid, int Tile, int Elevation, int Rotation, int Destination, string Path, string PathSha256,
    int[] Remaining, double Fraction, int Frame, double FrameSeconds, string? Failure, bool Started);

/// <summary>Source script walking owns the live hex and FRM clock; presentation only consumes it.</summary>
internal sealed class ClassicActorMotion(ClassicPlayerSession player)
{
    private readonly Dictionary<(string Map, int Serial), ClassicActorWalk> _walks = [];
    private readonly Dictionary<string, Fallout1NativeFrmFrame[][]> _frames = new(StringComparer.Ordinal);
    internal event Action? Changed;
    internal IEnumerable<string> Failures => _walks.Values.Where(row => row.Map == player.MapPath && row.Failure is not null)
        .Select(row => $"Actor {row.Serial} walk: {row.Failure}");
    internal int Tile(string map, Fallout1NativeMapObject source) =>
        _walks.TryGetValue((map, source.Serial), out var row) ? row.Tile : source.Tile;
    internal int Rotation(string map, Fallout1NativeMapObject source) =>
        _walks.TryGetValue((map, source.Serial), out var row) ? row.Rotation :
            player.Actors?.Trait(map, source.Serial, 10, source.Rotation) ?? source.Rotation;
    internal void Rotate(string map, int serial, int rotation)
    { if (_walks.TryGetValue((map, serial), out var row)) _walks[(map, serial)] = row with { Rotation = rotation }; }
    internal ClassicActorWalk? Pose(int serial) => _walks.GetValueOrDefault((player.MapPath, serial));
    internal bool Moving(int serial) => Pose(serial) is { Remaining.Length: > 0, Failure: null };

    private Fallout1NativeFrmFrame[][] Frames(string path, string hash)
    {
        if (_frames.TryGetValue(path, out var result)) return result;
        var bytes = player.Catalog.Read(path, out _);
        if (ClassicPremadeReader.Hash(bytes) != hash) throw new InvalidDataException("Actor walk art changed.");
        var first = Fallout1NativeFrmReader.ReadFirstFrame(bytes);
        if (first.StoredFps == 0 || first.FramesPerDirection <= 0)
            throw new NotSupportedException("Actor walk requires an authored frame clock.");
        result = Enumerable.Range(0, 6).Select(direction => Enumerable.Range(0, first.FramesPerDirection)
            .Select(frame => Fallout1NativeFrmReader.ReadFrame(bytes, direction, frame)).ToArray()).ToArray();
        if (result.Any(direction => direction.Sum(frame => Math.Abs((int)frame.FrameX)) == 0 ||
            direction.Any(frame => frame.StoredFps != first.StoredFps || frame.FramesPerDirection != first.FramesPerDirection)))
            throw new InvalidDataException("Actor walk directions have inconsistent clocks or no source displacement.");
        _frames.Add(path, result); return result;
    }

    internal IReadOnlySet<int> Walkable(Fallout1NativeMapObject mover)
    {
        var level = player.Level;
        var navigation = new ClassicMapNavigation(level.Map, level.Objects, mover.Elevation,
            source => source.Prototype.ObjectType == 1 ? false : player.Doors.Blocks(source));
        var blocked = navigation.Blocked.ToHashSet();
        foreach (var actor in level.Objects.TopLevelObjects.Where(source => source.Serial != mover.Serial &&
            source.Elevation == mover.Elevation && source.Prototype.ObjectType == 1 && (source.Flags & 0x11) == 0))
        {
            if (player.Combat.Dead(actor.Serial)) continue;
            var tile = Tile(level.Path, actor); blocked.Add(tile);
            if ((actor.Flags & 0x800) != 0) blocked.UnionWith(ClassicHexGrid.Neighbors(tile));
        }
        if (player.Elevation == mover.Elevation) blocked.Add(player.Tile);
        return navigation.FloorBacked.Where(tile => !blocked.Contains(tile) &&
            ((mover.Flags & 0x800) == 0 || ClassicHexGrid.Neighbors(tile).All(neighbor =>
                navigation.FloorBacked.Contains(neighbor) && !blocked.Contains(neighbor)))).ToHashSet();
    }

    internal Action Prepare(IReadOnlyList<ClassicIntMovement> movements)
    {
        if (movements.Count == 0) return () => { };
        if (player.Combat.Active != false)
            throw new NotSupportedException("Script walk during combat requires its AP/turn movement owner.");
        var prepared = new Dictionary<(string, int), ClassicActorWalk>();
        foreach (var command in movements)
        {
            var source = player.Level.Objects.TopLevelObjects.SingleOrDefault(row => row.Serial + 1 == command.ObjectHandle)
                ?? throw new InvalidDataException("Script walk target is absent from the active MAP.");
            if (source.Prototype.ObjectType != 1 || source.Elevation != player.Elevation ||
                (source.Flags & 1) != 0 || command.Speed != 0 || command.DestinationElevation is not null || command.DestinationTile is < 0 or >= ClassicHexGrid.Count)
                throw new NotSupportedException("Script movement requires a visible same-elevation critter walk; run/teleport remain separate.");
            if (ClassicCritterInstance.Read(source) is { Results: not 0 } or { Dead: true } || ((source.Fid >> 16) & 0xff) != 0)
                throw new NotSupportedException("Actor status or stored action requires its own locomotion admission.");
            var key = (player.MapPath, source.Serial);
            if (prepared.ContainsKey(key) || Moving(source.Serial))
                throw new NotSupportedException("Replacing an active actor animation sequence requires its cancellation owner.");
            var tile = Tile(player.MapPath, source);
            if (command.SourceTile != tile || player.Combat.Dead(source.Serial))
                throw new InvalidOperationException("Actor walk source pose is stale or dead.");
            var fid = (source.Fid & ~0x00ff0000u) | 0x00010000u;
            var path = ClassicArtCache.ResolveCritter(path => player.Catalog.Read(path, out _), fid, 0).Path;
            if (!path.EndsWith(".frm", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Split-direction walking requires a retained per-direction art identity.");
            var bytes = player.Catalog.Read(path, out _); var hash = ClassicPremadeReader.Hash(bytes);
            Frames(path, hash);
            var walkable = Walkable(source);
            var route = ClassicHexGrid.Path(tile, command.DestinationTile, walkable);
            var failure = tile != command.DestinationTile && route.Length == 0 ? "No legal source hex path to the requested tile." : null;
            prepared.Add(key, new(player.MapPath, player.Level.Sha256, source.Serial, source.Pid, source.Fid,
                tile, source.Elevation, route.Length == 0 ? Rotation(player.MapPath, source) : ClassicHexGrid.Rotation(tile, route[0]),
                command.DestinationTile, path, hash, route, 0, 0, 0, failure, false));
        }
        return () => { foreach (var (key, value) in prepared) _walks[key] = value; Changed?.Invoke(); };
    }

    internal void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        foreach (var key in _walks.Keys.Where(key => key.Map == player.MapPath).ToArray())
        {
            var row = _walks[key];
            if (row.Failure is not null || row.Remaining.Length == 0) continue;
            if (!row.Started) { _walks[key] = row with { Started = true }; continue; }
            if (player.Combat.Dead(row.Serial))
            { _walks[key] = row with { Remaining = [], Fraction = 0 }; Changed?.Invoke(); continue; }
            if (player.Combat.Active != false) continue;
            var source = player.Level.Objects.TopLevelObjects.Single(item => item.Serial == row.Serial);
            var frames = Frames(row.Path, row.PathSha256); var elapsed = row.FrameSeconds + seconds;
            while (elapsed >= 1.0 / frames[row.Rotation][row.Frame].StoredFps && row.Remaining.Length != 0)
            {
                var frame = frames[row.Rotation][row.Frame]; elapsed -= 1.0 / frame.StoredFps;
                var fraction = row.Fraction + Math.Abs((int)frame.FrameX) / (row.Rotation is 1 or 4 ? 32.0 : 16.0);
                if (!Walkable(source).Contains(row.Remaining[0]))
                { row = row with { Failure = "A live source blocker now occupies the next hex." }; break; }
                while (fraction >= 1 && row.Remaining.Length != 0)
                {
                    row = row with { Tile = row.Remaining[0], Remaining = row.Remaining[1..] }; fraction -= 1;
                    if (row.Remaining.Length != 0) row = row with { Rotation = ClassicHexGrid.Rotation(row.Tile, row.Remaining[0]) };
                    _walks[key] = row; player.Combat.PublishActorLocation(row.Serial, row.Tile, row.Rotation); Changed?.Invoke();
                    if (row.Remaining.Length != 0 && !Walkable(source).Contains(row.Remaining[0]))
                    { row = row with { Failure = "A live source blocker now occupies the next hex." }; break; }
                }
                row = row with
                {
                    Fraction = row.Remaining.Length == 0 || row.Failure is not null ? 0 : fraction,
                    Frame = row.Remaining.Length == 0 ? 0 : (row.Frame + 1) % frames[row.Rotation].Length
                };
                if (row.Failure is not null) break;
            }
            _walks[key] = row with { FrameSeconds = row.Remaining.Length == 0 ? 0 : elapsed };
        }
    }

    internal ClassicActorWalk[] Save() => _walks.Values.Select(row => row with { Remaining = [.. row.Remaining] }).ToArray();
    internal void Restore(ClassicActorWalk[] saved)
    {
        foreach (var row in saved)
        {
            var level = player.Catalog.Load(row.Map);
            var source = level.Objects.TopLevelObjects.Single(item => item.Serial == row.Serial);
            var frames = Frames(row.Path, row.PathSha256);
            var expectedPath = ClassicArtCache.ResolveCritter(path => player.Catalog.Read(path, out _),
                (source.Fid & ~0x00ff0000u) | 0x00010000u, 0).Path;
            if (level.Sha256 != row.MapSha256 || source.Prototype.ObjectType != 1 || source.Pid != row.Pid || source.Fid != row.Fid || source.Elevation != row.Elevation ||
                row.Path != expectedPath || row.Remaining is null || row.Destination is < 0 or >= ClassicHexGrid.Count ||
                row.Tile is < 0 or >= ClassicHexGrid.Count || row.Rotation is < 0 or >= 6 ||
                !double.IsFinite(row.Fraction) || row.Fraction is < 0 or >= 1 || !double.IsFinite(row.FrameSeconds) ||
                row.FrameSeconds < 0 || row.Frame < 0 || row.Frame >= frames[row.Rotation].Length ||
                row.FrameSeconds >= 1.0 / frames[row.Rotation][row.Frame].StoredFps && row.Failure is null ||
                !row.Started && (row.Fraction != 0 || row.Frame != 0 || row.FrameSeconds != 0) ||
                row.Remaining.Length == 0 && (row.Fraction != 0 || row.Frame != 0 || row.FrameSeconds != 0) ||
                row.Remaining.Length != 0 && row.Rotation != ClassicHexGrid.Rotation(row.Tile, row.Remaining[0]) ||
                row.Failure is null && (row.Remaining.Length == 0 ? row.Tile : row.Remaining[^1]) != row.Destination ||
                row.Remaining.Prepend(row.Tile).Zip(row.Remaining).Any(pair => ClassicHexGrid.Distance(pair.First, pair.Second) != 1) ||
                !_walks.TryAdd((row.Map, row.Serial), row with { Remaining = [.. row.Remaining] }))
                throw new InvalidDataException("Actor walk source, clock or route receipt is invalid.");
        }
    }
}
