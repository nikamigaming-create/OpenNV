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
// An attack's authored reentry point is independent of its visual end.
var refireClock = new FalloutWeaponAnimationTimeline([new(0, "start"), new(.1f, "Hit"),
    new(.4f, "a:5"), new(1, "a:3"), new(2, "end")], 0, 2, 1);
if (refireClock.TryRefire(.399, out _) || !refireClock.TryRefire(.4f, out var nextAttack) || nextAttack != "attack5" ||
    !refireClock.IsRefireMarker("a:5") || refireClock.IsRefireMarker("a:3") ||
    refireClock.Crossed(0, .4, true, false).Count(key => FalloutWeaponAnimationTimeline.DischargesWeapon(key.Text)) != 1)
    throw new InvalidOperationException("Semi-automatic refire did not retain first-marker selection and the committed discharge prefix.");
var tailRefire = new FalloutWeaponAnimationTimeline([new(0, "start"), new(.1f, "Hit"), new(1.8f, "a:R"), new(2, "end")], 0, 2, 1);
if (tailRefire.TryRefire(1.5, out _) || !tailRefire.TryRefire(MathF.BitIncrement(1.5f), out var fallbackAttack) || fallbackAttack is not null)
    throw new InvalidOperationException("Source attack tail threshold lost its strict boundary or authored-weapon fallback.");
var sourceOrderRefire = new FalloutWeaponAnimationTimeline([new(0, "start"), new(.1f, "Hit"),
    new(.8f, "a:4"), new(.4f, "a:3"), new(2, "end")], 0, 2, 1);
if (sourceOrderRefire.TryRefire(.5, out _) || !sourceOrderRefire.TryRefire(.8f, out var firstSourceGroup) || firstSourceGroup != "attack4")
    throw new InvalidOperationException("Attack reentry reordered authored text-key declarations.");
var revolverClock = new FalloutWeaponAnimationTimeline([new(0, "start"), new(.0333333f, "Hit"),
    new(.19999981f, "Sound: WPN357RevolverCock"), new(.39999986f, "a:5"), new(.6666665f, "end")], 0, .6666665f, 1);
if (!revolverClock.TryRefire(.82 * .699999988079071, out var revolverNext) || revolverNext != "attack5" ||
    .82 * .699999988079071 >= revolverClock.Duration)
    throw new InvalidOperationException("Owned revolver timing still waits for the entire visual clip before refiring.");
if (weaponClock.TryRefire(1, out _))
    throw new InvalidOperationException("Unqualified missing-marker replacement was admitted.");

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
