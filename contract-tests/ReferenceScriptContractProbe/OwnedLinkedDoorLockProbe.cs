using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class OwnedLinkedDoorLockProbe
{
    internal static void Run(string dataRoot, uint firstId, uint secondId)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var first = records.RuntimeFormKey(firstId); var second = records.RuntimeFormKey(secondId);
        var sources = new[] { records.GetEffective(first), records.GetEffective(second) };
        var hashes = sources.Select(Hash).ToArray();
        if (sources[0].ReadSubrecords().Any(field => field.Signature == "XLOC"))
            throw new InvalidDataException("Selected caller must inherit its source lock.");
        var data = sources[1].ReadSubrecords().Single(field => field.Signature == "XLOC").Data;
        if (data.Length is not (12 or 20) || (data.Span[8] & 4) != 0)
            throw new InvalidDataException("Selected source requires one non-leveled lock declaration.");
        var level = data.Span[0];
        using var world = new FalloutReferenceWorld(records);
        if (world.GetLocked(first) != 1 || world.GetLockLevel(first) != level || world.Lock(first)?.Level != level ||
            world.InstanceCount != 1 || world.ResidentCellCount != 0)
            throw new InvalidDataException("Inherited source lock lost access or instantiated the opposite side.");
        var link = sources[0].ReadSubrecords().Single(field => field.Signature == "XTEL").Data;
        if (link.Length != 32 || sources[0].Plugin.AdjustFormId(
                System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(link.Span)) != second)
            throw new InvalidDataException("Selected source lock owner is not the teleport destination.");
        // Disposable state exercise, not ordinary gameplay or a change to the
        // read-only source installation. Native activation is checked separately.
        world.UnlockReference(first);
        if (world.GetLocked(first) != 0 || world.GetLocked(second) != 0 || world.GetLockLevel(first) != level ||
            world.Get(first).LockState is not null || world.Get(second).LockState is not { Locked: false })
            throw new InvalidDataException("Inherited Unlock did not mutate only the actual source owner.");
        var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
        foreach (var snapshots in new[] { saved, saved.Reverse().ToArray() })
        {
            using var cold = new FalloutReferenceWorld(records); cold.Restore(snapshots);
            if (cold.GetLocked(first) != 0 || cold.GetLocked(second) != 0 || cold.GetLockLevel(first) != level ||
                cold.InstanceCount != 2 || cold.ResidentCellCount != 0)
                throw new InvalidDataException("Cold inherited source access differs or depends on restore order.");
            cold.LockReference(first, 75);
            if (cold.GetLockLevel(first) != 75 || cold.GetLockLevel(second) != 75 || cold.GetLocked(second) != 1 ||
                cold.Get(first).LockState is not null)
                throw new InvalidDataException("Cold inherited Lock created a competing local owner.");
        }
        if (!sources.Select(Hash).SequenceEqual(hashes)) throw new InvalidDataException("Owned source records changed.");
        Console.WriteLine(JsonSerializer.Serialize(new { first, second, level, hashes, sourceUnchanged = true,
            noDestinationInstanceOnQuery = true, inheritedMutation = true, coldBothOrders = true,
            ordinaryTraversal = false, parity = false, recording = false }));
        Console.WriteLine("OPENNV_OWNED_LINKED_SOURCE_LOCK_PASS inherited=true sourceOwnerOnly=true queryReadOnly=true coldBothOrders=true recording=false");
    }

    private static string Hash(FalloutPluginRecord source) => Convert.ToHexString(SHA256.HashData(source.ReadData()));
}
