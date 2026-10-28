using System.Text;

namespace OpenNV.Runtime.Content;

internal enum FalloutMenuTokenKind { Open, Close, Text }

internal sealed record FalloutMenuToken(FalloutMenuTokenKind Kind, string Value, int Offset,
    IReadOnlyDictionary<string, string>? Attributes = null, bool SelfClosing = false);

// Source tokens retain order and offsets without requiring XML nesting. The
// consumer, include boundary and tile-close semantics need their own owner.
internal static class FalloutMenuTokenStream
{
    internal static IReadOnlyList<FalloutMenuToken> Read(ReadOnlySpan<byte> bytes)
    {
        var text = new UTF8Encoding(false, true).GetString(bytes);
        var result = new List<FalloutMenuToken>(); var index = 0;
        bool IsName(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or ':';
        void Space() { while (index < text.Length && char.IsWhiteSpace(text[index])) ++index; }
        string Name()
        {
            var start = index;
            if (index >= text.Length || !(char.IsAsciiLetter(text[index]) || text[index] == '_'))
                throw new InvalidDataException($"Menu name is invalid at offset {index}.");
            while (index < text.Length && IsName(text[index])) ++index;
            return text[start..index];
        }
        while (index < text.Length)
        {
            var start = index;
            if (text[index] != '<')
            {
                var end = text.IndexOf('<', index);
                index = end < 0 ? text.Length : end;
                result.Add(new(FalloutMenuTokenKind.Text, text[start..index], start));
                continue;
            }
            if (text.AsSpan(index).StartsWith("<!--", StringComparison.Ordinal))
            {
                var end = text.IndexOf("-->", index + 4, StringComparison.Ordinal);
                if (end < 0) throw new InvalidDataException($"Menu comment is incomplete at offset {index}.");
                index = end + 3; continue;
            }
            ++index; var closing = index < text.Length && text[index] == '/';
            if (closing) ++index;
            var name = Name(); Space();
            if (closing)
            {
                if (index >= text.Length || text[index++] != '>')
                    throw new InvalidDataException($"Menu closing tag is incomplete at offset {start}.");
                result.Add(new(FalloutMenuTokenKind.Close, name, start)); continue;
            }
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (index < text.Length && text[index] != '>' && text[index] != '/')
            {
                var attribute = Name(); Space();
                if (index >= text.Length || text[index++] != '=')
                    throw new InvalidDataException($"Menu attribute lacks a value at offset {index}.");
                Space();
                if (index >= text.Length || text[index] is not ('\'' or '"'))
                    throw new NotSupportedException($"Unquoted menu attribute at offset {index}.");
                var quote = text[index++]; var valueStart = index;
                var end = text.IndexOf(quote, index);
                if (end < 0) throw new InvalidDataException($"Menu attribute is incomplete at offset {valueStart}.");
                if (text.AsSpan(valueStart, end - valueStart).Contains('<'))
                    throw new InvalidDataException($"Menu attribute contains a tag delimiter at offset {valueStart}.");
                if (!attributes.TryAdd(attribute, text[valueStart..end]))
                    throw new InvalidDataException($"Duplicate menu attribute at offset {valueStart}.");
                index = end + 1;
                if (index < text.Length && text[index] is not ('>' or '/') && !char.IsWhiteSpace(text[index]))
                    throw new InvalidDataException($"Menu attributes lack a separator at offset {index}.");
                Space();
            }
            var selfClosing = index < text.Length && text[index] == '/';
            if (selfClosing) ++index;
            if (index >= text.Length || text[index++] != '>')
                throw new InvalidDataException($"Menu opening tag is incomplete at offset {start}.");
            result.Add(new(FalloutMenuTokenKind.Open, name, start, attributes, selfClosing));
        }
        return result;
    }
}
