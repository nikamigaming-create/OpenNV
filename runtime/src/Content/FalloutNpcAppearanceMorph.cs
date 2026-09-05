using OpenNV.Runtime.Formats.FaceGen;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutNpcAppearanceMorphSource(string LogicalPath, string ResourceOwner,
    FalloutEgmFile Geometry);

internal static class FalloutNpcAppearanceMorph
{
    /// <summary>
    /// Statistical morphs are a companion resource of the selected source model.
    /// MODD identifies model body regions, not the presence of an EGM file.
    /// </summary>
    internal static FalloutNpcAppearanceMorphSource? Resolve(RuntimeLiveContentSource source,
        FalloutNpcAppearancePart part, string? selectedShape = null)
    {
        if (part.ModelPath is not { } model || !model.EndsWith(".nif", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("NPC morph resolution requires the selected source NIF model.");
        if (part.Role == "hair" && selectedShape is null)
            throw new InvalidDataException("Hair EGM resolution requires the equipped shape selection.");
        var path = selectedShape is null ? Path.ChangeExtension(model, ".egm") :
            model[..^4] + selectedShape.ToLowerInvariant() + ".egm";
        return source.TryRead(path, null, out var bytes, out var owner)
            ? new FalloutNpcAppearanceMorphSource(path, owner, FalloutEgmFile.Read(bytes))
            : null;
    }
}
