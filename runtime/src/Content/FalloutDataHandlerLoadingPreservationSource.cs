namespace OpenNV.Runtime.Content;

internal sealed record FalloutDataHandlerLoadingPreservationSource(FalloutActorPackageAssignmentSource Assignment, string Contract)
{
    private const string Rules = "source-FO3-DataHandler-cleanup-loading-preservation/v1;" +
        "actual-constructor-word-zero-and-shared-cleanup-byte;" +
        "selected-record-load-copy-finalization-preserve-this-shared-byte;" +
        "independent-record-busy-and-materialization-fields-not-cleanup-loading;" +
        "source-record-receivers-distinct-from-shared-DataHandler-and-Player-same-offset-fields;" +
        "shared-byte-writers-TES-teardown-DataHandler-clear-and-CloseFile-command;" +
        "actual-returned-CSharp-record-input-current-association-no-original-loader-execution;" +
        "no-synthetic-zero-store-preserve-before-value-and-prior-availability;" +
        "unknown-copy-native-Script-and-singleton-writers-invalidate-before-current-read;" +
        "cold-original-input-preservation-writer-history-fresh-current-bindings-without-replay";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Assignment.Identity + "\0" + Contract);
    internal static FalloutDataHandlerLoadingPreservationSource Reviewed(FalloutActorPackageAssignmentSource assignment)
        => new(assignment, FalloutAdvancementRuntimeReceipt.Hash(Rules));
    internal void Validate(FalloutActorPackageAssignmentSource assignment)
    {
        Assignment.Validate(); assignment.Validate();
        if (Assignment != assignment || this != Reviewed(assignment))
            throw new InvalidDataException("DataHandler records preservation changed its selected loading field or source footprint.");
    }
}
