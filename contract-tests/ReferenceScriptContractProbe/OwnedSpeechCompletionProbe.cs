using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class OwnedSpeechCompletionProbe
{
    internal static void Run(string mod, string root, string game, FalloutFormKey speaker, string topicOperand,
        FalloutFormKey info, string local, double initial, double expected, FalloutFormKey destination,
        string output, string[] dependencies)
    {
        var path = OutputPath(output, game, root, dependencies);
        using var content = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game).OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var speakerRecord = records.GetEffective(speaker);
        var origin = FalloutCellSceneReader.ParentCell(speakerRecord) ?? throw new InvalidDataException("Speaker has no source cell.");
        if (destination == origin || !double.IsFinite(initial) || !double.IsFinite(expected) || initial == expected)
            throw new InvalidDataException("Selected completion fixture has no cell transition or local-state change.");
        world.LoadCell(FalloutCellSceneReader.Read(records, origin));
        var instance = world.Retained(speaker);
        var definition = instance.Script ?? throw new InvalidDataException("Selected speaker has no retained attached source script.");
        var program = definition.Record;
        var bindings = new FalloutScriptBindings(records, speakerRecord, program, program.ReadSubrecords());
        var topic = bindings.Form(topicOperand);
        if (topic.Signature != "DIAL" || !definition.Locals.TryGetValue(local, out var index))
            throw new InvalidDataException("Selected completion operand/local is absent from its attached source bindings.");
        var sources = new[] { speakerRecord, records.GetEffective(instance.Base), program, topic,
            records.GetEffective(info), records.GetEffective(origin), records.GetEffective(destination) };
        var hashes = sources.Select(Hash).ToArray();
        var before = instance.Read(index);
        // A disposable owner fixture, not a campaign save or a forced source
        // GameMode branch. The real source SayToDone must consume this local.
        instance.Write(index, initial);
        world.LoadCell(FalloutCellSceneReader.Read(records, destination)); world.UnloadCell(origin);
        var effects = 0; var deliveries = 0;
        var scripts = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false,
            _ => { ++effects; throw new NotSupportedException("Selected completion reached an unbound source effect."); }));
        var owner = new FalloutSpeechCompletionEvents();
        var receipt = new FalloutSpeechCompletionReceipt(speaker, new HashSet<FalloutFormKey> { topic.FormKey }, info, 1);
        FalloutReferenceScriptEventResult? delivered = null;
        owner.Complete(receipt, pending =>
        {
            delivered = scripts.DispatchSpeechCompletion(pending);
            if (delivered.Error is not null) throw new NotSupportedException(delivered.Error);
            ++deliveries;
        });
        var after = instance.Read(index);
        owner.Drain(_ => throw new InvalidDataException("Settled owned completion replayed."));
        var duplicateRefused = false;
        try { owner.Complete(receipt, _ => ++deliveries); }
        catch (InvalidOperationException) { duplicateRefused = true; }
        var ordinaryRefused = false;
        try { scripts.Dispatch(speaker, "GameMode"); }
        catch (InvalidOperationException) { ordinaryRefused = true; }
        if (after != expected || delivered is null || delivered.Blocks <= 0 || deliveries != 1 || owner.Active || owner.Error is not null ||
            !duplicateRefused || !ordinaryRefused || effects != 0 || world.IsResident(speaker) ||
            !ReferenceEquals(instance, world.Retained(speaker)))
            throw new InvalidDataException("Selected source completion did not settle the retained owner exactly once across unload.");
        var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
        using var cold = new FalloutReferenceWorld(records); cold.Restore(saved);
        if (cold.Retained(speaker).Read(index) != expected || cold.IsResident(speaker) ||
            sources.Where((record, sourceIndex) => Hash(record) != hashes[sourceIndex]).Any())
            throw new InvalidDataException("Selected completion lost cold source-bound locals or changed owned bytes.");
        var result = new
        {
            schema = "opennv-owned-speech-completion/v1",
            sourceContractTimestampUtc = DateTime.UtcNow,
            runtimeMvid = typeof(FalloutReferenceScripts).Module.ModuleVersionId,
            sources = sources.Select((record, sourceIndex) => new
            {
                identity = record.FormKey.ToString(), signature = record.Signature,
                winner = record.Plugin.Name, masters = record.Plugin.Masters, sourceSha256 = hashes[sourceIndex]
            }),
            speaker = speaker.ToString(), script = program.FormKey.ToString(), topic = topic.FormKey.ToString(), info = info.ToString(),
            generation = receipt.Generation, origin = origin.ToString(), destination = destination.ToString(),
            local, localIndex = index, beforeFixture = before, fixtureInitial = initial, after,
            matchingSourceBlocks = delivered.Blocks, deliveries, retainedInstance = true, sourceEffects = effects,
            duplicateRefused, ordinaryOffCellGameModeRefused = ordinaryRefused, coldLocals = true, sourceUnchanged = true,
            boundary = "disposable-original-source-event-owner; no-campaign-slot/input/audio/native-process/retail-oracle; full-cold-Continue-and-parity-unverified",
            recording = false
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            JsonSerializer.Serialize(stream, result, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine("OPENNV_OWNED_SPEECH_COMPLETION_PASS originalSource=true retainedOnly=true acrossUnload=true " +
            "sourceOnce=true ordinaryOffCellRefused=true coldLocals=true sourceUnchanged=true campaignAndParity=unverified recording=false");
    }

    internal static string OutputPath(string output, string game, string root, string[] dependencies)
    {
        var path = Path.GetFullPath(output);
        if (!Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase) || File.Exists(path) || Directory.Exists(path))
            throw new InvalidDataException("Owned speech audit requires a fresh JSON result file.");
        foreach (var source in new[] { game, root }.Concat(dependencies))
        {
            var input = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
            if (path.Equals(input, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(input + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Owned speech audit results cannot be written inside a selected input root.");
        }
        return path;
    }

    private static string Hash(FalloutPluginRecord record) => Convert.ToHexString(SHA256.HashData(record.ReadData()));
}
