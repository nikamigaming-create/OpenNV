using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

// Offsets come from the Fallout bhkWorldObject, bhkEntity and 550/660 CInfo
// layouts. Distinct sentinels catch field swaps that a zero-filled body cannot.
var bodyBytes = Enumerable.Range(0, 236).Select(index => (byte)index).ToArray();
BinaryPrimitives.WriteInt32LittleEndian(bodyBytes, -1);
for (var offset = 52; offset < 212; offset += sizeof(float))
    BinaryPrimitives.WriteUInt32LittleEndian(bodyBytes.AsSpan(offset), 0x3f800000U + (uint)offset);
BinaryPrimitives.WriteUInt32LittleEndian(bodyBytes.AsSpan(84), 0x80000000U);
BinaryPrimitives.WriteUInt32LittleEndian(bodyBytes.AsSpan(128), 0x7fc12345U);
BinaryPrimitives.WriteUInt32LittleEndian(bodyBytes.AsSpan(144), 0xff800000U);
BinaryPrimitives.WriteUInt32LittleEndian(bodyBytes.AsSpan(160), 0x80000000U);
BinaryPrimitives.WriteUInt32LittleEndian(bodyBytes.AsSpan(228), 0);

foreach (var version in new uint[] { 32, 34 })
{
    foreach (var type in new[] { "bhkRigidBody", "bhkRigidBodyT" })
    {
        var file = FalloutNifFile.Read(WrapBody(bodyBytes, type, version));
        var body = (FalloutNifRigidBody)file.ReadObject(0);
        Require(body.Filter == new FalloutNifCollisionFilter(4, 5, 0x0706), "World filter was lost.");
        Require(body.InfoFilter == new FalloutNifCollisionFilter(36, 37, 0x2726), "CInfo filter was substituted.");
        Require(body.BroadPhaseType == 12, "Broadphase type differs.");
        Require(body.Property == new FalloutNifHavokProperty(0x13121110, 0x17161514, 0x1b1a1918),
            "World property fields differ.");
        Require(body.EntityResponse == new FalloutNifCollisionResponse(28, 29, 0x1f1e) &&
            body.InfoResponse == new FalloutNifCollisionResponse(44, 45, 0x2f2e),
            "Entity and CInfo response or callback delay were conflated.");
        Require(BitConverter.SingleToUInt32Bits(body.LinearVelocity.X) == 0x80000000U,
            "Source signed-zero velocity bits changed.");
        Require(BitConverter.SingleToUInt32Bits(body.AngularVelocity.Z) == 0x3f80006cU,
            "Angular velocity offset differs.");
        Require(body.Inertia.Padding0 == 0x7fc12345U && body.Inertia.Padding1 == 0xff800000U &&
            body.Inertia.Padding2 == 0x80000000U, "Inertia padding was interpreted as floating-point state.");
        Require(BitConverter.SingleToUInt32Bits(body.Inertia.Row1.X) == 0x3f800084U,
            "Inertia matrix row stride differs.");
        Require(BitConverter.SingleToUInt32Bits(body.MaxLinearVelocity) == 0x3f8000c8U &&
            BitConverter.SingleToUInt32Bits(body.PenetrationDepth) == 0x3f8000d0U,
            "Motion limits were lost.");
        Require(body.MotionSystem == 212 && body.DeactivatorType == 213 &&
            body.SolverDeactivation == 214 && body.QualityType == 215,
            "Unknown solver enums were silently replaced with defaults.");
        Require(body.BodyFlags == 0xebeae9e8U, "Body flags differ.");
        Require(EncodeBody(body).AsSpan().SequenceEqual(bodyBytes), "A source body byte was lost.");
        Require(body.SourceBytes.Span.SequenceEqual(bodyBytes), "Original body bytes were not retained.");
    }
}
var invalid = (byte[])bodyBytes.Clone();
BinaryPrimitives.WriteUInt32LittleEndian(invalid.AsSpan(84), 0x7fc12345U);
ExpectInvalid(() => FalloutNifFile.Read(WrapBody(invalid, "bhkRigidBody", 34)).ReadObject(0),
    "linear velocity x is not finite");
ExpectInvalid(() => FalloutNifFile.Read(WrapBody(bodyBytes[..^1], "bhkRigidBody", 34)).ReadObject(0),
    "rigid-body flags");
var constrained = new byte[244];
bodyBytes.AsSpan(0, 228).CopyTo(constrained);
BinaryPrimitives.WriteUInt32LittleEndian(constrained.AsSpan(228), 2);
BinaryPrimitives.WriteInt32LittleEndian(constrained.AsSpan(232), 0);
BinaryPrimitives.WriteInt32LittleEndian(constrained.AsSpan(236), -1);
BinaryPrimitives.WriteUInt32LittleEndian(constrained.AsSpan(240), 0x87654321);
var constrainedBody = (FalloutNifRigidBody)FalloutNifFile.Read(WrapBody(constrained, "bhkRigidBody", 34)).ReadObject(0);
Require(constrainedBody.Constraints.SequenceEqual([0, -1]) &&
    EncodeBody(constrainedBody).AsSpan().SequenceEqual(constrained), "Constraint table order or trailing flags differ.");
Console.WriteLine("OPENNV_NIF_PHYSICS_CONTRACT_OK completeBodyBytes=true finiteVelocityValidation=true");

if (args.Length == 1)
{
    var root = Path.GetFullPath(args[0]);
    var archives = File.Exists(root) ? [root] : Directory.GetFiles(root, "*.bsa");
    var files = 0;
    var bodies = 0;
    var constraints = 0;
    var filters = new SortedSet<byte>();
    var motions = new SortedSet<byte>();
    foreach (var archivePath in archives.Order(StringComparer.OrdinalIgnoreCase))
    {
        using var archive = new FalloutBsaArchive(archivePath);
        var archiveBodies = 0;
        foreach (var path in archive.MemberPaths.Where(path => path.EndsWith(".nif", StringComparison.OrdinalIgnoreCase)))
        {
            var file = FalloutNifFile.Read(archive.Read(path));
            ++files;
            foreach (var block in file.Blocks.Where(block => block.TypeName is "bhkRigidBody" or "bhkRigidBodyT"))
            {
                var body = (FalloutNifRigidBody)file.ReadObject(block.Index);
                Require(EncodeBody(body).AsSpan().SequenceEqual(body.SourceBytes.Span),
                    $"Physics body bytes differ: {Path.GetFileName(archivePath)} {path} block={block.Index}");
                ++bodies;
                ++archiveBodies;
                constraints += body.Constraints.Length;
                filters.Add(body.Filter.Layer);
                motions.Add(body.MotionSystem);
            }
        }
        if (archiveBodies != 0)
            Console.WriteLine($"OPENNV_NIF_PHYSICS_ARCHIVE archive={Path.GetFileName(archivePath)} bodies={archiveBodies}");
    }
    Require(bodies != 0, "Owned audit did not discover any rigid bodies.");
    Console.WriteLine($"OPENNV_NIF_PHYSICS_OWNED_OK files={files} bodies={bodies} constraints={constraints} " +
        $"layers={string.Join(',', filters)} motionSystems={string.Join(',', motions)} byteLoss=0 gameplayParity=unverified");
}
else if (args.Length != 0)
    throw new ArgumentException("Use no arguments for synthetic contracts, or one owned Data directory/BSA path.");

static byte[] WrapBody(byte[] body, string type, uint version)
{
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
    writer.Write(Encoding.ASCII.GetBytes("Gamebryo File Format, Version 20.2.0.7\n"));
    writer.Write(FalloutNifFile.Version);
    writer.Write((byte)1);
    writer.Write(FalloutNifFile.UserVersion);
    writer.Write(1U);
    writer.Write(version);
    writer.Write(new byte[] { 1, 0, 1, 0, 1, 0 });
    writer.Write((ushort)1);
    writer.Write(type.Length);
    writer.Write(Encoding.ASCII.GetBytes(type));
    writer.Write((ushort)0);
    writer.Write(body.Length);
    writer.Write(0U);
    writer.Write(0U);
    writer.Write(0U);
    writer.Write(body);
    writer.Write(1U);
    writer.Write(0);
    return stream.ToArray();
}

static byte[] EncodeBody(FalloutNifRigidBody body)
{
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    void Filter(FalloutNifCollisionFilter value) { writer.Write(value.Layer); writer.Write(value.Flags); writer.Write(value.Group); }
    void Response(FalloutNifCollisionResponse value) { writer.Write(value.Type); writer.Write(value.Unused); writer.Write(value.CallbackDelay); }
    void Vector3(FalloutNifVector3 value) { writer.Write(value.X); writer.Write(value.Y); writer.Write(value.Z); }
    void Vector4(FalloutNifVector4 value) { writer.Write(value.X); writer.Write(value.Y); writer.Write(value.Z); writer.Write(value.W); }
    writer.Write(body.Shape);
    Filter(body.Filter);
    writer.Write(body.WorldUnused);
    writer.Write(body.BroadPhaseType);
    writer.Write(body.BroadPhaseUnused);
    writer.Write(body.Property.Data); writer.Write(body.Property.Size); writer.Write(body.Property.CapacityAndFlags);
    Response(body.EntityResponse);
    writer.Write(body.InfoUnused1); Filter(body.InfoFilter); writer.Write(body.InfoUnused2);
    Response(body.InfoResponse); writer.Write(body.InfoUnused3);
    Vector4(body.Translation);
    writer.Write(body.Rotation.X); writer.Write(body.Rotation.Y); writer.Write(body.Rotation.Z); writer.Write(body.Rotation.W);
    Vector4(body.LinearVelocity); Vector4(body.AngularVelocity);
    Vector3(body.Inertia.Row0); writer.Write(body.Inertia.Padding0);
    Vector3(body.Inertia.Row1); writer.Write(body.Inertia.Padding1);
    Vector3(body.Inertia.Row2); writer.Write(body.Inertia.Padding2);
    Vector4(body.Center);
    writer.Write(body.Mass); writer.Write(body.LinearDamping); writer.Write(body.AngularDamping);
    writer.Write(body.Friction); writer.Write(body.Restitution);
    writer.Write(body.MaxLinearVelocity); writer.Write(body.MaxAngularVelocity); writer.Write(body.PenetrationDepth);
    writer.Write(body.MotionSystem); writer.Write(body.DeactivatorType); writer.Write(body.SolverDeactivation); writer.Write(body.QualityType);
    foreach (var value in body.InfoUnused4) writer.Write(value);
    writer.Write(body.Constraints.Length);
    foreach (var value in body.Constraints) writer.Write(value);
    writer.Write(body.BodyFlags);
    return stream.ToArray();
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidDataException(message);
}

static void ExpectInvalid(Action action, string message)
{
    try { action(); }
    catch (InvalidDataException error) when (error.Message.Contains(message, StringComparison.Ordinal)) { return; }
    throw new InvalidDataException($"Expected rejection containing: {message}");
}
