using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// Decals have a separate lifetime from the impact particle graph. Keeping a
// wound under that graph made it disappear when the short burst ended.
internal sealed partial class RuntimeNativeImpactDecals : Node
{
    private sealed record Entry(Decal Node, Node3D? Target, Transform3D Local, double Created);
    private readonly List<Entry> _entries = [];
    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Random _random = new();
    private readonly float _units, _lifetime;
    private double _seconds;
    private long _created;
    internal int Count => _entries.Count;
    internal object Observation => new { created = _created, active = Count, lifetime = _lifetime,
        boundary = "projected-Godot-decal;source-parallax-specular-and-skinned-clipping-unmatched" };

    internal RuntimeNativeImpactDecals(FalloutPluginStack records, float units)
    {
        Name = "SourceImpactDecals"; _units = units;
        _lifetime = FalloutGameSettingFloats.Read(records, "fDecalLifetime:Display");
        if (_lifetime <= 0) throw new InvalidDataException("Source decal lifetime must be positive.");
    }

    internal void Place(FalloutImpact impact, Vector3 point, Vector3 normal, Node3D? target)
    {
        if (impact.Decal is not { } source) return;
        var width = Mathf.Lerp(source.MinimumWidth, source.MaximumWidth, _random.NextSingle()) * _units;
        var height = Mathf.Lerp(source.MinimumHeight, source.MaximumHeight, _random.NextSingle()) * _units;
        var y = normal.Normalized();
        var helper = Mathf.Abs(y.Dot(Vector3.Up)) < .99f ? Vector3.Up : Vector3.Right;
        var x = helper.Cross(y).Normalized().Rotated(y, _random.NextSingle() * Mathf.Tau);
        var pose = new Transform3D(new Basis(x, y, x.Cross(y)), point);
        var decal = new Decal
        {
            Name = "ImpactDecal_" + ++_created, TopLevel = true, Size = new(width, source.Depth * _units, height),
            TextureAlbedo = Texture(source.Diffuse), TextureNormal = source.Normal is null ? null : Texture(source.Normal),
            Modulate = new(source.Red / 255f, source.Green / 255f, source.Blue / 255f, 1),
            AlbedoMix = 1, NormalFade = .5f, UpperFade = 0, LowerFade = 0,
        };
        AddChild(decal); decal.GlobalTransform = pose;
        _entries.Add(new(decal, target, target is null ? pose : target.GlobalTransform.AffineInverse() * pose, _seconds));
    }

    public override void _Process(double delta)
    {
        _seconds += delta;
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            var entry = _entries[i];
            if (_seconds - entry.Created >= _lifetime || entry.Target is not null && !IsInstanceValid(entry.Target))
            { entry.Node.Free(); _entries.RemoveAt(i); continue; }
            if (entry.Target is not null) entry.Node.GlobalTransform = entry.Target.GlobalTransform * entry.Local;
        }
    }

    private Texture2D Texture(string path)
    {
        if (!_textures.TryGetValue(path, out var texture)) _textures.Add(path, texture = NativeOwnedMediaLoader.LoadTexture(path));
        return texture;
    }
}
