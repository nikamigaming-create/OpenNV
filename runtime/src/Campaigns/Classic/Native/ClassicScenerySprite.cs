using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

/// <summary>Original repeating fire/sign frames; doors and scripted scenery retain their stored pose.</summary>
internal sealed partial class ClassicScenerySprite : Sprite3D
{
    private ClassicArtCache? _art;
    private string _path = "";
    private int _rotation, _frame, _count;
    private ushort _fps;
    private double _clock;

    internal void Configure(ClassicArtCache art, string path, Fallout1NativeMapObject placed)
    {
        var name = Path.GetFileName(path.Replace('\\', '/')).ToLowerInvariant();
        if (name is not ("woodfire.frm" or "barrel.frm" or "gizsign.frm" or "barsign.frm")) return;
        var frame = art.Frame(path, placed.Rotation, placed.Frame).Frame;
        if (frame.StoredFps == 0 || frame.FramesPerDirection <= 1) return;
        _art = art; _path = path; _rotation = placed.Rotation; _frame = placed.Frame;
        _fps = frame.StoredFps; _count = frame.FramesPerDirection;
    }

    public override void _Process(double delta)
    {
        if (_art is null) return;
        _clock += delta;
        if (_clock < 1.0 / _fps) return;
        var steps = (int)(_clock * _fps); _clock -= steps / (double)_fps;
        _frame = (_frame + steps) % _count;
        var image = _art.Frame(_path, _rotation, _frame); Texture = image.Texture;
        Offset = new(image.Frame.DirectionX + image.Frame.FrameX,
            image.Frame.Height / 2f - image.Frame.DirectionY - image.Frame.FrameY);
    }
}
