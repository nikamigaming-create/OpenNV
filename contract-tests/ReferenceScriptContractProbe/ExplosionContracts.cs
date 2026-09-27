using OpenNV.Runtime.Content;

internal static class ExplosionContracts
{
    internal static void Run(FalloutPluginStack records)
    {
        var ordinary = FalloutExplosion.Read(records, records.RuntimeFormKey(0xA00));
        Require(ordinary.Damage == 28 && ordinary.Radius == 256 && ordinary.Flags == 0x12 &&
            ordinary.IgnoresLineOfSight && ordinary.UsesWorldOrientation && ordinary.Model == "meshes/effects/synthetic.nif",
            "Winning EXPL DATA fields, flags or owned model path were decoded incorrectly.");
        ordinary.RequireRuntimeDamageOwner();

        Reject(() => FalloutExplosion.Read(records, records.RuntimeFormKey(0xA01)).RequireRuntimeDamageOwner());
        var force = FalloutExplosion.Read(records, records.RuntimeFormKey(0xA02));
        force.RequireRuntimeDamageOwner();
        Require(force.Force == 5, "Source blast force changed during admission.");
        Reject(() => FalloutExplosion.Read(records, records.RuntimeFormKey(0xA03)));
        var always = FalloutExplosion.Read(records, records.RuntimeFormKey(0xA04));
        var formula = FalloutExplosion.Read(records, records.RuntimeFormKey(0xA05));
        var sourceOnly = FalloutExplosion.Read(records, records.RuntimeFormKey(0xA06));
        always.RequireRuntimeDamageOwner(); formula.RequireRuntimeDamageOwner(); sourceOnly.RequireRuntimeDamageOwner();
        Require(always.KnocksDownAlways && !always.KnocksDownByFormula && !always.UsesWorldOrientation &&
            formula.KnocksDownByFormula && !formula.IgnoresLineOfSight && sourceOnly.PushesSourceOnly && !sourceOnly.IgnoresImageSpaceSwap,
            "Source explosion bit indices were shifted or conflated.");

        var imageSpaceOnly = FalloutExplosion.Read(records, records.RuntimeFormKey(0xA07));
        Require(imageSpaceOnly.IgnoresImageSpaceSwap && !imageSpaceOnly.IgnoresLineOfSight && !imageSpaceOnly.UsesWorldOrientation,
            "EXPL flag IDs mapped to the wrong source bits.");
        imageSpaceOnly.RequireRuntimeDamageOwner();
        var noOrientation = FalloutExplosion.Read(records, records.RuntimeFormKey(0xA08));
        Require(!noOrientation.IgnoresLineOfSight && noOrientation.UsesWorldOrientation,
            "World-orientation and line-of-sight flags were conflated.");
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
