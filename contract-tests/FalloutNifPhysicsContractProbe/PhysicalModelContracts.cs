using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

internal static class PhysicalModelContracts
{
    internal static void Run()
    {
        var bytes = Fixture();
        var retained = (byte[])bytes.Clone();
        var source = FalloutNifFile.Read(bytes);
        var graph = FalloutNifPhysicalModel.Read(source);
        Require(ReferenceEquals(graph.Source, source) && graph.Roots.SequenceEqual([0]) &&
            graph.Scene.Select(value => value.Declaration.Block.Index).SequenceEqual([0, 2, 1, 11, 13]),
            "Physical scene changed original root/child order or selected another source.");
        Require(graph.Scene[0].Children.SequenceEqual([2, -1, 1, 11, 13]) &&
            graph.Scene[1].Parent == 0 && graph.Scene[1].Slot == 0 && graph.Scene[2].Slot == 2 &&
            graph.Scene[2].Children.SequenceEqual([-1]) && graph.FindSceneObject(8) is null,
            "Physical scene dropped null slots, parent identity or admitted a detached block.");
        Require(graph.Bodies.Select(value => value.Block.Index).SequenceEqual([6, 5]) &&
            graph.Constraints.Count == 1 && graph.Constraints[0].Header.EntityA == 5 &&
            graph.Constraints[0].Header.EntityB == 6 && graph.Constraints[0].Declaration is FalloutNifHingeConstraint &&
            graph.Registrations.Select(value => (value.Body.Block.Index, value.Slot, value.Constraint?.Header.Block.Index))
                .SequenceEqual(new (int, int, int?)[] { (6, 0, 7), (5, 0, 7), (5, 1, null), (5, 2, 7) }),
            "Physical graph lost endpoint identity, shared constraint identity or registration slots.");
        Require(ReferenceEquals(graph.Registrations[0].Constraint, graph.Registrations[1].Constraint) &&
            ReferenceEquals(graph.FindAttachedBody(5), source.ReadObject(5)) &&
            graph.FindSceneObject(11)!.Collision is { Body: -1 } &&
            graph.FindSceneObject(13)!.Collision is { Body: 15 } && graph.FindAttachedBody(15) is null,
            "Physical graph copied a foreign body or invented a rigid body for a null/phantom wrapper.");
        Require(((FalloutNifNode)graph.Scene[1].Declaration).Name == ((FalloutNifNode)graph.Scene[2].Declaration).Name,
            "Synthetic ambiguous names did not exercise identity independent of name matching.");
        var detached = FalloutNifPhysicalModel.Read(FalloutNifFile.Read(Fixture("detached-endpoint")));
        Require(detached.Constraints.Single().Second.Block.Index == 10 && detached.FindAttachedBody(10) is null,
            "An authored endpoint outside the scene was invented as a current attached body.");
        foreach (var mode in new[] { "cycle", "multiple-parent", "foreign-target", "foreign-registration", "unknown-constraint" })
            Reject(() => FalloutNifPhysicalModel.Read(FalloutNifFile.Read(Fixture(mode))), mode);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { FalloutNifPhysicalModel.Read(source, cancellation.Token); throw new InvalidDataException("Cancelled physical preparation returned."); }
        catch (OperationCanceledException) { }
        Require(bytes.AsSpan().SequenceEqual(retained), "Physical preparation changed source bytes.");
        Console.WriteLine("OPENNV_NIF_PHYSICAL_MODEL_CONTRACT_OK orderedScene=true nullSlots=true sharedConstraints=true " +
            "detachedEndpoint=unattached ambiguousNames=identityOnly nullableBody=true phantom=independent cancelled=true sourceBytes=unchanged liveDynamics=unbound");
    }

    internal static int RunOwned(string archivePath, string[] members)
    {
        if (members.Length == 0) throw new ArgumentException("Select original NIF members for the owned physical-model audit.");
        using var archive = new FalloutBsaArchive(Path.GetFullPath(archivePath));
        foreach (var member in members)
        {
            var bytes = archive.Read(member);
            var hash = System.Security.Cryptography.SHA256.HashData(bytes);
            var source = FalloutNifFile.Read(bytes);
            var graph = FalloutNifPhysicalModel.Read(source);
            VerifySource(graph);
            Require(System.Security.Cryptography.SHA256.HashData(bytes).SequenceEqual(hash), "Owned physical model changed source bytes.");
            Console.WriteLine($"OPENNV_NIF_PHYSICAL_MODEL_OWNED_OK archive={Path.GetFileName(archivePath)} member={member} " +
                $"sha256={source.Sha256} scene={graph.Scene.Count} bodies={graph.Bodies.Count} constraints={graph.Constraints.Count} " +
                $"registrations={graph.Registrations.Count} sourceOrder=exact liveDynamics=unbound gameplayParity=unverified");
        }
        return 0;
    }

    internal static void VerifySource(FalloutNifPhysicalModel graph)
    {
        var source = graph.Source;
        foreach (var entry in graph.Scene)
        {
            Require(ReferenceEquals(source.ReadObject(entry.Declaration.Block.Index), entry.Declaration), "Scene graph lost decoded source identity.");
            if (entry.Parent is { } parent)
                Require(source.ReadNode(parent).Children[entry.Slot] == entry.Declaration.Block.Index, "Physical child-slot differs from original source.");
            else Require(source.Roots[entry.Slot] == entry.Declaration.Block.Index, "Physical root slot differs from original source.");
            if (entry.Declaration is FalloutNifNode node)
                Require(node.Children.SequenceEqual(entry.Children), "Physical scene dropped original null child slots.");
            if (entry.Collision is { } collision)
                Require(collision.Target == entry.Declaration.Block.Index && ReferenceEquals(source.ReadObject(collision.Block.Index), collision),
                    "Physical wrapper lost its actual source target.");
        }
        foreach (var registration in graph.Registrations)
            Require(registration.Body.Constraints[registration.Slot] == (registration.Constraint?.Header.Block.Index ?? -1),
                "Physical constraint registration differs from source body order.");
        foreach (var constraint in graph.Constraints)
            Require(ReferenceEquals(source.ReadObject(constraint.Header.EntityA), constraint.First) &&
                ReferenceEquals(source.ReadObject(constraint.Header.EntityB), constraint.Second) &&
                ReferenceEquals(source.ReadObject(constraint.Header.Block.Index), constraint.Declaration),
                "Physical joint lost actual source endpoint or full descriptor identity.");
    }

    private static void Require(bool condition, string reason)
    { if (!condition) throw new InvalidDataException(reason); }

    private static void Reject(Action action, string mode)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException)
        {
            var expected = mode switch
            {
                "cycle" or "multiple-parent" => "repeats or cycles",
                "foreign-target" => "targets another scene object",
                "foreign-registration" => "registers another pair's constraint",
                _ => "has no identity contract",
            };
            Require(error.Message.Contains(expected, StringComparison.Ordinal), $"Physical negative {mode} reached unrelated refusal: {error.Message}");
            return;
        }
        throw new InvalidDataException("Physical graph admitted " + mode);
    }

    private static byte[] Fixture(string mode = "valid")
    {
        static byte[] Bytes(Action<BinaryWriter> write)
        { using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); write(writer); return stream.ToArray(); }
        static byte[] Node(int name, int collision, int[] children) => Bytes(writer =>
        {
            writer.Write(name); writer.Write(0U); writer.Write(-1); writer.Write(14U);
            foreach (var value in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1 }) writer.Write(value);
            writer.Write(0U); writer.Write(collision); writer.Write(children.Length);
            foreach (var child in children) writer.Write(child); writer.Write(0U);
        });
        static byte[] Body(int[] constraints)
        {
            var bytes = new byte[236 + constraints.Length * sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, -1);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(80), 1);
            bytes[212] = 4;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(228), constraints.Length);
            for (var slot = 0; slot < constraints.Length; slot++) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(232 + slot * sizeof(int)), constraints[slot]);
            return bytes;
        }
        static byte[] Collision(int target, int body, bool blend = false) => Bytes(writer =>
        { writer.Write(target); writer.Write((ushort)1); writer.Write(body); if (blend) { writer.Write(.4f); writer.Write(.8f); } });
        var hinge = Bytes(writer =>
        {
            writer.Write(2U); writer.Write(5); writer.Write(mode is "detached-endpoint" or "foreign-registration" ? 10 : 6); writer.Write(1U);
            for (var endpoint = 0; endpoint < 2; endpoint++)
                foreach (var vector in new float[][] { [1, 0, 0], [0, 1, 0], [0, 0, 1], [.1f, .2f, .3f] })
                { foreach (var value in vector) writer.Write(value); writer.Write(uint.MaxValue); }
        });
        var phantom = new byte[100]; BinaryPrimitives.WriteInt32LittleEndian(phantom, -1);
        for (var component = 0; component < 16; component++)
            BinaryPrimitives.WriteSingleLittleEndian(phantom.AsSpan(36 + component * sizeof(float)), component % 5 == 0 ? 1 : 0);
        var blocks = new (string Type, byte[] Bytes)[]
        {
            ("NiNode", Node(0, -1, mode == "cycle" ? [2, -1, 1, 0] : mode == "multiple-parent" ? [2, 1, 1] : [2, -1, 1, 11, 13])),
            ("NiNode", Node(1, 3, [-1])), ("NiNode", Node(1, 4, [])),
            ("bhkCollisionObject", Collision(mode == "foreign-target" ? 2 : 1, 5)), ("bhkBlendCollisionObject", Collision(2, 6, true)),
            ("bhkRigidBody", Body([7, -1, 7])), ("bhkRigidBodyT", Body(mode == "detached-endpoint" ? [] : [7])),
            (mode == "unknown-constraint" ? "bhkStiffSpringConstraint" : "bhkHingeConstraint", hinge),
            ("NiNode", Node(2, 9, [])), ("bhkCollisionObject", Collision(8, 10)), ("bhkRigidBody", Body([])),
            ("NiNode", Node(3, 12, [])), ("bhkCollisionObject", Collision(11, -1)),
            ("NiNode", Node(4, 14, [])), ("bhkCollisionObject", Collision(13, 15)), ("bhkSimpleShapePhantom", phantom),
        };
        return Bytes(writer =>
        {
            writer.Write(Encoding.ASCII.GetBytes("Gamebryo File Format, Version 20.2.0.7\n"));
            writer.Write(FalloutNifFile.Version); writer.Write((byte)1); writer.Write(FalloutNifFile.UserVersion);
            writer.Write(blocks.Length); writer.Write(34U); writer.Write(new byte[] { 1, 0, 1, 0, 1, 0 });
            var types = blocks.Select(block => block.Type).Distinct(StringComparer.Ordinal).ToArray(); writer.Write((ushort)types.Length);
            foreach (var type in types) { writer.Write(type.Length); writer.Write(Encoding.ASCII.GetBytes(type)); }
            foreach (var block in blocks) writer.Write((ushort)Array.IndexOf(types, block.Type));
            foreach (var block in blocks) writer.Write(block.Bytes.Length);
            string[] names = ["Root", "DuplicateName", "Detached", "Nullable", "Phantom"];
            writer.Write(names.Length); writer.Write(names.Max(value => value.Length));
            foreach (var name in names) { writer.Write(name.Length); writer.Write(Encoding.ASCII.GetBytes(name)); }
            writer.Write(0U); foreach (var block in blocks) writer.Write(block.Bytes); writer.Write(1U); writer.Write(0);
        });
    }
}
