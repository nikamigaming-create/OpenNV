using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// A known result byte, not the physical presence of a retail extra allocation.
// Null snapshots retain unknown historical state; generic locks are independent.
internal sealed record FalloutTerminalHackState(string ReferenceSha256, byte RawState, string? Error = null)
{
    internal bool Hacked => (RawState & 0x80) != 0;
    internal int Failures => RawState & 0x7f;
    internal void Validate()
    {
        if (ReferenceSha256 is not { Length: 64 } || !ReferenceSha256.All(Uri.IsHexDigit) || Error is { Length: 0 })
            throw new InvalidDataException("Saved terminal hacking state has no source identity.");
    }
}

internal sealed partial class FalloutReferenceWorld
{
    private FalloutTerminalHackState? InitialTerminalHackState(FalloutReferenceInstance instance) =>
        !records.TryGetEffective(instance.Base, out var basis) || basis.Signature != "TERM" ? null :
            new(LockSourceHash(instance.Reference, AccessRecord(instance.Reference)), 0);

    internal FalloutTerminalHackState TerminalHackResult(FalloutFormKey reference)
    {
        var instance = Get(reference);
        var source = Access(reference);
        var terminal = TerminalAccess(reference) ??
            throw new InvalidDataException("Hacking result state requires a TERM reference.");
        _ = TerminalLock(terminal, source); // Keep reached declaration/override refusals.
        var state = instance.TerminalHackState ??
            throw new NotSupportedException("Saved terminal hacking history was not retained; no initial result is assumed.");
        state.Validate();
        if (!state.ReferenceSha256.Equals(LockSourceHash(reference, source), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Terminal hacking state differs from its winning source.");
        return state;
    }

    internal void SetTerminalHacked(FalloutFormKey reference, bool hacked)
    {
        var state = TerminalHackResult(reference);
        if (state.Error is not null) throw new InvalidOperationException("Terminal hacking retains its outcome failure: " + state.Error);
        Get(reference).TerminalHackState = state with
        {
            RawState = (byte)(hacked ? state.RawState | 0x80 : state.RawState & 0x7f)
        };
    }

    internal void SetTerminalHackFailure(FalloutFormKey reference, bool failed)
    {
        var state = TerminalHackResult(reference);
        if (state.Error is not null) throw new InvalidOperationException("Terminal hacking retains its outcome failure: " + state.Error);
        Get(reference).TerminalHackState = state with
        {
            RawState = failed ? unchecked((byte)(state.RawState + 1)) : (byte)0
        };
    }

    internal bool TerminalHackLockedOut(FalloutFormKey reference, Func<float>? ignoreLockedTerminal = null)
    {
        var failures = TerminalHackResult(reference).Failures;
        if (failures == 0) return false;
        if (failures != 1) return true;
        var value = (ignoreLockedTerminal ?? throw new NotSupportedException("Terminal lockout requires its actual perk entry owner."))();
        if (!float.IsFinite(value)) throw new InvalidDataException("Terminal lockout perk result is not finite.");
        return value == 0;
    }

    private void RestoreTerminalHackState(FalloutReferenceInstance instance, FalloutReferenceSnapshot snapshot)
    {
        // Missing historical evidence must replace the temporary fresh-instance
        // default, rather than silently turn into a known zero during restoration.
        instance.TerminalHackState = null;
        if (snapshot.TerminalHackState is not { } state) return;
        state.Validate();
        if (TerminalAccess(instance.Reference) is null ||
            !state.ReferenceSha256.Equals(LockSourceHash(instance.Reference, AccessRecord(instance.Reference)),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Saved terminal hacking state differs from its winning TERM/reference.");
        instance.TerminalHackState = state;
    }
}
