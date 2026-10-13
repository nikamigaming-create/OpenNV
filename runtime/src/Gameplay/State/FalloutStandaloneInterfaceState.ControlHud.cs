using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutHudInterfaceWrite(Guid Observation, int OriginalArgument, long Changed,
    byte Before, byte Stored, bool Returned, string? Failure);
internal sealed record FalloutHudInterfaceFields(byte MovementOverride, bool Available, long Stores,
    FalloutHudInterfaceWrite? Call)
{
    internal static FalloutHudInterfaceFields Constructed => new(0, true, 0, null);
}

internal sealed partial class FalloutStandaloneInterfaceState
{
    private FalloutHudInterfaceFields _hudControl = FalloutHudInterfaceFields.Constructed;
    private Guid? _hudInputFactory;
    private bool _hudOverrideWriting;
    private long _hudOverrideReentry;
    private string? HudControlSaveBlocker => _hudOverrideWriting ? "source-Interface-HUD-override-writer-entered" :
        !_hudControl.Available ? "source-Interface-HUD-override-observed-writer-unbound" : _hudControl.Call?.Failure;
    internal void PublishHudControlInput(FalloutDirectInputState input)
    {
        RequireCurrent(native: false); ArgumentNullException.ThrowIfNull(input);
        var current = input.ReadManagerOwner();
        if (!ReferenceEquals(current, input) || current.Source != _stack || current.Factory == Guid.Empty ||
            _hudInputFactory is { } factory && factory != current.Factory ||
            !FalloutDirectInputState.ValidProvider(current.Provider, current.Window))
            throw new InvalidOperationException("Source HUD interface cannot publish from a foreign actual input owner.");
        _hudInputFactory = current.Factory; _thread = Environment.CurrentManagedThreadId;
    }
    internal bool ReadHudMovementOverride()
    {
        RequireCurrent(native: false);
        if (!_hudControl.Available) throw new NotSupportedException("source-Interface-HUD-override-observed-writer-unbound");
        if (_hudControl.Call?.Failure is { } failure) throw new NotSupportedException(failure);
        return _hudControl.MovementOverride != 0;
    }
    internal bool ReadHudActiveMenu(uint menu)
    {
        RequireCurrent(native: false);
        return menu >= 1001 && menu <= 1060 && _active[(int)(menu - 1001)] != 0;
    }
    internal void ApplyHudMovementOverride(Guid observation, int originalArgument, Action<byte> applyPlayerDisableMask)
    {
        RequireCurrent(native: false); ArgumentNullException.ThrowIfNull(applyPlayerDisableMask);
        RequireHudOverrideWriter();
        if (observation == Guid.Empty || _hudControl.Call?.Observation == observation || !_hudControl.Available || _hudControl.Call?.Failure is not null)
            throw new InvalidOperationException("Source HUD override replayed or crossed an unbound writer.");
        var stored = unchecked((byte)originalArgument); var before = _hudControl.MovementOverride;
        var call = new FalloutHudInterfaceWrite(observation, originalArgument, Next(), before, stored, false, null);
        _hudControl = new(stored, true, checked(_hudControl.Stores + 1), call);
        var fault = _hudOverrideReentry; _hudOverrideWriting = true;
        try
        {
            // Both source ingress arms store before the complete Player mask
            // child, including its zero argument. Equality does not skip it.
            applyPlayerDisableMask(stored); RequireCurrent(native: false);
            if (_hudOverrideReentry != fault)
                throw new InvalidOperationException("Source HUD override swallowed a reentered mandatory writer.");
            _hudControl = _hudControl with { Call = call with { Changed = Next(), Returned = true } };
        }
        catch (Exception error)
        {
            _hudControl = _hudControl with { Call = call with { Changed = Next(), Failure = error.Message } }; throw;
        }
        finally { _hudOverrideWriting = false; }
    }
    internal void ObserveHudMovementOverride(Guid observation, int originalArgument)
    {
        RequireCurrent(native: false);
        RequireHudOverrideWriter();
        if (observation == Guid.Empty || _hudControl.Call?.Observation == observation || !_hudControl.Available || _hudControl.Call?.Failure is not null)
            throw new InvalidOperationException("Source HUD override omitted or replayed its observed producer.");
        _hudControl = _hudControl with
        {
            Available = false,
            Call = new(observation, originalArgument, Next(),
            _hudControl.MovementOverride, _hudControl.MovementOverride, false, "source-Interface-HUD-override-observed-writer-unbound")
        };
    }
    private void RequireHudOverrideWriter()
    {
        if (_hudOverrideWriting)
        {
            ++_hudOverrideReentry;
            throw new InvalidOperationException("Source HUD override reentered its actual Player mask child.");
        }
    }
    private static void ValidateHudControl(FalloutHudInterfaceFields? fields)
    {
        if (fields is null) throw new InvalidDataException("Source interface omitted its independent HUD override constructor.");
        if (fields.Stores < 0 || fields.Call is null && fields != FalloutHudInterfaceFields.Constructed ||
            fields.Call is { } call && (call.Observation == Guid.Empty || call.Changed < 1 ||
                call.Returned && (call.Failure is not null || !fields.Available) ||
                fields.Available && (fields.Stores < 1 || call.Stored != unchecked((byte)call.OriginalArgument)) ||
                call.Stored != fields.MovementOverride || !fields.Available && call.Failure != "source-Interface-HUD-override-observed-writer-unbound"))
            throw new InvalidDataException("Source HUD override lost its constructor, exact argument or pre-child store.");
    }
}
