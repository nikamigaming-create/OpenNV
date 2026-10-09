using System.Globalization;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

// Neutral contracts for the selected original utility child. The declaration
// never supplies a platform interface, login result, achievement or callback.
internal sealed record FalloutMainUtilitySource(FalloutMainScriptCallerSource Main, string ContractSha256)
{
    private const string Contract = "source-Main-platform-utilities/v1;lazy-three-getters;achievement-queue-first;" +
        "source-FIFO-inline-first-and-appended-next;duplicate-signed-ids-preserved;" +
        "payload-retired-before-range-platform-and-iterator;id-greater-than-100-diagnostic;" +
        "platform-name-prefix-ASCII65-signed-decimal-minimum-width-two-zero-padding;" +
        "four-byte-destination-reserves-NUL-explicit-byte3-termination;overflow-enters-unowned-invalid-parameter-handler;" +
        "nonnull-statistics-test-then-format-before-fresh-interface-query;" +
        "SetAchievement-and-StoreStats-return-booleans-ignored-by-source-but-retained-as-observations;" +
        "StoreStats-before-entire-queue-clear;empty-queue-no-platform-query;" +
        "login-constructor-false-null-callback;IsSteamRunning-then-nonnull-user-then-fresh-user-BLoggedOn;" +
        "cache-commit-before-changed-callback-context-zero;register-callback-before-immediate-current-value;" +
        "third-utility-frame-source-noop;original-AddAchievement-suppression-byte-before-lazy-queue-getter;" +
        "no-source-native-interface-or-suppression-default;failed-prefix-not-replayed";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Main.Identity + "\0" + ContractSha256);
    internal static FalloutMainUtilitySource Read(FalloutMainScriptCallerSource main)
    { main.Validate(); return new(main, FalloutAdvancementRuntimeReceipt.Hash(Contract)); }
    internal void Validate()
    {
        if (Main is null || ContractSha256 != FalloutAdvancementRuntimeReceipt.Hash(Contract))
            throw new InvalidDataException("Main utility declaration changed its genuine selected source contract.");
        Main.Validate();
    }
    internal static string AchievementIdentifier(int id)
    {
        var number = id.ToString(CultureInfo.InvariantCulture);
        // The original printf width includes a minus sign. D2 would add a
        // third character to -1 and silently change the actual API argument.
        var identifier = ((char)65).ToString() + (number.Length < 2 ? number.PadLeft(2, '0') : number);
        // The selected source formatter reserves its fourth byte for NUL.
        // Overflow clears the destination and invokes a separate original
        // invalid-parameter handler. Its outcome is not truncation, a wider
        // identifier or an admitted empty achievement API argument.
        if (identifier.Length >= 4)
            throw new NotSupportedException("source-four-byte-achievement-invalid-parameter-handler-unbound");
        return identifier;
    }
}
