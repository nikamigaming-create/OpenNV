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
    internal void BindAnalog(Node3D model)
    {
        _body = model as ClassicPlayerBody;
        _creature = model as ClassicCreatureBody;
        _creature?.SetProcess(false);
    }
    private int _lastRotation = -1, _lastFrame = -1;

    internal void Configure(ClassicArtCache art, ClassicWorldPreview world, Fallout1NativeMapObject placed, string path, int frame)
    {
        _art = art; _world = world; _placed = placed; _path = path;
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
        if (_idle)
        {
            _clock += delta;
            while (_clock >= 1.0 / _fps) { _clock -= 1.0 / _fps; _frame = (_frame + 1) % _frames; }
        }
        var direction = ClassicHexGrid.Neighbor(_placed.Tile, _placed.Rotation);
        var facing = direction < 0 ? Vector3.Forward : Fo1HexMath.Center(direction) - Fo1HexMath.Center(_placed.Tile);
        var phase = _idle ? (_frame + _clock * _fps) / _frames : (_frames <= 1 ? 0 : (double)_frame / (_frames - 1));
        _body?.Publish(false, phase, facing);
        _creature?.PublishPhase(phase);
        var camera = _world.Camera;
        var projected = camera.UnprojectPosition(GlobalPosition + facing) - camera.UnprojectPosition(GlobalPosition);
        Vector2[] directions = [new(16, -12), new(32, 0), new(16, 12), new(-16, 12), new(-32, 0), new(-16, -12)];
        var rotation = Enumerable.Range(0, 6).MaxBy(index => directions[index].Normalized().Dot(projected.Normalized()));
        if (_lastRotation == rotation && _lastFrame == _frame) return;
        (ImageTexture Texture, Fallout1NativeFrmFrame Frame) image;
        try { image = _art.Frame(_path, rotation, _frame); }
        catch (Exception error) when (error is InvalidDataException or FileNotFoundException or NotSupportedException)
        {
            var failure = $"serial={_placed.Serial} art={_path} direction={rotation} frame={_frame}: {error.Message}";
            SetMeta("presentation_unbound", failure);
            _world.ReportPresentationFailure(failure);
            SetProcess(false);
            return;
        }
        Texture = image.Texture;
        Offset = new(image.Frame.DirectionX + (_idle ? 0 : image.Frame.FrameX),
            image.Frame.Height / 2f - image.Frame.DirectionY - (_idle ? 0 : image.Frame.FrameY));
        _lastRotation = rotation; _lastFrame = _frame;
    }
}
