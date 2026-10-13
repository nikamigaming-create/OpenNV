using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class CompiledScriptContracts
{
    internal static void RunTriggerMembership()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-trigger-membership-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "Bytecode.esm");
            File.WriteAllBytes(path, TriggerMembershipFixture());
            var original = SHA256.HashData(File.ReadAllBytes(path));
            using var records = FalloutPluginStack.Load(directory, ["Bytecode.esm"]);
            var cell = FalloutCellSceneReader.Read(records, Key(0x800));
            using var world = new FalloutReferenceWorld(records); world.LoadCell(cell);
            var trigger = world.Get(Key(0x900));
            var contacts = trigger.TriggerContacts;
            var player = records.RuntimeFormKey(FalloutScriptBindings.PlayerReferenceId);
            var first = contacts.Advance([player, Key(0xa00), Key(0xa01)]);
            Require(first.First() is { Name: "OnTriggerEnter" } && first.First().ActionReference == player,
                "The actual reference owner did not preserve first contact discovery order.");
            var saved = Copy(world.Capture().ToArray());
            var membership = saved.Single(row => row.Reference == trigger.Reference).TriggerContacts!;
            Require(membership is { State: FalloutTriggerMembershipState.Known } && membership.Admitted.SequenceEqual([player]),
                "Logical capture confused pending physical overlaps with already admitted contacts.");
            Require(ReferenceEquals(contacts, world.Retained(trigger.Reference).TriggerContacts),
                "Repeated reference access replaced the living logical contact owner.");
            world.UnloadCell(cell.Cell.FormKey); world.LoadCell(cell);
            Require(ReferenceEquals(contacts, world.Get(trigger.Reference).TriggerContacts) && contacts.HasAdmittedContacts,
                "Logical CELL residency discarded already committed trigger membership.");

            using var cold = new FalloutReferenceWorld(records); cold.Restore(saved); cold.LoadCell(cell);
            var coldContacts = cold.Get(trigger.Reference).TriggerContacts;
            Require(!ReferenceEquals(contacts, coldContacts) &&
                JsonSerializer.Serialize(contacts.Capture()) == JsonSerializer.Serialize(coldContacts.Capture()),
                "Cold restoration borrowed a warm owner or changed membership before any actual sample.");
            // Each input below is an authored sample, not native contact proof.
            // A new enter defers the departed player, then the other enter, then
            // the player leave. The ordered admitted list must survive cold.
            foreach (var sample in new FalloutFormKey[][]
            {
                [Key(0xa00), Key(0xa01)], [Key(0xa00), Key(0xa01)],
                [Key(0xa00), Key(0xa01)], [Key(0xa00), Key(0xa01)], [], [], []
            })
            {
                Require(TriggerMembershipTrace(contacts.Advance(sample)) == TriggerMembershipTrace(coldContacts.Advance(sample)) &&
                    JsonSerializer.Serialize(contacts.Capture()) == JsonSerializer.Serialize(coldContacts.Capture()),
                    "Cold trigger changed deferred enter/leave ordering or continuous membership.");
            }
            Require(!contacts.HasAdmittedContacts && !coldContacts.HasAdmittedContacts,
                "Ordered departure retained a stale occupant.");

            using var held = new FalloutReferenceWorld(records); held.Restore(saved);
            var heldEvents = held.Get(trigger.Reference).TriggerContacts.Advance([player]);
            Require(heldEvents.Count == 1 && heldEvents[0] is { Name: "OnTrigger", ActionReference: null } &&
                heldEvents[0].TriggerReferences!.SetEquals([player]),
                "Cold uninterrupted overlap replayed Enter or manufactured an action reference.");
            Require(held.Get(trigger.Reference).TriggerContacts.Advance([]).Single() is
                { Name: "OnTriggerLeave", ActionReference: { ObjectId: FalloutScriptBindings.PlayerReferenceId } },
                "The first authored departed sample lost its one real Leave subject.");

            var before = JsonSerializer.Serialize(contacts.Capture());
            Reject(() => contacts.Advance([player, player]));
            Reject(() => contacts.Advance([player, player with { OwnerPlugin = "BYTECODE.ESM" }]));
            Reject(() => contacts.Advance([player, Key(0x100)]));
            Reject(() => contacts.Advance([player, new("Missing.esm", 1)]));
            Require(before == JsonSerializer.Serialize(contacts.Capture()),
                "Invalid later contact changed earlier membership before complete admission validation.");
            var copy = contacts.Capture();
            Reject(() => ((IList<FalloutFormKey>)copy.Admitted).Add(player));
            Reject(() => contacts.Restore(membership));

            foreach (var invalid in new[]
            {
                membership with { Schema = "unknown" }, membership with { Reference = Key(0x901) },
                membership with { SourceSha256 = new string('0', 64) },
                membership with { Admitted = [player, player] }, membership with { Admitted = [Key(0x100)] },
                membership with { Admitted = [player, player with { OwnerPlugin = "BYTECODE.ESM" }] },
                membership with { Admitted = null! }, membership with { Admitted = [Key(0)] },
                membership with { Admitted = [new("Missing.esm", 1)] },
                membership with { State = (FalloutTriggerMembershipState)99 },
                membership with { State = FalloutTriggerMembershipState.LegacyMissing }
            })
            {
                using var rejected = new FalloutReferenceWorld(records);
                Reject(() => rejected.Restore(saved.Select(row => row.Reference == trigger.Reference
                    ? row with { TriggerContacts = invalid } : row).ToArray()));
                Require(rejected.InstanceCount == 0 && rejected.PendingPackageEventCount == 0,
                    "Invalid contact snapshot published a partial restored world.");
            }

            using var legacy = new FalloutReferenceWorld(records);
            legacy.Restore(saved.Select(row => row.Reference == trigger.Reference ? row with { TriggerContacts = null } : row).ToArray());
            var legacyContacts = legacy.Get(trigger.Reference).TriggerContacts;
            Require(legacyContacts.LegacyMembershipMissing && !legacyContacts.HasAdmittedContacts &&
                legacyContacts.Capture().State == FalloutTriggerMembershipState.LegacyMissing,
                "Missing legacy membership silently became a known empty contact list.");
            Reject(() => legacyContacts.Advance([]));
            Reject(() => legacyContacts.Advance([player]));
            using var legacyAgain = new FalloutReferenceWorld(records); legacyAgain.Restore(Copy(legacy.Capture().ToArray()));
            Require(legacyAgain.Get(trigger.Reference).TriggerContacts.LegacyMembershipMissing,
                "A second cold cycle erased the independent legacy membership refusal.");

            TriggerMembershipProjectiles(records, cell);
            TriggerMembershipFailedPrefix(directory);
            TriggerMembershipSourceDrift(directory, saved);
            Require(original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Trigger membership changed its source file.");
            Console.WriteLine("OPENNV_TRIGGER_MEMBERSHIP_PASS referenceOwner=true orderedCold=true noColdEnterReplay=true " +
                "deferredDeparture=true invalidAtomic=true sourceDriftRefused=true legacyMissingRetained=true " +
                "compiledFailedPrefix=true projectileContacts=true renderer=NONE contactProducer=UNOWNED gameplaySave=UNACCEPTED");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static string TriggerMembershipTrace(IReadOnlyList<FalloutReferenceScriptEvent> events) =>
        JsonSerializer.Serialize(events.Select(row => new
        {
            row.Name, row.ActionReference,
            Members = row.TriggerReferences?.OrderBy(reference => reference.ToString(), StringComparer.Ordinal).ToArray()
        }));

    private static byte[] TriggerMembershipFixture()
    {
        var code = Join(Instruction(0x1d),
            TriggerBlock(FalloutCompiledScriptEvents.TriggerEnter, Join(U16(1), Form(1)), Set('f', 1, " 101")),
            TriggerBlock(FalloutCompiledScriptEvents.TriggerEnter, Join(U16(1), Form(2)), Set('f', 1, " 102")),
            TriggerBlock(FalloutCompiledScriptEvents.Trigger, Join(U16(1), Form(1)), Set('f', 2, " 201")),
            TriggerBlock(FalloutCompiledScriptEvents.Trigger, Join(U16(1), Form(2)), Set('f', 2, " 202")),
            TriggerBlock(FalloutCompiledScriptEvents.TriggerLeave, Join(U16(1), Form(1)), Set('f', 1, " 301")),
            TriggerBlock(FalloutCompiledScriptEvents.TriggerLeave, Join(U16(1), Form(2)), Set('f', 1, " 302")));
        var references = Join(
            Record("PGRE", 0xa02, Field("NAME", U32(0x111)), Field("DATA", new byte[24])),
            Record("PMIS", 0xa03, Field("NAME", U32(0x111)), Field("DATA", new byte[24])),
            Record("REFR", 0x904, Field("NAME", U32(0x104)), Field("DATA", new byte[24])));
        var group = new byte[24 + references.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        UInt(group, 4, (uint)group.Length); UInt(group, 8, 0x800); UInt(group, 12, 6); references.CopyTo(group, 24);
        return Join(TriggerCallbackFixture(), Record("PROJ", 0x111), Record("STAT", 0x104, Field("SCRI", U32(0x64))),
            Script(0x64, code, [Field("SCRO", U32(0xa02)), Field("SCRO", U32(0xa03))]), group);
    }

    private static void TriggerMembershipProjectiles(FalloutPluginStack records, FalloutCellScene cell)
    {
        using var world = new FalloutReferenceWorld(records); world.LoadCell(cell);
        var trigger = world.Get(Key(0x904));
        FalloutReferenceScripts Scripts(FalloutReferenceWorld current) => new(records, current, new(records),
            new((_, _) => false, _ => throw new InvalidDataException("Unexpected projectile trigger effect."),
                Command: (_, _, _, _) => throw new InvalidDataException("Projectile trigger interpreted source text.")));
        foreach (var (signature, projectile, ordinal) in new[] { ("PGRE", Key(0xa02), 1), ("PMIS", Key(0xa03), 2) })
        {
            Require(records.GetEffective(projectile).Signature == signature && world.Get(projectile).Cell == cell.Cell.FormKey,
                "The actual placement reader did not construct its projectile reference.");
            var admitted = trigger.TriggerContacts.Advance([projectile]);
            Require(admitted.Count == 2 && admitted[0] is { Name: "OnTriggerEnter" } && admitted[0].ActionReference == projectile &&
                Scripts(world).DispatchFrame(trigger.Reference, admitted, 0).All(row => row is { Error: null, Blocks: 1 }) &&
                trigger.Read(1) == 100 + ordinal && trigger.Read(2) == 200 + ordinal,
                "Projectile identity did not match its compiled fixed enter/continuous filters.");
            var action = world.Get(Key(0x901));
            Require(Scripts(world).DispatchFrame(action.Reference, [admitted[0]], 0).Single() is { Error: null, Blocks: 1 } &&
                action.Read(3) == records.RuntimeFormId(projectile), "Projectile Enter lost its actual action reference.");
            var saved = Copy(world.Capture().ToArray());
            using var cold = new FalloutReferenceWorld(records); cold.Restore(saved); cold.LoadCell(cell);
            var coldTrigger = cold.Get(trigger.Reference);
            var held = coldTrigger.TriggerContacts.Advance([projectile]);
            Require(held.Single() is { Name: "OnTrigger" } &&
                Scripts(cold).DispatchFrame(coldTrigger.Reference, held, 0).Single() is { Error: null, Blocks: 1 } &&
                coldTrigger.Read(1) == 100 + ordinal && coldTrigger.Read(2) == 200 + ordinal,
                "Cold projectile membership replayed Enter or lost its fixed continuous filter.");
            foreach (var current in new[] { world, cold })
            {
                var currentTrigger = current.Get(trigger.Reference);
                var departed = currentTrigger.TriggerContacts.Advance([]);
                Require(departed.Single() is { Name: "OnTriggerLeave" } && departed[0].ActionReference == projectile &&
                    Scripts(current).DispatchFrame(currentTrigger.Reference, departed, 0).Single() is { Error: null, Blocks: 1 } &&
                    currentTrigger.Read(1) == 300 + ordinal && !currentTrigger.TriggerContacts.HasAdmittedContacts,
                    "Projectile departure lost its retained identity or fixed compiled filter.");
                Require(Scripts(current).DispatchFrame(action.Reference, departed, 0).Single() is { Error: null, Blocks: 1 } &&
                    current.Get(action.Reference).Read(3) == records.RuntimeFormId(projectile),
                    "Projectile Leave borrowed a different action reference after cold restoration.");
            }
        }
    }

    private static void TriggerMembershipFailedPrefix(string directory)
    {
        var code = Join(Instruction(0x1d), TriggerBlock(FalloutCompiledScriptEvents.TriggerEnter, U16(0),
            Join(Set('f', 1, " 71"), Call(0x1022, U16(0), 1), Instruction(0x2f03), Set('f', 2, " 999"))));
        File.WriteAllBytes(Path.Combine(directory, "Fault.esp"), Join(Tes4("Bytecode.esm"),
            Script(0x62, code, [Field("SCRO", U32(0xa00))], source: "begin OnTriggerEnter\nset value to 999\nend")));
        using var records = FalloutPluginStack.Load(directory, ["Bytecode.esm", "Fault.esp"]);
        var cell = FalloutCellSceneReader.Read(records, Key(0x800));
        using var world = new FalloutReferenceWorld(records); world.LoadCell(cell);
        var trigger = world.Get(Key(0x902));
        var effects = 0;
        FalloutReferenceScripts Scripts(FalloutReferenceWorld current) => new(records, current, new(records),
            new((_, _) => false, _ => ++effects,
                Command: (_, _, _, _) => throw new InvalidDataException("Trigger membership used contradictory SCTX.")));
        var delivered = trigger.TriggerContacts.Advance([Key(0xa00)]);
        var result = Scripts(world).DispatchFrame(trigger.Reference, delivered, 0);
        Require(result.All(row => row.Error is not null) && trigger.Read(1) == 71 && trigger.Read(2) == 0 &&
            effects == 1 && trigger.TriggerContacts.HasAdmittedContacts,
            "Failed compiled callback discarded admitted membership or its committed local/effect prefix.");
        var saved = Copy(world.Capture().ToArray());
        using var cold = new FalloutReferenceWorld(records); cold.Restore(saved); cold.LoadCell(cell);
        Require(effects == 1 && cold.Get(trigger.Reference).ScriptError == trigger.ScriptError,
            "Cold membership restoration executed or cleared the failed source invocation.");
        var held = cold.Get(trigger.Reference).TriggerContacts.Advance([Key(0xa00)]);
        Require(held.Single() is { Name: "OnTrigger" } && Scripts(cold).DispatchFrame(trigger.Reference, held, 0).Single().Error is not null &&
            cold.Get(trigger.Reference).Read(1) == 71 && cold.Get(trigger.Reference).Read(2) == 0 && effects == 1,
            "Cold held contact replayed Enter or the failed compiled effect prefix.");
        var departed = cold.Get(trigger.Reference).TriggerContacts.Advance([]);
        Require(departed.Single() is { Name: "OnTriggerLeave" } &&
            Scripts(cold).DispatchFrame(trigger.Reference, departed, 0).Single().Error is not null && effects == 1,
            "A new departure cleared the stopped invocation or replayed its prefix.");
    }

    private static void TriggerMembershipSourceDrift(string directory, FalloutReferenceSnapshot[] saved)
    {
        var reference = Record("REFR", 0x900, Field("NAME", U32(0x100)), Field("DATA", new byte[24]));
        var group = new byte[24 + reference.Length]; System.Text.Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        UInt(group, 4, (uint)group.Length); UInt(group, 8, 0x800); UInt(group, 12, 6); reference.CopyTo(group, 24);
        File.WriteAllBytes(Path.Combine(directory, "Reference.esp"), Join(Tes4("Bytecode.esm"), group));
        using var changed = FalloutPluginStack.Load(directory, ["Bytecode.esm", "Reference.esp"]);
        using var rejected = new FalloutReferenceWorld(changed);
        Reject(() => rejected.Restore(saved));
        Require(rejected.InstanceCount == 0, "An identical-body winning reference replacement silently reused historical contact ownership.");
    }
}
