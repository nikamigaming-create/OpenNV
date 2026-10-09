using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutInterfaceFadeDirection { Absent, Increasing, Decreasing }
internal enum FalloutInterfaceFadeOperation
{ Start, ReplaceRetirement, End, Frame, FrameOpacity, FrameRetirement, ColdPublication, NativePublication, NativeRetirement }
internal sealed record FalloutInterfaceFadeChannel(int Channel, long Generation, long Revision,
    FalloutInterfaceFadeDirection Direction, float Duration, float Opacity);
internal sealed record FalloutInterfaceFadeFailure(long Attempt, int? Channel, FalloutInterfaceFadeOperation Operation, string Error);
internal sealed record FalloutInterfaceFadeSnapshot(string Schema, FalloutInterfaceFadeSource Source, long Attempt,
    byte ReleaseHold, IReadOnlyList<FalloutInterfaceFadeChannel> Channels, FalloutInterfaceFadeFailure? Failure,
    IReadOnlyList<FalloutInterfaceFadeFailure> NativeFailures)
{
    internal void Validate()
    {
        if (Source is null || Channels is null || NativeFailures is null)
            throw new InvalidDataException("Interface fade snapshot omits a required current owner.");
        Source.Validate();
        if (Schema != FalloutInterfaceFade.Schema || Attempt < 0 || ReleaseHold > 3 || Channels.Count != 3 ||
            Channels.Where((channel, ordinal) => channel is null || channel.Channel != ordinal || channel.Generation < 0 || channel.Revision < 0 ||
                !Enum.IsDefined(channel.Direction) || !float.IsFinite(channel.Duration) || channel.Duration < 0 ||
                !float.IsFinite(channel.Opacity) || channel.Opacity is < 0 or > 1 ||
                channel.Direction == FalloutInterfaceFadeDirection.Absent && channel.Opacity != 0 ||
                channel.Generation == 0 && (channel.Revision != 0 || channel.Direction != FalloutInterfaceFadeDirection.Absent || channel.Duration != 0) ||
                channel.Direction != FalloutInterfaceFadeDirection.Absent && (channel.Generation == 0 || channel.Revision == 0)).Any())
            throw new InvalidDataException("Interface fade snapshot is structurally incomplete.");
        void FailureValid(FalloutInterfaceFadeFailure failure)
        {
            if (failure.Attempt < 1 || failure.Attempt > Attempt || failure.Channel is < 0 or >= 3 ||
                !Enum.IsDefined(failure.Operation) || string.IsNullOrWhiteSpace(failure.Error))
                throw new InvalidDataException("Interface fade lost its attempted operation prefix.");
        }
        if (Failure is { } retained) FailureValid(retained);
        foreach (var failure in NativeFailures) FailureValid(failure);
        if (NativeFailures.Count != 0 && Failure is null)
            throw new InvalidDataException("Interface fade native failure was silently cleared.");
    }
}
