using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// Shared manual/Force session. The source-owned geometry and clock drive
// gameplay; a minimal first-party menu adapts input and exposes visual gaps.
internal sealed partial class FalloutLockpickSession
{
    internal int Chance
    {
        get
        {
            var value = _chance();
            return value is >= 0 and <= 100 ? value : throw new InvalidDataException("Force chance is outside its unit percentage.");
        }
    }
    internal bool Finished { get; private set; }
    internal bool Turning { get; private set; }
    internal float CylinderAngle { get; private set; }
    internal float LayoutUnit { get; }
    private uint? _stamp;
    private readonly Func<uint, uint> _random;
    private readonly Func<int> _chance;
    private readonly Action _success, _failure;

    internal FalloutLockpickSession(int chance, Func<uint, uint> random, Action success, Action failure)
        : this(() => chance, random, success, failure) { }

    internal FalloutLockpickSession(Func<int> chance, Func<uint, uint> random, Action success, Action failure)
    {
        _chance = chance; _ = Chance;
        _random = random; _success = success; _failure = failure;
        // The real menu initializes its layout before a Force draw, even
        // though this first presentation does not display that manual layout.
        LayoutUnit = (float)(random(uint.MaxValue) * (1.0 / 4294967296.0));
    }

    internal bool Force()
    {
        if (Finished || Turning) throw new InvalidOperationException("This force attempt has retired or is turning.");
        Finished = true; // Including uncertain RNG/failure callback: never retry.
        var chance = Chance; // The source re-queries current skill for Force.
        var draw = _random(100);
        if (draw >= 100) throw new InvalidDataException("The shared RNG returned an invalid bounded draw.");
        var success = draw < chance;
        if (success) { Finished = false; Turning = true; }
        else _failure();
        return success;
    }

    internal void Advance(uint milliseconds)
    {
        if (!Turning || Finished) return;
        var elapsed = _stamp is { } previous ? unchecked(milliseconds - previous) : 0;
        _stamp = milliseconds;
        if (elapsed > int.MaxValue) throw new InvalidDataException("The lockpick clock regressed outside admitted elapsed storage.");
        CylinderAngle = MathF.Min(CylinderAngle + (float)(elapsed * 90.0 / 1000.0), 90);
        if (CylinderAngle < 90) return;
        Finished = true; Turning = false;
        _success();
    }

    internal void RebaseClock() => _stamp = null;

    internal void Cancel() { Finished = true; Turning = false; }

    internal static int Skill(float current)
    {
        if (!float.IsFinite(current)) throw new InvalidDataException("Current Lockpick skill is not finite.");
        return (int)MathF.Floor(Math.Clamp(current, 0, 100));
    }

    internal static int RequirePlainPins(FalloutPluginStack records, FalloutPlayerInventory inventory, FalloutFormKey pin)
    {
        var source = records.GetEffective(pin);
        if (source.Signature != "MISC") throw new InvalidDataException("The source lockpick item is not MISC.");
        // Quest-modified depletion has not been qualified. This is a temporary
        // admission boundary, not a rule that retail never consumes quest pins.
        if (records.QuestObjects.IsQuestObject(pin))
            throw new NotSupportedException("Quest-marked pin removal is outside the current qualified picking scope.");
        if (inventory.Item(pin) is not { Count: > 0, UnequipLocked: false } item || item.Variants?.Any(value =>
                value.Condition is not null || value.Owner is not null || value.Global is not null || value.FactionRank is not null) == true)
            throw new NotSupportedException("Initial picking requires a known plain bobby-pin stack; zero/extended stacks remain unadmitted.");
        if (source.ReadSubrecords().Any(field => field.Signature == "SCRI"))
            throw new NotSupportedException("Scripted pin removal needs its item-event owner.");
        return item.Count;
    }

    internal static (int Class, int Chance, int Reward) Admission(FalloutPluginStack records, int level, float current)
    {
        string[] names = ["VeryEasy", "Easy", "Average", "Hard", "VeryHard"];
        var skill = Skill(current);
        var difficulty = 0;
        while (difficulty < names.Length && level > unchecked((int)FalloutGameSettingIntegers.Read(records, "iLockLevelMax" + names[difficulty])))
            ++difficulty;
        if (difficulty == names.Length) throw new NotSupportedException("Key-only locks cannot be picked.");
        // The source uses a startup cache, whose override refresh is not joined.
        // Admit the unchanged source thresholds only; do not assume live refresh.
        int[] cache = [0, 25, 50, 75, 100];
        for (var index = 0; index < names.Length; ++index)
            if (unchecked((int)FalloutGameSettingIntegers.Read(records, "iLockLevelMax" + names[index])) != cache[index])
                throw new NotSupportedException("Modified picking thresholds need the source cache lifetime owner.");
        if (skill < cache[difficulty]) throw new InvalidOperationException("Current Lockpick skill is below this lock's threshold.");
        var chance = ForceChance(records, difficulty, current);
        var result = -1; var fallback = 0;
        foreach (var name in names)
        {
            if (result >= 0) break;
            var threshold = unchecked((int)FalloutGameSettingIntegers.Read(records, "iXPLevelPickLock" + name));
            var reward = unchecked((int)FalloutGameSettingIntegers.Read(records, "iXPRewardPickLock" + name));
            if (difficulty <= threshold) result = reward; else fallback = reward;
        }
        return (difficulty, chance, result >= 0 ? result : fallback);
    }

    internal static int ForceChance(FalloutPluginStack records, int difficulty, float current)
    {
        int[] cache = [0, 25, 50, 75, 100];
        if (difficulty is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(difficulty));
        var basis = FalloutGameSettingFloats.Read(records, "fLockSkillBase");
        if (!float.IsFinite(basis) || basis < int.MinValue || (double)basis > int.MaxValue)
            throw new InvalidDataException("Force-lock basis is outside finite signed integer storage.");
        return (int)Math.Clamp((long)Skill(current) - cache[difficulty] + (int)basis, 0, 100);
    }
}
