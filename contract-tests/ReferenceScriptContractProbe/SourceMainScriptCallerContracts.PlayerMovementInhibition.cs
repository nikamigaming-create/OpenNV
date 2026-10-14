using System.Runtime.CompilerServices;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class SourceMainScriptCallerContracts
{
    // Authored component scope supplies a distinct actual call nonce. It is
    // never installed as a product Main/scene producer or an input substitute.
    private static readonly ConditionalWeakTable<FalloutPlayerMovementControlState, AuthoredMovementScope> MovementScopes = new();
    private sealed class AuthoredMovementScope
    {
        internal FalloutPlayerSourceCaller? Current;
        internal Action<Guid, long>? RequireActualMain;
        private readonly int _thread = Environment.CurrentManagedThreadId;
        internal void Require(FalloutPlayerInhibitionConsumer consumer, FalloutPlayerSourceCaller caller)
        {
            if (consumer != FalloutPlayerInhibitionConsumer.Movement || Current != caller || _thread != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Authored movement inhibition changed its entered source component scope.");
            RequireActualMain?.Invoke(caller.Main, caller.Ordinal);
        }
    }
    private static FalloutPlayerMovementControlState AuthoredMovementControl(FalloutPlayerMovementControlSource source, string stack,
        Guid process, FalloutPlayerMovementControlSnapshot? saved = null, FalloutPlayerControlFieldsState? fields = null)
    {
        var scope = new AuthoredMovementScope();
        var inhibition = new FalloutPlayerInhibitionState(FalloutPlayerFollowingContinuationSource.Reviewed(source), stack,
            process, Guid.NewGuid(), scope.Require, saved?.InhibitionOwner);
        var controls = new FalloutPlayerMovementControlState(source, stack, process, saved, fields, inhibition);
        MovementScopes.Add(controls, scope); return controls;
    }
    private static void RequireAuthoredMovementMain(FalloutPlayerMovementControlState controls, Action<Guid, long> require)
    { MovementScopes.GetValue(controls, _ => throw new InvalidOperationException("Missing authored movement scope.")).RequireActualMain = require; }
    private static bool ExecuteAuthoredMovement(FalloutPlayerMovementControlState controls, Guid main, long ordinal,
        uint argument, ushort mover, FalloutDirectInputState input)
    {
        var scope = MovementScopes.GetValue(controls, _ => throw new InvalidOperationException("Missing authored movement scope."));
        if (scope.Current is not null) throw new InvalidOperationException("Authored movement scope reentered its source caller.");
        var caller = new FalloutPlayerSourceCaller(main, ordinal, Guid.NewGuid()); scope.Current = caller;
        try { return controls.ExecutePrefix(main, ordinal, argument, mover, input, invocation: caller.Invocation); }
        finally { scope.Current = null; }
    }
}
