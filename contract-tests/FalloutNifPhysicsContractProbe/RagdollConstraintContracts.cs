using System.Text;
using OpenNV.Runtime.Formats.Gamebryo;

internal static class RagdollConstraintContracts
{
    internal static void Run()
    {
        foreach (var version in new uint[] { 32, 34 })
        foreach (var type in new[] { "bhkRagdollConstraint", "bhkLimitedHingeConstraint", "bhkMalleableConstraint" })
        {
            var payload = Payload(type);
            var parsed = Read(type, payload, version);
            Require(parsed.Header.EntityA == 0 && parsed.Header.EntityB == 1 && parsed.Header.Priority == 1,
                "Constraint changed the source body graph.");
            Require(parsed.TwistA == new FalloutNifVector3(1, 0, 0) && parsed.PlaneB == new FalloutNifVector3(0, 0, 1) &&
                parsed.PivotA == new FalloutNifVector3(.1f, .2f, .3f) && parsed.PivotB == new FalloutNifVector3(-.4f, .5f, -.6f),
                "Joint frames changed axes, pivots or padding interpretation.");
            Require(parsed.TwistMinimum == -.3f && parsed.TwistMaximum == .6f && parsed.Friction == 12 &&
                parsed.Strength == (type == "bhkMalleableConstraint" ? .8f : 1) &&
                parsed.Cone == (type == "bhkLimitedHingeConstraint" ? 0 : .9f), "Joint limits or malleable strength changed.");
            Reject(() => Read(type, payload[..^1], version));
            Reject(() => Read(type, [.. payload, 0], version));
            var motor = (byte[])payload.Clone(); motor[^(type == "bhkMalleableConstraint" ? 5 : 1)] = 1;
            Reject(() => Read(type, motor, version));
            var nonfinite = (byte[])payload.Clone();
            BitConverter.GetBytes(float.NaN).CopyTo(nonfinite, type == "bhkMalleableConstraint" ? 36 : 16);
            Reject(() => Read(type, nonfinite, version));
        }
        Console.WriteLine("OPENNV_RAGDOLL_CONSTRAINT_CONTRACT_PASS sourceFrames=true hinge=true ragdoll=true malleable=true padding=true malformed=true");
    }

    private static byte[] Payload(string type)
    {
        using var data = new MemoryStream(); using var writer = new BinaryWriter(data);
        writer.Write(2U); writer.Write(0); writer.Write(1); writer.Write(1U);
        if (type == "bhkMalleableConstraint")
        {
            writer.Write(7U); writer.Write(2U); writer.Write(-1); writer.Write(-1); writer.Write(1U);
        }
        void Vector(float x, float y, float z) { writer.Write(x); writer.Write(y); writer.Write(z); writer.Write(uint.MaxValue); }
        Vector(1, 0, 0); Vector(0, 1, 0); Vector(0, 0, 1); Vector(.1f, .2f, .3f);
        Vector(0, 1, 0); Vector(0, 0, 1); Vector(1, 0, 0); Vector(-.4f, .5f, -.6f);
        if (type != "bhkLimitedHingeConstraint") { writer.Write(.9f); writer.Write(-.2f); writer.Write(.4f); }
        writer.Write(-.3f); writer.Write(.6f); writer.Write(12f); writer.Write((byte)0);
        if (type == "bhkMalleableConstraint") writer.Write(.8f);
        return data.ToArray();
    }

    private static FalloutNifRagdollConstraint Read(string type, byte[] payload, uint version)
    {
        using var data = new MemoryStream(); using var writer = new BinaryWriter(data);
        writer.Write(Encoding.ASCII.GetBytes("Gamebryo File Format, Version 20.2.0.7\n"));
        writer.Write(FalloutNifFile.Version); writer.Write((byte)1); writer.Write(FalloutNifFile.UserVersion);
        writer.Write(3U); writer.Write(version); writer.Write(new byte[] { 1, 0, 1, 0, 1, 0 });
        writer.Write((ushort)2);
        foreach (var name in new[] { "bhkRigidBody", type }) { writer.Write(name.Length); writer.Write(Encoding.ASCII.GetBytes(name)); }
        writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)1);
        writer.Write(236); writer.Write(236); writer.Write(payload.Length);
        writer.Write(0U); writer.Write(0U); writer.Write(0U);
        writer.Write(new byte[472]); writer.Write(payload); writer.Write(1U); writer.Write(2);
        return FalloutNifFile.Read(data.ToArray()).ReadRagdollConstraint(2);
    }

    private static void Require(bool condition, string error) { if (!condition) throw new InvalidDataException(error); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Malformed or unsupported source joint was accepted.");
    }
}
