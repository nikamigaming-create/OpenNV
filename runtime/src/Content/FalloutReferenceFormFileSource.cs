namespace OpenNV.Runtime.Content;

internal sealed record FalloutReferenceFormFileSource(FalloutReferencePositionSource Reference, string Contract)
{
    private const string Rules = "source-FO3-reference-form-file-association/v1;" +
        "actual-form-constructor-two-null-list-cells-before-current-DataHandler-file-query;" +
        "fresh-source-file-runtime-flags-zero-selected-four-header-only-one-sixteen-eighty-clone-one;" +
        "active-file-assignment-requires-runtime-eight-fresh-pre-Player-prefix-has-no-eight-true-writer;" +
        "null-active-file-appends-nothing-ID-and-Base-writers-preserve-the-same-form-file-list;" +
        "absent-winning-engine-reference-row-preserves-list-through-record-ingress;" +
        "current-file-minus-one-walks-list-last-nonnull-empty-list-returns-null-before-persistent-flag;" +
        "copy-load-native-association-or-file-selection-writer-invalidates-current-field;" +
        "whole-DataHandler-record-loader-and-reference-constructor-return-remain-independent";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Reference.Identity + "\0" + Contract);
    internal static FalloutReferenceFormFileSource Reviewed(FalloutReferencePositionSource reference)
    { reference.Validate(); return new(reference, FalloutAdvancementRuntimeReceipt.Hash(Rules)); }
    internal void Validate()
    { if (this != Reviewed(Reference)) throw new InvalidDataException("Reference form-file source contract changed."); }
}
