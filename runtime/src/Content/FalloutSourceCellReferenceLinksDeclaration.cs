namespace OpenNV.Runtime.Content;

internal sealed record FalloutSourceCellReferenceLinksDeclaration(string EngineSha256, string ContractSha256)
{
    private const string ReviewedExecutable = "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57";
    internal static bool Supports(string executableSha256) => executableSha256 is ReviewedExecutable or FalloutSourceMainFamily.Fallout3;
    private const string Rules = "source-CELL-linked-reference-list/v1;constructor-empty;ordinary-CELL-unlink-old-first;" +
        "new-CELL-head-insert;actual-ParentCELL-setter-after-insertion;independent-transfer-tail;" +
        "persistent-CELL-independent-owner;initial-insertion-caller-order-required;current-cold-no-reinsertion";
    internal static FalloutSourceCellReferenceLinksDeclaration Read(FalloutAdvancementRuntimeReceipt receipt)
    {
        receipt.Validate();
        return ForExecutable(receipt.EngineSha256);
    }
    internal static FalloutSourceCellReferenceLinksDeclaration ForExecutable(string executableSha256)
    {
        if (!Supports(executableSha256))
            throw new NotSupportedException("Selected original CELL linked-reference constructor/insertion writers are unowned.");
        return new(executableSha256, FalloutAdvancementRuntimeReceipt.Hash(SelectedRules(executableSha256)));
    }
    internal void Validate()
    {
        if (!Supports(EngineSha256) ||
            ContractSha256 != FalloutAdvancementRuntimeReceipt.Hash(SelectedRules(EngineSha256)))
            throw new InvalidDataException("CELL linked-reference source declaration changed.");
    }
    private static string SelectedRules(string executable) => executable == FalloutSourceMainFamily.Fallout3 ?
        "source-FO3-CELL-linked-reference-list/v1;actual-empty-head-and-next-constructor;" +
        "ordinary-unlink-previous-before-head-insertion-before-selected-reference-ParentCELL-setter;" +
        "same-CELL-reinsertion-keeps-real-unlink-and-new-head-order;" +
        "persistent-CELL-and-whole-attachment-effects-independent;actual-insertion-stream-cold-no-replay" : Rules;
}
