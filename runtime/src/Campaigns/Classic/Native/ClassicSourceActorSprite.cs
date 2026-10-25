using Godot;
using OpenNV.Runtime.Campaigns.Fallout1;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

/// <summary>Live original critter art retains its source identity, facing, pose and authored idle clock.</summary>
internal sealed partial class ClassicSourceActorSprite : Sprite3D
{
    private ClassicArtCache _art = null!;
    private ClassicWorldPreview _world = null!;
    private Fallout1NativeMapObject _placed = null!;
    private string _path = "";
    private int _frame, _frames;
    private ushort _fps;
    private double _clock;
    private bool _idle;
    private ClassicPlayerBody? _body;
    private ClassicCreatureBody? _creature;
    private ClassicCombatEncounter? _combat;
    private ClassicActorMotion? _motion;
    private Node3D? _analog;
    private Vector3 _sourcePosition, _analogPosition;
    private bool _wasWalking;
    private uint _sourceLayers;
    private bool _bound;
    private uint _restLayers;
    private string _lastPath = "";
    internal void BindCombat(ClassicCombatEncounter combat, ClassicActorMotion motion)
    {
        if (!_bound) { _sourceLayers = Layers; _bound = true; }
        _combat = combat; _motion = motion; _restLayers = _sourceLayers; _wasWalking = false;
        Position = _sourcePosition;
        if (_analog is not null) _analog.Position = _analogPosition;
        // Reusing a preview must not consume the fresh source actor's idle
        // before its gameplay owner binds. Pending actions supply their own
        // retained pose; the standing fallback starts at its source frame.
        _frame = _placed.Frame < 0 ? _frames - 1 : _placed.Frame;
        _clock = 0; _lastFrame = -1; _lastRotation = -1; _lastPath = "";
    }
    internal void BindAnalog(Node3D model)
    {
        _analog = model; _analogPosition = model.Position;
        _body = model as ClassicPlayerBody;
        _creature = model as ClassicCreatureBody;
        _creature?.SetProcess(false);
    }
    private int _lastRotation = -1, _lastFrame = -1;

    internal void Configure(ClassicArtCache art, ClassicWorldPreview world, Fallout1NativeMapObject placed, string path, int frame)
    {
        _art = art; _world = world; _placed = placed; _path = path;
        _sourcePosition = Position;
        var decoded = art.Frame(path, placed.Rotation, frame).Frame;
        _frames = decoded.FramesPerDirection; _fps = decoded.StoredFps;
        _frame = frame < 0 ? _frames - 1 : frame;
        // Death, attack and scripted poses keep their stored frame. Only a
        // standing source FID owns a repeating idle until AI is connected.
        _idle = ((placed.Fid >> 16) & 0xff) == 0 && _frames > 1 && _fps > 0;
    }

    public override void _Process(double delta)
    {
        if (_art is null || _world.Camera is null) return;
        var action = _combat?.ActorActionPose(_placed.Serial);
        var walk = _motion?.Pose(_placed.Serial);
        var walking = walk is { Remaining.Length: > 0 } && action is null;
        if (!walking && _wasWalking) { _frame = 0; _clock = 0; }
        _wasWalking = walking;
        var currentTile = walk?.Tile ?? _placed.Tile;
        var center = Fo1HexMath.Center(currentTile);
        if (walking) center = center.Lerp(Fo1HexMath.Center(walk!.Remaining[0]), (float)walk.Fraction);
        var displacement = center - Fo1HexMath.Center(_placed.Tile);
        Position = _sourcePosition + displacement;
        if (_analog is not null) _analog.Position = _analogPosition + displacement;
        var path = action?.Path ?? (walking ? walk!.Path : _path);
        var frame = action?.Frame ?? (walking ? walk!.Frame : _frame);
        if (_combat is not null) Layers = action is null && (!walking || _body is not null) ? _restLayers : ClassicWorldPreview.ModelLayer | ClassicWorldPreview.SpriteLayer;
        if (_body is not null) _body.Visible = action is null;
        // A donor idle is not a walk. Keep actual source walk art visible until
        // a matching skinned walk has its own admitted binding.
        if (_creature is not null) _creature.Visible = action is null && !walking;
        if (_idle && action is null && !walking)
        {
            _clock += delta;
            while (_clock >= 1.0 / _fps) { _clock -= 1.0 / _fps; _frame = (_frame + 1) % _frames; }
            frame = _frame;
        }
        var direction = ClassicHexGrid.Neighbor(currentTile, action?.Rotation ?? (walking ? walk!.Rotation : _combat?.ActorRotation(_placed.Serial) ?? walk?.Rotation ?? _placed.Rotation));
        var facing = direction < 0 ? Vector3.Forward : Fo1HexMath.Center(direction) - Fo1HexMath.Center(currentTile);
        var phase = walking ? (walk!.Frame + walk.FrameSeconds * _art.Frame(path, walk.Rotation, walk.Frame).Frame.StoredFps) /
            _art.Frame(path, walk.Rotation, walk.Frame).Frame.FramesPerDirection :
            _idle ? (_frame + _clock * _fps) / _frames : (_frames <= 1 ? 0 : (double)_frame / (_frames - 1));
        _body?.Publish(walking, phase, facing);
        _creature?.Face(facing);
        _creature?.PublishPhase(phase);
        var camera = _world.Camera;
        var projected = camera.UnprojectPosition(GlobalPosition + facing) - camera.UnprojectPosition(GlobalPosition);
        Vector2[] directions = [new(16, -12), new(32, 0), new(16, 12), new(-16, 12), new(-32, 0), new(-16, -12)];
        var rotation = Enumerable.Range(0, 6).MaxBy(index => directions[index].Normalized().Dot(projected.Normalized()));
        if (_lastRotation == rotation && _lastFrame == frame && _lastPath == path) return;
        (ImageTexture Texture, Fallout1NativeFrmFrame Frame) image;
        try { image = _art.Frame(path, rotation, frame); }
        catch (Exception error) when (error is InvalidDataException or FileNotFoundException or NotSupportedException)
        {
            var failure = $"serial={_placed.Serial} art={path} direction={rotation} frame={frame}: {error.Message}";
            SetMeta("presentation_unbound", failure);
            _world.ReportPresentationFailure(failure);
            SetProcess(false);
            return;
        }
        Texture = image.Texture;
        var idle = _idle && action is null && !walking;
        Offset = new(image.Frame.DirectionX + (idle ? 0 : image.Frame.FrameX),
            image.Frame.Height / 2f - image.Frame.DirectionY - (idle ? 0 : image.Frame.FrameY));
        _lastRotation = rotation; _lastFrame = frame; _lastPath = path;
    }
}
