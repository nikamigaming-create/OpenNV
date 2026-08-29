namespace OpenNV.Runtime.Campaigns.Fallout1;

internal static class Fo1CharacterProfileNumericContracts
{
    // Immutable format, source-art, geometry, and acceptance contracts.
    // Runtime-tunable Fallout 1 behavior remains in the versioned runtime recipe.
    internal const int SourcePresentationInt10 = 10;
    internal const int SourcePresentationInt11 = 11;
    internal const int SourcePresentationInt15 = 15;
    internal const int SourcePresentationInt16 = 16;
    internal const int SourcePresentationInt20 = 20;
    internal const int SourcePresentationInt25 = 25;
    internal const int SourcePresentationInt30 = 30;
    internal const int SourcePresentationInt35 = 35;
    internal const int SourcePresentationInt40 = 40;
    internal const int SourcePresentationInt5 = 5;
    internal const int SourcePresentationInt55 = 55;
    internal const int SourcePresentationInt6 = 6;
    internal const int SourcePresentationInt7 = 7;
}

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
    internal Fo1CharacterAppearance? Appearance { get; init; }

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
    internal int HitPoints => Fo1CharacterProfileNumericContracts.SourcePresentationInt15 + 2 * EffectiveEndurance + EffectiveStrength;
    internal int ArmorClass => HasTrait("Kamikaze") ? 0 : EffectiveAgility;
    internal int ActionPoints => Math.Max(1, Fo1CharacterProfileNumericContracts.SourcePresentationInt5 + EffectiveAgility / 2 - (HasTrait("Bruiser") ? 2 : 0));
    internal int Sequence => 2 * EffectivePerception + (HasTrait("Kamikaze") ? Fo1CharacterProfileNumericContracts.SourcePresentationInt5 : 0);
    internal int CarryWeight => HasTrait("Small Frame")
        ? Fo1CharacterProfileNumericContracts.SourcePresentationInt15 * EffectiveStrength
        : Fo1CharacterProfileNumericContracts.SourcePresentationInt25 + Fo1CharacterProfileNumericContracts.SourcePresentationInt25 * EffectiveStrength;
    internal int MeleeDamage => Math.Max(1, EffectiveStrength - Fo1CharacterProfileNumericContracts.SourcePresentationInt5) + (HasTrait("Heavy Handed") ? 4 : 0);
    internal int PoisonResistance => HasTrait("Fast Metabolism") ? 0 : Fo1CharacterProfileNumericContracts.SourcePresentationInt5 * EffectiveEndurance;
    internal int RadiationResistance => HasTrait("Fast Metabolism") ? 0 : 2 * EffectiveEndurance;
    internal int HealingRate => Math.Max(1, EffectiveEndurance / 3) + (HasTrait("Fast Metabolism") ? 2 : 0);
    internal int CriticalChance => EffectiveLuck + (HasTrait("Finesse") ? Fo1CharacterProfileNumericContracts.SourcePresentationInt10 : 0);
    internal int WeaponActionPointAdjustment => HasTrait("Fast Shot") ? -1 : 0;

    internal static Fo1CharacterProfile Demo() => new(
        "NIKAMI",
        Fo1CharacterProfileNumericContracts.SourcePresentationInt25,
        "Male",
        Strength: Fo1CharacterProfileNumericContracts.SourcePresentationInt6,
        Perception: Fo1CharacterProfileNumericContracts.SourcePresentationInt7,
        Endurance: Fo1CharacterProfileNumericContracts.SourcePresentationInt5,
        Charisma: Fo1CharacterProfileNumericContracts.SourcePresentationInt5,
        Intelligence: Fo1CharacterProfileNumericContracts.SourcePresentationInt5,
        Agility: Fo1CharacterProfileNumericContracts.SourcePresentationInt7,
        Luck: Fo1CharacterProfileNumericContracts.SourcePresentationInt5,
        ["Small Guns", "First Aid", "Speech"],
        ["Fast Shot", "Bloody Mess"]);

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > Fo1CharacterProfileNumericContracts.SourcePresentationInt11)
            throw new InvalidOperationException("Fallout character name must contain 1-11 visible characters.");
        if (Age is < Fo1CharacterProfileNumericContracts.SourcePresentationInt16 or > Fo1CharacterProfileNumericContracts.SourcePresentationInt35)
            throw new InvalidOperationException("Fallout character age must be between 16 and 35.");
        if (Sex is not ("Male" or "Female"))
            throw new InvalidOperationException("Fallout character sex must be Male or Female.");
        foreach (var value in AllocatedSpecial())
            if (value is < 1 or > Fo1CharacterProfileNumericContracts.SourcePresentationInt10)
                throw new InvalidOperationException("Fallout SPECIAL values must be between 1 and 10.");
        if (AllocatedSpecialTotal != Fo1CharacterProfileNumericContracts.SourcePresentationInt40)
            throw new InvalidOperationException(
                $"Fallout character creation must spend exactly five SPECIAL points; total is {AllocatedSpecialTotal}.");
        if (TaggedSkills.Count != 3 || TaggedSkills.Distinct(StringComparer.Ordinal).Count() != 3 ||
            TaggedSkills.Any(skill => !SkillNames.Contains(skill, StringComparer.Ordinal)))
            throw new InvalidOperationException("Fallout character creation requires exactly three distinct tag skills.");
        if (Traits.Count > 2 || Traits.Distinct(StringComparer.Ordinal).Count() != Traits.Count ||
            Traits.Any(trait => !TraitNames.Contains(trait, StringComparer.Ordinal)))
            throw new InvalidOperationException("Fallout character creation allows no more than two distinct traits.");
        Appearance?.Validate(Sex);
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
        var combatPenalty = HasTrait("Good Natured") ? Fo1CharacterProfileNumericContracts.SourcePresentationInt10 : 0;
        var giftedPenalty = HasTrait("Gifted") ? Fo1CharacterProfileNumericContracts.SourcePresentationInt10 : 0;
        var values = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Small Guns"] = Fo1CharacterProfileNumericContracts.SourcePresentationInt35 + agility - combatPenalty,
            ["Big Guns"] = Fo1CharacterProfileNumericContracts.SourcePresentationInt10 + agility - combatPenalty,
            ["Energy Weapons"] = Fo1CharacterProfileNumericContracts.SourcePresentationInt10 + agility - combatPenalty,
            ["Unarmed"] = Fo1CharacterProfileNumericContracts.SourcePresentationInt40 + (agility + strength) / 2 - combatPenalty,
            ["Melee Weapons"] = Fo1CharacterProfileNumericContracts.SourcePresentationInt55 + (agility + strength) / 2 - combatPenalty,
            ["Throwing"] = Fo1CharacterProfileNumericContracts.SourcePresentationInt40 + agility,
            ["First Aid"] = Fo1CharacterProfileNumericContracts.SourcePresentationInt30 + (perception + intelligence) / 2 + (HasTrait("Good Natured") ? Fo1CharacterProfileNumericContracts.SourcePresentationInt20 : 0),
            ["Doctor"] = Fo1CharacterProfileNumericContracts.SourcePresentationInt15 + (perception + intelligence) / 2 + (HasTrait("Good Natured") ? Fo1CharacterProfileNumericContracts.SourcePresentationInt20 : 0),
            ["Sneak"] = Fo1CharacterProfileNumericContracts.SourcePresentationInt25 + agility,
            ["Lockpick"] = Fo1CharacterProfileNumericContracts.SourcePresentationInt20 + (perception + agility) / 2,
            ["Steal"] = Fo1CharacterProfileNumericContracts.SourcePresentationInt20 + agility,
            ["Traps"] = Fo1CharacterProfileNumericContracts.SourcePresentationInt20 + (perception + agility) / 2,
            ["Science"] = Fo1CharacterProfileNumericContracts.SourcePresentationInt25 + 2 * intelligence,
            ["Repair"] = Fo1CharacterProfileNumericContracts.SourcePresentationInt20 + intelligence,
            ["Speech"] = Fo1CharacterProfileNumericContracts.SourcePresentationInt25 + 2 * charisma + (HasTrait("Good Natured") ? Fo1CharacterProfileNumericContracts.SourcePresentationInt20 : 0),
            ["Barter"] = Fo1CharacterProfileNumericContracts.SourcePresentationInt20 + 2 * charisma + (HasTrait("Good Natured") ? Fo1CharacterProfileNumericContracts.SourcePresentationInt20 : 0),
            ["Gambling"] = Fo1CharacterProfileNumericContracts.SourcePresentationInt20 + 3 * luck,
            ["Outdoorsman"] = Fo1CharacterProfileNumericContracts.SourcePresentationInt5 + (intelligence + endurance) / 2,
        };
        foreach (var skill in SkillNames)
        {
            values[skill] -= giftedPenalty;
            if (TaggedSkills.Contains(skill, StringComparer.Ordinal))
                values[skill] += Fo1CharacterProfileNumericContracts.SourcePresentationInt20;
            values[skill] = Math.Max(0, values[skill]);
        }
        return values;
    }

    internal object Report() => new
    {
        schema = Appearance is null
            ? "opennv-fo1-character/v1"
            : "opennv-fo1-character/v2",
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
        appearance = Appearance?.Report(),
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

    private int Effective(int value) => Math.Clamp(value + (HasTrait("Gifted") ? 1 : 0), 1, Fo1CharacterProfileNumericContracts.SourcePresentationInt10);

    private int[] AllocatedSpecial() =>
        [Strength, Perception, Endurance, Charisma, Intelligence, Agility, Luck];
}
