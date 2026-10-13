using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutActorIdleAnimationFields(byte Flag, Guid? CurrentIdle, short Action,
    uint WaitBits, long Stores)
{
    internal static FalloutActorIdleAnimationFields Constructed => new(0, null, -1, 0x3f800000, 0);
}
internal sealed record FalloutIdleSelectedObject(byte Kind, bool HasExtra, FalloutIdlePackageSource? Source = null);
internal sealed record FalloutIdleAnimationAction(uint State, uint? SequenceState);
internal sealed record FalloutIdleAnimationContext(Guid Ownership, FalloutIdleAnimationAction? First,
    FalloutIdleAnimationAction? Second)
{
    internal bool BlocksIdle
    {
        get
        {
            if (First is { } first)
            {
                if (first.SequenceState is null) return first.State != 3;
                if (first.SequenceState is 2 or 5) return true;
            }
            return Second is { } second && (second.SequenceState is null or 2 or 5);
        }
    }
    internal bool AllowsIdle => Second is null &&
        (First is null || First is { SequenceState: not null, State: 3 });
}

// Each getter is an independent original child. Model selection is repeated;
// an animation context is neither implied by a model nor the common body.
internal interface IFalloutActorIdleAnimationConsumers
{
    string Owner { get; }
    FalloutIdlePackageRead? SelectedPackageRead => null;
    FalloutSourceSceneNode? ReadModel();
    FalloutIdleSelectedObject? ReadSelectedObject();
    FalloutIdleAnimationContext? ReadAnimation();
    FalloutIdleSelectedObject? ReadAlternateObject();
    uint? ReadBoundRadius(FalloutSourceSceneNode model);
    float ReadDistanceToPlayer();
    float ReadDistanceSetting();
    bool ReadActorStatePredicate();
    bool ReadActorClassPredicate();
    FalloutActorIdleClassValue? ActorClassValue => null;
    float ReadWaitSetting();
    float ReadMainScaledTimer();
    void StartIdle();
}

internal sealed record FalloutActorIdleAnimationTrace(string Step, IReadOnlyList<FalloutSourceSceneNode?> Models,
    Guid? Animation, uint? RadiusBits, uint? DistanceBits, uint? DistanceSettingBits,
    uint? WaitSettingBits, uint? ResetWaitSettingBits, uint? MainTimerBits, bool IdleCalled, bool Returned, string? Failure,
    bool SelectedObjectReturned = false, FalloutIdleSelectedObject? SelectedObject = null,
    bool AlternateObjectReturned = false, FalloutIdleSelectedObject? AlternateObject = null,
    FalloutIdlePackageRead? SelectedPackageRead = null, FalloutActorIdleClassValue? ActorClassValue = null);

internal static class FalloutActorIdleAnimationUpdate
{
    internal static void Execute(FalloutDetectionProcessLevel level, IFalloutActorIdleAnimationConsumers consumers,
        Func<FalloutActorIdleAnimationFields> readFields, Action<FalloutActorIdleAnimationFields> store,
        Action<FalloutActorIdleAnimationTrace> observe)
    {
        ArgumentNullException.ThrowIfNull(consumers); ArgumentNullException.ThrowIfNull(readFields);
        ArgumentNullException.ThrowIfNull(store); ArgumentNullException.ThrowIfNull(observe);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumers.Owner);
        if (!Enum.IsDefined(level)) throw new InvalidDataException("Idle update has no selected original process class.");
        var models = new List<FalloutSourceSceneNode?>();
        var trace = new FalloutActorIdleAnimationTrace("class", [], null, null, null, null, null, null, null, false, false, null);
        try
        {
            if (level != FalloutDetectionProcessLevel.High) { Return("lower-class-return"); return; }
            Step("first-model"); var model = consumers.ReadModel(); Model(model);
            if (model is null) { Return("null-model-return"); return; }
            Step("selected-object"); var selected = consumers.ReadSelectedObject();
            trace = trace with
            {
                SelectedObjectReturned = true,
                SelectedObject = selected,
                SelectedPackageRead = consumers.SelectedPackageRead
            }; observe(trace);
            Step("animation"); var animation = consumers.ReadAnimation();
            if (animation is { Ownership: var ownership } && ownership == Guid.Empty)
                throw new InvalidDataException("Idle animation context lost its actual factory ownership.");
            trace = trace with { Animation = animation?.Ownership }; observe(trace);
            if (selected is { Kind: 0x1c })
            {
                Step("alternate-object"); selected = consumers.ReadAlternateObject();
                trace = trace with { AlternateObjectReturned = true, AlternateObject = selected }; observe(trace);
            }
            var fields = readFields();
            if (fields.Flag != 0)
            {
                // The original flag arm dereferences its context without a
                // null guard. A missing context is a divergence, not false.
                if (animation is null) throw new InvalidDataException("High idle flag has no actual animation context.");
                if (!animation.BlocksIdle && animation.AllowsIdle)
                    store(fields = fields with { Flag = 0, Stores = checked(fields.Stores + 1) });
            }
            Step("second-model"); model = consumers.ReadModel(); Model(model);
            if (model is null) throw new InvalidDataException("High idle second model disappeared before its required bound dereference.");
            Step("bound"); var radius = consumers.ReadBoundRadius(model) ?? 0u;
            trace = trace with { RadiusBits = radius }; observe(trace);
            var scaledRadius = BitConverter.UInt32BitsToSingle(radius) * .015625f;
            if (fields.CurrentIdle is not null) { Return("current-idle-return"); return; }
            Step("distance"); var distance = consumers.ReadDistanceToPlayer();
            var setting = consumers.ReadDistanceSetting();
            trace = trace with { DistanceBits = Bits(distance), DistanceSettingBits = Bits(setting) }; observe(trace);
            if (distance > scaledRadius * setting) { Return("distance-return"); return; }
            if (selected is { HasExtra: true } or { Kind: 0x1a }) { Return("object-return"); return; }
            if (animation is null) { Return("null-animation-return"); return; }
            if (animation.BlocksIdle || !animation.AllowsIdle) { Return("animation-return"); return; }
            if (fields.Action != -1) { Return("action-return"); return; }
            Step("actor-state"); if (consumers.ReadActorStatePredicate()) { Return("actor-state-return"); return; }
            Step("actor-class"); var blocked = consumers.ReadActorClassPredicate();
            trace = trace with { ActorClassValue = consumers.ActorClassValue }; observe(trace);
            if (blocked) { Return("actor-class-return"); return; }
            var wait = BitConverter.UInt32BitsToSingle(fields.WaitBits);
            if (wait < 0)
            {
                Step("wait-setting"); setting = consumers.ReadWaitSetting();
                trace = trace with { WaitSettingBits = Bits(setting) }; observe(trace);
                // UCOMISS/LAHF/TEST 44h admits unequal and unordered values.
                // Both signed zeros skip the call; NaN enters it.
                if (setting != 0)
                {
                    Step("idle-child"); consumers.StartIdle();
                    trace = trace with { IdleCalled = true }; observe(trace);
                    // This is a separate source setting read after the child.
                    Step("wait-reset"); setting = consumers.ReadWaitSetting();
                    trace = trace with { ResetWaitSettingBits = Bits(setting) }; observe(trace);
                    store(fields with { WaitBits = Bits(setting), Stores = checked(fields.Stores + 1) });
                    Return("reset-return"); return;
                }
            }
            Step("main-timer"); var seconds = consumers.ReadMainScaledTimer();
            trace = trace with { MainTimerBits = Bits(seconds) }; observe(trace);
            store(fields with { WaitBits = Bits(wait - seconds), Stores = checked(fields.Stores + 1) });
            Return("subtract-return");
        }
        catch (Exception error) { trace = trace with { Failure = error.Message }; observe(trace); throw; }
        void Model(FalloutSourceSceneNode? value)
        {
            if (value is not null && (value.Process == Guid.Empty || value.Factory == Guid.Empty || value.NativeRoot == 0))
                throw new InvalidDataException("High idle model lost its actual living factory/root.");
            models.Add(value); trace = trace with { Models = models.ToArray() }; observe(trace);
        }
        void Step(string step) { trace = trace with { Step = step }; observe(trace); }
        void Return(string step) { trace = trace with { Step = step, Returned = true }; observe(trace); }
        static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
    }
}
