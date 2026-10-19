using OpenNV.Runtime.Campaigns.Classic;
using OpenNV.Runtime.Content;

internal static class ClassicFo1StartingSkillsProbe
{
    internal static void Run(ClassicIntExpressionContext context)
    {
        var profile = new ClassicCharacterProfile("Skill test", 23, false, [5, 5, 5, 5, 5, 5, 10],
            [0, 9, 17], [], new int[18]);
        var skills = ClassicFo1StartingSkills.Resolve(profile, 721, ClassicSkillDifficulty.Normal);
        if (skills[0] != 60 || skills[3] != 70 || skills[9] != 45 || skills[12] != 35 || skills[17] != 30)
            throw new InvalidOperationException("FO1 starting skills used the wrong base, tag or stat rule.");
        var odd = profile with { Special = [5, 6, 5, 4, 5, 5, 10] };
        if (ClassicFo1StartingSkills.Resolve(odd, 721, ClassicSkillDifficulty.Normal)[7] != 20)
            throw new InvalidOperationException("FO1 two-stat skill did not truncate its average.");
        var bonuses = new int[18]; bonuses[0] = 11; bonuses[13] = 11;
        skills = ClassicFo1StartingSkills.Resolve(profile with { SkillBonuses = bonuses }, 721, ClassicSkillDifficulty.Normal);
        if (skills[0] != 82 || skills[13] != 36) throw new InvalidOperationException("FO1 tag did not double source skill bonuses.");
        bonuses[0] = 1000;
        if (ClassicFo1StartingSkills.Resolve(profile with { SkillBonuses = bonuses }, 721, ClassicSkillDifficulty.Normal)[0] != 200)
            throw new InvalidOperationException("FO1 skill cap differs from its source contract.");
        var traits = profile with { Traits = [14, 10] };
        skills = ClassicFo1StartingSkills.Resolve(traits, 721, ClassicSkillDifficulty.Normal);
        if (skills[0] != 60 || skills[6] != 60 || skills[14] != 60)
            throw new InvalidOperationException("FO1 Skilled/Good Natured adjustments differ.");
        var easy = ClassicFo1StartingSkills.Resolve(profile, 721, ClassicSkillDifficulty.Easy);
        var hard = ClassicFo1StartingSkills.Resolve(profile, 721, ClassicSkillDifficulty.Hard);
        if (easy[0] != 60 || hard[0] != 60 || easy[6] != 55 || hard[6] != 25)
            throw new InvalidOperationException("FO1 difficulty changed a combat skill or used the wrong noncombat adjustment.");
        var night = profile with { Traits = [13, 15] };
        foreach (var (hour, expected) in new[] { (0, 7), (559, 7), (600, 5), (1759, 5), (1800, 7), (2359, 7) })
        {
            var special = ClassicFo1StartingSkills.Special(night, hour);
            if (special[1] != expected || special[4] != expected || special[0] != 6)
                throw new InvalidOperationException("Night Person clock boundary or stat scope changed.");
        }
        var capped = night with { Special = [5, 10, 5, 4, 10, 5, 1] };
        if (ClassicFo1StartingSkills.Special(capped, 721)[1] != 10)
            throw new InvalidOperationException("SPECIAL was clamped before applying all trait contributions.");

        var world = ClassicIntWorldObjectState.Empty with
        {
            Objects = new Dictionary<int, ClassicIntWorldObject>
            { [99] = new(99, 0x01000000, 200, 0, true), [98] = new(98, 4, 200, 0, true) },
        };
        var state = new ClassicIntProcedureState(new Dictionary<int, int>(), new Dictionary<int, int>(),
            new Dictionary<int, int>(), new Dictionary<int, int>(), new Dictionary<int, int>(), [], null);
        foreach (var (handle, skill, expected, failure) in new (int, int, int, string?)[]
        {
            (0, 9, 0, null), (98, 9, 0, null), (99, 9, 45, null), (99, 18, -5, null),
            (99, -1, -5, null), (99, 8, 0, "missing-effective-skill-owner"), (97, 9, 0, "missing-world-object"),
        })
        {
            ClassicIntInstruction[] code = [new(0, 0xc001, handle), new(6, 0xc001, skill), new(12, 0x80aa, null), new(14, 0x801c, null)];
            var procedure = new ClassicIntProcedure("skill", 0, null, code);
            var program = new ClassicIntProgram("skill-query", [procedure],
                new Dictionary<string, ClassicIntProcedure> { [procedure.Name] = procedure },
                code.ToDictionary(row => row.Offset), new Dictionary<int, string>(), new Dictionary<int, string>());
            try
            {
                var result = ClassicIntProcedureVm.Execute(program, procedure.Name, state,
                    context with { Skills = new Dictionary<(int, int), int> { [(99, 9)] = 45 } }, world, null, 4);
                if (failure is not null || !result.State.ValueStack.SequenceEqual([expected]))
                    throw new Exception("Source skill query returned an unowned or incorrect value.");
            }
            catch (InvalidOperationException error) when (failure is not null && error.Message.Contains(failure, StringComparison.Ordinal)) { }
        }
        Console.WriteLine("OPENNV_FO1_STARTING_SKILL_PASS sourceRules=true tagBonus=true traits=true difficulty=true cap=true nightClock=true queryOwner=true gameplay=false");
    }
}
