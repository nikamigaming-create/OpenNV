using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class ReferenceHitContracts
{
    internal static void Run()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-reference-hit-");
        try
        {
            const string ordered = "short trace\nref caller\nref action\n" +
                "begin OnHitWith GunB\nset trace to trace * 10 + 1\nend\n" +
                "begin GameMode\nset trace to trace * 10 + 2\nend\n" +
                "begin OnHit Player\nset trace to trace * 10 + 3\nend\n" +
                "begin OnHitWith BothGuns\nset trace to trace * 10 + 4\nset caller to GetSelf\nset action to GetActionRef\nPlayGroup Forward 1\nend\n" +
                "begin OnHitWith GunA\nset trace to trace * 10 + 5\nend";
            File.WriteAllBytes(Path.Combine(directory.FullName, "Hits.esm"), Join(Header(),
                Actor(1, 0x50), Actor(2, 0x51), Actor(3, 0x52), Actor(4, 0x53), Actor(5, 0x54),
                Record("ACTI", 7, Field("SCRI", U32(0x50))),
                Script(0x50, ordered, 0x70, 0x71, 0x72, 0x14),
                Script(0x51, "short trace\nbegin OnHitWith GunA\nset trace to trace + 1\nUnsupportedHitCommand\nend\n" +
                    "begin GameMode\nset trace to trace + 100\nend", 0x70),
                Script(0x52, "short trace\nbegin OnHitWith WrongKind\nset trace to 1\nend", 0x73),
                Script(0x53, "short trace\nbegin OnHitWith GunA\nset trace to 1\nend"),
                Script(0x54, "short trace\nbegin OnHitWith NestedList\nset trace to 1\nend", 0x74),
                Named("WEAP", 0x70, "GunA"), Named("WEAP", 0x71, "GunB"),
                Record("FLST", 0x72, Field("EDID", Text("BothGuns")), Field("LNAM", U32(0x70)), Field("LNAM", U32(0x71)), Field("LNAM", U32(0x73))),
                Named("STAT", 0x73, "WrongKind"),
                Record("FLST", 0x74, Field("EDID", Text("NestedList")), Field("LNAM", U32(0x72))),
                Record("CELL", 0x80, Field("DATA", [1])), Group(0x80,
                    Reference("ACRE", 0x90, 1), Reference("ACRE", 0x91, 1), Reference("ACRE", 0x92, 2),
                    Reference("ACRE", 0x93, 3), Reference("ACRE", 0x94, 4), Reference("ACRE", 0x95, 5),
                    Reference("REFR", 0x96, 7))));
            File.WriteAllBytes(Path.Combine(directory.FullName, "HitPatch.esp"), Join(Header("Hits.esm"),
                Script(0x50, ordered, 0x70, 0x01000101, 0x72, 0x14), Named("WEAP", 0x01000101, "GunB"),
                Record("FLST", 0x72, Field("EDID", Text("BothGuns")), Field("LNAM", U32(0x70)), Field("LNAM", U32(0x01000101)), Field("LNAM", U32(0x73)))));
            using var records = FalloutPluginStack.Load(directory.FullName, ["Hits.esm", "HitPatch.esp"]);
            using var world = new FalloutReferenceWorld(records);
            var actor = Key(0x90);
            var player = records.RuntimeFormKey(0x14);
            var second = Key(0x91);
            var queue = world.HitEvents;
            var animationRequests = 0;
            var damageApplied = false;
            var requeue = false;
            var scripts = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false, _ =>
                throw new InvalidDataException("Unexpected hit fixture effect."), PlayGroup: (reference, group, initialization) =>
                {
                    Check(damageApplied && group.Equals("forward", StringComparison.OrdinalIgnoreCase) && initialization == 1,
                        "A queued hit ran before its completed damage owner or changed the source animation request.");
                    animationRequests++;
                    if (requeue) queue.Mark(reference, player, Key(0x70), FalloutReferenceHitKind.Projectile);
                }));
            // Damage is a separate authoritative lane. Admission cannot execute
            // the script in a contact callback or manufacture any damage itself.
            damageApplied = true;
            queue.Mark(actor, player, Key(0x70), FalloutReferenceHitKind.Projectile);
            queue.Mark(actor, player, Key(0x70), FalloutReferenceHitKind.Projectile);
            queue.Mark(actor, second, GunB(), FalloutReferenceHitKind.Projectile);
            queue.Mark(second, player, Key(0x70), FalloutReferenceHitKind.Projectile);
            Check(world.InstanceCount == 0 && queue.PendingCount == 3 && animationRequests == 0,
                "Pellet marks ran scripts, lost coalescence or borrowed another reference's event list.");
            Reject(() => world.Capture());
            var cell = FalloutCellSceneReader.Read(records, Key(0x80)); world.LoadCell(cell);
            var batch = queue.SnapshotPending(actor);
            Check(batch.Count == 2 && batch.Events.Count == 2 &&
                batch.Events.Single(item => item.Name == "OnHitWith").HitWeapons!.SetEquals([Key(0x70), GunB()]) &&
                batch.Events is not FalloutReferenceScriptEvent[] && batch.Events.All(item => item.HitWeapons is not HashSet<FalloutFormKey>),
                "Hit receipt lost typed weapons, actor membership or immutable admission.");
            var results = scripts.DispatchFrame(actor, [new("GameMode"), .. batch.Events], 1d / 60);
            Check(results.All(result => result.Error is null) && results.Sum(result => result.Blocks) == 5 &&
                world.Get(actor).Read(1) == 12345 && world.Get(actor).Read(2) == records.RuntimeFormId(actor) &&
                world.Get(actor).Read(3) == 0 && animationRequests == 1,
                "Hit blocks lost source declaration order, WEAP/FLST master adjustment, GetSelf or null action ref: " +
                JsonSerializer.Serialize(results) + $" trace={world.Get(actor).Read(1)} caller={world.Get(actor).Read(2)} action={world.Get(actor).Read(3)}.");
            queue.Consume(batch);
            Check(queue.PendingReferences.SequenceEqual([second]), "Hit consumption crossed reference owners.");
            Reject(() => queue.Consume(batch));
            var other = queue.SnapshotPending(second);
            Check(scripts.DispatchFrame(second, other.Events, 0).All(result => result.Error is null) && world.Get(second).Read(1) == 345,
                "Two actors shared hit locals or filtered weapon membership.");
            queue.Consume(other);
            var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            using var cold = new FalloutReferenceWorld(records); cold.Restore(saved);
            Check(cold.PendingHitEventCount == 0 && cold.Get(actor).Read(1) == 12345, "Consumed hit marks replayed during cold restoration.");

            queue.Mark(Key(0x96), player, Key(0x70), FalloutReferenceHitKind.Melee);
            var melee = queue.SnapshotPending(Key(0x96));
            Check(melee.Events.All(item => item.Name != "OnHitWith") &&
                scripts.DispatchFrame(Key(0x96), melee.Events, 0).All(result => result.Error is null) && world.Get(Key(0x96)).Read(1) == 0,
                "Activator admitted a melee weapon event or its disabled actor-filtered OnHit block.");
            queue.Consume(melee);
            queue.Mark(Key(0x96), player, Key(0x70), FalloutReferenceHitKind.Projectile);
            var projectile = queue.SnapshotPending(Key(0x96));
            Check(scripts.DispatchFrame(Key(0x96), [.. projectile.Events, new("GameMode")], 0).All(result => result.Error is null) &&
                world.Get(Key(0x96)).Read(1) == 245, "Activator projectile lost its source hit blocks or acquired actor-filtered OnHit.");
            queue.Consume(projectile);

            requeue = true;
            queue.Mark(actor, player, Key(0x70), FalloutReferenceHitKind.Projectile);
            var original = queue.SnapshotPending(actor);
            Check(scripts.DispatchFrame(actor, original.Events, 0).All(result => result.Error is null), "Reentrant hit frame failed.");
            queue.Consume(original);
            Check(queue.HasPending(actor), "Source effects lost a newer identical contact while consuming the admitted receipt.");
            var newer = queue.SnapshotPending(actor); queue.Consume(newer); requeue = false;
            var empty = queue.SnapshotPending(actor);
            Check(empty.Events.Count == 0 && scripts.DispatchFrame(actor, empty.Events, 0).Count == 0, "No-contact frame replayed a consumed hit.");
            queue.Consume(empty);
            var foreign = new FalloutReferenceHitEvents(records); Reject(() => foreign.Consume(newer));
            var retired = queue.SnapshotPending(actor); queue.Clear(); Reject(() => queue.Consume(retired));
            queue.Mark(actor, player, Key(0x70), FalloutReferenceHitKind.Projectile);
            var unloaded = queue.SnapshotPending(actor); world.UnloadCell(cell.Cell.FormKey);
            Reject(() => scripts.DispatchFrame(actor, unloaded.Events, 0));
            Check(queue.HasPending(actor), "Unloaded source reference discarded its pending hit.");
            world.LoadCell(cell); queue.Consume(unloaded);

            foreach (var bad in new[] { Key(0x93), Key(0x94), Key(0x95) })
            {
                queue.Mark(bad, player, Key(0x70), FalloutReferenceHitKind.Projectile);
                var admission = queue.SnapshotPending(bad);
                Check(scripts.DispatchFrame(bad, admission.Events, 0).Any(result => result.Error is not null),
                    "Wrong filter type, uncompiled form or unknown nested membership was admitted.");
                queue.Consume(admission);
            }
            queue.Mark(Key(0x92), player, Key(0x70), FalloutReferenceHitKind.Projectile);
            var failed = queue.SnapshotPending(Key(0x92));
            Check(scripts.DispatchFrame(Key(0x92), [.. failed.Events, new("GameMode")], 0).All(result => result.Error is not null) &&
                world.Get(Key(0x92)).Read(1) == 1, "Unsupported hit instruction lost its executed prefix or advanced GameMode.");
            queue.Consume(failed);
            Check(scripts.Dispatch(Key(0x92), "GameMode").Error is not null && world.Get(Key(0x92)).Read(1) == 1,
                "A later frame cleared a retained source hit fault.");
            Reject(() => queue.Mark(Key(0x70), player, Key(0x70), FalloutReferenceHitKind.Projectile));
            Reject(() => queue.Mark(actor, Key(0x73), Key(0x70), FalloutReferenceHitKind.Projectile));
            Reject(() => queue.Mark(actor, player, Key(0x73), FalloutReferenceHitKind.Projectile));
            Reject(() => queue.Mark(actor, player, null, FalloutReferenceHitKind.Projectile));
            Reject(() => scripts.DispatchFrame(actor, [new("OnHitWith")], 0));
            Reject(() => scripts.DispatchFrame(actor, [new("OnHitWith", HitWeapons: new HashSet<FalloutFormKey> { Key(0x73) })], 0));
            Reject(() => scripts.DispatchFrame(actor, [new("GameMode", HitWeapons: new HashSet<FalloutFormKey> { Key(0x70) })], 0));
            Reject(() => scripts.DispatchFrame(actor, [new("OnHitWith", player, HitWeapons: new HashSet<FalloutFormKey> { Key(0x70) })], 0));
            Check(queue.PendingCount == 0, "Hit contract left pending admissions.");
            Console.WriteLine("OPENNV_REFERENCE_HIT_CONTRACT_PASS sourceOrder=true actualReferences=true typedWeapons=true sourceFormList=true " +
                "pelletsCoalesced=true actorFilter=true activatorAmmoOnly=true postDamageAdmission=true coldConsumed=true reentrant=true " +
                "unloadedRetained=true pendingSaveRefused=true faultPrefixRetained=true invalidPayloadRefused=true");
        }
        finally { directory.Delete(true); }
    }

    private static FalloutFormKey Key(uint id) => new("Hits.esm", id);
    private static FalloutFormKey GunB() => new("HitPatch.esp", 0x101);
    private static byte[] Actor(uint id, uint script)
    {
        var acbs = new byte[24]; acbs[8] = 1;
        return Record("CREA", id, Field("ACBS", acbs), Field("DATA", new byte[17]), Field("SCRI", U32(script)));
    }
    private static byte[] Script(uint id, string source, params uint[] forms) => Record("SCPT", id,
        Local(1, "trace"), Local(2, "caller", true), Local(3, "action", true), Field("SCTX", Text(source)),
        Join(forms.Select(form => Field("SCRO", U32(form))).ToArray()));
    private static byte[] Named(string kind, uint id, string name) => Record(kind, id, Field("EDID", Text(name)));
    private static byte[] Reference(string kind, uint id, uint baseId) => Record(kind, id, Field("NAME", U32(baseId)), Field("DATA", new byte[24]));
    private static byte[] Group(uint cell, params byte[][] references)
    {
        var data = Join(references); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(bytes, 0);
        U32((uint)bytes.Length).CopyTo(bytes, 4); U32(cell).CopyTo(bytes, 8); U32(6).CopyTo(bytes, 12); data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Local(uint id, string name, bool reference = false)
    {
        var data = new byte[24]; U32(id).CopyTo(data, 0); data[16] = reference ? (byte)1 : (byte)0;
        return Join(Field("SLSD", data), Field("SCVR", Text(name)));
    }
    private static byte[] Header(string? master = null) => Record("TES4", 0, Field("HEDR", new byte[12]),
        master is null ? [] : Join(Field("MAST", Text(master)), Field("DATA", new byte[8])));
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        U32((uint)data.Length).CopyTo(bytes, 4); U32(id).CopyTo(bytes, 12); data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] U32(uint value) => BitConverter.GetBytes(value);
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static void Check(bool valid, string error) { if (!valid) throw new InvalidDataException(error); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or KeyNotFoundException) { return; }
        throw new InvalidDataException("Unbound hit payload, source owner or pending continuation was admitted.");
    }
}
