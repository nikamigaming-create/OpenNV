namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed partial class FalloutNifFile
{
    internal FalloutNifRagdollConstraint ReadRagdollConstraint(int blockIndex)
    {
        var declaration = ReadObject(blockIndex) as FalloutNifRagdollConstraint ??
            throw new NotSupportedException("Wrapped actor constraint is not a limited hinge or ragdoll joint.");
        RequireNativeMotor(declaration);
        return declaration;
    }
}
