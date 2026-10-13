namespace OpenNV.Runtime.Content;

internal sealed record FalloutPlayerReferenceHudSource(FalloutMainScriptCallerSource Main, string Contract,
    string Branch, string BracketTemplate, string MeterTemplate, string NameTemplate,
    uint InitialColorBits, byte InitialSuppressed, byte InitialHostile, byte InitialArgument)
{
    // Implementation-neutral declarations from the selected owned Main/Player
    // and HUD constructors. Color is source tile user0, not a presentation tint.
    private const string Rules = "source-FO3-Player-reference-HUD/v1;" +
        "actual-HUD-menu-root-before-even-null-reference;" +
        "null-reference-or-independent-suppression-or-zero-argument-enters-clear;" +
        "ordered-Float32-color-equal-one-hides-bracket-meter-name-in-order;" +
        "different-or-unordered-color-retains-tiles;" +
        "hostile-byte-zero-then-argument-byte-store-on-clear;" +
        "nonnull-reference-actor-dead-hostility-and-injured-value-consumers-independent;" +
        "constructor-EnemyHealth-three-source-template-factories-color-two;" +
        "flags-and-suppression-have-independent-loaded-zero-producers";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Main.Identity + "\0" + Contract);
    internal static FalloutPlayerReferenceHudSource Reviewed(FalloutMainScriptCallerSource main)
    {
        main.Validate();
        if (!FalloutSourceMainFamily.IsFallout3(main.EngineSha256))
            throw new NotSupportedException("Player reference HUD source family is unreviewed.");
        return new(main, FalloutAdvancementRuntimeReceipt.Hash(Rules), "EnemyHealth",
            "template_enemy_health_bracket", "template_meter", "template_justify_center_text",
            0x40000000, 0, 0, 0);
    }
    internal void Validate()
    {
        if (this != Reviewed(Main)) throw new InvalidDataException("Player HUD source changed its actual tile, flag or branch declarations.");
    }
    internal static bool ClearsTiles(uint colorBits) => BitConverter.UInt32BitsToSingle(colorBits) == 1f;
}
