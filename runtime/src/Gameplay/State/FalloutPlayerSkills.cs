using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed class FalloutPlayerSkills
{
    internal static string SkillName(string editorId) => editorId switch
    {
        "AVSmallGuns" => "Guns",
        "AVThrowing" => "Survival",
        _ when editorId.StartsWith("AV", StringComparison.Ordinal) => editorId[2..],
        _ => throw new InvalidDataException("Skill has no actor-value identity."),
    };
    private readonly FalloutPluginStack _records;
    private readonly Func<FalloutNativeSpecialState> _special;
    private readonly Func<string, bool> _tagged;
    private readonly Func<IReadOnlyList<FalloutNativeTraitIdentity>> _traits;
    private readonly FalloutGlobalState? _globals;
    private readonly FalloutPlayerInventory _inventory;
    private readonly FalloutAbilityModifiers _abilities;
    private readonly FalloutFormKey _actor;
    private readonly Func<FalloutFormKey> _race;
    private readonly Func<bool> _hardcore;
    private readonly Dictionary<(FalloutFormKey Form, string Field), FalloutFormKey[]> _links = [];
    private readonly Dictionary<string, float> _settings = new(StringComparer.Ordinal);
    private readonly HashSet<int> _evaluating = [];
    private long _weightRevision = -1;
    private bool _weightHardcore;
    private float _carriedWeight;
    private static readonly (int Value, string Name, string Setting, int Attribute)[] Skills =
    [
        (32, "Barter", "Barter", 8), (34, "EnergyWeapons", "EnergyWeapons", 6), (35, "Explosives", "Explosives", 6),
        (36, "Lockpick", "Lockpick", 6), (37, "Medicine", "Medicine", 9), (38, "MeleeWeapons", "MeleeWeapons", 5),
        (39, "Repair", "Repair", 9), (40, "Science", "Science", 9), (41, "Guns", "SmallGuns", 10),
        (42, "Sneak", "Sneak", 10), (43, "Speech", "Speech", 8), (44, "Survival", "Survival", 7), (45, "Unarmed", "Unarmed", 7)
    ];

    internal FalloutPlayerSkills(FalloutPluginStack records, Func<FalloutNativeSpecialState> special, Func<string, bool> tagged,
        Func<IReadOnlyList<FalloutNativeTraitIdentity>> traits, FalloutGlobalState? globals, FalloutPlayerInventory inventory,
        FalloutFormKey actor, Func<FalloutFormKey> race, Func<bool> hardcore)
    {
        _records = records; _special = special; _tagged = tagged; _traits = traits; _globals = globals;
        _inventory = inventory; _abilities = new(records); _actor = actor; _race = race; _hardcore = hardcore;
    }

    internal float Value(string name)
    {
        var attribute = FalloutNativeVigorResolver.AttributeNames.ToList().FindIndex(value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (attribute >= 0) return Value(attribute + 5);
        var skill = Skills.SingleOrDefault(skill => skill.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return skill.Name is not null ? Value(skill.Value) : throw new NotSupportedException($"Player value {name} is unbound.");
    }

    internal float Value(int value)
    {
        if (!_evaluating.Add(value)) throw new NotSupportedException("Actor ability conditions have a recursive value dependency.");
        try
        {
            if (value == 46)
            {
                if (_weightRevision != _inventory.Revision || _weightHardcore != _hardcore())
                {
                    _carriedWeight = _inventory.Items.Sum(item => Weight(item) * item.Count);
                    if (!float.IsFinite(_carriedWeight)) throw new InvalidDataException("Carried weight exceeds finite storage.");
                    _weightRevision = _inventory.Revision; _weightHardcore = _hardcore();
                }
                return _carriedWeight;
            }
            float initial;
            if (value is >= 5 and <= 11) initial = _special().Values[value - 5];
            else
            {
                var skill = Skills.SingleOrDefault(skill => skill.Value == value);
                if (skill.Name is null) throw new NotSupportedException($"Player value {value} is unbound.");
                initial = Setting("fAVDSkill" + skill.Setting + "Base") +
                    MathF.Floor(Setting("fAVDSkillPrimaryBonusMult") * Value(skill.Attribute)) +
                    MathF.Ceiling(Setting("fAVDSkillLuckBonusMult") * Value(11)) + (_tagged(skill.Name) ? Setting("fAVDTagSkillBonus") : 0);
            }
            foreach (var form in ConstantEffects())
                foreach (var effect in _abilities.Spell(form))
                    if (effect.ActorValue == value && FalloutCondition.AllPass(effect.Conditions, Condition)) initial += effect.Amount;
            return Math.Clamp(initial, value is >= 5 and <= 11 ? 1 : 0, value is >= 5 and <= 11 ? 10 : 100);
        }
        finally { _evaluating.Remove(value); }
    }

    internal IReadOnlyList<FalloutPerkEntry> PerkEntries => _traits().SelectMany(trait =>
        _abilities.Perk(_records.RuntimeFormKey(trait.RuntimeFormId)).Entries).ToArray();

    private IEnumerable<FalloutFormKey> ConstantEffects() =>
        Links(_actor, "SPLO").Concat(Links(_race(), "SPLO"))
            .Concat(_traits().SelectMany(trait => _abilities.Perk(_records.RuntimeFormKey(trait.RuntimeFormId)).Spells)).Distinct()
            .Concat(_inventory.Equipped.Select(_records.RuntimeFormKey).Where(form => _records.GetEffective(form).Signature == "ARMO")
                .SelectMany(form => Links(form, "EITM")));

    private FalloutFormKey[] Links(FalloutFormKey form, string name)
    {
        if (_links.TryGetValue((form, name), out var cached)) return cached;
        var record = _records.GetEffective(form);
        if (name == "SPLO" && record.Signature is "NPC_" or "CREA") record = FalloutActorTemplateOwner.Resolve(_records, record, 8);
        var result = record.ReadSubrecords().Where(field => field.Signature == name).Select(field =>
        {
            if (field.Data.Length != 4) throw new InvalidDataException($"Actor effect {form}/{name} extent is invalid.");
            return record.Plugin.AdjustOptionalFormId(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
        }).Where(form => form is not null).Select(form => form!.Value).ToArray();
        _links.Add((form, name), result);
        return result;
    }

    private float Weight(FalloutCampaignItem item)
    {
        if (item.RecordType != "AMMO") return item.Weight ??
            (item.RecordType == "NOTE" ? 0 : throw new NotSupportedException($"Inventory weight is unbound for {item.FormKey}."));
        if (!_hardcore()) return 0;
        var data = _records.GetEffective(item.FormKey).ReadSubrecords().Single(field => field.Signature == "DAT2").Data;
        if (data.Length is not (12 or 20)) throw new NotSupportedException("Ammunition weight layout is unbound.");
        var weight = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(data.Span[8..]);
        return float.IsFinite(weight) && weight >= 0 ? weight : throw new InvalidDataException("Ammunition weight is invalid.");
    }

    private float Condition(FalloutCondition condition) => condition.RunOn != 0
        ? throw new NotSupportedException($"Ability condition run-on {condition.RunOn} is unbound.") : condition.Function switch
        {
            74 => (_globals ?? throw new InvalidOperationException("Ability has no global state owner.")).Get(condition.FormArgument1),
            14 => Value(checked((int)condition.Argument1)),
            _ => throw new NotSupportedException($"Ability condition {condition.Owner.FormKey}/{condition.Function} is unbound.")
        };
    private float Setting(string name)
    {
        if (!_settings.TryGetValue(name, out var value)) _settings.Add(name, value = FalloutGameSettingFloats.Read(_records, name));
        return value;
    }
}
