using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerReferenceOrientationOwner
{
    internal FalloutPhysicalVector3 ReadPhysicalReferenceRotation(Guid constructor)
    {
        RequireOwner();
        if (constructor == Guid.Empty) throw new InvalidDataException("Physical reference rotation omitted its actual constructor.");
        return new(_x, _y, _z);
    }
    internal FalloutPackageCommandRawRotationRead ReadPackageCommandRawRotationZ(Guid constructor)
    {
        RequireOwner();
        if (constructor == Guid.Empty)
            throw new InvalidDataException("Package rotation read has no actual scene constructor.");
        return new(Source.Identity, Stack, Process, Factory, constructor, Binding.Reference, Binding.Base, _sequence, _z);
    }
}
