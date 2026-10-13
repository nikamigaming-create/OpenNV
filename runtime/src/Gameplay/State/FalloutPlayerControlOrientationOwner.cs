using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// These reads belong to current C# reference/world fields. A Godot camera or
// transform is never a producer. Each returned read retains its actual source
// lifetime and invocation; a cold receipt grants no current runtime lease.
internal sealed record FalloutPlayerControlReadLease(string Source, string Stack, Guid Process,
    Guid Factory, Guid Main, long MainOrdinal, long Revision, string Producer)
{
    internal void Validate()
    {
        if (!FalloutAdvancementRuntimeReceipt.Digest(Source) || string.IsNullOrWhiteSpace(Stack) ||
            Process == Guid.Empty || Factory == Guid.Empty || Main == Guid.Empty || MainOrdinal < 1 ||
            Revision < 0 || string.IsNullOrWhiteSpace(Producer))
            throw new InvalidDataException("Player orientation getter lost its actual source/current invocation lease.");
    }
}
internal sealed record FalloutPlayerControlWorldFovRead(FalloutPlayerControlReadLease Lease, uint Bits);
internal sealed record FalloutPlayerControlYawRead(FalloutPlayerControlReadLease Lease, uint ReferenceYawBits,
    uint ReferenceRollBits, bool IsCharacter, FalloutFormKey Base, string BaseSignature, uint BaseFlags);
internal sealed record FalloutPlayerControlPitchRead(FalloutPlayerControlReadLease Lease, uint Bits);

internal interface IFalloutPlayerControlOrientationOwner
{
    FalloutPlayerMovementControlSource Source { get; }
    string Stack { get; }
    Guid Process { get; }
    Guid Factory { get; }
    void RequireCurrent();
    void RequireCurrent(Guid main, long ordinal);
    FalloutPlayerControlWorldFovRead ReadCurrentWorldFov(Guid main, long ordinal);
    FalloutPlayerControlYawRead ReadCurrentReferenceYaw(Guid main, long ordinal);
    FalloutPlayerControlPitchRead ReadCurrentReferencePitch(Guid main, long ordinal);
}

internal static class FalloutPlayerControlOrientation
{
    internal const uint NpcTiltYaw = 0x00800000;
    internal const uint TwoPiBits = 0x40c90fdb;
    private static readonly float TwoPi = BitConverter.UInt32BitsToSingle(TwoPiBits);
    private static readonly double WideTwoPi = BitConverter.Int64BitsToDouble(0x401921fb60000000);

    internal static uint Yaw(FalloutPlayerControlYawRead read, uint playerOffset)
    {
        read.Lease.Validate();
        if (string.IsNullOrWhiteSpace(read.Base.OwnerPlugin) || read.Base.ObjectId == 0 ||
            read.BaseSignature is not ("NPC_" or "CREA"))
            throw new InvalidDataException("Player yaw getter has no exact current winning Actor base.");
        var yaw = Finite(read.ReferenceYawBits); var offset = Finite(playerOffset);
        if (read.IsCharacter && read.BaseSignature == "NPC_" && (read.BaseFlags & NpcTiltYaw) != 0)
            yaw = Normalize((float)(yaw + Finite(read.ReferenceRollBits)));
        return BitConverter.SingleToUInt32Bits(Normalize((float)(yaw + offset)));
    }
    internal static float WorldFovScale(float declared, uint currentBits)
    {
        var current = Finite(currentBits);
        if (!float.IsFinite(declared)) throw new InvalidDataException("Default world FOV is not a source Float32.");
        return declared > current ? (float)(current / declared) : 1;
    }
    internal static float Finite(uint bits)
    {
        var value = BitConverter.UInt32BitsToSingle(bits);
        if (!float.IsFinite(value))
            throw new NotSupportedException("source-Player-control-nonfinite-orientation-arithmetic-unowned");
        return value;
    }
    private static float Normalize(float value)
    {
        if (!float.IsFinite(value))
            throw new NotSupportedException("source-Player-control-nonfinite-orientation-arithmetic-unowned");
        // Preserve the source's Float32 add/store, then its signed remainder
        // with the independently emitted Float64 divisor. It is not a positive
        // modulo, single-wrap subtraction or IEEE nearest-quotient remainder.
        if (value < 0) return (float)((double)(float)(value + TwoPi) % WideTwoPi);
        if (value >= TwoPi) return (float)((double)value % WideTwoPi);
        return value;
    }
}
