namespace OpenNV.Runtime.Content;

internal sealed record FalloutSourceCellLoaderDeclaration(string EngineSha256, string ContractSha256)
{
    private const string Executable = "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57";
    private const string Contract = "source-CELL-reference-loader/v1;configured-source-files-in-registered-order;" +
        "file-physical-record-order;eager-interior-persistent-reference-insertion;" +
        "same-ParentCELL-override-keeps-membership;lazy-temporary-group-nine;" +
        "temporary-winning-file-filter;already-constructed-reference-skip;head-insertion-before-ParentCELL;" +
        "canonical-Player-source-factory-clears-constructor-bit-eight;Player-current-native-body-required;" +
        "partial-deleted-relocated-and-exterior-producers-independent;current-cold-source-digest-no-insertion-replay";
    internal uint CanonicalPlayerInitialReferenceFlags => 0;

    internal static FalloutSourceCellLoaderDeclaration Read(string executable)
    {
        if (executable != Executable)
            throw new NotSupportedException("Selected executable initial CELL reference ingestion is unowned.");
        return new(executable, FalloutAdvancementRuntimeReceipt.Hash(Contract));
    }

    internal void Validate()
    {
        if (this != Read(EngineSha256))
            throw new InvalidDataException("Source CELL loader changed its selected physical traversal contract.");
    }
}
