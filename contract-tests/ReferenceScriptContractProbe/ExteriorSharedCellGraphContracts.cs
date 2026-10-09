using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class ExteriorSharedCellGraphContracts
{
    private const string Selection = "authored-exterior-shared-cell-graph";
    private static readonly FalloutFormKey World = Key(0x400), Persistent = Key(0x401), A = Key(0x500), B = Key(0x501), C = Key(0x502);
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-shared-cell-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "Grid.esm"), Plugin());
            File.WriteAllBytes(Path.Combine(directory, "GridPatch.esp"), Patch());
            using var records = FalloutPluginStack.Load(directory, ["Grid.esm", "GridPatch.esp"]);
            foreach (var image in new[] { "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57",
                "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e" })
                Graph(records, image);
        }
        finally
        {
            File.Delete(Path.Combine(directory, "GridPatch.esp")); File.Delete(Path.Combine(directory, "Grid.esm")); Directory.Delete(directory);
        }
        Console.WriteLine("OPENNV_EXTERIOR_SHARED_CELL_GRAPH_CONTRACT_PASS actualReaders=true sameRootOverlap=true exactCellEpochs=true perCellLand=true selectedOverride=true publishAfterRetirement=true preparedCancellation=true historicalIdsNotCurrent=true coldCurrentGraphOnly=true missingCellAndAliasNegatives=true failedPrefixRetained=true native=unexecuted");
    }
    private static void Graph(FalloutPluginStack records, string image)
    {
        var grid = new FalloutExteriorGrid(records);
        var before = grid.Resolve(World, Persistent, .5f * 4096, .5f * 4096, 3);
        var after = grid.Resolve(World, Persistent, 2.5f * 4096, .5f * 4096, 3);
        var declaration = FalloutCellProcessDeclaration.ForExecutable(image);
        var owner = new FalloutCellProcesses(declaration, records, Selection);
        var lease = owner.BeginAttachment(before.Scene, Cells(before), 100);
        Complete(owner, lease, 1000); owner.PublishRoot(lease, 100, "authored-root-publication");
        var old = owner.ReadAttachment(lease);
        Require(owner.ReadNativeCellSource(B).Landscape == Key(0x601) &&
            FalloutLandscapeTransportResolver.ResolveCell(records, before.Cells.Single(cell => cell.FormKey == B)).Heights[0] == 47,
            "Per-CELL terrain used a loser or borrowed another CELL's LAND.");
        owner.BeginSharedGraph(lease, 100, before.Scene, after.Scene, Cells(after));
        Require(owner.ReadPhase(B).Require() == 6 && owner.ReadPhase(C).Require() == 5 && owner.ReadPhase(A).Require() == 6 &&
            owner.ReadAttachment(lease).CellEpochs[B] == old.CellEpochs[B] && !owner.ReadAttachment(lease).RootPublished && owner.SaveBlocker is not null,
            "Preparation retired overlap or certified the target from root residency.");
        Complete(owner, lease, 3000);
        owner.BeginSharedGraphPublication(lease, 100, after.Scene);
        RetireDelta(owner, lease);
        owner.CompleteSharedGraph(lease, 100); owner.ReleaseSource(A);
        var current = owner.ReadAttachment(lease);
        Require(current.NativeRoot == 100 && current.RootPublished && !current.Retired &&
            current.Children.Single(child => child.Source.Reference == Key(0x801)).NativeObjects.SequenceEqual(
                old.Children.Single(child => child.Source.Reference == Key(0x801)).NativeObjects) &&
            current.CellConsumers.Single(cell => cell.Source.Cell.Cell == B).NativeObjects.SequenceEqual(
                old.CellConsumers.Single(cell => cell.Source.Cell.Cell == B).NativeObjects) &&
            owner.ReadPhase(A).Require() == 0 && owner.ReadPhase(C).Require() == 6 && owner.SaveBlocker is null,
            "Shared publication replaced retained native owners or lost actual outgoing release.");
        var saved = RoundTrip(owner.Capture()); FalloutCellProcesses.RequireCommittedSnapshot(saved);
        Reject(() => new FalloutCellProcesses(declaration, records, Selection, saved with
        { Attachments = saved.Attachments.Select(value => value with { CellConsumers = value.CellConsumers.Skip(1).ToArray() }).ToArray() }));
        Reject(() => new FalloutCellProcesses(declaration, records, Selection, saved with
        { Attachments = saved.Attachments.Select(value => value with { CellConsumers = value.CellConsumers.Select(cell => cell.Source.Landscape is null ? cell : cell with { NativeObjects = [100] }).ToArray() }).ToArray() }));
        Reject(() => new FalloutCellProcesses(declaration, records, Selection, saved with
        { SharedGraphs = saved.SharedGraphs.Select(change => change with { DestroyedReferences = change.DestroyedReferences.Append(Key(0x801)).ToArray() }).ToArray() }));
        Reject(() => new FalloutCellProcesses(declaration, records, Selection, saved with
        { SharedGraphs = saved.SharedGraphs.Select(change => change with { TargetCellSources = change.TargetCellSources.Select(source => source with { TransportSha256 = new string('e', 64) }).ToArray() }).ToArray() }));
        Reject(() => new FalloutCellProcesses(declaration, records, Selection, saved with
        { SharedGraphs = saved.SharedGraphs.Select(change => change with { BeforeCellConsumers = change.BeforeCellConsumers.Select(cell => cell.Source.Landscape is null ? cell : cell with { NativeObjects = [change.NativeRoot] }).ToArray() }).ToArray() }));

        var cold = new FalloutCellProcesses(declaration, records, Selection, saved);
        Require(cold.ReadPhase(B).Value is null && cold.Capture().Attachments.Single().CellConsumers.All(cell => cell.NativeObjects.Count == 0) &&
            cold.Capture().SharedGraphs.Single().NativeRoot == 100,
            "Cold import reused historical native resources as current authority.");
        cold.BeginColdAttachment(lease, after.Scene, Cells(after), 200);
        Complete(cold, lease, 5000); cold.PublishRoot(lease, 200, "authored-cold-root-publication");
        Require(cold.ReadAttachment(lease).CellConsumers.Where(cell => cell.Source.Landscape is not null).All(cell => cell.NativeObjects[0] >= 6000),
            "Cold root rebuilt more than the committed current graph or borrowed old terrain IDs.");
        using (var twice = new FalloutCellProcesses(declaration, records, Selection, RoundTrip(cold.Capture())))
            Require(twice.SaveBlocker is not null && twice.Capture().Attachments.Single().NativeRoot == 0,
                "A second cold epoch treated a source attachment receipt as actual native completion.");

        owner.BeginSharedGraph(lease, 100, after.Scene, before.Scene, Cells(before)); Complete(owner, lease, 7000);
        owner.BeginSharedGraphCancellation(lease, 100); RetireDelta(owner, lease); owner.CompleteSharedGraph(lease, 100); owner.ReleaseSource(A);
        Require(owner.ReadAttachment(lease).Children.Select(child => child.Source).SequenceEqual(current.Children.Select(child => child.Source)) &&
            owner.Capture().SharedGraphs.Last().Phase == FalloutCellSharedGraphPhase.Cancelled && owner.SaveBlocker is null,
            "Prepared cancellation retired a retained actor or silently published the unused target.");
        using (var cancelledCold = new FalloutCellProcesses(declaration, records, Selection, RoundTrip(owner.Capture())))
            Require(cancelledCold.Capture().Attachments.Single().CellEpochs.Keys.ToHashSet().SetEquals(Cells(after)),
                "Cold cancellation reconstructed the abandoned grid.");

        var failed = new FalloutCellProcesses(declaration, records, Selection);
        var failedLease = failed.BeginAttachment(before.Scene, Cells(before), 900);
        Complete(failed, failedLease, 9000); failed.PublishRoot(failedLease, 900, "authored-first-root");
        failed.BeginSharedGraph(failedLease, 900, before.Scene, after.Scene, Cells(after));
        Reject(() => failed.PublishRoot(failedLease, 900, "wrong-root-only-publication"));
        Require(failed.SaveBlocker is not null && failed.ReadPhase(C).Value == 5,
            "Early publication erased the source/native entered prefix.");
        RetireRoot(failed, failedLease, 900);
        Require(failed.Capture().SharedGraphs.Single().Phase == FalloutCellSharedGraphPhase.RootRetired && failed.SaveBlocker is not null,
            "Failed root teardown fabricated successful grid publication or erased the original failure.");
        failed.Dispose(); RetireRoot(owner, lease, 100); owner.Dispose(); RetireRoot(cold, lease, 200); cold.Dispose();
    }
    private static FalloutFormKey[] Cells(FalloutExteriorGridScene grid) => grid.Cells.Select(cell => cell.FormKey).Append(grid.PersistentCell).ToArray();
    private static void Complete(FalloutCellProcesses owner, Guid lease, ulong first)
    {
        var identity = first;
        foreach (var child in owner.ReadAttachment(lease).Children.Where(child => child.Phase == FalloutCellProcessChildPhase.Pending))
            owner.CompleteChild(lease, child.Source, FalloutCellProcessChildPhase.Published, [identity++], "authored-reference-protocol");
        identity = first + 1000;
        foreach (var cell in owner.ReadAttachment(lease).CellConsumers.Where(cell => cell.Phase == FalloutCellProcessChildPhase.Pending))
            owner.CompleteNativeCell(lease, cell.Source.Cell.Cell, cell.Source, [identity++], "authored-terrain-protocol");
    }
    private static void RetireDelta(FalloutCellProcesses owner, Guid lease)
    {
        var change = owner.ReadSharedGraph(lease)!; var cancelled = change.Phase == FalloutCellSharedGraphPhase.Cancelling;
        var references = cancelled ? change.TargetChildren.Select(child => child.Source.Reference).Except(change.BeforeChildren.Select(child => child.Source.Reference)) :
            change.BeforeChildren.Select(child => child.Source.Reference).Except(change.TargetChildren.Select(child => child.Source.Reference));
        var cells = cancelled ? change.AfterEpochs.Keys.Except(change.BeforeEpochs.Keys) : change.BeforeEpochs.Keys.Except(change.AfterEpochs.Keys);
        foreach (var reference in references) owner.RetireSharedChild(lease, reference, "authored-consumers-gone-protocol");
        foreach (var cell in cells) owner.RetireNativeCell(lease, cell, "authored-terrain-gone-protocol");
    }
    private static void RetireRoot(FalloutCellProcesses owner, Guid lease, ulong root)
    {
        Require(owner.BeginDetach(lease, root, "authored-root-detach"), "Root destruction lost the exact source phase guard.");
        foreach (var child in owner.ReadAttachment(lease).Children) owner.RetireChild(lease, child.Source, "authored-child-gone-protocol");
        foreach (var cell in owner.ReadAttachment(lease).CellConsumers) owner.RetireNativeCell(lease, cell.Source.Cell.Cell, "authored-terrain-gone-protocol");
        owner.CompleteDetach(lease, root, "authored-root-gone-protocol");
    }
    private static byte[] Plugin() => Join(Record("TES4", 0, 1, Field("HEDR", new byte[12])),
        Record("WRLD", 0x400, 0, Field("DATA", [1]), Field("DNAM", Pair(15, 0))),
        Record("MISC", 0x700, 0, Field("MODL", Text("authored\\shared.nif"))),
        Record("LTEX", 0x710, 0, Field("TNAM", U32(0x711))), Record("TXST", 0x711, 0, Field("TX00", Text("authored\\land.dds"))),
        Group(0x400, 1, Record("CELL", 0x401, 0x400, Field("DATA", [0, 0])),
            Cell(0x500, 0x600, 0x800, 0), Cell(0x501, 0x601, 0x801, 1), Cell(0x502, 0x602, 0x802, 2)));
    private static byte[] Patch() => Join(Record("TES4", 0, 0, Field("HEDR", new byte[12]), Field("MAST", Text("Grid.esm")), Field("DATA", new byte[8])),
        Group(0x400, 1, Group(0x501, 6, Landscape(0x601, 47))));
    private static byte[] Cell(uint cell, uint land, uint reference, int x) => Join(
        Record("CELL", cell, 0, Field("DATA", [0, 0]), Field("XCLC", Coordinates(x, 0))),
        Group(cell, 6, Landscape(land, 15), Reference(reference, x)));
    private static byte[] Landscape(uint id, float height)
    {
        var heights = new byte[1096]; BinaryPrimitives.WriteSingleLittleEndian(heights, height / 8);
        var normals = new byte[3267]; for (var i = 2; i < normals.Length; i += 3) normals[i] = 127;
        var fields = new List<byte[]> { Field("DATA", U32(1)), Field("VHGT", heights), Field("VNML", normals) };
        for (byte q = 0; q < 4; q++) { var layer = new byte[8]; U32(0x710).CopyTo(layer, 0); layer[4] = q; fields.Add(Field("BTXT", layer)); }
        return Record("LAND", id, 0, Join(fields.ToArray()));
    }
    private static byte[] Reference(uint id, int x)
    { var data = new byte[24]; BinaryPrimitives.WriteSingleLittleEndian(data, (x + .5f) * 4096); return Record("REFR", id, 0, Field("NAME", U32(0x700)), Field("DATA", data)); }
    private static byte[] Coordinates(int x, int y) { var data = new byte[8]; BinaryPrimitives.WriteInt32LittleEndian(data, x); BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), y); return data; }
    private static byte[] Pair(float x, float y) { var data = new byte[8]; BinaryPrimitives.WriteSingleLittleEndian(data, x); BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(4), y); return data; }
    private static byte[] U32(uint value) => BitConverter.GetBytes(value);
    private static byte[] Text(string value) => Encoding.UTF8.GetBytes(value + '\0');
    private static byte[] Record(string signature, uint id, uint flags, params byte[][] fields)
    { var data = Join(fields); var value = new byte[data.Length + 24]; Encoding.ASCII.GetBytes(signature).CopyTo(value, 0); BinaryPrimitives.WriteUInt32LittleEndian(value.AsSpan(4), (uint)data.Length); BinaryPrimitives.WriteUInt32LittleEndian(value.AsSpan(8), flags); BinaryPrimitives.WriteUInt32LittleEndian(value.AsSpan(12), id); data.CopyTo(value, 24); return value; }
    private static byte[] Group(uint label, int type, params byte[][] rows)
    { var data = Join(rows); var value = new byte[data.Length + 24]; "GRUP"u8.CopyTo(value); BinaryPrimitives.WriteUInt32LittleEndian(value.AsSpan(4), (uint)value.Length); BinaryPrimitives.WriteUInt32LittleEndian(value.AsSpan(8), label); BinaryPrimitives.WriteInt32LittleEndian(value.AsSpan(12), type); data.CopyTo(value, 24); return value; }
    private static byte[] Field(string name, byte[] data)
    { var value = new byte[data.Length + 6]; Encoding.ASCII.GetBytes(name).CopyTo(value, 0); BinaryPrimitives.WriteUInt16LittleEndian(value.AsSpan(4), (ushort)data.Length); data.CopyTo(value, 6); return value; }
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static FalloutFormKey Key(uint id) => new("Grid.esm", id);
    private static T RoundTrip<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static void Require(bool value, string error) { if (!value) throw new InvalidDataException(error); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or NotSupportedException or KeyNotFoundException) { return; }
        throw new InvalidDataException("Foreign/unfinished/missing CELL/native graph was admitted.");
    }
}
