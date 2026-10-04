using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class OwnedWeaponSightProbe
{
    internal static void Run(string game, string root, string[] dependencies)
    {
        using var content = new FalloutModStackSelection([new("ttw", root, dependencies)]).Resolve(game).OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var selected = records.GetEffective(new("FalloutNV.esm", 0x0c0327));
        var hash = SHA256.HashData(selected.ReadData());
        var dnam = selected.ReadSubrecords().Single(field => field.Signature == "DNAM").Data.Span;
        var weapon = FalloutWeaponPresentation.Read(records, selected.FormKey);
        if (weapon.SightFieldOfViewDegrees != BinaryPrimitives.ReadSingleLittleEndian(dnam[28..]) ||
            weapon.FirstPersonIronSightsAnimation != ((dnam[12] & 0x40) == 0) ||
            weapon.ThirdPersonIronSightsAnimation != ((BinaryPrimitives.ReadUInt32LittleEndian(dnam[56..]) & 0x100) == 0))
            throw new InvalidDataException("Original BB gun sight fields disagree with its winning declaration.");
        var settings = FalloutInstallationSettings.Read(content);
        var world = FalloutCameraProjection.Read(settings);
        var sight = new FalloutWeaponSight(records, world.ReferenceHorizontalFovDegrees);
        var duration = FalloutGameSettingFloats.Read(records, "fIronSightsFOVTimeChange");
        sight.Advance(duration, weapon, true);
        if (MathF.Abs(sight.HorizontalDegrees - weapon.SightFieldOfViewDegrees) > .0001f ||
            weapon.AimGroup(true, true) != "2hraim" || weapon.AimGroup(false, true) != "2hraimis")
            throw new InvalidDataException("Original BB gun sight publication or animation exclusion failed.");
        sight.Advance(duration, weapon, false);
        if (MathF.Abs(sight.HorizontalDegrees - world.ReferenceHorizontalFovDegrees) > .0001f ||
            !hash.SequenceEqual(SHA256.HashData(selected.ReadData())))
            throw new InvalidDataException("Sight release changed the world projection or source bytes.");
        Console.WriteLine($"OPENNV_OWNED_WEAPON_SIGHT_PASS weapon={weapon.Form} winner={selected.Plugin.Name} sight={weapon.SightFieldOfViewDegrees:R} " +
            $"duration={duration:R} firstGroup={weapon.AimGroup(true, true)} thirdGroup={weapon.AimGroup(false, true)} " +
            "sourceReadonly=true fixture=isolated-source-sight-clock nativePublication=separate shotAndRetailParity=unverified");
    }
}
