using OpenNV.Runtime.Content;

internal static class ExplosionContracts
{
    internal static void Run(FalloutPluginStack records)
    {
        var ordinary = FalloutExplosion.Read(records, records.RuntimeFormKey(0xA00));
        Require(ordinary.Damage == 28 && ordinary.Radius == 256 && ordinary.Flags == 3 &&
            ordinary.IgnoresLineOfSight && ordinary.UsesWorldOrientation && ordinary.Model == "meshes/effects/synthetic.nif",
            "Winning EXPL DATA fields, flags or owned model path were decoded incorrectly.");
        ordinary.RequireRuntimeDamageOwner();

        var unsupportedFlags = FalloutExplosion.Read(records, records.RuntimeFormKey(0xA01));
        Reject(unsupportedFlags.RequireRuntimeDamageOwner);
        var unsupportedForce = FalloutExplosion.Read(records, records.RuntimeFormKey(0xA02));
        Reject(unsupportedForce.RequireRuntimeDamageOwner);
        Reject(() => FalloutExplosion.Read(records, records.RuntimeFormKey(0xA03)));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException) { return; }
        throw new InvalidDataException("Invalid explosion record was admitted.");
    }
}
