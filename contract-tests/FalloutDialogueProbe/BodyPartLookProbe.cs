using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;

internal static class BodyPartLookProbe
{
    internal static void Run()
    {
        var source = new FalloutFormKey("Synthetic.esm", 0x7788);
        FalloutBodyPartLook? Read(params FalloutPluginSubrecord[] fields) => FalloutBodyPartLook.Read(source, fields);
        var one = Part(4, 0x22, "AimJoint", 37.5f);
        var selected = Read(one) ?? throw new Exception("Tracking part was not selected.");
        if (selected.Source != source || selected.BodyPart != 4 || selected.TargetNode != "AimJoint" ||
            selected.ConeDegrees != 37.5f || selected.ConeRadians != (float)(37.5 * Math.PI / 180))
            throw new Exception("Body-part look source binding changed.");
        if (Read(Part(4, 0x20, "AimJoint", 37.5f)) is not null || Read(Part(4, 2, "AimJoint", 37.5f)) is not null)
            throw new Exception("Both source tracking flags are required.");
        if (Read(one.Concat(Part(1, 0x22, "OtherJoint", 11)).ToArray())?.TargetNode != "OtherJoint")
            throw new Exception("Tracking selection followed file order rather than body-part slots.");
        if (Read(Part(4, 0x22, "OverriddenJoint", 22))?.TargetNode != "OverriddenJoint")
            throw new Exception("Authored head node was replaced by a fixed name.");
        if (Read(one.Where(field => field.Signature != "BPTN").ToArray()) != selected)
            throw new Exception("An optional display name changed head tracking.");
        Reject(() => Read(one.Concat(one).ToArray()));
        Reject(() => Read(Part(15, 0x22, "AimJoint", 37.5f)));
        Reject(() => Read(Part(4, 0x22, "", 37.5f)));
        Reject(() => Read(Part(4, 0x22, "AimJoint", float.NaN)));
        Reject(() => Read(one.Where(field => field.Signature != "BPNT").ToArray()));
        Reject(() => Read([.. one, new("BPNT", Encoding.ASCII.GetBytes("Duplicate\0"))]));
        Reject(() => Read(new FalloutPluginSubrecord("BPNT", Encoding.ASCII.GetBytes("Orphan\0"))));
        var truncated = one.ToArray();
        truncated[^1] = new("BPND", truncated[^1].Data[..83]);
        Reject(() => Read(truncated));
        var values = new Dictionary<string, float>
        {
            ["fMinTrackingDist:LookIK"] = 17,
            ["fMaxTrackingDist:LookIK"] = 921,
            ["fAngleMax:LookIK"] = 4.25f,
            ["fAngleMaxEase:LookIK"] = 0.75f,
            ["fEaseAngleShutOff:LookIK"] = 0.125f,
        };
        if (FalloutLookSettings.Read(key => values[key]) != new FalloutLookSettings(17, 921, 4.25f, 0.75f, 0.125f))
            throw new Exception("Look limits did not follow their source identities.");
        values["fMaxTrackingDist:LookIK"] = 16;
        Reject(() => FalloutLookSettings.Read(key => values[key]));
        Console.WriteLine("Body-part look selection, source node/cone and settings contracts passed.");
    }

    private static FalloutPluginSubrecord[] Part(byte part, byte flags, string target, float cone)
    {
        var data = new byte[84];
        data[4] = flags; data[5] = part;
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(20), cone);
        return [new("BPTN", Encoding.ASCII.GetBytes("Synthetic part\0")),
            new("BPNN", Encoding.ASCII.GetBytes("DifferentSeverJoint\0")),
            new("BPNT", Encoding.ASCII.GetBytes(target + "\0")), new("BPND", data)];
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new Exception("Invalid body-part look declaration was admitted.");
    }
}
