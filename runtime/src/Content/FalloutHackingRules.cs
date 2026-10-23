using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed class FalloutHackingWord
{
    private readonly byte[] _bytes;
    internal ReadOnlySpan<byte> Bytes => _bytes;
    internal int Length => _bytes.Length;

    internal FalloutHackingWord(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 1 or > 31 || bytes.Contains((byte)0) ||
            bytes.Contains((byte)'\r') || bytes.Contains((byte)'\n'))
            throw new NotSupportedException("Hacking dictionary row requires a nonempty, bounded byte word.");
        _bytes = bytes.ToArray();
    }
}

internal sealed class FalloutHackingDictionary
{
    private readonly FalloutHackingWord[] _words;
    internal string SourceHash { get; }
    internal IReadOnlyList<FalloutHackingWord> Words { get; }

    private FalloutHackingDictionary(byte[] source, FalloutHackingWord[] words)
    {
        SourceHash = Convert.ToHexString(SHA256.HashData(source));
        _words = words;
        Words = Array.AsReadOnly(_words);
    }

    // The source file reader consumes a space delimiter and retains at most
    // 31 bytes per buffer. It does not split arbitrary whitespace or fold case.
    internal static FalloutHackingDictionary ReadSpaceDelimited(ReadOnlySpan<byte> source)
    {
        if (source.StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) || source.Contains((byte)0))
            throw new NotSupportedException("Hacking dictionary encoding is not the admitted byte-token layout.");
        var words = new List<FalloutHackingWord>();
        var position = 0;
        while (position < source.Length)
        {
            var start = position;
            while (position < source.Length && position - start < 31 && source[position] != (byte)' ')
                ++position;
            var row = source[start..position];
            if (position < source.Length && source[position] == (byte)' ' && position - start < 31)
                ++position;
            if (!row.IsEmpty) words.Add(new(row));
        }
        if (words.Count == 0) throw new InvalidDataException("Owned hacking dictionary has no words.");
        return new(source.ToArray(), words.ToArray());
    }
}

internal sealed record FalloutHackingRequest(uint TerminalFormId, uint EffectiveDifficulty,
    float CurrentScience, float MinimumScience, uint MaximumWords, uint MinimumWords)
{
    internal static FalloutHackingRequest Read(FalloutPluginStack records, FalloutFormKey terminal,
        uint effectiveDifficulty, float currentScience)
    {
        if (records.GetEffective(terminal).Signature != "TERM" || effectiveDifficulty > 4)
            throw new NotSupportedException("Hacking requires a hackable TERM and qualified effective difficulty.");
        string[] names = ["VeryEasy", "Easy", "Average", "Hard", "VeryHard"];
        var minimum = (float)records.IniSettings.Get($"fHackingMinSkill{names[effectiveDifficulty]}:Hacking");
        return new(records.RuntimeFormId(terminal), effectiveDifficulty, currentScience, minimum,
            FalloutGameSettingIntegers.Read(records, "iHackingMaxWords"),
            FalloutGameSettingIntegers.Read(records, "iHackingMinWords"));
    }

    internal int WordLength
    {
        get
        {
            if (EffectiveDifficulty > 4)
                throw new NotSupportedException("Key-only terminal difficulty cannot generate a hacking puzzle.");
            return (int)Math.Min(12u, 4u + 2u * EffectiveDifficulty + (TerminalFormId & 1));
        }
    }

    // The caller already performed ordinary access admission. This perk changes
    // generation Science; it is not permission to bypass the admission owner.
    internal int RequestedWords(Func<float, float> applyScienceBonus)
    {
        ArgumentNullException.ThrowIfNull(applyScienceBonus);
        _ = WordLength;
        if (!float.IsFinite(CurrentScience) || !float.IsFinite(MinimumScience))
            throw new InvalidDataException("Hacking Science inputs are not finite.");
        var science = applyScienceBonus(CurrentScience);
        if (!float.IsFinite(science)) throw new InvalidDataException("Hacking Science bonus is not finite.");
        var maximum = Math.Min(20u, MaximumWords);
        var minimum = Math.Min(maximum, MinimumWords);
        var threshold = MathF.Min(100, MathF.Max(0, MinimumScience));
        var excess = MathF.Max(0, science - threshold);
        var denominator = (float)(100d - threshold);
        var factor = denominator == 0 ? .5f : (float)(1d - (double)excess / denominator);
        var span = (float)((double)(maximum - minimum) * factor);
        if (!float.IsFinite(span) || (double)span < int.MinValue || (double)span > int.MaxValue)
            throw new NotSupportedException("Hacking count conversion exceeds its qualified finite integer extent.");
        var truncated = (int)MathF.Truncate(span);
        var rounded = (long)truncated + ((double)span - truncated >= .5d ? 1 : 0);
        if (rounded is < int.MinValue or > int.MaxValue)
            throw new NotSupportedException("Hacking count rounding exceeds its qualified integer extent.");
        var count = Math.Min(20u, unchecked(minimum + (uint)(int)rounded));
        if (count == 0) throw new NotSupportedException("Zero-word hacking generation has no qualified continuation.");
        return (int)count;
    }
}

internal static class FalloutHackingRules
{
    internal static int Likeness(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        var matches = 0;
        for (var index = 0; index < Math.Min(left.Length, right.Length); ++index)
            if (left[index] == right[index]) ++matches;
        return matches;
    }

    internal static uint Draw(Func<uint, uint> random, uint bound)
    {
        if (bound == 0) throw new InvalidDataException("Hacking requested an empty random range.");
        var result = random(bound);
        if (result >= bound) throw new InvalidDataException("Authoritative hacking RNG returned an out-of-range draw.");
        return result;
    }

    // The source comparator deliberately has no transitive ordering. Its exact
    // gap/insertion walk owns draw order; library sorting/shuffling is different.
    internal static void Order(List<FalloutHackingWord> words, Func<uint, uint> random)
    {
        var gap = 1;
        while (gap <= (words.Count - 1) / 9) gap = checked(gap * 3 + 1);
        for (; gap > 0; gap /= 3)
        {
            for (var index = gap; index < words.Count; ++index)
            {
                var value = words[index];
                var destination = index;
                while (destination >= gap && Draw(random, 2) == 0)
                {
                    words[destination] = words[destination - gap];
                    destination -= gap;
                }
                words[destination] = value;
            }
        }
    }
}
