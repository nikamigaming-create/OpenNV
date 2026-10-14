using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerReferenceOrientationOwner
{
    internal FalloutPackageCommandRawRotationRead ReadPackageCommandRawRotationZ(Guid constructor)
    {
        RequireOwner();
        if (constructor == Guid.Empty)
            throw new InvalidDataException("Package rotation read has no actual scene constructor.");
        return new(Source.Identity, Stack, Process, Factory, constructor, Binding.Reference, Binding.Base, _sequence, _z);
    }
}
