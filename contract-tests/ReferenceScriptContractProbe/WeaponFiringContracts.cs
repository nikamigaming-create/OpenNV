using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class WeaponFiringContracts
{
    internal static void Run()
    {
        VatsSelectionCameraContracts();
        var skillBadgeData = new byte[204];
        BinaryPrimitives.WriteInt32LittleEndian(skillBadgeData.AsSpan(104), 41);
        BinaryPrimitives.WriteInt32LittleEndian(skillBadgeData.AsSpan(168), 7);
        BinaryPrimitives.WriteInt32LittleEndian(skillBadgeData.AsSpan(200), 25);
        var badge = FalloutWeaponSkillBadge.Read(skillBadgeData);
        Require(badge.ActorValue == 41 && badge.Requirement == 25 &&
            badge.Icon == @"Interface\Icons\TypeIcons\weap_skill_icon_sm_arms.dds",
            "Inventory weapon badge used the STR field or a shot/ammunition family admission.");
        foreach (var skill in new[] { 33, 34, 35, 38, 41, 45 })
            Require(FalloutWeaponSkillBadge.IconFor(skill) is not null, "Source weapon skill icon is missing.");
        Require(FalloutWeaponSkillBadge.IconFor(36) is null && FalloutWeaponSkillBadge.IconFor(-1) is null,
            "Unknown source skill minted a fallback weapon badge.");
        BinaryPrimitives.WriteInt32LittleEndian(skillBadgeData.AsSpan(200), -1);
        Require(FalloutWeaponSkillBadge.Read(skillBadgeData).Requirement == -1,
            "Source signed requirement formatting was silently clamped.");
        var missingSkillSuffixRefused = false;
        try { _ = FalloutWeaponSkillBadge.Read(new byte[172]); }
        catch (NotSupportedException) { missingSkillSuffixRefused = true; }
        Require(missingSkillSuffixRefused, "Unknown minimum-skill suffix initialization was invented.");
        var directory = Path.Combine(Path.GetTempPath(), "opennv-firing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var header = new byte[12]; Float(header, 0, 1.34f);
            var weaponData = new byte[204]; UInt(weaponData, 0, 3); Float(weaponData, 4, 1); weaponData[14] = 2;
            Float(weaponData, 28, 55); weaponData[12] = 0x40;
            UInt(weaponData, 104, 41); Float(weaponData, 116, 1.5f); Float(weaponData, 124, .4f);
            weaponData[41] = 32; weaponData[42] = 1; UInt(weaponData, 36, 3); Float(weaponData, 60, 1.25f);
            var economics = new byte[15]; BinaryPrimitives.WriteInt32LittleEndian(economics.AsSpan(4), 100); economics[12] = 16; economics[14] = 5;
            var projectile = new byte[84]; projectile[0] = 1; projectile[2] = 1; Float(projectile, 8, 200); Float(projectile, 12, 1000);
            var overrideProjectile = (byte[])projectile.Clone(); Float(overrideProjectile, 12, 2400);
            var ammo = new byte[20]; UInt(ammo, 4, 4); UInt(ammo, 12, 5); Float(ammo, 16, 100);
            var explosionData = new byte[52]; Float(explosionData, 4, 14); Float(explosionData, 8, 160);
            var explosiveProjectile = new byte[84]; explosiveProjectile[0] = 2; explosiveProjectile[2] = 1;
            Float(explosiveProjectile, 8, 200); Float(explosiveProjectile, 12, 1000); UInt(explosiveProjectile, 36, 12);
            var mineProjectile = new byte[84]; mineProjectile[0] = 0x66; mineProjectile[2] = 2;
            Float(mineProjectile, 8, 400); Float(mineProjectile, 12, 10000); Float(mineProjectile, 28, 100);
            Float(mineProjectile, 32, 3); UInt(mineProjectile, 36, 12);
            var invalidMineProjectile = (byte[])mineProjectile.Clone(); Float(invalidMineProjectile, 28, -1);
            var detonatesProjectile = (byte[])mineProjectile.Clone(); detonatesProjectile[0] = 0x62; detonatesProjectile[1] = 4;
            var timedMineProjectile = (byte[])mineProjectile.Clone(); timedMineProjectile[0] = 0x06; timedMineProjectile[1] = 0x08;
            Float(timedMineProjectile, 28, 0); Float(timedMineProjectile, 32, 15);
            var beamProjectile = new byte[84]; beamProjectile[2] = 4; Float(beamProjectile, 8, 1500); Float(beamProjectile, 12, 1200);
            Float(beamProjectile, 48, .14f);
            var alternateBeamProjectile = (byte[])beamProjectile.Clone(); alternateBeamProjectile[0] = 4;
            var timedBeamProjectile = (byte[])alternateBeamProjectile.Clone(); Float(timedBeamProjectile, 28, 1);
            var alternateFlightProjectile = new byte[84]; alternateFlightProjectile[0] = 0x8c; alternateFlightProjectile[1] = 0x02; alternateFlightProjectile[2] = 1;
            Float(alternateFlightProjectile, 8, 200); Float(alternateFlightProjectile, 12, 1000);
            var signedBounceProjectile = (byte[])alternateFlightProjectile.Clone(); Float(signedBounceProjectile, 80, -1.98179996f);
            var nonfiniteBounceProjectile = (byte[])signedBounceProjectile.Clone(); Float(nonfiniteBounceProjectile, 80, float.NaN);
            var explosiveBeamProjectile = (byte[])beamProjectile.Clone(); explosiveBeamProjectile[0] = 2; UInt(explosiveBeamProjectile, 36, 12);
            var flameProjectile = new byte[84]; flameProjectile[0] = 0x8d; flameProjectile[2] = 8;
            Float(flameProjectile, 8, 12000); Float(flameProjectile, 12, 640);
            var missileFlameProjectile = (byte[])flameProjectile.Clone(); missileFlameProjectile[0] = 0x8c;
            var flameWeapon = (byte[])weaponData.Clone(); UInt(flameWeapon, 0, 8); flameWeapon[41] = 86; UInt(flameWeapon, 36, 25);
            var thrownWeapon = (byte[])weaponData.Clone(); UInt(thrownWeapon, 0, 13); thrownWeapon[41] = 114; UInt(thrownWeapon, 36, 13);
            var mineWeapon = (byte[])weaponData.Clone(); UInt(mineWeapon, 0, 11); mineWeapon[41] = 102;
            var lunchboxMine = (byte[])weaponData.Clone(); UInt(lunchboxMine, 0, 12); lunchboxMine[41] = 108;
            var melee1hmWeapon = (byte[])weaponData.Clone(); UInt(melee1hmWeapon, 0, 1); Float(melee1hmWeapon, 8, 0.6f);
            melee1hmWeapon[41] = 26; UInt(melee1hmWeapon, 36, 0); melee1hmWeapon[42] = 0;
            var melee2hmWeapon = (byte[])weaponData.Clone(); UInt(melee2hmWeapon, 0, 2); Float(melee2hmWeapon, 8, 1.2f);
            melee2hmWeapon[41] = 32; UInt(melee2hmWeapon, 36, 0); melee2hmWeapon[42] = 0;
            var meleeEconomics = (byte[])economics.Clone(); BinaryPrimitives.WriteInt16LittleEndian(meleeEconomics.AsSpan(12), 25);
            var impacts = new byte[48]; UInt(impacts, 4 * 4, 7); UInt(impacts, 4, 1);
            var impactData = new byte[24]; Float(impactData, 0, .25f); UInt(impactData, 4, 2); Float(impactData, 8, 90); Float(impactData, 12, 16);
            var projectileCountAmmunition = new[] { 3u, 0x100u, 0x101u, 129u, uint.MaxValue, 128u }
                .Select((count, index) =>
                {
                    var countAmmo = (byte[])ammo.Clone(); UInt(countAmmo, 0, count);
                    return Record("AMMO", checked((uint)(0x901 + index)), Field("DATA", new byte[13]), Field("DAT2", countAmmo));
                }).SelectMany(record => record).ToArray();
            var projectileModWeapons = new[]
            {
                (Id: 0x931u, Count: (byte)1, Value: 2f, Duplicate: true),
                (Id: 0x932u, Count: (byte)3, Value: -.5f, Duplicate: false),
                (Id: 0x933u, Count: (byte)254, Value: 3f, Duplicate: false),
                (Id: 0x934u, Count: (byte)0, Value: 2f, Duplicate: false),
                (Id: 0x935u, Count: (byte)1, Value: -1f, Duplicate: false),
                (Id: 0x936u, Count: (byte)1, Value: -MathF.Pow(2, -60), Duplicate: false),
                (Id: 0x937u, Count: (byte)1, Value: float.MaxValue, Duplicate: false),
                (Id: 0x938u, Count: (byte)3, Value: -4f, Duplicate: false),
                (Id: 0x939u, Count: (byte)3, Value: .1f, Duplicate: false),
                (Id: 0x93au, Count: (byte)1, Value: MathF.Pow(2, -60), Duplicate: false),
            }.Select(value =>
            {
                var dnam = (byte[])weaponData.Clone(); dnam[42] = value.Count;
                UInt(dnam, 140, 12); Float(dnam, 152, value.Value);
                if (value.Duplicate) { UInt(dnam, 144, 12); Float(dnam, 156, 9); }
                var fields = new List<byte[]> { Field("DATA", economics), Field("DNAM", dnam), Field("WMI1", BitConverter.GetBytes(0x940u)) };
                if (value.Duplicate) fields.Add(Field("WMI2", BitConverter.GetBytes(0x941u)));
                return Record("WEAP", value.Id, fields.ToArray());
            }).SelectMany(record => record).Concat(Record("IMOD", 0x940)).Concat(Record("IMOD", 0x941)).ToArray();
            var loopRateDnam = (byte[])weaponData.Clone(); loopRateDnam[12] |= 2; loopRateDnam[41] = 74;
            Float(loopRateDnam, 60, 1.5f); Float(loopRateDnam, 64, 3); Float(loopRateDnam, 88, 71);
            UInt(loopRateDnam, 140, 8); Float(loopRateDnam, 152, .25f);
            UInt(loopRateDnam, 144, 8); Float(loopRateDnam, 156, .75f);
            var loopRateWeapon = Record("WEAP", 0x950, Field("DATA", economics), Field("DNAM", loopRateDnam),
                Field("ETYP", BitConverter.GetBytes(2)),
                Field("WMI1", BitConverter.GetBytes(0x940u)), Field("WMI2", BitConverter.GetBytes(0x941u)));
            var shortDnamWeaponRecords = new[] { 164, 172, 180, 196 }.Select((extent, index) =>
            {
                var dnam = weaponData[..extent];
                if (index == 1) { dnam[12] = 0; UInt(dnam, 56, 0x100); Float(dnam, 28, 37); }
                if (extent >= 172) UInt(dnam, 168, 7);
                if (extent >= 180) Float(dnam, 176, .3f);
                return Record("WEAP", checked((uint)(30 + index)), Field("EDID", Text("TestWeaponDnam" + extent)),
                    Field("MODL", Text("test-layout-weapon.nif")), Field("DATA", economics),
                    Field("ETYP", BitConverter.GetBytes(1)), Field("DNAM", dnam), Field("NAM0", BitConverter.GetBytes(2u)));
            }).ToArray();
            var rechargeableDnam = (byte[])weaponData.Clone(); Float(rechargeableDnam, 176, .3f);
            var unarmedDnam = (byte[])melee1hmWeapon.Clone(); UInt(unarmedDnam, 0, 0); Float(unarmedDnam, 8, 0); UInt(unarmedDnam, 104, 45);
            var unarmedData = (byte[])meleeEconomics.Clone(); BinaryPrimitives.WriteInt16LittleEndian(unarmedData.AsSpan(12), 0);
            var rechargeableAmmo = (byte[])ammo.Clone(); UInt(rechargeableAmmo, 12, 0); Float(rechargeableAmmo, 16, 0);
            File.WriteAllBytes(Path.Combine(directory, "Test.esm"), Record("TES4", 0, Field("HEDR", header))
                .Concat(Record("WEAP", 1, Field("EDID", Text("TestGun")), Field("MODL", Text("test.nif")),
                    Field("NNAM", Text("SourceEmitter")),
                    Field("DATA", economics), Field("ETYP", BitConverter.GetBytes(1)), Field("DNAM", weaponData), Field("NAM0", BitConverter.GetBytes(2u)), Field("MOD2", Text("Projectiles/test-case.nif")),
                    Field("INAM", BitConverter.GetBytes(6u))))
                .Concat(Record("AMMO", 2, Field("EDID", Text("TestAmmo")), Field("DATA", new byte[13]), Field("DAT2", ammo)))
                .Concat(Record("WEAP", 0x960, Field("EDID", Text("TestRecharger")), Field("DATA", economics),
                    Field("ETYP", BitConverter.GetBytes(1)), Field("DNAM", rechargeableDnam), Field("NAM0", BitConverter.GetBytes(0x961u))))
                .Concat(Record("AMMO", 0x961, Field("EDID", Text("TestGeneratedAmmo")), Field("DATA", new byte[13]), Field("DAT2", rechargeableAmmo)))
                .Concat(Record("AMMO", 27, Field("EDID", Text("TestFlameFuel")), Field("DATA", new byte[13])))
                .Concat(Record("PROJ", 3, Field("DATA", projectile)))
                .Concat(Record("PROJ", 4, Field("DATA", overrideProjectile)))
                .Concat(Record("EXPL", 12, Field("DATA", explosionData)))
                .Concat(Record("PROJ", 13, Field("MODL", Text("Effects/rocket.nif")), Field("DATA", explosiveProjectile)))
                .Concat(Record("PROJ", 20, Field("DATA", mineProjectile)))
                .Concat(Record("PROJ", 21, Field("DATA", invalidMineProjectile)))
                .Concat(Record("PROJ", 22, Field("DATA", detonatesProjectile)))
                .Concat(Record("PROJ", 23, Field("DATA", timedMineProjectile)))
                .Concat(Record("PROJ", 17, Field("MODL", Text("Effects/beam.nif")), Field("DATA", beamProjectile)))
                .Concat(Record("PROJ", 18, Field("MODL", Text("Effects/alternate-beam.nif")), Field("DATA", alternateBeamProjectile)))
                .Concat(Record("PROJ", 19, Field("MODL", Text("Effects/explosive-beam.nif")), Field("DATA", explosiveBeamProjectile)))
                .Concat(Record("PROJ", 28, Field("MODL", Text("Effects/timed-beam.nif")), Field("DATA", timedBeamProjectile)))
                .Concat(Record("PROJ", 29, Field("MODL", Text("Effects/alternate-flight.nif")), Field("DATA", alternateFlightProjectile)))
                .Concat(Record("PROJ", 0x962, Field("MODL", Text("Effects/signed-flight.nif")), Field("DATA", signedBounceProjectile)))
                .Concat(Record("PROJ", 0x963, Field("DATA", nonfiniteBounceProjectile)))
                .Concat(Record("PROJ", 25, Field("DATA", flameProjectile)))
                .Concat(Record("PROJ", 26, Field("MODL", Text("Effects/flame.nif")), Field("DATA", missileFlameProjectile)))
                .Concat(Record("WEAP", 24, Field("EDID", Text("TestFlamer")), Field("MODL", Text("test-flamer.nif")),
                    Field("DATA", economics), Field("ETYP", BitConverter.GetBytes(10)), Field("DNAM", flameWeapon)))
                .Concat(Record("WEAP", 14, Field("EDID", Text("TestThrownExplosive")), Field("MODL", Text("test-thrown.nif")),
                    Field("DATA", economics), Field("ETYP", BitConverter.GetBytes(1)), Field("DNAM", thrownWeapon)))
                .Concat(Record("WEAP", 15, Field("EDID", Text("TestMine")), Field("MODL", Text("test-mine.nif")),
                    Field("DATA", economics), Field("ETYP", BitConverter.GetBytes(3)), Field("DNAM", mineWeapon)))
                .Concat(Record("WEAP", 16, Field("EDID", Text("TestLunchboxMine")), Field("MODL", Text("test-lunchbox-mine.nif")),
                    Field("DATA", economics), Field("ETYP", BitConverter.GetBytes(6)), Field("DNAM", lunchboxMine)))
                .Concat(Record("WEAP", 50, Field("EDID", Text("Test1hmKnife")), Field("MODL", Text("test-knife.nif")),
                    Field("DATA", meleeEconomics), Field("ETYP", BitConverter.GetBytes(3)), Field("DNAM", melee1hmWeapon)))
                .Concat(Record("WEAP", 51, Field("EDID", Text("Test2hmHammer")), Field("MODL", Text("test-hammer.nif")),
                    Field("DATA", meleeEconomics), Field("ETYP", BitConverter.GetBytes(4)), Field("DNAM", melee2hmWeapon)))
                .Concat(Record("WEAP", 0x1f4, Field("DATA", unarmedData), Field("ETYP", BitConverter.GetBytes(4)), Field("DNAM", unarmedDnam)))
                .Concat(Record("MISC", 5, Field("EDID", Text("TestCasing")), Field("DATA", new byte[8])))
                .Concat(Record("IPDS", 6, Field("DATA", impacts)))
                .Concat(Record("IPCT", 7, Field("DATA", impactData), Field("MODL", Text("Effects/test-metal.nif"))))
                .Concat(Record("IPDS", 8, Field("DATA", impacts[..^4])))
                .Concat(Record("GMST", 9, Field("EDID", Text("fDamageWeaponMult")), Field("DATA", BitConverter.GetBytes(2f))))
                .Concat(Record("GMST", 10, Field("EDID", Text("fDamageSkillBase")), Field("DATA", BitConverter.GetBytes(.25f))))
                .Concat(Record("GMST", 11, Field("EDID", Text("fDamageSkillMult")), Field("DATA", BitConverter.GetBytes(.75f))))
                .Concat(Record("GMST", 52, Field("EDID", Text("fCombatDistance")), Field("DATA", BitConverter.GetBytes(128.0f))))
                .Concat(Record("GMST", 53, Field("EDID", Text("fCombatHitConeAngle")), Field("DATA", BitConverter.GetBytes(35.0f))))
                .Concat(Record("GMST", 54, Field("EDID", Text("fIronSightsFOVTimeChange")), Field("DATA", BitConverter.GetBytes(.25f))))
                .Concat(Record("CELL", 0x800, Field("DATA", [1])))
                .Concat(Record("NPC_", 0x801, Field("EDID", Text("AmmoOwner"))))
                .Concat(Record("FACT", 0x802, Field("EDID", Text("AmmoFaction"))))
                .Concat(Record("GLOB", 0x803, Field("EDID", Text("AmmoOwnershipGlobal")), Field("FNAM", [(byte)'f']), Field("FLTV", BitConverter.GetBytes(1f))))
                .Concat(shortDnamWeaponRecords.SelectMany(record => record)).Concat(projectileCountAmmunition).Concat(projectileModWeapons).Concat(loopRateWeapon).ToArray());
            using var records = FalloutPluginStack.Load(directory, ["Test.esm"]);
            FalloutFormKey Key(uint id) => new("Test.esm", id);
            var weapon = FalloutWeaponPresentation.Read(records, Key(1));
            Require(weapon.EmbeddedNodeName == "SourceEmitter" &&
                weapon.ResolveEmbeddedProjectileNode(["ProjectileNode_Left", "SourceEmitter", "ProjectileNode_Right"]) == "SourceEmitter",
                "Embedded WEAP declaration lost its exact source node among multiple emitters.");
            Reject(() => weapon.ResolveEmbeddedProjectileNode(["ProjectileNode_Left", "ProjectileNode_Right"]));
            Reject(() => weapon.ResolveEmbeddedProjectileNode(["SourceEmitter", "SourceEmitter"]));
            var unnamed = weapon with { EmbeddedNodeName = null };
            Require(unnamed.ResolveEmbeddedProjectileNode(["OtherBone", "ProjectileNode_Only"]) == "ProjectileNode_Only",
                "Undeclared unique embedded emitter lost existing admission.");
            Reject(() => unnamed.ResolveEmbeddedProjectileNode(["ProjectileNode_Left", "ProjectileNode_Right"]));

            Require(weapon.SightFieldOfViewDegrees == 55 && weapon.AimGroup(true, true) == "1hpaim" &&
                weapon.AimGroup(false, true) == "1hpaimis", "First-person sight exclusion also changed third-person or lost the source angle.");
            CheckSight(records, weapon);
            Require(FalloutWeaponShot.Read(records, Key(1), Key(0x901)).Projectiles == 6 &&
                FalloutWeaponShot.Read(records, Key(1), Key(0x902)).Projectiles == weaponData[42] &&
                FalloutWeaponShot.Read(records, Key(1), Key(0x903)).Projectiles == 2 &&
                FalloutWeaponShot.Read(records, Key(1), Key(0x904)).Projectiles == 2 &&
                FalloutWeaponShot.Read(records, Key(1), Key(0x905)).Projectiles == 254,
                "AMMO count did not preserve source low-byte override, AmmoUse multiplication or WEAP fallback.");
            Reject(() => FalloutWeaponShot.Read(records, Key(1), Key(0x906)).RequireRuntimeAttackOwner());
            FalloutWeaponShot ModifiedShot(uint id, byte mask = 1) => FalloutWeaponModifications.Apply(records,
                FalloutWeaponShot.Read(records, Key(id), Key(2)), mask);
            Require(ModifiedShot(0x931, 0).Projectiles == 1 && ModifiedShot(0x931, 2).Projectiles == 3 &&
                ModifiedShot(0x931, 3).Projectiles == 3 && ModifiedShot(0x932).Projectiles == 2 &&
                ModifiedShot(0x933).Projectiles == 1 && ModifiedShot(0x938).Projectiles == 255 &&
                ModifiedShot(0x939).Projectiles == 3 && ModifiedShot(0x93a).Projectiles == 1,
                "Projectile modifier lost installed admission, first-authored priority, sum truncation or byte storage.");
            var rescued = ModifiedShot(0x934);
            Require(rescued.Projectiles == 2, "A source projectile modifier could not rescue its zero base count.");
            rescued.RequireRuntimeAttackOwner();
            Require(ModifiedShot(0x935).Projectiles == 0, "Zero source result was replaced with invented projectiles.");
            Reject(() => ModifiedShot(0x935).RequireRuntimeAttackOwner());
            Reject(() => (ModifiedShot(0x935) with { Projectile = FalloutProjectile.Read(records, Key(26)) }).RequireRuntimeAttackOwner());
            Reject(() => ModifiedShot(0x936)); Reject(() => ModifiedShot(0x937));
            var shot = FalloutWeaponShot.Read(records, Key(1), Key(2));
            WeaponHandlingContracts.RechargeShot(records, FalloutWeaponPresentation.Read(records, Key(0x960)),
                FalloutWeaponShot.Read(records, Key(0x960), Key(0x961)));
            WeaponInstanceMagazineContracts.Run(records, weapon, shot);
            shot.RequireInstantRay();
            foreach (var (extent, index) in new[] { 164, 172, 180, 196 }.Select((extent, index) => (extent, index)))
            {
                var key = Key(checked((uint)(30 + index)));
                var sourceWeapon = FalloutWeaponPresentation.Read(records, key, firstPerson: false);
                Require(sourceWeapon.AmmoRegenerationSeconds == (extent >= 180 ? .3f : 0),
                    "Source recharge interval was lost, rounded or fabricated for an older DNAM extent.");
                Require(sourceWeapon.SightFieldOfViewDegrees == (index == 1 ? 37 : 55) &&
                    sourceWeapon.FirstPersonIronSightsAnimation == (index == 1) &&
                    sourceWeapon.ThirdPersonIronSightsAnimation == (index != 1), "Short WEAP lost independent sight fields.");
                var sourceShot = FalloutWeaponShot.Read(records, key, Key(2));
                sourceShot.RequireRuntimeAttackOwner();
                var inventoryForCondition = new FalloutPlayerInventory();
                inventoryForCondition.Add(records, key, 1, 1, true);
                var wornWeapon = FalloutWeaponCondition.AfterShot(records, sourceWeapon, sourceShot,
                    inventoryForCondition.Item(key)!);
                Require(sourceShot.StrengthRequirement == (extent >= 172 ? 7 : 0) && sourceShot.SkillRequirement == 0 &&
                    MathF.Abs((wornWeapon.Variants?.Single().Condition ?? 0) - .998f) < .0001f,
                    $"WEAP DNAM extent {extent} lost an available field or bypassed its condition owner.");
            }
            Require(MathF.Abs(shot.Projectile.HitscanImpactDelaySeconds(5, .01f) - 2.5f) < .00001f &&
                BeamProjectileSpeedDelay(records, Key(17)) == 0,
                "Hitscan source speed did not determine impact time, or an unflagged beam gained projectile delay.");
            var ordinaryBullet = shot with { Projectile = shot.Projectile with { Flags = 0x0089 } };
            ordinaryBullet.RequireRuntimeAttackOwner();
            var alternateTriggerBullet = shot with { Projectile = shot.Projectile with { Flags = 0x008d } };
            alternateTriggerBullet.RequireRuntimeAttackOwner();
            var transparentPassThroughBullet = shot with { Projectile = shot.Projectile with { Flags = 0x028d } };
            transparentPassThroughBullet.RequireRuntimeAttackOwner();
            Require(ordinaryBullet.Projectile.HasMuzzleFlash && ordinaryBullet.Projectile.HasSupersonicAudio &&
                transparentPassThroughBullet.Projectile.PassesThroughSmallTransparent,
                "Supported source Hitscan appearance and collision flags were not decoded.");
            var beam = (shot with { Projectile = FalloutProjectile.Read(records, Key(17)) });
            Require(!beam.Projectile.Hitscan && beam.Projectile.IsInstantRayAttack,
                "Beam type did not select the direct-ray path without the Hitscan flag.");
            Require(beam.Projectile.FadeSeconds == .14f, "Beam lifetime was not read from source DATA.");
            beam.RequireRuntimeAttackOwner();
            var alternateBeam = shot with { Projectile = FalloutProjectile.Read(records, Key(18)) };
            Require(alternateBeam.Projectile.HasAlternateTrigger && !alternateBeam.Projectile.HasAlternateTriggerParameters,
                "Zero-data beam alternate-trigger flag was lost.");
            alternateBeam.RequireRuntimeAttackOwner();
            var alternateFlight = shot with { Projectile = FalloutProjectile.Read(records, Key(29)) };
            Require(!alternateFlight.Projectile.IsInstantRayAttack && alternateFlight.Projectile.HasAlternateTrigger &&
                alternateFlight.Projectile.PassesThroughSmallTransparent,
                "Source alternate-trigger projectile flags were not retained for in-flight shots.");
            alternateFlight.RequireRuntimeAttackOwner();
            var signedBounce = FalloutProjectile.Read(records, Key(0x962));
            Require(BitConverter.SingleToInt32Bits(signedBounce.BouncyMultiplier) == BitConverter.SingleToInt32Bits(-1.98179996f),
                "Source signed Bouncy Mult lost its exact serialized value.");
            var signedFlightRefused = false;
            try { (alternateFlight with { Projectile = signedBounce }).RequireRuntimeAttackOwner(); }
            catch (NotSupportedException error) when (error.Message.Contains("signed Bouncy Mult", StringComparison.Ordinal))
            { signedFlightRefused = true; }
            Require(signedFlightRefused, "Unqualified signed Bouncy Mult reached flight admission.");
            var nonfiniteBounceRefused = false;
            try { _ = FalloutProjectile.Read(records, Key(0x963)); }
            catch (InvalidDataException) { nonfiniteBounceRefused = true; }
            Require(nonfiniteBounceRefused, "Nonfinite Bouncy Mult passed source parsing.");
            var flameShot = FalloutWeaponShot.Read(records, Key(24), Key(27));
            Require(flameShot.Projectile.Type == 8 && flameShot.Projectile.Hitscan && flameShot.Projectile.HasAlternateTrigger &&
                flameShot.Projectile.PassesThroughActors && flameShot.Projectile.Speed == 12000 && flameShot.Projectile.Range == 640,
                "Source Flame projectile fields or actor pass-through behavior were not retained.");
            flameShot.RequireRuntimeAttackOwner();
            var missileFlame = FalloutProjectile.Read(records, Key(26));
            Require(!missileFlame.Hitscan && missileFlame.PassesThroughActors && missileFlame.Model == "meshes/Effects/flame.nif",
                "Non-hitscan Flame projectile did not retain missile-flight presentation.");
            (flameShot with { Projectile = missileFlame }).RequireRuntimeAttackOwner();
            var explosiveShot = FalloutProjectile.Read(records, Key(13));
            Require(explosiveShot.ExplosionSource is { Damage: 14, Radius: 160 } && explosiveShot.Model == "meshes/Effects/rocket.nif",
                "Projectile did not resolve its winning EXPL record and source model.");
            (shot with { Projectile = explosiveShot }).RequireRuntimeAttackOwner();
            var explosiveRay = shot with { Projectile = explosiveShot with { Flags = 3 } };
            explosiveRay.RequireRuntimeAttackOwner();
            Require(explosiveRay.Projectile.IsInstantRayAttack && explosiveRay.Projectile.ExplosionSource?.Form == Key(12) &&
                explosiveRay.Projectile.HitscanImpactDelaySeconds(5, .01f) > 0,
                "Explosive Hitscan lost its actual EXPL or source contact delay.");
            Reject(() => (explosiveRay with { Projectile = explosiveRay.Projectile with { Flags = 7 } }).RequireRuntimeAttackOwner());
            Reject(() => (explosiveRay with { Projectile = explosiveRay.Projectile with { Flags = 0x403 } }).RequireRuntimeAttackOwner());
            (explosiveRay with { Projectile = explosiveRay.Projectile with { Type = 4 } }).RequireRuntimeAttackOwner();
            var explosiveBeam = shot with { Projectile = FalloutProjectile.Read(records, Key(19)) };
            explosiveBeam.RequireRuntimeAttackOwner();
            Require(explosiveBeam.Projectile.Type == 4 && !explosiveBeam.Projectile.Hitscan &&
                explosiveBeam.Projectile.ExplosionSource?.Form == Key(12) &&
                explosiveBeam.Projectile.HitscanImpactDelaySeconds(5, .01f) == 0,
                "Explosive beam lost its immediate ray contact or source EXPL owner.");
            (explosiveBeam with { Projectile = explosiveBeam.Projectile with { Flags = 0x020a } }).RequireRuntimeAttackOwner();
            Reject(() => (explosiveBeam with { Projectile = explosiveBeam.Projectile with { Flags = 6 } }).RequireRuntimeAttackOwner());
            Reject(() => (explosiveBeam with { Projectile = explosiveBeam.Projectile with { Flags = 0x402 } }).RequireRuntimeAttackOwner());
            var mineProjectileSource = FalloutProjectile.Read(records, Key(20));
            Require(mineProjectileSource.Type == 2 && mineProjectileSource.HasAlternateTrigger && mineProjectileSource.CanBeDisabled &&
                mineProjectileSource.CanBePickedUp && mineProjectileSource.ExplosionAltTriggerProximity == 100 &&
                mineProjectileSource.ExplosionAltTriggerTimer == 3 && mineProjectileSource.ExplosionSource?.Form == Key(12),
                "Mine alternate-trigger flags, proximity, timer or explosion link were not decoded from PROJ DATA.");
            var detonatesProjectileSource = FalloutProjectile.Read(records, Key(22));
            Require(!detonatesProjectileSource.HasAlternateTrigger && detonatesProjectileSource.CanBeDisabled &&
                detonatesProjectileSource.CanBePickedUp && detonatesProjectileSource.Detonates,
                "Detonating projectile flags were confused with proximity triggering.");
            var remoteSource = FalloutMineSource.Read(records, Key(22)) ?? throw new InvalidOperationException("Remote source was rejected.");
            var armedRemote = FalloutMineState.Initial(remoteSource);
            var commandedRemote = armedRemote.CommandDetonation(remoteSource, Key(0x14));
            Require(detonatesProjectileSource.IsRemoteMine && !detonatesProjectileSource.IsProximityMine &&
                commandedRemote.Phase == FalloutMinePhase.Countdown && commandedRemote.RemainingSeconds == 0 &&
                commandedRemote.TriggerActor == Key(0x14) && armedRemote.Phase == FalloutMinePhase.Armed,
                "Remote explosive command lost its source distinction, actor attribution or deferred blast commit.");
            var disabledRemote = armedRemote.Disable(remoteSource);
            try { disabledRemote.CommandDetonation(remoteSource, Key(0x14)); throw new InvalidDataException("Disabled remote explosive was commanded."); }
            catch (InvalidOperationException) { }
            var timedMineProjectileSource = FalloutProjectile.Read(records, Key(23));
            Require(timedMineProjectileSource.HasAlternateTrigger && timedMineProjectileSource.HasExplicitRotation &&
                timedMineProjectileSource.ExplosionAltTriggerProximity == 0 && timedMineProjectileSource.ExplosionAltTriggerTimer == 15,
                "Timed, rotated mine fields were not decoded from the winning projectile declaration.");
            Reject(() => FalloutProjectile.Read(records, Key(21)));
            var thrownWeaponPresentation = FalloutWeaponPresentation.Read(records, Key(14));
            var thrownShot = FalloutWeaponShot.Read(records, Key(14), null, thrownWeaponPresentation.HasAmmunitionSource);
            Require(thrownWeaponPresentation.AnimationGroup == "1gt" && thrownWeaponPresentation.AttackGroup == "attackthrow" &&
                !thrownWeaponPresentation.IsMine && thrownShot.Projectile.ExplosionSource?.Form == Key(12),
                "Thrown explosive lost its authored animation or EXPL source.");
            thrownShot.RequireRuntimeAttackOwner();
            var thrownInventory = new FalloutPlayerInventory();
            thrownInventory.Add(records, Key(14), 2, 1, true); thrownInventory.Equip(records, Key(14));
            var thrownHandling = new FalloutWeaponHandling(thrownInventory);
            Require(thrownHandling.CanFire(thrownWeaponPresentation) && thrownInventory.Item(Key(14))!.Count == 2,
                "Preparing a throw consumed its source weapon.");
            Require(thrownHandling.ConsumeShot(thrownWeaponPresentation, thrownShot, records) && thrownInventory.Item(Key(14))!.Count == 1,
                "An accepted throw did not consume exactly one source weapon.");
            Require(thrownHandling.ConsumeShot(thrownWeaponPresentation, thrownShot, records) && thrownInventory.Item(Key(14)) is null &&
                !thrownInventory.Equipped.Contains(14u) && !thrownHandling.ConsumeShot(thrownWeaponPresentation, thrownShot, records),
                "Last thrown weapon remained equipped or could be thrown again.");
            var mine = FalloutWeaponPresentation.Read(records, Key(15));
            var lunchbox = FalloutWeaponPresentation.Read(records, Key(16));
            Require(mine.IsMine && !mine.IsMeleeWeapon && mine.WeaponAnimationType == 11 && mine.AttackGroup == "placemine" &&
                lunchbox.IsMine && !lunchbox.IsMeleeWeapon && lunchbox.WeaponAnimationType == 12 && lunchbox.AttackGroup == "placemine2",
                "Mine animation types were admitted as melee attacks or lost their placement groups.");
            var impact = FalloutImpact.Resolve(records, shot.ImpactDataSet!.Value, 4);
            Require(impact is { Duration: .25f, Orientation: 2, Model: "meshes/Effects/test-metal.nif" } && impact.Form == Key(7) &&
                FalloutImpact.Resolve(records, Key(6), 0) is null, "Impact material selected the wrong source record or replaced a null slot.");
            Reject(() => FalloutImpact.Resolve(records, Key(6), 1));
            Reject(() => FalloutImpact.Resolve(records, Key(8), 0));
            Require(shot.Projectile.Form == Key(4) && shot.Projectile.Range == 2400 && weapon.AttackGroup == "attackright" && weapon.AttackMultiplier == 1.25f &&
                weapon.ShellModel == "meshes/Projectiles/test-case.nif",
                "Ammo projectile override or attack declaration was lost.");
            var inventory = new FalloutPlayerInventory();
            var unarmed = FalloutWeaponPresentation.Read(records, FalloutDefaultUnarmedWeapon.Require(records));
            var bareInventory = new FalloutPlayerInventory();
            var bareDamage = new FalloutWeaponDamageResolver(records, bareInventory,
                value => value == 56 ? 20 : value == 45 ? 40 : throw new InvalidDataException("Unexpected unarmed damage AV."), () => []);
            Require(unarmed.DefaultUnarmed && unarmed.Model is null && unarmed.IsMeleeWeapon && unarmed.Reach == 0 &&
                bareDamage.Resolve(unarmed.Form, 0).Amount == 20 && bareDamage.Resolve(unarmed.Form, 3).Amount == 26 &&
                bareInventory.Items.Count == 0 && bareInventory.Equipped.Count == 0,
                "Default unarmed source required a model/item or scaled derived AV damage twice.");
            inventory.Add(records, Key(1), 1, 1, true); inventory.Add(records, Key(2), 9, 1, true); inventory.Equip(records, Key(1));
            var damage = new FalloutWeaponDamageResolver(records, inventory,
                value => value == 41 ? 40 : throw new InvalidDataException("Weapon requested the wrong source skill."), () => []);
            Require(MathF.Abs(damage.Resolve(shot).Amount - 17.6f) < .00001f && damage.Resolve(shot).LimbMultiplier == 1.5f,
                "Source weapon damage, skill, settings or limb multiplier changed.");
            var multiProjectileDamage = damage.Resolve(shot with { Projectiles = 7 });
            var slugDamage = damage.Resolve(shot with { Projectiles = 1 });
            Require(MathF.Abs(multiProjectileDamage.Amount * 7 - slugDamage.Amount) < .00001f &&
                multiProjectileDamage.LimbMultiplier == slugDamage.LimbMultiplier &&
                multiProjectileDamage.AmmoEffects == slugDamage.AmmoEffects &&
                damage.Resolve(shot with { Projectiles = 7 }, 14).Amount == damage.Resolve(shot, 14).Amount,
                "Multi-projectile contacts multiplied the discharge damage, lost target modifiers or divided independent blast damage.");
            var zeroProjectileDamageRefused = false;
            try { _ = damage.Resolve(shot with { Projectiles = 0 }); }
            catch (InvalidDataException) { zeroProjectileDamageRefused = true; }
            Require(zeroProjectileDamageRefused, "Zero-projectile source action produced damage.");
            var wornInventory = new FalloutPlayerInventory();
            wornInventory.Add(records, Key(1), 1, 1, true, extra: new(1, .25f));
            var worn = new FalloutWeaponDamageResolver(records, wornInventory, _ => 40, () => []);
            Require(MathF.Abs(worn.Resolve(shot).Amount - 11.704f) < .00001f,
                "NV damaged-weapon curve did not reach the ordinary damage resolver.");
            Require(FalloutWeaponDamageResolver.NewVegasConditionMultiplier(.75f) == 1 &&
                FalloutWeaponDamageResolver.NewVegasConditionMultiplier(1) == 1 &&
                MathF.Abs(FalloutWeaponDamageResolver.NewVegasConditionMultiplier(0) - .4975f) < .000001f,
                "NV condition threshold or zero-condition damage boundary changed.");
            Reject(() => FalloutWeaponDamageResolver.NewVegasConditionMultiplier(float.NaN));
            Reject(() => FalloutWeaponDamageResolver.NewVegasConditionMultiplier(-.01f));
            Reject(() => FalloutWeaponDamageResolver.NewVegasConditionMultiplier(1.01f));
            wornInventory.Add(records, Key(1), 1, 1, true, extra: new(1, .9f));
            var inventoryDamageRevision = wornInventory.Revision;
            var hovered = wornInventory.Instances(Key(1)).Single(instance => instance.Extra.Condition == .9f).Extra;
            var hoveredDamage = worn.ResolveInventory(Key(1), shot.BaseDamage, hovered, shot.AmmoEffects);
            Require(hoveredDamage.Condition == .9f && MathF.Abs(hoveredDamage.Amount - 17.6f) < .00001f &&
                wornInventory.Revision == inventoryDamageRevision,
                "Inventory damage used the first variant or mutated equipment while inspecting another carried instance.");
            var staleInventoryVariantRefused = false;
            try { _ = worn.ResolveInventory(Key(1), shot.BaseDamage, hovered with { Condition = .8f }, shot.AmmoEffects); }
            catch (InvalidOperationException) { staleInventoryVariantRefused = true; }
            Require(staleInventoryVariantRefused, "Inventory damage admitted a variant that is no longer carried.");
            Require(FalloutInventoryItemStatistics.ConditionValue(100, .5f, .2f, .08f, 2) == 180 &&
                FalloutInventoryItemStatistics.ConditionValue(.4f, 1, 1, 1, 1) == .4f &&
                FalloutInventoryItemStatistics.RoundIncrement(.15f, .1f) == .2f &&
                FalloutInventoryItemStatistics.RoundIncrement(1.4999999f, 1) == 1,
                "Inventory value lost condition scaling, fractional values or its native rounding boundaries.");
            Reject(() => FalloutInventoryItemStatistics.ConditionValue(100, float.NaN, .2f, .08f, 2));
            Reject(() => FalloutInventoryItemStatistics.RoundIncrement(float.PositiveInfinity, .1f));
            var mixedCondition = worn.Resolve(shot).Condition;
            Require(mixedCondition is .25f or .9f, "Mixed condition variants did not select one usable weapon instance.");
            var loopRate = FalloutWeaponPresentation.Read(records, Key(0x950));
            Require(loopRate.Automatic && loopRate.SourceFireRate == 3 && loopRate.AttackShotsPerSecond == 4.5f &&
                FalloutWeaponPresentation.Read(records, Key(0x950), weaponMods: 2).AttackShotsPerSecond == 5.25f &&
                FalloutWeaponPresentation.Read(records, Key(0x950), weaponMods: 3).AttackShotsPerSecond == 5.25f,
                "Gameplay loop cadence used animation-authoring shots/sec or stacked/multiplied the installed effect8 values.");
            var handling = new FalloutWeaponHandling(inventory);
            Require(!handling.ConsumeShot(weapon, shot, records), "Empty magazine consumed ammunition.");
            handling.CompleteReload(weapon);
            Require(handling.ConsumeShot(weapon, shot, records) && handling.Loaded(Key(1)) == 3 && inventory.Item(Key(2))!.Count == 7 && inventory.Item(Key(5))!.Count == 2,
                "Shot did not conserve source AmmoUse and recovered casings.");
            var autoInventory = new FalloutPlayerInventory(); var autoItems = inventory.Capture();
            autoInventory.Restore(autoItems.Inventory, autoItems.EquippedRuntimeFormIds.ToArray(), autoItems.InventoryRandomState);
            var autoHandling = new FalloutWeaponHandling(autoInventory);
            var autoInstance = autoInventory.SelectedWeapon(weapon.Form).Extra.Instance!.Value;
            autoInventory.SetWeaponMagazine(weapon.Form, autoInstance, new(shot.Ammunition!.Value, weapon.AmmoUse));
            var beforeAuto = autoHandling.Loaded(weapon.Form); var carriedBeforeAuto = autoInventory.Item(shot.Ammunition.Value)!.Count;
            Require(!autoHandling.NeedsAutomaticReload(weapon, 0) &&
                autoHandling.ConsumeShot(weapon, shot, records) && autoHandling.NeedsAutomaticReload(weapon, beforeAuto),
                "Actual last-round consumption did not admit source auto reload, or an empty query invented it.");
            var exhaustedInventory = new FalloutPlayerInventory(); var exhaustedItems = autoInventory.Capture();
            exhaustedInventory.Restore(exhaustedItems.Inventory, exhaustedItems.EquippedRuntimeFormIds.ToArray(), exhaustedItems.InventoryRandomState);
            var actualProcessAmmo = shot.Ammunition.Value;
            exhaustedInventory.Remove(actualProcessAmmo, exhaustedInventory.Item(actualProcessAmmo)!.Count, silent: true);
            var alternateAmmo = Key(27);
            exhaustedInventory.Add(records, alternateAmmo, 3, 1, true);
            var exhaustedHandling = new FalloutWeaponHandling(exhaustedInventory);
            var alternateWeapon = weapon with { Ammunition = [actualProcessAmmo, alternateAmmo] };
            Require(exhaustedHandling.CanReload(alternateWeapon) && !exhaustedHandling.NeedsAutomaticReload(alternateWeapon, beforeAuto),
                "Automatic reload cycled away from an exhausted actual process AMMO without its source selection producer.");
            autoHandling.CompleteReload(weapon);
            Require(autoHandling.Loaded(weapon.Form) == Math.Min(weapon.ClipSize, carriedBeforeAuto - weapon.AmmoUse) &&
                autoInventory.Item(shot.Ammunition.Value)!.Count == carriedBeforeAuto - weapon.AmmoUse &&
                !autoHandling.NeedsAutomaticReload(weapon, beforeAuto),
                "Auto reload repeated consumption, granted ammunition, or retained the empty transition.");
            var snapshot = handling.Capture(); var items = inventory.Capture();
            var coldInventory = new FalloutPlayerInventory(); coldInventory.Restore(items.Inventory, items.EquippedRuntimeFormIds.ToArray(), items.InventoryRandomState);
            var cold = new FalloutWeaponHandling(coldInventory); cold.Restore(snapshot, _ => weapon);
            var probabilistic = shot with { RecoveryPercent = 37 };
            Require(handling.ConsumeShot(weapon, probabilistic, records) && cold.ConsumeShot(weapon, probabilistic, records) &&
                JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(handling.Capture()) &&
                JsonSerializer.Serialize(coldInventory.Capture()) == JsonSerializer.Serialize(inventory.Capture()), "Cold shot changed inventory or the next recovery roll.");
            var unchanged = JsonSerializer.Serialize(handling.Capture());
            Require(!handling.ConsumeShot(weapon, shot, records) && JsonSerializer.Serialize(handling.Capture()) == unchanged,
                "Insufficient magazine rounds advanced the shot state.");
            handling.CompleteReload(weapon); handling.SetDrawn(false);
            Require(!handling.ConsumeShot(weapon, shot, records), "Holstered weapon fired.");
            handling.SetDrawn(true); inventory.Unequip(records, Key(1));
            Require(!handling.ConsumeShot(weapon, shot, records), "Unequipped weapon fired.");
            var beforeFailure = JsonSerializer.Serialize(inventory.Capture());
            Reject(() => inventory.ConsumeAmmunition(Key(2), 1, new(Key(5), 5, "invalid", "WEAP", 1, 0, 0)));
            Require(JsonSerializer.Serialize(inventory.Capture()) == beforeFailure, "Invalid return partially consumed ammunition.");
            Reject(() => (shot with { Projectile = shot.Projectile with { Flags = 0 } }).RequireInstantRay());
            Reject(() => (shot with { Projectile = shot.Projectile with { Speed = 0 } }).RequireInstantRay());
            Reject(() => shot.Projectile.HitscanImpactDelaySeconds(-.01f, .01f));
            Reject(() => (shot with { Projectile = shot.Projectile with { Flags = 0x0101 } }).RequireRuntimeAttackOwner());
            Reject(() => (shot with { Projectile = FalloutProjectile.Read(records, Key(28)) }).RequireRuntimeAttackOwner());
            Reject(() => (flameShot with { Projectile = flameShot.Projectile with { Flags = (ushort)(flameShot.Projectile.Flags | 0x0100) } }).RequireRuntimeAttackOwner());
            Reject(() => (shot with { Projectiles = 0 }).RequireInstantRay());
            Reject(() => (shot with { AmmoEffects = [new FalloutAmmoEffect(Key(5), FalloutAmmoEffect.Fatigue + 1, 0, 0)] }).RequireInstantRay());
            var knife = FalloutWeaponPresentation.Read(records, Key(50));
            var hammer = FalloutWeaponPresentation.Read(records, Key(51));
            Require(knife.IsMeleeWeapon && knife.AnimationGroup == "1hm" && knife.AttackGroup == "attackleft" && MathF.Abs(knife.Reach - 0.6f) < 0.001f,
                "1HM melee presentation layout failed.");
            Require(hammer.IsMeleeWeapon && hammer.AnimationGroup == "2hm" && hammer.AttackGroup == "attackright" && MathF.Abs(hammer.Reach - 1.2f) < 0.001f,
                "2HM melee presentation layout failed.");
            var reachDistance = knife.Reach * FalloutGameSettingFloats.Read(records, "fCombatDistance");
            Require(MathF.Abs(reachDistance - 76.8f) < 0.001f, "Melee reach distance calculation failed.");
            var hitConeAngle = FalloutGameSettingFloats.Read(records, "fCombatHitConeAngle");
            Require(MathF.Abs(hitConeAngle - 35.0f) < 0.001f, "Melee combat hit cone angle setting failed.");
            var meleeInventory = new FalloutPlayerInventory();
            meleeInventory.Add(records, Key(50), 1, 1, true);
            meleeInventory.Equip(records, Key(50));
            var meleeDamageResolver = new FalloutWeaponDamageResolver(records, meleeInventory, _ => 40, () => []);
            var meleeDamage = meleeDamageResolver.Resolve(knife.Form, 25);
            Require(meleeDamage.Amount > 0 && meleeDamage.LimbMultiplier == 1.5f, "Melee damage resolution failed.");
            var meleeHandling = new FalloutWeaponHandling(meleeInventory);
            Require(meleeHandling.CanUse(knife), "Melee weapon was not usable when equipped.");
            meleeHandling.ApplyMeleeConditionWear(knife, records);
            var wornKnife = meleeInventory.Item(Key(50))!;
            Require((wornKnife.Variants?.Single().Condition ?? 0) < 1.0f, "Melee strike did not apply weapon condition wear.");
            Console.WriteLine("OPENNV_WEAPON_FIRING_CONTRACT_PASS ammoUse=true recovery=true coldRandom=true emptyHolsteredUnequipped=true sourceOverride=true flameActorPassThrough=true hitscanDelay=true melee=true unsupported=true");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static float BeamProjectileSpeedDelay(FalloutPluginStack records, FalloutFormKey key)
    {
        var beam = FalloutProjectile.Read(records, key);
        return beam.HitscanImpactDelaySeconds(5, .01f);
    }
    private static void CheckSight(FalloutPluginStack records, FalloutWeaponPresentation weapon)
    {
        var sight = new FalloutWeaponSight(records, 75);
        Require(sight.Advance(0, weapon, true) == 75 && sight.Advance(.125, weapon, true) == 65 &&
            sight.Advance(.125, weapon, true) == 55, "Source sight angle or transition duration was not applied.");
        Require(sight.Advance(.125, weapon, false) == 65 && sight.Advance(.125, weapon, true) == 60 &&
            sight.Advance(.125, weapon, true) == 55, "A reversed sight transition jumped or kept the old target.");
        Require(records.NumericSettings.Set("fIronSightsFOVTimeChange", 0) && sight.Advance(0, weapon, false) == 75,
            "Live zero-duration sight setting did not release zoom.");
        foreach (var sentinel in new[] { 0f, 1, 5, 75 })
            Require(sight.Advance(0, weapon with { SightFieldOfViewDegrees = sentinel }, true) == 75,
                "Disabled or no-zoom source sight value narrowed the camera.");
        Require(!FalloutWeaponSight.CanAim(weapon with { SightFieldOfViewDegrees = 0 }) &&
            !FalloutWeaponSight.CanAim(weapon with { EquipmentType = 3, WeaponAnimationType = 1 }),
            "Disabled sights or melee received flat firearm aiming.");
        Require(sight.Advance(0, weapon with { SightFieldOfViewDegrees = 100 }, true) == 100,
            "A valid widening sight field of view was artificially restricted.");
        var projection = FalloutCameraProjection.FromReferenceFov(55, 2.5f);
        Require(MathF.Abs(projection.ReferenceHorizontalFovDegrees - 55) < .0001f &&
            projection.VerticalFovDegrees < 55, "Horizontal sight FOV was used as the vertical camera angle.");
        Reject(() => sight.Advance(-1, weapon, true));
        Reject(() => sight.Advance(0, weapon with { SightFieldOfViewDegrees = float.NaN }, true));
        Require(records.NumericSettings.Set("fIronSightsFOVTimeChange", -1), "Invalid fixture setting could not be supplied.");
        var previous = sight.HorizontalDegrees;
        Reject(() => sight.Advance(0, weapon, true));
        Require(sight.HorizontalDegrees == previous, "Invalid sight setting changed published projection.");
        Require(records.NumericSettings.Set("fIronSightsFOVTimeChange", .25), "Fixture setting did not restore.");
        Console.WriteLine("OPENNV_WEAPON_SIGHT_PASS sourceAngle=true independentAnimationFlags=true clock=true reversal=true liveSetting=true sentinels=true projection=true invalidAtomic=true retailTransition=unverified");
    }

    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Unsupported shot was admitted.");
    }
    private static void Float(byte[] bytes, int offset, float value) => BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), value);
    private static void UInt(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Field(string signature, byte[] payload)
    {
        var result = new byte[6 + payload.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)payload.Length)); payload.CopyTo(result, 6); return result;
    }
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var payload = fields.SelectMany(field => field).ToArray(); var result = new byte[24 + payload.Length];
        Encoding.ASCII.GetBytes(signature).CopyTo(result, 0); UInt(result, 4, (uint)payload.Length); UInt(result, 12, id); payload.CopyTo(result, 24); return result;
    }
    private static void VatsSelectionCameraContracts()
    {
        var source = new FalloutVatsSelectionCamera(46, 30, 1750, 1.5f, 2, 20, .4f, .2f, .1f);
        var near = source.Fov(100, 200, 1);
        var far = source.Fov(100, 1750, 1);
        var scaled = source.Fov(200, 3500, 1);
        const float units = .0142875f;
        var metreNear = source.Fov(100 * units, 200 * units, units);
        if (MathF.Abs(near.World - MathF.PI / 4 * (46 - 16 * (200f / 1750))) > .00001f ||
            near.FirstPerson != 20 || MathF.Abs(metreNear.World - near.World) > .00001f || MathF.Abs(far.World - (float)Math.Atan(200f / 1750) * 30) > .00001f ||
            MathF.Abs(scaled.World - far.World) > .00001f || source.Fov(0, 200, 1).World != 1.5f)
            throw new InvalidOperationException("VATS camera lost source bound/distance units, near/far interpolation, minimum or separate first-person cap.");
        var points = new System.Numerics.Vector3[] { new(5, 0, 0), new(-5, 2, 0) };
        if (FalloutVatsSelectionCamera.Focus(System.Numerics.Vector3.Zero, new(0, 0, 299), points, 1) != new System.Numerics.Vector3(0, 0, 299) ||
            FalloutVatsSelectionCamera.Focus(System.Numerics.Vector3.Zero, new(0, 0, 300), points, 1) != new System.Numerics.Vector3(0, 1, 0))
            throw new InvalidOperationException("VATS camera changed strict source head distance or the actual BPNT-node mean.");
        var forward = FalloutVatsSelectionCamera.Angles(new(0, 1, 0), 1);
        var right = FalloutVatsSelectionCamera.Angles(new(1, 0, 0), 1);
        var vertical = FalloutVatsSelectionCamera.Angles(new(0, 0, 1), 1);
        if (forward != (0f, 0f) || MathF.Abs(right.Yaw - MathF.PI / 2) > .000001f ||
            MathF.Abs(vertical.Pitch + MathF.PI / 2) > .000001f || MathF.Abs(vertical.Yaw + MathF.PI / 2) > .000001f)
            throw new InvalidOperationException("VATS camera changed native source axes, pitch sign or vertical-direction yaw.");
        try { _ = FalloutVatsSelectionCamera.Angles(System.Numerics.Vector3.Zero, 1); throw new InvalidOperationException("Degenerate VATS camera angle was admitted."); }
        catch (InvalidDataException) { }
        foreach (var duration in new[] { 0f, -1f })
        {
            var immediate = new FalloutVatsCameraTransition(10, 20, duration);
            if (!immediate.Complete || immediate.Current != 20 || immediate.Advance(.1f) != 20)
                throw new InvalidOperationException("VATS nonpositive source camera duration lost its immediate endpoint.");
        }
        var curve = new FalloutVatsCameraTransition(10, 20, 1);
        if (curve.Current != 10 || MathF.Abs(curve.Advance(.25f) - 11.188384f) > .0001f ||
            MathF.Abs(curve.Advance(.25f) - 15) > .00001f || curve.Complete || curve.Advance(.5f) != 20 || !curve.Complete)
            throw new InvalidOperationException("VATS camera transition changed native sample/global-progress interpolation or completion.");
        try { _ = source.Fov(1, 0, 1); throw new InvalidOperationException("Degenerate VATS camera distance was admitted."); }
        catch (InvalidDataException) { }
    }

}
