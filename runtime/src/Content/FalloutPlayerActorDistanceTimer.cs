namespace OpenNV.Runtime.Content;

// The retail constructor does not write this cell. OpenNV deliberately starts
// its own managed timer at zero, instead of inheriting arbitrary heap payload.
// This initialization difference is reported separately from source behavior.
internal sealed record FalloutPlayerActorDistanceTimer(uint Bits, long Stores)
{
    internal const string Initialization = "managed-Player-zero;retail-constructor-unwritten;initial-byte-parity-unaccepted";
    internal static FalloutPlayerActorDistanceTimer Allocated => new(0, 0);
    internal bool SubtractsArgument => !(BitConverter.UInt32BitsToSingle(Bits) <= 0);
    internal FalloutPlayerActorDistanceTimer Subtract(float seconds) => this with
    {
        Bits = BitConverter.SingleToUInt32Bits(BitConverter.UInt32BitsToSingle(Bits) - seconds),
        Stores = checked(Stores + 1)
    };
    internal FalloutPlayerActorDistanceTimer Reset(float setting) => this with
    { Bits = BitConverter.SingleToUInt32Bits(setting), Stores = checked(Stores + 1) };
}
