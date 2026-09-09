using System.Numerics;
using OpenNV.Runtime.Gameplay.Bots;

// Integrate the requested inputs into a separate simple camera. Check tracking
// of a moving target, a frame stall, loss of control and invalid observations.
var steering = new ReactiveSteering();
float yaw = 0, pitch = 0;
var moved = false;
for (var frame = 0; frame < 360; frame++)
{
    var targetYaw = frame < 180 ? 1.1f : -.8f;
    var forward = new Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch));
    var goal = new Vector3(MathF.Sin(targetYaw) * 5, .5f, MathF.Cos(targetYaw) * 5);
    var intent = steering.Step(Vector3.Zero, forward, goal, true, frame == 30 ? 1 : 1f / 60);
    if (MathF.Abs(intent.YawRadians) > .106f) throw new Exception("Frame stall caused an unbounded camera turn.");
    yaw += intent.YawRadians; pitch += intent.PitchRadians; moved |= intent.Forward;
    if (frame == 179 && MathF.Abs(yaw - targetYaw) > .015f) throw new Exception("First target did not converge.");
}
if (!moved || MathF.Abs(yaw + .8f) > .015f) throw new Exception("Moved target was not reacquired.");
steering.Reset();
var stopped = steering.Step(Vector3.Zero, Vector3.UnitZ, Vector3.UnitX, false, 1f / 60);
if (stopped.Forward || MathF.Abs(stopped.YawRadians) > .003f) throw new Exception("Reset retained movement or turn momentum.");
try { steering.Step(new(float.NaN, 0, 0), Vector3.UnitZ, Vector3.UnitX, true, .016f); throw new Exception("Invalid observation was accepted."); }
catch (ArgumentException) { }
Console.WriteLine("Reactive steering: moving-target convergence, bounded delayed frame, control reset and invalid observations PASS.");
ReferenceBotContracts.Run();
