using System.Numerics;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Content;

// Pure source data, owned by one preparation job and handed to the scene thread
// only after that job completes. No Godot resources or gameplay mutations.
internal sealed record FalloutNpcPreparedPart(FalloutNpcAppearancePart Part, FalloutNifFile Model,
    string? SelectedShape, string? MorphIdentity,
    IReadOnlyDictionary<int, FalloutNifMeshData> Geometry,
    IReadOnlyDictionary<int, IReadOnlyDictionary<string, Vector3[]>> Morphs);

internal sealed record FalloutNpcPreparedGeometry(FalloutNpcAppearance Appearance, byte[] Skeleton,
    IReadOnlyDictionary<string, FalloutNifTransform>? HeadBinds, IReadOnlyList<FalloutNpcPreparedPart> Parts)
{
    internal static FalloutNpcPreparedGeometry Read(FalloutNpcAppearance appearance,
        RuntimeLiveContentSource source, CancellationToken cancellation = default)
    {
        if (!appearance.CanConstruct) throw new NotSupportedException(string.Join("; ", appearance.Blockers));
        var skeleton = ReadBytes(appearance.SkeletonPath);
        var parts = new List<FalloutNpcPreparedPart>();
        foreach (var part in appearance.Models)
        {
            cancellation.ThrowIfCancellationRequested();
            if (part.ModelPath is null) throw new InvalidDataException($"NPC model {part.Source}/{part.Role} has no NIF path.");
            try
            {
                var model = FalloutNifFile.Read(ReadBytes(part.ModelPath));
                var selected = FalloutNpcAppearanceHairShape.Select(appearance, part);
                var morphSource = FalloutNpcAppearanceMorph.Resolve(source, part, selected);
                var morph = morphSource is null ? null : new FalloutNpcFaceGeometry(appearance, part, morphSource.Geometry, selected);
                var expressions = FalloutNpcFaceMorph.Resolve(source, appearance, part, morphSource?.Geometry, selected);
                var geometry = new Dictionary<int, FalloutNifMeshData>();
                var morphs = new Dictionary<int, IReadOnlyDictionary<string, Vector3[]>>();
                if (morph is not null || expressions is not null)
                    foreach (var block in model.Blocks.Where(block => block.TypeName is "NiTriShape" or "NiTriStrips" or "BSSegmentedTriShape"))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        var shape = model.ReadGeometry(block.Index);
                        if (selected is not null && !shape.Name.Equals(selected, StringComparison.OrdinalIgnoreCase)) continue;
                        var mesh = model.ReadMeshData(shape.Data);
                        if (morph is not null) geometry.Add(block.Index, mesh = morph.Apply(model, shape, mesh));
                        if (expressions is not null) morphs.Add(block.Index, expressions.Build(model, shape, mesh));
                    }
                parts.Add(new(part, model, selected, morphSource?.ResourceOwner, geometry, morphs));
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or FileNotFoundException)
            {
                throw new NotSupportedException($"NPC {appearance.Npc} part {part.Role} ({part.ModelPath}): {error.Message}", error);
            }
        }
        var head = parts.SingleOrDefault(part => part.Part.Role == "head");
        return new(appearance, skeleton, head is null ? null : FalloutNpcFaceAttachment.ReadHeadBinds(head.Model), parts);

        byte[] ReadBytes(string path)
        {
            cancellation.ThrowIfCancellationRequested();
            return source.TryRead(path, null, out var bytes, out _) ? bytes : throw new FileNotFoundException($"NPC source resource is missing: {path}");
        }
    }
}
