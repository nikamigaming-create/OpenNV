using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Physics;

internal static class PhysicalConstructorContracts
{
    private static readonly FalloutPhysicalRotation Identity = new(V(1, 0, 0), V(0, 1, 0), V(0, 0, 1));

    internal static void Run()
    {
        var reads = new List<string>();
        var settings = FalloutPhysicalEffectiveSettings.Read(name =>
        {
            reads.Add(name);
            return name == "fDebrisMinExtent" ? 3.5f : 27.25f;
        });
        Require(reads.SequenceEqual(["fDebrisMinExtent", "fDebrisMaxVelocity"]) &&
            settings == new FalloutPhysicalEffectiveSettings(3.5f, 27.25f), "Effective settings lost source read order or winning values.");
        Reject<NotSupportedException>(() => new FalloutPhysicalConstructorInputs(new('f', 64), settings));
        Reject<ArgumentNullException>(() => new FalloutPhysicalConstructorInputs(FalloutPhysicalConstructorInputs.ReviewedEngineSha256, null!));
        Reject<InvalidOperationException>(() => FalloutPhysicalEffectiveSettings.Read(_ => throw new InvalidOperationException("unowned setting")));
        var inputs = new FalloutPhysicalConstructorInputs(FalloutPhysicalConstructorInputs.ReviewedEngineSha256, settings);
        Normalization(inputs);
        Player(inputs);
        StandardConvex(inputs);
        CollisionGroups(inputs);
        Transforms(inputs);
        Console.WriteLine("OPENNV_PHYSICAL_CONSTRUCTOR_INPUTS_OK effectiveSettings=injected sourceIdentity=reviewed " +
            "normalization=orderedBits signedZero=true nanBranches=true transformOrder=yThenXThenZ " +
            "proxyReads=current failure=propagated selectedShape=convexSlot1 physicalConstruction=unowned gameplayParity=unverified");
    }

    internal static int RunOwned(string executablePath, string archivePath, string[] members)
    {
        Run();
        var executable = File.ReadAllBytes(Path.GetFullPath(executablePath));
        var engineHash = Convert.ToHexString(SHA256.HashData(executable)).ToLowerInvariant();
        // These authored setting values do not certify source setting publication;
        // this owned audit exercises original body input projection independently.
        var inputs = new FalloutPhysicalConstructorInputs(engineHash, new(2f, 25f));
        using var archive = new FalloutBsaArchive(Path.GetFullPath(archivePath));
        var bodies = 0;
        foreach (var member in members)
        {
            var bytes = archive.Read(member); var retainedHash = SHA256.HashData(bytes);
            var source = FalloutNifFile.Read(bytes);
            var graph = FalloutNifPhysicalModel.Read(source);
            PhysicalModelContracts.VerifySource(graph);
            foreach (var body in graph.Bodies)
            {
                var retained = SHA256.HashData(body.SourceBytes.Span);
                var projected = inputs.ProjectRigidBody(body);
                Require(ReferenceEquals(projected.Source, body) &&
                    ReferenceEquals(projected.Source, source.ReadObject(body.Block.Index)) &&
                    SHA256.HashData(body.SourceBytes.Span).SequenceEqual(retained), "Owned projection changed its original declaration or bytes.");
                Require(projected.MaxLinearVelocity >= 1000f && projected.PenetrationDepth > 0f &&
                    projected.PenetrationDepth <= .2f, "Owned input normalization retained an out-of-policy finite bound.");
                bodies++;
            }
            Require(SHA256.HashData(bytes).SequenceEqual(retainedHash), "Owned projection changed source NIF bytes.");
            Console.WriteLine($"OPENNV_PHYSICAL_CONSTRUCTOR_OWNED_INPUTS_OK member={member} sha256={source.Sha256} " +
                $"bodies={graph.Bodies.Count} sourceBytes=unchanged settings=authored physicalConstruction=unowned gameplayParity=unverified");
        }
        Require(bodies > 0, "Owned constructor audit admitted no original rigid-body inputs.");
        Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.GetFullPath(executablePath)))).ToLowerInvariant() == engineHash,
            "Constructor audit changed its owned engine input.");
        return 0;
    }

    private static void Normalization(FalloutPhysicalConstructorInputs inputs)
    {
        var body = Body(); var bytes = body.SourceBytes.ToArray();
        foreach (var (value, expected) in new (uint, uint)[]
        {
            (0, 0x3e19999a), (0x80000000, 0x3e19999a), (0x34000000, 0x3e19999a),
            (0x34000001, 0x34000001), (0x3e4ccccd, 0x3e4ccccd), (0x3e4cccce, 0x3e19999a),
            (0x7f800000, 0x3e19999a), (0xff800000, 0x3e19999a), (0x7fc12345, 0x7fc12345)
        }) Require(B(inputs.ProjectRigidBody(body with { PenetrationDepth = F(value) }).PenetrationDepth) == expected,
            "Penetration normalization lost a threshold, ordered NaN or raw unchanged value.");
        foreach (var (value, expected) in new (uint, uint)[]
        {
            (0x4479ffff, 0x447a0000), (0x447a0000, 0x447a0000), (0x447a0001, 0x447a0001),
            (0x7fc12345, 0x7fc12345), (0xff800000, 0x447a0000), (0x7f800000, 0x7f800000)
        }) Require(B(inputs.ProjectRigidBody(body with { MaxLinearVelocity = F(value) }).MaxLinearVelocity) == expected,
            "Maximum velocity normalization collapsed the independent legacy cap or unordered branch.");
        foreach (var (value, expected) in new (uint, uint)[]
        {
            (0x3ffeb851, 0x3ffeb851), (0x3ffeb852, 0x3ffd70a4), (0x3ffeb853, 0x3ffd70a4),
            (0x7fc12345, 0x7fc12345), (0xff800000, 0xff800000), (0x7f800000, 0x3ffd70a4)
        }) Require(B(inputs.ProjectRigidBody(body with { Restitution = F(value) }).Restitution) == expected,
            "Restitution normalization lost the exact threshold/replacement bits.");
        foreach (var mass in new uint[] { 0, 0x80000000 })
        {
            var fixedBody = inputs.ProjectRigidBody(body with { Mass = F(mass), MotionSystem = 7, QualityType = 0 });
            var keyframed = inputs.ProjectRigidBody(body with { Mass = F(mass), MotionSystem = 6, QualityType = 0 });
            Require(fixedBody is { InitialMotion: 7, InitialQuality: 1, SavedMotion: null } &&
                keyframed is { InitialMotion: 6, InitialQuality: 0, SavedMotion: null }, "Signed-zero mass conflated fixed/keyframed construction.");
        }
        foreach (var mass in new uint[] { 0x3f800000, 0x7fc12345 })
            foreach (var motion in new byte[] { 6, 7 })
            {
                var projected = inputs.ProjectRigidBody(body with { Mass = F(mass), MotionSystem = motion });
                Require(projected.InitialMotion == 1 && projected.InitialQuality == 4 && projected.SavedMotion == motion,
                    "Nonzero/unordered mass omitted the separate saved-motion conversion input.");
            }
        var ordinary = inputs.ProjectRigidBody(body with { Mass = 1, MotionSystem = 4, QualityType = 0 });
        Require(ordinary is { InitialMotion: 4, InitialQuality: 4, SavedMotion: null } &&
            ordinary.Source.Inertia == body.Inertia && body.SourceBytes.Span.SequenceEqual(bytes),
            "Construction projection changed unrelated source state or declaration bytes.");
    }

    private static void Player(FalloutPhysicalConstructorInputs inputs)
    {
        var player = inputs.ProjectPlayer(V(33, -17.5f, 12345.67f), V(0, 0, 0));
        Require(player.ScaledPosition == new FalloutPhysicalVector3(0x4096e0b9, 0xc020059e, 0x44dc7d16) &&
            player.CapsuleRadiusBits == 0x40392b12 && player.CapsulePositiveEndpoint.Z == 0x40c80705 &&
            player.CapsuleNegativeEndpoint.Z == 0xc0c80705 && player.RetainedOffset.Z == 0x4113e7e1 &&
            player.StepBits == 0x408dbbd5 && player.HeightBits == 0x41924e47 && player.SelectedShapeSlot == 1,
            "Player projection replaced source rounded values, standard step or selected convex shape.");
        var changedCenter = inputs.ProjectPlayer(V(0, 0, 0), V(5, -3, 2));
        Require(changedCenter.StandardCenter == V(5, -3, 2) && changedCenter.RetainedOffset != player.RetainedOffset &&
            changedCenter.CapsulePositiveEndpoint.X == changedCenter.CapsuleNegativeEndpoint.X,
            "Player projection cached an initial zero standard center as current source state.");
        // The inspected axial source adds center Z to the positive endpoint,
        // then negates that entire endpoint before scaling. It mirrors Z about
        // zero even for a nonzero center; do not replace it with recentered bounds.
        Require(changedCenter.CapsulePositiveEndpoint.Z == 0x40d12bea &&
            changedCenter.CapsuleNegativeEndpoint.Z == 0xc0d12bea && changedCenter.RetainedOffset.Z == 0x41187a53 &&
            B(F(changedCenter.CapsulePositiveEndpoint.Z) + F(changedCenter.CapsuleNegativeEndpoint.Z)) == 0,
            "Nonzero center Z changed the source endpoint-negation order or retained offset.");
    }

    private static void StandardConvex(FalloutPhysicalConstructorInputs inputs)
    {
        var player = inputs.ProjectPlayer(V(33, -17.5f, 12345.67f), V(0, 0, 0));
        var projected = inputs.ProjectStandardConvex(player, 0);
        // Source-order goldens retain the eighteen input slots before sorting,
        // hull construction or plane merging. These are not a physical hull.
        uint[] x = [0, 0x40392b12, 0x4002eeb6, 0, 0xc002eeb6, 0xc0392b12, 0xc002eeb6, 0, 0x4002eeb6,
            0x40392b12, 0x4002eeb6, 0, 0xc002eeb6, 0xc0392b12, 0xc002eeb6, 0, 0x4002eeb6, 0];
        uint[] y = [0, 0, 0x4002eeb6, 0x40392b12, 0x4002eeb6, 0, 0xc002eeb6, 0xc0392b12, 0xc002eeb6,
            0, 0x4002eeb6, 0x40392b12, 0x4002eeb6, 0, 0xc002eeb6, 0xc0392b12, 0xc002eeb6, 0];
        uint[] z = [0xc1124e47, 0xbfdc5ff4, 0xbfdc5ff4, 0xbfdc5ff4, 0xbfdc5ff4, 0xbfdc5ff4, 0xbfdc5ff4, 0xbfdc5ff4, 0xbfdc5ff4,
            0x40f651ca, 0x40f651ca, 0x40f651ca, 0x40f651ca, 0x40f651ca, 0x40f651ca, 0x40f651ca, 0x40f651ca, 0x41124e47];
        Require(projected.Points.Count == x.Length && projected.ConvexRadiusBits == 0x3d4ccccd &&
            projected.SpecializedRadiusBits == 0x3dcccccd && projected.KeepContactDistanceBits == 0x40392b12,
            "Source convex inputs conflated its independent radii, keep-contact distance or slot count.");
        for (var index = 0; index < x.Length; index++)
            Require(projected.Points[index] == new FalloutPhysicalVector4(new(x[index], y[index], z[index]), 0x40392b12),
                $"Source standard convex point {index} changed ordered Float32 stores or W radius.");
        Reject<NotSupportedException>(() => ((IList<FalloutPhysicalVector4>)projected.Points)[0] = default);
        foreach (var (current, expected) in new (uint, uint)[]
        {
            (0, 0x40392b12), (0x80000000, 0x40392b12), (0x3f800000, 0x3f800000),
            (0x7fc12345, 0x7fc12345), (0xff800000, 0xff800000)
        }) Require(inputs.ProjectStandardConvex(player, current).KeepContactDistanceBits == expected,
            "Keep-contact distance changed the source ordered-zero replacement or unordered retention.");
        Reject<InvalidDataException>(() => inputs.ProjectStandardConvex(player with { StepBits = 0 }, 0));
        var centeredPlayer = inputs.ProjectPlayer(V(0, 0, 0), V(5, -3, 2));
        var centered = inputs.ProjectStandardConvex(centeredPlayer, 0);
        Require(centered.Points[0].Position.X == centeredPlayer.CapsuleNegativeEndpoint.X &&
            centered.Points[17].Position.Y == centeredPlayer.CapsulePositiveEndpoint.Y &&
            centered.Points[1].Position.X != projected.Points[1].Position.X &&
            centered.Points[9].Position.X == projected.Points[9].Position.X &&
            centered.Points[9].Position.Y == projected.Points[9].Position.Y,
            "Nonzero standard center lost the source endpoint/lower-ring versus upper-ring XY asymmetry.");
        var another = inputs.ProjectStandardConvex(inputs.ProjectPlayer(V(0, 0, 0), V(0, 0, 0)), 0);
        Require(another.Points.SequenceEqual(projected.Points), "Reference placement changed local source shape inputs.");
        Console.WriteLine("OPENNV_STANDARD_CONVEX_INPUTS_OK sourceOrder=18 exactFloat32=true radii=distinct " +
            "keepContact=orderedZero immutable=true referencePlacement=independent canonicalHull=unowned physicalShape=unowned");
    }

    private static void CollisionGroups(FalloutPhysicalConstructorInputs inputs)
    {
        var groups = new FalloutCharacterCollisionGroups(inputs);
        Require(groups.Capture().Counter == 10 && groups.AllocateCreationInfoFilter() == 0x000b001e &&
            groups.AllocateCreationInfoFilter() == 0x000c001e, "Creation info lost its shared initial counter or source filter layout.");
        var saved = System.Text.Json.JsonSerializer.Deserialize<FalloutCharacterCollisionGroupsSnapshot>(
            System.Text.Json.JsonSerializer.Serialize(groups.Capture()))!;
        var cold = new FalloutCharacterCollisionGroups(inputs, saved);
        Require(cold.Identity != groups.Identity && cold.AllocateCreationInfoFilter() == groups.AllocateCreationInfoFilter(),
            "Cold collision groups replayed construction allocations or retained an old owner identity.");
        var wrapping = new FalloutCharacterCollisionGroups(inputs, saved with { Counter = ushort.MaxValue });
        Require(wrapping.AllocateCreationInfoFilter() == 0x000a001e && wrapping.Capture().Counter == 10 &&
            wrapping.AllocateCreationInfoFilter() == 0x000b001e, "Zero wrap substituted only a local result rather than the shared source counter.");
        Reject<InvalidDataException>(() => new FalloutCharacterCollisionGroups(inputs, saved with { Counter = 0 }));
        Reject<InvalidDataException>(() => new FalloutCharacterCollisionGroups(inputs, saved with { EngineSha256 = new('f', 64) }));
        Reject<InvalidDataException>(() => new FalloutCharacterCollisionGroups(inputs, saved with { Schema = "foreign" }));
        Exception? escaped = null;
        var thread = new Thread(() => { try { groups.AllocateCreationInfoFilter(); } catch (Exception error) { escaped = error; } });
        var before = groups.Capture(); thread.Start(); thread.Join();
        Require(escaped is InvalidOperationException && groups.Capture() == before,
            "A foreign thread changed source allocation order before refusing.");
        groups.Dispose(); groups.Dispose();
        Reject<ObjectDisposedException>(() => groups.AllocateCreationInfoFilter());
        Reject<ObjectDisposedException>(() => groups.Capture());
        cold.Dispose(); wrapping.Dispose();
        Console.WriteLine("OPENNV_CHARACTER_COLLISION_GROUPS_OK sharedCounter=true firstGroup=11 lowLayer=30 " +
            "zeroWrapResetsCounter=10 coldFreshIdentity=true foreignThreadRefused=true retiredRefused=true productionPublication=unowned");
    }

    private static void Transforms(FalloutPhysicalConstructorInputs inputs)
    {
        var current = new FalloutCharacterControllerProxyTransform(new(V(0, 0, 1), V(1, 0, 0), V(0, 1, 0)), V(1, 2, 12.5f));
        var reads = 0;
        var owner = new FalloutCharacterControllerTransformState(inputs, V(3, 5, 7), () => { reads++; return current; });
        Require(FalloutCharacterControllerTransformState.RotateOffset(V(3, 5, 7), current.Rotation) == V(5, 7, 3) &&
            owner.ReadPositionZBits() == 0x4284fb55 && reads == 1 && owner.ReadHeightDeltaBits(B(80)) == 0x41582558 && reads == 2,
            "Current proxy position/height changed rotation, source conversion or provider read count.");
        current = current with { Translation = V(1, 2, 13.5f) };
        Require(owner.ReadPositionZBits() != 0x4284fb55 && reads == 3, "Controller reused a stale proxy transform.");
        Require(owner.ProxyTranslationForReferencePosition(V(0, 0, 0), current.Rotation) == V(5, 7, 3),
            "Proxy input construction included current translation in the rotated retained offset.");
        var cancellation = new FalloutPhysicalRotation(V(0, 0, 1), V(0, 0, 1), V(0, 0, 1));
        Require(FalloutCharacterControllerTransformState.RotateOffset(V(16777216, 1, -16777216), cancellation).Z == 0,
            "Rotation reassociated y+x+z into a different Float32 cancellation result.");
        var noFusion = new FalloutPhysicalRotation(new(0, 0, 0x3f800001), V(0, 0, 1), V(0, 0, 0));
        Require(FalloutCharacterControllerTransformState.RotateOffset(new(0x3f800001, 0xbf800002, 0), noFusion).Z == 0,
            "Rotation fused a product/addition across the original Float32 store.");
        var refusalReads = 0;
        var unavailable = new FalloutCharacterControllerTransformState(inputs, V(0, 0, 0), () =>
        { refusalReads++; throw new NotSupportedException("actual proxy unbound"); });
        Require(unavailable.ReadHeightDeltaBits(0) == 0 && unavailable.ReadHeightDeltaBits(0x80000000) == 0 && refusalReads == 0,
            "Signed-zero scalar read an unneeded proxy or returned negative zero.");
        Reject<NotSupportedException>(() => unavailable.ReadHeightDeltaBits(0x3f800000));
        Require(refusalReads == 1, "Missing current proxy was replaced by constructor zero.");
        var signedZero = new FalloutCharacterControllerTransformState(inputs, V(0, 0, 0), () => new(Identity, new(0, 0, 0x80000000)));
        Require(signedZero.ReadPositionZBits() == 0x80000000, "Controller position lost source signed zero.");
        Require(float.IsNaN(F(owner.ReadHeightDeltaBits(0x7fc12345))) && reads == 4, "Unordered pending scalar skipped its actual current proxy read.");
    }

    private static FalloutNifRigidBody Body() => new(new(0, "bhkRigidBody", 0, 236), -1,
        new(1, 2, 3, 0), new(1, 0, 0, 0), new(4, 5, 6, 0), 1f, .1f, .2f, .3f, .4f, 4, [])
    {
        Filter = new(1, 2, 3), WorldUnused = 7, BroadPhaseType = 1, BroadPhaseUnused = [4, 5, 6],
        Property = new(1, 2, 3), EntityResponse = new(1, 2, 3), InfoUnused1 = 1, InfoFilter = new(4, 5, 6),
        InfoUnused2 = 2, InfoResponse = new(4, 5, 6), InfoUnused3 = 3, LinearVelocity = new(-0f, 1, 2, 0),
        AngularVelocity = new(3, 4, 5, 0), Inertia = new(new(1, 0, 0), 11, new(0, 1, 0), 12, new(0, 0, 1), 13),
        MaxLinearVelocity = 25, MaxAngularVelocity = 7, PenetrationDepth = .15f, DeactivatorType = 2,
        SolverDeactivation = 3, QualityType = 4, InfoUnused4 = [14, 15, 16], BodyFlags = 17, SourceBytes = new byte[236],
    };
    private static FalloutPhysicalVector3 V(float x, float y, float z) => FalloutPhysicalVector3.FromValues(x, y, z);
    private static float F(uint bits) => BitConverter.UInt32BitsToSingle(bits);
    private static uint B(float value) => BitConverter.SingleToUInt32Bits(value);
    private static void Require(bool value, string reason) { if (!value) throw new InvalidDataException(reason); }
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidDataException($"Expected {typeof(T).Name} from unowned/corrupt constructor input.");
    }
}
