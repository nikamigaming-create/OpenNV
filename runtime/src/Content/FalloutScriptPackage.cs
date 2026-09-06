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
    internal IReadOnlyDictionary<string, FalloutPackageEvent> EventPrograms { get; init; } =
        new Dictionary<string, FalloutPackageEvent>();

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
        var programs = new Dictionary<string, FalloutPackageEvent>();
        var eventFields = new List<FalloutPluginSubrecord>();
        string? currentEvent = null;
        void FinishEvent()
        {
            if (currentEvent is null) return;
            if (!programs.TryAdd(currentEvent, new(record, currentEvent, eventFields.ToArray())))
                throw new InvalidDataException("Package repeats an event declaration.");
            eventFields.Clear();
        }
        foreach (var field in fields)
        {
            if (field.Signature is "POBA" or "POEA" or "POCA")
            {
                FinishEvent();
                if (field.Data.Length != 0) throw new InvalidDataException("Package event marker is not empty.");
                currentEvent = field.Signature;
            }
            else if (currentEvent is not null && field.Signature == "INAM")
            {
                if (field.Data.Length != 4 || !events.TryAdd(currentEvent,
                    record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span))))
                    throw new InvalidDataException("Package event has an invalid animation identity.");
            }
            if (currentEvent is not null && field.Signature is not ("POBA" or "POEA" or "POCA"))
                eventFields.Add(field);
        }
        FinishEvent();
        return new(record.FormKey, FalloutDialogueTopic.Text(fields.Single(field => field.Signature == "EDID").Data.Span),
            idleFlags, timer, idles, events)
        { Procedure = procedure, LocationType = locationType, EventPrograms = programs };
    }
}

// An authored event is a declaration until its procedure reaches that event.
// Retain its own compiled-reference scope; later events cannot lend bindings.
internal sealed record FalloutPackageEvent(FalloutPluginRecord Package, string Kind,
    IReadOnlyList<FalloutPluginSubrecord> Fields)
{
    internal string Source
    {
        get
        {
            var source = Fields.Where(field => field.Signature == "SCTX").ToArray();
            if (source.Length > 1) throw new InvalidDataException("Package event repeats its source program.");
            return source.Length == 0 ? "" : FalloutDialogueTopic.ScriptText(source[0].Data.Span);
        }
    }

    internal void RequireEmptyScript()
    {
        var compiled = Fields.Where(field => field.Signature == "SCDA").ToArray();
        if (compiled.Length > 1) throw new InvalidDataException("Package event repeats its compiled program.");
        var headers = Fields.Where(field => field.Signature == "SCHR").ToArray();
        if (headers.Length > 1 || headers.Length == 1 && (headers[0].Data.Length != 20 ||
            BinaryPrimitives.ReadUInt32LittleEndian(headers[0].Data.Span[8..]) !=
                (compiled.Length == 0 ? 0 : compiled[0].Data.Length) ||
            BinaryPrimitives.ReadUInt32LittleEndian(headers[0].Data.Span[4..]) !=
                Fields.Count(field => field.Signature is "SCRO" or "SCRV")))
            throw new InvalidDataException("Package event compiled extents disagree with its header.");
        if (compiled.Length == 1 && compiled[0].Data.Length != 0 && !FalloutDialogueTopic.CodeLines(Source).Any())
            throw new NotSupportedException($"PACK {Package.FormKey} {Kind} compiled program has no source execution owner.");
        if (FalloutDialogueTopic.CodeLines(Source).Any())
            throw new NotSupportedException($"PACK {Package.FormKey} {Kind} event script execution is unbound.");
        foreach (var topic in Fields.Where(field => field.Signature == "TNAM"))
            if (topic.Data.Length != 4 || BinaryPrimitives.ReadUInt32LittleEndian(topic.Data.Span) != 0)
                throw new NotSupportedException($"PACK {Package.FormKey} {Kind} event topic execution is unbound.");
    }
}

internal sealed class FalloutPackageEvents(Action<FalloutScriptPackage, string> dispatch)
{
    internal FalloutScriptPackage? Active { get; private set; }
    internal bool Done { get; private set; }
    internal long Revision { get; private set; }
    internal string? LastEvent { get; private set; }
    internal FalloutFormKey? LastPackage { get; private set; }
    internal string? Error { get; private set; }

    internal void Change(FalloutScriptPackage? next)
    {
        RequireHealthy();
        if (Active?.Form == next?.Form) return;
        if (Active is { } previous) Publish(previous, "POCA");
        Active = next;
        Done = false;
        if (next is not null) Publish(next, "POBA");
    }

    internal void Complete()
    {
        RequireHealthy();
        if (Active is null || Done) return;
        Publish(Active, "POEA");
        Done = true;
    }

    private void RequireHealthy()
    {
        if (Error is not null) throw new NotSupportedException(Error);
    }

    private void Publish(FalloutScriptPackage package, string kind)
    {
        LastEvent = kind;
        LastPackage = package.Form;
        Revision++;
        try { dispatch(package, kind); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or FileNotFoundException)
        {
            Error = error.Message;
            throw;
        }
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
