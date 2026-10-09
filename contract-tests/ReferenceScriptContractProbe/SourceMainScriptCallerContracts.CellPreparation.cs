using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunPlayerCellPreparationContracts()
    {
        CellPreparationCurrentAndCold(); StandaloneCellPreparationCurrentAndCold();
        Console.WriteLine("OPENNV_PLAYER_INTERIOR_CELL_PREPARATION_PASS authoredValuesOnly=true nonConsuming=true exactPending=true " +
            "sourceParentAndFloat32=true newProcessCold=true replacedOwnerRefused=true prematureCompletionRefused=true " +
            "worldspaceFistpMainNativeAndVaultProgression=UNEXECUTED");
    }

    private static void CellPreparationCurrentAndCold()
    {
        using var warm = new Fixture();
        var source = FalloutMainPlayerCellSource.Read(warm.Host.Source); var slot = new FalloutPlayerPendingSlot();
        warm.Owner.ConstructMainPlayerCell(source, slot, null);
        var rawSource = FalloutPlayerRawTransferSource.Read(FalloutMainPlayerPendingSource.Read(source));
        var (request, cell) = StorePreparationInput(rawSource, slot);
        const string stack = "authored-source-Main-caller";
        using var prepared = FalloutPlayerCellPreparation.Create(rawSource, stack,
            warm.Owner.CharacterControllerSourceProcess, slot, request, cell);
        PreparationPureCases(prepared, rawSource, stack, warm.Owner.CharacterControllerSourceProcess, slot, cell);
        var saved = JsonSerializer.Deserialize<FalloutPlayerCellPreparationSnapshot>(JsonSerializer.Serialize(prepared.Capture()))!;
        var player = warm.Owner.CaptureMainPlayerCell();
        var transportedPending = JsonSerializer.Deserialize<FalloutPlayerPendingSlotSnapshot>(JsonSerializer.Serialize(player.Pending))!;
        using var cold = new Fixture(warm.Owner.Capture(), warm.Owner.CaptureMainScriptFrameEvidence(),
            warm.Owner.CaptureMainScriptCaller(), warm.Fade.Capture());
        var coldSlot = new FalloutPlayerPendingSlot();
        cold.Owner.ConstructMainPlayerCell(source, coldSlot, player with { Pending = transportedPending });
        using var restored = FalloutPlayerCellPreparation.Restore(saved, rawSource, stack,
            cold.Owner.CharacterControllerSourceProcess, coldSlot, cell);
        Require(restored.Capture().Handoff is { } handoff && handoff.PreviousProcess == saved.CapturedProcess &&
            handoff.CurrentProcess == cold.Owner.CharacterControllerSourceProcess && handoff.PreviousPreparation == saved.Identity &&
            handoff.CurrentPreparation != saved.Identity && ReferenceEquals(restored.Request, coldSlot.Next) &&
            restored.Payload == prepared.Payload && cold.Owner.CaptureMainPlayerCell().Calls == 0 &&
            cold.Owner.Capture().Fistp.Conversions == 0,
            "Cold preload replayed Main/conversion, changed captured values or reused an old process/preparation.");
        Reject(() => FalloutPlayerCellPreparation.Restore(saved, rawSource, stack, saved.CapturedProcess, coldSlot, cell));
        Reject(() => FalloutPlayerCellPreparation.Restore(saved, rawSource, "foreign-stack", cold.Owner.CharacterControllerSourceProcess, coldSlot, cell));
        Reject(() => FalloutPlayerCellPreparation.Restore(saved with { Request = saved.Request with { Identity = Guid.NewGuid() } },
            rawSource, stack, cold.Owner.CharacterControllerSourceProcess, coldSlot, cell));
        Reject(() => FalloutPlayerCellPreparation.Restore(saved with { Request = saved.Request with
            { SourcePayload = saved.Request.SourcePayload! with { PositionX = saved.Request.SourcePayload!.PositionX + 1 } } },
            rawSource, stack, cold.Owner.CharacterControllerSourceProcess, coldSlot, cell));
        Reject(() => FalloutPlayerCellPreparation.Restore(saved, rawSource, stack,
            cold.Owner.CharacterControllerSourceProcess, coldSlot, cell with { Source = cell.Source with { Sha256 = new('d', 64) } }));
        Require(slot.Capture().Completion is null && coldSlot.Capture().Completion is null,
            "Preparation/cold admission wrote a Player completion receipt.");
        PreparationRefusalCases(rawSource, stack, warm.Owner.CharacterControllerSourceProcess, cell);
        slot.Complete(request, "authored-preload-only-premature-null-store");
        Reject(() => prepared.RequireReturned(source, stack, warm.Owner.CharacterControllerSourceProcess, null, slot));
        using (warm.Owner.BindMainPlayerCell(new AuthoredPlayerCell(source), "authored-unexecuted-child-binding"))
            Reject(() => warm.Owner.RequireReturnedPlayerCellPreparation(prepared));
    }

    private static void StandaloneCellPreparationCurrentAndCold()
    {
        using var warm = new StandaloneFixture();
        var rawSource = FalloutPlayerRawTransferSource.Read(FalloutMainPlayerPendingSource.Read(warm.Owner.MainPlayerCellSource));
        var (request, cell) = StorePreparationInput(rawSource, warm.Slot);
        const string stack = "authored-FO3-Main";
        using var prepared = FalloutPlayerCellPreparation.Create(rawSource, stack,
            warm.Owner.CharacterControllerSourceProcess, warm.Slot, request, cell);
        PreparationPureCases(prepared, rawSource, stack, warm.Owner.CharacterControllerSourceProcess, warm.Slot, cell);
        var saved = JsonSerializer.Deserialize<FalloutPlayerCellPreparationSnapshot>(JsonSerializer.Serialize(prepared.Capture()))!;
        using var cold = new StandaloneFixture(warm.Owner.Capture(), warm.Fade.Capture());
        using var restored = FalloutPlayerCellPreparation.Restore(saved, rawSource, stack,
            cold.Owner.CharacterControllerSourceProcess, cold.Slot, cell);
        Require(restored.Payload.SourceTailWord == 0 && cold.Owner.Capture().Fistp.Conversions == 0 &&
            cold.Owner.CaptureMainPlayerCell().Calls == 0 && restored.Capture().Handoff?.PreviousProcess == saved.CapturedProcess,
            "FO3 interior preparation imported FNV fields, performed FISTP or replayed pending consumers during cold rebind.");
        var replacement = warm.Slot.StoreSource(FalloutPlayerPendingKind.MoveTo, request.Move, null,
            "authored-source-payload-replacement", request.SourcePayload!);
        Require(replacement.Identity != request.Identity, "Replacement unexpectedly reused the earlier source invocation.");
        Reject(() => prepared.RequireCurrent(rawSource, stack, warm.Owner.CharacterControllerSourceProcess, warm.Slot, cell));
        Reject(() => prepared.Capture());
        prepared.Dispose();
        Reject(() => _ = prepared.Placement);
        Require(ReferenceEquals(warm.Slot.Next, replacement) && warm.Slot.Capture().Completion is null,
            "Disposed/superseded preparation consumed another request or erased its original allocation.");
    }

    private static (FalloutPlayerPendingRequest Request, FalloutMainPlayerSourceCell Cell) StorePreparationInput(
        FalloutPlayerRawTransferSource source, FalloutPlayerPendingSlot slot)
    {
        var cell = new FalloutMainPlayerSourceCell(new(new("Authored.esm", 50), 0, new('c', 64), null, null), 1, null, null);
        var receipt = new FalloutPlayerRawTransferReceipt(source, FalloutPlayerRawTransferWriter.MoveTo, Guid.NewGuid(),
            new("Authored.esm", 20), new('a', 64), new("Authored.esm", 30), new('b', 64), cell.Source, cell.CellFlags,
            FalloutPlayerTransferVector.Read([16_777_216f, -4095.75f, 30]),
            FalloutPlayerTransferVector.Read([BitConverter.UInt32BitsToSingle(0x80000000), .7f, .3f]),
            FalloutPlayerTransferVector.Read([1, 2, -3]), "authored-real-writer-placement-value-input");
        var move = new FalloutPlayerMove(receipt.RequestSource, receipt.Target, 1, 2, -3);
        return (slot.StoreSource(FalloutPlayerPendingKind.MoveTo, move, null, "authored-MoveTo-command-value-input",
            FalloutPlayerRawTransferFactory.MoveTo(receipt)), cell);
    }

    private static void PreparationPureCases(FalloutPlayerCellPreparation prepared, FalloutPlayerRawTransferSource source,
        string stack, Guid process, FalloutPlayerPendingSlot slot, FalloutMainPlayerSourceCell cell)
    {
        var before = slot.Capture();
        prepared.RequireCurrent(source, stack, process, slot, cell);
        var placement = prepared.Placement;
        Require(placement.Cell == cell.Source.Cell && placement.Position.Select(BitConverter.SingleToUInt32Bits)
                .SequenceEqual(new[] { prepared.Payload.PositionX, prepared.Payload.PositionY, prepared.Payload.PositionZ }) &&
            placement.RotationRadians.Select(BitConverter.SingleToUInt32Bits)
                .SequenceEqual(new[] { prepared.Payload.RotationX, prepared.Payload.RotationY, prepared.Payload.RotationZ }) &&
            prepared.Payload.RotationX == 0x80000000 && prepared.Payload.RotationY == 0,
            "Interior projection changed the captured Float32 stores or lost signed-zero rotation.");
        placement.Position[0] = -1; placement.RotationRadians[0] = 4;
        Require(BitConverter.SingleToUInt32Bits(prepared.Placement.Position[0]) == prepared.Payload.PositionX &&
            BitConverter.SingleToUInt32Bits(prepared.Placement.RotationRadians[0]) == prepared.Payload.RotationX,
            "A native preload mutated the authoritative captured placement arrays.");
        var movedTarget = FalloutPlayerRawTransferFactory.MoveTo(prepared.Payload.FactoryReceipt! with
            { TargetPosition = FalloutPlayerTransferVector.Read([100, 200, 300]) });
        Require(movedTarget.PositionX != prepared.Payload.PositionX && slot.Capture() == before && before.Completion is null,
            "Projection reread a moved target, entered the pending child or manufactured a null store.");
        Reject(() => prepared.RequireCurrent(source, stack, Guid.NewGuid(), slot, cell));
        Reject(() => prepared.RequireCurrent(source, stack, process, slot, cell with { CellFlags = 0 }));
        Reject(() => FalloutPlayerCellPreparation.Create(source, stack, process, slot, prepared.Request with { }, cell));
        using (slot.Enter(prepared.Request)) Reject(() => prepared.Capture());
        Require(slot.Capture() == before, "An entered-preload admission check changed the source pending state.");
    }

    private static void PreparationRefusalCases(FalloutPlayerRawTransferSource source, string stack,
        Guid process, FalloutMainPlayerSourceCell cell)
    {
        var slot = new FalloutPlayerPendingSlot(); slot.BindSourceFamily(source.Pending.Player);
        var (request, _) = StorePreparationInput(source, slot);
        var payload = request.SourcePayload!;
        var receipt = payload.FactoryReceipt!;
        var exteriorReceipt = receipt with { ParentCell = cell.Source with
            { Worldspace = new("Authored.esm", 2), WorldspaceSha256 = new('d', 64) }, ParentCellFlags = 0 };
        var outside = slot.StoreSource(FalloutPlayerPendingKind.MoveTo, request.Move, null, "authored-exterior-value-input",
            FalloutPlayerRawTransferFactory.MoveTo(exteriorReceipt));
        Reject(() => FalloutPlayerCellPreparation.Create(source, stack, process, slot, outside,
            new(exteriorReceipt.ParentCell, 0, 0, 0)));
        var drift = slot.StoreSource(FalloutPlayerPendingKind.MoveTo, request.Move, null, "authored-one-bit-payload-drift",
            payload with { PositionX = payload.PositionX + 1 });
        Reject(() => FalloutPlayerCellPreparation.Create(source, stack, process, slot, drift, cell));
        var logical = slot.Store(FalloutPlayerPendingKind.MoveTo, request.Move, null, "authored-logical-only-request");
        Reject(() => FalloutPlayerCellPreparation.Create(source, stack, process, slot, logical, cell));
        Require(ReferenceEquals(slot.Next, logical) && slot.Capture().Completion is null,
            "A refused unsupported or drifted preparation consumed its genuine allocation.");
    }
}
