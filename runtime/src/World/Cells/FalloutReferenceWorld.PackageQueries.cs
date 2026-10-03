using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal FalloutFormKey? CurrentPackage(FalloutFormKey reference)
    {
        var instance = Get(reference);
        if (records.GetEffective(reference).Signature is not ("ACHR" or "ACRE"))
            throw new InvalidDataException($"Current-package target {reference} is not an actor.");
        return (instance.QueryCurrentPackage ??
            throw new NotSupportedException($"Actor {reference} has no active reference package owner."))();
    }
}
