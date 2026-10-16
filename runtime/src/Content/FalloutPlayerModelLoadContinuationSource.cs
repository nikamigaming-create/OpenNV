using System.Text.Json.Serialization;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutPlayerModelLoadContinuationSource(FalloutLoadedAnimationSource Animation,
    string Contract, string CameraNode, string SkinAttachment, string BipedPrefix)
{
    [JsonInclude] internal IReadOnlyList<string> FirstNodeNames { get; init; } = [];
    internal const uint WeaponThreadKind = 50;
    internal const uint WornEntryThreadKind = 54;
    internal const uint ActiveRootFlag = 1;
    internal const uint FirstRootFlag = 0x200000;
    private const string Rules = "source-FO3-Player-model-load-continuation/v1;" +
        "independent-Player-first-model-constructor-null-before-real-field-release-store-retain;" +
        "first-root-flag-before-three-source-named-globals-before-time-zero-root-update-and-current-Camera1st-world-height;" +
        "embedded-clamped-active-transform-controllers-retain-initial-zero-clock-before-null-or-original-interpolator-sample-and-world-bounds;" +
        "selected-original-identity-matrix-copy-before-real-first-resource-and-AnimData-constructors;" +
        "same-weapon-getter-before-kind-fifty-setter-requery-equality-skips-only-replacement;" +
        "zero-argument-tail-clears-same-common-two-words-and-actual-player-global-before-signed-minus-one-setter;" +
        "source-root-active-flag-before-named-camera-node-global-store-before-base-and-race-magic-reset;" +
        "five-actual-worn-slot-entries-enchantment-cast-and-source-temporary-release-before-reserved-spell-cast;" +
        "same-scene-update-mask-clear-before-first-root-palette-first-SkinAttachment-lookup;" +
        "fallback-excludes-BIP-prefixed-children-before-current-bound-and-shared-shader-kind-seven-through-eleven-selection;" +
        "same-post-action-field-real-shared-property-release-store-retain;" +
        "source-controller-and-world-model-attachment-before-interface-Pipboy-rebuild-before-toddler-animation-reload;" +
        "failed-prefix-retains-stores-no-enclosing-load-return-until-all-reached-children-return";
    internal static string ContractDigest => FalloutAdvancementRuntimeReceipt.Hash(Rules);
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Animation.Identity + "\0" + Contract + "\0" + CameraNode + "\0" + SkinAttachment + "\0" + BipedPrefix + "\0" + string.Join("\0", FirstNodeNames));
    internal void Validate()
    {
        Animation.Validate();
        if (Contract != ContractDigest || string.IsNullOrWhiteSpace(CameraNode) || string.IsNullOrWhiteSpace(SkinAttachment) || BipedPrefix.Length != 3 ||
            FirstNodeNames is not { Count: 3 } || FirstNodeNames.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Player model continuation lost its actual selected source fields or names.");
    }
}
