using System.Collections;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

internal static partial class NativeNavigationContracts
{
    private sealed class PendingPursuitQuery : IEnumerator<int>
    {
        internal int Disposals { get; private set; }
        public int Current => 0;
        object IEnumerator.Current => Current;
        public bool MoveNext() => throw new InvalidOperationException("Goal retirement advanced obsolete navigation work.");
        public void Reset() => throw new NotSupportedException();
        public void Dispose() => Disposals++;
    }

    private static void CheckPursuitQueryRetarget()
    {
        var owner = new FalloutFormKey("Pursuit.esm", 0x800);
        var enemy = new FalloutFormKey("Pursuit.esm", 0x801);
        var goal = new NativePursuitQueryGoal(Vector3.Zero, .75f, null, null, enemy);
        var stable = new PendingPursuitQuery();
        foreach (var target in new[] { Vector3.Zero, new Vector3(.1f, 0, 0), new Vector3(.3f, 0, 0) })
            if (goal.RetireIfObsolete(stable, goal with { Target = target }, .3f) || stable.Disposals != 0)
                throw new InvalidOperationException("Pending pursuit lost its query within the existing actor-radius reuse threshold.");
        foreach (var changed in new[]
        {
            goal with { Target = new(2.5f, 0, 0) },
            goal with { Target = new(0, .31f, 0) },
            goal with { Radius = .5f },
            goal with { CombatTarget = new FalloutFormKey("Pursuit.esm", 0x802) },
            goal with { Package = owner, CombatTarget = null },
        })
        {
            var pending = new PendingPursuitQuery();
            if (!goal.RetireIfObsolete(pending, changed, .3f) || pending.Disposals != 1)
                throw new InvalidOperationException("Obsolete pending pursuit retained its captured goal or navigation lease.");
        }
        var package = goal with { Package = owner, CombatTarget = null, SourceSha256 = new string('a', 64) };
        var sameSource = new PendingPursuitQuery();
        if (package.RetireIfObsolete(sameSource, package with { SourceSha256 = new string('A', 64) }, .3f))
            throw new InvalidOperationException("Identical package source identity retired its query by hash casing.");
        var changedSource = new PendingPursuitQuery();
        if (!package.RetireIfObsolete(changedSource, package with { SourceSha256 = new string('b', 64) }, .3f) || changedSource.Disposals != 1)
            throw new InvalidOperationException("Package query survived replacement of its source navigation graph.");
        var invalid = new PendingPursuitQuery();
        try { goal.RetireIfObsolete(invalid, goal with { Target = new(float.NaN, 0, 0) }, .3f); }
        catch (ArgumentOutOfRangeException)
        {
            if (invalid.Disposals != 0) throw new InvalidOperationException("Invalid source goal disposed the current query before refusal.");
            GD.Print("OPENNV_PURSUIT_QUERY_RETARGET staleMovedGoal=true ownerAndRadius=true sourceGraph=true nearMotionRetained=true noAdvance=true invalidAtomic=true");
            return;
        }
        throw new InvalidOperationException("Nonfinite pursuit goal entered navigation reuse.");
    }
}
