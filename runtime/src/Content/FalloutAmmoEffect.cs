using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutAmmoEffect(FalloutFormKey Form, uint Type, uint Operation, float Value)
{
    internal const uint Damage = 0;
    internal const uint DamageResistance = 1;
    internal const uint DamageThreshold = 2;

    internal static FalloutAmmoEffect Read(FalloutPluginStack records, FalloutFormKey form)
    {
        var record = records.GetEffective(form);
        if (record.Signature != "AMEF") throw new InvalidDataException("Ammo effect identity is not AMEF.");
        var data = record.ReadSubrecords().Single(field => field.Signature == "DATA").Data.Span;
        if (data.Length != 12) throw new NotSupportedException($"AMEF DATA extent {data.Length} is unbound.");
        var type = BinaryPrimitives.ReadUInt32LittleEndian(data);
        var operation = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        var value = FalloutProjectile.Number(data, 8);
        if (type > 5 || operation > 2) throw new NotSupportedException("AMEF type or operation is unbound.");
        return new(form, type, operation, value);
    }

    internal float Apply(float source)
    {
        if (!float.IsFinite(source)) throw new InvalidDataException("Ammo effect input is not finite.");
        var result = Operation switch
        {
            0 => source + Value,
            1 => source * Value,
            2 => source - Value,
            _ => throw new NotSupportedException($"AMEF operation {Operation} is unbound.")
        };
        return float.IsFinite(result) ? result : throw new InvalidDataException("Ammo effect output is not finite.");
    }
}
