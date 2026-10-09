using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class OwnedCompiledScriptProbe
{
    internal static void Refusal(string mod, string root, string game, FalloutFormKey source, string scope,
        FalloutFormKey target, uint slot, string expectedError, string output, string[] dependencies)
    {
        var path = OwnedSpeechCompletionProbe.OutputPath(output, game, root, dependencies);
        if (slot == 0 || scope is not ("begin" or "end") || string.IsNullOrWhiteSpace(expectedError))
            throw new InvalidDataException("Compiled refusal proof requires a declared slot and exact expected divergence.");
        using var owned = new OwnedScope(mod, root, game, dependencies);
        var content = owned.Source;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var record = records.GetEffective(source); var info = FalloutDialogueTopic.Decode(record);
        var selected = FalloutScriptScope.Dialogue(record, scope == "begin");
        if (selected.Any(field => field.Signature == "SCTX") || !FalloutCompiledScriptProgram.HasInstructions(selected))
            throw new InvalidDataException("Owned refusal proof requires unchanged SCDA with source absent.");
        var program = FalloutCompiledScriptProgram.Read(record, selected, standalone: false);
        if (program.Instructions.Count != 1 || program.Instructions[0].Opcode != 0x15 ||
            !program.References.Any(reference => reference.Form == target))
            throw new InvalidDataException("Selected refusal proof is outside one retained numeric assignment.");
        var destination = records.GetEffective(target);
        var script = FalloutScriptLocals.AttachedScript(records, destination) ??
            throw new InvalidDataException("Selected refusal target has no original attached script.");
        var sources = new[] { record, destination, script }.Distinct().ToArray(); var hashes = sources.Select(Hash).ToArray();
        using var world = new FalloutReferenceWorld(records); var quests = new FalloutQuestState(records);
        if (destination.Signature is "REFR" or "ACHR" or "ACRE") _ = world.Get(target);
        var baselineInstances = world.InstanceCount;
        var originalReferences = world.Capture().ToArray();
        var beforeWorld = JsonSerializer.Serialize(originalReferences); var beforeQuests = JsonSerializer.Serialize(quests.Capture());
        var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
            _ => throw new InvalidDataException("Refused compiled assignment dispatched an effect.")));
        string? failure = null;
        try { scripts.ExecuteResult(info, records.RuntimeFormKey(0x14), scope == "begin"); }
        catch (Exception error) when (Failure(error)) { failure = error.Message; }
        if (failure is null || !failure.Contains(expectedError, StringComparison.Ordinal) ||
            beforeWorld != JsonSerializer.Serialize(originalReferences.Select(value => world.Retained(value.Reference).Capture()).ToArray()) ||
            beforeQuests != JsonSerializer.Serialize(quests.Capture()) ||
            world.InstanceCount != baselineInstances || world.ScriptManualSaves.EnteredInvocations != 0 ||
            sources.Where((value, index) => Hash(value) != hashes[index]).Any())
            throw new InvalidDataException("Owned compiled refusal lost its exact divergence, mutated state or concealed source drift.");
        var captureRefused = false;
        try { _ = world.Capture(); }
        catch (NotSupportedException error) when (error.Message.Contains("compiled result", StringComparison.OrdinalIgnoreCase))
        { captureRefused = true; }
        if (!captureRefused) throw new InvalidDataException("Failed compiled result concealed its unowned cold continuation.");
        var declarations = script.ReadSubrecords().Where(field => field.Signature == "SLSD").Select(field =>
        {
            if (field.Data.Length != 24) throw new InvalidDataException("Owned SLSD extent differs.");
            return new { index = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span), flags = field.Data.Span[16] };
        }).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, new
        {
            schema = "opennv-owned-compiled-refusal/v1", capturedUtc = DateTime.UtcNow,
            runtimeMvid = typeof(FalloutReferenceScripts).Module.ModuleVersionId, saveCompatibilityId = content.SaveCompatibilityId,
            originalSource = sources.Select((value, index) => new { form = value.FormKey.ToString(), value.Signature,
                winner = value.Plugin.Name, sourceSha256 = hashes[index] }),
            source = source.ToString(), scope, program.ScopeStart, program.CodeBytes, program.ProgramSha256,
            target = target.ToString(), slot, actualLocalDeclarations = declarations, expectedError, retainedDivergence = failure,
            baselineLogicalReferences = baselineInstances, nativeReferencesCreated = 0,
            sourceTextPresent = false, noStateOrRandomOrEffectsChanged = true, sourceUnchanged = true, coldCaptureRefused = true,
            boundary = "Real winning compiled/source local mismatch remains refused; no fabricated slot, native actor, campaign state or source-text fallback."
        }, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine($"OPENNV_OWNED_COMPILED_REFUSAL_PASS source={source} scope={scope} target={target} slot={slot} " +
            "SCTXAbsent=true exactDivergence=true noStateMutation=true inputAndNative=false recording=false");
    }

    internal static void Result(string mod, string root, string game, FalloutFormKey source, string scope,
        FalloutFormKey target, uint slot, double expected, string output, string[] dependencies)
    {
        var path = OwnedSpeechCompletionProbe.OutputPath(output, game, root, dependencies);
        if (!double.IsFinite(expected) || slot == 0 || scope is not ("begin" or "end"))
            throw new InvalidDataException("Compiled result audit requires a finite expected slot value and original INFO scope.");
        using var owned = new OwnedScope(mod, root, game, dependencies);
        var content = owned.Source;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var record = records.GetEffective(source);
        var info = FalloutDialogueTopic.Decode(record);
        var selected = FalloutScriptScope.Dialogue(record, scope == "begin");
        if (selected.Any(field => field.Signature == "SCTX") || !FalloutCompiledScriptProgram.HasInstructions(selected))
            throw new InvalidDataException("This owned proof requires unchanged nonempty SCDA with SCTX absent.");
        var program = FalloutCompiledScriptProgram.Read(record, selected, standalone: false);
        if (program.Instructions.Count != 1 || program.Instructions[0].Opcode != 0x15 ||
            !program.References.Any(reference => reference.Form == target))
            throw new InvalidDataException("Selected proof is not one original literal assignment to its retained form binding.");
        var destination = records.GetEffective(target);
        var localOwner = FalloutScriptLocals.AttachedScript(records, destination) ??
            throw new InvalidDataException("Selected result destination has no original local owner.");
        var sources = new[] { record, destination, localOwner, records.GetEffective(info.Quest) }.Distinct().ToArray();
        var hashes = sources.Select(Hash).ToArray();
        using var world = new FalloutReferenceWorld(records); var quests = new FalloutQuestState(records);
        var before = world.ReadVariable(quests, target, slot);
        var invocation = records.RuntimeFormKey(0x14);
        var effects = 0;
        var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
            _ => { ++effects; throw new InvalidDataException("Selected numeric compiled result reached an unrelated effect."); },
            Command: (_, _, _, _) => throw new InvalidDataException("Source-removed result used a source command.")));
        var receipt = scripts.ExecuteResultOwned(info, invocation, scope == "begin");
        var actual = world.ReadVariable(quests, target, slot);
        var references = Copy(world.Capture().ToArray()); var savedQuests = Copy(quests.Capture().ToArray());
        using var cold = new FalloutReferenceWorld(records); cold.Restore(references);
        var coldQuests = new FalloutQuestState(records); coldQuests.Restore(savedQuests);
        Copy(receipt).Require(selected, invocation);
        if (actual != expected || cold.ReadVariable(coldQuests, target, slot) != expected || effects != 0 ||
            world.ScriptManualSaves.EnteredInvocations != 0 || sources.Where((value, index) => Hash(value) != hashes[index]).Any())
            throw new InvalidDataException("Original source-removed assignment lost typed state/cold identity or changed source inputs.");
        world.ScriptManualSaves.RequireCapture();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, new
        {
            schema = "opennv-owned-compiled-result/v1", capturedUtc = DateTime.UtcNow,
            runtimeMvid = typeof(FalloutReferenceScripts).Module.ModuleVersionId,
            saveCompatibilityId = content.SaveCompatibilityId,
            originalSource = sources.Select((value, index) => new { form = value.FormKey.ToString(), value.Signature,
                winner = value.Plugin.Name, masters = value.Plugin.Masters, sourceSha256 = hashes[index] }),
            source = source.ToString(), scope, program.ScopeStart, program.CodeBytes, program.ProgramSha256,
            orderedReferences = program.References, sharedResultReceipt = receipt, target = target.ToString(), slot, before, expected, actual,
            infoQuest = info.Quest.ToString(), scriptLocalOwner = localOwner.FormKey.ToString(),
            sourceTextPresent = false, authoritativeInstructionsExecuted = 1,
            fixture = new { caller = "reserved PLAYER in a disposable C# result invocation", nativeAndInput = false,
                completeCampaignSaveWritten = false, rngAndAudioInvoked = false, exactColdState = true },
            sourceUnchanged = true, recording = false,
            boundary = "One unchanged authored compiled numeric assignment through shared C# locals; other opcodes, native effects, scheduler/extension instructions and campaign parity remain separate."
        }, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine($"OPENNV_OWNED_COMPILED_RESULT_PASS source={source} scope={scope} target={target} slot={slot} " +
            $"expected={expected.ToString("R", CultureInfo.InvariantCulture)} SCTXAbsent=true authoritativeSCDA=true exactCold=true inputAndNative=false recording=false");
    }

    internal static void Framing(string mod, string root, string game, string output, string[] dependencies)
    {
        var path = OwnedSpeechCompletionProbe.OutputPath(output, game, root, dependencies);
        using var owned = new OwnedScope(mod, root, game, dependencies);
        Framing(owned.Source, path);
    }

    internal static void FramingInstallation(string installation, string output, string[] options)
    {
        if (RuntimeLiveContentSource.Current is not null)
            throw new InvalidOperationException("Owned compiled framing cannot replace an active content owner.");
        var command = DevelopmentLabSource.ParseCommand(["compiled-framing", installation, "records", .. options]);
        using var content = DevelopmentLabSource.Open(installation, command.Selection);
        var path = OwnedSpeechCompletionProbe.OutputPath(output, installation, content.ContentRoots[0], content.ContentRoots.Skip(1).ToArray());
        Framing(content, path);
    }

    private static void Framing(RuntimeLiveContentSource content, string path)
    {
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var scopes = 0; var nonempty = 0; var noSource = 0; var framed = 0;
        var instructions = new Dictionary<ushort, int>(); var failures = new List<object>();
        foreach (var record in records.EffectiveRecords())
        {
            FalloutPluginSubrecord[] fields;
            try { fields = record.ReadSubrecords().ToArray(); }
            catch (Exception error) when (Failure(error))
            { failures.Add(new { lane = "record-reader", source = record.FormKey.ToString(), record.Signature, error = error.Message }); continue; }
            var selectedScopes = new List<FalloutScriptScope>();
            try
            {
                if (record.Signature == "SCPT") selectedScopes.Add(FalloutScriptScope.Standalone(record));
                else if (record.Signature == "INFO")
                {
                    selectedScopes.Add(FalloutScriptScope.Dialogue(record, true));
                    selectedScopes.Add(FalloutScriptScope.Dialogue(record, false));
                }
                else if (record.Signature == "QUST")
                {
                    var currentStage = -1;
                    for (var field = 0; field < fields.Length; ++field)
                    {
                        if (fields[field].Signature == "INDX") currentStage = field;
                        else if (fields[field].Signature == "QOBJ") currentStage = -1;
                        else if (fields[field].Signature == "QSDT")
                            selectedScopes.Add(FalloutScriptScope.QuestEntry(record, currentStage, field));
                    }
                }
            }
            catch (Exception error) when (Failure(error))
            { failures.Add(new { lane = "reader-owned-scope", source = record.FormKey.ToString(), record.Signature, error = error.Message }); }
            var covered = new HashSet<int>();
            foreach (var selected in selectedScopes)
            {
                foreach (var index in Enumerable.Range(selected.FieldStart, selected.FieldCount)) covered.Add(index);
                if (!selected.Any(field => field.Signature == "SCHR") && !selected.Compiled) continue;
                ++scopes;
                if (!selected.Compiled) continue;
                if (FalloutCompiledScriptProgram.HasInstructions(selected)) ++nonempty;
                if (!selected.Any(field => field.Signature == "SCTX")) ++noSource;
                try
                {
                    var program = FalloutCompiledScriptProgram.Read(record, selected, record.Signature == "SCPT");
                    foreach (var instruction in program.Instructions)
                        instructions[instruction.Opcode] = instructions.GetValueOrDefault(instruction.Opcode) + 1;
                    ++framed;
                }
                catch (Exception error) when (Failure(error))
                { failures.Add(new { lane = "compiled-layout-or-source-binding", source = record.FormKey.ToString(), record.Signature,
                    sourceFieldStart = selected.FieldStart, selected.Kind, selected.StageOrdinal, selected.ResultOrdinal, error = error.Message }); }
            }
            for (var index = 0; index < fields.Length; ++index)
                if ((fields[index].Signature is "SCHR" or "SCDA") && !covered.Contains(index))
                    failures.Add(new { lane = "unowned-original-result-family", source = record.FormKey.ToString(), record.Signature,
                        sourceFieldStart = index, error = "Original script field has no admitted canonical record result scope." });
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, new
        {
            schema = "opennv-owned-compiled-framing/v2", capturedUtc = DateTime.UtcNow,
            runtimeMvid = typeof(FalloutCompiledScriptProgram).Module.ModuleVersionId,
            saveCompatibilityId = content.SaveCompatibilityId, game = content.Game, stack = content.StackId,
            sourceContentRoots = content.ContentRoots, orderedPlugins = content.PluginSources.Select(source => source.Name).ToArray(),
            winningRecords = records.EffectiveRecordCount,
            scopes, nonempty, noSource, framed, failures,
            instructionOccurrences = instructions.OrderBy(pair => pair.Key).Select(pair => new { opcode = pair.Key.ToString("x4"), count = pair.Value }),
            readerOwnedRangesAndOrdinals = true, gameplayApplied = false, nativeLaunched = false, sourceExpressionsParsed = false,
            boundary = "Layout and unchanged source-scope binding only; instruction occurrence/declaration is not command support or campaign parity."
        }, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine($"OPENNV_OWNED_COMPILED_FRAMING_RESULT scopes={scopes} nonempty={nonempty} SCTXAbsent={noSource} framed={framed} failed={failures.Count} semanticExecution=false");
        if (failures.Count != 0) throw new InvalidDataException("Compiled layout audit retained an issue inventory at the selected output.");
    }

    private static bool Failure(Exception error) => error is IOException or InvalidDataException or InvalidOperationException or
        NotSupportedException or KeyNotFoundException or OverflowException;
    private sealed class OwnedScope : IDisposable
    {
        internal RuntimeLiveContentSource Source { get; }
        internal OwnedScope(string mod, string root, string game, string[] dependencies)
        {
            if (RuntimeLiveContentSource.Current is not null)
                throw new InvalidOperationException("Owned compiled audit cannot replace an active content owner.");
            var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game);
            try
            {
                RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
                    setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
                Source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned source is not configured.");
            }
            catch { RuntimeLiveContentSource.Clear(); throw; }
        }
        public void Dispose() => RuntimeLiveContentSource.Clear();
    }
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static string Hash(FalloutPluginRecord record) => Convert.ToHexString(SHA256.HashData(record.ReadData()));
}
