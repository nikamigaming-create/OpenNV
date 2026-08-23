using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal partial class Fo1TacticalSession : Node
{
    private const string SaveSchema = "opennv-fo1-hex-save/v1";
    private readonly Queue<int> _movement = new();
    private bool[] _walkable = [];
    private int[] _floorIds = [];
    private IReadOnlyDictionary<int, string> _floorNames = new Dictionary<int, string>();
    private string _sceneSha256 = "";
    private string _savePath = "";
    private int _maximumActionPoints;
    private int _doorTile;
    private int _hoveredTile = -1;
    private int _selectedTile = -1;
    private int _turn = 1;
    private int _actionPoints;
    private int _playerTile;
    private MeshInstance3D _playerToken = null!;
    private MeshInstance3D _hoverMarker = null!;
    private MultiMeshInstance3D _pathMarkers = null!;
    private Label _turnLabel = null!;
    private Label _hexLabel = null!;
    private Label _statusLabel = null!;
    private string _status = "Select a highlighted floor hex to move";

    internal int PlayerTile => _playerTile;
    internal int HoveredTile => _hoveredTile;
    internal int ActionPoints => _actionPoints;
    internal int Turn => _turn;
    internal Node3D PlayerToken => _playerToken;
    internal CanvasLayer Hud { get; private set; } = null!;
    internal bool CanWalk(int tile) => tile >= 0 && tile < _walkable.Length && _walkable[tile];

    internal void Configure(
        string sceneSha256,
        bool[] walkable,
        int[] floorIds,
        IReadOnlyDictionary<int, string> floorNames,
        int entryTile,
        int doorTile,
        int actionPoints,
        string? savePath)
    {
        if (walkable.Length != Fo1HexMath.Width * Fo1HexMath.Height || floorIds.Length != 10000)
            throw new ArgumentException("Fallout tactical session received an invalid grid.");
        if (!walkable[entryTile])
            throw new InvalidOperationException($"V13ENT entry tile is not provisionally walkable: {entryTile}");
        _sceneSha256 = sceneSha256;
        _walkable = walkable;
        _floorIds = floorIds;
        _floorNames = floorNames;
        _playerTile = entryTile;
        _doorTile = doorTile;
        _maximumActionPoints = actionPoints;
        _actionPoints = actionPoints;
        _savePath = ResolvePath(savePath ?? "user://saves/fo1-v13ent-hex-v1.json");
        Name = "Fo1TacticalSession";
        Load();
        BuildWorldMarkers();
    }

    public override void _Ready()
    {
        BuildHud();
        RefreshHud();
    }

    public override void _Process(double delta)
    {
        if (_movement.Count == 0)
            return;
        var targetTile = _movement.Peek();
        var target = Fo1HexMath.Center(targetTile) + Vector3.Up * 0.38f;
        _playerToken.Position = _playerToken.Position.MoveToward(target, (float)delta * 4.0f);
        if (_playerToken.Position.DistanceTo(target) > 0.005f)
            return;
        _playerToken.Position = target;
        _movement.Dequeue();
        _playerTile = targetTile;
        _actionPoints = Math.Max(0, _actionPoints - 1);
        _status = _movement.Count == 0
            ? $"Arrived at hex {_playerTile}"
            : $"Moving: {_movement.Count} step(s) queued";
        RefreshPathMarkers();
        RefreshHud();
        Save();
    }

    internal void SetHoveredTile(int tile)
    {
        if (_hoveredTile == tile)
            return;
        _hoveredTile = tile;
        _hoverMarker.Visible = tile >= 0;
        if (tile >= 0)
        {
            _hoverMarker.Position = Fo1HexMath.Center(tile) + Vector3.Up * 0.055f;
            var material = _hoverMarker.MaterialOverride as StandardMaterial3D;
            if (material is not null)
                material.AlbedoColor = _walkable[tile]
                    ? new Color(0.35f, 1.0f, 0.28f, 0.85f)
                    : new Color(1.0f, 0.25f, 0.18f, 0.85f);
        }
        RefreshHud();
    }

    internal void SelectTile(int tile)
    {
        if (tile < 0 || tile >= _walkable.Length)
            return;
        _selectedTile = tile;
        _movement.Clear();
        if (!_walkable[tile])
        {
            _status = $"Hex {tile} has no non-default floor art; blocked in this proof";
            RefreshPathMarkers();
            RefreshHud();
            return;
        }
        var path = FindPath(_playerTile, tile);
        if (path.Count == 0 && tile != _playerTile)
        {
            _status = $"No provisional floor path to hex {tile}";
            RefreshPathMarkers();
            RefreshHud();
            return;
        }
        var allowed = Math.Min(_actionPoints, path.Count);
        foreach (var step in path.Take(allowed))
            _movement.Enqueue(step);
        _status = path.Count == 0
            ? $"Already at hex {tile}"
            : allowed == 0
                ? "No AP remaining; press Space to end turn"
                : allowed < path.Count
                    ? $"Path is {path.Count} hexes; moving {allowed} with remaining AP"
                    : $"Moving {allowed} hex(es) at 1 AP each";
        RefreshPathMarkers();
        RefreshHud();
    }

    internal void EndTurn()
    {
        _movement.Clear();
        _turn++;
        _actionPoints = _maximumActionPoints;
        _status = $"Turn {_turn}: AP restored";
        RefreshPathMarkers();
        RefreshHud();
        Save();
    }

    internal void SaveAndNotify()
    {
        Save();
        _status = $"Saved at hex {_playerTile}";
        RefreshHud();
    }

    internal void SetCameraStatus(string status)
    {
        _status = status;
        RefreshHud();
    }

    internal object Report() => new
    {
        schema = SaveSchema,
        sceneSha256 = _sceneSha256,
        playerTile = _playerTile,
        playerHex = new[] { _playerTile % 200, _playerTile / 200 },
        doorTile = _doorTile,
        turn = _turn,
        actionPoints = _actionPoints,
        maximumActionPoints = _maximumActionPoints,
        movementCostPerHex = 1,
        queuedSteps = _movement.Count,
        provisionalWalkableHexes = _walkable.Count(value => value),
        savePath = _savePath,
    };

    private List<int> FindPath(int start, int target)
    {
        if (start == target)
            return [];
        var parents = Enumerable.Repeat(-2, _walkable.Length).ToArray();
        var queue = new Queue<int>();
        parents[start] = -1;
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var neighbor in Fo1HexMath.Neighbors(current))
            {
                if (!_walkable[neighbor] || parents[neighbor] != -2)
                    continue;
                parents[neighbor] = current;
                if (neighbor == target)
                {
                    queue.Clear();
                    break;
                }
                queue.Enqueue(neighbor);
            }
        }
        if (parents[target] == -2)
            return [];
        var path = new List<int>();
        for (var current = target; current != start; current = parents[current])
            path.Add(current);
        path.Reverse();
        return path;
    }

    private void BuildWorldMarkers()
    {
        var tokenMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.10f, 0.58f, 0.95f),
            Metallic = 0.25f,
            Roughness = 0.35f,
            EmissionEnabled = true,
            Emission = new Color(0.02f, 0.18f, 0.35f),
            EmissionEnergyMultiplier = 1.4f,
        };
        _playerToken = new MeshInstance3D
        {
            Name = "VaultDwellerHexToken",
            Mesh = new CylinderMesh
            {
                TopRadius = 0.20f,
                BottomRadius = 0.27f,
                Height = 0.70f,
                RadialSegments = 16,
            },
            MaterialOverride = tokenMaterial,
            Position = Fo1HexMath.Center(_playerTile) + Vector3.Up * 0.38f,
        };
        AddChild(_playerToken);
        _hoverMarker = new MeshInstance3D
        {
            Name = "HoveredFalloutHex",
            Mesh = Fo1HexVisuals.BuildRingMesh(0.78f, 0.98f),
            MaterialOverride = Fo1HexVisuals.Material(new Color(0.35f, 1.0f, 0.28f, 0.85f), true),
            Visible = false,
        };
        AddChild(_hoverMarker);
        _pathMarkers = new MultiMeshInstance3D
        {
            Name = "QueuedFalloutHexPath",
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = Fo1HexVisuals.BuildRingMesh(0.86f, 0.94f),
            },
            MaterialOverride = Fo1HexVisuals.Material(new Color(0.95f, 0.76f, 0.18f, 0.72f), true),
        };
        AddChild(_pathMarkers);
    }

    private void RefreshPathMarkers()
    {
        var steps = _movement.ToArray();
        _pathMarkers.Multimesh.InstanceCount = steps.Length;
        for (var index = 0; index < steps.Length; index++)
            _pathMarkers.Multimesh.SetInstanceTransform(
                index,
                new Transform3D(Basis.Identity, Fo1HexMath.Center(steps[index]) + Vector3.Up * 0.04f));
    }

    private void BuildHud()
    {
        Hud = new CanvasLayer { Name = "Fo1HexHud", Layer = 50 };
        AddChild(Hud);
        var panel = new ColorRect
        {
            Position = new Vector2(18.0f, 532.0f),
            Size = new Vector2(910.0f, 170.0f),
            Color = new Color(0.012f, 0.022f, 0.018f, 0.91f),
        };
        Hud.AddChild(panel);
        var labels = new VBoxContainer
        {
            Position = new Vector2(32.0f, 542.0f),
            Size = new Vector2(875.0f, 145.0f),
        };
        Hud.AddChild(labels);
        var title = new Label { Text = "FALLOUT 1  •  V13ENT  •  200×200 HEX TACTICAL SLICE" };
        title.AddThemeColorOverride("font_color", new Color(0.96f, 0.77f, 0.28f));
        title.AddThemeFontSizeOverride("font_size", 18);
        labels.AddChild(title);
        _turnLabel = HudLabel(labels);
        _hexLabel = HudLabel(labels);
        _statusLabel = HudLabel(labels);
        var controls = HudLabel(labels);
        controls.Text = "LMB move • double-LMB focus • MMB orbit/tilt • RMB drag pan • Wheel cursor-zoom • WASD/edge pan • F player • Home route • Space end turn • F5 save";
        controls.AddThemeFontSizeOverride("font_size", 14);
    }

    private static Label HudLabel(Container parent)
    {
        var label = new Label();
        label.AddThemeColorOverride("font_color", new Color(0.68f, 0.96f, 0.48f));
        label.AddThemeFontSizeOverride("font_size", 16);
        parent.AddChild(label);
        return label;
    }

    private void RefreshHud()
    {
        if (_turnLabel is null)
            return;
        var pips = new string('●', _actionPoints) + new string('○', _maximumActionPoints - _actionPoints);
        _turnLabel.Text = $"TURN {_turn}   AP {pips}  {_actionPoints}/{_maximumActionPoints}   PLAYER HEX {_playerTile}   DOOR HEX {_doorTile}";
        var inspected = _hoveredTile >= 0 ? _hoveredTile : _selectedTile >= 0 ? _selectedTile : _playerTile;
        var floorId = _floorIds[Fo1HexMath.FloorIndex(inspected)];
        var floorName = _floorNames.GetValueOrDefault(floorId, "unknown.frm");
        _hexLabel.Text = $"CURSOR HEX {inspected} ({inspected % 200},{inspected / 200})   FLOOR {floorId} {floorName}   " +
            $"{(_walkable[inspected] ? "PROVISIONAL FLOOR" : "NO FLOOR")}";
        _statusLabel.Text = _status;
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_savePath)!);
        var document = new
        {
            schema = SaveSchema,
            sceneSha256 = _sceneSha256,
            playerTile = _playerTile,
            turn = _turn,
            actionPoints = _actionPoints,
        };
        var temporary = _savePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(document, new JsonSerializerOptions
        {
            WriteIndented = true,
        }) + System.Environment.NewLine);
        File.Move(temporary, _savePath, true);
    }

    private void Load()
    {
        if (!File.Exists(_savePath))
            return;
        using var document = JsonDocument.Parse(File.ReadAllText(_savePath));
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != SaveSchema ||
            root.GetProperty("sceneSha256").GetString() != _sceneSha256)
            throw new InvalidOperationException($"Fallout hex save does not match this scene: {_savePath}");
        var tile = root.GetProperty("playerTile").GetInt32();
        if (tile is < 0 or >= Fo1HexMath.Width * Fo1HexMath.Height || !_walkable[tile])
            throw new InvalidOperationException($"Fallout hex save contains an invalid player tile: {tile}");
        _playerTile = tile;
        _turn = Math.Max(1, root.GetProperty("turn").GetInt32());
        _actionPoints = Math.Clamp(root.GetProperty("actionPoints").GetInt32(), 0, _maximumActionPoints);
    }

    private static string ResolvePath(string path) =>
        path.StartsWith("user://", StringComparison.Ordinal)
            ? ProjectSettings.GlobalizePath(path)
            : Path.GetFullPath(path);
}
