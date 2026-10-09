using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutReferencePrimitive(float X, float Y, float Z, uint Type, uint? CollisionLayer)
{
    internal bool IsPlanar => Type == 3 && Y == 0;

    // XPRM contains three half extents, editor color, four uninterpreted bytes and a
    // uint32 shape kind. XTRI, when present, is the source collision layer.
    internal static FalloutReferencePrimitive? Read(FalloutPluginRecord reference)
    {
        var fields = reference.ReadSubrecords().ToArray();
        var primitives = fields.Where(field => field.Signature == "XPRM").ToArray();
        if (primitives.Length == 0) return null;
        if (primitives.Length != 1 || primitives[0].Data.Length != 32)
            throw new InvalidDataException($"Reference {reference.FormKey} XPRM extent is invalid or duplicated.");
        var bytes = primitives[0].Data.Span;
        var type = BinaryPrimitives.ReadUInt32LittleEndian(bytes[28..]);
        var bounds = new float[3];
        for (var index = 0; index < bounds.Length; ++index)
        {
            bounds[index] = BinaryPrimitives.ReadSingleLittleEndian(bytes[(index * sizeof(float))..]);
            // Type 3 also encodes source occlusion/portal planes: the
            // local Y thickness is zero; X/Z retain the authored rectangle.
            // This is a source declaration, never an epsilon-thick trigger.
            var zeroAllowed = type == 0 || type == 3 && index == 1;
            if (!float.IsFinite(bounds[index]) || bounds[index] < 0 || bounds[index] == 0 && !zeroAllowed)
                throw new InvalidDataException($"Reference {reference.FormKey} primitive has invalid half extents.");
        }
        var layers = fields.Where(field => field.Signature == "XTRI").ToArray();
        if (layers.Length > 1 || layers.Length == 1 && layers[0].Data.Length != sizeof(uint))
            throw new InvalidDataException($"Reference {reference.FormKey} XTRI extent is invalid or duplicated.");
        return new(bounds[0], bounds[1], bounds[2], type,
            layers.Length == 0 ? null : BinaryPrimitives.ReadUInt32LittleEndian(layers[0].Data.Span));
    }
}
