using Godot;
using OpenNV.Runtime.World.Cells;

public partial class NativeLocomotionAudit
{
    private async Task CheckNavigationScheduling()
    {
        var baseline = NativeCapsuleNavigation.RegisteredSearches;
        var scene = new Node3D(); AddChild(scene);
        var floor = new StaticBody3D { Position = new(0, -.25f, 1) };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(12, .5f, 14) } });
        scene.AddChild(floor);
        CharacterBody3D Body(float x)
        {
            var body = new CharacterBody3D
            {
                Position = new(x, .002f, -2),
                SafeMargin = .001f,
                FloorMaxAngle = FloorAngle,
                FloorSnapLength = .32f
            };
            body.AddChild(new CollisionShape3D
            {
                Position = new(0, .9f, 0),
                Shape = new CapsuleShape3D { Radius = .32f, Height = 1.8f }
            });
            scene.AddChild(body); return body;
        }
        var first = Body(-1); var second = Body(1);
        IEnumerator<IReadOnlyList<Vector3>?>? a = null, b = null, retired = null;
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var targetA = new Vector3(-1, 0, 4); var targetB = new Vector3(1, 0, 4);
            a = NativeCapsuleNavigation.Search(first, first.GlobalPosition, targetA, .4f, .3f,
                _ => true, 512, corridor: [targetA]).GetEnumerator();
            b = NativeCapsuleNavigation.Search(second, second.GlobalPosition, targetB, .4f, .3f,
                _ => true, 512, corridor: [targetB]).GetEnumerator();
            IReadOnlyList<Vector3>? pathA = null, pathB = null;
            for (var frame = 0; frame < 240 && (pathA is null || pathB is null); frame++)
            {
                void Advance(IEnumerator<IReadOnlyList<Vector3>?> search, ref IReadOnlyList<Vector3>? result)
                {
                    if (result is not null) return;
                    var before = NativeCapsuleNavigation.Work(search)!.IteratorSteps;
                    if (NativeCapsuleNavigation.Advance(search, out var path)) result = path;
                    if (NativeCapsuleNavigation.Work(search)!.IteratorSteps - before > NativeNavigationWorkSchedule.MaximumIteratorSteps)
                        throw new InvalidOperationException("Scheduled native query exceeded its iterator-step slice bound.");
                }
                Advance(a, ref pathA); Advance(b, ref pathB);
                if (pathA is null || pathB is null) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            var workA = NativeCapsuleNavigation.Work(a)!; var workB = NativeCapsuleNavigation.Work(b)!;
            if (pathA is null || pathB is null || pathA[^1].DistanceTo(targetA) > .02f || pathB[^1].DistanceTo(targetB) > .02f ||
                workA.State != "completed" || workB.State != "completed" || workA.Grants == 0 || workB.Grants == 0 ||
                workA.GuidedSamples == 0 || workB.GuidedSamples == 0 || NativeCapsuleNavigation.RegisteredSearches != baseline)
                throw new InvalidOperationException("Two scheduled actual capsule queries lost progress, support or registration cleanup.");
            retired = NativeCapsuleNavigation.Search(first, first.GlobalPosition, targetA, .4f, .3f, _ => true).GetEnumerator();
            scene.RemoveChild(first);
            if (NativeCapsuleNavigation.Work(retired) is not { State: "owner-retired", IteratorSteps: 0 } ||
                NativeCapsuleNavigation.RegisteredSearches != baseline)
                throw new InvalidOperationException("Actual native body retirement left registered navigation work.");
            var refused = false;
            try { NativeCapsuleNavigation.Advance(retired, out _); }
            catch (InvalidOperationException) { refused = true; }
            if (!refused) throw new InvalidOperationException("Retired native query was admitted after its body left the source graph.");
            GD.Print($"OPENNV_NATIVE_NAVIGATION_SCHEDULING_PASS firstSteps={workA.IteratorSteps} secondSteps={workB.IteratorSteps} " +
                $"firstGrants={workA.Grants} secondGrants={workB.Grants} firstDeniedBudget={workA.DeniedBudget} " +
                $"secondDeniedBudget={workB.DeniedBudget} firstDeniedTurn={workA.DeniedTurn} secondDeniedTurn={workB.DeniedTurn} " +
                "nativeBodyRetirement=true collisionAdmission=unchanged gameplay=separate parity=unverified");
        }
        finally
        {
            a?.Dispose(); b?.Dispose(); retired?.Dispose();
            if (GodotObject.IsInstanceValid(first) && !first.IsInsideTree()) first.Free();
            scene.Free();
        }
    }
}
