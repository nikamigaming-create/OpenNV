using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal sealed record ClassicCreatureBinding(string ArtBase, string SourceGame, string Skeleton, string Model,
    float ForwardYawDegrees, Dictionary<string, string> Animations, Dictionary<int, string> SourceAnimations);
internal sealed record ClassicCreatureRecipe(string Schema, ClassicCreatureBinding[] Bindings);

/// <summary>Recovered classic creature donors, read directly from the selected owned library.</summary>
internal sealed class ClassicOwnedCreatures
{
    private sealed record ModelSource(RuntimeLiveContentSource Library, byte[] Skeleton, byte[] Model,
        Dictionary<string, (FalloutNifFile File, FalloutNifControllerSequence Sequence)> Clips);
    private readonly ClassicCreatureRecipe _recipe;
    private readonly Dictionary<ClassicCreatureBinding, ModelSource> _sources = [];

    internal ClassicOwnedCreatures()
    {
        _recipe = JsonSerializer.Deserialize<ClassicCreatureRecipe>(
            Godot.FileAccess.GetFileAsString("res://config/classic-creatures-v1.json"))
            ?? throw new InvalidDataException("Classic creature recipe is absent.");
        if (_recipe.Schema != "opennv-classic-owned-creatures/v1" ||
            _recipe.Bindings.Select(row => row.ArtBase).Distinct(StringComparer.OrdinalIgnoreCase).Count() != _recipe.Bindings.Length)
            throw new InvalidDataException("Classic creature bindings are unsupported or ambiguous.");
    }

    internal ClassicCreatureBody? Create(string artPath, ClassicArtCache art, Fallout1NativeMapObject placed,
        ClassicBlockoutPolicy policy, Func<string, string, RuntimeLiveContentSource> resolve)
    {
        var name = Path.GetFileNameWithoutExtension(artPath);
        var binding = _recipe.Bindings.SingleOrDefault(row => name.Length == row.ArtBase.Length + 2 &&
            name.StartsWith(row.ArtBase, StringComparison.OrdinalIgnoreCase));
        if (binding is null) return null;
        if (((placed.Fid >> 12) & 0xf) != 0)
            throw new NotSupportedException($"Creature {binding.ArtBase} held equipment needs its weapon/socket/clip binding; original armed art retained.");
        var sourceState = (int)((placed.Fid >> 16) & 0xff);
        if (!binding.SourceAnimations.TryGetValue(sourceState, out var role))
            throw new NotSupportedException($"Creature {binding.ArtBase} action {sourceState} has no faithful 3D clip; original art retained.");
        if (!_sources.TryGetValue(binding, out var model))
        {
            var library = resolve(binding.Model, binding.SourceGame);
            byte[] Read(string path) => library.TryRead(path, null, out var bytes, out _)
                ? bytes : throw new FileNotFoundException("Owned creature resource is absent: " + path);
            var clips = new Dictionary<string, (FalloutNifFile, FalloutNifControllerSequence)>();
            foreach (var (key, path) in binding.Animations)
            {
                var file = FalloutNifFile.Read(Read(path));
                var sequence = file.Roots.Select(file.ReadObject).OfType<FalloutNifControllerSequence>().Single();
                if (!float.IsFinite(sequence.StartTime) || !float.IsFinite(sequence.StopTime) || sequence.StopTime <= sequence.StartTime)
                    throw new InvalidDataException("Creature clip has no complete source duration: " + path);
                clips.Add(key, (file, sequence));
            }
            model = new(library, Read(binding.Skeleton), Read(binding.Model), clips);
            _sources.Add(binding, model);
        }
        var body = new ClassicCreatureBody();
        try
        {
            var reference = art.Frame($"art/critters/{binding.ArtBase}aa.frm", placed.Rotation, 0).Frame;
            var frame = art.Frame(artPath, placed.Rotation, placed.Frame).Frame;
            body.Configure(binding, model.Library, model.Skeleton, model.Model, model.Clips,
                placed, role, frame, reference, policy);
            return body;
        }
        catch { body.Free(); throw; }
    }
}
