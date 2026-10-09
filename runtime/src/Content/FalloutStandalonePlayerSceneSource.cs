using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutStandalonePlayerSceneSource(FalloutMainScriptCallerSource Main, string Contract)
{
    private const string Rules = "source-FO3-Main-Player-scene/v1;" +
        "Main-factory-byte-scene-zero-and-independent-hold-zero;" +
        "Player-constructor-view-zero-first-person-null;" +
        "selected-first-person-factory-return-old-owner-release-before-store-before-acquire;" +
        "getter-view-zero-and-nonnull-first-person-returns-directly;" +
        "other-arms-original-TLS-initializer-actor-match-scene-otherwise-loaded-data-scene;" +
        "free-camera-scene-inversion-before-hold-enter-and-argument-nonzero;" +
        "actual-enabled-input-manager-child-byte-before-enter-only-position-height-angles;" +
        "scene-clock-loader-numerator-one-denominator-one-manager-kind-zero;" +
        "byte-increment-before-clock-query-wide-ratio-times-scalar-times-delivered-Float32;" +
        "Float32-store-before-actual-Player-virtual-return-before-byte-decrement;" +
        "unit-kind-not-four-arm-exact-identity-conversion-only;" +
        "nonunit-x87-and-kind-four-child-not-substituted;" +
        "two-loader-zero-view-request-bytes-independent-from-POV;" +
        "Player-constructor-countdown-disabled-five-and-null-scene-reference;" +
        "virtual-releases-shared-owned-child-before-countdown;" +
        "reference-scratch-zero-store-before-Player-query-before-null-reference-test;" +
        "unknown-POV-and-full-virtual-children-retain-entered-prefix;" +
        "current-native-factory-and-source-resource-identity-fresh-cold-rebind";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Main.Identity + "\0" + Contract);
    internal static FalloutStandalonePlayerSceneSource Read(FalloutMainScriptCallerSource main)
    {
        main.Validate();
        if (!FalloutSourceMainFamily.IsFallout3(main.EngineSha256))
            throw new NotSupportedException("The original standalone Player scene declaration is not this source family.");
        return new(main, FalloutAdvancementRuntimeReceipt.Hash(Rules));
    }
    internal void Validate()
    {
        Main.Validate();
        if (!FalloutSourceMainFamily.IsFallout3(Main.EngineSha256) || Contract != FalloutAdvancementRuntimeReceipt.Hash(Rules))
            throw new InvalidDataException("Standalone Player scene changed its original constructor, selector or transition declaration.");
    }
    internal const uint One = 0x3f800000;
    internal static (byte Scene, byte Hold) Toggle(byte before, byte argument) =>
        ((byte)(before == 0 ? 1 : 0), (byte)(before == 0 && argument != 0 ? 1 : 0));
}
