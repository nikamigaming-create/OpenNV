using Godot;

namespace OpenNV.Runtime.World.Actors;

/// <summary>Transfers authored furniture accumulation through the marker's approach frame.</summary>
internal sealed record NativeFurnitureRootMotion(Transform3D Anchor, Vector3 ReferenceRoot)
{
    internal static NativeFurnitureRootMotion Enter(Transform3D occupied, float headingDelta, Vector3 terminalRoot)
        => Create(occupied, headingDelta, terminalRoot);

    internal static NativeFurnitureRootMotion Exit(Transform3D occupied, float headingDelta, Vector3 initialRoot)
        => Create(occupied, headingDelta, initialRoot);

    private static NativeFurnitureRootMotion Create(Transform3D occupied, float headingDelta, Vector3 referenceRoot)
    {
        if (!occupied.IsFinite() || occupied.Basis.Determinant() <= 0 ||
            !float.IsFinite(headingDelta) || !referenceRoot.IsFinite())
            throw new InvalidDataException("Furniture accumulation frame is invalid.");
        return new(new(occupied.Basis * new Basis(Vector3.Up, headingDelta), occupied.Origin), referenceRoot);
    }

    internal Transform3D Sample(Vector3 sourceRoot)
    {
        if (!sourceRoot.IsFinite()) throw new InvalidDataException("Furniture accumulation sample is not finite.");
        return new(Anchor.Basis, Anchor * (sourceRoot - ReferenceRoot));
    }
}
