using System.Buffers.Binary;
using System.Text.RegularExpressions;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutScriptPackage(FalloutFormKey Form, string EditorId, byte IdleFlags,
    float IdleTimer, IReadOnlyList<FalloutFormKey> Idles, IReadOnlyDictionary<string, FalloutFormKey?> Events)
{
    internal bool RunInSequence => (IdleFlags & 1) != 0;
    internal bool DoOnce => (IdleFlags & 4) != 0;
    internal byte Procedure { get; init; }
    internal int LocationType { get; init; }

    internal static FalloutScriptPackage Read(FalloutPluginRecord record)
    {
        if (record.Signature != "PACK") throw new InvalidDataException("Script package target is not PACK.");
        var fields = record.ReadSubrecords().ToArray();
        ReadOnlyMemory<byte> Required(string name, int size)
        {
            var found = fields.Where(field => field.Signature == name).ToArray();
            if (found.Length != 1 || found[0].Data.Length != size)
                throw new InvalidDataException($"PACK {record.FormKey} requires one {size}-byte {name}.");
            return found[0].Data;
        }
        var data = Required("PKDT", 12).Span;
        var location = Required("PLDT", 12).Span;
        var procedure = data[4];
        var locationType = BinaryPrimitives.ReadInt32LittleEndian(location);
        var idleFlags = fields.Any(field => field.Signature == "IDLF") ? Required("IDLF", 1).Span[0] : (byte)0;
        if ((idleFlags & ~5) != 0) throw new NotSupportedException($"PACK {record.FormKey} has unbound idle flags {idleFlags:x2}.");
        var idleCount = fields.Any(field => field.Signature == "IDLC") ? Required("IDLC", 1).Span[0] : 0;
        var timer = fields.Any(field => field.Signature == "IDLT") ? BinaryPrimitives.ReadSingleLittleEndian(Required("IDLT", 4).Span) : 0;
        if (!float.IsFinite(timer) || timer < 0) throw new InvalidDataException("Package idle timer is invalid.");
        var idles = new List<FalloutFormKey>();
        if (idleCount != 0 || fields.Any(field => field.Signature == "IDLA"))
        {
            var list = Required("IDLA", idleCount * 4).Span;
            for (var index = 0; index < idleCount; index++)
                idles.Add(record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(list[(index * 4)..])));
        }
        var events = new Dictionary<string, FalloutFormKey?>();
        string? currentEvent = null;
        foreach (var field in fields)
        {
            if (field.Signature is "POBA" or "POEA" or "POCA") currentEvent = field.Signature;
            else if (currentEvent is not null && field.Signature == "INAM")
            {
                if (field.Data.Length != 4 || !events.TryAdd(currentEvent,
                    record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span))))
                    throw new InvalidDataException("Package event has an invalid animation identity.");
            }
            else if (currentEvent is not null && field.Signature == "SCTX" &&
                FalloutDialogueTopic.CodeLines(FalloutDialogueTopic.Text(field.Data.Span)).Any())
                throw new NotSupportedException($"PACK {record.FormKey} event script execution is unbound.");
        }
        return new(record.FormKey, FalloutDialogueTopic.Text(fields.Single(field => field.Signature == "EDID").Data.Span),
            idleFlags, timer, idles, events)
        { Procedure = procedure, LocationType = locationType };
    }
}

internal sealed record FalloutScriptPackageCommand(int Line, string ActorEditorId, string? PackageEditorId);

internal static partial class FalloutScriptPackageCommands
{
    internal static IReadOnlyList<FalloutScriptPackageCommand> Read(string script)
    {
        var result = new List<FalloutScriptPackageCommand>();
        var depth = 0;
        var index = 0;
        foreach (var line in FalloutDialogueTopic.CodeLines(script))
        {
            if (Regex.IsMatch(line, @"^if\b", RegexOptions.IgnoreCase)) depth++;
            if (Regex.IsMatch(line, @"^endif\b", RegexOptions.IgnoreCase)) depth--;
            if (Regex.IsMatch(line, @"\b(addscriptpackage|removescriptpackage)\b", RegexOptions.IgnoreCase))
            {
                var match = Command().Match(line);
                if (!match.Success || depth != 0)
                    throw new NotSupportedException($"Script package command needs a bound expression/condition owner: {line}");
                var package = match.Groups["package"].Success ? match.Groups["package"].Value : null;
                if (match.Groups["operation"].Value.Equals("addscriptpackage", StringComparison.OrdinalIgnoreCase) != (package is not null))
                    throw new InvalidDataException("Add/RemoveScriptPackage has an invalid argument count.");
                result.Add(new(index, match.Groups["actor"].Value, package));
            }
            index++;
        }
        return result;
    }

    [GeneratedRegex(@"^(?<actor>[A-Za-z0-9_]+)\s*\.\s*(?<operation>addscriptpackage|removescriptpackage)(?:\s+(?<package>[A-Za-z0-9_]+))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Command();
}
