using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutBoundLookCommand(int Line, FalloutFormKey Actor, FalloutFormKey? Target);

internal static class FalloutHeadTrackingPrograms
{
    internal static bool RequiresProcess(FalloutPluginRecord reference, FalloutFormKey activeCell, bool hasProcess)
    {
        // Look is an Actor command. A dormant reference has no process to
        // receive its head target. Never treat a missing in-cell owner as a
        // dormant actor: that is an independent entity/lifecycle divergence.
        if (reference.Signature is not ("ACHR" or "ACRE")) return false;
        if (hasProcess) return true;
        var cell = FalloutCellSceneReader.ParentCell(reference) ??
            throw new NotSupportedException("Look actor has no admitted cell/process lifetime.");
        if (cell == activeCell)
            throw new NotSupportedException($"Look actor {reference.FormKey} is missing from its loaded cell.");
        return false;
    }

    internal static IReadOnlyList<FalloutBoundLookCommand> Bind(string source, FalloutScriptBindings bindings,
        FalloutFormKey? implicitActor = null) => FalloutLookCommands.Read(source).Select(command =>
        new FalloutBoundLookCommand(command.Line,
            command.Actor is { } actor ? bindings.Reference(actor) : implicitActor ??
                throw new NotSupportedException("Implicit Look caller has no reference-instance owner."),
            command.Target is { } target ? bindings.Reference(target) : null)).ToArray();

    internal static IReadOnlyList<FalloutBoundLookCommand> Stage(FalloutPluginStack records, FalloutOpeningControlStage stage)
    {
        if (FalloutLookCommands.Read(stage.Source).Count == 0) return [];
        var quest = records.GetEffective(stage.Quest);
        var fields = quest.ReadSubrecords().ToArray();
        var starts = fields.Select((field, index) => (field, index)).Where(value =>
            value.field.Signature == "INDX" && value.field.Data.Length == 2 &&
            BinaryPrimitives.ReadInt16LittleEndian(value.field.Data.Span) == stage.Stage).ToArray();
        if (quest.Signature != "QUST" || starts.Length != 1)
            throw new InvalidDataException("Head-tracking stage source identity is ambiguous.");
        var scope = fields.Skip(starts[0].index + 1).TakeWhile(field => field.Signature is not ("INDX" or "QOBJ")).ToArray();
        if (scope.Any(field => field.Signature == "CTDA"))
            throw new NotSupportedException("Conditional stage Look requires its result-program owner.");
        var sources = scope.Where(field => field.Signature == "SCTX").ToArray();
        if (sources.Length != 1 || FalloutOpeningPlayerControlResolver.ReadSource(quest, sources[0].Data.Span) != stage.Source)
            throw new NotSupportedException("Stage Look requires one matching source program.");
        ValidateReferences(scope);
        return Bind(stage.Source, new(records, quest, quest, scope));
    }

    internal static IReadOnlyList<FalloutBoundLookCommand> InfoEnd(FalloutPluginStack records,
        FalloutDialogueInfo info, FalloutFormKey? speaker)
    {
        if (FalloutLookCommands.Read(info.EndScript).Count == 0) return [];
        var fields = info.Record.ReadSubrecords().ToArray();
        var next = fields.Select((field, index) => (field, index)).Where(value => value.field.Signature == "NEXT").ToArray();
        if (next.Length != 1) throw new NotSupportedException("INFO Look requires one end-result scope.");
        var scope = fields.Skip(next[0].index + 1).ToArray();
        var sources = scope.Where(field => field.Signature == "SCTX").ToArray();
        if (sources.Length != 1 || FalloutDialogueTopic.ScriptText(sources[0].Data.Span) != info.EndScript)
            throw new NotSupportedException("INFO Look requires one matching end-result program.");
        ValidateReferences(scope);
        return Bind(info.EndScript, new(records, records.GetEffective(info.Quest), info.Record, scope), speaker);
    }

    private static void ValidateReferences(IReadOnlyList<FalloutPluginSubrecord> scope)
    {
        var headers = scope.Where(field => field.Signature == "SCHR").ToArray();
        var code = scope.Where(field => field.Signature == "SCDA").ToArray();
        if (headers.Length != 1 || headers[0].Data.Length != 20 || code.Length != 1 ||
            BinaryPrimitives.ReadUInt32LittleEndian(headers[0].Data.Span[4..]) != scope.Count(field => field.Signature is "SCRO" or "SCRV") ||
            BinaryPrimitives.ReadUInt32LittleEndian(headers[0].Data.Span[8..]) != code[0].Data.Length)
            throw new InvalidDataException("Look result program disagrees with its compiled extent/reference scope.");
    }
}
