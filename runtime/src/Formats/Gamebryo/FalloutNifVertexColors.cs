namespace OpenNV.Runtime.Formats.Gamebryo;

// Vertex-colour availability is derived when geometry is bound to a shader.
// The serialized property flag alone is not the rendered property state.
internal readonly record struct FalloutNifVertexColorState(uint SourceFlags2, uint EffectiveFlags2)
{
    private const uint VertexColors = 1U << 5;
    internal bool Enabled => (EffectiveFlags2 & VertexColors) != 0;

    internal static FalloutNifVertexColorState Resolve(uint sourceFlags2, int vertices, int colors)
    {
        if (vertices < 0 || colors < 0 || colors != 0 && colors != vertices)
            throw new InvalidDataException("NIF vertex colours must be absent or cover every vertex.");
        return new(sourceFlags2, colors == 0 ? sourceFlags2 & ~VertexColors : sourceFlags2 | VertexColors);
    }
}
