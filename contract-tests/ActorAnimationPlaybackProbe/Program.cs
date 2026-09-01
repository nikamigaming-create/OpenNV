using Godot;
using OpenNV.Runtime.World.Actors;

if (ActorAnimationPlayback.LoopModeForCycleType(
        ActorAnimationPlayback.LoopCycleType) != Animation.LoopModeEnum.Linear ||
    ActorAnimationPlayback.LoopModeForCycleType(
        ActorAnimationPlayback.ClampCycleType) != Animation.LoopModeEnum.None)
    throw new InvalidOperationException("Source animation cycle mapping differs.");

var loop = ActorAnimationPlayback.AdvanceClock(
    positionSeconds: 1.75,
    deltaSeconds: 0.5,
    startSeconds: 0.0,
    stopSeconds: 2.0,
    ActorAnimationPlayback.LoopCycleType);
if (loop.Terminal || Math.Abs(loop.PositionSeconds - 0.25) > 0.000001)
    throw new InvalidOperationException("Source loop animation did not wrap exactly.");

var clamp = ActorAnimationPlayback.AdvanceClock(
    positionSeconds: 1.75,
    deltaSeconds: 0.5,
    startSeconds: 0.0,
    stopSeconds: 2.0,
    ActorAnimationPlayback.ClampCycleType);
if (!clamp.Terminal || Math.Abs(clamp.PositionSeconds - 2.0) > 0.000001)
    throw new InvalidOperationException("Source clamp animation did not stop exactly.");

var unsupportedRejected = false;
try
{
    ActorAnimationPlayback.LoopModeForCycleType(1);
}
catch (InvalidOperationException)
{
    unsupportedRejected = true;
}
if (!unsupportedRejected)
    throw new InvalidOperationException("Unsupported source animation cycle was accepted.");

Console.WriteLine("ACTOR_ANIMATION_PLAYBACK_PROBE_PASS loop=0.25 clamp=2 terminal=1");
