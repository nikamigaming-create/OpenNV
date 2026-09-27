using Godot;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.Formats.Gamebryo;

var weaponClock = new FalloutWeaponAnimationTimeline([new(0, "start"), new(.2f, "Fire"),
    new(.8f, "Loop"), new(1.6f, "end")], 0, 1.6f, 1);
var coarse = weaponClock.Crossed(0, 4, true, true).Select(key => (key.SourceOrdinal, key.Text)).ToArray();
var fine = Enumerable.Range(0, 240).SelectMany(frame => weaponClock.Crossed(frame / 60d, (frame + 1) / 60d, frame == 0, true))
    .Select(key => (key.SourceOrdinal, key.Text)).ToArray();
if (!coarse.SequenceEqual(fine) || coarse.Count(key => key.Text == "Fire") != 7 ||
    coarse.Any(key => key.Text == "end") || weaponClock.SampleSeconds(4, true) is < .2 or > .8)
    throw new InvalidOperationException("Weapon internal Fire/Loop cadence lost events, replayed windup or advanced into winddown.");
var released = weaponClock.SampleSeconds(4, true);
if (weaponClock.Crossed(released, weaponClock.Duration, false, false).Any(key => key.Text == "Fire") ||
    weaponClock.Crossed(released, weaponClock.Duration, false, false).Last().Text != "end")
    throw new InvalidOperationException("Automatic weapon release did not exit through its finite source tail.");
var throwClock = new FalloutWeaponAnimationTimeline([new(0, "start"), new(.5f, "Hold"), new(.8f, "Release"), new(1.5f, "end")], 0, 1.5f, 1);
if (throwClock.Hold != .5 || throwClock.Discharges != 1 ||
    throwClock.Crossed(0, .5, true, false).Any(key => FalloutWeaponAnimationTimeline.DischargesWeapon(key.Text)) ||
    throwClock.Crossed(.5, 1, false, false).Count(key => FalloutWeaponAnimationTimeline.DischargesWeapon(key.Text)) != 1)
    throw new InvalidOperationException("Throw Hold/Release event ownership differs.");

var sourceLoop = new FalloutWeaponAnimationTimeline([new(0, "start"), new(.2f, "end")], 0, .2f, 1, true, true);
if (!sourceLoop.UsesWeaponCadence || sourceLoop.Discharges != 1 ||
    sourceLoop.Crossed(0, .81, true).Count(key => FalloutWeaponAnimationTimeline.DischargesWeapon(key.Text)) != 5)
    throw new InvalidOperationException("Continuous automatic pose did not use its explicit WEAP cadence owner.");

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
