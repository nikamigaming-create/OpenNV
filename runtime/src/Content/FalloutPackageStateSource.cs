using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutPackageStateSource(FalloutPlayerMovementControlSource Controls, string Fields, string Registry)
{
    internal static FalloutPackageStateSource Reviewed(FalloutPlayerMovementControlSource controls)
    {
        controls.Validate();
        if (!FalloutSourceMainFamily.IsFallout3(controls.Main.EngineSha256))
            throw new NotSupportedException("Original mutable package fields are unreviewed for this image.");
        return new(controls, FalloutProcessPackageState.Source(controls.Main.EngineSha256),
            FalloutPackageRuntimeRegistry.SourceFor(controls.Main.EngineSha256));
    }
    internal void Validate()
    {
        if (this != Reviewed(Controls)) throw new InvalidDataException("Package setter source lost its selected control/field/registry association.");
    }
}
