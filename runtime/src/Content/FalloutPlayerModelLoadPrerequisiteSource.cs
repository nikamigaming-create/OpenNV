namespace OpenNV.Runtime.Content;

internal sealed record FalloutPlayerModelLoadPrerequisiteSource(FalloutPlayerModelLoadContinuationSource Continuation,
    FalloutReferencePositionSource Reference, FalloutIniStringDefault CharacterGenerationQuest, string Contract)
{
    internal const uint InventoryPreparationSuppressionMask = 2;
    internal const byte LoaderInitializedCharacterGenerationByte = 0;
    internal const byte EnteredCharacterGenerationByte = 1;
    internal const int InventorySlotCount = 20;
    private const string Rules = "source-FO3-Player-model-load-prerequisites/v1;" +
        "same-source-reference-change-manager-current-word-bit-one-before-independent-current-process-byte;" +
        "independent-global-byte-PE-loader-zero-filled-writable-virtual-tail-not-Script-InCharGen;" +
        "selected-character-generation-deferred-work-callback-reads-actual-mutable-Main-string-setting;" +
        "hexadecimal-form-lookup-Quest-kind-and-cast-before-actual-independent-byte-one-store;" +
        "byte-store-precedes-current-Player-and-Quest-start-following-children-no-enclosing-helper-return-claim;" +
        "actual-LoadingMenu-destructor-byte-zero-store-and-explicit-entered-unknown-writer-barrier;" +
        "fresh-process-loader-zero-construction-retains-original-history-without-replaying-startup-writes;" +
        "manager-bit-one-or-current-byte-nonzero-skips-inventory-preparation-only;" +
        "otherwise-actual-inventory-materialization-twenty-worn-queries-and-selected-preparation-child-remain-mandatory";
    internal static string ContractDigest => FalloutAdvancementRuntimeReceipt.Hash(Rules);
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Continuation.Identity + "\0" + Reference.Identity + "\0" +
        System.Text.Json.JsonSerializer.Serialize(CharacterGenerationQuest) + "\0" + Contract);
    internal void Validate()
    {
        Continuation.Validate(); Reference.Validate();
        if (Contract != ContractDigest || Reference.Fields.Identity != Continuation.Animation.Tail.Movement.Mover.Controller.Fields.Identity ||
            CharacterGenerationQuest.Declaration is not { Kind: 's', Collection: FalloutIniCollection.Main } ||
            !CharacterGenerationQuest.Declaration.Name.Equals("sCharGenQuest:General", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(CharacterGenerationQuest.Value))
            throw new InvalidDataException("Player model prerequisites changed their actual selected field or startup setting owners.");
    }
}
