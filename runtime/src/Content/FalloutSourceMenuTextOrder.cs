using System.Text;

namespace OpenNV.Runtime.Content;

// The Western source menu compares its encoded displayed row strings. Prepare
// each key once per list publication; comparisons allocate no string/byte copies.
internal static class FalloutSourceMenuTextOrder
{
    private static readonly Encoding Encoding = CodePagesEncodingProvider.Instance.GetEncoding(1252,
        EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)!;
    private sealed class ByteOrder : IComparer<byte[]>
    {
        internal static readonly ByteOrder Instance = new();
        public int Compare(byte[]? left, byte[]? right)
        {
            ArgumentNullException.ThrowIfNull(left); ArgumentNullException.ThrowIfNull(right);
            return left.AsSpan().SequenceCompareTo(right);
        }
    }
    internal static T[] Order<T>(IEnumerable<T> source, Func<T, string> label) => source
        .Select(value => (Value: value, Key: Encoding.GetBytes(label(value))))
        .OrderBy(entry => entry.Key, ByteOrder.Instance).Select(entry => entry.Value).ToArray();
}
