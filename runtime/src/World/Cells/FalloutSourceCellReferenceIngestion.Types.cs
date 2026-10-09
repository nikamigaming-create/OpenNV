using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutSourceCellIngestionDisposition
{
    InsertPersistent, RetainPersistentMembership, InsertWinningTemporary,
    SkipEarlierTemporaryProvider, SkipAlreadyConstructed, Refused
}

internal sealed record FalloutSourceCellIngestionInput(string Plugin, string PluginSha256, int LoadOrder,
    long HeaderOffset, FalloutFormKey Reference, FalloutFormKey Cell, string Signature, uint Flags,
    int ChildGroup, string BodySha256, FalloutSourceCellIngestionDisposition Disposition, string? Refusal);

internal sealed record FalloutSourceCellIngestionEvidence(FalloutSourceCellLoaderDeclaration Source,
    string Stack, FalloutCellProcessIdentity Cell, string GraphSha256, string InputsSha256,
    IReadOnlyList<FalloutSourceCellIngestionInput> Inputs);

internal interface IFalloutSourceOwnedCellReferenceIngestion : IFalloutSourceCellReferenceIngestion
{
    FalloutSourceCellIngestionEvidence ReadEvidence(FalloutCellProcessData cell);
    void RequireEvidence(FalloutSourceCellIngestionEvidence evidence, FalloutCellProcessData cell);
}
