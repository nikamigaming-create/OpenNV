using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunRawPlayerTransferContracts()
    {
        using var fixture = new Fixture();
        var pending = FalloutMainPlayerPendingSource.Read(FalloutMainPlayerCellSource.Read(fixture.Host.Source));
        var source = FalloutPlayerRawTransferSource.Read(pending);
        var cell = new FalloutCellProcessIdentity(new("Authored.esm", 50), 0, new('c', 64), null, null);
        var receipt = new FalloutPlayerRawTransferReceipt(source, FalloutPlayerRawTransferWriter.MoveTo, Guid.NewGuid(),
            new("Authored.esm", 20), new('a', 64), new("Authored.esm", 30), new('b', 64), cell, 1,
            FalloutPlayerTransferVector.Read([10, 20, 30]), FalloutPlayerTransferVector.Read([.1f, .7f, .3f]),
            FalloutPlayerTransferVector.Read([2, -3, 4]), "authored-current-source-placement-input");
        var raw = FalloutPlayerRawTransferFactory.MoveTo(receipt);
        Require(raw.Target == FalloutPlayerTransferTarget.Cell && raw.Cell == cell.Cell && raw.Worldspace is null &&
            raw.Reference is null && raw.Callback is null && raw.Furniture == receipt.Target && raw.TransferArgument == 1 &&
            raw.PositionX == BitConverter.SingleToUInt32Bits(12) && raw.PositionY == BitConverter.SingleToUInt32Bits(17) &&
            raw.PositionZ == BitConverter.SingleToUInt32Bits(34) && raw.RotationX == receipt.TargetRotation.X && raw.RotationY == 0 &&
            raw.RotationZ == receipt.TargetRotation.Z, "Raw MoveTo lost original constructor/write cells or copied unwritten rotation Y.");
        FalloutPlayerRawTransferFactory.Require(raw, source);
        var stored = JsonSerializer.Deserialize<FalloutPlayerTransferPayload>(JsonSerializer.Serialize(raw))!;
        FalloutPlayerRawTransferFactory.Require(stored, source);
        Require(stored == raw && raw.PositionX != FalloutPlayerRawTransferFactory.MoveTo(receipt with
        { TargetPosition = FalloutPlayerTransferVector.Read([100, 200, 300]) }).PositionX,
            "Retained raw values were replaced by a later destination or lost during value-only cold transport.");
        Reject(() => FalloutPlayerRawTransferFactory.Require(raw with { RotationY = receipt.TargetRotation.Y }, source));
        Reject(() => FalloutPlayerRawTransferFactory.Require(raw with { TransferArgument = 0 }, source));
        Reject(() => FalloutPlayerRawTransferFactory.Require(raw with { FactoryReceipt = null }, source));
        Reject(() => FalloutPlayerRawTransferFactory.Require(raw with { PositionX = 0 }, source));
        Reject(() => FalloutPlayerRawTransferFactory.Require(raw with { Callback = new(Guid.NewGuid(), pending.Contract, "foreign", receipt.RequestSource) }, source));

        var exterior = receipt with { ParentCell = cell with { Worldspace = new("Authored.esm", 2), WorldspaceSha256 = new('d', 64) }, ParentCellFlags = 0 };
        var outside = FalloutPlayerRawTransferFactory.MoveTo(exterior);
        Require(outside.Target == FalloutPlayerTransferTarget.Worldspace && outside.Cell is null && outside.Worldspace == exterior.ParentCell.Worldspace,
            "Raw exterior parent world was confused with the direct interior CELL pointer.");
        Require((outside with { Cell = cell.Cell }).Target == FalloutPlayerTransferTarget.Worldspace &&
            (outside with { Cell = cell.Cell, Reference = receipt.Target }).Target == FalloutPlayerTransferTarget.Reference,
            "Original reference/world/CELL pointer priority changed.");
        Reject(() => FalloutPlayerRawTransferFactory.MoveTo(exterior with { ParentCell = cell }));

        var door = FalloutPlayerRawTransferFactory.Door(receipt with { Writer = FalloutPlayerRawTransferWriter.Door,
            Offsets = new(0, 0, 0), PlacementOwner = "authored-directed-XTEL-input" });
        Require(door.TransferArgument == 0 && door.Furniture is null && door.RotationY == receipt.TargetRotation.Y &&
            door.PositionZ == receipt.TargetPosition.Z && door.Callback is { } callback && callback.Context == receipt.RequestSource &&
            callback.Factory == receipt.Allocation, "Door allocation lost source XTEL values, nonnull handler or actual reference context.");
        FalloutPlayerRawTransferFactory.Require(door, source);
        Reject(() => FalloutPlayerRawTransferFactory.Require(door with { Callback = null }, source));
        Reject(() => FalloutPlayerRawTransferFactory.Require(door with { Callback = door.Callback! with { Context = receipt.Target } }, source));
        RawControllerFieldsAndColdMetadata(pending);
        Console.WriteLine("OPENNV_SOURCE_PLAYER_RAW_TRANSFER_PASS authoredValues=true pointerOrder=true enqueueValuesRetained=true " +
            "rotationPartialWrite=true doorRealContext=true exactDriftRefusal=true controllerType=true coldFieldChain=true " +
            "nativeFactorySkyTlsAndMovement=UNEXECUTED");
    }

    private static void RawControllerFieldsAndColdMetadata(FalloutMainPlayerPendingSource pending)
    {
        var source = FalloutCharacterControllerSource.Read(pending);
        var player = new FalloutFormKey("Authored.esm", 0x14);
        var actor = new FalloutCombatActorIdentity(player, new("Authored.esm", 7), "ENGINE_PLAYER", 0,
            pending.Player.Main.EngineSha256, "NPC_", 0, new('b', 64), true);
        // These authored typed declarations exercise structural admission only;
        // no delegate impersonates an actual native body or successful factory.
        var body = new FalloutActorProcessBodyBinding(player, "meshes/authored/skeleton.nif", new('e', 64),
            new("Authored.esm", 60), new('f', 64), [], null, null, null, null, null, null, "authored-source-body-input");
        var firstProcess = Guid.NewGuid(); var firstController = Guid.NewGuid();
        var factory = new FalloutCharacterControllerFactory(source, new('a', 64), firstProcess, 1, FalloutDetectionProcessLevel.High,
            actor, body, "authored-source-character-controller-input");
        var initialized = new FalloutCharacterControllerSnapshot(FalloutCharacterControllerState.Schema, factory,
            firstController, 0, 0, null, null, null, []);
        FalloutCharacterControllerState.Validate(initialized);
        Reject(() => FalloutCharacterControllerState.Validate(initialized with { ScalarBits = 1 }));
        Reject(() => FalloutCharacterControllerState.RequireFactory(factory with { Level = FalloutDetectionProcessLevel.Low }));
        Reject(() => FalloutCharacterControllerState.RequireFactory(factory with { Actor = actor with { ReferenceSha256 = new('f', 64) } }));
        var bits = BitConverter.SingleToUInt32Bits(34);
        var store = new FalloutCharacterControllerFieldStore(Guid.NewGuid(), Guid.NewGuid(), bits, 1,
            "authored-field-store-input", firstProcess, firstController, 1);
        var secondProcess = Guid.NewGuid(); var secondController = Guid.NewGuid();
        var thirdProcess = Guid.NewGuid(); var thirdController = Guid.NewGuid();
        var cold = initialized with { Factory = factory with { Process = thirdProcess }, Identity = thirdController, ScalarBits = bits,
            Sequence = 3, LastStore = store, Handoffs = [new(firstProcess, secondProcess, firstController, secondController, 2),
                new(secondProcess, thirdProcess, secondController, thirdController, 3)] };
        FalloutCharacterControllerState.Validate(cold);
        FalloutCharacterControllerState.Validate(JsonSerializer.Deserialize<FalloutCharacterControllerSnapshot>(JsonSerializer.Serialize(cold))!);
        Reject(() => FalloutCharacterControllerState.Validate(cold with { Handoffs = [cold.Handoffs[1]] }));
        Reject(() => FalloutCharacterControllerState.Validate(cold with { Handoffs = [cold.Handoffs[0], cold.Handoffs[1] with { PreviousController = Guid.NewGuid() }] }));
        Reject(() => FalloutCharacterControllerState.Validate(cold with { ScalarBits = 0 }));
        Require(FalloutCharacterControllerState.HeightDeltaBits(0x80000000, bits) == 0 &&
            FalloutCharacterControllerState.HeightDeltaBits(BitConverter.SingleToUInt32Bits(12.5f), BitConverter.SingleToUInt32Bits(10.25f)) ==
            BitConverter.SingleToUInt32Bits(2.25f), "Controller scalar getter changed signed-zero or stored Float32 subtraction rules.");
    }
}
