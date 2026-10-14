using F = OpenNV.Runtime.World.Physics.FalloutPhysicalFloat32;

namespace OpenNV.Runtime.Gameplay.State;

internal static partial class FalloutActorTeleportFade
{
    // Check the retained source stores and their observation order. This never
    // calls a timer, setting, Actor, scene, or renderer and never replays a child.
    internal static void ValidateTransition(FalloutActorTeleportFadeFields before,
        FalloutActorTeleportFadeFields after, FalloutActorTeleportFadeTrace trace, bool enginePlayer)
    {
        Validate(before); Validate(after);
        if (trace is null || trace.Invocation == Guid.Empty || string.IsNullOrWhiteSpace(trace.Owner) ||
            trace.Changed < 1 || !Enum.IsDefined(trace.Step) ||
            trace.FailedAt is { } failed && (!Enum.IsDefined(failed) || failed == FalloutActorTeleportFadeStep.Failed) ||
            (trace.Step == FalloutActorTeleportFadeStep.Failed) != (trace.FailedAt is not null) ||
            (trace.Step == FalloutActorTeleportFadeStep.Failed) != (trace.Failure is not null) ||
            trace.Failure is not null && string.IsNullOrWhiteSpace(trace.Failure) ||
            trace.SecondsBits.HasValue != trace.DeltaBits.HasValue ||
            trace.SecondsBits is not null && trace.TimerBits is null ||
            trace.DeltaBits is { } retainedDelta && retainedDelta != Bits(F.Divide(Value(trace.TimerBits!.Value), Value(trace.SecondsBits!.Value))))
            throw new InvalidDataException("Teleport receipt lost its actual source math or entered/failed trace.");

        var matched = false;
        Walk(npcCompleted: false);
        if (!enginePlayer && before.State == 4) Walk(npcCompleted: true);
        if (!matched) throw new InvalidDataException("Teleport receipt fields are not an actual source branch or committed failure prefix.");

        void Walk(bool npcCompleted)
        {
            var fields = before;
            var expected = trace with
            {
                Changed = 1,
                Step = FalloutActorTeleportFadeStep.Entered,
                FailedAt = null,
                TimerBits = null,
                SecondsBits = null,
                DeltaBits = null,
                OpacityReturned = false,
                Result = null,
                Failure = null
            };
            Check();
            if (fields.State == 0)
            {
                if (!(Value(fields.FadeBits) >= 1))
                {
                    if (!Fade(One)) return;
                    Opacity();
                }
                Returned(false); return;
            }
            Step(FalloutActorTeleportFadeStep.Timer);
            if (trace.TimerBits is not { } timer) return;
            expected = expected with { TimerBits = timer, Changed = expected.Changed + 1 }; Check();
            if (trace.SecondsBits is not { } seconds || trace.DeltaBits is not { } deltaBits) return;
            expected = expected with { SecondsBits = seconds, DeltaBits = deltaBits, Changed = expected.Changed + 1 }; Check();
            var delta = Value(deltaBits);
            switch (fields.State)
            {
                case 1 or 3:
                    var increased = F.Add(delta, Value(fields.FadeBits));
                    if (!Fade(Bits(increased))) return;
                    if (increased > 1 && (!Fade(One) || !State(0))) return;
                    break;
                case 2:
                    if (Value(fields.FadeBits) > 0)
                    {
                        var decreased = F.Subtract(Value(fields.FadeBits), delta);
                        if (!Fade(Bits(decreased))) return;
                        if (decreased < 0 && (!Fade(0) || !TargetClear())) return;
                    }
                    break;
                case 4 when enginePlayer:
                    var target = fields.Target;
                    if (!Fade(One) || !TargetClear()) return;
                    if (target is not null) Step(FalloutActorTeleportFadeStep.TargetChild);
                    if (!State(0)) return;
                    Returned(true); return;
                case 4:
                    var remaining = F.Subtract(Value(fields.FadeBits), delta);
                    if (!Fade(Bits(remaining))) return;
                    if (remaining < 0)
                    {
                        var npcTarget = fields.Target;
                        if (!Fade(0) || !TargetClear()) return;
                        if (npcTarget is not null) Step(FalloutActorTeleportFadeStep.TargetChild);
                        Step(FalloutActorTeleportFadeStep.ActorChild);
                        if (npcCompleted)
                        {
                            if (!State(0)) return;
                            Returned(true); return;
                        }
                        if (fields.State is not (5 or 6))
                        {
                            var previousFade = Value(fields.FadeBits);
                            if (!State(3) || !TargetClear()) return;
                            if (!float.IsNaN(previousFade) && previousFade >= 1 && !Fade(0)) return;
                        }
                    }
                    break;
                case 5 or 6:
                    var expired = F.Subtract(Value(fields.FadeBits), delta);
                    if (!Fade(Bits(expired))) return;
                    if (expired < 0)
                    {
                        if (!Fade(0)) return;
                        Step(FalloutActorTeleportFadeStep.ActorChild);
                    }
                    break;
            }
            Opacity(); Returned(fields.State is >= 3 and <= 6);

            void Check()
            {
                if (fields != after) return;
                if (trace == expected || trace == (expected with
                {
                    Step = FalloutActorTeleportFadeStep.Failed,
                    FailedAt = expected.Step,
                    Changed = expected.Changed + 1,
                    Failure = trace.Failure
                })) matched = true;
            }
            void Step(FalloutActorTeleportFadeStep step)
            {
                expected = expected with { Step = step, Changed = expected.Changed + 1 }; Check();
            }
            bool Write(FalloutActorTeleportFadeFields next)
            {
                // A checked counter overflow faults before publication.
                if (fields.Stores == long.MaxValue) return false;
                fields = next with { Stores = fields.Stores + 1 }; Check(); return true;
            }
            bool Fade(uint bits) { Step(FalloutActorTeleportFadeStep.FadeStore); return Write(fields with { FadeBits = bits }); }
            bool State(int value) { Step(FalloutActorTeleportFadeStep.StateStore); return Write(fields with { State = value }); }
            bool TargetClear() { Step(FalloutActorTeleportFadeStep.TargetClear); return Write(fields with { Target = null }); }
            void Opacity()
            {
                Step(FalloutActorTeleportFadeStep.Opacity);
                expected = expected with { OpacityReturned = true, Changed = expected.Changed + 1 }; Check();
            }
            void Returned(bool result)
            {
                expected = expected with
                {
                    Result = result,
                    Step = FalloutActorTeleportFadeStep.Returned,
                    Changed = expected.Changed + 1
                }; Check();
            }
        }
    }
}
