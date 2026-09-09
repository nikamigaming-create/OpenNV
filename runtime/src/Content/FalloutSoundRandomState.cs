namespace OpenNV.Runtime.Content;

internal sealed class FalloutSoundRandomState
{
    private const ulong WeylIncrement = 0x9e3779b97f4a7c15UL;
    private const ulong FirstMixMultiplier = 0xbf58476d1ce4e5b9UL;
    private const ulong SecondMixMultiplier = 0x94d049bb133111ebUL;
    private const int FirstMixShift = 30;
    private const int SecondMixShift = 27;
    private const int FinalMixShift = 31;

    internal FalloutSoundRandomState(ulong state) => State = state;

    internal ulong State { get; private set; }
    internal void Restore(ulong state) => State = state;

    internal float NextUnitFloat() => (float)(unchecked((uint)NextUInt64()) / 4294967296.0);

    internal uint NextBounded(uint exclusiveUpperBound)
    {
        if (exclusiveUpperBound == 0U)
            throw new ArgumentOutOfRangeException(nameof(exclusiveUpperBound));
        var rejectionThreshold = unchecked((uint)(0U - exclusiveUpperBound)) % exclusiveUpperBound;
        while (true)
        {
            var candidate = unchecked((uint)NextUInt64());
            if (candidate >= rejectionThreshold)
                return candidate % exclusiveUpperBound;
        }
    }

    private ulong NextUInt64()
    {
        State = unchecked(State + WeylIncrement);
        var mixed = State;
        mixed = (mixed ^ (mixed >> FirstMixShift)) * FirstMixMultiplier;
        mixed = (mixed ^ (mixed >> SecondMixShift)) * SecondMixMultiplier;
        return mixed ^ (mixed >> FinalMixShift);
    }
}
