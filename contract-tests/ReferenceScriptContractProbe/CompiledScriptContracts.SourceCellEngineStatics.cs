using System.Buffers.Binary;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class CompiledScriptContracts
{
    private static void RunSourceCellEngineStaticsContracts()
    {
        var ids = new[] { FalloutDynamicPrimitiveSource.RoomMarkerRuntimeId, FalloutDynamicPrimitiveSource.PlaneMarkerRuntimeId,
            FalloutDynamicPrimitiveSource.PortalMarkerRuntimeId, FalloutDynamicPrimitiveSource.CollisionMarkerRuntimeId };
        foreach (var variant in new[] { "valid", "existing-base", "unknown-base", "missing-primitive", "malformed-primitive" })
        {
            var directory = Path.Combine(Path.GetTempPath(), "opennv-cell-engine-static-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var inputs = ids.Select((id, ordinal) =>
                {
                    var basis = ordinal == 0 && variant == "unknown-base" ? 0x22u : id;
                    var primitive = new byte[variant == "malformed-primitive" && ordinal == 0 ? 31 : 32];
                    BinaryPrimitives.WriteSingleLittleEndian(primitive, 2);
                    BinaryPrimitives.WriteSingleLittleEndian(primitive.AsSpan(sizeof(float)), 3);
                    BinaryPrimitives.WriteSingleLittleEndian(primitive.AsSpan(2 * sizeof(float)), 4);
                    if (primitive.Length == 32) UInt(primitive, 28,
                        id is FalloutDynamicPrimitiveSource.PlaneMarkerRuntimeId or FalloutDynamicPrimitiveSource.PortalMarkerRuntimeId ? 3u : 1u);
                    return Record("REFR", 0xa10u + (uint)ordinal, Field("NAME", U32(basis)), Field("DATA", new byte[24]),
                        variant == "missing-primitive" && ordinal == 0 ? [] : Field("XPRM", primitive));
                }).ToArray();
                UInt(inputs[0], 8, 0x400);
                var bases = variant == "existing-base" ? Join(
                    Record("STAT", FalloutDynamicPrimitiveSource.RoomMarkerRuntimeId, Field("EDID", Text("AuthoredRoomBase"))),
                    Record("STAT", FalloutDynamicPrimitiveSource.CollisionMarkerRuntimeId, Field("EDID", Text("AuthoredCollisionBase")))) : [];
                File.WriteAllBytes(Path.Combine(directory, "Bytecode.esm"), Join(Tes4(), bases, Record("CELL", 0x800, Field("DATA", [1])),
                    SourceCellGroup(6, 0x800, SourceCellGroup(8, 0x800, inputs[0]), SourceCellGroup(9, 0x800, inputs[1..]))));
                using var records = FalloutPluginStack.Load(directory, ["Bytecode.esm"]);
                var cellSource = new FalloutCellProcessSource(records);
                if (variant == "missing-primitive") { Reject(() => cellSource.Read(Key(0x800))); continue; }
                var cell = cellSource.Read(Key(0x800));
                var rows = cell.References.ToDictionary(row => row.Reference, FalloutFormKeyComparer.Instance);
                using var reader = new FalloutSourceCellReferenceIngestion(FalloutSourceCellLoaderDeclaration.Read(FalloutSourceMainFamily.Fallout3),
                    records, "authored-engine-static-stack");
                using var links = new FalloutSourceCellReferenceLinks(FalloutSourceCellReferenceLinksDeclaration.ForExecutable(FalloutSourceMainFamily.Fallout3),
                    "authored-engine-static-stack", cellSource.Read, form => rows[form], reader);
                var root = new object(); reader.PublishNativeOwner(root); links.PublishNativeOwner(root);
                if (variant is not ("valid" or "existing-base"))
                {
                    Reject(() => links.Read(cell.Source.Cell));
                    Require(links.SaveBlocker is not null, "Unknown or malformed engine-static input lost its failed entered list prefix.");
                    continue;
                }
                Require(ids.All(id => records.TryGetWinner(Key(id), out _) == (variant == "existing-base" &&
                    (id is FalloutDynamicPrimitiveSource.RoomMarkerRuntimeId or FalloutDynamicPrimitiveSource.CollisionMarkerRuntimeId))) &&
                    cell.References.All(row => (row.BaseSha256 is not null) == (variant == "existing-base" &&
                        (row.Base.ObjectId is FalloutDynamicPrimitiveSource.RoomMarkerRuntimeId or FalloutDynamicPrimitiveSource.CollisionMarkerRuntimeId))) &&
                    links.Read(cell.Source.Cell).OrderedReferences.Select(row => row.Reference).SequenceEqual(
                        new[] { Key(0xa13), Key(0xa12), Key(0xa11), Key(0xa10) }),
                    "Actual XPRM engine marker acquired a fabricated ESM base, missing member or altered source head order.");
                var saved = Copy(links.Capture());
                for (var cycle = 0; cycle < 3; cycle++)
                {
                    using var coldReader = new FalloutSourceCellReferenceIngestion(saved.Cells.Single().InitialEvidence!.Source, records, saved.Stack);
                    using var cold = new FalloutSourceCellReferenceLinks(saved.Source, saved.Stack, cellSource.Read, form => rows[form], coldReader, saved);
                    var coldRoot = new object(); coldReader.PublishNativeOwner(coldRoot); cold.PublishNativeOwner(coldRoot);
                    Require(cold.Read(cell.Source.Cell).OrderedReferences.SequenceEqual(saved.Cells.Single().OrderedReferences) &&
                        cold.Capture().Membership == saved.Membership && cold.Capture().Attempts.Count == saved.Attempts.Count,
                        "Cold engine marker membership replayed allocation or source insertion.");
                    saved = Copy(cold.Capture());
                }
            }
            finally { Directory.Delete(directory, recursive: true); }
        }
        Console.WriteLine("OPENNV_CELL_ENGINE_STATIC_PASS actualXPRM=true constructorSelectedSTAT=true existingSTATReused=true absentESMBasePreserved=true " +
            "unknownBaseRefused=true malformedPrimitiveRefused=true missingPrimitiveRefused=true coldCycles=3 originalExecution=UNEXECUTED");
    }
}
