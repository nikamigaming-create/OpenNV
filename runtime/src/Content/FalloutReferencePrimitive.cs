using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutReferencePrimitive(float X, float Y, float Z, uint Type, uint? CollisionLayer)
{
    internal bool IsPlanar => Type == 3;

    // XPRM contains three stored bounds, four editor color scalars and a
    // uint32 shape kind. Sphere construction consumes X as its radius; planes
    // consume X/Z. Stored Y is not a marker role or a physical plane thickness.
    // XTRI, when present, is the source collision layer.
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
            // Plane construction retains its stored middle bound independently
            // of the zero-thickness rectangle. Sphere Y/Z are retained source
            // scalars, not separately consumed radius operands.
            var consumed = type is not (0 or 2 or 3) || type == 2 && index == 0 || type == 3 && index != 1;
            if (!float.IsFinite(bounds[index]) || consumed && bounds[index] <= 0)
                throw new InvalidDataException($"Reference {reference.FormKey} primitive has invalid half extents.");
        }
        var layers = fields.Where(field => field.Signature == "XTRI").ToArray();
        if (layers.Length > 1 || layers.Length == 1 && layers[0].Data.Length != sizeof(uint))
            throw new InvalidDataException($"Reference {reference.FormKey} XTRI extent is invalid or duplicated.");
        return new(bounds[0], bounds[1], bounds[2], type,
            layers.Length == 0 ? null : BinaryPrimitives.ReadUInt32LittleEndian(layers[0].Data.Span));
    }
}
