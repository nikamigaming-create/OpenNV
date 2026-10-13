using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutPlayerReferenceHudStep { ReferenceGate, ColorRead, BracketHide, MeterHide, NameHide, HostileStore, ArgumentStore, Returned, Failed }
internal sealed record FalloutPlayerReferenceHudCall(string Source, string Stack, Guid Process, Guid Factory,
    Guid Main, long MainOrdinal, long Entered, long Changed, FalloutFormKey? Reference, byte Argument,
    uint? ColorBits, byte Hostile, byte DeliveredArgument, FalloutPlayerReferenceHudStep Step,
    FalloutPlayerReferenceHudStep? FailedAt, string? Failure);

// C# owns the actual source menu component tree and flag writers. Presentation
// consumes this same tree. Menu state is constructed afresh on a cold launch;
// saved Player failure receipts cannot promote an old menu factory into one.
internal sealed class FalloutPlayerReferenceHudState
{
    internal FalloutPlayerReferenceHudSource Source { get; }
    internal string Stack { get; }
    internal Guid Process { get; }
    internal Guid Factory { get; } = Guid.NewGuid();
    internal FalloutUiComponentStore Components { get; }
    internal string Branch { get; }
    internal string Bracket { get; }
    internal string Meter { get; }
    internal string Name { get; }
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private long _sequence;
    private byte _suppressed, _hostile, _argument;
    private bool _entered, _retired;
    private FalloutPlayerReferenceHudCall? _call;
    internal FalloutPlayerReferenceHudCall? LastCall => _call;
    internal object State => new
    {
        source = Source.Identity,
        Stack,
        Process,
        Factory,
        Branch,
        Bracket,
        Meter,
        Name,
        suppressed = _suppressed,
        hostile = _hostile,
        argument = _argument,
        call = _call,
        retired = _retired
    };
    internal FalloutPlayerReferenceHudState(FalloutPlayerReferenceHudSource source, string stack, Guid process,
        FalloutUiComponentStore components)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack); ArgumentNullException.ThrowIfNull(components);
        if (process == Guid.Empty) throw new ArgumentException("HUD requires its actual Main process.", nameof(process));
        Source = source; Stack = stack; Process = process; Components = components;
        _suppressed = source.InitialSuppressed; _hostile = source.InitialHostile; _argument = source.InitialArgument;
        Branch = "HUDMainMenu/" + source.Branch;
        // Missing or modified definitions fail before the source factory returns.
        _ = components.CopyComponent(Branch);
        Bracket = components.InstantiateTemplate(Branch, source.BracketTemplate);
        Meter = components.InstantiateTemplate(Branch, source.MeterTemplate);
        Name = components.InstantiateTemplate(Branch, source.NameTemplate);
        components.WriteComponentFloat(Branch, "user0", BitConverter.UInt32BitsToSingle(source.InitialColorBits));
        foreach (var tile in new[] { Bracket, Meter }) components.WriteComponentFloat(tile, "visible", 1);
    }
    internal void Update(Guid main, long ordinal, FalloutFormKey? reference, byte argument)
    {
        RequireCurrent();
        if (main == Guid.Empty || ordinal < 1 || _call is { Step: FalloutPlayerReferenceHudStep.Failed })
            throw new InvalidOperationException("HUD child cannot lose or replay its actual failed Main invocation.");
        if (_call is { } previous && (main != previous.Main || ordinal <= previous.MainOrdinal))
            throw new InvalidOperationException("HUD child reordered the actual Main source calls.");
        _entered = true;
        _call = new(Source.Identity, Stack, Process, Factory, main, ordinal, Next(), _sequence, reference, argument,
            null, _hostile, _argument, FalloutPlayerReferenceHudStep.ReferenceGate, null, null);
        try
        {
            if (reference is not null && _suppressed == 0 && argument != 0)
                throw new NotSupportedException("source-Player-HUD-actual-reference-actor-dead-hostility-value-children-unowned");
            Changed(FalloutPlayerReferenceHudStep.ColorRead);
            var color = Components.ReadComponentFloat(Branch, "user0");
            _call = _call with { ColorBits = BitConverter.SingleToUInt32Bits(color), Changed = Next() };
            // Source UCOMISS/LAHF/TEST/JP hides only ordered equality to one.
            // Neither inequality nor unordered comparison selects these stores.
            if (FalloutPlayerReferenceHudSource.ClearsTiles(BitConverter.SingleToUInt32Bits(color)))
            {
                Hide(FalloutPlayerReferenceHudStep.BracketHide, Bracket);
                Hide(FalloutPlayerReferenceHudStep.MeterHide, Meter);
                Hide(FalloutPlayerReferenceHudStep.NameHide, Name);
            }
            Changed(FalloutPlayerReferenceHudStep.HostileStore); _hostile = 0;
            _call = _call with { Hostile = _hostile, Changed = Next() };
            Changed(FalloutPlayerReferenceHudStep.ArgumentStore); _argument = argument;
            _call = _call with { DeliveredArgument = _argument, Changed = Next() };
            Changed(FalloutPlayerReferenceHudStep.Returned);
        }
        catch (Exception error)
        {
            _call = _call with
            {
                FailedAt = _call.Step,
                Step = FalloutPlayerReferenceHudStep.Failed,
                Changed = Next(),
                Failure = error.Message
            }; throw;
        }
        finally { _entered = false; }
        void Hide(FalloutPlayerReferenceHudStep step, string tile)
        { Changed(step); Components.WriteComponentFloat(tile, "visible", 0); }
        void Changed(FalloutPlayerReferenceHudStep step) => _call = _call! with { Step = step, Changed = Next() };
    }
    private long Next() => _sequence = checked(_sequence + 1);
    internal void RequireCurrent()
    {
        if (_retired || _entered || Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("HUD source child changed its living menu/thread or reentered the original writer.");
    }
    internal void Retire() { if (_retired) return; RequireCurrent(); _retired = true; }
}
