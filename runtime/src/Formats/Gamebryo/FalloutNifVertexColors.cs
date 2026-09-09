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

    internal static FalloutNifColor Project(FalloutNifColor source, bool usesAlpha)
    {
        // Some owned exports retain uninitialized alpha bytes even though the
        // shader never reads vertex alpha. Keep those bytes in the decoded NIF;
        // only the consumed channels participate in renderer validation.
        if (!float.IsFinite(source.R) || !float.IsFinite(source.G) || !float.IsFinite(source.B) ||
            usesAlpha && !float.IsFinite(source.A))
            throw new InvalidDataException("NIF shader consumes a non-finite vertex color channel.");
        return usesAlpha ? source : source with { A = 1 };
    }
}
