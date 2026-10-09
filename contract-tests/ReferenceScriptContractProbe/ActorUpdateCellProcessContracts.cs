using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class ActorUpdateCellProcessContracts
{
    private const string Selection = "authored-actor-update-cell-process";
    private static readonly string[] Images =
    ["518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57",
     "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e"];
    private static readonly FalloutFormKey Cell = Key(0x800), OtherCell = Key(0x801), Npc = Key(0x900), Creature = Key(0x901);
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-actor-update-cell-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "State.esm"), Plugin());
            File.WriteAllBytes(Path.Combine(directory, "StatePatch.esp"), Override());
            using var records = FalloutPluginStack.Load(directory, ["State.esm", "StatePatch.esp"]);
            foreach (var image in Images)
            {
                ActorByte(records, image); CellPhases(records, image); SourcePlacement(records, image);
                ScriptAndCurrentCell(records, image);
            }
        }
        finally
        {
            File.Delete(Path.Combine(directory, "StatePatch.esp")); File.Delete(Path.Combine(directory, "State.esm")); Directory.Delete(directory);
        }
        ExteriorSharedCellGraphContracts.Run();
        Console.WriteLine("OPENNV_ACTOR_UPDATE_CELL_PROCESS_CONTRACT_PASS actualReader=true bothSelectedContracts=true actorByteIndependentOfEnable=true signedIntToggleQuery=true scriptConsumerJoined=true winnerMasterBound=true sourcePhaseOrder=true incompleteRootHeld=true coldRequiresNewNativeAttachment=true priorIdsNotAuthority=true sourcePlacementDistinctFromAncestry=true retirementPrefixRetained=true native=unexecuted");
    }
    private static void ActorByte(FalloutPluginStack records, string image)
    {
        var declaration = FalloutActorUpdateDeclaration.ForExecutable(image);
        var group = FalloutCombatGroupDeclaration.ForExecutable(image);
        FalloutCombatActorIdentity Identity(FalloutFormKey key) => FalloutCombatActorSource.Read(records, group, key);
        using var owner = new FalloutActorUpdateState(declaration, Selection, Identity);
        owner.Construct(Npc); owner.Construct(Creature); owner.Construct(records.RuntimeFormKey(0x14));
        Require(owner.Read(Npc).Require() && owner.IsOff(Creature) == 0, "Actual shared actor constructor did not initialize one.");
        owner.Set(Npc, 0, Creature, "authored-real-SetActorsAI");
        Require(!owner.Read(Npc).Require() && owner.IsOff(Npc) == 1 && owner.Read(Creature).Require(), "Actor update write borrowed another actor.");
        owner.Set(Npc, -9, Creature, "authored-signed-SetActorsAI"); owner.Toggle(Npc, Npc, "authored-ToggleActorsAI");
        Require(owner.IsOff(Npc) == 1, "Negative signed argument/toggle zero test drifted.");
        var saved = RoundTrip(owner.Capture());
        using var cold = new FalloutActorUpdateState(declaration, Selection, Identity, saved);
        Require(cold.Capture().CapturedProcess != saved.CapturedProcess && cold.IsOff(Npc) == 1 &&
            cold.Capture().Receipts.SequenceEqual(saved.Receipts), "Cold byte restoration replayed a source command or used the old process.");
        Reject(() => new FalloutActorUpdateState(declaration, Selection, Identity, saved with
        { Actors = saved.Actors.Select(actor => actor.Source.Reference == Npc ? actor with { LastChanged = actor.Created } : actor).ToArray() }));
        Reject(() => new FalloutActorUpdateState(declaration, Selection, Identity, saved with
        { Receipts = saved.Receipts.Select(receipt => receipt.Operation == FalloutActorUpdateMutation.SetActorsAI ? receipt with { Argument = null } : receipt).ToArray() }));
        Reject(() => new FalloutActorUpdateState(declaration, Selection, Identity, saved with
        { Actors = saved.Actors.Select(actor => actor.Source.Reference == Npc ? actor with { Source = actor.Source with { ReferenceSha256 = new string('c', 64) } } : actor).ToArray() }));
        Reject(() => new FalloutActorUpdateState(declaration, Selection, key => Identity(key) with { Reference = new("Foreign.esm", key.ObjectId) }, saved));
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, .5, (double)int.MaxValue + 1 }) Reject(() => FalloutActorUpdateDeclaration.Integer(value));
        owner.Retire(Npc, "actual-source-actor-retired"); Reject(() => owner.Set(Npc, 1, Creature, "late-source-command")); Reject(() => owner.Construct(Npc));
        owner.ActorDataLoadReset(Creature, "authored-independent-actor-data-load");
        Require(owner.Read(Creature).Require(), "Actual independent data-load reset did not own its literal one.");
    }
    private static void CellPhases(FalloutPluginStack records, string image)
    {
        var declaration = FalloutCellProcessDeclaration.ForExecutable(image);
        foreach (var value in Enum.GetValues<FalloutCellProcessPhase>())
        {
            Require(FalloutCellProcessDeclaration.Eligible(value, true) == (value is FalloutCellProcessPhase.Attaching or FalloutCellProcessPhase.Attached), "High source phase predicate drifted.");
            Require(FalloutCellProcessDeclaration.Eligible(value, false) == (value is FalloutCellProcessPhase.LoadingData or FalloutCellProcessPhase.DataLoaded or
                FalloutCellProcessPhase.Detaching or FalloutCellProcessPhase.Attaching or FalloutCellProcessPhase.Attached), "Secondary source phase predicate drifted.");
        }
        Require(!FalloutCellProcessDeclaration.Eligible(null, false), "Actual null CELL became eligible.");
        var scene = FalloutCellSceneReader.Read(records, Cell);
        var owner = new FalloutCellProcesses(declaration, records, Selection);
        Require(owner.ReadPhase(Cell).Require() == 0, "Actual CELL class constructor synthesized residency.");
        owner.LoadSource(Cell); Require(owner.ReadPhase(Cell).Require() == 3, "Actual CELL reader did not own load2→3.");
        var lease = owner.BeginAttachment(scene, [Cell], 100);
        Require(owner.ReadPhase(Cell).Require() == 5 && owner.SaveBlocker is not null, "Attachment published before real child work.");
        Complete(owner, lease, 1000); owner.PublishRoot(lease, 100, "authored-native-root-published");
        Require(owner.ReadPhase(Cell).Require() == 6 && owner.SaveBlocker is null, "Complete child/root protocol did not own6.");
        var saved = RoundTrip(owner.Capture());
        Reject(() => new FalloutCellProcesses(declaration, records, Selection, saved with { Cells = saved.Cells.Select(cell => cell with { Phase = FalloutCellProcessPhase.DataLoaded }).ToArray() }));
        Reject(() => new FalloutCellProcesses(declaration, records, Selection, saved with { Transitions = saved.Transitions.Select(row => row.Operation == FalloutCellProcessOperation.BeginAttach ? row with { After = FalloutCellProcessPhase.Attached } : row).ToArray() }));
        Reject(() => new FalloutCellProcesses(declaration, records, Selection, saved with { Cells = saved.Cells.Select(cell => cell with { Source = cell.Source with { Sha256 = new string('d', 64) } }).ToArray() }));
        Reject(() => new FalloutCellProcesses(declaration, records, Selection, saved with { Attachments = saved.Attachments.Select(attachment => attachment with
        { Children = attachment.Children.Select(child => child.Phase == FalloutCellProcessChildPhase.Published ? child with { NativeObjects = [100] } : child).ToArray() }).ToArray() }));
        var calls = 0;
        var cold = new FalloutCellProcesses(declaration, records, Selection, saved, reference =>
        { calls++; return new(reference.Cell, reference.Position.ToArray(), reference.RotationRadians.ToArray()); });
        Require(calls == 0 && cold.ReadPhase(Cell).Value is null && cold.SaveBlocker is not null &&
            cold.Capture().Attachments.All(attachment => attachment.NativeRoot == 0 && !attachment.RootPublished && attachment.Children.All(child => child.NativeObjects.Count == 0)),
            "Cold metadata import asserted an old native owner or replayed a producer.");
        cold.BeginColdAttachment(lease, scene, [Cell], 200); Require(cold.ReadPhase(Cell).Require() == 5 && calls > 0, "Real cold attachment did not enter5.");
        Complete(cold, lease, 2000); cold.PublishRoot(lease, 200, "authored-new-process-root-published");
        var rebound = RoundTrip(cold.Capture());
        Require(rebound.CapturedProcess != saved.CapturedProcess && cold.ReadPhase(Cell).Require() == 6 && cold.SaveBlocker is null &&
            rebound.ColdHandoff!.PreviousAttachments.Single().NativeRoot == 100 && rebound.ColdHandoff.AwaitingNativeAttachments.Count == 0,
            "Actual cold attachment lost its previous receipt or published without retirement state.");
        var twice = new FalloutCellProcesses(declaration, records, Selection, rebound);
        Require(twice.ReadPhase(Cell).Value is null && twice.Capture().CapturedProcess != rebound.CapturedProcess, "Second process inherited a native phase as ready.");
        twice.BeginColdAttachment(lease, scene, [Cell], 300); Complete(twice, lease, 3000); twice.PublishRoot(lease, 300, "authored-second-process-root-published");
        Retire(twice, lease, 300); twice.Dispose(); Retire(cold, lease, 200); cold.Dispose(); Retire(owner, lease, 100); owner.Dispose();

        var failed = new FalloutCellProcesses(declaration, records, Selection);
        var unfinished = failed.BeginAttachment(scene, [Cell], 400);
        Reject(() => failed.PublishRoot(unfinished, 400, "wrong-early-publish"));
        Require(failed.ReadPhase(Cell).Value == 5 && failed.SaveBlocker is not null, "Incomplete child graph was falsely ready.");
        Retire(failed, unfinished, 400); Require(failed.SaveBlocker is not null, "Retirement erased the original failed operation."); failed.Dispose();
        var omitted = new FalloutCellProcesses(declaration, records, Selection);
        Reject(() => omitted.BeginAttachment(scene with { References = scene.References.Skip(1).ToArray() }, [Cell], 500));
        Require(omitted.ReadPhase(Cell).Value == 3 && omitted.SaveBlocker is not null, "Missing source child crossed attachment entry or vanished."); omitted.Dispose();
        var aliased = new FalloutCellProcesses(declaration, records, Selection);
        var aliasLease = aliased.BeginAttachment(scene, [Cell], 700);
        var sourceChildren = aliased.ReadAttachment(aliasLease).Children;
        aliased.CompleteChild(aliasLease, sourceChildren[0].Source, FalloutCellProcessChildPhase.Published, [710], "authored-original-child");
        Reject(() => aliased.CompleteChild(aliasLease, sourceChildren[1].Source,
            FalloutCellProcessChildPhase.Published, [710], "wrong-foreign-source-child"));
        Reject(() => aliased.JoinNativeChildConsumers(aliasLease, sourceChildren[0].Source, [700], "wrong-root-as-child"));
        Require(aliased.ReadAttachment(aliasLease).Children[0].NativeObjects.SequenceEqual([710UL]) &&
            aliased.ReadAttachment(aliasLease).Children[1].Phase == FalloutCellProcessChildPhase.Pending && aliased.SaveBlocker is not null,
            "Rejected native alias replaced the actual prefix or fabricated child completion.");
        Retire(aliased, aliasLease, 700); aliased.Dispose();
    }
    private static void SourcePlacement(FalloutPluginStack records, string image)
    {
        using var world = new FalloutReferenceWorld(records);
        var original = FalloutCellSceneReader.Read(records, Cell);
        world.LoadCell(original); world.SetPlacement(Npc, new(OtherCell, [12, 17, 3], [0, 0, .5f]));
        var owner = new FalloutCellProcesses(FalloutCellProcessDeclaration.ForExecutable(image), records, Selection,
            currentPlacement: reference => world.Placement(reference.FormKey));
        var current = world.ComposeResidency(FalloutCellSceneReader.Read(records, OtherCell));
        var lease = owner.BeginAttachment(current, [OtherCell], 600);
        Require(owner.ReadAttachment(lease).Children.Single().Source.SourceCell == Cell &&
            owner.ReadAttachment(lease).Children.Single().Placement.Cell == OtherCell,
            "A moved actor borrowed source ancestry as its current CELL.");
        Complete(owner, lease, 4000); owner.PublishRoot(lease, 600, "authored-moved-child-root-published"); Retire(owner, lease, 600); owner.Dispose();
    }
    private static void ScriptAndCurrentCell(FalloutPluginStack records, string image)
    {
        using var world = new FalloutReferenceWorld(records);
        world.LoadCell(FalloutCellSceneReader.Read(records, Cell));
        world.ConfigureCombatGroups(FalloutCombatGroupDeclaration.ForExecutable(image), Selection);
        world.ConfigureActorUpdates(FalloutActorUpdateDeclaration.ForExecutable(image), Selection);
        world.ConfigureCellProcesses(FalloutCellProcessDeclaration.ForExecutable(image), Selection);
        Require(world.SourceActorAiEnabled(Npc) && world.IsEnabled(Npc), "Real actor constructor/enable owners disagree before a command.");
        var scripts = new FalloutReferenceScripts(records, world, new FalloutQuestState(records),
            new((_, _) => false, _ => throw new InvalidDataException("AI command borrowed a presentation effect.")));
        var result = scripts.Dispatch(Npc, "GameMode");
        Require(result is { Error: null, Blocks: 1 } && world.Get(Npc).Read(1) == 1 && world.SourceActorAiEnabled(Npc) && world.IsEnabled(Npc),
            "Actual SCTX/AI query consumer did not preserve Set/query/Toggle order: " + result.Error);
        world.SetActorsAi(Npc, 0, Creature);
        Require(!world.SourceActorAiEnabled(Npc) && world.IsEnabled(Npc) && world.SourceActorAiEnabled(Creature), "AI byte was substituted by reference Enabled.");
        world.SetPlacement(Npc, new(OtherCell, [10, 20, 30], [0, 0, 0]));
        Require(world.ReadSourceActorCellPhase(Npc).Require() == 0, "Current actor CELL was invented from old residency.");
        Reject(() => world.ReadSourceActorCellPhase(records.RuntimeFormKey(0x14)).Require());
        using (world.BindActualPlayerProcessCell(() => Cell))
            Require(world.ReadSourceActorCellPhase(records.RuntimeFormKey(0x14)).Require() == 0, "Player current CELL publication was replaced by cohort membership.");
        Reject(() => world.ReadSourceActorCellPhase(records.RuntimeFormKey(0x14)).Require());
    }
    private static void Complete(FalloutCellProcesses owner, Guid lease, ulong first)
    {
        var i = first;
        foreach (var child in owner.ReadAttachment(lease).Children)
            owner.CompleteChild(lease, child.Source, FalloutCellProcessChildPhase.Published, [i++], "authored-structural-child-protocol");
    }
    private static void Retire(FalloutCellProcesses owner, Guid lease, ulong root)
    {
        Require(owner.BeginDetach(lease, root, "authored-detach-entered"), "Detach guard rejected actual5/6.");
        foreach (var child in owner.ReadAttachment(lease).Children) owner.RetireChild(lease, child.Source, "authored-structural-child-retirement");
        foreach (var cell in owner.ReadAttachment(lease).CellConsumers)
            owner.RetireNativeCell(lease, cell.Source.Cell.Cell, "authored-structural-cell-scope-retirement");
        owner.CompleteDetach(lease, root, "authored-structural-root-retirement");
    }
    private static byte[] Plugin() => Join(Record("TES4", 0, Field("HEDR", new byte[12])), Actor("NPC_", 7),
        Actor("NPC_", 0x700, 0x750), Actor("CREA", 0x701), Record("MISC", 0x702), Script(),
        Record("CELL", 0x800, Field("DATA", [1, 0])), Group(0x800, Reference("ACHR", 0x900, 0x700),
            Reference("ACRE", 0x901, 0x701), Reference("REFR", 0x902, 0x702)), Record("CELL", 0x801, Field("DATA", [1, 0])));
    private static byte[] Override() => Join(Record("TES4", 0, Field("HEDR", new byte[12]),
        Field("MAST", Text("State.esm")), Field("DATA", new byte[8])), Group(0x800,
        Reference("ACHR", 0x900, 0x700, 9)));
    private static byte[] Actor(string signature, uint id, uint? script = null) => Record(signature, id,
        Field("ACBS", new byte[24]), Field("AIDT", new byte[20]), script is { } key ? Field("SCRI", BitConverter.GetBytes(key)) : []);
    private static byte[] Reference(string type, uint id, uint basis, float x = 0)
    {
        var data = new byte[24]; BinaryPrimitives.WriteSingleLittleEndian(data, x);
        return Record(type, id, Field("NAME", BitConverter.GetBytes(basis)), Field("DATA", data));
    }
    private static byte[] Script()
    {
        var local = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(local, 1);
        var header = new byte[20]; BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 1); header[16] = 1;
        return Record("SCPT", 0x750, Field("SCHR", header), Field("SLSD", local), Field("SCVR", Text("observed")),
            Field("SCTX", Text("begin GameMode\nSetActorsAI 0\nset observed to IsActorsAIOff\nToggleActorsAI\nend")));
    }
    private static byte[] Group(uint cell, params byte[][] children)
    {
        var data = Join(children); var result = new byte[data.Length + 24]; Encoding.ASCII.GetBytes("GRUP").CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), checked((uint)result.Length)); BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), cell);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(12), 6); data.CopyTo(result, 24); return result;
    }
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var data = Join(fields); var result = new byte[data.Length + 24]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), checked((uint)data.Length)); BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), id);
        data.CopyTo(result, 24); return result;
    }
    private static byte[] Field(string signature, byte[] data)
    {
        var result = new byte[data.Length + 6]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(result, 6); return result;
    }
    private static byte[] Text(string value) => Encoding.UTF8.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static FalloutFormKey Key(uint id) => new("State.esm", id);
    private static T RoundTrip<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static void Require(bool value, string error) { if (!value) throw new InvalidDataException(error); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or KeyNotFoundException) { return; }
        throw new InvalidDataException("Foreign/missing/incomplete source actor or CELL state was admitted.");
    }
}
