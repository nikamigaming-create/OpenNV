using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

// This is an explicit capability boundary, not an approximation of the source
// flag. Peaceful movement never asks for a combat decision.
internal sealed class FalloutFollowCombatBoundary
{
    private FalloutFormKey? _package;
    internal string? Error { get; private set; }

    internal void Require(FalloutFollowPackage? source, FalloutReferenceInstance state, string reached)
    {
        if (source is not { RequiresDefensiveCombatOwner: true }) return;
        Refuse(source, state, reached, "DefensiveCombat requires its native combat-policy owner");
    }

    internal void RequireContinuation(FalloutFollowPackage? source, FalloutReferenceInstance state, string reached)
    {
        Require(source, state, reached);
        if (source is { Accompany: false } && (source.Flags & 0x04000000) != 0)
            Refuse(source, state, reached, "Follow during combat requires its concurrent destination movement and firing owner");
    }

    private void Refuse(FalloutFollowPackage source, FalloutReferenceInstance state, string reached, string reason)
    {
        if (_package is { } owner && owner != source.Form)
            throw new InvalidOperationException("A previous Follow combat-policy owner has not retired.");
        _package = source.Form;
        Error ??= $"Follow package {source.Form} reached {reached}; {reason}.";
        state.ProcedureCaptureBlocker = Error;
        throw new NotSupportedException(Error);
    }

    internal string? Retire(FalloutFormKey package, FalloutReferenceInstance state)
    {
        if (_package != package) return null;
        var retired = Error;
        if (state.ProcedureCaptureBlocker == retired) state.ProcedureCaptureBlocker = null;
        _package = null;
        Error = null;
        return retired;
    }
}
