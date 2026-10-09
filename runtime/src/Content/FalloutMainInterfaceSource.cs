using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

// These are engine menu/class declarations, not selected content outcomes.
// The source factory constructs its own Pip-Boy manager before enabling the
// interface. The independent dialog byte has a different constructor/close.
internal sealed record FalloutMainInterfaceSource(FalloutMainScriptCallerSource Main, string Contract)
{
    private const string Engine = "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57";
    private const string Rules = "source-interface-Main/v1;enabled-zero-UInt32-mode-one-context-zero;" +
        "sixty-loader-zero-active-menu-bytes;own-and-alternate-null;ten-zero-stack-slots;" +
        "actual-FOPipboyManager-returned-before-enabled-one;" +
        "menu-gate-enabled-and-mode-not-one;GUI-mode-two-independent-context-equal-two;" +
        "DialogMenu-loader-zero-constructor-one-close-and-destruction-zero;" +
        "existing-console-loader-null-signed-byte-greater-than-zero;" +
        "ordered-selector-1003-1002-1035-1061-1023-otherwise-alternate;" +
        "nonnull-active-pointer-inequality-to-current-own-manager;" +
        "context-kind-signed-low-byte;native-resource-lifetime-and-current-cold-rebind";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Main.Identity + "\0" + Contract);
    internal static FalloutMainInterfaceSource Read(FalloutMainScriptCallerSource main)
    {
        main.Validate();
        if (main.EngineSha256 != Engine) throw new NotSupportedException("Selected Main interface class declaration is unowned.");
        return new(main, FalloutAdvancementRuntimeReceipt.Hash(Rules));
    }
    internal void Validate()
    {
        if (this != Read(Main)) throw new InvalidDataException("Main interface changed its selected independent predicates.");
    }
    internal static ReadOnlySpan<uint> OwnSelectorCodes => [1003u, 1002u, 1035u, 1061u, 1023u];
    internal static int Kind(uint mode) => unchecked((sbyte)mode);
}
