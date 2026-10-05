using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class OwnedTerminalProbe
{
    internal static void Run(string mod, string root, string game, FalloutFormKey reference, int entryIndex,
        string output, string[] dependencies)
    {
        var path = OutputPath(output, game, root, dependencies);
        using var content = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game).OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var placed = records.GetEffective(reference);
        if (placed.Signature != "REFR") throw new InvalidDataException("Owned terminal audit requires a placed REFR.");
        var basis = FalloutDialogueTopic.RequiredForm(placed, "NAME");
        var terminal = FalloutTerminal.Read(records, basis);
        if (entryIndex < 0 || entryIndex >= terminal.Entries.Count)
            throw new InvalidDataException("Owned terminal audit has no selected source ordinal.");
        var selected = terminal.Entries[entryIndex];
        selected.Program.RequireSourceExecution();
        // Build the exact selected compiled scope and parse its source. No
        // gameplay, world instance, condition result or engine effect is created.
        _ = new FalloutScriptBindings(records, placed, terminal.Record, selected.Program.Fields);
        _ = FalloutGameModeProgram.Read("begin Result\n" + selected.Program.Source + "\nend", "Result");
        var sources = new[] { placed, terminal.Record }.Concat(terminal.Entries
            .SelectMany(entry => entry.Program.References.Where(value => value.Form is not null)
                .Select(value => records.GetEffective(value.Form!.Value))))
            .Concat(terminal.Entries.SelectMany(entry => new[] { entry.Note, entry.Submenu })
                .Append(terminal.Password).Where(value => value is not null)
                .Select(value => records.GetEffective(value!.Value)))
            .DistinctBy(record => record.FormKey).ToArray();
        var before = sources.Select(Hash).ToArray();
        foreach (var entry in terminal.Entries)
            if (entry.Note is { } note) _ = FalloutNote.Read(records, note);
        if (sources.Where((record, index) => Hash(record) != before[index]).Any())
            throw new InvalidDataException("Owned terminal source changed during the read-only audit.");
        var result = new
        {
            schema = "opennv-owned-terminal-source/v1",
            timestampUtc = DateTime.UtcNow,
            runtimeMvid = typeof(FalloutTerminal).Module.ModuleVersionId,
            reference = reference.ToString(), terminal = basis.ToString(),
            sourceHash = terminal.SourceHash, entries = terminal.Entries.Count,
            selectedEntry = entryIndex, selectedFragment = selected.Program.Identity,
            difficulty = terminal.Difficulty, terminalFlags = terminal.Flags, serverType = terminal.ServerType,
            password = terminal.Password?.ToString(),
            menu = terminal.Entries.Select(entry => new
            {
                index = entry.Index, flags = entry.Flags,
                itemTextLength = entry.Text.Length, resultTextLength = entry.ResultText.Length,
                note = entry.Note?.ToString(), submenu = entry.Submenu?.ToString(),
                compiledSize = entry.Program.CompiledSize, locals = entry.Program.Locals.Count,
                references = entry.Program.References.Select(value => new
                { form = value.Form?.ToString(), localIndex = value.LocalIndex }).ToArray(),
                conditions = entry.Conditions.Select(condition => new
                {
                    condition.Function, condition.Flags, condition.Comparison, condition.RunOn,
                    rawArgument1 = condition.Argument1, rawArgument2 = condition.Argument2, rawReference = condition.Reference
                }).ToArray(), fragment = entry.Program.Identity
            }).ToArray(),
            sources = sources.Select((record, index) => new
            {
                identity = record.FormKey.ToString(), signature = record.Signature,
                winner = record.Plugin.Name, masters = record.Plugin.Masters, sha256 = before[index]
            }).ToArray(),
            sourceUnchanged = true, recording = false,
            boundary = "source-reader/selected-compiled-binding-and-parser-only; no-world/menu-input/unlock/script-effects/campaign/native-process/retail-oracle; ordinary-terminal-and-parity-unverified"
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            JsonSerializer.Serialize(stream, result, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine("OPENNV_OWNED_TERMINAL_SOURCE_PASS realReference=true orderedEntries=true " +
            "selectedCompiledScope=true sourceUnchanged=true gameplayAndParity=unverified recording=false");
    }

    internal static string OutputPath(string output, string game, string root, string[] dependencies)
    {
        var path = Path.GetFullPath(output);
        if (!Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase) || File.Exists(path) || Directory.Exists(path))
            throw new InvalidDataException("Owned terminal audit requires a fresh JSON result file.");
        foreach (var source in new[] { game, root }.Concat(dependencies))
        {
            var input = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
            if (path.Equals(input, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(input + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Owned terminal audit result cannot be inside a selected input root.");
        }
        return path;
    }

    private static string Hash(FalloutPluginRecord record) => Convert.ToHexString(SHA256.HashData(record.ReadData()));
}
