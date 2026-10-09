using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal FalloutMainPlayerPendingState CampaignPlayerPendingConsumers => ProcessRuntime.MainPlayerPendingConsumers;
    internal FalloutPlayerTransferPayload ReadMainPlayerPendingPayload(FalloutPlayerPendingRequest request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); PlayerMoves.SourcePending.Require(request);
        var payload = CampaignPlayerPendingConsumers.Payload(request);
        RequireMainPlayerPayloadSources(payload);
        return payload;
    }
    private void RequireMainPlayerPayloadSources(FalloutPlayerTransferPayload payload)
    {
        RequireTarget(payload.Cell, "CELL"); RequireTarget(payload.Worldspace, "WRLD");
        if (payload.Reference is { } target && target != _enginePlayer)
        {
            var record = records.GetEffective(target);
            if (record.IsDeleted || record.Signature is not ("REFR" or "ACHR" or "ACRE" or "PGRE" or "PMIS"))
                throw new InvalidDataException("Pending target lost its exact effective source signature/master/winner.");
        }
        if (payload.Furniture is { } furniture && furniture != _enginePlayer)
        {
            var reference = records.GetEffective(furniture);
            if (reference.IsDeleted || reference.Signature is not ("REFR" or "ACHR" or "ACRE" or "PGRE" or "PMIS"))
                throw new InvalidDataException("Pending furniture lost its actual winning reference.");
        }
        void RequireTarget(FalloutFormKey? key, string signature)
        {
            if (key is not { } form) return;
            var record = records.GetEffective(form);
            if (record.IsDeleted || record.Signature != signature)
                throw new InvalidDataException("Pending raw " + signature + " pointer changed winning source ownership.");
        }
    }
    private void RequirePendingColdPayloadSources(FalloutMainPlayerCellSnapshot saved)
    {
        if (saved.Pending.Pending?.SourcePayload is not { } payload) return;
        payload.Validate(FalloutMainPlayerPendingSource.Read(saved.Source)); RequireMainPlayerPayloadSources(payload);
        if (saved.Pending.Pending!.Kind is FalloutPlayerPendingKind.MoveTo or FalloutPlayerPendingKind.Door)
            RequireSourcePlayerRawTransfer(payload);
    }
    internal FalloutExteriorCellLoaderTaskSource ReadOrCreateSourceExteriorLoader(FalloutMainPlayerCellInvocation invocation,
        FalloutCellProcessIdentity cell, int x, int y, Func<uint, IFalloutExteriorCellLoaderTask> factory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var actual = ReadMainPlayerSourceCell(cell.Cell);
        if (actual.Source != cell || actual.X != x || actual.Y != y || (actual.CellFlags & 1) != 0)
            throw new InvalidDataException("Exterior loader task changed its exact winning CELL/world source.");
        return CampaignPlayerPendingConsumers.ExteriorLoaders.ReadOrCreate(invocation, cell, x, y, factory);
    }
    internal FalloutExteriorCellLoaderTaskSource ConstructSourceExteriorLoader(FalloutMainPlayerCellInvocation invocation,
        FalloutCellProcessIdentity cell, int x, int y, IFalloutExteriorCellLoaderTaskConsumers? consumers) =>
        ReadOrCreateSourceExteriorLoader(invocation, cell, x, y, key => new FalloutExteriorCellLoaderTask(
            FalloutMainPlayerPendingSource.Read(CampaignMainPlayerCellSource), invocation, key, cell, x, y, consumers));
}
