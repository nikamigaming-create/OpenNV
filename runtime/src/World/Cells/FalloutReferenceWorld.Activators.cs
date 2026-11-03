using System.Buffers.Binary;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    // Called only after the presentation owner establishes that no Open/Close
    // sequence is declared. Other object clips keep their actual source clock.
    internal void DefaultActivateUnanimatedActivator(FalloutFormKey reference)
    {
        var instance = Get(reference);
        var source = records.GetEffective(instance.Base);
        if (source.Signature != "ACTI")
            throw new InvalidOperationException($"Logical activator action requires an ACTI, not {source.Signature}.");
        if (instance.DoorMotion is not null)
            throw new NotSupportedException($"ACTI {reference} retains an Open/Close motion that needs its actual owner.");
        foreach (var field in source.ReadSubrecords().Where(field => field.Signature is "VNAM" or "WNAM" or "RNAM" or "INAM"))
        {
            if (field.Data.Length != 4)
                throw new InvalidDataException($"ACTI {source.FormKey} {field.Signature} must contain a FormID.");
            if (source.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span)) is not null)
                throw new NotSupportedException($"Default activation of ACTI {reference} needs its source {field.Signature} activation owner.");
        }
        // A plain activator's open/close callbacks are empty. Missing sequences
        // toggle this persistent logical bit without starting or resetting a clip.
        instance.DoorOpen = !instance.DoorOpen;
    }

    internal int? LogicalActivatorOpenState(FalloutFormKey reference)
    {
        var instance = Get(reference);
        if (records.GetEffective(instance.Base).Signature != "ACTI") return null;
        if (instance.DoorMotion is not null)
            throw new NotSupportedException($"ACTI {reference} retains an Open/Close motion that needs its actual owner.");
        return instance.DoorOpen ? 1 : 3;
    }
}
