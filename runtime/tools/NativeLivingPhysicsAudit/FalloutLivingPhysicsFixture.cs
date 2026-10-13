using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Formats.Gamebryo;

// Independent mathematical spheres, joints and arbitrary authored scene names.
internal static class FalloutLivingPhysicsFixture
{
    internal static byte[] Create()
    {
        var blocks = new List<(string Type, byte[] Bytes)>
        {
            ("NiNode", Node(0, -1, [1, -1, 2, 3])),
            ("NiNode", Node(1, 4, [])), ("NiNode", Node(2, 5, [])), ("NiNode", Node(3, 6, [])),
            ("bhkCollisionObject", Collision(1, 8)), ("bhkCollisionObject", Collision(2, 9)), ("bhkCollisionObject", Collision(3, 10)),
            ("bhkSphereShape", Bytes(writer => { writer.Write(0U); writer.Write(.1f); })),
            ("bhkRigidBody", Body(7, 7, [11])), ("bhkRigidBody", Body(7, 1, [11, -1, 12, 13, 14])),
            ("bhkRigidBody", Body(7, 1, [12, 13, 14])),
            ("bhkHingeConstraint", Constraint(8, 9, 1)), ("bhkHingeConstraint", Constraint(9, 10, 1)),
            ("bhkHingeConstraint", Constraint(9, 10, 3)), ("bhkHingeConstraint", Constraint(9, 10, 1)),
        };
        return Bytes(writer =>
        {
            writer.Write("Gamebryo File Format, Version 20.2.0.7\n"u8); writer.Write(FalloutNifFile.Version);
            writer.Write((byte)1); writer.Write(FalloutNifFile.UserVersion); writer.Write(blocks.Count); writer.Write(34U);
            writer.Write(new byte[] { 1, 0, 1, 0, 1, 0 });
            var types = blocks.Select(row => row.Type).Distinct().ToArray(); writer.Write((ushort)types.Length);
            foreach (var type in types) { writer.Write(type.Length); writer.Write(Encoding.ASCII.GetBytes(type)); }
            foreach (var block in blocks) writer.Write((ushort)Array.IndexOf(types, block.Type));
            foreach (var block in blocks) writer.Write(block.Bytes.Length);
            string[] names = ["AuthoredRoot", "FixedAnchor", "ArbitraryBody", "OtherBody"];
            writer.Write(names.Length); writer.Write(names.Max(name => name.Length));
            foreach (var name in names) { writer.Write(name.Length); writer.Write(Encoding.ASCII.GetBytes(name)); }
            writer.Write(0U); foreach (var block in blocks) writer.Write(block.Bytes); writer.Write(1U); writer.Write(0);
        });
    }
    private static byte[] Node(int name, int collision, int[] children) => Bytes(writer =>
    {
        writer.Write(name); writer.Write(0U); writer.Write(-1); writer.Write(14U);
        foreach (var value in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1 }) writer.Write(value);
        writer.Write(0U); writer.Write(collision); writer.Write(children.Length);
        foreach (var child in children) writer.Write(child); writer.Write(0U);
    });
    private static byte[] Collision(int node, int body) => Bytes(writer => { writer.Write(node); writer.Write((ushort)1); writer.Write(body); });
    private static byte[] Body(int shape, byte motion, int[] constraints)
    {
        var bytes = new byte[236 + constraints.Length * sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, shape); bytes[4] = 8;
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(80), 1);
        foreach (var offset in new[] { 116, 136, 156 }) BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), 1);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(180), motion == 7 ? 0 : 2);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(192), .6f);
        bytes[212] = motion; BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(228), constraints.Length);
        for (var index = 0; index < constraints.Length; index++) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(232 + index * sizeof(int)), constraints[index]);
        return bytes;
    }
    private static byte[] Constraint(int first, int second, uint priority) => Bytes(writer =>
    {
        writer.Write(2U); writer.Write(first); writer.Write(second); writer.Write(priority);
        foreach (var value in new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0,
            1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0 }) writer.Write(value);
    });
    private static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        write(writer); return stream.ToArray();
    }
}
