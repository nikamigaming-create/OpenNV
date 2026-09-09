using Godot;
using OpenNV.Runtime.Campaigns.Fallout1;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

/// <summary>Source roof tiles grouped by connected building, cut away when the player is underneath.</summary>
internal sealed partial class ClassicRoofPresentation : Node3D
{
    private readonly Dictionary<int, Node3D> _byTile = [];
    private readonly List<Node3D> _buildings = [];
    private Node3D? _cutaway;
    private bool _enabled = true;

    internal void Build(uint[] tiles, IReadOnlyList<string> names, ClassicArtCache art, float height, int texturePixels)
    {
        Name = "ClassicSourceRoofs";
        var remaining = Enumerable.Range(0, tiles.Length).Where(index => ((tiles[index] >> 16) & 0xfff) != 1).ToHashSet();
        while (remaining.Count > 0)
        {
            var group = new List<int>(); var queue = new Queue<int>();
            var seed = remaining.First(); remaining.Remove(seed); queue.Enqueue(seed);
            while (queue.TryDequeue(out var index))
            {
                group.Add(index);
                foreach (var next in new[] { index - 100, index + 100, index % 100 == 0 ? -1 : index - 1, index % 100 == 99 ? -1 : index + 1 })
                    if (remaining.Remove(next)) queue.Enqueue(next);
            }
            var building = new Node3D { Name = $"Roof_{seed}" }; AddChild(building); _buildings.Add(building);
            foreach (var index in group) _byTile.Add(index, building);
            foreach (var pieces in group.GroupBy(index => (int)((tiles[index] >> 16) & 0xfff)))
            {
                if (pieces.Key >= names.Count) throw new InvalidDataException("Source roof art index exceeds tiles.lst.");
                var path = "art/tiles/" + names[pieces.Key]; var indices = pieces.ToArray();
                if (!art.Frame(path).Frame.PaletteIndexes.Any(color => color != 0)) continue;
                var instances = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    Mesh = ClassicHexBlockout.FloorPatch(new StandardMaterial3D
                    {
                        AlbedoTexture = art.Floor(path, texturePixels, false),
                        Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
                        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                        TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                    }),
                    InstanceCount = indices.Length,
                };
                for (var i = 0; i < indices.Length; i++)
                    instances.SetInstanceTransform(i, new Transform3D(Basis.Identity,
                        Fo1HexMath.FloorPatchCenter(indices[i]) + Vector3.Up * height));
                var mesh = new MultiMeshInstance3D { Name = $"SourceRoofArt_{pieces.Key}", Multimesh = instances };
                mesh.SetMeta("source_art", path); building.AddChild(mesh);
            }
        }
    }

    internal void Reveal(Vector3 position)
    {
        var hex = Fo1HexMath.NearestTile(position);
        var next = hex < 0 ? null : _byTile.GetValueOrDefault(ClassicHexGrid.FloorIndex(hex));
        if (_cutaway == next) return;
        if (_cutaway is not null) _cutaway.Visible = _enabled;
        _cutaway = next;
        if (_cutaway is not null) _cutaway.Visible = false;
    }

    internal void Toggle()
    {
        _enabled = !_enabled;
        foreach (var building in _buildings) building.Visible = _enabled && building != _cutaway;
    }
}
