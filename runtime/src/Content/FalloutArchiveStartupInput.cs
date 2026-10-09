using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutArchiveAuxiliaryInput(string ScopeSha256, string Filename, bool Present,
    long Bytes, string? Sha256);
internal sealed record FalloutArchiveAuxiliaryDeclaration(string Filename, string AppendSeparator, int AppendCapacity);
internal sealed record FalloutArchiveStartupInput(string ModesSha256, FalloutArchiveAuxiliaryInput Auxiliary,
    IReadOnlyList<string> Names)
{
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(JsonSerializer.Serialize(this));
    internal void Validate()
    {
        if (!FalloutAdvancementRuntimeReceipt.Digest(ModesSha256) || Auxiliary is null || Names is null ||
            !FalloutAdvancementRuntimeReceipt.Digest(Auxiliary.ScopeSha256) || string.IsNullOrEmpty(Auxiliary.Filename) ||
            Path.GetFileName(Auxiliary.Filename) != Auxiliary.Filename || Auxiliary.Bytes < 0 ||
            Auxiliary.Present != (Auxiliary.Sha256 is not null) || !Auxiliary.Present && Auxiliary.Bytes != 0 ||
            Auxiliary.Sha256 is { } sha && !FalloutAdvancementRuntimeReceipt.Digest(sha))
            throw new InvalidDataException("Archive startup lost the actual ordered setting/auxiliary-file observation.");
        foreach (var name in Names) RequireName(name);
    }
    internal static void RequireName(string name)
    {
        FalloutArchiveNameHash.RequireAscii(name);
        if (string.IsNullOrEmpty(name) || Path.GetFileName(name) != name || name.IndexOfAny(['\\', '/', ':', '*', '?']) >= 0)
            throw new NotSupportedException("Original archive registration requires an unowned source path namespace.");
    }

    internal static IReadOnlyList<string> ReadConfiguredNames(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Contains('\0')) throw new InvalidDataException("Archive list contains an embedded source terminator.");
        var names = new List<string>();
        foreach (var token in value.Split(','))
        {
            // CRT tokenization discards empty comma fields; source removes only
            // these leading bytes. Duplicate tokens invoke registration again.
            if (token.Length == 0) continue;
            var name = token.TrimStart(' ', '\t', '\n');
            RequireName(name); names.Add(name);
        }
        return Array.AsReadOnly(names.ToArray());
    }

    internal static IReadOnlyList<string> ReadAuxiliaryArchiveNames(ReadOnlySpan<byte> input)
    {
        // The original text-file reader folds CRLF and stops at CTRL-Z before
        // fgets' 259-byte payload limit. Each accepted extension is replaced
        // through its terminator, so the subsequent line suffix is discarded.
        var text = new List<byte>(input.Length);
        for (var at = 0; at < input.Length; at++)
        {
            var value = input[at];
            if (value == 0x1a) break;
            if (value == '\r' && at + 1 < input.Length && input[at + 1] == '\n') { text.Add((byte)'\n'); at++; }
            else text.Add(value);
        }
        var names = new List<string>();
        for (var at = 0; at < text.Count;)
        {
            var end = at;
            while (end < text.Count && end - at < 259) { if (text[end++] == '\n') break; }
            var lineBytes = text.GetRange(at, end - at).ToArray(); at = end;
            var terminator = Array.IndexOf(lineBytes, (byte)0);
            var line = Encoding.ASCII.GetString(lineBytes, 0, terminator < 0 ? lineBytes.Length : terminator);
            if (line.Length == 0 || line[0] is '#' or '\n') continue;
            if (lineBytes.Any(value => value > 0x7f))
                throw new NotSupportedException("Auxiliary archive startup requires its actual non-ASCII CRT/path owner.");
            var suffix = line.IndexOf(".esp", StringComparison.Ordinal);
            if (suffix < 0) suffix = line.IndexOf(".esm", StringComparison.Ordinal);
            if (suffix < 0) continue;
            var name = line[..suffix] + ".bsa";
            // Missing/invalid wildcard/absolute candidates need actual access
            // semantics; a managed existence probe cannot invent their result.
            RequireName(name); names.Add(name);
        }
        return Array.AsReadOnly(names.ToArray());
    }
}

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutArchiveAuxiliaryDeclaration ReadArchiveAuxiliaryDeclaration(string executable, string expectedSha256)
    {
        var bytes = File.ReadAllBytes(executable);
        if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != expectedSha256)
            throw new InvalidDataException("Archive auxiliary declaration changed its original selected image.");
        var (code, image) = Load(bytes); var declarations = new List<FalloutArchiveAuxiliaryDeclaration>();
        for (var at = 0; at + 24 <= code.Length; at++)
        {
            if (code[at] != 0x68 || code[at + 5] != 0x68 ||
                image.Literal(U32(code, at + 6)) != "%sPlugins.txt") continue;
            if (image.Literal(U32(code, at + 1)) != "")
                throw new NotSupportedException("Actual archive auxiliary prefix requires its independent source directory producer.");
            // Both reviewed compiler envelopes pass the actual path capacity,
            // address a local output buffer, and invoke the bounded formatter.
            var cursor = at + 10;
            if (code[cursor] == 0x68 && U32(code, cursor + 1) == 260)
            { cursor += 5; if (cursor + 7 > code.Length || code[cursor] != 0x8d || (code[cursor + 1] & 0xc7) != 0x85) continue; cursor += 6; }
            else if (code[cursor] == 0x8d && (code[cursor + 1] & 0xc7) == 0x45)
            { cursor += 3; if (cursor + 5 > code.Length || code[cursor] != 0x68 || U32(code, cursor + 1) != 260) continue; cursor += 5; }
            else continue;
            if (cursor + 6 > code.Length || code[cursor] is < 0x50 or > 0x57 || code[cursor + 1] != 0xe8) continue;
            var separators = new List<string>();
            for (var append = cursor + 6; append + 16 <= Math.Min(code.Length, at + 900); append++)
            {
                if (code[append] != 0x68 || code[append + 5] != 0x68 || U32(code, append + 6) != 260) continue;
                var separator = image.Literal(U32(code, append + 1));
                if (separator is null || separator.Count(character => character == ',') != 1 ||
                    separator.Any(character => character is not (',' or ' ' or '\t' or '\n'))) continue;
                var receiver = append + 10;
                if (code[receiver] == 0x8b && (code[receiver + 1] & 0xc7) == 0x85) receiver += 6;
                else if (code[receiver] == 0x8b && (code[receiver + 1] & 0xc7) == 0x45) receiver += 3;
                if (receiver + 6 > code.Length || code[receiver] is < 0x50 or > 0x57 || code[receiver + 1] != 0xe8) continue;
                separators.Add(separator);
            }
            if (separators.Count != 1) throw new NotSupportedException("Actual auxiliary startup has no unique bounded source append declaration.");
            declarations.Add(new(image.Literal(U32(code, at + 6))![2..], separators[0], 260));
        }
        if (declarations.Count != 1) throw new NotSupportedException("Original startup has no unique complete relative auxiliary-file declaration.");
        return declarations[0];
    }
}
