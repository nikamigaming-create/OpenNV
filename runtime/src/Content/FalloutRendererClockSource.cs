namespace OpenNV.Runtime.Content;

internal enum FalloutRendererClockMode { Fixed, Adaptive, Raw, Smoothed }

// Independent of the calendar, cached OS timer and animation delta. These
// values describe the selected source publisher and its actual loaded cells.
internal sealed record FalloutRendererClockSource(FalloutMainScriptCallerSource Main, string Contract,
    float MaximumAccumulatedSeconds, float DeferredSeconds, float FixedStepFraction,
    float MillisecondsPerSecond, float HourFactor, uint LoadedInputBits, uint LoadedAccumulatedBits,
    uint LoadedPreviousBits, uint LoadedRemainderBits, uint LoadedDeltaBits, uint LoadedSteps,
    uint LoadedMainMode, byte LoadedForce)
{
    private const string Rules = "source-FO3-renderer-clock/v1;raw-input-before-remainder-add;Float32-at-each-operation;" +
        "upper-clamp-before-nonpositive-return;independent-scene-complexity-one-otherwise-three-substeps;" +
        "source-mode-four-or-independent-force-byte;INI-copied-update-type-and-chase-multiplier;" +
        "fixed-zero-adaptive-one-raw-two-smoothed-ten-others-adaptive;" +
        "fixed-quantum-times-actual-timer-rate;cvttss2si-then-unsigned-cap;consumed-and-remainder-separate;" +
        "half-quantum-deferral-and-source-force;raw-uses-input-and-preserves-remainder;" +
        "adaptive-threshold-deferral-otherwise-equal-substeps;" +
        "indexed-installed-callback-hour-paused-scaled-paused-unsigned-milliseconds-unconditional-scaled;" +
        "interface-enabled-mode-not-one-independent-of-scene-complexity;" +
        "current-and-cold-authoritative-cells-without-source-writer-replay";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Main.Identity + "\0" + Contract);
    internal static FalloutRendererClockSource Reviewed(FalloutMainScriptCallerSource main)
    {
        main.Validate();
        if (!FalloutSourceMainFamily.IsFallout3(main.EngineSha256))
            throw new NotSupportedException("Selected renderer clock publisher has no reduced source owner.");
        return new(main, FalloutAdvancementRuntimeReceipt.Hash(Rules), 166.66667f, .008333334f, .5f,
            1000f, 60f, BitConverter.SingleToUInt32Bits(.016f), BitConverter.SingleToUInt32Bits(.016f),
            BitConverter.SingleToUInt32Bits(.032f), 0, 0, 0, 0, 0);
    }
    internal void Validate()
    {
        if (this != Reviewed(Main))
            throw new InvalidDataException("Renderer clock changed its selected source arithmetic or loaded cells.");
    }
    internal static FalloutRendererClockMode Mode(uint updateType) => updateType switch
    {
        0 => FalloutRendererClockMode.Fixed,
        2 => FalloutRendererClockMode.Raw,
        10 => FalloutRendererClockMode.Smoothed,
        _ => FalloutRendererClockMode.Adaptive,
    };
}

internal sealed record FalloutRendererClockConfiguration(FalloutRendererClockSource Source, string IniSource,
    FalloutIniValue UpdateType, FalloutIniValue ChaseMultiplier)
{
    internal uint CopiedUpdateType => unchecked((uint)(int)UpdateType.Number!.Value);
    internal float CopiedChaseMultiplier => (float)ChaseMultiplier.Number!.Value;
    internal static FalloutRendererClockConfiguration Read(FalloutPluginStack records, FalloutMainScriptCallerSource main)
    {
        var owned = records.OwnedSource ?? throw new InvalidDataException("Renderer clock has no selected owned source.");
        var value = new FalloutRendererClockConfiguration(
            FalloutExecutableStringTable.ReadRendererClock(owned.FalloutExecutablePath, main), records.IniSettings.Source,
            records.IniSettings.Find(FalloutIniCollection.Main, "iUpdateType:HAVOK") ??
                throw new NotSupportedException("Renderer clock omitted its registered update-type INI owner."),
            records.IniSettings.Find(FalloutIniCollection.Main, "fChaseDeltaMult:HAVOK") ??
                throw new NotSupportedException("Renderer clock omitted its registered chase multiplier INI owner."));
        value.Validate(); return value;
    }
    internal void Validate()
    {
        Source.Validate();
        if (string.IsNullOrWhiteSpace(IniSource) || UpdateType is null || ChaseMultiplier is null ||
            UpdateType.Declaration.Name != "iUpdateType:HAVOK" || UpdateType.Declaration.Collection != FalloutIniCollection.Main ||
            ChaseMultiplier.Declaration.Name != "fChaseDeltaMult:HAVOK" || ChaseMultiplier.Declaration.Collection != FalloutIniCollection.Main ||
            UpdateType.Number is not { } mode || !double.IsFinite(mode) || mode != Math.Truncate(mode) || mode < int.MinValue || mode > int.MaxValue ||
            ChaseMultiplier.Number is not { } chase || !float.IsFinite((float)chase) ||
            string.IsNullOrWhiteSpace(UpdateType.Origin) || string.IsNullOrWhiteSpace(ChaseMultiplier.Origin))
            throw new InvalidDataException("Renderer clock lost its actual typed INI copies.");
    }
}
