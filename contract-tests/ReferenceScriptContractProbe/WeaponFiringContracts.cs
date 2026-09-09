using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class WeaponFiringContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-firing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var header = new byte[12]; Float(header, 0, 1.34f);
            var weaponData = new byte[204]; UInt(weaponData, 0, 3); Float(weaponData, 4, 1); weaponData[14] = 2;
            UInt(weaponData, 104, 41); Float(weaponData, 116, 1.5f); Float(weaponData, 124, .4f);
            weaponData[41] = 32; weaponData[42] = 1; UInt(weaponData, 36, 3); Float(weaponData, 60, 1.25f);
            var economics = new byte[15]; economics[12] = 16; economics[14] = 5;
            var projectile = new byte[84]; projectile[0] = 1; projectile[2] = 1; Float(projectile, 8, 200); Float(projectile, 12, 1000);
            var overrideProjectile = (byte[])projectile.Clone(); Float(overrideProjectile, 12, 2400);
            var ammo = new byte[20]; UInt(ammo, 4, 4); UInt(ammo, 12, 5); Float(ammo, 16, 100);
            var impacts = new byte[48]; UInt(impacts, 4 * 4, 7); UInt(impacts, 4, 1);
            var impactData = new byte[24]; Float(impactData, 0, .25f); UInt(impactData, 4, 2); Float(impactData, 8, 90); Float(impactData, 12, 16);
            File.WriteAllBytes(Path.Combine(directory, "Test.esm"), Record("TES4", 0, Field("HEDR", header))
                .Concat(Record("WEAP", 1, Field("EDID", Text("TestGun")), Field("MODL", Text("test.nif")),
                    Field("DATA", economics), Field("DNAM", weaponData), Field("NAM0", BitConverter.GetBytes(2u)), Field("MOD2", Text("Projectiles/test-case.nif")),
                    Field("INAM", BitConverter.GetBytes(6u))))
                .Concat(Record("AMMO", 2, Field("EDID", Text("TestAmmo")), Field("DATA", new byte[13]), Field("DAT2", ammo)))
                .Concat(Record("PROJ", 3, Field("DATA", projectile)))
                .Concat(Record("PROJ", 4, Field("DATA", overrideProjectile)))
                .Concat(Record("MISC", 5, Field("EDID", Text("TestCasing")), Field("DATA", new byte[8])))
                .Concat(Record("IPDS", 6, Field("DATA", impacts)))
                .Concat(Record("IPCT", 7, Field("DATA", impactData), Field("MODL", Text("Effects/test-metal.nif"))))
                .Concat(Record("IPDS", 8, Field("DATA", impacts[..^4])))
                .Concat(Record("GMST", 9, Field("EDID", Text("fDamageWeaponMult")), Field("DATA", BitConverter.GetBytes(2f))))
                .Concat(Record("GMST", 10, Field("EDID", Text("fDamageSkillBase")), Field("DATA", BitConverter.GetBytes(.25f))))
                .Concat(Record("GMST", 11, Field("EDID", Text("fDamageSkillMult")), Field("DATA", BitConverter.GetBytes(.75f)))).ToArray());
            using var records = FalloutPluginStack.Load(directory, ["Test.esm"]);
            FalloutFormKey Key(uint id) => new("Test.esm", id);
            var weapon = FalloutWeaponPresentation.Read(records, Key(1));
            var shot = FalloutWeaponShot.Read(records, Key(1), Key(2));
            shot.RequireHitscan();
            var impact = FalloutImpact.Resolve(records, shot.ImpactDataSet!.Value, 4);
            Require(impact is { Duration: .25f, Orientation: 2, Model: "meshes/Effects/test-metal.nif" } && impact.Form == Key(7) &&
                FalloutImpact.Resolve(records, Key(6), 0) is null, "Impact material selected the wrong source record or replaced a null slot.");
            Reject(() => FalloutImpact.Resolve(records, Key(6), 1));
            Reject(() => FalloutImpact.Resolve(records, Key(8), 0));
            Require(shot.Projectile.Form == Key(4) && shot.Projectile.Range == 2400 && weapon.AttackGroup == "attackright" && weapon.AttackMultiplier == 1.25f &&
                weapon.ShellModel == "meshes/Projectiles/test-case.nif",
                "Ammo projectile override or attack declaration was lost.");
            var inventory = new FalloutPlayerInventory();
            inventory.Add(records, Key(1), 1, 1, true); inventory.Add(records, Key(2), 9, 1, true); inventory.Equip(records, Key(1));
            var damage = new FalloutWeaponDamageResolver(records, inventory,
                value => value == 41 ? 40 : throw new InvalidDataException("Weapon requested the wrong source skill."), () => []);
            Require(MathF.Abs(damage.Resolve(shot).Amount - 17.6f) < .00001f && damage.Resolve(shot).LimbMultiplier == 1.5f,
                "Source weapon damage, skill, settings or limb multiplier changed.");
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
            Reject(() => worn.Resolve(shot));
            var handling = new FalloutWeaponHandling(inventory);
            Require(!handling.ConsumeShot(weapon, shot, records), "Empty magazine consumed ammunition.");
            handling.CompleteReload(weapon);
            Require(handling.ConsumeShot(weapon, shot, records) && handling.Loaded(Key(1)) == 3 && inventory.Item(Key(2))!.Count == 7 && inventory.Item(Key(5))!.Count == 2,
                "Shot did not conserve source AmmoUse and recovered casings.");
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
            Reject(() => (shot with { Projectile = shot.Projectile with { Flags = 0 } }).RequireHitscan());
            Reject(() => (shot with { Projectiles = 8 }).RequireHitscan());
            Reject(() => (shot with { AmmoEffects = [Key(5)] }).RequireHitscan());
            Console.WriteLine("OPENNV_WEAPON_FIRING_CONTRACT_PASS ammoUse=true recovery=true coldRandom=true emptyHolsteredUnequipped=true sourceOverride=true unsupported=true");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
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
}
