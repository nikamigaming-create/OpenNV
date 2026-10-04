using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedLinkedDoorAccessProbe
{
    internal static void Run(string mod, string root, string game, string firstPlugin, uint firstId,
        string secondPlugin, uint secondId, string[] dependencies)
    {
        using var content = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game).OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var first = new FalloutFormKey(firstPlugin, firstId); var second = new FalloutFormKey(secondPlugin, secondId);
        var source = new[] { records.GetEffective(first), records.GetEffective(second) };
        var hashes = source.ToDictionary(record => record.FormKey, Hash);
        foreach (var (record, index) in source.Select((record, index) => (record, index)))
        {
            if (record.ReadSubrecords().Any(field => field.Signature == "XLOC"))
                throw new InvalidDataException("Selected unlocked-pair fixture has a source lock declaration.");
            var link = record.ReadSubrecords().Single(field => field.Signature == "XTEL").Data;
            if (link.Length != 32 || record.Plugin.AdjustOptionalFormId(
                System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(link.Span)) != source[1 - index].FormKey)
                throw new InvalidDataException("Selected source references are not the same reciprocal pair.");
        }
        using var world = new FalloutReferenceWorld(records);
        if (world.GetLocked(first) != 0 || world.GetLockLevel(first) != 0 || world.Lock(first) is not null ||
            world.InstanceCount != 1 || world.ResidentCellCount != 0)
            throw new InvalidDataException("Selected source-unlocked pair created access or destination state.");
        if (world.GetLocked(second) != 0 || world.GetLockLevel(second) != 0 || world.Lock(second) is not null ||
            !world.UnlockWithKey(first, new FalloutPlayerInventory()))
            throw new InvalidDataException("Selected opposite source side did not remain unlocked.");
        var sourceState = RoundTrip(world.Capture());
        using (var sourceCold = new FalloutReferenceWorld(records))
        {
            sourceCold.Restore(sourceState);
            if (sourceCold.GetLocked(first) != 0 || sourceCold.GetLocked(second) != 0 ||
                sourceCold.Get(first).LockState is not null || sourceCold.Get(second).LockState is not null)
                throw new InvalidDataException("Cold source-unlocked pair invented a lock.");
        }
        // Disposable shared-state fixture. This is not a source command, ordinary
        // activation, campaign traversal or a modification of the owned records.
        world.LockReference(first, 75);
        if (world.GetLocked(first) != 1 || world.GetLockLevel(first) != 75 || world.Lock(first)?.Level != 75)
            throw new InvalidDataException("Selected actual side lost its fixture lock.");
        RefuseInheritance(() => world.GetLocked(second));
        var locked = RoundTrip(world.Capture());
        foreach (var snapshots in new[] { locked, locked.Reverse().ToArray() })
        {
            using var cold = new FalloutReferenceWorld(records); cold.Restore(snapshots);
            if (cold.GetLocked(first) != 1 || cold.GetLockLevel(first) != 75 || cold.InstanceCount != 2)
                throw new InvalidDataException("Cold selected fixture lock changed or depended on snapshot order.");
            RefuseInheritance(() => cold.GetLocked(second));
            cold.UnlockReference(first);
            if (cold.GetLocked(first) != 0 || cold.GetLockLevel(first) != 75)
                throw new InvalidDataException("Cold selected fixture Unlock lost retained difficulty.");
            RefuseInheritance(() => cold.GetLockLevel(second));
        }
        using (var legacy = new FalloutReferenceWorld(records))
        {
            legacy.Restore([sourceState.Single(snapshot => snapshot.Reference == first) with { Unlocked = true }]);
            if (legacy.GetLocked(first) != 0 || legacy.Get(first).LockState is not null || legacy.GetLocked(second) != 0)
                throw new InvalidDataException("Selected legacy absent lock migrated into a new lock.");
        }
        if (source.Any(record => hashes[record.FormKey] != Hash(record)))
            throw new InvalidDataException("Selected owned door records changed.");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "opennv-owned-linked-door-access/v1",
            sources = source.Select(record => new
            {
                reference = record.FormKey.ToString(),
                winner = record.Plugin.Name,
                masters = record.Plugin.Masters,
                sourceSha256 = hashes[record.FormKey],
                cell = world.Get(record.FormKey).Cell.ToString(),
                basis = world.Get(record.FormKey).Base.ToString(),
                sourceLockDeclarations = 0
            }),
            sourceUnlocked = true,
            sourceCold = true,
            localFixtureLockBlocks = true,
            oppositeDynamicInheritance = "refused",
            coldBothOrders = true,
            legacyAbsent = true,
            lockDependencySha256 = world.Get(first).LockState!.ReferenceSha256,
            sourceUnchanged = true,
            boundary = "selected-shared-access-fixture; source-pair-unlocked; script-propagation/native-activation/physical-traversal/full-cold-Continue/retail-parity-unverified",
            recording = false
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("OPENNV_OWNED_LINKED_DOOR_ACCESS_PASS reciprocalSourceUnlocked=true localFixtureLockBlocks=true " +
            "coldBothOrders=true legacyAbsent=true sourceUnchanged=true linkedLockInheritance=unbound nativeTraversalAndCampaign=unverified recording=false");
    }

    private static string Hash(FalloutPluginRecord record) => Convert.ToHexString(SHA256.HashData(record.ReadData()));
    private static FalloutReferenceSnapshot[] RoundTrip(IReadOnlyList<FalloutReferenceSnapshot> snapshots) =>
        JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(snapshots))!;
    private static void RefuseInheritance(Action action)
    {
        try { action(); }
        catch (NotSupportedException) { return; }
        throw new InvalidDataException("Selected opposite-side dynamic lock was silently ignored.");
    }
}
