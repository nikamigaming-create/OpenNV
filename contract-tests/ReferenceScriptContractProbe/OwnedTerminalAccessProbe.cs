using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class OwnedTerminalAccessProbe
{
    internal static void Run(string mod, string modRoot, string gameRoot, string[] dependencies)
    {
        var setup = new FalloutModStackSelection([new(mod, modRoot, dependencies)]).Resolve(gameRoot);
        using var content = setup.OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var quest = FalloutDialogueTopic.Find(records, "QUST", "CG04");
        var fields = quest.ReadSubrecords().ToArray();
        var begin = Array.FindIndex(fields, field => field.Signature == "INDX" && field.Data.Span.SequenceEqual(new byte[2]));
        if (begin < 0) throw new InvalidDataException("Owned escape stage zero is absent.");
        var end = begin + 1;
        while (end < fields.Length && fields[end].Signature is not ("INDX" or "QOBJ")) ++end;
        using var world = new FalloutReferenceWorld(records);
        var commands = new List<(string Source, FalloutFormKey Reference, FalloutPluginSubrecord[] Entry)>();
        for (var index = begin + 1; index < end;)
        {
            if (fields[index].Signature != "QSDT") throw new InvalidDataException("Owned stage entry flags are absent.");
            var next = index + 1;
            while (next < end && fields[next].Signature != "QSDT") ++next;
            var scope = fields[index..next];
            var sourceField = scope.SingleOrDefault(field => field.Signature == "SCTX");
            if (!sourceField.Data.IsEmpty)
            {
                var source = FalloutDialogueTopic.ScriptText(sourceField.Data.Span);
                var bindings = new FalloutScriptBindings(records, quest, quest, scope);
                foreach (Match match in Regex.Matches(source, @"(?im)^\s*(\w+)\.lock\s+255\s*$"))
                {
                    var reference = bindings.Reference(match.Groups[1].Value);
                    if (records.GetEffective(world.Get(reference).Base).Signature == "TERM")
                        commands.Add((match.Value.Trim(), reference, scope));
                }
            }
            index = next;
        }
        if (commands.Count != 1) throw new InvalidDataException("Owned escape result has no unique original terminal lock.");
        var selected = commands[0];
        var terminal = records.GetEffective(world.Get(selected.Reference).Base);
        string Hash(FalloutPluginRecord record) => Convert.ToHexString(SHA256.HashData(record.ReadData()));
        var questHash = Hash(quest); var terminalHash = Hash(terminal);
        var initialLocked = world.GetLocked(selected.Reference); var initialLevel = world.GetLockLevel(selected.Reference);
        var executor = new FalloutReferenceScripts(records, world, new FalloutQuestState(records),
            new((_, _) => false, _ => throw new InvalidOperationException("Terminal Lock emitted a presentation effect.")));
        executor.ExecuteStage(quest, selected.Entry, selected.Source);
        if (world.GetLocked(selected.Reference) != 1 || world.GetLockLevel(selected.Reference) != 255)
            throw new InvalidDataException("Original owned terminal Lock255 did not reach the shared access owner.");
        var snapshots = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
        using var cold = new FalloutReferenceWorld(records); cold.Restore(snapshots);
        if (cold.GetLocked(selected.Reference) != 1 || cold.GetLockLevel(selected.Reference) != 255)
            throw new InvalidDataException("Cold owned terminal access lost its source result.");
        cold.UnlockReference(selected.Reference); cold.LockReference(selected.Reference);
        if (cold.GetLocked(selected.Reference) != 1 || cold.GetLockLevel(selected.Reference) != 255 ||
            Hash(quest) != questHash || Hash(terminal) != terminalHash)
            throw new InvalidDataException("Owned terminal relock or source identity changed.");
        Console.WriteLine(JsonSerializer.Serialize(new { schema = "opennv-owned-terminal-access/v1", quest = quest.FormKey.ToString(),
            reference = selected.Reference.ToString(), terminal = terminal.FormKey.ToString(), questHash, terminalHash,
            initialLocked, initialLevel, sourceCommand = true, cold = true, sourceUnchanged = true,
            boundary = "isolated-original-terminal-command; campaign-prefix-and-terminal-menu-unverified", recording = false }));
        Console.WriteLine("OPENNV_OWNED_TERMINAL_ACCESS_PASS originalLock255=true cold=true sourceUnchanged=true campaign=false framesRecorded=false");
    }
}
