using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal static class NativeActorAmbientIdleAdmission
{
    internal static bool Advance(FalloutAmbientIdleState state, FalloutPluginStack records, Node3D actor,
        RuntimeNativePlayer? player, Func<float> posedRadiusMeters, float units, double seconds, bool ready)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || seconds > float.MaxValue)
            throw new InvalidDataException("Automatic idle frame interval is invalid.");
        if (!ready || player is null || !actor.IsInsideTree()) return false;
        var radius = posedRadiusMeters();
        var distance = records.NumericSettings.Float("fAIIdleAnimationDistance");
        if (!float.IsFinite(radius) || radius < 0 || !float.IsFinite(distance) || !float.IsFinite(units) || units <= 0)
            throw new InvalidDataException("Automatic idle distance has no posed source bound.");
        // World radius already carries model/reference scale and metres. The
        // native radius/64 factor consequently cancels source unit conversion.
        var inRange = actor.GlobalPosition.DistanceTo(player.GlobalPosition) <= distance * radius / 64;
        return state.Advance((float)seconds, records.NumericSettings.Float("fAIIdleWaitTime"), inRange);
    }
}
