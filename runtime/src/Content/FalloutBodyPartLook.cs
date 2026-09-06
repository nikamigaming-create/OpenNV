using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

// The body-part's IK-data and head-tracking flags select the target node.
// Its cone is authored in degrees. Other BPTD combat/dismemberment fields are
// deliberately outside this reader's contract.
internal sealed record FalloutBodyPartLook(FalloutFormKey Source, byte BodyPart, string TargetNode, float ConeDegrees)
{
    internal float ConeRadians => (float)(ConeDegrees * (Math.PI / 180));

    internal static FalloutBodyPartLook? Read(FalloutPluginRecord record)
    {
        if (record.Signature != "BPTD") throw new InvalidDataException("Look rig source is not BPTD.");
        return Read(record.FormKey, record.ReadSubrecords());
    }

    internal static FalloutBodyPartLook? Read(FalloutFormKey source, IEnumerable<FalloutPluginSubrecord> fields)
    {
        var parts = new Dictionary<byte, FalloutBodyPartLook?>();
        var current = new List<FalloutPluginSubrecord>();
        void Finish()
        {
            if (current.Count == 0) return;
            var declarations = current.Where(field => field.Signature == "BPND").ToArray();
            if (declarations.Length != 1 || declarations[0].Data.Length != 84)
                throw new NotSupportedException("Body-part look declaration requires the admitted BPND extent.");
            var data = declarations[0].Data.Span;
            var part = data[5];
            if (part >= 15 || parts.ContainsKey(part))
                throw new InvalidDataException("Body-part slot is outside the source table or repeated.");
            FalloutBodyPartLook? look = null;
            if ((data[4] & 0x22) == 0x22)
            {
                var names = current.Where(field => field.Signature == "BPNT").ToArray();
                if (names.Length != 1) throw new InvalidDataException("Head tracking requires one authored target node.");
                var name = FalloutDialogueTopic.Text(names[0].Data.Span);
                var cone = BinaryPrimitives.ReadSingleLittleEndian(data[20..]);
                if (string.IsNullOrWhiteSpace(name) || !float.IsFinite(cone) || cone is < 0 or > 180)
                    throw new InvalidDataException("Head-tracking node or cone is invalid.");
                look = new(source, part, name, cone);
            }
            parts.Add(part, look);
            current.Clear();
        }
        foreach (var field in fields)
        {
            // A part's display name is optional. BPND binds its preceding
            // names; using BPTN as a mandatory delimiter drops unnamed parts.
            if (field.Signature is "BPTN" or "BPNN" or "BPNT" or "BPND") current.Add(field);
            if (field.Signature == "BPND") Finish();
        }
        Finish();
        // Native selection walks the body-part table, rather than file order,
        // and binds the first eligible tracking part.
        return parts.OrderBy(part => part.Key).Select(part => part.Value).FirstOrDefault(part => part is not null);
    }
}

internal sealed record FalloutLookSettings(float MinimumDistance, float MaximumDistance,
    float MaximumStepDegrees, float EasingStepDegrees, float EasingStopDegrees)
{
    internal static FalloutLookSettings Read(FalloutInstallationSettings settings) => Read(settings.Number);

    internal static FalloutLookSettings Read(Func<string, float> number)
    {
        var value = new FalloutLookSettings(number("fMinTrackingDist:LookIK"), number("fMaxTrackingDist:LookIK"),
            number("fAngleMax:LookIK"), number("fAngleMaxEase:LookIK"), number("fEaseAngleShutOff:LookIK"));
        if (!float.IsFinite(value.MinimumDistance) || !float.IsFinite(value.MaximumDistance) ||
            value.MinimumDistance < 0 || value.MaximumDistance < value.MinimumDistance ||
            !float.IsFinite(value.MaximumStepDegrees) || value.MaximumStepDegrees is < 0 or > 180 ||
            !float.IsFinite(value.EasingStepDegrees) || value.EasingStepDegrees is < 0 or > 180 ||
            !float.IsFinite(value.EasingStopDegrees) || value.EasingStopDegrees is < 0 or > 180)
            throw new InvalidDataException("Source LookIK limits are invalid.");
        return value;
    }
}
