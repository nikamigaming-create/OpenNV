using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

// Object roles are part of the selected declaration. The independent dialog
// byte is not a Pip-Boy flag, and the own-menu object is a Pip-Boy manager.
internal sealed record FalloutStandaloneInterfaceSource(FalloutMainScriptCallerSource Main, string Contract)
{
    private const string Rules = "source-FO3-interface/v1;constructor-enabled-zero-mode-one-context-zero;" +
        "ten-zero-menu-slots;sixty-loader-zero-active-bytes;null-alternate-and-own-object;" +
        "own-object-is-FOPipboyManager;manager-constructor-null-reference-arrays-and-selection-minus-one;" +
        "store-returned-manager-before-enabled-byte-one;" +
        "menu-gate-present-enabled-and-UInt32-mode-not-one;GUI2-UInt32-context-equal-two;" +
        "context-kind-low-byte-of-mode;first-predicate-independent-DialogMenu-lifecycle-byte;" +
        "dialog-constructor-one-close-and-destruction-zero;" +
        "final-predicate-existing-console-and-signed-counter-greater-than-zero;" +
        "foreign-selector-source-active-codes-1003-1002-1035-1023-otherwise-alternate;" +
        "nonnull-selected-object-and-reference-inequality-to-own;" +
        "menu-mode-three-push-four-empty-remove-ordered-slots;" +
        "tile-transitions-source-manager-and-input-manager-children-independent;" +
        "current-native-publication-and-fresh-cold-rebind-not-restored-pointers";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Main.Identity + "\0" + Contract);
    internal static FalloutStandaloneInterfaceSource Read(FalloutMainScriptCallerSource main)
    {
        main.Validate();
        if (!FalloutSourceMainFamily.IsFallout3(main.EngineSha256))
            throw new NotSupportedException("The selected interface class/factory family is not the standalone FO3 declaration.");
        return new(main, FalloutAdvancementRuntimeReceipt.Hash(Rules));
    }
    internal void Validate()
    {
        Main.Validate();
        if (!FalloutSourceMainFamily.IsFallout3(Main.EngineSha256) || Contract != FalloutAdvancementRuntimeReceipt.Hash(Rules))
            throw new InvalidDataException("Standalone interface changed its original class identities or predicate declaration.");
    }
    internal static bool MenuGate(byte enabled, uint mode) => enabled != 0 && mode != 1;
    internal static bool GuiModeTwo(uint context) => context == 2;
    internal static byte ContextKind(uint mode) => unchecked((byte)mode);
    internal static bool ConsoleOpen(sbyte counter) => counter > 0;
    internal static ReadOnlySpan<uint> OwnSelectorCodes => [1003u, 1002u, 1035u, 1023u];
}
