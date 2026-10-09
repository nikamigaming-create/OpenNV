using System.Text;
using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

// This is a bridge from the shared typed C# evaluator, never a second parser or
// a fallback from compiled bytes to SCTX. The caller retains its exact source
// scope and must supply the existing admitted argument evaluator. Unowned NVSE
// bytecode stays refused by that evaluator rather than accepted by this bridge.
internal static class FalloutNativePluginExpressionArguments
{
    internal static NativeNvseExpressionArgumentOwner Deferred(string sourceOwner, uint start, uint end,
        Func<IReadOnlyList<FalloutScriptValue>> evaluate) => new(sourceOwner, start, end,
            () => new(end, evaluate().Select(Convert).ToArray()));

    internal static NativeNvseExpressionValue Convert(FalloutScriptValue value) => value.Kind switch
    {
        FalloutScriptValueKind.Number => NativeNvseExpressionValue.Numeric(value.Number),
        FalloutScriptValueKind.Form => NativeNvseExpressionValue.Identity(NativeNvseTokenType.Form, checked((uint)value.Number)),
        FalloutScriptValueKind.Array => NativeNvseExpressionValue.Identity(NativeNvseTokenType.Array, checked((uint)value.Number)),
        FalloutScriptValueKind.String => NativeNvseExpressionValue.String(Encode(value.Text)),
        FalloutScriptValueKind.Pair => NativeNvseExpressionValue.Pair(Convert(value.Pair.Key), Convert(value.Pair.Value)),
        _ => throw new NotSupportedException("Shared script value has no native expression token owner."),
    };

    private static byte[] Encode(string text)
    {
        var encoding = CodePagesEncodingProvider.Instance.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)!;
        var bytes = encoding.GetBytes(text);
        if (!string.Equals(encoding.GetString(bytes), text, StringComparison.Ordinal))
            throw new InvalidDataException("Source string has no exact declared Windows-1252 native bytes.");
        return bytes;
    }
}
