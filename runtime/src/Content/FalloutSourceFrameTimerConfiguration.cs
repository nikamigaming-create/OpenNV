namespace OpenNV.Runtime.Content;

internal sealed record FalloutSourceFrameTimerConfiguration(FalloutSourceFrameTimer Source,
    string IniSource, FalloutIniDeclaration Declaration, bool ChangeRateSlowly, string ValueOrigin)
{
    internal static FalloutSourceFrameTimerConfiguration Read(FalloutAdvancementRuntimeReceipt receipt,
        FalloutNumericIniSettings settings)
    {
        var source = FalloutSourceFrameTimer.Read(receipt);
        var selected = settings.Find(FalloutIniCollection.Main, "bChangeTimeMultSlowly:General") ??
            throw new NotSupportedException("Source cached timer has no registered main INI rate-mode descriptor.");
        if (selected.Number is not (0 or 1))
            throw new InvalidDataException("Source cached timer INI mode has no Boolean byte representation.");
        var value = new FalloutSourceFrameTimerConfiguration(source, settings.Source, selected.Declaration,
            selected.Number == 1, selected.Origin);
        value.Validate(); return value;
    }
    internal void Validate()
    {
        Source.Validate();
        if (string.IsNullOrWhiteSpace(IniSource) || string.IsNullOrWhiteSpace(ValueOrigin) ||
            Declaration.Name != "bChangeTimeMultSlowly:General" || Declaration.Collection != FalloutIniCollection.Main ||
            Declaration.Payload != 1)
            throw new InvalidDataException("Source cached timer differs from its actual selected INI constructor.");
    }
}
