using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutPlayerTransferTarget { Empty, Cell, Worldspace, Reference }
internal sealed record FalloutPlayerTransferCallback(Guid Factory, string SourceContract, string Handler, FalloutFormKey Context);

// The third position cell is also consumed by the actual source physics
// character-controller scalar store. Raw constructor/write fields and callback
// context have their own retained factory receipt; no runtime defaults infer them.
internal sealed record FalloutPlayerTransferPayload(string SourceContract, string Factory,
    FalloutFormKey? Cell, FalloutFormKey? Worldspace, FalloutFormKey? Reference,
    uint PositionX, uint PositionY, uint PositionZ,
    uint RotationX, uint RotationY, uint RotationZ, byte TransferArgument,
    FalloutPlayerTransferCallback? Callback, FalloutFormKey? Furniture, FalloutPlayerRawTransferReceipt? FactoryReceipt = null, uint? SourceTailWord = null)
{
    // All three original pointer cells are retained. Selection follows the
    // original reference-first, then worldspace, then CELL branch; a losing
    // nonnull field is not discarded by a convenient tagged union.
    internal FalloutPlayerTransferTarget Target => Reference is not null ? FalloutPlayerTransferTarget.Reference :
        Worldspace is not null ? FalloutPlayerTransferTarget.Worldspace : Cell is not null ? FalloutPlayerTransferTarget.Cell : FalloutPlayerTransferTarget.Empty;
    internal FalloutFormKey? TargetForm => Reference ?? Worldspace ?? Cell;
    internal uint ControllerScalarBits => PositionZ;
    internal void Validate(FalloutMainPlayerPendingSource source)
    {
        source.Validate();
        ValidateDeclaration();
        if (!source.Player.Main.HasNewVegasChildren && SourceTailWord is null)
            throw new InvalidDataException("FO3 pending allocation omitted its independently initialized final word.");
        if (SourceContract != source.Contract)
            throw new InvalidDataException("Pending transfer changed its selected source consumer declaration.");
    }
    internal void ValidateDeclaration()
    {
        if (SourceContract is not { Length: 64 } || !SourceContract.All(Uri.IsHexDigit) ||
            string.IsNullOrWhiteSpace(Factory) || Cell is { } cell && !Key(cell) ||
            Worldspace is { } world && !Key(world) || Reference is { } reference && !Key(reference) ||
            Furniture is { } furniture && !Key(furniture) ||
            Callback is { } callback && (callback.Factory == Guid.Empty || callback.SourceContract != SourceContract ||
                string.IsNullOrWhiteSpace(callback.Handler) || !Key(callback.Context)))
            throw new InvalidDataException("Pending transfer lost its exact source factory, raw cells or borrowed source identities.");
    }
    internal void RequireRole(FalloutPlayerPendingKind role)
    {
        ValidateDeclaration();
        if (role == FalloutPlayerPendingKind.Opaque ||
            (role == FalloutPlayerPendingKind.ReferenceTravel) != (Target == FalloutPlayerTransferTarget.Reference) ||
            (role == FalloutPlayerPendingKind.Empty) != (Target == FalloutPlayerTransferTarget.Empty))
            throw new InvalidDataException("Pending logical request differs from its actual raw source target branch.");
    }
    private static bool Key(FalloutFormKey key) => key.ObjectId is > 0 and <= FalloutFormKey.ObjectIdMask && !string.IsNullOrWhiteSpace(key.OwnerPlugin);
}

internal interface IFalloutPlayerTransferCallback
{
    FalloutPlayerTransferCallback Source { get; }
    Guid Process { get; }
    string Owner { get; }
    void Invoke(FalloutMainPlayerCellInvocation invocation, FalloutFormKey context);
}

internal interface IFalloutPlayerOwnedChild
{
    Guid Identity { get; }
    Guid Process { get; }
    string SourceContract { get; }
    string Owner { get; }
    void Acquire();
    void Release();
}
