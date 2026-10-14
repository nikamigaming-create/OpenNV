namespace OpenNV.Runtime.Content;

internal sealed record FalloutPackageAssignmentSource(FalloutPackageCommandFieldsSource Fields, string Contract)
{
    private const string Rules = "source-FO3-AddScriptPackage-ordered-command/v2;" +
        "selected-package-Primary-first-predicate-Primary-reread-else-Current-special-kind-28;" +
        "selected-Player-special-void-return-initial-Actor-package-process-null-direct-true-return;" +
        "direct-Current-optional-change-event-800-change-callback-before-request-byte-20-clear;" +
        "start-event-200-before-start-callback-procedure-minus-one-before-saved-reference-Extra;" +
        "signed-kind-pure-procedure-stores-through-owned-jump-table-dependent-and-error-arms-independent;" +
        "saved-reference-change-before-runtime-package-flag-4000-before-Actor-Current-false-false-assignment;" +
        "fresh-tail-process-null-or-tier-zero-joins-Player-Fourth-one-nonzero-clock-tier-one-children;" +
        "POBA-result-IDLE-topic-independent-nonnull-IDLE-return-before-supplied-start-byte-one;" +
        "null-IDLE-distinct-byte-one-before-Actor-animation-rereads-and-independent-helper;" +
        "nonnull-Actor-cleanup-current-process-child-first-2000-clear-fresh-child-second-1000-clear;" +
        "selected-byte-getters-stores-source-association-only-current-writer-admission-independent;" +
        "failed-child-committed-prefix-retained-cold-lineage-no-command-replay";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Fields.Identity + "\0" + Contract);
    internal static FalloutPackageAssignmentSource Reviewed(FalloutPackageCommandFieldsSource fields)
    {
        fields.Validate(); return new(fields, FalloutAdvancementRuntimeReceipt.Hash(Rules));
    }
    internal void Validate()
    {
        Fields.Validate();
        if (this != Reviewed(Fields)) throw new InvalidDataException("Package assignment lost its selected original command graph.");
    }

    // These source cases contain a single procedure store before the common
    // return predicate. Target-dependent and error-report cases are separate
    // callable paths and cannot use a default procedure.
    internal static int? PureProcedure(sbyte kind) => kind switch
    {
        3 => 5,
        4 => 4,
        5 => 1,
        6 => 0,
        7 => 27,
        8 => 28,
        9 => 30,
        10 => 32,
        12 => 37,
        13 => 38,
        14 => 41,
        15 => 10,
        16 => 44,
        18 => 12,
        20 => 13,
        21 => 11,
        22 => 19,
        23 => 20,
        24 => 15,
        26 => 21,
        27 => 25,
        28 => 10,
        29 => 31,
        30 => 33,
        31 => 36,
        32 => 39,
        33 => 40,
        34 => 42,
        35 => 43,
        36 => 46,
        _ => null
    };
}
