using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutPlacedLightResolver
{
    internal static FalloutPlacedLight ResolveModeled(FalloutPlacedReference reference,
        FalloutBaseObjectDefinition basis, FalloutModeledLightSource model,
        FalloutPluginStack? records = null, Func<FalloutFormKey, float[]>? regionEmittance = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (basis.ModelPath is null || !model.MatchesPath(basis.ModelPath))
            throw new InvalidDataException("Modeled light differs from its declared model path.");
        var result = ResolveParameters(reference, basis, modeled: true, records, regionEmittance);
        var diffuse = NormalizeModeledRecordColor(result.ColorRgb);
        if (reference.Emittance is { } emittance)
        {
            var value = new FalloutExternalEmittance(records ??
                throw new InvalidDataException("Modeled light has no XEMI source."), emittance, regionEmittance).Sample();
            for (var channel = 0; channel < diffuse.Length; channel++) diffuse[channel] *= value[channel];
        }
        return result with { ShaderColorRgb = diffuse };
    }

    // The selected light setup stores byte/255, independently of NiLight's
    // bind color and the separately owned external-emittance conversion.
    internal static float[] NormalizeModeledRecordColor(IReadOnlyList<byte> color)
    {
        if (color.Count != 3) throw new InvalidDataException("Modeled light RGB requires three source channels.");
        return [color[0] / 255f, color[1] / 255f, color[2] / 255f];
    }
}

internal sealed class FalloutModeledLightSource
{
    internal FalloutNifFile Model { get; }
    internal FalloutNifPointLight Point { get; }
    internal string ModelPath { get; }
    internal IReadOnlyList<int> ReachablePoints { get; }
    internal bool ReplaceNodeRootTransform { get; }

    private FalloutModeledLightSource(FalloutNifFile model, string path, FalloutNifPointLight point,
        IReadOnlyList<int> points, bool replaceRoot)
    { Model = model; ModelPath = path; Point = point; ReachablePoints = points; ReplaceNodeRootTransform = replaceRoot; }

    internal bool MatchesPath(string path) => ModelPath.Equals(PathIdentity(path), StringComparison.Ordinal);

    internal static FalloutModeledLightSource Read(FalloutNifFile model, string logicalPath)
    {
        ArgumentNullException.ThrowIfNull(model);
        var path = PathIdentity(logicalPath);
        if (model.Roots.Count != 1)
            throw new NotSupportedException("Modeled LIGH has no single source placement root.");
        var seen = new HashSet<int>();
        var points = new List<FalloutNifPointLight>();
        void Visit(int index)
        {
            if (!seen.Add(index)) throw new InvalidDataException("Modeled light repeats a source visual owner.");
            switch (model.ReadObject(index))
            {
                case FalloutNifNode node:
                    if (node.Effects.Any(effect => effect >= 0))
                        throw new NotSupportedException("Modeled light has unowned subtree effect registration.");
                    foreach (var child in node.Children.Where(child => child >= 0)) Visit(child);
                    break;
                case FalloutNifPointLight point: points.Add(point); break;
                case FalloutNifGeometry:
                case FalloutNifParticleSystem:
                case FalloutNifAmbientLight:
                    break;
                default:
                    throw new NotSupportedException($"Modeled light visual {index}/{model.Blocks[index].TypeName} has no selection owner.");
            }
        }
        Visit(model.Roots[0]);
        if (points.Count == 0)
            throw new NotSupportedException("Modeled light requires the unowned missing-point native constructor branch.");
        // Original setup selects the first point in ordered depth-first child
        // traversal. Additional points do not become extra record emitters.
        var selected = points[0];
        if (!selected.Light.SwitchState || selected.Light.AffectedNodes.Any(node => node >= 0))
            throw new NotSupportedException("Modeled point has an unowned switch or affected-node declaration.");
        return new(model, path, selected, points.Select(point => point.Block.Index).ToArray(),
            model.ReadObject(model.Roots[0]) is FalloutNifNode);
    }

    private static string PathIdentity(string value)
    {
        var path = value.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.Contains(':') ||
            path.Split('/').Any(component => component is "" or "." or ".."))
            throw new InvalidDataException("Modeled light has an invalid logical source path.");
        return path.ToLowerInvariant();
    }
}
