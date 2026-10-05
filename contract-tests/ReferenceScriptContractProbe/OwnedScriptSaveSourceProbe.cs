using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class OwnedScriptSaveSourceProbe
{
    internal static void Run(string game, string mod, string root, FalloutFormKey script,
        string output, string[] dependencies)
    {
        var destination = OwnedTerminalProbe.OutputPath(output, game, root, dependencies);
        using var content = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game).OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var source = records.GetEffective(script);
        if (source.Signature != "SCPT") throw new InvalidDataException("Owned save audit requires a winning SCPT source.");
        string Hash() => Convert.ToHexString(SHA256.HashData(source.ReadData())).ToLowerInvariant();
        var before = Hash();
        var fields = source.ReadSubrecords().ToArray();
        var texts = fields.Where(field => field.Signature == "SCTX").ToArray();
        if (texts.Length != 1) throw new NotSupportedException("Owned source-save audit requires one available source text.");
        var programs = FalloutGameModeProgram.ReadEvents(FalloutDialogueTopic.ScriptText(texts[0].Data.Span));
        var binding = new FalloutScriptBindings(records, source, source, fields);
        if (Hash() != before) throw new InvalidDataException("Owned source changed during source-save inspection.");
        var result = new
        {
            schema = "opennv-owned-script-save-source/v1", timestampUtc = DateTime.UtcNow,
            runtimeMvid = typeof(FalloutGameModeProgram).Module.ModuleVersionId,
            source = source.FormKey.ToString(), sourceHash = before,
            winner = source.Plugin.Name, masters = source.Plugin.Masters,
            binding = binding.Source.ToString(),
            blocks = programs.Select(block => new
            {
                sourceEvent = block.Event, block.Filter,
                programHash = block.Program.ProgramSha256,
                commands = block.Program.CommandNames.Distinct().ToArray()
            }).ToArray(),
            sourceUnchanged = true, recording = false,
            boundary = "winning-reader/compiled-scope/source-program-only; no world instance, event, effect, save, native process or campaign continuation; SCDA authority and retail timing unverified"
        };
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, result, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine("OPENNV_OWNED_SCRIPT_SAVE_SOURCE_PASS sourceBound=true compiledScope=true " +
            "orderedEventPrograms=true sourceUnchanged=true executionAndSaving=unverified recording=false");
    }
}
