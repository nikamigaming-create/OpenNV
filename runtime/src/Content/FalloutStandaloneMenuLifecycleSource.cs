using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

// Common Menu fields and Interface stack operations retain their actual
// caller. Native visibility and the scene-tree pause flag supply no fields.
internal sealed record FalloutStandaloneMenuLifecycleSource(FalloutStandaloneInterfaceSource Interface, string Contract)
{
    internal const uint StartMenu = 1013;
    internal const uint HudMenu = 1001;
    internal const uint FirstMenu = 1001;
    internal const uint LastMenu = 1060;
    internal const int StackExtent = 10;
    internal const uint QuietMode = 1;
    internal const uint MenuMode = 3;
    internal const uint ReturningMode = 4;
    internal const uint ConstructedLifecycle = 4;
    internal const uint LeavingLifecycle = 2;
    internal const uint EnteringLifecycle = 8;
    internal const uint LiveLifecycle = 1;
    internal const ushort ConstructorFlags = 0x100;
    internal const string StartMenuPath = "menus/options/start_menu.xml";
    private const string Rules = "source-FO3-common-menu-lifecycle/v1;" +
        "real-StartMenu-class-getter1013-and-inherited-Menu-null-fields-lifecycle4-flags256;" +
        "winning-source-XML-root-before-stacking102-103-6006-6007;identity-before-stack-push;" +
        "ten-current-Interface-slots-first-empty-mode3-on-first;special-code3-front-shift-through-slot8;" +
        "independent-shared-image-space-slot15-menu-byte-after-stack-mode-stores;" +
        "source-visible-trait-nonzero-active-byte-within1001-through1060;" +
        "normal-removal-admission-before-ordered-copy-force-removal-distinct;" +
        "source-adjacent-word-read-required-only-on-reached-full-stack-tail;" +
        "empty-removal-mode4-before-image-menu-byte;context-preserved-by-common-menu-path;" +
        "duration4033-nonzero-otherwise4034-before-queue-remove-first-append-real-kind13;opening8-completes1-closing2-completes4;" +
        "elapsed-current-Main-delta-source-Float32-order-and-closed-menu-prefix-before-mode1;" +
        "actual-source-input-manager-menu-word-mask8-distinct-from-image-menu-byte;held-Common-direct-context-before-Player-first-pointer-either-null-return;" +
        "current-native-source-root-lifetime-and-fresh-cold-constructor-history-without-callback-replay";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Interface.Identity + "\0" + Contract);
    internal static FalloutStandaloneMenuLifecycleSource Reviewed(FalloutStandaloneInterfaceSource source)
    {
        source.Validate(); return new(source, FalloutAdvancementRuntimeReceipt.Hash(Rules));
    }
    internal void Validate()
    {
        if (this != Reviewed(Interface))
            throw new InvalidDataException("Menu lifecycle changed its selected source class or ordered field writers.");
    }
    internal static bool Visible(uint bits) => BitConverter.UInt32BitsToSingle(bits) != 0;
    internal static uint Duration(uint first, uint second) =>
        FalloutStandaloneMenuTransitions.Duration(BitConverter.UInt32BitsToSingle(first) != 0 ? first : second);
}
