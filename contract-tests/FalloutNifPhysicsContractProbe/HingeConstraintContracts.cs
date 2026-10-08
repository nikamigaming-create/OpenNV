using System.Text;
using OpenNV.Runtime.Formats.Gamebryo;

internal static class HingeConstraintContracts
{
    internal static void Run()
    {
        foreach (var version in new uint[] { 32, 34 })
        {
            var payload = Payload();
            var hinge = Read(payload, version);
            if (hinge.Header.EntityA != 0 || hinge.Header.EntityB != 1 || hinge.Header.WrappedType != 1 ||
                hinge.PivotA != new FalloutNifVector3(.1f, .2f, .3f) || hinge.PivotB != new FalloutNifVector3(-.4f, .5f, -.6f) ||
                hinge.AxisA != new FalloutNifVector3(0, 0, 1) || hinge.AxisB != new FalloutNifVector3(0, 1, 0) ||
                hinge.Minimum is not null || hinge.Maximum is not null || hinge.Friction != 0)
                throw new InvalidDataException("Basic hinge changed body identity, local frames or freedom.");
            Reject(() => Read(payload[..^1], version));
            Reject(() => Read([.. payload, 0], version));
            var invalid = (byte[])payload.Clone(); BitConverter.GetBytes(0).CopyTo(invalid, 8);
            Reject(() => Read(invalid, version));
            invalid = (byte[])payload.Clone(); BitConverter.GetBytes(2f).CopyTo(invalid, 24);
            Reject(() => Read(invalid, version));
            invalid = (byte[])payload.Clone(); BitConverter.GetBytes(float.NaN).CopyTo(invalid, 64);
            Reject(() => Read(invalid, version));
        }
        Console.WriteLine("OPENNV_HINGE_CONSTRAINT_CONTRACT_PASS frames=true padding=true freedoms=true malformed=true");
    }

    private static byte[] Payload()
    {
        using var data = new MemoryStream(); using var writer = new BinaryWriter(data);
        writer.Write(2U); writer.Write(0); writer.Write(1); writer.Write(1U);
        void Vector(float x, float y, float z) { writer.Write(x); writer.Write(y); writer.Write(z); writer.Write(uint.MaxValue); }
        Vector(0, 0, 1); Vector(0, 1, 0); Vector(-1, 0, 0); Vector(.1f, .2f, .3f);
        Vector(0, 1, 0); Vector(0, 0, 1); Vector(1, 0, 0); Vector(-.4f, .5f, -.6f);
        return data.ToArray();
    }

    private static FalloutNifHingeConstraint Read(byte[] payload, uint version)
    {
        using var data = new MemoryStream(); using var writer = new BinaryWriter(data);
        writer.Write(Encoding.ASCII.GetBytes("Gamebryo File Format, Version 20.2.0.7\n"));
        writer.Write(FalloutNifFile.Version); writer.Write((byte)1); writer.Write(FalloutNifFile.UserVersion);
        writer.Write(3U); writer.Write(version); writer.Write(new byte[] { 1, 0, 1, 0, 1, 0 });
        writer.Write((ushort)2);
        foreach (var name in new[] { "bhkRigidBody", "bhkHingeConstraint" }) { writer.Write(name.Length); writer.Write(Encoding.ASCII.GetBytes(name)); }
        writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)1);
        writer.Write(236); writer.Write(236); writer.Write(payload.Length);
        writer.Write(0U); writer.Write(0U); writer.Write(0U);
        writer.Write(new byte[472]); writer.Write(payload); writer.Write(1U); writer.Write(2);
        return FalloutNifFile.Read(data.ToArray()).ReadHingeConstraint(2);
    }

    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Malformed hinge was admitted.");
    }
}
