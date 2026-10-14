namespace OpenNV.Runtime.Content;

internal sealed record FalloutOriginalInputSource(FalloutPlayerMovementControlSource Controls, string Contract)
{
    internal FalloutMainScriptCallerSource Main => Controls.Main;
    private const string Rules = "source-FO3-original-input-query/v1;" +
        "actual-selected-input-manager-factory-and-pure-current-singleton;" +
        "constructor-zero-complete-current-previous-keyboard-and-mouse-buffers-and-menu-word;" +
        "mouse-swap-previous-return-store-before-host-restore-and-double-click-time;" +
        "actual-current-controller-status-and-independent-manager-suppression-byte;" +
        "current-to-previous-before-per-device-acquire;acquire-zero-or-one-read-otherwise-clear-current;" +
        "owned-destination-state-bytes-retained-even-ignored-read-HRESULT;" +
        "normal-actions-keyboard-before-mouse-FF-skip-and-Int32-sum-count-two;" +
        "bit-seven-held-rising-falling-either-and-wheel-before-mode-or-swap;" +
        "source-menu-mask-eight-clear-and-conditional-restore-after-positive-count;" +
        "hook-raw-game-inserted-caches-separate-from-engine-current-and-previous;" +
        "hook-user-disable-before-script-hold-tap-and-success-only-tap-consumption;" +
        "wheel-hook-cache-never-written-back-to-signed-engine-wheel;" +
        "unowned-current-writers-selection-gamepad-mode-four-adjacent-fields-fail-at-reached-read;" +
        "fresh-cold-current-provider-and-immutable-historical-receipts-no-device-or-query-replay";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Main.Identity + "\0" + Contract);
    internal static FalloutOriginalInputSource Reviewed(FalloutPlayerMovementControlSource controls)
    {
        controls.Validate();
        if (!FalloutSourceMainFamily.IsFallout3(controls.Main.EngineSha256))
            throw new NotSupportedException("Original control-query source is unreviewed.");
        return new(controls, FalloutAdvancementRuntimeReceipt.Hash(Rules));
    }
    internal void Validate()
    {
        if (this != Reviewed(Controls)) throw new InvalidDataException("Original input source declaration changed its selected constructors or queries.");
    }
}
