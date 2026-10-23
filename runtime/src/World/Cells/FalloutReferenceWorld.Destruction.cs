using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal int DestructionStage(FalloutFormKey? reference)
    {
        if (reference is not { } target) return -1;
        var instance = Get(target);
        var source = FalloutDestructible.Read(records, instance.Base);
        if (source is null) return -1;
        if (instance.ObjectHealthPresent == false) return -1;
        if (instance.ObjectHealthPresent is null)
            throw new NotSupportedException("Saved object-health presence is unknown.");
        if (records.GetEffective(target).Signature != "REFR")
            throw new NotSupportedException("Actor destruction queries require their actual maximum-health owner.");
        return (instance.Destruction ?? throw new InvalidDataException("Object health has no retained value."))
            .QueryStage(source);
    }
}
