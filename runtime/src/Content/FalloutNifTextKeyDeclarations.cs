namespace OpenNV.Runtime.Content;

internal enum FalloutNifTextKeyDeclarationKind
{
    EmptySegment,
    SequenceBoundary,
    Sound,
    StopSounds,
    Unbound
}

internal sealed record FalloutNifTextKeyDeclaration(
    int RawSegmentOrdinal,
    int Offset,
    int Length,
    string RawSegment,
    string Text,
    int? DispatchOrdinal,
    FalloutNifTextKeyDeclarationKind Kind,
    string? EditorId,
    string? Emitter);

/// <summary>
/// The NIF dispatch grammar only. This does not bind an emitter, sound,
/// sequence interval, playback event or native voice.
/// </summary>
internal static class FalloutNifTextKeyDeclarations
{
    internal const string SoundPrefix = "Sound:";
    internal const string StopPrefix = "Enum: StopSounds";

    internal static IReadOnlyList<FalloutNifTextKeyDeclaration> Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var declarations = new List<FalloutNifTextKeyDeclaration>();
        var start = 0;
        var dispatch = 0;
        for (var cursor = 0; cursor <= text.Length; cursor++)
        {
            if (cursor != text.Length && text[cursor] is not ('\r' or '\n')) continue;
            var raw = text[start..cursor];
            var value = raw.Trim();
            var kind = raw.Length == 0 ? FalloutNifTextKeyDeclarationKind.EmptySegment :
                value.Equals("start", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("end", StringComparison.OrdinalIgnoreCase)
                    ? FalloutNifTextKeyDeclarationKind.SequenceBoundary
                    : FalloutNifTextKeyDeclarationKind.Unbound;
            string? editor = null;
            string? emitter = null;
            // RemoveEmptyEntries precedes Trim in the original dispatch:
            // whitespace-only raw segments still dispatch an unbound key.
            int? dispatchOrdinal = raw.Length == 0 ? null : dispatch++;
            if (value.StartsWith(SoundPrefix, StringComparison.OrdinalIgnoreCase))
            {
                kind = FalloutNifTextKeyDeclarationKind.Sound;
                var payload = value[SoundPrefix.Length..].Trim();
                var separator = payload.IndexOfAny([' ', '\t']);
                editor = separator < 0 ? payload : payload[..separator];
                emitter = separator < 0 ? "" : payload[(separator + 1)..].Trim();
            }
            else if (value.Equals(StopPrefix, StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith(StopPrefix + " ", StringComparison.OrdinalIgnoreCase))
            {
                kind = FalloutNifTextKeyDeclarationKind.StopSounds;
                emitter = value[StopPrefix.Length..].Trim();
            }
            declarations.Add(new(declarations.Count, start, raw.Length, raw, value,
                dispatchOrdinal, kind, editor, emitter));
            start = cursor + 1;
        }
        return declarations.AsReadOnly();
    }
}
