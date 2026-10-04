using System.Buffers.Binary;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class FurnitureContracts
{
    private static void StoppedProcedureContracts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-stopped-procedures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var selectionData = new byte[12]; selectionData[4] = 6;
            var condition = new byte[28]; BinaryPrimitives.WriteUInt16LittleEndian(condition.AsSpan(8), 84);
            var dialogueData = new byte[12]; dialogueData[4] = 15;
            var declaration = new byte[24]; BinaryPrimitives.WriteSingleLittleEndian(declaration, 100);
            var idleTiming = new byte[8]; BinaryPrimitives.WriteUInt16LittleEndian(idleTiming.AsSpan(4), 4);
            File.WriteAllBytes(Path.Combine(directory, "Procedures.esm"), Join(Header(),
                Record("NPC_", 0x700, Field("ACBS", new byte[24]), Field("PKID", BitConverter.GetBytes(0x710u))),
                Record("NPC_", 0x701, Field("ACBS", new byte[24])), Record("CELL", 0x800, Field("DATA", [1])),
                Record("IDLE", 0x720, Field("DATA", idleTiming)),
                Record("PACK", 0x710, Field("EDID", Text("SyntheticFailedPredicate")), Field("PKDT", selectionData), Field("CTDA", condition)),
                Record("PACK", 0x711, Field("EDID", Text("SyntheticDialogueWait")), Field("PKDT", dialogueData), Field("PKDD", declaration),
                    Field("PTDT", Join(BitConverter.GetBytes(0), BitConverter.GetBytes(0x14u), BitConverter.GetBytes(260), new byte[4]))),
                ReferencesForContinuation()));
            using var records = FalloutPluginStack.Load(directory, ["Procedures.esm"]);
            FalloutFormKey Key(uint value) => new("Procedures.esm", value);
            var actor = Key(0x900); var other = Key(0x901);
            using var world = new FalloutReferenceWorld(records);
            var cell = FalloutCellSceneReader.Read(records, Key(0x800)); world.LoadCell(cell);
            var owner = world.Get(actor);
            var hash = new string('A', 64);
            owner.Animation.Restore(new("meshes/fixture-idle.kf", hash, .3125, false));
            float[] pose = [1, 0, 0, 0, 1, 0, 0, 0, 1, 3, 2, 1];
            var package = records.GetEffective(Key(0x710));
            var failure = new FalloutActorSelectionFailure(package.FormKey, FalloutActorFurnitureContinuation.RecordHash(package), 0,
                "Synthetic reached source predicate fault.", pose, 12345, 8.25, null, false, new(0, null, null, null), null);
            owner.SelectionFailure = failure; owner.ProcedureCaptureBlocker = failure.Error;
            var saved = world.Capture();
            using (var cold = new FalloutReferenceWorld(records))
            {
                cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(saved))!);
                Require(cold.PendingProcedureCaptureCount == 0 &&
                    JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(saved),
                    "Failed predicate cold restore changed its source cursor, clock, fault or random state.");
            }
            void RejectSave(FalloutReferenceSnapshot[] candidate)
            {
                using var cold = new FalloutReferenceWorld(records);
                Reject(() => cold.Restore(candidate));
                Require(cold.InstanceCount == 0, "Rejected procedure save partially published references.");
            }
            FalloutReferenceSnapshot[] ChangedFailure(FalloutActorSelectionFailure value) => saved.Select(snapshot =>
                snapshot.Reference == actor ? snapshot with { SelectionFailure = value } : snapshot).ToArray();
            RejectSave(ChangedFailure(failure with { Sha256 = new string('B', 64) }));
            RejectSave(ChangedFailure(failure with { Condition = 1 }));
            RejectSave(ChangedFailure(failure with { PollRemaining = double.NaN }));
            RejectSave(saved.Select(snapshot => snapshot.Reference == other ? snapshot with
                { SelectionFailure = failure, Animation = owner.Animation.Capture() } : snapshot).ToArray());
            owner.SelectionFailure = null;
            package = records.GetEffective(Key(0x711));
            var assignment = new FalloutActorPackageAssignment(package.FormKey, FalloutActorFurnitureContinuation.RecordHash(package), false);
            var cooldown = new FalloutIdleReplayCooldown(Key(0x720), FalloutActorFurnitureContinuation.RecordHash(records.GetEffective(Key(0x720))), 1.25f);
            var idles = new FalloutActorPackageIdleState(package.FormKey, assignment.Sha256, new(0, 0, 0, false), [cooldown],
                "Synthetic retained idle fault.");
            var dialogue = new FalloutActorDialogueContinuation(assignment, 1, "POBA", package.FormKey, assignment.Sha256,
                pose, [3, 2, 1], true, true, 67890, 2.75, null, null, idles);
            owner.PackageAssignment = assignment; owner.DialogueContinuation = dialogue;
            owner.ProcedureCaptureBlocker = FalloutActorDialogueContinuation.CaptureBlocker;
            saved = world.Capture();
            using (var cold = new FalloutReferenceWorld(records))
            {
                cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(saved))!);
                Require(cold.PendingProcedureCaptureCount == 0 && JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(saved),
                    "Cold dialogue wait changed lifecycle history, source wait position, idle fault or replay delay.");
            }
            FalloutReferenceSnapshot[] ChangedDialogue(FalloutActorDialogueContinuation value) => saved.Select(snapshot =>
                snapshot.Reference == actor ? snapshot with { DialogueContinuation = value } : snapshot).ToArray();
            RejectSave(ChangedDialogue(dialogue with { WaitReached = false }));
            RejectSave(ChangedDialogue(dialogue with { NativeMovement = false }));
            RejectSave(ChangedDialogue(dialogue with { TargetFloor = [0, 0, 0] }));
            RejectSave(ChangedDialogue(dialogue with { IdleState = idles with { Collection = new(1, 1, 0, false) } }));
            RejectSave(ChangedDialogue(dialogue with { IdleState = idles with { Cooldowns = [cooldown, cooldown] } }));
            RejectSave(ChangedDialogue(dialogue with { IdleState = idles with { Cooldowns = [cooldown with { Remaining = 5 }] } }));
            var replay = new FalloutIdleReplayState(); replay.Restore(new Dictionary<FalloutFormKey, float> { [cooldown.Idle] = cooldown.Remaining });
            replay.Advance(.5f);
            Require(replay.Remaining[cooldown.Idle] == .75f, "Cold actor replay delay did not retain its consumed fraction.");
            try { replay.Restore(new Dictionary<FalloutFormKey, float>()); throw new InvalidDataException("Replay delay restored into a running owner."); }
            catch (InvalidOperationException) { }
            Console.WriteLine("OPENNV_STOPPED_PROCEDURE_COLD_PASS failedPredicate=true sourceCursor=true randomAndPoll=true dialogueWait=true lifecycle=true idleFault=true replayDelay=true atomicReject=true native=separate-audit");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static byte[] ReferencesForContinuation()
    {
        var body = Join(Record("ACHR", 0x900, Field("NAME", BitConverter.GetBytes(0x700u)), Field("DATA", new byte[24])),
            Record("ACHR", 0x901, Field("NAME", BitConverter.GetBytes(0x701u)), Field("DATA", new byte[24])));
        var group = new byte[24 + body.Length]; System.Text.Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), 0x800); BinaryPrimitives.WriteInt32LittleEndian(group.AsSpan(12), 6);
        body.CopyTo(group, 24); return group;
    }
}
