using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class PatrolContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-patrol-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
            File.WriteAllBytes(Path.Combine(directory, "Patrol.esm"), Join(Record("TES4", 0, Field("HEDR", header)),
                Record("STAT", 0x3b), Package(1, 10), Package(2, 20), Package(3, 20, false), Package(4, 10, false),
                Point(10, 11, 0), Point(11, 12, 10), Point(12, null, 20),
                Point(20, 21, 0), Point(21, 22, 10), Point(22, 20, 20)));
            using var records = FalloutPluginStack.Load(directory, ["Patrol.esm"]);
            FalloutPatrolRoute Route(uint id) => FalloutPatrolRoute.Read(records, records.GetEffective(new("Patrol.esm", id)), new("Patrol.esm", 99));
            var linear = Route(1); var circular = Route(2);
            var progress = linear.Start(point => Math.Abs(point.Position[0] - 11));
            Require(progress.Index == 1 && linear.WeaponDrawn && !linear.Circular, "Closest point/source flags lost.");
            Require(linear.Advance(progress, false, 100) == progress, "Time moved an actor that never arrived.");
            progress = linear.Advance(progress, true, .1);
            progress = linear.Advance(progress, true, 1.5);
            var cold = JsonSerializer.Deserialize<FalloutPatrolProgress>(JsonSerializer.Serialize(progress))!;
            Require(cold.RemainingSeconds == .5 && cold.Index == 1 && cold.Arrived, "Cold wait lost.");
            progress = linear.Advance(cold, true, .5);
            Require(progress.Index == 2 && !progress.Arrived, "Wait did not release to next point.");
            progress = Step(linear, progress);
            Require(progress.Index == 1 && progress.Direction == -1, "Linear route did not reverse.");
            progress = Step(linear, progress); progress = Step(linear, progress);
            Require(progress.Index == 1 && progress.Direction == 1, "Linear return did not reverse at start.");
            var loop = circular.Start(point => -point.Position[0]);
            Require(Step(circular, loop).Index == 0, "Circular route did not wrap.");
            var once = Route(3); var one = once.Start(_ => -99);
            Require(one.Index == 0, "Nonrepeatable route skipped its first point.");
            one = Step(once, one); one = Step(once, one); one = Step(once, one);
            Require(one.Index == 0 && one.ReturningToStart && !one.Complete, "Nonrepeatable loop did not return to start.");
            Require(Step(once, one).Complete, "Nonrepeatable loop did not finish on arrival.");
            var singlePass = Route(4); var finish = singlePass.Start(_ => 0);
            finish = Step(singlePass, finish); finish = Step(singlePass, finish); finish = Step(singlePass, finish);
            Require(finish.Complete && finish.Index == 2, "Linear nonrepeatable route did not finish.");
            Reject(() => linear.Validate(cold with { SourceSha256 = new('0', 64) }));
            Reject(() => linear.Validate(cold with { Index = 9 }));
            Reject(() => linear.Validate(cold with { RemainingSeconds = double.NaN }));
            Reject(() => linear.Validate(cold with { RemainingSeconds = 3 }));
            Reject(() => linear.Validate(cold with { Direction = 0 }));
            Console.WriteLine("OPENNV_PATROL_CONTRACT_PASS linkedRoute=true nearest=true arrivalWait=true linearReverse=true circular=true nonrepeat=true coldWait=true changedSourceRejected=true");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static FalloutPatrolProgress Step(FalloutPatrolRoute route, FalloutPatrolProgress progress) =>
        route.Advance(route.Advance(progress, true, .1), true, 2);
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Malformed patrol state accepted.");
    }
    private static byte[] Package(uint id, uint first, bool repeat = true)
    {
        var data = new byte[12]; data[4] = 13; BinaryPrimitives.WriteUInt32LittleEndian(data, 0x800000);
        var location = new byte[12]; BinaryPrimitives.WriteUInt32LittleEndian(location.AsSpan(4), first);
        return Record("PACK", id, Field("PKDT", data), Field("PLDT", location), Field("PKPT", [repeat ? (byte)1 : (byte)0, 0]));
    }
    private static byte[] Point(uint id, uint? next, float x)
    {
        var position = new byte[24]; BinaryPrimitives.WriteSingleLittleEndian(position, x);
        return Record("REFR", id, Field("NAME", BitConverter.GetBytes(0x3bu)), Field("DATA", position),
            Field("XPRD", BitConverter.GetBytes(2f)), next.HasValue ? Field("XLKR", BitConverter.GetBytes(next.Value)) : []);
    }
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24); return bytes;
    }
}
