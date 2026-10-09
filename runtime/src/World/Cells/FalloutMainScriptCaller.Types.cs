using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutMainScriptCallerStep
{
    ClockPrelude, TabKey, AltKey, Prologue, MenuGateBefore, GuiModeBefore, FirstPredicateBefore,
    FirstSample, FinalPredicateBefore, ForeignMenu, ContextKind, KindThreePrelude, Player,
    SteamCallbacks, MainHold, TimedContexts, MenuGateAfter, GuiModeAfter, FirstPredicateAfter,
    SecondSample, FinalPredicateAfter, MenuBeforeStore, FirstBeforeStore, FinalBeforeStore,
    MenuAfterStore, FirstAfterStore, FinalAfterStore,
}
internal enum FalloutMainScriptCallerDisposition { Entered, ScopeReturned, InputSuppressed, Failed }
internal sealed record FalloutMainScriptChild(FalloutMainScriptCallerStep Step, long Entered, long? Returned,
    bool? Boolean, int? Integer, string? FailureType, string? Error);
internal sealed record FalloutMainScriptCall(long Ordinal, Guid Invocation, Guid SourceProcess, string Consumer,
    string DeliveryOwner, ulong DeliveryFrame, uint DeliveredSecondsBits, long Entered, long Changed,
    FalloutMainScriptCallerDisposition Disposition, IReadOnlyList<FalloutMainScriptChild> Children,
    string? FailureType, string? Error);
internal sealed record FalloutMainInterfaceCachedFields(bool MenuGate, bool FirstPredicate, bool FinalPredicate, long Changed);
internal sealed record FalloutMainContextTimeWrite(long Mutation, Guid Invocation, uint BeforeBits,
    uint AddedBits, uint AfterBits, long Changed);
internal sealed record FalloutMainScriptCallerSnapshot(string Schema, FalloutMainScriptCallerSource Source,
    string Stack, Guid CapturedProcess, long Changed, long Calls, uint ContextTimeBits, long ContextTimeWrites,
    FalloutMainContextTimeWrite? LastContextTimeWrite, FalloutMainScriptCall? LastCall,
    FalloutActorProcessRuntimeHandoff? ColdHandoff, FalloutMainInterfaceCachedFields CachedInterfaceFields);

// A token exists only while the same Main owner is actually invoking this
// child. The delivery frame is correlation, not a source-frame receipt.
internal sealed class FalloutMainScriptInvocation
{
    internal FalloutActorProcessRuntimeState Owner { get; }
    internal Guid Identity { get; }
    internal long Ordinal { get; }
    internal Guid Process { get; }
    internal FalloutMainScriptInvocation(FalloutActorProcessRuntimeState owner, Guid identity, long ordinal, Guid process)
    { Owner = owner; Identity = identity; Ordinal = ordinal; Process = process; }
    internal void Require(FalloutMainScriptCallerStep step) => Owner.RequireMainScriptChild(this, step);
    internal FalloutMainInterfaceCachedFields ReadCachedInterfaceFields() => Owner.ReadMainInterfaceCachedFields(this);
    internal void AdvanceContextTime(float actualSourceTimerDelta) => Owner.AdvanceMainContextTime(this, actualSourceTimerDelta);
}

internal interface IFalloutMainScriptCallerConsumers
{
    FalloutMainScriptCallerSource Source { get; }
    string Owner { get; }
    bool AsyncKeyHighBit(FalloutMainScriptInvocation invocation, int virtualKey);
    void Prologue(FalloutMainScriptInvocation invocation);
    bool MenuGate(FalloutMainScriptInvocation invocation);
    bool GuiModeTwo(FalloutMainScriptInvocation invocation);
    bool FirstInterfacePredicate(FalloutMainScriptInvocation invocation);
    bool FinalInterfacePredicate(FalloutMainScriptInvocation invocation);
    bool ForeignActiveMenu(FalloutMainScriptInvocation invocation);
    int InterfaceContextKind(FalloutMainScriptInvocation invocation);
    void KindThreePrelude(FalloutMainScriptInvocation invocation);
    void Player(FalloutMainScriptInvocation invocation);
    void SteamCallbacks(FalloutMainScriptInvocation invocation);
    bool MainHold(FalloutMainScriptInvocation invocation);
    void TimedContexts(FalloutMainScriptInvocation invocation, bool advanceTimer);
}
