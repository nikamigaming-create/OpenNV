using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal static class FalloutPlayerRawTransferFactory
{
    internal static FalloutPlayerTransferPayload MoveTo(FalloutPlayerRawTransferReceipt receipt)
    {
        Require(receipt, FalloutPlayerRawTransferWriter.MoveTo);
        var position = Add(receipt.TargetPosition, receipt.Offsets);
        return Payload(receipt, position, new(receipt.TargetRotation.X, 0, receipt.TargetRotation.Z), 1, null,
            receipt.Target);
    }

    internal static FalloutPlayerTransferPayload Door(FalloutPlayerRawTransferReceipt receipt)
    {
        Require(receipt, FalloutPlayerRawTransferWriter.Door);
        if (receipt.Offsets != new FalloutPlayerTransferVector(0, 0, 0))
            throw new InvalidDataException("Door factory cannot acquire MoveTo offset cells.");
        var callback = new FalloutPlayerTransferCallback(receipt.Allocation, receipt.Source.Pending.Contract,
            "source-directed-door-post-transfer", receipt.RequestSource);
        return Payload(receipt, receipt.TargetPosition, receipt.TargetRotation, 0, callback, null);
    }

    internal static void Require(FalloutPlayerTransferPayload payload, FalloutPlayerRawTransferSource source)
    {
        source.Validate(); payload.Validate(source.Pending);
        var receipt = payload.FactoryReceipt ?? throw new NotSupportedException("Player raw payload has no actual allocation/writer receipt.");
        if (receipt.Source != source) throw new InvalidDataException("Player raw factory changed selected source ownership.");
        var expected = receipt.Writer switch
        {
            FalloutPlayerRawTransferWriter.MoveTo => MoveTo(receipt),
            FalloutPlayerRawTransferWriter.Door => Door(receipt),
            _ => throw new InvalidDataException("Player raw factory writer is absent or unknown.")
        };
        if (payload != expected) throw new InvalidDataException("Player raw fields drifted from their real constructor/writer.");
    }

    private static FalloutPlayerTransferPayload Payload(FalloutPlayerRawTransferReceipt receipt,
        FalloutPlayerTransferVector position, FalloutPlayerTransferVector rotation, byte argument,
        FalloutPlayerTransferCallback? callback, FalloutFormKey? furniture)
    {
        var interior = (receipt.ParentCellFlags & 1) != 0;
        return new(receipt.Source.Pending.Contract, receipt.Writer == FalloutPlayerRawTransferWriter.MoveTo ?
            "source-Player-MoveTo-pending-allocation" : "source-DOOR-XTEL-pending-allocation",
            interior ? receipt.ParentCell.Cell : null, interior ? null : receipt.ParentCell.Worldspace, null,
            position.X, position.Y, position.Z, rotation.X, rotation.Y, rotation.Z, argument, callback, furniture, receipt,
            receipt.Source.Pending.Player.Main.HasNewVegasChildren ? null : 0u);
    }

    private static void Require(FalloutPlayerRawTransferReceipt receipt, FalloutPlayerRawTransferWriter writer)
    {
        if (receipt is null || receipt.Source is null || receipt.Writer != writer || !Enum.IsDefined(writer) ||
            receipt.Allocation == Guid.Empty || !Key(receipt.RequestSource) || !Key(receipt.Target) ||
            !Digest(receipt.RequestSha256) || !Digest(receipt.TargetSha256) || receipt.ParentCell is null ||
            !Key(receipt.ParentCell.Cell) || !Digest(receipt.ParentCell.Sha256) ||
            (receipt.ParentCell.Worldspace is { } world && (!Key(world) || !Digest(receipt.ParentCell.WorldspaceSha256))) ||
            (receipt.ParentCellFlags & 1) == 0 && receipt.ParentCell.Worldspace is null ||
            receipt.TargetPosition is null || receipt.TargetRotation is null || receipt.Offsets is null ||
            string.IsNullOrWhiteSpace(receipt.PlacementOwner))
            throw new InvalidDataException("Player raw allocation omitted its original target, source ancestry or value writer.");
        receipt.Source.Validate(); receipt.TargetPosition.Validate(); receipt.TargetRotation.Validate(); receipt.Offsets.Validate();
    }

    private static FalloutPlayerTransferVector Add(FalloutPlayerTransferVector first, FalloutPlayerTransferVector second)
    {
        var a = first.Values; var b = second.Values;
        // Float32 source operands add in the original wide arithmetic and are
        // stored to Float32 before the constructor payload is published.
        return FalloutPlayerTransferVector.Read([(float)((double)a[0] + b[0]),
            (float)((double)a[1] + b[1]), (float)((double)a[2] + b[2])]);
    }
    private static bool Key(FalloutFormKey key) => key.ObjectId is > 0 and <= FalloutFormKey.ObjectIdMask && !string.IsNullOrWhiteSpace(key.OwnerPlugin);
    private static bool Digest(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
