namespace OpenNV.Runtime.Content;

internal sealed partial record FalloutSkyTransferDeclaration
{
    private const string StandaloneRules =
        "source-FO3-Sky-transfer-reset/v1;selected-original-constructor-clock-Float32-ten-mode-four-blend-one-transition-positive-zero;" +
        "weather-climate-current-previous-target-override-and-Clouds-Precipitation-constructor-null;constructor-flags-thirty-two;" +
        "loader-six-global-time-caches-positive-zero;two-base-time-caches-distinct-from-extended-times;" +
        "override-null-before-previous-null-before-current-null-before-combined-flags-old-and-not-eight-or-one;" +
        "climate-writer-publishes-winning-climate-first-and-combines-six-time-cache-dirty-mask-at-the-same-flags-store;" +
        "transition-positive-zero-before-optional-Clouds-before-optional-Precipitation-before-image-manager;" +
        "climate-caller-mask-sixty-four-only-after-return;unchanged-nonforced-climate-skips-reset;" +
        "Clouds-texture-pointer-null-then-optional-property-texture-null-and-Float32-zero;" +
        "Precipitation-second-node-before-first-node-parent-detach-and-refcount-release-before-null;" +
        "four-distinct-ImageSpaceModifierInstanceForm-zero-null-constructors;" +
        "manager-registers-previous-primary-current-primary-previous-secondary-current-secondary;" +
        "source-allocation-order-enables-flag-one-then-byte-one;mode-zero-or-one-stores-four-positive-zero-weights;" +
        "mode-other-calls-four-time-getters-in-source-order;climate-bytes-convert-to-Float32-hours-divide-six;" +
        "dirty-base-getter-with-null-climate-keeps-base-cache-and-dirty-bit;extended-getter-still-updates-and-clears-its-own-bit;" +
        "Float32-subtract-extension-then-source-maximum-zero;Float32-add-extension-then-source-minimum-twenty-three-point-nine-nine;" +
        "four-weather-IMAD-slots-sunrise-day-sunset-night;strict-sunrise-and-sunset-intervals-inclusive-day-boundaries;" +
        "night-includes-start-and-end;remaining-source-branch-selects-day;no-noon-interpolation;" +
        "Float32-after-every-subtract-multiply-add-divide-and-one-minus;" +
        "current-primary-before-current-secondary-before-previous-primary-before-previous-secondary;" +
        "nulled-current-and-previous-weather-reach-anonymous-default-and-zero-previous-weights;" +
        "anonymous-static-two-knots-zero-one-multiply-one-add-zero-effects-zero-tint-white-alpha-zero-fade-zero-not-a-FormID;" +
        "returned-reset-does-not-certify-original-TLS-factory-native-weather-frame-or-final-pixels";

    internal bool IsStandalone => EngineSha256 == FalloutSourceMainFamily.Fallout3;
    internal int WeatherImageSlots => IsStandalone ? 4 : 6;
    internal static string StandaloneContractSha256 => Hash(StandaloneRules);
    internal static string ContractForEngine(string engine) => engine switch
    {
        SelectedEngine => Hash(Rules),
        FalloutSourceMainFamily.Fallout3 => Hash(StandaloneRules),
        _ => throw new NotSupportedException("Selected original Sky constructor/reset declaration is unowned.")
    };

    private static FalloutSkyTransferDeclaration FromEngine(string engine)
    {
        var declaration = new FalloutSkyTransferDeclaration(engine, ContractForEngine(engine));
        declaration.Validate(); return declaration;
    }
}
