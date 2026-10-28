namespace OpenNV.Runtime.Content;

internal enum FalloutMenuSourceEventKind { EnterSource, Token, LeaveSource }

internal sealed record FalloutMenuSourceEvent(FalloutMenuSourceEventKind Kind, string Source,
    FalloutMenuToken? Token = null);

// A source-qualified candidate event sequence. Retail include timing and the
// consumer's tile stack are separate contracts; this does not construct tiles.
internal static class FalloutMenuSourceProgram
{
    internal static IReadOnlyList<FalloutMenuSourceEvent> Read(string root, Func<string, byte[]> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        var result = new List<FalloutMenuSourceEvent>();
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Append(string path)
        {
            if (!active.Add(path)) throw new InvalidDataException($"Menu include cycle at {path}.");
            try
            {
                result.Add(new(FalloutMenuSourceEventKind.EnterSource, path));
                foreach (var token in FalloutMenuTokenStream.Read(read(path)))
                {
                    result.Add(new(FalloutMenuSourceEventKind.Token, path, token));
                    if (token.Kind != FalloutMenuTokenKind.Open ||
                        !token.Value.Equals("include", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!token.SelfClosing || token.Attributes is not { Count: 1 } ||
                        !token.Attributes.TryGetValue("src", out var name))
                        throw new NotSupportedException($"Menu include syntax is unowned at {path}:{token.Offset}.");
                    name = name.Replace('\\', '/');
                    if (name.Length == 0 || name.StartsWith('/') || name.Contains(':') ||
                        name.Split('/').Any(part => part is "" or "." or ".."))
                        throw new InvalidDataException($"Menu include path is invalid at {path}:{token.Offset}.");
                    Append("menus/prefabs/" + name);
                }
                result.Add(new(FalloutMenuSourceEventKind.LeaveSource, path));
            }
            finally { active.Remove(path); }
        }
        Append(root.Replace('\\', '/'));
        return result;
    }
}
