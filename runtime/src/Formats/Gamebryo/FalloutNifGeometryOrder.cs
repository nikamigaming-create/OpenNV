using System.Runtime.CompilerServices;

namespace OpenNV.Runtime.Formats.Gamebryo;

/// <summary>Model texture indices enumerate geometry in depth-first scene-child order.</summary>
internal static class FalloutNifGeometryOrder
{
    private static readonly ConditionalWeakTable<FalloutNifFile, IReadOnlyList<FalloutNifGeometry>> Cache = new();

    internal static IReadOnlyList<FalloutNifGeometry> Read(FalloutNifFile source) => Cache.GetValue(source, ReadCore);

    private static IReadOnlyList<FalloutNifGeometry> ReadCore(FalloutNifFile source)
    {
        var result = new List<FalloutNifGeometry>();
        var pending = new Stack<int>(source.Roots.Reverse());
        var visited = new HashSet<int>();
        while (pending.TryPop(out var index))
        {
            if (index < 0) continue;
            if (!visited.Add(index))
                throw new InvalidDataException("Model texture traversal has a cyclic or shared scene child.");
            switch (source.ReadObject(index))
            {
                case FalloutNifNode node:
                    foreach (var child in node.Children.Reverse()) pending.Push(child);
                    break;
                case FalloutNifGeometry geometry:
                    result.Add(geometry);
                    break;
                case FalloutNifParticleSystem particles:
                    result.Add(particles.Geometry);
                    break;
                case FalloutNifAmbientLight or FalloutNifPointLight:
                    break;
                default:
                    throw new NotSupportedException($"Model texture traversal has an unbound scene object: {source.Blocks[index].TypeName}.");
            }
        }
        return result.AsReadOnly();
    }
}
