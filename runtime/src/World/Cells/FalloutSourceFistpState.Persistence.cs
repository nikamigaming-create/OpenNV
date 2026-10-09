using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutSourceFistpState
{
    internal FalloutSourceFistpSnapshot Capture()
    {
        RequireIdle();
        if (_host is not null) _host.RequireCurrent(_lastBinding!);
        return new(Schema, _source.Contract, _source.ExecutableSha256, _stack, _process, _sequence,
            _pairs, _conversions, _lastBinding, _host is not null, _last, _cold);
    }
    private void Restore(FalloutSourceFistpSnapshot saved)
    {
        Validate(saved);
        if (saved.Contract != _source.Contract || saved.ExecutableSha256 != _source.ExecutableSha256 || saved.Stack != _stack || saved.CapturedProcess == _process)
            throw new InvalidDataException("Cold FISTP lost its exact selected source or actual new process epoch.");
        _sequence = saved.Sequence; _pairs = saved.Pairs; _conversions = saved.Conversions;
        _last = saved.Last; _lastBinding = saved.LastBinding;
        _cold = new(saved.CapturedProcess, _process, Next());
        // Saved CW/SW, thread IDs and instruction receipts are history only.
        // No native thread is constructed, no control word is written, and no
        // consumed conversion is replayed during restore. Bind needs a fresh
        // actual provider before the next living original invocation.
    }
    internal static void Validate(FalloutSourceFistpSnapshot saved)
    {
        if (saved is null || saved.Schema != Schema || saved.CapturedProcess == Guid.Empty || saved.Sequence < 0 || saved.Pairs < 0 ||
            saved.Conversions < 0 || saved.Conversions > saved.Pairs * 2m || string.IsNullOrWhiteSpace(saved.Stack) ||
            (saved.Pairs == 0) != (saved.Last is null) || saved.Bound && saved.LastBinding is null)
            throw new InvalidDataException("Current FISTP omitted its source/current counters, native binding or actual last prefix.");
        var source = FalloutMainFrameDeclaration.ForExecutable(saved.ExecutableSha256);
        if (source.Contract != saved.Contract) throw new InvalidDataException("Saved FISTP changed its selected original Main declaration.");
        if (saved.LastBinding is { } binding) FalloutFistp32Abi.RequireBinding(binding);
        if (saved.Last is { } pair)
        {
            ValidateContext(pair.Context);
            if (pair.Context.Process != saved.CapturedProcess && saved.ColdHandoff is null || pair.Entered < 1 || pair.Changed < pair.Entered ||
                pair.Changed > saved.Sequence || pair.Operands is null || pair.Operands.Count > 2 || pair.Returned == (pair.Failure is not null) ||
                pair.Failure is not null && string.IsNullOrWhiteSpace(pair.Failure) || pair.Returned && pair.Operands.Count != 2)
                throw new InvalidDataException("Saved FISTP lost its real invocation or ordered returned/failed operand prefix.");
            if (pair.Host is { } host) FalloutFistp32Abi.RequireBinding(host);
            if (pair.Operands.Count != 0 && pair.Host is null) throw new InvalidDataException("A FISTP operand has no actual native provider construction.");
            var previous = pair.Entered; var complete = 0;
            for (var index = 0; index < pair.Operands.Count; index++)
            {
                var operand = pair.Operands[index];
                if (operand is null || operand.Entered <= previous || operand.Entered > pair.Changed || operand.Returned < operand.Entered || operand.Returned > pair.Changed ||
                    (operand.Integer is null) != (operand.Returned is null) || (operand.Grid is null) != (operand.Returned is null) ||
                    operand.Failure is not null && (string.IsNullOrWhiteSpace(operand.Failure) || operand.Returned is not null) ||
                    complete != index)
                    throw new InvalidDataException("Saved FISTP invented or reordered an integer/grid return across a failed operand.");
                if (operand.Returned is { } returned)
                {
                    var actual = operand.Native ?? throw new InvalidDataException("A returned FISTP integer has no actual native instruction observation.");
                    FalloutFistp32Abi.RequireHeader(actual, FalloutSourceMainFamily.GridOperation(saved.ExecutableSha256), operand.Bits);
                    if (actual.NativeProcess != pair.Host!.Construction.NativeProcess || actual.NativeThread != pair.Host.Construction.NativeThread)
                        throw new InvalidDataException("Saved FISTP crossed its actual native thread/process lease.");
                    var expected = FalloutFistp32Semantics.RequireConverted(actual);
                    if (operand.Integer != expected.Integer || operand.Grid != expected.Integer >> 12 || operand.Failure is not null)
                        throw new InvalidDataException("Saved FISTP changed the observed integer or signed shift-twelve result.");
                    previous = returned; complete++;
                }
                else
                {
                    if (operand.Failure is null || !ReferenceEquals(operand, pair.Operands[^1]))
                        throw new InvalidDataException("Saved FISTP hides an entered/unreturned native operand.");
                    previous = operand.Entered;
                }
            }
            if (pair.Returned && complete != 2 || complete > saved.Conversions)
                throw new InvalidDataException("Saved FISTP completion does not match the actual two instruction returns.");
        }
        if (saved.ColdHandoff is { } cold && (cold.PreviousProcess == Guid.Empty || cold.CurrentProcess != saved.CapturedProcess ||
            cold.PreviousProcess == cold.CurrentProcess || cold.Sequence < 1 || cold.Sequence > saved.Sequence))
            throw new InvalidDataException("Saved FISTP lost its actual source-process cold handoff.");
    }
    private static void ValidateContext(FalloutSourceFistpContext context)
    {
        if (context is null || context.Process == Guid.Empty || context.MainInvocation == Guid.Empty || context.MainOrdinal < 1 || !Enum.IsDefined(context.Site) ||
            context.Child != (context.Site switch
            {
                FalloutSourceFistpSite.PlayerContainment => FalloutMainPlayerCellStep.ContainmentQuery,
                FalloutSourceFistpSite.PlayerTargetCell => FalloutMainPlayerCellStep.TargetCellRead,
                FalloutSourceFistpSite.PlayerPendingWorldspace => FalloutMainPlayerCellStep.PendingDestination,
                _ => throw new InvalidDataException("FISTP has no owned original consumer site.")
            }) || (context.Site == FalloutSourceFistpSite.PlayerPendingWorldspace) != (context.PendingRequest is not null) || context.PendingRequest == Guid.Empty)
            throw new InvalidDataException("Calling-thread FISTP has no exact source Main/Player child/request context.");
    }
}
