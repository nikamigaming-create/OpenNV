using System.Reflection;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Diagnostics;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Actors;

public partial class NativeLocomotionAudit
{
    private readonly record struct StepQueryOutcome(bool Accepted, Vector3 Destination, string? Reason,
        NativeNavigationContact? Contact, long Sweeps, long SupportRays);

    private async Task CheckReusableStepQueries()
    {
        var scene = new Node3D(); AddChild(scene);
        StaticBody3D Box(Vector3 position, Vector3 size)
        {
            var box = new StaticBody3D { Position = position };
            box.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } }); scene.AddChild(box); return box;
        }
        Box(new(0, -.5f, -1), new(4, 1, 12));
        Box(new(0, .06f, -2), new(4, .12f, 6));
        var ceiling = Box(new(0, 2.18f, -1), new(4, .2f, 12));
        var body = CorridorBody(scene, new(0, .01f, 2));
        using var resources = new NativeCharacterStep.QueryResources(body);
        World3D? isolatedWorld = null;
        var calls = (long[])typeof(RuntimeFrameWork).GetField("Calls", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        StepQueryOutcome Query(Transform3D from, Vector3 motion, NativeCharacterStep.QueryResources? owner)
        {
            var sweeps = calls[(int)RuntimeFrameWork.Owner.NativeCapsuleSweep];
            var rays = calls[(int)RuntimeFrameWork.Owner.NativeSupportRay];
            var accepted = NativeCharacterStep.TryQuery(body, from, motion, .4f, out var destination,
                out var reason, out var contact, resources: owner);
            return new(accepted, destination, reason, contact,
                calls[(int)RuntimeFrameWork.Owner.NativeCapsuleSweep] - sweeps,
                calls[(int)RuntimeFrameWork.Owner.NativeSupportRay] - rays);
        }
        StepQueryOutcome Borrowed(Transform3D from, Vector3 motion, bool mismatch = false)
        {
            using var parameters = new PhysicsTestMotionParameters3D { From = from, Motion = motion, Margin = body.SafeMargin, MaxCollisions = 4 };
            using var result = new PhysicsTestMotionResult3D();
            if (!NativeCharacterStep.TestMotion(body.GetRid(), parameters, result))
                throw new InvalidDataException("Observed step fixture lacks an actual obstructed horizontal sweep.");
            if (mismatch) parameters.Margin += .002f;
            var sweeps = calls[(int)RuntimeFrameWork.Owner.NativeCapsuleSweep];
            var rays = calls[(int)RuntimeFrameWork.Owner.NativeSupportRay];
            var accepted = NativeCharacterStep.TryQueryAfterObstruction(body, from, motion, .4f, parameters, result,
                out var destination, out var reason, out var contact, resources);
            return new(accepted, destination, reason, contact,
                calls[(int)RuntimeFrameWork.Owner.NativeCapsuleSweep] - sweeps,
                calls[(int)RuntimeFrameWork.Owner.NativeSupportRay] - rays);
        }
        void SameObservedStep(Transform3D from, Vector3 motion)
        {
            var original = Query(from, motion, resources);
            var borrowed = Borrowed(from, motion);
            if (borrowed with { Sweeps = borrowed.Sweeps + 1 } != original || Borrowed(from, motion, mismatch: true) != original)
                throw new InvalidDataException("Borrowed obstruction changed step/support/contact or bypassed a mismatched margin query.");
        }
        StepQueryOutcome Pair(Transform3D from, Vector3 motion)
        {
            var fresh = Query(from, motion, null);
            var reused = Query(from, motion, resources);
            if (fresh != reused)
                throw new InvalidDataException($"Reusable native step query changed outcome/contact/queries: fresh={fresh}; reused={reused}.");
            return reused;
        }
        try
        {
            await SettleCorridorBody(body);
            var pose = body.GlobalTransform;
            CheckDeferredNavigationProbe(body);
            var from = new Transform3D(body.GlobalBasis, new(0, pose.Origin.Y, 1.35f));
            var motion = new Vector3(0, 0, -.16f);
            var supported = Pair(from, motion);
            SameObservedStep(from, motion);
            if (!supported.Accepted || supported.Sweeps < 3 || supported.SupportRays < 2 || supported.Contact is null)
                throw new InvalidDataException("Reusable step fixture did not exercise a supported riser, headroom and forward sweeps.");
            var parameters = resources.MotionParameters(body); var result = resources.MotionResult(body); var ray = resources.SupportRay(body);
            if (Pair(from.Translated(Vector3.Up * .34f), motion) is not { Accepted: false, Reason: "floor-support", Sweeps: 0, SupportRays: 1 } ||
                Pair(pose, Vector3.Back * .1f) is not { Accepted: false, Reason: "no-obstruction", Sweeps: 1 })
                throw new InvalidDataException("Reusable query admitted unsupported motion or changed the no-obstruction path.");
            ceiling.Position = new(0, 1.98f, -1);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            if (Pair(from, motion).Accepted) throw new InvalidDataException("Reusable query bypassed real low-ceiling clearance.");
            SameObservedStep(from, motion);
            GD.Print("OPENNV_OBSTRUCTED_STEP_REUSE_PASS supportedRiser=true lowCeilingRefused=true exactContactAndSupport=true oneSweepRemoved=true marginMismatchFreshQuery=true");
            ceiling.Position = new(0, 2.18f, -1);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var mask = body.CollisionMask; var margin = body.SafeMargin;
            body.CollisionMask = 0;
            if (Pair(from, motion) is not { Accepted: false, Reason: "floor-support" })
                throw new InvalidDataException("Reusable query retained a stale collision mask.");
            body.CollisionMask = mask;
            foreach (var safeMargin in new[] { .001f, .006f })
            {
                body.SafeMargin = safeMargin;
                _ = Pair(from, motion);
            }
            body.SafeMargin = margin;
            if (!ReferenceEquals(parameters, resources.MotionParameters(body)) || !ReferenceEquals(result, resources.MotionResult(body)) ||
                !ReferenceEquals(ray, resources.SupportRay(body)) || body.GlobalTransform != pose)
                throw new InvalidDataException("Reusable step resources were replaced or published a hypothetical body pose.");
            long Allocated(NativeCharacterStep.QueryResources? owner)
            {
                for (var index = 0; index < 128; index++) _ = Query(from, motion, owner);
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var index = 0; index < 256; index++) _ = Query(from, motion, owner);
                return GC.GetAllocatedBytesForCurrentThread() - before;
            }
            var freshBytes = Allocated(null); var reusedBytes = Allocated(resources);
            if (reusedBytes >= freshBytes)
                throw new InvalidDataException($"Reusable step wrappers did not reduce warmed managed allocations: fresh={freshBytes}, reused={reusedBytes}.");
            var other = CorridorBody(scene, new(1, .01f, 2));
            var mismatchRefused = false;
            try { _ = NativeCharacterStep.TrySupport(other, other.GlobalPosition, .4f, out _, resources); }
            catch (InvalidOperationException) { mismatchRefused = true; }
            if (!mismatchRefused) throw new InvalidDataException("Another body borrowed a planner's step query resources.");
            var offsetBody = new CharacterBody3D { Position = new(-1, .01f, 2), FloorSnapLength = .3f, FloorMaxAngle = FloorAngle };
            var envelope = NativeActorMovementEnvelope.Horizontal(new(0, 0, 35), new(30, 63, 35), 1, .0142875f)!;
            var offsetShape = new CollisionShape3D(); envelope.Capsules[0].Publish(offsetShape, 1);
            offsetBody.AddChild(offsetShape);
            NativeCharacterStep.SetRootContactOffset(offsetBody, envelope.RootContactOffset);
            scene.AddChild(offsetBody);
            await SettleCorridorBody(offsetBody);
            using (var offsetResources = new NativeCharacterStep.QueryResources(offsetBody))
            using (var edge = new NativeCapsuleNavigation.EdgeQuery(offsetBody, .4f, .3f, _ => true))
            {
                if (offsetBody.GlobalPosition.Y >= 0 ||
                    !NativeCharacterStep.TrySupport(offsetBody, offsetBody.GlobalPosition, .4f, out _, offsetResources) ||
                    !edge.ValidateSegment(offsetBody.GlobalPosition, offsetBody.GlobalPosition + Vector3.Back * .1f, _ => true, out _, out _))
                    throw new InvalidDataException("Source-offset controller lost its real floor when its settled root was below that floor.");
                offsetBody.CollisionMask = 0;
                if (NativeCharacterStep.TrySupport(offsetBody, offsetBody.GlobalPosition, .4f, out _, offsetResources))
                    throw new InvalidDataException("Source root contact offset fabricated missing floor support.");
            }
            isolatedWorld = new World3D();
            var isolated = new SubViewport { Size = new(16, 16), World3D = isolatedWorld }; scene.AddChild(isolated);
            var previousWorld = body.GetWorld3D().GetInstanceId();
            body.Reparent(isolated);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            if (body.GetWorld3D().GetInstanceId() == previousWorld ||
                Pair(body.GlobalTransform, motion) is not { Accepted: false, Reason: "floor-support", Sweeps: 0, SupportRays: 1 })
                throw new InvalidDataException("Reusable query retained floor support from the body's retired world.");
            resources.Dispose();
            var disposedRefused = false;
            try { _ = resources.SupportRay(body); }
            catch (ObjectDisposedException) { disposedRefused = true; }
            if (!disposedRefused || GodotObject.IsInstanceValid(parameters) || GodotObject.IsInstanceValid(result) || GodotObject.IsInstanceValid(ray))
                throw new InvalidDataException("Retired step resources retained query wrappers or allowed reuse.");
            GD.Print($"OPENNV_NATIVE_STEP_QUERY_REUSE_PASS exactOutcomes=true exactContact=true exactQueryCounts=true supportedStep=true " +
                $"sourceOffsetRootSupport=true unsupportedRefused=true lowCeilingRefused=true liveMaskMargin=true changedWorldRefused=true bodyMismatchRefused=true disposed=true " +
                $"warmedQueries=256 freshBytes={freshBytes} reusedBytes={reusedBytes} fixture=synthetic-native-component gameplay=separate");
        }
        finally { scene.Free(); isolatedWorld?.Dispose(); }
    }
    private static void CheckDeferredNavigationProbe(CharacterBody3D body)
    {
        using var parameters = new PhysicsTestMotionParameters3D
        {
            From = body.GlobalTransform.Translated(Vector3.Up * .2f),
            Motion = Vector3.Down * .8f,
            Margin = body.SafeMargin,
            MaxCollisions = 4,
        };
        using var hit = new PhysicsTestMotionResult3D();
        if (!NativeCharacterStep.TestMotion(body.GetRid(), parameters, hit) || hit.GetCollisionCount() == 0)
            throw new InvalidDataException("Probe allocation fixture has no real native downward contact.");
        var firstIdentity = new FalloutFormKey("Synthetic.esm", 1);
        var currentIdentity = firstIdentity;
        var lookups = 0;
        var refuseLookup = false;
        var probe = new NativeNavigationProbe(null, _ =>
        {
            ++lookups;
            if (refuseLookup) throw new InvalidOperationException("Synthetic source identity lookup refusal.");
            return currentIdentity;
        });
        var from = parameters.From.Origin; var desired = from + parameters.Motion;
        var count = hit.GetCollisionCount();
        void Observe() { probe.BeginEdge(); probe.ObserveDownward(hit, from, desired, true); }
        Observe();
        var first = probe.FirstDownwardSweep ?? throw new InvalidDataException("First native sweep was not retained.");
        for (var index = 0; index < 64; index++) Observe();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 256; index++) Observe();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (allocated != 0 || lookups != count * 321)
            throw new InvalidDataException($"Warmed unretained probe snapshots allocate or skip source resolution: bytes={allocated}, lookups={lookups}.");
        currentIdentity = new("Synthetic.esm", 2);
        // Overwrite the borrowed native result before reading the retained last
        // values; neither fields nor identities may be read lazily from it.
        parameters.From = body.GlobalTransform; parameters.Motion = Vector3.Up * .01f;
        _ = NativeCharacterStep.TestMotion(body.GetRid(), parameters, hit);
        var last = probe.LastDownwardSweep ?? throw new InvalidDataException("Last native sweep disappeared.");
        if (last.From != first.From || last.Desired != first.Desired || last.Travel != first.Travel ||
            last.Collided != first.Collided || !last.Contacts.SequenceEqual(first.Contacts) ||
            last.Contacts.Any(contact => contact.Reference != firstIdentity))
            throw new InvalidDataException("Deferred probe snapshot changed native values, lookup timing or frozen first receipt.");
        probe.RejectGuide(); probe.BeginEdge();
        if (probe.LastDownwardSweep is not null || !ReferenceEquals(probe.GuideDownwardSweep, last) ||
            !ReferenceEquals(probe.FirstDownwardSweep, first) || !last.Contacts.SequenceEqual(first.Contacts))
            throw new InvalidDataException("The next probe edge mutated a retained immutable receipt.");
        parameters.From = new(body.GlobalBasis, from); parameters.Motion = desired - from;
        if (!NativeCharacterStep.TestMotion(body.GetRid(), parameters, hit) || hit.GetCollisionCount() == 0)
            throw new InvalidDataException("Probe source-refusal fixture lost its actual native contact.");
        refuseLookup = true;
        var refused = false;
        try { Observe(); }
        catch (InvalidOperationException error) when (error.Message == "Synthetic source identity lookup refusal.") { refused = true; }
        if (!refused || probe.LastDownwardSweep is not null || !ReferenceEquals(probe.FirstDownwardSweep, first) ||
            !ReferenceEquals(probe.GuideDownwardSweep, last))
            throw new InvalidDataException("Source identity lookup failure was delayed, hidden or corrupted retained receipts.");
        GD.Print($"OPENNV_NAVIGATION_PROBE_REUSE_PASS nativeContact=true lookupTiming=true lookupFailurePropagated=true frozenReceipts=true borrowedResultOverwritten=true warmedObservations=256 allocatedBytes={allocated}");
    }

}
