namespace OpenNV.Runtime.Content;

internal sealed record FalloutPlayerControlConfigurationSnapshot(string Source, string IniSource,
    FalloutIniValue InvertY, FalloutIniValue WorldFov)
{
    internal void Validate(FalloutPlayerMovementControlSource source, string stack)
    {
        source.Validate();
        if (Source != source.Identity || string.IsNullOrWhiteSpace(IniSource) ||
            !IniSource.StartsWith(stack + ":", StringComparison.Ordinal) ||
            !IniSource.EndsWith(":" + source.Main.EngineSha256, StringComparison.Ordinal) ||
            InvertY is not
            {
                Declaration:
                {
                    Name: FalloutPlayerControlConfiguration.InvertYName,
                    Collection: FalloutIniCollection.Prefs, Payload: 0
                }, Number: 0 or 1
            } ||
            string.IsNullOrWhiteSpace(InvertY.Origin) ||
            WorldFov is not
            {
                Declaration:
                {
                    Name: FalloutPlayerControlConfiguration.WorldFovName,
                    Collection: FalloutIniCollection.Main, Payload: FalloutPlayerControlConfiguration.WorldFovDefaultBits
                }, Number: { } fov
            } ||
            !double.IsFinite(fov) || (double)(float)fov != fov || string.IsNullOrWhiteSpace(WorldFov.Origin))
            throw new InvalidDataException("Player control configuration lost its exact typed collections, layered values or selected source.");
    }
}

// The executable association reader proves these exact typed descriptors at
// their actual control reads. Layering preserves the descriptor and provenance
// independently of the current payload. The world root's current FOV is a
// separate runtime field; this setting cannot stand in for that field.
internal sealed class FalloutPlayerControlConfiguration
{
    internal const string InvertYName = "bInvertYValues:Controls";
    internal const string WorldFovName = "fDefaultWorldFOV:Display";
    internal const uint WorldFovDefaultBits = 0x42960000;
    internal FalloutPlayerControlConfigurationSnapshot Fields { get; }

    internal FalloutPlayerControlConfiguration(FalloutPlayerMovementControlSource source, string stack,
        FalloutNumericIniSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Fields = new(source.Identity, settings.Source,
            settings.Find(FalloutIniCollection.Prefs, InvertYName) ??
                throw new NotSupportedException("source-Player-control-Prefs-invert-Y-declaration-unbound"),
            settings.Find(FalloutIniCollection.Main, WorldFovName) ??
                throw new NotSupportedException("source-Player-control-Main-default-world-FOV-declaration-unbound"));
        Fields.Validate(source, stack);
    }
    internal bool ReadInvertY() => Fields.InvertY.Number != 0;
    internal float ReadDefaultWorldFov() => (float)Fields.WorldFov.Number!.Value;
}
