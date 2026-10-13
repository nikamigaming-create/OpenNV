namespace OpenNV.Runtime.Content;

// Distinct Actor and Player fields reduced from their actual constructors and
// the scalar stores following the independent process getters. This is not a
// gameplay cooldown, frame argument, or presentation cache substituted by name.
internal sealed record FalloutPlayerSceneScalarFields(uint ActorRemainingBits, uint ActorWord,
    uint PlayerCachedWord, uint FirstScratch, uint SecondScratch)
{
    internal static FalloutPlayerSceneScalarFields Constructed => new(0, 0, 0, 0, 0);
    internal bool SubtractsScaledTimer => BitConverter.UInt32BitsToSingle(ActorRemainingBits) > 0;

    internal FalloutPlayerSceneScalarFields Update(float scaledTimer) => this with
    {
        FirstScratch = 0,
        SecondScratch = 0,
        ActorRemainingBits = SubtractsScaledTimer
            ? BitConverter.SingleToUInt32Bits(BitConverter.UInt32BitsToSingle(ActorRemainingBits) - scaledTimer)
            : ActorRemainingBits,
        ActorWord = SubtractsScaledTimer ? ActorWord : 0,
    };
}
