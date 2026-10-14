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
        if (executable is not (Executable or FalloutSourceMainFamily.Fallout3))
            throw new NotSupportedException("Selected executable initial CELL reference ingestion is unowned.");
        return new(executable, FalloutAdvancementRuntimeReceipt.Hash(executable == FalloutSourceMainFamily.Fallout3 ?
            "source-FO3-CELL-reference-loader/v3;registered-plugin-and-physical-record-order;" +
            "eager-interior-persistent-source-reference-construction-same-parent-retains-membership;" +
            "TES4-ONAM-adjusted-FormID-provider-table-later-registration-overwrites;" +
            "lazy-temporary-group-nine-selected-provider-filter-missing-key-permits-file;" +
            "actual-existing-object-skips-construction-before-source-body-effects;" +
            "engine-constructed-STAT-marker-base-with-actual-XPRM-independent-of-absent-ESM-row;" +
            "head-insert-before-current-ParentCELL-publication;real-engine-Player-independent-of-placed-rows;" +
            "current-Player-flags-borrow-existing-reference-owner-not-initial-loader-zero;" +
            "deleted-partial-relocated-visible-distant-and-exterior-effects-independent;" +
            "cold-unchanged-input-digests-and-membership-history-no-insertion-replay" : Contract));
    }

    internal bool HasEngineConstructedStaticBase(FalloutPluginStack records, FalloutFormKey basis) =>
        FalloutDynamicPrimitiveSource.Supports(EngineSha256) &&
        FalloutDynamicPrimitiveSource.Read(EngineSha256).IsEngineConstructedStatic(records, basis);

    internal void Validate()
    {
        if (this != Read(EngineSha256))
            throw new InvalidDataException("Source CELL loader changed its selected physical traversal contract.");
    }
}
