using System.Text.Json;
using Godot;

using OpenNV.Runtime.Campaigns.Classic;

namespace OpenNV.Runtime.Campaigns.Fallout1;

internal sealed record Fo1CampaignAdjacentState(
    string CampaignSha256,
    string JoinCatalogSha256,
    int MapIndex,
    string MapName,
    string MapSha256,
    int Elevation,
    int Tile,
    int Rotation);

internal sealed partial class Fo1CampaignAdjacentRuntime : Node3D
{
    private Fo1CampaignPresentationCatalog _presentation = null!;
    private ClassicAdjacentMapCatalog _joins = null!;
    private Fo1CampaignPresentationViewer _viewer = null!;
    private string _savePath = "";
    private Fo1CampaignAdjacentState _state = null!;
    private IReadOnlySet<int> _walkable = new HashSet<int>();

    internal Fo1CampaignPresentationViewer Viewer => _viewer;
    internal Fo1CampaignAdjacentState State => _state;

    internal Fo1CampaignMapViewCoverage Configure(
        Fo1CampaignPresentationCatalog presentation,
        ClassicAdjacentMapCatalog joins,
        string initialMap,
        int? initialElevation,
        string savePath)
    {
        _presentation = presentation;
        _joins = joins;
        _savePath = ResolveSavePath(savePath);
        _viewer = new Fo1CampaignPresentationViewer();
        AddChild(_viewer);
        var coverage = _viewer.Configure(
            presentation, initialMap, initialElevation, includeSourcePlayer: false);
        var map = _viewer.CurrentMap;
        var entry = map.Entry;
        _state = new Fo1CampaignAdjacentState(
            presentation.CampaignSha256,
            joins.Sha256,
            RequireJoinedMap(map).MapIndex,
            map.SourceFile,
            map.MapSha256,
            coverage.Elevation,
            entry.Tile,
            entry.Rotation);
        if (File.Exists(_savePath))
            _state = ReadSave();
        return ApplyState();
    }

    internal bool TryMoveTo(int targetTile)
    {
        var path = FindPath(_state.Tile, targetTile);
        if (path.Count == 0 && targetTile != _state.Tile)
            return false;
        foreach (var tile in path)
        {
            var rotation = Enumerable.Range(0, Fo1HexMath.DirectionCount)
                .Single(direction => Fo1HexMath.TileInDirection(_state.Tile, direction) == tile);
            _state = _state with { Tile = tile, Rotation = rotation };
            _viewer.SetPlayablePlayer(tile, rotation);
            var committed = _joins.TryCommitAt(
                _state.MapIndex,
                _state.MapSha256,
                tile,
                _state.Elevation);
            if (committed is not null)
            {
                _state = new Fo1CampaignAdjacentState(
                    _state.CampaignSha256,
                    _state.JoinCatalogSha256,
                    committed.Join.Destination.MapIndex,
                    committed.Join.Destination.MapName ?? throw new InvalidOperationException(
                        "Fallout adjacent destination MAP name is absent."),
                    committed.Join.Destination.MapSha256,
                    committed.Join.Destination.Elevation ?? throw new InvalidOperationException(
                        "Fallout adjacent destination elevation is absent."),
                    committed.Join.Destination.Tile,
                    committed.Join.Destination.Rotation ?? throw new InvalidOperationException(
                        "Fallout adjacent destination rotation is absent."));
                ApplyState();
                break;
            }
        }
        WriteSave();
        return true;
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventMouseButton
            {
                ButtonIndex: MouseButton.Left,
                Pressed: true,
                DoubleClick: false,
            } mouse)
        {
            var tile = _viewer.TileAtScreen(mouse.Position);
            if (tile >= 0 && TryMoveTo(tile))
                GetViewport().SetInputAsHandled();
        }
    }

    private Fo1CampaignMapViewCoverage ApplyState()
    {
        if (_state.CampaignSha256 != _presentation.CampaignSha256 ||
            _state.JoinCatalogSha256 != _joins.Sha256)
            throw new InvalidOperationException(
                "Fallout adjacent save provenance differs from active catalogs.");
        var endpoint = _joins.RequireMap(_state.MapName, _state.MapSha256);
        if (endpoint.MapIndex != _state.MapIndex)
            throw new InvalidOperationException("Fallout adjacent saved MAP index drifted.");
        var coverage = _viewer.LoadPlayableMap(
            _state.MapName,
            _state.Elevation,
            _state.Tile,
            _state.Rotation);
        if (_viewer.CurrentMap.MapSha256 != _state.MapSha256)
            throw new InvalidOperationException(
                "Fallout adjacent presentation/source MAP identity drifted.");
        _walkable = _viewer.Walkable(_viewer.CurrentElevation);
        return coverage;
    }

    private ClassicMapEndpoint RequireJoinedMap(Fo1CampaignMapPresentation map) =>
        _joins.RequireMap(map.SourceFile, map.MapSha256);

    private List<int> FindPath(int start, int target)
    {
        if (!_walkable.Contains(start) || !_walkable.Contains(target))
            return [];
        var frontier = new Queue<int>();
        var previous = new Dictionary<int, int>();
        frontier.Enqueue(start);
        previous[start] = start;
        while (frontier.Count > 0 && !previous.ContainsKey(target))
        {
            var current = frontier.Dequeue();
            foreach (var neighbor in Fo1HexMath.Neighbors(current))
            {
                if (!_walkable.Contains(neighbor) || previous.ContainsKey(neighbor))
                    continue;
                previous[neighbor] = current;
                frontier.Enqueue(neighbor);
            }
        }
        if (!previous.ContainsKey(target))
            return [];
        var result = new List<int>();
        for (var tile = target; tile != start; tile = previous[tile])
            result.Add(tile);
        result.Reverse();
        return result;
    }

    private Fo1CampaignAdjacentState ReadSave()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(_savePath));
        var source = document.RootElement;
        if (source.GetProperty("schema").GetString() !=
            "opennv-fo1-campaign-adjacent-save/v1")
            throw new InvalidOperationException("Unexpected Fallout adjacent save schema.");
        return JsonSerializer.Deserialize<Fo1CampaignAdjacentState>(
            source.GetProperty("state").GetRawText()) ??
            throw new InvalidOperationException("Fallout adjacent save state is absent.");
    }

    private void WriteSave()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_savePath)!);
        var temporary = _savePath + ".tmp";
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                schema = "opennv-fo1-campaign-adjacent-save/v1",
                state = _state,
            },
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllBytes(temporary, payload);
        File.Move(temporary, _savePath, true);
    }

    private static string ResolveSavePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Fallout adjacent save path is empty.", nameof(value));
        var path = value.StartsWith("user://", StringComparison.Ordinal)
            ? ProjectSettings.GlobalizePath(value)
            : Path.GetFullPath(value);
        var root = Path.GetFullPath(Path.Combine(
            System.Environment.GetFolderPath(
                System.Environment.SpecialFolder.LocalApplicationData),
            "OpenNV"));
        if (!path.StartsWith(
                root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Fallout adjacent saves must stay in OpenNV user data.");
        return path;
    }
}
