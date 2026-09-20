using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

/// <summary>Winning projectile declaration. Range and speed retain source game units.</summary>
internal sealed record FalloutProjectile(FalloutFormKey Form, ushort Flags, ushort Type, float Gravity,
    float Speed, float Range, float TracerChance, string? Model, string? MuzzleFlash, float MuzzleSeconds,
    FalloutFormKey? MuzzleLight, FalloutFormKey? Explosion)
{
    internal bool Hitscan => (Flags & 1) != 0;
    internal bool HasAlternateTrigger => (Flags & 0x0004) != 0;
    internal bool CanBeDisabled => (Flags & 0x0020) != 0;
    internal bool CanBePickedUp => (Flags & 0x0040) != 0;
    internal bool Detonates => (Flags & 0x0400) != 0;
    internal bool IsInstantRayAttack => Hitscan || Type == 4;
    internal bool PassesThroughActors => Type == 8;
    internal bool HasExplicitRotation { get; private init; }
    internal float BouncyMultiplier { get; private init; }
    internal float ExplosionAltTriggerProximity { get; private init; }
    internal float ExplosionAltTriggerTimer { get; private init; }
    internal FalloutExplosion? ExplosionSource { get; private init; }

    internal float HitscanImpactDelaySeconds(float distanceMeters, float unitsToMeters)
    {
        if (!Hitscan) return 0;
        if (!float.IsFinite(distanceMeters) || distanceMeters < 0 || !float.IsFinite(unitsToMeters) || unitsToMeters <= 0 ||
            !float.IsFinite(Speed) || Speed <= 0)
            throw new InvalidDataException("Hitscan travel distance, unit scale or source speed is invalid.");
        var speedMetersPerSecond = Speed * unitsToMeters;
        if (!float.IsFinite(speedMetersPerSecond) || speedMetersPerSecond <= 0)
            throw new InvalidDataException("Hitscan speed cannot be converted to world units.");
        var delay = distanceMeters / speedMetersPerSecond;
        return float.IsFinite(delay) ? delay : throw new InvalidDataException("Hitscan impact delay is invalid.");
    }

    internal static FalloutProjectile Read(FalloutPluginStack records, FalloutFormKey key)
    {
        var record = records.GetEffective(key);
        if (record.Signature != "PROJ") throw new InvalidDataException("Projectile reference is not PROJ.");
        var fields = record.ReadSubrecords().ToArray();
        var data = fields.Single(field => field.Signature == "DATA").Data.Span;
        if (data.Length is not (64 or 68 or 80 or 84)) throw new NotSupportedException($"PROJ DATA extent {data.Length} is unbound.");
        var flags = BinaryPrimitives.ReadUInt16LittleEndian(data);
        var explicitRotation = (flags & 0x0800) != 0;
        if (explicitRotation && data.Length < 80)
            throw new InvalidDataException("Projectile rotation flag has no rotation fields.");
        var bouncy = data.Length == 84 ? Number(data, 80) : 0;
        var explosion = record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(data[36..]));
        var result = new FalloutProjectile(key, flags, BinaryPrimitives.ReadUInt16LittleEndian(data[2..]),
            Number(data, 4), Number(data, 8), Number(data, 12), Number(data, 24), Path("MODL"), Path("NAM1"), Number(data, 44),
            record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(data[20..])),
            explosion)
        {
            HasExplicitRotation = explicitRotation,
            BouncyMultiplier = bouncy,
            ExplosionAltTriggerProximity = Number(data, 28),
            ExplosionAltTriggerTimer = Number(data, 32),
            ExplosionSource = explosion is { } form ? FalloutExplosion.Read(records, form) : null,
        };
        if (result.Range <= 0 || result.Speed < 0 || result.Gravity < 0 || result.TracerChance is < 0 or > 1 ||
            result.MuzzleSeconds < 0 || result.BouncyMultiplier < 0 || result.ExplosionAltTriggerProximity < 0 ||
            result.ExplosionAltTriggerTimer < 0)
            throw new InvalidDataException("Projectile motion/appearance values are invalid.");
        return result;

        string? Path(string signature)
        {
            var bytes = fields.SingleOrDefault(field => field.Signature == signature).Data;
            if (bytes.IsEmpty) return null;
            var path = FalloutPlugin.DecodeZeroTerminated(bytes.Span, "projectile model").Replace('\\', '/');
            if (path.Split('/').Any(part => part is ".." or ".") || path.Contains(':') || path.StartsWith('/'))
                throw new InvalidDataException("Projectile model leaves the owned namespace.");
            return path.StartsWith("meshes/", StringComparison.OrdinalIgnoreCase) ? path : "meshes/" + path;
        }
    }

    internal static float Number(ReadOnlySpan<byte> data, int offset)
    {
        var value = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
        return float.IsFinite(value) ? value : throw new InvalidDataException("Projectile/weapon field is not finite.");
    }
}
