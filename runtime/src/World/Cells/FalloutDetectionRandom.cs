namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutDetectionRandomSnapshot(string Schema, uint[] Words, int Index, long Draws)
{
    internal FalloutDetectionRandomSnapshot Copy() => this with { Words = Words.ToArray() };
}

// The source cadence's raw unsigned generator uses a 624-word twist and a
// multiplicative 69069 seed expansion. Its unsigned extraction is untempered;
// applying conventional MT19937 tempering would produce another stream.
// Initialization requires an explicit fresh-run seed. Restore never seeds or
// draws. The eventual shared simulation owner must also own stream sharing and
// call order; constructing this component does not establish that lifecycle.
internal sealed class FalloutDetectionRandom
{
    private const string Schema = "opennv-detection-random/v1";
    private const int WordCount = 624;
    private const int Offset = 397;
    private const uint Matrix = 0x9908b0df;
    private uint[] _words = new uint[WordCount];
    private int _index = WordCount;
    private long _draws;

    internal FalloutDetectionRandom(uint freshRunSeed)
    {
        _words[0] = freshRunSeed;
        for (var index = 1; index < _words.Length; index++)
            _words[index] = unchecked(_words[index - 1] * 69069U);
    }

    internal uint NextUInt32()
    {
        var draws = checked(_draws + 1);
        if (_index == WordCount) Twist();
        var result = _words[_index++];
        _draws = draws;
        return result;
    }

    internal float NextFloat(float minimum, float maximum)
    {
        if (!float.IsFinite(minimum) || !float.IsFinite(maximum))
            throw new InvalidDataException("Detection cadence range is non-finite.");
        var word = NextUInt32();
        // Signedness matters: every raw bit pattern is a nonnegative uint.
        return (float)(word * ((double)maximum - minimum) / 4294967296d + minimum);
    }

    internal FalloutDetectionRandomSnapshot Capture() => new(Schema, _words.ToArray(), _index, _draws);

    internal void Restore(FalloutDetectionRandomSnapshot saved)
    {
        if (saved.Schema != Schema || saved.Words is not { Length: WordCount } ||
            saved.Index is < 0 or > WordCount || saved.Draws < 0 ||
            saved.Draws == 0 && saved.Index != WordCount ||
            saved.Draws > 0 && saved.Index != (saved.Draws - 1) % WordCount + 1)
            throw new InvalidDataException("Saved detection random stream has invalid source extent or cursor.");
        var words = saved.Words.ToArray();
        _words = words; _index = saved.Index; _draws = saved.Draws;
    }

    private void Twist()
    {
        // This is an in-place twist. The latter portion deliberately reads
        // already updated earlier words; a separate all-old array differs.
        for (var index = 0; index < WordCount - 1; index++)
        {
            var joined = (_words[index] & 0x80000000U) | (_words[index + 1] & 0x7fffffffU);
            var other = index + Offset < WordCount ? index + Offset : index + Offset - WordCount;
            _words[index] = _words[other] ^ (joined >> 1) ^ ((joined & 1) == 0 ? 0 : Matrix);
        }
        var final = (_words[WordCount - 1] & 0x80000000U) | (_words[0] & 0x7fffffffU);
        _words[WordCount - 1] = _words[Offset - 1] ^ (final >> 1) ^ ((final & 1) == 0 ? 0 : Matrix);
        _index = 0;
    }
}
