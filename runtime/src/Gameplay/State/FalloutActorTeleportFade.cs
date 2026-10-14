using OpenNV.Runtime.Content;
using F = OpenNV.Runtime.World.Physics.FalloutPhysicalFloat32;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutActorTeleportFadeFields(int State, uint FadeBits, FalloutFormKey? Target, long Stores)
{
    internal static FalloutActorTeleportFadeFields Constructed => new(1, 0, null, 0);
}
internal enum FalloutActorTeleportFadeStep { Entered, Timer, FadeStore, TargetClear, StateStore, TargetChild, ActorChild, Opacity, Returned, Failed }
internal sealed record FalloutActorTeleportFadeTrace(Guid Invocation, string Owner, long Changed,
    FalloutActorTeleportFadeStep Step, FalloutActorTeleportFadeStep? FailedAt, uint? TimerBits,
    uint? SecondsBits, uint? DeltaBits, bool OpacityReturned, bool? Result, string? Failure);

internal interface IFalloutActorTeleportFadeConsumers
{
    string Owner { get; }
    Guid Invocation { get; }
    uint ReadMainScaledTimerBits();
    uint ReadTeleportSecondsBits(string setting);
    void ApplyOpacity();
    void MoveToTarget(FalloutFormKey target);
    bool ReadNpcTeleportCompletion();
    void DisableActor();
    void EnableActor();
}

// The field publisher is the current High process, not a copy of an Actor or a
// renderer timer. Every store precedes its child and survives that child's fault.
internal static partial class FalloutActorTeleportFade
{
    internal static void Execute(Guid invocation, bool enginePlayer, IFalloutActorTeleportFadeConsumers consumers,
        Func<FalloutActorTeleportFadeFields> read, Action<FalloutActorTeleportFadeFields> store,
        Action<FalloutActorTeleportFadeTrace> observe)
    {
        if (invocation == Guid.Empty) throw new ArgumentException("Teleport fade requires its actual invocation.", nameof(invocation));
        ArgumentNullException.ThrowIfNull(consumers); ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(store); ArgumentNullException.ThrowIfNull(observe);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumers.Owner);
        if (consumers.Invocation != invocation) throw new InvalidOperationException("Teleport child changed its actual caller invocation.");
        long sequence = 0;
        var trace = new FalloutActorTeleportFadeTrace(invocation, consumers.Owner, ++sequence,
            FalloutActorTeleportFadeStep.Entered, null, null, null, null, false, null, null);
        observe(trace);
        try
        {
            var fields = read();
            if (fields.State == 0)
            {
                // COMISS/JAE: unordered enters the same store arm as less-than.
                if (!(Value(fields.FadeBits) >= 1)) { Fade(One); Opacity(); }
                Return(false); return;
            }
            Step(FalloutActorTeleportFadeStep.Timer);
            var timer = consumers.ReadMainScaledTimerBits();
            trace = trace with { TimerBits = timer, Changed = ++sequence }; observe(trace);
            var seconds = consumers.ReadTeleportSecondsBits(enginePlayer ? FalloutPlayerOpacitySource.PlayerFadeSeconds : FalloutPlayerOpacitySource.ActorFadeSeconds);
            var delta = F.Divide(Value(timer), Value(seconds));
            trace = trace with { SecondsBits = seconds, DeltaBits = Bits(delta), Changed = ++sequence }; observe(trace);
            fields = read();
            switch (fields.State)
            {
                case 1 or 3:
                    var increased = F.Add(delta, Value(fields.FadeBits));
                    Fade(Bits(increased));
                    if (increased > 1) { Fade(One); State(0); }
                    break;
                case 2:
                    if (Value(fields.FadeBits) > 0)
                    {
                        var decreased = F.Subtract(Value(fields.FadeBits), delta); Fade(Bits(decreased));
                        if (decreased < 0) { Fade(0); TargetClear(); }
                    }
                    break;
                case 4 when enginePlayer:
                    var target = fields.Target;
                    Fade(One); TargetClear();
                    if (target is { } playerTarget) { Step(FalloutActorTeleportFadeStep.TargetChild); consumers.MoveToTarget(playerTarget); }
                    State(0); Return(true); return;
                case 4:
                    var remaining = F.Subtract(Value(fields.FadeBits), delta); Fade(Bits(remaining));
                    if (remaining < 0)
                    {
                        var npcTarget = read().Target; Fade(0); TargetClear();
                        if (npcTarget is { } value) { Step(FalloutActorTeleportFadeStep.TargetChild); consumers.MoveToTarget(value); }
                        Step(FalloutActorTeleportFadeStep.ActorChild);
                        if (consumers.ReadNpcTeleportCompletion()) { State(0); Return(true); return; }
                        if (read().State is not (5 or 6))
                        {
                            var previousFade = Value(read().FadeBits); State(3); TargetClear();
                            // COMISS/JB: unordered takes the no-reset arm.
                            if (!float.IsNaN(previousFade) && previousFade >= 1) Fade(0);
                        }
                    }
                    break;
                case 5 or 6:
                    var expired = F.Subtract(Value(fields.FadeBits), delta); Fade(Bits(expired));
                    if (expired < 0)
                    {
                        Fade(0); Step(FalloutActorTeleportFadeStep.ActorChild);
                        if (fields.State == 6) consumers.DisableActor(); else consumers.EnableActor();
                    }
                    break;
                    // Unknown integer states still take the observed common opacity
                    // arm and unsigned source switch. They are not clamped to an enum.
            }
            Opacity(); Return(read().State is >= 3 and <= 6);
        }
        catch (Exception error)
        {
            trace = trace with
            {
                FailedAt = trace.Step,
                Step = FalloutActorTeleportFadeStep.Failed,
                Changed = ++sequence,
                Failure = error.Message
            }; observe(trace); throw;
        }
        void Step(FalloutActorTeleportFadeStep step) { trace = trace with { Step = step, Changed = ++sequence }; observe(trace); }
        void Write(FalloutActorTeleportFadeFields fields) => store(fields with { Stores = checked(fields.Stores + 1) });
        void Fade(uint bits) { Step(FalloutActorTeleportFadeStep.FadeStore); Write(read() with { FadeBits = bits }); }
        void State(int value) { Step(FalloutActorTeleportFadeStep.StateStore); Write(read() with { State = value }); }
        void TargetClear() { Step(FalloutActorTeleportFadeStep.TargetClear); Write(read() with { Target = null }); }
        void Opacity() { Step(FalloutActorTeleportFadeStep.Opacity); consumers.ApplyOpacity(); trace = trace with { OpacityReturned = true, Changed = ++sequence }; observe(trace); }
        void Return(bool result) { trace = trace with { Result = result, Step = FalloutActorTeleportFadeStep.Returned, Changed = ++sequence }; observe(trace); }
    }
    internal const uint One = 0x3f800000;
    internal static float Value(uint bits) => BitConverter.UInt32BitsToSingle(bits);
    internal static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
    internal static void Validate(FalloutActorTeleportFadeFields fields)
    {
        if (fields is null || fields.Stores < 0 || fields.Stores == 0 && fields != FalloutActorTeleportFadeFields.Constructed)
            throw new InvalidDataException("High teleport fade omitted its constructor or retained stores.");
    }
}
