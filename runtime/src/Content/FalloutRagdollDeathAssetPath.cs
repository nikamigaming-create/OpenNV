using System.Text;

namespace OpenNV.Runtime.Content;

internal static class FalloutRagdollDeathAssetPath
{
    internal const string Suffix = "Death.psa";
    private static readonly Encoding SourceEncoding = CodePagesEncodingProvider.Instance.GetEncoding(1252,
        EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)!;
    internal static string? Select(string bodyPartModel)
    {
        ArgumentNullException.ThrowIfNull(bodyPartModel);
        // The source bounded copy precedes its literal backslash search. A
        // forward slash does not select the append branch in that function.
        if (SourceEncoding.GetByteCount(bodyPartModel) >= 260)
            throw new NotSupportedException("source-ragdoll-model-path-bounded-copy-failure-consumer-unowned");
        var separator = bodyPartModel.LastIndexOf('\\');
        if (separator < 0) return null;
        var selected = bodyPartModel[..(separator + 1)] + Suffix;
        if (SourceEncoding.GetByteCount(selected) >= 260)
            throw new NotSupportedException("source-ragdoll-death-path-bounded-append-failure-consumer-unowned");
        return selected;
    }
}
