using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutInterfaceFadeQuery(string Source, int Channel, long Generation, long Revision,
    bool NativePointer, bool Increasing, float Opacity, bool Value)
{
    internal void Validate()
    {
        if (!FalloutAdvancementRuntimeReceipt.Digest(Source) || Channel is < 0 or >= 3 || Generation < 0 || Revision < 0 ||
            !float.IsFinite(Opacity) || Opacity is < 0 or > 1 ||
            Value != (NativePointer && Increasing && Opacity == 1) || !NativePointer && (Increasing || Opacity != 0))
            throw new InvalidDataException("Interface channel query omits its actual pointer/direction/Float32 scalar.");
    }
}

internal sealed partial class FalloutInterfaceFade
{
    internal FalloutInterfaceFadeQuery QueryIncreasingOpaque(int channel)
    {
        RequireHealthy(); var current = Channel(channel);
        var present = current.Direction != FalloutInterfaceFadeDirection.Absent;
        if (present) RequirePublished(current);
        var increasing = present && current.Direction == FalloutInterfaceFadeDirection.Increasing;
        var result = new FalloutInterfaceFadeQuery(Source.Identity, channel, current.Generation, current.Revision,
            present, increasing, current.Opacity, present && increasing && current.Opacity == 1);
        result.Validate(); return result;
    }
}
