using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutHeldPlayerMenuFirstContext(FalloutPackageCommandFieldsSource Source, string Stack,
    Guid CommonProcess, Guid Fields, Guid Constructor, FalloutCombatActorIdentity Actor, long Epoch,
    Guid Main, long MainOrdinal, Guid? Context);
internal sealed record FalloutHeldPlayerMenuCall(Guid Invocation, Guid Main, long MainOrdinal, Guid RuntimeProcess,
    string Source, int Thread, FalloutProcessSceneQuery? Common, FalloutHeldPlayerMenuFirstContext? First,
    string Step, bool Returned, string? Failure);

// The source held child starts with these two independent pointer reads. Its
// null return leaves animation groups and their timers unchanged. A nonnull
// process/first pair reaches real animation consumers and remains explicit.
internal sealed class FalloutSourceHeldPlayerMenu
{
    private readonly FalloutStandaloneMenuLifecycleSource _source;
    private readonly Action<FalloutMainPlayerCellInvocation> _requireActual;
    private readonly Func<FalloutProcessSceneQuery> _common;
    private readonly Func<FalloutMainPlayerCellInvocation, FalloutHeldPlayerMenuFirstContext> _first;
    private bool _entered;
    private long _reentry;
    internal FalloutHeldPlayerMenuCall? LastCall { get; private set; }

    internal FalloutSourceHeldPlayerMenu(FalloutStandaloneMenuLifecycleSource source,
        Action<FalloutMainPlayerCellInvocation> requireActual, Func<FalloutProcessSceneQuery> currentCommon,
        Func<FalloutMainPlayerCellInvocation, FalloutHeldPlayerMenuFirstContext> currentFirst)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(requireActual); ArgumentNullException.ThrowIfNull(currentCommon);
        ArgumentNullException.ThrowIfNull(currentFirst); _source = source; _requireActual = requireActual;
        _common = currentCommon; _first = currentFirst;
    }
    internal FalloutHeldPlayerMenuCall Execute(FalloutMainPlayerCellInvocation invocation)
    {
        if (_entered) { ++_reentry; throw new InvalidOperationException("Held Player menu child reentered its actual source invocation."); }
        invocation.Require(FalloutMainPlayerCellStep.HeldChild); _requireActual(invocation);
        var faults = _reentry; _entered = true;
        LastCall = new(Guid.NewGuid(), invocation.Main.Identity, invocation.Main.Ordinal, invocation.Main.Process,
            _source.Identity, Environment.CurrentManagedThreadId, null, null, "common-animation-getter", false, null);
        try
        {
            var common = _common(); Check();
            if (common.Process == Guid.Empty || common.Stack != invocation.Owner.StandaloneInterface.Stack ||
                common.ExecutableSha256 != _source.Interface.Main.EngineSha256 || !common.Actor.EnginePlayer ||
                common.Epoch < 1 || common.GameplayOwnership == Guid.Empty)
                throw new InvalidDataException("Held Player menu query changed its actual current Common process/Actor.");
            LastCall = LastCall with { Common = common, Step = "first-person-animation-field" };
            var first = _first(invocation); Check();
            first.Source.Validate();
            if (first.Source.Controls.Main != _source.Interface.Main || first.Stack != common.Stack || first.CommonProcess != common.Process ||
                first.Fields == Guid.Empty || first.Constructor == Guid.Empty || first.Actor != common.Actor || first.Epoch != common.Epoch ||
                first.Main != invocation.Main.Identity || first.MainOrdinal != invocation.Main.Ordinal)
                throw new InvalidDataException("Held menu pointer read borrowed another current Player animation field owner.");
            LastCall = LastCall with { First = first };
            if (common.AnimationContext is not null && first.Context is not null)
            {
                LastCall = LastCall with { Step = "current-animation-eligibility-group-five-and-pause-consumers" };
                throw new NotSupportedException("source-held-Player-nonnull-animation-pair-current-group-and-timer-consumers-unbound");
            }
            Check(); return LastCall = LastCall with { Step = "null-context-return", Returned = true };
        }
        catch (Exception error) { LastCall = LastCall with { Failure = error.Message }; throw; }
        finally { _entered = false; }
        void Check()
        {
            invocation.Require(FalloutMainPlayerCellStep.HeldChild); _requireActual(invocation);
            if (LastCall is not { } current || current.Thread != Environment.CurrentManagedThreadId || _reentry != faults)
                throw new InvalidOperationException("Held source child swallowed reentry or changed its actual current thread.");
        }
    }
}
