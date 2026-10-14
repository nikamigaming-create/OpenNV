namespace OpenNV.Runtime.Content;

internal sealed record FalloutReferenceScriptExtraInitializerSource(FalloutPackageCommandFieldsSource Fields, string Contract)
{
    internal const uint InitializedFlag = 8, ScriptSuppressionFlag = 0x20;
    internal const uint ActorConstructionFlag = 0x200000, PlayerPersistenceFlag = 0x400;
    internal const uint ConstructorFlags = ActorConstructionFlag | PlayerPersistenceFlag;
    internal const uint ScriptThreadKind = 0x15;
    private const string Rules = "source-FO3-independent-reference-Script-Extra-initialization/v1;" +
        "actual-Player-reference-Extra-list-constructor-empty-template-null;" +
        "Form-initialized-eight-Actor-construction-flag-Player-clears-eight-then-persistence-flag;" +
        "selected-early-physical-controller-preserves-Script-Extra-template-and-reference-predicates;" +
        "constructor-other-Extra-and-inherited-Extra-initialization-do-not-create-Script-Extra;" +
        "contained-item-Script-Extra-lists-are-distinct-potential-Script-callbacks-require-writers;" +
        "winning-engine-reference-payload-requires-independent-record-initialization-writer;" +
        "winning-source-BaseForm-template-and-SCRI-selection-separate-from-renderer;" +
        "inherited-initialized-store-is-independent-from-whole-initializer-return;" +
        "current-reference-Script-predicate-before-current-thread-kind-hex-fifteen;" +
        "existing-Extra-Script-nonnull-bypasses-attachment-otherwise-template-then-BaseForm;" +
        "null-Script-component-or-Script-allocates-nothing;" +
        "nonnull-Script-attaches-Extra-before-independent-event-list-local-variable-factory;" +
        "failed-factory-retains-attached-Extra-and-entered-thread-kind;" +
        "returned-Script-branch-restores-prior-kind-and-admits-only-Script-Extra;" +
        "actual-unowned-flags-template-copy-load-and-callback-writers-invalidate-before-read;" +
        "cold-retains-original-failed-prefix-and-fresh-parent-lineage-without-initializer-replay;" +
        "whole-reference-initializer-physical-controller-and-other-field-lanes-remain-independent";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Fields.Identity + "\0" + Contract);
    internal static FalloutReferenceScriptExtraInitializerSource Reviewed(FalloutPackageCommandFieldsSource fields)
    { fields.Validate(); return new(fields, FalloutAdvancementRuntimeReceipt.Hash(Rules)); }
    internal void Validate()
    {
        Fields.Validate();
        if (this != Reviewed(Fields)) throw new InvalidDataException("Reference Script initializer changed its selected source contract.");
    }
}
