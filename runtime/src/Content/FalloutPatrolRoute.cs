using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutPatrolPoint(FalloutFormKey Reference, float[] Position, float WaitSeconds,
    bool FaceHeading, FalloutIdleCollection? Idles, FalloutPackageEvent? Arrival, FalloutFormKey? ArrivalIdle);

internal sealed record FalloutPatrolRoute(FalloutFormKey Package, string SourceSha256,
    IReadOnlyList<FalloutPatrolPoint> Points, bool Circular, bool Repeatable, int Radius, bool Running, bool WeaponDrawn)
{
    internal static FalloutPatrolRoute Read(FalloutPluginStack records, FalloutPluginRecord package, FalloutFormKey actor)
    {
        if (package.Signature != "PACK") throw new InvalidDataException("Patrol source is not PACK.");
        var fields = package.ReadSubrecords().ToArray();
        var data = Required(fields, "PKDT", 12).Span;
        if (data[4] != 13) throw new InvalidDataException("Package procedure is not Patrol.");
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(data);
        const uint supported = 0x00800000 | 0x00002000 | 0x01000000;
        if ((flags & ~supported) != 0 || fields.Any(field => field.Signature is "PLD2" or "PTDT" or "PTD2"))
            throw new NotSupportedException("Patrol package flags or alternate targets need their procedure owner.");
        var location = Required(fields, "PLDT", 12).Span;
        var repeat = Required(fields, "PKPT", 2).Span[0];
        if (repeat > 1) throw new InvalidDataException("Patrol repeat flag is invalid.");
        var radius = BinaryPrimitives.ReadInt32LittleEndian(location[8..]);
        if (radius < 0) throw new InvalidDataException("Patrol radius is negative.");
        var first = BinaryPrimitives.ReadInt32LittleEndian(location) switch
        {
            0 => package.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(location[4..])),
            6 => Linked(records.GetEffective(actor)) ?? throw new NotSupportedException("Patrol actor has no linked start reference."),
            _ => throw new NotSupportedException("Patrol starting location is not a reference or linked reference.")
        };
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(package.ReadData()); hash.AppendData(Encoding.UTF8.GetBytes(first.ToString()));
        var points = new List<FalloutPatrolPoint>(); var visited = new HashSet<FalloutFormKey>();
        FalloutFormKey? next = first;
        while (next is { } key && visited.Add(key))
        {
            var point = records.GetEffective(key);
            if (point.Signature != "REFR" || point.IsDeleted) throw new InvalidDataException("Patrol point is not a live placed reference.");
            var rows = point.ReadSubrecords().ToArray();
            var position = Required(rows, "DATA", 24);
            var xyz = Enumerable.Range(0, 3).Select(index => BinaryPrimitives.ReadSingleLittleEndian(position.Span[(index * 4)..])).ToArray();
            if (xyz.Any(value => !float.IsFinite(value))) throw new InvalidDataException("Patrol point position is invalid.");
            var wait = rows.Any(row => row.Signature == "XPRD") ? BinaryPrimitives.ReadSingleLittleEndian(Required(rows, "XPRD", 4).Span) : 0;
            if (!float.IsFinite(wait) || wait < 0) throw new InvalidDataException("Patrol wait duration is invalid.");
            var baseRecord = records.GetEffective(FalloutDialogueTopic.RequiredForm(point, "NAME"));
            if (baseRecord.Signature != "IDLM" && !FalloutNewVegasBuiltinForms.IsInternalStatic(baseRecord.Signature, records.RuntimeFormId(baseRecord.FormKey)))
                throw new NotSupportedException("Patrol point requires its furniture or object interaction owner.");
            var marker = Array.FindIndex(rows, row => row.Signature == "XPPA");
            FalloutPackageEvent? arrival = null; FalloutFormKey? idle = null;
            if (marker >= 0)
            {
                if (rows.Count(row => row.Signature == "XPPA") != 1 || rows[marker].Data.Length != 0)
                    throw new InvalidDataException("Patrol arrival marker is invalid.");
                var eventFields = rows.Skip(marker + 1).TakeWhile(row => row.Signature is "INAM" or "SCHR" or "SCDA" or "SCTX" or "SCRO" or "SCRV" or "SLSD" or "SCVR" or "TNAM").ToArray();
                arrival = new(point, "XPPA", eventFields);
                if (eventFields.Any(row => row.Signature == "INAM")) idle = point.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(Required(eventFields, "INAM", 4).Span));
            }
            hash.AppendData(Encoding.UTF8.GetBytes(key.ToString())); hash.AppendData(point.ReadData()); hash.AppendData(baseRecord.ReadData());
            points.Add(new(key, xyz, wait, baseRecord.Signature == "IDLM" || records.RuntimeFormId(baseRecord.FormKey) is 0x32 or 0x34,
                baseRecord.Signature == "IDLM" && (flags & 0x01000000) == 0 ? FalloutIdleCollection.Read(baseRecord) : null, arrival, idle));
            next = Linked(point);
        }
        if (next is not null && next != first) throw new NotSupportedException("Patrol chain enters a cycle after its starting point.");
        return new(package.FormKey, Convert.ToHexString(hash.GetHashAndReset()), points, next == first,
            repeat == 1, radius, (flags & 0x2000) != 0, (flags & 0x800000) != 0);
    }

    internal static FalloutFormKey? Linked(FalloutPluginRecord record)
    {
        var links = record.ReadSubrecords().Where(field => field.Signature == "XLKR").ToArray();
        if (links.Length == 0) return null;
        if (links.Length != 1 || links[0].Data.Length != 4) throw new InvalidDataException("Patrol linked reference has an invalid extent/count.");
        return record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(links[0].Data.Span));
    }

    private static ReadOnlyMemory<byte> Required(FalloutPluginSubrecord[] fields, string name, int size)
    {
        var selected = fields.Where(field => field.Signature == name).ToArray();
        return selected.Length == 1 && selected[0].Data.Length == size ? selected[0].Data :
            throw new InvalidDataException($"Patrol has invalid {name} extent/count.");
    }

    internal FalloutPatrolProgress Start(Func<FalloutPatrolPoint, float> distance)
    {
        var index = Repeatable ? Enumerable.Range(0, Points.Count).MinBy(index => distance(Points[index])) : 0;
        return new(SourceSha256, index, 1, false, 0, false, false);
    }

    internal void Validate(FalloutPatrolProgress state)
    {
        state.Validate();
        if (!state.SourceSha256.Equals(SourceSha256, StringComparison.OrdinalIgnoreCase) || state.Index >= Points.Count ||
            state.RemainingSeconds > Points[state.Index].WaitSeconds || Circular && state.Direction != 1 ||
            state.Complete && (Repeatable || state.RemainingSeconds != 0) ||
            state.ReturningToStart && (Repeatable || !Circular || state.Index != 0))
            throw new InvalidDataException("Saved patrol progress differs from its winning route.");
    }

    internal FalloutPatrolProgress Advance(FalloutPatrolProgress state, bool arrived, double seconds)
    {
        Validate(state);
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (state.Complete) return state;
        if (!state.Arrived) return arrived ? state with { Arrived = true, RemainingSeconds = Points[state.Index].WaitSeconds } : state;
        var remaining = Math.Max(0, state.RemainingSeconds - seconds);
        if (remaining > 0) return state with { RemainingSeconds = remaining };
        if (!Repeatable && (Points.Count == 1 || state.ReturningToStart || !Circular && state.Index == Points.Count - 1))
            return state with { RemainingSeconds = 0, Complete = true };
        if (Points.Count == 1) return state with { RemainingSeconds = Points[0].WaitSeconds };
        var direction = state.Direction; var index = state.Index + direction;
        if (Circular) index %= Points.Count;
        else if (index < 0 || index >= Points.Count) { direction = -direction; index = state.Index + direction; }
        return state with
        {
            Index = index,
            Direction = direction,
            Arrived = false,
            RemainingSeconds = 0,
            ReturningToStart = !Repeatable && Circular && index == 0
        };
    }
}
