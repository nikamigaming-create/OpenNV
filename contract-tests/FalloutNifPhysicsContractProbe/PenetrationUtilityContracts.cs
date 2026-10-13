using OpenNV.Runtime.World.Physics;

internal static class PenetrationUtilityContracts
{
    private static readonly FalloutPhysicalRotation Identity = new(V(1, 0, 0), V(0, 1, 0), V(0, 0, 1));

    internal static void Run()
    {
        SceneSelection();
        ProductionMathAndSampling();
        Registration();
        DetectorAndImpulse();
        ClockAndWorld();
        ColdAndFailure();
        Console.WriteLine("OPENNV_ACTOR_PENETRATION_UTILITY_OK currentModel=independent nonemptySystems=true " +
            "memberships=directional detector=ordered filterImpulse=invokedCallbacks " +
            "clock=signed64 sourceOrdering=injected cold=noRegistrationReplay canonicalPhysics=unverified gameplayParity=unverified");
    }

    private static void ProductionMathAndSampling()
    {
        var root = new Body("ordered-root") { Position = V(10, 20, 30), Rotation = new(V(0, 1, 0), V(-1, 0, 0), V(0, 0, 1)) };
        var first = new Body("ordered-first") { Position = V(-1, -2, 4) };
        var second = new Body("ordered-second")
        { Position = V(1, 2, 3), Rotation = new(V(0, 1, 0), V(-1, 0, 0), V(0, 0, 1)) };
        var parent = new Body("ordered-parent") { Position = V(10, 22, 30) };
        var firstConstraint = new Constraint("ordered-first-constraint", 7, first, parent);
        var secondConstraint = new Constraint("ordered-second-constraint", 7, second, parent);
        var parentConstraint = new Constraint("ordered-parent-constraint", 7, parent, root);
        root.Outgoing.Add(parentConstraint); parent.Incoming.Add(parentConstraint);
        parent.Outgoing.Add(firstConstraint); parent.Outgoing.Add(secondConstraint);
        first.Incoming.Add(firstConstraint); second.Incoming.Add(secondConstraint);
        var graph = FalloutPenetrationGraph.Validate([root, first, second, parent],
            [firstConstraint, secondConstraint, parentConstraint]) ?? throw new InvalidOperationException("Authored graph was deferred.");
        var ordered = FalloutPenetrationSourceMath.Order(graph);
        Require(ordered.Bodies.SequenceEqual([root, parent, second, first]) &&
            ordered.Constraints.SequenceEqual([parentConstraint, secondConstraint, firstConstraint]) &&
            ordered.Parents.SequenceEqual([-1, 0, 1, 1]),
            "Source lower-depth displacement lost its indirect equal-depth reversal or corresponding constraints.");
        Query? query = null;
        var backend = new FalloutBodyPenetrationBackend(construction => query = new Query(construction));
        var skeleton = FalloutPenetrationSkeleton.Construct(ordered, backend);
        Require(skeleton.Bones.Count == 6 && skeleton.Bones[1].ReferencePose.Translation == V(2, 0, 0) &&
            skeleton.Bones[4].Name == second.Name + " Nub" && skeleton.Bones[5].Name == first.Name + " Nub",
            "Derived relative physical pose, terminal body order or actual source suffix was changed.");
        var construction = new FalloutPenetrationSystemConstruction(root, ordered, skeleton, 0x1234,
            new(V(-10, -10, -10), V(30, 30, 30)), 0x1234001e);
        using var instance = backend.Construct(construction);
        Require(instance.BoneToBodyIndices.SequenceEqual([0, 1, 2, 3, -1, -1]),
            "Derived terminal endpoints acquired invented independent rigid bodies.");
        var pose = instance.SampleBonePositions(skeleton);
        Require(pose[2] == V(1, 2, 3) && pose[4] == V(1, 3, 3) && pose[5] == V(0, -2, 4),
            "Production sampling borrowed reference/visual positions or omitted current physical rotation.");
        second.Position = V(8, 9, 10);
        pose = instance.SampleBonePositions(skeleton);
        Require(pose[2] == V(8, 9, 10) && pose[4] == V(8, 10, 10),
            "Production sampling did not consume changed current body transforms.");
        Reject<InvalidOperationException>(() => Task.Run(() => instance.SampleBonePositions(skeleton)).GetAwaiter().GetResult());
        instance.Dispose();
        Require(query is { Disposed: true } && second.ReadTransform().Translation == V(8, 9, 10),
            "Physical system retirement leaked query ownership or destroyed borrowed Actor bodies.");
        Reject<ObjectDisposedException>(() => instance.SampleBonePositions(skeleton));
        var normalized = FalloutPenetrationSourceMath.Normalize(V(3, 4, 0));
        Require(normalized.LengthBits == B(5) && normalized.Direction ==
            V(FalloutPhysicalFloat32.Multiply(3, .2f), FalloutPhysicalFloat32.Multiply(4, .2f), 0),
            "Finite source normalization lost its square-root/store/reciprocal multiplication sequence.");
        Require(FalloutPenetrationSourceMath.Normalize(default) == new FalloutPenetrationNormalizedVector(0, default),
            "Zero-length source normalization did not retain its explicit zero reciprocal.");
        Reject<NotSupportedException>(() => FalloutPenetrationSourceMath.Normalize(new(0x7fc12345, 0, 0)));
    }

    private static void SceneSelection()
    {
        var fixture = new Fixture(); var graph = fixture.First;
        Require(FalloutPenetrationGraph.IsCandidateRoot(graph.Root) &&
            !FalloutPenetrationGraph.IsCandidateRoot(graph.Child), "Incoming/outgoing structural direction was conflated.");
        var contact = new Constraint("contact", 11, graph.Root, graph.Child);
        graph.Root.Incoming.Add(contact);
        Require(FalloutPenetrationGraph.IsCandidateRoot(graph.Root), "Actual incoming contact excluded a candidate.");
        graph.Root.Incoming.Add(graph.Constraint);
        Require(!FalloutPenetrationGraph.IsCandidateRoot(graph.Root), "Incoming structural parent was admitted as contact.");
        graph.Root.Incoming.Clear();
        var walk = FalloutPenetrationGraph.Walk([null, graph.Scene]).ToArray();
        Require(walk.Length == 2 && walk[0].Key == graph.Root.Key && walk[1].Key == graph.Child.Key,
            "Ordered occupied source slots or null child slots were lost.");
        var collected = FalloutPenetrationGraph.Collect(graph.Scene);
        Require(collected.Bodies.SequenceEqual([graph.Root, graph.Child]) && collected.Constraints.SequenceEqual([graph.Constraint]),
            "Connected collection changed wrapper/body or linked-constraint order.");
        graph.Child.Weapon = true;
        Require(FalloutPenetrationGraph.Collect(graph.Scene).Bodies.SequenceEqual([graph.Root]), "Unlinked weapon body was admitted.");
        graph.Child.Linked.Add(contact);
        Require(FalloutPenetrationGraph.Collect(graph.Scene).Bodies.SequenceEqual([graph.Root, graph.Child]),
            "Qualifying linked weapon body was omitted.");
        graph.Child.Linked.Add(new Constraint("breakable", 12, graph.Child, graph.Root));
        Require(FalloutPenetrationGraph.Collect(graph.Scene).Bodies.SequenceEqual([graph.Root]),
            "Type-12 exclusion retained body or earlier collected constraints.");
        Require(FalloutPenetrationGraph.Validate([graph.Root, graph.Child], []) is null,
            "Mismatched connected constraint count did not return the independent deferred result.");
        Reject<InvalidDataException>(() => FalloutPenetrationGraph.Validate([graph.Root, graph.Child],
            [new Constraint("absent", 7, new Body("foreign"), graph.Root)]));
        Reject<InvalidDataException>(() => FalloutPenetrationGraph.Validate([graph.Root, graph.Child],
            [new Constraint("cycle", 7, graph.Child, graph.Child)]));
    }

    private static void Registration()
    {
        var fixture = new Fixture(); using var utility = fixture.Utility();
        Require(utility.CollisionGroup is null && !utility.Registered,
            "Constructor invented written collision group or current-model registration.");
        utility.Update();
        Require(!utility.Registered && utility.CollisionGroup is null && utility.Capture().Phase == "zero-system-update-completed",
            "Actual constructor-empty Update invented registration or a written group.");
        fixture.Selected = new Model("empty", []);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            utility.Register();
            Require(utility.Registered && utility.RegistrationRetry && utility.SystemCount == 0 && utility.CollisionGroup == 0x1234,
                "Source empty registration lost retained retry/group before the sixth attempt.");
            utility.Update();
        }
        utility.Register();
        Require(!utility.RegistrationRetry && utility.Capture().RetryCount == 0,
            "Sixth empty registration did not clear retry and counter.");
        fixture.Selected = fixture.First.Model;
        utility.Register();
        Require(utility.SystemCount == 1 && fixture.Backend.Systems.Count == 1 && utility.SaveBlocker is null,
            "Nonempty current model did not construct an independent retained system.");
        Require(utility.Capture().Systems[0].Bones.Count == 3 && fixture.Backend.Systems[0].Construction.PhantomFilter == 0x1234001e,
            "Physical terminal skeleton or independent layer-30 phantom filter was lost.");
        utility.Register();
        Require(fixture.Backend.Systems.Count == 1 && !fixture.Backend.Systems[0].Disposed,
            "Matching current root/body count did not reuse the actual prior system.");
        var replacement = Graph.Create("second"); fixture.Selected = replacement.Model;
        utility.Register();
        Require(fixture.Backend.Systems.Count == 2 && fixture.Backend.Systems[0].Disposed && utility.SystemCount == 1,
            "Changed model failed to construct new or retire unmatched actual system.");
        var countFixture = new Fixture();
        countFixture.First.Root.Linked.Add(new Constraint("extra", 7, countFixture.First.Child, countFixture.First.Root));
        using var deferred = countFixture.Utility(); deferred.Register();
        Require(deferred.SystemCount == 0 && deferred.Capture().Deferred && !deferred.RegistrationRetry,
            "Invalid nonempty count was collapsed into retry-empty success.");
    }

    private static void DetectorAndImpulse()
    {
        var fixture = new Fixture(); using var utility = fixture.Utility(); utility.Register();
        var system = fixture.Backend.Systems[0];
        system.QueryOwner.Hits.Enqueue([
            Hit(false, 0, .1f), Hit(true, 8, .2f), Hit(true, 29, .3f), Hit(true, 0, .8f), Hit(true, 0, .1f)]);
        system.QueryOwner.Hits.Enqueue([Hit(true, 0, .5f)]);
        fixture.Clock.Samples.Enqueue(1001); fixture.Clock.Samples.Enqueue(1002);
        utility.Update();
        var saved = utility.Capture();
        Require(saved.Systems[0].Penetrating && saved.Systems[0].Detector.States.SequenceEqual(new byte[] { 0, 1, 2 }),
            "Contact ancestor/descendant states were not reconstructed from actual ordered hits.");
        Require(saved.Systems[0].Detector.Contacts[0].Point == V(.8f, 0, 0),
            "All-hit collector order was replaced by a nearest-hit query.");
        Require(fixture.First.Child.Filter == (0x12340005 | FalloutActorPenetrationUtility.PenetrationFilterFlag) &&
            fixture.First.Child.Impulses.Count == 1 && fixture.First.Child.Activations == 1,
            "Affected body filter, activation or actual point-impulse callback was omitted.");
        Require(system.QueryOwner.Filters.All(filter => filter == 0x1234001d), "Ray filter did not retain controller group/layer29.");
        fixture.Clock.Samples.Enqueue(1003);
        utility.Update();
        saved = utility.Capture();
        Require(!saved.Systems[0].Penetrating && saved.Systems[0].Detector.States.All(state => state == 1) &&
            (fixture.First.Child.Filter & FalloutActorPenetrationUtility.PenetrationFilterFlag) == 0,
            "True-to-false penetration did not reset detector and clear retained physical filters.");

        var contacts = new[] { new FalloutPenetrationContact(1, 0, V(1, 0, 0), V(1, 0, 0)),
            new FalloutPenetrationContact(1, 0, V(2, 0, 0), V(0, 1, 0)) };
        fixture.First.Child.Linear = V(0, 1, 0); fixture.First.Child.Angular = V(0, 0, 1);
        fixture.First.Child.InverseMass = B(.5f);
        FalloutPenetrationImpulse.Apply(fixture.First.Child, 1, contacts, fixture.Settings, fixture.Backend);
        Require(fixture.First.Child.Impulses[^1] == (V(0, 24, 0), V(2, 0, 0)),
            "Final matching contact, angular point velocity, normalization/projection or reciprocal mass was lost.");
        fixture.First.Child.InverseMass = 0;
        FalloutPenetrationImpulse.Apply(fixture.First.Child, 1, contacts, fixture.Settings, fixture.Backend);
        Require(fixture.First.Child.Impulses[^1].Impulse.YValue == 0, "Source zero inverse-mass arm produced infinite mass.");
        fixture.First.Child.InverseMass = B(1);
        fixture.First.Child.Angular = default;
        fixture.First.Child.Linear = V(BitConverter.UInt32BitsToSingle(FalloutPenetrationImpulse.ProjectionLengthEpsilonBits), 0, 0);
        var dotReads = fixture.Backend.DotReads;
        FalloutPenetrationImpulse.Apply(fixture.First.Child, 1, contacts, fixture.Settings, fixture.Backend);
        Require(fixture.Backend.DotReads == dotReads && fixture.First.Child.Impulses[^1].Impulse == V(0, 15, 0),
            "Projection epsilon equality called the dot getter or failed its positive-zero projection.");
        fixture.First.Child.Linear = V(BitConverter.UInt32BitsToSingle(FalloutPenetrationImpulse.ProjectionLengthEpsilonBits + 1), 0, 0);
        FalloutPenetrationImpulse.Apply(fixture.First.Child, 1, contacts, fixture.Settings, fixture.Backend);
        Require(fixture.Backend.DotReads == dotReads + 1, "Above-epsilon projection omitted the real dot getter.");

        var reverse = new Fixture(); using var reverseUtility = reverse.Utility(); reverseUtility.Register();
        var query = reverse.Backend.Systems[0].QueryOwner;
        query.Hits.Enqueue([]); query.Hits.Enqueue([new(true, 0, B(.25f), V(-1, 0, 0))]);
        query.Hits.Enqueue([]); query.Hits.Enqueue([]);
        reverseUtility.Update();
        var reverseContact = reverseUtility.Capture().Systems[0].Detector.Contacts[0];
        Require(reverseContact.Point == V(.75f, 0, 0) && reverseContact.Direction == V(-1, 0, 0) && query.Toggles == 2,
            "Reverse endpoints/query byte were lost or reverse normal was silently negated.");
    }

    private static void ClockAndWorld()
    {
        var ordered = new Fixture(); var calls = new List<string>(); ordered.Clock.Calls = calls;
        using var orderedUtility = new FalloutActorPenetrationUtility(ordered.ReadModel, ordered.ReadFilter,
            () => { calls.Add("detection-setting"); return ordered.Settings; }, ordered.Clock, ordered.Backend);
        orderedUtility.Register();
        Require(calls.SequenceEqual(["detection-setting", "counter", "scale", "conversion"]) &&
            orderedUtility.Capture() is { RegistrationCounterTicks: 1000, DetectionDurationTicks: 5000 },
            "Registration sampled QPC before the detection setting/store, or lost explicit source tick units.");
        var fixture = new Fixture(); fixture.Clock.Now = 1L << 40;
        using var utility = fixture.Utility(); utility.Register();
        var before = fixture.Clock.Reads;
        fixture.Clock.Now += 5000;
        utility.Update();
        Require(fixture.Clock.Reads == before + 2, "Duration equality skipped detection or clock was truncated to UInt32.");
        fixture.Clock.Now++;
        before = fixture.Clock.Reads;
        utility.Update();
        Require(fixture.Clock.Reads == before + 1, "Strictly expired nonpenetrating system reached a second clock sample.");
        fixture.First.Child.MotionValue = 6;
        before = fixture.Clock.Reads; utility.Update();
        Require(fixture.Clock.Reads == before, "Excluded body motion unexpectedly sampled the source clock.");
        fixture.First.Child.MotionValue = 1; fixture.First.Root.World = null;
        before = fixture.Clock.Reads; utility.Update();
        Require(fixture.Clock.Reads == before, "Detached system unexpectedly sampled clock or pose.");
        fixture.First.Root.World = "new-world"; fixture.Clock.Now -= 5001;
        utility.Update();
        Require(fixture.Backend.Systems[0].QueryOwner.WorldKey == "new-world", "Current first-body world change did not rebind real query.");
        var crossing = new Fixture(); using var crossingUtility = crossing.Utility(); crossingUtility.Register();
        crossingUtility.Update();
        var query = crossing.Backend.Systems[0].QueryOwner;
        var queries = query.Filters.Count;
        crossing.Clock.Samples.Enqueue(6000); crossing.Clock.Samples.Enqueue(6001);
        crossingUtility.Update();
        Require(query.Filters.Count == queries && crossingUtility.Capture().Systems[0].Detector.Completed,
            "Crossing between two source clock samples silently reset the completed detector or recast segments.");
    }

    private static void ColdAndFailure()
    {
        var fixture = new Fixture(); using var utility = fixture.Utility(); utility.Register();
        var query = fixture.Backend.Systems[0].QueryOwner;
        query.Hits.Enqueue([Hit(true, 0, .3f)]); query.Hits.Enqueue([]); query.Hits.Enqueue([]);
        utility.Update();
        var saved = utility.Capture(); var modelReads = fixture.ModelReads; var clockReads = fixture.Clock.Reads;
        var bodyCallbacks = fixture.First.Child.Impulses.Count;
        var coldBackend = new Backend();
        var freshGraph = Graph.Create("first");
        freshGraph.Root.Filter = fixture.First.Root.Filter; freshGraph.Child.Filter = fixture.First.Child.Filter;
        using var cold = FalloutActorPenetrationUtility.Restore(saved, fixture.ReadModel, fixture.ReadFilter,
            () => fixture.Settings, fixture.Clock, coldBackend,
            key => Require(key == fixture.Selected.Key, "Cold current model identity changed."),
            key => key == freshGraph.Root.Key ? freshGraph.Root : freshGraph.Child,
            key => key == freshGraph.Constraint.Key ? freshGraph.Constraint : throw new InvalidDataException("Unknown constraint"));
        Require(fixture.ModelReads == modelReads && fixture.Clock.Reads == clockReads && fixture.First.Child.Impulses.Count == bodyCallbacks,
            "Cold utility replayed current model getter, registration clock or point impulse.");
        Require(!ReferenceEquals(coldBackend.Systems[0].Bodies[0], fixture.First.Root) &&
            ReferenceEquals(coldBackend.Systems[0].Bodies[0], freshGraph.Root),
            "Cold utility restored a previous physical object instead of fresh owned body bindings.");
        Require(cold.Capture().Systems[0].Detector.States.SequenceEqual(saved.Systems[0].Detector.States) &&
            cold.Capture().Systems[0].Query == saved.Systems[0].Query && cold.SaveBlocker is null,
            "Cold utility lost detector/query state or refused a complete reconstructed owner.");
        Reject<InvalidDataException>(() => FalloutActorPenetrationUtility.Validate(saved with { Version = 99 }));
        Reject<InvalidDataException>(() => FalloutActorPenetrationUtility.Validate(saved with { CollisionGroup = null }));

        var failing = new Fixture(); using var failed = failing.Utility(); failed.Register();
        failing.Backend.Systems[0].QueryOwner.Hits.Enqueue([Hit(true, 0, .5f)]);
        failing.Backend.Systems[0].QueryOwner.Hits.Enqueue([]); failing.Backend.Systems[0].QueryOwner.Hits.Enqueue([]);
        failing.First.Child.ThrowImpulse = true;
        Reject<NotSupportedException>(failed.Update);
        var prefix = failed.Capture();
        Require(prefix.Failure?.Phase == "body-point-impulse" && failed.SaveBlocker is not null &&
            (failing.First.Child.Filter & FalloutActorPenetrationUtility.PenetrationFilterFlag) != 0,
            "Failed point impulse rolled back its prior physical filter or lost save refusal.");
        using var failedCold = FalloutActorPenetrationUtility.Restore(prefix, failing.ReadModel, failing.ReadFilter,
            () => failing.Settings, failing.Clock, new Backend(), _ => { },
            _ => throw new InvalidOperationException("Failed cold must not replay body constructor"),
            _ => throw new InvalidOperationException("Failed cold must not replay constraints"));
        Require(failedCold.Capture() == prefix && failedCold.SaveBlocker == failed.SaveBlocker,
            "Cold failed prefix was replayed or converted into empty success.");
        Reject<InvalidOperationException>(failedCold.Update);
    }

    private static FalloutPenetrationRayHit Hit(bool rigid, uint layer, float fraction) => new(rigid, layer, B(fraction), V(1, 0, 0));
    private static FalloutPhysicalVector3 V(float x, float y, float z) => FalloutPhysicalVector3.FromValues(x, y, z);
    private static uint B(float value) => BitConverter.SingleToUInt32Bits(value);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private sealed class Fixture
    {
        internal Graph First { get; } = Graph.Create("first");
        internal Model Selected { get; set; }
        internal Clock Clock { get; } = new();
        internal Backend Backend { get; } = new();
        internal int ModelReads { get; private set; }
        internal FalloutPenetrationEffectiveSettings Settings { get; } = new(B(15), B(1500), B(5));
        internal Fixture() => Selected = First.Model;
        internal IFalloutPenetrationModel ReadModel() { ModelReads++; return Selected; }
        internal uint? ReadFilter() => 0x12340005;
        internal FalloutActorPenetrationUtility Utility() => new(ReadModel, ReadFilter, () => Settings, Clock, Backend);
    }

    private sealed record Graph(Body Root, Body Child, Constraint Constraint, Scene Scene, Model Model)
    {
        internal static Graph Create(string key)
        {
            var root = new Body(key + ":root"); var child = new Body(key + ":child") { Position = V(1, 0, 0) };
            var constraint = new Constraint(key + ":constraint", 7, child, root);
            root.Outgoing.Add(constraint); child.Incoming.Add(constraint); root.Linked.Add(constraint);
            var scene = new Scene(root.Key, root, [null, new Scene(child.Key, child, [])]);
            return new(root, child, constraint, scene, new(key, [scene]));
        }
    }
    private sealed record Model(string Key, IReadOnlyList<IFalloutPenetrationScene?> Roots) : IFalloutPenetrationModel;
    private sealed record Scene(string Key, IFalloutPenetrationBody? Body,
        IReadOnlyList<IFalloutPenetrationScene?> Children) : IFalloutPenetrationScene;
    private sealed record Constraint(string Key, int? DataType, IFalloutPenetrationBody First,
        IFalloutPenetrationBody Second) : IFalloutPenetrationConstraint;

    private sealed class Body(string key) : IFalloutPenetrationBody
    {
        public string Key { get; } = key;
        public string Name => Key;
        public bool IsWeaponObject => Weapon;
        public byte Motion => MotionValue;
        public uint CollisionFilter => Filter;
        public string? WorldKey => World;
        public IReadOnlyList<IFalloutPenetrationConstraint> IncomingConstraints => Incoming;
        public IReadOnlyList<IFalloutPenetrationConstraint> OutgoingConstraints => Outgoing;
        public IReadOnlyList<IFalloutPenetrationConstraint?> LinkedConstraints => Linked;
        internal List<IFalloutPenetrationConstraint> Incoming { get; } = [];
        internal List<IFalloutPenetrationConstraint> Outgoing { get; } = [];
        internal List<IFalloutPenetrationConstraint?> Linked { get; } = [];
        internal bool Weapon { get; set; }
        internal byte MotionValue { get; set; } = 1;
        internal uint Filter { get; set; } = 0x12340005;
        internal string? World { get; set; } = "world";
        internal FalloutPhysicalVector3 Position { get; set; }
        internal FalloutPhysicalRotation Rotation { get; set; } = Identity;
        internal FalloutPhysicalVector3 Linear { get; set; }
        internal FalloutPhysicalVector3 Angular { get; set; }
        internal uint InverseMass { get; set; } = B(1);
        internal bool ThrowImpulse { get; set; }
        internal int Activations { get; private set; }
        internal List<(FalloutPhysicalVector3 Impulse, FalloutPhysicalVector3 Point)> Impulses { get; } = [];
        public FalloutCharacterControllerProxyTransform ReadTransform() => new(Rotation, Position);
        public FalloutPhysicalVector3 ReadCenterOfMass() => default;
        public FalloutPhysicalVector3 ReadLinearVelocity() => Linear;
        public FalloutPhysicalVector3 ReadAngularVelocity() => Angular;
        public uint ReadInverseMassBits() => InverseMass;
        public FalloutPenetrationTerminalShape ReadTerminalShape() => new(2, default, default, B(.5f));
        public FalloutPenetrationBounds ReadBounds() => new(Position, FalloutPenetrationImpulse.Add(Position, V(1, 1, 1)));
        public void Activate() => Activations++;
        public void PublishCollisionFilter(uint filter) => Filter = filter;
        public void ApplyPointImpulse(FalloutPhysicalVector3 impulse, FalloutPhysicalVector3 point)
        {
            if (ThrowImpulse) throw new NotSupportedException("unowned reached point-impulse backend");
            Impulses.Add((impulse, point));
        }
    }

    private sealed class Clock : IFalloutPenetrationClock
    {
        internal long Now { get; set; } = 1000;
        internal Queue<long> Samples { get; } = [];
        internal int Reads { get; private set; }
        internal List<string>? Calls { get; set; }
        public long ReadCounterTicks() { Calls?.Add("counter"); Reads++; return Samples.TryDequeue(out var sample) ? sample : Now; }
        public uint ReadTicksPerMillisecondBits() { Calls?.Add("scale"); return B(1); }
        public long ConvertDurationTicks(uint bits) { Calls?.Add("conversion"); return (long)BitConverter.UInt32BitsToSingle(bits); }
    }

    private sealed class Backend : IFalloutPenetrationBackend
    {
        internal List<SystemOwner> Systems { get; } = [];
        internal int DotReads { get; private set; }
        public FalloutPenetrationOrderedGraph OrderBySourceDepth(FalloutPenetrationConnectedGraph graph)
        {
            // Authored fixture has no equal-depth ambiguity. Production source
            // ordering is independently bound and cannot borrow this fixture.
            Require(graph.Depths.SequenceEqual([0, 1]), "Fixture requires root then child graph.");
            return new(graph.Bodies, graph.Constraints, graph.Parents);
        }
        public FalloutCharacterControllerProxyTransform MakeRelativeTransform(FalloutCharacterControllerProxyTransform child,
            FalloutCharacterControllerProxyTransform parent) => new(child.Rotation,
                FalloutPenetrationImpulse.Subtract(child.Translation, parent.Translation));
        public string TerminalBoneName(string bodyName) => bodyName + ":authored-terminal";
        public FalloutPenetrationNormalizedVector Normalize(FalloutPhysicalVector3 vector)
        {
            var sum = FalloutPhysicalFloat32.Add(FalloutPhysicalFloat32.Add(
                FalloutPhysicalFloat32.Multiply(vector.XValue, vector.XValue),
                FalloutPhysicalFloat32.Multiply(vector.YValue, vector.YValue)),
                FalloutPhysicalFloat32.Multiply(vector.ZValue, vector.ZValue));
            var length = MathF.Sqrt(sum);
            return new(B(length), length == 0 ? default : V(vector.XValue / length, vector.YValue / length, vector.ZValue / length));
        }
        public uint DotBits(FalloutPhysicalVector3 left, FalloutPhysicalVector3 right)
        {
            DotReads++;
            return B(FalloutPhysicalFloat32.Add(
                FalloutPhysicalFloat32.Add(FalloutPhysicalFloat32.Multiply(left.XValue, right.XValue),
                    FalloutPhysicalFloat32.Multiply(left.YValue, right.YValue)),
                FalloutPhysicalFloat32.Multiply(left.ZValue, right.ZValue)));
        }
        public FalloutPhysicalVector3 InitialImpulseDirection() => V(0, 0, 1);
        public IFalloutPenetrationSystem Construct(FalloutPenetrationSystemConstruction construction)
        {
            var system = new SystemOwner(construction); Systems.Add(system); return system;
        }
    }

    private sealed class SystemOwner(FalloutPenetrationSystemConstruction construction) : IFalloutPenetrationSystem
    {
        internal FalloutPenetrationSystemConstruction Construction { get; } = construction;
        internal Query QueryOwner { get; } = new(construction);
        internal bool Disposed { get; private set; }
        public IReadOnlyList<IFalloutPenetrationBody> Bodies => Construction.Graph.Bodies;
        public IReadOnlyList<int> BoneToBodyIndices => Construction.Skeleton.Bones.Select(bone => bone.Terminal ? -1 : bone.Body).ToArray();
        public IFalloutPenetrationQuery Query => QueryOwner;
        public IReadOnlyList<FalloutPhysicalVector3> SampleBonePositions(FalloutPenetrationSkeleton skeleton) =>
            skeleton.Bones.Select(bone => bone.Terminal
                ? FalloutPenetrationImpulse.Add(Bodies[bone.Body].ReadTransform().Translation, bone.ReferencePose.Translation)
                : Bodies[bone.Body].ReadTransform().Translation).ToArray();
        public void Dispose() { QueryOwner.Dispose(); Disposed = true; }
    }

    private sealed class Query(FalloutPenetrationSystemConstruction construction) : IFalloutPenetrationQuery
    {
        public string? WorldKey { get; private set; } = construction.Graph.Bodies[0].WorldKey;
        public bool ReverseQueryFlag { get; private set; }
        private FalloutPenetrationBounds _bounds = construction.Bounds;
        internal Queue<IReadOnlyList<FalloutPenetrationRayHit>> Hits { get; } = [];
        internal List<uint> Filters { get; } = [];
        internal int Toggles { get; private set; }
        internal bool Disposed { get; private set; }
        public void BindWorld(string? world) => WorldKey = world;
        public void PublishBounds(FalloutPenetrationBounds bounds) => _bounds = bounds;
        public void SetReverseQueryFlag(bool value) { ReverseQueryFlag = value; Toggles++; }
        public IReadOnlyList<FalloutPenetrationRayHit> CastAll(FalloutPenetrationSegment segment, uint filter)
        {
            Filters.Add(filter); return Hits.TryDequeue(out var hits) ? hits : [];
        }
        public FalloutPenetrationQuerySnapshot Capture() => new(WorldKey, ReverseQueryFlag, _bounds, construction.PhantomFilter);
        public void Restore(FalloutPenetrationQuerySnapshot saved)
        {
            WorldKey = saved.WorldKey; ReverseQueryFlag = saved.ReverseQueryFlag; _bounds = saved.Bounds;
        }
        public void Dispose() => Disposed = true;
    }
}
