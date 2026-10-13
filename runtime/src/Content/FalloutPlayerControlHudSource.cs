namespace OpenNV.Runtime.Content;

internal sealed record FalloutPlayerControlHudSource(FalloutPlayerMovementControlSource Controls, string Contract)
{
    internal const string Menu = "HUDMainMenu";
    internal const uint MenuId = 1004;
    internal const string CrosshairSetting = "bCrossHair:GamePlay";
    internal const string WidthSetting = "iSize W:Display", HeightSetting = "iSize H:Display";
    internal const string SafeXSetting = "iSafeZoneX:Interface", SafeYSetting = "iSafeZoneY:Interface";
    internal const string WideXSetting = "iSafeZoneXWide:Interface", WideYSetting = "iSafeZoneYWide:Interface";
    internal const int InitialWidth = 640, InitialHeight = 480;
    internal const float LogicalWidth = 1280, LogicalHeight = 960;
    internal const int MeterInsetX = 390, MeterInsetY = 280, SafeZoneScale = 2;
    internal static readonly IReadOnlyList<string> VisibilityTiles = Array.AsReadOnly(new[]
    {
        "ActionPoints", "HitPoints", "RadiationMeter", "EnemyHealth", "QuestReminder", "Region_Location",
        "ReticleCenter", "SneakMeter", "Messages", "Info", "Subtitles", "Hokeys", "XPMeter", "BreathMeter",
        "Explosive_positioning_rect", "crippled_limb_indicator"
    });
    private const string Rules = "source-FO3-Player-control-HUD/v2;actual-HUDMainMenu-not-DirectInput;" +
        "actual-returned-XML-menu-identity-before-global-publication-before-source-tile-bindings;" +
        "inherited-Menu-constructor-mode-four-independent-HUD-current-mode-four;" +
        "display-loaded-640-480-before-renderer-collection-integer-width-height-stores;" +
        "selected-mode-dimensions-independent-before-normalized-Float32-input-x87-logical-1280-960;" +
        "actual-Main-integer-safe-zones-independent-cached-wide-byte-before-doubled-insets;" +
        "XP-x-y-user7-stores-before-HUD-initialization-request-five;" +
        "allow-mask-FFBF-plus-winning-typed-Prefs-crosshair-byte-bit-six;message-handle-minus-one;" +
        "present-HUD-mode-not-one-enters-menu-transition-before-independent-request-selection;" +
        "transition-duration-fadein-ordered-zero-selects-fadeout-null-root-quarter;" +
        "current-thread-kind-thirteen-replace-first-menu-transition-append-fresh-zero-elapsed;" +
        "returning-allocation-restores-prior-kind-before-menu-mode-eight-before-advance-all-current-transitions;" +
        "advance-source-current-scaled-Float32-delta-control-mode-four-divisor-only;" +
        "requested-one-current-menu-and-Player-status-short-circuits-remain-owned-separately;" +
        "actual-Player-movement-disable-byte-before-independent-InterfaceManager-HUD-override-byte;" +
        "ordinary-disabled-movement-request-forces-twelve-mask-1514-before-retained-allowed-mask;" +
        "global-HUD-message-mask-before-XPMeter-user7-Y-reset-before-subtitle-transition-before-current-mode-store;" +
        "sixteen-ordered-visible-trait-stores-use-Float32-mask-and-single-bit-not-Boolean-one;" +
        "message-handle-minus-one-pure-return-before-independent-active-sound-pause-resume;" +
        "no-already-current-mode-bypass-no-cold-effect-replay";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Controls.Identity + "\0" + Contract);
    internal static FalloutPlayerControlHudSource Reviewed(FalloutPlayerMovementControlSource source)
    { source.Validate(); return new(source, FalloutAdvancementRuntimeReceipt.Hash(Rules)); }
    internal void Validate()
    { if (this != Reviewed(Controls)) throw new InvalidDataException("HUD controls lost their actual source menu/field declaration."); }
}
