using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutWeaponPresentation(FalloutFormKey Form, FalloutNpcAppearancePart Model,
    string AnimationGroup, float AnimationMultiplier, byte Grip)
{
    internal byte ClipSize { get; init; }
    internal byte AmmoUse { get; init; }
    internal byte ReloadAnimation { get; init; }
    internal byte AttackAnimation { get; init; }
    internal float AttackMultiplier { get; init; } = 1;
    internal bool Automatic { get; init; }
    internal string? ShellModel { get; init; }
    internal string AttackGroup => AttackAnimation switch
    {
        26 => "attackleft",
        32 => "attackright",
        38 => "attack3",
        44 => "attack4",
        50 => "attack5",
        56 => "attack6",
        62 => "attack7",
        68 => "attack8",
        74 => "attackloop",
        80 => "attackspin",
        86 => "attackspin2",
        102 => "placemine",
        108 => "placemine2",
        114 => "attackthrow",
        120 => "attackthrow2",
        126 => "attackthrow3",
        132 => "attackthrow4",
        138 => "attackthrow5",
        144 => "attack9",
        150 => "attackthrow6",
        156 => "attackthrow7",
        162 => "attackthrow8",
        _ => throw new NotSupportedException($"WEAP attack group {AttackAnimation} has no selection owner.")
    };
    internal IReadOnlyList<FalloutFormKey> Ammunition { get; init; } = [];
    internal IReadOnlyDictionary<string, FalloutFormKey> Sounds { get; init; } = new Dictionary<string, FalloutFormKey>();
    internal string ReloadGroup => ReloadAnimation < 23 ? "reload" + "abcdefghijklmnopqrswxyz"[ReloadAnimation] :
        throw new NotSupportedException($"WEAP reload group {ReloadAnimation} is unbound.");

    internal static FalloutWeaponPresentation Read(FalloutPluginStack records, FalloutFormKey key, bool firstPerson = true)
    {
        var weapon = records.GetEffective(key);
        if (weapon.Signature != "WEAP") throw new InvalidDataException("Equipped weapon is not WEAP.");
        var fields = weapon.ReadSubrecords().ToArray();
        var data = fields.Single(field => field.Signature == "DNAM").Data.Span;
        if (data.Length is not (120 or 124 or 136 or 200 or 204)) throw new NotSupportedException($"WEAP DNAM extent {data.Length} is unbound.");
        var type = BinaryPrimitives.ReadUInt32LittleEndian(data);
        var group = type switch
        {
            0 => "h2h",
            1 => "1hm",
            2 => "2hm",
            3 or 4 => "1hp",
            5 or 7 => "2hr",
            6 => "2ha",
            8 => "2hh",
            9 => "2hl",
            10 or 13 => "1gt",
            11 => "1lm",
            12 => "1md",
            _ => throw new NotSupportedException($"WEAP animation type {type} is unbound.")
        };
        var multiplier = BinaryPrimitives.ReadSingleLittleEndian(data[4..]);
        if (!float.IsFinite(multiplier) || multiplier <= 0) throw new InvalidDataException("WEAP animation multiplier is invalid.");
        var model = fields.SingleOrDefault(field => field.Signature == "WNAM").Data;
        var owner = weapon;
        if (firstPerson && !model.IsEmpty)
        {
            if (model.Length != 4) throw new InvalidDataException("WEAP first-person model FormID has an invalid extent.");
            var id = BinaryPrimitives.ReadUInt32LittleEndian(model.Span);
            if (id != 0)
            {
                owner = records.GetEffective(weapon.Plugin.AdjustFormId(id));
                if (owner.Signature != "STAT") throw new InvalidDataException("Player weapon model must resolve to STAT.");
            }
        }
        var itemData = fields.Single(field => field.Signature == "DATA").Data.Span;
        if (itemData.Length != 15) throw new NotSupportedException($"WEAP DATA extent {itemData.Length} is unbound.");
        FalloutFormKey? Form(string signature)
        {
            var bytes = fields.SingleOrDefault(field => field.Signature == signature).Data;
            if (bytes.IsEmpty) return null;
            if (bytes.Length != 4) throw new InvalidDataException($"WEAP {signature} FormID extent is invalid.");
            var id = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Span);
            return id == 0 ? null : weapon.Plugin.AdjustFormId(id);
        }
        var ammunition = new List<FalloutFormKey>();
        if (Form("NAM0") is { } ammo)
        {
            var source = records.GetEffective(ammo);
            if (source.Signature == "AMMO") ammunition.Add(ammo);
            else if (source.Signature == "FLST")
                foreach (var field in source.ReadSubrecords().Where(field => field.Signature == "LNAM"))
                {
                    if (field.Data.Length != 4) throw new InvalidDataException("Weapon ammunition list has an invalid FormID extent.");
                    var entry = source.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
                    if (records.GetEffective(entry).Signature != "AMMO") throw new NotSupportedException("Weapon ammunition list contains a non-AMMO entry.");
                    ammunition.Add(entry);
                }
            else throw new NotSupportedException("WEAP ammunition is neither AMMO nor FLST.");
        }
        var sounds = new Dictionary<string, FalloutFormKey>();
        foreach (var (name, signature) in new[] { ("equip", "NAM9"), ("unequip", "NAM8"), ("empty", "TNAM"), ("shoot", "XNAM") })
            if (Form(signature) is { } sound)
            {
                if (records.GetEffective(sound).Signature != "SOUN") throw new InvalidDataException("Weapon sound is not SOUN.");
                sounds.Add(name, sound);
            }
        return new(key, FalloutNpcAppearanceResolver.ReadModel(records, owner, "weapon", "MODL", "MODS", "MODD", 0, null), group, multiplier, data[13])
        {
            ClipSize = itemData[14],
            AmmoUse = data[14],
            ReloadAnimation = data[15],
            AttackAnimation = data[41],
            AttackMultiplier = BinaryPrimitives.ReadSingleLittleEndian(data[60..]),
            Automatic = (data[12] & 2) != 0,
            Ammunition = ammunition,
            Sounds = sounds,
            ShellModel = FalloutNpcAppearanceResolver.PathField(weapon, "MOD2", "meshes", false, fields)
        };
    }
}
