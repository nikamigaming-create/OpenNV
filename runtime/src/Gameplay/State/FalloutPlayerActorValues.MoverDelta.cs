using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerActorValues
{
    internal int ReadMoverDeltaIgnoreCrippling()
    {
        _abilityLifecycle?.Invoke();
        _ = Value(72);
        if (!_evaluating.Add(72)) throw new NotSupportedException("Player ability conditions have a recursive actor value dependency.");
        try
        {
            if (_moverSource is null || ReadMoverBase(72) != 0)
                throw new NotSupportedException("source-Player-speed-current-integer-base-72-nondefault-override-unbound");
            // The selected default NPC base leaf returns integer zero. The Player
            // integer virtual separately truncates each real Float32 modifier pool
            // then performs wrapping integer additions, rather than truncating a
            // previously summed float getter.
            return unchecked(Truncate(Pool(72, FalloutActorValuePool.Temporary)) +
                Truncate(Pool(72, FalloutActorValuePool.Permanent)) + Truncate(Pool(72, FalloutActorValuePool.Damage)));
        }
        finally { _evaluating.Remove(72); }
    }
    private static int Truncate(float value) => !float.IsFinite(value) || value >= 2147483648f || value < -2147483648f ?
        int.MinValue : (int)value;
}
