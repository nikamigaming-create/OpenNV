namespace OpenNV.Runtime;

internal sealed record Fo1CharacterProfile(
    string Name,
    int Age,
    string Sex,
    int Strength,
    int Perception,
    int Endurance,
    int Charisma,
    int Intelligence,
    int Agility,
    int Luck,
    IReadOnlyList<string> TaggedSkills,
    IReadOnlyList<string> Traits)
{
    internal static readonly string[] SkillNames =
    [
        "Small Guns", "Big Guns", "Energy Weapons", "Unarmed", "Melee Weapons", "Throwing",
        "First Aid", "Doctor", "Sneak", "Lockpick", "Steal", "Traps", "Science", "Repair",
        "Speech", "Barter", "Gambling", "Outdoorsman",
    ];

    internal static readonly string[] TraitNames =
    [
        "Fast Metabolism", "Bruiser", "Small Frame", "One Hander", "Finesse", "Kamikaze",
        "Heavy Handed", "Fast Shot", "Bloody Mess", "Jinxed", "Good Natured", "Chem Reliant",
        "Chem Resistant", "Night Person", "Skilled", "Gifted",
    ];

    internal int AllocatedSpecialTotal =>
        Strength + Perception + Endurance + Charisma + Intelligence + Agility + Luck;

    internal int EffectiveStrength => Effective(Strength + (HasTrait("Bruiser") ? 2 : 0));
    internal int EffectivePerception => Effective(Perception);
    internal int EffectiveEndurance => Effective(Endurance);
    internal int EffectiveCharisma => Effective(Charisma);
    internal int EffectiveIntelligence => Effective(Intelligence);
    internal int EffectiveAgility => Effective(Agility + (HasTrait("Small Frame") ? 1 : 0));
    internal int EffectiveLuck => Effective(Luck);
    internal int HitPoints => 15 + 2 * EffectiveEndurance + EffectiveStrength;
    internal int ArmorClass => HasTrait("Kamikaze") ? 0 : EffectiveAgility;
    internal int ActionPoints => Math.Max(1, 5 + EffectiveAgility / 2 - (HasTrait("Bruiser") ? 2 : 0));
    internal int Sequence => 2 * EffectivePerception + (HasTrait("Kamikaze") ? 5 : 0);
    internal int CarryWeight => HasTrait("Small Frame")
        ? 15 * EffectiveStrength
        : 25 + 25 * EffectiveStrength;
    internal int MeleeDamage => Math.Max(1, EffectiveStrength - 5) + (HasTrait("Heavy Handed") ? 4 : 0);
    internal int PoisonResistance => HasTrait("Fast Metabolism") ? 0 : 5 * EffectiveEndurance;
    internal int RadiationResistance => HasTrait("Fast Metabolism") ? 0 : 2 * EffectiveEndurance;
    internal int HealingRate => Math.Max(1, EffectiveEndurance / 3) + (HasTrait("Fast Metabolism") ? 2 : 0);
    internal int CriticalChance => EffectiveLuck + (HasTrait("Finesse") ? 10 : 0);
    internal int WeaponActionPointAdjustment => HasTrait("Fast Shot") ? -1 : 0;

    internal static Fo1CharacterProfile Demo() => new(
        "NIKAMI",
        25,
        "Male",
        Strength: 6,
        Perception: 7,
        Endurance: 5,
        Charisma: 5,
        Intelligence: 5,
        Agility: 7,
        Luck: 5,
        ["Small Guns", "First Aid", "Speech"],
        ["Fast Shot", "Bloody Mess"]);

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 11)
            throw new InvalidOperationException("Fallout character name must contain 1-11 visible characters.");
        if (Age is < 16 or > 35)
            throw new InvalidOperationException("Fallout character age must be between 16 and 35.");
        if (Sex is not ("Male" or "Female"))
            throw new InvalidOperationException("Fallout character sex must be Male or Female.");
        foreach (var value in AllocatedSpecial())
            if (value is < 1 or > 10)
                throw new InvalidOperationException("Fallout SPECIAL values must be between 1 and 10.");
        if (AllocatedSpecialTotal != 40)
            throw new InvalidOperationException(
                $"Fallout character creation must spend exactly five SPECIAL points; total is {AllocatedSpecialTotal}.");
        if (TaggedSkills.Count != 3 || TaggedSkills.Distinct(StringComparer.Ordinal).Count() != 3 ||
            TaggedSkills.Any(skill => !SkillNames.Contains(skill, StringComparer.Ordinal)))
            throw new InvalidOperationException("Fallout character creation requires exactly three distinct tag skills.");
        if (Traits.Count > 2 || Traits.Distinct(StringComparer.Ordinal).Count() != Traits.Count ||
            Traits.Any(trait => !TraitNames.Contains(trait, StringComparer.Ordinal)))
            throw new InvalidOperationException("Fallout character creation allows no more than two distinct traits.");
    }

    internal IReadOnlyDictionary<string, int> Skills()
    {
        var strength = EffectiveStrength;
        var perception = EffectivePerception;
        var endurance = EffectiveEndurance;
        var charisma = EffectiveCharisma;
        var intelligence = EffectiveIntelligence;
        var agility = EffectiveAgility;
        var luck = EffectiveLuck;
        var combatPenalty = HasTrait("Good Natured") ? 10 : 0;
        var giftedPenalty = HasTrait("Gifted") ? 10 : 0;
        var values = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Small Guns"] = 35 + agility - combatPenalty,
            ["Big Guns"] = 10 + agility - combatPenalty,
            ["Energy Weapons"] = 10 + agility - combatPenalty,
            ["Unarmed"] = 40 + (agility + strength) / 2 - combatPenalty,
            ["Melee Weapons"] = 55 + (agility + strength) / 2 - combatPenalty,
            ["Throwing"] = 40 + agility,
            ["First Aid"] = 30 + (perception + intelligence) / 2 + (HasTrait("Good Natured") ? 20 : 0),
            ["Doctor"] = 15 + (perception + intelligence) / 2 + (HasTrait("Good Natured") ? 20 : 0),
            ["Sneak"] = 25 + agility,
            ["Lockpick"] = 20 + (perception + agility) / 2,
            ["Steal"] = 20 + agility,
            ["Traps"] = 20 + (perception + agility) / 2,
            ["Science"] = 25 + 2 * intelligence,
            ["Repair"] = 20 + intelligence,
            ["Speech"] = 25 + 2 * charisma + (HasTrait("Good Natured") ? 20 : 0),
            ["Barter"] = 20 + 2 * charisma + (HasTrait("Good Natured") ? 20 : 0),
            ["Gambling"] = 20 + 3 * luck,
            ["Outdoorsman"] = 5 + (intelligence + endurance) / 2,
        };
        foreach (var skill in SkillNames)
        {
            values[skill] -= giftedPenalty;
            if (TaggedSkills.Contains(skill, StringComparer.Ordinal))
                values[skill] += 20;
            values[skill] = Math.Max(0, values[skill]);
        }
        return values;
    }

    internal object Report() => new
    {
        schema = "opennv-fo1-character/v1",
        name = Name,
        age = Age,
        sex = Sex,
        allocatedSpecial = new
        {
            strength = Strength,
            perception = Perception,
            endurance = Endurance,
            charisma = Charisma,
            intelligence = Intelligence,
            agility = Agility,
            luck = Luck,
            total = AllocatedSpecialTotal,
        },
        effectiveSpecial = new
        {
            strength = EffectiveStrength,
            perception = EffectivePerception,
            endurance = EffectiveEndurance,
            charisma = EffectiveCharisma,
            intelligence = EffectiveIntelligence,
            agility = EffectiveAgility,
            luck = EffectiveLuck,
        },
        taggedSkills = TaggedSkills,
        traits = Traits,
        derived = new
        {
            hitPoints = HitPoints,
            armorClass = ArmorClass,
            actionPoints = ActionPoints,
            sequence = Sequence,
            carryWeight = CarryWeight,
            meleeDamage = MeleeDamage,
            poisonResistance = PoisonResistance,
            radiationResistance = RadiationResistance,
            healingRate = HealingRate,
            criticalChance = CriticalChance,
            weaponActionPointAdjustment = WeaponActionPointAdjustment,
        },
        skills = Skills(),
    };

    private bool HasTrait(string name) => Traits.Contains(name, StringComparer.Ordinal);

    private int Effective(int value) => Math.Clamp(value + (HasTrait("Gifted") ? 1 : 0), 1, 10);

    private int[] AllocatedSpecial() =>
        [Strength, Perception, Endurance, Charisma, Intelligence, Agility, Luck];
}
