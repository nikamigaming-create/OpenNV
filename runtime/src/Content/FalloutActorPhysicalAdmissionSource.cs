namespace OpenNV.Runtime.Content;

internal sealed record FalloutActorPhysicalAdmissionSource(FalloutMainScriptCallerSource Main, string Contract)
{
    private const string Rules = "source-FO3-Actor-physical-admission/v1;" +
        "actual-Actor-parent-initializer-writes-independent-mode-zero-and-physical-state-zero;" +
        "actual-Player-constructs-High-inheriting-MiddleHigh-signed-activity-byte-zero;" +
        "actual-old-Low-common-copy-before-High-publication-before-controller-construction;" +
        "actual-mode-two-caller-short-circuit-before-reference-model-predicate;" +
        "predicate-current-thread-reference-model-cache-or-current-reference-data-model-before-mode;" +
        "predicate-argument-mask-four-hundred-and-current-mode-one-two-six;" +
        "mode-four-ordered-active-effect-class-and-signed-state-less-than-thirty-independent;" +
        "current-process-null-false-otherwise-five-independent-signed-activity-queries-one-three-two-four-five;" +
        "false-selects-model-motion-six-and-skips-penetration-registration-true-selects-motion-one-and-registers;" +
        "registration-Player-view-model-getter-independent-from-predicate-reference-model;" +
        "raw-mode-reuses-retained-Actor-life-owner-physical-state-and-process-activity-are-distinct;" +
        "constructor-fields-do-not-admit-later-life-physical-state-or-activity-writers;" +
        "cold-predicate-history-never-replays-getters-or-promotes-old-model-and-process-identities";

    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Main.Identity + "\0" + Contract);
    internal static FalloutActorPhysicalAdmissionSource Reviewed(FalloutMainScriptCallerSource main)
    {
        main.Validate();
        if (!FalloutSourceMainFamily.IsFallout3(main.EngineSha256))
            throw new NotSupportedException("Actor physical admission source is unreviewed.");
        return new(main, FalloutAdvancementRuntimeReceipt.Hash(Rules));
    }
    internal void Validate()
    {
        if (this != Reviewed(Main))
            throw new InvalidDataException("Actor physical admission changed its selected constructor, fields or predicate.");
    }
}

// This is Actor's separate physical state. Its mode already belongs to the
// retained neutral-life owner; process activity belongs to the current process.
internal sealed record FalloutActorPhysicalAdmissionFields(uint State, long Stores)
{
    internal static FalloutActorPhysicalAdmissionFields Constructed => new(0, 0);
    internal void Validate()
    {
        if (this != Constructed)
            throw new InvalidDataException("Actor physical state has a reached unowned transition writer.");
    }
}
