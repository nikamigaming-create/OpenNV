using System.Diagnostics;

namespace OpenNV.Runtime.Diagnostics;

// Main-thread inclusive owner timings. Nested owners are not additive.
internal static class RuntimeFrameWork
{
    internal enum Owner { Opening, QuestScripts, ReferenceEvents, ReferencePresentation, ObjectAnimation, Particle, AnimationAudio, NpcAnimation, NpcPackages, CreatureAnimation, CreaturePackages, Combat, PlayerPhysics, PlayerPresentation, NativeCapsuleSweep, NativeSupportRay, ActorMove }
    private static readonly long[] Calls = new long[Enum.GetValues<Owner>().Length];
    private static readonly long[] Ticks = new long[Calls.Length];
    private static readonly long[] Maximum = new long[Calls.Length];
    internal static Scope Measure(Owner owner) => new(owner);
    internal static object Capture() => Enum.GetValues<Owner>().Select(owner => new
    {
        owner = owner.ToString(),
        calls = Calls[(int)owner],
        totalMilliseconds = Ticks[(int)owner] * (1000.0 / Stopwatch.Frequency),
        maximumMilliseconds = Maximum[(int)owner] * (1000.0 / Stopwatch.Frequency)
    }).ToArray();

    internal readonly struct Scope(Owner owner) : IDisposable
    {
        private readonly long _started = Stopwatch.GetTimestamp();
        public void Dispose()
        {
            var elapsed = Stopwatch.GetTimestamp() - _started;
            var index = (int)owner;
            ++Calls[index]; Ticks[index] += elapsed; Maximum[index] = Math.Max(Maximum[index], elapsed);
        }
    }
}
